#!/usr/bin/env python3
"""Build and sign the content manifest consumed by PCL managed servers.

The server's mods directory is the source of truth for mod files:

* ``name.jar`` is published to clients as ``mods/name.jar``.
* ``name.jar.disabled`` is server-disabled but is published to clients as
  ``mods/name.jar``.  This is how client-only mods are represented.

Non-mod files already present in the current manifest are retained.  A private
client override directory can add or replace explicitly managed client files.
Deployment-specific addresses and the signing key live in the local JSON
configuration, never in this source tree.
"""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tempfile
import time
from typing import Any, Iterable


ALLOWED_ROOTS = {
    "mods",
    "config",
    "defaultconfigs",
    "kubejs",
    "emotes",
    "resourcepacks",
    "shaderpacks",
}


class PublishError(RuntimeError):
    pass


def json_bytes(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def atomic_write(path: Path, data: bytes, mode: int = 0o644) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, mode)
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def sha256_file(path: Path) -> tuple[str, int]:
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
            size += len(chunk)
    return digest.hexdigest(), size


def normalized_relative(raw: str) -> str:
    path = PurePosixPath(raw.replace("\\", "/").lstrip("/"))
    if not path.parts or path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        raise PublishError(f"unsafe managed path: {raw!r}")
    if path.parts[0].lower() not in ALLOWED_ROOTS:
        raise PublishError(f"managed path uses a forbidden root: {raw!r}")
    return path.as_posix()


def source_snapshot(mods_root: Path) -> tuple[tuple[str, int, int], ...]:
    rows: list[tuple[str, int, int]] = []
    if not mods_root.is_dir():
        raise PublishError(f"mods directory does not exist: {mods_root}")
    for path in sorted(mods_root.iterdir(), key=lambda item: item.name.casefold()):
        if not path.is_file() or path.is_symlink():
            continue
        if path.name.endswith(".jar") or path.name.endswith(".jar.disabled"):
            stat = path.stat()
            rows.append((path.name, stat.st_size, stat.st_mtime_ns))
    return tuple(rows)


def wait_until_stable(mods_root: Path, seconds: float, timeout: float) -> None:
    if seconds <= 0:
        return
    deadline = time.monotonic() + timeout
    previous = source_snapshot(mods_root)
    while True:
        time.sleep(seconds)
        current = source_snapshot(mods_root)
        if current == previous:
            return
        if time.monotonic() >= deadline:
            raise PublishError("mods directory did not become stable before the timeout")
        previous = current


def sign_payload(payload: bytes, private_key: Path) -> dict[str, Any]:
    completed = subprocess.run(
        ["openssl", "dgst", "-sha256", "-sign", str(private_key)],
        input=payload,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        raise PublishError(f"OpenSSL signing failed: {completed.stderr.decode('utf-8', 'replace').strip()}")
    return {
        "schema": 1,
        "payload": base64.b64encode(payload).decode("ascii"),
        "signature": base64.b64encode(completed.stdout).decode("ascii"),
    }


def ensure_content_object(source: Path, repository: Path) -> dict[str, Any]:
    before = source.stat()
    digest, size = sha256_file(source)
    after = source.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise PublishError(f"source changed while hashing: {source}")

    relative_object = PurePosixPath("objects", digest[:2], digest)
    destination = repository.joinpath(*relative_object.parts)
    if destination.exists():
        existing_digest, existing_size = sha256_file(destination)
        if existing_digest != digest or existing_size != size:
            raise PublishError(f"content object is corrupt: {destination}")
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        descriptor, temporary_name = tempfile.mkstemp(prefix=f".{digest}.", dir=destination.parent)
        os.close(descriptor)
        temporary = Path(temporary_name)
        try:
            shutil.copyfile(source, temporary)
            copied_digest, copied_size = sha256_file(temporary)
            if copied_digest != digest or copied_size != size:
                raise PublishError(f"source changed while copying: {source}")
            os.chmod(temporary, 0o644)
            os.replace(temporary, destination)
        finally:
            if temporary.exists():
                temporary.unlink()
    return {"sha256": digest, "size": size, "url": relative_object.as_posix()}


def existing_non_mod_files(payload: dict[str, Any], repository: Path) -> dict[str, dict[str, Any]]:
    result: dict[str, dict[str, Any]] = {}
    for raw in payload.get("files", []):
        relative = normalized_relative(str(raw.get("path", "")))
        if relative.casefold().startswith("mods/"):
            continue
        url = PurePosixPath(str(raw.get("url", "")))
        if url.is_absolute() or ".." in url.parts or not url.parts or url.parts[0] != "objects":
            raise PublishError(f"existing file has an unsafe object URL: {relative}")
        object_path = repository.joinpath(*url.parts)
        if not object_path.is_file():
            raise PublishError(f"existing content object is missing: {object_path}")
        result[relative.casefold()] = {
            "path": relative,
            "sha256": str(raw["sha256"]).lower(),
            "size": int(raw["size"]),
            "url": url.as_posix(),
        }
    return result


def iter_override_files(root: Path) -> Iterable[tuple[str, Path]]:
    if not root.exists():
        return
    for path in sorted(root.rglob("*"), key=lambda item: item.as_posix().casefold()):
        if path.is_symlink():
            raise PublishError(f"client override may not contain symlinks: {path}")
        if not path.is_file():
            continue
        relative = normalized_relative(path.relative_to(root).as_posix())
        yield relative, path


def build_files(config: dict[str, Any], old_payload: dict[str, Any], write_objects: bool) -> list[dict[str, Any]]:
    server_root = Path(config["serverRoot"])
    repository = Path(config["repositoryRoot"])
    mods_root = server_root / "mods"
    excluded = {str(value).casefold() for value in config.get("excludedServerMods", [])}
    result = existing_non_mod_files(old_payload, repository)

    for source in sorted(mods_root.iterdir(), key=lambda item: item.name.casefold()):
        if not source.is_file() or source.is_symlink():
            continue
        source_name = source.name
        if source_name.casefold() in excluded:
            continue
        if source_name.endswith(".jar.disabled"):
            target_name = source_name[: -len(".disabled")]
        elif source_name.endswith(".jar"):
            target_name = source_name
        else:
            continue
        relative = normalized_relative(f"mods/{target_name}")
        key = relative.casefold()
        if key in result:
            raise PublishError(f"multiple sources map to the same client path: {relative}")
        content = ensure_content_object(source, repository) if write_objects else {
            "sha256": sha256_file(source)[0],
            "size": source.stat().st_size,
            "url": f"objects/<sha256>/{source.name}",
        }
        result[key] = {"path": relative, **content}

    override_root = Path(config["clientOverrideRoot"])
    for relative, source in iter_override_files(override_root):
        content = ensure_content_object(source, repository) if write_objects else {
            "sha256": sha256_file(source)[0],
            "size": source.stat().st_size,
            "url": f"objects/<sha256>/{source.name}",
        }
        result[relative.casefold()] = {"path": relative, **content}

    return sorted(result.values(), key=lambda entry: entry["path"].casefold())


def validate_config(config: dict[str, Any]) -> None:
    required = {
        "serverRoot",
        "repositoryRoot",
        "outputRoot",
        "backupRoot",
        "clientOverrideRoot",
        "privateKey",
        "manifestUrl",
        "serverAddress",
        "packId",
        "packVersion",
        "minecraft",
        "loader",
    }
    missing = sorted(required - config.keys())
    if missing:
        raise PublishError(f"configuration is missing: {', '.join(missing)}")
    if not str(config["manifestUrl"]).startswith("https://"):
        raise PublishError("manifestUrl must use HTTPS")
    if not isinstance(config["loader"], dict) or not config["loader"].get("type") or not config["loader"].get("version"):
        raise PublishError("loader must contain type and version")


def comparable_payload(payload: dict[str, Any]) -> dict[str, Any]:
    result = dict(payload)
    result.pop("publishedAt", None)
    result.pop("contentRevision", None)
    return result


def copy_backup(output_root: Path, backup_root: Path) -> None:
    existing = [output_root / name for name in ("manifest.payload.json", "manifest.json", "discovery.json")]
    existing = [path for path in existing if path.exists()]
    if not existing:
        return
    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d-%H%M%SZ")
    destination = backup_root / stamp
    destination.mkdir(parents=True, exist_ok=False)
    for path in existing:
        shutil.copy2(path, destination / path.name)


def publish(config: dict[str, Any], check_only: bool, force: bool) -> dict[str, Any]:
    validate_config(config)
    repository = Path(config["repositoryRoot"])
    output_root = Path(config["outputRoot"])
    old_payload_path = repository / "manifest.payload.json"
    if not old_payload_path.is_file():
        raise PublishError(f"seed manifest is missing: {old_payload_path}")
    old_payload = json.loads(old_payload_path.read_text(encoding="utf-8"))

    wait_until_stable(
        Path(config["serverRoot"]) / "mods",
        float(config.get("stabilitySeconds", 8)),
        float(config.get("stabilityTimeoutSeconds", 300)),
    )
    files = build_files(config, old_payload, write_objects=not check_only)
    now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)
    payload: dict[str, Any] = {
        "schema": 1,
        "packId": config["packId"],
        "packVersion": config["packVersion"],
        "contentRevision": now.strftime("%Y%m%d%H%M%S"),
        "publishedAt": now.isoformat().replace("+00:00", "Z"),
        "minecraft": config["minecraft"],
        "loader": config["loader"],
        "serverAddress": config["serverAddress"],
        "releaseNotesUrl": config.get("releaseNotesUrl", ""),
        "removeFiles": config.get("removeFiles", old_payload.get("removeFiles", [])),
        "files": files,
    }
    changed = comparable_payload(payload) != comparable_payload(old_payload)
    summary = {
        "changed": changed,
        "fileCount": len(files),
        "modCount": sum(entry["path"].casefold().startswith("mods/") for entry in files),
        "packVersion": payload["packVersion"],
        "contentRevision": payload["contentRevision"],
    }
    if check_only or (not changed and not force):
        return summary

    private_key = Path(config["privateKey"])
    if not private_key.is_file():
        raise PublishError(f"private signing key is missing: {private_key}")
    payload_bytes = json_bytes(payload)
    manifest = sign_payload(payload_bytes, private_key)
    discovery_payload = {
        "schema": 1,
        "kind": "pcl-managed-server",
        "packId": config["packId"],
        "manifestUrl": config["manifestUrl"],
        "serverAddress": config["serverAddress"],
    }
    discovery = sign_payload(json_bytes(discovery_payload), private_key)

    copy_backup(output_root, Path(config["backupRoot"]))
    atomic_write(output_root / "manifest.payload.json", json.dumps(payload, ensure_ascii=False, indent=2).encode("utf-8") + b"\n")
    atomic_write(output_root / "manifest.json", json.dumps(manifest, ensure_ascii=False, separators=(",", ":")).encode("utf-8") + b"\n")
    atomic_write(output_root / "discovery.json", json.dumps(discovery, ensure_ascii=False, separators=(",", ":")).encode("utf-8") + b"\n")
    return summary


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", default="/etc/fool-launcher-publisher.json")
    parser.add_argument("--check", action="store_true", help="validate and hash sources without publishing")
    parser.add_argument("--force", action="store_true", help="publish even if managed content is unchanged")
    args = parser.parse_args()
    try:
        config_path = Path(args.config)
        config = json.loads(config_path.read_text(encoding="utf-8"))
        lock_path = Path(config.get("lockFile", "/run/lock/fool-launcher-publisher.lock"))
        lock_path.parent.mkdir(parents=True, exist_ok=True)
        with lock_path.open("a+") as lock:
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX)
            summary = publish(config, args.check, args.force)
        print(json.dumps(summary, ensure_ascii=False, sort_keys=True))
        return 0
    except (OSError, ValueError, PublishError, subprocess.SubprocessError) as error:
        print(f"publisher error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

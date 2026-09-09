#!/usr/bin/env python3
"""Generate and RSA-sign a Fool Launcher manifest from AutoModpack content metadata."""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import fcntl
import hashlib
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path, PurePosixPath


FICLONE = 0x40049409
ALLOWED_TOP_LEVEL = {"mods", "config", "defaultconfigs", "kubejs", "emotes", "resourcepacks", "shaderpacks"}


def canonical_json(value: object) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def safe_relative_path(raw: str) -> str:
    normalized = raw.replace("\\", "/").lstrip("/")
    path = PurePosixPath(normalized)
    if not normalized or path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        raise ValueError(f"unsafe path in source manifest: {raw!r}")
    if path.parts[0] not in ALLOWED_TOP_LEVEL:
        raise ValueError(f"unsupported top-level directory in source manifest: {raw!r}")
    return path.as_posix()


def hash_file(path: Path) -> tuple[int, str, str]:
    sha256_digest = hashlib.sha256()
    sha1_digest = hashlib.sha1()
    size = 0
    with path.open("rb") as handle:
        while chunk := handle.read(1024 * 1024):
            size += len(chunk)
            sha256_digest.update(chunk)
            sha1_digest.update(chunk)
    return size, sha256_digest.hexdigest(), sha1_digest.hexdigest()


def clone_or_copy(source: Path, destination: Path) -> str:
    destination.parent.mkdir(parents=True, exist_ok=True)
    try:
        with source.open("rb") as src, destination.open("xb") as dst:
            fcntl.ioctl(dst.fileno(), FICLONE, src.fileno())
        return "clone"
    except OSError:
        destination.unlink(missing_ok=True)
        shutil.copyfile(source, destination)
        return "copy"


def find_source(relative: str, roots: list[Path]) -> Path:
    for root in roots:
        candidate = root / relative
        if candidate.is_file():
            return candidate
    joined = "\n  ".join(str(root / relative) for root in roots)
    raise FileNotFoundError(f"managed client file is missing: {relative}\n  {joined}")


def write_atomic(path: Path, content: bytes, mode: int = 0o644) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temp_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as handle:
            handle.write(content)
            handle.flush()
            os.fsync(handle.fileno())
        os.chmod(temp_name, mode)
        os.replace(temp_name, path)
    finally:
        if os.path.exists(temp_name):
            os.unlink(temp_name)


def sign(private_key: Path, payload: bytes) -> bytes:
    result = subprocess.run(
        ["openssl", "dgst", "-sha256", "-sign", str(private_key)],
        input=payload,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if result.returncode:
        raise RuntimeError(result.stderr.decode("utf-8", "replace").strip())
    return result.stdout


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--automodpack-content", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, action="append", required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--private-key", type=Path, required=True)
    parser.add_argument("--pack-version", required=True)
    parser.add_argument("--minecraft", required=True)
    parser.add_argument("--loader", choices=("forge", "neoforge", "fabric", "quilt"), required=True)
    parser.add_argument("--loader-version", required=True)
    parser.add_argument("--server-address", required=True)
    parser.add_argument("--manifest-url", required=True)
    parser.add_argument("--release-notes-url", default="")
    parser.add_argument(
        "--remove-file",
        action="append",
        default=[],
        metavar="PATH=SHA256",
        help="remove a legacy managed file only when its SHA-256 matches (repeatable)",
    )
    args = parser.parse_args()

    source_document = json.loads(args.automodpack_content.read_text(encoding="utf-8"))
    source_entries = source_document.get("list")
    if not isinstance(source_entries, list):
        raise ValueError("AutoModpack content document has no list array")

    roots = [root.resolve() for root in args.source_root]
    output_root = args.output_root.resolve()
    objects_root = output_root / "objects"
    objects_root.mkdir(parents=True, exist_ok=True)

    files: list[dict[str, object]] = []
    object_stats = {"existing": 0, "clone": 0, "copy": 0}
    seen_paths: set[str] = set()
    for source_entry in source_entries:
        relative = safe_relative_path(str(source_entry["file"]))
        if relative in seen_paths:
            raise ValueError(f"duplicate managed path: {relative}")
        seen_paths.add(relative)
        source = find_source(relative, roots)
        size, sha256, sha1 = hash_file(source)
        declared_size = int(source_entry["size"])
        if size != declared_size:
            raise ValueError(f"size mismatch for {relative}: AutoModpack={declared_size}, actual={size}")
        declared_sha1 = str(source_entry.get("sha1", "")).lower()
        if declared_sha1 and sha1 != declared_sha1:
            raise ValueError(f"SHA-1 mismatch for {relative}: AutoModpack={declared_sha1}, actual={sha1}")

        object_path = objects_root / sha256[:2] / sha256
        if object_path.exists():
            object_size, object_hash, _ = hash_file(object_path)
            if object_size != size or object_hash != sha256:
                raise ValueError(f"corrupt existing object: {object_path}")
            object_stats["existing"] += 1
        else:
            method = clone_or_copy(source, object_path)
            object_stats[method] += 1
            os.chmod(object_path, 0o644)
        files.append({
            "path": relative,
            "size": size,
            "sha256": sha256,
            "url": f"objects/{sha256[:2]}/{sha256}",
        })

    files.sort(key=lambda entry: str(entry["path"]).casefold())
    remove_files: list[dict[str, object]] = []
    for removal in args.remove_file:
        try:
            raw_path, sha256 = removal.rsplit("=", 1)
        except ValueError as exc:
            raise ValueError(f"invalid --remove-file value: {removal!r}") from exc
        relative = safe_relative_path(raw_path)
        sha256 = sha256.lower()
        if len(sha256) != 64 or any(character not in "0123456789abcdef" for character in sha256):
            raise ValueError(f"invalid removal SHA-256: {sha256!r}")
        if relative in seen_paths:
            raise ValueError(f"path cannot be both downloaded and removed: {relative}")
        remove_files.append({"path": relative, "sha256": sha256})
    remove_files.sort(key=lambda entry: str(entry["path"]).casefold())
    payload = {
        "schema": 1,
        "packId": "the-fool",
        "packVersion": args.pack_version,
        "minecraft": args.minecraft,
        "loader": {"type": args.loader, "version": args.loader_version},
        "serverAddress": args.server_address,
        "publishedAt": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "releaseNotesUrl": args.release_notes_url,
        "files": files,
        "removeFiles": remove_files,
    }
    payload_bytes = canonical_json(payload)
    signature = sign(args.private_key, payload_bytes)
    envelope = {
        "schema": 1,
        "payload": base64.b64encode(payload_bytes).decode("ascii"),
        "signature": base64.b64encode(signature).decode("ascii"),
    }
    envelope_bytes = canonical_json(envelope) + b"\n"
    discovery_payload = {
        "schema": 1,
        "kind": "pcl-managed-server",
        "packId": "the-fool",
        "manifestUrl": args.manifest_url,
        "serverAddress": args.server_address,
    }
    discovery_payload_bytes = canonical_json(discovery_payload)
    discovery_envelope = {
        "schema": 1,
        "payload": base64.b64encode(discovery_payload_bytes).decode("ascii"),
        "signature": base64.b64encode(sign(args.private_key, discovery_payload_bytes)).decode("ascii"),
    }
    discovery_envelope_bytes = canonical_json(discovery_envelope) + b"\n"
    payload_pretty = json.dumps(payload, ensure_ascii=False, sort_keys=True, indent=2).encode("utf-8") + b"\n"
    marker = {
        "schema": 1,
        "packId": "the-fool",
        "channel": "stable",
        "confirmed": False,
        "manifestUrl": args.manifest_url,
        "serverAddress": args.server_address,
    }
    marker_bytes = json.dumps(marker, ensure_ascii=False, sort_keys=True, indent=2).encode("utf-8") + b"\n"

    write_atomic(output_root / "manifest.json", envelope_bytes)
    write_atomic(output_root / "discovery.json", discovery_envelope_bytes)
    write_atomic(output_root / "manifest.payload.json", payload_pretty)
    write_atomic(output_root / "bootstrap" / ".fool-managed.json", marker_bytes)
    summary = {
        "files": len(files),
        "bytes": sum(int(entry["size"]) for entry in files),
        "payloadSha256": hashlib.sha256(payload_bytes).hexdigest(),
        "objectWrites": object_stats,
        "manifest": str(output_root / "manifest.json"),
        "discovery": str(output_root / "discovery.json"),
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

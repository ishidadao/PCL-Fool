#!/usr/bin/env python3

import base64
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest


MODULE_PATH = Path(__file__).with_name("fool_update_publisher.py")
SPEC = importlib.util.spec_from_file_location("fool_update_publisher", MODULE_PATH)
PUBLISHER = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(PUBLISHER)


class PublisherTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.server = self.root / "server"
        self.repository = self.root / "repository"
        self.output = self.root / "output"
        self.overrides = self.root / "overrides"
        self.backups = self.root / "backups"
        (self.server / "mods").mkdir(parents=True)
        (self.repository / "objects").mkdir(parents=True)
        self.overrides.mkdir()

        config_bytes = b"client-setting"
        digest = __import__("hashlib").sha256(config_bytes).hexdigest()
        object_path = self.repository / "objects" / digest[:2] / digest
        object_path.parent.mkdir(parents=True)
        object_path.write_bytes(config_bytes)
        seed = {
            "schema": 1,
            "packId": "test-pack",
            "packVersion": "1.0",
            "publishedAt": "2026-01-01T00:00:00Z",
            "minecraft": "1.20.1",
            "loader": {"type": "forge", "version": "47.4.0"},
            "serverAddress": "example.invalid:25565",
            "removeFiles": [],
            "files": [{
                "path": "config/client.json",
                "sha256": digest,
                "size": len(config_bytes),
                "url": f"objects/{digest[:2]}/{digest}",
            }],
        }
        (self.repository / "manifest.payload.json").write_text(json.dumps(seed), encoding="utf-8")
        self.key = self.root / "signing.pem"
        subprocess.run(
            ["openssl", "genpkey", "-quiet", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", self.key],
            check=True,
        )
        self.config = {
            "serverRoot": str(self.server),
            "repositoryRoot": str(self.repository),
            "outputRoot": str(self.output),
            "backupRoot": str(self.backups),
            "clientOverrideRoot": str(self.overrides),
            "privateKey": str(self.key),
            "manifestUrl": "https://example.invalid/manifest.json",
            "serverAddress": "example.invalid:25565",
            "packId": "test-pack",
            "packVersion": "1.0",
            "minecraft": "1.20.1",
            "loader": {"type": "forge", "version": "47.4.0"},
            "excludedServerMods": ["server-only.jar"],
            "stabilitySeconds": 0,
        }

    def tearDown(self):
        self.temporary.cleanup()

    def test_disabled_mod_is_enabled_on_client_and_server_only_is_excluded(self):
        (self.server / "mods" / "shared.jar").write_bytes(b"shared")
        (self.server / "mods" / "graphics.jar.disabled").write_bytes(b"graphics")
        (self.server / "mods" / "server-only.jar").write_bytes(b"server")
        summary = PUBLISHER.publish(self.config, check_only=False, force=True)
        self.assertEqual(summary["modCount"], 2)

        payload = json.loads((self.output / "manifest.payload.json").read_text(encoding="utf-8"))
        paths = {entry["path"] for entry in payload["files"]}
        self.assertIn("mods/shared.jar", paths)
        self.assertIn("mods/graphics.jar", paths)
        self.assertNotIn("mods/graphics.jar.disabled", paths)
        self.assertNotIn("mods/server-only.jar", paths)

    def test_manifest_signature_verifies(self):
        (self.server / "mods" / "graphics.jar.disabled").write_bytes(b"graphics")
        PUBLISHER.publish(self.config, check_only=False, force=True)
        envelope = json.loads((self.output / "manifest.json").read_text(encoding="utf-8"))
        payload = base64.b64decode(envelope["payload"])
        signature = base64.b64decode(envelope["signature"])
        public_key = self.root / "public.pem"
        subprocess.run(["openssl", "pkey", "-in", self.key, "-pubout", "-out", public_key], check=True)
        payload_path = self.root / "payload.bin"
        signature_path = self.root / "signature.bin"
        payload_path.write_bytes(payload)
        signature_path.write_bytes(signature)
        verified = subprocess.run(
            ["openssl", "dgst", "-sha256", "-verify", public_key, "-signature", signature_path, payload_path],
            capture_output=True,
            text=True,
        )
        self.assertEqual(verified.returncode, 0, verified.stderr)

    def test_unchanged_staged_output_is_not_republished(self):
        (self.server / "mods" / "graphics.jar.disabled").write_bytes(b"graphics")
        first = PUBLISHER.publish(self.config, check_only=False, force=True)
        second = PUBLISHER.publish(self.config, check_only=False, force=False)
        self.assertTrue(first["changed"])
        self.assertFalse(second["changed"])

    def test_duplicate_enabled_and_disabled_sources_are_rejected(self):
        (self.server / "mods" / "same.jar").write_bytes(b"enabled")
        (self.server / "mods" / "same.jar.disabled").write_bytes(b"disabled")
        with self.assertRaises(PUBLISHER.PublishError):
            PUBLISHER.publish(self.config, check_only=True, force=False)


if __name__ == "__main__":
    unittest.main()

# Fool Launcher server tools

`generate_manifest.py` converts the already-tested AutoModpack client content list into the signed discovery document and manifest consumed by the customized PCL launcher.

Security properties:

- only explicitly allowed client directories are published;
- each source is checked against AutoModpack's tested size and SHA-1 before publication;
- every file is addressed and verified by SHA-256;
- the manifest payload is signed with RSA/SHA-256;
- the private key stays outside this repository;
- published objects are immutable snapshots (CoW clone when supported, ordinary copy otherwise).

Production generation example:

```bash
sudo -n python3 FoolServerTools/generate_manifest.py \
  --automodpack-content /mnt/Data/Minecraft/fool/automodpack/host-modpack/automodpack-content.json \
  --source-root /mnt/Data/Minecraft/fool/automodpack/host-modpack/main \
  --source-root /mnt/Data/Minecraft/fool/incoming/The-Fool-Server-0.2.2 \
  --source-root /mnt/Data/Minecraft/fool \
  --output-root /mnt/Data/Minecraft/fool/launcher-repository \
  --private-key /mnt/Data/Minecraft/fool-launcher-signing/private.pem \
  --pack-version 0.2.2 \
  --minecraft 1.20.1 \
  --loader forge \
  --loader-version 47.4.0 \
  --server-address managed-server.example.invalid:25565 \
  --manifest-url https://managed-server.example.invalid:4443/fool/manifest.json \
  --release-notes-url https://bbsmc.net/modpack/the-fool/changelog \
  --remove-file mods/automodpack-mc1.20.1-forge-4.0.6.jar=a51323b7ea435e122a8ab854b2ac89887ed09a5a626e71bfa27969e7b22b060a
```

The generated `bootstrap/.fool-managed.json` must be placed in the isolated Minecraft instance root. The launcher never guesses a managed instance from its folder name.

Publish the generated `discovery.json` at `https://<the player-entered server host>:4443/.well-known/pcl-managed.json`. The discovery document is signed and returns both the manifest URL and the actual Minecraft connection address. Do not publish `manifest.payload.json` in the final web configuration. It is generated for administrator inspection only; clients consume `discovery.json` and `manifest.json`.

`--remove-file` is intentionally hash-gated. The launcher backs up and removes that path only if its current SHA-256 exactly matches the signed value; a same-named but modified file blocks the launch instead of being deleted.

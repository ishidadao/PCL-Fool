# Fool managed-client manifest v1

When the player marks a saved server as providing automatic updates, the launcher derives only the discovery endpoint from the entered Minecraft host:

`https://<entered-host>:4443/.well-known/pcl-managed.json`

The endpoint returns a signed envelope whose payload declares `kind: pcl-managed-server`, the pack ID, the manifest URL, and the actual Minecraft connection address. The launcher verifies this envelope with its pinned public key before following the manifest URL. The manifest is independently signed and must declare the same connection address. After synchronization, PCL connects to that signed address.

Servers not marked as managed skip discovery entirely and use ordinary direct connection. A managed server cannot silently downgrade to ordinary mode: an unavailable or invalid discovery response blocks the launch.

The download endpoint returns a JSON envelope containing:

- `schema`: envelope schema version;
- `payload`: Base64-encoded UTF-8 canonical JSON;
- `signature`: Base64 RSA/SHA-256 signature over the exact payload bytes.

The signed payload declares the pack identity, pack version, Minecraft version, loader and loader version, direct-connect server address, and the complete managed file set.

Managed paths are restricted to `mods`, `config`, `defaultconfigs`, `kubejs`, `emotes`, `resourcepacks`, and `shaderpacks`. User data such as `saves`, `screenshots`, `options.txt`, `servers.dat`, logs, accounts, and launcher settings are outside the managed set.

The launcher records only files from the last successfully applied manifest. On a later update it may remove an obsolete file only if that path was present in the previous managed state. Unrelated user-added files are not deleted.

Every launch computes SHA-256 for the managed files. File size alone is never treated as proof that a client file matches the signed manifest.

The top level of `mods` is strict: an active `.jar` absent from the signed client manifest is copied into the timestamped recovery backup and removed from the active folder. Disabled files such as `.jar.disabled` are left alone. Other managed roots are not purged merely because they contain an extra user file.

The optional signed `removeFiles` array is for one-time migration away from a known legacy updater. Every entry includes a path and SHA-256. A matching file is backed up and removed; a hash mismatch fails closed and is never deleted.

When Minecraft or the mod loader changes, v1 fails closed before launch. A later migration implementation will create a new PCL instance instead of overwriting an existing major-version instance.

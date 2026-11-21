# Clonedivers Forge Linux 0.9.1

> **Published alongside Windows `v2.0.1`:** Linux Forge `0.9.1` ([linux-v0.9.1](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.9.1)) is published alongside live Windows [v2.0.1](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.0.1) and [`forge-mods-2026-11-20`](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-mods-2026-11-20).

## Hotfix Summary (`build25480438-r6`)

- Upgrades embedded gameplay catalog from `build25480438-r5` to **`build25480438-r6`** (`14` downloads, `8,298,489` bytes), shipping `clonedivers.lego-compact-leaf-writer 0.2.0` and `clonedivers.empire-atst-writer 0.2.0` to eliminate the Difficulty 11 / high-unit-count 1 FPS lock storm and diagnostic `.bin` log-budget exhaustion (`fatal reason=3`).
- Existing `0.9.0` installations automatically upgrade enabled `0.1.0` LEGO and AT-ST leaf writers to `0.2.0` while preserving disabled/removed optional choices.
- Package checksums (`SHA256SUMS`):
  - `Clonedivers-linux-x64.tar.gz` (`67,027,200` B): `087b5df401da4429e3ce147a051c34ad5731cac4aaa11b147687bf4d81025305`
  - `Clonedivers-linux-x64.deb` (`60,909,662` B): `65cbec5b4b6f7476efbc4db8d8ccc4b1bb76deb2bf082c514b9ef433b44c97a5`
  - `clonedivers-forge-channel.json` (`373` B): `adacd000e638cbcd2e0417d1fb634b6fc5d7953d0f351c570c06da6e71c4b6a2`
- Verified across all 3 Ubuntu `linux-package-qa` matrix jobs (`22.04`, `24.04`, `26.04`) including `0.8.0 -> 0.9.1` portable overlay and `.deb` `apt` upgrades with preserved fixture bytes. See [v2.0.1](v2.0.1.md).

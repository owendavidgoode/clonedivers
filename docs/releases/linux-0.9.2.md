# Clonedivers Forge Linux 0.9.2

> **Published alongside Windows `v2.0.2`:** Linux Forge `0.9.2` ([linux-v0.9.2](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.9.2)) is published alongside live Windows [v2.0.2](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.0.2) and [`forge-mods-2026-11-21`](https://github.com/owendavidgoode/clonedivers/releases/tag/forge-mods-2026-11-21).

## Summary

- **Unified Avalonia 11 UI (PR #32):** Shares the redesigned single-window dark Star Wars launcher, slide-over Customize drawer with live cache/download-size preview, non-blocking update banner, and Settings/Diagnostics modal with Windows.
- **Gameplay Catalog `build25480438-r7` (PR #31):** Ships D11-verified `lego-compact-leaf-writer 0.2.1` and `empire-atst-writer 0.2.1`; existing installs auto-upgrade enabled `0.1.0`/`0.2.0` writers.
- **Linux Fixes (PR #30, PR #32):** Aligns slow first-launch CLI timeout with the 10-minute `play` budget (`2 min -> 9 min` in `OfficialCommands.cs`), adds byte/MB/s/ETA progress to the 440 MB Linux installer download, and includes Issue #28 Linux Steam VDF user caching and startup shim probe fixes.
- **Package checksums (`SHA256SUMS`):**
  - `Clonedivers-linux-x64.tar.gz` (`67,109,305` B): `d5760c2d5afb2cf229cd135ab87547a616b16315ea2db26c19e1f24accb4e2ba`
  - `Clonedivers-linux-x64.deb` (`60,965,242` B): `740959b76d44490d211dc235c777d748306b70a5cf483f361d8ed18c1919d6b7`
  - `clonedivers-forge-channel.json` (`373` B): `fdd907d702c074d2e22f70c25c1f5cb0a26df4830c08642116f0e7200f822e00`
- Verified across all 3 Ubuntu `linux-package-qa` matrix jobs (`22.04`, `24.04`, `26.04`) including `0.8.0 -> 0.9.2` portable overlay and `.deb` `apt` upgrades with preserved fixture bytes and headless GUI smoke PNG verification. See [v2.0.2](v2.0.2.md).

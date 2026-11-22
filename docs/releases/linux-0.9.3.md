# Clonedivers Forge Linux 0.9.3

> **Published alongside Windows `v2.0.3`:** Linux Forge `0.9.3` ([linux-v0.9.3](https://github.com/owendavidgoode/clonedivers/releases/tag/linux-v0.9.3)) is published alongside live Windows [v2.0.3](https://github.com/owendavidgoode/clonedivers/releases/tag/v2.0.3).

## Summary

- **Deterministic Linux Download & Install Progress (PR #33):** Replaces indeterminate string progress in `LinuxForgeHost.PrepareAsync` with real file counts, GB, MB/s, ETA, and cancellation parity with Windows.
- **Cross-Platform Icon, Hotkeys & UI Parity:** Embeds an explicit 256×256 Clone helmet `WindowIcon` & `TrayIcon` for X11/Wayland docks and Alt-Tab, adds `Escape`/`Enter` modal keyboard shortcuts, visible UI error toasts, radically simplified full-variety defaults with collapsed **Advanced** drawer + Host Gameplay Controls, 5-tier Linux GPU/RAM hardware scanner, 1-click **Apply** & **Verify & Repair**, Disk Usage cleaner, Squad Match Hash (`r7-xxxx`), clickable **What's New**, UI sound effects (`paplay`/`aplay` fallback), and Discord Rich Presence IPC.
- **Package checksums (`SHA256SUMS`):**
  - `Clonedivers-linux-x64.tar.gz` (`67,235,667` B): `0397885b85da8f4fa61a3ab955fd1b061192cbe3725d1d1d6864ac145cb7bc16`
  - `Clonedivers-linux-x64.deb` (`61,123,924` B): `1154298bb272b269b8c2c2084e35918c3b3741f7ae911789aa7b06bdc4ff8322`
  - `clonedivers-forge-channel.json` (`373` B): `9ddcbeddb63f9d81609b875d3ce68e91982020d8e710d4e3e1f60c2549ca4041`
- Verified across all 3 Ubuntu `linux-package-qa` matrix jobs (`22.04`, `24.04`, `26.04`) on `main @ 3d04ab1` ([run 44979447304](https://github.com/owendavidgoode/clonedivers/actions/runs/44979447304)). See [v2.0.3](v2.0.3.md).

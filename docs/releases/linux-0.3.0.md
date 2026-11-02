The Linux launcher now opens a **real desktop window** by default.

- Helldivers, Clonedivers and EmpireDivers mode cards with the original helmet artwork.
- Pack options, download-size confirmation, progress and cancellation.
- Native folder picker plus paste-and-confirm folder selection.
- Steam Launch button, repair and interrupted-update recovery.
- Save-report dialog and optional recording setup/controls.
- Ubuntu/Debian installer with an Applications-menu entry and icon.
- Portable packages with an **Add app shortcut** button.

**Ubuntu PC:** download **Clonedivers-linux-x64.deb**, open it with your software
installer, install, then open **Clonedivers** from Applications. If a local graphical
package installer is unavailable, use `sudo apt install ~/Downloads/Clonedivers-linux-x64.deb`
once. Normal launcher use does not require a terminal.

For other distributions, use the x64 tar archive and open the `clonedivers` executable.
ARM64 packages are also provided; ARM game compatibility is not established. The
launcher includes .NET. Keep using the existing pack and settings; no pack redownload
is needed when the same bytes are already installed/cached.

[Setup, recording and optional terminal commands](https://github.com/owendavidgoode/clonedivers/blob/linux-v0.3.0/docs/LINUX.md).

The terminal menu is now optional (`--terminal`). Existing scripting commands remain
available. Actual modded Steam/Proton gameplay and driver-specific capture still need
player testing; Snap MangoHud Vulkan capture remains experimental.

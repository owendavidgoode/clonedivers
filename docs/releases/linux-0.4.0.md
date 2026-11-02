Both Linux interfaces are included: the **native desktop window** and an improved
**keyboard terminal dashboard**.

- Open **Clonedivers** from Applications, or run `clonedivers`, for the desktop GUI.
- Run `clonedivers --tui` for the terminal UI. `--terminal` also works.
- Navigate with arrows and Enter; toggle checkboxes with Space. Save options or
  discard edits with Esc.
- See the installed universe, game state, recovery status, options and game folder.
- Track download progress, bytes, speed and ETA. Ctrl+C cancels preparation/downloads
  back to the dashboard, keeping partial files for resuming.
- Change the game folder even when automatic discovery fails; quoted home paths work.
- Read support/recording results with wrapped paths and page navigation.
- Headless interactive sessions choose the terminal UI automatically. `NO_COLOR=1`
  disables colors; `--plain` or `TERM=dumb` uses numbered menus.

**Ubuntu/Debian:** download **Clonedivers-linux-x64.deb**, install it and open
**Clonedivers** from Applications. Other distributions can use the portable tar
archive. Both packages include .NET. ARM64 builds are provided; ARM game compatibility
is not established. Existing pack files/settings are reused.

Direct scripting commands, repair/recovery, local support reports and optional
MangoHud recording remain available in both interfaces.

QA exercises actual pseudo-terminals, option save/discard, headless startup and
Ctrl+C followed by HTTP Range resume, plus the native desktop controls and packaged
executable on Ubuntu 22.04 and 24.04. Actual modded Steam/Proton gameplay still needs
player testing; driver-specific recording and Snap MangoHud Vulkan capture retain
their documented limits.

[Setup and controls](https://github.com/owendavidgoode/clonedivers/blob/linux-v0.4.0/docs/LINUX.md).

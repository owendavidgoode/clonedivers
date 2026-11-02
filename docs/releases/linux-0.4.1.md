Launch now selects the **Steam installation that owns your chosen game library** in
both the desktop window and terminal interface.

Previously, Launch used the desktop's default `steam://` handler. On a machine with
multiple Steam packages, that could open another client and trigger its initial
client download/update.

- Native Steam uses its existing `steam.sh`; Snap and Flatpak launch their own app.
- External Steam libraries and Steam's symlink aliases are matched to their owner.
- Unregistered or ambiguous libraries show an explanation instead of guessing. You
  can still launch Helldivers 2 from your usual Steam; the installed mods apply.
- Status output, launch messages and local support reports identify Steam routing.
- Clonedivers does not install Steam or change the desktop's default handler. Steam
  can still download its own normal updates when it starts.

The native desktop GUI and keyboard terminal dashboard are both included. Upgrade
with the `.deb`, or replace the portable launcher while it is closed. Existing
settings, installed pack files and cached downloads are preserved.

QA covers native/Snap/Flatpak routing with a deliberately wrong default handler,
external libraries, symlinks, ambiguous ownership, actual CLI and desktop Launch
actions, and existing install/repair/recovery/recording flows. Source and packaged
executables are checked on Ubuntu 22.04 and 24.04. Actual Steam/Proton gameplay and
driver-specific recording still need player testing.

[Setup and controls](https://github.com/owendavidgoode/clonedivers/blob/linux-v0.4.1/docs/LINUX.md).

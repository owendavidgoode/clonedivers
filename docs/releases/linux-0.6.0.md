# Clonedivers Linux 0.6.0 — Forge migration

This x86-64 Ubuntu/Debian package replaces the old Linux launcher with Forge as
the default. Existing0.5 launchers show the0.6 release notice; install the full
.deb or extract the entire portable TAR.GZ. A notice does not automatically
install or overwrite your application. Keep the old package/directory to roll back.

The complete native launcher, bridge0.3.8 and pinned GPL source scripts install
together. The application/menu entry stays Clonedivers. User settings/accounts
remain outside the package and are preserved; first-time Forge setup carries
available legacy appearance choices. Forge update notices use a separate cache
and require the explicit Forge channel marker.

Follow [migration and setup](https://github.com/owendavidgoode/clonedivers/blob/main/docs/FORGE-MIGRATION.md).
Every player needs their own Steam-owned HD2, separate official pinned HellForge
installation, dedicated Proton prefix, supported namespace/sandbox and permitted
matching mods. No game files, accounts, third-party mod archives or private
Heavy/Squad repairs are bundled. Linux account capture, Proton gameplay and
Windows–Linux multiplayer still require real-machine acceptance. ARM64, Deck and
Flatpak/Snap Forge are not qualified.

The official-HD2 button opens Steam directly. --steam is retained only for
legacy recovery/one-time cleanup. No live Steam game file is changed by installing
this package. SHA256SUMS covers both packages and the channel marker; companion
metadata verifies its Windows helper and five complete GPL source scripts.

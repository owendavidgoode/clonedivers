# Clonedivers Forge for Linux

The current Ubuntu/Debian x86-64 launcher is **0.6.1**, with Forge as its default.
Use the complete .deb or portable archive, including the adjacent forge/ companion.

See [migration, package installation and first Forge setup](FORGE-MIGRATION.md).
The existing Linux launcher shows a release notice; installation is a manual or
package-manager step. The new .deb replaces the existing application/menu entry
without changing user configuration or accounts.

A separate owned HellForge0.3.0-beta/engine0.1.41/game25480438 installation in its
own Proton prefix and official namespace is required. No game/third-party mods
are bundled. Actual Linux account capture, Proton gameplay and Windows–Linux
multiplayer remain unqualified. Missing or incompatible dependencies block play.

Run `clonedivers --gui` for Regular/Clones/Empire. `clonedivers --help` describes
read-only terminal diagnostics. Use Steam for the official online game. Explicit
`--steam` opens legacy recovery mode; [the archived instructions](LEGACY-LINUX.md)
apply only to that mode. ARM64, Deck and Flatpak/Snap Forge are not qualified.

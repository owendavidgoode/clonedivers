#!/usr/bin/env bash
set -euo pipefail
binary=$(realpath "${1:?binary}")
arch=${2:?architecture}
version=${3:?version}
output=$(realpath -m "${4:?output .deb}")
repo=$(cd "$(dirname "$0")/.." && pwd)
case "$arch" in x64) deb_arch=amd64;; arm64) deb_arch=arm64;; *) exit 64;; esac
package_root=$(mktemp -d)
mkdir -p "$package_root/DEBIAN" "$package_root/usr/bin" "$package_root/usr/share/applications" "$package_root/usr/share/icons/hicolor/scalable/apps" "$package_root/usr/share/doc/clonedivers"
install -m 755 "$binary" "$package_root/usr/bin/clonedivers"
install -m 644 "$repo/Clonedivers.Linux/clonedivers.svg" "$package_root/usr/share/icons/hicolor/scalable/apps/clonedivers.svg"
install -m 644 "$repo/LICENSE" "$package_root/usr/share/doc/clonedivers/copyright"
install -m 644 "$repo/docs/LEGACY-LINUX.md" "$package_root/usr/share/doc/clonedivers/README.md"
install -m 644 "$repo/Clonedivers.Linux/THIRD-PARTY-NOTICES.txt" "$package_root/usr/share/doc/clonedivers/THIRD-PARTY-NOTICES.txt"
cat > "$package_root/DEBIAN/control" <<EOF
Package: clonedivers
Version: $version
Architecture: $deb_arch
Maintainer: Clonedivers project
Section: games
Priority: optional
Depends: libc6 (>= 2.35), libgcc-s1, libstdc++6, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, xdg-utils, libicu70 | libicu72 | libicu74 | libicu76 | libicu78 | libicu80
Homepage: https://github.com/owendavidgoode/clonedivers
Description: Clonedivers graphical Steam launcher
 Choose Helldivers, Clonedivers or EmpireDivers, install and repair the pack,
 and launch through Steam. Includes local reports and optional session recording.
EOF
cat > "$package_root/usr/share/applications/clonedivers.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Clonedivers
Comment=Choose your universe and launch through Steam
Exec=/usr/bin/clonedivers --gui
Icon=clonedivers
Terminal=false
Categories=Game;
StartupWMClass=Clonedivers
EOF
dpkg-deb --root-owner-group --build "$package_root" "$output"

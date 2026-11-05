#!/usr/bin/env bash
set -euo pipefail
stage=$(realpath "${1:?complete staging directory}")
version=${2:?version}
output=$(realpath -m "${3:?output deb}")
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && -x "$stage/clonedivers" && -f "$stage/forge/companion.json" && ! -e "$output" ]] || exit 64
if find "$stage" -type l -print -quit | grep -q .; then echo 'Linked package source refused' >&2; exit 64; fi
repo=$(cd "$(dirname "$0")/.." && pwd)
package_root=$(mktemp -d)
mkdir -p "$package_root/DEBIAN" "$package_root/usr/bin" "$package_root/usr/lib/clonedivers" "$package_root/usr/share/applications" "$package_root/usr/share/icons/hicolor/scalable/apps"
cp -a "$stage/." "$package_root/usr/lib/clonedivers/"
find "$package_root/usr/lib/clonedivers" -type d -exec chmod 755 {} +
find "$package_root/usr/lib/clonedivers" -type f -exec chmod 644 {} +
chmod 755 "$package_root/usr/lib/clonedivers/clonedivers" "$package_root"/usr/lib/clonedivers/forge/linux/*.sh
cat > "$package_root/usr/bin/clonedivers" <<'EOF'
#!/bin/sh
exec /usr/lib/clonedivers/clonedivers "$@"
EOF
chmod 755 "$package_root/usr/bin/clonedivers"
install -m 644 "$repo/Clonedivers.Linux/clonedivers.svg" "$package_root/usr/share/icons/hicolor/scalable/apps/clonedivers.svg"
cat > "$package_root/DEBIAN/control" <<EOF
Package: clonedivers
Version: $version
Architecture: amd64
Maintainer: Clonedivers project
Section: games
Priority: optional
Depends: libc6 (>= 2.35), libgcc-s1, libstdc++6, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, xdg-utils, libicu70 | libicu72 | libicu74 | libicu76 | libicu78 | libicu80
Homepage: https://github.com/owendavidgoode/clonedivers
Description: Clonedivers Forge launcher
 Regular, Clones and Empire appearances in a separate HellForge installation.
 Includes the native launcher and isolated Windows bridge, not game or mod files.
EOF
cat > "$package_root/usr/share/applications/clonedivers.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Clonedivers
Comment=Choose your universe in HellForge
Exec=/usr/bin/clonedivers --gui
Icon=clonedivers
Terminal=false
Categories=Game;
StartupWMClass=Clonedivers
EOF
dpkg-deb --root-owner-group --build "$package_root" "$output"

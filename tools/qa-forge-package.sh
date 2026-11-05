#!/usr/bin/env bash
set -euo pipefail
artifacts=$(realpath "${1:?artifacts directory}")
cd "$artifacts"
sha256sum --check SHA256SUMS
archive=Clonedivers-linux-x64.tar.gz
deb=Clonedivers-linux-x64.deb
tar -tzf "$archive" > /tmp/clonedivers-forge-archive-paths-$$
if grep -Eq '(^/|(^|/)\.\.(/|$))' /tmp/clonedivers-forge-archive-paths-$$; then echo 'Unsafe archive path' >&2; exit 1; fi
extract=$(mktemp -d)
tar -xzf "$archive" -C "$extract"
if find "$extract" -type l -print -quit | grep -q .; then echo 'Unexpected archive link' >&2; exit 1; fi
[[ -x "$extract/clonedivers" && -f "$extract/forge/linux/COPYING" && -f "$extract/preset.json" ]]
[[ $(jq -r .bridgeVersion "$extract/forge/companion.json") == 0.3.9 ]]
[[ $(jq -r .gameBuild "$extract/forge/companion.json") == 25480438 ]]
[[ $(sha256sum "$extract/forge/HellForge.Launcher.ClonediversBridge.exe" | cut -d' ' -f1) == $(jq -r .bridgeSha256 "$extract/forge/companion.json") ]]
for name in hellforge-linux.sh hellforge-offline.sh hf-inner.sh hf-bridge-host.sh allow-namespace.sh; do
  [[ -x "$extract/forge/linux/$name" ]]
  expected=$(jq -r --arg name "$name" '.Scripts[] | select(.Name==$name) | .BundleSha256' "$extract/forge/linux/bundle.json")
  [[ $(sha256sum "$extract/forge/linux/$name" | cut -d' ' -f1) == "$expected" ]]
done
"$extract/clonedivers" --version | grep -F 'Clonedivers Forge Linux 0.6.2'
"$extract/clonedivers" --help | grep -F 'Clonedivers Forge for Linux'
"$extract/clonedivers" --steam --help > /dev/null
[[ $(dpkg-deb -f "$deb" Package) == clonedivers && $(dpkg-deb -f "$deb" Version) == 0.6.2 ]]
sudo apt-get update -qq
sudo apt-get install -y "$artifacts/$deb"
clonedivers --version | grep -F 'Clonedivers Forge Linux 0.6.2'
clonedivers --help | grep -F 'Clonedivers Forge for Linux'
test -f /usr/lib/clonedivers/forge/companion.json
test -f /usr/lib/clonedivers/forge/linux/COPYING
echo 'Forge portable/deb install, default routing, companion hashes and recovery help passed; no game/account/Proton session launched.'

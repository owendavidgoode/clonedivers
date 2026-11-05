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
[[ -x "$extract/clonedivers" && -f "$extract/forge/LICENSE-CLONEDIVERS.txt" && -f "$extract/preset.json" ]]
[[ $(jq -r .bridgeVersion "$extract/forge/companion.json") == 0.4.0 ]]
[[ $(jq -r .gameBuild "$extract/forge/companion.json") == 25480438 ]]
[[ $(jq -r .control "$extract/forge/companion.json") == native-linux ]]
[[ $(sha256sum "$extract/forge/HellForge.Launcher.ClonediversBridge" | cut -d' ' -f1) == $(jq -r .bridgeSha256 "$extract/forge/companion.json") ]]
"$extract/forge/HellForge.Launcher.ClonediversBridge" --help >/dev/null
"$extract/clonedivers" --version | grep -F 'Clonedivers Forge Linux 0.7.0'
"$extract/clonedivers" --help | grep -F 'Clonedivers Forge for Linux'
"$extract/clonedivers" --steam --help > /dev/null
[[ $(dpkg-deb -f "$deb" Package) == clonedivers && $(dpkg-deb -f "$deb" Version) == 0.7.0 ]]
sudo apt-get update -qq
sudo apt-get install -y "$artifacts/$deb"
clonedivers --version | grep -F 'Clonedivers Forge Linux 0.7.0'
clonedivers --help | grep -F 'Clonedivers Forge for Linux'
test -f /usr/lib/clonedivers/forge/companion.json
test -x /usr/lib/clonedivers/forge/HellForge.Launcher.ClonediversBridge
if apt-cache show passt >/dev/null 2>&1; then command -v pasta; else echo 'This distribution lacks passt: real co-op stays blocked by the launcher preflight.'; fi
command -v python3
echo 'Forge portable/deb install, default routing, companion hashes and recovery help passed; no game/account/Proton session launched.'

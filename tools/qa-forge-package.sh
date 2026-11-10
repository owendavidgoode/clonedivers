#!/usr/bin/env bash
set -euo pipefail
artifacts=$(realpath "${1:?artifacts directory}")
cd "$artifacts"
sha256sum --check SHA256SUMS
archive=Clonedivers-linux-x64.tar.gz
deb=Clonedivers-linux-x64.deb
qa=$(mktemp -d)
echo "Disposable Forge package upgrade evidence: $qa"

# Only disposable package/config fixtures: never create a Forge installation,
# import an account, enter a namespace, or start Steam/the game.
export XDG_CONFIG_HOME="$qa/preserved/config"
export XDG_DATA_HOME="$qa/data"
export XDG_CACHE_HOME="$qa/cache"
mkdir -p "$XDG_CONFIG_HOME/clonedivers" "$XDG_DATA_HOME" "$XDG_CACHE_HOME" "$qa/baseline"
fixture_forge="$qa/preserved/forge"
mkdir -p "$fixture_forge/profiles/default" "$fixture_forge/account-snapshot/default"
jq -n --arg root "$fixture_forge" '{Format:1,InstallRoot:$root,Mode:"clonedivers",DeltaSquad:false,Texture:"lighter",Droids:false,Covenant:true}' > "$XDG_CONFIG_HOME/clonedivers/forge.json"
printf '%s\n' '{"fixture":"prior Steam settings","TextureProfile":"lighter","Options":{"commandos":false,"droids":false}}' > "$XDG_CONFIG_HOME/clonedivers/settings.json"
printf '%s\n' '{"fixture":"default progression","experience":14140}' > "$fixture_forge/profiles/default/profile.json"
printf '%s\n' '{"fixture":"prior ownership receipt; not a real account attestation"}' > "$fixture_forge/profiles/default/account-import.json"
printf '%s\n' '{"fixture":"prior account seal; not a real account attestation"}' > "$fixture_forge/account-snapshot/default/seal.json"
printf '%s\n' '{"fixture":"prior profile registry","active":"default"}' > "$fixture_forge/profiles/registry.json"
preserved_manifest() {
    if find "$qa/preserved" -type l -print -quit | grep -q .; then echo 'Linked preservation fixture refused' >&2; return 1; fi
    find "$qa/preserved" -type f -print0 | sort -z | xargs -0 sha256sum
}
preserved_manifest > "$qa/preserved-before.sha256"
check_preserved() {
    preserved_manifest > "$qa/preserved-after.sha256"
    cmp "$qa/preserved-before.sha256" "$qa/preserved-after.sha256"
    echo "PASS fixture configuration/profile/receipt inventory and bytes unchanged: $1"
}
extract_checked() {
    local source=$1 target=$2
    tar -tzf "$source" > "$qa/archive-paths"
    if grep -Eq '(^/|(^|/)\.\.(/|$))' "$qa/archive-paths"; then echo 'Unsafe archive path' >&2; return 1; fi
    mkdir -p "$target"
    tar -xzf "$source" -C "$target"
    if find "$target" -type l -print -quit | grep -q .; then echo 'Unexpected archive link' >&2; return 1; fi
}
check_pair() {
    local root=$1 version=$2
    [[ -x "$root/clonedivers" && -f "$root/forge/LICENSE-CLONEDIVERS.txt" && -f "$root/preset.json" ]]
    [[ $(jq -r .versions.linux "$root/clonedivers-forge-channel.json") == "$version" ]]
    [[ $(jq -r .versions.bridge "$root/clonedivers-forge-channel.json") == 0.4.0 ]]
    [[ $(jq -r .bridgeVersion "$root/forge/companion.json") == 0.4.0 ]]
    [[ $(jq -r .gameBuild "$root/forge/companion.json") == 25480438 ]]
    [[ $(jq -r .control "$root/forge/companion.json") == native-linux ]]
    [[ $(sha256sum "$root/forge/HellForge.Launcher.ClonediversBridge" | cut -d' ' -f1) == $(jq -r .bridgeSha256 "$root/forge/companion.json") ]]
    "$root/forge/HellForge.Launcher.ClonediversBridge" --help > /dev/null
    "$root/clonedivers" --version | grep -Fx "Clonedivers Forge Linux $version"
    "$root/clonedivers" --help | grep -F 'Clonedivers Forge for Linux'
    "$root/clonedivers" --steam --help | grep -F 'Clonedivers Linux'
}

# Immutable public 0.8.1 identities, independently byte-verified in the release
# rollback baseline. Do not replace these with latest/feed-derived artifacts.
baseline_url=https://github.com/owendavidgoode/clonedivers/releases/download/linux-v0.8.1
curl --fail --location --retry 3 --connect-timeout 20 --max-time 300 "$baseline_url/$archive" -o "$qa/baseline/$archive"
curl --fail --location --retry 3 --connect-timeout 20 --max-time 300 "$baseline_url/$deb" -o "$qa/baseline/$deb"
[[ $(stat -c %s "$qa/baseline/$archive") == 67002356 ]]
[[ $(stat -c %s "$qa/baseline/$deb") == 60884032 ]]
printf '%s  %s\n' \
    8899fd46e052229974c05b0a0854d6492551a376feef406bf62bd83e675c8ef2 "$qa/baseline/$archive" \
    d79806ff979b04ad0e68d61029c6e034e97d563beeb75c2085e506bdf6edfa8a "$qa/baseline/$deb" | sha256sum --check -

extract="$qa/candidate"
portable="$qa/portable"
extract_checked "$archive" "$extract"
cmp "$extract/clonedivers-forge-channel.json" clonedivers-forge-channel.json
check_pair "$extract" 0.8.2
check_preserved 'fresh portable candidate'

# Upgrade the same portable directory from the real old package. Check the
# actual new executable/companion bytes, not just a version string.
extract_checked "$qa/baseline/$archive" "$portable"
check_pair "$portable" 0.8.1
check_preserved 'portable 0.8.1'
extract_checked "$archive" "$portable"
check_pair "$portable" 0.8.2
for file in clonedivers forge/companion.json forge/HellForge.Launcher.ClonediversBridge clonedivers-forge-channel.json preset.json; do
    cmp "$extract/$file" "$portable/$file"
done
check_preserved 'portable 0.8.1 -> 0.8.2 overlay upgrade'

[[ $(dpkg-deb -f "$deb" Package) == clonedivers && $(dpkg-deb -f "$deb" Version) == 0.8.2 && $(dpkg-deb -f "$deb" Architecture) == amd64 ]]
[[ $(dpkg-deb -f "$qa/baseline/$deb" Package) == clonedivers && $(dpkg-deb -f "$qa/baseline/$deb" Version) == 0.8.1 && $(dpkg-deb -f "$qa/baseline/$deb" Architecture) == amd64 ]]
sudo apt-get update -qq
sudo apt-get install -y "$qa/baseline/$deb"
[[ $(dpkg-query -W -f='${Status} ${Version}' clonedivers) == 'install ok installed 0.8.1' ]]
check_pair /usr/lib/clonedivers 0.8.1
/usr/bin/clonedivers --version | grep -Fx 'Clonedivers Forge Linux 0.8.1'
check_preserved 'deb 0.8.1 installed'
sudo apt-get install -y "$artifacts/$deb"
[[ $(dpkg-query -W -f='${Status} ${Version}' clonedivers) == 'install ok installed 0.8.2' ]]
check_pair /usr/lib/clonedivers 0.8.2
/usr/bin/clonedivers --version | grep -Fx 'Clonedivers Forge Linux 0.8.2'
/usr/bin/clonedivers --help | grep -F 'Clonedivers Forge for Linux'
/usr/bin/clonedivers --steam --help | grep -F 'Clonedivers Linux'
dpkg-deb -x "$deb" "$qa/deb-expected"
cmp "$qa/deb-expected/usr/bin/clonedivers" /usr/bin/clonedivers
for file in clonedivers forge/companion.json forge/HellForge.Launcher.ClonediversBridge clonedivers-forge-channel.json preset.json; do
    cmp "$extract/$file" "/usr/lib/clonedivers/$file"
done
check_preserved 'deb 0.8.1 -> 0.8.2 apt upgrade'
if apt-cache show passt 2>/dev/null | grep -q '^Package: passt$'; then command -v pasta; else echo 'This distribution lacks passt: real co-op stays blocked by the launcher preflight.'; fi
command -v python3
echo 'Forge portable/deb 0.8.1 -> 0.8.2 package upgrade, preserved fixture bytes, default/Steam routing and exact launcher/companion pairing passed.'
echo 'Only version/help entrypoints ran: settings migration, real account ownership, gameplay, Proton and multiplayer remain unqualified.'

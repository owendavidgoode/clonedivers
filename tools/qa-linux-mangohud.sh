#!/usr/bin/env bash
set -euo pipefail
binary=$(realpath "${1:?Pass the published Linux x64 executable}")
qa_root=$(mktemp -d)
game="$qa_root/Game Library/steamapps/common/Helldivers 2"
mkdir -p "$game/data" "$game/bin"
touch "$game/bin/helldivers2.exe"
XDG_CONFIG_HOME="$qa_root/config" "$binary" --game "$game" record
icd=$(find /usr/share/vulkan/icd.d -name '*lvp*.json' -print -quit)
test -n "$icd"
# Software Vulkan + an X virtual display exercise the real installed MangoHud layer.
timeout 90s xvfb-run -a env VK_ICD_FILENAMES="$icd" "$game/mods_diagnostics/steam-capture.sh" vkcube --c 1800 --width 800 --height 600
csv=$(find "$game/mods_diagnostics/sessions" -path '*/mangohud/*.csv' -type f -print -quit)
test -n "$csv"
test -s "$csv"
grep -qi 'fps' "$csv"
printf 'Real MangoHud CSV verified: %s\n' "$csv"
XDG_CONFIG_HOME="$qa_root/config" "$binary" --game "$game" record-status
XDG_CONFIG_HOME="$qa_root/config" "$binary" --game "$game" report "$qa_root/support.zip"
test -s "$qa_root/support.zip"

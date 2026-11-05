#!/usr/bin/env bash
# Package an already reviewed allowlisted staging tree, with explicit POSIX modes.
set -euo pipefail
stage=$(realpath "${1:?staging directory}")
output=$(realpath -m "${2:?output .tar.gz}")
[[ -d "$stage/forge/linux" && -f "$stage/clonedivers" && -f "$stage/preset.json" ]] || exit 64
[[ ! -e "$output" && ! -e "${output%.gz}" ]] || { echo 'Output already exists' >&2; exit 64; }
[[ "$output" != "$stage/"* && "$output" == *.tar.gz ]] || exit 64
if find "$stage" -type l -print -quit | grep -q .; then echo 'Linked package input refused' >&2; exit 64; fi
archive=${output%.gz}
cd "$stage"
# Include regular data first, then executables. No directory entries need weak modes.
find . -type f ! -name clonedivers ! -name '*.sh' -print0 | LC_ALL=C sort -z |
  tar --null --no-recursion --owner=0 --group=0 --numeric-owner --mtime='2026-11-04 00:00:00Z' --mode=644 -cf "$archive" -T -
find . -type f \( -name clonedivers -o -name '*.sh' \) -print0 | LC_ALL=C sort -z |
  tar --null --no-recursion --owner=0 --group=0 --numeric-owner --mtime='2026-11-04 00:00:00Z' --mode=755 -rf "$archive" -T -
gzip -n -9 "$archive"

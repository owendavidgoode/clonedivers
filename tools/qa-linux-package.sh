#!/usr/bin/env bash
set -euo pipefail

artifacts=$(realpath "${1:?Pass the directory containing release archives and SHA256SUMS}")
repo=$(cd "$(dirname "$0")/.." && pwd)
cd "$artifacts"
sha256sum --check SHA256SUMS
package_qa_dir=$(mktemp -d)
for arch in x64 arm64; do
  archive="Clonedivers-linux-$arch.tar.gz"
  # Reject additional archive entries and validate the binary for each advertised architecture.
  diff -u <(printf 'LICENSE\nREADME.md\nclonedivers\n') <(tar -tzf "$archive" | sort)
  mkdir "$package_qa_dir/$arch"
  tar -xzf "$archive" -C "$package_qa_dir/$arch"
  test -x "$package_qa_dir/$arch/clonedivers"
  case "$arch" in
    x64) readelf -h "$package_qa_dir/$arch/clonedivers" | grep -q 'Advanced Micro Devices X86-64' ;;
    arm64) readelf -h "$package_qa_dir/$arch/clonedivers" | grep -q 'AArch64' ;;
  esac
done
"$package_qa_dir/x64/clonedivers" --version
"$package_qa_dir/x64/clonedivers" --help
cd "$repo"
CLONEDIVERS_TEST_BINARY="$package_qa_dir/x64/clonedivers" dotnet run --project Clonedivers.Linux.Tests -c Release

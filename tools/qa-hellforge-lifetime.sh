#!/usr/bin/env bash
# Shell stand-ins only. This never starts Proton, namespaces, Steam or a game.
set -euo pipefail
here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
source "$here/hellforge-linux/lifetime-functions.sh"
fixture=$(mktemp -d)
trap 'rm -rf -- "$fixture"' EXIT
mkdir -p "$fixture/bin"
cat > "$fixture/bin/pgrep" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "${HF_TEST_PIDS:-}"
STUB
chmod +x "$fixture/bin/pgrep"
export PATH="$fixture/bin:$PATH"
pat='HellForge\.(Launcher|Session|Cli)|LocalBackend|helldivers2'
passes=0
check() { if "$@"; then passes=$((passes + 1)); else echo "FAIL: $*" >&2; exit 1; fi; }
hf_parent_pid() { if [ "$1" = "$$" ]; then echo 424242; elif [ "$1" = 424242 ]; then echo 1; else return 1; fi; }
hf_process_exists() { case "$1" in 500001|500002) return 0 ;; *) return 1 ;; esac; }
export HF_TEST_PIDS
HF_TEST_PIDS=$(printf '%s\n424242\n' "$$")
check test -n "$HF_TEST_PIDS"
check eval '! hf_session_processes_running'
HF_TEST_PIDS=$(printf '%s\n424242\n500001\n' "$$")
check hf_session_processes_running
HF_TEST_PIDS=$(printf '500002\n%s\n424242\n' "$$")
check hf_session_processes_running
HF_TEST_PIDS=$(printf '%s\n424242\n599999\n' "$$")
check eval '! hf_session_processes_running'
HF_TEST_PIDS='malformed'
check eval '! hf_session_processes_running'
HF_TEST_PIDS=''
check eval '! hf_session_processes_running'
# Verify the exact original pattern matches a wrapper's own external argv.
check bash -c '[[ "bash hf-inner.sh /prefix/HellForge.Launcher.ClonediversBridge.exe --install C:\HellForge" =~ $1 ]]' _ "$pat"
printf '%s shell fixture assertions passed; no Linux runtime/game qualification.\n' "$passes"

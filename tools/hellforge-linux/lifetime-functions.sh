#!/usr/bin/env bash
# Clonedivers-authored lifetime helpers for a staged hf-inner.sh integration fix.
# This file changes process observation only, never namespace/network setup.

hf_parent_pid() {
  local key value rest
  while read -r key value rest; do
    if [ "$key" = 'PPid:' ]; then printf '%s\n' "$value"; return 0; fi
  done < "/proc/$1/status"
  return 1
}

hf_process_exists() { kill -0 "$1" 2>/dev/null; }

hf_session_processes_running() {
  local current=$$ parent candidate count=0
  local -A wrappers=()
  # pgrep -f sees the executable path in our external-launcher argv and in each
  # outer shell's argv. Ignore those shells, but retain actual Windows children.
  while [[ "$current" =~ ^[0-9]+$ ]] && [ "$current" -gt 1 ]; do
    [ -z "${wrappers[$current]+set}" ] || break
    wrappers[$current]=1
    count=$((count + 1))
    [ "$count" -le 128 ] || { echo 'Could not resolve wrapper ancestry.' >&2; return 0; }
    parent=$(hf_parent_pid "$current" 2>/dev/null) || break
    current=$parent
  done
  while read -r candidate; do
    [[ "$candidate" =~ ^[0-9]+$ ]] || continue
    [ -z "${wrappers[$candidate]+set}" ] || continue
    hf_process_exists "$candidate" && return 0
  # exec removes the process-substitution shell before pgrep scans argv.
  done < <(exec pgrep -u "$(id -u)" -f "$pat")
  return 1
}

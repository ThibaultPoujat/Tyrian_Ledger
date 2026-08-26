#!/usr/bin/env bash

# Stop only processes started by scripts/dev-up.sh. It refuses to kill a PID
# whose command line does not point back to this repository.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_dir="$repo_root/.local/dev"

stop_service() {
  local name="$1"
  local pid_file="$runtime_dir/$2.pid"
  local pid command_line

  if [[ ! -f "$pid_file" ]]; then
    echo "$name was not started by dev-up."
    return
  fi

  pid="$(<"$pid_file")"
  command_line="$(ps -p "$pid" -o command= 2>/dev/null || true)"
  if [[ -z "$command_line" ]]; then
    echo "$name is no longer running."
  elif [[ "$command_line" == *"$repo_root"* ]]; then
    kill "$pid"
    echo "Stopped $name."
  else
    echo "Refusing to stop $name: PID $pid does not belong to this repository." >&2
    return 1
  fi

  rm -f "$pid_file"
}

stop_service "frontend" "frontend"
stop_service "API" "api"

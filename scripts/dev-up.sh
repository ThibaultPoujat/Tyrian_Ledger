#!/usr/bin/env bash

# Start the local Tyrian Ledger API and Vite frontend when they are not already
# reachable, then open the frontend for a quick manual check.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
runtime_dir="$repo_root/.local/dev"
api_url="http://127.0.0.1:5000/healthz"
web_url="http://127.0.0.1:5173"

command -v curl >/dev/null || {
  echo "curl is required to check the local services." >&2
  exit 1
}
command -v dotnet >/dev/null || {
  echo "The .NET SDK is required. Install the version in global.json." >&2
  exit 1
}
command -v node >/dev/null || {
  echo "Node.js is required. Install a current Node.js release." >&2
  exit 1
}
[[ -f "$repo_root/frontend/node_modules/vite/bin/vite.js" ]] || {
  echo "Frontend dependencies are missing. Run: cd frontend && npm ci" >&2
  exit 1
}

mkdir -p "$runtime_dir"

is_reachable() {
  curl --silent --fail --max-time 1 "$1" >/dev/null 2>&1
}

wait_for() {
  local url="$1"
  local service_name="$2"
  local log_file="$3"

  for _ in $(seq 1 30); do
    if is_reachable "$url"; then
      return 0
    fi
    sleep 1
  done

  echo "$service_name did not become available at $url." >&2
  echo "Recent log output ($log_file):" >&2
  tail -n 40 "$log_file" >&2 || true
  exit 1
}

start_api() {
  local log_file="$runtime_dir/api.log"

  if is_reachable "$api_url"; then
    echo "API already running at $api_url"
    return
  fi

  echo "Starting API at $api_url"
  nohup dotnet run --project "$repo_root/src/Gw2Tp.Web" -- --urls http://127.0.0.1:5000 \
    >"$log_file" 2>&1 &
  echo "$!" >"$runtime_dir/api.pid"
  wait_for "$api_url" "API" "$log_file"
}

start_frontend() {
  local log_file="$runtime_dir/frontend.log"

  if is_reachable "$web_url"; then
    echo "Frontend already running at $web_url"
    return
  fi

  echo "Starting frontend at $web_url"
  (
    cd "$repo_root/frontend"
    nohup node "$repo_root/frontend/node_modules/vite/bin/vite.js" \
      --host 127.0.0.1 --port 5173 --strictPort >"$log_file" 2>&1 &
    echo "$!" >"$runtime_dir/frontend.pid"
  )
  wait_for "$web_url" "Frontend" "$log_file"
}

open_browser() {
  case "$(uname -s)" in
    Darwin) open "$web_url" >/dev/null 2>&1 & ;;
    Linux)
      if command -v xdg-open >/dev/null; then
        xdg-open "$web_url" >/dev/null 2>&1 &
      else
        echo "Open $web_url in a browser."
      fi
      ;;
    *) echo "Open $web_url in a browser." ;;
  esac
}

start_api
start_frontend
echo "Tyrian Ledger is ready at $web_url"
open_browser

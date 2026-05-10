#!/usr/bin/env bash
set -euo pipefail

mkdir -p upstream

fetch_repo() {
  local name="$1"
  local url="$2"
  local path="upstream/$name"

  if [ -d "$path/.git" ]; then
    git -C "$path" fetch --depth 1 origin
    git -C "$path" reset --hard FETCH_HEAD
  else
    git clone --depth 1 "$url" "$path"
  fi
}

fetch_repo NuvioMobile https://github.com/NuvioMedia/NuvioMobile.git
fetch_repo NuvioTV https://github.com/NuvioMedia/NuvioTV.git

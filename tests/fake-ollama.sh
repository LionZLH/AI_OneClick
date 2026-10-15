#!/usr/bin/env bash
# 测试用的 ollama 替身（通过 AISTATION_OLLAMA 指定）。serve 时在 11434 端口起一个空的 HTTP 服务。
state="${TMPDIR:-/tmp}/fake-ollama-models"
case "$1" in
  serve) exec python3 -m http.server 11434 --bind 127.0.0.1 >/dev/null 2>&1 ;;
  list)
    echo "NAME          ID              SIZE      MODIFIED"
    [ -f "$state" ] && while read -r m; do echo "$m    365c0bd3c000    5.2 GB    1 minute ago"; done < "$state"
    exit 0 ;;
  pull)
    echo "pulling manifest"
    for p in 10 30 55 80 100; do printf '\rpulling a3de86cd1c13: %3d%% ▕██████          ▏ 2.6 GB/5.2 GB  60 MB/s  40s' "$p"; sleep 0.4; done
    printf '\nverifying sha256 digest\nwriting manifest\nsuccess\n'
    echo "$2" >> "$state"; exit 0 ;;
esac
exit 1

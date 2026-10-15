#!/usr/bin/env bash
# 用 Mono 编译（仅用于在 Linux/macOS 上开发调试；正式版请在 Windows 上运行 build.cmd 或由 CI 构建）。
# -langversion:5 保证代码能被 Windows 自带的 csc.exe（C# 5）编译。
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p dist
res=()
for f in ui/*; do res+=("-resource:$f,ui/$(basename "$f")"); done
mcs -nologo -langversion:5 -target:winexe -optimize+ -platform:anycpu -sdk:4.5 \
  -win32icon:assets/app.ico -out:dist/AIStation.exe \
  -r:System.Web.Extensions.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll \
  "${res[@]}" src/*.cs
echo "Built dist/AIStation.exe"

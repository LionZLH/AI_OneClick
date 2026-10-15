#!/usr/bin/env bash
# 测试用的 winget 替身，行为尽量贴近真实 winget：
# - 输出被重定向时不打印进度条（真实 winget 也是如此）
# - 把安装包逐步写入 %TEMP%\WinGet\<包ID>.<版本>\，AI Station 通过测量文件大小得到真实进度
# - show 命令给出安装包地址（由 e2e 测试启动的本地服务器提供文件大小）
# 通过环境变量 AISTATION_WINGET 指向本文件，仅用于在非 Windows 环境下做界面联调。
case "$1" in
  --version) echo "v1.11.510"; exit 0 ;;
esac
verb="$1"; id=""
while [ $# -gt 0 ]; do [ "$1" = "--id" ] && id="$2"; shift; done
size=$((40 * 1024 * 1024))
if [ "$verb" = "show" ]; then
  echo "已找到 $id [$id]"
  echo "版本: 1.0.0"
  echo "主页: https://example.invalid/$id"
  echo "安装程序:"
  echo "  安装程序类型: nullsoft"
  echo "  安装程序 Url: ${FAKE_INSTALLER_BASE:-http://127.0.0.1:1}/$id-setup.exe"
  exit 0
fi
echo "已找到 $id [$id] 版本 1.0.0"
if [ "$verb" = "install" ] || [ "$verb" = "upgrade" ]; then
  echo "此应用程序由其所有者授权给你。"
  dir="${TMPDIR:-/tmp}/WinGet/$id.1.0.0"; mkdir -p "$dir"; f="$dir/setup.exe"; : > "$f"
  for i in $(seq 1 10); do head -c $((size / 10)) /dev/zero >> "$f"; sleep 0.4; done
  echo "已成功验证安装程序哈希"
  echo "正在启动程序包安装..."
  sleep 2
  rm -rf "$dir"
  if [ "$id" = "${FAKE_WINGET_FAIL:-none}" ]; then echo "安装程序失败，退出代码: 1603"; exit 1; fi
  echo "已成功安装"
  exit 0
fi
if [ "$verb" = "uninstall" ]; then echo "已成功卸载"; exit 0; fi
exit 1

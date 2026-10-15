#!/usr/bin/env bash
# 测试用的 winget 替身：模拟真实 winget 的输出格式（含 \r 刷新的下载进度）。
# 通过环境变量 AISTATION_WINGET 指向本文件，仅用于在非 Windows 环境下做界面联调。
case "$1" in
  --version) echo "v1.9.25200"; exit 0 ;;
esac
verb="$1"; id=""
while [ $# -gt 0 ]; do [ "$1" = "--id" ] && id="$2"; shift; done
echo "已找到 $id [$id] 版本 1.0.0"
if [ "$verb" = "install" ] || [ "$verb" = "upgrade" ]; then
  echo "此应用程序由其所有者授权给你。"
  echo "正在下载 https://example.invalid/$id-setup.exe"
  for i in 5 12 20 31 44 58 70 83 95 100; do
    printf '\r  ██████████▒▒▒▒▒▒▒▒  %s.0 MB / 100 MB' "$i"; sleep 0.25
  done
  printf '\n'
  echo "已成功验证安装程序哈希"
  echo "正在启动程序包安装..."
  sleep 1
  if [ "$id" = "${FAKE_WINGET_FAIL:-none}" ]; then echo "安装程序失败，退出代码: 1603"; exit 1; fi
  echo "已成功安装"
  exit 0
fi
if [ "$verb" = "uninstall" ]; then echo "正在启动程序包卸载..."; sleep 1; echo "已成功卸载"; exit 0; fi
exit 1

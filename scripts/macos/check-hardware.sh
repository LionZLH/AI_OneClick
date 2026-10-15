#!/usr/bin/env bash
# ============================================================
#  AI装机助手 · 硬件检测工具 (macOS)
#  版本: 1.0.0
#
#  说明:
#    在下载离线安装包之前运行此脚本，检测当前 Mac 硬件是否
#    满足要求，并推荐适合您的安装包版本。
#    只读操作，不会修改任何系统文件。无需 sudo。
#
#  兼容性: macOS 12+, bash 3.2+, zsh 5+
# ============================================================

set -euo pipefail

SCRIPT_VERSION="1.0.0"

# ─────────────────────────────────────────────
# 颜色与样式
# ─────────────────────────────────────────────
if [[ -t 1 ]]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    CYAN='\033[0;36m'
    WHITE='\033[1;37m'
    GRAY='\033[0;90m'
    RESET='\033[0m'
else
    RED=''; GREEN=''; YELLOW=''; CYAN=''; WHITE=''; GRAY=''; RESET=''
fi

# ─────────────────────────────────────────────
# 模型规格（从高到低排列，匹配第一个合适的）
# ─────────────────────────────────────────────
# 格式: label|file|minRamGB|sizeGB|package
MODEL_TIERS=(
    "14B|Qwen2.5-14B-Instruct-Q4_K_M.gguf|32|9.0|AI装机-完整版（32GB+内存）"
    "7B|Qwen2.5-7B-Instruct-Q4_K_M.gguf|16|4.7|AI装机-标准版（16GB+内存）"
    "1.5B|Qwen2.5-1.5B-Instruct-Q4_K_M.gguf|8|1.1|AI装机-基础版（8-16GB内存）"
    "0.5B|Qwen2.5-0.5B-Instruct-Q4_K_M.gguf|4|0.4|AI装机-入门版（4-8GB内存）"
)

# 各安装包所需磁盘空间（GB）
# 格式: package|diskGB
PACKAGE_DISK=(
    "AI装机-入门版（4-8GB内存）|3.5"
    "AI装机-基础版（8-16GB内存）|4.0"
    "AI装机-标准版（16GB+内存）|8.0"
    "AI装机-完整版（32GB+内存）|13.0"
)

MIN_RAM_GB=4
MIN_DISK_GB=3

# ─────────────────────────────────────────────
# 辅助函数
# ─────────────────────────────────────────────
print_header() {
    local line
    line=$(printf '─%.0s' {1..60})
    echo ""
    echo -e "${CYAN}${line}${RESET}"
    echo -e "${CYAN}  🖥️  AI装机助手 · 硬件检测工具  v${SCRIPT_VERSION}${RESET}"
    echo -e "${GRAY}  macOS 版  |  只读模式，不修改任何系统设置${RESET}"
    echo -e "${CYAN}${line}${RESET}"
    echo ""
}

print_section() {
    echo ""
    echo -e "${YELLOW}▌ $1${RESET}"
    echo -e "${GRAY}  $(printf '─%.0s' {1..40})${RESET}"
}

print_check() {
    local label="$1"
    local value="$2"
    local pass="$3"   # "true" or "false"
    local note="${4:-}"
    local icon color
    if [[ "$pass" == "true" ]]; then icon="✔"; color="$GREEN"; else icon="✘"; color="$RED"; fi
    local padded
    padded=$(printf "%-18s" "$label")
    if [[ -n "$note" ]]; then
        echo -e "  ${color}${icon}  ${padded} ${value}${RESET}  ${GRAY}(${note})${RESET}"
    else
        echo -e "  ${color}${icon}  ${padded} ${value}${RESET}"
    fi
}

print_info() {
    local label
    label=$(printf "%-18s" "$1")
    echo -e "  ${GRAY}·${RESET}  ${label} ${WHITE}$2${RESET}"
}

# 比较两个浮点数：$1 >= $2 ? 返回0 : 返回1
float_gte() {
    awk -v a="$1" -v b="$2" 'BEGIN{exit !(a >= b)}'
}

# ─────────────────────────────────────────────
# 硬件检测函数
# ─────────────────────────────────────────────
detect_arch() {
    uname -m
}

detect_macos_version() {
    sw_vers -productVersion 2>/dev/null || echo "0.0.0"
}

# 返回 macOS 主版本号
macos_major() {
    echo "$1" | cut -d. -f1
}

detect_cpu_name() {
    sysctl -n machdep.cpu.brand_string 2>/dev/null \
        || system_profiler SPHardwareDataType 2>/dev/null \
            | grep "Chip\|Processor Name" | head -1 | awk -F': ' '{print $2}' \
        || echo "未知 CPU"
}

detect_avx2() {
    # Apple Silicon (arm64) 不需要 AVX2，直接通过
    local arch
    arch=$(detect_arch)
    if echo "$arch" | grep -q "arm64"; then
        echo "arm64_skip"
        return
    fi
    # x86_64: 检测 AVX2 特性标志
    if sysctl -n machdep.cpu.features machdep.cpu.leaf7_features 2>/dev/null \
            | tr ' ' '\n' | grep -qi "AVX2"; then
        echo "true"
    else
        echo "false"
    fi
}

detect_ram_gb() {
    local bytes
    bytes=$(sysctl -n hw.memsize 2>/dev/null || echo 0)
    awk -v b="$bytes" 'BEGIN{printf "%.1f", b/1073741824}'
}

detect_free_disk_gb() {
    # df -k 输出 KB，第4列为可用块
    local kb
    kb=$(df -k / 2>/dev/null | tail -1 | awk '{print $4}')
    awk -v k="${kb:-0}" 'BEGIN{printf "%.1f", k/1048576}'
}

detect_gpu_info() {
    local vram_lines
    vram_lines=$(system_profiler SPDisplaysDataType 2>/dev/null \
        | grep -E "VRAM|Chipset Model" | sed 's/^ *//' || true)
    if [[ -z "$vram_lines" ]]; then
        echo "无法读取 GPU 信息"
    else
        # 将多行合并成易读格式
        local chip="" vram=""
        while IFS= read -r line; do
            if echo "$line" | grep -q "Chipset Model"; then
                chip=$(echo "$line" | awk -F': ' '{print $2}')
            elif echo "$line" | grep -q "VRAM"; then
                vram=$(echo "$line" | awk -F': ' '{print $2}')
                echo "${chip:-未知}  [VRAM: ${vram:-未知}]"
                chip=""; vram=""
            fi
        done <<< "$vram_lines"
    fi
}

# ─────────────────────────────────────────────
# 主逻辑
# ─────────────────────────────────────────────
print_header

# ── 1. 收集硬件信息 ──
print_section "正在检测硬件..."
echo ""

ARCH=$(detect_arch)
MACOS_VER=$(detect_macos_version)
MACOS_MAJOR=$(macos_major "$MACOS_VER")
CPU_NAME=$(detect_cpu_name)
AVX2_RESULT=$(detect_avx2)
RAM_GB=$(detect_ram_gb)
FREE_GB=$(detect_free_disk_gb)
GPU_INFO=$(detect_gpu_info)

# ── 2. 显示硬件摘要 ──
print_section "硬件信息汇总"

if echo "$ARCH" | grep -q "arm64"; then
    ARCH_LABEL="Apple Silicon (arm64)"
else
    ARCH_LABEL="Intel x86_64"
fi
print_info "处理器架构"  "$ARCH_LABEL"
print_info "macOS 版本"  "$MACOS_VER"
print_info "处理器"      "$CPU_NAME"
if [[ "$AVX2_RESULT" == "arm64_skip" ]]; then
    print_info "AVX2 指令集" "不适用（Apple Silicon 无需）"
elif [[ "$AVX2_RESULT" == "true" ]]; then
    print_info "AVX2 指令集" "支持"
else
    print_info "AVX2 指令集" "不支持"
fi
print_info "内存容量"    "${RAM_GB} GB"
print_info "系统盘可用"  "${FREE_GB} GB"
while IFS= read -r gpu_line; do
    print_info "显卡" "$gpu_line"
done <<< "$GPU_INFO"

# ── 3. 需求检查 ──
print_section "最低硬件需求检查"

CHECK_FAILED=false
FAIL_REASONS=()

# 架构检查（只接受 arm64 或 x86_64）
if echo "$ARCH" | grep -qE "arm64|x86_64"; then
    print_check "处理器架构" "$ARCH_LABEL" "true" "64位架构"
else
    print_check "处理器架构" "$ARCH_LABEL" "false" "需要64位 Mac"
    CHECK_FAILED=true
    FAIL_REASONS+=("❌ 不支持的处理器架构：$ARCH")
fi

# macOS 版本检查（需要 macOS 12+）
if [[ "$MACOS_MAJOR" -ge 12 ]]; then
    print_check "macOS 版本" "macOS $MACOS_VER" "true" "需要 12.0+"
else
    print_check "macOS 版本" "macOS $MACOS_VER" "false" "需要 macOS 12.0 或更高"
    CHECK_FAILED=true
    FAIL_REASONS+=("❌ macOS 版本过低：当前 $MACOS_VER，需要 12.0+")
fi

# AVX2 检查
if [[ "$AVX2_RESULT" == "arm64_skip" ]]; then
    print_check "AVX2 指令集" "不适用（Apple Silicon）" "true" "Apple Silicon 原生支持"
elif [[ "$AVX2_RESULT" == "true" ]]; then
    print_check "AVX2 指令集" "支持" "true" "现代AI推理必需"
else
    print_check "AVX2 指令集" "不支持" "false" "需要支持 AVX2 的 Intel CPU（2013年后）"
    CHECK_FAILED=true
    FAIL_REASONS+=("❌ CPU 不支持 AVX2 指令集（CPU 过旧）")
fi

# RAM 检查
if float_gte "$RAM_GB" "$MIN_RAM_GB"; then
    print_check "内存容量" "${RAM_GB} GB" "true" "最低需要 ${MIN_RAM_GB} GB"
else
    print_check "内存容量" "${RAM_GB} GB" "false" "最低需要 ${MIN_RAM_GB} GB"
    CHECK_FAILED=true
    FAIL_REASONS+=("❌ 内存不足：当前 ${RAM_GB} GB，最低需要 ${MIN_RAM_GB} GB")
fi

# 磁盘检查
if float_gte "$FREE_GB" "$MIN_DISK_GB"; then
    print_check "系统盘可用空间" "${FREE_GB} GB" "true" "最低需要 ${MIN_DISK_GB} GB"
else
    print_check "系统盘可用空间" "${FREE_GB} GB" "false" "最低需要 ${MIN_DISK_GB} GB"
    CHECK_FAILED=true
    FAIL_REASONS+=("❌ 磁盘空间不足：当前可用 ${FREE_GB} GB，最低需要 ${MIN_DISK_GB} GB")
fi

# ── 4. 检查失败则退出 ──
if [[ "$CHECK_FAILED" == "true" ]]; then
    echo ""
    echo -e "${RED}$(printf '─%.0s' {1..60})${RESET}"
    echo -e "${RED}  ✘  硬件不满足最低要求，无法运行 AI 本地模型${RESET}"
    echo -e "${RED}$(printf '─%.0s' {1..60})${RESET}"
    for reason in "${FAIL_REASONS[@]}"; do
        echo -e "${RED}  ${reason}${RESET}"
    done
    echo ""
    echo -e "${YELLOW}  请升级硬件后重新检测，或考虑使用在线 AI 服务。${RESET}"
    echo ""
    exit 1
fi

# ── 5. 推荐模型规格 ──
SELECTED_LABEL=""
SELECTED_FILE=""
SELECTED_MIN_RAM=""
SELECTED_SIZE=""
SELECTED_PACKAGE=""

for tier in "${MODEL_TIERS[@]}"; do
    IFS='|' read -r label file minRam sizeGB pkg <<< "$tier"
    if float_gte "$RAM_GB" "$minRam"; then
        SELECTED_LABEL="$label"
        SELECTED_FILE="$file"
        SELECTED_MIN_RAM="$minRam"
        SELECTED_SIZE="$sizeGB"
        SELECTED_PACKAGE="$pkg"
        break
    fi
done

# 兜底（理论上不会发生，因为上面已检查 >= 4GB）
if [[ -z "$SELECTED_LABEL" ]]; then
    IFS='|' read -r SELECTED_LABEL SELECTED_FILE SELECTED_MIN_RAM SELECTED_SIZE SELECTED_PACKAGE \
        <<< "${MODEL_TIERS[-1]}"
fi

print_section "推荐配置"
echo ""
echo -e "${GREEN}  ┌─────────────────────────────────────────────────────┐${RESET}"
echo -e "${GREEN}  │  🎯  根据您的内存 (${RAM_GB} GB) 推荐以下配置：${RESET}"
echo -e "${GREEN}  │${RESET}"
echo -e "${GREEN}  │  📦  安装包名称：${SELECTED_PACKAGE}${RESET}"
echo -e "${GREEN}  │  🤖  模型版本：  Qwen2.5-${SELECTED_LABEL} (Q4_K_M量化)${RESET}"
echo -e "${GREEN}  │  📄  模型文件：  ${SELECTED_FILE}${RESET}"
echo -e "${GREEN}  │  💾  模型大小：  ${SELECTED_SIZE} GB${RESET}"
echo -e "${GREEN}  └─────────────────────────────────────────────────────┘${RESET}"

# ── 6. 各安装包磁盘需求表 ──
print_section "各版本安装包所需磁盘空间参考"
echo ""
printf "  ${GRAY}%-32s %-14s %-10s${RESET}\n" "安装包名称" "所需空间(GB)" "当前可用"
echo -e "  ${GRAY}$(printf '─%.0s' {1..55})${RESET}"

# 反序显示（从小到大）
for tier in "${MODEL_TIERS[@]}"; do
    IFS='|' read -r label file minRam sizeGB pkg <<< "$tier"
    # 查找对应磁盘空间
    disk_need=""
    for pd in "${PACKAGE_DISK[@]}"; do
        IFS='|' read -r pd_pkg pd_disk <<< "$pd"
        if [[ "$pd_pkg" == "$pkg" ]]; then
            disk_need="$pd_disk"
            break
        fi
    done
    can_fit="✘ 不足"
    row_color="$GRAY"
    if float_gte "$FREE_GB" "${disk_need:-0}"; then
        can_fit="✔ 可用"
        row_color="$WHITE"
    fi
    suffix=""
    if [[ "$pkg" == "$SELECTED_PACKAGE" ]]; then
        suffix=" ◀ 推荐"
        row_color="$GREEN"
    fi
    echo -e "  ${row_color}$(printf "%-32s %-14s %-10s%s" "$pkg" "${disk_need:-?}" "$can_fit" "$suffix")${RESET}"
done

echo ""
echo -e "${CYAN}$(printf '─%.0s' {1..60})${RESET}"
echo -e "${CYAN}  ✅  硬件检测通过！请下载上方推荐的安装包。${RESET}"
echo -e "${CYAN}$(printf '─%.0s' {1..60})${RESET}"
echo ""

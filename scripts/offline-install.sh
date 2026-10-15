#!/usr/bin/env bash
# ============================================================
#  AI装机助手 · 离线安装器 (macOS)
#  版本: 1.0.0
#
#  说明:
#    全自动离线安装 AI 客户端及本地大语言模型。
#    需要在包含 apps/ 和 models/ 的离线安装包根目录下运行。
#    建议以 sudo 运行（某些 .pkg 安装需要管理员权限）。
#
#  目录结构:
#    offline-package/
#      offline-install.sh    ← 本脚本
#      apps/
#        LM-Studio-*.dmg     (或 .pkg)
#        AnythingLLM-*.dmg
#        Jan-*.dmg
#        GPT4All-*.dmg
#      models/
#        Qwen2.5-0.5B-Instruct-Q4_K_M.gguf
#        Qwen2.5-1.5B-Instruct-Q4_K_M.gguf
#        Qwen2.5-7B-Instruct-Q4_K_M.gguf
#        Qwen2.5-14B-Instruct-Q4_K_M.gguf
#
#  兼容性: macOS 12+, bash 3.2+, zsh 5+
# ============================================================

set -uo pipefail
# 注意: 不用 -e，以便在单步失败时继续执行其他步骤

SCRIPT_VERSION="1.0.0"

# 脚本所在目录（兼容直接运行和 source 运行）
if [[ -n "${BASH_SOURCE[0]:-}" ]]; then
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
else
    SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
fi

APPS_DIR="${SCRIPT_DIR}/apps"
MODELS_DIR="${SCRIPT_DIR}/models"

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
# 模型规格（从高到低）
# 格式: label|file|minRamGB|sizeGB
# ─────────────────────────────────────────────
MODEL_TIERS=(
    "14B|Qwen2.5-14B-Instruct-Q4_K_M.gguf|32|9.0"
    "7B|Qwen2.5-7B-Instruct-Q4_K_M.gguf|16|4.7"
    "1.5B|Qwen2.5-1.5B-Instruct-Q4_K_M.gguf|8|1.1"
    "0.5B|Qwen2.5-0.5B-Instruct-Q4_K_M.gguf|4|0.4"
)

# 模型注入目标路径
# LM Studio (macOS 官方路径)
LMS_MODEL_DIR="${HOME}/Library/Application Support/LM-Studio/models/lmstudio-community/Qwen2.5-Instruct-GGUF"
# Jan models
JAN_MODEL_DIR="${HOME}/jan/models"
# GPT4All
GPT4ALL_MODEL_DIR="${HOME}/Library/Application Support/nomic.ai/GPT4All"

# ─────────────────────────────────────────────
# 结果跟踪（使用临时文件避免子shell变量丢失问题）
# ─────────────────────────────────────────────
RESULTS_FILE="$(mktemp /tmp/ai_install_results.XXXXXX)"
ERRORS_FILE="$(mktemp /tmp/ai_install_errors.XXXXXX)"
trap 'rm -f "$RESULTS_FILE" "$ERRORS_FILE"' EXIT

add_result() {
    # app|status|detail
    printf "%s|%s|%s\n" "$1" "$2" "$3" >> "$RESULTS_FILE"
}

add_error() {
    echo "$1" >> "$ERRORS_FILE"
}

# ─────────────────────────────────────────────
# 辅助函数
# ─────────────────────────────────────────────
get_ts() { date '+%H:%M:%S'; }

print_banner() {
    local line
    line=$(printf '═%.0s' {1..62})
    echo ""
    echo -e "${CYAN}${line}${RESET}"
    echo -e "${CYAN}  🚀  AI装机助手 · 离线安装器  v${SCRIPT_VERSION}${RESET}"
    echo -e "${WHITE}  自动安装 AI 客户端 + 本地大语言模型（Qwen2.5）${RESET}"
    echo -e "${CYAN}${line}${RESET}"
    echo -e "${GRAY}  运行时间: $(date '+%Y-%m-%d %H:%M:%S')${RESET}"
    echo -e "${GRAY}  安装包目录: ${SCRIPT_DIR}${RESET}"
    echo -e "${CYAN}${line}${RESET}"
    echo ""
}

print_section() {
    echo ""
    echo -e "${YELLOW}▌ $1${RESET}"
    echo -e "${GRAY}  $(printf '─%.0s' {1..50})${RESET}"
}

log_step() { echo -e "  ${CYAN}[$(get_ts)]  → $1${RESET}"; }
log_ok()   { echo -e "  ${GREEN}[$(get_ts)]  ✔ $1${RESET}"; }
log_skip() { echo -e "  ${YELLOW}[$(get_ts)]  ⊘ $1${RESET}"; }
log_warn() { echo -e "  ${YELLOW}[$(get_ts)]  ⚠ $1${RESET}"; }
log_err()  { echo -e "  ${RED}[$(get_ts)]  ✘ $1${RESET}"; }

# 浮点数比较: $1 >= $2
float_gte() {
    awk -v a="$1" -v b="$2" 'BEGIN{exit !(a >= b)}'
}

# ─────────────────────────────────────────────
# 硬件检测
# ─────────────────────────────────────────────
hw_detect_arch() { uname -m; }

hw_detect_macos_major() {
    sw_vers -productVersion 2>/dev/null | cut -d. -f1
}

hw_detect_macos_ver() {
    sw_vers -productVersion 2>/dev/null || echo "0.0.0"
}

hw_detect_cpu() {
    sysctl -n machdep.cpu.brand_string 2>/dev/null \
        || system_profiler SPHardwareDataType 2>/dev/null \
            | grep -E "Chip|Processor Name" | head -1 | awk -F': ' '{print $2}' \
        || echo "未知"
}

hw_detect_avx2() {
    local arch
    arch=$(hw_detect_arch)
    if echo "$arch" | grep -q "arm64"; then
        echo "arm64_skip"
        return
    fi
    if sysctl -n machdep.cpu.features machdep.cpu.leaf7_features 2>/dev/null \
            | tr ' ' '\n' | grep -qi "AVX2"; then
        echo "true"
    else
        echo "false"
    fi
}

hw_detect_ram_gb() {
    local bytes
    bytes=$(sysctl -n hw.memsize 2>/dev/null || echo 0)
    awk -v b="$bytes" 'BEGIN{printf "%.1f", b/1073741824}'
}

hw_detect_free_gb() {
    local kb
    kb=$(df -k / 2>/dev/null | tail -1 | awk '{print $4}')
    awk -v k="${kb:-0}" 'BEGIN{printf "%.1f", k/1048576}'
}

# ─────────────────────────────────────────────
# 应用安装辅助
# ─────────────────────────────────────────────

# 检查 .app 是否已安装（按名称）
app_is_installed() {
    local app_name="$1"    # e.g. "LM Studio"
    test -d "/Applications/${app_name}.app"
}

# 挂载 DMG，把第一个 .app 复制到 /Applications，然后卸载
install_dmg() {
    local dmg_path="$1"
    local display_name="$2"

    log_step "挂载 DMG：$(basename "$dmg_path")"
    local mount_point
    mount_point=$(hdiutil attach "$dmg_path" -nobrowse -noverify -noautoopen 2>&1 \
        | grep "^/dev/" | grep -v "^/dev/disk" | awk '{print $NF}' | head -1)

    if [[ -z "$mount_point" ]]; then
        # 尝试备用方法
        mount_point=$(hdiutil attach "$dmg_path" -nobrowse -noverify -noautoopen 2>/dev/null \
            | tail -1 | awk '{print $NF}')
    fi

    if [[ -z "$mount_point" ]] || [[ ! -d "$mount_point" ]]; then
        echo "  ERROR: 无法挂载 DMG（mount_point=$mount_point）"
        return 1
    fi

    log_step "已挂载到：$mount_point"

    # 找第一个 .app
    local app_bundle
    app_bundle=$(find "$mount_point" -maxdepth 2 -name "*.app" -type d | head -1)
    if [[ -z "$app_bundle" ]]; then
        hdiutil detach "$mount_point" -quiet 2>/dev/null || true
        echo "  ERROR: DMG 中未找到 .app 文件"
        return 1
    fi

    local app_name
    app_name=$(basename "$app_bundle")
    log_step "复制 ${app_name} → /Applications/"
    cp -R "$app_bundle" /Applications/ 2>&1
    local cp_exit=$?

    hdiutil detach "$mount_point" -quiet 2>/dev/null || true

    if [[ $cp_exit -ne 0 ]]; then
        echo "  ERROR: 复制失败（退出码 $cp_exit），尝试使用 sudo"
        sudo cp -R "$app_bundle" /Applications/ || return 1
    fi

    # 移除隔离属性（避免安全提示）
    xattr -dr com.apple.quarantine "/Applications/${app_name}" 2>/dev/null || true

    return 0
}

# 安装 PKG 包
install_pkg() {
    local pkg_path="$1"
    log_step "安装 PKG：$(basename "$pkg_path")"
    if [[ $EUID -eq 0 ]]; then
        installer -pkg "$pkg_path" -target / 2>&1
    else
        sudo installer -pkg "$pkg_path" -target / 2>&1
    fi
}

# 根据 glob 模式找到第一个匹配文件
find_first() {
    local dir="$1"
    shift
    local pattern="$1"
    # 使用 ls + head 兼容 bash 3.2
    ls -1 "${dir}/"${pattern} 2>/dev/null | head -1
}

# ─────────────────────────────────────────────
# 安装单个应用（通用入口）
# ─────────────────────────────────────────────
install_app() {
    local key="$1"         # 内部键名，如 lmstudio
    local display="$2"     # 显示名，如 "LM Studio"
    local app_name="$3"    # .app 名称，如 "LM Studio"
    local glob_dmg="$4"    # DMG glob, 如 "LM-Studio-*.dmg"
    local glob_pkg="${5:-}"# PKG glob（可选）

    # 检查是否已安装
    if app_is_installed "$app_name"; then
        log_skip "${display}：已安装（/Applications/${app_name}.app），跳过"
        add_result "$display" "已存在" "/Applications/${app_name}.app"
        return 0
    fi

    # 查找安装包文件（优先 DMG）
    local installer_file=""
    local installer_type=""

    if [[ -n "$glob_dmg" ]]; then
        installer_file=$(find_first "$APPS_DIR" "$glob_dmg")
        if [[ -n "$installer_file" ]]; then installer_type="dmg"; fi
    fi

    if [[ -z "$installer_file" ]] && [[ -n "$glob_pkg" ]]; then
        installer_file=$(find_first "$APPS_DIR" "$glob_pkg")
        if [[ -n "$installer_file" ]]; then installer_type="pkg"; fi
    fi

    if [[ -z "$installer_file" ]]; then
        log_skip "${display}：apps/ 中未找到安装包（${glob_dmg}${glob_pkg:+, $glob_pkg}），已跳过"
        add_result "$display" "跳过" "安装包文件不存在"
        return 0
    fi

    log_step "安装 ${display}：$(basename "$installer_file")"

    local install_ok=false
    if [[ "$installer_type" == "dmg" ]]; then
        if install_dmg "$installer_file" "$display"; then
            install_ok=true
        fi
    elif [[ "$installer_type" == "pkg" ]]; then
        if install_pkg "$installer_file"; then
            install_ok=true
        fi
    fi

    if $install_ok && app_is_installed "$app_name"; then
        log_ok "${display} 安装完成"
        add_result "$display" "已安装" "/Applications/${app_name}.app"
    else
        log_err "${display} 安装失败（安装后未检测到 .app）"
        add_error "安装 ${display} 失败"
        add_result "$display" "失败" "安装后未检测到应用"
    fi
}

# ─────────────────────────────────────────────
# 模型注入
# ─────────────────────────────────────────────
inject_model() {
    local app_key="$1"       # lmstudio / jan / gpt4all
    local display="$2"       # 显示名
    local dest_dir="$3"      # 目标目录
    local model_file="$4"    # 模型文件完整路径
    local model_filename     # 模型文件名
    model_filename=$(basename "$model_file")

    # 仅当应用已安装时注入
    # 检查结果文件中对应 app 是否安装成功或已存在
    local app_installed=false
    while IFS='|' read -r res_app res_status res_detail; do
        if [[ "$res_app" == "$display" ]] && [[ "$res_status" == "已安装" || "$res_status" == "已存在" ]]; then
            app_installed=true
            break
        fi
    done < "$RESULTS_FILE"

    if ! $app_installed; then
        log_skip "${display} 未安装，跳过模型注入"
        return 0
    fi

    log_step "注入模型到 ${display}：${dest_dir}"

    # 如果是 Jan，子目录按模型ID组织
    if [[ "$app_key" == "jan" ]]; then
        local model_id
        model_id=$(basename "$model_filename" .gguf)
        dest_dir="${dest_dir}/${model_id}"
    fi

    # 创建目标目录
    mkdir -p "$dest_dir" 2>/dev/null || {
        log_err "无法创建目录：${dest_dir}"
        add_error "创建 ${display} 模型目录失败：${dest_dir}"
        add_result "${display} 模型" "失败" "无法创建目标目录"
        return 1
    }

    local dest_file="${dest_dir}/${model_filename}"

    # 如果目标文件已存在且大小相同则跳过
    if [[ -f "$dest_file" ]]; then
        local src_size dest_size
        src_size=$(stat -f%z "$model_file" 2>/dev/null || echo 0)
        dest_size=$(stat -f%z "$dest_file" 2>/dev/null || echo 0)
        if [[ "$src_size" -eq "$dest_size" ]] && [[ "$src_size" -gt 0 ]]; then
            log_skip "${display}：模型文件已存在且大小相同，跳过"
            add_result "${display} 模型" "已存在" "$dest_file"
            return 0
        fi
    fi

    log_step "复制模型文件 → ${dest_file}"
    if cp "$model_file" "$dest_file" 2>&1; then
        log_ok "${display} 模型注入完成"
        add_result "${display} 模型" "已注入" "$dest_file"
    else
        log_err "${display} 模型复制失败"
        add_error "复制模型到 ${display} 失败"
        add_result "${display} 模型" "失败" "cp 命令失败"
        return 1
    fi
}

# ─────────────────────────────────────────────
# STEP 1: 横幅
# ─────────────────────────────────────────────
print_banner

# ─────────────────────────────────────────────
# STEP 2: 硬件检测
# ─────────────────────────────────────────────
print_section "第一步：硬件检测"

HW_ARCH=$(hw_detect_arch)
HW_MACOS_VER=$(hw_detect_macos_ver)
HW_MACOS_MAJOR=$(hw_detect_macos_major)
HW_CPU=$(hw_detect_cpu)
HW_AVX2=$(hw_detect_avx2)
HW_RAM_GB=$(hw_detect_ram_gb)
HW_FREE_GB=$(hw_detect_free_gb)

if echo "$HW_ARCH" | grep -q "arm64"; then
    ARCH_LABEL="Apple Silicon (arm64)"
else
    ARCH_LABEL="Intel x86_64"
fi

echo -e "  ${WHITE}处理器：${HW_CPU}  [${ARCH_LABEL}]${RESET}"
echo -e "  ${WHITE}macOS：${HW_MACOS_VER}  |  内存：${HW_RAM_GB} GB  |  系统盘可用：${HW_FREE_GB} GB${RESET}"

HW_FAIL=false
HW_FAIL_MSGS=()

# 架构检查
if ! echo "$HW_ARCH" | grep -qE "arm64|x86_64"; then
    HW_FAIL=true
    HW_FAIL_MSGS+=("不支持的架构：$HW_ARCH（需要 arm64 或 x86_64）")
fi

# macOS 版本
if [[ "${HW_MACOS_MAJOR:-0}" -lt 12 ]]; then
    HW_FAIL=true
    HW_FAIL_MSGS+=("macOS 版本过低：${HW_MACOS_VER}（需要 12.0+）")
fi

# AVX2
if [[ "$HW_AVX2" == "false" ]]; then
    HW_FAIL=true
    HW_FAIL_MSGS+=("CPU 不支持 AVX2 指令集（需要 2013 年后的 Intel Mac）")
fi

# RAM
if ! float_gte "$HW_RAM_GB" "4"; then
    HW_FAIL=true
    HW_FAIL_MSGS+=("内存不足：当前 ${HW_RAM_GB} GB（需要 >= 4 GB）")
fi

# 磁盘
if ! float_gte "$HW_FREE_GB" "3"; then
    HW_FAIL=true
    HW_FAIL_MSGS+=("磁盘可用空间不足：当前 ${HW_FREE_GB} GB（需要 >= 3 GB）")
fi

if $HW_FAIL; then
    echo ""
    log_err "硬件不满足最低要求，无法继续安装："
    for msg in "${HW_FAIL_MSGS[@]}"; do
        echo -e "    ${RED}• ${msg}${RESET}"
    done
    echo ""
    exit 1
fi

log_ok "硬件检测通过"

# 选择模型
SEL_LABEL=""
SEL_FILE=""
SEL_SIZE=""

for tier in "${MODEL_TIERS[@]}"; do
    IFS='|' read -r t_label t_file t_min_ram t_size <<< "$tier"
    if float_gte "$HW_RAM_GB" "$t_min_ram"; then
        SEL_LABEL="$t_label"
        SEL_FILE="$t_file"
        SEL_SIZE="$t_size"
        break
    fi
done

# 兜底
if [[ -z "$SEL_LABEL" ]]; then
    IFS='|' read -r SEL_LABEL SEL_FILE _min SEL_SIZE <<< "${MODEL_TIERS[-1]}"
fi

log_ok "已选择模型：Qwen2.5-${SEL_LABEL}（${SEL_FILE}）"

# ─────────────────────────────────────────────
# STEP 3: 扫描安装包
# ─────────────────────────────────────────────
print_section "第二步：检查安装包内容"

if [[ ! -d "$APPS_DIR" ]]; then
    log_err "apps/ 目录不存在：$APPS_DIR"
    add_error "apps/ 目录缺失"
fi

# 检查模型文件
MODEL_FILE_PATH="${MODELS_DIR}/${SEL_FILE}"
MODEL_AVAILABLE=false

if [[ -f "$MODEL_FILE_PATH" ]]; then
    log_ok "发现模型文件：${SEL_FILE}"
    MODEL_AVAILABLE=true
else
    log_warn "所选模型不存在：${SEL_FILE}，尝试降级..."
    for tier in "${MODEL_TIERS[@]}"; do
        IFS='|' read -r t_label t_file t_min_ram t_size <<< "$tier"
        local_path="${MODELS_DIR}/${t_file}"
        if [[ -f "$local_path" ]]; then
            SEL_LABEL="$t_label"
            SEL_FILE="$t_file"
            SEL_SIZE="$t_size"
            MODEL_FILE_PATH="$local_path"
            MODEL_AVAILABLE=true
            log_ok "降级为：${SEL_FILE}"
            break
        fi
    done
    if ! $MODEL_AVAILABLE; then
        log_warn "未找到任何可用模型文件，模型注入步骤将被跳过"
    fi
fi

# 预览应用包
echo ""
for pattern in "LM-Studio-*.dmg" "AnythingLLM-*.dmg" "Jan-*.dmg" "GPT4All-*.dmg" \
               "LM-Studio-*.pkg" "AnythingLLM-*.pkg" "Jan-*.pkg" "GPT4All-*.pkg"; do
    found=$(find_first "$APPS_DIR" "$pattern" 2>/dev/null)
    if [[ -n "$found" ]]; then
        log_ok "发现：$(basename "$found")"
    fi
done

# ─────────────────────────────────────────────
# STEP 4: 确认安装
# ─────────────────────────────────────────────
print_section "第三步：安装计划确认"
echo ""
echo -e "  ${WHITE}即将安装以下内容：${RESET}"
echo -e "  ${CYAN}  ✦ LM Studio${RESET}"
echo -e "  ${CYAN}  ✦ AnythingLLM${RESET}"
echo -e "  ${CYAN}  ✦ Jan${RESET}"
echo -e "  ${CYAN}  ✦ GPT4All${RESET}"
if $MODEL_AVAILABLE; then
    echo -e "  ${CYAN}  ✦ 模型 Qwen2.5-${SEL_LABEL} → LM Studio / Jan / GPT4All${RESET}"
fi
echo ""

# 10秒倒计时
TIMEOUT=10
echo -ne "  ${YELLOW}继续安装？[Y/N]  （${TIMEOUT} 秒后自动确认 Y）${RESET}"
USER_INPUT=""
COUNTDOWN=$TIMEOUT

# 使用 read 超时（bash -t 支持；zsh 用 read -t）
# 兼容两者
OLD_IFS="$IFS"
if read -r -t "$TIMEOUT" -s -n 1 USER_INPUT 2>/dev/null || true; then
    :
fi
IFS="$OLD_IFS"
echo ""

if [[ "${USER_INPUT,,}" == "n" ]]; then
    echo ""
    echo -e "${YELLOW}  已取消安装。${RESET}"
    echo ""
    exit 0
fi

if [[ -z "$USER_INPUT" ]]; then
    echo -e "${GRAY}  （超时，自动继续）${RESET}"
fi
echo ""

# ─────────────────────────────────────────────
# STEP 5: 安装应用程序
# ─────────────────────────────────────────────
print_section "第四步：安装 AI 客户端"

install_app "lmstudio"    "LM Studio"   "LM Studio"   "LM-Studio-*.dmg"    "LM-Studio-*.pkg"
install_app "anythingllm" "AnythingLLM" "AnythingLLM" "AnythingLLM-*.dmg"  "AnythingLLM-*.pkg"
install_app "jan"         "Jan"          "Jan"         "Jan-*.dmg"           "Jan-*.pkg"
install_app "gpt4all"     "GPT4All"     "GPT4All"     "GPT4All-*.dmg"      "GPT4All-*.pkg"

# ─────────────────────────────────────────────
# STEP 6: 模型注入
# ─────────────────────────────────────────────
print_section "第五步：注入 AI 模型"

if ! $MODEL_AVAILABLE; then
    log_skip "未找到模型文件，跳过模型注入"
    add_result "模型注入" "跳过" "未找到 GGUF 文件"
else
    inject_model "lmstudio" "LM Studio"   "$LMS_MODEL_DIR"      "$MODEL_FILE_PATH"
    inject_model "jan"      "Jan"          "$JAN_MODEL_DIR"      "$MODEL_FILE_PATH"
    inject_model "gpt4all"  "GPT4All"     "$GPT4ALL_MODEL_DIR"  "$MODEL_FILE_PATH"
fi

# ─────────────────────────────────────────────
# STEP 7: 最终摘要
# ─────────────────────────────────────────────
print_section "安装摘要"
echo ""
printf "  ${GRAY}%-24s %-10s %s${RESET}\n" "组件" "状态" "详情"
echo -e "  ${GRAY}$(printf '─%.0s' {1..68})${RESET}"

SUCCESS_COUNT=0
SKIP_COUNT=0
FAIL_COUNT=0

while IFS='|' read -r res_app res_status res_detail; do
    row_color="$WHITE"
    case "$res_status" in
        "已安装"|"已注入") row_color="$GREEN";  ((SUCCESS_COUNT++)) || true ;;
        "已存在"|"跳过")   row_color="$YELLOW"; ((SKIP_COUNT++)) || true ;;
        "失败")            row_color="$RED";    ((FAIL_COUNT++)) || true ;;
    esac
    printf "  ${row_color}%-24s %-10s %s${RESET}\n" "$res_app" "$res_status" "$res_detail"
done < "$RESULTS_FILE"

# 错误汇总
if [[ -s "$ERRORS_FILE" ]]; then
    echo ""
    echo -e "${RED}  ⚠  安装过程中遇到以下错误：${RESET}"
    while IFS= read -r err_line; do
        echo -e "${RED}    • ${err_line}${RESET}"
    done < "$ERRORS_FILE"
fi

echo ""

# ─────────────────────────────────────────────
# STEP 8: 提供启动 LM Studio
# ─────────────────────────────────────────────
if [[ -d "/Applications/LM Studio.app" ]]; then
    echo -ne "  ${CYAN}是否现在启动 LM Studio？[Y/N] (10秒后自动跳过)${RESET}"
    LAUNCH_INPUT=""
    if read -r -t 10 -s -n 1 LAUNCH_INPUT 2>/dev/null || true; then :; fi
    echo ""
    if [[ "${LAUNCH_INPUT,,}" == "y" ]]; then
        log_step "启动 LM Studio..."
        open -a "LM Studio" 2>/dev/null && log_ok "LM Studio 已启动" \
            || log_err "启动 LM Studio 失败，请手动打开"
    fi
fi

echo ""
echo -e "${CYAN}$(printf '═%.0s' {1..62})${RESET}"
if [[ $FAIL_COUNT -eq 0 ]]; then
    FINAL_COLOR="$GREEN"
else
    FINAL_COLOR="$YELLOW"
fi
echo -e "${FINAL_COLOR}  🎉  安装完成  |  成功: ${SUCCESS_COUNT}  已存在/跳过: ${SKIP_COUNT}  失败: ${FAIL_COUNT}${RESET}"
echo -e "${CYAN}$(printf '═%.0s' {1..62})${RESET}"
echo ""

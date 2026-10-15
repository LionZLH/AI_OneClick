#Requires -Version 5.1
<#
.SYNOPSIS
    AI装机助手 · 离线安装器 (Windows)
.DESCRIPTION
    全自动离线安装 AI 客户端及本地大语言模型。
    需要在包含 apps/ 和 models/ 的离线安装包根目录下运行。
    建议以管理员身份运行以确保静默安装成功。
.VERSION
    1.0.0
.STRUCTURE
    offline-package/
      offline-install.ps1   ← 本脚本
      apps/
        lmstudio-setup.exe
        anythingllm-setup.exe
        jan-setup.exe
        gpt4all-setup.exe
      models/
        Qwen2.5-0.5B-Instruct-Q4_K_M.gguf
        Qwen2.5-1.5B-Instruct-Q4_K_M.gguf
        Qwen2.5-7B-Instruct-Q4_K_M.gguf
        Qwen2.5-14B-Instruct-Q4_K_M.gguf
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'   # 捕获错误但继续执行

# ─────────────────────────────────────────────
# 常量定义
# ─────────────────────────────────────────────
$SCRIPT_VERSION  = "1.0.0"
$SCRIPT_ROOT     = $PSScriptRoot
$APPS_DIR        = Join-Path $SCRIPT_ROOT "apps"
$MODELS_DIR      = Join-Path $SCRIPT_ROOT "models"
$INSTALL_TIMEOUT = 300   # 每个安装程序最长等待秒数

# 模型规格（从高到低，按RAM匹配）
$MODEL_TIERS = @(
    [PSCustomObject]@{ Label="14B"; File="Qwen2.5-14B-Instruct-Q4_K_M.gguf"; MinRamGB=32; SizeGB=9.0 }
    [PSCustomObject]@{ Label="7B";  File="Qwen2.5-7B-Instruct-Q4_K_M.gguf";  MinRamGB=16; SizeGB=4.7 }
    [PSCustomObject]@{ Label="1.5B";File="Qwen2.5-1.5B-Instruct-Q4_K_M.gguf";MinRamGB=8;  SizeGB=1.1 }
    [PSCustomObject]@{ Label="0.5B";File="Qwen2.5-0.5B-Instruct-Q4_K_M.gguf";MinRamGB=4;  SizeGB=0.4 }
)

# 应用配置：安装程序文件名、静默安装参数、安装后检测路径
$APP_CONFIGS = [ordered]@{
    "lmstudio"    = [PSCustomObject]@{
        Exe          = "lmstudio-setup.exe"
        SilentArgs   = "/S"
        DisplayName  = "LM Studio"
        # 检测是否已安装的注册表关键字 / 路径
        CheckPaths   = @(
            "$env:LOCALAPPDATA\Programs\LM-Studio\LM Studio.exe"
            "$env:ProgramFiles\LM-Studio\LM Studio.exe"
        )
    }
    "anythingllm" = [PSCustomObject]@{
        Exe          = "anythingllm-setup.exe"
        SilentArgs   = "/S"
        DisplayName  = "AnythingLLM"
        CheckPaths   = @(
            "$env:LOCALAPPDATA\Programs\AnythingLLM\AnythingLLM.exe"
            "$env:ProgramFiles\AnythingLLM\AnythingLLM.exe"
        )
    }
    "jan"         = [PSCustomObject]@{
        Exe          = "jan-setup.exe"
        SilentArgs   = "/S"
        DisplayName  = "Jan"
        CheckPaths   = @(
            "$env:LOCALAPPDATA\Programs\Jan\Jan.exe"
            "$env:ProgramFiles\Jan\Jan.exe"
        )
    }
    "gpt4all"     = [PSCustomObject]@{
        Exe          = "gpt4all-setup.exe"
        SilentArgs   = "/S"
        DisplayName  = "GPT4All"
        CheckPaths   = @(
            "$env:LOCALAPPDATA\Programs\GPT4All\GPT4All.exe"
            "$env:ProgramFiles\GPT4All\GPT4All.exe"
            "$env:LOCALAPPDATA\nomic.ai\GPT4All\GPT4All.exe"
        )
    }
}

# 模型注入目标路径（每个应用）
$MODEL_PATHS = @{
    "lmstudio" = "$env:USERPROFILE\.cache\lm-studio\models\lmstudio-community\Qwen2.5-Instruct-GGUF"
    "jan"      = "$env:APPDATA\jan\models"
    "gpt4all"  = "$env:LOCALAPPDATA\nomic.ai\GPT4All"
}

# ─────────────────────────────────────────────
# 全局安装结果跟踪
# ─────────────────────────────────────────────
$INSTALL_RESULTS  = [System.Collections.Generic.List[PSCustomObject]]::new()
$INSTALL_ERRORS   = [System.Collections.Generic.List[string]]::new()

# ─────────────────────────────────────────────
# 辅助函数
# ─────────────────────────────────────────────
function Write-Banner {
    $width = 62
    $line  = "═" * $width
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  🚀  AI装机助手 · 离线安装器  v$SCRIPT_VERSION" -ForegroundColor Cyan
    Write-Host "  自动安装 AI 客户端 + 本地大语言模型（Qwen2.5）" -ForegroundColor White
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  运行时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor DarkGray
    Write-Host "  安装包目录: $SCRIPT_ROOT" -ForegroundColor DarkGray
    Write-Host $line -ForegroundColor Cyan
    Write-Host ""
}

function Write-SectionTitle([string]$Title) {
    Write-Host ""
    Write-Host "▌ $Title" -ForegroundColor Yellow
    Write-Host ("  " + ("─" * 50)) -ForegroundColor DarkGray
}

function Write-Status([string]$Msg, [string]$Color = "White") {
    $ts = Get-Date -Format 'HH:mm:ss'
    Write-Host "  [$ts]  $Msg" -ForegroundColor $Color
}

function Write-Step([string]$Msg) { Write-Status "→ $Msg" "Cyan" }
function Write-OK([string]$Msg)   { Write-Status "✔ $Msg" "Green" }
function Write-Skip([string]$Msg) { Write-Status "⊘ $Msg" "DarkYellow" }
function Write-Warn([string]$Msg) { Write-Status "⚠ $Msg" "Yellow" }
function Write-Err([string]$Msg)  { Write-Status "✘ $Msg" "Red" }

function Add-Result([string]$App, [string]$Status, [string]$Detail) {
    $INSTALL_RESULTS.Add([PSCustomObject]@{
        App    = $App
        Status = $Status
        Detail = $Detail
    }) | Out-Null
}

function Add-Error([string]$Msg) {
    $INSTALL_ERRORS.Add($Msg) | Out-Null
}

# ─────────────────────────────────────────────
# 硬件检测（与 check-hardware.ps1 内联复用）
# ─────────────────────────────────────────────
function Get-IsOS64Bit    { return [System.Environment]::Is64BitOperatingSystem }

function Get-CpuName {
    try {
        return (Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1).Name.Trim()
    } catch {
        return "未知 CPU"
    }
}

function Get-HasAvx2 {
    try {
        Add-Type -TypeDefinition @"
using System.Runtime.Intrinsics.X86;
public class CpuFeat2 { public static bool HasAvx2() { return Avx2.IsSupported; } }
"@ -ErrorAction Stop
        return [CpuFeat2]::HasAvx2()
    } catch {
        return $true   # 无法检测时假定支持
    }
}

function Get-RamGB {
    try {
        $raw = (Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory
        return [math]::Round($raw / 1GB, 1)
    } catch { return 0 }
}

function Get-FreeDiskGB {
    try {
        $disk = Get-PSDrive -Name ($env:SystemDrive.TrimEnd(':')) -ErrorAction Stop
        return [math]::Round($disk.Free / 1GB, 1)
    } catch {
        try {
            $vol = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$env:SystemDrive'" -ErrorAction Stop
            return [math]::Round($vol.FreeSpace / 1GB, 1)
        } catch { return 0 }
    }
}

# ─────────────────────────────────────────────
# 检测应用是否已安装
# ─────────────────────────────────────────────
function Test-AppInstalled([PSCustomObject]$Config) {
    foreach ($path in $Config.CheckPaths) {
        if (Test-Path $path) { return $true }
    }
    # 额外检查注册表卸载列表
    $regPaths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($regPath in $regPaths) {
        $entry = Get-ItemProperty $regPath -ErrorAction SilentlyContinue |
                 Where-Object { $_.DisplayName -like "*$($Config.DisplayName)*" } |
                 Select-Object -First 1
        if ($null -ne $entry) { return $true }
    }
    return $false
}

# ─────────────────────────────────────────────
# STEP 1: 显示横幅
# ─────────────────────────────────────────────
Write-Banner

# ─────────────────────────────────────────────
# STEP 2: 硬件检查
# ─────────────────────────────────────────────
Write-SectionTitle "第一步：硬件检测"

$is64Bit = Get-IsOS64Bit
$cpuName = Get-CpuName
$hasAvx2 = Get-HasAvx2
$ramGB   = Get-RamGB
$freeGB  = Get-FreeDiskGB

Write-Host "  处理器：$cpuName" -ForegroundColor White
Write-Host "  内存：${ramGB} GB  |  系统盘可用：${freeGB} GB  |  AVX2：$(if($hasAvx2){'支持'}else{'不支持'})  |  64位：$(if($is64Bit){'是'}else{'否'})" -ForegroundColor White

# 校验硬件
$hwFail = $false
$hwMsgs = @()
if (-not $is64Bit)    { $hwFail = $true; $hwMsgs += "操作系统需为64位" }
if (-not $hasAvx2)    { $hwFail = $true; $hwMsgs += "CPU 需支持 AVX2 指令集" }
if ($ramGB   -lt 4)   { $hwFail = $true; $hwMsgs += "内存需 >= 4 GB（当前 ${ramGB} GB）" }
if ($freeGB  -lt 3)   { $hwFail = $true; $hwMsgs += "磁盘可用空间需 >= 3 GB（当前 ${freeGB} GB）" }

if ($hwFail) {
    Write-Host ""
    Write-Host "  ✘  硬件不满足要求，无法继续安装：" -ForegroundColor Red
    foreach ($m in $hwMsgs) { Write-Host "     • $m" -ForegroundColor Red }
    Write-Host ""
    exit 1
}
Write-OK "硬件检测通过"

# 根据 RAM 选择模型
$selectedModel = $null
foreach ($tier in $MODEL_TIERS) {
    if ($ramGB -ge $tier.MinRamGB) { $selectedModel = $tier; break }
}
if ($null -eq $selectedModel) { $selectedModel = $MODEL_TIERS[-1] }
Write-OK "已选择模型：Qwen2.5-$($selectedModel.Label)（$($selectedModel.File)）"

# ─────────────────────────────────────────────
# STEP 3: 扫描安装包内容
# ─────────────────────────────────────────────
Write-SectionTitle "第二步：检查安装包内容"

# 扫描 apps/
$foundApps = @()
if (Test-Path $APPS_DIR) {
    foreach ($key in $APP_CONFIGS.Keys) {
        $cfg     = $APP_CONFIGS[$key]
        $exePath = Join-Path $APPS_DIR $cfg.Exe
        if (Test-Path $exePath) {
            $foundApps += $key
            Write-OK "  发现：$($cfg.DisplayName)（$($cfg.Exe)）"
        } else {
            Write-Warn "  缺失：$($cfg.Exe) — 将跳过 $($cfg.DisplayName)"
        }
    }
} else {
    Write-Err "  apps/ 目录不存在：$APPS_DIR"
    Add-Error "apps/ 目录缺失，所有应用安装将被跳过"
}

# 检查选定模型文件
$modelFile = Join-Path $MODELS_DIR $selectedModel.File
$modelExists = Test-Path $modelFile
if ($modelExists) {
    Write-OK "  发现模型：$($selectedModel.File)"
} else {
    Write-Warn "  所选模型文件不存在：$($selectedModel.File)"
    Write-Warn "  将尝试降级到包内已有的模型..."
    # 尝试降级
    $modelExists = $false
    foreach ($tier in $MODEL_TIERS) {
        $tryFile = Join-Path $MODELS_DIR $tier.File
        if (Test-Path $tryFile) {
            $selectedModel = $tier
            $modelFile     = $tryFile
            $modelExists   = $true
            Write-OK "  降级为：$($selectedModel.File)"
            break
        }
    }
    if (-not $modelExists) {
        Write-Warn "  未找到任何可用模型文件，模型注入步骤将被跳过"
    }
}

# ─────────────────────────────────────────────
# STEP 4: 安装确认
# ─────────────────────────────────────────────
Write-SectionTitle "第三步：安装计划确认"
Write-Host ""
Write-Host "  即将安装以下内容：" -ForegroundColor White
foreach ($key in $foundApps) { Write-Host "    ✦ $($APP_CONFIGS[$key].DisplayName)" -ForegroundColor Cyan }
if ($modelExists) {
    Write-Host "    ✦ 模型 Qwen2.5-$($selectedModel.Label) → LM Studio / Jan / GPT4All" -ForegroundColor Cyan
}
Write-Host ""

# 倒计时10秒自动确认
$proceed = $false
$timeout = 10
Write-Host "  继续安装？[Y/N]  （$timeout 秒后自动确认 Y）" -ForegroundColor Yellow -NoNewline

# 使用非阻塞按键检测
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
while ($stopwatch.Elapsed.TotalSeconds -lt $timeout) {
    if ([Console]::KeyAvailable) {
        $key = [Console]::ReadKey($true)
        Write-Host ""
        if ($key.Key -eq 'N') {
            Write-Host ""
            Write-Host "  已取消安装。" -ForegroundColor Yellow
            Write-Host ""
            exit 0
        }
        $proceed = $true
        break
    }
    $remaining = $timeout - [int]$stopwatch.Elapsed.TotalSeconds
    Write-Host ("`r  继续安装？[Y/N]  （$remaining 秒后自动确认 Y）") -NoNewline -ForegroundColor Yellow
    Start-Sleep -Milliseconds 500
}
Write-Host ""
if (-not $proceed) { Write-Host "  （超时，自动继续）" -ForegroundColor DarkGray }
Write-Host ""

# ─────────────────────────────────────────────
# STEP 5: 安装应用程序
# ─────────────────────────────────────────────
Write-SectionTitle "第四步：安装 AI 客户端"

foreach ($key in $APP_CONFIGS.Keys) {
    $cfg     = $APP_CONFIGS[$key]
    $exePath = Join-Path $APPS_DIR $cfg.Exe

    if (-not (Test-Path $exePath)) {
        Write-Skip "$($cfg.DisplayName)：安装包不在 apps/ 目录中，已跳过"
        Add-Result $cfg.DisplayName "跳过" "安装包文件不存在"
        continue
    }

    # 检查是否已安装
    if (Test-AppInstalled $cfg) {
        Write-Skip "$($cfg.DisplayName)：已安装，跳过"
        Add-Result $cfg.DisplayName "已存在" "检测到已安装版本"
        continue
    }

    Write-Step "正在安装 $($cfg.DisplayName)..."
    try {
        $proc = Start-Process -FilePath $exePath `
                              -ArgumentList $cfg.SilentArgs `
                              -PassThru `
                              -Wait:$false `
                              -ErrorAction Stop
        # 等待安装程序完成
        if (-not $proc.WaitForExit($INSTALL_TIMEOUT * 1000)) {
            $proc.Kill()
            throw "安装超时（超过 $INSTALL_TIMEOUT 秒）"
        }
        if ($proc.ExitCode -ne 0) {
            throw "安装程序退出码：$($proc.ExitCode)"
        }
        Write-OK "$($cfg.DisplayName) 安装完成"
        Add-Result $cfg.DisplayName "已安装" "安装成功"
    } catch {
        $errMsg = "安装 $($cfg.DisplayName) 时出错：$_"
        Write-Err $errMsg
        Add-Error $errMsg
        Add-Result $cfg.DisplayName "失败" "$_"
    }
}

# ─────────────────────────────────────────────
# STEP 6: 模型注入
# ─────────────────────────────────────────────
Write-SectionTitle "第五步：注入 AI 模型"

if (-not $modelExists) {
    Write-Skip "未找到模型文件，跳过模型注入"
    Add-Result "模型注入" "跳过" "未找到 GGUF 文件"
} else {
    # 注入 LM Studio
    try {
        Write-Step "注入模型到 LM Studio..."
        $lmsPath = $MODEL_PATHS["lmstudio"]
        if (-not (Test-Path $lmsPath)) {
            New-Item -ItemType Directory -Path $lmsPath -Force | Out-Null
        }
        $destFile = Join-Path $lmsPath $selectedModel.File
        # 同尺寸则跳过
        if ((Test-Path $destFile) -and
            ((Get-Item $destFile).Length -eq (Get-Item $modelFile).Length)) {
            Write-Skip "LM Studio：模型文件已存在且大小相同，跳过"
            Add-Result "LM Studio 模型" "已存在" $destFile
        } else {
            Write-Step "复制 $($selectedModel.File) → $lmsPath"
            Copy-Item -Path $modelFile -Destination $destFile -Force -ErrorAction Stop
            Write-OK "LM Studio 模型注入完成"
            Add-Result "LM Studio 模型" "已注入" $destFile
        }
    } catch {
        $errMsg = "LM Studio 模型注入失败：$_"
        Write-Err $errMsg
        Add-Error $errMsg
        Add-Result "LM Studio 模型" "失败" "$_"
    }

    # 注入 Jan（仅当 Jan 成功安装或已存在时）
    $janResult = $INSTALL_RESULTS | Where-Object { $_.App -eq "Jan" } | Select-Object -First 1
    if ($null -ne $janResult -and $janResult.Status -in @("已安装","已存在")) {
        try {
            Write-Step "注入模型到 Jan..."
            $janPath = $MODEL_PATHS["jan"]
            # Jan 要求每个模型在独立子目录中，子目录名 = 模型文件名（不含扩展名）
            $modelId     = [System.IO.Path]::GetFileNameWithoutExtension($selectedModel.File)
            $janModelDir = Join-Path $janPath $modelId
            if (-not (Test-Path $janModelDir)) {
                New-Item -ItemType Directory -Path $janModelDir -Force | Out-Null
            }
            $destFile = Join-Path $janModelDir $selectedModel.File
            if ((Test-Path $destFile) -and
                ((Get-Item $destFile).Length -eq (Get-Item $modelFile).Length)) {
                Write-Skip "Jan：模型文件已存在且大小相同，跳过"
                Add-Result "Jan 模型" "已存在" $destFile
            } else {
                Copy-Item -Path $modelFile -Destination $destFile -Force -ErrorAction Stop
                Write-OK "Jan 模型注入完成"
                Add-Result "Jan 模型" "已注入" $destFile
            }
        } catch {
            $errMsg = "Jan 模型注入失败：$_"
            Write-Err $errMsg
            Add-Error $errMsg
            Add-Result "Jan 模型" "失败" "$_"
        }
    } else {
        Write-Skip "Jan 未安装，跳过 Jan 模型注入"
    }
}

# ─────────────────────────────────────────────
# STEP 7: 最终摘要
# ─────────────────────────────────────────────
Write-SectionTitle "安装摘要"
Write-Host ""
Write-Host ("  {0,-22} {1,-10} {2}" -f "组件", "状态", "详情") -ForegroundColor DarkGray
Write-Host "  $("─" * 68)" -ForegroundColor DarkGray

foreach ($r in $INSTALL_RESULTS) {
    $color = switch ($r.Status) {
        "已安装" { "Green" }
        "已存在" { "DarkYellow" }
        "已注入" { "Green" }
        "跳过"   { "DarkGray" }
        "失败"   { "Red" }
        default  { "White" }
    }
    Write-Host ("  {0,-22} {1,-10} {2}" -f $r.App, $r.Status, $r.Detail) -ForegroundColor $color
}

# 错误汇总
if ($INSTALL_ERRORS.Count -gt 0) {
    Write-Host ""
    Write-Host "  ⚠  安装过程中遇到以下错误：" -ForegroundColor Red
    foreach ($e in $INSTALL_ERRORS) {
        Write-Host "    • $e" -ForegroundColor Red
    }
}

Write-Host ""

# ─────────────────────────────────────────────
# STEP 8: 提供启动 LM Studio
# ─────────────────────────────────────────────
$lmsResult = $INSTALL_RESULTS | Where-Object { $_.App -eq "LM Studio" } | Select-Object -First 1
if ($null -ne $lmsResult -and $lmsResult.Status -in @("已安装","已存在")) {
    Write-Host "  是否现在启动 LM Studio？[Y/N] (10秒后自动跳过)" -ForegroundColor Cyan -NoNewline
    $launch = $false
    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw2.Elapsed.TotalSeconds -lt 10) {
        if ([Console]::KeyAvailable) {
            $key = [Console]::ReadKey($true)
            Write-Host ""
            if ($key.Key -eq 'Y') { $launch = $true }
            break
        }
        $rem = 10 - [int]$sw2.Elapsed.TotalSeconds
        Write-Host ("`r  是否现在启动 LM Studio？[Y/N] ($rem 秒后自动跳过)") -NoNewline -ForegroundColor Cyan
        Start-Sleep -Milliseconds 500
    }
    Write-Host ""

    if ($launch) {
        $lmsExePaths = @(
            "$env:LOCALAPPDATA\Programs\LM-Studio\LM Studio.exe"
            "$env:ProgramFiles\LM-Studio\LM Studio.exe"
        )
        $lmsExe = $lmsExePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($lmsExe) {
            try {
                Start-Process -FilePath $lmsExe -ErrorAction Stop
                Write-OK "LM Studio 已启动"
            } catch {
                Write-Err "启动 LM Studio 失败：$_"
            }
        } else {
            Write-Warn "找不到 LM Studio 可执行文件，请手动启动"
        }
    }
}

$successCount = ($INSTALL_RESULTS | Where-Object { $_.Status -in @("已安装","已注入") }).Count
$skipCount    = ($INSTALL_RESULTS | Where-Object { $_.Status -in @("已存在","跳过") }).Count
$failCount    = ($INSTALL_RESULTS | Where-Object { $_.Status -eq "失败" }).Count

Write-Host ""
Write-Host "═" * 62 -ForegroundColor Cyan
$finalColor = if ($failCount -eq 0) { "Green" } else { "Yellow" }
Write-Host ("  🎉  安装完成  |  成功: $successCount  已存在/跳过: $skipCount  失败: $failCount") -ForegroundColor $finalColor
Write-Host "═" * 62 -ForegroundColor Cyan
Write-Host ""

#Requires -Version 5.1
<#
.SYNOPSIS
    AI装机助手 - 硬件检测工具 (Windows)
.DESCRIPTION
    在下载离线安装包之前运行此脚本，检测当前硬件是否满足要求，
    并推荐适合您硬件的安装包版本。
    无需管理员权限，只读操作，不会修改任何系统文件。
.VERSION
    1.0.0
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─────────────────────────────────────────────
# 常量定义
# ─────────────────────────────────────────────
$SCRIPT_VERSION = "1.0.0"

# 模型规格表
$MODEL_TIERS = @(
    [PSCustomObject]@{ Label="14B"; File="Qwen2.5-14B-Instruct-Q4_K_M.gguf"; MinRamGB=32; SizeGB=9.0;  Package="AI装机-完整版（32GB+内存）" }
    [PSCustomObject]@{ Label="7B";  File="Qwen2.5-7B-Instruct-Q4_K_M.gguf";  MinRamGB=16; SizeGB=4.7;  Package="AI装机-标准版（16GB+内存）" }
    [PSCustomObject]@{ Label="1.5B";File="Qwen2.5-1.5B-Instruct-Q4_K_M.gguf";MinRamGB=8;  SizeGB=1.1;  Package="AI装机-基础版（8-16GB内存）" }
    [PSCustomObject]@{ Label="0.5B";File="Qwen2.5-0.5B-Instruct-Q4_K_M.gguf";MinRamGB=4;  SizeGB=0.4;  Package="AI装机-入门版（4-8GB内存）" }
)

# 各安装包所需磁盘空间（GB，含模型+应用+缓冲）
$PACKAGE_DISK = @{
    "AI装机-入门版（4-8GB内存）"   = 3.5
    "AI装机-基础版（8-16GB内存）"  = 4.0
    "AI装机-标准版（16GB+内存）"   = 8.0
    "AI装机-完整版（32GB+内存）"   = 13.0
}

$MIN_RAM_GB    = 4
$MIN_DISK_GB   = 3

# ─────────────────────────────────────────────
# 辅助函数
# ─────────────────────────────────────────────
function Write-Header {
    $width = 60
    $line  = "─" * $width
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  🖥️  AI装机助手 · 硬件检测工具  v$SCRIPT_VERSION" -ForegroundColor Cyan
    Write-Host "  Windows 版  |  只读模式，不修改任何系统设置" -ForegroundColor DarkGray
    Write-Host $line -ForegroundColor Cyan
    Write-Host ""
}

function Write-SectionTitle([string]$Title) {
    Write-Host ""
    Write-Host "▌ $Title" -ForegroundColor Yellow
    Write-Host ("  " + ("─" * 40)) -ForegroundColor DarkGray
}

function Write-CheckRow([string]$Label, [string]$Value, [bool]$Pass, [string]$Note="") {
    $icon   = if ($Pass) { "✔" } else { "✘" }
    $color  = if ($Pass) { "Green" } else { "Red" }
    $padded = $Label.PadRight(18)
    Write-Host "  $icon  $padded $Value" -ForegroundColor $color -NoNewline
    if ($Note) { Write-Host "  ($Note)" -ForegroundColor DarkGray } else { Write-Host "" }
}

function Write-InfoRow([string]$Label, [string]$Value) {
    $padded = $Label.PadRight(18)
    Write-Host "  ·  $padded $Value" -ForegroundColor White
}

# ─────────────────────────────────────────────
# 硬件检测函数
# ─────────────────────────────────────────────
function Get-IsOS64Bit {
    return [System.Environment]::Is64BitOperatingSystem
}

function Get-CpuName {
    try {
        $cpu = Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop | Select-Object -First 1
        return $cpu.Name.Trim()
    } catch {
        try {
            return (Get-WmiObject Win32_Processor | Select-Object -First 1).Name.Trim()
        } catch {
            return "未知 CPU"
        }
    }
}

function Get-HasAvx2 {
    # 尝试通过 .NET 运行时 Intrinsics 检测 AVX2
    try {
        Add-Type -TypeDefinition @"
using System.Runtime.Intrinsics.X86;
public class CpuFeatureCheck {
    public static bool HasAvx2() { return Avx2.IsSupported; }
}
"@ -ErrorAction Stop
        return [CpuFeatureCheck]::HasAvx2()
    } catch {
        # Add-Type 失败（可能是老版本 .NET），默认假定支持
        return $true
    }
}

function Get-RamGB {
    try {
        $cs  = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop
        $raw = $cs.TotalPhysicalMemory
        return [math]::Round($raw / 1GB, 1)
    } catch {
        try {
            $raw = (Get-WmiObject Win32_ComputerSystem).TotalPhysicalMemory
            return [math]::Round($raw / 1GB, 1)
        } catch {
            return 0
        }
    }
}

function Get-FreeDiskGB {
    try {
        $sysDrive = $env:SystemDrive
        $disk = Get-PSDrive -Name ($sysDrive.TrimEnd(':')) -ErrorAction Stop
        return [math]::Round($disk.Free / 1GB, 1)
    } catch {
        try {
            $vol = Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DeviceID='$env:SystemDrive'" -ErrorAction Stop
            return [math]::Round($vol.FreeSpace / 1GB, 1)
        } catch {
            return 0
        }
    }
}

function Get-GpuInfo {
    $results = @()
    try {
        $gpus = Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop
        foreach ($gpu in $gpus) {
            $vramMB = [math]::Round($gpu.AdapterRAM / 1MB, 0)
            $vramStr = if ($vramMB -gt 0) { "${vramMB} MB" } else { "未知" }
            $results += "$($gpu.Name.Trim())  [VRAM: $vramStr]"
        }
    } catch {
        $results += "无法读取 GPU 信息"
    }
    return $results
}

# ─────────────────────────────────────────────
# 主逻辑
# ─────────────────────────────────────────────
Write-Header

# ── 1. 收集硬件信息 ──
Write-SectionTitle "正在检测硬件..."
Write-Host ""

$is64Bit  = Get-IsOS64Bit
$cpuName  = Get-CpuName
$hasAvx2  = Get-HasAvx2
$ramGB    = Get-RamGB
$freeGB   = Get-FreeDiskGB
$gpuLines = Get-GpuInfo

# ── 2. 显示硬件摘要 ──
Write-SectionTitle "硬件信息汇总"
Write-InfoRow "操作系统"     "$(if($is64Bit){'64位 Windows'}else{'32位 Windows'})"
Write-InfoRow "处理器"       $cpuName
Write-InfoRow "AVX2 支持"    "$(if($hasAvx2){'支持'}else{'不支持'})"
Write-InfoRow "内存容量"     "${ramGB} GB"
Write-InfoRow "系统盘可用"   "${freeGB} GB"
foreach ($g in $gpuLines) {
    Write-InfoRow "显卡" $g
}

# ── 3. 需求检查 ──
Write-SectionTitle "最低硬件需求检查"

$checkFailed = $false
$failReasons = @()

# 64位检查
$pass64 = $is64Bit
Write-CheckRow "64位系统" (if($is64Bit){"✓ 64位"}else{"✗ 32位"}) $pass64 "必须为64位操作系统"
if (-not $pass64) {
    $checkFailed = $true
    $failReasons += "❌ 操作系统必须为64位（当前为32位）"
}

# AVX2 检查
$passAvx2 = $hasAvx2
Write-CheckRow "AVX2 指令集" (if($hasAvx2){"支持"}else{"不支持"}) $passAvx2 "现代AI推理必需"
if (-not $passAvx2) {
    $checkFailed = $true
    $failReasons += "❌ CPU 不支持 AVX2 指令集（2013年之前的老CPU不支持）"
}

# RAM 检查
$passRam = ($ramGB -ge $MIN_RAM_GB)
Write-CheckRow "内存容量" "${ramGB} GB" $passRam "最低需要 ${MIN_RAM_GB} GB"
if (-not $passRam) {
    $checkFailed = $true
    $failReasons += "❌ 内存不足：当前 ${ramGB} GB，最低需要 ${MIN_RAM_GB} GB"
}

# 磁盘检查
$passDisk = ($freeGB -ge $MIN_DISK_GB)
Write-CheckRow "系统盘可用空间" "${freeGB} GB" $passDisk "最低需要 ${MIN_DISK_GB} GB"
if (-not $passDisk) {
    $checkFailed = $true
    $failReasons += "❌ 磁盘空间不足：当前可用 ${freeGB} GB，最低需要 ${MIN_DISK_GB} GB"
}

# ── 4. 检查失败则退出 ──
if ($checkFailed) {
    Write-Host ""
    Write-Host "─" * 60 -ForegroundColor Red
    Write-Host "  ✘  硬件不满足最低要求，无法运行AI本地模型" -ForegroundColor Red
    Write-Host "─" * 60 -ForegroundColor Red
    foreach ($reason in $failReasons) {
        Write-Host "  $reason" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  请升级硬件后重新检测，或考虑使用在线AI服务。" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

# ── 5. 推荐模型规格 ──
$selected = $null
foreach ($tier in $MODEL_TIERS) {
    if ($ramGB -ge $tier.MinRamGB) {
        $selected = $tier
        break
    }
}

if ($null -eq $selected) {
    # 兜底（理论上不会到达，因为上面已经检查了最低4GB）
    $selected = $MODEL_TIERS[-1]
}

Write-SectionTitle "推荐配置"
Write-Host ""
Write-Host "  ┌─────────────────────────────────────────────────────┐" -ForegroundColor Green
Write-Host "  │  🎯  根据您的内存 (${ramGB} GB) 推荐以下配置：" -ForegroundColor Green
Write-Host "  │" -ForegroundColor Green
Write-Host "  │  📦  安装包名称：$($selected.Package)" -ForegroundColor Green
Write-Host "  │  🤖  模型版本：  Qwen2.5-$($selected.Label) (Q4_K_M量化)" -ForegroundColor Green
Write-Host "  │  📄  模型文件：  $($selected.File)" -ForegroundColor Green
Write-Host "  │  💾  模型大小：  $($selected.SizeGB) GB" -ForegroundColor Green
Write-Host "  └─────────────────────────────────────────────────────┘" -ForegroundColor Green

# ── 6. 全部安装包磁盘需求表 ──
Write-SectionTitle "各版本安装包所需磁盘空间参考"
Write-Host ""
Write-Host ("  {0,-30} {1,-12} {2,-10}" -f "安装包名称", "所需空间(GB)", "当前可用") -ForegroundColor DarkGray
Write-Host "  $("─" * 55)" -ForegroundColor DarkGray
foreach ($tier in ($MODEL_TIERS | Sort-Object MinRamGB)) {
    $pkg     = $tier.Package
    $need    = $PACKAGE_DISK[$pkg]
    $canFit  = ($freeGB -ge $need)
    $fitMark = if ($canFit) { "✔ 可用" } else { "✘ 不足" }
    $color   = if ($canFit) { "White" } else { "DarkGray" }
    $isCur   = if ($pkg -eq $selected.Package) { " ◀ 推荐" } else { "" }
    Write-Host ("  {0,-30} {1,-12} {2,-10}{3}" -f $pkg, $need, $fitMark, $isCur) -ForegroundColor $color
}

Write-Host ""
Write-Host "─" * 60 -ForegroundColor Cyan
Write-Host "  ✅  硬件检测通过！请下载上方推荐的安装包。" -ForegroundColor Cyan
Write-Host "─" * 60 -ForegroundColor Cyan
Write-Host ""

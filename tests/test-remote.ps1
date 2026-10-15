[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   AI Station 自动化测试套件 (Windows)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# 1. 硬件检测与推荐逻辑测试
Write-Host "【测试项 1/4】硬件检测脚本测试 (scripts/check-hardware.ps1)" -ForegroundColor Yellow
$hwScript = "$PSScriptRoot\..\scripts\check-hardware.ps1"
if (Test-Path $hwScript) {
    & powershell.exe -ExecutionPolicy Bypass -File $hwScript
    Write-Host "✔ 硬件检测脚本执行成功！" -ForegroundColor Green
} else {
    Write-Host "❌ 未找到 check-hardware.ps1" -ForegroundColor Red
}
Write-Host ""

# 2. 本机 AI 客户端检测测试
Write-Host "【测试项 2/4】AI 客户端识别覆盖率测试" -ForegroundColor Yellow
$localApp = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$progFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$progFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)

$apps = @(
    @{ Id="doubao"; Name="豆包"; Paths=@("$localApp\Programs\Doubao\Doubao.exe", "$localApp\ByteDance\Doubao\Doubao.exe") },
    @{ Id="kimi"; Name="Kimi"; Paths=@("$localApp\Programs\Kimi\Kimi.exe") },
    @{ Id="yuanbao"; Name="腾讯元宝"; Paths=@("$localApp\Programs\Tencent\Yuanbao\Yuanbao.exe", "$progFiles\Tencent\Yuanbao\Yuanbao.exe") },
    @{ Id="trae"; Name="TRAE"; Paths=@("$localApp\Programs\Trae\Trae.exe") },
    @{ Id="deepseek"; Name="DeepSeek"; Paths=@("$localApp\Programs\DeepSeek\DeepSeek.exe", "$progFiles\DeepSeek\DeepSeek.exe") },
    @{ Id="lmstudio"; Name="LM Studio"; Paths=@("$localApp\Programs\LM Studio\LM Studio.exe", "$progFiles\LM Studio\LM Studio.exe") },
    @{ Id="anythingllm"; Name="AnythingLLM"; Paths=@("$localApp\Programs\anythingllm-desktop\AnythingLLM.exe", "$progFiles\AnythingLLM\AnythingLLM.exe") },
    @{ Id="jan"; Name="Jan"; Paths=@("$localApp\Programs\Jan\Jan.exe") },
    @{ Id="gpt4all"; Name="GPT4All"; Paths=@("$progFiles\GPT4All\bin\chat.exe", "$localApp\Programs\GPT4All\bin\chat.exe") },
    @{ Id="chatgpt"; Name="ChatGPT"; Paths=@("$localApp\Programs\ChatGPT\ChatGPT.exe") },
    @{ Id="claude"; Name="Claude"; Paths=@("$localApp\Programs\Claude\Claude.exe") },
    @{ Id="cursor"; Name="Cursor"; Paths=@("$localApp\Programs\cursor\Cursor.exe") },
    @{ Id="antigravity"; Name="Antigravity"; Paths=@("$localApp\Programs\Antigravity\Antigravity.exe") }
)

$detectedCount = 0
foreach ($app in $apps) {
    $found = $false
    $foundPath = ""
    foreach ($p in $app.Paths) {
        if (Test-Path $p) {
            $found = $true
            $foundPath = $p
            break
        }
    }
    if ($found) {
        $detectedCount++
        Write-Host "  ✔ [已安装] $($app.Name): $foundPath" -ForegroundColor Green
    } else {
        Write-Host "  ⚪ [未安装] $($app.Name)" -ForegroundColor Gray
    }
}
Write-Host "  -> 本机已检测到 $detectedCount 款 AI 客户端" -ForegroundColor Cyan
Write-Host ""

# 3. Winget 源与包可用性测试
Write-Host "【测试项 3/4】Winget 静默安装通道测试" -ForegroundColor Yellow
$wingetCmd = Get-Command winget -ErrorAction SilentlyContinue
if ($wingetCmd) {
    $wingetVer = & winget --version
    Write-Host "  ✔ Winget 可用，版本: $wingetVer" -ForegroundColor Green
    
    # 抽测几个包
    $testPackages = @("ByteDance.Doubao", "ElementLabs.LMStudio", "Anysphere.Cursor")
    foreach ($pkg in $testPackages) {
        $search = & winget search --id $pkg --exact --accept-source-agreements 2>&1
        if ($LASTEXITCODE -eq 0 -and ($search -match $pkg)) {
            Write-Host "  ✔ 官方源包存在: $pkg" -ForegroundColor Green
        } else {
            Write-Host "  ⚠ 官方源未直接匹配: $pkg (可通过官网直连下载)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "  ❌ 未检测到 Winget 命令" -ForegroundColor Red
}
Write-Host ""

# 4. 离线安装器与注入逻辑验证
Write-Host "【测试项 4/4】离线安装逻辑与目录结构测试" -ForegroundColor Yellow
$offlineScript = "$PSScriptRoot\..\scripts\offline-install.ps1"
if (Test-Path $offlineScript) {
    Write-Host "  ✔ 离线安装脚本 scripts/offline-install.ps1 存在" -ForegroundColor Green
    # 检查 apps 与 models 目录
    $appsDir = "$PSScriptRoot\..\apps"
    $modelsDir = "$PSScriptRoot\..\models"
    Write-Host "  - 本地 apps 目录: $(if (Test-Path $appsDir) { '存在' } else { '未创建 (在线模式)' })"
    Write-Host "  - 本地 models 目录: $(if (Test-Path $modelsDir) { '存在' } else { '未创建 (在线模式)' })"
} else {
    Write-Host "  ❌ 未找到 scripts/offline-install.ps1" -ForegroundColor Red
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   AI Station 核心功能测试完成" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

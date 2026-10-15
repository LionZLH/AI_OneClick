# 探查 LM Studio 命令行工具 lms 的位置和用法（用于确认“在 LM Studio 中下载模型”的实现）
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
winget install --id ElementLabs.LMStudio --exact --source winget --silent --accept-source-agreements --accept-package-agreements --disable-interactivity | Select-Object -Last 3
$roots = @("$env:LOCALAPPDATA\Programs\LM Studio", "$env:ProgramFiles\LM Studio", "$env:USERPROFILE\.lmstudio", "$env:USERPROFILE\.cache\lm-studio")
foreach ($r in $roots) { if (Test-Path $r) { Get-ChildItem $r -Recurse -Filter 'lms*.exe' -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "找到：$($_.FullName)" } } }
$app = Get-ChildItem "$env:LOCALAPPDATA\Programs\LM Studio", "$env:ProgramFiles\LM Studio" -Filter 'LM Studio.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($app) {
    Write-Host "启动 $($app.FullName) 20 秒"
    $p = Start-Process $app.FullName -PassThru; Start-Sleep 20
    Get-Process | Where-Object { $_.Path -like '*LM Studio*' -or $_.Path -like '*lmstudio*' } | Stop-Process -Force -ErrorAction SilentlyContinue
}
foreach ($r in $roots) { if (Test-Path $r) { Get-ChildItem $r -Recurse -Filter 'lms*.exe' -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "启动后找到：$($_.FullName)" } } }
$lms = "$env:USERPROFILE\.lmstudio\bin\lms.exe"
if (Test-Path $lms) {
    & $lms --help 2>&1 | Select-Object -First 30
    & $lms get --help 2>&1 | Select-Object -First 40
    Write-Host '== 试下载 qwen/qwen3-0.6b --yes'
    $j = Start-Job { & $using:lms get qwen/qwen3-0.6b --yes 2>&1 }
    if (Wait-Job $j -Timeout 300) { Receive-Job $j | Select-Object -Last 15 } else { Write-Host '超时'; Receive-Job $j | Select-Object -Last 15; Stop-Job $j }
}
exit 0

# 绿色版：数据写在 exe 旁边、不写 %LOCALAPPDATA%；桌面快捷方式；程序移动后开机自启自动修正；清除数据后不留痕迹
param([string]$Exe = "$PSScriptRoot\..\dist\AIStation.exe")
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$green = Join-Path $env:RUNNER_TEMP 'green'
New-Item -ItemType Directory -Force -Path $green | Out-Null
$app = Join-Path $green 'AIStation.exe'
Copy-Item $Exe $app -Force
$data = Join-Path $green 'AIStation-data'
$legacy = Join-Path $env:LOCALAPPDATA 'AIStation'
if (Test-Path $legacy) { Remove-Item $legacy -Recurse -Force }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$lnk = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'AI Station.lnk'
if (Test-Path $lnk) { Remove-Item $lnk -Force }

# 模拟“程序被移动过”：自启登记指向旧位置
New-ItemProperty -Path $runKey -Name 'AIStation' -Value '"C:\old-place\AIStation.exe" --minimized' -PropertyType String -Force | Out-Null

Write-Host '== 正常启动绿色版'
$p = Start-Process $app -PassThru
$inst = Join-Path $env:TEMP 'AIStation-instance.json'
for ($i = 0; $i -lt 30 -and -not (Test-Path $inst); $i++) { Start-Sleep 1 }
Start-Sleep 3
$info = Get-Content $inst -Raw | ConvertFrom-Json
$base = "http://127.0.0.1:$($info.port)/api"; $h = @{ 'X-AIStation-Token' = $info.token }
$s = Invoke-RestMethod -Uri "$base/state" -Headers $h
Write-Host "数据目录：$($s.system.dataDir)（放在程序旁边：$($s.system.dataNextToExe)）"
if (-not $s.system.dataNextToExe -or $s.system.dataDir -ne $data) { throw '数据没有放在程序旁边' }
if (-not (Test-Path (Join-Path $data 'logs\aistation.log'))) { throw '程序旁边没有日志' }
if (Test-Path $legacy) { throw '仍然写入了 %LOCALAPPDATA%\AIStation' }

$run = (Get-ItemProperty $runKey).AIStation
Write-Host "开机自启：$run"
if ($run -notlike "*$app*") { throw '开机自启没有改到新位置' }

$null = Invoke-RestMethod -Method Post -Uri "$base/desktop-shortcut" -Headers $h -ContentType 'application/json' -Body '{}'
$target = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk).TargetPath
Write-Host "桌面快捷方式：$target"
if ($target -ne $app) { throw '桌面快捷方式指向错误' }

Write-Host '== 清除数据并退出'
$null = Invoke-RestMethod -Method Post -Uri "$base/clear-data" -Headers $h -ContentType 'application/json' -Body '{}'
for ($i = 0; $i -lt 60 -and (Test-Path $data); $i++) { Start-Sleep 2 }
if (-not $p.HasExited) { Start-Sleep 3 }
Write-Host "进程已退出：$($p.HasExited)；数据目录仍在：$(Test-Path $data)"
if (-not $p.HasExited) { throw '清除数据后程序没有退出' }
if (Test-Path $data) { throw '数据目录没有删除' }
if ((Get-ItemProperty $runKey).PSObject.Properties.Name -contains 'AIStation') { throw '开机自启没有关闭' }
if (Test-Path $lnk) { throw '桌面快捷方式没有删除' }
if (Test-Path $legacy) { throw '在 %LOCALAPPDATA% 留下了文件' }
$left = Get-ChildItem $green | ForEach-Object Name
Write-Host "程序文件夹里剩下：$($left -join ', ')"
if (($left | Where-Object { $_ -ne 'AIStation.exe' }).Count -gt 0) { throw '程序文件夹里还有残留' }
Write-Host '绿色版测试全部通过'

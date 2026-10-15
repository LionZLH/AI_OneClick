# 以正常方式启动 AIStation.exe（托盘 + Edge 应用窗口），截取整个屏幕，确认窗口能打开。
param([string]$Exe = "$PSScriptRoot\..\dist\AIStation.exe", [string]$Out = "$PSScriptRoot\..\test-results")
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$p = Start-Process $Exe -PassThru
Start-Sleep 15
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$bmp.Save((Join-Path $Out 'window-smoke.png'))
$edge = Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" | Where-Object { $_.CommandLine -match 'AIStation' }
Write-Host "AIStation 进程存活：$(-not $p.HasExited)；Edge 应用窗口进程：$(@($edge).Count)"
$edge | Select-Object -First 1 | ForEach-Object { Write-Host $_.CommandLine }
$titles = @(Get-Process msedge -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle } | ForEach-Object { $_.MainWindowTitle })
Write-Host "Edge 窗口标题：$($titles -join ' | ')"
$alive = -not $p.HasExited
$edge | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
if (-not $alive) { Write-Host 'AIStation 进程意外退出'; exit 1 }
if (@($edge).Count -eq 0) { Write-Host '没有打开 Edge 应用窗口'; exit 1 }
if (-not ($titles -contains 'AI Station')) { Write-Host '没有找到标题为 AI Station 的窗口'; exit 1 }
Write-Host '窗口冒烟测试通过'
exit 0

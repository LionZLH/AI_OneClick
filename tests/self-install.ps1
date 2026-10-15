# 安装到本机 → 检查程序、快捷方式、系统“应用”列表登记 → 从本机卸载 AI Station
param([string]$Exe = "$PSScriptRoot\..\dist\AIStation.exe")
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$dir = Join-Path $env:LOCALAPPDATA 'Programs\AIStation'
$installed = Join-Path $dir 'AIStation.exe'
$links = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'AI Station.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'AI Station.lnk')
)
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AIStation'

Write-Host '== 安装到本机'
$p = Start-Process $Exe -ArgumentList '--install-self' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "安装返回 $($p.ExitCode)" }
if (-not (Test-Path $installed)) { throw '没有复制到程序目录' }
foreach ($l in $links) {
    if (-not (Test-Path $l)) { throw "缺少快捷方式 $l" }
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($l).TargetPath
    Write-Host "快捷方式：$l -> $target"
    if ($target -ne $installed) { throw '快捷方式指向错误' }
}
$reg = Get-ItemProperty $key
Write-Host "应用列表：$($reg.DisplayName) $($reg.DisplayVersion)，卸载命令：$($reg.UninstallString)"
if ($reg.DisplayName -ne 'AI Station') { throw '没有登记到系统应用列表' }

Write-Host '== 从本机卸载'
$p = Start-Process $installed -ArgumentList '--uninstall-self', '--quiet' -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "卸载返回 $($p.ExitCode)" }
Start-Sleep 6
foreach ($l in $links) { if (Test-Path $l) { throw "快捷方式没有删除：$l" } }
if (Test-Path $key) { throw '系统应用列表中的登记没有删除' }
if (Test-Path $dir) { throw '程序目录没有删除' }
Write-Host '安装到本机 / 卸载 全部通过'

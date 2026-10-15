# 在真实 Windows 上通过 AI Station 的接口安装 → 识别 → 启动 → 卸载 一个软件。
param([string]$AppId = 'jan', [string]$Exe = "$PSScriptRoot\..\dist\AIStation.exe")
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$port = 18790; $token = 'ci-token'
$data = Join-Path $env:RUNNER_TEMP 'aistation-real'
$proc = Start-Process $Exe -ArgumentList '--headless', '--port', $port, '--token', $token, '--data-dir', $data -PassThru
$base = "http://127.0.0.1:$port/api"
$h = @{ 'X-AIStation-Token' = $token }
function Post($path, $body) { Invoke-RestMethod -Method Post -Uri "$base/$path" -Headers $h -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5 -Compress))) }
function State { Invoke-RestMethod -Uri "$base/state" -Headers $h }
Add-Type @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class Win {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public static List<string> List() {
    var r = new List<string>();
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      uint pid; GetWindowThreadProcessId(h, out pid);
      var sb = new StringBuilder(512); GetWindowText(h, sb, 512);
      string name = ""; try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch {}
      if (!name.StartsWith("Un_", StringComparison.OrdinalIgnoreCase)) return true;
      var texts = new List<string>();
      EnumChildWindows(h, (c, l2) => { var t = new StringBuilder(512); GetWindowText(c, t, 512); if (t.Length > 0) texts.Add(t.ToString()); return true; }, IntPtr.Zero);
      r.Add(name + " | " + sb + " | " + string.Join(" / ", texts));
      return true;
    }, IntPtr.Zero);
    return r;
  }
}
'@
function WaitJobs {
    for ($i = 0; $i -lt 360; $i++) {
        Start-Sleep 5
        $s = Invoke-RestMethod -Uri "$base/jobs" -Headers $h
        $active = @($s.jobs | Where-Object { $_.status -eq 'queued' -or $_.status -eq 'running' })
        if ($active.Count -eq 0 -and -not $s.scanning) { return $s }
        $j = $active[0]; Write-Host ("  {0} {1} {2} {3}" -f $j.action, $j.status, $j.message, $j.progress)
        if ($j.action -eq 'uninstall') { [Win]::List() | ForEach-Object { Write-Host "    窗口：$_" } }
    }
    throw '任务超时'
}
try {
    for ($i = 0; $i -lt 40; $i++) { try { $null = State; break } catch { Start-Sleep 1 } }
    for ($i = 0; $i -lt 60; $i++) { $s = State; if ($s.winget.checked -and -not $s.scanning) { break }; Start-Sleep 1 }
    Write-Host "winget: $($s.winget.version)"
    if ($s.installed.$AppId.installed) { throw "$AppId 已经安装，无法测试" }

    Write-Host "== 安装 $AppId"
    $null = Post 'jobs' @{ jobs = @(@{ action = 'install'; appId = $AppId }) }
    $r = WaitJobs
    $job = $r.jobs[-1]; $job.log | Select-Object -Last 15 | ForEach-Object { Write-Host "  | $_" }
    Write-Host "结果：$($job.status) $($job.message)"
    if ($job.status -ne 'success') { throw '安装失败' }
    Write-Host ("下载进度测量：已下载 {0:N1} MB / 总大小 {1:N1} MB" -f ($job.downloaded / 1MB), ($job.total / 1MB))
    if (-not ($job.total -gt 0 -and $job.downloaded -gt 0)) { throw '没有测量到真实下载进度' }
    $null = Post 'refresh' @{}; Start-Sleep 2; $null = WaitJobs
    $info = (State).installed.$AppId
    Write-Host "识别：$($info | ConvertTo-Json -Compress)"
    if (-not $info.installed) { throw '安装后没有识别到' }
    if (-not $info.canLaunch) { throw '没有找到启动入口' }

    Write-Host "== 启动 $AppId"
    $l = Post 'launch' @{ id = $AppId }
    if (-not $l.ok) { throw "启动失败：$($l.error)" }
    Start-Sleep 8
    if (-not $info.exe) { throw '没有解析出主程序路径（启动台无法显示真实图标）' }
    $icon = Invoke-WebRequest -Uri "$base/icon?id=$AppId&t=$token" -UseBasicParsing
    Write-Host "图标：$($icon.Headers['Content-Type']) $($icon.RawContentLength) 字节"
    $dir = Split-Path $info.exe

    Write-Host "== 运行中卸载应被拒绝"
    $null = Post 'jobs' @{ jobs = @(@{ action = 'uninstall'; appId = $AppId }) }
    $r = WaitJobs
    $job = $r.jobs[-1]
    Write-Host "结果：$($job.status) $($job.message)"
    if ($job.status -ne 'failed' -or $job.message -notmatch '正在运行' -or $job.hint -ne 'running') { throw '软件运行时没有提示先关闭' }

    Write-Host "== 关闭并卸载 $AppId（软件仍在运行，由 AI Station 自动关闭）"
    $null = Post 'jobs' @{ jobs = @(@{ action = 'uninstall'; appId = $AppId; arg = 'close' }) }
    $r = WaitJobs
    $job = $r.jobs[-1]; $job.log | Select-Object -Last 10 | ForEach-Object { Write-Host "  | $_" }
    Write-Host "结果：$($job.status) $($job.message)"
    if ($job.status -ne 'success') { throw '卸载失败' }
    if ((State).installed.$AppId.installed) { throw '卸载后仍识别为已安装' }
    $left = @(Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) })
    if ($left.Count -gt 0) { throw "卸载后仍有进程在运行：$($left.ProcessName -join ', ')" }
    Write-Host '真实安装 / 识别 / 启动 / 卸载 全部通过'
} finally {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Get-Content (Join-Path $data 'logs\aistation.log') -ErrorAction SilentlyContinue | Select-Object -Last 40
}

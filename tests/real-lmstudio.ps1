# 通过 AI Station 安装 LM Studio，再用“在 LM Studio 中下载”下载一个小模型，确认 lms ls 能看到它
param([string]$Model = 'qwen/qwen3-0.6b', [string]$Exe = "$PSScriptRoot\..\dist\AIStation.exe")
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$port = 18792; $token = 'ci-token'
$data = Join-Path $env:RUNNER_TEMP 'aistation-lms'
$proc = Start-Process $Exe -ArgumentList '--headless', '--port', $port, '--token', $token, '--data-dir', $data -PassThru
$base = "http://127.0.0.1:$port/api"; $h = @{ 'X-AIStation-Token' = $token }
function Post($path, $body) { Invoke-RestMethod -Method Post -Uri "$base/$path" -Headers $h -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5 -Compress))) }
function WaitAll {
    for ($i = 0; $i -lt 200; $i++) {
        Start-Sleep 5
        $r = Invoke-RestMethod -Uri "$base/jobs" -Headers $h
        $active = @($r.jobs | Where-Object { $_.status -eq 'queued' -or $_.status -eq 'running' })
        if ($active.Count -eq 0 -and -not $r.scanning) { return $r }
        $j = $active[0]; Write-Host ("  {0} {1} {2} {3}%" -f $j.action, $j.phase, $j.message, $j.progress)
    }
    throw '超时'
}
try {
    for ($i = 0; $i -lt 60; $i++) { try { $s = Invoke-RestMethod -Uri "$base/state" -Headers $h; if ($s.winget.checked -and -not $s.scanning) { break } } catch { }; Start-Sleep 1 }
    if (-not $s.installed.lmstudio.installed) { $null = Post 'jobs' @{ jobs = @(@{ action = 'install'; appId = 'lmstudio' }) }; $null = WaitAll }
    $null = Post 'jobs' @{ jobs = @(@{ action = 'lms-get'; appId = 'lmstudio'; arg = $Model }) }
    $r = WaitAll
    $job = $r.jobs[-1]
    $job.log | Select-Object -Last 12 | ForEach-Object { Write-Host "  | $_" }
    Write-Host "结果：$($job.status) $($job.message)"
    if ($job.status -ne 'success') { throw 'LM Studio 下载模型失败' }
    $lms = "$env:USERPROFILE\.lmstudio\bin\lms.exe"
    if (Test-Path $lms) { & $lms ls 2>&1 | Select-Object -First 15 }
    Write-Host '在 LM Studio 中一键下载模型 通过'
} finally {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Get-Process | Where-Object { $_.Path -like '*LM Studio*' -or $_.Path -like '*\.lmstudio\*' } | Stop-Process -Force -ErrorAction SilentlyContinue
}

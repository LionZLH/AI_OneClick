# 真实走一遍“一键准备本地 AI”：通过 AI Station 安装 Ollama 并下载一个小模型，再确认模型列表里有它
param([string]$Model = 'qwen3:0.6b', [string]$Exe = "$PSScriptRoot\..\dist\AIStation.exe")
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$port = 18791; $token = 'ci-token'
$data = Join-Path $env:RUNNER_TEMP 'aistation-ollama'
$proc = Start-Process $Exe -ArgumentList '--headless', '--port', $port, '--token', $token, '--data-dir', $data -PassThru
$base = "http://127.0.0.1:$port/api"; $h = @{ 'X-AIStation-Token' = $token }
function Post($path, $body) { Invoke-RestMethod -Method Post -Uri "$base/$path" -Headers $h -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5 -Compress))) }
try {
    for ($i = 0; $i -lt 60; $i++) { try { $s = Invoke-RestMethod -Uri "$base/state" -Headers $h; if ($s.winget.checked -and -not $s.scanning) { break } } catch { }; Start-Sleep 1 }
    $null = Post 'jobs' @{ jobs = @(@{ action = 'install'; appId = 'ollama' }, @{ action = 'pull-model'; appId = 'ollama'; arg = $Model }) }
    for ($i = 0; $i -lt 200; $i++) {
        Start-Sleep 5
        $r = Invoke-RestMethod -Uri "$base/jobs" -Headers $h
        $active = @($r.jobs | Where-Object { $_.status -eq 'queued' -or $_.status -eq 'running' })
        if ($active.Count -eq 0) { break }
        $j = $active[0]; Write-Host ("  {0} {1} {2} {3}%" -f $j.action, $j.phase, $j.message, $j.progress)
    }
    foreach ($j in $r.jobs) { Write-Host "结果：$($j.action) $($j.status) $($j.message)"; $j.log | Select-Object -Last 5 | ForEach-Object { Write-Host "  | $_" } }
    if (@($r.jobs | Where-Object { $_.status -ne 'success' }).Count -gt 0) { throw '有任务没有成功' }
    $m = Post 'local-models' @{}
    Write-Host "Ollama 模型：$($m.ollama -join ', ')"
    if (-not ($m.ollama -contains $Model)) { throw '模型列表中没有刚下载的模型' }
    Write-Host '一键准备本地 AI 通过'
} finally {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Get-Process ollama*, 'ollama app' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

# 核对 catalog.json 中每个 winget / Microsoft Store 包 ID 在官方源中真实存在。
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$catalog = Get-Content "$PSScriptRoot\..\ui\catalog.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$missing = @()
foreach ($app in $catalog.apps) {
    $src = $app.install.source
    if ($src -ne 'winget' -and $src -ne 'msstore') { continue }
    $out = winget show --id $app.install.id --exact --source $src --accept-source-agreements --disable-interactivity 2>&1 | Out-String
    $ok = $LASTEXITCODE -eq 0
    $line = ($out -split "`n" | Where-Object { $_ -match '^(Found|已找到)' } | Select-Object -First 1)
    Write-Host ("{0,-12} {1,-8} {2,-28} {3}  {4}" -f $app.id, $src, $app.install.id, $(if ($ok) { 'OK' } else { "MISSING($LASTEXITCODE)" }), "$line".Trim())
    if (-not $ok) { $missing += $app.id }
}
if ($missing.Count -gt 0) { Write-Host "找不到的包：$($missing -join ', ')"; exit 1 }

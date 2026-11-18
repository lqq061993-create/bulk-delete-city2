# 版本一致性检查：四处版本号必须完全相同
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$sources = [ordered]@{}

# 1. Mod.cs
$modCs = Join-Path $root 'Mod.cs'
if (Test-Path $modCs) {
    $m = [regex]::Match((Get-Content -Raw -Encoding UTF8 $modCs), 'ModVersion\s*=\s*"([^"]+)"')
    if ($m.Success) { $sources['Mod.cs'] = $m.Groups[1].Value }
}

# 2. mod.json
$modJson = Join-Path $root 'mod.json'
if (Test-Path $modJson) {
    $j = Get-Content -Raw -Encoding UTF8 $modJson | ConvertFrom-Json
    $sources['mod.json'] = [string]$j.version
}

# 3. ui/mod.json
$uiJson = Join-Path $root 'ui\mod.json'
if (Test-Path $uiJson) {
    $j = Get-Content -Raw -Encoding UTF8 $uiJson | ConvertFrom-Json
    $sources['ui/mod.json'] = [string]$j.version
}

# 4. PublishConfiguration.xml
$cfg = Join-Path $root 'Properties\PublishConfiguration.xml'
if (Test-Path $cfg) {
    [xml]$x = Get-Content -Raw -Encoding UTF8 $cfg
    $sources['PublishConfiguration.xml'] = [string]$x.Publish.ModVersion.Value
}

Write-Host "=== Version sources ==="
foreach ($k in $sources.Keys) { Write-Host ("  {0,-28} {1}" -f $k, $sources[$k]) }

$distinct = @($sources.Values | Select-Object -Unique)
if ($distinct.Count -ne 1) {
    Write-Host ""
    Write-Host "FAIL: version mismatch across sources" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host ("OK: all sources agree on " + $distinct[0]) -ForegroundColor Green
exit 0

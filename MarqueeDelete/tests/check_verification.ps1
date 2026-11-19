# Checks that verification.json actually describes the artifact we are about to ship.
#
# verification.json is evidence for ONE build: ModHash is the hash of that exact dll.
# build.ps1 does not compile deterministically, so the same sources built twice give
# different hashes - the recorded hash can never be a stable cross-build identity.
# Therefore a stale file is not a cosmetic problem: install.ps1 copies it next to the
# installed dll (install.ps1:30-32), so a stale file ships a false claim.
#
# exit 0 = evidence matches the current dist dll and version; exit 1 = it does not,
# or the check cannot run (missing file, unreadable Game.dll). A check that cannot run
# is a failure, never a pass.

[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [string]$GameInstallDir = 'D:\steam\steamapps\common\Cities Skylines II'
)

$ErrorActionPreference = 'Stop'

$root = if ([string]::IsNullOrWhiteSpace($RepoRoot)) { Split-Path -Parent $PSScriptRoot } else { $RepoRoot }
$problems = New-Object System.Collections.Generic.List[string]

# --------------------------------------------------------------- current artifact
$dllPath = Join-Path $root 'dist\MarqueeDelete.dll'
if (-not (Test-Path -LiteralPath $dllPath)) {
    Write-Host ('FAIL: ' + $dllPath + ' does not exist; build first (build.ps1).')
    exit 1
}
$currentHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToUpperInvariant()

# --------------------------------------------------------------- versions in source
$modCs = Join-Path $root 'Mod.cs'
$m = [regex]::Match((Get-Content -LiteralPath $modCs -Raw -Encoding UTF8), 'ModVersion\s*=\s*"([^"]+)"')
if (-not $m.Success) {
    Write-Host ('FAIL: cannot read ModVersion from ' + $modCs)
    exit 1
}
$sourceVersion = $m.Groups[1].Value

$modJsonPath = Join-Path $root 'mod.json'
$jsonVersion = $null
if (Test-Path -LiteralPath $modJsonPath) {
    try { $jsonVersion = (Get-Content -LiteralPath $modJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json).version } catch { $jsonVersion = $null }
}
if ([string]::IsNullOrWhiteSpace($jsonVersion)) {
    Write-Host ('FAIL: cannot read "version" from ' + $modJsonPath)
    exit 1
}

# --------------------------------------------------------------- recorded evidence
$verPath = Join-Path $root 'verification.json'
if (-not (Test-Path -LiteralPath $verPath)) {
    Write-Host ('FAIL: ' + $verPath + ' does not exist. Run tests/verify_cecil.ps1 after a fresh build.')
    exit 1
}
try { $ver = Get-Content -LiteralPath $verPath -Raw -Encoding UTF8 | ConvertFrom-Json }
catch {
    Write-Host ('FAIL: ' + $verPath + ' is not valid JSON.')
    exit 1
}

if ($ver.version -ne $sourceVersion) {
    $problems.Add('version: verification.json says ' + $ver.version + ', Mod.cs says ' + $sourceVersion)
}
if ($jsonVersion -ne $sourceVersion) {
    $problems.Add('version: mod.json says ' + $jsonVersion + ', Mod.cs says ' + $sourceVersion)
}
if ($ver.ModHash -ne $currentHash) {
    $problems.Add('ModHash: verification.json records ' + $ver.ModHash + ', current dist\MarqueeDelete.dll is ' + $currentHash)
}

# --------------------------------------------------------------- game identity
$managed = Join-Path $GameInstallDir 'Cities2_Data\Managed'
$gameDll = Join-Path $managed 'Game.dll'
$cecilDll = Join-Path $managed 'Colossal.Mono.Cecil.dll'
if (-not (Test-Path -LiteralPath $gameDll)) {
    Write-Host ('FAIL: ' + $gameDll + ' not found; cannot confirm GameMvid. Pass -GameInstallDir for another install.')
    exit 1
}
if (-not (Test-Path -LiteralPath $cecilDll)) {
    Write-Host ('FAIL: ' + $cecilDll + ' not found; cannot read the Game.dll MVID.')
    exit 1
}
try {
    Add-Type -Path $cecilDll
    $module = [Colossal.Mono.Cecil.ModuleDefinition]::ReadModule($gameDll)
    $liveMvid = $module.Mvid.ToString('N').ToUpperInvariant()
    $module.Dispose()
} catch {
    Write-Host ('FAIL: could not read the MVID of ' + $gameDll + ': ' + $_.Exception.Message)
    exit 1
}
if ($ver.GameMvid -ne $liveMvid) {
    $problems.Add('GameMvid: verification.json records ' + $ver.GameMvid + ', installed Game.dll is ' + $liveMvid)
}

# 证据里的 MVID 匹配本机，只说明「这份证据对应本机输入」，不说明这个构建是我们声称支持的那个。
# 发布前还必须校验它等于 Compatibility.cs 里声明支持的目标 MVID，否则在一台装了别的游戏版本的
# 机器上刷新证据，也能得到一份自洽、却在目标版本上从未验证过的证据。
#
# 声明本身缺失也算失败，不能只告警：没有声明就等于「没有目标可校验」，那道校验被悄悄跳过，
# 而这正是它要防的情形。恰好一个有效的 32 位十六进制值才算声明成立。
$declaredMvid = ''
$declaredMvidValues = @()
$compatPath = Join-Path $root 'Compatibility.cs'
if (-not (Test-Path -LiteralPath $compatPath)) {
    $problems.Add('target MVID: ' + $compatPath + ' not found; the declared supported target cannot be checked')
} else {
    $compatText = Get-Content -LiteralPath $compatPath -Raw -Encoding UTF8
    foreach ($declaredMatch in [regex]::Matches($compatText, 'VerifiedGameMvid\s*=\s*"([^"]*)"')) {
        $declaredMvidValues += $declaredMatch.Groups[1].Value
    }
    if ($declaredMvidValues.Count -eq 0) {
        $problems.Add('target MVID: ' + $compatPath + ' does not declare VerifiedGameMvid')
    } elseif ($declaredMvidValues.Count -gt 1) {
        $problems.Add('target MVID: ' + $compatPath + ' declares VerifiedGameMvid ' + $declaredMvidValues.Count + ' times')
    } elseif ($declaredMvidValues[0] -notmatch '^[0-9A-Fa-f]{32}$') {
        $problems.Add('target MVID: declared VerifiedGameMvid "' + $declaredMvidValues[0] + '" is not a 32-digit hex MVID')
    } else {
        $declaredMvid = $declaredMvidValues[0].ToUpperInvariant()
    }
}
if ($declaredMvid -ne '') {
    if ($liveMvid -ne $declaredMvid) {
        $problems.Add('GameMvid: installed Game.dll is ' + $liveMvid + ', but the declared supported target is ' + $declaredMvid)
    }
    if ($ver.GameMvid -ne $declaredMvid) {
        $problems.Add('GameMvid: verification.json records ' + $ver.GameMvid + ', but the declared supported target is ' + $declaredMvid)
    }
    if ($liveMvid -eq $declaredMvid -and $ver.GameMvid -eq $declaredMvid) {
        Write-Host ('OK: GameMvid matches the declared supported target (' + $declaredMvid + ')')
    }
}

# --------------------------------------------------------------- verdict
if ($problems.Count -gt 0) {
    Write-Host 'FAIL: verification.json does not describe the current artifact:'
    foreach ($p in $problems) { Write-Host ('  - ' + $p) }
    Write-Host ''
    Write-Host 'Refresh it deliberately: build.ps1, then tests/verify_cecil.ps1, then this check again.'
    exit 1
}

Write-Host ('OK: verification.json matches version ' + $sourceVersion + ' and dist hash ' + $currentHash.Substring(0, 12) + '...')
exit 0
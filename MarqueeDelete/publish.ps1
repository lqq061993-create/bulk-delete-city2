<#
.SYNOPSIS
    Publish MarqueeDelete to Paradox Mods.

.DESCRIPTION
    Stages a clean content folder (the same layout Mod.targets deploys to
    CSII_LOCALMODSPATH\<ModName>) and invokes the official ModPublisher.exe.

    The toolchain reads Properties\PublishConfiguration.xml for everything that
    ends up on the mod page: name, descriptions, thumbnail, tags, version.

    Three publish modes (mirrors the VS/Rider publish profiles):
      -Mode Publish     first upload, prints the ModId you must copy back
      -Mode NewVersion  ship an updated build under a new version number
      -Mode Update      only refresh the page metadata, no new files

.PARAMETER Mode
    Publish | NewVersion | Update

.PARAMETER Email
    PDX account email. Omit to let ModPublisher reuse the in-game login.

.PARAMETER DryRun
    Stage the content folder and print the exact command line, then stop.

.EXAMPLE
    .\publish.ps1 -Mode Publish
    .\publish.ps1 -Mode NewVersion
    .\publish.ps1 -Mode Update
#>
[CmdletBinding()]
param(
    [ValidateSet('Publish', 'NewVersion', 'Update')]
    [string]$Mode = 'Publish',

    [string]$Email,
    [string]$Password,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$name = 'MarqueeDelete'
$root = $PSScriptRoot

# ---------------------------------------------------------------- toolchain
$install = 'D:\steam\steamapps\common\Cities Skylines II'
if (-not (Test-Path -LiteralPath $install)) {
    $install = [Environment]::GetEnvironmentVariable('CSII_INSTALLATIONPATH', 'User')
}
if (-not $install -or -not (Test-Path -LiteralPath $install)) {
    throw "Game installation not found. Set CSII_INSTALLATIONPATH or edit `$install in publish.ps1."
}

$publisher = Join-Path $install 'Cities2_Data\Content\Game\.ModdingToolchain\ModPublisher\ModPublisher.exe'
if (-not (Test-Path -LiteralPath $publisher)) {
    throw "ModPublisher.exe not found: $publisher`nInstall the Modding Toolchain from the in-game Options > Modding page."
}

# ------------------------------------------------------- preflight: build
$dll = Join-Path $root "dist\$name.dll"
$uiBundle = Join-Path $root "dist\_ui_stage\Mods\$name\$name.mjs"
if (-not (Test-Path -LiteralPath $dll)) { throw "Missing $dll - run .\build.ps1 first." }
if (-not (Test-Path -LiteralPath $uiBundle)) { throw "Missing UI bundle $uiBundle - run 'npm run build' in ui\ with CSII_USERDATAPATH set to dist\_ui_stage." }

$versionSync = Join-Path $root 'tests\verify_version_sync.ps1'
if (Test-Path -LiteralPath $versionSync) {
    $pwshCmd = Get-Command pwsh -ErrorAction SilentlyContinue
    $psExe = if ($null -ne $pwshCmd) { $pwshCmd.Source } else { (Get-Process -Id $PID).Path }
    & $psExe -NoProfile -ExecutionPolicy Bypass -File $versionSync
    if ($LASTEXITCODE -ne 0) { throw "Version mismatch across Mod.cs / mod.json / ui/mod.json / PublishConfiguration.xml." }
}

$dllItem = Get-Item -LiteralPath $dll
$newerCs = Get-ChildItem -LiteralPath $root -Filter '*.cs' -File | Where-Object { $_.LastWriteTime -gt $dllItem.LastWriteTime }
if ($newerCs) {
    throw "dist\$name.dll is older than C# source files ($($newerCs[0].Name)). Re-run .\build.ps1 first."
}

$uiBundleItem = Get-Item -LiteralPath $uiBundle
$uiInputs = @()
if (Test-Path -LiteralPath (Join-Path $root 'ui\mod.json')) { $uiInputs += Get-Item -LiteralPath (Join-Path $root 'ui\mod.json') }
if (Test-Path -LiteralPath (Join-Path $root 'ui\src')) { $uiInputs += Get-ChildItem -LiteralPath (Join-Path $root 'ui\src') -Recurse -File }
$newerUi = $uiInputs | Where-Object { $_.LastWriteTime -gt $uiBundleItem.LastWriteTime }
if ($newerUi) {
    throw "UI bundle $uiBundle is older than UI sources ($($newerUi[0].Name)). Re-run 'npm run build' in ui\ with CSII_USERDATAPATH=$root\dist\_ui_stage."
}

$configPath = Join-Path $root 'Properties\PublishConfiguration.xml'
if (-not (Test-Path -LiteralPath $configPath)) { throw "Missing $configPath" }

[xml]$cfg = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
$modId = [string]$cfg.Publish.ModId.Value
if ($Mode -ne 'Publish' -and [string]::IsNullOrWhiteSpace($modId)) {
    throw "Mode '$Mode' requires <ModId Value=`"...`"/> in PublishConfiguration.xml. Run -Mode Publish first and copy the printed Id back."
}

# ------------------------------------------------------ preflight: thumbnail
$thumb = Join-Path $root ([string]$cfg.Publish.Thumbnail.Value)
if (-not (Test-Path -LiteralPath $thumb)) {
    throw "Thumbnail not found: $thumb`nRun the thumbnail generator (Properties\make-thumbnail.py) or set <Thumbnail Value=...> to an existing PNG."
}

# --------------------------------------------------- preflight: validations
# ModPublisher hard-fails on any of these being empty; fail fast and clearly.
$required = [ordered]@{
    DisplayName      = [string]$cfg.Publish.DisplayName.Value
    ShortDescription = [string]$cfg.Publish.ShortDescription.Value
    LongDescription  = ([string]$cfg.Publish.LongDescription).Trim()
    ModVersion       = [string]$cfg.Publish.ModVersion.Value
    GameVersion      = [string]$cfg.Publish.GameVersion.Value
}
$tags = @($cfg.Publish.Tag | ForEach-Object { [string]$_.Value } | Where-Object { $_ })
if ($tags.Count -eq 0) { $required['Tag'] = '' }

foreach ($k in $required.Keys) {
    if ([string]::IsNullOrWhiteSpace($required[$k])) {
        throw "PublishConfiguration.xml: <$k> must be set (ModPublisher requires it)."
    }
}

# ------------------------------------------------------------- stage content
$stage = Join-Path $root "publish\$name"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item -LiteralPath $dll -Destination $stage -Force
$pdb = Join-Path $root "dist\$name.pdb"
if (Test-Path -LiteralPath $pdb) { Copy-Item -LiteralPath $pdb -Destination $stage -Force }

# UI bundle: mjs + css + images\  (flat, next to the dll - what the game expects)
Copy-Item -Path (Join-Path $root "dist\_ui_stage\Mods\$name\*") -Destination $stage -Recurse -Force

# mod.json descriptor (published mods do ship one; the game reads id/isCodeMod from it)
Copy-Item -LiteralPath (Join-Path $root 'mod.json') -Destination $stage -Force

$files = Get-ChildItem -LiteralPath $stage -Recurse -File
Write-Host ""
Write-Host "Content folder: $stage" -ForegroundColor Cyan
$files | ForEach-Object {
    Write-Host ("  {0,-44} {1,8:N0}" -f $_.FullName.Substring($stage.Length + 1), $_.Length)
}
Write-Host ("  {0,-44} {1,8:N0}" -f 'TOTAL', ($files | Measure-Object Length -Sum).Sum) -ForegroundColor Cyan

# -------------------------------------------------------------------- publish
$pubArgs = @($Mode, $configPath, '-c', $stage, '-v')
if ($Email)    { $pubArgs += @('-e', $Email) }
if ($Password) { $pubArgs += @('-p', $Password) }

Write-Host ""
Write-Host "Mode: $Mode" -ForegroundColor Yellow
if ($DryRun) {
    Write-Host "DRY RUN - would execute:" -ForegroundColor Yellow
    Write-Host "  `"$publisher`" $($pubArgs -join ' ')"
    exit 0
}

Write-Host "Launching ModPublisher..." -ForegroundColor Yellow
Push-Location -LiteralPath $root
try {
    & $publisher @pubArgs
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}

Write-Host ""
if ($code -eq 0) {
    Write-Host "Publish finished (exit 0)." -ForegroundColor Green
    if ($Mode -eq 'Publish') {
        Write-Host ""
        Write-Host "Look for 'Mod published with Id=<number>' in the output above." -ForegroundColor Yellow
        Write-Host "Copy that number into Properties\PublishConfiguration.xml:" -ForegroundColor Yellow
        Write-Host '    <ModId Value="123456" />' -ForegroundColor Yellow
        Write-Host "It is required for 'NewVersion' and 'Update'." -ForegroundColor Yellow
    }
    Write-Host "Review and enable the mod at https://mods.paradoxplaza.com/ -> My mods" -ForegroundColor Green
} else {
    Write-Host "Publish FAILED (exit $code). See the ModPublisher output above." -ForegroundColor Red
}
exit $code

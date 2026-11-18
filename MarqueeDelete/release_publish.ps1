# 发布打包脚本：把当前源码以「发布版本号」重新构建并暂存到 publish/
# 本地开发版本号保持不动，发布完成后再切回。
#
# 用法：
#   pwsh -NoProfile -File .\release_publish.ps1 -DryRun        # 只演示，不构建不改文件
#   pwsh -NoProfile -File .\release_publish.ps1                # 真正执行到暂存，不调用 publish.ps1
#   pwsh -NoProfile -File .\release_publish.ps1 -Upload        # 额外调用 publish.ps1 上传（需用户明确授权）
param(
    [string]$PublishVersion = '2.6.2',
    [switch]$DryRun,
    [switch]$Upload
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$modCs   = Join-Path $root 'Mod.cs'
$modJson = Join-Path $root 'mod.json'
$uiJson  = Join-Path $root 'ui\mod.json'
$pubCfg  = Join-Path $root 'Properties\PublishConfiguration.xml'

function Get-CurrentVersion {
    $m = [regex]::Match((Get-Content -Raw -Encoding UTF8 $modCs), 'ModVersion\s*=\s*"([^"]+)"')
    if (-not $m.Success) { throw "无法从 Mod.cs 读取 ModVersion" }
    return $m.Groups[1].Value
}

function Set-AllVersions([string]$ver) {
    # Mod.cs
    $t = Get-Content -Raw -Encoding UTF8 $modCs
    $t = [regex]::Replace($t, 'ModVersion\s*=\s*"[^"]+"', ('ModVersion = "' + $ver + '"'))
    [System.IO.File]::WriteAllText($modCs, $t, (New-Object System.Text.UTF8Encoding $false))

    # mod.json
    $t = Get-Content -Raw -Encoding UTF8 $modJson
    $t = [regex]::Replace($t, '"version"\s*:\s*"[^"]+"', ('"version": "' + $ver + '"'))
    [System.IO.File]::WriteAllText($modJson, $t, (New-Object System.Text.UTF8Encoding $false))

    # ui/mod.json
    $t = Get-Content -Raw -Encoding UTF8 $uiJson
    $t = [regex]::Replace($t, '"version"\s*:\s*"[^"]+"', ('"version": "' + $ver + '"'))
    [System.IO.File]::WriteAllText($uiJson, $t, (New-Object System.Text.UTF8Encoding $false))

    # PublishConfiguration.xml
    $t = Get-Content -Raw -Encoding UTF8 $pubCfg
    $t = [regex]::Replace($t, '<ModVersion Value="[^"]*" />', ('<ModVersion Value="' + $ver + '" />'))
    [System.IO.File]::WriteAllText($pubCfg, $t, (New-Object System.Text.UTF8Encoding $false))
}

$localVersion = Get-CurrentVersion
Write-Host "=== Release staging ===" -ForegroundColor Cyan
Write-Host ("Local (dev) version : " + $localVersion)
Write-Host ("Publish version     : " + $PublishVersion)

if ($localVersion -eq $PublishVersion) {
    throw "本地版本与发布版本相同 ($localVersion)。请让本地版本保持更高或更低，避免混淆。"
}

if ($DryRun) {
    Write-Host ""
    Write-Host "DRY RUN - 将执行以下步骤：" -ForegroundColor Yellow
    Write-Host ("  1) 把 4 处版本号从 " + $localVersion + " 暂改为 " + $PublishVersion)
    Write-Host  "  2) 重新构建 UI 与 C# 程序集"
    Write-Host  "  3) 运行测试与 Cecil 校验"
    Write-Host ("  4) 把版本号恢复为 " + $localVersion + " 并重建本地产物")
    if ($Upload) { Write-Host "  5) 调用 publish.ps1 -Mode NewVersion 上传" }
    else        { Write-Host "  5) 不调用 publish.ps1（未加 -Upload）" }
    exit 0
}

$staged = $false
try {
    Write-Host ""
    Write-Host ("[1/5] 切换到发布版本 " + $PublishVersion) -ForegroundColor Yellow
    Set-AllVersions $PublishVersion

    Write-Host "[2/5] 构建" -ForegroundColor Yellow
    Push-Location (Join-Path $root 'ui')
    $env:CSII_USERDATAPATH = Join-Path $root 'dist\_ui_stage'
    & npm run build 2>&1 | Select-Object -Last 3
    Pop-Location
    & pwsh -NoProfile -File (Join-Path $root 'build.ps1') 2>&1 | Select-Object -Last 2

    Write-Host "[3/5] 测试与校验" -ForegroundColor Yellow
    & pwsh -NoProfile -File (Join-Path $root 'tests\run_tests.ps1') 2>&1 | Select-String -Pattern 'Unit tests finished|Deletion regression'
    & pwsh -NoProfile -File (Join-Path $root 'tests\verify_cecil.ps1') 2>&1 | Select-String -Pattern 'UnresolvedCount'

    Write-Host "[4/5] 暂存到 publish/" -ForegroundColor Yellow
    & pwsh -NoProfile -File (Join-Path $root 'publish.ps1') -Mode NewVersion -DryRun 2>&1 | Select-Object -Last 6
    $staged = $true

    if ($Upload) {
        Write-Host "[5/5] 上传" -ForegroundColor Yellow
        & pwsh -NoProfile -File (Join-Path $root 'publish.ps1') -Mode NewVersion
    } else {
        Write-Host "[5/5] 未上传（未加 -Upload）" -ForegroundColor Yellow
    }
}
finally {
    Write-Host ""
    Write-Host ("[恢复] 切回本地版本 " + $localVersion) -ForegroundColor Yellow
    Set-AllVersions $localVersion
    Push-Location (Join-Path $root 'ui')
    $env:CSII_USERDATAPATH = Join-Path $root 'dist\_ui_stage'
    & npm run build 2>&1 | Select-Object -Last 1
    Pop-Location
    & pwsh -NoProfile -File (Join-Path $root 'build.ps1') 2>&1 | Select-Object -Last 2
    Write-Host ""
    Write-Host "=== 完成 ===" -ForegroundColor Cyan
    Write-Host ("本地产物已恢复为 " + $localVersion)
    if ($staged) {
        Write-Host ("发布暂存包已生成，版本号 " + $PublishVersion + "。publish/ 目录内容为发布版。")
        Write-Host "注意：dist/ 已切回本地版本，如要再次上传请重跑本脚本。" -ForegroundColor Yellow
    }
}

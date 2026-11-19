# 发布打包脚本：把当前源码以「发布版本号」重新构建并暂存到 publish/
# 本地开发版本号保持不动，发布完成后再切回。
#
# 失败即停：每一步（npm 构建、C# 构建、测试、Cecil 校验、暂存）都必须以 0 退出，
# 任何一步非零退出都会立刻中断，后续步骤不再执行，也不会打印「完成」。
# 之前的版本把这些命令的输出管道给 Select-Object / Select-String 后从不检查
# $LASTEXITCODE，所以构建或测试失败时脚本会继续往下跑，直到最后仍然报成功。
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

# 写入之后回读四处，确认版本号真的改成了目标值。正则没命中、文件结构变了、
# 只改了其中几处，都会在这里立刻暴露，而不是等到发布包已经上传之后。
function Assert-AllVersions([string]$ver) {
    $problems = @()

    $m = [regex]::Match((Get-Content -Raw -Encoding UTF8 $modCs), 'ModVersion\s*=\s*"([^"]+)"')
    if (-not $m.Success -or $m.Groups[1].Value -ne $ver) { $problems += ("Mod.cs 实际为 " + $(if ($m.Success) { $m.Groups[1].Value } else { '<未匹配>' })) }

    foreach ($pair in @(@{ Path = $modJson; Name = 'mod.json' }, @{ Path = $uiJson; Name = 'ui/mod.json' })) {
        $text = Get-Content -Raw -Encoding UTF8 $pair.Path
        $hit = [regex]::Match($text, '"version"\s*:\s*"([^"]+)"')
        if (-not $hit.Success -or $hit.Groups[1].Value -ne $ver) { $problems += ($pair.Name + " 实际为 " + $(if ($hit.Success) { $hit.Groups[1].Value } else { '<未匹配>' })) }
    }

    $cfgText = Get-Content -Raw -Encoding UTF8 $pubCfg
    $cfgHit = [regex]::Match($cfgText, '<ModVersion Value="([^"]*)" />')
    if (-not $cfgHit.Success -or $cfgHit.Groups[1].Value -ne $ver) { $problems += ("Properties/PublishConfiguration.xml 实际为 " + $(if ($cfgHit.Success) { $cfgHit.Groups[1].Value } else { '<未匹配>' })) }

    if ($problems.Count -gt 0) {
        throw ("版本号未能全部写成 " + $ver + "：" + ($problems -join '；'))
    }
}

# 跑一个外部命令，收集完整输出，只显示需要的部分，并按退出码决定是否中断。
# 输出先落到变量、再挑几行显示，是为了失败时还能把整段打出来——管道给
# Select-Object 之后原始输出就没了，只剩最后一两行，排查时什么都不剩。
function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = $root,
        [hashtable]$Environment = @{},
        [string[]]$ShowPattern = @(),
        [int]$ShowLast = 0
    )

    Write-Host ("  > " + $Title) -ForegroundColor DarkGray

    $pushed = $false
    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $Environment[$key], 'Process')
    }

    $output = @()
    $code = 0
    try {
        if ($WorkingDirectory) {
            Push-Location -LiteralPath $WorkingDirectory
            $pushed = $true
        }
        $output = & $FilePath @Arguments 2>&1 | ForEach-Object { "$_" }
        if ($null -ne $LASTEXITCODE) { $code = $LASTEXITCODE }
    } finally {
        if ($pushed) { Pop-Location }
        foreach ($key in $Environment.Keys) {
            [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process')
        }
    }

    if ($ShowPattern.Count -gt 0) {
        $shown = $output | Select-String -Pattern $ShowPattern | ForEach-Object { "$_" }
    } elseif ($ShowLast -gt 0) {
        $shown = $output | Select-Object -Last $ShowLast | ForEach-Object { "$_" }
    } else {
        $shown = $output
    }
    if ($shown) { $shown | ForEach-Object { Write-Host $_ } }

    if ($code -ne 0) {
        Write-Host ""
        Write-Host ("步骤失败：" + $Title + "（退出码 " + $code + "）") -ForegroundColor Red
        Write-Host "--- 该步骤完整输出 ---" -ForegroundColor Red
        if ($output) { $output | ForEach-Object { Write-Host $_ } } else { Write-Host "(无输出)" }
        throw ("Step failed: " + $Title + " (exit code " + $code + ")")
    }
}

# 构建成功还不够：要确认产物确实被这次构建重写过，而不是上一轮的旧文件。
# publish.ps1 只检查文件是否存在，不检查新旧，所以这里补上。
function Assert-FreshArtifact([string]$Path, [datetime]$Since, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path)) { throw ($Label + " 未生成：" + $Path) }
    $item = Get-Item -LiteralPath $Path
    if ($item.LastWriteTime -lt $Since) {
        throw ($Label + " 不是本次构建的产物（修改时间 " + $item.LastWriteTime.ToString('s') + "，本次构建开始于 " + $Since.ToString('s') + "）")
    }
    Write-Host ("    " + $Label + " -> " + $item.LastWriteTime.ToString('s')) -ForegroundColor DarkGray
}

$pwshCmd = Get-Command pwsh -ErrorAction SilentlyContinue
if ($null -eq $pwshCmd) { throw "pwsh (.NET 8+) is required." }
$pwshExe = $pwshCmd.Source

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
    Write-Host ("  4) 把版本号恢复为 " + $localVersion + " 并重建本地产物，再重新生成 verification.json")
    if ($Upload) { Write-Host "  5) 调用 publish.ps1 -Mode NewVersion 上传" }
    else        { Write-Host "  5) 不调用 publish.ps1（未加 -Upload）" }
    exit 0
}

$staged = $false
$failure = $null
$restoreFailure = $null
$uiStage = Join-Path $root 'dist\_ui_stage'

# Preflight: verification.json is evidence for one specific build (ModHash is that dll's
# hash, and readable compiles are not deterministic), so starting a release on stale
# evidence only hides the drift. install.ps1 copies this file next to the installed dll,
# which is why a stale one is worse than a missing one.
Write-Host ""
Write-Host "[0/5] 预检：verification.json 是否描述当前产物" -ForegroundColor Yellow
try {
    Invoke-Step -Title "tests/check_verification.ps1" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tests\check_verification.ps1')) -ShowLast 6
} catch {
    Write-Host ""
    Write-Host ("[预检失败] " + $_.Exception.Message) -ForegroundColor Red
    Write-Host "发布没有开始：版本号与 dist/ 产物都没有被改动。" -ForegroundColor Red
    exit 1
}

try {
    Write-Host ""
    Write-Host ("[1/5] 切换到发布版本 " + $PublishVersion) -ForegroundColor Yellow
    Set-AllVersions $PublishVersion
    Assert-AllVersions $PublishVersion

    Write-Host "[2/5] 构建" -ForegroundColor Yellow
    $buildStarted = Get-Date
    Invoke-Step -Title "npm run build (ui)" -FilePath "npm" -Arguments @('run', 'build') `
        -WorkingDirectory (Join-Path $root 'ui') `
        -Environment @{ CSII_USERDATAPATH = $uiStage } -ShowLast 3
    Invoke-Step -Title "build.ps1 (C#)" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'build.ps1')) -ShowLast 2
    Assert-FreshArtifact -Path (Join-Path $root 'dist\MarqueeDelete.dll') -Since $buildStarted -Label "dist\MarqueeDelete.dll"
    Assert-FreshArtifact -Path (Join-Path $uiStage 'Mods\MarqueeDelete\MarqueeDelete.mjs') -Since $buildStarted -Label "UI bundle"

    Write-Host "[3/5] 测试与校验" -ForegroundColor Yellow
    Invoke-Step -Title "tests/run_tests.ps1" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tests\run_tests.ps1')) `
        -ShowPattern @('passed', 'Unit tests finished', 'Deletion regression', 'error', 'failed')
    Invoke-Step -Title "tests/verify_cecil.ps1" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tests\verify_cecil.ps1')) `
        -ShowPattern @('UnresolvedCount', 'Unresolved', 'error', 'failed')

    Write-Host "[4/5] 暂存到 publish/" -ForegroundColor Yellow
    Invoke-Step -Title "publish.ps1 -Mode NewVersion -DryRun" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'publish.ps1'), '-Mode', 'NewVersion', '-DryRun') -ShowLast 6
    $staged = $true

    if ($Upload) {
        Write-Host "[5/5] 上传" -ForegroundColor Yellow
        Invoke-Step -Title "publish.ps1 -Mode NewVersion" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'publish.ps1'), '-Mode', 'NewVersion')
    } else {
        Write-Host "[5/5] 未上传（未加 -Upload）" -ForegroundColor Yellow
    }
}
catch {
    $failure = $_
}
finally {
    # 无论是否失败都要把版本号切回来：Set-AllVersions 可能在写到一半时抛错，
    # 那会让仓库停在「四个文件版本号不一致」的状态。
    Write-Host ""
    Write-Host ("[恢复] 切回本地版本 " + $localVersion) -ForegroundColor Yellow
    try {
        Set-AllVersions $localVersion
        Assert-AllVersions $localVersion
        Invoke-Step -Title "npm run build (restore)" -FilePath "npm" -Arguments @('run', 'build') `
            -WorkingDirectory (Join-Path $root 'ui') `
            -Environment @{ CSII_USERDATAPATH = $uiStage } -ShowLast 1
        Invoke-Step -Title "build.ps1 (restore)" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'build.ps1')) -ShowLast 2

        # 第 3 步的 verify_cecil 把 verification.json 写成了发布版产物（2.6.2 + 发布构建的哈希）。
        # 恢复重建后那份证据就不再描述 dist/ 里的东西，所以在这里重新生成，让证据始终跟随
        # 当前本地产物；生成完立刻用 check_verification.ps1 复核，不让它停在不确定状态。
        Invoke-Step -Title "tests/verify_cecil.ps1 (restore evidence)" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tests\verify_cecil.ps1')) `
            -ShowPattern @('UnresolvedCount', 'Unresolved', 'error', 'failed')
        Invoke-Step -Title "tests/check_verification.ps1 (restore evidence)" -FilePath $pwshExe -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tests\check_verification.ps1')) -ShowLast 6
    }
    catch {
        $restoreFailure = $_
        # 恢复失败时可能已经把 evidence 写成了半途状态（例如发布版的版本号、旧产物的哈希）。
        # 与其留下一份看不出问题的假证据，不如删掉它：install.ps1:30 只在文件存在时才拷贝。
        $verSrc = Join-Path $root 'verification.json'
        if (Test-Path -LiteralPath $verSrc) {
            & $pwshExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tests\check_verification.ps1') *> $null
            if ($LASTEXITCODE -ne 0) {
                Remove-Item -LiteralPath $verSrc -Force
                Write-Host "verification.json 与当前产物不一致，已删除（宁可缺失证据，也不留下错误证据）。" -ForegroundColor Red
            }
        }
    }
}

if ($null -ne $failure) {
    Write-Host ""
    Write-Host ("[失败] " + $failure.Exception.Message) -ForegroundColor Red
    Write-Host "发布流程已停止，后续步骤没有执行。" -ForegroundColor Red
    if ($null -ne $restoreFailure) {
        Write-Host ("[失败] 恢复本地版本也失败：" + $restoreFailure.Exception.Message) -ForegroundColor Red
        Write-Host "版本号可能停在发布版本上，请手工核对四处版本号。" -ForegroundColor Red
    }
    elseif ($staged) {
        Write-Host ("本地产物已恢复为 " + $localVersion + "；publish/ 里的暂存包对应 " + $PublishVersion + "。") -ForegroundColor Yellow
    }
    exit 1
}

if ($null -ne $restoreFailure) {
    Write-Host ""
    Write-Host ("[失败] 暂存完成，但恢复本地版本失败：" + $restoreFailure.Exception.Message) -ForegroundColor Red
    Write-Host "版本号可能停在发布版本上，请手工核对四处版本号。" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== 完成 ===" -ForegroundColor Cyan
Write-Host ("本地产物已恢复为 " + $localVersion)
if ($staged) {
    Write-Host ("发布暂存包已生成，版本号 " + $PublishVersion + "。publish/ 目录内容为发布版。")
    Write-Host "注意：dist/ 已切回本地版本，如要再次上传请重跑本脚本。" -ForegroundColor Yellow
}
exit 0
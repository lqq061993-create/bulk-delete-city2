param(
    [string]$GameInstallDir = 'D:\steam\steamapps\common\Cities Skylines II',
    [string]$BaselineJson = ''
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Core') {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $pwsh) {
        throw "pwsh (.NET 8+) is required to load Colossal.Mono.Cecil.dll cleanly."
    }
    # 转发调用方的参数：-File 调用方式下 $args 是空的（参数已被 param() 绑定），
    # 只写 @args 会把 -GameInstallDir / -BaselineJson 悄悄丢掉，子进程于是回落默认目录。
    $forward = @()
    foreach ($key in $PSBoundParameters.Keys) {
        $forward += ('-' + $key)
        $forward += [string]$PSBoundParameters[$key]
    }
    & $pwsh.Source -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @forward
    exit $LASTEXITCODE
}

$root = Split-Path -Parent $PSScriptRoot

# 1. 动态识别游戏 Managed 目录
$managed = $GameInstallDir
if (Test-Path (Join-Path $GameInstallDir 'Game.dll')) {
    $managed = $GameInstallDir
} elseif (Test-Path (Join-Path $GameInstallDir 'Cities2_Data\Managed\Game.dll')) {
    $managed = Join-Path $GameInstallDir 'Cities2_Data\Managed'
} elseif (-not $PSBoundParameters.ContainsKey('GameInstallDir') -and (Test-Path 'D:\steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed\Game.dll')) {
    # 只有调用方没显式指定目录时才回落默认路径；否则「指定的目录里没有 Game.dll」
    # 会被默认路径悄悄救回来，读不到的失败分支永远走不到。
    $managed = 'D:\steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
} else {
    throw "Cities Skylines II Managed directory not found at '$GameInstallDir'"
}

$dllPath = Join-Path $root 'dist\MarqueeDelete.dll'
$gameDll = Join-Path $managed 'Game.dll'

if (-not (Test-Path -LiteralPath $dllPath)) { throw "Mod DLL not found: $dllPath" }
if (-not (Test-Path -LiteralPath $gameDll)) { throw "Game.dll not found: $gameDll" }

# 1b. 版本锚定：先确定这份证据描述的是哪个游戏构建，再谈解析结果。
# 版本号不是从标准版本 API 读的：Game.dll 的版本字段全是 0.0.0.0，Cities2.exe 报的是
# Unity 自己的 2022.3.71f1。这里能证明的只是「这串版本字符串出现在 Game.dll 的字节里」，
# 它是从文件内容中提取的，不是官方版本元数据。
$gameVersionPattern = '\d+\.\d+\.\d+f\d+ \(\d+\.\d+[^)]*\) \[\d+\.\d+\]'
$gameVersionCandidates = @()
try {
    $gameVersionBytes = [System.IO.File]::ReadAllBytes($gameDll)
    $gameVersionText = [System.Text.Encoding]::Latin1.GetString($gameVersionBytes)
} catch {
    Write-Host ("无法读取 " + $gameDll + " 的内容（" + $_.Exception.Message + "），拒绝写入 verification.json。") -ForegroundColor Red
    exit 1
}
# 去重必须区分大小写：PowerShell 的 @{} 默认是不区分大小写的比较，所以
# "1.6.4f1 (1097.158a)" 和 "1.6.4f1 (1097.158A)" 会被当成同一个键合并掉，
# 两个不同的候选只剩一个，歧义检查就永远看不到它们。这里按 Ordinal 显式比较。
$gameVersionSeen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($match in [regex]::Matches($gameVersionText, $gameVersionPattern)) {
    if ($gameVersionSeen.Add($match.Value)) {
        $gameVersionCandidates += ,@($match.Value, $match.Index)
    }
}
if ($gameVersionCandidates.Count -eq 0) {
    Write-Host ("在 " + $gameDll + " 里找不到形如 1.6.4f1 (build) [rev] 的版本字符串，拒绝写入 verification.json。") -ForegroundColor Red
    exit 1
}
if ($gameVersionCandidates.Count -gt 1) {
    Write-Host ("在 " + $gameDll + " 里找到多段内容不同的版本字符串，无法确定哪一个描述本次产物，拒绝写入 verification.json：") -ForegroundColor Red
    foreach ($candidate in $gameVersionCandidates) {
        Write-Host ("  byte offset " + $candidate[1] + ': "' + $candidate[0] + '"')
    }
    exit 1
}
$gameVersionTextValue = $gameVersionCandidates[0][0]
$gameVersionOffset = $gameVersionCandidates[0][1]
$gameVersion = ([regex]::Match($gameVersionTextValue, '^\d+\.\d+\.\d+f\d+')).Value
$gameVersionSource = 'Game.dll byte offset ' + $gameVersionOffset + ': "' + $gameVersionTextValue + '"'

# 声明支持的目标版本来自 Compatibility.cs，与提取到的版本比对；两边不一致也算证据的一环。
$declaredGameVersion = ''
$compatSourcePath = Join-Path $root 'Compatibility.cs'
if (Test-Path -LiteralPath $compatSourcePath) {
    $declaredMatch = [regex]::Match((Get-Content -LiteralPath $compatSourcePath -Raw -Encoding UTF8), 'VerifiedGameVersion\s*=\s*"([^"]+)"')
    if ($declaredMatch.Success) { $declaredGameVersion = $declaredMatch.Groups[1].Value }
}
$gameVersionMatchesDeclared = ($declaredGameVersion -ne '') -and ($gameVersion -eq $declaredGameVersion)


# 2. 加载 Cecil 反射器
$cecilAsm = [System.Reflection.Assembly]::LoadFrom((Join-Path $managed 'Colossal.Mono.Cecil.dll'))
$cecilTypes = $cecilAsm.GetTypes()
$asmDefType = $cecilTypes | Where-Object { $_.Name -eq 'AssemblyDefinition' } | Select-Object -First 1
$resolverType = $cecilTypes | Where-Object { $_.Name -eq 'DefaultAssemblyResolver' } | Select-Object -First 1
$readerParamsType = $cecilTypes | Where-Object { $_.Name -eq 'ReaderParameters' } | Select-Object -First 1

$resolver = [Activator]::CreateInstance($resolverType)
$resolver.AddSearchDirectory($managed)
$readerParams = [Activator]::CreateInstance($readerParamsType)
$readerParams.AssemblyResolver = $resolver

$modAsm = $asmDefType::ReadAssembly($dllPath, $readerParams)
$gameAsm = $asmDefType::ReadAssembly($gameDll, $readerParams)

$mvid = $gameAsm.MainModule.Mvid.ToString('N').ToUpperInvariant()
$gameHash = (Get-FileHash $gameDll -Algorithm SHA256).Hash
$modHash = (Get-FileHash $dllPath -Algorithm SHA256).Hash

# 3. 兼容边界分类器（方案第 1 节四大组）
function Classify-AffectedFeature([string]$fullName) {
    if ($fullName -match 'Net\.|Road|Track|Node|Edge|Curve|Terrain|Raycast|Search|Transform|Owner|Attached|Building|Static') {
        return 'SearchAndPick'
    }
    if ($fullName -match 'Deleted|ModificationBarrier|ToolOutputBarrier|Batched|Signature|UniqueObject|UnderConstruction') {
        return 'DeletionSubmit'
    }
    if ($fullName -match 'PrefabRef|EdgeGeometry|SubNet|SubObject|SubReplacement|Composition|SelectiveUndo|ApplyObjectsSystem|ApplyNetSystem|ApplyAreasSystem|ApplyZonesSystem|ApplyBrushesSystem|Zones\.|Brush|GlobalUndo|Applied|BatchesUpdated') {
        return 'UndoRebuild'
    }
    if ($fullName -match 'UI|Binding|ProxyAction|LocalizationManager|DefaultToolSystem|Bulldoze') {
        return 'UIAccess'
    }
    return 'CoreECS/Infrastructure'
}

$resolvedMembers = 0
$unresolvedList = New-Object System.Collections.Generic.List[PSObject]

foreach ($mr in $modAsm.MainModule.GetMemberReferences()) {
    try {
        $def = $mr.Resolve()
        if ($null -eq $def) {
            $group = Classify-AffectedFeature $mr.FullName
            $unresolvedList.Add([PSCustomObject]@{
                Kind = 'Member'
                FullName = $mr.FullName
                FeatureGroup = $group
                Reason = 'Symbol returned null (not found in target assemblies)'
            })
        } else {
            $resolvedMembers++
        }
    } catch {
        $group = Classify-AffectedFeature $mr.FullName
        $unresolvedList.Add([PSCustomObject]@{
            Kind = 'Member'
            FullName = $mr.FullName
            FeatureGroup = $group
            Reason = $_.Exception.Message
        })
    }
}

$resolvedTypes = 0
foreach ($tr in $modAsm.MainModule.GetTypeReferences()) {
    try {
        $tdef = $tr.Resolve()
        if ($null -eq $tdef) {
            $group = Classify-AffectedFeature $tr.FullName
            $unresolvedList.Add([PSCustomObject]@{
                Kind = 'Type'
                FullName = $tr.FullName
                FeatureGroup = $group
                Reason = 'Type definition not found'
            })
        } else {
            $resolvedTypes++
        }
    } catch {
        $group = Classify-AffectedFeature $tr.FullName
        $unresolvedList.Add([PSCustomObject]@{
            Kind = 'Type'
            FullName = $tr.FullName
            FeatureGroup = $group
            Reason = $_.Exception.Message
        })
    }
}

$modAsm.Dispose()
$gameAsm.Dispose()

# 4. 读取上一版基线并计算差异
$baselinePath = if ($BaselineJson -and (Test-Path -LiteralPath $BaselineJson)) {
    $BaselineJson
} else {
    Join-Path $root 'verification.json'
}

$baseline = $null
if (Test-Path -LiteralPath $baselinePath) {
    try {
        $baseline = Get-Content -LiteralPath $baselinePath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {}
}

$diff = [ordered]@{
    hasBaseline = ($null -ne $baseline)
    prevMvid = if ($baseline) { [string]$baseline.GameMvid } else { 'N/A' }
    prevGameHash = if ($baseline) { [string]$baseline.GameHash } else { 'N/A' }
    prevModHash = if ($baseline) { [string]$baseline.ModHash } else { 'N/A' }
    prevResolvedMembers = if ($baseline) { [int]$baseline.resolvedMemberReferences } else { 0 }
    prevResolvedTypes = if ($baseline) { [int]$baseline.resolvedTypeReferences } else { 0 }
    prevUnresolvedCount = if ($baseline) { [int]$baseline.unresolvedMemberReferences } else { 0 }
    memberDiff = if ($baseline) { $resolvedMembers - [int]$baseline.resolvedMemberReferences } else { 0 }
    typeDiff = if ($baseline) { $resolvedTypes - [int]$baseline.resolvedTypeReferences } else { 0 }
    mvidChanged = if ($baseline) { ($baseline.GameMvid -ne $mvid) } else { $false }
}

# 5. 格式化控制台输出
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "   MarqueeDelete Cecil 深度兼容性与符号解析检测报告" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

Write-Host "`n[1. 游戏构建与程序集]" -ForegroundColor Yellow
Write-Host ("  游戏安装/Managed目录 : " + $managed)
Write-Host ("  Game MVID           : " + $mvid)
Write-Host ("  Game.dll SHA256     : " + $gameHash)
Write-Host ("  Mod.dll SHA256      : " + $modHash)

Write-Host "`n[2. 符号解析统计]" -ForegroundColor Yellow
Write-Host ("  已解析成员引用 (Resolved Members) : " + $resolvedMembers)
Write-Host ("  已解析类型引用 (Resolved Types)   : " + $resolvedTypes)
Write-Host ("  未解析异常数   (Unresolved Total) : " + $unresolvedList.Count)

Write-Host "`n[3. 与上一版基线差异对比 (Diff vs Baseline)]" -ForegroundColor Yellow
if ($diff.hasBaseline) {
    $mvidStatus = if ($diff.mvidChanged) { "已变更 ($($diff.prevMvid) -> $mvid)" } else { "一致 ($mvid)" }
    Write-Host ("  MVID 对比             : " + $mvidStatus)
    Write-Host ("  成员引用差异 (Members): {0:+0;-0;+0} (基线: {1} -> 当前: {2})" -f $diff.memberDiff, $diff.prevResolvedMembers, $resolvedMembers)
    Write-Host ("  类型引用差异 (Types)  : {0:+0;-0;+0} (基线: {1} -> 当前: {2})" -f $diff.typeDiff, $diff.prevResolvedTypes, $resolvedTypes)
} else {
    Write-Host "  未找到历史 baseline (verification.json)，本次作为全新基线记录。"
}

Write-Host "`n[4. 四组兼容边界状态评估 (Compatibility Boundaries)]" -ForegroundColor Yellow
$groups = @('SearchAndPick', 'DeletionSubmit', 'UndoRebuild', 'UIAccess')
foreach ($g in $groups) {
    $unresolvedInGroup = @($unresolvedList | Where-Object { $_.FeatureGroup -eq $g })
    if ($unresolvedInGroup.Count -eq 0) {
        if ($mvid -eq '46198354D4F94D23BF327E3C085D8B92') {
            Write-Host ("  [{0,-14}] : 已验证 (Verified) - 符号完整且构建完全匹配" -f $g) -ForegroundColor Green
        } else {
            if ($g -eq 'DeletionSubmit' -or $g -eq 'UndoRebuild') {
                Write-Host ("  [{0,-14}] : 可预览、待验证写入 (PreviewOnly) - 符号完好但为未知构建" -f $g) -ForegroundColor DarkYellow
            } else {
                Write-Host ("  [{0,-14}] : 已验证 (Verified) - 只读/UI符号通过" -f $g) -ForegroundColor Green
            }
        }
    } else {
        Write-Host ("  [{0,-14}] : 不可用 (Unavailable) - {1} 个关键符号未解析" -f $g, $unresolvedInGroup.Count) -ForegroundColor Red
    }
}

Write-Host "`n[5. 具体不兼容成员明细 (Incompatible Members Detail)]" -ForegroundColor Yellow
if ($unresolvedList.Count -eq 0) {
    Write-Host "  √ 完美通过：未发现任何不兼容成员或缺失类型！" -ForegroundColor Green
} else {
    foreach ($item in $unresolvedList) {
        Write-Host ("  [受影响功能: {0}] {1}: {2}" -f $item.FeatureGroup, $item.Kind, $item.FullName) -ForegroundColor Red
        Write-Host ("     原因: " + $item.Reason) -ForegroundColor DarkRed
    }
}
Write-Host "================================================================`n" -ForegroundColor Cyan

# 6. 回写 verification.json
$modMetaPath = Join-Path $root 'mod.json'
if (-not (Test-Path -LiteralPath $modMetaPath)) {
    Write-Host ("无法读取版本号：" + $modMetaPath + " 不存在，拒绝写入 verification.json。") -ForegroundColor Red
    exit 1
}
$modVersion = (Get-Content -LiteralPath $modMetaPath -Raw -Encoding UTF8 | ConvertFrom-Json).version
if ([string]::IsNullOrWhiteSpace($modVersion)) {
    Write-Host ("无法读取版本号：" + $modMetaPath + ' 里没有 "version"，拒绝写入 verification.json。') -ForegroundColor Red
    exit 1
}

$unresolvedJson = @($unresolvedList | ForEach-Object {
    [ordered]@{
        kind = $_.Kind
        fullName = $_.FullName
        featureGroup = $_.FeatureGroup
        reason = $_.Reason
    }
})


$ver = [ordered]@{
    mod = 'MarqueeDelete'
    version = $modVersion
    gameVersion = $gameVersion
    gameVersionSource = $gameVersionSource
    gameInstallDir = $managed
    GameMvid = $mvid
    GameHash = $gameHash
    ModHash = $modHash
    builtWith = 'roslyn csc.exe (net472)'
    resolvedMemberReferences = $resolvedMembers
    resolvedTypeReferences = $resolvedTypes
    unresolvedMemberReferences = $unresolvedList.Count
    baselineDiff = [ordered]@{
        memberDiff = $diff.memberDiff
        typeDiff = $diff.typeDiff
        mvidMatch = (-not $diff.mvidChanged)
    }
    unresolvedDetails = $unresolvedJson
    # 只写本脚本真的验证过的事实。UI 构建、单元测试、兼容边界这几项都在别处跑，
    # 写死在这里等于让证据文件说谎：在没有 UI 包的副本里，它照样会声称 webpack 构建通过。
    # 谁跑了那些步骤，谁就在自己的产物里报告，不要替别人签字。
    verifiedByThisRun = @(
        ('Cecil resolution against the installed Managed assemblies: ' + $resolvedMembers + ' member refs, ' + $resolvedTypes + ' type refs, ' + $unresolvedList.Count + ' unresolved'),
        ('Game MVID read from the installed Game.dll: ' + $mvid + ' (' + $(if ($diff.hasBaseline) { 'recorded baseline match: ' + (-not $diff.mvidChanged) } else { 'no baseline recorded, nothing to compare against' }) + ')'),
        ('Game version string extracted from Game.dll (not official version metadata): ' + $gameVersionSource + ' (declared supported target ' + $(if ($declaredGameVersion -ne '') { $declaredGameVersion + ', match: ' + $gameVersionMatchesDeclared } else { 'not declared in Compatibility.cs, cannot compare' }) + ')'),
        ('Artifact hash of dist\MarqueeDelete.dll: ' + $modHash)
    )
}

# verification.json 是「这个具体产物通过校验」的声明。校验没通过就不写：失败时留下它，
# 会让 install.ps1 把一份假的「已校验」证据装进游戏。宁可没有这个文件，也不要一份假的。
if ($unresolvedList.Count -gt 0) {
    Write-Host ("校验未通过（unresolved " + $unresolvedList.Count + " 项），未写入 verification.json。") -ForegroundColor Red
    exit 1
}

$ver | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'verification.json') -Encoding UTF8
exit 0

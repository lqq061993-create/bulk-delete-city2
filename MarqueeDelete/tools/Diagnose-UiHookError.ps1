#Requires -Version 5.1
<#
.SYNOPSIS
    只读扫描：找出哪个已安装的 CS2 模组会让整轮 UI 注册抛出
    "Module GameTopLeft was not found."，从而导致所有模组 UI 消失。

.DESCRIPTION
    游戏内 moduleRegistry.append(target, X) 在 X 不是函数时，会把 target
    (GameTopLeft / Game / Editor / Menu 等挂载点) 当成模块路径传给 extend()，
    而挂载点并不在注册表里，于是抛异常并中断整轮 UI 注册。

    本脚本静态解析每个 .mjs，精确解析第二参数是否为函数值：
      * function 声明 / 函数表达式 / 箭头函数（含被括号包裹的）
      * 参照标识符的实际定义递归判定
      * 显式识别 React.memo() / forwardRef() 结果、对象、字面量

    不修改任何文件。

.PARAMETER Bisect
    额外输出二分排查建议（按目录分组，便于在播放集里对半停用）。

.EXAMPLE
    pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Diagnose-UiHookError.ps1
#>
[CmdletBinding()]
param(
    [switch]$Bisect,
    [string]$UserDataPath
)

$ErrorActionPreference = 'Stop'

if (-not $UserDataPath) {
    $UserDataPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
}

$hookTargets = @('GameTopLeft', 'GameTopRight', 'GameBottomRight', 'UniversalModMenu', 'Game', 'Editor', 'Menu')

function Get-ChildMjs {
    param([string]$Root)
    if (-not (Test-Path -LiteralPath $Root)) { return @() }
    Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.mjs' -File -ErrorAction SilentlyContinue
}

function Split-TopLevelArgs {
    param([string]$Text)
    $result = New-Object System.Collections.ArrayList
    $depth = 0
    $current = New-Object System.Text.StringBuilder
    $quote = $null
    for ($i = 0; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($quote) {
            [void]$current.Append($ch)
            if ($ch -eq $quote -and ($i -eq 0 -or $Text[$i - 1] -ne '\')) { $quote = $null }
            continue
        }
        if ($ch -eq '"' -or $ch -eq "'" -or $ch -eq '`') { $quote = $ch; [void]$current.Append($ch); continue }
        if ($ch -eq '(' -or $ch -eq '[' -or $ch -eq '{') { $depth++; [void]$current.Append($ch); continue }
        if ($ch -eq ')' -or $ch -eq ']' -or $ch -eq '}') { $depth--; [void]$current.Append($ch); continue }
        if ($ch -eq ',' -and $depth -eq 0) {
            [void]$result.Add($current.ToString().Trim())
            [void]$current.Clear()
            continue
        }
        [void]$current.Append($ch)
    }
    if ($current.Length -gt 0 -or $result.Count -gt 0) { [void]$result.Add($current.ToString().Trim()) }
    return @($result)
}

function Get-CallArgumentText {
    param([string]$Source, [int]$OpenParenIndex)
    $depth = 0
    $quote = $null
    for ($i = $OpenParenIndex; $i -lt $Source.Length; $i++) {
        $ch = $Source[$i]
        if ($quote) {
            if ($ch -eq $quote -and $Source[$i - 1] -ne '\') { $quote = $null }
            continue
        }
        if ($ch -eq '"' -or $ch -eq "'" -or $ch -eq '`') { $quote = $ch; continue }
        if ($ch -eq '(') { $depth++ }
        elseif ($ch -eq ')') {
            $depth--
            if ($depth -eq 0) { return $Source.Substring($OpenParenIndex + 1, $i - $OpenParenIndex - 1) }
        }
    }
    return $null
}

# 去掉最外层成对的圆括号：((()=>x)) -> ()=>x
function Remove-OuterParens {
    param([string]$Text)
    $t = $Text.Trim()
    while ($t.Length -ge 2 -and $t[0] -eq '(' -and $t[$t.Length - 1] -eq ')') {
        $depth = 0
        $balanced = $true
        for ($i = 0; $i -lt $t.Length; $i++) {
            if ($t[$i] -eq '(') { $depth++ }
            elseif ($t[$i] -eq ')') {
                $depth--
                if ($depth -eq 0 -and $i -ne $t.Length - 1) { $balanced = $false; break }
            }
        }
        if (-not $balanced) { break }
        $t = $t.Substring(1, $t.Length - 2).Trim()
    }
    return $t
}

# 判定表达式在运行时是否为 function
# 返回 $true / $false / $null(无法判定)
function Test-IsFunctionValue {
    param(
        [string]$Expr,
        [hashtable]$Defs,
        [System.Collections.Generic.HashSet[string]]$FnNames,
        [int]$Depth = 0
    )

    if ([string]::IsNullOrWhiteSpace($Expr)) { return $false }
    if ($Depth -gt 6) { return $null }

    $t = Remove-OuterParens -Text $Expr
    if ($t.Length -eq 0) { return $false }

    # IIFE：以 (function 或 (() => 开头并以 )(...) 形式立刻调用 -> 结果不是函数
    # 注意 Remove-OuterParens 已经剥掉一层，这里检查 "()=>...(...)" 这类不现实，忽略。

    # 直接写法
    if ($t -match '^(async\s+)?function\b') { return $true }
    if ($t -match '^class\b') { return $true }
    if ($t -match '^(async\s*)?(\([^)]*\)|[A-Za-z_\$][\w\$]*)\s*=>') { return $true }

    # 具名函数声明（同作用域）
    if ($t -match '^[A-Za-z_\$][\w\$]*$') {
        $id = $t
        if ($FnNames.Contains($id)) { return $true }
        if (-not $Defs.ContainsKey($id)) { return $null }
        return Test-IsFunctionValue -Expr $Defs[$id] -Defs $Defs -FnNames $FnNames -Depth ($Depth + 1)
    }

    # React.memo(...) / forwardRef(...) -> object
    if ($t -match '^(\(0,\s*)?[A-Za-z_\$][\w\$]*\.(memo|forwardRef)\s*\(') { return $false }
    if ($t -match '^\(' -and $t -match '\.(memo|forwardRef)\s*\(') { return $false }

    # 对象字面量 / 数组 / 字面量 / 数字
    if ($t -match '^[\[{]') { return $false }
    if ($t -match '^\d') { return $false }
    if ($t -match '^(true|false|null|undefined)$') { return $false }

    # 成员访问 a.b / a?.b —— 可能是函数，也可能不是；保守返回 null
    if ($t -match '^[A-Za-z_\$][\w\$]*(\?\.|\.[A-Za-z_\$])') { return $null }

    return $false
}

# ---------------------------------------------------------------- 扫描
$roots = @(
    @{ Label = 'LOCAL'; Path = (Join-Path $UserDataPath 'Mods') },
    @{ Label = 'PDX';   Path = (Join-Path $UserDataPath '.cache\Mods\pdx_mods') }
)

$findings = New-Object System.Collections.ArrayList
$perMod = @{}
$seenFiles = New-Object System.Collections.Generic.HashSet[string]

foreach ($root in $roots) {
    foreach ($file in (Get-ChildMjs -Root $root.Path)) {
        $key = "$($root.Label)/$($file.Directory.Name)/$($file.Name)"
        if (-not $seenFiles.Add($key)) { continue }

        try { $source = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 }
        catch { continue }
        if ($source -notmatch '\.(append|extend)\(') { continue }

        # ---- 函数名集合：function 声明 + 形如 name=function / name=()=> 的定义
        $fnNames = New-Object System.Collections.Generic.HashSet[string]
        foreach ($m in [regex]::Matches($source, 'function\s+([A-Za-z_\$][\w\$]*)\s*\(')) {
            [void]$fnNames.Add($m.Groups[1].Value)
        }
        foreach ($m in [regex]::Matches($source, '(?:^|[;,{(\s])(?:const\s+|let\s+|var\s+)?([A-Za-z_\$][\w\$]*)\s*=\s*(?:async\s+)?(?:function\b|\(?[^=;{}]{0,60}?\)?\s*=>)')) {
            [void]$fnNames.Add($m.Groups[1].Value)
        }

        # ---- 标识符定义表（RHS 片段），用于递归判定
        $defs = @{}
        foreach ($m in [regex]::Matches($source, '(?:^|[;,{(\s])(?:const\s+|let\s+|var\s+)?([A-Za-z_\$][\w\$]*)\s*=\s*')) {
            $id = $m.Groups[1].Value
            if ($defs.ContainsKey($id)) { continue }
            $start = $m.Index + $m.Length
            $len = [Math]::Min(220, $source.Length - $start)
            if ($len -gt 0) { $defs[$id] = $source.Substring($start, $len) }
        }

        $perMod[$key] = @{ Local = 0; Hook = 0; Problems = 0 }

        foreach ($m in [regex]::Matches($source, '\.(append|extend)\(')) {
            $kind = $m.Groups[1].Value
            $argsText = Get-CallArgumentText -Source $source -OpenParenIndex ($m.Index + $m.Length - 1)
            if ($null -eq $argsText) { continue }
            $args = Split-TopLevelArgs -Text $argsText
            if ($args.Count -eq 0) { continue }

            $first = $args[0]
            if ($first -notmatch '^["'']([^"'']+)["'']$') { continue }
            $firstValue = $Matches[1]

            if ($hookTargets -contains $firstValue) { $perMod[$key].Hook++ } else { $perMod[$key].Local++ }

            # extend/override 指向挂载点 => 必定抛异常
            if ($kind -ne 'append') {
                if ($hookTargets -contains $firstValue) {
                    [void]$findings.Add([pscustomobject]@{
                        Severity = 'FATAL'
                        Mod      = $key
                        Call     = "$kind(`"$firstValue`", ...)"
                        Reason   = 'extend/override 的第一个参数必须是模块路径；挂载点会抛 "Module ... was not found."'
                    })
                    $perMod[$key].Problems++
                }
                continue
            }

            if (-not ($hookTargets -contains $firstValue)) { continue }

            if ($args.Count -lt 2) {
                [void]$findings.Add([pscustomobject]@{
                    Severity = 'FATAL'
                    Mod      = $key
                    Call     = "append(`"$firstValue`")"
                    Reason   = '缺少第二个参数（组件），append 会走 extend 分支并抛异常'
                })
                $perMod[$key].Problems++
                continue
            }

            $second = $args[1]

            # append(modulePath, exportName, Component) 三参形式：第二参是字符串导出名
            if ($args.Count -ge 3 -and (Remove-OuterParens $second) -match '^["''][\w\./]+["'']$') { continue }

            $isFn = Test-IsFunctionValue -Expr $second -Defs $defs -FnNames $fnNames
            if ($isFn -eq $true) { continue }

            $reason = if ($isFn -eq $null) {
                '第二参数无法静态判定（成员访问）；若运行时为 undefined 则必定抛异常'
            } elseif ((Remove-OuterParens $second) -match '\.(memo|forwardRef)\s*\(') {
                '第二参数是 React.memo(...)/forwardRef(...) 的返回值（object，不是函数）'
            } elseif ((Remove-OuterParens $second) -match '^[\[{]') {
                '第二参数是对象/数组字面量'
            } elseif ((Remove-OuterParens $second) -match '^(\d|true$|false$|null$|undefined$)') {
                '第二参数是字面量'
            } else {
                '第二参数求值结果不是函数'
            }

            $short = $second
            if ($short.Length -gt 60) { $short = $short.Substring(0, 60) + '...' }

            [void]$findings.Add([pscustomobject]@{
                Severity = 'FATAL'
                Mod      = $key
                Call     = "append(`"$firstValue`", $short)"
                Reason   = $reason
            })
            $perMod[$key].Problems++
        }
    }
}

# ---------------------------------------------------------------- 报告
Write-Host ''
Write-Host '========================================================' -ForegroundColor Cyan
Write-Host ' CS2 UI hook diagnosis (read-only)' -ForegroundColor Cyan
Write-Host '========================================================' -ForegroundColor Cyan
Write-Host "UserDataPath: $UserDataPath"
Write-Host ''

$logUi = Join-Path $UserDataPath 'Logs\UI.log'
if (Test-Path -LiteralPath $logUi) {
    $hits = Select-String -LiteralPath $logUi -Pattern 'Module GameTopLeft was not found' -ErrorAction SilentlyContinue
    if ($hits) {
        Write-Host "[LOG] UI.log contains $($hits.Count) occurrence(s) of 'Module GameTopLeft was not found.'" -ForegroundColor Red
        foreach ($h in ($hits | Select-Object -Last 3)) { Write-Host "      $($h.Line.Trim())" -ForegroundColor DarkGray }
    } else {
        Write-Host '[LOG] UI.log has no such error.' -ForegroundColor Green
    }
} else {
    Write-Host "[LOG] not found: $logUi" -ForegroundColor Yellow
}

Write-Host ''
if ($findings.Count -eq 0) {
    Write-Host 'No statically suspicious append(hookTarget, ...) call found.' -ForegroundColor Green
    Write-Host 'If the log still shows the error, use -Bisect or see tools\UI-DISAPPEARED.md' -ForegroundColor Yellow
} else {
    Write-Host "$($findings.Count) suspicious call(s):" -ForegroundColor Red
    $findings | Format-Table -AutoSize -Wrap Severity, Mod, Call, Reason
    Write-Host ''
    Write-Host 'Handle these first:' -ForegroundColor Yellow
    $findings | Group-Object Mod | Sort-Object Count -Descending | ForEach-Object {
        Write-Host "  * $($_.Name)  ($($_.Count))" -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '--- per-mod hook statistics ---' -ForegroundColor Cyan
$perMod.GetEnumerator() |
    Sort-Object { $_.Value.Problems } -Descending |
    ForEach-Object {
        $v = $_.Value
        $color = if ($v.Problems -gt 0) { 'Red' } else { 'DarkGray' }
        Write-Host ("  {0,-70} hook={1,-3} local={2,-3} problems={3}" -f $_.Key, $v.Hook, $v.Local, $v.Problems) -ForegroundColor $color
    }

if ($Bisect) {
    Write-Host ''
    Write-Host '--- bisect guidance ---' -ForegroundColor Cyan
    $dirs = @(Get-ChildItem -LiteralPath (Join-Path $UserDataPath '.cache\Mods\pdx_mods') -Directory -ErrorAction SilentlyContinue)
    $half = [Math]::Ceiling($dirs.Count / 2)
    Write-Host ''
    Write-Host "Group A (enable ONLY these $half dirs first):" -ForegroundColor Yellow
    $dirs | Select-Object -First $half | ForEach-Object { Write-Host "    $($_.Name)" }
    Write-Host ''
    Write-Host 'Group B (enable ONLY these if A is clean):' -ForegroundColor Yellow
    $dirs | Select-Object -Skip $half | ForEach-Object { Write-Host "    $($_.Name)" }
    Write-Host ''
    Write-Host 'Disable temporarily: untick in Paradox Mods playset, or rename the dir with a .off suffix.' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host 'Full explanation: tools\UI-DISAPPEARED.md' -ForegroundColor DarkGray

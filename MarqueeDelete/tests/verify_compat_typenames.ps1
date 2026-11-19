# 校验 Compatibility.cs 里引用的所有游戏类型名是否真实存在
# 类型名写错会让整组被判定 Unavailable，导致模组完全失效。构建前必跑。
#
# 这个脚本以前有三种「查不到也报成功」的失效，这里全部堵上：
#   1) 找不到 Game.dll 就 exit 0 并打印 OK —— 等于什么都没查。
#   2) 正则一个类型名都没提取到时同样打印 OK（checked 0, missing 0）——
#      源文件写法一变，检查就静默退化成空转。
#   3) 用 PowerShell 哈希表查名（默认大小写不敏感，而 Type.GetType 大小写敏感），
#      并且只扫顶层类型、不递归 NestedTypes —— 嵌套类型会被误报成 MISSING。
# 现在：缺 Game.dll 或提取到 0 个名字都 exit 1；比较用 Ordinal；索引递归嵌套类型。
param(
    [string]$GameInstallDir = 'D:\steam\steamapps\common\Cities Skylines II',
    [string]$SourceFile = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourceFile)) { $SourceFile = Join-Path $root 'Compatibility.cs' }
if (-not (Test-Path -LiteralPath $SourceFile)) { throw ("Source not found: " + $SourceFile) }

$managed = $null
$candidates = @(
    $GameInstallDir,
    (Join-Path $GameInstallDir 'Cities2_Data\Managed'))
# 只有调用方没指定安装目录时才回落到本机默认路径。显式给的目录就按给的来，
# 否则「指定的目录里没有 Game.dll」会被默认路径悄悄救回来，缺 DLL 的分支永远走不到。
if (-not $PSBoundParameters.ContainsKey('GameInstallDir')) {
    $candidates += 'D:\steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
}
foreach ($candidate in $candidates) {
    if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'Game.dll'))) {
        $managed = $candidate
        break
    }
}

if (-not $managed) {
    Write-Host ("FAIL: Game.dll not found under '" + $GameInstallDir + "'") -ForegroundColor Red
    Write-Host "The check cannot run, so it does not pass. Point -GameInstallDir at the game install." -ForegroundColor Red
    exit 1
}

Add-Type -Path (Join-Path $managed 'Colossal.Mono.Cecil.dll')

# 类型名分散在 Game.dll 与若干 Colossal.* / Unity.* 程序集里，全部扫一遍。
# HashSet<string> 默认就是 ordinal 比较，与 Type.GetType 的大小写敏感语义一致。
$known = New-Object 'System.Collections.Generic.HashSet[string]'

function Add-TypeDefinition($types) {
    foreach ($ty in $types) {
        # 只收反射能解析的写法：Cecil 对嵌套类型用 'Outer/Inner'，而 Type.GetType 只认
        # 'Outer+Inner'（实测 'System.Environment/SpecialFolder' 返回 null）。生产用反射，
        # 所以索引里不放 '/' 写法，源码里写 '/' 就该判 MISSING，而不是被这条检查放过去。
        $null = $known.Add($ty.FullName.Replace('/', '+'))
        if ($ty.HasNestedTypes) { Add-TypeDefinition $ty.NestedTypes }
    }
}

foreach ($dll in (Get-ChildItem -LiteralPath $managed -Filter *.dll -File)) {
    try {
        $asm = [Colossal.Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll.FullName)
        try {
            foreach ($mod in $asm.Modules) { Add-TypeDefinition $mod.Types }
        }
        finally { $asm.Dispose() }
    }
    catch { }
}

# 从 Compatibility.cs 提取所有 CheckType("...") 的字符串。
# 去重必须按 ordinal（大小写敏感）：Sort-Object -Unique 用的是区域性比较，
# 'Game.Objects.Attached' 与 'Game.Objects.attached' 会被合并成一条，
# 于是拼错的那个悄悄消失，检查照样退出 0。
$src = Get-Content -Raw -Encoding UTF8 $SourceFile
$unique = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($match in [regex]::Matches($src, 'CheckType\(\s*"([^"]+)"')) {
    $null = $unique.Add($match.Groups[1].Value)
}
$names = @($unique)

if ($names.Count -eq 0) {
    Write-Host ('FAIL: no CheckType("...") name was extracted from ' + $SourceFile) -ForegroundColor Red
    Write-Host "The extractor matched nothing, so this check verified nothing." -ForegroundColor Red
    exit 1
}

$missing = 0
foreach ($n in $names) {
    if (-not $known.Contains($n)) {
        Write-Host ("MISSING: " + $n) -ForegroundColor Red
        $missing++
    }
}

Write-Host ("checked " + $names.Count + " type names against " + $known.Count + " indexed types, missing " + $missing)
if ($missing -gt 0) {
    Write-Host "FAIL: a wrong type name makes its group Unavailable and disables the mod (names are case sensitive)" -ForegroundColor Red
    exit 1
}
Write-Host "OK: all referenced game type names exist" -ForegroundColor Green
exit 0
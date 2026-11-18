# 校验 Compatibility.cs 里引用的所有游戏类型名是否真实存在
# 类型名写错会让整组被判定 Unavailable，导致模组完全失效。构建前必跑。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$managed = 'D:\steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
$gameDll = Join-Path $managed 'Game.dll'
if (-not (Test-Path $gameDll)) { Write-Host "SKIP: Game.dll not found"; exit 0 }

Add-Type -Path (Join-Path $managed 'Colossal.Mono.Cecil.dll')

# Type names live in several assemblies (Game.dll, Colossal.*.dll, Unity.*.dll), so scan them all.
$known = @{}
foreach ($dll in (Get-ChildItem $managed -Filter *.dll -File)) {
    try {
        $a = [Colossal.Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll.FullName)
        foreach ($t in $a.Modules) { foreach ($ty in $t.Types) { $known[$ty.FullName] = $true } }
    } catch { }
}

# 从 Compatibility.cs 提取所有 CheckType("...") 的字符串
$src = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'Compatibility.cs')
$matches2 = [regex]::Matches($src, 'CheckType\(\s*"([^"]+)"')
$names = $matches2 | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

$missing = 0
foreach ($n in $names) {
    if (-not $known.ContainsKey($n)) {
        Write-Host ("MISSING: " + $n) -ForegroundColor Red
        $missing++
    }
}

Write-Host ("checked " + $names.Count + " type names, missing " + $missing)
if ($missing -gt 0) {
    Write-Host "FAIL: a wrong type name makes its group Unavailable and disables the mod" -ForegroundColor Red
    exit 1
}
Write-Host "OK: all referenced game type names exist" -ForegroundColor Green
exit 0

$ErrorActionPreference='Stop'
$managed='D:\steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
$out=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $out | Out-Null
$refs=@('mscorlib','netstandard','System','System.Core','Game','Unity.Entities','Unity.Collections','Unity.Mathematics','Unity.Burst','UnityEngine.CoreModule','UnityEngine.AudioModule','UnityEngine.InputLegacyModule','Unity.InputSystem','Colossal.Core','Colossal.Localization','Colossal.Logging','Colossal.Mathematics','Colossal.Collections','Colossal.IO.AssetDatabase','Colossal.UI','Colossal.UI.Binding') | ForEach-Object { '/reference:'+(Join-Path $managed ($_+'.dll')) }
$sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
$dll=Join-Path $out 'MarqueeDelete.dll'
if(Test-Path -LiteralPath $dll){Remove-Item -LiteralPath $dll -Force}
$csc='C:\Users\aaa20\Documents\Codex\2026-10-31\ne\work\roslyn\tasks\net472\csc.exe'
if(-not (Test-Path -LiteralPath $csc)){$csc=Join-Path $PSScriptRoot '..\PowerLine50000\tools\compiler\tasks\net472\csc.exe'}
if(-not (Test-Path -LiteralPath $csc)){throw "csc.exe not found: $csc"}
& $csc /nologo /noconfig /target:library /optimize+ /debug:portable /nostdlib+ "/out:$dll" $refs $sources
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
Write-Output ('Sources: ' + ($sources -join ', '))
Get-FileHash $dll -Algorithm SHA256

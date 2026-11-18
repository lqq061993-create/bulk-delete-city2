$ErrorActionPreference='Stop'
$name='MarqueeDelete'
if (Get-Process -Name Cities2 -ErrorAction SilentlyContinue) { throw 'Exit Cities: Skylines II before installing the local mod.' }
$src=Join-Path $PSScriptRoot 'dist'
$dll=Join-Path $src ($name+'.dll')
if(-not (Test-Path -LiteralPath $dll)){throw "Build first: $dll not found"}

$uiStage=Join-Path $src ('_ui_stage\Mods\'+$name)
$uiBundle=Join-Path $uiStage ($name+'.mjs')
if(-not (Test-Path -LiteralPath $uiBundle)){
  throw "UI bundle missing in stage: $uiBundle - run 'npm run build' in ui/ with CSII_USERDATAPATH=$src\_ui_stage"
}

$target=Join-Path $env:USERPROFILE ('AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\'+$name)
$marker=Join-Path $target ('.'+$name.ToLower()+'-owned')
if((Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath $marker)){
  throw "Refusing to overwrite a directory this project does not own: $target"
}
New-Item -ItemType Directory -Force $target | Out-Null

$hash=(Get-FileHash $dll -Algorithm SHA256).Hash
Copy-Item -LiteralPath $dll,(Join-Path $src ($name+'.pdb')) -Destination $target -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'mod.json') -Destination (Join-Path $target 'mod.json') -Force

# Copy staged UI bundle (mjs, css, images/) cleanly
Get-ChildItem -LiteralPath $uiStage | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
}

$verSrc=Join-Path $PSScriptRoot 'verification.json'
if(Test-Path -LiteralPath $verSrc){
  Copy-Item -LiteralPath $verSrc -Destination (Join-Path $target 'verification.json') -Force
}

Set-Content -LiteralPath $marker -Value $hash -NoNewline
Write-Output "Installed: $target"
Write-Output "SHA256: $hash"
Get-ChildItem -LiteralPath $target -Recurse -File | Select-Object FullName, Length

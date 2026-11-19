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

# verification.json is evidence for one specific build. Copying it next to a different dll would
# ship a false "verified" claim, so the recorded ModHash must be the hash of the dll being
# installed. No file at all is fine: nothing false gets installed, and the copy below is skipped.
$verSrc=Join-Path $PSScriptRoot 'verification.json'
if(Test-Path -LiteralPath $verSrc){
  $recordedHash=$null
  try { $recordedHash=(Get-Content -LiteralPath $verSrc -Raw -Encoding UTF8 | ConvertFrom-Json).ModHash } catch { $recordedHash=$null }
  if([string]::IsNullOrWhiteSpace($recordedHash)){
    throw "verification.json has no ModHash, so it cannot be matched against $dll. Re-run tests/verify_cecil.ps1, then install again."
  }
  if($recordedHash.ToUpperInvariant() -ne $hash.ToUpperInvariant()){
    throw ("verification.json describes a different build (ModHash " + $recordedHash + ") than $dll (" + $hash + "). Re-run build.ps1, then tests/verify_cecil.ps1, then install again. Nothing was installed.")
  }
}

Copy-Item -LiteralPath $dll,(Join-Path $src ($name+'.pdb')) -Destination $target -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'mod.json') -Destination (Join-Path $target 'mod.json') -Force

# Copy staged UI bundle (mjs, css, images/) cleanly
Get-ChildItem -LiteralPath $uiStage | ForEach-Object {
  Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
}

# Only reachable after the ModHash above matched this exact dll.
if(Test-Path -LiteralPath $verSrc){
  Copy-Item -LiteralPath $verSrc -Destination (Join-Path $target 'verification.json') -Force
} else {
  # No evidence exists for this dll. An older verification.json sitting in the target would describe
  # a different build, so it goes: the installed folder must not carry a claim about another dll.
  $staleVer=Join-Path $target 'verification.json'
  if(Test-Path -LiteralPath $staleVer){
    Remove-Item -LiteralPath $staleVer -Force
    Write-Output "Removed stale verification.json: this build has no evidence of its own."
  }
}

Set-Content -LiteralPath $marker -Value $hash -NoNewline
Write-Output "Installed: $target"
Write-Output "SHA256: $hash"
Get-ChildItem -LiteralPath $target -Recurse -File | Select-Object FullName, Length

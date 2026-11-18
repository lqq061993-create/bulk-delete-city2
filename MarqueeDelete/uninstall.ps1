$ErrorActionPreference='Stop'
$name='MarqueeDelete'
$target=Join-Path $env:USERPROFILE ('AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\'+$name)
$marker=Join-Path $target ('.'+$name.ToLower()+'-owned')
if(Get-Process -Name Cities2 -ErrorAction SilentlyContinue){
  throw 'Cities: Skylines II is running. Close the game first.'
}
if(-not (Test-Path -LiteralPath $target)){Write-Output 'Not installed.'; return}
if(-not (Test-Path -LiteralPath $marker)){
  throw "Refusing to delete a directory this project does not own: $target"
}
Remove-Item -LiteralPath $target -Recurse -Force
Write-Output ("Uninstalled: " + $target)
Write-Output 'The settings file under Logs/ may be removed manually if desired.'

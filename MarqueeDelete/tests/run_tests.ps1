$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'MarqueeDelete.Tests.csproj'
$testDll = Join-Path $PSScriptRoot 'bin\Release\net8.0\MarqueeDelete.Tests.dll'

& dotnet build $proj -c Release --no-restore --disable-build-servers --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }

& dotnet $testDll
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed" }

$regProj = Join-Path $PSScriptRoot 'DeletionRegression.csproj'
$regDll = Join-Path $PSScriptRoot 'bin\Release\net8.0\DeletionRegression.dll'

& dotnet build $regProj -c Release --no-restore --disable-build-servers --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "DeletionRegression build failed" }

& dotnet $regDll
if ($LASTEXITCODE -ne 0) { throw "DeletionRegression failed" }

# Undo harness: compiles the production restore path and drives two adjacent segments through
# separate batches. It needs the game's two math assemblies, which are pure managed; when that
# install is absent the harness cannot be built, and the run says so instead of implying a pass.
$harnessProj = Join-Path $PSScriptRoot 'UndoHarness.csproj'
$harnessDll = Join-Path $PSScriptRoot 'bin\Release\net8.0\UndoHarness.dll'
$managedDir = 'D:\steam\steamapps\common\Cities Skylines II\Cities2_Data\Managed'
$mathReady = (Test-Path (Join-Path $managedDir 'Unity.Mathematics.dll')) -and
             (Test-Path (Join-Path $managedDir 'Colossal.Mathematics.dll'))
if ($mathReady) {
    & dotnet build $harnessProj -c Release --disable-build-servers --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "UndoHarness build failed" }
    & dotnet $harnessDll
    if ($LASTEXITCODE -ne 0) { throw "UndoHarness failed" }
} else {
    Write-Host "SKIP: UndoHarness not run - missing $managedDir\Unity.Mathematics.dll or Colossal.Mathematics.dll"
}

# Type-name check: a wrong name makes its compatibility group Unavailable and the mod
# inert, so it belongs in the required set. It exits non-zero when it cannot run
# (no Game.dll, nothing extracted) as well as when a name is missing, so a skip
# can never pass as success.
$typenameScript = Join-Path $PSScriptRoot 'verify_compat_typenames.ps1'
$pwshCmd = Get-Command pwsh -ErrorAction SilentlyContinue
$hostExe = if ($null -ne $pwshCmd) { $pwshCmd.Source } else { (Get-Process -Id $PID).Path }
& $hostExe -NoProfile -ExecutionPolicy Bypass -File $typenameScript
if ($LASTEXITCODE -ne 0) { throw "verify_compat_typenames failed" }

# Evidence consistency: verification.json must describe this exact artifact, and the recorded MVID
# must equal the build Compatibility.cs declares as supported. Checking only "matches this machine"
# would let a run on a different game version produce self-consistent evidence for a build that was
# never verified against the declared target. It exits non-zero when it cannot run or when anything
# disagrees, so a missing check can never pass as success.
$evidenceScript = Join-Path $PSScriptRoot 'check_verification.ps1'
& $hostExe -NoProfile -ExecutionPolicy Bypass -File $evidenceScript
if ($LASTEXITCODE -ne 0) { throw "check_verification failed" }

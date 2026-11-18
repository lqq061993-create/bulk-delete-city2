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

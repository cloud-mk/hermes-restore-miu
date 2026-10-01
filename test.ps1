$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$programFile = Join-Path $PSScriptRoot 'dist\HermesRestore.exe'
if (-not (Test-Path -LiteralPath $programFile)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$testFile = Join-Path $PSScriptRoot 'dist\CoreTests.exe'
& $compiler /nologo "/out:$testFile" "/reference:$programFile" /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'tests\CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testFile
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }

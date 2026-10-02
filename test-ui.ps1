$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$programFile = Join-Path $PSScriptRoot 'dist\HermesRestore.exe'
if (-not (Test-Path -LiteralPath $programFile)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$testFile = Join-Path $PSScriptRoot 'dist\UIStartupTests.exe'
& $compiler /nologo "/out:$testFile" "/win32manifest:$(Join-Path $PSScriptRoot 'src\app.manifest')" "/reference:$programFile" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'tests\UIStartupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\app.config') -Destination ($testFile + '.config') -Force
& $testFile
if ($LASTEXITCODE -ne 0) { throw 'UI startup tests failed.' }

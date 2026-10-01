$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$outputDirectory = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$programFile = Join-Path $outputDirectory 'HermesRestore.exe'
$sourceFile = Join-Path $projectRoot 'src\HermesRestore.cs'
$artFile = Join-Path $projectRoot 'assets\cat-sidebar.png'
& $compiler /nologo /optimize+ /debug- /target:winexe "/out:$programFile" "/resource:$artFile,CatSidebar" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Management.dll $sourceFile
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'LICENSE') -Destination $outputDirectory
$archiveFile = Join-Path $outputDirectory 'HermesRestore-miu-edition.zip'
Compress-Archive -LiteralPath $programFile,(Join-Path $outputDirectory 'README.md'),(Join-Path $outputDirectory 'LICENSE') -DestinationPath $archiveFile -Force
$hashLines = @($programFile,$archiveFile) | ForEach-Object { $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))" }
$hashLines | Set-Content -LiteralPath (Join-Path $outputDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Output 'Built EXE, release ZIP and SHA256SUMS.txt.'

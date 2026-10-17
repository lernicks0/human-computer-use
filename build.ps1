param([string]$OutputName = 'Human.exe')
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Human requires Windows.' }
if ($OutputName -notmatch '^Human(?:-[a-z0-9]+)?\.exe$') { throw 'Invalid executable name.' }
$sourceDirectory = Join-Path $PSScriptRoot 'src'
$outputDirectory = Join-Path $PSScriptRoot 'dist'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$options = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Core.dll',
    "/win32manifest:$sourceDirectory\app.manifest", "/win32icon:$sourceDirectory\Human.ico", "/out:$outputDirectory\$OutputName")
$sources = @('Human.cs', 'CursorHalo.cs', 'StatusBanner.cs', 'CursorSession.cs', 'AssemblyInfo.cs') | ForEach-Object { Join-Path $sourceDirectory $_ }
& $compiler @options @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output (Join-Path $outputDirectory $OutputName)

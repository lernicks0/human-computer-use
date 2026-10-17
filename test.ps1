param([switch]$SkipBuild, [switch]$CursorSession)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$executable = Join-Path $PSScriptRoot 'dist\Human.exe'
$reportDirectory = Join-Path $PSScriptRoot 'test-results'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$testMode = if ($CursorSession) { '--verify-session' } else { '--self-test' }
$testProcess = Start-Process -FilePath $executable -ArgumentList $testMode, ('"{0}"' -f $reportDirectory) -WindowStyle Hidden -PassThru -Wait
$reportName = if ($CursorSession) { 'cursor-session-checks.txt' } else { 'verification.txt' }
$reportFile = Join-Path $reportDirectory $reportName
if (Test-Path -LiteralPath $reportFile) { Get-Content -LiteralPath $reportFile }
if ($testProcess.ExitCode -ne 0) { throw "Tests failed (exit $($testProcess.ExitCode))." }
if (-not (Test-Path -LiteralPath $reportFile)) { throw 'The test process did not create a report.' }

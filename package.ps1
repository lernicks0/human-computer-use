$ErrorActionPreference = 'Stop'
$releaseDirectory = Join-Path $PSScriptRoot 'dist\release'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\Human.exe') -Destination $releaseDirectory -Force
foreach ($document in @('README.md', 'LICENSE', 'SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $releaseDirectory -Force
}
$commit = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else {
    $revision = & git -C $PSScriptRoot rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0) { $revision } else { 'local-uncommitted-build' }
}
$buildInfo = [ordered]@{ source_commit = $commit; workflow_run = $env:GITHUB_RUN_ID; platform = 'Windows .NET Framework'; built_at_utc = [DateTime]::UtcNow.ToString('o'); signed_with_authenticode = $false }
$buildInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseDirectory 'build-info.json') -Encoding utf8
$portableFiles = @('Human.exe', 'README.md', 'LICENSE', 'SECURITY.md', 'build-info.json') | ForEach-Object { Join-Path $releaseDirectory $_ }
Compress-Archive -LiteralPath $portableFiles -DestinationPath (Join-Path $releaseDirectory 'Human-windows.zip') -Force
$checksums = foreach ($fileName in @('Human.exe', 'Human-windows.zip')) {
    $hash = Get-FileHash -LiteralPath (Join-Path $releaseDirectory $fileName) -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $fileName
}
$checksums | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Output $releaseDirectory

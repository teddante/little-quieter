$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Build.ps1')
& (Join-Path $PSScriptRoot 'Check.ps1')

$project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source\LittleQuieter.csproj') -Raw)
$version = $project.Project.PropertyGroup.Version
$packageName = "LittleQuieter-$version-win-x64.zip"
$packageFolder = Join-Path $PSScriptRoot 'work\packages'
New-Item -ItemType Directory -Path $packageFolder -Force | Out-Null
$packagePath = Join-Path $packageFolder $packageName
$files = @(
    'Install.ps1', 'README.md',
    'LATENCY-AND-PERFORMANCE.md', 'CONTRIBUTING.md', 'THIRD-PARTY-NOTICES.txt'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }
$files += Join-Path $PSScriptRoot 'work\publish\Little Quieter.exe'
Compress-Archive -LiteralPath $files -DestinationPath $packagePath -Force
$checksum = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  $packageName" | Set-Content -LiteralPath (Join-Path $packageFolder "LittleQuieter-$version-SHA256SUMS.txt") -Encoding ascii
Write-Output "Package: $packagePath"

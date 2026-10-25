$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'source\LittleQuieter.csproj'
$publishFolder = Join-Path $PSScriptRoot 'work\publish'
& dotnet publish $projectPath --configuration Release --output $publishFolder --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $publishFolder\Little Quieter.exe"

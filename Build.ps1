$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'source\LittleQuieter.csproj'
$publishFolder = Join-Path $PSScriptRoot 'work\publish'
& dotnet restore $projectPath --locked-mode --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
& dotnet publish $projectPath --configuration Release --self-contained false --no-restore -p:PublishSingleFile=true --output $publishFolder --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $publishFolder 'Little Quieter.exe') -Destination (Join-Path $PSScriptRoot 'Little Quieter.exe') -Force
Write-Output 'Built Little Quieter for the installed .NET 10 Windows Desktop runtime.'

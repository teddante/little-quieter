$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'source\LittleQuieter.csproj'
$publishFolder = Join-Path $PSScriptRoot 'work\publish'
$restoreArgs = @('restore', $projectPath, '--runtime', 'win-x64', '--verbosity', 'quiet')
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'source\packages.lock.json')) { $restoreArgs += '--locked-mode' }
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
& dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained false --no-restore -p:PublishSingleFile=true --output $publishFolder --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $publishFolder 'Little Quieter.exe') -Destination (Join-Path $PSScriptRoot 'Little Quieter.exe') -Force
Write-Output 'Built Little Quieter for the installed .NET 10 Windows Desktop runtime.'

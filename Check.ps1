[CmdletBinding(DefaultParameterSetName = 'Ordinary')]
param(
    [Parameter(ParameterSetName = 'Audio')][switch]$Audio,
    [Parameter(ParameterSetName = 'Running')][switch]$Running
)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'source\Checks.csproj'
& dotnet restore $projectPath --locked-mode --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Check dependency restore failed.' }
$arguments = @('run', '--project', $projectPath, '--configuration', 'Release', '--no-restore', '--no-launch-profile', '--verbosity', 'quiet', '--')
if ($Audio) { $arguments += '--audio' }
elseif ($Running) { $arguments += '--running' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }

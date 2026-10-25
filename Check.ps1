[CmdletBinding(DefaultParameterSetName = 'Ordinary')]
param(
    [Parameter(ParameterSetName = 'Audio')][switch]$Audio,
    [Parameter(ParameterSetName = 'Running')][switch]$Running
)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'checks\Checks.csproj'
$arguments = @('run', '--project', $projectPath, '--configuration', 'Release', '--no-launch-profile', '--verbosity', 'quiet', '--')
if ($Audio) { $arguments += '--audio' }
elseif ($Running) { $arguments += '--running' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
if (!$Audio -and !$Running) { & (Join-Path $PSScriptRoot 'checks\Install.Checks.ps1') }

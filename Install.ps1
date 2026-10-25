[CmdletBinding(SupportsShouldProcess)]
param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$sourceProgram = Join-Path $PSScriptRoot 'Little Quieter.exe'
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'source\LittleQuieter.csproj')) {
    $sourceProgram = Join-Path $PSScriptRoot 'work\publish\Little Quieter.exe'
}
if (!(Test-Path -LiteralPath $sourceProgram)) { throw 'Build Little Quieter first.' }
$installedProgram = Join-Path $env:LOCALAPPDATA 'Programs\LittleQuieter\Little Quieter.exe'
if (!$PSCmdlet.ShouldProcess($installedProgram, 'Install Little Quieter (preserve settings and startup)')) { return }
$arguments = @('--install')
if ($NoLaunch) { $arguments += '--no-launch' }
$installer = Start-Process -FilePath $sourceProgram -ArgumentList $arguments -WindowStyle Hidden -PassThru
# Wait for setup itself; Start-Process -Wait also waits for the background app it launches.
$installer.WaitForExit()
if ($installer.ExitCode -ne 0) { throw 'Installation failed. Check that Little Quieter is closed and the .NET 10 Desktop Runtime is installed.' }
Write-Output "Installed: $installedProgram"
Write-Output 'Existing settings and startup choice preserved. New installations do not enable startup.'

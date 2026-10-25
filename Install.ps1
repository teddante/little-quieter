[CmdletBinding(SupportsShouldProcess)]
param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$sourceProgram = Join-Path $PSScriptRoot 'Little Quieter.exe'
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'source\LittleQuieter.csproj')) {
    $sourceProgram = Join-Path $PSScriptRoot 'work\publish\Little Quieter.exe'
}
$installFolder = Join-Path $env:LOCALAPPDATA 'Programs\LittleQuieter'
$installedProgram = Join-Path $installFolder 'Little Quieter.exe'
if (!(Test-Path -LiteralPath $sourceProgram)) { throw 'Build Little Quieter first.' }
$runtimeList = & dotnet --list-runtimes
if ($LASTEXITCODE -ne 0 -or !($runtimeList -match '^Microsoft.WindowsDesktop.App 10\.')) {
    throw 'Install the x64 .NET 10 Windows Desktop runtime before installing Little Quieter.'
}
$firstInstall = !(Test-Path -LiteralPath $installedProgram)
if (!$PSCmdlet.ShouldProcess($installedProgram, 'Install Little Quieter and create a Start menu shortcut (enable startup on first install)')) { return }

# Ask only this app's installed process to exit before replacing its executable.
$running = @(Get-Process -Name 'Little Quieter' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedProgram })
if ($running.Count -gt 0) {
    Start-Process -FilePath $installedProgram -ArgumentList '--exit' -WindowStyle Hidden -Wait
    foreach ($process in $running) {
        if (!$process.WaitForExit(5000)) { throw 'Close Little Quieter from its tray menu, then retry the install.' }
    }
}
[IO.Directory]::CreateDirectory($installFolder) | Out-Null
Copy-Item -LiteralPath $sourceProgram -Destination $installedProgram -Force
if ((Get-FileHash -LiteralPath $sourceProgram).Hash -ne (Get-FileHash -LiteralPath $installedProgram).Hash) { throw 'The installed copy did not match the build.' }
$notices = Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt'
if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination $installFolder -Force }
if ($firstInstall) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (!(Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey -Force | Out-Null }
    New-ItemProperty -LiteralPath $runKey -Name 'LittleQuieter' -Value ('"' + $installedProgram + '" --background') -PropertyType String -Force | Out-Null
}

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Little Quieter.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = $installedProgram
$shortcut.WorkingDirectory = $installFolder
$shortcut.Description = 'Open Little Quieter microphone volume monitor'
$shortcut.Save()
if (!$NoLaunch) { Start-Process -FilePath $installedProgram -ArgumentList '--background' -WindowStyle Hidden }
Write-Output "Installed: $installedProgram"
if ($firstInstall) { Write-Output 'Starts listening in the system tray when you sign into Windows.' }
else { Write-Output 'Existing settings and startup choice preserved.' }
Write-Output "Start menu shortcut: $shortcutPath"

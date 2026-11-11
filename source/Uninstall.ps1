[CmdletBinding(SupportsShouldProcess)]
param()
$ErrorActionPreference = 'Stop'
$operationLock = $null
$ownsLock = $false
try {
    $expectedFolder = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\LittleQuieter')).TrimEnd('\')
    $installFolder = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    if ($installFolder -ne $expectedFolder) { throw 'Run the uninstaller from the Little Quieter installation folder.' }
    if (!$PSCmdlet.ShouldProcess($installFolder, 'Uninstall Little Quieter (keep personal settings)')) { return }
    $operationLock = [Threading.Mutex]::new($false, 'Local\LittleQuieter.Install.v1')
    try { $ownsLock = $operationLock.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $ownsLock = $true }
    if (!$ownsLock) { throw 'Another Little Quieter installation or removal is in progress. Try again when it finishes.' }
    $program = Join-Path $installFolder 'Little Quieter.exe'
    $running = @(Get-Process -Name 'Little Quieter' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $program })
    if ($running.Count -gt 0) {
        Start-Process -FilePath $program -ArgumentList '--exit' -WindowStyle Hidden -Wait
        foreach ($process in $running) {
            if (!$process.WaitForExit(5000)) { throw 'Close Little Quieter from its tray menu and try uninstalling again.' }
        }
    }
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $command = (Get-ItemProperty -LiteralPath $runKey -Name LittleQuieter -ErrorAction SilentlyContinue).LittleQuieter
    if ($command -eq ('"' + $program + '" --background')) {
        Remove-ItemProperty -LiteralPath $runKey -Name LittleQuieter
    }
    $registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LittleQuieter'
    if (Test-Path -LiteralPath $registration) { Remove-Item -LiteralPath $registration -Recurse }
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Little Quieter.lnk'
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
    foreach ($name in @('Little Quieter.exe', 'Little Quieter.exe.previous', 'THIRD-PARTY-NOTICES.txt', 'Uninstall.ps1')) {
        $file = Join-Path $installFolder $name
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
    }
    if (@(Get-ChildItem -LiteralPath $installFolder -Force).Count -eq 0) { Remove-Item -LiteralPath $installFolder }
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Little Quieter could not be removed') | Out-Null
    throw
} finally {
    if ($ownsLock) { $operationLock.ReleaseMutex() }
    if ($null -ne $operationLock) { $operationLock.Dispose() }
}

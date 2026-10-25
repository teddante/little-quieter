$ErrorActionPreference = 'Stop'
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('LittleQuieter-install-check-' + [guid]::NewGuid().ToString('N'))
$previousLocalAppData = $env:LOCALAPPDATA
$state = @{ Launches = 0; Removed = @(); RemovedStartup = $false }
function Assert($Valid, $Label) {
    if (!$Valid) { throw "FAIL: $Label" }
    Write-Output "PASS: $Label"
}

# Native C# checks exercise real copies, COM shortcuts and an isolated registry key.
# These mocks exercise the wrapper and removal without touching live registration.
function Start-Process {
    param($FilePath, $ArgumentList, $WindowStyle, [switch]$Wait, [switch]$PassThru)
    $state.Launches++
    $state.Program = $FilePath
    $state.Arguments = $ArgumentList
    if ($Wait) { throw 'The wrapper must not wait for the background child app.' }
    $process = [pscustomobject]@{ ExitCode = 0 }
    $process | Add-Member ScriptMethod WaitForExit {}
    $process
}
function Get-Process { param($Name, $ErrorAction) @() }
function Get-ItemProperty {
    param($LiteralPath, $Name, $ErrorAction)
    [pscustomobject]@{ LittleQuieter = ('"' + $state.Installed + '" --background') }
}
function Remove-ItemProperty {
    param($LiteralPath, $Name)
    if ($LiteralPath -ne 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -or $Name -ne 'LittleQuieter') { throw 'Unexpected registry removal.' }
    $state.RemovedStartup = $true
}
function Test-Path {
    param($LiteralPath)
    if ($LiteralPath -like 'HKCU:*') { return $true }
    Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath
}
function Remove-Item {
    param($LiteralPath, [switch]$Recurse)
    $state.Removed += $LiteralPath
}
try {
    New-Item -ItemType Directory -Path $sandbox | Out-Null
    $env:LOCALAPPDATA = Join-Path $sandbox 'local'
    $installer = Join-Path $sandbox 'Install.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Install.ps1') -Destination $installer
    $source = Join-Path $sandbox 'Little Quieter.exe'
    Set-Content -LiteralPath $source -Value 'test executable - never launched'
    & $installer -NoLaunch -WhatIf | Out-Null
    Assert ($state.Launches -eq 0) 'install preview makes no changes'
    & $installer -NoLaunch | Out-Null
    Assert ($state.Launches -eq 1 -and $state.Program -eq $source -and $state.Arguments -contains '--install' -and $state.Arguments -contains '--no-launch') 'PowerShell wrapper invokes native installation without launching monitoring'
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'source'), (Join-Path $sandbox 'work\publish') | Out-Null
    Set-Content -LiteralPath (Join-Path $sandbox 'source\LittleQuieter.csproj') -Value '<Project />'
    $published = Join-Path $sandbox 'work\publish\Little Quieter.exe'
    Set-Content -LiteralPath $published -Value 'newer executable - never launched'
    & $installer -NoLaunch | Out-Null
    Assert ($state.Program -eq $published) 'repository installation uses the published build'
    $installFolder = Join-Path $env:LOCALAPPDATA 'Programs\LittleQuieter'
    New-Item -ItemType Directory -Path $installFolder -Force | Out-Null
    $uninstaller = Join-Path $installFolder 'Uninstall.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\source\Uninstall.ps1') -Destination $uninstaller
    foreach ($name in @('Little Quieter.exe', 'Little Quieter.exe.previous', 'THIRD-PARTY-NOTICES.txt')) {
        Set-Content -LiteralPath (Join-Path $installFolder $name) -Value 'isolated removal test'
    }
    $state.Installed = Join-Path $installFolder 'Little Quieter.exe'
    & $uninstaller -WhatIf | Out-Null
    Assert ($state.Removed.Count -eq 0 -and !$state.RemovedStartup) 'uninstall preview preserves registration and files'
    & $uninstaller -Confirm:$false
    Assert ($state.RemovedStartup -and $state.Removed -contains 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LittleQuieter') 'uninstall removes only its startup entry and Windows app registration'
    Assert ($state.Removed -contains $state.Installed -and $state.Removed -contains $uninstaller -and !($state.Removed | Where-Object { $_ -like '*settings.xml' })) 'uninstall removes app files and keeps personal settings'
    Assert (!($state.Removed | Where-Object { $_ -match 'FxSound|MMDevices|AudioEndpoint' })) 'uninstall does not target Windows audio or FxSound'
}
finally {
    $env:LOCALAPPDATA = $previousLocalAppData
    $resolved = [IO.Path]::GetFullPath($sandbox)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot -or [IO.Path]::GetFileName($resolved) -notlike 'LittleQuieter-install-check-*') { throw 'Refusing unexpected test cleanup.' }
    Microsoft.PowerShell.Management\Remove-Item -LiteralPath $resolved -Recurse -Force
}

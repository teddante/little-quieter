$ErrorActionPreference = 'Stop'
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('LittleQuieter-install-check-' + [guid]::NewGuid().ToString('N'))
$previousLocalAppData = $env:LOCALAPPDATA
$state = @{ StartupWrites = 0; StartupCommand = $null; ShortcutSaved = $false }

# Exercise the actual installer against temporary files and an in-memory registry/shortcut.
function dotnet { $global:LASTEXITCODE = 0; 'Microsoft.WindowsDesktop.App 10.0.0 [test runtime]' }
function Get-Process { param($Name, $ErrorAction) @() }
function Start-Process { throw 'Installer unexpectedly tried to launch a process.' }
function Test-Path {
    param($LiteralPath)
    if ($LiteralPath -like 'HKCU:*') { return $true }
    Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath
}
function New-ItemProperty {
    param($LiteralPath, $Name, $Value, $PropertyType, [switch]$Force)
    $state.StartupWrites++
    $state.StartupCommand = $Value
}
function New-Object {
    param($ComObject)
    $shell = [pscustomobject]@{}
    $shell | Add-Member ScriptMethod CreateShortcut {
        param($Path)
        $shortcut = [pscustomobject]@{ TargetPath = ''; WorkingDirectory = ''; Description = '' }
        $shortcut | Add-Member ScriptMethod Save { $state.ShortcutSaved = $true }
        return $shortcut
    }
    return $shell
}
function Assert($Valid, $Label) {
    if (!$Valid) { throw "FAIL: $Label" }
    Write-Output "PASS: $Label"
}

try {
    New-Item -ItemType Directory -Path $sandbox | Out-Null
    $env:LOCALAPPDATA = Join-Path $sandbox 'local'
    $installer = Join-Path $sandbox 'Install.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Install.ps1') -Destination $installer
    $source = Join-Path $sandbox 'Little Quieter.exe'
    Set-Content -LiteralPath $source -Value 'test executable - never launched'
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\LittleQuieter\Little Quieter.exe'
    $settings = Join-Path $env:LOCALAPPDATA 'LittleQuieter\settings.xml'
    New-Item -ItemType Directory -Path (Split-Path $settings) -Force | Out-Null
    Set-Content -LiteralPath $settings -Value 'existing personal settings'
    $settingsHash = (Get-FileHash -LiteralPath $settings).Hash

    & $installer -NoLaunch -WhatIf | Out-Null
    Assert (!(Test-Path -LiteralPath $installed) -and $state.StartupWrites -eq 0 -and !$state.ShortcutSaved) 'install preview makes no changes'
    & $installer -NoLaunch | Out-Null
    Assert ((Get-FileHash -LiteralPath $installed).Hash -eq (Get-FileHash -LiteralPath $source).Hash -and $state.StartupWrites -eq 1 -and $state.ShortcutSaved) 'first installation copies the program, enables startup and creates a shortcut'
    $state.StartupCommand = 'existing startup command'
    & $installer -NoLaunch | Out-Null
    Assert ($state.StartupWrites -eq 1 -and $state.StartupCommand -eq 'existing startup command') 'updating preserves an existing startup command'
    $state.StartupCommand = $null
    & $installer -NoLaunch | Out-Null
    Assert ($state.StartupWrites -eq 1 -and $null -eq $state.StartupCommand -and (Get-FileHash -LiteralPath $settings).Hash -eq $settingsHash) 'updating preserves disabled startup and personal settings'

    New-Item -ItemType Directory -Path (Join-Path $sandbox 'source'), (Join-Path $sandbox 'work\publish') | Out-Null
    Set-Content -LiteralPath (Join-Path $sandbox 'source\LittleQuieter.csproj') -Value '<Project />'
    $published = Join-Path $sandbox 'work\publish\Little Quieter.exe'
    Set-Content -LiteralPath $published -Value 'newer published executable - never launched'
    & $installer -NoLaunch | Out-Null
    Assert ((Get-FileHash -LiteralPath $installed).Hash -eq (Get-FileHash -LiteralPath $published).Hash) 'repository installation uses the published build rather than a stale root executable'
}
finally {
    $env:LOCALAPPDATA = $previousLocalAppData
    $resolved = [IO.Path]::GetFullPath($sandbox)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot -or [IO.Path]::GetFileName($resolved) -notlike 'LittleQuieter-install-check-*') {
        throw 'Refusing to clean up an unexpected test directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

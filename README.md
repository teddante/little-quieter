# Little Quieter

[![Build](https://github.com/teddante/little-quieter/actions/workflows/build.yml/badge.svg)](https://github.com/teddante/little-quieter/actions/workflows/build.yml)

A Windows app that plays a steady alarm tone while you are too loud and stops when you quieten down. Runs locally in the system tray; no recordings or cloud services.

## Install and use

Requires **Windows 11 x64** and the [**.NET 10 Desktop Runtime**](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

1. Download and extract the ZIP from [Releases](https://github.com/teddante/little-quieter/releases/latest).
2. Double-click **Little Quieter Setup.exe**. It installs for your account, adds a Start menu shortcut and registers removal in Windows **Settings → Apps → Installed apps**. Updates preserve your settings and startup choice. `Install.ps1` remains available for command-line use.
3. New installations open visibly with listening paused and startup off. Choose your microphone, click **Learn my normal voice**, then **Start listening**. Adjust **Loudness limit** as needed: lower warns sooner. Enable startup when you want it.

Minimize to keep listening; double-click the tray icon to reopen. Closing the window exits. Use the startup checkbox to control automatic listening after sign-in.

The window resizes and uses the Windows light/dark colour mode selected when the app starts. Standard controls support high-contrast colours and keyboard navigation. The live meter exposes its value to assistive technology. **Show visual reminder** controls the separate reminder window; it does not stay on top or steal focus. Use its Dismiss button, Escape, or the tray menu to dismiss it.

Explicit microphone selections use endpoint IDs. A disconnected microphone stays selected as unavailable; the app pauses instead of opening another microphone. Old name-only selections migrate when their names match exactly one device.

For automatic hours, choose **From** and **to**, then enable **Listen on a daily schedule**. It is off by default. Times use your PC's local clock and repeat every day; overnight ranges such as 22:00–07:00 work. Outside those hours, the microphone and alarm streams close. Equal start/end times keep listening off until you choose different times. Keep the app running in the tray; it does not wake a sleeping PC. **Stop listening** pauses the current scheduled period; the next period resumes automatically, or you can start listening again yourself.

**Alarm output** affects only this app. Stop listening before changing devices. Windows audio and other apps keep their settings. Start with a low alarm volume, especially with headphones.

Little Quieter opens shared-mode Windows audio streams. It installs no audio drivers or virtual devices and does not change Windows default devices, endpoint volume/mute, FxSound configuration, or audio services.

The meter is relative microphone input, not calibrated room decibels. Other voices, TV and music can trigger it; headphones reduce alarm feedback. The EXE is unsigned.

Settings: `%LOCALAPPDATA%\LittleQuieter\settings.xml`. Installation: `%LOCALAPPDATA%\Programs\LittleQuieter`.

If settings cannot be read, the app pauses and preserves the original file. Repair it and restart, or choose **Back up and use new settings** to keep an exact backup before replacing it. Recovery starts with sound off.

To uninstall, use Windows **Settings → Apps → Installed apps → Little Quieter → Uninstall**. The local removal script closes only this app, removes its files, shortcut and registration, and keeps personal settings. It uses Windows PowerShell without changing execution policy. `Install.ps1 -WhatIf` previews installation without making changes.

## Build

With the **.NET 10 SDK**, run:

```powershell
.\Package.ps1
```

This builds the app, runs checks and writes a ZIP plus checksum to `work/packages/`. Source changes may be newer than the latest published release.

See [development](CONTRIBUTING.md), [measurements](LATENCY-AND-PERFORMANCE.md) and [dependency licences](THIRD-PARTY-NOTICES.txt). No open-source licence has been selected for the project code.

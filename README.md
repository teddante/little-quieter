# Little Quieter

[![Build and checks](https://github.com/teddante/little-quieter/actions/workflows/build.yml/badge.svg)](https://github.com/teddante/little-quieter/actions/workflows/build.yml)

A small Windows microphone monitor that plays a steady alarm tone while you are too loud and stops when you quieten down. It can listen in the system tray and start automatically when you sign in.

- A continuous 2.5 kHz tone with adjustable volume and a visual reminder.
- Direct loud/quiet decisions every 5 ms, with no added start delay or cooldown.
- Voice calibration, microphone selection and an alarm-only output selection.
- Shared Windows audio; no administrator rights, drivers, service or game hooks.
- Local processing: no recordings, transcription, telemetry or network access in the app.

## Download and run

Requires **Windows 11 x64** and the **x64 .NET 10 Windows Desktop Runtime**. Install the Desktop Runtime from [Microsoft's .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) if it is missing. Building requires the SDK instead.

1. Download the `LittleQuieter-<version>-win-x64.zip` file from the [latest published release](https://github.com/teddante/little-quieter/releases/latest). The source branch may contain changes awaiting release.
2. Extract the ZIP, then open `Little Quieter.exe`.
3. Choose your microphone and click **Learn my normal voice**. After two seconds of preparation, speak at a comfortable indoor volume for eight seconds.
4. Adjust **Loudness limit** if needed: lower warns sooner; higher allows more volume.

Start with a low alarm volume, especially with headphones. The executable is unsigned; Windows may show an unrecognized-app warning.

## Startup and background operation

To install with a Start menu shortcut and automatic background listening, run `Install.ps1` from PowerShell in the extracted folder:

```powershell
.\Install.ps1
```

It installs for your account, enables startup and starts listening immediately. No administrator rights are needed. You can also use the executable directly and enable startup in the app; keep that executable in a permanent location.

- **Start with Windows and listen in the background** controls startup. Windows may delay launch after sign-in.
- Minimize to keep listening in the tray. Double-click the tray icon to reopen the window.
- Closing the window with **×**, or choosing **Exit** in the tray menu, stops listening and exits for that session.
- **Test tone (2s)** plays an audible preview. **Alarm volume** changes the tone level; Windows volume and mute still apply.

The installation folder is `%LOCALAPPDATA%\Programs\LittleQuieter`. Settings are saved in `%LOCALAPPDATA%\LittleQuieter\settings.xml`. Startup uses your account's `Software\Microsoft\Windows\CurrentVersion\Run\LittleQuieter` entry.

To remove the app, disable its startup checkbox, exit it, and delete its installation folder and Start menu shortcut. You can delete its settings folder separately.

`Install.ps1 -WhatIf` previews the installation without stopping the app or changing files, startup or shortcuts.

## Audio routing and limitations

**Alarm output** changes only this app's output. New installations use **Windows default output**. To choose a physical output directly, stop listening, select the device, then start listening again. Windows defaults and other applications' routing are not changed. An unavailable explicitly selected output produces an error rather than redirecting the alarm to another speaker.

The 0–100 meter is relative microphone input, not calibrated room decibels. Microphone gain, distance and automatic processing affect it. The app cannot distinguish your voice from other people, TV or music. Keep the mic in its usual position during calibration.

Narrow filters around the alarm pitch reduce speaker-to-microphone feedback, but distortion or clipping can interfere. Headphones avoid that acoustic feedback. Hardware, wireless links and Windows audio buffers add delay beyond the 5 ms analysis window. Visual reminders may not appear over exclusive fullscreen games.

See [latency and resource measurements](LATENCY-AND-PERFORMANCE.md) for results from one development PC and their limits. Actual voice-to-ear latency and gaming/FPS impact have not been measured.

## Build and check

Clone this repository on Windows with the **.NET 10 SDK** installed, then run:

```powershell
.\Build.ps1
.\Check.ps1
```

`Build.ps1` creates `Little Quieter.exe` in the repository root. Intermediate outputs stay in the ignored `work/` folder. NuGet restores use the committed dependency lock files. GitHub Actions builds the app and runs the same ordinary checks without opening audio devices.

To build, run the ordinary checks and create the release ZIP and SHA-256 checksum in one step:

```powershell
.\Package.ps1
```

Packages are written to `work/packages/`. The package version comes from the application project. A fixed file list excludes settings, diagnostic reports and build intermediates. GitHub Actions uses this same packaging command and checks source formatting.

Optional local checks:

```powershell
.\Check.ps1 -Audio    # Native microphone/playback test at zero volume
.\Check.ps1 -Running  # Verify an installed app already listening, with startup enabled
```

The ordinary checks exercise callback gating, the stale-input watchdog, signal ramps, PCM/float formats, stereo handling, feedback rejection, calibration and settings migration. They do not play sound or access a microphone. Diagnostic tools in `source/Benchmarks.cs` can measure muted endpoint-clock timing and capture timestamps; generated reports are local and ignored by Git.

## Implementation and notices

C# / .NET 10 Windows Forms, with shared, event-driven WASAPI through pinned [NAudio 3.3.0](https://github.com/naudio/NAudio/releases/tag/v3.3.0). A prepared output stream emits silence between alarms; a gate changes its signal without reopening the device. UI summaries are throttled separately from the audio decision.

See [Windows implementation review](WINDOWS-REVIEW.md) for design details and validation limits. Dependency licences are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). No open-source licence has been selected for the original project code.

See [contributor guidance](CONTRIBUTING.md) for the source layout, formatting commands and validation expectations.

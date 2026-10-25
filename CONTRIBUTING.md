# Working on Little Quieter

Use Windows 11 x64 and the .NET 10 SDK. Dependency versions and hashes are committed in the two NuGet lock files. Change dependencies only when the change is justified; update the relevant lock files deliberately.

## Source layout

| Area | Files and responsibility |
|---|---|
| Audio capture | `AudioInput.cs` owns the selected microphone and bounded UI summaries; `MicrophoneAnalyzer.cs` decodes native samples into 5 ms readings. |
| Alarm decisions | `AlarmGate.cs` owns threshold decisions, preview lifetime and stale-input silencing; `AudioClock.cs` supplies monotonic time. |
| Playback | `AlarmTone.cs` owns the prepared shared output stream; `SteadyToneProvider.cs` generates its fixed tone and short gain ramps. |
| Feedback rejection | `AlarmRejectionFilter.cs` removes the narrow alarm band before input measurement. |
| Desktop and UI | `Program.cs` handles single-instance startup; `MainWindow.cs`, `LevelMeter.cs` and `ReminderWindow.cs` implement the interface; `DesktopIntegration.cs` owns startup registration and device notifications. |
| Settings and calibration | `Preferences.cs` validates and saves settings; `Levels.cs` maps microphone levels and calibrates the threshold. |
| Verification | `Checks.cs` contains ordinary and optional hardware checks; `Benchmarks.cs` contains silent timing diagnostics. These files are excluded from the shipped executable. |

Keep capture decisions independent of the UI timer. Do not add device creation, UI work, blocking I/O or managed allocations to the steady analysis/gating/rendering path. Use shared audio and app-only routing; leave global audio settings, other processes and games alone.

## Checks and formatting

```powershell
.\Check.ps1
dotnet format .\source\LittleQuieter.csproj --no-restore --verify-no-changes --severity warn
dotnet format .\source\Checks.csproj --no-restore --verify-no-changes --severity warn
.\Package.ps1
```

Follow `.editorconfig`; use short expression-bodied properties where they are clear. To apply only whitespace formatting, use `dotnet format whitespace` on the relevant project. To remove unused imports, use `dotnet format style` with `--diagnostics IDE0005 --severity warn`. Ordinary checks require no microphone or output device. Add focused regression checks for changes to decisions, signal handling or saved settings.

`Check.ps1 -Audio` opens the default microphone and shared playback at zero volume. `Check.ps1 -Running` only inspects an installed background instance. Neither produces an audible test. Do not run an audible tone, launch a game or change global audio settings as part of routine validation.

## Timing diagnostics

After building, these commands save timing metadata only:

```powershell
dotnet run --project .\source\Checks.csproj --configuration Release -- --endpoints
dotnet run --project .\source\Checks.csproj --configuration Release -- --clock-benchmark .\work\clock.json
dotnet run --project .\source\Checks.csproj --configuration Release -- --capture-timing .\work\capture.json
```

The clock command accepts an optional output endpoint ID after the report path. It mutes only its own session before activating a signal, then stops playback before restoring that session's mute state. Keep timing/resource reports in ignored local folders: they can contain device identifiers. Never commit recordings or personal settings.

Distinguish endpoint-clock timing, capture timestamps and actual acoustic delay. Keep historical measurements labelled with their tested version and machine; a refactor alone is not evidence of faster playback or better gaming performance.

`Package.ps1` is the single packaging entry point for local builds and CI. Its ZIP uses an explicit file list, and its checksum covers the completed ZIP. Installation is separate; use `Install.ps1 -WhatIf` for a non-mutating installer preview.

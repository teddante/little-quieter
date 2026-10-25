# Development

Use Windows 11 x64 and the .NET SDK selected by `global.json`. Open `LittleQuieter.slnx`.

- `source/`: app code; `MainWindow.Layout.cs` contains the window layout and event wiring, and `MainWindow.cs` handles monitoring and application state.
- `checks/`: tests and diagnostics. `work/`: ignored build output, with the executable in `work/publish/`.
- `Directory.Build.props` and `.editorconfig`: shared build and style settings.
- Restores use each project's dependency lock file. After deliberately changing dependencies, regenerate locks with `dotnet restore -p:RestoreLockedMode=false --force-evaluate`.

```powershell
.\Build.ps1
.\Check.ps1
dotnet format --no-restore --verify-no-changes --severity warn
.\Check.ps1 -Audio    # Silent hardware check at zero volume
.\Check.ps1 -Running  # Verify installed app, scheduled streams and startup
.\Package.ps1         # Build, check and package
```

The ordinary checks cover signal processing, settings recovery, microphone identity, daily scheduling, a hidden paused window, native installer copies, temporary COM shortcuts and an isolated per-user test registry key. Wrapper/removal checks simulate live registry operations. They do not open audio streams or change the installed app. `Check.ps1 -Audio` additionally exercises scheduled stream opening/closing at zero test volume. Installation from the repository uses `work/publish/`; run `Build.ps1` first. Installation never rewrites Run or StartupApproved registration.

`Package.ps1` includes a native **Little Quieter Setup.exe**, using the app's `--install` entry point. Installation stages and verifies the executable before replacement and keeps the previous EXE for rollback. Trusted publisher signing still requires an externally supplied code-signing certificate; do not generate or trust a self-signed certificate as a substitute.

For an off-screen UI render without opening audio, run `dotnet run --project checks/Checks.csproj -c Release -- --preview work/window.png`. Add `--dark` to render dark mode in that process only.

`DailySchedule` uses local calendar time for daily periods, including overnight ranges. Each active period is identified by its start date/time, so sleeping through an inactive period still resumes the next day. It issues one transition per period or configuration change; manual pauses and failures are not retried every UI tick. Audio processing and the stale-input watchdog retain their monotonic clock.

Keep audio callbacks allocation-free and independent of UI updates. Implementation types are internal; `Preferences` is public for XML serialization. Follow [AGENTS.md](AGENTS.md).

Optional timing tools save metadata only:

```powershell
dotnet run --project checks/Checks.csproj -c Release -- --endpoints
dotnet run --project checks/Checks.csproj -c Release -- --clock-benchmark work/clock.json
dotnet run --project checks/Checks.csproj -c Release -- --capture-timing work/capture.json
```

The clock test mutes only its own session; an optional output-device ID follows the report path. Keep reports and personal settings out of Git. Endpoint-clock timings are not acoustic latency.

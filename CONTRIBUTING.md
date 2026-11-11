# Development

Use Windows 11 x64 and the .NET SDK selected by `global.json`. Open `LittleQuieter.slnx`.

The SDK is pinned to an exact version because SDK patch updates can change implicit build dependencies. Update `global.json` and regenerate dependency locks together when upgrading it.

- `source/`: app code; `MainWindow.Layout.cs` contains layout and event wiring, `MainWindow.cs` handles monitoring/settings, and `MainWindow.Audio.cs` owns asynchronous audio transitions and cleanup.
- `checks/`: tests and diagnostics. `work/`: ignored build output, with the executable in `work/publish/`.
- `Directory.Build.props` and `.editorconfig`: shared build and style settings.
- Restores use each project's dependency lock file. After deliberately changing dependencies, regenerate locks with `dotnet restore -p:RestoreLockedMode=false --force-evaluate`.

```powershell
.\Build.ps1
.\Check.ps1
dotnet format --no-restore --verify-no-changes --severity warn
.\Check.ps1 -Audio    # Silent hardware check at zero volume
.\Check.ps1 -Running  # Report installed app's streams and startup registration
.\Package.ps1         # Build, check and package
```

The ordinary checks cover signal processing, settings recovery, microphone identity, unavailable outputs, daily scheduling, a hidden paused window, native installer copies, temporary COM shortcuts, an isolated per-user test registry key and a real uninstall preview under a Restricted parent policy. Wrapper/removal checks simulate live registry operations. They do not open audio streams or change the installed app. `Check.ps1 -Audio` additionally exercises scheduled stream opening/closing and restoration of the prior listening state after learning, at zero test volume. `Check.ps1 -Running` reports the installed app's actual state without assuming that listening or startup must be enabled. Installation from the repository uses `work/publish/`; run `Build.ps1` first. Installation never rewrites Run or StartupApproved registration.

`Package.ps1` includes a native **Little Quieter Setup.exe**, using the app's `--install` entry point. Installation stages and verifies the executable before replacement and keeps the previous EXE for rollback. Shortcuts use Windows' `IShellLinkW`/`IPersistFile` APIs directly, without scripting automation. The uninstaller and dependency notices are embedded in the executable, so installation always includes matching copies. Checks read back the shortcut's Unicode target and working directory, and verify the installed notices. The registered uninstaller uses a process-only execution-policy override for its bundled script, so removal works with Windows' default Restricted policy without changing saved policies. Group Policy still takes precedence. Trusted publisher signing still requires an externally supplied code-signing certificate; do not generate or trust a self-signed certificate as a substitute.

For an off-screen UI render without opening audio, run `dotnet run --project checks/Checks.csproj -c Release -- --preview work/window.png`. Add `--dark` for process-local dark mode, `--settings` to expand secondary settings, or `--narrow` for a narrow window.

Audio device opening and disposal run on worker threads. One UI transition owns the devices at a time; stale starts are discarded after cancellation, shutdown or a schedule boundary. Unexpected UI errors suspend the gate immediately and drain devices before reporting. The visual reminder has its own quiet-gap hold and dismissal state; it does not add delay to audio gating. Settings save only when changed, and failed replacements clean up their staging file. `WM_SETTINGCHANGE` reapplies the public WinForms system colour mode and documented DWM title-bar attribute. Small native-control subclasses expose protected handle recreation because WinForms 10 applies those themes on handle creation. Refreshes preserve values and suppress settings/device-change handlers.

`DailySchedule` uses local calendar time for daily periods, including overnight ranges. Each active period is identified by its start date/time, so sleeping through an inactive period still resumes the next day. It issues one transition per period or configuration change; manual pauses and failures are not retried every UI tick. Audio processing and the stale-input watchdog retain their monotonic clock.

Setup and removal share the named mutex `Local\LittleQuieter.Install.v1`; overlapping operations fail before taking snapshots or stopping the app. Installation stages files and snapshots the existing files, shortcut and uninstall registration before asking the running app to exit. A failed write restores those snapshots. Update restart arguments carry a pause timestamp separately from settings; they do not change the normal Windows startup command. To support upgrades from older releases, the installer reads capture-session activity without opening or modifying streams. Unknown activity resumes paused. Checks cover overlapping installs, rollback, relevant default-device changes, responsive pending transitions, immediate error suspension, reminder stability, meter repainting, theme updates and unchanged settings. Zero-volume hardware checks cover output-free monitoring/learning, explicit previews outside schedule hours, error cleanup and paused restarts.

Keep audio callbacks allocation-free and independent of UI updates. Implementation types are internal; `Preferences` is public for XML serialization. Follow [AGENTS.md](AGENTS.md).

Optional timing tools save metadata only:

```powershell
dotnet run --project checks/Checks.csproj -c Release -- --endpoints
dotnet run --project checks/Checks.csproj -c Release -- --clock-benchmark work/clock.json
dotnet run --project checks/Checks.csproj -c Release -- --capture-timing work/capture.json
```

The clock test mutes only its own session; an optional output-device ID follows the report path. It pairs the public player `GetPosition()` byte position with the midpoint of each monotonic-clock query, avoiding private dependency fields. Reports include query duration and use a different method from the historical raw-QPC measurements. Keep reports and personal settings out of Git. Endpoint-clock estimates are not acoustic latency.

# Development

Use Windows 11 x64 and the .NET SDK selected by `global.json`. Open `LittleQuieter.slnx`.

- `source/`: app code. `checks/`: tests and diagnostics. `work/`: ignored build output.
- `Directory.Build.props` and `.editorconfig`: shared build and style settings.
- Each project has a dependency lock file. Keep dependency changes deliberate.

```powershell
.\Build.ps1
.\Check.ps1
dotnet format --no-restore --verify-no-changes --severity warn
.\Check.ps1 -Audio    # Silent hardware check at zero volume
.\Check.ps1 -Running  # Installed app must be listening with startup enabled
.\Package.ps1         # Build, check and package
```

Keep audio callbacks allocation-free and independent of UI updates. Implementation types are internal; `Preferences` is public for XML serialization. Follow [AGENTS.md](AGENTS.md).

Optional timing tools save metadata only:

```powershell
dotnet run --project checks/Checks.csproj -c Release -- --endpoints
dotnet run --project checks/Checks.csproj -c Release -- --clock-benchmark work/clock.json
dotnet run --project checks/Checks.csproj -c Release -- --capture-timing work/capture.json
```

The clock test mutes only its own session; an optional output-device ID follows the report path. Keep reports and personal settings out of Git. Endpoint-clock timings are not acoustic latency.

# Measured latency and PC impact

Measured on one development PC on 25 October 2026. All tests were silent and used APIs/command-line tools, with no computer use. These are machine-specific observations, not performance guarantees for other systems. Device identifiers and private diagnostic files are not published.

## Timing results

The test exercised the production tone generator and the same WASAPI output path. Its diagnostic process was muted before any signal was activated. Generated signal markers were mapped to Windows' endpoint playback clock. Each run included 24 starts and 24 stops.

| Output route | Start median | Start p95 | Stop median | Stop p95 |
|---|---:|---:|---:|---:|
| Windows default: FxSound virtual endpoint | 57.42 ms | 60.86 ms | 57.91 ms | 62.12 ms |
| Direct alarm-only physical output, final repeat | **8.82 ms** | **9.39 ms** | **9.43 ms** | **10.27 ms** |

The direct route reduced these median output timings by about 84%. An earlier direct-output run measured 8.45 ms start / 9.58 ms stop, supporting the repeat result. These are observed command-to-endpoint-clock timings, rather than just requested buffer sizes.

**The full voice-to-ear delay remains unmeasured.** The numbers exclude the time to acquire and classify a new voice segment, and any sound-path delay after the endpoint clock, such as wireless transmission or speaker/headphone electronics. The FxSound row measures its virtual endpoint, not its final physical output. Signal markers use 1 ms energy buckets, so precision should not be interpreted below that scale. No physical sound was emitted for an acoustic reference measurement.

Additional capture measurements: 5 ms analysis windows; newest captured-sample age at callback arrival had a median of 0.040 ms and p95 of 0.261 ms, based on driver-reported timestamps. Analyzer processing per callback had a median of 0.0025 ms and p95 of 0.0061 ms. Those are separate stage measurements and must not be added together as a measured end-to-end percentile. Capture statistics exclude the first 20 startup packets.

An attempted muted process-loopback measurement returned only zeros and no detectable transitions. It was retained as a failed measurement, rather than reported as a latency result. The endpoint-clock method does not depend on unmuting the stream. [Microsoft describes the device position and its correlated timestamp](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclock-getposition).

## Changes applied

- On the benchmark machine, Little Quieter alone sent its alarm directly to the physical output. FxSound had an active playback session on that output before this selection. This is a local preference, not a hardcoded device.
- That machine's Windows default output remained **Speakers (FxSound Audio Enhancer)**. FxSound settings/executable hashes were unchanged, and its original process and active playback session remained present after the update. New installations use Windows default output until the user selects another device.
- Shared audio mode, ordinary process priority and Windows' Audio MMCSS task are retained. No drivers, game files, global audio defaults, system timer-resolution settings or scheduler settings were changed. There is no game injection, overlay hook or exclusive audio takeover.
- The sound decision still runs on every 5 ms reading. UI summaries were reduced from 200 to 20 per second; quiet hidden windows refresh less often; unchanged gate decisions avoid redundant cross-thread output writes. The independent stale-input watchdog now checks every 20 ms. These reductions do not delay normal loud/quiet transitions.

## Resource samples

Two separate roughly 20-second normal-listening windows were sampled on the 28-logical-processor benchmark PC:

| Measurement | Before | After |
|---|---:|---:|
| CPU, normalized to total machine capacity | 0.055% | **0.033%** |
| CPU, expressed as a fraction of one logical processor | 1.54% | **0.92%** |
| Private process memory, average | 23.5 MiB | **14.4 MiB** |

The final resident working set averaged 61.4 MiB, including shared runtime/library pages. These short, separate samples are observations under the then-current desktop workload, not a controlled gaming benchmark or a guarantee of the same usage everywhere. Handle/thread counts were also sampled; short checks cannot establish long-term leak freedom.

No actual game/FPS test, prolonged load test or acoustic latency test was performed. Software and drivers can still have bugs; the scoped changes and low measured resource use do not justify promising zero impact in every game.

## Verification and rollback

Release build and all 39 focused checks passed, followed by silent native audio and installed-session checks. The installed executable matches the delivered build. Existing limit/volume and startup were retained.

To use the system default alarm route: open Little Quieter, stop listening, select **Windows default output** under **Alarm output**, then start listening again. This changes only Little Quieter. A missing explicitly selected endpoint does not silently redirect a loud alarm to another speaker.

Raw timing/resource metadata was retained locally, with no recorded microphone audio. It is excluded from the public repository because it contains machine-specific diagnostics. The source includes diagnostic commands through `Checks.csproj`, using `--clock-benchmark`, `--capture-timing` and `--benchmark` for the retained loopback attempt.

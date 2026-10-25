# Latency and resource use

Historical **v1.3.1** results from one PC, 25 October 2026:

| Alarm route | Start median | Stop median |
|---|---:|---:|
| FxSound virtual output | 57.4 ms | 57.9 ms |
| Direct physical output | 8.8 ms | 9.4 ms |

Each silent test used 24 start/stop cycles. Signal markers were mapped to Windows' endpoint clock with 1 ms detection buckets. These figures exclude microphone detection and downstream hardware/acoustic delay. The virtual-output result also excludes FxSound's final physical output.

A separate 20-second listening sample on a 28-logical-processor PC averaged **0.033% total CPU** and **14.4 MiB private memory**. These observations are not guarantees for other PCs or current source revisions.

Full voice-to-ear latency and gaming/FPS impact remain unmeasured. A muted loopback attempt returned only zeros and was rejected. Raw reports are retained privately.

The app analyzes 5 ms windows and keeps playback ready between alarms. Current checks verify silent startup, prompt gating, feedback rejection and zero managed allocations in synthetic analysis, filter resets, gating and tone generation. See [development commands](CONTRIBUTING.md) to reproduce checks and diagnostics.

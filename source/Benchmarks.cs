using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LittleQuieter
{
    internal static class Benchmarks
    {
        public static int PrintEndpoints()
        {
            using var enumerator = new MMDeviceEnumerator();
            using (var defaultOutput = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console))
                Console.WriteLine("Windows default output remains: " + defaultOutput.FriendlyName);
            var fxPids = Process.GetProcessesByName("FxSound").Select(p => (uint)p.Id).ToHashSet();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (device)
                {
                    bool fxActive = false; var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++) if (fxPids.Contains(sessions[i].GetProcessID) && sessions[i].State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) fxActive = true;
                    Console.WriteLine(device.FriendlyName + " | " + device.ID + " | FxSound active=" + fxActive);
                }
            return 0;
        }
        public static int CaptureTiming(string reportPath)
        {
            using var endpoints = new MMDeviceEnumerator();
            using var endpoint = endpoints.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            using var recorder = new WasapiRecorderBuilder().WithDevice(endpoint).WithSharedMode().WithEventSync()
                .WithBufferLength(10).WithLowLatency().WithMmcssThreadPriority("Audio").Build();
            var analyzer = new MicrophoneAnalyzer(recorder.WaveFormat);
            var oldest = new List<double>(); var newest = new List<double>(); var processing = new List<double>();
            int timestampsRejected = 0, discontinuities = 0;
            recorder.DataAvailable += (buffer, flags, _, qpc) =>
            {
                double arrival = AudioInput.Now;
                if ((flags & AudioClientBufferFlags.TimestampError) != 0 || qpc <= 0) timestampsRejected++;
                else
                {
                    double first = qpc / 1e7, duration = buffer.Length / (double)recorder.WaveFormat.BlockAlign / recorder.WaveFormat.SampleRate;
                    oldest.Add((arrival - first) * 1000); newest.Add((arrival - first - duration) * 1000);
                }
                if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0) { discontinuities++; analyzer.Reset(); }
                double begin = AudioInput.Now; analyzer.Process(buffer, arrival); processing.Add((AudioInput.Now - begin) * 1000);
            };
            recorder.StartRecording(); Thread.Sleep(2500); recorder.StopRecording(); recorder.Dispose();
            var report = new
            {
                Method = "Raw capture packet QPC timestamps compared with callback arrival; no audio saved",
                CaptureDeviceName = endpoint.FriendlyName, TimestampsRejected = timestampsRejected, Discontinuities = discontinuities,
                OldestSampleAgeMinMedianP95MaxMs = Summary(oldest.Skip(20)),
                NewestSampleAgeMinMedianP95MaxMs = Summary(newest.Skip(20)),
                AnalyzerCallbackCostMinMedianP95MaxMs = Summary(processing.Skip(20)),
                AnalysisWindowMs = analyzer.WindowMilliseconds,
                Limitation = "Driver-reported capture positions, not an independently measured acoustic input onset."
            };
            if (reportPath != null) File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Capture newest-sample age min/median/p95/max ms: " + String.Join(", ", report.NewestSampleAgeMinMedianP95MaxMs.Select(x => x.ToString("F3"))));
            Console.WriteLine("Analyzer callback cost min/median/p95/max ms: " + String.Join(", ", report.AnalyzerCallbackCostMinMedianP95MaxMs.Select(x => x.ToString("F4"))));
            return 0;
        }
        private sealed record SignalMarker(bool Active, long Frame, double GeneratedAt);
        private sealed record ClockPoint(double Frame, double QpcSeconds);
        private sealed class TraceProvider : IWaveProvider
        {
            private readonly IWaveProvider source;
            public WaveFormat WaveFormat => source.WaveFormat;
            private long frames;
            private int bucket;
            private double squares;
            private bool active;
            public readonly List<SignalMarker> Markers = new List<SignalMarker>();
            public readonly List<double> ReadTimes = new List<double>();
            public TraceProvider(IWaveProvider provider) { source = provider; }
            public int Read(Span<byte> data)
            {
                int bytes = source.Read(data); double now = AudioInput.Now;
                ReadTimes.Add(now);
                var samples = MemoryMarshal.Cast<byte, float>(data.Slice(0, bytes));
                int bucketFrames = WaveFormat.SampleRate / 1000;
                for (int i = 0; i < samples.Length; i += WaveFormat.Channels)
                {
                    squares += samples[i] * (double)samples[i]; frames++;
                    if (++bucket < bucketFrames) continue;
                    bool value = Math.Sqrt(squares / bucket) > 0.0001;
                    if (value != active) { lock (Markers) Markers.Add(new SignalMarker(value, frames, now)); active = value; }
                    bucket = 0; squares = 0;
                }
                return bytes;
            }
        }

        public static int ClockRun(string reportPath, string deviceId = null)
        {
            TraceProvider trace = null;
            using var tone = new AlarmTone(source => trace = new TraceProvider(source)); tone.Start(30, deviceId);
            var player = (WasapiPlayer)typeof(AlarmTone).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tone);
            var client = (AudioClient)typeof(WasapiPlayer).GetField("audioClient", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
            bool previousMute = player.IsMuted; player.IsMuted = true;
            if (!player.IsMuted) throw new InvalidOperationException("Test session mute not confirmed; no activation occurred.");
            var clockPoints = new List<ClockPoint>(); var commands = new List<Command>();
            using var stop = new ManualResetEvent(false);
            Exception clockFailure = null;
            var reader = new Thread(() =>
            {
                try
                {
                    var clock = client.AudioClockClient; double frequency = clock.Frequency;
                    while (!stop.WaitOne(2))
                    {
                        clock.GetPosition(out ulong position, out ulong qpc);
                        if (position > 0 && qpc > 0) clockPoints.Add(new ClockPoint(position / frequency * tone.Format.SampleRate, qpc / 1e7));
                    }
                }
                catch (Exception error) { clockFailure = error; }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            try
            {
                reader.Start(); Thread.Sleep(300);
                for (int trial = 0; trial < 24; trial++)
                {
                    if (!player.IsMuted) throw new InvalidOperationException("Unexpected unmute; test aborted.");
                    commands.Add(new Command(true, AudioInput.Now)); tone.SetActive(true); Thread.Sleep(103 + trial % 7 * 3);
                    commands.Add(new Command(false, AudioInput.Now)); tone.SetActive(false); Thread.Sleep(107 + trial % 5 * 3);
                }
                Thread.Sleep(300); stop.Set(); reader.Join(); tone.SetActive(false); player.Stop();
                if (clockFailure != null) throw clockFailure;
                var matches = new List<object>(); var starts = new List<double>(); var stops = new List<double>();
                var generatedStarts = new List<double>(); var generatedStops = new List<double>();
                int index = 0;
                foreach (var command in commands)
                {
                    while (index < trace.Markers.Count && (trace.Markers[index].Active != command.Active || trace.Markers[index].GeneratedAt < command.Time)) index++;
                    if (index >= trace.Markers.Count) break;
                    var marker = trace.Markers[index++];
                    var point = clockPoints.MinBy(p => Math.Abs(p.Frame - marker.Frame));
                    double presentation = point.QpcSeconds + (marker.Frame - point.Frame) / tone.Format.SampleRate;
                    double delta = (presentation - command.Time) * 1000;
                    double generated = (marker.GeneratedAt - command.Time) * 1000;
                    if (command.Active) { starts.Add(delta); generatedStarts.Add(generated); }
                    else { stops.Add(delta); generatedStops.Add(generated); }
                    matches.Add(new { command.Active, command.Time, marker.Frame, marker.GeneratedAt, PresentationQpcSeconds = presentation, LatencyMs = delta, GenerationDelayMs = generated, ClockDistanceFrames = Math.Abs(point.Frame - marker.Frame) });
                }
                var report = new
                {
                    Method = "Actual production tone generator and WASAPI pipeline, test-process session muted, generated markers mapped onto IAudioClock endpoint positions",
                    OutputLowLatency = tone.LowLatency, OutputReportedEngineMs = tone.EngineMilliseconds,
                    OutputBufferFrames = client.BufferSize, OutputBufferMs = client.BufferSize * 1000.0 / tone.Format.SampleRate,
                    ReportedStreamLatencyRaw = client.StreamLatency, DetectionBucketMs = 1,
                    OutputDeviceName = player.DeviceFriendlyName,
                    RenderReadGapMinMedianP95MaxMs = Summary(trace.ReadTimes.Skip(1).Select((time, i) => (time - trace.ReadTimes[i]) * 1000)),
                    CommandCount = commands.Count, MarkerCount = trace.Markers.Count, MatchCount = matches.Count,
                    StartMinMedianP95MaxMs = Summary(starts), StopMinMedianP95MaxMs = Summary(stops),
                    StartGenerationMinMedianP95MaxMs = Summary(generatedStarts), StopGenerationMinMedianP95MaxMs = Summary(generatedStops),
                    ClockReadings = clockPoints.Count, Matches = matches,
                    Limitation = "Measures command-to-endpoint-clock presentation, with 1 ms signal buckets. Excludes microphone acoustic onset, downstream virtual-device routing, DAC/speaker/headphone/Bluetooth delay. No acoustic measurement."
                };
                if (reportPath != null) File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Session muted; native endpoint buffer=" + report.OutputBufferMs.ToString("F2") + " ms, engine=" + report.OutputReportedEngineMs + " ms; markers=" + matches.Count + "/" + commands.Count);
                Console.WriteLine("Endpoint-clock start min/median/p95/max ms: " + String.Join(", ", Summary(starts).Select(x => x.ToString("F2"))));
                Console.WriteLine("Endpoint-clock stop min/median/p95/max ms: " + String.Join(", ", Summary(stops).Select(x => x.ToString("F2"))));
                Console.WriteLine("Source-generation start median ms: " + Summary(generatedStarts).ElementAtOrDefault(1).ToString("F2"));
                if (matches.Count != commands.Count || starts.Any(x => x < -1) || stops.Any(x => x < -1)) throw new InvalidOperationException("Clock measurement incomplete or inconsistent; retain the data but do not claim valid timings.");
                return 0;
            }
            finally
            {
                stop.Set(); if (reader.IsAlive) reader.Join();
                tone.SetActive(false); player.Stop(); player.IsMuted = previousMute;
            }
        }

        private sealed record Transition(bool Active, double Time, double Arrival);
        private sealed record Command(bool Active, double Time);
        private static double[] Summary(IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(x => x).ToArray();
            return sorted.Length == 0 ? Array.Empty<double>() : new[] { sorted[0], sorted[sorted.Length / 2], sorted[(int)Math.Ceiling((sorted.Length - 1) * 0.95)], sorted[^1] };
        }
        [SupportedOSPlatform("windows10.0.20348")]
        public static int Run(string reportPath)
        {
            using var tone = new AlarmTone(); tone.Start(30);
            var player = (WasapiPlayer)typeof(AlarmTone).GetField("player", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tone);
            bool previousMute = player.IsMuted;
            // Only this diagnostic process's default audio session is muted. The source
            // is initially gated off, and must stay muted before any nonzero test data.
            player.IsMuted = true;
            if (!player.IsMuted) throw new InvalidOperationException("Test session mute could not be confirmed; no tone was activated.");
            try
            {
                Console.WriteLine("Test process audio session mute confirmed. Global and other-app volumes are unchanged.");
                using var capture = new WasapiRecorderBuilder().WithProcessLoopback((uint)Environment.ProcessId)
                    .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)).WithBufferLength(10).BuildAsync().GetAwaiter().GetResult();
                var transitions = new List<Transition>(); var commands = new List<Command>();
                int packets = 0, timestampErrors = 0; double maxRms = 0;
                const int detectionFrames = 48; // 1 ms detection buckets.
                int accumulated = 0; double squares = 0; bool observed = false;
                object sync = new object();
                capture.DataAvailable += (buffer, flags, position, qpc) =>
                {
                    if ((flags & AudioClientBufferFlags.TimestampError) != 0 || qpc <= 0) { Interlocked.Increment(ref timestampErrors); return; }
                    double arrival = AudioInput.Now, start = qpc / 1e7;
                    var samples = MemoryMarshal.Cast<byte, float>(buffer);
                    lock (sync)
                    {
                        packets++;
                        for (int frame = 0; frame < samples.Length / 2; frame++)
                        {
                            double value = samples[frame * 2]; squares += value * value;
                            if (++accumulated < detectionFrames) continue;
                            double rms = Math.Sqrt(squares / accumulated); maxRms = Math.Max(maxRms, rms);
                            bool active = rms > 0.0001;
                            if (active != observed)
                            {
                                transitions.Add(new Transition(active, start + (frame + 1) / 48000.0, arrival));
                                observed = active;
                            }
                            accumulated = 0; squares = 0;
                        }
                    }
                };
                capture.StartRecording(); Thread.Sleep(300);
                for (int trial = 0; trial < 16; trial++)
                {
                    if (!player.IsMuted) throw new InvalidOperationException("Test session unexpectedly unmuted; stopping without further activation.");
                    commands.Add(new Command(true, AudioInput.Now)); tone.SetActive(true);
                    Thread.Sleep(103 + trial % 7 * 3);
                    commands.Add(new Command(false, AudioInput.Now)); tone.SetActive(false);
                    Thread.Sleep(107 + trial % 5 * 3);
                }
                Thread.Sleep(300); capture.StopRecording();
                // Dispose/Join before reading the final callback lists.
                capture.Dispose();
                var starts = new List<double>(); var stops = new List<double>();
                var deliveredStarts = new List<double>(); var deliveredStops = new List<double>();
                var matched = new List<object>(); int next = 0;
                foreach (var command in commands)
                {
                    while (next < transitions.Count && (transitions[next].Active != command.Active || transitions[next].Time < command.Time - 0.01)) next++;
                    if (next >= transitions.Count) break;
                    var detected = transitions[next++];
                    double delta = (detected.Time - command.Time) * 1000;
                    double delivered = (detected.Arrival - command.Time) * 1000;
                    if (command.Active) { starts.Add(delta); deliveredStarts.Add(delivered); }
                    else { stops.Add(delta); deliveredStops.Add(delivered); }
                    matched.Add(new { command.Active, CommandQpcSeconds = command.Time, DetectedQpcSeconds = detected.Time, CallbackQpcSeconds = detected.Arrival, SignalLatencyMs = delta, ObservedCallbackLatencyMs = delivered });
                }
                var report = new
                {
                    Method = "Muted diagnostic-process WASAPI loopback; no acoustic measurement",
                    SessionMuteConfirmed = player.IsMuted, OutputLowLatency = tone.LowLatency, OutputReportedEngineMs = tone.EngineMilliseconds,
                    DetectionBucketMs = 1, Packets = packets, TimestampErrors = timestampErrors, MaximumCapturedRms = maxRms,
                    Commands = commands.Count, DetectedTransitions = transitions.Count, MatchedTransitions = matched.Count,
                    StartMinMedianP95MaxMs = Summary(starts), StopMinMedianP95MaxMs = Summary(stops),
                    StartCallbackMinMedianP95MaxMs = Summary(deliveredStarts), StopCallbackMinMedianP95MaxMs = Summary(deliveredStops),
                    Matches = matched,
                    Limitation = "Loopback timestamps are a Windows software observation point, not speaker/headphone sound or microphone acoustic onset. Muting may suppress loopback on some systems."
                };
                string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                if (reportPath != null) File.WriteAllText(reportPath, json);
                Console.WriteLine("Loopback packets=" + packets + "; max RMS=" + maxRms.ToString("G4") + "; transitions=" + transitions.Count + "; matched=" + matched.Count + "/" + commands.Count);
                Console.WriteLine("Start min/median/p95/max ms: " + String.Join(", ", Summary(starts).Select(x => x.ToString("F2"))));
                Console.WriteLine("Stop min/median/p95/max ms: " + String.Join(", ", Summary(stops).Select(x => x.ToString("F2"))));
                if (reportPath != null) Console.WriteLine("Saved timing metadata only: " + reportPath);
                if (matched.Count != commands.Count || starts.Any(x => x < -1) || stops.Any(x => x < -1))
                    Console.WriteLine("LIMIT: loopback did not produce a complete, nonnegative timing set; do not report these as valid output latencies.");
                return 0;
            }
            finally
            {
                tone.SetActive(false); player.Stop(); // Flush every test sample before restoring its prior mute state.
                player.IsMuted = previousMute;
            }
        }
    }
}

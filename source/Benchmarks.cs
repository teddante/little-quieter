using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
            foreach (var flow in new[] { DataFlow.Capture, DataFlow.Render })
            {
                using var selected = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
                Console.WriteLine(flow + " default: " + selected.FriendlyName);
                foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                    using (device)
                        Console.WriteLine(device.FriendlyName + " | " + device.ID);
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
            var oldest = new List<double>();
            var newest = new List<double>();
            var processing = new List<double>();
            int timestampsRejected = 0, discontinuities = 0;
            recorder.DataAvailable += (buffer, flags, _, qpc) =>
            {
                double arrival = AudioClock.Now;
                if ((flags & AudioClientBufferFlags.TimestampError) != 0 || qpc <= 0)
                    timestampsRejected++;
                else
                {
                    double first = qpc / 1e7, duration = buffer.Length / (double)recorder.WaveFormat.BlockAlign / recorder.WaveFormat.SampleRate;
                    oldest.Add((arrival - first) * 1000);
                    newest.Add((arrival - first - duration) * 1000);
                }
                if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0)
                {
                    discontinuities++;
                    analyzer.Reset();
                }
                double begin = AudioClock.Now;
                analyzer.Process(buffer, arrival);
                processing.Add((AudioClock.Now - begin) * 1000);
            };
            recorder.StartRecording();
            Thread.Sleep(2500);
            recorder.StopRecording();
            recorder.Dispose();
            var report = new
            {
                Method = "Raw capture packet QPC timestamps compared with callback arrival; no audio saved",
                CaptureDeviceName = endpoint.FriendlyName,
                TimestampsRejected = timestampsRejected,
                Discontinuities = discontinuities,
                OldestSampleAgeMinMedianP95MaxMs = Summary(oldest.Skip(20)),
                NewestSampleAgeMinMedianP95MaxMs = Summary(newest.Skip(20)),
                AnalyzerCallbackCostMinMedianP95MaxMs = Summary(processing.Skip(20)),
                AnalysisWindowMs = analyzer.WindowMilliseconds,
                Limitation = "Driver-reported capture positions, not an independently measured acoustic input onset."
            };
            WriteReport(reportPath, report);
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
            public TraceProvider(IWaveProvider provider)
            {
                source = provider;
            }
            public int Read(Span<byte> data)
            {
                int bytes = source.Read(data);
                double now = AudioClock.Now;
                ReadTimes.Add(now);
                var samples = MemoryMarshal.Cast<byte, float>(data.Slice(0, bytes));
                int bucketFrames = WaveFormat.SampleRate / 1000;
                for (int i = 0; i < samples.Length; i += WaveFormat.Channels)
                {
                    squares += samples[i] * (double)samples[i];
                    frames++;
                    if (++bucket < bucketFrames)
                        continue;
                    bool value = Math.Sqrt(squares / bucket) > 0.0001;
                    if (value != active)
                    {
                        lock (Markers)
                            Markers.Add(new SignalMarker(value, frames, now));
                        active = value;
                    }
                    bucket = 0;
                    squares = 0;
                }
                return bytes;
            }
        }

        public static int ClockRun(string reportPath, string deviceId = null)
        {
            TraceProvider trace = null;
            using var tone = new AlarmTone(source => trace = new TraceProvider(source));
            tone.Start(30, deviceId);
            var player = tone.Player;
            // NAudio exposes no public endpoint-clock client. Keep this version-specific
            // diagnostic access out of the application's rendering and capture paths.
            var client = typeof(WasapiPlayer).GetField("audioClient", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(player) as AudioClient
                ?? throw new NotSupportedException("The installed NAudio player does not expose the expected diagnostic clock field.");
            bool previousMute = player.IsMuted;
            player.IsMuted = true;
            if (!player.IsMuted)
                throw new InvalidOperationException("Test session mute not confirmed; no activation occurred.");
            var clockPoints = new List<ClockPoint>();
            var commands = new List<Command>();
            using var stop = new ManualResetEvent(false);
            Exception clockFailure = null;
            var reader = new Thread(() =>
            {
                try
                {
                    var clock = client.AudioClockClient;
                    double frequency = clock.Frequency;
                    while (!stop.WaitOne(2))
                    {
                        clock.GetPosition(out ulong position, out ulong qpc);
                        if (position > 0 && qpc > 0)
                            clockPoints.Add(new ClockPoint(position / frequency * tone.Format.SampleRate, qpc / 1e7));
                    }
                }
                catch (Exception error) { clockFailure = error; }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            try
            {
                reader.Start();
                Thread.Sleep(300);
                for (int trial = 0; trial < 24; trial++)
                {
                    if (!player.IsMuted)
                        throw new InvalidOperationException("Unexpected unmute; test aborted.");
                    commands.Add(new Command(true, AudioClock.Now));
                    tone.SetActive(true);
                    Thread.Sleep(103 + trial % 7 * 3);
                    commands.Add(new Command(false, AudioClock.Now));
                    tone.SetActive(false);
                    Thread.Sleep(107 + trial % 5 * 3);
                }
                Thread.Sleep(300);
                stop.Set();
                reader.Join();
                tone.SetActive(false);
                player.Stop();
                if (clockFailure != null)
                    throw clockFailure;
                if (clockPoints.Count == 0)
                    throw new InvalidOperationException("The output clock returned no positions; no timing result is valid.");
                var matches = new List<object>();
                var starts = new List<double>();
                var stops = new List<double>();
                var generatedStarts = new List<double>();
                var generatedStops = new List<double>();
                int index = 0;
                foreach (var command in commands)
                {
                    while (index < trace.Markers.Count && (trace.Markers[index].Active != command.Active || trace.Markers[index].GeneratedAt < command.Time))
                        index++;
                    if (index >= trace.Markers.Count)
                        break;
                    var marker = trace.Markers[index++];
                    var point = clockPoints.MinBy(p => Math.Abs(p.Frame - marker.Frame));
                    double presentation = point.QpcSeconds + (marker.Frame - point.Frame) / tone.Format.SampleRate;
                    double delta = (presentation - command.Time) * 1000;
                    double generated = (marker.GeneratedAt - command.Time) * 1000;
                    if (command.Active)
                    {
                        starts.Add(delta);
                        generatedStarts.Add(generated);
                    }
                    else
                    {
                        stops.Add(delta);
                        generatedStops.Add(generated);
                    }
                    matches.Add(new
                    {
                        command.Active,
                        command.Time,
                        marker.Frame,
                        marker.GeneratedAt,
                        PresentationQpcSeconds = presentation,
                        LatencyMs = delta,
                        GenerationDelayMs = generated,
                        ClockDistanceFrames = Math.Abs(point.Frame - marker.Frame)
                    });
                }
                var report = new
                {
                    Method = "Actual production tone generator and WASAPI pipeline, test-process session muted, generated markers mapped onto IAudioClock endpoint positions",
                    OutputLowLatency = tone.LowLatency,
                    OutputReportedEngineMs = tone.EngineMilliseconds,
                    OutputBufferFrames = client.BufferSize,
                    OutputBufferMs = client.BufferSize * 1000.0 / tone.Format.SampleRate,
                    ReportedStreamLatencyRaw = client.StreamLatency,
                    DetectionBucketMs = 1,
                    OutputDeviceName = player.DeviceFriendlyName,
                    RenderReadGapMinMedianP95MaxMs = Summary(trace.ReadTimes.Skip(1).Select((time, i) => (time - trace.ReadTimes[i]) * 1000)),
                    CommandCount = commands.Count,
                    MarkerCount = trace.Markers.Count,
                    MatchCount = matches.Count,
                    StartMinMedianP95MaxMs = Summary(starts),
                    StopMinMedianP95MaxMs = Summary(stops),
                    StartGenerationMinMedianP95MaxMs = Summary(generatedStarts),
                    StopGenerationMinMedianP95MaxMs = Summary(generatedStops),
                    ClockReadings = clockPoints.Count,
                    Matches = matches,
                    Limitation = "Measures command-to-endpoint-clock presentation, with 1 ms signal buckets. Excludes microphone acoustic onset, downstream virtual-device routing, DAC/speaker/headphone/Bluetooth delay. No acoustic measurement."
                };
                WriteReport(reportPath, report);
                Console.WriteLine("Session muted; native endpoint buffer=" + report.OutputBufferMs.ToString("F2") + " ms, engine=" + report.OutputReportedEngineMs + " ms; markers=" + matches.Count + "/" + commands.Count);
                Console.WriteLine("Endpoint-clock start min/median/p95/max ms: " + String.Join(", ", Summary(starts).Select(x => x.ToString("F2"))));
                Console.WriteLine("Endpoint-clock stop min/median/p95/max ms: " + String.Join(", ", Summary(stops).Select(x => x.ToString("F2"))));
                Console.WriteLine("Source-generation start median ms: " + Summary(generatedStarts).ElementAtOrDefault(1).ToString("F2"));
                if (matches.Count != commands.Count || starts.Any(x => x < -1) || stops.Any(x => x < -1))
                    throw new InvalidOperationException("Clock measurement incomplete or inconsistent; retain the data but do not claim valid timings.");
                return 0;
            }
            finally
            {
                stop.Set();
                if (reader.IsAlive)
                    reader.Join();
                tone.SetActive(false);
                player.Stop();
                player.IsMuted = previousMute;
            }
        }

        private sealed record Command(bool Active, double Time);
        private static void WriteReport(string path, object report)
        {
            if (path == null)
                return;
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        private static double[] Summary(IEnumerable<double> values)
        {
            double[] sorted = values.OrderBy(x => x).ToArray();
            return sorted.Length == 0 ? Array.Empty<double>() : new[] { sorted[0], sorted[sorted.Length / 2], sorted[(int)Math.Ceiling((sorted.Length - 1) * 0.95)], sorted[^1] };
        }
    }
}

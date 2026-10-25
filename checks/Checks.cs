using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace LittleQuieter;

internal static class Checks
{
    private static int count;
    private static void Assert(bool valid, string label)
    {
        if (!valid)
            throw new Exception("FAIL: " + label);
        count++;
        Console.WriteLine("PASS: " + label);
    }
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--audio")
                return AudioCheck();
            if (args.Length > 0 && args[0] == "--running")
                return RunningCheck();
            if (args.Length > 0 && args[0] == "--clock-benchmark")
                return Benchmarks.ClockRun(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
            if (args.Length > 0 && args[0] == "--endpoints")
                return Benchmarks.PrintEndpoints();
            if (args.Length > 0 && args[0] == "--capture-timing")
                return Benchmarks.CaptureTiming(args.Length > 1 ? args[1] : null);
            if (args.Length > 0)
                throw new ArgumentException("Unknown check option. Use --audio, --running, --endpoints, --clock-benchmark [report] [device], or --capture-timing [report].");
            GateChecks();
            ToneChecks();
            AnalyzerChecks();
            SettingsChecks();
            Console.WriteLine("All " + count + " checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void GateChecks()
    {
        bool output = false;
        var gate = new AlarmGate(active => output = active);
        double now = AudioClock.Now;
        gate.Configure(-28, true);
        gate.Update(new SoundFrame(-20, now, false));
        Assert(output && gate.Active, "first loud callback opens the tone gate without a UI tick");
        gate.Update(new SoundFrame(-35, now + 0.005, false));
        Assert(!output && !gate.Active, "first quiet callback closes the tone gate");
        gate.Update(new SoundFrame(-20, now + 0.01, false));
        Assert(output, "next loud callback restarts without a delay or cooldown");
        gate.Configure(-28, false);
        Assert(!output && gate.Active, "muting stops sound while retaining the visual level state");
        gate.Configure(-28, true);
        gate.Suspend(true);
        gate.Update(new SoundFrame(-10, now + 0.015, false));
        Assert(!output && !gate.Active, "calibration suspension prevents the alarm");
        gate.Suspend(false);
        gate.Update(new SoundFrame(-20, now + 0.02, false));
        gate.Refresh(now + 0.13);
        Assert(!output, "watchdog silences a missing microphone stream independently of UI");
        gate.Preview(now + 1);
        Assert(output, "explicit tone preview can run without microphone data");
        Assert(gate.IsPreviewing(now + 2.99), "preview lifetime is shared with the UI");
        gate.Refresh(now + 3.01);
        Assert(!output, "preview expires independently of the UI timer");
        Assert(!gate.IsPreviewing(now + 3.01), "preview state expires with the sound gate");
        gate.Update(new SoundFrame(double.NaN, now + 4, false));
        Assert(!output, "invalid levels fail silent");
        gate.Update(new SoundFrame(-20, now + 4.01, false));
        gate.Reset();
        Assert(!output, "reset immediately closes the gate");
        gate.Preview(now + 5);
        gate.Suspend(true);
        Assert(!output && !gate.IsPreviewing(now + 5), "calibration or stop cancels both preview sound and lifetime");
        gate.Suspend(false);
        gate.Update(new SoundFrame(-20, double.NaN, false));
        Assert(!output && !gate.Active, "an invalid input timestamp cannot latch the tone on");
        int writes = 0;
        var steady = new AlarmGate(_ => writes++);
        steady.Configure(-28, true);
        for (int i = 0; i < 1000; i++)
            steady.Update(new SoundFrame(-50, now, false));
        Assert(writes == 0, "steady quiet input avoids redundant output-gate writes");
    }
    private static float[] ReadTone(SteadyToneProvider source, int frames)
    {
        byte[] data = new byte[frames * source.WaveFormat.BlockAlign];
        if (source.Read(data) != data.Length)
            throw new Exception("Tone source unexpectedly ended.");
        return MemoryMarshal.Cast<byte, float>(data).ToArray();
    }
    private static double Rms(ReadOnlySpan<float> values)
    {
        double sum = 0;
        foreach (float value in values)
            sum += value * (double)value;
        return Math.Sqrt(sum / values.Length);
    }
    private static void ToneChecks()
    {
        var source = new SteadyToneProvider(80);
        Assert(Rms(ReadTone(source, 4800)) == 0, "ready output emits silence before activation");
        source.SetActive(true);
        float[] onset = ReadTone(source, 48);
        Assert(Rms(onset.AsSpan(24)) > 0.4, "tone reaches full level within half a millisecond of generated audio");
        source.SetActive(false);
        float[] release = ReadTone(source, 48);
        Assert(Rms(release.AsSpan(24)) == 0, "generated tone becomes exactly silent within half a millisecond");
        source.SetActive(true);
        bool steady = true;
        for (int block = 0; block < 32; block++)
        {
            float[] samples = ReadTone(source, 12000);
            int crossings = 0;
            for (int i = 1; i < samples.Length; i++)
                if (samples[i - 1] <= 0 && samples[i] > 0)
                    crossings++;
            steady &= Math.Abs(crossings / 0.25 - AlarmTone.FrequencyHz) <= 4 && Rms(samples) > 0.47;
        }
        Assert(steady, "continuous fixed-pitch tone has no gaps or finite ending");
        source.SetVolume(20);
        float[] softer = ReadTone(source, 4800);
        Assert(Rms(softer.AsSpan(48)) < 0.13, "volume updates on the existing stream");
        var stereo = new SteadyToneProvider(100, 44100, 2);
        stereo.SetActive(true);
        float[] frames = ReadTone(stereo, 4410);
        bool equal = true, unclipped = true;
        for (int i = 0; i < frames.Length; i += 2)
        {
            equal &= frames[i] == frames[i + 1];
            unclipped &= Math.Abs(frames[i]) <= 0.85;
        }
        Assert(equal && unclipped, "tone supports native sample rates and channel counts without clipping");
        var zero = new SteadyToneProvider(0);
        zero.SetActive(true);
        Assert(Rms(ReadTone(zero, 4800)) == 0, "zero-volume hardware tests cannot produce an audible signal");
        source.SetVolume(0);
        Assert(Rms(ReadTone(source, 4800).AsSpan(48)) == 0, "changing an active tone to zero volume still completes its release ramp");
    }
    private static byte[] Signal(WaveFormat format, int frames, Func<int, int, double> value)
    {
        byte[] data = new byte[frames * format.BlockAlign];
        for (int frame = 0; frame < frames; frame++)
            for (int channel = 0; channel < format.Channels; channel++)
            {
                int offset = frame * format.BlockAlign + channel * format.BitsPerSample / 8;
                double sample = value(frame, channel);
                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                    BitConverter.TryWriteBytes(data.AsSpan(offset, 4), (float)sample);
                else if (format.BitsPerSample == 16)
                    BitConverter.TryWriteBytes(data.AsSpan(offset, 2), (short)(sample * 32767));
                else
                {
                    int pcm = (int)(sample * 2147483647);
                    if (format.BitsPerSample == 32)
                        BitConverter.TryWriteBytes(data.AsSpan(offset, 4), pcm);
                    else
                    {
                        data[offset] = (byte)(pcm >> 8);
                        data[offset + 1] = (byte)(pcm >> 16);
                        data[offset + 2] = (byte)(pcm >> 24);
                    }
                }
            }
        return data;
    }
    private static void AnalyzerChecks()
    {
        foreach (var format in new[] { WaveFormat.CreateIeeeFloatWaveFormat(48000, 1), WaveFormat.CreateIeeeFloatWaveFormat(44100, 2), new WaveFormat(44100, 16, 1), new WaveFormat(48000, 24, 1), new WaveFormat(48000, 32, 2) })
        {
            var analyzer = new MicrophoneAnalyzer(format);
            var readings = new List<double>();
            analyzer.LevelAvailable += frame => readings.Add(frame.Db);
            byte[] signal = Signal(format, format.SampleRate, (i, c) => 0.2 * Math.Sin(2 * Math.PI * 1000 * i / format.SampleRate) * (c == 0 ? 1 : -1));
            analyzer.Process(signal, AudioClock.Now);
            double average = readings.Skip(readings.Count / 2).Average();
            Assert(analyzer.WindowMilliseconds <= 5 && readings.Count >= 200 && Math.Abs(average + 16.99) < 0.4,
                "5 ms analysis decodes " + format.SampleRate + " Hz / " + format.BitsPerSample + " bit / " + format.Channels + " channel " + format.Encoding);
            analyzer.Reset();
            readings.Clear();
            analyzer.Process(new byte[format.SampleRate / 10 * format.BlockAlign], AudioClock.Now);
            Assert(readings.All(db => db == -100), "reset removes filter history and silence stays quiet");
        }
        var voiceFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        var direct = new MicrophoneAnalyzer(voiceFormat);
        bool output = false;
        var gate = new AlarmGate(active => output = active);
        gate.Configure(-40, true);
        direct.LevelAvailable += gate.Update;
        direct.Process(Signal(voiceFormat, 240, (i, _) => 0.12 * Math.Sin(2 * Math.PI * 400 * i / 48000.0)), AudioClock.Now);
        Assert(output, "microphone analyzer drives output after the first 5 ms without UI processing");
        direct.Process(new byte[240 * 4], AudioClock.Now);
        Assert(!output, "quiet microphone data directly closes the output gate");
        var echo = new MicrophoneAnalyzer(voiceFormat);
        bool everActiveAfterQuiet = false;
        double released = -1;
        var echoGate = new AlarmGate(_ => { });
        echoGate.Configure(-40, true);
        int current = 0;
        echo.LevelAvailable += frame => { echoGate.Update(frame); if (current >= 200 && released < 0 && !echoGate.Active) released = (current - 199) * 5; if (current > 220) everActiveAfterQuiet |= echoGate.Active; };
        for (current = 0; current < 400; current++)
        {
            int block = current;
            echo.Process(Signal(voiceFormat, 240, (i, _) =>
            {
                double time = (block * 240 + i) / 48000.0;
                return (block < 200 ? 0.12 * Math.Sin(2 * Math.PI * 400 * time) : 0) +
                    (block >= 100 ? 0.8 * Math.Sin(2 * Math.PI * 2500 * time) : 0);
            }), AudioClock.Now);
        }
        Assert(released > 0 && released <= 20 && !everActiveAfterQuiet, "voice release defeats continuing simulated alarm echo within 20 ms");
        Console.WriteLine("Synthetic voice/echo release: " + released + " ms of analysis data.");
        var readingsForCalibration = new List<double>();
        for (int i = 0; i < 80; i++)
            readingsForCalibration.Add(-40 + i % 20);
        Assert(Levels.TryCalibrate(readingsForCalibration, out int limit) && limit >= 70 && limit <= 85, "voice calibration still sets a useful margin");
        Assert(!Levels.TryCalibrate(Enumerable.Repeat(double.NaN, 80).ToList(), out _), "invalid calibration input cannot produce a loudness limit");
        Assert(!Levels.TryCalibrate(Enumerable.Repeat(-100.0, 80).ToList(), out _) && !Levels.TryCalibrate(Enumerable.Repeat(-30.0, 80).ToList(), out _), "silence and a constant background level are rejected during calibration");
        Assert(Math.Abs(Levels.ToMeter(Levels.ToDb(50)) - 50) < 0.00001, "meter conversion remains consistent");
        byte[] steadyInput = Signal(voiceFormat, 240, (i, _) => 0.12 * Math.Sin(2 * Math.PI * 400 * i / 48000.0));
        var allocationAnalyzer = new MicrophoneAnalyzer(voiceFormat);
        var allocationGate = new AlarmGate(_ => { });
        allocationAnalyzer.LevelAvailable += allocationGate.Update;
        var allocationTone = new SteadyToneProvider(80);
        allocationTone.SetActive(true);
        byte[] outputBuffer = new byte[240 * allocationTone.WaveFormat.BlockAlign];
        for (int i = 0; i < 100; i++)
        {
            allocationAnalyzer.Process(steadyInput, AudioClock.Now);
            allocationTone.Read(outputBuffer);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            allocationAnalyzer.Process(steadyInput, AudioClock.Now);
            allocationTone.Read(outputBuffer);
        }
        Assert(GC.GetAllocatedBytesForCurrentThread() == allocated, "steady analysis, gating and tone generation allocate no managed memory");
    }
    private static void SettingsChecks()
    {
        Assert(StartupRegistration.Command(@"C:\Program Files\Little Quieter.exe") == "\"C:\\Program Files\\Little Quieter.exe\" --background", "startup command quotes paths");
        string folder = Path.Combine(Path.GetTempPath(), "LittleQuieter-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "settings.xml");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(path, "<Preferences><Duration>0.5</Duration><Cooldown>20</Cooldown><Message>Old speech text</Message><Speak>true</Speak><Limit>50</Limit><ToneVolume>80</ToneVolume></Preferences>");
            var loaded = Preferences.Load(path);
            Assert(loaded.Limit == 50 && loaded.ToneVolume == 80, "old speech and delay fields are ignored while level and volume are preserved");
            loaded.OutputDeviceId = "explicit-test-device";
            loaded.OutputDeviceName = "Test output";
            loaded.Save(path);
            string saved = File.ReadAllText(path);
            Assert(!saved.Contains("Message") && !saved.Contains("Duration") && !saved.Contains("Speak") && !saved.Contains("Cooldown"), "saving removes obsolete speech, message, delay and cooldown data");
            loaded.Limit = 999;
            loaded.Save(path);
            Assert(Preferences.Load(path).Limit == 95, "settings validate and replace atomically");
            Assert(Preferences.Load(path).OutputDeviceId == "explicit-test-device", "app-only output selection persists");
            File.WriteAllText(path, "broken XML");
            Assert(Preferences.Load(path).Limit == 65, "damaged preferences fall back safely");
            File.WriteAllText(path, "<Preferences xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\" />");
            Assert(Preferences.Load(path).Limit == 65, "an empty XML settings object falls back safely");
            File.WriteAllText(path, "<Preferences xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><DeviceName xsi:nil=\"true\"/><OutputDeviceName xsi:nil=\"true\"/></Preferences>");
            var emptyNames = Preferences.Load(path);
            Assert(emptyNames.DeviceName == "Windows default microphone" && emptyNames.OutputDeviceName == "Windows default output", "null device names are normalized to their defaults");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
        string relativePath = "LittleQuieter-check-" + Guid.NewGuid().ToString("N") + ".xml";
        try
        {
            new Preferences { Limit = 50 }.Save(relativePath);
            Assert(Preferences.Load(relativePath).Limit == 50, "settings can be saved to a filename in the current directory");
        }
        finally { File.Delete(relativePath); }
    }
    private static int AudioCheck()
    {
        using var alarm = new AlarmTone();
        alarm.Start(0);
        Assert(alarm.IsReady && !alarm.IsActive, "WASAPI output stays ready and initially gated silent");
        Console.WriteLine("Output: low latency=" + alarm.LowLatency + "; reported engine period=" + alarm.EngineMilliseconds + " ms; " + alarm.Format);
        using var input = new AudioInput();
        int callbacks = 0;
        var gaps = new List<double>();
        double last = 0;
        var gate = new AlarmGate(alarm.SetActive);
        gate.Configure(-120, true); // Volume stays zero even when activated.
        input.LevelAvailable += frame => { gate.Update(frame); callbacks++; if (last > 0 && frame.Time > last) gaps.Add((frame.Time - last) * 1000); last = frame.Time; };
        input.Start(null);
        Console.WriteLine("Input: low latency=" + input.LowLatency + "; reported engine period=" + input.EngineMilliseconds + " ms; analysis=" + input.AnalysisMilliseconds.ToString("F2") + " ms; " + input.Format);
        Thread.Sleep(1500);
        Assert(input.Fault == null && alarm.Fault == null && callbacks >= 200, "live native-format capture directly controls silent warm playback");
        int uiFrames = 0;
        while (input.TryRead(out _))
            uiFrames++;
        Assert(uiFrames > 0 && uiFrames <= 20, "UI summaries stay bounded while the fast audio callbacks continue");
        Assert(alarm.IsReady && alarm.IsActive, "gate changes keep the same prepared audio stream running");
        var elapsed = Stopwatch.StartNew();
        gate.Suspend(true);
        elapsed.Stop();
        Assert(alarm.IsReady && !alarm.IsActive, "silencing closes only the gate and retains the prepared output");
        Console.WriteLine("In-process gate-close call: " + (elapsed.Elapsed.TotalMilliseconds * 1000).ToString("F1") + " us (excludes device buffering).");
        if (gaps.Count > 0)
        {
            gaps.Sort();
            Console.WriteLine("Observed capture-callback gap p95: " + gaps[(int)((gaps.Count - 1) * 0.95)].ToString("F2") + " ms.");
        }
        alarm.Player.Stop();
        Assert(SpinWait.SpinUntil(() => alarm.Fault != null, 1000) && !alarm.IsReady, "an unexpected output stop is reported as a fault");
        Console.WriteLine("Hardware checks stayed at zero volume. No recording or audible test.");
        return 0;
    }
    private static int RunningCheck()
    {
        string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LittleQuieter", "Little Quieter.exe");
        using var app = Process.GetProcessesByName("Little Quieter").Single(process => String.Equals(process.MainModule?.FileName, installed, StringComparison.OrdinalIgnoreCase));
        Assert(app.Responding, "installed app is responsive");
        Console.WriteLine("Installed app window: " + (app.MainWindowHandle == IntPtr.Zero ? "hidden" : "open"));
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var saved = Preferences.Load(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleQuieter", "settings.xml"));
        string? microphoneId = AudioDevice.List(NAudio.CoreAudioApi.DataFlow.Capture).Find(device => device.Name == saved.DeviceName)?.Id;
        foreach (var flow in new[] { NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DataFlow.Render })
        {
            string? deviceId = flow == NAudio.CoreAudioApi.DataFlow.Capture ? microphoneId : saved.OutputDeviceId;
            using var endpoint = deviceId == null ? enumerator.GetDefaultAudioEndpoint(flow, NAudio.CoreAudioApi.Role.Console) : enumerator.GetDevice(deviceId);
            var sessions = endpoint.AudioSessionManager.Sessions;
            bool active = false;
            for (int i = 0; i < sessions.Count; i++)
                if (sessions[i].GetProcessID == app.Id && sessions[i].State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                    active = true;
            Assert(active, "Windows confirms the app's active " + flow + " session");
        }
        Assert(StartupRegistration.Enabled, "startup remains registered");
        return 0;
    }
}

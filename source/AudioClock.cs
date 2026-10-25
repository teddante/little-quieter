using System.Diagnostics;

namespace LittleQuieter
{
    // Monotonic seconds shared by audio callbacks, watchdogs and UI scheduling.
    public static class AudioClock
    {
        public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }
}

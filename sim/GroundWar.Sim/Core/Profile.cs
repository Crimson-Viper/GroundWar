using System.Diagnostics;

namespace GroundWar.Sim.Core;

/// <summary>Optional timing of each part of a tick, used by the headless runner's --profile flag.</summary>
public static class Profile
{
    public static bool Enabled { get; set; }
    public static readonly Dictionary<string, double> Milliseconds = new();

    public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public static long Lap(string name, long start)
    {
        if (!Enabled) return 0;
        long now = Stopwatch.GetTimestamp();
        Milliseconds[name] = Milliseconds.GetValueOrDefault(name) + (now - start) * 1000.0 / Stopwatch.Frequency;
        return now;
    }
}

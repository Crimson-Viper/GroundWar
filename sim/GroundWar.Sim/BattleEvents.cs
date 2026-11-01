using System.Numerics;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>A bullet fired this tick. Godot draws these as tracers and dust.</summary>
/// <summary>A bullet or shell fired this tick. Shell = a tank gun round (Godot draws a bigger tracer and a burst).</summary>
public readonly record struct ShotEvent(int ShooterId, Vector2 From, Vector2 Impact, bool Hit, bool MachineGun, bool Shell = false);

public readonly record struct CasualtyEvent(int SoldierId, int ShooterId, bool Killed);

/// <summary>What a log line is about. Thoughts are every soldier's change of action, so there are a lot of them;
/// events are what a commander would hear about (casualties, men breaking, squads changing tactic). Key moments are
/// the few lines that tell the story of the battle (a plan made, a squad breaking, an objective taken); the
/// after-action report's timeline is made of them.</summary>
public enum LogKind { Event, Thought, Casualty, Key }

/// <summary>One line of the battle log. Side is whose soldier or squad it is about (null for the whole battle).</summary>
public readonly record struct LogEntry(float Time, int SoldierId, string Text, Side? Side, LogKind Kind);

public sealed class BattleResult
{
    public Side? Winner { get; init; }
    public float Time { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>Running record of decisions and events. The headless runner prints it; Godot shows the latest lines.</summary>
public sealed class BattleLog
{
    private readonly List<LogEntry> _entries = new();
    public int Capacity { get; set; } = 20000;
    public IReadOnlyList<LogEntry> Entries => _entries;
    public bool Verbose { get; set; } = true;

    public void Add(float time, int soldierId, string text, Side? side = null, LogKind kind = LogKind.Event)
    {
        if (_entries.Count >= Capacity) _entries.RemoveRange(0, Capacity / 4);
        _entries.Add(new LogEntry(time, soldierId, text, side, kind));
    }
}

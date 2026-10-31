using System.Numerics;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>A bullet fired this tick. Godot draws these as tracers and dust.</summary>
public readonly record struct ShotEvent(int ShooterId, Vector2 From, Vector2 Impact, bool Hit, bool MachineGun);

public readonly record struct CasualtyEvent(int SoldierId, int ShooterId, bool Killed);

public readonly record struct LogEntry(float Time, int SoldierId, string Text);

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

    public void Add(float time, int soldierId, string text)
    {
        if (_entries.Count >= Capacity) _entries.RemoveRange(0, Capacity / 4);
        _entries.Add(new LogEntry(time, soldierId, text));
    }
}

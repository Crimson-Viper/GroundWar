using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Data;

namespace GroundWar.Sim.Units;

/// <summary>A named place battalion commanders fight over (a village, a farm), or an HQ.</summary>
public sealed class KeyPoint
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public Vector2 Position { get; init; }
    public float Radius { get; init; } = 40f;
    /// <summary>The enemy's battalion HQ (the last objective of an attack).</summary>
    public bool IsHq { get; init; }
}

public enum Posture { Attack, Defend }

/// <summary>What battalion has given a company to do.</summary>
public enum BattalionTaskKind
{
    /// <summary>Kept back, out of the fight, ready to be sent where it's needed.</summary>
    Reserve,
    /// <summary>Holding a key point (or where it is, when <see cref="BattalionTask.Point"/> is null).</summary>
    Hold,
    /// <summary>Attacking a key point.</summary>
    Attack,
    /// <summary>A defender's reserve retaking a key point it has lost.</summary>
    Counterattack,
}

public sealed class BattalionTask
{
    public BattalionTaskKind Kind { get; init; }
    public KeyPoint? Point { get; init; }
    /// <summary>When the order was sent.</summary>
    public float Issued { get; init; }
    /// <summary>The company has said it refused this order, and when.</summary>
    public float RefusedAt { get; set; } = -1f;
    public bool Insisted { get; set; }
}

/// <summary>What battalion HQ last heard from a company. Battalion only knows what its companies tell it: a
/// company that has been cut to pieces is "fresh" until a report says otherwise.</summary>
public sealed class CompanyNews
{
    public float Time { get; set; } = -999f;
    public int Strength { get; set; }
    public MessageKind Last { get; set; }
    /// <summary>When it last reported taking its objective, failing, or refusing.</summary>
    public float TakenAt { get; set; } = -999f;
    public float FailedAt { get; set; } = -999f;
    public float RefusedAt { get; set; } = -999f;
    /// <summary>When it last sent a plan (a brief-back, or word that it's going ahead or changing plan).</summary>
    public float PlanAt { get; set; } = -999f;
}

/// <summary>A battalion commander and what he believes about the battle (milestone 7, part 2). He orders his
/// companies, never their squads. See docs/systems/battalion-commander.md.</summary>
public sealed class Battalion
{
    public Side Side { get; init; }
    public string Commander { get; init; } = "Major";
    public TraitsDef Traits { get; init; } = new();
    public Posture Posture { get; init; }
    public List<Company> Companies { get; } = new();
    /// <summary>The key points, plus the two HQs (as key points: his own, and the enemy's).</summary>
    public List<KeyPoint> Points { get; } = new();
    public KeyPoint EnemyHq { get; set; } = null!;
    public KeyPoint OwnHq { get; set; } = null!;
    public Dictionary<Company, BattalionTask> Tasks { get; } = new();
    public Dictionary<Company, CompanyNews> News { get; } = new();
    /// <summary>Who he believes holds each key point (null = nobody he knows of).</summary>
    public Dictionary<KeyPoint, Side?> Held { get; } = new();
    /// <summary>When an attack on a key point last failed.</summary>
    public Dictionary<KeyPoint, float> FailedAt { get; } = new();
    /// <summary>True when the computer runs this battalion. The player's own battalion only runs itself if he
    /// hands it over to his staff.</summary>
    public bool Active { get; set; }
    public float NextThink { get; set; }
    public float LastArtillery { get; set; } = -999f;
    /// <summary>What he is trying to do, in a line (shown in the game).</summary>
    public string Intent { get; set; } = "";
    /// <summary>Still pressing an attack or counterattack: the battle doesn't end for quiet while this is so.</summary>
    public bool Busy { get; set; }
    public bool Started { get; set; }

    public CompanyNews NewsOf(Company c)
    {
        if (!News.TryGetValue(c, out var n)) News[c] = n = new CompanyNews { Strength = c.StartStrength };
        return n;
    }

    /// <summary>The fraction of a company still fit, as far as battalion has heard.</summary>
    public float StrengthOf(Company c) => c.StartStrength == 0 ? 0f : NewsOf(c).Strength / (float)c.StartStrength;
}

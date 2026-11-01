using System.Numerics;
using GroundWar.Sim.Units;

namespace GroundWar.Sim.Comms;

/// <summary>How a message travels. See docs/systems/messages-and-delay.md.</summary>
public enum MessageMethod { Phone, Radio, Runner, Flare }

public enum MessageStatus { InTransit, Delivered, Lost }

/// <summary>What an order tells a squad to do. Halt and FallBack need no place; the rest do.</summary>
public enum OrderKind { Hold, Advance, Attack, Suppress, Halt, FallBack }

/// <summary>An order from HQ to one squad.</summary>
public sealed class Order
{
    public OrderKind Kind { get; init; }
    public Vector2 Objective { get; init; }
    public float Radius { get; init; } = 15f;
    /// <summary>For Suppress: where to fire from. Defaults to where the squad is when the order arrives.</summary>
    public Vector2? SupportPosition { get; init; }

    public bool CanGoByFlare => Kind is OrderKind.Halt or OrderKind.FallBack;

    public override string ToString() => Kind switch
    {
        OrderKind.Halt => "Halt and hold where you are",
        OrderKind.FallBack => "Fall back",
        OrderKind.Hold => "Hold the position",
        OrderKind.Advance => "Advance to the objective",
        OrderKind.Attack => "Attack the objective",
        _ => "Suppress the objective",
    };
}

/// <summary>A sighting passed on in a report: what the squad knew when the report was written.</summary>
public readonly record struct ReportedContact(int EnemyId, Vector2 Position, float SeenAt, float Confidence);

/// <summary>An order going down or a report going up. Reports carry the squad's sightings at the time of writing,
/// so HQ's picture of the enemy is always a little out of date.</summary>
public sealed class Message
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public MessageMethod Method { get; init; }
    public Group Group { get; init; } = null!;
    /// <summary>True for HQ to squad (an order), false for squad to HQ (a report).</summary>
    public bool Down { get; init; }
    public Order? Order { get; init; }
    public string Text { get; init; } = "";
    public List<ReportedContact> Contacts { get; init; } = new();
    public float SentTime { get; init; }
    /// <summary>For phone, radio and flare: when it arrives. A runner arrives when he gets there.</summary>
    public float ArriveTime { get; set; }
    public MessageStatus Status { get; set; }
    public float DoneTime { get; set; }

    // Runner state
    public Vector2 RunnerPosition { get; set; }
    public List<Vector2>? RunnerPath { get; set; }
    public int RunnerPathIndex { get; set; }
    public float RunnerRepath { get; set; }

    public string Describe() => Down ? $"Order to {Group.Name}: {Text}" : $"{Group.Name}: {Text}";
}

/// <summary>A field telephone cable from HQ along a route. Shells can cut it.</summary>
public sealed class PhoneLine
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public List<Vector2> Points { get; init; } = new();
    public Vector2 End => Points[^1];
    public bool Cut { get; set; }
    public float CutTime { get; set; }
}

/// <summary>A side's headquarters: the player's (or enemy commander's) end of every order and report.</summary>
public sealed class Headquarters
{
    public Side Side { get; init; }
    public string Name { get; init; } = "HQ";
    public Vector2 Position { get; init; }
    public bool HasRadio { get; init; }
    /// <summary>What HQ knows about the enemy: only what reports have brought in.</summary>
    public SideKnowledge Knowledge { get; init; } = null!;
}

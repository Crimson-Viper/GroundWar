using System.Numerics;
using GroundWar.Sim.Units;

namespace GroundWar.Sim.Comms;

/// <summary>How a message travels. See docs/systems/messages-and-delay.md.</summary>
public enum MessageMethod { Phone, Radio, Runner, Flare, Voice }

public enum MessageStatus { InTransit, Delivered, Lost }

/// <summary>What a message is about. Company commanders read reports by kind (a squad saying it is pinned, or has
/// taken the objective), and battalion HQ answers a brief-back with Approve or Reject.</summary>
public enum MessageKind { Order, Contact, Sitrep, Losses, Pinned, Broken, Taken, Failed, BriefBack, Refusal, Approve, Reject, Note, FireRequest }

/// <summary>What an order tells a squad to do. Halt and FallBack need no place; the rest do. FireMission is for a
/// mortar section (fire on a place); Attach puts a battalion asset (an MG section) under a company.</summary>
public enum OrderKind { Hold, Advance, Attack, Suppress, Halt, FallBack, FireMission, Attach }

/// <summary>What a fire mission fires: high explosive or smoke.</summary>
public enum FireKind { He, Smoke }

/// <summary>An order from HQ to one squad.</summary>
public sealed class Order
{
    public OrderKind Kind { get; init; }
    public Vector2 Objective { get; init; }
    public float Radius { get; init; } = 15f;
    /// <summary>For Suppress: where to fire from. Defaults to where the squad is when the order arrives.</summary>
    public Vector2? SupportPosition { get; init; }

    /// <summary>For Advance: just a place to get to (a start line, a support position), not an objective to take.</summary>
    public bool IsWaypoint { get; init; }

    /// <summary>For FireMission: HE or smoke, and how many rounds.</summary>
    public FireKind Fire { get; init; }
    public int Rounds { get; init; }
    /// <summary>For Attach: the company to join (null = back under battalion).</summary>
    public Company? AttachTo { get; init; }
    /// <summary>For FireMission: the request it answers (for a company commander's own request).</summary>
    public FireMission? Mission { get; init; }

    public bool CanGoByFlare => Kind is OrderKind.Halt or OrderKind.FallBack;

    public override string ToString() => Kind switch
    {
        OrderKind.Halt => "Halt and hold where you are",
        OrderKind.FallBack => "Fall back",
        OrderKind.Hold => "Hold the position",
        OrderKind.Advance => "Advance to the objective",
        OrderKind.Attack => "Attack the objective",
        OrderKind.FireMission => $"Fire mission: {Rounds} rounds {(Fire == FireKind.Smoke ? "smoke" : "HE")}",
        OrderKind.Attach => AttachTo != null ? $"Join {AttachTo.Name}" : "Come back under battalion",
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
    /// <summary>Set when the message is between a company HQ and one of its squads, not battalion HQ.</summary>
    public Company? Company { get; init; }
    /// <summary>A brief-back: the company commander's plan, sent up for HQ to see.</summary>
    public CompanyPlan? Plan { get; init; }
    /// <summary>A company commander's request for mortar fire, on its way to battalion.</summary>
    public FireMission? Mission { get; init; }
    /// <summary>True for HQ to squad (an order), false for squad to HQ (a report).</summary>
    public bool Down { get; init; }
    public Order? Order { get; init; }
    public MessageKind Kind { get; init; }
    /// <summary>For a report: how many of the sender's men were still fit when it was written.</summary>
    public int Strength { get; set; }
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

    public string Describe() => Down
        ? $"{(Company != null ? Company.CommanderName : "HQ")} to {(Group.IsCompanyHq ? Group.Company!.Name : Group.Name)}: {Text}"
        : $"{(Group.IsCompanyHq ? Group.Company!.Name : Group.Name)}{(Company != null ? " to " + Company.CommanderName : "")}: {Text}";
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
    /// <summary>The HQ's own men, who defend it (null = none: the HQ is just a place).</summary>
    public Group? Staff { get; set; }
    /// <summary>Overrun by the enemy (milestone 7): in a battalion battle, that loses the battle.</summary>
    public bool Destroyed { get; set; }
    /// <summary>Since when enemy men have been at the HQ with none of its own (-1 = not now).</summary>
    public float OverrunSince { get; set; } = -1f;
}

/// <summary>Guns off the map that battalion HQ calls on by radio (milestone 7). Out of reach of the enemy, so
/// they only run out of ammunition. See docs/systems/support-fire.md.</summary>
public sealed class Battery
{
    public Side Side { get; init; }
    public string Name { get; init; } = "Artillery";
    public int Guns { get; init; }
    public int AmmoHe { get; set; }
    public int AmmoSmoke { get; set; }
    public Queue<FireMission> Queue { get; } = new();
    public FireMission? Current { get; set; }
    public float NextRound { get; set; }
    public int Ammo(FireKind k) => k == FireKind.Smoke ? AmmoSmoke : AmmoHe;
}

public enum FireMissionStatus { Requested, Ordered, Firing, Done, Refused, Cancelled }

/// <summary>A request for mortar fire: a company commander's (sent up to battalion, which passes it to the mortars)
/// or battalion's own. See docs/systems/support-fire.md.</summary>
public sealed class FireMission
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public Vector2 Target { get; init; }
    public FireKind Kind { get; init; }
    public int Rounds { get; init; }
    /// <summary>The company that asked for it (null = battalion's own).</summary>
    public Company? RequestedBy { get; init; }
    public string Reason { get; init; } = "";
    public float RequestedAt { get; init; }
    public FireMissionStatus Status { get; set; }
    public int RoundsFired { get; set; }
    public int RoundsLanded { get; set; }
    public float FirstLanded { get; set; } = -1f;
    public float DoneAt { get; set; } = -1f;
    /// <summary>The mortar section firing it.</summary>
    public Group? FiredBy { get; set; }
    /// <summary>An "on call" target: asked for ahead of time, so the mortars are laid on it and wait for the word
    /// to fire (the commander's call, when his assault is ready). -1 = not called yet.</summary>
    public bool OnCall { get; init; }
    public float CallAt { get; set; } = -1f;
    public bool Waiting => OnCall && CallAt < 0f;
    /// <summary>Fired by the artillery off the map rather than mortars.</summary>
    public Battery? Battery { get; set; }
    public bool Artillery => Battery != null;
    public string Describe() => $"{Rounds} rounds {(Kind == FireKind.Smoke ? "smoke" : "HE")}{(Artillery ? " (artillery)" : "")}";
}

using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Data;

namespace GroundWar.Sim.Units;

/// <summary>Where the company commander is with his current order.</summary>
public enum CompanyState
{
    /// <summary>No order yet, or the last one is done: squads hold where they are.</summary>
    AwaitingOrders,
    /// <summary>A plan is made and has been sent to HQ; it runs 10 s after HQ hears it unless HQ steps in.</summary>
    BriefBack,
    /// <summary>Carrying out the plan, phase by phase.</summary>
    Executing,
    /// <summary>The order looked like a death trap: he has said so and is holding until told otherwise.</summary>
    Refused,
    /// <summary>The plan is done (objective taken, or holding as ordered).</summary>
    Holding,
}

public enum PlanKind { FrontalAssault, PinAndFlankLeft, PinAndFlankRight, WaitForSupport, Defend, Advance, Suppress, Halt, FallBack }

public enum PhaseKind { MoveUp, EstablishFire, Assault, Consolidate, Hold }

/// <summary>Mortar fire the commander means to ask for when a phase starts.</summary>
public sealed class PlannedFire
{
    public int Phase { get; init; }
    public Vector2 Target { get; init; }
    public FireKind Kind { get; init; }
    public int Rounds { get; init; }
    public string Reason { get; init; } = "";
}

/// <summary>One squad's job in one phase.</summary>
public sealed class SquadTask
{
    public Group Squad { get; init; } = null!;
    public Order Order { get; init; } = null!;
}

public sealed class PlanPhase
{
    public PhaseKind Kind { get; init; }
    public string Name { get; init; } = "";
    public List<SquadTask> Tasks { get; } = new();
    /// <summary>Seconds before the commander decides the phase has stalled.</summary>
    public float TimeLimit { get; init; } = 240f;
}

/// <summary>A course of action: phases of squad tasks, with the commander's estimate of how it will go.</summary>
public sealed class CompanyPlan
{
    public PlanKind Kind { get; init; }
    public string Summary { get; set; } = "";
    public List<PlanPhase> Phases { get; } = new();
    public float ExpectedLoss { get; set; }
    public float Success { get; set; }
    public float Speed { get; set; }
    public float Score { get; set; }
    /// <summary>The commander's guess at each squad's losses (fraction of its men).</summary>
    public List<(Group Squad, float Loss)> SquadLosses { get; } = new();
    /// <summary>The squad giving covering fire, in a pin-and-flank plan.</summary>
    public Group? Support { get; init; }
    /// <summary>Mortar fire he will ask battalion for, and in which phase.</summary>
    public List<PlannedFire> Fires { get; } = new();
    /// <summary>Arrows for the brief-back on the player's map: (from, to, phase index).</summary>
    public List<(Vector2 From, Vector2 To, int Phase)> Arrows { get; } = new();
}

/// <summary>What a squad last told its company commander, and when.</summary>
public sealed class SquadReport
{
    public float Time { get; set; } = -999f;
    public int Strength { get; set; }
    public bool Pinned { get; set; }
    public float PinnedAt { get; set; } = -999f;
    public bool Taken { get; set; }
    public bool Failed { get; set; }
    public bool Broken { get; set; }
}

/// <summary>A company: a commander with a personality, his HQ group (him and a radio operator) and his squads.
/// He turns orders from battalion HQ into plans and runs them. See docs/systems/company-commander.md.</summary>
public sealed class Company
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string CommanderName { get; init; } = "";
    public Side Side { get; init; }
    public Group HqGroup { get; init; } = null!;
    public List<Group> Squads { get; } = new();
    public TraitsDef Traits { get; init; } = new();
    /// <summary>What the commander knows of the enemy: what his squads have reported.</summary>
    public SideKnowledge Knowledge { get; init; } = null!;

    public CompanyState State { get; set; } = CompanyState.AwaitingOrders;
    public Order? CurrentOrder { get; set; }
    public CompanyPlan? Plan { get; set; }
    /// <summary>The plans he weighed up for the current order, best first (for the brief-back and the log).</summary>
    public List<CompanyPlan> Options { get; } = new();
    public int PhaseIndex { get; set; }
    public float PhaseStarted { get; set; }
    public float PlanStarted { get; set; }
    public int StrengthAtPlan { get; set; }
    /// <summary>The enemy machine guns he knew of when he made (or last checked) the plan.</summary>
    public HashSet<int> MgsPlannedFor { get; } = new();
    public Message? BriefBackMessage { get; set; }
    /// <summary>When the brief-back plan starts unless HQ steps in (-1 = waiting for HQ to hear it).</summary>
    /// <summary>The last order he refused, so that if HQ sends it again he knows they insist.</summary>
    public Order? RefusedOrder { get; set; }
    public float NextThink { get; set; }
    public float LastSitrep { get; set; } = -999f;
    public HashSet<Group> PinnedSquads { get; } = new();
    public string Status { get; set; } = "Waiting for orders";
    /// <summary>The last word from each squad.</summary>
    public Dictionary<Group, SquadReport> Reports { get; } = new();
    /// <summary>The commander (and his HQ) are out of the fight; the squads carry on with their last orders.</summary>
    public bool CommanderDown { get; set; }
    /// <summary>New plans made for the current order (after the first).</summary>
    public int Replans { get; set; }
    public float PlanMadeAt { get; set; }
    /// <summary>HQ sent the order again after he refused it: he does it, whatever he thinks.</summary>
    public bool Insisted { get; set; }
    /// <summary>When battalion HQ heard the brief-back (-1 = not yet), and whether it has answered.</summary>
    public float BriefBackHeardAt { get; set; } = -1f;
    public bool BriefBackAnswered { get; set; }
    /// <summary>The commander's guess at each squad's losses (fraction of its men).</summary>
    public List<(Group Squad, float Loss)> SquadLosses { get; } = new();
    /// <summary>The squad giving covering fire in the current plan, if any.</summary>
    public Group? SupportSquad { get; set; }
    /// <summary>Enemy machine guns he has already turned the support squad's fire onto.</summary>
    public HashSet<int> MgsEngaged { get; } = new();
    /// <summary>Why he refused the last order (for the player).</summary>
    public string RefusalReason { get; set; } = "";
    /// <summary>The on-call fire missions he asked for when the plan started, by the planned fire they are for.</summary>
    public Dictionary<PlannedFire, FireMission> PlannedMissions { get; } = new();
    /// <summary>Every fire request he has made: where and when (so he doesn't keep asking for the same thing).</summary>
    public List<(Vector2 Target, float Time)> FireRequests { get; } = new();
    /// <summary>Mortar fire he asked for when he refused an order; once it has landed he thinks again.</summary>
    public FireMission? RefusalFire { get; set; }
    public int RefusalFires { get; set; }

    public PlanPhase? Phase => Plan != null && PhaseIndex < Plan.Phases.Count ? Plan.Phases[PhaseIndex] : null;
    public int Effective => Squads.Sum(s => s.CountEffective);
    public int StartStrength => Squads.Sum(s => s.StartStrength);
    public Vector2 Centroid
    {
        get
        {
            Vector2 sum = Vector2.Zero; int n = 0;
            foreach (var g in Squads) if (g.CountEffective > 0) { sum += g.Centroid; n++; }
            return n > 0 ? sum / n : HqGroup.Centroid;
        }
    }
}

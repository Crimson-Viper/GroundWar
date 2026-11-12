using System.Numerics;

namespace GroundWar.Sim.Units;

/// <summary>What a squad has been told to do (by the scenario now; by its company commander from milestone 5).</summary>
public enum GroupOrder
{
    /// <summary>Defend the objective area.</summary>
    Hold,
    /// <summary>Move to the objective, fight if needed, then hold it.</summary>
    Advance,
    /// <summary>Take the objective from the enemy: fix them with fire, go round, assault.</summary>
    Attack,
    /// <summary>Go to the support position and keep the enemy at the objective pinned with fire.</summary>
    Suppress,
}

/// <summary>How the squad is going about its order right now. Picked by the squad leader (see SquadBrain).</summary>
public enum Tactic
{
    Travel,          // moving in a loose file, no enemy expected
    Bounding,        // half moves, half watches
    FireAndMovement, // one team shoots while the other rushes forward, then swap
    Flank,           // gun team pins the enemy, rifle team goes round the side
    Assault,         // the final rush onto the enemy position
    Suppress,        // everyone fires on the enemy position from where they are
    Defend,          // hold positions with good fields of fire
    FallBack,        // retreat by teams, one covering the other
}

/// <summary>The squad's nerve as a unit, from its cohesion. Steady; Shaken (goes to ground, won't start an assault);
/// Wavering (gives up an attack, falls back from a defence); Broken (runs for its rally point).</summary>
public enum SquadNerve { Steady, Shaken, Wavering, Broken }

public enum TeamRole { Gun, Rifle }

/// <summary>Half a squad. WW2 squads split into a gun team (around the light machine gun) and a rifle team.</summary>
public sealed class Team
{
    public int Id { get; init; }
    public TeamRole Role { get; init; }
    public string Name => Role == TeamRole.Gun ? "gun team" : "rifle team";
    public List<Soldier> Members { get; } = new();
    public Vector2 Destination { get; set; }
    public bool Moving { get; set; }
    public float MoveStarted { get; set; }

    public IEnumerable<Soldier> Effective => Members.Where(m => m.IsEffective);
    public int CountEffective => Members.Count(m => m.IsEffective);

    public Vector2 Centroid
    {
        get
        {
            Vector2 sum = Vector2.Zero;
            int n = 0;
            foreach (var m in Members)
                if (m.IsEffective) { sum += m.Position; n++; }
            return n > 0 ? sum / n : (Members.Count > 0 ? Members[0].Position : Vector2.Zero);
        }
    }
}

/// <summary>A squad: 8 to 12 men under a sergeant, with one order, two teams and its own picture of the enemy.</summary>
public sealed class Group
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public string Name { get; init; } = "";
    public GroupOrder Order { get; set; }
    public Vector2 Objective { get; set; }
    public float ObjectiveRadius { get; set; } = 15f;
    /// <summary>For a Suppress order: where to set up the guns.</summary>
    public Vector2? SupportPosition { get; set; }
    /// <summary>Where the enemy is expected before anyone is spotted.</summary>
    public Vector2 Facing { get; set; }
    public Vector2 Rear { get; set; }
    public List<Soldier> Members { get; } = new();
    public List<Team> Teams { get; } = new();
    public Soldier? Leader { get; set; }
    public bool HasBroken { get; set; }
    public int StartStrength { get; set; }

    // The squad's nerve as a unit (see Battle.Cohesion.cs)
    /// <summary>0 to 100: how well the squad is holding together.</summary>
    public float Cohesion { get; set; } = 100f;
    public SquadNerve Nerve { get; set; } = SquadNerve.Steady;
    /// <summary>Taking fire from the side, from behind, or from two directions at once.</summary>
    public bool Flanked { get; set; }
    public float FlankedSince { get; set; } = -999f;
    /// <summary>Which way the squad is facing the enemy: where he first showed himself. Null until he does.</summary>
    public Vector2? Front { get; set; }
    internal float FrontFireAt { get; set; }
    /// <summary>Where the flanking fire is coming from.</summary>
    public Vector2 FlankedFrom { get; set; }
    /// <summary>Enemy known between the squad and its way to the rear.</summary>
    public bool CutOff { get; set; }
    /// <summary>Where its broken men run to and pull themselves together. Null = nowhere safe: off the field.</summary>
    public Vector2? RallyPoint { get; set; }
    public float RallyPointSet { get; set; } = -999f;
    public float BrokeAt { get; set; } = -999f;
    /// <summary>How many times the squad has broken and rallied.</summary>
    public int Rallies { get; set; }
    /// <summary>The squad gave up its attack because its nerve went (not because it was ordered to).</summary>
    public bool GaveUp { get; set; }
    /// <summary>The order it gave up, to take up again if it steadies before anyone tells it otherwise.</summary>
    public GroupOrder? GaveUpOrder { get; set; }
    /// <summary>The battalion HQ's own men: they stand and defend it whatever happens.</summary>
    public bool IsHqStaff { get; set; }
    internal int CohesionCasualties { get; set; }
    internal float CohesionLeaderLost { get; set; } = -999f;
    internal float LastUnderFire { get; set; } = -999f;
    /// <summary>When any of the squad was last shot at.</summary>
    public float LastUnderFireTime => LastUnderFire;
    internal float NextCohesion { get; set; }
    /// <summary>When the squad last pulled back because its nerve went.</summary>
    public float LastFallBack { get; set; } = -999f;
    /// <summary>Shaken on the attack and lying where it is, shooting back.</summary>
    public bool WentToGround { get; set; }
    /// <summary>The squad knew of some enemy when cohesion was last worked out (a second ago).</summary>
    internal bool KnewOfEnemy { get; set; }
    /// <summary>Uses squad cohesion: a squad of men on foot (not tanks, crews, or an HQ).</summary>
    public bool HasCohesion => !IsVehicles && !IsCrew && !IsCompanyHq && !IsHqStaff && StartStrength >= 3;

    /// <summary>What this squad knows about the enemy: only what its own men saw or heard, or nearby squads told it.</summary>
    public SideKnowledge Knowledge { get; }

    // Squad leader's state
    public Tactic Tactic { get; set; } = Tactic.Travel;
    public float TacticStarted { get; set; }
    public string Status { get; set; } = "";
    public float NextThink { get; set; }
    public Vector2 EnemyEstimate { get; set; }
    public Vector2 FlankWaypoint { get; set; }
    public Vector2 FlankPoint { get; set; }
    public bool FlankLegTwo { get; set; }
    public float PinnedSince { get; set; } = -1f;
    public Vector2 HoldPosition { get; set; }
    public int MovingTeam { get; set; } = 1;
    public GroupOrder OriginalOrder { get; set; }
    public bool TookObjective { get; set; }
    public bool AttackFailed { get; set; }
    /// <summary>When the squad last lost its leader (-999 = never). Losing one replacement after another
    /// in quick succession is one shock to the men, not several.</summary>
    public float LeaderLostTime { get; set; } = -999f;
    /// <summary>The company this squad belongs to (or whose HQ it is), if any.</summary>
    public Company? Company { get; set; }
    /// <summary>This group is a company HQ (the commander and his radio operator), not a fighting squad.</summary>
    public bool IsCompanyHq => Company != null && Company.HqGroup == this;
    /// <summary>The squad carries a radio set.</summary>
    public bool HasRadio { get; set; }
    // What the squad has already reported to HQ, so it doesn't repeat itself.
    public bool ReportedContact { get; set; }
    public float LastSitrep { get; set; } = -999f;
    public int ReportedLossQuarter { get; set; }
    public bool ReportedPinned { get; set; }
    public bool ReportedBroken { get; set; }
    public bool ReportedTaken { get; set; }
    public bool ReportedFailed { get; set; }
    /// <summary>Has been ordered to attack, advance or suppress at some point (for the time-limit result).</summary>
    public bool EverAttacked { get; set; }
    /// <summary>The Advance order is only to get somewhere (a waypoint), so arriving doesn't count as taking an objective.</summary>
    public bool MoveOnly { get; set; }
    /// <summary>The last order that reached the squad (so a company commander knows his order has arrived).</summary>
    public GroundWar.Sim.Comms.Order? LastOrderReceived { get; set; }
    /// <summary>A group of vehicles (a tank section) rather than a squad of men.</summary>
    public bool IsVehicles => Members.Count > 0 && Members.All(m => m.IsTank);
    /// <summary>A mortar section: its men work the tubes instead of fighting as a squad.</summary>
    public MortarSection? Mortars { get; set; }
    /// <summary>A battalion asset (an MG section or mortars) that the player can attach to a company.</summary>
    public bool IsAsset { get; set; }
    /// <summary>The crews of a tank platoon (milestone 8): they ride inside until they bail out.</summary>
    public bool IsCrew { get; set; }
    /// <summary>A tank platoon: which half is bounding forward (the other watches over it), and since when.</summary>
    public int BoundingSection { get; set; }
    public float BoundStarted { get; set; }
    public bool PulledBack { get; set; }
    public bool IsAttacking => Order is GroupOrder.Attack or GroupOrder.Advance or GroupOrder.Suppress;

    public Group(Side side)
    {
        Side = side;
        Knowledge = new SideKnowledge(side);
    }

    public Team Gun => Teams[0];
    public Team Rifles => Teams.Count > 1 ? Teams[1] : Teams[0];

    public int CountActive => Members.Count(m => m.IsActive);
    public int CountEffective => Members.Count(m => m.IsEffective);

    public Vector2 Centroid
    {
        get
        {
            Vector2 sum = Vector2.Zero;
            int n = 0;
            foreach (var m in Members)
                if (m.IsEffective) { sum += m.Position; n++; }
            return n > 0 ? sum / n : (Members.Count > 0 ? Members[0].Position : Objective);
        }
    }
}

/// <summary>The tubes, ammunition and fire missions of a mortar section (milestone 7).</summary>
public sealed class MortarSection
{
    public int Tubes { get; init; }
    public int AmmoHe { get; set; }
    public int AmmoSmoke { get; set; }
    public Queue<GroundWar.Sim.Comms.FireMission> Queue { get; } = new();
    public GroundWar.Sim.Comms.FireMission? Current { get; set; }
    /// <summary>When the tubes are laid on the current target and can start firing.</summary>
    public float ReadyAt { get; set; }
    public float NextRound { get; set; }
    public int Ammo(GroundWar.Sim.Comms.FireKind k) => k == GroundWar.Sim.Comms.FireKind.Smoke ? AmmoSmoke : AmmoHe;
}

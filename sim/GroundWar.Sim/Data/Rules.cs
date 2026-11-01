namespace GroundWar.Sim.Data;

// Tuning numbers for the battle. Loaded from data/rules.json; the defaults here are only a fallback.
// Every number that affects how a fight feels should live here (or in weapons.json), not in code.

public sealed class Rules
{
    public SoldierRules Soldier { get; set; } = new();
    public PerceptionRules Perception { get; set; } = new();
    public CombatRules Combat { get; set; } = new();
    public MoraleRules Morale { get; set; } = new();
    public AiRules Ai { get; set; } = new();
    public SquadRules Squad { get; set; } = new();
    public CommsRules Comms { get; set; } = new();
    public CompanyRules Company { get; set; } = new();
    public SupportRules Support { get; set; } = new();
    public BattalionRules Battalion { get; set; } = new();
}

public sealed class SquadRules
{
    public float ThinkInterval { get; set; } = 1.5f;
    public float TravelStep { get; set; } = 40f;
    public float BoundStep { get; set; } = 30f;
    public float RushStep { get; set; } = 22f;
    public float TeamRadius { get; set; } = 9f;
    public float MaxBoundTime { get; set; } = 40f;
    public float BoundingRange { get; set; } = 150f;
    public float ContactRange { get; set; } = 350f;
    public float PlanRange { get; set; } = 140f;
    public float QuietEndTime { get; set; } = 60f;
    public float FlankOffset { get; set; } = 50f;
    public float FlankSideStep { get; set; } = 45f;
    public float AssaultRange { get; set; } = 60f;
    public float LiftFireRange { get; set; } = 25f;
    public float EnemyQuietTime { get; set; } = 4f;
    public float PinnedAbortTime { get; set; } = 20f;
    public float FallBackStrength { get; set; } = 0.4f;
    public float FallBackStep { get; set; } = 40f;
    public float ShareRadius { get; set; } = 80f;
    public float AreaFireAccuracy { get; set; } = 0.3f;
    public float FriendlyFireSafety { get; set; } = 8f;
}

public sealed class SoldierRules
{
    public float WalkSpeed { get; set; } = 1.5f;
    public float RunSpeed { get; set; } = 3.5f;
    public float CrawlSpeed { get; set; } = 0.6f;
    public float CraterSpeedFactor { get; set; } = 0.7f;
    public float SkillMin { get; set; } = 0.8f;
    public float SkillMax { get; set; } = 1.2f;
    public float MoraleMin { get; set; } = 60f;
    public float MoraleMax { get; set; } = 85f;
    public float LeaderMoraleBonus { get; set; } = 10f;
    public float KillChanceOnHit { get; set; } = 0.45f;
}

public sealed class PerceptionRules
{
    public float Interval { get; set; } = 0.5f;
    public float ViewRange { get; set; } = 400f;
    public float AutoSpotRange { get; set; } = 25f;
    public int MaxTracked { get; set; } = 12;
    public float BaseSpotChance { get; set; } = 0.5f;
    public float FarSpotChance { get; set; } = 0.04f;
    public float FiringMultiplier { get; set; } = 4f;
    public float FiringWindow { get; set; } = 3f;
    public float MovingMultiplier { get; set; } = 1.5f;
    public float ProneMultiplier { get; set; } = 0.5f;
    public float CoverMultiplier { get; set; } = 0.5f;
    public float ContactMemory { get; set; } = 30f;
    public float SharedContactWindow { get; set; } = 2f;
    public float MuzzleFlashRevealChance { get; set; } = 0.35f;
}

public sealed class CombatRules
{
    public float LowWallProtection { get; set; } = 0.7f;
    public float CraterProtection { get; set; } = 0.6f;
    /// <summary>A dug foxhole: all-round protection, better than a crater.</summary>
    public float FoxholeProtection { get; set; } = 0.75f;
    public float CoverCheckDistance { get; set; } = 1.8f;
    public float StandingHitFactor { get; set; } = 1f;
    public float CrouchedHitFactor { get; set; } = 0.6f;
    public float ProneHitFactor { get; set; } = 0.35f;
    public float MovingTargetFactor { get; set; } = 0.7f;
    public float ShakenAccuracy { get; set; } = 0.8f;
    public float SuppressionAccuracyLoss { get; set; } = 0.75f;
    public float CoverSuppressionShield { get; set; } = 0.5f;
}

public sealed class MoraleRules
{
    public float ShakenBelow { get; set; } = 50f;
    public float PinnedBelow { get; set; } = 32f;
    public float BrokenBelow { get; set; } = 18f;
    public float RallyAbove { get; set; } = 40f;
    public float RallySafeDistance { get; set; } = 80f;

    public float SuppressionShaken { get; set; } = 35f;
    public float SuppressionPinned { get; set; } = 55f;
    public float SuppressionNoFire { get; set; } = 80f;
    public float SuppressionMax { get; set; } = 100f;
    public float SuppressionDecayDelay { get; set; } = 1.5f;
    public float SuppressionDecayPerSecond { get; set; } = 12f;

    public float NearMissMoralePerSuppression { get; set; } = 0.08f;
    public float FriendKilled { get; set; } = 12f;
    public float FriendWounded { get; set; } = 7f;
    public float FriendRadius { get; set; } = 25f;
    public float LeaderKilled { get; set; } = 15f;
    /// <summary>Morale lost per second by a man who can see an enemy tank this close and has nothing that can hurt it.</summary>
    public float TankFearPerSecond { get; set; } = 0.8f;
    public float TankFearRange { get; set; } = 150f;
    /// <summary>Seconds after losing a leader during which losing his replacement too adds no further shock.</summary>
    public float LeaderShockWindow { get; set; } = 30f;
    public float EnemyDownSeen { get; set; } = 3f;
    public float GroupBrokenFraction { get; set; } = 0.5f;
    public float GroupBrokenShock { get; set; } = 12f;

    public float RecoverPerSecond { get; set; } = 0.4f;
    public float RecoverInCoverPerSecond { get; set; } = 0.3f;
    public float RecoverLeaderNearbyPerSecond { get; set; } = 0.3f;
    public float LeaderRadius { get; set; } = 25f;
    public float AloneDrainPerSecond { get; set; } = 0.3f;
    public float AloneRadius { get; set; } = 15f;
    public float NoAmmoDrainPerSecond { get; set; } = 0.5f;
    public float QuietSuppression { get; set; } = 10f;
}

public sealed class AiRules
{
    public float ThinkInterval { get; set; } = 0.4f;
    public float CoverSearchRadius { get; set; } = 25f;
    public float SuppressedCoverSearchRadius { get; set; } = 8f;
    public float StickinessBonus { get; set; } = 10f;
    /// <summary>Seconds of digging for one man to dig a foxhole (shortened from real life so it shows in a battle).</summary>
    public float DigTime { get; set; } = 90f;
    /// <summary>A man only digs if nobody has shot at him for this many seconds.</summary>
    public float DigQuietTime { get; set; } = 10f;
    public float UnderFireWindow { get; set; } = 5f;
    public float MapEdgeRoutDistance { get; set; } = 4f;
}

/// <summary>How orders and reports travel between HQ and squads (milestone 4). See docs/systems/messages-and-delay.md.</summary>
public sealed class CommsRules
{
    /// <summary>Seconds for a telephone message: cranking the handset, getting the right man, saying it.</summary>
    public float PhoneDelay { get; set; } = 12f;
    public float PhoneDelayJitter { get; set; } = 5f;
    /// <summary>A squad can use a phone line if its sergeant is this close to the line's end.</summary>
    public float PhoneReach { get; set; } = 25f;
    /// <summary>A shell bursting this close to a cable cuts it.</summary>
    public float LineCutRadius { get; set; } = 3f;
    /// <summary>Seconds for a radio message: coding it, sending, decoding.</summary>
    public float RadioDelay { get; set; } = 30f;
    public float RadioDelayJitter { get; set; } = 10f;
    /// <summary>Chance a radio message doesn't get through first time and has to be sent again (double the delay).</summary>
    public float RadioRepeatChance { get; set; } = 0.15f;
    /// <summary>Seconds from a flare going up to the squad acting on it.</summary>
    public float FlareDelay { get; set; } = 6f;
    /// <summary>A squad sees a flare if its sergeant has a clear line to HQ within this range.</summary>
    public float FlareRange { get; set; } = 1500f;
    /// <summary>Metres a second a runner covers (running, taking cover now and then).</summary>
    public float RunnerSpeed { get; set; } = 3f;
    /// <summary>Enemies who can see a runner within this range may shoot him.</summary>
    public float RunnerDangerRange { get; set; } = 250f;
    /// <summary>Chance a second that a runner is hit, per enemy that can see him, at point blank (less further out).</summary>
    public float RunnerHitPerSecond { get; set; } = 0.004f;
    /// <summary>When a runner carrying an order is shot, HQ sends the order again this many seconds later (when he hasn't come back).</summary>
    public float RunnerLostResend { get; set; } = 60f;
    /// <summary>Squads in contact send a situation report this often.</summary>
    public float SitrepInterval { get; set; } = 30f;
    /// <summary>HQ forgets a sighting this long after the enemy was last seen (it keeps old news longer than a squad does).</summary>
    public float HqContactMemory { get; set; } = 120f;
    /// <summary>True: the player sees the enemy only as reports reach HQ. False: as soon as any of their soldiers sees him.</summary>
    public bool PlayerSeesReportsOnly { get; set; } = true;

    // Inside a company (milestone 5): the commander and his squads.
    /// <summary>A squad sergeant this close to his company commander just shouts (or walks over).</summary>
    public float VoiceRange { get; set; } = 60f;
    public float VoiceDelay { get; set; } = 3f;
    /// <summary>Seconds for a message on the company's own short-range radio net (plain speech, no coding).</summary>
    public float CompanyRadioDelay { get; set; } = 10f;
    public float CompanyRadioDelayJitter { get; set; } = 4f;
    /// <summary>A company commander sends battalion a situation report this often.</summary>
    public float CompanySitrepInterval { get; set; } = 60f;
    /// <summary>A brief-back plan starts this long after HQ hears it, unless the player approves or rejects it first.</summary>
    public float BriefBackWait { get; set; } = 10f;
    /// <summary>Seconds for HQ's "approved" or "rejected" to reach a commander who is waiting on the radio for it.</summary>
    public float BriefBackReplyDelay { get; set; } = 5f;
}

/// <summary>How company commanders weigh plans (milestone 5). See docs/systems/company-commander.md.</summary>
public sealed class CompanyRules
{
    /// <summary>Base expected loss (fraction of the company) above which a plan is a death trap. Traits move it.</summary>
    public float DeathTrapLoss { get; set; } = 0.35f;
    /// <summary>A plan whose chance of success is below this is a death trap too.</summary>
    public float DeathTrapSuccess { get; set; } = 0.3f;
    /// <summary>Fire value of an enemy the commander assumes is on an objective he knows nothing about (plus 2 per point of caution).</summary>
    public float AssumedEnemyFire { get; set; } = 6f;
    /// <summary>How deadly a second of exposure to one point of enemy fire is (tunes expected losses).</summary>
    public float ExposureLethality { get; set; } = 0.00045f;
    /// <summary>Distance from the objective of the start line, the support position and the flank assembly area.</summary>
    public float StartLineDistance { get; set; } = 170f;
    public float SupportDistance { get; set; } = 130f;
    public float FlankWidth { get; set; } = 70f;
    /// <summary>Seconds the support squad fires before the flankers go in.</summary>
    public float EstablishFireTime { get; set; } = 20f;
    /// <summary>Distance the company HQ keeps behind its squads.</summary>
    public float HqBehind { get; set; } = 60f;
    /// <summary>Losing this fraction of the men he had when he made the plan makes the commander think again.</summary>
    public float ReplanLoss { get; set; } = 0.35f;
    /// <summary>After this many new plans for one order, he stops and holds.</summary>
    public int MaxReplans { get; set; } = 2;
}

/// <summary>Battalion support (milestone 7): mortars, smoke and fire requests. See docs/systems/support-fire.md.</summary>
public sealed class SupportRules
{
    /// <summary>Seconds for a mortar section to lay its tubes on a new target before the first round.</summary>
    public float SetUpTime { get; set; } = 12f;
    /// <summary>Seconds between rounds from one tube.</summary>
    public float RoundInterval { get; set; } = 5f;
    /// <summary>Time of flight: this many seconds, plus one per <see cref="FlightSpeed"/> metres.</summary>
    public float FlightTime { get; set; } = 8f;
    public float FlightSpeed { get; set; } = 150f;
    /// <summary>Spread of the first rounds (metres, one standard deviation), and of later ones once the fall of
    /// shot has been corrected.</summary>
    public float FirstSpread { get; set; } = 30f;
    public float AdjustedSpread { get; set; } = 14f;
    /// <summary>Mortars can't fire closer than this, or further.</summary>
    public float MinRange { get; set; } = 80f;
    public float MaxRange { get; set; } = 2500f;
    /// <summary>Two crew are needed to work each tube.</summary>
    public int CrewPerTube { get; set; } = 2;
    /// <summary>Smoke: full radius of one round's cloud (metres), how long it lasts, how long it takes to build up.</summary>
    public float SmokeRadius { get; set; } = 14f;
    public float SmokeDuration { get; set; } = 75f;
    public float SmokeGrowTime { get; set; } = 6f;
    /// <summary>Wind in metres per second (x, y): smoke drifts with it.</summary>
    public float[] Wind { get; set; } = { 0.4f, 0.15f };
    /// <summary>Rounds a commander asks for: HE prep fire, smoke, and quick fire on a machine gun.</summary>
    public int PrepRounds { get; set; } = 9;
    public int SmokeRounds { get; set; } = 6;
    public int QuickRounds { get; set; } = 4;
    /// <summary>Seconds a company commander waits between his own fire requests on the same area.</summary>
    public float RequestCooldown { get; set; } = 90f;
    /// <summary>Friendly troops closer than this to a target make the commander lift or shift the fire.</summary>
    public float DangerClose { get; set; } = 60f;
    /// <summary>A defending commander fires on attackers seen within this distance of his position.</summary>
    public float DefensiveFireRange { get; set; } = 350f;

    /// <summary>Artillery (guns off the map): seconds for the call to reach the guns by radio, to lay them, between
    /// rounds from one gun, and in flight; spread of the first and of corrected rounds (metres).</summary>
    public float ArtilleryCallDelay { get; set; } = 25f;
    public float ArtillerySetUpTime { get; set; } = 20f;
    public float ArtilleryRoundInterval { get; set; } = 8f;
    public float ArtilleryFlightTime { get; set; } = 25f;
    public float ArtilleryFirstSpread { get; set; } = 55f;
    public float ArtilleryAdjustedSpread { get; set; } = 28f;
    /// <summary>Rounds battalion asks for in one artillery mission.</summary>
    public int ArtilleryRounds { get; set; } = 8;
}

/// <summary>Battalion commanders and HQs (milestone 7, part 2). See docs/systems/battalion-commander.md.</summary>
public sealed class BattalionRules
{
    /// <summary>Seconds between a battalion commander's looks at the battle.</summary>
    public float ThinkInterval { get; set; } = 20f;
    /// <summary>An HQ is destroyed when enemy men are within this many metres of it and none of its own are, for
    /// <see cref="HqCaptureTime"/> seconds.</summary>
    public float HqCaptureRadius { get; set; } = 30f;
    public float HqCaptureTime { get; set; } = 15f;
    /// <summary>A company with at least this fraction of its men fit is fresh enough to attack.</summary>
    public float FreshStrength { get; set; } = 0.55f;
    /// <summary>Below this fraction a company is spent: it holds where it is.</summary>
    public float SpentStrength { get; set; } = 0.3f;
    /// <summary>Seconds after a failed attack before the same place is tried again.</summary>
    public float RetryDelay { get; set; } = 180f;
    /// <summary>Seconds he waits on a company that refused before insisting or giving up on that place.</summary>
    public float RefusalWait { get; set; } = 120f;
    /// <summary>An attack that hasn't taken its key point after this long is treated as having failed.</summary>
    public float AttackTimeLimit { get; set; } = 900f;
    /// <summary>Enemy known this close to a key point count as being at it (on top of its radius).</summary>
    public float KeyPointRange { get; set; } = 40f;
    /// <summary>Enemy known this close to his HQ make a defender send his reserve to guard it.</summary>
    public float HqThreatRange { get; set; } = 350f;
    /// <summary>Seconds between his artillery missions, the fewest enemy known together worth one, and how close to
    /// his own men he won't fire.</summary>
    public float ArtilleryCooldown { get; set; } = 120f;
    public int ArtilleryMinTargets { get; set; } = 5;
    public float ArtilleryDangerClose { get; set; } = 110f;
    /// <summary>Sightings older than this (seconds) are too stale to fire on or count.</summary>
    public float ContactAge { get; set; } = 90f;
}

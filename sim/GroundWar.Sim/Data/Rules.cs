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
    public float UnderFireWindow { get; set; } = 5f;
    public float MapEdgeRoutDistance { get; set; } = 4f;
}

namespace GroundWar.Sim.Data;

/// <summary>A battle setup from data/scenarios/*.json: which map, who fights, and their orders.</summary>
public sealed class ScenarioDef
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Map { get; set; } = "";
    public float TimeLimit { get; set; } = 600f;
    public List<SideDef> Sides { get; set; } = new();
    /// <summary>Orders HQ sends during the battle (the enemy's plan, or a scripted test). They travel like any message.</summary>
    public List<ScheduledOrderDef> Orders { get; set; } = new();
    /// <summary>Companies: a commander, his HQ group and the squads under him (milestone 5).</summary>
    public List<CompanyDef> Companies { get; set; } = new();
}

/// <summary>A company: its commander's personality, his HQ group, and his squads (by group name).</summary>
public sealed class CompanyDef
{
    public string Name { get; set; } = "";
    public string Commander { get; set; } = "Captain";
    /// <summary>The group that is the company HQ (the commander and his radio operator).</summary>
    public string Hq { get; set; } = "";
    public List<string> Squads { get; set; } = new();
    public TraitsDef Traits { get; set; } = new();
}

/// <summary>A commander's personality, each 1 (low) to 5 (high). See docs/systems/company-commander.md.</summary>
public sealed class TraitsDef
{
    public int Aggression { get; set; } = 3;
    public int Caution { get; set; } = 3;
    public int Initiative { get; set; } = 3;
    public int Competence { get; set; } = 3;
    public int Composure { get; set; } = 3;
}

/// <summary>An order a side's HQ sends at a set time.</summary>
public sealed class ScheduledOrderDef
{
    public float Time { get; set; }
    /// <summary>The squad, or the company, the order is for.</summary>
    public string Group { get; set; } = "";
    /// <summary>"Hold", "Advance", "Attack", "Suppress", "Halt" or "FallBack".</summary>
    public string Order { get; set; } = "Hold";
    public float[]? Objective { get; set; }
    public float ObjectiveRadius { get; set; } = 15f;
    public float[]? SupportPosition { get; set; }
}

/// <summary>A side's headquarters: where orders come from and reports go to.</summary>
public sealed class HqDef
{
    public string Name { get; set; } = "HQ";
    public float[] Position { get; set; } = new float[2];
    /// <summary>A radio set at HQ (WW2: company HQ and above).</summary>
    public bool Radio { get; set; } = true;
}

/// <summary>A field telephone line laid from HQ. Its far end is where a squad can pick up the phone.</summary>
public sealed class PhoneLineDef
{
    /// <summary>The route of the cable after HQ, ending at the phone.</summary>
    public float[][] Points { get; set; } = Array.Empty<float[]>();
}

public sealed class SideDef
{
    public string Side { get; set; } = "Blue";
    public string Name { get; set; } = "";
    /// <summary>Direction this side runs to when broken, e.g. [-1, 0] for west.</summary>
    public float[] Rear { get; set; } = { -1f, 0f };
    public List<GroupDef> Groups { get; set; } = new();
    /// <summary>With an HQ, orders and reports travel by phone, radio, runner or flare and take time.
    /// Without one (older scenarios), everything is instant.</summary>
    public HqDef? Hq { get; set; }
    public List<PhoneLineDef> PhoneLines { get; set; } = new();
}

public sealed class GroupDef
{
    public string Name { get; set; } = "";
    /// <summary>"Hold", "Advance", "Attack" or "Suppress". See GroupOrder.</summary>
    public string Order { get; set; } = "Hold";
    /// <summary>For "Suppress": where to set up and fire from.</summary>
    public float[]? SupportPosition { get; set; }
    public float[] Objective { get; set; } = new float[2];
    public float ObjectiveRadius { get; set; } = 15f;
    /// <summary>Where the enemy is expected, used before anyone is spotted.</summary>
    public float[] Facing { get; set; } = { 1f, 0f };
    public float[] SpawnCenter { get; set; } = new float[2];
    public float[] SpawnSize { get; set; } = { 10f, 20f };
    /// <summary>Weapon id to count, e.g. { "rifle": 9, "lmg": 1 }.</summary>
    public Dictionary<string, int> Weapons { get; set; } = new();
    /// <summary>Vehicle id to count, e.g. { "tank": 1 }. A group with vehicles is a vehicle section, not a squad.</summary>
    public Dictionary<string, int> Vehicles { get; set; } = new();
    public float MoraleBonus { get; set; }
    /// <summary>The squad carries a radio set (rare for a WW2 squad).</summary>
    public bool Radio { get; set; }
}

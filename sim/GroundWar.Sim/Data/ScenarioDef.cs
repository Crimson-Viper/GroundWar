namespace GroundWar.Sim.Data;

/// <summary>A battle setup from data/scenarios/*.json: which map, who fights, and their orders.</summary>
public sealed class ScenarioDef
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Map { get; set; } = "";
    public float TimeLimit { get; set; } = 600f;
    public List<SideDef> Sides { get; set; } = new();
}

public sealed class SideDef
{
    public string Side { get; set; } = "Blue";
    public string Name { get; set; } = "";
    /// <summary>Direction this side runs to when broken, e.g. [-1, 0] for west.</summary>
    public float[] Rear { get; set; } = { -1f, 0f };
    public List<GroupDef> Groups { get; set; } = new();
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
    public float MoraleBonus { get; set; }
}

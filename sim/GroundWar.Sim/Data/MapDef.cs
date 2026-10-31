namespace GroundWar.Sim.Data;

/// <summary>A battlefield layout from data/maps/*.json. Positions are [x, y] in metres.</summary>
public sealed class MapDef
{
    public string Name { get; set; } = "";
    public float Width { get; set; } = 300f;
    public float Height { get; set; } = 200f;
    public List<WallDef> Walls { get; set; } = new();
    public List<CraterDef> Craters { get; set; } = new();
}

public sealed class WallDef
{
    public float[] A { get; set; } = new float[2];
    public float[] B { get; set; } = new float[2];
    public float Thickness { get; set; } = 0.6f;
    /// <summary>High walls block sight and bullets. Low walls give cover you can shoot over.</summary>
    public bool High { get; set; }
}

public sealed class CraterDef
{
    public float[] Center { get; set; } = new float[2];
    public float Radius { get; set; } = 3f;
}

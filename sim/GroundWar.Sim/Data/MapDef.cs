namespace GroundWar.Sim.Data;

/// <summary>A battlefield layout from data/maps/*.json. Positions are [x, y] in metres.</summary>
public sealed class MapDef
{
    public string Name { get; set; } = "";
    public float Width { get; set; } = 300f;
    public float Height { get; set; } = 200f;
    public List<WallDef> Walls { get; set; } = new();
    public List<CraterDef> Craters { get; set; } = new();
    /// <summary>Round hills (or hollows, with a negative height). Heights add up where they overlap.</summary>
    public List<HillDef> Hills { get; set; } = new();
    /// <summary>Long ridges (or valleys, with a negative height) along a line.</summary>
    public List<RidgeDef> Ridges { get; set; } = new();
    /// <summary>Patches of woods or standing crops, each an outline of points.</summary>
    public List<AreaDef> Areas { get; set; } = new();
    /// <summary>Single trees outside the woods.</summary>
    public List<float[]> Trees { get; set; } = new();
}

public sealed class HillDef
{
    public float[] Center { get; set; } = new float[2];
    /// <summary>Distance from the middle at which the hill has run out to nothing.</summary>
    public float Radius { get; set; } = 60f;
    public float Height { get; set; } = 5f;
}

public sealed class RidgeDef
{
    public float[] A { get; set; } = new float[2];
    public float[] B { get; set; } = new float[2];
    /// <summary>Distance from the ridge line at which it has run out to nothing.</summary>
    public float Width { get; set; } = 50f;
    public float Height { get; set; } = 5f;
}

public sealed class AreaDef
{
    /// <summary>"woods" or "crops".</summary>
    public string Kind { get; set; } = "woods";
    public List<float[]> Points { get; set; } = new();
}

public sealed class WallDef
{
    public float[] A { get; set; } = new float[2];
    public float[] B { get; set; } = new float[2];
    public float Thickness { get; set; } = 0.6f;
    /// <summary>High walls block sight and bullets. Low walls give cover you can shoot over.</summary>
    public bool High { get; set; }
    /// <summary>A thick hedgerow: hides what is behind it unless you are right up against it, slows a man pushing
    /// through, stops a tank, and gives some protection (it grows on an earth bank).</summary>
    public bool Hedge { get; set; }
}

public sealed class CraterDef
{
    public float[] Center { get; set; } = new float[2];
    public float Radius { get; set; } = 3f;
}

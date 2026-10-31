using System.Numerics;

namespace GroundWar.Sim.Map;

public sealed class Wall
{
    public int Id { get; init; }
    public Vector2 A { get; init; }
    public Vector2 B { get; init; }
    public float Thickness { get; init; }
    public bool High { get; init; }
    public float Length => Vector2.Distance(A, B);
    public Vector2 Direction => Vector2.Normalize(B - A);
    public float HeightMetres => High ? 2.6f : 1.0f;
}

public sealed class Crater
{
    public int Id { get; init; }
    public Vector2 Center { get; init; }
    public float Radius { get; init; }
}

public enum CoverKind { LowWall, HighWall, Crater }

/// <summary>A spot a soldier can take cover at. Generated from the map's walls and craters.</summary>
public sealed class CoverPoint
{
    public int Id { get; init; }
    public Vector2 Position { get; init; }
    public CoverKind Kind { get; init; }
    /// <summary>Direction the cover protects against (points from the soldier towards the wall). Zero for craters (all round).</summary>
    public Vector2 Facing { get; init; }
    public float Strength { get; init; }
    /// <summary>False for the middle of a high wall: safe, but you cannot see or shoot from it.</summary>
    public bool CanFire { get; init; }
    public int ClaimedBy { get; set; } = -1;
}

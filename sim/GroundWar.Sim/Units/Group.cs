using System.Numerics;

namespace GroundWar.Sim.Units;

public enum GroupOrder { Hold, Advance }

/// <summary>A body of men with one order. For milestone 2 this is a simple squad; real squad tactics come in milestone 3.</summary>
public sealed class Group
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public string Name { get; init; } = "";
    public GroupOrder Order { get; set; }
    public Vector2 Objective { get; set; }
    public float ObjectiveRadius { get; set; } = 15f;
    /// <summary>Where the enemy is expected before anyone is spotted.</summary>
    public Vector2 Facing { get; set; }
    public Vector2 Rear { get; set; }
    public List<Soldier> Members { get; } = new();
    public Soldier? Leader { get; set; }
    public bool HasBroken { get; set; }

    public int CountActive => Members.Count(m => m.IsActive);
    public int CountEffective => Members.Count(m => m.IsEffective);
}

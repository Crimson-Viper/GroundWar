using System.Numerics;

namespace GroundWar.Sim.Units;

/// <summary>Soldiers bucketed into 20 m squares per side, rebuilt every tick, so "who is near me" doesn't
/// have to check all 2,000 men.</summary>
public sealed class SoldierHash
{
    public const float CellSize = 20f;
    private readonly int _w, _h;
    private readonly List<Soldier>[][] _cells; // [side][cell]
    private static (int dx, int dy, float minDist)[]? _rings;

    public SoldierHash(float width, float height)
    {
        _w = (int)MathF.Ceiling(width / CellSize);
        _h = (int)MathF.Ceiling(height / CellSize);
        _cells = new List<Soldier>[2][];
        for (int s = 0; s < 2; s++)
        {
            _cells[s] = new List<Soldier>[_w * _h];
            for (int i = 0; i < _cells[s].Length; i++) _cells[s][i] = new List<Soldier>();
        }
    }

    public void Rebuild(IReadOnlyList<Soldier> soldiers)
    {
        foreach (var side in _cells) foreach (var c in side) c.Clear();
        foreach (var s in soldiers)
        {
            if (!s.IsActive) continue;
            _cells[(int)s.Side][Index(s.Position)].Add(s);
        }
    }

    private int Index(Vector2 p)
    {
        int x = Math.Clamp((int)(p.X / CellSize), 0, _w - 1), y = Math.Clamp((int)(p.Y / CellSize), 0, _h - 1);
        return y * _w + x;
    }

    /// <summary>Active soldiers of a side within radius, roughly nearest first (ring by ring).</summary>
    public IEnumerable<Soldier> Near(Side side, Vector2 p, float radius)
    {
        var rings = Rings();
        int cx = Math.Clamp((int)(p.X / CellSize), 0, _w - 1), cy = Math.Clamp((int)(p.Y / CellSize), 0, _h - 1);
        float r2 = radius * radius;
        var cells = _cells[(int)side];
        foreach (var (dx, dy, minDist) in rings)
        {
            if (minDist > radius) yield break;
            int x = cx + dx, y = cy + dy;
            if (x < 0 || y < 0 || x >= _w || y >= _h) continue;
            foreach (var s in cells[y * _w + x])
                if (Vector2.DistanceSquared(s.Position, p) <= r2) yield return s;
        }
    }

    public bool AnyNear(Side side, Vector2 p, float radius, Soldier? except)
    {
        foreach (var s in Near(side, p, radius)) if (s != except) return true;
        return false;
    }

    private static (int, int, float)[] Rings()
    {
        if (_rings != null) return _rings;
        const int max = 40; // 800 m
        var list = new List<(int, int, float)>();
        for (int dy = -max; dy <= max; dy++)
        for (int dx = -max; dx <= max; dx++)
        {
            // closest possible distance between a point in the centre cell and a point in this cell
            float mx = MathF.Max(0, MathF.Abs(dx) - 1) * CellSize, my = MathF.Max(0, MathF.Abs(dy) - 1) * CellSize;
            list.Add((dx, dy, MathF.Sqrt(mx * mx + my * my)));
        }
        list.Sort((a, b) => a.Item3.CompareTo(b.Item3));
        return _rings = list.ToArray();
    }
}

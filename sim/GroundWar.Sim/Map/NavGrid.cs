using System.Numerics;

namespace GroundWar.Sim.Map;

/// <summary>1 m walkability grid with A* pathfinding. Walls block movement; craters slow it (handled by the mover).</summary>
public sealed class NavGrid
{
    public const float CellSize = 1f;
    public int Width { get; }
    public int Height { get; }
    private readonly bool[] _blocked;
    private readonly float[] _g;
    private readonly int[] _parent;
    private readonly int[] _stamp;
    private int _currentStamp;
    private readonly PriorityQueue<int, float> _open = new();

    public int MaxExpansions { get; set; } = 40000;

    public NavGrid(float worldWidth, float worldHeight, IEnumerable<Wall> walls, float agentRadius)
    {
        Width = (int)MathF.Ceiling(worldWidth / CellSize);
        Height = (int)MathF.Ceiling(worldHeight / CellSize);
        int n = Width * Height;
        _blocked = new bool[n];
        _g = new float[n];
        _parent = new int[n];
        _stamp = new int[n];
        foreach (var w in walls) Rasterize(w, agentRadius);
    }

    private void Rasterize(Wall w, float agentRadius)
    {
        float r = w.Thickness * 0.5f + agentRadius;
        int x0 = Math.Max(0, (int)((MathF.Min(w.A.X, w.B.X) - r) / CellSize) - 1);
        int x1 = Math.Min(Width - 1, (int)((MathF.Max(w.A.X, w.B.X) + r) / CellSize) + 1);
        int y0 = Math.Max(0, (int)((MathF.Min(w.A.Y, w.B.Y) - r) / CellSize) - 1);
        int y1 = Math.Min(Height - 1, (int)((MathF.Max(w.A.Y, w.B.Y) + r) / CellSize) + 1);
        float r2 = r * r;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            if (Core.Geo.PointSegmentDistSq(CellCenter(x, y), w.A, w.B, out _) < r2)
                _blocked[y * Width + x] = true;
        }
    }

    public Vector2 CellCenter(int x, int y) => new((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public bool IsBlocked(int x, int y) => !InBounds(x, y) || _blocked[y * Width + x];

    public bool IsWalkable(Vector2 p) => !IsBlocked((int)(p.X / CellSize), (int)(p.Y / CellSize));

    /// <summary>Nearest walkable spot to p (searches up to maxRadius metres). Returns p if already walkable.</summary>
    public Vector2 NearestWalkable(Vector2 p, float maxRadius = 6f)
    {
        int cx = (int)(p.X / CellSize), cy = (int)(p.Y / CellSize);
        if (!IsBlocked(cx, cy)) return p;
        int maxR = (int)MathF.Ceiling(maxRadius / CellSize);
        Vector2 best = p;
        float bestD = float.MaxValue;
        for (int r = 1; r <= maxR; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                if (IsBlocked(cx + dx, cy + dy)) continue;
                var c = CellCenter(cx + dx, cy + dy);
                float d = Vector2.DistanceSquared(c, p);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (bestD < float.MaxValue) return best;
        }
        return p;
    }

    /// <summary>True if a soldier can walk in a straight line from a to b.</summary>
    public bool StraightLineClear(Vector2 a, Vector2 b)
    {
        float dist = Vector2.Distance(a, b);
        int steps = Math.Max(1, (int)(dist / (CellSize * 0.4f)));
        for (int i = 0; i <= steps; i++)
        {
            if (!IsWalkable(Vector2.Lerp(a, b, i / (float)steps))) return false;
        }
        return true;
    }

    /// <summary>A* path from start to goal as a list of waypoints (smoothed). Null if unreachable.</summary>
    public List<Vector2>? FindPath(Vector2 start, Vector2 goal)
    {
        start = NearestWalkable(start);
        goal = NearestWalkable(goal);
        if (StraightLineClear(start, goal)) return new List<Vector2> { goal };

        int sx = (int)(start.X / CellSize), sy = (int)(start.Y / CellSize);
        int gx = (int)(goal.X / CellSize), gy = (int)(goal.Y / CellSize);
        if (IsBlocked(sx, sy) || IsBlocked(gx, gy)) return null;

        _currentStamp++;
        _open.Clear();
        int startIdx = sy * Width + sx, goalIdx = gy * Width + gx;
        Visit(startIdx, 0f, -1);
        _open.Enqueue(startIdx, Heuristic(sx, sy, gx, gy));
        int expansions = 0;
        bool found = false;
        while (_open.TryDequeue(out int idx, out float f))
        {
            if (idx == goalIdx) { found = true; break; }
            int x = idx % Width, y = idx / Width;
            float g = _g[idx];
            if (f - Heuristic(x, y, gx, gy) > g + 0.001f) continue; // stale queue entry
            if (++expansions > MaxExpansions) break;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (IsBlocked(nx, ny)) continue;
                if (dx != 0 && dy != 0 && (IsBlocked(x + dx, y) || IsBlocked(x, y + dy))) continue; // no corner cutting
                int nIdx = ny * Width + nx;
                float ng = g + (dx != 0 && dy != 0 ? 1.41421f : 1f);
                if (_stamp[nIdx] == _currentStamp && _g[nIdx] <= ng) continue;
                Visit(nIdx, ng, idx);
                _open.Enqueue(nIdx, ng + Heuristic(nx, ny, gx, gy));
            }
        }
        if (!found) return null;

        var cells = new List<Vector2>();
        for (int i = goalIdx; i != -1; i = _parent[i]) cells.Add(CellCenter(i % Width, i / Width));
        cells.Reverse();
        cells[^1] = goal;
        return Smooth(start, cells);
    }

    private void Visit(int idx, float g, int parent)
    {
        _stamp[idx] = _currentStamp;
        _g[idx] = g;
        _parent[idx] = parent;
    }

    private static float Heuristic(int x, int y, int gx, int gy)
    {
        int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return (dx + dy) + (1.41421f - 2f) * Math.Min(dx, dy);
    }

    private List<Vector2> Smooth(Vector2 start, List<Vector2> cells)
    {
        var result = new List<Vector2>();
        Vector2 from = start;
        int i = 0;
        while (i < cells.Count)
        {
            int furthest = i;
            for (int j = cells.Count - 1; j > i; j--)
            {
                if (StraightLineClear(from, cells[j])) { furthest = j; break; }
            }
            result.Add(cells[furthest]);
            from = cells[furthest];
            i = furthest + 1;
        }
        return result;
    }
}

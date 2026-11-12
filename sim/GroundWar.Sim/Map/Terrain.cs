using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Data;

namespace GroundWar.Sim.Map;

/// <summary>What grows on a square of ground.</summary>
public enum GroundKind : byte { Open, Woods, Crops }

/// <summary>A single tree: something to get behind.</summary>
public readonly record struct Tree(Vector2 Position, float Radius, float HeightMetres);

/// <summary>
/// The shape of the ground and what grows on it: height (hills, ridges, hollows), woods and standing crops.
/// Kept as a grid of 4 m squares built once from the map file. A crest hides what is behind it from eyes and
/// bullets alike; woods hide men from view after a few tens of metres but don't stop a bullet.
/// See docs/systems/terrain.md.
/// </summary>
public sealed class Terrain
{
    public const float CellSize = 4f;
    /// <summary>Height of a man's eyes (and of the top of the man being looked at) above the ground.</summary>
    public const float EyeHeight = 1.5f;

    public int Width { get; }
    public int Height { get; }
    /// <summary>True if the whole map is level (then none of the height sums are done).</summary>
    public bool Flat { get; }
    public bool HasWoods { get; }
    public bool HasCrops { get; }
    public float MinHeight { get; }
    public float MaxHeight { get; }
    public IReadOnlyList<Tree> Trees => _trees;

    private readonly float[] _h;          // (Width+1) x (Height+1) corner heights
    private readonly GroundKind[] _kind;  // Width x Height
    private readonly List<Tree> _trees = new();
    private readonly List<int>[] _treeBuckets;
    private readonly float _mapW, _mapH;
    private readonly TerrainRules _r;

    public Terrain(MapDef def, TerrainRules rules)
    {
        _r = rules;
        _mapW = def.Width; _mapH = def.Height;
        Width = Math.Max(1, (int)MathF.Ceiling(def.Width / CellSize));
        Height = Math.Max(1, (int)MathF.Ceiling(def.Height / CellSize));
        _h = new float[(Width + 1) * (Height + 1)];
        _kind = new GroundKind[Width * Height];
        _treeBuckets = new List<int>[Width * Height];

        float min = 0f, max = 0f;
        if (def.Hills.Count > 0 || def.Ridges.Count > 0)
        {
            for (int y = 0; y <= Height; y++)
            for (int x = 0; x <= Width; x++)
            {
                var p = new Vector2(x * CellSize, y * CellSize);
                float h = 0f;
                foreach (var hill in def.Hills)
                    h += Bump(Vector2.Distance(p, BattleMap.V(hill.Center)), hill.Radius) * hill.Height;
                foreach (var r in def.Ridges)
                    h += Bump(MathF.Sqrt(Geo.PointSegmentDistSq(p, BattleMap.V(r.A), BattleMap.V(r.B), out _)), r.Width) * r.Height;
                _h[y * (Width + 1) + x] = h;
                min = MathF.Min(min, h); max = MathF.Max(max, h);
            }
        }
        MinHeight = min; MaxHeight = max;
        Flat = max - min < 0.05f;

        foreach (var area in def.Areas)
        {
            var kind = area.Kind.ToLowerInvariant() switch
            {
                "woods" or "wood" or "forest" => GroundKind.Woods,
                "crops" or "wheat" or "grass" => GroundKind.Crops,
                _ => GroundKind.Open,
            };
            if (kind == GroundKind.Open || area.Points.Count < 3) continue;
            var pts = area.Points.Select(BattleMap.V).ToArray();
            float x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
            for (int y = Math.Max(0, (int)(y0 / CellSize)); y <= Math.Min(Height - 1, (int)(y1 / CellSize)); y++)
            for (int x = Math.Max(0, (int)(x0 / CellSize)); x <= Math.Min(Width - 1, (int)(x1 / CellSize)); x++)
                if (InPolygon(new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize), pts))
                    _kind[y * Width + x] = kind;
        }

        // Trees: one or two to each square of woods, always in the same places for the same map
        // (the sim and the picture must agree, and a replay must find them where they were).
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            var k = _kind[y * Width + x];
            if (k == GroundKind.Woods) HasWoods = true;
            if (k == GroundKind.Crops) HasCrops = true;
            if (k != GroundKind.Woods) continue;
            uint seed = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            int n = 1 + (int)(Hash01(seed) * 2f);
            for (int i = 0; i < n; i++)
            {
                var p = new Vector2((x + 0.15f + 0.7f * Hash01(seed + 11u * (uint)i + 1u)) * CellSize,
                                    (y + 0.15f + 0.7f * Hash01(seed + 17u * (uint)i + 2u)) * CellSize);
                AddTree(p, 0.25f + 0.2f * Hash01(seed + 5u * (uint)i + 3u), 7f + 5f * Hash01(seed + 7u * (uint)i + 4u));
            }
        }
        foreach (var t in def.Trees) AddTree(BattleMap.V(t), 0.4f, 9f);
    }

    private void AddTree(Vector2 p, float radius, float height)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= _mapW || p.Y >= _mapH) return;
        int idx = CellIndex(p);
        (_treeBuckets[idx] ??= new List<int>()).Add(_trees.Count);
        _trees.Add(new Tree(p, radius, height));
    }

    /// <summary>A smooth mound: 1 in the middle, 0 at the edge and beyond.</summary>
    private static float Bump(float d, float radius) =>
        d >= radius || radius <= 0f ? 0f : 0.5f * (1f + MathF.Cos(MathF.PI * d / radius));

    private static float Hash01(uint v)
    {
        v ^= v >> 16; v *= 0x7feb352du; v ^= v >> 15; v *= 0x846ca68bu; v ^= v >> 16;
        return (v & 0xFFFFFF) / (float)0x1000000;
    }

    private static bool InPolygon(Vector2 p, Vector2[] pts)
    {
        bool inside = false;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
            if ((pts[i].Y > p.Y) != (pts[j].Y > p.Y) &&
                p.X < (pts[j].X - pts[i].X) * (p.Y - pts[i].Y) / (pts[j].Y - pts[i].Y) + pts[i].X)
                inside = !inside;
        return inside;
    }

    private int CellIndex(Vector2 p) =>
        Math.Clamp((int)(p.Y / CellSize), 0, Height - 1) * Width + Math.Clamp((int)(p.X / CellSize), 0, Width - 1);

    /// <summary>Height of the ground in metres at a spot (0 on a flat map).</summary>
    public float HeightAt(Vector2 p)
    {
        if (Flat) return 0f;
        float fx = Math.Clamp(p.X / CellSize, 0f, Width - 0.001f), fy = Math.Clamp(p.Y / CellSize, 0f, Height - 0.001f);
        int x = (int)fx, y = (int)fy;
        float tx = fx - x, ty = fy - y;
        int i = y * (Width + 1) + x;
        float top = _h[i] + (_h[i + 1] - _h[i]) * tx;
        float bottom = _h[i + Width + 1] + (_h[i + Width + 2] - _h[i + Width + 1]) * tx;
        return top + (bottom - top) * ty;
    }

    public GroundKind KindAt(Vector2 p) => _kind[CellIndex(p)];

    /// <summary>How steeply the ground climbs going from a towards b (rise over run; negative downhill).</summary>
    public float SlopeAlong(Vector2 at, Vector2 dir)
    {
        if (Flat) return 0f;
        return (HeightAt(at + dir * 2f) - HeightAt(at - dir * 2f)) / 4f;
    }

    /// <summary>True if the ground itself (a crest, a hill) stands between a and b.</summary>
    public bool GroundBlocks(Vector2 a, Vector2 b) => !Flat && Blocks(a, b, false);

    /// <summary>True if the ground or too much woodland stands between a and b.</summary>
    public bool SightBlocked(Vector2 a, Vector2 b) => (!Flat || HasWoods) && Blocks(a, b, true);

    private bool Blocks(Vector2 a, Vector2 b, bool woods)
    {
        float dist = Vector2.Distance(a, b);
        if (dist < CellSize) return false;
        int n = (int)MathF.Ceiling(dist / CellSize);
        float step = dist / n;
        float za = HeightAt(a) + EyeHeight, zb = HeightAt(b) + EyeHeight;
        float through = 0f;
        woods &= HasWoods;
        for (int i = 1; i < n; i++)
        {
            float t = i / (float)n;
            var p = Vector2.Lerp(a, b, t);
            float line = za + (zb - za) * t;
            float ground = Flat ? 0f : HeightAt(p);
            if (ground > line) return true;
            if (woods && _kind[CellIndex(p)] == GroundKind.Woods && line < ground + _r.TreeHeight)
            {
                through += step;
                if (through > _r.WoodsSightDepth) return true;
            }
        }
        return false;
    }

    /// <summary>Is there a tree trunk close in front of a man here, between him and where the fire comes from?</summary>
    public bool BehindTree(Vector2 pos, Vector2 from)
    {
        if (_trees.Count == 0) return false;
        var toFire = Geo.SafeNormalize(from - pos, Vector2.UnitX);
        int cx = Math.Clamp((int)(pos.X / CellSize), 0, Width - 1), cy = Math.Clamp((int)(pos.Y / CellSize), 0, Height - 1);
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(Height - 1, cy + 1); y++)
        for (int x = Math.Max(0, cx - 1); x <= Math.Min(Width - 1, cx + 1); x++)
        {
            var bucket = _treeBuckets[y * Width + x];
            if (bucket == null) continue;
            foreach (int ti in bucket)
            {
                var d = _trees[ti].Position - pos;
                float len = d.Length();
                if (len > _r.TreeCoverDistance) continue;
                if (len < 0.05f || Vector2.Dot(d / len, toFire) > 0.7f) return true;
            }
        }
        return false;
    }

    /// <summary>Trees within a few metres of a spot (for placing cover points).</summary>
    public IEnumerable<Tree> TreesIn(int cellX, int cellY)
    {
        var bucket = _treeBuckets[cellY * Width + cellX];
        if (bucket == null) yield break;
        foreach (int ti in bucket) yield return _trees[ti];
    }
}

using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Data;

namespace GroundWar.Sim.Map;

/// <summary>The ground: walls, craters, generated cover points, sight lines and walkability.</summary>
public sealed class BattleMap
{
    public const float BucketSize = 10f;

    public string Name { get; }
    public float Width { get; }
    public float Height { get; }
    public IReadOnlyList<Wall> Walls => _walls;
    public IReadOnlyList<Crater> Craters => _craters;
    public IReadOnlyList<CoverPoint> CoverPoints => _cover;
    public NavGrid Nav { get; }
    private NavGrid? _tankNav;
    /// <summary>Where a tank fits: walls get a wide margin, so tanks keep to streets and open ground.
    /// Built the first time a tank needs it.</summary>
    public NavGrid TankNav => _tankNav ??= new NavGrid(Width, Height, _walls, 1.8f);

    private readonly List<Wall> _walls = new();
    private readonly List<Wall> _highWalls = new();
    private readonly List<Crater> _craters = new();
    private readonly List<CoverPoint> _cover = new();
    private readonly int _bucketsX, _bucketsY;
    private readonly List<CoverPoint>[] _coverBuckets;
    private readonly List<Wall>[] _wallBuckets;
    private readonly List<Crater>[] _craterBuckets;
    private readonly List<Wall>[] _highWallBuckets;
    private readonly int[] _wallStamp;
    private int _sightStamp;
    private readonly CombatRules _combat;

    public BattleMap(MapDef def, CombatRules combat)
    {
        _combat = combat;
        Name = def.Name;
        Width = def.Width;
        Height = def.Height;
        foreach (var w in def.Walls)
        {
            var wall = new Wall { Id = _walls.Count, A = V(w.A), B = V(w.B), Thickness = w.Thickness, High = w.High };
            _walls.Add(wall);
            if (wall.High) _highWalls.Add(wall);
        }
        foreach (var c in def.Craters)
            _craters.Add(new Crater { Id = _craters.Count, Center = V(c.Center), Radius = c.Radius });

        Nav = new NavGrid(Width, Height, _walls, 0.3f);

        _bucketsX = (int)MathF.Ceiling(Width / BucketSize);
        _bucketsY = (int)MathF.Ceiling(Height / BucketSize);
        _coverBuckets = NewBuckets<CoverPoint>();
        _wallBuckets = NewBuckets<Wall>();
        _craterBuckets = NewBuckets<Crater>();
        _highWallBuckets = NewBuckets<Wall>();
        _wallStamp = new int[_walls.Count];
        foreach (var w in _highWalls)
            AddToBuckets(_highWallBuckets, w, MathF.Min(w.A.X, w.B.X) - 1, MathF.Min(w.A.Y, w.B.Y) - 1,
                MathF.Max(w.A.X, w.B.X) + 1, MathF.Max(w.A.Y, w.B.Y) + 1);
        foreach (var w in _walls)
        {
            float pad = w.Thickness + combat.CoverCheckDistance;
            AddToBuckets(_wallBuckets, w, MathF.Min(w.A.X, w.B.X) - pad, MathF.Min(w.A.Y, w.B.Y) - pad,
                MathF.Max(w.A.X, w.B.X) + pad, MathF.Max(w.A.Y, w.B.Y) + pad);
        }
        foreach (var c in _craters)
            AddToBuckets(_craterBuckets, c, c.Center.X - c.Radius, c.Center.Y - c.Radius, c.Center.X + c.Radius, c.Center.Y + c.Radius);

        GenerateCover();
    }

    public static Vector2 V(float[] a) => a.Length >= 2 ? new Vector2(a[0], a[1]) : Vector2.Zero;

    public bool InBounds(Vector2 p) => p.X >= 0 && p.Y >= 0 && p.X <= Width && p.Y <= Height;

    public Vector2 Clamp(Vector2 p, float margin = 1f) =>
        new(Math.Clamp(p.X, margin, Width - margin), Math.Clamp(p.Y, margin, Height - margin));

    // ---------------------------------------------------------------- sight and cover

    /// <summary>True if a high wall or a smoke screen blocks the view between a and b.</summary>
    public bool SightBlocked(Vector2 a, Vector2 b) => (_smoke.Count > 0 && SmokeBlocks(a, b)) || WallBlocked(a, b);

    /// <summary>True if a high wall stands between a and b (smoke hides, but doesn't stop a bullet or a blast).</summary>
    public bool WallBlocked(Vector2 a, Vector2 b)
    {
        if (_highWalls.Count <= 8)
        {
            foreach (var w in _highWalls)
                if (WallBlocks(w, a, b)) return true;
            return false;
        }
        // Walk the 10 m squares the line passes through (Amanatides & Woo) and only test the high walls in them.
        _sightStamp++;
        int x = Math.Clamp((int)(a.X / BucketSize), 0, _bucketsX - 1), y = Math.Clamp((int)(a.Y / BucketSize), 0, _bucketsY - 1);
        int ex = Math.Clamp((int)(b.X / BucketSize), 0, _bucketsX - 1), ey = Math.Clamp((int)(b.Y / BucketSize), 0, _bucketsY - 1);
        Vector2 d = b - a;
        int stepX = d.X > 0 ? 1 : -1, stepY = d.Y > 0 ? 1 : -1;
        float tDeltaX = d.X != 0 ? MathF.Abs(BucketSize / d.X) : float.MaxValue;
        float tDeltaY = d.Y != 0 ? MathF.Abs(BucketSize / d.Y) : float.MaxValue;
        float nextX = (stepX > 0 ? (x + 1) * BucketSize : x * BucketSize);
        float nextY = (stepY > 0 ? (y + 1) * BucketSize : y * BucketSize);
        float tMaxX = d.X != 0 ? (nextX - a.X) / d.X : float.MaxValue;
        float tMaxY = d.Y != 0 ? (nextY - a.Y) / d.Y : float.MaxValue;
        for (int guard = 0; guard < 4 * (_bucketsX + _bucketsY); guard++)
        {
            foreach (var w in _highWallBuckets[y * _bucketsX + x])
            {
                if (_wallStamp[w.Id] == _sightStamp) continue;
                _wallStamp[w.Id] = _sightStamp;
                if (WallBlocks(w, a, b)) return true;
            }
            if (x == ex && y == ey) break;
            if (tMaxX < tMaxY) { tMaxX += tDeltaX; x += stepX; }
            else { tMaxY += tDeltaY; y += stepY; }
            if (x < 0 || y < 0 || x >= _bucketsX || y >= _bucketsY) break;
        }
        return false;
    }

    private static bool WallBlocks(Wall w, Vector2 a, Vector2 b)
    {
        float r = w.Thickness * 0.5f;
        return Geo.SegmentSegmentDistSq(a, b, w.A, w.B, out _, out _) < r * r;
    }

    public bool HasLineOfSight(Vector2 a, Vector2 b) => !SightBlocked(a, b);

    /// <summary>How well a man at <paramref name="pos"/> is protected from fire coming from <paramref name="from"/>.
    /// 0 = in the open, 1 = completely hidden behind a high wall.</summary>
    public float ProtectionAt(Vector2 pos, Vector2 from)
    {
        if (WallBlocked(from, pos)) return 1f;
        float prot = 0f;
        var wb = BucketAt(_wallBuckets, pos);
        if (wb != null)
        {
            float check2 = _combat.CoverCheckDistance * _combat.CoverCheckDistance;
            foreach (var w in wb)
            {
                if (w.High) continue;
                float half = w.Thickness * 0.5f;
                float d2 = Geo.PointSegmentDistSq(pos, w.A, w.B, out _);
                float reach = _combat.CoverCheckDistance + half;
                if (d2 > reach * reach) continue;
                // Does the line of fire actually pass through this wall close to the target?
                if (Geo.SegmentSegmentDistSq(from, pos, w.A, w.B, out float s, out _) < (half + 0.05f) * (half + 0.05f))
                {
                    float distFromTarget = (1f - s) * Vector2.Distance(from, pos);
                    if (distFromTarget * distFromTarget <= check2 + half * half)
                        prot = MathF.Max(prot, _combat.LowWallProtection);
                }
            }
        }
        var cb = BucketAt(_craterBuckets, pos);
        if (cb != null)
        {
            foreach (var c in cb)
            {
                if (Vector2.DistanceSquared(pos, c.Center) < c.Radius * c.Radius &&
                    Vector2.Distance(from, c.Center) > c.Radius + 2f)
                    prot = MathF.Max(prot, c.Foxhole ? _combat.FoxholeProtection : _combat.CraterProtection);
            }
        }
        return prot;
    }

    /// <summary>A soldier has finished digging a foxhole here: a small hole that protects all round,
    /// better than a shell crater. Returns its cover point.</summary>
    public CoverPoint AddFoxhole(Vector2 pos)
    {
        var c = new Crater { Id = _craters.Count, Center = pos, Radius = 0.9f, Foxhole = true };
        _craters.Add(c);
        AddToBuckets(_craterBuckets, c, pos.X - c.Radius, pos.Y - c.Radius, pos.X + c.Radius, pos.Y + c.Radius);
        var cp = new CoverPoint { Id = _cover.Count, Position = pos, Kind = CoverKind.Foxhole, Facing = Vector2.Zero,
            Strength = _combat.FoxholeProtection, CanFire = true };
        _cover.Add(cp);
        BucketAt(_coverBuckets, pos)?.Add(cp);
        return cp;
    }

    public bool InCrater(Vector2 pos)
    {
        var cb = BucketAt(_craterBuckets, pos);
        if (cb == null) return false;
        foreach (var c in cb)
            if (Vector2.DistanceSquared(pos, c.Center) < c.Radius * c.Radius) return true;
        return false;
    }

    /// <summary>Cover points within radius of p.</summary>
    public IEnumerable<CoverPoint> CoverNear(Vector2 p, float radius)
    {
        int x0 = Math.Max(0, (int)((p.X - radius) / BucketSize)), x1 = Math.Min(_bucketsX - 1, (int)((p.X + radius) / BucketSize));
        int y0 = Math.Max(0, (int)((p.Y - radius) / BucketSize)), y1 = Math.Min(_bucketsY - 1, (int)((p.Y + radius) / BucketSize));
        float r2 = radius * radius;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            foreach (var c in _coverBuckets[y * _bucketsX + x])
                if (Vector2.DistanceSquared(c.Position, p) <= r2) yield return c;
        }
    }

    // ---------------------------------------------------------------- cover generation

    private void GenerateCover()
    {
        const float spacing = 2f;
        foreach (var w in _walls)
        {
            Vector2 dir = w.Direction, normal = Geo.Perp(dir);
            float len = w.Length;
            int count = Math.Max(1, (int)(len / spacing));
            float offset = w.Thickness * 0.5f + 0.6f;
            for (int i = 0; i <= count; i++)
            {
                float along = count == 0 ? len * 0.5f : len * i / count;
                Vector2 basePos = w.A + dir * along;
                bool nearEnd = along < 1.5f || along > len - 1.5f;
                foreach (float side in new[] { 1f, -1f })
                {
                    // Step out from the wall until the spot is walkable (the nav grid is 1 m squares).
                    Vector2 pos = basePos + normal * (offset * side);
                    for (int n = 0; n < 4 && !Nav.IsWalkable(pos); n++) pos += normal * (0.3f * side);
                    if (w.High)
                    {
                        // Middle of a high wall: a safe hiding spot. Ends: you can lean out and fire.
                        AddCover(pos, CoverKind.HighWall, -normal * side, nearEnd ? 0.85f : 0.95f, nearEnd);
                    }
                    else
                    {
                        AddCover(pos, CoverKind.LowWall, -normal * side, _combat.LowWallProtection, true);
                    }
                }
            }
        }
        foreach (var c in _craters)
        {
            int count = Math.Clamp((int)(c.Radius * 1.6f), 3, 10);
            for (int i = 0; i < count; i++)
            {
                float ang = MathF.Tau * i / count;
                var pos = c.Center + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * (c.Radius * 0.55f);
                AddCover(pos, CoverKind.Crater, Vector2.Zero, _combat.CraterProtection, true);
            }
        }
    }

    private void AddCover(Vector2 pos, CoverKind kind, Vector2 facing, float strength, bool canFire)
    {
        if (!InBounds(pos) || !Nav.IsWalkable(pos)) return;
        if (CoverNear(pos, 1.2f).Any()) return; // too close to an existing point
        var cp = new CoverPoint { Id = _cover.Count, Position = pos, Kind = kind, Facing = facing, Strength = strength, CanFire = canFire };
        _cover.Add(cp);
        var b = BucketAt(_coverBuckets, pos);
        b?.Add(cp);
    }

    // ---------------------------------------------------------------- buckets

    private List<T>[] NewBuckets<T>()
    {
        var arr = new List<T>[_bucketsX * _bucketsY];
        for (int i = 0; i < arr.Length; i++) arr[i] = new List<T>();
        return arr;
    }

    private void AddToBuckets<T>(List<T>[] buckets, T item, float minX, float minY, float maxX, float maxY)
    {
        int x0 = Math.Max(0, (int)(minX / BucketSize)), x1 = Math.Min(_bucketsX - 1, (int)(maxX / BucketSize));
        int y0 = Math.Max(0, (int)(minY / BucketSize)), y1 = Math.Min(_bucketsY - 1, (int)(maxY / BucketSize));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            buckets[y * _bucketsX + x].Add(item);
    }

    private List<T>? BucketAt<T>(List<T>[] buckets, Vector2 p)
    {
        int x = (int)(p.X / BucketSize), y = (int)(p.Y / BucketSize);
        if (x < 0 || y < 0 || x >= _bucketsX || y >= _bucketsY) return null;
        return buckets[y * _bucketsX + x];
    }

    // ---------------------------------------------------------------- smoke screens (milestone 7)

    private readonly List<SmokeCloud> _smoke = new();
    /// <summary>Smoke clouds from smoke shells. They hide everything behind them until they thin out.</summary>
    public IReadOnlyList<SmokeCloud> Smoke => _smoke;

    public SmokeCloud AddSmoke(Vector2 at, float radius, float born, float duration)
    {
        var c = new SmokeCloud { Center = at, FullRadius = radius, Born = born, Expires = born + duration };
        _smoke.Add(c);
        return c;
    }

    /// <summary>Clouds grow to full size over a few seconds, drift with the wind and thin out at the end.</summary>
    public void UpdateSmoke(float time, float dt, Vector2 wind, float growTime)
    {
        for (int i = _smoke.Count - 1; i >= 0; i--)
        {
            var c = _smoke[i];
            if (time >= c.Expires) { _smoke.RemoveAt(i); continue; }
            c.Center += wind * dt;
            float age = time - c.Born, left = c.Expires - time;
            float grow = Math.Clamp(age / MathF.Max(growTime, 0.1f), 0f, 1f);
            float thin = Math.Clamp(left / 10f, 0f, 1f);
            c.Radius = c.FullRadius * (0.3f + 0.7f * grow) * (0.6f + 0.4f * thin);
            c.Density = MathF.Min(grow * 1.5f, 1f) * thin;
        }
    }

    /// <summary>A line of sight is blocked if it passes through the thick middle of a cloud. Two men close
    /// together inside the smoke can still see each other.</summary>
    private bool SmokeBlocks(Vector2 a, Vector2 b)
    {
        if (Vector2.DistanceSquared(a, b) < 12f * 12f) return false;
        foreach (var c in _smoke)
        {
            if (c.Density < 0.5f) continue;
            float core = c.Radius * 0.8f;
            if (Geo.PointSegmentDistSq(c.Center, a, b, out _) < core * core) return true;
        }
        return false;
    }
}

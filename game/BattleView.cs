using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>Draws the moving parts of the battle: soldiers (smoothly between sim ticks), tracers, dust,
/// target and path lines, and the selected soldier's ring.</summary>
public partial class BattleView : Node3D
{
    private readonly Battle _battle;
    private SoldierCrowd _crowd = null!;
    private readonly List<TankView> _tanks = new();
    /// <summary>Follows the selected soldier around, so the camera can follow him.</summary>
    public Node3D SelectedAnchor { get; } = new() { Name = "SelectedAnchor" };
    private System.Numerics.Vector2[] _prev = System.Array.Empty<System.Numerics.Vector2>();
    private System.Numerics.Vector2[] _curr = System.Array.Empty<System.Numerics.Vector2>();

    private MeshInstance3D _lines = null!;
    private ImmediateMesh _lineMesh = null!;
    private StandardMaterial3D _lineMat = null!;
    private MeshInstance3D _selectRing = null!;

    private readonly List<Tracer> _tracers = new();
    /// <summary>A shell or rocket has landed here (for sound and lingering smoke).</summary>
    public System.Action<Vector3>? ShellBurst { get; set; }
    private readonly List<Puff> _puffs = new();
    // Tracers and dust puffs are drawn in batches too: one MultiMesh each, refilled every frame.
    private MultiMesh _rifleTracers = null!, _mgTracers = null!, _puffMesh = null!;
    private float[] _rifleTracerBuf = System.Array.Empty<float>(), _mgTracerBuf = System.Array.Empty<float>(), _puffBuf = System.Array.Empty<float>();

    public bool ShowLines { get; set; }
    public bool ShowPlans { get; set; } = true;
    /// <summary>Strict fog of war: enemy soldiers are only drawn while your side can see them.</summary>
    public bool Fog { get; set; } = true;
    public Side PlayerSide { get; set; } = Side.Blue;
    // Last-known enemy positions, all in one batch.
    private MultiMesh _ghosts = null!;
    private float[] _ghostBuf = System.Array.Empty<float>();
    public bool ShowBars { get; set; } = true;
    public bool ShowAllLabels { get; set; }
    public Soldier? Selected { get; set; }

    private sealed class Tracer
    {
        public Vector3 From, To;
        public float Age, Duration;
        public bool Hit, MachineGun, Shell;
    }

    private sealed class Puff
    {
        public Vector3 At;
        public float Age, Life, Size;
        public Color Color;
    }

    public BattleView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        _crowd = new SoldierCrowd(_battle) { Name = "Soldiers" };
        AddChild(_crowd);
        AddChild(SelectedAnchor);
        foreach (var s in _battle.Soldiers)
        {
            if (!s.IsTank) continue;
            var tv = new TankView(s) { Name = $"Tank {s.Label}" };
            AddChild(tv);
            _tanks.Add(tv);
        }
        _prev = new System.Numerics.Vector2[_battle.Soldiers.Count];
        _curr = new System.Numerics.Vector2[_battle.Soldiers.Count];
        for (int i = 0; i < _curr.Length; i++) _prev[i] = _curr[i] = _battle.Soldiers[i].Position;

        _lineMesh = new ImmediateMesh();
        _lineMat = Draw.VertexColored(transparent: true);
        _lines = new MeshInstance3D { Mesh = _lineMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_lines);

        _selectRing = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.9f, OuterRadius = 1.1f, Rings = 32, RingSegments = 4 },
            MaterialOverride = Draw.Unlit(new Color(1f, 0.95f, 0.3f)),
            Scale = new Vector3(1, 0.2f, 1),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_selectRing);

        var tracerMesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, 1f) };
        _rifleTracers = Batch(tracerMesh, Draw.Glow(new Color(1f, 0.85f, 0.45f), 4f), colors: false);
        _mgTracers = Batch(tracerMesh, Draw.Glow(new Color(1f, 0.6f, 0.3f), 5f), colors: false);
        var puffMat = Draw.VertexColored(transparent: true);
        puffMat.VertexColorIsSrgb = true;
        _puffMesh = Batch(new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 }, puffMat, colors: true);
        var ghostMat = Draw.VertexColored(transparent: true);
        ghostMat.VertexColorIsSrgb = true;
        _ghosts = Batch(new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.7f, Height = 0.1f, RadialSegments = 12 }, ghostMat, colors: true);
    }

    private MultiMesh Batch(Mesh mesh, Material mat, bool colors)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = colors,
            Mesh = mesh,
            CustomAabb = new Aabb(new Vector3(-50, -10, -50), new Vector3(_battle.Map.Width + 100, 60, _battle.Map.Height + 100)),
        };
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return mm;
    }

    /// <summary>Is this soldier drawn, given the fog of war?</summary>
    private bool KnownToPlayer(Soldier s)
    {
        if (!Fog || s.Side == PlayerSide) return true;
        if (!s.IsActive) return s.Status != SoldierStatus.Routed; // bodies stay where they fell
        var c = _battle.Knowledge(PlayerSide).Get(s.Id);
        return c != null && c.Confidence >= 0.9f && _battle.Time - c.LastSeen <= 1.5f;
    }

    private void UpdateGhosts()
    {
        int used = 0;
        if (Fog)
        {
            var knowledge = _battle.Knowledge(PlayerSide);
            Grow(_ghosts, ref _ghostBuf, knowledge.Count, 16);
            foreach (var c in knowledge.Contacts)
            {
                float age = _battle.Time - c.LastSeen;
                if (age <= 1.5f && c.Confidence >= 0.9f) continue; // he's drawn for real
                // A guess from a muzzle flash is drawn bigger: "somewhere around here".
                float size = c.Confidence < 0.9f ? 3f : 1f;
                int o = 16 * used++;
                Put(_ghostBuf, o, new Transform3D(Basis.FromScale(new Vector3(size, 1, size)), Draw.To3(c.LastPosition, 0.06f)));
                // Fades from solid to faint as the sighting gets older.
                _ghostBuf[o + 12] = 1f; _ghostBuf[o + 13] = 0.35f; _ghostBuf[o + 14] = 0.3f;
                _ghostBuf[o + 15] = 0.7f - Mathf.Clamp((int)(age / 5f), 0, 5) * 0.11f;
            }
        }
        Flush(_ghosts, _ghostBuf, used);
    }

    /// <summary>Call once before each sim step so soldiers can be drawn between the last two positions.</summary>
    public void BeforeStep()
    {
        for (int i = 0; i < _curr.Length; i++) _prev[i] = _battle.Soldiers[i].Position;
    }

    /// <summary>Call after each sim step to pick up this tick's shots.</summary>
    public void AfterStep(float gameSpeed)
    {
        for (int i = 0; i < _curr.Length; i++) _curr[i] = _battle.Soldiers[i].Position;
        foreach (var shot in _battle.Shots)
        {
            var shooter = _battle.Soldiers[shot.ShooterId];
            float muzzle = shooter.IsTank ? 1.95f : shooter.Stance switch { Stance.Prone => 0.3f, Stance.Crouched => 0.95f, _ => 1.35f };
            var from = Draw.To3(shot.From, muzzle);
            if (shot.Shell)
            {
                var burst = Draw.To3(shot.Impact, shot.Hit ? 1.5f : 0.3f);
                var muzzleEnd = from + (burst - from).Normalized() * 3.5f;
                SpawnTracer(muzzleEnd, burst, from.DistanceTo(burst) / 700f, shot.Hit, true, shell: true);
                SpawnPuff(muzzleEnd, new Color(1f, 0.8f, 0.4f, 1f), 0.15f, 1.2f);   // muzzle flash
                SpawnPuff(muzzleEnd, new Color(0.7f, 0.68f, 0.62f, 0.6f), 1.2f, 2.0f); // gun smoke
                continue;
            }
            var to = Draw.To3(shot.Impact, shot.Hit ? 0.9f : 0.05f);
            if (!shot.Hit)
            {
                // Misses keep going a little past the target into the ground.
                var dir = (to - from).Normalized();
                to = from + dir * (from.DistanceTo(to) + 2f);
                to.Y = 0.05f;
            }
            float dist = from.DistanceTo(to);
            SpawnTracer(from, to, dist / 450f, shot.Hit, shot.MachineGun);
            SpawnPuff(from + (to - from).Normalized() * 0.6f, new Color(1f, 0.9f, 0.5f, 0.9f), 0.35f, 0.06f);
        }
    }

    public void UpdateVisuals(float alpha, float realDelta, float gameSpeed, bool paused)
    {
        _crowd.UpdateAll(KnownToPlayer, _prev, _curr, alpha, ShowBars, ShowAllLabels, Selected);
        if (Selected != null) SelectedAnchor.Position = _crowd.Positions[Selected.Id];
        foreach (var tv in _tanks) tv.UpdateVisual(_crowd.Positions[tv.Tank.Id], _crowd.Shown[tv.Tank.Id]);

        float dt = paused ? 0f : realDelta * gameSpeed;
        UpdateTracers(dt);
        UpdatePuffs(dt);
        UpdateSelection();
        UpdateGhosts();
        DrawLines();
    }

    // ---------------------------------------------------------------- effects

    private void SpawnTracer(Vector3 from, Vector3 to, float duration, bool hit, bool mg, bool shell = false)
    {
        _tracers.Add(new Tracer { From = from, To = to, Duration = Mathf.Max(duration, 0.05f), Hit = hit, MachineGun = mg, Shell = shell });
    }

    private void UpdateTracers(float dt)
    {
        int rifles = 0, mgs = 0;
        for (int i = _tracers.Count - 1; i >= 0; i--)
        {
            var t = _tracers[i];
            t.Age += dt;
            if (t.Age >= t.Duration && t.Shell)
            {
                ShellBurst?.Invoke(t.To);
                // Shell burst: a flash, then a column of earth and smoke.
                SpawnPuff(t.To + new Vector3(0, 0.5f, 0), new Color(1f, 0.75f, 0.35f, 1f), 0.25f, 3.5f);
                SpawnPuff(t.To + new Vector3(0, 1.0f, 0), new Color(0.32f, 0.27f, 0.22f, 0.9f), 1.8f, 6f);
                SpawnPuff(t.To + new Vector3(0, 2.5f, 0), new Color(0.45f, 0.42f, 0.38f, 0.6f), 2.5f, 5f);
                _tracers[i] = _tracers[^1];
                _tracers.RemoveAt(_tracers.Count - 1);
                continue;
            }
            if (t.Age >= t.Duration)
            {
                SpawnPuff(t.To, t.Hit ? new Color(0.6f, 0.1f, 0.08f, 0.9f) : new Color(0.55f, 0.5f, 0.42f, 0.8f),
                    t.Hit ? 0.5f : 0.7f, t.Hit ? 0.5f : 0.8f);
                _tracers[i] = _tracers[^1];
                _tracers.RemoveAt(_tracers.Count - 1);
                continue;
            }
            if (t.MachineGun) mgs++; else rifles++;
        }
        Grow(_rifleTracers, ref _rifleTracerBuf, rifles, 12);
        Grow(_mgTracers, ref _mgTracerBuf, mgs, 12);
        int ri = 0, mi = 0;
        foreach (var t in _tracers)
        {
            float k = t.Age / t.Duration;
            var dir = t.To - t.From;
            float len = dir.Length();
            float streak = Mathf.Min(9f, len);
            var head = t.From + dir * k;
            var mid = head - dir / len * streak * 0.5f;
            var xf = new Transform3D(Basis.LookingAt(dir / len, Vector3.Up) * Basis.FromScale(new Vector3(1, 1, streak)), mid);
            if (t.MachineGun) Put(_mgTracerBuf, 12 * mi++, xf);
            else Put(_rifleTracerBuf, 12 * ri++, xf);
        }
        Flush(_rifleTracers, _rifleTracerBuf, rifles);
        Flush(_mgTracers, _mgTracerBuf, mgs);
    }

    private void SpawnPuff(Vector3 at, Color color, float life, float size)
    {
        _puffs.Add(new Puff { At = at, Life = life, Size = size, Color = color });
    }

    private void UpdatePuffs(float dt)
    {
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var p = _puffs[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                _puffs[i] = _puffs[^1];
                _puffs.RemoveAt(_puffs.Count - 1);
            }
        }
        Grow(_puffMesh, ref _puffBuf, _puffs.Count, 16);
        for (int i = 0; i < _puffs.Count; i++)
        {
            var p = _puffs[i];
            float k = p.Age / p.Life;
            float size = p.Size * (0.4f + 1.2f * k);
            int o = 16 * i;
            Put(_puffBuf, o, new Transform3D(Basis.FromScale(new Vector3(size, size, size)), p.At));
            _puffBuf[o + 12] = p.Color.R; _puffBuf[o + 13] = p.Color.G; _puffBuf[o + 14] = p.Color.B;
            _puffBuf[o + 15] = p.Color.A * (1f - k);
        }
        Flush(_puffMesh, _puffBuf, _puffs.Count);
    }

    /// <summary>Make sure a batch has room for at least <paramref name="needed"/> instances.</summary>
    private static void Grow(MultiMesh mm, ref float[] buf, int needed, int stride)
    {
        if (mm.InstanceCount >= needed) return;
        int size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)System.Math.Max(64, needed));
        mm.InstanceCount = size;
        buf = new float[size * stride];
    }

    private static void Flush(MultiMesh mm, float[] buf, int used)
    {
        mm.VisibleInstanceCount = used;
        if (used > 0) RenderingServer.MultimeshSetBuffer(mm.GetRid(), buf);
    }

    private static void Put(float[] buf, int o, in Transform3D t)
    {
        var bs = t.Basis;
        buf[o + 0] = bs.X.X; buf[o + 1] = bs.Y.X; buf[o + 2] = bs.Z.X; buf[o + 3] = t.Origin.X;
        buf[o + 4] = bs.X.Y; buf[o + 5] = bs.Y.Y; buf[o + 6] = bs.Z.Y; buf[o + 7] = t.Origin.Y;
        buf[o + 8] = bs.X.Z; buf[o + 9] = bs.Y.Z; buf[o + 10] = bs.Z.Z; buf[o + 11] = t.Origin.Z;
    }

    // ---------------------------------------------------------------- debug drawing

    private void UpdateSelection()
    {
        if (Selected == null) { _selectRing.Visible = false; return; }
        _selectRing.Visible = _crowd.Shown[Selected.Id];
        _selectRing.Position = _crowd.Positions[Selected.Id] + new Vector3(0, 0.05f, 0);
    }

    private void DrawLines()
    {
        _lineMesh.ClearSurfaces();
        bool any = false;
        foreach (var s in _battle.Soldiers)
        {
            if (!s.IsActive || !_crowd.Shown[s.Id]) continue;
            bool show = ShowLines || s == Selected;
            if (!show) continue;
            var from = _crowd.Positions[s.Id] + new Vector3(0, 0.6f, 0);
            if (s.Target == null && s.AreaTarget != null && s.Action == SoldierAction.Engage)
            {
                if (!any) { _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMat); any = true; }
                AddLine(from, Draw.To3(s.AreaTarget.Value, 0.6f), new Color(1f, 0.9f, 0.4f, 0.45f));
            }
            if (s.Target != null && s.Action == SoldierAction.Engage)
            {
                if (!any) { _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMat); any = true; }
                var c = s.Side == Side.Blue ? new Color(0.5f, 0.7f, 1f, 0.7f) : new Color(1f, 0.5f, 0.45f, 0.7f);
                _lineMesh.SurfaceSetColor(c);
                _lineMesh.SurfaceAddVertex(from);
                _lineMesh.SurfaceSetColor(c);
                _lineMesh.SurfaceAddVertex(_crowd.Positions[s.Target.Id] + new Vector3(0, 0.6f, 0));
            }
            if (s.Path != null)
            {
                if (!any) { _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMat); any = true; }
                var prev = from;
                var c = new Color(1f, 1f, 1f, 0.55f);
                for (int i = s.PathIndex; i < s.Path.Count; i++)
                {
                    var next = Draw.To3(s.Path[i], 0.6f);
                    _lineMesh.SurfaceSetColor(c);
                    _lineMesh.SurfaceAddVertex(prev);
                    _lineMesh.SurfaceSetColor(c);
                    _lineMesh.SurfaceAddVertex(next);
                    prev = next;
                }
            }
        }
        if (ShowPlans)
        {
            foreach (var g in _battle.Groups)
            {
                if (Fog && g.Side != PlayerSide) continue;
                if (g.CountEffective == 0) continue;
                foreach (var t in g.Teams)
                {
                    if (t.CountEffective == 0) continue;
                    if (!any) { _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMat); any = true; }
                    var c = t.Role == TeamRole.Gun ? new Color(1f, 0.65f, 0.2f, 0.9f) : new Color(0.3f, 0.95f, 1f, 0.9f);
                    var from = Draw.To3(t.Centroid, 0.3f);
                    var to = Draw.To3(t.Destination, 0.3f);
                    if (from.DistanceTo(to) > 2f) AddLine(from, to, c);
                    // a small cross where the team is going
                    AddLine(to + new Vector3(-1.5f, 0, -1.5f), to + new Vector3(1.5f, 0, 1.5f), c);
                    AddLine(to + new Vector3(-1.5f, 0, 1.5f), to + new Vector3(1.5f, 0, -1.5f), c);
                }
                if (g.Tactic == Tactic.Flank)
                {
                    if (!any) { _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMat); any = true; }
                    var c = new Color(0.8f, 0.5f, 1f, 0.8f);
                    AddLine(Draw.To3(g.Rifles.Centroid, 0.4f), Draw.To3(g.FlankWaypoint, 0.4f), c);
                    AddLine(Draw.To3(g.FlankWaypoint, 0.4f), Draw.To3(g.FlankPoint, 0.4f), c);
                    AddLine(Draw.To3(g.FlankPoint, 0.4f), Draw.To3(g.EnemyEstimate, 0.4f), c);
                }
            }
        }
        if (any) _lineMesh.SurfaceEnd();
    }

    private void AddLine(Vector3 a, Vector3 b, Color c)
    {
        _lineMesh.SurfaceSetColor(c);
        _lineMesh.SurfaceAddVertex(a);
        _lineMesh.SurfaceSetColor(c);
        _lineMesh.SurfaceAddVertex(b);
    }

    /// <summary>The soldier nearest to a ground point, within reach metres.</summary>
    public Soldier? SoldierNear(Vector3 ground, float reach = 2.5f)
    {
        Soldier? best = null;
        float bestD = reach * reach;
        for (int i = 0; i < _battle.Soldiers.Count; i++)
        {
            if (!_crowd.Shown[i]) continue;
            var p = _crowd.Positions[i];
            float d = new Vector2(p.X - ground.X, p.Z - ground.Z).LengthSquared();
            if (d < bestD) { bestD = d; best = _battle.Soldiers[i]; }
        }
        return best;
    }
}

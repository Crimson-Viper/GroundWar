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
    private readonly List<SoldierView> _views = new();
    private System.Numerics.Vector2[] _prev = System.Array.Empty<System.Numerics.Vector2>();
    private System.Numerics.Vector2[] _curr = System.Array.Empty<System.Numerics.Vector2>();

    private MeshInstance3D _lines = null!;
    private ImmediateMesh _lineMesh = null!;
    private StandardMaterial3D _lineMat = null!;
    private MeshInstance3D _selectRing = null!;

    private readonly List<Tracer> _tracers = new();
    private readonly Stack<MeshInstance3D> _tracerPool = new();
    private readonly List<Puff> _puffs = new();
    private readonly Stack<MeshInstance3D> _puffPool = new();
    private Material _tracerMat = null!, _tracerMgMat = null!;
    private Mesh _tracerMesh = null!, _puffMesh = null!;

    public bool ShowLines { get; set; }
    public bool ShowBars { get; set; } = true;
    public bool ShowAllLabels { get; set; }
    public Soldier? Selected { get; set; }

    private sealed class Tracer
    {
        public required MeshInstance3D Node;
        public Vector3 From, To;
        public float Age, Duration;
        public bool Hit;
    }

    private sealed class Puff
    {
        public required MeshInstance3D Node;
        public required StandardMaterial3D Mat;
        public float Age, Life, Size;
        public Color Color;
    }

    public BattleView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        var mats = new SoldierView.Materials
        {
            Blue = Draw.Lit(new Color(0.25f, 0.42f, 0.85f)),
            Red = Draw.Lit(new Color(0.82f, 0.25f, 0.22f)),
            BlueDown = Draw.Lit(new Color(0.15f, 0.2f, 0.4f)),
            RedDown = Draw.Lit(new Color(0.4f, 0.13f, 0.12f)),
            Dead = Draw.Lit(new Color(0.18f, 0.18f, 0.18f)),
            Rifle = Draw.Lit(new Color(0.2f, 0.15f, 0.1f)),
            Cap = Draw.Lit(new Color(0.95f, 0.8f, 0.2f)),
            BarShader = Draw.LoadShader("res://game/shaders/status_bar.gdshader"),
            Body = new CapsuleMesh { Radius = 0.28f, Height = 1.75f, RadialSegments = 12, Rings = 4 },
            RifleMesh = new BoxMesh { Size = new Vector3(0.07f, 0.07f, 1.1f) },
            CapMesh = new BoxMesh { Size = new Vector3(0.42f, 0.14f, 0.42f) },
            BarMesh = new QuadMesh { Size = new Vector2(1.3f, 0.26f) },
        };
        foreach (var s in _battle.Soldiers)
        {
            var v = new SoldierView(s, mats) { Name = s.Label };
            AddChild(v);
            _views.Add(v);
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

        _tracerMat = Draw.Glow(new Color(1f, 0.85f, 0.45f), 4f);
        _tracerMgMat = Draw.Glow(new Color(1f, 0.6f, 0.3f), 5f);
        _tracerMesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, 1f) };
        _puffMesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 };
    }

    public SoldierView? ViewOf(Soldier s) => s.Id < _views.Count ? _views[s.Id] : null;

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
            float muzzle = shooter.Stance switch { Stance.Prone => 0.3f, Stance.Crouched => 0.95f, _ => 1.35f };
            var from = Draw.To3(shot.From, muzzle);
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
        for (int i = 0; i < _views.Count; i++)
        {
            var p = System.Numerics.Vector2.Lerp(_prev[i], _curr[i], alpha);
            var v = _views[i];
            v.SetLabelVisible(ShowAllLabels || v.Soldier == Selected);
            v.UpdateVisual(Draw.To3(p), ShowBars);
        }

        float dt = paused ? 0f : realDelta * gameSpeed;
        UpdateTracers(dt);
        UpdatePuffs(dt);
        UpdateSelection();
        DrawLines();
    }

    // ---------------------------------------------------------------- effects

    private void SpawnTracer(Vector3 from, Vector3 to, float duration, bool hit, bool mg)
    {
        var node = _tracerPool.Count > 0 ? _tracerPool.Pop() : CreateTracerNode();
        node.MaterialOverride = mg ? _tracerMgMat : _tracerMat;
        node.Visible = true;
        _tracers.Add(new Tracer { Node = node, From = from, To = to, Duration = Mathf.Max(duration, 0.05f), Hit = hit });
    }

    private MeshInstance3D CreateTracerNode()
    {
        var n = new MeshInstance3D { Mesh = _tracerMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(n);
        return n;
    }

    private void UpdateTracers(float dt)
    {
        for (int i = _tracers.Count - 1; i >= 0; i--)
        {
            var t = _tracers[i];
            t.Age += dt;
            float k = t.Age / t.Duration;
            if (k >= 1f)
            {
                SpawnPuff(t.To, t.Hit ? new Color(0.6f, 0.1f, 0.08f, 0.9f) : new Color(0.55f, 0.5f, 0.42f, 0.8f),
                    t.Hit ? 0.5f : 0.7f, t.Hit ? 0.5f : 0.8f);
                t.Node.Visible = false;
                _tracerPool.Push(t.Node);
                _tracers.RemoveAt(i);
                continue;
            }
            var dir = t.To - t.From;
            float len = dir.Length();
            float streak = Mathf.Min(9f, len);
            var head = t.From + dir * k;
            var mid = head - dir / len * streak * 0.5f;
            t.Node.GlobalTransform = new Transform3D(Basis.LookingAt(dir / len, Vector3.Up).Scaled(new Vector3(1, 1, streak)), mid);
        }
    }

    private void SpawnPuff(Vector3 at, Color color, float life, float size)
    {
        MeshInstance3D node;
        StandardMaterial3D mat;
        if (_puffPool.Count > 0)
        {
            node = _puffPool.Pop();
            mat = (StandardMaterial3D)node.MaterialOverride;
        }
        else
        {
            mat = Draw.Unlit(color, transparent: true);
            node = new MeshInstance3D { Mesh = _puffMesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node);
        }
        node.Visible = true;
        node.Position = at;
        _puffs.Add(new Puff { Node = node, Mat = mat, Life = life, Size = size, Color = color });
    }

    private void UpdatePuffs(float dt)
    {
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var p = _puffs[i];
            p.Age += dt;
            float k = p.Age / p.Life;
            if (k >= 1f)
            {
                p.Node.Visible = false;
                _puffPool.Push(p.Node);
                _puffs.RemoveAt(i);
                continue;
            }
            float s = p.Size * (0.4f + 1.2f * k);
            p.Node.Scale = new Vector3(s, s, s);
            p.Mat.AlbedoColor = new Color(p.Color, p.Color.A * (1f - k));
        }
    }

    // ---------------------------------------------------------------- debug drawing

    private void UpdateSelection()
    {
        if (Selected == null) { _selectRing.Visible = false; return; }
        var v = ViewOf(Selected);
        _selectRing.Visible = v != null && v.Visible;
        if (v != null) _selectRing.Position = v.Position + new Vector3(0, 0.05f, 0);
    }

    private void DrawLines()
    {
        _lineMesh.ClearSurfaces();
        bool any = false;
        foreach (var s in _battle.Soldiers)
        {
            if (!s.IsActive) continue;
            bool show = ShowLines || s == Selected;
            if (!show) continue;
            var view = _views[s.Id];
            var from = view.Position + new Vector3(0, 0.6f, 0);
            if (s.Target != null && s.Action == SoldierAction.Engage)
            {
                if (!any) { _lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMat); any = true; }
                var c = s.Side == Side.Blue ? new Color(0.5f, 0.7f, 1f, 0.7f) : new Color(1f, 0.5f, 0.45f, 0.7f);
                _lineMesh.SurfaceSetColor(c);
                _lineMesh.SurfaceAddVertex(from);
                _lineMesh.SurfaceSetColor(c);
                _lineMesh.SurfaceAddVertex(_views[s.Target.Id].Position + new Vector3(0, 0.6f, 0));
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
        if (any) _lineMesh.SurfaceEnd();
    }

    /// <summary>The soldier nearest to a ground point, within reach metres.</summary>
    public Soldier? SoldierNear(Vector3 ground, float reach = 2.5f)
    {
        Soldier? best = null;
        float bestD = reach * reach;
        foreach (var v in _views)
        {
            if (!v.Visible) continue;
            float d = new Vector2(v.Position.X - ground.X, v.Position.Z - ground.Z).LengthSquared();
            if (d < bestD) { bestD = d; best = v.Soldier; }
        }
        return best;
    }
}

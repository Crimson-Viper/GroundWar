using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;
using SVec2 = System.Numerics.Vector2;

namespace GroundWar.Game;

/// <summary>
/// Milestone 5 on the map: each of your company commanders' plans drawn as arrows on the ground (the phase he is
/// on bright, phases done faded, phases to come in between), and a flag over each company HQ.
/// See docs/systems/company-commander.md.
/// </summary>
public partial class CompanyView : Node3D
{
    private readonly Battle _battle;
    private readonly Dictionary<Company, (CompanyPlan? plan, int phase, CompanyState state)> _drawn = new();
    private readonly Dictionary<Company, Node3D> _arrows = new();
    private readonly Dictionary<Company, Label3D> _flags = new();

    public Side PlayerSide { get; set; } = Side.Blue;
    public bool Fog { get; set; } = true;
    public bool ShowArrows { get; set; } = true;

    private static readonly Color BlueColor = new(0.45f, 0.65f, 1f), RedColor = new(1f, 0.5f, 0.45f);

    public CompanyView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        foreach (var c in _battle.Companies)
        {
            var flag = new Label3D
            {
                Text = $"{c.Name}\n{c.CommanderName}", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true, PixelSize = 0.0008f,
                FontSize = 22, OutlineSize = 8, NoDepthTest = true, Modulate = c.Side == Side.Blue ? BlueColor : RedColor,
            };
            AddChild(flag);
            _flags[c] = flag;
        }
    }

    public void UpdateVisuals()
    {
        foreach (var c in _battle.Companies)
        {
            bool shown = !Fog || c.Side == PlayerSide;
            var flag = _flags[c];
            flag.Visible = shown && !c.CommanderDown;
            if (flag.Visible) flag.Position = Draw.To3(Battle.SquadPost(c.HqGroup), 4.5f);

            var key = (c.Plan, c.PhaseIndex, c.State);
            bool wantArrows = shown && ShowArrows && c.Plan != null && c.Plan.Arrows.Count > 0 &&
                              c.State is CompanyState.BriefBack or CompanyState.Executing && !c.CommanderDown;
            if (!wantArrows)
            {
                if (_arrows.Remove(c, out var old)) old.QueueFree();
                _drawn.Remove(c);
                continue;
            }
            if (_drawn.TryGetValue(c, out var was) && was == key) continue;
            _drawn[c] = key;
            if (_arrows.Remove(c, out var stale)) stale.QueueFree();
            var node = BuildArrows(c);
            AddChild(node);
            _arrows[c] = node;
        }
    }

    private Node3D BuildArrows(Company c)
    {
        var node = new Node3D { Name = c.Name + " plan" };
        var baseCol = c.Side == Side.Blue ? BlueColor : RedColor;
        int current = c.State == CompanyState.Executing ? c.PhaseIndex : -1;
        // Arrow phases are counted in the plan's own terms; a brief-back shows the whole plan at full strength.
        foreach (var (from, to, phase) in c.Plan!.Arrows)
        {
            float alpha = current < 0 ? 0.85f : phase < current ? 0.25f : phase == current ? 0.95f : 0.55f;
            var col = new Color(baseCol, alpha);
            if (current >= 0 && phase == current) col = col.Lightened(0.25f);
            node.AddChild(Arrow(from, to, col, phase == current ? 1.6f : 1.1f));
        }
        return node;
    }

    /// <summary>A flat arrow lying on the ground: a strip with a triangle head.</summary>
    private static MeshInstance3D Arrow(SVec2 from, SVec2 to, Color col, float width)
    {
        var mesh = new ImmediateMesh();
        var d = to - from;
        float len = d.Length();
        var mi = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        if (len < 2f) return mi;
        var dir = d / len;
        var side = new SVec2(-dir.Y, dir.X);
        float head = Mathf.Min(6f, len * 0.4f);
        var shaftEnd = to - dir * head;
        const float y = 0.35f;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        void V(SVec2 p) => mesh.SurfaceAddVertex(Draw.To3(p, y));
        var a = from + side * width * 0.5f; var b = from - side * width * 0.5f;
        var e = shaftEnd + side * width * 0.5f; var f = shaftEnd - side * width * 0.5f;
        V(a); V(b); V(e);
        V(e); V(b); V(f);
        V(shaftEnd + side * width * 1.6f); V(shaftEnd - side * width * 1.6f); V(to);
        mesh.SurfaceEnd();
        var mat = Draw.Unlit(col, transparent: true);
        mat.NoDepthTest = true;
        mi.MaterialOverride = mat;
        return mi;
    }
}

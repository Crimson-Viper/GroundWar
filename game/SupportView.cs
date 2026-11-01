using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>
/// Battalion support (milestone 7): the mortar tubes of each mortar section, and a ring on the ground over every
/// target your side's mortars have been ordered to fire on (orange for high explosive, white for smoke), with
/// the rounds landed so far. See docs/systems/support-fire.md.
/// </summary>
public partial class SupportView : Node3D
{
    private readonly Battle _battle;
    private readonly Dictionary<Group, Node3D> _tubes = new();
    private readonly List<(MeshInstance3D Ring, Label3D Label)> _markers = new();
    private StandardMaterial3D _heMat = null!, _smokeMat = null!;

    public Side PlayerSide { get; set; } = Side.Blue;
    public bool Fog { get; set; } = true;

    public SupportView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        _heMat = Draw.Unlit(new Color(1f, 0.55f, 0.2f, 0.75f), transparent: true);
        _smokeMat = Draw.Unlit(new Color(0.95f, 0.95f, 0.95f, 0.75f), transparent: true);
        foreach (var g in _battle.Groups)
        {
            if (g.Mortars is not { } ms) continue;
            var n = new Node3D { Name = g.Name };
            var facing = new Vector3(g.Facing.X, 0, g.Facing.Y).Normalized();
            var across = new Vector3(-facing.Z, 0, facing.X);
            for (int i = 0; i < ms.Tubes; i++)
            {
                var at = across * ((i - (ms.Tubes - 1) * 0.5f) * 3f);
                // A baseplate and a tube leaning towards the enemy.
                n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.06f }, Position = at + new Vector3(0, 0.03f, 0), MaterialOverride = Draw.Lit(new Color(0.22f, 0.24f, 0.2f)) });
                var tube = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 1.3f }, MaterialOverride = Draw.Lit(new Color(0.18f, 0.2f, 0.16f)) };
                tube.Basis = new Basis(across, Mathf.DegToRad(-25f));
                tube.Position = at + new Vector3(0, 0.6f, 0) + facing * 0.25f;
                n.AddChild(tube);
            }
            AddChild(n);
            _tubes[g] = n;
        }
    }

    private bool Shows(Side side) => !Fog || side == PlayerSide;

    public void UpdateVisuals()
    {
        foreach (var (g, n) in _tubes)
        {
            n.Visible = Shows(g.Side) && g.CountEffective > 0;
            n.Position = Draw.To3(g.HoldPosition);
        }
        int used = 0;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.GetTicksMsec() * 0.004f);
        foreach (var fm in _battle.FireMissions)
        {
            if (fm.Status is not (FireMissionStatus.Ordered or FireMissionStatus.Firing) && !(fm.Status == FireMissionStatus.Done && _battle.Time - fm.DoneAt < 5f)) continue;
            if (!Shows(fm.Side)) continue;
            if (used == _markers.Count)
            {
                var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1f, Rings = 48, RingSegments = 4 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                var label = new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true, PixelSize = 0.0008f, FontSize = 22, OutlineSize = 8, NoDepthTest = true };
                AddChild(ring);
                AddChild(label);
                _markers.Add((ring, label));
            }
            var (r, l) = _markers[used++];
            float radius = fm.Kind == FireKind.Smoke ? _battle.Rules.Support.SmokeRadius * 1.5f : _battle.Rules.Support.AdjustedSpread * 1.5f;
            r.Visible = l.Visible = true;
            r.MaterialOverride = fm.Kind == FireKind.Smoke ? _smokeMat : _heMat;
            r.Position = Draw.To3(fm.Target, 0.3f);
            r.Scale = new Vector3(radius * pulse, 1f, radius * pulse);
            string kind = fm.Kind == FireKind.Smoke ? "Smoke" : "HE";
            string state = fm.Status switch
            {
                FireMissionStatus.Firing when fm.Waiting => "on call",
                FireMissionStatus.Ordered => "ordered",
                FireMissionStatus.Firing when fm.RoundsFired == 0 => "laying the mortars",
                FireMissionStatus.Firing => $"{fm.RoundsLanded}/{fm.Rounds} landed",
                _ => "done",
            };
            l.Text = $"{kind}: {state}";
            l.Modulate = fm.Kind == FireKind.Smoke ? new Color(0.95f, 0.95f, 0.95f) : new Color(1f, 0.65f, 0.35f);
            l.Position = Draw.To3(fm.Target, 6f);
        }
        for (int i = used; i < _markers.Count; i++) _markers[i].Ring.Visible = _markers[i].Label.Visible = false;
    }
}

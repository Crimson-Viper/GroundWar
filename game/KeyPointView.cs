using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>
/// The key points of a battalion battle (milestone 7, part 2): a faint ring on the ground round each named place
/// (the village, the farm) with its name. The ring is blue or red when that side's men alone are on it, and
/// white when nobody or both are. With fog of war, only your own men count. See docs/systems/battalion-commander.md.
/// </summary>
public partial class KeyPointView : Node3D
{
    private readonly Battle _battle;
    private readonly List<(KeyPoint Point, MeshInstance3D Ring, Label3D Label)> _points = new();
    private StandardMaterial3D _neutral = null!, _blue = null!, _red = null!;

    public Side PlayerSide { get; set; } = Side.Blue;
    public bool Fog { get; set; } = true;

    public KeyPointView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        _neutral = Draw.Unlit(new Color(0.95f, 0.92f, 0.8f, 0.35f), transparent: true);
        _blue = Draw.Unlit(new Color(0.4f, 0.6f, 1f, 0.5f), transparent: true);
        _red = Draw.Unlit(new Color(1f, 0.45f, 0.4f, 0.5f), transparent: true);
        foreach (var p in _battle.KeyPoints)
        {
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.97f, OuterRadius = 1f, Rings = 64, RingSegments = 4 },
                Position = Draw.To3(p.Position, 0.25f), Scale = new Vector3(p.Radius, 1f, p.Radius),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = _neutral,
            };
            var label = new Label3D
            {
                Text = char.ToUpperInvariant(p.Name[0]) + p.Name[1..], Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true,
                PixelSize = 0.0009f, FontSize = 24, OutlineSize = 8, NoDepthTest = true, Position = Draw.To3(p.Position, 14f),
                Modulate = new Color(0.98f, 0.95f, 0.85f),
            };
            AddChild(ring);
            AddChild(label);
            _points.Add((p, ring, label));
        }
    }

    public void UpdateVisuals()
    {
        foreach (var (p, ring, label) in _points)
        {
            int blue = 0, red = 0;
            foreach (var s in _battle.Soldiers)
            {
                if (!s.IsEffective || System.Numerics.Vector2.DistanceSquared(s.Position, p.Position) > p.Radius * p.Radius) continue;
                if (Fog && s.Side != PlayerSide) continue;
                if (s.Side == Side.Blue) blue++; else red++;
            }
            ring.MaterialOverride = blue > 0 && red == 0 ? _blue : red > 0 && blue == 0 ? _red : _neutral;
        }
    }
}

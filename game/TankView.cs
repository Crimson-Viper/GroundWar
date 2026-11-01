using Godot;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>Grey-box tank: hull, two tracks, a turret that turns on its own, and a gun barrel.
/// A knocked-out tank goes dark and its turret is left skewed.</summary>
public partial class TankView : Node3D
{
    public Soldier Tank { get; }
    private Node3D _turret = null!;
    private MeshInstance3D _hull = null!, _turretBox = null!, _barrel = null!;
    private StandardMaterial3D _paint = null!;
    private bool _wrecked;

    public TankView(Soldier tank) { Tank = tank; }

    public override void _Ready()
    {
        var v = Tank.Vehicle!;
        var colour = Tank.Side == Side.Blue ? new Color(0.32f, 0.42f, 0.62f) : new Color(0.62f, 0.32f, 0.28f);
        _paint = Draw.Lit(colour, 0.8f);
        var trackMat = Draw.Lit(new Color(0.16f, 0.16f, 0.15f));
        // -Z is forward (Draw.YawFor makes -Z face the sim facing).
        _hull = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(v.Width * 0.75f, 1.1f, v.Length) }, MaterialOverride = _paint, Position = new Vector3(0, 1.0f, 0) };
        AddChild(_hull);
        foreach (float side in new[] { -1f, 1f })
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(v.Width * 0.22f, 0.9f, v.Length * 1.02f) },
                MaterialOverride = trackMat,
                Position = new Vector3(side * v.Width * 0.39f, 0.45f, 0),
            });
        // A sloped glacis plate at the front so you can tell front from back.
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(v.Width * 0.74f, 0.12f, 1.4f) },
            MaterialOverride = _paint,
            Position = new Vector3(0, 1.3f, -v.Length * 0.5f + 0.35f),
            RotationDegrees = new Vector3(-35f, 0, 0),
        });
        _turret = new Node3D { Position = new Vector3(0, 1.55f, 0.3f) };
        AddChild(_turret);
        _turretBox = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(v.Width * 0.6f, 0.75f, v.Length * 0.4f) }, MaterialOverride = _paint, Position = new Vector3(0, 0.37f, 0) };
        _turret.AddChild(_turretBox);
        _barrel = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.09f, Height = 3.0f, RadialSegments = 8 },
            MaterialOverride = trackMat,
            Position = new Vector3(0, 0.4f, -v.Length * 0.2f - 1.5f),
            RotationDegrees = new Vector3(-90f, 0, 0),
        };
        _turret.AddChild(_barrel);
    }

    public void UpdateVisual(Vector3 position, bool shown)
    {
        Visible = shown;
        if (!shown) return;
        Position = position;
        Rotation = new Vector3(0, Draw.YawFor(Tank.Facing), 0);
        if (Tank.Status == SoldierStatus.Dead)
        {
            if (!_wrecked)
            {
                _wrecked = true;
                _paint.AlbedoColor = new Color(0.13f, 0.12f, 0.11f);
                _turret.Rotation = new Vector3(0.05f, 0.6f, 0.08f);
            }
            return;
        }
        // Turret yaw relative to the hull.
        _turret.Rotation = new Vector3(0, Draw.YawFor(Tank.TurretFacing) - Draw.YawFor(Tank.Facing), 0);
    }
}

using Godot;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>Draws one tank: the model from models/tank.glb (see docs/models.md and UnitModels), whose "Turret"
/// node turns on its own. Without the model it is a grey box: hull, two tracks, turret and gun barrel.
/// A knocked-out tank goes dark and its turret is left skewed.</summary>
public partial class TankView : Node3D
{
    public Soldier Tank { get; }
    private Node3D _turret = null!;
    private MeshInstance3D _hull = null!, _turretBox = null!, _barrel = null!;
    private StandardMaterial3D _paint = null!;
    private bool _wrecked;
    // With a model: every material on it (copied, so each tank can be painted and burnt on its own).
    private readonly System.Collections.Generic.List<StandardMaterial3D> _modelMats = new();
    private Basis _turretRest = Basis.Identity;

    public static readonly Color BlueColor = new(0.32f, 0.42f, 0.62f), RedColor = new(0.62f, 0.32f, 0.28f);

    public TankView(Soldier tank) { Tank = tank; }

    public override void _Ready()
    {
        var v = Tank.Vehicle!;
        var colour = Tank.Side == Side.Blue ? BlueColor : RedColor;
        if (ReadyModel(colour)) return;
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

    private bool ReadyModel(Color team)
    {
        var model = UnitModels.LoadTank();
        if (model == null) return false;
        var turret = model.FindChild("Turret", recursive: true, owned: false) as Node3D;
        if (turret == null)
        {
            GD.PushWarning($"{UnitModels.TankPath} has no node called Turret; the turret won't turn.");
            turret = new Node3D();
            model.AddChild(turret);
        }
        _turret = turret;
        _turretRest = turret.Basis;
        // Models face +Z and the game's forward is -Z, so the model sits inside a half-turned holder.
        var holder = new Node3D { Basis = UnitModels.FaceForward };
        holder.AddChild(model);
        AddChild(holder);
        _modelMats.AddRange(UnitModels.Paint(model, team));
        return true;
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
                if (_modelMats.Count > 0)
                    foreach (var m in _modelMats) { m.AlbedoColor = m.AlbedoColor.Darkened(0.75f); m.Metallic = 0f; }
                else _paint.AlbedoColor = new Color(0.13f, 0.12f, 0.11f);
                _turret.Basis = _turretRest * Basis.FromEuler(new Vector3(0.05f, 0.6f, 0.08f));
            }
            return;
        }
        // Turret yaw relative to the hull.
        _turret.Basis = _turretRest * new Basis(Vector3.Up, Draw.YawFor(Tank.TurretFacing) - Draw.YawFor(Tank.Facing));
    }
}

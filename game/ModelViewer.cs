using Godot;

namespace GroundWar.Game;

/// <summary>
/// A turntable for checking the unit models without running a battle: every soldier pose and the tank, in both
/// sides' colours, lit like the battlefield. Open game/ModelViewer.tscn in Godot and press F6 (run current scene).
/// Use it after changing a file in models/ to check size, facing, poses and team colour. See docs/models.md.
///
/// Mouse wheel zooms, left/right arrows turn the view, Space stops the turret turning.
/// Screenshot mode: godot res://game/ModelViewer.tscn -- --screenshot=out.png [--camera=yaw,pitch,distance]
/// </summary>
public partial class ModelViewer : Node3D
{
    private readonly System.Collections.Generic.List<Node3D> _turrets = new();
    private readonly System.Collections.Generic.List<Basis> _turretRest = new();
    private Camera3D _camera = null!;
    private float _yaw = 20f, _pitch = -24f, _distance = 17f, _turretAngle, _clock;
    private bool _turning = true;
    private string? _screenshot;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot=")) _screenshot = arg.Substring(13);
            if (arg.StartsWith("--camera="))
            {
                var p = System.Array.ConvertAll(arg.Substring(9).Split(','), v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
                _yaw = p[0]; if (p.Length > 1) _pitch = p[1]; if (p.Length > 2) _distance = p[2];
            }
        }
        if (_screenshot != null) _turning = false;

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial { SkyTopColor = new Color(0.45f, 0.55f, 0.68f), SkyHorizonColor = new Color(0.72f, 0.74f, 0.72f), GroundBottomColor = new Color(0.3f, 0.3f, 0.28f), GroundHorizonColor = new Color(0.72f, 0.74f, 0.72f) } },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.45f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, -35, 0), ShadowEnabled = true, LightEnergy = 0.9f });
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(60, 60) }, MaterialOverride = Draw.Lit(new Color(0.55f, 0.57f, 0.53f)) });
        _camera = new Camera3D { Fov = 40f };
        AddChild(_camera);

        // Soldiers: a row per side, in front of the tanks. Game axes: -Z is forward, so they face the camera's side (+Z).
        var poses = UnitModels.LoadSoldier();
        if (poses == null) AddLabel("No soldier model (models/soldier.glb)", new Vector3(0, 2, 3));
        else
        {
            string[] names = { "Standing", "Crouched", "Prone", "Dead" };
            var shader = Draw.LoadShader("res://game/shaders/team_paint.gdshader");
            for (int row = 0; row < 2; row++)
            {
                var team = row == 0 ? SoldierCrowd.BlueColor : SoldierCrowd.RedColor;
                for (int k = 0; k < names.Length; k++)
                {
                    var colour = k == 3 ? SoldierCrowd.DeadColor : team;
                    var at = new Vector3(-4.5f + k * 2.8f, 0, 2.5f + row * 3.2f);
                    // One-soldier batch: the team shader reads its colour from the instance data, as in battle.
                    var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = poses[names[k]].Mesh, InstanceCount = 1 };
                    mm.SetInstanceTransform(0, new Transform3D(new Basis(Vector3.Up, Mathf.Pi), at) * poses[names[k]].Transform);
                    mm.SetInstanceCustomData(0, colour.SrgbToLinear());
                    AddChild(new MultiMeshInstance3D { Multimesh = mm });
                    if (row == 0) AddLabel(names[k], at + new Vector3(0, 2.1f, 0));
                }
            }
        }

        // Tanks, one per side.
        for (int side = 0; side < 2; side++)
        {
            var tank = UnitModels.LoadTank();
            if (tank == null) { AddLabel("No tank model (models/tank.glb)", new Vector3(0, 2, -4)); break; }
            UnitModels.Paint(tank, side == 0 ? TankView.BlueColor : TankView.RedColor);
            tank.Position = new Vector3(side == 0 ? -4f : 5f, 0, -4.5f);
            tank.RotationDegrees = new Vector3(0, side == 0 ? 40f : -40f, 0);
            AddChild(tank);
            if (tank.FindChild("Turret", true, false) is Node3D t) { _turrets.Add(t); _turretRest.Add(t.Basis); }
        }
        UpdateCamera();
    }

    private void AddLabel(string text, Vector3 at) => AddChild(new Label3D
    {
        Text = text, Position = at, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, PixelSize = 0.006f, OutlineSize = 10,
    });

    private void UpdateCamera()
    {
        var target = new Vector3(0.5f, 0.9f, 0.8f);
        var dir = new Basis(Vector3.Up, Mathf.DegToRad(_yaw)) * new Basis(Vector3.Right, Mathf.DegToRad(_pitch)) * Vector3.Back;
        _camera.Position = target + dir * _distance;
        _camera.LookAt(target);
    }

    public override void _Process(double delta)
    {
        if (_turning) _turretAngle += (float)delta * 0.5f;
        else if (_screenshot != null) _turretAngle = 0.35f;
        for (int i = 0; i < _turrets.Count; i++) _turrets[i].Basis = _turretRest[i] * new Basis(Vector3.Up, i == 0 ? _turretAngle : -_turretAngle);
        if (Input.IsKeyPressed(Key.Left)) { _yaw -= 60f * (float)delta; }
        if (Input.IsKeyPressed(Key.Right)) { _yaw += 60f * (float)delta; }
        UpdateCamera();

        if (_screenshot == null) return;
        _clock += (float)delta;
        if (_clock < 1f) return;
        GetViewport().GetTexture().GetImage().SavePng(_screenshot);
        GD.Print($"Saved screenshot {_screenshot}");
        GetTree().Quit();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) _distance = Mathf.Max(3f, _distance * 0.9f);
            if (mb.ButtonIndex == MouseButton.WheelDown) _distance = Mathf.Min(60f, _distance * 1.1f);
        }
        if (e is InputEventKey { Pressed: true, Keycode: Key.Space }) _turning = !_turning;
    }
}

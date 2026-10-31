using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>
/// Entry point. Loads a scenario into the simulation, steps it at fixed ticks, and hands the result
/// to the views. Everything about the battle itself lives in the GroundWar.Sim project.
/// </summary>
public partial class Main : Node3D
{
    private static readonly string[] Scenarios = { "scenarios/firefight.json", "scenarios/meeting.json" };
    private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f, 8f };

    private int _scenario;
    private ulong _seed = 1;
    private Battle _battle = null!;
    private MapView? _mapView;
    private BattleView? _battleView;
    private CameraRig _camera = null!;
    private Hud _hud = null!;
    private float _accumulator;
    private int _speedIndex = 2;
    private bool _paused;
    private bool _stepOnce;

    public override void _Ready()
    {
        BuildEnvironment();
        _camera = new CameraRig { Name = "CameraRig" };
        AddChild(_camera);
        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
        StartBattle(frameCamera: true);

        // Screenshot mode, used for automated checks: godot -- --screenshot=out.png --seconds=40
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--seconds=")) _autoSeconds = float.Parse(arg.Substring(10), System.Globalization.CultureInfo.InvariantCulture);
            if (arg.StartsWith("--screenshot=")) _screenshotPath = arg.Substring(13);
            if (arg.StartsWith("--overlays=")) _autoOverlays = arg.Substring(11);
            if (arg.StartsWith("--seed=")) { _seed = ulong.Parse(arg.Substring(7)); StartBattle(true); }
            if (arg.StartsWith("--scenario=")) { _scenario = int.Parse(arg.Substring(11)); StartBattle(true); }
            if (arg.StartsWith("--camera=")) _autoCamera = arg.Substring(9);
            if (arg.StartsWith("--select=")) _autoSelect = int.Parse(arg.Substring(9));
        }
    }

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.45f, 0.55f, 0.68f),
            SkyHorizonColor = new Color(0.72f, 0.74f, 0.72f),
            GroundBottomColor = new Color(0.3f, 0.3f, 0.28f),
            GroundHorizonColor = new Color(0.72f, 0.74f, 0.72f),
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.45f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.62f, 0.65f, 0.66f),
            FogDensity = 0.0004f,
            GlowEnabled = true,
            GlowHdrThreshold = 1.2f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-50, -35, 0),
            ShadowEnabled = true,
            LightEnergy = 0.9f,
            DirectionalShadowMaxDistance = 400f,
        });
    }

    private void StartBattle(bool frameCamera)
    {
        _mapView?.QueueFree();
        _battleView?.QueueFree();
        _battle = Battle.Load(new GodotDataSource(), Scenarios[_scenario], _seed);
        _mapView = new MapView(_battle) { Name = "Map" };
        AddChild(_mapView);
        _battleView = new BattleView(_battle) { Name = "Battle" };
        AddChild(_battleView);
        _accumulator = 0;
        if (frameCamera) _camera.Frame(_battle.Map.Width, _battle.Map.Height);
        _camera.FollowTarget = null;
        GD.Print($"Started '{_battle.Scenario.Name}' seed {_seed}: {_battle.Soldiers.Count} soldiers, {_battle.Map.CoverPoints.Count} cover points");
    }

    public override void _Process(double delta)
    {
        float speed = Speeds[_speedIndex];
        if (!_paused || _stepOnce)
        {
            _accumulator += _stepOnce ? Battle.Dt : (float)delta * speed;
            _stepOnce = false;
            int steps = 0;
            while (_accumulator >= Battle.Dt && steps < 40)
            {
                _battleView!.BeforeStep();
                _battle.Step();
                _battleView.AfterStep(speed);
                _accumulator -= Battle.Dt;
                steps++;
            }
            if (steps == 40) _accumulator = 0; // can't keep up; slow down rather than spiral
        }
        _battleView!.UpdateVisuals(_accumulator / Battle.Dt, (float)delta, speed, _paused);
        _hud.UpdateHud(_battle, speed, _paused, _battleView.Selected, OverlayNames());
        RunAutomation((float)delta);
    }

    private string OverlayNames()
    {
        var parts = new List<string>();
        if (_mapView!.Grid != MapView.GridMode.Off) parts.Add($"grid: {_mapView.Grid.ToString().ToLowerInvariant()}");
        if (_mapView.CoverVisible) parts.Add("cover points");
        if (_battleView!.ShowLines) parts.Add("lines");
        if (_battleView.ShowAllLabels) parts.Add("thoughts");
        return string.Join(", ", parts);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Space: _paused = !_paused; break;
                case Key.Period: _paused = true; _stepOnce = true; break;
                case >= Key.Key1 and <= Key.Key6: _speedIndex = (int)(key.Keycode - Key.Key1); break;
                case Key.R: StartBattle(frameCamera: false); break;
                case Key.N: _seed++; StartBattle(frameCamera: false); break;
                case Key.Tab: _scenario = (_scenario + 1) % Scenarios.Length; StartBattle(frameCamera: true); break;
                case Key.G: _mapView!.CycleGrid(); break;
                case Key.C: _mapView!.ToggleCover(); break;
                case Key.L: _battleView!.ShowLines = !_battleView.ShowLines; break;
                case Key.T: _battleView!.ShowAllLabels = !_battleView.ShowAllLabels; break;
                case Key.B: _battleView!.ShowBars = !_battleView.ShowBars; break;
                case Key.H: _hud.ToggleHelp(); break;
                case Key.F:
                    if (_battleView!.Selected != null)
                        _camera.FollowTarget = _camera.FollowTarget == null ? _battleView.ViewOf(_battleView.Selected) : null;
                    break;
                case Key.Escape: _battleView!.Selected = null; _camera.FollowTarget = null; break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            var ground = _camera.GroundPointUnderMouse(mb.Position);
            if (ground != null)
            {
                _battleView!.Selected = _battleView.SoldierNear(ground.Value, 3f);
                if (_camera.FollowTarget != null && _battleView.Selected != null)
                    _camera.FollowTarget = _battleView.ViewOf(_battleView.Selected);
            }
        }
    }

    // ---------------------------------------------------------------- automated screenshots

    private float _autoSeconds = -1;
    private string? _screenshotPath;
    private string? _autoOverlays;
    private string? _autoCamera;
    private int _autoSelect = -1;
    private bool _autoApplied;
    private float _autoClock;

    private void RunAutomation(float delta)
    {
        if (_screenshotPath == null) return;
        if (!_autoApplied)
        {
            _autoApplied = true;
            _speedIndex = 5;
            if (_autoOverlays != null)
            {
                if (_autoOverlays.Contains('g')) _mapView!.CycleGrid();
                if (_autoOverlays.Contains('v')) { _mapView!.CycleGrid(); _mapView.CycleGrid(); }
                if (_autoOverlays.Contains('c')) _mapView!.ToggleCover();
                if (_autoOverlays.Contains('l')) _battleView!.ShowLines = true;
                if (_autoOverlays.Contains('t')) _battleView!.ShowAllLabels = true;
            }
            // --camera=x,y,distance,yaw,pitch
            if (_autoCamera != null)
            {
                var p = System.Array.ConvertAll(_autoCamera.Split(','), v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
                _camera.LookAt(new Vector3(p[0], 0, p[1]), p[2], p.Length > 3 ? p[3] : 0f, p.Length > 4 ? p[4] : -40f);
            }
        }
        if (_autoSelect >= 0) _battleView!.Selected = _battle.GetSoldier(_autoSelect);
        if (_battle.Time < _autoSeconds) return;
        _speedIndex = 0;
        _autoClock += delta;
        if (_autoClock < 0.6f) return; // let tracers and interpolation settle
        GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        GD.Print($"Saved screenshot {_screenshotPath} at battle time {_battle.Time:0.0}s");
        GetTree().Quit();
    }
}

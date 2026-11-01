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
    private static readonly string[] Scenarios = { "scenarios/squad_attack.json", "scenarios/firefight.json", "scenarios/meeting.json", "scenarios/town_attack.json", "scenarios/tank_support.json", "scenarios/orders_under_fire.json", "scenarios/farmhouse.json", "scenarios/farmhouse_support.json", "scenarios/battalion.json", "scenarios/tank_platoons.json", "scenarios/stress_1000.json" };
    private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f, 8f };

    private int _scenario;
    private ulong _seed = 1;
    private Battle _battle = null!;
    private MapView? _mapView;
    private BattleView? _battleView;
    private CommsView? _commsView;
    private CompanyView? _companyView;
    private SupportView? _supportView;
    private KeyPointView? _keyPointView;
    private SoundView? _soundView;
    private SmokeView? _smokeView;
    private TableView? _tableView;
    private bool _muted;
    private CameraRig _camera = null!;
    private Hud _hud = null!;
    private float _accumulator;
    private int _speedIndex = 2;
    private bool _paused;
    private bool _stepOnce;
    private bool _fog = true;

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
            if (arg == "--handover") _battle.PlayerHandOver(true); // your second in command runs the battle (put it after --seed / --scenario)
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

    /// <summary>Start (or restart) the battle. With <paramref name="replay"/>, the player's recorded commands are played
    /// back; with <paramref name="runUntil"/>, the battle is run on without drawing to that time first (a jump in a replay).</summary>
    private void StartBattle(bool frameCamera, IReadOnlyList<GroundWar.Sim.PlayerCommand>? replay = null, float runUntil = 0f)
    {
        _soundView?.QueueFree();
        _smokeView?.QueueFree();
        _tableView?.QueueFree();
        _reportShown = false;
        _actionCam = false;
        _mapView?.QueueFree();
        _battleView?.QueueFree();
        _commsView?.QueueFree();
        _companyView?.QueueFree();
        _supportView?.QueueFree();
        _keyPointView?.QueueFree();
        _pendingOrder = null;
        _battle = Battle.Load(new GodotDataSource(), Scenarios[_scenario], _seed);
        if (replay != null) _battle.LoadReplay(replay);
        if (runUntil > 0f) _battle.RunUntil(runUntil);
        _mapView = new MapView(_battle) { Name = "Map" };
        AddChild(_mapView);
        _battleView = new BattleView(_battle) { Name = "Battle", Fog = _fog };
        AddChild(_battleView);
        _commsView = new CommsView(_battle) { Name = "Comms", Fog = _fog };
        AddChild(_commsView);
        _companyView = new CompanyView(_battle) { Name = "Companies", Fog = _fog };
        AddChild(_companyView);
        _supportView = new SupportView(_battle) { Name = "Support", Fog = _fog };
        _keyPointView = new KeyPointView(_battle) { Name = "KeyPoints", Fog = _fog };
        AddChild(_keyPointView);
        AddChild(_supportView);
        _soundView = new SoundView(_battle, _camera.Camera) { Name = "Sound", Muted = _muted };
        AddChild(_soundView);
        _smokeView = new SmokeView(_battle) { Name = "Smoke" };
        AddChild(_smokeView);
        _tableView = new TableView(_battle) { Name = "Table", Fog = _fog };
        AddChild(_tableView);
        _battleView.ShellBurst = at => { _soundView?.Burst(at); _smokeView?.Burst(at); };
        _battleView.SmokeRound = at => _soundView?.SmokePop(at);
        _hud.OrderRequested = OnOrderButton;
        _hud.FireRequested = OnFireButton;
        _hud.ArtilleryRequested = kind => { _pendingArtillery = kind; _pendingOrder = null; };
        _hud.HandOverRequested = () => _battle.PlayerHandOver(!(_battle.BattalionOf(_battle.PlayerSide)?.Active ?? true));
        _hud.AttachRequested = OnAttachButton;
        _hud.ApproveRequested = c => _battle.PlayerApprove(c);
        _hud.RejectRequested = c => _battle.PlayerReject(c);
        _hud.HideReport();
        _accumulator = 0;
        if (frameCamera) _camera.Frame(_battle.Map.Width, _battle.Map.Height);
        _camera.FollowTarget = null;
        GD.Print($"Started '{_battle.Scenario.Name}' seed {_seed}: {_battle.Soldiers.Count} soldiers, {_battle.Map.CoverPoints.Count} cover points");
    }

    public override void _Process(double delta)
    {
        float speed = Speeds[_speedIndex];
        // Battle cam slow motion: a moment of drama near the man we're watching plays at a quarter speed.
        if (_slowmoUntil > 0f)
        {
            if (Time.GetTicksMsec() / 1000f < _slowmoUntil) speed = SlowMoSpeed;
            else _slowmoUntil = 0f;
        }
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
                _soundView!.AfterStep(speed);
                _actionCamera.AfterStep(_battle);
                _accumulator -= Battle.Dt;
                steps++;
            }
            if (steps == 40) _accumulator = 0; // can't keep up; slow down rather than spiral
        }
        long drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _battleView!.UpdateVisuals(_accumulator / Battle.Dt, (float)delta, speed, _paused);
        _drawMs += System.Diagnostics.Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;
        _drawFrames++;
        _commsView!.UpdateVisuals(Time.GetTicksMsec() / 1000f);
        _smokeView!.UpdateVisuals(_paused ? 0f : (float)delta * speed);
        _tableView!.UpdateVisuals(_camera.Distance);
        _soundView!.UpdateAudio((float)delta, _battleView.PlayerSide);
        UpdateActionCam((float)delta);
        if (_battle.Result != null && !_reportShown) { _reportShown = true; _hud.ShowReport(AfterActionReport.Build(_battle).ToText()); }
        _hud.UpdateHud(_battle, _slowmoUntil > 0f ? SlowMoSpeed : speed, _paused, _battleView.Selected, OverlayNames(), _battleView.Fog, _battleView.PlayerSide);
        _hud.UpdateOrders(_battle, _battleView.Selected, _battleView.PlayerSide, _pendingOrder, _pendingFire);
        _companyView!.UpdateVisuals();
        _supportView!.UpdateVisuals();
        _keyPointView!.UpdateVisuals();
        _hud.UpdateCompany(_battle, _battleView.Selected, _battleView.PlayerSide);
        _hud.UpdateBattalion(_battle, _battleView.PlayerSide, _fog, _pendingArtillery);
        RunAutomation((float)delta);
    }

    private string OverlayNames()
    {
        var parts = new List<string>();
        if (_mapView!.Grid != MapView.GridMode.Off) parts.Add($"grid: {_mapView.Grid.ToString().ToLowerInvariant()}");
        if (_mapView.CoverVisible) parts.Add("cover points");
        if (_battleView!.ShowLines) parts.Add("lines");
        if (_battleView.ShowPlans) parts.Add("plans");
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
                case Key.R when key.ShiftPressed:
                    // Replay: the same battle with everything you did, done again at the same moments.
                    var commands = new List<GroundWar.Sim.PlayerCommand>(_battle.Commands);
                    StartBattle(frameCamera: false, replay: commands);
                    break;
                case Key.R: StartBattle(frameCamera: false); break;
                case Key.Bracketleft:
                    {
                        // Back 30 s: replay from the start (with your commands) and run on to that moment.
                        var cmds = new List<GroundWar.Sim.PlayerCommand>(_battle.Commands);
                        StartBattle(frameCamera: false, replay: cmds, runUntil: Mathf.Max(0f, _battle.Time - 30f));
                        break;
                    }
                case Key.Bracketright: _battle.RunUntil(_battle.Time + 30f); break;
                case Key.M: _muted = !_muted; _soundView!.Muted = _muted; break;
                case Key.O: _hud.ToggleReport(AfterActionReport.Build(_battle).ToText()); break;
                case Key.Z:
                    if (_camera.Distance > TableView.FadeEnd) _camera.FlyTo(_camera.Position, 140f, 0f, -50f, 1.2f);
                    else _camera.ToTable();
                    break;
                case Key.J:
                    _actionCam = !_actionCam;
                    _actionClock = 999f;
                    if (!_actionCam) { _slowmoUntil = 0f; _camera.FollowTarget = null; }
                    break;
                case Key.N: _seed++; StartBattle(frameCamera: false); break;
                case Key.Tab: _scenario = (_scenario + 1) % Scenarios.Length; StartBattle(frameCamera: true); break;
                case Key.G: _mapView!.CycleGrid(); break;
                case Key.C: _mapView!.ToggleCover(); break;
                case Key.L: _battleView!.ShowLines = !_battleView.ShowLines; break;
                case Key.P: _battleView!.ShowPlans = !_battleView.ShowPlans; break;
                case Key.V: _fog = !_fog; _battleView!.Fog = _fog; _commsView!.Fog = _fog; _companyView!.Fog = _fog; _supportView!.Fog = _fog; _keyPointView!.Fog = _fog; _tableView!.Fog = _fog; break;
                case Key.Enter or Key.KpEnter:
                    if (_hud.ShownCompany is { } ac) _battle.PlayerApprove(ac);
                    break;
                case Key.Backspace:
                    if (_hud.ShownCompany is { } rc) _battle.PlayerReject(rc);
                    break;
                case Key.K: _battle.PlayerHandOver(!(_battle.BattalionOf(_battle.PlayerSide)?.Active ?? true)); break;
                case Key.Y when _battle.ArtilleryFor(_battle.PlayerSide, GroundWar.Sim.Comms.FireKind.He) != null:
                    _pendingArtillery = GroundWar.Sim.Comms.FireKind.He; _pendingOrder = null; break;
                case Key.U when _battle.ArtilleryFor(_battle.PlayerSide, GroundWar.Sim.Comms.FireKind.Smoke) != null:
                    _pendingArtillery = GroundWar.Sim.Comms.FireKind.Smoke; _pendingOrder = null; break;
                case Key.T: _battleView!.ShowAllLabels = !_battleView.ShowAllLabels; break;
                case Key.B: _battleView!.ShowBars = !_battleView.ShowBars; break;
                case Key.H: _hud.ToggleHelp(); break;
                case Key.F:
                    if (_battleView!.Selected != null)
                        _camera.FollowTarget = _camera.FollowTarget == null ? _battleView.SelectedAnchor : null;
                    break;
                case Key.Escape:
                    if (_pendingOrder != null || _pendingArtillery != null) { _pendingOrder = null; _pendingArtillery = null; break; }
                    _battleView!.Selected = null; _camera.FollowTarget = null; break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            var ground = _camera.GroundPointUnderMouse(mb.Position);
            if (ground != null && _pendingArtillery is { } artyKind)
            {
                // Second click of an artillery call: the target. It goes from HQ to the guns by radio.
                _battle.PlayerArtillery(new System.Numerics.Vector2(ground.Value.X, ground.Value.Z), artyKind);
                _pendingArtillery = null;
            }
            else if (ground != null && _pendingOrder != null && _battleView!.Selected != null)
            {
                var at = new System.Numerics.Vector2(ground.Value.X, ground.Value.Z);
                var own = _battleView.Selected.Group;
                switch (_pendingOrder.Value)
                {
                    case GroundWar.Sim.Comms.OrderKind.FireMission:
                        // Second click of a fire mission: the target. It goes to the mortar section itself.
                        int rounds = _pendingFire == GroundWar.Sim.Comms.FireKind.Smoke ? _battle.Rules.Support.SmokeRounds : _battle.Rules.Support.PrepRounds;
                        _battle.PlayerOrder(own, new GroundWar.Sim.Comms.Order { Kind = GroundWar.Sim.Comms.OrderKind.FireMission, Objective = at, Fire = _pendingFire, Rounds = rounds, Radius = 10f });
                        break;
                    case GroundWar.Sim.Comms.OrderKind.Attach:
                        // Second click of attach: any man of the company it is to join.
                        var man = _battleView.SoldierNear(ground.Value, 4f);
                        if (man?.Group.Company is not { } company || man.Side != own.Side) return;
                        _battle.PlayerOrder(own, new GroundWar.Sim.Comms.Order { Kind = GroundWar.Sim.Comms.OrderKind.Attach, AttachTo = company });
                        break;
                    default:
                        // Second click of an order: where it's to. A squad in a company is ordered through its commander.
                        var g = own.Company?.HqGroup ?? own;
                        _battle.PlayerOrder(g, new GroundWar.Sim.Comms.Order { Kind = _pendingOrder.Value, Objective = at, Radius = 18f });
                        break;
                }
                _pendingOrder = null;
            }
            else if (ground != null)
            {
                _battleView!.Selected = _battleView.SoldierNear(ground.Value, 3f);
            }
        }
    }

    // ---------------------------------------------------------------- orders

    /// <summary>An order waiting for its second click (where to), or null.</summary>
    private GroundWar.Sim.Comms.OrderKind? _pendingOrder;
    /// <summary>An artillery call waiting for its target click (HE or smoke), or null.</summary>
    private GroundWar.Sim.Comms.FireKind? _pendingArtillery;
    /// <summary>For a pending fire mission: HE or smoke.</summary>
    private GroundWar.Sim.Comms.FireKind _pendingFire;

    /// <summary>The player pressed an order button in the HUD. Halt and Fall back go at once; the rest need a place.</summary>
    private void OnOrderButton(GroundWar.Sim.Comms.OrderKind kind)
    {
        var s = _battleView!.Selected;
        if (s == null || s.Side != _battleView.PlayerSide) return;
        if (kind is GroundWar.Sim.Comms.OrderKind.Halt or GroundWar.Sim.Comms.OrderKind.FallBack)
        {
            _battle.PlayerOrder(s.Group.Company?.HqGroup ?? s.Group, new GroundWar.Sim.Comms.Order { Kind = kind });
            _pendingOrder = null;
        }
        else _pendingOrder = kind;
    }

    /// <summary>The player pressed HE or Smoke with a mortar section selected: the next click is the target.</summary>
    private void OnFireButton(GroundWar.Sim.Comms.FireKind kind)
    {
        var s = _battleView!.Selected;
        if (s == null || s.Side != _battleView.PlayerSide || s.Group.Mortars == null) return;
        _pendingFire = kind;
        _pendingOrder = GroundWar.Sim.Comms.OrderKind.FireMission;
    }

    /// <summary>Attach (then click a man of the company) or, for a section already attached, back under battalion.</summary>
    private void OnAttachButton(bool detach)
    {
        var s = _battleView!.Selected;
        if (s == null || s.Side != _battleView.PlayerSide || !s.Group.IsAsset) return;
        if (detach)
        {
            _battle.PlayerOrder(s.Group, new GroundWar.Sim.Comms.Order { Kind = GroundWar.Sim.Comms.OrderKind.Attach, AttachTo = null });
            _pendingOrder = null;
        }
        else _pendingOrder = GroundWar.Sim.Comms.OrderKind.Attach;
    }

    // ---------------------------------------------------------------- battle cam (milestone 6)

    private const float SlowMoSpeed = 0.25f;
    private readonly ActionCamera _actionCamera = new();
    private bool _actionCam, _reportShown, _autoReport;
    private float _actionClock, _slowmoUntil;

    /// <summary>J: the camera picks the fiercest fighting your men are in, flies down behind one of them and follows
    /// him for a few seconds, then moves on. A man hit close by slows time for a moment.</summary>
    private void UpdateActionCam(float delta)
    {
        if (!_actionCam) return;
        _actionClock += delta;
        var focus = _battleView!.Selected;
        if (focus != null && _actionCamera.DramaNear(focus, _battle) && _slowmoUntil == 0f && !_paused)
            _slowmoUntil = Time.GetTicksMsec() / 1000f + 2.5f;
        bool stale = focus == null || !focus.IsActive || _actionClock > 9f;
        if (!stale) return;
        var next = _actionCamera.PickHotspot(_battle, _battleView.PlayerSide, _fog);
        _actionClock = 0f;
        if (next == null) return;
        _battleView.Selected = next;
        var behind = -next.Facing;
        float yaw = Mathf.RadToDeg(Mathf.Atan2(behind.X, behind.Y)) + (float)GD.RandRange(-35, 35);
        _camera.FollowTarget = _battleView.SelectedAnchor;
        _camera.FlyTo(Draw.To3(next.Position), (float)GD.RandRange(16, 30), yaw, (float)GD.RandRange(-22, -12), 1.6f);
    }

    // ---------------------------------------------------------------- automated screenshots

    private float _autoSeconds = -1;
    private string? _screenshotPath;
    private string? _autoOverlays;
    private string? _autoCamera;
    private int _autoSelect = -1;
    private bool _autoApplied;
    private float _autoClock;
    private double _drawMs;
    private int _drawFrames;

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
                if (_autoOverlays.Contains('f')) { _fog = false; _battleView!.Fog = false; _commsView!.Fog = false; _companyView!.Fog = false; _supportView!.Fog = false; _keyPointView!.Fog = false; _tableView!.Fog = false; }
                if (_autoOverlays.Contains('j')) { _actionCam = true; _actionClock = 999f; }
                if (_autoOverlays.Contains('r')) _autoReport = true;
            }
            // --camera=x,y,distance,yaw,pitch
            if (_autoCamera != null)
            {
                var p = System.Array.ConvertAll(_autoCamera.Split(','), v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
                _camera.LookAt(new Vector3(p[0], 0, p[1]), p[2], p.Length > 3 ? p[3] : 0f, p.Length > 4 ? p[4] : -40f);
            }
        }
        if (_autoSelect >= 0) _battleView!.Selected = _battle.GetSoldier(_autoSelect);
        if (_battle.Time < _autoSeconds && _battle.Result == null) return; // a battle that ends early is shot at its end
        if (_autoReport && !_reportShown) { _reportShown = true; _hud.ShowReport(AfterActionReport.Build(_battle).ToText()); }
        _speedIndex = 0;
        _autoClock += delta;
        if (_autoClock < 0.6f) return; // let tracers and interpolation settle
        GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
        GD.Print($"Saved screenshot {_screenshotPath} at battle time {_battle.Time:0.0}s; " +
                 $"updating the soldiers took {_drawMs / System.Math.Max(_drawFrames, 1):0.00} ms a frame, " +
                 $"{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} draw calls, " +
                 $"{Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} nodes");
        GetTree().Quit();
    }
}

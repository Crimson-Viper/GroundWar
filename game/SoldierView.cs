using Godot;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>Grey-box soldier: a capsule body, a rifle stick showing where he faces, a cap for sergeants,
/// and a floating bar (morale on top, suppression below).</summary>
public partial class SoldierView : Node3D
{
    public Soldier Soldier { get; }
    private readonly Materials _mats;
    private MeshInstance3D _body = null!;
    private MeshInstance3D _rifle = null!;
    private MeshInstance3D? _cap;
    private MeshInstance3D _bar = null!;
    private Label3D _label = null!;
    private Stance _shownStance = (Stance)(-1);
    private SoldierStatus _shownStatus = (SoldierStatus)(-1);

    public sealed class Materials
    {
        public required Material Blue, Red, BlueDown, RedDown, Dead, Rifle, Cap;
        public required Shader BarShader;
        public required Mesh Body, RifleMesh, CapMesh, BarMesh;
    }

    public SoldierView(Soldier soldier, Materials mats)
    {
        Soldier = soldier;
        _mats = mats;
    }

    public override void _Ready()
    {
        _body = new MeshInstance3D { Mesh = _mats.Body, MaterialOverride = Soldier.Side == Side.Blue ? _mats.Blue : _mats.Red };
        AddChild(_body);
        _rifle = new MeshInstance3D { Mesh = _mats.RifleMesh, MaterialOverride = _mats.Rifle };
        AddChild(_rifle);
        _bar = new MeshInstance3D
        {
            Mesh = _mats.BarMesh,
            MaterialOverride = new ShaderMaterial { Shader = _mats.BarShader },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0, 2.4f, 0),
        };
        AddChild(_bar);
        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FixedSize = true,
            PixelSize = 0.0009f,
            FontSize = 22,
            OutlineSize = 8,
            Position = new Vector3(0, 2.9f, 0),
            Visible = false,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        AddChild(_label);
    }

    public void SetLabelVisible(bool v) => _label.Visible = v;
    public void SetBarVisible(bool v) => _bar.Visible = v && Soldier.IsActive;

    public void UpdateVisual(Vector3 position, bool showBar)
    {
        var s = Soldier;
        Position = position;
        Rotation = new Vector3(0, Draw.YawFor(s.Facing), 0);

        if (s.IsLeader && _cap == null)
        {
            _cap = new MeshInstance3D { Mesh = _mats.CapMesh, MaterialOverride = _mats.Cap };
            AddChild(_cap);
            _shownStance = (Stance)(-1);
        }
        else if (!s.IsLeader && _cap != null)
        {
            _cap.QueueFree();
            _cap = null;
        }

        if (s.Status != _shownStatus)
        {
            _shownStatus = s.Status;
            _body.MaterialOverride = s.Status switch
            {
                SoldierStatus.Dead => _mats.Dead,
                SoldierStatus.Wounded => s.Side == Side.Blue ? _mats.BlueDown : _mats.RedDown,
                _ => s.Side == Side.Blue ? _mats.Blue : _mats.Red,
            };
            _rifle.Visible = s.IsActive;
            if (s.Status == SoldierStatus.Routed) Visible = false;
            _shownStance = (Stance)(-1);
        }

        var stance = s.IsActive ? s.Stance : Stance.Prone;
        if (stance != _shownStance)
        {
            _shownStance = stance;
            switch (stance)
            {
                case Stance.Standing:
                    _body.Position = new Vector3(0, 0.875f, 0);
                    _body.Rotation = Vector3.Zero;
                    _body.Scale = Vector3.One;
                    _rifle.Position = new Vector3(0.2f, 1.3f, -0.45f);
                    break;
                case Stance.Crouched:
                    _body.Position = new Vector3(0, 0.6f, 0);
                    _body.Rotation = Vector3.Zero;
                    _body.Scale = new Vector3(1, 0.68f, 1);
                    _rifle.Position = new Vector3(0.2f, 0.95f, -0.45f);
                    break;
                default:
                    _body.Position = new Vector3(0, 0.28f, 0.3f);
                    _body.Rotation = new Vector3(Mathf.DegToRad(-90f), 0, 0);
                    _body.Scale = Vector3.One;
                    _rifle.Position = new Vector3(0.2f, 0.3f, -0.85f);
                    break;
            }
            if (_cap != null)
                _cap.Position = stance switch
                {
                    Stance.Standing => new Vector3(0, 1.8f, 0),
                    Stance.Crouched => new Vector3(0, 1.22f, 0),
                    _ => new Vector3(0, 0.3f, -0.6f),
                };
            _bar.Position = new Vector3(0, stance == Stance.Prone ? 1.2f : 2.4f, 0);
            _label.Position = _bar.Position + new Vector3(0, 0.45f, 0);
        }

        _bar.Visible = showBar && s.IsActive;
        if (_bar.Visible)
        {
            _bar.SetInstanceShaderParameter("morale", s.Morale / 100f);
            _bar.SetInstanceShaderParameter("suppression", s.Suppression / 100f);
            _bar.SetInstanceShaderParameter("bar_color", MoraleColor(s.MoraleState));
        }
        if (_label.Visible)
        {
            _label.Text = $"{s.Label} {s.MoraleState}\n{s.Thought}";
            _label.Modulate = s.IsActive ? MoraleColor(s.MoraleState).Lightened(0.3f) : new Color(0.7f, 0.7f, 0.7f);
        }
    }

    public static Color MoraleColor(MoraleState m) => m switch
    {
        MoraleState.Steady => new Color(0.3f, 0.9f, 0.3f),
        MoraleState.Shaken => new Color(0.95f, 0.85f, 0.2f),
        MoraleState.Pinned => new Color(1f, 0.5f, 0.1f),
        _ => new Color(1f, 0.15f, 0.15f),
    };
}

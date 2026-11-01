using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>
/// Draws every soldier in the battle. Each soldier is a capsule body, a rifle stick showing where he faces,
/// a cap for sergeants and a floating bar (morale on top, suppression below).
///
/// All the bodies are one MultiMesh, all the rifles another, and so on, so the graphics card draws a whole
/// battalion in a handful of calls instead of several nodes per man. A part that should not show this frame
/// gets a zero-size transform. Name labels are real Label3D nodes, but only for the soldiers that need one.
/// </summary>
public partial class SoldierCrowd : Node3D
{
    private readonly Battle _battle;
    private readonly int _count;

    /// <summary>Where each soldier is drawn this frame (between the last two sim ticks).</summary>
    public Vector3[] Positions { get; }
    /// <summary>Is each soldier drawn this frame (fog of war, routed men leave the map).</summary>
    public bool[] Shown { get; }

    private MultiMesh _bodies = null!, _rifles = null!, _caps = null!, _bars = null!;
    private MultiMeshInstance3D _barNode = null!;
    private float[] _bodyBuf = null!, _rifleBuf = null!, _capBuf = null!, _barBuf = null!;
    private readonly Dictionary<int, Label3D> _labels = new();

    // Floats per instance in each buffer: 12 for the transform, then 4 for colour, then 4 for custom data.
    private const int BodyStride = 16, PartStride = 12, BarStride = 20;

    private static readonly Color BlueColor = new(0.25f, 0.42f, 0.85f), RedColor = new(0.82f, 0.25f, 0.22f);
    private static readonly Color BlueDown = new(0.15f, 0.2f, 0.4f), RedDown = new(0.4f, 0.13f, 0.12f);
    private static readonly Color DeadColor = new(0.18f, 0.18f, 0.18f);

    /// <summary>Where each part sits relative to the soldier's feet, for each stance.</summary>
    private readonly struct Pose
    {
        public readonly Transform3D Body, Rifle, Cap;
        public readonly float BarHeight;
        public Pose(Transform3D body, Vector3 rifle, Vector3 cap, float barHeight)
        {
            Body = body;
            Rifle = new Transform3D(Basis.Identity, rifle);
            Cap = new Transform3D(Basis.Identity, cap);
            BarHeight = barHeight;
        }
    }

    private static readonly Pose Standing = new(new Transform3D(Basis.Identity, new Vector3(0, 0.875f, 0)),
        new Vector3(0.2f, 1.3f, -0.45f), new Vector3(0, 1.8f, 0), 2.4f);
    private static readonly Pose Crouched = new(new Transform3D(Basis.FromScale(new Vector3(1, 0.68f, 1)), new Vector3(0, 0.6f, 0)),
        new Vector3(0.2f, 0.95f, -0.45f), new Vector3(0, 1.22f, 0), 2.4f);
    private static readonly Pose Prone = new(new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(-90f)), new Vector3(0, 0.28f, 0.3f)),
        new Vector3(0.2f, 0.3f, -0.85f), new Vector3(0, 0.3f, -0.6f), 1.2f);

    public SoldierCrowd(Battle battle)
    {
        _battle = battle;
        _count = battle.Soldiers.Count;
        Positions = new Vector3[_count];
        Shown = new bool[_count];
    }

    public override void _Ready()
    {
        var bodyMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.9f };
        _bodies = Add(new CapsuleMesh { Radius = 0.28f, Height = 1.75f, RadialSegments = 12, Rings = 4 }, bodyMat, colors: true, custom: false, shadows: true, out _);
        _rifles = Add(new BoxMesh { Size = new Vector3(0.07f, 0.07f, 1.1f) }, Draw.Lit(new Color(0.2f, 0.15f, 0.1f)), false, false, true, out _);
        _caps = Add(new BoxMesh { Size = new Vector3(0.42f, 0.14f, 0.42f) }, Draw.Lit(new Color(0.95f, 0.8f, 0.2f)), false, false, true, out _);
        var barMat = new ShaderMaterial { Shader = Draw.LoadShader("res://game/shaders/status_bar.gdshader") };
        _bars = Add(new QuadMesh { Size = new Vector2(1.3f, 0.26f) }, barMat, true, true, false, out _barNode);

        _bodyBuf = new float[_count * BodyStride];
        _rifleBuf = new float[_count * PartStride];
        _capBuf = new float[_count * PartStride];
        _barBuf = new float[_count * BarStride];
    }

    private MultiMesh Add(Mesh mesh, Material mat, bool colors, bool custom, bool shadows, out MultiMeshInstance3D node)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = colors,
            UseCustomData = custom,
            Mesh = mesh,
            InstanceCount = _count,
            // The battle never leaves the map, so one fixed box saves Godot working it out every frame.
            CustomAabb = new Aabb(new Vector3(-10, -5, -10), new Vector3(_battle.Map.Width + 20, 20, _battle.Map.Height + 20)),
        };
        node = new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = mat,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(node);
        return mm;
    }

    /// <summary>Move every soldier to his drawn position and refresh his parts.</summary>
    /// <param name="visible">Which soldiers the player may see.</param>
    public void UpdateAll(System.Func<Soldier, bool> visible, System.Numerics.Vector2[] prev, System.Numerics.Vector2[] curr,
        float alpha, bool showBars, bool showAllLabels, Soldier? selected)
    {
        var soldiers = _battle.Soldiers;
        for (int i = 0; i < _count; i++)
        {
            var s = soldiers[i];
            var p = System.Numerics.Vector2.Lerp(prev[i], curr[i], alpha);
            var pos = Draw.To3(p);
            Positions[i] = pos;
            bool shown = s.Status != SoldierStatus.Routed && visible(s);
            Shown[i] = shown;
            if (!shown || s.IsTank) // tanks are drawn by TankView
            {
                Hide(_bodyBuf, i * BodyStride);
                Hide(_rifleBuf, i * PartStride);
                Hide(_capBuf, i * PartStride);
                Hide(_barBuf, i * BarStride);
                continue;
            }

            var pose = s.IsActive ? s.Stance switch { Stance.Standing => Standing, Stance.Crouched => Crouched, _ => Prone } : Prone;
            var root = new Transform3D(new Basis(Vector3.Up, Draw.YawFor(s.Facing)), pos);

            int b = i * BodyStride;
            Put(_bodyBuf, b, root * pose.Body);
            var c = s.Status switch
            {
                SoldierStatus.Dead => DeadColor,
                SoldierStatus.Wounded => s.Side == Side.Blue ? BlueDown : RedDown,
                _ => s.Side == Side.Blue ? BlueColor : RedColor,
            };
            _bodyBuf[b + 12] = c.R; _bodyBuf[b + 13] = c.G; _bodyBuf[b + 14] = c.B; _bodyBuf[b + 15] = 1f;

            if (s.IsActive) Put(_rifleBuf, i * PartStride, root * pose.Rifle);
            else Hide(_rifleBuf, i * PartStride);

            if (s.IsLeader) Put(_capBuf, i * PartStride, root * pose.Cap);
            else Hide(_capBuf, i * PartStride);

            int r = i * BarStride;
            if (showBars && s.IsActive)
            {
                Put(_barBuf, r, new Transform3D(Basis.Identity, pos + new Vector3(0, pose.BarHeight, 0)));
                var mc = MoraleColor(s.MoraleState);
                _barBuf[r + 12] = mc.R; _barBuf[r + 13] = mc.G; _barBuf[r + 14] = mc.B; _barBuf[r + 15] = 1f;
                _barBuf[r + 16] = s.Morale / 100f;
                _barBuf[r + 17] = s.Suppression / 100f;
                _barBuf[r + 18] = 0f;
                _barBuf[r + 19] = 0f;
            }
            else Hide(_barBuf, r);
        }

        RenderingServer.MultimeshSetBuffer(_bodies.GetRid(), _bodyBuf);
        RenderingServer.MultimeshSetBuffer(_rifles.GetRid(), _rifleBuf);
        RenderingServer.MultimeshSetBuffer(_caps.GetRid(), _capBuf);
        RenderingServer.MultimeshSetBuffer(_bars.GetRid(), _barBuf);
        _barNode.Visible = showBars;

        UpdateLabels(showAllLabels, selected);
    }

    private void UpdateLabels(bool all, Soldier? selected)
    {
        // Labels are expensive, so only the soldiers that need one get one.
        foreach (var (id, label) in _labels)
            label.Visible = Shown[id] && (all || selected?.Id == id);
        if (all)
            for (int i = 0; i < _count; i++) { if (Shown[i]) ShowLabel(i); }
        else if (selected != null && Shown[selected.Id])
            ShowLabel(selected.Id);
    }

    private void ShowLabel(int id)
    {
        if (!_labels.TryGetValue(id, out var label))
        {
            label = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FixedSize = true,
                PixelSize = 0.0009f,
                FontSize = 22,
                OutlineSize = 8,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            AddChild(label);
            _labels[id] = label;
        }
        var s = _battle.Soldiers[id];
        label.Visible = true;
        bool low = !s.IsActive || s.Stance == Stance.Prone;
        label.Position = Positions[id] + new Vector3(0, low ? 1.65f : 2.85f, 0);
        label.Text = $"{s.Label} {s.MoraleState}\n{s.Thought}";
        label.Modulate = s.IsActive ? MoraleColor(s.MoraleState).Lightened(0.3f) : new Color(0.7f, 0.7f, 0.7f);
    }

    /// <summary>Write a transform into a MultiMesh buffer: three rows of [basis | origin].</summary>
    private static void Put(float[] buf, int o, in Transform3D t)
    {
        var bs = t.Basis;
        buf[o + 0] = bs.X.X; buf[o + 1] = bs.Y.X; buf[o + 2] = bs.Z.X; buf[o + 3] = t.Origin.X;
        buf[o + 4] = bs.X.Y; buf[o + 5] = bs.Y.Y; buf[o + 6] = bs.Z.Y; buf[o + 7] = t.Origin.Y;
        buf[o + 8] = bs.X.Z; buf[o + 9] = bs.Y.Z; buf[o + 10] = bs.Z.Z; buf[o + 11] = t.Origin.Z;
    }

    /// <summary>A zero-size transform: the part is not drawn.</summary>
    private static void Hide(float[] buf, int o)
    {
        for (int k = 0; k < 12; k++) buf[o + k] = 0f;
    }

    public static Color MoraleColor(MoraleState m) => m switch
    {
        MoraleState.Steady => new Color(0.3f, 0.9f, 0.3f),
        MoraleState.Shaken => new Color(0.95f, 0.85f, 0.2f),
        MoraleState.Pinned => new Color(1f, 0.5f, 0.1f),
        _ => new Color(1f, 0.15f, 0.15f),
    };
}

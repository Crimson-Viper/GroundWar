using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Comms;
using Side = GroundWar.Sim.Units.Side;

namespace GroundWar.Game;

/// <summary>
/// Draws the communications of milestone 4: each HQ (a command tent with a flag), field telephone cables
/// (dark on the ground, red where cut), runners carrying messages, a pulse running along the cable for a phone
/// call, rings spreading from the radio aerial, and flares over HQ. See docs/systems/messages-and-delay.md.
/// </summary>
public partial class CommsView : Node3D
{
    private const int MaxRunners = 64, MaxPulses = 64;
    private readonly Battle _battle;
    private MultiMesh _runners = null!, _pulses = null!, _rings = null!;
    private Node3D? _lines;
    private int _cutCount = -1;
    private MeshInstance3D? _flare;

    public Side PlayerSide { get; set; } = Side.Blue;
    public bool Fog { get; set; } = true;

    private static readonly Color BlueColor = new(0.25f, 0.42f, 0.85f), RedColor = new(0.82f, 0.25f, 0.22f);

    public CommsView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        foreach (var side in new[] { Side.Blue, Side.Red })
            if (_battle.Hq(side) is { } hq) AddChild(BuildHq(hq));
        _runners = Pool(new CapsuleMesh { Radius = 0.3f, Height = 1.6f }, Draw.VertexColored(false), MaxRunners, colors: true);
        _pulses = Pool(new SphereMesh { Radius = 0.6f, Height = 1.2f }, Draw.Glow(new Color(1f, 0.95f, 0.5f), 2f), MaxPulses, colors: false);
        _rings = Pool(new TorusMesh { InnerRadius = 0.96f, OuterRadius = 1f, Rings = 24, RingSegments = 4 },
            Draw.Unlit(new Color(0.6f, 0.9f, 1f, 0.7f), transparent: true), MaxPulses, colors: false);
        _flare = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.2f, Height = 2.4f }, MaterialOverride = Draw.Glow(new Color(1f, 0.3f, 0.2f), 6f), Visible = false };
        AddChild(_flare);
    }

    private MultiMesh Pool(Mesh mesh, Material mat, int count, bool colors)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colors, Mesh = mesh, InstanceCount = count, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return mm;
    }

    private Node3D BuildHq(Headquarters hq)
    {
        var n = new Node3D { Name = hq.Name, Position = Draw.To3(hq.Position) };
        var col = hq.Side == Side.Blue ? BlueColor : RedColor;
        // A tent: a box with a sloped roof, a flagpole and a flag in the side's colour, and a radio mast.
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(4f, 1.6f, 3f) }, Position = new Vector3(0, 0.8f, 0), MaterialOverride = Draw.Lit(new Color(0.42f, 0.4f, 0.3f)) });
        n.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(4.2f, 1.2f, 3.2f) }, Position = new Vector3(0, 2.2f, 0), MaterialOverride = Draw.Lit(new Color(0.36f, 0.35f, 0.26f)) });
        n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 6f }, Position = new Vector3(2.6f, 3f, 0), MaterialOverride = Draw.Lit(new Color(0.3f, 0.25f, 0.2f)) });
        n.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.6f, 1f, 0.05f) }, Position = new Vector3(3.4f, 5.4f, 0), MaterialOverride = Draw.Lit(col) });
        if (hq.HasRadio)
            n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.03f, Height = 8f }, Position = new Vector3(-2.4f, 4f, 1f), MaterialOverride = Draw.Lit(new Color(0.2f, 0.2f, 0.2f)) });
        var label = new Label3D { Text = hq.Name, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true, PixelSize = 0.0009f, FontSize = 24, OutlineSize = 8, NoDepthTest = true, Position = new Vector3(0, 7.5f, 0), Modulate = col.Lightened(0.4f) };
        n.AddChild(label);
        return n;
    }

    private bool Shows(Side side) => !Fog || side == PlayerSide;

    public void UpdateVisuals(float realTime)
    {
        // Cables: rebuilt when one is cut.
        int cut = 0;
        foreach (var l in _battle.PhoneLines) if (l.Cut) cut++;
        if (cut != _cutCount) RebuildLines(cut);

        int r = 0, p = 0, rings = 0;
        float flareUntil = -1f;
        System.Numerics.Vector2? flareAt = null;
        foreach (var m in _battle.Messages)
        {
            if (m.Status != MessageStatus.InTransit || !Shows(m.Side)) continue;
            var hq = _battle.Hq(m.Side)!;
            switch (m.Method)
            {
                case MessageMethod.Runner when r < MaxRunners:
                    var col = m.Side == Side.Blue ? BlueColor.Lightened(0.35f) : RedColor.Lightened(0.35f);
                    _runners.SetInstanceTransform(r, new Transform3D(Basis.Identity, Draw.To3(m.RunnerPosition, 0.8f)));
                    _runners.SetInstanceColor(r, col);
                    r++;
                    break;
                case MessageMethod.Phone when p < MaxPulses:
                    var line = LineFor(m);
                    if (line != null)
                    {
                        float t = Mathf.Clamp((_battle.Time - m.SentTime) / Mathf.Max(m.ArriveTime - m.SentTime, 0.1f), 0f, 1f);
                        if (!m.Down) t = 1f - t;
                        _pulses.SetInstanceTransform(p++, new Transform3D(Basis.Identity, Draw.To3(AlongLine(line, t), 0.6f)));
                    }
                    break;
                case MessageMethod.Radio when rings + 1 < MaxPulses:
                    // Rings spread from the sender's end every couple of seconds while the message is being worked.
                    var from = m.Down ? hq.Position : Battle.SquadPost(m.Group);
                    float phase = (realTime * 0.6f + m.Id * 0.37f) % 1f;
                    var basis = Basis.FromScale(new Vector3(2f + phase * 18f, 1f, 2f + phase * 18f));
                    _rings.SetInstanceTransform(rings++, new Transform3D(basis, Draw.To3(new System.Numerics.Vector2(from.X, from.Y), 1f)));
                    break;
                case MessageMethod.Flare:
                    flareAt = hq.Position;
                    flareUntil = m.ArriveTime;
                    float rise = 1f - (m.ArriveTime - _battle.Time) / _battle.Rules.Comms.FlareDelay;
                    _flare!.Position = Draw.To3(hq.Position, 6f + 40f * Mathf.Sin(Mathf.Clamp(rise, 0f, 1f) * Mathf.Pi));
                    break;
            }
        }
        _runners.VisibleInstanceCount = r;
        _pulses.VisibleInstanceCount = p;
        _rings.VisibleInstanceCount = rings;
        _flare!.Visible = flareAt != null && _battle.Time < flareUntil;
    }

    private PhoneLine? LineFor(Message m)
    {
        PhoneLine? best = null;
        float bd = float.MaxValue;
        var post = Battle.SquadPost(m.Group);
        foreach (var l in _battle.PhoneLines)
        {
            if (l.Side != m.Side) continue;
            float d = System.Numerics.Vector2.DistanceSquared(l.End, post);
            if (d < bd) { bd = d; best = l; }
        }
        return best;
    }

    private static System.Numerics.Vector2 AlongLine(PhoneLine l, float t)
    {
        float total = 0f;
        for (int i = 0; i + 1 < l.Points.Count; i++) total += System.Numerics.Vector2.Distance(l.Points[i], l.Points[i + 1]);
        float want = total * t;
        for (int i = 0; i + 1 < l.Points.Count; i++)
        {
            float seg = System.Numerics.Vector2.Distance(l.Points[i], l.Points[i + 1]);
            if (want <= seg) return System.Numerics.Vector2.Lerp(l.Points[i], l.Points[i + 1], seg > 0f ? want / seg : 0f);
            want -= seg;
        }
        return l.End;
    }

    private void RebuildLines(int cut)
    {
        _cutCount = cut;
        _lines?.QueueFree();
        _lines = new Node3D { Name = "PhoneLines" };
        var ok = new List<Transform3D>();
        var broken = new List<Transform3D>();
        var ends = new List<Transform3D>();
        foreach (var l in _battle.PhoneLines)
        {
            if (!Shows(l.Side)) continue;
            for (int i = 0; i + 1 < l.Points.Count; i++)
            {
                var a = l.Points[i]; var b = l.Points[i + 1];
                float len = System.Numerics.Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                var mid = (a + b) * 0.5f;
                var basis = new Basis(Vector3.Up, Draw.YawFor(System.Numerics.Vector2.Normalize(b - a))) * Basis.FromScale(new Vector3(0.12f, 0.06f, len));
                (l.Cut ? broken : ok).Add(new Transform3D(basis, Draw.To3(mid, 0.05f)));
            }
            // A field telephone box at the far end.
            ends.Add(new Transform3D(Basis.Identity, Draw.To3(l.End, 0.25f)));
        }
        var box = new BoxMesh { Size = Vector3.One };
        if (ok.Count > 0) _lines.AddChild(Draw.Batch(box, Draw.Unlit(new Color(0.12f, 0.1f, 0.08f)), ok, shadows: false));
        if (broken.Count > 0) _lines.AddChild(Draw.Batch(box, Draw.Unlit(new Color(0.85f, 0.15f, 0.1f)), broken, shadows: false));
        if (ends.Count > 0) _lines.AddChild(Draw.Batch(new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.4f) }, Draw.Lit(new Color(0.25f, 0.3f, 0.2f)), ends));
        AddChild(_lines);
    }
}

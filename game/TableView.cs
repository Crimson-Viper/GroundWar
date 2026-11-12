using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Map;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;
using SVec2 = System.Numerics.Vector2;

namespace GroundWar.Game;

/// <summary>
/// The war-room map table (milestone 6). Zoom the camera out far enough and the battlefield fades into a paper map
/// on the battalion commander's table: hedges, walls and shell holes inked in, a 50 m grid, a wooden counter for
/// each of your squads with its strength, and red pencil marks where HQ believes the enemy is. Zoom back in and the
/// table fades away into the real battlefield. Z jumps straight to the table. See docs/systems/spectacle.md.
/// </summary>
public partial class TableView : Node3D
{
    private float Height = 4.2f; // above walls, men, hilltops and trees; below HQ flags and labels
    private readonly Battle _battle;
    private MeshInstance3D _paper = null!;
    private StandardMaterial3D _paperMat = null!;
    private readonly Dictionary<Group, (MeshInstance3D Block, Label3D Label)> _counters = new();
    private MultiMesh _marks = null!;
    private StandardMaterial3D _counterMat = null!, _enemyCounterMat = null!, _markMat = null!;

    public Side PlayerSide { get; set; } = Side.Blue;
    public bool Fog { get; set; } = true;
    /// <summary>0 = the battlefield, 1 = the paper map.</summary>
    public float Opacity { get; private set; }

    /// <summary>Camera distances between which the table fades in.</summary>
    public const float FadeStart = 260f, FadeEnd = 360f;

    public TableView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        float w = _battle.Map.Width, h = _battle.Map.Height;
        var terrain = _battle.Map.Terrain;
        Height = 4.2f + terrain.MaxHeight + (terrain.Trees.Count > 0 ? 9f : 0f);
        _paperMat = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(Paint()),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        // The table: a big dark wooden board under a sheet of map paper.
        var board = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w + 120f, 0.5f, h + 120f) }, Position = new Vector3(w / 2, Height - 0.4f, h / 2),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.14f, 0.08f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Name = "Board",
        };
        AddChild(board);
        _paper = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w, h) }, Position = new Vector3(w / 2, Height, h / 2), MaterialOverride = _paperMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_paper);

        _counterMat = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.45f, 0.75f) };
        _enemyCounterMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.3f, 0.25f) };
        _markMat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.12f, 0.1f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _marks = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new BoxMesh { Size = new Vector3(3.2f, 0.1f, 0.6f) }, InstanceCount = 512, VisibleInstanceCount = 0 };
        AddChild(new MultiMeshInstance3D { Multimesh = _marks, MaterialOverride = _markMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        Visible = false;
    }

    /// <summary>The paper map, inked from the battle map once at the start (into a byte buffer: fast).</summary>
    private Image Paint()
    {
        float w = _battle.Map.Width, h = _battle.Map.Height;
        float scale = Mathf.Min(4f, 2048f / Mathf.Max(w, h));
        var ink = new Ink((int)(w * scale), (int)(h * scale), scale);
        var rnd = new System.Random(7);
        for (int i = 0; i < ink.Data.Length; i += 4)
        {
            int n = rnd.Next(-6, 7);
            ink.Data[i] = (byte)(219 + n); ink.Data[i + 1] = (byte)(206 + n); ink.Data[i + 2] = (byte)(168 + n); ink.Data[i + 3] = 255;
        }
        var grid = new Color(0.62f, 0.58f, 0.46f);
        for (float gx = 0; gx <= w; gx += 50f) ink.Line(new SVec2(gx, 0), new SVec2(gx, h), 0.6f, grid);
        for (float gy = 0; gy <= h; gy += 50f) ink.Line(new SVec2(0, gy), new SVec2(w, gy), 0.6f, grid);
        // Woods in green, crops in straw, and form lines every 2 m of height, as on a staff map.
        var terrain = _battle.Map.Terrain;
        if (terrain.HasWoods || terrain.HasCrops || !terrain.Flat)
        {
            for (int py = 0; py < ink.H; py++)
            for (int px = 0; px < ink.W; px++)
            {
                var p = new SVec2((px + 0.5f) / scale, (py + 0.5f) / scale);
                var kind = terrain.KindAt(p);
                if (kind == GroundKind.Woods) ink.Tint(px, py, new Color(0.45f, 0.62f, 0.38f), 0.55f);
                else if (kind == GroundKind.Crops) ink.Tint(px, py, new Color(0.86f, 0.74f, 0.36f), 0.45f);
                if (!terrain.Flat)
                {
                    float hh = terrain.HeightAt(p);
                    float hx = terrain.HeightAt(p + new SVec2(1f / scale, 0)), hy = terrain.HeightAt(p + new SVec2(0, 1f / scale));
                    if (Mathf.FloorToInt(hh / 2f) != Mathf.FloorToInt(hx / 2f) || Mathf.FloorToInt(hh / 2f) != Mathf.FloorToInt(hy / 2f))
                        ink.Tint(px, py, new Color(0.55f, 0.36f, 0.2f), 0.7f);
                }
            }
            foreach (var t in terrain.Trees) if (terrain.KindAt(t.Position) != GroundKind.Woods) ink.Ring(t.Position, 1.2f, new Color(0.3f, 0.45f, 0.25f));
        }
        foreach (var c in _battle.Map.Craters) ink.Ring(c.Center, c.Radius, new Color(0.45f, 0.38f, 0.28f));
        foreach (var wall in _battle.Map.Walls)
            ink.Line(wall.A, wall.B, wall.High || wall.Hedge ? 1.6f : 0.9f, wall.High ? new Color(0.18f, 0.15f, 0.12f) : wall.Hedge ? new Color(0.2f, 0.36f, 0.16f) : new Color(0.35f, 0.42f, 0.25f));
        return Image.CreateFromData(ink.W, ink.H, false, Image.Format.Rgba8, ink.Data);
    }

    private sealed class Ink
    {
        public readonly int W, H;
        public readonly float Scale;
        public readonly byte[] Data;
        public Ink(int w, int h, float scale) { W = w; H = h; Scale = scale; Data = new byte[w * h * 4]; }

        public void Tint(int x, int y, Color c, float amount)
        {
            int o = (y * W + x) * 4;
            Data[o] = (byte)Mathf.Lerp(Data[o], c.R * 255, amount); Data[o + 1] = (byte)Mathf.Lerp(Data[o + 1], c.G * 255, amount);
            Data[o + 2] = (byte)Mathf.Lerp(Data[o + 2], c.B * 255, amount);
        }

        private void Dot(int x, int y, int r, Color c)
        {
            for (int yy = y - r; yy <= y + r; yy++)
            for (int xx = x - r; xx <= x + r; xx++)
            {
                if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                int o = (yy * W + xx) * 4;
                Data[o] = (byte)(c.R * 255); Data[o + 1] = (byte)(c.G * 255); Data[o + 2] = (byte)(c.B * 255);
            }
        }

        public void Line(SVec2 a, SVec2 b, float width, Color c)
        {
            int steps = Mathf.Max(1, (int)(SVec2.Distance(a, b) * Scale));
            int r = Mathf.Max(0, (int)(width * Scale * 0.5f) - 1);
            for (int i = 0; i <= steps; i++)
            {
                var p = SVec2.Lerp(a, b, i / (float)steps) * Scale;
                Dot((int)p.X, (int)p.Y, r, c);
            }
        }

        public void Ring(SVec2 centre, float radius, Color c)
        {
            int steps = Mathf.Max(8, (int)(radius * Scale * 6));
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Tau * i / steps;
                var p = (centre + new SVec2(Mathf.Cos(a), Mathf.Sin(a)) * radius) * Scale;
                Dot((int)p.X, (int)p.Y, 0, c);
            }
        }
    }

    /// <summary>Each frame: fade with the camera's distance and move the counters.</summary>
    public void UpdateVisuals(float cameraDistance)
    {
        Opacity = Mathf.Clamp((cameraDistance - FadeStart) / (FadeEnd - FadeStart), 0f, 1f);
        Visible = Opacity > 0.01f;
        if (!Visible) return;
        _paperMat.AlbedoColor = new Color(1, 1, 1, Opacity);
        ((StandardMaterial3D)((MeshInstance3D)GetNode("Board")).MaterialOverride).AlbedoColor = new Color(0.22f, 0.14f, 0.08f, Opacity);
        bool counters = Opacity > 0.4f;

        foreach (var g in _battle.Groups)
        {
            bool mine = g.Side == PlayerSide;
            bool show = counters && g.CountEffective > 0 && (mine || !Fog);
            if (!_counters.TryGetValue(g, out var ctr))
            {
                if (!show) continue;
                var block = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(9f, 1.2f, 6f) }, MaterialOverride = mine ? _counterMat : _enemyCounterMat };
                var label = new Label3D
                {
                    FontSize = 40, OutlineSize = 0, PixelSize = 0.06f, Modulate = new Color(0.08f, 0.06f, 0.04f), RotationDegrees = new Vector3(-90, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector3(0, 0.7f, 0),
                };
                block.AddChild(label);
                AddChild(block);
                ctr = (block, label);
                _counters[g] = ctr;
            }
            ctr.Block.Visible = show;
            if (!show) continue;
            ctr.Block.Position = Draw.To3Flat(g.Centroid, Height + 0.7f);
            string name = g.IsCompanyHq ? $"{g.Company!.Name} HQ" : g.Name.Replace(" Squad", "");
            ctr.Label.Text = g.IsVehicles ? $"{name}\n▲" : $"{name}\n{g.CountEffective}/{g.Members.Count}";
        }

        // Red pencil crosses where HQ thinks the enemy is (with fog; without it the enemy has counters).
        int n = 0;
        if (counters && Fog)
        {
            foreach (var c in _battle.Knowledge(PlayerSide).Contacts)
            {
                if (n + 2 > _marks.InstanceCount) break;
                var at = Draw.To3Flat(c.LastPosition, Height + 0.1f);
                _marks.SetInstanceTransform(n++, new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 4), at));
                _marks.SetInstanceTransform(n++, new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 4), at));
            }
        }
        _marks.VisibleInstanceCount = n;
        _markMat.AlbedoColor = new Color(0.75f, 0.12f, 0.1f, Opacity);
    }
}

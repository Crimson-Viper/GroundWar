using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Map;
using GroundWar.Sim.Units;
using Side = GroundWar.Sim.Units.Side;
using SVec2 = System.Numerics.Vector2;

namespace GroundWar.Game;

/// <summary>Draws the battlefield: ground, walls, craters, objectives, and the grid and cover overlays.</summary>
public partial class MapView : Node3D
{
    public enum GridMode { Off, Cover, Visibility, Control }

    private readonly Battle _battle;
    private MultiMeshInstance3D _gridMesh = null!;
    private MultiMeshInstance3D _coverMesh = null!;
    private GridMode _gridMode = GridMode.Off;
    private float _refreshTimer;

    public GridMode Grid => _gridMode;
    public bool CoverVisible => _coverMesh.Visible;

    public MapView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        BuildGround();
        BuildWalls();
        BuildCraters();
        BuildObjectives();
        BuildGridOverlay();
        BuildCoverOverlay();
    }

    public void CycleGrid()
    {
        _gridMode = (GridMode)(((int)_gridMode + 1) % 4);
        _gridMesh.Visible = _gridMode != GridMode.Off;
        RefreshGrid();
    }

    public void ToggleCover()
    {
        _coverMesh.Visible = !_coverMesh.Visible;
        RefreshCover();
    }

    public override void _Process(double delta)
    {
        _refreshTimer -= (float)delta;
        if (_refreshTimer > 0) return;
        _refreshTimer = 0.25f;
        if (_gridMode == GridMode.Control) RefreshGrid();
        if (_coverMesh.Visible) RefreshCover();
    }

    // ---------------------------------------------------------------- static scenery

    private void BuildGround()
    {
        var map = _battle.Map;
        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(map.Width + 400, map.Height + 400) },
            Position = new Vector3(map.Width * 0.5f, 0, map.Height * 0.5f),
        };
        var mat = new ShaderMaterial { Shader = Draw.LoadShader("res://game/shaders/ground.gdshader") };
        mat.SetShaderParameter("map_size", new Vector2(map.Width, map.Height));
        ground.MaterialOverride = mat;
        AddChild(ground);
    }

    private void BuildWalls()
    {
        var low = Draw.Lit(new Color(0.62f, 0.60f, 0.55f));
        var high = Draw.Lit(new Color(0.48f, 0.45f, 0.42f));
        foreach (var w in _battle.Map.Walls)
        {
            var mid = (w.A + w.B) * 0.5f;
            float h = w.HeightMetres;
            var box = Draw.Box(new Vector3(w.Thickness, h, w.Length + w.Thickness), w.High ? high : low);
            box.Name = $"Wall{w.Id}";
            box.Position = Draw.To3(mid, h * 0.5f);
            box.Rotation = new Vector3(0, Draw.YawFor(w.Direction), 0);
            AddChild(box);
        }
    }

    private void BuildCraters()
    {
        var floor = Draw.Lit(new Color(0.28f, 0.26f, 0.23f));
        var rim = Draw.Lit(new Color(0.36f, 0.34f, 0.30f));
        foreach (var c in _battle.Map.Craters)
        {
            var disc = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = c.Radius, BottomRadius = c.Radius, Height = 0.04f, RadialSegments = 24 },
                MaterialOverride = floor,
                Position = Draw.To3(c.Center, 0.02f),
            };
            AddChild(disc);
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = c.Radius * 0.92f, OuterRadius = c.Radius * 1.3f, Rings = 24, RingSegments = 8 },
                MaterialOverride = rim,
                Position = Draw.To3(c.Center, 0f),
                Scale = new Vector3(1, 0.35f, 1),
            };
            AddChild(ring);
        }
    }

    private void BuildObjectives()
    {
        foreach (var g in _battle.Groups)
        {
            var color = g.Side == Side.Blue ? new Color(0.35f, 0.55f, 1f, 0.35f) : new Color(1f, 0.35f, 0.3f, 0.35f);
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = g.ObjectiveRadius - 0.25f, OuterRadius = g.ObjectiveRadius + 0.25f, Rings = 64, RingSegments = 4 },
                MaterialOverride = Draw.Unlit(color, transparent: true),
                Position = Draw.To3(g.Objective, 0.05f + g.Id * 0.02f),
                Scale = new Vector3(1, 0.1f, 1),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(ring);
            // A flag for the attacker's objective.
            if (g.Order == GroupOrder.Advance)
            {
                var pole = Draw.Box(new Vector3(0.1f, 5f, 0.1f), Draw.Lit(new Color(0.2f, 0.2f, 0.2f)));
                pole.Position = Draw.To3(g.Objective, 2.5f);
                AddChild(pole);
                var flag = Draw.Box(new Vector3(0.05f, 1f, 1.6f), Draw.Unlit(new Color(color, 1f)));
                flag.Position = Draw.To3(g.Objective, 4.5f) + new Vector3(0, 0, 0.8f);
                AddChild(flag);
            }
        }
    }

    // ---------------------------------------------------------------- overlays

    private void BuildGridOverlay()
    {
        var grid = _battle.Grid;
        float size = grid.CellSize * 0.94f;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            InstanceCount = grid.Width * grid.Height,
        };
        for (int y = 0; y < grid.Height; y++)
        for (int x = 0; x < grid.Width; x++)
        {
            var c = grid.CellCenter(x, y);
            mm.SetInstanceTransform(y * grid.Width + x, new Transform3D(Basis.Identity, Draw.To3(c, 0.08f)));
        }
        _gridMesh = new MultiMeshInstance3D
        {
            Name = "GridOverlay",
            Multimesh = mm,
            MaterialOverride = Draw.VertexColored(transparent: true),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_gridMesh);
    }

    private void RefreshGrid()
    {
        if (_gridMode == GridMode.Off) return;
        var grid = _battle.Grid;
        var mm = _gridMesh.Multimesh;
        for (int i = 0; i < grid.Width * grid.Height; i++)
        {
            Color c = _gridMode switch
            {
                GridMode.Cover => grid.Cover[i] <= 0f ? new Color(0, 0, 0, 0.05f) : new Color(0.2f, 0.9f, 0.3f, 0.15f + 0.5f * grid.Cover[i]),
                GridMode.Visibility => new Color(1f, 0.9f, 0.3f, 0.05f + 0.45f * grid.Visibility[i]),
                _ => grid.Control[i] < 0
                    ? new Color(0.3f, 0.5f, 1f, 0.6f * -grid.Control[i])
                    : new Color(1f, 0.3f, 0.25f, 0.6f * grid.Control[i]),
            };
            mm.SetInstanceColor(i, c);
        }
    }

    private void BuildCoverOverlay()
    {
        var points = _battle.Map.CoverPoints;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.2f, 1.2f) },
            InstanceCount = points.Count,
        };
        for (int i = 0; i < points.Count; i++)
        {
            var cp = points[i];
            // Each marker is a short bar pointing towards the side it protects from. Crater markers are small squares.
            var basis = cp.Facing == SVec2.Zero
                ? Basis.Identity.Scaled(new Vector3(1.6f, 1f, 0.6f))
                : new Basis(Vector3.Up, Draw.YawFor(cp.Facing));
            mm.SetInstanceTransform(i, new Transform3D(basis, Draw.To3(cp.Position, 0.1f)));
        }
        _coverMesh = new MultiMeshInstance3D
        {
            Name = "CoverOverlay",
            Multimesh = mm,
            MaterialOverride = Draw.VertexColored(transparent: false),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_coverMesh);
    }

    private void RefreshCover()
    {
        var points = _battle.Map.CoverPoints;
        var mm = _coverMesh.Multimesh;
        for (int i = 0; i < points.Count; i++)
        {
            var cp = points[i];
            Color c = cp.ClaimedBy >= 0 ? new Color(1f, 0.6f, 0.1f)
                : cp.Kind switch
                {
                    CoverKind.LowWall => new Color(0.2f, 0.9f, 0.3f),
                    CoverKind.Crater => new Color(0.8f, 0.9f, 0.2f),
                    _ => cp.CanFire ? new Color(0.2f, 0.9f, 0.9f) : new Color(0.3f, 0.4f, 1f),
                };
            mm.SetInstanceColor(i, c);
        }
    }
}

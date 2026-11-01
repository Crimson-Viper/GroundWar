using System.Collections.Generic;
using Godot;
using GroundWar.Sim;
using GroundWar.Sim.Units;

namespace GroundWar.Game;

/// <summary>
/// Lingering smoke (milestone 6): clouds that rise and drift downwind from shell and bazooka bursts and hang
/// about for half a minute, and a column of black smoke with a fire glow from every knocked-out tank. Purely
/// visual: smoke doesn't block sight in the simulation (yet). See docs/systems/spectacle.md.
/// </summary>
public partial class SmokeView : Node3D
{
    private const int Max = 600;
    private readonly Battle _battle;
    private readonly List<Cloud> _clouds = new();
    private MultiMesh _mm = null!;
    private float[] _buf = System.Array.Empty<float>();
    private readonly Dictionary<int, OmniLight3D> _fires = new();
    private float _emit;
    /// <summary>Which way the wind blows (metres a second).</summary>
    public Vector3 Wind { get; set; } = new(0.8f, 0f, 0.35f);

    private sealed class Cloud
    {
        public Vector3 At, Velocity;
        public float Age, Life, Size, Grow, Alpha;
        public Color Color;
    }

    public SmokeView(Battle battle) { _battle = battle; }

    public override void _Ready()
    {
        var mat = new ShaderMaterial { Shader = Draw.LoadShader("res://game/shaders/smoke.gdshader") };
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new QuadMesh { Size = Vector2.One },
            CustomAabb = new Aabb(new Vector3(-100, -10, -100), new Vector3(_battle.Map.Width + 200, 120, _battle.Map.Height + 200)),
        };
        AddChild(new MultiMeshInstance3D { Multimesh = _mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>A shell or rocket burst: a dirty brown-grey cloud that billows up and drifts away.</summary>
    public void Burst(Vector3 at)
    {
        for (int i = 0; i < 7; i++)
        {
            var off = new Vector3((float)GD.RandRange(-1.5, 1.5), (float)GD.RandRange(0.5, 2.5), (float)GD.RandRange(-1.5, 1.5));
            float grey = (float)GD.RandRange(0.35, 0.5);
            Add(at + off, new Vector3(off.X * 0.3f, (float)GD.RandRange(0.4, 1.0), off.Z * 0.3f), (float)GD.RandRange(18, 32),
                (float)GD.RandRange(3, 5), 0.35f, 0.55f, new Color(grey, grey * 0.94f, grey * 0.86f));
        }
    }

    private void Add(Vector3 at, Vector3 velocity, float life, float size, float grow, float alpha, Color color)
    {
        if (_clouds.Count >= Max) _clouds.RemoveAt(0);
        _clouds.Add(new Cloud { At = at, Velocity = velocity, Life = life, Size = size, Grow = grow, Alpha = alpha, Color = color });
    }

    /// <summary>Called every frame with game seconds passed.</summary>
    public void UpdateVisuals(float dt)
    {
        // Burning wrecks: a steady column of black smoke and a flickering glow.
        _emit += dt;
        bool emit = _emit >= 0.4f;
        if (emit) _emit = 0f;
        foreach (var s in _battle.Soldiers)
        {
            if (!s.IsTank || s.Status != SoldierStatus.Dead) continue;
            var at = Draw.To3(s.Position, 2.2f);
            if (emit)
            {
                float g = (float)GD.RandRange(0.08, 0.16);
                Add(at + new Vector3((float)GD.RandRange(-0.6, 0.6), 0, (float)GD.RandRange(-0.6, 0.6)), new Vector3(0, (float)GD.RandRange(1.5, 2.5), 0),
                    (float)GD.RandRange(14, 22), 2.2f, 0.45f, 0.75f, new Color(g, g, g));
            }
            if (!_fires.TryGetValue(s.Id, out var fire))
            {
                fire = new OmniLight3D { LightColor = new Color(1f, 0.5f, 0.2f), OmniRange = 12f, Position = at };
                AddChild(fire);
                _fires[s.Id] = fire;
            }
            fire.LightEnergy = 1.5f + 0.8f * Mathf.Sin(Time.GetTicksMsec() * 0.023f) * Mathf.Sin(Time.GetTicksMsec() * 0.011f);
        }

        int n = 0;
        for (int i = _clouds.Count - 1; i >= 0; i--)
        {
            var c = _clouds[i];
            c.Age += dt;
            if (c.Age >= c.Life) { _clouds.RemoveAt(i); continue; }
            c.At += (c.Velocity + Wind * Mathf.Min(1f, c.Age / 4f)) * dt;
            c.Velocity *= Mathf.Exp(-dt * 0.3f);
        }
        if (_mm.InstanceCount < _clouds.Count)
        {
            _mm.InstanceCount = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)System.Math.Max(64, _clouds.Count));
            _buf = new float[_mm.InstanceCount * 16];
        }
        foreach (var c in _clouds)
        {
            float t = c.Age / c.Life;
            float size = c.Size * (1f + c.Grow * c.Age);
            float alpha = c.Alpha * Mathf.Min(1f, c.Age * 3f) * (1f - t) * (1f - t);
            int o = n * 16;
            // Transform3D as 12 floats (basis rows with origin), then colour.
            _buf[o + 0] = size; _buf[o + 1] = 0; _buf[o + 2] = 0; _buf[o + 3] = c.At.X;
            _buf[o + 4] = 0; _buf[o + 5] = size; _buf[o + 6] = 0; _buf[o + 7] = c.At.Y;
            _buf[o + 8] = 0; _buf[o + 9] = 0; _buf[o + 10] = size; _buf[o + 11] = c.At.Z;
            _buf[o + 12] = c.Color.R; _buf[o + 13] = c.Color.G; _buf[o + 14] = c.Color.B; _buf[o + 15] = alpha;
            n++;
        }
        _mm.VisibleInstanceCount = n;
        if (n > 0) RenderingServer.MultimeshSetBuffer(_mm.GetRid(), _buf);
    }
}

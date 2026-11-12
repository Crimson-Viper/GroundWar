using Godot;
using SVec2 = System.Numerics.Vector2;

namespace GroundWar.Game;

/// <summary>Small helpers for building grey-box meshes and converting sim positions to 3D.</summary>
public static class Draw
{
    /// <summary>The simulation is 2D (x, y on the ground). Godot is 3D with y up, so sim y becomes Godot z.</summary>
    /// <paramref name="height"/> is height above the ground at that spot, so everything sits on the hills.
    public static Vector3 To3(SVec2 p, float height = 0f) => new(p.X, GroundAt(p) + height, p.Y);

    /// <summary>The same, ignoring the hills: for things at a fixed level, like the map table.</summary>
    public static Vector3 To3Flat(SVec2 p, float height = 0f) => new(p.X, height, p.Y);

    /// <summary>The battle's terrain, set by Main when a battle loads (null in the model viewer).</summary>
    public static GroundWar.Sim.Map.Terrain? Ground;

    public static float GroundAt(SVec2 p) => Ground?.HeightAt(p) ?? 0f;
    /// <summary>A basis tilted to lie on the slope at a spot (for craters and other things flat on the ground).</summary>
    public static Basis GroundTilt(SVec2 p)
    {
        if (Ground == null || Ground.Flat) return Basis.Identity;
        float hx = Ground.HeightAt(p + new SVec2(2, 0)) - Ground.HeightAt(p - new SVec2(2, 0));
        float hy = Ground.HeightAt(p + new SVec2(0, 2)) - Ground.HeightAt(p - new SVec2(0, 2));
        var up = new Vector3(-hx, 4f, -hy).Normalized();
        var right = new Vector3(1, 0, 0).Slide(up).Normalized();
        return new Basis(right, up, right.Cross(up));
    }

    public static float GroundAt(Vector3 p) => Ground?.HeightAt(new SVec2(p.X, p.Z)) ?? 0f;

    public static StandardMaterial3D Lit(Color c, float roughness = 0.9f) => new()
    {
        AlbedoColor = c,
        Roughness = roughness,
    };

    public static StandardMaterial3D Unlit(Color c, bool transparent = false) => new()
    {
        AlbedoColor = c,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public static StandardMaterial3D VertexColored(bool transparent) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    public static StandardMaterial3D Glow(Color c, float energy = 3f) => new()
    {
        AlbedoColor = c,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = energy,
    };

    public static MeshInstance3D Box(Vector3 size, Material mat, bool shadows = true)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat };
        if (!shadows) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return mi;
    }

    /// <summary>Yaw (rotation about Y) that makes a node's -Z face the given ground direction.</summary>
    public static float YawFor(SVec2 dir) => Mathf.Atan2(-dir.X, -dir.Y);

    /// <summary>Many copies of one mesh drawn in a single call, for things that never move (walls, craters).</summary>
    public static MultiMeshInstance3D Batch(Mesh mesh, Material mat, System.Collections.Generic.IReadOnlyList<Transform3D> transforms, bool shadows = true)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = transforms.Count };
        for (int i = 0; i < transforms.Count; i++) mm.SetInstanceTransform(i, transforms[i]);
        return new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = mat,
            CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public static Shader LoadShader(string path) => GD.Load<Shader>(path);
}

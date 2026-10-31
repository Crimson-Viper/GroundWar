using Godot;
using SVec2 = System.Numerics.Vector2;

namespace GroundWar.Game;

/// <summary>Small helpers for building grey-box meshes and converting sim positions to 3D.</summary>
public static class Draw
{
    /// <summary>The simulation is 2D (x, y on the ground). Godot is 3D with y up, so sim y becomes Godot z.</summary>
    public static Vector3 To3(SVec2 p, float height = 0f) => new(p.X, height, p.Y);

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

    public static Shader LoadShader(string path) => GD.Load<Shader>(path);
}

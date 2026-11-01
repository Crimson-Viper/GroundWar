using System.Collections.Generic;
using Godot;

namespace GroundWar.Game;

/// <summary>
/// Loads the 3D models for soldiers and tanks from models/ (see docs/models.md).
///
/// The rules a model has to follow, so an artist can drop in a replacement:
/// - glTF (.glb/.gltf), 1 unit = 1 metre, Y up, feet/tracks on Y = 0, facing +Z (Blender's front view).
/// - Any material whose name contains "Team" is painted in the side's colour.
/// - soldier.glb holds one mesh object per pose, named Standing, Crouched, Prone and Dead.
///   Only Standing is required; a missing pose falls back to the closest one.
/// - tank.glb holds the hull (any name) and a node named "Turret" that the game turns.
///   Anything under the Turret node turns with it (gun, cupola, hatches).
/// If a file is missing the game draws the old grey boxes instead, so nothing breaks.
/// </summary>
public static class UnitModels
{
    public const string SoldierPath = "res://models/soldier.glb";
    public const string TankPath = "res://models/tank.glb";

    /// <summary>Models face +Z; the game treats -Z as forward. This turns one into the other.</summary>
    public static readonly Basis FaceForward = new(Vector3.Up, Mathf.Pi);

    /// <summary>A pose of the soldier: the mesh, and where it sits relative to the soldier's feet.</summary>
    public readonly record struct Part(Mesh Mesh, Transform3D Transform);

    public static bool IsTeam(Material? m) =>
        m != null && m.ResourceName.Contains("team", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>The soldier's poses by name, or null if there is no soldier model.</summary>
    public static Dictionary<string, Part>? LoadSoldier()
    {
        var scene = Load(SoldierPath);
        if (scene == null) return null;
        var root = scene.Instantiate<Node3D>();
        var poses = new Dictionary<string, Part>();
        var teamShader = Draw.LoadShader("res://game/shaders/team_paint.gdshader");
        foreach (var name in new[] { "Standing", "Crouched", "Prone", "Dead" })
        {
            var mi = FindMesh(root, name);
            if (mi?.Mesh == null) continue;
            // Each pose is drawn for the whole battle in one batch, so the team colour can't be a material
            // per soldier. Team surfaces get a shader that reads the colour from each soldier's instance data.
            var mesh = (Mesh)mi.Mesh.Duplicate();
            for (int i = 0; i < mesh.GetSurfaceCount(); i++)
            {
                var mat = mi.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i);
                if (IsTeam(mat)) mesh.SurfaceSetMaterial(i, TeamPaint(teamShader, mat as BaseMaterial3D));
                else if (mat != null) mesh.SurfaceSetMaterial(i, mat);
            }
            poses[name] = new Part(mesh, new Transform3D(FaceForward, Vector3.Zero) * RelativeTransform(mi, root));
        }
        root.Free();
        if (!poses.ContainsKey("Standing"))
        {
            GD.PushWarning($"{SoldierPath} has no mesh called Standing; using the grey-box soldier.");
            return null;
        }
        if (!poses.ContainsKey("Crouched")) poses["Crouched"] = poses["Standing"];
        if (!poses.ContainsKey("Prone")) poses["Prone"] = poses["Crouched"];
        if (!poses.ContainsKey("Dead")) poses["Dead"] = poses["Prone"];
        return poses;
    }

    /// <summary>A fresh copy of the tank model, or null if there is none. The caller owns it.</summary>
    public static Node3D? LoadTank()
    {
        var scene = Load(TankPath);
        return scene?.Instantiate<Node3D>();
    }

    /// <summary>Give every mesh under a model its own copy of its materials, with the "Team" ones painted in the
    /// side's colour. Returns the copies, so the caller can change them later (a knocked-out tank goes dark).</summary>
    public static List<StandardMaterial3D> Paint(Node model, Color team)
    {
        var mats = new List<StandardMaterial3D>();
        foreach (var node in model.FindChildren("*", "MeshInstance3D", recursive: true, owned: false))
        {
            var mi = (MeshInstance3D)node;
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                if ((mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i)) is not StandardMaterial3D src) continue;
                var mat = (StandardMaterial3D)src.Duplicate();
                if (IsTeam(src)) mat.AlbedoColor = src.AlbedoColor * team;
                mi.SetSurfaceOverrideMaterial(i, mat);
                mats.Add(mat);
            }
        }
        return mats;
    }

    private static PackedScene? Load(string path)
    {
        if (!ResourceLoader.Exists(path)) return null;
        var scene = GD.Load<PackedScene>(path);
        if (scene == null) GD.PushWarning($"Couldn't load {path}; using grey boxes.");
        return scene;
    }

    private static ShaderMaterial TeamPaint(Shader shader, BaseMaterial3D? from)
    {
        var m = new ShaderMaterial { Shader = shader };
        if (from != null)
        {
            m.SetShaderParameter("albedo", from.AlbedoColor);
            if (from.AlbedoTexture != null) m.SetShaderParameter("albedo_texture", from.AlbedoTexture);
            m.SetShaderParameter("roughness", from.Roughness);
        }
        return m;
    }

    /// <summary>Find a mesh by name: the node itself, or the first mesh under a node of that name.</summary>
    public static MeshInstance3D? FindMesh(Node root, string name)
    {
        var n = root.FindChild(name, recursive: true, owned: false);
        if (n is MeshInstance3D mi) return mi;
        if (n == null) return null;
        foreach (var c in n.FindChildren("*", "MeshInstance3D", recursive: true, owned: false))
            return (MeshInstance3D)c;
        return null;
    }

    /// <summary>Where a node sits relative to an ancestor (works before the node is in the scene tree).</summary>
    public static Transform3D RelativeTransform(Node3D node, Node root)
    {
        var t = Transform3D.Identity;
        for (Node? n = node; n != null && n != root; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }
}

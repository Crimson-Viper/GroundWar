using Godot;
using GroundWar.Sim.Data;

namespace GroundWar.Game;

/// <summary>Lets the simulation read data files from inside the Godot project (res://data/).</summary>
public sealed class GodotDataSource : IDataSource
{
    public string ReadText(string relativePath)
    {
        string path = "res://data/" + relativePath;
        if (!FileAccess.FileExists(path)) throw new System.IO.FileNotFoundException($"Missing data file {path}");
        return FileAccess.GetFileAsString(path);
    }
}

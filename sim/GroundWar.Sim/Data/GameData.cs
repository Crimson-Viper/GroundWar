using System.Text.Json;

namespace GroundWar.Sim.Data;

/// <summary>Reads data files by path relative to the data folder. Godot and the test runner each supply one.</summary>
public interface IDataSource
{
    string ReadText(string relativePath);
}

/// <summary>Reads from a folder on disk (used by tests and the headless runner).</summary>
public sealed class FolderDataSource : IDataSource
{
    public string Root { get; }
    public FolderDataSource(string root) { Root = root; }
    public string ReadText(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    /// <summary>Walks up from a folder until it finds the repo's data folder.</summary>
    public static FolderDataSource FindFrom(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "data", "rules.json");
            if (File.Exists(candidate)) return new FolderDataSource(Path.Combine(dir.FullName, "data"));
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not find data/rules.json above {startDir}");
    }
}

/// <summary>All loaded tuning data.</summary>
public sealed class GameData
{
    public Rules Rules { get; init; } = new();
    public Dictionary<string, WeaponDef> Weapons { get; init; } = new();
    public Dictionary<string, VehicleDef> Vehicles { get; init; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidDataException($"Empty {typeof(T).Name}");

    public static GameData Load(IDataSource source)
    {
        var rules = Parse<Rules>(source.ReadText("rules.json"));
        var weapons = Parse<Dictionary<string, WeaponDef>>(source.ReadText("weapons.json"));
        foreach (var (id, w) in weapons) w.Id = id;
        var vehicles = Parse<Dictionary<string, VehicleDef>>(source.ReadText("vehicles.json"));
        foreach (var (id, v) in vehicles) v.Id = id;
        return new GameData { Rules = rules, Weapons = weapons, Vehicles = vehicles };
    }

    public static ScenarioDef LoadScenario(IDataSource source, string path) => Parse<ScenarioDef>(source.ReadText(path));
    public static MapDef LoadMap(IDataSource source, string path) => Parse<MapDef>(source.ReadText(path));
}

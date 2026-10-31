using GroundWar.Sim;
using GroundWar.Sim.Data;

namespace GroundWar.Sim.Tests;

internal static class TestData
{
    public static readonly FolderDataSource Source = FolderDataSource.FindFrom(AppContext.BaseDirectory);
    public static GameData Data => GameData.Load(Source);

    public static Battle Firefight(ulong seed) => Battle.Load(Source, "scenarios/firefight.json", seed);

    /// <summary>A small hand-made map for geometry tests: one low wall, one high wall, one crater.</summary>
    public static MapDef SmallMap() => new()
    {
        Name = "small",
        Width = 100,
        Height = 100,
        Walls =
        {
            new WallDef { A = new[] { 50f, 20f }, B = new[] { 50f, 40f }, Thickness = 0.6f, High = false },
            new WallDef { A = new[] { 50f, 60f }, B = new[] { 50f, 80f }, Thickness = 0.7f, High = true },
        },
        Craters = { new CraterDef { Center = new[] { 20f, 50f }, Radius = 3f } },
    };
}

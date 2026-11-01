using System.Diagnostics;
using GroundWar.Sim;
using GroundWar.Sim.Data;
using GroundWar.Sim.Units;

// Usage:
//   dotnet run --project sim/GroundWar.Runner -- [--scenario scenarios/firefight.json] [--battles 20] [--seed 1] [--log]
//   --log prints every decision for the first battle.
//   --map maps/x.json plays the scenario on another map (e.g. to check a result isn't down to the ground).

string scenario = "scenarios/firefight.json";
int battles = 20;
ulong seed = 1;
bool log = false;
string? mapOverride = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--scenario": scenario = args[++i]; break;
        case "--battles": battles = int.Parse(args[++i]); break;
        case "--seed": seed = ulong.Parse(args[++i]); break;
        case "--log": log = true; break;
        case "--map": mapOverride = args[++i]; break;
        case "--profile": GroundWar.Sim.Core.Profile.Enabled = true; break;
    }
}

var source = FolderDataSource.FindFrom(AppContext.BaseDirectory);
var sourceCwd = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "data"))
    ? new FolderDataSource(Path.Combine(Directory.GetCurrentDirectory(), "data")) : source;

Console.WriteLine($"Scenario: {scenario}{(mapOverride != null ? $" on {mapOverride}" : "")}   battles: {battles}   first seed: {seed}");
Console.WriteLine();
Console.WriteLine(" seed  winner  time   blue lost  red lost  blue broke  red broke  shots   hits  in-cover%");

int blueWins = 0, redWins = 0, draws = 0;
float totalTime = 0;
var sw = Stopwatch.StartNew();
long ticks = 0;
for (int b = 0; b < battles; b++)
{
    var battle = Battle.Load(sourceCwd, scenario, seed + (ulong)b, mapOverride);
    var result = battle.RunToEnd();
    ticks += battle.Tick;
    totalTime += result.Time;
    if (result.Winner == Side.Blue) blueWins++;
    else if (result.Winner == Side.Red) redWins++;
    else draws++;

    string Lost(Side side) => $"{battle.Soldiers.Count(s => s.Side == side && (s.Status == SoldierStatus.Dead || s.Status == SoldierStatus.Wounded))}/{battle.Soldiers.Count(s => s.Side == side)}";
    int Broke(Side side) => battle.Soldiers.Count(s => s.Side == side && (s.Status == SoldierStatus.Routed || (s.IsActive && s.MoraleState == MoraleState.Broken)));
    int shots = battle.Soldiers.Sum(s => s.ShotsFired);
    int hits = battle.Soldiers.Sum(s => s.Hits);
    float underFire = battle.Soldiers.Sum(s => s.TimeUnderFire);
    float inCover = battle.Soldiers.Sum(s => s.TimeInCover);
    float total = battle.Soldiers.Count * result.Time;
    Console.WriteLine($"{seed + (ulong)b,5}  {(result.Winner?.ToString() ?? "draw"),-6} {result.Time,5:0}s  {Lost(Side.Blue),9}  {Lost(Side.Red),8}  {Broke(Side.Blue),10}  {Broke(Side.Red),9}  {shots,5}  {hits,5}  {100f * inCover / MathF.Max(total, 1f),8:0}");

    if (log && b == 0)
    {
        Console.WriteLine();
        foreach (var e in battle.Log.Entries) Console.WriteLine($"  [{e.Time,6:0.0}] {e.Text}");
        Console.WriteLine();
    }
}
sw.Stop();
Console.WriteLine();
Console.WriteLine($"Blue wins {blueWins}, Red wins {redWins}, draws {draws}.  Average length {totalTime / battles:0} s.");
Console.WriteLine($"Simulated {ticks * Battle.Dt / 60f:0} battle-minutes in {sw.Elapsed.TotalSeconds:0.0} s ({ticks * Battle.Dt / sw.Elapsed.TotalSeconds:0}x real time).");
if (GroundWar.Sim.Core.Profile.Enabled)
{
    Console.WriteLine();
    Console.WriteLine("Time per part of the tick (ms per simulated second):");
    double secs = ticks * Battle.Dt;
    foreach (var (name, ms) in GroundWar.Sim.Core.Profile.Milliseconds.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"  {name,-10} {ms / secs,8:0.0}");
}

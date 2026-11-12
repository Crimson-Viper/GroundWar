using System.Diagnostics;
using GroundWar.Sim;
using GroundWar.Sim.Data;
using GroundWar.Sim.Units;

// Usage:
//   dotnet run --project sim/GroundWar.Runner -- [--scenario scenarios/firefight.json] [--battles 20] [--seed 1] [--log]
//   --log prints every decision for the first battle.
//   --report prints the after-action report of the first battle.
//   --map maps/x.json plays the scenario on another map (e.g. to check a result isn't down to the ground).
//   --realism adds the realism report for the scenario: how the battles were fought, against the targets in
//     data/realism.json (docs/systems/realism-report.md).
//   --realism-suite plays every scenario listed in data/realism.json and prints one table of all of them.

string scenario = "scenarios/firefight.json";
int battles = 20;
bool report = false;
ulong seed = 1;
bool log = false;
string? mapOverride = null;
bool realism = false, suite = false, battlesGiven = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--scenario": scenario = args[++i]; break;
        case "--battles": battles = int.Parse(args[++i]); battlesGiven = true; break;
        case "--realism": realism = true; break;
        case "--realism-suite": suite = true; break;
        case "--seed": seed = ulong.Parse(args[++i]); break;
        case "--log": log = true; break;
        case "--map": mapOverride = args[++i]; break;
        case "--report": report = true; break;
        case "--profile": GroundWar.Sim.Core.Profile.Enabled = true; break;
    }
}

var source = FolderDataSource.FindFrom(AppContext.BaseDirectory);
var sourceCwd = Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "data"))
    ? new FolderDataSource(Path.Combine(Directory.GetCurrentDirectory(), "data")) : source;

if (suite)
{
    RealismSuite.Run(sourceCwd, battlesGiven ? battles : 30, seed);
    return;
}
var realismTotals = new RealismTotals();

Console.WriteLine($"Scenario: {scenario}{(mapOverride != null ? $" on {mapOverride}" : "")}   battles: {battles}   first seed: {seed}");
Console.WriteLine();
Console.WriteLine(" seed  winner  time   blue lost  red lost  blue broke  red broke  shots   hits  in-cover%");

int blueWins = 0, redWins = 0, draws = 0;
int tanks = 0, tanksKnockedOut = 0, tanksImmobilised = 0;
float totalTime = 0;
var sw = Stopwatch.StartNew();
long ticks = 0;
for (int b = 0; b < battles; b++)
{
    var battle = Battle.Load(sourceCwd, scenario, seed + (ulong)b, mapOverride);
    // No player here: the player's battalion (if the scenario has one) is run by his staff, like the enemy's.
    battle.PlayerHandOver(true);
    if (realism) realismTotals.Add(RealismProbe.Run(battle));
    var result = battle.RunToEnd();
    ticks += battle.Tick;
    totalTime += result.Time;
    if (result.Winner == Side.Blue) blueWins++;
    else if (result.Winner == Side.Red) redWins++;
    else draws++;

    foreach (var t in battle.Soldiers.Where(s => s.IsTank))
    {
        tanks++;
        if (t.Status == SoldierStatus.Dead) tanksKnockedOut++;
        else if (t.Immobilised) tanksImmobilised++;
    }

    string Lost(Side side) => $"{battle.Soldiers.Count(s => s.Side == side && (s.Status == SoldierStatus.Dead || s.Status == SoldierStatus.Wounded))}/{battle.Soldiers.Count(s => s.Side == side)}";
    int Broke(Side side) => battle.Soldiers.Count(s => s.Side == side && (s.Status == SoldierStatus.Routed || (s.IsActive && s.MoraleState == MoraleState.Broken)));
    int shots = battle.Soldiers.Sum(s => s.ShotsFired);
    int hits = battle.Soldiers.Sum(s => s.Hits);
    float underFire = battle.Soldiers.Sum(s => s.TimeUnderFire);
    float inCover = battle.Soldiers.Sum(s => s.TimeInCover);
    float total = battle.Soldiers.Count * result.Time;
    Console.WriteLine($"{seed + (ulong)b,5}  {(result.Winner?.ToString() ?? "draw"),-6} {result.Time,5:0}s  {Lost(Side.Blue),9}  {Lost(Side.Red),8}  {Broke(Side.Blue),10}  {Broke(Side.Red),9}  {shots,5}  {hits,5}  {100f * inCover / MathF.Max(total, 1f),8:0}");

    if (report && b == 0)
    {
        Console.WriteLine();
        Console.WriteLine(AfterActionReport.Build(battle).ToText());
        Console.WriteLine();
    }
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
if (tanks > 0)
    Console.WriteLine($"Tanks: {tanks} fought, {tanksKnockedOut} knocked out, {tanksImmobilised} more immobilised (stuck but still fighting).");
Console.WriteLine($"Simulated {ticks * Battle.Dt / 60f:0} battle-minutes in {sw.Elapsed.TotalSeconds:0.0} s ({ticks * Battle.Dt / sw.Elapsed.TotalSeconds:0}x real time).");
if (realism)
{
    Console.WriteLine();
    Console.Write(realismTotals.ToText(RealismTargets.Load(sourceCwd)));
}
if (GroundWar.Sim.Core.Profile.Enabled)
{
    Console.WriteLine();
    Console.WriteLine("Time per part of the tick (ms per simulated second):");
    double secs = ticks * Battle.Dt;
    foreach (var (name, ms) in GroundWar.Sim.Core.Profile.Milliseconds.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"  {name,-10} {ms / secs,8:0.0}");
}

/// <summary>--realism-suite: every scenario in data/realism.json side by side, one row per measure.</summary>
static class RealismSuite
{
    public static void Run(IDataSource source, int battles, ulong seed)
    {
        var targets = RealismTargets.Load(source);
        var all = new List<(string Name, RealismTotals Totals)>();
        foreach (string scenario in targets.Suite)
        {
            var totals = new RealismTotals();
            int blue = 0, red = 0, draws = 0;
            for (int b = 0; b < battles; b++)
            {
                var battle = Battle.Load(source, scenario, seed + (ulong)b);
                battle.PlayerHandOver(true);
                totals.Add(RealismProbe.Run(battle));
                if (battle.Result!.Winner == Side.Blue) blue++;
                else if (battle.Result.Winner == Side.Red) red++;
                else draws++;
            }
            string name = Path.GetFileNameWithoutExtension(scenario);
            all.Add((name, totals));
            Console.Error.WriteLine($"  {name}: Blue {blue}, Red {red}, draws {draws}");
        }

        Console.WriteLine($"Realism suite: {battles} battles of each scenario, first seed {seed}. A * marks a value outside the target.");
        Console.WriteLine();
        Console.Write($"{"measure",-52} {"target",-11}");
        foreach (var (name, _) in all) Console.Write($" {Short(name),14}");
        Console.WriteLine();
        var rows = all.Select(a => a.Totals.Lines(targets)).ToList();
        for (int r = 0; r < rows[0].Count; r++)
        {
            Console.Write($"{rows[0][r].Label,-52} {rows[0][r].TargetText,-11}");
            foreach (var lines in rows) Console.Write($" {lines[r].ValueText + (lines[r].Ok == false ? "*" : " "),14}");
            Console.WriteLine();
        }
        Console.Write($"{"In range",-52} {"",-11}");
        int okAll = 0, ofAll = 0;
        foreach (var (_, totals) in all)
        {
            var (ok, of) = totals.Score(targets);
            okAll += ok; ofAll += of;
            Console.Write($" {$"{ok} of {of} ",14}");
        }
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"Overall: {okAll} of {ofAll} measures inside their target.");
        Console.WriteLine();
        Console.WriteLine("How the battles ended:");
        foreach (var (name, totals) in all)
            Console.WriteLine($"  {Short(name),-14} {string.Join(", ", totals.Endings.OrderByDescending(e => e.Value).Select(e => $"{e.Key} {e.Value}"))}");
    }

    private static string Short(string name) => name.Length <= 14 ? name : name[..14];
}

using System.Numerics;
using System.Text;
using GroundWar.Sim.Data;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>The realism report: measures HOW battles are fought (not who wins) and compares each number with a
/// target range from data/realism.json. It only watches; it never changes a battle.
/// See docs/systems/realism-report.md.</summary>
public sealed class RealismTargets
{
    /// <summary>Measure id to [lowest, highest] value that counts as realistic.</summary>
    public Dictionary<string, float[]> Targets { get; set; } = new();
    /// <summary>The scenarios the runner's --realism-suite plays.</summary>
    public List<string> Suite { get; set; } = new();

    public static RealismTargets Load(IDataSource source) => GameData.Parse<RealismTargets>(source.ReadText("realism.json"));
}

/// <summary>One line of the report: a measure, its value and whether it is inside the target range.</summary>
public sealed class RealismLine
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Unit { get; init; } = "%";
    /// <summary>NaN when there was nothing to measure (e.g. no squad broke).</summary>
    public float Value { get; init; } = float.NaN;
    public float? Min { get; init; }
    public float? Max { get; init; }
    public bool HasValue => !float.IsNaN(Value);
    public bool HasTarget => Min != null && Max != null;
    /// <summary>Inside the target range. Null when there is no target or nothing was measured.</summary>
    public bool? Ok => HasTarget && HasValue ? Value >= Min && Value <= Max : null;
    public string ValueText => !HasValue ? "-" : Unit == "%" ? $"{Value:0}%" : Unit == "" ? $"{Value:0}" : Unit == "min" ? $"{Value:0.0} min" : $"{Value:0} {Unit}";
    public string TargetText => !HasTarget ? "" : Unit == "%" ? $"{Min:0}-{Max:0}%" : Unit == "" ? $"{Min:0}-{Max:0}" : $"{Min:0}-{Max:0} {Unit}";
}

/// <summary>Sums over one battle or many. Add battles with <see cref="RealismProbe"/>, then read <see cref="Lines"/>.</summary>
public sealed class RealismTotals
{
    public const int RangeBin = 10, RangeBins = 80;

    public int Battles, Decisive;
    public float FightSeconds;
    public readonly SortedDictionary<string, int> Endings = new();

    // Men (infantry on foot; tanks and their crews are left out)
    public int Men, Killed, Wounded, Fled;
    public int LoserMen, LoserCasualties, WinnerMen, WinnerCasualties;
    public int MenWhoBroke, MenWhoRallied;

    // Squads
    public int SquadsFought, SquadsBroke, SquadsShotToHalf;
    public float SquadLossAtBreakSum;

    // Under fire
    public float UnderFireSeconds, PinnedSeconds;
    public int MovingSamples, BunchedSamples;

    // Shooting
    public int SmallArmsShots, SmallArmsCasualties;
    public int ByRifle, ByMachineGun, ByShell, ByTank;
    public readonly int[] ShotRanges = new int[RangeBins];
    public readonly int[] HitRanges = new int[RangeBins];

    public int Casualties => Killed + Wounded;

    public void Add(RealismTotals o)
    {
        Battles += o.Battles; Decisive += o.Decisive; FightSeconds += o.FightSeconds;
        foreach (var (k, v) in o.Endings) Endings[k] = Endings.GetValueOrDefault(k) + v;
        Men += o.Men; Killed += o.Killed; Wounded += o.Wounded; Fled += o.Fled;
        LoserMen += o.LoserMen; LoserCasualties += o.LoserCasualties;
        WinnerMen += o.WinnerMen; WinnerCasualties += o.WinnerCasualties;
        MenWhoBroke += o.MenWhoBroke; MenWhoRallied += o.MenWhoRallied;
        SquadsFought += o.SquadsFought; SquadsBroke += o.SquadsBroke; SquadsShotToHalf += o.SquadsShotToHalf;
        SquadLossAtBreakSum += o.SquadLossAtBreakSum;
        UnderFireSeconds += o.UnderFireSeconds; PinnedSeconds += o.PinnedSeconds;
        MovingSamples += o.MovingSamples; BunchedSamples += o.BunchedSamples;
        SmallArmsShots += o.SmallArmsShots; SmallArmsCasualties += o.SmallArmsCasualties;
        ByRifle += o.ByRifle; ByMachineGun += o.ByMachineGun; ByShell += o.ByShell; ByTank += o.ByTank;
        for (int i = 0; i < RangeBins; i++) { ShotRanges[i] += o.ShotRanges[i]; HitRanges[i] += o.HitRanges[i]; }
    }

    private static float Pct(float part, float whole) => whole <= 0f ? float.NaN : 100f * part / whole;

    private static float Median(int[] bins)
    {
        long total = 0;
        foreach (int n in bins) total += n;
        if (total == 0) return float.NaN;
        long seen = 0;
        for (int i = 0; i < bins.Length; i++)
        {
            seen += bins[i];
            if (seen * 2 >= total) return i * RangeBin + RangeBin / 2f;
        }
        return bins.Length * RangeBin;
    }

    /// <summary>The report lines, in the order they are printed. Measures with a target come first.</summary>
    public List<RealismLine> Lines(RealismTargets? targets = null)
    {
        var lines = new List<RealismLine>();
        void Line(string id, string label, float value, string unit = "%")
        {
            float[]? t = null;
            targets?.Targets.TryGetValue(id, out t);
            lines.Add(new RealismLine
            {
                Id = id, Label = label, Value = value, Unit = unit,
                Min = t is { Length: 2 } ? t[0] : null, Max = t is { Length: 2 } ? t[1] : null,
            });
        }

        Line("brokeShare", "Men out of the fight who ran rather than were hit", Pct(Fled, Fled + Casualties));
        Line("loserCasualties", "Beaten side: men hit by the end", Pct(LoserCasualties, LoserMen));
        Line("winnerCasualties", "Winning side: men hit by the end", Pct(WinnerCasualties, WinnerMen));
        Line("squadLossAtBreak", "A squad breaks after losing", SquadsBroke == 0 ? float.NaN : 100f * SquadLossAtBreakSum / SquadsBroke);
        Line("squadsShotToHalf", "Squads shot down to half strength or less", Pct(SquadsShotToHalf, SquadsFought));
        Line("pinnedShare", "Time under fire spent pinned down", Pct(PinnedSeconds, UnderFireSeconds));
        Line("bunched", "Moving men bunched up (within 2 m of a squad mate)", Pct(BunchedSamples, MovingSamples));
        Line("shotRange", "Typical range of rifle and MG fire", Median(ShotRanges), "m");
        Line("roundsPerCasualty", "Rounds fired per man hit (rifles and MGs)", SmallArmsCasualties == 0 ? float.NaN : SmallArmsShots / (float)SmallArmsCasualties, "");
        Line("killedShare", "Men hit who are killed outright", Pct(Killed, Casualties));
        // For information: no target.
        Line("hitRange", "Typical range at which men are hit", Median(HitRanges), "m");
        Line("rallied", "Broken men who rally", Pct(MenWhoRallied, MenWhoBroke));
        int by = ByRifle + ByMachineGun + ByShell + ByTank;
        Line("byRifle", "Hit by rifles", Pct(ByRifle, by));
        Line("byMachineGun", "Hit by machine guns", Pct(ByMachineGun, by));
        Line("byShell", "Hit by mortars and artillery", Pct(ByShell, by));
        Line("byTank", "Hit by tanks", Pct(ByTank, by));
        Line("fightMinutes", "Length of the fight, first shot to the end", Battles == 0 ? float.NaN : FightSeconds / Battles / 60f, "min");
        return lines;
    }

    /// <summary>How many of the measures with a target are inside it: (inside, measured).</summary>
    public (int Ok, int Of) Score(RealismTargets targets)
    {
        int ok = 0, of = 0;
        foreach (var l in Lines(targets))
        {
            if (l.Ok == null) continue;
            of++;
            if (l.Ok == true) ok++;
        }
        return (ok, of);
    }

    public string ToText(RealismTargets? targets = null)
    {
        var sb = new StringBuilder();
        sb.Append($"Realism report ({Battles} battle{(Battles == 1 ? "" : "s")})\n");
        sb.Append($"  {"measure",-52} {"value",8}  {"target",-11}\n");
        foreach (var l in Lines(targets))
        {
            string mark = l.Ok switch { true => "ok", false => "MISS", null => "" };
            sb.Append($"  {l.Label,-52} {l.ValueText,8}  {l.TargetText,-11} {mark}\n");
        }
        if (targets != null)
        {
            var (ok, of) = Score(targets);
            sb.Append($"  In range: {ok} of {of}.\n");
        }
        sb.Append("  How the battles ended: ");
        sb.Append(string.Join(", ", Endings.OrderByDescending(e => e.Value).Select(e => $"{e.Key} {e.Value}")));
        sb.Append('\n');
        return sb.ToString();
    }
}

/// <summary>Watches one battle. Call <see cref="AfterStep"/> after every Battle.Step (or just use <see cref="Run"/>),
/// then <see cref="Finish"/> once there is a result.</summary>
public sealed class RealismProbe
{
    /// <summary>Closer than this to a squad mate while moving counts as bunched up.</summary>
    public const float BunchedDistance = 2f;
    private const int SampleEvery = 10; // ticks: once a second

    private readonly Battle _b;
    private readonly bool[] _wasBroken;
    private readonly bool[] _everBroke;
    private readonly bool[] _rallied;
    private readonly HashSet<int> _brokenSquads = new();
    private bool _finished;

    public RealismTotals Totals { get; } = new();

    public RealismProbe(Battle battle)
    {
        _b = battle;
        int n = battle.Soldiers.Count;
        _wasBroken = new bool[n];
        _everBroke = new bool[n];
        _rallied = new bool[n];
    }

    /// <summary>Play a battle to its end under the probe and return what it measured.</summary>
    public static RealismTotals Run(Battle battle)
    {
        var probe = new RealismProbe(battle);
        while (battle.Result == null)
        {
            battle.Step();
            probe.AfterStep();
        }
        return probe.Finish();
    }

    /// <summary>On foot and in the fight as infantry: not a tank and not a tank crewman.</summary>
    public static bool IsInfantry(Soldier s) => !s.IsTank && s.CrewOf == null;

    /// <summary>A fighting squad (not a tank section, crew, mortar section or company HQ).</summary>
    public static bool IsSquad(Group g) => !g.IsVehicles && !g.IsCrew && g.Mortars == null && !g.IsCompanyHq && g.StartStrength >= 4;

    private static int Hit(Group g) => g.Members.Count(m => m.Status is SoldierStatus.Dead or SoldierStatus.Wounded);

    public void AfterStep()
    {
        var t = Totals;
        foreach (var shot in _b.Shots)
        {
            if (shot.Shell || shot.Indirect) continue;
            var shooter = _b.GetSoldier(shot.ShooterId);
            if (shooter == null || shooter.IsTank) continue;
            t.SmallArmsShots++;
            int bin = Math.Min(RealismTotals.RangeBins - 1, (int)(Vector2.Distance(shot.From, shot.Impact) / RealismTotals.RangeBin));
            t.ShotRanges[bin]++;
            if (shot.Hit) t.HitRanges[bin]++;
        }
        foreach (var c in _b.Casualties)
        {
            var victim = _b.GetSoldier(c.SoldierId);
            var shooter = _b.GetSoldier(c.ShooterId);
            if (victim == null || shooter == null || !IsInfantry(victim)) continue;
            if (shooter.IsTank || shooter.CrewOf != null) t.ByTank++;
            else if (shooter.Group.Mortars != null || shooter.Side == victim.Side) t.ByShell++;
            else
            {
                t.SmallArmsCasualties++;
                if (shooter.Weapon.IsMachineGun) t.ByMachineGun++; else t.ByRifle++;
            }
        }
        foreach (var g in _b.Groups)
        {
            if (!g.HasBroken || !IsSquad(g) || !_brokenSquads.Add(g.Id)) continue;
            t.SquadsBroke++;
            t.SquadLossAtBreakSum += Hit(g) / (float)g.StartStrength;
        }

        if (_b.Tick % SampleEvery != 0) return;
        float dt = SampleEvery * Battle.Dt;
        float window = _b.Rules.Ai.UnderFireWindow;
        var soldiers = _b.Soldiers;
        for (int i = 0; i < soldiers.Count; i++)
        {
            var s = soldiers[i];
            if (!IsInfantry(s)) continue;
            bool broken = s.IsActive && s.MoraleState == MoraleState.Broken || s.Status == SoldierStatus.Routed;
            if (broken && !_wasBroken[i]) _everBroke[i] = true;
            if (!broken && _wasBroken[i] && s.IsActive) _rallied[i] = true;
            _wasBroken[i] = broken;
            if (!s.IsActive) continue;

            if (s.UnderFire(_b.Time, window))
            {
                t.UnderFireSeconds += dt;
                if (s.MoraleState >= MoraleState.Pinned) t.PinnedSeconds += dt;
            }
            if (s.IsMoving && s.MoraleState != MoraleState.Broken)
            {
                t.MovingSamples++;
                foreach (var m in s.Group.Members)
                {
                    if (m == s || !m.IsActive) continue;
                    if (Vector2.DistanceSquared(m.Position, s.Position) < BunchedDistance * BunchedDistance) { t.BunchedSamples++; break; }
                }
            }
        }
    }

    /// <summary>Count up how the battle ended. Call once, when the battle has a result.</summary>
    public RealismTotals Finish()
    {
        if (_finished) return Totals;
        _finished = true;
        var t = Totals;
        var result = _b.Result;
        t.Battles = 1;
        t.FightSeconds = _b.FirstShotTime >= 0f ? _b.Time - _b.FirstShotTime : 0f;
        string ending = Ending(result?.Reason ?? "");
        t.Endings[ending] = t.Endings.GetValueOrDefault(ending) + 1;
        if (result?.Winner != null) t.Decisive = 1;

        var soldiers = _b.Soldiers;
        for (int i = 0; i < soldiers.Count; i++)
        {
            var s = soldiers[i];
            if (!IsInfantry(s)) continue;
            t.Men++;
            bool hit = s.Status is SoldierStatus.Dead or SoldierStatus.Wounded;
            if (s.Status == SoldierStatus.Dead) t.Killed++;
            else if (s.Status == SoldierStatus.Wounded) t.Wounded++;
            else if (s.Status == SoldierStatus.Routed || s.MoraleState == MoraleState.Broken) t.Fled++;
            if (_everBroke[i]) t.MenWhoBroke++;
            if (_rallied[i]) t.MenWhoRallied++;
            if (result?.Winner is Side winner)
            {
                if (s.Side == winner) { t.WinnerMen++; if (hit) t.WinnerCasualties++; }
                else { t.LoserMen++; if (hit) t.LoserCasualties++; }
            }
        }
        foreach (var g in _b.Groups)
        {
            if (!IsSquad(g)) continue;
            int hit = Hit(g);
            if (hit == 0 && !g.HasBroken) continue;
            t.SquadsFought++;
            if (hit * 2 >= g.StartStrength) t.SquadsShotToHalf++;
        }
        return t;
    }

    /// <summary>The battle's end reason without the side names, so endings can be counted.</summary>
    public static string Ending(string reason)
    {
        if (reason.Contains("took the objective") || reason.Contains("holds the objective")) return "objective taken";
        if (reason.Contains("death trap")) return "attack refused";
        if (reason.Contains("attack failed")) return "attack failed";
        if (reason.Contains("can no longer fight") || reason.Contains("Both sides broke")) return "one side wiped out or fled";
        if (reason.Contains("overran")) return "HQ overrun";
        if (reason.Contains("held on")) return "defender held to the time limit";
        if (reason.Contains("came off better") || reason.Contains("upper hand")) return "time ran out";
        if (reason.Contains("died down")) return "fighting died down";
        return reason.Length == 0 ? "no result" : reason;
    }
}

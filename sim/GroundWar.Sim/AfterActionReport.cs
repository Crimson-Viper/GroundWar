using System.Text;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>
/// The after-action report (milestone 6): what happened, worked out from the finished (or paused) battle. Shown in
/// the game when a battle ends, and printed by the runner with --report. See docs/systems/replays-and-reports.md.
/// </summary>
public sealed class AfterActionReport
{
    public sealed class SideSummary
    {
        public Side Side { get; init; }
        public int Men, Killed, Wounded, Fled, Fighting, Shots, Hits, Tanks, TanksLost;
        public int OrdersSent, ReportsSent, MessagesLost;
        public int FireMissions, MortarRounds, SmokeRounds, ArtilleryMissions, Shells;
        public float AverageOrderDelay;
        public string Casualties => $"{Killed} killed, {Wounded} wounded, {Fled} fled, {Fighting} of {Men} still fighting";
    }

    public sealed class SquadSummary
    {
        public string Name = "", Company = "";
        public Side Side;
        public int Start, Fit, Killed, Wounded, Fled, Shots, Hits;
        public bool TookObjective, Broke, Vehicles;
    }

    public sealed class CompanySummary
    {
        public string Name = "", Commander = "", Traits = "", Outcome = "";
        public Side Side;
        public List<(float Time, string Text)> Story { get; } = new();
    }

    public sealed record Standout(string Label, string Squad, Side Side, int Hits, int Shots, string Status);

    public string Scenario { get; init; } = "";
    public ulong Seed { get; init; }
    public float Time { get; init; }
    public BattleResult? Result { get; init; }
    public SideSummary Blue { get; init; } = null!;
    public SideSummary Red { get; init; } = null!;
    public List<SquadSummary> Squads { get; } = new();
    public List<CompanySummary> Companies { get; } = new();
    /// <summary>The key moments, in order.</summary>
    public List<(float Time, string Text, Side? Side)> Timeline { get; } = new();
    /// <summary>The men who hit most, each side.</summary>
    public List<Standout> Standouts { get; } = new();

    public static AfterActionReport Build(Battle b)
    {
        var r = new AfterActionReport
        {
            Scenario = b.Scenario.Name, Seed = b.Seed, Time = b.Time, Result = b.Result,
            Blue = SideOf(b, Side.Blue), Red = SideOf(b, Side.Red),
        };
        foreach (var g in b.Groups)
        {
            r.Squads.Add(new SquadSummary
            {
                Name = g.Name, Side = g.Side, Company = g.Company?.Name ?? "", Start = g.Members.Count, Fit = g.CountEffective,
                Killed = g.Members.Count(m => m.Status == SoldierStatus.Dead),
                Wounded = g.Members.Count(m => m.Status == SoldierStatus.Wounded),
                Fled = g.Members.Count(m => m.Status == SoldierStatus.Routed),
                Shots = g.Members.Sum(m => m.ShotsFired), Hits = g.Members.Sum(m => m.Hits),
                TookObjective = g.TookObjective, Broke = g.HasBroken, Vehicles = g.IsVehicles,
            });
        }
        var keyLines = b.Log.Entries.Where(e => e.Kind == LogKind.Key || e.Kind == LogKind.Casualty && e.Text.Contains("knocked out")).ToList();
        foreach (var c in b.Companies)
        {
            var t = c.Traits;
            var cs = new CompanySummary
            {
                Name = c.Name, Commander = c.CommanderName, Side = c.Side,
                Traits = $"aggression {t.Aggression}, caution {t.Caution}, initiative {t.Initiative}, competence {t.Competence}, composure {t.Composure}",
                Outcome = c.CommanderDown ? "Commander down" : c.Status,
            };
            foreach (var e in keyLines)
                if (e.Text.StartsWith(c.CommanderName) || e.Text.StartsWith(c.Name + ":")) cs.Story.Add((e.Time, e.Text));
            r.Companies.Add(cs);
        }
        foreach (var e in keyLines)
        {
            // Phase changes are in each company's story; the timeline keeps the big moments.
            if (e.Text.Contains(": phase ")) continue;
            r.Timeline.Add((e.Time, Shorten(e.Text), e.Side));
        }
        if (b.FirstShotTime >= 0f) r.Timeline.Add((b.FirstShotTime, b.FirstShotText, null));
        foreach (var side in new[] { Side.Blue, Side.Red })
        {
            var first = b.Log.Entries.FirstOrDefault(e => e.Kind == LogKind.Casualty && e.Side == side);
            if (first.Text != null) r.Timeline.Add((first.Time, $"First {side} casualty: {first.Text}", side));
        }
        r.Timeline.Sort((x, y) => x.Time.CompareTo(y.Time));
        foreach (var side in new[] { Side.Blue, Side.Red })
            foreach (var s in b.Soldiers.Where(s => s.Side == side && s.Hits > 0).OrderByDescending(s => s.Hits).ThenBy(s => s.ShotsFired).Take(3))
                r.Standouts.Add(new Standout(s.Label, s.Group.Name, side, s.Hits, s.ShotsFired, s.Status.ToString()));
        return r;
    }

    private static string Shorten(string text) => text.Length > 140 ? text.Substring(0, 137) + "..." : text;

    private static SideSummary SideOf(Battle b, Side side)
    {
        var men = b.Soldiers.Where(s => s.Side == side && !s.IsTank).ToList();
        var tanks = b.Soldiers.Where(s => s.Side == side && s.IsTank).ToList();
        var msgs = b.Messages.Where(m => m.Side == side).ToList();
        var delivered = msgs.Where(m => m.Down && m.Order != null && m.Status == MessageStatus.Delivered).ToList();
        return new SideSummary
        {
            Side = side, Men = men.Count,
            Killed = men.Count(s => s.Status == SoldierStatus.Dead),
            Wounded = men.Count(s => s.Status == SoldierStatus.Wounded),
            Fled = men.Count(s => s.Status == SoldierStatus.Routed),
            Fighting = men.Count(s => s.IsEffective),
            Shots = b.Soldiers.Where(s => s.Side == side).Sum(s => s.ShotsFired),
            Hits = b.Soldiers.Where(s => s.Side == side).Sum(s => s.Hits),
            Tanks = tanks.Count, TanksLost = tanks.Count(t => t.Status == SoldierStatus.Dead),
            OrdersSent = msgs.Count(m => m.Down && m.Order != null),
            ReportsSent = msgs.Count(m => !m.Down),
            FireMissions = b.FireMissions.Count(f => f.Side == side && !f.Artillery && f.RoundsLanded > 0),
            MortarRounds = b.FireMissions.Where(f => f.Side == side && !f.Artillery && f.Kind == FireKind.He).Sum(f => f.RoundsLanded),
            SmokeRounds = b.FireMissions.Where(f => f.Side == side && !f.Artillery && f.Kind == FireKind.Smoke).Sum(f => f.RoundsLanded),
            ArtilleryMissions = b.FireMissions.Count(f => f.Side == side && f.Artillery && f.RoundsLanded > 0),
            Shells = b.FireMissions.Where(f => f.Side == side && f.Artillery).Sum(f => f.RoundsLanded),
            MessagesLost = msgs.Count(m => m.Status == MessageStatus.Lost),
            AverageOrderDelay = delivered.Count > 0 ? delivered.Average(m => m.DoneTime - m.SentTime) : 0f,
        };
    }

    public static string Clock(float t) => $"{(int)(t / 60)}:{(int)t % 60:00}";

    /// <summary>The report as plain text (the runner prints this; the game shows it in a panel).</summary>
    public string ToText(int timelineLines = 40)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"AFTER-ACTION REPORT: {Scenario} (seed {Seed}), {Clock(Time)}");
        sb.AppendLine(Result == null ? "Battle still going." : $"{Result.Reason}. {(Result.Winner == null ? "No winner" : $"{Result.Winner} wins")}.");
        sb.AppendLine();
        foreach (var s in new[] { Blue, Red })
        {
            sb.AppendLine($"{s.Side}: {s.Casualties}. {s.Shots} shots, {s.Hits} hits ({(s.Shots > 0 ? 100f * s.Hits / s.Shots : 0f):0.0}%)." +
                          (s.Tanks > 0 ? $" Tanks: {s.TanksLost} of {s.Tanks} knocked out." : ""));
            if (s.OrdersSent > 0 || s.ReportsSent > 0)
                sb.AppendLine($"      {(s.OrdersSent > 0 ? $"{s.OrdersSent} orders (average {s.AverageOrderDelay:0} s to arrive), " : "")}{s.ReportsSent} reports, {s.MessagesLost} messages lost.");
            if (s.FireMissions > 0)
                sb.AppendLine($"      Mortars: {s.FireMissions} fire missions, {s.MortarRounds} rounds of HE{(s.SmokeRounds > 0 ? $" and {s.SmokeRounds} of smoke" : "")}.");
            if (s.ArtilleryMissions > 0)
                sb.AppendLine($"      Artillery: {s.ArtilleryMissions} fire missions, {s.Shells} shells.");
        }
        sb.AppendLine();
        sb.AppendLine("SQUADS");
        foreach (var q in Squads)
        {
            string fate = q.Broke ? "broke" : q.Fit == 0 ? "wiped out" : q.TookObjective ? "took its objective" : "";
            sb.AppendLine($"  {q.Name,-18} {q.Side,-4} {q.Fit,2}/{q.Start,-2} fit  {q.Killed} killed {q.Wounded} wounded {q.Fled} fled  {q.Hits} hit{(q.Hits == 1 ? "" : "s")} from {q.Shots} shots  {fate}");
        }
        foreach (var c in Companies)
        {
            sb.AppendLine();
            sb.AppendLine($"{c.Name.ToUpperInvariant()}: {c.Commander} ({c.Traits})");
            sb.AppendLine($"  Ended: {c.Outcome}");
            foreach (var (t, text) in c.Story.Take(20)) sb.AppendLine($"  {Clock(t),5}  {Shorten(text)}");
        }
        sb.AppendLine();
        sb.AppendLine("KEY MOMENTS");
        foreach (var (t, text, _) in Timeline.Take(timelineLines)) sb.AppendLine($"  {Clock(t),5}  {text}");
        if (Standouts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("BEST SHOTS");
            foreach (var s in Standouts) sb.AppendLine($"  {s.Label} ({s.Squad}): {s.Hits} hit{(s.Hits == 1 ? "" : "s")} from {s.Shots} shots, {s.Status.ToLowerInvariant()}");
        }
        return sb.ToString().TrimEnd();
    }
}

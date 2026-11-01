using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

/// <summary>Milestone 7: mortars, smoke screens, fire requests and MG sections. See docs/systems/support-fire.md.</summary>
public class SupportTests
{
    private static Battle Farm(ulong seed = 1) => Battle.Load(TestData.Source, "scenarios/farmhouse_support.json", seed);
    private static void Run(Battle b, float seconds) { int n = (int)(seconds / Battle.Dt); for (int i = 0; i < n; i++) b.Step(); }
    private static void RunUntil(Battle b, Func<bool> done, float limit = 300f)
    {
        float end = b.Time + limit;
        while (!done() && b.Time < end) b.Step();
        Assert.True(done(), "timed out waiting");
    }
    private static Company A(Battle b) => b.Companies.Single(c => c.Name == "A Company");
    private static Group Mortars(Battle b) => b.Groups.Single(g => g.Name == "Mortar Section");

    [Fact]
    public void SmokeBlocksSightButNotBullets()
    {
        var b = Farm();
        // A stretch of open field with nothing in the way.
        float y = Enumerable.Range(5, 290).Select(v => (float)v).First(v => !b.Map.SightBlocked(new Vector2(100, v), new Vector2(200, v)));
        var a = new Vector2(100, y);
        var c = new Vector2(200, y);
        b.Map.AddSmoke(new Vector2(150, y), 14f, b.Time, 60f);
        b.Map.UpdateSmoke(b.Time + 10f, 0f, Vector2.Zero, 6f); // fully built up
        Assert.True(b.Map.SightBlocked(a, c), "a thick cloud hides what's behind it");
        Assert.False(b.Map.WallBlocked(a, c), "but it doesn't stop a bullet");
        Assert.False(b.Map.SightBlocked(new Vector2(145, y), new Vector2(152, y)), "men close together in the smoke see each other");
        b.Map.UpdateSmoke(b.Time + 61f, 0f, Vector2.Zero, 6f);
        Assert.False(b.Map.SightBlocked(a, c), "and it clears");
    }

    [Fact]
    public void SmokeDriftsWithTheWind()
    {
        var b = Farm();
        var cloud = b.Map.AddSmoke(new Vector2(150, 150), 14f, 0f, 60f);
        for (int i = 0; i < 100; i++) b.Map.UpdateSmoke(i * 0.1f, 0.1f, new Vector2(1f, 0f), 6f);
        Assert.True(cloud.Center.X > 159f && cloud.Center.X < 161f, $"drifted to {cloud.Center}");
    }

    [Fact]
    public void ABattalionFireMissionTakesTimeAndLandsNearTheTarget()
    {
        var b = Farm();
        var mortars = Mortars(b);
        var target = new Vector2(300, 150);
        int heBefore = mortars.Mortars!.AmmoHe;
        b.PlayerOrder(mortars, new Order { Kind = OrderKind.FireMission, Objective = target, Fire = FireKind.He, Rounds = 6 });
        var fm = b.FireMissions.Single(m => m.RequestedBy == null);
        var landed = new List<Vector2>();
        float firstLanded = -1f;
        while (fm.Status != FireMissionStatus.Done && b.Time < 120f)
        {
            b.Step();
            foreach (var i in b.Impacts) { landed.Add(i.Position); if (firstLanded < 0f) firstLanded = b.Time; }
        }
        Assert.Equal(FireMissionStatus.Done, fm.Status);
        Assert.Equal(6, landed.Count);
        Assert.Equal(heBefore - 6, mortars.Mortars.AmmoHe);
        // Order by phone, laying the tubes, then the time of flight: nothing lands for a good while.
        float minimum = b.Rules.Support.SetUpTime + b.Rules.Support.FlightTime;
        Assert.True(firstLanded > minimum, $"first round landed at {firstLanded:0.0} s");
        float mean = landed.Average(p => Vector2.Distance(p, target));
        Assert.True(mean < 60f, $"rounds should fall around the target (mean miss {mean:0} m)");
    }

    [Fact]
    public void ACommanderPlansMortarFireOnCallAndBattalionPassesItOn()
    {
        var b = Farm();
        var c = A(b);
        RunUntil(b, () => c.Plan != null && c.State != CompanyState.AwaitingOrders, 200f);
        Assert.NotEmpty(c.Plan!.Fires);
        Assert.Contains("Mortars", c.Plan.Summary);
        // When the plan starts, the fire plan goes to battalion as on-call targets.
        RunUntil(b, () => b.FireMissions.Any(m => m.RequestedBy == c), 600f);
        var fm = b.FireMissions.First(m => m.RequestedBy == c);
        Assert.True(fm.OnCall);
        Assert.Contains(b.Messages, m => !m.Down && m.Kind == MessageKind.FireRequest && m.Mission == fm);
        RunUntil(b, () => fm.Status != FireMissionStatus.Requested, 200f);
        if (fm.Status == FireMissionStatus.Cancelled) return; // he changed his plan before battalion had it
        Assert.Contains(b.Messages, m => m.Down && m.Order?.Mission == fm && m.Group == Mortars(b));
    }

    [Fact]
    public void AnOnCallTargetIsLaidOnAndWaitsForTheWord()
    {
        var b = Farm();
        var c = A(b);
        var fm = b.RequestFire(c, new Vector2(300, 150), FireKind.He, 6, "a test target", onCall: true)!;
        RunUntil(b, () => fm.Status == FireMissionStatus.Firing, 200f);
        Run(b, 60f);
        Assert.Equal(0, fm.RoundsFired); // laid on, but nobody has said "fire"
        float called = b.Time;
        b.CallFire(c, fm);
        RunUntil(b, () => fm.RoundsLanded > 0, 60f);
        Assert.True(b.Time - called < 40f, $"once called, the first rounds land quickly ({b.Time - called:0} s)");
    }

    [Fact]
    public void ACancelledTargetIsNeverFired()
    {
        var b = Farm();
        var c = A(b);
        var fm = b.RequestFire(c, new Vector2(300, 150), FireKind.He, 6, "a test target", onCall: true)!;
        RunUntil(b, () => fm.Status == FireMissionStatus.Firing, 200f);
        b.CancelFire(fm, "test");
        Run(b, 30f);
        Assert.Equal(0, fm.RoundsFired);
        Assert.Null(Mortars(b).Mortars!.Current);
    }

    [Fact]
    public void AnMgSectionAttachedToACompanyBecomesOneOfItsSquads()
    {
        var b = Farm();
        var c = A(b);
        var mg = b.Groups.Single(g => g.Name == "MG Section");
        Assert.True(mg.IsAsset);
        RunUntil(b, () => mg.Company == c, 120f);
        Assert.Contains(mg, c.Squads);
        // With two heavy machine guns it is the obvious squad to give covering fire.
        RunUntil(b, () => c.Plan != null && c.State != CompanyState.AwaitingOrders, 200f);
        if (c.Plan!.Support != null) Assert.Equal(mg, c.Plan.Support);
    }

    [Fact]
    public void DefendersCallFireOnAttackersTheySeeComing()
    {
        var b = Farm();
        var red = b.Companies.Single(c => c.Name == "Red Company");
        RunUntil(b, () => b.FireMissions.Any(m => m.RequestedBy == red), 900f);
        var fm = b.FireMissions.First(m => m.RequestedBy == red);
        Assert.Equal(FireKind.He, fm.Kind);
    }

    [Fact]
    public void NoMortarsMeansNoFireRequests()
    {
        var b = Battle.Load(TestData.Source, "scenarios/farmhouse.json", 1);
        Run(b, 600f);
        Assert.Empty(b.FireMissions);
        Assert.All(b.Companies.Where(c => c.Plan != null), c => Assert.Empty(c.Plan!.Fires));
    }

    [Fact]
    public void AMortarSectionRunsOutOfAmmunition()
    {
        var b = Farm();
        var mortars = Mortars(b);
        mortars.Mortars!.AmmoSmoke = 2;
        b.PlayerOrder(mortars, new Order { Kind = OrderKind.FireMission, Objective = new Vector2(300, 150), Fire = FireKind.Smoke, Rounds = 6 });
        var fm = b.FireMissions.Single(m => m.RequestedBy == null);
        RunUntil(b, () => fm.Status == FireMissionStatus.Done, 120f);
        Assert.Equal(2, fm.RoundsLanded);
        Assert.Equal(0, mortars.Mortars.AmmoSmoke);
        Assert.False(b.HasMortars(Side.Blue, FireKind.Smoke));
        Assert.True(b.HasMortars(Side.Blue, FireKind.He));
    }
}

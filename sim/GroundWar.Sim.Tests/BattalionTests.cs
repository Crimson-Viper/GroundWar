using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

/// <summary>Milestone 7, part 2: battalion commanders, artillery, and overrunning the HQ. See docs/systems/battalion-commander.md.</summary>
public class BattalionTests
{
    private static Battle Valley(ulong seed = 1) => Battle.Load(TestData.Source, "scenarios/battalion.json", seed);
    private static void Run(Battle b, float seconds) { int n = (int)(seconds / Battle.Dt); for (int i = 0; i < n; i++) b.Step(); }
    private static Company Co(Battle b, string name) => b.Companies.Single(c => c.Name == name);

    /// <summary>Move a squad somewhere and tell it to stay there.</summary>
    private static void PutSquadAt(Group g, Vector2 at)
    {
        g.Order = GroupOrder.Hold;
        g.HoldPosition = g.Objective = at;
        foreach (var s in g.Members) { s.Position = at; s.TaskPosition = at; }
    }

    [Fact]
    public void ThePlayersBattalionWaitsForHimUntilHeHandsItOver()
    {
        var b = Valley();
        var blue = b.BattalionOf(Side.Blue)!;
        Assert.False(blue.Active);
        Assert.True(b.BattalionOf(Side.Red)!.Active);
        Run(b, 30f);
        Assert.DoesNotContain(b.Messages, m => m.Side == Side.Blue && m.Down && m.Order?.Kind == OrderKind.Attack);
        b.PlayerHandOver(true);
        Assert.True(blue.Active);
        Assert.Contains(b.Commands, c => c.Kind == CommandKind.HandOver);
    }

    [Fact]
    public void AnAttackingBattalionSendsTwoCompaniesAtTheNearestKeyPointsAndKeepsItsReserve()
    {
        var b = Valley();
        b.PlayerHandOver(true);
        Run(b, 10f);
        var bn = b.BattalionOf(Side.Blue)!;
        var attacks = bn.Tasks.Where(kv => kv.Value.Kind == BattalionTaskKind.Attack).ToList();
        Assert.Equal(2, attacks.Count);
        Assert.Equal(new[] { "the farm", "the village" }, attacks.Select(kv => kv.Value.Point!.Name).OrderBy(n => n));
        Assert.Equal(BattalionTaskKind.Reserve, bn.Tasks[Co(b, "C Company")].Kind);
        // The orders go to the company commanders like any order, and take time to get there.
        foreach (var (c, _) in attacks)
            Assert.Contains(b.Messages, m => m.Down && m.Group == c.HqGroup && m.Order?.Kind == OrderKind.Attack);
    }

    [Fact]
    public void ADefendingBattalionHoldsItsKeyPoints()
    {
        var b = Valley();
        Run(b, 10f);
        var bn = b.BattalionOf(Side.Red)!;
        Assert.Equal("the village", bn.Tasks[Co(b, "1st Company")].Point?.Name);
        Assert.Equal("the farm", bn.Tasks[Co(b, "2nd Company")].Point?.Name);
        Assert.Equal(BattalionTaskKind.Reserve, bn.Tasks[Co(b, "3rd Company")].Kind);
        Assert.Contains(b.Messages, m => m.Down && m.Group == Co(b, "1st Company").HqGroup && m.Order?.Kind == OrderKind.Hold);
    }

    [Fact]
    public void WhenAKeyPointFallsTheReserveCounterattacks()
    {
        var b = Valley();
        Run(b, 10f);
        var bn = b.BattalionOf(Side.Red)!;
        var village = b.KeyPoints.Single(p => p.Name == "the village");
        // Battalion hears that 1st Company is cut to pieces, and that a few of the enemy are in the village.
        bn.NewsOf(Co(b, "1st Company")).Strength = 2;
        foreach (var s in b.Soldiers.Where(s => s.Side == Side.Blue).Take(5))
            b.Hq(Side.Red)!.Knowledge.Report(s, village.Position, b.Time, 1f);
        bn.NextThink = b.Time;
        b.Step();
        Assert.Equal(Side.Blue, bn.Held[village]);
        var task = bn.Tasks[Co(b, "3rd Company")];
        Assert.Equal(BattalionTaskKind.Counterattack, task.Kind);
        Assert.Equal(village, task.Point);
        Assert.Contains(b.Messages, m => m.Down && m.Group == Co(b, "3rd Company").HqGroup && m.Order?.Kind == OrderKind.Attack);
    }

    [Fact]
    public void ArtilleryTakesTimeToArriveAndLandsAroundTheTarget()
    {
        var b = Valley();
        var target = new Vector2(500, 300);
        var fm = b.PlayerArtillery(target, FireKind.He)!;
        Assert.True(fm.Artillery);
        var landed = new List<Vector2>();
        float first = -1f;
        while (fm.Status != FireMissionStatus.Done && b.Time < 200f)
        {
            b.Step();
            foreach (var i in b.Impacts)
                if (i.Heavy && i.Side == Side.Blue) { landed.Add(i.Position); if (first < 0f) first = b.Time; }
        }
        Assert.Equal(FireMissionStatus.Done, fm.Status);
        Assert.Equal(b.Rules.Support.ArtilleryRounds, landed.Count);
        var sr = b.Rules.Support;
        Assert.True(first >= sr.ArtilleryCallDelay + sr.ArtillerySetUpTime + sr.ArtilleryFlightTime - 0.2f, $"first shell at {first:0} s");
        Assert.True(landed.Average(p => Vector2.Distance(p, target)) < 100f);
        Assert.Equal(48 - landed.Count, b.Batteries.Single(x => x.Side == Side.Blue).AmmoHe);
    }

    [Fact]
    public void EnemyMenAtAnHqWithNoneOfItsOwnOverrunItAndWinTheBattle()
    {
        var b = Valley();
        var hq = b.Hq(Side.Red)!;
        Assert.NotNull(hq.Staff);
        foreach (var s in hq.Staff!.Members) s.Status = SoldierStatus.Dead;
        PutSquadAt(Co(b, "A Company").Squads[0], hq.Position + new Vector2(-6f, 0f));
        Run(b, b.Rules.Battalion.HqCaptureTime + 3f);
        Assert.True(hq.Destroyed);
        Assert.Equal(Side.Blue, b.Result?.Winner);
        Assert.Contains("overran", b.Result!.Reason);
    }

    [Fact]
    public void AnHqStillDefendedIsNotOverrun()
    {
        var b = Valley();
        var hq = b.Hq(Side.Red)!;
        PutSquadAt(Co(b, "A Company").Squads[0], hq.Position + new Vector2(-6f, 0f));
        Run(b, b.Rules.Battalion.HqCaptureTime + 3f);
        Assert.False(hq.Destroyed);
    }

    [Fact]
    public void OlderScenariosAreNotWonByOverrunningAnHq()
    {
        var b = Battle.Load(TestData.Source, "scenarios/farmhouse.json", 1);
        Assert.False(b.HqVictory);
        Assert.Empty(b.Battalions);
    }
}

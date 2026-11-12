using GroundWar.Sim.Data;
using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

/// <summary>The realism report and the drill scenarios (docs/systems/realism-report.md). These check that the
/// report measures properly, not that the battles are realistic yet: making the numbers come right is the job of
/// the morale, squad drill and commander work that follows.</summary>
public class RealismTests
{
    private static readonly RealismTargets Targets = RealismTargets.Load(TestData.Source);

    [Fact]
    public void EveryTargetBelongsToAMeasureAndIsARange()
    {
        var ids = new RealismTotals().Lines().Select(l => l.Id).ToHashSet();
        Assert.NotEmpty(Targets.Targets);
        foreach (var (id, range) in Targets.Targets)
        {
            Assert.Contains(id, ids);
            Assert.Equal(2, range.Length);
            Assert.True(range[0] < range[1], id);
        }
    }

    [Theory]
    [InlineData("scenarios/drill_mg.json")]
    [InlineData("scenarios/drill_flanked.json")]
    [InlineData("scenarios/drill_ambush.json")]
    public void EachDrillIsAFightThatEnds(string scenario)
    {
        Assert.Contains(scenario, Targets.Suite);
        var battle = Battle.Load(TestData.Source, scenario, 1);
        var totals = RealismProbe.Run(battle);
        Assert.NotNull(battle.Result);
        Assert.True(battle.Time <= battle.Scenario.TimeLimit + 1f);
        Assert.True(totals.SmallArmsShots > 20, "the two sides never fought");
        Assert.True(totals.Casualties + totals.Fled > 0, "nobody was hit or ran");
    }

    [Fact]
    public void EverySuiteScenarioLoads()
    {
        foreach (string scenario in Targets.Suite)
            Assert.NotEmpty(Battle.Load(TestData.Source, scenario, 1).Soldiers);
    }

    [Fact]
    public void WatchingABattleDoesNotChangeIt()
    {
        var plain = Battle.Load(TestData.Source, "scenarios/drill_ambush.json", 5);
        var plainResult = plain.RunToEnd();
        var watched = Battle.Load(TestData.Source, "scenarios/drill_ambush.json", 5);
        RealismProbe.Run(watched);
        Assert.Equal(plainResult.Winner, watched.Result!.Winner);
        Assert.Equal(plainResult.Time, watched.Result.Time);
        Assert.Equal(plain.Soldiers.Count(s => s.Status == SoldierStatus.Dead), watched.Soldiers.Count(s => s.Status == SoldierStatus.Dead));
    }

    [Fact]
    public void TheCountsAddUp()
    {
        var battle = Battle.Load(TestData.Source, "scenarios/squad_attack.json", 2);
        var t = RealismProbe.Run(battle);
        Assert.Equal(1, t.Battles);
        Assert.Equal(battle.Soldiers.Count, t.Men); // no tanks in this one
        Assert.Equal(battle.Soldiers.Count(s => s.Status == SoldierStatus.Dead), t.Killed);
        Assert.Equal(battle.Soldiers.Count(s => s.Status == SoldierStatus.Wounded), t.Wounded);
        Assert.Equal(battle.Soldiers.Sum(s => s.ShotsFired), t.SmallArmsShots);
        Assert.Equal(t.Casualties, t.ByRifle + t.ByMachineGun);
        Assert.Equal(t.SmallArmsShots, t.ShotRanges.Sum());
        Assert.True(t.PinnedSeconds <= t.UnderFireSeconds);
        Assert.True(t.BunchedSamples <= t.MovingSamples);
        Assert.True(t.SquadsBroke <= t.SquadsFought);
        if (battle.Result!.Winner != null)
        {
            Assert.Equal(t.Men, t.WinnerMen + t.LoserMen);
            Assert.Equal(t.Casualties, t.WinnerCasualties + t.LoserCasualties);
        }
    }

    [Fact]
    public void BattlesAddTogether()
    {
        var a = RealismProbe.Run(Battle.Load(TestData.Source, "scenarios/drill_mg.json", 1));
        var b = RealismProbe.Run(Battle.Load(TestData.Source, "scenarios/drill_mg.json", 2));
        var sum = new RealismTotals();
        sum.Add(a);
        sum.Add(b);
        Assert.Equal(2, sum.Battles);
        Assert.Equal(a.Men + b.Men, sum.Men);
        Assert.Equal(a.SmallArmsShots + b.SmallArmsShots, sum.SmallArmsShots);
        Assert.Equal(a.Endings.Values.Sum() + b.Endings.Values.Sum(), sum.Endings.Values.Sum());
    }

    [Fact]
    public void ALineIsMarkedAgainstItsTarget()
    {
        var targets = new RealismTargets { Targets = { ["killedShare"] = new[] { 20f, 35f }, ["loserCasualties"] = new[] { 10f, 40f } } };
        var t = new RealismTotals { Battles = 1, Killed = 3, Wounded = 7, LoserMen = 10, LoserCasualties = 8 };
        var lines = t.Lines(targets).ToDictionary(l => l.Id);
        Assert.True(lines["killedShare"].Ok);            // 30% killed: inside 20-35
        Assert.False(lines["loserCasualties"].Ok);       // 80% of the loser hit: outside 10-40
        Assert.Null(lines["squadLossAtBreak"].Ok);       // no squad broke: nothing to mark
        Assert.Null(lines["byRifle"].Ok);                // information only
        Assert.Equal((1, 2), t.Score(targets));
        Assert.Contains("MISS", t.ToText(targets));
    }

    [Fact]
    public void EndingsAreCountedWithoutSideNames()
    {
        Assert.Equal(RealismProbe.Ending("Blue took the objective"), RealismProbe.Ending("Time up: Red holds the objective"));
        Assert.Equal(RealismProbe.Ending("Blue can no longer fight"), RealismProbe.Ending("Red can no longer fight"));
        Assert.NotEqual(RealismProbe.Ending("Blue's attack failed"), RealismProbe.Ending("Blue's attack stalled (called a death trap)"));
    }
}

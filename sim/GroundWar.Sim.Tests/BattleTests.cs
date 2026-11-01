using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

public class BattleTests
{
    [Fact]
    public void SameSeedGivesSameBattle()
    {
        var a = TestData.Firefight(42);
        var b = TestData.Firefight(42);
        for (int i = 0; i < 1500; i++) { a.Step(); b.Step(); }
        for (int i = 0; i < a.Soldiers.Count; i++)
        {
            Assert.Equal(a.Soldiers[i].Position, b.Soldiers[i].Position);
            Assert.Equal(a.Soldiers[i].Status, b.Soldiers[i].Status);
            Assert.Equal(a.Soldiers[i].Morale, b.Soldiers[i].Morale);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void BattleEndsWithinTimeLimit(ulong seed)
    {
        var battle = TestData.Firefight(seed);
        var result = battle.RunToEnd();
        Assert.True(result.Time <= battle.Scenario.TimeLimit + 0.01f);
        Assert.True(battle.Soldiers.Sum(s => s.ShotsFired) > 50, "there should be a real firefight");
    }

    [Fact]
    public void BattlesUsuallyEndInABreakNotATimeout()
    {
        int decided = 0;
        for (ulong seed = 1; seed <= 10; seed++)
            if (TestData.Firefight(seed).RunToEnd().Winner != null) decided++;
        Assert.True(decided >= 7, $"only {decided}/10 battles were decided");
    }

    [Fact]
    public void SoldiersUseCoverUnderFire()
    {
        var battle = TestData.Firefight(5);
        battle.RunToEnd();
        float underFire = 0, inCover = 0;
        foreach (var s in battle.Soldiers.Where(s => s.Side == Side.Red))
        {
            underFire += s.TimeUnderFire;
            inCover += s.TimeInCover;
        }
        Assert.True(underFire > 0);
        Assert.True(inCover > underFire * 0.5f, $"defenders were in cover {inCover:0}s vs under fire {underFire:0}s");
    }

    [Fact]
    public void SuppressionFadesWhenTheShootingStops()
    {
        var battle = TestData.Firefight(1);
        var s = battle.Soldiers[0];
        s.Suppression = 80;
        s.LastSuppressedTime = 0;
        for (int i = 0; i < 100; i++) battle.Step(); // 10 s, enemy too far to spot at the start? doesn't matter: decay must happen
        Assert.True(s.Suppression < 80);
    }

    [Fact]
    public void BrokenSoldierRunsForTheRear()
    {
        var battle = TestData.Firefight(1);
        battle.Step();
        var s = battle.Soldiers.First(x => x.Side == Side.Blue);
        float startX = s.Position.X;
        s.Morale = 5;
        for (int i = 0; i < 50; i++) battle.Step();
        Assert.Equal(MoraleState.Broken, s.MoraleState);
        Assert.Equal(SoldierAction.Flee, s.Action);
        Assert.True(s.Position.X < startX || s.Status == SoldierStatus.Routed, "Blue's rear is west");
    }

    [Fact]
    public void LeaderIsReplacedWhenHeFalls()
    {
        var battle = TestData.Firefight(1);
        var group = battle.Groups[0];
        var leader = group.Leader!;
        leader.Status = SoldierStatus.Dead;
        battle.Step();
        Assert.NotNull(group.Leader);
        Assert.NotSame(leader, group.Leader);
        Assert.True(group.Leader!.IsLeader);
    }
}

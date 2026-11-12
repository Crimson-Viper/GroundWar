using System.Numerics;
using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

/// <summary>Squad cohesion: the squad's nerve as a unit (docs/systems/morale-and-suppression.md).</summary>
public class CohesionTests
{
    private static Battle Load(string name, ulong seed = 1) => Battle.Load(TestData.Source, $"scenarios/{name}.json", seed);
    private static Group Squad(Battle b, Side side) => b.Groups.First(g => g.Side == side && g.HasCohesion);
    private static void Run(Battle b, float seconds) { for (int i = 0; i < seconds * 10; i++) b.Step(); }
    private static void RemoveSide(Battle b, Side side) { foreach (var s in b.Soldiers.Where(s => s.Side == side)) s.Status = SoldierStatus.Dead; }

    [Fact]
    public void AFreshSquadIsSteady()
    {
        var b = Load("drill_mg");
        Run(b, 3);
        var g = Squad(b, Side.Blue);
        Assert.Equal(100f, g.Cohesion);
        Assert.Equal(SquadNerve.Steady, g.Nerve);
        Assert.False(g.Flanked);
    }

    [Fact]
    public void LossesShakeASquadAndCapHowFarItCanRecover()
    {
        var b = Load("drill_mg");
        RemoveSide(b, Side.Red);
        var g = Squad(b, Side.Blue);
        foreach (var m in g.Members.Where(m => !m.IsLeader).Take(3)) m.Status = SoldierStatus.Wounded;
        Run(b, 2);
        var co = b.Rules.Cohesion;
        Assert.True(g.Cohesion < co.ShakenBelow, $"cohesion {g.Cohesion:0} after 3 of 10 hit");
        Assert.NotEqual(SquadNerve.Steady, g.Nerve);
        // Left in peace it recovers, but never past the ceiling its losses set.
        Run(b, 120);
        Assert.Equal(b.CohesionCeiling(g), g.Cohesion, 1);
        Assert.True(b.CohesionCeiling(g) < co.ShakenBelow);
    }

    [Fact]
    public void ASquadThatHasLostFourInTenCannotRally()
    {
        var b = Load("drill_mg");
        var g = Squad(b, Side.Blue);
        foreach (var m in g.Members.Take(4)) m.Status = SoldierStatus.Wounded;
        Assert.True(b.CohesionCeiling(g) < b.Rules.Cohesion.RallyAt);
    }

    [Fact]
    public void ABrokenSquadRunsBackTogetherAndRalliesWhenLeftAlone()
    {
        var b = Load("drill_mg");
        var g = Squad(b, Side.Blue);
        Run(b, 60); // out into the pasture first, so there is room behind it
        RemoveSide(b, Side.Red);
        var where = g.Centroid;
        g.Cohesion = 5f;
        Run(b, 2);
        Assert.True(g.HasBroken);
        Assert.Equal(SquadNerve.Broken, g.Nerve);
        Assert.All(g.Members.Where(m => m.IsActive), m => Assert.Equal(MoraleState.Broken, m.MoraleState));
        Assert.NotNull(g.RallyPoint);
        Assert.True(Vector2.Dot(g.RallyPoint!.Value - where, g.Rear) > 40f, "the rally point is not to the rear");

        Run(b, 120);
        Assert.False(g.HasBroken, $"still broken: cohesion {g.Cohesion:0}, {g.Members.Count(m => m.IsActive)} men, {g.Members.Count(m => m.IsActive && Vector2.Distance(m.Position, g.RallyPoint!.Value) < 20f)} at the rally point, last under fire {b.Time - g.LastUnderFireTime:0} s ago");
        Assert.Equal(1, g.Rallies);
        Assert.Equal(GroupOrder.Hold, g.Order);
        Assert.True(g.CountEffective >= g.CountActive - 1);
        Assert.All(g.Members.Where(m => m.IsActive), m => Assert.NotEqual(SoldierStatus.Routed, m.Status));
        Assert.True(Vector2.Distance(g.Centroid, g.RallyPoint!.Value) < 25f, "the squad did not gather at its rally point");
        Assert.Contains(b.Log.Entries, e => e.Text.Contains("has rallied"));
    }

    [Fact]
    public void ASquadWithItsBackToTheEdgeRunsOffTheField()
    {
        var b = Load("drill_mg");
        var g = Squad(b, Side.Blue); // starts 24 m from its own edge
        RemoveSide(b, Side.Red);
        g.Cohesion = 5f;
        Run(b, 30);
        Assert.Null(g.RallyPoint);
        Assert.Contains(g.Members, m => m.Status == SoldierStatus.Routed);
    }

    [Fact]
    public void AWaveringAttackerGivesUpAndGoesInAgainOnceSteady()
    {
        var b = Load("drill_mg");
        var g = Squad(b, Side.Blue);
        Run(b, 60);
        RemoveSide(b, Side.Red);
        g.Cohesion = 30f;
        Run(b, 3);
        Assert.Equal(SquadNerve.Wavering, g.Nerve);
        Assert.Equal(GroupOrder.Hold, g.Order);
        Assert.True(g.GaveUp);
        Assert.True(g.AttackFailed);
        Run(b, 90);
        Assert.Equal(SquadNerve.Steady, g.Nerve);
        Assert.Equal(GroupOrder.Advance, g.Order);
        Assert.False(g.AttackFailed);
    }

    [Fact]
    public void FireFromAnEnemyNobodyHadSeenIsAShockAndWorseUpClose()
    {
        float Shock(float range)
        {
            var b = Load("drill_mg");
            RemoveSide(b, Side.Red);
            var g = Squad(b, Side.Blue);
            Run(b, 2);
            foreach (var m in g.Members.Take(2))
            {
                m.LastShotAtTime = b.Time;
                m.LastShotFrom = m.Position + new Vector2(range, 0);
            }
            Run(b, 1.2f);
            return 100f - g.Cohesion;
        }
        float far = Shock(200f), close = Shock(50f);
        Assert.True(far >= b_SurpriseShock() - 1f, $"no shock at 200 m ({far:0.0})");
        Assert.True(close >= far * 1.8f, $"an ambush at 50 m ({close:0.0}) should shake a squad about twice as much as fire from 200 m ({far:0.0})");
    }

    private static float b_SurpriseShock() => TestData.Data.Rules.Cohesion.SurpriseShock;

    [Fact]
    public void ASquadHitFromTheFlankFeelsItAndUsuallyBreaksBeforeHalfAreDown()
    {
        int flanked = 0, brokeEarly = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            var b = Load("drill_flanked", seed);
            b.RunToEnd();
            var red = Squad(b, Side.Red);
            if (b.Log.Entries.Any(e => e.Text.Contains("Red 1st Squad: fire from the flank"))) flanked++;
            int hit = red.Members.Count(m => m.Status is SoldierStatus.Dead or SoldierStatus.Wounded);
            if (hit * 2 < red.StartStrength) brokeEarly++;
        }
        Assert.True(flanked >= 4, $"the squad noticed it was flanked in only {flanked}/6 battles");
        Assert.True(brokeEarly >= 4, $"the flanked squad kept under half its men in only {brokeEarly}/6 battles");
    }

    [Fact]
    public void HeadquartersAndTanksHaveNoSquadNerve()
    {
        var b = Load("battalion");
        Assert.Contains(b.Groups, g => g.IsHqStaff);
        Assert.All(b.Groups.Where(g => g.IsHqStaff || g.IsCompanyHq), g => Assert.False(g.HasCohesion));
        var t = Load("tank_platoons");
        Assert.All(t.Groups.Where(g => g.IsVehicles || g.IsCrew), g => Assert.False(g.HasCohesion));
    }
}

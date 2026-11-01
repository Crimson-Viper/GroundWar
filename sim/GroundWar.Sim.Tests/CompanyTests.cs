using System.Numerics;
using GroundWar.Sim.AI;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Data;
using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

/// <summary>Milestone 5: the company commander. See docs/systems/company-commander.md.</summary>
public class CompanyTests
{
    private static Battle Farm(ulong seed = 1) => Battle.Load(TestData.Source, "scenarios/farmhouse.json", seed);
    private static void Run(Battle b, float seconds) { int n = (int)(seconds / Battle.Dt); for (int i = 0; i < n; i++) b.Step(); }
    private static void RunUntil(Battle b, Func<bool> done, float limit = 300f)
    {
        float end = b.Time + limit;
        while (!done() && b.Time < end) b.Step();
        Assert.True(done(), "timed out waiting");
    }
    private static Company A(Battle b) => b.Companies.Single(c => c.Name == "A Company");

    [Fact]
    public void TheCommanderBriefsBackHisPlanAndWaitsForHq()
    {
        var b = Farm();
        var c = A(b);
        RunUntil(b, () => c.State == CompanyState.BriefBack);
        Assert.NotNull(c.Plan);
        Assert.True(c.Plan!.Phases.Count >= 3, "an attack plan has phases");
        Assert.True(c.Options.Count >= 2, "he weighs up more than one way of doing it");
        Assert.Contains(b.Messages, m => !m.Down && m.Kind == MessageKind.BriefBack && m.Group == c.HqGroup);
        Assert.All(c.Squads, g => Assert.Null(g.LastOrderReceived)); // nobody moves before HQ answers

        RunUntil(b, () => c.State == CompanyState.Executing);
        RunUntil(b, () => c.Squads.All(g => g.LastOrderReceived != null), 120f);
        Assert.Contains(c.Squads, g => g.LastOrderReceived!.IsWaypoint);
    }

    [Fact]
    public void AtTheFarmhouseHePinsTheFarmAndGoesRoundTheCoveredLeft()
    {
        var b = Farm();
        var c = A(b);
        RunUntil(b, () => c.State == CompanyState.BriefBack);
        Assert.Equal(PlanKind.PinAndFlankLeft, c.Plan!.Kind);
        var frontal = c.Options.Single(p => p.Kind == PlanKind.FrontalAssault);
        Assert.True(c.Plan.ExpectedLoss < frontal.ExpectedLoss, "the covered flank should cost less than going straight in");
        Assert.NotNull(c.Plan.Support);
        Assert.Equal(2, c.Plan.Support!.Members.Count(m => m.Weapon.IsMachineGun)); // the squad with two guns gives the covering fire
    }

    [Fact]
    public void RejectingTheBriefBackStandsTheCompanyDown()
    {
        var b = Farm();
        var c = A(b);
        RunUntil(b, () => b.AwaitingApproval(c));
        b.RejectBriefBack(c);
        RunUntil(b, () => c.State == CompanyState.AwaitingOrders, 60f);
        Run(b, 20f);
        Assert.All(c.Squads, g => Assert.Null(g.LastOrderReceived));
    }

    [Fact]
    public void ApprovingStartsThePlanAtOnce()
    {
        var b = Farm();
        var c = A(b);
        RunUntil(b, () => b.AwaitingApproval(c));
        float heard = b.Time;
        b.ApproveBriefBack(c);
        RunUntil(b, () => c.State == CompanyState.Executing, 30f);
        Assert.True(b.Time - heard < b.Rules.Comms.BriefBackWait, "approving should beat the automatic go-ahead");
    }

    [Fact]
    public void ACautiousCommanderRefusesADeathTrapAndObeysWhenHqInsists()
    {
        var b = Farm();
        var c = A(b);
        c.Traits.Aggression = 1; c.Traits.Caution = 5; c.Traits.Composure = 1;
        RunUntil(b, () => c.State == CompanyState.Refused);
        Assert.Contains(b.Messages, m => !m.Down && m.Kind == MessageKind.Refusal && m.Group == c.HqGroup);
        Assert.Contains("expect to lose", c.RefusalReason);
        Assert.All(c.Squads, g => Assert.Null(g.LastOrderReceived)); // he holds

        // HQ sends the same order again: he does it.
        b.SendOrder(c, new Order { Kind = OrderKind.Attack, Objective = c.RefusedOrder!.Objective, Radius = c.RefusedOrder.Radius });
        RunUntil(b, () => c.State == CompanyState.BriefBack);
        Assert.True(c.Insisted);
    }

    [Fact]
    public void BoldMenAcceptMoreRiskThanCautiousOnes()
    {
        var r = new CompanyRules();
        float bold = CompanyCommander.DeathTrapThreshold(new TraitsDef { Aggression = 5, Caution = 1, Composure = 5 }, r);
        float average = CompanyCommander.DeathTrapThreshold(new TraitsDef(), r);
        float careful = CompanyCommander.DeathTrapThreshold(new TraitsDef { Aggression = 1, Caution = 5, Composure = 1 }, r);
        Assert.True(bold > average && average > careful);
        Assert.Equal(r.DeathTrapLoss, average, 3);
        Assert.True(CompanyCommander.ThinkInterval(new TraitsDef { Competence = 5 }) < CompanyCommander.ThinkInterval(new TraitsDef { Competence = 1 }));
    }

    [Fact]
    public void SquadsReportToTheirCommanderNotBattalion()
    {
        var b = Farm();
        var c = A(b);
        Run(b, 240f);
        Assert.False(c.CommanderDown && b.Time < 200f);
        var fromSquads = b.Messages.Where(m => !m.Down && c.Squads.Contains(m.Group) && m.SentTime < 200f).ToList();
        Assert.NotEmpty(fromSquads);
        if (!c.CommanderDown) Assert.All(fromSquads, m => Assert.Equal(c, m.Company));
        // ...and the commander reports to battalion himself.
        Assert.Contains(b.Messages, m => !m.Down && m.Group == c.HqGroup && m.Company == null);
    }

    [Fact]
    public void InsideTheCompanyHeShoutsWhenCloseAndSendsARunnerWhenNot()
    {
        var b = Farm();
        var c = A(b);
        var near = c.Squads.OrderBy(g => Vector2.Distance(g.Centroid, c.HqGroup.Centroid)).First();
        Assert.Equal(MessageMethod.Voice, b.CompanyMethod(c, near));
        foreach (var m in c.HqGroup.Members) m.Position = new Vector2(10f, 20f); // the HQ wanders off
        var far = c.Squads.OrderBy(g => Vector2.Distance(g.Centroid, c.HqGroup.Centroid)).Last();
        Assert.Equal(MessageMethod.Runner, b.CompanyMethod(c, far));
    }

    [Fact]
    public void TheFarmhouseBattleIsDecidedAndRepeatable()
    {
        var r1 = Farm(3).RunToEnd();
        var r2 = Farm(3).RunToEnd();
        Assert.Equal(r1.Winner, r2.Winner);
        Assert.Equal(r1.Time, r2.Time);
        Assert.Equal(r1.Reason, r2.Reason);
    }
}

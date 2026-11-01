using System.Numerics;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

public class CommsTests
{
    private static Battle Orders(ulong seed = 1) => Battle.Load(TestData.Source, "scenarios/orders_under_fire.json", seed);
    private static void Run(Battle b, float seconds) { int n = (int)(seconds / Battle.Dt); for (int i = 0; i < n; i++) b.Step(); }

    [Fact]
    public void AnOrderByTelephoneArrivesAfterTheDelayNotBefore()
    {
        var b = Orders();
        var g = b.Groups.Single(x => x.Name == "Blue 1st Squad");
        Run(b, 5.05f); // the scenario's order goes out at 5 s
        var m = b.Messages.Single(x => x.Down && x.Group == g);
        Assert.Equal(MessageMethod.Phone, m.Method);
        Assert.Equal(GroupOrder.Hold, g.Order);
        Run(b, m.ArriveTime - b.Time - 0.5f);
        Assert.Equal(GroupOrder.Hold, g.Order); // still waiting
        Run(b, 1f);
        Assert.Equal(MessageStatus.Delivered, m.Status);
        Assert.Equal(GroupOrder.Suppress, g.Order);
    }

    [Fact]
    public void ASquadWithNoPhoneGetsItsOrderByRunner()
    {
        var b = Orders();
        var g = b.Groups.Single(x => x.Name == "Blue 2nd Squad");
        Run(b, 5.05f);
        var m = b.Messages.Single(x => x.Down && x.Group == g);
        Assert.Equal(MessageMethod.Runner, m.Method);
        float distance = Vector2.Distance(b.Hq(Side.Blue)!.Position, Battle.SquadPost(g));
        Run(b, distance / b.Rules.Comms.RunnerSpeed * 0.5f);
        Assert.Equal(MessageStatus.InTransit, m.Status); // halfway there
        Assert.True(Vector2.Distance(m.RunnerPosition, b.Hq(Side.Blue)!.Position) > 5f, "the runner should be on his way");
        Run(b, distance / b.Rules.Comms.RunnerSpeed + 5f);
        Assert.Equal(MessageStatus.Delivered, m.Status);
        Assert.Equal(GroupOrder.Attack, g.Order);
    }

    [Fact]
    public void ACutLineMeansARunner()
    {
        var b = Orders();
        var g = b.Groups.Single(x => x.Name == "Blue 1st Squad");
        Assert.Equal(MessageMethod.Phone, b.BestMethod(g, b.Hq(Side.Blue)!, down: true, new Order { Kind = OrderKind.Hold }));
        var line = b.PhoneFor(g)!;
        b.CutLinesNear(Vector2.Lerp(line.Points[0], line.Points[1], 0.5f));
        Assert.True(line.Cut);
        Assert.Equal(MessageMethod.Runner, b.BestMethod(g, b.Hq(Side.Blue)!, down: true, new Order { Kind = OrderKind.Hold }));
    }

    [Fact]
    public void HaltGoesByFlareWhenTheSquadCanSeeHq()
    {
        var b = Orders();
        var g = b.Groups.Single(x => x.Name == "Blue 2nd Squad");
        var m = b.SendOrder(g, new Order { Kind = OrderKind.Halt })!;
        Assert.Equal(MessageMethod.Flare, m.Method);
    }

    [Fact]
    public void WithoutAnHqOrdersAreInstant()
    {
        var b = TestData.Firefight(1);
        var g = b.Groups.First(x => x.Side == Side.Blue);
        Assert.Null(b.SendOrder(g, new Order { Kind = OrderKind.Halt }));
        Assert.Equal(GroupOrder.Hold, g.Order);
    }

    [Fact]
    public void ThePlayerSeesTheEnemyOnlyWhenAReportReachesHq()
    {
        var b = Orders();
        var g = b.Groups.Single(x => x.Name == "Blue 1st Squad");
        // Step until the squad first sees someone.
        while (g.Knowledge.Count == 0 && b.Time < 120f) b.Step();
        Assert.True(g.Knowledge.Count > 0);
        Assert.Equal(0, b.Knowledge(Side.Blue).Count); // HQ hasn't heard yet
        // The sergeant writes his contact report within a second; it then has to travel.
        while (!b.Messages.Any(x => !x.Down && x.Side == Side.Blue)) b.Step();
        Assert.Equal(0, b.Knowledge(Side.Blue).Count);
        var report = b.Messages.Where(x => !x.Down && x.Side == Side.Blue).OrderBy(x => x.ArriveTime).First();
        while (report.Status == MessageStatus.InTransit) b.Step();
        Assert.True(b.Knowledge(Side.Blue).Count > 0);
    }
}

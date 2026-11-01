using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;
using Xunit;

namespace GroundWar.Sim.Tests;

/// <summary>Milestone 6: replays (seed + player commands) and the after-action report.</summary>
public class ReplayTests
{
    private static Battle Farm(ulong seed = 2) => Battle.Load(TestData.Source, "scenarios/farmhouse.json", seed);

    /// <summary>Plays the farmhouse with a player who rejects the first plan and then sends his own attack.</summary>
    private static Battle PlayWithOrders(out List<PlayerCommand> commands)
    {
        var b = Farm();
        var c = b.Companies.Single(x => x.Side == Side.Blue);
        while (!b.AwaitingApproval(c)) b.Step();
        b.PlayerReject(c);
        b.RunUntil(b.Time + 30f);
        b.PlayerOrder(c.HqGroup, new Order { Kind = OrderKind.Attack, Objective = new System.Numerics.Vector2(299, 152), Radius = 22f });
        while (!b.AwaitingApproval(c) && b.Time < 400f) b.Step();
        if (b.AwaitingApproval(c)) b.PlayerApprove(c);
        b.RunUntil(b.Time + 200f);
        commands = b.Commands.ToList();
        return b;
    }

    [Fact]
    public void TheSameSeedAndCommandsGiveTheSameBattle()
    {
        var original = PlayWithOrders(out var commands);
        Assert.Equal(3, commands.Count);
        Assert.Equal(CommandKind.Reject, commands[0].Kind);

        var replay = Farm();
        replay.LoadReplay(commands);
        replay.RunUntil(original.Time);
        Assert.Equal(original.Tick, replay.Tick);
        Assert.False(replay.Replaying);
        Assert.Equal(commands.Count, replay.Commands.Count);
        for (int i = 0; i < original.Soldiers.Count; i++)
        {
            Assert.Equal(original.Soldiers[i].Position, replay.Soldiers[i].Position);
            Assert.Equal(original.Soldiers[i].Status, replay.Soldiers[i].Status);
        }
    }

    [Fact]
    public void WithoutTheCommandsItIsADifferentBattle()
    {
        var original = PlayWithOrders(out _);
        var plain = Farm();
        plain.RunUntil(original.Time);
        Assert.Contains(Enumerable.Range(0, original.Soldiers.Count), i => original.Soldiers[i].Position != plain.Soldiers[i].Position);
    }

    [Fact]
    public void TheReportAddsUpAndTellsTheStory()
    {
        var b = Farm(1);
        b.RunToEnd();
        var r = AfterActionReport.Build(b);
        int blueMen = b.Soldiers.Count(s => s.Side == Side.Blue);
        Assert.Equal(blueMen, r.Blue.Men);
        Assert.Equal(blueMen, r.Blue.Killed + r.Blue.Wounded + r.Blue.Fled + r.Blue.Fighting +
                              b.Soldiers.Count(s => s.Side == Side.Blue && s.IsActive && !s.IsEffective));
        Assert.Equal(b.Groups.Count, r.Squads.Count);
        Assert.Equal(r.Timeline.OrderBy(t => t.Time).Select(t => t.Time), r.Timeline.Select(t => t.Time));
        Assert.Contains(r.Timeline, t => t.Text.StartsWith("Battle over"));
        Assert.Contains(r.Timeline, t => t.Text.StartsWith("First shots"));
        var hale = r.Companies.Single(c => c.Commander == "Capt. Hale");
        Assert.Contains(hale.Story, s => s.Text.Contains("plans:"));
        string text = r.ToText();
        Assert.Contains("AFTER-ACTION REPORT", text);
        Assert.Contains(b.Result!.Reason, text);
    }
}

using GroundWar.Sim.Comms;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

public enum CommandKind { Order, Approve, Reject }

/// <summary>Something the player did, and the tick it was done after. A battle is the seed plus these: play the
/// same seed with the same commands and you get the same battle again (a replay).</summary>
public sealed record PlayerCommand(int Tick, CommandKind Kind, int GroupId, int CompanyId, Order? Order);

/// <summary>
/// The player's commands (milestone 6). Everything the player does to the battle goes through here, so it can be
/// recorded and played back. See docs/systems/replays-and-reports.md.
/// </summary>
public sealed partial class Battle
{
    private readonly List<PlayerCommand> _commands = new();
    private readonly Queue<PlayerCommand> _replay = new();
    private bool _playingBack;

    /// <summary>Everything the player has done so far, in order.</summary>
    public IReadOnlyList<PlayerCommand> Commands => _commands;
    /// <summary>True while a recorded command is still waiting to be played back.</summary>
    public bool Replaying => _replay.Count > 0;

    /// <summary>The player sends an order to a squad or (through its HQ group) a company.</summary>
    public Message? PlayerOrder(Group g, Order order)
    {
        TakeOver();
        _commands.Add(new PlayerCommand(Tick, CommandKind.Order, g.Id, -1, order));
        return SendOrder(g, order);
    }

    public void PlayerApprove(Company c)
    {
        if (!AwaitingApproval(c)) return;
        TakeOver();
        _commands.Add(new PlayerCommand(Tick, CommandKind.Approve, -1, c.Id, null));
        ApproveBriefBack(c);
    }

    public void PlayerReject(Company c)
    {
        if (!AwaitingApproval(c)) return;
        TakeOver();
        _commands.Add(new PlayerCommand(Tick, CommandKind.Reject, -1, c.Id, null));
        RejectBriefBack(c);
    }

    /// <summary>The player does something himself during a replay: from here on it's his battle, and the rest of
    /// the recording is dropped.</summary>
    private void TakeOver()
    {
        if (!_playingBack) _replay.Clear();
    }

    /// <summary>Play these commands back as the battle runs. Call on a new battle with the same seed (and scenario)
    /// as the one they were recorded in. Anything the player does on top is recorded as well.</summary>
    public void LoadReplay(IEnumerable<PlayerCommand> commands)
    {
        foreach (var c in commands.OrderBy(c => c.Tick)) _replay.Enqueue(c);
    }

    /// <summary>Recorded commands were given after tick N, before tick N+1: play them at the same point.</summary>
    private void PlayReplay()
    {
        while (_replay.Count > 0 && _replay.Peek().Tick <= Tick)
        {
            var c = _replay.Dequeue();
            _playingBack = true;
            switch (c.Kind)
            {
                case CommandKind.Order:
                    var o = c.Order!;
                    // An order naming a company names the one in this battle, not the battle it was recorded in.
                    if (o.AttachTo != null)
                        o = new Order { Kind = o.Kind, Objective = o.Objective, Radius = o.Radius, AttachTo = _companies[o.AttachTo.Id] };
                    PlayerOrder(_groups[c.GroupId], o);
                    break;
                case CommandKind.Approve: PlayerApprove(_companies[c.CompanyId]); break;
                case CommandKind.Reject: PlayerReject(_companies[c.CompanyId]); break;
            }
            _playingBack = false;
        }
    }

    /// <summary>When the first shot of the battle was fired (-1 = not yet), and by whom.</summary>
    public float FirstShotTime { get; private set; } = -1f;
    public string FirstShotText { get; private set; } = "";

    private void FirstShot(Soldier s)
    {
        FirstShotTime = Time;
        FirstShotText = $"First shots: {s.Label} of {s.Group.Name} ({s.Side}) opens fire";
    }

    /// <summary>Run on, without drawing, until the given time (used to jump about in a replay).</summary>
    public void RunUntil(float time)
    {
        while (Time < time - Dt * 0.5f) Step();
    }
}

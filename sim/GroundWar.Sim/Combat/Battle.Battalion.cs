using System.Numerics;
using GroundWar.Sim.AI;
using GroundWar.Sim.Comms;
using GroundWar.Sim.Map;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>
/// The battalion battle (milestone 7, part 2): key points, battalion commanders, artillery batteries, and HQs that
/// can be overrun. In a scenario with "victory": "Hq", destroying the enemy's battalion HQ wins the battle.
/// See docs/systems/battalion-commander.md.
/// </summary>
public sealed partial class Battle
{
    private readonly List<Battalion> _battalions = new();
    private readonly List<KeyPoint> _keyPoints = new();
    private BattalionCommander _bnCommander = null!;

    public IReadOnlyList<Battalion> Battalions => _battalions;
    public IReadOnlyList<KeyPoint> KeyPoints => _keyPoints;
    public Battalion? BattalionOf(Side side) => _battalions.FirstOrDefault(b => b.Side == side);
    /// <summary>The battle is won by destroying the enemy's battalion HQ (not by taking an objective).</summary>
    public bool HqVictory => Scenario.Victory.Equals("Hq", StringComparison.OrdinalIgnoreCase);

    private Data.BattalionRules Bn => Rules.Battalion;

    private void SetUpBattalions()
    {
        foreach (var kd in Scenario.KeyPoints)
            _keyPoints.Add(new KeyPoint { Id = _keyPoints.Count, Name = kd.Name, Position = BattleMap.V(kd.Position), Radius = kd.Radius });
        foreach (var sideDef in Scenario.Sides)
        {
            if (sideDef.Artillery is not { } ad) continue;
            var side = Enum.Parse<Side>(sideDef.Side, ignoreCase: true);
            _batteries.Add(new Battery { Side = side, Name = ad.Name, Guns = ad.Guns, AmmoHe = ad.He, AmmoSmoke = ad.Smoke });
        }
        _bnCommander = new BattalionCommander(this);
        foreach (var bd in Scenario.Battalions)
        {
            var side = Enum.Parse<Side>(bd.Side, ignoreCase: true);
            var own = _hq[(int)side] ?? throw new InvalidDataException($"Battalion for {side} needs an HQ");
            var enemy = _hq[(int)Enemy(side)] ?? throw new InvalidDataException($"Battalion for {side} needs an enemy HQ to aim at");
            var bn = new Battalion
            {
                Side = side, Commander = bd.Commander, Traits = bd.Traits,
                Posture = Enum.Parse<Posture>(bd.Posture, ignoreCase: true),
                Active = side != PlayerSide, NextThink = 2f + Rng.Range(0f, 3f),
            };
            bn.Points.AddRange(_keyPoints);
            bn.OwnHq = new KeyPoint { Id = -1, Name = own.Name, Position = own.Position, Radius = Bn.HqCaptureRadius * 2f, IsHq = true };
            bn.EnemyHq = new KeyPoint { Id = -2, Name = enemy.Name, Position = enemy.Position, Radius = Bn.HqCaptureRadius * 2f, IsHq = true };
            bn.Points.Add(bn.OwnHq);
            bn.Points.Add(bn.EnemyHq);
            foreach (var c in _companies.Where(c => c.Side == side)) bn.Companies.Add(c);
            foreach (var name in bd.Reserve)
            {
                var c = FindCompany(name) ?? throw new InvalidDataException($"Battalion reserve names unknown company '{name}'");
                bn.Tasks[c] = new BattalionTask { Kind = BattalionTaskKind.Reserve };
            }
            _battalions.Add(bn);
        }
    }

    private void UpdateBattalions()
    {
        foreach (var bn in _battalions)
            if (bn.Active && Time >= bn.NextThink) _bnCommander.Think(bn);
        if (Tick % 10 == 0) UpdateHqs();
    }

    /// <summary>A company's report has reached battalion HQ: note how it's doing.</summary>
    private void HearFromCompany(Message m)
    {
        if (BattalionOf(m.Side) is not { } bn || m.Group.Company is not { } c) return;
        var n = bn.NewsOf(c);
        n.Time = Time;
        n.Strength = m.Strength;
        n.Last = m.Kind;
        switch (m.Kind)
        {
            case MessageKind.Taken: n.TakenAt = Time; break;
            case MessageKind.Failed: n.FailedAt = Time; break;
            case MessageKind.Refusal: n.RefusedAt = Time; break;
            case MessageKind.BriefBack: n.PlanAt = Time; break;
            case MessageKind.Note when m.Plan != null: n.PlanAt = Time; break;
        }
    }

    /// <summary>The player hands his battalion over to his staff (or takes it back). Recorded, so a replay does the same.</summary>
    public void PlayerHandOver(bool staffInCommand)
    {
        if (BattalionOf(PlayerSide) is not { } bn || bn.Active == staffInCommand) return;
        TakeOver();
        _commands.Add(new PlayerCommand(Tick, CommandKind.HandOver, -1, staffInCommand ? 1 : 0, null));
        bn.Active = staffInCommand;
        bn.NextThink = Time;
        Log.Add(Time, -1, staffInCommand ? $"{bn.Commander} (your second in command) takes over the battalion" : "You take back command of the battalion", PlayerSide, LogKind.Key);
    }

    /// <summary>An HQ is overrun when enemy men are at it and none of its own are, for a while. Only in a battle
    /// won by destroying the HQ.</summary>
    private void UpdateHqs()
    {
        if (!HqVictory) return;
        float r = Bn.HqCaptureRadius;
        foreach (var hq in _hq)
        {
            if (hq == null || hq.Destroyed) continue;
            bool enemy = Hash.Near(Enemy(hq.Side), hq.Position, r).Any(s => s.IsEffective && Vector2.Distance(s.Position, hq.Position) <= r);
            bool own = Hash.Near(hq.Side, hq.Position, r).Any(s => s.IsEffective && Vector2.Distance(s.Position, hq.Position) <= r);
            if (!enemy || own) { hq.OverrunSince = -1f; continue; }
            if (hq.OverrunSince < 0f) hq.OverrunSince = Time;
            else if (Time - hq.OverrunSince >= Bn.HqCaptureTime)
            {
                hq.Destroyed = true;
                Log.Add(Time, -1, $"{hq.Name} has been overrun!", null, LogKind.Key);
            }
        }
    }

    /// <summary>The battle ended with both HQs standing: the defender wins if the attacker didn't get through,
    /// otherwise the side left clearly stronger.</summary>
    private BattleResult NoHqResult(string why)
    {
        var attackers = _battalions.Where(b => b.Posture == Posture.Attack).Select(b => b.Side).Distinct().ToList();
        if (attackers.Count == 1)
        {
            var defender = Enemy(attackers[0]);
            return new BattleResult { Winner = defender, Time = Time, Reason = $"{why}: {defender} held on, its HQ still standing" };
        }
        var r = TimeLimitResult();
        return new BattleResult { Winner = r.Winner, Time = Time, Reason = r.Reason.Replace("Time up", why) };
    }
}

using System.Numerics;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

public sealed partial class Battle
{
    /// <summary>A soldier looks around. He only sees what is in range and not behind a high wall,
    /// and spotting a man in cover far away takes luck. What he sees, his side learns.</summary>
    private void Perceive(Soldier s)
    {
        var pr = Rules.Perception;
        var knowledge = _knowledge[(int)s.Side];
        s.VisibleEnemies.Clear();
        foreach (var e in _soldiers)
        {
            if (e.Side == s.Side || !e.IsActive) continue;
            float d = Vector2.Distance(s.Position, e.Position);
            if (d > pr.ViewRange) continue;
            if (Map.SightBlocked(s.Position, e.Position)) continue;

            bool sees;
            if (d <= pr.AutoSpotRange) sees = true;
            else if (knowledge.SeenRecently(e.Id, Time, pr.SharedContactWindow)) sees = Rng.Chance(0.9f); // a mate shouted it out
            else sees = Rng.Chance(SpotChance(s, e, d));

            if (!sees) continue;
            s.VisibleEnemies.Add(e);
            knowledge.Report(e, e.Position, Time, 1f);
        }
    }

    private float SpotChance(Soldier observer, Soldier target, float distance)
    {
        var pr = Rules.Perception;
        float t = Math.Clamp((distance - pr.AutoSpotRange) / MathF.Max(pr.ViewRange - pr.AutoSpotRange, 1f), 0f, 1f);
        float p = pr.BaseSpotChance + (pr.FarSpotChance - pr.BaseSpotChance) * t;
        if (Time - target.LastFiredTime < pr.FiringWindow) p *= pr.FiringMultiplier;
        if (target.IsMoving && target.MoveMode != MoveMode.Crawl) p *= pr.MovingMultiplier;
        if (target.Stance == Stance.Prone) p *= pr.ProneMultiplier;
        if (Map.ProtectionAt(target.Position, observer.Position) >= 0.5f) p *= pr.CoverMultiplier;
        return Math.Clamp(p, 0f, 0.95f);
    }
}

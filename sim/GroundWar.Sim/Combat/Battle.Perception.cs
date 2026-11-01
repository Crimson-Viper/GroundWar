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
        var knowledge = s.Group.Knowledge;
        s.VisibleEnemies.Clear();
        var enemySide = s.Side == Side.Blue ? Side.Red : Side.Blue;
        // Nearest first. A man can only keep track of so many enemies at once.
        foreach (var e in Hash.Near(enemySide, s.Position, pr.ViewRange))
        {
            if (s.VisibleEnemies.Count >= pr.MaxTracked) break;
            float d = Vector2.Distance(s.Position, e.Position);

            // Roll the dice first and only do the (expensive) sight-line check if he might see him.
            float chance;
            if (d <= pr.AutoSpotRange) chance = 1f;
            else if (knowledge.SeenRecently(e.Id, Time, pr.SharedContactWindow)) chance = 0.9f; // a mate shouted it out
            else chance = SpotChance(e, d);
            if (chance < 1f && !Rng.Chance(chance)) continue;
            if (Map.SightBlocked(s.Position, e.Position)) continue;
            // A man behind cover is harder to pick out (only for a fresh sighting).
            if (chance < 0.9f && Map.ProtectionAt(e.Position, s.Position) >= 0.5f && !Rng.Chance(pr.CoverMultiplier)) continue;

            s.VisibleEnemies.Add(e);
            Report(s.Group, e, e.Position, 1f);
        }
    }

    private float SpotChance(Soldier target, float distance)
    {
        var pr = Rules.Perception;
        float t = Math.Clamp((distance - pr.AutoSpotRange) / MathF.Max(pr.ViewRange - pr.AutoSpotRange, 1f), 0f, 1f);
        float p = pr.BaseSpotChance + (pr.FarSpotChance - pr.BaseSpotChance) * t;
        if (Time - target.LastFiredTime < pr.FiringWindow) p *= pr.FiringMultiplier;
        if (target.IsMoving && target.MoveMode != MoveMode.Crawl) p *= pr.MovingMultiplier;
        if (target.Stance == Stance.Prone) p *= pr.ProneMultiplier;
        return Math.Clamp(p, 0f, 0.95f);
    }
}

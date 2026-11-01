using System.Collections.Generic;
using GroundWar.Sim;
using GroundWar.Sim.Units;

namespace GroundWar.Game;

/// <summary>
/// Picks what the battle cam (J) looks at (milestone 6): it remembers where shots were fired and landed over the last
/// few seconds and chooses the man in the thick of it. With fog of war on it only picks your own men, so it never
/// gives away an enemy you haven't seen. See docs/systems/spectacle.md.
/// </summary>
public sealed class ActionCamera
{
    private readonly List<(float Time, System.Numerics.Vector2 At)> _recent = new();
    private readonly List<(float Time, System.Numerics.Vector2 At)> _hits = new();

    public void AfterStep(Battle b)
    {
        foreach (var s in b.Shots) { if (s.Indirect) continue; _recent.Add((b.Time, s.From)); _recent.Add((b.Time, s.Impact)); }
        foreach (var i in b.Impacts) if (!i.Smoke) _recent.Add((b.Time, i.Position));
        foreach (var c in b.Casualties) if (b.GetSoldier(c.SoldierId) is { } v) _hits.Add((b.Time, v.Position));
        _recent.RemoveAll(r => b.Time - r.Time > 5f);
        _hits.RemoveAll(r => b.Time - r.Time > 1f);
    }

    /// <summary>The man (on your side, under fog) with the most shooting within 25 m in the last 5 s.</summary>
    public Soldier? PickHotspot(Battle b, Side playerSide, bool fog)
    {
        Soldier? best = null;
        int bestScore = 2;
        foreach (var s in b.Soldiers)
        {
            if (!s.IsEffective || (fog && s.Side != playerSide)) continue;
            int score = 0;
            foreach (var r in _recent) if (System.Numerics.Vector2.DistanceSquared(r.At, s.Position) < 625f) score++;
            // A little variety: don't keep picking the same man.
            score = score * 10 + (s.Id * 7919 + (int)b.Time) % 10;
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    /// <summary>Someone was hit within 30 m of the man we're following, just now.</summary>
    public bool DramaNear(Soldier focus, Battle b)
    {
        foreach (var h in _hits) if (System.Numerics.Vector2.DistanceSquared(h.At, focus.Position) < 900f) { _hits.Clear(); return true; }
        return false;
    }
}

using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>Squad cohesion: the squad's nerve as a unit. Each man still has his own morale (Battle.Morale.cs);
/// this is what makes a squad go to ground, give up an attack, fall back or break TOGETHER, and rally together
/// afterwards. See docs/systems/morale-and-suppression.md.</summary>
public sealed partial class Battle
{
    private void UpdateCohesion()
    {
        foreach (var g in _groups)
        {
            if (Time < g.NextCohesion || !g.HasCohesion) continue;
            float dt = g.NextCohesion <= 0f ? 1f : 1f + (Time - g.NextCohesion);
            g.NextCohesion = Time + 1f;
            UpdateCohesion(g, dt);
        }
    }

    private static int MenHit(Group g)
    {
        int n = 0;
        foreach (var m in g.Members) if (m.Status is SoldierStatus.Dead or SoldierStatus.Wounded) n++;
        return n;
    }

    /// <summary>The most cohesion the squad can have with the losses it has taken.</summary>
    public float CohesionCeiling(Group g) =>
        Math.Clamp(100f - Rules.Cohesion.CeilingPerLossPercent * 100f * MenHit(g) / MathF.Max(1, g.StartStrength), 0f, 100f);

    private void UpdateCohesion(Group g, float dt)
    {
        var co = Rules.Cohesion;
        int active = 0, pinned = 0, underFire = 0;
        foreach (var m in g.Members)
        {
            if (!m.IsActive) continue;
            active++;
            if (m.MoraleState >= MoraleState.Pinned) pinned++;
            if (m.UnderFire(Time, Rules.Ai.UnderFireWindow)) underFire++;
        }
        if (active == 0) return;

        // Coming under fire with no idea the enemy was there.
        if (underFire > 0)
        {
            if (Time - g.LastUnderFire > 30f && !g.HasBroken)
            {
                var from = FireSource(g);
                float range = Vector2.Distance(from, g.Centroid);
                if (!g.KnewOfEnemy)
                {
                    g.Cohesion -= co.SurpriseShock * (range < co.AmbushRange ? 2f : 1f);
                    Note(g, range < co.AmbushRange ? $"{g.Name}: ambushed at {range:0} m!" : $"{g.Name}: under fire from an enemy it hadn't seen");
                }
            }
            g.LastUnderFire = Time;
        }

        // Men hit since the last look, and the sergeant going down.
        int hit = MenHit(g);
        if (hit > g.CohesionCasualties)
        {
            g.Cohesion -= (hit - g.CohesionCasualties) * co.CasualtyShock * 10f / MathF.Max(3, g.StartStrength);
            g.CohesionCasualties = hit;
        }
        if (g.LeaderLostTime > g.CohesionLeaderLost)
        {
            g.CohesionLeaderLost = g.LeaderLostTime;
            g.Cohesion -= co.LeaderLost;
        }

        UpdateFlanked(g, underFire);
        bool quiet = Time - g.LastUnderFire > co.QuietTime;
        if (!quiet) g.Cohesion -= co.PinnedDrainPerSecond * dt * pinned / active;
        if (g.Flanked) g.Cohesion -= co.FlankedDrainPerSecond * dt;
        if (g.CutOff) g.Cohesion -= co.CutOffDrainPerSecond * dt;

        if (quiet)
            g.Cohesion += (co.RecoverPerSecond + (g.Leader is { IsActive: true } ? co.RecoverWithLeaderPerSecond : 0f)) * dt;
        g.Cohesion = Math.Clamp(g.Cohesion, 0f, CohesionCeiling(g));

        g.KnewOfEnemy = g.Knowledge.Contacts.Any();

        if (g.HasBroken) { UpdateBrokenSquad(g); return; }

        // Nerve follows cohesion, with a margin on the way back up so it doesn't flicker.
        var nerve = g.Cohesion < co.BrokenBelow ? SquadNerve.Broken
            : g.Cohesion < co.WaveringBelow ? SquadNerve.Wavering
            : g.Cohesion < co.ShakenBelow ? SquadNerve.Shaken : SquadNerve.Steady;
        if (nerve < g.Nerve)
        {
            float line = g.Nerve switch { SquadNerve.Shaken => co.ShakenBelow, SquadNerve.Wavering => co.WaveringBelow, _ => co.BrokenBelow };
            if (g.Cohesion < line + co.Hysteresis) nerve = g.Nerve;
        }
        if (nerve == SquadNerve.Broken) { BreakSquad(g, WhyBroke(g)); return; }
        if (nerve != g.Nerve)
        {
            if (nerve > g.Nerve)
                Note(g, nerve == SquadNerve.Shaken ? $"{g.Name} is shaken{(g.Flanked ? " (flanked)" : "")}" : $"{g.Name} is wavering{(g.Flanked ? " (flanked)" : "")}",
                    nerve == SquadNerve.Wavering ? LogKind.Key : LogKind.Event);
            else Note(g, $"{g.Name} has steadied");
            g.Nerve = nerve;
            // Steady again after giving up an attack on its own account: the sergeant gets them going again.
            if (nerve == SquadNerve.Steady && g.GaveUpOrder is GroupOrder back && g.Order == GroupOrder.Hold)
            {
                g.Order = back;
                g.GaveUpOrder = null;
                g.AttackFailed = false;
                g.ReportedFailed = false;
                Note(g, $"{g.Name}: back on its feet, going in again", LogKind.Key);
            }
            g.NextThink = Time; // the sergeant reacts now
        }
    }

    private string WhyBroke(Group g) =>
        g.Flanked ? "flanked" : g.CutOff ? "cut off" : $"{MenHit(g)} of {g.StartStrength} men hit";

    /// <summary>Where the fire on the squad is coming from: the average of where its men were last shot at from.</summary>
    private Vector2 FireSource(Group g)
    {
        Vector2 sum = Vector2.Zero;
        int n = 0;
        foreach (var m in g.Members)
            if (m.IsActive && m.UnderFire(Time, Rules.Ai.UnderFireWindow)) { sum += m.LastShotFrom; n++; }
        return n > 0 ? sum / n : g.Centroid + g.Facing * 100f;
    }

    /// <summary>Flanked means fire from well off the squad's front. Its front is wherever the enemy first showed
    /// himself (the first fire, or the first men seen), not where the map said to expect him.</summary>
    private void UpdateFlanked(Group g, int underFire)
    {
        var co = Rules.Cohesion;
        var centre = g.Centroid;

        // Settle which way the front is: the first fire, else the nearest enemy known.
        if (g.Front == null)
        {
            if (underFire > 0) g.Front = Geo.SafeNormalize(FireSource(g) - centre, g.Facing);
            else if (NearestContactTo(g, centre) is Vector2 seen) g.Front = Geo.SafeNormalize(seen - centre, g.Facing);
            g.FrontFireAt = Time;
        }
        else if (underFire == 0 && Time - g.LastUnderFire > 30f && !g.Flanked)
        {
            g.Front = null; // long quiet: wherever he turns up next is the front
        }
        if (g.Front is not Vector2 front) { g.Flanked = false; UpdateCutOff(g, centre); return; }

        float cosFlank = MathF.Cos(co.FlankAngle * MathF.PI / 180f);
        int off = 0, ahead = 0;
        Vector2 offSum = Vector2.Zero;
        foreach (var m in g.Members)
        {
            if (!m.IsActive || !m.UnderFire(Time, Rules.Ai.UnderFireWindow)) continue;
            var dir = m.LastShotFrom - centre;
            if (dir.LengthSquared() < 1f) continue;
            if (Vector2.Dot(Vector2.Normalize(dir), front) < cosFlank) { off++; offSum += m.LastShotFrom; }
            else ahead++;
        }
        if (ahead > 0) g.FrontFireAt = Time;
        bool flanked = off >= 2 || (off >= 1 && ahead >= 1);
        Vector2 flankFrom = off > 0 ? offSum / off : default;

        if (flanked && !g.Flanked)
        {
            g.Flanked = true;
            g.FlankedFrom = flankFrom;
            // The shock is felt once; a squad already flanked a moment ago has had it.
            if (Time - g.FlankedSince > 60f)
            {
                g.Cohesion -= co.FlankedShock;
                foreach (var m in g.Members) if (m.IsActive) m.Morale -= co.FlankedMoraleShock;
                Note(g, $"{g.Name}: fire from the flank!", LogKind.Key);
            }
            g.FlankedSince = Time;
            g.NextThink = Time;
        }
        else if (flanked)
        {
            g.FlankedFrom = flankFrom;
            // Nothing from the old front for a while: the squad has turned to face the new one.
            if (Time - g.FrontFireAt > co.TurnToFaceTime) TurnToFace(g, flankFrom - centre);
        }
        else if (g.Flanked && off == 0 && Time - g.FlankedSince > Rules.Ai.UnderFireWindow)
        {
            g.Flanked = false;
        }
        UpdateCutOff(g, centre);
    }

    private void TurnToFace(Group g, Vector2 towards)
    {
        g.Front = Geo.SafeNormalize(towards, g.Facing);
        g.FrontFireAt = Time;
        g.Flanked = false;
        if (g.Order == GroupOrder.Hold) g.Facing = g.Front.Value;
        Note(g, $"{g.Name} has turned to face the fire");
    }

    private void UpdateCutOff(Group g, Vector2 centre)
    {
        var co = Rules.Cohesion;
        // Enemy known behind us, on the way out.
        bool cutOff = false;
        foreach (var c in g.Knowledge.Contacts)
        {
            var d = c.LastPosition - centre;
            float dist = d.Length();
            if (dist < 5f || dist > co.CutOffRange) continue;
            if (Vector2.Dot(d / dist, g.Rear) > 0.7f) { cutOff = true; break; }
        }
        if (cutOff && !g.CutOff) Note(g, $"{g.Name}: the enemy is behind us!", LogKind.Key);
        g.CutOff = cutOff;
    }

    /// <summary>The squad breaks as one: every man runs for the rally point.</summary>
    internal void BreakSquad(Group g, string why)
    {
        if (g.HasBroken) return;
        g.HasBroken = true;
        g.Nerve = SquadNerve.Broken;
        g.BrokeAt = Time;
        g.Cohesion = MathF.Min(g.Cohesion, Rules.Cohesion.BrokenBelow - 1f);
        g.RallyPoint = PickRallyPoint(g);
        g.RallyPointSet = Time;
        if (g.IsAttacking) { g.AttackFailed = true; g.GaveUp = true; }
        Note(g, $"{g.Name} ({g.Side}) is breaking! ({why})", LogKind.Key);
        foreach (var m in g.Members)
        {
            if (!m.IsActive) continue;
            m.Morale = MathF.Min(m.Morale, Rules.Morale.BrokenBelow - 3f);
            if (m.MoraleState != MoraleState.Broken)
            {
                m.MoraleState = MoraleState.Broken;
                m.NextThink = Time;
            }
        }
        // Panic spreads to the squads beside it.
        foreach (var o in _groups)
            if (o != g && o.Side == g.Side && o.HasCohesion && !o.HasBroken &&
                Vector2.DistanceSquared(o.Centroid, g.Centroid) < Rules.Cohesion.NeighbourRadius * Rules.Cohesion.NeighbourRadius)
                o.Cohesion -= Rules.Cohesion.NeighbourBroke;
    }

    private void UpdateBrokenSquad(Group g)
    {
        var co = Rules.Cohesion;
        int active = 0;
        foreach (var m in g.Members) if (m.IsActive) active++;
        if (active == 0) return;

        // The rally point is no good if the enemy is on it: run further, or off the field.
        if (g.RallyPoint is Vector2 rp && RallyPointUnsafe(g, rp))
        {
            g.RallyPoint = PickRallyPoint(g, from: rp);
            g.RallyPointSet = Time;
        }

        if (g.RallyPoint == null || g.Cohesion < co.RallyAt || Time - g.LastUnderFire < co.QuietTime) return;
        // Most of the men have to have got there.
        int there = 0;
        foreach (var m in g.Members)
            if (m.IsActive && Vector2.DistanceSquared(m.Position, g.RallyPoint.Value) < (co.RallyRadius + 8f) * (co.RallyRadius + 8f)) there++;
        if (there * 2 < active) return;

        g.HasBroken = false;
        g.Rallies++;
        g.Nerve = SquadNerve.Wavering;
        g.Flanked = false;
        g.Order = GroupOrder.Hold;
        g.HoldPosition = g.RallyPoint.Value;
        g.Tactic = Tactic.Defend;
        g.NextThink = Time;
        foreach (var m in g.Members)
        {
            if (!m.IsActive) continue;
            m.Morale = MathF.Max(m.Morale, Rules.Morale.RallyAbove + 2f);
            m.Suppression = 0f;
            m.MoraleState = MoraleState.Shaken;
            m.NextThink = Time;
        }
        Note(g, $"{g.Name} ({g.Side}) has rallied, {g.CountEffective} men holding where they are", LogKind.Key);
    }

    private bool RallyPointUnsafe(Group g, Vector2 rp)
    {
        float danger = Rules.Cohesion.RallyDangerDistance;
        // Men who have got there and are still being shot at: this is no place to stop.
        float r = Rules.Cohesion.RallyRadius + 4f;
        foreach (var m in g.Members)
            if (m.IsActive && m.UnderFire(Time, 3f) && Vector2.DistanceSquared(m.Position, rp) < r * r) return true;
        foreach (var c in g.Knowledge.Contacts)
            if (Vector2.DistanceSquared(c.LastPosition, rp) < danger * danger) return true;
        return false;
    }

    /// <summary>Somewhere to the rear, out of the enemy's sight if the ground allows. Null if the squad has its back
    /// to the edge of the field: then there is nowhere to go but off it.</summary>
    internal Vector2? PickRallyPoint(Group g, Vector2? from = null)
    {
        var co = Rules.Cohesion;
        var start = from ?? g.Centroid;
        var threat = NearestContactTo(g, start) ?? start - g.Rear * 150f;
        var side = Geo.Perp(g.Rear);
        Vector2? fallback = null;
        foreach (float dist in new[] { co.RallyDistance, co.RallyDistance * 1.4f, co.RallyDistance * 1.8f })
            foreach (float across in new[] { 0f, 40f, -40f })
            {
                var p = start + g.Rear * dist + side * across;
                var c = Map.Nav.NearestWalkable(Map.Clamp(p, 12f));
                if (Vector2.Distance(c, start) < co.RallyDistance * 0.5f) continue; // the edge of the field is in the way
                if (RallyPointUnsafe(g, c)) continue;
                fallback ??= c;
                if (Map.SightBlocked(threat, c)) return c;
            }
        return fallback;
    }

    /// <summary>A spot about this far to the rear to pull back to, out of the enemy's sight if there is one.</summary>
    internal Vector2 PullBackSpot(Group g, float distance)
    {
        var start = g.Centroid;
        var threat = NearestContactTo(g, start) ?? start - g.Rear * 150f;
        var side = Geo.Perp(g.Rear);
        Vector2? first = null;
        foreach (float d in new[] { distance, distance * 1.5f })
            foreach (float across in new[] { 0f, 25f, -25f })
            {
                var c = Map.Nav.NearestWalkable(Map.Clamp(start + g.Rear * d + side * across, 10f));
                first ??= c;
                if (Map.SightBlocked(threat, c)) return c;
            }
        return first ?? start;
    }

    private static Vector2? NearestContactTo(Group g, Vector2 p)
    {
        Vector2? best = null;
        float bestD = float.MaxValue;
        foreach (var c in g.Knowledge.Contacts)
        {
            float d = Vector2.DistanceSquared(c.LastPosition, p);
            if (d < bestD) { bestD = d; best = c.LastPosition; }
        }
        return best;
    }

    /// <summary>Where a broken man of this squad should run to (null = off the field). A squad that hasn't broken
    /// as a whole still gets a rally point for its stragglers, refreshed as the squad moves.</summary>
    internal Vector2? RallyPointFor(Group g)
    {
        if (!g.HasCohesion) return null;
        if (!g.HasBroken && Time - g.RallyPointSet > 45f)
        {
            g.RallyPoint = PickRallyPoint(g);
            g.RallyPointSet = Time;
        }
        return g.RallyPoint;
    }
}

using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Units;

namespace GroundWar.Sim.AI;

/// <summary>
/// The tank commander (first iteration). He drives towards his objective in bounds, stops for short halts
/// to fire the main gun, sweeps infantry with the machine gun, keeps his front armour towards anything that
/// can hurt him, and won't drive into enemy infantry without his own infantry close by.
/// </summary>
public sealed class TankBrain
{
    private readonly Battle _b;
    private readonly SquadBrain _squads;

    public TankBrain(Battle battle, SquadBrain squads) { _b = battle; _squads = squads; }

    private const float BoundLength = 40f;
    private const float OverwatchHalt = 6f;
    private const float InfantrySupportRadius = 30f;

    public void Think(Soldier t)
    {
        var g = t.Group;
        var v = t.Vehicle!;
        _squads.ShareWithNeighbours(g);

        PickGunTarget(t);
        PickMgTarget(t);

        // Goal for the order.
        Vector2 goal = g.Order switch
        {
            GroupOrder.Hold => g.HoldPosition,
            GroupOrder.Suppress => g.SupportPosition ?? g.Objective,
            _ => g.Objective,
        };
        float goalRadius = g.Order == GroupOrder.Suppress ? 8f : g.ObjectiveRadius;
        bool atGoal = Vector2.Distance(t.Position, goal) <= goalRadius;

        // Objective reached and clear: it's ours.
        if (g.Order is GroupOrder.Attack or GroupOrder.Advance && atGoal && !EnemyNear(g, g.Objective, g.ObjectiveRadius + 25f))
        {
            _b.Note(g, g.MoveOnly ? $"{g.Name}: in position" : $"{g.Name}: on the objective, holding it");
            g.TookObjective = !g.MoveOnly;
            g.HoldPosition = g.Objective;
            g.Order = GroupOrder.Hold;
        }

        float nearestInfantry = NearestEnemyInfantry(g, t.Position);
        bool support = FriendlyInfantryNear(t, InfantrySupportRadius);
        bool tooClose = nearestInfantry < v.StandOff && !support;

        if (t.Immobilised || Battle.DriveFactor(t) <= 0f)
        {
            g.Status = t.Immobilised ? "Immobilised: fighting where it stands" : "Nobody left to drive it: fighting where it stands";
            _b.MaybeAbandonStuck(t, nearestInfantry, support);
            if (!t.IsActive) return;
        }
        else if (tooClose && g.Order != GroupOrder.Hold)
        {
            if (t.IsMoving && Vector2.Distance(t.Destination ?? t.Position, t.Position) > 3f &&
                NearestEnemyInfantry(g, t.Destination ?? t.Position) < nearestInfantry)
                _b.StopMoving(t);
            g.Status = "Holding back: enemy infantry close and no infantry of our own with us";
        }
        else if (t.PlatoonGoal is { } slot)
        {
            // In a platoon: go where the platoon leader wants this tank, or halt and watch over the others.
            if (t.Overwatch)
            {
                if (t.IsMoving && _b.Time >= t.HaltUntil) _b.StopMoving(t);
                g.Status = g.PulledBack ? "Platoon pulling back" : "Platoon bounding: one half moves, the other watches over it";
            }
            else if (!t.IsMoving && _b.Time >= t.HaltUntil && Vector2.Distance(t.Position, slot) > 5f)
            {
                if (!_b.SetTankMove(t, slot)) g.Status = "Can't find a way through";
            }
        }
        else if (!atGoal && !t.IsMoving && _b.Time >= t.HaltUntil)
        {
            var dir = Geo.SafeNormalize(goal - t.Position, g.Facing);
            float dist = Vector2.Distance(goal, t.Position);
            var next = dist <= BoundLength * 1.3f ? goal : t.Position + dir * BoundLength;
            if (_b.SetTankMove(t, next))
            {
                // With the enemy about, stop after each bound to look and shoot (overwatch).
                if (g.Knowledge.Count > 0) t.HaltUntil = 0f;
                g.Status = g.Knowledge.Count > 0 ? "Advancing by bounds, halting to fire" : "Driving towards the objective";
            }
            else g.Status = "Can't find a way through";
        }
        else if (!t.IsMoving)
        {
            if (atGoal) g.Status = g.Order == GroupOrder.Suppress ? "In position, firing on the objective" : "Holding the position";
            else g.Status = "Halted to fire";
        }

        // After a bound, halt a moment if there is anyone to shoot at.
        if (!t.IsMoving && t.HaltUntil < _b.Time && g.Knowledge.Count > 0 && t.GunTarget != null)
            t.HaltUntil = _b.Time + OverwatchHalt;

        // Halted: keep the thick front armour towards the most dangerous enemy that can hurt us.
        if (!t.IsMoving && !t.Immobilised && ArmourThreat(t) is Vector2 threat)
        {
            var want = Geo.SafeNormalize(threat - t.Position, t.Facing);
            t.Facing = Battle.TurnTowards(t.Facing, want, v.TurnRate * _b.Rules.Ai.ThinkInterval);
        }

        t.Action = t.Target != null ? SoldierAction.Engage : t.IsMoving ? SoldierAction.Advance : SoldierAction.Idle;
        t.Thought = t.GunTarget != null ? $"Main gun on {t.GunTarget.Label} ({t.GunTarget.Role.ToLowerInvariant()}), {Vector2.Distance(t.Position, t.GunTarget.Position):0} m"
            : t.GunAim != null ? "Main gun on where the enemy was seen"
            : t.Target != null ? $"Machine gun on {t.Target.Label}"
            : g.Status;
    }

    /// <summary>The main gun goes for enemy tanks first, then anti-tank guns and machine guns, then bunches of men.</summary>
    private void PickGunTarget(Soldier t)
    {
        var gun = t.MainGun!;
        Soldier? best = null;
        float bestScore = 0f;
        foreach (var e in t.VisibleEnemies)
        {
            if (!e.IsActive) continue;
            float d = Vector2.Distance(t.Position, e.Position);
            if (d > gun.Range || d < 12f) continue; // too close for the big gun: the machine gun's job
            float score;
            if (e.IsTank) score = gun.CanHurtArmour ? 100f : 0f;
            else
            {
                int bunch = 0;
                foreach (var o in _b.Hash.Near(e.Side, e.Position, gun.BlastRadius)) if (o.IsActive) bunch++;
                score = bunch * (e.Weapon.CanHurtArmour ? 4f : e.Weapon.IsMachineGun ? 2f : 1f) * (e.MoraleState == MoraleState.Broken ? 0.3f : 1f);
            }
            score *= 1f + 50f / MathF.Max(d, 20f);
            if (e == t.GunTarget) score *= 1.3f;
            // The platoon spreads its fire: a man (or gun) another tank of the platoon is already shooting at is
            // worth less. An enemy tank is the exception: the more guns on it the better.
            if (!e.IsTank)
                foreach (var o in t.Group.Members)
                    if (o != t && o.IsActive && o.GunTarget == e) score *= 0.5f;
            if (score > bestScore) { bestScore = score; best = e; }
        }
        t.GunTarget = best;
        t.GunAim = null;
        if (best == null && t.Group.Order is GroupOrder.Suppress or GroupOrder.Attack)
        {
            // Nobody in sight: shell where the enemy was last seen near the objective.
            Contact? c = null;
            float bd = float.MaxValue;
            foreach (var k in t.Group.Knowledge.Contacts)
            {
                if (_b.Time - k.LastSeen > 20f) continue;
                float d = Vector2.Distance(k.LastPosition, t.Group.Objective);
                if (d < t.Group.ObjectiveRadius + 40f && d < bd) { bd = d; c = k; }
            }
            if (c != null && !_b.Map.SightBlocked(t.Position, c.LastPosition)) t.GunAim = c.LastPosition;
        }
    }

    private void PickMgTarget(Soldier t)
    {
        Soldier? best = null;
        float bd = float.MaxValue;
        foreach (var e in t.VisibleEnemies)
        {
            if (!e.IsActive || e.IsTank) continue;
            float d = Vector2.Distance(t.Position, e.Position);
            if (d > t.Weapon.Range || _b.FriendNear(t, e.Position, 4f)) continue;
            if (e.Weapon.CanHurtArmour) d *= 0.5f; // the man with the anti-tank rifle first
            if (d < bd) { bd = d; best = e; }
        }
        t.Target = best;
    }

    private Vector2? ArmourThreat(Soldier t)
    {
        Vector2? best = null;
        float bd = float.MaxValue;
        foreach (var c in t.Group.Knowledge.Contacts)
        {
            var e = _b.GetSoldier(c.EnemyId);
            if (e == null || !e.IsActive) continue;
            if (!(e.IsTank || e.Weapon.CanHurtArmour)) continue;
            float d = Vector2.Distance(c.LastPosition, t.Position);
            if (d < bd) { bd = d; best = c.LastPosition; }
        }
        return best;
    }

    private bool EnemyNear(Group g, Vector2 p, float radius)
    {
        foreach (var c in g.Knowledge.Contacts)
            if (_b.Time - c.LastSeen < 30f && Vector2.Distance(c.LastPosition, p) < radius) return true;
        return false;
    }

    private float NearestEnemyInfantry(Group g, Vector2 p)
    {
        float best = float.MaxValue;
        foreach (var c in g.Knowledge.Contacts)
        {
            var e = _b.GetSoldier(c.EnemyId);
            if (e == null || e.IsTank || !e.IsEffective) continue;
            best = MathF.Min(best, Vector2.Distance(c.LastPosition, p));
        }
        return best;
    }

    private bool FriendlyInfantryNear(Soldier t, float radius)
    {
        foreach (var f in _b.Hash.Near(t.Side, t.Position, radius))
            if (f != t && !f.IsTank && f.IsEffective) return true;
        return false;
    }

    // ---------------------------------------------------------------- the platoon (milestone 8)

    private const float BoundStep = 45f;
    private const float PlatoonSpacing = 30f;

    /// <summary>
    /// The platoon leader's half of the job, every couple of seconds: where each tank of the platoon should be.
    /// With no enemy about, the platoon drives in a wedge behind him. Once the enemy is known, it bounds: one half
    /// halts and watches (ready to fire at anything that shows itself) while the other drives forward 45 m, then
    /// they swap. When it reaches its objective it forms a line across it. Half the platoon lost and it pulls back
    /// to where it started the attack. A platoon of one tank is left to its commander (see Think).
    /// </summary>
    public void ThinkPlatoon(Group g)
    {
        var tanks = g.Members.Where(m => m.IsActive && m.IsTank).ToList();
        if (tanks.Count < 2 || g.Members.Count < 2)
        {
            foreach (var m in g.Members) { m.PlatoonGoal = null; m.Overwatch = false; }
            return;
        }
        // The platoon leader: if his tank is gone, the next tank commander takes over.
        if (g.Leader == null || !g.Leader.IsActive)
        {
            var next = tanks[0];
            if (g.Leader != null) _b.Note(g, $"{g.Name}: {next.Label} takes over the platoon");
            if (g.Leader != null) g.Leader.IsLeader = false;
            g.Leader = next;
            next.IsLeader = true;
        }
        tanks.Remove(g.Leader);
        tanks.Insert(0, g.Leader);
        var mobile = tanks.Where(m => !m.Immobilised && Battle.DriveFactor(m) > 0f).ToList();

        // Half the platoon gone or stuck while attacking: pull back to the start line and hold.
        int lost = g.Members.Count - mobile.Count;
        if (!g.PulledBack && g.Order is GroupOrder.Attack or GroupOrder.Advance && lost * 2 >= g.Members.Count && !g.MoveOnly)
        {
            g.PulledBack = true;
            _b.Note(g, $"{g.Name}: lost {lost} of {g.Members.Count} tanks, pulling back", LogKind.Key);
            g.Order = GroupOrder.Hold;
            g.HoldPosition = _b.Map.TankNav.NearestWalkable(_b.Map.Clamp(Centroid(mobile.Count > 0 ? mobile : tanks) + g.Rear * 120f, 6f), 15f);
            g.AttackFailed = true;
        }

        Vector2 goal = g.Order switch
        {
            GroupOrder.Hold => g.HoldPosition,
            GroupOrder.Suppress => g.SupportPosition ?? g.Objective,
            _ => g.Objective,
        };
        var center = Centroid(mobile.Count > 0 ? mobile : tanks);
        var dir = Geo.SafeNormalize(goal - center, g.Facing);
        if (g.Order is GroupOrder.Hold or GroupOrder.Suppress || Vector2.Distance(center, goal) < 35f) dir = Geo.SafeNormalize(g.Facing, dir);
        var perp = new Vector2(-dir.Y, dir.X);
        Vector2 Slot(int i, Vector2 at, bool wedge) =>
            _b.Map.TankNav.NearestWalkable(_b.Map.Clamp(at + perp * Offset(i, tanks.Count) - dir * (wedge && i > 0 ? 15f : 0f), 6f), 15f);

        float toGoal = Vector2.Distance(center, goal);
        bool enemyKnown = g.Knowledge.Contacts.Any(k => _b.Time - k.LastSeen < 60f);
        bool arrived = toGoal < 25f || g.Order is GroupOrder.Hold or GroupOrder.Suppress && toGoal < 40f;

        if (arrived || g.Order == GroupOrder.Hold && toGoal < 60f)
        {
            // There: a line across the objective (or the position), everyone free to fight.
            for (int i = 0; i < tanks.Count; i++) { tanks[i].PlatoonGoal = Slot(i, goal, wedge: false); tanks[i].Overwatch = false; }
            return;
        }
        if (!enemyKnown || g.Order == GroupOrder.Hold)
        {
            // Travelling: a wedge behind the leader, heading for the goal.
            var guide = center + dir * MathF.Min(60f, toGoal);
            for (int i = 0; i < tanks.Count; i++) { tanks[i].PlatoonGoal = Slot(i, guide, wedge: true); tanks[i].Overwatch = false; }
            return;
        }
        // Bounding overwatch: half the platoon moves (the leader's half first), the other half watches over it.
        bool boundDone = true;
        for (int i = 0; i < tanks.Count; i++)
        {
            var m = tanks[i];
            if (i % 2 != g.BoundingSection || !mobile.Contains(m)) continue;
            if (m.IsMoving || m.PlatoonGoal is { } pg && Vector2.Distance(m.Position, pg) > 6f) boundDone = false;
        }
        if (boundDone || _b.Time - g.BoundStarted > 40f)
        {
            g.BoundingSection = 1 - g.BoundingSection;
            g.BoundStarted = _b.Time;
            var guide = center + dir * MathF.Min(BoundStep, toGoal);
            for (int i = 0; i < tanks.Count; i++)
                if (i % 2 == g.BoundingSection) tanks[i].PlatoonGoal = Slot(i, guide, wedge: false);
        }
        for (int i = 0; i < tanks.Count; i++)
        {
            tanks[i].Overwatch = i % 2 != g.BoundingSection && tanks.Count(m => mobile.Contains(m)) > 1;
            tanks[i].PlatoonGoal ??= tanks[i].Position;
        }
    }

    /// <summary>Sideways place in the line: the leader in the middle, the others alternately to his left and right.</summary>
    private static float Offset(int i, int n) => (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * PlatoonSpacing;

    private static Vector2 Centroid(List<Soldier> tanks)
    {
        Vector2 sum = Vector2.Zero;
        foreach (var t in tanks) sum += t.Position;
        return tanks.Count > 0 ? sum / tanks.Count : Vector2.Zero;
    }
}

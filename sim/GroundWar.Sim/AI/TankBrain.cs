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
            _b.Note(g, $"{g.Name}: on the objective, holding it");
            g.TookObjective = true;
            g.HoldPosition = g.Objective;
            g.Order = GroupOrder.Hold;
        }

        float nearestInfantry = NearestEnemyInfantry(g, t.Position);
        bool support = FriendlyInfantryNear(t, InfantrySupportRadius);
        bool tooClose = nearestInfantry < v.StandOff && !support;

        if (t.Immobilised)
        {
            g.Status = "Immobilised: fighting where it stands";
        }
        else if (tooClose && g.Order != GroupOrder.Hold)
        {
            if (t.IsMoving && Vector2.Distance(t.Destination ?? t.Position, t.Position) > 3f &&
                NearestEnemyInfantry(g, t.Destination ?? t.Position) < nearestInfantry)
                _b.StopMoving(t);
            g.Status = "Holding back: enemy infantry close and no infantry of our own with us";
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
}

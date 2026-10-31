using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

public sealed partial class Battle
{
    private void UpdateWeapon(Soldier s)
    {
        var w = s.Weapon;
        if (s.ReloadTimer > 0f)
        {
            s.ReloadTimer -= Dt;
            if (s.ReloadTimer <= 0f) s.Magazine = w.MagazineSize;
            return;
        }
        s.FireTimer -= Dt;

        if (s.BurstRemaining > 0)
        {
            s.BurstTimer -= Dt;
            if (s.BurstTimer > 0f) return;
            if (s.Target != null && s.Target.IsActive && !s.IsMoving && s.Suppression < Rules.Morale.SuppressionMax)
            {
                FireRound(s, s.Target);
                s.BurstRemaining--;
                s.BurstTimer = w.BurstInterval;
            }
            else s.BurstRemaining = 0;
            return;
        }

        if (s.Action != SoldierAction.Engage || s.IsMoving) return;
        var t = s.Target;
        if (t == null || !t.IsActive || !s.VisibleEnemies.Contains(t)) { s.Target = null; return; }
        if (s.Suppression >= Rules.Morale.SuppressionNoFire) return;

        if (s.Magazine <= 0)
        {
            if (s.SpareMagazines > 0)
            {
                s.SpareMagazines--;
                s.ReloadTimer = w.ReloadTime;
            }
            return;
        }
        if (s.FireTimer > 0f) return;

        // Shaken and suppressed men shoot more slowly as well as worse.
        float slow = 1f + s.Suppression / 100f;
        s.FireTimer = (w.FireInterval + w.FireIntervalJitter * Rng.Range(-1f, 1f)) * slow;
        s.Facing = Geo.SafeNormalize(t.Position - s.Position, s.Facing);
        FireRound(s, t);
        s.BurstRemaining = w.BurstSize - 1;
        s.BurstTimer = w.BurstInterval;
    }

    /// <summary>Chance that one round from shooter hits target right now.</summary>
    public float HitChance(Soldier shooter, Soldier target)
    {
        var cr = Rules.Combat;
        float d = Vector2.Distance(shooter.Position, target.Position);
        float prot = Map.ProtectionAt(target.Position, shooter.Position);
        if (prot >= 1f) return 0f;
        float p = shooter.Weapon.HitChanceAt(d) * shooter.Skill;
        p *= target.Stance switch
        {
            Stance.Prone => cr.ProneHitFactor,
            Stance.Crouched => cr.CrouchedHitFactor,
            _ => cr.StandingHitFactor,
        };
        if (target.IsMoving) p *= cr.MovingTargetFactor;
        if (shooter.MoraleState != MoraleState.Steady) p *= cr.ShakenAccuracy;
        p *= 1f - cr.SuppressionAccuracyLoss * Math.Clamp(shooter.Suppression / 100f, 0f, 1f);
        p *= 1f - prot;
        return Math.Clamp(p, 0f, 0.95f);
    }

    private void FireRound(Soldier s, Soldier t)
    {
        if (s.Magazine <= 0) { s.BurstRemaining = 0; return; }
        var w = s.Weapon;
        s.Magazine--;
        s.ShotsFired++;
        s.LastFiredTime = Time;

        bool hit = Rng.Chance(HitChance(s, t));
        Vector2 impact;
        if (hit)
        {
            impact = t.Position;
        }
        else
        {
            Vector2 dir = Geo.SafeNormalize(t.Position - s.Position, Vector2.UnitX);
            impact = t.Position + Geo.Perp(dir) * (Rng.Gaussian() * w.Spread) + dir * (Rng.Gaussian() * w.Spread * 1.5f);
        }
        _shots.Add(new ShotEvent(s.Id, s.Position, impact, hit, w.IsMachineGun));

        // Near misses frighten everyone close to where the bullet went.
        float radius = w.SuppressionRadius;
        foreach (var u in _soldiers)
        {
            if (u.Side == s.Side || !u.IsActive || (hit && u == t)) continue;
            float falloff;
            if (u == t) falloff = 1f;
            else
            {
                float dd = Vector2.Distance(u.Position, impact);
                if (dd >= radius) continue;
                falloff = 1f - dd / radius;
            }
            float shield = 1f - Rules.Combat.CoverSuppressionShield * Map.ProtectionAt(u.Position, s.Position);
            AddSuppression(u, w.Suppression * falloff * shield, s);
        }

        // A muzzle flash can give the shooter away.
        if (Rng.Chance(Rules.Perception.MuzzleFlashRevealChance))
        {
            var guess = s.Position + new Vector2(Rng.Gaussian(), Rng.Gaussian()) * 3f;
            _knowledge[(int)t.Side].Report(s, guess, Time, 0.5f);
        }

        if (hit) ApplyHit(t, s);
    }

    private void AddSuppression(Soldier u, float amount, Soldier shooter)
    {
        if (amount <= 0f) return;
        var mr = Rules.Morale;
        u.Suppression = MathF.Min(mr.SuppressionMax, u.Suppression + amount);
        u.LastSuppressedTime = Time;
        u.LastShotAtTime = Time;
        u.LastShotFrom = shooter.Position;
        u.Morale -= amount * mr.NearMissMoralePerSuppression;
    }

    private void ApplyHit(Soldier t, Soldier shooter)
    {
        var mr = Rules.Morale;
        bool killed = Rng.Chance(Rules.Soldier.KillChanceOnHit);
        t.Status = killed ? SoldierStatus.Dead : SoldierStatus.Wounded;
        t.Stance = Stance.Prone;
        t.Path = null;
        t.MoveMode = MoveMode.None;
        t.Action = SoldierAction.Idle;
        t.Thought = killed ? "Killed" : "Wounded";
        t.BurstRemaining = 0;
        ReleaseCover(t);
        shooter.Hits++;
        _casualties.Add(new CasualtyEvent(t.Id, shooter.Id, killed));
        Note(t, $"{(killed ? "killed" : "wounded")} by {shooter.Label} at {Vector2.Distance(t.Position, shooter.Position):0} m");
        _knowledge[(int)shooter.Side].Forget(t.Id);

        foreach (var f in _soldiers)
        {
            if (!f.IsActive) continue;
            float d = Vector2.Distance(f.Position, t.Position);
            if (f.Side == t.Side)
            {
                if (d < mr.FriendRadius)
                    f.Morale -= (killed ? mr.FriendKilled : mr.FriendWounded) * (1f - 0.5f * d / mr.FriendRadius);
                if (t.IsLeader && f.Group == t.Group) f.Morale -= mr.LeaderKilled;
            }
            else if (f.Group == shooter.Group && d < 150f)
            {
                f.Morale = MathF.Min(f.StartMorale, f.Morale + mr.EnemyDownSeen);
            }
        }
    }

    internal void ReleaseCover(Soldier s)
    {
        if (s.Cover != null && s.Cover.ClaimedBy == s.Id) s.Cover.ClaimedBy = -1;
        s.Cover = null;
    }
}

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
            bool targetOk = s.Target != null ? s.Target.IsActive : s.AreaTarget != null;
            if (targetOk && !s.IsMoving && s.Suppression < Rules.Morale.SuppressionMax)
            {
                FireRound(s, s.Target, s.Target?.Position ?? s.AreaTarget!.Value);
                s.BurstRemaining--;
                s.BurstTimer = w.BurstInterval;
            }
            else s.BurstRemaining = 0;
            return;
        }

        if (s.Action != SoldierAction.Engage || s.IsMoving) return;
        var t = s.Target;
        Vector2 aim;
        if (t != null)
        {
            if (!t.IsActive || !s.VisibleEnemies.Contains(t)) { s.Target = null; return; }
            aim = t.Position;
        }
        else if (s.AreaTarget != null) aim = s.AreaTarget.Value;
        else return;
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

        // Frightened men shoot more slowly as well as worse.
        float slow = 1f + s.Suppression / 100f;
        s.FireTimer = (w.FireInterval + w.FireIntervalJitter * Rng.Range(-1f, 1f)) * slow;
        s.Facing = Geo.SafeNormalize(aim - s.Position, s.Facing);
        FireRound(s, t, aim);
        s.BurstRemaining = w.BurstSize - 1;
        s.BurstTimer = w.BurstInterval;
    }

    /// <summary>True if a friendly soldier is near a spot, so firing there would risk hitting him.</summary>
    internal bool FriendNear(Soldier shooter, Vector2 point, float radius)
    {
        foreach (var f in Hash.Near(shooter.Side, point, radius))
            if (f != shooter && Vector2.DistanceSquared(f.Position, shooter.Position) > 25f) return true;
        return false;
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

    /// <summary>One round. Aimed at a man (t) or, for area fire, at a spot where the enemy was seen.</summary>
    private void FireRound(Soldier s, Soldier? t, Vector2 aim)
    {
        if (s.Magazine <= 0) { s.BurstRemaining = 0; return; }
        var w = s.Weapon;
        s.Magazine--;
        s.ShotsFired++;
        s.LastFiredTime = Time;
        var enemySide = Enemy(s.Side);

        // Area fire can still hit someone who happens to be there.
        Soldier? victim = t;
        float p;
        if (t != null) p = HitChance(s, t);
        else
        {
            p = 0f;
            foreach (var e in Hash.Near(enemySide, aim, 3f))
            {
                float d = Vector2.Distance(e.Position, aim);
                float pe = HitChance(s, e) * Rules.Squad.AreaFireAccuracy * (1f - d / 3f);
                if (pe > p) { p = pe; victim = e; }
            }
        }

        bool hit = victim != null && Rng.Chance(p);
        Vector2 impact;
        if (hit) impact = victim!.Position;
        else
        {
            Vector2 dir = Geo.SafeNormalize(aim - s.Position, Vector2.UnitX);
            float spread = w.Spread * (t == null ? 1.5f : 1f);
            impact = aim + Geo.Perp(dir) * (Rng.Gaussian() * spread) + dir * (Rng.Gaussian() * spread * 1.5f);
        }
        _shots.Add(new ShotEvent(s.Id, s.Position, impact, hit, w.IsMachineGun));

        // Near misses frighten everyone close to where the bullet went.
        float radius = w.SuppressionRadius;
        Soldier? frightened = null;
        if (t != null && !hit) { SuppressOne(t, 1f, s); frightened = t; }
        foreach (var u in Hash.Near(enemySide, impact, radius))
        {
            if (u == t || (hit && u == victim)) continue;
            float dd = Vector2.Distance(u.Position, impact);
            if (dd >= radius) continue;
            SuppressOne(u, 1f - dd / radius, s);
            frightened ??= u;
        }

        // A muzzle flash can give the shooter away to the men being shot at.
        var reveal = frightened ?? victim;
        if (reveal != null && Rng.Chance(Rules.Perception.MuzzleFlashRevealChance))
        {
            var guess = s.Position + new Vector2(Rng.Gaussian(), Rng.Gaussian()) * 3f;
            Report(reveal.Group, s, guess, 0.5f);
        }

        if (hit) ApplyHit(victim!, s);
    }

    private void SuppressOne(Soldier u, float falloff, Soldier shooter)
    {
        float shield = 1f - Rules.Combat.CoverSuppressionShield * Map.ProtectionAt(u.Position, shooter.Position);
        AddSuppression(u, shooter.Weapon.Suppression * falloff * shield, shooter);
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
        Note(t, $"{(killed ? "killed" : "wounded")} by {shooter.Label} at {Vector2.Distance(t.Position, shooter.Position):0} m ({t.Action}, {(t.IsMoving ? "moving" : t.Stance.ToString().ToLowerInvariant())}, cover {Map.ProtectionAt(t.Position, shooter.Position) * 100:0}%)", LogKind.Casualty);
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
        _knowledge[(int)shooter.Side].Forget(t.Id);
        foreach (var g in _groups) if (g.Side == shooter.Side) g.Knowledge.Forget(t.Id);

        foreach (var f in Hash.Near(t.Side, t.Position, mr.FriendRadius))
        {
            if (f == t || !f.IsActive) continue;
            float d = Vector2.Distance(f.Position, t.Position);
            f.Morale -= (killed ? mr.FriendKilled : mr.FriendWounded) * (1f - 0.5f * d / mr.FriendRadius);
        }
        if (t.IsLeader)
            foreach (var f in t.Group.Members)
                if (f.IsActive && f != t) f.Morale -= mr.LeaderKilled;
        foreach (var f in shooter.Group.Members)
            if (f.IsActive && Vector2.DistanceSquared(f.Position, t.Position) < 150f * 150f)
                f.Morale = MathF.Min(f.StartMorale, f.Morale + mr.EnemyDownSeen);
    }

    internal void ReleaseCover(Soldier s)
    {
        if (s.Cover != null && s.Cover.ClaimedBy == s.Id) s.Cover.ClaimedBy = -1;
        s.Cover = null;
    }
}

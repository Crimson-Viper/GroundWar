using System.Numerics;
using GroundWar.Sim.Core;
using GroundWar.Sim.Data;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>Tanks: spawning, driving, the main gun, and what happens when one is hit.</summary>
public sealed partial class Battle
{
    private void AddVehicle(Group group, VehicleDef v, Vector2 center, Vector2 size)
    {
        if (!Data.Weapons.TryGetValue(v.MachineGun, out var mg))
            throw new InvalidDataException($"Vehicle '{v.Id}' uses unknown weapon '{v.MachineGun}'");
        if (!Data.Weapons.TryGetValue(v.MainGun, out var gun))
            throw new InvalidDataException($"Vehicle '{v.Id}' uses unknown weapon '{v.MainGun}'");
        Vector2 pos = center + new Vector2(Rng.Range(-0.5f, 0.5f) * size.X, Rng.Range(-0.5f, 0.5f) * size.Y);
        pos = Map.TankNav.NearestWalkable(Map.Clamp(pos, 4f), 15f);
        var s = new Soldier
        {
            Id = _soldiers.Count,
            Side = group.Side,
            Group = group,
            Weapon = mg,
            MainGun = gun,
            Vehicle = v,
            Skill = Rng.Range(Rules.Soldier.SkillMin, Rules.Soldier.SkillMax),
            StartMorale = 100f,
            Morale = 100f,
            Position = pos,
            Facing = group.Facing,
            TurretFacing = group.Facing,
            Magazine = mg.MagazineSize,
            SpareMagazines = mg.Magazines,
            MainGunAmmo = gun.Magazines,
            NextThink = Rng.Range(0f, Rules.Ai.ThinkInterval),
            NextPerceive = Rng.Range(0f, Rules.Perception.Interval),
            FireTimer = Rng.Range(0f, mg.FireInterval),
            MainGunTimer = Rng.Range(1f, gun.FireInterval),
        };
        _soldiers.Add(s);
        group.Members.Add(s);
    }

    /// <summary>Send a tank along a path it fits through.</summary>
    internal bool SetTankMove(Soldier t, Vector2 destination)
    {
        if (t.Immobilised) return false;
        var path = Map.TankNav.FindPath(t.Position, Map.TankNav.NearestWalkable(destination, 15f));
        if (path == null || path.Count == 0) return false;
        t.Path = path;
        t.PathIndex = 0;
        t.MoveMode = MoveMode.Walk;
        t.Destination = path[^1];
        return true;
    }

    /// <summary>A tank turns its hull towards the next waypoint and only drives once it is nearly lined up.</summary>
    private void MoveTank(Soldier t)
    {
        var v = t.Vehicle!;
        if (t.Path == null || t.MoveMode == MoveMode.None) return;
        if (t.Immobilised || Time < t.HaltUntil) return;
        var target = t.Path[t.PathIndex];
        var delta = target - t.Position;
        float dist = delta.Length();
        if (dist < 0.5f)
        {
            t.PathIndex++;
            if (t.PathIndex >= t.Path.Count) StopMoving(t);
            return;
        }
        var want = delta / dist;
        t.Facing = TurnTowards(t.Facing, want, v.TurnRate * Dt);
        float align = Vector2.Dot(t.Facing, want);
        if (align < 0.85f) return; // turning on the spot
        float speed = v.Speed * (Map.InCrater(t.Position) ? 0.6f : 1f);
        float step = MathF.Min(speed * Dt, dist);
        t.Position += t.Facing * step;
    }

    internal static Vector2 TurnTowards(Vector2 from, Vector2 to, float maxDegrees)
    {
        float a = MathF.Atan2(from.Y, from.X), b = MathF.Atan2(to.Y, to.X);
        float diff = b - a;
        while (diff > MathF.PI) diff -= 2 * MathF.PI;
        while (diff < -MathF.PI) diff += 2 * MathF.PI;
        float max = maxDegrees * MathF.PI / 180f;
        diff = Math.Clamp(diff, -max, max);
        return new Vector2(MathF.Cos(a + diff), MathF.Sin(a + diff));
    }

    /// <summary>Turn the turret onto the gun's target and fire when it is lined up and loaded.
    /// The tank stops for a few seconds to fire: shooting on the move would miss.</summary>
    private void UpdateMainGun(Soldier t)
    {
        var gun = t.MainGun!;
        t.MainGunTimer -= Dt;
        Vector2? aim = null;
        if (t.GunTarget != null)
        {
            if (!t.GunTarget.IsActive || !t.VisibleEnemies.Contains(t.GunTarget)) t.GunTarget = null;
            else aim = t.GunTarget.Position;
        }
        aim ??= t.GunAim;
        if (aim == null) { t.TurretFacing = TurnTowards(t.TurretFacing, t.Facing, t.Vehicle!.TurretTurnRate * Dt); return; }
        var dir = Geo.SafeNormalize(aim.Value - t.Position, t.TurretFacing);
        t.TurretFacing = TurnTowards(t.TurretFacing, dir, t.Vehicle!.TurretTurnRate * Dt);
        if (Vector2.Dot(t.TurretFacing, dir) < 0.995f || t.MainGunTimer > 0f || t.MainGunAmmo <= 0) return;
        if (Vector2.Distance(t.Position, aim.Value) > gun.Range) return;
        if (t.GunTarget == null && FriendNear(t, aim.Value, gun.BlastRadius + 4f)) return;
        if (t.IsMoving && !t.Immobilised && Time >= t.HaltUntil)
        {
            // Short halt: stop, then fire next time round.
            t.HaltUntil = Time + 4f;
            if (t.MainGunTimer < 1f) t.MainGunTimer = 1f;
            return;
        }
        FireShell(t, t.GunTarget, aim.Value);
        t.MainGunAmmo--;
        t.MainGunTimer = gun.FireInterval + gun.FireIntervalJitter * Rng.Range(-1f, 1f);
        t.HaltUntil = MathF.Max(t.HaltUntil, Time + 1.5f);
    }

    /// <summary>A high-explosive shell. It lands on the aim point or somewhere around it, then blasts
    /// everyone close by (cover between the burst and a man protects him). A direct hit on a tank tests its armour.</summary>
    private void FireShell(Soldier t, Soldier? target, Vector2 aim)
    {
        var gun = t.MainGun!;
        t.ShotsFired++;
        t.LastFiredTime = Time;
        float d = Vector2.Distance(t.Position, aim);
        float p = gun.HitChanceAt(d) * t.Skill;
        if (target?.IsTank == true) p *= target.Vehicle!.SizeHitMultiplier * 0.5f;
        bool onTarget = Rng.Chance(MathF.Min(p, 0.95f));
        var dir = Geo.SafeNormalize(aim - t.Position, Vector2.UnitX);
        // Even a good shot lands a little short or to one side of a man (a wall in front of him catches it).
        var impact = target?.IsTank == true ? aim : aim - dir * Rng.Range(0.5f, 3f) + Geo.Perp(dir) * (Rng.Gaussian() * 1.2f);
        if (!onTarget)
        {
            float spread = gun.Spread * MathF.Max(0.5f, d / 100f);
            impact = aim + Geo.Perp(dir) * (Rng.Gaussian() * spread * 0.6f) + dir * (Rng.Gaussian() * spread);
        }
        impact = Map.Clamp(impact, 0.5f);
        bool directTankHit = onTarget && target?.IsTank == true;
        _shots.Add(new ShotEvent(t.Id, t.Position, impact, directTankHit, false, true));
        if (FirstShotTime < 0f) FirstShot(t);

        if (directTankHit) ArmourHit(target!, t, gun);
        Blast(t, impact, gun);

        // The flash and bang give the tank away to everyone near the burst.
        foreach (var u in Hash.Near(Enemy(t.Side), impact, gun.SuppressionRadius))
        {
            Report(u.Group, t, t.Position, 1f);
            break;
        }
    }

    private void Blast(Soldier shooter, Vector2 at, WeaponDef gun)
    {
        CutLinesNear(at);
        var mr = Rules.Morale;
        float r = gun.BlastRadius;
        // Friends too: a shell doesn't care whose men are near it.
        foreach (var side in new[] { Side.Blue, Side.Red })
        {
            _blastScratch.Clear();
            _blastScratch.AddRange(Hash.Near(side, at, gun.SuppressionRadius));
            foreach (var u in _blastScratch)
            {
                if (!u.IsActive || u.IsTank) continue;
                float dist = Vector2.Distance(u.Position, at);
                if (dist >= gun.SuppressionRadius) continue;
                float prot = Map.ProtectionAt(u.Position, at);
                if (Map.InCrater(u.Position) && Map.InCrater(at) && Vector2.Distance(u.Position, at) < 3f) prot = 0f;
                if (dist < r)
                {
                    float k = 1f - dist / r;
                    // Most of a burst's fragments fly up and out: lying flat or behind a wall saves most men.
                    float pHit = 0.6f * k * k * (u.Stance == Stance.Prone ? 0.5f : 1f) * (1f - prot);
                    if (Rng.Chance(pHit)) { ApplyHit(u, shooter); continue; }
                }
                float fear = gun.Suppression * (1f - dist / gun.SuppressionRadius) * (1f - Rules.Combat.CoverSuppressionShield * prot);
                if (u.Side != shooter.Side) AddSuppression(u, fear, shooter);
                else { u.Suppression = MathF.Min(mr.SuppressionMax, u.Suppression + fear); u.LastSuppressedTime = Time; }
            }
        }
    }
    private readonly List<Soldier> _blastScratch = new();

    /// <summary>Which armour a shot from <paramref name="from"/> meets: front, side or rear.</summary>
    internal static float ArmourFacing(Soldier tank, Vector2 from)
    {
        var v = tank.Vehicle!;
        var toShooter = Geo.SafeNormalize(from - tank.Position, tank.Facing);
        float c = Vector2.Dot(toShooter, tank.Facing);
        return c > 0.5f ? v.ArmourFront : c < -0.5f ? v.ArmourRear : v.ArmourSide;
    }

    /// <summary>A round that can hurt armour has hit a tank. Through the armour: knocked out, tracks broken,
    /// or the crew shaken. Bounced off: maybe a broken track.</summary>
    private void ArmourHit(Soldier tank, Soldier shooter, WeaponDef weapon)
    {
        var v = tank.Vehicle!;
        float armour = ArmourFacing(tank, shooter.Position);
        string side = armour == v.ArmourFront ? "front" : armour == v.ArmourRear ? "rear" : "side";
        shooter.Hits++;
        if (weapon.PenetrationAt(Vector2.Distance(tank.Position, shooter.Position)) > armour)
        {
            float roll = Rng.NextFloat();
            if (roll < v.KnockOutChance)
            {
                Note(tank, $"knocked out by {shooter.Label} at {Vector2.Distance(tank.Position, shooter.Position):0} m (hit in the {side})", LogKind.Casualty);
                tank.Status = SoldierStatus.Dead;
                tank.Thought = "Knocked out";
                StopMoving(tank);
                _casualties.Add(new CasualtyEvent(tank.Id, shooter.Id, true));
                _knowledge[(int)shooter.Side].Forget(tank.Id);
                foreach (var g in _groups) if (g.Side == shooter.Side) g.Knowledge.Forget(tank.Id);
                // Seeing your tank brew up is a blow; seeing theirs is a lift.
                foreach (var f in Hash.Near(tank.Side, tank.Position, 60f)) if (f.IsActive) f.Morale -= Rules.Morale.FriendKilled;
                foreach (var f in Hash.Near(shooter.Side, tank.Position, 150f)) if (f.IsActive) f.Morale = MathF.Min(f.StartMorale, f.Morale + 10f);
            }
            else if (roll < v.KnockOutChance + v.ImmobiliseChance && !tank.Immobilised)
            {
                Note(tank, $"immobilised by {shooter.Label} (hit in the {side})", LogKind.Casualty);
                tank.Immobilised = true;
                StopMoving(tank);
            }
            else
            {
                Note(tank, $"hit by {shooter.Label}: crew wounded, gun slowed (hit in the {side})", LogKind.Casualty);
                tank.Skill *= 0.8f;
                tank.MainGunTimer += 6f;
            }
        }
        else if (Rng.Chance(weapon.TrackBreakChance > 0f ? weapon.TrackBreakChance : v.TrackHitChance) && !tank.Immobilised)
        {
            Note(tank, $"track broken by {shooter.Label}", LogKind.Casualty);
            tank.Immobilised = true;
            StopMoving(tank);
        }
    }
}

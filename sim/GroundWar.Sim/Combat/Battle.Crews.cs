using System.Numerics;
using GroundWar.Sim.Data;
using GroundWar.Sim.Units;

namespace GroundWar.Sim;

/// <summary>
/// Tank crews (milestone 8). Each tank has five men inside: commander, gunner, loader, driver and bow gunner.
/// They are real soldiers who sit in the tank ("mounted") and take no part until they get out. A hit that goes
/// through the armour can kill or wound some of them, and the tank fights worse for each man lost: the loader
/// moves up to the gun, the bow gunner takes over driving, a tank without its commander is buttoned up and half
/// blind. When the tank is knocked out, or the crew decide to abandon it, the men still able bail out and make
/// for the rear on foot. See docs/systems/tanks.md.
/// </summary>
public sealed partial class Battle
{
    private static readonly CrewRole[] CrewOrder = { CrewRole.Commander, CrewRole.Gunner, CrewRole.Loader, CrewRole.Driver, CrewRole.BowGunner };
    private readonly Dictionary<Group, Group> _crewGroups = new();

    /// <summary>Put a crew in a tank: men of the platoon's crew group, mounted.</summary>
    private void AddCrew(Soldier tank, VehicleDef v)
    {
        if (v.Crew <= 0) return;
        var platoon = tank.Group;
        if (!_crewGroups.TryGetValue(platoon, out var crews))
        {
            crews = new Group(platoon.Side)
            {
                Id = _groups.Count, Name = $"{platoon.Name} crews", Order = GroupOrder.Hold, Objective = platoon.Objective,
                ObjectiveRadius = 15f, Facing = platoon.Facing, Rear = platoon.Rear, IsCrew = true,
            };
            crews.OriginalOrder = GroupOrder.Hold;
            crews.HoldPosition = tank.Position;
            crews.NextThink = float.MaxValue; // nothing to think about until somebody bails out
            _groups.Add(crews);
            _crewGroups[platoon] = crews;
        }
        var rifle = Data.Weapons["rifle"];
        for (int i = 0; i < Math.Min(v.Crew, CrewOrder.Length); i++)
        {
            var sr = Rules.Soldier;
            float morale = Rng.Range(sr.MoraleMin, sr.MoraleMax);
            var m = new Soldier
            {
                Id = _soldiers.Count, Side = tank.Side, Group = crews, Weapon = rifle, Skill = Rng.Range(sr.SkillMin, sr.SkillMax),
                StartMorale = morale, Morale = morale, Position = tank.Position, Facing = tank.Facing, Status = SoldierStatus.Mounted,
                Magazine = rifle.MagazineSize, SpareMagazines = 1, CrewRole = CrewOrder[i], CrewOf = tank,
                NextThink = float.MaxValue, NextPerceive = float.MaxValue,
            };
            _soldiers.Add(m);
            crews.Members.Add(m);
            tank.Crew.Add(m);
        }
        crews.StartStrength = crews.Members.Count;
    }

    /// <summary>Is this crewman still at his post (in the tank and unhurt)? A tank with no crew modelled always is.</summary>
    internal static bool CrewFit(Soldier tank, CrewRole role)
    {
        if (tank.Crew.Count == 0) return true;
        foreach (var m in tank.Crew) if (m.CrewRole == role) return m.Status == SoldierStatus.Mounted;
        return false;
    }

    internal static int CrewLost(Soldier tank) => tank.Crew.Count(m => m.Status != SoldierStatus.Mounted);

    /// <summary>Who is working the main gun, and how well: (aim, loading time). The gunner and loader; else the
    /// loader moves to the gun and loads it himself; else the commander does both. Nobody: the gun is silent.</summary>
    internal static (float Aim, float Load) GunCrew(Soldier tank)
    {
        bool gunner = CrewFit(tank, CrewRole.Gunner), loader = CrewFit(tank, CrewRole.Loader);
        if (gunner && loader) return (1f, 1f);
        if (gunner) return (1f, 1.8f);
        if (loader) return (0.75f, 1.6f);
        return CrewFit(tank, CrewRole.Commander) ? (0.6f, 2.2f) : (0f, 99f);
    }

    /// <summary>How fast it can be driven: the driver, or the bow gunner in his seat (slower), or not at all.</summary>
    internal static float DriveFactor(Soldier tank) =>
        CrewFit(tank, CrewRole.Driver) ? 1f : CrewFit(tank, CrewRole.BowGunner) ? 0.7f : 0f;

    /// <summary>Without its commander's head out of the hatch, a tank sees half as much.</summary>
    internal static float LookoutFactor(Soldier tank) => CrewFit(tank, CrewRole.Commander) ? 1f : 0.5f;

    /// <summary>A hit has gone through the armour: one or two of the men inside are hit.</summary>
    internal void HitCrew(Soldier tank, Soldier shooter, int count)
    {
        var inside = tank.Crew.Where(m => m.Status == SoldierStatus.Mounted).ToList();
        for (int i = 0; i < count && inside.Count > 0; i++)
        {
            var m = inside[Rng.NextInt(inside.Count)];
            inside.Remove(m);
            bool killed = Rng.Chance(0.5f);
            m.Status = killed ? SoldierStatus.Dead : SoldierStatus.Wounded;
            m.Thought = killed ? "Killed in his tank" : "Wounded in his tank";
            _casualties.Add(new CasualtyEvent(m.Id, shooter.Id, killed));
            Note(tank, $"{m.Role.ToLowerInvariant()} {(killed ? "killed" : "wounded")}", LogKind.Casualty);
        }
        if (tank.Status != SoldierStatus.Active) return;
        // Too few left to fight the tank (or nobody to command or aim it): they get out.
        if (CrewLost(tank) >= 3 || !CrewFit(tank, CrewRole.Commander) && !CrewFit(tank, CrewRole.Gunner))
            Abandon(tank, "too few of the crew left to fight it");
    }

    /// <summary>The crew abandon a tank that can still be fought (too many hurt, or stuck with enemy infantry
    /// closing in): it counts as lost, and they get out.</summary>
    internal void Abandon(Soldier tank, string why)
    {
        if (tank.Status != SoldierStatus.Active) return;
        Note(tank, $"abandoned by its crew: {why}", LogKind.Casualty);
        tank.Status = SoldierStatus.Dead;
        tank.Thought = "Abandoned";
        StopMoving(tank);
        _knowledge[(int)Enemy(tank.Side)].Forget(tank.Id);
        foreach (var g in _groups) if (g.Side != tank.Side) g.Knowledge.Forget(tank.Id);
        BailOut(tank, brewedUp: false);
    }

    /// <summary>The men still able get out. In a tank that has brewed up (caught fire), each man inside has an
    /// even chance of not making it.</summary>
    private void BailOut(Soldier tank, bool brewedUp)
    {
        if (tank.BailedOut || tank.Crew.Count == 0) return;
        tank.BailedOut = true;
        int out_ = 0;
        var crews = tank.Crew[0].Group;
        foreach (var m in tank.Crew)
        {
            if (m.Status != SoldierStatus.Mounted) continue;
            if (brewedUp && Rng.Chance(0.5f))
            {
                m.Status = SoldierStatus.Dead;
                m.Thought = "Killed when his tank brewed up";
                _casualties.Add(new CasualtyEvent(m.Id, m.Id, true));
                continue;
            }
            out_++;
            m.GotOut = true;
            m.Status = SoldierStatus.Active;
            m.Position = Map.Nav.NearestWalkable(Map.Clamp(tank.Position + new Vector2(Rng.Range(-3f, 3f), Rng.Range(-3f, 3f)), 2f));
            m.Morale = MathF.Min(m.Morale, Rules.Morale.ShakenBelow);
            m.Suppression = MathF.Max(m.Suppression, Rules.Morale.SuppressionPinned);
            m.LastSuppressedTime = Time;
            m.Task = TaskMode.Hold;
            m.TaskPosition = m.Position;
            m.NextThink = Time + Rng.Range(0.5f, 1.5f);
            m.NextPerceive = Time + Rng.Range(0f, Rules.Perception.Interval);
        }
        Note(tank, out_ > 0 ? $"crew bails out ({out_} of {tank.Crew.Count} get out)" : "nobody gets out", LogKind.Casualty);
        if (out_ == 0) return;
        // The crews make their way back on foot to where the platoon started.
        crews.Order = GroupOrder.Hold;
        crews.NextThink = Time;
        if (crews.Leader == null || !crews.Leader.IsActive)
        {
            var lead = tank.Crew.FirstOrDefault(m => m.IsActive);
            if (lead != null) MakeLeader(crews, lead, initial: false);
        }
        if (crews.Teams.Count == 0)
        {
            var team = new Team { Id = 0, Role = TeamRole.Rifle };
            foreach (var m in crews.Members) { team.Members.Add(m); m.Team = team; }
            crews.Teams.Add(team);
        }
    }

    /// <summary>Crew aboard ride with their tank (so that when they get out, they're in the right place).</summary>
    private void CarryCrews()
    {
        foreach (var s in _soldiers)
            if (s.Status == SoldierStatus.Mounted && s.CrewOf is { } t) { s.Position = t.Position; s.Facing = t.Facing; }
    }

    /// <summary>A crew stuck in a tank that can't move, with enemy infantry close and none of their own: they may
    /// get out before someone climbs up with a grenade. A steadier crew holds on longer.</summary>
    internal void MaybeAbandonStuck(Soldier tank, float nearestEnemyInfantry, bool friendlyInfantryNear)
    {
        if (!tank.Immobilised && DriveFactor(tank) > 0f) return;
        if (nearestEnemyInfantry > 35f || friendlyInfantryNear) return;
        if (Rng.Chance(0.06f)) Abandon(tank, "stuck, with enemy infantry closing in");
    }
}

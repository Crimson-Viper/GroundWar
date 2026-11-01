using System.Numerics;
using GroundWar.Sim.Data;
using GroundWar.Sim.Map;

namespace GroundWar.Sim.Units;

public enum Side { Blue = 0, Red = 1 }

/// <summary>Mounted: a tank crewman inside his tank (milestone 8). He takes no part until he bails out.</summary>
public enum SoldierStatus { Active, Wounded, Dead, Routed, Mounted }

/// <summary>A tank crewman's job (milestone 8). See docs/systems/tanks.md.</summary>
public enum CrewRole { None, Commander, Gunner, Loader, Driver, BowGunner }

public enum Stance { Standing, Crouched, Prone }

/// <summary>Steady, then Shaken (slower, less accurate), Pinned (won't advance), Broken (runs, ignores orders).</summary>
public enum MoraleState { Steady, Shaken, Pinned, Broken }

public enum SoldierAction { Idle, TakeCover, Engage, Advance, HugCover, Flee, Dig }

public enum MoveMode { None, Walk, Run, Crawl }

/// <summary>What his squad leader wants from him: stay in an area, or get to one.</summary>
public enum TaskMode { Hold, Move }

public sealed class Soldier
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public Group Group { get; init; } = null!;
    public WeaponDef Weapon { get; init; } = null!;
    public float Skill { get; set; }
    public float StartMorale { get; set; }
    public bool IsLeader { get; set; }

    public string Label => $"{(Side == Side.Blue ? 'B' : 'R')}{Id}";
    public string Role => Vehicle != null ? Vehicle.Name
        : CrewRole != CrewRole.None ? CrewRole switch { CrewRole.BowGunner => "Tank bow gunner", var r => $"Tank {r.ToString().ToLowerInvariant()}" }
        : IsLeader ? "Sergeant" : Weapon.AntiTankOnly ? "Bazooka gunner" : Weapon.CanHurtArmour ? "Anti-tank rifleman" : Weapon.IsMachineGun ? "MG gunner" : "Rifleman";

    // Vehicle. A tank is one "soldier" standing for the whole crew: Weapon is its machine gun,
    // MainGun its cannon. Facing is the hull; the turret turns on its own.
    public VehicleDef? Vehicle { get; init; }
    public bool IsTank => Vehicle != null;
    public WeaponDef? MainGun { get; init; }
    public Vector2 TurretFacing { get; set; } = Vector2.UnitX;
    public int MainGunAmmo { get; set; }
    public float MainGunTimer { get; set; }
    /// <summary>Who or where the main gun is laid on.</summary>
    public Soldier? GunTarget { get; set; }
    public Vector2? GunAim { get; set; }
    /// <summary>Tracks broken: the tank can still shoot but not move.</summary>
    public bool Immobilised { get; set; }
    /// <summary>The tank stops to fire its main gun (a "short halt") and doesn't move off before this time.</summary>
    public float HaltUntil { get; set; }
    /// <summary>Armour-piercing rounds (for enemy tanks); MainGunAmmo is the high-explosive rounds.</summary>
    public int ApAmmo { get; set; }
    /// <summary>The five men inside (milestone 8): commander, gunner, loader, driver, bow gunner.</summary>
    public List<Soldier> Crew { get; } = new();
    /// <summary>For a crewman: his job and his tank.</summary>
    public CrewRole CrewRole { get; init; }
    public Soldier? CrewOf { get; init; }
    /// <summary>A crewman who got out of his tank (when it was knocked out or abandoned).</summary>
    public bool GotOut { get; set; }
    /// <summary>A crewman still inside his tank: at his post, or killed or wounded in it. Not drawn on the ground.</summary>
    public bool InTank => CrewOf != null && !GotOut;
    /// <summary>Platoon (milestone 8): where the platoon leader wants this tank, and whether it is halted to watch
    /// over the tanks that are moving.</summary>
    public Vector2? PlatoonGoal { get; set; }
    public bool Overwatch { get; set; }
    /// <summary>The crew have got out (knocked out, or abandoned).</summary>
    public bool BailedOut { get; set; }

    // Body
    public Vector2 Position { get; set; }
    public Vector2 Facing { get; set; } = Vector2.UnitX;
    public SoldierStatus Status { get; set; } = SoldierStatus.Active;
    public Stance Stance { get; set; } = Stance.Standing;
    public bool IsMoving => MoveMode != MoveMode.None && Path != null;
    public bool IsActive => Status == SoldierStatus.Active;
    /// <summary>Can still fight: alive, unhurt, and not running away.</summary>
    public bool IsEffective => Status == SoldierStatus.Active && MoraleState != MoraleState.Broken;

    // Mind
    public float Morale { get; set; }
    public float Suppression { get; set; }
    public MoraleState MoraleState { get; set; } = MoraleState.Steady;
    public SoldierAction Action { get; set; } = SoldierAction.Idle;
    public float ActionStarted { get; set; }
    /// <summary>Plain-words reason for the current action, shown in the debug overlay.</summary>
    public string Thought { get; set; } = "";
    public float LastProtection { get; set; }

    // Weapon
    public int Magazine { get; set; }
    public int SpareMagazines { get; set; }
    public float ReloadTimer { get; set; }
    public float FireTimer { get; set; }
    public int BurstRemaining { get; set; }
    public float BurstTimer { get; set; }
    public Soldier? Target { get; set; }
    public List<Soldier> VisibleEnemies { get; } = new();

    // Movement
    public List<Vector2>? Path { get; set; }
    public int PathIndex { get; set; }
    public MoveMode MoveMode { get; set; }
    public Vector2? Destination { get; set; }
    public CoverPoint? Cover { get; set; }
    /// <summary>Seconds spent digging a foxhole at <see cref="DigSpot"/>.</summary>
    public float DigProgress { get; set; }
    public Vector2 DigSpot { get; set; }

    // Orders from the squad leader
    public Team? Team { get; set; }
    public TaskMode Task { get; set; } = TaskMode.Hold;
    public Vector2 TaskPosition { get; set; }
    public float TaskRadius { get; set; } = 10f;
    public bool TaskRun { get; set; }
    public bool TaskAssault { get; set; }
    /// <summary>Where the squad wants fire put down, even if he can't see anyone there.</summary>
    public Vector2? FireAt { get; set; }
    /// <summary>The spot he is firing at when he has no man in his sights (area fire).</summary>
    public Vector2? AreaTarget { get; set; }

    // Timers and memory
    public float NextThink { get; set; }
    public float NextPerceive { get; set; }
    public float LastFiredTime { get; set; } = -999f;
    public float LastSuppressedTime { get; set; } = -999f;
    public float LastShotAtTime { get; set; } = -999f;
    public Vector2 LastShotFrom { get; set; }

    // Stats for the after-action report and tests
    public int ShotsFired { get; set; }
    public int Hits { get; set; }
    public float TimeInCover { get; set; }
    public float TimeUnderFire { get; set; }

    public bool InCover => Cover != null && Vector2.DistanceSquared(Position, Cover.Position) < 0.8f * 0.8f;
    public bool UnderFire(float now, float window) => now - LastShotAtTime < window;
}

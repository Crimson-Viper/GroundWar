namespace GroundWar.Sim.Data;

/// <summary>A vehicle type from data/vehicles.json. First iteration: a tank with a main gun and a machine gun.</summary>
public sealed class VehicleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Metres per second on open ground, forwards.</summary>
    public float Speed { get; set; } = 4f;
    /// <summary>Degrees per second the hull can turn.</summary>
    public float TurnRate { get; set; } = 35f;
    /// <summary>Degrees per second the turret can turn.</summary>
    public float TurretTurnRate { get; set; } = 30f;
    public float Length { get; set; } = 6f;
    public float Width { get; set; } = 3f;
    /// <summary>Armour in millimetres. A hit only goes through if the weapon's penetration is higher.</summary>
    public float ArmourFront { get; set; } = 30f;
    public float ArmourSide { get; set; } = 20f;
    public float ArmourRear { get; set; } = 12f;
    /// <summary>Weapon ids from weapons.json.</summary>
    public string MainGun { get; set; } = "tank_gun";
    public string MachineGun { get; set; } = "tank_mg";
    /// <summary>Armour-piercing rounds for the main gun (milestone 8): weapon id, and how many are carried.</summary>
    public string MainGunAp { get; set; } = "";
    public int ApRounds { get; set; }
    /// <summary>Men inside: commander, gunner, loader, driver, bow gunner (milestone 8). 0 = no crew modelled.</summary>
    public int Crew { get; set; }
    /// <summary>Chances for a hit that goes through the armour (the rest wound the crew, see KnockedOut / Immobilised).</summary>
    public float KnockOutChance { get; set; } = 0.45f;
    public float ImmobiliseChance { get; set; } = 0.3f;
    /// <summary>Chance that a hit that bounces off still breaks a track.</summary>
    public float TrackHitChance { get; set; } = 0.1f;
    /// <summary>How much easier a tank is to spot and to hit than a man standing in the open.</summary>
    public float SpotMultiplier { get; set; } = 4f;
    public float SizeHitMultiplier { get; set; } = 3f;
    /// <summary>The crew won't close to less than this from enemy infantry unless their own infantry is near.</summary>
    public float StandOff { get; set; } = 45f;
}

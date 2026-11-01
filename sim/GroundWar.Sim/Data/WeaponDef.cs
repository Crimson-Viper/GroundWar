namespace GroundWar.Sim.Data;

/// <summary>A weapon type from data/weapons.json.</summary>
public sealed class WeaponDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float Range { get; set; } = 400f;
    /// <summary>Pairs of [range in metres, chance to hit a standing, exposed man] for an average shooter.</summary>
    public float[][] Accuracy { get; set; } = Array.Empty<float[]>();
    public int BurstSize { get; set; } = 1;
    public float BurstInterval { get; set; } = 0.1f;
    /// <summary>How far misses land from the target, in metres.</summary>
    public float Spread { get; set; } = 1.5f;
    public float FireInterval { get; set; } = 4f;
    public float FireIntervalJitter { get; set; } = 1f;
    public int MagazineSize { get; set; } = 5;
    public int Magazines { get; set; } = 10;
    public float ReloadTime { get; set; } = 4f;
    public float Suppression { get; set; } = 10f;
    public float SuppressionRadius { get; set; } = 3f;
    /// <summary>How much enemies want to shoot the man carrying this (machine guns first).</summary>
    public float Threat { get; set; } = 1f;
    public bool IsMachineGun { get; set; }
    /// <summary>Millimetres of armour this weapon can go through. 0 = can't hurt a tank.</summary>
    public float Penetration { get; set; }
    /// <summary>Radius of the explosion in metres, for shells. 0 = a bullet.</summary>
    public float BlastRadius { get; set; }
    public bool IsShell => BlastRadius > 0f;
    public bool CanHurtArmour => Penetration > 0f;
    /// <summary>Penetration falls off with range: full at the muzzle, half at maximum range.</summary>
    public float PenetrationAt(float range) => Penetration * (1f - 0.5f * Math.Clamp(range / MathF.Max(Range, 1f), 0f, 1f));

    public float HitChanceAt(float range)
    {
        if (Accuracy.Length == 0) return 0f;
        if (range <= Accuracy[0][0]) return Accuracy[0][1];
        for (int i = 1; i < Accuracy.Length; i++)
        {
            if (range <= Accuracy[i][0])
            {
                float r0 = Accuracy[i - 1][0], r1 = Accuracy[i][0];
                float t = (range - r0) / MathF.Max(r1 - r0, 0.001f);
                return Accuracy[i - 1][1] + (Accuracy[i][1] - Accuracy[i - 1][1]) * t;
            }
        }
        return range <= Range ? Accuracy[^1][1] : 0f;
    }
}

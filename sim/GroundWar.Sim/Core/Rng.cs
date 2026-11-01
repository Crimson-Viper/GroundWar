namespace GroundWar.Sim.Core;

/// <summary>Small deterministic random generator (SplitMix64). Same seed, same battle, on every machine.</summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) { _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

    public ulong NextULong()
    {
        ulong z = (_state += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform float in [0, 1).</summary>
    public float NextFloat() => (NextULong() >> 40) * (1f / (1UL << 24));

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    public int NextInt(int maxExclusive) => maxExclusive <= 0 ? 0 : (int)(NextULong() % (ulong)maxExclusive);

    public bool Chance(float p) => NextFloat() < p;

    /// <summary>Roughly normal value with mean 0 and standard deviation 1 (sum of uniforms).</summary>
    public float Gaussian()
    {
        float sum = 0f;
        for (int i = 0; i < 6; i++) sum += NextFloat();
        return (sum - 3f) * 1.4142f;
    }
}

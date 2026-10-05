namespace Nodey.Core.Image.Noise;

/// <summary>
/// Deterministic integer hashing shared by all noise generators.
/// Uses only integer arithmetic (murmur3-style finalizer), so results are
/// identical on every platform — this is what makes every generator
/// reproducible from its seed alone.
/// </summary>
public static class NoiseHash
{
    /// <summary>Hashes a 2D integer lattice coordinate against a seed.</summary>
    public static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= Mix((uint)x);
            h = (h << 13) | (h >> 19);
            h ^= Mix((uint)y + 0x9E3779B9u);
            h = (h << 13) | (h >> 19);
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            return h;
        }
    }

    /// <summary>
    /// Two independent uniform floats in [0, 1) derived from one coordinate
    /// (two different hash lanes), for e.g. feature points.
    /// </summary>
    public static void Unit2(int x, int y, int seed, out float a, out float b)
    {
        a = Unit(Hash(x, y, seed));
        b = Unit(Hash(x, y, seed ^ 0x5F356495));
    }

    /// <summary>Uniform float in [0, 1) from a hash value.</summary>
    public static float Unit(uint hash) => (hash & 0x7FFFFFFFu) / 2147483647f;

    private static uint Mix(uint v)
    {
        v ^= v >> 16;
        v *= 0x85EBCA77u;
        v ^= v >> 13;
        return v;
    }
}

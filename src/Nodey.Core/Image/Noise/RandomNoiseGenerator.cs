namespace Nodey.Core.Image.Noise;

/// <summary>
/// Per-pixel white noise: one uniform random value in [0, 1) per integer
/// pixel, hashed from (floor(x), floor(y)) and the seed. There is no
/// spatial coherence by design — octaves/persistence/lacunarity are ignored.
/// </summary>
public sealed class RandomNoiseGenerator : INoiseGenerator
{
    private readonly int _seed;

    public RandomNoiseGenerator(int seed) => _seed = seed;

    public float Sample(float x, float y, int octaves, float persistence, float lacunarity)
    {
        int px = (int)System.MathF.Floor(x);
        int py = (int)System.MathF.Floor(y);
        return NoiseHash.Unit(NoiseHash.Hash(px, py, _seed));
    }
}

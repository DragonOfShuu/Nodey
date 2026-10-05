namespace Nodey.Core.Image.Noise;

/// <summary>
/// Value noise: a lattice of uniform random values in [0, 1) (deterministic
/// hash of the lattice coordinate) bilinearly interpolated with a quadratic
/// smoothstep. The raw field is already in [0, 1], so the fBm sum needs no
/// further normalization.
/// </summary>
public sealed class ValueNoiseGenerator : INoiseGenerator
{
    private readonly int _seed;

    public ValueNoiseGenerator(int seed) => _seed = seed;

    public float Sample(float x, float y, int octaves, float persistence, float lacunarity)
        => NoiseMath.Fbm(Single, x, y, octaves, persistence, lacunarity);

    private float Single(float x, float y)
    {
        int x0 = (int)System.MathF.Floor(x);
        int y0 = (int)System.MathF.Floor(y);
        float u = NoiseMath.Smoothstep(x - x0);
        float v = NoiseMath.Smoothstep(y - y0);

        float v00 = NoiseHash.Unit(NoiseHash.Hash(x0, y0, _seed));
        float v10 = NoiseHash.Unit(NoiseHash.Hash(x0 + 1, y0, _seed));
        float v01 = NoiseHash.Unit(NoiseHash.Hash(x0, y0 + 1, _seed));
        float v11 = NoiseHash.Unit(NoiseHash.Hash(x0 + 1, y0 + 1, _seed));

        return NoiseMath.Lerp(
            NoiseMath.Lerp(v00, v10, u),
            NoiseMath.Lerp(v01, v11, u),
            v);
    }
}

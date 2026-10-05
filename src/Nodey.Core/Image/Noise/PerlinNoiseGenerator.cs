namespace Nodey.Core.Image.Noise;

/// <summary>
/// Classic Perlin (gradient) noise. Each lattice point is assigned one of
/// eight unit gradient directions (cardinal + diagonal) by a deterministic
/// hash; the field is the bilinear interpolation (Perlin's improved fade) of
/// the gradient dot products at the four surrounding corners.
/// </summary>
/// <remarks>
/// With unit gradients the raw field lies in [-sqrt(2), sqrt(2)], so the
/// fBm sum is normalized to 0..1 by <c>0.5 + 0.5 * v / sqrt(2)</c>.
/// </remarks>
public sealed class PerlinNoiseGenerator : INoiseGenerator
{
    private const float Sqrt2 = 1.4142135623730951f;
    private const float InvSqrt2 = 0.7071067811865476f;

    // Eight unit gradient directions: E, N, W, S and the four diagonals.
    private static readonly float[] GradientX =
    {
        1f, 0f, -1f, 0f,
        InvSqrt2, InvSqrt2, -InvSqrt2, -InvSqrt2,
    };
    private static readonly float[] GradientY =
    {
        0f, 1f, 0f, -1f,
        InvSqrt2, -InvSqrt2, InvSqrt2, -InvSqrt2,
    };

    private readonly int _seed;

    public PerlinNoiseGenerator(int seed) => _seed = seed;

    public float Sample(float x, float y, int octaves, float persistence, float lacunarity)
    {
        float v = NoiseMath.Fbm(Single, x, y, octaves, persistence, lacunarity);
        return 0.5f + 0.5f * v / Sqrt2;
    }

    private float Single(float x, float y)
    {
        int x0 = (int)System.MathF.Floor(x);
        int y0 = (int)System.MathF.Floor(y);
        float xf = x - x0;
        float yf = y - y0;
        float u = NoiseMath.Fade(xf);
        float v = NoiseMath.Fade(yf);

        float n00 = Dot(x0, y0, xf, yf);
        float n10 = Dot(x0 + 1, y0, xf - 1f, yf);
        float n01 = Dot(x0, y0 + 1, xf, yf - 1f);
        float n11 = Dot(x0 + 1, y0 + 1, xf - 1f, yf - 1f);

        return NoiseMath.Lerp(
            NoiseMath.Lerp(n00, n10, u),
            NoiseMath.Lerp(n01, n11, u),
            v);
    }

    private float Dot(int x, int y, float dx, float dy)
    {
        int g = (int)(NoiseHash.Hash(x, y, _seed) & 7u);
        return GradientX[g] * dx + GradientY[g] * dy;
    }
}

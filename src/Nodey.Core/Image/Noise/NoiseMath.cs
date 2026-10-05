namespace Nodey.Core.Image.Noise;

/// <summary>
/// Small shared math helpers for the noise generators (interpolation curves
/// and fractal Brownian motion).
/// </summary>
internal static class NoiseMath
{
    /// <summary>Linear interpolation of <paramref name="a"/> and <paramref name="b"/> at <paramref name="t"/>.</summary>
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Perlin's improved fade curve: 6t^5 - 15t^4 + 10t^3 (zero 1st/2nd derivative at 0 and 1).</summary>
    public static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    /// <summary>Quadratic smoothstep: 3t^2 - 2t^3.</summary>
    public static float Smoothstep(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// Fractal Brownian motion: sums <paramref name="single"/> at increasing
    /// frequencies with decreasing amplitude, normalized by the sum of the
    /// amplitudes. The result lies in the same range as <paramref name="single"/>.
    /// </summary>
    public static float Fbm(
        Func<float, float, float> single,
        float x, float y,
        int octaves, float persistence, float lacunarity)
    {
        int n = System.Math.Max(1, octaves);
        float total = 0f, amplitude = 1f, frequency = 1f, maxAmplitude = 0f;
        for (int i = 0; i < n; i++)
        {
            total += single(x * frequency, y * frequency) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return total / maxAmplitude;
    }
}

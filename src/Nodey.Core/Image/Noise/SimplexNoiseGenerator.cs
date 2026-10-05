namespace Nodey.Core.Image.Noise;

/// <summary>
/// 2D simplex noise, per Stefan Gustavson's "Simplex noise demystified"
/// (skew/unskew to the simplex grid, three corner contributions, the
/// standard 12-vector gradient table, and Gustavson's 70.0f 2D scale so the
/// raw output lands in ~[-1, 1]).
/// </summary>
/// <remarks>
/// The fBm sum is normalized to 0..1 by <c>0.5 + 0.5 * v</c>; the dense-grid
/// range test in <c>NoiseTests</c> verifies the bound holds.
/// </remarks>
public sealed class SimplexNoiseGenerator : INoiseGenerator
{
    // First two components of Gustavson's grad3 table (12 vectors).
    private static readonly float[] GradientX =
    {
        1f, -1f, 1f, -1f, 1f, -1f, 1f, -1f, 0f, 0f, 0f, 0f,
    };
    private static readonly float[] GradientY =
    {
        1f, 1f, -1f, -1f, 0f, 0f, 0f, 0f, 1f, -1f, 1f, -1f,
    };

    private readonly int _seed;

    public SimplexNoiseGenerator(int seed) => _seed = seed;

    public float Sample(float x, float y, int octaves, float persistence, float lacunarity)
    {
        float v = NoiseMath.Fbm(Single, x, y, octaves, persistence, lacunarity);
        return 0.5f + 0.5f * v;
    }

    private float Single(float xin, float yin)
    {
        const float F2 = 0.3660254037844386f;   // 0.5 * (sqrt(3) - 1), skew factor
        const float G2 = 0.21132486540518713f;  // (2 - sqrt(3)) / 6, unskew factor

        // Skew the input point to find the simplex cell it falls in.
        float s = (xin + yin) * F2;
        int i = (int)System.MathF.Floor(xin + s);
        int j = (int)System.MathF.Floor(yin + s);
        float t = (i + j) * G2;
        float x0 = xin - (i - t);
        float y0 = yin - (j - t);

        int i1, j1;
        if (x0 > y0)
        {
            i1 = 1; j1 = 0; // lower triangle
        }
        else
        {
            i1 = 0; j1 = 1; // upper triangle
        }

        // Distances from the three corners, in unskewed coordinates.
        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2;
        float y2 = y0 - 1f + 2f * G2;

        float n0 = 0f, n1 = 0f, n2 = 0f;

        float t0 = 0.5f - x0 * x0 - y0 * y0;
        if (t0 > 0f)
        {
            t0 *= t0;
            int gi = (int)(NoiseHash.Hash(i, j, _seed) % 12u);
            n0 = t0 * t0 * (GradientX[gi] * x0 + GradientY[gi] * y0);
        }

        float t1 = 0.5f - x1 * x1 - y1 * y1;
        if (t1 > 0f)
        {
            t1 *= t1;
            int gi = (int)(NoiseHash.Hash(i + i1, j + j1, _seed) % 12u);
            n1 = t1 * t1 * (GradientX[gi] * x1 + GradientY[gi] * y1);
        }

        float t2 = 0.5f - x2 * x2 - y2 * y2;
        if (t2 > 0f)
        {
            t2 *= t2;
            int gi = (int)(NoiseHash.Hash(i + 1, j + 1, _seed) % 12u);
            n2 = t2 * t2 * (GradientX[gi] * x2 + GradientY[gi] * y2);
        }

        return 70f * (n0 + n1 + n2);
    }
}

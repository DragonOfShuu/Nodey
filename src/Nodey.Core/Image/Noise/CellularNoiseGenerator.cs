namespace Nodey.Core.Image.Noise;

/// <summary>
/// Cellular / Worley noise (F1): for each point, the distance to the nearest
/// hashed feature point. The search covers the 5x5 block of cells around the
/// point's cell, which is exact for F1: the same-cell feature point is at
/// most sqrt(2) away (the cell diagonal), so any point outside the 5x5 block
/// — at least 2 units away — can never be the nearest. The result is divided
/// by sqrt(2) so the field lies exactly in [0, 1] (distance 0 at a feature
/// point, approaching 1 at the farthest possible query point).
/// </summary>
public sealed class CellularNoiseGenerator : INoiseGenerator
{
    private const float Sqrt2 = 1.4142135623730951f;

    private readonly int _seed;

    public CellularNoiseGenerator(int seed) => _seed = seed;

    public float Sample(float x, float y, int octaves, float persistence, float lacunarity)
        => NoiseMath.Fbm(Single, x, y, octaves, persistence, lacunarity);

    private float Single(float x, float y)
    {
        int cx = (int)System.MathF.Floor(x);
        int cy = (int)System.MathF.Floor(y);
        float minDist2 = float.MaxValue;
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int fx = cx + dx;
                int fy = cy + dy;
                NoiseHash.Unit2(fx, fy, _seed, out float px, out float py);
                float ddx = (fx + px) - x;
                float ddy = (fy + py) - y;
                float d2 = ddx * ddx + ddy * ddy;
                if (d2 < minDist2)
                    minDist2 = d2;
            }
        }
        return System.MathF.Sqrt(minDist2) / Sqrt2;
    }
}

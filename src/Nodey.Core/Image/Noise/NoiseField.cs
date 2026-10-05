using Nodey.Core.Types;

namespace Nodey.Core.Image.Noise;

/// <summary>
/// Generates 2D scalar fields (<see cref="SignalType.Scalar2D"/>) from the
/// <see cref="INoiseGenerator"/> implementations.
/// </summary>
public static class NoiseField
{
    /// <summary>
    /// Generates a <paramref name="width"/> x <paramref name="height"/> scalar
    /// field with the given algorithm.
    /// </summary>
    /// <remarks>
    /// Pixel (x, y) samples the generator at noise-space coordinate
    /// <c>((x + 0.5) * scale + offsetX, (y + 0.5) * scale + offsetY)</c>
    /// (pixel centers), so <paramref name="scale"/> is "noise cells per
    /// pixel" and the offsets shift the pattern in noise space.
    /// Output convention (Scalar2D): value in R, G = B = 0, A = 1.
    /// </remarks>
    public static PixelBuffer Generate(
        NoiseAlgorithm algorithm,
        int width,
        int height,
        float scale,
        int octaves = 4,
        float persistence = 0.5f,
        float lacunarity = 2f,
        int seed = 0,
        float offsetX = 0f,
        float offsetY = 0f)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        if (octaves < 1)
            throw new ArgumentOutOfRangeException(nameof(octaves), octaves, "Octaves must be at least 1.");
        if (scale <= 0f)
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must be positive.");

        INoiseGenerator generator = CreateGenerator(algorithm, seed);
        var field = new PixelBuffer(width, height);
        float[] data = field.Rgba;
        for (int y = 0; y < height; y++)
        {
            float ny = (y + 0.5f) * scale + offsetY;
            for (int x = 0; x < width; x++)
            {
                float nx = (x + 0.5f) * scale + offsetX;
                float v = generator.Sample(nx, ny, octaves, persistence, lacunarity);
                int i = (y * width + x) * 4;
                data[i] = v;    // Scalar2D convention: value in the red channel
                data[i + 1] = 0f;
                data[i + 2] = 0f;
                data[i + 3] = 1f;
            }
        }
        return field;
    }

    /// <summary>Creates the generator for <paramref name="algorithm"/> with the given seed.</summary>
    public static INoiseGenerator CreateGenerator(NoiseAlgorithm algorithm, int seed) => algorithm switch
    {
        NoiseAlgorithm.Perlin => new PerlinNoiseGenerator(seed),
        NoiseAlgorithm.Simplex => new SimplexNoiseGenerator(seed),
        NoiseAlgorithm.Value => new ValueNoiseGenerator(seed),
        NoiseAlgorithm.Cellular => new CellularNoiseGenerator(seed),
        NoiseAlgorithm.Random => new RandomNoiseGenerator(seed),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown noise algorithm."),
    };
}

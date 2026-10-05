namespace Nodey.Core.Image.Noise;

/// <summary>
/// The noise algorithm used by <see cref="NoiseField"/> and the Noise node
/// (STEP-3). Each value maps to a generator implementing
/// <see cref="INoiseGenerator"/>.
/// </summary>
public enum NoiseAlgorithm
{
    /// <summary>Classic gradient (Perlin) noise.</summary>
    Perlin,

    /// <summary>Simplex noise (Gustavson's algorithm).</summary>
    Simplex,

    /// <summary>Value noise (smoothstep-interpolated lattice of random values).</summary>
    Value,

    /// <summary>Cellular / Worley noise (distance to the nearest random feature point).</summary>
    Cellular,

    /// <summary>
    /// Per-pixel white noise: a uniform random value per pixel, no fBm
    /// octaves (octaves/persistence/lacunarity are ignored by design).
    /// </summary>
    Random,
}

namespace Nodey.Core.Image.Noise;

/// <summary>
/// A deterministic noise field generator. All generators are pure functions
/// of their (x, y) input and the seed fixed at construction: the same
/// generator always returns the same value for the same input.
/// </summary>
public interface INoiseGenerator
{
    /// <summary>
    /// Samples the noise at (x, y) in *noise space* (the caller applies
    /// Scale/Offset before calling) and returns a value in 0..1.
    /// </summary>
    /// <param name="x">X coordinate in noise space.</param>
    /// <param name="y">Y coordinate in noise space.</param>
    /// <param name="octaves">Number of fBm octaves (&lt; 1 is treated as 1).
    /// Ignored by <see cref="NoiseAlgorithm.Random"/> by design.</param>
    /// <param name="persistence">Amplitude decay between octaves (0..1).</param>
    /// <param name="lacunarity">Frequency growth between octaves (&gt; 1).</param>
    float Sample(float x, float y, int octaves, float persistence, float lacunarity);
}

namespace Nodey.Core.Tests;

/// <summary>
/// Small floating-point comparison helpers for the engine tests.
/// </summary>
internal static class Approx
{
    /// <summary>Default epsilon for 0..1 float comparisons.</summary>
    public const float Eps = 1e-4f;

    /// <summary>Asserts <paramref name="actual"/> is within <paramref name="eps"/> of <paramref name="expected"/>.</summary>
    public static void Near(float actual, float expected, float eps = Eps)
        => Assert.True(
            System.Math.Abs(actual - expected) <= eps,
            $"expected {expected} ± {eps} but got {actual}");

    /// <summary>Asserts two arrays are element-wise within <paramref name="eps"/> (same length).</summary>
    public static void NearAll(float[] actual, float[] expected, float eps = Eps)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++)
            Assert.True(
                System.Math.Abs(actual[i] - expected[i]) <= eps,
                $"index {i}: expected {expected[i]} ± {eps} but got {actual[i]}");
    }

    /// <summary>Builds a w x h buffer with a per-pixel function (tests helper).</summary>
    public static Nodey.Core.Image.PixelBuffer Make(int w, int h, System.Func<int, int, (float R, float G, float B, float A)> pixel)
    {
        var buffer = new Nodey.Core.Image.PixelBuffer(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                (float r, float g, float b, float a) = pixel(x, y);
                buffer.SetPixel(x, y, new Nodey.Core.Image.Rgba(r, g, b, a));
            }
        return buffer;
    }
}

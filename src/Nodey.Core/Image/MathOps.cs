using Nodey.Core.Types;

namespace Nodey.Core.Image;

/// <summary>
/// Element-wise math operations (the "Math" node in STEP-3).
/// </summary>
public enum MathOperation
{
    /// <summary><c>a + b</c></summary>
    Add,

    /// <summary><c>a - b</c></summary>
    Subtract,

    /// <summary><c>a * b</c></summary>
    Multiply,

    /// <summary><c>a / b</c>; division by zero yields 0 (documented edge case).</summary>
    Divide,
}

/// <summary>
/// Element-wise math on <see cref="SignalType.Scalar2D"/> fields
/// (value in the red channel; see <c>NoiseField</c>).
/// </summary>
/// <remarks>
/// <see cref="ApplyToBuffers"/> reads the red channel of each input pixel and
/// writes the result to the red channel of a new buffer (G = B = 0, A = 1),
/// so two scalar fields compose into another scalar field.
/// </remarks>
public static class MathOps
{
    /// <summary>Applies the operation to two scalar values.</summary>
    /// <remarks>
    /// Division by zero returns 0 by design (documented edge case); all other
    /// operations are the plain float arithmetic.
    /// </remarks>
    public static float Apply(MathOperation op, float a, float b) => op switch
    {
        MathOperation.Add => a + b,
        MathOperation.Subtract => a - b,
        MathOperation.Multiply => a * b,
        MathOperation.Divide => b == 0f ? 0f : a / b,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown math operation."),
    };

    /// <summary>
    /// Applies the operation per pixel (red channel) to two same-size scalar
    /// fields and returns a new scalar field.
    /// </summary>
    public static PixelBuffer ApplyToBuffers(MathOperation op, PixelBuffer a, PixelBuffer b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!a.SameSizeAs(b))
            throw new ArgumentException(
                $"Buffers must have the same dimensions ({a.Width}x{a.Height} vs {b.Width}x{b.Height}).",
                nameof(b));

        float[] da = a.Rgba;
        float[] db = b.Rgba;
        var dst = new PixelBuffer(a.Width, a.Height);
        float[] o = dst.Rgba;
        for (int i = 0; i < o.Length; i += 4)
        {
            o[i] = Apply(op, da[i], db[i]);
            o[i + 1] = 0f;
            o[i + 2] = 0f;
            o[i + 3] = 1f;
        }
        return dst;
    }
}

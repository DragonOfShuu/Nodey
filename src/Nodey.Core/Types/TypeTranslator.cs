using Nodey.Core.Image;

namespace Nodey.Core.Types;

/// <summary>
/// The "foreign nodes" type-translation system (PLAN.md §5.4, RESEARCH.md §1).
/// Converts a value of one <see cref="SignalType"/> into another, so a node
/// whose port <b>expects</b> type X can accept a connection from a node whose
/// output is type Y (Y ∈ the port's AcceptedTypes).
/// </summary>
/// <remarks>
/// Translation table (source → destination):
/// <list type="table">
/// <item><term>Scalar → Scalar2D</term><description>fill a W×H field with the constant value</description></item>
/// <item><term>Scalar → Color2D</term><description>solid W×H image of the gray value (R=G=B=v, A=1)</description></item>
/// <item><term>Scalar2D → Scalar</term><description>downsample to a single value (mean of the red channel)</description></item>
/// <item><term>Scalar2D → Color2D</term><description>interpret the field as luminance: R=G=B=v, A=1</description></item>
/// <item><term>Color2D → Scalar</term><description>mean BT.709 luminance over the image</description></item>
/// <item><term>Color2D → Scalar2D</term><description>per-pixel BT.709 luminance into the red channel</description></item>
/// </list>
/// Identity (same type) returns the input unchanged (same instance).
/// Luminance is BT.709: <c>0.2126 R + 0.7152 G + 0.0722 B</c> (matches
/// STEP-2's <see cref="PixelBuffer.Luminance"/>). No clamping is applied —
/// math output may legitimately exceed 0..1 and is left to the consumer.
/// </remarks>
public static class TypeTranslator
{
    /// <summary>
    /// Translates a scalar value into a 2D buffer of the destination type.
    /// </summary>
    public static PixelBuffer FromScalar(double value, SignalType to, int width, int height)
    {
        float v = (float)value;
        switch (to)
        {
            case SignalType.Scalar2D:
                var field = new PixelBuffer(width, height);
                float[] d = field.Rgba;
                for (int i = 0; i < d.Length; i += 4)
                {
                    d[i] = v; d[i + 1] = 0f; d[i + 2] = 0f; d[i + 3] = 1f;
                }
                return field;
            case SignalType.Color2D:
                return PixelBuffer.Solid(width, height, v, v, v, 1f);
            case SignalType.Scalar:
                // A Scalar target is represented as a 1×1 buffer holding the value.
                var one = new PixelBuffer(1, 1);
                one.Rgba[0] = v; one.Rgba[1] = 0f; one.Rgba[2] = 0f; one.Rgba[3] = 1f;
                return one;
            default:
                throw new ArgumentOutOfRangeException(nameof(to), to, "Unknown destination signal type.");
        }
    }

    /// <summary>
    /// Translates a scalar field (Scalar2D) into another type.
    /// </summary>
    public static PixelBuffer FromField(PixelBuffer field, SignalType to, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(field);
        switch (to)
        {
            case SignalType.Scalar2D:
                // Identity, but resampled to the document size if needed.
                return SameSize(field, width, height) ? field : field.ScaleTo(width, height);
            case SignalType.Color2D:
                var color = new PixelBuffer(width, height);
                float[] o = color.Rgba;
                if (SameSize(field, width, height))
                {
                    float[] s = field.Rgba;
                    for (int i = 0; i < o.Length; i += 4)
                    {
                        o[i] = s[i]; o[i + 1] = s[i]; o[i + 2] = s[i]; o[i + 3] = 1f;
                    }
                }
                else
                {
                    PixelBuffer f = field.ScaleTo(width, height);
                    float[] s = f.Rgba;
                    for (int i = 0; i < o.Length; i += 4)
                    {
                        o[i] = s[i]; o[i + 1] = s[i]; o[i + 2] = s[i]; o[i + 3] = 1f;
                    }
                }
                return color;
            case SignalType.Scalar:
                var one = new PixelBuffer(1, 1);
                one.Rgba[0] = MeanRed(field);
                one.Rgba[3] = 1f;
                return one;
            default:
                throw new ArgumentOutOfRangeException(nameof(to), to, "Unknown destination signal type.");
        }
    }

    /// <summary>
    /// Translates a color image (Color2D) into another type.
    /// </summary>
    public static PixelBuffer FromImage(PixelBuffer image, SignalType to, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(image);
        switch (to)
        {
            case SignalType.Color2D:
                return SameSize(image, width, height) ? image : image.ScaleTo(width, height);
            case SignalType.Scalar2D:
            {
                PixelBuffer src = SameSize(image, width, height) ? image : image.ScaleTo(width, height);
                var field = new PixelBuffer(width, height);
                float[] s = src.Rgba;
                float[] o = field.Rgba;
                for (int i = 0; i < o.Length; i += 4)
                {
                    float lum = 0.2126f * s[i] + 0.7152f * s[i + 1] + 0.0722f * s[i + 2];
                    o[i] = lum; o[i + 1] = 0f; o[i + 2] = 0f; o[i + 3] = 1f;
                }
                return field;
            }
            case SignalType.Scalar:
            {
                PixelBuffer src = SameSize(image, width, height) ? image : image.ScaleTo(width, height);
                var one = new PixelBuffer(1, 1);
                one.Rgba[0] = MeanLuminance(src);
                one.Rgba[3] = 1f;
                return one;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(to), to, "Unknown destination signal type.");
        }
    }

    /// <summary>
    /// The single entry point the evaluator uses: translates
    /// <paramref name="from"/> into <paramref name="to"/>. A
    /// <see cref="SignalType.Scalar"/> source is carried in
    /// <paramref name="scalarValue"/> (the buffer is ignored); a 2D source
    /// requires <paramref name="buffer"/>.
    /// </summary>
    public static PixelBuffer Translate(
        SignalType from, PixelBuffer? buffer, double scalarValue, SignalType to, int width, int height)
    {
        // Dispatching to the per-source methods handles identity correctly:
        // a same-size 2D identity returns the same instance, a different size
        // is resampled, and a Scalar source repacks the value.
        switch (from)
        {
            case SignalType.Scalar:
                return FromScalar(scalarValue, to, width, height);
            case SignalType.Scalar2D:
                return FromField(buffer!, to, width, height);
            case SignalType.Color2D:
                return FromImage(buffer!, to, width, height);
            default:
                throw new ArgumentOutOfRangeException(nameof(from), from, "Unknown source signal type.");
        }
    }

    private static bool SameSize(PixelBuffer b, int w, int h) => b.Width == w && b.Height == h;

    /// <summary>Mean of the red channel (the Scalar2D value channel).</summary>
    public static float MeanRed(PixelBuffer field)
    {
        ArgumentNullException.ThrowIfNull(field);
        float[] d = field.Rgba;
        float sum = 0f;
        int n = 0;
        for (int i = 0; i < d.Length; i += 4)
        {
            sum += d[i];
            n++;
        }
        return n == 0 ? 0f : sum / n;
    }

    /// <summary>Mean BT.709 luminance over the whole image.</summary>
    public static float MeanLuminance(PixelBuffer image)
    {
        ArgumentNullException.ThrowIfNull(image);
        float[] d = image.Rgba;
        float sum = 0f;
        int n = 0;
        for (int i = 0; i < d.Length; i += 4)
        {
            sum += 0.2126f * d[i] + 0.7152f * d[i + 1] + 0.0722f * d[i + 2];
            n++;
        }
        return n == 0 ? 0f : sum / n;
    }
}

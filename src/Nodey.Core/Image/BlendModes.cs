namespace Nodey.Core.Image;

/// <summary>
/// Per-pixel blend modes (the "Blend" node in STEP-3).
/// </summary>
/// <remarks>
/// Formulas (all channels 0..1; <c>b</c> = base, <c>f</c> = blend):
/// <list type="bullet">
/// <item><see cref="Normal"/>: <c>f</c></item>
/// <item><see cref="Multiply"/>: <c>b * f</c></item>
/// <item><see cref="Screen"/>: <c>b + f - b * f</c></item>
/// <item><see cref="Overlay"/>: <c>b &lt;= 0.5 ? 2*b*f : 1 - 2*(1-b)*(1-f)</c></item>
/// <item><see cref="SoftLight"/>: Photoshop formula (see <c>SoftLight</c>)</item>
/// <item><see cref="HardLight"/>: <c>f &lt;= 0.5 ? 2*b*f : 1 - 2*(1-b)*(1-f)</c></item>
/// <item><see cref="Difference"/>: <c>|b - f|</c></item>
/// <item><see cref="Exclusion"/>: <c>b + f - 2*b*f</c></item>
/// <item><see cref="LinearBurn"/>: <c>max(0, b + f - 1)</c></item>
/// <item><see cref="LinearDodge"/>: <c>min(1, b + f)</c></item>
/// <item><see cref="Color"/> / <see cref="Luminosity"/>: non-separable, via HSL
/// (see <see cref="Hsl"/>).</item>
/// </list>
/// The separable formulas are the standard Photoshop definitions; SoftLight and
/// HardLight use the exact Photoshop piecewise formulas as given in the widely
/// used reference implementation "Photoshop Blend Modes in HLSL"
/// (Ryan Juckett, ryanjuckett.us).
/// </remarks>
public enum BlendMode
{
    /// <summary>Replace base with blend.</summary>
    Normal,

    /// <summary>Multiply: <c>b * f</c>.</summary>
    Multiply,

    /// <summary>Screen: <c>b + f - b * f</c>.</summary>
    Screen,

    /// <summary>Overlay (blend-driven): <c>b &lt;= 0.5 ? 2*b*f : 1 - 2*(1-b)*(1-f)</c>.</summary>
    Overlay,

    /// <summary>Soft Light (Photoshop piecewise formula).</summary>
    SoftLight,

    /// <summary>Hard Light (blend-driven overlay): <c>f &lt;= 0.5 ? 2*b*f : 1 - 2*(1-b)*(1-f)</c>.</summary>
    HardLight,

    /// <summary>Difference: <c>|b - f|</c>.</summary>
    Difference,

    /// <summary>Exclusion: <c>b + f - 2*b*f</c>.</summary>
    Exclusion,

    /// <summary>Linear Burn: <c>max(0, b + f - 1)</c>.</summary>
    LinearBurn,

    /// <summary>Linear Dodge: <c>min(1, b + f)</c>.</summary>
    LinearDodge,

    /// <summary>Color: blend's hue/saturation (HSL) over base's lightness.</summary>
    Color,

    /// <summary>Luminosity: base's hue/saturation (HSL) with blend's lightness.</summary>
    Luminosity,
}

/// <summary>
/// Blends two same-size <see cref="PixelBuffer"/>s with a <see cref="BlendMode"/>.
/// </summary>
/// <remarks>
/// The entry point is <see cref="Apply"/> (the spec's literal
/// <c>Blend.Blend</c> is not legal C# — member names cannot match the
/// enclosing type; <c>Apply</c> matches <c>Filter.Apply</c>).
/// </remarks>
public static class Blend
{
    /// <summary>
    /// Blends <paramref name="blendImg"/> onto <paramref name="baseImg"/> and
    /// returns a new buffer.
    /// </summary>
    /// <remarks>
    /// Per channel (RGB):
    /// <c>out = base * (1 - opacity) + mode(base, blend) * opacity</c>.
    /// Alpha uses normal (straight) compositing for the mode result, then the
    /// same opacity mix:
    /// <c>out.a = base.a * (1 - opacity) + (blend.a + base.a * (1 - blend.a)) * opacity</c>.
    /// </remarks>
    public static PixelBuffer Apply(PixelBuffer baseImg, PixelBuffer blendImg, BlendMode mode, float opacity)
    {
        ArgumentNullException.ThrowIfNull(baseImg);
        ArgumentNullException.ThrowIfNull(blendImg);
        if (!baseImg.SameSizeAs(blendImg))
            throw new ArgumentException(
                $"Buffers must have the same dimensions ({baseImg.Width}x{baseImg.Height} vs {blendImg.Width}x{blendImg.Height}).",
                nameof(blendImg));

        float op = System.Math.Clamp(opacity, 0f, 1f);
        float[] b = baseImg.Rgba;
        float[] f = blendImg.Rgba;
        var dst = new PixelBuffer(baseImg.Width, baseImg.Height);
        float[] o = dst.Rgba;

        if (mode == BlendMode.Color || mode == BlendMode.Luminosity)
            BlendNonSeparable(b, f, o, mode, op);
        else
            BlendSeparable(b, f, o, mode, op);

        return dst;
    }

    /// <summary>
    /// Separable (per-channel) blend formulas, standard Photoshop definitions.
    /// All inputs/outputs are 0..1.
    /// </summary>
    public static float Channel(float baseC, float blendC, BlendMode mode) => mode switch
    {
        BlendMode.Normal => blendC,
        BlendMode.Multiply => baseC * blendC,
        BlendMode.Screen => baseC + blendC - baseC * blendC,
        BlendMode.Overlay => baseC <= 0.5f
            ? 2f * baseC * blendC
            : 1f - 2f * (1f - baseC) * (1f - blendC),
        BlendMode.SoftLight => SoftLight(baseC, blendC),
        BlendMode.HardLight => blendC <= 0.5f
            ? 2f * baseC * blendC
            : 1f - 2f * (1f - baseC) * (1f - blendC),
        BlendMode.Difference => System.Math.Abs(baseC - blendC),
        BlendMode.Exclusion => baseC + blendC - 2f * baseC * blendC,
        BlendMode.LinearBurn => System.MathF.Max(0f, baseC + blendC - 1f),
        BlendMode.LinearDodge => System.MathF.Min(1f, baseC + blendC),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Color/Luminosity are non-separable; use Blend.Apply()."),
    };

    /// <summary>
    /// Photoshop Soft Light (per channel):
    /// <c>f &lt;= 0.5 ? b - (1 - 2f) * b * (1 - b) : b + (2f - 1) * (D(b) - b)</c>
    /// where <c>D(b) = b &lt;= 0.25 ? (16b - 12)b + 4b ... = ((16b - 12) * b + 4) * b : sqrt(b)</c>.
    /// Neutral at f = 0.5; output stays in 0..1.
    /// </summary>
    private static float SoftLight(float b, float f)
    {
        if (f <= 0.5f)
            return b - (1f - 2f * f) * b * (1f - b);
        float d = b <= 0.25f
            ? ((16f * b - 12f) * b + 4f) * b
            : System.MathF.Sqrt(b);
        return b + (2f * f - 1f) * (d - b);
    }

    private static void BlendSeparable(float[] b, float[] f, float[] o, BlendMode mode, float op)
    {
        float invOp = 1f - op;
        for (int i = 0; i < o.Length; i += 4)
        {
            float br = b[i], bg = b[i + 1], bb = b[i + 2], ba = b[i + 3];
            float fr = f[i], fg = f[i + 1], fb = f[i + 2], fa = f[i + 3];
            float aComp = fa + ba * (1f - fa);
            o[i] = br * invOp + Channel(br, fr, mode) * op;
            o[i + 1] = bg * invOp + Channel(bg, fg, mode) * op;
            o[i + 2] = bb * invOp + Channel(bb, fb, mode) * op;
            o[i + 3] = ba * invOp + aComp * op;
        }
    }

    private static void BlendNonSeparable(float[] b, float[] f, float[] o, BlendMode mode, float op)
    {
        float invOp = 1f - op;
        for (int i = 0; i < o.Length; i += 4)
        {
            float br = b[i], bg = b[i + 1], bb = b[i + 2], ba = b[i + 3];
            float fr = f[i], fg = f[i + 1], fb = f[i + 2], fa = f[i + 3];

            float cr, cg, cb;
            if (mode == BlendMode.Color)
            {
                // Blend's hue/saturation over base's lightness.
                var (bh, bs, _) = Hsl.FromRgb(fr, fg, fb);
                var (_, _, bl) = Hsl.FromRgb(br, bg, bb);
                (cr, cg, cb) = Hsl.ToRgb(bh, bs, bl);
            }
            else // Luminosity: base's hue/saturation with blend's lightness.
            {
                var (bh, bs, _) = Hsl.FromRgb(br, bg, bb);
                var (_, _, bl) = Hsl.FromRgb(fr, fg, fb);
                (cr, cg, cb) = Hsl.ToRgb(bh, bs, bl);
            }

            float aComp = fa + ba * (1f - fa);
            o[i] = br * invOp + cr * op;
            o[i + 1] = bg * invOp + cg * op;
            o[i + 2] = bb * invOp + cb * op;
            o[i + 3] = ba * invOp + aComp * op;
        }
    }
}

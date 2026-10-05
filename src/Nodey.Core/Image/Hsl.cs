namespace Nodey.Core.Image;

/// <summary>
/// HSL (hue, saturation, lightness) conversions used by the non-separable
/// blend modes (<see cref="BlendMode.Color"/> and
/// <see cref="BlendMode.Luminosity"/>).
/// </summary>
/// <remarks>
/// Conventions: <c>r, g, b</c> in 0..1; <c>h</c> in [0, 1) (fraction of the
/// color wheel, 0 = red); <c>s, l</c> in 0..1. Lightness is the HSL
/// lightness <c>(max + min) / 2</c>. This is the "via HSL" option the STEP-2
/// spec allows for Color/Luminosity (the alternative being YIQ).
/// </remarks>
public static class Hsl
{
    /// <summary>Converts RGB (0..1) to HSL with h in [0, 1).</summary>
    public static (float H, float S, float L) FromRgb(float r, float g, float b)
    {
        float max = System.MathF.Max(r, System.MathF.Max(g, b));
        float min = System.MathF.Min(r, System.MathF.Min(g, b));
        float l = (max + min) * 0.5f;

        float d = max - min;
        if (d < 1e-7f)
        {
            // Achromatic: hue and saturation are undefined; return 0/0.
            return (0f, 0f, l);
        }

        float s = l > 0.5f ? d / (2f - max - min) : d / (max + min);

        float h;
        if (System.Math.Abs(max - r) < 1e-7f)
            h = (g - b) / d;
        else if (System.Math.Abs(max - g) < 1e-7f)
            h = 2f + (b - r) / d;
        else
            h = 4f + (r - g) / d;

        h *= 1f / 6f;
        if (h < 0f) h += 1f;
        else if (h >= 1f) h -= 1f;

        return (h, s, l);
    }

    /// <summary>Converts HSL (h in [0, 1), s and l in 0..1) to RGB (0..1).</summary>
    public static (float R, float G, float B) ToRgb(float h, float s, float l)
    {
        if (s < 1e-7f)
            return (l, l, l);

        float hh = h - System.MathF.Floor(h); // [0, 1)
        float hp = hh * 6f;                  // [0, 6)
        float c = (1f - System.MathF.Abs(2f * l - 1f)) * s;
        float x = c * (1f - System.MathF.Abs(hp % 2f - 1f));
        float m = l - c * 0.5f;

        float r, g, b;
        switch ((int)hp)
        {
            case 0: (r, g, b) = (c, x, 0f); break;
            case 1: (r, g, b) = (x, c, 0f); break;
            case 2: (r, g, b) = (0f, c, x); break;
            case 3: (r, g, b) = (0f, x, c); break;
            case 4: (r, g, b) = (x, 0f, c); break;
            default: (r, g, b) = (c, 0f, x); break; // case 5
        }

        return (r + m, g + m, b + m);
    }
}

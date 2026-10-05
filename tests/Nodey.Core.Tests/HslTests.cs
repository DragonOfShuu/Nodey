using Nodey.Core.Image;

namespace Nodey.Core.Tests;

public class HslTests
{
    [Fact]
    public void AchromaticValuesHaveZeroSaturationAndCorrectLightness()
    {
        var (h, s, l) = Hsl.FromRgb(0.5f, 0.5f, 0.5f);
        Approx.Near(s, 0f);
        Approx.Near(l, 0.5f);
        Assert.Equal(0f, h);

        var (_, s0, l0) = Hsl.FromRgb(0f, 0f, 0f);
        Approx.Near(s0, 0f);
        Approx.Near(l0, 0f);

        var (_, s1, l1) = Hsl.FromRgb(1f, 1f, 1f);
        Approx.Near(s1, 0f);
        Approx.Near(l1, 1f);
    }

    [Fact]
    public void PrimaryHuesRoundTrip()
    {
        // (r, g, b) -> expected hue in [0,1)
        var primaries = new (float R, float G, float B, float Hue)[]
        {
            (1f, 0f, 0f, 0f),
            (0f, 1f, 0f, 1f / 3f),
            (0f, 0f, 1f, 2f / 3f),
        };
        foreach ((float r, float g, float b, float hue) in primaries)
        {
            var (h, s, l) = Hsl.FromRgb(r, g, b);
            Approx.Near(h, hue);
            Approx.Near(s, 1f);
            Approx.Near(l, 0.5f);

            // Round trip back to RGB.
            var (rr, gg, bb) = Hsl.ToRgb(h, s, l);
            Approx.Near(rr, r);
            Approx.Near(gg, g);
            Approx.Near(bb, b);
        }
    }

    [Fact]
    public void RoundTripPreservesColor()
    {
        float[] samples =
        {
            0.2f, 0.4f, 0.6f,
            0.9f, 0.1f, 0.3f,
            0.1f, 0.8f, 0.5f,
            0.35f, 0.35f, 0.7f,
        };
        for (int i = 0; i < samples.Length; i += 3)
        {
            float r = samples[i], g = samples[i + 1], b = samples[i + 2];
            var (h, s, l) = Hsl.FromRgb(r, g, b);
            var (rr, gg, bb) = Hsl.ToRgb(h, s, l);
            Approx.Near(rr, r);
            Approx.Near(gg, g);
            Approx.Near(bb, b);
        }
    }

    [Fact]
    public void ToRgbHandlesEdgeLightnessAndHueWrapping()
    {
        // Pure hue at l = 0.5; h just below 1 must wrap to red-ish.
        var (rr, gg, bb) = Hsl.ToRgb(0.999f, 1f, 0.5f);
        Assert.True(rr > 0.9f && gg < 0.1f);

        // l = 0 and l = 1 are black/white regardless of h/s.
        var (r0, g0, b0) = Hsl.ToRgb(0.3f, 1f, 0f);
        Assert.Equal(0f, r0);
        Assert.Equal(0f, g0);
        Assert.Equal(0f, b0);
        var (r1, g1, b1) = Hsl.ToRgb(0.3f, 1f, 1f);
        Approx.Near(r1, 1f);
        Approx.Near(g1, 1f);
        Approx.Near(b1, 1f);
    }
}

using Nodey.Core.Image;

namespace Nodey.Core.Tests;

public class BlendModesTests
{
    private static readonly float[] OpaqueRed = { 1f, 0f, 0f, 1f };
    private static readonly float[] OpaqueWhite = { 1f, 1f, 1f, 1f };
    private static readonly float[] OpaqueBlack = { 0f, 0f, 0f, 1f };
    private static readonly float[] OpaqueGray = { 0.5f, 0.5f, 0.5f, 1f };

    private static PixelBuffer OnePixel(float[] rgba)
    {
        var buffer = new PixelBuffer(1, 1);
        buffer.SetPixel(0, 0, new Rgba(rgba[0], rgba[1], rgba[2], rgba[3]));
        return buffer;
    }

    [Theory]
    [InlineData(BlendMode.Multiply, 1f)]     // white is identity (b * 1 = b)
    [InlineData(BlendMode.Screen, 0f)]       // black is identity (b + 0 - b*0 = b)
    [InlineData(BlendMode.Overlay, 0.5f)]    // gray is neutral
    [InlineData(BlendMode.HardLight, 0.5f)]  // gray is neutral
    [InlineData(BlendMode.SoftLight, 0.5f)]  // gray is neutral
    [InlineData(BlendMode.LinearDodge, 0f)]  // black is identity
    [InlineData(BlendMode.LinearBurn, 1f)]   // white is identity (b + 1 - 1 = b)
    [InlineData(BlendMode.Difference, 0f)]   // black is identity
    [InlineData(BlendMode.Exclusion, 0f)]    // black is identity
    public void NeutralBlendLeavesBaseUnchanged(BlendMode mode, float blendC)
    {
        // The identity blend value must leave the base unchanged for several
        // base values, not just one.
        foreach (float baseC in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            PixelBuffer baseImg = OnePixel(new[] { baseC, baseC, baseC, 1f });
            PixelBuffer blendImg = OnePixel(new[] { blendC, blendC, blendC, 1f });
            PixelBuffer result = Blend.Apply(baseImg, blendImg, mode, 1f);
            Rgba p = result.GetPixel(0, 0);
            Approx.Near(p.R, baseC);
            Approx.Near(p.G, baseC);
            Approx.Near(p.B, baseC);
            Approx.Near(p.A, 1f);
        }
    }

    [Fact]
    public void SelfBlendsMatchKnownValues()
    {
        // Multiply(self) = c^2
        PixelBuffer gray = OnePixel(OpaqueGray);
        PixelBuffer result = Blend.Apply(gray, gray, BlendMode.Multiply, 1f);
        Approx.Near(result.GetPixel(0, 0).R, 0.25f);

        // Screen(self) = 2c - c^2
        result = Blend.Apply(gray, gray, BlendMode.Screen, 1f);
        Approx.Near(result.GetPixel(0, 0).R, 0.75f);

        // Difference(self) = 0
        result = Blend.Apply(gray, gray, BlendMode.Difference, 1f);
        Approx.Near(result.GetPixel(0, 0).R, 0f);

        // Exclusion(self) = 2c(1-c)
        result = Blend.Apply(gray, gray, BlendMode.Exclusion, 1f);
        Approx.Near(result.GetPixel(0, 0).R, 0.5f);

        // LinearDodge(self) = min(1, 2c)
        result = Blend.Apply(gray, gray, BlendMode.LinearDodge, 1f);
        Approx.Near(result.GetPixel(0, 0).R, 1f);

        // LinearBurn(self) = max(0, 2c - 1)
        result = Blend.Apply(gray, gray, BlendMode.LinearBurn, 1f);
        Approx.Near(result.GetPixel(0, 0).R, 0f);

        // Overlay(self) on the bright side: 1 - 2(1-c)^2. Use c = 0.75
        // (the formula is discontinuous exactly at c = 0.5).
        PixelBuffer bright = OnePixel(new[] { 0.75f, 0.75f, 0.75f, 1f });
        result = Blend.Apply(bright, bright, BlendMode.Overlay, 1f);
        Approx.Near(result.GetPixel(0, 0).R, 0.875f);
    }

    [Fact]
    public void OverlayAndHardLightAreRoleSwapped()
    {
        // Overlay(b, f) == HardLight(f, b) by construction.
        PixelBuffer baseImg = OnePixel(new[] { 0.2f, 0.4f, 0.6f, 1f });
        PixelBuffer blendImg = OnePixel(new[] { 0.8f, 0.3f, 0.5f, 1f });
        PixelBuffer a = Blend.Apply(baseImg, blendImg, BlendMode.Overlay, 1f);
        PixelBuffer b = Blend.Apply(blendImg, baseImg, BlendMode.HardLight, 1f);
        Approx.NearAll(a.Rgba, b.Rgba);
    }

    [Fact]
    public void SoftLightIsNeutralAtHalfAndStaysInRange()
    {
        // f = 0.5 -> base unchanged, across the whole range.
        for (int i = 0; i <= 10; i++)
        {
            float c = i / 10f;
            PixelBuffer baseImg = OnePixel(new[] { c, c, c, 1f });
            PixelBuffer blendImg = OnePixel(OpaqueGray);
            PixelBuffer result = Blend.Apply(baseImg, blendImg, BlendMode.SoftLight, 1f);
            Approx.Near(result.GetPixel(0, 0).R, c);
        }
        // Full scan: output must stay in [0, 1].
        for (int i = 0; i <= 20; i++)
        {
            float c = i / 20f;
            for (int j = 0; j <= 20; j++)
            {
                float f = j / 20f;
                float v = Blend.Channel(c, f, BlendMode.SoftLight);
                Assert.InRange(v, 0f, 1f);
            }
        }
    }

    [Fact]
    public void DifferenceAndExclusionAreSymmetric()
    {
        for (int i = 0; i <= 10; i++)
        {
            float a = i / 10f;
            float b = (10 - i) / 10f;
            Approx.Near(Blend.Channel(a, b, BlendMode.Difference), Blend.Channel(b, a, BlendMode.Difference));
            Approx.Near(Blend.Channel(a, b, BlendMode.Exclusion), Blend.Channel(b, a, BlendMode.Exclusion));
        }
    }

    [Fact]
    public void OpacityZeroReturnsBaseAndOneAppliesFully()
    {
        // Multiply(red, blue) = black, so the three opacity levels give
        // distinct, hand-computable results.
        PixelBuffer baseImg = OnePixel(OpaqueRed);
        PixelBuffer blendImg = OnePixel(new[] { 0f, 0f, 1f, 1f });

        PixelBuffer zero = Blend.Apply(baseImg, blendImg, BlendMode.Multiply, 0f);
        Approx.NearAll(zero.Rgba, OpaqueRed);

        PixelBuffer full = Blend.Apply(baseImg, blendImg, BlendMode.Multiply, 1f);
        Approx.NearAll(full.Rgba, OpaqueBlack);

        // Half opacity: out.r = 1*0.5 + 0*0.5 = 0.5; out.a = 1 (both opaque).
        PixelBuffer half = Blend.Apply(baseImg, blendImg, BlendMode.Multiply, 0.5f);
        Rgba p = half.GetPixel(0, 0);
        Approx.Near(p.R, 0.5f);
        Approx.Near(p.G, 0f);
        Approx.Near(p.B, 0f);
        Approx.Near(p.A, 1f);
    }

    [Fact]
    public void AlphaUsesStraightCompositingThenOpacityMix()
    {
        // out.a = ba * (1 - op) + (fa + ba * (1 - fa)) * op.
        // With op = 1, ba = 0.2, fa = 0.5: out.a = 0.5 + 0.2 * 0.5 = 0.6.
        PixelBuffer baseImg = OnePixel(new[] { 1f, 0f, 0f, 0.2f });
        PixelBuffer blendImg = OnePixel(new[] { 1f, 1f, 1f, 0.5f });
        PixelBuffer result = Blend.Apply(baseImg, blendImg, BlendMode.Normal, 1f);
        Rgba p = result.GetPixel(0, 0);
        Approx.Near(p.A, 0.6f);
        // Normal mode takes the blend's RGB (1, 1, 1) fully at opacity 1.
        Approx.Near(p.R, 1f);
        Approx.Near(p.G, 1f);
        Approx.Near(p.B, 1f);

        // With op = 0 the base alpha passes through untouched.
        PixelBuffer untouched = Blend.Apply(baseImg, blendImg, BlendMode.Normal, 0f);
        Approx.Near(untouched.GetPixel(0, 0).A, 0.2f);
    }

    [Fact]
    public void ColorModeTakesHueSaturationFromBlendAndLightnessFromBase()
    {
        // Base: light gray (l = 0.8, achromatic). Blend: saturated red.
        // Result must keep the base lightness (0.8) and the blend's hue/sat.
        float l = 0.8f;
        PixelBuffer baseImg = OnePixel(new[] { l, l, l, 1f });
        PixelBuffer blendImg = OnePixel(new[] { 1f, 0f, 0f, 1f });
        PixelBuffer result = Blend.Apply(baseImg, blendImg, BlendMode.Color, 1f);
        Rgba p = result.GetPixel(0, 0);
        var (h, s, outL) = Hsl.FromRgb(p.R, p.G, p.B);
        Approx.Near(outL, l);
        Approx.Near(s, 1f);
        Approx.Near(h, 0f);
    }

    [Fact]
    public void LuminosityModeTakesLightnessFromBlendAndHueSaturationFromBase()
    {
        // Base: saturated blue. Blend: light gray (l = 0.8).
        float l = 0.8f;
        PixelBuffer baseImg = OnePixel(new[] { 0f, 0f, 1f, 1f });
        PixelBuffer blendImg = OnePixel(new[] { l, l, l, 1f });
        PixelBuffer result = Blend.Apply(baseImg, blendImg, BlendMode.Luminosity, 1f);
        Rgba p = result.GetPixel(0, 0);
        var (h, s, outL) = Hsl.FromRgb(p.R, p.G, p.B);
        Approx.Near(outL, l);
        Approx.Near(s, 1f);
        Approx.Near(h, 2f / 3f);
    }

    [Fact]
    public void ColorAndLuminosityRoundTripPreservesBoth()
    {
        // Color(base, blend) then Luminosity(result, base) should restore the
        // base's lightness with the blend's hue/saturation.
        PixelBuffer baseImg = OnePixel(new[] { 0.3f, 0.7f, 0.4f, 1f });
        PixelBuffer blendImg = OnePixel(new[] { 0.9f, 0.2f, 0.6f, 1f });
        PixelBuffer colored = Blend.Apply(baseImg, blendImg, BlendMode.Color, 1f);
        PixelBuffer restored = Blend.Apply(colored, baseImg, BlendMode.Luminosity, 1f);
        Rgba p = restored.GetPixel(0, 0);
        var (_, _, baseL) = Hsl.FromRgb(0.3f, 0.7f, 0.4f);
        var (_, _, outL) = Hsl.FromRgb(p.R, p.G, p.B);
        Approx.Near(outL, baseL);
    }

    [Fact]
    public void AllModesProduceUnitRangeOutputOnFixedInputs()
    {
        // Fixed "random-ish" inputs: a deterministic pseudo-random RGBA field
        // (opaque, so alpha compositing stays well-defined).
        var baseImg = new PixelBuffer(8, 8);
        var blendImg = new PixelBuffer(8, 8);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                baseImg.SetPixel(x, y, new Rgba(
                    0.1f + 0.8f * ((x * 7 + y * 13) % 10) / 10f,
                    0.1f + 0.8f * ((x * 11 + y * 3) % 10) / 10f,
                    0.1f + 0.8f * ((x * 5 + y * 17) % 10) / 10f, 1f));
                blendImg.SetPixel(x, y, new Rgba(
                    0.1f + 0.8f * ((x * 3 + y * 7) % 10) / 10f,
                    0.1f + 0.8f * ((x * 17 + y * 11) % 10) / 10f,
                    0.1f + 0.8f * ((x * 13 + y * 5) % 10) / 10f, 1f));
            }
        }

        foreach (BlendMode mode in System.Enum.GetValues<BlendMode>())
        {
            PixelBuffer result = Blend.Apply(baseImg, blendImg, mode, 1f);
            foreach (float v in result.Rgba)
                Assert.InRange(v, 0f, 1f);
        }
    }

    [Fact]
    public void MultiplyByBlackIsBlackAndScreenByWhiteIsWhite()
    {
        PixelBuffer gray = OnePixel(OpaqueGray);
        PixelBuffer black = OnePixel(OpaqueBlack);
        PixelBuffer white = OnePixel(OpaqueWhite);

        PixelBuffer mult = Blend.Apply(gray, black, BlendMode.Multiply, 1f);
        Approx.Near(mult.GetPixel(0, 0).R, 0f);

        PixelBuffer screen = Blend.Apply(gray, white, BlendMode.Screen, 1f);
        Approx.Near(screen.GetPixel(0, 0).R, 1f);
    }

    [Fact]
    public void BlendRejectsMismatchedSizes()
    {
        PixelBuffer a = new(2, 2);
        PixelBuffer b = new(3, 2);
        Assert.Throws<ArgumentException>(() => Blend.Apply(a, b, BlendMode.Multiply, 1f));
    }
}

using Nodey.Core.Image;

namespace Nodey.Core.Tests;

public class ColorRampTests
{
    private static ColorRamp TwoStop() => new(
    [
        new RampStop(0f, 0f, 0f, 0f, 1f),
        new RampStop(1f, 1f, 1f, 1f, 1f),
    ]);

    [Fact]
    public void EndpointsEvaluateExactly()
    {
        ColorRamp ramp = TwoStop();
        Rgba at0 = ramp.Evaluate(0f);
        Approx.NearAll(new[] { at0.R, at0.G, at0.B, at0.A }, new[] { 0f, 0f, 0f, 1f });
        Rgba at1 = ramp.Evaluate(1f);
        Approx.NearAll(new[] { at1.R, at1.G, at1.B, at1.A }, new[] { 1f, 1f, 1f, 1f });
    }

    [Fact]
    public void MidpointInterpolatesLinearly()
    {
        ColorRamp ramp = TwoStop();
        Rgba mid = ramp.Evaluate(0.5f);
        Approx.NearAll(new[] { mid.R, mid.G, mid.B, mid.A }, new[] { 0.5f, 0.5f, 0.5f, 1f });
    }

    [Fact]
    public void OutOfRangeClampsToEndpoints()
    {
        ColorRamp ramp = TwoStop();
        Approx.NearAll(new[] { ramp.Evaluate(-0.5f).R, ramp.Evaluate(-0.5f).G }, new[] { 0f, 0f });
        Approx.NearAll(new[] { ramp.Evaluate(1.5f).R, ramp.Evaluate(1.5f).G }, new[] { 1f, 1f });
    }

    [Fact]
    public void StopsAreSortedRegardlessOfInputOrder()
    {
        ColorRamp ramp = new(
        [
            new RampStop(1f, 1f, 0f, 0f, 1f),   // red at 1
            new RampStop(0f, 0f, 0f, 1f, 1f),   // blue at 0
        ]);
        Approx.Near(ramp.Evaluate(0f).B, 1f);
        Approx.Near(ramp.Evaluate(0f).R, 0f);
        Approx.Near(ramp.Evaluate(1f).R, 1f);
        Approx.Near(ramp.Evaluate(1f).B, 0f);
    }

    [Fact]
    public void CtorRejectsFewerThanTwoStops()
    {
        Assert.Throws<ArgumentException>(() => new ColorRamp(
        [
            new RampStop(0f, 0f, 0f, 0f, 1f),
        ]));
        Assert.Throws<ArgumentNullException>(() => new ColorRamp(null!));
    }

    [Fact]
    public void RampStopRejectsBadPosition()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RampStop(-0.1f, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RampStop(1.1f, 0, 0, 0));
    }

    [Fact]
    public void DefaultRampIsBlackToWhite()
    {
        Approx.Near(ColorRamp.Default.Evaluate(0f).R, 0f);
        Approx.Near(ColorRamp.Default.Evaluate(1f).R, 1f);
        Approx.Near(ColorRamp.Default.Evaluate(0.25f).R, 0.25f);
    }

    [Fact]
    public void ApplyMapsScalarFieldThroughTheRamp()
    {
        // Scalar2D field: R = x/3 across a 4x1 row.
        var field = new PixelBuffer(4, 1);
        for (int x = 0; x < 4; x++)
        {
            float v = x / 3f;
            field.SetPixel(x, 0, new Rgba(v, 0f, 0f, 1f));
        }
        PixelBuffer colors = TwoStop().Apply(field);
        for (int x = 0; x < 4; x++)
        {
            Rgba c = colors.GetPixel(x, 0);
            float expected = x / 3f;
            Approx.Near(c.R, expected);
            Approx.Near(c.G, expected);
            Approx.Near(c.B, expected);
            Approx.Near(c.A, 1f);
        }
    }
}

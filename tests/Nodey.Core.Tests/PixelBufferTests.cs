using Nodey.Core.Image;

namespace Nodey.Core.Tests;

public class PixelBufferTests
{
    [Fact]
    public void CtorRejectsNonPositiveDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(4, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(-1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelBuffer(4, -2));
    }

    [Fact]
    public void NewBufferIsAllZero()
    {
        var buffer = new PixelBuffer(3, 2);
        foreach (float v in buffer.Rgba)
            Assert.Equal(0f, v);
    }

    [Fact]
    public void FillSetsEveryChannel()
    {
        var buffer = new PixelBuffer(4, 3);
        buffer.Fill(0.25f, 0.5f, 0.75f, 0.9f);
        for (int i = 0; i < buffer.Rgba.Length; i += 4)
        {
            Approx.Near(buffer.Rgba[i], 0.25f);
            Approx.Near(buffer.Rgba[i + 1], 0.5f);
            Approx.Near(buffer.Rgba[i + 2], 0.75f);
            Approx.Near(buffer.Rgba[i + 3], 0.9f);
        }
    }

    [Fact]
    public void SetGetPixelRoundTrips()
    {
        var buffer = new PixelBuffer(2, 2);
        buffer.SetPixel(1, 0, new Rgba(0.1f, 0.2f, 0.3f, 0.4f));
        Rgba p = buffer.GetPixel(1, 0);
        Approx.Near(p.R, 0.1f);
        Approx.Near(p.G, 0.2f);
        Approx.Near(p.B, 0.3f);
        Approx.Near(p.A, 0.4f);
    }

    [Fact]
    public void PixelAccessEnforcesBounds()
    {
        var buffer = new PixelBuffer(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetPixel(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetPixel(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetPixel(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.SetPixel(0, -1, new Rgba(1, 1, 1, 1)));
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var buffer = new PixelBuffer(2, 2);
        buffer.Fill(1f, 0f, 0f);
        PixelBuffer copy = buffer.Clone();
        buffer.Fill(0f, 1f, 0f);
        Approx.Near(copy.GetPixel(0, 0).R, 1f);
        Approx.Near(copy.GetPixel(0, 0).G, 0f);
    }

    [Fact]
    public void Rgba8RoundTripsWithinOneStep()
    {
        var buffer = new PixelBuffer(3, 2);
        buffer.Fill(0.123f, 0.456f, 0.789f, 0.999f);
        byte[] bytes = buffer.ToRgba8();
        Assert.Equal(3 * 2 * 4, bytes.Length);

        PixelBuffer back = PixelBuffer.FromRgba8(bytes, 3, 2);
        Approx.Near(back.GetPixel(1, 1).R, 0.123f, 1f / 255f);
        Approx.Near(back.GetPixel(1, 1).G, 0.456f, 1f / 255f);
        Approx.Near(back.GetPixel(1, 1).B, 0.789f, 1f / 255f);
        Approx.Near(back.GetPixel(1, 1).A, 0.999f, 1f / 255f);
    }

    [Fact]
    public void ToRgba8ClampsOutOfRange()
    {
        var buffer = new PixelBuffer(1, 1);
        buffer.SetPixel(0, 0, new Rgba(-0.5f, 2f, 0f, 1f));
        byte[] bytes = buffer.ToRgba8();
        Assert.Equal(0, bytes[0]);
        Assert.Equal(255, bytes[1]);
        Assert.Equal(0, bytes[2]);
        Assert.Equal(255, bytes[3]);
    }

    [Fact]
    public void FromRgba8RejectsWrongLength()
    {
        byte[] data = new byte[8]; // not enough for 2x2
        Assert.Throws<ArgumentException>(() => PixelBuffer.FromRgba8(data, 2, 2));
    }

    [Fact]
    public void LuminanceUsesBt709Weights()
    {
        var buffer = new PixelBuffer(1, 1);
        buffer.SetPixel(0, 0, new Rgba(1f, 0f, 0f, 1f));
        Approx.Near(buffer.Luminance(0, 0), 0.2126f);
        buffer.SetPixel(0, 0, new Rgba(1f, 1f, 1f, 1f));
        Approx.Near(buffer.Luminance(0, 0), 1f);
        buffer.SetPixel(0, 0, new Rgba(0f, 1f, 0f, 1f));
        Approx.Near(buffer.Luminance(0, 0), 0.7152f);
    }

    [Fact]
    public void ScaleToSameSizeIsIdentity()
    {
        var buffer = new PixelBuffer(3, 2);
        buffer.SetPixel(0, 0, new Rgba(0.1f, 0.2f, 0.3f, 0.4f));
        buffer.SetPixel(2, 1, new Rgba(0.5f, 0.6f, 0.7f, 0.8f));
        PixelBuffer scaled = buffer.ScaleTo(3, 2);
        Approx.NearAll(scaled.Rgba, buffer.Rgba);
    }

    [Fact]
    public void ScaleToUpscalePreservesCornersAndInterpolatesCenter()
    {
        // 2x2 corner colors: red, green / blue, white.
        var src = new PixelBuffer(2, 2);
        src.SetPixel(0, 0, new Rgba(1f, 0f, 0f, 1f));
        src.SetPixel(1, 0, new Rgba(0f, 1f, 0f, 1f));
        src.SetPixel(0, 1, new Rgba(0f, 0f, 1f, 1f));
        src.SetPixel(1, 1, new Rgba(1f, 1f, 1f, 1f));

        PixelBuffer dst = src.ScaleTo(4, 4);
        Assert.Equal((4, 4), (dst.Width, dst.Height));

        // Corners land exactly on source corners.
        Approx.NearAll(new[] { dst.Rgba[0], dst.Rgba[1], dst.Rgba[2] }, new[] { 1f, 0f, 0f });
        int last = (3 * 4 + 3) * 4;
        Approx.NearAll(new[] { dst.Rgba[last], dst.Rgba[last + 1], dst.Rgba[last + 2] }, new[] { 1f, 1f, 1f });

        // Pixel (1,1) samples at (0.25, 0.25): bilinear of the four corners.
        Rgba p = dst.GetPixel(1, 1);
        Approx.Near(p.R, 0.625f);
        Approx.Near(p.G, 0.25f);
        Approx.Near(p.B, 0.25f);
        Approx.Near(p.A, 1f);
    }

    [Fact]
    public void ScaleToDownscaleAveragesNeighbors()
    {
        // 4x4: left half red, right half blue, opaque.
        var src = new PixelBuffer(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                src.SetPixel(x, y, x < 2 ? new Rgba(1f, 0f, 0f) : new Rgba(0f, 0f, 1f));

        PixelBuffer dst = src.ScaleTo(2, 2);
        // dst(0,0) samples at x = 0.5 -> average of source columns 0,1 (red).
        Approx.NearAll(new[] { dst.Rgba[0], dst.Rgba[1], dst.Rgba[2] }, new[] { 1f, 0f, 0f });
        // dst(1,0) samples at x = 2.5 -> average of source columns 2,3 (blue).
        int i = 1 * 4; // pixel (1,0)
        Approx.NearAll(new[] { dst.Rgba[i], dst.Rgba[i + 1], dst.Rgba[i + 2] }, new[] { 0f, 0f, 1f });
    }

    [Fact]
    public void ScaleToInterpolatesAlphaToo()
    {
        var src = new PixelBuffer(2, 1);
        src.SetPixel(0, 0, new Rgba(1f, 0f, 0f, 1f));
        src.SetPixel(1, 0, new Rgba(0f, 0f, 0f, 0f));
        PixelBuffer dst = src.ScaleTo(3, 1);
        // Middle pixel samples at x = 0.5: alpha should be ~0.5.
        Approx.Near(dst.GetPixel(1, 0).A, 0.5f);
    }

    [Fact]
    public void ScaleToRejectsNonPositiveTargets()
    {
        var buffer = new PixelBuffer(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ScaleTo(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ScaleTo(2, 0));
    }

    [Fact]
    public void SolidAndEmptyFactories()
    {
        PixelBuffer solid = PixelBuffer.Solid(2, 2, 0.5f, 0.6f, 0.7f, 0.8f);
        Approx.NearAll(solid.Rgba, new float[] { 0.5f, 0.6f, 0.7f, 0.8f, 0.5f, 0.6f, 0.7f, 0.8f, 0.5f, 0.6f, 0.7f, 0.8f, 0.5f, 0.6f, 0.7f, 0.8f });
        PixelBuffer empty = PixelBuffer.Empty(2, 2);
        foreach (float v in empty.Rgba)
            Assert.Equal(0f, v);
    }

    [Fact]
    public void SameSizeAsChecksDimensions()
    {
        var a = new PixelBuffer(2, 3);
        Assert.True(a.SameSizeAs(new PixelBuffer(2, 3)));
        Assert.False(a.SameSizeAs(new PixelBuffer(3, 2)));
        Assert.False(a.SameSizeAs(null));
    }
}

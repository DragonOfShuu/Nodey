using Nodey.Core.Image;
using Nodey.Core.Image.Filters;

namespace Nodey.Core.Tests;

public class FiltersTests
{
    [Fact]
    public void BlurOfSolidIsTheSameSolid()
    {
        PixelBuffer solid = PixelBuffer.Solid(8, 8, 0.3f, 0.5f, 0.7f, 1f);
        foreach (FilterKind kind in new[] { FilterKind.GaussianBlur, FilterKind.BoxBlur, FilterKind.LensBlur })
        {
            PixelBuffer result = Filter.Apply(solid, new FilterParams(kind, Radius: 3));
            for (int i = 0; i < result.Rgba.Length; i++)
                Approx.Near(result.Rgba[i], solid.Rgba[i]);
        }
    }

    [Fact]
    public void BlurOfDotSpreadsAndPreservesCenterWeighting()
    {
        var src = new PixelBuffer(5, 5);
        src.SetPixel(2, 2, new Rgba(1f, 0f, 0f, 1f)); // red dot, alpha 1
        // Note: the dot's neighbors are alpha 0; blur averages all channels.

        PixelBuffer gaussian = Filter.Apply(src, new FilterParams(FilterKind.GaussianBlur, Radius: 1));
        // Center keeps most of the weight; orthogonal neighbors get less;
        // diagonals less still (separable product).
        float center = gaussian.GetPixel(2, 2).R;
        float ortho = gaussian.GetPixel(3, 2).R;
        float diag = gaussian.GetPixel(3, 3).R;
        Assert.True(center > ortho, $"center {center} should exceed orthogonal {ortho}");
        Assert.True(ortho > diag, $"orthogonal {ortho} should exceed diagonal {diag}");

        PixelBuffer box = Filter.Apply(src, new FilterParams(FilterKind.BoxBlur, Radius: 1));
        // Box: center = 1/9, orthogonal = 1/9 too? No: box radius 1 window is
        // 3x3; the dot contributes 1/9 to all 9 covered pixels.
        Approx.Near(box.GetPixel(2, 2).R, 1f / 9f);
        Approx.Near(box.GetPixel(3, 2).R, 1f / 9f);
        Approx.Near(box.GetPixel(1, 1).R, 1f / 9f);
        Approx.Near(box.GetPixel(0, 0).R, 0f);
    }

    [Fact]
    public void LensBlurWithApertureBlursMoreThanPlainGaussian()
    {
        var src = new PixelBuffer(9, 9);
        src.SetPixel(4, 4, new Rgba(1f, 0f, 0f, 1f));

        PixelBuffer plain = Filter.Apply(src, new FilterParams(FilterKind.LensBlur, Radius: 2, Aperture: 1f));
        PixelBuffer wide = Filter.Apply(src, new FilterParams(FilterKind.LensBlur, Radius: 2, Aperture: 2f));
        // Aperture 2 doubles the effective Radius: the far pixel (4,8) should
        // be brighter under the wider blur.
        Assert.True(wide.GetPixel(4, 8).R > plain.GetPixel(4, 8).R);
        // And the center should be dimmer (energy spread wider).
        Assert.True(wide.GetPixel(4, 4).R < plain.GetPixel(4, 4).R);
    }

    [Fact]
    public void SharpenIncreasesContrastAroundEdges()
    {
        // A step edge: left half 0.5, right half 0.5 + a bump.
        var src = new PixelBuffer(9, 1);
        for (int x = 0; x < 9; x++)
            src.SetPixel(x, 0, new Rgba(0.5f, 0f, 0f, 1f));
        src.SetPixel(4, 0, new Rgba(0.8f, 0f, 0f, 1f));

        PixelBuffer sharpened = Filter.Apply(src, new FilterParams(FilterKind.Sharpen, Amount: 1f, Radius: 1));
        // The bump should be higher after sharpening.
        Assert.True(sharpened.GetPixel(4, 0).R > 0.8f);
        // And clamped to 1.
        foreach (float v in sharpened.Rgba)
            Assert.InRange(v, 0f, 1f);
    }

    [Fact]
    public void SharpenZeroAmountReturnsCopy()
    {
        var src = new PixelBuffer(3, 3);
        src.SetPixel(1, 1, new Rgba(0.5f, 0.5f, 0.5f, 1f));
        PixelBuffer result = Filter.Apply(src, new FilterParams(FilterKind.Sharpen, Amount: 0f, Radius: 2));
        Approx.NearAll(result.Rgba, src.Rgba);
    }

    [Fact]
    public void EmbossOfSolidIsHalfGray()
    {
        PixelBuffer solid = PixelBuffer.Solid(5, 5, 0.2f, 0.6f, 0.9f, 1f);
        PixelBuffer result = Filter.Apply(solid, new FilterParams(FilterKind.Emboss));
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
            {
                Rgba p = result.GetPixel(x, y);
                Approx.Near(p.R, 0.5f);
                Approx.Near(p.G, 0.5f);
                Approx.Near(p.B, 0.5f);
                Approx.Near(p.A, 1f);
            }
    }

    [Fact]
    public void ErosionShrinksADotAndDilationGrowsIt()
    {
        var src = new PixelBuffer(5, 5);
        // A single opaque white pixel on a fully transparent background.
        src.SetPixel(2, 2, new Rgba(1f, 1f, 1f, 1f));

        PixelBuffer eroded = Filter.Apply(src, new FilterParams(FilterKind.Erosion));
        // Erosion: the dot's neighborhood includes transparent pixels, so the
        // min is 0 -> the dot disappears (all transparent).
        for (int i = 0; i < eroded.Rgba.Length; i += 4)
            Assert.Equal(0f, eroded.Rgba[i + 3]);

        PixelBuffer dilated = Filter.Apply(src, new FilterParams(FilterKind.Dilation));
        // Dilation: the 3x3 around the dot becomes opaque white.
        float count = 0;
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                if (dilated.GetPixel(x, y).A > 0.5f)
                    count++;
        Assert.Equal(9f, count);
        // And the grown pixels are white (max luminance of the dot).
        Approx.Near(dilated.GetPixel(2, 2).R, 1f);
        Approx.Near(dilated.GetPixel(3, 3).R, 1f);
    }

    [Fact]
    public void ErosionOfSolidKeepsSolid()
    {
        PixelBuffer solid = PixelBuffer.Solid(5, 5, 0.4f, 0.4f, 0.4f, 1f);
        PixelBuffer result = Filter.Apply(solid, new FilterParams(FilterKind.Erosion));
        for (int i = 0; i < result.Rgba.Length; i += 4)
        {
            Approx.Near(result.Rgba[i], 0.2126f * 0.4f + 0.7152f * 0.4f + 0.0722f * 0.4f);
            Approx.Near(result.Rgba[i + 3], 1f);
        }
    }

    [Fact]
    public void FiltersPreserveSize()
    {
        var src = new PixelBuffer(6, 4);
        foreach (FilterKind kind in new[]
        {
            FilterKind.GaussianBlur, FilterKind.BoxBlur, FilterKind.LensBlur,
            FilterKind.Sharpen, FilterKind.Emboss, FilterKind.Erosion, FilterKind.Dilation,
        })
        {
            PixelBuffer result = Filter.Apply(src, new FilterParams(kind, Amount: 1f, Radius: 2, Aperture: 1f));
            Assert.Equal((6, 4), (result.Width, result.Height));
        }
    }
}

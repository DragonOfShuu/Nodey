using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Tests;

/// <summary>
/// Tests the "foreign nodes" translation table (PLAN.md §5.4).
/// </summary>
public class TypeTranslatorTests
{
    // A 2×2 Scalar2D field with red values [0, 0.5, 0.5, 1] (mean 0.5).
    private static PixelBuffer MakeField()
    {
        var field = new PixelBuffer(2, 2);
        field.SetPixel(0, 0, new Rgba(0f, 0f, 0f, 1f));
        field.SetPixel(1, 0, new Rgba(0.5f, 0f, 0f, 1f));
        field.SetPixel(0, 1, new Rgba(0.5f, 0f, 0f, 1f));
        field.SetPixel(1, 1, new Rgba(1f, 0f, 0f, 1f));
        return field;
    }

    // A 2×2 Color2D image: left column red (1,0,0), right column green (0,1,0), opaque.
    private static PixelBuffer MakeImage()
    {
        var image = new PixelBuffer(2, 2);
        image.SetPixel(0, 0, new Rgba(1f, 0f, 0f, 1f));
        image.SetPixel(1, 0, new Rgba(0f, 1f, 0f, 1f));
        image.SetPixel(0, 1, new Rgba(1f, 0f, 0f, 1f));
        image.SetPixel(1, 1, new Rgba(0f, 1f, 0f, 1f));
        return image;
    }

    [Fact]
    public void Scalar_To_Scalar2D_FillsConstantField()
    {
        PixelBuffer result = TypeTranslator.FromScalar(0.5, SignalType.Scalar2D, 2, 2);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                Rgba p = result.GetPixel(x, y);
                Approx.Near(p.R, 0.5f);
                Approx.Near(p.G, 0f);
                Approx.Near(p.B, 0f);
                Approx.Near(p.A, 1f);
            }
    }

    [Fact]
    public void Scalar_To_Color2D_IsSolidGray()
    {
        PixelBuffer result = TypeTranslator.FromScalar(0.25, SignalType.Color2D, 2, 2);
        Rgba p = result.GetPixel(1, 1);
        Approx.Near(p.R, 0.25f);
        Approx.Near(p.G, 0.25f);
        Approx.Near(p.B, 0.25f);
        Approx.Near(p.A, 1f);
    }

    [Fact]
    public void Scalar2D_To_Scalar_IsMeanRed()
    {
        PixelBuffer field = MakeField();
        PixelBuffer result = TypeTranslator.FromField(field, SignalType.Scalar, 2, 2);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
        Approx.Near(result.GetPixel(0, 0).R, 0.5f);
    }

    [Fact]
    public void Scalar2D_To_Color2D_IsLuminanceImage()
    {
        PixelBuffer field = MakeField();
        PixelBuffer result = TypeTranslator.FromField(field, SignalType.Color2D, 2, 2);
        // (1,1) had red 1.0 -> R=G=B=1
        Rgba p = result.GetPixel(1, 1);
        Approx.Near(p.R, 1f);
        Approx.Near(p.G, 1f);
        Approx.Near(p.B, 1f);
        Approx.Near(p.A, 1f);
        // (0,0) had red 0 -> black
        Rgba q = result.GetPixel(0, 0);
        Approx.Near(q.R, 0f);
        Approx.Near(q.G, 0f);
        Approx.Near(q.B, 0f);
    }

    [Fact]
    public void Color2D_To_Scalar2D_IsPerPixelLuminance()
    {
        PixelBuffer image = MakeImage();
        PixelBuffer result = TypeTranslator.FromImage(image, SignalType.Scalar2D, 2, 2);
        // Red pixel (1,0,0): lum = 0.2126. Green pixel (0,1,0): lum = 0.7152.
        Approx.Near(result.GetPixel(0, 0).R, 0.2126f);
        Approx.Near(result.GetPixel(1, 0).R, 0.7152f);
        // Scalar2D convention: G=B=0, A=1.
        Rgba p = result.GetPixel(0, 0);
        Approx.Near(p.G, 0f);
        Approx.Near(p.B, 0f);
        Approx.Near(p.A, 1f);
    }

    [Fact]
    public void Color2D_To_Scalar_IsMeanLuminance()
    {
        PixelBuffer image = MakeImage();
        // Mean of {0.2126, 0.7152, 0.2126, 0.7152} = 0.4639.
        PixelBuffer result = TypeTranslator.FromImage(image, SignalType.Scalar, 2, 2);
        Approx.Near(result.GetPixel(0, 0).R, (0.2126f + 0.7152f) / 2f);
    }

    [Fact]
    public void Identity_Scalar2D_ReturnsSameInstance()
    {
        PixelBuffer field = MakeField();
        PixelBuffer result = TypeTranslator.FromField(field, SignalType.Scalar2D, 2, 2);
        Assert.Same(field, result);
    }

    [Fact]
    public void Identity_Color2D_ReturnsSameInstance()
    {
        PixelBuffer image = MakeImage();
        PixelBuffer result = TypeTranslator.FromImage(image, SignalType.Color2D, 2, 2);
        Assert.Same(image, result);
    }

    [Fact]
    public void Identity_DifferentSize_Resamples()
    {
        PixelBuffer field = MakeField();
        // 2×2 -> 4×4 is not identity-by-size, so a new buffer is returned.
        PixelBuffer result = TypeTranslator.FromField(field, SignalType.Scalar2D, 4, 4);
        Assert.NotSame(field, result);
        Assert.Equal(4, result.Width);
        Assert.Equal(4, result.Height);
    }

    [Fact]
    public void Translate_DispatchesBySource()
    {
        PixelBuffer field = MakeField();
        // Scalar2D -> Color2D through the single entry point.
        PixelBuffer result = TypeTranslator.Translate(
            SignalType.Scalar2D, field, 0, SignalType.Color2D, 2, 2);
        Assert.Equal(2, result.Width);
        Rgba p = result.GetPixel(1, 1);
        Approx.Near(p.R, 1f);
        Approx.Near(p.G, 1f);
        Approx.Near(p.B, 1f);
    }

    [Fact]
    public void Translate_ScalarSource_UsesScalarValue()
    {
        PixelBuffer result = TypeTranslator.Translate(
            SignalType.Scalar, null, 0.75, SignalType.Scalar2D, 2, 2);
        Approx.Near(result.GetPixel(0, 0).R, 0.75f);
    }

    [Fact]
    public void MeanRed_AllZeros_IsZero()
    {
        PixelBuffer field = PixelBuffer.Empty(2, 2);
        Approx.Near(TypeTranslator.MeanRed(field), 0f);
    }

    [Fact]
    public void MeanLuminance_SolidWhite_IsOne()
    {
        PixelBuffer image = PixelBuffer.Solid(2, 2, 1f, 1f, 1f, 1f);
        Approx.Near(TypeTranslator.MeanLuminance(image), 1f);
    }

    [Fact]
    public void FromField_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TypeTranslator.FromField(null!, SignalType.Color2D, 2, 2));
    }

    [Fact]
    public void FromImage_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TypeTranslator.FromImage(null!, SignalType.Scalar2D, 2, 2));
    }
}

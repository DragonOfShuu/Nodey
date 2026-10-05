using Nodey.Core.Image;

namespace Nodey.Core.Tests;

public class MathOpsTests
{
    [Theory]
    [InlineData(MathOperation.Add, 0.2f, 0.3f, 0.5f)]
    [InlineData(MathOperation.Subtract, 0.8f, 0.3f, 0.5f)]
    [InlineData(MathOperation.Multiply, 0.4f, 0.5f, 0.2f)]
    [InlineData(MathOperation.Divide, 0.8f, 0.25f, 3.2f)]
    public void ScalarApplyMatchesHandComputation(MathOperation op, float a, float b, float expected)
        => Approx.Near(MathOps.Apply(op, a, b), expected);

    [Fact]
    public void DivideByZeroYieldsZero()
        => Assert.Equal(0f, MathOps.Apply(MathOperation.Divide, 1f, 0f));

    [Fact]
    public void ApplyToBuffersUsesRedChannelAndWritesScalar2D()
    {
        var a = new PixelBuffer(2, 2);
        var b = new PixelBuffer(2, 2);
        // Red channel carries the values; other channels carry junk.
        a.SetPixel(0, 0, new Rgba(0.2f, 9f, 9f, 1f));
        a.SetPixel(1, 0, new Rgba(0.4f, 9f, 9f, 1f));
        a.SetPixel(0, 1, new Rgba(0.6f, 9f, 9f, 1f));
        a.SetPixel(1, 1, new Rgba(0.8f, 9f, 9f, 1f));
        b.SetPixel(0, 0, new Rgba(0.1f, 9f, 9f, 1f));
        b.SetPixel(1, 0, new Rgba(0.2f, 9f, 9f, 1f));
        b.SetPixel(0, 1, new Rgba(0.3f, 9f, 9f, 1f));
        b.SetPixel(1, 1, new Rgba(0.4f, 9f, 9f, 1f));

        PixelBuffer sum = MathOps.ApplyToBuffers(MathOperation.Add, a, b);
        Approx.Near(sum.GetPixel(0, 0).R, 0.3f);
        Approx.Near(sum.GetPixel(1, 0).R, 0.6f);
        Approx.Near(sum.GetPixel(0, 1).R, 0.9f);
        Approx.Near(sum.GetPixel(1, 1).R, 1.2f); // > 1 is fine for scalar fields
        // Scalar2D convention: G = B = 0, A = 1.
        for (int i = 0; i < sum.Rgba.Length; i += 4)
        {
            Assert.Equal(0f, sum.Rgba[i + 1]);
            Assert.Equal(0f, sum.Rgba[i + 2]);
            Assert.Equal(1f, sum.Rgba[i + 3]);
        }
    }

    [Fact]
    public void ApplyToBuffersRejectsMismatchedSizes()
    {
        var a = new PixelBuffer(2, 2);
        var b = new PixelBuffer(3, 3);
        Assert.Throws<ArgumentException>(() => MathOps.ApplyToBuffers(MathOperation.Add, a, b));
    }
}

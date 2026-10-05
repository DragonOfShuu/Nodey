using Nodey.Core.Image;
using Nodey.Core.Image.Noise;

namespace Nodey.Core.Tests;

public class NoiseTests
{
    private static readonly NoiseAlgorithm[] AllAlgorithms =
    {
        NoiseAlgorithm.Perlin,
        NoiseAlgorithm.Simplex,
        NoiseAlgorithm.Value,
        NoiseAlgorithm.Cellular,
        NoiseAlgorithm.Random,
    };

    [Theory]
    [InlineData(NoiseAlgorithm.Perlin)]
    [InlineData(NoiseAlgorithm.Simplex)]
    [InlineData(NoiseAlgorithm.Value)]
    [InlineData(NoiseAlgorithm.Cellular)]
    [InlineData(NoiseAlgorithm.Random)]
    public void FieldIsScalar2D(NoiseAlgorithm algorithm)
    {
        PixelBuffer field = NoiseField.Generate(algorithm, 16, 16, 0.1f, seed: 7);
        float[] data = field.Rgba;
        for (int i = 0; i < data.Length; i += 4)
        {
            Assert.Equal(0f, data[i + 1]);
            Assert.Equal(0f, data[i + 2]);
            Assert.Equal(1f, data[i + 3]);
        }
    }

    [Theory]
    [InlineData(NoiseAlgorithm.Perlin)]
    [InlineData(NoiseAlgorithm.Simplex)]
    [InlineData(NoiseAlgorithm.Value)]
    [InlineData(NoiseAlgorithm.Cellular)]
    [InlineData(NoiseAlgorithm.Random)]
    public void FieldValuesStayInUnitRange(NoiseAlgorithm algorithm)
    {
        // Dense grid, several scales: the documented [0, 1] contract.
        for (int scale = 1; scale <= 4; scale++)
        {
            PixelBuffer field = NoiseField.Generate(algorithm, 64, 64, scale * 0.05f, octaves: 3, seed: 11);
            foreach (float v in field.Rgba)
                Assert.InRange(v, 0f, 1f);
        }
    }

    [Theory]
    [InlineData(NoiseAlgorithm.Perlin)]
    [InlineData(NoiseAlgorithm.Simplex)]
    [InlineData(NoiseAlgorithm.Value)]
    [InlineData(NoiseAlgorithm.Cellular)]
    [InlineData(NoiseAlgorithm.Random)]
    public void SameSeedAndParamsGiveIdenticalFields(NoiseAlgorithm algorithm)
    {
        PixelBuffer a = NoiseField.Generate(algorithm, 32, 16, 0.07f, octaves: 3, seed: 42);
        PixelBuffer b = NoiseField.Generate(algorithm, 32, 16, 0.07f, octaves: 3, seed: 42);
        Approx.NearAll(a.Rgba, b.Rgba);
    }

    [Fact]
    public void DifferentSeedsGiveDifferentFields()
    {
        PixelBuffer a = NoiseField.Generate(NoiseAlgorithm.Perlin, 32, 32, 0.1f, seed: 1);
        PixelBuffer b = NoiseField.Generate(NoiseAlgorithm.Perlin, 32, 32, 0.1f, seed: 2);
        Assert.NotEqual(a.Rgba, b.Rgba);
    }

    [Fact]
    public void FieldSizeMatchesRequest()
    {
        PixelBuffer field = NoiseField.Generate(NoiseAlgorithm.Perlin, 5, 7, 0.1f);
        Assert.Equal((5, 7), (field.Width, field.Height));
    }

    [Fact]
    public void OffsetShiftsTheField()
    {
        // 2D Perlin: a pure X offset moves the pattern horizontally.
        PixelBuffer a = NoiseField.Generate(NoiseAlgorithm.Perlin, 32, 16, 0.05f, seed: 3);
        PixelBuffer b = NoiseField.Generate(NoiseAlgorithm.Perlin, 32, 16, 0.05f, offsetX: 2.5f, seed: 3);
        PixelBuffer c = NoiseField.Generate(NoiseAlgorithm.Perlin, 32, 16, 0.05f, offsetY: 2.5f, seed: 3);
        Assert.NotEqual(a.Rgba, b.Rgba);
        Assert.NotEqual(a.Rgba, c.Rgba);
    }

    [Fact]
    public void ChangingOctavesChangesCoherentFields()
    {
        // Random noise ignores octaves by design (white noise), so it is
        // excluded here.
        foreach (NoiseAlgorithm algorithm in AllAlgorithms)
        {
            if (algorithm == NoiseAlgorithm.Random)
                continue;
            PixelBuffer a = NoiseField.Generate(algorithm, 32, 32, 0.05f, octaves: 1, seed: 5);
            PixelBuffer b = NoiseField.Generate(algorithm, 32, 32, 0.05f, octaves: 5, seed: 5);
            Assert.NotEqual(a.Rgba, b.Rgba);
        }
    }

    [Fact]
    public void GenerateValidatesArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoiseField.Generate(NoiseAlgorithm.Perlin, 0, 4, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => NoiseField.Generate(NoiseAlgorithm.Perlin, 4, 0, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => NoiseField.Generate(NoiseAlgorithm.Perlin, 4, 4, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => NoiseField.Generate(NoiseAlgorithm.Perlin, 4, 4, 1f, octaves: 0));
    }

    [Fact]
    public void ValueNoiseAtIntegerCoordinatesIsTheHashedLatticeValue()
    {
        // At integer coordinates the smoothstep interpolation is exactly at a
        // lattice point, so the value is NoiseHash.Unit(Hash(x, y, seed)).
        var generator = new ValueNoiseGenerator(9);
        for (int x = -3; x <= 3; x++)
            for (int y = -3; y <= 3; y++)
            {
                float expected = NoiseHash.Unit(NoiseHash.Hash(x, y, 9));
                Approx.Near(generator.Sample(x, y, 1, 0.5f, 2f), expected, 1e-5f);
            }
    }

    [Fact]
    public void RandomNoiseIsPerPixelAndIndependentOfPositionFractions()
    {
        var generator = new RandomNoiseGenerator(4);
        // Fractional parts of the position must not matter.
        Assert.Equal(generator.Sample(1.0f, 2.0f, 1, 0.5f, 2f), generator.Sample(1.9f, 2.4f, 1, 0.5f, 2f));
        // Different pixels are (almost surely) different.
        float a = generator.Sample(0, 0, 1, 0.5f, 2f);
        float b = generator.Sample(1, 0, 1, 0.5f, 2f);
        float c = generator.Sample(0, 1, 1, 0.5f, 2f);
        Assert.True(a != b && b != c && a != c);
    }

    [Fact]
    public void CellularIsZeroAtAFeaturePoint()
    {
        var generator = new CellularNoiseGenerator(5);
        // The feature point of cell (0,0) is at (px, py) in [0,1)^2.
        NoiseHash.Unit2(0, 0, 5, out float px, out float py);
        Approx.Near(generator.Sample(px, py, 1, 0.5f, 2f), 0f, 1e-5f);
    }

    [Fact]
    public void CellularIsSmoothWithinCells()
    {
        var generator = new CellularNoiseGenerator(5);
        // Sample a 64x64 grid at 0.25 steps inside a single cell (0..1):
        // the F1 distance function is continuous, so adjacent samples must
        // be close (no discontinuities).
        float prev = -1f;
        for (int i = 0; i <= 16; i++)
        {
            float v = generator.Sample(i * 0.05f, 0.5f, 1, 0.5f, 2f);
            if (prev >= 0f)
                Assert.InRange(System.Math.Abs(v - prev), 0f, 0.06f);
            prev = v;
        }
    }

    [Fact]
    public void SimplexCoversMostOfTheUnitRangeOnADenseGrid()
    {
        PixelBuffer field = NoiseField.Generate(NoiseAlgorithm.Simplex, 256, 256, 0.05f, octaves: 1, seed: 1);
        float min = 1f, max = 0f;
        foreach (float v in field.Rgba)
        {
            min = System.Math.Min(min, v);
            max = System.Math.Max(max, v);
        }
        // 70.0f-scaled simplex is ~[-1, 1]; a dense grid should approach both
        // ends but never leave [0, 1].
        Assert.InRange(max, 0f, 1f);
        Assert.InRange(min, 0f, 1f);
        Assert.True(max > 0.9f, $"max {max} should approach 1");
        Assert.True(min < 0.1f, $"min {min} should approach 0");
    }

    [Fact]
    public void PerlinSpansWideRangeAndStaysInUnitRange()
    {
        var generator = new PerlinNoiseGenerator(2);
        float min = 1f, max = 0f;
        // Cover ~64 cells at fine resolution. The raw range is bounded by
        // about ±0.81 with diagonal unit gradients, so the normalized field
        // spans at most ~0.57 of [0, 1]; 0.4 is a safe degeneracy bound.
        for (int x = 0; x < 512; x++)
            for (int y = 0; y < 512; y++)
            {
                float v = generator.Sample(x * 0.125f, y * 0.125f, 1, 0.5f, 2f);
                min = System.Math.Min(min, v);
                max = System.Math.Max(max, v);
            }
        Assert.InRange(min, 0f, 1f);
        Assert.InRange(max, 0f, 1f);
        Assert.True(max - min > 0.4f, $"Perlin should span a wide range, got {max - min}");
    }
}

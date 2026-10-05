namespace Nodey.Core.Image;

/// <summary>
/// One stop in a <see cref="ColorRamp"/>: a color at a normalized position
/// 0..1 along the ramp.
/// </summary>
/// <remarks>
/// Color channels are expected in 0..1 (matching <see cref="PixelBuffer"/>
/// storage); only <see cref="Position"/> is validated.
/// </remarks>
public readonly struct RampStop
{
    /// <summary>Normalized position along the ramp, 0..1.</summary>
    public float Position { get; }

    /// <summary>Red, 0..1.</summary>
    public float R { get; }

    /// <summary>Green, 0..1.</summary>
    public float G { get; }

    /// <summary>Blue, 0..1.</summary>
    public float B { get; }

    /// <summary>Alpha, 0..1.</summary>
    public float A { get; }

    /// <summary>Creates a stop. <paramref name="position"/> must be in 0..1.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside 0..1.</exception>
    public RampStop(float position, float r, float g, float b, float a = 1f)
    {
        if (position < 0f || position > 1f)
            throw new ArgumentOutOfRangeException(
                nameof(position), position, "Stop position must be between 0 and 1.");
        Position = position;
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>The stop's color as an <see cref="Rgba"/>.</summary>
    public Rgba Color => new(R, G, B, A);
}

/// <summary>
/// A piecewise-linear color ramp: maps a scalar value (0..1) to a color by
/// linearly interpolating between sorted stops.
/// </summary>
public sealed class ColorRamp
{
    private readonly RampStop[] _stops;

    /// <summary>The stops, sorted by position (at least two).</summary>
    public IReadOnlyList<RampStop> Stops => _stops;

    /// <summary>
    /// Creates a ramp from the given stops. Stops are sorted by position
    /// automatically; duplicates are allowed (the segment collapses to a step).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="stops"/> is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two stops.</exception>
    public ColorRamp(IEnumerable<RampStop> stops)
    {
        if (stops is null)
            throw new ArgumentNullException(nameof(stops));
        var list = stops.ToList();
        if (list.Count < 2)
            throw new ArgumentException("A color ramp needs at least two stops.", nameof(stops));
        _stops = list.OrderBy(s => s.Position).ToArray();
    }

    /// <summary>The default ramp: opaque black (0) to opaque white (1).</summary>
    public static ColorRamp Default { get; } = new(
    [
        new RampStop(0f, 0f, 0f, 0f, 1f),
        new RampStop(1f, 1f, 1f, 1f, 1f),
    ]);

    /// <summary>
    /// Evaluates the ramp at <paramref name="t"/>: the segment between the
    /// two surrounding stops is interpolated linearly (all four channels);
    /// values below the first stop clamp to it, values above the last stop
    /// clamp to it.
    /// </summary>
    public Rgba Evaluate(float t)
    {
        RampStop[] stops = _stops;
        if (t <= stops[0].Position)
            return stops[0].Color;
        int count = stops.Length;
        if (t >= stops[count - 1].Position)
            return stops[count - 1].Color;

        for (int i = 1; i < count; i++)
        {
            if (t < stops[i].Position)
            {
                RampStop s0 = stops[i - 1];
                RampStop s1 = stops[i];
                float span = s1.Position - s0.Position;
                float f = span > 0f ? (t - s0.Position) / span : 1f;
                return new Rgba(
                    s0.R + (s1.R - s0.R) * f,
                    s0.G + (s1.G - s0.G) * f,
                    s0.B + (s1.B - s0.B) * f,
                    s0.A + (s1.A - s0.A) * f);
            }
        }
        return stops[count - 1].Color;
    }

    /// <summary>
    /// Maps a scalar field (value in the red channel; the Scalar2D
    /// convention) through this ramp and returns a color image.
    /// </summary>
    public PixelBuffer Apply(PixelBuffer scalarField)
    {
        ArgumentNullException.ThrowIfNull(scalarField);
        float[] src = scalarField.Rgba;
        var dst = new PixelBuffer(scalarField.Width, scalarField.Height);
        float[] o = dst.Rgba;
        for (int i = 0; i < src.Length; i += 4)
        {
            Rgba c = Evaluate(src[i]);
            o[i] = c.R;
            o[i + 1] = c.G;
            o[i + 2] = c.B;
            o[i + 3] = c.A;
        }
        return dst;
    }
}

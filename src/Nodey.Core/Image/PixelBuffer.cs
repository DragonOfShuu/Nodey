namespace Nodey.Core.Image;

/// <summary>
/// A CPU-side pixel buffer: a flat, row-major RGBA array of <see cref="float"/>
/// values normalized to 0..1, with straight (unmultiplied) alpha.
/// This is the engine's fundamental image type — the UI layer converts to/from
/// UI framework pixel formatters in STEP-3.
/// </summary>
/// <remarks>
/// Storage layout: <c>Rgba[(y * Width + x) * 4 + channel]</c>, channels R, G, B, A.
/// Hot paths (scale, blend, filter) work on the raw <see cref="Rgba"/> array
/// through local references and avoid per-pixel allocations.
/// </remarks>
public sealed class PixelBuffer
{
    private readonly float[] _rgba;

    /// <summary>Buffer width in pixels.</summary>
    public int Width { get; }

    /// <summary>Buffer height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// The raw RGBA channel data, row-major, one float per channel, 0..1.
    /// Length is <c>Width * Height * 4</c>.
    /// </summary>
    public float[] Rgba => _rgba;

    /// <summary>Creates an empty (all-zero / fully transparent) buffer.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    public PixelBuffer(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        Width = width;
        Height = height;
        _rgba = new float[width * height * 4];
    }

    /// <summary>Creates an empty (fully transparent black) buffer.</summary>
    public static PixelBuffer Empty(int width, int height) => new(width, height);

    /// <summary>Creates a buffer filled with the given color.</summary>
    public static PixelBuffer Solid(int width, int height, float r, float g, float b, float a = 1f)
    {
        var buffer = new PixelBuffer(width, height);
        buffer.Fill(r, g, b, a);
        return buffer;
    }

    /// <summary>
    /// Creates a buffer from 8-bit RGBA data (one byte per channel, row-major,
    /// <c>width * height * 4</c> bytes), scaled to 0..1.
    /// </summary>
    public static PixelBuffer FromRgba8(byte[] data, int width, int height)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        if (data.Length != width * height * 4)
            throw new ArgumentException(
                $"Expected {width * height * 4} bytes for a {width}x{height} buffer, got {data.Length}.", nameof(data));
        var buffer = new PixelBuffer(width, height);
        float[] dst = buffer._rgba;
        for (int i = 0, j = 0; i < data.Length; i++, j++)
            dst[j] = data[i] / 255f;
        return buffer;
    }

    /// <summary>
    /// Encodes the buffer as 8-bit RGBA data (round-trips with <see cref="FromRgba8"/>
    /// within 1/255 per channel).
    /// </summary>
    public byte[] ToRgba8()
    {
        byte[] dst = new byte[_rgba.Length];
        float[] src = _rgba;
        for (int i = 0; i < src.Length; i++)
        {
            float v = src[i] * 255f;
            if (v < 0f) v = 0f;
            else if (v > 255f) v = 255f;
            dst[i] = (byte)System.MathF.Round(v, System.MidpointRounding.AwayFromZero);
        }
        return dst;
    }

    /// <summary>Fills the whole buffer with the given color.</summary>
    public void Fill(float r, float g, float b, float a = 1f)
    {
        float[] data = _rgba;
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = a;
        }
    }

    /// <summary>Reads the pixel at (x, y).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The pixel is outside the buffer.</exception>
    public Rgba GetPixel(int x, int y)
    {
        CheckPixel(x, y);
        float[] data = _rgba;
        int i = (y * Width + x) * 4;
        return new Rgba(data[i], data[i + 1], data[i + 2], data[i + 3]);
    }

    /// <summary>Writes the pixel at (x, y).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The pixel is outside the buffer.</exception>
    public void SetPixel(int x, int y, Rgba value)
    {
        CheckPixel(x, y);
        float[] data = _rgba;
        int i = (y * Width + x) * 4;
        data[i] = value.R;
        data[i + 1] = value.G;
        data[i + 2] = value.B;
        data[i + 3] = value.A;
    }

    /// <summary>
    /// Returns a bilinearly scaled copy of this buffer. All four channels
    /// (including alpha) are interpolated; borders are clamped (no wrap, no
    /// sampling outside the image). Scaling to the current size is an exact
    /// identity.
    /// </summary>
    public PixelBuffer ScaleTo(int width, int height)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Target width must be positive.");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Target height must be positive.");

        float[] src = _rgba;
        var dst = new PixelBuffer(width, height);
        float[] dstData = dst._rgba;
        int srcW = Width, srcH = Height;
        float xRatio = (float)srcW / width;
        float yRatio = (float)srcH / height;

        for (int y = 0; y < height; y++)
        {
            // Map the destination pixel center into source pixel space.
            float sy = (y + 0.5f) * yRatio - 0.5f;
            if (sy < 0f) sy = 0f;
            else if (sy > srcH - 1f) sy = srcH - 1f;
            int y0 = (int)sy;
            int y1 = System.Math.Min(y0 + 1, srcH - 1);
            float fy = sy - y0;

            for (int x = 0; x < width; x++)
            {
                float sx = (x + 0.5f) * xRatio - 0.5f;
                if (sx < 0f) sx = 0f;
                else if (sx > srcW - 1f) sx = srcW - 1f;
                int x0 = (int)sx;
                int x1 = System.Math.Min(x0 + 1, srcW - 1);
                float fx = sx - x0;

                int i00 = (y0 * srcW + x0) * 4;
                int i10 = (y0 * srcW + x1) * 4;
                int i01 = (y1 * srcW + x0) * 4;
                int i11 = (y1 * srcW + x1) * 4;
                int o = (y * width + x) * 4;

                for (int c = 0; c < 4; c++)
                {
                    float top = src[i00 + c] + (src[i10 + c] - src[i00 + c]) * fx;
                    float bottom = src[i01 + c] + (src[i11 + c] - src[i01 + c]) * fx;
                    dstData[o + c] = top + (bottom - top) * fy;
                }
            }
        }
        return dst;
    }

    /// <summary>
    /// Returns the BT.709 luminance of the pixel at (x, y):
    /// <c>0.2126 R + 0.7152 G + 0.0722 B</c>.
    /// Inputs are treated as display-referred sRGB and gamma correction is
    /// skipped in v1 — consistency across the engine is what matters.
    /// </summary>
    public float Luminance(int x, int y)
    {
        CheckPixel(x, y);
        float[] data = _rgba;
        int i = (y * Width + x) * 4;
        return 0.2126f * data[i] + 0.7152f * data[i + 1] + 0.0722f * data[i + 2];
    }

    /// <summary>True if <paramref name="other"/> is non-null and has the same width and height.</summary>
    public bool SameSizeAs(PixelBuffer? other) =>
        other is not null && other.Width == Width && other.Height == Height;

    /// <summary>Returns an independent copy of this buffer.</summary>
    public PixelBuffer Clone()
    {
        var copy = new PixelBuffer(Width, Height);
        System.Array.Copy(_rgba, copy._rgba, _rgba.Length);
        return copy;
    }

    private void CheckPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException(
                nameof(x), (x, y), $"Pixel ({x}, {y}) is outside the {Width}x{Height} buffer.");
    }
}

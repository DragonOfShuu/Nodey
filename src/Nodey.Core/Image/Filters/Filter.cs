namespace Nodey.Core.Image.Filters;

/// <summary>
/// CPU filter implementations (the "Filter" node in STEP-3).
/// </summary>
/// <remarks>
/// All filters return a new buffer of the same size as the input and use
/// clamped (replicate) borders. Blur kernels are separable (two 1D passes)
/// and allocation-conscious: one temp buffer per pass, kernel precomputed
/// once, hot loops on raw array references.
/// </remarks>
public static class Filter
{
    /// <summary>Applies a filter with the given kind and scalar parameters.</summary>
    public static PixelBuffer Apply(PixelBuffer src, FilterKind kind, float amount, float radius, float aperture)
        => Apply(src, new FilterParams(kind, amount, radius, aperture));

    /// <summary>Applies a filter described by <paramref name="params"/>.</summary>
    public static PixelBuffer Apply(PixelBuffer src, FilterParams @params)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(@params);
        return @params.Kind switch
        {
            FilterKind.GaussianBlur => GaussianBlur(src, @params.Radius),
            FilterKind.BoxBlur => BoxBlur(src, @params.Radius),
            FilterKind.LensBlur => LensBlur(src, @params.Radius, @params.Aperture),
            FilterKind.Sharpen => Sharpen(src, @params.Amount, @params.Radius),
            FilterKind.Emboss => Emboss(src),
            FilterKind.Erosion => Morphology(src, erode: true),
            FilterKind.Dilation => Morphology(src, erode: false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(@params), @params.Kind, "Unknown filter kind."),
        };
    }

    /// <summary>
    /// Gaussian blur: a 1D kernel <c>exp(-d^2 / (2 sigma^2))</c> with
    /// <c>sigma = radius / 2</c>, applied separably (horizontal then
    /// vertical) with clamped borders. Radius 0 returns a copy.
    /// </summary>
    public static PixelBuffer GaussianBlur(PixelBuffer src, float radiusF)
    {
        int radius = System.Math.Max(0, (int)System.MathF.Round(radiusF));
        if (radius == 0)
            return src.Clone();

        int k = 2 * radius + 1;
        float sigma = radius / 2f;
        float[] kernel = new float[k];
        float sum = 0f;
        for (int i = 0; i < k; i++)
        {
            float d = i - radius;
            kernel[i] = System.MathF.Exp(-d * d / (2f * sigma * sigma));
            sum += kernel[i];
        }
        for (int i = 0; i < k; i++)
            kernel[i] /= sum;

        var tmp = new PixelBuffer(src.Width, src.Height);
        Convolve(src, tmp, kernel, horizontal: true);
        var dst = new PixelBuffer(src.Width, src.Height);
        Convolve(tmp, dst, kernel, horizontal: false);
        return dst;
    }

    /// <summary>
    /// Box blur: uniform window of radius <paramref name="radius"/> on each
    /// side, applied separably with a running sum (O(1) per pixel) and
    /// clamped borders. Radius 0 returns a copy.
    /// </summary>
    public static PixelBuffer BoxBlur(PixelBuffer src, float radiusF)
    {
        int radius = System.Math.Max(0, (int)System.MathF.Round(radiusF));
        if (radius == 0)
            return src.Clone();

        var tmp = new PixelBuffer(src.Width, src.Height);
        BoxPass(src, tmp, radius, horizontal: true);
        var dst = new PixelBuffer(src.Width, src.Height);
        BoxPass(tmp, dst, radius, horizontal: false);
        return dst;
    }

    /// <summary>
    /// Lens blur: a Gaussian whose effective radius is
    /// <c>radius * aperture</c> (center-weighted falloff; a v1 stand-in for a
    /// true bokeh disk — see FilterKind docs).
    /// </summary>
    public static PixelBuffer LensBlur(PixelBuffer src, float radius, float aperture)
        => GaussianBlur(src, System.MathF.Max(0f, radius * aperture));

    /// <summary>
    /// One 1D box pass with a running sum (clamped borders): the window is
    /// the 2r+1 samples around the pixel, with edge samples duplicated.
    /// </summary>
    private static void BoxPass(PixelBuffer src, PixelBuffer dst, int radius, bool horizontal)
    {
        int w = src.Width;
        int h = src.Height;
        float win = 2f * radius + 1f;
        float[] s = src.Rgba;
        float[] d = dst.Rgba;

        if (horizontal)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int i = -radius; i <= radius; i++)
                {
                    int idx = (row + System.Math.Clamp(i, 0, w - 1)) * 4;
                    r += s[idx];
                    g += s[idx + 1];
                    b += s[idx + 2];
                    a += s[idx + 3];
                }
                for (int x = 0; x < w; x++)
                {
                    int o = (row + x) * 4;
                    d[o] = r / win;
                    d[o + 1] = g / win;
                    d[o + 2] = b / win;
                    d[o + 3] = a / win;
                    int addX = System.Math.Clamp(x + radius + 1, 0, w - 1);
                    int remX = System.Math.Clamp(x - radius, 0, w - 1);
                    int ia = (row + addX) * 4;
                    int ir = (row + remX) * 4;
                    r += s[ia] - s[ir];
                    g += s[ia + 1] - s[ir + 1];
                    b += s[ia + 2] - s[ir + 2];
                    a += s[ia + 3] - s[ir + 3];
                }
            }
        }
        else
        {
            for (int x = 0; x < w; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int i = -radius; i <= radius; i++)
                {
                    int idx = (System.Math.Clamp(i, 0, h - 1) * w + x) * 4;
                    r += s[idx];
                    g += s[idx + 1];
                    b += s[idx + 2];
                    a += s[idx + 3];
                }
                for (int y = 0; y < h; y++)
                {
                    int o = (y * w + x) * 4;
                    d[o] = r / win;
                    d[o + 1] = g / win;
                    d[o + 2] = b / win;
                    d[o + 3] = a / win;
                    int addY = System.Math.Clamp(y + radius + 1, 0, h - 1);
                    int remY = System.Math.Clamp(y - radius, 0, h - 1);
                    int ia = (addY * w + x) * 4;
                    int ir = (remY * w + x) * 4;
                    r += s[ia] - s[ir];
                    g += s[ia + 1] - s[ir + 1];
                    b += s[ia + 2] - s[ir + 2];
                    a += s[ia + 3] - s[ir + 3];
                }
            }
        }
    }

    /// <summary>
    /// One 1D convolution pass (horizontal or vertical) of <paramref name="src"/>
    /// into <paramref name="dst"/> with clamped borders, all four channels.
    /// </summary>
    private static void Convolve(PixelBuffer src, PixelBuffer dst, float[] kernel, bool horizontal)
    {
        int w = src.Width;
        int h = src.Height;
        int radius = (kernel.Length - 1) / 2;
        float[] s = src.Rgba;
        float[] d = dst.Rgba;

        if (horizontal)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int i = -radius; i <= radius; i++)
                    {
                        int xx = System.Math.Clamp(x + i, 0, w - 1);
                        float kv = kernel[i + radius];
                        int idx = (row + xx) * 4;
                        r += s[idx] * kv;
                        g += s[idx + 1] * kv;
                        b += s[idx + 2] * kv;
                        a += s[idx + 3] * kv;
                    }
                    int o = (row + x) * 4;
                    d[o] = r;
                    d[o + 1] = g;
                    d[o + 2] = b;
                    d[o + 3] = a;
                }
            }
        }
        else
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int i = -radius; i <= radius; i++)
                    {
                        int yy = System.Math.Clamp(y + i, 0, h - 1);
                        float kv = kernel[i + radius];
                        int idx = (yy * w + x) * 4;
                        r += s[idx] * kv;
                        g += s[idx + 1] * kv;
                        b += s[idx + 2] * kv;
                        a += s[idx + 3] * kv;
                    }
                    int o = (y * w + x) * 4;
                    d[o] = r;
                    d[o + 1] = g;
                    d[o + 2] = b;
                    d[o + 3] = a;
                }
            }
        }
    }

    /// <summary>
    /// Sharpen (unsharp mask): <c>src + amount * (src - blur(src, radius))</c>,
    /// clamped to 0..1, alpha untouched. amount = 0 returns a copy.
    /// </summary>
    public static PixelBuffer Sharpen(PixelBuffer src, float amount, float radius)
    {
        if (amount == 0f)
            return src.Clone();
        PixelBuffer blurred = GaussianBlur(src, radius);
        int w = src.Width;
        int h = src.Height;
        float[] s = src.Rgba;
        float[] bl = blurred.Rgba;
        var dst = new PixelBuffer(w, h);
        float[] o = dst.Rgba;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                o[i] = System.Math.Clamp(s[i] + amount * (s[i] - bl[i]), 0f, 1f);
                o[i + 1] = System.Math.Clamp(s[i + 1] + amount * (s[i + 1] - bl[i + 1]), 0f, 1f);
                o[i + 2] = System.Math.Clamp(s[i + 2] + amount * (s[i + 2] - bl[i + 2]), 0f, 1f);
                o[i + 3] = s[i + 3];
            }
        }
        return dst;
    }

    /// <summary>
    /// Emboss: directional (1,-1) derivative <c>0.5 + 0.5 * (f(x+1, y-1) -
    /// f(x-1, y+1))</c> per RGB channel with clamped borders, alpha
    /// untouched. A flat image embosses to 0.5 gray.
    /// </summary>
    public static PixelBuffer Emboss(PixelBuffer src)
    {
        int w = src.Width;
        int h = src.Height;
        float[] s = src.Rgba;
        var dst = new PixelBuffer(w, h);
        float[] o = dst.Rgba;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int xa = System.Math.Clamp(x - 1, 0, w - 1);
                int ya = System.Math.Clamp(y + 1, 0, h - 1);
                int xb = System.Math.Clamp(x + 1, 0, w - 1);
                int yb = System.Math.Clamp(y - 1, 0, h - 1);
                int ia = (ya * w + xa) * 4;
                int ib = (yb * w + xb) * 4;
                int io = (y * w + x) * 4;
                for (int c = 0; c < 3; c++)
                    o[io + c] = System.Math.Clamp(0.5f + 0.5f * (s[ib + c] - s[ia + c]), 0f, 1f);
                o[io + 3] = s[io + 3];
            }
        }
        return dst;
    }

    /// <summary>
    /// 3x3 erosion (min) / dilation (max) of the BT.709 luminance and the
    /// alpha channel, with clamped borders; the output is grayscale
    /// (R = G = B = luminance result). Transparent neighbors count as
    /// luminance 0, so erosion shrinks shapes into transparency.
    /// </summary>
    private static PixelBuffer Morphology(PixelBuffer src, bool erode)
    {
        int w = src.Width;
        int h = src.Height;
        float[] s = src.Rgba;
        var dst = new PixelBuffer(w, h);
        float[] o = dst.Rgba;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float lum = erode ? 1f : 0f;
                float alpha = erode ? 1f : 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = System.Math.Clamp(y + dy, 0, h - 1);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = System.Math.Clamp(x + dx, 0, w - 1);
                        int i = (yy * w + xx) * 4;
                        float l = 0.2126f * s[i] + 0.7152f * s[i + 1] + 0.0722f * s[i + 2];
                        float a = s[i + 3];
                        if (erode)
                        {
                            if (l < lum) lum = l;
                            if (a < alpha) alpha = a;
                        }
                        else
                        {
                            if (l > lum) lum = l;
                            if (a > alpha) alpha = a;
                        }
                    }
                }
                int io = (y * w + x) * 4;
                o[io] = lum;
                o[io + 1] = lum;
                o[io + 2] = lum;
                o[io + 3] = alpha;
            }
        }
        return dst;
    }
}

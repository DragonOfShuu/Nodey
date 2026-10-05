namespace Nodey.Core.Image.Filters;

/// <summary>
/// The filter algorithms (the "Filter" node in STEP-3).
/// </summary>
public enum FilterKind
{
    /// <summary>Gaussian blur (separable, clamped borders).</summary>
    GaussianBlur,

    /// <summary>Box blur (running-sum separable, clamped borders).</summary>
    BoxBlur,

    /// <summary>
    /// Lens blur: a Gaussian with effective radius <c>radius * aperture</c>
    /// (center-weighted falloff, a v1 stand-in for a true bokeh disk).
    /// </summary>
    LensBlur,

    /// <summary>Sharpen (unsharp mask: <c>src + amount * (src - blur)</c>).</summary>
    Sharpen,

    /// <summary>Emboss (directional (1,-1) derivative, 0.5 gray offset).</summary>
    Emboss,

    /// <summary>Erosion (3x3 min of luminance; grayscale output).</summary>
    Erosion,

    /// <summary>Dilation (3x3 max of luminance; grayscale output).</summary>
    Dilation,
}

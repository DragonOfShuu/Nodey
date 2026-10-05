namespace Nodey.Core.Image.Filters;

/// <summary>
/// Bundles the parameters of a <see cref="Filter"/> call. This mirrors the
/// Filter node's parameters in STEP-3 so the node can forward its state
/// unchanged.
/// </summary>
/// <param name="Kind">Which filter to run.</param>
/// <param name="Amount">Sharpen amount (0..~2); unused by other filters.</param>
/// <param name="Radius">Blur radius in pixels (Gaussian/Box/Lens/Sharpen);
/// unused by Emboss/Erosion/Dilation.</param>
/// <param name="Aperture">Lens blur aperture multiplier; only used by
/// <see cref="FilterKind.LensBlur"/> (effective radius = radius * aperture).</param>
public sealed record FilterParams(
    FilterKind Kind,
    float Amount = 1f,
    float Radius = 4f,
    float Aperture = 1f);

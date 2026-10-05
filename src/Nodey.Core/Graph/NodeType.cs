namespace Nodey.Core.Graph;

/// <summary>
/// The eight node types in the catalog (PLAN.md §4.2).
/// </summary>
public enum NodeType
{
    /// <summary>Paintable surface; buffer lives in the graph's layer store.</summary>
    Layer,

    /// <summary>Procedural noise generator (Scalar2D output).</summary>
    Noise,

    /// <summary>Scalar-field → color ramp mapping.</summary>
    ColorRamp,

    /// <summary>Two-image blend (base + blend, mode + opacity).</summary>
    Mix,

    /// <summary>Dynamic stack of layers folded bottom-up (grows on connect).</summary>
    Multilayer,

    /// <summary>Pixel filter (blur, sharpen, emboss, morphology, …).</summary>
    Filter,

    /// <summary>Element-wise scalar math (Add/Subtract/Multiply/Divide).</summary>
    Math,

    /// <summary>Constant solid color (Color2D output).</summary>
    Color,
}

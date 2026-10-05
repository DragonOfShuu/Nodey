namespace Nodey.Core.Types;

/// <summary>
/// The kind of data a node's output port carries.
/// STEP-2 defines the enum only; the node-output-to-signal translation table
/// (which port of which node type maps to which <see cref="SignalType"/>)
/// is defined in STEP-3.
/// </summary>
public enum SignalType
{
    /// <summary>
    /// A single scalar value (e.g. a parameter).
    /// </summary>
    Scalar,

    /// <summary>
    /// A 2D scalar field. Convention: the value is stored in the red channel
    /// of each pixel, with green = blue = 0 and alpha = 1 (see
    /// <c>NoiseField.Generate</c> and <c>MathOps</c>).
    /// </summary>
    Scalar2D,

    /// <summary>
    /// A 2D color image (RGBA, all channels meaningful).
    /// </summary>
    Color2D,
}

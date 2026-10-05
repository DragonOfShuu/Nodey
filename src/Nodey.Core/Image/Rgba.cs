namespace Nodey.Core.Image;

/// <summary>
/// An RGBA color with straight (unmultiplied) alpha. All channels are
/// normalized to 0..1, matching <see cref="PixelBuffer"/> storage.
/// </summary>
/// <remarks>
/// <see cref="ColorRamp.Evaluate"/> returns this type rather than a UI
/// <c>Color</c> because Nodey.Core is UI-free (no UI framework dependency) —
/// the spec's <c>Color</c> is realized as this struct.
/// </remarks>
public readonly record struct Rgba(float R, float G, float B, float A)
{
    /// <summary>Creates an <see cref="Rgba"/> with alpha 1.</summary>
    public Rgba(float r, float g, float b)
        : this(r, g, b, 1f)
    {
    }
}

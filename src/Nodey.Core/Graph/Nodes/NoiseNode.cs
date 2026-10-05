using Nodey.Core.Image;
using Nodey.Core.Image.Noise;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// Procedural noise generator (PLAN.md §4.2): fills the document with a
/// deterministic <see cref="SignalType.Scalar2D"/> field (value in the red
/// channel) computed at document resolution.
/// </summary>
public sealed class NoiseNode : GraphNode
{
    private NoiseAlgorithm _algorithm = NoiseAlgorithm.Perlin;
    private float _scale = 0.02f;
    private int _octaves = 4;
    private int _seed;
    private float _offsetX;
    private float _offsetY;

    public NoiseNode(double x, double y)
        : base("Noise", x, y)
    {
        AddOutputPort("Value", SignalType.Scalar2D);
    }

    internal NoiseNode(Guid id, double x, double y)
        : base(id, "Noise", x, y)
    {
        AddOutputPort("Value", SignalType.Scalar2D);
    }

    public override NodeType Type => NodeType.Noise;

    /// <summary>Which generator to use (Perlin, Simplex, Value, Cellular, Random).</summary>
    public NoiseAlgorithm Algorithm
    {
        get => _algorithm;
        set => SetParam(ref _algorithm, value);
    }

    /// <summary>Noise-space scale (cells per pixel; bigger = tighter pattern).</summary>
    public float Scale
    {
        get => _scale;
        set => SetParam(ref _scale, value);
    }

    /// <summary>Fractal (fBm) octave count.</summary>
    public int Octaves
    {
        get => _octaves;
        set => SetParam(ref _octaves, value);
    }

    /// <summary>Random seed (deterministic per seed).</summary>
    public int Seed
    {
        get => _seed;
        set => SetParam(ref _seed, value);
    }

    /// <summary>Noise-space X offset.</summary>
    public float OffsetX
    {
        get => _offsetX;
        set => SetParam(ref _offsetX, value);
    }

    /// <summary>Noise-space Y offset.</summary>
    public float OffsetY
    {
        get => _offsetY;
        set => SetParam(ref _offsetY, value);
    }

    internal override PixelBuffer EvaluateNode(NodeEvaluationContext ctx)
        => NoiseField.Generate(
            _algorithm, ctx.Width, ctx.Height,
            scale: _scale, octaves: _octaves, seed: _seed,
            offsetX: _offsetX, offsetY: _offsetY);
}

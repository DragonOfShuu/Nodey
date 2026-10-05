using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// Blends two images: <c>Base</c> underneath, <c>Blend</c> on top, with a
/// <see cref="BlendMode"/> and an <see cref="Opacity"/> (PLAN.md §4.2). Both
/// inputs expect <see cref="SignalType.Color2D"/> but also accept
/// <see cref="SignalType.Scalar"/> and <see cref="SignalType.Scalar2D"/>
/// (translated by the evaluator).
/// </summary>
public sealed class MixNode : GraphNode
{
    private BlendMode _blendMode = BlendMode.Normal;
    private float _opacity = 1f;

    public MixNode(double x, double y)
        : base("Mix", x, y)
    {
        AddInputPort("Base", SignalType.Color2D,
            SignalType.Color2D, SignalType.Scalar, SignalType.Scalar2D);
        AddInputPort("Blend", SignalType.Color2D,
            SignalType.Color2D, SignalType.Scalar, SignalType.Scalar2D);
        AddOutputPort("Result", SignalType.Color2D);
    }

    internal MixNode(Guid id, double x, double y)
        : base(id, "Mix", x, y)
    {
        AddInputPort("Base", SignalType.Color2D,
            SignalType.Color2D, SignalType.Scalar, SignalType.Scalar2D);
        AddInputPort("Blend", SignalType.Color2D,
            SignalType.Color2D, SignalType.Scalar, SignalType.Scalar2D);
        AddOutputPort("Result", SignalType.Color2D);
    }

    public override NodeType Type => NodeType.Mix;

    /// <summary>The blend mode applied to the pair.</summary>
    public BlendMode BlendMode
    {
        get => _blendMode;
        set => SetParam(ref _blendMode, value);
    }

    /// <summary>Blend opacity, 0..1.</summary>
    public float Opacity
    {
        get => _opacity;
        set => SetParam(ref _opacity, value);
    }

    internal override PixelBuffer EvaluateNode(NodeEvaluationContext ctx)
        => Blend.Apply(ctx.GetInput(0), ctx.GetInput(1), _blendMode, _opacity);
}

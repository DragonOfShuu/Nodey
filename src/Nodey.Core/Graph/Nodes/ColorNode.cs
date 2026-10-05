using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// A constant solid color (PLAN.md §4.2): outputs a
/// <see cref="SignalType.Color2D"/> buffer filled with (R, G, B, A) at
/// document resolution.
/// </summary>
public sealed class ColorNode : GraphNode
{
    private float _r = 1f;
    private float _g = 1f;
    private float _b = 1f;
    private float _a = 1f;

    public ColorNode(double x, double y)
        : base("Color", x, y)
    {
        AddOutputPort("Color", SignalType.Color2D);
    }

    internal ColorNode(Guid id, double x, double y)
        : base(id, "Color", x, y)
    {
        AddOutputPort("Color", SignalType.Color2D);
    }

    public override NodeType Type => NodeType.Color;

    /// <summary>Red, 0..1.</summary>
    public float ColorR
    {
        get => _r;
        set => SetParam(ref _r, value);
    }

    /// <summary>Green, 0..1.</summary>
    public float ColorG
    {
        get => _g;
        set => SetParam(ref _g, value);
    }

    /// <summary>Blue, 0..1.</summary>
    public float ColorB
    {
        get => _b;
        set => SetParam(ref _b, value);
    }

    /// <summary>Alpha, 0..1.</summary>
    public float ColorA
    {
        get => _a;
        set => SetParam(ref _a, value);
    }

    internal override PixelBuffer EvaluateNode(NodeEvaluationContext ctx)
        => PixelBuffer.Solid(ctx.Width, ctx.Height, _r, _g, _b, _a);
}

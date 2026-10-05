using Nodey.Core.Image;
using Nodey.Core.Image.Filters;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// Applies a STEP-2 pixel <see cref="FilterKind"/> to its input
/// (PLAN.md §4.2). The input expects <see cref="SignalType.Color2D"/> and
/// accepts only <see cref="SignalType.Color2D"/>.
/// </summary>
public sealed class FilterNode : GraphNode
{
    private FilterKind _kind = FilterKind.GaussianBlur;
    private float _amount = 1f;
    private float _radius = 4f;
    private float _aperture = 1f;

    public FilterNode(double x, double y)
        : base("Filter", x, y)
    {
        AddInputPort("Input", SignalType.Color2D, SignalType.Color2D);
        AddOutputPort("Output", SignalType.Color2D);
    }

    internal FilterNode(Guid id, double x, double y)
        : base(id, "Filter", x, y)
    {
        AddInputPort("Input", SignalType.Color2D, SignalType.Color2D);
        AddOutputPort("Output", SignalType.Color2D);
    }

    public override NodeType Type => NodeType.Filter;

    /// <summary>Which filter to apply.</summary>
    public FilterKind Kind
    {
        get => _kind;
        set => SetParam(ref _kind, value);
    }

    /// <summary>Sharpen amount (only used by <see cref="FilterKind.Sharpen"/>).</summary>
    public float Amount
    {
        get => _amount;
        set => SetParam(ref _amount, value);
    }

    /// <summary>Blur radius in pixels (Gaussian/Box/Lens/Sharpen).</summary>
    public float Radius
    {
        get => _radius;
        set => SetParam(ref _radius, value);
    }

    /// <summary>Lens blur aperture multiplier (only used by <see cref="FilterKind.LensBlur"/>).</summary>
    public float Aperture
    {
        get => _aperture;
        set => SetParam(ref _aperture, value);
    }

    internal override PixelBuffer EvaluateNode(NodeEvaluationContext ctx)
        => Filter.Apply(ctx.GetInput(0), new FilterParams(_kind, _amount, _radius, _aperture));
}

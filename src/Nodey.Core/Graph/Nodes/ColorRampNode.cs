using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// Maps a scalar field to colors through a piecewise-linear ramp
/// (PLAN.md §4.2). This is the spec's "foreign nodes" example: the input
/// port <b>expects</b> <see cref="SignalType.Scalar2D"/> but <b>accepts</b>
/// <see cref="SignalType.Scalar"/>, <see cref="SignalType.Scalar2D"/> and
/// <see cref="SignalType.Color2D"/> — the evaluator translates whatever is
/// connected into the expected type before the mutate function runs.
/// </summary>
public sealed class ColorRampNode : GraphNode
{
    private List<RampStop> _stops = [..ColorRamp.Default.Stops];
    private ColorRamp _ramp = ColorRamp.Default;

    public ColorRampNode(double x, double y)
        : base("Color Ramp", x, y)
    {
        AddInputPort("Input", SignalType.Scalar2D,
            SignalType.Scalar, SignalType.Scalar2D, SignalType.Color2D);
        AddOutputPort("Color", SignalType.Color2D);
    }

    internal ColorRampNode(Guid id, double x, double y)
        : base(id, "Color Ramp", x, y)
    {
        AddInputPort("Input", SignalType.Scalar2D,
            SignalType.Scalar, SignalType.Scalar2D, SignalType.Color2D);
        AddOutputPort("Color", SignalType.Color2D);
    }

    public override NodeType Type => NodeType.ColorRamp;

    /// <summary>The ramp stops (at least two; sorted by position).</summary>
    public IReadOnlyList<RampStop> Stops => _stops;

    /// <summary>
    /// Replaces all stops (at least two required) and rebuilds the cached
    /// ramp. Raises <see cref="Changed"/>.
    /// </summary>
    public void SetStops(IEnumerable<RampStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var list = stops.ToList();
        if (list.Count < 2)
            throw new ArgumentException("A color ramp needs at least two stops.", nameof(stops));
        _stops = list;
        _ramp = new ColorRamp(list);
        OnChanged();
    }

    internal override PixelBuffer EvaluateNode(NodeEvaluationContext ctx)
        => _ramp.Apply(ctx.GetInput(0));
}

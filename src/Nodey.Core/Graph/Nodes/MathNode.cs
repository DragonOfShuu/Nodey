using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// Element-wise math on two scalar fields (PLAN.md §4.2). Both inputs expect
/// <see cref="SignalType.Scalar2D"/> and accept <see cref="SignalType.Scalar"/>
/// and <see cref="SignalType.Scalar2D"/>. The output is a
/// <see cref="SignalType.Scalar2D"/> field.
/// </summary>
public sealed class MathNode : GraphNode
{
    private MathOperation _operation = MathOperation.Add;

    public MathNode(double x, double y)
        : base("Math", x, y)
    {
        AddInputPort("A", SignalType.Scalar2D, SignalType.Scalar, SignalType.Scalar2D);
        AddInputPort("B", SignalType.Scalar2D, SignalType.Scalar, SignalType.Scalar2D);
        AddOutputPort("Result", SignalType.Scalar2D);
    }

    internal MathNode(Guid id, double x, double y)
        : base(id, "Math", x, y)
    {
        AddInputPort("A", SignalType.Scalar2D, SignalType.Scalar, SignalType.Scalar2D);
        AddInputPort("B", SignalType.Scalar2D, SignalType.Scalar, SignalType.Scalar2D);
        AddOutputPort("Result", SignalType.Scalar2D);
    }

    public override NodeType Type => NodeType.Math;

    /// <summary>Which element-wise operation to apply (Add/Subtract/Multiply/Divide).</summary>
    public MathOperation Operation
    {
        get => _operation;
        set => SetParam(ref _operation, value);
    }

    internal override PixelBuffer EvaluateNode(NodeEvaluationContext ctx)
        => MathOps.ApplyToBuffers(_operation, ctx.GetInput(0), ctx.GetInput(1));
}

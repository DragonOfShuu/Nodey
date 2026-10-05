using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// A paintable surface (PLAN.md §4.2). The node itself holds no pixels: its
/// buffer lives in the owning graph's layer store (<see cref="Graph.GetLayerBuffer"/>),
/// created at document W×H when the node is added. The node's
/// <see cref="GraphNode.Name"/> is the layer name shown in the graph and the
/// Draw View's layers panel.
/// </summary>
public sealed class LayerNode : GraphNode
{
    public LayerNode(double x, double y)
        : base("Layer", x, y)
    {
        AddOutputPort("Image", SignalType.Color2D);
    }

    internal LayerNode(Guid id, double x, double y)
        : base(id, "Layer", x, y)
    {
        AddOutputPort("Image", SignalType.Color2D);
    }

    public override NodeType Type => NodeType.Layer;

    internal override PixelBuffer EvaluateNode(NodeEvaluationContext ctx)
    {
        // Returns the LIVE layer buffer (not a clone): consumers must not
        // mutate it in place. Painting in the Draw View goes through
        // Graph.SetLayerBuffer, which keeps dirty propagation correct.
        return ctx.Graph.GetLayerBuffer(Id);
    }
}

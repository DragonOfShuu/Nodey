using Nodey.Core.Image;

namespace Nodey.Core.Graph;

/// <summary>
/// Raised by <see cref="GraphEvaluator"/> after a node is (re-)evaluated.
/// Carries the node and its fresh output buffer so the UI (STEP-4/5) can
/// refresh that node's preview without re-evaluating anything.
/// </summary>
public sealed class NodeEvaluatedEventArgs : EventArgs
{
    /// <summary>The node that was just evaluated.</summary>
    public GraphNode Node { get; }

    /// <summary>The node's fresh output buffer (document-sized).</summary>
    public PixelBuffer Result { get; }

    public NodeEvaluatedEventArgs(GraphNode node, PixelBuffer result)
    {
        Node = node;
        Result = result;
    }
}

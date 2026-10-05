using Nodey.Core.Image;

namespace Nodey.Core.Graph;

/// <summary>
/// Per-node evaluation context handed to <see cref="GraphNode.EvaluateNode"/>.
/// The evaluator has already translated each connected input into the port's
/// <see cref="NodePort.ExpectedType"/> (PLAN.md §5.4 pipeline); unconnected
/// ports resolve to their default (Scalar 0 / zero field / transparent).
/// </summary>
public sealed class NodeEvaluationContext
{
    private readonly PixelBuffer[] _inputs;
    private readonly bool[] _connected;

    internal NodeEvaluationContext(Graph graph, PixelBuffer[] inputs, bool[] connected)
    {
        Graph = graph;
        _inputs = inputs;
        _connected = connected;
    }

    /// <summary>The graph this evaluation is running on (for W×H and layer buffers).</summary>
    public Graph Graph { get; }

    /// <summary>Document width in pixels.</summary>
    public int Width => Graph.Width;

    /// <summary>Document height in pixels.</summary>
    public int Height => Graph.Height;

    /// <summary>Whether input port <paramref name="index"/> has a connection.</summary>
    public bool IsInputConnected(int index) => _connected[index];

    /// <summary>
    /// The translated input for port <paramref name="index"/>: the upstream
    /// buffer converted to the port's expected type, or the port's default
    /// (Scalar 0 / zero field / transparent) when unconnected.
    /// </summary>
    public PixelBuffer GetInput(int index) => _inputs[index]!;
}

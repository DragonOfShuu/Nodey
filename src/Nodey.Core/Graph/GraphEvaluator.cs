using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// The incremental evaluation engine (PLAN.md §5). Runs the graph on demand:
/// topological order, re-evaluating only dirty nodes, caching each node's
/// output buffer, and translating cross-type connections through
/// <see cref="TypeTranslator"/>.
/// </summary>
/// <remarks>
/// The evaluator never mutates node parameters or graph structure. It reads
/// the dirty flags maintained by <see cref="Graph"/>, clears them as it goes,
/// and raises <see cref="NodeEvaluated"/> for every node it re-evaluates.
/// A node is clean only when it has a cached result, so reading an upstream
/// node's cached buffer is always safe.
/// </remarks>
public sealed class GraphEvaluator
{
    private readonly Graph _graph;
    private readonly Dictionary<Guid, PixelBuffer> _results = new();
    private readonly Dictionary<Guid, double> _constants = new();

    /// <summary>
    /// Creates an evaluator for <paramref name="graph"/>. Subscribes to the
    /// graph's <see cref="Graph.Changed"/> to drop cached results of removed
    /// nodes (the dirty flags already handle re-evaluation).
    /// </summary>
    public GraphEvaluator(Graph graph)
    {
        _graph = graph;
        _graph.Changed += OnGraphChanged;
    }

    /// <summary>
    /// Raised after each node is (re-)evaluated, in topological order.
    /// </summary>
    public event EventHandler<NodeEvaluatedEventArgs>? NodeEvaluated;

    /// <summary>
    /// Sets a constant scalar value on an unconnected input port. The value is
    /// translated to the port's expected type at evaluation time (e.g. a
    /// 0.5 on a Scalar2D port becomes a 0.5 field). Marks the owning node and
    /// everything downstream dirty.
    /// </summary>
    public void SetConstantValue(NodePort port, double value)
    {
        if (!port.IsInput)
            throw new ArgumentException("Constant values can only be set on input ports.", nameof(port));
        _constants[port.Id] = value;
        MarkNodeAndDownstreamDirty(port.Owner);
    }

    /// <summary>
    /// Clears a previously set constant value on an input port (the port then
    /// evaluates to its type's default). Marks the owning node and everything
    /// downstream dirty.
    /// </summary>
    public void ClearConstantValue(NodePort port)
    {
        if (!port.IsInput)
            throw new ArgumentException("Constant values can only be cleared on input ports.", nameof(port));
        if (_constants.Remove(port.Id))
            MarkNodeAndDownstreamDirty(port.Owner);
    }

    /// <summary>
    /// Evaluates the graph: topological order, re-evaluating only dirty nodes.
    /// Throws <see cref="InvalidOperationException"/> on a cycle (the graph
    /// normally prevents these, but a defensively).
    /// </summary>
    public void Evaluate()
    {
        List<GraphNode> order = TopologicalOrder();
        foreach (GraphNode node in order)
        {
            if (!node.IsDirty)
                continue; // cached result is still valid
            PixelBuffer result = EvaluateNode(node);
            _results[node.Id] = result;
            node.ClearDirty();
            NodeEvaluated?.Invoke(this, new NodeEvaluatedEventArgs(node, result));
        }
    }

    /// <summary>
    /// The cached output buffer of <paramref name="nodeId"/>. The node must
    /// exist and have been evaluated (call <see cref="Evaluate"/> first).
    /// </summary>
    public PixelBuffer GetResult(Guid nodeId)
    {
        if (_graph.FindNode(nodeId) is null)
            throw new InvalidOperationException($"Node {nodeId} is not in the graph.");
        if (!_results.TryGetValue(nodeId, out PixelBuffer? buffer))
            throw new InvalidOperationException($"Node {nodeId} has not been evaluated. Call Evaluate() first.");
        return buffer;
    }

    /// <summary>
    /// The cached output buffer of the document output node (see
    /// <see cref="Graph.FindOutputNode"/>). The graph must have been
    /// evaluated first.
    /// </summary>
    public PixelBuffer GetOutput()
    {
        GraphNode? output = _graph.FindOutputNode();
        if (output is null)
            throw new InvalidOperationException("The graph has no output node.");
        return GetResult(output.Id);
    }

    // -- internals -----------------------------------------------------------------

    private void OnGraphChanged(object? sender, EventArgs e)
    {
        // Drop cached results of nodes that no longer exist (structural change).
        // The dirty flags already force re-evaluation of everything affected.
        var live = new HashSet<Guid>(_graph.Nodes.Select(n => n.Id));
        foreach (Guid id in _results.Keys.Where(id => !live.Contains(id)).ToList())
            _results.Remove(id);
        foreach (Guid id in _constants.Keys.Where(id => _graph.FindPortOrNull(id) is null).ToList())
            _constants.Remove(id);
    }

    private PixelBuffer EvaluateNode(GraphNode node)
    {
        int w = _graph.Width;
        int h = _graph.Height;
        NodePort[] inputs = node.Inputs.ToArray();
        int count = inputs.Length;
        var buffers = new PixelBuffer[count];
        var connected = new bool[count];
        GraphConnection[] incoming = _graph.IncomingOf(node).ToArray();

        for (int i = 0; i < count; i++)
        {
            NodePort port = inputs[i];
            GraphConnection? conn = incoming.FirstOrDefault(c => c.TargetPortId == port.Id);
            if (conn is null)
            {
                connected[i] = false;
                buffers[i] = _constants.TryGetValue(port.Id, out double constant)
                    ? TypeTranslator.FromScalar(constant, port.ExpectedType, w, h)
                    : DefaultBuffer(port.ExpectedType, w, h);
                continue;
            }
            connected[i] = true;
            GraphNode source = _graph.NodeOfPort(conn.SourcePortId);
            PixelBuffer sourceBuffer = GetResult(source.Id);
            buffers[i] = TypeTranslator.Translate(
                source.OutputType, sourceBuffer, 0, port.ExpectedType, w, h);
        }

        var ctx = new NodeEvaluationContext(_graph, buffers, connected);
        return node.EvaluateNode(ctx);
    }

    private void MarkNodeAndDownstreamDirty(GraphNode node)
    {
        _graph.MarkDirty(node);
        _graph.MarkDownstreamDirty(node);
    }

    /// <summary>
    /// Kahn's algorithm over the graph's connections, deterministic (ready
    /// nodes are always processed in <see cref="GraphNode.AddedOrder"/>).
    /// </summary>
    private List<GraphNode> TopologicalOrder()
    {
        IReadOnlyList<GraphNode> nodes = _graph.Nodes;
        var inDegree = new Dictionary<Guid, int>();
        var dependents = new Dictionary<Guid, List<GraphNode>>();
        foreach (GraphNode n in nodes)
        {
            inDegree[n.Id] = 0;
            dependents[n.Id] = [];
        }
        foreach (GraphConnection c in _graph.Connections)
        {
            GraphNode src = _graph.NodeOfPort(c.SourcePortId);
            GraphNode tgt = _graph.NodeOfPort(c.TargetPortId);
            inDegree[tgt.Id]++;
            dependents[src.Id].Add(tgt);
        }

        var ready = nodes.Where(n => inDegree[n.Id] == 0).OrderBy(n => n.AddedOrder).ToList();
        var order = new List<GraphNode>(nodes.Count);
        while (ready.Count > 0)
        {
            GraphNode n = ready[0];
            ready.RemoveAt(0);
            order.Add(n);
            foreach (GraphNode d in dependents[n.Id].OrderBy(d => d.AddedOrder))
            {
                inDegree[d.Id]--;
                if (inDegree[d.Id] == 0)
                    ready.Add(d);
            }
            ready = ready.OrderBy(x => x.AddedOrder).ToList();
        }

        if (order.Count != nodes.Count)
            throw new InvalidOperationException("The graph contains a cycle.");
        return order;
    }

    private static PixelBuffer DefaultBuffer(SignalType type, int w, int h) => type switch
    {
        SignalType.Scalar => MakeScalar(0f),
        SignalType.Scalar2D => ZeroField(w, h),
        SignalType.Color2D => PixelBuffer.Empty(w, h),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown signal type."),
    };

    private static PixelBuffer MakeScalar(float value)
    {
        var one = new PixelBuffer(1, 1);
        one.Rgba[0] = value;
        one.Rgba[3] = 1f;
        return one;
    }

    private static PixelBuffer ZeroField(int w, int h)
    {
        var field = new PixelBuffer(w, h);
        float[] d = field.Rgba;
        for (int i = 3; i < d.Length; i += 4)
            d[i] = 1f; // A = 1; R/G/B stay 0
        return field;
    }
}

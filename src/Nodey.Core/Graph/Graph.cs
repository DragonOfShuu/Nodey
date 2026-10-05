using Nodey.Core.Image;

namespace Nodey.Core.Graph;

/// <summary>
/// The node graph: nodes, connections, the layer buffer store, and the dirty
/// walk (PLAN.md §4.3). The graph is the source of truth; the UI (STEP-4/5)
/// subscribes to <see cref="Changed"/> (structure) and to each node's
/// <see cref="GraphNode.Changed"/> (parameters).
/// </summary>
/// <remarks>
/// Change semantics:
/// <list type="bullet">
/// <item>Parameter changes raise <see cref="GraphNode.Changed"/> (not
/// <see cref="Changed"/>); the graph marks that node and everything
/// downstream dirty. No evaluation happens here (no reentrancy — the
/// evaluator runs on demand).</item>
/// <item>Structural changes (add/remove node, connect/disconnect, document
/// size) mark ALL nodes dirty and raise <see cref="Changed"/>.</item>
/// <item><see cref="SetLayerBuffer"/> marks the layer node and everything
/// downstream dirty (pixels are data, not structure — no
/// <see cref="Changed"/>).</item>
/// </list>
/// Nothing in this class evaluates nodes — see <see cref="GraphEvaluator"/>.
/// </remarks>
public sealed class Graph
{
    private readonly List<GraphNode> _nodes = [];
    private readonly Dictionary<Guid, GraphNode> _nodeIndex = new();
    private readonly List<GraphConnection> _connections = [];
    private readonly Dictionary<Guid, PixelBuffer> _layerBuffers = new();
    private int _width = 512;
    private int _height = 512;
    private int _addedCounter;

    /// <summary>
    /// Creates an empty graph at the default 512×512 document resolution.
    /// </summary>
    public Graph()
    {
    }

    /// <summary>Document width in pixels (default 512). Changing it marks all nodes dirty.</summary>
    public int Width
    {
        get => _width;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Width must be positive.");
            if (value == _width)
                return;
            _width = value;
            MarkAllDirty();
            RaiseChanged();
        }
    }

    /// <summary>Document height in pixels (default 512). Changing it marks all nodes dirty.</summary>
    public int Height
    {
        get => _height;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Height must be positive.");
            if (value == _height)
                return;
            _height = value;
            MarkAllDirty();
            RaiseChanged();
        }
    }

    /// <summary>All nodes, in insertion order.</summary>
    public IReadOnlyList<GraphNode> Nodes => _nodes;

    /// <summary>All connections.</summary>
    public IReadOnlyList<GraphConnection> Connections => _connections;

    /// <summary>
    /// Raised after structural changes: nodes added/removed, connections
    /// made/broken, document size changed.
    /// </summary>
    public event EventHandler? Changed;

    // -- node management --------------------------------------------------------

    /// <summary>
    /// Creates a node of <paramref name="type"/> at (x, y), gives it a unique
    /// name, adds it to the graph, and (for Layer nodes) creates its layer
    /// buffer at document W×H. Marks all nodes dirty and raises
    /// <see cref="Changed"/>.
    /// </summary>
    public GraphNode AddNode(NodeType type, double x, double y)
        => AddNodeInternal(type, x, y, Guid.NewGuid());

    internal GraphNode AddNodeInternal(NodeType type, double x, double y, Guid id)
    {
        GraphNode node = CreateNode(type, x, y, id);
        node.Name = MakeUniqueName(type);
        node.Changed += OnNodeChanged;
        _nodes.Add(node);
        _nodeIndex[node.Id] = node;
        node.AddedOrder = ++_addedCounter;
        if (node is LayerNode)
            _layerBuffers[node.Id] = new PixelBuffer(_width, _height);
        MarkAllDirty();
        RaiseChanged();
        return node;
    }

    /// <summary>
    /// Removes a node and all of its connections. Marks all nodes dirty and
    /// raises <see cref="Changed"/>.
    /// </summary>
    public void RemoveNode(Guid id)
    {
        GraphNode? node = FindNode(id);
        if (node is null)
            return;
        node.Changed -= OnNodeChanged;
        _connections.RemoveAll(c => NodeOfPort(c.SourcePortId) == node || NodeOfPort(c.TargetPortId) == node);
        _nodes.Remove(node);
        _nodeIndex.Remove(id);
        _layerBuffers.Remove(id);
        MarkAllDirty();
        RaiseChanged();
    }

    /// <summary>Finds a node by id (null if absent).</summary>
    public GraphNode? FindNode(Guid id) =>
        _nodeIndex.TryGetValue(id, out GraphNode? node) ? node : null;

    /// <summary>
    /// The node catalog factory (PLAN.md §4.2). Internal so document loading
    /// can reconstruct nodes with their stored ids.
    /// </summary>
    internal static GraphNode CreateNode(NodeType type, double x, double y, Guid id) => type switch
    {
        NodeType.Layer => new LayerNode(id, x, y),
        NodeType.Noise => new NoiseNode(id, x, y),
        NodeType.ColorRamp => new ColorRampNode(id, x, y),
        NodeType.Mix => new MixNode(id, x, y),
        NodeType.Multilayer => new MultilayerNode(id, x, y),
        NodeType.Filter => new FilterNode(id, x, y),
        NodeType.Math => new MathNode(id, x, y),
        NodeType.Color => new ColorNode(id, x, y),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown node type."),
    };

    private string MakeUniqueName(NodeType type)
    {
        string baseName = type.ToString();
        if (!_nodeIndex.Values.Any(n => n.Name == baseName))
            return baseName;
        int n = 2;
        while (_nodeIndex.Values.Any(node => node.Name == $"{baseName} {n}"))
            n++;
        return $"{baseName} {n}";
    }

    // -- connections --------------------------------------------------------------

    /// <summary>
    /// Validates a prospective connection (PLAN.md §5.3 rules) and explains
    /// the rejection in <paramref name="reason"/> (user-readable for UI toasts).
    /// </summary>
    public bool CanConnect(NodePort source, NodePort target, out string reason)
    {
        reason = string.Empty;
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        // Rule 1: different nodes.
        if (source.Owner == target.Owner)
        {
            reason = "A node cannot connect to itself.";
            return false;
        }

        // Rule 2: direction (output → input).
        if (source.IsInput || !target.IsInput)
        {
            reason = "Connect an output port to an input port.";
            return false;
        }

        // Rule 3: type rule — the source node's output signal type must be
        // accepted by the target port (PLAN.md §5.3).
        if (!target.AcceptedTypes.Contains(source.Owner.OutputType))
        {
            reason = $"A {source.Owner.OutputType} output cannot connect to " +
                     $"'{target.Owner.Name}.{target.Label}' (accepts " +
                     $"{string.Join(", ", target.AcceptedTypes)}).";
            return false;
        }

        // Rule 4: no duplicate of the exact same connection.
        if (HasConnection(source.Id, target.Id))
        {
            reason = "These ports are already connected.";
            return false;
        }

        // Rule 5: no cycles.
        if (WouldCreateCycle(source.Owner, target.Owner))
        {
            reason = "This connection would create a cycle.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Connects <paramref name="source"/> (output) to <paramref name="target"/>
    /// (input) if <see cref="CanConnect"/> passes. Replaces any existing
    /// connection on the target port (one input per port). When the target is
    /// a Multilayer's topmost input, a new input port appears. Returns the new
    /// connection, or null if the connection was invalid. Marks all nodes
    /// dirty and raises <see cref="Changed"/> on success.
    /// </summary>
    public GraphConnection? Connect(NodePort source, NodePort target)
    {
        if (!CanConnect(source, target, out _))
            return null;

        bool targetWasConnected = IsConnected(target);

        // One input per port: replace any existing connection on the target.
        GraphConnection? replaced = _connections.FirstOrDefault(c => c.TargetPortId == target.Id);
        if (replaced is not null)
            _connections.Remove(replaced);

        GraphConnection connection = new(Guid.NewGuid(), source.Id, target.Id);
        _connections.Add(connection);

        // Multilayer growth: connecting the topmost input (which was empty) spawns
        // the next port. Guarded so re-wiring an already-connected topmost does
        // not grow twice.
        if (target.Owner is MultilayerNode multilayer && IsTopmostInput(target) && !targetWasConnected)
            multilayer.AddInputPort();

        MarkAllDirty();
        RaiseChanged();
        return connection;
    }

    /// <summary>
    /// Removes the connection with the given id (no-op if absent). Multilayer
    /// shrink: a Multilayer always keeps one empty "next" slot at the top, so
    /// when the disconnected port is the topmost <i>connected</i> layer (the
    /// port just below the empty slot), the empty topmost slot is removed and
    /// the just-freed port becomes the new empty topmost slot. Disconnecting a
    /// lower (middle) layer leaves an empty slot in place — the evaluator skips
    /// unconnected ports, and compaction is a UI concern (v1 limitation).
    /// Marks all nodes dirty and raises <see cref="Changed"/>.
    /// </summary>
    public void Disconnect(Guid connectionId)
    {
        GraphConnection? connection = _connections.FirstOrDefault(c => c.Id == connectionId);
        if (connection is null)
            return;
        _connections.Remove(connection);

        NodePort target = FindPort(connection.TargetPortId);
        if (target.Owner is MultilayerNode multilayer
            && multilayer.Inputs.Count >= 2
            && multilayer.Inputs[^2] == target)
        {
            multilayer.RemoveLastInputPort();
        }

        MarkAllDirty();
        RaiseChanged();
    }

    /// <summary>True if a connection exists from exactly these two ports.</summary>
    public bool HasConnection(Guid sourcePortId, Guid targetPortId) =>
        _connections.Any(c => c.SourcePortId == sourcePortId && c.TargetPortId == targetPortId);

    /// <summary>True if the port currently has a connection (in or out).</summary>
    public bool IsConnected(NodePort port) => port.IsInput
        ? _connections.Any(c => c.TargetPortId == port.Id)
        : _connections.Any(c => c.SourcePortId == port.Id);

    /// <summary>All connections leaving <paramref name="node"/>.</summary>
    public IEnumerable<GraphConnection> OutgoingOf(GraphNode node)
    {
        foreach (NodePort p in node.Outputs)
            foreach (GraphConnection c in _connections)
                if (c.SourcePortId == p.Id)
                    yield return c;
    }

    /// <summary>All connections entering <paramref name="node"/>.</summary>
    public IEnumerable<GraphConnection> IncomingOf(GraphNode node)
    {
        foreach (NodePort p in node.Inputs)
            foreach (GraphConnection c in _connections)
                if (c.TargetPortId == p.Id)
                    yield return c;
    }

    // -- queries -------------------------------------------------------------------

    /// <summary>
    /// All transitive dependents of <paramref name="nodeId"/> (everything that
    /// directly or indirectly consumes its output), in BFS order, excluding
    /// the node itself.
    /// </summary>
    public IEnumerable<GraphNode> DownstreamOf(Guid nodeId)
    {
        GraphNode? start = FindNode(nodeId);
        if (start is null)
            yield break;
        var visited = new HashSet<Guid> { start.Id };
        var queue = new Queue<GraphNode>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            GraphNode current = queue.Dequeue();
            foreach (GraphConnection c in OutgoingOf(current))
            {
                GraphNode next = NodeOfPort(c.TargetPortId);
                if (visited.Add(next.Id))
                {
                    yield return next;
                    queue.Enqueue(next);
                }
            }
        }
    }

    /// <summary>
    /// All transitive sources of <paramref name="nodeId"/> (everything the
    /// node directly or indirectly depends on), in BFS order, excluding the
    /// node itself.
    /// </summary>
    public IEnumerable<GraphNode> UpstreamOf(Guid nodeId)
    {
        GraphNode? start = FindNode(nodeId);
        if (start is null)
            yield break;
        var visited = new HashSet<Guid> { start.Id };
        var queue = new Queue<GraphNode>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            GraphNode current = queue.Dequeue();
            foreach (GraphConnection c in IncomingOf(current))
            {
                GraphNode prev = NodeOfPort(c.SourcePortId);
                if (visited.Add(prev.Id))
                {
                    yield return prev;
                    queue.Enqueue(prev);
                }
            }
        }
    }

    /// <summary>
    /// The document output node (PLAN.md §4.3): among all nodes whose output
    /// has no outgoing connection, the one with the greatest
    /// <see cref="GraphNode.PositionX"/> (rightmost); ties go to the most
    /// recently added node. Null when the graph has no nodes.
    /// </summary>
    public GraphNode? FindOutputNode()
    {
        if (_nodes.Count == 0)
            return null;

        GraphNode? best = null;
        foreach (GraphNode n in _nodes)
        {
            if (OutgoingOf(n).Any())
                continue;
            if (best is null
                || n.PositionX > best.PositionX
                || (n.PositionX == best.PositionX && n.AddedOrder > best.AddedOrder))
            {
                best = n;
            }
        }
        return best;
    }

    // -- layer buffer store ----------------------------------------------------------

    /// <summary>
    /// Replaces the pixel buffer of a Layer node. The buffer must be
    /// document-sized. Marks the layer node and everything downstream dirty
    /// (no <see cref="Changed"/> — pixels are data, not structure).
    /// </summary>
    public void SetLayerBuffer(Guid layerNodeId, PixelBuffer buffer)
    {
        GraphNode? node = FindNode(layerNodeId);
        if (node is null || node.Type != NodeType.Layer)
            throw new ArgumentException($"Node {layerNodeId} is not a Layer node.", nameof(layerNodeId));
        ArgumentNullException.ThrowIfNull(buffer);
        _layerBuffers[layerNodeId] = buffer;
        MarkDirty(node);
        MarkDownstreamDirty(node);
    }

    /// <summary>
    /// The live pixel buffer of a Layer node. Consumers must not mutate it in
    /// place — paint through <see cref="SetLayerBuffer"/>.
    /// </summary>
    public PixelBuffer GetLayerBuffer(Guid layerNodeId)
    {
        if (!_layerBuffers.TryGetValue(layerNodeId, out PixelBuffer? buffer))
            throw new InvalidOperationException($"No layer buffer for node {layerNodeId}.");
        return buffer;
    }

    // -- dirty walk (PLAN.md §4.3) ---------------------------------------------------

    /// <summary>Marks a single node dirty.</summary>
    public void MarkDirty(GraphNode node) => node.MarkDirty();

    /// <summary>
    /// Marks <paramref name="node"/> and everything transitively downstream
    /// of it dirty (BFS over outgoing connections, visited-set guarded).
    /// </summary>
    public void MarkDownstreamDirty(GraphNode node)
    {
        var visited = new HashSet<Guid> { node.Id };
        var queue = new Queue<GraphNode>();
        queue.Enqueue(node);
        while (queue.Count > 0)
        {
            GraphNode current = queue.Dequeue();
            foreach (GraphConnection c in OutgoingOf(current))
            {
                GraphNode next = NodeOfPort(c.TargetPortId);
                if (visited.Add(next.Id))
                {
                    next.MarkDirty();
                    queue.Enqueue(next);
                }
            }
        }
    }

    private void MarkAllDirty()
    {
        foreach (GraphNode n in _nodes)
            n.MarkDirty();
    }

    private void OnNodeChanged(object? sender, EventArgs e)
    {
        if (sender is not GraphNode node)
            return;
        MarkDirty(node);
        MarkDownstreamDirty(node);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // -- port helpers ------------------------------------------------------------------

    private static bool IsTopmostInput(NodePort port)
    {
        GraphNode owner = port.Owner;
        return owner.Inputs.Count > 0 && owner.Inputs[^1] == port;
    }

    /// <summary>
    /// Cycle check: adding target→source would create a cycle iff
    /// <paramref name="sourceNode"/> is reachable from
    /// <paramref name="targetNode"/> by following outgoing connections. The
    /// existing connection into the target port (if any) is an INCOMING edge
    /// of the target node and can never lie on a path out of it, so
    /// replace-then-connect needs no special handling.
    /// </summary>
    private bool WouldCreateCycle(GraphNode sourceNode, GraphNode targetNode)
    {
        var visited = new HashSet<Guid> { targetNode.Id };
        var stack = new Stack<GraphNode>();
        stack.Push(targetNode);
        while (stack.Count > 0)
        {
            GraphNode current = stack.Pop();
            foreach (GraphConnection c in OutgoingOf(current))
            {
                GraphNode next = NodeOfPort(c.TargetPortId);
                if (next.Id == sourceNode.Id)
                    return true;
                if (visited.Add(next.Id))
                    stack.Push(next);
            }
        }
        return false;
    }

    /// <summary>Finds the port with the given id (throws if absent).</summary>
    internal NodePort FindPort(Guid portId)
    {
        return FindPortOrNull(portId)
            ?? throw new InvalidOperationException($"Port {portId} does not belong to any node in this graph.");
    }

    /// <summary>Finds the port with the given id, or null if absent.</summary>
    internal NodePort? FindPortOrNull(Guid portId)
    {
        foreach (GraphNode n in _nodes)
        {
            foreach (NodePort p in n.Inputs)
                if (p.Id == portId)
                    return p;
            foreach (NodePort p in n.Outputs)
                if (p.Id == portId)
                    return p;
        }
        return null;
    }

    /// <summary>The node owning the port with the given id.</summary>
    internal GraphNode NodeOfPort(Guid portId) => FindPort(portId).Owner;

    /// <summary>
    /// The index of the port within its owner's input (or output) list — the
    /// stable identity used by document save/load.
    /// </summary>
    internal int PortIndexOf(Guid portId)
    {
        NodePort port = FindPort(portId);
        IReadOnlyList<NodePort> list = port.IsInput ? port.Owner.Inputs : port.Owner.Outputs;
        for (int i = 0; i < list.Count; i++)
            if (list[i] == port)
                return i;
        throw new InvalidOperationException($"Port {portId} not found in its owner's port list.");
    }

    /// <summary>
    /// Adds a connection directly (no validation, no Multilayer growth).
    /// Used by document loading, which reconstructs exact port counts before
    /// re-adding connections in stored order.
    /// </summary>
    internal void AddConnectionInternal(NodePort source, NodePort target, Guid connectionId)
    {
        _connections.Add(new GraphConnection(connectionId, source.Id, target.Id));
        MarkAllDirty();
        RaiseChanged();
    }
}

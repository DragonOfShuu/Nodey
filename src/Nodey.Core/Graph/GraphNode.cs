using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// Base class for all node types (PLAN.md §4.2). Concrete classes
/// (<c>LayerNode</c>, <c>NoiseNode</c>, …) declare their ports in their
/// constructor and expose typed parameter properties that raise
/// <see cref="Changed"/> when set.
/// </summary>
/// <remarks>
/// Change notification (the UI subscribes in STEP-4/5):
/// <list type="bullet">
/// <item><see cref="Changed"/> fires on every parameter change (including
/// <see cref="Name"/>). The owning <see cref="Graph"/> marks the node and
/// everything downstream dirty. <b>Position changes do NOT fire
/// <see cref="Changed"/></b> — layout is not evaluation state.</item>
/// <item><see cref="IsDirty"/> is owned by the graph's dirty walk
/// (PLAN.md §4.3); nodes read it but never set it themselves.</item>
/// </list>
/// Core stays UI-free: positions are plain doubles in graph space, no
/// Avalonia types.
/// </remarks>
public abstract class GraphNode
{
    private readonly List<NodePort> _inputs = [];
    private readonly List<NodePort> _outputs = [];
    private string _name;
    private bool _isDirty;

    protected GraphNode(string name, double positionX, double positionY)
        : this(Guid.NewGuid(), name, positionX, positionY)
    {
    }

    internal GraphNode(Guid id, string name, double positionX, double positionY)
    {
        Id = id;
        _name = name;
        PositionX = positionX;
        PositionY = positionY;
    }

    /// <summary>Stable identity (preserved across save/load).</summary>
    public Guid Id { get; }

    /// <summary>Which of the eight catalog types this node is.</summary>
    public abstract NodeType Type { get; }

    /// <summary>
    /// Display name (editable). For Layer nodes this is the layer name shown
    /// in the graph and the Draw View's layers panel. Changing it raises
    /// <see cref="Changed"/>.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (value == _name)
                return;
            _name = value;
            OnChanged();
        }
    }

    /// <summary>Graph-space X position (UI coordinates, not evaluation state).</summary>
    public double PositionX { get; set; }

    /// <summary>Graph-space Y position (UI coordinates, not evaluation state).</summary>
    public double PositionY { get; set; }

    /// <summary>Input ports, in declaration order.</summary>
    public IReadOnlyList<NodePort> Inputs => _inputs;

    /// <summary>Output ports, in declaration order.</summary>
    public IReadOnlyList<NodePort> Outputs => _outputs;

    /// <summary>
    /// The signal type this node's output carries. v1 nodes have exactly one
    /// output port; this is its type (used by the connection rule,
    /// PLAN.md §5.3).
    /// </summary>
    public SignalType OutputType => _outputs[0].ExpectedType;

    /// <summary>
    /// Whether the node needs re-evaluation. Set by the graph's dirty walk
    /// (parameter change, structural change, layer buffer change) and cleared
    /// by the evaluator after it has re-evaluated the node.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Insertion order within the graph (monotonically increasing). Used to
    /// break <see cref="Graph.FindOutputNode"/> ties (most recently added
    /// wins).
    /// </summary>
    internal int AddedOrder { get; set; }

    /// <summary>
    /// Raised when a parameter (or the name) changes. The owning graph marks
    /// this node and all downstream nodes dirty. Never raised from the
    /// constructor.
    /// </summary>
    public event EventHandler? Changed;

    // -- dirty state (graph/evaluator owned) ----------------------------------

    internal void MarkDirty() => _isDirty = true;

    internal void ClearDirty() => _isDirty = false;

    // -- port management (constructors + Multilayer) ---------------------------

    protected void AddInputPort(string label, SignalType expectedType, params SignalType[] acceptedTypes)
    {
        ArgumentNullException.ThrowIfNull(acceptedTypes);
        _inputs.Add(new NodePort(this, label, true, expectedType, acceptedTypes));
    }

    protected void AddOutputPort(string label, SignalType signalType)
    {
        _outputs.Add(new NodePort(this, label, false, signalType, [signalType]));
    }

    /// <summary>Removes an input port (Multilayer shrink; driven by the graph).</summary>
    protected void RemoveInputPort(int index) => _inputs.RemoveAt(index);

    // -- port lookup helpers ----------------------------------------------------

    /// <summary>The input port at <paramref name="index"/> (declaration order).</summary>
    public NodePort GetInputPort(int index)
    {
        if (index < 0 || index >= _inputs.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"Node '{Name}' has {_inputs.Count} input ports.");
        return _inputs[index];
    }

    /// <summary>The output port at <paramref name="index"/> (declaration order).</summary>
    public NodePort GetOutputPort(int index)
    {
        if (index < 0 || index >= _outputs.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"Node '{Name}' has {_outputs.Count} output ports.");
        return _outputs[index];
    }

    /// <summary>Finds an input port by label (case-sensitive).</summary>
    public bool TryGetInputPortByLabel(string label, out NodePort port)
    {
        foreach (NodePort p in _inputs)
        {
            if (p.Label == label)
            {
                port = p;
                return true;
            }
        }
        port = null!;
        return false;
    }

    // -- parameters / change notification ---------------------------------------

    /// <summary>
    /// Sets a parameter field only when the value actually changed, raising
    /// <see cref="Changed"/> in that case. Use for every parameter property.
    /// </summary>
    protected void SetParam<T>(ref T field, T value) where T : notnull
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnChanged();
    }

    /// <summary>Raises <see cref="Changed"/> (safe with no subscribers).</summary>
    protected void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    // -- evaluation ---------------------------------------------------------------

    /// <summary>
    /// The node's mutate function (last pipeline stage, PLAN.md §5.4): given
    /// its inputs already translated to each port's expected type by the
    /// evaluator, produce this node's output buffer.
    /// </summary>
    internal abstract PixelBuffer EvaluateNode(NodeEvaluationContext context);
}

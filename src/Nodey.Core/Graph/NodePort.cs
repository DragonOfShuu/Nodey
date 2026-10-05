using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// A single input or output port on a <see cref="GraphNode"/>.
/// </summary>
/// <remarks>
/// For <b>input</b> ports, <see cref="ExpectedType"/> is the signal type the
/// node's mutate function consumes, and <see cref="AcceptedTypes"/> is the set
/// of signal types the port accepts a connection from (PLAN.md §5.2–5.3).
/// For <b>output</b> ports, <see cref="ExpectedType"/> is the signal type the
/// port carries and <see cref="AcceptedTypes"/> is just that single type
/// (unused by the connection check, which looks at the source node's output).
/// </remarks>
public sealed class NodePort
{
    private readonly GraphNode _owner;

    internal NodePort(GraphNode owner, string label, bool isInput, SignalType expectedType, SignalType[] acceptedTypes)
    {
        _owner = owner;
        Label = label;
        IsInput = isInput;
        ExpectedType = expectedType;
        AcceptedTypes = acceptedTypes.ToHashSet();
        Id = Guid.NewGuid();
    }

    /// <summary>Stable port identity (used by connections; regenerated on load).</summary>
    public Guid Id { get; }

    /// <summary>Display label shown on the node (and in the UI's port list).</summary>
    public string Label { get; set; }

    /// <summary>
    /// Input: the signal type this node consumes from the port (the mutate
    /// function receives the input translated to this type).
    /// Output: the signal type this port carries.
    /// </summary>
    public SignalType ExpectedType { get; }

    /// <summary>
    /// The set of signal types this port accepts a connection from (PLAN.md
    /// §5.3). The check is: <c>source.Owner.OutputType ∈ AcceptedTypes</c>.
    /// </summary>
    public IReadOnlySet<SignalType> AcceptedTypes { get; }

    /// <summary>True for input ports, false for output ports.</summary>
    public bool IsInput { get; }

    /// <summary>The node this port belongs to.</summary>
    public GraphNode Owner => _owner;

    public override string ToString() => $"{Owner.Name}.{Label}";
}

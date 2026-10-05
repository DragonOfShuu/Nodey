using Nodey.Core.Image;
using Nodey.Core.Types;

namespace Nodey.Core.Graph;

/// <summary>
/// A dynamic stack of layers folded bottom-up (PLAN.md §4.2). Starts with one
/// input port <c>Layer0</c>; the owning <see cref="Graph"/> calls
/// <see cref="AddInputPort"/> when a connection is made to the topmost input
/// (the "another input appears" behavior) and <see cref="RemoveLastInputPort"/>
/// when the topmost <i>connected</i> layer is disconnected (the empty topmost
/// "next" slot is dropped, keeping at least one port). Each port has its own
/// <see cref="PortBlend"/> (blend mode + opacity).
/// </summary>
/// <remarks>
/// Fold semantics: inputs are folded bottom-up (port 0 = bottom).
/// <b>Unconnected ports are skipped</b> — they represent empty slots waiting
/// for a connection. This is a deliberate refinement of the blanket "unconnected
/// input = default" rule: blending the transparent default at Normal/1.0 would
/// wipe the stack to black (STEP-2's Normal mode replaces RGB, it does not
/// alpha-weight), so an empty slot must contribute nothing at all.
/// </remarks>
public sealed class MultilayerNode : GraphNode
{
    /// <summary>Per-input blend parameters, index-aligned with <see cref="GraphNode.Inputs"/>.</summary>
    public sealed record PortBlend(BlendMode BlendMode, float Opacity);

    private readonly List<PortBlend> _portParams = [new(BlendMode.Normal, 1f)];

    public MultilayerNode(double x, double y)
        : base("Multilayer", x, y)
    {
        AddLayerPort();
        AddOutputPort("Result", SignalType.Color2D);
    }

    internal MultilayerNode(Guid id, double x, double y)
        : base(id, "Multilayer", x, y)
    {
        AddLayerPort();
        AddOutputPort("Result", SignalType.Color2D);
    }

    public override NodeType Type => NodeType.Multilayer;

    /// <summary>Per-input parameters, index-aligned with <see cref="Inputs"/>.</summary>
    public IReadOnlyList<PortBlend> PortParams => _portParams;

    /// <summary>The parameters for input port <paramref name="index"/>.</summary>
    public PortBlend GetPortParams(int index) => _portParams[index];

    /// <summary>
    /// Sets the blend mode + opacity for input port <paramref name="index"/>.
    /// Raises <see cref="Changed"/> when the value changes.
    /// </summary>
    public void SetPortParams(int index, BlendMode blendMode, float opacity)
    {
        if (index < 0 || index >= _portParams.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index,
                $"Node '{Name}' has {_portParams.Count} input ports.");
        PortBlend current = _portParams[index];
        if (current.BlendMode == blendMode && current.Opacity == opacity)
            return;
        _portParams[index] = new PortBlend(blendMode, opacity);
        OnChanged();
    }

    /// <summary>
    /// Appends a new topmost input port (<c>LayerN</c>) with default
    /// parameters. Called by the graph when a connection is made to the
    /// current topmost port. Raises <see cref="Changed"/>.
    /// </summary>
    public void AddInputPort()
    {
        AddLayerPort();
        _portParams.Add(new PortBlend(BlendMode.Normal, 1f));
        OnChanged();
    }

    /// <summary>
    /// Removes the topmost input port. The first port (<c>Layer0</c>) is never
    /// removed (the node keeps at least one input). Called by the graph when
    /// the topmost connected layer is disconnected, so the freed port becomes
    /// the new empty topmost slot. Raises <see cref="Changed"/> when a port is
    /// actually removed.
    /// </summary>
    public void RemoveLastInputPort()
    {
        if (Inputs.Count <= 1)
            return;
        RemoveInputPort(Inputs.Count - 1);
        _portParams.RemoveAt(_portParams.Count - 1);
        OnChanged();
    }

    /// <summary>
    /// Sets the exact number of input ports (used by document loading, which
    /// reconstructs ports before connections are re-added). Raises
    /// <see cref="Changed"/> when the count changes.
    /// </summary>
    internal void EnsureInputPorts(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count,
                "A Multilayer keeps at least one input port.");
        while (Inputs.Count < count)
            AddInputPort();
        while (Inputs.Count > count)
            RemoveLastInputPort();
    }

    private void AddLayerPort()
    {
        AddInputPort(
            $"Layer{Inputs.Count}", SignalType.Color2D,
            SignalType.Color2D, SignalType.Scalar, SignalType.Scalar2D);
    }

    internal override PixelBuffer EvaluateNode(NodeEvaluationContext ctx)
    {
        // Fold bottom-up: the first connected port is the base; each higher
        // connected port blends on top with its own mode/opacity. Unconnected
        // ports are skipped (see class remarks).
        PixelBuffer? result = null;
        int count = Inputs.Count;
        for (int i = 0; i < count; i++)
        {
            if (!ctx.IsInputConnected(i))
                continue;
            PixelBuffer input = ctx.GetInput(i);
            if (result is null)
            {
                result = input;
            }
            else
            {
                PortBlend p = _portParams[i];
                result = Blend.Apply(result, input, p.BlendMode, p.Opacity);
            }
        }
        return result ?? PixelBuffer.Empty(ctx.Width, ctx.Height);
    }
}

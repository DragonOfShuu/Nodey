using Nodey.Core.Graph;
using Nodey.Core.Image;
using Nodey.Core.Types;
using GraphModel = Nodey.Core.Graph.Graph;

namespace Nodey.Core.Tests;

/// <summary>
/// Tests the graph model: node catalog, connection rules, traversal, and the
/// Multilayer's dynamic ports (PLAN.md §4–5).
/// </summary>
public class GraphTests
{
    // -- catalog ----------------------------------------------------------------

    [Fact]
    public void Catalog_NodePortsMatchSpec()
    {
        var g = new GraphModel();
        void Check(GraphNode n, int inputs, SignalType outputType)
        {
            Assert.Equal(inputs, n.Inputs.Count);
            Assert.Single(n.Outputs);
            Assert.Equal(outputType, n.OutputType);
        }
        Check(g.AddNode(NodeType.Layer, 0, 0), 0, SignalType.Color2D);
        Check(g.AddNode(NodeType.Noise, 0, 0), 0, SignalType.Scalar2D);
        Check(g.AddNode(NodeType.ColorRamp, 0, 0), 1, SignalType.Color2D);
        Check(g.AddNode(NodeType.Mix, 0, 0), 2, SignalType.Color2D);
        Check(g.AddNode(NodeType.Multilayer, 0, 0), 1, SignalType.Color2D);
        Check(g.AddNode(NodeType.Filter, 0, 0), 1, SignalType.Color2D);
        Check(g.AddNode(NodeType.Math, 0, 0), 2, SignalType.Scalar2D);
        Check(g.AddNode(NodeType.Color, 0, 0), 0, SignalType.Color2D);
    }

    [Fact]
    public void AddNode_GeneratesUniqueNames()
    {
        var g = new GraphModel();
        GraphNode a = g.AddNode(NodeType.Noise, 0, 0);
        GraphNode b = g.AddNode(NodeType.Noise, 0, 0);
        Assert.Equal("Noise", a.Name);
        Assert.Equal("Noise 2", b.Name);
    }

    [Fact]
    public void AddNode_Layer_CreatesDocumentSizedBuffer()
    {
        var g = new GraphModel();
        g.Width = 64;
        g.Height = 32;
        GraphNode layer = g.AddNode(NodeType.Layer, 0, 0);
        PixelBuffer buffer = g.GetLayerBuffer(layer.Id);
        Assert.Equal(64, buffer.Width);
        Assert.Equal(32, buffer.Height);
    }

    [Fact]
    public void Width_Height_MustBePositive()
    {
        var g = new GraphModel();
        Assert.Throws<ArgumentOutOfRangeException>(() => g.Width = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => g.Height = -1);
    }

    // -- CanConnect rules (PLAN.md §5.3) ---------------------------------------

    [Fact]
    public void CanConnect_Self_Rejected()
    {
        var g = new GraphModel();
        GraphNode mix = g.AddNode(NodeType.Mix, 0, 0);
        Assert.False(g.CanConnect(mix.Outputs[0], mix.GetInputPort(0), out _));
    }

    [Fact]
    public void CanConnect_WrongDirection_Rejected()
    {
        var g = new GraphModel();
        GraphNode a = g.AddNode(NodeType.Mix, 0, 0);
        GraphNode b = g.AddNode(NodeType.Mix, 0, 0);
        // input -> input
        Assert.False(g.CanConnect(a.GetInputPort(0), b.GetInputPort(0), out _));
        // output -> output
        Assert.False(g.CanConnect(a.Outputs[0], b.Outputs[0], out _));
    }

    [Fact]
    public void CanConnect_TypeMismatch_Rejected()
    {
        var g = new GraphModel();
        GraphNode noise = g.AddNode(NodeType.Noise, 0, 0);   // Scalar2D output
        GraphNode filter = g.AddNode(NodeType.Filter, 0, 0); // accepts Color2D only
        Assert.False(g.CanConnect(noise.Outputs[0], filter.GetInputPort(0), out string reason));
        Assert.Contains("cannot connect", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanConnect_Color2DToScalar2DInput_Rejected()
    {
        var g = new GraphModel();
        GraphNode color = g.AddNode(NodeType.Color, 0, 0);  // Color2D output
        GraphNode math = g.AddNode(NodeType.Math, 0, 0);    // accepts Scalar/Scalar2D
        Assert.False(g.CanConnect(color.Outputs[0], math.GetInputPort(0), out _));
    }

    [Fact]
    public void CanConnect_Valid_Accepted()
    {
        var g = new GraphModel();
        GraphNode color = g.AddNode(NodeType.Color, 0, 0);
        GraphNode mix = g.AddNode(NodeType.Mix, 0, 0);
        Assert.True(g.CanConnect(color.Outputs[0], mix.GetInputPort(0), out _));
    }

    [Fact]
    public void CanConnect_Duplicate_Rejected()
    {
        var g = new GraphModel();
        GraphNode color = g.AddNode(NodeType.Color, 0, 0);
        GraphNode mix = g.AddNode(NodeType.Mix, 0, 0);
        g.Connect(color.Outputs[0], mix.GetInputPort(0));
        Assert.False(g.CanConnect(color.Outputs[0], mix.GetInputPort(0), out _));
    }

    [Fact]
    public void CanConnect_Cycle_Rejected()
    {
        var g = new GraphModel();
        GraphNode a = g.AddNode(NodeType.Mix, 0, 0);
        GraphNode b = g.AddNode(NodeType.Mix, 0, 0);
        GraphNode c = g.AddNode(NodeType.Mix, 0, 0);
        g.Connect(a.Outputs[0], b.GetInputPort(0));
        g.Connect(b.Outputs[0], c.GetInputPort(0));
        Assert.False(g.CanConnect(c.Outputs[0], a.GetInputPort(0), out string reason));
        Assert.Contains("cycle", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Connect_Invalid_ReturnsNull()
    {
        var g = new GraphModel();
        GraphNode noise = g.AddNode(NodeType.Noise, 0, 0);
        GraphNode filter = g.AddNode(NodeType.Filter, 0, 0);
        Assert.Null(g.Connect(noise.Outputs[0], filter.GetInputPort(0)));
        Assert.Empty(g.Connections);
    }

    // -- Connect / Disconnect ---------------------------------------------------

    [Fact]
    public void Connect_ReplacesExistingTargetConnection()
    {
        var g = new GraphModel();
        GraphNode c1 = g.AddNode(NodeType.Color, 0, 0);
        GraphNode c2 = g.AddNode(NodeType.Color, 0, 0);
        GraphNode mix = g.AddNode(NodeType.Mix, 0, 0);
        g.Connect(c1.Outputs[0], mix.GetInputPort(0));
        g.Connect(c2.Outputs[0], mix.GetInputPort(0));
        Assert.Single(g.Connections);
        Assert.True(g.HasConnection(c2.Outputs[0].Id, mix.GetInputPort(0).Id));
        Assert.False(g.HasConnection(c1.Outputs[0].Id, mix.GetInputPort(0).Id));
    }

    [Fact]
    public void Disconnect_RemovesConnection()
    {
        var g = new GraphModel();
        GraphNode c = g.AddNode(NodeType.Color, 0, 0);
        GraphNode mix = g.AddNode(NodeType.Mix, 0, 0);
        GraphConnection? conn = g.Connect(c.Outputs[0], mix.GetInputPort(0));
        Assert.NotNull(conn);
        g.Disconnect(conn!.Id);
        Assert.Empty(g.Connections);
        // Disconnecting an unknown id is a no-op.
        g.Disconnect(Guid.NewGuid());
    }

    // -- Multilayer dynamic ports -----------------------------------------------

    [Fact]
    public void Multilayer_GrowsWhenTopmostConnected()
    {
        var g = new GraphModel();
        MultilayerNode multi = (MultilayerNode)g.AddNode(NodeType.Multilayer, 0, 0);
        GraphNode color = g.AddNode(NodeType.Color, 0, 0);
        Assert.Single(multi.Inputs);
        g.Connect(color.Outputs[0], multi.GetInputPort(0));
        Assert.Equal(2, multi.Inputs.Count);
        Assert.True(g.IsConnected(multi.GetInputPort(0)));
        Assert.False(g.IsConnected(multi.GetInputPort(1)));
    }

    [Fact]
    public void Multilayer_ShrinksWhenTopmostLayerDisconnected()
    {
        var g = new GraphModel();
        MultilayerNode multi = (MultilayerNode)g.AddNode(NodeType.Multilayer, 0, 0);
        GraphNode c1 = g.AddNode(NodeType.Color, 0, 0);
        GraphNode c2 = g.AddNode(NodeType.Color, 0, 0);
        GraphConnection? conn1 = g.Connect(c1.Outputs[0], multi.GetInputPort(0));
        GraphConnection? conn2 = g.Connect(c2.Outputs[0], multi.GetInputPort(1));
        Assert.Equal(3, multi.Inputs.Count);

        // Disconnect the topmost connected layer (port 1): the empty topmost
        // slot is dropped and port 1 becomes the new empty topmost slot.
        g.Disconnect(conn2!.Id);
        Assert.Equal(2, multi.Inputs.Count);
        Assert.True(g.IsConnected(multi.GetInputPort(0)));
        Assert.False(g.IsConnected(multi.GetInputPort(1)));
    }

    [Fact]
    public void Multilayer_NeverShrinksBelowOnePort()
    {
        var g = new GraphModel();
        MultilayerNode multi = (MultilayerNode)g.AddNode(NodeType.Multilayer, 0, 0);
        GraphNode c = g.AddNode(NodeType.Color, 0, 0);
        GraphConnection? conn = g.Connect(c.Outputs[0], multi.GetInputPort(0));
        Assert.Equal(2, multi.Inputs.Count);
        g.Disconnect(conn!.Id);
        // One connected port freed -> the empty topmost slot is dropped,
        // leaving the single (now empty) Layer0.
        Assert.Single(multi.Inputs);
        Assert.False(g.IsConnected(multi.GetInputPort(0)));
    }

    // -- traversal --------------------------------------------------------------

    [Fact]
    public void DownstreamAndUpstream()
    {
        var g = new GraphModel();
        GraphNode a = g.AddNode(NodeType.Mix, 0, 0);
        GraphNode b = g.AddNode(NodeType.Mix, 0, 0);
        GraphNode c = g.AddNode(NodeType.Mix, 0, 0);
        g.Connect(a.Outputs[0], b.GetInputPort(0));
        g.Connect(b.Outputs[0], c.GetInputPort(0));

        List<GraphNode> down = g.DownstreamOf(a.Id).ToList();
        Assert.Contains(b, down);
        Assert.Contains(c, down);
        Assert.DoesNotContain(a, down);

        List<GraphNode> up = g.UpstreamOf(c.Id).ToList();
        Assert.Contains(a, up);
        Assert.Contains(b, up);
        Assert.DoesNotContain(c, up);
    }

    // -- FindOutputNode ---------------------------------------------------------

    [Fact]
    public void FindOutputNode_RightmostWithNoOutgoing()
    {
        var g = new GraphModel();
        GraphNode a = g.AddNode(NodeType.Mix, 100, 0);
        GraphNode b = g.AddNode(NodeType.Mix, 300, 0);
        GraphNode c = g.AddNode(NodeType.Mix, 200, 0);
        g.Connect(a.Outputs[0], b.GetInputPort(0)); // a has an outgoing edge
        Assert.Same(b, g.FindOutputNode());
    }

    [Fact]
    public void FindOutputNode_Tie_MostRecentlyAdded()
    {
        var g = new GraphModel();
        GraphNode a = g.AddNode(NodeType.Mix, 100, 0);
        GraphNode b = g.AddNode(NodeType.Mix, 100, 0);
        Assert.Same(b, g.FindOutputNode());
    }

    [Fact]
    public void FindOutputNode_EmptyGraph_Null()
    {
        var g = new GraphModel();
        Assert.Null(g.FindOutputNode());
    }

    // -- RemoveNode -------------------------------------------------------------

    [Fact]
    public void RemoveNode_RemovesItsConnections()
    {
        var g = new GraphModel();
        GraphNode a = g.AddNode(NodeType.Color, 0, 0);
        GraphNode b = g.AddNode(NodeType.Mix, 0, 0);
        g.Connect(a.Outputs[0], b.GetInputPort(0));
        Assert.Single(g.Connections);
        g.RemoveNode(a.Id);
        Assert.Empty(g.Connections);
        Assert.DoesNotContain(g.Nodes, n => n.Id == a.Id);
    }

    // -- change notification ----------------------------------------------------

    [Fact]
    public void StructuralChanges_RaiseChanged()
    {
        var g = new GraphModel();
        int count = 0;
        g.Changed += (_, _) => count++;
        g.AddNode(NodeType.Noise, 0, 0);
        Assert.Equal(1, count);
        g.Width = 64;
        Assert.Equal(2, count);
    }

    [Fact]
    public void ParameterChange_MarksNodeAndDownstreamDirty()
    {
        var g = new GraphModel();
        GraphNode a = g.AddNode(NodeType.Mix, 0, 0);
        GraphNode b = g.AddNode(NodeType.Mix, 0, 0);
        g.Connect(a.Outputs[0], b.GetInputPort(0));
        var evaluator = new GraphEvaluator(g);
        evaluator.Evaluate(); // clears all dirty

        Assert.False(a.IsDirty);
        Assert.False(b.IsDirty);

        ((MixNode)a).BlendMode = BlendMode.Multiply; // parameter change on a
        Assert.True(a.IsDirty);
        Assert.True(b.IsDirty);
    }
}

using Nodey.Core.Document;
using Nodey.Core.Graph;
using Nodey.Core.Image;
using Nodey.Core.Image.Filters;
using Nodey.Core.Image.Noise;

namespace Nodey.Core.Tests;

/// <summary>
/// Tests document save/load round-trips (PLAN.md §6): structure, parameters,
/// layer pixels, node/connection id preservation, and byte-identical re-save.
/// </summary>
public class DocumentTests : IDisposable
{
    private readonly string _dir =
        Directory.CreateTempSubdirectory("nodey-doc-test-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string Path(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public void RoundTrip_PreservesStructureAndParams()
    {
        // -- build a rich document -------------------------------------------
        var doc = new NodeyDocument { Name = "Test Doc" };
        doc.Width = 8;
        doc.Height = 8;

        GraphNode layer = doc.Graph.AddNode(NodeType.Layer, 10, 20);
        var layerBuffer = PixelBuffer.Solid(8, 8, 0.25f, 0.5f, 0.75f);
        layerBuffer.SetPixel(0, 0, new Rgba(1f, 0f, 0f));
        layerBuffer.SetPixel(7, 7, new Rgba(0f, 1f, 0f));
        doc.Graph.SetLayerBuffer(layer.Id, layerBuffer);

        NoiseNode noise = (NoiseNode)doc.Graph.AddNode(NodeType.Noise, 40, 20);
        noise.Algorithm = NoiseAlgorithm.Value;
        noise.Scale = 0.05f;
        noise.Octaves = 6;
        noise.Seed = 42;
        noise.OffsetX = 1.5f;
        noise.OffsetY = -2f;

        ColorRampNode ramp = (ColorRampNode)doc.Graph.AddNode(NodeType.ColorRamp, 80, 20);
        ramp.SetStops(new[]
        {
            new RampStop(0f, 1f, 0f, 0f),
            new RampStop(0.5f, 0f, 1f, 0f),
            new RampStop(1f, 0f, 0f, 1f),
        });

        MixNode mix = (MixNode)doc.Graph.AddNode(NodeType.Mix, 120, 20);
        mix.BlendMode = BlendMode.Screen;
        mix.Opacity = 0.75f;

        FilterNode filter = (FilterNode)doc.Graph.AddNode(NodeType.Filter, 160, 20);
        filter.Kind = FilterKind.LensBlur;
        filter.Radius = 3f;
        filter.Aperture = 0.5f;

        MathNode math = (MathNode)doc.Graph.AddNode(NodeType.Math, 200, 20);
        math.Operation = MathOperation.Subtract;

        ColorNode color = (ColorNode)doc.Graph.AddNode(NodeType.Color, 240, 20);
        color.ColorR = 0.1f; color.ColorG = 0.2f; color.ColorB = 0.3f; color.ColorA = 0.4f;

        MultilayerNode multi = (MultilayerNode)doc.Graph.AddNode(NodeType.Multilayer, 280, 20);
        ColorNode m1 = (ColorNode)doc.Graph.AddNode(NodeType.Color, 320, 20);
        ColorNode m2 = (ColorNode)doc.Graph.AddNode(NodeType.Color, 320, 60);
        m1.ColorR = 1f; m1.ColorG = 0f; m1.ColorB = 0f;
        m2.ColorR = 0f; m2.ColorG = 1f; m2.ColorB = 0f;
        doc.Graph.Connect(m1.Outputs[0], multi.GetInputPort(0));
        doc.Graph.Connect(m2.Outputs[0], multi.GetInputPort(1));
        multi.SetPortParams(1, BlendMode.Overlay, 0.6f);

        // wire a few edges
        doc.Graph.Connect(noise.Outputs[0], ramp.GetInputPort(0));
        doc.Graph.Connect(ramp.Outputs[0], mix.GetInputPort(0));
        doc.Graph.Connect(color.Outputs[0], mix.GetInputPort(1));
        doc.Graph.Connect(mix.Outputs[0], filter.GetInputPort(0));

        string p = Path("rich.nodey");
        doc.Save(p);

        // -- load and compare ------------------------------------------------
        NodeyDocument loaded = NodeyDocument.Load(p);
        Assert.Equal("Test Doc", loaded.Name);
        Assert.Equal(1, loaded.Version);
        Assert.Equal(8, loaded.Width);
        Assert.Equal(8, loaded.Height);
        Assert.Equal(doc.Graph.Nodes.Count, loaded.Graph.Nodes.Count);
        Assert.Equal(doc.Graph.Connections.Count, loaded.Graph.Connections.Count);

        // Node ids, types, positions, names are preserved.
        foreach (GraphNode n in doc.Graph.Nodes)
        {
            GraphNode ln = loaded.Graph.FindNode(n.Id)!;
            Assert.NotNull(ln);
            Assert.Equal(n.Type, ln.Type);
            Assert.Equal(n.Name, ln.Name);
            Approx.Near((float)n.PositionX, (float)ln.PositionX);
            Approx.Near((float)n.PositionY, (float)ln.PositionY);
        }

        // Parameters round-trip.
        NoiseNode lnoise = (NoiseNode)loaded.Graph.FindNode(noise.Id)!;
        Assert.Equal(NoiseAlgorithm.Value, lnoise.Algorithm);
        Assert.Equal(6, lnoise.Octaves);
        Assert.Equal(42, lnoise.Seed);
        Approx.Near(0.05f, lnoise.Scale);

        ColorRampNode lramp = (ColorRampNode)loaded.Graph.FindNode(ramp.Id)!;
        Assert.Equal(3, lramp.Stops.Count);
        Approx.Near(0.5f, lramp.Stops[1].Position);

        MixNode lmix = (MixNode)loaded.Graph.FindNode(mix.Id)!;
        Assert.Equal(BlendMode.Screen, lmix.BlendMode);
        Approx.Near(0.75f, lmix.Opacity);

        FilterNode lfilter = (FilterNode)loaded.Graph.FindNode(filter.Id)!;
        Assert.Equal(FilterKind.LensBlur, lfilter.Kind);
        Approx.Near(0.5f, lfilter.Aperture);

        MathNode lmath = (MathNode)loaded.Graph.FindNode(math.Id)!;
        Assert.Equal(MathOperation.Subtract, lmath.Operation);

        ColorNode lcolor = (ColorNode)loaded.Graph.FindNode(color.Id)!;
        Approx.Near(0.1f, lcolor.ColorR);
        Approx.Near(0.4f, lcolor.ColorA);

        MultilayerNode lmulti = (MultilayerNode)loaded.Graph.FindNode(multi.Id)!;
        Assert.Equal(multi.Inputs.Count, lmulti.Inputs.Count);
        Assert.Equal(BlendMode.Overlay, lmulti.GetPortParams(1).BlendMode);
        Approx.Near(0.6f, lmulti.GetPortParams(1).Opacity);

        // Layer pixels round-trip (8-bit, so compare with 1/255 tolerance).
        GraphNode llayer = loaded.Graph.FindNode(layer.Id)!;
        PixelBuffer lbuf = loaded.Graph.GetLayerBuffer(llayer.Id);
        Assert.Equal(8, lbuf.Width);
        Assert.Equal(8, lbuf.Height);
        Rgba c00 = lbuf.GetPixel(0, 0);
        Approx.Near(1f, c00.R);
        Approx.Near(0f, c00.G);
        Rgba c77 = lbuf.GetPixel(7, 7);
        Approx.Near(1f, c77.G);
        Rgba mid = lbuf.GetPixel(3, 3);
        Approx.Near(0.25f, mid.R, 1f / 255f);
        Approx.Near(0.5f, mid.G, 1f / 255f);
        Approx.Near(0.75f, mid.B, 1f / 255f);
    }

    [Fact]
    public void RoundTrip_SaveLoadSave_IsByteIdentical()
    {
        var doc = new NodeyDocument { Name = "Stable" };
        doc.Width = 4;
        doc.Height = 4;
        NoiseNode noise = (NoiseNode)doc.Graph.AddNode(NodeType.Noise, 0, 0);
        noise.Seed = 7;
        MixNode mix = (MixNode)doc.Graph.AddNode(NodeType.Mix, 100, 0);
        doc.Graph.Connect(noise.Outputs[0], mix.GetInputPort(0));

        string p1 = Path("a.nodey");
        string p2 = Path("b.nodey");
        doc.Save(p1);
        NodeyDocument loaded = NodeyDocument.Load(p1);
        loaded.Save(p2);

        Assert.Equal(File.ReadAllText(p1), File.ReadAllText(p2));
    }

    [Fact]
    public void RoundTrip_ConnectionIdsPreserved()
    {
        var doc = new NodeyDocument();
        doc.Width = 4;
        doc.Height = 4;
        NoiseNode noise = (NoiseNode)doc.Graph.AddNode(NodeType.Noise, 0, 0);
        MixNode mix = (MixNode)doc.Graph.AddNode(NodeType.Mix, 100, 0);
        GraphConnection? conn = doc.Graph.Connect(noise.Outputs[0], mix.GetInputPort(0));
        Assert.NotNull(conn);

        string p = Path("conn.nodey");
        doc.Save(p);
        NodeyDocument loaded = NodeyDocument.Load(p);
        Assert.Contains(loaded.Graph.Connections, c => c.Id == conn!.Id);
    }
}

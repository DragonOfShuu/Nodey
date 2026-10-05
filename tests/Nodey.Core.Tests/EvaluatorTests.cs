using Nodey.Core.Graph;
using Nodey.Core.Image;
using Nodey.Core.Image.Filters;
using Nodey.Core.Types;
using GraphModel = Nodey.Core.Graph.Graph;

namespace Nodey.Core.Tests;

/// <summary>
/// Tests the incremental evaluation engine (PLAN.md §5): cross-type
/// translation, per-node caching, dirty propagation, and the NodeEvaluated
/// event. All tests use a small document (4x4) for speed.
/// </summary>
public class EvaluatorTests
{
    private static (GraphModel g, GraphEvaluator eval) NewGraph()
    {
        var g = new GraphModel();
        g.Width = 4;
        g.Height = 4;
        return (g, new GraphEvaluator(g));
    }

    // -- Math -------------------------------------------------------------------

    [Fact]
    public void Math_Constants_DivideByZeroReturnsZero()
    {
        (var g, var eval) = NewGraph();
        MathNode math = (MathNode)g.AddNode(NodeType.Math, 0, 0);
        math.Operation = MathOperation.Divide;
        eval.SetConstantValue(math.GetInputPort(0), 1.0); // A
        eval.SetConstantValue(math.GetInputPort(1), 0.5); // B
        eval.Evaluate();
        float[] d = eval.GetResult(math.Id).Rgba;
        for (int i = 0; i < d.Length; i += 4)
            Assert.Equal(2.0f, d[i], 3); // 1 / 0.5

        eval.SetConstantValue(math.GetInputPort(1), 0.0); // divide by zero
        eval.Evaluate();
        float[] d2 = eval.GetResult(math.Id).Rgba;
        for (int i = 0; i < d2.Length; i += 4)
            Assert.Equal(0.0f, d2[i], 3);
    }

    // -- ColorRamp --------------------------------------------------------------

    [Fact]
    public void ColorRamp_ConstantMidpoint_Interpolates()
    {
        (var g, var eval) = NewGraph();
        ColorRampNode ramp = (ColorRampNode)g.AddNode(NodeType.ColorRamp, 0, 0);
        eval.SetConstantValue(ramp.GetInputPort(0), 0.5);
        eval.Evaluate();
        Rgba px = eval.GetResult(ramp.Id).GetPixel(0, 0);
        Approx.Near(0.5f, px.R);
        Approx.Near(0.5f, px.G);
        Approx.Near(0.5f, px.B);
        Approx.Near(1f, px.A);
    }

    [Fact]
    public void ColorRamp_CustomStops_MapsCorrectly()
    {
        (var g, var eval) = NewGraph();
        ColorRampNode ramp = (ColorRampNode)g.AddNode(NodeType.ColorRamp, 0, 0);
        ramp.SetStops(new[]
        {
            new RampStop(0f, 1f, 0f, 0f), // red at 0
            new RampStop(1f, 0f, 0f, 1f), // blue at 1
        });
        eval.SetConstantValue(ramp.GetInputPort(0), 0.25f);
        eval.Evaluate();
        Rgba px = eval.GetResult(ramp.Id).GetPixel(0, 0);
        Approx.Near(0.75f, px.R);
        Approx.Near(0.25f, px.B);
    }

    // -- Noise ------------------------------------------------------------------

    [Fact]
    public void Noise_SameSeed_IsDeterministic()
    {
        (var g, var eval) = NewGraph();
        NoiseNode a = (NoiseNode)g.AddNode(NodeType.Noise, 0, 0);
        a.Seed = 12345;
        NoiseNode b = (NoiseNode)g.AddNode(NodeType.Noise, 0, 0);
        b.Seed = 12345;
        eval.Evaluate();
        float[] da = eval.GetResult(a.Id).Rgba;
        float[] db = eval.GetResult(b.Id).Rgba;
        for (int i = 0; i < da.Length; i++)
            Assert.Equal(da[i], db[i], 5);
    }

    [Fact]
    public void Noise_DifferentSeed_Differs()
    {
        (var g, var eval) = NewGraph();
        NoiseNode a = (NoiseNode)g.AddNode(NodeType.Noise, 0, 0);
        a.Seed = 1;
        NoiseNode b = (NoiseNode)g.AddNode(NodeType.Noise, 0, 0);
        b.Seed = 2;
        eval.Evaluate();
        float[] da = eval.GetResult(a.Id).Rgba;
        float[] db = eval.GetResult(b.Id).Rgba;
        bool differs = false;
        for (int i = 0; i < da.Length; i++)
        {
            if (System.Math.Abs(da[i] - db[i]) > Approx.Eps)
            {
                differs = true;
                break;
            }
        }
        Assert.True(differs);
    }

    // -- Mix --------------------------------------------------------------------

    [Fact]
    public void Mix_Normal_OpacityOne_ReplacesBase()
    {
        (var g, var eval) = NewGraph();
        MixNode mix = (MixNode)g.AddNode(NodeType.Mix, 0, 0);
        mix.BlendMode = BlendMode.Normal;
        mix.Opacity = 1f;
        eval.SetConstantValue(mix.GetInputPort(0), 0.8); // Base
        eval.SetConstantValue(mix.GetInputPort(1), 0.2); // Blend
        eval.Evaluate();
        Rgba px = eval.GetResult(mix.Id).GetPixel(0, 0);
        Approx.Near(0.2f, px.R);
        Approx.Near(0.2f, px.G);
        Approx.Near(0.2f, px.B);
    }

    [Fact]
    public void Mix_Multiply_Blends()
    {
        (var g, var eval) = NewGraph();
        MixNode mix = (MixNode)g.AddNode(NodeType.Mix, 0, 0);
        mix.BlendMode = BlendMode.Multiply;
        mix.Opacity = 1f;
        eval.SetConstantValue(mix.GetInputPort(0), 0.5); // Base
        eval.SetConstantValue(mix.GetInputPort(1), 0.5); // Blend
        eval.Evaluate();
        Rgba px = eval.GetResult(mix.Id).GetPixel(0, 0);
        Approx.Near(0.25f, px.R);
    }

    // -- Multilayer -------------------------------------------------------------

    [Fact]
    public void Multilayer_FoldsConnectedLayers()
    {
        (var g, var eval) = NewGraph();
        MultilayerNode multi = (MultilayerNode)g.AddNode(NodeType.Multilayer, 0, 0);
        ColorNode red = (ColorNode)g.AddNode(NodeType.Color, 0, 0);
        red.ColorR = 1f; red.ColorG = 0f; red.ColorB = 0f;
        ColorNode blue = (ColorNode)g.AddNode(NodeType.Color, 0, 0);
        blue.ColorR = 0f; blue.ColorG = 0f; blue.ColorB = 1f;
        g.Connect(red.Outputs[0], multi.GetInputPort(0));
        g.Connect(blue.Outputs[0], multi.GetInputPort(1));
        multi.SetPortParams(1, BlendMode.Normal, 0.5f);
        eval.Evaluate();
        Rgba px = eval.GetResult(multi.Id).GetPixel(0, 0);
        Approx.Near(0.5f, px.R);
        Approx.Near(0f, px.G);
        Approx.Near(0.5f, px.B);
    }

    [Fact]
    public void Multilayer_SkipsUnconnectedTopPort()
    {
        (var g, var eval) = NewGraph();
        MultilayerNode multi = (MultilayerNode)g.AddNode(NodeType.Multilayer, 0, 0);
        ColorNode red = (ColorNode)g.AddNode(NodeType.Color, 0, 0);
        red.ColorR = 1f; red.ColorG = 0f; red.ColorB = 0f;
        g.Connect(red.Outputs[0], multi.GetInputPort(0)); // grows to 2 ports
        eval.Evaluate();
        // The empty top port must be skipped, not blended as a transparent
        // default (which would wipe the red to black under Normal/1.0).
        Rgba px = eval.GetResult(multi.Id).GetPixel(0, 0);
        Approx.Near(1f, px.R);
        Approx.Near(0f, px.G);
        Approx.Near(0f, px.B);
    }

    // -- Filter -----------------------------------------------------------------

    [Fact]
    public void Filter_GaussianBlurOfSolid_IsStable()
    {
        (var g, var eval) = NewGraph();
        FilterNode filter = (FilterNode)g.AddNode(NodeType.Filter, 0, 0);
        filter.Kind = FilterKind.GaussianBlur;
        filter.Amount = 1f;
        filter.Radius = 2f;
        ColorNode gray = (ColorNode)g.AddNode(NodeType.Color, 0, 0);
        gray.ColorR = 0.5f; gray.ColorG = 0.5f; gray.ColorB = 0.5f;
        g.Connect(gray.Outputs[0], filter.GetInputPort(0));
        eval.Evaluate();
        Rgba px = eval.GetResult(filter.Id).GetPixel(0, 0);
        Approx.Near(0.5f, px.R);
        Approx.Near(0.5f, px.G);
        Approx.Near(0.5f, px.B);
    }

    // -- caching / dirty --------------------------------------------------------

    [Fact]
    public void Evaluate_SkipsCleanNodes()
    {
        (var g, var eval) = NewGraph();
        NoiseNode noise = (NoiseNode)g.AddNode(NodeType.Noise, 0, 0);
        int evaluations = 0;
        eval.NodeEvaluated += (_, _) => evaluations++;

        eval.Evaluate(); // all dirty
        int firstPass = evaluations;
        Assert.Equal(1, firstPass);

        eval.Evaluate(); // nothing changed -> no re-evaluation
        Assert.Equal(1, evaluations);

        noise.Seed = 999; // dirty only the noise node
        eval.Evaluate();
        Assert.Equal(2, evaluations);
    }

    [Fact]
    public void GetOutput_ReturnsRightmostNodeResult()
    {
        (var g, var eval) = NewGraph();
        ColorNode color = (ColorNode)g.AddNode(NodeType.Color, 0, 0);
        color.ColorR = 0.3f; color.ColorG = 0.4f; color.ColorB = 0.5f;
        eval.Evaluate();
        Rgba px = eval.GetOutput().GetPixel(0, 0);
        Approx.Near(0.3f, px.R);
        Approx.Near(0.4f, px.G);
        Approx.Near(0.5f, px.B);
    }

    [Fact]
    public void GetResult_UnEvaluatedNode_Throws()
    {
        (var g, var eval) = NewGraph();
        NoiseNode noise = (NoiseNode)g.AddNode(NodeType.Noise, 0, 0);
        // No Evaluate() call yet.
        Assert.Throws<InvalidOperationException>(() => eval.GetResult(noise.Id));
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Nodey.Core.Graph;
using Nodey.Core.Image;
using Nodey.Core.Image.Filters;
using Nodey.Core.Image.Noise;

namespace Nodey.Core.Document;

/// <summary>
/// A savable Nodey document: a named <see cref="Graph"/> plus metadata,
/// serialized to a JSON file (PLAN.md §6). This is the v1 persistence model —
/// one file per document.
/// </summary>
/// <remarks>
/// The format is JSON (camelCase, enums as strings). Connections are stored by
/// node id + port <b>index</b> (stable across save/load, unlike runtime port
/// Guids); node ids and connection ids are preserved so a save → load → save
/// round-trip is byte-identical. Layer pixels are stored as base64 8-bit RGBA.
/// </remarks>
public sealed class NodeyDocument
{
    /// <summary>Format version (1).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Document display name.</summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>The node graph.</summary>
    public Nodey.Core.Graph.Graph Graph { get; } = new();

    /// <summary>Document width (mirrors <see cref="Graph.Width"/>).</summary>
    public int Width
    {
        get => Graph.Width;
        set => Graph.Width = value;
    }

    /// <summary>Document height (mirrors <see cref="Graph.Height"/>).</summary>
    public int Height
    {
        get => Graph.Height;
        set => Graph.Height = value;
    }

    /// <summary>
    /// Saves the document to <paramref name="path"/> as JSON (creating parent
    /// directories as needed).
    /// </summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        DocumentDto dto = ToDto();
        string json = JsonSerializer.Serialize(dto, SerializerOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads a document from <paramref name="path"/> into a fresh
    /// <see cref="NodeyDocument"/>, reconstructing nodes (with their stored
    /// ids), parameters, layer pixels, and connections (with their stored ids).
    /// </summary>
    public static NodeyDocument Load(string path)
    {
        string json = File.ReadAllText(path);
        DocumentDto dto = JsonSerializer.Deserialize<DocumentDto>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Empty document file.");
        return FromDto(dto);
    }

    // -- (de)serialization ---------------------------------------------------------

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private DocumentDto ToDto()
    {
        var dto = new DocumentDto
        {
            Version = Version,
            Name = Name,
            Width = Graph.Width,
            Height = Graph.Height,
        };
        foreach (GraphNode n in Graph.Nodes)
        {
            var nd = new NodeDto
            {
                Id = n.Id,
                Type = n.Type,
                Name = n.Name,
                X = n.PositionX,
                Y = n.PositionY,
                Params = SerializeParams(n),
            };
            if (n is LayerNode)
            {
                PixelBuffer layer = Graph.GetLayerBuffer(n.Id);
                nd.Layer = new LayerDataDto
                {
                    Width = layer.Width,
                    Height = layer.Height,
                    Data = Convert.ToBase64String(layer.ToRgba8()),
                };
            }
            dto.Nodes.Add(nd);
        }
        foreach (GraphConnection c in Graph.Connections)
        {
            dto.Connections.Add(new ConnectionDto
            {
                Id = c.Id,
                SourceNode = Graph.NodeOfPort(c.SourcePortId).Id,
                SourcePort = Graph.PortIndexOf(c.SourcePortId),
                TargetNode = Graph.NodeOfPort(c.TargetPortId).Id,
                TargetPort = Graph.PortIndexOf(c.TargetPortId),
            });
        }
        return dto;
    }

    private static NodeyDocument FromDto(DocumentDto dto)
    {
        var doc = new NodeyDocument
        {
            Version = dto.Version,
            Name = dto.Name,
        };
        // Set size first so Layer buffers are created at the right W×H.
        doc.Graph.Width = dto.Width;
        doc.Graph.Height = dto.Height;

        var nodeById = new Dictionary<Guid, GraphNode>();
        foreach (NodeDto nd in dto.Nodes)
        {
            GraphNode node = doc.Graph.AddNodeInternal(nd.Type, nd.X, nd.Y, nd.Id);
            node.Name = nd.Name;
            if (nd.Params is not null)
                ApplyParams(node, nd.Params.Value);
            if (nd.Layer is not null)
            {
                byte[] data = Convert.FromBase64String(nd.Layer.Data);
                doc.Graph.SetLayerBuffer(node.Id, PixelBuffer.FromRgba8(data, nd.Layer.Width, nd.Layer.Height));
            }
            nodeById[node.Id] = node;
        }

        foreach (ConnectionDto cd in dto.Connections)
        {
            GraphNode src = nodeById[cd.SourceNode];
            GraphNode tgt = nodeById[cd.TargetNode];
            NodePort sourcePort = src.Outputs[cd.SourcePort];
            NodePort targetPort = tgt.Inputs[cd.TargetPort];
            doc.Graph.AddConnectionInternal(sourcePort, targetPort, cd.Id);
        }
        return doc;
    }

    private static JsonElement? SerializeParams(GraphNode node) => node switch
    {
        NoiseNode n => El(new NoiseParamsDto(n.Algorithm, n.Scale, n.Octaves, n.Seed, n.OffsetX, n.OffsetY)),
        ColorRampNode n => El(new RampParamsDto(n.Stops.Select(s => new RampStopDto(s.Position, s.R, s.G, s.B, s.A)).ToList())),
        MixNode n => El(new MixParamsDto(n.BlendMode, n.Opacity)),
        MultilayerNode n => El(new MultilayerParamsDto(
            n.PortParams.Select(p => new MultilayerPortParamsDto(p.BlendMode, p.Opacity)).ToList())),
        FilterNode n => El(new FilterParamsDto(n.Kind, n.Amount, n.Radius, n.Aperture)),
        MathNode n => El(new MathParamsDto(n.Operation)),
        ColorNode n => El(new ColorParamsDto(n.ColorR, n.ColorG, n.ColorB, n.ColorA)),
        _ => null, // Layer, and any node without parameters
    };

    private static JsonElement? El<T>(T value) =>
        JsonSerializer.SerializeToElement(value, SerializerOptions);

    private static void ApplyParams(GraphNode node, JsonElement element)
    {
        switch (node)
        {
            case NoiseNode noise:
            {
                NoiseParamsDto? p = element.Deserialize<NoiseParamsDto>(SerializerOptions);
                if (p is not null)
                {
                    noise.Algorithm = p.Algorithm;
                    noise.Scale = p.Scale;
                    noise.Octaves = p.Octaves;
                    noise.Seed = p.Seed;
                    noise.OffsetX = p.OffsetX;
                    noise.OffsetY = p.OffsetY;
                }
                break;
            }
            case ColorRampNode ramp:
            {
                RampParamsDto? p = element.Deserialize<RampParamsDto>(SerializerOptions);
                if (p is not null)
                    ramp.SetStops(p.Stops.Select(s => new RampStop(s.Position, s.R, s.G, s.B, s.A)));
                break;
            }
            case MixNode mix:
            {
                MixParamsDto? p = element.Deserialize<MixParamsDto>(SerializerOptions);
                if (p is not null)
                {
                    mix.BlendMode = p.BlendMode;
                    mix.Opacity = p.Opacity;
                }
                break;
            }
            case MultilayerNode multi:
            {
                MultilayerParamsDto? p = element.Deserialize<MultilayerParamsDto>(SerializerOptions);
                if (p is not null)
                {
                    multi.EnsureInputPorts(p.Ports.Count);
                    for (int i = 0; i < p.Ports.Count; i++)
                        multi.SetPortParams(i, p.Ports[i].BlendMode, p.Ports[i].Opacity);
                }
                break;
            }
            case FilterNode filter:
            {
                FilterParamsDto? p = element.Deserialize<FilterParamsDto>(SerializerOptions);
                if (p is not null)
                {
                    filter.Kind = p.Kind;
                    filter.Amount = p.Amount;
                    filter.Radius = p.Radius;
                    filter.Aperture = p.Aperture;
                }
                break;
            }
            case MathNode math:
            {
                MathParamsDto? p = element.Deserialize<MathParamsDto>(SerializerOptions);
                if (p is not null)
                    math.Operation = p.Operation;
                break;
            }
            case ColorNode color:
            {
                ColorParamsDto? p = element.Deserialize<ColorParamsDto>(SerializerOptions);
                if (p is not null)
                {
                    color.ColorR = p.R;
                    color.ColorG = p.G;
                    color.ColorB = p.B;
                    color.ColorA = p.A;
                }
                break;
            }
        }
    }

    // -- DTOs ------------------------------------------------------------------------

    private sealed class DocumentDto
    {
        public int Version { get; set; }
        public string Name { get; set; } = "Untitled";
        public int Width { get; set; }
        public int Height { get; set; }
        public List<NodeDto> Nodes { get; set; } = [];
        public List<ConnectionDto> Connections { get; set; } = [];
    }

    private sealed class NodeDto
    {
        public Guid Id { get; set; }
        public NodeType Type { get; set; }
        public string Name { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public JsonElement? Params { get; set; }
        public LayerDataDto? Layer { get; set; }
    }

    private sealed class ConnectionDto
    {
        public Guid Id { get; set; }
        public Guid SourceNode { get; set; }
        public int SourcePort { get; set; }
        public Guid TargetNode { get; set; }
        public int TargetPort { get; set; }
    }

    private sealed class LayerDataDto
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string Data { get; set; } = "";
    }

    private sealed record NoiseParamsDto(
        NoiseAlgorithm Algorithm, float Scale, int Octaves, int Seed, float OffsetX, float OffsetY);

    private sealed record RampStopDto(float Position, float R, float G, float B, float A);
    private sealed record RampParamsDto(List<RampStopDto> Stops);

    private sealed record MixParamsDto(BlendMode BlendMode, float Opacity);

    private sealed record MultilayerPortParamsDto(BlendMode BlendMode, float Opacity);
    private sealed record MultilayerParamsDto(List<MultilayerPortParamsDto> Ports);

    private sealed record FilterParamsDto(FilterKind Kind, float Amount, float Radius, float Aperture);

    private sealed record MathParamsDto(MathOperation Operation);

    private sealed record ColorParamsDto(float R, float G, float B, float A);
}

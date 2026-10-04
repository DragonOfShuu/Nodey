# STEP 3 — Graph Model, Type System & Evaluation (Nodey.Core/Graph)

You are building **Nodey** (read `PLAN.md` first, `RESEARCH.md` for framework facts).
This is **step 3 of 6**: the core node-graph model, the "foreign nodes" type
translation system, and the incremental evaluation engine — all UI-free, all tested.

## Context

- STEP-2 is done: `Nodey.Core/Image` contains `PixelBuffer`, noise, `ColorRamp`,
  `BlendModes`, filters, `MathOps`, and the `SignalType` enum. Reuse all of it; do not
  duplicate pixel logic here.
- This step defines the data model that the UI (STEP-4/5) binds to. Design for
  **event-driven change notification** from day one (the UI will subscribe).

## Tasks

### 3.1 Ports & nodes (`Nodey.Core/Graph/`)

```csharp
public sealed class NodePort
{
    public Guid Id { get; }
    public string Label { get; set; }                 // "Input", "A", "Base", …
    public SignalType ExpectedType { get; }           // what the node consumes
    public IReadOnlySet<SignalType> AcceptedTypes { get; }  // connection rule (§5.3)
    public bool IsInput { get; }
    public Node Owner { get; }
}

public enum NodeType { Layer, Noise, ColorRamp, Mix, Multilayer, Filter, Math, Color }

public abstract class GraphNode
{
    public Guid Id { get; }
    public NodeType Type { get; }
    public string Name { get; set; }                  // editable; Layer nodes show it in UI
    public double PositionX, PositionY;               // graph space (UI coordinates)
    public IReadOnlyList<NodePort> Inputs { get; }
    public IReadOnlyList<NodePort> Outputs { get; }
    public bool IsDirty { get; }
    public event EventHandler? Changed;               // params/structure changed
}
```

- One class per node type (`LayerNode`, `NoiseNode`, `ColorRampNode`, `MixNode`,
  `MultilayerNode`, `FilterNode`, `MathNode`, `ColorNode`), each declaring its ports in
  the constructor **exactly per PLAN.md §4.2** (expected types + accepted types per
  PLAN.md §5.2) and exposing typed parameter properties that raise `Changed` when set:
  - `LayerNode`: `Name`; buffer lives in the graph's layer store (see 3.4).
  - `NoiseNode`: `Algorithm` (STEP-2 enum), `Scale`, `Octaves`, `Seed`, `OffsetX`,
    `OffsetY`.
  - `ColorRampNode`: `Stops` (from STEP-2 `ColorRamp`), input port
    `ExpectedType=Scalar2D`, `AcceptedTypes={Scalar, Scalar2D, Color2D}`.
  - `MixNode`: `BlendMode`, `Opacity`; ports `Base`, `Blend` (Expected `Color2D`,
    Accepted `{Color2D, Scalar, Scalar2D}`).
  - `MultilayerNode`: **dynamic input ports** — starts with one input `Layer0`
    (Expected `Color2D`, Accepted `{Color2D, Scalar, Scalar2D}`); each port has its own
    `BlendMode` + `Opacity` (store per-port params in a list parallel to the ports).
    Methods: `AddInputPort()`, `RemoveLastInputPort()` (called by the graph when
    connections change — see 3.3).
  - `FilterNode`: `Kind` (STEP-2 `FilterKind`), `Amount`, `Radius`, `Aperture`; input
    Expected `Color2D`, Accepted `{Color2D}`.
  - `MathNode`: `Operation` (STEP-2 `MathOperation`); ports `A`, `B` Expected
    `Scalar2D`, Accepted `{Scalar, Scalar2D}`.
  - `ColorNode`: `ColorR, G, B, A` (0..1); output `Color2D`.
- Port lookup helpers: `GetInputPort(int index)`, `GetOutputPort(int index)`,
  `TryGetInputPortByLabel(string, out NodePort)`.

### 3.2 Connections & graph

```csharp
public sealed record GraphConnection(Guid SourcePortId, Guid TargetPortId);

public sealed class Graph
{
    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyList<GraphConnection> Connections { get; }
    public int Width { get; set; }      // document resolution, default 512
    public int Height { get; set; }
    public event EventHandler? Changed; // structure changed (nodes/connections/size)

    public GraphNode AddNode(NodeType type, double x, double y);
    public void RemoveNode(Guid id);            // also removes its connections
    public bool CanConnect(NodePort source, NodePort target, out string reason);
    public GraphConnection? Connect(NodePort source, NodePort target); // null if invalid
    public void Disconnect(Guid connectionId);
    // queries
    public IEnumerable<GraphNode> DownstreamOf(Guid nodeId);  // transitive dependents
    public IEnumerable<GraphNode> UpstreamOf(Guid nodeId);
    public GraphNode? FindOutputNode();   // see 3.5 rule
}
```

- `CanConnect` rules (all must hold; `reason` is user-readable for UI toast):
  1. source and target belong to different nodes.
  2. source is an output, target is an input.
  3. **type rule (PLAN.md §5.3): `source.Owner`'s output signal type ∈
     `target.AcceptedTypes`** — the output's `SignalType` is a property on the source
     node/port (each node type declares its output type; put `SignalType OutputType`
     on `GraphNode`).
  4. no duplicate connection into the same target port (one input per port — replacing
     an existing connection on that port is allowed: `Connect` disconnects the old one
     first and reports it).
  5. **no cycles**: adding the connection must not create a directed cycle
     (DFS from target's node to source's node).
- `Disconnect` + `Connect` must maintain **MultilayerNode** input ports:
  - when a connection is *made* to a Multilayer's topmost input, call
    `AddInputPort()` (the "another input appears" behavior from the spec).
  - when a Multilayer's *last* input port has its connection removed and the port is
    empty, call `RemoveLastInputPort()` — but never remove the first port (keep ≥1).
  - raise `Changed` after structural changes.
- Layer buffer store: `Graph` owns `Dictionary<Guid, PixelBuffer> LayerBuffers` keyed
  by Layer node id (created on node add at document W×H, removed on node remove).
  `void SetLayerBuffer(Guid layerNodeId, PixelBuffer buf)` + `PixelBuffer
  GetLayerBuffer(Guid)`. (Kept on Graph so documents serialize one place.)

### 3.3 Type translation — "foreign nodes" (PLAN.md §5, implement the full table)

`Nodey.Core/Types/TypeTranslator.cs`:

```csharp
public static class TypeTranslator
{
    // scalar source
    public static PixelBuffer FromScalar(double value, SignalType to, int w, int h);
    // field source
    public static PixelBuffer FromField(PixelBuffer field, SignalType to, int w, int h);
    // image source
    public static PixelBuffer FromImage(PixelBuffer image, SignalType to, int w, int h);

    // The one-call API the evaluator uses:
    public static PixelBuffer Translate(
        SignalType from, PixelBuffer? buffer, double scalarValue,
        SignalType to, int w, int h);
}
```

Implement **exactly** the table in PLAN.md §5.4 (Scalar→Scalar2D fill; Scalar→Color2D
solid gray; Scalar2D→Color2D grayscale; Color2D→Scalar2D luminance; identities; size
mismatch ⇒ resample with `ScaleTo` first). Pure, no side effects.

### 3.4 Evaluation engine (`Nodey.Core/Graph/GraphEvaluator.cs`)

- `GraphEvaluator(Graph graph)` with:
  - `event EventHandler<NodeEvaluatedEventArgs>? NodeEvaluated` (node id + resulting
    buffer — the UI preview subscribes).
  - `void Evaluate()` — topological order (Kahn's algorithm over the connection DAG);
    evaluate only dirty nodes (and nodes whose inputs changed value — track by
    comparing input connection presence + source dirty state; simplest correct v1:
    **re-evaluate the whole DAG when anything is dirty**, but *skip* a node if neither
    it nor any upstream node is dirty — implement the dirty walk properly:
    `MarkDirty(node)` + `MarkDownstreamDirty` on every `Changed` event and on
    `SetLayerBuffer`).
  - Per-node evaluation = **Translate each input into the port's ExpectedType
    (TypeTranslator), then the node's mutate function** (PLAN.md §5.4 pipeline).
    Results cached in `Dictionary<Guid, PixelBuffer>`.
  - Node mutate functions (each a small `EvaluateXxx` method using STEP-2 engine):
    - Layer → `graph.GetLayerBuffer(id)` (clone? no — return the buffer itself;
      document that consumers must not mutate it in place).
    - Noise → `NoiseField.Generate(...)` with the node's params (this is the
      Scalar2D output).
    - ColorRamp → input translated to Scalar2D → `ColorRamp.Apply`.
    - Mix → translate both to Color2D → `BlendModes.Blend`.
    - Multilayer → fold inputs bottom-up: start with `Layer0`'s translated Color2D,
      then for each higher port blend with its own mode/opacity.
    - Filter → `Filters.Apply` on translated Color2D.
    - Math → translate both to Scalar2D → `MathOps.ApplyToBuffers`.
    - Color → `PixelBuffer.Solid(w, h, r, g, b, a)`.
- `PixelBuffer? GetOutput()` — the buffer of `graph.FindOutputNode()` (see 3.5).
- Thread-safety: evaluator is **not** thread-safe by itself; the UI (STEP-4) wraps
  calls in a lock + background task. Document this contract in XML comments.

### 3.5 Document output rule (PLAN.md §4.3)

`FindOutputNode()`: among all nodes, pick the node whose output port has **no outgoing
connection**; if several, the one with the **greatest `PositionX`** (rightmost in the
graph); ties → most recently added (document). If the graph has zero nodes → `null`.
If a node's output is connected but its *target* is disconnected downstream, the target
wins (it's further right/downstream). Implement as: start from every node, walk
outgoing connections as far as possible; the node reached that cannot advance is an
output candidate; pick rightmost among candidates.

### 3.6 Document model & save/load (`Nodey.Core/Document/`)

- `NodeyDocument`: wraps a `Graph` + `Version` int + `Name`.
- Serialization format: **a single JSON file** (`.nodey`) containing:
  - document meta (name, width, height),
  - nodes: id, type, name, x, y, parameters (per-type),
  - connections: source/target **port ids** (port ids must be stable: derive port id
    deterministically, e.g. hash of `nodeId + portIndex + label`, or store an explicit
    port index — **store explicit port indices**: simpler and stable),
  - Multilayer per-port params aligned to port order,
  - layers: each Layer node's buffer as **base64 PNG** (encode via a tiny pure
    encoder? no — see below).
- **PNG encoding without SkiaSharp**: to keep `Nodey.Core` dependency-free, serialize
  layer buffers as **base64 raw RGBA8** (width, height, then bytes). A `.nodey` file is
  thus self-contained and dependency-free. (STEP-6 may add PNG compression; raw is the
  v1 requirement — document the tradeoff.)
- `static NodeyDocument Load(string path)` / `void Save(string path)` — must be a
  **lossless round-trip**: nodes, positions, params, connections, layer pixels,
  document size. Use `System.Text.Json` with a small `JsonSerializerOptions` (camelCase,
  write-indented for debug).
- Port id stability across save/load: reconstruct ports in the same order (node type
  order, then Multilayer dynamic ports in stored order) so stored port indices resolve
  correctly.

## Tests (`tests/Nodey.Core.Tests/`)

- `TypeTranslatorTests` — **every cell** of the §5.4 table (9 cells incl. 3 identities):
  exact values on small buffers (2×2), scalar fill, luminance formula spot-checks,
  size-mismatch resampling, `null` buffer handling for Scalar source.
- `GraphTests` — add/remove nodes; `CanConnect` rejections with reasons (same node,
  wrong direction, type mismatch, cycle of 2 and 3 nodes, duplicate target); connect
  replaces existing target connection; Multilayer grows/shrinks input ports per rules
  (connect topmost ⇒ new port appears; disconnect last ⇒ port removed down to 1).
- `EvaluatorTests` —
  - Math node: A=2 (scalar constant), B=3 (scalar constant) via two ColorNodes? No —
    use MathNode with two upstream… simplest: connect a `NoiseNode`? Keep it
    deterministic: **constants** are modeled by connecting nothing (unconnected input =
    default value 0 — document: *unconnected ports evaluate to their default: Scalar 0,
    Scalar2D zero-field, Color2D transparent*) — so test: unconnected Math A+B =
    zero-field; then connect a `ColorNode` (which outputs Color2D — not accepted by
    Math!) — instead test Math with… Math inputs accept Scalar/Scalar2D only; a pure
    scalar constant source node doesn't exist in v1 (constants are node parameters).
    Therefore: test Math by driving its inputs through the evaluator's
    `SetExternalValue(port, scalar)` test hook? — **Decision:** add
    `Evaluator.SetConstantValue(NodePort, double)` (documented as the way constant
    values enter the graph; the UI will use it for unconnected scalar inputs showing a
    value box). Tests: 2+3=5 field; 10-4=6; 3*4=12; 8/2=4; 1/0→0.
  - ColorRamp: constant 0.5 through ramp(black→white) ⇒ mid-gray field; ramp with
    custom stops gives expected color at t.
  - Noise node: output field deterministic across two `Evaluate()` runs; changing
    `Seed` param + dirty ⇒ output changes.
  - Mix: two ColorNodes (red over blue, Multiply) ⇒ black; Normal opacity 0.5 ⇒
    expected blend.
  - Multilayer: 3 layers fold in order (assert exact pixel with Normal/1.0 stack).
  - Filter: Gaussian on a solid is unchanged; on a step edge, center pixel darkens.
  - Dirty propagation: change a parameter on an upstream node ⇒ downstream re-evaluated
    (count evaluations via a test counter hook or by observing buffer identity change);
    unrelated subgraph not re-evaluated.
  - `FindOutputNode`: chain A→B→C returns C; two branches ⇒ rightmost; empty graph ⇒
    null.
- `DocumentTests` — build a graph with one of each node type + connections + painted
  layer pixels (random-ish deterministic pattern) ⇒ Save to temp file ⇒ Load ⇒ assert
  structural equality (node set, params, connections, layer buffer bytes identical,
  document size). Round-trip twice (idempotence).

## Verification

```bash
dotnet build Nodey.sln -c Release
dotnet test
```

## Pitfalls

- **Port identity**: use explicit stored port indices for save/load, not label strings
  (labels can be renamed by the user).
- Cycle detection must consider *replacing* a connection (disconnect-old-then-connect
  must not be rejected as a cycle when the old connection was the path).
- Unconnected-input defaults (PLAN: Scalar 0, Scalar2D zero-field, Color2D transparent)
  are a **spec decision made here** — the UI (STEP-4) shows these as editable value
  boxes via `SetConstantValue`.
- Keep `Graph`/`GraphNode` free of Avalonia types (positions are plain doubles).
- Don't evaluate in the `Changed` handler (reentrancy) — mark dirty there; evaluate on
  demand.

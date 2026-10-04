# Nodey — Project Plan

**Nodey** is a node-based image creation and painting application. Users build an image by
wiring together nodes (noise generators, color ramps, blend/mix nodes, filters, math, solid
colors) in a node graph, and can paint directly onto **Layer** nodes in a Photoshop-style
Draw View. The result is a hybrid of Blender's compositor, GIMP's layers, and a modern
Web3×Unreal-Engine aesthetic.

> **How to use this plan.** You are an AI implementer. Read this whole file first, then
> read `RESEARCH.md` (framework facts you must not get wrong), then implement **your**
> `STEP-n.md` file. Each step file is self-contained: it tells you what exists before you
> start, what to build, and how to verify it. Do not implement future steps, but do not
> break their prerequisites.

---

## 1. Goals and non-goals

### Goals
- A node graph editor (Avalonia + NodifyAvalonia) where nodes generate and transform images.
- Eight node types: **Layer, Noise, Color Ramp, Mix, Multilayer, Filter, Math, Color**.
- A type-tolerant connection system: nodes accept a *list* of input types and translate
  foreign values into what they need before mutating them ("foreign nodes", §5).
- A Draw View for painting on Layer nodes: brush, pencil, bucket fill, blur, box select,
  lasso select, quick select, with per-tool property panels.
- Blender-style hotkeys in the node graph; Photoshop-style hotkeys in the Draw View.
- A beautiful, high-contrast, rounded, modern UI ("Web3 crossed with Unreal Engine").
- Documents: save/load the whole graph + layer pixels to a single file.

### Non-goals (for v1)
- No multi-document tabs (single document, with save/load).
- No real-time video/streaming inputs.
- No GPU/CUDA pipeline (CPU SkiaSharp/float buffers; architecture must not preclude GPU later).
- No plugin system, no scripting.
- No mobile/web deployment (desktop only; keep it *buildable* for WASM by not using
  platform-specific APIs in `Nodey.Core`).

## 2. Tech stack (pinned — see RESEARCH.md §2 for why)

| Item | Choice |
|---|---|
| Architecture pattern | **MVVM** — every view has a view model; views contain layout + bindings only |
| Language / runtime | C# 13, **.NET 9** |
| UI framework | **Avalonia 11.3.x** (Fluent theme as base) — *not* Avalonia 12 |
| Node editor controls | **NodifyAvalonia 6.6.0** (`Install-Package NodifyAvalonia`) |
| Nodify theme | `<ResourceInclude Source="avares://Nodify/Theme.axaml" />` |
| Image pixel work | `SkiaSharp` (via `Avalonia.Skia`) for bitmaps; raw `float[]`/`byte[]` buffers in core |
| Testing | xUnit (+ `Avalonia.Headless.XUnit` only if a step says so) |
| Packaging | Plain `dotnet` CLI projects; no F# / no MAUI |

## 3. Solution layout

```
Nodey/
├── PLAN.md, RESEARCH.md, STEP-1.md … STEP-6.md
├── Nodey.sln
├── Directory.Build.props            # net9.0, nullable, langversion, common props
├── .gitignore
├── src/
│   ├── Nodey.Core/                  # ⚠ UI-FREE. No Avalonia types (except System.*).
│   │   ├── Image/                  # PixelBuffer, blend modes, filters, noise, ramps
│   │   ├── Graph/                  # Node model, connectors, connections, evaluation
│   │   ├── Types/                  # SignalType, type translation
│   │   └── Document/               # save/load (JSON + embedded PNGs)
│   └── Nodey/                      # Avalonia app (UI layer only)
│       ├── Program.cs, App.axaml(.cs), MainWindow.axaml(.cs)
│       ├── Views/
│       │   ├── NodeView.axaml(.cs)          # left palette / center graph / right preview
│       │   ├── DrawView.axaml(.cs)          # left tools / center canvas / right layers
│       │   └── …
│       ├── ViewModels/
│       │   ├── ShellViewModel.cs             # tab switching Node/Draw, shared document
│       │   ├── NodeEditor/                   # Nodify MVVM VMs (Node/Connector/Connection/…)
│       │   ├── Draw/                         # canvas, tools, layers VMs
│       │   └── …
│       ├── Controls/                     # custom controls (CanvasSurface, ColorRampBar, …)
│       ├── Themes/                     # Nodey theme: palette, fonts, control themes
│       └── Services/                   # evaluation host, document IO, undo stack
└── tests/
    └── Nodey.Core.Tests/           # xUnit tests for engine + graph (UI-free)
```

**Hard rule:** `Nodey.Core` must compile with zero Avalonia/SkiaSharp-UI dependencies
(SkiaSharp core is allowed for `SKBitmap`-free math if needed, but prefer pure arrays so the
engine stays testable anywhere). The UI layer holds *view-model* copies/proxies of core
objects where needed (see §4.4).

## 4. Core architecture

> **MVVM is mandatory.** All UI logic lives in view models (`Nodey/ViewModels`), never in
> code-behind. Views (.axaml) contain layout, bindings, and DataTemplates only. This is
> also what NodifyAvalonia is designed around (its official examples are pure MVVM — see
> RESEARCH.md §5 for the exact binding contract: `ItemContainerTheme` pushes
> `Location`/`IsSelected`/`ActualSize` into node VMs, connector themes push `Anchor`/
> `IsConnected` back, and commands flow VM → editor). Code-behind is permitted only for:
> custom control `Render(DrawingContext)` overrides, pointer-coordinate math in custom
> controls, and one-time setup (theme inclusion, `EditorGestures` configuration).

### 4.1 Image representation

- `PixelBuffer` (in `Nodey.Core/Image`): width, height, `float[]` RGBA (4 floats per pixel,
  0..1, premultiplied-alpha off), plus `byte[]` RGBA8 conversion for display.
  - Methods: `Clone()`, `FromBytes()`, `ToBytes()`, `GetPixel/SetPixel`, `Fill(float r,g,b,a)`,
    `CopyFrom`, `ScaleTo(w,h)` (nearest + bilinear), `Empty(w,h)`.
- **Fixed canvas resolution** per document: default **512×512** (configurable in document
  settings). Every node evaluates at this resolution. This keeps the whole pipeline fast and
  predictable; the Draw View canvas is a 1:1 mapping of the document buffer.
- All pixel operations are pure functions on `PixelBuffer` (or in-place variants marked
  `*InPlace`) — trivially unit-testable.

### 4.2 Node catalog (core model)

Every node has: `Id` (Guid), `Type` (enum), `Name` (string), `Position` (double x,y),
`Parameters` (per-type dict/typed properties), `Inputs` (list of `NodeInputPort`),
`Outputs` (list of `NodeOutputPort`). Ports have a declared **expected `SignalType`** and an
**accepted-types list** (§5).

| Node | Inputs | Outputs | Parameters |
|---|---|---|---|
| **Layer** | — (source) | `Image` (Color2D) | `Name` (editable string, shown in Layer node + layers panel) |
| **Noise** | — | `Value` (Scalar2D) | `Algorithm`: **Perlin, Value, Cellular/Worley, Simplex, Random** (≥4 options), `Scale`, `Octaves` (fBm), `Seed`, `Offset` (x,y) |
| **Color Ramp** | `Input` (accepts Scalar, Scalar2D, Color2D; expects Scalar2D) | `Color` (Color2D) | `Stops`: ordered list of (position 0..1, color), ≥2 stops; Blender-style: leftmost stop maps dark→its color, rightmost maps light→its color; interpolation between stops; colors before first stop / after last stop clamp |
| **Mix** | `Base` (Color2D), `Blend` (Color2D) | `Result` (Color2D) | `BlendMode` (Classic: Normal, Multiply, Screen, Overlay, SoftLight, HardLight, Difference, Exclusion, LinearBurn, LinearDodge, HSL Color/Luminosity…), `Opacity` 0..1 |
| **Multilayer** | dynamic: `Layer0` (Color2D) — **each new connection adds `LayerN`** | `Result` (Color2D) | per-input `BlendMode` + `Opacity` |
| **Filter** | `Input` (Color2D) | `Output` (Color2D) | `Kind`: **Lens Blur, Gaussian Blur, Box Blur, Sharpen, Emboss, Erosion, Dilation**, `Amount`/`Radius`, plus kind-specific params (e.g. lens blur `Aperture`) |
| **Math** | `A` (accepts Scalar, Scalar2D; expects Scalar2D), `B` (same) | `Result` (Scalar2D) | `Operation`: **Add, Subtract, Multiply, Divide** (dropdown) |
| **Color** | — | `Color` (Color2D) | `RGBA` (color picker) — generates a solid color buffer |

Notes:
- **Layer** nodes are the paintable surfaces: their buffer is edited in the Draw View.
  Layer nodes also appear in the Draw View's right panel (top→bottom order = node-graph
  right→left order of connected layer nodes, per spec).
- **Multilayer**'s "indefinite inputs" is implemented by appending a new input port when a
  connection is made to the current topmost input (and removing the last port when its
  connection is removed and it's empty).
- Node defaults: new nodes get a default parameter set and a sensible default position
  (cursor location in the editor).

### 4.3 Evaluation engine

- `Graph` holds `Nodes` (dict by Id) + `Connections` (source port → target port).
- **Dirty propagation:** each node has an `IsDirty` flag; changing a parameter, connection,
  or layer buffer marks the node dirty; a `MarkDownstreamDirty(node)` walk marks dependents.
  `Graph.Evaluate()` does a topological pass and re-evaluates only dirty nodes; results are
  cached per node (`PixelBuffer?` or scalar).
- Cycle detection on connection creation (reject with a user-visible message).
- Evaluation runs on a background `Task` when triggered from UI; the UI preview subscribes to
  a `GraphChanged` / `NodeEvaluated` event. Debounce: at most one evaluation per ~16 ms of
  changes.
- The **document output** = the evaluation of the *output-most* node(s) — concretely, the
  rightmost connected node in the graph that has no outgoing connections (the Draw View's
  "Full Preview" and the Node View's right panel show this).

### 4.4 UI ↔ core mapping

- Core objects (`Node`, `Graph`) are the **source of truth**.
- The Nodify editor binds to **view-models** (`NodeVm`, `ConnectorVm`, …) that wrap core
  nodes 1:1 (the Calculator example's pattern, RESEARCH.md §5). VMs live in
  `Nodey/ViewModels/NodeEditor`.
- Parameter edits in node content (sliders, dropdowns, the color ramp bar) write through to
  the core node and mark it dirty; the evaluation host then updates the preview.
- Layer painting (Draw View) writes to the core `LayerNode` buffer, marks it dirty, and the
  preview updates.

## 5. The "foreign nodes" type system (core spec — implement exactly this)

### 5.1 Signal types

```csharp
enum SignalType { Scalar, Scalar2D, Color2D }
```

- `Scalar` — one float (a constant value node / a scalar output).
- `Scalar2D` — 2D field of floats (0..1), e.g. Perlin noise output.
- `Color2D` — 2D RGBA image, e.g. any layer/mix/ramp output.

### 5.2 Ports

Each input port declares:
- `ExpectedType` — what the node's mutate function consumes.
- `AcceptedTypes` — the set of `SignalType`s the port will accept a connection from.

Example: **Color Ramp** input: `ExpectedType = Scalar2D`, `AcceptedTypes = { Scalar, Scalar2D, Color2D }`.
A Math node input: `ExpectedType = Scalar2D`, `AcceptedTypes = { Scalar, Scalar2D }`.
A Mix node input: `ExpectedType = Color2D`, `AcceptedTypes = { Color2D, Scalar, Scalar2D }`
(scalars/fields auto-become grayscale images).

### 5.3 Connection rule

A connection from output port `S` to input port `T` is **valid iff**
`S.SignalType ∈ T.AcceptedTypes`. Everything else is rejected at connect time
(pending connection shows red / is not allowed, and the VM does not commit).

### 5.4 The pipeline (per input port, at evaluation time)

```
Input value  →  Translate into Expectations  →  Mutate as the Node Does  →  Output
```

`TypeTranslator` (in `Nodey.Core/Types`) performs the translation step using only
`T.ExpectedType` and the incoming `SignalType`:

| from \ to | Scalar | Scalar2D | Color2D |
|---|---|---|---|
| **Scalar** | identity | fill W×H field with the value | solid image of that grayscale value (alpha 1) |
| **Scalar2D** | mean of field* | identity | map each value v → grayscale color (v,v,v,1) |
| **Color2D** | mean of luminance* | per-pixel luminance (0.2126R+0.7152G+0.0722B) | identity |

\* Scalar targets are only produced when a port expects a scalar (rare; Math node with a
scalar constant is the main case — constants are stored as `Scalar` values, not evaluated).

After translation, the node's mutate function runs on the *expected* type only. This is
exactly the spec's "Color Ramp given a constant float → fill a 2D array with that number →
ramp it" example.

**Implementation requirement:** translation is a pure function
`PixelValue Translate(SignalType from, PixelBuffer? buf, double scalar, SignalType to, int w, int h)`.
Unit-test every cell of the table above.

## 6. Views

### 6.1 Shell

`MainWindow` = a `TabControl`-style top bar with two views (custom-styled tabs, not the
stock control): **Node View** and **Draw View**. Both views operate on the *same* document
(shared `ShellViewModel.Document`). Window: dark background, rounded corners (client-side
rounding on Windows via `ExtendClientAreaToDecorationsHint`), minimum 1280×800.

### 6.2 Node View (three columns)

```
┌──────────────┬──────────────────────────────────┬──────────────┐
│ LEFT         │ CENTER                          │ RIGHT        │
│ Node palette │ NodifyEditor graph              │ Image panel  │
│ + search     │ (nodes, connections, grid,      │ live preview │
│              │  minimap, Shift+A menu)         │ of document  │
└──────────────┴──────────────────────────────────┴──────────────┘
```

- **Left — palette & search:** searchable list of the 8 node types (filter as you type;
  icons + name + one-line description). Click = create at viewport center; drag into editor
  = create at drop point (the Calculator example's drag-drop palette is the reference).
  Also hosts the "add node" affordance for Blender's `Shift+A`.
- **Center — graph:** `NodifyEditor` with `CircuitConnection` wires (rounded, animated
  directional arrows optional), grid background (tile DrawingBrush, like the example),
  `Minimap` in a corner, `DecoratorContainer`-based **Shift+A create menu** at cursor,
  Blender hotkeys (§7.1).
- **Right — image panel:** live `Image` preview of the document output (updated by the
  evaluation host), with a checkerboard behind transparency, a "Fit" indicator, and the
  document size label. This is the "created image" panel from the spec.

### 6.3 Draw View (three columns, GIMP/Photoshop-like)

```
┌──────────────┬──────────────────────────────────┬──────────────┐
│ LEFT         │ CENTER                          │ RIGHT        │
│ Tools        │ CanvasSurface                   │ Layers list  │
│ (icons)      │ (full preview by default;       │ (top→bottom  │
│ + properties │  toggle: Full Preview /         │  = graph     │
│  of selected │  Selected Layer Only)           │  right→left) │
│  tool        │                                │ name + mini  │
└──────────────┴──────────────────────────────────┴──────────────┘
```

- **Center — canvas:** custom `CanvasSurface` control (a `Control` overriding
  `Render(DrawingContext)`), draws: (a) the **full document output** (default) or (b) the
  **selected layer's** buffer, scaled to fit with integer-ish zoom; zoom (Ctrl+wheel),
  pan (Space/middle-drag), 100% indicator. Top-center: a **segmented toggle**
  `Full Preview | Selected Layer Only` (spec requirement).
- **Left — tools:** icon buttons: **Brush, Pencil, Bucket Fill, Blur** (paint tools) and
  **Box Select, Lasso Select, Quick Select** (selection tools). Below the icons: the
  selected tool's **properties panel** (e.g. Brush: size, hardness, opacity, color; Bucket:
  threshold; Blur: radius; selects: feather). Tool state (size/color) persists per tool.
- **Right — layers:** one row per **connected** Layer node, ordered top→bottom = graph
  right→left (i.e. the layer nearest the final output is on top). Each row: small live
  preview thumbnail, editable **name** (the Layer node's `Name` — editing renames the node
  in the graph too), visibility/lock toggles (v1: visibility only). Clicking a row selects
  that layer node (and selects it in the graph as well).

### 6.4 Tool behavior (Draw View)

- Painting writes into the selected Layer node's buffer at canvas coordinates (accounting
  for zoom/pan). Brush = soft circular stamp with hardness/opacity; Pencil = hard 1px-ish
  stamp; Bucket Fill = flood fill with **threshold** tolerance (4-connectivity, on the
  layer buffer); Blur tool = gaussian blur applied along the stroke path (radius param).
- Selections produce a selection mask on the canvas; **Quick Select** = grow from clicked
  point by color similarity (a simple region-growing with tolerance); Box/Lasso define
  polygon/rect masks. v1: selections gate painting (paint only inside selection) and show
  the marching-ants overlay. (Full selection operations — copy/delete/move selection — are
  a v1 stretch; the mask + gating is the requirement.)
- Undo/redo (Ctrl+Z / Ctrl+Shift+Z) covers paint strokes and tool parameter changes via a
  command stack (buffer snapshots per stroke — 512×512 RGBA8 snapshots are cheap enough).

## 7. Hotkeys

### 7.1 Node View — Blender node-editor mapping

| Gesture | Action |
|---|---|
| `Shift+A` | Open create-node menu at cursor (DecoratorContainer) |
| `G` | Grab/move selected node(s) (Nodify's default drag gesture — ensure `EditorGestures.Mappings.ItemContainer.Drag` includes this; Blender uses G+drag) |
| `S` | Scale… *(v1: skip; document as future)* |
| `X` / `Delete` | Delete selected nodes (and their connections) |
| `C` | (Blender "connect") — v1: start a pending connection from the selected node's first output |
| `Ctrl+D` | Duplicate selected node(s) (offset +20,+20) |
| `Ctrl+A` | Select all nodes |
| `Escape` | Cancel pending connection / deselect |
| `F` | Frame all (FitToScreen) |
| `Ctrl+Z` / `Ctrl+Shift+Z` | Undo / redo (graph ops: move, create, delete, connect, disconnect, parameter change) |
| `Ctrl+S` | Save document |
| wheel | Zoom (Nodify default; keep) |
| middle-drag / `Alt`+drag | Pan (Nodify defaults; keep) |

Implementation: `KeyBinding`s on the `NodifyEditor` + `EditorGestures.Mappings` tweaks for
mouse gestures. Blender's exact mouse gestures (right-drag pan, etc.) are a bonus — the
table above is the requirement.

### 7.2 Draw View — Photoshop mapping

| Key | Tool / action |
|---|---|
| `B` | Brush |
| `P` | Pencil |
| `G` | Bucket Fill (G = paint bucket in PS) |
| `O` | Blur (PS blur tool) |
| `M` | Box (marquee) select |
| `L` | Lasso select |
| `W` | Quick select (PS magic-wand-ish) |
| `[` / `]` | Decrease / increase tool size |
| `D` | Reset foreground color to default (white) |
| `X` | Swap foreground/background (v1: just swap brush color with last color) |
| `Space` (hold) | Hand/pan |
| `Ctrl+wheel` | Zoom |
| `Ctrl+Z` / `Ctrl+Shift+Z` | Undo / redo |
| `Esc` | Deselect / cancel current stroke |
| `V` | (PS move) — v1: select layer (no move tool) |

## 8. Visual design — "Web3 × Unreal Engine"

A dark, high-contrast, neon-accented, rounded, glassy UI. Concretely:

- **Base palette** (define as resource keys in `Themes/Palette.axaml`):
  - `Background.Base` `#0A0E17` (near-black navy), `Background.Panel` `#111827` @ 85%
    (glass: `Opacity` + 1px `Border` + `CornerRadius` 12), `Background.Elevated` `#1E293B`.
  - `Accent.Cyan` `#22D3EE`, `Accent.Magenta` `#F0439B`, `Accent.Violet` `#8B5CF6`,
    `Accent.Green` `#34D399` (status/success), `Accent.Amber` `#FBBF24` (warning).
  - `Text.Primary` `#F8FAFC`, `Text.Secondary` `#94A3B8`, `Text.Disabled` `#475569`.
  - Node header gradient: `#6366F1 → #A855F7 → #EC4899` (the Calculator example's animated
    border is a nice touch — optional, static is fine).
- **Typography:** Inter (UI, via `Avalonia.Fonts.Inter`), a display font for titles/logo
  (bundle an OFL font, e.g. *Rajdhani* or *Orbitron*), JetBrains Mono / Inter for numeric
  readouts. Define `FontFamily` resource keys (`Font.Ui`, `Font.Display`, `Font.Mono`).
- **Shape language:** `CornerRadius` 8–12 on panels, 6 on inputs/buttons; 1px borders at
  20–40% white; soft outer glow on focus/selection (`BoxShadow` via a `Border` +
  `DropShadowEffect` or a blurred `Border` behind); rounded connector wires
  (`CircuitConnection.CornerRadius`).
- **Nodify theming:** merge `avares://Nodify/Theme.axaml`, then override the built-in
  resource keys our app defines (Node background, header, connector colors, connection
  stroke = `Accent.Cyan` with glow, selection = `Accent.Violet`). Keep node content
  templates ours (each node type gets a styled template: header bar + parameter body).
- **Feedback:** hover brightens 10–15%, active tool gets an accent pill, pending
  connections animate, invalid connections flash `Accent.Amber`.

## 9. Conventions

- **MVVM, no code-behind logic.** Views contain layout + bindings only (code-behind is
  allowed for: control `Render` overrides, pointer-coordinate math in custom controls, and
  `EditorGestures`/theme setup). VMs use the vendored `ObservableObject`/`DelegateCommand`
  (RESEARCH.md §6) + `INotifyPropertyChanged`.
- **Naming:** `*View` (.axaml), `*ViewModel`, core classes without suffix. Node VMs:
  `NodeVm`, `ConnectorVm`, `ConnectionVm`, `PendingConnectionVm`, `NodeEditorViewModel`.
- **Immutable-ish core:** core `Node`/`Graph` mutate through methods that raise
  `Changed` events; no silent mutation.
- **Every public core type is unit-tested** (at least: PixelBuffer, each blend mode,
  every noise algorithm (determinism via seed), color ramp (stop math), every filter,
  math ops, the full translation table, graph evaluation order, cycle detection,
  document round-trip).
- **Verification after every step:** `dotnet build Nodey.sln -c Release` must pass with 0
  warnings (treat warnings as errors in `Directory.Build.props` is optional — do not
  enable it in step 1, it makes iteration painful; but keep the code warning-free).
- **Git:** one commit per logical change; commit at the end of each step with message
  `STEP-n: <summary>`.

## 10. Step index

| Step | File | Deliverable (verifiable outcome) |
|---|---|---|
| 1 | `STEP-1.md` | Solution scaffold, app shell (window + tab bar + empty views), full Web3×Unreal theme, Nodify package + theme included, builds & runs |
| 2 | `STEP-2.md` | `Nodey.Core` image engine: PixelBuffer, noise, ramps, blends, filters, math + passing unit tests |
| 3 | `STEP-3.md` | `Nodey.Core` graph model: nodes/ports/connections, type translation, evaluation engine, document model + passing unit tests |
| 4 | `STEP-4.md` | Node View: live Nodify graph with all 8 node types, palette+search, right preview panel, Blender hotkeys, Shift+A menu |
| 5 | `STEP-5.md` | Draw View: canvas surface, 7 tools + property panels, layers panel, preview toggle, Photoshop hotkeys |
| 6 | `STEP-6.md` | Integration: document save/load, undo/redo polish, performance pass, final QA checklist |

**Dependencies:** 2 → 3 (3 uses 2). 4 and 5 both need 2+3 (4 first, since 5's layers panel
shows layer nodes that 4 makes visible in the graph). 6 last.

## 11. Definition of done (project-level)

1. `dotnet build Nodey.sln -c Release` → 0 errors, 0 warnings.
2. `dotnet test` → all tests pass.
3. App launches; user can: create nodes (palette, drag, Shift+A), connect them (only
   type-valid connections), see the live preview update; paint on layers with all 7 tools;
   toggle Full Preview/Selected Layer Only; use the documented hotkeys in both views;
   save and reload a document preserving nodes, connections, parameters, and layer pixels.
4. The UI matches §8 (dark, contrasty, rounded, neon accents, modern fonts).

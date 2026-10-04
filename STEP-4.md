# STEP 4 — Node View (Nodify graph, palette, preview, Blender hotkeys)

You are building **Nodey** (read `PLAN.md` first, **`RESEARCH.md` §4–§6 carefully** —
this step is the one where the Nodify API matters most). This is **step 4 of 6**: turn
the Node View placeholder into a working node editor.

## Context

- STEP-1..3 are done: app shell + theme exist; `Nodey.Core` has the image engine,
  graph model, `TypeTranslator`, `GraphEvaluator`, and documents. `dotnet test` is green.
- The Node View currently shows a three-column skeleton with a hidden test
  `NodifyEditor` (STEP-1). Replace the placeholder content with the real editor.
- **Reference code:** the Calculator example in the nodify-avalonia repo
  (`Examples/Nodify.Calculator/*` — RESEARCH.md §9). Its MVVM contract (RESEARCH.md §5)
  is the pattern to replicate. You may vendor small helpers from `Examples/Nodify.Shared`
  (`ObservableObject`, `NodifyObservableCollection`, `DelegateCommand`,
  `ItemToListConverter`) into `src/Nodey/Shared/` — they're sample code, keep them
  recognizable (header comment with the source URL).

## Tasks

### 4.1 View-model layer (`Nodey/ViewModels/NodeEditor/`)

Wrap core objects 1:1 (core stays source of truth — PLAN.md §4.4):

- **`NodeVm`** (per core `GraphNode`): `Id`, `Type`, `Title` (bound to core `Name`),
  `Location` (`Avalonia.Point` — two-way with `ItemContainer.Location`), `Size`
  (`Avalonia.Size`, OneWayToSource), `IsSelected`, `Inputs` (observable collection of
  `ConnectorVm`), `Output` (single `ConnectorVm` for v1 — all our nodes have one
  output; use `ItemToListConverter` in XAML), back-ref to core node, and
  `Parameters` access (expose the core node's parameter properties so content templates
  bind directly — e.g. `NoiseNode Noise => (NoiseNode)Node`).
- **`ConnectorVm`** (per core `NodePort`): `Title` (port label), `IsConnected`,
  `IsInput`, `Anchor` (`Avalonia.Point`, OneWayToSource), `SignalType`, `Value`
  (display: for unconnected scalar inputs show/edit the constant — see 4.4),
  back-refs to `NodeVm` + core port.
- **`ConnectionVm`** (per core `GraphConnection`): `Input`/`Output` (`ConnectorVm`s).
- **`PendingConnectionVm`**: `Source`, `Target` (ConnectorVm, OneWayToSource),
  `TargetLocation` (Point, OneWayToSource), `IsVisible`, `IsValid` (for red-highlighting
  invalid targets — optional polish).
- **`NodeEditorViewModel`** (the editor VM, mirrors `CalculatorViewModel`):
  - `Nodes` (observable collection of `NodeVm`), `Connections` (observable
    `ConnectionVm`), `SelectedNodes` (IList, two-way to `NodifyEditor.SelectedItems`),
    `PendingConnection`.
  - Commands: `StartConnectionCommand` (source port → create pending VM,
    `IsVisible=true`), `CreateConnectionCommand` (target port → call
    `Graph.Connect` with the type rule; on success add `ConnectionVm`, refresh
    `IsConnected` on both ports; on failure keep pending visible with invalid style),
    `DisconnectConnectorCommand` (from connector right-click), `DeleteSelectionCommand`
    (remove selected nodes + their connections from core `Graph` and VMs),
    `AddNodeCommand(NodeType, Point?)` (core `Graph.AddNode` + VM, position = param or
    viewport center), `DuplicateSelectionCommand`, `SelectAllCommand`,
    `FitToScreenCommand` (calls the editor's `FitToScreen()` via a service locator or a
    reference set when the view loads — keep it simple: an `IEditorReference` interface
    the view implements).
  - Subscribes to core `Graph.Changed` / evaluator events to keep VMs in sync
    (add/remove nodes & connections, port changes for Multilayer).
  - Undo/redo: **out of scope here** (STEP-6) — but structure mutations through a single
    `ApplyGraphChange(Action)` seam so STEP-6 can wrap it.

### 4.2 Node content templates (one per type)

In `Views/NodeView.axaml` (or a partial `NodeTemplates.axaml` resource dictionary),
`NodifyEditor.DataTemplates` with a `DataTemplate` per `NodeVm` subtype (or one template
+ per-type `ContentTemplate` chosen by `Type` — your choice, but keep each node's
parameter UI in its own template):

- **Common chrome:** `nodify:Node` with `Header="{Binding Title}"` (editable `TextBox`
  in the header for Layer nodes — the spec requires a layer name input box; for all
  nodes the header is a styled label, editable for Layer), header background =
  `Node.HeaderGradient` (or per-type accent color — assign each node type an accent:
  Layer=violet, Noise=cyan, ColorRamp=magenta, Mix=green, Multilayer=amber,
  Filter=blue, Math=orange, Color=pink — document the mapping), rounded, our theme.
- **Per-type content** (bind to core parameters via the `NodeVm`'s exposed core node):
  - `LayerNode`: name `TextBox` (bound to `Name`) + a small buffer thumbnail
    (updated from evaluator events — can be a low-res `Bitmap` refresh; keep it cheap,
    e.g. 64×64).
  - `NoiseNode`: `ComboBox` Algorithm, `Slider` Scale (0.01–10, log-feel ok),
    `Slider`/`NumericBox` Octaves (1–8), `NumericBox` Seed + "reseed" button, sliders
    OffsetX/OffsetY.
  - `ColorRampNode`: the **`ColorRampBar` custom control** (task 4.5) — a horizontal
    gradient bar, draggable stops, double-click to add stop, right-click stop to
    remove (min 2), each stop a small color swatch with a color dialog on click.
  - `MixNode`: `ComboBox` BlendMode, `Slider` Opacity 0–1.
  - `MultilayerNode`: per-input row in the content: blend `ComboBox` + opacity
    `Slider` (bound to the per-port params); the inputs themselves are the node's
    `NodeInput`s (they grow as connections are made — core handles that).
  - `FilterNode`: `ComboBox` Kind, `Slider` Radius/Amount, `Slider` Aperture (visible
    only for Lens Blur — `IsVisible` binding).
  - `MathNode`: `ComboBox` Operation (Add/Subtract/Multiply/Divide) — the spec's
    dropdown — plus the two input ports.
  - `ColorNode`: a color swatch button opening a color dialog (Avalonia has a built-in
    color editor? no — implement a small `ColorPickerDialog` view with RGBA sliders +
    hex box; or use a 4-slider inline UI. Inline sliders are simpler and fine).
- **Connectors:** `NodeInput`/`NodeOutput` `HeaderTemplate`s show the port label;
  unconnected **scalar** inputs also show an editable value box (the
  `ConnectorVm.Value` TextBox, like the Calculator example) — editing writes
  `Evaluator.SetConstantValue` (STEP-3) and marks dirty.
- **Connections:** `CircuitConnection` template with `CornerRadius=8`, stroke
  `Connection.Stroke`, `DirectionalArrowsCount=1` + `IsAnimatingDirectionalArrows=True`
  (animated flow — the Web3 vibe; if it costs perf, make it a setting).

### 4.3 Node View layout (replace the STEP-1 placeholders)

- **Left panel — palette & search** (`Views/NodePaletteView.axaml` +
  `NodePaletteViewModel`): search `TextBox` (filters the list by name/description),
  `ItemsControl` of the 8 node types (icon + name + one-line description, from a
  static `NodeTypeInfo` registry: type, display name, blurb, accent color, default
  params). Click ⇒ `AddNodeCommand` at viewport center. Drag-out ⇒ create at drop
  point (the Calculator example's drag-drop palette: `PointerPressed/Moved/Released` on
  the item + a `DecoratorContainer` ghost, or simpler: drop onto the editor via
  `DragDrop` with the editor's `AllowDrop=True` — pick one, keep it simple).
- **Center — the editor:** the real `nodify:NodifyEditor` (remove the hidden test one):
  - wire `ItemsSource/Connections/SelectedItems/PendingConnection/
    PendingConnectionTemplate/ConnectionTemplate/DisconnectConnectorCommand/
    ItemContainerTheme` exactly per RESEARCH.md §5;
  - `ItemContainerTheme` = the `ItemContainerStyle` from RESEARCH.md §5
    (Location two-way, IsSelected, ActualSize→Size OneWayToSource, plus our node
    border/rounding);
  - grid background (tile `DrawingBrush` like the Calculator example, subtle
    `Text.Disabled`-ish lines), `GridCellSize=15`, `SnapToGrid` on (nice for
    alignment; keep the snap helper available);
  - `Minimap` in the bottom-right corner (small, glass-styled);
  - `KeyBindings` per PLAN.md §7.1 (Shift+A opens the create menu — a
    `DecoratorContainer` at the cursor listing the 8 types, click to create at that
    point; G handled by Nodify's drag gesture — verify `EditorGestures.Mappings`
    defaults and only override what differs; Delete/X, Ctrl+D, Ctrl+A, F, Escape,
    Ctrl+Z/Ctrl+Shift+Z wired to the STEP-6 seam (no-op until then), Ctrl+S no-op
    until STEP-6).
- **Right panel — image preview** (`Views/PreviewPanelView.axaml` +
  `PreviewViewModel`): shows the document output (`Evaluator.GetOutput()` after each
  evaluation) as an `Image` on a checkerboard background, scaled to fit the panel;
  document size label (`512×512`); "output node: {name}" label. Subscribes to
  `Evaluator.NodeEvaluated`/`Graph.Changed` → refresh (throttle to ~30 fps max).
  Converting `PixelBuffer` → display: `ToRgba8()` → `Bitmap` via
  `Avalonia.Media.Imaging` (write a small `PixelBufferConverter` helper in
  `Services/` — it's the only place core pixels touch Avalonia types).

### 4.4 Evaluation host (`Nodey/Services/EvaluationHost.cs`)

- Owns the `GraphEvaluator` + a background `Task` loop or a
  `CancellationTokenSource`-guarded `Task.Run` per dirty batch:
  - core `Graph.Changed` / `SetConstantValue` / layer paint (later) ⇒ mark
    "needs evaluation" (debounce ~16 ms).
  - on tick: snapshot graph state, `Task.Run(() => evaluator.Evaluate())`, on
    completion marshal `NodeEvaluated` events to the UI thread
    (`Dispatcher.UIThread.Post`) and refresh preview + thumbnails.
  - guard against concurrent evaluations (skip if one is running; coalesce).
- This is the seam STEP-5 uses for layer painting (paint ⇒ `SetLayerBuffer` ⇒
  dirty ⇒ same pipeline).

### 4.5 `ColorRampBar` custom control (`Nodey/Controls/ColorRampBar.cs` + `.axaml` if needed)

- A `Control` (or `UserControl`) ~full-width × 28px: renders the ramp gradient
  (`Render(DrawingContext)` with a `LinearGradientBrush` built from stops), stop
  handles (small diamonds), drag to move (updates position 0..1), double-click empty
  area = add stop at position with current foreground color, right-click handle =
  remove (enforce ≥2), click handle = open color dialog (use the same inline RGBA
  slider dialog as ColorNode, or a simple popup with sliders).
- Two-way with `ColorRamp` (STEP-2): `Ramp` property; raises `RampChanged` on any edit
  (the node VM marks the node dirty).

## Acceptance criteria

- [ ] `dotnet build Nodey.sln -c Release` clean; `dotnet test` still green.
- [ ] Create all 8 node types from the palette (click + search filter works) and via
      `Shift+A` menu at cursor; nodes appear at the right spot with correct chrome and
      per-type parameter UIs.
- [ ] Connect nodes: valid connections (per the accepted-types rule) commit with the
      cyan circuit wire; invalid connections are rejected (pending stays red / is
      cancelled with a tooltip reason); right-click a connection or its connector to
      disconnect; `Delete`/`X` removes selected nodes + wires.
- [ ] Multilayer behavior: connecting a node to its topmost input makes a new input
      appear; disconnecting the last empty input removes it (down to 1).
- [ ] Preview panel updates live: e.g. ColorNode(red) → Mix(Multiply) with
      ColorNode(blue) ⇒ preview goes black; Noise → ColorRamp shows a ramp-colored
      noise image; Math 2+3 with constant inputs shows a uniform field.
- [ ] Unconnected scalar inputs show an editable value box; editing it re-evaluates.
- [ ] Blender hotkeys from PLAN.md §7.1 work (at minimum: Shift+A, Delete/X, Ctrl+D,
      Ctrl+A, F, Escape, G-drag move, wheel zoom, pan).
- [ ] Node positions/selection persist when switching to Draw View and back (both
      views stay alive).
- [ ] No logic in code-behind beyond allowed exceptions (RESEARCH.md/PLAN.md §4).

## Verification

```bash
dotnet build Nodey.sln -c Release
dotnet test
dotnet run --project src/Nodey
# Manual script:
# 1) Create Color(red) + Mix + Color(blue), connect, set Multiply → preview black.
# 2) Create Noise + ColorRamp, connect (test foreign-type path: it's Scalar2D→ramp),
#    tweak stops on the ramp bar → preview changes.
# 3) Shift+A menu, G-drag, Ctrl+D duplicate, Delete, F frame-all.
# 4) Search "ramp" in palette → only Color Ramp listed.
```

## Pitfalls (RESEARCH.md cross-references)

- The **exact** Nodify wiring (ItemContainerTheme, connector ControlThemes with
  `Anchor`/`IsConnected` OneWayToSource, PendingConnection template with
  Started/Completed commands bound to the editor's DataContext) is in RESEARCH.md §5 —
  copy its structure, don't improvise bindings.
- `Decorators` must be an `ArrayList` (XAML: `<collections:ArrayList>`), not a
  `Panel.Children`-style collection.
- Use `Avalonia.Point` in VMs. `Node.Output` is `IEnumerable` → use the
  `ItemToListConverter` for single outputs.
- Don't bind the editor's `ItemsSource` to a collection you mutate without using
  `ObservableCollection` (or `NodifyObservableCollection` for WhenAdded hooks).
- Keep evaluation off the UI thread (4.4); the preview refresh is the only UI-thread
  pixel work.
- If a Nodify resource key you want to override doesn't exist in the package theme,
  style the control directly in our theme instead of guessing key names.

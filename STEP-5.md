# STEP 5 — Draw View (canvas, tools, layers panel, Photoshop hotkeys)

You are building **Nodey** (read `PLAN.md` first, `RESEARCH.md` for framework facts).
This is **step 5 of 6**: turn the Draw View placeholder into a GIMP/Photoshop-style
painting surface for Layer nodes.

## Context

- STEP-1..4 are done: the Node View is a working node editor; `EvaluationHost` runs
  background evaluation and refreshes the Node View preview; core has
  `Graph.SetLayerBuffer` / `GetLayerBuffer`, `GraphEvaluator`, documents.
- The Draw View currently shows a three-column skeleton. Replace it with the real
  drawing UI. Both views stay alive (tab switch must preserve everything).
- **The painted target is a core `LayerNode`'s buffer** — Draw View edits are
  ordinary core mutations (paint ⇒ `SetLayerBuffer` ⇒ dirty ⇒ `EvaluationHost`
  re-evaluates ⇒ Node View preview + layers panel thumbnails update). No parallel
  "draw document" — one graph, one source of truth.

## Tasks

### 5.1 View-model layer (`Nodey/ViewModels/Draw/`)

- **`DrawViewModel`** (root of this view):
  - `Layers` (observable collection of `LayerRowVm`) — rebuilt whenever the graph
    structure changes (subscribe to core `Graph.Changed`): one row per **connected**
    Layer node, ordered **top→bottom = graph right→left** (PLAN.md §6.3): compute
    each layer node's "depth" = length of its longest downstream path to the output
    node (or max `PositionX` as the spec's right→left proxy — use
    **downstream path length, tie-break by greater `PositionX`**; document the choice
    in code). Row: `Name` (two-way with the core node's `Name` — renaming updates the
    node in the graph), `Thumbnail` (small `Bitmap`, refreshed from evaluator events,
    64×64), `IsVisible` (v1: display-only checkbox wired to a per-layer
    `IsVisible` flag on the core `LayerNode` that the evaluator respects — if you
    must, mark the evaluator change in STEP-3 files with a `// STEP-5` note; it's a
    3-line change: skip hidden layers by compositing transparent), `IsSelected`
    (two-way with selection).
  - `SelectedLayer` (single selection; clicking a row selects it and also selects the
    node in the graph's selection model if the Node View is alive — share the
    selection via a small `GraphSelection` service in `Services/` so both views agree).
  - `ActiveTool` (`DrawToolKind`), `ToolProperties` (per-tool state, see 5.3),
    `PreviewMode` (`Full | SelectedLayerOnly`) — the top-center toggle.
  - Commands: `SelectToolCommand(kind)`, `ZoomIn/OutCommand`, `ResetZoomCommand`,
    `DeselectCommand`, `UndoCommand`/`RedoCommand` (wire to the STEP-6 undo seam;
    for this step, implement a **local stroke undo stack** in the canvas VM —
    buffer snapshots per stroke — and route `Ctrl+Z` to it; STEP-6 unifies it).
- **`LayerRowVm`** as above.

### 5.2 Canvas surface (`Nodey/Controls/CanvasSurface.cs` + `CanvasSurfaceViewModel`)

A custom `Control` (allowed code-behind: `Render` + pointer math). Responsibilities:

- **View transform:** `zoom` (0.05–32), `pan` (pixels). Fit-to-view on load and on
  `R`/double-click-empty? (v1: fit on load + a "Fit" button next to the zoom label).
  `Ctrl+wheel` zooms at cursor; `Space`-hold or middle-drag pans (left-drag paints —
  never pans). Show a zoom % label (the view's top bar, not inside the control).
- **Rendering (`Render(DrawingContext)`):**
  - background: checkerboard (transparency indicator) in a subtle dark tone.
  - the image to show, per `PreviewMode`:
    - `Full` (default): the **document output** (latest evaluated output buffer from
      `EvaluationHost` — the spec: "the whole outputted image will be shown by
      default").
    - `SelectedLayerOnly`: the selected layer's buffer (transparent elsewhere).
  - draw via a cached `Avalonia` `Bitmap` (rebuilt only when the underlying buffer
    reference changes — keep a `version` counter on buffers? v1: rebuild when the
    host signals a refresh; it's already throttled).
  - selection overlay: marching-ants rectangle/polygon (animate via a
    `DispatcherTimer` at ~10 fps, offset phase).
- **Painting pipeline (pointer events → core):**
  - on `PointerPressed` (left, tool is a paint tool): begin a stroke — **snapshot the
    layer buffer** (RGBA8 copy) for undo; for selections: box = start rect, lasso =
    start polygon, quick select = run region-grow at the point.
  - on `PointerMoved` (captured): brush/pencil stamp along the segment from the last
    point (interpolate with `max(1, dist/3)` steps so fast strokes don't gap);
    blur tool: apply a small gaussian (radius = tool radius) to a 3×3-ish neighborhood
    around the point (cheap incremental — document the approximation); bucket fill:
    on press only (not a stroke).
  - on `PointerReleased`: end stroke — push the (snapshot, new buffer) pair onto the
    undo stack, `Graph.SetLayerBuffer(selectedLayer, buffer)` ⇒ host re-evaluates ⇒
    everything (preview, thumbnails, Full mode canvas) refreshes.
  - **Selection gating:** while a selection exists, paint tools only write inside the
    mask (mask = `bool[]` W×H; box/lasso fill via even-odd polygon test; quick
    select via region grow). `Esc` clears the selection.
- **Quick Select algorithm** (document in code): from the clicked pixel, BFS 4-
  connectivity where `|lum(p) - lum(seed)| <= tolerance` (tolerance = tool property,
  0..1, default 0.08) — simple, fast, good enough for v1.
- **Bucket fill:** 4-connectivity flood from the clicked pixel, replacing pixels with
  `|lum(p) - lum(seed)| <= threshold` with the current color (respects selection
  gating); iterative stack (no recursion — 512×512 = 262k pixels).
- **Brush:** radial falloff stamp — `alpha = (1 - smoothstep(hardness, 1, dist/r)) *
  opacity`, color = current color; **Pencil:** same but `dist < r*0.5 ? 1 : 0`
  (hard edge), size in pixels (1–200).
- Stamps write into a **working `PixelBuffer` copy** of the layer (the stroke buffer);
  only on stroke end does it become the layer buffer (single undo unit per stroke).

### 5.3 Tools & properties (`Nodey/ViewModels/Draw/Tools/`)

`enum DrawToolKind { Brush, Pencil, BucketFill, Blur, BoxSelect, LassoSelect, QuickSelect }`

Each tool = a small class implementing `IDrawTool { Kind; DisplayName; Glyph (path
data or char); ToolProperties Properties; }` — or, simpler and acceptable: a
`ToolProperties` VM per tool kind with a `Dictionary<DrawToolKind, object>` of live
instances. Properties (bound in the left panel below the icons):

- **Brush:** Size (1–200 px), Hardness (0–1), Opacity (0–1), Color (swatch + RGBA
  sliders, same inline picker as STEP-4's ColorNode).
- **Pencil:** Size (1–50), Color.
- **BucketFill:** **Threshold** (0–1, the spec's named property), Color.
- **Blur:** Radius (1–50), (Strength 0–1 optional).
- **BoxSelect:** Feather (v1: display-only, 0 — document).
- **LassoSelect:** (none beyond shared).
- **QuickSelect:** Tolerance (0–1).
- Shared: **current color** + **last color** (`X` swap), `[`/`]` size step.

Left panel UI: a 2-column icon grid (7 tools, our `Button.Icon` theme, active tool =
accent pill) + a `ContentControl` below showing the active tool's properties template
(`DataTemplate` per tool VM). Tool icons: simple vector paths (brush stroke, pencil,
bucket, blur circle, dashed rect, lasso loop, wand) — hand-drawn `Path` geometries are
fine; keep them 24×24, stroke-based, current-color.

### 5.4 Draw View layout (replace the STEP-1 placeholders)

- **Top center (spec requirement):** the segmented toggle
  **`Full Preview | Selected Layer Only`** (our `Tab.Pill` style), bound to
  `PreviewMode`; plus the zoom label + Fit button (top-right of the canvas area).
- **Left:** tools (5.3). **Center:** `CanvasSurface` inside a glass panel.
  **Right:** layers panel (`ItemsControl` of `LayerRowVm`: 48×48 thumbnail, name
  `TextBox` (transparent until hover/edit — a simple always-editable `TextBox` with
  transparent bg is fine for v1), visibility checkbox; selected row highlighted with
  the accent border; empty state: "No layer nodes — create one in the Node View" with
  a button that jumps to the Node View and adds a Layer node (nice-to-have, cheap).
- **Photoshop hotkeys (PLAN.md §7.2)** as `KeyBinding`s on the Draw View root
  (active only while this view is visible — bind `KeyBinding` visibility or use a
  top-level handler that checks `ActiveView == Draw`): B/P/G/O/M/L/W tool selection,
  `[`/`]` size, D reset color, X swap, Space-pan (handled in the canvas), Ctrl+wheel
  zoom (canvas), Ctrl+Z / Ctrl+Shift+Z undo/redo (stroke stack), Esc deselect/cancel.

### 5.5 Cross-view wiring

- `ShellViewModel` gets the shared services created once (core `Graph` +
  `GraphEvaluator` + `EvaluationHost` + `GraphSelection`) — move construction from
  STEP-4's view-local scope into the shell so both views share them (small refactor;
  keep STEP-4 behavior intact).
- Selecting a layer in the Draw View selects its node in the graph (and vice versa:
  selecting a Layer node in the Node View selects the row).
- `EvaluationHost` refresh events drive: Node View preview (STEP-4), layer thumbnails
  (5.1), and the canvas's Full-mode image (5.2) — one pipeline, three consumers.

## Acceptance criteria

- [ ] `dotnet build Nodey.sln -c Release` clean; `dotnet test` still green.
- [ ] With a graph containing ≥1 Layer node (e.g. Layer → output): the Draw View lists
      it in the right panel with a live thumbnail and editable name; renaming updates
      the node in the Node View.
- [ ] Selecting the layer and painting with Brush/Pencil writes into the layer; the
      stroke appears in **Full Preview** (through the whole graph) and in
      **Selected Layer Only** mode; thumbnails + Node View preview update after the
      stroke.
- [ ] Bucket fill fills a connected region respecting **Threshold**; Blur softens
      along the stroke; Box/Lasso/Quick Select produce a visible selection that gates
      painting; Esc clears.
- [ ] The `Full Preview / Selected Layer Only` toggle works (top center).
- [ ] All PLAN.md §7.2 hotkeys work while the Draw View is active (and don't fire in
      the Node View — e.g. `B` in the Node View must not switch tools).
- [ ] Zoom (Ctrl+wheel at cursor) + pan (Space/middle) work; fit-on-load correct.
- [ ] Undo (Ctrl+Z) reverts the last stroke; redo (Ctrl+Shift+Z) reapplies.
- [ ] Tab-switching to Node View and back preserves canvas zoom/pan, selection, tool
      state, and layers.

## Verification

```bash
dotnet build Nodey.sln -c Release
dotnet test
dotnet run --project src/Nodey
# Manual script (build this graph in the Node View first):
#   Layer "Base" → Multilayer → (output)
# 1) Draw View: layer row appears; select it; B; paint a red circle.
# 2) Toggle Selected Layer Only → only the circle on transparency.
# 3) Toggle Full Preview → same circle (graph passthrough).
# 4) G bucket: threshold 0.1, blue → fills the rest of the layer.
# 5) M box-select a corner → paint green → only the box area changes.
# 6) W quick-select on the red circle → paints only inside it.
# 7) Ctrl+Z ×3 → stroke by stroke back. Ctrl+Shift+Z forward.
# 8) [ and ] change brush size live; B/P/G/O/M/L/W switch tools.
# 9) Switch to Node View: Layer node thumbnail shows the painting.
```

## Pitfalls

- **One source of truth:** never keep a second copy of layer pixels in the UI. The
  canvas shows buffers it borrows; strokes are the only writer.
- Rebuild the Avalonia `Bitmap` for display only when the buffer actually changed
  (reference/version check) — rebuilding a 512×512 bitmap every frame will stutter.
- Pointer coordinates: convert to buffer space with `(pointer - pan) / zoom` and
  clamp; remember the buffer is Y-down (same as screen — no flip).
- Flood fill must be iterative (stack/queue), not recursive.
- `Space`-hold pan must release cleanly on `KeyUp` even when focus moves (handle
  `LostFocus`/`PointerReleased` defensively).
- Keep the stroke buffer copy at stroke start (not per stamp) — that's what makes
  undo a single cheap operation.

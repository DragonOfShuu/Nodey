# STEP 6 — Integration: Documents, Undo/Redo, Performance, Final QA

You are building **Nodey** (read `PLAN.md` first, `RESEARCH.md` for framework facts).
This is **step 6 of 6**: make the whole application cohesive — document open/save with
UI, unified undo/redo, performance pass, and the final quality checklist.

## Context

- STEP-1..5 are done: the app is fully functional (node graph + painting) but:
  - document save/load exists in core (`NodeyDocument.Save/Load`, STEP-3) with **no
    UI** yet;
  - undo is a **local stroke stack** in the Draw View (STEP-5) and a no-op seam in the
    Node View (STEP-4's `ApplyGraphChange` seam);
  - the shell's document name/save button are placeholders (STEP-1).
- Your job: wire it all together and harden it.

## Tasks

### 6.1 Document UI (shell)

- `ShellViewModel` gains: `DocumentName` (editable in the top bar), `IsDirty` flag
  (any core mutation sets it; loading a document clears it), `NewDocumentCommand`,
  `OpenDocumentCommand`, `SaveDocumentCommand`, `SaveAsDocumentCommand`.
- Top bar: document name `TextBox` (or click-to-rename label), Save button (enabled
  when `IsDirty`), Open (file dialog), New (confirm if dirty).
- File dialogs: Avalonia's `IStorageProvider` (`TopLevel.StorageProvider` — get it via
  `Window.StorageProvider` or an `IStorageService` registered in DI; pick one pattern
  and use it consistently). Open = `OpenFilePickerAsync` filtered to `*.nodey`;
  Save/SaveAs = `SaveFilePickerAsync`.
- **Load flow:** `NodeyDocument.Load(path)` ⇒ replace the shell's core `Graph` +
  evaluator state ⇒ **rebuild both views' VM collections** (Node View: clear/rebuild
  `NodeVm`s from the new graph; Draw View: rebuild layer rows; canvas: clear
  selection + stroke stacks). Implement this as `ShellViewModel.LoadDocument(doc)`
  raising a `DocumentReplaced` event the views subscribe to (views must not be
  recreated — just resync).
- **New document:** empty graph, default 512×512, confirm-discard if dirty.
- `Ctrl+S` (both views) = Save (or SaveAs if the document has never been saved).
- Window title: `Nodey — {documentName}{* if dirty}`.

### 6.2 Unified undo/redo

Replace the STEP-5 local stroke stack and the STEP-4 no-op seam with **one**
`UndoService` (`Nodey/Services/`):

- Two stacks (undo/redo), capped at 50 entries each, each entry =
  `UndoEntry { string Label; Action Undo; Action Redo; }` (command pattern — the
  STEP-4 `ApplyGraphChange` seam is where graph entries get pushed).
- Entry kinds (implement all):
  - **Graph ops:** node create / node delete (with its connections) / node move
    (batch all moved nodes into one entry) / connect / disconnect / parameter change
    (old+new values) / name change / Multilayer port add-remove (implicit via
    connect/disconnect entries).
  - **Layer paint:** stroke entries (snapshot before/after — the STEP-5 stack's
    entries, now pushed here).
- `Ctrl+Z` / `Ctrl+Shift+Z` in **both views** route to the service (remove the
  view-local stacks; keep behavior identical for strokes).
- Any new action clears the redo stack. `IsDirty` flips accordingly (optional
  nicety: track a "saved snapshot" to make `IsDirty` precise — v1: any change since
  load/save = dirty, that's enough; document it).
- Unit-test what's testable: the service itself (push/undo/redo/cap/redo-clear) in a
  small test class in `Nodey.Core.Tests`? No — it lives in the UI project; instead
  verify by the manual QA script below. (Keep the service free of Avalonia types so
  it *could* move to core later — use plain `Action`s.)

### 6.3 Performance pass

- **Profile first** (no guessing): run the app, build a ~30-node graph (several
  Noise→Ramp→Mix chains + 3 painted layers + a Multilayer), drag a slider that feeds
  the output, and watch:
  - evaluation time per tick (log it behind a `NODEY_PERF` env flag),
  - UI thread time (should be < 4 ms per refresh),
  - memory (no per-frame allocations in the hot path — check with
    `dotnet-counters` or just reason about the code).
- Expected hot spots & fixes (apply only what the profile shows):
  - preview bitmap rebuilds: ensure they happen **once per evaluation**, not per
    consumer (share one `Bitmap` from the host; consumers draw it).
  - thumbnails: downscale to 64×64 **once** per evaluation and share.
  - noise generation at 512×512 with octaves: if > ~8 ms, precompute the lattice
    per (algorithm, scale, seed) and cache the *un-octaved* field (document the
    cache key).
  - Multilayer folding allocates a buffer per input per evaluation — reuse a scratch
    buffer pool (2 buffers) — only if profiling shows GC pressure.
- **Debounce audit:** parameter sliders must not enqueue an evaluation per tick
  beyond the host's 16 ms coalescing (verify the host coalesces, not queues).
- Keep the app responsive while a large fill (bucket over 512×512) runs: bucket fill
  is synchronous but fast (< 50 ms expected); if it's slow, move it to a task with a
  "filling…" cursor (optional).

### 6.4 Polish & robustness

- **Empty states:** Node View with no nodes → a centered hint ("Press Shift+A or pick
  a node from the palette"); Draw View with no layers → the hint + create button
  (STEP-5 may have added it — verify).
- **Errors:** invalid connections show the `reason` (from `Graph.CanConnect`) as a
  tooltip/toast (a small non-modal toast in the top bar is fine); document load
  failures show a message dialog (corrupt file, version mismatch — `NodeyDocument`
  stores a version int; reject unknown versions with a clear message).
- **Window:** minimum size honored (content doesn't break below 1280×800 — panels
  collapse gracefully: left/right panels get a min width, center takes the rest);
  resize behavior of the canvas (fit-on-resize optional).
- **Fonts:** if `Font.Display` is still Inter Bold (STEP-1/2), bundle one OFL display
  font (e.g. *Rajdhani* — check the license file ships with it) as an
  `AvaloniaResource` and point `Font.Display` at it. (Nice-to-have; do it if time
  permits after QA passes.)
- **README.md** (repo root): 3-paragraph project intro, build/run instructions
  (`dotnet run --project src/Nodey`), hotkey tables (copy from PLAN.md §7), pointer to
  PLAN/RESEARCH/STEP files, license note (MIT if desired).

## Final QA checklist (the project Definition of Done, PLAN.md §11)

Run the **entire** list; fix what fails; then commit.

- [ ] `dotnet build Nodey.sln -c Release` → 0 errors, 0 warnings.
- [ ] `dotnet test` → all green (engine, translator, graph, evaluator, document
      round-trip).
- [ ] App launches on a clean machine profile (no stale state): dark shell, tabs work.
- [ ] **Node View:** all 8 node types creatable (palette click, palette search,
      Shift+A menu); connections validate by type; Multilayer grows inputs;
      Delete/Ctrl+D/Ctrl+A/F/G-drag/Escape work; preview updates live; constant
      value boxes work; minimap + zoom + pan work.
- [ ] **Draw View:** all 7 tools work with property panels; layer list ordering
      (right→left) is correct for a 3-layer graph; names sync with the graph;
      Full/Selected toggle works; zoom/pan; selection gating; bucket threshold.
- [ ] **Undo/redo:** Ctrl+Z/Ctrl+Shift+Z across *both* views, mixing graph ops and
      strokes (script: move node → paint → connect → undo ×3 → redo ×3, verify each
      state).
- [ ] **Documents:** Save → close → Open → identical graph + layer pixels (compare
      visually + a `GetLayerBuffer` hash in a debug build if you want to be
      rigorous); New with confirm; `Ctrl+S`; dirty indicator + title `*`.
- [ ] **Hotkey tables** (PLAN.md §7.1/§7.2) fully honored per view.
- [ ] **Look:** matches PLAN.md §8 (dark navy, glass panels, neon accents, rounded
      corners, display font on titles, animated/rounded wires).
- [ ] Performance: dragging a Noise `Scale` slider on a 30-node graph keeps the UI at
      ~60 fps (preview may lag one frame — fine; UI thread must not).
- [ ] `git log` shows the per-step commits (add `STEP-6: ...` at the end).

## Commit

```
git add -A && git commit -m "STEP-6: documents UI, unified undo/redo, perf pass, QA"
```

## Pitfalls

- **Load = full resync, not partial patch:** the simplest correct load is "replace
  everything and rebuild VMs". Don't try to diff old vs new graph.
- Undo entries that capture core objects by reference must survive node removal
  (a "recreate node" undo must re-add the node *and* its connections — capture the
  full node state, not a live reference).
- Don't let `Ctrl+Z` in the Draw View's `TextBox` (layer name) be swallowed by the
  view-level binding (and vice versa) — scope key bindings to the right controls.
- File dialog APIs are async — don't block the UI thread; show a busy state if the
  file is large.
- The QA checklist is the contract. If something on it can't pass, fix the code, not
  the checklist (unless the item contradicts PLAN.md — then flag it in the commit
  message).

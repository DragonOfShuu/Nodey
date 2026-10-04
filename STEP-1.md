# STEP 1 — Scaffold, App Shell & Theme

You are building **Nodey** (see `PLAN.md` for the full picture, `RESEARCH.md` for
framework facts). This is **step 1 of 6**: create the solution, the runnable app shell
(window + tab bar + two empty views), and the complete Web3×Unreal theme. No node graph
and no painting yet — but everything must **build and run**, and the Nodify package must
be installed and its theme included (so later steps can drop controls in immediately).

## Context

- The repo currently contains only the plan documents. You are creating everything.
- Target: **.NET 9**, **Avalonia 11.3.x** (NOT 12 — see RESEARCH.md §2),
  **NodifyAvalonia 6.6.0**.
- Verify tooling first: `dotnet --version` (need 9.x SDK; if absent, install .NET 9 SDK
  before proceeding).

## Tasks

### 1.1 Solution & projects

Create (via CLI where possible, so the boilerplate is the official Avalonia template):

1. `dotnet new install Avalonia.Templates` (if not already installed), then
   `dotnet new avalonia.app -n Nodey -o src/Nodey -f net9.0` (adjust flags for the
   installed template; the result must be a standard Avalonia 11 app: `Program.cs`,
   `App.axaml`, `MainWindow.axaml`, `.csproj` with `Avalonia`, `Avalonia.Desktop`,
   `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`).
2. `dotnet new sln -n Nodey` at repo root; add the app project.
3. `dotnet new classlib -n Nodey.Core -o src/Nodey.Core -f net9.0` (no Avalonia refs —
   add them in later steps; for now it must stay UI-free, PLAN.md §3).
4. `dotnet new xunit -n Nodey.Core.Tests -o tests/Nodey.Core.Tests -f net9.0`; reference
   `Nodey.Core`.
5. `Directory.Build.props` at root: `<TargetFramework>net9.0</TargetFramework>`,
   `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`,
   `<AvaloniaVersion>11.3.0</AvaloniaVersion>` (property so csprojs can reuse it).
6. `.gitignore` (dotnet + OS noise).
7. Pin the app's csproj packages with **exact** versions (no floating):
   `Avalonia*` 11.3.x, plus `<PackageReference Include="NodifyAvalonia" Version="6.6.0" />`.

**Sanity check before moving on:** `dotnet build Nodey.sln -c Release` → 0 errors.

### 1.2 App shell (MVVM)

MVVM is mandatory (PLAN.md §4): views = layout + bindings only.

1. **`ViewModels/ShellViewModel.cs`** — holds:
   - `ActiveView` (enum `ViewKind { Node, Draw }` or two bools) with
     `SelectNodeCommand` / `SelectDrawCommand` (or a `SelectViewCommand(ViewKind)`).
     This is the only state the tab bar needs.
   - (Later steps add `Document` here — leave a clearly marked `// STEP-6` seam, do not
     implement.)
2. **`Views/ShellView.axaml`** (or put the shell directly in `MainWindow`) — a top bar:
   - Left: app logo/title "Nodey" in the display font.
   - Center: two tab buttons **Node View** / **Draw View** (styled as a segmented pill
     control, accent-colored when active — bind `IsSelected`-style via the VM).
   - Right: (placeholder) document name + save button (disabled until STEP-6).
   - Below: a `ContentControl`/`Panel` that shows `NodeView` or `DrawView` based on
     `ActiveView` (use two views with `IsVisible` bindings — simplest and keeps both
     alive, which is what we want: state must survive tab switches).
3. **`Views/NodeView.axaml`** — three-column `Grid` (`Auto`/`*`/`Auto` → e.g.
   260px / * / 320px) with placeholder panels labeled "Node Palette", "Node Graph",
   "Image Preview" (each in its own glass panel, so the layout skeleton is visible).
4. **`Views/DrawView.axaml`** — same three-column skeleton: "Tools", "Canvas", "Layers".
5. `App.axaml`: `RequestedThemeVariant="Dark"`; merge **`avares://Nodify/Theme.axaml`**
   (RESEARCH.md §3) *plus* our theme dictionaries (next task). Keep `<FluentTheme/>` in
   `Application.Styles` as the base.
6. `Program.cs`: keep template startup; `.WithInterFont()`; window
   `MinWidth=1280 MinHeight=800`, `ExtendClientAreaToDecorationsHint=True` (rounded
   client area on Windows; harmless elsewhere).

### 1.3 Theme — Web3 × Unreal (PLAN.md §8)

Create `src/Nodey/Themes/` with:

1. **`Palette.axaml`** — every color from PLAN.md §8 as `SolidColorBrush` resources
   (`Background.Base`, `Background.Panel`, `Background.Elevated`, `Accent.Cyan`,
   `Accent.Magenta`, `Accent.Violet`, `Accent.Green`, `Accent.Amber`, `Text.Primary`,
   `Text.Secondary`, `Text.Disabled`, `Node.HeaderGradient` (a
   `LinearGradientBrush` #6366F1→#A855F7→#EC4899), `Connection.Stroke` (cyan),
   `Selection.Glow` (violet)).
2. **`Fonts.axaml`** — `FontFamily` resources: `Font.Ui` (Inter), `Font.Mono` (Inter
   for now; a mono font can be bundled later), `Font.Display` (Inter Bold for now —
   bundle an OFL display font in STEP-6 if desired).
3. **`Controls.axaml`** — base control themes (each `BasedOn` the Fluent default so we
   only override what differs):
   - `Panel.Glass` (Border-based "glass panel" style: `Background.Panel` at ~0.85
     opacity, 1px `#FFFFFF33` border, `CornerRadius=12`, subtle drop shadow).
   - `Tab.Pill` (the segmented tab buttons: rounded, accent background + white text when
     active, `Text.Secondary` when not, hover brightens).
   - `Button.Primary` (accent violet, rounded 8, glow on hover),
     `Button.Icon` (square 32×32, rounded 8, hover bg `Background.Elevated`).
   - `TextBox`, `Slider`, `ComboBox` dark restyles (rounded 8, `Background.Elevated`
     fill, accent focus border) — used heavily by node parameters in STEP-4/5.
   - `ScrollViewer` thin dark scrollbars.
4. **`NodifyTheme.axaml`** — *override layer for Nodify* (applied after the package
   theme): set the Nodify resource keys our app uses to our palette (node background
   `Background.Elevated`, node header `Node.HeaderGradient`, connector fill/border
   `Accent.Cyan`, `CircuitConnection` stroke `Connection.Stroke` with
   `CornerRadius=8`, selection outline `Accent.Violet`). Nodify's keys follow the
   package's `Themes/Dark.xaml` — open that file (in the NuGet package cache,
   `~/.nuget/packages/nodifyavalonia/6.6.0/...`) to copy exact key names; where a key
   doesn't exist, set the property via a `ControlTheme` on the control in the view
   instead. **Do not** edit package files.
5. Wire the merge order in `App.axaml`:
   `Nodify/Theme.axaml` first, then `Palette.axaml`, `Fonts.axaml`, `Controls.axaml`,
   `NodifyTheme.axaml` last (so ours win).

## Acceptance criteria

- [ ] `dotnet build Nodey.sln -c Release` → 0 errors, 0 warnings.
- [ ] `dotnet run --project src/Nodey` opens a dark 1280×800 window with the "Nodey"
      title, a working Node View / Draw View tab switch (both views render their
      three-column glass-panel skeletons; content persists across switches).
- [ ] The Nodify package is referenced and its theme is merged (provable by building a
      minimal `<nodify:NodifyEditor>` — add a **hidden** test: put an empty
      `nodify:NodifyEditor` (500×300) inside the Node View center panel behind the
      placeholder, so the XAML proves the theme/namespace works; STEP-4 will replace it).
- [ ] Tab switching, colors, and rounded panels visibly match PLAN.md §8 (dark navy
      background, glass panels, neon accents, display font on the title).
- [ ] `Nodey.Core` and `Nodey.Core.Tests` build and `dotnet test` passes (0 tests is
      OK for now — the project must exist and compile).
- [ ] No code-behind logic: `MainWindow`/views' `.cs` files contain only
      `InitializeComponent` (and constructor DI if any).

## Verification

```bash
dotnet build Nodey.sln -c Release
dotnet test
dotnet run --project src/Nodey   # visually confirm the shell
```

## Pitfalls (from RESEARCH.md)

- **Avalonia 12 is out there and tempting — don't use it** (NodifyAvalonia 6.6.0 is
  validated against 11.1.0; 11.3.x is our pin).
- Use `Avalonia.Point`/`Avalonia.Size` in any VM you write (not `System.Windows.*`).
- Nodify's theme include is `avares://Nodify/Theme.axaml` (single file; it merges the
  rest). Don't hand-merge `Nodify/Themes/*` instead of it.
- Keep `Nodey.Core` free of Avalonia references from day one — it's a hard rule that
  becomes expensive to break later.

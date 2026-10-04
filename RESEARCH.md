# Nodey — Research Findings

Research performed against the **nodify-avalonia** repository (github.com/BAndysc/nodify-avalonia),
the **NodifyAvalonia** NuGet package, and the **Avalonia** framework. Read this file before
implementing any step — it resolves the most common sources of confusion.

---

## 1. Package identity (do not mix these up)

| Package | Author | What it is |
|---|---|---|
| **`NodifyAvalonia`** | BAndysc | ⭐ **This is the one we use.** Direct port of miroiu's Nodify (WPF) to Avalonia. |
| `Nodify` | miroiu | The original **WPF-only** library. Not usable from Avalonia. |
| `Nodify.Avalonia` | (someone else) | A **different, unrelated** port to Avalonia 12. **Do not install this.** |

The nodify-avalonia README explicitly warns:
> ⚠️ Please do not confuse with `Nodify.Avalonia` which is a different package ⚠️

## 2. Version compatibility — pin Avalonia to 11.x

- Latest **NodifyAvalonia** = **6.6.0** (as of research date).
- Official compatibility chart from the repo:

  | NodifyAvalonia | Avalonia |
  |---|---|
  | 6.6.0 | 11.1.0 |
  | 6.5.0 | 11.1.0 |
  | 6.2.0 | 11.1.0 |

- **Avalonia 12.x is the current stable line** of the framework itself, but NodifyAvalonia has
  **no tested support for Avalonia 12**. Do **not** use Avalonia 12.
- The package targets `netstandard2.0`, so it loads into any modern .NET (we use **.NET 9**).
- **Decision: use Avalonia 11.3.x** (latest 11.x service release; backward compatible with the
  11.1.0 API Nodify was built against). If anything breaks, fall back to exactly **11.1.0**.

```xml
<PackageReference Include="Avalonia" Version="11.3.*" />
<PackageReference Include="Avalonia.Desktop" Version="11.3.*" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.*" />
<PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.*" />
<PackageReference Include="NodifyAvalonia" Version="6.6.0" />
```

## 3. Theme inclusion

The package ships a single theme entry point:

```xml
<ResourceInclude Source="avares://Nodify/Theme.axaml" />
```

`avares://Nodify/Theme.axaml` merges `avares://Nodify/Themes/Generic.xaml` (control templates)
plus theme dictionaries:

```xml
<ResourceDictionary.ThemeDictionaries>
    <ResourceInclude x:Key="Default" Source="avares://Nodify/Themes/Light.xaml" />
    <ResourceInclude x:Key="Dark"    Source="avares://Nodify/Themes/Dark.xaml" />
</ResourceDictionary.ThemeDictionaries>
```

So the Nodify controls automatically follow the app's `ThemeVariant` (Light/Dark).
**Nodey is dark-only in practice** (`RequestedThemeVariant="Dark"`) and we layer our own
Web3×Unreal palette on top by defining our own resource keys (see PLAN.md § Visual Design).

## 4. The nodify-avalonia control inventory (API surface we build on)

All controls live in the `Nodify` namespace; the XAML prefix in the examples is
`xmlns:nodify="https://miroiu.github.io/nodify"` (kept for parity with the WPF docs).

### `NodifyEditor` (the canvas)
Key properties (all `AvaloniaProperty`s, bindable):

- `ItemsSource` (IEnumerable) — the node view models.
- `Connections` (IEnumerable) — the connection view models.
- `SelectedItems` (IList, two-way) / `SelectedConnections` / `SelectedConnection`.
- `PendingConnection` (object) — the in-progress drag connection VM.
- `ConnectionTemplate`, `PendingConnectionTemplate`, `DecoratorTemplate` (DataTemplate).
- `ItemContainerTheme` (ControlTheme) — applied to every node container (this is where you bind
  `Location`, `IsSelected`, `ActualSize` — see §5).
- `DisconnectConnectorCommand` (ICommand) — invoked when a connector is right-click-disconnected.
- `ViewportZoom`, `MinViewportZoom`, `MaxViewportZoom`, `ViewportLocation`, `ViewportSize`,
  `ViewportTransform`, `DpiScaledViewportTransform`.
- `GridCellSize` (uint) + `SnapToGrid(double value)` static/instance helper — grid snapping.
- `ItemsExtent`, `DecoratorsExtent`, `SelectedArea`, `IsSelecting`, `IsPanning`, `MouseLocation`.
- `DisableZooming`, `DisablePanning`, `DisableAutoPanning`, `AutoPanSpeed`, `AutoPanEdgeDistance`.
- `BringIntoView(Rect?, ...)`, `FitToScreen(Rect?)` methods; `FitToScreenExtentMargin` static.
- `EnableRealtimeSelection`, `EnableDraggingContainersOptimizations`.
- `Decorators` (IEnumerable of `DecoratorContainer`) — floating panels that live in editor space
  (e.g. a node-creation menu that follows the cursor).
- `KeyBindings` — standard Avalonia `KeyBinding` collection, works per-editor.
- Commands: `EditorCommands.ZoomIn / ZoomOut / SelectAll / BringIntoView / FitToScreen /
  Align / ...` are `RoutedUICommand`s wired to the default gestures.

### `ItemContainer` (base of every node)
- `Location` (Point, two-way) — node position in graph space.
- `ActualSize` (Size, **OneWayToSource** into the VM's `Size` property).
- `IsSelected` (bool, two-way).
- `Draggable`, `IsDraggableHost`, `MovementMode` (Self/Content) etc.
- Gestures (drag, select) are configurable via `EditorGestures.Mappings.ItemContainer`.

### `Node` (the visual node box)
- `Header` (object) + `HeaderTemplate`, `Content` (object) + `ContentTemplate`,
  `Footer` + `FooterTemplate`, `HasFooter` (direct bool).
- `Input` (IEnumerable of connector VMs), `Output` (IEnumerable; a single output VM is fine —
  the examples pass a single VM or a list; a `ItemToListConverter` is used when Output is a
  single object and the property expects IEnumerable).
- `ContentBrush`, `HeaderBrush`, `FooterBrush` — per-zone background brushes.
- `InputConnectorTemplate`, `OutputConnectorTemplate` — DataTemplates for the connector rows.
- `ContentContainerStyle`, `HeaderContainerStyle`, `FooterContainerStyle` (ControlTheme, 6.2+).
- `VerticalAlignment` affects connector panel placement.

### `NodeInput` / `NodeOutput` (connectors)
- `Header` (object) + `HeaderTemplate` — the label/content next to the port.
- `IsConnected` (bool, two-way into VM).
- `Anchor` (Point, **OneWayToSource** into the VM) — the port's graph-space position,
  written back by the control.
- `Orientation` (Horizontal/Vertical, 6.0+), `Spacing` etc.
- `IsPendingConnection` (direct bool).

### `Connector` (base of NodeInput/NodeOutput)
- `Anchor` (Point), `IsConnected`, `DisconnectCommand` (ICommand),
  `EnableStickyConnections` (two-step connect, 4.0+).

### Connections
- `BaseConnection` — `Source`/`Target` (Point anchors), `SourceOffset`/`TargetOffset`
  (Size, mode Static/Revolve), `SourceOrientation`/`TargetOrientation`, `Direction`,
  `DirectionalArrowsCount`/`IsAnimatingDirectionalArrows` (animated flow arrows!),
  `Spacing`, `ArrowSize`, `ArrowEnds`, `ArrowShape`, `OutlineBrush`/`OutlineThickness`
  (selection halo), `Text` + text styling (5.2+), `SplitCommand`, `DisconnectCommand`,
  `CornerRadius` (6.6+), `Text` display.
- Built-in shapes: **`CircuitConnection`** (rounded-rectangle flow, the "Blender" look),
  `LineConnection`, `StepConnection` (6.1+).
- `PendingConnection` — `Source`, `Target` (object, OneWayToSource), `SourceAnchor`,
  `TargetAnchor` (Point, OneWayToSource), `PreviewTarget`, `EnablePreview`,
  `EnableSnapping`, `AllowOnlyConnectors`, `IsVisible` (two-way), `StartedCommand`,
  `CompletedCommand`.
- `CuttingLine` exists (6.3+) but **cutting is effectively unsupported** in the Avalonia port
  (blocked by AvaloniaUI/Avalonia#16549). Don't rely on it; use right-click disconnect +
  Delete key on selected connections instead.

### `GroupingNode`
- `Header` + `HeaderTemplate`, `CanResize`, `ActualSize` (two-way into VM `GroupSize`/
  `DesiredSize`), `MovementMode`, `SwitchMovementModeModifierKey`.

### `Minimap` (6.2+)
- Drop a `nodify:Minimap` inside the editor (it's an editor "decorator" style control):
  `ViewportStyle`, `ItemsExtent`, `MaxViewportOffset`, `ResizeToViewport`, `Zoom` routed event.
  `EditorGestures.Mappings.Minimap` configures its drag gesture.

### `DecoratorContainer`
- `Location` (Point), `DataContext` — floating UI in graph space (the Calculator example uses
  this for the Shift+A "create node" menu that appears at the cursor).

## 5. The MVVM contract (from the official Calculator example)

This is the **exact pattern** to replicate. VMs implement `INotifyPropertyChanged`; the
controls push state *into* the VMs via `OneWayToSource` bindings set in `ItemContainerTheme`
and connector `ControlTheme`s:

```xaml
<!-- The one place where Location/Size/Selection flow from control to VM -->
<ControlTheme x:Key="ItemContainerStyle" TargetType="nodify:ItemContainer"
              BasedOn="{StaticResource {x:Type nodify:ItemContainer}}">
    <Setter Property="Location"   Value="{Binding Location}" />            <!-- two-way -->
    <Setter Property="IsSelected" Value="{Binding IsSelected}" />
    <Setter Property="ActualSize" Value="{Binding Size, Mode=OneWayToSource}" />
</ControlTheme>

<!-- Per-connector: Anchor + IsConnected flow back into the VM -->
<ControlTheme TargetType="nodify:NodeInput" x:Key="{x:Type nodify:NodeInput}" ...>
    <Setter Property="Header"     Value="{Binding}" />
    <Setter Property="IsConnected" Value="{Binding IsConnected}" />
    <Setter Property="Anchor"     Value="{Binding Anchor, Mode=OneWayToSource}" />
    ...
</ControlTheme>
```

Required VM shape (mirrors `OperationViewModel` / `ConnectorViewModel` /
`ConnectionViewModel` / `PendingConnectionViewModel` in the example):

- **NodeVM**: `Location` (Point), `Size` (Size), `Title`, `IsSelected`,
  `Input` (observable collection of ConnectorVM), `Output` (ConnectorVM or collection).
- **ConnectorVM**: `Title`, `IsConnected`, `IsInput`, `Anchor` (Point), `Operation` (back-ref),
  plus whatever payload it carries.
- **ConnectionVM**: `Input` (ConnectorVM) + `Output` (ConnectorVM).
- **PendingConnectionVM**: `Source`, `Target`, `TargetLocation`, `IsVisible`.

Editor wiring (mirrors `EditorView.xaml`):

```xaml
<nodify:NodifyEditor ItemsSource="{Binding Operations}"
                     Connections="{Binding Connections}"
                     SelectedItems="{Binding SelectedOperations}"
                     PendingConnection="{Binding PendingConnection}"
                     PendingConnectionTemplate="{StaticResource PendingConnectionTemplate}"
                     ConnectionTemplate="{StaticResource ConnectionTemplate}"
                     DisconnectConnectorCommand="{Binding DisconnectConnectorCommand}"
                     ItemContainerTheme="{StaticResource ItemContainerStyle}"
                     GridCellSize="15">
    <nodify:NodifyEditor.DataTemplates>
        <DataTemplate DataType="{x:Type local:MyNodeVm}">
            <nodify:Node Header="{Binding Title}" Input="{Binding Input}"
                         Output="{Binding Output, Converter={StaticResource ItemToListConverter}}" />
        </DataTemplate>
    </nodify:NodifyEditor.DataTemplates>
    <nodify:NodifyEditor.KeyBindings>
        <KeyBinding Gesture="Delete" Command="{Binding DeleteSelectionCommand}" />
    </nodify:NodifyEditor.KeyBindings>
</nodify:NodifyEditor>
```

Connection templates:

```xaml
<DataTemplate x:Key="ConnectionTemplate" DataType="local:ConnectionVm">
    <nodify:CircuitConnection Source="{Binding Output.Anchor}"
                              Target="{Binding Input.Anchor}" />
</DataTemplate>
<DataTemplate x:Key="PendingConnectionTemplate" DataType="local:PendingConnectionVm">
    <nodify:PendingConnection IsVisible="{Binding IsVisible}"
                              Source="{Binding Source, Mode=OneWayToSource}"
                              Target="{Binding Target, Mode=OneWayToSource}"
                              TargetAnchor="{Binding TargetLocation, Mode=OneWayToSource}"
                              StartedCommand="{Binding DataContext.StartConnectionCommand,
                                              RelativeSource={RelativeSource AncestorType=nodify:NodifyEditor}}"
                              CompletedCommand="{Binding DataContext.CreateConnectionCommand,
                                               RelativeSource={RelativeSource AncestorType=nodify:NodifyEditor}}" />
</DataTemplate>
```

The editor VM must implement: `StartConnectionCommand` (creates the pending VM, sets
`IsVisible`), `CreateConnectionCommand` (validates types, appends to `Connections`, removes the
pending VM), `DisconnectConnectorCommand`, `DeleteSelectionCommand`, and node-creation
commands. Selection changes come from `SelectedItems`.

## 6. Type gotchas (read before writing any VM)

- **Use `Avalonia.Point` / `Avalonia.Size`, NOT `System.Windows.Point`.** The examples carry
  `using System.Windows;` leftovers and the package ships *empty placeholder namespaces*
  (`Nodify.Compatibility.EmptyNamespaces`) so those usings compile — but the real type is
  `Avalonia.Point`. The README states this explicitly.
- `Decorators` must be an `IEnumerable` that is **not** a `UIElementCollection`; the examples
  use `System.Collections.ArrayList` (declared as `<collections:ArrayList>` in XAML).
- `Node.Output` is `IEnumerable` — when a VM exposes a *single* output VM, convert with the
  example's `ItemToListConverter` or expose a single-element list.
- `EditorGestures` is a **singleton instance** (`EditorGestures.Mappings`), not a static class
  (changed in 6.0). It is mutable at runtime — you can reassign gestures in code (e.g. in
  `App` startup) to implement Blender-style hotkeys. Subclasses are also supported.
- `NodifyObservableCollection<T>` (from the example's `Nodify.Shared` project) adds
  `WhenAdded`/`WhenRemoved` hooks — **it is not in the NuGet package**. Either vendor it into
  our solution (it's ~100 lines) or use a plain `ObservableCollection` + `CollectionChanged`.
  Same for `ObservableObject`/`SetProperty(...).Then(...)`, `DelegateCommand`, and the shared
  converters — they live in `Examples/Nodify.Shared`, which is sample code we may copy.
- `ItemContainer`s cannot exist outside a `NodifyEditor` (4.0+).
- `BringIntoView`/`ZoomAtPosition`/public geometry APIs all take **graph-space** coordinates.
- The `Nodify.Shapes` example demonstrates custom cursors, canvas decorators, and
  undo/redo integration — useful reference for our Draw View.

## 7. Avalonia 11 specifics that matter here

- XAML files are `.axaml` (the Nodify examples use `.xaml` + a build-time rename; the official
  Avalonia templates use `.axaml` — either works, pick `.axaml` for consistency with
  `avalonia new`).
- Theming: `FluentTheme` (via `<Application.Styles>`) + `ResourceDictionary` merges in
  `App.axaml`. `ControlTheme BasedOn="{StaticResource {x:Type X}}"` extends built-in control
  themes. `DynamicResource` for theme-switchable brushes.
- `RequestedThemeVariant="Dark"` on `Application`.
- Fonts: `Avalonia.Fonts.Inter` package + `.WithInterFont()` in `Program.cs`. For a more
  "Unreal Engine" display font, bundle an OFL font (e.g. Orbitron/Rajdhani for headers,
  Inter for UI, JetBrains Mono for numbers) as `AvaloniaResource` and reference via
  `FontFamily` resource keys.
- KeyBindings: `<KeyBinding Gesture="Shift+A" Command="..."/>` on any control; gestures use
  `+` for modifiers and `Ctrl`/`Alt`/`Shift`.
- Custom rendering: a custom `Control` overriding `Render(DrawingContext)` +
  `PointerPressed/Moved/Released` is the standard way to build the Draw View canvas
  (Avalonia has no WPF-style `OnRender` override name; it's `Render`).
- SkiaSharp: Avalonia's Skia backend means `Avalonia.Media` types (e.g. `Bitmap`) are Skia-
  backed; for raw pixel work use `SkiaSharp` directly (`SKBitmap`/`SKImage`) or plain
  `byte[]`/`float[]` buffers — keep the engine UI-independent.
- Threading: UI updates must be on the UI thread; heavy pixel work goes to
  `Task.Run` + `Dispatcher.UIThread.Invoke` (or `Avalonia.Threading.Dispatcher`).
- Testing: `Avalonia.Headless` / `Avalonia.Headless.XUnit` exists for UI tests, but for Nodey
  the testable core (engine + graph) is UI-free — plain xUnit is enough.

## 8. NodifyAvalonia 6.6.0 feature notes (what we get for free)

- `CornerRadius` on `LineConnection`/`CircuitConnection`/`StepConnection` (6.6) — rounded wires,
  part of our aesthetic.
- `EditorGestures.Editor.PushItems` + `PushedAreaStyle`/`PushedAreaOrientation`/
  `IsPushingItems` (6.6) — push nodes aside to make room.
- `Minimap` (6.2), `SnapToGrid` (6.6), `BringIntoView(Rect)` (6.2),
  `SelectedConnection(s)` + connection selection gestures (6.5),
  `PanWithMouseWheel` + `PanHorizontalModifierKey`/`PanVerticalModifierKey` (6.6).
- `ContentContainerStyle`/`HeaderContainerStyle`/`FooterContainerStyle` on `Node` (6.2).
- **Not supported in the Avalonia port:** `InputGroupStyle`/`OutputGroupStyle` (6.6, WPF-only).
- **Unsupported (blocked upstream):** cutting lines (Avalonia#16549).

## 9. Where to copy reference code from

The nodify-avalonia repo's `Examples/` folder (MIT) is the canonical reference. Files most
relevant to Nodey (paths relative to repo root):

| File | Why |
|---|---|
| `Examples/Nodify.Calculator/EditorView.xaml` | Full editor wiring: templates, ItemContainerTheme, KeyBindings, Decorators, drag-drop palette panel. |
| `Examples/Nodify.Calculator/CalculatorViewModel.cs` | Editor VM: selection, delete, group, disconnect, pending connection lifecycle. |
| `Examples/Nodify.Calculator/OperationViewModel.cs` | Node VM with input-change → recompute pattern. |
| `Examples/Nodify.Calculator/Operations/*` | A small "operation" abstraction (factory, container) mirroring our node-type system. |
| `Examples/Nodify.Shared/NodifyObservableCollection.cs` | `WhenAdded`/`WhenRemoved` collection (vendor it). |
| `Examples/Nodify.Shared/ObservableObject.cs` | `SetProperty(...).Then(...)` base (vendor it). |
| `Examples/Nodify.Shapes/Canvas/*` | Custom canvas gestures, decorators, undo/redo — reference for Draw View. |
| `Examples/Nodify.Playground/Editor/*` | Every Nodify setting exercised; `NodifyEditorViewModel.cs` is a tour of the API. |

## 10. Performance expectations

- Nodify is "optimized for interactions with hundreds of nodes at once" (README). Our graph is
  small (dozens of nodes) — no concern.
- The real performance risk is **pixel work** (noise generation, filters on full-res buffers).
  Mitigations are in PLAN.md § Image Engine: fixed canvas resolution, float32 buffers,
  background-thread evaluation, dirty-flag propagation, preview downscaling.

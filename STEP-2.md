# STEP 2 — Image Engine (Nodey.Core/Image)

You are building **Nodey** (read `PLAN.md` first, `RESEARCH.md` for framework facts).
This is **step 2 of 6**: implement the pure, UI-free pixel engine in `Nodey.Core` with
full unit test coverage. No UI work in this step.

## Context

- STEP-1 is done: solution builds; `src/Nodey.Core` and `tests/Nodey.Core.Tests` exist
  (classlib + xUnit). `Nodey.Core` has **no** Avalonia references and must keep none.
- Everything you write here is the math that every node type later depends on.
  Precision matters more than speed, but keep hot loops tight (float arithmetic, no
  allocations in inner loops).

## Tasks

### 2.1 `PixelBuffer` (`Nodey.Core/Image/PixelBuffer.cs`)

- `readonly int Width, Height; float[] Rgba;` (length `Width*Height*4`, RGBA, each
  channel 0..1, straight alpha).
- API:
  - `static PixelBuffer Empty(int w, int h)`
  - `static PixelBuffer Solid(int w, int h, float r, float g, float b, float a = 1f)`
  - `PixelBuffer Clone()`
  - `void Fill(float r, float g, float b, float a = 1f)`
  - `void GetPixel(int x, int y, out float r, out float g, out float b, out float a)`
  - `void SetPixel(int x, int y, float r, float g, float b, float a = 1f)`
  - `float Luminance(int x, int y)` — `0.2126r + 0.7152g + 0.0722b` (on linear-ish
    stored values; document that inputs are treated as display-referred sRGB and we
    skip gamma correction in v1 — consistent everywhere is what matters).
  - `byte[] ToRgba8()` / `static PixelBuffer FromRgba8(byte[] data, int w, int h)`
    (8-bit ↔ float conversion, `v/255f`).
  - `PixelBuffer ScaleTo(int w, int h)` — bilinear (alpha-aware).
  - `bool SameSizeAs(PixelBuffer other)`
- Bounds checks: throw `ArgumentOutOfRangeException` with clear messages (tests assert
  this).

### 2.2 Noise generators (`Nodey.Core/Image/Noise/`)

Interface + five implementations (PLAN.md §4.2 requires **at least 4**; we do 5):

```csharp
public interface INoiseGenerator
{
    // value in 0..1 at (x, y) in *noise space* (caller applies Scale/Offset)
    float Sample(float x, float y, int octaves, float persistence, float lacunarity);
}
```

- `PerlinNoiseGenerator` — classic improved Perlin (gradient lattice, fade curve,
  fBm via `octaves`).
- `SimplexNoiseGenerator` — 2D simplex (Gustavson).
- `ValueNoiseGenerator` — value lattice + smoothstep interpolation + fBm.
- `CellularNoiseGenerator` — Worley (distance to nearest feature point), fBm optional.
- `RandomNoiseGenerator` — white noise (hash of pixel coords; no state).
- **All must be deterministic given `(x, y, seed)`** — implement a small
  `NoiseRandom`/hash helper (e.g. integer hash → [0,1)) so a `Seed` parameter produces
  identical output across runs and platforms (no `System.Random` with unseeded
  default, no `Random.Shared`).
- `NoiseField` helper: `static PixelBuffer Generate(NoiseAlgorithm algo, int w, int h,
  float scale, int octaves, float persistence, float lacunarity, int seed, float offsetX, float offsetY)`
  producing a `Scalar2D`-style buffer (value stored in R channel, G=B=0, A=1 — document
  the Scalar2D storage convention: **value in R, A=1**).
- `NoiseAlgorithm` enum: `Perlin, Simplex, Value, Cellular, Random`.

### 2.3 Color ramp (`Nodey.Core/Image/ColorRamp.cs`)

Blender-style (PLAN.md §4.2):

- `RampStop { float Position; float R, G, B, A; }` (position 0..1, sorted).
- `ColorRamp` with `IReadOnlyList<RampStop> Stops` (enforce ≥2 stops; auto-sort by
  position on set).
- `Color Evaluate(float t)` — for a scalar input `t` (0..1 = dark→light):
  - `t <= first.Position` → first stop's color (clamp).
  - `t >= last.Position` → last stop's color (clamp).
  - else find the bracketing stops, interpolate each channel
    `(t - p0) / (p1 - p0)` linear. (Blender offers more interpolation modes; linear is
    v1 — structure the code so an `InterpolationMode` enum can be added.)
- `PixelBuffer Apply(PixelBuffer scalarField)` — per pixel: `t = field.R[x,y]` →
  `Evaluate(t)` → RGBA output (alpha from stop).
- Default ramp: black (0) → white (1).

### 2.4 Blend modes (`Nodey.Core/Image/BlendModes.cs`)

`enum BlendMode { Normal, Multiply, Screen, Overlay, SoftLight, HardLight, Difference,
Exclusion, LinearBurn, LinearDodge, Color, Luminosity }`

`static PixelBuffer Blend(PixelBuffer baseImg, PixelBuffer blendImg, BlendMode mode, float opacity)`:

- Implement the standard Photoshop formulas (work per channel on 0..1 values):
  - Multiply `b*f`, Screen `1-(1-b)(1-f)`, Overlay (f on f<0.5 ? 2bf : 1-2(1-b)(1-f)),
    SoftLight/HardLight (standard PS approximations are fine — document which),
    Difference `|b-f|`, Exclusion, LinearBurn `b+f-1`, LinearDodge `min(b+f,1)`.
  - `Color`: blend's hue/saturation over base's luminosity (via HSL or YIQ — pick one,
    document it).
  - `Luminosity`: base's hue/saturation with blend's luminosity.
- Composite: `out = base*(1-opacity) + blendResult*opacity` (per channel; alpha:
  `a = f.a + b.a*(1-f.a)` normal compositing, then apply opacity to the whole result).
- Must be allocation-conscious: one output buffer, loop with local array refs.

### 2.5 Filters (`Nodey.Core/Image/Filters/`)

`enum FilterKind { LensBlur, GaussianBlur, BoxBlur, Sharpen, Emboss, Erosion, Dilation }`

`static PixelBuffer Apply(PixelBuffer src, FilterKind kind, float amount, float radius, float aperture)`:

- `GaussianBlur` — separable 1D gaussian kernel (radius → kernel size, sigma =
  radius/2), horizontal then vertical, clamp-at-border.
- `BoxBlur` — sliding-window average (separable too, or single pass 2D; document).
- `LensBlur` — v1 approximation: gaussian with radius scaled by `aperture` + a slight
  center-weighted falloff (document that a true bokeh kernel is a v2 item).
- `Sharpen` — unsharp mask: `src + amount*(src - blurred(src, radius))`.
- `Emboss` — directional (1,-1) derivative + 0.5 offset, grayscale-ish result.
- `Erosion`/`Dilation` — 3×3 min/max on luminance (alpha-aware: ignore fully
  transparent neighbors).
- `FilterParams` record bundling kind + params (this mirrors the Filter node's
  parameters in STEP-3).

### 2.6 Math ops (`Nodey.Core/Image/MathOps.cs`)

`enum MathOperation { Add, Subtract, Multiply, Divide }`

- `static float Apply(MathOperation op, float a, float b)` — divide-by-zero → 0
  (document; tests assert).
- `static PixelBuffer ApplyToBuffers(MathOperation op, PixelBuffer a, PixelBuffer b)`
  — per-pixel op on the R channel (Scalar2D convention from §2.2), output A=1.
  (Alpha handling: v1 operates on R channel only — document.)

### 2.7 Scalar2D / Color2D storage conventions (document in code + tests)

- `Scalar2D` field: value in **R**, G=B=0, A=1.
- `Color2D`: full RGBA.
- Add a `Nodey.Core/Types/SignalType.cs` enum now (`Scalar, Scalar2D, Color2D`) — the
  translation table itself is STEP-3, but the enum and any helpers it needs belong here
  so STEP-3 can build on it.

## Tests (`tests/Nodey.Core.Tests/`)

Every public behavior above. Minimum list (name the test classes after the type under
test):

- `PixelBufferTests` — fill/get/set, clone independence, ToRgba8/FromRgba8 round-trip
  (within 1/255), ScaleTo size + corner/center sanity, bounds exceptions.
- `NoiseTests` — **determinism**: same seed ⇒ identical buffer across two calls;
  different seed ⇒ different output; value range ⊂ [0,1] (sample a grid of points);
  `octaves=1` vs `octaves=4` differ; scale changes output.
- `ColorRampTests` — clamp at both ends, midpoint interpolation exact values,
  non-uniform stop spacing, alpha interpolation, ≥2-stops enforcement.
- `BlendModesTests` — identity cases (opacity=0 ⇒ base unchanged; opacity=1 with
  Normal ⇒ blend), Multiply black ⇒ black, Screen white ⇒ white, Difference equal
  inputs ⇒ 0, all modes produce [0,1] output on random-ish fixed inputs, alpha
  compositing checks.
- `FiltersTests` — Gaussian of a solid = same solid (±epsilon), blur reduces contrast
  of a step edge, box blur of solid unchanged, sharpen amplifies an edge, erosion of a
  white dot shrinks it, dilation grows it, lens blur radius>gaussian radius at same
  aperture (sanity), all filters preserve dimensions.
- `MathOpsTests` — each op on scalars (incl. divide-by-zero = 0), buffer op on
  constant fields gives expected constant.

**Definition of done for this step:** `dotnet build Nodey.sln -c Release` clean;
`dotnet test` all green; zero Avalonia references in `Nodey.Core` (`grep -r "Avalonia"
src/Nodey.Core` returns nothing).

## Verification

```bash
dotnet build Nodey.sln -c Release
dotnet test --filter "FullyQualifiedName~Nodey.Core.Tests"
grep -ri avalonia src/Nodey.Core || echo "OK: core is UI-free"
```

## Pitfalls

- **Determinism is a hard requirement** (seeds must work across runs/OSes) — use
  integer hashing, not `Random` state.
- Keep all methods `static`/pure and allocation-light; STEP-3's evaluation engine will
  call these every frame during a drag.
- Don't reach for SkiaSharp here — plain arrays keep the engine testable and
  UI-free. (SkiaSharp enters the project only in the UI layer for display.)
- Float comparisons in tests: use an epsilon helper (e.g. `1e-4`) — define one in the
  test project and reuse.

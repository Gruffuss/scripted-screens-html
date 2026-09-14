# ScriptedScreens Vector Layer — project brief

Carries everything established so far so the work doesn't restart from zero.

**Layout.** Project root is `StationeersLuaAddonTemplate/`, holding this file and
`vector-format-spec.md`. Three projects below it: `StationeersLuaAddonTemplate-main/` (the
fonts mod, folder name is the unzipped template's, kept as-is), `ScriptedScreensVector/`
(the vector mod), and `ScriptedScreensVector.Tests/`. Not a git repo — no undo.

**Tests.** `cd ScriptedScreensVector.Tests && dotnet run`. Exit code 0 on pass. It compiles
the real `Triangulator.cs` from source against a stub logger (`Stubs.cs`) rather than
re-implementing it — a ported copy drifts from the code it claims to test. Unity's `Vector2`
and `Mathf` are plain managed types and work outside the engine, so no Unity runtime is
needed; BepInEx is avoided by simply not compiling the plugin file.

**Not everything in UnityEngine works headless.** `Vector2` and `Mathf` are ordinary managed
code and run fine, but `ColorUtility.TryParseHtmlString` is a **native ECall** and throws
`SecurityException` outside the player. `GradientTests` parses hex itself for that reason.
Expect the same for any other Unity API that bottoms out in native code.

Coverage: `Triangulator` (5), `Clip` (7), `Gradient` (17). No coverage: `Stroke`,
`PathData`, `Expression`, `Tessellator`.

---

## Goal

Two independent capabilities, added to Stationeers via client-side mods that do not modify
ScriptedScreens on disk:

1. **Fonts** — make the game's TMP font assets usable in ScriptedScreens labels via
   `<font="Name">`. Nearly done; see milestone 1.
2. **Vector rendering** — a resolution-independent, GPU-composited drawing layer to replace
   the pixel-canvas approach, with client-side animation driven by expressions.

## Environment

- Stationeers, Unity **2022.3**, UGUI + TextMeshPro. No runtime UI Toolkit in use.
- Mods load through **StationeersLaunchPad** (BepInEx 5.4). Harmony available.
- Base template: `OrbitalFoundryModTeam/StationeersLuaAddonTemplate`, targets `netstandard2.1`.
- Build config lives in `Stationeers.VS.User.props` (gitignored), one property:
  `SteamLibraryDirectory` pointing at the `steamapps\common` folder.
- ScriptedScreens **0.9.5.0**, installed at
  `steamapps\workshop\content\544550\3666779631\`. Decompiled with ILSpy for reference.
  Not open source — read it to understand the integration surface, don't lift code from it.

## Working agreements

- Ask for source, context, or clarification rather than guessing or fabricating.
- Consult existing project context before asking questions it already answers.
- Prefer the minimal solution. Push back on over-engineering.
- Don't start writing implementation files without the source or template material in hand.

---

## Milestone 1 — font registration (client-side, standalone)

**Status: working.** Confirmed in-game on 2026-08-19 via `FontTest.lua` on a ~480x480
console: 19 fonts registered, no warnings or errors, and **18 of 19 render in their own
typeface** through `<font="X">`. `RBNoBold` renders bold, `RBBook EXTENDED` wide, `code`
as a thin mono — these are genuinely distinct faces, not a shared fallback.

The one failure is `ss_dejavu_fallback`, and its failure mode is diagnostic: it printed the
literal string `<font="ss_dejavu_fallback">` instead of falling back to the default face.
Literal output means TMP's `ValidateHtmlTag` rejected the tag outright rather than resolving
it to a missing asset. See "Known risk" below — but it is not worth fixing (see there).

Registered names: `LiberationSans SDF`, `RBNo3.1-Book SDF`, `LiberationSans SDF - Fallback`
(at mod load); `font_english`, `font_extended`, `font_russian`, `RBBook SDF`, `code`,
`RBNoBold`, `RBBook EXTENDED`, `RBNoBook` (after the `Base` scene loaded); `font_cjk`,
`font_cjk_b`, `font_cjk_j`, `font_hangul`, `font_hangul_b`, `font_misans_cn`, `noto-punc`,
`ss_dejavu_fallback` (later still).

That 3 / 8 / 8 stagger is the load-bearing result: **a single scan at mod load would have
caught 3 of 19.** The rescan window is necessary, and 30s is comfortably long enough. Note
`ss_dejavu_fallback` is ScriptedScreens' own asset and `code` is monospace — both likely
useful in consoles.

`FontRegistry.cs`, `FontLoader.cs`, `FontRegistryLoader.cs`, `ScriptedScreensFontsPlugin.cs`,
`PluginInfo.cs`. `ScreenHierarchyProbe.cs` deleted (the decompiled source answered what it
would measure). The mod now does two separate things: registers the game's own font assets
(this section) and builds assets from font files on disk (see below).

**Mechanism.** TMP resolves `<font="X">` by hashing the name into
`MaterialReferenceManager`, then falling back to
`Resources.Load<TMP_FontAsset>("Fonts & Materials/" + name)`. Stationeers fonts come from
asset bundles and are never in a Resources path, so the fallback always misses and the tag
errors. Registering the assets directly fixes it:

```csharp
foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
    MaterialReferenceManager.AddFontAsset(font);   // no-ops on duplicates
```

This is global TMP state, so it works inside ScriptedScreens labels with **no patching**.

**Validated by prior testing:** exactly one font worked (TMP's own bundled asset, which
*is* in a Resources path) while all others threw TMP errors — exactly what this mechanism
predicts.

**Two findings from live testing (2026-08-19), both fixed in the mod:**

*`_CullMode` error spam.* Rendering `RBBook SDF` in a ScriptedScreens label makes Unity log
`material 'rbbook sdf material' ... doesn't have a float or range property '_CullMode'`
every canvas update. `TextMeshProUGUI` pushes `_CullMode` onto the font's material as part
of its UI culling path; that material's shader doesn't declare it. World-space `TextMeshPro`
never takes that path, which is why the game itself uses the font without complaint.
Registration is inert — only rendering triggers it. `FontRegistry.WarnIfUiIncompatible` now
flags such fonts at registration instead of leaving the error unexplained. Do **not** patch
the shared material: it's the same asset the game's own text uses. Making such a font
UI-safe needs a cloned `TMP_FontAsset` with a cloned material — not built, likely not worth
it.

*Fonts arrive later than the scan window.* Signage faces (e.g. the one on door labels) load
with the prefabs that use them, well after the 30s burst closes, so they never got
registered. `FontRegistryLoader` now follows the burst with a 20s heartbeat.

**Why a full up-front registration is impossible:** `FindObjectsOfTypeAll` returns objects
already in memory. A font inside an unloaded bundle is not an object yet, so there is
nothing to pass to `AddFontAsset`. The 3 / 8 / 8 stagger is not a scan missing things — the
later 16 did not exist during the first scan. Registering everything eagerly would mean
force-loading every bundle in the game at startup.

**Planned (decided 2026-08-19, not yet built):** harvest the complete font list over a few
sessions, hardcode it as an *expected set*, and use it as a **stop condition** — the
heartbeat runs until every expected name is registered, then stops for good. Zero
steady-state cost and no Harmony patch, keeping milestone 1 patch-free. Needs a time cap as
well as the set test, since fonts that never load in a given session (CJK under some
language settings) would otherwise keep the heartbeat alive forever.

Note the hardcoded list *cannot* replace scanning — a name is not an asset, and registration
still needs the loaded object. It only tells the loader when it is finished.

Rejected for now: rescanning on TMP lookup miss (a Harmony postfix on
`MaterialReferenceManager.TryGetFontAsset`). Strictly cheaper, but it would make milestone 1
patch TMP, and the stop-condition approach gets the same steady-state cost without that.

**Notes.** Bundled fonts don't exist at mod load, so `FontRegistryLoader` rescans in a
bounded window after each scene load. Skip entirely when `Application.isBatchMode`. No
StationeersLua dependency — the Lua/MCP example files, the `StationeersLua` reference, the
`EnsureStationeersLuaDll` target, the `LuaCSharp` package reference, and the
`OrderAfter`/`Dependencies` block in `About.xml` have all been stripped. Project renamed to
`ScriptedScreensFonts` (csproj, sln, `ModName`) — `ModName` drives `RootNamespace`, and
`GenerateVersionConst` emits `PLUGIN_VERSION_CONST` into `$(RootNamespace)`, so a mismatch
between it and the namespace in `PluginInfo.cs` breaks the build.

**Known risk — now measured, and smaller than feared.** The predicted tag-hash failure hit
exactly one font of 19: `ss_dejavu_fallback`.

Cause, now well supported: `scriptedscreens.assets.manifest` lists the asset as
`Assets/Resources/fonts/SS_DejaVu_Fallback.asset`, but the mod logs its runtime `.name` as
`ss_dejavu_fallback` — something lowercases it after import. `AddFontAsset(font)` registers
under the asset's stored `hashCode` (derived from the *original* name), while `<font="X">`
hashes the tag string. Original-cased hash vs lowercase tag hash = miss, and a miss in
`ValidateHtmlTag` prints the tag literally. That matches the observed symptom exactly and is
the case-sensitivity failure the brief predicted.

**That diagnosis was tested on 2026-09-01 and is wrong.** `FontTest.lua` renders both
spellings; **neither** resolves — both print their tag literally. If the asset were
registered under the original-cased hash, `SS_DejaVu_Fallback` would have rendered. It is
registered under a hash matching neither spelling, so whatever `hashCode` the asset carries
is not derived from either name as it appears. Cause unknown and deliberately not chased
further; see the decision below, which is unaffected.

Worth keeping as a method note: this cost nothing to falsify because the prediction was
written down with the render that would refute it. Two other predictions in this session
(`CreateFontAsset` composing with OS fonts, a start-order rule for the MCP) were stated
without that and both survived longer than they deserved.

Also worth knowing: this asset sits in a **Resources** path inside the bundle, so
`Resources.Load<TMP_FontAsset>("fonts/SS_DejaVu_Fallback")` would find it by name. TMP's own
fallback misses it only because TMP hardcodes the prefix `"Fonts & Materials/"`. It is the
one font in the set that name-based loading could reach.

**Decision: not fixing it.** The reflection alias into `m_FontAssetReferenceLookup` would
recover one font, and that font is ScriptedScreens' internal fallback — nobody has a reason
to name it in a tag. 18/19 including every brand and mono face is the useful set. Revisit
only if a font someone actually wants shows the same literal-tag symptom.

### Loading fonts the game does not ship — working, confirmed in game 2026-09-01

`FontLoader.cs` builds TMP font assets from `.ttf`/`.otf` files under the mod's
`Assets/fonts` folder (scanned **recursively** — organising by project is the obvious thing
to do, and a skipped subfolder looks exactly like a font that failed to load). Confirmed
with all 36 Barlow and Barlow Condensed faces: every weight from Thin to Black plus italics
renders as its own face on a 460x460 console, 191 glyphs each.

Names come from the font's own metadata: family, plus style when that is not Regular, so
`Barlow-Bold.ttf` is `<font="Barlow Bold">` and `BarlowCondensed-Regular.ttf` is
`<font="Barlow Condensed">`.

**The obvious approach does not work, and the failure is instructive.**
`Font.CreateDynamicFontFromOSFont` + `TMP_FontAsset.CreateFontAsset(font)` looks like a
fifteen-line solution. Both APIs exist; they do not compose. `CreateFontAsset` calls
`FontEngine.LoadFontFace(Font, pointSize)`, which needs a `Font` carrying **embedded** font
data. An OS font's data lives in the OS, and there is no runtime way to build a `Font` from
a file, so it fails with *"Make sure Include Font Data is enabled in the Font Import
Settings"*. This TMP version also has no `DynamicOS` atlas mode — `AtlasPopulationMode` is
`Static` and `Dynamic` only — so there is no supported path from a file path to a dynamic
font asset. **Verifying that two APIs exist is not verifying that they compose**; this was
shipped and had to be deleted.

**What does work:** `FontEngine.LoadFontFace(byte[])` and `LoadFontFace(string filePath)`
are public and carry no such restriction. The face is read straight from the file and the
asset assembled by hand, mirroring `TMP_FontAsset.TryAddCharacters`. Consequence: a
**static** atlas. Glyphs are rendered once at load and nothing can be added afterwards,
because "afterwards" would need the `Font` that does not exist. Hence a fixed character set
— printable ASCII plus Latin-1, with an `ExtraCharacters` config string for anything beyond
(box drawing, arrows, CJK). A font must be present before the game starts; there is no
rescan.

Three details that each cost a restart to find:

- **`asset.version = "1.1.0"` is load bearing.** `ReadFontAssetDefinition` treats a
  version-less asset as pre-1.1 and runs `UpgradeFontAsset`, which dereferences legacy
  fields a fresh asset never had. `CreateFontAsset` sets it; copying its setup without that
  line gives an NRE inside TMP.
- **Loading cannot happen at mod load.** `ShaderUtilities.ShaderRef_MobileSDF` resolves
  through `Shader.Find`, which returns null until TMP's own resources are up, and
  `new Material(null)` throws. `FontLoader.TryLoadPending` is driven from
  `FontRegistryLoader`'s existing rescan window and **retries while the shader is null**
  rather than waiting a fixed delay.
- **Log the exception, not `ex.Message`.** The first NRE was diagnosed by decompiling TMP
  because the catch had thrown the stack trace away. `ex.ToString()` named the exact frame
  on the next run.

**Sampling size is 48pt, not TMP's default 90.** The whole character set has to fit one
1024x1024 atlas since a static atlas cannot spill into a second texture; at 90pt it runs out
around 100 characters and the remainder silently does not render. `TryAddGlyphsToTexture`
returning false is logged as *"did not fit"* rather than left to be discovered in game.

**Character set: printable ASCII, Latin-1, and 81 baked extras** — dashes and curly quotes,
maths (`- ~= != <= >= inf sqrt sum prod Delta nabla pi Omega`), arrows single and double,
geometric shapes, check/cross/warning, block bars for text sparklines, and box drawing.
`ExtraCharacters` in the config adds to that. Written as codepoints in `DefaultExtras`, not
literals, so the set does not depend on this source file's encoding.

**A glyph the font lacks is skipped, and that is the whole policy.** `TryGetGlyphIndex`
returns 0 and the character is left out, so an absent glyph costs nothing and a broad baked
list cannot make a font render what it does not contain. Measured on Barlow by parsing its
`cmap`: 8/8 punctuation, 13/15 maths, and **0 of 10 arrows, 0 of 12 shapes, 0 of 3 marks,
0 of 33 blocks and box drawing**. It is a text face; it carries typography and maths and
nothing else. 191 glyphs became 212.

**Rejected: a TMP fallback chain** (`fallbackFontAssets`) to source missing glyphs from a
font that has them. It works, and it is about ten lines, but it makes `<font="Barlow">`
silently render some other typeface for any character Barlow lacks. User's call and the
right one: loading a font faithfully is the mod's job, and a font's coverage is the font's
business. An author who wants an arrow can switch face for that character
(`<font="noto-punc">`) or draw it in the vector layer, both of which are visible decisions.
Note the max requested set is 272 codepoints against roughly 289 cells in a 1024x1024 atlas
at 48pt, so a font with full coverage would sit near the edge — it logs *"did not fit"*
rather than failing quietly, and the answer then is a smaller sampling size.

**Cost: one 1024x1024 Alpha8 atlas per face, ~1 MB.** 36 faces is ~36 MB of texture memory.
Fine for a handful; prune to the weights actually used before loading several families.

**Build:** needs `BepInEx.AssemblyPublicizer.MSBuild` on both `Unity.TextMeshPro` and
`UnityEngine.TextCoreFontEngineModule` — `TryAddGlyphsToTexture`, `ResetAtlasTexture`,
`ShaderRef_MobileSDF` and several `TMP_FontAsset` setters are internal. Compile-time only.

**Licensing, checked rather than assumed.** Barlow declares SIL OFL 1.1 in its own `name`
table (nameID 13), read with a ~20-line Python parser of the TTF name table rather than
trusted from memory. OFL permits bundling into software provided the copyright notice and
licence travel with the font and it is not sold separately; Barlow's copyright line declares
no Reserved Font Name, so even a subset is fine. `Assets/fonts/OFL.txt` ships alongside.
**Do this per family** — most Google Fonts are OFL, some are Apache-2.0, and the font file
itself is the authoritative source.

**Rejected: an AssetBundle baked in the Unity Editor.** Correct in every way and needs no
runtime hacks, but it fixes the font set at build time and drags the Editor into every
future addition. The runtime loader turns the mod from "here are my fonts" into a font
loader, which is the feature worth having. If a Unity upgrade ever breaks the publicised
TextCore internals, bundling remains the fallback with the font files already in the repo.

**Identity note.** `ModID`/`PLUGIN_GUID` is `gruffuss.stationeers.scriptedscreens.fonts`.
Renaming it mid-session split the config in two — LaunchPad's settings UI wrote into the
stale file while the mod read the new one, which presented as "the setting does nothing".
`modconfig.xml` keys **local** mods by path, not ModID, so a rename does not disable the
mod; only the `BepInEx/config/<ModID>.cfg` filename moves. Rename with the game closed and
delete the orphan.

**Build note.** The template sets `AnalysisLevel=latest-all` with
`TreatWarningsAsErrors=true`. On SDK 10.0.400 this cost exactly one error — CA1812 on
`FontRegistryLoader`, which the analyzer can't see being instantiated through
`AddComponent`. Added to `NoWarn` rather than downgrading `AnalysisLevel`; revisit only if
the noise grows.

**Deploy note.** Documents is OneDrive-redirected on this machine, so the template's
hardcoded `C:\Users\$(username)\Documents\My Games\Stationeers` pointed at a folder the game
never reads — and every `Copy` is `ContinueOnError="true"`, so it failed silently. Fixed by
making `StationeersDocumentsDirectory` conditional in `Stationeers.VS.props` and overriding
it in `Stationeers.VS.User.props`. If a build appears to succeed but nothing changes in
game, check the deploy path first.

---

## Milestone 2 — vector layer

### Why

Measured: roughly 10 FPS lost per console. Cause is **op count per frame**, not fill area —
confirmed by the user's own measurement that S=3, S=4 and S=6 all report the same 1,292
canvas calls. Each op is recorded, MessagePack-batched, synced, replayed, then the texture
is uploaded. The fix is making per-frame op count zero, not making fills faster.

Consoles run up to ~760×760, often rendered larger and downsampled.

### Integration surface — re-verified 2026-08-19 against the shipped DLL

Decompiled with `ilspycmd` (installed as a dotnet global tool) using the game's `Managed`
folder plus the ScriptedScreens directory as reference paths — with both, the output is
clean; without them it is unusable IL noise. Command:

```
ilspycmd -r <Managed> -r <ScriptedScreens dir> -t <FullTypeName> ScriptedScreens.dll
```

All four claims below hold. Exact confirmations:

- `ApplyElementInternal` signature matches the brief **character for character**.
- Host creation is literally
  `new GameObject("Ui:" + element.Id, typeof(RectTransform), typeof(CanvasRenderer))`,
  parented via `ResolveParentTransform`, layered, `ApplyRect`-ed, and cached in
  `state.SurfaceElementRoots[surface]` keyed by element id, reused on later upserts.
- The final `else` adds an `Image`, colours it from `style.bg` (default
  `(0.1, 0.1, 0.1, 0.8)`), and sets `raycastTarget = false`.
- Stale-component cleanup is a **single** `DestroyImmediate`, and only for `CircleGraphic`
  when the type is not `circle`. Nothing else is destroyed, so our component is safe.
- `ParseProps` iterates every string key with no whitelist; `ParseValue` handles
  nil/number/bool/string and recurses through `ParseTableValue` for tables.

**Correction to the brief:** it says `UiElement` and `BoardState` are internal. True, but
incomplete — `ScriptedScreensScriptableUiSystem` *itself* is `internal static class`, and
`UiProp`, `UiValue`, `UiValueType` are internal nested types too. The publicised reference is
required, not optional. Note `IgnoresAccessChecksTo` will **not** work here: Roslyn does not
honour it at compile time, so use `BepInEx.AssemblyPublicizer.MSBuild`.

**The 27 existing element types** (so a new one does not collide): label, button,
interface_button, panel, progress, spinner, image, checkbox, radio, slider, toggle, select,
textinput, line, rect_outline/border, circle, icon, divider, scrollview, sparkline, table,
barchart, gauge, linechart, canvas, media, sound. **`vector` is free.**

### Integration surface — original notes

**Props are an open map.** `ScriptedScreenscriptableUiLibrary.ParseProps` iterates every
string key in the Lua `props` table with no whitelist. `ParseValue` recurses into nested
tables, producing `UiValue` with `Array` and `Map` variants, MessagePack-serialised.

Consequence: **the scene tree ships as native structured data.** No string encoding, no
sentinel, no wire format, no parser. Expressions remain short strings inside the structure —
the only text parsing needed.

**Unknown element types already produce a usable host.** In
`ScriptedScreensScriptableUiSystem.ApplyElementInternal`, an unrecognised type still gets a
`GameObject` named `Ui:<id>` with `RectTransform` + `CanvasRenderer`, correctly parented to
the surface root, layered, and rect-applied. It then falls to a final `else` adding an
`Image` tinted from `style.bg` — set that transparent and it's invisible.

Host objects are cached by element id in `state.SurfaceElementRoots[surface]`, so they
persist across upserts. Their stale-component cleanup only destroys their own graphic types
(`CircleGraphic` etc.) and will leave ours alone.

**One Harmony postfix** is the entire integration:

```csharp
private static void ApplyElementInternal(
    Motherboard? board, CartridgeIntegratedCircuitLua? cartridge,
    ProgrammableVisorGlasses? visor, BoardState state,
    string surface, UiElement element)
```

Reads `element.Type`; when it matches ours, hands `element.Props` to the renderer component
on that element's GameObject.

**Build wrinkle:** `UiElement` and `BoardState` are `internal`, and Harmony patch parameter
types must match exactly. Use a publicised reference (`BepInEx.AssemblyPublicizer`) or an
`IgnoresAccessChecksTo` attribute. Compile-time only; the shipped DLL is untouched.

**Precedent in their code.** `ScriptedScreens.ScriptableUi.Rendering` already contains
`CircleGraphic`, `GaugeGraphic`, `SparklineGraphic`, `SpinnerGraphic`, `UIGradient` — all
UGUI mesh `Graphic` subclasses. Vector rendering is the same pattern generalised, not a
foreign concept. Note their `time` prop injection is media-playback specific, not a general
animation loop.

### Design

New element type alongside `canvas`, **not** a replacement. Existing scripts do not benefit
automatically; porting is mostly deletion (see below).

**Renderer.** A `MaskableGraphic` on a child of the element's `RectTransform`, stretched to
fill. Child rather than same object because UGUI allows only one `Graphic` per GameObject
and the fallback `Image` occupies it.

**Model — SVG semantics, not the file format.** Scene graph of nodes with transforms;
paths of move/line/quad/cubic/arc/close; rects with corner radii; ellipses; polylines and
polygons. Fill and stroke, solid or gradient, real alpha. Caps, joins, dashes. Group
opacity and clipping. Coordinates top-left origin, +Y down, viewbox mapped onto the element
rect.

**Clipping:** convex clip paths only in v1 (rect, rounded rect, ellipse, convex polygon) —
clipped geometrically during tessellation, no stencil buffer. Covers every realistic layout.

**Animation — general, not enumerated.** No wave primitive, no particle primitive. Instead:

- Any numeric field may be an expression over `t` (client clock) and `i` (repeat index).
- A `repeat` node instantiates a subtree N times with `i` bound.
- Functions: sin cos tan atan2 abs sign sqrt floor ceil round min max clamp lerp mod saw
  tri pulse step smoothstep if eq lt gt lte gte and or not **hash hash2** pi tau.
- `hash(i)` gives deterministic per-instance pseudo-randoms — stable across frames and
  clients, nothing transmitted. This is what makes particle fields work without a primitive.

Nodes with no `t` reference evaluate once and cache. Topology rebuilds only on structural
change; time-varying nodes update vertex positions only.

**Payload split.** Structure prop changes only when layout changes. Data prop (named
values referenced as `$name`, scalars and arrays) rewritten per tick. Their existing element
diffing handles the rest.

**Not supported:** blur, drop shadow, per-pixel glow — all need an offscreen pass or custom
shader. Geometric feathering (`fea`) fakes them adequately for UI.

**Text:** out of scope for v1. Stays as ScriptedScreens' own label elements layered over the
artwork, using milestone 1's fonts. In-scene text means managing child TMP objects from the
renderer — deferred.

### What porting deletes

From the user's 1,178-line `GasUI.lua`, roughly 950 lines are view code and most of it is
workaround: the supersample machinery and `set_resolution` traps; `COL_STEP`/`BARS` tuning
against the instruction budget; `gas_ramp()`/`liq_pal()` pre-composited opaque palettes
(canvas alpha overwrites instead of blending); the `f*` Y-flip helpers (canvas Y is
bottom-left, element rects top-left); `notch_corners()` faking rounded corners in 16 calls
per gauge; `chrome_step`/`build_step`/`refresh_step` spreading work across frames under the
50,000-instruction-per-tick budget; `canvas_begin_update`/`end_update`/`apply`; the entire
`on_frame` loop; `STEPS` gradient banding and `MARKROWS` band-index approximation.

What survives is the bottom ~170 lines: device wiring, `ic.find`, thresholds, sampling in
`tick(dt)`.

A pure-Lua canvas-compatible shim over the vector layer is possible and would give old
scripts alpha and unlimited resolution — but not the framerate, because the cost is Lua
issuing ops per frame at all. Migration aid at best; don't design around it.

---

## Open question 2 — ANSWERED, and the answer is bad (2026-08-19)

The brief assumed ~1,700 animated quads would be trivial on the CPU. **It is not.** Measured
with `StressTest.lua` on one console:

| quads | ms / rebuild | vertices | frame budget |
|-------|--------------|----------|--------------|
| 1,700 | 7.8 ms       | 23,814   | ~31%         |
| 5,000 | 21.4 ms      | 59,999 (capped) | ~50%  |

User-observed: 1,700 costs 5–6 FPS, 5,000 costs ~20 FPS.

**Why this is far worse than it sounds, and why the game renders a whole world for less.**
World geometry is uploaded to the GPU once and redrawn from vertex buffers; per frame the CPU
only issues draw calls, and the GPU chews millions of triangles in parallel. Global
illumination is a GPU pass, so it barely touches the main thread. This layer instead
**regenerates its entire mesh in C#, single-threaded, on the main thread, every frame**, and
re-uploads it. 23,814 vertices is nothing for a GPU — less than one character model — but
building them in managed code costs 7.8 ms of the 16.7 ms frame, serialised with everything
else the game does.

So the design achieved what it set out to (zero per-frame Lua, zero per-frame network) and
then **replaced that cost with per-frame CPU tessellation**. For static scenes the cost is
genuinely zero. For a few hundred animated shapes it is fine. A 1,700-mote field is the
pathological case.

**First optimisation pass (2026-08-19), not yet re-measured in game:**

- *Convex fast path.* A rect was running full ear clipping — allocating four `List`s and an
  O(n²) search to rediscover that a quad is two triangles. Convex outlines with no holes, no
  clip and no gradient refinement now fan directly.
- *Shared vertices.* The per-triangle emitter duplicated every vertex three times: 6 verts
  for a quad instead of 4. `FanShared` adds each once and indexes it.
- *No per-shape allocation.* Outline building reuses a static scratch buffer. The old path
  allocated roughly 270,000 lists/second for a mote field, which is pure GC pressure.
- *Feather size gate.* Feathering doubles a shape's geometry, and on a 1x1 mote it was two
  thirds of the vertices. Below ~2x the feather width the ramp is wider than the shape, so it
  is dropped — it stops reading as a soft edge anyway.

Measured before: **14 vertices per quad**. Expected after: 4 for small motes, 12 for shapes
large enough to feather.

**CPU optimisation cannot close this gap — measured, not assumed.** `Benchmark.cs`
(`dotnet run -c Release -- --bench`) breaks the ~4,600 ns per animated quad down:

| component | cost | share |
|-----------|------|-------|
| 5 expression attributes | 513 ns | 11% |
| ear-clipping a 4-point quad | 276 ns | 6% |
| one `List<Vector2>(64)` alloc | 64 ns | 1.4% |
| a *constant* attribute | 2.3 ns | — |

Hoisting `i`-invariant subtrees (the three `hash(i…)` calls are constant per instance) was
measured by simulating the collapsed expression: it removes **60% of expression cost but only
6.7% of the total**. Combined with the fast path already built, all CPU work available to
optimise is **~14%** — 7.8 ms becomes ~6.7 ms. **Direction 1 was measured and rejected.**

Two caveats that matter for reading any of these numbers:

- The benchmark runs on **.NET 8**; the game runs **Mono**. **Neither the absolute figures
  nor the ratios transfer** — an earlier version of this file claimed the ratios did, and
  acting on that produced a wrong prediction (below). Mono penalises a recursive
  tree-walking interpreter far more than it penalises list appends, so the *shape* of the
  profile differs, not just its scale.
- `VertexHelper.AddVert` appends to several parallel lists (position, colour, 4 UV channels,
  normal, tangent), so 14 verts is ~84 list appends per shape. Much of the unexplained
  remainder lives there, and it *is* proportional to vertex count.

**Consoles multiply this.** 4 consoles x 1,700 motes = 6,800 quads ≈ 31 ms/frame against a
16.7 ms budget. The gap is ~10x, not ~1.5x, so the fix has to be structural.

**The fast-path pass is measured: 7.8 ms -> 4.66 ms, 23,814 -> 10,740 verts, 31% -> 22% of a
frame (one console at full size).** A 40% cut, better than the ~28% predicted. Note the
vertex arithmetic confirms **all 1,700 instances still drew**: motes are 1 or 2 scene units,
`step(0.7, hash)` makes ~30% the larger size, and only those clear the feather threshold, so
70% x 4 + 30% x 12 = 6.4 verts average x 1,700 = ~10,700. The gain is entirely shared
vertices, the feather gate and the convex fast path.

**Screen size was being measured wrong — fixed 2026-08-19.** `Tessellator`'s matrix is built
from the element's rect in **canvas** units and stops there; the canvas-to-screen projection
happens later, in the Canvas. So `matrix.lossyScale` is a constant, every "screen pixel"
derived from it was really a canvas pixel, and **distance was invisible to the renderer**.

That silently broke three things that claim to adapt to on-screen size: LOD (which therefore
did nothing at all), the auto-feather, and the path-flattening cache. They looked correct
only because canvas units approximate screen pixels at close range.

`VectorGraphic.ScreenPixelsPerCanvasUnit` projects two corners of the rect through the
canvas camera and passes the real ratio into `Tessellator`.

**Getting the camera right is the whole difficulty, and the first attempt shipped broken.** A
world-space Canvas normally has a **null `worldCamera`** unless an event camera was assigned,
and the fallback treated world coordinates as screen coordinates. A two-metre console then
measured about two pixels wide, which starved the rebuild rate to its 8 Hz floor and shed
almost every instance — reported in game as "50 motes at 5 Hz with my face against the
screen". Now: `worldCamera`, else a cached `Camera.main` (it is a tag search, and this runs
per frame), with `ScreenSpaceOverlay` handled separately since it is already in screen space.

**The load-bearing lesson is the direction of failure.** When on-screen size cannot be
determined the answer is now -1 and every consumer treats it as *full quality*. Failing
toward full detail costs frames; failing toward minimum detail makes the mod look broken, and
that asymmetry should decide the default in anything adaptive. The stats line prints
`screen size UNKNOWN (full quality)` rather than a fabricated number.

**LOD is temporal by default; count reduction is opt-in (revised 2026-08-19 on user
feedback).** Shedding instances was built first and rejected on sight: motes pop in and out,
and apparent density falls with the count, so a field visibly dims as the player walks away.
Neither is acceptable for decoration whose whole job is to look continuous.

`VectorGraphic.DueForRebuild` instead rate-limits by on-screen width — capped at 30 Hz for
everything, scaling down to a floor of 8 Hz as an element shrinks.

**The 30 Hz cap is the half that helps a near console**, which distance-based LOD by
definition never touches: it halves the 22%-of-a-frame worst case. UI motion does not need
more — film runs at 24, and everything animated here is drift, ripple or a sweeping needle.

Note what is and is not decoupled: **rendering is per-frame regardless**, since the mesh
lives on a `CanvasRenderer` and the GPU draws it every frame. Only mesh *regeneration* is
rate-limited, and that is where the whole CPU cost sits. The schedule advances by whole
periods rather than snapping to `now`, so a late rebuild does not drag the schedule and beat
against the frame rate. Every shape stays present and correctly placed and
is merely resampled less often, which at small sizes is invisible. It saves the same work:
seven distant consoles at 10 Hz cost a sixth of seven at 60 Hz.

Fast motion is unaffected in practice, since full rate returns well before an element is
large enough to read detail in.

**Both curves were too steep on first contact (revised 2026-08-19).** Reported as "drops too
much and too abruptly ... the screen seems almost empty".

*Count LOD scaled by area* — half the on-screen size gave a quarter of the instances. The
justification was "constant density per screen pixel", which is theoretically tidy and
perceptually wrong: a console is read as an **object**, so what registers is its internal
density, and quartering the field empties it. Now **linear** (half size, half the instances)
with a 0.45 floor, bucketed to sixteenths rather than eighths — coarse buckets are what read
as popping.

*Rate LOD fell linearly* and hit its floor by ~107 px, which is choppy rather than invisible.
Now **sqrt**, so rate is held up near the threshold and eases into a floor raised from 8 Hz
to 15 Hz.

**Config must bind into `ModBehaviour.Config`, not a `ConfigFile` of its own.** LaunchPad's
`StationeersModsEntrypoint.Configs()` returns exactly that instance to its settings UI, and
`base.OnLoaded(contentHandler)` already creates it. Constructing a private `ConfigFile` writes
a perfectly valid .cfg that **LaunchPad never displays** — which is what happened first time
and showed up as "LaunchPad says you did not add any mod configuration".

For reference if a non-`ModBehaviour` entrypoint is ever used: `DefaultEntrypoint` instead
injects a `ConfigFile` when the `OnLoaded` method *declares one as a parameter*, pattern
matching over `List<GameObject>`, `ConfigFile`, `List<Assembly>`, `ModData`. `ContentHandler`
is not in that set, which is why the two paths are separate.

**Config: `BepInEx/config/<ModID>.cfg`** (`VectorConfig.cs`). Both mechanisms
can be disabled outright, and every threshold, floor and curve endpoint is editable. Added
early rather than at the end because the right curve is a judgement call that can only be
made by walking around a real console, and rebuilding the mod for each guess made the
feedback loop the bottleneck. An in-game toggle UI is still wanted; the file is the interim.

**`Patterns.lua` shipped broken and was caught immediately** — it drew nothing (the demo call
was commented out and the file ended in `return P`, implying a module system chips do not
have), and the dial's tick marks all stacked at twelve o'clock because the rotation was
described in a comment but never written. Both fixed; the file now runs on paste.

Worth stating in the docs because it is easy to get wrong even having written the renderer:
**a repeat instantiates its children unchanged.** Nothing varies by itself; an expression over
`i` has to do it, and for rotation that means a `G` *inside* the repeat.

**Data interpolation added 2026-08-20**, prompted by "animations run at very low Hz" in
`Patterns.lua`. The cause was two clocks in one scene: `t` advances per frame while `$data`
changes about twice a second, so `t`-driven motes glided beside a `$pressure`-driven needle
stepping in quarter-second jumps. Inherent to the design, but it would have made every gauge
in the GasUI port look broken.

`EvalContext` now keeps the previous payload and a `Blend` factor; `Scalar()` returns
`lerp(previous, current, blend)`. The window self-tunes to the measured gap between payloads
(clamped 0.05-1.5s), so it adapts to any tick rate. `VectorGraphic` also had to keep
rebuilding while a blend is in flight — a scene with no `t` reference is otherwise idle and
would snap anyway.

Trade-off worth knowing: **a value that should change instantly no longer does.** A mode flip
eases over about one tick. `Renderer.SmoothData = false` restores snapping.

**Arrays were left unsmoothed on the reasoning that stepped history "reads as correct for a
chart". Wrong — it looked broken next to smooth motion, and was reported immediately.** They
are smoothed now, and the case is actually stronger than for scalars: a history chart shifts
each sample one slot per tick, so blending `old[i]` toward `new[i]` (holding what used to be
at `i+1`) produces a real scroll rather than a cross-fade. Previous arrays are handed over by
reference rather than copied, since `ReadData` allocates fresh ones per payload.

This only works if the chart is fed a **rolling window**. `Patterns.lua` originally
regenerated the whole curve each tick, which animates the array instead of scrolling it —
fixed, and the README now shows the shift-and-append idiom.

**Documentation written 2026-08-20:** `ScriptedScreensVector/README.md` (authoring guide)
and `Patterns.lua` (tank, mote field, bar, dial, chart, alarm lamp, plus a worked console).
The guide leads with the two-element structure/data split and the `RP` versus `YS`
distinction, and has a Traps section for the things that cost time in this session: gradient
placement, fade-to-black, LOD opt-in, convex-only clipping.

**Telemetry is off by default** behind `Diagnostics.Enabled`. The instrumentation stays
compiled in — it earned its place several times over — but a line every five seconds is noise
during ordinary play.

**Original documentation debt (raised 2026-08-19):** `lod = 1` being a Lua-side opt-in is accepted,
but it is exactly the kind of thing that gets missed. The eventual docs and examples must
make the split explicit — **rate LOD is automatic and renderer-side, count LOD is per-node
and must be asked for** — or authors will assume both are free and wonder why a mote field
never thins.

**Count-based LOD, opt-in via `lod = 1`.** `n` becomes the
full-size count rather than a literal. Instances scale with **area** so density per screen
pixel is constant; bucketed to eighths so camera drift does not rebuild topology every frame.
`i`/`n` in expressions still refer to the authored count, so a thinned field loses members
rather than redistributing.

**Corrected 2026-08-20 while writing the docs: there is no automatic application.**
`SceneModel` sets `AllowLod` only when an explicit `lod` value above 0.5 is present
(`node.AllowLod = lod?.Type == Number && lod.Value.Number > 0.5f`), and `Tessellator` bails
out on `!node.AllowLod`. The "repeats of >= 50 opt in automatically" behaviour described here
was never implemented. Pure opt-in is arguably the better default anyway — silently thinning
someone's tick marks is the failure this whole mechanism exists to avoid — so the docs
describe the code rather than the code being changed to match this file.

Why this and not more per-quad tuning: the real arithmetic is 16 tanks x ~100 motes x 7-8
visible consoles ~= 12,000 animated quads. Only the console being looked at is near full
size. LOD attacks the multiplication; micro-optimisation attacks the constant, and the
constant was never the problem.

**Measured cost centres, for reference when tuning further** (`--bench`, .NET 8):

| per quad | ns | share of ~4,600 |
|----------|-----|-----------------|
| VertexHelper, 14 verts | ~1,299 | 28% |
| 5 expression attributes | 531 | 12% |
| ear clipping a quad | 276 | 6% |
| List allocation | 64 | 1.4% |

`VertexHelper` is the largest single cost — it appends to seven parallel lists per vertex, so
14 verts is ~98 appends. The 14 -> 4 vertex reduction already shipped is therefore worth
~928 ns (20%), three times what expression hoisting would have given.

**A side effect of the screen-scale fix, worth knowing.** Once `ScreenScale` became correct
the auto-feather got ~3x narrower (it had been computed against a stuck value of 1), so far
more small shapes clear the "worth feathering" gate. Mote vertex count went from ~6.4 to 12
each — `20,412 = 1700 x 12 + 12` exactly. More accurate and more expensive. Raising the gate
from 2x to 4x the feather width would roughly halve mote-field geometry at the cost of hard
edges on sub-3px shapes; deliberately not done, since it only helps mote-sized things and
real UI shapes are larger.

**If that is not enough, the remaining levers, in order:**

1. ~~**Persistent mesh, positions only.**~~ **First half done 2026-08-19: `VertexHelper` is
   gone.** In-game cost worked out at 5.28 ms / 20,412 verts = **259 ns per vertex**, against
   ~93 ns/vert measured for `VertexHelper` on .NET 8 — and Mono runs 2-4x slower, so
   `VertexHelper` *was* the rebuild, with everything else reduced to noise beside it. It
   writes **seven** parallel lists per vertex (position, colour, uv0-uv3, normal, tangent)
   and the UI shader reads three.

   `MeshBuilder` accumulates position/colour/uv0 plus indices in persistent lists and uploads
   them with bulk `Mesh.SetVertices` calls. `VectorGraphic` overrides `UpdateGeometry` and
   calls `canvasRenderer.SetMesh` directly, which is where the base implementation ends up
   anyway, so masking, batching and materials are unaffected. `OnPopulateMesh` is gone.

   **Measured result: 5.28 -> 4.94 ms, about 6%.** The predicted 2.5-4x did not appear, so
   `VertexHelper` was *not* the dominant cost and the inference that produced that prediction
   (259 ns/vert in game vs 93 ns/vert on .NET 8, scaled by a Mono penalty) was unsound. The
   change is still worth keeping — it is strictly less work and removed a dependency on
   UGUI's mesh path — but it did not move the needle.

   **Then measured properly, and it was neither.** With per-surface stats, one scene, one
   spot:

   | verts  | ms/rebuild |
   |--------|------------|
   | 10,740 | 4.35–4.39  |
   | 20,412 | 4.48–4.55  |

   Both 1,701 shapes. **Double the vertices costs ~3% more time**, so geometry emission is
   near-free. And the 20,412 runs were `SIMPLE = true` — trivial expressions — so **deep
   `hash`/`sin` expressions cost no more than shallow ones either. Cost was ~2.6 us per
   shape, flat.

   **The culprit was `Matrix4x4.lossyScale`.** It is a **native ECall** (`GetLossyScale()`) —
   it throws `SecurityException` headless, like `ColorUtility` — and it decomposes the matrix
   rather than reading a field. The tessellator called it two or three times per shape, so a
   1,700-mote field made ~4,000 managed-to-native transitions per rebuild. That matches the
   profile exactly: per-shape, constant, indifferent to vertex count and expression depth.

   Fixed by carrying `Scale` in the `Frame` struct. It was never needed: the root scale falls
   out of the viewbox fit, and a group's is its parent's times its own `s` attribute. Zero
   `lossyScale` calls remain in the tessellator.

   **Removing it changed nothing: still 4.38-4.45 ms.** Third theory falsified. Keep the
   change — it removed ~4,000 native transitions per rebuild and cost nothing — but it was
   not the bottleneck either.

   **Four theories tested against a single combined number, which was the real mistake.** The
   stopwatch wrapped `Tessellator.Emit` *and* `MeshBuilder.Apply` *and*
   `canvasRenderer.SetMesh` together. The last two are native calls pushing the entire vertex
   buffer at the engine, and every hypothesis was being scored against a figure that included
   them. `VectorGraphic` now times **tessellate** and **upload** separately and reports both:

   ```
   vector "stress": 29 Hz, 4.39 ms/rebuild (tessellate 0.85 + upload 3.54), ...
   ```

   If upload dominates, no amount of tuning inside the tessellator can help and the levers are
   vertex count, rebuild rate, or splitting the mesh. Split the measurement before theorising
   about which half is slow.

   **Split measured: `tessellate 4.28 + upload 0.13`.** Upload is **3%**; the cost is in
   managed tessellation. Fourth theory falsified.

   **Frame-time ground truth agrees with the stopwatch.** ~19-21 ms mean (47-53 FPS) with the
   renderer on; powering the console off moves it 1-2 ms. Predicted 0.6 rebuilds/frame x
   4.41 ms = 2.6 ms, so the numbers are consistent — "13% of a core" simply sounds worse than
   2 ms of a 20 ms frame. Note the baseline here is ~50 FPS, not 60.

   **Five falsified theories: vertex count (3% for 2x), expression depth (SIMPLE == full),
   `lossyScale`, `VertexHelper` (6%), upload (3%).** What remains is ~2.5 us per shape of
   diffuse managed work with no single peak, which is consistent with Mono costing 2-3x on
   everything rather than one thing being pathological.

   **Ablation switches now live on the scene root** (`nofill`, `nofeather`, `noeval`) so a
   measurement needs no game restart — set them from Lua, read `tessellate`, subtract. Guessing
   has a 0/5 record here; subtraction is the remaining honest tool.

   **Lesson worth keeping: in Unity, treat any property as a possible native call.** The two
   that have bitten this project — `ColorUtility.TryParseHtmlString` and
   `Matrix4x4.lossyScale` — both look like ordinary managed code at the call site. The tell
   is that they throw `SecurityException` in the headless test harness, which makes the test
   project a useful detector for them.

   **Superseded — the earlier expression theory.** 4.94 ms / 1,700 motes =
   2.9 us each; expressions measured 531 ns on .NET 8, and if Mono costs ~3x for a recursive
   tree walk that is ~1.6 us, over half the total. If so, the hoisting optimisation rejected
   earlier at "6.7% overall" was rejected on .NET 8 proportions and would be worth far more
   here.

   **Do not act on that without measuring it.** `StressTest.lua` has a `SIMPLE` toggle that
   draws the same shape and vertex count with trivial expressions instead of deep ones with
   `hash()`/`sin()`. The delta between `SIMPLE = false` and `SIMPLE = true` isolates
   expression cost directly, in Mono, with no cross-runtime inference.

   Still to do regardless: skip rewriting the index buffer when topology is unchanged
   between frames, true of any repeat whose count did not change.

2. **Rebuild below display rate.** 30 Hz halves the cost and UI motion rarely needs 60.
3. **Nested `Canvas` on the vector surface** so dirtying it does not force the parent canvas
   to re-batch. Standard UGUI isolation; costs one draw call.
4. **Push time-varying transforms into a vertex shader** — build the mesh once with hash
   seeds in a UV channel and animate on the GPU. The only option that makes a large particle
   field genuinely free.

   **Correction to an earlier claim in this file:** the GPU route is *not* limited to a fixed
   set of motion forms. Unity cannot compile shaders from source at runtime, but expressions
   can be compiled to the stack program the spec already describes and **interpreted** by one
   precompiled shader. Arbitrary motion, no per-frame CPU. Its real cost is practical:
   authoring a shader and shipping an asset bundle, which this codebase has so far avoided
   entirely.

**Two behavioural bugs worth more than any micro-optimisation (found 2026-08-20 by user
observation, not by profiling). Confirmed working: `idle (off screen, paused, or static)`
throughout the log, and a clean split of ~20.5-21 ms mean while looking at the console
against ~19 ms looking away (46 vs 53 FPS in game).**

**Possible effect on earlier measurements, stated carefully.** Only one mote console existed,
so this is *not* a story about other consoles adding hidden load. What it does mean is that
the stress console kept rebuilding while out of frame, so any reading taken while turning —
or shortly after — included work for something not being looked at. Readings taken standing
still and facing it were unaffected. The per-surface `ms/rebuild` figures were sound
throughout. **Treat the frame-level A/Bs below as provisional**, and re-take any that matter
now that the background is stable.

*Off-screen consoles were still rebuilding.* `Update` runs regardless of visibility — Unity
culls the **draw**, not the mesh rebuild — so every console in the base paid full CPU cost
whether or not it was in frame. In a base with eight consoles and two or three visible, this
is a much larger saving than anything the per-shape work could yield. `IsOnScreen` projects
the rect's four corners and tests a padded bounding box; unknown counts as visible, since a
console that stays blank reads as a broken mod while an extra rebuild only costs frames.

*Animation ran while the game was paused.* The scene clock used `Time.unscaledTime`,
deliberately, so animation would not depend on game speed. Wrong twice: a console kept
visibly animating while everything around it was frozen, and it kept rebuilding meshes for a
paused game. Now `Time.time` by default, and `DueForRebuild` returns false outright when
`timeScale <= 0`. `Renderer.PauseWithGame = false` restores the old behaviour.

Both are config-gated under `Renderer`. A surface that stops rebuilding now logs
`idle (off screen, paused, or static)` rather than falling silent, so "no line" never has to
be interpreted.

## The GasUI cost, found by instrumentation rather than by guessing (2026-08-20)

**Seven theories were proposed and six were measured false** — vertex count, expression
depth, `lossyScale`, `VertexHelper`, mesh upload, and band ear-clipping. The seventh was not
proposed at all: per-op timing named it in one run.

`Tessellator.OpMilliseconds[]` / `OpCounts[]` charge each leaf op with a `Stopwatch`
timestamp pair, and `VectorGraphic.Stats` prints the top types sorted by cost. Groups are
charged **before** descending, so a group's own cost is separated from its children's rather
than swallowing them. First run on the real console:

```
vector "gas": 28 Hz, 19.72 ms/rebuild (tessellate 19.60 + upload 0.12), 54.8% of a core,
              17623 verts, 800 shapes, 911 px
    by op: YS 13.41ms x184, R 6.03ms x616, G 0.05ms x16
```

Two assumptions died there. There are **184 bands**, not the ~16 one-per-tank I had assumed
— so a `YS` cost that looked negligible in aggregate was the largest single item. And `G` is
**0.05 ms across 16**, which retires the `Quaternion.Euler` native-ECall theory outright
before any work went into it. That is the whole value of the instrumentation: it falsified a
plausible hypothesis for free.

**Cause: `ClipRegion.ClipPolygon` allocated one `List<Vector2>` per boundary edge.**

```csharp
for (var edge = 0; edge < _boundary.Count; edge++)
    output = new List<Vector2>(input.Count + 4);   // per edge, per call
```

Sutherland–Hodgman is a pass per boundary half-plane, so a rounded-rect clip (~40 boundary
points after its corner arcs) allocated **40 lists to clip one quad**. The band strip rewrite
made this worse rather than better: it moved from one `ClipPolygon` call per band to one per
quad, roughly 39x more calls. 184 bands x ~39 quads x 40 edges is ~290,000 allocations per
rebuild, with the 616 clipped rects adding ~25,000 more.

That also explains the shape of every earlier failed measurement. The cost was **per clipped
shape**, indifferent to vertex count and to expression depth, and invisible to the stress
test — `StressTest.lua` has no clip at all, which is exactly why its motes cost 2.6 us while
GasUI's shapes cost 21.5 us. **A benchmark that omits a feature cannot price it.**

**Two fixes:**

1. *Ping-pong buffers.* `ClipPolygon(subject, output)` alternates between a caller-owned list
   and one static scratch, allocating nothing. The allocating single-argument overload
   remains for the few callers that keep the result.
2. *Bounds fast paths.* `ClipRegion` precomputes its bounding box and an **inner** box proven
   interior to the region — the largest box centred on the centroid, with the bounding box's
   aspect, satisfying every half-plane (`ex*|nx| + ey*|ny| <= dist(centroid, edge)`, minimised
   over edges). A subject inside the inner box is returned **untouched**; one outside the
   outer box returns empty. For an axis-aligned rectangular clip the two boxes coincide, so
   clipping such a region costs a bounding-box test and nothing else.

The fast path is the dangerous half: too large an inner box means clipping **silently stops
happening** and geometry escapes its window with no warning — the same fail-toward-broken
direction as the LOD bug. `ClipTests.DiagonalBoundaryStillClips` pins it with a diamond,
whose bounding box is twice its area, so half that box must not qualify. Plus
`BufferedMatchesAllocating`, which reuses one buffer across straddling/empty/straddling calls
so a dirty buffer shows up as a wrong third result.

Worth noting the first version of the diamond test asserted the wrong number — a 20x20 square
at (30,30) is entirely inside that diamond, since every corner satisfies `x+y >= 50`. The
code was right and the expectation was wrong. Check the arithmetic before believing a failure.

**Not yet re-measured in game.** Both changes are deployed; the `by op:` line is the check.

## Threaded tessellation — the change of direction (2026-08-20)

**Why the incremental route was abandoned.** Per-op and per-phase instrumentation did work —
it found the clip allocations (19.7 -> 15 ms) and then attributed every remaining
millisecond:

```
by op: YS 10.32ms x184, R 4.40ms x616, G 0.09ms x16
    YS phases: sample 2.77ms, strip 4.37ms, feather 3.13ms, 2120 quads
```

| unit | cost |
|------|------|
| expression eval (6,900 of them) | 0.40 us |
| strip quad (2,120) | 2.06 us |
| feather vertex (2,300) | 1.36 us |
| rect (616) | 7.1 us |

**There is no hotspot left.** The profile is flat: a few microseconds per shape spread over
800 shapes. That is the signature of an architecture doing too much work, not of a bug, and
it is the point at which micro-optimisation should stop — a perfect 2x on every line still
leaves ~3.7 ms per frame at 30 Hz. Seven theories were tested to reach that conclusion; the
useful output of the last three was not a speed-up but the evidence that no single fix exists.

Two fixes did land from the phase data and are worth keeping: `ClampInside` gained the same
inner-box fast path as `ClipPolygon` (it was walking every clip boundary edge per feather
vertex — 1.36 us each, and it was added the same morning), and `EmitStrip` keeps its
shared-vertex path when the clip provably contains the whole band instead of falling back to
per-quad for any clip at all.

**The direction chosen: move tessellation off the main thread.** Not because it is elegant —
it relocates work rather than reducing it — but because it is the only option whose payoff
does not depend on guessing right about scene composition, and guessing about this scene had
a 0/6 record. Static-subtree caching would not have touched the 184 animated bands that are
two thirds of the cost.

**It is legal, and that was checked before committing.** The only Unity native call on the
rebuild path is the mesh upload. `ColorUtility.TryParseHtmlString` — the one that would have
made this impossible — lives in scene and defs parsing, never in tessellation. Everything
else is `Vector2`, `Matrix4x4`, `Color` and `List<T>`: ordinary managed code. And the split
is already measured: **tessellate 15 ms, upload 0.12 ms**, so ~99% of the cost leaves the
frame.

**What made it possible was removing `lossyScale` earlier.** That change was recorded here as
"measured, changed nothing, keep it anyway" — a native ECall in the middle of the tree walk
would have made the tessellator un-threadable. A cleanup that paid off for a reason nobody
had in mind at the time.

**Shape of the implementation:**

- Every mutable static in `Tessellator` and `Clip` is now `[ThreadStatic]` — scratch buffers,
  `ScreenScale`, the ablation flags, the diagnostic counters. These were plain statics while
  the rebuild was main-thread only; sharing them across workers would corrupt one surface's
  geometry with another's, intermittently and invisibly. Note they **cannot** use inline
  initialisers: a `[ThreadStatic]` field is initialised only on the thread that runs the
  static constructor, so each is a lazy property instead.
- Diagnostics are harvested into a `TessellationStats` object at the end of `Emit`, on the
  thread that produced them. The main thread can no longer read the counters directly.
- `UpdateGeometry` no longer tessellates. UGUI calls it on any dirtying — rect change,
  material change, canvas rebuild — and doing 15 ms of managed work in that callback is how
  the cost got onto the main thread. It now presents the current mesh and flags a rebuild.
- `Update` lands a finished job (upload + stats) and dispatches a new one. Only one job per
  surface is ever in flight.
- `SetScene`/`SetData` **defer** while a job holds `_context`. Blocking the sync patch until
  the worker finished would hand the main thread back exactly the cost being removed. At a
  0.5 s tick against a sub-frame job this is almost never reached.
- A faulted job is logged. A swallowed worker exception would show up as a console that
  silently stops updating, with nothing in the log — the same fail-toward-broken direction
  the LOD and clip fast paths had to be guarded against.

**Multiple consoles now tessellate in parallel** rather than serially, which is the case the
whole LOD system exists to attack.

**The stats line now labels which half is on the frame** (`tessellate X off-thread + upload Y
on-thread`, plus a main-thread percentage). Reading the big number as frame cost is precisely
the mistake that would send the next round of tuning at the wrong target.

**CONFIRMED IN GAME 2026-08-20.** The GasUI console, unchanged:

```
vector "gas": 32 Hz, 14.33 ms/rebuild (tessellate 14.21 off-thread + upload 0.12 on-thread),
              0.38% of a frame on the main thread, 45.3% of a worker core,
              21800 verts, 800 shapes, 1012 px
```

Full geometry at full rate — 800 shapes, 21,800 verts, 184 bands, 616 rects, 32 Hz — with
**0.38% of a frame** on the main thread. Nothing was shed to buy it, which is the first thing
to check when a cost falls this far.

Frame-time ground truth: **15.9-20.5 ms (49-63 FPS) with the console on**, against 17-18 ms
off and **30+ ms on** before the change. The console being on is no longer distinguishable
from it being off.

Two things to carry forward:

- **The work did not get cheaper, it moved.** A worker core sits at 45%. Free on a machine
  whose main thread was the bottleneck and whose other cores were idle; not free on a
  CPU-starved one, and eight consoles in parallel will eventually find that ceiling.
- **`YS` is still 10 of the 14 ms** of worker time. The 184 bands across ~16 tanks (~11.5
  each) look like stacked strips faking a vertical gradient, which `units = "bbox"` made
  unnecessary the same day. Collapsing them is a free ~10x on the dominant op — but it now
  optimises something that costs no frames, so it is a nice-to-have, not a fix.

**Rate is not a lever.** 30 Hz is a hard floor by user decision; anything below is
unacceptable. Do not propose it again.

## What LOD is for now that threading landed (2026-08-20)

User's call after seeing the threaded numbers: **LOD is a nice-to-have, not a requirement.**
Correct, with one distinction worth preserving.

Both mechanisms were built to attack the *multiplication* — many consoles rebuilding on one
main thread. Threading attacks that directly and better, so:

- **Count LOD (`lod = 1`) is genuinely optional.** It is pure opt-in, so a scene that ignores
  it pays nothing. Keep it for very large decorative fields; do not reach for it by default.
- **Rate LOD still has a job, but a different one.** It no longer protects frame time — it
  protects the worker pool. One console measured **45% of a worker core**; eight at full rate
  is ~360%, which is comfortable on a machine with spare cores and not on a CPU-starved one,
  and the game wants cores of its own.

So the failure mode LOD guards against moved from "frame drops" to "worker saturation with
many consoles". Nothing was removed: both are config-gated and inert when unused.

**The 30 Hz floor still stands regardless.**

## `fo2` — the per-column opacity ramp (2026-08-20)

**A proposal of mine was checked with arithmetic and failed before it was written.** I had
told the user that GasUI's 184 bands were stacked strips faking a gradient, and that
`units = "bbox"` would collapse them ~10x. Reading the port showed the reasoning was sounder
than that: the ramp is measured **down from the rippling gas surface**, and a gradient is
linear in space, anchored to the bounding box.

With `FADE = 18` and ripple `3*sin + 2*sin` (so +/-5), the fade band's bbox is 28 tall:

| column | ramp actually spans |
|--------|---------------------|
| crest  | 0 -> 18/28 = **64%** of GAS_MAX |
| trough | 10/28 = **36%** -> 100% |

So a bbox gradient gives a bright line exactly where the fade should vanish. `fea` ramps
outward from a solid edge — the opposite direction. The previous session's twenty abutting
strips were the only thing that worked.

**The fix was therefore a renderer feature, not a scene change: `fo2` on `YS`.** Fill opacity
at the `y2` edge, ramping from `fo` at the sampled `y` edge. Each column interpolates between
its **own** two endpoints, so it follows the wave exactly, with **no extra geometry** — the
strip already emits a vertex per edge. 20 bands per gas gauge become 1.

Implementation notes worth keeping:

- `ResolvePaint` gained an `opacityOverride`. A ramped band builds its paint at opacity 1 and
  scales alpha per vertex. A ratio against `fo` would not work: the common case is `fo = 0`.
- The clipped per-quad path recovers the ramp from each vertex's position between the
  column's two edges, since clipping can put a vertex anywhere.
- **`FeatherEdge` had to be told about the ramp.** Because paint is now built at full
  opacity, the auto-feather would have drawn a brightness-1 halo above the gas surface
  exactly where the ramp says zero. It now takes the edge opacity and returns early when that
  is ~0. Caught by asking what else consumed `paint`, not by seeing it in game.
- `HasFill` was checked first: it keys off the presence of `f`, not opacity, so `fo = 0` does
  not skip the band.

**GasUI.lua edited in place** at `OneDrive/Documents/StationeersLua/GasUi/`, backed up first
(`GasUI.lua.bak-20260820-220111`) since this is not a git repo. `NBANDS` is gone; its
rationale block and the two changelog references were rewritten rather than left describing
code that no longer exists.

**Immediately found in game: a bright rule where the fade meets the body.** Reported from a
screenshot, diagnosed without a second round trip because the geometry pins it — the line sits
exactly `FADE` below the wavy surface.

Cause: the body band's sampled edge **abuts** the fade band, and `EdgeFeather == null` falls
through to the automatic ~1.3 px feather. It ramps `GAS_MAX -> 0` upward into a region the
fade band has already brought to `GAS_MAX`, and straight source-over gives
`1-(1-0.62)^2 = 0.86` — 40% brighter than either shape.

**It appeared now because of this morning's port-feedback #1 fix.** `FeatherEdge` used to be
skipped whenever a clip was present, and every gauge is clipped, so it had never run on these
bands. Letting it survive a clip was correct; the consequence is that any band whose sampled
edge abuts another shape now double-composites there.

Fixed scene-side with `fea_edge = 0` on the body band, and documented in both README and
REFERENCE as a general trap: **feathering exists to soften a silhouette, and an edge with a
neighbour flush against it has none.** The renderer cannot detect abutment, so the scene has
to say so. An edge already at opacity 0 is skipped automatically and needs no guard.

**Not yet measured.** Expected: 184 bands -> 24, `YS` from ~10 ms to ~1.5 ms.

## Battery port — what dogfooding found (2026-08-20)

`BatteryGauge.lua` ported to the vector layer as `BatteryGauge-vector.lua` (written alongside,
not over: the canvas build still works). The point was not the port — it was to find what the
docs and the renderer were missing. It found five things, four of them real defects.

**Deleted by the port:** supersampling and `set_resolution`; `COL_STEP` and its scaling by
battery count (the ~1,800-call canvas ceiling); `over()`/`bcol[]`/pre-composited palettes;
`fy()`; `arc_inset`/`CI[]`/`CO[]`/`draw_frame`'s ~62 calls per cell; `on_frame`. Kept: device
discovery, batch reads, joules-over-watts ETA, deadband, band thresholds.

**Renderer gaps found and filled:**

- **A shape with `fo = 0` was fully tessellated.** There is no conditional node, so the idiom
  for showing one of two things is two shapes with opposing opacity expressions — and both
  were being emitted every rebuild. `IsInvisible` now skips them. Note a ramped band builds
  its paint at opacity 1, so it is judged on its ramp endpoints instead.
- **Curve LOD**, on user suggestion: `YS`/`LS` join samples with straight lines, and the right
  count depends on on-screen size. Safe to automate in a way count LOD is not — `i` is a float
  and the geometry expressions are continuous in it, so evaluating at 0, 1.7, 3.4 … walks the
  **same curve** with a longer step. Nothing is dropped. Quantised to quarters of the authored
  count; never exceeds it.

**Doc gaps found, all now in README/REFERENCE:**

- **Arrays are 0-based in expressions, 1-based in Lua.** `$p[0]` is Lua `p[1]`. Inside a repeat
  `i` lines up by itself; a hand-written index does not, and an off-by-one reads as `0` rather
  than erroring.
- **A stroke is centred on its path.** Outlining a shape on its exact bounds leaves a `sw/2`
  gap against anything clipped to those same bounds — which is exactly what the battery cell
  frame did until it was inset.
- **`hash` is random, not evenly spread.** Reported as "bubbles on only half the battery".
  Measured: `hash(i)` for `i = 0..8` returns **0.405..0.988**, so the left 40% was empty. Not a
  hash bug — small-sample clumping. Fix is stratification, `(i + hash(i)) / n`, not a luckier
  offset.
- **Repeated blocks animate identically unless seeded.** Reported as "the bubbles are identical
  in the 8 batteries". The canvas build had `c.phase = i * 1.7` and per-cell `math.random()`
  seeding; the port dropped both. Baked in at build time as `hash(i + SEED)` and a constant
  phase offset, so it stays deterministic across clients.
- **Sample count is a drawing decision, not a signal-processing one.** GasUI sized `SAMP_GAS`
  at "~8 samples per period" from the sampling theorem. That is right for reconstructing a sine
  and wrong for drawing one: eight straight segments per period is visibly faceted, reported as
  "choppy, big lines instead of splines". Raised to ~20 per period (12 -> 36, 18 -> 48).

**Confirmed in game, and then matched to the HTML mockup.** The waves came out flatter than
`liquid-battery-preview.html`, and the reason generalises well beyond this file.

**A canvas build bakes its own limitations into its constants, and porting the constants
carries the artefact forward.** Three of the four differences were exactly that:

| | mockup | canvas build, ported verbatim |
|---|--------|-------------------------------|
| layer alphas | `0.85, 0.55, 0.60, 0.35` | `0.95, 0.75, 0.75, 0.55` — raised to survive pre-compositing |
| draw order | declaration order: deep base first, light film last | `{4,3,2,1}` — most opaque layer ON TOP, which flattens the depth cue |
| bubble paint | lightest shade @ 0.45 | white, because after pre-compositing the highlight and body shades were nearly identical |
| amplitude | `amp * IH` | `amp * IW * 1.6`, justified for tall slabs but these cells are near-square, so ~39% too deep |

None of those reads as wrong in the canvas source — each has a comment explaining why it was
necessary *there*. The lesson for any future port: **when the original had to work around a
platform limit, check the design reference rather than the working code.** The mockup was the
ground truth and it was sitting in the same folder the whole time.

Not reverted: the mockup's four *state* palettes. The canvas build replaced them with five
percent-based bands and moved charge direction onto a glyph so colour could mean "how full" —
a considered improvement over the reference, not drift from it.

**Ellipse tessellation was a flat 32 segments, fixed 2026-08-20.** Found when asking whether
round gas motes were affordable. They were not, and the reason was a renderer defect rather
than an inherent cost: every ellipse got 32 vertices regardless of size, so a one-unit mote
two screen pixels across cost eight times a rectangle, several hundred times per console. The
same flat count also under-tessellated large circles, which showed facets on a dial rim.

Rounded-rect corners had scaled with on-screen size from the beginning (`CornerSegments`,
3..16). Circles simply never got the same treatment, and nothing pointed at it until someone
asked for round particles. Now 6..48 by on-screen radius, quantised to fours.

Worth generalising: **a constant that is right for the case it was written for is invisible
until a different case arrives.** 32 is a perfectly sensible number for the dials and lamps
the ellipse code was first used on.

**A documentation pattern went stale within the hour, which is worth its own note.** After
making gas motes round I wrote a README pattern recommending `hash^2` for size variation. The
user's next report — "a few BIG ones and the small ones seem almost non-existent" — was that
exact formula: 4.3x between smallest and largest, with a floor at 0.45 that reads as absent
rather than small. Now `hash^1.4` over a raised floor, 2.0x, plus a per-instance breathing
term so no mote is permanently the runt.

**Then the breath itself was invisible, and the cause is a good rule.** Reported as "size
change is almost non-existent -- if I stare at it for a while I can see some subtle
differences". Measured rather than guessed: a mote is ~1.2 units of radius, the gas console
draws 911 px across a 768-unit viewbox = **1.19 px per unit**, so +-22% is **+-0.31 screen
pixels** and the diameter moved 0.63 px. Sub-pixel. The period was also 9-25 s, far too slow to
read as motion.

**A proportional change to a small shape is sub-pixel by construction.** The swing has to be
absolute -- sized in units worth a pixel or two -- and the rate has to be seconds, not tens of
seconds. Now +0.55..1.05 units (1.3..2.5 px of diameter) over a 3..8 s cycle. `(0.5+0.5*sin)`
rather than `sin` so the term only adds and the base stays the floor.

Worth keeping as a general check: **before tuning any animated quantity, convert it to screen
pixels.** Three separate things this session were invisible for this reason -- the feather
before ScreenScale was fixed, the ripple amplitude in `Patterns.lua`, and now this.

**Documentation written from a change that has not been looked at yet is a guess.** The pattern
was recorded confidently, from arithmetic, one message before the render contradicted it. Docs
describing a just-made visual decision need the same "confirmed in game" bar as the code does.

Also noted in REFERENCE while fixing it: **`^` is the power operator and there is no `pow()`.**
Nearly shipped `pow(hash(i+4),1.4)` into GasUI; an unknown function throws at parse time, so it
fails loudly rather than silently, but the scene would not have drawn.

**The pattern across all five: every one is something the renderer does correctly and the
author cannot guess.** That is precisely what a port is for, and none of them would have been
found by writing more tests against the renderer.

## Documentation, written properly (2026-08-20)

The previous pass was rejected, correctly: the README leaned on `vector-format-spec.md`
instead of standing alone, and the "examples" were the scenes built while debugging the
renderer. Those teach the reader nothing about authoring.

**Written:**

- `README.md` — rewritten as a guide. Why the layer exists (op count, not fill area), the
  two-element structure, the two clocks, `RP` versus `YS`, the three colour mechanisms, a
  Traps section, and a performance section that now says *stop budgeting* because threading
  landed.
- `REFERENCE.md` — every node, attribute, `defs` declaration and expression function.
  **Checked against the parser, not the spec.** Where they disagreed the code won.
- `examples/01-hello` … `08-console` — a progressive set, each introducing one idea and
  runnable on paste. 01 shapes and the viewbox, 02 motion with no tick, 03 the two-element
  split, 04 repeats and `hash`, 05 `YS` versus `RP` (drawing the same wave both ways so the
  staircase is visible), 06 paint, 07 clipping, 08 a complete four-tank console.
- `About/About.xml` — the in-game description, which still read
  *"Scaffold. The element type resolves and draws a rounded rectangle; the scene format is
  not wired up yet."*

**Three things the code corrected while writing:**

1. **Count LOD is pure opt-in.** This file claimed automatic application to repeats >= 50.
   It is not implemented; see the correction above.
2. **Label syntax.** `08-console.lua` was written with `text = ...` / `style = { fg, size }`
   from memory. The real form is `props = { text = ... }` with
   `style = { font_size, color, align }` — found by reading `AlphaDemo.lua`. That would have
   been another example that ships broken on paste.
3. **`06-paint.lua` referenced `@fade_clean` / `@fade_muddy` without declaring them**, with a
   footnote telling the reader to add them. Fixed by declaring them, since "runs on paste" is
   the whole contract of these files.

**Verification, since Lua cannot be run here:** a bracket and string-literal balance checker
over all nine files (`scratchpad/luacheck.py`), plus `ElementTree` on both `About.xml`s. That
catches gross syntax errors, not semantic ones — the examples still want one pass in game.

Worth keeping: **documentation is a place where remembered API is as unreliable as remembered
code.** Two of the three defects above came from writing what I expected the API to be. The
existing demo scenes were the cheapest available source of truth for the ScriptedScreens side.

## Measuring the remaining open questions

**Frame time is the only ground truth.** Everything else this mod logs is its own stopwatch
timing its own code, which misses GPU upload stalls, extra draw calls and GC triggered
elsewhere — and it cannot be compared against "console off", because with no surfaces there
is nothing to report.

`FrameMonitor` runs for the whole session regardless and logs every 5s:

```
frame: 16.94 ms mean (59 FPS), 24.10 ms p99, 41.2 ms worst, vector ON
```

**Read p99 alongside the mean.** A rebuild every few frames arrives as a spike, and a mean
hides a stutter that is obvious to look at.

For a clean A/B set `Renderer.Enabled = false` in the config rather than powering the console
down: the scene, the elements and the mod all stay loaded and only the geometry stops, so the
two runs differ in exactly one thing.



`VectorGraphic.Stats` logs one aggregate line every 5s across all surfaces:

```
vector: N surface(s), R rebuilds/s, M ms each, L% of a frame budget, peak V verts
```

**Stats are per surface and name their scene** (`vector "stress": 28 Hz, 4.94 ms/rebuild,
13.8% of a core, 20412 verts, 1700 shapes, 855 px`).

Earlier versions aggregated across surfaces and divided by their count, which silently
averaged unrelated work: a console animating at 28 Hz beside a static one reported "14 Hz
each" — below a 15 Hz floor nothing had breached — and `ms per rebuild` blended two scenes
into a figure describing neither. An attempted A/B of expression cost had to be thrown away
because of it. Any measurement worth acting on has to be attributable to one scene.

**Rebuild rate quantises to the frame rate.** Gating in `Update` means rebuilds can only land
on frame boundaries, so a 28 Hz request at 42 FPS becomes every second frame = 21 Hz. Not a
bug, but it makes the effective rate step rather than track the setting.

**Read `L` first.** One frame at 60 FPS is 16.7 ms, so `L%` is the share of a whole frame
that tessellation costs. Under ~5% the CPU side is a non-issue and any remaining FPS loss is
elsewhere — draw calls, overdraw, or the game itself. `StressTest.lua` drives it, with
`QUADS` and `ANIMATE` at the top.

**`ANIMATE = false` must log nothing at all.** No `t` reference means no rebuild, so silence
is the pass condition, not a low number. That is the sharper half of the `on_frame` question:
a static scene should be free, not cheap.

**The `on_frame` question is two questions, and only one is answered.**

*Does animation still need per-frame Lua?* **No, demonstrated.** `StressTest.lua` has no
`tick` and no `on_frame`, and its field animates. That is the design claim and it holds.

*Is it cheaper than canvas + `on_frame`?* **Still open**, and it needs the same console built
both ways and compared at 1, 2 and 4 instances. Watching an FPS counter with nothing to
compare against cannot answer it, and an earlier comment in `StressTest.lua` implied it
could — corrected. The brief's baseline
is ~10 FPS lost per console with 1,292 canvas calls per frame. Note `on_frame` remains
necessary for anything that cannot be written as `f(t, i, $data)`: motion with memory
(integration, trailing averages) or that depends on state sampled faster than the 0.5s tick.
The port is what will reveal whether the real UI contains any such thing.

**GasUI port: being done separately** (deliberately, to keep it out of this context). Worth
capturing on return: which of the ~950 view lines actually disappeared, whether anything
needed `on_frame` after all, whether text-on-top was sufficient, and the `L%` figure for a
real console versus the canvas build.

## GasUI cost 20-25 FPS — cause found and fixed (2026-08-20)

Diagnostics, one console:

```
vector "gas": 29 Hz, 17.46 ms/rebuild (tessellate 17.35 + upload 0.11), 50% of a core,
              16576 verts, 800 shapes, 948 px
```

**800 shapes costing 17 ms, against 1,701 shapes costing 4.4 ms in the stress test** — 21.5 us
per shape versus 2.6. Vertex count was ordinary (20/shape), so something was expensive *per
shape*, and it was not geometry volume.

**Cause: `YS` bands were being ear-clipped.** `EmitBand` had been changed to build a closed
contour and route it through `FillContour` so clipping and gradients would work on it. A wave
is non-convex, so it missed the convex fast path and landed in the triangulator — which is
O(n^3) here, since `IsEar` rescans every remaining vertex per candidate. Measured with
`--bench` on .NET 8:

| band | contour points | cost |
|------|----------------|------|
| 16 samples | 32 | 21.6 us |
| 24 samples | 48 | 49.8 us |
| 40 samples | 80 | 139.5 us |

A plain rect is 0.9 us. **One band cost as much as 24-150 rectangles**, growing ~O(n^2.2), and
Mono is 2-3x worse again. A console with a band per tank is exactly the pathological case.

**Fix: a band is already a strip and needs no triangulation at all.** `EmitStrip` emits quads
between adjacent samples — O(n), and in the common case (no clip, flat paint) it shares
vertices along the strip so n samples give 2n vertices rather than 4 per quad. Clipping or
gradient refinement falls back to per-quad handling, where a quad is convex and cheap to clip,
still linear in sample count.

The lesson is narrower than "ear clipping is slow": **routing a shape through the general path
to inherit its features can cost more than the features are worth.** Clipping and gradients on
bands were the reason for the change, and both are available per quad at a fraction of the
price.

## Port feedback — what was done (2026-08-20)

Five of the seven acted on; all three "corroborated from memory" claims were re-read against
the source first and all three held.

| # | finding | status |
|---|---------|--------|
| 1 | feather cannot ramp one side; halo escapes clip | **fixed** — and it was worse than reported |
| 2 | `i1` vs `i` flips meaning inside `YS` | **fixed** |
| 3 | gradient coordinates static | **fixed** via `units = "bbox"` |
| 4 | clip under transform | **fixed** — clips are now scene-space |
| 5 | silent empty console | **partly** — visible marker; chip log unresolved |
| 6 | `WorthFeathering` pops | **fixed** — cliff replaced with a ramp |
| 7 | capture excludes vector meshes | **not started** |

**#1 was worse than described.** `EmitBand` routed its contour through `FillContour`, which
emitted its own ring on *all four sides* built from the clipped contour, and *then* `EmitBand`
added `FeatherEdge` on top — so the sampled edge was feathered twice while three sides gained
a halo that escaped the clip. Now: the ring is suppressed for bands, `FeatherEdge` survives a
clip, and both ring and edge clamp their outer vertices inside the clip via
`ClipRegion.ClampInside` — where the clip cut the outline the ramp collapses to zero width, so
a cut edge gets no halo and an untouched edge keeps its feather. New **`fea_edge`** gives the
sampled edge its own width.

**#3 shipped as `units = "bbox"` rather than expression coordinates.** Same result for the gas
case, no per-frame evaluation, and it reuses a bounding box the tessellator already computes.
`Paint` carries the bounds and maps points into gradient space; the radial focus maps back the
other way for banding. Tested: one declaration gives mid-grey at the midpoint of both a
10-unit and a 200-unit shape.

**#4 was a spec/code disagreement, not just a bug.** §7 reads as scene coordinates; the code
used the referencing group's local space — identical only when that group has no transform,
which every demo and test happened to satisfy. `Frame` now carries `SceneToLocal` and declared
outlines are brought into local space. `GradientDemo.lua`'s clip group is now **translated**,
so the previously untested case is covered by a demo.

**#5 is partial and honest about it.** A missing clip id, a degenerate clip, an unknown op or
a scene that emits nothing now draws a magenta hatched border with one stripe per problem,
instead of looking like a switched-off console. Also fixed: `ClipRegion.FromPolygon` returning
null silently fell back to **no clip** — "draw everything", the least safe default — and now
reports. Routing to the chip log itself is unresolved: that log belongs to StationeersLua's VM
and no public sink was found from the ScriptedScreens or game assemblies. Worth another look.

**#7 not attempted.** Probably the highest leverage of the seven, since it is what forces a
human into every visual check, but it is a different codebase.

## Next session — findings from the GasUI port (2026-08-20)

Reported by the user after porting a real console. Ordered by cost incurred, not by size of
fix. Where a claim matches my memory of the code it is marked **corroborated**; those still
want re-reading before acting, given today's record on remembered code.

### 1. Feathering cannot express a directional ramp, and fails silently

**Cost the most time of anything in the port.** Two halves, both wrong in the same situation:

- `FillContour` emits `FeatherRing` unconditionally — a halo on *every* side — and builds it
  from the **already-clipped** contour, so the halo draws outside the clip meant to contain
  it. **Corroborated:** `FillContour` clips `outer` into `contour`, then passes `contour` to
  `FeatherRing`.
- `EmitBand`'s `FeatherEdge`, the one that *is* directional, is skipped whenever
  `frame.Clip != null`. **Corroborated:** the guard reads `if (feather > 0f && frame.Clip == null)`.

So `fea` + `clip` reliably delivers the wrong half of both: no fade where one was wanted, and
a halo escaping the tank.

Fixes worth having: clip the feather ring, and let the directional edge feather survive a
clip (`ClipRegion.ClipPolyline` already does this job for strokes). A **`fea_edge` that ramps
one side only** would make soft liquid and gas surfaces a one-node job.

### 2. `i1` versus `i` flips meaning inside `YS` depending on which attribute you are in

Geometry is evaluated inside the per-sample `PushRepeat`; paint is resolved *after*
`PopRepeat`. So an enclosing repeat is `i1` in `y`/`y2` but plain `i` in `fo`/`f`. Nothing
warns — the wrong one silently yields 0.

**Corroborated:** `EmitBand` pushes and pops per sample while building the point lists, then
calls `ResolvePaint` once, outside that scope.

Fix: resolve paint inside the same repeat scope. Removes the trap entirely rather than
documenting it.

### 3. Gradient coordinates are static, which rules gradients out of most live UI

`ParseDefs` reads them with `PropNumber`, so a ramp is anchored in space and cannot track
anything data-driven. Any gradient anchored to a *value* rather than a *place* has to be
faked with stacked strips.

**The user rates this the highest-value single addition, and I agree.** Two shapes it could
take:

- expression-capable `x1`/`y1`/`x2`/`y2`, or
- an SVG-style `objectBoundingBox` mode where the gradient spans the shape's own bounds.

The second is arguably better: it needs no per-frame evaluation and covers the common case
(a ramp across whatever this shape happens to be) without the author computing coordinates at
all.

Corrections to the record from the same investigation: gradients work fine inside clipped,
animated groups — `GradientDemo.lua`'s `@bars` proves it. The earlier "clamps to the end
stop" diagnosis was *not* the cause in the liquid case; the real fault was declaring the ramp
`0..GAUGE_H` while the geometry sat at an absolute `gy`, which is the already-documented
misplacement trap.

### 4. Clip under a transformed group does not match the spec

§7 promises a clip is re-expressed through nested transforms. A shared clip plus translated
groups did not behave that way. **Every demo uses an untransformed group, so the documented
case is the untested one.**

Also noted: `ClipRegion.FromPolygon` returning null **silently falls back to no clip** rather
than erroring — so a rejected clip shape means "draw everything", the least safe default.

Fix it or add a worked example; either way it needs a test with a transformed group.

### 5. A scene that draws nothing gives no feedback

Bad clip id, a rejected non-convex clip, an unknown op — all render as an empty console.
Warnings exist (`clip "x" has no usable shape`) but only in `BepInEx/LogOutput.log`.

**Surfacing them in the chip log, where the script author is already looking**, would have
turned several dead ends into one line. This is probably the cheapest large win in the list.

### 6. `WorthFeathering`'s hard cutoff pops

The test is `smallest > feather * 2`. A stack of thin bands crosses that threshold as the
camera moves, so antialiasing appears and disappears and the whole thing shimmers. A falloff
— ramp the feather down toward the threshold instead of switching it off — would stop it.

### 7. Tooling: `capture_scripted_screen` does not include vector meshes

The user was debugging blind and relying on a second party for every visual check.
**Probably the single biggest multiplier on how long the port took.** Worth investigating
whether the capture path can be made to include a `CanvasRenderer` mesh, since without it
every visual question needs a human in the loop.

### Caveat carried over

A midpoint-bias fix was pushed to the port code based on a described symptom rather than an
observed one. If the fade still starts with a visible step out of the tank colour, that was
not the cause and it needs looking at again.

## Open questions

1. ~~**Payload ceiling.**~~ **Answered from the source, 2026-08-19: there is no hard
   ceiling.** The path is `UiBatch` → `MessagePackSerializer.Serialize` →
   `"MP_UI_SYNC_V1:" + Convert.ToBase64String(...)` → split into 600-char chunks
   (`MaxUiSyncChunkChars = 600`) → one `SsCartridgeUiSyncMessage` per chunk via `SendAll`.

   Chunk count is `(text.Length + 599) / 600` with **no cap**, and nothing on this path
   rejects or truncates an oversized payload. The `too large` guards that do exist are on
   the *client→server input* path (`MaxReassembledInputChars`) and on canvas ops
   (`CanvasCommandMaxOpsPerBatch`) — neither applies to element sync.

   So the real cost is **linear, paid in network messages**: base64 inflates by 4/3, so a
   payload of B bytes becomes roughly `1.33 * B / 600` messages. 100 KB of MessagePack is
   ~222 messages per sync. The constraint is message flood and bandwidth, not a limit to
   design around — which supports keeping the structure/data split, since only the small
   data payload is resent per tick.

   **Crucially, the whole send path is gated on
   `NetworkManager.IsActive && IsServer && HasRemoteClients()`.** In a solo session none of
   it runs — elements apply locally with no serialisation at all. A single-player test
   therefore measures Lua build cost and nothing about chunking; the network ceiling needs
   a second client connected.

   `PayloadCeilingTest.lua` plus the `ReportProbe` instrumentation in `VectorElementPatch`
   measure this empirically: a ladder of 50/200/1000/4000/12000 nodes, one rung per tick,
   logging `claimed` vs `nodes` received.
2. **Rebuild cost at scale.** ~1,700 mote quads re-evaluated per frame on the CPU across a
   full console. Expected trivial; measure before committing to CPU evaluation over pushing
   time-varying transforms into a shader.
3. **Whether `on_frame` is still needed at all** under 2/s data plus client-side animation.
4. **Distance-based tessellation tolerance** causes topology rebuilds as the player
   approaches a console. Likely needs hysteresis.

## Spec

`vector-format-spec.md` **revision 2 (2026-08-19) is current** — rewritten against structured
props. The string wire format, sentinel prefixes and `<`/`>` prohibition are gone. `lt`/`gt`
stay as functions by decision, not necessity. New §10 records the measured budgets.

Two findings from the rewrite that constrain everything downstream:

**`set_props` cannot do a partial update.** It merges the given props into the element's
existing props and then `ops.Upsert`s the *whole element*. So structure and data must live on
**separate elements**, or every data tick resends the entire scene. Revision 1 reached the
same conclusion for a different (now obsolete) reason; it holds for a real one.

**The Lua instruction budget is the actual ceiling, not the payload.** 50,000 instructions
per tick, tick = 0.5 s. Measured: 4,000 constructed nodes succeed, 12,000 fails with
`Instruction limit exceeded` in the builder loop — roughly 12 instructions per six-attribute
node, so ~4,000 nodes/tick. This makes `RP` (repeat) the load-bearing node in the format
rather than a convenience, and it bounds how big a structure can be built in one tick.

### Scaffold — WORKING, confirmed in game 2026-08-19

`VectorTest.lua` renders both elements correctly on a ~480x480 console: green rounded rect
at the default radius, amber at `radius = 6`, no magenta anywhere. That single screenshot
confirms the whole chain end to end:

1. an unknown element type really does yield a usable, correctly parented and sized host;
2. the Harmony postfix claims it, and clearing the fallback `Image` hides it properly;
3. **structured props survive Lua → MessagePack → sync → client** — `fill` (string) and
   `radius` (number) both arrived. This was the load-bearing unverified assumption, and it
   holds. No string wire format is needed;
4. a `MaskableGraphic` on a child composites correctly with the surrounding ScriptedScreens
   UI, obeys the surface layout, and needs no render texture;
5. corner arcs tessellate and rasterise smoothly at display resolution.

Nothing in the milestone-2 design is blocked on an unknown any more. What remains is
building the format on top of a proven surface.

### Tessellator — built 2026-08-19, not yet run in game

`Expression.cs` (parser + evaluator), `SceneModel.cs` (`VecNode`/`VecScene` + prop parsing),
`Tessellator.cs` (tree walk → `VertexHelper`), rewritten `VectorGraphic.cs`, and a
structure/data pairing registry in `VectorElementPatch.cs`. Builds clean. `VectorDemo.lua`
is the end-to-end exercise.

**Implemented:** `G` (full transform: scale→rotate→translate about `a`, plus group opacity),
`RP`, `R` (with true arc corners), `C`; solid fills with `fo`; the complete §6 expression
grammar including `hash`/`hash2`; `$name` and `$name[expr]` data binding; viewbox `fit`.

**Strokes (added 2026-08-19).** `Stroke.cs` is one tessellator serving everything linear:
`L`/`Y` (literal point lists), `LS` (sampled polyline), `SP` (Catmull-Rom spline), and
outlines on `R`/`C`/`Y`. Plus dashes (`dash`/`dofs`) as a pre-pass that splits a path into
runs, and butt/square/round caps.

Built as **offset polylines with one left and one right point per input point**, so both
sides have matching counts and the body is a plain strip. That is what makes the feather a
single seamless ribbon per side; a per-segment approach would seam at every joint. The
trade: joins are a **clamped miter**, not inserted bevel/round geometry — invisible at UI
stroke widths, visible on very wide strokes at sharp corners. `join` is closer to a hint
than a guarantee. Documented in the spec rather than hidden.

`SP` uses Catmull-Rom, not bezier, because it passes *through* its control points — a scene
author supplying data points wants a curve through them, not handles.

**Paths and concave fill (added 2026-08-19).** `PathData.cs` parses full SVG path syntax
(M/L/H/V/Q/C/A/Z, relative forms, implicit repeated commands, exponent numbers) and flattens
beziers and arcs. `Triangulator.cs` is ear clipping with hole bridging, replacing the centre
fan everywhere a fill is not guaranteed convex.

Two decisions worth keeping:

*Parse and flatten are separate.* `d` is a string and cannot contain expressions, so the
command list is static — only the tolerance varies with resolution and camera distance.
Flattening happens at draw time against a **quantised** scale (~12% buckets), which caches
the result and simultaneously answers open question 4: ordinary camera drift does not cross a
bucket boundary, so it does not retessellate. Hysteresis for free.

*Holes by bridging.* The largest closed subpath is the outer contour, the rest are holes,
joined by a zero-area seam. Feathering deliberately uses the **original** outline, not the
bridged one — feathering the seam would produce a visible spike.

**Four bugs found by in-game renders of `PathDemo.lua` (2026-08-19), all fixed.** Note the
first two both had to be fixed before holes worked — fixing only the seam left the artefact
looking unchanged, which is what made this take two passes:

1. *Bridge seam was malformed.* It emitted
   `outer[best] → outer[best] → hole… → hole[r]` and never returned to `outer[best]`, so the
   contour ran from the hole straight to `outer[best+1]` and cut a visible chord across the
   shape. A correct bridge is
   `outer[best] → hole[r] → … → hole[r] → outer[best]`; **both** duplicated points are load
   bearing, they are what makes the seam zero-area.
2. *The ear test rejected duplicated seam vertices.* Bridging **intentionally** duplicates
   the seam points, and `IsEar` rejected any candidate triangle containing another vertex by
   **position**. A duplicate sits exactly on its twin, so it always tested as "inside", every
   ear touching the seam was refused, clipping stalled, and the shape was left partly
   triangulated. Fix: skip candidates coincident with a corner, not merely sharing its index.

   Measured on a 29/15 concentric ring (`scratchpad/tri_check.py`, a port of this algorithm):
   before, clipping stalled with 23 of 50 vertices left and filled 1295.6 of an expected
   1935.2 area — **the missing third is exactly the visible wedge**. After, it completes with
   48 triangles and 1936.3 area, 0.06% off.

   A stall now logs a warning. It previously failed silently and showed up only as a
   rendering artefact, which reads as a mistake in the scene rather than in the tessellator.

**Regression tests** (`ScriptedScreensVector.Tests`) cover all of this. Each was verified by
reverting the fix and confirming the test fails — a regression test that has never been seen
to fail is an assumption, not a test. Measured on the broken code: ring **−32.4%** area (the
visible wedge), square-with-hole and two-holes produce **no geometry at all**, duplicated
closing vertex **−50%**.

Two things learned while writing them, both of which had to change the tests:

- *Area alone is insufficient.* With only the dedupe reverted, every area check still passed
  — a degenerate zero-area triangle keeps the total correct while emitting geometry that
  should not exist. The duplicate-vertex and star cases therefore also assert triangle
  **count** (a simple polygon of n vertices yields exactly n−2).
- *The two fixes overlap.* Either the coincidence skip or the dedupe alone repairs the
  duplicated-closing-vertex case; only reverting **both** reproduces the original failure.
  Worth knowing before deleting one as redundant — they are belt and braces on purpose.

3. *Coincident vertices were not stripped before clipping.* Any path ending in `Z` at its
   start point yields a contour whose last point repeats its first; ear clipping then makes a
   degenerate triangle that shows as a crack down the shape. `Stroke.Clean` already did this;
   the triangulator now does too, for holes as well as the outer contour.
4. *`S` and `T` were never implemented* — the parser hit its unknown-command path and
   silently abandoned the rest of the `d` string, so a curve using them rendered only up to
   that point. Now supported, reflecting the previous control point per curve family.

*`fr` now does something.* `evenodd`: every further contour is a hole. `nonzero` (default): a
contour is a hole only when wound *against* the outer one; wound the same way it stays
filled and is drawn as its own region. This is winding comparison, not scanline evaluation —
exact for nested non-overlapping contours, approximate where contours partially overlap.

**Self-intersecting contours are unsupported** and fail visibly rather than gracefully.
Detecting them costs more than the fill, so the contract is simple polygons.

**Gradients and clipping (added 2026-08-19).** `Gradient.cs` (defs, stop sampling, and a
`Paint` struct that is either a flat colour or a gradient) and `Clip.cs` (convex region as
half-planes, Sutherland–Hodgman for fills, Liang–Barsky style parametric clipping for
strokes). `Tessellator` was restructured: `Frame` now carries a clip region, every shape
routes through `FillContour`, and the old centre-fan path is gone.

*Gradient accuracy is a geometry problem, not a shader one.* Vertex colours interpolate
linearly, so a **two-stop linear gradient is exact** and is emitted untouched. Multi-stop
linear is piecewise affine and radial is not affine at all, so both get midpoint subdivision
until each triangle spans a small slice of the gradient parameter. Skipping this shows as
flat facets across a large radial fill.

*Clipping is geometric because it is convex-only.* A convex region is an intersection of
half-planes, so no stencil buffer is involved and there is no draw-time cost. Fills are
reshaped before triangulation; strokes are **cut** into the runs that fall inside, since a
stroke must be severed by a clip rather than redirected along the boundary.

*Coordinate-space decision:* gradient geometry and clip outlines are interpreted in the
**local space of the node or group using them**, not absolute scene space. Identical for
untransformed artwork; under a transform a gradient rotates with its group, which is
normally what is wanted and avoids an inverse transform per vertex. A nested group
re-expresses an inherited clip through the inverse of its own matrix.

**Two sign errors in `Clip.cs`, found on first in-game render (2026-08-19), both fixed.**
The whole console flooded with one gradient and nothing else drew. The stall warning added
after the triangulator bugs named the cause immediately — 28 × `triangulation stalled …
contour is likely self-intersecting`, one per clipped fill — so this was minutes of
diagnosis rather than another round of screenshot archaeology. That warning has now paid for
itself.

1. *`Intersect` had its parameter negated.* `t = num / -den` where the derivation gives
   `t = num / den`: solving `(p + t·seg − a) × edge = 0` yields `t = −((p−a) × edge) / (seg ×
   edge)`, and `seg × edge == −den`, so the two negations cancel. The intersection landed on
   the far side of the clip edge, which is exactly how every clipped contour became
   self-intersecting.
2. *`ClipSegment` had enter/exit swapped.* Inside is `dot(normal, P − a) ≥ 0`, i.e.
   `t·den ≥ num`, so a **positive** denominator is a lower bound (entering) and a negative
   one an upper bound (leaving). Reversed, it kept precisely the part of each segment lying
   *outside* the region.

Verified the same way as the triangulator bugs — revert and watch the tests fail. On the
broken code: partial overlap **+456%** area (the flood), subject-covers-region **0** area,
polyline cut **0 runs**. `ClipTests` covers subject inside / outside / partial / enclosing,
that a clipped contour still triangulates cleanly, and polyline cutting.

**Clipping confirmed working in game 2026-08-19** — zero `triangulation stalled` warnings
(down from 28), bars cut square at the window edge and correctly rounded at its corners,
dashed boundary drawn unclipped for comparison. Gradients confirmed too: the two-stop linear
and the gradient *stroke* both render correctly.

*Authoring trap worth remembering:* gradient coordinates are in the same space as the
geometry using them, and out-of-range parameters clamp to the end stops. The first
`GradientDemo.lua` declared `heat` at `x1=10 x2=90` but drew the shapes at `x=108..192`, and
`orb` centred at `cy=45` for a circle at `cy=118` — every one rendered a flat end-stop colour
and looked like three broken gradients. It was the demo, not the renderer. Each gradient is
now placed over the shape it fills.

**Radial gradients fill as concentric bands, not by ear clipping (2026-08-19).** First
attempt faceted badly; the refinement test only measured parameter spread across a
triangle's three *corners*, and ear clipping produces slivers reaching across a shape whose
corners all sit on the rim at the same parameter. Corner spread ~0 meant nothing ever
subdivided. `Gradient.SpreadOver` now samples the centroid and edge midpoints too (tested:
corners report 0.000, true spread 0.956).

That helped but did not finish the job — **midpoint subdivision of a sliver yields smaller
slivers**, and neighbouring ones do not share vertices, so faint streaks remained at the
T-junctions. The structural fix is `FillRadialBands`: rings of the outline scaled toward the
focus, so vertices land at even gradient parameters and bands follow the ramp instead of
cutting across it. Requires the focus inside the outline (the rings are a homothety, valid
only for a star-shaped region about that point); `Encloses` checks and falls back to ear
clipping.

**Colour is animatable, three ways (and the third was added on 2026-08-19).** `fo`/`so` are
expressions, so *fading* always worked. `f = "$name"` binds to a data colour. New:
`f = { grad = "name", at = "=expr" }` samples a ramp at an expression — genuine colour
interpolation over `t` or over `$data`, blending through every stop. It exists because the
expression evaluator is scalar and a colour is not a scalar, so `f = "=lerp(...)"` has
nothing to return; routing through a ramp sidesteps that without adding a colour type to the
language. Result is a flat colour, so it costs no per-vertex lookup and no subdivision.

**Radial band count follows on-screen radius** (~1 ring per 2.5 screen px, 12..96), not just
stop count. A fixed count bands visibly when the console is large — 16 rings over a 90px
radius is a step every 5px, which is exactly the residual stairstepping reported after the
banding fix landed.

**Alpha works throughout and needed no changes**: `#rrggbbaa` literals, `fo`/`so`
expressions, group `o`, and the feather ramp all multiply into straight source-over
blending. The one trap is documented in spec §5.1 and pinned by `AlphaInterpolation`:
because blending is *not* premultiplied, a gradient interpolates RGB and alpha
independently, so fading to transparent **black** passes through grey at half alpha
(measured: `#808080@0.50`) while fading to the same colour at zero alpha holds its hue
(`#FFFFFF@0.50`). `AlphaDemo.lua` shows both side by side.

*Known limits:* a clipped fill cannot carry holes (a hole straddling the boundary needs
boolean subtraction — dropped with a warning); clip outlines are static, so `t` inside a clip
is silently constant; `$name` colour bindings from the data payload are still unimplemented.

**Two edge-quality fixes after first in-game render (2026-08-19).** The first demo's liquid
surface came out visibly stair-stepped. Two independent causes, both now addressed:

*Geometry, and the larger of the two.* The wave was `RP n=34` emitting 34 separate
rectangles, each with its own flat top — a staircase by construction, at any sample density.
`RP` instantiates discrete shapes and cannot produce a connected edge. Added **`YS`, a
sampled band**: `n` samples of `x`/`y`/`y2` with `i` bound as in a repeat, joined into one
triangle strip. Tessellated as a strip rather than a fan, because any wave with two crests is
non-convex and a centre fan would fold. Rule of thumb now in the spec: `RP` for discrete
repeated things, `YS` for anything that should read as a continuous edge.

*Antialiasing.* UGUI applies none, so every edge was hard. Implemented `fea` — a ring (or
edge strip) of quads fading to zero alpha. **The default is automatic and resolves to ~1.3
screen pixels, not a fixed scene-unit value**, because the same scene draws at very different
sizes with console resolution and camera distance; a fixed value is invisible when small and
a halo when large. Feather normals come from adjacent edges, not from the shape centre — a
centre-radial normal badly over-feathers the short sides of anything long and thin, such as a
1×1 mote.

Decisions worth keeping:

- `hash` is an integer avalanche over fixed-point input, **not** `fract(sin(x)*k)`.
  Transcendentals are not guaranteed bit-identical across platforms and every client must
  agree or a particle field looks different to each player.
- Expression evaluation is a tree walk, not the stack program the spec describes. Same
  semantics; revisit only if profiling says dispatch matters.
- Division by zero yields 0, not infinity — an infinity would poison vertex positions and
  produce a corrupt or invisible mesh rather than a visible glitch.
- `UsesTime` propagates up the tree so `VectorGraphic.Update` is a single boolean test. A
  scene with no `t` reference genuinely costs nothing per frame.
- The scene→UGUI Y flip happens once in the viewbox matrix, so every node below it is
  written in the spec's top-left/+Y-down coordinates.

Second mod at `ScriptedScreensVector/` (sibling of the fonts project, its own csproj).
Builds clean, deploys to `mods\ScriptedScreensVector`. Files: `ScriptedScreensVectorPlugin.cs`
(LaunchPad entry, `Harmony.PatchAll`), `VectorElementPatch.cs` (the single postfix),
`VectorGraphic.cs` (`MaskableGraphic`, draws one rounded rect), `PluginInfo.cs`,
`GlobalSuppressions.cs`, `About/About.xml`, `VectorTest.lua`.

It imports the fonts project's `Stationeers.VS.props` / `.References.props` by relative
path, so `SteamLibraryDirectory` and the Documents override stay in one file.

Build notes worth keeping:

- `BepInEx.AssemblyPublicizer.MSBuild` 0.4.3 with `Publicize="true"` on the reference works;
  the internal types compile against directly.
- There is **no `MessagePack.dll`** in the ScriptedScreens folder — referencing it fails.
  Not needed: the MessagePack attributes on `UiValue`/`UiProp` are ignored when unresolvable.
- `GlobalSuppressions.cs` must be copied to any new mod project — CA2243 fires on the
  reverse-DNS `[StationeersMod]` GUID otherwise.
- Keep the `Harmony` instance in a static field, or CA2000 fails the build.
- MSBuild XML comments cannot contain `--`.

The patch clears the fallback `Image` (setting `color = Color.clear`) rather than destroying
it, because ScriptedScreens re-adds it on every upsert. `VectorTest.lua` sets `style.bg` to
magenta specifically so a failed postfix is visually obvious.

## Suggested first tasks

1. ~~Get milestone 1 compiling~~ (done) — now launch the game and confirm the font list in
   `BepInEx/LogOutput.log`, then a `<font="Name">` tag in a real ScriptedScreens label.
   This proves the loop before anything harder depends on it.
2. Rewrite the spec against structured props.
3. Measure the payload ceiling with a throwaway element carrying a large nested prop table.
4. Scaffold the vector mod: publicised reference, the single Harmony postfix, and a
   `MaskableGraphic` that draws one hardcoded rounded rect. End-to-end before any
   tessellation depth.

---

## Milestone 3 — HTML/CSS layer on Unity UI Toolkit (started 2026-09-09)

**Why.** Familiarity: far more people can read and write HTML+CSS than an SVG-style scene
graph. The vector mod is untouched and stays; this is a third project, `ScriptedScreensHtml/`,
reusing only the harness (csproj, LaunchPad entry, the single `ApplyElementInternal` postfix
claiming element type `html`, About, suppressions).

**The shortcut: the game ships `UnityEngine.UIElementsModule.dll`,** Unity's UI Toolkit. It is
a DOM with a CSS subset, flexbox layout (Yoga), borders, radii, transitions and its own text.
Confirmed in game the same evening: all three runtime shaders resolve
(`Hidden/Internal-UIRDefault` etc.), `LegacyRuntime.ttf` is available as a font, and a
`PanelSettings` with `targetTexture` renders into a `RenderTexture` shown by a `RawImage` on
the element's child. No theme stylesheet exists in the build (warning is cosmetic; styles are
set inline). UXML/USS importers are editor-only, so HTML and CSS parsers are ours to write;
they map to `VisualElement` and its `style` object.

**Structural difference from the vector mod, and its consequences.** The vector mod emits a
mesh into the world canvas: resolution is free and a static scene costs nothing. UI Toolkit
paints into a texture, so both had to be built:

- *Resolution follows on-screen size.* The element rect is in canvas units (436x400 on a 460
  console) and a texture at that size is blurred. `HtmlSurface` measures screen pixels per
  canvas unit (same projection as `VectorGraphic`), renders the panel at that integer scale
  (1..4, `panel.scale` so layout stays in canvas units), and recreates the texture on change.
  **Whole-number steps with hysteresis** — the first version quantised to halves and
  recreated 72 times in one short walk; **the new texture is shown one frame after creation**
  or the screen sees an unpainted texture for a frame (that was the flicker).
- *Static panels sleep.* `UIElementsRuntimeUtility.RepaintOffscreenPanels` repaints every
  offscreen panel every frame, dirty or not (read in the decompiled module, no dirty check).
  After a change the document stays enabled three frames, then is disabled: it detaches, the
  panel is disposed on the next `UpdateRuntimePanels`, and the texture keeps its contents.
  `Wake()` re-enables and re-attaches the content tree (disabling nulls the document root).
  Not yet measured, but the FPS drop first blamed on this was the day cycle: consoles off
  changed nothing.

**Capture tool now works for html consoles** (it never did for vector meshes). Three separate
causes, found one per restart by logging: (1) the capturer calls `RebuildSurfaceFromModel`
first, which recreates the host and therefore a fresh, never-painted panel; (2) it destroys
the old host with a deferred `Destroy`, so the old surface was still registered and matched
first; (3) a panel created that frame has a zero-sized root until `UpdateRuntimePanels` runs
`ApplyPanelSettings`. Fix: the clone (detected by `_panel == null`) finds its twin by
**texture reference** (Instantiate keeps it), calls `UpdateRuntimePanels` then
`RepaintOffscreenPanels` by reflection (the class is internal), reads the pixels into a
`Texture2D`, and shows that. The `UIDocument` lives outside the console hierarchy so it is
never cloned. **Sample pixels are logged** (`centre`, `corner`) so a blank capture says which
half failed.

**A bug the capture found before the user did:** moving content into its own element for the
sleep mechanism dropped the stretch on the document root, which then sized to its text
(436x53). The root is now `position: absolute; inset: 0`. The centre-pixel log line is what
exposed it.

**Aspect:** the ScriptedScreens canvas is 460x460 for every console size and physical panels
are not square; the stretch is ScriptedScreens', identical for labels and our texture.

**No hot reload.** LaunchPad's `ReloadMods` only rescans the list at the pre-load stage;
LaunchPadBooster is prefabs/save data/network. Mono cannot unload assemblies. Every DLL
change is a restart, so the design goal is that visual iteration happens in the `src` string
on the chip (pushed live with `StationeersLua/push.py`), and the DLL changes only when the
renderer gains a capability.

**Tooling:** full decompile of ScriptedScreens at `decompiled/ScriptedScreens/` (ilspycmd -p),
grep-able. `HtmlTest.lua` is the probe scene. Chip ref ids this world: 563 (1x1), 561 (2x2),
586 (3x3), 562 (visor). `python mcp.py capture_scripted_screen '{"ref_id":563}'` returns a
PNG path.

### Parser and live data — working, confirmed in game 2026-09-10

`HtmlParser.cs` (tag soup: void/self-closing tags, comments, entities, raw `<style>`/`<script>`)
and `CssParser.cs` (rules, selector lists, `tag .class #id` compounds and descendant chains,
specificity, comments, `!important` stripped, `@`-rules and pseudo-classes skipped with a
warning) are **Unity-free** and tested headless in `ScriptedScreensHtml.Tests` (`dotnet run`).
`StyleApplier.cs` maps the CSS subset onto `IStyle`; `HtmlRenderer.cs` builds the tree.

**Layout model, because UI Toolkit has no inline flow:** a block whose children are only text
and inline tags (`b i u s span small big sub sup mark code font br`) becomes one `Label` of
Unity rich text. Otherwise it is a container. An inline element **with an id** is kept as its
own Label so data can bind to it, and its parent becomes a wrapping row of labels; without
that, `<span id=x>` inside a sentence flattened away and `data` warned `matches no element id`.

**Cascade:** tag defaults (h1-h3, p, b, i, hr, button), stylesheet rules by specificity then
source order, inline `style`. Unsupported declarations warn once as `css: ... not supported`.

**Three render defects found by capture, one push each (no restart):**
- `overflow: hidden` + `border-radius` painted **solid white**. Rounded clipping goes through
  the **stencil buffer**; the RenderTexture had 0 depth bits. Now 24. Isolated with a
  six-variant page: radius alone fine, overflow alone fine, both = white.
- Sharp quality loss walking away: minification with no mipmaps plus the scale stepping down
  to 1x. Now mipmaps + trilinear and the scale **only ratchets up** (max 4). Memory only.
- "3 tanks" rendered "3tanks": `CollapseWhitespace` dropped leading spaces, so the run after
  an inline tag lost its separator. Leading/trailing whitespace is now a single space and
  whole labels are trimmed.

**Live data:** second `html` element with `page = "<page id>"` and `data = { id = value }`.
String/number → label text, table → CSS declarations on that element, bool → display. Because
elements persist, **CSS `transition` animates the change** — confirmed by the user: bars glide,
they do not jump. No per-frame Lua. Data wakes the panel for 60 frames; a transition longer
than ~1 s freezes until the next tick (ponytail note in `ApplyData`).

**Lua trap that errored all three consoles:** `string.format("%d", x)` with a float is a
runtime error in this Lua ("number has no integer representation"). `math.floor` first.
`python mcp.py get_chip_errors '{"ref_id":N}'` names the line.

**JS dropped by decision (2026-09-10).** Its only purpose was animation, and modern CSS does
that. No Jint, no dependency. `Result.Script` still collects `<script>` text but nothing runs it.

### CSS animation, inline SVG, gradients — working, confirmed in game 2026-09-10

**`@keyframes` + `animation`** without keyframe support in UI Toolkit: `KeyframeRunner`
(`Animation.cs`) steps at keyframe boundaries and sets `transition: all <segment span>` so
UI Toolkit interpolates the segment. A few style writes per keyframe, never per frame. Supports
duration, delay, easing, iteration count/infinite, reverse, alternate; fill is always forwards.
Uses `Time.time` so it pauses with the game. An animating panel stays awake; static still sleeps.
Two things that only showed in game:
- **Each iteration must snap to its start frame with no transition, then run the first
  segment on the NEXT update.** A from/to animation has one segment, so "same segment, skip"
  meant it fired once and stopped. Both writes in one frame would animate the snap.
- **`rotate(360deg)` is the same rotation as 0 and transitions to nothing.** `Angle()` shaves
  0.01 degrees off whole turns. The bob (translate) worked first time; the spin did not.

**`linear-gradient`** is baked into a 64-pixel ramp `Texture2D` set as `backgroundImage` with
`backgroundSize` 100%/100% (not `Cover`, which crops a 1xN texture). Angle snaps to the nearer
axis. `unityBackgroundScaleMode` is obsolete in this version.

**Inline `<svg>`** via `Painter2D` (present in the shipped module) in `SvgElement.cs`:
polyline, polygon, line, rect (rx), circle, ellipse (4 beziers), path M L H V C Q Z; fill,
stroke, widths, opacity, caps, joins; `viewBox` with uniform-centred default and
`preserveAspectRatio="none"`. Shapes are re-read on every repaint, so **data binding by shape
id** is `shape.Set(attr, value)`: a string sets `points`, a map sets any attributes, and a
**number array is spread evenly across the viewBox width as y values** — the live-graph case
is `hist = ys` from a Lua rolling window. Confirmed: scrolling CO2 history with filled area.

**Also added:** `>` child combinator, `!important` (ordered after everything else),
per-side `border-*-color` / `border-*-width` (the spinner was a uniform ring without them),
inline elements with an **id** kept as their own Label (a `<b>` inside a sentence is otherwise
flattened into rich text and cannot be animated or bound).

**Iteration loop that now works:** edit the page → `push.py` to the three chips → capture →
read. No restart unless the DLL changes. A restart is still ~2 minutes; batch DLL changes.

### Sharpness and fonts, settled (2026-09-10, late)

**Confirmed by the user at the screen: Barlow via UI Toolkit's own text engine, on a fixed
4096 texture, is crisp.** What it took, and what it cost to learn:

- **The texture is a fixed 4096 for every console, made once, never re-made.** Measuring
  the on-screen size and re-rendering in steps was built twice; both times each step was a
  visible change in clarity while walking, and the user chose stability. Mipmaps, aniso 16,
  mip bias -0.5 handle the far end. ~90 MB per console; memory is the price. The measuring
  code is deleted, not disabled.
- **The real blur bug was density**: every console reports a 460 canvas whatever its physical
  size, and an early build sized the texture from canvas units, so a 3x3 had a third of the
  1x1's pixel density. The fixed 4096 makes the size irrelevant.
- **`font-family` resolves font files first** (`FontLibrary.cs`: the Fonts mod's `Assets/fonts`
  and this mod's, family and style from the file name, `Barlow SemiBold` addressable),
  built with `FontAsset.CreateFontAsset(path, ...)` as dynamic SDF; registered TextMeshPro
  faces second, by mirroring atlas and tables into a TextCore `FontAsset`; no OS fonts (a
  legacy `Font` from the OS rendered nothing in UI Toolkit). Generic families are not mapped.
- **Design size: `<meta name="viewport" content="width=768">`.** Not a CSS transform (text
  rasterised at layout size then scaled, soft), not `ui:set_resolution` (the whole surface
  goes through ScriptedScreens' buffer at that size).

**The lesson, in the user's words: "you give up too fast".** The Barlow route was written
off after one test, and that test ran under the density bug, which blurred everything
regardless of font. Separate the variables before writing off a route: one change per
restart, and never judge sharpness from a capture or a scaled screenshot.

**Also learned:** UI Toolkit is the one text renderer in Unity that does not use TextMeshPro,
which is why the Fonts mod's registrations are invisible to it and the file route is the
right one. The verbatim mockup port (`GasUI-html2.lua`) needed only grid->flex, the viewport
meta and `var()` substitution in Lua; the CSS it rejects is the platform gap list: `gap`,
`font-variant-numeric`, `line-height`, `text-transform`, `text-decoration`. Its gauges are
empty tanks because the mockup drew them with JS on a canvas; that is the vector layer's job.


### The vector back-end (2026-09-10, evening) — WORKING, confirmed in game

**Why the texture route was abandoned, measured.** With the mockup's JS canvas gauges,
one console cost 70 → 17 fps on the main thread (Jint: 33 ms per frame for ~47k drawing
numbers, `JsBench.cs`). A worker thread and fewer ramp steps got it to a 4 fps drop per
console; the remainder was Painter2D tessellating 16 canvases per frame. The texture route
also had structural costs: a 4096 texture per console (~90 MB), blur at any other size, no
gradient fills, no TextMeshPro fonts. The user's call: "why render it as html, why not
translate to the vector instructions". Under an hour later it rendered.

**What it is now.** HTML/CSS front-end, vector back-end. The parser and cascade are
unchanged; UI Toolkit is used for **layout only** (no RawImage, scale 1, nothing is ever
shown from its texture); `VectorEmitter.cs` walks the laid-out tree and writes the vector
mod's scene text (`R` for boxes, `T` for text in the Fonts mod's faces, `G` for
transform/opacity, `CP` for overflow, `GL` for gradients, SVG shapes one-to-one);
`VectorBridge.cs` finds `ScriptedScreensVector.VectorElementPatch.Postfix` by reflection
and hands it a synthetic `vector` element with `scene`/`src` (structure) or
`scene`/`keep`/`data` (values). **The vector mod is untouched and must stay that way:
additions only, never a change to what it does on its own** (user's rule). No compile-time
link between the mods.

**Result.** The verbatim mockup renders through the vector mod, crisp at any distance
("look how crisp it is despite being rendered at about 1000px height"), Barlow via
TextMeshPro, and it costs nothing: 0.2 rebuilds/s when static, a few ms off-thread when
animating, fps unchanged. Everything the texture route fought for a day is free here.

**Gauges are SVG with expressions, no script.** Three extensions carry it, all in the
emitter: any SVG attribute may be `="expression"` (passed through; the vector mod's `t`,
`i`, `$data`, `hash`); `n="36"` on `polygon`/`polyline` is a sampled band/line (`YS`/`LS`,
`x y y2` per sample `i`); `n="42"` on `circle`/`rect`/`ellipse`/`path` is a repeat (`RP`);
`fo2 fea fea_edge lod dash dofs` pass through. `GasUI-html2.lua` builds the tank markup in
Lua from the vector GasUI's tuned constants (wave, fade, motes). An SVG gradient def is
scoped by prefixing its id with the svg's, since sixteen tanks each declare `#fill`.

**Data goes both ways.** The `data` payload still binds by id on the page (text, CSS,
display, shape attributes) and is **also forwarded flattened** to the vector scene as
`$a_b` (`co2 = { gasFill = .. }` is `$co2_gasFill`), through the data element's host with
`keep=1`. The vector mod eases scalars between ticks, so tanks move smoothly on 0.5 s data
with nothing re-emitted. Numbers, strings and number arrays as they are; bools as 0/1.

**Things found by the user's eyes, one restart each:**
- *Motes invisible.* `fill-opacity` as an expression was parsed as a number (0). `Mul` now
  passes expressions through and multiplies a plain `opacity` into them.
- *Black corners on the two-tone badge.* CSS runs a diagonal gradient corner to corner
  (line length `w|sin| + h|cos|`); the first version ran it 71% of the way, and the two
  corners past the ends fell outside every segment. A **hard stop** (two colours at one
  position) cannot be a vertex-colour gradient, so the box is split geometrically: one
  rect per segment, each clipped to its band of the gradient line (`CP { Y p=[...] }`,
  transformed into scene space through the element's transform), solid or its own ramp.
- *`triangulation stalled` every frame.* A bar growing from 0% with `border-radius: 4px`
  is self-intersecting below 8px tall. CSS clamps radii (scale all when adjacent radii
  exceed a side); the emitter now does the same.
- *"GA / S", the badge subscript on its own line.* TextMeshPro measures Barlow a little
  wider than UI Toolkit, and a shrink-wrapped label has no slack, so it wrapped. Labels
  get slack on the side their alignment allows (the rect only positions text). Also: a
  face that is already a named weight (`Barlow SemiBold`) is not bolded again, since TMP's
  synthetic bold widens every glyph.
- *Arrows as boxes.* Barlow has no ▼▲; the page asks `noto-punc` for that span.

**The 60,000-vertex cap.** With the camera against a 3x3 console (1866 px wide on screen),
the gas page reached exactly 59,999 vertices: a 6 px mote gets 24 segments plus a feather
ring, ~85 verts, times ~530 motes. Nothing visible was lost at that distance (checked tile
by tile), and the vector GasUI has the same exposure. Additive fixes if it ever bites:
32-bit mesh indices, or a gentler segment curve for small circles. Not done.

**Transitions and keyframes are compiled to expressions (`Tweens.cs`).** UI Toolkit's own
transitions are off in vector mode (`StyleApplier.VectorMode`), because the emitter
translates a snapshot and an interpolating layout would only ever show its end state.
Instead the surface keeps what the scene currently shows per element (rect, opacity,
rotate/translate/scale), diffs it after every change, and where the element's CSS names a
`transition` for that kind of property (or the keyframe runner has set the segment's
timing in `Tweens.Override`) starts a tween. The emitter writes tweened numbers as
`=from+(to-from)*ease(clamp((t-start)/dur,0,1))`: width/height on the box, a `G t=[..]`
translate group carrying the whole subtree from where it was, `G o=` and the transform
group's `t r s`. Both mods read `Time.time`. When the last tween ends the scene is
re-emitted once with plain numbers, so it goes static and costs nothing again. A keyframe
animation re-emits at keyframe boundaries (twice per iteration), never per frame. Colours
snap: a colour is not a scalar in the expression language. **Not yet seen in game** at the
time of writing; `HtmlTest.lua` (563: bars, spinner, bob) is the check.

**Support list** for authors: `ScriptedScreensHtml/SUPPORT.md` — what works from HTML5,
CSS and JS, what does not, and why, by pipeline stage. Two corrections from the user
worth keeping: images, video and sound are **ScriptedScreens' own elements** (`image`,
`media`, `sound`, URLs included), so `<img>`/`<video>`/`<audio>` map onto those through
the same postfix route, and the vector mod needs nothing for them.

**Additive wants on the vector side**, none blocking: `wrap=1` on `T` (paragraphs are
single-line today), `\"` escapes in quoted scene strings (the emitter substitutes `″`),
`lh` line height, the vertex cap above.

**Method notes.** The bridge worked first time because it reuses the vector mod's own
entry point with its own types (publicised `UiElement`), and because every earlier
session's finding about that mod (clips are scene-space, `units=bbox`, `fo2`, `Pair`
accepts expression strings) was recorded here and read before writing. The gauges were
written by copying the vector GasUI's tuned constants rather than the mockup's JS: here
the vector version *was* the tuned reference.


### Closing the web-platform gaps (2026-09-10 to 2026-09-14) — batches, one restart each

The user's real concern with `SUPPORT.md` was not the list lengths but the promise: "write
it as a web page" is only kept if a page written from browser habit works. Four batches,
each one build, each committed to `github.com/Gruffuss/scripted-screens-html` (private;
the vector mod stays in its own repo and is git-ignored here so two histories never fight).

**Layout.** `display: grid` (`GridLayout.cs`): the container stays a flex box and its
children are positioned absolutely from the tracks, recomputed on every geometry change of
container or child. `px`/`%`/`fr`/`auto`/`repeat()`/`minmax()`, `gap`, auto placement,
explicit lines including negative ones (`1 / -1`), auto rows measured from the children
(settles in two passes). Flex `gap` as margins. `var()` resolved in the cascade **and** in
the rich-text path — the second was missed first and every colour set through `innerHTML`
went white. Custom properties live on the `HtmlNode` so both paths share them. `calc()`,
`em/rem/vw/vh`. `:root`, `:first/last-child`, `:nth-child()`, `:not()`; state pseudo-classes
never match instead of dropping the rule (three parser tests updated). `z-index` as sibling
paint order. `display: flex` is a row, as in CSS — the flex port had added
`flex-direction: row` by hand everywhere. **Proof:** `GasUI-html3.lua`, the mockup's CSS
verbatim (grid, gap, `:root`, `var()`, `text-transform`), only fonts swapped and canvas
replaced by svg.

**Two data bugs the grid page exposed, both timing.** (1) The first full payload reached the
vector scene before the scene existed and was dropped, so every tank but O2 was empty; the
surface now keeps the merged payload and resends it after the first structure. (2) Same
shape for colours, see `var()` above.

**Paint.** `box-shadow` (the vector mod has a real geometric Gaussian shadow, `sh`, blur
included; `inset` skipped; a transparent box with a shadow gets an invisible fill to carry
it), `radial-gradient` backgrounds (`GR units=bbox`, size keywords approximated by radius),
`border-style: dashed/dotted` (`dash`, round caps for dots), `text-decoration` as rich-text
`<u>`/`<s>`, `text-transform` outside tags, `text-shadow` once the vector mod grew `sh` on
`T` (0.10.2.0, TextMeshPro underlay: one per label).

**Elements.** `<img>`, `<video>`, `<audio>` are ScriptedScreens' own `image`/`media`/`sound`
elements, applied through the real `ApplyElementInternal` with the id `page/imgN` and the
design box scaled to the host's px. **Correction (2026-09-15):** a hierarchical id does
*not* parent an element under the page host; ScriptedScreens only does that for scrollview
parents (or `parent_id`), so the host sits under the surface root beside the page. And
after every batch and rebuild it re-sorts hosts by `z_index`, moving each **model** element
to the last sibling, so an element that is merely applied sinks under the page and is never
seen (the "image loaded for a second" report). The element is therefore written into the
surface model (`SurfaceState.Elements`) with `z_index` one above the page's, and removed
from it with `RemoveElement` when the box is gone. Local model only: a remote client is not
sent it. The ScriptedScreens guide (MCP `search_docs` scope `ss`) documents `parent_id`
and `z_index`; read it before the decompile.
`<button>` (or `onclick`/`data-click`) is a vector click region: the background `R`
carries `id` + `click=1` and the click arrives at the page element's Lua `on_click` with
the button id. Page JS does not see clicks by design; Lua bounces what it wants via `data`.

**DOM creation.** `document.createElement` builds a detached shim on the worker; on
`appendChild` to a live element it is serialised to HTML, parsed and cascaded on the main
thread like page markup (`HtmlRenderer.AppendFragment`), gets an id if it had none, and
forwards its writes by id from then on. `remove()`/`removeChild` drop the element, its node
and every id under it. `classList` is real now. Trap worth keeping: **the JS prelude is a C#
verbatim string, so every `"` inside it must be `""`** — a tool that unescapes `\"` on the
way into a shell heredoc turned that into a two-round build failure; the fix was a script
file, not a heredoc.

**Non-uniform svg fits.** `preserveAspectRatio="none"` on a 100x60 viewBox in a wide box
stretched the graph's stroke with the box (thin on the flat, thick on the steep, as a
browser would). When the two scales differ the emitter now bakes the scale into every
coordinate and keeps the stroke width in scene units; `path` keeps a scaled group of its
own since `d` cannot be rewritten.

**Small things that each cost a restart:** the scene reader types a quoted `"3"` as a
number, so a purely numeric label vanished (`<noparse>` guard); a spinner's differently
coloured top side needs per-side arcs (the width check alone drew one blue ring); the
vector `t` restarts on every scene apply, so tween start times must be relative to the
emission and a scene with live tweens must be resent even when its text is unchanged.

**Test pages:** `HtmlTest.lua` (563, motion), `HtmlTest2.lua` (561: grid, paint, units,
`<img>`, a counting `<button>`, script-made pills), `GasUI-html3.lua` (586, the mockup).


### Capture, diagnostics, clicks (2026-09-14)

**Captures show HTML pages now**, through the vector mod (its 0.10.2.x clone carries the
mesh and builds inline). What the HTML side needed: emit the first structure
**synchronously at build**, because a capture rebuilds the surface and clones it inside one
call, and a scene handed over in the next `Update` is too late. The texture-era capture path
(RawImage snapshot, twin-finding, `TryCaptureSurfaceShared` patch) was dead in vector mode
and is deleted. The vector session's cross-mod analysis was right that the two paths could
not share code; the resolution is that the HTML mod no longer has one. Worth keeping from
that analysis: `Harmony.PatchAll(Type)` registers one class and silently skips nested patch
classes; this mod uses `PatchAll(Assembly)`.

**Diagnostics config** (`HtmlConfig.cs`), the vector mod's shape: `Diagnostics.Enabled`
(per-page line every second: emits/s, layout + translate ms, nodes, KB, tweens, script
ms/frame, externals; also the informational lines) and `Diagnostics.DumpScenes`
(`scenes/<page>.txt` beside the DLL, the emitter's exact output). Both bind into
`ModBehaviour.Config`, read live, off by default. The dump answered the click question in
one look: `R ... id=bump click=1` was there.

**Two click findings.** (1) The 2x2 test page hit the 60,000-vertex cap with 48 shapes: one
90x50 `radial-gradient` box costs ~50,000 vertices (rings of the whole outline, one per 2.5
screen px), a blurred `box-shadow` ~13,000, the rest of the page 4,000; the button, late in
document order, was dropped with its hit region. Measured by ablation (four pushes, no
restart). The vector mod's own gradient demo costs 16,000 on a 1x1. A ring cap belongs on
the vector side; the page uses a solid until then. (2) With the button back, clicks reached
Lua (chip log) but the count stayed at zero: a page with a script data handler skipped the
id-binding entirely, a rule from the texture days. Ids now always bind and the script gets
the event afterwards; "matches no element id" is silent when a script is present.

**Image element.** `<img>` through ScriptedScreens' `image` element works (raw GitHub URL;
Wikimedia answers 400 to Unity's request). It was re-downloaded on every emit because the
element was re-applied unconditionally; now re-applied only when rect or attributes change.
A surface rebuild (a click on the console triggers one) recreates the host and the image
with it.

**Seen in the capture, a text-layer limit — since fixed on the vector side (0.11.11.0).** All
text painted above all geometry, so a label from a low `z-index` box showed over a higher box.
The vector mod now takes **`ztext = 1`** on the scene (or `SCENE ztext=1` in the `src` form,
which needed no renderer change — `SCENE` already copies every prop through). Labels then obey
scene order: a `T` forces a mesh cut where a later shape's bounds actually overlap its box, and
the label's mask is parented between the two slices.

**On by default since 0.11.12.0** (`ztext = 0` opts out). It shipped opt-in and was flipped
the same day once measured: bounds tracking is ~0.19 ms per 40,000 vertices (.NET 8), on the
worker. The emitter needs to do nothing for `z-index` to work; SUPPORT.md's limitation note is
now simply wrong. The vector mod is a standalone mod and its docs describe it on its own terms,
not as the HTML mod's back end.

Cost is one extra mesh, so one draw call, **per label a later shape actually covers** — not per
label. That distinction is the whole design: pinned by `TextOrderTests`, where thirty labelled
tiles stay in one mesh and the naive "cut at every label" version turns them into thirty. One
case it cannot serve: a label declared before any shape, since the surface's own renderer always
draws before its children; it stays on top rather than vanishing.

**Open, reported 2026-09-14 (not yet investigated):** on `HtmlTest2.lua` the **first click on
the counter takes a long time to register; later clicks are fast.** Candidates, in the
order to test: the panel wake after sleep (the document is disabled after three idle frames
and the first write re-enables, re-attaches and lays out from cold); the first JS `data`
event on the worker (engine warm-up); ScriptedScreens' own 0.25 s click debounce would not
explain "long". Measure with `Diagnostics.Enabled`: the per-page line shows layout and
translate ms per emit, and the chip log timestamps the click.

**Verified 2026-09-14 evening, vector mod 0.11.12.0:** text draw order works end to end for
pages (capture: the "z 1" label hides under the z-3 box); nothing to do in the emitter.
Re-measured the radial box: 53,482 vertices with it, 4,786 without, so ~49,000 for one 90x50
rounded box with an off-centre focus; the vector-side radial change did not reach this case.
**Error spam on capture, vector side:** 66 x Unity "Trying to add VectorSlice for graphic
rebuild while we are already inside a graphic rebuild loop", starting right after "vector
capture: built inline ... across 7 mesh(es)". The capture-time `BuildNow()` runs inside
`UpdateGeometry` (the canvas rebuild loop) and now calls `ApplySlices()`, which creates
`VectorSlice` graphics there; one mesh never needed a slice, draw-order text does. Fix on the
vector side: defer slice creation out of the rebuild loop when building inline.

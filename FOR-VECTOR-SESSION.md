# Notes for the vector mod session, from the HTML mod (2026-09-14, vector mod 0.11.12.0)

**Both fixed by 0.11.20.0, confirmed from the HTML side on 2026-09-15:** the 2x2 test page
with its radial box is 11,403 vertices (was 53,482) and three captures logged no
`VectorSlice` error. Kept for the record.

Two findings, both measured, both reproducible without the HTML mod's help. Nothing here
asks for a behaviour change to existing scenes; both are additive fixes.

## 1. Radial fill vertex count

A `GR units=bbox` fill on one 90x50 rounded rect (`rx=8`) with an off-centre focus costs
about **49,000 vertices** at a 2x2 console's on-screen size (1,010 px wide):

```
GR id=rad1 units=bbox cx=0.3 cy=0.3 r=0.71 stops=[[0,#7DD3FC],[0.7,#0369A1]]
R x=10 y=243 w=90 h=50 rx=8 f=@rad1
```

| page | vertices |
|---|---|
| with the radial box | 53,482 |
| without it | 4,786 |

The whole rest of that page, 47 shapes including a blurred shadow, is 4,786. Your own
`GradientDemo.lua` costs 16,442 on a 1x1. The ring count follows on-screen radius with no
cap from the shape's size, so a small box at close range builds rings finer than a pixel.
The commit "Radial fills cost about half the mesh, and a small many-stop one far less" did
not reach this case; re-measured after it. Suggested: cap rings by the shape's smaller
dimension in screen pixels, since a 50-unit box cannot show more distinct rings than half
its height in pixels. Where it bites: at 60,000 the mesh drops whatever comes last in the
scene, silently, and on a page that was the click button.

## 2. Unity error spam on screen capture

After `capture_scripted_screen` on any scene whose text forces mesh cuts, the log gets 66
lines of:

```
[Error : Unity Log] Trying to add VectorSlice (ScriptedScreensVector.VectorSlice) for graphic
rebuild while we are already inside a graphic rebuild loop. This is not supported.
```

They start immediately after `vector capture: "html:page" built inline, 4258 verts across
7 mesh(es)`. The capture-time `BuildNow()` runs from `UpdateGeometry`, which Unity calls
inside its canvas rebuild loop, and it now calls `ApplySlices()`, which creates `VectorSlice`
graphics there. Before draw-order text a scene this size was one mesh and nothing was
created in that path; now every text cut is a slice. Suggested: when building inline, defer
slice creation to the next `Update`, or pre-create the slices the previous build needed.

Repro without the HTML mod: push `examples/14-ztext.lua` (or any scene with labels over
shapes), run a capture, read the log.

## Not a vector issue, for the record

The test page's gradient box being plain blue is the HTML side's doing: the page carries a
solid until item 1 lands, so its button stays under the cap.

---

# Additive work agreed with the HTML side (2026-09-15)

The HTML emitter will write these forms and treat them as present. Feasibility was checked
read-only against the vector code and the game's shipped shaders (details per item).

1. **Text-form gradient sample.** `fat==expr` / `sat==expr` in the scene text, honoured when
   `f`/`s` is `@gradient`, on shapes and on `T`. `SceneText` turns them into the map form
   `{ grad, at }` that `SceneModel` already parses (SceneModel.cs ~1043); `T` resolves paint
   the way shapes do.

2. **Inset shadow on text.** TMP's underlay has an inner variant, `UNDERLAY_INNER`, and the
   shipped `TextMeshPro/Distance Field` shaders (and Mobile ones) compile it alongside
   `UNDERLAY_ON`: verified in `rocketstation_Data/resources.assets`. Same properties
   (`_UnderlayColor/OffsetX/OffsetY/Dilate/Softness`), same `TextShadow.Fit` maths. Change:
   `TextLayer.WriteUnderlay` (~line 314) enables `"UNDERLAY_INNER"` instead (no
   `ShaderUtilities` constant exists for it; probe `shader.keywordSpace` as the existing guard
   at ~245 does) and the offset-copy caster path (~274-282, `TextShadow.ShouldCast`) is
   skipped for inset. Trivial to small.

3. **Inset shadow on shapes.** `Shadow.Emit` with the shape's own outline as the clip
   (`ClipRegion.FromPolygon`, rounded corners come as arc polygons and stay convex). Invert
   three things: `Offset(...)` grows outward (sign and spread meaning flip), the solid core is
   an interior fill (becomes a full-alpha ring next to the edge, `EmitRing` unchanged), and
   the alpha ramp `Coverage(d, sigma)` becomes `1 - Coverage`. Run after `FillContour` and
   before `StrokeOutline` (Tessellator ~786-793). Refuse on concave `P`/`SP` with a
   `scene.Problem`. Parsing: a sixth `inset` field in `sh` (SceneModel ~1383). Small to medium.

4. **More than one text shadow on `T`.** One underlay per label is enforced at emit
   (Tessellator ~654, "text takes one shadow"); a second entry needs a second label behind,
   offset and coloured, the way the caster copy already works. Small.

5. **Justified text.** `T align=justified` -> TMP Justified. Trivial.

6. **Rounded text masks.** Today `RectMask2D` on the clip's bounding box, per-label parent
   object (TextLayer ~479-496). Realistic route: a UGUI `Mask` with a small `MaskableGraphic`
   drawing the convex clip polygon as a fan on that parent, keeping `RectMask2D` for the
   axis-aligned case; the shipped UI/TMP shaders carry `_Stencil*`. ~2 draw calls per masked
   label. Cutting TMP glyph quads was judged large and fragile (the mod never touches TMP
   meshes; sub-meshes and the caster copy would all need it), and TMP's texture masking
   variants (`MASK_SOFT/HARD/TEX`) are not compiled in the shipped shaders. Medium.

7. **Forced scroll offset.** `so=<offset> sov=<version>` on `SC`: a changed version applies
   the offset once, then wheel/drag own it again. Unlocks `scrollTop =` and
   `scrollIntoView()` from a page.

Not needed from the vector side after all: hover and click coordinates; the HTML side takes
them from its own layout boxes and the pointer position.

# Requirements for the vector mod, from the HTML layer (2026-09-15)

**Status 2026-09-15: all thirteen implemented on the vector side as 0.11.21.0, tested offline
(171 unit checks, offline renders, no regressions on five old scenes), not yet seen in game.**
The build deploys itself, so the next game start loads 0.11.21. The HTML emitter's forms were
checked against the 0.11.21 reference and parser and match. Pending in-game confirmation, per
the vector session: inset text shadow (3), stencil masks on labels (6, 8), IMG download and
rebuild (9), filters and masks on text (10, 12), extra text shadow copies (4), so/sov live (7).

Two behaviours to know: an inset shadow costs about 3x the vertices of an outset one; a
concave clip draws its contents once per convex piece. Filters do not reach IMG (reported).
A nested clip with an empty intersection now draws nothing, as CSS does.

All additive: nothing existing changes meaning. Where a feasibility check was done against
the vector code and the game's shipped shaders, the findings are inline.

Resolved and removed from this file: the radial fill vertex count and the `VectorSlice`
error spam on capture, both fixed in 0.11.20.0 and confirmed from the HTML side.

## 1. Text-form gradient sample

`fat==expr` and `sat==expr` in the scene text, honoured when `f` / `s` is `@gradient`, on
shapes and on `T`. `SceneText` turns them into the map form `{ grad, at }` that `SceneModel`
already parses (~line 1043); `T` resolves paint the way shapes do. What the emitter writes for
a colour transition:

```
DEFS { GL id=tw7 stops=[[0,#B5352C],[1,#2E8B6E]] }
R x=10 y=10 w=80 h=20 f=@tw7 fat==clamp((t-1.25)/0.3,0,1)
T x=12 y=12 w=76 h=16 text="ok" f=@tw7 fat==clamp((t-1.25)/0.3,0,1)
```

A keyframe animation with several colours is a multi-stop ramp sampled over the iteration.

## 2. Inset shadow on shapes

`sh` entries take a sixth field, `inset` (or `1`), e.g. `sh=[[0,2,6,0,#000000,inset]]`.
Ring and feather drawn inward from the outline and clipped to the shape's own outline via
`ClipRegion.FromPolygon` (rounded corners arrive as arc polygons, still convex). Invert
three things in `Shadow.Emit`: the offset sign and spread meaning, the solid core (an
interior fill today; becomes a full-alpha ring next to the edge, `EmitRing` unchanged), and
the alpha ramp `Coverage(d, sigma)` → `1 - Coverage`. Run after `FillContour`, before
`StrokeOutline` (Tessellator ~786-793). Refuse on concave `P`/`SP` with a `scene.Problem`.

## 3. Inset shadow on text

TMP's underlay has an inner variant, keyword `UNDERLAY_INNER`, compiled into the shipped
`TextMeshPro/Distance Field` and Mobile shaders alongside `UNDERLAY_ON` (verified in
`rocketstation_Data/resources.assets`). Same properties (`_UnderlayColor/OffsetX/OffsetY/
Dilate/Softness`), same `TextShadow.Fit` maths. `TextLayer.WriteUnderlay` (~314) enables
`"UNDERLAY_INNER"` instead for an inset entry (no `ShaderUtilities` constant exists; probe
`shader.keywordSpace` as the guard at ~245 does); skip the offset-copy caster path
(~274-282, `TextShadow.ShouldCast`) for inset.

## 4. More than one text shadow

`sh` on `T` with several entries (today `Tessellator` ~654 reports "text takes one shadow").
Each extra entry is a second label behind the first, offset and coloured, the way the caster
copy already works.

## 5. Justified text

`T align=justified` → TMP `Justified`.

## 6. Rounded text masks

Today a `RectMask2D` on the clip's bounding box, on the per-label parent (`TextLayer`
~479-496). Route: a UGUI `Mask` with a small `MaskableGraphic` drawing the convex clip
polygon as a fan on that parent, keeping `RectMask2D` for the axis-aligned case. The shipped
UI/TMP shaders carry `_Stencil*`. About two draw calls per masked label. Not: cutting TMP
glyph quads (the mod never touches TMP meshes; sub-meshes and the caster copy would all need
it), and not TMP's texture masking (`MASK_SOFT/HARD/TEX` are not compiled in).

## 7. Forced scroll offset

`SC ... so=<offset> sov=<version>`: a changed `sov` applies `so` once, then wheel and drag own
the offset again. The HTML side writes it when a page script sets `scrollTop` or calls
`scrollIntoView()`, bumping the version.

## 8. Concave clips

A `CP` whose polygon is not convex (today refused with a problem) puts its clipped subtree
into a slice child, as the text-order slices do, under a UGUI `Mask` whose graphic is the
clip polygon triangulated by the existing ear clipper. Labels in the subtree parent under the
same mask. One draw call per such group. The geometric clipper keeps the convex case. Same
stencil mechanism as 6.

## 9. `IMG` node

```
IMG x=10 y=10 w=120 h=72 src="https://.../thumb.png" fit=cover rx=6 o=1
```

A textured quad drawn as its own slice with a material carrying the texture (the default UI
material with `_MainTex`). `fit`: `fill` stretches, `contain` letterboxes, `cover` crops, all
from the texture's natural size. `rx` (the per-corner form of `R` too) and convex clips cut
the quad polygon with interpolated UVs; concave clips through 8. Sits in scene order and
scrolls inside `SC`. Texture via `UnityWebRequest.Get` on the URL, or ScriptedScreens'
`ImageElementController` cache by reflection (it holds a `Texture2D` per URL). Until the
texture arrives, nothing is drawn. This carries `<img>`, `background-image`, image radii,
`object-fit`, images inside scroll and clip groups, and image order against text.

## 10. Gradient mask on a group

`G mask=@gradient { ... }`: every vertex's alpha under the group multiplied by the gradient's
alpha sampled at that vertex, with the fill subdivision so the ramp is smooth; labels take
the mask's alpha per glyph through TMP vertex colours. The emitter writes it for
`mask-image: linear-gradient(...)` and for scroll-edge fades.

## 11. Conic gradient

`GC id=.. cx= cy= a= stops=[[..]]` in defs (`units=bbox` like the others); the parameter is
the vertex's angle around the centre starting at `a` degrees, clockwise; refine like radial.

## 12. Colour filters on a group

`G bri=1.2 con=1 sat=0 hue=90 gray=1 sep=0 inv=0 { ... }` with CSS `filter()` semantics
(brightness, contrast, saturate, hue-rotate in degrees, grayscale, sepia, invert), applied to
every vertex colour and label colour under the group at emit. Only the ones present apply.

## 13. Matrix on a group

`G m=[a,b,c,d,e,f] { ... }`: a 2x3 affine matrix composed after `t r s` (CSS `matrix()`
order). Makes `skew()` and `matrix()` exact.

## 14. Proposed, not agreed: gradient text (`background-clip: text`)

A gradient through the glyphs needs the text drawn with a gradient over its quads. TMP allows
per-vertex colour on each glyph's four corners, so a linear ramp across a label is exact per
glyph and continuous across the label. Form: `T ... f=@gradient` where the gradient is
linear with `units=bbox` over the label box. Radial would be the per-glyph approximation.
Say yes or no.

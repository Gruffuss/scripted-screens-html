# Requirements for the vector mod, from the HTML layer (2026-09-15)

**Status 2026-09-15: all thirteen implemented on the vector side as 0.11.21.0, tested offline
(171 unit checks, offline renders, no regressions on five old scenes), not yet seen in game.**
The build deploys itself, so the next game start loads 0.11.21. The HTML emitter's forms were
checked against the 0.11.21 reference and parser and match. Pending in-game confirmation, per
the vector session: inset text shadow (3), stencil masks on labels (6, 8), IMG download and
rebuild (9), filters and masks on text (10, 12), extra text shadow copies (4), so/sov live (7).

**Items 14-16 (2026-09-15, same 0.11.21.0 build): implemented, tested offline, not seen in game.**
Item 3 changed route after being seen in game: `UNDERLAY_INNER` is declared by the shipped
TMP shaders but draws nothing (four arrangements tried, keyword confirmed on in all). Inset text
is now a stencil mask of the glyphs, a shadow-coloured copy inside it and the face moved by the
offset on top; confirmed drawing in game, offset not capped by padding.
14 is the face only: `s=@gradient` on `T` is not done, because text has no outline in the vector
mod at all and TMP's outline colour is a material property, not per vertex. 15 as specified
(`fl` keys `f size weight font`, values with spaces quoted '...'). 16 reads `v` from the top.

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

## 14. Gradient text (agreed)

`T ... f=@gradient` (and `s=@gradient` for an outline): the gradient sampled at each glyph's
four vertex corners, `units=bbox` over the label box, so a linear ramp is exact per glyph and
continuous across the label; radial and conic per-glyph. Same per-vertex recolour path as
filters and masks. Carries `background-clip: text` with a gradient background.

## 15. First-line information (agreed)

`::first-line` needs to know where TMP broke the first line. Additive form:
`T ... fl="<attrs>"` where attrs is a subset of `f`, `size`, `weight`, `font` for the first
line only. The text layer, after the mesh update, reads `textInfo.lineInfo[0]`'s last
character index and re-sets the text with rich tags around that span (a text re-set, not a
mesh edit). If the layout moves the break (a later size change), it repeats once.

## 16. Source crop on `IMG` (agreed)

`IMG ... uv=[u0,v0,u1,v1]`, fractions of the texture, the part of the picture the box shows
(default `[0,0,1,1]`). Carries the nine-slice `border-image` (nine `IMG` nodes) and canvas
`drawImage` with a source rectangle.

## 17. Holes in a clipped fill (seen in game 2026-09-15)

`clipped fills cannot carry holes; holes ignored` fires for every inline svg: the HTML side
wraps each `<svg>` in `G clip=<its box>` (a browser clips svg content to its viewport by
default), so a path with a hole (`fill-rule: evenodd`, or two same-wound subpaths under
`nonzero`) inside an svg draws solid. Additive ask: when every hole lies inside the clip
region, keep the holes (clip the outer contour, bridge the holes as unclipped); only a hole
that straddles the clip boundary needs the boolean subtraction that is not there.

**Status (vector side, 0.11.23, seen in game 2026-09-15):** done, and further than asked. A hole
inside the clip is bridged as unclipped. A hole crossing the clip, or crossing the cut between a
concave clip's convex pieces, no longer needs subtraction: the unclipped shape is triangulated and
each triangle clipped (exact, no cracks). No warning remains. Test: `InGameTest-holes.lua`.

## 18. Report a scroll container's offset to the host (asked 2026-09-16)

A page script reacts to scrolling (`scroll` events, `scrollTop` reads: the Apple-style Atmo
console fades the edges of its lists only where there is more content in that direction). The
`SC` container scrolls on the client and the host never learns the offset: `sy` exists only
inside expressions. Additive ask: when an `SC` (or scroll view) offset changes, tell the host
element (a callback on the `vector` element with the scene id, the `SC` node id and the
offset, throttled to a few per second; or a readable prop). The HTML side then raises
`scroll` on the page element and answers `scrollTop` with the real value.

Please notify me (session "Vector drawing for scripted screens") when it is done or if you
have questions.

**Status (vector side, 0.11.24, commit c5a35f7, 2026-09-16): done.** `VectorGraphic.ScrollChanged`
(host, scene id, offset, max, view) and `TryGetScroll`; the HTML side subscribes by reflection,
answers `scrollTop`/`scrollHeight` from it and raises `scroll`. Wheel scrolling in game still
waits for the user's hand.

## 19. A data payload that snaps (asked 2026-09-16)

The HTML mod is being changed to send a page's structure once and every changing value as
`keep = 1` data (step 1 of ScriptedScreensHtml/REDESIGN.md), the way a Lua console does. Today
every scalar in a payload eases from its previous value (`ApplyData` fills `Previous` for all
scalars). For a page that is wrong: a browser moves a box only when CSS says to transition, and
the constants inside a running tween expression (its start time and endpoints) must never glide.

Additive ask: a payload prop `snap = 1` makes that payload's scalars apply without easing (their
`Previous` entries are dropped or set to the new value), while other names keep easing as now.
Without the prop, behaviour is unchanged. A Lua console never sends it, so nothing changes for
existing scenes.

Please notify me (session "Vector drawing for scripted screens") when it is done or if you
have questions.

## 20. Values inside gradient defs (asked 2026-09-21)

The HTML mod compiles a page once and writes every moving value into a named slot, so
anything a running page can change needs somewhere to live in the compiled scene. A gradient
is resolved at parse time, so a `$slot` in a stop was dropped and `x1/x2/cx/r` were numbers
only. Asked for: `$name` and expressions in stop positions, stop colours and `GL`/`GR`
geometry, re-resolved per tick the way `f=$name` already is. Geometry alone would carry the
common case (AtmoDark's `stops=[[0,C],[0.64,transparent]]` is fixed stops plus a slotted
`x2`).

**Status (vector side, 0.11.31, 2026-09-21): done, both halves, tested offline, NOT yet seen
in game.** `x1 y1 x2 y2`, `cx cy r fx fy`, `a`, stop positions and `$name` stop colours all
accept expressions and are re-read once per rebuild, before the walk. A focus that was never
stated follows a live centre. Live stop positions that cross between ticks are re-sorted with
their slots. A gradient of plain numbers takes the old path and costs nothing per rebuild.

Found while testing, and it was the AtmoDark case exactly: **a two-stop ramp whose stops did
not span the shape kept ramping past its last stop** instead of holding that colour, so a
fade to transparent never finished fading. Fixed (it now cuts at the stop lines, as a
multi-stop ramp does). It costs geometry where it applies: a rounded box went 40 -> 134
vertices. Ramps that span 0..1 are untouched.

## 21. Values in clip geometry (asked 2026-09-21)

`CP id=clipN { R x=... w=... }` had the same problem, and it is the one that matters for real
layouts: any `overflow: hidden` box whose size is data-driven forced a full structure re-emit.

**Status (vector side, 0.11.31, 2026-09-21): done, tested offline, NOT yet seen in game.** A
clip shape written with expressions keeps its node and is re-cut once per rebuild. One
decision worth knowing: **a live clip that evaluates to nothing hides its group** rather than
falling back to unclipped, so a bar at 0% disappears instead of flooding its track. A clip of
plain numbers is still cut once at parse. Measured: 16 clipped bars, static 0.084 ms per
rebuild against live 0.085 ms.

## 22. Group-level early-out (asked 2026-09-21)

`G o=0` descended and skipped each child individually; asked for an early return so a hidden
subtree is free, because the compile-once model emits every reachable state and gates the
inactive ones with opacity.

**Status (vector side, 0.11.31, 2026-09-21): done, tested offline, NOT yet seen in game.**
One exception, deliberately: an `o = 0` subtree containing a click region, a scroll container
or an `IMG` is still walked, because a browser sends clicks to an `opacity: 0` element and a
picture that never downloads would never appear when shown. Everything else stops at the
group.

**And the switch that turns that exception off, asked the same day: `G v=0`.** CSS
`visibility: hidden` — the subtree is not there at all, hit regions and scroll containers
with it. It is an expression, so one scene carries every state and shows one:
`G v=$night { ... }` beside `G v="=1-$night" { ... }`. `o` fades something that stays live;
`v` switches between states that must not overlap.

---

## Capture comes back near-blank on a PORTRAIT surface (2026-09-21)

Not new, but newly narrowed: it correlates with orientation, it is deterministic, and the mesh is
demonstrably there.

| console | size | capture |
|---|---|---|
| 2x1 wide | 1036 x 460 | **correct** - full page, every element |
| 2x1 tall | 460 x 1036 | near-white with one thin black smear, identical across three captures |
| 3x2 tall | 460 x 714 | same |

**The clone built real geometry for the failing ones.** From the log, taken during those captures:

```
vector capture: "html:run" built inline, 6008 verts across 1 mesh(es)   <- the wide one, correct
vector capture: "html:run" built inline, 4264 verts across 1 mesh(es)   <- a tall one, near-blank
```

4,264 verts is a full page, so `BuildNow` and the inline path are both working. Every surface also
reports 137-141 shapes in its ordinary stats line, so nothing is failing to draw either. The fault
is therefore **downstream of the mesh** - what the capture composites, not what the renderer built.
A thin black smear across the middle is the shape you would expect from a camera or rect sized for
a landscape surface being pointed at a portrait one.

Nothing in the HTML mod distinguishes the two: same page, same code path, only the console's shape
differs. Worth checking the capture clone's camera rect / orthographic size against the surface's
aspect rather than against a square or a fixed landscape assumption.

Confirmed by the user as "a screenshot bug", i.e. the consoles themselves look right in game.

**The HTML side is ruled out by measurement, not by argument.** The obvious suspect was the layout:
`LayoutSize()` derives the aspect from the world transform of the rect corners, and a surface built
inside a capture's own call could plausibly have had no valid transform yet - which would lay the
page out square and wreck a portrait console while barely touching a landscape one. So it was
instrumented and measured during the captures themselves:

```
html: ".../main/run" laying out 1035.7x460 (aspect 0.444), rect 1035.7x460   <- the wide one
html: ".../main/run" laying out 460x1035.7 (aspect 2.251), rect 460x1035.7   <- the tall one
```

The clone measures the tall console **correctly**. The page is laid out for the right shape, the
renderer builds 4,264 verts of it inline, and the image is still near-white. So the fault is
downstream of both the layout and the mesh - in what the capture presents or composites.

One more observation worth having: the image has a **white** background with black marks on it,
while the page's own background is near-black (`#0B1020`). Whatever is being composited is not the
page's background rect, so this looks less like "the page drew wrong" and more like "most of the
mesh is not in the captured image at all".

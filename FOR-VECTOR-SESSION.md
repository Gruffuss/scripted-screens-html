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

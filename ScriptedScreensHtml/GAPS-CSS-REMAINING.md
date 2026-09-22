# The CSS gaps that are left, by name

CSS is at **338 of 342 properties accounted for (98.8%)**, 88 of 88 selectors, 34 of 34 at-rules,
98 of 106 values. This file names what remains so it never has to be audited again.

Regenerate the numbers with:

```bash
cd ScriptedScreensHtml.Tests && dotnet run -c Release -- --csslanguage
```

---

## Properties: 4 left

### `container-type` / `container-name`

Being worked on. `@container` discarded the container's name and answered every query against the
page's design width, so `@container sidebar (...)` and `@container main (...)` were the same query
and a 200px panel took a 900px branch. These two properties exist only to feed that, and nothing
read them.

### `object-position`

**Not implementable here, and the reason is specific.** The picture becomes the scene's `IMG` node,
whose crop is a `uv` rectangle in *source* coordinates. Turning `object-position` into a uv crop
needs the image's intrinsic width and height, and the emitter never learns them — it places the node
and the renderer loads the file later. Same reason `border-image-slice` in pixels is read as thirds
and reported rather than being exact.

Would need the vector side to accept a position in *destination* fractions, which is an addition to
a separate mod rather than a change here.

### `animation-composition: add`

**Attempted 2026-09-22 and reverted, because it could not be shown to work.** Recording the evidence
so the next attempt starts three steps along:

- The parsing works. A `case "animation-composition"` in `HtmlRenderer.ApplyAnimationDeclaration`
  reaches an `AnimationSpec.Add` flag — measured, `spec.Add=True`.
- The base snapshot works. `add` composes the frame with the element's own value, and the record is
  the same dictionary the frames write into, so it has to be copied in the `KeyframeRunner`
  constructor — measured, `base=9 hasTransform=True`.
- The composition works. `Composed()` turned the frame's `translateX(0)` against a base of
  `translateX(20px)` into `translateX(20px) translateX(0)` — measured, printed.
- **And the emitted scene was still `t=[0,0]`, identical to the replace-mode baseline.** So the
  composed value is written into the record and something downstream does not read it, or reads a
  different copy. That is where the next attempt should start: dump the record for the element
  immediately before `VectorEmitter.Emit` and see whether the composed string is in it.

Worth knowing before spending on it: nothing in the corpus of 91 pages uses the property, and its
browser support is recent. It was reverted rather than left in because unverified code that looks
wired up is this project's characteristic bug, and shipping one more of those costs more than the
feature is worth.

---

## Values and functions: 8 left

| value | why |
|---|---|
| `attr()` as a length | Reported now rather than silently drawing 0. `attr()` outside `content` is a 2025 feature with one implementation; inside `content` it works. |
| `image-set()` | Picks a source by resolution, and a console has one. |
| `lh` / `rlh` | The line box is 1.2em by construction here, so these parse to a plausible but not exact number. |
| `path()` in `offset-path` | Implemented; the row draws something else. Unclassified. |
| `repeating-linear-gradient()` | Draws one non-repeating gradient. Known since the repeating-stop bug (BUGS #62). |
| `url()` | Row draws something else. Unclassified. |
| `s` / `ms` | The probe flags its own row as bad: the reference draws nothing either. |

---

## Selectors and at-rules: none

Both report "nothing missing". Seven selectors and two at-rules are named as deliberate:
`:visited`, `:target`, `::first-line`, `::selection`, `::file-selector-button`, the column
combinator, `::placeholder` (implemented, but handed to ScriptedScreens' own field control so it
never enters the scene), `@import` and `@page`.

---

## Fifty-two properties that are not gaps

Listed with a reason each in `CssLanguage.Deliberate`. They divide into: print and fragmentation
(there is no printer and nothing splits a box across a boundary), hints to a browser that is not
here, controls the font engine does not expose, things needing an inline flow or a bidi pass, the
flat unblended scene, and scrolling, which belongs to the scene's own node rather than to a CSS
translation.

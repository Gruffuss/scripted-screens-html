# The CSS gaps that are left, by name

CSS is at **341 of 342 properties accounted for (99.7%)**, 88 of 88 selectors, 34 of 34 at-rules,
109 of 109 values. This file names what remains so it never has to be audited again.

Regenerate the numbers with:

```bash
cd ScriptedScreensHtml.Tests && dotnet run -c Release -- --csslanguage
```

---

## Properties: 1 left

### `object-position`

**Reported, not drawn, and the reason is specific.** The picture becomes the scene's `IMG` node,
which the vector layer loads after the scene is sent - so the vector side is the only one that ever
knows the picture's own width and height, and every effect `object-position` has (where a
letterboxed picture sits under `object-fit: contain`, which part shows under `cover`) needs them.
The `uv` crop cannot stand in: it is a rectangle in source fractions and would need the same size.
`IMG` has no alignment key, and an attribute the vector mod does not know is a scene problem, so the
emitter writes nothing and the cascade says `"object-position: ..." is not drawn` once per page.

What would close it is an addition on the vector side, not a change here: an `IMG` key giving the
alignment of the picture within its box (fractions, `[0.5, 0.5]` being today's centring; a `px`
string an offset from the box's top-left edge), applied by the renderer once the texture size is
known. Until then a page is told rather than silently drawn centred.

---

## Values and functions: none

`attr()` as a length (`attr(name unit)`, `attr(name type(...))`, with a fallback), `lh`/`rlh` (a
declared `line-height`, else the emitter's 1.2em line box), `path()` in `clip-path`, and the
`repeating-linear-gradient()`, `url()`, `image-set()` and `s`/`ms` rows all pass. Four of those were
never gaps: their rows expected a string the emitter does not write (`image` for the `IMG` node,
`GL` for hard stripes that are drawn as a repeat of rects) or, for `s`/`ms`, had no transition for
the duration to be the duration of.

---

## Selectors and at-rules: none

Both report "nothing missing". Seven selectors and two at-rules are named as deliberate:
`:visited`, `:target`, `::first-line`, `::selection`, `::file-selector-button`, the column
combinator, `::placeholder` (implemented, but handed to ScriptedScreens' own field control so it
never enters the scene), `@import` and `@page`.

`@container` is answered by the nearest container above the element, by name when the query gives
one, from the container's own laid-out box, and its subtree is re-cascaded when that box changes;
`container-type` and `container-name` are what it reads. Their rows set the property on the
ancestor and read it through a block below, since neither does anything on its own.

---

## Fifty-two properties that are not gaps

Listed with a reason each in `CssLanguage.Deliberate`. They divide into: print and fragmentation
(there is no printer and nothing splits a box across a boundary), hints to a browser that is not
here, controls the font engine does not expose, things needing an inline flow or a bidi pass, the
flat unblended scene, and scrolling, which belongs to the scene's own node rather than to a CSS
translation.

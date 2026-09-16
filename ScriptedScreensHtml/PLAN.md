# Plan: write it as for a browser

The contract: a page written from browser habit works here. Anything a browser does that
fails is a defect, and "there is no vector node for it" is not a reason to skip it: it is
faked with what the vector mod has, or the vector mod gains an additive feature. Only three
things are structurally out and stay listed as such:

1. **Per-pixel effects on the page**: `filter: blur()`, `backdrop-filter`, `mix-blend-mode`.
   The page is geometry, there is no offscreen pass.
2. **Network from a page** (`fetch`, `WebSocket`): data comes from the chip by design.
   `<script src>` and `<link rel=stylesheet>` from a URL are fetched (done, Batch D).

Concave clipping was on this list and is not any more: a stencil mask takes any mesh, so it is
vector work (below). A limit that can be named is a feature to ask for, not a note to write.

Everything else is on this list. Each batch is one build and one restart, verified on a test
page by capture before it moves to `main`. Unverified work lives on `untested`.

The inventory below was taken from the code on 2026-09-15 (StyleApplier's property switch,
HtmlRenderer's tag handling, the script prelude), not from SUPPORT.md, which is a summary.

---

## Batch A — cascade and value correctness (the things that make a real stylesheet misrender)

| Gap | What happens today | Plan |
|---|---|---|
| `box-sizing` | accepted silently; the layout is border-box for everything | CSS default is **content-box**: `width` excludes padding and border. Honour it: when an element has no `box-sizing: border-box` in its cascade, add padding and border to the width/height it was given. Pages with `* { box-sizing: border-box }` (most) are unaffected |
| `inherit`, `initial`, `unset`, `revert` keywords | unknown value, warns | resolve in the cascade: `inherit` copies the parent's cascaded value, `initial`/`unset` drop the declaration |
| `currentColor` | not a colour | resolve to the element's cascaded `color` (or inherited) at apply time, for borders, backgrounds, svg fills, outlines |
| colour syntax | `#rgb`, `#rrggbb(aa)`, `rgb()`/`rgba()` with commas, some names | add `hsl()`/`hsla()`, modern space syntax `rgb(1 2 3 / 50%)`, the full 148 named colours, `transparent` everywhere |
| `min()`, `max()`, `clamp()` | not parsed | evaluate like `calc()` |
| units `vmin`, `vmax`, `ch`, `ex`, `%` on `font-size`, `line-height` unitless | partly | add; `ch` from the face's `0` width |
| CSS nesting (`.a { .b { } &:hover { } }`) | the parser reads a nested block as garbage | parse nesting, expand to flat rules with `&` substitution |
| pseudo-classes `:nth-of-type()`, `:first/last/only-of-type`, `:only-child`, `:empty`, `:is()`, `:where()`, `:has()` (child-only), `:not()` with a list | rule skipped | implement on the node tree, all cheap |
| `:hover`, `:active`, `:focus` | never match | HTML side: a pointer-move handler on the page's host (UGUI passes moves up from the child under the pointer) converts the pointer to page coordinates and hit-tests the layout boxes; enter/leave is a re-cascade and one emit, as a checkbox click is now. `:active` while the button is down, `:focus` for the last clicked control |
| `@font-face { src: url(x.ttf) }` | skipped | resolve the url against the mod's `Assets/fonts` and the Fonts mod's folder; register under the declared `font-family` |
| generic families `monospace`, `serif`, `sans-serif`, `system-ui` | not mapped, legacy face | map: `monospace` → the game's `code`, `sans-serif`/`system-ui` → Barlow, `serif` → RBBook, unless the page named a real face first |
| numeric `font-weight` | bold ≥ 600, else regular | map to the family's real faces (Barlow has Thin..Black); synthetic bold only when the family has no such face |
| `font-stretch: condensed` | ignored | map to `Barlow Condensed` when the family has it |
| easing `steps(n)`, `cubic-bezier()` | steps missing; bezier partial | `steps` as a floor over the tween clock; bezier approximated by a few linear pieces |
| `animation-fill-mode`, `animation-play-state`, `animation-direction` | forwards only, paused missing | implement in KeyframeRunner |
| `transition` on `padding`, `margin`, `border-width`, `border-radius`, `letter-spacing`, `font-size` | rect changes tween, the rest snap | radius and border width as tweened numbers on the `R`; font size snaps (text is TMP) and says so |
| **colour transitions and colour keyframes** | snap | on the vector mod's text-form gradient sample (`fat=`/`sat=`, being added, see Vector work). The emitter declares a two-stop (or per-keyframe multi-stop) `GL` per tweened colour and samples it over the tween clock, through the same tween code width and opacity use |

## Batch B — HTML coverage

| Gap | Plan |
|---|---|
| inline tags `abbr cite q kbd samp var time dfn del ins bdi wbr` | add to the inline set with browser defaults (q quotes, kbd/samp/code monospace, del strike, ins underline) |
| block defaults `blockquote address figure figcaption dl dt dd nav header footer main section article aside h1..h6 margins` | defaults table: margins and indents a browser gives |
| `<pre>` and `white-space: pre/pre-wrap/pre-line` | preserve newlines and runs of spaces in text (the whitespace collapse skips them today) |
| `<label for=id>` | a click on the label toggles/focuses the control it names |
| `<fieldset>/<legend>` | a bordered box with the legend cut into the top edge (two border segments) |
| `<details>/<summary>` | summary is a click region; click toggles the `open` attribute, re-cascade, children shown/hidden; a marker triangle drawn like list markers |
| `<dialog open>`, `showModal()`/`close()` from script | display by the `open` attribute, centred box, script methods |
| `<progress value max>`, `<meter>` | drawn: track and fill in accent, sized by CSS |
| `<optgroup>`, `<option disabled>` | flatten into the select's options, disabled ones skipped |
| `<input type=number/date/time/color/email/url/tel/search>` | text field; `number` with `min/max/step` validated on change |
| `<textarea rows cols>` | size from rows/cols when CSS gives none |
| `<button type=submit>` inside `<form onsubmit>` | fires `submit` on the form in script (`preventDefault` works), no navigation |
| `<img srcset>`, `<picture>/<source>` | take the first candidate |
| `<img alt>` while loading/failed | the alt text as a label until the element reports |
| **table v2** | rows as real elements so `tr` takes `background`, `:nth-child`, `:hover`; `rowspan`; column widths from the widest cell content, `<col width>`/`<th width>` honoured; `border-collapse` as hairlines; `caption-side` |
| entities | the full HTML5 named list, not a dozen |
| `<canvas>` | see Batch E |
| `<iframe> <object> <embed>` | out, stays listed |

## Batch C — paint, on the vector work below plus emitter mappings

Nothing in this batch is faked. Where the vector mod lacks the primitive, it is on the
vector list (items 7–12) and the emitter writes the form given there.

| Gap | Plan |
|---|---|
| `background-image: url()`, `background-size`, `background-position` | an `IMG` node (vector 8) behind the box's content, sized and offset from the CSS; `repeat` by tiling `IMG` nodes across the box |
| `<img>` `border-radius`, `object-fit`, `<img>` inside `overflow`/clip/z-order, remote clients | the `IMG` node carries all of it: radii cut the quad, clips apply, it sits in scene order, the URL travels with the scene |
| `conic-gradient` | `GC` (vector 10) |
| `repeating-linear-gradient`, `repeating-radial-gradient` | expanded to an explicit stop list; emitter only |
| `border-style: double`, `groove`, `ridge`, `inset`, `outset` | two strokes, light/dark side colours; emitter only |
| `filter: drop-shadow()` | `sh` on the subtree; emitter only |
| `filter: brightness() contrast() grayscale() sepia() invert() saturate() hue-rotate() opacity()` | colour filters on a group (vector 11) |
| `filter: blur()`, `backdrop-filter`, `mix-blend-mode` | structural, out |
| `clip-path: inset() circle() ellipse() polygon()` | `CP`; a concave polygon through the stencil clip (vector 7) |
| `mask-image: linear-gradient(...)` | gradient mask on a group (vector 9) |
| `transform: skew()`, `matrix()`, 3D functions | the 2x3 matrix on `G` (vector 12); 3D takes its 2D part |
| `writing-mode: vertical-rl/lr` | the label in `G r=90/-90`; emitter only |
| `vertical-align: sub/super/middle` on inline text | TMP `<sub>`, `<sup>`, `<voffset>`; emitter only |
| `text-align: justify` | `T align=justified` (vector 4) |
| `float: left/right`, `clear` | layout: the parent becomes a wrapping row, the float first or last with auto margin. Not text flowing around a box; a real float needs an inline formatting context, which UI Toolkit does not have |
| `display: inline-block`, `contents`, `table*` | layout mapping |
| `columns` | a wrapping row of equal columns filled in order |
| `aspect-ratio` | height from width on layout, and the reverse |
| `position: fixed` | absolute to the page (done, untested) |
| `z-index` across parents | a positioned element with z-index above its parent's later siblings is emitted at the stacking root |

## Batch D — script DOM completeness

| Gap | Plan |
|---|---|
| `getElementsByClassName`, `getElementsByTagName`, `getElementsByName` | over the node tree |
| `insertAdjacentHTML/Element/Text`, `before`, `after`, `prepend`, `replaceWith`, `toggleAttribute` | via insert-before and the existing fragment path |
| `el.style` read-back, `style.cssText`, `style.setProperty/getPropertyValue/removeProperty` | keep a per-element style map on the worker that mirrors what was set, plus the cascade's record for reads |
| `className` getter, `id` setter, `hidden`, `disabled`, `title`, `name`, `type`, `href`, `src` properties | attribute-backed properties |
| `dispatchEvent`, `new Event()`, `new CustomEvent()`, `Event` constructor, `addEventListener` on `document`/`window` for `click` | local dispatch to the element's listeners, bubbling up parents (so a listener on a container catches its children's clicks, a browser habit) |
| `DOMContentLoaded`, `load`, `readyState` | fire after the page script runs; `readyState` = complete |
| `window.innerWidth/innerHeight`, `screen`, `devicePixelRatio`, `matchMedia()` | from the design size; matchMedia uses the same evaluator as `@media` |
| `getComputedStyle(el)` | the cascade record with resolved colours and the layout sizes; `getPropertyValue` |
| `scrollWidth/scrollHeight`, `scrollTop` read | from layout and the scroll box's content height; **setting `scrollTop` and `scrollIntoView()`** through the vector mod's forced scroll offset (`so=`/`sov=` on `SC`, see Vector work) |
| `Element.animate(keyframes, options)` | compile to the keyframe runner; returns an object with `cancel()`/`finish()` |
| `crypto.randomUUID/getRandomValues`, `URL`, `URLSearchParams`, `TextEncoder/Decoder`, `structuredClone`, `queueMicrotask`, `console.table/group/time` | small shims |
| `new Image()` with `onload`, `new Audio(url).play()` | Image resolves after the image element reports; Audio maps to a sound element |
| `<script src="url">`, `<link rel=stylesheet href="url">`, `type="module"` | fetch text through UnityWebRequest like images (raw GitHub works), then run/apply; modules: strip `import`/`export` of local names, no real module graph |
| `event.clientX/Y`, `offsetX/Y`, `pageX/Y` on click; `mousemove`, `mouseover/out`, `mouseenter/leave` | HTML side: the click reaches the input prefix in the frame it happened, so the pointer position is read there and converted to page coordinates; moves come from the same host handler as `:hover`. Keyboard events stay out |
| `requestAnimationFrame` per-frame DOM writes | works at 30 Hz; the cost note stays. A page that animates a `transform` via rAF re-emits 30 scenes a second; that is the one browser habit that is honestly expensive here, and SUPPORT says so |

## Batch E — `<canvas>`

The 2D context is already recorded (moveTo/lineTo/arc/bezier/rect/fill/stroke/fillRect, colours,
widths, caps, alpha). Translate a recorded frame into vector nodes: paths become `P d=…`
with `f`/`s`/`sw`/`cap`/`join`, `fillRect` becomes `R`, `clearRect` starts a new frame,
`save/restore` with `translate/rotate/scale` become `G`. Text (`fillText`) becomes `T`,
gradients (`createLinearGradient`) become `GL`, `drawImage` an image element, `clip()` a `CP`
when convex. A canvas painted once (a chart drawn at load, redrawn on data) is then free; a
canvas repainted every frame costs a scene emit per frame and the docs say so, the same as
rAF DOM writes. `getImageData`/`putImageData` stay out.

## Batch F — measured gaps (2026-09-15 inventory: 78 pages, MDN standard list)

Built from `ScriptedScreensHtml.Tests/inventory.py`, most used first, then the spec list.

| Part | Items |
|---|---|
| F1 SVG and numbers | `font-variant-numeric: tabular-nums` (TMP monospacing on digit runs); SVG `<text>`/`<tspan>`; SVG styled by CSS (`fill`, `stroke`, `stroke-width`, `opacity`, `font-*` as properties and inline `style`); the `transform` attribute on shapes and groups; `stroke-dasharray`/`dashoffset`; `text-anchor`; `<use>`/`<symbol>`; `<clipPath>` and the `clip-path` attribute; `fill-rule`; `<image>` as `IMG`; `<title>`/`<desc>` skipped |
| F2 cascade | the 52 logical properties as a mapping table; per-side `border-*-style`; `text-decoration-line/color/style/thickness`, `text-underline-offset` (offset: TMP draws at one position, colour and line map); `color-mix()`; `@layer` (contents in order), `@property` (ignored), `@import` (fetched), `@scope` (prefix), `@container` size queries on layout; `::placeholder` to the field's placeholder colour, `::marker` to the marker; `:required/:optional/:valid/:invalid/:in-range/:out-of-range/:placeholder-shown/:default/:indeterminate/:read-only/:read-write/:disabled` from attributes and values; `:open`/`:modal`; `:any-link`; `:lang()`; `:dir()` |
| F3 text, lists, tables, layout, script | `text-indent`; `word-break`/`overflow-wrap`/`hyphens` to TMP wrap modes; `line-clamp`; `text-align-last`; `counter-reset`/`counter-increment` and `counter()` in `content`; `list-style-image`; `caption-side`, `table-layout`, `empty-cells`; flex `order`, `flex-flow`, `place-*`, `justify-items`/`justify-self`; `grid-area`, `grid-template`, `grid-template-areas`; `DocumentFragment`; a module loader for `import` from a URL |
| F4 rescued from the old exclusion list | `::first-letter` (a generated child); `::first-line` (estimated now, exact through vector 15); `::-webkit-scrollbar`, `scrollbar-width`, `scrollbar-color` as a drawn track and thumb over `sy`; `@counter-style`; `corner-shape` as a `P` outline; `border-image` with a gradient as a gradient stroke, nine-slice through vector 16; `ruby`; `background-attachment` no-op; scroll-driven animations (`animation-timeline: scroll()`/`view()`) as expressions over `sy`; `backface-visibility`/`perspective` card flip as `scaleX(cos θ)` with a hidden back; `background-clip: text` through vector 14 |
| F5 accepted silently, listed | scroll snap and scroll margins, `will-change`, `contain`, `isolation`, `touch-action`, `-webkit-font-smoothing`, `color-scheme`, `zoom`, `all`, `overscroll-behavior`, `text-wrap`, `image-rendering` |

Out, with the reason: `::selection` (no text selection), `@page` (print), `@view-transition`
(no document navigation), `shape-outside` (no inline formatting context), MathML (no user),
per-pixel effects, network from a page, animation triggers (draft spec).

---

## Vector work (agreed 2026-09-15, all additive; the HTML side treats them as present)

What the vector session implements, with the shape the HTML emitter will write:

1. **Text-form gradient sample.** `fat==expr` and `sat==expr` in the scene text, honoured when
   `f`/`s` is `@gradient`, on shapes and on `T`. The reader turns them into the map form
   `{ grad, at }` that `SceneModel` already parses; `T` resolves paint the way shapes do.
   Unlocks colour transitions and colour keyframes.
2. **Inset shadow on shapes.** An inset entry in `sh` (a flag or an `shi=` list): the ring and
   feather drawn inward from the outline and clipped to the shape's own outline. Every CSS box
   is convex, so the existing clip serves.
3. **More than one text shadow.** `sh` on `T` with several entries: a second label behind the
   first per extra entry, offset and coloured.
4. **Justified text.** `T align=justified` → TMP `Justified`.
5. **Rounded text masks.** Labels clipped to the rounded clip rather than its rectangle,
   by cutting the TMP glyph quads against the rounded outline after the mesh update.
   Cosmetic; only visible within one corner radius of a rounded edge. Feasibility of this
   and of an inset shadow on text itself is being checked separately (glyph outlines are
   concave, so anything that must clip to letters is suspect).
6. **Forced scroll offset.** `so=<offset> sov=<version>` on `SC`: a changed version applies
   the offset once, then the wheel owns it again. Unlocks `scrollTop =` and
   `scrollIntoView()` from a page script.
7. **Concave clips.** A `CP` whose outline is not convex puts the clipped subtree into its
   own slice child under a UGUI `Mask` whose graphic is the clip polygon (triangulated as
   paths already are); labels inside get the same mask. One draw call per such group.
   The geometric clipper keeps the convex case.
8. **`IMG` node.** `IMG x= y= w= h= src=url fit=cover|contain|fill rx=` — a textured quad
   drawn as its own slice with the texture's material; radii and convex clips cut the quad
   with interpolated UVs, concave clips use 7; it sits in scene order and inside `SC`. The
   texture from the URL through a UnityWebRequest (or ScriptedScreens' image cache by
   reflection); natural size from the texture, so `contain`/`cover` are exact.
9. **Gradient mask on a group.** `G mask=@gradient { ... }`: every vertex's alpha in the
   subtree multiplied by the gradient's alpha at that vertex, with the fill subdivision so
   the ramp is smooth; labels take it per glyph through TMP vertex colours.
10. **Conic gradient.** `GC id cx cy angle stops` in defs, angle-per-vertex.
11. **Colour filters on a group.** `G bri= con= sat= hue= gray= sep= inv= { ... }`, applied
    per vertex at emit, so gradients and labels take them too.
12. **A 2x3 matrix on `G`.** `m=[a,b,c,d,e,f]` alongside `t r s`. Makes `skew()` and
    `matrix()` exact.

Not vector work after all: `:hover` and click coordinates are done on the HTML side from
its own layout boxes and the pointer position (Batch A and D).

## Status (2026-09-15 evening, branch `untested`, seen on consoles 561 and 586)

| Batch | On a console | Fixed from what the console showed |
|---|---|---|
| A cascade and values | HtmlTest4: all rows, motion row by eye | :hover followed presses, not the cursor (legacy input module sends no moves: polled now) |
| B HTML coverage | HtmlTest5: all rows | |
| D script DOM | HtmlTest6: all PASS, external script loaded (repo made public) | reads after DOM writes saw the old tree; script-made elements lacked the live API once adopted; `load` fired per script; `innerHTML` on a container did not replace built children |
| C paint | HtmlTest7: all rows; vector 1, 2, 4, 5, 8, 9, 10, 11, 12, 13 seen | float in a sentence sat left; conic stops all at 0; 3D border keywords warned |
| E canvas | HtmlTest8: the canvases drew nothing (the vector mod flagged a degenerate clip) | a canvas box was 0x0: it now takes its bitmap size unless CSS sizes it; by eye after that |
| F1 SVG and numbers | HtmlTest9: all rows (the `evenodd` hole since vector 0.11.23) | svg text/image lost their own x/y/size; clipPath defs were viewBox-space |
| F2 cascade | HtmlTest10: all rows | controls had no border and clipped their text |
| F3 text, lists, tables, layout, script | HtmlTest11: text, counters, tables, flex, grid rows; script rows need eyes | a ::before stacked above its text (inline runs between blocks now flow as one line); list-style-image read from the list; caption-side inherited; img/controls inline; width-less table cells sized to content; break-all wraps in the layout |
| F4 rescued items | HtmlTest12: all rows; vector 7, 14, 15, 16 seen | ::first-letter only ran with an ::after; the border-image gradient came out flat (def now in scene space) |
| F5 silent list | n/a | |
| I the Apple-style Atmo mockup's constructs (2026-09-16, HtmlTest15) | theme switch by a root attribute re-cascading the subtree, mixed calc() against the containing block, striped duct bars marching, grid cell alignment and equal 1fr rows, baseline alignment, scroll events with the real offset (vector 0.11.24), tabular digits, the mockup's shadow; captures on 586 | a flex/grid container of spans became one label (its grid was never laid out); typography set on body never reached the labels (font-family, weight, line-height, letter-spacing are inherited now); tabular digits wrapped in the numeric guard printed their tags; block text was centred in a tall box; script style writes reached the emitter only for offset-*; a static field initialised after a selector that used it (would have broken the parser in game); a page whose panel had gone to sleep never pumped its script, so timers on a static page never fired; letter-spacing in em was resolved twice; an auto row in a grid with its own height did not fill it; a re-cascade reset grid items to flow position and the grid's placement cache skipped them (post-layout placements re-run after every re-cascade now); a capture's last script frame left its writes unpumped |
| H every build row of COVERAGE.md (2026-09-16) | HtmlTest14 on 586 (two halves, PART=1/2): units, colour spaces and relative colours, light-dark, env, xywh/rect/evenodd clips, corner shapes, grid shorthand, details-content, popover, text-wrap-mode, preserve, initial-letter, baseline-shift, text-emphasis, background-origin, image-set, base href, ray and offset-anchor, column-width/span, overflow-clip-margin, mask-size/position by capture; markers, paint-order, pathLength, crispEdges, non-scaling stroke, fill-box rotation, DOMParser/XMLSerializer by scene dump; the display transition by dump; map areas and the datalist need eyes | svg geometry from CSS rules never reached the shape (only inherited names were copied; the first fix landed on svg text alone); the unit table compared `Q` case-sensitively; `image-set()` as a background warned as unsupported |
| G after COVERAGE.md (2026-09-16) | HtmlTest13 on 586: backdrop, trig widths, column-rule, offset-path (dots and the clipped arrow; the animated distance seen in the scene dump), col widths, picture/source, ::after counters seen by capture; @starting-style seen in the log (the capture is taken at t=0 of the fade, so the card is still invisible there); :user-invalid, elementFromPoint, add-pill insert, dialog close and the held button need eyes | the modal dialog drew in document order (top layer now); counters never advanced inside a label; column-count on plain text did nothing; a clip-path stayed where the untransformed box was under a transform or offset-path; a dialog the page positions kept the default centring translate; the unmatched-data-key warning fired before the first scene; a setTimeout set by the page's load script fired on the first frame (the script clock started at zero) |

Captures now run the page script for a few frames before grabbing the page, so script-drawn
content shows; animation and network fetches still need eyes.

**Docs for publishing (2026-09-15 evening):** `README.md` (the guide), `About/About.xml`,
`SUPPORT.md` (from what the consoles showed) and `examples/01-06` (each verified on a console
by capture). The examples found and fixed: list text beside its marker, flex/grid children as
items, script-built table rows, a canvas laid out at 0x0, an inline element holding a classed
span folding into text, a page without font-size having none, the unmatched-data-key warning
firing for `$name` expression keys.

## Approximations left by Batch F (to finish, not limits)

- `@container` size queries are decided against the design size like `@media`; a real one
  needs the container's laid-out size and a re-cascade after layout.
- `::first-letter`: the rest of the paragraph wraps below the letter as one label, not
  around it (no inline flow around a float in the layout engine).
- `ruby`: the annotation is small and raised after its base, in the sentence; a browser
  stacks it above the base.
- `border-image` with a `px`/number slice: the image size is unknown here, read as thirds
  (percent slices are exact). Vector 16 carries the crop.
- `text-decoration` longhands (colour, thickness, offset, style) are drawn as geometry
  under single-line labels only; a wrapped label keeps TextMeshPro's plain underline.
- `rotateX`/`rotateY` are the flat foreshortening (`cos`), no perspective.
- `animation-timeline: scroll()/view()` covers opacity and the 2D transform functions
  in the keyframes; `animation-range` is ignored.
- An inline-block box beside text (a small LED span) sits at the bottom of the line, not on
  the text baseline; `vertical-align` on it is not applied.

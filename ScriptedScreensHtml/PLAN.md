# Plan: write it as for a browser

The contract: a page written from browser habit works here. Anything a browser does that
fails is a defect, and "there is no vector node for it" is not a reason to skip it: it is
faked with what the vector mod has, or the vector mod gains an additive feature. Only three
things are structurally out and stay listed as such:

1. **Clipping to an arbitrary (concave) shape.** The vector mod clips geometrically to convex
   regions only. Convex `clip-path` shapes work; a star does not.
2. **Per-pixel effects on the page**: `filter: blur()`, `backdrop-filter`, `mix-blend-mode`.
   The page is geometry, there is no offscreen pass. Colour-only filters are faked (below).
3. **Network from a page** (`fetch`, `WebSocket`): data comes from the chip by design. One
   exception is planned: `<script src>` and `<link rel=stylesheet>` from a URL, fetched the way
   ScriptedScreens fetches an image.

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
| `:hover`, `:active`, `:focus` | never match | **needs the vector mod**: pointer enter/leave on click regions (see Vector asks). Then a hover is one re-cascade and re-emit, as a click is now |
| `@font-face { src: url(x.ttf) }` | skipped | resolve the url against the mod's `Assets/fonts` and the Fonts mod's folder; register under the declared `font-family` |
| generic families `monospace`, `serif`, `sans-serif`, `system-ui` | not mapped, legacy face | map: `monospace` → the game's `code`, `sans-serif`/`system-ui` → Barlow, `serif` → RBBook, unless the page named a real face first |
| numeric `font-weight` | bold ≥ 600, else regular | map to the family's real faces (Barlow has Thin..Black); synthetic bold only when the family has no such face |
| `font-stretch: condensed` | ignored | map to `Barlow Condensed` when the family has it |
| easing `steps(n)`, `cubic-bezier()` | steps missing; bezier partial | `steps` as a floor over the tween clock; bezier approximated by a few linear pieces |
| `animation-fill-mode`, `animation-play-state`, `animation-direction` | forwards only, paused missing | implement in KeyframeRunner |
| `transition` on `padding`, `margin`, `border-width`, `border-radius`, `letter-spacing`, `font-size` | rect changes tween, the rest snap | radius and border width as tweened numbers on the `R`; font size snaps (text is TMP) and says so |
| **colour transitions and colour keyframes** | snap | **needs the vector mod's text-form gradient sample** (`fat=`/`sat=`, see asks). Until then: fake with a stepped tween (re-emit the colour at ~10 steps over the duration, from Lua-side clock) so a 300 ms fade is 3 re-emits, not a snap |

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

## Batch C — paint fakes (things the vector mod does not do natively, done with what it has)

| Gap | Fake |
|---|---|
| `background-image: url()` | a ScriptedScreens image element **under** the page (z_index page − 1), and the box emits no fill so the image shows through; `background-size: cover/contain/px`, `background-position` by sizing the element's rect (clipped by the page mesh where the box has a border); `no-repeat` only, `repeat` stays listed |
| `box-shadow: inset` | four linear-gradient bands inside the edges, clipped to the box (convex, so fine), plus corner blend; multiple shadows layered |
| second and further `text-shadow` | extra `T` labels behind the text, offset and coloured (TMP underlay stays for the first, with blur) |
| `conic-gradient` | fan of wedges (`P` pie slices) with per-wedge flat colour, 36 slices, stops interpolated |
| `repeating-linear-gradient`, `repeating-radial-gradient` | expanded to an explicit stop list over the box |
| `border-style: double`, `groove`, `ridge`, `inset`, `outset` | two strokes; the 3D ones as light/dark side colours |
| `border-image` | out, listed |
| `filter: drop-shadow()` | `sh` on the subtree's shapes and labels |
| `filter: brightness() contrast() grayscale() sepia() invert() saturate() hue-rotate() opacity()` | colour math on every flat colour in the subtree (fills, strokes, text); gradients per stop. Exact for flat colours, which is what pages use it on |
| `filter: blur()`, `backdrop-filter`, `mix-blend-mode` | out, listed (structural) |
| `clip-path: inset() circle() ellipse() polygon()` | `CP` when convex (inset, circle, ellipse, and a polygon that tests convex); concave polygons warn and clip to the bounding box |
| `mask-image: linear-gradient(...)` | when the element's ancestor background is flat: a gradient band from transparent to that background over the edge (the scroll-fade idiom); otherwise listed |
| `transform: skew()`, `matrix()`, `rotate3d/translate3d/scale3d` | decompose to rotate/scale/translate (skew approximated by the closest rotate+scale, or exact for svg by baking); 3D takes the 2D part |
| `writing-mode: vertical-rl/lr`, `text-orientation` | the label in a `G r=90/-90` |
| `vertical-align: sub/super/middle/text-top` on inline text | TMP `<sub>`, `<sup>`, `<voffset>` |
| `text-align: justify` | TMP `align=justified` if the vector `T` passes it; else left, listed |
| `object-fit: contain/cover` on `<img>` | needs the natural size: read it back from ScriptedScreens' image cache after load, then size the rect; until loaded, stretch |
| `border-radius` on `<img>` | the image element cannot be rounded; draw the page background as a rounded frame over its corners (works on flat backgrounds, which is the case that matters) |
| `outline-style: dashed/dotted` | `dash` on the outline stroke |
| `float: left/right`, `clear` | the parent becomes a wrapping row; a float goes first (left) or last with `margin-left: auto` (right), text after it fills the rest. Not a real float around, but the layouts pages actually write |
| `display: inline-block`, `inline`, `contents`, `table*` | inline-block is a row item (done); `contents` unwraps; `display: table/table-row/table-cell` goes through table v2 |
| `columns` | a wrapping row of equal columns filled in order |
| `aspect-ratio` | height from width on layout, and the reverse |
| `position: fixed` | absolute to the page (done, untested) |
| `z-index` across parents | sibling order only today; a positioned element with z-index above its parent's later siblings needs re-parenting the emitted subtree to the stacking root: do it for `position: absolute/fixed` + z-index |

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
| `scrollWidth/scrollHeight`, `scrollTop` read | from layout and the scroll box's content height; **setting `scrollTop` and `scrollIntoView()`** need the vector mod to accept a scroll offset (see asks) |
| `Element.animate(keyframes, options)` | compile to the keyframe runner; returns an object with `cancel()`/`finish()` |
| `crypto.randomUUID/getRandomValues`, `URL`, `URLSearchParams`, `TextEncoder/Decoder`, `structuredClone`, `queueMicrotask`, `console.table/group/time` | small shims |
| `new Image()` with `onload`, `new Audio(url).play()` | Image resolves after the image element reports; Audio maps to a sound element |
| `<script src="url">`, `<link rel=stylesheet href="url">`, `type="module"` | fetch text through UnityWebRequest like images (raw GitHub works), then run/apply; modules: strip `import`/`export` of local names, no real module graph |
| keyboard events, `event.clientX/Y` on click | keyboard: out until the vector mod has it; click coordinates: needs the vector mod to pass them (see asks) |
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

## Batch F — verification and docs

- One test page per batch, pushed to the 3x3, captured before and after the restart, kept in
  the repo. A feature is not done until the capture shows it.
- SUPPORT.md rewritten from this file once a batch lands: the "does not work" tables should
  end with the three structural items, the vector-side waits, and the honest cost notes.

---

## Vector asks (relayed to the vector session, all additive)

1. **Text-form gradient sample**: `fat==expr` / `sat==expr` when `f`/`s` is `@gradient`, on
   shapes and on `T`. Unlocks colour transitions and colour keyframes directly.
2. **Pointer enter/leave on click regions** (`hover=1`, events `enter`/`leave` with the node id
   through the same forwarder as clicks). Unlocks `:hover`, `:active`, `mouseover/mouseout`.
3. **Click position**: the click event carrying the pointer's scene coordinates. Unlocks
   `event.offsetX/Y`, `clientX/Y`, and click-to-position widgets (sliders drawn by the page).
4. **Scroll offset from the outside**: a way to set an `SC`'s offset (a prop on the element, or
   a data key). Unlocks `scrollTop =`, `scrollIntoView()`, "scroll to bottom" logs.
5. **Rounded text masks**: labels clipped by the rounded clip, not its rectangle. Cosmetic.
6. Optional, since they are faked meanwhile: inset shadow, a second text shadow, `justified`
   text alignment on `T`.

## Order

A, then B, then D, then C, then E. A first because a real stylesheet hits `box-sizing`,
`currentColor`, `hsl()` and nesting on its first screen; B because pages use the tags; D because
scripts written from habit use `insertAdjacentHTML` and `dispatchEvent` before they use any
paint effect; C and E are visual polish and can run while the vector asks are in flight.

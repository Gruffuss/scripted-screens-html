# What works from HTML5, CSS and JS, and what does not — and why

State as of 2026-09-15, vector back-end, vector mod 0.11.20.0, everything below confirmed on a console. The pipeline decides everything below:

```
HTML + CSS  →  parse, cascade  →  UI Toolkit lays the boxes out (layout ONLY; grid is ours)
            →  emitter translates the laid-out boxes to the vector mod's scene text
            →  the vector mod draws it as geometry, off-thread, in the Fonts mod's faces
JS          →  runs on a worker thread; its DOM writes re-run the translation
```

So a feature "works" when all three agree: the CSS parser understands it, the layout can
place it, and the emitter has a vector node for it. Anything that needs **per-frame
painting** (canvas, JS animation loops) or **per-pixel filters** (blur of the page itself,
backdrop effects) is structurally out, because the vector layer draws geometry, not pixels.
Pointer input reaches a page only as **clicks on buttons**, delivered to Lua.

The rule of thumb: **the page changes on data, motion is an expression.** JS and data
decide what is on screen; anything that moves every frame is an SVG attribute written as
`="…"` over `t` (seconds), `i` (instance) and `$name` (data), or a CSS transition or
keyframe, which compiles to the same thing.

---

## HTML

### Works

| Feature | Notes |
|---|---|
| `<html> <head> <body> <style> <script>` | one page per `html` element, `src` prop |
| `<meta name="viewport" content="width=768">` | design width; the page lays out at it and is stretched to the console |
| Block containers: `div section header footer main nav article aside p h1–h6 ul ol li` | a block is a flex column; `display: flex` is a row; `display: grid` is a grid |
| Inline text: `b i u s span small big sub sup mark code font br` | rendered as rich text inside one label |
| Inline element **with an id** | kept as its own label so data and JS can target it |
| `id`, `class`, inline `style` | |
| Comments, entities (`&amp; &lt; &nbsp; &#x25BC;`) | |
| `<hr>` | a 1px rule |
| `<table>` with `thead/tbody/tfoot/tr/td/th/caption`, `colspan` | laid out as a grid, one column per cell of the widest row, columns of equal width (size them with CSS on the cells); `tr` is transparent, so a rule on `tr` styles nothing |
| `<ul>/<ol>/<li>` | markers at the front of each item, as a browser draws them: disc, circle and square are shapes in the text colour (no font involved), decimal/alpha/roman are text; `list-style-type` on the list or the item, `none` for no marker |
| `<a href>` | underlined link-coloured text; there is nowhere to navigate, so `href` is inert |
| `<input type=text/password/number/search/…>`, `<textarea>` | a ScriptedScreens `textinput` over the box (`value`, `placeholder`, `title`); the page's `background`, `color` and `font-size` style it. Editing it fires `input` and `change` on the element in page script (`e.target.value`), and the page element's Lua `on_change(v)` gets `"name=value"` (the input's `name`, else its id) |
| `<input type=checkbox>`, `<input type=radio name=g>` | drawn by the page: a box or ring sized by `width`/`height` (16px default), stroked in the text colour, filled with `accent-color` and a white tick or dot when checked. `checked` state is kept on the page, radios are exclusive per `name`. Restyle them the browser way: `appearance: none`, your own `border`/`background`/`border-radius`, and an `input:checked { … }` rule (`:checked`, `:disabled`, `:enabled` match the attributes). A click fires `change` on the element in script (`e.target.checked`) and reaches Lua as `"name=true"`/`"name=false"` |
| `<input type=range min max value>` | a ScriptedScreens `slider`; `accent-color` is the fill; Lua gets the number |
| `<select><option value>` | a ScriptedScreens `select`; `selected` on an option picks the initial one; script and Lua get the option's `value` (else its text) |
| `<input type=button/submit/reset value>` | a click region like `<button>` |
| Script writes to a control | `el.value = …` and `el.checked = …` update the control; `focus()`/`blur()` are no-ops |
| `<img src>` | a ScriptedScreens `image` element placed over the box (URLs load through ScriptedScreens; raw GitHub works, Wikimedia refuses Unity's request); `width`/`height` attributes or CSS size the box. Confirmed 2026-09-15: stays through clicks and surface rebuilds. Host and single player only: the element is written to the local surface model, not sent to remote clients |
| `<video src autoplay loop muted>`, `<audio src autoplay loop>` | ScriptedScreens `media` and `sound` elements, placed the same way as `<img>`; ScriptedScreens' own multiplayer and video gating applies. Not yet seen on a console |
| `<button id>` (also any element with `onclick` or `data-click`) | a click region in the vector scene. The click arrives at the page element's Lua `on_click(nodeId, player)` with the button's id, and a `data` write from there updates the page. Page JS does not see clicks; bounce them through `data` if the page needs them. Confirmed 2026-09-15 with no noticeable delay on the first click |
| `<svg>` with `line polyline polygon rect circle ellipse path g defs linearGradient radialGradient` | translated one-to-one to vector nodes; `viewBox`, `preserveAspectRatio="none"` (strokes keep one width: the scale is baked into coordinates), `fill stroke stroke-width opacity fill-opacity stroke-opacity stroke-linecap stroke-linejoin`, `fill="url(#id)"` |
| SVG extensions | any attribute may be `="expression"`; `n="36"` on `polygon`/`polyline` makes a sampled band/line (`x y y2` per sample `i`); `n="42"` on `circle`/`rect`/`ellipse`/`path` repeats it (`hash(i)` for per-instance randoms); `fo2 fea fea_edge lod dash dofs` pass through to the vector layer |
| Data binding by id | `data = { id = value }` from Lua: string/number → text, table → CSS, bool → display; an svg shape id takes `points`, an attribute table, or a **number array**, which binds the shape to `$id[i]` so the vector mod scrolls it between ticks. Ids always bind, and a page script's `data` handler gets the same payload afterwards; the whole payload is also forwarded flattened to the scene as `$a_b` |

### Does not work

| Feature | Why |
|---|---|
| `<canvas>` | its whole model is "script repaints pixels every frame". The vector layer draws geometry once and animates it with expressions. A canvas is laid out but draws nothing. Use `<svg>` with expressions |
| `<form>` submission, `<input type=file/date/color>`, `<datalist>` | a form is just a box (no submit, no navigation); those input types have no ScriptedScreens control. `<input type=date>` and friends fall back to a text field |
| `<iframe> <object>` | no meaning here |
| Text clipped to a rounded shape | a label under a rounded `overflow: hidden` box is clipped to the box's rectangle, not its rounded outline (the vector text layer masks with a rectangle). Standing limit; invisible at the radii dashboards use |
| `<img>` etc. for remote players | the image, media and sound elements are written into the host's local surface model, not sent as Lua ops, so a remote client never receives them. Single player and the host see them. The route, if needed: issue them as upsert ops |

---

## CSS

### Works

**Selectors:** `tag`, `.class`, `#id`, compounds (`div.a#b`), descendant (`a b`), child
(`a > b`), adjacent sibling (`a + b`), selector lists (`a, b`), specificity and source
order, `!important`, `:root`, `:first-child`, `:last-child`, `:nth-child(an+b | odd | even)`,
`:not(compound)`. State pseudo-classes (`:hover :active :focus …`) parse and never match:
there is no pointer.

**Values:** `px`, `%`, `em`, `rem`, `vw`, `vh`, `vmin`, `pt`; `calc()` with `+ - * /` and
parentheses (px and % kept apart: the non-zero part wins); custom properties (`--x` on any
element, inherited) and `var(--x, fallback)`.

**Box model:** `width height min-* max-*`, `margin` and per-side, `padding` and per-side,
`position: absolute | relative`, `top right bottom left`, `inset`, `display: none | block |
flex | grid`, `overflow: hidden` (a clip, rounded corners honoured), `visibility`,
`outline` / `outline-offset` (a stroke outside the border box, `outline-style` solid only), `pointer-events: none` (the element is no click region), `opacity` (whole subtree), `z-index` (siblings painted in z order, document order within
a value; text obeys it too, since vector mod 0.11.12.0 draws labels in scene order),
`box-sizing` (always border-box, as UI Toolkit is).

**Flexbox:** `flex-direction`, `flex-wrap`, `flex`, `flex-grow/shrink/basis`,
`justify-content`, `align-items`, `align-self`, `align-content`, `gap` / `row-gap` /
`column-gap` (as margins on the children).

**Grid:** `grid-template-columns` / `-rows` in `px`, `%`, `fr`, `auto`, `repeat()`,
`minmax()` (its max); `grid-auto-rows`; `gap`; auto placement row-first; `grid-column` /
`grid-row` as `a`, `a / b`, `span n`, `a / span n`, negative lines (`1 / -1`). Children fill
their cells; auto rows take the tallest child. Not: named lines and areas, dense packing,
`justify-items`/`align-items` inside a cell.

**Paint:** `color`, `background`/`background-color`, `linear-gradient(...)` (angle or `to
side`, any number of stops, **hard stops** split geometrically so the edge is exact),
`radial-gradient(...)` (`circle`/`ellipse`, `at x y`, size keywords approximated by radius; affordable since vector mod 0.11.20.0: the whole 2x2 test page including its 90x50 radial box is 11,403 vertices at 1,408 px on screen, where it was 53,482 before),
`border` shorthand, per-side `border-*-width` and `border-*-color`, a rounded box with a
differently coloured side (drawn as arcs), `border-style: dashed | dotted`, `border-radius`
and per-corner (CSS overflow clamp applied), `box-shadow` (offset, blur, spread, colour,
several; drawn as geometry by the vector mod; `inset` skipped).

**Text:** `font-family` (any face in the Fonts mod's folder: `'Barlow'`, `'Barlow
SemiBold'`, game faces such as `'noto-punc'`), `font-size`, `font` shorthand,
`font-weight` (bold ≥ 600, not applied twice on a face that is already a named weight),
`text-align`, `white-space: nowrap`, `letter-spacing`, `text-overflow: ellipsis` (with
overflow hidden), wrapping (a label laid out on more than one line wraps in the vector
layer too), `line-height` (number, px, em or %), `text-transform`, `text-decoration:
underline | line-through`, `text-shadow` (one per label, via the font shader's underlay),
quotes, backslashes and line breaks in text.

**Transform:** `transform: translate() translateX/Y() rotate() scale() scaleX/Y()` — a
vector group with anchor at the box centre.

**Motion:** `transition` (property, duration, easing, delay) on layout properties, `opacity`
and `transform`, and `@keyframes` + `animation` (duration, delay, easing, iteration count,
`infinite`, `reverse`, `alternate`). Compiled to vector expressions over `t`: a change is
emitted once as `=from+(to-from)*ease(clamp((t-start)/dur,0,1))`, the vector mod moves it
every frame, and the scene is re-emitted with plain numbers when the last tween ends.
Easings: `linear`, `ease-in`, `ease-out`, everything else is smoothstep.

### Does not work

| Feature | Why |
|---|---|
| `display: inline`, `inline-block`, `float`, inline flow | no inline formatting context. A block is either all text (one label, rich text) or all boxes (a container); mixed content becomes a wrapping row |
| `transition` on `color`/`background-color`, colour keyframes | the vector mod animates a colour by sampling a gradient at an expression (`f = { grad, at }`), but that form exists only as a structured prop and the page reaches the vector mod through the scene text, which has no map syntax; colour changes snap until the text form gains it (asked for as `fat=`/`sat=`). Layout, opacity and transform transitions animate |
| `filter`, `backdrop-filter`, `mix-blend-mode` | per-pixel effects on the page need an offscreen pass; the vector layer is geometry |
| `box-shadow: inset`, more than one `text-shadow` | the vector shadow is a drop shadow only; the text underlay is a single layer (a second is reported in the log) |
| `outline` | not mapped; use a border |
| `@font-face`, `@import` | fonts come from the Fonts mod folder; there is nothing to import from. `@media` **works**: decided once against the design size (`min/max-width/height`, `orientation`, `aspect-ratio`, `screen`, `print`, `not`, `and`, commas); `@supports` is taken as true |
| Pseudo-elements other than `::before`/`::after` | `::before` and `::after` **work** with `content` (quoted strings with `\25B2` escapes, `attr(name)`, `none`), as a generated inline child that takes the rule's own styles, so a decorative dot with `width`/`height`/`background` is a box; `counter()` does not exist. Attribute selectors (`[a]`, `[a=v]`, `~= |= ^= $= *=`) and the `~` combinator **work** |
| `background-image: url()`, `background-size/position/repeat`, multiple backgrounds | images are elements (`<img>`), not paint |
| `cursor`, `user-select`, `scroll-*` | no pointer or scrolling; accepted silently. `pointer-events: none` is honoured (see above) |

---

## JavaScript

Runs on a **worker thread** in Jint (a .NET ES2023 engine). The language itself is
complete: classes, arrow functions, destructuring, template strings, `Proxy`, `JSON`,
`Math`, `Date`, regular expressions, `Map`/`Set`, generators, promises.

### Works

| API | Notes |
|---|---|
| `document.getElementById`, `document.querySelector`, `document.querySelectorAll`, `document.body` | simple selectors (tag, `.class`, `#id`, descendant) |
| `document.createElement`, `createTextNode`, `el.appendChild`, `el.append`, `el.removeChild`, `el.remove()` | an element is built detached and serialised to HTML when appended to a live one; it is cascaded like page markup and gets an id if it had none, then forwards its writes by id |
| element `.style.prop = v` | any CSS property from the list above |
| `.innerHTML =`, `.textContent =`, `.innerText =` | a fragment is parsed and cascaded with the page's stylesheet |
| `.className`, `.classList.add/remove/toggle/contains` | re-cascades the element |
| `.getAttribute/.setAttribute`, `.dataset`, `.tagName` | attributes on the page tree, SVG shape attributes included (set an expression at runtime) |
| `.clientWidth/.clientHeight/.offsetWidth` | layout sizes, snapshotted per frame |
| `setTimeout`, `setInterval`, `requestAnimationFrame` | rAF is capped at 30 Hz |
| `console.log/error` | to the BepInEx log |
| `addEventListener('data', e => e.detail)`, `window.ondata` | the Lua `data` payload, as an object |
| `performance.now()` | |

**Cost model:** every DOM write re-translates the page and, if the scene text changed,
resends it to the vector mod (a parse of ~40 KB for the gas console). Cheap at data
rates (0.5 s), wrong at frame rates. JS decides *what* is shown; expressions move it.

### Does not work

| API | Why |
|---|---|
| Canvas 2D drawing (`getContext('2d')` and its calls) | recorded, never drawn in vector mode — see `<canvas>` above |
| `requestAnimationFrame` **as an animation loop** | it runs, at 30 Hz, but each frame's DOM writes re-emit the scene, which is the cost that killed the texture back-end. Use it for logic only |
| Pointer and keyboard events (`click`, `mousemove`, `keydown`) on elements | clicks on buttons go to Lua, not to page script; bounce them through `data` if the page needs them |
| `fetch`, `XMLHttpRequest`, `WebSocket` | no network from a page by design; data comes from the chip |
| `localStorage`, `sessionStorage`, `IndexedDB`, cookies | no persistence in the page; keep state in Lua or in JS variables. `localStorage`/`sessionStorage` exist as in-memory stores so a page using them runs; they forget on rebuild |
| `insertBefore`, `replaceChild`, `cloneNode`, reading `.innerHTML`/`.textContent` back, `parentElement`, `children` | the tree lives on the main thread; the worker only writes to it. Appending is at the end of the parent |
| `getComputedStyle`, `getBoundingClientRect` (beyond client sizes), CSSOM | no layout query API beyond the sizes above |
| Web Animations, `IntersectionObserver`, `ResizeObserver`, `MutationObserver` | not implemented |
| `window.location`, `history`, `navigator`, `alert`/`confirm`/`prompt`, `import()` | no browser, no modules. The globals exist as inert stubs (`alert` writes to the log, `confirm` answers true) so a script that touches them does not throw |
| Timers driving visual motion | same reason as rAF: a timer that writes the DOM every 30 ms re-emits the scene 30 times a second |

---

## The vector mod side — additions used, and the one still open

Everything above runs on the vector mod as it is, plus these additions it gained for this
front-end (all additive): `wrap=1`, `lh`, string escapes (0.10.1.0); `sh` on closed shapes
and on `T` (0.10.2.0).

Reported to the vector side on 2026-09-14 and fixed there by 0.11.20.0, both confirmed on
the 2x2 test page on 2026-09-15: the radial fill vertex count (page 53,482 → 11,403
vertices, so the button after the radial box no longer drops past the 60,000 cap) and the
`VectorSlice` error spam on capture (three captures, zero errors).

Still open, only if a page needs it: non-convex clips by convex decomposition of the clip
polygon on the existing geometric path. A stencil pass does not fit a one-mesh, one-material
renderer. Sixteen mote fields at nose distance reach the 60,000-vertex mesh cap (nothing
visible was lost at the closest the camera can get); 32-bit indices would lift it.

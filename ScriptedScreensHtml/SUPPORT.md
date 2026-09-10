# What works from HTML5, CSS and JS, and what does not — and why

State as of 2026-09-10, vector back-end. The pipeline decides everything below:

```
HTML + CSS  →  parse, cascade  →  UI Toolkit lays the boxes out (layout ONLY)
            →  emitter translates the laid-out boxes to the vector mod's scene text
            →  the vector mod draws it as geometry, off-thread, in the Fonts mod's faces
JS          →  runs on a worker thread; its DOM writes re-run the translation
```

So a feature "works" when all three agree: the CSS parser understands it, UI Toolkit can
lay it out, and the emitter has a vector node for it. Anything that needs **per-frame
painting** (canvas, JS animation loops) or **per-pixel effects** (blur, shadows, filters)
is structurally out, because the vector layer draws geometry, not pixels. Anything that
needs **pointer input** is out because nothing routes input into the vector layer.

The rule of thumb: **the page changes on data, motion is an expression.** JS and data
decide what is on screen; anything that moves every frame is an SVG attribute written as
`="…"` over `t` (seconds), `i` (instance) and `$name` (data), evaluated by the vector mod
with nothing else running.

---

## HTML

### Works

| Feature | Notes |
|---|---|
| `<html> <head> <body> <style> <script>` | one page per `html` element, `src` prop |
| `<meta name="viewport" content="width=768">` | design width; the page lays out at it and is stretched to the console |
| Block containers: `div section header footer main nav article aside p h1–h6 ul ol li` | every block is a flex container (see CSS) |
| Inline text: `b i u s span small big sub sup mark code font br` | rendered as rich text inside one label |
| Inline element **with an id** | kept as its own label so data and JS can target it |
| `id`, `class`, inline `style` | |
| Comments, entities (`&amp; &lt; &nbsp; &#x25BC;`) | |
| `<hr>` | a 1px rule |
| `<svg>` with `line polyline polygon rect circle ellipse path g defs linearGradient radialGradient` | translated one-to-one to vector nodes; `viewBox`, `preserveAspectRatio="none"`, `fill stroke stroke-width opacity fill-opacity stroke-opacity stroke-linecap stroke-linejoin`, `fill="url(#id)"` |
| SVG extensions | any attribute may be `="expression"`; `n="36"` on `polygon`/`polyline` makes a sampled band/line (`x y y2` per sample `i`); `n="42"` on `circle`/`rect`/`ellipse`/`path` repeats it (`hash(i)` for per-instance randoms); `fo2 fea fea_edge lod dash dofs` pass through to the vector layer |
| Data binding by id | `data = { id = value }` from Lua: string/number → text, table → CSS, bool → display; svg shape ids take `points`, attribute tables, or a number array spread across the viewBox; the same payload is forwarded flattened to the scene as `$a_b` |

### Does not work

| Feature | Why |
|---|---|
| `<canvas>` | its whole model is "script repaints pixels every frame". The vector layer draws geometry once and animates it with expressions. A canvas is laid out but draws nothing. Use `<svg>` with expressions |
| `<img>`, `background-image: url()` | not wired yet. The route is ScriptedScreens' own `image` element (URLs included), created through the same postfix route the vector element uses, positioned at the box's rect. Nothing needed in the vector mod |
| `<table>` | UI Toolkit has flexbox only, no table layout. Build tables from flex rows |
| `<input> <button> <select> <textarea> <form> <a>` | no input path into the vector layer. ScriptedScreens has its own `button`/`textinput` elements; layer them over the page. A vector node can carry `click`, so `<button>` could become one later |
| `<video> <audio>` | not wired yet; same route as `<img>`, onto ScriptedScreens' `media` and `sound` elements |
| `<iframe> <object>` | no meaning here |
| List markers on `<ul>/<ol>` | no marker generation; write the bullet |
| Text clipped to a rounded shape | a label under a rounded `overflow: hidden` box is clipped to the box's rectangle, not its rounded outline (the vector text layer masks with a rectangle). Standing limit; invisible at the radii dashboards use |

---

## CSS

### Works

**Selectors:** `tag`, `.class`, `#id`, compounds (`div.a#b`), descendant (`a b`), child
(`a > b`), adjacent sibling (`a + b`), selector lists (`a, b`), specificity and source
order, `!important`.

**Box model:** `width height min-* max-*` (px, %), `margin` and per-side, `padding` and
per-side, `position: absolute | relative`, `top right bottom left`, `inset`, `display:
none | flex | block` (block *is* flex-column here), `overflow: hidden` (a clip, rounded
corners honoured), `visibility`, `opacity` (whole subtree, group opacity).

**Flexbox:** `flex-direction`, `flex-wrap`, `flex`, `flex-grow/shrink/basis`,
`justify-content`, `align-items`, `align-self`, `align-content`.

**Paint:** `color`, `background`/`background-color`, `background: linear-gradient(...)`
(angle or `to side`, any number of stops, **hard stops** — two colours at one position — are
split geometrically so the edge is exact), `border` shorthand, per-side `border-*-width`
and `border-*-color`, `border-radius` and per-corner (CSS overflow clamp applied).

**Text:** `font-family` (any face in the Fonts mod's folder: `'Barlow'`, `'Barlow
SemiBold'`, game faces such as `'noto-punc'`), `font-size`, `font` shorthand,
`font-weight` (bold ≥ 600, not applied twice on a face that is already a named weight),
`text-align`, `white-space: nowrap`, `letter-spacing`, `text-overflow: ellipsis` (with
overflow hidden), **wrapping** (a label UI Toolkit laid out on more than one line wraps in
the vector layer too, vector mod 0.10.1.0), `line-height` (number, px, em or %), quotes,
backslashes and line breaks in text (escaped into the scene text).

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
| `display: grid` | UI Toolkit's layout engine (Yoga) is flexbox only. Use nested flex rows |
| `gap` | the Yoga version in Unity 2022 has no gap. Use margins (`.a + .a { margin-left }`) |
| `display: inline`, `inline-block`, `float`, inline flow | no inline formatting context. A block is either all text (one label, rich text) or all boxes (a flex container); mixed content becomes a row |
| `transition` on `color`/`background-color` | a colour is not a scalar in the vector expression language; colour changes snap. Layout, opacity and transform transitions are compiled to expressions over `t` (see Works) |
| `box-shadow`, `text-shadow`, `filter`, `backdrop-filter`, `blur` | per-pixel effects need an offscreen pass; the vector layer is geometry. The vector mod has a geometric shadow (`sh`) that `box-shadow` without blur could map to later |
| `radial-gradient` as a background | not mapped yet (SVG `<radialGradient>` works; the vector `GR` def exists, so this is a small emitter addition) |
| `border-style: dashed/dotted` | not mapped yet; the vector stroke has `dash`, small addition |
| `outline` | not mapped |
| `text-decoration`, `text-transform` | rich text `<u>`/`<s>` tags work; the CSS properties are not mapped (`text-transform` is trivial to add) |
| `z-index` | paint order is document order, in UI Toolkit and in the vector layer alike. Put the thing on top last |
| Units other than `px` and `%` | `em/rem/vw/vh` not parsed (`letter-spacing` accepts `em`) |
| `calc()`, `var()` custom properties, `@media`, `@font-face`, `@import` | no expression evaluation in the CSS parser; `var()` is substituted in Lua today; fonts come from the Fonts mod folder, not from CSS |
| Pseudo-classes (`:hover :active :nth-child :first-child`), pseudo-elements (`::before ::after`), attribute selectors (`[data-x]`), `~` | no pointer states exist, and generated content / structural pseudo-classes are not implemented (structural ones are feasible) |
| `background-size/position/repeat`, multiple backgrounds | no images |
| `cursor`, `user-select`, `pointer-events`, `scroll-*` | no input or scrolling |

---

## JavaScript

Runs on a **worker thread** in Jint (a .NET ES2023 engine). The language itself is
complete: classes, arrow functions, destructuring, template strings, `Proxy`, `JSON`,
`Math`, `Date`, regular expressions, `Map`/`Set`, generators, promises.

### Works

| API | Notes |
|---|---|
| `document.getElementById`, `document.querySelector`, `document.querySelectorAll` | simple selectors (tag, `.class`, `#id`, descendant) |
| element `.style.prop = v` | any CSS property from the list above |
| `.innerHTML =`, `.textContent =`, `.innerText =` | a fragment is parsed and cascaded with the page's stylesheet |
| `.className`, `.classList.add/remove/toggle/contains` | re-cascades the element |
| `.getAttribute/.setAttribute`, `.dataset` | attributes on the page tree, SVG shape attributes included (set an expression at runtime) |
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
| Pointer and keyboard events (`click`, `mousemove`, `keydown`) | no input reaches the vector layer. Buttons are ScriptedScreens elements; their events arrive in Lua |
| `fetch`, `XMLHttpRequest`, `WebSocket` | no network from a page by design; data comes from the chip |
| `localStorage`, `sessionStorage`, `IndexedDB`, cookies | no persistence in the page; keep state in Lua or in JS variables |
| `document.createElement`, `appendChild`, `removeChild` | the tree is built by the HTML parser; replace a subtree with `innerHTML` instead |
| `getComputedStyle`, `getBoundingClientRect` (beyond client sizes), CSSOM | no layout query API beyond the sizes above |
| Web Animations, `IntersectionObserver`, `ResizeObserver`, `MutationObserver` | not implemented |
| `window.location`, `history`, `navigator`, `alert`, `import()` | no browser, no modules |
| Timers driving visual motion | same reason as rAF: a timer that writes the DOM every 30 ms re-emits the scene 30 times a second |

---

## The vector mod side — additions wanted, none required

Everything above runs on the vector mod **as it is**. Additions that would close gaps,
all additive:

1. ~~`wrap=1` on `T`~~ — in 0.10.1.0, used.
2. ~~`\"` escapes inside quoted strings~~ — in 0.10.1.0, used (`\"`, `\\`, `\n`, `\t`).
3. ~~`lh` (line height) on `T`~~ — in 0.10.1.0, used.
4. Non-convex clips: convex decomposition of the clip polygon on the existing path, when a
   page needs one. A stencil pass does not fit a one-mesh, one-material renderer.
5. 32-bit mesh indices, or a gentler segment curve for small circles: sixteen mote fields
   at nose distance reach the 60,000-vertex cap (nothing visible was lost at the closest
   the camera can get, so this is a note, not a request).

# Writing a page for a console: what works from HTML, CSS and JS

State as of 2026-09-15 (vector mod 0.11.23.0). Everything in the
"works" tables below was seen on a console that day, on the verification pages
(`HtmlTest4` to `HtmlTest12` in the repository, https://github.com/Gruffuss/scripted-screens-html), on 2x2 and 3x3 consoles. The contract is simple: **a page is written exactly as for a
browser.** Anything a browser page does that fails here is a defect, not a convention, and
the two lists at the end say what is still being finished.

```
HTML + CSS  →  parse, cascade  →  the mod's own layout engine lays the boxes out (flexbox, grid, inline flow)
            →  the emitter translates the laid-out boxes to the vector mod's scene text
            →  the vector mod draws geometry off-thread, text in the Fonts mod's faces
JS          →  Jint on a worker thread; DOM writes land on the main thread and re-run the translation
```

**What that pipeline means for a page author**

- Static pages cost nothing after their first emit. Motion is written once and evaluated
  by the vector mod: CSS transitions, keyframes and scroll-driven animations become
  expressions; an SVG attribute may be an expression over `t` (seconds), `i` (instance)
  and `$name` (data) directly.
- Pointer input reaches the page: `:hover`, `:active`, `:focus`, `click`, `mousemove`
  and friends on any element; the cursor over a world console is the crosshair when the
  cursor is locked.
- Form controls are ScriptedScreens' own controls placed inside the element's box (its
  border and background are the page's). Images, video and audio are ScriptedScreens'
  `image`, `media` and `sound` elements, so URLs the game can fetch work.
- Network: `<link rel=stylesheet>`, `@import`, `<script src>` and module `import` from
  http(s) URLs are fetched (a private GitHub repo answers 404; make it public). Page
  `fetch()`/`XMLHttpRequest` are not provided: data comes from Lua through `data`.

---

## HTML

| Area | What works |
|---|---|
| Document | `html head body meta style script link title` (title is ignored); `<meta name="viewport" content="width=N">` sets the design width the page lays out at; the page is stretched to the console |
| Blocks | `div section article aside header footer main nav p h1-h6 pre blockquote address figure figcaption fieldset legend hr dl dt dd details summary dialog` with sensible defaults; a block is a flex column, `display: flex` a row, `display: grid` a grid |
| Inline text | `b strong i em u s span small big sub sup mark code kbd samp tt font br abbr cite q var time dfn del ins bdi wbr data strike ruby rt rp` as rich text inside one label; an inline element with an id, a class, or that is `img`/a control keeps its own element inside a wrapping row |
| Inline flow | text and inline elements between blocks flow as one line box (a `::before` beside its text, a span before a nested list); a floated span floats; `::first-letter` is a drop cap with the text flowing beside it |
| Lists | `ul ol li` with markers as a browser draws them: disc/circle/square as shapes, decimal/alpha/roman as text, `@counter-style` systems, `list-style-image`, `list-style-type: none`; nested lists |
| Tables | `table thead tbody tfoot tr td th caption col`, `colspan`, a `width` attribute, `colgroup`/`col` widths (`width` attribute or style, `span`); rows and sections are real elements (`tr:nth-child` striping, hover); `caption-side`, `empty-cells`; cells share a table with a width equally, size to content without one |
| Forms | `input` (text, number with min/max/step, checkbox, radio, range, email, url, password, search), `textarea` (rows/cols), `select` with `optgroup`, `button`, `label for`, `form` with `submit`, `progress`, `meter` (three colours), `fieldset`/`legend`; values reach Lua as `on_change("name=value")` and the page script as `input`/`change` events |
| Details, dialog | `<details open>` toggles on the summary click; `dialog.showModal()`/`close()`; `:open`, `:modal` |
| Popover | the `popover` attribute (hidden until shown), `popovertarget` buttons (`popovertargetaction` show/hide/toggle), `showPopover()`/`hidePopover()`/`togglePopover()`, `:popover-open`, drawn in the top layer with an optional `::backdrop` |
| Maps, lists, base | `<img usemap>` with `<map>`/`<area>` (rect, circle, poly; the area gets the click event), `<input list>` with `<datalist>` (the options open under the focused field, a click fills the value), `<base href>` for relative `src`, `href`, `url()` and `@import` |
| Media | `img` (`object-fit`, radius, `alt` ignored), `video`, `audio` (autoplay, loop, controls attributes as the ScriptedScreens elements allow); a `picture` renders its `img` and ignores its `source` candidates (see COVERAGE.md) |
| Inline SVG | see the SVG section |
| Canvas | see the JS section |
| Entities, comments | named, decimal and hex entities; comments dropped; whitespace collapsed as in HTML |
| Attributes | `id class style title lang dir hidden disabled required readonly placeholder checked selected open data-*`, `onclick` |

## SVG

An inline `<svg>` is resolved the way a browser resolves it, then written one shape to one
vector node.

| Area | What works |
|---|---|
| Shapes | `rect` (rx), `circle`, `ellipse`, `line`, `polyline`, `polygon`, `path` (full `d` syntax incl. arcs and relative commands) |
| Structure | `g`, `defs`, `symbol`, `use` (href to a shape or a symbol with its own viewBox, x/y/width/height), `clipPath` on shapes and groups (convex and concave), `title`/`desc`/`metadata` skipped |
| Presentation | attributes, inline `style`, and stylesheet rules on shape nodes (`.bar:nth-child(2) { fill }`, `svg circle { stroke }`, geometry too: `r`, `cx`, `x`, `d`...); `marker-start/mid/end` with `<marker>` (orient auto, markerUnits, viewBox, refX/Y); `paint-order: stroke`; `pathLength` (dashes scale to it); `shape-rendering: crispEdges`; `vector-effect: non-scaling-stroke`; `transform-origin` with `transform-box: fill-box`; `fill stroke stroke-width fill-opacity stroke-opacity opacity stroke-linecap stroke-linejoin stroke-dasharray stroke-dashoffset stroke-miterlimit fill-rule` inherit through groups as in SVG |
| Transforms | `transform` attribute with `translate rotate(a[,cx,cy]) scale skewX skewY matrix`, composed through nested groups |
| Gradients | `linearGradient`, `radialGradient` with stops (`offset`, `stop-color`, `stop-opacity`, inline style), `gradientUnits`; ids scoped per svg |
| Text | `text` and `tspan` (own x/y start a line; tspan `fill`/`font-weight`/`font-style` as rich text), `font-size` in viewBox units, `text-anchor`, `dominant-baseline`, `font-family`, `letter-spacing` |
| Image | `<image href>` with `preserveAspectRatio` (fill/contain/cover, and its xMin/xMid/xMax Y alignment) |
| Fit | `viewBox`, `preserveAspectRatio="none"` (non-uniform scale baked into coordinates so strokes keep one width) |
| Expressions | any attribute may be `="expression"` for the vector mod (`t`, `i`, `$data`, `hash`); `n="36"` on polygon/polyline is a sampled band/line, on other shapes a repeat; `fo2 fea fea_edge lod dash dofs ml sh sd sdo fr` pass through |
| Data binding | a shape with an id takes `data` from Lua: a string sets `points`, a map sets attributes, a number array becomes evenly spaced y values |

## CSS

### Cascade and selectors

| Area | What works |
|---|---|
| Rules | selector lists, specificity, source order, `!important`, inline `style`, tag defaults, CSS nesting with `&`, nested `@media`/`@supports`/`@layer`/`@container` |
| Combinators | descendant, `>`, `+`, `~` |
| Simple selectors | tag, `.class`, `#id`, `*`, attribute selectors (`[a]`, `=`, `~=`, `|=`, `^=`, `$=`, `*=`, `i` flag) |
| Pseudo-classes | `:root :first-child :last-child :only-child :nth-child() :nth-last-child() :nth-of-type() :nth-last-of-type() :first-of-type :last-of-type :only-of-type :empty :not() :is() :where() :has() :hover :active :focus :focus-visible :focus-within :checked :disabled :enabled :required :optional :read-only :read-write :placeholder-shown :default :indeterminate :valid :invalid :user-valid :user-invalid (after the field was changed) :popover-open :scope :in-range :out-of-range :open :modal :link :any-link :lang() :dir()`; `:visited`/`:target` never match |
| Pseudo-elements | `::before`/`::after` with `content` (strings, `attr()`, `counter()`, `counters()`), `::marker`, `::placeholder` (colour), `::first-letter`, `::first-line` (colour, size, weight, face), `::backdrop` (background and opacity of the dim layer behind a modal dialog or a styled popover, on top of the page), `::details-content` (the body of a details as one box), `::-webkit-scrollbar`, `-thumb`, `-track` |
| At-rules | `@media` (width/height/min/max/orientation/`not`/`and`/lists, decided against the design size), `@supports`, `@keyframes`, `@font-face` (a font file the Fonts mod has, by file name, or a link the Fonts mod may download - Google Fonts by default, including a Google Fonts `<link>`/`@import` stylesheet; other hosts only if the player adds them to the Fonts mod's `PageFontHosts` setting; `.ttf`/`.otf`, not WOFF2), `@import`, `@layer` (source order), `@scope (root)`, `@container` (size queries, named or not, answered by the nearest container above the element from its own laid-out box, re-cascaded when that box changes), `@property` (`initial-value`), `@counter-style`, `@starting-style` (top level and nested: the state a new element transitions from, on load and on insert) |
| Values | custom properties with `var()` and fallbacks (a `var()` naming no property in scope and with no fallback makes its declaration invalid and dropped, as in a browser; re-resolved through the subtree when a script changes a class or an attribute a rule selects on, so a theme switch on the root restyles the page), `inherit`, `initial`, `unset`, `revert`, `currentColor`, `calc()` (nested, and mixed `%` with `px` resolved against the containing block after layout), `min()`, `max()`, `clamp()`, `sin() cos() tan() asin() acos() atan() atan2()`, `pow() sqrt() hypot() log() exp()`, `abs() sign() mod() rem() round()`, `attr()` in content and, as `attr(name unit)` or `attr(name type(...))` with an optional fallback, in any other property (`width: attr(data-w px)`), `lh`/`rlh` (the element's and the root's line box: a declared `line-height`, else 1.2em), `env()` (safe-area insets are 0), `image-set()` (first candidate) |
| Units | `px em rem % vw vh vmin vmax ch ex cap ic cm mm in pt pc Q fr deg rad turn grad s ms` (`cap` 0.7em, `ic` 1em) |
| Colours | 148 named, `#rgb #rgba #rrggbb #rrggbbaa`, `rgb()/rgba()` (legacy and modern), `hsl()/hsla()`, `transparent`, `color-mix(in srgb, ...)`, `hwb()`, `lab()`, `lch()`, `oklab()`, `oklch()`, the relative colour syntax (`rgb(from red r g b / 50%)`, `hsl(from x calc(h + 120) s l)`, any of the functions), `light-dark()` by the cascade's `color-scheme` |
| Logical properties | `inline-size block-size min/max-*-size`, `margin/padding/border/inset-inline|block(-start|-end)`, `border-start-start-radius` and siblings, `overflow-inline/block`, `text-align: start/end`, `float: inline-start/end` (a horizontal, left-to-right page) |

### Layout

| Area | What works |
|---|---|
| Box model | `width height min-* max-* margin padding border box-sizing` (content-box is the default, as in CSS) |
| Display | `block inline inline-block flex inline-flex grid none contents -webkit-box` (an inline tag given `inline-block`, `inline-flex`, `inline-grid`, `block`, `flex`, `grid` or `table` inline becomes a box of its own inside its sentence); `visibility` |
| Flex | `flex-direction flex-wrap flex-flow flex flex-grow flex-shrink flex-basis justify-content align-items align-self align-content gap row-gap column-gap order place-items place-content place-self` |
| Grid | `auto` columns sized by their content, `fr` columns sharing the rest; `align-items`/`justify-items` and the `-self` forms (start, center, end, stretch) inside a cell; `grid-auto-rows: 1fr` rows equal in a grid without a height; `grid-template-columns/rows` (px % fr auto minmax repeat), `grid-template-areas`, `grid-template`, `grid-area` (name or lines), `grid-column/row(-start/-end)` incl. negative lines and `span`, `gap`, `grid-auto-rows/columns/flow`, auto placement, content-sized auto tracks |
| Position | `static relative absolute fixed sticky` (sticky inside a scrolling box), `top right bottom left inset`, `z-index` (across parents) |
| Overflow | `hidden`, `clip`, `auto`/`scroll` (a real scroll box: wheel and drag; `scroll` events, `scrollTop` and `scrollHeight` read the real offset the vector mod reports (0.11.24), `scrollIntoView`), drawn scrollbars styled by `scrollbar-width`, `scrollbar-color` or `::-webkit-scrollbar*` |
| Flex | a text node directly inside a flex or grid container is an anonymous item of its own (a lamp span followed by a word); a flex box holding only text places it by `justify-content`/`align-items` (buttons, tabs); `align-items: baseline` (bottoms aligned and each item lifted by its own descent, from the font size and line height); text in a box taller than its lines starts at the top, as a block does, and is centred only where the box is a flex container that centres its items |
| Float, columns | `float: left/right` with text flowing beside, `clear`, `column-count` or `column-width` (block children, or the words of a text block), `columns`, `column-gap`, `column-rule`, `column-span: all`, `column-fill` (accepted: fills like balance) |
| Motion path | `offset-path: path()` or `ray(angle size)` (from `offset-position`), the `offset` shorthand, `offset-anchor`, with `offset-distance` (px or %) and `offset-rotate` (`auto`, an angle, `auto` plus an angle); the distance animates through `transition` and `@keyframes` (the path between the two distances sampled 16 times into one expression over the tween's progress, the turn following the tangent) |
| Other | `aspect-ratio`, `-webkit-line-clamp`/`line-clamp` (ellipsis on the last line) |

### Paint

| Area | What works |
|---|---|
| Background | `background-color`, `background` shorthand, `background-image: url()` (`background-size` contain/cover/percent/px, `background-position`, `background-repeat` no-repeat), `linear-gradient` (angles, `to` keywords, positioned stops, hard stops), `radial-gradient` (shape, size keywords, position), `conic-gradient` (`from`, `at`), `repeating-linear-gradient`, `background-clip: text` with a gradient |
| Backgrounds | layered `background` (`linear-gradient(...) left bottom/100% 3px no-repeat, #000`: the colour layer under gradient layers each at its own position and size; a repeat draws once); `background-origin` (padding-box, content-box, border-box), `background-position-x/y` names; `repeating-linear-gradient` with px and two-position stops, and axis-aligned hard-stop stripes drawn as a repeat that marches when `@keyframes` moves `background-position` |
| Corners | `corner-shape` with one to four values (bevel, scoop, notch, square, round), `corner-top-left-shape` and the other corner and side names, the logical `corner-start-start-shape` family |
| Masks | `mask-image` gradients with `mask-size`, `mask-position`, `mask-origin`, `mask-clip` (`mask-repeat` accepted) |
| Borders | `border` and every per-side longhand (`width`, `color`, `style` incl. per side), `solid dashed dotted double inset outset groove ridge none hidden`, `border-radius` per corner incl. elliptical, `corner-shape: bevel/scoop/notch`, `border-image` (gradient source as a gradient frame, image source as nine slices in percent), `outline` (`width style color offset`) |
| Shadows | `box-shadow` (offset, blur, spread, colour, `inset`, several), `text-shadow` (several), `filter: drop-shadow()` |
| Effects | `opacity`, `filter: brightness contrast saturate hue-rotate grayscale sepia invert drop-shadow` (on the subtree), `clip-path: inset() rect() xywh() circle() ellipse() polygon() path()` (also concave; a path is flattened to its outline) and the reference boxes border-box / padding-box / content-box / fill-box / stroke-box / view-box, alone or beside a shape, `mask-image: linear-gradient(...)`, `mix-blend-mode` and `backdrop-filter` accepted without effect (per-pixel) |
| Transforms | `transform` with `translate scale rotate skew matrix` (and the X/Y/3d spellings; `rotateX/Y` as their flat foreshortening, `perspective` ignored), `transform-origin`, `backface-visibility: hidden` |

### Text

| Area | What works |
|---|---|
| Fonts | any face registered with the game's text engine, by name (the Fonts mod's faces, including the player's own files, and the game's faces); the layout measures with that same face, so this mod reads no font files; the layout measures with the face the scene draws (`font-weight: 700` on Barlow Condensed lays out in Barlow Condensed Bold, not a synthetic bold of the regular face); a glyph the face lacks (Barlow has no subscript digits, no gear) is drawn from the game's own `font_english` face, as a browser falls back to a system font; generic families (`sans-serif`, `serif`, `monospace`, `system-ui`) map to a face inside rich text too; `font-family` resolves the Fonts mod's font files (family + weight + style, e.g. `Barlow`, `Barlow SemiBold`, `Barlow Condensed`), then registered TextMeshPro faces, then generic families mapped to what is installed (`monospace` to `code`); `font-size` (px em rem % keywords), `font-weight` (numeric weights pick real faces), `font-style`, `font` shorthand, `@font-face` aliases (a file the Fonts mod has, or a link it downloads; the page lays out again when the face arrives) |
| Layout | `text-overflow: ellipsis` only where declared (a clipped label without it is clipped by its box, as in a browser); `font-variant-numeric: tabular-nums` widens the label's box by what the digit cells add, so a unit after a number does not overlap it; `line-height` (a label's box is its line count times the line height, as a browser's line boxes), `letter-spacing`, `word-spacing`, `text-align` (incl. `justify`), `text-align-last`, `text-indent`, `white-space` (normal, nowrap, pre), `word-break: break-all`, `overflow-wrap`, `text-overflow: ellipsis`, `writing-mode: vertical-rl/lr, sideways-*`, `vertical-align` (sub, super, offsets) |
| Decoration | `text-decoration` and `-line/-color/-style/-thickness`, `text-underline-offset` (underline, overline, line-through; solid, double, dotted, dashed, wavy), `text-transform`, `font-variant-numeric: tabular-nums`, `color` |
| Lists, counters | `list-style`, `list-style-type` (incl. `@counter-style` names), `list-style-image`, `list-style-position`, `counter-reset`, `counter-increment`, `counter-set`, `counter()`, `counters()` |

### Motion and interaction

| Area | What works |
|---|---|
| Transitions | `transition` on size, position, opacity, transform, colours and `offset-distance`, a script's `style` writes reach the emitter for every property (gradients, masks, clips...), `transition-behavior: allow-discrete` (a `display: none` waits for the transition), `@starting-style` (`transition-property/duration/delay/timing-function`, `steps()`, `cubic-bezier()`); compiled to expressions, no per-frame work |
| Keyframes | `@keyframes` with `animation-name/duration/delay/iteration-count/direction/fill-mode/play-state/timing-function/composition` (`add` and `accumulate` compose a frame's transform and opacity onto the element's own; a compiled loop composes the same way), `Element.animate()`, `style.animation` written by a script |
| Scroll-driven | `animation-timeline: scroll()` and `view()` over opacity and 2D transforms, evaluated from the scroll offset |
| Pointer | `:hover` (follows the cursor, or the crosshair), `:active` (while pressed), `:focus`/`:focus-within` (after a click), `pointer-events: none`, `cursor` accepted |

### Accepted without effect

These parse without a warning and change nothing here, because there is no printer, no
pointer physics, no font hinting and no snap physics: `scroll-snap-*`, `scroll-margin*`,
`scroll-padding*`, `scroll-behavior`, `overscroll-behavior*`, `will-change`, `contain`,
`content-visibility`, `isolation`, `touch-action`, `-webkit-font-smoothing`,
`text-rendering`, `image-rendering`, `color-scheme`, `zoom`, `all`, `text-wrap`,
`text-size-adjust`, `-webkit-tap-highlight-color`, `print-color-adjust`, `resize`,
`caret-color`, `tab-size`, `orphans`, `widows`, `page-break-*`, `break-*`,
`unicode-bidi`, `direction`, `font-kerning`, `font-feature-settings`,
`font-optical-sizing`, `font-synthesis`, `font-stretch`, `font-variant*`, `quotes`,
`hanging-punctuation`, `background-attachment`, `perspective*`,
`transform-style`, `ruby-*`, `appearance`, `user-select`, `accent-color`.

Coverage measured against the MDN list: 285 of 491 standard CSS properties handled
(`ScriptedScreensHtml.Tests/inventory.py`).

## JavaScript

Page scripts run on a worker thread (Jint). A script frame has 15 seconds before it is
stopped, which only a game load ever approaches.

| Area | What works |
|---|---|
| Loading | inline `<script>`, `<script src=URL>`, `<script type="module">` with `import ... from "https://..."` (named and default exports), `DOMContentLoaded`, `load`, `readyState` |
| Lookup | `getElementById`, `querySelector(All)` (the same selectors as CSS), `getElementsByClassName/TagName/Name`, `closest`, `matches`, `contains` |
| Tree | `createElement`, `createTextNode`, `createDocumentFragment`, `appendChild`, `append`, `prepend`, `before`, `after`, `insertAdjacentHTML/Element/Text`, `replaceWith`, `replaceChildren`, `removeChild`, `remove`, `cloneNode`, `innerHTML` (read and write, inline text or built elements), `outerHTML`, `textContent`, `innerText`, `children`, `childNodes`, `firstChild`, `parentElement`, `nextElementSibling`, `previousElementSibling`; a read right after a write sees the write |
| Attributes, style | `getAttribute`, `setAttribute`, `removeAttribute`, `hasAttribute`, `toggleAttribute`, `dataset`, `id`, `className`, `classList` (add/remove/toggle/contains/replace), `hidden`, `disabled`, `value`, `checked`, `open`, `style.x = ...`, `style.cssText`, `setProperty`, `getPropertyValue`, `getComputedStyle` |
| Geometry | `getBoundingClientRect`, `clientWidth/Height`, `offsetWidth/Height`, `scrollWidth/Height`, `scrollTop` (read/write), `scrollTo`, `scrollBy`, `scrollIntoView`, `innerWidth/Height`, `matchMedia`. **A size is the last drawn layout's**: a page that changes something and wants the new size reads it on the next frame, where a browser would lay the page out on the spot. `window.innerWidth/Height` are the page's own design size and are right from the first line of the script. |
| Events | `addEventListener`/`removeEventListener`, `on*` properties, bubbling and capture, `preventDefault`, `stopPropagation`, `Event`, `CustomEvent`, `dispatchEvent`; `click`, `mousedown/up/move/over/out/enter/leave`, `pointer*`, `input`, `change`, `submit`, `keydown` (not delivered: no keyboard focus), `data` from Lua (`window.ondata` or a `data` event) |
| Timers | `setTimeout`, `setInterval`, `requestAnimationFrame` (once per game frame; the console redraws up to the vector mod's rate limit), `requestIdleCallback`, `queueMicrotask`, `Promise` |
| Web APIs | `localStorage`/`sessionStorage` (in memory), `URL`, `URLSearchParams`, `TextEncoder/Decoder`, `crypto.randomUUID/getRandomValues`, `structuredClone`, `JSON`, `Intl` basics, `performance.now`, `console.*` (to the BepInEx log), `alert` (to the log), `navigator`, `location`, `history` (inert), `screen`, `Image`, `Audio`, `MutationObserver`/`ResizeObserver`/`IntersectionObserver` (inert stubs), `Element.animate()` |
| Canvas | `getContext('2d')` records every call and the frame is translated to vector paths: paths (`moveTo lineTo arc arcTo ellipse bezierCurveTo quadraticCurveTo rect roundRect closePath`), `fill` (nonzero/evenodd), `stroke`, `fillRect strokeRect clearRect`, `fillText strokeText measureText` (font, align, baseline, rotation), `clip`, `save/restore`, `translate rotate scale transform setTransform resetTransform`, `lineWidth lineCap lineJoin setLineDash lineDashOffset miterLimit`, `globalAlpha`, `shadow*`, `createLinearGradient/RadialGradient/ConicGradient`, `drawImage`; a static drawing costs nothing after its first frame, a `requestAnimationFrame` loop costs one emit per frame |

Not provided, by decision: `fetch`, `XMLHttpRequest`, `WebSocket`, `eval`-loaded
third-party libraries that need a real DOM, `Worker`. Data comes from Lua.

## Diagnostics

`BepInEx/config/gruffuss.stationeers.scriptedscreens.html.cfg`: `Diagnostics.Enabled`
prints a line per page per second (emits, layout and translate ms, nodes, tweens, script
ms); `Diagnostics.DumpScenes` writes the exact scene text to `scenes/<page id>.txt` beside
the DLL. A page whose scene the vector mod refuses shows a magenta hatched frame, one stripe
per problem, with the reason in the log. Script errors are logged with their message and
stack. Unsupported CSS is logged once per property as `css: ... not supported`.

## Being finished

Nothing is pending on the vector side: items 1 to 17 are in vector mod 0.11.23 and seen
working from a page.

Approximations in this layer, listed to be replaced, not kept:

- `::after` content is generated before the element's children, so a `counter()` in it does
  not see increments by descendants.
- `ruby`: the annotation is small and raised after its base, not stacked above it.
- `border-image` with `px`/number slices reads them as thirds (percent slices are exact).
- `object-position` keywords, percentages and calc() of percentages place the picture exactly, in
  every syntax including `right 10% bottom 20%`. A length offset needs the picture's own size, so the
  picture is placed from that edge and the page is told. On `<video>`, object-fit and object-position
  are not drawn: a video is ScriptedScreens' own media element.
- `text-decoration` colour, thickness, offset and style are drawn on single-line labels;
  a wrapped label keeps the plain underline.
- `rotateX`/`rotateY` are the flat foreshortening, no perspective.
- `animation-timeline` covers opacity and 2D transform functions; `animation-range` is ignored.
- `word-break: break-all` estimates its wrapped height in the layout from an average glyph width.
- `:hover` uses the page rect, not a raycast: something standing between the player and
  the console does not block it.
- An inline-block box beside text (a small LED span) sits at the bottom of the line, not on
  the text baseline; `vertical-align` on it is not applied.

Out, with the reason: `::selection` (no text selection), `@page` (print),
`@view-transition` (no document navigation), `shape-outside` (no inline formatting context
around floats), MathML (no user), per-pixel effects (`filter: blur()`, `backdrop-filter`,
`mix-blend-mode`: the vector layer draws geometry, not pixels), network from a page.

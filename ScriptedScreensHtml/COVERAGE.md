# What works, what does not, and why (2026-09-16)

One file. The counts and the item names are read from the code by `ScriptedScreensHtml.Tests/coverage.py` (run it after a change), against MDN's CSS data, the HTML living standard's element list and a list of the DOM and Web APIs a page script commonly uses; the reasons come from `ScriptedScreensHtml.Tests/reasons.py`, written by hand. "Handled" means the code names it; whether it behaves as a browser is what the console pages and examples check (`SUPPORT.md`).

Kinds: **build** = not done yet, nothing in the way. **approximation** = drawn, but not as a browser draws it, listed to be replaced. **out** = decided against, with the reason; reopen by deciding otherwise. **fine** = a browser does no more.


## Summary

| Area | Handled | Of | Share |
|---|---|---|---|
| CSS properties (standard) | 343 | 491 | 69% |
| CSS at-rules | 13 | 18 | 72% |
| CSS pseudo-classes and pseudo-elements | 56 | 98 | 57% |
| CSS functions | 78 | 95 | 82% |
| CSS units | 24 | 32 | 75% |
| HTML elements | 97 | 112 | 86% |
| JavaScript: document | 34 | 35 | 97% |
| JavaScript: element | 96 | 100 | 96% |
| JavaScript: window | 65 | 80 | 81% |
| JavaScript: canvas | 60 | 60 | 100% |

## CSS properties, by specification group

| Group | Handled | Not handled | Kind | Why, and what closes it |
|---|---|---|---|---|
| CSS Backgrounds and Borders | 41 of 61 | `background-origin`, `background-position-x`, `background-position-y`, `border-shape`, `corner-block-end-shape`, `corner-block-start-shape`, `corner-bottom-left-shape`, `corner-bottom-right-shape`, `corner-bottom-shape`, `corner-end-end-shape`, `corner-end-start-shape`, `corner-inline-end-shape`, `corner-inline-start-shape`, `corner-left-shape`, `corner-right-shape`, `corner-start-end-shape`, `corner-start-start-shape`, `corner-top-left-shape`, `corner-top-right-shape`, `corner-top-shape` | build | `background-position-x/-y` are longhands the shorthand already covers: add the two names. `background-origin` moves the image box to the padding or content edge: an offset in the image emit. The per-corner `corner-*-shape` and `border-shape` need the 1-to-4 expansion `border-radius` has; `corner-shape` for all four corners is done. |
| CSS Masking | 3 of 19 | `clip-rule`, `mask-border`, `mask-border-mode`, `mask-border-outset`, `mask-border-repeat`, `mask-border-slice`, `mask-border-source`, `mask-border-width`, `mask-clip`, `mask-composite`, `mask-mode`, `mask-origin`, `mask-position`, `mask-repeat`, `mask-size`, `mask-type` | build | gradient masks are done (vector item 10). An image mask (`mask-image: url()`) needs a vector mask from a texture's alpha, not asked for yet. The geometry longhands (`mask-position/size/repeat/origin/clip`) are the same offsets `background-*` uses. `clip-rule` is `fill-rule` for clip paths: pass `fr` on the CP def. `mask-border-*` is nine-slice for masks, on top of `border-image`. |
| Scalable Vector Graphics | 15 of 31 | `cx`, `cy`, `d`, `marker`, `marker-end`, `marker-mid`, `marker-start`, `paint-order`, `path-length`, `r`, `rx`, `ry`, `shape-rendering`, `vector-effect`, `x`, `y` | build | `fill`/`stroke` as CSS are done; the geometry properties (`cx cy r rx ry x y d`) need the same treatment in the svg collector. `marker-start/mid/end` (arrowheads) need `<marker>`: the marker's shapes emitted at each vertex, turned by the segment's angle. `vector-effect: non-scaling-stroke` is what a baked fit already does; needs the flag read. |
| CSS Animations | 10 of 21 | `animation-trigger`, `timeline-trigger`, `timeline-trigger-activation-range`, `timeline-trigger-activation-range-end`, `timeline-trigger-activation-range-start`, `timeline-trigger-active-range`, `timeline-trigger-active-range-end`, `timeline-trigger-active-range-start`, `timeline-trigger-name`, `timeline-trigger-source`, `trigger-scope` | out | `animation-trigger` and the `timeline-trigger-*` family are a 2025 draft no browser ships. |
| CSS Fonts | 13 of 24 | `font-language-override`, `font-palette`, `font-size-adjust`, `font-synthesis-small-caps`, `font-synthesis-style`, `font-synthesis-weight`, `font-variant-alternates`, `font-variant-east-asian`, `font-variant-emoji`, `font-variant-position`, `font-variation-settings` | out | the Fonts mod builds static atlases: no variable axes (`font-variation-settings`), no OpenType features (`font-variant-*`), no colour palettes. `font-variant-numeric: tabular-nums` was the one that mattered and is faked with a monospaced digit span. |
| CSS Box Sizing | 8 of 17 | `column-height`, `column-width`, `column-wrap`, `contain-intrinsic-block-size`, `contain-intrinsic-height`, `contain-intrinsic-inline-size`, `contain-intrinsic-size`, `contain-intrinsic-width`, `frame-sizing` | build | `column-width` (column count from a width) is arithmetic on top of `column-count`. `contain-intrinsic-*` only matters with `content-visibility`, which is a no-op here; `frame-sizing` is a 2025 draft. |
| CSS Text | 14 of 23 | `hyphenate-character`, `hyphenate-limit-chars`, `line-break`, `text-autospace`, `text-fit`, `text-justify`, `text-wrap-mode`, `text-wrap-style`, `white-space-collapse` | build | `white-space-collapse` and `text-wrap-mode` are the new longhands of `white-space`: map them. `hyphens: auto` needs a dictionary: out. `text-justify` variants: the text engine has one justification. `text-wrap-style: balance/pretty`: no control over the line breaker. |
| CSS Basic User Interface | 12 of 19 | `caret`, `caret-animation`, `caret-shape`, `interactivity`, `interest-delay`, `interest-delay-end`, `interest-delay-start` | out | no caret (typing happens in ScriptedScreens' own control); `interactivity` and `interest-delay-*` are 2025 drafts. |
| CSS Inline | 3 of 10 | `alignment-baseline`, `baseline-shift`, `baseline-source`, `initial-letter`, `text-box`, `text-box-edge`, `text-box-trim` | build | `initial-letter` is the drop cap with a line count: the `::first-letter` machinery exists, the size is lines times line-height. `baseline-shift` and `alignment-baseline` are `vertical-align` spellings. `text-box-*` (leading trim) needs font metrics the text engine does not expose. |
| CSS Text Decoration | 9 of 14 | `text-decoration-inset`, `text-emphasis`, `text-emphasis-color`, `text-emphasis-position`, `text-emphasis-style` | build | `text-emphasis-*` are small marks above each character: a second label of dots; low value. `text-decoration-inset` is new and rarely used. |
| CSS Overflow | 9 of 13 | `overflow-clip-margin`, `scroll-axis-lock`, `scroll-marker-group`, `scroll-target-group` | build | `overflow-clip-margin` is a larger clip rect: small. Scroll markers and `scroll-axis-lock` are 2025 drafts (carousel dots). |
| Filter Effects | 2 of 6 | `color-interpolation-filters`, `flood-color`, `flood-opacity`, `lighting-color` | out | SVG filter primitives (`<filter>`, feGaussianBlur, flood, lighting) are per-pixel; out with `filter: blur()`: the page is geometry, not pixels. |
| CSS Shapes | 0 of 3 | `shape-image-threshold`, `shape-margin`, `shape-outside` | out | text flowing around a float's outline needs an inline formatting context that breaks lines around shapes. The layout engine has none: text lives in labels, labels are boxes. This is the structural limit behind every inline approximation in this file. |
| CSS View Transitions | 0 of 3 | `view-transition-class`, `view-transition-name`, `view-transition-scope` | out | no document navigation, so no view transitions. |
| Motion Path | 3 of 6 | `offset`, `offset-anchor`, `offset-position` | build | `offset-path` places the box at `offset-distance` along a `path()`, turned by `offset-rotate`. `offset` is their shorthand: split it. `offset-anchor` moves which point of the box sits on the path (the centre today); `offset-position` is the start for `ray()`, not read. |
| CSS Display | 3 of 5 | `reading-flow`, `reading-order` | out | `reading-flow`/`reading-order` set keyboard focus order; a page has no keyboard. |
| CSS Multi-column Layout | 6 of 8 | `column-fill`, `column-span` | build | `column-rule` is a left border on every column but the first. `column-span: all` breaks an element out of the columns: rebuild the columns around it. `column-fill: balance` is what exists; `auto` fills the first column first. |
| CSS Overscroll Behavior | 3 of 5 | `overscroll-behavior-block`, `overscroll-behavior-inline` | fine | logical spellings of `overscroll-behavior`, which is a no-op here (no overscroll physics). |
| MathML | 0 of 2 | `math-depth`, `math-style` | out | no user for MathML. |
| CSS Color | 5 of 6 | `dynamic-range-limit` | out | `dynamic-range-limit` is an HDR display hint. |
| CSS Flexible Box Layout | 7 of 8 | `flex-line-count` | out | `flex-line-count` is a 2025 draft. |
| CSS Fragmentation | 5 of 6 | `box-decoration-break` | out | `box-decoration-break` only matters when a box fragments across lines or pages; inline boxes here do not fragment. |
| CSS Grid Layout | 14 of 15 | `grid` | build | `grid` is the shorthand of `grid-template` plus the auto-flow parts: parse and split it. |
| CSS Images | 3 of 4 | `image-orientation` | out | `image-orientation: from-image` reads EXIF; the image element has no EXIF access. |
| CSS Paged Media | 0 of 1 | `page` | out | print. |
| CSS Ruby | 2 of 3 | `ruby-overhang` | out | `ruby-overhang` tunes ruby stacking, and ruby is not stacked here (see approximations). |
| CSS Scroll Anchoring | 0 of 1 | `overflow-anchor` | out | scroll anchoring is a browser scrolling heuristic; the scroll box here keeps its offset. |
| CSS Transforms | 9 of 10 | `transform-box` | build | `transform-box: fill-box` changes the transform origin box for svg content: small. |
| CSS Transitions | 5 of 6 | `transition-behavior` | build | `transition-behavior: allow-discrete` lets `display` transition; the tween system could hold `display` until the tween ends. |
| CSS Writing Modes | 4 of 5 | `text-combine-upright` | out | `text-combine-upright` packs characters in vertical text; vertical text here is a rotated label. |
| Compositing and Blending | 2 of 3 | `background-blend-mode` | out | `background-blend-mode` is per-pixel compositing. |

Complete groups: CSS Anchor Positioning, CSS Box Alignment, CSS Box Model, CSS Cascading and Inheritance, CSS Conditional Rules, CSS Containment, CSS Generated Content, CSS Lists and Counters, CSS Logical Properties and Values, CSS Positioned Layout, CSS Scroll Snap, CSS Scrollbars Styling, CSS Table, CSS Viewport, CSS Will Change, Pointer Events.

## CSS at-rules

Handled: `@-webkit-keyframes`, `@charset`, `@container`, `@counter-style`, `@font-face`, `@import`, `@keyframes`, `@layer`, `@media`, `@property`, `@scope`, `@starting-style`, `@supports`

| Not handled | Kind | Why |
|---|---|---|
| `@font-feature-values` | out | OpenType features and palettes: see fonts. |
| `@font-palette-values` | out | OpenType features and palettes: see fonts. |
| `@namespace` | out | XML namespaces in selectors; nobody writes them for HTML. |
| `@page` | out | print. |
| `@view-transition` | out | no document navigation. |

## CSS selectors

Handled pseudo-classes: `:active`, `:any-link`, `:checked`, `:default`, `:dir`, `:disabled`, `:empty`, `:enabled`, `:first-child`, `:first-of-type`, `:focus`, `:focus-visible`, `:focus-within`, `:has`, `:hover`, `:in-range`, `:indeterminate`, `:invalid`, `:is`, `:lang`, `:last-child`, `:last-of-type`, `:link`, `:matches`, `:modal`, `:not`, `:nth-child`, `:nth-last-child`, `:nth-last-of-type`, `:nth-of-type`, `:only-child`, `:only-of-type`, `:open`, `:optional`, `:out-of-range`, `:placeholder-shown`, `:read-only`, `:read-write`, `:required`, `:root`, `:target`, `:user-invalid`, `:user-valid`, `:valid`, `:visited`, `:where`

Handled pseudo-elements: `::-webkit-scrollbar`, `::-webkit-scrollbar-thumb`, `::-webkit-scrollbar-track`, `::after`, `::backdrop`, `::before`, `::first-letter`, `::first-line`, `::marker`, `::placeholder`

| Not handled | Kind | Why |
|---|---|---|
| `::checkmark` | out | parts of browser-native widgets. The select's arrow and the checkbox's mark are drawn here and could take colours from these rules later. |
| `::cue` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `::details-content` | build | `::details-content` styles the body of a details element, which is an element already: match it. |
| `::file-selector-button` | out | parts of browser-native widgets. The select's arrow and the checkbox's mark are drawn here and could take colours from these rules later. |
| `::grammar-error` | out | no text selection, so nothing to style. |
| `::highlight` | out | no text selection, so nothing to style. |
| `::part` | out | web components and shadow DOM: the renderer has one tree, no custom elements. |
| `::picker` | out | parts of browser-native widgets. The select's arrow and the checkbox's mark are drawn here and could take colours from these rules later. |
| `::picker-icon` | out | parts of browser-native widgets. The select's arrow and the checkbox's mark are drawn here and could take colours from these rules later. |
| `::selection` | out | no text selection, so nothing to style. |
| `::slotted` | out | web components and shadow DOM: the renderer has one tree, no custom elements. |
| `::spelling-error` | out | no text selection, so nothing to style. |
| `::target-text` | out | no text selection, so nothing to style. |
| `::view-transition` | out | view transitions. |
| `::view-transition-group` | out | view transitions. |
| `::view-transition-image-pair` | out | view transitions. |
| `::view-transition-new` | out | view transitions. |
| `::view-transition-old` | out | view transitions. |
| `:active-view-transition` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:active-view-transition-type` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:autofill` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:buffering` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:defined` | out | web components and shadow DOM: the renderer has one tree, no custom elements. |
| `:first` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:fullscreen` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:future` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:has-slotted` | out | web components and shadow DOM: the renderer has one tree, no custom elements. |
| `:host` | out | web components and shadow DOM: the renderer has one tree, no custom elements. |
| `:host-context` | out | web components and shadow DOM: the renderer has one tree, no custom elements. |
| `:left` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:muted` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:past` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:paused` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:picture-in-picture` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:playing` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:popover-open` | build | `popover` attribute support would make `:popover-open` the same as `:open`. |
| `:right` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:scope` | build | inside `querySelector` and `@scope` it is the root (done there); as a bare selector it is `:root`. One case. |
| `:seeking` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:stalled` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:state` | out | web components and shadow DOM: the renderer has one tree, no custom elements. |
| `:volume-locked` | out | media state of ScriptedScreens' media element is not readable from the page. |

## CSS functions

Handled: `abs`, `acos`, `asin`, `atan`, `atan2`, `attr`, `blur`, `brightness`, `calc`, `circle`, `clamp`, `color`, `color-mix`, `conic-gradient`, `contrast`, `cos`, `counter`, `counters`, `cubic-bezier`, `drop-shadow`, `ellipse`, `exp`, `fit-content`, `grayscale`, `hsl`, `hue-rotate`, `hypot`, `image`, `inset`, `invert`, `layer`, `linear`, `linear-gradient`, `log`, `matrix`, `matrix3d`, `max`, `min`, `minmax`, `mod`, `opacity`, `path`, `perspective`, `polygon`, `pow`, `radial-gradient`, `rect`, `rem`, `repeating-conic-gradient`, `repeating-linear-gradient`, `repeating-radial-gradient`, `rgb`, `rotate`, `rotate3d`, `rotateX`, `rotateY`, `rotateZ`, `round`, `saturate`, `scale`, `scale3d`, `scaleX`, `scaleY`, `sepia`, `sign`, `sin`, `skew`, `skewX`, `skewY`, `sqrt`, `steps`, `symbols`, `tan`, `translate`, `translate3d`, `translateX`, `translateY`, `var`

| Not handled | Kind | Why |
|---|---|---|
| `alpha()` | build | `image-set` picks a URL by resolution: take the first. `xywh()` is a `clip-path` rect form. `ray()` belongs to motion paths. `alpha` is the relative-colour syntax (`rgb(from ...)`). |
| `cross-fade()` | out | `paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft. |
| `env()` | build | safe-area insets: return 0. |
| `hwb()` | build | colour spaces: the conversion maths into sRGB; `color-mix(in oklch)` then becomes exact instead of sRGB. |
| `image-set()` | build | `image-set` picks a URL by resolution: take the first. `xywh()` is a `clip-path` rect form. `ray()` belongs to motion paths. `alpha` is the relative-colour syntax (`rgb(from ...)`). |
| `lab()` | build | colour spaces: the conversion maths into sRGB; `color-mix(in oklch)` then becomes exact instead of sRGB. |
| `lch()` | build | colour spaces: the conversion maths into sRGB; `color-mix(in oklch)` then becomes exact instead of sRGB. |
| `light-dark()` | build | pick the light value, or by `color-scheme`. |
| `oklab()` | build | colour spaces: the conversion maths into sRGB; `color-mix(in oklch)` then becomes exact instead of sRGB. |
| `oklch()` | build | colour spaces: the conversion maths into sRGB; `color-mix(in oklch)` then becomes exact instead of sRGB. |
| `paint()` | out | `paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft. |
| `palette-mix()` | out | `paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft. |
| `param()` | out | `paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft. |
| `ray()` | build | `image-set` picks a URL by resolution: take the first. `xywh()` is a `clip-path` rect form. `ray()` belongs to motion paths. `alpha` is the relative-colour syntax (`rgb(from ...)`). |
| `scaleZ()` | approximation | 3D spellings take their flat projection: `rotateX/Y` as foreshortening, Z ignored. A true perspective projection needs depth the vector layer does not have. |
| `translateZ()` | approximation | 3D spellings take their flat projection: `rotateX/Y` as foreshortening, Z ignored. A true perspective projection needs depth the vector layer does not have. |
| `xywh()` | build | `image-set` picks a URL by resolution: take the first. `xywh()` is a `clip-path` rect form. `ray()` belongs to motion paths. `alpha` is the relative-colour syntax (`rgb(from ...)`). |

## CSS units

Handled: `%`, `ch`, `cm`, `deg`, `em`, `ex`, `fr`, `grad`, `in`, `mm`, `ms`, `pc`, `pt`, `px`, `q`, `rad`, `rem`, `s`, `turn`, `vh`, `vmax`, `vmin`, `vw`, `x`

| Not handled | Kind | Why |
|---|---|---|
| `Hz` | out | resolution and frequency units only appear in media queries this layer does not need. |
| `Q` | build | `Q` is a quarter millimetre; `cap` and `ic` need the font's cap height and ideographic advance: approximate as 0.7em and 1em. |
| `cap` | build | `Q` is a quarter millimetre; `cap` and `ic` need the font's cap height and ideographic advance: approximate as 0.7em and 1em. |
| `dpcm` | out | resolution and frequency units only appear in media queries this layer does not need. |
| `dpi` | out | resolution and frequency units only appear in media queries this layer does not need. |
| `dppx` | out | resolution and frequency units only appear in media queries this layer does not need. |
| `ic` | build | `Q` is a quarter millimetre; `cap` and `ic` need the font's cap height and ideographic advance: approximate as 0.7em and 1em. |
| `kHz` | out | resolution and frequency units only appear in media queries this layer does not need. |

## HTML elements

Handled: `a`, `abbr`, `address`, `article`, `aside`, `audio`, `b`, `bdi`, `blockquote`, `body`, `br`, `button`, `canvas`, `caption`, `cite`, `code`, `col`, `colgroup`, `data`, `dd`, `del`, `details`, `dfn`, `dialog`, `div`, `dt`, `em`, `fieldset`, `figcaption`, `figure`, `footer`, `form`, `h1`, `h2`, `h3`, `h4`, `h5`, `h6`, `head`, `header`, `hr`, `html`, `i`, `img`, `input`, `ins`, `kbd`, `label`, `legend`, `li`, `link`, `main`, `mark`, `meta`, `meter`, `nav`, `noscript`, `ol`, `optgroup`, `option`, `p`, `picture`, `pre`, `progress`, `q`, `rp`, `rt`, `ruby`, `s`, `samp`, `script`, `section`, `select`, `small`, `source`, `span`, `strong`, `style`, `sub`, `summary`, `sup`, `table`, `tbody`, `td`, `template`, `textarea`, `tfoot`, `th`, `thead`, `time`, `title`, `tr`, `u`, `ul`, `var`, `video`, `wbr`

| Not handled | Kind | Why |
|---|---|---|
| `<area>` | build | image maps: a click on an `img` inside a `map` must hit-test the `area` shapes and fire the area's id. The click plumbing exists, the shape test does not. |
| `<base>` | build | base URL for relative `href`/`src`; relative URLs are not resolved at all today. |
| `<bdo>` | out | bidirectional override; the text engine is left-to-right only. |
| `<datalist>` | build | suggestions for an input: the control is ScriptedScreens', with no suggestion UI; could be drawn by the page as a list under the field on focus. |
| `<dl>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<embed>` | out | no second document, no plugins; a console page is one document. |
| `<hgroup>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<iframe>` | out | no second document, no plugins; a console page is one document. |
| `<map>` | build | image maps: a click on an `img` inside a `map` must hit-test the `area` shapes and fire the area's id. The click plumbing exists, the shape test does not. |
| `<menu>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<object>` | out | no second document, no plugins; a console page is one document. |
| `<output>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<search>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<slot>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<track>` | out | subtitles on a video: the ScriptedScreens media element has no text track. |

## JavaScript: DOM and Web APIs

Handled: document 34 of 35, element 96 of 100, window 65 of 80, canvas 60 of 60.

| Object | Not handled | Kind | Why |
|---|---|---|---|
| document | `write` | out | replaced by `innerHTML` decades ago; ignored. |
| element | `compareDocumentPosition` | out | no use in a console page. |
| element | `requestFullscreen` | out | no use in a console page. |
| element | `setPointerCapture` | out | no use in a console page. |
| element | `releasePointerCapture` | out | no use in a console page. |
| window | `fetch` | out | a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies. |
| window | `XMLHttpRequest` | out | a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies. |
| window | `WebSocket` | out | a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies. |
| window | `Worker` | out | one script thread per page is the design; a worker would be a second engine per page. |
| window | `postMessage` | out | one script thread per page is the design; a worker would be a second engine per page. |
| window | `Blob` | out | no file system, no uploads. `FormData` over a form's fields could be built from the DOM if asked. |
| window | `File` | out | no file system, no uploads. `FormData` over a form's fields could be built from the DOM if asked. |
| window | `FileReader` | out | no file system, no uploads. `FormData` over a form's fields could be built from the DOM if asked. |
| window | `FormData` | out | no file system, no uploads. `FormData` over a form's fields could be built from the DOM if asked. |
| window | `Headers` | out | a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies. |
| window | `Request` | out | a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies. |
| window | `Response` | out | a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies. |
| window | `AbortController` | out | a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies. |
| window | `DOMParser` | build | parse a string into a detached tree: the detached shim holds `innerHTML`; `querySelector` over a detached tree is the missing half. |
| window | `XMLSerializer` | build | parse a string into a detached tree: the detached shim holds `innerHTML`; `querySelector` over a detached tree is the missing half. |

## Approximations: drawn, but not as a browser draws it

| What | Why | What closes it |
|---|---|---|
| `offset-path` is a static position on the path; `offset-distance` does not animate | the tween system interpolates snapshots of box, opacity and transform, not a path distance | a tween on the distance written as an expression that samples the flattened path |
| `offset-path` reads `path()` only; `ray()`, `circle()` and `url()` are ignored, arcs flatten to their chord | one flattener for M L H V C S Q T Z | the arc flattening the vector mod already has, exposed or copied |
| `column-rule` is `solid` or dashed/dotted only; `column-span` and `column-fill` are ignored | the rule is a column's left border | column-span needs the columns rebuilt around the spanning element |
| Inline flow: text wraps beside a float, a drop cap or a list marker only as one shrinkable label; it does not flow around a shape or continue under the letter at the left margin | the layout engine (UI Toolkit) has no inline formatting context: text lives in labels, labels are flex boxes | a real line breaker: measure text, break into line boxes, place runs around floats. Large; would also give `shape-outside`, `::first-line` without the vector, `initial-letter`, mixed inline sizes on one line |
| An inline-block beside text (a small LED span) sits at the line bottom, not on the baseline; `vertical-align` on it is ignored | same: a row of boxes aligned flex-end | same, or a per-item top margin from the font metrics as a stopgap |
| `ruby`: the reading is small and raised after its base, not stacked above it | stacking needs a two-line inline box | same |
| `::first-letter` drop cap: the rest of the paragraph is one label beside the letter, every line indented past it | same | same |
| `@container` size queries are decided against the design width, not the container's | a real container query needs the container's laid-out size and a re-cascade after layout, which today runs once before layout | cascade twice: layout, re-cascade the elements whose rules have container conditions, layout again |
| `border-image` with `px`/number slices reads them as thirds | the image's pixel size is unknown to the emitter (the vector mod downloads it) | vector: expose an image's natural size, or interpret `uv` slices in pixels |
| `text-decoration` colour, thickness, offset and style are drawn only under single-line labels | for a wrapped label the emitter does not know where the text engine broke the lines | the first-line machinery (vector item 15) generalised to every line, or per-line underlines from the vector side |
| `rotateX/Y` are the flat foreshortening, `perspective` ignored | no depth in the vector layer | a 3D matrix group with a depth-sorted mesh: vector work, not asked for |
| `animation-timeline` covers opacity and 2D transforms; `animation-range` ignored | the piecewise expression builder handles those properties | extend it to width/height/background-color (colour through a gradient ramp) and read `animation-range` as an offset |
| `word-break: break-all` estimates its wrapped height from an average glyph width | the layout engine breaks only at spaces; the vector mod breaks at the zero-width spaces the emitter inserts, but the box height must be known first | measure the text with the layout font at the box width, per character |
| `:hover` uses the page rect, not a raycast; something standing between the player and the console does not block it | the game's input module delivers presses only, so the cursor is polled | a raycast from the camera through the cursor against the console before accepting the hover |
| Controls: text capped at about two thirds of the field height; the select's arrow, the checkbox mark and the range thumb are ScriptedScreens' look, not the page's | the controls are ScriptedScreens elements | page-drawn select and range when a page styles them; text input stays native (it needs the keyboard) |
| `@import` and `<link>` sheets are fetched once at load, `<script src>` runs after the inline script rather than before it | fetches are asynchronous; a browser blocks parsing on them | hold the page's first script until its external scripts arrive (the module path does this); the same for sheets |
| `color-mix()` mixes in sRGB whatever colour space is named | no oklab/oklch conversion yet | the colour-space maths (see functions) |
| Relative URLs (`images/x.png`) are not resolved | a page has no base URL | resolve against `<base href>` or a `base` prop on the element |

## Decided out, with no single name to measure

| What | Why |
|---|---|
| Keyboard events (`keydown`, `keyup`, `keypress`), `contenteditable`, caret | the page never has keyboard focus; typing happens in ScriptedScreens' own text control, which reports `input`/`change` |
| `template.content`, `customElements`, shadow DOM | web components need a second tree per element; the renderer has one tree. `innerHTML` from a string covers the usual `template` idiom |
| Drag and drop, clipboard, `navigator.share`, geolocation, notifications, `history` navigation, `location` changes | no browser chrome behind the page; `history` and `location` exist as inert objects so libraries that read them do not throw |
| Canvas `getImageData`/`putImageData`/`createImageData`, `globalCompositeOperation`, `filter` | the canvas is vector paths, not pixels: nothing to read or write, and compositing modes are per-pixel |
| `requestAnimationFrame` above 30 a second | 30 is the vector mod's rebuild cap; a page cannot beat the renderer |
| `select multiple`, `input type=color/date/time/file` | the ScriptedScreens control set has no multi-select, colour or date pickers and no file access |
| Per-pixel effects: `filter: blur()`, `backdrop-filter`, `mix-blend-mode`, `background-blend-mode`, SVG filters | the page is geometry drawn by the vector mod, not pixels |

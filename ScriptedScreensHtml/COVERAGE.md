# What works, what does not, and why (2026-09-16)

One file. The counts and the item names are read from the code by `ScriptedScreensHtml.Tests/coverage.py` (run it after a change), against MDN's CSS data, the HTML living standard's element list and a list of the DOM and Web APIs a page script commonly uses; the reasons come from `ScriptedScreensHtml.Tests/reasons.py`, written by hand. "Handled" means the code names it; whether it behaves as a browser is what the console pages and examples check (`SUPPORT.md`).

Kinds: **build** = not done yet, nothing in the way. **approximation** = drawn, but not as a browser draws it, listed to be replaced. **out** = decided against, with the reason; reopen by deciding otherwise. **fine** = a browser does no more.


## Summary

| Area | Handled | Of | Share |
|---|---|---|---|
| CSS properties (standard) | 399 | 491 | 81% |
| CSS at-rules | 13 | 18 | 72% |
| CSS pseudo-classes and pseudo-elements | 59 | 98 | 60% |
| CSS functions | 89 | 95 | 93% |
| CSS units | 26 | 31 | 83% |
| HTML elements | 101 | 112 | 90% |
| JavaScript: document | 34 | 35 | 97% |
| JavaScript: element | 96 | 100 | 96% |
| JavaScript: window | 67 | 80 | 83% |
| JavaScript: canvas | 60 | 60 | 100% |

## CSS properties, by specification group

| Group | Handled | Not handled | Kind | Why, and what closes it |
|---|---|---|---|---|
| CSS Animations | 10 of 21 | `animation-trigger`, `timeline-trigger`, `timeline-trigger-activation-range`, `timeline-trigger-activation-range-end`, `timeline-trigger-activation-range-start`, `timeline-trigger-active-range`, `timeline-trigger-active-range-end`, `timeline-trigger-active-range-start`, `timeline-trigger-name`, `timeline-trigger-source`, `trigger-scope` | out | `animation-trigger` and the `timeline-trigger-*` family are a 2025 draft no browser ships. |
| CSS Fonts | 13 of 24 | `font-language-override`, `font-palette`, `font-size-adjust`, `font-synthesis-small-caps`, `font-synthesis-style`, `font-synthesis-weight`, `font-variant-alternates`, `font-variant-east-asian`, `font-variant-emoji`, `font-variant-position`, `font-variation-settings` | out | the Fonts mod builds static atlases: no variable axes (`font-variation-settings`), no OpenType features (`font-variant-*`), no colour palettes. `font-variant-numeric: tabular-nums` was the one that mattered and is faked with a monospaced digit span. |
| CSS Masking | 9 of 19 | `mask-border`, `mask-border-mode`, `mask-border-outset`, `mask-border-repeat`, `mask-border-slice`, `mask-border-source`, `mask-border-width`, `mask-composite`, `mask-mode`, `mask-type` | out | an image mask (`mask-image: url()`, `mask-border-*`) needs an alpha mask from a texture on the vector side, not asked for; `mask-mode: luminance`, `mask-composite` (several masks) and `mask-type` (svg <mask>) are the same vector work. The gradient mask's geometry longhands (`mask-position/size/origin/clip/repeat`) are done. |
| CSS Box Sizing | 9 of 17 | `column-height`, `column-wrap`, `contain-intrinsic-block-size`, `contain-intrinsic-height`, `contain-intrinsic-inline-size`, `contain-intrinsic-size`, `contain-intrinsic-width`, `frame-sizing` | out | `column-height` and `column-wrap` are 2025 drafts; `contain-intrinsic-*` only matters with `content-visibility`, a no-op here; `frame-sizing` is a draft. `column-width` is done. |
| CSS Basic User Interface | 12 of 19 | `caret`, `caret-animation`, `caret-shape`, `interactivity`, `interest-delay`, `interest-delay-end`, `interest-delay-start` | out | no caret (typing happens in ScriptedScreens' own control); `interactivity` and `interest-delay-*` are 2025 drafts. |
| CSS Text | 16 of 23 | `hyphenate-character`, `hyphenate-limit-chars`, `line-break`, `text-autospace`, `text-fit`, `text-justify`, `text-wrap-style` | out | `hyphens: auto` and its `hyphenate-*` need a dictionary; `line-break` and `text-autospace` are CJK rules the text engine has no hooks for; `text-justify` variants: the engine has one justification; `text-wrap-style: balance/pretty`: no control over the line breaker; `text-fit` is a draft. `white-space-collapse` and `text-wrap-mode` are done. |
| CSS Backgrounds and Borders | 56 of 61 | `border-shape`, `corner-block-end-shape`, `corner-block-start-shape`, `corner-inline-end-shape`, `corner-inline-start-shape` | out | `border-shape` is a 2025 draft (arbitrary border shapes) no browser ships; `corner-shape` and the per-corner/side `corner-*-shape` names are done. |
| CSS Inline | 6 of 10 | `baseline-source`, `text-box`, `text-box-edge`, `text-box-trim` | out | `text-box-*` (leading trim) and `baseline-source` need font metrics the text engine does not expose. `initial-letter`, `baseline-shift` and `alignment-baseline` are done. |
| Filter Effects | 2 of 6 | `color-interpolation-filters`, `flood-color`, `flood-opacity`, `lighting-color` | out | SVG filter primitives (`<filter>`, feGaussianBlur, flood, lighting) are per-pixel; out with `filter: blur()`: the page is geometry, not pixels. |
| CSS Overflow | 10 of 13 | `scroll-axis-lock`, `scroll-marker-group`, `scroll-target-group` | out | scroll markers (`scroll-marker-group`, `scroll-target-group`) and `scroll-axis-lock` are 2025 drafts (carousel dots). `overflow-clip-margin` is done. |
| CSS Shapes | 0 of 3 | `shape-image-threshold`, `shape-margin`, `shape-outside` | out | text flowing around a float's outline needs an inline formatting context that breaks lines around shapes. The layout engine has none: text lives in labels, labels are boxes. This is the structural limit behind every inline approximation in this file. |
| CSS View Transitions | 0 of 3 | `view-transition-class`, `view-transition-name`, `view-transition-scope` | out | no document navigation, so no view transitions. |
| CSS Display | 3 of 5 | `reading-flow`, `reading-order` | out | `reading-flow`/`reading-order` set keyboard focus order; a page has no keyboard. |
| CSS Overscroll Behavior | 3 of 5 | `overscroll-behavior-block`, `overscroll-behavior-inline` | fine | logical spellings of `overscroll-behavior`, which is a no-op here (no overscroll physics). |
| MathML | 0 of 2 | `math-depth`, `math-style` | out | no user for MathML. |
| CSS Color | 5 of 6 | `dynamic-range-limit` | out | `dynamic-range-limit` is an HDR display hint. |
| CSS Flexible Box Layout | 7 of 8 | `flex-line-count` | out | `flex-line-count` is a 2025 draft. |
| CSS Fragmentation | 5 of 6 | `box-decoration-break` | out | `box-decoration-break` only matters when a box fragments across lines or pages; inline boxes here do not fragment. |
| CSS Images | 3 of 4 | `image-orientation` | out | `image-orientation: from-image` reads EXIF; the image element has no EXIF access. |
| CSS Paged Media | 0 of 1 | `page` | out | print. |
| CSS Ruby | 2 of 3 | `ruby-overhang` | out | `ruby-overhang` tunes ruby stacking, and ruby is not stacked here (see approximations). |
| CSS Scroll Anchoring | 0 of 1 | `overflow-anchor` | out | scroll anchoring is a browser scrolling heuristic; the scroll box here keeps its offset. |
| CSS Text Decoration | 13 of 14 | `text-decoration-inset` | out | `text-decoration-inset` is a 2025 draft. `text-emphasis` is done (see approximations). |
| CSS Writing Modes | 4 of 5 | `text-combine-upright` | out | `text-combine-upright` packs characters in vertical text; vertical text here is a rotated label. |
| Compositing and Blending | 2 of 3 | `background-blend-mode` | out | `background-blend-mode` is per-pixel compositing. |

Complete groups: CSS Anchor Positioning, CSS Box Alignment, CSS Box Model, CSS Cascading and Inheritance, CSS Conditional Rules, CSS Containment, CSS Generated Content, CSS Grid Layout, CSS Lists and Counters, CSS Logical Properties and Values, CSS Multi-column Layout, CSS Positioned Layout, CSS Scroll Snap, CSS Scrollbars Styling, CSS Table, CSS Transforms, CSS Transitions, CSS Viewport, CSS Will Change, Motion Path, Pointer Events, Scalable Vector Graphics.

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

Handled pseudo-classes: `:active`, `:any-link`, `:checked`, `:default`, `:dir`, `:disabled`, `:empty`, `:enabled`, `:first-child`, `:first-of-type`, `:focus`, `:focus-visible`, `:focus-within`, `:has`, `:hover`, `:in-range`, `:indeterminate`, `:invalid`, `:is`, `:lang`, `:last-child`, `:last-of-type`, `:link`, `:matches`, `:modal`, `:not`, `:nth-child`, `:nth-last-child`, `:nth-last-of-type`, `:nth-of-type`, `:only-child`, `:only-of-type`, `:open`, `:optional`, `:out-of-range`, `:placeholder-shown`, `:popover-open`, `:read-only`, `:read-write`, `:required`, `:root`, `:scope`, `:target`, `:user-invalid`, `:user-valid`, `:valid`, `:visited`, `:where`

Handled pseudo-elements: `::-webkit-scrollbar`, `::-webkit-scrollbar-thumb`, `::-webkit-scrollbar-track`, `::after`, `::backdrop`, `::before`, `::details-content`, `::first-letter`, `::first-line`, `::marker`, `::placeholder`

| Not handled | Kind | Why |
|---|---|---|
| `::checkmark` | out | parts of browser-native widgets. The select's arrow and the checkbox's mark are drawn here and could take colours from these rules later. |
| `::cue` | out | media state of ScriptedScreens' media element is not readable from the page. |
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
| `:right` | out | no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions. |
| `:seeking` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:stalled` | out | media state of ScriptedScreens' media element is not readable from the page. |
| `:state` | out | web components and shadow DOM: the renderer has one tree, no custom elements. |
| `:volume-locked` | out | media state of ScriptedScreens' media element is not readable from the page. |

## CSS functions

Handled: `abs`, `acos`, `alpha`, `asin`, `atan`, `atan2`, `attr`, `blur`, `brightness`, `calc`, `circle`, `clamp`, `color`, `color-mix`, `conic-gradient`, `contrast`, `cos`, `counter`, `counters`, `cubic-bezier`, `drop-shadow`, `ellipse`, `env`, `exp`, `fit-content`, `grayscale`, `hsl`, `hue-rotate`, `hwb`, `hypot`, `image`, `image-set`, `inset`, `invert`, `lab`, `layer`, `lch`, `light-dark`, `linear`, `linear-gradient`, `log`, `matrix`, `matrix3d`, `max`, `min`, `minmax`, `mod`, `oklab`, `oklch`, `opacity`, `path`, `perspective`, `polygon`, `pow`, `radial-gradient`, `ray`, `rect`, `rem`, `repeating-conic-gradient`, `repeating-linear-gradient`, `repeating-radial-gradient`, `rgb`, `rotate`, `rotate3d`, `rotateX`, `rotateY`, `rotateZ`, `round`, `saturate`, `scale`, `scale3d`, `scaleX`, `scaleY`, `sepia`, `sign`, `sin`, `skew`, `skewX`, `skewY`, `sqrt`, `steps`, `symbols`, `tan`, `translate`, `translate3d`, `translateX`, `translateY`, `var`, `xywh`

| Not handled | Kind | Why |
|---|---|---|
| `cross-fade()` | out | `paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft. |
| `paint()` | out | `paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft. |
| `palette-mix()` | out | `paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft. |
| `param()` | out | `paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft. |
| `scaleZ()` | approximation | 3D spellings take their flat projection: `rotateX/Y` as foreshortening, Z ignored. A true perspective projection needs depth the vector layer does not have. |
| `translateZ()` | approximation | 3D spellings take their flat projection: `rotateX/Y` as foreshortening, Z ignored. A true perspective projection needs depth the vector layer does not have. |

## CSS units

Handled: `%`, `cap`, `ch`, `cm`, `deg`, `em`, `ex`, `fr`, `grad`, `ic`, `in`, `mm`, `ms`, `pc`, `pt`, `px`, `q`, `rad`, `rem`, `s`, `turn`, `vh`, `vmax`, `vmin`, `vw`, `x`

| Not handled | Kind | Why |
|---|---|---|
| `Hz` | out | resolution and frequency units only appear in media queries this layer does not need. |
| `dpcm` | out | resolution and frequency units only appear in media queries this layer does not need. |
| `dpi` | out | resolution and frequency units only appear in media queries this layer does not need. |
| `dppx` | out | resolution and frequency units only appear in media queries this layer does not need. |
| `kHz` | out | resolution and frequency units only appear in media queries this layer does not need. |

## HTML elements

Handled: `a`, `abbr`, `address`, `area`, `article`, `aside`, `audio`, `b`, `base`, `bdi`, `blockquote`, `body`, `br`, `button`, `canvas`, `caption`, `cite`, `code`, `col`, `colgroup`, `data`, `datalist`, `dd`, `del`, `details`, `dfn`, `dialog`, `div`, `dt`, `em`, `fieldset`, `figcaption`, `figure`, `footer`, `form`, `h1`, `h2`, `h3`, `h4`, `h5`, `h6`, `head`, `header`, `hr`, `html`, `i`, `img`, `input`, `ins`, `kbd`, `label`, `legend`, `li`, `link`, `main`, `map`, `mark`, `meta`, `meter`, `nav`, `noscript`, `ol`, `optgroup`, `option`, `p`, `picture`, `pre`, `progress`, `q`, `rp`, `rt`, `ruby`, `s`, `samp`, `script`, `section`, `select`, `small`, `source`, `span`, `strong`, `style`, `sub`, `summary`, `sup`, `table`, `tbody`, `td`, `template`, `textarea`, `tfoot`, `th`, `thead`, `time`, `title`, `tr`, `u`, `ul`, `var`, `video`, `wbr`

| Not handled | Kind | Why |
|---|---|---|
| `<bdo>` | out | bidirectional override; the text engine is left-to-right only. |
| `<dl>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<embed>` | out | no second document, no plugins; a console page is one document. |
| `<hgroup>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<iframe>` | out | no second document, no plugins; a console page is one document. |
| `<menu>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<object>` | out | no second document, no plugins; a console page is one document. |
| `<output>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<search>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<slot>` | fine | renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it. |
| `<track>` | out | subtitles on a video: the ScriptedScreens media element has no text track. |

## JavaScript: DOM and Web APIs

Handled: document 34 of 35, element 96 of 100, window 67 of 80, canvas 60 of 60.

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

## Approximations: drawn, but not as a browser draws it

| What | Why | What closes it |
|---|---|---|
| `text-emphasis` marks are a second label of marks spaced by their own advance, not by the glyphs under them | the glyph positions of a label are not known here | exact only for monospace text; a per-glyph layout from the text engine would place them |
| `column-fill: auto` fills like `balance` | the columns are made before layout, when the heights are unknown | a post-layout pass moving children down the first column until it is full |
| `marker-mid` is skipped on a `path` (start and end markers draw) | the path's vertices here are the flattened curve, not the author's command points | keep the command endpoints when flattening |
| `mask-repeat` is accepted; a gradient mask does not tile | a gradient clamps to its end stops outside its box | vector: a repeating gradient |
| image map `coords` are read in the box's own px | the picture's pixel size is unknown to the page (ScriptedScreens loads it) | exact when the img's width/height attributes match the picture; vector: expose the natural size |
| `image-set()` takes its first candidate | one resolution here | fine: a console has one pixel density |
| `light-dark()` follows the last `color-scheme` seen in the cascade, page-wide | no per-subtree scheme | resolve per element from its ancestors' color-scheme |
| `DOMParser` parses with the page's tag-soup parser whatever the MIME type | one parser | fine for HTML and svg fragments; XML namespaces are not a thing here |
| `transition-behavior: allow-discrete` holds `display: none` until the element's other transition ends; without one it hides at once | display is not a number to tween | as a browser |
| An animated `offset-distance` follows the path as 16 straight pieces between the two distances | the position is a piecewise-linear expression over the tween's progress; the vector expression language has no path lookup | more pieces when a long path shows corners (the count is a constant in the emitter) |
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

# reasons.py -- why each unhandled item is unhandled, and what closes it. Read by coverage.py,
# which prints these next to the measured gaps in COVERAGE.md. Keys:
#   "group:<CSS group name>"  a whole property group   "prop:<name>"   one property
#   "at:@name"  "sel:<selector>"  "fn:<name>"  "unit:<name>"  "html:<tag>"  "js:<member>"
# Value: (kind, text). Kinds: "build" (not done yet, nothing in the way), "approximation"
# (drawn, but not as a browser draws it), "out" (decided against, with the reason), "fine"
# (a browser does no more).

R = {}

def put(kind, text, *keys):
    for k in keys:
        R[k] = (kind, text)

# ---------------------------------------------------------------- CSS property groups
put("out", "`animation-trigger` and the `timeline-trigger-*` family are a 2025 draft no browser ships.", "group:CSS Animations")
put("out", "the Fonts mod builds static atlases: no variable axes (`font-variation-settings`), no OpenType features (`font-variant-*`), no colour palettes. `font-variant-numeric: tabular-nums` was the one that mattered and is faked with a monospaced digit span.", "group:CSS Fonts")
put("out", "no caret (typing happens in ScriptedScreens' own control); `interactivity` and `interest-delay-*` are 2025 drafts.", "group:CSS Basic User Interface")
put("out", "SVG filter primitives (`<filter>`, feGaussianBlur, flood, lighting) are per-pixel; out with `filter: blur()`: the page is geometry, not pixels.", "group:Filter Effects")
put("out", "text flowing around a float's outline needs an inline formatting context that breaks lines around shapes. The layout engine has none: text lives in labels, labels are boxes. This is the structural limit behind every inline approximation in this file.", "group:CSS Shapes")
put("out", "no document navigation, so no view transitions.", "group:CSS View Transitions")
put("out", "no user for MathML.", "group:MathML")
put("fine", "logical spellings of `overscroll-behavior`, which is a no-op here (no overscroll physics).", "group:CSS Overscroll Behavior")
put("out", "`reading-flow`/`reading-order` set keyboard focus order; a page has no keyboard.", "group:CSS Display")
put("out", "`background-blend-mode` is per-pixel compositing.", "group:Compositing and Blending")
put("out", "`box-decoration-break` only matters when a box fragments across lines or pages; inline boxes here do not fragment.", "group:CSS Fragmentation")
put("out", "`dynamic-range-limit` is an HDR display hint.", "group:CSS Color")
put("out", "`flex-line-count` is a 2025 draft.", "group:CSS Flexible Box Layout")
put("out", "`image-orientation: from-image` reads EXIF; the image element has no EXIF access.", "group:CSS Images")
put("out", "scroll anchoring is a browser scrolling heuristic; the scroll box here keeps its offset.", "group:CSS Scroll Anchoring")
put("out", "print.", "group:CSS Paged Media")
put("out", "`ruby-overhang` tunes ruby stacking, and ruby is not stacked here (see approximations).", "group:CSS Ruby")
put("out", "`text-combine-upright` packs characters in vertical text; vertical text here is a rotated label.", "group:CSS Writing Modes")

# ---------------------------------------------------------------- at-rules
put("out", "OpenType features and palettes: see fonts.", "at:@font-feature-values", "at:@font-palette-values")
put("out", "XML namespaces in selectors; nobody writes them for HTML.", "at:@namespace")
put("out", "print.", "at:@page")
put("out", "no document navigation.", "at:@view-transition")

# ---------------------------------------------------------------- selectors
put("out", "no text selection, so nothing to style.", "sel:::selection", "sel:::highlight", "sel:::spelling-error", "sel:::grammar-error", "sel:::target-text")
put("out", "parts of browser-native widgets. The select's arrow and the checkbox's mark are drawn here and could take colours from these rules later.", "sel:::file-selector-button", "sel:::picker", "sel:::picker-icon", "sel:::checkmark")
put("out", "web components and shadow DOM: the renderer has one tree, no custom elements.", "sel:::part", "sel:::slotted", "sel::host", "sel::host-context", "sel::has-slotted", "sel::defined", "sel::state")
put("out", "media state of ScriptedScreens' media element is not readable from the page.", "sel:::cue", "sel::playing", "sel::paused", "sel::muted", "sel::buffering", "sel::seeking", "sel::stalled", "sel::volume-locked", "sel::picture-in-picture")
put("out", "no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions.", "sel::fullscreen", "sel::autofill", "sel::first", "sel::left", "sel::right", "sel::past", "sel::future", "sel::active-view-transition", "sel::active-view-transition-type")
put("out", "view transitions.", "sel:::view-transition", "sel:::view-transition-group", "sel:::view-transition-image-pair", "sel:::view-transition-new", "sel:::view-transition-old")

# ---------------------------------------------------------------- functions
put("out", "`paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft.", "fn:paint", "fn:cross-fade", "fn:palette-mix", "fn:param")
put("approximation", "3D spellings take their flat projection: `rotateX/Y` as foreshortening, Z ignored. A true perspective projection needs depth the vector layer does not have.", "fn:rotate3d", "fn:scale3d", "fn:translate3d", "fn:scaleZ", "fn:translateZ", "fn:matrix3d")

# ---------------------------------------------------------------- units
put("out", "resolution and frequency units only appear in media queries this layer does not need.", "unit:dpi", "unit:dpcm", "unit:dppx", "unit:Hz", "unit:kHz")

# ---------------------------------------------------------------- HTML
put("out", "no second document, no plugins; a console page is one document.", "html:iframe", "html:embed", "html:object")
put("out", "subtitles on a video: the ScriptedScreens media element has no text track.", "html:track")
put("out", "bidirectional override; the text engine is left-to-right only.", "html:bdo")
put("fine", "renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it.", "html:dl", "html:hgroup", "html:menu", "html:search", "html:output", "html:slot")

# ---------------------------------------------------------------- JavaScript
put("out", "a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies.", "js:fetch", "js:XMLHttpRequest", "js:WebSocket", "js:Request", "js:Response", "js:Headers", "js:AbortController")
put("out", "one script thread per page is the design; a worker would be a second engine per page.", "js:Worker", "js:postMessage")
put("out", "no file system, no uploads. `FormData` over a form's fields could be built from the DOM if asked.", "js:Blob", "js:File", "js:FileReader", "js:FormData")
put("out", "replaced by `innerHTML` decades ago; ignored.", "js:write")
put("out", "no use in a console page.", "js:compareDocumentPosition", "js:requestFullscreen", "js:setPointerCapture", "js:releasePointerCapture")


# ---- reclassified after Batch H (2026-09-16): what stays out, and why
put("out", "`border-shape` is a 2025 draft (arbitrary border shapes) no browser ships; `corner-shape` and the per-corner/side `corner-*-shape` names are done.", "group:CSS Backgrounds and Borders")
put("out", "an image mask (`mask-image: url()`, `mask-border-*`) needs an alpha mask from a texture on the vector side, not asked for; `mask-mode: luminance`, `mask-composite` (several masks) and `mask-type` (svg <mask>) are the same vector work. The gradient mask's geometry longhands (`mask-position/size/origin/clip/repeat`) are done.", "group:CSS Masking")
put("out", "`column-height` and `column-wrap` are 2025 drafts; `contain-intrinsic-*` only matters with `content-visibility`, a no-op here; `frame-sizing` is a draft. `column-width` is done.", "group:CSS Box Sizing")
put("out", "`hyphens: auto` and its `hyphenate-*` need a dictionary; `line-break` and `text-autospace` are CJK rules the text engine has no hooks for; `text-justify` variants: the engine has one justification; `text-wrap-style: balance/pretty`: no control over the line breaker; `text-fit` is a draft. `white-space-collapse` and `text-wrap-mode` are done.", "group:CSS Text")
put("out", "`text-box-*` (leading trim) and `baseline-source` need font metrics the text engine does not expose. `initial-letter`, `baseline-shift` and `alignment-baseline` are done.", "group:CSS Inline")
put("out", "`text-decoration-inset` is a 2025 draft. `text-emphasis` is done (see approximations).", "group:CSS Text Decoration")
put("out", "scroll markers (`scroll-marker-group`, `scroll-target-group`) and `scroll-axis-lock` are 2025 drafts (carousel dots). `overflow-clip-margin` is done.", "group:CSS Overflow")
put("out", "`alpha` is not a function: MDN lists the relative-colour channel keyword; `rgb(from ...)` and the other relative forms are done.", "fn:alpha")

# ---------------------------------------------------------------- approximations (drawn, but not as a browser draws it)
APPROX = [
    ("`text-emphasis` marks are a second label of marks spaced by their own advance, not by the glyphs under them", "the glyph positions of a label are not known here", "exact only for monospace text; a per-glyph layout from the text engine would place them"),
    ("`column-fill: auto` fills like `balance`", "the columns are made before layout, when the heights are unknown", "a post-layout pass moving children down the first column until it is full"),
    ("`marker-mid` is skipped on a `path` (start and end markers draw)", "the path's vertices here are the flattened curve, not the author's command points", "keep the command endpoints when flattening"),
    ("`mask-repeat` is accepted; a gradient mask does not tile", "a gradient clamps to its end stops outside its box", "vector: a repeating gradient"),
    ("image map `coords` are read in the box's own px", "the picture's pixel size is unknown to the page (ScriptedScreens loads it)", "exact when the img's width/height attributes match the picture; vector: expose the natural size"),
    ("`image-set()` takes its first candidate", "one resolution here", "fine: a console has one pixel density"),
    ("`light-dark()` follows the last `color-scheme` seen in the cascade, page-wide", "no per-subtree scheme", "resolve per element from its ancestors' color-scheme"),
    ("`DOMParser` parses with the page's tag-soup parser whatever the MIME type", "one parser", "fine for HTML and svg fragments; XML namespaces are not a thing here"),
    ("`transition-behavior: allow-discrete` holds `display: none` until the element's other transition ends; without one it hides at once", "display is not a number to tween", "as a browser"),
    ("An animated `offset-distance` follows the path as 16 straight pieces between the two distances", "the position is a piecewise-linear expression over the tween's progress; the vector expression language has no path lookup", "more pieces when a long path shows corners (the count is a constant in the emitter)"),
    ("`offset-path` reads `path()` only; `ray()`, `circle()` and `url()` are ignored, arcs flatten to their chord", "one flattener for M L H V C S Q T Z", "the arc flattening the vector mod already has, exposed or copied"),
    ("`column-rule` is `solid` or dashed/dotted only; `column-span` and `column-fill` are ignored", "the rule is a column's left border", "column-span needs the columns rebuilt around the spanning element"),
    ("Inline flow: text wraps beside a float, a drop cap or a list marker only as one shrinkable label; it does not flow around a shape or continue under the letter at the left margin",
     "the layout engine (UI Toolkit) has no inline formatting context: text lives in labels, labels are flex boxes",
     "a real line breaker: measure text, break into line boxes, place runs around floats. Large; would also give `shape-outside`, `::first-line` without the vector, `initial-letter`, mixed inline sizes on one line"),
    ("An inline-block beside text (a small LED span) sits at the line bottom, not on the baseline; `vertical-align` on it is ignored", "same: a row of boxes aligned flex-end", "same, or a per-item top margin from the font metrics as a stopgap"),
    ("`ruby`: the reading is small and raised after its base, not stacked above it", "stacking needs a two-line inline box", "same"),
    ("`::first-letter` drop cap: the rest of the paragraph is one label beside the letter, every line indented past it", "same", "same"),
    ("`@container` size queries are decided against the design width, not the container's", "a real container query needs the container's laid-out size and a re-cascade after layout, which today runs once before layout", "cascade twice: layout, re-cascade the elements whose rules have container conditions, layout again"),
    ("`border-image` with `px`/number slices reads them as thirds", "the image's pixel size is unknown to the emitter (the vector mod downloads it)", "vector: expose an image's natural size, or interpret `uv` slices in pixels"),
    ("`text-decoration` colour, thickness, offset and style are drawn only under single-line labels", "for a wrapped label the emitter does not know where the text engine broke the lines", "the first-line machinery (vector item 15) generalised to every line, or per-line underlines from the vector side"),
    ("`rotateX/Y` are the flat foreshortening, `perspective` ignored", "no depth in the vector layer", "a 3D matrix group with a depth-sorted mesh: vector work, not asked for"),
    ("`animation-timeline` covers opacity and 2D transforms; `animation-range` ignored", "the piecewise expression builder handles those properties", "extend it to width/height/background-color (colour through a gradient ramp) and read `animation-range` as an offset"),
    ("`word-break: break-all` estimates its wrapped height from an average glyph width", "the layout engine breaks only at spaces; the vector mod breaks at the zero-width spaces the emitter inserts, but the box height must be known first", "measure the text with the layout font at the box width, per character"),
    ("`:hover` uses the page rect, not a raycast; something standing between the player and the console does not block it", "the game's input module delivers presses only, so the cursor is polled", "a raycast from the camera through the cursor against the console before accepting the hover"),
    ("Controls: text capped at about two thirds of the field height; the select's arrow, the checkbox mark and the range thumb are ScriptedScreens' look, not the page's", "the controls are ScriptedScreens elements", "page-drawn select and range when a page styles them; text input stays native (it needs the keyboard)"),
    ("`@import` and `<link>` sheets are fetched once at load, `<script src>` runs after the inline script rather than before it", "fetches are asynchronous; a browser blocks parsing on them", "hold the page's first script until its external scripts arrive (the module path does this); the same for sheets"),
    ("`color-mix()` mixes in sRGB whatever colour space is named", "no oklab/oklch conversion yet", "the colour-space maths (see functions)"),
    ("Relative URLs (`images/x.png`) are not resolved", "a page has no base URL", "resolve against `<base href>` or a `base` prop on the element"),
]

# ---------------------------------------------------------------- decided out with no measurable name
OUT = [
    ("Keyboard events (`keydown`, `keyup`, `keypress`), `contenteditable`, caret", "the page never has keyboard focus; typing happens in ScriptedScreens' own text control, which reports `input`/`change`"),
    ("`template.content`, `customElements`, shadow DOM", "web components need a second tree per element; the renderer has one tree. `innerHTML` from a string covers the usual `template` idiom"),
    ("Drag and drop, clipboard, `navigator.share`, geolocation, notifications, `history` navigation, `location` changes", "no browser chrome behind the page; `history` and `location` exist as inert objects so libraries that read them do not throw"),
    ("Canvas `getImageData`/`putImageData`/`createImageData`, `globalCompositeOperation`, `filter`", "the canvas is vector paths, not pixels: nothing to read or write, and compositing modes are per-pixel"),
    ("`requestAnimationFrame` above 30 a second", "30 is the vector mod's rebuild cap; a page cannot beat the renderer"),
    ("`select multiple`, `input type=color/date/time/file`", "the ScriptedScreens control set has no multi-select, colour or date pickers and no file access"),
    ("Per-pixel effects: `filter: blur()`, `backdrop-filter`, `mix-blend-mode`, `background-blend-mode`, SVG filters", "the page is geometry drawn by the vector mod, not pixels"),
]

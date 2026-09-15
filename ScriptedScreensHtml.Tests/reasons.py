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
put("build", "`background-position-x/-y` are longhands the shorthand already covers: add the two names. `background-origin` moves the image box to the padding or content edge: an offset in the image emit. The per-corner `corner-*-shape` and `border-shape` need the 1-to-4 expansion `border-radius` has; `corner-shape` for all four corners is done.", "group:CSS Backgrounds and Borders")
put("build", "gradient masks are done (vector item 10). An image mask (`mask-image: url()`) needs a vector mask from a texture's alpha, not asked for yet. The geometry longhands (`mask-position/size/repeat/origin/clip`) are the same offsets `background-*` uses. `clip-rule` is `fill-rule` for clip paths: pass `fr` on the CP def. `mask-border-*` is nine-slice for masks, on top of `border-image`.", "group:CSS Masking")
put("build", "`fill`/`stroke` as CSS are done; the geometry properties (`cx cy r rx ry x y d`) need the same treatment in the svg collector. `marker-start/mid/end` (arrowheads) need `<marker>`: the marker's shapes emitted at each vertex, turned by the segment's angle. `vector-effect: non-scaling-stroke` is what a baked fit already does; needs the flag read.", "group:Scalable Vector Graphics")
put("out", "`animation-trigger` and the `timeline-trigger-*` family are a 2025 draft no browser ships.", "group:CSS Animations")
put("out", "the Fonts mod builds static atlases: no variable axes (`font-variation-settings`), no OpenType features (`font-variant-*`), no colour palettes. `font-variant-numeric: tabular-nums` was the one that mattered and is faked with a monospaced digit span.", "group:CSS Fonts")
put("build", "`column-width` (column count from a width) is arithmetic on top of `column-count`. `contain-intrinsic-*` only matters with `content-visibility`, which is a no-op here; `frame-sizing` is a 2025 draft.", "group:CSS Box Sizing")
put("build", "`white-space-collapse` and `text-wrap-mode` are the new longhands of `white-space`: map them. `hyphens: auto` needs a dictionary: out. `text-justify` variants: the text engine has one justification. `text-wrap-style: balance/pretty`: no control over the line breaker.", "group:CSS Text")
put("build", "`initial-letter` is the drop cap with a line count: the `::first-letter` machinery exists, the size is lines times line-height. `baseline-shift` and `alignment-baseline` are `vertical-align` spellings. `text-box-*` (leading trim) needs font metrics the text engine does not expose.", "group:CSS Inline")
put("out", "no caret (typing happens in ScriptedScreens' own control); `interactivity` and `interest-delay-*` are 2025 drafts.", "group:CSS Basic User Interface")
put("build", "`text-emphasis-*` are small marks above each character: a second label of dots; low value. `text-decoration-inset` is new and rarely used.", "group:CSS Text Decoration")
put("out", "SVG filter primitives (`<filter>`, feGaussianBlur, flood, lighting) are per-pixel; out with `filter: blur()`: the page is geometry, not pixels.", "group:Filter Effects")
put("build", "`overflow-clip-margin` is a larger clip rect: small. Scroll markers and `scroll-axis-lock` are 2025 drafts (carousel dots).", "group:CSS Overflow")
put("out", "text flowing around a float's outline needs an inline formatting context that breaks lines around shapes. The layout engine has none: text lives in labels, labels are boxes. This is the structural limit behind every inline approximation in this file.", "group:CSS Shapes")
put("out", "no document navigation, so no view transitions.", "group:CSS View Transitions")
put("out", "no user for MathML.", "group:MathML")
put("fine", "logical spellings of `overscroll-behavior`, which is a no-op here (no overscroll physics).", "group:CSS Overscroll Behavior")
put("out", "`reading-flow`/`reading-order` set keyboard focus order; a page has no keyboard.", "group:CSS Display")
put("out", "`background-blend-mode` is per-pixel compositing.", "group:Compositing and Blending")
put("out", "`box-decoration-break` only matters when a box fragments across lines or pages; inline boxes here do not fragment.", "group:CSS Fragmentation")
put("out", "`dynamic-range-limit` is an HDR display hint.", "group:CSS Color")
put("out", "`flex-line-count` is a 2025 draft.", "group:CSS Flexible Box Layout")
put("build", "`grid` is the shorthand of `grid-template` plus the auto-flow parts: parse and split it.", "group:CSS Grid Layout")
put("out", "`image-orientation: from-image` reads EXIF; the image element has no EXIF access.", "group:CSS Images")
put("out", "scroll anchoring is a browser scrolling heuristic; the scroll box here keeps its offset.", "group:CSS Scroll Anchoring")
put("out", "print.", "group:CSS Paged Media")
put("out", "`ruby-overhang` tunes ruby stacking, and ruby is not stacked here (see approximations).", "group:CSS Ruby")
put("out", "`text-combine-upright` packs characters in vertical text; vertical text here is a rotated label.", "group:CSS Writing Modes")
put("build", "`transform-box: fill-box` changes the transform origin box for svg content: small.", "group:CSS Transforms")
put("build", "`transition-behavior: allow-discrete` lets `display` transition; the tween system could hold `display` until the tween ends.", "group:CSS Transitions")

# ---------------------------------------------------------------- at-rules
put("out", "OpenType features and palettes: see fonts.", "at:@font-feature-values", "at:@font-palette-values")
put("out", "XML namespaces in selectors; nobody writes them for HTML.", "at:@namespace")
put("out", "print.", "at:@page")
put("out", "no document navigation.", "at:@view-transition")

# ---------------------------------------------------------------- selectors
put("out", "no text selection, so nothing to style.", "sel:::selection", "sel:::highlight", "sel:::spelling-error", "sel:::grammar-error", "sel:::target-text")
put("build", "`::details-content` styles the body of a details element, which is an element already: match it.", "sel:::details-content")
put("out", "parts of browser-native widgets. The select's arrow and the checkbox's mark are drawn here and could take colours from these rules later.", "sel:::file-selector-button", "sel:::picker", "sel:::picker-icon", "sel:::checkmark")
put("out", "web components and shadow DOM: the renderer has one tree, no custom elements.", "sel:::part", "sel:::slotted", "sel::host", "sel::host-context", "sel::has-slotted", "sel::defined", "sel::state")
put("out", "media state of ScriptedScreens' media element is not readable from the page.", "sel:::cue", "sel::playing", "sel::paused", "sel::muted", "sel::buffering", "sel::seeking", "sel::stalled", "sel::volume-locked", "sel::picture-in-picture")
put("out", "no fullscreen, no autofill, no paged media, no time-based media cues, no view transitions.", "sel::fullscreen", "sel::autofill", "sel::first", "sel::left", "sel::right", "sel::past", "sel::future", "sel::active-view-transition", "sel::active-view-transition-type")
put("build", "`popover` attribute support would make `:popover-open` the same as `:open`.", "sel::popover-open")
put("build", "inside `querySelector` and `@scope` it is the root (done there); as a bare selector it is `:root`. One case.", "sel::scope")
put("out", "view transitions.", "sel:::view-transition", "sel:::view-transition-group", "sel:::view-transition-image-pair", "sel:::view-transition-new", "sel:::view-transition-old")

# ---------------------------------------------------------------- functions
put("build", "safe-area insets: return 0.", "fn:env")
put("build", "pick the light value, or by `color-scheme`.", "fn:light-dark")
put("build", "colour spaces: the conversion maths into sRGB; `color-mix(in oklch)` then becomes exact instead of sRGB.", "fn:hwb", "fn:lab", "fn:lch", "fn:oklab", "fn:oklch")
put("build", "`image-set` picks a URL by resolution: take the first. `xywh()` is a `clip-path` rect form. `ray()` belongs to motion paths. `alpha` is the relative-colour syntax (`rgb(from ...)`).", "fn:image-set", "fn:xywh", "fn:ray", "fn:alpha")
put("out", "`paint()` is the Houdini paint API; `cross-fade()` blends two images per pixel; `palette-mix()` is a font palette; `param()` is a draft.", "fn:paint", "fn:cross-fade", "fn:palette-mix", "fn:param")
put("approximation", "3D spellings take their flat projection: `rotateX/Y` as foreshortening, Z ignored. A true perspective projection needs depth the vector layer does not have.", "fn:rotate3d", "fn:scale3d", "fn:translate3d", "fn:scaleZ", "fn:translateZ", "fn:matrix3d")

# ---------------------------------------------------------------- units
put("build", "`Q` is a quarter millimetre; `cap` and `ic` need the font's cap height and ideographic advance: approximate as 0.7em and 1em.", "unit:Q", "unit:cap", "unit:ic")
put("out", "resolution and frequency units only appear in media queries this layer does not need.", "unit:dpi", "unit:dpcm", "unit:dppx", "unit:Hz", "unit:kHz")

# ---------------------------------------------------------------- HTML
put("out", "no second document, no plugins; a console page is one document.", "html:iframe", "html:embed", "html:object")
put("build", "image maps: a click on an `img` inside a `map` must hit-test the `area` shapes and fire the area's id. The click plumbing exists, the shape test does not.", "html:area", "html:map")
put("build", "suggestions for an input: the control is ScriptedScreens', with no suggestion UI; could be drawn by the page as a list under the field on focus.", "html:datalist")
put("out", "subtitles on a video: the ScriptedScreens media element has no text track.", "html:track")
put("build", "base URL for relative `href`/`src`; relative URLs are not resolved at all today.", "html:base")
put("out", "bidirectional override; the text engine is left-to-right only.", "html:bdo")
put("fine", "renders as a plain block, as a browser's default; nothing names it in the code, which is why the tool counts it.", "html:dl", "html:hgroup", "html:menu", "html:search", "html:output", "html:slot")

# ---------------------------------------------------------------- JavaScript
put("out", "a page has no network of its own by decision: data comes from Lua, so a console cannot become a general web client, and the game's fetch path (used for images and scripts) is a one-shot download without headers or bodies.", "js:fetch", "js:XMLHttpRequest", "js:WebSocket", "js:Request", "js:Response", "js:Headers", "js:AbortController")
put("out", "one script thread per page is the design; a worker would be a second engine per page.", "js:Worker", "js:postMessage")
put("out", "no file system, no uploads. `FormData` over a form's fields could be built from the DOM if asked.", "js:Blob", "js:File", "js:FileReader", "js:FormData")
put("build", "parse a string into a detached tree: the detached shim holds `innerHTML`; `querySelector` over a detached tree is the missing half.", "js:DOMParser", "js:XMLSerializer")
put("out", "replaced by `innerHTML` decades ago; ignored.", "js:write")
put("out", "no use in a console page.", "js:compareDocumentPosition", "js:requestFullscreen", "js:setPointerCapture", "js:releasePointerCapture")

put("build", "`offset-path` places the box at `offset-distance` along a `path()`, turned by `offset-rotate`. `offset` is their shorthand: split it. `offset-anchor` moves which point of the box sits on the path (the centre today); `offset-position` is the start for `ray()`, not read.", "group:Motion Path")
put("build", "`column-rule` is a left border on every column but the first. `column-span: all` breaks an element out of the columns: rebuild the columns around it. `column-fill: balance` is what exists; `auto` fills the first column first.", "group:CSS Multi-column Layout")

# ---------------------------------------------------------------- approximations (drawn, but not as a browser draws it)
APPROX = [
    ("`offset-path` is a static position on the path; `offset-distance` does not animate", "the tween system interpolates snapshots of box, opacity and transform, not a path distance", "a tween on the distance written as an expression that samples the flattened path"),
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

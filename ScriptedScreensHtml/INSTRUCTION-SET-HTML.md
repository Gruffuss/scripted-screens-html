Rules and columns: see INSTRUCTION-SET.md.

## 1. HTML

### 1a. Elements

#### HTML elements (conforming)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| <a> | | | | |
| <abbr> | | | | |
| <address> | | | | |
| <area> | | | | |
| <article> | | | | |
| <aside> | | | | |
| <audio> | | | | |
| <b> | | | | |
| <base> | | | | |
| <bdi> | | | | |
| <bdo> | | | | |
| <blockquote> | | | | |
| <body> | | | | |
| <br> | | | | |
| <button> | | | | |
| <canvas> | | | | |
| <caption> | | | | |
| <cite> | | | | |
| <code> | | | | |
| <col> | | | | |
| <colgroup> | | | | |
| <data> | | | | |
| <datalist> | | | | |
| <dd> | | | | |
| <del> | | | | |
| <details> | | | | |
| <dfn> | | | | |
| <dialog> | | | | |
| <div> | | | | |
| <dl> | | | | |
| <dt> | | | | |
| <em> | | | | |
| <embed> | | | | |
| <fieldset> | | | | |
| <figcaption> | | | | |
| <figure> | | | | |
| <footer> | | | | |
| <form> | | | | |
| <h1> | | | | |
| <h2> | | | | |
| <h3> | | | | |
| <h4> | | | | |
| <h5> | | | | |
| <h6> | | | | |
| <head> | | | | |
| <header> | | | | |
| <hgroup> | | | | |
| <hr> | | | | |
| <html> | | | | |
| <i> | | | | |
| <iframe> | | | | |
| <img> | | | | |
| <input> | | | | |
| <ins> | | | | |
| <kbd> | | | | |
| <label> | | | | |
| <legend> | | | | |
| <li> | | | | |
| <link> | | | | |
| <main> | | | | |
| <map> | | | | |
| <mark> | | | | |
| <math> (embedded MathML root — see MathML table below) | | | | |
| <menu> | | | | |
| <meta> | | | | |
| <meter> | | | | |
| <nav> | | | | |
| <noscript> | | | | |
| <object> | | | | |
| <ol> | | | | |
| <optgroup> | | | | |
| <option> | | | | |
| <output> | | | | |
| <p> | | | | |
| <picture> | | | | |
| <pre> | | | | |
| <progress> | | | | |
| <q> | | | | |
| <rp> | | | | |
| <rt> | | | | |
| <ruby> | | | | |
| <s> | | | | |
| <samp> | | | | |
| <script> | | | | |
| <search> | | | | |
| <section> | | | | |
| <select> | | | | |
| <selectedcontent> | | | | |
| <slot> | | | | |
| <small> | | | | |
| <source> | | | | |
| <span> | | | | |
| <strong> | | | | |
| <sub> | | | | |
| <summary> | | | | |
| <sup> | | | | |
| <svg> (embedded SVG root — see SVG table below) | | | | |
| <table> | | | | |
| <tbody> | | | | |
| <td> | | | | |
| <template> | | | | |
| <textarea> | | | | |
| <tfoot> | | | | |
| <th> | | | | |
| <thead> | | | | |
| <time> | | | | |
| <title> | | | | |
| <tr> | | | | |
| <track> | | | | |
| <u> | | | | |
| <ul> | | | | |
| <var> | | | | |
| <video> | | | | |
| <wbr> | | | | |
| autonomous custom elements (any author-defined tag name registered via `customElements.define`, e.g. `<my-widget>`) — one grouped row, open-ended family, not enumerable | | | | |

#### HTML elements — obsolete / non-conforming (WHATWG "Obsolete but conforming" and "Non-conforming features")

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| <acronym> (obsolete) | | | | |
| <applet> (obsolete) | | | | |
| <basefont> (obsolete) | | | | |
| <bgsound> (obsolete) | | | | |
| <big> (obsolete) | | | | |
| <blink> (obsolete) | | | | |
| <center> (obsolete) | | | | |
| <content> (obsolete, Shadow DOM v0) | | | | |
| <dir> (obsolete) | | | | |
| <font> (obsolete) | | | | |
| <frame> (obsolete) | | | | |
| <frameset> (obsolete) | | | | |
| <image> (obsolete HTML alias for `<img>` — not the SVG `<image>` element) | | | | |
| <isindex> (obsolete) | | | | |
| <keygen> (obsolete) | | | | |
| <listing> (obsolete) | | | | |
| <marquee> (obsolete) | | | | |
| <menuitem> (obsolete) | | | | |
| <multicol> (obsolete) | | | | |
| <nextid> (obsolete) | | | | |
| <nobr> (obsolete) | | | | |
| <noembed> (obsolete) | | | | |
| <noframes> (obsolete) | | | | |
| <param> (obsolete) | | | | |
| <plaintext> (obsolete) | | | | |
| <rb> (obsolete) | | | | |
| <rtc> (obsolete) | | | | |
| <shadow> (obsolete, Shadow DOM v0) | | | | |
| <spacer> (obsolete) | | | | |
| <strike> (obsolete) | | | | |
| <tt> (obsolete) | | | | |
| <xmp> (obsolete) | | | | |

#### SVG elements (embedded in HTML via inline `<svg>`)

Source enumerates elements by category (Animation, Basic shapes, Container, Descriptive, Filter primitive, Gradient, Graphics, Light source, Never-rendered, Paint server, Renderable, Shape, Structural, Text content, Uncategorized); many elements belong to more than one category on the source page, so this table lists each element once.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| <svg:a> | | | | |
| <animate> | | | | |
| <animateMotion> | | | | |
| <animateTransform> | | | | |
| <circle> | | | | |
| <clipPath> | | | | |
| <defs> | | | | |
| <desc> | | | | |
| <ellipse> | | | | |
| <feBlend> | | | | |
| <feColorMatrix> | | | | |
| <feComponentTransfer> | | | | |
| <feComposite> | | | | |
| <feConvolveMatrix> | | | | |
| <feDiffuseLighting> | | | | |
| <feDisplacementMap> | | | | |
| <feDistantLight> | | | | |
| <feDropShadow> | | | | |
| <feFlood> | | | | |
| <feFuncA> | | | | |
| <feFuncB> | | | | |
| <feFuncG> | | | | |
| <feFuncR> | | | | |
| <feGaussianBlur> | | | | |
| <feImage> | | | | |
| <feMerge> | | | | |
| <feMergeNode> | | | | |
| <feMorphology> | | | | |
| <feOffset> | | | | |
| <fePointLight> | | | | |
| <feSpecularLighting> | | | | |
| <feSpotLight> | | | | |
| <feTile> | | | | |
| <feTurbulence> | | | | |
| <filter> | | | | |
| <foreignObject> | | | | |
| <g> | | | | |
| <svg:image> | | | | |
| <line> | | | | |
| <linearGradient> | | | | |
| <marker> | | | | |
| <mask> | | | | |
| <metadata> | | | | |
| <mpath> | | | | |
| <path> | | | | |
| <pattern> | | | | |
| <polygon> | | | | |
| <polyline> | | | | |
| <radialGradient> | | | | |
| <rect> | | | | |
| <svg:script> | | | | |
| <set> | | | | |
| <stop> | | | | |
| <svg:style> | | | | |
| <svg> | | | | |
| <switch> | | | | |
| <symbol> | | | | |
| <text> | | | | |
| <textPath> | | | | |
| <svg:title> | | | | |
| <tspan> | | | | |
| <use> | | | | |
| <view> | | | | |
| SVG legacy/deprecated elements — group: `altGlyph`, `altGlyphDef`, `altGlyphItem`, `animateColor`, `cursor`, `font`, `font-face`, `font-face-format`, `font-face-name`, `font-face-src`, `font-face-uri`, `glyph`, `glyphRef`, `hkern`, `missing-glyph`, `tref`, `vkern` (SVG 1.1 font/text-on-path features; confirmed absent from MDN's current SVG element reference as of 2026-09-23 — see Sources) | | | | |

#### MathML elements (embedded in HTML via inline `<math>`)

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| <math> | | | | |
| <mi> | | | | |
| <mn> | | | | |
| <mo> | | | | |
| <ms> | | | | |
| <mspace> | | | | |
| <mtext> | | | | |
| <math:a> (MathML anchor, implements MathMLAnchorElement) | | | | |
| <menclose> | | | | |
| <merror> | | | | |
| <mfenced> | | | | |
| <mfrac> | | | | |
| <mpadded> | | | | |
| <mphantom> | | | | |
| <mroot> | | | | |
| <mrow> | | | | |
| <msqrt> | | | | |
| <mstyle> | | | | |
| <mmultiscripts> | | | | |
| <mover> | | | | |
| <mprescripts> | | | | |
| <msub> | | | | |
| <msubsup> | | | | |
| <msup> | | | | |
| <munder> | | | | |
| <munderover> | | | | |
| <mtable> | | | | |
| <mtd> | | | | |
| <mtr> | | | | |
| <maction> | | | | |
| <annotation> | | | | |
| <annotation-xml> | | | | |
| <semantics> | | | | |

### 1b. Global attributes

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| accesskey | | | | |
| anchor (CSS anchor positioning, newer) | | | | |
| autocapitalize | | | | |
| autocorrect | | | | |
| autofocus | | | | |
| class | | | | |
| contenteditable | | | | |
| data-* custom data attributes — one grouped row, open-ended family | | | | |
| dir | | | | |
| draggable | | | | |
| enterkeyhint | | | | |
| exportparts | | | | |
| headingoffset | | | | |
| headingreset | | | | |
| hidden | ✅ | the browser's own `[hidden] { display: none }`, a user-agent rule: not drawn and out of the layout, unless the page's own CSS gives the element a `display` (a rule or its style attribute), which wins and the element shows (`HtmlRenderer.HiddenByAttribute`, the one test the page's build and every laid-out state use). Set and cleared by a script: see HTMLElement.hidden | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "hidden under a display rule of the page's own: the rule wins, as it does over a browser's [hidden] { display: none }"; PlainTranslatorTests: "plain hidden: [hidden] is display: none unless the page's own CSS gives a display"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "hidden: the element leaves the layout and comes back, and one hidden from the start shows" | |
| id | | | | |
| inert | | | | |
| inputmode | | | | |
| is | | | | |
| itemid (microdata) | | | | |
| itemprop (microdata) | | | | |
| itemref (microdata) | | | | |
| itemscope (microdata) | | | | |
| itemtype (microdata) | | | | |
| lang | | | | |
| nonce | | | | |
| part | | | | |
| popover | | | | |
| role + aria-* (WAI-ARIA global attributes, ~50 aria-* properties plus role) — one grouped row | | | | |
| slot | | | | |
| spellcheck | | | | |
| style | | | | |
| tabindex | | | | |
| title | | | | |
| translate | | | | |
| virtualkeyboardpolicy | | | | |
| writingsuggestions | | | | |
| xml:lang (deprecated XHTML-compat) | | | | |
| xml:base (deprecated XHTML-compat) | | | | |

### 1c. Element-specific attributes, grouped by element

Scope: element-specific (non-global) content attributes only, per the WHATWG HTML Living
Standard. Global attributes (1b above) and `on*` event-handler attributes (1d below) are out of
scope here. Where an element's only "attribute" is a global one carrying special per-element
semantics (e.g. `title` on `<abbr>`/`<dfn>`/`<link>`/`<style>`, `dir` on `<bdo>`), it is noted but
not row-listed. SVG and MathML elements (`<svg>`, `<math>`, and their sub-elements) are out of
scope — HTML elements only.

#### `<a>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| href | | | | |
| target | | | | |
| download | | | | |
| ping | | | | |
| rel | | | | |
| hreflang | | | | |
| type | | | | |
| referrerpolicy | | | | |

#### `<abbr>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes; `title` (global) carries special "full expansion" semantics) | | | | |

#### `<address>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<area>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| alt | | | | |
| coords | | | | |
| shape | | | | |
| href | | | | |
| target | | | | |
| download | | | | |
| ping | | | | |
| rel | | | | |
| referrerpolicy | | | | |

#### `<article>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<aside>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<audio>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| src | | | | |
| crossorigin | | | | |
| preload | | | | |
| autoplay | | | | |
| loop | | | | |
| muted | | | | |
| controls | | | | |
| loading | | | | |

#### `<b>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<base>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| href | | | | |
| target | | | | |

#### `<bdi>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<bdo>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes; relies on global `dir`, required in practice) | | | | |

#### `<blockquote>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| cite | | | | |

#### `<body>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific content attributes; legacy `on*` window-event-handler attributes are event handlers, out of scope for this section — see 1d) | | | | |

#### `<br>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<button>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| command | | | | |
| commandfor | | | | |
| disabled | | | | |
| form | | | | |
| formaction | | | | |
| formenctype | | | | |
| formmethod | | | | |
| formnovalidate | | | | |
| formtarget | | | | |
| name | | | | |
| popovertarget | | | | |
| popovertargetaction | | | | |
| type | | | | |
| value | | | | |

#### `<canvas>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| width | | | | |
| height | | | | |

#### `<caption>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<cite>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<code>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<col>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| span | | | | |

#### `<colgroup>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| span | | | | |

#### `<data>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| value | | | | |

#### `<datalist>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<dd>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<del>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| cite | | | | |
| datetime | | | | |

#### `<details>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| open | | | | |
| name | | | | |

#### `<dfn>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes; `title` (global) carries special "full expansion" semantics) | | | | |

#### `<dialog>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| open | | | | |
| closedby | | | | |

#### `<div>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<dl>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<dt>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<em>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<embed>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| src | | | | |
| type | | | | |
| width | | | | |
| height | | | | |

#### `<fieldset>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| disabled | | | | |
| form | | | | |
| name | | | | |

#### `<figcaption>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<figure>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<footer>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<form>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| accept-charset | | | | |
| action | | | | |
| autocomplete | | | | |
| enctype | | | | |
| method | | | | |
| name | | | | |
| novalidate | | | | |
| rel | | | | |
| target | | | | |

#### `<h1>`–`<h6>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes; identical for h1 through h6) | | | | |

#### `<head>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<header>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<hgroup>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<hr>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<html>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes; `lang`/`xmlns` are global/namespace concerns) | | | | |

#### `<i>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<iframe>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| src | | | | |
| srcdoc | | | | |
| name | | | | |
| sandbox | | | | |
| allow | | | | |
| allowfullscreen | | | | |
| width | | | | |
| height | | | | |
| referrerpolicy | | | | |
| loading | | | | |

#### `<img>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| alt | | | | |
| src | | | | |
| srcset | | | | |
| sizes | | | | |
| crossorigin | | | | |
| usemap | | | | |
| ismap | | | | |
| width | | | | |
| height | | | | |
| referrerpolicy | | | | |
| decoding | | | | |
| loading | | | | |
| fetchpriority | | | | |

#### `<input>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| type | | | | |
| accept | | | | |
| alt | | | | |
| alpha | | | | |
| autocomplete | | | | |
| checked | | | | |
| colorspace | | | | |
| dirname | | | | |
| disabled | | | | |
| form | | | | |
| formaction | | | | |
| formenctype | | | | |
| formmethod | | | | |
| formnovalidate | | | | |
| formtarget | | | | |
| height | | | | |
| list | | | | |
| max | | | | |
| maxlength | | | | |
| min | | | | |
| minlength | | | | |
| multiple | | | | |
| name | | | | |
| pattern | | | | |
| placeholder | | | | |
| popovertarget | | | | |
| popovertargetaction | | | | |
| readonly | | | | |
| required | | | | |
| size | | | | |
| src | | | | |
| step | | | | |
| value | | | | |
| width | | | | |

#### `<ins>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| cite | | | | |
| datetime | | | | |

#### `<kbd>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<label>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| for | | | | |

#### `<legend>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<li>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| value | | | | |

#### `<link>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| href | | | | |
| crossorigin | | | | |
| rel | | | | |
| media | | | | |
| integrity | | | | |
| hreflang | | | | |
| type | | | | |
| referrerpolicy | | | | |
| sizes | | | | |
| imagesizes | | | | |
| imagesrcset | | | | |
| as | | | | |
| blocking | | | | |
| color | | | | |
| disabled | | | | |
| fetchpriority | | | | |

#### `<main>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<map>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| name | | | | |

#### `<mark>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<menu>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<meta>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| name | | | | |
| http-equiv | | | | |
| content | | | | |
| charset | | | | |
| media | | | | |

#### `<meter>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| value | | | | |
| min | | | | |
| max | | | | |
| low | | | | |
| high | | | | |
| optimum | | | | |

#### `<nav>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<noscript>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<object>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| data | | | | |
| type | | | | |
| name | | | | |
| form | | | | |
| width | | | | |
| height | | | | |

#### `<ol>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| reversed | | | | |
| start | | | | |
| type | | | | |

#### `<optgroup>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| disabled | | | | |
| label | | | | |

#### `<option>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| disabled | | | | |
| label | | | | |
| selected | | | | |
| value | | | | |

#### `<output>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| for | | | | |

#### `<p>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<picture>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes; composes `<source>` + `<img>` children) | | | | |

#### `<pre>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<progress>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| value | | | | |
| max | | | | |

#### `<q>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| cite | | | | |

#### `<rp>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<rt>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<ruby>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<s>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<samp>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<script>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| src | | | | |
| type | | | | |
| nomodule | | | | |
| async | | | | |
| defer | | | | |
| crossorigin | | | | |
| integrity | | | | |
| referrerpolicy | | | | |
| blocking | | | | |
| fetchpriority | | | | |

#### `<search>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<section>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<select>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| autocomplete | | | | |
| disabled | | | | |
| form | | | | |
| multiple | | | | |
| name | | | | |
| required | | | | |
| size | | | | |

#### `<selectedcontent>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes; mirrors the selected `<option>`'s content, part of the customizable-select feature) | | | | |

#### `<slot>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| name | | | | |

#### `<small>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<source>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| src (child of audio/video) | | | | |
| type | | | | |
| srcset (child of picture) | | | | |
| sizes (child of picture) | | | | |
| media (child of picture) | | | | |
| width (child of picture) | | | | |
| height (child of picture) | | | | |

#### `<span>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<strong>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<style>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| media | | | | |
| type | | | | |
| blocking | | | | |

#### `<sub>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<summary>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<sup>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<table>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<tbody>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<td>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| colspan | | | | |
| rowspan | | | | |
| headers | | | | |

#### `<template>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| for | | | | |
| shadowrootmode | | | | |
| shadowrootclonable | | | | |
| shadowrootdelegatesfocus | | | | |
| shadowrootserializable | | | | |
| shadowrootcustomelementregistry | | | | |
| shadowrootslotassignment | | | | |

#### `<textarea>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| autocomplete | | | | |
| cols | | | | |
| dirname | | | | |
| maxlength | | | | |
| minlength | | | | |
| placeholder | | | | |
| readonly | | | | |
| required | | | | |
| rows | | | | |
| wrap | | | | |

#### `<tfoot>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<th>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| colspan | | | | |
| rowspan | | | | |
| headers | | | | |
| scope | | | | |
| abbr | | | | |

#### `<thead>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<time>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| datetime | | | | |

#### `<title>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<tr>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<track>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| default | | | | |
| kind | | | | |
| label | | | | |
| src | | | | |
| srclang | | | | |

#### `<u>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<ul>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<var>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### `<video>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| src | | | | |
| crossorigin | | | | |
| poster | | | | |
| preload | | | | |
| autoplay | | | | |
| playsinline | | | | |
| loop | | | | |
| muted | | | | |
| controls | | | | |
| width | | | | |
| height | | | | |
| loading | | | | |

#### `<wbr>`
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no element-specific attributes) | | | | |

#### Autonomous custom elements
| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| (no standard content attributes — a custom element's attributes are entirely author-defined; not applicable to this compiler's fixed instruction set) | | | | |

### 1d. Event handler attributes (on* content attributes)

Core set defined on the `GlobalEventHandlers` mixin — applies to every HTML element plus `Window` and `Document`, which also mix it in.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| onabort | | | | |
| onauxclick | | | | |
| onbeforeinput | | | | |
| onbeforematch | | | | |
| onbeforetoggle | | | | |
| onblur | | | | |
| oncancel | | | | |
| oncanplay | | | | |
| oncanplaythrough | | | | |
| onchange | | | | |
| onclick | ✅ | the element's click handler, made before the script runs: `v_onclick(name, function(event) … end)` at the top of the chunk, the element a hit region on the scene's `on_click`; `this` is the element and `event` the click (`V_EV`); it runs in the element's listener list where it was first set (so before any listener the script adds), and a script's `el.onclick = …` replaces it. The element needs no id (the compile names it), and `<body onclick>` takes a click anywhere on the page. Refused for now: on an element drawn as part of its parent's text, and inside markup a script writes | PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "onclick attributes and this: an attribute's code with this and event, one handing this to a function, one on an element with no id, a listener's this on two elements, an onclick property's this, an attribute's handler running before a listener the script adds, and one the script replaces"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: "an onclick attribute on the body, and this there"; PlainTranslatorTests, each at 460x460, 1036x460 and 460x1036: plain-events.lua (the in-game page, not yet seen in game) | |
| onclose | | | | |
| oncommand | | | | |
| oncontextmenu | | | | |
| oncopy | | | | |
| oncuechange | | | | |
| oncut | | | | |
| ondblclick | | | | |
| ondrag | | | | |
| ondragend | | | | |
| ondragenter | | | | |
| ondragleave | | | | |
| ondragover | | | | |
| ondragstart | | | | |
| ondrop | | | | |
| ondurationchange | | | | |
| onemptied | | | | |
| onended | | | | |
| onerror | | | | |
| onfocus | | | | |
| onformdata | | | | |
| ongotpointercapture | | | | |
| oninput | | | | |
| oninterest | | | | |
| oninvalid | | | | |
| onkeydown | | | | |
| onkeypress | | | | |
| onkeyup | | | | |
| onload | | | | |
| onloadeddata | | | | |
| onloadedmetadata | | | | |
| onloadstart | | | | |
| onloseinterest | | | | |
| onlostpointercapture | | | | |
| onmousedown | | | | |
| onmouseenter | | | | |
| onmouseleave | | | | |
| onmousemove | | | | |
| onmouseout | | | | |
| onmouseover | | | | |
| onmouseup | | | | |
| onpaste | | | | |
| onpause | | | | |
| onplay | | | | |
| onplaying | | | | |
| onpointercancel | | | | |
| onpointerdown | | | | |
| onpointerenter | | | | |
| onpointerleave | | | | |
| onpointermove | | | | |
| onpointerout | | | | |
| onpointerover | | | | |
| onpointerup | | | | |
| onprogress | | | | |
| onratechange | | | | |
| onreset | | | | |
| onresize | | | | |
| onscroll | | | | |
| onscrollend | | | | |
| onsecuritypolicyviolation | | | | |
| onseeked | | | | |
| onseeking | | | | |
| onselect | | | | |
| onselectionchange | | | | |
| onselectstart | | | | |
| onslotchange | | | | |
| onstalled | | | | |
| onsubmit | | | | |
| onsuspend | | | | |
| ontimeupdate | | | | |
| ontoggle | | | | |
| onvolumechange | | | | |
| onwaiting | | | | |
| onwheel | | | | |
| onanimationcancel | | | | |
| onanimationend | | | | |
| onanimationiteration | | | | |
| onanimationstart | | | | |
| ontransitioncancel | | | | |
| ontransitionend | | | | |
| ontransitionrun | | | | |
| ontransitionstart | | | | |

`WindowEventHandlers` mixin — `Window` and `<body>`/`<frameset>` (which forward to the Window).

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| onafterprint | | | | |
| onbeforeprint | | | | |
| onbeforeunload | | | | |
| onhashchange | | | | |
| onlanguagechange | | | | |
| onmessage | | | | |
| onmessageerror | | | | |
| onoffline | | | | |
| ononline | | | | |
| onpagehide | | | | |
| onpageshow | | | | |
| onpopstate | | | | |
| onrejectionhandled | | | | |
| onstorage | | | | |
| onunhandledrejection | | | | |
| onunload | | | | |

`Document`-specific event handlers (not part of `GlobalEventHandlers`).

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| onreadystatechange | | | | |
| onvisibilitychange | | | | |

Newer/external-spec `on*` handlers found on `Window`/`Document` during sourcing (Fullscreen API, Pointer Lock API, Navigation-related, scroll-snap, Gamepad API, Device Orientation API) — each is a distinct standards-track attribute but not part of WHATWG HTML's own core two mixins above.

| Feature | Status | Maps to (vector / Lua) | Test | Reason (❌ only) |
|---|---|---|---|---|
| onfullscreenchange | | | | |
| onfullscreenerror | | | | |
| onpointerlockchange | | | | |
| onpointerlockerror | | | | |
| onpagereveal | | | | |
| onpageswap | | | | |
| onscrollsnapchange | | | | |
| onscrollsnapchanging | | | | |
| onprerenderingchange | | | | |
| ongamepadconnected | | | | |
| ongamepaddisconnected | | | | |
| ondevicemotion | | | | |
| ondeviceorientation | | | | |
| ondeviceorientationabsolute | | | | |

## Sources

- HTML Standard — Elements index — https://html.spec.whatwg.org/multipage/indices.html — accessed 2026-09-23
- HTML Standard — Non-conforming features (obsolete elements) — https://html.spec.whatwg.org/multipage/obsolete.html — accessed 2026-09-23
- HTML Standard — one page per topic for element-specific attributes: input.html, form-elements.html, tables.html, media.html, links.html, embedded-content.html, iframe-embed-object.html, image-maps.html, semantics.html, interactive-elements.html, scripting.html, grouping-content.html, text-level-semantics.html, edits.html, forms.html, sections.html — all accessed 2026-09-23
- MDN — HTML elements reference — https://developer.mozilla.org/en-US/docs/Web/HTML/Element — accessed 2026-09-23
- MDN — SVG element reference — https://developer.mozilla.org/en-US/docs/Web/SVG/Element and https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element — accessed 2026-09-23
- MDN — MathML element reference — https://developer.mozilla.org/en-US/docs/Web/MathML/Element — accessed 2026-09-23
- MDN — Global attributes — https://developer.mozilla.org/en-US/docs/Web/HTML/Global_attributes — accessed 2026-09-23
- HTML Standard — DOM / global attributes section — https://html.spec.whatwg.org/multipage/dom.html — accessed 2026-09-23 (fetch truncated before the full table; MDN used as primary, cross-checked against the partial WHATWG result)
- HTML Standard — Event handlers on elements, Document objects, and Window objects — https://html.spec.whatwg.org/multipage/webappapis.html — accessed 2026-09-23 (page too large for the fetch tool to reach the IDL block; not usable as primary source for 1d, MDN interface pages used instead)
- MDN — Window, Document, Element, HTMLElement, Node (Events sections) — accessed 2026-09-23

**Gaps flagged during research, and their resolution:**

1. *WHATWG's element index reports ~124 rows vs 114 conforming elements enumerated here.* Re-checked directly (2026-09-23): the WHATWG page states no explicit total count anywhere in its own text — every "124" or "140-145"-type figure seen in this session came from a fetch tool's own summarization of the table, not from the spec. A second independent fetch produced yet a third estimate ("approximately 140-145"). Treated as a fetch-tool artifact, not a real gap: 114 is the count actually enumerated element-by-element and cross-checked against MDN's own HTML element list. Still worth a manual recount by a future editor if exactness matters.
2. *SVG deprecated elements list could not be fetched.* Re-checked directly against both `/Web/SVG/Element` and `/Web/SVG/Reference/Element` (2026-09-23): confirmed there is no "Deprecated elements" section on MDN's current SVG element reference at all (not a fetch failure — the section genuinely isn't there any more). The grouped row above is kept as a named list of the known SVG 1.1 legacy/font/text-path elements (altGlyph family, font-face family, cursor, animateColor, hkern/vkern, tref) since they remain part of SVG 1.1 and browsers still parse them; it is no longer sourced from a live MDN "deprecated" section, which should be noted if this file is re-verified later.

## Counts

**719 total data rows** (verified by counting table rows in the assembled file, i.e. every `| ... | | | | |` line — sub-agent self-reported subtotals below are approximate hand counts, kept for orientation): 1a elements ~250 across HTML/obsolete/SVG/MathML, 1b global attributes 39, 1c element-specific attributes ~214 across 91 element headings, 1d event handlers 121.

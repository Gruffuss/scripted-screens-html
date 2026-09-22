# Every gap, enumerated

Generated. Do not hand-edit — regenerate by running the three probes and transcribing what they
print:

```bash
cd ScriptedScreensHtml.Tests
dotnet run -c Release -- --language       # JavaScript syntax + standard library
dotnet run -c Release -- --csslanguage    # CSS properties, selectors, at-rules, values
dotnet run -c Release -- --domlanguage    # DOM, HTML elements, attributes, style→scene
```

This file exists so nobody audits the same surface twice. Every name below is a thing a page
can legally write that this mod does not yet do — **except** the blocks headed `not a gap:`, which
are the opposite: things deliberately not done, each with the reason. Those are kept separate on
purpose. Folding them into the missing lists is how a correct refusal gets audited as a hole, which
cost more time on 2026-09-22 than every real gap found that day put together.

Last regenerated 2026-09-22.

---

## JavaScript

```
JavaScript syntax: 61 of 70 constructs (87%)

NOT translated:
  BigInt                             Literal is not translatable
  Promise                            `new Promise`, which neither the page nor the prelude defines i~
  Symbol                             `Symbol`, which neither the page nor the prelude defines, is no~
  async / await                      AwaitExpression is not translatable
  dynamic import                     ImportExpression is not translatable
  export                             Unexpected token 'export' (1:1)
  generator                          YieldExpression is not translatable
  import                             Cannot use import statement outside a module (1:1)
  top-level await                    await is only valid in async functions and the top level bodies~

JavaScript standard library: 165 of 174 members (95%)

  String        36/36
  Array         39/39
  Object        15/15
  Number        11/11
  Math          35/35
  JSON           2/2
  Map / Set     10/10
  Date          15/15
  RegExp         2/2
  Promise        0/9    missing: all allSettled any catch finally race reject resolve then

  (a name shared by two objects - `entries`, `values`, `delete` - counts as covered
   when ANY of them is implemented: the manifest is keyed by name alone, which is
   bug #3 in BUGS.md and makes this number optimistic.)
```

## CSS

The `not a gap:` blocks are the deliberate refusals — a console has no printer, no browser to hint,
no bidi pass, no blending and no third dimension. They are excluded from the score, not counted as
successes. `NOT drawn:` is the real remainder.

```
CSS properties: 286 of 342 (84%), plus 52 that are not gaps

not a gap:
  -webkit-font-smoothing                   the atlas has one rasterisation and no smoothing mode to pick
  -webkit-text-stroke                      a label carries colour, weight, spacing and shadow; there is no outline to ask for
  backdrop-filter                          nothing is composited behind a shape to filter
  background-attachment                    the page does not scroll under its own background
  background-blend-mode                    layers are drawn one over another, never blended
  border-image-repeat                      each slice is its own draw, so tiling an edge would cost a node per repetition
  break-after                              a console never prints, and column-count shares whole children out in order rather than fragmenting them
  break-before                             a console never prints, and column-count shares whole children out in order rather than fragmenting them
  break-inside                             a column takes whole children, so nothing can be split across a boundary to avoid
  caret-color                              a field is ScriptedScreens' own control and its caret follows the field's text colour
  column-fill                              the splitter already shares items out equally, which is `balance`; `auto` needs height fragmentation there is no pass for
  contain                                  a hint that lets a browser skip work it might otherwise redo; this page is laid out and drawn whole every time
  cursor                                   the game draws the player's own pointer, so the scene has no cursor to change
  direction                                there is no bidi and no logical mirroring in the layout, and mirroring the alignment alone would be worse than saying so
  font-feature-settings                    TextMeshPro exposes no OpenType feature table; the one that mattered, tabular figures, is faked with monospaced digit runs
  font-kerning                             kerning is a flag on the font asset, not something a single label can turn off
  font-optical-sizing                      this Unity's font engine has no variation-axis API at all, so there is no optical-size axis to set
  font-stretch                             implemented, but it picks a Condensed face by name and no face is registered in a headless run
  font-variant-ligatures                   the face's ligature table is applied whole, with no tag to suppress it
  forced-color-adjust                      there is no OS high-contrast mode to opt out of
  hanging-punctuation                      a glyph can only hang outside a line box, and a label is one rect with no per-line geometry
  hyphens                                  `auto` needs a hyphenation dictionary there is none of, and `none` is already what happens
  image-rendering                          a picture is ScriptedScreens' own image element, which exposes no sampling mode
  isolation                                nothing blends, so there is no blending group to isolate
  list-style-position                      the marker is a box beside the text, which is `outside`; `inside` needs a first line box to sit in
  mask-composite                           nesting masks gives an intersection; there is no alpha arithmetic between two of them
  mask-repeat                              a mask is a gradient, and a gradient clamps to its ends rather than tiling
  mix-blend-mode                           shapes are drawn one over another, never blended
  orphans                                  this limits the lines left at a fragment boundary, and nothing here splits a paragraph across one
  overscroll-behavior                      each scrolling box handles its own wheel and nothing chains to a parent, so `contain` is already what happens
  page-break-inside                        the print-era alias of break-inside, and a console never prints
  perspective                              the scene is flat
  perspective-origin                       the scene is flat
  print-color-adjust                       a console never prints, so there is no printer's colour economy to override
  resize                                   a console has no window edge to drag
  scroll-behavior                          smoothing a programmatic scroll means sending an offset every frame, which is the traffic a compiled page exists to remove
  scroll-margin                            reachable only through snapping or scrollIntoView, and both compute an exact jump from the boxes themselves
  scroll-padding                           reachable only through snapping or scrollIntoView, and both compute an exact jump from the boxes themselves
  scroll-snap-align                        the drag and wheel gesture lives inside the scene's own scrolling node; snapping would be a feature of that, not a translation of this
  scroll-snap-type                         the drag and wheel gesture lives inside the scene's own scrolling node; snapping would be a feature of that, not a translation of this
  tab-size                                 whitespace is collapsed before a label is built, so a tab only survives under white-space:pre, where the face's own advance draws it
  table-layout                             columns already take equal shares, which is `fixed`, so no value changes what is drawn
  text-rendering                           a hinting hint with no equivalent in a signed-distance-field atlas
  text-size-adjust                         this inflates text against a mobile browser's own zoom, and a console has none
  touch-action                             the player drives a cursor and a wheel; there are no touch gestures to opt out of
  transform-style                          the scene is flat
  transition-behavior                      implemented, but allow-discrete only shows on the SECOND emit after a display write, and the probe emits once
  unicode-bidi                             there is no bidirectional reordering pass; a label is drawn as written
  user-select                              nothing on a console is selectable, so there is no selection to allow or forbid
  widows                                   this limits the lines carried past a fragment boundary, and nothing here splits a paragraph across one
  will-change                              a hint about what to prepare for; nothing here keeps a layer to prepare
  zoom                                     this scales layout rather than paint, and the design size is fixed by the viewport meta tag

NOT drawn:
  animation-composition                    accepted, draws the same
  container-name                           accepted, draws the same
  container-type                           accepted, draws the same
  object-position                          accepted, draws the same

CSS selectors: 81 of 88 (92%), plus 7 that are not gaps

not a gap:
  ::file-selector-button                   no file picker exists
  ::first-line                             where a line breaks is only known after layout
  ::placeholder                            handed to ScriptedScreens' own field as placeholder_color, so it is not in the scene
  ::selection                              nothing selects text on a console
  :target                                  a console has no URL fragment
  :visited                                 a console has no browsing history
  column ||                                the page has no column boxes

  nothing missing

CSS at-rules: 31 of 34 (91%), plus 2 that are not gaps

not a gap:
  @import                                  there is no second file and no network to fetch it from
  @page                                    a console never prints, so there is no page box to style

NOT drawn:
  @container                               the block changed nothing

CSS values and functions: 98 of 106 (92%)

NOT drawn:
  attr()                                   "width: attr(data-w px)" is not a length this understands, ~
  image-set()                              drew something else
  lh                                       parsed to something else
  path()                                   ignored
  repeating-linear-gradient()              drew something else
  rlh                                      parsed to something else
  s / ms                                   BAD ROW: the reference draws nothing either
  url()                                    drew something else
```

`container-name` and `container-type` are the two properties that exist only to feed `@container`,
which is the one at-rule left — they are one item, not three (BUGS.md #83).

## DOM and HTML

```
1. Node and Element: 48 of 53 (91%)
  answers nothing (3):
    innerHTML (read)                  undefined   [by design]
    outerHTML                         undefined
    textContent (read)                undefined   [by design]
  refused by the compiler (1):
    insertAdjacentHTML                line 2: `.insertAdjacentHTML()`, which the prelude does not provide is no~
  WRONG ANSWER (1):
    attributes                        2

2. HTMLElement: 17 of 23 (74%)
  answers nothing (5):
    getComputedStyle (cascade)        undefined   [by design]
    scrollHeight                      undefined   [by design]
    scrollLeft                        undefined   [by design]
    scrollTop                         undefined   [by design]
    scrollWidth                       undefined   [by design]
  refused by the compiler (1):
    scrollIntoView                    line 2: `.scrollIntoView()`, which the prelude does not provide is not tr~

3. Document: 15 of 16 (94%)
  answers nothing (1):
    document.addEventListener         0

4. Events: 20 of 22 (91%)
  answers nothing (2):
    event.code                        undefined
    event.key                         undefined

5. classList, style, dataset: 16 of 16 (100%)

6. Timers and frames: 10 of 11 (91%)
  answers nothing (1):
    queueMicrotask                    0

5b. style properties that reach the scene: 13 of 47 (28%)
    width height left top opacity transform background backgroundColor borderRadius fontSize borderColor borderWidth borderTopLeftRadius
  a write the compiler refuses (34):
    alignItems              `align-items` has no equivalent in the scene
    backdropFilter          `backdrop-filter` has no equivalent in the scene
    backgroundImage         `background-image` has no equivalent in the scene
    border                  `border` has no equivalent in the scene
    bottom                  `bottom` is measured from the far edge, so its position depends on the parent's size
    boxShadow               `box-shadow` has no equivalent in the scene
    clipPath                `clip-path` has no equivalent in the scene
    color                   "e" paints a background, so its `f` slot is the box's fill rather than the text's
    cursor                  `cursor` has no equivalent in the scene
    display                 `display` has no equivalent in the scene
    filter                  `filter` has no equivalent in the scene
    flex                    `flex` has no equivalent in the scene
    flexDirection           `flex-direction` has no equivalent in the scene
    fontFamily              `font-family` has no equivalent in the scene
    fontWeight              `font-weight` has no equivalent in the scene
    gap                     `gap` has no equivalent in the scene
    gridTemplateColumns     `grid-template-columns` has no equivalent in the scene
    justifyContent          `justify-content` has no equivalent in the scene
    letterSpacing           `letter-spacing` has no equivalent in the scene
    lineHeight              `line-height` has no equivalent in the scene
    margin                  `margin` has no equivalent in the scene
    marginLeft              `margin-left` has no equivalent in the scene
    maxWidth                `max-width` has no equivalent in the scene
    minHeight               `min-height` has no equivalent in the scene
    overflow                `overflow` has no equivalent in the scene
    padding                 `padding` has no equivalent in the scene
    position                `position` has no equivalent in the scene
    right                   `right` is measured from the far edge, so its position depends on the parent's size
    strokeDasharray         `stroke-dasharray` has no equivalent in the scene
    textAlign               `text-align` has no equivalent in the scene
    textShadow              `text-shadow` has no equivalent in the scene
    transition              `transition` has no equivalent in the scene
    visibility              "e" emits no fo, so `visibility` has no slot
    zIndex                  `z-index` has no equivalent in the scene

7. HTML elements: 112 of 112 (100%)
  drawn in the scene: 93
  drawn by ScriptedScreens' own element: 8  (img picture canvas video audio input select textarea)
  correctly invisible, as in a browser: 11  (map datalist dialog template noscript head title meta link style script)

8. Global HTML attributes: 22 of 25 (88%)
  in the markup, absent from the scene (3):
    rows/cols                 changes nothing (the field's box)
    title                     changes nothing (a tooltip)
    type                      changes nothing (which control is drawn)
```

The style→scene row is the one with no `not a gap:` block, and one of its refusals is conditional
rather than absolute: `color` is refused **only** on an element that paints a background, because
the box and its label then share one `f` slot. Nothing else in that list depends on the fixture.
[GAPS-RUNTIME-STYLE.md](GAPS-RUNTIME-STYLE.md) has the order the rest is worth doing in.

# Every gap, enumerated

Generated. Do not hand-edit — regenerate with:

```bash
cd ScriptedScreensHtml.Tests && dotnet run -c Release -- --language    > ../ScriptedScreensHtml/GAPS.md
dotnet run -c Release -- --csslanguage >> ../ScriptedScreensHtml/GAPS.md
dotnet run -c Release -- --domlanguage >> ../ScriptedScreensHtml/GAPS.md
```

This file exists so nobody audits the same surface twice. Every name below is a thing a page
can legally write that this mod does not yet do.

---

## JavaScript

```
JavaScript syntax: 56 of 70 constructs (80%)

NOT translated:
  BigInt                             Literal is not translatable
  Promise                            `new Promise`, which neither the page nor the prelude defines i~
  Symbol                             `Symbol`, which neither the page nor the prelude defines, is no~
  async / await                      AwaitExpression is not translatable
  dynamic import                     ImportExpression is not translatable
  export                             Unexpected token 'export' (1:1)
  generator                          YieldExpression is not translatable
  import                             Cannot use import statement outside a module (1:1)
  labelled break                     LabeledStatement is not translatable
  private class field                that property access is not translatable
  regex literal                      the regular expression /[0-9]+/g is not translatable
  static block                       a static initialisation block is not translatable
  tagged template                    a tagged template is not translatable
  top-level await                    await is only valid in async functions and the top level bodies~

JavaScript standard library: 119 of 174 members (68%)

  String        30/36   missing: match matchAll normalize raw fromCharCode fromCodePoint
  Array         33/39   missing: copyWithin reduceRight toReversed toSorted toSpliced with
  Object         7/15   missing: create defineProperty getOwnPropertyNames getPrototypeOf hasOwnProperty is seal setPrototypeOf
  Number         9/11   missing: isSafeInteger parseInt
  Math          25/35   missing: acosh asinh atanh cosh expm1 fround imul log1p sinh tanh
  JSON           2/2  
  Map / Set     10/10 
  Date           3/15   missing: getDate getDay getFullYear getHours getMilliseconds getMinutes getMonth getSeconds getTimezoneOffset toISOString toLocaleDateString toLocaleTimeString
  RegExp         0/2    missing: exec test
  Promise        0/9    missing: all allSettled any catch finally race reject resolve then

  (a name shared by two objects - `entries`, `values`, `delete` - counts as covered
   when ANY of them is implemented: the manifest is keyed by name alone, which is
   bug #3 in BUGS.md and makes this number optimistic.)
```

## CSS

```
CSS properties: 195 of 342 (57%)

NOT drawn:
  -webkit-font-smoothing                   accepted, draws the same
  -webkit-text-fill-color                  accepted, draws the same
  -webkit-text-stroke                      refused
  accent-color                             accepted, draws the same
  all                                      accepted, draws the same
  animation-composition                    accepted, draws the same
  animation-delay                          accepted, draws the same
  animation-direction                      accepted, draws the same
  animation-duration                       accepted, draws the same
  animation-fill-mode                      accepted, draws the same
  animation-iteration-count                accepted, draws the same
  animation-play-state                     accepted, draws the same
  animation-timing-function                accepted, draws the same
  appearance                               accepted, draws the same
  aspect-ratio                             accepted, draws the same
  backdrop-filter                          accepted, draws the same
  backface-visibility                      accepted, draws the same
  background-attachment                    accepted, draws the same
  background-blend-mode                    refused
  background-origin                        accepted, draws the same
  background-position                      accepted, draws the same
  background-position-x                    accepted, draws the same
  background-position-y                    accepted, draws the same
  background-repeat                        accepted, draws the same
  background-size                          accepted, draws the same
  border-bottom-color                      accepted, draws the same
  border-collapse                          accepted, draws the same
  border-image-outset                      accepted, draws the same
  border-image-repeat                      accepted, draws the same
  border-image-slice                       accepted, draws the same
  border-left-color                        accepted, draws the same
  border-right-color                       accepted, draws the same
  border-spacing                           accepted, draws the same
  break-after                              accepted, draws the same
  break-before                             accepted, draws the same
  break-inside                             accepted, draws the same
  caption-side                             accepted, draws the same
  caret-color                              accepted, draws the same
  clear                                    accepted, draws the same
  clip-rule                                accepted, draws the same
  color-scheme                             accepted, draws the same
  column-fill                              accepted, draws the same
  column-span                              accepted, draws the same
  columns                                  accepted, draws the same
  contain                                  accepted, draws the same
  container-name                           accepted, draws the same
  container-type                           accepted, draws the same
  content-visibility                       accepted, draws the same
  cursor                                   accepted, draws the same
  cx                                       accepted, draws the same
  d                                        accepted, draws the same
  direction                                accepted, draws the same
  dominant-baseline                        accepted, draws the same
  empty-cells                              accepted, draws the same
  filter                                   accepted, draws the same
  flex-direction                           accepted, draws the same
  flex-flow                                accepted, draws the same
  font-feature-settings                    accepted, draws the same
  font-kerning                             accepted, draws the same
  font-optical-sizing                      accepted, draws the same
  font-stretch                             accepted, draws the same
  font-style                               accepted, draws the same
  font-synthesis                           accepted, draws the same
  font-variant                             accepted, draws the same
  font-variant-caps                        accepted, draws the same
  font-variant-ligatures                   accepted, draws the same
  font-variant-numeric                     accepted, draws the same
  forced-color-adjust                      accepted, draws the same
  grid-auto-columns                        accepted, draws the same
  grid-auto-flow                           accepted, draws the same
  grid-row-end                             accepted, draws the same
  grid-template-areas                      accepted, draws the same
  hanging-punctuation                      accepted, draws the same
  hyphens                                  accepted, draws the same
  image-rendering                          accepted, draws the same
  initial-letter                           accepted, draws the same
  isolation                                accepted, draws the same
  justify-items                            accepted, draws the same
  list-style-position                      accepted, draws the same
  margin-block-end                         accepted, draws the same
  margin-bottom                            accepted, draws the same
  marker-end                               accepted, draws the same
  mask-composite                           refused
  mask-mode                                refused
  mask-position                            accepted, draws the same
  mask-repeat                              accepted, draws the same
  mix-blend-mode                           accepted, draws the same
  object-fit                               accepted, draws the same
  object-position                          accepted, draws the same
  offset-rotate                            accepted, draws the same
  orphans                                  accepted, draws the same
  outline-style                            accepted, draws the same
  overflow-inline                          accepted, draws the same
  overflow-wrap                            accepted, draws the same
  overflow-x                               accepted, draws the same
  overscroll-behavior                      accepted, draws the same
  page-break-inside                        accepted, draws the same
  perspective                              accepted, draws the same
  perspective-origin                       accepted, draws the same
  place-self                               accepted, draws the same
  pointer-events                           accepted, draws the same
  print-color-adjust                       accepted, draws the same
  quotes                                   accepted, draws the same
  resize                                   accepted, draws the same
  row-gap                                  accepted, draws the same
  ry                                       accepted, draws the same
  scroll-behavior                          accepted, draws the same
  scroll-margin                            accepted, draws the same
  scroll-padding                           accepted, draws the same
  scroll-snap-align                        accepted, draws the same
  scroll-snap-type                         accepted, draws the same
  scrollbar-gutter                         accepted, draws the same
  stroke-linecap                           accepted, draws the same
  stroke-linejoin                          accepted, draws the same
  stroke-miterlimit                        accepted, draws the same
  tab-size                                 accepted, draws the same
  table-layout                             accepted, draws the same
  text-align-last                          accepted, draws the same
  text-anchor                              accepted, draws the same
  text-decoration-color                    accepted, draws the same
  text-decoration-style                    accepted, draws the same
  text-decoration-thickness                accepted, draws the same
  text-orientation                         accepted, draws the same
  text-overflow                            accepted, draws the same
  text-overflow (longhand)                 accepted, draws the same
  text-rendering                           accepted, draws the same
  text-size-adjust                         accepted, draws the same
  text-underline-offset                    accepted, draws the same
  text-underline-position                  accepted, draws the same
  text-wrap                                accepted, draws the same
  touch-action                             accepted, draws the same
  transform-box                            accepted, draws the same
  transform-origin                         accepted, draws the same
  transform-style                          accepted, draws the same
  transition-behavior                      accepted, draws the same
  transition-delay                         accepted, draws the same
  transition-duration                      accepted, draws the same
  transition-timing-function               accepted, draws the same
  unicode-bidi                             accepted, draws the same
  user-select                              accepted, draws the same
  vector-effect                            accepted, draws the same
  vertical-align                           accepted, draws the same
  widows                                   accepted, draws the same
  will-change                              accepted, draws the same
  word-spacing                             accepted, draws the same
  word-wrap                                accepted, draws the same
  zoom                                     accepted, draws the same

CSS selectors: 64 of 88 (73%)

NOT drawn:
  ::-webkit-scrollbar                      parsed, painted nothing
  ::backdrop                               parsed, painted nothing
  ::file-selector-button                   "file-selector-button" matches nothing here: no text select~
  ::first-line                             parsed, painted nothing
  ::placeholder                            parsed, painted nothing
  ::selection                              "selection" matches nothing here: no text selection, shadow~
  :active                                  parsed, painted nothing
  :focus                                   parsed, painted nothing
  :focus-visible                           parsed, painted nothing
  :focus-within                            parsed, painted nothing
  :hover                                   parsed, painted nothing
  :in-range                                parsed, painted nothing
  :indeterminate                           parsed, painted nothing
  :modal                                   parsed, painted nothing
  :nth-child(B of S)                       parsed, painted nothing
  :popover-open                            parsed, painted nothing
  :target                                  parsed, painted nothing
  :user-invalid                            parsed, painted nothing
  :user-valid                              parsed, painted nothing
  :valid                                   parsed, painted nothing
  :visited                                 parsed, painted nothing
  [attr=v i] case-insensitive              parsed, painted nothing
  [attr=v s] case-sensitive                parsed, painted nothing
  column ||                                parsed, painted nothing

CSS at-rules: 24 of 34 (71%)

NOT drawn:
  @counter-style                           the block changed nothing
  @import                                  the block changed nothing
  @media (hover)                           the block changed nothing
  @media (pointer)                         the block changed nothing
  @media (prefers-color-scheme)            the block changed nothing
  @media (prefers-reduced-motion)          the block changed nothing
  @media (resolution)                      the block changed nothing
  @media range syntax                      the block changed nothing
  @page                                    @page skipped
  @property                                the block changed nothing

CSS values and functions: 84 of 106 (79%)

NOT drawn:
  asin()                                   parsed to something else
  atan2()                                  parsed to something else
  attr()                                   parsed to something else
  calc() percent                           parsed to something else
  color()                                  ignored
  dvh                                      parsed to something else
  dvw                                      parsed to something else
  env() fallback                           parsed to something else
  grad                                     parsed to something else
  image-set()                              ignored
  lh                                       parsed to something else
  light-dark()                             parsed to something else
  lvh                                      parsed to something else
  path()                                   ignored
  percent                                  parsed to something else
  repeating-linear-gradient()              drew something else
  rlh                                      parsed to something else
  s / ms                                   BAD ROW: the reference draws nothing either
  svh                                      parsed to something else
  svw                                      parsed to something else
  url()                                    ignored
  var()                                    ignored
```

## DOM and HTML

```

1. Node and Element: 35 of 53 (66%)
  answers nothing (11):
    attributes                        0
    id                                undefined
    innerHTML (read)                  undefined   [by design]
    matches (tag.class)               false
    nextElementSibling                false
    nextSibling                       false
    nodeType                          undefined
    outerHTML                         undefined
    previousSibling                   false
    tagName (own markup)              undefined
    textContent (read)                undefined   [by design]
  refused by the compiler (3):
    insertAdjacentElement             line 5: `.insertAdjacentElement()`, which the prelude does not provide is~
    insertAdjacentHTML                line 2: `.insertAdjacentHTML()`, which the prelude does not provide is no~
    insertAdjacentText                line 2: `.insertAdjacentText()`, which the prelude does not provide is no~
  COMPILES THEN THROWS (3):
    contains                          Lua-CSharp: [string "prelude"]:512: the page calls .contains() on a objec~
    querySelector                     Lua-CSharp: [string "prelude"]:512: the page calls .querySelector() on a ~
    querySelectorAll                  Lua-CSharp: [string "prelude"]:512: the page calls .querySelectorAll() on~
  WRONG ANSWER (1):
    createElement per call            shared

2. HTMLElement: 7 of 23 (30%)
  answers nothing (12):
    clientHeight                      0   [by design]
    clientWidth                       0   [by design]
    getBoundingClientRect             0   [by design]
    getComputedStyle (cascade)        undefined   [by design]
    offsetHeight                      0   [by design]
    offsetLeft                        0   [by design]
    offsetTop                         0   [by design]
    offsetWidth                       0   [by design]
    scrollHeight                      undefined   [by design]
    scrollLeft                        undefined   [by design]
    scrollTop                         undefined   [by design]
    scrollWidth                       undefined   [by design]
  refused by the compiler (4):
    blur                              line 2: `.blur()`, which the prelude does not provide is not translatable
    click                             line 2: `.click()`, which the prelude does not provide is not translatable
    focus                             line 2: `.focus()`, which the prelude does not provide is not translatable
    scrollIntoView                    line 2: `.scrollIntoView()`, which the prelude does not provide is not tr~

3. Document: 9 of 16 (56%)
  answers nothing (5):
    document.addEventListener         0
    document.getElementsByTagName     0   [by design]
    document.querySelector (.class)   false
    document.querySelectorAll         0   [by design]
    head                              false
  refused by the compiler (2):
    createDocumentFragment            line 1: `.createDocumentFragment()`, which the prelude does not provide i~
    createTextNode                    line 1: `.createTextNode()`, which the prelude does not provide is not tr~

4. Events: 5 of 22 (23%)
  answers nothing (17):
    addEventListener                  0
    bubbling to a parent              0
    event.altKey                      undefined
    event.bubbles                     undefined
    event.button                      undefined
    event.clientX/clientY             0
    event.code                        undefined
    event.ctrlKey                     undefined
    event.currentTarget               false
    event.key                         undefined
    event.offsetX/offsetY             0
    event.preventDefault              undefined
    event.shiftKey                    undefined
    event.target                      false
    event.type                        
    onclick property                  0
    window.addEventListener           0

5. classList, style, dataset: 12 of 16 (75%)
  answers nothing (3):
    classList item access             undefined
    classList.length                  0
    style.cssText                     undefined
  COMPILES THEN THROWS (1):
    classList.replace                 Lua-CSharp: [string "prelude"]:512: the page calls .replace() on a object~

6. Timers and frames: 3 of 11 (27%)
  answers nothing (6):
    an interval beside a rAF loop     0
    requestAnimationFrame             0
    setInterval                       0
    setInterval honours its delay     0
    setTimeout runs                   0
    setTimeout with no delay          0
  refused by the compiler (1):
    queueMicrotask                    line 1: `queueMicrotask`, which neither the page nor the prelude defines,~
  WRONG ANSWER (1):
    rAF is given a timestamp          none

5b. style properties that reach the scene: 11 of 47 (23%)
    width height left top opacity transform background backgroundColor color borderRadius fontSize
  a write the compiler refuses (36):
    alignItems              `align-items` has no equivalent in the scene
    backdropFilter          `backdrop-filter` has no equivalent in the scene
    backgroundImage         `background-image` has no equivalent in the scene
    border                  `border` has no equivalent in the scene
    borderColor             `border-color` has no equivalent in the scene
    borderTopLeftRadius     `border-top-left-radius` has no equivalent in the scene
    borderWidth             `border-width` has no equivalent in the scene
    bottom                  `bottom` is measured from the far edge, so its position depends on the parent's size
    boxShadow               `box-shadow` has no equivalent in the scene
    clipPath                `clip-path` has no equivalent in the scene
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
    visibility              `visibility` has no equivalent in the scene
    zIndex                  `z-index` has no equivalent in the scene

7. HTML elements: 111 of 112 (99%)
  drawn in the scene: 93
  drawn by ScriptedScreens' own element: 8  (img picture canvas video audio input select textarea)
  correctly invisible, as in a browser: 10  (map datalist template noscript head title meta link style script)
  takes room but paints nothing (1):
    dialog

8. Global HTML attributes: 16 of 25 (64%)
  in the markup, absent from the scene (9):
    colspan                   changes nothing (a cell spanning two columns)
    colspan on a wide cell    changes nothing (the span is read, not assumed)
    colspan on th             changes nothing (a header spanning two columns)
    href                      changes nothing (a link is coloured)
    rows/cols                 changes nothing (the field's box)
    start on ol               changes nothing (the first number)
    title                     changes nothing (a tooltip)
    type                      changes nothing (which control is drawn)
    value on li               changes nothing (one item's number)
```

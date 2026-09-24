# ScriptedScreens Html changelog

Newest first. The workshop page is a short overview; this file has the full history.

## Unreleased

- A page is compiled once, for the size of the console it sits on, into a vector scene and a short
  Lua program on the console's chip, written the way a hand-made vector console is. After that,
  nothing of this mod runs for it: the page's timers run on the chip's own tick, clicks arrive through
  the scene, and only changed values are sent. This covers pages whose script uses `setTimeout`,
  `setInterval` and their `clear` calls, click listeners and `onclick`, `getElementById`, writes to
  `textContent`, `className`, `classList` and `style`, `toFixed`, `location` (a console page has no URL,
  so it reads as `about:blank`; the `#hash` works), `localStorage` (kept in the chip's own store, so it
  survives), `sessionStorage`, `innerWidth`/`innerHeight`, attributes (including ones CSS selects on),
  `hidden`, `dataset`, and reading back what the script wrote. `console` calls are removed. Other pages
  still run as before, and the log says which feature kept a page from compiling.
- Compiled that way too: `querySelector`, `querySelectorAll`, `getElementsByClassName` and
  `getElementsByTagName` (on the document or on an element), the lists they give (`length`, `item()`,
  `forEach`, an index, `for...of`), an element chosen while the page runs (`getElementById('row' + i)`,
  an array of elements, an element passed to a function), a value from a fixed set that comes back in a
  function's returned object, `innerText`, `textContent +=`, and `classList.item()`, `.length` and
  `.value`.
- A one-line label with padding is no longer taken for a wrapping one: a 22px text with 4px of padding
  above and below, holding a space, was drawn wrapped at its box's width depending on how its position
  rounded, and a script writing such a text was not compiled.
- `innerHTML` compiles too, when the markup's shape comes from the page's own text: literals, `+`,
  template literals, ternaries and helper functions returning markup. The markup is laid out once; the
  console then only writes the values in it and picks which shape shows.
- Lists of markup compile too: `.map(...).join('')` (with `slice` and `filter` before it), a loop adding
  markup to a name (`h += ...`) or to the element (`el.innerHTML += ...` after `el.innerHTML = ...`), and
  `forEach`. A list is laid out once at the most rows its array can hold - read from the page's own code:
  a fixed array, an array held to a length (`if (log.length > 4) log.pop()` after a push, and similar), a
  slice of fixed size - and the console shows as many rows as the array has, what follows moving up as a
  browser would lay it out. A row whose markup depends on its item picks its own shape. The rows can be
  looked up afterwards (`querySelectorAll`), each given a listener, or reached from one listener through
  `e.target`, `closest()`, `dataset`, `matches()`, `id` and `tagName`. A list whose length the compile
  cannot bound is refused, and the log says why.
- A list kept in a field of an object compiles too, followed as a list in a name is: `state.log =
  [line].concat(state.log).slice(0, 5)`, `state.items = state.items.filter(...)`, a push under a length
  test, the object passed to helpers, patched with `Object.assign` (`set({ log: [...].concat(state.log)
  .slice(0, 3) })` in a `set(patch)` helper included) or read back through another object.
  A list in a field that grows with nothing holding it is now refused by name; before, a `push` into such
  a list went unseen and it was drawn one row long.
- Rows with many choices compile: an icon, a badge and a note each chosen on their own make each its own
  state instead of every combination of them, and only choices that move the same things are combined.
  A choice the row's own index or item decides (`i > 0 ? '<div class="sep"></div>' : ''`) is made once,
  when the page is compiled.
- `Object.assign(target, ...)` in a compiled script changes its target, as in a browser; it made a new
  object and left the target as it was.
- `[x].concat(obj.list)` adds the items of `obj.list`; it was counted as one item.
- A list passed to a helper that takes items from it (`a.pop()` there) shows fewer rows as it shrinks;
  the rows it dropped stayed drawn.
- A table built by a function called where it is written (`const ROLES = (() => { ... return [...]; })()`) is
  a list and a source of fixed values, as a table written out is. An item found in a list with `find` (with a
  `|| list[0]` or `?? fallback`) has its fields read as the values the list's items can have, and its own lists
  as lists; the same for each item a callback walking a list is given, and for rows made by a `map` callback.
- A row of a list takes its colour, class or style from its item's fields (`style="color:${e.color}"`) as one of
  the values those fields can have, also when the list is kept in a state object and rebuilt with `concat` and
  `slice`, and when an item is an array read by index (`d[1]`).
- A style colour only known at run time is now said as such; the log read `style.color ... in "no unit"`.
- An object patched with `Object.assign` is no longer read as a table of constants: a field it changes that way
  kept its first value where a fixed value was needed.
- A style written from a fixed set on an element without a class left an empty `class` attribute on the page.
- `array.length = n` works (it clears or shortens an array as JavaScript does).
- A compiled page sends only what changed since its last send: a value set and set back in one go is no
  longer sent again.
- A value in markup that is a whole number the compile knows (`${i + 1}` in a row) is added as a number,
  not joined as text ("1", not "01").
- A name given markup in steps with template literals (`` h += `<li>${x}</li>` ``) is markup: its text was
  written as plain text, tags and all.
- Markup with elements written into an element that only held text no longer stops a page from
  compiling on the older path; it runs as before, and the log says why it was not compiled plainly.
- An element whose class is set with `className` from a fixed set lays out only the class sets it can
  be given, so a class that removes the element's background in some other combination no longer
  stops the page compiling.
- `null` now prints as "null" where a page turns it into text (`'x' + el.getAttribute('y')`, a template,
  `String()`), and a method called on an array written in place (`[1, 2].forEach(f)`) works.
- A console in a room with no player compiles and runs its page too; before, it waited until someone
  walked in.
- `parseInt` and `parseFloat` read a leading number as JavaScript does: `parseInt("10px")` is 10, not
  NaN.
- A stylesheet `%` width no longer overrides a width set later by a class or an inline style.
- `onclick="..."` on an element compiles: its code runs as the element's click handler, with `this`
  the element and `event` the click, before any listener the script adds, as in a browser. A script
  setting `el.onclick` replaces it. The element needs no id. An `onclick` handler set by the script
  also takes its turn among the element's listeners in the order it was first set; it always ran last.
  Other `on...` attributes are still refused, and so is `onclick` inside markup a script writes (before,
  that one was dropped without a word).
- `this` in a click listener (a `function`, an `onclick` handler or attribute) is the element it listens
  on, as `e.currentTarget` is.
- A `for` loop adding markup can count from any number, up or down, by any fixed step: `i = 1; i <= n`,
  `i = 4; i > 0; i--`, `i += 2`. Before, only a count from 0 by 1 compiled. A count loop that skips
  rows (`if (...) continue`) is now refused by name; it compiled and drew every row.
- A boolean, `undefined` or an array written into text now shows as JavaScript prints it: `'all on: ' +
  list.every(...)` left the old text on the console, because the value went out as a Lua boolean.
  `toString()` and `valueOf()` on a boolean work; they stopped the page's Lua with an error.
- `hidden` on an element the page's CSS gives a `display` (`.row { display: flex }`) no longer hides it:
  the browser's own `[hidden] { display: none }` gives way to the page's rules.
- A console is laid out in whole CSS pixels: `innerHeight` read 459 on a 460x460 console and 1035 on a
  460x1036 one.
- An element with no id that the script drives on a page that also writes markup lost its entry in the
  page's ids after the compile.
- Clicks kept as functions in an array compile: a helper that pushes a function and returns an attribute
  for the markup (`'<div' + act(fn) + ' style="...">'`, returning `' data-act="' + (acts.length - 1) +
  '"'`), then `document.querySelectorAll('[data-act]')` after the write and a listener on each calling
  `acts[Number(el.getAttribute('data-act'))]()`. The attribute's name is fixed and its value is the
  number the helper returned; `cond ? act(fn) : ''` gives an element the attribute or not. A function
  made in a list's row keeps the row it was made in, as a JavaScript closure does.
- A list looked up over elements markup makes in only some of its shapes (a tab that is one of two
  elements, rows that have an attribute or not) holds what is shown when it is looked up, as a
  browser's list does; it was refused. A first match (`querySelector`) there is still refused.
- `[name]` in a selector asks only whether an attribute is there, so an attribute markup gives a value
  no longer stops a lookup by its presence from compiling.
- A page whose only code is in `onclick` attributes, with no `<script>`, runs them: it was treated as a
  page driven by data alone, and its clicks did nothing.
- The children of a flex or grid container are items with boxes of their own, as CSS makes them: a
  `<span>` with no id or class in a `display: flex` span or a grid row was folded into its parent's
  text, so a value only known at run time in its attributes (`style="color:' + e.color + '"`) stopped
  the page from compiling.
- `gap` in a flex container is laid out by the layout itself, between the items shown: rows of a list
  whose shape changes with their item (two shapes of one size) were refused as "another size" when a gap
  spaced them, and now compile as they did with margins. `gap` on a block's children (not a flex box)
  no longer adds space, as in a browser, and a wrapping flex row takes its gaps between lines and items
  exactly.
- `getAttribute('data-click')` and `hasAttribute` on an element markup makes read null and false, as for
  any attribute the page never wrote: they read the compile's own click-region marker.
- A style attribute written in markup from a value the page picks from a fixed set
  (`'<div style="' + r.style + '">'`) compiles: each of its values is laid out as the style attribute it
  is. What is inside it is laid out as that style says, so the children of a `display: flex` or `grid`
  value are boxes, a value only known at run time can sit in their attributes, and a value that is
  `flex` one time and `block` another gives the element one shape per style.
- One markup write with more than 32 shapes compiles when its choices are separate: a badge here, an
  icon there, a panel that switches each keep a state of their own, and only choices that move the same
  things are combined. A write it still cannot lay out names the choices in its message.
- A list turned round in place (`items.push(items.shift())`, `items.unshift(items.pop())`, or a
  `shift()` right before a `push()`) keeps its length; it was refused as a list whose length cannot be
  bounded.
- A list callback may read the array it walks, its third argument (`arr.map((x, i, all) => ... all.length
  ...)`), in `map` and `forEach`: over the array itself, or after `filter` and `slice` over what is left
  of it, which the console keeps as the rows are counted. Marking the last row and counting the rows no
  longer stop a page from compiling.
- `document.documentElement` compiles: the root element, drawn as one box with the body, so a theme
  attribute set on it (`setAttribute('data-mode', 'dark')`) recolours everything its custom properties
  reach, and reads back.
- `scrollTop` and `scrollLeft` of an element that is not a scroll box read 0 and ignore a write, as in a
  browser. On a scroll box (`overflow` auto, scroll or hidden) they are refused and the log says why: the
  offset the player scrolled to stays on each client and never reaches the chip.
- Custom properties from a rule that stopped matching (a class or attribute taken away) no longer stay
  on the element: after a theme attribute went from "green" back to none, the green accent stayed.
- The page rebuilt from its own markup keeps its styles: `<style>` text was written with its quotes as
  `&quot;`, so a selector like `[data-mode="light"]` stopped matching.
- `innerHTML` compiles when it is written into an element chosen while the page runs: one of two ids
  (`getElementById(on ? 'a' : 'b')`), an element of a list, an element passed to a function, an id a
  helper is given. The markup is laid out once into each element it can reach, and the console picks
  the one the script holds. An element a write at load does not reach shows what the page wrote there.
- `getElementById` with an id built while the page runs compiles in more forms: through a helper
  (`const $ = (id) => document.getElementById(id)`), from `this.id` in a getter of objects pushed into
  an array (`get el() { return $(this.id); }`, then `o.el` is an element), and for an element markup
  makes (`$('pad' + i)` after the markup gave it `id="pad0"`...).
- `requestAnimationFrame` and `cancelAnimationFrame` compile: a game loop runs on the chip's per-frame
  callback, every frame, its callbacks given the time in ms since the page loaded, as `performance.now()`
  counts it. The page takes the chip's frame callback only while a frame is queued and hands it back
  within half a second of the last one, so a page that stopped its loop costs nothing per frame; a frame
  callback the chip's own program registered keeps running, after the page's.
- `mousedown`, `mouseup`, `mouseleave`, `pointerdown`, `pointerup` and `pointerleave` listeners compile, for
  press-and-hold buttons: the element reports its presses through the scene (vector `press=1`), pointer
  events first, bubbling as a browser does (a leave only on the element itself). As the vector mod reports
  them: an up arrives for the element pressed wherever the pointer is released, a leave once per press,
  and two presses less than a quarter of a second apart arrive as one.

## 0.2.0

- A page without a script, driven by Lua data, sends its values straight to the scene once the
  mapping has proved itself against what the page drew - no layout, translate or emit per tick.
  A CSS transition on such a value glides on the renderer with the declared duration, curve and
  delay (vector mod 0.11.33 or later); everything else snaps, as a browser does.
- `animation-composition: add` and `accumulate`; a keyframe loop on an element with its own
  transform no longer runs offset by it.
- `attr()` outside `content`, the `lh` and `rlh` units, `clip-path: path()`.
- `line-height: 1.5em` was applied twice over.
- `@container` queries re-cascade when the container's size changes.
- `object-position` places the picture (vector mod 0.11.33's IMG `at`); a url() background honours
  `background-position`, from the top-left corner as CSS starts it.
- `@supports` reads nested conditions, selector() and strings as a browser does, and answers yes for
  gap, grid and the other properties drawn outside the cascade.
- `clip-path` reference boxes.
- A `background` shorthand whose url has a slash in it is no longer stretched.
- No "not drawn" warning for sixteen properties that are drawn.
- A data page compiles in the emit that draws its first payload; a key it cannot place is dropped
  alone and named once; keys first sent later map on arrival.
- The three Atmo mockups compile: a panel rebuilt through `innerHTML` is laid out once with every
  alternative and row, and the chip writes values into it.
- `@font-face` with an `https` link (a Google Fonts stylesheet included) loads through the Fonts mod.
- Pages are laid out by a layout engine inside the mod instead of Unity's UI Toolkit, and each
  page runs its script, animations and layout on a thread of its own. The game thread only
  hands finished scenes to the vector mod. The layout engine is Yoga (MIT); THIRD-PARTY-NOTICES.md
  in the mod folder carries its licence and those of the JavaScript engine.
- A page rebuilt for a screen capture runs its script to the first render, then gets the data Lua
  sent, then a few frames, so the capture shows the page as its script draws it.
- The Apple mockup keeps its lists' scroll positions when it redraws.
- `examples/07-game.lua`: Stationeer Run, an endless runner played with clicks, with a demo that
  plays itself until someone taps. It fits the console it is on: a wide (2x1) screen gets a
  compact header and pads, and the ground, the scenery and the jump follow the height that leaves.
- `requestAnimationFrame` runs once per game frame, and a frame's writes reach the screen in the
  next one (animation frames were capped at 30 a second and landed two or three frames late).
- `document.getElementById` returns the same object for the same element, and a lookup no longer
  waits for the frame's style writes to be applied (a script that moved elements ran at 3 frames
  a second).
- An element moved by a script sends new values, not a new scene structure.
- A class or attribute change undoes what the rules that stopped matching had set (a title stayed
  red after its parent lost the class that coloured it); a script's `element.style` values survive
  the re-cascade and win over the rules, as the style attribute does.
- Writing the same `element.style`, class or text again changes nothing, so a page whose script
  rewrites unchanged values every frame is not laid out and translated again.
- A frame's style, text and class writes from a page script are handed over as one piece of work
  instead of one each, and a property name is spelled as CSS spells it once, not per write.
- Numbers and colours are written straight into the scene buffer instead of one string each: a
  frame writes thousands of them.
- A page that emits every frame makes far less garbage: the scene's template split reads the scene
  in place and keeps its buffer, names and values; the emitter keeps its buffers and lists. Collections
  are what a stuttering console feels, and they came every second or two with a few animated pages.
- A script's `element.style.setProperty('--name', ...)` declares the custom property on the
  element and re-runs the rules that read it with `var()`, as a browser does.
- A number with `tabular-nums` set on an ancestor keeps one width while it counts; the width pass
  ignored inherited values, so a changing score wrapped under its label for a frame.
- A rounded box with some sides unbordered (`border-bottom: none`) draws the others along its
  rounded corners.
- Pages are laid out with the faces the Fonts mod and the game registered, mirrored by name; the
  mod reads no font files and knows no font folders (a Workshop install names mod folders by item
  id, and player fonts live where the Fonts mod keeps them). A face registered later is picked up
  within seconds.

- Documentation and examples are published to the StationeersLua MCP: search scope `html`,
  `stationeers://html/index` as the quick start, the guide and SUPPORT.md one resource per
  section, the changelog, and every chip under `examples/`.
- `examples/`: six chips that run on paste, one idea each; `mockups/`: the three Atmo Regulator
  consoles written as pages, served as `stationeers://html-mockups/` in a search scope of their own.
- Text nodes directly inside a flex or grid container are items of their own (a lamp span
  followed by a word was dropped).
- The `font` shorthand's longhands reach the cascade record (family, weight, line height), so a
  label draws in the face the page names (Barlow Condensed Bold, not the body's Barlow).
- The layout measures with the real weight face the scene draws, not a synthetic bold.
- Glyphs a face lacks (subscript digits, the gear) are drawn from the game's own face, as a
  browser falls back to a system font; generic families map to a face inside rich text.
- Layered `background` values: the colour layer under gradient layers, each at its own position
  and size (fill edges, corner marks).
- `text-overflow: ellipsis` only where declared; other clipped labels are clipped by their box.
- `font-variant-numeric: tabular-nums` widens the label's box by what the digit cells add; the
  cell is the face's widest digit.
- Flex `gap` survives innerHTML and re-cascades; the baseline pass owns margin-bottom in a row.
- A declared width or height under a pixel lays out as one pixel.
- An undefined `var()` with no fallback drops its declaration, as in a browser.
- `display: inline-block` from a stylesheet makes a box, not only from the inline style.
- innerHTML assigns ids and fills the attribute cache on the worker: a render's handler loop no
  longer waits a game frame per `getAttribute`; a worker read never starves.
- Keyframe runners of removed elements are pruned; a mid-frame drain for a worker read does not
  emit; the emit waits for a settled layout.
- Diagnostics: each dirty cause and the runner count on the per-page line; the config file is
  re-read when it changes on disk.

## 0.1.0

- First release: HTML, CSS and JavaScript pages on ScriptedScreens consoles, drawn through
  ScriptedScreens Vector.

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

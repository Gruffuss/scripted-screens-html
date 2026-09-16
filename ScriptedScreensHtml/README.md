# ScriptedScreens Html

Write a console screen as a web page. HTML for the structure, CSS for the look, JavaScript
if you want it, and Lua only for the data. The page is drawn by the ScriptedScreens Vector
mod as crisp geometry at any distance, in real fonts, and a page that does not change costs
the game nothing per frame.

```lua
local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()

ui:element({
    id = "screen", type = "html",
    rect = { unit = "px", x = 0, y = 0, w = 460, h = 460 },
    props = { src = [[
<html>
<head>
<meta name="viewport" content="width=460">
<style>
  body { background: #0B1622; color: #E4F1F7; font-family: Barlow; padding: 16px; }
  h1 { color: #38BDF8; margin: 0 0 8px 0; }
  .card { background: #172033; border-radius: 8px; padding: 12px; }
</style>
</head>
<body>
  <h1>Hello, base</h1>
  <div class="card">This is a web page on a Stationeers console.</div>
</body>
</html>
]] },
})
ui:commit()
```

Paste that into a Lua chip in a ScriptedScreens console and it renders.

## Requirements

- [StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad) (BepInEx)
- [ScriptedScreens](https://steamcommunity.com/sharedfiles/filedetails/?id=3666779631) with
  StationeersLua (the Lua chips and the `ss.ui` API)
- ScriptedScreens Vector (draws the page) and ScriptedScreens Fonts (the typefaces)

Client-side. Every player who looks at the console needs the mods; the page source travels
with the chip's Lua like any other ScriptedScreens element.

## The `html` element

One ScriptedScreens element of type `html` holds the page. Its `rect` is where the page is
drawn on the surface; the page lays itself out at its own design width and is stretched to
fit that rect.

| Prop | Meaning |
|---|---|
| `src` | the page: a complete HTML document as a string (Lua's `[[ ... ]]` is convenient) |
| `page` | optional; names the page a *data* element belongs to (see Data), default is the element id |
| `data` | on a second element: a table of values for the page |

| Event | Meaning |
|---|---|
| `on_click(id, player)` | a `<button>`, an element with `onclick`, or one that a script listens to for `click`, was clicked; `id` is the element's id |
| `on_change(value, player)` | a control changed; `value` is `"name=value"`, the control's `name` (or id) and its new value: the text, `true`/`false` for a checkbox, the number for a range, the option value for a select |

The design width comes from the page: `<meta name="viewport" content="width=640">`. Lay
the page out for that width in px units, as you would for a phone, and it scales to any
console size. Without the meta tag the design width is the element's width in canvas units
(460 on every console).

## Data: the page changes, Lua decides

Keep the page element static and send values through a second, tiny element that names the
page. Its `data` table binds by element id:

```lua
local data = ui:element({
    id = "screen_data", type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "screen", data = {} },
})
ui:commit()

function tick(dt)
    data:set_props({ data = {
        temp   = string.format("%.1f K", read_temperature()),   -- text of <span id="temp">
        bar    = { width = pct .. "%" },                          -- CSS on <div id="bar">
        alarm  = pressure > 500,                                  -- display of <div id="alarm">
        hist   = history,                                         -- an SVG shape with id="hist"
    } })
    ui:commit()
end
```

| Value type | What it does to the element with that id |
|---|---|
| string or number | becomes its text |
| table of `property = value` | sets those CSS declarations on it (`{ width = "60%", background = "#B5352C" }`) |
| boolean | shows or hides it |
| number array | on an SVG shape: y values spread across the viewBox width (a live graph); on a text element, joined |
| string on an SVG shape | its `points` |
| table on an SVG shape | its attributes |

Because the elements persist, a CSS `transition` on the element animates the change: a bar
with `transition: width 0.4s` glides to its new width, nothing in Lua runs per frame.

The whole `data` table also reaches the vector layer as `$name` values (`$temp`, `$tank_fill`
for `tank = { fill = ... }`), which SVG expressions can use directly (see SVG).

Page scripts see the same values as a `data` event on `window` (`window.ondata = function
(d) { ... }`), if you would rather react in JavaScript.

## Controls and clicks

`<button>` and anything with `onclick` is a click region: the click arrives in Lua's
`on_click` with the element id, and in the page script as a normal `click` event that
bubbles. `<input>`, `<textarea>` and `<select>` are real controls (ScriptedScreens' own),
placed inside the element's box, so the page's border, radius and background frame them.
Their changes arrive in `on_change` as `"name=value"` and in the script as `input`/`change`
events. `<input type="checkbox">`, radio buttons, `<progress>`, `<meter>`, `<details>` and
`<dialog>` are drawn by the page and behave as in a browser.

`:hover`, `:active` and `:focus` work. The cursor over a console is the crosshair while the
cursor is locked.

## CSS

Write it as you would for a browser. Flexbox and grid, `position: sticky` inside a
scrolling box, `overflow: auto` (real scrolling with the wheel), gradients of every kind,
box and text shadows, filters, clip paths, masks, borders of every style, `border-image`,
transforms, transitions, `@keyframes`, scroll-driven animations, `@media` against the design
width, custom properties, `calc()` with the trigonometric functions, `@font-face`, counters,
logical properties, `@starting-style` for a fade-in on insert, `::backdrop` behind a modal
dialog, popovers, `column-rule`, `offset-path` and `ray()`, the colour spaces (`oklch()` and the
rest, relative colours), image maps, `<datalist>`, `<base>`, and so on.
`SUPPORT.md` lists everything that has been seen working on a console and the few things
still being finished.

Two things that are not a browser:

- **Per-pixel effects are not drawn.** `filter: blur()`, `backdrop-filter` and
  `mix-blend-mode` are accepted and do nothing; the page is geometry, not pixels.
- **A page has no network of its own.** No `fetch`; `<link rel=stylesheet>`, `@import`,
  `<script src>` and module imports from http(s) URLs are fetched once when the page loads.

## Fonts

`font-family` names a typeface the Fonts mod has: the files in its `Assets/fonts` folder by
family and style (`Barlow`, `Barlow SemiBold`, `Barlow Condensed`), then the game's own
TextMeshPro faces, then the generic families mapped to what is installed (`monospace` is the
game's `code` face). A glyph a font lacks draws as a box, as the font policy says: choose a
face that has it (`noto-punc` for arrows and symbols).

## SVG

Inline `<svg>` is resolved as a browser resolves it and written shape for shape: paths,
gradients, `use`/`symbol`, `clipPath`, `text`, `image`, `transform`, CSS on shapes. Two
extensions exist because the vector layer animates for free:

- Any attribute may be an expression: `cy="=60+8*sin(t*2)"`. `t` is seconds, `i` the
  instance index inside a repeat, `$name` a data value. Functions: `sin cos abs min max
  clamp lerp step smoothstep hash if lt gt ...` (the vector mod's REFERENCE lists them).
- `n="36"` on a `polygon`/`polyline` samples `x`, `y` (and `y2` for a filled band) at 36
  points of `i`, giving a continuous wave; `n` on any other shape repeats it with `i` bound.

```html
<svg viewBox="0 0 100 60" width="200" height="120">
  <polygon n="40" x="=i*100/39" y="=30+4*sin(i*0.5+t*3)*$level" y2="60" fill="#38BDF8" fill-opacity="0.8"/>
</svg>
```

That tank surface ripples on the client with nothing sent per frame; `$level` comes from
the data table.

## JavaScript

A `<script>` in the page runs on a worker thread, with the DOM you expect: `document`,
`querySelector`, `createElement`, `appendChild`, `innerHTML`, `classList`, `style`,
`addEventListener`, `dispatchEvent`, timers, `requestAnimationFrame`, `localStorage`, `URL`,
`fetch`-less networking (see above), `Element.animate()`, `elementFromPoint`, `DOMParser`,
`XMLSerializer`, `<canvas>` with a full 2D context
(drawn as vector paths), `import` from a URL in a module script. Reads after writes see the
writes. `console.log` goes to the BepInEx log.

Use it for what a browser page would use it for: building the DOM from data, reacting to
clicks, drawing on a canvas. Do not use it for animation loops that could be CSS or an SVG
expression: a `requestAnimationFrame` loop costs one page emit per frame (capped at 30 a
second), a CSS animation costs nothing.

## Performance, in one paragraph

A page is translated to vector geometry when it changes, never per frame. Data ticks
re-translate the page (a fraction of a millisecond for a typical page); transitions,
keyframes, SVG expressions and scroll animations are evaluated by the vector mod on a worker
thread. The costs to know about: a huge radial gradient or a blurred shadow is many
vertices; a canvas redrawn every frame is an emit every frame; a page script that touches
hundreds of elements per tick is a lot of layout. The diagnostics line tells you which.

## Diagnostics

`BepInEx/config/gruffuss.stationeers.scriptedscreens.html.cfg`:

- `Diagnostics.Enabled`: one line per page per second in the BepInEx log: emits per second,
  layout and translate milliseconds, node count, scene size, live tweens, script
  milliseconds per frame.
- `Diagnostics.DumpScenes`: the exact scene text the vector mod receives, in
  `scenes/<page id>.txt` beside the mod's DLL.

A magenta hatched frame around the page means the vector mod refused part of the scene; the
reason is in the log. CSS the page uses that is not supported is logged once per property.
Script errors are logged with their message and stack. `python mcp.py capture_scripted_screen`
(StationeersLua's MCP) captures the page as an image, script output included.

## Examples

`examples/` has runnable chips, each introducing one idea:

| File | Shows |
|---|---|
| `01-hello.lua` | a styled page: layout, fonts, a card, a list |
| `02-live-data.lua` | the data element: text, CSS and visibility bound by id, a transition on a bar |
| `03-controls.lua` | buttons, inputs and a select talking to Lua through `on_click` and `on_change` |
| `04-svg-gauge.lua` | an SVG tank with an expression-driven surface fed by `$level` |
| `05-script.lua` | a page script building a table from a `data` event and reacting to clicks |
| `06-console.lua` | a complete gas monitor: grid layout, live values, alarms, a history graph |

## Where things live

`SUPPORT.md` is the checklist of what works, `PLAN.md` the work log, `FOR-VECTOR-SESSION.md`
what the vector mod is being asked for. Test pages `HtmlTest*.lua` are the verification
pages, one per batch of features.

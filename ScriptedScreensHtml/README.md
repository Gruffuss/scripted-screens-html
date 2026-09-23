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
| `page` | optional; names the page a *data* element belongs to (see Showing real device data), default is the element id |
| `data` | on a second element: a table of values for the page |

| Event | Meaning |
|---|---|
| `on_click(id, player)` | a `<button>`, an element with `onclick`, or one that a script listens to for `click`, was clicked; `id` is the element's id |
| `on_change(value, player)` | a control changed; `value` is `"name=value"`, the control's `name` (or id) and its new value: the text, `true`/`false` for a checkbox, the number for a range, the option value for a select |

The design width comes from the page: `<meta name="viewport" content="width=640">`. Lay
the page out for that width in px units, as you would for a phone, and it scales to any
console size. Without the meta tag the design width is the element's width in canvas units
(460 on every console).

## Showing real device data

A page cannot read the game. The chip's Lua reads the devices and sends the values; the page
decides how they look. That takes two elements: the page (`src`), and a second, tiny `html`
element whose `page` names the first and whose `data` table carries the values.

```lua
local data = ui:element({
    id = "screen_data", type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },   -- off screen; its rect does not matter
    props = { page = "screen", data = {} },
})
ui:commit()

local LT = ic.enums.LogicType
function tick(dt)                                            -- runs about twice a second
    local id = ic.find("Tank")                               -- the device's Labeller name
    local kpa = id and ic.read_id(id, LT.Pressure)
    local pct = math.floor(math.min(100, (kpa or 0) / 60000 * 100))
    data:set_props({ data = {
        pressure = kpa and string.format("%.0f kPa", kpa) or "--",   -- text of id="pressure"
        bar = { width = string.format("%d%%", pct) },                -- CSS on id="bar"
        alarm = (kpa or 0) > 55000,                                  -- shows or hides id="alarm"
    } })
    ui:commit()
end
```

Each key of `data` is the `id` of an element in the page:

| Value | What it does to that element |
|---|---|
| string or number | becomes its text |
| table of CSS declarations | sets them: the `width` of a bar inside its track (`"62%"`), `background-color`, `color` |
| `true` / `false` | shows / hides it |

A CSS `transition` on the element runs on every change: `transition: width 0.6s ease` on the
bar slides it to each new value. Colours change at once. On a page with no `<script>` the CSS
and layout are worked out once, from the first values; after that only the values move, so a
readout costs nothing between ticks. A gauge needle turned by `transform` from data is coming;
today a bar is a `width`.

`examples/10-device-readout.lua` is one tank: pressure, temperature, a bar that changes colour,
and an alarm. `examples/11-device-list.lua` lists every device whose name starts with a prefix,
a row each, with the unused rows hidden.

What not to do:

- **Don't send every frame.** `tick(dt)` runs about twice a second, which is plenty: the
  transition fills in the motion between ticks.
- **Don't send raw numbers as text.** Format them in Lua, units and all:
  `string.format("%.1f kPa", kpa)`. `%d` needs a whole number, so `math.floor` first, or the
  chip stops with an error.
- **Don't animate from Lua.** Stepping a width a little every tick fights the transition. Send
  where the value should end up.
- **Don't leave keys out, or send an empty string.** The page is prepared from the first values
  it gets: send every key every time, and `"--"` for a reading you do not have. A `true`/`false`
  first sent later cannot hide anything, and text first sent empty has nowhere to go.

A key the page cannot use is named in the BepInEx log, once.

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

`font-family` names any face registered with the game's text engine: the ones the Fonts mod
loads (by family and style: `Barlow`, `Barlow SemiBold`, `Barlow Condensed`, and your own font
files, which go wherever the Fonts mod's documentation says) and the game's own faces
(`LiberationSans SDF`, `font_english`, `noto-punc`). `font-weight` picks the registered weight
face (`600` is `SemiBold`) where there is one. Generic families map to what is installed
(`sans-serif` is Barlow, `monospace` the game's `code` face). This mod reads no font files: it
lays the page out with the registered faces themselves, so a face the Fonts mod has not loaded
yet is picked up a few seconds after it arrives. A glyph a face lacks is drawn from the game's
own face.

## SVG

Inline `<svg>` is resolved as a browser resolves it and written shape for shape: paths,
gradients, `use`/`symbol`, `clipPath`, `text`, `image`, `transform`, CSS on shapes. A readout,
a bar or an alarm needs none of it: boxes, text and the data table above do those.

### If you already know SVG

Two extensions exist because the vector layer animates for free:

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

That tank surface ripples on the client with nothing sent per frame. `$level` comes from the
data table: every data value is also a `$name` for expressions (`$tank_fill` for
`tank = { fill = ... }`). A number array sent to an SVG shape's id is its y values, spread
evenly across the viewBox width: a live graph.

## JavaScript

A `<script>` in the page runs on a worker thread, with the DOM you expect: `document`,
`querySelector`, `createElement`, `appendChild`, `innerHTML`, `classList`, `style`,
`addEventListener`, `dispatchEvent`, timers, `requestAnimationFrame`, `localStorage`, `URL`,
`fetch`-less networking (see above), `Element.animate()`, `elementFromPoint`, `DOMParser`,
`XMLSerializer`, `<canvas>` with a full 2D context
(drawn as vector paths), `import` from a URL in a module script. Reads after writes see the
writes. Sizes are the exception: `clientHeight`, `getBoundingClientRect` and the rest report the
last drawn layout, so a script that changes something and wants the new size reads it on the next
frame, where a browser would lay the page out on the spot. `window.innerWidth` and
`window.innerHeight` are the page's own design size and are right from the first line.
`console.log` goes to the BepInEx log. Data from Lua arrives as a `data` event on `window`
(`window.ondata = function (d) { ... }`), if you would rather react in JavaScript.

Use it for what a browser page would use it for: building the DOM from data, reacting to
clicks, drawing on a canvas. Do not use it for animation loops that could be CSS or an SVG
expression: a `requestAnimationFrame` loop costs one page emit per game frame (off the game thread, but
not free), a CSS animation costs nothing.

## Performance, in one paragraph

A page is translated to vector geometry when it changes, never per frame. On a page with no
script a data tick writes its values straight into the drawn scene, with no layout or
translation; transitions, keyframes, SVG expressions and scroll animations are evaluated by
the vector mod on a worker thread. The costs to know about: a huge radial gradient or a blurred shadow is many
vertices; a canvas redrawn every frame is an emit every frame; a page script that touches
hundreds of elements per tick is a lot of layout. The diagnostics line tells you which.

## Diagnostics

`BepInEx/config/gruffuss.stationeers.scriptedscreens.html.cfg`:

- `Diagnostics.Enabled`: one line per page per second in the BepInEx log: emits per second,
  layout and translate milliseconds, node count, scene size, live tweens, script
  milliseconds per frame. A page emits once its layout has settled (a frame in which the
  grid, line-box and baseline passes wrote nothing), so one innerHTML is one emit; a page
  that re-renders every tick emits at its tick rate, not per frame.
- `Diagnostics.DumpScenes`: the exact scene text the vector mod receives, in
  `scenes/<page id>.txt` beside the mod's DLL.

A magenta hatched frame around the page means the vector mod refused part of the scene; the
reason is in the log. CSS the page uses that is not supported is logged once per property.
Script errors are logged with their message and stack. StationeersLua's MCP tool
`capture_scripted_screen` captures the page as an image, script output included.

## Examples

`examples/` has runnable chips, each introducing one idea:

| File | Shows |
|---|---|
| `01-hello.lua` | a styled page: layout, fonts, a card, a list, a table |
| `02-live-data.lua` | the data element: text, CSS and visibility bound by id, a transition on a bar |
| `03-controls.lua` | buttons, inputs and a select talking to Lua through `on_click` and `on_change` |
| `04-svg-gauge.lua` | an SVG tank with an expression-driven surface fed by `$level` |
| `05-script.lua` | a page script building a table from a `data` event and reacting to clicks |
| `06-console.lua` | a complete gas monitor: grid layout, live values, alarms, a history graph |
| `07-game.lua` | Stationeer Run, an endless runner: a `requestAnimationFrame` loop, clicks, a demo that plays itself |
| `10-device-readout.lua` | a real tank found by name: pressure, temperature, a bar that changes colour, an alarm |
| `11-device-list.lua` | every device whose name starts with a prefix, one row each, unused rows hidden |

`mockups/` beside them holds three complete consoles written as browser pages, each opening
on the screen named by `TAB` at its top (`atmo`, `supply`, `filter`, `alarm`, `config`):

| File | Shows |
|---|---|
| `AtmoApple.lua` | an Apple-style regulator console, five screens, a page script with a simulation |
| `AtmoDark.lua` | the same console in the Coldbench dark design system: token sheets, layered backgrounds, corner marks |
| `AtmoLight.lua` | the same console in the Hardsuit light theme: heavy outlines, a theme attribute switch |

With StationeersLua installed, these docs and the examples are MCP resources: search with
scope `html`, and start at `stationeers://html/index` (QUICKSTART.md with the full URI list).
The mockups are `stationeers://html-mockups/<file>/part1`, `part2` and on, in a search scope of their own
(`html-mockups`), since their CSS would otherwise answer every CSS question first.

## Where things live

`QUICKSTART.md` is the one page to read first, `CHANGELOG.md` the release history,
`SUPPORT.md` the checklist of what works. The repository (https://github.com/Gruffuss/scripted-screens-html) also holds
the work log (`PLAN.md`), the coverage table (`COVERAGE.md`), and the verification pages
(`HtmlTest*.lua`), one per batch of features; none of those ship with the mod.

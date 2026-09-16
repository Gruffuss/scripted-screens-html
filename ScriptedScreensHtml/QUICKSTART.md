# ScriptedScreens Html: quick start

Written for an editor or an AI writing a console page. Everything here runs as written.

## What it is

An `html` element type for ScriptedScreens surfaces. Give it an HTML document with CSS and,
optionally, JavaScript; the page is laid out like a browser page and drawn by ScriptedScreens
Vector as geometry, crisp at any distance, in the Fonts mod's typefaces. Use it when a console
is a layout of text, boxes, bars, tables, forms or charts. Use the vector element directly for
particle fields or thousands of animated shapes; use `canvas`/`image` for pictures.

A page that does not change costs nothing per frame. Motion comes from CSS transitions,
keyframes and SVG expressions evaluated on the client; Lua sends values a few times a second.

## Workflow

1. Write the page as for a browser. One design width: `<meta name="viewport" content="width=640">`
   (a 3x3 console); the page scales to the console.
2. Paste it into a Lua chip in a ScriptedScreens console, or from an MCP client write it with
   StationeersLua's `set_chip_code` (the chip ids come from `list_chips`).
3. Look: `capture_scripted_screen` with the chip's ref returns a PNG of the console.
4. Errors: `get_chip_errors` for Lua; `BepInEx/LogOutput.log` lines starting `css:` or `js:` for
   the page. Turn on `Diagnostics.Enabled` and `Diagnostics.DumpScenes` in
   `BepInEx/config/gruffuss.stationeers.scriptedscreens.html.cfg` (read live) for a per-page
   cost line every second and the exact scene text in `scenes/<page id>.txt` beside the DLL.
5. Fix the page, push again. No restart is needed for a page change.

## A page and its data, as written

```lua
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()

local page = [[
<html><head><meta name="viewport" content="width=640"><style>
  body { margin: 0; padding: 24px; background: #0B1622; color: #E4F1F7; font-family: Barlow; font-size: 22px; }
  h1 { margin: 0 0 12px; color: #38BDF8; font-family: 'Barlow Condensed'; font-weight: 600; }
  .card { background: #13233A; border-radius: 12px; padding: 16px; margin-bottom: 12px; }
  .row { display: flex; justify-content: space-between; align-items: baseline; gap: 12px; }
  .bar { height: 10px; background: #1E3350; border-radius: 5px; overflow: hidden; margin-top: 8px; }
  .bar div { height: 100%; width: 50%; background: #34D399; border-radius: 5px; transition: width .5s ease; }
  .alarm { color: #F87171; font-weight: 600; }
</style></head><body>
  <h1>Hab core</h1>
  <div class="card">
    <div class="row"><span>O2</span><span id="o2">21.0 %</span></div>
    <div class="bar"><div id="o2bar"></div></div>
  </div>
  <div class="card row"><span>Pressure</span><span id="press">101.3 kPa</span><span id="alarm" class="alarm">TRIP</span></div>
</body></html>
]]

ui:element({ id = "page", type = "html", rect = { unit = "px", x = 0, y = 0, w = W, h = H }, props = { src = page }, style = { bg = "#FF00FF" } })
-- an off-screen 1x1 element carries the values; it names the page it feeds
local data = ui:element({ id = "page_data", type = "html", rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 }, props = { page = "page", data = {} } })
ui:commit()

local t = 0
function tick(dt)
    t = t + (dt or 0.5)
    local o2 = 21 + 3 * math.sin(t * 0.4)
    local press = 101.3 + 2 * math.sin(t * 0.2)
    data:set_props({ data = {
        o2 = string.format("%.1f %%", o2),          -- a string sets the element's text
        o2bar = { width = string.format("%d%%", math.floor(o2 * 4)) },  -- a table sets CSS; the transition animates it
        press = string.format("%.1f kPa", press),
        alarm = press > 102.5,                       -- a boolean shows or hides
    } })
end
```

Values bind by element id. A number array binds to an SVG shape's points (a live graph).
Clicks: `<button id="purge">` or any element with `onclick` reports to `function on_click(id)`;
inputs, selects, checkboxes and ranges report to `function on_change(v)` as `"name=value"`.

## Which construct for which need

| Need | Write |
|---|---|
| a value that changes | an element with an id; send a string through `data` |
| a bar, gauge fill, progress | a box with `width`/`height` from `data` and a `transition` |
| show/hide on a condition | a boolean through `data` (`display`) |
| a blinking lamp, a marching duct | `@keyframes` + `animation` on the element |
| a live graph | inline `<svg>` with a `<polyline id="hist">` and a number array through `data` |
| a rippling tank surface | `<polygon n="36" ...>` with expressions over `t`, `i`, `$level` (see the guide, SVG) |
| a table from a list | a page `<script>` listening to the `data` event and writing `innerHTML` |
| a button | `<button id="x">`: Lua gets `on_click(id)`, and a page script's `click` listener fires too |
| a picture, video, sound | `<img src>`, `<video>`, `<audio>` with a URL the game can fetch |
| a screen the player scrolls | `overflow-y: auto` on a box; the wheel scrolls it |
| a theme switch | `[data-mode="dark"]` rules with custom properties; the script sets the attribute |

Fonts: `font-family: Barlow`, `'Barlow Condensed'`, `Manrope` and every weight the Fonts mod
ships (`font-weight: 600` picks the SemiBold face); `monospace` is the game's `code` face; a
glyph a face lacks (subscript digits, the gear) comes from the game's own face automatically.

## Rules that fail silently

- [ ] The page's `id` is what the data element names in `page = "..."`; a mismatch binds nothing.
- [ ] Every element that receives data has an `id`; an id that matches nothing warns once in the log.
- [ ] `string.format("%d", x)` with a float is a Lua error on the chip; `math.floor` first.
- [ ] One design width in the viewport meta; without it the page is laid out at the console's 460.
- [ ] A page script has no `fetch`, no custom elements, no frameworks; the DOM subset is in the guide.
- [ ] Only elements that are clickable report clicks: a `<button>`, an element with `onclick`, or one a script gave a `click` listener.
- [ ] `innerHTML` rebuilds every element under the target; keep handlers inside the rebuilt markup
      (`data-act` plus a delegating loop), and prefer value writes for per-tick updates.
- [ ] Per-pixel effects (`blur`, `backdrop-filter`, blend modes) are accepted and do nothing.
- [ ] A `.5px` hairline draws one pixel wide, as in a browser at 1x.
- [ ] `:hover` needs a pointer over the console; captures never show hover states.
- [ ] Colours in `transition` snap; sizes, positions, opacity and transforms glide.

## Symptom to cause

| Symptom | Cause |
|---|---|
| magenta frame, nothing drawn | the vector mod refused the scene; the reason is in the log |
| a value never updates | the data key is not an element id, or the data element's `page` is wrong |
| text is there but in the wrong face | the family name is not one the Fonts mod ships; check `html: font library scanned` in the log |
| a word next to a lamp is missing | (fixed in 0.2.0) a text node inside a flex box is an item of its own |
| a label vanishes when the value gets longer | the box clips it; give the box room or `text-overflow: ellipsis` |
| a number overlaps its unit | (fixed in 0.2.0) tabular digits now widen the box |
| a page re-emits every frame | Diagnostics line `dirty:` names the cause: a keyframe on many elements, a script writing per frame, a transition restarting each tick |
| clicks arrive seconds late | (fixed in 0.2.0) a worker read no longer waits per element |

## Where to read more

| Topic | URI |
|---|---|
| the html element, rect, design width | `stationeers://html/guide/the-html-element` |
| the data element and binding by id | `stationeers://html/guide/data-the-page-changes-lua-decides` |
| buttons, inputs, on_click, on_change | `stationeers://html/guide/controls-and-clicks` |
| CSS that works, by area | `stationeers://html/support/css` and its subsections |
| fonts | `stationeers://html/guide/fonts` |
| SVG, expressions, live graphs | `stationeers://html/guide/svg` |
| JavaScript and the DOM subset | `stationeers://html/guide/javascript`, `stationeers://html/support/javascript` |
| what is accepted without effect | `stationeers://html/support/css-accepted-without-effect` |
| diagnostics | `stationeers://html/guide/diagnostics` |
| examples | `stationeers://html/examples/index` |
| whole consoles written as pages | `stationeers://html-mockups/AtmoApple.lua/part1` and the parts after it (scope `html-mockups`) |

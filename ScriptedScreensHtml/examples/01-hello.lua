-- 01-hello.lua -- a styled page. Paste into a Lua chip in a ScriptedScreens console.
-- Shows: a design width, a font, flex layout, a card, a list, a table. Nothing moves, so
-- after the first draw this page costs nothing.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "hello",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = [[
<html>
<head>
<meta name="viewport" content="width=480">
<style>
  :root { --ink: #E4F1F7; --dim: #7A93A6; --accent: #38BDF8; --panel: #172033; }
  body { background: #0B1622; color: var(--ink); font-family: Barlow; padding: 16px; font-size: 14px; }
  header { display: flex; justify-content: space-between; align-items: baseline; border-bottom: 1px solid #24314A; padding-bottom: 8px; }
  h1 { margin: 0; font-size: 22px; color: var(--accent); }
  header small { color: var(--dim); }
  .cards { display: flex; gap: 12px; margin-top: 14px; }
  .card { flex: 1; background: var(--panel); border-radius: 10px; padding: 12px; box-shadow: 0 4px 12px #00000080; }
  .card h2 { margin: 0 0 6px 0; font-size: 12px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; }
  .big { font-size: 24px; font-weight: 600; }
  .ok { color: #2E8B6E; } .warn { color: #E2A94E; }
  ul { margin: 14px 0 0 0; padding-left: 20px; }
  li { margin: 3px 0; }
  table { width: 100%; margin-top: 14px; border-collapse: collapse; font-size: 13px; }
  th { text-align: left; color: var(--dim); font-weight: 500; border-bottom: 1px solid #24314A; padding: 4px 6px; }
  td { padding: 4px 6px; }
  tr:nth-child(even) td { background: #101C2A; }
</style>
</head>
<body>
  <header><h1>Airlock 3</h1><small>hello, base</small></header>

  <div class="cards">
    <div class="card"><h2>Pressure</h2><div class="big ok">101.3 kPa</div></div>
    <div class="card"><h2>Temperature</h2><div class="big">293 K</div></div>
    <div class="card"><h2>Oxygen</h2><div class="big warn">19.4 %</div></div>
  </div>

  <ul>
    <li>Write the page as for a browser: HTML, CSS, and JavaScript if you want it.</li>
    <li>Lay it out for the design width in the viewport tag; it scales to any console.</li>
    <li>Fonts are the ones the Fonts mod ships: <b>Barlow</b> here, <span style="font-family: 'Barlow Condensed'">Barlow Condensed</span>, <code>code</code>.</li>
  </ul>

  <table>
    <tr><th>Tank</th><th>Gas</th><th>State</th></tr>
    <tr><td>T1</td><td>O2</td><td class="ok">ok</td></tr>
    <tr><td>T2</td><td>CO2</td><td class="warn">filling</td></tr>
    <tr><td>T3</td><td>N2</td><td class="ok">ok</td></tr>
  </table>
</body>
</html>
]] },
})

ui:commit()

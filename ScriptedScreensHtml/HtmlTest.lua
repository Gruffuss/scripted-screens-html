-- ScriptedScreens Html: a page written as HTML + CSS, fed live data.
--
-- Two elements, same as the vector mod's structure/data split:
--   * the PAGE element carries `src` and is sent once
--   * the DATA element carries `data`, keyed by HTML id, and is resent per tick
-- A string sets a label's text; a table sets CSS properties on the element. Because
-- the element persists, the `transition` on .fill makes each bar glide to its new
-- height instead of stepping.
--
-- Expect: a dark dashboard, three tank bars drifting up and down smoothly, values
-- updating twice a second, a spinner turning, the status pill pulsing, gradient
-- backgrounds on the cards and the N2 bar, and a scrolling CO2 history graph drawn
-- as inline SVG (a polyline fed a number array, plus a filled area polygon). All motion is client-side: @keyframes,
-- transition, no per-frame Lua. Warnings go to BepInEx/LogOutput.log as "html:" / "css:".

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local page = [[
<html>
<head>
<style>
  body { background: #0F172A; padding: 12px; }
  .title { flex-direction: row; justify-content: space-between; align-items: center; margin-bottom: 10px; }
  h1 { font-size: 22px; margin: 0; color: #E2E8F0; }
  .pill { background: #2E8B6E; color: white; padding: 2px 10px; border-radius: 10px; font-size: 13px;
          transition: background-color 0.4s ease; animation: pulse 1.2s ease-in-out infinite alternate; }
  @keyframes pulse { from { opacity: 1; } to { opacity: 0.45; } }
  @keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
  @keyframes bob { 0% { transform: translate(0px, 0px); } 50% { transform: translate(0px, -6px); } 100% { transform: translate(0px, 0px); } }
  .spinner { width: 18px; height: 18px; border-radius: 9px; border: 3px solid #334155; border-top-color: #38BDF8;
             animation: spin 1s linear infinite; margin-right: 8px; }
  .titleleft { flex-direction: row; align-items: center; }
  .card { background: linear-gradient(to bottom, #1E293B, #0F172A); }
  .n2 { background: linear-gradient(to top, #7C3AED, #C4B5FD); }
  #tanks { animation: bob 2s ease-in-out infinite; }
  .row { flex-direction: row; flex-grow: 1; }
  .card { flex: 1; margin: 4px; padding: 8px; background: #1E293B; border: 1px solid #334155; border-radius: 8px; }
  .card h2 { font-size: 15px; margin: 0 0 6px 0; color: #94A3B8; }
  .bar { flex-grow: 1; background: #0B1622; border-radius: 4px; overflow: hidden; justify-content: flex-end; }
  .fill { border-radius: 4px; height: 0%; transition: height 0.45s ease-out; }
  .o2  { background: #38BDF8; }
  .co2 { background: #F59E0B; }
  .n2  { background: #A78BFA; }
  .val { text-align: center; font-size: 18px; margin-top: 6px; }
  .foot { color: #64748B; font-size: 12px; margin-top: 8px; }
  .graph { height: 92px; margin: 4px; padding: 6px; background: #1E293B; border: 1px solid #334155; border-radius: 8px; }
  .graph h2 { font-size: 13px; margin: 0 0 2px 0; color: #94A3B8; }
  .graph svg { flex-grow: 1; }
</style>
</head>
<body>
  <div class="title">
    <div class="titleleft"><div class="spinner"></div><h1>GAS STORAGE</h1></div>
    <span id="status" class="pill">ONLINE</span>
  </div>
  <div class="row">
    <div class="card">
      <h2>O<sub>2</sub></h2>
      <div class="bar"><div id="o2_fill" class="fill o2"></div></div>
      <div id="o2_val" class="val">--</div>
    </div>
    <div class="card">
      <h2>CO<sub>2</sub></h2>
      <div class="bar"><div id="co2_fill" class="fill co2"></div></div>
      <div id="co2_val" class="val">--</div>
    </div>
    <div class="card">
      <h2>N<sub>2</sub></h2>
      <div class="bar"><div id="n2_fill" class="fill n2"></div></div>
      <div id="n2_val" class="val">--</div>
    </div>
  </div>
  <div class="graph">
    <h2>CO<sub>2</sub> history</h2>
    <svg viewBox="0 0 100 60" preserveAspectRatio="none">
      <line x1="0" y1="30" x2="100" y2="30" stroke="#334155" stroke-width="0.5" />
      <polygon id="hist_area" points="" fill="#F59E0B" fill-opacity="0.18" />
      <polyline id="hist" points="" fill="none" stroke="#F59E0B" stroke-width="1.5" stroke-linejoin="round" />
    </svg>
  </div>
  <div class="foot">Sampled 0.5 s &middot; <b id="tanks">3</b> tanks &middot; <span id="ticks">0</span> ticks</div>
</body>
</html>
]]

ui:element({
    id = "page",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#FF00FF" },
})

-- Off-screen 1x1 element: it draws nothing, it only carries the data payload.
local data = ui:element({
    id = "page_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "page", data = {} },
})

ui:commit()

local elapsed, ticks = 0, 0
local N = 40
local hist = {}
for i = 1, N do hist[i] = 30 end

function tick(dt)
    elapsed = elapsed + (dt or 0.5)
    ticks = ticks + 1
    local o2  = math.floor(50 + 40 * math.sin(elapsed * 0.35))
    local co2 = math.floor(30 + 25 * math.sin(elapsed * 0.6 + 1))
    local n2  = math.floor(70 + 25 * math.sin(elapsed * 0.2 + 2))
    local alarm = co2 > 45

    -- Rolling window: shift and append. The viewBox is 100x60 with y down, so the
    -- polyline gets y = 60 - value * 0.6; an array of numbers is spread across x.
    table.remove(hist, 1)
    hist[N] = co2
    local ys = {}
    for i = 1, N do
        ys[i] = 60 - hist[i] * 0.6
    end
    -- Both shapes take the array: the polyline draws it, the polygon fills under it down
    -- to the bottom of the viewBox. The renderer scrolls the window between ticks.

    data:set_props({ data = {
        hist      = ys,
        hist_area = ys,
        o2_fill  = { height = string.format("%d%%", o2) },
        co2_fill = { height = string.format("%d%%", co2) },
        n2_fill  = { height = string.format("%d%%", n2) },
        o2_val   = string.format("%d%%", o2),
        co2_val  = string.format("%d%%", co2),
        n2_val   = string.format("%d%%", n2),
        status   = alarm and "CO2 HIGH" or "ONLINE",
        ticks    = tostring(ticks),
    } })
    ui:commit()
end

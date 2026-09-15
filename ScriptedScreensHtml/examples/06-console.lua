-- 06-console.lua -- a complete gas monitor. Paste into a Lua chip; works on any console size.
-- Shows: a grid of four tanks with expression-driven surfaces, live readouts, alarm states
-- switched by CSS class, a scrolling history graph bound to a number array, and a status
-- footer. Everything visual is HTML/CSS/SVG; Lua only samples and sends numbers.
-- Readings are simulated below; the `sample()` function is where device reads go.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local GASES = { { id = "o2", name = "O2", colour = "#38BDF8" }, { id = "co2", name = "CO2", colour = "#E2A94E" },
                { id = "n2", name = "N2", colour = "#2E8B6E" }, { id = "h2", name = "H2", colour = "#B5352C" } }

-- One tank card per gas, built in Lua so the markup stays short.
local cards = {}
for i, g in ipairs(GASES) do
    cards[i] = string.format([[
    <div class="tank" id="card_%s">
      <div class="head"><span>%s</span><span class="pct" id="pct_%s">--</span></div>
      <svg viewBox="0 0 60 100" preserveAspectRatio="none">
        <polygon n="30" x="=i*60/29" y="=100-$%s_level+2*sin(i*0.7+t*2.2+%d)+1.2*sin(i*1.4-t*1.6)" y2="100" fill="%s" fill-opacity="0.85"/>
        <rect x="1" y="1" width="58" height="98" rx="4" fill="none" stroke="#24314A" stroke-width="2"/>
      </svg>
      <div class="kpa" id="kpa_%s">-- kPa</div>
    </div>]], g.id, g.name, g.id, g.id, i, g.colour, g.id)
end

local page = [[
<html>
<head>
<meta name="viewport" content="width=640">
<style>
  :root { --ink: #E4F1F7; --dim: #7A93A6; --panel: #172033; --line: #24314A; }
  body { background: #0B1622; color: var(--ink); font-family: Barlow; padding: 14px; font-size: 13px; display: flex; flex-direction: column; height: 100vh; box-sizing: border-box; }
  header { display: flex; justify-content: space-between; align-items: baseline; padding-bottom: 8px; border-bottom: 1px solid var(--line); }
  h1 { margin: 0; font-size: 20px; color: #38BDF8; }
  header .clock { color: var(--dim); font-variant-numeric: tabular-nums; }
  .tanks { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; margin-top: 12px; }
  .tank { background: var(--panel); border-radius: 10px; padding: 8px; display: flex; flex-direction: column; gap: 6px; border: 2px solid transparent; transition: border-color 0.4s; }
  .tank.alarm { border-color: #B5352C; }
  .tank .head { display: flex; justify-content: space-between; font-weight: 600; }
  .tank .pct { color: var(--dim); font-weight: 400; }
  .tank svg { width: 100%; height: 110px; }
  .tank .kpa { text-align: center; font-size: 16px; font-weight: 600; font-variant-numeric: tabular-nums; }
  .graph { margin-top: 12px; background: var(--panel); border-radius: 10px; padding: 8px; flex: 1; display: flex; flex-direction: column; }
  .graph .title { color: var(--dim); font-size: 11px; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 4px; }
  .graph svg { width: 100%; flex: 1; }
  footer { display: flex; gap: 16px; margin-top: 10px; color: var(--dim); font-size: 12px; }
  footer .led { display: inline-block; width: 8px; height: 8px; border-radius: 4px; background: #2E8B6E; margin-right: 6px; }
  #warn { display: none; color: #E2A94E; font-weight: 600; }
</style>
</head>
<body>
  <header><h1>Gas monitor</h1><span class="clock" id="clock">--:--</span></header>
  <div class="tanks">
]] .. table.concat(cards, "\n") .. [[
  </div>
  <div class="graph">
    <div class="title">O2 pressure, last 60 samples</div>
    <svg viewBox="0 0 100 40" preserveAspectRatio="none">
      <polygon id="hist_fill" points="" fill="#38BDF8" fill-opacity="0.25"/>
      <polyline id="hist" points="" fill="none" stroke="#38BDF8" stroke-width="0.6"/>
    </svg>
  </div>
  <footer><span><span class="led"></span>network ok</span><span id="warn">CO2 high</span></footer>
</body>
</html>
]]

ui:element({
    id = "gas",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})

local data = ui:element({
    id = "gas_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "gas", data = {} },
})

ui:commit()

-- Simulated readings per gas: pressure in kPa. Replace with device reads, e.g.
--   local tank = ic.find("O2 Tank"); return tank.Pressure
local t = 0
local function sample(i)
    return 200 + 150 * math.sin(t / (6 + i)) + 30 * math.sin(t / 2 + i)
end

local history = {}
local acc = 0
function tick(dt)
    acc = acc + dt
    if acc < 1 then return end
    acc = 0
    t = t + 1
    local payload = {}
    local co2high = false
    for i, g in ipairs(GASES) do
        local kpa = sample(i)
        local level = math.max(0, math.min(100, kpa / 4))
        payload[g.id .. "_level"] = level                       -- reaches the svg as $o2_level
        payload["pct_" .. g.id] = string.format("%d %%", math.floor(level))
        payload["kpa_" .. g.id] = string.format("%.0f kPa", kpa)
        payload["card_" .. g.id] = { ["border-color"] = kpa > 330 and "#B5352C" or "transparent" }
        if g.id == "co2" and kpa > 300 then co2high = true end
        if g.id == "o2" then
            history[#history + 1] = 40 - level * 0.4                 -- y in the graph's viewBox, top is 0
            if #history > 60 then table.remove(history, 1) end
        end
    end
    -- The graph: a number array spreads across the viewBox width as y values; the fill
    -- polygon gets the same points closed down to the bottom edge.
    local pts, fill = {}, { "0,40" }
    for i, y in ipairs(history) do
        local x = (i - 1) * 100 / math.max(1, #history - 1)
        pts[#pts + 1] = string.format("%.1f,%.1f", x, y)
        fill[#fill + 1] = pts[#pts]
    end
    fill[#fill + 1] = "100,40"
    payload.hist = table.concat(pts, " ")
    payload.hist_fill = table.concat(fill, " ")
    payload.warn = co2high
    local secs = math.floor(t)
    payload.clock = string.format("%02d:%02d", math.floor(secs / 60) % 60, secs % 60)
    data:set_props({ data = payload })
    ui:commit()
end

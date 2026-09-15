-- 02-live-data.lua -- the page stays put, Lua sends values. Paste into a Lua chip.
-- Shows: a second `html` element carrying `data` for the page; a string becomes an
-- element's text, a table becomes its CSS, a boolean shows or hides it. The bar has a CSS
-- transition, so each tick it glides instead of jumping, with nothing running per frame.
-- The values here are simulated; replace `sample()` with your device reads.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "monitor",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = [[
<html>
<head>
<meta name="viewport" content="width=480">
<style>
  body { background: #0B1622; color: #E4F1F7; font-family: Barlow; padding: 16px; font-size: 14px; }
  h1 { font-size: 20px; color: #38BDF8; margin: 0 0 12px 0; }
  .row { display: flex; justify-content: space-between; margin: 8px 0 4px 0; }
  .row span:last-child { font-weight: 600; }
  .track { height: 14px; background: #172033; border-radius: 7px; overflow: hidden; }
  .fill { height: 100%; width: 0%; background: #2E8B6E; border-radius: 7px; transition: width 0.6s ease, background-color 0.6s ease; }
  #alarm { margin-top: 16px; padding: 10px; background: #B5352C; border-radius: 8px; font-weight: 600; text-align: center; }
  .note { margin-top: 16px; color: #7A93A6; font-size: 12px; }
</style>
</head>
<body>
  <h1>Tank pressure</h1>
  <div class="row"><span>Pressure</span><span id="pressure">--</span></div>
  <div class="track"><div class="fill" id="bar"></div></div>
  <div class="row"><span>Temperature</span><span id="temp">--</span></div>
  <div id="alarm">OVER PRESSURE</div>
  <div class="note">Values arrive from Lua every second; the bar animates through a CSS transition.</div>
</body>
</html>
]] },
})

-- The data element: it names the page, its rect does not matter.
local data = ui:element({
    id = "monitor_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "monitor", data = {} },
})

ui:commit()

-- Simulated readings. With real devices: local tank = ic.find("Tank Name"); tank.Pressure
local t = 0
local function sample()
    t = t + 1
    local pressure = 300 + 250 * math.sin(t / 5)
    local temp = 290 + 5 * math.sin(t / 9)
    return pressure, temp
end

local acc = 0
function tick(dt)
    acc = acc + dt
    if acc < 1 then return end
    acc = 0
    local pressure, temp = sample()
    local pct = math.floor(math.max(0, math.min(100, pressure / 6)))
    data:set_props({ data = {
        pressure = string.format("%.0f kPa", pressure),
        temp = string.format("%.1f K", temp),
        bar = { width = pct .. "%", ["background-color"] = pressure > 480 and "#B5352C" or "#2E8B6E" },
        alarm = pressure > 480,
    } })
    ui:commit()
end

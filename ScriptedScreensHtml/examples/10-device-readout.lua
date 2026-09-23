-- 10-device-readout.lua -- one real tank on a console. Paste into a Lua chip.
-- Shows: a page fed by a device on the data network. Lua finds the tank by its Labeller
-- name, reads its pressure and temperature twice a second, and sends finished text, a bar
-- width and colour, and an alarm flag. The page does the rest: a CSS transition slides the
-- bar to each new width, and nothing runs between ticks.
--
-- Setup: label a tank (or a gas sensor, a pipe analyser: anything that reports Pressure and
-- Temperature) with the Labeller, put the same name in TANK below, and have the device on
-- the same data network as the chip. With no such device the page says so.

local TANK = "Tank"          -- the device's Labeller name, exactly: capitals count
local FULL_KPA = 60000       -- the pressure the bar shows as full
local WARN_KPA = 45000       -- from here the bar turns amber
local ALARM_KPA = 55000      -- from here the bar turns red and OVER PRESSURE shows

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "tank",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = [[
<html>
<head>
<meta name="viewport" content="width=480">
<style>
  body { background: #0B1622; color: #E4F1F7; font-family: Barlow; padding: 16px; font-size: 16px; }
  h1 { font-size: 24px; color: #38BDF8; margin: 0; }
  #status { color: #7A93A6; font-size: 13px; margin: 2px 0 16px 0; }
  .row { display: flex; justify-content: space-between; margin: 12px 0 6px 0; }
  .value { width: 150px; text-align: right; font-weight: 600; }
  .track { height: 16px; background: #172033; border-radius: 8px; }
  .fill { height: 100%; width: 0%; background: #2E8B6E; border-radius: 8px; transition: width 0.6s ease; }
  #alarm { margin-top: 20px; padding: 10px; background: #B5352C; border-radius: 8px; font-weight: 600; text-align: center; }
</style>
</head>
<body>
  <h1 id="device">Tank</h1>
  <div id="status">Looking for the device...</div>
  <div class="row"><span>Pressure</span><span class="value" id="pressure">--</span></div>
  <div class="track"><div class="fill" id="bar"></div></div>
  <div class="row"><span>Temperature</span><span class="value" id="temp">--</span></div>
  <div id="alarm">OVER PRESSURE</div>
</body>
</html>
]] },
})

-- The data element: it names the page, its rect does not matter.
local data = ui:element({
    id = "tank_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "tank", data = {} },
})

ui:commit()

local LT = ic.enums.LogicType
local id                     -- the tank's reference id, found by name and kept

-- One reading, or nil. A device that stops answering (rebuilt, relabelled, unplugged) is
-- looked up by name again on the next tick, so the console recovers by itself.
local function read(logic)
    if not id then id = ic.find(TANK) end
    if not id then return nil end
    local v = ic.read_id(id, logic)
    if v == nil then id = nil end
    return v
end

-- tick runs about twice a second: often enough for a readout, and the transition fills in
-- the motion between ticks. Every key goes in every payload, and "--" rather than an empty
-- string when there is no reading.
function tick(dt)
    local kpa = read(LT.Pressure)
    local kelvin = read(LT.Temperature)

    local colour = "#2E8B6E"
    if kpa and kpa >= ALARM_KPA then colour = "#B5352C"
    elseif kpa and kpa >= WARN_KPA then colour = "#E2A94E" end
    local pct = math.floor(math.max(0, math.min(100, (kpa or 0) / FULL_KPA * 100)))

    data:set_props({ data = {
        device = TANK,                                                        -- text of id="device"
        status = kpa and "Live, twice a second" or ('No device named "' .. TANK .. '" on this network'),
        pressure = kpa and string.format("%.0f kPa", kpa) or "--",
        temp = kelvin and string.format("%.1f °C", kelvin - 273.15) or "--",
        bar = { width = string.format("%d%%", pct), ["background-color"] = colour },  -- CSS on id="bar"
        alarm = kpa ~= nil and kpa >= ALARM_KPA,                              -- shows or hides id="alarm"
    } })
    ui:commit()
end

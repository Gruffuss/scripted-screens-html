-- 11-device-list.lua -- every tank on the network in one list. Paste into a Lua chip.
-- Shows: a list with a fixed number of rows, filled from whatever devices are on the data
-- network. Each row is a name, a value and a small bar; rows with no device are hidden by
-- sending `false` for them. The rows are written once, in Lua, so the markup stays short.
--
-- Setup: give each tank a Labeller name starting with PREFIX ("Tank O2", "Tank Fuel", ...).
-- The list is sorted by name and looks for new devices every ten seconds, so a tank you
-- label later appears on its own.

local PREFIX = "Tank"        -- list every device whose Labeller name starts with this
local ROWS = 8               -- rows on the page; devices beyond this are counted, not shown
                             -- (a wide, short console fits four: lower it there)
local FULL_KPA = 60000       -- the pressure a bar shows as full
local ALARM_KPA = 55000      -- from here a bar turns red

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

-- One row per slot. The ids carry the row number: row3, name3, val3, bar3.
local rows = {}
for r = 1, ROWS do
    rows[r] = string.format([[
  <div class="row" id="row%d">
    <span class="name" id="name%d">--</span>
    <span class="value" id="val%d">--</span>
    <div class="track" id="track%d"><div class="fill" id="bar%d"></div></div>
  </div>]], r, r, r, r, r)
end

ui:element({
    id = "tanks",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = [[
<html>
<head>
<meta name="viewport" content="width=480">
<style>
  body { background: #0B1622; color: #E4F1F7; font-family: Barlow; padding: 16px; font-size: 15px; }
  h1 { font-size: 22px; color: #38BDF8; margin: 0; }
  #count { color: #7A93A6; font-size: 13px; margin: 2px 0 12px 0; }
  .row { display: flex; align-items: center; gap: 12px; padding: 7px 10px; margin-bottom: 5px; background: #13233A; border-radius: 6px; }
  .name { flex: 1; }
  .value { width: 90px; text-align: right; font-weight: 600; }
  .track { width: 110px; height: 8px; background: #1E3350; border-radius: 4px; }
  .fill { height: 100%; width: 0%; background: #2E8B6E; border-radius: 4px; transition: width 0.8s ease; }
</style>
</head>
<body>
  <h1>Tanks</h1>
  <div id="count">Searching the network...</div>
]] .. table.concat(rows, "\n") .. [[
</body>
</html>
]] },
})

local data = ui:element({
    id = "tanks_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "tanks", data = {} },
})

ui:commit()

local LT = ic.enums.LogicType

-- Every device on the chip's data network whose name starts with PREFIX, sorted by name.
local found = {}
local function scan()
    found = {}
    for _, d in ipairs(ic.device.list()) do
        local name = d.display_name
        if name and name:sub(1, #PREFIX) == PREFIX then
            found[#found + 1] = { id = d.ref_id, name = name }
        end
    end
    table.sort(found, function(a, b) return a.name < b.name end)
end

local since_scan = 10        -- seconds since the last scan; the first tick scans

-- tick runs about twice a second. Every row's keys go in every payload, the empty ones
-- included: a row with no device gets `false` (hidden) and "--" for its text.
function tick(dt)
    since_scan = since_scan + dt
    if since_scan >= 10 then
        since_scan = 0
        scan()
    end

    local payload = { count = string.format("%d found", #found) }
    for r = 1, ROWS do
        local dev = found[r]
        local kpa = dev and ic.read_id(dev.id, LT.Pressure)
        local pct = math.floor(math.max(0, math.min(100, (kpa or 0) / FULL_KPA * 100)))
        payload["row" .. r] = dev ~= nil                                   -- shows or hides the row
        payload["name" .. r] = dev and dev.name or "--"
        payload["val" .. r] = kpa and string.format("%.0f kPa", kpa) or "--"
        payload["bar" .. r] = {
            width = string.format("%d%%", pct),
            ["background-color"] = (kpa and kpa >= ALARM_KPA) and "#B5352C" or "#2E8B6E",
        }
    end
    data:set_props({ data = payload })
    ui:commit()
end

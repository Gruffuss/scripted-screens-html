-- 03-controls.lua -- buttons and inputs talking to Lua. Paste into a Lua chip.
-- Shows: a <button> arrives in on_click with its id; an <input>, a checkbox, a range and a
-- <select> arrive in on_change as "name=value"; Lua answers through the data element.
-- :hover and :active styles work on the buttons.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local data  -- the data element, declared below

local state = { setpoint = 101, mode = "auto", fan = false, count = 0 }

local function show()
    data:set_props({ data = {
        echo = string.format("setpoint %d kPa, mode %s, fan %s, clicks %d",
            math.floor(state.setpoint), state.mode, state.fan and "on" or "off", state.count),
        fanled = state.fan,
    } })
    ui:commit()
end

ui:element({
    id = "panel",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = [[
<html>
<head>
<meta name="viewport" content="width=480">
<style>
  body { background: #0B1622; color: #E4F1F7; font-family: Barlow; padding: 16px; font-size: 14px; }
  h1 { font-size: 20px; color: #38BDF8; margin: 0 0 12px 0; }
  .row { display: flex; align-items: center; gap: 10px; margin: 10px 0; }
  label { width: 90px; color: #7A93A6; }
  button { padding: 6px 14px; background: #24314A; color: #E4F1F7; border-radius: 6px; font-size: 14px; }
  button:hover { background: #2E8B6E; }
  button:active { background: #38BDF8; color: #0B1622; }
  input[type=number], select { width: 120px; height: 28px; background: #172033; color: #E4F1F7; border: 1px solid #24314A; border-radius: 4px; padding: 0 6px; }
  input[type=range] { width: 200px; }
  #fanled { width: 14px; height: 14px; border-radius: 7px; background: #2E8B6E; box-shadow: 0 0 8px #2E8B6E; }
  #echo { margin-top: 18px; padding: 10px; background: #172033; border-radius: 8px; color: #E2A94E; }
</style>
</head>
<body>
  <h1>Pump control</h1>
  <div class="row"><label>Setpoint</label><input type="number" name="setpoint" value="101" min="0" max="500" step="1"></div>
  <div class="row"><label>Level</label><input type="range" name="level" min="0" max="100" value="40"></div>
  <div class="row"><label>Mode</label>
    <select name="mode"><option value="auto" selected>Automatic</option><option value="manual">Manual</option><option value="off">Off</option></select>
  </div>
  <div class="row"><label>Fan</label><input type="checkbox" name="fan" id="fan"><label for="fan">enabled</label><div id="fanled"></div></div>
  <div class="row"><button id="plus">+10</button><button id="minus">-10</button><button id="reset">reset</button></div>
  <div id="echo">waiting for input</div>
</body>
</html>
]] },
    on_click = function(id, player)
        if id == "plus" then state.setpoint = state.setpoint + 10
        elseif id == "minus" then state.setpoint = state.setpoint - 10
        elseif id == "reset" then state.setpoint = 101 end
        state.count = state.count + 1
        show()
    end,
    on_change = function(v, player)
        local name, value = tostring(v):match("^([^=]*)=(.*)$")
        if name == "setpoint" then state.setpoint = tonumber(value) or state.setpoint
        elseif name == "mode" then state.mode = value
        elseif name == "fan" then state.fan = (value == "true")
        elseif name == "level" then print("level " .. value) end
        show()
    end,
})

data = ui:element({
    id = "panel_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "panel", data = { fanled = false } },
})

ui:commit()

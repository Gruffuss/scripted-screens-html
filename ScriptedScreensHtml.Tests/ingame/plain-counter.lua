-- plain-counter.lua -- in-game check of the plain translator (not shipped).
-- A setInterval counter: a readout with toFixed and units, a bar width in %, a colour picked by a
-- condition, and clearInterval after 8 ticks. Compiled, it must become a vector scene plus plain Lua
-- on this chip's tick; afterwards the frame line's "work after compile" stays 0.
-- Expected after ~5 s: "100.0 kPa", bar at 100 %, bar colour red, status "stopped".

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local page = [[
<!doctype html>
<html>
<head>
<style>
  body { margin: 0; background: #11141A; color: #CFD6E4; font-family: Barlow; padding: 24px; }
  .readout { font-size: 40px; }
  .track { width: 300px; height: 20px; background: #232833; margin-top: 16px; }
  .bar { width: 0%; height: 20px; background: #2F855A; }
  .status { margin-top: 12px; font-size: 18px; color: #7C8798; }
</style>
</head>
<body>
  <div class="readout" id="kpa">0.0 kPa</div>
  <div class="track"><div class="bar" id="bar"></div></div>
  <div class="status" id="status">counting</div>
  <script>
    var n = 0;
    var timer = setInterval(function () {
      n = n + 1;
      document.getElementById('kpa').textContent = (n * 12.5).toFixed(1) + ' kPa';
      document.getElementById('bar').style.width = Math.min(100, n * 12.5) + '%';
      document.getElementById('bar').style.background = n >= 6 ? '#C53030' : '#2F855A';
      if (n >= 8) {
        clearInterval(timer);
        document.getElementById('status').textContent = 'stopped';
      }
    }, 500);
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "cnt",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

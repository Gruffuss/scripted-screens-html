-- plain-fields.lua -- in-game check of lists in reassigned object fields and rows with independent
-- choices (not shipped). A log kept in a state object, reassigned with concat().slice() through
-- Object.assign; device rows each choosing an icon, a status colour and a HOT badge independently, with a
-- separator between rows.
-- Expected once the timer stops (n = 5, ~3 s): log "event 5", "event 4", "event 3" (newest first, three
-- lines); Fan ON (green row), Pump OFF (red row), Heater ON (red row) with a HOT badge; separators between
-- the three rows.

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
  body { margin: 0; background: #11141A; color: #CFD6E4; font-family: Barlow; padding: 20px; font-size: 20px; }
  h2 { font-size: 16px; color: #7C8798; margin: 10px 0 4px; }
  .dev { display: flex; gap: 10px; padding: 4px 8px; width: 300px; }
  .dev.good { background: #1F3A2B; }
  .dev.bad { background: #4A1F22; }
  .icon { width: 44px; color: #F6E05E; }
  .badge { color: #11141A; background: #F6AD55; padding: 0 6px; }
  .sep { height: 2px; width: 300px; background: #3A4150; margin: 2px 0; }
  .line { padding: 2px 8px; width: 300px; background: #232833; margin-bottom: 3px; }
</style>
</head>
<body>
  <h2>Devices</h2>
  <div id="devs"></div>
  <h2>Log</h2>
  <div id="log"></div>
  <script>
    var state = { log: [], n: 0 };
    var devices = [
      { name: 'Fan', on: true, alarm: false, hot: false },
      { name: 'Pump', on: false, alarm: true, hot: false },
      { name: 'Heater', on: true, alarm: false, hot: true }
    ];
    function row(d, i) {
      var bad = d.alarm || state.n % 3 === i;
      return (i > 0 ? '<div class="sep"></div>' : '') +
        '<div class="dev ' + (bad ? 'bad' : 'good') + '">' +
        '<span class="icon">' + (d.on ? 'ON' : 'OFF') + '</span>' +
        '<span class="name">' + d.name + '</span>' +
        (d.hot ? '<span class="badge">HOT</span>' : '') +
        '</div>';
    }
    function render() {
      document.getElementById('devs').innerHTML = devices.map(row).join('');
      document.getElementById('log').innerHTML =
        state.log.map(function (e) { return '<div class="line">' + e + '</div>'; }).join('');
    }
    function set(patch) { Object.assign(state, patch); render(); }
    render();
    var t = setInterval(function () {
      var n = state.n + 1;
      set({ n: n, log: ['event ' + n].concat(state.log).slice(0, 3) });
      if (n >= 5) clearInterval(t);
    }, 500);
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "fld",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

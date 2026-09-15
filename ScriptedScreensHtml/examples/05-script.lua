-- 05-script.lua -- a page script does the presentation. Paste into a Lua chip.
-- Shows: JavaScript building rows from a `data` event, sorting on a header click, a
-- click counter, a <canvas> drawn once as vector paths. Lua sends a table of devices; the
-- script decides how to show it.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "list",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = [[
<html>
<head>
<meta name="viewport" content="width=480">
<style>
  body { background: #0B1622; color: #E4F1F7; font-family: Barlow; padding: 16px; font-size: 14px; }
  h1 { font-size: 20px; color: #38BDF8; margin: 0 0 10px 0; display: flex; justify-content: space-between; }
  h1 span { font-size: 12px; color: #7A93A6; font-weight: 400; }
  table { width: 100%; border-collapse: collapse; }
  th { text-align: left; color: #7A93A6; font-weight: 500; padding: 4px 6px; border-bottom: 1px solid #24314A; }
  th:hover { color: #38BDF8; }
  td { padding: 4px 6px; }
  tr.hot td { color: #E2A94E; }
  tr:nth-child(even) td { background: #101C2A; }
  canvas { margin-top: 12px; }
</style>
</head>
<body>
  <h1>Devices <span id="count">no data yet</span></h1>
  <table>
    <thead><tr><th id="by-name">Name</th><th id="by-temp">Temperature</th><th id="by-power">Power</th></tr></thead>
    <tbody id="rows"></tbody>
  </table>
  <canvas id="chart" width="440" height="100"></canvas>
  <script>
    var devices = [], key = 'name', clicks = 0;
    var rows = document.getElementById('rows');

    function render() {
      devices.sort(function (a, b) { return a[key] < b[key] ? -1 : a[key] > b[key] ? 1 : 0; });
      rows.innerHTML = '';
      devices.forEach(function (d) {
        var tr = document.createElement('tr');
        if (d.temp > 300) tr.className = 'hot';
        tr.innerHTML = '<td>' + d.name + '</td><td>' + d.temp.toFixed(1) + ' K</td><td>' + d.power + ' W</td>';
        rows.appendChild(tr);
      });
      document.getElementById('count').textContent = devices.length + ' devices, sorted by ' + key + ', ' + clicks + ' clicks';
      chart();
    }

    function chart() {
      var c = document.getElementById('chart').getContext('2d');
      c.clearRect(0, 0, 440, 100);
      c.fillStyle = '#172033'; c.fillRect(0, 0, 440, 100);
      var max = 1; devices.forEach(function (d) { max = Math.max(max, d.power); });
      var w = 440 / Math.max(1, devices.length);
      devices.forEach(function (d, i) {
        var h = 80 * d.power / max;
        c.fillStyle = d.temp > 300 ? '#E2A94E' : '#38BDF8';
        c.fillRect(i * w + 6, 90 - h, w - 12, h);
        c.fillStyle = '#E4F1F7'; c.font = '11px Barlow'; c.textAlign = 'center';
        c.fillText(d.name, i * w + w / 2, 99);
      });
    }

    // Lua's data table arrives here; `devices` is the array it sends.
    window.ondata = function (d) { if (d.devices) { devices = d.devices; render(); } };

    ['name', 'temp', 'power'].forEach(function (k) {
      document.getElementById('by-' + k).addEventListener('click', function () { key = k; clicks++; render(); });
    });
  </script>
</body>
</html>
]] },
})

local data = ui:element({
    id = "list_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "list", data = {} },
})

ui:commit()

-- Simulated devices; with real ones, fill the table from ic.find / network reads.
local names = { "Furnace", "Pump A", "Pump B", "Heater", "Filter" }
local t, acc = 0, 0
function tick(dt)
    acc = acc + dt
    if acc < 2 then return end
    acc = 0
    t = t + 1
    local list = {}
    for i, n in ipairs(names) do
        list[i] = { name = n, temp = 280 + 40 * math.abs(math.sin(t / 7 + i)), power = math.floor(100 + 400 * math.abs(math.cos(t / 5 + i))) }
    end
    data:set_props({ data = { devices = list } })
    ui:commit()
end

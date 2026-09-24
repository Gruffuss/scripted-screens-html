-- plain-find.lua -- in-game check of a table built by an immediately invoked function, an item found in it with
-- a fallback (find ... || list[0]), findIndex, and fields of a list's items as fixed values: each tab's colour,
-- each device's class, and each log line's colour taken from an entry kept in a state object (not shipped).
-- Expected once the timer stops (n = 4, ~2 s): title "CO2 (3 of 3)" in red (#FC8181); devices Scrubber (green),
-- Vent (grey), Tank (green); tabs Oxygen (blue), Nitrogen (yellow), CO2 (red), CO2 on a lighter ground; log
-- newest first, three lines: "picked CO2" (red), "picked Nitrogen" (yellow), "picked Oxygen" (blue - the pick
-- "xe" found nothing and fell back to the first role).

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local page = [==[
<!doctype html>
<html>
<head>
<style>
  body { margin: 0; background: #11141A; color: #CFD6E4; font-family: Barlow; padding: 20px; font-size: 20px; }
  h2 { font-size: 16px; color: #7C8798; margin: 10px 0 4px; }
  #title { font-size: 24px; margin: 0 0 6px; }
  #menu { display: flex; gap: 6px; }
  .tab { width: 110px; padding: 2px 6px; }
  .tab.on { background: #2A3345; }
  .tab.off { background: #11141A; }
  .dev { padding: 2px 8px; width: 300px; }
  .dev.on { color: #68D391; }
  .dev.off { color: #7C8798; }
  .line { padding: 2px 8px; width: 300px; background: #232833; margin-bottom: 3px; }
</style>
</head>
<body>
  <p id="title">-</p>
  <div id="menu"></div>
  <h2>Devices</h2>
  <div id="devs"></div>
  <h2>Log</h2>
  <div id="log"></div>
  <script>
    var ROLES = (function () {
      var PUMPS = [['Pump A', 'on'], ['Pump B', 'off']];
      var R = function (key, label, ink, devs) {
        return { key: key, label: label, ink: ink, devs: devs.map(function (d) { return { name: d[0], state: d[1] }; }) };
      };
      return [
        R('o2', 'Oxygen', '#63B3ED', PUMPS),
        R('n2', 'Nitrogen', '#F6E05E', [['Line', 'on']]),
        R('co2', 'CO2', '#FC8181', [['Scrubber', 'on'], ['Vent', 'off'], ['Tank', 'on']])
      ];
    })();
    var st = { pick: 'n2', n: 0, log: [] };
    function selected() { return ROLES.find(function (r) { return r.key === st.pick; }) || ROLES[0]; }
    function add(msg, ink) { st.log = [{ msg: msg, ink: ink }].concat(st.log).slice(0, 3); }
    function render() {
      var sel = selected();
      var at = ROLES.findIndex(function (r) { return r.key === st.pick; });
      document.getElementById('title').textContent = sel.label + ' (' + (at + 1) + ' of ' + ROLES.length + ')';
      document.getElementById('title').style.color = sel.ink;
      document.getElementById('menu').innerHTML = ROLES.map(function (r) {
        return '<span class="tab ' + (r.key === sel.key ? 'on' : 'off') + '" style="color:' + r.ink + '">' + r.label + '</span>';
      }).join('');
      document.getElementById('devs').innerHTML = sel.devs.map(function (d) {
        return '<div class="dev ' + d.state + '">' + d.name + '</div>';
      }).join('');
      document.getElementById('log').innerHTML = st.log.map(function (e) {
        return '<div class="line" style="color:' + e.ink + '">' + e.msg + '</div>';
      }).join('');
    }
    render();
    var t = setInterval(function () {
      st.n++;
      st.pick = ['o2', 'co2', 'xe', 'n2', 'co2'][st.n % 5];
      var sel = selected();
      add('picked ' + sel.label, sel.ink);
      render();
      if (st.n >= 4) clearInterval(t);
    }, 500);
  </script>
</body>
</html>
]==]

ui:clear()
ui:element({
    id = "fnd",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

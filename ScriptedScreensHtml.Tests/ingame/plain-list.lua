-- plain-list.lua -- in-game check of innerHTML part 2 (not shipped): lists of markup compiled once. A
-- readings table built in a for loop with +=, a log that grows newest first to four lines (a message while
-- it is empty, the line below it moving down as it grows), the devices that are on, each row's shape chosen
-- by its alarm, and a row of gases with one click listener for all of them (e.target.closest('li')).
-- Expected once the timer stops (tick 10, ~5 s): Room "116.3 kPa" and Tank "118.4 kPa" in red, Pipe "66.5 C"
-- in green; the log "event 9", "event 6", "event 3", newest first, then "end of log"; devices Fan (ALARM, red
-- row), Heater (ALARM, red row), Vent (ok) - Pump is off; gases O2 N2 CO2 with N2 highlighted, "tap a gas"
-- below. Tapping a gas highlights it and writes "picked <key>" (o2, n2 or co2).

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
  body { margin: 0; background: #11141A; color: #CFD6E4; font-family: Barlow; padding: 12px; font-size: 15px; }
  h4 { margin: 6px 0 3px 0; font-size: 14px; color: #7C8798; }
  p { margin: 2px 0; }
  .row { display: flex; width: 300px; }
  .name { width: 90px; }
  .val { width: 140px; }
  .line { padding: 1px 6px; background: #1A2130; margin-bottom: 2px; width: 280px; }
  .empty { color: #777777; }
  .dev { display: flex; width: 260px; padding: 1px 6px; background: #1B2230; margin-bottom: 2px; }
  .tag { width: 70px; color: #48BB78; }
  .dev.alarm { background: #3A1A1A; }
  .dev.alarm .tag { color: #F56565; }
  .menu { display: flex; margin: 0; padding: 0; }
  .menu li { list-style: none; padding: 2px 10px; margin-right: 6px; background: #1D2433; }
  .menu li.on { background: #2B4A7A; }
</style>
</head>
<body>
  <h4>Readings</h4>
  <div id="table"></div>
  <h4>Log</h4>
  <div id="log"></div>
  <p id="after">end of log</p>
  <h4>Devices</h4>
  <div id="devs"></div>
  <h4>Gas</h4>
  <ul id="menu" class="menu"></ul>
  <p id="picked">tap a gas</p>
  <script>
    const sensors = [{ name: 'Room', unit: 'kPa' }, { name: 'Tank', unit: 'kPa' }, { name: 'Pipe', unit: 'C' }];
    const base = [101.3, 88.4, 21.5];
    const log = [];
    const devices = [
      { name: 'Pump', on: true, alarm: false }, { name: 'Fan', on: true, alarm: false },
      { name: 'Heater', on: false, alarm: false }, { name: 'Vent', on: true, alarm: false }];
    const gases = [{ key: 'o2', label: 'O2' }, { key: 'n2', label: 'N2' }, { key: 'co2', label: 'CO2' }];
    let tick = 0;
    let chosen = 'n2';

    function readings() {
      let html = '';
      for (let i = 0; i < sensors.length; i++) {
        const v = base[i] + tick * (i + 1) * 1.5;
        html += '<div class="row"><span class="name">' + sensors[i].name + '</span><span class="val" style="color:'
          + (v > 100 ? '#F56565' : '#48BB78') + '">' + v.toFixed(1) + ' ' + sensors[i].unit + '</span></div>';
      }
      document.getElementById('table').innerHTML = html;
    }

    function showLog() {
      document.getElementById('log').innerHTML = log.length
        ? log.map((e) => '<div class="line">' + e + '</div>').join('')
        : '<p class="empty">no events yet</p>';
    }

    function showDevices() {
      document.getElementById('devs').innerHTML = devices.filter((d) => d.on).map((d) => d.alarm
        ? `<div class="dev alarm"><span class="name">${d.name}</span><span class="tag">ALARM</span></div>`
        : `<div class="dev"><span class="name">${d.name}</span><span class="tag">ok</span></div>`).join('');
    }

    function menu() {
      document.getElementById('menu').innerHTML = gases.map((g, i) =>
        `<li id="gas${i}" data-key="${g.key}" class="${g.key === chosen ? 'on' : ''}">${g.label}</li>`).join('');
    }

    document.getElementById('menu').addEventListener('click', (e) => {
      const li = e.target.closest('li');
      if (!li) return;
      chosen = li.dataset.key;
      document.getElementById('picked').textContent = 'picked ' + chosen;
      menu();
    });

    readings();
    showLog();
    showDevices();
    menu();
    const timer = setInterval(() => {
      tick++;
      if (tick % 3 === 0) {
        log.unshift('event ' + tick);
        if (log.length > 4) log.pop();
      }
      if (tick === 4) devices[1].alarm = true;
      if (tick === 7) { devices[0].on = false; devices[2].on = true; devices[2].alarm = true; }
      readings();
      showLog();
      showDevices();
      if (tick >= 10) clearInterval(timer);
    }, 500);
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "ls",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

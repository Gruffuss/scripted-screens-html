-- plain-flex.lua -- in-game check of flex and grid items and of flex gaps (not shipped): the children of a flex or
-- grid container are items with boxes of their own, never folded into text, so a value only known at run time may sit
-- in their attributes; and a flex gap spaces only the rows shown, so rows with two shapes compile as with margins.
-- Before any click: two grid rows "02:14 | tag INFO | 0" (INFO grey) and "02:13 | tag WARN | 1" (WARN orange), the
--   small grey "tag" 6 px left of each word; "at 0", then "at 2", "at 4"... twice a second (a data-at attribute
--   written into a grid item and a flex item, read back with getAttribute).
-- Below: three rows 6 px apart, "Pump on" (green box), "Fan" (dark), "Vent on" (green); a row of three chips 10 px
--   apart, green, dark, green; "end" under them; a box "TOGGLE".
-- Click TOGGLE: Pump turns dark and reads "Pump", its chip dark; again: Fan turns green "Fan on"; again: Vent turns
--   dark... The rows stay 6 px apart and the chips 10 px, and "end" and TOGGLE never move.

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
  body { margin: 0; padding: 8px; background: #11141A; color: #CFD6E4; font-family: Barlow; font-size: 20px; }
  .log { display: grid; grid-template-columns: 70px 1fr 40px; gap: 8px; align-items: center; height: 30px; margin-bottom: 4px; background: #1B2130; }
  #list { display: flex; flex-direction: column; gap: 6px; margin-top: 8px; }
  .row { height: 30px; background: #2B3A55; }
  .row.on { background: #2F855A; }
  #chips { display: flex; gap: 10px; margin-top: 8px; }
  .chip { width: 60px; height: 20px; background: #2B3A55; }
  .chip.on { background: #2F855A; }
  .big { height: 44px; margin-top: 8px; background: #232833; display: flex; align-items: center; justify-content: center; }
  p { margin: 4px 0; }
</style>
</head>
<body>
  <div id="log"></div>
  <p id="at">at -</p>
  <div id="list"></div>
  <div id="chips"></div>
  <p>end</p>
  <div id="tog" class="big">TOGGLE</div>
  <script>
    const entries = [{ t: '02:14', tag: 'INFO', color: '#8894A8' }, { t: '02:13', tag: 'WARN', color: '#F6AD55' }];
    const items = [{ n: 'Pump', on: true }, { n: 'Fan', on: false }, { n: 'Vent', on: true }];
    let k = 0, t = 0;
    function drawLog() {
      document.getElementById('log').innerHTML = entries.map((e, i) => '<div class="log"><span data-at="' + (k + i) + '">' + e.t + '</span>'
        + '<span style="display:flex;gap:6px;align-items:baseline"><span style="font-size:14px;color:#8894A8">tag</span>'
        + '<span data-at="' + (k * 2 + i) + '" style="color:' + e.color + '">' + e.tag + '</span></span><span>' + i + '</span></div>').join('');
      document.getElementById('at').textContent = 'at ' + Number(document.querySelectorAll('[data-at]')[1].getAttribute('data-at'));
    }
    function drawRows() {
      document.getElementById('list').innerHTML = items.map((g) => g.on ? '<div class="row on">' + g.n + ' on</div>' : '<div class="row">' + g.n + '</div>').join('');
      document.getElementById('chips').innerHTML = items.map((g) => g.on ? '<div class="chip on"></div>' : '<div class="chip"></div>').join('');
    }
    document.getElementById('tog').addEventListener('click', () => { items[t % 3].on = !items[t % 3].on; t++; drawRows(); });
    setInterval(() => { k++; drawLog(); }, 500);
    drawLog();
    drawRows();
  </script>
</body>
</html>
]==]

ui:clear()
ui:element({
    id = "flex",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

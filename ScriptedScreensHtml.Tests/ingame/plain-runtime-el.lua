-- plain-runtime-el.lua -- in-game check of elements chosen at run time: innerHTML written into one of
-- several elements, and getElementById with an id built at run time (a `$` helper, a getter reading
-- this.id, buttons markup makes found by 'pad' + i) (not shipped).
-- Before any press: three pads "1", "2", "3"; three dark lamps; "-" and "-"; two empty dark panels and two
--   empty strips; "no press yet".
-- Press pad 1: the first lamp turns green; the first line reads "press 1 on 1" (the 1 in bold); the upper
--   panel "1.5 kPa"; the upper strip three green bars, each longer than the last; "pad 1, 1 presses".
-- Press pad 3: the third lamp turns green; the second line "press 2 on 3"; the upper panel "3 kPa"; the
--   upper strip two green bars; "pad 3, 2 presses".
-- Press pad 2: the second lamp turns green; the first line "press 3 on 2"; the lower panel "4.5 kPa" and a
--   red "HIGH" under it; the lower strip one amber bar; "pad 2, 3 presses".
-- Press pad 1 again: the first lamp goes dark; the second line "press 4 on 1"; the upper panel "6 kPa" and
--   "HIGH"; the upper strip three green bars; "pad 1, 4 presses".
-- A panel or strip a press does not reach keeps what it showed.

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
  body { margin: 0; padding: 8px; background: #11141A; color: #CFD6E4; font-family: Barlow; font-size: 18px; }
  .row { display: flex; gap: 8px; }
  .pad { width: 130px; height: 64px; background: #2B3A55; display: flex; align-items: center; justify-content: center; font-size: 24px; }
  .lamp { width: 60px; height: 14px; background: #333A44; margin-top: 6px; }
  .lamp.on { background: #2F855A; }
  .line { margin: 4px 0 0 0; }
  .panel { background: #1C212B; padding: 2px 4px; margin-top: 4px; min-height: 6px; }
  .v { color: #88CC88; margin: 0; }
  .hi { color: #FF5555; margin: 0; }
  .strip { margin-top: 4px; }
  .seg { height: 8px; margin: 2px 0; }
  #log { margin: 4px 0 0 0; }
</style>
</head>
<body>
  <div id="pads" class="row"></div>
  <div class="row"><div id="lamp0" class="lamp"></div><div id="lamp1" class="lamp"></div><div id="lamp2" class="lamp"></div></div>
  <p id="ta" class="line">-</p>
  <p id="tb" class="line">-</p>
  <div id="left" class="panel"></div>
  <div id="right" class="panel"></div>
  <div id="s1" class="strip"></div>
  <div id="s2" class="strip"></div>
  <p id="log">no press yet</p>
  <script>
    const $ = (id) => document.getElementById(id);
    const lamps = [];
    for (let i = 0; i < 3; i++) lamps.push({ id: 'lamp' + i, on: false, get el() { return $(this.id); } });
    let h = '';
    for (let i = 0; i < 3; i++) h += '<div class="pad" id="pad' + i + '">' + (i + 1) + '</div>';
    $('pads').innerHTML = h;
    const panels = [$('left'), $('right')];
    const counts = [1, 3, 2];
    let n = 0;
    function fill(el, v) {
      el.innerHTML = '<p class="v">' + v + ' kPa</p>' + (v > 3 ? '<p class="hi">HIGH</p>' : '');
    }
    function strip(id, count, colour) {
      let s = '';
      for (let i = 0; i < count; i++) s += '<div class="seg" style="width:' + (40 + i * 40) + 'px;background:' + colour + '"></div>';
      $(id).innerHTML = s;
    }
    function press(i) {
      n++;
      const lamp = lamps[i];
      lamp.on = !lamp.on;
      lamp.el.classList.toggle('on', lamp.on);
      $(n % 2 ? 'ta' : 'tb').innerHTML = 'press <b>' + n + '</b> on ' + (i + 1);
      fill(panels[i % 2], n * 1.5);
      strip(i === 1 ? 's2' : 's1', counts[n % 3], i === 1 ? '#AA7733' : '#33AA77');
      $('log').textContent = 'pad ' + (i + 1) + ', ' + n + ' presses';
    }
    [0, 1, 2].forEach((i) => $('pad' + i).addEventListener('click', () => press(i)));
  </script>
</body>
</html>
]==]

ui:clear()
ui:element({
  id = "page",
  type = "html",
  rect = { unit = "px", x = 0, y = 0, w = W, h = H },
  props = { src = page },
})
ui:commit()

-- plain-globals.lua -- in-game check of translator group 2 (not shipped): window size, localStorage
-- on the chip's store, an attribute CSS selects on, hidden, reading back, location.hash, console.
-- Expected after ~4 s on a 460x460 console: "460 x 460", "visit N" (N counts pushes of this same
-- source, if the chip's store keeps it), the lamp green, the note shown, "lamp is on", "#x".

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
  body { margin: 0; background: #11141A; color: #CFD6E4; font-family: Barlow; padding: 24px; font-size: 22px; }
  p { margin: 8px 0; }
  .lamp { width: 40px; height: 40px; border-radius: 20px; background: #552222; }
  .lamp[data-state="on"] { background: #22EE55; }
  #note { color: #F6AD55; }
</style>
</head>
<body>
  <p id="size">size</p>
  <p id="visits">visits</p>
  <div class="lamp" id="lamp"></div>
  <p id="note" hidden>shown after 2 s</p>
  <p id="read">not read yet</p>
  <p id="hash">no hash</p>
  <script>
    console.log('page starting');
    document.getElementById('size').textContent = window.innerWidth + ' x ' + window.innerHeight;
    var visits = parseInt(localStorage.getItem('visits') || '0') + 1;
    localStorage.setItem('visits', String(visits));
    document.getElementById('visits').textContent = 'visit ' + visits;
    location.hash = '#x';
    document.getElementById('hash').textContent = location.hash;
    setTimeout(function () {
      document.getElementById('lamp').setAttribute('data-state', 'on');
      console.log('lamp on');
    }, 1000);
    setTimeout(function () {
      document.getElementById('note').hidden = false;
    }, 2000);
    setTimeout(function () {
      document.getElementById('read').textContent = 'lamp is ' + document.getElementById('lamp').getAttribute('data-state');
    }, 3000);
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "glb",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

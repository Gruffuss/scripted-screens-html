-- plain-frames.lua -- in-game check of requestAnimationFrame and the pointer's press events (not shipped).
-- At start: an amber block slides left to right across the track and back, smoothly (every frame, not twice a
-- second), taking 2 s each way; "frames N" counts up about 30-60 a second; "running".
-- STOP: the block halts where it is, the count stops, "stopped". GO: it carries on from there, "running".
-- HOLD (press and keep the pointer down): the button turns green and reads "held" at once, before the release;
-- release on it: dark again, "released", then the click count goes up by one. Press, then slide off it while
-- held: dark again, "slid off" (and no click). The line under it counts "down D up U leave L click C".

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
  body { margin: 0; padding: 8px; background: #11141A; color: #CFD6E4; font-family: Barlow; font-size: 22px; }
  #track { position: relative; height: 48px; background: #232833; margin-bottom: 8px; }
  #block { position: absolute; left: 0; top: 4px; width: 60px; height: 40px; background: #D69E2E; }
  p { margin: 0 0 8px 0; }
  .pair { display: flex; gap: 8px; margin-bottom: 8px; }
  .big { flex: 1; height: 80px; background: #2B3A55; display: flex; align-items: center; justify-content: center; }
  #hold { height: 110px; background: #3A2A3F; display: flex; align-items: center; justify-content: center; margin-bottom: 8px; }
  #hold.held { background: #38A169; color: #11141A; }
</style>
</head>
<body>
  <div id="track"><div id="block"></div></div>
  <p id="count">frames 0</p>
  <p id="state">running</p>
  <div class="pair">
    <div id="stop" class="big">STOP</div>
    <div id="go" class="big">GO</div>
  </div>
  <div id="hold">HOLD</div>
  <p id="press">down 0 up 0 leave 0 click 0</p>
  <p id="said">-</p>
  <script>
    const block = document.getElementById('block');
    const count = document.getElementById('count');
    const span = window.innerWidth - 16 - 60;
    let x = 0, dir = 1, frames = 0, last = null, handle = 0;
    function step(t) {
      if (last !== null) x += dir * (t - last) * span / 2000;
      if (x >= span) { x = span; dir = -1; }
      if (x <= 0) { x = 0; dir = 1; }
      last = t;
      frames++;
      block.style.left = x + 'px';
      count.textContent = 'frames ' + frames;
      handle = requestAnimationFrame(step);
    }
    handle = requestAnimationFrame(step);
    document.getElementById('stop').addEventListener('click', () => {
      cancelAnimationFrame(handle);
      document.getElementById('state').textContent = 'stopped';
    });
    document.getElementById('go').addEventListener('click', () => {
      cancelAnimationFrame(handle);
      last = null;
      handle = requestAnimationFrame(step);
      document.getElementById('state').textContent = 'running';
    });

    const hold = document.getElementById('hold');
    const said = document.getElementById('said');
    let downs = 0, ups = 0, leaves = 0, clicks = 0;
    function show() { document.getElementById('press').textContent = 'down ' + downs + ' up ' + ups + ' leave ' + leaves + ' click ' + clicks; }
    hold.addEventListener('mousedown', () => { downs++; hold.classList.add('held'); said.textContent = 'held'; show(); });
    hold.addEventListener('mouseup', () => { ups++; hold.classList.remove('held'); said.textContent = 'released'; show(); });
    hold.addEventListener('mouseleave', () => { leaves++; hold.classList.remove('held'); said.textContent = 'slid off'; show(); });
    hold.addEventListener('click', () => { clicks++; show(); });
  </script>
</body>
</html>
]==]

ui:clear()
ui:element({
    id = "frames",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

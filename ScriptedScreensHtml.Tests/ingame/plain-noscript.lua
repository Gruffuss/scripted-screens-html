-- plain-noscript.lua -- in-game check of a page whose only code is in onclick attributes, with no <script> at all
-- (not shipped). A browser runs those attributes; so must the console.
-- Before any click: a dark red lamp, "lamp: OFF", boxes ON and OFF, a dark box "SELECT", "box: none".
-- Click ON: the lamp turns green and the line reads "lamp: ON". Click OFF: red again, "lamp: OFF".
-- Click SELECT: it turns amber and "box: selected"; again: dark and "box: none".

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
  .lamp { width: 90px; height: 90px; border-radius: 45px; background: #742A2A; margin-bottom: 8px; }
  .lamp.lit { background: #38A169; }
  .big { height: 72px; margin-bottom: 8px; background: #232833; display: flex; align-items: center; justify-content: center; }
  .big.sel { background: #D69E2E; color: #11141A; }
  .pair { display: flex; gap: 8px; }
  .pair .big { flex: 1; }
  p { margin: 0 0 8px 0; }
</style>
</head>
<body>
  <div id="lamp" class="lamp"></div>
  <p id="state">lamp: OFF</p>
  <div class="pair">
    <div id="on" class="big" onclick="document.getElementById('lamp').classList.add('lit'); document.getElementById('state').textContent = 'lamp: ON'">ON</div>
    <div id="off" class="big" onclick="document.getElementById('lamp').classList.remove('lit'); document.getElementById('state').textContent = 'lamp: OFF'">OFF</div>
  </div>
  <div id="box" class="big" onclick="this.classList.toggle('sel'); document.getElementById('pick').textContent = this.classList.contains('sel') ? 'box: selected' : 'box: none'">SELECT</div>
  <p id="pick">box: none</p>
</body>
</html>
]==]

ui:clear()
ui:element({
    id = "noscript",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

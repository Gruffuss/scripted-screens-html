-- plain-bool.lua -- in-game check of booleans written into text (not shipped): a comparison, every() and
-- includes() joined to a string by a timer.
-- Expected after ~2 s: "flag true", "all on: true", "has 3: false".

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
  body { margin: 0; background: #11141A; color: #CFD6E4; font-family: Barlow; padding: 24px; font-size: 26px; }
  p { margin: 10px 0; }
</style>
</head>
<body>
  <p id="flag">flag ?</p>
  <p id="all">all on: ?</p>
  <p id="has">has 3: ?</p>
  <script>
    var n = 0;
    var on = [1, 2];
    var t = setInterval(function () {
      n++;
      if (n < 3) return;
      clearInterval(t);
      document.getElementById('flag').textContent = 'flag ' + (n > 2);
      document.getElementById('all').textContent = 'all on: ' + on.every(function (x) { return x > 0; });
      document.getElementById('has').textContent = 'has 3: ' + on.includes(3);
    }, 500);
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "bool",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

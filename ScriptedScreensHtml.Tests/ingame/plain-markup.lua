-- plain-markup.lua -- in-game check of innerHTML part 1 (not shipped): rich text with a bold value and
-- a coloured word picked by a condition, a helper function returning markup with a class and a style
-- width, a panel switching between two shapes, and an element showing its own content until a write.
-- Expected after ~4 s: "running" in italics; "Pressure 50.0 kPa, high" with "high" in orange; the
-- panel showing the "odd" bar at half width.

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
  .bar { width: 300px; background: #232833; margin-top: 12px; }
  .lbl { display: block; font-size: 16px; color: #7C8798; padding: 2px 6px; }
  .fill { height: 14px; background: #3182CE; }
  .even { color: #9AE6B4; }
</style>
</head>
<body>
  <div id="status">starting</div>
  <p id="rt">no reading yet</p>
  <div id="panel"></div>
  <script>
    function bar(label, pct, unit) {
      unit = unit || '%';
      return '<div class="bar"><span class="lbl">' + label + '</span><div class="fill" style="width:' + pct + unit + '"></div></div>';
    }
    var n = 0;
    var t = setInterval(function () {
      n = n + 1;
      document.getElementById('rt').innerHTML = 'Pressure <b>' + (n * 10).toFixed(1) + '</b> kPa, ' +
        (n > 3 ? '<span style="color:#F6AD55">high</span>' : '<span style="color:#48BB78">ok</span>');
      document.getElementById('panel').innerHTML = n % 2 ? bar('odd', n * 10) : '<p class="even">even tick ' + n + '</p>';
      if (n >= 5) clearInterval(t);
    }, 500);
    setTimeout(function () {
      document.getElementById('status').innerHTML = '<i>running</i>';
    }, 1000);
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "mk",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

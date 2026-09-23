-- 09-transition.lua -- a CSS transition that runs once and then stops.
--
-- A second in, the bar widens over 1.2 s and the note says so; at 2.6 s the note changes again and
-- the page is still from then on. Compiled, the page becomes a vector scene and a few lines of Lua on
-- this chip's tick: nothing of the HTML mod runs for it afterwards, and the width glides on the
-- renderer. The colour changes at once: the renderer glides numbers, not colours.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

local page = [[
<html>
<head>
<meta name="viewport" content="width=768">
<style>
  body { background: #11141a; color: #cfd6e4; font-family: Barlow; padding: 40px; }
  h1   { font-size: 34px; margin: 0 0 24px; }
  .bar { height: 46px; background: #2b6cb0; border-radius: 6px; width: 120px;
         transition: width 1.2s ease-in-out, background-color 1.2s ease-in-out; }
  .bar.wide { width: 620px; background: #2f855a; }
  .note { margin-top: 26px; font-size: 18px; color: #7c8798; }
</style>
</head>
<body>
  <h1>One transition, then still</h1>
  <div class="bar" id="bar"></div>
  <p class="note" id="note">waiting</p>
  <script>
    // fires once, a second in: the transition runs for 1.2 s and is then finished forever
    setTimeout(function () {
      document.getElementById('bar').classList.add('wide');
      document.getElementById('note').textContent = 'transition started';
    }, 1000);
    setTimeout(function () {
      document.getElementById('note').textContent = 'finished - the page is now static';
    }, 2600);
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "tr",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

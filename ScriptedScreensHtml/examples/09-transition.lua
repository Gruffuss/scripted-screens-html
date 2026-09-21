-- 09-transition.lua -- a CSS transition that runs once and then stops.
--
-- Every other example animates with @keyframes, requestAnimationFrame or a timer, so none of them
-- ever leaves a *finished* transition behind. That gap hid a defect: a tween that has ended is kept
-- (deliberately, so its end-value expression stays in the scene) and used to disqualify its element
-- from the emit cache for the life of the page, so every element that had ever transitioned was
-- rebuilt on every frame from then on. This page is the case that catches it.
--
-- Watch the diagnostics line: after the transition ends, `why ... tween` must fall back to 0 and
-- `cache ... rebuilt` must return to what it was before the transition started.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

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
ui:add("html", { id = "tr", props = { src = page } })
ui:commit()

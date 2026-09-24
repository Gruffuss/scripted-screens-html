-- plain-apple.lua -- in-game check of a theme set on document.documentElement, list callbacks reading the array
-- they walk (the third argument), and scrollTop of a box that does not scroll (not shipped).
-- Before any click: a dark page, THEME and ADD boxes; three tank rows "O2 80% (1 of 3)", "N2 15% (2 of 3)",
-- "CO2 40% (3 of 3)", the last one green; below them "LOW N2, 1 low" in green; "dark theme, scroll 0".
-- Every half second one tank's level moves on by 35 (wrapping at 100): the LOW rows follow it, the last one green,
-- each saying how many are low. Click THEME: the page turns light (background, boxes and text from the custom
-- properties; the rows keep their own blue and green) and
-- the line reads "light theme, scroll 0"; again: dark. Click ADD: a row "H3 20% (4 of 4)" joins, green as the
-- last, and "LOW H3" too; ADD at five rows takes the last one away. The scroll number stays 0.

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
  :root { --bg: #11141A; --card: #232833; --ink: #CFD6E4; }
  [data-mode="light"] { --bg: #E8ECF2; --card: #C9D1DE; --ink: #1A202C; }
  body { margin: 0; padding: 8px; background: var(--bg); color: var(--ink); font-family: Barlow; font-size: 20px; }
  .big { height: 64px; margin-bottom: 8px; background: var(--card); display: flex; align-items: center; justify-content: center; }
  .pair { display: flex; gap: 8px; }
  .pair .big { flex: 1; }
  .box { height: 140px; margin-bottom: 8px; }
  .row { height: 26px; margin-bottom: 2px; padding: 0 6px; background: #2B3A55; color: #E2E8F0; }
  .row.last { background: #2F855A; }
  p { margin: 0 0 8px 0; }
</style>
</head>
<body>
  <div class="pair"><div id="theme" class="big">THEME</div><div id="add" class="big">ADD</div></div>
  <div id="tanks" class="box"></div>
  <div id="low" class="box"></div>
  <p id="info">-</p>
<script>
const tanks = [{ n: 'O2', level: 80 }, { n: 'N2', level: 15 }, { n: 'CO2', level: 40 }];
let light = false, k = 0;
const root = document.documentElement;
function render() {
  const box = document.getElementById('tanks');
  const kept = box.scrollTop;
  box.innerHTML = tanks.map((t, i, all) => '<div class="row' + (i === all.length - 1 ? ' last' : '') + '">' + t.n + ' ' + t.level + '% (' + (i + 1) + ' of ' + all.length + ')</div>').join('');
  box.scrollTop = kept;
  document.getElementById('low').innerHTML = tanks.filter((t) => t.level < 30).map((t, i, arr) => '<div class="row' + (i === arr.length - 1 ? ' last' : '') + '">LOW ' + t.n + ', ' + arr.length + ' low</div>').join('');
  document.getElementById('info').textContent = root.getAttribute('data-mode') + ' theme, scroll ' + box.scrollTop;
}
document.getElementById('theme').addEventListener('click', () => { light = !light; root.setAttribute('data-mode', light ? 'light' : 'dark'); render(); });
document.getElementById('add').addEventListener('click', () => { if (tanks.length < 5) tanks.push({ n: 'H' + tanks.length, level: 20 }); else tanks.pop(); render(); });
setInterval(() => { k++; const t = tanks[k % tanks.length]; t.level = (t.level + 35) % 100; render(); }, 500);
root.setAttribute('data-mode', 'dark');
render();
</script>
</body>
</html>
]==]

ui:clear()
ui:element({
    id = "apple",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

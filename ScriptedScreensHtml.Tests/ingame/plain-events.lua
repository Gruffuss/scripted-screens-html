-- plain-events.lua -- in-game check of onclick attributes, `this` in click listeners, for loops that count
-- from 1, down and by 2, and hidden under a display rule of the page's own (not shipped).
-- Before any click: boxes A, B, C, D, E, O dark and reading "A: 0", "B: 0", "C: 0", "D 0", "E 0", "O";
--   "order: -"; three rows of numbered cells "1", "3", "0" (one cell each); the green strip "shown: its
--   display rule beats hidden" drawn; nothing where the hidden paragraph is.
-- Click A (onclick="this.classList.toggle('on'); countA(event.currentTarget === this)"): A turns green / dark
--   by turns and reads "A: 1 true", "A: 2 true"...
-- Click B (onclick="bump(this)"): B reads "B: 1", "B: 2"... and stays dark.
-- Click C (an element with no id, onclick attribute): C reads "C: 1", "C: 2"...
-- Click D or E (one listener function on both, using `this`): the clicked box turns green / dark by turns and
--   reads "D 1", "E 2"... (the number counts clicks on D and E together).
-- Click O (its onclick attribute adds "h", then a listener the script added writes the line): "order: h",
--   then "order: hh"... ("order: " with nothing after it on the first click means the listener ran first).
-- Every click on A, B, D, E or O also changes how many cells each row has: 2, 3, 4, 1, 2...; with n cells
--   the rows read "1 2 .. n" (counting from 1 with <=), "3 2 .. 4-n" (counting down with >) and
--   "0 2 .. 2n-2" (a step of 2).

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
  .grid { display: flex; flex-wrap: wrap; gap: 8px; }
  .box { width: 130px; height: 64px; background: #232833; display: flex; align-items: center; justify-content: center; }
  .box.on { background: #2F855A; }
  .cells { display: flex; gap: 6px; margin-top: 6px; }
  .cell { width: 34px; height: 26px; background: #2B3A55; display: flex; align-items: center; justify-content: center; font-size: 16px; }
  .strip { display: flex; margin-top: 6px; padding: 4px; background: #276749; font-size: 16px; }
  #order { margin: 6px 0 0 0; }
</style>
</head>
<body>
  <div class="grid">
    <div id="a" class="box" onclick="this.classList.toggle('on'); countA(event.currentTarget === this)">A: 0</div>
    <div id="b" class="box" onclick="bump(this)">B: 0</div>
    <div class="box" onclick="clicksC++; document.getElementById('c-out').textContent = 'C: ' + clicksC"><span id="c-out">C: 0</span></div>
    <div id="d" class="box">D 0</div>
    <div id="e" class="box">E 0</div>
    <div id="o" class="box" onclick="order += 'h'">O</div>
  </div>
  <p id="order">order: -</p>
  <div id="up" class="cells"><div class="cell">1</div></div>
  <div id="down" class="cells"><div class="cell">3</div></div>
  <div id="even" class="cells"><div class="cell">0</div></div>
  <div class="strip" hidden>shown: its display rule beats hidden</div>
  <p hidden>this paragraph is hidden</p>
  <script>
    var clicksA = 0, clicksB = 0, clicksC = 0, clicksDE = 0, order = '';
    var SIZES = [1, 2, 3, 4];
    var turn = 0;
    function countA(same) {
      clicksA++;
      document.getElementById('a').textContent = 'A: ' + clicksA + ' ' + same;
      grow();
    }
    function bump(el) {
      clicksB++;
      el.textContent = 'B: ' + clicksB;
      grow();
    }
    function grow() {
      turn++;
      var n = SIZES[turn % 4];
      var up = '';
      for (var i = 1; i <= n; i++) up += '<div class="cell">' + i + '</div>';
      document.getElementById('up').innerHTML = up;
      var down = '';
      for (var j = 3; j > 3 - n; j--) down += '<div class="cell">' + j + '</div>';
      document.getElementById('down').innerHTML = down;
      var even = '';
      for (var k = 0; k < 2 * n; k += 2) even += '<div class="cell">' + k + '</div>';
      document.getElementById('even').innerHTML = even;
    }
    [document.getElementById('d'), document.getElementById('e')].forEach(function (box) {
      box.addEventListener('click', function () {
        clicksDE++;
        this.classList.toggle('on');
        this.textContent = this.id.toUpperCase() + ' ' + clicksDE;
        grow();
      });
    });
    document.getElementById('o').addEventListener('click', function () {
      document.getElementById('order').textContent = 'order: ' + order;
      grow();
    });
  </script>
</body>
</html>
]==]

ui:clear()
ui:element({
    id = "evt",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

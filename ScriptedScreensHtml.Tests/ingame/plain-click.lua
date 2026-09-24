-- plain-click.lua -- in-game check of clicks on a compiled page (not shipped). Three large targets, one
-- per way a page takes a click: a box with addEventListener('click'), a box with its onclick property
-- set, and rows written with innerHTML whose clicks one listener on their container catches
-- (e.target.closest('.row')).
-- Expected before any click: "A: 0", "B: 0", "row: none", every target dark. Each click on A or B adds 1
-- to its count and turns it green; a click on row 1 or row 2 writes "row: 1" or "row: 2" and highlights
-- that row only.

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
  body { margin: 0; background: #11141A; color: #CFD6E4; font-family: Barlow; font-size: 28px; }
  .big { height: 110px; margin: 10px; background: #232833; display: flex; align-items: center;
         justify-content: center; }
  .big.on { background: #2F855A; }
  #rows { display: flex; margin: 10px; gap: 10px; }
  .row { flex: 1; height: 110px; background: #232833; display: flex; align-items: center;
         justify-content: center; }
  .row.on { background: #3182CE; }
  #picked { margin: 0 10px; font-size: 22px; }
</style>
</head>
<body>
  <div class="big" id="a">A: 0</div>
  <div class="big" id="b">B: 0</div>
  <div id="rows"></div>
  <p id="picked">row: none</p>
  <script>
    var a = 0, b = 0;
    document.getElementById('a').addEventListener('click', function () {
      a++;
      var el = document.getElementById('a');
      el.textContent = 'A: ' + a;
      el.classList.add('on');
    });
    function clickB() {
      b++;
      var el = document.getElementById('b');
      el.textContent = 'B: ' + b;
      el.classList.add('on');
    }
    document.getElementById('b').onclick = clickB;
    var picked = 0;
    function render() {
      var h = '';
      for (var i = 0; i < 2; i++) {
        h += '<div class="row' + (i + 1 === picked ? ' on' : '') + '" data-k="' + (i + 1) + '">' +
          'row ' + (i + 1) + '</div>';
      }
      document.getElementById('rows').innerHTML = h;
    }
    document.getElementById('rows').addEventListener('click', function (e) {
      var r = e.target.closest('.row');
      if (!r) return;
      picked = +r.dataset.k;
      render();
      document.getElementById('picked').textContent = 'row: ' + picked;
    });
    render();
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "clk",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

-- plain-select.lua -- in-game check of translator group 3 (not shipped): querySelectorAll and its
-- forEach, an element chosen at run time by a computed id, textContent +=, a value from a fixed set
-- through a function's returned object into an attribute CSS selects on, innerText and classList reads.
-- Expected after ~4 s: rows "row 0".."row 3" with "row 1" highlighted, "wait......" (six dots),
-- "mode odd" in orange, "4 rows, class row, text row 2".

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
  .row { padding: 4px 8px; background: #232833; margin-bottom: 4px; width: 200px; }
  .row.on { background: #2F855A; }
  p { margin: 8px 0; }
  #mode { color: #7C8798; }
  #mode[data-mode="odd"] { color: #F6AD55; }
</style>
</head>
<body>
  <div id="list">
    <div class="row" id="row0">a</div>
    <div class="row" id="row1">b</div>
    <div class="row" id="row2">c</div>
    <div class="row" id="row3">d</div>
  </div>
  <p id="dots">wait</p>
  <p id="mode" data-mode="even">mode even</p>
  <p id="info">info</p>
  <script>
    var rows = document.querySelectorAll('.row');
    rows.forEach(function (r, i) { r.textContent = 'row ' + i; });
    function state(n) { return { mode: n % 2 ? 'odd' : 'even' }; }
    var i = 0;
    var t = setInterval(function () {
      for (var k = 0; k < rows.length; k++) rows[k].classList.remove('on');
      document.getElementById('row' + (i % 4)).classList.add('on');
      document.getElementById('dots').textContent += '.';
      var m = state(i).mode;
      var p = document.getElementById('mode');
      p.setAttribute('data-mode', m);
      p.textContent = 'mode ' + m;
      i++;
      if (i >= 6) {
        clearInterval(t);
        document.getElementById('info').textContent =
          rows.length + ' rows, class ' + rows[1].classList.item(0) + ', text ' + rows[2].innerText;
      }
    }, 500);
  </script>
</body>
</html>
]]

ui:clear()
ui:element({
    id = "sel",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

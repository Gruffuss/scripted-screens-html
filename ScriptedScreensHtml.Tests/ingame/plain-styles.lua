-- plain-styles.lua -- in-game check of style attributes written from a fixed set, of one markup write with more than 32
-- shapes, and of lists turned round in place (not shipped).
-- Before any click: two cards side by side, "Room" and "Vent", each with a thin grey bar under its name; a grey strip
--   "mode", "left" and "right" 12 px apart on one line; a row of badges "A", "no B", "C", "E"; a panel
--   "panel one 0"; three rows "Pump", "Fan", "Vent" (Pump and Vent green) and four tags "a b c d"; a box "NEXT" at the bottom.
-- Twice a second: the rows turn round (Fan, Vent, Pump - then Vent, Pump, Fan...), the tags too, the other way
--   (d a b c - then c d a b...); every row and tag stays where it is, only the names move.
-- Click NEXT (the wide box at the bottom): the Room card turns red-bordered on a dark red fill with an orange bar and
--   a red name, the strip turns brown with "mode leftright" run together as one text; the badges
--   change (A goes, B shows "B"; every third click C goes; D shows on the 1st, 5th... click; E hides on the 2nd, 7th...);
--   the panel steps through "panel two" with three rows x, y, z under it, then "panel three", then "panel one N".
--   Click again: the card and strip go back. Nothing overlaps and "end" sits under the lowest thing each time.

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
  body { margin: 0; padding: 8px; background: #11141A; color: #CFD6E4; font-family: Barlow; font-size: 18px; }
  #cards { display: flex; gap: 8px; }
  .bar { display: flex; gap: 6px; height: 28px; align-items: center; margin-top: 6px; }
  .b { padding: 1px 6px; background: #2B3A55; }
  .p { height: 30px; background: #232833; margin-top: 6px; }
  .r { height: 20px; background: #2B3A55; margin-top: 3px; font-size: 14px; }
  .row { height: 22px; background: #2B3A55; margin-top: 3px; font-size: 14px; }
  .row.on { background: #2F855A; }
  #tags { display: flex; gap: 6px; margin-top: 6px; }
  .tag { width: 40px; background: #345; font-size: 14px; }
  #next { height: 60px; margin-top: 8px; background: #3A4A6A; display: flex; align-items: center; justify-content: center; font-size: 24px; }
  p { margin: 4px 0; }
</style>
</head>
<body>
  <div id="cards"></div>
  <div id="strip"></div>
  <div id="screen"></div>
  <div id="list"></div>
  <div id="tags"></div>
  <div id="next">NEXT</div>
  <p>end</p>
  <script>
    const st = { hot: false, a: true, b: false, c: true, d: false, e: true, mode: 0, clicks: 0, items: ['x', 'y', 'z'] };
    const rows = [{ n: 'Pump', on: true }, { n: 'Fan', on: false }, { n: 'Vent', on: true }];
    const tags = ['a', 'b', 'c', 'd'];
    function cards() {
      const list = [{ label: 'Room', hot: st.hot }, { label: 'Vent', hot: false }].map((c) => ({ label: c.label, ink: c.hot ? '#EE5555' : '#CFD6E4',
        style: 'box-sizing:border-box;flex:1;display:flex;flex-direction:column;gap:4px;padding:6px;border:2px solid ' + (c.hot ? '#CC3333' : '#556070') + ';background:' + (c.hot ? '#3A1818' : '#1B2130') }));
      document.getElementById('cards').innerHTML = list.map((c) => '<div style="' + c.style + '"><span data-at="' + st.clicks + '" style="color:' + c.ink + '">' + c.label + '</span>'
        + '<div style="height:4px;background:' + (st.hot ? '#FFAA00' : '#445566') + '"></div></div>').join('');
      const strip = st.hot ? 'display:block;background:#3A2A1A;margin-top:6px' : 'display:flex;gap:12px;background:#232833;margin-top:6px';
      document.getElementById('strip').innerHTML = '<div style="' + strip + '"><span>mode </span><span>left</span><span>right</span></div>';
    }
    function screen() {
      document.getElementById('screen').innerHTML = '<div class="bar">'
        + (st.a ? '<span class="b">A</span>' : '')
        + (st.b ? '<span class="b">B</span>' : '<span class="b">no B</span>')
        + (st.c ? '<span class="b">C</span>' : '')
        + (st.d ? '<span class="b">D</span>' : '')
        + (st.e ? '<span class="b">E</span>' : '')
        + '</div>'
        + (st.mode === 0 ? '<div class="p">panel one ' + st.clicks + '</div>'
          : st.mode === 1 ? '<div class="p">panel two</div>' + st.items.map((x) => '<div class="r">' + x + '</div>').join('')
          : '<div class="p">panel three</div>');
    }
    function turn() {
      document.getElementById('list').innerHTML = rows.map((g) => '<div class="row' + (g.on ? ' on' : '') + '">' + g.n + '</div>').join('');
      document.getElementById('tags').innerHTML = tags.map((t) => '<div class="tag">' + t + '</div>').join('');
    }
    document.getElementById('next').addEventListener('click', () => {
      st.clicks++;
      st.hot = !st.hot;
      st.a = !st.a;
      if (st.clicks % 2) st.b = !st.b;
      if (st.clicks % 3 === 0) st.c = !st.c;
      st.d = st.clicks % 4 === 1;
      st.e = st.clicks % 5 !== 2;
      st.mode = st.clicks % 3;
      cards();
      screen();
    });
    setInterval(() => { rows.push(rows.shift()); tags.unshift(tags.pop()); turn(); }, 500);
    cards();
    screen();
    turn();
  </script>
</body>
</html>
]==]

ui:clear()
ui:element({
    id = "styles",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

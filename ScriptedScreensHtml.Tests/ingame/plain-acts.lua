-- plain-acts.lua -- in-game check of clicks kept as functions in an array (not shipped): a helper `act(fn)` pushes
-- fn onto `acts` and returns the attribute ` data-act="<index>"`, markup is written with it, and after the write
-- every [data-act] shown gets a listener calling acts[Number(el.getAttribute('data-act'))].
-- Before any click: a dark box "TAB A 0"; a box "ADD 0"; a row of three boxes "Pump", "Fan", "Vent"; a second row
--   "Pump" (green text), "Fan" (grey), "Vent" (green text); "log: -"; "7 bound"; a box "ROTATE".
-- Click TAB A: it turns green and reads "TAB B"; click it again: dark "TAB A n" again. "7 bound" stays.
-- Click ADD: "ADD 1", "ADD 2"... and TAB A shows the same number.
-- Click a box of the first row: "log: 0 Pump / 7" (its place and name, then how many functions acts holds).
-- Click ROTATE (nothing visible changes), then the first box of the first row: "log: 0 Pump / 7" again - the
--   function keeps the item it was made with - and the row then reads "Fan", "Vent", "Pump".
-- Second row: "Pump" and "Vent" write "log: pick Pump / 7" and "log: pick Vent / 7"; "Fan" has no function and
--   does nothing.

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
  .big { height: 56px; margin-bottom: 8px; background: #232833; display: flex; align-items: center; justify-content: center; }
  .big.on { background: #2F855A; }
  .line { display: flex; margin-bottom: 8px; }
  .row { flex: 1; margin-right: 8px; height: 56px; background: #2B3A55; display: flex; align-items: center; justify-content: center; color: #8894A8; }
  .row.on { color: #68D391; }
  p { margin: 0 0 6px 0; font-size: 20px; }
</style>
</head>
<body>
  <div id="tabs"></div>
  <div id="panel"></div>
  <p id="log">log: -</p>
  <p id="count">-</p>
  <div id="rot" class="big">ROTATE</div>
  <script>
    let acts = [];
    const act = (fn) => { acts.push(fn); return ' data-act="' + (acts.length - 1) + '"'; };
    let n = 0, mode = 'a';
    const items = [{ name: 'Pump', pick: null }, { name: 'Fan', pick: null }, { name: 'Vent', pick: null }];
    items[0].pick = () => say('pick Pump');
    items[2].pick = () => say('pick Vent');
    function say(s) { document.getElementById('log').textContent = 'log: ' + s + ' / ' + acts.length; render(); }
    function values() { return { mode, n, bump: () => { n++; render(); } }; }
    function renderTabs(v) {
      return v.mode === 'a'
        ? '<div id="ta" class="big"' + act(() => { mode = 'b'; render(); }) + '>TAB A ' + v.n + '</div>'
        : '<div id="tb" class="big on"' + act(() => { mode = 'a'; render(); }) + '>TAB B</div>';
    }
    function render() {
      const v = values();
      acts = [];
      document.getElementById('tabs').innerHTML = renderTabs(v);
      document.getElementById('panel').innerHTML = '<div id="add" class="big"' + act(v.bump) + '>ADD ' + v.n + '</div>'
        + '<div class="line">' + items.map((g, i) => '<div id="r' + i + '" class="row"' + act(() => say(i + ' ' + g.name)) + '>' + g.name + '</div>').join('') + '</div>'
        + '<div class="line">' + items.map((g) => '<div class="row' + (g.pick ? ' on' : '') + '"' + (g.pick ? act(g.pick) : '') + '>' + g.name + '</div>').join('') + '</div>';
      const bound = document.querySelectorAll('[data-act]');
      for (let i = 0; i < bound.length; i++) {
        const el = bound[i], idx = Number(el.getAttribute('data-act'));
        el.addEventListener('click', () => acts[idx]());
      }
      document.getElementById('count').textContent = bound.length + ' bound';
    }
    document.getElementById('rot').addEventListener('click', () => { const first = items.shift(); if (items.length < 3) items.push(first); });
    render();
  </script>
</body>
</html>
]==]

ui:clear()
ui:element({
    id = "acts",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
})
ui:commit()

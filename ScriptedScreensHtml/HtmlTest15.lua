-- HtmlTest15.lua -- the constructs of the Apple-style Atmo mockup (AtmoUi/ios) that did not work. Push to the 3x3 console (586); design width 806.
--
--   theme      the script sets data-mode="dark" on the root 300 ms after load: every var(--token) below re-resolves
--              (panel, cards, label colours, the colour inside a sentence) - the capture must show the dark palette
--   calc       the gauge fill is height: calc(75% - 12px) of a 120px track: 78px
--   duct       two 52x5 stripe bars (repeating-linear-gradient 6px on / 6px off): the top one marches (ap-march), the bottom stands
--   gasrow     a gas row as in the mockup: grid 12px | 1fr | 132px | 240-300px, align-items center: the dot and the 8px bar
--              sit centred, not stretched to the row height
--   baseline   52px numeral and 20px unit share a baseline (align-items: baseline)
--   tiles      3-column grid with grid-auto-rows: 1fr: the short tile is as tall as the tall one
--   weights    Manrope 400 / 500 / 600 / 700 are four faces
--   shadow     card with the mockup's --shadow (a negative spread and a .5px ring)
--   tabular    111.1 over 888.8 with tabular-nums: same width
--   scroll     a 72px list of eight rows: the label shows scrollTop / scrollHeight as reported by the vector mod (ask 18)
--              and updates on the wheel (a scroll event); the edge fade follows (bottom fade at the top, both mid-way, top at the end)

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local page = [[
<html>
<head>
<meta name="viewport" content="width=806">
<style>
  :root { --bg:#f5f5f7; --card:#ffffff; --label:#000000; --label2:#55555a; --sep:#c6c6c8; --track:#e3e3e8; --fill:#eeeef1; --gauge:#5aa9ff; --green:#34c759; --shadow:0 24px 60px -26px rgba(0,0,0,.3),0 0 0 .5px rgba(0,0,0,.07); }
  [data-mode="dark"] { --bg:#1c1c1e; --card:#2c2c2e; --label:#ffffff; --label2:rgba(235,235,245,.62); --sep:rgba(84,84,88,.72); --track:rgba(120,120,128,.32); --fill:rgba(120,120,128,.28); --gauge:#0a84ff; --green:#30d158; --shadow:0 0 0 .5px rgba(255,255,255,.08),0 24px 60px -26px rgba(0,0,0,.9); }
  body { margin: 0; padding: 18px; background: var(--bg); color: var(--label); font-family: Manrope; font-size: 18px; line-height: 24px; }
  h2 { font-size: 14px; font-weight: 600; letter-spacing: .02em; color: var(--label2); margin: 10px 0 4px; text-transform: uppercase; }
  .card { background: var(--card); border-radius: 28px; padding: 12px 20px; box-shadow: var(--shadow); }
  .row { display: flex; gap: 16px; align-items: center; }
  .track { position: relative; width: 60px; height: 120px; border-radius: 16px; background: var(--fill); overflow: hidden; }
  .track div { position: absolute; left: 6px; right: 6px; bottom: 6px; height: calc(75% - 12px); border-radius: 14px; background: var(--gauge); }
  .duct { width: 52px; height: 5px; border-radius: 2.5px; background: repeating-linear-gradient(90deg, var(--green) 0 6px, var(--track) 6px 12px); }
  .march { animation: ap-march .9s linear infinite; }
  @keyframes ap-march { to { background-position: 12px 0 } }
  .gas { display: grid; grid-template-columns: 12px minmax(0,1fr) 132px minmax(240px,300px); align-items: center; gap: 18px; height: 55px; padding: 0 24px; background: var(--card); border-radius: 28px; }
  .dot { width: 12px; height: 12px; border-radius: 50%; background: var(--green); }
  .bar { position: relative; height: 8px; border-radius: 4px; background: var(--track); }
  .bar div { position: absolute; left: 0; top: 0; bottom: 0; width: 62%; border-radius: 4px; background: var(--green); }
  .num { font-size: 52px; line-height: 56px; font-weight: 600; letter-spacing: -.022em; font-variant-numeric: tabular-nums; }
  .unit { font-size: 20px; line-height: 25px; color: var(--label2); }
  .tiles { display: grid; grid-template-columns: repeat(3, minmax(0,1fr)); grid-auto-rows: 1fr; gap: 12px; width: 500px; }
  .tile { background: var(--card); border-radius: 28px; padding: 14px 20px; }
  .w4 { font-weight: 400 } .w5 { font-weight: 500 } .w6 { font-weight: 600 } .w7 { font-weight: 700 }
  .tab { font-size: 30px; line-height: 36px; font-weight: 600; font-variant-numeric: tabular-nums; }
  .list { width: 220px; height: 72px; overflow-y: auto; background: var(--card); border-radius: 14px; }
  .list div { height: 24px; padding: 0 12px; line-height: 24px; border-top: .5px solid var(--sep); }
</style>
</head>
<body>
  <h2>theme (dark after load), shadow, text colours</h2>
  <div class="card">Room pressure <span style="color: var(--label2)">set 101.3 kPa &middot; holding</span></div>

  <div class="row" style="margin-top: 10px">
    <div>
      <h2>calc gauge</h2>
      <div class="track"><div></div></div>
    </div>
    <div>
      <h2>duct bars</h2>
      <div class="duct march"></div>
      <div class="duct" style="margin-top: 10px"></div>
    </div>
    <div>
      <h2>baseline</h2>
      <div style="display: flex; align-items: baseline; gap: 8px"><span class="num">101.1</span><span class="unit">kPa</span></div>
    </div>
    <div>
      <h2>tabular</h2>
      <div class="tab">111.1</div><div class="tab">888.8</div>
    </div>
  </div>

  <h2>gas row</h2>
  <div class="gas"><span class="dot"></span><span style="display:flex;flex-direction:column"><span style="font-size:22px;font-weight:600;line-height:26px">O2</span><span style="font-size:14px;line-height:18px;color:var(--label2)">Target 21.0 % &middot; pump 12 %</span></span><span style="display:flex;align-items:baseline;justify-content:flex-end;gap:6px"><span class="tab">20.9</span><span style="font-size:19px;color:var(--label2)">%</span></span><span class="bar"><div></div></span></div>

  <h2>tiles (grid-auto-rows: 1fr)</h2>
  <div class="tiles"><div class="tile">Pressure<br>101.1 kPa<br>92.3 - 110.3 &middot; trip 140</div><div class="tile">O2</div><div class="tile">Temp<br>21.2</div></div>

  <h2>scroll report (wheel over the list)</h2>
  <div class="row"><div id="list" class="list"><div>row 1</div><div>row 2</div><div>row 3</div><div>row 4</div><div>row 5</div><div>row 6</div><div>row 7</div><div>row 8</div></div><span id="sc" class="unit">?</span></div>

  <h2>weights</h2>
  <div class="row"><span class="w4">Manrope 400</span><span class="w5">Manrope 500</span><span class="w6">Manrope 600</span><span class="w7">Manrope 700</span></div>
<script>
  setTimeout(() => { document.documentElement.setAttribute('data-mode', 'dark'); }, 300);
  const list = document.getElementById('list'), sc = document.getElementById('sc');
  const fade = () => {
    const top = list.scrollTop <= 2, end = list.scrollTop + list.clientHeight >= list.scrollHeight - 2;
    sc.textContent = 'top ' + Math.round(list.scrollTop) + ' of ' + Math.round(list.scrollHeight) + (top ? ' (at start)' : '') + (end ? ' (at end)' : '');
    list.style.maskImage = 'linear-gradient(' + (top ? '#000 0,' : 'rgba(0,0,0,.2) 0,#000 8%,') + (end ? '#000 100%)' : '#000 92%,rgba(0,0,0,.2) 100%)');
  };
  list.addEventListener('scroll', fade);
  setTimeout(fade, 350);
</script>
</body>
</html>
]]

ui:element({
    id = "web",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#FF00FF" },
})

ui:commit()

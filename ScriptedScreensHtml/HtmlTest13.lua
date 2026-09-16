-- HtmlTest13.lua -- Batch G: the agreed list after COVERAGE.md. Push to the 3x3 console (586); design width 640.
--
--   start      @starting-style: the amber card fades and slides in on load; "add" inserts a pill that scales in
--   backdrop   a modal <dialog> (open on load) dims the page behind it with its ::backdrop colour; "close" removes it
--   trig       widths from calc() with sin/cos/hypot/pow/round: four bars of 50, 87, 100 and 60 px
--   col-rule   two columns of text with a dashed amber column-rule between them
--   offset     three dots placed along one path("...") at 0%, 50% and 100%, the arrow turned along the tangent
--   motion     the green dot glides to the end of the path over 2 s after load (transition: offset-distance);
--              the amber arrow loops along it every 4 s (@keyframes on offset-distance, offset-rotate auto)
--   col        <colgroup><col width> gives the first table column 60% of the width
--   picture    <picture> with a <source media="(min-width: 600px)"> picks the wide image
--   uinvalid   the empty required input turns red only after it is touched (:user-invalid)
--   point      "which?" logs document.elementFromPoint(320, 40) into the label
--   after      ::after with counter(n) after the children: "items: 3" (the counters were incremented by the children)
--   pointer    press and hold the green button: it stays dark green through the surface rebuild the press causes

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local page = [[
<html>
<head>
<meta name="viewport" content="width=640">
<style>
  body { margin: 0; padding: 8px; background: #101418; color: #dde; font-family: Barlow; font-size: 13px; }
  h2 { font-size: 12px; color: #8ab; margin: 8px 0 3px; text-transform: uppercase; }
  .row { display: flex; gap: 8px; align-items: center; }
  .lbl { color: #789; font-size: 11px; }

  .card { background: #f5a623; color: #201; padding: 6px 10px; border-radius: 6px; transition: opacity .8s, transform .8s; }
  @starting-style { .card { opacity: 0; transform: translateY(20px); } }
  .pill { background: #3a7; color: #fff; padding: 3px 8px; border-radius: 10px; transition: transform .5s, opacity .5s; opacity: 1; transform: scale(1);
          @starting-style { transform: scale(0.2); opacity: 0; } }
  button { background: #345; color: #fff; border: 0; padding: 4px 8px; border-radius: 4px; }

  dialog { position: absolute; left: 400px; top: 20px; width: 180px; background: #eef; color: #123; border: 2px solid #88a; border-radius: 6px; padding: 8px; }
  dialog::backdrop { background: rgba(40, 0, 80, 0.5); }

  .bar { height: 10px; background: #4af; border-radius: 3px; }
  .b1 { width: calc(sin(30deg) * 100px); }
  .b2 { width: calc(cos(30deg) * 100px); }
  .b3 { width: calc(hypot(60px, 80px)); }
  .b4 { width: calc(pow(2, 3) * 7.5px); }

  .cols { column-count: 2; column-gap: 20px; column-rule: 2px dashed #f5a623; width: 400px; font-size: 11px; }

  .track { position: relative; width: 300px; height: 70px; border: 1px dashed #345; }
  .dot { position: absolute; width: 10px; height: 10px; border-radius: 5px; background: #4af; offset-path: path("M 20 50 C 100 -20, 200 120, 280 30"); }
  .arrow { position: absolute; width: 18px; height: 8px; background: #f5a623; clip-path: polygon(0 0, 100% 50%, 0 100%); offset-path: path("M 20 50 C 100 -20, 200 120, 280 30"); offset-distance: 50%; offset-rotate: auto; }

  .mover { position: absolute; width: 10px; height: 10px; border-radius: 5px; background: #3c7; offset-path: path("M 20 50 C 100 -20, 200 120, 280 30"); offset-distance: 0%; transition: offset-distance 2s ease-in-out; }
  @keyframes travel { from { offset-distance: 0%; } to { offset-distance: 100%; } }
  .orbit { position: absolute; width: 18px; height: 8px; background: #f5a623; clip-path: polygon(0 0, 100% 50%, 0 100%); offset-path: path("M 20 50 C 100 -20, 200 120, 280 30"); offset-rotate: auto; animation: travel 4s linear infinite; }

  table { border-collapse: collapse; width: 300px; } td { border: 1px solid #456; padding: 2px 4px; }

  input { background: #223; color: #eee; border: 2px solid #567; padding: 3px; width: 120px; }
  input:user-invalid { border-color: #e33; }
  input:user-valid { border-color: #3c3; }

  .list { counter-reset: n; }
  .list span { counter-increment: n; margin-right: 6px; }
  .list::after { content: "items: " counter(n); color: #f5a623; }

  .hold { background: #2a5; }
  .hold:active { background: #063; }
  .hold:hover { background: #3c7; }
</style>
</head>
<body>
  <h2>@starting-style</h2>
  <div class="row"><div class="card">fades and slides in on load</div><button id="add">add pill</button><span id="pills"></span></div>

  <h2>dialog ::backdrop</h2>
  <dialog id="dlg"><b>modal</b><br>the page is dimmed behind<br><button id="close">close</button></dialog>
  <div class="row"><button id="open">open</button><span class="lbl">a modal dialog, dimmed backdrop</span></div>

  <h2>trigonometric calc()</h2>
  <div class="bar b1"></div><div class="bar b2"></div><div class="bar b3"></div><div class="bar b4"></div>
  <div class="lbl">50, 87, 100, 60 px</div>

  <h2>column-rule</h2>
  <div class="cols">Two columns of text with a dashed amber rule down the middle of the gap. The rule is drawn between the columns, in the centre of the gap, and reaches the height of the columns.</div>

  <h2>offset-path</h2>
  <div class="track">
    <div class="dot" style="offset-distance: 0%"></div><div class="dot" style="offset-distance: 100%"></div>
    <div class="arrow"></div>
  </div>
  <div class="track"><div id="mover" class="mover"></div><div class="orbit"></div></div>

  <h2>&lt;col&gt; widths</h2>
  <table><colgroup><col width="60%"><col></colgroup><tr><td>sixty percent</td><td>rest</td></tr></table>

  <h2>picture / source</h2>
  <div class="row"><picture>
    <source media="(min-width: 600px)" srcset="https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png">
    <img src="https://raw.githubusercontent.com/Gruffuss/scripted-screens-html/main/ScriptedScreensHtml/About/thumb.png" width="120" height="40">
  </picture><span class="lbl">the vector mod thumbnail (source matched), not this mod's</span></div>

  <h2>:user-invalid</h2>
  <div class="row"><input id="name" required placeholder="required"><span class="lbl">red after typing then clearing; green with text</span></div>

  <h2>elementFromPoint</h2>
  <div class="row"><button id="which">which?</button><span id="hit" class="lbl">?</span></div>

  <h2>::after counters</h2>
  <div class="list"><span>a</span><span>b</span><span>c</span></div>

  <h2>pointer across rebuild</h2>
  <div class="row"><button class="hold">hold me</button><span class="lbl">dark green while held</span></div>

<script>
  document.getElementById('dlg').showModal();
  setTimeout(() => { document.getElementById('mover').style.offsetDistance = '100%'; }, 800);
  document.getElementById('close').onclick = () => document.getElementById('dlg').close();
  document.getElementById('open').onclick = () => document.getElementById('dlg').showModal();
  document.getElementById('add').onclick = () => { const p = document.createElement('span'); p.className = 'pill'; p.textContent = 'new'; document.getElementById('pills').appendChild(p); };
  document.getElementById('which').onclick = () => { const e = document.elementFromPoint(320, 40); document.getElementById('hit').textContent = e ? (e.tagName + '.' + e.className) : 'none'; };
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

-- HtmlTest14.lua -- Batch H: every "build" row of COVERAGE.md. Push to a 3x3 console; design width 640.
--
--   units      three bars: 100Q (94px), 10cap (91px at 13px), 8ic (104px)
--   colours    swatches: hwb, lab, lch, oklab, oklch, rgb(from ...) at half alpha, hsl(from red) hue +120 (green),
--              light-dark with color-scheme: dark (black swatch), env() fallback margin (30px indent)
--   clip       xywh() and rect() clip forms; clip-rule: evenodd on a star (hole in the middle)
--   corners    corner-shape with four values (bevel scoop notch square), one per-corner override
--   grid       grid: auto-flow 24px / 1fr 1fr with four items
--   details    details::details-content gets a dark blue body; :scope colours the root text
--   popover    a popover shown at load by script, toggled by its button
--   text       text-wrap-mode: nowrap; white-space-collapse: preserve; initial-letter drop cap; baseline-shift super;
--              text-emphasis filled circles over a word
--   bg         background-origin: content-box (picture inset by border + padding); image-set() picks the first
--   base       <base href> resolves the bare "thumb.png" to the vector mod's thumbnail
--   offset     offset shorthand with ray(45deg) moves the blue dot up-right; offset-anchor: left top on the green one
--   columns    column-width 120px over 400px (3 columns), the heading spans all
--   overflow   overflow-clip-margin: 10px lets the child show 10px past the box
--   mask       mask-size 50% + mask-position right: the gradient fades only in the right half
--   svg        arrowhead markers, paint-order stroke (dark stroke under the amber fill), pathLength 100 with dash 50
--              (half dashed), crispEdges, non-scaling stroke (thin) beside a scaled one (thick), r from CSS,
--              transform-box: fill-box rotation about the rect's own centre
--   discrete   the pink card fades out then disappears (transition: display allow-discrete) 1 s after load
--   map        click the left / right half of the mapped image: the label names the area
--   datalist   focus the input: a list of fruits opens under it; click one
--   dom        DOMParser counts the items of a parsed string; XMLSerializer prints an element

local PART = 1  -- 1: the top half of the page, 2: the bottom half (the console shows 640px of it)

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
<base href="https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/">
<style>
  body { margin: 0; padding: 6px; background: #101418; color: #dde; font-family: Barlow; font-size: 12px; color-scheme: dark; }
  :scope { color: #dde; }
  h2 { font-size: 11px; color: #8ab; margin: 6px 0 2px; text-transform: uppercase; }
  .row { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
  .lbl { color: #789; font-size: 10px; }
  .bar { height: 8px; background: #4af; margin-bottom: 2px; }
  .uq { width: 100Q; } .ucap { width: 10cap; } .uic { width: 8ic; }
  .sw { width: 22px; height: 16px; border-radius: 3px; }
  .env { margin-left: env(not-a-thing, 30px); padding-left: env(safe-area-inset-left); }
  .clipbox { width: 60px; height: 40px; background: #3a7; }
  .cx { clip-path: xywh(10px 10px 40px 20px round 4px); }
  .cr { clip-path: rect(5px 55px 35px 5px); }
  .star { width: 40px; height: 40px; background: #f5a623; clip-rule: evenodd; clip-path: polygon(50% 0%, 61% 35%, 98% 35%, 68% 57%, 79% 91%, 50% 70%, 21% 91%, 32% 57%, 2% 35%, 39% 35%); }
  .corner { width: 50px; height: 36px; background: #4af; border-radius: 12px; }
  .c1 { corner-shape: bevel scoop notch square; }
  .c2 { corner-top-left-shape: notch; }
  .grid { display: grid; grid: auto-flow 24px / 1fr 1fr; gap: 4px; width: 200px; }
  .grid div { background: #345; text-align: center; }
  details::details-content { background: #234; padding: 4px; }
  [popover] { position: absolute; left: 420px; top: 40px; width: 150px; background: #eef; color: #123; padding: 6px; border: 1px solid #88a; border-radius: 4px; }
  .nowrap { width: 90px; text-wrap-mode: nowrap; overflow: hidden; background: #223; }
  .pre { white-space-collapse: preserve; }
  .drop::first-letter { initial-letter: 2; color: #f5a623; }
  .emph { text-emphasis: filled circle #f5a623; }
  .bgo { width: 90px; height: 50px; border: 4px solid #567; padding: 6px; background: #223 url(thumb.png) center / contain no-repeat; background-origin: content-box; }
  .iset { width: 90px; height: 50px; background-image: image-set("thumb.png" 1x, "missing.png" 2x); background-size: contain; background-repeat: no-repeat; }
  .track { position: relative; width: 300px; height: 60px; border: 1px dashed #345; }
  .dotb { position: absolute; left: 100px; top: 25px; width: 10px; height: 10px; border-radius: 5px; background: #4af; offset: ray(45deg closest-side) 40px; }
  .dotg { position: absolute; width: 10px; height: 10px; background: #3c7; offset-path: path("M 20 45 L 280 45"); offset-distance: 30%; offset-anchor: left top; }
  .cols { column-width: 120px; column-gap: 20px; width: 400px; column-rule: 1px solid #345; font-size: 10px; }
  .cols p { margin: 0 0 2px; } .cols h4 { column-span: all; margin: 2px 0; color: #f5a623; font-size: 11px; }
  .ocm { width: 60px; height: 24px; background: #345; overflow: clip; overflow-clip-margin: 10px; }
  .ocm div { width: 90px; height: 24px; background: #4af; opacity: .6; }
  .mask { width: 120px; height: 30px; background: #f5a623; mask-image: linear-gradient(to right, black, transparent); mask-size: 50% 100%; mask-position: right; }
  .fade { background: #e6a; color: #201; padding: 4px 8px; border-radius: 4px; transition: opacity .6s, display .6s allow-discrete; }
  input { background: #223; color: #eee; border: 1px solid #567; padding: 2px; width: 100px; }
  svg .big { r: 12; }
  svg .rot { transform: rotate(20deg); transform-box: fill-box; transform-origin: center; }
</style>
</head>
<body>
<div id="part1">
  <h2>units</h2>
  <div class="bar uq"></div><div class="bar ucap"></div><div class="bar uic"></div>

  <h2>colour spaces, relative colours, light-dark, env</h2>
  <div class="row">
    <div class="sw" style="background: hwb(120 10% 10%)"></div><div class="sw" style="background: lab(60 40 -40)"></div>
    <div class="sw" style="background: lch(60 60 250)"></div><div class="sw" style="background: oklab(0.7 0.1 0.1)"></div>
    <div class="sw" style="background: oklch(0.7 0.15 30)"></div><div class="sw" style="background: rgb(from #f5a623 r g b / 50%)"></div>
    <div class="sw" style="background: hsl(from red calc(h + 120) s l)"></div><div class="sw" style="background: light-dark(#fff, #000); border: 1px solid #567"></div>
    <span class="lbl env">env(): indented 30px</span>
  </div>

  <h2>clip forms, corner shapes, grid shorthand</h2>
  <div class="row"><div class="clipbox cx"></div><div class="clipbox cr"></div><div class="star"></div>
    <div class="corner c1"></div><div class="corner c2"></div>
    <div class="grid"><div>1</div><div>2</div><div>3</div><div>4</div></div></div>

  <h2>details-content, popover, text</h2>
  <div class="row">
    <details open style="width:150px"><summary>summary</summary>the body is dark blue</details>
    <button popovertarget="pop">popover</button><div id="pop" popover>a popover, top layer</div>
    <div class="nowrap">this line does not wrap at all</div>
    <span class="pre">a   b   c</span>
    <span class="emph">emphasis</span>
    <span>x<span style="baseline-shift: super">2</span> + y<span style="baseline-shift: sub">i</span></span>
  </div>
  <p class="drop" style="width: 300px; margin: 2px 0">Drop cap two lines high from initial-letter, the rest of the paragraph wraps beside it as usual.</p>

  <h2>background-origin, image-set, base href</h2>
  <div class="row"><div class="bgo"></div><div class="iset"></div><img src="thumb.png" width="90" height="50"></div>

  <h2>offset: ray(), offset-anchor</h2>
  <div class="track"><div class="dotb"></div><div class="dotg"></div></div>

  <h2>column-width, column-span</h2>
  <div class="cols"><p>one</p><p>two</p><h4>spans all columns</h4><p>three</p><p>four</p><p>five</p><p>six</p></div>

  <h2>overflow-clip-margin, mask-size/position</h2>
  <div class="row"><div class="ocm"><div></div></div><div class="mask"></div></div>

</div>
<div id="part2">
  <h2>svg</h2>
  <div class="row">
    <svg width="200" height="60" viewBox="0 0 200 60">
      <defs><marker id="arrow" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="6" markerHeight="6" orient="auto"><path d="M 0 0 L 10 5 L 0 10 z" fill="#f5a623"/></marker></defs>
      <polyline points="10,50 60,15 110,50 160,15" fill="none" stroke="#4af" stroke-width="2" marker-start="url(#arrow)" marker-mid="url(#arrow)" marker-end="url(#arrow)"/>
      <rect x="170" y="10" width="24" height="24" fill="#f5a623" stroke="#123" stroke-width="8" paint-order="stroke"/>
      <line x1="10" y1="56" x2="110" y2="56" stroke="#3c7" stroke-width="3" pathLength="100" stroke-dasharray="50 50"/>
      <circle class="big" cx="140" cy="48" r="3" fill="#e6a" shape-rendering="crispEdges"/>
    </svg>
    <svg width="100" height="60" viewBox="0 0 25 15">
      <line x1="2" y1="3" x2="23" y2="3" stroke="#4af" stroke-width="2"/>
      <line x1="2" y1="8" x2="23" y2="8" stroke="#4af" stroke-width="2" vector-effect="non-scaling-stroke"/>
      <rect class="rot" x="10" y="10" width="5" height="4" fill="#3c7"/>
    </svg>
  </div>

  <h2>discrete display transition, image map, datalist, DOMParser</h2>
  <div class="row"><div id="fade" class="fade">fades, then gone</div>
    <img src="thumb.png" width="120" height="40" usemap="#m"><map name="m"><area shape="rect" coords="0,0,60,40" id="left" href="#"><area shape="rect" coords="60,0,120,40" id="right" href="#"></map><span id="hit" class="lbl">click a half</span>
    <input id="fruit" list="fruits" placeholder="fruit"><datalist id="fruits"><option value="Apple"><option value="Banana"><option value="Cherry"></datalist>
    <span id="dom" class="lbl">?</span></div>
</div>

<script>
  document.getElementById('pop').showPopover();
  setTimeout(() => { const f = document.getElementById('fade'); f.style.opacity = '0'; f.style.display = 'none'; }, 1000);
  document.getElementById('left').addEventListener('click', () => { document.getElementById('hit').textContent = 'left half'; });
  document.getElementById('right').addEventListener('click', () => { document.getElementById('hit').textContent = 'right half'; });
  const doc = new DOMParser().parseFromString('<ul><li class="a">1</li><li>2</li><li class="a">3</li></ul>', 'text/html');
  const s = new XMLSerializer().serializeToString(doc.querySelector('li.a'));
  document.getElementById('dom').textContent = doc.querySelectorAll('li').length + ' items, ' + doc.querySelectorAll('li.a').length + ' .a, ' + s;
</script>
</body>
</html>
]]

page = page:gsub("</style>", (PART == 2 and "#part1 { display: none }" or "#part2 { display: none }") .. "</style>", 1)

ui:element({
    id = "web",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#FF00FF" },
})

ui:commit()

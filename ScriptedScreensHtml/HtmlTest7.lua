-- HtmlTest7.lua -- Batch C: paint. Push to a 3x3 console; design width 640.
-- Several rows need the vector additions in FOR-VECTOR-SESSION.md and show nothing (or
-- the fallback) until they land; each row says which requirement it needs.
--
--   images    <img> as an IMG node with object-fit and radius (req 9); background-image (req 9)
--   gradients conic (req 11), repeating linear (emitter only)
--   borders   double, inset, outset, groove (emitter only)
--   filters   drop-shadow (emitter only); grayscale/hue-rotate on a group (req 12)
--   clips     clip-path inset/circle/polygon; the star is concave (req 8)
--   mask      mask-image fading a strip to the right (req 10)
--   transform skew() and matrix() (req 13)
--   text      vertical writing-mode (emitter only), justify (req 5), vertical-align sub/super
--   shadows   inset box-shadow (req 2), two text-shadows (req 4)
--   colour    a background-color transition on hover (req 1)
--   layout    float right, display: contents, column-count, aspect-ratio, z-index across parents

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
  :root { --ink: #E4F1F7; --dim: #7A93A6; --accent: #38BDF8; }
  body { background: #0B1622; color: var(--ink); font-family: Barlow; padding: 10px; font-size: 12px; }
  h2 { font-size: 11px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; margin: 6px 0 2px 0; }
  .row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
  .sw { width: 70px; height: 40px; border-radius: 6px; }
  .lbl { font-size: 10px; color: var(--dim); width: 64px; }

  img.pic { width: 96px; height: 54px; border-radius: 8px; object-fit: cover; }
  .bgimg { width: 96px; height: 54px; border-radius: 8px; background: #172033 url(https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png) center / contain no-repeat; }

  .conic { background: conic-gradient(from 0deg, #B5352C, #E2A94E, #2E8B6E, #38BDF8, #B5352C); border-radius: 50%; width: 40px; height: 40px; }
  .rep { background: repeating-linear-gradient(90deg, #24314A 0%, #24314A 10%, #38BDF8 10%, #38BDF8 20%); }

  .b1 { border: 6px double var(--accent); } .b2 { border: 4px inset #7A93A6; } .b3 { border: 4px outset #7A93A6; } .b4 { border: 4px groove #7A93A6; }

  .fshadow { background: #2E8B6E; filter: drop-shadow(4px 4px 4px #000000); }
  .fgray { filter: grayscale(1); } .fhue { filter: hue-rotate(120deg); }
  .clip1 { background: #E2A94E; clip-path: inset(6px 10px round 6px); }
  .clip2 { background: #38BDF8; clip-path: circle(45%); }
  .clip3 { background: #B5352C; clip-path: polygon(50% 0%, 61% 35%, 98% 35%, 68% 57%, 79% 91%, 50% 70%, 21% 91%, 32% 57%, 2% 35%, 39% 35%); }
  .mask { width: 200px; height: 20px; background: #38BDF8; mask-image: linear-gradient(to right, black 40%, transparent 100%); }

  .skew { background: #24314A; transform: skew(-15deg); padding: 4px 10px; }
  .mtx { background: #24314A; transform: matrix(1, 0.2, 0, 1, 0, 0); padding: 4px 10px; }

  .vert { writing-mode: vertical-rl; height: 90px; color: var(--accent); }
  .just { width: 260px; text-align: justify; font-size: 11px; }
  .ish { background: #172033; box-shadow: inset 0 3px 8px #000000; }
  .tsh2 { font-size: 16px; text-shadow: 2px 2px 0 #B5352C, -2px -2px 0 #38BDF8; }
  .hov { padding: 4px 10px; background: #24314A; border-radius: 4px; transition: background-color 0.4s ease; }
  .hov:hover { background: #2E8B6E; }

  .flt { width: 300px; background: #172033; padding: 4px; }
  .flt .r { float: right; background: #B5352C; padding: 2px 6px; }
  .contents { display: contents; }
  .cols { column-count: 3; column-gap: 12px; width: 320px; background: #172033; padding: 4px; }
  .ar { width: 60px; aspect-ratio: 3 / 2; background: #2E8B6E; }
  .stack { position: relative; width: 120px; height: 40px; background: #172033; }
  .stack .a { position: absolute; left: 10px; top: 6px; width: 60px; height: 28px; background: #B5352C; z-index: 5; }
  .sib { width: 80px; height: 30px; background: #E2A94E; margin-left: -40px; }
</style>
</head>
<body>
  <h2>images (req 9), gradients</h2>
  <div class="row">
    <img class="pic" src="https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png">
    <div class="bgimg"></div>
    <div class="conic"></div><div class="lbl">conic (req 11)</div>
    <div class="sw rep"></div><div class="lbl">repeating</div>
  </div>

  <h2>borders, filters, clips, mask</h2>
  <div class="row">
    <div class="sw b1"></div><div class="sw b2"></div><div class="sw b3"></div><div class="sw b4"></div>
    <div class="sw fshadow"></div><div class="sw fgray" style="background: #E2A94E"></div><div class="sw fhue" style="background: #E2A94E"></div>
  </div>
  <div class="row" style="margin-top: 6px">
    <div class="sw clip1"></div><div class="sw clip2"></div><div class="sw clip3"></div><div class="lbl">star: req 8</div>
    <div class="mask"></div><div class="lbl">mask: req 10</div>
  </div>

  <h2>transform (req 13), text, shadows, colour transition (req 1)</h2>
  <div class="row">
    <div class="skew">skew(-15deg)</div><div class="mtx">matrix(1,0.2,0,1,0,0)</div>
    <div class="vert">vertical-rl</div>
    <div class="just">justified text fills both edges of its box when the line wraps around and around and around</div>
    <div class="sw ish"></div><div class="lbl">inset shadow: req 2</div>
    <div class="tsh2">two shadows</div>
    <div>x<span style="vertical-align: super">2</span> H<span style="vertical-align: sub">2</span>O</div>
    <div class="hov">hover: green fades in</div>
  </div>

  <h2>layout</h2>
  <div class="row">
    <div class="flt"><span class="r">float right</span>text beside the float, in a wrapping row</div>
    <div class="contents"><div class="sw" style="background: #2E8B6E"></div><div class="sw" style="background: #38BDF8"></div></div>
  </div>
  <div class="row" style="margin-top: 6px">
    <div class="cols"><div>one</div><div>two</div><div>three</div><div>four</div><div>five</div><div>six</div></div>
    <div class="ar"></div><div class="lbl">3:2</div>
    <div class="stack"><div class="a">z 5</div></div><div class="sib"></div><div class="lbl">red over amber</div>
  </div>
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

-- HtmlTest9.lua -- Batch F1: SVG as a browser reads it, and tabular numbers. Push to the
-- 3x3 console (586); design width 640. Each row names what it checks.
--
--   text      <text>/<tspan> with text-anchor, dominant-baseline, font-size in viewBox units
--   css       shapes styled by stylesheet rules (class, tag, :nth-child) and inline style
--   transform translate/rotate/scale/skew/matrix, nested through <g>, inherited fill/stroke
--   use       <use href> of a shape and of a <symbol> with its own viewBox
--   clip      <clipPath> on a shape and on a group (vector req 8 for the concave one)
--   dash      stroke-dasharray/dashoffset/miterlimit, fill-rule evenodd
--   image     <image href> inside the svg (vector req 9)
--   numbers   font-variant-numeric: tabular-nums (digits line up in the two columns)

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
  .lbl { font-size: 10px; color: var(--dim); width: 64px; }
  svg { background: #101C2A; border-radius: 4px; }

  /* css-styled svg */
  .bar { fill: #2E8B6E; }
  .bar:nth-child(2) { fill: #E2A94E; }
  .bar:nth-child(3) { fill: #B5352C; }
  svg.styled circle { fill: none; stroke: var(--accent); stroke-width: 3; }
  .styled text { fill: var(--ink); font-size: 12px; font-family: Barlow; }

  .tab { font-variant-numeric: tabular-nums; }
  .num { display: flex; flex-direction: column; width: 90px; font-size: 14px; }
  .num div { text-align: right; }
</style>
</head>
<body>
  <h2>text</h2>
  <div class="row">
    <svg width="240" height="60" viewBox="0 0 240 60">
      <text x="6" y="20" font-size="14" fill="#E4F1F7">start <tspan fill="#38BDF8" font-weight="bold">bold span</tspan></text>
      <text x="120" y="40" font-size="12" fill="#7A93A6" text-anchor="middle">middle anchored</text>
      <text x="234" y="56" font-size="10" fill="#E2A94E" text-anchor="end">end anchored</text>
      <line x1="120" y1="28" x2="120" y2="44" stroke="#B5352C" />
    </svg>
    <svg width="120" height="60" viewBox="0 0 120 60">
      <line x1="0" y1="30" x2="120" y2="30" stroke="#24314A" />
      <text x="4" y="30" font-size="12" fill="#E4F1F7" dominant-baseline="middle">on the line</text>
      <text x="70" y="30" font-size="12" fill="#38BDF8" dominant-baseline="hanging">below</text>
    </svg>
  </div>

  <h2>css-styled shapes</h2>
  <div class="row">
    <svg class="styled" width="160" height="60" viewBox="0 0 160 60">
      <rect class="bar" x="10" y="20" width="30" height="30" />
      <rect class="bar" x="50" y="10" width="30" height="40" />
      <rect class="bar" x="90" y="30" width="30" height="20" />
      <circle cx="140" cy="30" r="14" />
      <text x="10" y="14">styled by css</text>
    </svg>
    <div class="lbl">green amber red, blue ring</div>
  </div>

  <h2>transform</h2>
  <div class="row">
    <svg width="220" height="70" viewBox="0 0 220 70">
      <g fill="#2E8B6E" stroke="#E4F1F7" stroke-width="1">
        <rect x="10" y="20" width="30" height="30" />
        <rect x="10" y="20" width="30" height="30" transform="translate(50 0) rotate(20 25 35)" fill="#E2A94E" />
        <g transform="translate(110 20) scale(1.5 1)">
          <rect x="0" y="0" width="30" height="30" fill="#38BDF8" />
        </g>
        <rect x="170" y="20" width="30" height="30" transform="skewX(-15)" fill="#B5352C" />
      </g>
    </svg>
    <div class="lbl">green, amber tilted, blue wide, red skewed</div>
  </div>

  <h2>use and symbol</h2>
  <div class="row">
    <svg width="220" height="60" viewBox="0 0 220 60">
      <defs>
        <circle id="dot" r="8" fill="#38BDF8" />
        <symbol id="tri" viewBox="0 0 10 10"><polygon points="5,0 10,10 0,10" fill="#E2A94E" /></symbol>
      </defs>
      <use href="#dot" x="20" y="30" />
      <use href="#dot" x="50" y="30" fill="#B5352C" />
      <use href="#tri" x="80" y="10" width="40" height="40" />
      <use href="#tri" x="140" y="20" width="20" height="20" />
    </svg>
    <div class="lbl">two dots, two triangles</div>
  </div>

  <h2>clip, dash, fill-rule</h2>
  <div class="row">
    <svg width="220" height="60" viewBox="0 0 220 60">
      <defs>
        <clipPath id="win"><rect x="10" y="10" width="40" height="40" rx="8" /></clipPath>
        <clipPath id="star"><polygon points="90,5 97,25 118,25 101,38 107,58 90,46 73,58 79,38 62,25 83,25" /></clipPath>
      </defs>
      <circle cx="10" cy="10" r="40" fill="#2E8B6E" clip-path="url(#win)" />
      <g clip-path="url(#star)"><rect x="60" y="0" width="60" height="60" fill="#E2A94E" /></g>
      <rect x="130" y="10" width="40" height="40" fill="none" stroke="#38BDF8" stroke-width="2" stroke-dasharray="6 3" stroke-dashoffset="2" />
      <path d="M180 10 h30 v40 h-30 z M188 18 h14 v24 h-14 z" fill="#B5352C" fill-rule="evenodd" />
    </svg>
    <div class="lbl">quarter disc, star, dashed box, hollow box</div>
  </div>

  <h2>image</h2>
  <div class="row">
    <svg width="160" height="60" viewBox="0 0 160 60">
      <image href="https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png" x="4" y="4" width="96" height="52" />
      <text x="108" y="34" font-size="10" fill="#7A93A6">image</text>
    </svg>
  </div>

  <h2>tabular numbers</h2>
  <div class="row">
    <div class="num"><div>1111.11</div><div>8888.88</div><div>1234.56</div></div>
    <div class="num tab"><div>1111.11</div><div>8888.88</div><div>1234.56</div></div>
    <div class="lbl">right column: digits align</div>
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

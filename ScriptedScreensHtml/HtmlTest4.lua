-- HtmlTest4.lua -- Batch A: cascade and value correctness. Push to a 3x3 console;
-- design width 640. Each row names what it proves.
--
--   sizing    content-box (the CSS default) vs border-box: same width, different boxes
--   colours   hsl(), modern rgb(), named colours, currentColor on a border
--   maths     min(), max(), clamp(), vmin/ch units
--   nesting   CSS nesting with & and a nested @media
--   types     :nth-of-type striping, :has(), :is()
--   hover     :hover / :active on the cards (move the pointer over them), :focus on a button
--   faces     numeric weights to real Barlow faces, condensed, monospace, an @font-face alias
--   motion    steps() and cubic-bezier() easing, fill-mode none, a paused animation
--   inherit   `inherit` on a border, `initial` dropping a colour

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
  @font-face { font-family: "Signage"; src: url(Barlow-Black.ttf) format("truetype"); }

  :root { --ink: hsl(200 40% 93%); --dim: #7A93A6; --accent: rgb(56 189 248 / 100%); }
  body { background: #0B1622; color: var(--ink); font-family: Barlow; padding: 10px; font-size: 14px; }
  h2 { font-size: 13px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; margin: 10px 0 4px 0; }
  .row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
  .tag { font-size: 11px; color: var(--dim); width: 80px; }

  .cb, .bb { width: 120px; height: 20px; padding: 6px; border: 2px solid var(--accent); font-size: 11px; line-height: 20px; }
  .cb { box-sizing: content-box; }
  .bb { box-sizing: border-box; }

  .sw { width: 28px; height: 20px; border-radius: 4px; }
  .cur { color: tomato; border: 3px solid currentColor; padding: 2px 6px; font-size: 12px; }

  .m1 { height: 16px; background: #2E8B6E; width: min(300px, 40%); }
  .m2 { height: 16px; background: #E2A94E; width: max(60px, 10vmin); }
  .m3 { height: 16px; background: #B5352C; width: clamp(80px, 12ch, 200px); }

  .card { padding: 6px 10px; border-radius: 6px; background: #172033; width: 140px; font-size: 12px;
    .title { color: var(--accent); font-size: 13px; }
    &:hover { background: #24314A; outline: 1px solid var(--accent); }
    &:active { background: #2E8B6E; }
    > small { color: var(--dim); }
    @media (min-width: 600px) { border-left: 3px solid var(--accent); }
  }
  ul.types { list-style: none; padding: 0; margin: 0; }
  ul.types li { font-size: 12px; padding: 2px 6px; }
  ul.types li:nth-of-type(odd) { background: #172033; }
  ul.types li:has(b) { color: #E2A94E; }
  ul.types :is(b, i) { color: #2E8B6E; }
  button { font-size: 12px; padding: 4px 10px; background: #24314A; color: var(--ink); border-radius: 4px; }
  button:focus { outline: 2px solid var(--accent); }
  button:hover { background: #2E8B6E; }

  .w1 { font-weight: 200; } .w2 { font-weight: 500; } .w3 { font-weight: 800; } .w4 { font-weight: bold; }
  .cond { font-stretch: condensed; }
  .mono { font-family: monospace; }
  .sig { font-family: Signage; font-size: 16px; }

  @keyframes slide { from { transform: translateX(0px); } to { transform: translateX(120px); } }
  .mv { width: 16px; height: 16px; border-radius: 3px; background: var(--accent); }
  .steps { animation: slide 2s steps(4, end) infinite; }
  .bez { animation: slide 2s cubic-bezier(0.2, 0.8, 0.2, 1) infinite alternate; }
  .once { animation: slide 1.5s ease-out 1; }
  .held { animation: slide 2s linear infinite paused; }

  .inh { border: 2px solid #2E8B6E; padding: 4px; font-size: 12px; }
  .inh div { border: inherit; padding: 2px 6px; }
  .init { color: #E2A94E; }
  .init span { color: initial; }
</style>
</head>
<body>
  <h2>sizing</h2>
  <div class="row"><div class="tag">content-box</div><div class="cb">width 120 + padding + border</div></div>
  <div class="row"><div class="tag">border-box</div><div class="bb">width 120 in all</div></div>

  <h2>colours</h2>
  <div class="row">
    <div class="sw" style="background: hsl(340 80% 55%)"></div>
    <div class="sw" style="background: rgb(46 139 110 / 80%)"></div>
    <div class="sw" style="background: rebeccapurple"></div>
    <div class="sw" style="background: goldenrod"></div>
    <div class="sw" style="background: hsla(200, 90%, 60%, 0.5)"></div>
    <div class="cur">currentColor border</div>
  </div>

  <h2>maths</h2>
  <div class="m1"></div><div class="m2" style="margin-top: 3px"></div><div class="m3" style="margin-top: 3px"></div>

  <h2>nesting, hover, types</h2>
  <div class="row">
    <div class="card"><div class="title">nested card</div>hover me<br><small>nested &gt; small</small></div>
    <div class="card"><div class="title">second card</div>press me<br><small>:active turns green</small></div>
    <ul class="types"><li>plain one</li><li>plain two</li><li>with <b>bold</b></li><li>with <i>italic</i></li></ul>
    <button id="f1">focus me</button><button id="f2">or me</button>
  </div>

  <h2>faces</h2>
  <div class="row">
    <span class="w1">200 ExtraLight</span><span class="w2">500 Medium</span><span class="w3">800 ExtraBold</span><span class="w4">bold face</span>
    <span class="cond">condensed</span><span class="mono">monospace 0123</span><span class="sig">@font-face Signage</span>
  </div>

  <h2>motion</h2>
  <div class="row"><div class="tag">steps(4)</div><div class="mv steps"></div></div>
  <div class="row"><div class="tag">cubic-bezier</div><div class="mv bez"></div></div>
  <div class="row"><div class="tag">once, fill none</div><div class="mv once"></div></div>
  <div class="row"><div class="tag">paused</div><div class="mv held"></div></div>

  <h2>inherit</h2>
  <div class="inh">outer border<div>inherited border</div></div>
  <div class="init" style="margin-top: 6px">orange, <span>then initial</span></div>
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

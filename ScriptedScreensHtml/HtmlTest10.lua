-- HtmlTest10.lua -- Batch F2: the cascade. Push to the 3x3 console (586); design width 640.
-- Each row names what it checks; PASS is what a browser would show.
--
--   logical    margin-inline, padding-block, inset-inline-start, inline-size, border-start-end-radius
--   borders    border-top-style: dashed with solid sides; border-style: none with a width
--   decoration text-decoration-color/style/thickness, text-underline-offset, overline, wavy, double
--   colour     color-mix(in srgb, ...) two ways
--   at-rules   @layer (in order), @scope (.card), @container against the design size, @property initial-value, @import (fetched)
--   pseudo-el  ::marker colour on a list, ::placeholder colour on a field
--   pseudo-cl  :required/:invalid/:placeholder-shown/:in-range on inputs (type in them), :open on details, :any-link, :lang(), :dir()

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local page = [[
<html lang="en-GB">
<head>
<meta name="viewport" content="width=640">
<style>
  @import url(https://raw.githubusercontent.com/Gruffuss/scripted-screens-html/untested/ScriptedScreensHtml/tests/external.css);
  @property --pad { syntax: "<length>"; inherits: false; initial-value: 6px; }
  :root { --ink: #E4F1F7; --dim: #7A93A6; --accent: #38BDF8; }
  body { background: #0B1622; color: var(--ink); font-family: Barlow; padding: 10px; font-size: 12px; }
  h2 { font-size: 11px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; margin: 6px 0 2px 0; }
  .row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
  .sw { width: 60px; height: 30px; background: #24314A; }
  .lbl { font-size: 10px; color: var(--dim); width: 70px; }

  .log1 { margin-inline: 20px 0; padding-block: 4px; background: #2E8B6E; inline-size: 80px; border-start-end-radius: 12px; }
  .log2 { position: relative; inset-inline-start: 30px; background: #E2A94E; block-size: 20px; inline-size: 40px; }

  .bt { border: 3px solid var(--accent); border-top-style: dashed; border-bottom-style: dotted; }
  .bn { border: 3px solid var(--accent); border-style: none; }
  .bl { border-inline: 3px solid #B5352C; }

  .d1 { text-decoration: underline; text-decoration-color: #B5352C; text-decoration-thickness: 3px; text-underline-offset: 5px; }
  .d2 { text-decoration: underline wavy #38BDF8; }
  .d3 { text-decoration: overline double #E2A94E; }
  .d4 { text-decoration-line: line-through; text-decoration-color: #B5352C; }

  .mix1 { background: color-mix(in srgb, #B5352C, #38BDF8); }
  .mix2 { background: color-mix(in srgb, #B5352C 20%, #38BDF8); }

  @layer base { .layered { background: #B5352C; } }
  @layer over { .layered { background: #2E8B6E; } }
  @scope (.card) { .sw { background: #38BDF8; } :scope { padding: var(--pad); background: #172033; } }
  @container (min-width: 500px) { .cq { background: #2E8B6E; } }
  @container (min-width: 5000px) { .cq { background: #B5352C; } }

  ul { margin: 0; padding-left: 18px; }
  li::marker { color: #E2A94E; }
  input { width: 110px; height: 22px; background: #172033; color: var(--ink); border: 2px solid #24314A; }
  input::placeholder { color: #E2A94E; }
  input:required { border-color: #E2A94E; }
  input:invalid { border-color: #B5352C; }
  input:valid { border-color: #2E8B6E; }
  input:placeholder-shown { background: #101C2A; }
  input:out-of-range { background: #3A1A1A; }
  details:open summary { color: #2E8B6E; }
  a:any-link { color: #38BDF8; }
  p:lang(en) { color: #E2A94E; }
  div:dir(rtl) .sw { background: #B5352C; }
</style>
</head>
<body>
  <h2>logical properties</h2>
  <div class="row"><div class="log1">inline 20 left</div><div class="log2"></div><div class="lbl">green shifted, amber shifted</div></div>

  <h2>border styles per side</h2>
  <div class="row"><div class="sw bt"></div><div class="sw bn"></div><div class="sw bl"></div><div class="lbl">dashed top dotted bottom; none; red sides only</div></div>

  <h2>text-decoration longhands</h2>
  <div class="row"><span class="d1">thick red low</span><span class="d2">wavy blue</span><span class="d3">double over</span><span class="d4">red strike</span></div>

  <h2>color-mix</h2>
  <div class="row"><div class="sw mix1"></div><div class="sw mix2"></div><div class="lbl">purple, bluer purple</div></div>

  <h2>at-rules</h2>
  <div class="row">
    <div class="sw layered"></div><div class="lbl">green (later layer)</div>
    <div class="card"><div class="sw"></div></div><div class="lbl">blue in a padded card</div>
    <div class="sw cq"></div><div class="lbl">green (design 640 wide)</div>
    <div class="sw imported"></div><div class="lbl">green from @import</div>
  </div>

  <h2>::marker and ::placeholder</h2>
  <div class="row">
    <ul><li>amber dots</li><li>on both</li></ul>
    <input placeholder="amber placeholder" name="ph">
  </div>

  <h2>form pseudo-classes</h2>
  <div class="row">
    <input required placeholder="required: red until typed" name="req">
    <input type="number" min="1" max="5" value="3" name="num">
    <input type="email" value="not-an-email" name="mail">
    <div class="lbl">red; green; red. Type in them.</div>
  </div>
  <div class="row" style="margin-top: 6px">
    <details><summary>open me: green when open</summary>inside</details>
    <a href="#x">any-link blue</a>
    <p>lang en amber</p>
    <div dir="rtl"><div class="sw"></div></div><div class="lbl">rtl red</div>
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

-- HtmlTest11.lua -- Batch F3: text, lists, tables, layout, script. Push to the 3x3 console
-- (586); design width 640. Each row names what it checks.
--
--   text      text-indent, word-break: break-all, line-clamp (2 lines, ellipsis), text-align-last
--   counters  counter-reset/increment with counter() and counters() in ::before content
--   lists     list-style-image (the thumb as a marker)
--   tables    caption-side: bottom, empty-cells: hide
--   flex      order, flex-flow, place-items
--   grid      grid-template-areas + grid-area names; grid-template shorthand; grid-area lines
--   script    DocumentFragment appended as its children; a module script importing from a URL

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
  .row { display: flex; gap: 10px; align-items: flex-start; flex-wrap: wrap; }
  .lbl { font-size: 10px; color: var(--dim); width: 70px; }
  .sw { width: 40px; height: 24px; background: #24314A; color: #E4F1F7; text-align: center; }

  .indent { width: 200px; text-indent: 24px; }
  .breakall { width: 90px; word-break: break-all; background: #172033; }
  .clamp { width: 150px; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; background: #172033; }
  .last { width: 160px; text-align: justify; text-align-last: right; background: #172033; }

  .chapters { counter-reset: chapter; }
  .chapters h3 { counter-increment: chapter; font-size: 12px; margin: 0; }
  .chapters h3::before { content: "Chapter " counter(chapter) ": "; color: var(--accent); }
  .nested { counter-reset: item; list-style: none; padding-left: 0; margin: 0; }
  .nested li { counter-increment: item; }
  .nested li::before { content: counters(item, ".") " "; color: #E2A94E; }

  ul.pics { list-style-image: url(https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png); margin: 0; padding-left: 4px; }

  table { border-collapse: collapse; caption-side: bottom; empty-cells: hide; }
  td { border: 1px solid #38BDF8; padding: 2px 8px; }
  caption { color: var(--dim); font-size: 10px; }

  .ord { display: flex; gap: 4px; } .ord .a { order: 3; } .ord .b { order: 1; } .ord .c { order: 2; }
  .flow { display: flex; flex-flow: column wrap; height: 60px; gap: 4px; }
  .place { display: flex; width: 120px; height: 40px; place-content: center; background: #172033; }

  .areas { display: grid; width: 220px; gap: 4px; grid-template-columns: 60px 1fr; grid-template-rows: 20px 30px 20px;
           grid-template-areas: "head head" "side main" "foot foot"; }
  .areas .head { grid-area: head; background: #2E8B6E; } .areas .side { grid-area: side; background: #E2A94E; }
  .areas .main { grid-area: main; background: #38BDF8; } .areas .foot { grid-area: foot; background: #B5352C; }
  .short { display: grid; width: 220px; gap: 4px; grid-template: "l r" 24px / 1fr 2fr; }
  .short .l { grid-area: l; background: #2E8B6E; } .short .r { grid-area: r; background: #38BDF8; }
  .lines { display: grid; width: 220px; gap: 4px; grid-template-columns: repeat(3, 1fr); grid-template-rows: 20px 20px; }
  .lines .big { grid-area: 1 / 1 / 3 / 3; background: #E2A94E; } .lines .sm { background: #24314A; }

  #frag li { color: #2E8B6E; } #mod { color: #E2A94E; }
</style>
</head>
<body>
  <h2>script</h2>
  <div class="row"><ul id="frag"></ul><div id="mod">module pending</div></div>

  <h2>text</h2>
  <div class="row">
    <div class="indent">indented first line of a paragraph that wraps onto a second line here</div>
    <div class="breakall">supercalifragilisticexpialidocious</div>
    <div class="clamp">one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen</div>
    <div class="last">last line right</div>
  </div>

  <h2>counters</h2>
  <div class="row">
    <div class="chapters"><h3>One</h3><h3>Two</h3><h3>Three</h3></div>
    <ol class="nested"><li>alpha<ol class="nested"><li>beta</li><li>gamma</li></ol></li><li>delta</li></ol>
    <div class="lbl">Chapter 1..3; 1, 1.1, 1.2, 2</div>
  </div>

  <h2>list-style-image</h2>
  <div class="row"><ul class="pics"><li>thumb marker</li><li>another</li></ul></div>

  <h2>tables</h2>
  <div class="row">
    <table><caption>caption below</caption><tr><td>a</td><td></td><td>c</td></tr><tr><td></td><td>e</td><td>f</td></tr></table>
    <div class="lbl">empty cells unbordered, caption under</div>
  </div>

  <h2>flex order, flow, place</h2>
  <div class="row">
    <div class="ord"><div class="sw a">A</div><div class="sw b">B</div><div class="sw c">C</div></div><div class="lbl">B C A</div>
    <div class="flow"><div class="sw">1</div><div class="sw">2</div><div class="sw">3</div><div class="sw">4</div></div><div class="lbl">two columns</div>
    <div class="place"><div class="sw">mid</div></div>
  </div>

  <h2>grid areas</h2>
  <div class="row">
    <div class="areas"><div class="head">head</div><div class="side">side</div><div class="main">main</div><div class="foot">foot</div></div>
    <div class="short"><div class="l">l</div><div class="r">r</div></div>
    <div class="lines"><div class="big">2x2</div><div class="sm"></div><div class="sm"></div><div class="sm"></div></div>
  </div>

  <script>
    var frag = document.createDocumentFragment();
    ['fragment', 'children', 'appended'].forEach(function (t) { var li = document.createElement('li'); li.textContent = t; frag.appendChild(li); });
    document.getElementById('frag').appendChild(frag);
  </script>
  <script type="module">
    import greet, { shout, answer } from "https://raw.githubusercontent.com/Gruffuss/scripted-screens-html/untested/ScriptedScreensHtml/tests/module.js";
    document.getElementById('mod').textContent = greet('module') + ' ' + shout('imported') + ' ' + answer;
  </script>
</body>
</html>
]]

ui:element({
    id = "f3",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#FF00FF" },
})

ui:commit()

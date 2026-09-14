-- HtmlTest3.lua -- web habits: markup and CSS written the way a browser page is written,
-- no vector or ScriptedScreens knowledge. Push to the 3x3 console (586); design width 640.
--
--   table     thead/tbody, th, colspan, cells coloured by an attribute selector
--   list      ul with square markers, ol numbered, a link inside an item
--   ::before  a generated dot drawn as a box (content: "" with width/height/background)
--   outline   outline + outline-offset around a box
--   @media    one rule that applies at the 640 design width and one that does not
--   ~         a general-sibling rule
--   form      text input, checkbox, range, select: ScriptedScreens controls placed over the
--             page; the page script gets change events, Lua's on_change gets "name=value"
--   scroll    a box with overflow: auto, longer than it is tall; wheel over it

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
  body { background: #0B1622; color: var(--ink); font-family: 'Barlow'; padding: 10px; font-size: 14px; }
  h2 { font-size: 13px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; margin: 10px 0 4px 0; }
  h2 ~ p { margin: 0 0 6px 0; color: var(--dim); font-size: 12px; }

  table { background: #172033; border-radius: 6px; }
  th { color: var(--dim); text-transform: uppercase; font-size: 11px; }
  td[data-state="ok"] { color: #2E8B6E; }
  td[data-state="warn"] { color: #E2A94E; }
  .live::before { content: ""; display: inline-block; width: 8px; height: 8px; border-radius: 50%; background: #2E8B6E; margin-right: 6px; align-self: center; }

  .cols { display: flex; gap: 16px; align-items: flex-start; }
  ul { list-style: square; }
  a { color: var(--accent); }

  .focus { padding: 4px 8px; border-radius: 4px; background: #24314A; outline: 2px solid var(--accent); outline-offset: 3px; margin: 8px 0 0 4px; }
  .media { color: #B5352C; margin-top: 10px; }

  form { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; margin-top: 6px; }
  label { color: var(--dim); font-size: 12px; }
  input[type=text] { width: 150px; height: 26px; background: #172033; color: var(--ink); font-size: 14px; }
  input[type=range] { width: 120px; accent-color: var(--accent); }
  input[type=checkbox], input[type=radio] { width: 16px; height: 16px; accent-color: #2E8B6E; }
  select { width: 110px; height: 26px; background: #172033; color: var(--ink); }
  #echo { margin-top: 6px; color: var(--accent); font-size: 13px; }
  button { font-size: 13px; padding: 4px 10px; background: #2E8B6E; color: white; border-radius: 4px; }
  #anyel { color: var(--dim); font-size: 12px; }
  .log { height: 64px; overflow: auto; background: #172033; border-radius: 6px; padding: 4px 8px; margin-top: 8px; width: 300px; }
  .log div { font-size: 12px; color: var(--dim); padding: 2px 0; }
  .log div:nth-child(odd) { color: var(--ink); }
  /* the browser way to restyle a checkbox: appearance none, then your own box and a :checked rule */
  .pill-check { appearance: none; width: 34px; height: 18px; border-radius: 9px; background: #24314A; border: 1px solid var(--dim); }
  .pill-check:checked { background: #2E8B6E; border-color: #2E8B6E; }
  @media (min-width: 600px) { .media { color: #2E8B6E; } }
  @media (max-width: 300px) { .media { color: #B5352C; } }
</style>
</head>
<body>
  <h2>table</h2>
  <p>a sibling rule via ~ made this line dim</p>
  <table>
    <thead><tr><th>tank</th><th>kPa</th><th>state</th></tr></thead>
    <tbody>
      <tr><td>O2</td><td>4 200</td><td data-state="ok">ok</td></tr>
      <tr><td>CO2</td><td>190</td><td data-state="warn">low</td></tr>
      <tr><td colspan="3" class="live">live, generated dot before</td></tr>
    </tbody>
  </table>

  <h2>lists, link, outline, media</h2>
  <div class="cols">
    <ul><li>square marker</li><li>with <a href="#">a link</a></li></ul>
    <ul style="list-style: disc"><li>disc</li><li style="list-style-type: circle">circle</li></ul>
    <ol><li>first</li><li>second</li></ol>
  </div>
  <div class="focus">outline, offset 3</div>
  <div class="media">@media: green at width 600 and up</div>

  <h2>form</h2>
  <form>
    <label>name</label><input type="text" id="room" name="room" placeholder="room name" value="Airlock 2">
    <label>alarm</label><input type="checkbox" id="alarm" name="alarm" checked>
    <label>target</label><input type="range" id="target" name="target" min="0" max="200" value="120">
    <label>mode</label><select id="mode" name="mode"><option value="auto">Auto</option><option value="manual" selected>Manual</option><option value="off">Off</option></select>
    <label>unit</label><input type="radio" id="kpa" name="unit" value="kPa" checked><label>kPa</label><input type="radio" id="mpa" name="unit" value="MPa"><label>MPa</label>
    <label>custom</label><input type="checkbox" id="custom" name="custom" class="pill-check">
    <button id="ping">listener</button>
    <button onclick="document.getElementById('echo').textContent = 'js: inline onclick ran on ' + this.id" id="inline">inline</button>
    <span id="anyel">a span with a click listener</span>
  </form>
  <div id="echo">js: nothing changed yet</div>
  <div class="log">
    <div>01 overflow: auto scrolls with the wheel</div><div>02 the box clips to itself</div><div>03 and slides its children</div>
    <div>04 a scroll costs one rebuild</div><div>05 no tick, no network</div><div>06 vertical only</div>
    <div>07 nth-child striping still applies</div><div>08 seven</div><div>09 eight</div><div>10 nine</div><div>11 ten</div><div>12 the end</div>
  </div>
  <script>
    var echo = document.getElementById('echo');
    document.getElementById('ping').addEventListener('click', function(e){ echo.textContent = 'js: click listener on ' + e.target.id; });
    document.getElementById('anyel').addEventListener('click', function(){ echo.textContent = 'js: the span was clicked'; });
    ['room', 'alarm', 'target', 'mode', 'kpa', 'mpa', 'custom'].forEach(function(id){
      document.getElementById(id).addEventListener('change', function(e){
        echo.textContent = 'js: ' + id + ' = ' + e.target.value + (id === 'alarm' ? ' (checked ' + e.target.checked + ')' : '');
      });
    });
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
    -- A control on the page reports here as "name=value": the input's name (or id) and
    -- its text, "true"/"false" for a checkbox, the number for a range, the option value
    -- for a select. Split at the first "=".
    on_change = function(v, player)
        local name, value = tostring(v):match("^([^=]*)=(.*)$")
        print("page change: " .. tostring(name) .. " -> " .. tostring(value))
    end,
})

ui:commit()

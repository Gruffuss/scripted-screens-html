-- HtmlTest5.lua -- Batch B: HTML coverage. Push to a 3x3 console; design width 640.
--
--   inline    abbr, q, kbd, code, del/ins, cite, sup, entities
--   blocks    blockquote, dl/dt/dd, figure/figcaption, fieldset/legend, pre
--   details   click the summary to open and close; a dialog opened from a button
--   controls  progress, an indeterminate progress, a meter in its three colours,
--             label for a checkbox, optgroup select, number with min/max/step, textarea rows/cols
--   table v2  rows as elements: striped tr, a th width, colspan, thead
--   form      a submit button fires submit on the form in script (see the echo line)

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
  body { background: #0B1622; color: var(--ink); font-family: Barlow; padding: 10px; font-size: 13px; }
  h2 { font-size: 12px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; margin: 8px 0 3px 0; }
  .row { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
  .cols { display: flex; gap: 16px; align-items: flex-start; }
  pre { background: #172033; padding: 6px 8px; border-radius: 4px; font-size: 12px; }
  details { background: #172033; border-radius: 6px; padding: 4px 8px; width: 280px; }
  details[open] summary { color: var(--accent); }
  dialog { border: 1px solid var(--accent); width: 220px; }
  dialog p { margin: 0 0 8px 0; }
  button { font-size: 12px; padding: 3px 10px; background: #24314A; color: var(--ink); border-radius: 4px; }
  progress, meter { width: 140px; accent-color: var(--accent); }
  table { width: 420px; background: #172033; border-radius: 6px; font-size: 12px; }
  thead tr { color: var(--dim); text-transform: uppercase; font-size: 11px; }
  tbody tr:nth-child(even) { background: #1E293B; }
  tbody tr:hover { background: #24314A; }
  td, th { padding: 3px 6px; }
  #echo { color: var(--accent); font-size: 12px; margin-top: 4px; }
</style>
</head>
<body>
  <h2>inline</h2>
  <p><abbr title="Carbon dioxide">CO<sub>2</sub></abbr>, <q>quoted</q>, press <kbd>Ctrl</kbd>+<kbd>S</kbd>, <code>ic.read()</code>,
     <del>old</del> <ins>new</ins>, <cite>a source</cite>, x<sup>2</sup>, &copy; &eacute; &alpha; &ne; &hearts; &check; &euro;</p>

  <h2>blocks</h2>
  <div class="cols">
    <div>
      <blockquote>A quote with a left rule.</blockquote>
      <dl><dt>Pressure</dt><dd>101 kPa, defined</dd><dt>Temperature</dt><dd>293 K</dd></dl>
    </div>
    <figure><div style="width: 80px; height: 40px; background: #2E8B6E; border-radius: 4px"></div><figcaption>figure caption</figcaption></figure>
    <fieldset><legend>Legend</legend>inside the fieldset</fieldset>
    <pre>pre keeps
  its   spaces</pre>
  </div>

  <h2>details, dialog</h2>
  <div class="row">
    <details><summary>Click to open</summary><div>Hidden until open. The triangle turns.</div><div>Second line.</div></details>
    <details open><summary>Starts open</summary><div>details[open] styles the summary.</div></details>
    <button id="dlg-open">open dialog</button>
  </div>
  <dialog id="dlg"><p>A dialog, centred, opened from script.</p><button id="dlg-close">close</button></dialog>

  <h2>controls</h2>
  <div class="row">
    <progress value="0.6" max="1"></progress><progress></progress>
    <meter value="0.3" min="0" max="1" low="0.4" high="0.7" optimum="0.9"></meter>
    <meter value="0.55" min="0" max="1" low="0.4" high="0.7" optimum="0.9"></meter>
    <meter value="0.85" min="0" max="1" low="0.4" high="0.7" optimum="0.9"></meter>
  </div>
  <form id="f" class="row" style="margin-top: 6px">
    <input type="checkbox" id="agree" name="agree"><label for="agree">label for the box</label>
    <select id="grp" name="grp"><optgroup label="Gases"><option value="o2">O2</option><option value="co2" disabled>CO2 (disabled)</option></optgroup><optgroup label="Other"><option value="n2">N2</option></optgroup></select>
    <input type="number" id="num" name="num" min="0" max="100" step="5" value="50" style="width: 60px">
    <textarea id="note" name="note" rows="2" cols="18">two rows</textarea>
    <button type="submit">submit</button>
  </form>
  <div id="echo">js: nothing yet</div>

  <h2>table v2</h2>
  <table>
    <thead><tr><th width="40%">tank</th><th>kPa</th><th>state</th></tr></thead>
    <tbody>
      <tr><td>O2</td><td>4 200</td><td>ok</td></tr>
      <tr><td>CO2</td><td>190</td><td>low</td></tr>
      <tr><td colspan="2">spanning two</td><td>--</td></tr>
      <tr><td>N2</td><td>2 800</td><td>ok</td></tr>
    </tbody>
  </table>

  <script>
    var echo = document.getElementById('echo');
    document.getElementById('dlg-open').addEventListener('click', function(){ document.getElementById('dlg').showModal(); });
    document.getElementById('dlg-close').addEventListener('click', function(){ document.getElementById('dlg').close(); });
    document.getElementById('f').addEventListener('submit', function(e){ e.preventDefault(); echo.textContent = 'js: submit, agree=' + document.getElementById('agree').checked + ' grp=' + document.getElementById('grp').value + ' num=' + document.getElementById('num').value; });
    document.querySelectorAll('details').forEach(function(d){ d.addEventListener('toggle', function(){ echo.textContent = 'js: details ' + (d.open ? 'opened' : 'closed'); }); });
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
    on_change = function(v) print("page change: " .. tostring(v)) end,
})

ui:commit()

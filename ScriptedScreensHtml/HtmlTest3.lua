-- HtmlTest3.lua -- web habits: markup and CSS written the way a browser page is written,
-- no vector or ScriptedScreens knowledge. Push to the 3x3 console (586); design width 640.
--
--   table     thead/tbody, th, colspan, cells coloured by an attribute selector
--   list      ul with square markers, ol numbered, a link inside an item
--   ::before  a generated dot drawn as a box (content: "" with width/height/background)
--   outline   outline + outline-offset around a box
--   @media    one rule that applies at the 640 design width and one that does not
--   ~         a general-sibling rule

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

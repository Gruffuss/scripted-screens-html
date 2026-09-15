-- HtmlProbe.lua -- scratch page for one question at a time.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:clear()
ui:element({ id = "probe", type = "html", rect = { unit = "px", x = 0, y = 0, w = 460, h = 460 }, props = { src = [[
<html><head><style>
  body { background: #0B1622; color: #E4F1F7; font-family: Barlow; padding: 12px; }
  footer { display: flex; gap: 16px; }
  .led { display: inline-block; width: 8px; height: 8px; border-radius: 4px; background: #2E8B6E; margin-right: 6px; }
  .box { width: 20px; height: 20px; background: #E2A94E; }
</style></head><body>
<footer><span><span class="led"></span>network ok</span><span>second</span></footer>
<div><span class="box"></span>after an empty box span</div>
</body></html>
]] } })
ui:commit()

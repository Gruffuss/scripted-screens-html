-- HtmlTest17.lua -- which property makes the Hardsuit header status vanish: the same label under one property each. 3x3 (586).
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()
local page = [[
<html><head><meta name="viewport" content="width=806"><style>
:root { --cb-font-display:'Barlow Condensed',system-ui,sans-serif; }
body { margin:0; padding:20px; background:#e4e7e1; color:#14170F; font-family:Barlow; font-size:22px; }
.row { display:flex; align-items:center; gap:12px; height:40px; }
.n { width:300px; font-size:16px; color:#5F6558; flex:none }
</style></head><body>
<div class="row"><span class="n">1 plain</span><span style="font-size:15px;font-weight:700">CORRECTING</span></div>
<div class="row"><span class="n">2 letter-spacing .14em</span><span style="font-size:15px;font-weight:700;letter-spacing:.14em">CORRECTING</span></div>
<div class="row"><span class="n">3 font shorthand condensed</span><span style="font:700 15px/1 var(--cb-font-display)">CORRECTING</span></div>
<div class="row"><span class="n">4 shorthand + spacing</span><span style="font:700 15px/1 var(--cb-font-display);letter-spacing:.14em">CORRECTING</span></div>
<div class="row"><span class="n">5 + uppercase + nowrap</span><span style="font:700 15px/1 var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;white-space:nowrap">Correcting</span></div>
<div class="row"><span class="n">6 as the design (flex, gap, lamp)</span><span style="display:flex;align-items:center;gap:8px;font:700 15px/1 var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;color:#B3710A;white-space:nowrap"><span style="width:11px;height:11px;flex:none;background:#B3710A"></span>Correcting</span></div>
<div class="row"><span class="n">7 flex text item, no lamp</span><span style="display:flex;align-items:center;gap:8px;font:700 15px/1 var(--cb-font-display)">Correcting</span></div>
<div class="row"><span class="n">8 tabular 88.8 kPa</span><span style="display:flex;align-items:baseline;gap:6px"><span style="font:700 56px/1 var(--cb-font-display);font-variant-numeric:tabular-nums">88.8</span><span style="font-size:20px">kPa</span></span></div>
<div class="row"><span class="n">9 sub/gear in Barlow</span><span style="font:700 17px/1 var(--cb-font-display)">O₂ · CH₄ · ⚙</span></div>
</body></html>
]]
ui:element({ id = "web", type = "html", rect = { unit = "px", x = 0, y = 0, w = W, h = H }, props = { src = page }, style = { bg = "#FF00FF" } })
ui:commit()

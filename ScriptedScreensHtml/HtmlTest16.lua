-- HtmlTest16.lua -- which in-game faces carry the glyphs the Coldbench designs use: subscript digits (O₂ CH₄), the gear, minus, dots. a 3x3 console.
local ui = ss.ui.surface("main")
ss.ui.activate("main")
local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end
ui:clear()
local page = [[
<html><head><meta name="viewport" content="width=806"><style>
body { margin:0; padding:20px; background:#111; color:#eee; font-size:30px; line-height:44px; }
div { white-space: nowrap }
i { color:#8ab; font-style:normal; font-size:18px; width:260px; display:inline-block }
</style></head><body>
<div><i>Barlow</i><span style="font-family:Barlow">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>Barlow Condensed</i><span style="font-family:'Barlow Condensed'">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>Manrope</i><span style="font-family:Manrope">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>LiberationSans SDF</i><span style="font-family:'LiberationSans SDF'">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>font_english</i><span style="font-family:font_english">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>noto-punc</i><span style="font-family:noto-punc">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>font_extended</i><span style="font-family:font_extended">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>RBNoBook</i><span style="font-family:RBNoBook">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>sans-serif (generic)</i><span style="font-family:sans-serif">O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
<div><i>unset (body default)</i><span>O₂ · CH₄ · ⚙ · − · • · 21.0 %</span></div>
</body></html>
]]
ui:element({ id = "web", type = "html", rect = { unit = "px", x = 0, y = 0, w = W, h = H }, props = { src = page }, style = { bg = "#FF00FF" } })
ui:commit()

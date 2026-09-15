-- 04-svg-gauge.lua -- an SVG tank whose surface ripples on the client. Paste into a Lua chip.
-- Shows: inline SVG drawn as vector geometry; attributes written as expressions over `t`
-- (seconds), `i` (sample index) and `$level` (a value from the data table); `n="40"` on a
-- polygon samples it into a continuous wave. Lua only sends the level, once a second.

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "tank",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = [[
<html>
<head>
<meta name="viewport" content="width=480">
<style>
  body { background: #0B1622; color: #E4F1F7; font-family: Barlow; padding: 16px; }
  h1 { font-size: 20px; color: #38BDF8; margin: 0 0 12px 0; }
  .wrap { display: flex; gap: 20px; align-items: flex-end; }
  svg { background: #172033; border-radius: 12px; }
  .readout { font-size: 34px; font-weight: 600; }
  .readout small { font-size: 14px; color: #7A93A6; font-weight: 400; }
  p { color: #7A93A6; font-size: 12px; max-width: 200px; }
</style>
</head>
<body>
  <h1>Oxygen tank</h1>
  <div class="wrap">
    <svg viewBox="0 0 100 160" width="150" height="240">
      <defs>
        <linearGradient id="gas" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="#38BDF8" stop-opacity="0.9"/>
          <stop offset="1" stop-color="#1B6E9E"/>
        </linearGradient>
      </defs>
      <!-- the gas: 40 samples across, the surface rides sin over t and sits at 160 - level*1.5 -->
      <polygon n="40" x="=6+i*88/39" y="=160-$level*1.5+2.5*sin(i*0.6+t*2.4)+1.5*sin(i*1.3-t*1.7)" y2="156" fill="url(#gas)"/>
      <!-- rising motes: 24 of them, each with its own phase from hash(i) -->
      <circle n="24" cx="=12+76*hash(i)" cy="=156-mod(t*(8+10*hash(i+7))+150*hash(i+3), 150)" r="=0.8+1.2*hash(i+11)" fill="#E4F1F7" fill-opacity="=0.35*step(156-$level*1.5, 156-mod(t*(8+10*hash(i+7))+150*hash(i+3), 150))"/>
      <rect x="4" y="4" width="92" height="152" rx="10" fill="none" stroke="#38BDF8" stroke-width="2"/>
      <text x="50" y="26" font-size="12" fill="#E4F1F7" text-anchor="middle" font-family="Barlow">O2</text>
    </svg>
    <div>
      <div class="readout"><span id="pct">--</span><small> %</small></div>
      <p>The wave and the motes are expressions the vector layer evaluates every frame; Lua sends one number a second.</p>
    </div>
  </div>
</body>
</html>
]] },
})

local data = ui:element({
    id = "tank_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "tank", data = { level = 40 } },
})

ui:commit()

-- Simulated level 0..100; with a real tank: math.floor(tank.RatioOxygen * 100) or the Quantity ratio.
local t, acc = 0, 0
function tick(dt)
    acc = acc + dt
    if acc < 1 then return end
    acc = 0
    t = t + 1
    local level = math.floor(50 + 45 * math.sin(t / 8))
    data:set_props({ data = { level = level, pct = tostring(level) } })
    ui:commit()
end

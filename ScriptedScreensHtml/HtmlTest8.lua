-- HtmlTest8.lua -- Batch E: <canvas> as vector paths. Push to the 3x3 console (586);
-- design width 640. Three canvases:
--
--   static    a gauge drawn once at load: arcs, gradient fill, text, dashed stroke, clip,
--             a rotated label. Costs nothing after the first emit.
--   data      a bar chart redrawn when the chip sends `vals` (every 2 s here): one emit per tick.
--   rAF       a spinner repainted every animation frame: the honest cost, one emit per frame,
--             at most 30 a second. Compare the diagnostics line with and without it.

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
  .row { display: flex; gap: 14px; align-items: flex-start; }
  canvas { background: #172033; border-radius: 6px; }
</style>
</head>
<body>
  <h2>static gauge, data chart, rAF spinner</h2>
  <div class="row">
    <canvas id="gauge" width="200" height="120"></canvas>
    <canvas id="chart" width="240" height="120"></canvas>
    <canvas id="spin" width="120" height="120"></canvas>
  </div>
  <div id="note" style="color: var(--dim); margin-top: 6px">spinner frames: 0</div>
  <script>
    // --- static: drawn once
    (function(){
      var c = document.getElementById('gauge').getContext('2d');
      c.clearRect(0, 0, 200, 120);
      c.lineWidth = 12; c.lineCap = 'round';
      c.strokeStyle = '#24314A'; c.beginPath(); c.arc(100, 100, 70, Math.PI, 2 * Math.PI); c.stroke();
      var g = c.createLinearGradient(30, 0, 170, 0); g.addColorStop(0, '#2E8B6E'); g.addColorStop(0.6, '#E2A94E'); g.addColorStop(1, '#B5352C');
      c.strokeStyle = g; c.beginPath(); c.arc(100, 100, 70, Math.PI, Math.PI * 1.7); c.stroke();
      c.fillStyle = '#E4F1F7'; c.font = 'bold 22px Barlow'; c.textAlign = 'center'; c.fillText('70%', 100, 92);
      c.font = '11px Barlow'; c.fillStyle = '#7A93A6'; c.fillText('static, drawn once', 100, 112);
      c.setLineDash([4, 3]); c.lineWidth = 1; c.strokeStyle = '#38BDF8'; c.strokeRect(6.5, 6.5, 187, 107);
      c.save(); c.beginPath(); c.rect(150, 10, 44, 30); c.clip(); c.fillStyle = '#38BDF8'; c.beginPath(); c.arc(172, 25, 30, 0, 2 * Math.PI); c.fill(); c.restore();
      c.save(); c.translate(30, 30); c.rotate(-Math.PI / 6); c.fillStyle = '#E2A94E'; c.font = '10px Barlow'; c.textAlign = 'left'; c.fillText('rotated', 0, 0); c.restore();
    })();

    // --- data: redrawn when the chip sends values
    function chart(vals){
      var c = document.getElementById('chart').getContext('2d');
      c.clearRect(0, 0, 240, 120);
      var n = vals.length, bw = 240 / n;
      for (var i = 0; i < n; i++) {
        var h = Math.max(2, vals[i] * 100);
        c.fillStyle = vals[i] > 0.8 ? '#B5352C' : '#38BDF8';
        c.beginPath(); c.roundRect(i * bw + 4, 116 - h, bw - 8, h, 3); c.fill();
      }
      c.fillStyle = '#7A93A6'; c.font = '10px Barlow'; c.textAlign = 'right'; c.fillText('data: ' + n + ' bars', 236, 12);
    }
    chart([0.3, 0.5, 0.7, 0.4, 0.9, 0.6]);
    addEventListener('data', function(e){ if (e.detail.vals) chart(e.detail.vals); });

    // --- rAF: the costly kind, on purpose
    var frames = 0, angle = 0;
    function spin(){
      var c = document.getElementById('spin').getContext('2d');
      c.clearRect(0, 0, 120, 120);
      c.save(); c.translate(60, 60); c.rotate(angle);
      for (var i = 0; i < 8; i++) { c.rotate(Math.PI / 4); c.globalAlpha = 0.2 + 0.8 * i / 7; c.fillStyle = '#38BDF8'; c.fillRect(20, -4, 24, 8); }
      c.restore();
      angle += 0.08; frames++;
      if (frames % 30 === 0) document.getElementById('note').textContent = 'spinner frames: ' + frames;
      requestAnimationFrame(spin);
    }
    requestAnimationFrame(spin);
  </script>
</body>
</html>
]]

local data
ui:element({
    id = "web",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#FF00FF" },
})
data = ui:element({
    id = "web_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "web", data = {} },
})
ui:commit()

local ticks = 0
function tick(dt)
    ticks = ticks + 1
    if ticks % 4 == 0 then
        local vals = {}
        for i = 1, 6 do vals[i] = 0.2 + 0.8 * ((ticks * 7 + i * 13) % 10) / 10 end
        data:set_props({ data = { vals = vals } })
        ui:commit()
    end
end

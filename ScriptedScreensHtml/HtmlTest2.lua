-- HtmlTest2.lua -- the second probe page: everything the layout, paint, elements and DOM
-- batches added. Push to a 2x2 console (561). What to look for is in each block's heading.
--
--   grid      a 3-column grid with gap, a spanning cell, nth-child striping, z-index overlap
--   paint     radial gradient (see the CSS note on cost), dashed and dotted borders, box-shadow, underline/strike
--   units     calc(), em, vw, a :root variable with a fallback
--   img       a ScriptedScreens image element placed over the page's box
--   button    a click region: the page's on_click gets the button id; Lua bumps a counter
--   script    document.createElement / appendChild / remove from the page script

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
  :root { --ink: #E4F1F7; --dim: #7A93A6; --accent: #38BDF8; --pad: 10px; }
  body { background: #0B1622; color: var(--ink); font-family: 'Barlow'; padding: var(--pad); }
  h2 { font-size: 13px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; margin: 8px 0 4px 0;
       text-shadow: 2px 2px 3px #000000; }
  .deco { text-shadow: 0px 0px 6px #38BDF8; }

  .grid { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 8px; }
  .cell { background: #1E293B; border-radius: 6px; padding: 8px; font-size: 14px; min-height: 30px; }
  .cell:nth-child(odd) { background: #24314A; }
  .cell:first-child { border: 1px solid var(--accent); }
  .wide { grid-column: 1 / -1; background: #172033; }

  .stack { position: relative; height: 60px; }
  .stack div { position: absolute; width: 90px; height: 40px; border-radius: 6px; font-size: 12px; padding: 4px; }
  .a { left: 0px; top: 0px; background: #B5352C; z-index: 3; }
  .b { left: 40px; top: 10px; background: #2E8B6E; z-index: 1; }
  .c { left: 80px; top: 20px; background: #E2A94E; z-index: 2; color: #111; }

  .row { display: flex; gap: 10px; align-items: center; }
  /* radial-gradient: this box alone cost ~49,000 vertices on vector mod 0.11.12.0 and pushed
     the button after it past the 60,000-vertex cap; since 0.11.20.0 the whole page is ~11,400.
     Kept in so a regression shows up here first. */
  .radial { width: 90px; height: 50px; border-radius: 8px;
            background: radial-gradient(circle at 30% 30%, #7DD3FC, #0369A1 70%); }
  .dashed { width: 90px; height: 50px; border: 2px dashed var(--accent); border-radius: 8px; }
  .dotted { width: 90px; height: 50px; border: 3px dotted #E2A94E; border-radius: 8px; }
  .shadow { width: 90px; height: 50px; background: #1E293B; border-radius: 8px;
            box-shadow: 4px 6px 10px rgba(0,0,0,0.6); }
  .deco { font-size: 14px; }
  .deco u { text-decoration: underline; }
  .deco s { text-decoration: line-through; color: var(--dim); }

  .units { font-size: 12px; color: var(--dim); }
  .calc { width: calc(100% - 40px); height: 18px; background: #24314A; border-radius: 4px; }
  .em { width: 8em; height: 1.2em; background: #2E8B6E; border-radius: 4px; margin-top: 4px; }
  .vw { width: 25vw; height: 14px; background: #B5352C; border-radius: 4px; margin-top: 4px; }

  img { width: 120px; height: 72px; border-radius: 6px; }
  button { font-size: 14px; padding: 6px 14px; background: #2E8B6E; color: white; border-radius: 6px; }
  #count { font-size: 14px; margin-left: 10px; }
  #made { font-size: 13px; color: var(--accent); }
  .pill { display: flex; padding: 2px 8px; border-radius: 10px; background: #24314A; margin-right: 6px; font-size: 12px; }

</style>
</head>
<body>
  <h2>grid, gap, nth-child, z-index</h2>
  <div class="grid">
    <div class="cell">one</div><div class="cell">two</div><div class="cell">three</div>
    <div class="cell wide">four, spanning 1 / -1</div>
    <div class="cell">five</div><div class="cell">six</div>
  </div>
  <div class="stack"><div class="a">z 3</div><div class="b">z 1</div><div class="c">z 2</div></div>

  <h2>paint</h2>
  <div class="row">
    <div class="radial"></div><div class="dashed"></div><div class="dotted"></div><div class="shadow"></div>
  </div>
  <p class="deco">plain, <u>underlined</u>, <s>struck</s></p>

  <h2>units</h2>
  <div class="calc"></div><div class="em"></div><div class="vw"></div>
  <div class="units">calc(100% - 40px), 8em, 25vw</div>

  <h2>image, button, script</h2>
  <div class="row">
    <img src="https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png">
    <div>
      <div class="row"><button id="bump">click me</button><span id="count">0 clicks</span></div>
      <div id="made" class="row"></div>
    </div>
  </div>
  <script>
    // Elements made by script: a pill per tick, capped, oldest removed.
    var pills = [];
    addEventListener('data', function(e) {
      if (e.detail.tick === undefined) return;
      var p = document.createElement('span');
      p.className = 'pill';
      p.textContent = 't' + e.detail.tick;
      document.getElementById('made').appendChild(p);
      pills.push(p);
      if (pills.length > 6) pills.shift().remove();
    });
  </script>
</body>
</html>
]]

local clicks = 0
local data

ui:element({
    id = "page",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#FF00FF" },
    -- A <button> is a click region in the vector scene: its id arrives here as the value.
    on_click = function(nodeId, player)
        print("page click: " .. tostring(nodeId))
        if nodeId == "bump" then
            clicks = clicks + 1
            data:set_props({ data = { count = clicks .. " clicks" } })
            ui:commit()
        end
    end,
})

-- A native ScriptedScreens button as a control: if this one logs and the page's does
-- not, the pointer never reaches the vector layer's hit region.
ui:element({
    id = "native", type = "button",
    rect = { unit = "px", x = W - 110, y = H - 40, w = 100, h = 30 },
    props = { text = "native" },
    style = { bg = "#2E8B6E", text = "#FFFFFF", font_size = 14 },
    on_click = function(v, player) print("native click") end,
})

-- A native ScriptedScreens image as a control: if this one shows and the page's <img> does
-- not, the download works and the fault is in how the page places its image element.
ui:element({
    id = "native_img", type = "image",
    rect = { unit = "px", x = W - 130, y = H - 130, w = 120, h = 72 },
    props = { url = "https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png" },
})

data = ui:element({
    id = "page_data",
    type = "html",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { page = "page", data = {} },
})

ui:commit()

local ticks = 0
function tick(dt)
    ticks = ticks + 1
    if ticks % 4 == 0 then
        data:set_props({ data = { tick = math.floor(ticks / 4) } })
        ui:commit()
    end
end

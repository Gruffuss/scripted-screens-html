-- FontProbe.lua — throwaway. Answers two questions the offline harness cannot:
--   1. what family names ScriptedScreensFonts actually registered
--   2. whether the vector layer draws, and what surface size we get
-- An unresolvable <font> name prints its tag verbatim, so each row is its own test.

print("ss available: " .. tostring(ss ~= nil))

local ui = ss.ui.surface("main")
ss.ui.activate("main")
ui:set_resolution(0, 0)

local sz = ui:size()
local W, H = 460, 460
if sz and (sz.w or 0) > 0 then W, H = sz.w, sz.h end
print(string.format("surface size: %dx%d", W, H))

ui:clear()
ui:element({ id = "bg", type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#000000" } })

-- Candidate family spellings. Whichever renders as clean text is the live one;
-- the rest will print their own tag back at us.
local TRY = {
    "Barlow",
    "Barlow Condensed",
    "Barlow-Regular",
    "BarlowCondensed-SemiBold",
    "Barlow SDF",
    "Barlow Condensed SDF",
}

local y = 6
for i, name in ipairs(TRY) do
    ui:element({ id = "f" .. i, type = "label",
        rect = { unit = "px", x = 8, y = y, w = W - 16, h = 26 },
        props = { text = '<font="' .. name .. '">' .. name .. " 0123 \194\183 \226\136\14660.0" },
        style = { font_size = 18, color = "#E7E7EA", align = "left" } })
    y = y + 26
end

-- weight + tracking, on whichever face ends up resolving
ui:element({ id = "wt", type = "label",
    rect = { unit = "px", x = 8, y = y, w = W - 16, h = 26 },
    props = { text = '<font="Barlow Condensed"><font-weight=600>WEIGHT 600' },
    style = { font_size = 18, color = "#94BCE3", align = "left" } })
y = y + 26
ui:element({ id = "cs", type = "label",
    rect = { unit = "px", x = 8, y = y, w = W - 16, h = 26 },
    props = { text = '<font="Barlow Condensed"><cspace=0.18em>TRACKED .18EM' },
    style = { font_size = 18, color = "#94BCE3", align = "left" } })
y = y + 34

-- vector layer: static bar, expression-driven bar, a repeat, a sampled curve
local vy = y
ui:element({ id = "vec_s", type = "vector",
    rect = { unit = "px", x = 8, y = vy, w = W - 16, h = H - vy - 8 },
    props = { scene = "probe", w = 200, h = 100, fit = "stretch", defs = {}, root = {
        { op = "R", x = 0, y = 0, w = 200, h = 100, f = "#0A0A0A" },
        { op = "R", x = 4, y = 4, w = 192, h = 2, f = "#232C37" },
        { op = "R", x = 4, y = 12, h = 8, f = "#94BCE3", w = "=190*clamp($f,0,1)" },
        { op = "RP", n = 12, c = {
            { op = "R", w = 4, h = 4, y = 30, f = "#749DC4",
              x = "=6+mod(i*16+t*10,180)", fo = "=0.35+0.65*tri(t*0.5+i*0.08)" },
        } },
        { op = "LS", n = 24, s = "#94BCE3", sw = 2, cap = "round",
          x = "=6+i*7.8", y = "=70+18*sin(i*0.35+t*1.2)" },
        { op = "C", cx = 190, cy = 92, rx = 4, ry = 4, f = "#CF6A58",
          fo = "=0.3+0.7*tri(t*0.6)" },
    } } })

local data = ui:element({ id = "vec_d", type = "vector",
    rect = { unit = "px", x = -4, y = -4, w = 1, h = 1 },
    props = { scene = "probe", data = { f = 0.5 } } })

ui:commit()
print("probe built, " .. #TRY .. " font rows")

local n = 0
function tick(dt)
    n = n + 1
    data:set_props({ data = { f = 0.5 + 0.45 * math.sin(n * 0.05) } })
    ui:commit()
    if n % 20 == 0 then print("tick " .. n) end
end

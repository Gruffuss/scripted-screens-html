-- Font tag test for the ScriptedScreens Fonts mod.
--
-- The names below are the ones the mod registered on this machine
-- (BepInEx/LogOutput.log, "Font available:" lines). Re-check the log after a
-- game update -- the list depends on which bundles happen to be loaded.
--
-- Reading the result:
--   row renders in its own typeface  -> the tag resolved
--   row looks like the CONTROL row   -> TMP silently fell back to the default
--   row shows the literal <font=...> -> TMP rejected the tag outright
--
-- The header prints the surface's real size, so the layout can be checked
-- against what the console actually reports rather than an assumed 480x272.

local FONTS = {
    "LiberationSans SDF",
    "RBNo3.1-Book SDF",
    "LiberationSans SDF - Fallback",
    "font_english",
    "font_extended",
    "font_russian",
    -- "RBBook SDF",  -- its material has no _CullMode; rendering it in a UI label makes
    --                -- Unity log an error every canvas update. Re-enable to reproduce.
    "code",
    "RBNoBold",
    "RBBook EXTENDED",
    "RBNoBook",
    "font_cjk",
    "font_cjk_b",
    "font_cjk_j",
    "font_hangul",
    "font_hangul_b",
    "font_misans_cn",
    "noto-punc",
    -- The mod logs this font's runtime name as lowercase, but the asset inside
    -- ScriptedScreens' bundle is Assets/Resources/fonts/SS_DejaVu_Fallback.asset.
    -- If the capitalised row renders and the lowercase one prints its raw tag, the
    -- failure is a hash-of-original-name vs hash-of-current-name mismatch.
    "ss_dejavu_fallback",
    "SS_DejaVu_Fallback",
}

-- Loaded from .ttf/.otf files in the mod's Assets/fonts folder (scanned recursively).
-- The name is the font's own family plus its style when that is not Regular, exactly as
-- the log reports it on the "Font available:" lines. The tag hash is case sensitive.
local OS_FONTS = {
    "Barlow Thin",
    "Barlow ExtraLight",
    "Barlow Light",
    "Barlow",
    "Barlow Medium",
    "Barlow SemiBold",
    "Barlow Bold",
    "Barlow ExtraBold",
    "Barlow Black",
    "Barlow Italic",
    "Barlow Bold Italic",
    "Barlow Condensed Thin",
    "Barlow Condensed Light",
    "Barlow Condensed",
    "Barlow Condensed Medium",
    "Barlow Condensed SemiBold",
    "Barlow Condensed Bold",
    "Barlow Condensed Black",
    "Barlow Condensed Italic",
    "Barlow Condensed Bold Italic",
}

for _, name in ipairs(OS_FONTS) do
    FONTS[#FONTS + 1] = name
end

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 480, 480
if size then W, H = size.w, size.h end

ui:clear()

ui:element({
    id = "bg",
    type = "panel",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    style = { bg = "#0F172A" },
})

-- One column at full width: the longest name is ~29 characters, so a single
-- column is the only layout that never wraps, and wrapping is exactly what
-- makes a failed row (which prints the whole tag) blow out the bottom.
local PAD = 8
local head = ui:element({
    id = "control",
    type = "label",
    rect = { unit = "px", x = PAD, y = 4, w = W - PAD * 2, h = 18 },
    props = { text = string.format("CONTROL  %dx%d  abcdefg 0123", W, H) },
    style = { font_size = 13, color = "#94A3B8", align = "left" },
})

local TOP = 24
local avail = H - TOP - PAD
local row = avail / #FONTS
local fsize = math.max(9, math.min(15, math.floor(row) - 4))

for i, name in ipairs(FONTS) do
    ui:element({
        id = "font" .. i,
        type = "label",
        rect = {
            unit = "px",
            x = PAD,
            y = math.floor(TOP + (i - 1) * row),
            w = W - PAD * 2,
            h = math.floor(row),
        },
        props = { text = '<font="' .. name .. '">' .. i .. '. ' .. name },
        style = { font_size = fsize, color = "#22C55E", align = "left" },
    })
end

ui:commit()

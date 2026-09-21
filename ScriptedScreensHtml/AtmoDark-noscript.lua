-- AtmoDark-noscript.lua -- AtmoDark with its script block removed, nothing else changed.
--
-- The control half of the only measurement that can size the JS interpreter's share under Mono.
-- The bench cannot: its counter is per-thread and Jint runs on the ScriptHost's own worker. And
-- this player serves no per-frame allocation counter - checked by enumerating all 42 of them in
-- game on 2026-09-21; the memory ones are all used/reserved, i.e. heap size. So comparing two
-- pages that differ in exactly one thing is the instrument that is left.
--
-- Same markup, same CSS, same layout and emit work, no interpreter. Note the split: of the page
-- 58,621 of 68,218 characters are the script.

-- AtmoDark.lua -- the same console in the Coldbench dark design system, written as an ordinary web page (HTML, CSS and a script) and embedded here.
-- Paste into a Lua chip in a ScriptedScreens console; a 3x3 console shows it best (design width in the page).
-- Maintainers: generated from AtmoDark.html in the repository (https://github.com/Gruffuss/scripted-screens-html); edit the html there.
-- TAB picks the screen the page opens on (atmo | supply | filter | alarm | config), for captures.

local TAB = "atmo"

local ui = ss.ui.surface("main")
ss.ui.activate("main")

local size = ui:size()
local W, H = 460, 460
if size then W, H = size.w, size.h end

ui:clear()

local page = [==[
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=726">
<title>Atmo Regulator, Coldbench dark</title>
<style>
/* tokens/fonts.css */
/* Barlow + Barlow Condensed, served by Google Fonts.
   Coldbench uses no local font binaries; if a consuming project needs
   offline fonts, vendor the Barlow family and rewrite these rules. */

/* tokens/colors.css */
:root{
  /* Ground — OLED black. Panels are never filled; they are drawn. */
  --cb-ground:#000000;
  --cb-ground-raised:#0a0a0a;
  --cb-ground-inset:#0d0d0d;
  --cb-ground-well:#101010;
  --cb-ground-alarm:#1a100e;
  --cb-ground-caution:#2a2318;
  --cb-ground-alarm-2:#2a1a18;

  /* Steel — the one accent, as a ramp. 300 is "live", 500 draws, 800/900 rule. */
  --cb-steel-200:#b5d9fd;
  --cb-steel-300:#94bce3;
  --cb-steel-400:#749dc4;
  --cb-steel-500:#597ea3;
  --cb-steel-600:#416180;
  --cb-steel-700:#2c455d;
  --cb-steel-750:#243444;
  --cb-steel-800:#232c37;
  --cb-steel-900:#1b232c;

  /* Semantics — exactly two. Amber warns, rust trips. No green: nominal is steel. */
  --cb-amber:#e0a44f;
  --cb-rust:#cf6a58;

  /* Type */
  --cb-text-1:#e7e7ea;
  --cb-text-1-dim:#b7b7ba;
  --cb-text-2:#98989b;
  --cb-text-3:#7a7a7d;
  --cb-text-4:#5d5d60;

  /* Semantic aliases */
  --cb-hairline:var(--cb-steel-800);
  --cb-hairline-inner:var(--cb-steel-900);
  --cb-hairline-live:var(--cb-steel-700);
  --cb-hairline-hot:var(--cb-steel-600);
  --cb-hairline-alarm:#3a2420;
  --cb-mark:var(--cb-steel-500);
  --cb-live:var(--cb-steel-300);
  --cb-track:var(--cb-steel-400);
  --cb-link:var(--cb-steel-300);
  --cb-link-hover:var(--cb-steel-200);
  --cb-on-accent:#000000;

  /* Track fills — a fraction bar is a 3px edge, never a filled body. */
  --cb-fill-steel:rgba(116,157,196,.75);
  --cb-fill-live:rgba(148,188,227,.75);
  --cb-fill-amber:rgba(224,164,79,.75);
  --cb-fill-rust:rgba(207,106,88,.75);
  --cb-wash-steel:rgba(116,157,196,.13);
  --cb-wash-live:rgba(148,188,227,.10);
  --cb-wash-amber:rgba(224,164,79,.06);
}

/* tokens/typography.css */
:root{
  --cb-font-display:'Barlow Condensed',system-ui,sans-serif;
  --cb-font-body:Barlow,system-ui,sans-serif;

  /* Display / numeric readouts — condensed, 600, tight leading */
  --cb-size-hero:88px;      --cb-lh-hero:1.0;
  --cb-size-hero-2:72px;    --cb-lh-hero-2:0.9;
  --cb-size-hero-3:54px;    --cb-lh-hero-3:1.06;
  --cb-size-value-xl:42px;  --cb-lh-value:1;
  --cb-size-value-lg:34px;
  --cb-size-value:26px;
  --cb-size-value-sm:19px;
  --cb-size-value-xs:16px;

  /* Labels — condensed, 500, uppercase, wide tracking */
  --cb-size-label-lg:18px;
  --cb-size-label:15px;
  --cb-size-label-sm:13px;
  --cb-size-label-xs:12.5px;

  /* Body / annotation — Barlow, sentence case */
  --cb-size-body:15px;
  --cb-size-body-sm:13px;
  --cb-size-annot:12.5px;
  --cb-size-micro:11.5px;

  --cb-track-screen:.18em;   /* screen title */
  --cb-track-label:.16em;    /* field labels */
  --cb-track-status:.12em;   /* status words, buttons */
  --cb-track-value:.04em;    /* inline numerics */
  --cb-track-hero:-.02em;    /* hero numerals close up */

  --cb-weight-value:600;
  --cb-weight-label:500;
}

/* tokens/spacing.css */
:root{
  /* Odd-numbered, integer-aligned: this is a 726px screen, not a web page. */
  --cb-hair:1px;
  --cb-track-h:3px;   /* fraction-bar edge thickness */
  --cb-space-1:5px;
  --cb-space-2:6px;
  --cb-space-3:9px;
  --cb-space-4:11px;
  --cb-space-5:13px;
  --cb-space-6:16px;
  --cb-space-7:18px;
  --cb-space-8:22px;
  --cb-space-9:24px;
  --cb-space-10:30px;

  --cb-screen:726px;        /* medium console, square */
  --cb-header-h:56px;
  --cb-tabbar-h:52px;
  --cb-screen-pad:22px 24px;
  --cb-header-pad:0 30px;
  --cb-mark-size:13px;      /* corner registration mark */
  --cb-mark-offset:-7px;
  --cb-radius:0;            /* nothing is rounded, ever */
}

/* tokens/motion.css */
:root{
  --cb-ease:linear;            /* machines do not ease */
  --cb-dur-march:1.1s;
  --cb-dur-blip:1.6s;
  --cb-dur-clamp:3.4s;
  --cb-dur-intake:2.6s;
  --cb-dur-trip:.9s;
}

/* Gas marching through a duct */
@keyframes cb-march{to{background-position:26px 0}}
/* A live sample point */
@keyframes cb-blip{0%,100%{opacity:1}50%{opacity:.3}}
/* A value pinned against a clamp */
@keyframes cb-clamp{0%,100%{background-color:rgba(224,164,79,.05)}50%{background-color:rgba(224,164,79,.15)}}
/* A vent breathing inward */
@keyframes cb-intake{0%{opacity:.15;transform:translateY(-3px)}45%{opacity:1;transform:translateY(2px)}100%{opacity:.15;transform:translateY(-3px)}}
/* A tripped alarm tile */
@keyframes cb-trip{0%,100%{filter:brightness(1)}50%{filter:brightness(1.3)}}

@media (prefers-reduced-motion:reduce){
  *{animation:none !important}
}

/* tokens/surfaces.css */
/* Base type + the two drawn primitives every Coldbench surface is made of. */
body{
  margin:0;
  background:var(--cb-ground);
  color:var(--cb-text-1);
  font-family:var(--cb-font-body);
  -webkit-font-smoothing:antialiased;
}
a{color:var(--cb-link);text-underline-offset:3px}
a:hover{color:var(--cb-link-hover)}
::selection{background:var(--cb-wash-steel)}
:focus-visible{outline:1px solid var(--cb-live);outline-offset:2px}

/* Hairline box — the only container. No fill, no radius, no shadow. */
.cb-box{position:relative;border:var(--cb-hair) solid var(--cb-hairline);background:var(--cb-ground)}
.cb-box-live{border-color:var(--cb-hairline-live)}
.cb-box-hot{border-color:var(--cb-hairline-hot)}
.cb-box-alarm{border-color:var(--cb-hairline-alarm)}

/* Registration mark — a crosshair, drawn in two gradients, never an SVG. */
.cb-mark{
  position:absolute;width:var(--cb-mark-size);height:var(--cb-mark-size);pointer-events:none;
  background:
    linear-gradient(var(--cb-mark),var(--cb-mark)) 6px 0/1px 13px no-repeat,
    linear-gradient(var(--cb-mark),var(--cb-mark)) 0 6px/13px 1px no-repeat;
}
.cb-mark.tl{left:var(--cb-mark-offset);top:var(--cb-mark-offset)}
.cb-mark.tr{right:var(--cb-mark-offset);top:var(--cb-mark-offset)}
.cb-mark.bl{left:var(--cb-mark-offset);bottom:var(--cb-mark-offset)}
.cb-mark.br{right:var(--cb-mark-offset);bottom:var(--cb-mark-offset)}

/* Label / value / annotation */
.cb-label{font:var(--cb-weight-label) var(--cb-size-label) var(--cb-font-display);letter-spacing:var(--cb-track-label);text-transform:uppercase;color:var(--cb-text-2)}
.cb-value{font:var(--cb-weight-value) var(--cb-size-value) var(--cb-font-display);letter-spacing:var(--cb-track-value)}
.cb-annot{font-family:var(--cb-font-body);font-size:var(--cb-size-annot);color:var(--cb-text-3)}

/* the design file's own page style */
  body { margin: 0; background: #000; }
  a { color: var(--cb-link); } a:hover { color: var(--cb-link-hover); }

/* hover states the design expresses outside CSS: the setpoint buttons' style-hover attribute
   and ConsoleButton's React hover state (default and danger variants) */
  [style-hover]:hover { background:var(--cb-wash-steel); }
  [role="button"]:hover { background:var(--cb-wash-steel); }
  [role="button"][data-variant="danger"]:hover { background:rgba(207,106,88,.13); }
</style>
</head>
<body>
<div id="frame" style="position:relative;width:726px;height:726px;flex:none;overflow:hidden;background:var(--cb-ground);color:var(--cb-text-1);font-family:var(--cb-font-body);display:flex;flex-direction:column"></div>
<!-- script removed: control half of the interpreter A/B -->
</body>
</html>

]==]

page = page:gsub("%|%| '#atmo'%)%.slice%(1%) %|%| 'atmo'", "|| '#" .. TAB .. "').slice(1) || '" .. TAB .. "'", 1)

ui:element({
    id = "web",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#FF00FF" },
})

ui:commit()

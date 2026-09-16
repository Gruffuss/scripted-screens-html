-- AtmoLight.lua -- the Hardsuit (lit hull) Atmo Regulator mockup (AtmoUi/Light) as a page: AtmoLight.html verbatim, embedded.
-- Generated from AtmoLight.html the way AtmoApple.lua is from AtmoApple.html; edit the html, not this file. Push to the 3x3 console (586).
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
<meta name="viewport" content="width=806">
<title>Atmo Regulator, Hardsuit lit</title>
<style>
/* ===== _ds/tokens/fonts.css ===== */
/* Barlow + Barlow Condensed, served by Google Fonts.
   Coldbench uses no local font binaries; if a consuming project needs
   offline fonts, vendor the Barlow family and rewrite these rules. */

/* ===== _ds/tokens/colors.css ===== */
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

/* ===== _ds/tokens/typography.css ===== */
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

/* ===== _ds/tokens/spacing.css ===== */
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

/* ===== _ds/tokens/motion.css ===== */
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

/* ===== _ds/tokens/surfaces.css ===== */
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

/* ===== hardsuit-theme.css ===== */
/* ─────────────────────────────────────────────────────────────
   HARDSUIT — console theme, LIT HULL direction.

   The station interior is bright: white-grey hull plate, dark stencilled
   ink, bold blocks of colour, thick outlines, softly radiused corners.
   Panels are BUILT, not drawn — a block is a filled shape with a heavy
   edge, never a hairline box. Colour is used generously but it always
   carries a meaning:

     GREEN  clear / go / complete
     AMBER  working / caution
     RED    fault / trip / override
     TEAL   held stock / data / telemetry
     ORANGE hazard & wayfinding — tape, tracks, active edges

   Type: Barlow Condensed caps for labels, Barlow for prose, tabular
   numerals for readouts. No mono.
   ───────────────────────────────────────────────────────────── */
:root {
  /* Hull — lit interior plate. Blocks sit ON this, not in a void. */
  --cb-ground: #e4e7e1;
  --cb-ground-raised: #f1f3ee;
  --cb-ground-inset: #d5d9d2;
  --cb-ground-well: #c8cdc5;
  --cb-ground-alarm: #f6dcd8;
  --cb-ground-caution: #f8e8c8;
  --cb-ground-alarm-2: #f2cdc7;

  /* Ramp: 300 is the data/held colour (teal), 700+ are the light rules. */
  --cb-steel-200: #4fa8c4;
  --cb-steel-300: #1f6f8a;
  --cb-steel-400: #5e6558;
  --cb-steel-500: #454b40;
  --cb-steel-600: #6f7669;
  --cb-steel-700: #aab1a4;
  --cb-steel-750: #b7bdb1;
  --cb-steel-800: #98a092;
  --cb-steel-900: #c3c9bd;

  /* True amber. It is an EDGE, LAMP or FILL colour — never type on hull,
     because an amber dark enough to pass 4.5:1 reads brown. */
  --cb-amber: #d68f16;
  --cb-rust: #b3241a;

  /* Ink — stencilled dark on hull */
  --cb-text-1: #14170f;
  --cb-text-1-dim: #2c3126;
  --cb-text-2: #464c3f;
  --cb-text-3: #5f6558;
  --cb-text-4: #7c8375;

  /* Outlines are structural and heavy; inner rules are faint. */
  --cb-hairline: #8b9385;
  --cb-hairline-inner: #c3c9bd;
  --cb-hairline-live: #1f8a30;
  --cb-hairline-hot: #a2660a;
  --cb-hairline-alarm: #b3241a;
  --cb-mark: var(--cb-steel-500);

  --cb-live: #1f8a30;
  /* Ink variants — the same signals as TYPE on hull. Fills keep the brighter
     values above; anything that is text or a thin mark uses these (≥4.5:1). */
  --cb-live-ink: #14661f;
  /* Amber as TYPE: true amber, dark enough for ~3.6:1 — so it is allowed only
     at headline scale (≥19px/700), never on captions or small labels. */
  --cb-amber-ink: #b3710a;
  --cb-track: var(--hs-hazard);
  --cb-link: var(--cb-steel-300);
  --cb-link-hover: var(--cb-steel-200);
  --cb-on-accent: #f4f7f1;

  --cb-fill-steel: #1f6f8a;
  --cb-fill-live: #1f8a30;
  --cb-fill-amber: #d68f16;
  --cb-fill-rust: #b3241a;
  --cb-wash-steel: rgba(31,111,138,.14);
  --cb-wash-live: rgba(31,138,48,.15);
  --cb-wash-amber: rgba(214,143,22,.20);

  /* Material */
  --hs-theme-btn-bg: #f1f3ee;
  --hs-hazard: #e8622a;
  --hs-tape: repeating-linear-gradient(135deg, #f0b21f 0 8px, #1b1d16 8px 16px);
  --hs-radius: 3px;
  --hs-edge: 3px;
  /* Depth comes from fill + edge weight only. No bevels, gradients or drop
     shadows: a header is part of the console surface, not a plate on top of it.
     The plate stops are therefore all the SAME value in LIT — anything painting
     with them renders as one flat fill, not a bevelled slab. */
  --hs-edge-ink: #14170f;
  --hs-plate-hi: #f1f3ee;
  --hs-plate: #f1f3ee;
  --hs-plate-lo: #f1f3ee;
  --hs-plate-ink: #14170f;
}

/* ── DIM: the same system with the hull unlit (night shift, EVA, power-save).
   Only the ground/ink pairs invert; the five signal hues keep their meaning
   and simply brighten, because on a dark hull light type is the legible one. */
[data-hs-theme="dim"] {
  /* DIM is the design system's original OLED-black console theme, unchanged:
     drawn hairline panels on #000 with the steel ramp as the only accent.
     Kept verbatim from _ds/tokens/colors.css so the two themes are a true
     swap — LIT is the new lit-hull palette, DIM is the console as it shipped. */
  --cb-ground: #000000;
  --cb-ground-raised: #0a0a0a;
  --cb-ground-inset: #0d0d0d;
  --cb-ground-well: #101010;
  --cb-ground-alarm: #1a100e;
  --cb-ground-caution: #2a2318;
  --cb-ground-alarm-2: #2a1a18;

  --cb-steel-200: #b5d9fd;
  --cb-steel-300: #94bce3;
  --cb-steel-400: #749dc4;
  --cb-steel-500: #597ea3;
  --cb-steel-600: #416180;
  --cb-steel-700: #2c455d;
  --cb-steel-750: #243444;
  --cb-steel-800: #232c37;
  --cb-steel-900: #1b232c;

  --cb-amber: #e0a44f;
  --cb-amber-ink: #e0a44f;
  --cb-rust: #cf6a58;
  --cb-live: #94bce3;
  --cb-live-ink: #94bce3;

  --cb-text-1: #e7e7ea;
  --cb-text-1-dim: #b7b7ba;
  --cb-text-2: #98989b;
  --cb-text-3: #7a7a7d;
  --cb-text-4: #5d5d60;

  --cb-hairline: #232c37;
  --cb-hairline-inner: #1b232c;
  --cb-hairline-live: #2c455d;
  --cb-hairline-hot: #416180;
  --cb-hairline-alarm: #3a2420;
  --cb-mark: #597ea3;
  --cb-track: #749dc4;
  --cb-link: #94bce3;
  --cb-link-hover: #b5d9fd;
  --cb-on-accent: #000000;

  --cb-fill-steel: rgba(116,157,196,.75);
  --cb-fill-live: rgba(148,188,227,.75);
  --cb-fill-amber: rgba(224,164,79,.75);
  --cb-fill-rust: rgba(207,106,88,.75);
  --cb-wash-steel: rgba(116,157,196,.13);
  --cb-wash-live: rgba(148,188,227,.10);
  --cb-wash-amber: rgba(224,164,79,.06);

  --hs-plate-hi: #0a0a0a;
  --hs-plate: #0a0a0a;
  --hs-plate-lo: #000000;
  --hs-plate-ink: #e7e7ea;
  --hs-theme-btn-bg: #0a0a0a;
}
  body { margin: 0; background: #b9bfb6; }
  a { color: var(--cb-link); } a:hover { color: var(--cb-link-hover); }
</style>
</head>
<body>
<div id="panel" data-hs-theme="lit" style="position:relative;box-sizing:border-box;width:806px;height:806px;flex:none;overflow:hidden;display:flex;flex-direction:column;gap:10px;padding:0 14px 14px;background:var(--cb-ground);color:var(--cb-text-1);font-family:var(--cb-font-body)"></div>
<script>
// ---- the design's data model, verbatim from its <script>
const SCEN = {
  nominal:    { press: 101.1, temp: 21.2, o2: 20.9, co2: 0.34, poll: 0, ch4: 0, vol: 0, nox: 0, steam: 0.03 },
  correcting: { press: 88.4,  temp: 14.6, o2: 15.8, co2: 0.92, poll: 0.06, ch4: 0.03, vol: 0, nox: 0, steam: 0.12 },
  tripped:    { press: 143.6, temp: 29.4, o2: 18.2, co2: 2.41, poll: 0.62, ch4: 0.41, vol: 0.21, nox: 0.06, steam: 0.34 }
};
const ROLES = (() => {
  const PA = [['O2-AN', '2 039 010', '20.9 % O₂ · 2.4 k mol'], ['CO2-AN', '2 039 011', '0.9 % CO₂ · 0.8 k mol'], ['N-AN', '2 039 012', '78 % N₂ · 1.1 k mol'], ['Pipe Analyzer', '2 042 780', '0 kPa · unlabelled']];
  const VP = [['O2-Pump', '2 039 020', 'setting 6.2 L · on'], ['CO2-Pump', '2 039 021', 'setting 1.4 L · on'], ['N-Pump', '2 039 022', 'setting 0 L · off'], ['Volume Pump', '2 042 300', 'setting 0 · unlabelled']];
  const VENT = [['Vent-Filt', '2 039 041', 'MODE 0 · setting 130'], ['Vent-Safety', '2 039 042', 'MODE 0 · setting 140'], ['Powered Vent', '2 042 410', 'MODE 1 · unlabelled']];
  const R = (k, g, sub, devs) => ({ key: k, group: g, sub: sub, devs: devs.map((x) => ({ name: x[0], id: x[1], meta: x[2] })) });
  return [
    R('O2-AN', 'gas', 'Pipe Analyzer · oxygen supply line into the room', PA),
    R('O2-Pump', 'gas', 'Volume Pump · injects O₂ against the O₂ target', VP),
    R('CO2-AN', 'gas', 'Pipe Analyzer · carbon dioxide supply line', PA),
    R('CO2-Pump', 'gas', 'Volume Pump · trims CO₂ up to the ceiling', VP),
    R('N-AN', 'gas', 'Pipe Analyzer · nitrogen line, optional buffer gas', PA),
    R('N-Pump', 'gas', 'Volume Pump · nitrogen makeup, optional', VP),
    R('Filt', 'filt', 'Filtration unit · CH₄ and pollutant filters', [['Filt', '2 039 050', 'ON · 2 filters seated'], ['Filtration', '2 042 500', 'OFF · unlabelled']]),
    R('Vent-Filt', 'filt', 'Vent · pulls room gas into the filtration inlet', VENT),
    R('Pump-Out', 'filt', 'Volume Pump · filtration output into the waste tank', VP),
    R('Vent-Safety', 'filt', 'Vent · overpressure relief at the trip setting', VENT),
    R('Sensor-Room', 'ctl', 'Gas Sensor · pressure, temperature and composition', [['Sensor-Room', '2 039 070', '101 kPa · 21.2 °C'], ['Gas Sensor', '2 042 600', '102 kPa · 21.4 °C'], ['Sensor-Hall', '2 039 071', '99 kPa · 19.8 °C']]),
    R('AC', 'ctl', 'Air Conditioner · holds the room temperature setpoint', [['AC', '2 039 080', 'MODE 1 · set 294 K'], ['Air Conditioner', '2 042 700', 'MODE 0 · unlabelled']]),
    R('Vent-Pass', 'ctl', 'Passive Vent · returns AC output into the room', [['Vent-Pass', '2 039 081', 'open · hab side'], ['Passive Vent', '2 042 710', 'open · unlabelled']]),
    R('Alarm-Snd', 'ctl', 'Sound Alarm · klaxon on any tripped condition', [['Alarm-Snd', '2 039 090', 'off · hab'], ['Sound Alarm', '2 042 800', 'off · unlabelled']]),
    R('Alarm-Lgt', 'ctl', 'Light Alarm · strobe on any tripped condition', [['Alarm-Lgt', '2 039 091', 'off · corridor'], ['Light Alarm', '2 042 810', 'off · unlabelled']])
  ];
})();
const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
const f1 = (v) => (v < 0 ? '−' : '') + Math.abs(v).toFixed(1);
const f2 = (v) => v.toFixed(2);
const SETPOINTS = [
  { key: 'o2', field: 'o2', label: 'O₂ target', unit: '%', step: 0.5, min: 0, max: 40, fmt: f1, note: 'oxygen the regulator holds in the room · breathable above 19.5 %' },
  { key: 'co2', field: 'co2', label: 'CO₂ target', unit: '%', step: 0.05, min: 0, max: 5, fmt: f2, note: 'carbon dioxide held in the room · pump feeds below it, filtration trims above' },
  { key: 'press', field: 'press', label: 'Pressure', unit: 'kPa', step: 2, min: 0, max: 200, fmt: f1, note: 'total room pressure the supply pumps charge toward' },
  { key: 'temp', field: 'temp', label: 'Temp', unit: '°C', step: 0.5, min: -40, max: 60, fmt: f1, note: 'air conditioner setpoint, fed back through the passive vent' },
  { key: 'trip', field: 'trip', label: 'Vent trip', unit: 'kPa', step: 5, min: 20, max: 300, fmt: (v) => v.toFixed(0), note: 'safety vent dumps the room above this pressure' }
];

// ---- the design's state and simulation, setState -> a render call (the design's data-props defaults)
const props = { scenario: 'correcting', simSpeed: 1, nitrogenLine: false, roomName: 'hab core' };
const st = {
  tab: ((typeof location !== 'undefined' && location.hash) || '#atmo').slice(1) || 'atmo', tick: 0,
  r: { ...(SCEN[props.scenario] || SCEN.correcting) },
  t: { press: 101.3, temp: 21.0, o2: 21.0, co2: 0.30, trip: 140 },
  filters: { ch4: 76, poll: 62 },
  sound: true, light: true, purging: false,
  role: 'N-AN', sp: 'o2',
  lines: { o2: { temp: 11.8, press: 3140, mol: 2412, pump: 6.2 }, co2: { temp: 14.2, press: 1880, mol: 1046, pump: 1.4 }, n2: { temp: 9.6, press: 4270, mol: 3318, pump: 0 } },
  dirty: false, dim: false,
  bound: ROLES.reduce((a, r) => { if (r.key.indexOf('N-') !== 0) a[r.key] = r.key; return a; }, {}),
  log: [
    { t: '02:14:06', msg: 'regulator armed · sampling room atmosphere', tag: 'INFO', color: 'var(--cb-text-4)' },
    { t: '02:13:52', msg: 'bindings loaded · 13 of 15 roles set', tag: 'CONFIG', color: 'var(--cb-text-4)' },
    { t: '02:11:30', msg: 'pollutant filter swapped · 62 % remaining', tag: 'MAINT', color: 'var(--cb-text-4)' }
  ]
};
let alarmPrev = null;
function clock(tick) {
  const total = 8046 + Math.round((tick || st.tick) * 0.42);
  const h = String(Math.floor(total / 3600) % 24).padStart(2, '0');
  const m = String(Math.floor(total / 60) % 60).padStart(2, '0');
  const s = String(total % 60).padStart(2, '0');
  return h + ':' + m + ':' + s;
}
function push(msg, tag, color) { st.log = [{ t: clock(st.tick), msg, tag, color }].concat(st.log).slice(0, 7); }
function step() {
  const k = props.simSpeed == null ? 1 : props.simSpeed;
  if (k === 0) return;
  const r = st.r, t = st.t, fl = st.filters;
  const nz = () => (Math.random() - 0.5) * 0.03 * k;
  const vent = r.press > t.trip;
  const purge = st.purging;
  r.o2 += (t.o2 - r.o2) * 0.045 * k + nz();
  r.co2 += (t.co2 - r.co2) * (r.co2 > t.co2 ? 0.075 : 0.035) * k + nz() * 0.2;
  r.poll += (purge ? -0.09 : -0.045) * r.poll * k + (props.scenario === 'tripped' && !purge ? 0.0035 * k : 0);
  r.ch4 += -0.05 * r.ch4 * k;
  r.vol += -0.045 * r.vol * k;
  r.nox += -0.045 * r.nox * k;
  r.steam += (r.temp > t.temp ? -0.02 : -0.06) * r.steam * k;
  const duty = { o2: clamp((t.o2 - r.o2) * 34, 0, 100), co2: clamp((t.co2 - r.co2) * 220, 0, 100), n2: props.nitrogenLine ? clamp((100 - t.o2 - t.co2 - (100 - r.o2 - r.co2)) * 20, 0, 100) : 0 };
  Object.keys(st.lines).forEach((key) => {
    const L = st.lines[key], d = duty[key];
    L.mol = Math.max(0, L.mol - d * 0.012 * k);
    L.press = L.mol * (key === 'o2' ? 1.302 : key === 'co2' ? 1.797 : 1.287);
    L.temp += ((d > 1 ? 8.5 : 10.5) - L.temp) * 0.02 * k + (Math.random() - 0.5) * 0.05;
    L.pump = d > 1 ? Math.max(0.4, d * 0.062) : 0;
  });
  const pTarget = purge ? 12 : t.press;
  r.press += (pTarget - r.press) * (vent || purge ? 0.11 : 0.035) * k + nz();
  r.temp += (t.temp - r.temp) * 0.03 * k + nz() * 0.4;
  r.o2 = clamp(r.o2, 0, 40); r.co2 = clamp(r.co2, 0, 8); r.poll = clamp(r.poll, 0, 4);
  if (r.co2 > t.co2 || r.poll > 0.02) { fl.poll = Math.max(0, fl.poll - 0.006 * k); fl.ch4 = Math.max(0, fl.ch4 - 0.002 * k); }
  st.tick++;
  if (purge && r.press < 14) st.purging = false;
  watch();
  render(false);
}
function watch() {
  const r = st.r, t = st.t;
  const now = {
    press: Math.abs(r.press - t.press) > 20 ? 'trip' : Math.abs(r.press - t.press) > 9 ? 'caution' : 'ok',
    o2: r.o2 < 17 ? 'trip' : r.o2 < 19.5 ? 'caution' : 'ok',
    poll: (r.poll + r.ch4) > 0.1 ? 'trip' : (r.poll + r.ch4) > 0.001 ? 'caution' : 'ok',
    temp: Math.abs(r.temp - t.temp) > 6 ? 'trip' : Math.abs(r.temp - t.temp) > 3 ? 'caution' : 'ok'
  };
  const prev = alarmPrev || (alarmPrev = { ...now });
  const COPY = {
    press: ['pressure back inside window · ' + f1(r.press) + ' kPa', 'pressure drifting · ' + f1(r.press) + ' kPa off set', 'pressure outside window · ' + f1(r.press) + ' kPa'],
    o2: ['O₂ restored · ' + f1(r.o2) + ' %', 'O₂ below 19.5 % · pump commanded open', 'O₂ deficit · ' + f1(r.o2) + ' % · breathable limit'],
    poll: ['pollutant cleared · back to zero', 'pollutant detected · ' + f2(r.poll + r.ch4) + ' % · filtration on', 'pollutant well over zero · ' + f2(r.poll + r.ch4) + ' %'],
    temp: ['room temp back on setpoint', 'temp deviation ' + f1(r.temp - t.temp) + ' °C · AC correcting', 'temp deviation ' + f1(r.temp - t.temp) + ' °C · AC at limit']
  };
  const RANK = { ok: 0, caution: 1, trip: 2 };
  Object.keys(now).forEach((k) => {
    if (now[k] === prev[k]) return;
    const i = RANK[now[k]];
    const tag = i === 2 ? 'TRIP' : i === 1 ? 'WATCH' : 'CLEAR';
    const color = i === 2 ? 'var(--cb-rust)' : i === 1 ? 'var(--cb-amber)' : 'var(--cb-live)';
    push(COPY[k][i], tag, color);
    prev[k] = now[k];
  });
}
function bump(key, d, min, max) { st.t[key] = clamp(Math.round((st.t[key] + d) * 100) / 100, min, max); render(true); }

// ---- the design's renderVals(), only the values the Hardsuit markup binds; a click is an index into acts[]
let acts = [];
const act = (fn) => { acts.push(fn); return ' data-act="' + (acts.length - 1) + '"'; };
const set = (patch) => { Object.assign(st, patch); render(true); };

function values() {
  const r = st.r, t = st.t;
  const nitro = !!props.nitrogenLine;
  const n2 = clamp(100 - r.o2 - r.co2 - r.poll - r.ch4 - (r.vol || 0) - (r.nox || 0) - (r.steam || 0), 0, 100);
  const tN2 = clamp(100 - t.o2 - t.co2, 0, 100);
  const o2Duty = clamp((t.o2 - r.o2) * 34, 0, 100);
  const co2Duty = clamp((t.co2 - r.co2) * 220, 0, 100);
  const filtLoad = clamp((r.co2 - t.co2) * 60 + r.poll * 90 + r.ch4 * 90, 0, 100);
  const dT = r.temp - t.temp;
  const acMode = Math.abs(dT) < 0.4 ? 'IDLE' : dT > 0 ? 'COOLING' : 'HEATING';
  const dP = r.press - t.press;
  const venting = r.press > t.trip || st.purging;
  const pressState = Math.abs(dP) > 20 ? 'trip' : Math.abs(dP) > 9 ? 'caution' : 'ok';
  const o2State = r.o2 < 17 ? 'trip' : r.o2 < 19.5 ? 'caution' : 'ok';
  const foreign = [{ n: 'Volatiles', v: r.vol || 0 }, { n: 'N₂O', v: r.nox || 0 }, { n: 'Steam', v: r.steam || 0 }];
  const detected = foreign.filter((g) => g.v > 0.005);
  const foreignSum = foreign.reduce((a, g) => a + g.v, 0);
  const foreignState = detected.some((g) => g.n !== 'Steam') ? 'trip' : detected.length ? 'caution' : 'ok';
  const pollState = (r.poll + r.ch4) > 0.1 ? 'trip' : (r.poll + r.ch4) > 0.001 ? 'caution' : 'ok';
  const tempState = Math.abs(dT) > 6 ? 'trip' : Math.abs(dT) > 3 ? 'caution' : 'ok';
  const worst = [pressState, o2State, pollState, tempState, foreignState];
  const anyTrip = worst.indexOf('trip') >= 0, anyCaution = worst.indexOf('caution') >= 0;
  const titles = { supply: 'Supply · gas lines', atmo: 'Atmo · ' + (props.roomName || 'hab core'), filter: 'Filtration · ' + (props.roomName || 'hab core'), alarm: 'Alarms · ' + (props.roomName || 'hab core'), config: 'Bindings · atmo regulator' };
  const stampOf = (s) => s === 'ok' ? 'no trip on record' : 'since ' + clock(Math.max(0, st.tick - 40));
  const selSet = SETPOINTS.find((s) => s.key === st.sp) || SETPOINTS[0];
  const lowestMol = Math.min(st.lines.o2.mol, st.lines.co2.mol, nitro ? st.lines.n2.mol : Infinity);
  const reserveTotal = st.lines.o2.mol + st.lines.co2.mol + (nitro ? st.lines.n2.mol : 0);
  const drawTotal = st.lines.o2.pump + st.lines.co2.pump + (nitro ? st.lines.n2.pump : 0);
  const bound = st.bound || {};
  const sel = ROLES.find((x) => x.key === st.role) || ROLES[0];
  const unbound = ROLES.filter((x) => !bound[x.key]).length;
  const hsRows = (g) => ROLES.filter((ro) => ro.group === g).map((ro) => {
    const b = bound[ro.key], on = ro.key === st.role;
    return {
      label: ro.key, bound: b || 'not bound',
      style: 'box-sizing:border-box;display:flex;align-items:center;gap:9px;height:28px;flex:none;margin-bottom:3px;padding:0 9px;border-radius:3px;cursor:pointer;user-select:none;background:'
        + (on ? 'var(--cb-wash-live)' : 'var(--cb-ground-raised)')
        + ';box-shadow:inset 4px 0 0 ' + (on ? 'var(--cb-live)' : b ? 'var(--cb-steel-300)' : 'var(--cb-amber)'),
      nameStyle: 'flex:1;min-width:0;font-size:12.5px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;color:'
        + (b ? 'var(--cb-text-2)' : 'var(--cb-text-3)'),
      pick: () => set({ role: ro.key })
    };
  });

  return {
    notConfig: st.tab !== 'config',
    hsTheme: st.dim ? 'dim' : 'lit',
    themeLabel: st.dim ? 'Dim' : 'Lit',
    themeLamp: st.dim ? 'var(--cb-steel-300)' : 'var(--cb-amber)',
    toggleTheme: () => set({ dim: !st.dim }),

    headTitle: titles[st.tab],
    headSub: st.tab === 'atmo' ? 'gas sensor · sampled each tick'
      : st.tab === 'supply' ? 'analyzer → volume pump → room'
      : st.tab === 'filter' ? 'room → filtration → waste tank'
      : 'six watched conditions · klaxon and strobe',
    headStatus: st.purging ? 'PURGING' : anyTrip ? 'TRIPPED' : anyCaution ? 'CORRECTING' : 'HOLDING',
    headToneColor: st.purging || anyTrip ? 'var(--cb-rust)' : anyCaution ? 'var(--cb-amber-ink)' : 'var(--cb-live-ink)',
    headLamp: st.purging || anyTrip ? 'var(--cb-rust)' : anyCaution ? 'var(--cb-amber)' : 'var(--cb-live)',
    headDotAnim: st.purging || anyTrip ? 'cb-blip .6s linear infinite'
      : anyCaution ? 'cb-blip 1.2s linear infinite' : 'none',

    tabItems: [{ id: 'atmo', label: 'Atmo' }, { id: 'supply', label: 'Supply' }, { id: 'filter', label: 'Filter' }, { id: 'alarm', label: 'Alarm' }, { id: 'config', label: '⚙', cog: true }]
      .map((tb) => ({
        label: tb.label,
        pick: () => set({ tab: tb.id }),
        style: 'display:flex;align-items:center;justify-content:center;height:100%;border-radius:3px;cursor:pointer;user-select:none;'
          + (tb.cog ? 'flex:none;width:62px;font:400 24px var(--cb-font-body);' : 'flex:1;font:700 19px var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;')
          + (st.tab === tb.id
            ? 'background:var(--hs-hazard);color:#1b1d16;box-shadow:inset 0 0 0 2px var(--cb-text-1),inset 0 -3px 0 rgba(0,0,0,.22)'
            : 'background:linear-gradient(180deg,var(--hs-plate-hi),var(--hs-plate) 60%,var(--hs-plate-lo));color:var(--cb-text-2);box-shadow:inset 0 0 0 2px var(--cb-text-1),0 2px 0 rgba(0,0,0,.18)')
      })),

    heroEdge: pressState === 'trip' ? 'var(--cb-rust)' : pressState === 'caution' ? 'var(--cb-amber)' : 'var(--cb-live)',
    heroInk: pressState === 'trip' ? 'var(--cb-rust)' : pressState === 'caution' ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
    heroBg: pressState === 'trip' ? 'rgba(179,36,26,.13)' : pressState === 'caution' ? 'var(--cb-wash-amber)' : 'var(--cb-ground-raised)',
    heroTag: venting ? 'VENTING' : pressState === 'trip' ? 'OUT OF WINDOW' : pressState === 'caution' ? 'CORRECTING' : 'ON TARGET',
    pressStr: f1(r.press),
    heroFillPct: clamp(r.press / t.trip * 100, 0, 100).toFixed(1) + '%',
    heroLine: 'set ' + f1(t.press) + ' kPa · trip ' + t.trip.toFixed(0) + ' · '
      + (venting ? 'safety vent dumping' : (dP > 0 ? 'bleeding ' : 'charging ') + f1(Math.abs(dP)) + ' kPa'),
    tempStr: f1(r.temp),
    tempEdge: tempState === 'trip' ? 'var(--cb-rust)' : tempState === 'caution' ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
    tempInk: tempState === 'trip' ? 'var(--cb-rust)' : tempState === 'caution' ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
    acLabel: 'AC · set ' + f1(t.temp) + ' °C',
    acWord: acMode,
    acInk: acMode === 'IDLE' ? 'var(--cb-text-3)' : 'var(--cb-live-ink)',
    mixNote: detected.length ? 'unexpected: ' + detected.map((g) => g.n).join(', ') : 'O₂ ' + f1(r.o2) + ' · N₂ ' + f1(n2) + ' · CO₂ ' + f2(r.co2) + ' · X ' + f2(r.poll + r.ch4) + ' %',
    gases: [
      { label: 'O₂', value: f1(r.o2), color: o2State === 'trip' ? 'var(--cb-rust)' : o2State === 'caution' ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
        fill: clamp(r.o2 / 30 * 100, 0, 100).toFixed(1) + '%', mark: clamp(t.o2 / 30 * 100, 0, 100).toFixed(1) + '%',
        bar: o2State === 'ok' ? 'var(--cb-live)' : o2State === 'caution' ? 'var(--cb-amber)' : 'var(--cb-rust)',
        note: 'target ' + f1(t.o2) + ' % · pump ' + o2Duty.toFixed(0) + ' %', scale: '30 %', sp: 'o2' },
      { label: 'N₂', value: f1(n2), color: 'var(--cb-text-1)',
        fill: clamp(n2, 0, 100).toFixed(1) + '%', mark: clamp(tN2, 0, 100).toFixed(1) + '%',
        bar: 'var(--cb-hairline)',
        note: nitro ? 'residual ' + f1(tN2) + ' % · N line live' : 'residual ' + f1(tN2) + ' % · N unbound', scale: '100 %' },
      { label: 'CO₂', value: f2(r.co2), color: r.co2 > t.co2 * 1.5 ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
        fill: clamp(r.co2 / 3 * 100, 0, 100).toFixed(1) + '%', mark: clamp(t.co2 / 3 * 100, 0, 100).toFixed(1) + '%',
        bar: r.co2 > t.co2 * 1.5 ? 'var(--cb-amber)' : 'var(--cb-live)',
        note: 'target ' + f2(t.co2) + ' % · pump ' + co2Duty.toFixed(0) + ' %', scale: '3 %', sp: 'co2' },
      { label: 'X · CH₄', value: f2(r.poll + r.ch4), color: pollState === 'trip' ? 'var(--cb-rust)' : pollState === 'caution' ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
        fill: clamp((r.poll + r.ch4) * 100, 0, 100).toFixed(1) + '%', mark: '0%',
        bar: pollState === 'trip' ? 'var(--cb-rust)' : pollState === 'caution' ? 'var(--cb-amber)' : 'var(--cb-hairline)',
        note: pollState === 'ok' ? 'ceiling zero · clear' : 'ceiling zero · filtration ' + filtLoad.toFixed(0) + ' %', scale: '1 %' },
      { label: 'Other', value: f2(foreignSum), color: foreignState === 'trip' ? 'var(--cb-rust)' : foreignState === 'caution' ? 'var(--cb-amber-ink)' : 'var(--cb-text-3)',
        fill: clamp(foreignSum * 100, 0, 100).toFixed(1) + '%', mark: '0%',
        bar: foreignState === 'trip' ? 'var(--cb-rust)' : foreignState === 'caution' ? 'var(--cb-amber)' : 'var(--cb-hairline)',
        note: detected.length ? detected.map((g) => g.n + ' ' + f2(g.v)).join(' · ') : 'no unexpected gas detected', scale: '1 %' }
    ].map((g) => {
      const on = g.sp && g.sp === st.sp;
      return Object.assign(g, {
        cursor: g.sp ? 'pointer' : 'default',
        bg: on ? 'var(--cb-wash-steel)' : 'transparent',
        mark2: on ? 'inset 3px 0 0 var(--cb-live)' : 'none',
        pick: g.sp ? () => set({ sp: g.sp }) : null,
        dotAnim: (g.sp === 'o2' && o2Duty > 1) || (g.sp === 'co2' && co2Duty > 1)
          ? 'cb-blip var(--cb-dur-blip) linear infinite' : 'none',
        dotColor: (g.sp === 'o2' && o2Duty > 1) || (g.sp === 'co2' && co2Duty > 1) ? 'var(--cb-live)' : 'transparent'
      });
    }),
    atmoFootA: anyTrip
      ? 'room outside limits · regulator correcting on ' + (o2Duty > 1 ? 'O₂' : co2Duty > 1 ? 'CO₂' : 'pressure')
      : anyCaution
        ? 'closing on target · ' + (o2Duty > 1 || co2Duty > 1 ? 'supply pumps open' : 'pumps idle')
        : 'holding target · all four gases inside their windows',
    atmoFootB: 'AC ' + acMode.toLowerCase() + ' · filtration ' + filtLoad.toFixed(0) + ' % · vent ' + (venting ? 'open' : 'closed'),
    setpointCards: SETPOINTS.map((sp) => {
      const on = sp.key === st.sp;
      return {
        label: sp.label, unit: sp.unit, value: sp.fmt(t[sp.field]),
        color: 'var(--cb-text-1)',
        style: 'box-sizing:border-box;flex:1;display:flex;flex-direction:column;gap:2px;padding:9px 12px 11px;border-radius:3px;cursor:pointer;user-select:none;border:2px solid '
          + (on ? 'var(--cb-steel-300)' : 'var(--cb-text-1)') + ';background:' + (on ? 'var(--cb-wash-steel)' : 'var(--cb-ground-raised)'),
        pick: () => set({ sp: sp.key })
      };
    }),
    selSetLabel: selSet.label, selSetUnit: selSet.unit, selSetNote: selSet.note,
    selSetValue: selSet.fmt(t[selSet.field]),
    setUp: () => bump(selSet.field, selSet.step, selSet.min, selSet.max),
    setDn: () => bump(selSet.field, -selSet.step, selSet.min, selSet.max),

    supplyNote: nitro ? 'three lines bound · analyzer sampled each tick' : 'N line unbound · O₂ and CO₂ sampled each tick',
    lines: [
      { key: 'o2', name: 'O₂', analyzer: 'O2-AN · supply line', live: true, duty: o2Duty },
      { key: 'co2', name: 'CO₂', analyzer: 'CO2-AN · supply line', live: true, duty: co2Duty },
      { key: 'n2', name: 'N₂', analyzer: nitro ? 'N-AN · supply line' : 'N-AN · not bound', live: nitro, duty: 0 }
    ].map((L) => {
      const d = st.lines[L.key], low = d.mol < 400, crit = d.mol < 200, cap = 4200;
      const bar = !L.live ? 'var(--cb-hairline)' : crit ? 'var(--cb-rust)' : low ? 'var(--cb-amber)' : 'var(--cb-steel-300)';
      return {
        head: L.name + ' · supply', analyzer: L.analyzer,
        fill: L.live ? clamp(d.mol / cap * 100, 0, 100).toFixed(0) : '0',
        barColor: bar,
        edge: !L.live ? 'var(--cb-hairline)' : crit ? 'var(--cb-rust)' : low ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
        bg: !L.live ? 'var(--cb-ground-well)' : crit ? 'rgba(179,36,26,.13)' : low ? 'var(--cb-wash-amber)' : 'var(--cb-ground-raised)',
        state: !L.live ? 'UNBOUND' : d.pump > 0 ? 'PUMPING' : 'STANDBY',
        stateColor: !L.live ? 'var(--cb-text-4)' : d.pump > 0 ? 'var(--cb-live-ink)' : 'var(--cb-text-3)',
        press: L.live ? d.press.toFixed(0) : '—',
        pressColor: !L.live ? 'var(--cb-text-4)' : low ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
        temp: L.live ? f1(d.temp) : '—',
        mol: L.live ? d.mol.toFixed(0) : '—',
        molColor: !L.live ? 'var(--cb-text-4)' : crit ? 'var(--cb-rust)' : low ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
        pump: L.live ? d.pump.toFixed(1) : '0.0',
        pumpColor: d.pump > 0 ? 'var(--cb-live-ink)' : 'var(--cb-text-4)',
        pumpNote: !L.live ? 'off' : d.pump > 0 ? 'duty ' + L.duty.toFixed(0) + ' %' : 'idle',
        duct: d.pump > 0 && L.live
          ? 'repeating-linear-gradient(90deg,var(--cb-amber) 0 7px,var(--cb-ground-well) 7px 14px)'
          : 'repeating-linear-gradient(90deg,var(--cb-hairline) 0 7px,transparent 7px 14px)',
        ductAnim: d.pump > 0 && L.live ? 'cb-march var(--cb-dur-march) linear infinite' : 'none'
      };
    }),
    supplyFootA: lowestMol < 400
      ? 'lowest tank ' + lowestMol.toFixed(0) + ' mol · refill before the room falls off target'
      : 'all bound tanks above the 400 mol watch line',
    supplyFootB: 'analyzers sampled each tick · ' + clock(),

    filtEdge: pollState === 'trip' ? 'var(--cb-rust)' : filtLoad > 70 ? 'var(--cb-amber)' : filtLoad > 5 ? 'var(--cb-live)' : 'var(--cb-text-1)',
    filtInk: pollState === 'trip' ? 'var(--cb-rust)' : filtLoad > 70 ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
    filtBg: pollState === 'trip' ? 'rgba(179,36,26,.13)' : filtLoad > 70 ? 'var(--cb-wash-amber)' : 'var(--cb-ground-raised)',
    filtTag: filtLoad > 5 ? 'RUNNING' : 'IDLE',
    filtStr: f2(filtLoad * 0.042),
    filtFillPct: clamp(filtLoad, 0, 100).toFixed(1) + '%',
    filtLine: f2(r.co2) + ' % CO₂ + ' + f2(r.poll + r.ch4) + ' % X drawn · out at ' + (60 + filtLoad * 0.6).toFixed(0) + ' kPa',
    nodeCards: [
      { label: 'Room', value: f1(r.press), unit: 'kPa', secondary: f1(r.temp), secondaryUnit: '°C', note: 'sensor · hab', hot: pressState !== 'ok', ink: pressState === 'trip' ? 'var(--cb-rust)' : 'var(--cb-text-1)', edge: pressState === 'trip' ? 'var(--cb-rust)' : 'var(--cb-text-1)' },
      { label: 'Intake', value: (r.press * 0.94).toFixed(1), unit: 'kPa', secondary: f1(r.temp - 0.4), secondaryUnit: '°C', note: 'passive vent', ink: 'var(--cb-text-1)', edge: 'var(--cb-text-1)' },
      { label: 'Filtration', value: (60 + filtLoad * 0.6).toFixed(0), unit: 'kPa', secondary: f1(r.temp + 2.1), secondaryUnit: '°C', note: filtLoad > 5 ? 'ON · 2 filters' : 'IDLE', ink: filtLoad > 70 ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)', edge: filtLoad > 70 ? 'var(--cb-amber)' : filtLoad > 5 ? 'var(--cb-live)' : 'var(--cb-text-1)' },
      { label: 'Out pump', value: (120 + filtLoad * 4).toFixed(0), unit: 'kPa', secondary: f1(r.temp + 3.4), secondaryUnit: '°C', note: (filtLoad * 0.9).toFixed(0) + ' L · waste', ink: 'var(--cb-text-1)', edge: 'var(--cb-text-1)' },
      { label: 'Waste', value: (4321 + Math.round(filtLoad * 3)).toString(), unit: 'kPa', secondary: '18.6', secondaryUnit: '°C', note: 'tank · vent 5000', ink: 'var(--cb-text-1)', edge: 'var(--cb-text-1)' }
    ].map((n, i, arr) => Object.assign({}, n, {
      arrow: i < arr.length - 1,
      duct: filtLoad > 5
        ? 'repeating-linear-gradient(90deg,var(--cb-amber) 0 7px,var(--cb-ground-well) 7px 14px)'
        : 'repeating-linear-gradient(90deg,var(--cb-hairline) 0 7px,transparent 7px 14px)',
      ductAnim: filtLoad > 5 ? 'cb-march var(--cb-dur-march) linear infinite' : 'none',
      style: 'box-sizing:border-box;flex:1;min-width:0;display:flex;flex-direction:column;gap:2px;padding:10px 12px 11px;border-radius:3px;border:2px solid ' + n.edge
        + ';background:' + (n.edge === 'var(--cb-amber)' ? 'var(--cb-wash-amber)' : n.edge === 'var(--cb-rust)' ? 'rgba(179,36,26,.13)' : 'var(--cb-ground-raised)')
    })),
    filterTiles: [
      { label: 'CH₄ filter', value: st.filters.ch4.toFixed(0), unit: '%', note: 'remaining', pct: st.filters.ch4 },
      { label: 'Pollutant filter', value: st.filters.poll.toFixed(0), unit: '%', note: 'remaining', pct: st.filters.poll },
      { label: 'Output pump', value: (filtLoad * 0.9).toFixed(0), unit: 'L', note: filtLoad > 5 ? 'to waste tank' : 'stopped', pct: filtLoad * 0.9, data: true }
    ].map((ft) => {
      const crit = !ft.data && ft.pct < 15, low = !ft.data && ft.pct < 35;
      return {
        label: ft.label, value: ft.value, unit: ft.unit, note: ft.note,
        fill: clamp(ft.pct, 0, 100).toFixed(1) + '%',
        bar: crit ? 'var(--cb-rust)' : low ? 'var(--cb-amber)' : 'var(--cb-steel-300)',
        ink: crit ? 'var(--cb-rust)' : low ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)',
        style: 'box-sizing:border-box;flex:1;display:flex;flex-direction:column;gap:2px;padding:10px 13px 12px;border-radius:3px;border:2px solid '
          + (crit ? 'var(--cb-rust)' : low ? 'var(--cb-amber-ink)' : 'var(--cb-text-1)')
          + ';background:' + (crit ? 'rgba(179,36,26,.13)' : low ? 'var(--cb-wash-amber)' : 'var(--cb-ground-raised)')
      };
    }),
    statRows: [
      { label: 'Filtration unit', value: filtLoad > 5 ? 'ON · AUTO' : 'IDLE', unit: '', tone: filtLoad > 5 ? 'live' : 'dim' },
      { label: 'Intake vent', value: (t.trip - 10).toFixed(0), unit: 'kPa', tone: 'ink' },
      { label: 'Safety vent · trip', value: st.purging ? 'PURGE DUMP' : venting ? 'DUMPING' : r.press > t.trip - 10 ? 'ARMED' : 'CLOSED', unit: '', tone: venting ? 'rust' : r.press > t.trip - 10 ? 'amber' : 'dim' },
      { label: 'Passive vent · AC', value: acMode + ' · set ' + f1(t.temp) + ' °C', unit: '', tone: acMode === 'IDLE' ? 'dim' : 'live' }
    ].map((sr, i) => ({
      label: sr.label, value: sr.value, unit: sr.unit,
      ink: sr.tone === 'rust' ? 'var(--cb-rust)' : sr.tone === 'amber' ? 'var(--cb-amber-ink)' : sr.tone === 'live' ? 'var(--cb-live-ink)' : sr.tone === 'dim' ? 'var(--cb-text-3)' : 'var(--cb-text-1)',
      style: 'flex:1;min-height:0;display:flex;align-items:center;justify-content:space-between;gap:10px;padding:0 13px'
        + (i ? ';box-shadow:inset 0 1px 0 var(--cb-hairline-inner)' : '')
    })),

    alarmTiles: [
      { label: 'Pressure', state: pressState, value: f1(r.press) + ' kPa', detail: 'Window ' + f1(t.press - 9) + '–' + f1(t.press + 9) + ' kPa, trip at ' + t.trip.toFixed(0) },
      { label: 'O₂ deficit', state: o2State, value: f1(r.o2) + ' %', detail: 'Watch below 19.5 %, trip below 17.0 %' },
      { label: 'Pollutant · CH₄', state: pollState, value: f2(r.poll + r.ch4) + ' %', detail: 'Ceiling is zero · any trace runs filtration' },
      { label: 'Temp deviation', state: tempState, value: f1(dT) + ' °C', detail: 'Deviation from ' + f1(t.temp) + ' °C setpoint' },
      { label: 'Unexpected gas', state: foreignState, value: f2(foreignSum) + ' %', detail: detected.length ? 'Detected: ' + detected.map((g) => g.n).join(', ') : 'No gas outside O₂ / N₂ / CO₂ in the room' },
      { label: 'Supply reserve', state: lowestMol < 200 ? 'trip' : lowestMol < 400 ? 'caution' : 'ok', value: lowestMol.toFixed(0) + ' mol', detail: 'Lowest bound tank · watch under 400 mol' }
    ].map((a) => ({
      label: a.label, value: a.value, detail: a.detail, stamp: stampOf(a.state),
      word: a.state === 'trip' ? 'TRIP' : a.state === 'caution' ? 'WATCH' : 'CLEAR',
      ink: a.state === 'trip' ? 'var(--cb-rust)' : a.state === 'caution' ? 'var(--cb-amber-ink)' : 'var(--cb-live-ink)',
      style: 'box-sizing:border-box;display:flex;flex-direction:column;gap:3px;padding:11px 13px 12px;border-radius:3px;border:2px solid '
        + (a.state === 'trip' ? 'var(--cb-rust)' : a.state === 'caution' ? 'var(--cb-amber)' : 'var(--cb-live)')
        + ';background:' + (a.state === 'trip' ? 'rgba(179,36,26,.13)' : a.state === 'caution' ? 'var(--cb-wash-amber)' : 'var(--cb-ground-raised)')
    })),
    log: st.log, logNote: clock() + ' · ' + st.log.length + ' retained',
    soundLabel: 'SOUND ' + (st.sound ? 'ARMED' : 'MUTED'),
    lightLabel: 'LIGHT ' + (st.light ? 'ARMED' : 'OFF'),
    toggleSound: () => { const was = st.sound; st.sound = !st.sound; push(was ? 'sound alarm muted at console' : 'sound alarm armed', 'ALARM', 'var(--cb-amber)'); render(true); },
    toggleLight: () => { const was = st.light; st.light = !st.light; push(was ? 'light alarm disabled' : 'light alarm armed', 'ALARM', 'var(--cb-amber)'); render(true); },
    purge: () => { st.purging = true; push('room purge commanded · safety vent open', 'PURGE', 'var(--cb-rust)'); render(true); },
    soundBtn: 'box-sizing:border-box;height:60px;display:flex;align-items:center;justify-content:center;border-radius:3px;font:700 21px var(--cb-font-display);letter-spacing:.12em;text-transform:uppercase;cursor:pointer;user-select:none;border:2px solid '
      + (st.sound ? 'var(--cb-text-1);background:var(--cb-ground-raised);color:var(--cb-text-1)' : 'var(--cb-hairline);background:var(--cb-ground-well);color:var(--cb-text-4)'),
    lightBtn: 'box-sizing:border-box;height:60px;display:flex;align-items:center;justify-content:center;border-radius:3px;font:700 21px var(--cb-font-display);letter-spacing:.12em;text-transform:uppercase;cursor:pointer;user-select:none;border:2px solid '
      + (st.light ? 'var(--cb-text-1);background:var(--cb-ground-raised);color:var(--cb-text-1)' : 'var(--cb-hairline);background:var(--cb-ground-well);color:var(--cb-text-4)'),
    purgeBtn: 'box-sizing:border-box;height:60px;display:flex;align-items:center;justify-content:center;border-radius:3px;font:700 21px var(--cb-font-display);letter-spacing:.12em;text-transform:uppercase;cursor:pointer;user-select:none;border:2px solid var(--cb-text-1);'
      + (st.purging ? 'background:var(--cb-ground-well);color:var(--cb-text-4);border-color:var(--cb-hairline)' : 'background:var(--cb-rust);color:#fdf4f3'),

    rowsGas: hsRows('gas'), rowsFilt: hsRows('filt'), rowsCtl: hsRows('ctl'),
    selLabel: sel.key, selSub: sel.sub,
    selCount: sel.devs.length + ' matching devices on this network',
    candidates: sel.devs.map((dv) => {
      const on = bound[sel.key] === dv.name;
      const taken = !on && Object.keys(bound).some((k) => bound[k] === dv.name);
      return {
        name: dv.name, meta: 'ReferenceId ' + dv.id + ' · ' + dv.meta,
        tag: on ? 'BOUND' : (taken ? 'IN USE' : 'SELECT'),
        style: 'box-sizing:border-box;display:flex;align-items:center;justify-content:space-between;gap:10px;padding:9px 12px;border-radius:3px;cursor:pointer;user-select:none;border:2px solid '
          + (on ? 'var(--cb-live)' : 'var(--cb-hairline)') + ';background:' + (on ? 'var(--cb-wash-live)' : 'var(--cb-ground-raised)'),
        tagStyle: 'flex:none;font:700 14px var(--cb-font-display);letter-spacing:.12em;color:'
          + (on ? 'var(--cb-live-ink)' : taken ? 'var(--cb-text-4)' : 'var(--cb-text-2)'),
        bind: () => { bound[sel.key] = bound[sel.key] === dv.name ? null : dv.name; set({ dirty: true }); }
      };
    }),
    unboundLabel: unbound ? unbound + (unbound === 1 ? ' ROLE UNBOUND' : ' ROLES UNBOUND') : 'ALL ' + ROLES.length + ' ROLES BOUND',
    unboundColor: unbound ? 'var(--cb-amber-ink)' : 'var(--cb-live-ink)',
    writeLabel: st.dirty ? 'WRITE BINDINGS' : 'BINDINGS WRITTEN',
    writeStyle: 'box-sizing:border-box;display:flex;align-items:center;justify-content:center;border-radius:3px;font:700 16px var(--cb-font-display);letter-spacing:.1em;cursor:pointer;user-select:none;'
      + (st.dirty
        ? 'background:var(--cb-live);color:var(--cb-on-accent);border:2px solid var(--cb-text-1)'
        : 'background:var(--cb-ground-well);color:var(--cb-text-4);border:2px solid var(--cb-hairline)'),
    exitConfig: () => set({ tab: 'atmo' }),
    rescan: () => { push('device rescan · ' + ROLES.length + ' roles on the network', 'CONFIG', 'var(--cb-text-4)'); render(true); },
    writeBindings: () => { st.dirty = false; push('bindings written to regulator · ' + (ROLES.length - unbound) + ' devices', 'CONFIG', 'var(--cb-live)'); render(true); }
  };
}

// ---- the design's markup as strings: every element and inline style verbatim, {{ }} filled in, onClick -> data-act
function renderHeader(v) {
  return '<div style="flex:none;display:flex;align-items:center;justify-content:space-between;gap:12px;height:48px;padding:0 2px">'
    + '<span style="display:flex;align-items:baseline;gap:12px;min-width:0">'
    + '<span style="font:700 27px var(--cb-font-display);letter-spacing:.1em;text-transform:uppercase;color:var(--cb-text-1);white-space:nowrap">' + v.headTitle + '</span>'
    + '<span style="font:500 14px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3);white-space:nowrap">' + v.headSub + '</span>'
    + '</span>'
    + '<span style="display:flex;align-items:center;gap:9px;font:700 19px var(--cb-font-display);letter-spacing:.12em;text-transform:uppercase;color:' + v.headToneColor + ';white-space:nowrap">'
    + '<span style="width:11px;height:11px;border-radius:2px;background:' + v.headLamp + ';animation:' + v.headDotAnim + '"></span>' + v.headStatus + '</span>'
    + '</div>';
}

function renderAtmo(v) {
  const gasRows = v.gases.map((g) => '<div' + (g.pick ? act(g.pick) : '') + ' style="display:grid;grid-template-columns:88px 88px 1fr 176px;align-items:center;gap:12px;flex:1;min-height:0;padding:0 13px 0 11px;cursor:' + g.cursor + ';background:' + g.bg + ';box-shadow:' + g.mark2 + '">'
    + '<span style="display:flex;align-items:center;gap:7px;font:700 17px/1 var(--cb-font-display);letter-spacing:.05em;color:var(--cb-text-1);white-space:nowrap"><span style="width:9px;height:9px;flex:none;border-radius:2px;background:' + g.dotColor + ';animation:' + g.dotAnim + '"></span>' + g.label + '</span>'
    + '<span style="display:flex;align-items:baseline;justify-content:flex-end;gap:4px">'
    + '<span style="font:600 25px/1 var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + g.color + '">' + g.value + '</span>'
    + '<span style="font-size:11.5px;color:var(--cb-text-3)">%</span>'
    + '</span>'
    + '<span style="position:relative;height:6px;border-radius:3px;background:var(--cb-ground-well)">'
    + '<span style="position:absolute;left:0;top:0;bottom:0;width:' + g.fill + ';border-radius:3px;background:' + g.bar + '"></span>'
    + '<span style="position:absolute;top:-4px;bottom:-4px;left:' + g.mark + ';width:2px;border-radius:1px;background:var(--cb-steel-300)"></span>'
    + '</span>'
    + '<span style="font-size:12px;color:var(--cb-text-2);text-align:right;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + g.note + '</span>'
    + '</div>').join('');
  const cards = v.setpointCards.map((s) => '<div' + act(s.pick) + ' style="' + s.style + '">'
    + '<span style="font:500 11.5px var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;color:var(--cb-text-3);white-space:nowrap">' + s.label + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:3px">'
    + '<span style="font:700 21px/1 var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + s.color + '">' + s.value + '</span>'
    + '<span style="font-size:11px;color:var(--cb-text-3)">' + s.unit + '</span>'
    + '</span>'
    + '</div>').join('');
  return '<div style="flex:1;display:flex;flex-direction:column;gap:10px;min-height:0">'
    + '<div style="flex:none;display:flex;gap:10px">'
    + '<div style="flex:1;min-width:0;box-sizing:border-box;border-radius:3px;padding:12px 16px 14px;border:2px solid ' + v.heroEdge + ';background:' + v.heroBg + '">'
    + '<div style="display:flex;align-items:center;justify-content:space-between;gap:10px">'
    + '<span style="font:600 15px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">Room pressure</span>'
    + '<span style="font:700 15px var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;color:' + v.heroInk + '">' + v.heroTag + '</span>'
    + '</div>'
    + '<div style="display:flex;align-items:baseline;gap:8px">'
    + '<span style="font:600 58px/1.02 var(--cb-font-display);letter-spacing:-.02em;font-variant-numeric:tabular-nums;color:' + v.heroInk + '">' + v.pressStr + '</span>'
    + '<span style="font:500 16px var(--cb-font-display);letter-spacing:.06em;color:var(--cb-text-3)">kPa</span>'
    + '</div>'
    + '<div style="font:500 13px var(--cb-font-display);letter-spacing:.08em;text-transform:uppercase;color:var(--cb-text-2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + v.heroLine + '</div>'
    + '<div style="height:6px;border-radius:3px;margin-top:8px;background:var(--cb-ground-well);overflow:hidden">'
    + '<div style="height:100%;width:' + v.heroFillPct + ';background:' + v.heroEdge + '"></div>'
    + '</div>'
    + '</div>'
    + '<div style="flex:none;width:196px;display:flex;flex-direction:column;gap:10px">'
    + '<div style="flex:1;box-sizing:border-box;border-radius:3px;padding:10px 13px;border:2px solid ' + v.tempEdge + ';background:var(--cb-ground-raised);display:flex;flex-direction:column;justify-content:center">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">Room temp</span>'
    + '<span style="display:flex;align-items:baseline;gap:4px">'
    + '<span style="font:700 34px/1 var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + v.tempInk + '">' + v.tempStr + '</span>'
    + '<span style="font-size:13px;color:var(--cb-text-3)">&deg;C</span>'
    + '</span>'
    + '</div>'
    + '<div style="flex:1;box-sizing:border-box;border-radius:3px;padding:10px 13px;border:2px solid var(--cb-text-1);background:var(--cb-ground-raised);display:flex;flex-direction:column;justify-content:center">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">' + v.acLabel + '</span>'
    + '<span style="font:700 26px/1 var(--cb-font-display);letter-spacing:.06em;color:' + v.acInk + '">' + v.acWord + '</span>'
    + '</div>'
    + '</div>'
    + '</div>'
    + '<div style="flex:1;min-height:0;display:flex;flex-direction:column;overflow:hidden;border-radius:3px;border:2px solid var(--cb-text-1);background:var(--cb-ground-raised)">'
    + '<div style="flex:none;display:flex;justify-content:space-between;align-items:baseline;gap:10px;padding:9px 13px 7px">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">Composition &middot; measured against target</span>'
    + '<span style="font-size:12px;color:var(--cb-text-3);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + v.mixNote + '</span>'
    + '</div>'
    + gasRows
    + '<div style="flex:none;display:flex;align-items:baseline;justify-content:space-between;gap:10px;padding:8px 13px 10px;box-shadow:inset 0 1px 0 var(--cb-hairline-inner)">'
    + '<span style="font-size:13px;color:var(--cb-text-2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + v.atmoFootA + '</span>'
    + '<span style="font-size:13px;color:var(--cb-text-3);white-space:nowrap">' + v.atmoFootB + '</span>'
    + '</div>'
    + '</div>'
    + '<div style="flex:none;display:flex;gap:8px">' + cards + '</div>'
    + '<div style="flex:none;box-sizing:border-box;display:flex;align-items:center;gap:16px;border-radius:3px;border:2px solid var(--cb-steel-300);background:var(--cb-wash-steel);padding:9px 13px">'
    + '<span style="flex:1;min-width:0;display:flex;flex-direction:column">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-2)">' + v.selSetLabel + '</span>'
    + '<span style="font-size:12.5px;line-height:1.4;color:var(--cb-text-2);text-wrap:pretty">' + v.selSetNote + '</span>'
    + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:4px;flex:none">'
    + '<span style="font:700 34px/1 var(--cb-font-display);font-variant-numeric:tabular-nums;color:var(--cb-text-1)">' + v.selSetValue + '</span>'
    + '<span style="font-size:13px;color:var(--cb-text-3)">' + v.selSetUnit + '</span>'
    + '</span>'
    + '<span style="display:flex;gap:6px;flex:none">'
    + '<span' + act(v.setDn) + ' style="width:50px;height:42px;box-sizing:border-box;display:flex;align-items:center;justify-content:center;border-radius:3px;border:2px solid var(--cb-text-1);background:var(--cb-ground-raised);color:var(--cb-text-1);font:700 24px var(--cb-font-display);cursor:pointer;user-select:none">&minus;</span>'
    + '<span' + act(v.setUp) + ' style="width:50px;height:42px;box-sizing:border-box;display:flex;align-items:center;justify-content:center;border-radius:3px;border:2px solid var(--cb-text-1);background:var(--cb-ground-raised);color:var(--cb-text-1);font:700 24px var(--cb-font-display);cursor:pointer;user-select:none">+</span>'
    + '</span>'
    + '</div>'
    + '</div>';
}

function renderSupply(v) {
  const lines = v.lines.map((l) => '<div style="box-sizing:border-box;border-radius:3px;border:2px solid ' + l.edge + ';background:' + l.bg + ';padding:11px 14px;display:flex;flex-direction:column">'
    + '<div style="display:flex;align-items:baseline;justify-content:space-between;gap:8px">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3);white-space:nowrap">' + l.head + '</span>'
    + '<span style="font:700 12px var(--cb-font-display);letter-spacing:.12em;color:' + l.stateColor + '">' + l.state + '</span>'
    + '</div>'
    + '<div style="display:flex;align-items:baseline;gap:5px"><span style="font:600 42px/1 var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + l.molColor + '">' + l.mol + '</span><span style="font:500 16px var(--cb-font-display);color:var(--cb-text-3)">mol</span></div>'
    + '<div style="height:5px;border-radius:3px;margin:4px 0 6px;background:var(--cb-ground-well);overflow:hidden"><div style="height:100%;width:' + l.fill + '%;background:' + l.barColor + '"></div></div>'
    + '<div style="font-size:11.5px;color:var(--cb-text-3);margin-bottom:5px">' + l.analyzer + '</div>'
    + '<div style="margin-top:auto;display:flex;align-items:baseline;justify-content:space-between;padding:5px 0;box-shadow:inset 0 1px 0 var(--cb-hairline-inner)"><span style="font-size:12.5px;color:var(--cb-text-2)">Pressure</span><span style="font:600 17px var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + l.pressColor + '">' + l.press + ' kPa</span></div>'
    + '<div style="display:flex;align-items:baseline;justify-content:space-between;padding:5px 0;box-shadow:inset 0 1px 0 var(--cb-hairline-inner)"><span style="font-size:12.5px;color:var(--cb-text-2)">Temp</span><span style="font:600 17px var(--cb-font-display);font-variant-numeric:tabular-nums;color:var(--cb-text-1)">' + l.temp + ' &deg;C</span></div>'
    + '<div style="display:flex;align-items:baseline;justify-content:space-between;padding:5px 0;box-shadow:inset 0 1px 0 var(--cb-hairline-inner)"><span style="font-size:12.5px;color:var(--cb-text-2)">Fill</span><span style="font:600 17px var(--cb-font-display);font-variant-numeric:tabular-nums;color:var(--cb-text-1)">' + l.fill + ' %</span></div>'
    + '<div style="display:flex;align-items:baseline;justify-content:space-between;padding:5px 0;box-shadow:inset 0 1px 0 var(--cb-hairline-inner)"><span style="font-size:12.5px;color:var(--cb-text-2)">Volume pump</span><span style="font:600 17px var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + l.pumpColor + '">' + l.pump + ' L</span></div>'
    + '<div style="display:flex;align-items:center;gap:7px;padding-top:7px;box-shadow:inset 0 1px 0 var(--cb-hairline-inner)">'
    + '<span style="width:44px;height:4px;border-radius:2px;flex:none;background:' + l.duct + ';animation:' + l.ductAnim + '"></span>'
    + '<span style="font-size:11.5px;color:var(--cb-text-3)">' + l.pumpNote + '</span>'
    + '</div>'
    + '</div>').join('');
  return '<div style="flex:1;display:flex;flex-direction:column;gap:10px;min-height:0">'
    + '<div style="flex:none;display:flex;align-items:baseline;justify-content:space-between;gap:12px;padding:0 2px">'
    + '<span style="font:600 15px var(--cb-font-display);letter-spacing:.18em;text-transform:uppercase;color:var(--cb-text-2)">Supply tanks &middot; analyzer &rarr; volume pump &rarr; room</span>'
    + '<span style="font:500 13px var(--cb-font-display);letter-spacing:.1em;color:var(--cb-text-3)">' + v.supplyNote + '</span>'
    + '</div>'
    + '<div style="flex:1;display:grid;grid-template-columns:repeat(3,1fr);gap:8px;min-height:0">' + lines + '</div>'
    + '<div style="flex:none;display:flex;align-items:baseline;justify-content:space-between;gap:12px;padding:0 2px">'
    + '<span style="font-size:13.5px;color:var(--cb-text-2)">' + v.supplyFootA + '</span>'
    + '<span style="font-size:13.5px;color:var(--cb-text-3)">' + v.supplyFootB + '</span>'
    + '</div>'
    + '</div>';
}

function renderFilter(v) {
  const nodes = v.nodeCards.map((n) => '<div style="' + n.style + '">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + n.label + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:5px">'
    + '<span style="font:700 30px/1 var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + n.ink + '">' + n.value + '</span>'
    + '<span style="font-size:12.5px;color:var(--cb-text-3)">' + n.unit + '</span>'
    + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:5px">'
    + '<span style="font:600 15px var(--cb-font-display);color:var(--cb-text-1)">' + n.secondary + '</span>'
    + '<span style="font-size:12px;color:var(--cb-text-4)">' + n.secondaryUnit + '</span>'
    + '</span>'
    + '<span style="font-size:12px;color:var(--cb-text-3);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + n.note + '</span>'
    + (n.arrow ? '<span style="height:4px;border-radius:2px;margin-top:5px;background:' + n.duct + ';animation:' + n.ductAnim + '"></span>' : '')
    + '</div>').join('');
  const tiles = v.filterTiles.map((ft) => '<div style="' + ft.style + '">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">' + ft.label + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:4px">'
    + '<span style="font:700 30px/1 var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + ft.ink + '">' + ft.value + '</span>'
    + '<span style="font-size:13px;color:var(--cb-text-3)">' + ft.unit + '</span>'
    + '</span>'
    + '<span style="font-size:12px;color:var(--cb-text-3)">' + ft.note + '</span>'
    + '<span style="height:5px;border-radius:3px;margin-top:6px;background:var(--cb-ground-well);overflow:hidden"><span style="display:block;height:100%;width:' + ft.fill + ';background:' + ft.bar + '"></span></span>'
    + '</div>').join('');
  const stats = v.statRows.map((sr) => '<div style="' + sr.style + '">'
    + '<span style="font:600 15px var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;color:var(--cb-text-2)">' + sr.label + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:5px">'
    + '<span style="font:700 21px var(--cb-font-display);letter-spacing:.05em;font-variant-numeric:tabular-nums;color:' + sr.ink + '">' + sr.value + '</span>'
    + '<span style="font-size:12.5px;color:var(--cb-text-3)">' + sr.unit + '</span>'
    + '</span>'
    + '</div>').join('');
  return '<div style="flex:1;display:flex;flex-direction:column;gap:10px;min-height:0">'
    + '<div style="flex:none;box-sizing:border-box;border-radius:3px;border:2px solid ' + v.filtEdge + ';background:' + v.filtBg + ';padding:12px 16px 14px">'
    + '<div style="display:flex;align-items:center;justify-content:space-between;gap:10px">'
    + '<span style="font:600 15px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">Filtration throughput</span>'
    + '<span style="font:700 15px var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;color:' + v.filtInk + '">' + v.filtTag + '</span>'
    + '</div>'
    + '<div style="display:flex;align-items:baseline;gap:8px">'
    + '<span style="font:600 54px/1.02 var(--cb-font-display);letter-spacing:-.02em;font-variant-numeric:tabular-nums;color:' + v.filtInk + '">' + v.filtStr + '</span>'
    + '<span style="font:500 16px var(--cb-font-display);letter-spacing:.06em;color:var(--cb-text-3)">mol/s</span>'
    + '</div>'
    + '<div style="font:500 13px var(--cb-font-display);letter-spacing:.08em;text-transform:uppercase;color:var(--cb-text-2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + v.filtLine + '</div>'
    + '<div style="height:6px;border-radius:3px;margin-top:8px;background:var(--cb-ground-well);overflow:hidden">'
    + '<div style="height:100%;width:' + v.filtFillPct + ';background:' + v.filtEdge + '"></div>'
    + '</div>'
    + '</div>'
    + '<div style="flex:none;display:flex;align-items:stretch;gap:6px">' + nodes + '</div>'
    + '<div style="flex:none;display:flex;gap:6px">' + tiles + '</div>'
    + '<div style="flex:1;min-height:0;display:flex;flex-direction:column;overflow:hidden;border-radius:3px;border:2px solid var(--cb-text-1);background:var(--cb-ground-raised)">' + stats + '</div>'
    + '</div>';
}

function renderAlarm(v) {
  const tiles = v.alarmTiles.map((a) => '<div style="' + a.style + '">'
    + '<span style="display:flex;align-items:baseline;justify-content:space-between;gap:8px">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + a.label + '</span>'
    + '<span style="flex:none;font:700 13px var(--cb-font-display);letter-spacing:.12em;color:' + a.ink + '">' + a.word + '</span>'
    + '</span>'
    + '<span style="font:700 27px/1.05 var(--cb-font-display);font-variant-numeric:tabular-nums;color:' + a.ink + '">' + a.value + '</span>'
    + '<span style="font-size:12.5px;line-height:1.35;color:var(--cb-text-2)">' + a.detail + '</span>'
    + '<span style="font-size:11.5px;color:var(--cb-text-4);margin-top:auto">' + a.stamp + '</span>'
    + '</div>').join('');
  const log = v.log.map((e) => '<div style="display:grid;grid-template-columns:64px 1fr auto;gap:11px;align-items:baseline;padding:7px 13px;box-shadow:inset 0 1px 0 var(--cb-hairline-inner)">'
    + '<span style="font:600 12.5px var(--cb-font-display);letter-spacing:.12em;color:var(--cb-text-3)">' + e.t + '</span>'
    + '<span style="font-size:12.5px;color:var(--cb-text-1)">' + e.msg + '</span>'
    + '<span style="font:700 12.5px var(--cb-font-display);letter-spacing:.12em;color:' + e.color + '">' + e.tag + '</span>'
    + '</div>').join('');
  return '<div style="flex:1;display:flex;flex-direction:column;gap:10px;min-height:0">'
    + '<div style="flex:none;display:grid;grid-template-columns:repeat(3,1fr);gap:6px">' + tiles + '</div>'
    + '<div style="flex:1;min-height:0;display:flex;flex-direction:column;overflow:hidden;border-radius:3px;border:2px solid var(--cb-text-1);background:var(--cb-ground-raised)">'
    + '<div style="flex:none;display:flex;justify-content:space-between;align-items:baseline;gap:10px;padding:9px 13px 6px">'
    + '<span style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">Event log</span>'
    + '<span style="font-size:12px;color:var(--cb-text-3)">' + v.logNote + '</span>'
    + '</div>'
    + log
    + '</div>'
    + '<div style="flex:none;display:grid;grid-template-columns:1fr 1fr 1fr;gap:6px">'
    + '<div' + act(v.toggleSound) + ' style="' + v.soundBtn + '">' + v.soundLabel + '</div>'
    + '<div' + act(v.toggleLight) + ' style="' + v.lightBtn + '">' + v.lightLabel + '</div>'
    + '<div' + act(v.purge) + ' style="' + v.purgeBtn + '">PURGE ROOM</div>'
    + '</div>'
    + '</div>';
}

function renderConfig(v) {
  const rows = (list) => list.map((r) => '<div' + act(r.pick) + ' style="' + r.style + '"><span style="font:700 15px var(--cb-font-display);letter-spacing:.06em;width:92px;flex:none;color:var(--cb-text-1)">' + r.label + '</span><span style="' + r.nameStyle + '">' + r.bound + '</span></div>').join('');
  const candidates = v.candidates.map((c) => '<div' + act(c.bind) + ' style="' + c.style + '">'
    + '<span style="display:flex;flex-direction:column;min-width:0">'
    + '<span style="font:700 19px var(--cb-font-display);letter-spacing:.05em;color:var(--cb-text-1)">' + c.name + '</span>'
    + '<span style="font-size:12px;color:var(--cb-text-3);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + c.meta + '</span>'
    + '</span>'
    + '<span style="' + c.tagStyle + '">' + c.tag + '</span>'
    + '</div>').join('');
  return '<div style="flex:1;display:flex;flex-direction:column;min-height:0;overflow:hidden;gap:8px">'
    + '<div style="flex:none;display:flex;align-items:center;justify-content:space-between;gap:12px;height:44px;padding:0 2px">'
    + '<span style="font:600 18px var(--cb-font-display);letter-spacing:.18em;text-transform:uppercase;color:var(--cb-text-2)">Configuration &middot; cog to exit</span>'
    + '<span style="display:flex;align-items:center;gap:12px">'
    + '<span style="font:600 18px var(--cb-font-display);letter-spacing:.12em;color:' + v.unboundColor + ';white-space:nowrap">' + v.unboundLabel + '</span>'
    + '<span' + act(v.toggleTheme) + ' style="box-sizing:border-box;display:flex;align-items:center;gap:6px;height:28px;padding:0 11px;border-radius:3px;border:2px solid var(--cb-hairline);background:var(--hs-theme-btn-bg);font:700 14px var(--cb-font-display);letter-spacing:.12em;text-transform:uppercase;color:var(--cb-text-2);cursor:pointer;user-select:none">'
    + '<span style="width:9px;height:9px;border-radius:2px;background:' + v.themeLamp + '"></span>' + v.themeLabel + '</span>'
    + '</span>'
    + '</div>'
    + '<div style="flex:1;display:flex;gap:8px;min-height:0">'
    + '<div style="width:296px;flex:none;display:flex;flex-direction:column;min-height:0;overflow:hidden">'
    + '<div style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3);margin:2px 0 4px">Gas side</div>'
    + rows(v.rowsGas)
    + '<div style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3);margin:8px 0 4px">Filtration</div>'
    + rows(v.rowsFilt)
    + '<div style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3);margin:8px 0 4px">Atmos &amp; alarms</div>'
    + rows(v.rowsCtl)
    + '</div>'
    + '<div style="flex:1;display:flex;flex-direction:column;gap:8px;min-height:0">'
    + '<div style="flex:none;padding-bottom:8px;box-shadow:inset 0 -2px 0 var(--cb-hairline-inner)">'
    + '<div style="font:700 30px var(--cb-font-display);letter-spacing:.05em;color:var(--cb-text-1)">' + v.selLabel + '</div>'
    + '<div style="font-size:12.5px;color:var(--cb-text-2)">' + v.selSub + '</div>'
    + '</div>'
    + '<div style="flex:none;font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">' + v.selCount + '</div>'
    + '<div style="flex:none;display:flex;flex-direction:column;gap:6px">' + candidates + '</div>'
    + '<div style="margin-top:auto;flex:none;box-sizing:border-box;border-radius:3px;border:2px solid var(--cb-hairline);background:var(--cb-ground-raised);padding:11px 13px">'
    + '<div style="font:600 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">How binding works</div>'
    + '<div style="font-size:12.5px;line-height:1.5;color:var(--cb-text-2);margin-top:3px;text-wrap:pretty">Roles resolve by prefab + name hash, so only devices of the matching prefab are listed. The regulator keeps running on the old binding until you write.</div>'
    + '</div>'
    + '</div>'
    + '</div>'
    + '<div style="flex:none;display:grid;grid-template-columns:1fr 1fr 1.2fr;gap:6px;height:52px">'
    + '<div' + act(v.exitConfig) + ' style="box-sizing:border-box;display:flex;align-items:center;justify-content:center;border-radius:3px;border:2px solid var(--cb-hairline);background:var(--cb-ground-raised);font:700 16px var(--cb-font-display);letter-spacing:.1em;color:var(--cb-text-2);cursor:pointer;user-select:none">RETURN</div>'
    + '<div' + act(v.rescan) + ' style="box-sizing:border-box;display:flex;align-items:center;justify-content:center;border-radius:3px;border:2px solid var(--cb-steel-300);background:var(--cb-ground-raised);font:700 16px var(--cb-font-display);letter-spacing:.1em;color:var(--cb-steel-300);cursor:pointer;user-select:none">RESCAN DEVICES</div>'
    + '<div' + act(v.writeBindings) + ' style="' + v.writeStyle + '">' + v.writeLabel + '</div>'
    + '</div>'
    + '</div>';
}

function renderTabs(v) {
  return '<div style="flex:none;display:flex;gap:8px;height:52px">'
    + v.tabItems.map((tb) => '<div' + act(tb.pick) + ' style="' + tb.style + '">' + tb.label + '</div>').join('')
    + '</div>';
}

function render(structural) {
  // the Bindings screen shows nothing the simulation changes: it re-renders on its own clicks, not on the tick
  if (!structural && st.tab === 'config') return;
  const v = values();
  acts = [];
  const panel = document.getElementById('panel');
  panel.setAttribute('data-hs-theme', v.hsTheme);
  panel.innerHTML = (v.notConfig ? renderHeader(v) : '')
    + (st.tab === 'atmo' ? renderAtmo(v) : st.tab === 'supply' ? renderSupply(v) : st.tab === 'filter' ? renderFilter(v) : st.tab === 'alarm' ? renderAlarm(v) : renderConfig(v))
    + (v.notConfig ? renderTabs(v) : '');
  const bound = document.querySelectorAll('[data-act]');
  for (let i = 0; i < bound.length; i++) {
    const el = bound[i], idx = Number(el.getAttribute('data-act'));
    el.addEventListener('click', () => acts[idx]());
  }
}
render(true);
setInterval(step, 420);
</script>
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

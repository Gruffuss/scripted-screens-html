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
<script>
// ---- the design's data model, unchanged
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

// ---- the design's state and simulation, setState replaced by a render call
const props = { scenario: 'correcting', nitrogenLine: false, simSpeed: 1, roomName: 'hab core' };
const st = {
  tab: ((typeof location !== 'undefined' && location.hash) || '#atmo').slice(1) || 'atmo',
  tick: 0,
  r: { ...(SCEN[props.scenario] || SCEN.correcting) },
  t: { press: 101.3, temp: 21.0, o2: 21.0, co2: 0.30, trip: 140 },
  filters: { ch4: 76, poll: 62 },
  sound: true,
  light: true,
  purging: false,
  role: 'N-AN',
  sp: 'o2',
  lines: {
    o2:  { temp: 11.8, press: 3140, mol: 2412, pump: 6.2 },
    co2: { temp: 14.2, press: 1880, mol: 1046, pump: 1.4 },
    n2:  { temp: 9.6,  press: 4270, mol: 3318, pump: 0 }
  },
  dirty: false,
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
  render();
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
function bump(key, d, min, max) { st.t[key] = clamp(Math.round((st.t[key] + d) * 100) / 100, min, max); render(); }

// ---- rendering: the design's markup and the components' JSX as strings; a click handler is an index into acts[]
let acts = [];
const act = (fn) => { acts.push(fn); return ' data-act="' + (acts.length - 1) + '"'; };

// components/shell/CornerMarks.jsx
const MARK_POS = {
  tl: 'left:var(--cb-mark-offset);top:var(--cb-mark-offset)',
  tr: 'right:var(--cb-mark-offset);top:var(--cb-mark-offset)',
  bl: 'left:var(--cb-mark-offset);bottom:var(--cb-mark-offset)',
  br: 'right:var(--cb-mark-offset);bottom:var(--cb-mark-offset)'
};
function cornerMarks(corners, color) {
  color = color || 'var(--cb-mark)';
  const line = 'linear-gradient(' + color + ',' + color + ')';
  return (corners || ['tl', 'tr', 'bl', 'br']).map((c) => '<i style="position:absolute;width:var(--cb-mark-size);height:var(--cb-mark-size);pointer-events:none;background:' + line + ' 6px 0/1px 13px no-repeat,' + line + ' 0 6px/13px 1px no-repeat;' + MARK_POS[c] + '"></i>').join('');
}
// components/panels/Panel.jsx
const PANEL_BORDER = { default: 'var(--cb-hairline)', live: 'var(--cb-hairline-live)', hot: 'var(--cb-hairline-hot)', alarm: 'var(--cb-hairline-alarm)' };
const FILL = { steel: 'var(--cb-fill-steel)', live: 'var(--cb-fill-live)', amber: 'var(--cb-fill-amber)', rust: 'var(--cb-fill-rust)' };
const TONE = { live: 'var(--cb-live)', text: 'var(--cb-text-1)', amber: 'var(--cb-amber)', rust: 'var(--cb-rust)' };
function panel(o) {
  const ground = 'var(--cb-ground)';
  const bg = o.fill == null ? ground
    : 'linear-gradient(90deg,' + FILL[o.fillTone || 'steel'] + ' 0 ' + o.fill + '%,transparent ' + o.fill + '%) left bottom/100% var(--cb-track-h) no-repeat,' + ground;
  return '<div style="position:relative;border:var(--cb-hair) solid ' + (PANEL_BORDER[o.tone] || PANEL_BORDER.default) + ';background:' + bg + ';padding:' + (o.pad || '18px 22px') + (o.style ? ';' + o.style : '') + '">'
    + cornerMarks() + o.children + '</div>';
}
// components/panels/HeroReadout.jsx
function heroReadout(o) {
  const size = o.size || 'var(--cb-size-hero)';
  const stats = o.stats || [];
  return panel({ tone: o.panelTone || 'default', fill: o.fill, fillTone: o.fillTone, pad: '18px 22px 16px', style: 'display:flex;align-items:flex-end;justify-content:space-between', children:
    '<div>'
    + '<div style="font:500 var(--cb-size-label) var(--cb-font-display);letter-spacing:var(--cb-track-label);text-transform:uppercase;color:var(--cb-text-2);margin-bottom:2px;white-space:nowrap">' + o.label + '</div>'
    + '<div style="display:flex;align-items:baseline;gap:8px">'
    + '<span style="font:600 ' + size + '/1.0 var(--cb-font-display);letter-spacing:var(--cb-track-hero)">' + o.value + '</span>'
    + (o.unit ? '<span style="font:500 var(--cb-size-value) var(--cb-font-display);color:var(--cb-text-2)">' + o.unit + '</span>' : '')
    + '</div>'
    + (o.note ? '<div style="font-size:var(--cb-size-body-sm);color:var(--cb-text-3);margin-top:6px;white-space:nowrap">' + o.note + '</div>' : '')
    + (stats.length ? '<div style="display:flex;align-items:baseline;gap:24px;margin-top:10px;padding-top:8px;border-top:var(--cb-hair) solid var(--cb-hairline-inner)">'
      + stats.map((s) => '<span style="display:flex;align-items:baseline;gap:7px">'
        + '<span style="font:500 var(--cb-size-label-xs) var(--cb-font-display);letter-spacing:var(--cb-track-label);text-transform:uppercase;color:var(--cb-text-3)">' + s.label + '</span>'
        + '<span style="font:600 var(--cb-size-value-sm) var(--cb-font-display);color:' + (TONE[s.tone] || TONE.text) + '">' + s.value + '</span>'
        + '</span>').join('')
      + '</div>' : '')
    + '</div>'
    + (o.aside ? '<div style="text-align:right;display:flex;flex-direction:column;gap:10px">' + o.aside + '</div>' : '')
  });
}
// components/shell/ScreenHeader.jsx
const HEAD_TONES = {
  live: { fg: 'var(--cb-live)', rule: 'var(--cb-hairline)', bg: 'transparent' },
  steel: { fg: 'var(--cb-text-2)', rule: 'var(--cb-hairline)', bg: 'transparent' },
  amber: { fg: 'var(--cb-amber)', rule: 'var(--cb-hairline)', bg: 'transparent' },
  rust: { fg: 'var(--cb-rust)', rule: 'var(--cb-hairline-alarm)', bg: 'var(--cb-ground-alarm)' }
};
function screenHeader(o) {
  const tone = o.tone || 'live';
  const t = HEAD_TONES[tone] || HEAD_TONES.live;
  return '<div style="display:flex;align-items:center;justify-content:space-between;padding:var(--cb-header-pad);height:var(--cb-header-h);flex:none;border-bottom:var(--cb-hair) solid ' + t.rule + ';background:' + t.bg + '">'
    + '<span style="font:600 var(--cb-size-label-lg) var(--cb-font-display);letter-spacing:var(--cb-track-screen);text-transform:uppercase;color:' + (tone === 'rust' ? t.fg : 'var(--cb-text-2)') + ';cursor:default">' + o.title + '</span>'
    + (o.status ? '<span style="display:flex;align-items:center;gap:var(--cb-space-3);font:600 var(--cb-size-label-lg) var(--cb-font-display);letter-spacing:var(--cb-track-status);color:' + t.fg + '">'
      + (o.dot ? '<span style="width:9px;height:9px;background:' + t.fg + ';border-radius:50%;animation:cb-blip var(--cb-dur-blip) linear infinite"></span>' : '')
      + o.status + '</span>' : '')
    + '</div>';
}
// components/shell/TabBar.jsx
function tabBar(o) {
  return '<div style="display:flex;flex:none;height:var(--cb-tabbar-h);border-top:var(--cb-hair) solid var(--cb-hairline)">'
    + o.tabs.map((t) => {
      const on = t.id === o.active;
      return '<div' + act(() => o.onSelect(t.id)) + ' style="flex:1;display:flex;align-items:center;justify-content:center;cursor:pointer;user-select:none;border-right:var(--cb-hair) solid var(--cb-hairline-inner);background:' + (on ? 'var(--cb-wash-live)' : 'transparent') + ';box-shadow:' + (on ? 'inset 0 -3px 0 var(--cb-live)' : 'none') + ';font:600 var(--cb-size-label-lg) var(--cb-font-display);letter-spacing:var(--cb-track-status);text-transform:uppercase;color:' + (on ? 'var(--cb-live)' : 'var(--cb-text-3)') + '">' + t.label + '</div>';
    }).join('')
    + (o.trailing ? '<div style="flex:none;width:var(--cb-tabbar-h);display:flex;align-items:center;justify-content:center;color:var(--cb-text-4);cursor:pointer">' + o.trailing + '</div>' : '')
    + '</div>';
}
// components/panels/StatTile.jsx
function statTile(o) {
  const fillTone = o.fillTone || 'steel';
  const bg = o.fill == null ? 'var(--cb-ground)'
    : 'linear-gradient(90deg,' + FILL[fillTone] + ' 0 ' + o.fill + '%,transparent ' + o.fill + '%) left bottom/100% var(--cb-track-h) no-repeat,var(--cb-ground)';
  return '<div style="background:' + bg + ';padding:' + (o.pad || '14px 16px') + ';border:' + (o.border === false ? 'none' : 'var(--cb-hair) solid var(--cb-hairline)') + '">'
    + '<div style="font:500 var(--cb-size-label-sm) var(--cb-font-display);letter-spacing:var(--cb-track-label);text-transform:uppercase;color:var(--cb-text-2);white-space:nowrap">' + o.label + '</div>'
    + '<div style="display:flex;align-items:baseline;gap:5px">'
    + '<span style="font:600 var(--cb-size-value) var(--cb-font-display);color:' + (TONE[o.tone || 'text'] || TONE.text) + '">' + o.value + '</span>'
    + (o.unit ? '<span style="font:500 var(--cb-size-label) var(--cb-font-display);color:var(--cb-text-2)">' + o.unit + '</span>' : '')
    + '</div>'
    + (o.note ? '<div style="font-size:var(--cb-size-micro);color:var(--cb-text-3);margin-top:2px;white-space:nowrap">' + o.note + '</div>' : '')
    + '</div>';
}
// components/panels/StatRow.jsx
const ROW_TONE = { live: 'var(--cb-live)', text: 'var(--cb-text-1)', amber: 'var(--cb-amber)', rust: 'var(--cb-rust)', dim: 'var(--cb-text-2)' };
function statRow(o) {
  const clamped = !!o.clamped, rule = o.rule || 'top';
  return '<div style="display:flex;align-items:baseline;justify-content:space-between;padding:' + (clamped ? '6px 24px' : '5px 0') + ';margin:' + (clamped ? '0 -24px' : '0') + ';border-top:' + (rule === 'top' ? 'var(--cb-hair) solid var(--cb-hairline-inner)' : 'none') + ';border-bottom:' + (rule === 'bottom' ? 'var(--cb-hair) solid var(--cb-hairline-inner)' : 'none') + ';animation:' + (clamped ? 'cb-clamp var(--cb-dur-clamp) linear infinite' : 'none') + '">'
    + '<span style="font-size:var(--cb-size-annot);color:var(--cb-text-2)">' + o.label + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:4px">'
    + '<span style="font:600 var(--cb-size-value-xs) var(--cb-font-display);color:' + (ROW_TONE[o.tone || 'text'] || ROW_TONE.text) + '">' + o.value + '</span>'
    + (o.unit ? '<span style="font-size:var(--cb-size-micro);color:var(--cb-text-3)">' + o.unit + '</span>' : '')
    + '</span></div>';
}
// components/status/AlarmTile.jsx
const ALARM_STATE = {
  ok: { fg: 'var(--cb-text-2)', border: 'var(--cb-hairline)', bg: 'var(--cb-ground)', word: 'CLEAR' },
  caution: { fg: 'var(--cb-amber)', border: 'var(--cb-hairline)', bg: 'var(--cb-ground-caution)', word: 'WATCH' },
  trip: { fg: 'var(--cb-rust)', border: 'var(--cb-hairline-alarm)', bg: 'var(--cb-ground-alarm)', word: 'TRIPPED' }
};
function alarmTile(o) {
  const state = o.state || 'ok';
  const s = ALARM_STATE[state] || ALARM_STATE.ok;
  return '<div style="position:relative;border:var(--cb-hair) solid ' + s.border + ';background:' + s.bg + ';padding:14px 16px;display:flex;flex-direction:column;gap:5px;animation:' + (state === 'trip' ? 'cb-trip var(--cb-dur-trip) linear infinite' : 'none') + '">'
    + '<div style="display:flex;align-items:baseline;justify-content:space-between">'
    + '<span style="font:500 var(--cb-size-label-sm) var(--cb-font-display);letter-spacing:var(--cb-track-label);text-transform:uppercase;color:var(--cb-text-2)">' + o.label + '</span>'
    + '<span style="font:600 var(--cb-size-label-xs) var(--cb-font-display);letter-spacing:var(--cb-track-status);color:' + s.fg + '">' + s.word + '</span>'
    + '</div>'
    + (o.value != null ? '<div style="font:600 var(--cb-size-value-lg)/1 var(--cb-font-display);color:' + s.fg + '">' + o.value + '</div>' : '')
    + (o.detail ? '<div style="font-size:var(--cb-size-annot);color:var(--cb-text-3)">' + o.detail + '</div>' : '')
    + (o.stamp ? '<div style="font-size:var(--cb-size-micro);color:var(--cb-text-4)">' + o.stamp + '</div>' : '')
    + '</div>';
}
// components/status/ConsoleButton.jsx (hover state is the [role="button"]:hover rules in the stylesheet)
const BTN = {
  primary: { bg: 'var(--cb-live)', fg: 'var(--cb-on-accent)', border: 'var(--cb-live)' },
  default: { bg: 'transparent', fg: 'var(--cb-text-2)', border: 'var(--cb-hairline)' },
  danger: { bg: 'transparent', fg: 'var(--cb-rust)', border: 'var(--cb-rust)' }
};
function consoleButton(o) {
  const variant = o.variant || 'default';
  const v = BTN[variant] || BTN.default;
  return '<div role="button" tabindex="0" data-variant="' + variant + '"' + act(o.onClick) + ' style="display:flex;align-items:center;justify-content:center;min-height:62px;padding:0 var(--cb-space-6);cursor:pointer;user-select:none;opacity:1;border:var(--cb-hair) solid ' + v.border + ';background:' + v.bg + ';color:' + v.fg + ';font:600 var(--cb-size-value-sm) var(--cb-font-display);letter-spacing:var(--cb-track-status);text-transform:uppercase">' + o.children + '</div>';
}
// components/instruments/FlowLine.jsx
const FLOW_TONE = { steel: 'var(--cb-steel-500)', live: 'var(--cb-live)', amber: 'var(--cb-amber)', rust: 'var(--cb-rust)' };
function flowLine(o) {
  const c = FLOW_TONE[o.tone || 'steel'] || FLOW_TONE.steel;
  const active = o.active !== false, dash = 13, period = dash * 2;
  return '<div style="height:3px;flex:1;min-width:0;background:repeating-linear-gradient(90deg,' + c + ' 0 ' + dash + 'px,transparent ' + dash + 'px ' + period + 'px);background-size:' + period + 'px 100%;opacity:' + (active ? 1 : .25) + ';animation:' + (active ? 'cb-march var(--cb-dur-march) linear infinite' : 'none') + '"></div>';
}
// components/instruments/NodeStrip.jsx
function nodeStrip(o) {
  return '<div style="display:flex;border:var(--cb-hair) solid var(--cb-hairline)">'
    + o.nodes.map((n, i) =>
      (i > 0 ? '<div style="flex:none;width:26px;display:flex;align-items:center;border-left:var(--cb-hair) solid var(--cb-hairline-inner)">' + flowLine({ active: o.flowing, tone: n.flowTone || 'steel' }) + '</div>' : '')
      + '<div style="flex:' + (n.width || 1) + ';padding:11px 13px;min-width:0;border-left:' + (i > 0 ? 'var(--cb-hair) solid var(--cb-hairline-inner)' : 'none') + '">'
      + '<div style="font:500 var(--cb-size-label-sm) var(--cb-font-display);letter-spacing:var(--cb-track-label);text-transform:uppercase;color:var(--cb-text-3);white-space:nowrap">' + n.label + '</div>'
      + '<div style="display:flex;align-items:baseline;gap:5px">'
      + '<span style="font:600 40px/1 var(--cb-font-display);color:' + (TONE[n.tone] || TONE.text) + '">' + n.value + '</span>'
      + (n.unit ? '<span style="font:500 var(--cb-size-label) var(--cb-font-display);color:var(--cb-text-2)">' + n.unit + '</span>' : '')
      + '</div>'
      + (n.secondary ? '<div style="font:600 var(--cb-size-label-lg) var(--cb-font-display);letter-spacing:var(--cb-track-value);color:var(--cb-text-1)">' + n.secondary + ' ' + (n.secondaryUnit ? '<span style="font-size:12px;color:var(--cb-text-3)">' + n.secondaryUnit + '</span>' : '') + '</div>' : '')
      + (n.note ? '<div style="font-size:var(--cb-size-micro);color:var(--cb-text-3);margin-top:2px;white-space:nowrap">' + n.note + '</div>' : '')
      + '</div>').join('')
    + '</div>';
}

// ---- the design's renderVals(), the values its template binds
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
  const cap = 'font:500 12.5px var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;color:var(--cb-text-2)';
  const big = 'font:600 32px/1 var(--cb-font-display);letter-spacing:.02em';
  const unit = 'font-size:13px;color:var(--cb-text-3)';
  const titles = { supply: 'Supply · gas lines', atmo: 'Atmo · ' + (props.roomName || 'hab core'), filter: 'Filtration · ' + (props.roomName || 'hab core'), alarm: 'Alarms · ' + (props.roomName || 'hab core'), config: 'Bindings · atmo regulator' };
  const stampOf = (s) => s === 'ok' ? 'no trip on record' : 'since ' + clock(Math.max(0, st.tick - 40));
  const selSet = SETPOINTS.find((s) => s.key === st.sp) || SETPOINTS[0];
  const lowestMol = Math.min(st.lines.o2.mol, st.lines.co2.mol, nitro ? st.lines.n2.mol : Infinity);
  const reserveTotal = st.lines.o2.mol + st.lines.co2.mol + (nitro ? st.lines.n2.mol : 0);
  const drawTotal = st.lines.o2.pump + st.lines.co2.pump + (nitro ? st.lines.n2.pump : 0);
  const bound = st.bound || {};
  const sel = ROLES.find((r) => r.key === st.role) || ROLES[0];
  const rowBase = 'display:flex;align-items:center;gap:9px;height:24px;flex:none;border-bottom:1px solid #1b232c;cursor:pointer;padding:0 6px 0 4px';
  const rows = (g) => ROLES.filter((r) => r.group === g).map((r) => {
    const b = bound[r.key], on = r.key === sel.key;
    return {
      label: r.key, bound: b || 'not bound',
      style: rowBase + (on ? ';background:rgba(148,188,227,.12);box-shadow:inset 2px 0 0 #94bce3' : ''),
      dot: 'width:9px;height:9px;flex:none;border-radius:50%;background:' + (b ? '#94bce3' : '#e0a44f'),
      nameStyle: 'font-size:12.5px;overflow:hidden;white-space:nowrap;text-overflow:ellipsis;color:' + (b ? (on ? '#e7e7ea' : '#98989b') : '#e0a44f'),
      pick: () => { st.role = r.key; render(); }
    };
  });
  const candidates = sel.devs.map((dv) => {
    const on = bound[sel.key] === dv.name;
    const taken = !on && Object.keys(bound).some((k) => bound[k] === dv.name);
    return {
      name: dv.name, meta: 'ReferenceId ' + dv.id + ' · ' + dv.meta,
      tag: on ? 'BOUND' : (taken ? 'IN USE' : 'SELECT'),
      style: 'border:1px solid ' + (on ? '#94bce3' : '#232c37') + ';padding:9px 12px;display:flex;align-items:center;justify-content:space-between;cursor:pointer;background:' + (on ? 'rgba(148,188,227,.10)' : 'transparent'),
      tagStyle: "font:600 14px 'Barlow Condensed',sans-serif;letter-spacing:.12em;color:" + (on ? '#94bce3' : (taken ? '#5d5d60' : '#98989b')),
      bind: () => { bound[sel.key] = bound[sel.key] === dv.name ? null : dv.name; st.dirty = true; render(); }
    };
  });
  const unbound = ROLES.filter((r) => !bound[r.key]).length;

  return {
    tabs: [{ id: 'atmo', label: 'Atmo' }, { id: 'supply', label: 'Supply' }, { id: 'filter', label: 'Filter' }, { id: 'alarm', label: 'Alarm' }],
    tab: st.tab,
    setTab: (id) => { st.tab = id; render(); },
    gear: '<span' + act(() => { st.tab = st.tab === 'config' ? 'atmo' : 'config'; render(); }) + ' title="Bindings" style="font-size:15px;cursor:pointer;color:' + (st.tab === 'config' ? 'var(--cb-live)' : 'inherit') + '">⚙</span>',
    isAtmo: st.tab === 'atmo', isFilter: st.tab === 'filter', isAlarm: st.tab === 'alarm', isConfig: st.tab === 'config',
    headTitle: titles[st.tab],
    headStatus: st.purging ? 'PURGING' : anyTrip ? 'TRIPPED' : anyCaution ? 'CORRECTING' : 'HOLDING',
    headTone: st.purging ? 'rust' : anyTrip ? 'rust' : anyCaution ? 'amber' : 'live',

    pressStr: f1(r.press),
    heroTone: pressState === 'trip' ? 'alarm' : pressState === 'caution' ? 'hot' : 'live',
    pressFill: clamp(r.press / t.trip * 100, 0, 100),
    pressFillTone: venting ? 'rust' : pressState === 'caution' ? 'amber' : 'live',
    heroNote: 'set ' + f1(t.press) + ' kPa · trip ' + t.trip.toFixed(0) + ' · '
      + '<span style="color:' + (venting ? 'var(--cb-rust)' : 'var(--cb-live)') + '">' + (venting ? 'safety vent dumping' : (dP > 0 ? 'bleeding ' : 'charging ') + f1(Math.abs(dP)) + ' kPa') + '</span>',
    heroAside:
      '<div><div style="' + cap + '">Room temp</div>'
      + '<div style="' + big + ';color:' + (tempState === 'ok' ? 'var(--cb-text-1)' : tempState === 'caution' ? 'var(--cb-amber)' : 'var(--cb-rust)') + '">' + f1(r.temp) + ' <span style="' + unit + '">°C</span></div></div>'
      + '<div><div style="' + cap + '">AC · set ' + f1(t.temp) + '</div>'
      + '<div style="font:600 19px var(--cb-font-display);letter-spacing:.1em;color:' + (acMode === 'IDLE' ? 'var(--cb-text-3)' : 'var(--cb-live)') + '">' + acMode + '</div></div>',

    gases: [
      { label: 'O₂', value: f1(r.o2), color: o2State === 'trip' ? 'var(--cb-rust)' : o2State === 'caution' ? 'var(--cb-amber)' : 'var(--cb-text-1)',
        fill: clamp(r.o2 / 30 * 100, 0, 100).toFixed(1) + '%', mark: clamp(t.o2 / 30 * 100, 0, 100).toFixed(1) + '%',
        bar: o2State === 'ok' ? 'var(--cb-live)' : o2State === 'caution' ? 'var(--cb-amber)' : 'var(--cb-rust)',
        note: 'target ' + f1(t.o2) + ' % · pump ' + o2Duty.toFixed(0) + ' %', scale: '30 %', sp: 'o2' },
      { label: 'N₂', value: f1(n2), color: 'var(--cb-text-1)',
        fill: clamp(n2, 0, 100).toFixed(1) + '%', mark: clamp(tN2, 0, 100).toFixed(1) + '%',
        bar: 'var(--cb-steel-700)',
        note: nitro ? 'residual ' + f1(tN2) + ' % · N line live' : 'residual ' + f1(tN2) + ' % · N unbound', scale: '100 %' },
      { label: 'CO₂', value: f2(r.co2), color: r.co2 > t.co2 * 1.5 ? 'var(--cb-amber)' : 'var(--cb-text-1)',
        fill: clamp(r.co2 / 3 * 100, 0, 100).toFixed(1) + '%', mark: clamp(t.co2 / 3 * 100, 0, 100).toFixed(1) + '%',
        bar: r.co2 > t.co2 * 1.5 ? 'var(--cb-amber)' : 'var(--cb-live)',
        note: 'target ' + f2(t.co2) + ' % · pump ' + co2Duty.toFixed(0) + ' %', scale: '3 %', sp: 'co2' },
      { label: 'X · CH₄', value: f2(r.poll + r.ch4), color: pollState === 'trip' ? 'var(--cb-rust)' : pollState === 'caution' ? 'var(--cb-amber)' : 'var(--cb-text-1)',
        fill: clamp((r.poll + r.ch4) * 100, 0, 100).toFixed(1) + '%', mark: '0%',
        bar: pollState === 'trip' ? 'var(--cb-rust)' : pollState === 'caution' ? 'var(--cb-amber)' : 'var(--cb-steel-700)',
        note: pollState === 'ok' ? 'ceiling zero · clear' : 'ceiling zero · filtration ' + filtLoad.toFixed(0) + ' %', scale: '1 %' },
      { label: 'Other', value: f2(foreignSum), color: foreignState === 'trip' ? 'var(--cb-rust)' : foreignState === 'caution' ? 'var(--cb-amber)' : 'var(--cb-text-3)',
        fill: clamp(foreignSum * 100, 0, 100).toFixed(1) + '%', mark: '0%',
        bar: foreignState === 'trip' ? 'var(--cb-rust)' : foreignState === 'caution' ? 'var(--cb-amber)' : 'var(--cb-steel-700)',
        note: detected.length ? detected.map((g) => g.n + ' ' + f2(g.v)).join(' · ') : 'no unexpected gas detected', scale: '1 %' }
    ].map((g) => {
      const on = g.sp && g.sp === st.sp;
      return Object.assign(g, {
        cursor: g.sp ? 'pointer' : 'default',
        bg: on ? 'var(--cb-wash-steel)' : 'transparent',
        mark2: on ? 'inset 3px 0 0 var(--cb-live)' : 'none',
        pick: g.sp ? () => { st.sp = g.sp; render(); } : null,
        dotAnim: (g.sp === 'o2' && o2Duty > 1) || (g.sp === 'co2' && co2Duty > 1)
          ? 'cb-blip var(--cb-dur-blip) linear infinite' : 'none',
        dotColor: (g.sp === 'o2' && o2Duty > 1) || (g.sp === 'co2' && co2Duty > 1) ? 'var(--cb-live)' : 'transparent'
      });
    }),
    isSupply: st.tab === 'supply',
    accentCorners: ['tl', 'br'],
    supplyFootA: lowestMol < 400
      ? 'lowest tank ' + lowestMol.toFixed(0) + ' mol · refill before the room falls off target'
      : 'all bound tanks above the 400 mol watch line',
    supplyFootB: 'analyzers sampled each tick · ' + clock(),
    atmoFootA: anyTrip
      ? 'room outside limits · regulator correcting on ' + (o2Duty > 1 ? 'O₂' : co2Duty > 1 ? 'CO₂' : 'pressure')
      : anyCaution
        ? 'closing on target · ' + (o2Duty > 1 || co2Duty > 1 ? 'supply pumps open' : 'pumps idle')
        : 'holding target · all four gases inside their windows',
    atmoFootB: 'AC ' + acMode.toLowerCase() + ' · filtration ' + filtLoad.toFixed(0) + ' % · vent ' + (venting ? 'open' : 'closed'),
    supplyNote: nitro ? 'three lines bound · analyzer sampled each tick' : 'N line unbound · O₂ and CO₂ sampled each tick',
    lines: [
      { key: 'o2', name: 'O₂', analyzer: 'O2-AN · supply line', live: true, duty: o2Duty },
      { key: 'co2', name: 'CO₂', analyzer: 'CO2-AN · supply line', live: true, duty: co2Duty },
      { key: 'n2', name: 'N₂', analyzer: nitro ? 'N-AN · supply line' : 'N-AN · not bound', live: nitro, duty: 0 }
    ].map((L) => {
      const d = st.lines[L.key], low = d.mol < 400, cap = 4200;
      return {
        head: L.name + ' · supply',
        fill: L.live ? (d.mol / cap * 100).toFixed(0) : '0',
        levelBg: L.live
          ? 'linear-gradient(0deg,' + (d.mol < 200 ? 'rgba(207,106,88,.75)' : low ? 'rgba(224,164,79,.75)' : 'rgba(148,188,227,.75)') + ' 0 ' + clamp(d.mol / cap * 100, 0, 100).toFixed(1) + '%,rgba(0,0,0,0) 0) left bottom/3px 100% no-repeat,#000'
          : 'linear-gradient(0deg,rgba(90,102,116,.4) 0 100%,rgba(0,0,0,0) 0) left bottom/3px 100% no-repeat,#000',
        name: L.name, analyzer: L.analyzer,
        state: !L.live ? 'UNBOUND' : d.pump > 0 ? 'PUMPING' : 'STANDBY',
        stateColor: !L.live ? 'var(--cb-text-4)' : d.pump > 0 ? 'var(--cb-live)' : 'var(--cb-text-3)',
        press: L.live ? d.press.toFixed(0) : '—',
        pressColor: !L.live ? 'var(--cb-text-4)' : low ? 'var(--cb-amber)' : 'var(--cb-text-1)',
        temp: L.live ? f1(d.temp) : '—',
        mol: L.live ? d.mol.toFixed(0) : '—',
        molColor: !L.live ? 'var(--cb-text-4)' : low ? 'var(--cb-amber)' : 'var(--cb-text-1)',
        pump: L.live ? d.pump.toFixed(1) : '0.0',
        pumpColor: d.pump > 0 ? 'var(--cb-live)' : 'var(--cb-text-4)',
        pumpNote: !L.live ? 'off' : d.pump > 0 ? 'duty ' + L.duty.toFixed(0) + ' %' : 'idle',
        duct: d.pump > 0 && L.live
          ? 'repeating-linear-gradient(90deg,var(--cb-live) 0 13px,var(--cb-steel-800) 13px 26px)'
          : 'repeating-linear-gradient(90deg,var(--cb-steel-800) 0 13px,transparent 13px 26px)',
        ductAnim: d.pump > 0 && L.live ? 'cb-march var(--cb-dur-march) linear infinite' : 'none'
      };
    }),
    alSupply: lowestMol < 200 ? 'trip' : lowestMol < 400 ? 'caution' : 'ok',
    alSupplyVal: lowestMol.toFixed(0) + ' mol',
    alSupplyDetail: 'Lowest bound supply tank · watch under 400 mol',
    alSupplyStamp: stampOf(lowestMol < 200 ? 'trip' : lowestMol < 400 ? 'caution' : 'ok'),
    setpoints: SETPOINTS.map((s) => {
      const on = s.key === st.sp;
      return {
        label: s.label, unit: s.unit, value: s.fmt(t[s.field]),
        color: on ? 'var(--cb-live)' : 'var(--cb-text-1)',
        style: 'background:linear-gradient(90deg,' + (on ? 'var(--cb-fill-live)' : 'var(--cb-steel-700)') + ' 0 ' + clamp((t[s.field] - s.min) / (s.max - s.min) * 100, 0, 100).toFixed(1) + '%,transparent 0) left bottom/100% 3px no-repeat,' + (on ? 'var(--cb-wash-steel)' : '#000')
          + ';padding:9px 12px 12px;display:flex;flex-direction:column;gap:1px;cursor:pointer',
        pick: () => { st.sp = s.key; render(); }
      };
    }),
    selSetLabel: selSet.label, selSetUnit: selSet.unit, selSetNote: selSet.note,
    selSetValue: selSet.fmt(t[selSet.field]),
    selSetColor: selSet.key === 'trip' ? 'var(--cb-amber)' : 'var(--cb-live)',
    setUp: () => bump(selSet.field, selSet.step, selSet.min, selSet.max),
    setDn: () => bump(selSet.field, -selSet.step, selSet.min, selSet.max),
    mixNote: detected.length ? 'unexpected: ' + detected.map((g) => g.n).join(', ') : 'O₂ ' + f1(r.o2) + ' · N₂ ' + f1(n2) + ' · CO₂ ' + f2(r.co2) + ' · X ' + f2(r.poll + r.ch4) + ' %',

    filtStr: f2(filtLoad * 0.042), filtTone: pollState === 'trip' ? 'alarm' : 'live',
    filtFill: filtLoad, filtFillTone: filtLoad > 70 ? 'amber' : 'live',
    filtNote: f2(r.co2) + ' % CO₂ + ' + f2(r.poll + r.ch4) + ' % X drawn through the unit · '
      + '<span style="color:var(--cb-live)">out at ' + (60 + filtLoad * 0.6).toFixed(0) + ' kPa</span>',
    filtStats: [
      { label: 'Load', value: filtLoad.toFixed(0) + '%', tone: filtLoad > 70 ? 'amber' : 'live' },
      { label: 'Room draw', value: f1(r.press) + ' kPa' },
      { label: 'Vent set', value: t.trip.toFixed(0) + ' kPa' }
    ],
    nodes: [
      { label: 'Room', value: f1(r.press), unit: 'kPa', secondary: f1(r.temp), secondaryUnit: '°C', note: 'sensor · hab', tone: pressState === 'trip' ? 'rust' : 'text' },
      { label: 'Intake', value: (r.press * 0.94).toFixed(1), unit: 'kPa', secondary: f1(r.temp - 0.4), secondaryUnit: '°C', note: 'passive vent', flowTone: filtLoad > 5 ? 'live' : 'steel' },
      { label: 'Filtration', value: (60 + filtLoad * 0.6).toFixed(0), unit: 'kPa', secondary: f1(r.temp + 2.1), secondaryUnit: '°C', note: filtLoad > 5 ? 'ON · 2 filters' : 'IDLE', tone: filtLoad > 70 ? 'amber' : 'live', width: 1.2, flowTone: filtLoad > 5 ? 'live' : 'steel' },
      { label: 'Out pump', value: (120 + filtLoad * 4).toFixed(0), unit: 'kPa', secondary: f1(r.temp + 3.4), secondaryUnit: '°C', note: (filtLoad * 0.9).toFixed(0) + ' L · waste', flowTone: filtLoad > 5 ? 'live' : 'steel' },
      { label: 'Waste', value: (4321 + Math.round(filtLoad * 3)).toString(), unit: 'kPa', secondary: '18.6', secondaryUnit: '°C', note: 'tank · vent 5000', flowTone: filtLoad > 5 ? 'live' : 'steel' }
    ],
    flowing: filtLoad > 5,
    ch4Filter: st.filters.ch4.toFixed(0), ch4FilterTone: st.filters.ch4 < 15 ? 'rust' : st.filters.ch4 < 35 ? 'amber' : 'steel',
    ch4FilterTone2: st.filters.ch4 < 15 ? 'rust' : st.filters.ch4 < 35 ? 'amber' : 'text',
    pollFilter: st.filters.poll.toFixed(0), pollFilterTone: st.filters.poll < 15 ? 'rust' : st.filters.poll < 35 ? 'amber' : 'steel',
    pollFilterTone2: st.filters.poll < 15 ? 'rust' : st.filters.poll < 35 ? 'amber' : 'text',
    outPump: (filtLoad * 0.9).toFixed(0), outPumpNote: filtLoad > 5 ? 'to waste tank' : 'stopped', outPumpFill: filtLoad * 0.9,
    filtMode: filtLoad > 5 ? 'ON · AUTO' : 'IDLE', filtModeTone: filtLoad > 5 ? 'live' : 'dim',
    ventState: (t.trip - 10).toFixed(0), ventUnit: 'kPa', ventTone: 'text',
    safetyState: st.purging ? 'PURGE DUMP' : venting ? 'DUMPING' : r.press > t.trip - 10 ? 'ARMED' : 'CLOSED',
    safetyTone: venting ? 'rust' : r.press > t.trip - 10 ? 'amber' : 'dim',
    safetyClamped: venting,
    acMode: acMode + ' · set ' + f1(t.temp) + ' °C', acTone: acMode === 'IDLE' ? 'dim' : 'live',

    alPress: pressState, alPressVal: f1(r.press) + ' kPa', alPressDetail: 'Window ' + f1(t.press - 9) + '–' + f1(t.press + 9) + ' kPa, trip at ' + t.trip.toFixed(0), alPressStamp: stampOf(pressState),
    alO2: o2State, alO2Val: f1(r.o2) + ' %', alO2Detail: 'Watch below 19.5 %, trip below 17.0 %', alO2Stamp: stampOf(o2State),
    alPoll: pollState, alPollVal: f2(r.poll + r.ch4) + ' %', alPollDetail: 'Pollutant + CH₄ ceiling is zero · any trace runs filtration', alPollStamp: stampOf(pollState),
    alOther: foreignState, alOtherVal: f2(foreignSum) + ' %', alOtherDetail: detected.length ? 'Detected: ' + detected.map((g) => g.n).join(', ') : 'No gas outside O₂ / N₂ / CO₂ in the room', alOtherStamp: stampOf(foreignState),
    alTemp: tempState, alTempVal: f1(dT) + ' °C', alTempDetail: 'Deviation from ' + f1(t.temp) + ' °C setpoint', alTempStamp: stampOf(tempState),
    log: st.log, logNote: clock() + ' · ' + st.log.length + ' retained',
    soundLabel: 'SOUND ' + (st.sound ? 'ARMED' : 'MUTED'),
    lightLabel: 'LIGHT ' + (st.light ? 'ARMED' : 'OFF'),
    toggleSound: () => { push(st.sound ? 'sound alarm muted at console' : 'sound alarm armed', 'ALARM', 'var(--cb-amber)'); st.sound = !st.sound; render(); },
    toggleLight: () => { push(st.light ? 'light alarm disabled' : 'light alarm armed', 'ALARM', 'var(--cb-amber)'); st.light = !st.light; render(); },
    purge: () => { st.purging = true; push('room purge commanded · safety vent open', 'PURGE', 'var(--cb-rust)'); render(); },

    notConfig: st.tab !== 'config',
    rowsGas: rows('gas'), rowsFilt: rows('filt'), rowsCtl: rows('ctl'),
    candidates: candidates, selLabel: sel.key, selSub: sel.sub,
    selCount: sel.devs.length + ' matching devices on this network',
    unboundLabel: unbound ? unbound + (unbound === 1 ? ' ROLE UNBOUND' : ' ROLES UNBOUND') : 'ALL ' + ROLES.length + ' ROLES BOUND',
    unboundStyle: "font:600 18px 'Barlow Condensed',sans-serif;letter-spacing:.12em;color:" + (unbound ? '#e0a44f' : '#94bce3'),
    writeLabel: st.dirty ? 'WRITE BINDINGS' : 'BINDINGS WRITTEN',
    writeStyle: 'display:flex;align-items:center;justify-content:center;font:600 16px var(--cb-font-display);letter-spacing:.1em;cursor:pointer;'
      + (st.dirty ? 'background:var(--cb-live);color:#000' : 'color:var(--cb-text-4);border-left:1px solid var(--cb-hairline-inner)'),
    exitConfig: () => { st.tab = 'atmo'; render(); },
    rescan: () => { push('device rescan · ' + ROLES.length + ' roles on the network', 'CONFIG', 'var(--cb-text-4)'); render(); },
    writeBindings: () => { st.dirty = false; push('bindings written to regulator · ' + (ROLES.length - unbound) + ' devices', 'CONFIG', 'var(--cb-live)'); render(); }
  };
}

// ---- the design's template, screen by screen
function renderAtmo(v) {
  return '<div style="flex:1;display:flex;flex-direction:column;gap:11px;padding:22px 24px;min-height:0">'
    + heroReadout({ label: 'Room pressure', value: v.pressStr, unit: 'kPa', size: '56px', note: v.heroNote, aside: v.heroAside, panelTone: v.heroTone, fill: v.pressFill, fillTone: v.pressFillTone })
    + '<div style="flex:1;display:flex;flex-direction:column;min-height:0;overflow:hidden">'
    + '<div style="display:flex;justify-content:space-between;align-items:baseline;padding-bottom:6px;flex:none">'
    + '<div style="font:500 12.5px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">Composition · measured against target</div>'
    + '<div style="font-family:var(--cb-font-body);font-size:11.5px;color:var(--cb-text-4)">' + v.mixNote + '</div>'
    + '</div>'
    + v.gases.map((g) =>
      '<div' + (g.pick ? act(g.pick) : '') + ' style="display:grid;grid-template-columns:88px 88px 1fr 176px;align-items:center;gap:12px;flex:1;min-height:0;border-top:1px solid var(--cb-hairline-inner);cursor:' + g.cursor + ';background:' + g.bg + ';box-shadow:' + g.mark2 + ';padding-left:9px">'
      + '<span style="display:flex;align-items:center;gap:6px;font:600 17px/1 var(--cb-font-display);letter-spacing:.05em;color:var(--cb-text-2);white-space:nowrap"><span style="width:7px;height:7px;flex:none;border-radius:50%;background:' + g.dotColor + ';animation:' + g.dotAnim + '"></span>' + g.label + '</span>'
      + '<div style="display:flex;align-items:baseline;justify-content:flex-end;gap:4px">'
      + '<span style="font:600 25px/1 var(--cb-font-display);letter-spacing:.01em;color:' + g.color + '">' + g.value + '</span>'
      + '<span style="font-size:11.5px;color:var(--cb-text-3)">%</span>'
      + '</div>'
      + '<div style="position:relative;height:3px;background:var(--cb-hairline)">'
      + '<div style="position:absolute;left:0;top:0;bottom:0;width:' + g.fill + ';background:' + g.bar + '"></div>'
      + '<div style="position:absolute;top:-5px;bottom:-5px;left:' + g.mark + ';width:1px;background:var(--cb-steel-300)"></div>'
      + '</div>'
      + '<span style="font-family:var(--cb-font-body);font-size:11.5px;color:var(--cb-text-4);text-align:right;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + g.note + '</span>'
      + '</div>').join('')
    + '<div style="flex:none;display:flex;align-items:baseline;justify-content:space-between;border-top:1px solid var(--cb-hairline);padding-top:8px;margin-top:2px">'
    + '<span style="font-family:var(--cb-font-body);font-size:13px;color:var(--cb-text-2)">' + v.atmoFootA + '</span>'
    + '<span style="font-family:var(--cb-font-body);font-size:13px;color:var(--cb-text-4)">' + v.atmoFootB + '</span>'
    + '</div>'
    + '</div>'
    + '<div style="flex:none;display:flex;flex-direction:column;gap:9px">'
    + '<div style="display:grid;grid-template-columns:repeat(5,1fr);gap:1px;background:var(--cb-hairline);border:1px solid var(--cb-hairline)">'
    + v.setpoints.map((s) =>
      '<div' + act(s.pick) + ' style="' + s.style + '">'
      + '<div style="font:500 11.5px var(--cb-font-display);letter-spacing:.14em;text-transform:uppercase;color:var(--cb-text-3);white-space:nowrap">' + s.label + '</div>'
      + '<div style="display:flex;align-items:baseline;gap:3px">'
      + '<span style="font:600 21px/1 var(--cb-font-display);letter-spacing:.02em;color:' + s.color + '">' + s.value + '</span>'
      + '<span style="font-size:11px;color:var(--cb-text-4)">' + s.unit + '</span>'
      + '</div>'
      + '</div>').join('')
    + '</div>'
    + '<div style="position:relative;display:flex;align-items:center;gap:16px;border:1px solid var(--cb-hairline);padding:9px 13px">'
    + cornerMarks(v.accentCorners)
    + '<div style="flex:1;min-width:0">'
    + '<div style="font:500 12.5px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-2)">' + v.selSetLabel + '</div>'
    + '<div style="font-family:var(--cb-font-body);font-size:12px;color:var(--cb-text-4);text-wrap:pretty">' + v.selSetNote + '</div>'
    + '</div>'
    + '<div style="display:flex;align-items:baseline;gap:4px">'
    + '<span style="font:600 34px/1 var(--cb-font-display);letter-spacing:.01em;color:' + v.selSetColor + '">' + v.selSetValue + '</span>'
    + '<span style="font-size:13px;color:var(--cb-text-3)">' + v.selSetUnit + '</span>'
    + '</div>'
    + '<div style="display:flex;gap:6px">'
    + '<button' + act(v.setDn) + ' style="width:50px;height:42px;border:1px solid var(--cb-steel-700);background:transparent;color:var(--cb-live);font:600 22px var(--cb-font-display);cursor:pointer;padding:0" style-hover="background:var(--cb-wash-steel)">−</button>'
    + '<button' + act(v.setUp) + ' style="width:50px;height:42px;border:1px solid var(--cb-steel-700);background:transparent;color:var(--cb-live);font:600 22px var(--cb-font-display);cursor:pointer;padding:0" style-hover="background:var(--cb-wash-steel)">+</button>'
    + '</div>'
    + '</div>'
    + '</div>'
    + '</div>';
}

function renderSupply(v) {
  return '<div style="flex:1;display:flex;flex-direction:column;gap:12px;padding:20px 22px;min-height:0">'
    + '<div style="display:flex;align-items:baseline;justify-content:space-between;flex:none">'
    + '<span style="font:500 14px var(--cb-font-display);letter-spacing:.18em;text-transform:uppercase;color:var(--cb-text-2)">Supply tanks · analyzer → volume pump → room</span>'
    + '<span style="font:500 13px var(--cb-font-display);letter-spacing:.1em;color:var(--cb-text-3)">' + v.supplyNote + '</span>'
    + '</div>'
    + '<div style="flex:1;display:grid;grid-template-columns:repeat(3,1fr);gap:9px;min-height:0">'
    + v.lines.map((l) =>
      '<div style="border:1px solid var(--cb-hairline);padding:11px 14px;display:flex;flex-direction:column;background:' + l.levelBg + '">'
      + '<div style="display:flex;align-items:baseline;justify-content:space-between">'
      + '<span style="font:500 13px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3);white-space:nowrap">' + l.head + '</span>'
      + '<span style="font:500 12px var(--cb-font-display);letter-spacing:.12em;color:' + l.stateColor + '">' + l.state + '</span>'
      + '</div>'
      + '<div style="display:flex;align-items:baseline;gap:5px"><span style="font:600 42px/1 var(--cb-font-display);color:' + l.molColor + '">' + l.mol + '</span><span style="font:500 16px var(--cb-font-display);color:var(--cb-text-3)">mol</span></div>'
      + '<div style="font-size:11.5px;color:var(--cb-text-4);margin-bottom:5px">' + l.analyzer + '</div>'
      + '<div style="margin-top:auto;display:flex;align-items:baseline;justify-content:space-between;border-top:1px solid var(--cb-hairline-inner);padding:5px 0"><span style="font-size:12.5px;color:var(--cb-text-2)">Pressure</span><span style="font:600 17px var(--cb-font-display);color:' + l.pressColor + '">' + l.press + ' kPa</span></div>'
      + '<div style="display:flex;align-items:baseline;justify-content:space-between;border-top:1px solid var(--cb-hairline-inner);padding:5px 0"><span style="font-size:12.5px;color:var(--cb-text-2)">Temp</span><span style="font:600 17px var(--cb-font-display);color:var(--cb-text-1)">' + l.temp + ' °C</span></div>'
      + '<div style="display:flex;align-items:baseline;justify-content:space-between;border-top:1px solid var(--cb-hairline-inner);padding:5px 0"><span style="font-size:12.5px;color:var(--cb-text-2)">Fill</span><span style="font:600 17px var(--cb-font-display);color:var(--cb-text-1)">' + l.fill + ' %</span></div>'
      + '<div style="display:flex;align-items:baseline;justify-content:space-between;border-top:1px solid var(--cb-hairline-inner);padding:5px 0"><span style="font-size:12.5px;color:var(--cb-text-2)">Volume pump</span><span style="font:600 17px var(--cb-font-display);color:' + l.pumpColor + '">' + l.pump + ' L</span></div>'
      + '<div style="display:flex;align-items:center;gap:7px;border-top:1px solid var(--cb-hairline-inner);padding-top:7px">'
      + '<span style="width:44px;height:2px;flex:none;background:' + l.duct + ';animation:' + l.ductAnim + '"></span>'
      + '<span style="font-size:11.5px;color:var(--cb-text-4)">' + l.pumpNote + '</span>'
      + '</div>'
      + '</div>').join('')
    + '</div>'
    + '<div style="flex:none;display:flex;align-items:baseline;justify-content:space-between;border-top:1px solid var(--cb-hairline);padding-top:9px">'
    + '<span style="font-size:13.5px;color:var(--cb-text-2)">' + v.supplyFootA + '</span>'
    + '<span style="font-size:13.5px;color:var(--cb-text-4)">' + v.supplyFootB + '</span>'
    + '</div>'
    + '</div>';
}

function renderFilter(v) {
  return '<div style="flex:1;display:flex;flex-direction:column;gap:14px;padding:22px 24px;min-height:0">'
    + heroReadout({ label: 'Filtration throughput', value: v.filtStr, unit: 'mol/s', size: '54px', note: v.filtNote, stats: v.filtStats, panelTone: v.filtTone, fill: v.filtFill, fillTone: v.filtFillTone })
    + nodeStrip({ nodes: v.nodes, flowing: v.flowing })
    + '<div style="display:grid;grid-template-columns:repeat(3,1fr);gap:1px;background:var(--cb-hairline);border:1px solid var(--cb-hairline)">'
    + statTile({ border: false, label: 'CH₄ filter', value: v.ch4Filter, unit: '%', note: 'remaining', fill: v.ch4Filter, fillTone: v.ch4FilterTone, tone: v.ch4FilterTone2 })
    + statTile({ border: false, label: 'Pollutant filter', value: v.pollFilter, unit: '%', note: 'remaining', fill: v.pollFilter, fillTone: v.pollFilterTone, tone: v.pollFilterTone2 })
    + statTile({ border: false, label: 'Output pump', value: v.outPump, unit: 'L', note: v.outPumpNote, fill: v.outPumpFill, fillTone: 'steel' })
    + '</div>'
    + '<div style="flex:1;display:flex;flex-direction:column;min-height:0">'
    + statRow({ label: 'Filtration unit', value: v.filtMode, tone: v.filtModeTone, rule: 'top' })
    + statRow({ label: 'Intake vent', value: v.ventState, unit: v.ventUnit, tone: v.ventTone, rule: 'top' })
    + statRow({ label: 'Safety vent · trip', value: v.safetyState, tone: v.safetyTone, clamped: v.safetyClamped, rule: 'top' })
    + statRow({ label: 'Passive vent · AC', value: v.acMode, tone: v.acTone, rule: 'top' })
    + '</div>'
    + '</div>';
}

function renderAlarm(v) {
  return '<div style="flex:1;display:flex;flex-direction:column;gap:14px;padding:22px 24px;min-height:0">'
    + '<div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:1px;background:var(--cb-hairline);border:1px solid var(--cb-hairline)">'
    + alarmTile({ label: 'Pressure', detail: v.alPressDetail, state: v.alPress, value: v.alPressVal, stamp: v.alPressStamp })
    + alarmTile({ label: 'O₂ deficit', detail: v.alO2Detail, state: v.alO2, value: v.alO2Val, stamp: v.alO2Stamp })
    + alarmTile({ label: 'Pollutant · CH₄', detail: v.alPollDetail, state: v.alPoll, value: v.alPollVal, stamp: v.alPollStamp })
    + alarmTile({ label: 'Temp deviation', detail: v.alTempDetail, state: v.alTemp, value: v.alTempVal, stamp: v.alTempStamp })
    + alarmTile({ label: 'Unexpected gas', detail: v.alOtherDetail, state: v.alOther, value: v.alOtherVal, stamp: v.alOtherStamp })
    + alarmTile({ label: 'Supply reserve', detail: v.alSupplyDetail, state: v.alSupply, value: v.alSupplyVal, stamp: v.alSupplyStamp })
    + '</div>'
    + '<div style="flex:1;display:flex;flex-direction:column;gap:6px;min-height:0;overflow:hidden">'
    + '<div style="display:flex;justify-content:space-between;align-items:baseline">'
    + '<div style="font:500 12.5px var(--cb-font-display);letter-spacing:.16em;text-transform:uppercase;color:var(--cb-text-3)">Event log</div>'
    + '<div style="font-family:var(--cb-font-body);font-size:11.5px;color:var(--cb-text-4)">' + v.logNote + '</div>'
    + '</div>'
    + v.log.map((e) =>
      '<div style="display:grid;grid-template-columns:64px 1fr auto;gap:11px;align-items:baseline;padding:6px 0;border-top:1px solid var(--cb-hairline-inner)">'
      + '<span style="font:500 12.5px var(--cb-font-display);letter-spacing:.12em;color:var(--cb-text-4)">' + e.t + '</span>'
      + '<span style="font-family:var(--cb-font-body);font-size:12.5px;color:var(--cb-text-1-dim)">' + e.msg + '</span>'
      + '<span style="font:500 12.5px var(--cb-font-display);letter-spacing:.12em;color:' + e.color + '">' + e.tag + '</span>'
      + '</div>').join('')
    + '</div>'
    + '<div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:9px">'
    + consoleButton({ onClick: v.toggleSound, children: v.soundLabel })
    + consoleButton({ onClick: v.toggleLight, children: v.lightLabel })
    + consoleButton({ variant: 'danger', onClick: v.purge, children: 'PURGE ROOM' })
    + '</div>'
    + '</div>';
}

function renderConfig(v) {
  const rowList = (list) => list.map((r) => '<div' + act(r.pick) + ' style="' + r.style + '"><span style="' + r.dot + '"></span><span style="font:600 15px \'Barlow Condensed\',sans-serif;letter-spacing:.06em;width:90px;flex:none">' + r.label + '</span><span style="' + r.nameStyle + '">' + r.bound + '</span></div>').join('');
  return '<div style="flex:1;display:flex;flex-direction:column;min-height:0;overflow:hidden">'
    + '<div style="display:flex;align-items:center;justify-content:space-between;padding:0 30px;height:56px;flex:none;border-bottom:1px solid #232c37;background:#0a0a0a">'
    + '<span style="font:600 18px \'Barlow Condensed\',sans-serif;letter-spacing:.18em;text-transform:uppercase;color:#98989b">Configuration · cog in CONFIG</span>'
    + '<span style="' + v.unboundStyle + '">' + v.unboundLabel + '</span></div>'
    + '<div style="flex:1;display:flex;gap:16px;padding:14px 20px;min-height:0">'
    + '<div style="width:280px;flex:none;display:flex;flex-direction:column;min-height:0;overflow:hidden">'
    + '<div style="font:500 13px \'Barlow Condensed\',sans-serif;letter-spacing:.16em;text-transform:uppercase;color:#7a7a7d;margin:5px 0 1px">Gas side</div>' + rowList(v.rowsGas)
    + '<div style="font:500 13px \'Barlow Condensed\',sans-serif;letter-spacing:.16em;text-transform:uppercase;color:#7a7a7d;margin:5px 0 1px">Filtration</div>' + rowList(v.rowsFilt)
    + '<div style="font:500 13px \'Barlow Condensed\',sans-serif;letter-spacing:.16em;text-transform:uppercase;color:#7a7a7d;margin:5px 0 1px">Atmos &amp; alarms</div>' + rowList(v.rowsCtl)
    + '</div>'
    + '<div style="flex:1;display:flex;flex-direction:column;gap:9px;min-height:0">'
    + '<div style="border-bottom:1px solid #232c37;padding-bottom:8px;flex:none">'
    + '<div style="font:600 30px \'Barlow Condensed\',sans-serif;letter-spacing:.05em">' + v.selLabel + '</div>'
    + '<div style="font-size:12.5px;color:#7a7a7d">' + v.selSub + '</div></div>'
    + '<div style="font:500 13px \'Barlow Condensed\',sans-serif;letter-spacing:.16em;text-transform:uppercase;color:#7a7a7d;flex:none">' + v.selCount + '</div>'
    + '<div style="display:flex;flex-direction:column;gap:7px;flex:none">'
    + v.candidates.map((c) => '<div' + act(c.bind) + ' style="' + c.style + '">'
      + '<div><div style="font:600 19px \'Barlow Condensed\',sans-serif;letter-spacing:.05em">' + c.name + '</div><div style="font-size:12px;color:#7a7a7d">' + c.meta + '</div></div>'
      + '<span style="' + c.tagStyle + '">' + c.tag + '</span></div>').join('')
    + '</div>'
    + '<div style="margin-top:auto;border:1px solid #232c37;padding:11px 13px;flex:none">'
    + '<div style="font:500 13px \'Barlow Condensed\',sans-serif;letter-spacing:.16em;text-transform:uppercase;color:#7a7a7d">How binding works</div>'
    + '<div style="font-size:12.5px;line-height:1.5;color:#98989b;margin-top:3px">Roles resolve by prefab + name hash, so only devices of the matching prefab are listed. The regulator keeps running on the old binding until you write.</div></div>'
    + '</div>'
    + '</div>'
    + '<div style="display:grid;grid-template-columns:1fr 1fr 1.2fr;height:52px;flex:none;border-top:1px solid var(--cb-hairline)">'
    + '<div' + act(v.exitConfig) + ' style="display:flex;align-items:center;justify-content:center;font:600 16px var(--cb-font-display);letter-spacing:.1em;color:var(--cb-text-3);cursor:pointer">COG TO EXIT</div>'
    + '<div' + act(v.rescan) + ' style="display:flex;align-items:center;justify-content:center;font:600 16px var(--cb-font-display);letter-spacing:.1em;color:var(--cb-live);border-left:1px solid var(--cb-hairline-inner);cursor:pointer">RESCAN</div>'
    + '<div' + act(v.writeBindings) + ' style="' + v.writeStyle + '">' + v.writeLabel + '</div>'
    + '</div>'
    + '</div>';
}

function render() {
  acts = [];
  const v = values();
  const frame = document.getElementById('frame');
  frame.innerHTML =
    (v.notConfig ? screenHeader({ title: v.headTitle, status: v.headStatus, tone: v.headTone, dot: true }) : '')
    + (v.isAtmo ? renderAtmo(v) : v.isSupply ? renderSupply(v) : v.isFilter ? renderFilter(v) : v.isAlarm ? renderAlarm(v) : v.isConfig ? renderConfig(v) : '')
    + (v.notConfig ? tabBar({ tabs: v.tabs, active: v.tab, onSelect: v.setTab, trailing: v.gear }) : '');
  const bound = document.querySelectorAll('[data-act]');
  for (let i = 0; i < bound.length; i++) {
    const el = bound[i], idx = Number(el.getAttribute('data-act'));
    el.addEventListener('click', () => acts[idx]());
  }
}
render();
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

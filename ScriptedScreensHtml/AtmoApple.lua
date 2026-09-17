-- AtmoApple.lua -- an Apple-style atmosphere regulator console, written as an ordinary web page (HTML, CSS and a script) and embedded here.
-- Paste into a Lua chip in a ScriptedScreens console; a 3x3 console shows it best (design width in the page).
-- Maintainers: generated from AtmoApple.html in the repository (https://github.com/Gruffuss/scripted-screens-html); edit the html there.
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
<title>Atmo Regulator, Apple style</title>
<style>
  :root {
    --desk:#e9e9ee; --bg:#f5f5f7; --card:#ffffff; --knob:#ffffff; --seg-thumb:#ffffff;
    --label:#000000; --label2:#55555a; --sep:#c6c6c8; --track:#e3e3e8;
    --seg:#ebebf0; --fill:#eeeef1; --dot-idle:#c7c7cc; --tick:#8a8a8e;
    --blue:#007aff; --gauge:#5aa9ff; --green:#34c759; --green-ink:#248a3d;
    --orange:#ff9500; --orange-ink:#8f5000; --red:#ff3b30; --red-ink:#c0271d;
    --tint-amber:#fff6e8; --tint-red:#fff0ef; --tint-blue:#eef4ff; --tint-green:#eef9f0;
    --pill-amber:#ffedd4; --pill-red:#ffe1df;
    --shadow:0 24px 60px -26px rgba(0,0,0,.3),0 0 0 .5px rgba(0,0,0,.07);
    --seg-shadow:0 3px 8px rgba(0,0,0,.12),0 3px 1px rgba(0,0,0,.04);
    --half-hover:#e4e4e9;
  }
  [data-mode="dark"] {
    --desk:#000000; --bg:#1c1c1e; --card:#2c2c2e; --knob:#ffffff;
    --label:#ffffff; --label2:rgba(235,235,245,.62); --sep:rgba(84,84,88,.72);
    --track:rgba(120,120,128,.32); --seg:rgba(120,120,128,.24); --seg-thumb:#5b5b5f;
    --fill:rgba(120,120,128,.28); --dot-idle:#5b5b5f; --tick:rgba(235,235,245,.42);
    --blue:#0a84ff; --gauge:#0a84ff; --green:#30d158; --green-ink:#30d158;
    --orange:#ff9f0a; --orange-ink:#ffb340; --red:#ff453a; --red-ink:#ff6961;
    --tint-amber:rgba(255,159,10,.14); --tint-red:rgba(255,69,58,.14);
    --tint-blue:rgba(10,132,255,.16); --tint-green:rgba(48,209,88,.14);
    --pill-amber:rgba(255,159,10,.22); --pill-red:rgba(255,69,58,.22);
    --shadow:0 0 0 .5px rgba(255,255,255,.08),0 24px 60px -26px rgba(0,0,0,.9);
    --seg-shadow:0 1px 3px rgba(0,0,0,.5);
    --half-hover:rgba(120,120,128,.45);
  }
  [data-accent="green"] { --blue:#30b14b; --gauge:#5cc46f; }
  [data-accent="graphite"] { --blue:#6c6c72; --gauge:#9a9aa1; }
  body { margin:0; background:var(--desk); font-family:'Manrope','Helvetica Neue',Helvetica,Arial,sans-serif; font-size:24px; line-height:30px; color:var(--label); }
  *::-webkit-scrollbar { width:0; height:0; }
  * { scrollbar-width:none; box-sizing:border-box; }
  @keyframes ap-pulse { 0%,100% { opacity:1 } 50% { opacity:.25 } }
  @keyframes ap-march { to { background-position:12px 0 } }
  .half:hover { background:var(--half-hover); }
  .tab:hover { opacity:.85; }
</style>
</head>
<body>
<div id="panel" style="position:relative;width:806px;height:806px;overflow:hidden;display:flex;flex-direction:column;gap:12px;padding:18px;background:var(--bg);border-radius:48px;box-shadow:var(--shadow);color:var(--label)">
  <div style="flex:none;display:flex;align-items:center;justify-content:space-between;gap:16px;padding:0 4px">
    <span style="display:flex;min-width:0"><span id="headTitle" style="font-size:34px;font-weight:700;letter-spacing:-.012em;line-height:41px;color:var(--label);white-space:nowrap"></span></span>
    <span id="headStatusWrap" style="display:flex;align-items:center;gap:6px;flex:none;font-size:20px;font-weight:600;line-height:26px;white-space:nowrap"><span id="headLamp" style="width:9px;height:9px;border-radius:50%"></span><span id="headStatus"></span></span>
  </div>
  <div id="tabs" style="flex:none;display:flex;gap:2px;padding:2px;border-radius:999px;background:var(--seg)"></div>
  <div id="screen" style="flex:1;display:flex;flex-direction:column;gap:12px;min-height:0"></div>
</div>
<script>
const INK = 'var(--label)', INK2 = 'var(--label2)';
const BLUE = 'var(--blue)', GREEN = 'var(--green-ink)', GREEN_ON = 'var(--green)', ORANGE_INK = 'var(--orange-ink)', ORANGE = 'var(--orange)', RED = 'var(--red)', RED_INK = 'var(--red-ink)';
const CARD = 'var(--card)', TRACK = 'var(--track)', HAIR = 'var(--sep)';
const TINT_ORANGE = 'var(--tint-amber)', TINT_RED = 'var(--tint-red)', TINT_BLUE = 'var(--tint-blue)', TINT_GREEN = 'var(--tint-green)';

const SCEN = {
  nominal:    { press: 101.1, temp: 21.2, o2: 20.9, co2: 0.34, poll: 0, ch4: 0, vol: 0, nox: 0, steam: 0.03 },
  correcting: { press: 88.4,  temp: 14.6, o2: 15.8, co2: 0.92, poll: 0.06, ch4: 0.03, vol: 0, nox: 0, steam: 0.12 },
  tripped:    { press: 143.6, temp: 29.4, o2: 18.2, co2: 2.41, poll: 0.62, ch4: 0.41, vol: 0.21, nox: 0.06, steam: 0.34 }
};
const ROLES = (() => {
  const PA = [['O2-AN', '2 039 010', '20.9 % O2 · 2.4 k mol'], ['CO2-AN', '2 039 011', '0.9 % CO2 · 0.8 k mol'], ['N-AN', '2 039 012', '78 % N2 · 1.1 k mol'], ['Pipe Analyzer', '2 042 780', '0 kPa · unlabelled']];
  const VP = [['O2-Pump', '2 039 020', 'setting 6.2 L · on'], ['CO2-Pump', '2 039 021', 'setting 1.4 L · on'], ['N-Pump', '2 039 022', 'setting 0 L · off'], ['Volume Pump', '2 042 300', 'setting 0 · unlabelled']];
  const VENT = [['Vent-Filt', '2 039 041', 'MODE 0 · setting 130'], ['Vent-Safety', '2 039 042', 'MODE 0 · setting 140'], ['Powered Vent', '2 042 410', 'MODE 1 · unlabelled']];
  const R = (k, g, sub, devs) => ({ key: k, group: g, sub: sub, devs: devs.map((x) => ({ name: x[0], id: x[1], meta: x[2] })) });
  return [
    R('O2-AN', 'gas', 'Pipe Analyzer · oxygen supply line into the room', PA),
    R('O2-Pump', 'gas', 'Volume Pump · injects O2 against the O2 target', VP),
    R('CO2-AN', 'gas', 'Pipe Analyzer · carbon dioxide supply line', PA),
    R('CO2-Pump', 'gas', 'Volume Pump · trims CO2 up to the ceiling', VP),
    R('N-AN', 'gas', 'Pipe Analyzer · nitrogen line, optional buffer gas', PA),
    R('N-Pump', 'gas', 'Volume Pump · nitrogen makeup, optional', VP),
    R('Filt', 'filt', 'Filtration unit · CH4 and pollutant filters', [['Filt', '2 039 050', 'ON · 2 filters seated'], ['Filtration', '2 042 500', 'OFF · unlabelled']]),
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
  { key: 'o2', field: 'o2', label: 'O2 target', unit: '%', step: 0.5, min: 0, max: 40, fmt: f1 },
  { key: 'co2', field: 'co2', label: 'CO2 target', unit: '%', step: 0.05, min: 0, max: 5, fmt: f2 },
  { key: 'press', field: 'press', label: 'Pressure', unit: 'kPa', step: 2, min: 0, max: 200, fmt: f1 },
  { key: 'temp', field: 'temp', label: 'Temp', unit: '°C', step: 0.5, min: -40, max: 60, fmt: f1 },
  { key: 'trip', field: 'trip', label: 'Vent trip', unit: 'kPa', step: 5, min: 20, max: 300, fmt: (v) => v.toFixed(0) }
];
const groupCard = (extra) => 'border-radius:28px;background:' + CARD + ';' + (extra || '');
const switchTrack = (on) => 'flex:none;position:relative;width:64px;height:38px;border-radius:19px;background:' + (on ? GREEN_ON : TRACK);
const switchKnob = (on) => 'position:absolute;top:2px;left:' + (on ? '28px' : '2px') + ';width:34px;height:34px;border-radius:50%;background:var(--knob);box-shadow:0 3px 8px rgba(0,0,0,.15),0 1px 1px rgba(0,0,0,.16)';

// ---- the prototype's state and simulation, unchanged apart from setState -> a render call
const props = { scenario: 'correcting', simSpeed: 1, nitrogenLine: false, roomName: 'Hab core', startDark: false, startAccent: 'blue' };
const st = {
  tab: ((typeof location !== 'undefined' && location.hash) || '#atmo').slice(1) || 'atmo', tick: 0,
  r: { ...(SCEN[props.scenario] || SCEN.correcting) },
  t: { press: 101.3, temp: 21.0, o2: 21.0, co2: 0.30, trip: 140 },
  filters: { ch4: 76, poll: 62 },
  sound: true, light: true, purging: false,
  dark: null, accent: null,
  role: 'N-AN', sp: 'o2',
  lines: { o2: { temp: 11.8, press: 3140, mol: 2412, pump: 6.2 }, co2: { temp: 14.2, press: 1880, mol: 1046, pump: 1.4 }, n2: { temp: 9.6, press: 4270, mol: 3318, pump: 0 } },
  dirty: false, manual: {},
  bound: ROLES.reduce((a, r) => { if (r.key.indexOf('N-') !== 0) a[r.key] = r.key; return a; }, {}),
  log: [
    { t: '02:14:06', msg: 'Regulator armed · sampling room atmosphere', tag: 'Info', color: INK2 },
    { t: '02:13:52', msg: 'Bindings loaded · 13 of 15 roles set', tag: 'Config', color: INK2 },
    { t: '02:11:30', msg: 'Pollutant filter swapped · 62 % remaining', tag: 'Maint', color: INK2 }
  ],
  bindAtStart: true, bindAtEnd: false
};
let alarmPrev = null;
function clock(tick) {
  const total = 8046 + Math.round((tick == null ? st.tick : tick) * 0.42);
  const h = String(Math.floor(total / 3600) % 24).padStart(2, '0');
  const m = String(Math.floor(total / 60) % 60).padStart(2, '0');
  const s = String(total % 60).padStart(2, '0');
  return h + ':' + m + ':' + s;
}
function push(msg, tag, color) { st.log = [{ t: clock(), msg, tag, color }].concat(st.log).slice(0, 8); }
function step() {
  const k = props.simSpeed;
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
    press: ['Pressure back inside window · ' + f1(r.press) + ' kPa', 'Pressure drifting · ' + f1(r.press) + ' kPa off set', 'Pressure outside window · ' + f1(r.press) + ' kPa'],
    o2: ['O2 restored · ' + f1(r.o2) + ' %', 'O2 below 19.5 % · pump commanded open', 'O2 deficit · ' + f1(r.o2) + ' % · breathable limit'],
    poll: ['Pollutant cleared · back to zero', 'Pollutant detected · ' + f2(r.poll + r.ch4) + ' % · filtration on', 'Pollutant well over zero · ' + f2(r.poll + r.ch4) + ' %'],
    temp: ['Room temp back on setpoint', 'Temp deviation ' + f1(r.temp - t.temp) + ' °C · AC correcting', 'Temp deviation ' + f1(r.temp - t.temp) + ' °C · AC at limit']
  };
  const RANK = { ok: 0, caution: 1, trip: 2 };
  Object.keys(now).forEach((k) => {
    if (now[k] === prev[k]) return;
    const i = RANK[now[k]];
    push(COPY[k][i], i === 2 ? 'Trip' : i === 1 ? 'Watch' : 'Clear', i === 2 ? RED_INK : i === 1 ? ORANGE_INK : GREEN);
    prev[k] = now[k];
  });
}
function bump(key, d, min, max) { st.t[key] = clamp(Math.round((st.t[key] + d) * 100) / 100, min, max); render(true); }

// ---- rendering: the prototype's templates as strings; a click handler is an index into acts[]
let acts = [];
const act = (fn) => { acts.push(fn); return ' data-act="' + (acts.length - 1) + '"'; };
const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;');
const lamp = (color, anim) => '<span style="width:9px;height:9px;border-radius:50%;background:' + color + ';animation:' + anim + '"></span>';

function values() {
  const r = st.r, t = st.t;
  const nitro = !!props.nitrogenLine;
  const n2 = clamp(100 - r.o2 - r.co2 - r.poll - r.ch4 - (r.vol || 0) - (r.nox || 0) - (r.steam || 0), 0, 100);
  const tN2 = clamp(100 - t.o2 - t.co2, 0, 100);
  const o2Duty = clamp((t.o2 - r.o2) * 34, 0, 100);
  const co2Duty = clamp((t.co2 - r.co2) * 220, 0, 100);
  const filtLoad = clamp((r.co2 - t.co2) * 60 + r.poll * 90 + r.ch4 * 90, 0, 100);
  const dT = r.temp - t.temp;
  const acMode = Math.abs(dT) < 0.4 ? 'Idle' : dT > 0 ? 'Cooling' : 'Heating';
  const dP = r.press - t.press;
  const venting = r.press > t.trip || st.purging;
  const pressState = Math.abs(dP) > 20 ? 'trip' : Math.abs(dP) > 9 ? 'caution' : 'ok';
  const o2State = r.o2 < 17 ? 'trip' : r.o2 < 19.5 ? 'caution' : 'ok';
  const foreign = [{ n: 'Volatiles', v: r.vol || 0 }, { n: 'N2O', v: r.nox || 0 }, { n: 'Steam', v: r.steam || 0 }];
  const detected = foreign.filter((g) => g.v > 0.005);
  const foreignSum = foreign.reduce((a, g) => a + g.v, 0);
  const foreignState = detected.some((g) => g.n !== 'Steam') ? 'trip' : detected.length ? 'caution' : 'ok';
  const pollState = (r.poll + r.ch4) > 0.1 ? 'trip' : (r.poll + r.ch4) > 0.001 ? 'caution' : 'ok';
  const tempState = Math.abs(dT) > 6 ? 'trip' : Math.abs(dT) > 3 ? 'caution' : 'ok';
  const worst = [pressState, o2State, pollState, tempState, foreignState];
  const anyTrip = worst.indexOf('trip') >= 0, anyCaution = worst.indexOf('caution') >= 0;
  const inkOf = (s) => s === 'trip' ? RED_INK : s === 'caution' ? ORANGE_INK : GREEN;
  const barOf = (s) => s === 'trip' ? RED : s === 'caution' ? ORANGE : GREEN_ON;
  const lowestMol = Math.min(st.lines.o2.mol, st.lines.co2.mol, nitro ? st.lines.n2.mol : Infinity);
  const reserveTotal = st.lines.o2.mol + st.lines.co2.mol + (nitro ? st.lines.n2.mol : 0);
  const drawTotal = st.lines.o2.pump + st.lines.co2.pump + (nitro ? st.lines.n2.pump : 0);
  return { r, t, nitro, n2, tN2, o2Duty, co2Duty, filtLoad, dT, acMode, dP, venting, pressState, o2State, foreign, detected, foreignSum, foreignState, pollState, tempState, anyTrip, anyCaution, inkOf, barOf, lowestMol, reserveTotal, drawTotal };
}

function renderHeader(v) {
  const titles = { atmo: props.roomName, supply: 'Supply', filter: 'Filtration', alarm: 'Alarms', config: 'Devices' };
  document.getElementById('headTitle').textContent = titles[st.tab];
  const status = st.purging ? 'Purging' : v.anyTrip ? 'Tripped' : v.anyCaution ? 'Correcting' : 'Holding';
  const tone = st.purging || v.anyTrip ? RED_INK : v.anyCaution ? ORANGE_INK : GREEN;
  const wrap = document.getElementById('headStatusWrap');
  wrap.style.color = tone;
  document.getElementById('headStatus').textContent = status;
  const dot = document.getElementById('headLamp');
  dot.style.background = st.purging || v.anyTrip ? RED : v.anyCaution ? ORANGE : GREEN_ON;
  dot.style.animation = st.purging || v.anyTrip ? 'ap-pulse .9s ease-in-out infinite' : v.anyCaution ? 'ap-pulse 1.6s ease-in-out infinite' : 'none';
}
function renderTabs() {
  const tabs = [{ id: 'atmo', label: 'Atmo' }, { id: 'supply', label: 'Supply' }, { id: 'filter', label: 'Filter' }, { id: 'alarm', label: 'Alarms' }, { id: 'config', label: 'Devices' }];
  document.getElementById('tabs').innerHTML = tabs.map((tb) =>
    '<div class="tab"' + act(() => { st.tab = tb.id; render(true); }) + ' style="flex:1;display:flex;align-items:center;justify-content:center;height:46px;border-radius:999px;font-size:18px;line-height:24px;'
    + (st.tab === tb.id ? 'background:var(--seg-thumb);font-weight:600;color:' + INK + ';box-shadow:var(--seg-shadow)' : 'font-weight:400;color:' + INK) + '">' + tb.label + '</div>').join('');
}

function renderAtmo(v) {
  const r = v.r, t = v.t;
  const heroBg = v.pressState === 'trip' ? TINT_RED : v.pressState === 'caution' ? TINT_ORANGE : CARD;
  const heroInk = v.pressState === 'trip' ? RED_INK : v.pressState === 'caution' ? ORANGE_INK : INK;
  const heroTag = v.venting ? 'Venting' : v.pressState === 'trip' ? 'Out of window' : v.pressState === 'caution' ? 'Correcting' : 'On target';
  const heroLine = 'Set ' + f1(t.press) + ' kPa · trip ' + t.trip.toFixed(0) + ' · ' + (v.venting ? 'safety vent dumping' : (v.dP > 0 ? 'bleeding ' : 'charging ') + f1(Math.abs(v.dP)) + ' kPa');
  const tempInk = v.tempState === 'trip' ? RED_INK : v.tempState === 'caution' ? ORANGE_INK : INK;
  const acInk = v.acMode === 'Idle' ? INK2 : GREEN;
  const gases = [
    { label: 'O2', value: f1(r.o2), color: v.o2State === 'trip' ? RED_INK : v.o2State === 'caution' ? ORANGE_INK : INK, fill: clamp(r.o2 / 30 * 100, 0, 100).toFixed(1) + '%', mark: clamp(t.o2 / 30 * 100, 0, 100).toFixed(1) + '%', bar: v.barOf(v.o2State), note: 'Target ' + f1(t.o2) + ' % · pump ' + v.o2Duty.toFixed(0) + ' %', sp: 'o2' },
    { label: 'N2', value: f1(v.n2), color: INK, fill: clamp(v.n2, 0, 100).toFixed(1) + '%', mark: clamp(v.tN2, 0, 100).toFixed(1) + '%', bar: '#aeaeb2', note: v.nitro ? 'Residual ' + f1(v.tN2) + ' % · line live' : 'Residual ' + f1(v.tN2) + ' % · line unbound' },
    { label: 'CO2', value: f2(r.co2), color: r.co2 > t.co2 * 1.5 ? ORANGE_INK : INK, fill: clamp(r.co2 / 3 * 100, 0, 100).toFixed(1) + '%', mark: clamp(t.co2 / 3 * 100, 0, 100).toFixed(1) + '%', bar: r.co2 > t.co2 * 1.5 ? ORANGE : GREEN_ON, note: 'Target ' + f2(t.co2) + ' % · pump ' + v.co2Duty.toFixed(0) + ' %', sp: 'co2' },
    { label: 'X · CH4', value: f2(r.poll + r.ch4), color: v.pollState === 'trip' ? RED_INK : v.pollState === 'caution' ? ORANGE_INK : INK, fill: clamp((r.poll + r.ch4) * 100, 0, 100).toFixed(1) + '%', mark: '0%', bar: v.pollState === 'trip' ? RED : v.pollState === 'caution' ? ORANGE : '#aeaeb2', note: v.pollState === 'ok' ? 'Ceiling zero · clear' : 'Ceiling zero · filtration ' + v.filtLoad.toFixed(0) + ' %' },
    { label: 'Other', value: f2(v.foreignSum), color: v.foreignState === 'trip' ? RED_INK : v.foreignState === 'caution' ? ORANGE_INK : INK, fill: clamp(v.foreignSum * 100, 0, 100).toFixed(1) + '%', mark: '0%', bar: v.foreignState === 'trip' ? RED : v.foreignState === 'caution' ? ORANGE : '#aeaeb2', note: v.detected.length ? v.detected.map((g) => g.n + ' ' + f2(g.v)).join(' · ') : 'No unexpected gas detected' }
  ];
  const gasRows = gases.map((g, i, arr) => {
    const on = g.sp && g.sp === st.sp;
    const live = (g.sp === 'o2' && v.o2Duty > 1) || (g.sp === 'co2' && v.co2Duty > 1);
    return '<div' + (g.sp ? act(() => { st.sp = g.sp; render(true); }) : '') + ' style="display:grid;grid-template-columns:12px minmax(0,1fr) 132px minmax(240px,300px);align-items:center;gap:18px;flex:1;min-height:0;padding:0 24px;'
      + (i ? 'border-top:.5px solid ' + HAIR + ';' : 'border-radius:28px 28px 0 0;') + (i === arr.length - 1 ? 'border-radius:0 0 28px 28px;' : '') + 'background:' + (on ? TINT_BLUE : 'transparent') + '">'
      + '<span style="width:12px;height:12px;flex:none;border-radius:50%;background:' + (live ? GREEN_ON : 'transparent') + ';animation:' + (live ? 'ap-pulse 1.1s ease-in-out infinite' : 'none') + '"></span>'
      + '<span style="display:flex;flex-direction:column;min-width:0;gap:1px"><span style="font-size:22px;font-weight:600;line-height:26px;color:var(--label);white-space:nowrap">' + g.label + '</span>'
      + '<span style="font-size:14px;font-weight:400;line-height:18px;color:var(--label2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + g.note + '</span></span>'
      + '<span style="display:flex;align-items:baseline;justify-content:flex-end;gap:6px"><span style="font-size:30px;line-height:36px;font-weight:600;font-variant-numeric:tabular-nums;color:' + g.color + '">' + g.value + '</span><span style="font-size:19px;font-weight:400;color:var(--label2)">%</span></span>'
      + '<span style="position:relative;height:8px;border-radius:4px;background:var(--track)"><span style="position:absolute;left:0;top:0;bottom:0;width:' + g.fill + ';border-radius:4px;background:' + g.bar + '"></span><span style="position:absolute;top:-3px;bottom:-3px;left:' + g.mark + ';width:2px;border-radius:1px;background:var(--tick)"></span></span>'
      + '</div>';
  }).join('');
  const selSet = SETPOINTS.find((s) => s.key === st.sp) || SETPOINTS[0];
  const cards = SETPOINTS.map((sp) => {
    const on = sp.key === st.sp;
    return '<div' + act(() => { st.sp = sp.key; render(true); }) + ' style="flex:1;display:flex;flex-direction:column;gap:4px;padding:12px 16px 14px;border-radius:20px;background:' + (on ? TINT_BLUE : 'transparent') + '">'
      + '<span style="font-size:19px;font-weight:400;line-height:24px;color:' + INK2 + ';white-space:nowrap">' + sp.label + '</span>'
      + '<span style="display:flex;align-items:baseline;gap:3px"><span style="font-size:24px;line-height:30px;font-weight:600;font-variant-numeric:tabular-nums;color:' + (on ? BLUE : INK) + '">' + sp.fmt(t[sp.field]) + '</span><span style="font-size:18px;font-weight:400;color:' + INK2 + '">' + sp.unit + '</span></span></div>';
  }).join('');
  return '<div style="flex:none;display:flex;flex-direction:column;border-radius:26px;padding:12px 20px 12px;background:' + heroBg + '">'
    + '<div style="display:flex;align-items:center;justify-content:space-between;gap:10px"><span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2)">Room pressure</span><span style="font-size:20px;font-weight:600;line-height:22px;color:' + heroInk + '">' + heroTag + '</span></div>'
    + '<div style="display:flex;align-items:baseline;gap:8px;margin-top:2px"><span style="font-size:52px;line-height:56px;font-weight:600;letter-spacing:-.022em;font-variant-numeric:tabular-nums;color:' + heroInk + '">' + f1(r.press) + '</span><span style="font-size:20px;font-weight:400;line-height:25px;color:var(--label2)">kPa</span></div>'
    + '<div style="font-size:18px;font-weight:400;line-height:22px;color:var(--label2);margin-top:2px;white-space:nowrap">' + heroLine + '</div>'
    + '<div style="display:flex;align-items:stretch;margin:10px -20px -12px;border-top:.5px solid var(--sep)">'
    + '<div style="flex:1;display:flex;flex-direction:column;gap:1px;padding:6px 20px 7px"><span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2)">Room temp</span><span style="display:flex;align-items:baseline;gap:4px"><span style="font-size:30px;line-height:36px;font-weight:600;font-variant-numeric:tabular-nums;color:' + tempInk + '">' + f1(r.temp) + '</span><span style="font-size:19px;font-weight:400;color:var(--label2)">&deg;C</span></span></div>'
    + '<div style="flex:1;display:flex;flex-direction:column;gap:1px;padding:6px 20px 7px;border-left:.5px solid var(--sep)"><span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2)">Air conditioner</span><span style="font-size:30px;line-height:36px;font-weight:600;color:' + acInk + '">' + v.acMode + '</span></div>'
    + '<div style="flex:1;display:flex;flex-direction:column;gap:1px;padding:6px 20px 7px;border-left:.5px solid var(--sep)"><span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2)">AC setpoint</span><span style="display:flex;align-items:baseline;gap:4px"><span style="font-size:30px;line-height:36px;font-weight:600;font-variant-numeric:tabular-nums;color:var(--label)">' + f1(t.temp) + '</span><span style="font-size:19px;font-weight:400;color:var(--label2)">&deg;C</span></span></div>'
    + '</div></div>'
    + '<div style="flex:1;min-height:0;display:flex;flex-direction:column;gap:7px"><div style="flex:1;min-height:0;display:flex;flex-direction:column;border-radius:28px;background:var(--card);overflow:hidden">' + gasRows + '</div></div>'
    + '<div style="flex:none;display:flex;flex-direction:column;gap:7px"><div style="display:flex;flex-direction:column;border-radius:28px;background:var(--card);overflow:hidden">'
    + '<div style="display:flex;gap:4px;padding:5px;border-bottom:.5px solid var(--sep)">' + cards + '</div>'
    + '<div style="display:flex;align-items:center;gap:16px;padding:7px 14px 7px 20px">'
    + '<span style="flex:1;min-width:0;display:flex;align-items:center"><span style="font-size:26px;font-weight:500;line-height:32px;color:var(--label)">' + selSet.label + '</span></span>'
    + '<span style="display:flex;align-items:baseline;gap:4px;flex:none"><span style="font-size:32px;line-height:38px;font-weight:600;font-variant-numeric:tabular-nums;color:var(--label)">' + selSet.fmt(t[selSet.field]) + '</span><span style="font-size:22px;font-weight:400;color:var(--label2)">' + selSet.unit + '</span></span>'
    + '<span style="flex:none;display:flex;align-items:center;height:40px;border-radius:999px;overflow:hidden;background:var(--fill)">'
    + '<span class="half"' + act(() => bump(selSet.field, -selSet.step, selSet.min, selSet.max)) + ' style="width:56px;height:40px;display:flex;align-items:center;justify-content:center;color:var(--blue);font-size:28px;font-weight:400">&minus;</span>'
    + '<span style="width:.5px;height:20px;background:var(--sep)"></span>'
    + '<span class="half"' + act(() => bump(selSet.field, selSet.step, selSet.min, selSet.max)) + ' style="width:56px;height:40px;display:flex;align-items:center;justify-content:center;color:var(--blue);font-size:28px;font-weight:400">+</span>'
    + '</span></div></div></div>';
}

function renderSupply(v) {
  const lines = [
    { key: 'o2', name: 'O2', analyzer: 'O2-AN · supply line', live: true, duty: v.o2Duty },
    { key: 'co2', name: 'CO2', analyzer: 'CO2-AN · supply line', live: true, duty: v.co2Duty },
    { key: 'n2', name: 'N2', analyzer: v.nitro ? 'N-AN · supply line' : 'N-AN · not bound', live: v.nitro, duty: 0 }
  ].map((L) => {
    const d = st.lines[L.key], low = d.mol < 400, crit = d.mol < 200, cap = 4200;
    const fill = L.live ? clamp(d.mol / cap * 100, 0, 100).toFixed(0) : '0';
    const barColor = !L.live ? 'var(--dot-idle)' : crit ? RED : low ? ORANGE : 'var(--gauge)';
    const gaugeH = 'calc(' + (L.live ? clamp(d.mol / cap * 100, 4, 100).toFixed(1) : 4) + '% - 12px)';
    const duct = d.pump > 0 && L.live ? 'repeating-linear-gradient(90deg,' + GREEN_ON + ' 0 6px,' + TRACK + ' 6px 12px)' : 'repeating-linear-gradient(90deg,#d1d1d6 0 6px,transparent 6px 12px)';
    const ductAnim = d.pump > 0 && L.live ? 'ap-march .9s linear infinite' : 'none';
    const row = (label, value, color) => '<div style="display:flex;align-items:center;justify-content:space-between;flex:none;min-height:62px;padding:0 24px;border-top:.5px solid var(--sep)"><span style="font-size:20px;font-weight:400;line-height:22px;color:var(--label)">' + label + '</span><span style="font-size:20px;font-weight:400;font-variant-numeric:tabular-nums;color:' + color + '">' + value + '</span></div>';
    return '<div style="' + groupCard('flex:1;min-width:0;display:flex;flex-direction:column;' + (crit ? 'background:' + TINT_RED : low ? 'background:' + TINT_ORANGE : '')) + '">'
      + '<div style="display:flex;align-items:baseline;justify-content:space-between;gap:8px;padding:18px 20px 0"><span style="font-size:20px;font-weight:600;line-height:25px;color:var(--label);white-space:nowrap">' + L.name + ' supply</span><span style="font-size:18px;font-weight:600;line-height:24px;color:' + (!L.live ? INK2 : d.pump > 0 ? GREEN : INK2) + '">' + (!L.live ? 'Unbound' : d.pump > 0 ? 'Pumping' : 'Standby') + '</span></div>'
      + '<div style="display:flex;align-items:baseline;gap:5px;padding:6px 20px 0"><span style="font-size:46px;line-height:54px;font-weight:600;letter-spacing:-.01em;font-variant-numeric:tabular-nums;color:' + (!L.live ? INK2 : crit ? RED_INK : low ? ORANGE_INK : INK) + '">' + (L.live ? d.mol.toFixed(0) : '—') + '</span><span style="font-size:20px;font-weight:400;color:var(--label2)">mol</span></div>'
      + '<div style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2);padding:4px 20px 14px">' + L.analyzer + '</div>'
      + '<div style="flex:1;min-height:120px;position:relative;margin:0 24px 18px;border-radius:16px;background:var(--fill);overflow:hidden"><div style="position:absolute;left:6px;right:6px;bottom:6px;height:' + gaugeH + ';border-radius:14px;background:' + barColor + '"></div><div style="position:absolute;left:16px;top:12px;font-size:18px;font-weight:600;color:var(--label2)">' + fill + ' %</div></div>'
      + row('Pressure', (L.live ? d.press.toFixed(0) : '—') + ' kPa', !L.live ? INK2 : low ? ORANGE_INK : INK)
      + row('Temperature', (L.live ? f1(d.temp) : '—') + ' &deg;C', 'var(--label)')
      + row('Volume pump', (L.live ? d.pump.toFixed(1) : '0.0') + ' L', d.pump > 0 ? GREEN : INK2)
      + '<div style="display:flex;align-items:center;gap:10px;flex:none;padding:18px 24px 20px;border-top:.5px solid var(--sep)"><span style="width:52px;height:5px;border-radius:2.5px;flex:none;background:' + duct + ';animation:' + ductAnim + '"></span><span style="font-size:18px;font-weight:400;color:var(--label2)">' + (!L.live ? 'Off' : d.pump > 0 ? 'Duty ' + L.duty.toFixed(0) + ' %' : 'Idle') + '</span></div>'
      + '</div>';
  }).join('');
  const footA = v.lowestMol < 400 ? 'Lowest tank ' + v.lowestMol.toFixed(0) + ' mol · refill before the room falls off target' : 'All bound tanks above the 400 mol watch line · ' + (v.reserveTotal / 1000).toFixed(2) + ' k mol reserve';
  const footB = (v.drawTotal > 0.1 ? 'Drawing ' + v.drawTotal.toFixed(1) + ' L/s · ' : 'No line drawing · ') + clock();
  return '<div style="flex:1;display:grid;grid-template-columns:repeat(3,1fr);gap:12px;min-height:0">' + lines + '</div>'
    + '<div style="flex:none;display:flex;align-items:baseline;justify-content:space-between;gap:12px;padding:0 6px"><span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2)">' + footA + '</span><span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2)">' + footB + '</span></div>';
}

function renderFilter(v) {
  const r = v.r, t = v.t, filtLoad = v.filtLoad;
  const filtBg = v.pollState === 'trip' ? TINT_RED : filtLoad > 70 ? TINT_ORANGE : CARD;
  const filtInk = v.pollState === 'trip' ? RED_INK : filtLoad > 70 ? ORANGE_INK : INK;
  const filtTagInk = v.pollState === 'trip' ? RED_INK : filtLoad > 70 ? ORANGE_INK : filtLoad > 5 ? GREEN : INK2;
  const filtBar = v.pollState === 'trip' ? RED : filtLoad > 70 ? ORANGE : GREEN_ON;
  const duct = filtLoad > 5 ? 'repeating-linear-gradient(90deg,' + GREEN_ON + ' 0 6px,' + TRACK + ' 6px 12px)' : 'repeating-linear-gradient(90deg,#d1d1d6 0 6px,transparent 6px 12px)';
  const ductAnim = filtLoad > 5 ? 'ap-march .9s linear infinite' : 'none';
  const nodes = [
    { label: 'Room', value: f1(r.press), unit: 'kPa', secondary: f1(r.temp), secondaryUnit: '°C', note: 'Sensor · hab', state: v.pressState },
    { label: 'Intake', value: (r.press * 0.94).toFixed(1), unit: 'kPa', secondary: f1(r.temp - 0.4), secondaryUnit: '°C', note: 'Passive vent', state: 'ok' },
    { label: 'Filtration', value: (60 + filtLoad * 0.6).toFixed(0), unit: 'kPa', secondary: f1(r.temp + 2.1), secondaryUnit: '°C', note: filtLoad > 5 ? 'On · 2 filters' : 'Idle', state: filtLoad > 70 ? 'caution' : 'ok' },
    { label: 'Out pump', value: (120 + filtLoad * 4).toFixed(0), unit: 'kPa', secondary: f1(r.temp + 3.4), secondaryUnit: '°C', note: (filtLoad * 0.9).toFixed(0) + ' L · waste', state: 'ok' },
    { label: 'Waste', value: (4321 + Math.round(filtLoad * 3)).toString(), unit: 'kPa', secondary: '18.6', secondaryUnit: '°C', note: 'Vent 5000', state: 'ok' }
  ].map((n, i, arr) => '<div style="' + groupCard('flex:1;min-width:0;display:flex;flex-direction:column;gap:2px;padding:10px 12px 11px;' + (n.state === 'trip' ? 'background:' + TINT_RED : n.state === 'caution' ? 'background:' + TINT_ORANGE : '')) + '">'
    + '<span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + n.label + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:4px"><span style="font-size:26px;line-height:32px;font-weight:600;font-variant-numeric:tabular-nums;color:' + (n.state === 'trip' ? RED_INK : n.state === 'caution' ? ORANGE_INK : INK) + '">' + n.value + '</span><span style="font-size:13px;font-weight:400;color:var(--label2)">' + n.unit + '</span></span>'
    + '<span style="display:flex;align-items:baseline;gap:4px"><span style="font-size:17px;font-weight:400;line-height:21px;color:var(--label)">' + n.secondary + '</span><span style="font-size:17px;font-weight:400;color:var(--label2)">' + n.secondaryUnit + '</span></span>'
    + '<span style="font-size:15px;font-weight:400;line-height:19px;color:var(--label2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + n.note + '</span>'
    + (i < arr.length - 1 ? '<span style="height:4px;border-radius:2px;margin-top:5px;background:' + duct + ';animation:' + ductAnim + '"></span>' : '')
    + '</div>').join('');
  const tiles = [
    { label: 'CH4 filter', value: st.filters.ch4.toFixed(0), unit: '%', note: 'Remaining', pct: st.filters.ch4 },
    { label: 'Pollutant filter', value: st.filters.poll.toFixed(0), unit: '%', note: 'Remaining', pct: st.filters.poll },
    { label: 'Output pump', value: (filtLoad * 0.9).toFixed(0), unit: 'L', note: filtLoad > 5 ? 'To waste tank' : 'Stopped', pct: filtLoad * 0.9, data: true }
  ].map((ft) => {
    const crit = !ft.data && ft.pct < 15, low = !ft.data && ft.pct < 35;
    return '<div style="' + groupCard('flex:1;display:flex;flex-direction:column;gap:3px;padding:18px 20px 20px;' + (crit ? 'background:' + TINT_RED : low ? 'background:' + TINT_ORANGE : '')) + '">'
      + '<span style="font-size:17px;font-weight:400;line-height:22px;color:var(--label2)">' + ft.label + '</span>'
      + '<span style="display:flex;align-items:baseline;gap:4px"><span style="font-size:28px;line-height:34px;font-weight:600;font-variant-numeric:tabular-nums;color:' + (crit ? RED_INK : low ? ORANGE_INK : INK) + '">' + ft.value + '</span><span style="font-size:20px;font-weight:400;color:var(--label2)">' + ft.unit + '</span></span>'
      + '<span style="font-size:16px;font-weight:400;line-height:21px;color:var(--label2)">' + ft.note + '</span>'
      + '<span style="height:6px;border-radius:3px;margin-top:6px;background:var(--track);overflow:hidden"><span style="display:block;height:100%;border-radius:3px;width:' + clamp(ft.pct, 0, 100).toFixed(1) + '%;background:' + (crit ? RED : low ? ORANGE : BLUE) + '"></span></span></div>';
  }).join('');
  const stats = [
    { label: 'Filtration unit', value: filtLoad > 5 ? 'On · auto' : 'Idle', unit: '', tone: filtLoad > 5 ? 'live' : 'dim' },
    { label: 'Intake vent', value: (t.trip - 10).toFixed(0), unit: 'kPa', tone: 'ink' },
    { label: 'Safety vent trip', value: st.purging ? 'Purge dump' : v.venting ? 'Dumping' : r.press > t.trip - 10 ? 'Armed' : 'Closed', unit: '', tone: v.venting ? 'rust' : r.press > t.trip - 10 ? 'amber' : 'dim' },
    { label: 'Passive vent · AC', value: v.acMode + ' · set ' + f1(t.temp) + ' °C', unit: '', tone: v.acMode === 'Idle' ? 'dim' : 'live' }
  ].map((sr, i) => '<div style="flex:1;min-height:43px;display:flex;align-items:center;justify-content:space-between;gap:10px;padding:0 20px' + (i ? ';border-top:.5px solid ' + HAIR : '') + '">'
    + '<span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label)">' + sr.label + '</span>'
    + '<span style="display:flex;align-items:baseline;gap:5px"><span style="font-size:18px;font-weight:400;line-height:24px;font-variant-numeric:tabular-nums;color:' + (sr.tone === 'rust' ? RED_INK : sr.tone === 'amber' ? ORANGE_INK : sr.tone === 'live' ? GREEN : sr.tone === 'dim' ? INK2 : INK) + '">' + sr.value + '</span><span style="font-size:18px;font-weight:400;color:var(--label2)">' + sr.unit + '</span></span></div>').join('');
  return '<div style="flex:none;border-radius:28px;background:' + filtBg + ';padding:12px 20px 14px">'
    + '<div style="display:flex;align-items:center;justify-content:space-between;gap:10px"><span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2)">Filtration throughput</span><span style="font-size:20px;font-weight:600;line-height:22px;color:' + filtTagInk + '">' + (filtLoad > 5 ? 'Running' : 'Idle') + '</span></div>'
    + '<div style="display:flex;align-items:baseline;gap:8px;margin-top:2px"><span style="font-size:46px;line-height:50px;font-weight:600;letter-spacing:-.02em;font-variant-numeric:tabular-nums;color:' + filtInk + '">' + f2(filtLoad * 0.042) + '</span><span style="font-size:20px;font-weight:400;color:var(--label2)">mol/s</span><span style="flex:1"></span><span style="font-size:18px;font-weight:400;line-height:24px;color:var(--label2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">' + f2(r.co2) + ' % CO2 + ' + f2(r.poll + r.ch4) + ' % X drawn · out at ' + (60 + filtLoad * 0.6).toFixed(0) + ' kPa</span></div>'
    + '<div style="height:7px;border-radius:3.5px;margin-top:8px;background:var(--track);overflow:hidden"><div style="height:100%;border-radius:3px;width:' + clamp(filtLoad, 0, 100).toFixed(1) + '%;background:' + filtBar + '"></div></div></div>'
    + '<div style="flex:none;display:flex;flex-direction:column;gap:7px"><div style="display:flex;align-items:stretch;gap:10px">' + nodes + '</div></div>'
    + '<div style="flex:none;display:flex;gap:12px">' + tiles + '</div>'
    + '<div style="flex:1;min-height:0;display:flex;flex-direction:column;overflow:hidden;border-radius:28px;background:var(--card)">' + stats + '</div>';
}

function renderAlarm(v) {
  const r = v.r, t = v.t;
  const tiles = [
    { label: 'Pressure', state: v.pressState, value: f1(r.press) + ' kPa', detail: f1(t.press - 9) + '–' + f1(t.press + 9) + ' · trip ' + t.trip.toFixed(0) },
    { label: 'O2 deficit', state: v.o2State, value: f1(r.o2) + ' %', detail: 'Watch 19.5 · trip 17.0' },
    { label: 'Pollutant · CH4', state: v.pollState, value: f2(r.poll + r.ch4) + ' %', detail: 'Ceiling 0.00' },
    { label: 'Temp deviation', state: v.tempState, value: f1(v.dT) + ' °C', detail: 'Setpoint ' + f1(t.temp) + ' °C' },
    { label: 'Unexpected gas', state: v.foreignState, value: f2(v.foreignSum) + ' %', detail: v.detected.length ? v.detected.map((g) => g.n).join(', ') : 'None detected' },
    { label: 'Supply reserve', state: v.lowestMol < 200 ? 'trip' : v.lowestMol < 400 ? 'caution' : 'ok', value: v.lowestMol.toFixed(0) + ' mol', detail: 'Watch under 400 mol' }
  ].map((a) => '<div style="' + groupCard('display:flex;flex-direction:column;gap:5px;padding:18px 20px 20px;' + (a.state === 'trip' ? 'background:' + TINT_RED : a.state === 'caution' ? 'background:' + TINT_ORANGE : '')) + '">'
    + '<span style="display:flex;align-items:center;justify-content:space-between;gap:8px"><span style="font-size:16px;font-weight:400;line-height:22px;color:var(--label2);white-space:nowrap">' + a.label + '</span>'
    + '<span style="flex:none;font-size:15px;font-weight:600;line-height:18px;padding:3px 9px;border-radius:999px;color:' + v.inkOf(a.state) + ';background:' + (a.state === 'trip' ? 'var(--pill-red)' : a.state === 'caution' ? 'var(--pill-amber)' : TINT_GREEN) + '">' + (a.state === 'trip' ? 'Trip' : a.state === 'caution' ? 'Watch' : 'Clear') + '</span></span>'
    + '<span style="font-size:27px;line-height:33px;font-weight:600;font-variant-numeric:tabular-nums;color:' + (a.state === 'trip' ? RED_INK : a.state === 'caution' ? ORANGE_INK : INK) + '">' + a.value + '</span>'
    + '<span style="font-size:16px;font-weight:400;line-height:21px;color:var(--label2);white-space:nowrap">' + a.detail + '</span></div>').join('');
  const log = st.log.map((e, i) => '<div style="display:grid;grid-template-columns:96px 1fr auto;gap:12px;align-items:center;flex:none;min-height:58px;padding:12px 24px' + (i ? ';border-top:.5px solid ' + HAIR : '') + '">'
    + '<span style="font-size:18px;font-weight:400;font-variant-numeric:tabular-nums;color:var(--label2)">' + e.t + '</span><span style="font-size:20px;font-weight:400;line-height:22px;color:var(--label)">' + e.msg + '</span><span style="font-size:18px;font-weight:600;line-height:24px;color:' + e.color + '">' + e.tag + '</span></div>').join('');
  return '<div style="flex:none;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));grid-auto-rows:1fr;gap:12px">' + tiles + '</div>'
    + '<div style="flex:1;min-height:0;display:flex;flex-direction:column;gap:7px"><div id="log" style="flex:1;min-height:0;display:flex;flex-direction:column;overflow-y:auto;border-radius:28px;background:var(--card);mask-image:linear-gradient(#000 88%,rgba(0,0,0,.25) 100%)">' + log + '</div></div>'
    + '<div style="flex:none;display:flex;flex-direction:column;border-radius:28px;background:var(--card);overflow:hidden">'
    + '<div' + act(() => { st.sound = !st.sound; push(st.sound ? 'Sound alarm armed' : 'Sound alarm muted at console', 'Alarm', ORANGE_INK); render(true); }) + ' style="display:flex;align-items:center;justify-content:space-between;gap:12px;height:56px;padding:0 22px"><span style="font-size:20px;font-weight:400;line-height:25px;color:var(--label)">Sound alarm</span><span style="' + switchTrack(st.sound) + '"><span style="' + switchKnob(st.sound) + '"></span></span></div>'
    + '<div' + act(() => { st.light = !st.light; push(st.light ? 'Light alarm armed' : 'Light alarm disabled', 'Alarm', ORANGE_INK); render(true); }) + ' style="display:flex;align-items:center;justify-content:space-between;gap:12px;height:56px;padding:0 22px;border-top:.5px solid var(--sep)"><span style="font-size:20px;font-weight:400;line-height:25px;color:var(--label)">Light alarm</span><span style="' + switchTrack(st.light) + '"><span style="' + switchKnob(st.light) + '"></span></span></div>'
    + '<div' + act(() => { if (st.purging) return; st.purging = true; push('Room purge commanded · safety vent open', 'Purge', RED_INK); render(true); }) + ' style="display:flex;align-items:center;justify-content:center;height:68px;padding:0 24px;border-top:.5px solid ' + HAIR + ';font-size:20px;font-weight:400;line-height:25px;color:' + (st.purging ? INK2 : RED) + '">' + (st.purging ? 'Purge in progress' : 'Purge room') + '</div>'
    + '</div>';
}

function renderConfig() {
  const bound = st.bound, manual = st.manual;
  const sel = ROLES.find((x) => x.key === st.role) || ROLES[0];
  const unbound = ROLES.filter((x) => !bound[x.key]).length;
  const rows = (g) => ROLES.filter((ro) => ro.group === g).map((ro, i, arr) => {
    const b = bound[ro.key], on = ro.key === st.role;
    const kind = !b ? 'none' : manual[ro.key] ? 'manual' : 'auto';
    return '<div' + act(() => { st.role = ro.key; render(true); }) + ' style="display:flex;align-items:center;gap:10px;height:38px;flex:none;padding:0 16px;'
      + (i ? 'border-top:.5px solid ' + HAIR + ';' : 'border-radius:28px 28px 0 0;') + (i === arr.length - 1 ? 'border-radius:0 0 28px 28px;' : '')
      + 'background:' + (on ? TINT_BLUE : kind === 'none' ? TINT_ORANGE : 'transparent') + '">'
      + '<span style="width:8px;height:8px;flex:none;border-radius:50%;background:' + (kind === 'none' ? ORANGE : kind === 'manual' ? BLUE : 'var(--dot-idle)') + '"></span>'
      + '<span style="font-size:16px;font-weight:600;line-height:22px;width:132px;flex:none;white-space:nowrap;color:var(--label)">' + ro.key + '</span>'
      + '<span style="flex:1;min-width:0;font-size:12px;font-weight:600;line-height:16px;white-space:nowrap;text-align:right;color:' + (b ? INK2 : ORANGE_INK) + '">' + (b || 'Not bound') + '</span></div>';
  }).join('');
  const group = (title, g, first) => '<span style="font-size:14px;font-weight:600;line-height:20px;letter-spacing:.02em;color:var(--label2);padding:' + (first ? '0 8px' : '2px 8px 0') + '">' + title + '</span><div style="flex:none;display:flex;flex-direction:column;border-radius:28px;overflow:hidden;background:var(--card)">' + rows(g) + '</div>';
  const bindMask = listMask(st.bindAtStart !== false, !!st.bindAtEnd);
  const candidates = sel.devs.map((dv, i, arr) => {
    const on = bound[sel.key] === dv.name;
    const taken = !on && Object.keys(bound).some((k) => bound[k] === dv.name);
    return '<div' + act(() => { const off = bound[sel.key] === dv.name; bound[sel.key] = off ? null : dv.name; if (off) delete manual[sel.key]; else manual[sel.key] = true; st.dirty = true; render(true); }) + ' style="display:flex;align-items:center;justify-content:space-between;gap:10px;flex:none;height:60px;padding:0 18px;'
      + (i ? 'border-top:.5px solid ' + HAIR + ';' : 'border-radius:28px 28px 0 0;') + (i === arr.length - 1 ? 'border-radius:0 0 28px 28px;' : '') + 'background:' + (on ? TINT_BLUE : 'transparent') + '">'
      + '<span style="display:flex;flex-direction:column;min-width:0"><span style="font-size:16px;font-weight:600;line-height:22px;color:var(--label)">' + dv.name + '</span><span style="font-size:14px;font-weight:400;line-height:19px;color:var(--label2)">ReferenceId ' + dv.id + ' · ' + dv.meta + '</span></span>'
      + '<span style="flex:none;font-size:15px;font-weight:600;line-height:20px;color:' + (on ? BLUE : taken ? INK2 : BLUE) + '">' + (on ? 'Bound' : taken ? 'In use' : 'Select') + '</span></div>';
  }).join('');
  const mode = (st.dark == null ? !!props.startDark : st.dark) ? 'dark' : 'light';
  const accent = st.accent || props.startAccent;
  const modes = [{ id: 'light', label: 'Light' }, { id: 'dark', label: 'Dark' }].map((m) => '<span' + act(() => { st.dark = m.id === 'dark'; applyTheme(); render(true); }) + ' style="display:flex;align-items:center;justify-content:center;width:64px;height:28px;border-radius:999px;font-size:13px;line-height:18px;'
    + (mode === m.id ? 'background:var(--seg-thumb);font-weight:600;color:var(--label);box-shadow:var(--seg-shadow)' : 'font-weight:400;color:var(--label2)') + '">' + m.label + '</span>').join('');
  const accents = [{ id: 'blue', c: '#0a84ff' }, { id: 'green', c: '#30b14b' }, { id: 'graphite', c: '#6c6c72' }].map((a) => '<span' + act(() => { st.accent = a.id; applyTheme(); render(true); }) + ' style="width:26px;height:26px;border-radius:50%;background:' + a.c + ';box-shadow:' + (accent === a.id ? '0 0 0 2px var(--card),0 0 0 4px ' + a.c : 'none') + '"></span>').join('');
  return '<div style="flex:1;display:flex;gap:18px;min-height:0">'
    + '<div id="bindList" style="width:336px;flex:none;display:flex;flex-direction:column;min-height:0;gap:4px;overflow-y:auto;mask-image:' + bindMask + '">' + group('Gas side', 'gas', true) + group('Filtration', 'filt') + group('Atmos &amp; alarms', 'ctl') + '</div>'
    + '<div style="flex:1;display:flex;flex-direction:column;gap:10px;min-height:0">'
    + '<div style="flex:none;display:flex;align-items:flex-end;justify-content:space-between;gap:12px;padding:0 4px"><span style="display:flex;flex-direction:column;min-width:0"><span style="font-size:20px;font-weight:700;line-height:26px;color:var(--label)">' + sel.key + '</span><span style="font-size:14px;line-height:19px;color:var(--label2)">' + sel.sub + '</span></span><span style="flex:none;font-size:14px;font-weight:600;line-height:24px;color:' + (unbound ? ORANGE_INK : GREEN) + ';white-space:nowrap">' + (unbound ? unbound + (unbound === 1 ? ' role unbound' : ' roles unbound') : 'All ' + ROLES.length + ' roles bound') + '</span></div>'
    + '<div style="flex:0 1 auto;min-height:0;display:flex;flex-direction:column;border-radius:28px;overflow-y:auto;background:var(--card)">' + candidates + '</div>'
    + '<div style="flex:none;margin-top:auto;display:flex;flex-direction:column;border-radius:28px;background:var(--card);overflow:hidden">'
    + '<div style="display:flex;align-items:center;justify-content:space-between;gap:12px;height:52px;padding:0 20px"><span style="font-size:15px;font-weight:600;line-height:20px;color:var(--label)">Appearance</span><span style="display:flex;gap:2px;padding:2px;border-radius:999px;background:var(--seg)">' + modes + '</span></div>'
    + '<div style="display:flex;align-items:center;justify-content:space-between;gap:12px;height:52px;padding:0 20px;border-top:.5px solid var(--sep)"><span style="font-size:15px;font-weight:600;line-height:20px;color:var(--label)">Theme</span><span style="display:flex;gap:10px">' + accents + '</span></div></div>'
    + '<div style="flex:none;margin-top:10px;border-radius:28px;background:var(--card);padding:18px 20px"><div style="font-size:15px;font-weight:600;line-height:20px;color:var(--label)">How binding works</div><div style="font-size:14px;font-weight:400;line-height:19px;color:var(--label2);margin-top:4px">Roles resolve by prefab and name hash, so only devices of the matching prefab are listed. The regulator keeps running on the old binding until you write.</div></div>'
    + '</div></div>'
    + '<div style="flex:none;display:grid;grid-template-columns:1fr 1fr 1.2fr;gap:10px;height:52px">'
    + '<div' + act(() => { st.tab = 'atmo'; render(true); }) + ' style="display:flex;align-items:center;justify-content:center;border-radius:999px;background:var(--card);font-size:16px;font-weight:600;line-height:22px;color:var(--blue)">Return</div>'
    + '<div' + act(() => { push('Device rescan · ' + ROLES.length + ' roles on the network', 'Config', INK2); render(true); }) + ' style="display:flex;align-items:center;justify-content:center;border-radius:999px;background:var(--card);font-size:16px;font-weight:600;line-height:22px;color:var(--blue)">Rescan devices</div>'
    + '<div' + act(() => { st.dirty = false; push('Bindings written to regulator · ' + (ROLES.length - unbound) + ' devices', 'Config', INK2); render(true); }) + ' style="display:flex;align-items:center;justify-content:center;border-radius:999px;font-size:16px;line-height:22px;' + (st.dirty ? 'background:' + BLUE + ';color:#fff;font-weight:600' : 'background:' + TRACK + ';color:' + INK2 + ';font-weight:400') + '">' + (st.dirty ? 'Write bindings' : 'Bindings written') + '</div>'
    + '</div>';
}

function applyTheme() {
  const root = document.documentElement;
  root.setAttribute('data-mode', (st.dark == null ? !!props.startDark : st.dark) ? 'dark' : 'light');
  root.setAttribute('data-accent', st.accent || props.startAccent);
}
let lastTab = null;
// the fade at a list's top or bottom shows there is more to scroll to
function listMask(atStart, atEnd) {
  return 'linear-gradient(' + (atStart ? '#000 0,' : 'rgba(0,0,0,.2) 0,#000 8%,') + (atEnd ? '#000 100%)' : '#000 92%,rgba(0,0,0,.2) 100%)');
}
function render(structural) {
  const v = values();
  renderHeader(v);
  // the Devices tab keeps its scrolled lists: it re-renders on its own changes, not on the simulation tick
  if (!structural && st.tab === 'config') return;
  acts = [];
  renderTabs();
  const screen = document.getElementById('screen');
  // replacing the markup makes new scroll boxes at the top; keep where the reader scrolled to
  const kept = {};
  if (st.tab === lastTab) ['log', 'bindList'].forEach((id) => { const el = document.getElementById(id); if (el) kept[id] = el.scrollTop; });
  screen.innerHTML = st.tab === 'atmo' ? renderAtmo(v) : st.tab === 'supply' ? renderSupply(v) : st.tab === 'filter' ? renderFilter(v) : st.tab === 'alarm' ? renderAlarm(v) : renderConfig();
  const bound = document.querySelectorAll('[data-act]');
  for (let i = 0; i < bound.length; i++) {
    const el = bound[i], idx = Number(el.getAttribute('data-act'));
    el.addEventListener('click', () => acts[idx]());
  }
  Object.keys(kept).forEach((id) => { const el = document.getElementById(id); if (el) el.scrollTop = kept[id]; });
  const list = document.getElementById('bindList');
  if (list) list.addEventListener('scroll', () => {
    // only the fade changes: re-rendering here would put the list back at the top
    const end = list.scrollTop + list.clientHeight >= list.scrollHeight - 2, start = list.scrollTop <= 2;
    if (end !== !!st.bindAtEnd || start !== (st.bindAtStart !== false)) {
      st.bindAtEnd = end; st.bindAtStart = start;
      list.style.maskImage = listMask(start, end);
    }
  });
  lastTab = st.tab;
}
applyTheme();
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

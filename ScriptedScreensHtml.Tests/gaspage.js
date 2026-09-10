
/* GAS TABLE: c = badge colour, c2 = right half of a split badge, t = tank fill colour */
const GAS = [
  { key:'o2',  c:'#DCE6EE' },
  { key:'n2',  c:'#141414', t:'#2E3A4A' },
  { key:'co2', c:'#5B6672', t:'#6E6A64' },
  { key:'x',   c:'#E4BE22' },
  { key:'ch4', c:'#CE3E2C' },
  { key:'n2o', c:'#3FA95C' },
  { key:'h2',  c:'#B5352C', c2:'#9DA1A4', t:'#7C1A15' },
  { key:'o3',  c:'#8E5AC6' }
];

/* RENDER CONSTANTS (the reference's) */
const MARKS    = [0.25, 0.50, 0.75];
const STEPS    = 18;    // density ramp steps for the gas gauge (18 -> one per px of FADE)
const GAS_MAX  = 0.62;  // gas density at a full tank
const FADE     = 18;    // px over which the gas edge fades in
const LIQ_OP   = [0.72, 0.94];
const LWAVE = [
  { amp: 2.8, k: 0.19, sp:  1.0, off: 3 },
  { amp: 2.0, k: 0.29, sp: -1.4, off: 0 }
];
const GEDGE = [
  { amp: 3, k: 0.11, sp:  0.7 },
  { amp: 2, k: 0.19, sp: -0.5 }
];
const DRIFT_SPEED = 26;
const MOTES_GAS   = 42;
const MOTES_LIQ   = 26;
const MOTE_POOL   = 64;
const PHI = 0.6180339, RT2 = 0.4142136;

const rgbOf = h => [parseInt(h.substr(1,2),16), parseInt(h.substr(3,2),16), parseInt(h.substr(5,2),16)];
const rgba  = (c, a) => 'rgba(' + Math.round(c[0]) + ',' + Math.round(c[1]) + ',' + Math.round(c[2]) + ',' + a.toFixed(3) + ')';
const mix   = (a,b,t) => [a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t, a[2]+(b[2]-a[2])*t];
const tankHex = g => g.t || g.c;
const TANK = [7, 16, 25];

/* Per-gas paints. With real alpha the ramp is one colour at STEPS alpha levels, and the
   mote colour is the reference's mix. */
const GP = {}, LP = {};
GAS.forEach(g => {
  const col = rgbOf(tankHex(g));
  GP[g.key] = { col: col, mote: rgba(mix(mix(TANK, col, 0.86), [255,255,255], 0.12), 1) };
  LP[g.key] = { col: col, mote: rgba(mix(mix(TANK, col, 0.82), [255,255,255], 0.14), 1) };
});

/* MOTE POOL: one shared table, seeded per gauge (deterministic: same on every client) */
let seed = 12345;
const rnd = () => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed / 2147483648; };
const MOTES = [];
for (let i = 0; i < MOTE_POOL; i++) {
  MOTES.push({
    hx: rnd(), hy: rnd(),
    fx1: 0.6 + rnd()*1.1, fx2: 1.5 + rnd()*1.4,
    fy1: 0.5 + rnd()*1.0, fy2: 1.3 + rnd()*1.6,
    px1: rnd()*6.283, px2: rnd()*6.283, py1: rnd()*6.283, py2: rnd()*6.283,
    ax: 2.0 + rnd()*3.0, ay: 2.4 + rnd()*3.8,
    s: rnd() < 0.62 ? 1 : 2
  });
}

function paintMotes(ctx, colour, n, top, bottom, w, drift, seed) {
  const span = bottom - top;
  if (span < 6) return;
  const sx = seed * PHI, sy = seed * RT2, sp = seed * 2.399;
  ctx.fillStyle = colour;
  for (let m = 0; m < n; m++) {
    const p = MOTES[m];
    const hx = (p.hx + sx) % 1, hy = (p.hy + sy) % 1;
    let mx = hx * w + Math.sin(phase*p.fx1 + p.px1 + sp) * p.ax + Math.sin(phase*p.fx2 + p.px2 + sp) * p.ax * 0.45;
    const wob = Math.sin(phase*p.fy1 + p.py1 + sp) * p.ay + Math.sin(phase*p.fy2 + p.py2 + sp) * p.ay * 0.45;
    const base = hy * span + (drift ? -drift * phase * DRIFT_SPEED : 0);
    let my = top + (((base % span) + span) % span) + wob;
    if (my < top) my = top;
    if (my > bottom - p.s) my = bottom - p.s;
    if (mx < 0) mx = 0;
    if (mx > w - p.s) mx = w - p.s;
    ctx.fillRect(mx, my, p.s, p.s);
  }
}

/* BUILD */
const FIELDS = [
  ['gasMol','mol',1], ['liqL','L',1],
  ['gasKpa','kPa',0], ['liqKpa','kPa',0],
  ['gasT','°C',1], ['liqT','°C',1]
];

/* VIEW: the whole surface the tank logic needs */
const STATE = {};
const View = {
  update: function(key, data) {
    STATE[key] = Object.assign(STATE[key] || {}, data);
    const s = STATE[key];
    FIELDS.forEach(function(fd) {
      const f = fd[0], unit = fd[1], dp = fd[2];
      if (data[f] === undefined) return;
      const el = document.getElementById(key + '-' + f);
      if (!el) return;
      el.className = 'val' + (data[f] === 0 ? ' dim' : '');
      el.innerHTML = data[f].toFixed(dp) + '<small>' + unit + '</small>';
    });
    const flow = function(id, v, cls, glyph, label) {
      const el = document.getElementById(key + '-' + id);
      if (!el || v === undefined) return;
      el.innerHTML = '<u>' + label + '</u> <span class="' + (v > 0.001 ? cls : 'fzero') +
        '"><i>' + glyph + '</i> ' + v.toFixed(2) + '</span> <u>mol/s</u>';
    };
    flow('in',  s.molIn,  'fin',  '▼', 'In');
    flow('out', s.molOut, 'fout', '▲', 'Out');
  }
};
addEventListener('data', function(e) { for (const k in e.detail) View.update(k, e.detail[k]); });

/* GAUGE RENDER */
let phase = 0;
const canvases = document.querySelectorAll('canvas');

function markRows(w, h) {
  return MARKS.map(function(m) {
    const full = m === 0.5;
    const x0 = full ? 0 : Math.round(w * 0.28);
    return { y: Math.round(h - h * m), x0: x0, x1: full ? w : x0 + Math.round(w * 0.44) };
  });
}

/* Trace a wave surface y(x) across the width, then close along the bottom. */
function surfacePath(ctx, w, h, yAt) {
  ctx.beginPath();
  ctx.moveTo(0, yAt(0));
  for (let x = 1; x <= w; x++) ctx.lineTo(x, yAt(x));
  ctx.lineTo(w, h); ctx.lineTo(0, h); ctx.closePath();
}

function drawGauge(cv, seed) {
  const key = cv.dataset.key, isGas = cv.dataset.phase === 'gas';
  const st = STATE[key] || {};
  const frac = Math.max(0, Math.min(1, (isGas ? st.gasFill : st.liqFill) || 0));
  const drift = st.drift || 0;
  const w = cv.clientWidth, h = cv.clientHeight;
  if (!w || !h) return;
  if (cv.width !== w || cv.height !== h) { cv.width = w; cv.height = h; }
  const ctx = cv.getContext('2d');
  ctx.clearRect(0, 0, w, h);

  // graduation marks
  ctx.fillStyle = 'rgba(190,214,228,0.15)';
  markRows(w, h).forEach(function(r) { ctx.fillRect(r.x0, r.y, r.x1 - r.x0, 1); });

  const lvl = h - h * frac;

  if (!isGas) {
    const P = LP[key];
    if (frac > 0.001) {
      const c0 = function(x) { return lvl + LWAVE[0].off + Math.sin(x*LWAVE[0].k + phase*LWAVE[0].sp) * LWAVE[0].amp; };
      const c1 = function(x) { return lvl + LWAVE[1].off + Math.sin(x*LWAVE[1].k + phase*LWAVE[1].sp) * LWAVE[1].amp; };
      // "only": under the higher of the two surfaces; "both": under both
      surfacePath(ctx, w, h, function(x) { return Math.min(c0(x), c1(x)); });
      ctx.fillStyle = rgba(P.col, LIQ_OP[0]); ctx.fill();
      surfacePath(ctx, w, h, function(x) { return Math.max(c0(x), c1(x)); });
      ctx.fillStyle = rgba(P.col, 1 - (1 - LIQ_OP[1]) / (1 - LIQ_OP[0])); ctx.fill();
    }
    if (drift !== 0) paintMotes(ctx, P.mote, MOTES_LIQ, 2, Math.max(2, lvl - 6), w, drift, seed);
    return;
  }

  const R = GP[key];
  if (frac > 0.001) {
    const edge = function(x) {
      return lvl + Math.sin(x*GEDGE[0].k + phase*GEDGE[0].sp) * GEDGE[0].amp
                 + Math.sin(x*GEDGE[1].k + phase*GEDGE[1].sp) * GEDGE[1].amp;
    };
    // density ramp: STEPS layers, each from (edge + FADE*i/STEPS) to the floor, at an
    // equal alpha so that STEPS layers composite to GAS_MAX
    const a = 1 - Math.pow(1 - GAS_MAX, 1 / STEPS);
    ctx.fillStyle = rgba(R.col, a);
    for (let i = 0; i < STEPS; i++) {
      const d = FADE * i / STEPS;
      surfacePath(ctx, w, h, function(x) { return edge(x) + d; });
      ctx.fill();
    }
  }
  paintMotes(ctx, R.mote, MOTES_GAS, 2, h - 2, w, drift, seed);
}

function frame(now) {
  phase = now / 1000;
  canvases.forEach(function(cv, i) { drawGauge(cv, i); });
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);

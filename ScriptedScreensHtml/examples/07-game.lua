-- 07-game.lua -- an endless runner in the spirit of the browser's offline dinosaur game, with a Stationeer.
-- Paste into a Lua chip in a ScriptedScreens console; a 3x3 console plays best.
-- Tap JUMP (or the field) to jump over canisters and crates, hold DUCK to crouch under drones.
-- The whole game is the page below: HTML, CSS and a script. The chip only puts it on the screen.

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
<meta name="viewport" content="width=800">
<title>Stationeer Run</title>
<style>
  :root {
    --sky-top: #0b1020;
    --sky-bottom: #1d1630;
    --ground: #b8583a;
    --ground-dark: #7c3624;
    --ink: #f3e9dc;
    --dim: #a89c8f;
    --suit: #f0a020;
    --suit-dark: #b86e10;
    --visor: #4fd1e8;
    --steel: #c9cfd6;
    --steel-dark: #6f7a86;
    --alarm: #ff4d3d;
  }
  * { box-sizing: border-box; }
  html, body { margin: 0; background: #05070d; }
  body { font-family: Barlow, "Segoe UI", Arial, sans-serif; color: var(--ink); }

  #app { position: relative; width: 800px; height: 800px; overflow: hidden; margin: 0 auto;
    background: linear-gradient(180deg, var(--sky-top), var(--sky-bottom)); user-select: none; }

  header { position: absolute; left: 0; top: 0; width: 800px; height: 90px; padding: 22px 32px;
    display: flex; align-items: center; justify-content: space-between; }
  .title { font-family: "Barlow Condensed", Barlow, sans-serif; font-weight: 700; font-size: 34px; letter-spacing: 0.08em; }
  .title span { color: var(--suit); }
  .scores { display: flex; gap: 28px; font-family: "Barlow Condensed", Barlow, sans-serif; font-size: 30px;
    font-variant-numeric: tabular-nums; }
  .scores .lbl { color: var(--dim); font-size: 18px; letter-spacing: 0.12em; margin-right: 8px; }
  #score.flash { color: var(--suit); }

  /* the playfield: everything that moves lives here */
  #field { position: absolute; left: 0; top: 90px; width: 800px; height: 520px; overflow: hidden; cursor: pointer; }
  .star { position: absolute; width: 3px; height: 3px; border-radius: 50%; background: #ffffff; }
  #planet { position: absolute; left: 560px; top: 30px; width: 120px; height: 120px; border-radius: 50%;
    background: linear-gradient(135deg, #7fb2d9, #2d4f7a); box-shadow: 0 0 30px rgba(127, 178, 217, 0.35); }
  #planetRing { position: absolute; left: 530px; top: 84px; width: 180px; height: 14px; border-radius: 50%;
    border: 3px solid rgba(220, 200, 160, 0.55); transform: rotate(-12deg); }

  .ridge { position: absolute; top: 0; left: 0; }
  .ridge div { position: absolute; bottom: 0; border-radius: 60px 60px 0 0; }
  #ridgeFar div { background: #2a1d33; }
  #ridgeNear div { background: #3b2233; }

  .dome { position: absolute; bottom: 0; }
  .dome .cap { position: absolute; left: 0; bottom: 18px; width: 90px; height: 45px; border-radius: 45px 45px 0 0;
    background: rgba(79, 209, 232, 0.25); border: 2px solid rgba(79, 209, 232, 0.6); border-bottom: none; }
  .dome .base { position: absolute; left: -10px; bottom: 0; width: 110px; height: 18px; background: #4a3140; }
  .dome .lamp { position: absolute; left: 42px; bottom: 60px; width: 6px; height: 6px; border-radius: 50%; background: var(--alarm); }

  #groundLine { position: absolute; left: 0; top: 440px; width: 800px; height: 80px;
    background: linear-gradient(180deg, var(--ground), var(--ground-dark)); border-top: 3px solid #e07a52; }
  .pebble { position: absolute; top: 452px; height: 4px; border-radius: 2px; background: #8e422c; }

  /* the Stationeer */
  #player { position: absolute; left: 0; top: 0; width: 60px; height: 80px; }
  #player div { position: absolute; }
  .pack { left: 2px; top: 22px; width: 16px; height: 34px; border-radius: 4px; background: var(--steel-dark); }
  .tank { left: 5px; top: 18px; width: 10px; height: 8px; border-radius: 3px 3px 0 0; background: var(--steel); }
  .body { left: 14px; top: 26px; width: 30px; height: 32px; border-radius: 8px 8px 4px 4px; background: var(--suit); }
  .belt { left: 14px; top: 48px; width: 30px; height: 5px; background: var(--suit-dark); }
  .helmet { left: 12px; top: 0; width: 36px; height: 32px; border-radius: 16px 16px 12px 12px; background: var(--steel); }
  .visor { left: 26px; top: 8px; width: 20px; height: 14px; border-radius: 4px 10px 10px 4px; background: var(--visor); }
  .glint { left: 36px; top: 10px; width: 6px; height: 3px; border-radius: 2px; background: #d9f7ff; }
  .arm { left: 30px; top: 32px; width: 16px; height: 8px; border-radius: 4px; background: var(--suit-dark); }
  .legA, .legB { top: 56px; width: 10px; height: 24px; border-radius: 3px; background: var(--suit-dark); }
  .legA { left: 17px; }
  .legB { left: 31px; }
  .boot { width: 14px; height: 6px; border-radius: 3px; background: #3a3f47; }

  /* ducking: the same parts, lower and longer */
  #player.duck .helmet { left: 34px; top: 30px; }
  #player.duck .visor { left: 48px; top: 38px; }
  #player.duck .glint { left: 58px; top: 40px; }
  #player.duck .body { left: 6px; top: 44px; width: 44px; height: 20px; }
  #player.duck .belt { left: 6px; top: 58px; width: 44px; }
  #player.duck .pack { left: 0; top: 38px; width: 30px; height: 12px; }
  #player.duck .tank { left: 4px; top: 34px; }
  #player.duck .arm { left: 44px; top: 52px; }
  #player.hurt .visor { background: var(--alarm); }

  /* obstacles: every one has the same parts, the class picks the look */
  .ob { position: absolute; left: 0; top: 0; }
  .ob div { position: absolute; }
  .ob .a, .ob .b, .ob .c { left: 0; top: 0; width: 0; height: 0; }
  .ob.canister .a { left: 0; top: 8px; width: 26px; height: 52px; border-radius: 6px; background: linear-gradient(90deg, #d8dde3, #8a949f); }
  .ob.canister .b { left: 7px; top: 0; width: 12px; height: 10px; border-radius: 3px 3px 0 0; background: #5d6670; }
  .ob.canister .c { left: 0; top: 24px; width: 26px; height: 8px; background: #3da35a; }
  .ob.twin .a { left: 0; top: 8px; width: 24px; height: 44px; border-radius: 6px; background: linear-gradient(90deg, #f1d36b, #b08a1c); }
  .ob.twin .b { left: 26px; top: 0; width: 24px; height: 52px; border-radius: 6px; background: linear-gradient(90deg, #e36b5b, #9c3326); }
  .ob.twin .c { left: 6px; top: 20px; width: 12px; height: 6px; background: #3a3f47; }
  .ob.crate .a { left: 0; top: 0; width: 44px; height: 40px; border-radius: 3px; background: #7a8591; border: 3px solid #4b545e; }
  .ob.crate .b { left: 6px; top: 16px; width: 32px; height: 6px; background: #f0a020; }
  .ob.crate .c { left: 18px; top: 4px; width: 8px; height: 8px; border-radius: 50%; background: #d9dee4; }
  .ob.drone .a { left: 6px; top: 8px; width: 40px; height: 16px; border-radius: 8px; background: #59626d; }
  .ob.drone .b { left: 0; top: 0; width: 52px; height: 4px; border-radius: 2px; background: #c9cfd6; }
  .ob.drone .c { left: 22px; top: 12px; width: 8px; height: 8px; border-radius: 50%; background: var(--alarm); }

  /* bottom controls */
  #controls { position: absolute; left: 0; top: 610px; width: 800px; height: 190px; padding: 24px 32px;
    display: flex; gap: 24px; }
  .pad { flex: 1; border-radius: 22px; display: flex; flex-direction: column; align-items: center; justify-content: center;
    font-family: "Barlow Condensed", Barlow, sans-serif; font-weight: 700; font-size: 40px; letter-spacing: 0.1em; cursor: pointer; }
  .pad small { font-family: Barlow, sans-serif; font-weight: 400; font-size: 16px; letter-spacing: 0.04em; color: var(--dim); margin-top: 4px; }
  #jumpBtn { background: #2b3a55; border: 3px solid #4d6a99; }
  #jumpBtn:active { background: #3d5480; }
  #duckBtn { background: #3a2a3f; border: 3px solid #6d4a73; }
  #duckBtn:active, #duckBtn.held { background: #563e5d; }

  /* messages over the field */
  #overlay { position: absolute; left: 0; top: 90px; width: 800px; height: 440px; display: flex; flex-direction: column;
    align-items: center; justify-content: center; opacity: 1; pointer-events: none; }
  #overlay .big { font-family: "Barlow Condensed", Barlow, sans-serif; font-weight: 700; font-size: 64px; letter-spacing: 0.08em; }
  #overlay .small { font-size: 22px; color: var(--dim); margin-top: 6px; }
  #overlay.hidden { opacity: 0; }
  #overlay.over .big { color: var(--alarm); }
</style>
</head>
<body>
<div id="app">
  <header>
    <div class="title">STATIONEER <span>RUN</span></div>
    <div class="scores">
      <div><span class="lbl">HI</span><span id="hi">00000</span></div>
      <div><span class="lbl">SCORE</span><span id="score">00000</span></div>
    </div>
  </header>

  <div id="field">
    <div id="stars"></div>
    <div id="planet"></div>
    <div id="planetRing"></div>
    <div id="ridgeFar" class="ridge"></div>
    <div id="ridgeNear" class="ridge"></div>
    <div id="domes"></div>
    <div id="groundLine"></div>
    <div id="pebbles"></div>
    <div id="obstacles"></div>
    <div id="player">
      <div class="pack"></div><div class="tank"></div>
      <div class="legA" id="legA"><div class="boot" id="bootA" style="left:-2px;top:20px"></div></div>
      <div class="legB" id="legB"><div class="boot" id="bootB" style="left:-2px;top:20px"></div></div>
      <div class="body"></div><div class="belt"></div><div class="arm"></div>
      <div class="helmet"></div><div class="visor"></div><div class="glint"></div>
    </div>
  </div>

  <div id="overlay">
    <div class="big" id="msgBig">STATIONEER RUN</div>
    <div class="small" id="msgSmall">tap JUMP to start</div>
  </div>

  <div id="controls">
    <div class="pad" id="jumpBtn">JUMP<small>or tap the field</small></div>
    <div class="pad" id="duckBtn">DUCK<small>hold to crouch</small></div>
  </div>
</div>

<script>
// Stationeer Run: an endless runner. Everything that moves is a fixed set of elements moved with
// transforms, so a frame only changes numbers. Physics is in seconds, so it plays the same at any
// frame rate.
const GROUND = 440;          // top of the ground in field pixels
const PLAYER_X = 90;
const GRAVITY = 2600;
const JUMP_V = 900;
const START_SPEED = 380;
const MAX_SPEED = 950;
const POOL = 4;

const $ = (id) => document.getElementById(id);
const field = $('field'), player = $('player');
const scoreEl = $('score'), hiEl = $('hi'), overlay = $('overlay');

// scenery, built once
(function scenery() {
  let s = '';
  for (let i = 0; i < 40; i++) {
    const x = (i * 197) % 800, y = (i * 83) % 300, o = 0.3 + ((i * 37) % 7) / 10;
    s += '<div class="star" style="left:' + x + 'px;top:' + y + 'px;opacity:' + o.toFixed(2) + '"></div>';
  }
  $('stars').innerHTML = s;
  const ridge = (id, n, h, w, seed) => {
    let r = '';
    for (let i = 0; i < n; i++) {
      const hh = h + ((i * seed) % 5) * 14, ww = w + ((i * seed) % 3) * 40;
      r += '<div style="left:' + (i * 1600 / n) + 'px;top:' + (GROUND - hh) + 'px;width:' + ww + 'px;height:' + hh + 'px"></div>';
    }
    $(id).innerHTML = r;
  };
  ridge('ridgeFar', 8, 70, 220, 3);
  ridge('ridgeNear', 6, 40, 260, 7);
  let d = '';
  for (let i = 0; i < 2; i++)
    d += '<div class="dome" style="left:' + (300 + i * 800) + 'px;top:' + GROUND + 'px"><div class="cap"></div><div class="base"></div><div class="lamp"></div></div>';
  $('domes').innerHTML = d;
  let p = '';
  for (let i = 0; i < 14; i++)
    p += '<div class="pebble" id="pb' + i + '" style="width:' + (8 + (i * 7) % 20) + 'px;top:' + (452 + (i * 13) % 50) + 'px"></div>';
  $('pebbles').innerHTML = p;
  let o = '';
  for (let i = 0; i < POOL; i++)
    o += '<div class="ob canister" id="ob' + i + '"><div class="a"></div><div class="b"></div><div class="c"></div></div>';
  $('obstacles').innerHTML = o;
})();

const obs = [];
for (let i = 0; i < POOL; i++) obs.push({ el: $('ob' + i), active: false, x: -200, y: 0, w: 0, h: 0, kind: 'canister' });
const KINDS = {
  canister: { w: 26, h: 60, fly: false },
  twin:     { w: 50, h: 52, fly: false },
  crate:    { w: 44, h: 40, fly: false },
  drone:    { w: 52, h: 24, fly: true },
};

let hi = 0;
try { hi = Number(localStorage.getItem('stationeerRunHi')) || 0; } catch (e) { hi = 0; }

const g = {
  state: 'running', // running | over
  demo: true,       // the attract mode: the Stationeer runs by itself until someone taps
  demoRestartAt: 0,
  y: 0, vy: 0,      // height above the ground (up is positive)
  duckHeld: false, duckUntil: 0,
  speed: START_SPEED,
  dist: 0, score: 0, nextSpawn: 600,
  far: 0, near: 0, domes: 0, ground: 0,
  step: 0, clock: 0, flashUntil: 0, lastBeep: 0,
};

const pad = (n) => String(Math.floor(n)).padStart(5, '0');
hiEl.textContent = pad(hi);

function setMsg(big, small, mode) {
  $('msgBig').textContent = big;
  $('msgSmall').textContent = small;
  overlay.className = mode || '';
}

function reset(demo) {
  g.state = 'running';
  g.demo = demo;
  g.y = 0; g.vy = 0; g.speed = START_SPEED;
  g.dist = 0; g.score = 0; g.nextSpawn = 500; g.lastBeep = 0;
  obs.forEach((o) => { o.active = false; o.x = -200; });
  player.className = '';
  if (demo) setMsg('STATIONEER RUN', 'tap JUMP to play', '');
  else setMsg('', '', 'hidden');
}

function jump() {
  if (g.demo || g.state !== 'running') { reset(false); return; }
  if (g.y <= 0.5 && !ducking()) g.vy = JUMP_V;
}
function ducking() { return g.state === 'running' && (g.duckHeld || g.clock < g.duckUntil); }
function duck(on) {
  g.duckHeld = on;
  // a press ducks for at least a moment, so a quick tap still clears a drone
  if (on) g.duckUntil = g.clock + 0.45;
  $('duckBtn').className = on ? 'pad held' : 'pad';
  if (on && g.y > 0) g.vy = Math.min(g.vy, -900); // fast fall, as in the original
}

field.addEventListener('click', jump);
$('jumpBtn').addEventListener('click', jump);
$('duckBtn').addEventListener('mousedown', () => duck(true));
$('duckBtn').addEventListener('mouseup', () => duck(false));
$('duckBtn').addEventListener('mouseleave', () => duck(false));
$('duckBtn').addEventListener('click', () => { if (g.demo || g.state !== 'running') reset(false); });

function spawn() {
  const slot = obs.find((o) => !o.active);
  if (!slot) return;
  const r = Math.random();
  let kind = r < 0.35 ? 'canister' : r < 0.6 ? 'crate' : r < 0.8 ? 'twin' : 'drone';
  if (kind === 'drone' && g.score < 150) kind = 'crate'; // drones come once the run is under way
  const k = KINDS[kind];
  slot.kind = kind;
  slot.active = true;
  slot.w = k.w; slot.h = k.h;
  slot.x = 820;
  // drones fly at head height (duck) or low (jump); ground things sit on the ground
  slot.y = k.fly ? (Math.random() < 0.5 ? GROUND - 70 : GROUND - 34) : GROUND - k.h;
  slot.el.className = 'ob ' + kind;
  slot.miss = g.demo && Math.random() < 0.03; // the autopilot fumbles now and then
}

// the attract mode's pilot: jump a little before a ground thing or a low drone, crouch under a high one
function autopilot() {
  const front = PLAYER_X + 40;
  for (const o of obs) {
    if (!o.active || o.miss || o.x + o.w < PLAYER_X) continue;
    const high = o.kind === 'drone' && o.y < GROUND - 50;
    if (high) {
      if (o.x - front < g.speed * 0.3) g.duckUntil = g.clock + 0.1;
    } else if (o.x - front < g.speed * 0.16 && g.y <= 0.5 && !ducking()) {
      g.vy = JUMP_V;
    }
  }
}

function hit(o) {
  const duckNow = ducking();
  const pw = duckNow ? 56 : 34, ph = duckNow ? 36 : 76;
  const px = PLAYER_X + (duckNow ? 2 : 12);
  const py = GROUND - g.y - ph;
  const m = 6; // forgiving edges, as the original is
  return px + m < o.x + o.w && px + pw - m > o.x && py + m < o.y + o.h && py + ph - m > o.y;
}

function over() {
  g.state = 'over';
  player.className = (ducking() ? 'duck ' : '') + 'hurt';
  if (g.demo) {
    g.demoRestartAt = g.clock + 2.5;
    setMsg('SUIT BREACH', 'tap JUMP to play', 'over');
    return;
  }
  if (g.score > hi) {
    hi = Math.floor(g.score);
    hiEl.textContent = pad(hi);
    try { localStorage.setItem('stationeerRunHi', String(hi)); } catch (e) { /* storage is optional */ }
  }
  setMsg('SUIT BREACH', 'score ' + pad(g.score) + ' · tap JUMP to run again', 'over');
}

function update(dt) {
  g.clock += dt;
  if (g.state === 'over' && g.demo && g.clock > g.demoRestartAt) reset(true);
  if (g.state !== 'running') return;
  if (g.demo) autopilot();
  g.speed = Math.min(MAX_SPEED, g.speed + 9 * dt);
  const dx = g.speed * dt;
  g.dist += dx;
  g.score = g.dist / 40;

  // jump physics
  if (g.y > 0 || g.vy > 0) {
    g.vy -= GRAVITY * dt;
    g.y += g.vy * dt;
    if (g.y <= 0) { g.y = 0; g.vy = 0; }
  }

  // parallax and ground
  g.far = (g.far + dx * 0.15) % 800;
  g.near = (g.near + dx * 0.35) % 800;
  g.domes = (g.domes + dx * 0.5) % 800;
  g.ground = (g.ground + dx) % 800;

  // obstacles
  g.nextSpawn -= dx;
  if (g.nextSpawn <= 0) {
    spawn();
    const gap = 260 + g.speed * 0.55 + Math.random() * 380;
    g.nextSpawn = gap;
  }
  for (const o of obs) {
    if (!o.active) continue;
    o.x -= dx * (o.kind === 'drone' ? 1.08 : 1);
    if (o.x < -80) { o.active = false; o.x = -200; continue; }
    if (hit(o)) { over(); return; }
  }

  // every 100 points the score blinks, as in the original
  const hundreds = Math.floor(g.score / 100);
  if (hundreds > g.lastBeep) { g.lastBeep = hundreds; g.flashUntil = g.clock + 0.8; }
  g.step += dt * (g.speed / 55);
}

function draw() {
  const duckNow = ducking();
  const cls = (duckNow ? 'duck' : '') + (g.state === 'over' ? ' hurt' : '');
  if (player.className !== cls.trim()) player.className = cls.trim();
  player.style.transform = 'translate(' + PLAYER_X + 'px,' + (GROUND - 80 - g.y).toFixed(1) + 'px)';

  // legs: a run cycle on the ground, tucked in the air
  const air = g.y > 0;
  const phase = Math.floor(g.step) % 2;
  const la = air ? 18 : (g.state === 'running' ? (phase ? 24 : 16) : 24);
  const lb = air ? 18 : (g.state === 'running' ? (phase ? 16 : 24) : 24);
  const legTop = duckNow ? 62 : 56;
  $('legA').style.height = la + 'px';
  $('legB').style.height = lb + 'px';
  $('legA').style.top = (legTop + (duckNow ? 0 : 24 - la)) + 'px';
  $('legB').style.top = (legTop + (duckNow ? 0 : 24 - lb)) + 'px';
  $('bootA').style.top = (la - 4) + 'px';
  $('bootB').style.top = (lb - 4) + 'px';

  $('ridgeFar').style.transform = 'translateX(' + (-g.far).toFixed(1) + 'px)';
  $('ridgeNear').style.transform = 'translateX(' + (-g.near).toFixed(1) + 'px)';
  $('domes').style.transform = 'translateX(' + (-g.domes).toFixed(1) + 'px)';
  for (let i = 0; i < 14; i++) {
    const x = ((i * 61 + 800) - g.ground % 800 + 800) % 860 - 30;
    $('pb' + i).style.transform = 'translateX(' + x.toFixed(1) + 'px)';
  }
  for (const o of obs) {
    const bob = o.kind === 'drone' ? Math.sin(g.clock * 8 + o.x * 0.01) * 3 : 0;
    o.el.style.transform = 'translate(' + o.x.toFixed(1) + 'px,' + (o.y + bob).toFixed(1) + 'px)';
  }

  scoreEl.textContent = pad(g.score);
  const flash = g.clock < g.flashUntil && Math.floor(g.clock * 6) % 2 === 0;
  scoreEl.className = flash ? 'flash' : '';
}

let last = 0;
function frame(t) {
  const now = t / 1000;
  const dt = last ? Math.min(0.05, now - last) : 0;
  last = now;
  update(dt);
  draw();
  requestAnimationFrame(frame);
}
reset(true);
draw();
requestAnimationFrame(frame);
</script>
</body>
</html>
]==]

ui:element({
    id = "run",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#05070D" },
})
ui:commit()

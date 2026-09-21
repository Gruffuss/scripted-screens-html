-- 07-game-noscript.lua -- 07-game with its script removed, nothing else changed.
-- Control half for the interpreter A/B on a requestAnimationFrame page: unlike AtmoDark this one
-- already updates incrementally (~30 guarded style writes per frame, no innerHTML in draw()), so
-- it brackets the top of the range where AtmoDark gave the bottom.

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
    --ground-col: #b8583a;
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

  #app { position: relative; width: 800px; height: 100vh; overflow: hidden; margin: 0 auto;
    display: flex; flex-direction: column;
    background: linear-gradient(180deg, var(--sky-top), var(--sky-bottom)); user-select: none; }

  header { flex: none; width: 800px; padding: 22px 32px;
    display: flex; align-items: center; justify-content: space-between; }
  .title { font-family: "Barlow Condensed", Barlow, sans-serif; font-weight: 700; font-size: 34px; letter-spacing: 0.08em; }
  .title span { color: var(--suit); }
  .scores { display: flex; gap: 28px; font-family: "Barlow Condensed", Barlow, sans-serif; font-size: 30px;
    font-variant-numeric: tabular-nums; }
  .scores .lbl { color: var(--dim); font-size: 18px; letter-spacing: 0.12em; margin-right: 8px; }
  #score.flash { color: var(--suit); }

  /* the playfield: everything that moves lives here */
  /* the playfield takes the height the header and controls leave */
  #field { flex: none; position: relative; width: 800px; overflow: hidden; cursor: pointer; }
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
    background: linear-gradient(180deg, var(--ground-col), var(--ground-dark)); border-top: 3px solid #e07a52; }
  .pebble { position: absolute; height: 4px; border-radius: 2px; background: #8e422c; }

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
  #controls { flex: none; width: 800px; height: 190px; padding: 24px 32px; display: flex; gap: 24px; }
  .pad { flex: 1; border-radius: 22px; display: flex; flex-direction: column; align-items: center; justify-content: center;
    font-family: "Barlow Condensed", Barlow, sans-serif; font-weight: 700; font-size: 40px; letter-spacing: 0.1em; cursor: pointer; }
  .pad small { font-family: Barlow, sans-serif; font-weight: 400; font-size: 16px; letter-spacing: 0.04em; color: var(--dim); margin-top: 4px; }
  #jumpBtn { background: #2b3a55; border: 3px solid #4d6a99; }
  #jumpBtn:active { background: #3d5480; }
  #duckBtn { background: #3a2a3f; border: 3px solid #6d4a73; }
  #duckBtn:active, #duckBtn.held { background: #563e5d; }

  /* messages over the field */
  #overlay { position: absolute; left: 0; top: 0; right: 0; bottom: 80px; display: flex; flex-direction: column;
    align-items: center; justify-content: center; opacity: 1; pointer-events: none; }
  #overlay .big { font-family: "Barlow Condensed", Barlow, sans-serif; font-weight: 700; font-size: 64px; letter-spacing: 0.08em; }
  #overlay .small { font-size: 22px; color: var(--dim); margin-top: 6px; }
  #overlay.hidden { opacity: 0; }
  #overlay.over .big { color: var(--alarm); }

  /* a wide console (2x1) has half the height: a slimmer header, shorter pads, no hints */
  :root[data-short=""] header { padding: 10px 24px; }
  :root[data-short=""] .title { font-size: 24px; }
  :root[data-short=""] .scores { font-size: 22px; gap: 18px; }
  :root[data-short=""] .scores .lbl { font-size: 14px; margin-right: 5px; }
  :root[data-short=""] #controls { height: 72px; padding: 8px 24px; gap: 16px; }
  :root[data-short=""] .pad { font-size: 26px; border-radius: 14px; }
  :root[data-short=""] .pad small { display: none; }
  :root[data-short=""] #overlay .big { font-size: 40px; }
  :root[data-short=""] #overlay .small { font-size: 17px; }
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

<!-- script removed: control half of the interpreter A/B -->
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

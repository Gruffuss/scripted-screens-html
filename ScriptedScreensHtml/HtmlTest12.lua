-- HtmlTest12.lua -- Batch F4: the rescued items. Push to the 3x3 console (586); design width 640.
-- Each row names what it checks; rows that need a vector addition say which.
--
--   first      ::first-letter (big amber drop letter); ::first-line (blue first line of a wrapped paragraph, vector req 15)
--   scrollbar  a drawn track and thumb over the scroll box (scroll it: the thumb follows); ::-webkit-scrollbar-thumb colour
--   counter    @counter-style cyclic and alphabetic as list markers
--   corners    corner-shape bevel / scoop / notch on rounded boxes
--   border-img border-image with a gradient (gradient stroke); with an image, nine slices (vector req 16)
--   ruby       <ruby>base<rt>reading</rt></ruby> inline
--   scroll-anim animation-timeline: scroll() fades and slides a bar as the box scrolls (no clock)
--   flip       rotateY(160deg) with backface-visibility: hidden hides the card; rotateY(60deg) narrows it
--   grad-text  background-clip: text with a gradient (vector req 14)

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
  @counter-style stars { system: cyclic; symbols: "*" "+"; suffix: " "; }
  @counter-style abc { system: alphabetic; symbols: a b c; suffix: ") "; }
  @keyframes slidein { from { opacity: 0.2; transform: translateX(-60px); } to { opacity: 1; transform: translateX(0); } }
  :root { --ink: #E4F1F7; --dim: #7A93A6; --accent: #38BDF8; }
  body { background: #0B1622; color: var(--ink); font-family: Barlow; padding: 10px; font-size: 12px; }
  h2 { font-size: 11px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; margin: 6px 0 2px 0; }
  .row { display: flex; gap: 10px; align-items: flex-start; flex-wrap: wrap; }
  .lbl { font-size: 10px; color: var(--dim); width: 70px; }
  .sw { width: 56px; height: 36px; background: #24314A; }

  .drop { width: 220px; }
  .drop::first-letter { font-size: 28px; color: #E2A94E; font-weight: bold; }
  .fline { width: 200px; }
  .fline::first-line { color: var(--accent); font-weight: bold; }

  .box { width: 150px; height: 70px; overflow: auto; background: #172033; scrollbar-color: #E2A94E #24314A; }
  .box2 { width: 150px; height: 70px; overflow: auto; background: #172033; scrollbar-width: thin; }
  .box2::-webkit-scrollbar-thumb { background: #B5352C; border-radius: 3px; }
  .tall { height: 220px; background: linear-gradient(#2E8B6E, #38BDF8); }

  ul.stars { list-style-type: stars; margin: 0; padding-left: 18px; }
  ol.abc { list-style-type: abc; margin: 0; padding-left: 18px; }

  .bevel { corner-shape: bevel; border-radius: 12px; } .scoop { corner-shape: scoop; border-radius: 12px; } .notch { corner-shape: notch; border-radius: 10px; }
  .bstroke { corner-shape: bevel; border-radius: 12px; background: none; border: 3px solid #E2A94E; }

  .bimg { width: 90px; height: 50px; border: 8px solid transparent; border-image: linear-gradient(90deg, #B5352C, #38BDF8) 1; }
  .bimg2 { width: 90px; height: 50px; border: 12px solid transparent; border-image: url(https://raw.githubusercontent.com/Gruffuss/scripted-screens-vector/main/ScriptedScreensVector/About/thumb.png) 33% / 12px; }

  .scroller { width: 200px; height: 80px; overflow: auto; background: #172033; }
  .scroller .filler { height: 300px; }
  .bar { position: sticky; top: 6px; margin: 6px; height: 16px; background: #2E8B6E; animation: slidein 1s linear; animation-timeline: scroll(); }

  .card { width: 60px; height: 40px; background: #38BDF8; backface-visibility: hidden; }
  .turned { transform: rotateY(160deg); } .half { transform: rotateY(60deg); }

  .gtext { font-size: 26px; font-weight: bold; background: linear-gradient(90deg, #E2A94E, #38BDF8); -webkit-background-clip: text; background-clip: text; color: transparent; }
</style>
</head>
<body>
  <h2>first-letter, first-line</h2>
  <div class="row">
    <p class="drop">Once upon a time a page was written the way a browser reads it.</p>
    <p class="fline">The first line of this paragraph is blue and bold, the lines after it are plain, however far they wrap.</p>
  </div>

  <h2>scrollbars</h2>
  <div class="row">
    <div class="box"><div class="tall"></div></div>
    <div class="box2"><div class="tall"></div></div>
    <div class="lbl">amber thumb on grey; thin red thumb</div>
  </div>

  <h2>counter styles</h2>
  <div class="row"><ul class="stars"><li>star</li><li>plus</li><li>star again</li></ul><ol class="abc"><li>a</li><li>b</li><li>c</li><li>aa</li></ol></div>

  <h2>corner-shape</h2>
  <div class="row"><div class="sw bevel"></div><div class="sw scoop"></div><div class="sw notch"></div><div class="sw bstroke"></div><div class="lbl">bevel, scoop, notch, bevel stroke</div></div>

  <h2>border-image</h2>
  <div class="row"><div class="bimg"></div><div class="bimg2"></div><div class="lbl">gradient frame; picture frame</div></div>

  <h2>ruby</h2>
  <div class="row"><p><ruby>漢<rp>(</rp><rt>kan</rt><rp>)</rp></ruby><ruby>字<rt>ji</rt></ruby> with readings, and <ruby>base<rt>note</rt></ruby> in a sentence</p></div>

  <h2>scroll-driven animation</h2>
  <div class="row"><div class="scroller"><div class="bar"></div><div class="filler"></div></div><div class="lbl">scroll: the bar fades in and slides right</div></div>

  <h2>backface</h2>
  <div class="row"><div class="card"></div><div class="card half"></div><div class="card turned"></div><div class="lbl">full, narrow, gone</div></div>

  <h2>gradient text</h2>
  <div class="row"><div class="gtext">AMBER TO BLUE</div></div>
</body>
</html>
]]

ui:element({
    id = "web",
    type = "html",
    rect = { unit = "px", x = 0, y = 0, w = W, h = H },
    props = { src = page },
    style = { bg = "#FF00FF" },
})

ui:commit()

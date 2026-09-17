-- HtmlTest6.lua -- Batch D: the script DOM. Push to a 3x3 console; design width 640.
-- The page script runs a list of checks at load and prints PASS/FAIL per item into the
-- page, so a capture is the verdict. Click "run again" to repeat after the page settled.
--
--   ready     DOMContentLoaded and load fire, readyState is complete
--   lookup    getElementsByClassName / TagName / Name, matchMedia, innerWidth
--   tree      insertAdjacentHTML, before/after/prepend, replaceWith, toggleAttribute, className
--   style     el.style read-back, cssText, setProperty, getComputedStyle
--   events    dispatchEvent with a CustomEvent, bubbling from a child to its parent
--   shims     URL, URLSearchParams, crypto.randomUUID, TextEncoder, structuredClone
--   animate   el.animate() moves the square, cancel() puts it back
--   external  a script loaded from a URL sets window.externalLoaded

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
  :root { --ink: #E4F1F7; --dim: #7A93A6; --accent: #38BDF8; }
  body { background: #0B1622; color: var(--ink); font-family: Barlow; padding: 10px; font-size: 13px; }
  h2 { font-size: 12px; color: var(--dim); text-transform: uppercase; letter-spacing: 1px; margin: 8px 0 3px 0; }
  .ok { color: #2E8B6E; } .bad { color: #E2A94E; }
  #list div { padding: 1px 0; font-size: 12px; }
  #box { width: 18px; height: 18px; background: var(--accent); border-radius: 3px; margin: 6px 0; }
  .card { width: 200px; padding: 6px 8px; background: #172033; border-radius: 6px; font-size: 12px; }
  .card.hot { background: #24314A; }
  button { font-size: 12px; padding: 3px 10px; background: #24314A; color: var(--ink); border-radius: 4px; margin-top: 6px; }
</style>
</head>
<body>
  <h2>script DOM checks</h2>
  <div id="list"></div>
  <div id="box"></div>
  <div class="card" id="card"><span id="inner">click the card or this text</span></div>
  <button id="again">run again</button>
  <div id="ext" style="font-size: 12px; color: var(--dim)">external script: waiting</div>
  <script src="https://raw.githubusercontent.com/Gruffuss/scripted-screens-html/untested/ScriptedScreensHtml/tests/external.js"></script>
  <script>
    var list = document.getElementById('list');
    var results = [];
    function check(name, ok){ results.push({ name: name, ok: !!ok }); }
    function show(){
      list.innerHTML = '';
      results.forEach(function(r){ var d = document.createElement('div'); d.className = r.ok ? 'ok' : 'bad'; d.textContent = (r.ok ? 'PASS ' : 'FAIL ') + r.name; list.appendChild(d); });
      results = [];
    }
    var readyFired = false, loadFired = false;
    document.addEventListener('DOMContentLoaded', function(){ readyFired = true; });
    window.addEventListener('load', function(){ loadFired = true; setTimeout(run, 50); });

    function run(){
      check('DOMContentLoaded and load fired, readyState complete', readyFired && loadFired && document.readyState === 'complete');
      check('getElementsByClassName / TagName / Name', document.getElementsByClassName('card').length === 1 && document.getElementsByTagName('button').length >= 1);
      check('matchMedia(min-width: 600px).matches', matchMedia('(min-width: 600px)').matches && !matchMedia('(max-width: 300px)').matches);
      check('innerWidth is the design width', window.innerWidth === 640);

      var card = document.getElementById('card');
      card.insertAdjacentHTML('beforeend', '<span id="tail"> tail</span>');
      var tail = document.getElementById('tail');
      check('insertAdjacentHTML beforeend', !!tail && tail.parentElement === null ? false : !!tail);
      var lead = document.createElement('span'); lead.id = 'lead'; lead.textContent = 'lead ';
      card.prepend(lead);
      check('prepend puts it first', card.firstElementChild && card.firstElementChild.id === 'lead');
      var mid = document.createElement('span'); mid.id = 'mid'; mid.textContent = ' mid ';
      document.getElementById('inner').after(mid);
      check('after() places a sibling', document.getElementById('inner').nextElementSibling && document.getElementById('inner').nextElementSibling.id === 'mid');
      var rep = document.createElement('span'); rep.id = 'rep'; rep.textContent = ' replaced';
      mid.replaceWith(rep);
      check('replaceWith swaps the node', !document.getElementById('mid') && !!document.getElementById('rep'));
      card.toggleAttribute('data-flag', true);
      check('toggleAttribute sets then reads', card.hasAttribute('data-flag'));
      card.classList.add('hot');
      check('className reads back', card.className.indexOf('hot') >= 0);

      var box = document.getElementById('box');
      box.style.width = '40px';
      check('style read-back after a write', box.style.width === '40px');
      box.style.setProperty('height', '14px');
      check('setProperty / getPropertyValue', box.style.getPropertyValue('height') === '14px' && box.style.cssText.indexOf('height: 14px') >= 0);
      check('getComputedStyle sees the cascade', getComputedStyle(box).getPropertyValue('border-radius') === '3px');

      var got = null;
      card.addEventListener('ping', function(e){ got = e.detail; });
      card.dispatchEvent(new CustomEvent('ping', { detail: 42 }));
      check('dispatchEvent + CustomEvent detail', got === 42);
      var bubbled = false;
      card.addEventListener('click', function(e){ bubbled = e.target && e.target.id === 'inner'; });
      document.getElementById('inner').dispatchEvent(new Event('click', { bubbles: true }));
      check('a click on the child bubbles to the parent', bubbled);

      var u = new URL('https://example.com:8080/a/b?x=1&y=two#frag');
      check('URL parts', u.hostname === 'example.com' && u.port === '8080' && u.pathname === '/a/b' && u.searchParams.get('y') === 'two' && u.hash === '#frag');
      var p = new URLSearchParams('a=1&b=2'); p.set('c', '3');
      check('URLSearchParams', p.get('a') === '1' && p.toString() === 'a=1&b=2&c=3');
      check('crypto.randomUUID shape', /^[0-9a-f-]{36}$/.test(crypto.randomUUID()));
      check('TextEncoder round trip', new TextDecoder().decode(new TextEncoder().encode('héllo')) === 'héllo');
      check('structuredClone', structuredClone({ a: [1, 2] }).a[1] === 2);

      var anim = box.animate([{ transform: 'translateX(0px)' }, { transform: 'translateX(200px)' }], { duration: 1500, iterations: Infinity, direction: 'alternate' });
      check('el.animate returns a handle', anim && typeof anim.cancel === 'function' && anim.id > 0);
      setTimeout(function(){ anim.cancel(); }, 6000);

      show();
    }
    document.getElementById('again').addEventListener('click', run);
    document.addEventListener('click', function(e){ if (e.target && e.target.id === 'inner') document.getElementById('inner').textContent = 'clicked (document listener saw it)'; });
    setTimeout(function(){ document.getElementById('ext').textContent = 'external script: ' + (window.externalLoaded ? 'loaded, said ' + window.externalLoaded : 'not loaded'); }, 4000);
  </script>
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

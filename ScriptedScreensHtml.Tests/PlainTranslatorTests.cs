using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Jint;
using System.Text.RegularExpressions;
using ScriptedScreensHtml;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// The plain translator (PlainTranslator), one feature or feature group per snippet: each compiles to a
/// scene and plain Lua, its Lua carries no DOM (SpecTests.PlainLua), and it draws what a browser would.
/// </summary>
/// <remarks>
/// The differential check runs the same page twice. Once as its compiled Lua, driven as the chip drives
/// it (Probe4.DrivePlain: ticks and clicks), keeping what the data element ends up holding. Once as the
/// original JavaScript under Jint, on a DOM that records what the script leaves each element with
/// (text, class, inline style), with timers on a virtual clock and clicks bubbling to the ancestors -
/// and that final DOM is then applied to the page and laid out and emitted by the real engine. The two
/// scenes, every value filled in, must match line by line: numbers within half a pixel (the layout
/// rounds to whole pixels, a scale and offset does not), text exactly. Nothing is compared to anything
/// the translator itself computed.
/// </remarks>
internal static class PlainTranslatorTests
{
    private const string Head = "<html><head><meta name=\"viewport\" content=\"width=400\"><style>"
        + "body { background:#111; color:#ddd; font-family: Barlow; padding: 10px; font-size: 16px; }"
        + "button { width: 90px; height: 30px; }";

    private static string Page(string css, string body, string script)
        => Head + css + "</style></head><body>" + body + "<script>" + script + "</script></body></html>";

    private static List<(double, string?)> Ticks(int n, double dt = 0.5) => Enumerable.Repeat((dt, (string?)null), n).ToList();

    private static List<(double, string?)> Steps(params object[] steps)
        => steps.Select(s => s is string c ? (0.0, (string?)c) : (Convert.ToDouble(s, CultureInfo.InvariantCulture), (string?)null)).ToList();

    /// <summary>Each feature group: a snippet, how it is driven, and the step counts after which the two runs are compared.</summary>
    private static readonly (string Feature, string Page, List<(double, string?)> Steps, int[] Checkpoints)[] Cases =
    {
        ("setInterval + clearInterval: a counter's readout (number with units, toFixed) and a bar width in %",
         Page(".track { width: 300px; height: 12px; background: #333; } #bar { height: 12px; width: 0%; background: #3a7; }",
              "<p>Pressure: <span id=\"kpa\">0.0 kPa</span></p><div class=\"track\"><div id=\"bar\"></div></div>",
              "let n = 0;" +
              "const readout = document.getElementById('kpa');" +
              "const timer = setInterval(function () {" +
              "  n = n + 1;" +
              "  readout.textContent = (n * 12.5).toFixed(1) + ' kPa';" +
              "  document.getElementById('bar').style.width = Math.min(100, n * 15) + '%';" +
              "  if (n >= 8) clearInterval(timer);" +
              "}, 500);"),
         Ticks(12), new[] { 1, 3, 12 }),

        ("addEventListener('click'): a button toggles a class and changes a label",
         Page(".lamp { width: 40px; height: 40px; background: #522; border-radius: 20px; } .lamp.on { background: #2e5; }",
              "<div id=\"lamp\" class=\"lamp\"></div><p id=\"state\">OFF</p><button id=\"power\">Power</button>",
              "let on = false;" +
              "document.getElementById('power').addEventListener('click', () => {" +
              "  on = !on;" +
              "  document.getElementById('lamp').classList.toggle('on');" +
              "  document.getElementById('state').textContent = on ? 'ON' : 'OFF';" +
              "});"),
         Steps("power", 0.5, "power", "power"), new[] { 1, 3, 4 }),

        ("onclick and bubbling: a click on a button reaches its own onclick and its panel's listener",
         Page(".panel { padding: 8px; background: #222; } #last { color: #ccc; }",
              "<div id=\"panel\" class=\"panel\"><button id=\"a\">A</button><button id=\"b\">B</button></div><p id=\"last\">none</p>",
              "const last = document.getElementById('last');" +
              "let count = 0;" +
              "document.getElementById('panel').addEventListener('click', function () { count++; last.textContent = 'clicks: ' + count; });" +
              "document.getElementById('a').onclick = () => { last.style.color = '#f80'; };" +
              "document.getElementById('b').onclick = () => last.style.color = '#08f';"),
         Steps("a", "b", "b", 0.5), new[] { 1, 3 }),

        ("a function shared by two timers: setTimeout, a text and a colour passed as arguments",
         Page(".dot { position: absolute; left: 20px; top: 100px; width: 16px; height: 16px; background: #888; }",
              "<p id=\"msg\">idle</p><div id=\"dot\" class=\"dot\"></div>",
              "function show(text, colour) {" +
              "  document.getElementById('msg').textContent = text;" +
              "  document.getElementById('dot').style.background = colour;" +
              "}" +
              "setTimeout(() => show('warming up', '#fa0'), 700);" +
              "setTimeout(() => show('ready', '#0c6'), 1900);"),
         Ticks(6), new[] { 1, 2, 4, 6 }),

        ("an element held in a variable, in a function; clearTimeout cancels a pending timer",
         Page("#clock { font-size: 20px; }",
              "<p id=\"clock\">0s</p><p id=\"note\">waiting</p>",
              "let secs = 0;" +
              "function tick() { const out = document.getElementById('clock'); secs += 1; out.textContent = `${secs}s`; }" +
              "setInterval(tick, 1000);" +
              "const never = setTimeout(() => { document.getElementById('note').textContent = 'too late'; }, 2500);" +
              "setTimeout(() => { clearTimeout(never); document.getElementById('note').textContent = 'cancelled'; }, 1500);"),
         Ticks(8), new[] { 2, 3, 8 }),

        ("a colour picked by a condition from a constant table, and className set by a ternary",
         Page("#status { position: absolute; left: 10px; top: 60px; width: 120px; height: 24px; background: #444; } .badge { color: #999; } .badge.hot { color: #f55; }",
              "<div id=\"status\"></div><p id=\"label\" class=\"badge\">OK</p>",
              "const COLOURS = { ok: '#2a4', warn: '#fa0', alarm: '#e33' };" +
              "const status = document.getElementById('status');" +
              "const label = document.getElementById('label');" +
              "let level = 0;" +
              "setInterval(() => {" +
              "  level++;" +
              "  const state = level > 4 ? 'alarm' : level > 2 ? 'warn' : 'ok';" +
              "  status.style.backgroundColor = COLOURS[state];" +
              "  label.textContent = state.toUpperCase();" +
              "  label.className = level > 4 ? 'badge hot' : 'badge';" +
              "}, 500);"),
         Ticks(7), new[] { 1, 3, 5, 7 }),

        ("toFixed and a template literal with two values in one label",
         Page("", "<p id=\"t\">T 20.0 °C · 0 samples</p>",
              "let temp = 20, samples = 0;" +
              "setInterval(() => { temp += 0.25; samples += 1; document.getElementById('t').textContent = `T ${temp.toFixed(1)} °C · ${samples} samples`; }, 300);"),
         Ticks(5), new[] { 1, 5 }),

        ("the script's own logic: arrays, a loop, Math, an object; a px width and a class from it",
         Page("#gauge { position: absolute; left: 10px; top: 80px; height: 10px; width: 50px; background: #36c; } #gauge.hot { background: #c33; }",
              "<p id=\"out\">no data</p><div id=\"gauge\"></div>",
              "const out = document.getElementById('out');" +
              "const gauge = document.getElementById('gauge');" +
              "const readings = [];" +
              "let t = 0;" +
              "setInterval(() => {" +
              "  t++;" +
              "  readings.push(20 + (t % 5));" +
              "  if (readings.length > 4) readings.shift();" +
              "  let sum = 0;" +
              "  for (let i = 0; i < readings.length; i++) sum += readings[i];" +
              "  const avg = sum / readings.length;" +
              "  out.textContent = 'avg ' + avg.toFixed(2) + ' over ' + readings.length;" +
              "  const state = { hot: avg > 21.5 };" +
              "  gauge.style.width = Math.round(avg * 10) + 'px';" +
              "  if (state.hot) gauge.classList.add('hot'); else gauge.classList.remove('hot');" +
              "}, 400);"),
         Ticks(9), new[] { 1, 4, 9 }),

        ("style.left in px and style.opacity as numbers on out-of-flow elements",
         Page("#ship { position: absolute; left: 0px; top: 40px; width: 20px; height: 20px; background: #ddd; } #fade { position: absolute; left: 200px; top: 40px; width: 40px; height: 40px; background: #a5f; }",
              "<div id=\"ship\"></div><div id=\"fade\"></div>",
              "let x = 0, n = 0;" +
              "setInterval(() => { x += 7; n++; document.getElementById('ship').style.left = x + 'px'; document.getElementById('fade').style.opacity = 1 - n / 10; }, 250);"),
         Ticks(6), new[] { 1, 6 }),

        ("text written in two shapes, a word and a number, into a label that starts empty",
         Page("", "<p>Level: <span id=\"lvl\"></span></p>",
              "let v = 0;" +
              "setInterval(() => { v += 1; document.getElementById('lvl').textContent = v > 3 ? 'FULL' : v * 25 + ' %'; }, 500);"),
         Ticks(6), new[] { 1, 2, 6 }),
    };

    /// <summary>
    /// The window and the element's own state: location, storage, the window's size, console, attributes,
    /// hidden, dataset, and the script reading back what it wrote. Each compiled for every console shape.
    /// </summary>
    private static readonly (string Feature, string Page, List<(double, string?)> Steps, int[] Checkpoints)[] Browser =
    {
        ("location: what a page loaded from no URL reads, and a hash set by a click",
         Page("", "<p id=\"where\">?</p><p id=\"hash\">none</p><p id=\"anc\">-</p><button id=\"tab\">Settings</button><button id=\"back\">Main</button>",
              "const where = document.getElementById('where');" +
              "where.textContent = location.href + ' ' + location.protocol + ' ' + location.pathname + ' ' + window.location.origin + ' ' + (location.search || '-') + (location.hash || '-');" +
              "if (typeof location !== 'undefined' && location.host === '' && location.port === '' && location.hostname === '') document.getElementById('hash').textContent = 'blank';" +
              "document.getElementById('anc').textContent = location.ancestorOrigins.length + ' ' + location.ancestorOrigins.contains('x') + ' ' + location.ancestorOrigins.item(0);" +
              "document.getElementById('tab').addEventListener('click', () => {" +
              "  location.hash = 'settings';" +
              "  document.getElementById('hash').textContent = location.hash + ' ' + location.href;" +
              "});" +
              "document.getElementById('back').addEventListener('click', () => {" +
              "  location.assign('#main view');" +
              "  document.getElementById('hash').textContent = location.hash + ' ' + location.toString();" +
              "});"),
         Steps("tab", "back", "tab"), new[] { 0, 1, 2, 3 }),

        ("location.reload(): the page starts again, its session storage and hash kept, its timers and clicks gone",
         Page("", "<p id=\"loads\">-</p><p id=\"clicks\">0</p><p id=\"total\">0</p><p id=\"secs\">0</p><button id=\"more\">More</button><button id=\"again\">Reload</button><button id=\"frag\">Frag</button><p id=\"h\">-</p>",
              "const seen = Number(sessionStorage.getItem('loads') || 0);" +
              "document.getElementById('loads').textContent = 'loaded ' + seen;" +
              "sessionStorage.setItem('loads', seen + 1);" +
              "let clicks = 0, secs = 0;" +
              "setInterval(() => { secs += 1; document.getElementById('secs').textContent = secs; }, 700);" +
              "document.getElementById('more').addEventListener('click', () => {" +
              "  clicks++; document.getElementById('clicks').textContent = clicks;" +
              "  const total = Number(sessionStorage.getItem('total') || 0) + 1;" +
              "  sessionStorage.setItem('total', total); document.getElementById('total').textContent = total;" +
              "});" +
              "document.getElementById('again').addEventListener('click', () => { location.reload(); document.getElementById('clicks').textContent = 'reloading'; });" +
              "document.getElementById('frag').onclick = () => { location.replace('#b'); document.getElementById('h').textContent = location.hash; };"),
         Steps("more", "more", 1.0, "frag", "again", 0.5, "more", 1.0, 0.5), new[] { 2, 3, 5, 6, 7, 9 }),

        ("sessionStorage and localStorage: setItem, getItem, key, length, removeItem and clear",
         Page("", "<p id=\"a\">-</p><p id=\"b\">-</p><button id=\"put\">Put</button><button id=\"drop\">Drop</button><button id=\"wipe\">Wipe</button>",
              "const a = document.getElementById('a'), b = document.getElementById('b');" +
              "a.textContent = localStorage.length + ' ' + (localStorage.getItem('mode') === null ? 'auto' : localStorage.getItem('mode'));" +
              "document.getElementById('put').addEventListener('click', () => {" +
              "  localStorage.setItem('mode', 'manual');" +
              "  window.localStorage.setItem('target', 21.5);" +
              "  sessionStorage.setItem('n', sessionStorage.length + 1);" +
              "  a.textContent = localStorage.length + ' ' + localStorage.getItem('mode') + ' ' + localStorage.key(1) + '=' + localStorage.getItem('target');" +
              "  b.textContent = 'session ' + sessionStorage.getItem('n');" +
              "});" +
              "document.getElementById('drop').addEventListener('click', () => { localStorage.removeItem('mode'); a.textContent = localStorage.length + ' ' + localStorage.key(0); });" +
              "document.getElementById('wipe').addEventListener('click', () => { localStorage.clear(); sessionStorage.clear(); a.textContent = localStorage.length + ' ' + sessionStorage.length; });"),
         Steps("put", "put", "drop", "wipe", "put"), new[] { 0, 1, 3, 4, 5 }),

        ("innerWidth, innerHeight and devicePixelRatio: the console the page is compiled for",
         Page("#dot { position: absolute; top: 60px; left: 0px; width: 20px; height: 20px; background: #fa0; }",
              "<p id=\"size\">?</p><div id=\"dot\"></div>",
              "document.getElementById('size').textContent = innerWidth + ' x ' + window.innerHeight + ' @' + window.devicePixelRatio;" +
              "document.getElementById('dot').style.left = (window.innerWidth - 40) + 'px';"),
         Steps(0.5), new[] { 0 }),

        ("console: every call goes, and what its arguments do stays",
         Page("", "<p id=\"n\">0</p><button id=\"b\">B</button>",
              "let n = 0;" +
              "console.log('start', n, document.getElementById('n'));" +
              "console.info(n++);" +
              "const log = console.log;" +
              "log('never', n);" +
              "const levels = [1, 2];" +
              "levels.forEach(console.debug);" +
              "console.group('g'); console.groupCollapsed('c'); console.groupEnd(); console.time('t'); console.timeLog('t'); console.timeEnd('t');" +
              "console.timeStamp('s'); console.assert(n >= 0, 'n'); console.count('k'); console.countReset('k'); console.trace('here');" +
              "console.dir(n); console.dirxml(n); console.exception('x'); console.profile('p'); console.profileEnd('p'); console.clear();" +
              "setInterval(() => { console.warn('tick', n); n += 1; document.getElementById('n').textContent = n; console.table({ n: n }); }, 500);" +
              "document.getElementById('b').addEventListener('click', () => console.error('click', n += 10));"),
         Steps(0.5, 0.5, "b", 0.5), new[] { 0, 2, 4 }),

        ("setAttribute, removeAttribute, toggleAttribute and dataset on attributes CSS selects on: laid-out states",
         Page(".panel { width: 120px; height: 30px; background: #333; } .panel[data-state=\"warn\"] { background: #c80; }" +
              ".panel[data-state=\"alarm\"] { background: #e22; height: 50px; } .panel[data-muted] { width: 60px; }",
              "<div id=\"panel\" class=\"panel\" data-state=\"ok\" data-kind=\"pressure\"></div><p id=\"label\">ok</p><button id=\"cycle\">Next</button><button id=\"mute\">Mute</button>",
              "const panel = document.getElementById('panel');" +
              "const states = ['ok', 'warn', 'alarm'];" +
              "let i = 0;" +
              "document.getElementById('cycle').addEventListener('click', () => {" +
              "  i = (i + 1) % 3;" +
              "  panel.setAttribute('data-state', states[i]);" +
              "  document.getElementById('label').textContent = panel.getAttribute('data-kind') + ' ' + panel.dataset.state + (panel.hasAttribute('data-muted') ? ' muted' : '');" +
              "});" +
              "document.getElementById('mute').addEventListener('click', () => {" +
              "  const muted = panel.toggleAttribute('data-muted');" +
              "  if (panel.dataset.state === 'alarm') { delete panel.dataset.state; }" +
              "  document.getElementById('label').textContent = (muted ? 'muted ' : 'live ') + ('state' in panel.dataset ? panel.getAttribute('data-state') : 'none');" +
              "});"),
         Steps("cycle", "cycle", "mute", "cycle", "mute", "mute", "cycle"), new[] { 1, 2, 3, 4, 5, 7 }),

        ("attributes nothing selects on: kept as the program's own values, read back by getAttribute and dataset",
         Page("", "<p id=\"out\">-</p><button id=\"b\" data-count=\"0\">Count</button>",
              "const b = document.getElementById('b');" +
              "b.addEventListener('click', () => {" +
              "  b.dataset.count = Number(b.dataset.count) + 1;" +
              "  b.setAttribute('aria-label', 'pressed ' + b.getAttribute('data-count') + ' times');" +
              "  if (Number(b.dataset.count) > 2) b.removeAttribute('title'); else b.setAttribute('title', 'low');" +
              "  document.getElementById('out').textContent = b.getAttribute('aria-label') + ' ' + b.hasAttribute('title') + ' ' + b.getAttribute('ti' + 'tle');" +
              "});"),
         Steps("b", "b", "b"), new[] { 1, 3 }),

        ("hidden: the element leaves the layout and comes back, and one hidden from the start shows",
         Page(".banner { height: 30px; background: #a33; } .box { height: 20px; background: #3a3; margin-top: 4px; }",
              "<div id=\"banner\" class=\"banner\">Warning</div><div id=\"box\" class=\"box\"></div><p id=\"state\">shown</p><button id=\"t\">Toggle</button><div id=\"later\" hidden>Details here</div>",
              "const banner = document.getElementById('banner');" +
              "document.getElementById('t').addEventListener('click', () => {" +
              "  banner.hidden = !banner.hidden;" +
              "  document.getElementById('later').hidden = !banner.hidden;" +
              "  document.getElementById('state').textContent = banner.hidden ? 'hidden' : 'shown';" +
              "});"),
         Steps("t", "t", "t"), new[] { 0, 1, 2, 3 }),

        ("reads: textContent, className, classList.contains and style answer what the script wrote",
         Page(".bar { position: absolute; left: 10px; top: 120px; height: 10px; background: #36c; } .bar.full { background: #c33; }",
              "<p id=\"count\">0</p><div id=\"bar\" class=\"bar\" style=\"width: 10px\"></div><p id=\"info\">-</p><p id=\"note\">\n    Ready   now\n  </p><button id=\"b\">Add</button>",
              "const count = document.getElementById('count');" +
              "const bar = document.getElementById('bar');" +
              "document.getElementById('info').textContent = bar.className + ' ' + bar.style.width + ' ' + bar.style.color + '|' + document.getElementById('note').textContent.length;" +
              "document.getElementById('b').addEventListener('click', () => {" +
              "  const n = Number(count.textContent) + 1;" +
              "  count.textContent = n;" +
              "  bar.style.width = (parseInt(bar.style.width) + 20) + 'px';" +
              "  if (n >= 2 && !bar.classList.contains('full')) bar.classList.add('full');" +
              "  document.getElementById('info').textContent = bar.className + ' ' + bar.style.width + ' ' + bar.classList.contains('bar') + ' ' + count.textContent;" +
              "});"),
         Steps("b", "b", "b"), new[] { 0, 1, 2, 3 }),

        ("reads: a colour and a keyword written from a fixed set come back as the browser prints them",
         Page("#lamp { position: absolute; left: 10px; top: 60px; width: 30px; height: 30px; background-color: #222; }",
              "<div id=\"lamp\"></div><p id=\"out\">-</p><button id=\"b\">B</button>",
              "const lamp = document.getElementById('lamp');" +
              "let on = false;" +
              "document.getElementById('b').addEventListener('click', () => {" +
              "  on = !on;" +
              "  lamp.style.backgroundColor = on ? '#2E5' : '#ff000080';" +
              "  lamp.style.visibility = on ? 'VISIBLE' : 'visible';" +
              "  document.getElementById('out').textContent = lamp.style.backgroundColor + ' ' + lamp.style.visibility;" +
              "});"),
         Steps("b", "b"), new[] { 1, 2 }),
    };

    /// <summary>Features outside what is translated, each refused by name - the page keeps its old path.</summary>
    private static readonly (string Feature, string Page, string Reason)[] Refusals =
    {
        ("requestAnimationFrame", Page("", "<p id=\"a\">x</p>", "requestAnimationFrame(() => { document.getElementById('a').textContent = 'y'; });"), "requestAnimationFrame"),
        ("innerHTML", Page("", "<div id=\"a\"><p>x</p></div>", "setTimeout(() => { document.getElementById('a').innerHTML = '<b>y</b>'; }, 10);"), ".innerHTML of \"a\""),
        ("reading innerText", Page("", "<p id=\"a\">1</p>", "setTimeout(() => { const el = document.getElementById('a'); el.textContent = el.innerText + '!'; }, 10);"), "reading or computing with .innerText"),
        ("an element chosen at run time", Page("", "<p id=\"a1\">x</p>", "let i = 1; setTimeout(() => { document.getElementById('a' + i).textContent = 'y'; }, 10);"), "an element chosen at run time"),
        ("an element used as a value", Page("", "<p id=\"a\">x</p>", "function paint(el) { el.textContent = 'y'; } paint(document.getElementById('a'));"), "used as a value"),
        ("a non-click event", Page("", "<button id=\"b\">x</button>", "document.getElementById('b').addEventListener('mousedown', () => {});"), "delivers only clicks"),
        ("reading the event object", Page("", "<button id=\"b\">x</button><p id=\"a\">x</p>", "document.getElementById('b').addEventListener('click', (e) => { document.getElementById('a').textContent = e.type; });"), "reads its event object"),
        ("querySelector", Page("", "<p class=\"a\">x</p>", "setTimeout(() => { document.querySelector('.a').textContent = 'y'; }, 10);"), "document.querySelector"),
        ("a size that moves its siblings", Page("#a { height: 10px; background: #fff; }", "<div id=\"a\"></div><p>below</p>", "setTimeout(() => { document.getElementById('a').style.height = 30 + 'px'; }, 10);"), "is in normal flow"),
        ("classList.item (a read not translated)", Page("", "<p id=\"a\" class=\"x\">x</p>", "setTimeout(() => { const a = document.getElementById('a'); if (a.classList.item(0)) a.textContent = 'y'; }, 10);"), "classList.item"),
        ("navigating to another document", Page("", "<button id=\"b\">x</button>", "document.getElementById('b').onclick = () => { location.href = 'https://example.com/'; };"), "has no network to fetch another"),
        ("changing location.search", Page("", "<button id=\"b\">x</button>", "document.getElementById('b').onclick = () => { location.search = '?a=1'; };"), "location.search = …, which loads another document"),
        ("location.assign to a URL only known at run time", Page("", "<button id=\"b\">x</button>", "let u = 'page' + 2; document.getElementById('b').onclick = () => { location.assign(u); };"), "may be another document"),
        ("location used as a value", Page("", "<p id=\"a\">x</p>", "const l = location; setTimeout(() => { document.getElementById('a').textContent = l.hash; }, 10);"), "location used this way"),
        ("a named Storage property", Page("", "<p id=\"a\">x</p>", "localStorage.mode = 'x'; setTimeout(() => { document.getElementById('a').textContent = 'y'; }, 10);"), "named properties are not yet"),
        ("an attribute CSS selects on, set to a value known only at run time", Page("[data-v=\"1\"] { color: red; }", "<p id=\"a\">x</p>", "let v = 0; setInterval(() => { v++; document.getElementById('a').setAttribute('data-v', v); }, 10);"), "set to a value only known at run time"),
        ("an attribute the page draws from directly", Page("", "<img id=\"a\" src=\"x.png\">", "setTimeout(() => { document.getElementById('a').setAttribute('width', '20'); }, 10);"), "draws from directly"),
        ("setAttribute with a name known only at run time", Page("", "<p id=\"a\">x</p>", "let k = 'data-' + 1; setTimeout(() => { document.getElementById('a').setAttribute(k, 'y'); }, 10);"), "with a name only known at run time"),
        ("writing innerWidth", Page("", "<p id=\"a\">x</p>", "window.innerWidth = 5; setTimeout(() => { document.getElementById('a').textContent = 'y'; }, 10);"), "innerWidth written by the script"),
        ("a constant text that wraps at this console's size", Page("#a { width: 60px; }", "<p id=\"a\">ok</p><p>below</p>", "setTimeout(() => { document.getElementById('a').textContent = 'a message far too long for sixty pixels'; }, 10);"), "\"a message far too long for sixty pixels\" in \"a\""),
        ("a text that pushes the rest of its line along", Page("", "<p><span id=\"a\">1</span> then <span id=\"b\">2</span></p>", "setTimeout(() => { document.getElementById('a').textContent = 'one hundred'; }, 10);"), "moves the scene's"),
        ("a class name computed at run time", Page("", "<p id=\"a\">x</p>", "let k = 'c' + 1; setTimeout(() => { document.getElementById('a').className = k; }, 10);"), "className of \"a\" set to a value only known at run time"),
    };

    /// <summary>The console shapes a page is compiled for in game: square, wide and tall, in canvas units.</summary>
    private static readonly (float W, float H)[] Consoles = { (460f, 460f), (1036f, 460f), (460f, 1036f) };

    /// <summary>Pages seen in game, each compiled for every console shape and checked there.</summary>
    private static readonly (string File, List<(double, string?)> Steps, int[] Checkpoints)[] InGame =
    {
        (System.IO.Path.Combine("ScriptedScreensHtml", "examples", "09-transition.lua"), Ticks(8), new[] { 1, 2, 6, 8 }),
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-counter.lua"), Ticks(12), new[] { 1, 4, 12 }),
    };

    internal static void Run(Action<bool, string> check)
    {
        foreach (var (feature, page, steps, checkpoints) in Cases)
        {
            try { One(feature, page, steps, checkpoints, check); }
            catch (Exception ex) { check(false, $"plain [{feature}]: threw - {ex.Message.Split('\n')[0]}"); }
        }
        foreach (var (feature, page, steps, checkpoints) in Browser)
            foreach (var console in Consoles)
            {
                var name = $"{feature} on a {console.W:0}x{console.H:0} console";
                try { One(name, page, steps, checkpoints, check, console); }
                catch (Exception ex) { check(false, $"plain [{name}]: threw - {ex.Message.Split('\n')[0]}"); }
            }
        Persisted(check);
        ConsoleGone(check);
        var root = Root();
        foreach (var (file, steps, checkpoints) in InGame)
            foreach (var console in Consoles)
            {
                var name = $"{System.IO.Path.GetFileName(file)} on a {console.W:0}x{console.H:0} console";
                try
                {
                    var text = System.IO.File.ReadAllText(System.IO.Path.Combine(root, file));
                    One(name, MarkupProbe.Bracketed(text) ?? text, steps, checkpoints, check, console);
                }
                catch (Exception ex) { check(false, $"plain [{name}]: threw - {ex.Message.Split('\n')[0]}"); }
            }
        ScriptRanFirst(root, check);
        Retired(check);
        Eligibility(root, check);
        Sizes(check);
        foreach (var (feature, page, reason) in Refusals)
        {
            CompiledPage.Result compiled;
            try { compiled = Probe4.Headless(page).Compiled; }
            catch (Exception ex) { check(false, $"plain refuses [{feature}]: threw - {ex.Message.Split('\n')[0]}"); continue; }
            var said = compiled.Warnings.FirstOrDefault(w => w.StartsWith("not translated to plain Lua:", StringComparison.Ordinal)) ?? "";
            check(!compiled.Plain && said.Contains(reason, StringComparison.Ordinal),
                  $"plain refuses [{feature}] by name ({(compiled.Plain ? "it compiled plainly" : said.Length > 0 ? said : "no reason given")})");
        }
    }

    /// <summary>
    /// The in-game order before the push-time compile: the interpreter ran the page's script for a
    /// couple of its ticks - text written, an inline width and colour, a class - and the compile then
    /// read that live page. It must compile exactly what it compiles from the page as written: the
    /// same scene, the same opening values, the same program.
    /// </summary>
    private static void ScriptRanFirst(string root, Action<bool, string> check)
    {
        var runs = new (string File, Action<HtmlRenderer.Result> Script)[]
        {
            (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-counter.lua"), built =>
            {
                Write(built, "kpa", "25.0 kPa");
                Style(built, "bar", "width", "25%");
                Style(built, "bar", "background", "#2F855A");
            }),
            (System.IO.Path.Combine("ScriptedScreensHtml", "examples", "09-transition.lua"), built =>
            {
                built.Reclass(built.ById["bar"], "bar wide");
                Write(built, "note", "transition started");
            }),
        };
        foreach (var (file, script) in runs)
            foreach (var console in Consoles)
            {
                var name = $"{System.IO.Path.GetFileName(file)} on a {console.W:0}x{console.H:0} console";
                try
                {
                    var text = System.IO.File.ReadAllText(System.IO.Path.Combine(root, file));
                    var page = MarkupProbe.Bracketed(text) ?? text;
                    var clean = Probe4.Headless(page, out _, out _, out _, console: console).Compiled;
                    var touched = Probe4.Headless(page, out _, out _, out _, console: console, beforeCompile: script).Compiled;
                    var same = touched.Plain && clean.Plain && touched.Structure == clean.Structure && touched.Lua == clean.Lua
                               && touched.StructureValues!.Count == clean.StructureValues!.Count
                               && touched.StructureValues.All(p => clean.StructureValues.TryGetValue(p.Key, out var v) && v.Equals(p.Value));
                    check(same, $"plain [{name}]: a script that ran before the compile changes nothing it compiles"
                                + (same ? "" : $" (plain {touched.Plain}; {string.Join("; ", touched.Warnings.Take(2))}; opening "
                                    + string.Join(",", (touched.StructureValues ?? new()).Select(p => p.Key + "=" + (p.Value.IsNumber ? p.Value.Number.ToString(CultureInfo.InvariantCulture) : p.Value.Text))) + ")"));
                }
                catch (Exception ex) { check(false, $"plain [{name}]: script-first threw - {ex.Message.Split('\n')[0]}"); }
            }

        // As ScriptHost writes: textContent replaces the label's text and its node's children; a style
        // write goes to the node's script styles, which outlive a re-cascade and beat the rules.
        static void Write(HtmlRenderer.Result built, string id, string value)
        {
            var label = (Label)built.ById[id];
            var node = built.NodeOf[label];
            label.text = value;
            node.Children.Clear();
            node.Children.Add(new HtmlNode { Text = value, Parent = node });
        }
        static void Style(HtmlRenderer.Result built, string id, string property, string value)
        {
            var ve = built.ById[id];
            var node = built.NodeOf[ve];
            (node.ScriptStyle ??= new Dictionary<string, string>(StringComparer.Ordinal))[property] = value;
            built.Reclass(ve, node.Attr("class") ?? string.Empty);
        }
    }

    /// <summary>
    /// A page replaced after its hand-over (ChipHost.Retire sets its V_LIVE false): its timers stop,
    /// nothing more is sent, a click on its scene does nothing, and the tick it chained after still
    /// runs every time.
    /// </summary>
    private static void Retired(Action<bool, string> check)
    {
        var page = Page(".lamp { width: 40px; height: 40px; background: #522; } .lamp.on { background: #2e5; }",
                        "<div id=\"lamp\" class=\"lamp\"></div><p id=\"n\">0</p><button id=\"b\">B</button>",
                        "let n = 0; setInterval(() => { n++; document.getElementById('n').textContent = n; }, 500);" +
                        "document.getElementById('b').addEventListener('click', () => document.getElementById('lamp').classList.toggle('on'));");
        var compiled = Probe4.Headless(page).Compiled;
        if (!compiled.Plain || compiled.Lua == null) { check(false, "plain retired: the page does not compile plainly"); return; }
        var log = Probe4.DrivePlain(compiled.Lua, Steps(0.5, 0.5, "!retire", 0.5, 0.5, "b", 0.5), out var data);
        var sets = log.Where(l => l.StartsWith("set_props", StringComparison.Ordinal)).ToList();
        var retired = log.IndexOf("retired");
        var after = log.Skip(retired).Count(l => l.StartsWith("set_props", StringComparison.Ordinal));
        var ok = !log.Any(l => l.StartsWith("FAILED", StringComparison.Ordinal)) && retired > 0 && sets.Count == 2 && after == 0
                 && log[^1] == "author ticks 5, its tick untouched" && data.TryGetValue("n_p0", out var n) && n.ToString() == "2";
        check(ok, ok ? "plain retired: a replaced page's program stops - no timer, no send, no click - and the tick it chained after still runs"
                     : "plain retired: " + string.Join(" | ", log));
    }

    /// <summary>What decides that a page's script waits for its compile: its DOM use, read from the source alone.</summary>
    private static void Eligibility(string root, Action<bool, string> check)
    {
        var expect = new (string File, bool Eligible)[]
        {
            (System.IO.Path.Combine("ScriptedScreensHtml", "examples", "09-transition.lua"), true),
            (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-counter.lua"), true),
            (System.IO.Path.Combine("ScriptedScreensHtml", "AtmoDark.lua"), false),
            (System.IO.Path.Combine("ScriptedScreensHtml", "examples", "07-game.lua"), false),
        };
        foreach (var (file, eligible) in expect)
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(root, file));
            ResolvedStyle.DefaultFace = FontLibrary.Default();
            var built = HtmlRenderer.Build(MarkupProbe.Bracketed(text) ?? text, FontLibrary.Default());
            var got = PlainTranslator.Eligible(built);
            check(got == eligible, $"plain: {System.IO.Path.GetFileName(file)} {(eligible ? "is" : "is not")} compiled before its script runs (got {got})");
        }
    }

    /// <summary>The size a console's page is compiled for, including while its screen is off and its rect never laid out.</summary>
    private static void Sizes(Action<bool, string> check)
    {
        var none = Vector2.zero;
        var cases = new (string What, float Design, Vector2 Rect, Vector2 World, Vector2 Pushed, Vector2 Want)[]
        {
            ("a laid-out square console", 0, new(460, 460), new(1, 1), new(460, 460), new(460, 460)),
            ("a laid-out wide console, its world aspect", 0, new(1036, 460), new(2.25f, 1), new(1036, 460), new(1036, 460)),
            ("a wide console whose screen is off: the size it was pushed with", 0, none, none, new(1036, 460), new(1036, 460)),
            ("a tall console whose screen is off", 0, none, none, new(460, 1036), new(460, 1036)),
            ("a 768 viewport on a tall console whose screen is off", 768, none, none, new(460, 1036), new(768, 768 * 1036f / 460f)),
            ("nothing known: the canvas's 460 square, never the 64 floor", 0, none, none, none, new(460, 460)),
        };
        foreach (var (what, design, rect, world, pushed, want) in cases)
        {
            var got = PlainTranslator.ConsoleLayout(design, rect, world, pushed);
            check(Mathf.Abs(got.x - want.x) < 0.5f && Mathf.Abs(got.y - want.y) < 0.5f, $"plain size: {what} is {want.x:0.#}x{want.y:0.#} (got {got.x:0.#}x{got.y:0.#})");
        }
    }

    private static string Root()
    {
        for (var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "ScriptedScreensHtml.Tests", "ingame"))) return dir.FullName;
        throw new System.IO.DirectoryNotFoundException("the workspace root was not found above " + AppContext.BaseDirectory);
    }

    private static void One(string feature, string page, List<(double, string?)> steps, int[] checkpoints, Action<bool, string> check,
                            (float W, float H)? console = null)
    {
        var (compiled, _) = Probe4.Headless(page, out var built, out var panel, out var size, console: console);
        if (!compiled.Plain || compiled.Lua == null || compiled.Structure == null)
        {
            check(false, $"plain [{feature}]: does not compile plainly - {string.Join("; ", compiled.Warnings.Concat(compiled.Problems).Take(3))}");
            return;
        }
        SpecTests.PlainLua(feature, compiled.Lua, check);
        var script = Regex.Match(page, "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;

        foreach (var at in checkpoints)
        {
            var prefix = steps.Take(at).ToList();
            var log = Probe4.DrivePlain(compiled.Lua, prefix, out var data);
            if (log.FirstOrDefault(l => l.StartsWith("FAILED", StringComparison.Ordinal)) is { } failed)
            {
                check(false, $"plain [{feature}]: the Lua failed after {at} step(s) - {failed}");
                return;
            }
            var mine = Render(compiled.Structure, data);
            var theirs = Oracle(built, panel, size, page, script, prefix, out _);
            var diff = Compare(mine, theirs);
            check(diff == null, $"plain [{feature}]: after {at} step(s) the scene matches the page run as JavaScript and laid out"
                                + (diff == null ? "" : " - " + diff));
            if (diff != null) return;
        }
    }

    // ---- the compiled side ------------------------------------------------------------------------

    /// <summary>The compiled scene with the data element's values filled in, and every label's placeholders printed.</summary>
    private static string Render(string structure, Dictionary<string, Lua.LuaValue> data)
    {
        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        foreach (var pair in data)
            values[pair.Key] = pair.Value.TryRead<double>(out var d) ? new SceneSlots.Value((float)d) : new SceneSlots.Value(pair.Value.TryRead<string>(out var s) ? s : pair.Value.ToString());
        var scene = PlainTranslator.Literal(structure, values, new List<string>());
        scene = Regex.Replace(scene, @"\{\$(\w+)(?::([^}]*))?\}", m =>
        {
            if (!values.TryGetValue(m.Groups[1].Value, out var v)) return "--";
            if (!v.IsNumber) return v.Text!.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return m.Groups[2].Success ? PlainTranslator.Print(v.Number, m.Groups[2].Value) : v.Number.ToString("0.##", CultureInfo.InvariantCulture);
        });
        // As the vector mod draws it: a group at v=0 is not there at all (display: none), and v=1 is no group attribute.
        var lines = scene.Split('\n').ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!Regex.IsMatch(lines[i], @"^\s*G .* v=0 .*\{$")) continue;
            var depth = 0;
            var end = i;
            for (; end < lines.Count; end++)
            {
                if (lines[end].TrimEnd().EndsWith("{", StringComparison.Ordinal)) depth++;
                if (lines[end].Trim() == "}" && --depth == 0) break;
            }
            lines.RemoveRange(i, end - i + 1);
            i--;
        }
        return string.Join("\n", lines.Select(l => Regex.Replace(l, @"^(\s*G .*) v=1( .*)$", "$1$2")));
    }

    // ---- the browser side ---------------------------------------------------------------------------

    /// <summary>
    /// The page's script under Jint on a recording DOM, driven by the same steps; its final DOM laid out and
    /// emitted. What the DOM starts with (text, attributes, the style attribute) is read from the page's
    /// source here, not from the compiler's parse. A location.reload() loads the page again in a fresh
    /// engine, keeping what a browser keeps: its storage, its URL and the clock.
    /// </summary>
    /// <param name="local">localStorage as it was before the page loaded (key, value), in order.</param>
    /// <param name="localAfter">localStorage as the run leaves it.</param>
    private static string Oracle(HtmlRenderer.Result built, Panel panel, Vector2 size, string page, string script, List<(double, string?)> steps,
                                 out List<KeyValuePair<string, string>> localAfter, List<KeyValuePair<string, string>>? local = null)
    {
        HtmlRenderer.SurfaceAspect = size.y / size.x;
        (CssParser.ViewportWidth, CssParser.ViewportHeight) = (size.x, size.y);
        var carry = (Frag: "null", Local: JsonSerializer.Serialize((local ?? new()).Select(p => new[] { p.Key, p.Value })), Session: "[]", Now: 0.0);
        Jint.Engine Load()
        {
            var engine = new Jint.Engine();
            engine.SetValue("__exists", new Func<string, bool>(id => built.ById.ContainsKey(id)));
            engine.SetValue("__initialClass", new Func<string, string>(id => built.NodeOf[built.ById[id]].Attr("class") ?? ""));
            engine.SetValue("__initialText", new Func<string, string>(id => SourceText(page, id)));
            engine.SetValue("__initialAttrs", new Func<string, string>(id => JsonSerializer.Serialize(SourceAttributes(page, id))));
            engine.SetValue("__chain", new Func<string, string[]>(id =>
            {
                var chain = new List<string>();
                for (var ve = built.ById[id]; ve != null; ve = ve.parent) if (!string.IsNullOrEmpty(ve.name)) chain.Add(ve.name);
                return chain.ToArray();
            }));
            engine.SetValue("__w", Math.Floor(size.x));
            engine.SetValue("__h", Math.Floor(size.y));
            engine.SetValue("__carryFrag", carry.Frag);
            engine.SetValue("__carryLocal", carry.Local);
            engine.SetValue("__carrySession", carry.Session);
            engine.SetValue("__carryNow", carry.Now);
            engine.Execute(Harness);
            engine.Execute(script);
            return engine;
        }
        var engine = Load();
        var now = 0.0;
        foreach (var (dt, click) in steps)
        {
            if (click != null) engine.Invoke("__click", click);
            else { now += dt * 1000; engine.Invoke("__advance", now); }
            if (engine.Evaluate("__reloadAsked").AsBoolean())
            {
                carry = (engine.Evaluate("JSON.stringify(__frag)").AsString(), engine.Evaluate("JSON.stringify(localStorage.__dump())").AsString(),
                         engine.Evaluate("JSON.stringify(sessionStorage.__dump())").AsString(), now);
                engine = Load();
            }
        }
        localAfter = JsonSerializer.Deserialize<string[][]>(engine.Evaluate("JSON.stringify(localStorage.__dump())").AsString())!
            .Select(p => new KeyValuePair<string, string>(p[0], p[1])).ToList();
        var final = JsonDocument.Parse(engine.Evaluate("JSON.stringify(__final())").AsString()).RootElement;

        var undo = new List<Action>();
        lock (PageCompiler.Gate)
        {
            try
            {
                foreach (var el in final.EnumerateObject())
                {
                    var ve = built.ById[el.Name];
                    var node = built.NodeOf[ve];
                    if (el.Value.TryGetProperty("text", out var text))
                    {
                        var label = (Label)ve;
                        var (was, children) = (label.text, new List<HtmlNode>(node.Children));
                        label.text = text.GetString()!;
                        node.Children.Clear();
                        node.Children.Add(new HtmlNode { Text = label.text, Parent = node });
                        undo.Add(() => { label.text = was; node.Children.Clear(); node.Children.AddRange(children); });
                    }
                    var style = node.Attr("style");
                    var cls = node.Attr("class") ?? "";
                    var display = ve.style.display;
                    var declarations = el.Value.GetProperty("style").EnumerateObject().Select(p => DomSlots.Dashed(p.Name) + ":" + p.Value.GetString()).ToList();
                    var className = el.Value.TryGetProperty("className", out var c) ? c.GetString()! : cls;
                    // the attributes as the script left them, and the browser's own [hidden] { display: none }
                    var attrsWere = new List<(string, string?)>();
                    var hidden = false;
                    if (el.Value.TryGetProperty("attrs", out var attrs))
                    {
                        var want = attrs.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
                        foreach (var name in node.Attributes.Keys.Where(k => k is not ("id" or "class" or "style")).Union(want.Keys).ToList())
                        {
                            attrsWere.Add((name, node.Attr(name)));
                            if (want.TryGetValue(name, out var v)) node.Attributes[name] = v; else node.Attributes.Remove(name);
                        }
                        hidden = want.ContainsKey("hidden");
                        ve.style.display = StyleKeyword.Null;
                    }
                    if (declarations.Count > 0) node.Attributes["style"] = (style ?? "") + ";" + string.Join(";", declarations);
                    if (declarations.Count > 0 || className != cls || attrsWere.Count > 0) built.Reclass(ve, className);
                    if (hidden) ve.style.display = DisplayStyle.None;
                    undo.Add(() =>
                    {
                        if (style == null) node.Attributes.Remove("style"); else node.Attributes["style"] = style;
                        foreach (var (name, v) in attrsWere) if (v == null) node.Attributes.Remove(name); else node.Attributes[name] = v;
                        built.Reclass(ve, cls);
                        ve.style.display = display;
                    });
                }
                panel.Layout(size.x, size.y);
                var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
                var template = PageCompiler.Emitted(built, panel, values);
                return PlainTranslator.Literal(template, values, new List<string>());
            }
            finally
            {
                for (var i = undo.Count - 1; i >= 0; i--) undo[i]();
                panel.Layout(size.x, size.y);
            }
        }
    }

    /// <summary>The element with this id in the page's source: its start tag and what is inside it, found by hand.</summary>
    private static (string Tag, string Inner) SourceElement(string page, string id)
    {
        var open = Regex.Match(page, "<([a-zA-Z][a-zA-Z0-9-]*)((?:\\s[^>]*)?\\sid=\"" + Regex.Escape(id) + "\"[^>]*)>");
        if (!open.Success) return ("", "");
        var tag = open.Groups[1].Value;
        var from = open.Index + open.Length;
        var depth = 1;
        var at = from;
        foreach (Match m in Regex.Matches(page.Substring(from), "</?" + tag + "\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            depth += m.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            if (depth == 0) { at = from + m.Index; break; }
        }
        return (open.Groups[2].Value, depth == 0 ? page.Substring(from, at - from) : "");
    }

    /// <summary>textContent as a browser has it: every character of text inside the element, tags stripped, entities decoded.</summary>
    private static string SourceText(string page, string id)
        => System.Net.WebUtility.HtmlDecode(Regex.Replace(SourceElement(page, id).Inner, "<[^>]*>", ""));

    /// <summary>The attributes of the element's start tag, lower-cased, but for id, class and style.</summary>
    private static Dictionary<string, string> SourceAttributes(string page, string id)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(SourceElement(page, id).Tag, "([^\\s=\"'>/]+)(?:\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+)))?"))
        {
            var name = m.Groups[1].Value.ToLowerInvariant();
            if (name is "id" or "class" or "style") continue;
            found[name] = System.Net.WebUtility.HtmlDecode(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);
        }
        if (Regex.Match(SourceElement(page, id).Tag, "\\sstyle=\"([^\"]*)\"") is { Success: true } st) found["\u0001style"] = st.Groups[1].Value;
        return found;
    }

    /// <summary>
    /// localStorage kept across a game restart: the page run once and its setting changed, then loaded again
    /// on the same chip, against a browser loading it again with the same storage.
    /// </summary>
    private static void Persisted(Action<bool, string> check)
    {
        var page = Page("", "<p id=\"mode\">-</p><p id=\"seen\">0</p><button id=\"b\">Manual</button>",
                        "const mode = document.getElementById('mode');" +
                        "mode.textContent = localStorage.getItem('mode') || 'auto';" +
                        "document.getElementById('seen').textContent = Number(localStorage.getItem('visits') || 0);" +
                        "localStorage.setItem('visits', Number(localStorage.getItem('visits') || 0) + 1);" +
                        "document.getElementById('b').addEventListener('click', () => { localStorage.setItem('mode', 'manual'); mode.textContent = localStorage.getItem('mode'); });");
        foreach (var console in Consoles)
        {
            var name = $"localStorage outlives a restart, as the chip's own store, on a {console.W:0}x{console.H:0} console";
            try
            {
                var (compiled, _) = Probe4.Headless(page, out var built, out var panel, out var size, console: console);
                if (!compiled.Plain || compiled.Lua == null || compiled.Structure == null) { check(false, $"plain [{name}]: does not compile plainly - {string.Join("; ", compiled.Warnings.Take(2))}"); continue; }
                var script = Regex.Match(page, "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
                var store = new Dictionary<string, string>(StringComparer.Ordinal);
                var first = Steps("b", 0.5);
                var log1 = Probe4.DrivePlain(compiled.Lua, first, out _, store);
                Oracle(built, panel, size, page, script, first, out var browser);
                // the restart: the same chunk loaded again, on the store the first run left
                var log2 = Probe4.DrivePlain(compiled.Lua, Steps(0.5), out var data, store);
                var mine = Render(compiled.Structure, data);
                var theirs = Oracle(built, panel, size, page, script, Steps(0.5), out _, browser);
                var failed = log1.Concat(log2).FirstOrDefault(l => l.StartsWith("FAILED", StringComparison.Ordinal));
                var diff = failed ?? Compare(mine, theirs);
                var namespaced = store.Keys.All(k => k.StartsWith("html.ls", StringComparison.Ordinal));
                check(diff == null && namespaced && store.ContainsKey("html.ls:mode"),
                      $"plain [{name}]: the second load shows what the first saved" + (diff == null ? "" : " - " + diff)
                      + (namespaced ? "" : " (a key outside the page's own: " + string.Join(", ", store.Keys) + ")"));
            }
            catch (Exception ex) { check(false, $"plain [{name}]: threw - {ex.Message.Split('\n')[0]}"); }
        }
    }

    /// <summary>A chip has no console: the compiled Lua names none, and the page still does everything else.</summary>
    private static void ConsoleGone(Action<bool, string> check)
    {
        var page = Browser.First(b => b.Feature.StartsWith("console", StringComparison.Ordinal)).Page;
        var compiled = Probe4.Headless(page).Compiled;
        var code = Regex.Replace(compiled.Lua ?? "", @"--[^\n]*|""(?:\\.|[^""\\])*""|\[(=*)\[[\s\S]*?\]\1\]", " ");
        check(compiled.Plain && !code.Contains("console", StringComparison.Ordinal),
              "plain [console]: every console call is gone from the compiled Lua" + (compiled.Plain ? "" : " (it did not compile plainly)"));
    }

    /// <summary>Null when the two scenes draw the same: line by line, numbers within half a pixel, text exact.</summary>
    private static string? Compare(string mine, string theirs)
    {
        static string Clean(string s) => s.Replace("<noparse>", "").Replace("</noparse>", "");
        var a = Clean(mine).Split('\n');
        var b = Clean(theirs).Split('\n');
        if (a.Length != b.Length) return $"{a.Length} lines against {b.Length}";
        var token = new Regex("\"(?:\\\\.|[^\"\\\\])*\"|[^ ]+");
        for (var i = 0; i < a.Length; i++)
        {
            var ta = token.Matches(a[i]).Select(m => m.Value).ToList();
            var tb = token.Matches(b[i]).Select(m => m.Value).ToList();
            // A left-aligned single line of text draws from its x whatever its box's width: a label's
            // box is laid out once, and a browser widens it with its text. Everything else still counts.
            if (ta.Count > 0 && ta[0] == "T" && !ta.Any(t => t.StartsWith("align=", StringComparison.Ordinal) || t.StartsWith("wrap=", StringComparison.Ordinal) || t.StartsWith("fit=", StringComparison.Ordinal)))
            {
                ta.RemoveAll(t => t.StartsWith("w=", StringComparison.Ordinal));
                tb.RemoveAll(t => t.StartsWith("w=", StringComparison.Ordinal));
            }
            var same = ta.Count == tb.Count;
            for (var k = 0; same && k < ta.Count; k++)
            {
                if (ta[k] == tb[k]) continue;
                var ka = ta[k].Split('=', 2);
                var kb = tb[k].Split('=', 2);
                same = ka.Length == 2 && kb.Length == 2 && ka[0] == kb[0]
                       && double.TryParse(ka[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                       && double.TryParse(kb[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                       && Math.Abs(x - y) <= 0.51;
            }
            if (!same) return $"line {i}: compiled `{a[i].Trim()}` / browser `{b[i].Trim()}`";
        }
        return null;
    }

    /// <summary>
    /// A DOM that keeps what the script leaves each element with. Timers run on a virtual clock in the
    /// order they fall due (equal times in the order they were set), each seeing the clock at its own due
    /// time; a click runs the listeners and then the onclick of the element and of each ancestor.
    /// </summary>
    private const string Harness = @"
var __els = {}, __state = {}, __listeners = {}, __onclick = {};
function __el(id) {
  if (__els[id]) return __els[id];
  if (!__exists(id)) return null;
  var st = __state[id] = { className: __initialClass(id), style: {} };
  function words() { return st.className.split(/\s+/).filter(function (w) { return w.length; }); }
  var cl = {
    add: function () { var w = words(); for (var i = 0; i < arguments.length; i++) if (w.indexOf(arguments[i]) < 0) w.push(arguments[i]); st.className = w.join(' '); st.classWritten = true; },
    remove: function () { var w = words(); for (var i = 0; i < arguments.length; i++) { var k = w.indexOf(arguments[i]); if (k >= 0) w.splice(k, 1); } st.className = w.join(' '); st.classWritten = true; },
    toggle: function (c, force) { var w = words(), has = w.indexOf(c) >= 0, want = force === undefined ? !has : !!force;
      if (want && !has) w.push(c); if (!want && has) w.splice(w.indexOf(c), 1); st.className = w.join(' '); st.classWritten = true; return want; }
  };
  cl.contains = function (c) { return words().indexOf(c) >= 0; };
  var own = JSON.parse(__initialAttrs(id)), inline = {};
  (own['\u0001style'] || '').split(';').forEach(function (d) { var i = d.indexOf(':'); if (i > 0) inline[d.slice(0, i).trim().toLowerCase()] = d.slice(i + 1).trim(); });
  delete own['\u0001style'];
  st.attrs = own;
  function attr(n) { st.attrWritten = true; return String(n).toLowerCase(); }
  var style = new Proxy({}, {
    set: function (t, k, v) { delete st.style[k]; st.style[k] = String(v); return true; },
    get: function (t, k) {
      if (typeof k !== 'string') return undefined;
      if (k in st.style) return __css(st.style[k]);
      var d = k.replace(/[A-Z]/g, function (c) { return '-' + c.toLowerCase(); });
      return d in inline ? __css(inline[d]) : '';
    }
  });
  function data(k) { return 'data-' + String(k).replace(/[A-Z]/g, function (c) { return '-' + c.toLowerCase(); }); }
  var dataset = new Proxy({}, {
    get: function (t, k) { var n = data(k); return n in st.attrs ? st.attrs[n] : undefined; },
    set: function (t, k, v) { st.attrs[attr(data(k))] = String(v); return true; },
    deleteProperty: function (t, k) { delete st.attrs[attr(data(k))]; return true; },
    has: function (t, k) { return data(k) in st.attrs; }
  });
  var e = {
    id: id, classList: cl, style: style, dataset: dataset,
    getAttribute: function (n) { n = String(n).toLowerCase(); return n in st.attrs ? st.attrs[n] : null; },
    hasAttribute: function (n) { return String(n).toLowerCase() in st.attrs; },
    setAttribute: function (n, v) { st.attrs[attr(n)] = String(v); },
    removeAttribute: function (n) { delete st.attrs[attr(n)]; },
    toggleAttribute: function (n, force) { n = attr(n); var has = n in st.attrs, want = force === undefined ? !has : !!force;
      if (want && !has) st.attrs[n] = ''; if (!want && has) delete st.attrs[n]; return want; },
    get hidden() { return 'hidden' in st.attrs; }, set hidden(v) { if (v) st.attrs[attr('hidden')] = ''; else delete st.attrs[attr('hidden')]; },
    get textContent() { return st.text !== undefined ? st.text : __initialText(id); }, set textContent(v) { st.text = String(v); },
    get innerText() { return st.text; }, set innerText(v) { st.text = String(v); },
    get className() { return st.className; }, set className(v) { st.className = String(v); st.classWritten = true; },
    get onclick() { return __onclick[id] || null; }, set onclick(f) { __onclick[id] = f; },
    addEventListener: function (type, fn) { if (type !== 'click') return; var l = __listeners[id] || (__listeners[id] = []); if (l.indexOf(fn) < 0) l.push(fn); }
  };
  __els[id] = e;
  return e;
}
var document = { getElementById: __el };
var console = new Proxy({}, { get: function () { return function () {}; } });
// The window: a console is one, of the size the page is compiled for, at one canvas unit to a CSS pixel.
var window = globalThis, innerWidth = __w, innerHeight = __h, devicePixelRatio = 1;
function __css(v) {
  v = String(v).trim();
  var m = /^#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})$/i.exec(v);
  if (m) {
    var h = m[1];
    if (h.length <= 4) h = h.split('').map(function (c) { return c + c; }).join('');
    var b = []; for (var i = 0; i < h.length; i += 2) b.push(parseInt(h.substr(i, 2), 16));
    if (b.length === 3) return 'rgb(' + b.join(', ') + ')';
    var a = Math.round(b[3] / 255 * 100) / 100; if (Math.round(a * 255) !== b[3]) a = Math.round(b[3] / 255 * 1000) / 1000;
    return 'rgba(' + b.slice(0, 3).join(', ') + ', ' + a + ')';
  }
  m = /^([-+]?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?)([a-z%]*)$/i.exec(v);
  if (m) return String(parseFloat(Number(m[1]).toPrecision(6))) + m[2].toLowerCase();
  return /^[a-z-]+$/i.test(v) ? v.toLowerCase() : v;
}
// A page loaded from no URL: about:blank, and a fragment once the page navigates to one of its own.
var __frag = JSON.parse(__carryFrag), __reloadAsked = false;
function __pct(f) { return f.replace(/[\u0000-\u001f\u007f ""<>`]/g, function (c) { var x = c.charCodeAt(0).toString(16).toUpperCase(); return '%' + (x.length < 2 ? '0' : '') + x; }); }
function __nav(u) { u = String(u); if (u.charAt(0) !== '#') throw new Error('navigates away'); location.hash = u; }
var location = {
  get href() { return 'about:blank' + (__frag === null ? '' : '#' + __frag); }, set href(u) { __nav(u); },
  protocol: 'about:', host: '', hostname: '', port: '', pathname: 'blank', search: '', origin: 'null',
  get hash() { return __frag ? '#' + __frag : ''; },
  set hash(v) { v = String(v); if (v.charAt(0) === '#') v = v.slice(1); __frag = __pct(v); },
  assign: function (u) { __nav(u); }, replace: function (u) { __nav(u); }, reload: function () { __reloadAsked = true; },
  ancestorOrigins: { length: 0, item: function () { return null; }, contains: function () { return false; } },
  toString: function () { return this.href; }
};
function __Storage(init) {
  var keys = [], vals = {};
  init.forEach(function (p) { keys.push(p[0]); vals[p[0]] = p[1]; });
  return {
    getItem: function (k) { k = String(k); return keys.indexOf(k) >= 0 ? vals[k] : null; },
    setItem: function (k, v) { k = String(k); if (keys.indexOf(k) < 0) keys.push(k); vals[k] = String(v); },
    removeItem: function (k) { k = String(k); var i = keys.indexOf(k); if (i >= 0) { keys.splice(i, 1); delete vals[k]; } },
    clear: function () { keys = []; vals = {}; },
    key: function (i) { i = Math.floor(Number(i)) || 0; return i >= 0 && i < keys.length ? keys[i] : null; },
    get length() { return keys.length; },
    __dump: function () { return keys.map(function (k) { return [k, vals[k]]; }); }
  };
}
var localStorage = __Storage(JSON.parse(__carryLocal)), sessionStorage = __Storage(JSON.parse(__carrySession));
var __now = __carryNow, __timers = [], __last = 0;
function __add(fn, ms, every) { ms = Number(ms) || 0; if (ms < 0) ms = 0; if (every && ms < 4) ms = 4; __last++; __timers.push({ id: __last, due: __now + ms, every: every ? ms : 0, fn: fn }); return __last; }
function setTimeout(fn, ms) { return __add(fn, ms, false); }
function setInterval(fn, ms) { return __add(fn, ms, true); }
function clearTimeout(h) { __timers = __timers.filter(function (t) { return t.id !== h; }); }
var clearInterval = clearTimeout;
function __advance(to) {
  for (;;) {
    var best = null;
    for (var i = 0; i < __timers.length; i++) { var t = __timers[i]; if (t.due <= to && (!best || t.due < best.due || t.due === best.due && t.id < best.id)) best = t; }
    if (!best) break;
    __now = best.due;
    if (best.every) best.due += best.every; else __timers.splice(__timers.indexOf(best), 1);
    best.fn();
    if (__reloadAsked) break;
  }
  __now = to;
}
function __click(id) {
  var chain = __chain(id);
  for (var c = 0; c < chain.length; c++) {
    var l = (__listeners[chain[c]] || []).slice();
    for (var i = 0; i < l.length; i++) l[i]();
    if (__onclick[chain[c]]) __onclick[chain[c]]();
  }
}
function __final() {
  var out = {};
  for (var id in __state) {
    var st = __state[id], o = { style: st.style };
    if (st.text !== undefined) o.text = st.text;
    if (st.classWritten) o.className = st.className;
    if (st.attrWritten) o.attrs = st.attrs;
    out[id] = o;
  }
  return out;
}
";
}

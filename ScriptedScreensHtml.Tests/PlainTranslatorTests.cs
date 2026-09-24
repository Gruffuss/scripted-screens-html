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

        ("hidden under a display rule of the page's own: the rule wins, as it does over a browser's [hidden] { display: none }",
         Page(".row { display: flex; height: 24px; background: #345; } .plain { height: 20px; background: #a33; } #tag { display: block; color: #fc6; }",
              "<div id=\"row\" class=\"row\" hidden>row</div><div id=\"gone\" class=\"plain\" hidden>gone</div><p id=\"tag\" hidden>tag</p><p id=\"state\">-</p><button id=\"t\">Toggle</button>",
              "document.getElementById('t').addEventListener('click', () => {" +
              "  const row = document.getElementById('row'), gone = document.getElementById('gone');" +
              "  row.hidden = !row.hidden;" +
              "  gone.hidden = !gone.hidden;" +
              "  document.getElementById('state').textContent = row.hidden + ' ' + gone.hidden;" +
              "});"),
         Steps("t", "t"), new[] { 0, 1, 2 }),

        ("onclick attributes and this: an attribute's code with this and event, one handing this to a function, one on an element with no id, " +
         "a listener's this on two elements, an onclick property's this, an attribute's handler running before a listener the script adds, and one the script replaces",
         Page(".box { width: 120px; height: 30px; background: #333; margin-bottom: 4px; } .box.on { background: #2a6; }",
              "<div id=\"a\" class=\"box\" onclick=\"this.classList.toggle('on'); count(event.currentTarget === this)\">A 0</div>" +
              "<div id=\"b\" class=\"box\" onclick=\"hit(this)\">B 0</div>" +
              "<div class=\"box\" onclick=\"plain++; document.getElementById('log').textContent = 'plain ' + plain\">C</div>" +
              "<div id=\"c\" class=\"box\">c 0</div><div id=\"d\" class=\"box\">d 0</div><div id=\"e\" class=\"box\">e 0</div>" +
              "<div id=\"o\" class=\"box\" onclick=\"order += 'h'\">O</div><div id=\"r\" class=\"box\" onclick=\"document.getElementById('log').textContent = 'attribute'\">R</div>" +
              "<p id=\"log\">none</p><p id=\"order\">-</p>",
              "let n = 0, plain = 0, order = '';" +
              "function count(same) { n++; document.getElementById('a').textContent = 'A ' + n + ' ' + same; }" +
              "function hit(el) { n++; el.textContent = 'B ' + n; }" +
              "[document.getElementById('c'), document.getElementById('d')].forEach(function (el) {" +
              "  el.addEventListener('click', function () { n++; this.classList.toggle('on'); this.textContent = this.id + ' ' + n; });" +
              "});" +
              "document.getElementById('e').onclick = function () { this.textContent = 'e ' + (n > 3); };" +
              "document.getElementById('o').addEventListener('click', function () { order += 'l'; document.getElementById('order').textContent = order; });" +
              "document.getElementById('r').onclick = function () { document.getElementById('log').textContent = 'replaced ' + (this === document.getElementById('r')); };"),
         Steps("a", "b", "div1", "c", "d", "c", "e", "o", "o", "a", "r"), new[] { 1, 2, 3, 6, 7, 9, 10, 11 }),

        ("an onclick attribute on the body, and this there",
         Head + "#n { font-size: 20px; }</style></head><body onclick=\"hits++; document.getElementById('n').textContent = this.tagName + ' hits ' + hits\"><p id=\"n\">hits 0</p>" +
         "<script>var hits = 0;</script></body></html>",
         Steps("body", "body"), new[] { 1, 2 }),

        ("a page whose only code is in onclick attributes, with no script: classes added, removed and toggled on this, text written",
         Head + ".lamp { width: 40px; height: 40px; background: #522; } .lamp.lit { background: #2e5; } .box { width: 160px; height: 40px; background: #234; } .box.sel { background: #fa0; color: #111; }" +
         "</style></head><body><div id=\"lamp\" class=\"lamp\"></div><p id=\"state\">OFF</p>" +
         "<button id=\"on\" onclick=\"document.getElementById('lamp').classList.add('lit'); document.getElementById('state').textContent = 'ON'\">On</button>" +
         "<button id=\"off\" onclick=\"document.getElementById('lamp').classList.remove('lit'); document.getElementById('state').textContent = 'OFF'\">Off</button>" +
         "<div id=\"box\" class=\"box\" onclick=\"this.classList.toggle('sel'); document.getElementById('pick').textContent = this.classList.contains('sel') ? 'box: selected' : 'box: none'\">Toggle me</div>" +
         "<p id=\"pick\">box: none</p></body></html>",
         Steps("on", "box", "off", "box", "on"), new[] { 0, 1, 2, 3, 4, 5 }),

        ("booleans and undefined written into text: every(), includes(), a comparison, a boolean held in a name, toString(), valueOf() and String() of one, a variable never set",
         Page("", "<p id=\"flag\">flag ?</p><p id=\"all\">all on: ?</p><p id=\"has\">has 3: ?</p><p id=\"big\">?</p><p id=\"never\">?</p>",
              "var n = 0; var on = [1, 2]; var unset;" +
              "var t = setInterval(function () {" +
              "  n++;" +
              "  const big = n > 1;" +
              "  document.getElementById('flag').textContent = 'flag ' + (n > 2);" +
              "  document.getElementById('all').textContent = 'all on: ' + on.every(function (x) { return x > n - 2; });" +
              "  document.getElementById('has').textContent = 'has 3: ' + on.includes(n);" +
              "  document.getElementById('big').textContent = big + ' ' + big.toString() + ' ' + on.includes(1).toString() + ' ' + String(n > 2) + ' ' + big.valueOf();" +
              "  document.getElementById('never').textContent = 'never ' + unset;" +
              "  if (n >= 3) clearInterval(t);" +
              "}, 500);"),
         Ticks(4), new[] { 1, 2, 4 }),

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

    /// <summary>
    /// Group 3: elements found by selector, lists of them, an element chosen at run time, values from a
    /// function's returned object, innerText and compound text writes, classList's reads - and a JavaScript
    /// method on a literal array and null in text. Each compiled for every console shape.
    /// </summary>
    private static readonly (string Feature, string Page, List<(double, string?)> Steps, int[] Checkpoints)[] Lookups =
    {
        ("querySelector and querySelectorAll: a list's length, item(), an index and forEach",
         Page(".row { color: #999; height: 22px; background: #111; } .row.lit { color: #fe4; background: #333; }",
              "<p id=\"count\">-</p><div class=\"row\" id=\"r0\">a</div><div class=\"row\" id=\"r1\">b</div><div class=\"row\" id=\"r2\">c</div><button id=\"next\" class=\"go\">Next</button>",
              "const rows = document.querySelectorAll('.row');" +
              "const count = document.querySelector('#count');" +
              "count.textContent = rows.length + ' rows';" +
              "let k = 0;" +
              "document.querySelector('button.go').addEventListener('click', () => {" +
              "  rows.forEach((row, i) => row.classList.toggle('lit', i === k % rows.length));" +
              "  document.querySelectorAll('.none').forEach((e) => { e.textContent = 'never'; });" +
              "  rows[k % rows.length].textContent = 'row ' + k;" +
              "  rows.item((k + 1) % rows.length).textContent = 'next';" +
              "  k++;" +
              "  let lit = 0;" +
              "  for (const r of rows) if (r.classList.contains('lit')) lit++;" +
              "  count.textContent = k + ' of ' + rows.length + ', lit ' + lit;" +
              "});"),
         Steps("next", "next", "next", "next"), new[] { 0, 1, 2, 4 }),

        ("getElementsByClassName, getElementsByTagName and Element.querySelector, walked by for...of and an index",
         Page("#panel { padding: 4px; background: #222; } .v { height: 20px; }",
              "<div id=\"panel\"><h3 class=\"title\">Panel</h3><div class=\"v\" id=\"v1\">0</div><div class=\"v\" id=\"v2\">0</div></div><p class=\"note\" id=\"n1\">x</p><p class=\"note\" id=\"n2\">y</p>",
              "const panel = document.getElementById('panel');" +
              "const values = panel.getElementsByClassName('v');" +
              "const title = panel.querySelector('.title');" +
              "const heads = panel.getElementsByTagName('h3');" +
              "const boxes = panel.querySelectorAll('div');" +
              "const notes = document.getElementsByTagName('p');" +
              "let n = 0;" +
              "setInterval(() => {" +
              "  n++;" +
              "  for (const v of values) v.textContent = n * 10;" +
              "  title.textContent = 'Panel ' + n + ' of ' + values.length + '/' + boxes.length + ' ' + values.item(1).textContent;" +
              "  heads[0].style.opacity = n % 2 ? 0.5 : 1;" +
              "  for (let i = 0; i < notes.length; i++) notes[i].style.opacity = i === n % 2 ? 1 : 0.4;" +
              "}, 500);"),
         Ticks(4), new[] { 0, 1, 2, 4 }),

        ("an element chosen at run time: an id built from a loop counter, an array of elements, an element passed to a function",
         Page(".led { width: 20px; height: 20px; background: #333; margin: 2px; } .led.on { background: #3e5; }",
              "<div class=\"led\" id=\"led0\"></div><div class=\"led\" id=\"led1\"></div><div class=\"led\" id=\"led2\"></div><div class=\"led\" id=\"led3\"></div>" +
              "<p id=\"a\">-</p><p id=\"b\">-</p><p id=\"cell0\">0</p><p id=\"cell1\">0</p><div id=\"cells\"><p>total</p></div><button id=\"go\">Go</button>",
              "const labels = [document.getElementById('a'), document.getElementById('b')];" +
              "function paint(el, text, colour) { el.textContent = text; el.style.color = colour; }" +
              "let level = 0;" +
              "document.getElementById('go').addEventListener('click', () => {" +
              "  level = (level + 1) % 5;" +
              "  for (let i = 0; i < 4; i++) document.getElementById('led' + i).classList.toggle('on', i < level);" +
              "  for (let i = 0; i < 2; i++) document.getElementById(`cell${i}`).textContent = level * 10 + i;" +
              "  ['cell0', 'cell1'].forEach((id, k) => { document.getElementById(id).style.color = k === level % 2 ? '#fe4' : '#999'; });" +
              "  paint(labels[level % 2], 'level ' + level, level > 2 ? '#f55' : '#5f5');" +
              "  if (level === 4) paint(document.getElementById('b'), 'b' + level, '#ccc');" +
              "  if (level === 3) (level % 2 ? labels[0] : labels[1]).style.color = '#ff0';" +
              "});"),
         Steps("go", "go", "go", "go", "go", "go"), new[] { 1, 2, 4, 6 }),

        ("values from a fixed set through a function's returned object: an attribute CSS selects on and a text",
         Page(".panel { padding: 6px; background: #eee; color: #222; } .panel[data-theme=\"dim\"] { background: #222; } .panel[data-theme=\"dim\"] p { color: #ccc; } .base.dark { font-size: 20px; }",
              "<div id=\"panel\" class=\"panel\" data-theme=\"lit\"><p id=\"t\">lit</p></div><button id=\"b\">Dim</button>",
              "let dim = false;" +
              "function values() { return { theme: dim ? 'dim' : 'lit', label: dim ? 'Dim' : 'Lit' }; }" +
              "function render() {" +
              "  const v = values();" +
              "  document.getElementById('panel').setAttribute('data-theme', v.theme);" +
              "  document.getElementById('t').textContent = v.label;" +
              "  const cls = (dim ? 'dark' : '') + ' base';" +
              "  document.getElementById('t').className = cls.trim();" +
              "}" +
              "document.getElementById('b').addEventListener('click', () => { dim = !dim; render(); });" +
              "render();"),
         Steps("b", "b", "b"), new[] { 0, 1, 2, 3 }),

        ("innerText read and written, textContent +=, and classList's item(), length and value",
         Page("#box { height: 10px; background: #444; } #box.y { width: 50px; } #tag { height: 8px; width: 20px; background: #a44; } #tag.t2 { width: 60px; }",
              "<p id=\"log\">Log:</p><p id=\"up\" style=\"text-transform: uppercase\">  Mixed   case  </p><p id=\"cls\">-</p><div id=\"box\" class=\"x y z\"></div><div id=\"tag\" class=\"t1\"></div><button id=\"b\">B</button>",
              "const log = document.getElementById('log');" +
              "const box = document.getElementById('box');" +
              "document.getElementById('cls').innerText = document.getElementById('up').innerText + '|' + box.classList.length + ' ' + box.classList.item(1) + ' ' + box.classList.item(5) + ' ' + box.classList.value;" +
              "let n = 0;" +
              "document.getElementById('b').addEventListener('click', () => {" +
              "  n++;" +
              "  document.getElementById('tag').classList.value = n % 2 ? 't1 t2' : 't1';" +
              "  log.textContent += ' .';" +
              "  box.classList.toggle('y');" +
              "  document.getElementById('cls').innerText = box.classList.length + ' ' + box.classList.item(1) + ' ' + box.classList.value + ' ' + log.innerText.length;" +
              "});"),
         Steps("b", "b", "b"), new[] { 0, 1, 2, 3 }),

        ("a method called on a literal array, and null as text in a larger expression, a template and String()",
         Page("", "<p id=\"out\">-</p><p id=\"sum\">-</p><div id=\"d\" data-k=\"v\"></div>",
              "const out = document.getElementById('out');" +
              "let total = 0;" +
              "[1, 2, 3].forEach((n) => { total += n; });" +
              "const d = document.getElementById('d');" +
              "const text = 'x' + d.getAttribute('missing') + '/' + `${d.getAttribute('data-k')}-${d.getAttribute('nope')}` + String(d.getAttribute('none'));" +
              "out.textContent = text + ' ' + ['a', 'b'].map((s) => s + '!').join('') + ' ' + ('n' + null) + `${null}`;" +
              "document.getElementById('sum').textContent = total + (d.getAttribute('nothing') + 1);"),
         Steps(0.5), new[] { 0, 1 }),
    };

    /// <summary>
    /// innerHTML, part 1: markup built from literals, concatenation, templates, ternaries and helper functions,
    /// written into an element the script names. Each compiled for every console shape.
    /// </summary>
    private static readonly (string Feature, string Page, List<(double, string?)> Steps, int[] Checkpoints)[] Markups =
    {
        ("innerHTML as rich text: a value in bold, a word shown by a ternary, a colour from a fixed set",
         Page("", "<p id=\"status\">loading</p><button id=\"b\">More</button>",
              "let n = 0;" +
              "function show() {" +
              "  const colour = n > 1 ? '#fa0' : '#8c8';" +
              "  document.getElementById('status').innerHTML = 'Pressure <b style=\"color:' + colour + '\">' + (n * 12.5).toFixed(1) + '</b> kPa' + (n > 2 ? ' <i>HIGH</i>' : '');" +
              "}" +
              "show();" +
              "document.getElementById('b').addEventListener('click', () => { n++; show(); });"),
         Steps("b", "b", "b", "b"), new[] { 0, 1, 3, 4 }),

        ("innerHTML of elements from a helper function: text, a class and a style width from values",
         Page(".panel { width: 260px; background: #222; padding: 4px; } .row { padding: 2px 4px; background: #1a1a1a; } .row.warn { color: #f55; background: #311; }" +
              ".bar { width: 200px; height: 8px; background: #333; margin-top: 4px; } .fill { height: 8px; background: #3a7; }",
              "<div id=\"panel\" class=\"panel\"></div><button id=\"up\">Up</button>",
              "let p = 95;" +
              "function row(label, value, unit) {" +
              "  unit = unit || 'kPa';" +
              "  const warn = value > 100;" +
              "  return '<div class=\"row' + (warn ? ' warn' : '') + '\"><span>' + label + '</span> <b>' + value.toFixed(1) + '</b> ' + unit + '</div>';" +
              "}" +
              "function render() {" +
              "  document.getElementById('panel').innerHTML =" +
              "    '<h3>Pressures</h3>' + row('Room', p) + row('Tank', p / 2, 'bar') +" +
              "    `<div class=\"bar\"><div class=\"fill\" style=\"width:${Math.min(100, p)}%\"></div></div>`;" +
              "}" +
              "render();" +
              "document.getElementById('up').addEventListener('click', () => { p += 3; render(); });"),
         Steps("up", "up", "up"), new[] { 0, 1, 2, 3 }),

        ("innerHTML choosing between two shapes, with listeners on the buttons each one makes",
         Page(".card { background: #234; padding: 6px; } .big { font-size: 22px; } .hint { color: #888; }",
              "<div id=\"box\"></div><p id=\"log\">-</p>",
              "let open = false, count = 0;" +
              "function render() {" +
              "  document.getElementById('box').innerHTML = open" +
              "    ? '<div class=\"card\"><p class=\"big\">Open ' + count + '</p><button id=\"close\">Close</button></div>'" +
              "    : '<p class=\"hint\">Closed</p><button id=\"open\">Open</button>';" +
              "  if (open) document.getElementById('close').addEventListener('click', () => { open = false; render(); });" +
              "  else document.getElementById('open').addEventListener('click', () => { open = true; count++; render(); document.getElementById('log').textContent = 'opened ' + count; });" +
              "}" +
              "render();"),
         Steps("open", "close", "open", "close", "open"), new[] { 0, 1, 2, 3, 5 }),

        ("innerHTML written by a timer, the list as the page wrote it shown until then",
         Page(".item { color: #ccc; padding: 2px; } .item.ok { color: #3e5; }",
              "<div id=\"list\"><div class=\"item\">waiting</div></div><p id=\"after\">below</p>",
              "let n = 0;" +
              "setInterval(() => {" +
              "  n++;" +
              "  document.getElementById('list').innerHTML = `<div class=\"item ok\">${n} ready</div>` + (n > 1 ? '<div class=\"item\">second</div>' : '');" +
              "}, 1000);"),
         Ticks(6), new[] { 0, 1, 2, 4, 6 }),
    };

    /// <summary>
    /// innerHTML, part 2: markup repeated over a list - .map(…).join(…), a loop adding markup to a name or to the
    /// element - laid out at the most rows the list can have. Author-style pages, each compiled for every console shape.
    /// </summary>
    private static readonly (string Feature, string Page, List<(double, string?)> Steps, int[] Checkpoints)[] Lists =
    {
        ("a todo list from .map().join(''): template literals, the index in the markup, a class from each item, a listener per row found by [data-act], a push held to a cap, filter and splice; the rows found again by a class every row keeps",
         Page(".list { width: 300px; margin: 0; padding: 0; } .item { padding: 4px 8px; background: #1b2230; margin-bottom: 4px; list-style: none; }" +
              ".item.done { color: #6b7; background: #132; }",
              "<ul id=\"list\" class=\"list\"></ul><p id=\"count\">-</p><button id=\"add\">Add</button><button id=\"clear\">Clear done</button><button id=\"drop\">Drop first</button>",
              "let todos = [{ text: 'Check O2', done: false }, { text: 'Refill tank', done: true }];" +
              "const ideas = ['Fix airlock', 'Charge batteries', 'Sort ore'];" +
              "let next = 0;" +
              "function render() {" +
              "  document.getElementById('list').innerHTML = todos.map((t, i) => `<li id=\"todo${i}\" data-act=\"${i}\" class=\"item${t.done ? ' done' : ''}\">${i + 1}. ${t.text}</li>`).join('');" +
              "  const rows = document.querySelectorAll('#list [data-act]');" +
              "  document.getElementById('count').textContent = rows.length + ' items (' + todos.map((t) => t.done ? 'x' : 'o').join('') + ') ' + document.querySelectorAll('#list .item').length;" +
              "  rows.forEach((li) => li.addEventListener('click', () => { const k = Number(li.dataset.act); todos[k].done = !todos[k].done; render(); }));" +
              "}" +
              "document.getElementById('add').addEventListener('click', () => { if (todos.length < 5) { todos.push({ text: ideas[next % 3], done: false }); next++; } render(); });" +
              "document.getElementById('clear').addEventListener('click', () => { todos = todos.filter((t) => !t.done); render(); });" +
              "document.getElementById('drop').addEventListener('click', () => { todos.splice(0, 1); render(); });" +
              "render();"),
         Steps("add", "todo0", "add", "add", "todo3", "add", "todo4", "clear", "drop", "add", "add", "todo1"), new[] { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10, 12 }),

        ("a table of readings built in a for loop with +=: a constant per row, a colour from each value, text from a fixed array",
         Page(".row { display: flex; width: 300px; padding: 3px 6px; } .name { width: 140px; } .val { width: 130px; } .head { color: #888; }",
              "<div id=\"table\"></div><p>below the table</p>",
              "const sensors = [{ name: 'Room', unit: 'kPa' }, { name: 'Tank', unit: 'kPa' }, { name: 'Pipe', unit: 'C' }];" +
              "const base = [101.3, 88.4, 21.5];" +
              "let tick = 0;" +
              "function render() {" +
              "  let html = '<div class=\"row head\"><span class=\"name\">Sensor</span><span class=\"val\">Value</span></div>';" +
              "  for (let i = 0; i < sensors.length; i++) {" +
              "    const v = base[i] + tick * (i + 1) * 1.5;" +
              "    html += '<div class=\"row\"><span class=\"name\">' + sensors[i].name + '</span><span class=\"val\" style=\"color:' + (v > 100 ? '#f66' : '#6c6') + '\">' + v.toFixed(1) + ' ' + sensors[i].unit + '</span></div>';" +
              "  }" +
              "  document.getElementById('table').innerHTML = html;" +
              "}" +
              "setInterval(() => { tick++; render(); }, 1000);" +
              "render();"),
         Ticks(8), new[] { 0, 2, 4, 8 }),

        ("a log that grows to a cap, newest first, with a message while it is empty and a paragraph after it that moves",
         Page(".line { padding: 2px 6px; background: #1a2130; margin-bottom: 2px; width: 280px; } .empty { color: #777; }",
              "<div id=\"log\"></div><p id=\"after\">end of log</p>",
              "const log = [];" +
              "let n = 0;" +
              "function show() {" +
              "  document.getElementById('log').innerHTML = log.length" +
              "    ? log.map((e) => '<div class=\"line\">' + e + '</div>').join('')" +
              "    : '<p class=\"empty\">No events yet</p>';" +
              "}" +
              "show();" +
              "setInterval(() => {" +
              "  n++;" +
              "  log.unshift('event ' + n + ' at ' + (n * 0.5).toFixed(1) + ' s');" +
              "  if (log.length > 4) log.pop();" +
              "  show();" +
              "}, 500);"),
         Ticks(7), new[] { 0, 1, 2, 4, 5, 7 }),

        ("rows whose markup depends on their item, after a filter: each row its own shape, the rows after a filtered one moving up",
         Page(".dev { display: flex; width: 260px; padding: 3px 6px; background: #1b2230; margin-bottom: 3px; } .name { width: 150px; }" +
              ".tag { width: 80px; color: #6c6; } .alarm .tag { color: #f55; } .alarm { background: #311; }",
              "<div id=\"devs\"></div><p>devices end</p>",
              "const devices = [" +
              "  { name: 'Pump', on: true, alarm: false }, { name: 'Fan', on: true, alarm: true }," +
              "  { name: 'Heater', on: false, alarm: false }, { name: 'Vent', on: true, alarm: false }];" +
              "let t = 0;" +
              "function draw() {" +
              "  document.getElementById('devs').innerHTML = devices.filter((d) => d.on).map((d) => d.alarm" +
              "    ? `<div class=\"dev alarm\"><span class=\"name\">${d.name}</span><span class=\"tag\">ALARM</span></div>`" +
              "    : `<div class=\"dev\"><span class=\"name\">${d.name}</span><span class=\"tag\">ok</span></div>`).join('');" +
              "}" +
              "draw();" +
              "setInterval(() => { t++; devices[t % 4].alarm = !devices[t % 4].alarm; devices[(t + 1) % 4].on = !devices[(t + 1) % 4].on; draw(); }, 500);"),
         Ticks(6), new[] { 0, 1, 2, 3, 6 }),

        ("markup added to the element with += in a for...of, a string built in a forEach with a class from the index, and a slice of a list that is never held to a length",
         Page("#steps { display: flex; } .step { padding: 2px 8px; background: #223; margin-right: 4px; } #dots { display: flex; }" +
              ".dot { width: 16px; height: 16px; background: #333; margin-right: 4px; } .dot.done { background: #3a7; } .h { color: #aaa; }",
              "<div id=\"steps\"></div><div id=\"dots\"></div><div id=\"hist\"></div><p id=\"recent\">-</p><button id=\"next\">Next</button>",
              "const steps = ['Seal', 'Pump', 'Open'];" +
              "const seen = [];" +
              "let at = 0;" +
              "function show() {" +
              "  const box = document.getElementById('steps');" +
              "  box.innerHTML = '';" +
              "  for (const s of steps) box.innerHTML += '<span class=\"step\">' + s + '</span>';" +
              "  let h = '';" +
              "  steps.forEach((s, i) => { h += '<div class=\"dot' + (i < at ? ' done' : '') + '\"></div>'; });" +
              "  document.getElementById('dots').innerHTML = h;" +
              "  document.getElementById('hist').innerHTML = seen.slice(0, 3).map(histRow).join('');" +
              "  document.getElementById('recent').textContent = 'recent [' + seen.slice(0, 2).join(',') + ']';" +
              "}" +
              "function histRow(x, i) { return '<p class=\"h\">step ' + x + (i === 0 ? ' (last)' : '') + '</p>'; }" +
              "document.getElementById('next').addEventListener('click', () => { at = at < 3 ? at + 1 : 0; seen.unshift(at); show(); });" +
              "show();"),
         Steps("next", "next", "next", "next", "next"), new[] { 0, 1, 2, 4, 5 }),

        ("one listener on a list for every row: e.target, closest() and dataset pick the row a click lands in, the rows rewritten each time",
         Page(".menu { width: 280px; margin: 0; padding: 4px; background: #151a24; } .menu li { list-style: none; padding: 4px 8px; margin-bottom: 3px; background: #1d2433; }" +
              ".menu li.on { background: #2b4a7a; } .lbl { color: #cde; } .n { color: #789; margin-left: 12px; }",
              "<ul id=\"menu\" class=\"menu\"></ul><p id=\"picked\">none</p>",
              "const gases = [{ key: 'o2', label: 'Oxygen' }, { key: 'n2', label: 'Nitrogen' }, { key: 'co2', label: 'Carbon dioxide' }];" +
              "let chosen = '';" +
              "let picks = 0;" +
              "function draw() {" +
              "  document.getElementById('menu').innerHTML = gases.map((g, i) =>" +
              "    `<li id=\"m${i}\" data-key=\"${g.key}\" class=\"${g.key === chosen ? 'on' : ''}\"><span id=\"l${i}\" class=\"lbl\">${g.label}</span><span class=\"n\">#${i + 1}</span></li>`).join('');" +
              "}" +
              "document.getElementById('menu').addEventListener('click', (e) => {" +
              "  const li = e.target.closest('li');" +
              "  if (!li) return;" +
              "  chosen = li.dataset.key;" +
              "  picks++;" +
              "  document.getElementById('picked').textContent = 'picked ' + chosen + ' (' + e.target.tagName + ' ' + e.target.id + (e.target.matches('.lbl') ? ' label' : '') + ' in ' + e.currentTarget.id + ', ' + picks + ')';" +
              "  draw();" +
              "});" +
              "draw();"),
         Steps("l1", "m2", "l0", "menu", "m0"), new[] { 0, 1, 2, 3, 4, 5 }),

        ("a list inside a box that shows only while the list has rows, built by a helper that adds markup in steps; rows kept by a ternary that reads the index",
         Page(".box { background: #1a1a2a; padding: 4px; width: 260px; } .al { color: #f77; margin: 2px 0; } .ok { color: #7c7; } .odd { color: #fc6; margin: 1px 0; }",
              "<div id=\"al\"></div><div id=\"sk\"></div><p>end</p>",
              "const alarms = [];" +
              "const names = ['a', 'b', 'c', 'd'];" +
              "let t = 0;" +
              "function lines(list) {" +
              "  let h = '';" +
              "  for (const a of list) h += `<p class=\"al\">${a}</p>`;" +
              "  return h;" +
              "}" +
              "function show() {" +
              "  document.getElementById('al').innerHTML = alarms.length" +
              "    ? '<div class=\"box\"><b>Alarms</b>' + lines(alarms) + '</div>'" +
              "    : '<p class=\"ok\">all clear</p>';" +
              "  document.getElementById('sk').innerHTML = names.map((x, i) => i % 3 === t % 3 ? '' : `<p class=\"odd\">${i} ${x}</p>`).join('');" +
              "}" +
              "show();" +
              "setInterval(() => {" +
              "  t++;" +
              "  if (t % 4 !== 0) { alarms.push('alarm ' + t); if (alarms.length > 3) alarms.shift(); }" +
              "  else alarms.length = 0;" +
              "  show();" +
              "}, 500);"),
         Ticks(9), new[] { 0, 1, 2, 3, 4, 5, 7, 9 }),

        ("a list read from a field of an object, and a helper's list given as an argument or left to its default with ||",
         Page(".it { margin: 1px 0; } #m { display: flex; } .mk { width: 30px; margin-right: 4px; background: #234; }",
              "<div id=\"f\"></div><div id=\"m\"></div><p>after</p>",
              "const panel = { title: 'Pumps', items: ['P1', 'P2', 'P3'] };" +
              "function marks(corners) { return (corners || ['tl', 'br']).map((c) => `<span class=\"mk\">${c}</span>`).join(''); }" +
              "let t = 0;" +
              "function show() {" +
              "  document.getElementById('f').innerHTML = panel.items.map((x, i) => `<p class=\"it\">${panel.title} ${i}: ${x}</p>`).join('');" +
              "  document.getElementById('m').innerHTML = t % 2 ? marks(['a', 'b', 'c']) : marks();" +
              "}" +
              "show();" +
              "setInterval(() => { t++; show(); }, 500);"),
         Ticks(3), new[] { 0, 1, 2, 3 }),

        ("lists bounded other ways: an array made fresh and filled in a loop over a bounded list, and a while trim",
         Page(".hi { color: #fc6; margin: 1px 0; } .hs { color: #6cf; margin: 1px 0; }",
              "<div id=\"hi\"></div><div id=\"hist\"></div><p>end</p>",
              "const readings = [3, 8, 1, 9, 4];" +
              "const hist = [];" +
              "let t = 0;" +
              "function show() {" +
              "  const high = [];" +
              "  for (const r of readings) if (r + t % 3 > 4) high.push(r + t % 3);" +
              "  let h = '';" +
              "  for (const v of high) h += `<p class=\"hi\">${v}</p>`;" +
              "  document.getElementById('hi').innerHTML = h;" +
              "  document.getElementById('hist').innerHTML = hist.map((x) => `<p class=\"hs\">${x}</p>`).join('');" +
              "}" +
              "setInterval(() => {" +
              "  t++;" +
              "  hist.push('t' + t);" +
              "  while (hist.length > 3) hist.shift();" +
              "  show();" +
              "}, 500);" +
              "show();"),
         Ticks(5), new[] { 0, 1, 2, 3, 5 }),

        ("lists bounded other ways: a splice trim, and concat then slice",
         Page(".rc { color: #cf6; margin: 1px 0; } .ev { color: #f9c; margin: 1px 0; }",
              "<div id=\"rec\"></div><div id=\"ev\"></div><p>end</p>",
              "const ev = [];" +
              "let recent = [];" +
              "let t = 0;" +
              "function show() {" +
              "  document.getElementById('rec').innerHTML = recent.map((x) => `<p class=\"rc\">${x}</p>`).join('');" +
              "  document.getElementById('ev').innerHTML = ev.map((x) => `<p class=\"ev\">${x}</p>`).join('');" +
              "}" +
              "setInterval(() => {" +
              "  t++;" +
              "  ev.push('e' + t);" +
              "  ev.splice(0, ev.length - 2);" +
              "  recent = ['r' + t].concat(recent).slice(0, 2);" +
              "  show();" +
              "}, 500);" +
              "show();"),
         Ticks(4), new[] { 0, 1, 2, 4 }),

        ("rows of a list of fixed length, each changing its shape with its item",
         Page(".dev { display: flex; width: 240px; } .name { width: 120px; } .tag { width: 80px; color: #6c6; } .al .tag { color: #f55; }",
              "<div id=\"devs\"></div><p>end</p>",
              "const devices = [{ name: 'Pump', alarm: false }, { name: 'Fan', alarm: true }, { name: 'Vent', alarm: false }];" +
              "let t = 0;" +
              "function draw() {" +
              "  document.getElementById('devs').innerHTML = devices.map((d) => d.alarm" +
              "    ? `<div class=\"dev al\"><span class=\"name\">${d.name}</span><span class=\"tag\">ALARM</span></div>`" +
              "    : `<div class=\"dev\"><span class=\"name\">${d.name}</span><span class=\"tag\">ok</span></div>`).join('');" +
              "}" +
              "draw();" +
              "setInterval(() => { t++; devices[t % 3].alarm = !devices[t % 3].alarm; draw(); }, 500);"),
         Ticks(4), new[] { 0, 1, 2, 4 }),

        ("a log kept in a state object: newest first with concat, trimmed by a slice, the object patched through Object.assign and read back through the values a helper returns",
         Page(".line { display: flex; width: 300px; padding: 2px 6px; background: #1a2130; margin-bottom: 2px; } .t { width: 40px; color: #888; } .m { width: 220px; }",
              "<div id=\"log\"></div><p id=\"note\">-</p>",
              "const state = { tick: 0, mode: 'auto', log: [{ t: 0, msg: 'armed', warn: false }] };" +
              "function set(patch) { Object.assign(state, patch); render(); }" +
              "function add(msg, warn) { state.log = [{ t: state.tick, msg, warn }].concat(state.log).slice(0, 4); }" +
              "function values() { return { log: state.log, note: state.log.length + ' kept', mode: state.mode }; }" +
              "function render() {" +
              "  const v = values();" +
              "  document.getElementById('log').innerHTML = v.log.map((e) => `<div class=\"line\"><span class=\"t\">${e.t}</span><span class=\"m\" style=\"color:${e.warn ? '#fc6' : '#6cf'}\">${e.msg}</span></div>`).join('');" +
              "  document.getElementById('note').textContent = v.note + ' / ' + v.mode;" +
              "}" +
              "setInterval(() => {" +
              "  state.tick++;" +
              "  add('event ' + state.tick, state.tick % 2 === 1);" +
              "  if (state.tick % 3 === 0) set({ mode: state.mode === 'auto' ? 'manual' : 'auto' }); else render();" +
              "}, 500);" +
              "render();"),
         Ticks(6), new[] { 0, 1, 2, 3, 4, 6 }),

        ("a list in a state object's field: pushed under a length test, filtered back into the field, an item's field flipped",
         Page(".on { color: #6c6; margin: 1px 0; } .off { color: #777; margin: 1px 0; }",
              "<div id=\"list\"></div><p id=\"count\">-</p><button id=\"add\">Add</button><button id=\"prune\">Prune</button><button id=\"flip\">Flip</button>",
              "const state = { items: [{ name: 'Pump', on: true }, { name: 'Fan', on: false }] };" +
              "const names = ['Vent', 'Heater', 'Light'];" +
              "function draw() {" +
              "  document.getElementById('list').innerHTML = state.items.map((d) => `<p class=\"${d.on ? 'on' : 'off'}\">${d.name}</p>`).join('');" +
              "  document.getElementById('count').textContent = state.items.length + ' devices';" +
              "}" +
              "document.getElementById('add').addEventListener('click', () => { if (state.items.length < 5) state.items.push({ name: names[state.items.length % 3], on: state.items.length % 2 === 0 }); draw(); });" +
              "document.getElementById('prune').addEventListener('click', () => { state.items = state.items.filter((d) => d.on); draw(); });" +
              "document.getElementById('flip').addEventListener('click', () => { if (state.items.length) state.items[0].on = !state.items[0].on; draw(); });" +
              "draw();"),
         Steps("add", "add", "prune", "flip", "add", "add", "add", "add", "flip", "prune"), new[] { 0, 1, 2, 3, 4, 7, 8, 9, 10 }),

        ("a list a helper takes items from: the rows it drops hidden, what follows moving up",
         Page(".it { margin: 1px 0; color: #9cf; }",
              "<div id=\"l\"></div><p>after</p>",
              "const xs = ['a', 'b', 'c', 'd'];" +
              "function drop(a) { if (a.length > 1) a.pop(); }" +
              "function show() { document.getElementById('l').innerHTML = xs.map((x) => '<p class=\"it\">' + x + '</p>').join(''); }" +
              "setInterval(() => { drop(xs); show(); }, 500);" +
              "show();"),
         Ticks(4), new[] { 0, 1, 2, 4 }),

        ("rows with more than 8 shapes: an icon of three, a badge and a note chosen independently, a status colour, fields changed in a forEach and a row pushed under a cap",
         Page(".dev { display: flex; width: 330px; height: 22px; margin-bottom: 2px; background: #1b2230; } .ic { width: 20px; } .fan { color: #6cf; } .valve { color: #fc6; } .bolt { color: #f66; }" +
              ".name { width: 110px; } .badge { width: 40px; color: #111; background: #fc6; } .note { width: 100px; margin: 0; color: #888; }",
              "<div id=\"devs\"></div><p>end of list</p>",
              "const icons = ['fan', 'valve', 'bolt'];" +
              "const badges = ['', 'NEW', 'HOT'];" +
              "const devices = [" +
              "  { name: 'Pump', state: 'ok', badge: '', icon: 'fan', note: 'main loop' }," +
              "  { name: 'Vent', state: 'warn', badge: 'NEW', icon: 'valve', note: '' }];" +
              "let t = 0;" +
              "function row(d) {" +
              "  return '<div class=\"dev\">'" +
              "    + (d.icon === 'fan' ? '<span class=\"ic fan\">F</span>' : d.icon === 'valve' ? '<span class=\"ic valve\">V</span>' : '<span class=\"ic bolt\">B</span>')" +
              "    + '<span class=\"name\" style=\"color:' + (d.state === 'trip' ? '#f55' : d.state === 'warn' ? '#fc6' : '#6c6') + '\">' + d.name + '</span>'" +
              "    + (d.badge ? '<span class=\"badge\">' + d.badge + '</span>' : '')" +
              "    + (d.note ? '<p class=\"note\">' + d.note + '</p>' : '')" +
              "    + '</div>';" +
              "}" +
              "function draw() { document.getElementById('devs').innerHTML = devices.map(row).join(''); }" +
              "setInterval(() => {" +
              "  t++;" +
              "  devices.forEach((d, i) => {" +
              "    d.icon = icons[(t + i) % 3];" +
              "    d.badge = badges[(t * 2 + i) % 3];" +
              "    d.note = (t + i) % 2 ? '' : 'note ' + t;" +
              "    d.state = ['ok', 'warn', 'trip'][(t + 2 * i) % 3];" +
              "  });" +
              "  if (devices.length < 3) devices.push({ name: 'Heater', state: 'trip', badge: 'HOT', icon: 'bolt', note: 'check fuse' });" +
              "  draw();" +
              "}, 500);" +
              "draw();"),
         Ticks(7), new[] { 0, 1, 2, 3, 4, 5, 6, 7 }),

        ("rows with more than 8 shapes whose note draws nothing at rest (written `!note ? '' : …`), after a badge that changes the row's size; a separator on every row but the first",
         Page(".dev { display: flex; width: 330px; height: 22px; margin-bottom: 2px; background: #1b2230; } .ic { width: 20px; } .fan { color: #6cf; } .valve { color: #fc6; } .bolt { color: #f66; }" +
              ".name { width: 110px; } .badge { width: 40px; color: #111; background: #fc6; } .note { width: 100px; margin: 0; color: #888; } .sep { height: 2px; width: 330px; background: #345; margin-bottom: 2px; }",
              "<div id=\"devs\"></div><p>end of list</p>",
              "const icons = ['fan', 'valve', 'bolt'];" +
              "const devices = [{ icon: 'fan', badge: 'NEW', note: '' }, { icon: 'bolt', badge: '', note: 'x' }, { icon: 'valve', badge: 'HOT', note: 'y' }];" +
              "let t = 0;" +
              "function draw() {" +
              "  document.getElementById('devs').innerHTML = devices.map((d, i) => (i > 0 ? '<div class=\"sep\"></div>' : '') + '<div class=\"dev\">'" +
              "    + (d.icon === 'fan' ? '<span class=\"ic fan\">F</span>' : d.icon === 'valve' ? '<span class=\"ic valve\">V</span>' : '<span class=\"ic bolt\">B</span>')" +
              "    + '<span class=\"name\">dev ' + i + '</span>'" +
              "    + (d.badge ? '<span class=\"badge\">' + d.badge + '</span>' : '')" +
              "    + (!d.note ? '' : '<p class=\"note\">' + d.note + '</p>')" +
              "    + '</div>').join('');" +
              "}" +
              "setInterval(() => {" +
              "  t++;" +
              "  devices.forEach((d, i) => { d.icon = icons[(t + i) % 3]; d.badge = (t + i) % 2 ? '' : 'B' + t; d.note = (t + 2 * i) % 3 ? 'n' + t : ''; });" +
              "  draw();" +
              "}, 500);" +
              "draw();"),
         Ticks(4), new[] { 0, 1, 2, 3, 4 }),

        ("a table built by an immediately invoked function (arrow and function forms): its rows a list, their fields fixed values, a theme colour one too",
         Page(".row { display: flex; width: 320px; margin-bottom: 2px; } .k { width: 90px; } .g { width: 60px; } .n { width: 60px; } .sep { height: 2px; width: 320px; background: #333; }",
              "<h3 id=\"head\">Roles</h3><div id=\"rows\"></div><div id=\"units\"></div><p id=\"n\">-</p>",
              "const ROLES = (() => {" +
              "  const PUMPS = [['P1', 'kPa'], ['P2', 'kPa'], ['P3', 'L']];" +
              "  const R = (key, group, devs) => ({ key, group, colour: group === 'gas' ? '#6cf' : '#fc6', devs: devs.map((d) => ({ name: d[0], unit: d[1] })) });" +
              "  return [R('O2', 'gas', PUMPS), R('Filt', 'filt', [['F1', '%']]), R('CO2', 'gas', PUMPS.slice(0, 2))];" +
              "})();" +
              "const UNITS = function () { return ['kPa', '%', 'L', 'mol']; }();" +
              "const THEME = (() => { const dark = true; return dark ? '#8cf' : '#246'; })();" +
              "let t = 0;" +
              "function draw() {" +
              "  document.getElementById('head').style.color = t % 2 ? THEME : '#ddd';" +
              "  document.getElementById('rows').innerHTML = ROLES.filter((r) => t % 3 !== 1 || r.group === 'gas').map((r) =>" +
              "    `<div class=\"row\"><span class=\"k\" style=\"color:${r.colour}\">${r.key}</span><span class=\"g\">${r.group}</span><span class=\"n\">${r.devs.length}</span></div>`).join('');" +
              "  document.getElementById('units').innerHTML = UNITS.filter((u, i) => i <= t % 4).map((u) => `<span class=\"n\">${u}</span>`).join('');" +
              "  document.getElementById('n').textContent = ROLES.length + ' roles, ' + UNITS.length + ' units';" +
              "  ROLES.forEach((r, i) => { if (i === t % 3) document.getElementById('n').style.color = r.colour; });" +
              "}" +
              "setInterval(() => { t++; draw(); }, 500);" +
              "draw();"),
         Ticks(5), new[] { 0, 1, 2, 3, 4, 5 }),

        ("an item found in a list the compile knows, with || and ?? fallbacks: its fields fixed values, its own list's rows; findIndex, indexOf, some, every and includes read",
         Page(".ln { margin: 1px 0; color: #9ab; } #menu { display: flex; } #menu span { width: 110px; } .on { background: #234; } .off { background: #111; } #alt { background: #111; }",
              "<h3 id=\"title\">-</h3><div id=\"lines\"></div><div id=\"menu\"></div><p id=\"idx\">-</p><p id=\"alt\">-</p>",
              "const GASES = (() => {" +
              "  const mk = (key, label, color, lines) => ({ key, label, color, lines: lines.map((x) => ({ name: x[0], unit: x[1] })) });" +
              "  return [" +
              "    mk('o2', 'Oxygen', '#6cf', [['Tank A', 'kPa'], ['Tank B', 'kPa']])," +
              "    mk('n2', 'Nitrogen', '#fc6', [['Line', 'kPa']])," +
              "    mk('co2', 'Carbon dioxide', '#f66', [['Scrubber', '%'], ['Vent', 'kPa'], ['Tank', 'mol']])];" +
              "})();" +
              "let pick = 'n2';" +
              "let t = 0;" +
              "function draw() {" +
              "  const sel = GASES.find((g) => g.key === pick) || GASES[0];" +
              "  const alt = GASES.findLast((g) => g.lines.length > 5) ?? GASES[t % 3];" +
              "  document.getElementById('title').textContent = sel.label;" +
              "  document.getElementById('title').style.color = sel.color;" +
              "  document.getElementById('lines').innerHTML = sel.lines.map((l, i) => `<p class=\"ln\">${i + 1}. ${l.name} (${l.unit})</p>`).join('');" +
              "  document.getElementById('menu').innerHTML = GASES.map((g) => `<span class=\"${g.key === sel.key ? 'on' : 'off'}\" style=\"color:${g.color}\">${g.label}</span>`).join('');" +
              "  document.getElementById('idx').textContent = 'at ' + GASES.findIndex((g) => g.key === pick) + ', ' + (GASES.some((g) => g.lines.length > 2) ? 'long' : 'short') + ', '" +
              "    + (GASES.every((g) => g.lines.length > 0) ? 'all' : 'not all') + ', ' + (['o2', 'n2'].includes(pick) ? 'in' : 'out') + ', ' + ['o2', 'n2', 'co2'].indexOf(pick);" +
              "  document.getElementById('alt').textContent = alt.label;" +
              "  document.getElementById('alt').style.color = alt.color;" +
              "  document.getElementById('alt').style.background = ['#300', '#030'].find((c, i) => i === t % 3) ?? '#003';" +
              "}" +
              "setInterval(() => { t++; pick = ['o2', 'n2', 'co2', 'xe'][t % 4]; draw(); }, 500);" +
              "draw();"),
         Ticks(5), new[] { 0, 1, 2, 3, 4, 5 }),

        ("fields of a list's items as fixed values: a log in a state object whose rows take their colour and class from each entry, and rows made by a map callback",
         Page(".line { display: flex; width: 320px; margin-bottom: 2px; background: #1a2130; } .t { width: 40px; } .m { width: 200px; } .tag { width: 60px; }" +
              ".row { width: 200px; margin: 1px 0; } .row.hot { background: #422; } .row.cold { background: #224; } .row.mild { background: #242; }",
              "<div id=\"log\"></div><div id=\"rows\"></div>",
              "const st = { tick: 0, log: [{ t: 0, msg: 'armed', tag: 'INFO', color: '#888' }] };" +
              "function push(msg, tag, color) { st.log = [{ t: st.tick, msg, tag, color }].concat(st.log).slice(0, 4); }" +
              "const temps = [{ name: 'Room', v: 21 }, { name: 'Tank', v: 48 }, { name: 'Pipe', v: 5 }];" +
              "function draw() {" +
              "  document.getElementById('log').innerHTML = st.log.map((e) => `<div class=\"line\"><span class=\"t\">${e.t}</span><span class=\"m\">${e.msg}</span><span class=\"tag\" style=\"color:${e.color}\">${e.tag}</span></div>`).join('');" +
              "  const rows = temps.map((x) => ({ label: x.name + ' ' + x.v, kind: x.v > 30 ? 'hot' : x.v < 10 ? 'cold' : 'mild', ink: x.v > 30 ? '#f96' : '#9cf' }));" +
              "  document.getElementById('rows').innerHTML = rows.map((r) => `<p class=\"row ${r.kind}\" style=\"color:${r.ink}\">${r.label}</p>`).join('');" +
              "}" +
              "setInterval(() => {" +
              "  st.tick++;" +
              "  const i = st.tick % 3;" +
              "  const tag = i === 2 ? 'TRIP' : i === 1 ? 'WATCH' : 'CLEAR';" +
              "  const color = i === 2 ? '#f55' : i === 1 ? '#fc6' : '#6c6';" +
              "  push('event ' + st.tick, tag, color);" +
              "  temps[st.tick % 3].v = (temps[st.tick % 3].v + 17) % 50;" +
              "  draw();" +
              "}, 500);" +
              "draw();"),
         Ticks(6), new[] { 0, 1, 2, 3, 4, 6 }),

        ("a loop over a count from a fixed set, and a list filtered by a test that reads its index",
         Page("#meter { display: flex; } .bar { width: 12px; height: 20px; background: #3a7; margin-right: 3px; } .odd { color: #fc6; }",
              "<div id=\"meter\"></div><div id=\"odd\"></div><p>meter end</p>",
              "const levels = [1, 3, 5, 2];" +
              "const names = ['a', 'b', 'c', 'd', 'e', 'f'];" +
              "let k = 0;" +
              "function show() {" +
              "  const level = levels[k % 4];" +
              "  let h = '';" +
              "  for (let i = 0; i < level; i++) h += '<div class=\"bar\"></div>';" +
              "  document.getElementById('meter').innerHTML = h;" +
              "  document.getElementById('odd').innerHTML = names.filter((x, i) => i % 2 === k % 2).map((x, i) => `<p class=\"odd\">${i}: ${x}</p>`).join('');" +
              "}" +
              "setInterval(() => { k++; show(); }, 500);" +
              "show();"),
         Ticks(5), new[] { 0, 1, 2, 3, 5 }),

        ("for loops counting from 1 with <=, down with > and with >= by -5, by a step of 2, and by a fraction",
         Page(".row { display: flex; } .cell { width: 44px; height: 20px; background: #3a7; margin-right: 3px; }",
              "<div id=\"up\" class=\"row\"></div><div id=\"down\" class=\"row\"></div><div id=\"fives\" class=\"row\"></div><div id=\"even\" class=\"row\"></div><div id=\"tenths\" class=\"row\"></div><p>loops end</p>",
              "const sizes = [2, 4, 3, 1];" +
              "let k = 0;" +
              "function show() {" +
              "  const n = sizes[k % 4];" +
              "  let up = '';" +
              "  for (let i = 1; i <= n; i++) up += '<div class=\"cell\">' + i + '</div>';" +
              "  document.getElementById('up').innerHTML = up;" +
              "  let down = '';" +
              "  for (var j = 4; j > 4 - n; j--) down += `<div class=\"cell\">${j}</div>`;" +
              "  document.getElementById('down').innerHTML = down;" +
              "  let fives = '';" +
              "  for (let i = 20; i >= 20 - 5 * n; i -= 5) fives += '<div class=\"cell\">' + i + '</div>';" +
              "  document.getElementById('fives').innerHTML = fives;" +
              "  let even = '';" +
              "  for (let i = 0; i < 2 * n; i = i + 2) even += '<div class=\"cell\">' + i + '</div>';" +
              "  document.getElementById('even').innerHTML = even;" +
              "  let tenths = '';" +
              "  for (let i = 0; i < n * 0.1; i += 0.1) tenths += '<div class=\"cell\">' + i.toFixed(1) + '</div>';" +
              "  document.getElementById('tenths').innerHTML = tenths;" +
              "}" +
              "setInterval(() => { k++; show(); }, 500);" +
              "show();"),
         Ticks(4), new[] { 0, 1, 2, 3, 4 }),

        ("clicks kept as functions in an array: a helper pushes each and returns the attribute data-act=\"index\" (a tab of two shapes, rows, rows that have it or not), " +
         "every [data-act] shown found after the write and given a listener calling acts[Number(getAttribute)]; a row's function keeps the item it was made with",
         Page(".btn { width: 120px; height: 30px; background: #234; margin: 4px; } .btn.on { background: #363; } .row { height: 24px; background: #222; margin: 2px; } .row.on { color: #6c6; }",
              "<div id=\"tabs\"></div><div id=\"panel\"></div><p id=\"log\">-</p><p id=\"count\">-</p><button id=\"rot\">Rotate</button>",
              "let acts = [];" +
              "const act = (fn) => { acts.push(fn); return ' data-act=\"' + (acts.length - 1) + '\"'; };" +
              "let n = 0, mode = 'a';" +
              "const items = [{ name: 'Pump', pick: null }, { name: 'Fan', pick: null }, { name: 'Vent', pick: null }];" +
              "items[0].pick = () => say('pick Pump');" +
              "items[2].pick = () => say('pick Vent');" +
              "function say(s) { document.getElementById('log').textContent = s + ' / ' + acts.length; render(); }" +
              "function tabs(v) {" +
              "  return v.mode === 'a'" +
              "    ? '<div id=\"ta\" class=\"btn\"' + act(() => { mode = 'b'; render(); }) + '>Tab A ' + v.n + '</div>'" +
              "    : '<div id=\"tb\" class=\"btn on\"' + act(() => { mode = 'a'; render(); }) + '>Tab B</div>';" +
              "}" +
              "function render() {" +
              "  acts = [];" +
              "  const v = { mode, n };" +
              "  document.getElementById('tabs').innerHTML = tabs(v);" +
              "  document.getElementById('panel').innerHTML = '<div id=\"add\" class=\"btn\"' + act(() => { n++; render(); }) + '>Add ' + n + '</div>'" +
              "    + items.map((g, i) => '<div id=\"r' + i + '\" class=\"row\"' + act(() => say(i + ' ' + g.name)) + '>' + g.name + '</div>').join('')" +
              "    + items.map((g) => '<div class=\"row' + (g.pick ? ' on' : '') + '\"' + (g.pick ? act(g.pick) : '') + '>' + g.name + '</div>').join('');" +
              "  const bound = document.querySelectorAll('[data-act]');" +
              "  for (let i = 0; i < bound.length; i++) {" +
              "    const el = bound[i], idx = Number(el.getAttribute('data-act'));" +
              "    el.addEventListener('click', () => acts[idx]());" +
              "  }" +
              "  document.getElementById('count').textContent = bound.length + ' bound';" +
              "}" +
              "document.getElementById('rot').addEventListener('click', () => { const first = items.shift(); if (items.length < 3) items.push(first); });" +
              "render();"),
         Steps("r0", "rot", "r0", "add", "ta", "r1", "tb", "add"), new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }),

        ("an attribute the page never wrote reads null on an element markup makes, a click listener on it and the compile's own click region with it",
         Page(".b { width: 90px; height: 24px; background: #234; }", "<div id=\"box\"></div><p id=\"out\">-</p>",
              "let n = 0;" +
              "function draw() {" +
              "  document.getElementById('box').innerHTML = '<div id=\"m\" class=\"b\" data-k=\"x\">M ' + n + '</div>';" +
              "  const els = document.querySelectorAll('.b');" +
              "  for (let i = 0; i < els.length; i++) els[i].addEventListener('click', () => { n++; draw(); });" +
              "  document.getElementById('out').textContent = 'click ' + els[0].getAttribute('data-click') + ', k ' + els[0].getAttribute('data-k') + ', has ' + els[0].hasAttribute('data-click') + ', sel ' + document.querySelectorAll('[data-click]').length;" +
              "}" +
              "draw();"),
         Steps("m", "m"), new[] { 0, 1, 2 }),

        ("the children of a flex or grid container are items with boxes of their own, never text: spans with no id or class in a grid row " +
         "and in a display:flex span carry attributes only known at run time, read back with getAttribute",
         Page(".row { display: grid; grid-template-columns: 60px 1fr 60px; gap: 8px; height: 26px; margin-bottom: 4px; background: #1a2230; }",
              "<div id=\"list\"></div><p id=\"out\">-</p>",
              "const log = [{ t: '02:14', tag: 'INFO', color: '#888888' }, { t: '02:13', tag: 'WARN', color: '#ff8800' }];" +
              "let k = 0;" +
              "function show() {" +
              "  document.getElementById('list').innerHTML = log.map((e, i) => '<div class=\"row\"><span data-at=\"' + (k + i) + '\">' + e.t + '</span>'" +
              "    + '<span style=\"display:flex;gap:4px\"><span style=\"font-size:11px\">tag</span><span data-at=\"' + (k * 2 + i) + '\" style=\"color:' + e.color + '\">' + e.tag + '</span></span>'" +
              "    + '<span>' + e.t + '</span></div>').join('');" +
              "  document.getElementById('out').textContent = 'at ' + document.querySelectorAll('[data-at]')[1].getAttribute('data-at');" +
              "}" +
              "setInterval(() => { k++; show(); }, 500);" +
              "show();"),
         Ticks(3), new[] { 0, 1, 2, 3 }),

        ("a flex gap between list rows that have two shapes, the last row's shape changing: the gap is between the rows shown, as margins would be",
         Page("#list { display: flex; flex-direction: column; gap: 6px; } #chips { display: flex; gap: 10px; } .row { height: 26px; background: #1a2230; } .row.on { background: #2f855a; } .chip { width: 50px; height: 20px; background: #345; } .chip.on { background: #6a4; }",
              "<div id=\"list\"></div><div id=\"chips\"></div><p>end</p>",
              "const items = [{ n: 'Pump', on: true }, { n: 'Fan', on: false }, { n: 'Vent', on: true }];" +
              "let k = 0;" +
              "function show() {" +
              "  document.getElementById('list').innerHTML = items.map((g) => g.on ? '<div class=\"row on\">' + g.n + ' on</div>' : '<div class=\"row\">' + g.n + '</div>').join('');" +
              "  document.getElementById('chips').innerHTML = items.map((g) => g.on ? '<div class=\"chip on\"></div>' : '<div class=\"chip\"></div>').join('');" +
              "}" +
              "setInterval(() => { k++; items[k % 3].on = !items[k % 3].on; show(); }, 500);" +
              "show();"),
         Ticks(4), new[] { 0, 1, 2, 3, 4 }),

        ("a text beside rows whose shape choice changes in the same markup write, the text changed or not: it reads its value, never its stand-in",
         Page(".big { height: 30px; background: #232833; } .row { height: 24px; background: #2b3a55; color: #889; } .row.on { color: #6d9; }",
              "<div id=\"panel\"></div><p>end</p>",
              "const items = [{ n: 'Pump', pick: true }, { n: 'Fan', pick: false }, { n: 'Vent', pick: true }];" +
              "let n = 0, k = 0;" +
              "function render() {" +
              "  document.getElementById('panel').innerHTML = '<div id=\"add\" class=\"big\">ADD ' + n + '</div>'" +
              "    + items.map((g) => '<div class=\"row' + (g.pick ? ' on' : '') + '\">' + g.n + '</div>').join('');" +
              "}" +
              "setInterval(() => { k++; if (k % 2) n++; const first = items.shift(); if (items.length < 3) items.push(first); render(); }, 500);" +
              "render();"),
         Ticks(4), new[] { 0, 1, 2, 3, 4 }),
    };

    /// <summary>Features outside what is translated, each refused by name - the page keeps its old path.</summary>
    private static readonly (string Feature, string Page, string Reason)[] Refusals =
    {
        ("requestAnimationFrame", Page("", "<p id=\"a\">x</p>", "requestAnimationFrame(() => { document.getElementById('a').textContent = 'y'; });"), "requestAnimationFrame"),
        ("a list whose array grows with nothing holding it", Page("", "<div id=\"a\"></div>", "const log = []; setInterval(() => { log.push('x'); document.getElementById('a').innerHTML = log.map((x) => '<p>' + x + '</p>').join(''); }, 10);"), "a list whose length the compile cannot bound: \"log\".push() with nothing holding it to a length"),
        ("a list in an object's field that grows with nothing holding it", Page("", "<div id=\"a\"></div><p>end</p>", "const st = { log: ['a'] }; setInterval(() => { st.log.push('x'); document.getElementById('a').innerHTML = st.log.map((x) => '<p>' + x + '</p>').join(''); }, 10);"), "a list whose length the compile cannot bound: \"log\".push() with nothing holding it to a length"),
        ("a list in a field of an object handed where the compile cannot follow it", Page("", "<div id=\"a\"></div>", "const st = { log: ['a'] }; const fs = [function (o) { o.log.push('b'); }]; setInterval(() => { fs[0](st); document.getElementById('a').innerHTML = st.log.map((x) => '<p>' + x + '</p>').join(''); }, 10);"), "a list read from the field \"log\" of an object handed where the compile cannot follow what is done to it"),
        ("a list stored in another object's field, where it is added to", Page("", "<div id=\"a\"></div>", "const st = { log: ['a'] }; const view = { rows: st.log }; setInterval(() => { view.rows.push('x'); document.getElementById('a').innerHTML = st.log.map((x) => '<p>' + x + '</p>').join(''); }, 10);"), "is stored in the field \"rows\", which adds to it"),
        ("an array copied into an object's field by Object.assign, then added to there", Page("", "<div id=\"a\"></div><p>end</p>", "const st = { log: [] }; const a = ['x']; Object.assign(st, { log: a }); setInterval(() => { st.log.push('y'); document.getElementById('a').innerHTML = a.map((x) => '<p>' + x + '</p>').join(''); }, 10);"), "\"a\" is stored in the field \"log\", which adds to it"),
        ("a colour only known at run time, said as a colour", Page("", "<p id=\"a\">x</p>", "setTimeout(() => { document.getElementById('a').style.color = localStorage.getItem('ink'); }, 10);"), "style.color of \"a\" is written a colour only known at run time"),
        ("markup added with += outside a loop", Page("", "<ul id=\"a\"></ul>", "setInterval(() => { document.getElementById('a').innerHTML += '<li>x</li>'; }, 10);"), "with += outside a list the compile can bound"),
        ("a list of elements joined with text between them", Page("", "<div id=\"a\"></div>", "const xs = ['a', 'b']; setTimeout(() => { document.getElementById('a').innerHTML = xs.map((x) => '<p>' + x + '</p>').join(', '); }, 10);"), "a list joined with \", \" between its elements"),
        ("a list of text-level markup", Page("", "<p id=\"a\">-</p>", "const xs = ['a', 'b']; document.getElementById('a').innerHTML = xs.map((x) => '<b>' + x + '</b>').join(' ');"), "a list of text and text-level markup"),
        ("a list inside a list's rows", Page("", "<div id=\"a\"></div>", "const xs = [[1, 2], [3]]; setTimeout(() => { document.getElementById('a').innerHTML = xs.map((r) => '<div>' + r.map((x) => '<p>' + x + '</p>').join('') + '</div>').join(''); }, 10);"), "a list inside a row of another list"),
        ("CSS picking a list's rows by their place", Page(".row:last-child { color: #f00; }", "<div id=\"a\"></div>", "const xs = []; setInterval(() => { if (xs.length < 3) xs.push('x'); document.getElementById('a').innerHTML = xs.map((x) => '<p class=\"row\">' + x + '</p>').join(''); }, 10);"), "picking elements by their place among their siblings"),
        ("rows whose many choices all move each other", Page(".r { display: flex; } .s { margin-right: 4px; }", "<div id=\"a\"></div>",
            "const xs = [{ a: true, b: true, c: true, d: true }]; setInterval(() => { const x = xs[0]; x.a = !x.a; x.b = !x.b; x.c = !x.c; x.d = !x.d;" +
            " document.getElementById('a').innerHTML = xs.map((x) => '<div class=\"r\">' + (x.a ? '<span class=\"s\">aa</span>' : '') + (x.b ? '<span class=\"s\">bb</span>' : '') + (x.c ? '<span class=\"s\">cc</span>' : '') + (x.d ? '<span class=\"s\">dd</span>' : '') + '</div>').join(''); }, 10);"),
            "has choices that move the same things, in 16 combinations"),
        ("rows whose shapes are different sizes", Page("", "<div id=\"a\"></div>", "const xs = [{ big: true }, { big: false }]; setInterval(() => { xs[0].big = !xs[0].big; document.getElementById('a').innerHTML = xs.map((x) => x.big ? '<p>a</p><p>b</p>' : '<p>a</p>').join(''); }, 10);"), "is another size in its shape"),
        ("a list inside an attribute", Page("", "<div id=\"a\"></div>", "const xs = ['a', 'b']; document.getElementById('a').innerHTML = '<p title=\"' + xs.map((x) => '<b>' + x + '</b>').join('') + '\">t</p>';"), "a list inside an attribute's value"),
        ("a loop over a count only known at run time", Page("", "<div id=\"a\"></div>", "let n = 1; setInterval(() => { n = n * 2; let h = ''; for (let i = 0; i < n; i++) h += '<p>x</p>'; document.getElementById('a').innerHTML = h; }, 10);"), "a loop over a count only known at run time"),
        ("markup chosen with &&", Page("", "<div id=\"a\"></div>", "let on = true; setTimeout(() => { document.getElementById('a').innerHTML = on && '<b>on</b>'; }, 10);"), "chosen with && or ||"),
        ("markup whose tag is a value", Page("", "<div id=\"a\"></div>", "let t = 'b' + Math.random(); setTimeout(() => { document.getElementById('a').innerHTML = '<' + t + '>x</' + t + '>'; }, 10);"), "whose tag is a value"),
        ("reading innerHTML", Page("", "<div id=\"a\"><b>x</b></div><p id=\"o\">-</p>", "setTimeout(() => { document.getElementById('o').textContent = document.getElementById('a').innerHTML; }, 10);"), "reading .innerHTML"),
        // a list of them is what is shown when it is looked up (the data-act case in Lists); a first match is not followed yet
        ("a first match among what markup makes in only some of its shapes", Page("", "<div id=\"a\"></div><p id=\"n\">-</p>", "let on = false; setInterval(() => { on = !on; document.getElementById('a').innerHTML = on ? '<p class=\"x\">1</p>' : '<p class=\"x\">2</p><p class=\"x\">3</p>'; document.getElementById('n').textContent = document.querySelector('.x') ? 'y' : 'n'; }, 10);"), "in only some of its shapes"),
        ("a write to an element markup makes", Page("", "<div id=\"a\"></div>", "setTimeout(() => { document.getElementById('a').innerHTML = '<p id=\"m\">1</p>'; document.getElementById('m').style.color = '#f00'; }, 10);"), "the next markup write would undo it"),
        ("an element chosen by an id the compile cannot bound", Page("", "<p id=\"a1\">x</p>", "setTimeout(() => { document.getElementById(localStorage.getItem('which')).textContent = 'y'; }, 10);"), "from ids the compile cannot bound"),
        ("an element stored in an object", Page("", "<p id=\"a\">x</p>", "const o = { el: document.getElementById('a') }; setTimeout(() => { o.el.textContent = 'y'; }, 10);"), "used as a value this way"),
        ("an element passed to a function the compile cannot follow", Page("", "<p id=\"a\">x</p>", "const fs = [function (el) { el.textContent = 'y'; }]; fs[0](document.getElementById('a'));"), "passed to a function the compile cannot follow"),
        ("a selector testing a class the script changes", Page(".on { color: red; }", "<p id=\"a\" class=\"x\">x</p><p id=\"b\" class=\"on\">y</p>",
            "const lit = document.querySelectorAll('.on'); setTimeout(() => { document.getElementById('a').classList.add('on'); lit.forEach((e) => { e.textContent = 'z'; }); }, 10);"), "tests the class \"on\", which the script changes"),
        ("forEach on an HTMLCollection", Page("", "<p class=\"a\" id=\"a\">x</p>", "document.getElementsByClassName('a').forEach((e) => { e.textContent = 'y'; });"), "forEach on an HTMLCollection"),
        ("a list's member not translated", Page("", "<p id=\"a\">x</p>", "setTimeout(() => { for (const [i, e] of document.querySelectorAll('p').entries()) e.textContent = i; }, 10);"), ".entries of a list of elements"),
        ("a selector matching text drawn inside its parent", Page("", "<p id=\"a\">a <b>b</b></p>", "setTimeout(() => { document.querySelector('p b').textContent = 'y'; }, 10);"), "no box of its own"),
        ("a non-click event", Page("", "<button id=\"b\">x</button>", "document.getElementById('b').addEventListener('mousedown', () => {});"), "delivers only clicks"),
        ("reading the event object", Page("", "<button id=\"b\">x</button><p id=\"a\">x</p>", "document.getElementById('b').addEventListener('click', (e) => { document.getElementById('a').textContent = e.type; });"), "reads its event object"),
        ("a size that moves its siblings", Page("#a { height: 10px; background: #fff; }", "<div id=\"a\"></div><p>below</p>", "setTimeout(() => { document.getElementById('a').style.height = 30 + 'px'; }, 10);"), "is in normal flow"),
        ("classList.replace (not translated)", Page("", "<p id=\"a\" class=\"x\">x</p>", "setTimeout(() => { document.getElementById('a').classList.replace('x', 'y'); }, 10);"), "classList.replace"),
        ("navigating to another document", Page("", "<button id=\"b\">x</button>", "document.getElementById('b').onclick = () => { location.href = 'https://example.com/'; };"), "has no network to fetch another"),
        ("changing location.search", Page("", "<button id=\"b\">x</button>", "document.getElementById('b').onclick = () => { location.search = '?a=1'; };"), "location.search = …, which loads another document"),
        ("location.assign to a URL only known at run time", Page("", "<button id=\"b\">x</button>", "document.getElementById('b').onclick = () => { location.assign(localStorage.getItem('u')); };"), "may be another document"),
        ("location used as a value", Page("", "<p id=\"a\">x</p>", "const l = location; setTimeout(() => { document.getElementById('a').textContent = l.hash; }, 10);"), "location used this way"),
        ("a named Storage property", Page("", "<p id=\"a\">x</p>", "localStorage.mode = 'x'; setTimeout(() => { document.getElementById('a').textContent = 'y'; }, 10);"), "named properties are not yet"),
        ("an attribute CSS selects on, set to a value known only at run time", Page("[data-v=\"1\"] { color: red; }", "<p id=\"a\">x</p>", "let v = 0; setInterval(() => { v++; document.getElementById('a').setAttribute('data-v', v); }, 10);"), "set to a value only known at run time"),
        ("an attribute the page draws from directly", Page("", "<img id=\"a\" src=\"x.png\">", "setTimeout(() => { document.getElementById('a').setAttribute('width', '20'); }, 10);"), "draws from directly"),
        ("setAttribute with a name known only at run time", Page("", "<p id=\"a\">x</p>", "let k = 'data-' + 1; setTimeout(() => { document.getElementById('a').setAttribute(k, 'y'); }, 10);"), "with a name only known at run time"),
        ("writing innerWidth", Page("", "<p id=\"a\">x</p>", "window.innerWidth = 5; setTimeout(() => { document.getElementById('a').textContent = 'y'; }, 10);"), "innerWidth written by the script"),
        ("a constant text that wraps at this console's size", Page("#a { width: 60px; }", "<p id=\"a\">ok</p><p>below</p>", "setTimeout(() => { document.getElementById('a').textContent = 'a message far too long for sixty pixels'; }, 10);"), "\"a message far too long for sixty pixels\" in \"a\""),
        ("a text that pushes the rest of its line along", Page("", "<p><span id=\"a\">1</span> then <span id=\"b\">2</span></p>", "setTimeout(() => { document.getElementById('a').textContent = 'one hundred'; }, 10);"), "moves the scene's"),
        ("a class that takes shapes away says what it changed", Page(".box { width: 40px; height: 20px; background: #a33; } .box.gone { display: none; }", "<div id=\"a\" class=\"box\"></div><p>below</p>",
            "setTimeout(() => { document.getElementById('a').classList.add('gone'); }, 10);"), "scene lines at rest, "),
        ("a class name computed at run time", Page("", "<p id=\"a\">x</p>", "let k = 'c' + Math.random(); setTimeout(() => { document.getElementById('a').className = k; }, 10);"), "className of \"a\" set to a value only known at run time"),
        ("an inline handler for an event other than a click", Page("", "<button id=\"b\" onmousedown=\"go()\">x</button>", "function go() {}"), "(onmousedown=) on <button>: the vector mod delivers only clicks"),
        ("an onclick attribute on text drawn inside its parent", Page("", "<p id=\"a\">a <b onclick=\"go()\">b</b></p>", "function go() {}"), "the onclick attribute of <b>, which is drawn as part of its parent's text"),
        ("a refusal in an onclick attribute's code is said where it is", Page("", "<button id=\"b\" onclick=\"requestAnimationFrame(go)\">x</button>", "function go() {}"), "the onclick attribute of <button>: requestAnimationFrame"),
        ("this in a function also called other than as a listener", Page("", "<button id=\"b\">x</button>",
            "function f() { this.textContent = 'y'; } document.getElementById('b').addEventListener('click', f); setTimeout(f, 10);"), "`this` in f, which is called other than as a click listener"),
        ("this in an arrow inside a listener on two elements", Page("", "<button id=\"b\">x</button><button id=\"c\">y</button>",
            "for (const el of document.querySelectorAll('button')) el.addEventListener('click', function () { setTimeout(() => { this.textContent = 'z'; }, 10); });"), "`this` in an arrow function inside a click listener on more than one element"),
        ("an onclick attribute in markup", Page("", "<div id=\"a\"></div>", "function go() {} document.getElementById('a').innerHTML = '<button onclick=\"go()\">x</button>';"),
            "an inline event handler attribute (onclick=) in markup written into \"a\""),
        ("a markup loop over a count that skips rows", Page("", "<div id=\"up\"></div><p>end</p>",
            "const sizes = [2, 4]; let k = 0; setInterval(() => { k++; const n = sizes[k % 2]; let h = ''; for (let i = 0; i < n; i++) { if (i % 2) continue; h += '<p>' + i + '</p>'; } document.getElementById('up').innerHTML = h; }, 10);"),
            "a loop over a count that skips some of its rows"),
        ("a markup loop whose start is one of several values", Page("", "<div id=\"a\"></div>",
            "const ns = [2, 3]; let k = 0; setInterval(() => { k++; const n = ns[k % 2]; let h = ''; for (let i = n; i > 0; i--) h += '<p>x</p>'; document.getElementById('a').innerHTML = h; }, 10);"), "a for loop the compile does not follow"),
    };

    /// <summary>The console shapes a page is compiled for in game: square, wide and tall, in canvas units.</summary>
    private static readonly (float W, float H)[] Consoles = { (460f, 460f), (1036f, 460f), (460f, 1036f) };

    /// <summary>Pages seen in game, each compiled for every console shape and checked there.</summary>
    private static readonly (string File, List<(double, string?)> Steps, int[] Checkpoints)[] InGame =
    {
        (System.IO.Path.Combine("ScriptedScreensHtml", "examples", "09-transition.lua"), Ticks(8), new[] { 1, 2, 6, 8 }),
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-counter.lua"), Ticks(12), new[] { 1, 4, 12 }),
        // refused in game at first (a padded row read as two lines once "row 0" had a space in it); not yet seen compiled there
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-select.lua"), Ticks(8), new[] { 0, 1, 4, 8 }),
        // innerHTML part 1 in game: refused offline at first (a style width whose unit is a parameter's default)
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-markup.lua"), Ticks(12), new[] { 0, 1, 2, 3, 4, 6, 12 }),
        // innerHTML part 2: not yet seen in game
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-list.lua"), Steps(0.5, 0.5, 0.5, 0.5, "gas0", 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, "menu", "gas2", 0.5),
         new[] { 0, 1, 3, 5, 8, 11, 13, 14 }),
        // a list in a reassigned object field, patched through Object.assign by a helper: not yet seen in game
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-fields.lua"), Ticks(8), new[] { 0, 1, 2, 3, 4, 5, 8 }),
        // an item found in a table an immediately invoked function builds, fields of items as fixed values: not yet seen in game
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-find.lua"), Ticks(7), new[] { 0, 1, 2, 3, 4, 7 }),
        // booleans written into text: seen in game leaving the old text (a Lua boolean sent), not yet seen fixed there
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-bool.lua"), Ticks(4), new[] { 0, 2, 4 }),
        // the window's size, storage, an attribute CSS selects on, hidden, location.hash
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-globals.lua"), Ticks(8), new[] { 0, 2, 4, 8 }),
        // onclick attributes, this, for loops counting from 1, down and by 2, hidden under a display rule: not yet seen in game.
        // Box C (no id) is clicked in game only: the browser side here names an element with no id its own way.
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-events.lua"), Steps("a", "b", "d", "e", "d", "o", "o", "a"),
         new[] { 0, 1, 2, 3, 5, 6, 7, 8 }),
        // clicks kept as functions in an array, found by [data-act]: not yet seen in game. The second row's boxes (no id) are clicked in game only.
        // and after the last ADD, ROTATE then a row: the render where the rows' shapes change and ADD's text does not (seen in
        // game reading "ADD 0" there; the Lua sends nothing for it, as here)
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-acts.lua"), Steps("r0", "rot", "r0", "add", "ta", "r1", "tb", "add", "rot", "r0"),
         new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 }),
        // flex and grid items with attributes only known at run time, and a flex gap around rows of two shapes: not yet seen in game
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-flex.lua"), Steps(0.5, "tog", 0.5, "tog", "tog", 0.5, "tog"),
         new[] { 0, 1, 2, 3, 5, 6, 7 }),
        // a page whose only code is in onclick attributes: not yet seen in game
        (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-noscript.lua"), Steps("on", "box", "off", "box", "on"), new[] { 0, 1, 2, 3, 5 }),
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
        foreach (var (feature, page, steps, checkpoints) in Markups.Concat(Lists))
            foreach (var console in Consoles)
            {
                var name = $"{feature} on a {console.W:0}x{console.H:0} console";
                try { One(name, page, steps, checkpoints, check, console); }
                catch (Exception ex) { check(false, $"plain [{name}]: threw - {ex.Message.Split('\n')[0]}"); }
            }
        foreach (var (feature, page, steps, checkpoints) in Lookups)
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
        OldPathSurvives(check);
        Retired(check);
        Quiet(check);
        Eligibility(root, check);
        Sizes(check);
        HiddenRule(check);
        Gaps(check);
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
    /// A page the plain translator refuses falls back to the old path, which must never take the compile
    /// down: innerHTML with elements into a text-only element is left to the interpreter, said, and the
    /// plain translator's own reason is kept whatever the old path does.
    /// </summary>
    private static void OldPathSurvives(Action<bool, string> check)
    {
        var page = Page("", "<div id=\"status\">starting</div>",
                        "setTimeout(() => { document.getElementById('status').innerHTML = '<div class=\"x\">a</div><div>b</div>'; }, 10);" +
                        "requestAnimationFrame(() => {});");
        CompiledPage.Result compiled;
        try { compiled = Probe4.Headless(page).Compiled; }
        catch (Exception ex) { check(false, "plain fallback: the old path took the compile down - " + ex.Message.Split('\n')[0]); return; }
        var said = compiled.Warnings.FirstOrDefault(w => w.StartsWith("not translated to plain Lua:", StringComparison.Ordinal)) ?? "";
        var threw = compiled.Problems.FirstOrDefault(p => p.Contains("threw", StringComparison.Ordinal));
        check(!compiled.Plain && said.Contains("requestAnimationFrame", StringComparison.Ordinal) && threw == null,
              "plain fallback: markup with elements into a text-only element leaves the page on the old path, its plain reason said"
              + (threw != null ? " (" + threw + ")" : said.Length == 0 ? " (no plain reason)" : ""));
    }

    /// <summary>
    /// A page replaced after its hand-over (ChipHost.Retire sets its V_LIVE false): its timers stop,
    /// nothing more is sent, a click on its scene does nothing, and the tick it chained after still
    /// runs every time.
    /// </summary>
    /// <summary>
    /// A list written again with nothing changed sends nothing: rows whose shape depends on their item are
    /// hidden by the list's state and shown by their own within one write, which must not send them again.
    /// </summary>
    private static void Quiet(Action<bool, string> check)
    {
        var page = Page(".dev { display: flex; width: 240px; } .name { width: 120px; } .tag { width: 80px; }",
                        "<div id=\"devs\"></div><p>end</p>",
                        "const devices = [{ name: 'Pump', alarm: false, on: true }, { name: 'Fan', alarm: true, on: true }];" +
                        "function draw() {" +
                        "  document.getElementById('devs').innerHTML = devices.filter((d) => d.on).map((d) => d.alarm" +
                        "    ? `<div class=\"dev\"><span class=\"name\">${d.name}</span><span class=\"tag\">ALARM</span></div>`" +
                        "    : `<div class=\"dev\"><span class=\"name\">${d.name}</span><span class=\"tag\">ok</span></div>`).join('');" +
                        "}" +
                        "draw();" +
                        "setInterval(draw, 500);");
        var compiled = Probe4.Headless(page).Compiled;
        if (!compiled.Plain || compiled.Lua == null) { check(false, "plain quiet: the page does not compile plainly - " + string.Join("; ", compiled.Warnings.Take(2))); return; }
        var log = Probe4.DrivePlain(compiled.Lua, Ticks(3), out _);
        var sent = log.Where(l => l.StartsWith("set_props", StringComparison.Ordinal)).ToList();
        check(!log.Any(l => l.StartsWith("FAILED", StringComparison.Ordinal)) && sent.Count == 0,
              "plain quiet: a list written again with nothing changed sends nothing" + (sent.Count == 0 ? "" : " (" + sent[0] + ")"));
    }

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
            // no script, only onclick attributes: compiled like any page with one
            (System.IO.Path.Combine("ScriptedScreensHtml.Tests", "ingame", "plain-noscript.lua"), true),
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
        // what the surface runs, compiles or drives by data alone keys on: a script, handler attributes, or neither
        foreach (var (what, body, code) in new[]
                 {
                     ("only an onclick attribute", "<p id=\"a\" onclick=\"this.textContent = 'b'\">a</p>", true),
                     ("a script", "<p id=\"a\">a</p><script>var x = 1;</script>", true),
                     ("neither", "<p id=\"a\">a</p>", false),
                 })
        {
            var built = HtmlRenderer.Build(Head + "</style></head><body>" + body + "</body></html>", FontLibrary.Default());
            check(built.HasCode == code, $"plain: a page with {what} {(code ? "has" : "has no")} code of its own (got {built.HasCode})");
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
            ("a 768 viewport on a tall console whose screen is off, in whole pixels", 768, none, none, new(460, 1036), new(768, Mathf.Round(768 * 1036f / 460f))),
            ("a square console whose world aspect arrives a hair short: 460 whole pixels, not 459.9", 0, new(460, 460), new(1, 0.99978f), new(460, 460), new(460, 460)),
            ("a tall console whose world aspect arrives a hair short: 1036, not 1035.7", 0, new(460, 1036), new(1, 2.2515f), new(460, 1036), new(460, 1036)),
            ("nothing known: the canvas's 460 square, never the 64 floor", 0, none, none, none, new(460, 460)),
        };
        foreach (var (what, design, rect, world, pushed, want) in cases)
        {
            var got = PlainTranslator.ConsoleLayout(design, rect, world, pushed);
            // a browser window is whole CSS pixels: innerHeight reads what this is
            check(got.x == want.x && got.y == want.y, $"plain size: {what} is {want.x:0.###}x{want.y:0.###} (got {got.x:0.###}x{got.y:0.###})");
        }
    }

    /// <summary>The page as built: an element hidden by its attribute is not drawn, unless the page's own CSS gives it a display.</summary>
    /// <summary>
    /// gap as a browser lays it: between a flex container's items shown (one display: none leaves no gap behind, and
    /// none after the last), and nothing on a block's children.
    /// </summary>
    private static void Gaps(Action<bool, string> check)
    {
        ResolvedStyle.DefaultFace = FontLibrary.Default();
        var built = HtmlRenderer.Build(Page("#f { display: flex; flex-direction: column; gap: 10px; } #b { gap: 10px; } .i { height: 20px; }",
            "<div id=\"f\"><div id=\"f1\" class=\"i\"></div><div id=\"f2\" class=\"i\"></div><div id=\"f3\" class=\"i\"></div></div>" +
            "<div id=\"b\"><div id=\"b1\" class=\"i\"></div><div id=\"b2\" class=\"i\"></div></div>", ""), FontLibrary.Default());
        built.ById["f3"].style.display = DisplayStyle.None;
        new Panel(built.Root).Layout(460, 460);
        float Y(string id) => built.ById[id].layout.y;
        float H(string id) => built.ById[id].layout.height;
        check(Y("f2") - Y("f1") == 30 && H("f") == 50 && Y("b2") - Y("b1") == 20,
              $"plain gap: between the flex items shown and none on a block's children (f2 at +{Y("f2") - Y("f1")}, flex box {H("f")} high, b2 at +{Y("b2") - Y("b1")})");
    }

    private static void HiddenRule(Action<bool, string> check)
    {
        ResolvedStyle.DefaultFace = FontLibrary.Default();
        var built = HtmlRenderer.Build(Page("#tag { display: block; } .row { display: flex; }",
            "<p id=\"tag\" hidden>tag</p><div id=\"row\" class=\"row\" hidden>row</div><p id=\"gone\" hidden>gone</p><p id=\"shown\">shown</p>", ""), FontLibrary.Default());
        bool None(string id) => built.ById[id].style.display.value == DisplayStyle.None;
        check(!None("tag") && !None("row") && None("gone") && !None("shown"),
              $"plain hidden: [hidden] is display: none unless the page's own CSS gives a display (tag {None("tag")}, row {None("row")}, gone {None("gone")}, shown {None("shown")})");
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
        var script = Regex.Match(page, "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        var (compiled, _) = Probe4.Headless(page, out var built, out var panel, out var size, console: console);
        if (!compiled.Plain || compiled.Lua == null || compiled.Structure == null)
        {
            check(false, $"plain [{feature}]: does not compile plainly - {string.Join("; ", compiled.Warnings.Concat(compiled.Problems).Take(3))}");
            return;
        }
        SpecTests.PlainLua(feature, compiled.Lua, check);
        // markup is structure: what a write sends is a value or a state picked by one, never a string built for it
        if (script.Contains("innerHTML", StringComparison.Ordinal))
        {
            // and the compile lays it into the page only for as long as it compiles: the page is as it was built
            var fresh = HtmlRenderer.Build(page, FontLibrary.Default());
            // what the compile gives the page for good: a name for an element with no id that the script drives (`__div2`
            // is `div2` after it), and a hit region for an element a listener is on
            string Named(string k) => k.StartsWith("__", StringComparison.Ordinal) && !built.ById.ContainsKey(k) && built.ById.ContainsKey(k.Substring(2)) ? k.Substring(2) : k;
            var freshHtml = HtmlRenderer.ToHtml(fresh.Document!, outer: false, keepIds: true);
            foreach (var k in fresh.ById.Keys.Where(k => Named(k) != k)) freshHtml = freshHtml.Replace("id=\"" + k + "\"", "id=\"" + Named(k) + "\"");
            var same = HtmlRenderer.ToHtml(built.Document!, outer: false, keepIds: true).Replace(" data-click=\"1\"", "") == freshHtml.Replace(" data-click=\"1\"", "")
                       && built.ById.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(fresh.ById.Keys.Select(Named).OrderBy(k => k, StringComparer.Ordinal));
            check(same, $"plain [{feature}]: the page is put back as it was built after its markup compiles");
            var built2 = compiled.Lua.Split('\n').FirstOrDefault(l => Regex.IsMatch(l, @"v_(classname|state|setattr|set)\(.*(js_add\(\s*""|js_add\([^()]*,\s*""|\s\.\.\s)"));
            check(built2 == null, $"plain [{feature}]: markup writes send values, building no string" + (built2 == null ? "" : " (" + built2.Trim() + ")"));
        }
        // `PLAIN_DUMP=<words of a feature> PLAIN_DUMP_TO=<file>`: that case's Lua and scene, to read
        if (Environment.GetEnvironmentVariable("PLAIN_DUMP") is { Length: > 0 } dump && feature.Contains(dump, StringComparison.Ordinal)
            && Environment.GetEnvironmentVariable("PLAIN_DUMP_TO") is { Length: > 0 } to)
            System.IO.File.WriteAllText(to, compiled.Lua + "\n---- scene\n" + compiled.Structure);
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
            // markup the script writes: the browser's page is built again from its markup, so what names
            // and wraps the compile gave its elements is not compared, only what is drawn
            var diff = script.Contains("innerHTML", StringComparison.Ordinal) ? Compare(Drawn(mine), Drawn(theirs)) : Compare(mine, theirs);
            if (Environment.GetEnvironmentVariable("PLAIN_DUMP") is { Length: > 0 } d2 && feature.Contains(d2, StringComparison.Ordinal)
                && Environment.GetEnvironmentVariable("PLAIN_DUMP_TO") is { Length: > 0 } to2)
                System.IO.File.WriteAllText(to2 + "." + at.ToString(CultureInfo.InvariantCulture), mine + "\n---- browser\n" + theirs);
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
        // A script writing markup: the browser's document is kept as a tree of its own, from the page's
        // source, that the markup is parsed into; lookups search it, and the page is built again from it.
        var doc = script.Contains("innerHTML", StringComparison.Ordinal) ? new Doc(page) : null;
        Jint.Engine Load()
        {
            var engine = new Jint.Engine();
            if (doc != null)
            {
                doc = new Doc(page);
                engine.SetValue("__exists", new Func<string, bool>(id => doc.ById.ContainsKey(id)));
                engine.SetValue("__initialClass", new Func<string, string>(id => doc.ById[id].Attr("class") ?? ""));
                engine.SetValue("__initialText", new Func<string, string>(id => NodeText(doc.ById[id])));
                engine.SetValue("__initialAttrs", new Func<string, string>(id => JsonSerializer.Serialize(doc.Attributes(id))));
                engine.SetValue("__select", new Func<string, string?, string[]>((selector, under) => doc.Select(selector, under)));
                engine.SetValue("__rendered", new Func<string, string?, string>((id, text) => Rendered(built, page, id, text)));
                engine.SetValue("__chain", new Func<string, string[]>(id => doc.Chain(id)));
                engine.SetValue("__setHtml", new Func<string, string, string[]>((id, html) => doc.SetHtml(id, html)));
                engine.SetValue("__getHtml", new Func<string, string>(id => HtmlRenderer.ToHtml(doc.ById[id], outer: false, keepIds: true)));
                engine.SetValue("__closest", new Func<string, string, string?>((id, sel) => doc.Closest(id, sel)));
                engine.SetValue("__matches", new Func<string, string, bool>((id, sel) => Matches(doc.ById[id], sel)));
                engine.SetValue("__tag", new Func<string, string>(id => doc.ById[id].Tag!.ToUpperInvariant()));
            }
            else
            {
            engine.SetValue("__setHtml", new Func<string, string, string[]>((_, _) => throw new InvalidOperationException("innerHTML outside a markup page")));
            engine.SetValue("__getHtml", new Func<string, string>(_ => throw new InvalidOperationException("innerHTML outside a markup page")));
            engine.SetValue("__closest", new Func<string, string, string?>((id, sel) =>
            {
                for (var n = built.NodeOf[built.ById[id]]; n != null; n = n.Parent) if (!n.IsText && Matches(n, sel)) return n.Attr("id");
                return null;
            }));
            engine.SetValue("__matches", new Func<string, string, bool>((id, sel) => Matches(built.NodeOf[built.ById[id]], sel)));
            engine.SetValue("__tag", new Func<string, string>(id => built.NodeOf[built.ById[id]].Tag!.ToUpperInvariant()));
            engine.SetValue("__exists", new Func<string, bool>(id => built.ById.ContainsKey(id)));
            engine.SetValue("__initialClass", new Func<string, string>(id => built.NodeOf[built.ById[id]].Attr("class") ?? ""));
            engine.SetValue("__initialText", new Func<string, string>(id => SourceElement(page, id).Tag.Length > 0 ? SourceText(page, id) : NodeText(built.NodeOf[built.ById[id]])));
            engine.SetValue("__initialAttrs", new Func<string, string>(id => JsonSerializer.Serialize(SourceElement(page, id).Tag.Length > 0 ? SourceAttributes(page, id) : NodeAttributes(built.NodeOf[built.ById[id]]))));
            engine.SetValue("__select", new Func<string, string?, string[]>((selector, under) => Selected(built, selector, under)));
            engine.SetValue("__rendered", new Func<string, string?, string>((id, text) => Rendered(built, page, id, text)));
            engine.SetValue("__chain", new Func<string, string[]>(id =>
            {
                var chain = new List<string>();
                for (var ve = built.ById[id]; ve != null; ve = ve.parent) if (!string.IsNullOrEmpty(ve.name)) chain.Add(ve.name);
                return chain.ToArray();
            }));
            }
            engine.SetValue("__w", Math.Floor(size.x));
            engine.SetValue("__h", Math.Floor(size.y));
            engine.SetValue("__carryFrag", carry.Frag);
            engine.SetValue("__carryLocal", carry.Local);
            engine.SetValue("__carrySession", carry.Session);
            engine.SetValue("__carryNow", carry.Now);
            engine.Execute(Harness);
            var inline = doc != null
                ? doc.ById.Where(p => p.Value.Attr("onclick") != null).Select(p => (p.Key, p.Value.Attr("onclick")!))
                : built.NodeOf.Where(p => p.Value.Attr("onclick") != null).Select(p => (p.Key.name, p.Value.Attr("onclick")!));
            foreach (var (id, code) in inline.ToList()) engine.Invoke("__inline", id, code);
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
        if (doc != null) return doc.Drawn(final, size);

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
                    // a user-agent rule: a display the page's own CSS gives the element wins over it
                    if (hidden && !built.CssOf(ve).ContainsKey("display")) ve.style.display = DisplayStyle.None;
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

    /// <summary>
    /// The browser's document for a page whose script writes markup: parsed from the page's source, every
    /// element given an id (the page's own, or one of its own making), the markup parsed into it as it is
    /// written; at the end the script's final state applied to it and the whole page built again from it,
    /// laid out and emitted by the real engine.
    /// </summary>
    private sealed class Doc
    {
        public readonly HtmlNode Root;
        public readonly Dictionary<string, HtmlNode> ById = new(StringComparer.Ordinal);
        private int _made;

        public Doc(string page)
        {
            Root = HtmlParser.Parse(page, _ => { });
            Name(Root);
        }

        /// <summary>Names for the elements the page gives no id, kept off the nodes: an id changes how the renderer builds an inline element.</summary>
        private readonly Dictionary<HtmlNode, string> _names = new();

        private string? IdOf(HtmlNode n) => n.Attr("id") ?? (_names.TryGetValue(n, out var name) ? name : null);

        private void Name(HtmlNode n)
        {
            foreach (var c in n.Children)
            {
                if (c.IsText) continue;
                var id = c.Attr("id");
                if (id == null || ById.ContainsKey(id)) _names[c] = id = "__doc" + (++_made).ToString(CultureInfo.InvariantCulture);
                ById[id] = c;
                Name(c);
            }
        }

        public Dictionary<string, string> Attributes(string id) => NodeAttributes(ById[id]);

        /// <summary>element.closest(): the element or its nearest ancestor the selector matches, by id; null for none.</summary>
        public string? Closest(string id, string selector)
        {
            for (var n = ById[id]; n != null && n != Root; n = n.Parent)
                if (!n.IsText && Matches(n, selector)) return IdOf(n);
            return null;
        }

        public string[] Chain(string id)
        {
            var chain = new List<string>();
            for (var n = ById[id]; n != null; n = n.Parent) if (IdOf(n) is { } name) chain.Add(name);
            return chain.ToArray();
        }

        public string[] Select(string selector, string? under)
        {
            var parsed = CssParser.SplitTopLevel(selector, ',').Select(p => CssParser.ParseSelector(p.Trim(), _ => { })).Where(p => p != null).ToList();
            var found = new List<string>();
            void Walk(HtmlNode n)
            {
                foreach (var c in n.Children)
                {
                    if (!c.IsText && parsed.Any(p => p!.Matches(c)) && IdOf(c) is { } name) found.Add(name);
                    Walk(c);
                }
            }
            Walk(under != null ? ById[under] : Root);
            return found.ToArray();
        }

        /// <summary>innerHTML: the element's children replaced by the parsed markup. Returns the ids that went, whose listeners go with them.</summary>
        public string[] SetHtml(string id, string html)
        {
            var node = ById[id];
            var gone = new List<string>();
            void Forget(HtmlNode n) { foreach (var c in n.Children) { if (IdOf(c) is { } cid) { ById.Remove(cid); gone.Add(cid); } Forget(c); } }
            Forget(node);
            node.Children.Clear();
            foreach (var c in HtmlParser.Parse(html, _ => { }).Children) { c.Parent = node; node.Children.Add(c); }
            Name(node);
            return gone.ToArray();
        }

        /// <summary>The script's final state on the document, the page built again from it, laid out and emitted.</summary>
        public string Drawn(JsonElement final, Vector2 size)
        {
            foreach (var el in final.EnumerateObject())
            {
                if (!ById.TryGetValue(el.Name, out var node)) continue;
                if (el.Value.TryGetProperty("text", out var text))
                {
                    node.Children.Clear();
                    node.Children.Add(new HtmlNode { Text = text.GetString()!, Parent = node });
                }
                var declarations = el.Value.GetProperty("style").EnumerateObject().Select(p => DomSlots.Dashed(p.Name) + ":" + p.Value.GetString()).ToList();
                if (declarations.Count > 0) node.Attributes["style"] = (node.Attr("style") ?? "") + ";" + string.Join(";", declarations);
                if (el.Value.TryGetProperty("className", out var c)) node.Attributes["class"] = c.GetString()!;
                if (el.Value.TryGetProperty("attrs", out var attrs))
                {
                    var want = attrs.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
                    foreach (var name in node.Attributes.Keys.Where(k => k is not ("id" or "class" or "style")).Union(want.Keys).ToList())
                        if (want.TryGetValue(name, out var v)) node.Attributes[name] = v; else node.Attributes.Remove(name);
                }
                // what a click lands on: the renderer's hit region, as the compile gives an element with a listener
                if (el.Value.TryGetProperty("listens", out var l) && l.GetBoolean() && node.Tag != "button") node.Attributes["data-click"] = "1";
            }
            var html = HtmlRenderer.ToHtml(Root, outer: false, keepIds: true);
            ResolvedStyle.DefaultFace = FontLibrary.Default();
            var built = HtmlRenderer.Build(html, FontLibrary.Default());
            HtmlRenderer.NameDrivenGroups(built);
            var panel = new Panel(built.Root);
            foreach (var grid in built.Grids)
                if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
            PostLayout.Attach(built);
            // [hidden] { display: none }, a user-agent rule: a display the page's own CSS gives the element wins over it
            foreach (var pair in built.NodeOf)
                if (pair.Value.Attr("hidden") != null && !built.CssOf(pair.Key).ContainsKey("display")) pair.Key.style.display = DisplayStyle.None;
            panel.Layout(size.x, size.y);
            var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            var template = PageCompiler.Emitted(built, panel, values);
            return PlainTranslator.Literal(template, values, new List<string>());
        }
    }

    /// <summary>Whether a selector matches a node, by the renderer's own selector engine.</summary>
    private static bool Matches(HtmlNode n, string selector)
        => CssParser.SplitTopLevel(selector, ',').Select(p => CssParser.ParseSelector(p.Trim(), _ => { })).Any(p => p != null && p.Matches(n));

    /// <summary>A scene as it draws: no element names, and no group that only carries one (a name at full opacity).</summary>
    private static string Drawn(string scene)
    {
        // a zero radius is no radius: the compile keeps it for elements a script writes; and a hit region draws
        // nothing (what a click reaches is checked by clicking): an invisible box made to take clicks is not drawn
        var lines = scene.Split('\n')
            .Where(l => !Regex.IsMatch(l, @"^\s*R .* f=#00000001 .*click=1"))
            .Select(l => Regex.Replace(Regex.Replace(Regex.Replace(l, @" id=\S+", ""), @" rx=0(?= )", ""), @" click=1\b", "")).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            // a group that draws nothing different: full opacity, or a transform at identity
            if (!Regex.IsMatch(lines[i], @"^\s*G( o=1| a=\[[^\]]*\] t=\[0,0\] r=0 s=\[1,1\])? \{$")) continue;
            var depth = 0;
            for (var end = i; end < lines.Count; end++)
            {
                if (lines[end].TrimEnd().EndsWith("{", StringComparison.Ordinal)) depth++;
                if (lines[end].Trim() == "}" && --depth == 0) { lines.RemoveAt(end); break; }
            }
            lines.RemoveAt(i);
            i--;
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// The ids of the elements a selector matches, in document order, among the descendants of one element or
    /// the whole page. The match itself is the renderer's own selector engine: what is checked here is what the
    /// compile does with the elements it finds.
    /// </summary>
    private static string[] Selected(HtmlRenderer.Result built, string selector, string? under)
    {
        var parsed = CssParser.SplitTopLevel(selector, ',').Select(p => CssParser.ParseSelector(p.Trim(), _ => { })).Where(p => p != null).ToList();
        var top = under != null ? built.NodeOf[built.ById[under]] : built.NodeOf[built.Root];
        if (under == null) while (top.Parent != null) top = top.Parent;
        var found = new List<string>();
        void Walk(HtmlNode n)
        {
            foreach (var c in n.Children)
            {
                if (!c.IsText && parsed.Any(p => p!.Matches(c)) && c.Attr("id") is { } id && built.ById.ContainsKey(id)) found.Add(id);
                Walk(c);
            }
        }
        Walk(top);
        return found.ToArray();
    }

    /// <summary>innerText as a browser gives it: the text (as written, or the source's with `&lt;br&gt;` a line break), each run of white space one space, a block's ends trimmed, text-transform applied.</summary>
    private static string Rendered(HtmlRenderer.Result built, string page, string id, string? text)
    {
        var ve = built.ById[id];
        if (text == null)
        {
            var inner = SourceElement(page, id).Inner;
            text = System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(inner, @"<br\s*/?>", "\u0001", RegexOptions.IgnoreCase), "<[^>]*>", ""));
        }
        text = Regex.Replace(text, "[ \t\n\r\f]+", " ");
        text = Regex.Replace(text, " ?\u0001 ?", "\n");
        var inline = built.NodeOf[ve].Tag is "span" or "b" or "i" or "em" or "strong" or "a" or "code" or "small";
        if (!inline) text = text.Trim(' ');
        var css = built.CssOf(ve);
        var transform = css.TryGetValue("text-transform", out var t) ? t.Trim().ToLowerInvariant() : "none";
        return transform == "uppercase" ? text.ToUpperInvariant() : transform == "lowercase" ? text.ToLowerInvariant() : text;
    }

    /// <summary>textContent of an element the source gives no id: every text under its node.</summary>
    private static string NodeText(HtmlNode n) => n.IsText ? (n.Raw != null ? HtmlParser.DecodeEntities(n.Raw) : n.Text) : string.Concat(n.Children.Select(NodeText));

    /// <summary>The attributes of an element the source gives no id, as its node holds them, but for id, class and style.</summary>
    private static Dictionary<string, string> NodeAttributes(HtmlNode n)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        if (n.AttributeCount > 0)
            foreach (var pair in n.Attributes)
                if (pair.Key.ToLowerInvariant() is not ("id" or "class" or "style" or "data-click")) found[pair.Key.ToLowerInvariant()] = pair.Value;
        if (n.Attr("style") is { } st) found["\u0001style"] = st;
        return found;
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
        // a guard the emitter puts round a number is not compared; a tag guarded to print as text is
        static string Clean(string s) => s.Replace("<noparse><</noparse>", "&lt;").Replace("<noparse>", "").Replace("</noparse>", "");
        var a = Clean(mine).Split('\n');
        var b = Clean(theirs).Split('\n');
        if (a.Length != b.Length) return $"{a.Length} lines against {b.Length}";
        // a quoted value (`text="a  b"`) is one token, spaces and all: a text that differs only in its spaces differs
        var token = new Regex("[^ \"]*\"(?:\\\\.|[^\"\\\\])*\"|[^ ]+");
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
var __els = {}, __state = {}, __listeners = {}, __onclick = {}, __handler = {};
function __el(id) {
  if (__els[id]) return __els[id];
  if (!__exists(id)) return null;
  var st = __state[id] = { className: __initialClass(id), style: {} };
  function words() { return st.className.split(/\s+/).filter(function (w, i, all) { return w.length && all.indexOf(w) === i; }); }
  var cl = {
    add: function () { var w = words(); for (var i = 0; i < arguments.length; i++) if (w.indexOf(arguments[i]) < 0) w.push(arguments[i]); st.className = w.join(' '); st.classWritten = true; },
    remove: function () { var w = words(); for (var i = 0; i < arguments.length; i++) { var k = w.indexOf(arguments[i]); if (k >= 0) w.splice(k, 1); } st.className = w.join(' '); st.classWritten = true; },
    toggle: function (c, force) { var w = words(), has = w.indexOf(c) >= 0, want = force === undefined ? !has : !!force;
      if (want && !has) w.push(c); if (!want && has) w.splice(w.indexOf(c), 1); st.className = w.join(' '); st.classWritten = true; return want; }
  };
  cl.contains = function (c) { return words().indexOf(c) >= 0; };
  cl.item = function (i) { var w = words(); i = Math.floor(Number(i)) || 0; return i >= 0 && i < w.length ? w[i] : null; };
  Object.defineProperty(cl, 'length', { get: function () { return words().length; } });
  Object.defineProperty(cl, 'value', { get: function () { return st.className; }, set: function (v) { st.className = String(v); st.classWritten = true; } });
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
    get innerText() { return __rendered(id, st.text !== undefined ? st.text : null); }, set innerText(v) { st.text = String(v); },
    get innerHTML() { return __getHtml(id); },
    set innerHTML(v) {
      delete st.text;
      var gone = __setHtml(id, String(v));
      for (var gi = 0; gi < gone.length; gi++) { var g = gone[gi]; delete __els[g]; delete __state[g]; delete __listeners[g]; delete __onclick[g]; }
    },
    querySelector: function (s) { var ids = __select(s, id); return ids.length ? __el(ids[0]) : null; },
    closest: function (s) { var r = __closest(id, s); return r === null ? null : __el(r); },
    matches: function (s) { return __matches(id, s); },
    get tagName() { return __tag(id); },
    querySelectorAll: function (s) { return __list(__select(s, id), false); },
    getElementsByClassName: function (c) { return __list(__select(__classes(c), id), true); },
    getElementsByTagName: function (t) { return __list(__select(t, id), true); },
    get className() { return st.className; }, set className(v) { st.className = String(v); st.classWritten = true; },
    get onclick() { return __onclick[id] || null; },
    // an event handler joins the listeners where it is first set, and leaves them when set to null
    set onclick(f) {
      var l = __listeners[id] || (__listeners[id] = []);
      if (typeof f !== 'function') f = null;
      if (!f && __onclick[id]) l.splice(l.indexOf(__handler), 1); else if (f && !__onclick[id]) l.push(__handler);
      __onclick[id] = f;
    },
    addEventListener: function (type, fn) { if (type !== 'click') return; var l = __listeners[id] || (__listeners[id] = []); if (l.indexOf(fn) < 0) l.push(fn); }
  };
  __els[id] = e;
  return e;
}
// what a lookup by selector gives: a NodeList (with forEach), or a live HTMLCollection (without)
function __list(ids, collection) {
  var a = ids.map(__el), l = {};
  for (var i = 0; i < a.length; i++) l[i] = a[i];
  l.length = a.length;
  l.item = function (i) { i = Math.floor(Number(i)) || 0; return i >= 0 && i < a.length ? a[i] : null; };
  if (!collection) l.forEach = function (f) { for (var i = 0; i < a.length; i++) f(a[i], i, l); };
  l[Symbol.iterator] = function () { return a[Symbol.iterator](); };
  return l;
}
function __classes(c) { return String(c).trim().split(/\s+/).map(function (n) { return '.' + n; }).join(''); }
var document = {
  getElementById: __el,
  querySelector: function (s) { var ids = __select(s, null); return ids.length ? __el(ids[0]) : null; },
  querySelectorAll: function (s) { return __list(__select(s, null), false); },
  getElementsByClassName: function (c) { return __list(__select(__classes(c), null), true); },
  getElementsByTagName: function (t) { return __list(__select(t, null), true); }
};
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
  var ev = { target: __el(id), currentTarget: null };
  for (var c = 0; c < chain.length; c++) {
    ev.currentTarget = __el(chain[c]);
    var l = (__listeners[chain[c]] || []).slice();
    for (var i = 0; i < l.length; i++) { var f = l[i] === __handler ? __onclick[chain[c]] : l[i]; if (f) f.call(ev.currentTarget, ev); }
  }
}
// an onclick attribute: the element's handler before any script runs, a function of event with the element as this
function __inline(id, code) { __el(id).onclick = new Function('event', code); }
function __final() {
  var out = {};
  for (var id in __state) {
    var st = __state[id], o = { style: st.style };
    if (st.text !== undefined) o.text = st.text;
    if (st.classWritten) o.className = st.className;
    if (st.attrWritten) o.attrs = st.attrs;
    if ((__listeners[id] && __listeners[id].length) || __onclick[id]) o.listens = true;
    out[id] = o;
  }
  return out;
}
";
}

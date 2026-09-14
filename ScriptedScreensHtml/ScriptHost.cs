using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Jint;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// Runs a page's <c>&lt;script&gt;</c> in Jint ON ITS OWN THREAD, like the vector layer's
/// tessellator: an interpreter costs tens of milliseconds a frame for a page that redraws
/// canvases, and that must not be main-thread time. The main thread hands in frame times
/// and data payloads, and takes out DOM writes and canvas command lists through queues.
/// Reads the script needs (element sizes, attributes) come from snapshots the main thread
/// refreshes each frame. If a script frame is slower than the game's, frames are skipped
/// and the page animates at the rate it can afford, on its own core.
///
/// Browser surface: console, timers, requestAnimationFrame, document.getElementById and
/// querySelectorAll, elements with style/textContent/innerHTML/className/dataset, and a
/// 2D canvas context. The DOM shim is JavaScript (<see cref="Prelude"/>) over C# callbacks.
/// </summary>
internal sealed class ScriptHost : IDisposable
{
    private readonly Func<string, VisualElement?> _find;
    private readonly Func<string, SvgShape?> _findShape;
    private readonly Func<string, HtmlNode?> _findNode;
    private readonly Func<string, List<string>> _query;
    private readonly Action<VisualElement, string> _setClass;
    private readonly List<CssRule> _rules;
    private readonly Action<string> _warn;
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly ConcurrentQueue<Action> _toEngine = new();
    private readonly ConcurrentQueue<Action> _toMain = new();
    private readonly ConcurrentDictionary<string, (float w, float h)> _sizes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _attrCache = new(StringComparer.Ordinal);
    private volatile bool _stop;
    private volatile bool _dead;
    private volatile bool _frameRequested;
    private volatile bool _hasDataHandler;
    private volatile bool _hasPendingWork;
    private volatile bool _busy;
    private volatile float _frameNow;
    private float _lastFrameAt = -1f;
    private const float MinFrameInterval = 1f / 30f;   // the layer's animation floor, as for the vector mod
    private Engine? _engine;   // worker thread only

    public bool HasDataHandler => _hasDataHandler;
    public bool HasPendingWork => _hasPendingWork;

    /// <summary>Script time of the last completed frame, milliseconds, for diagnostics.</summary>
    public volatile float LastFrameMs;

    private readonly Action<string, string> _appendHtml;
    private readonly Action<string> _remove;

    public ScriptHost(Func<string, VisualElement?> find, Func<string, SvgShape?> findShape, Func<string, HtmlNode?> findNode,
        Func<string, List<string>> query, Action<VisualElement, string> setClass, List<CssRule> rules, Action<string> warn,
        Action<string, string> appendHtml, Action<string> remove)
    {
        _appendHtml = appendHtml;
        _remove = remove;
        _find = find;
        _findShape = findShape;
        _findNode = findNode;
        _query = query;
        _setClass = setClass;
        _rules = rules;
        _warn = warn;

        _thread = new Thread(Worker) { IsBackground = true, Name = "html script" };
        _thread.Start();
    }

    // ---------------- main-thread API ----------------

    /// <summary>Queue the page script. Runs after any earlier work, before later data.</summary>
    public void Run(string script)
    {
        _toEngine.Enqueue(() =>
        {
            if (HtmlConfig.Diagnostics) ScriptedScreensHtmlPlugin.Log?.LogInfo($"js: running page script ({script.Length} chars)");
            _engine!.Execute(script);
            AfterRun();
            if (HtmlConfig.Diagnostics) ScriptedScreensHtmlPlugin.Log?.LogInfo($"js: page script done, data handler {_hasDataHandler}, pending work {_hasPendingWork}");
        });
        _wake.Set();
    }

    /// <summary>
    /// Deliver a chip payload. If the page has a data handler it gets a `data` event;
    /// otherwise the fallback runs on the main thread. Ordered after Run by the queue.
    /// </summary>
    public void EmitData(string json)
    {
        _toEngine.Enqueue(() =>
        {
            if (_hasDataHandler)
            {
                _engine!.Invoke("__emit", "data", json);
                AfterRun();
            }
        });
        _wake.Set();
    }

    /// <summary>Main thread, once per frame: refresh what the script may read, ask for a frame, apply what came back.</summary>
    public bool Frame(float nowSeconds, Dictionary<string, VisualElement> elements)
    {
        if (_dead)
            return false;
        Snapshot(elements);
        if (!_busy && nowSeconds - _lastFrameAt >= MinFrameInterval)
        {
            _lastFrameAt = nowSeconds;
            _frameNow = nowSeconds;
            _frameRequested = true;
            _wake.Set();
        }
        return Pump();
    }

    /// <summary>Apply queued DOM writes and canvas frames. Main thread. Returns whether anything was applied.</summary>
    public bool Pump()
    {
        var any = false;
        while (_toMain.TryDequeue(out var a))
        {
            any = true;
            try { a(); }
            catch (Exception ex) { Report("js: apply: " + ex.Message); }
        }
        return any;
    }

    /// <summary>Run everything queued plus one frame and wait for it (capture path). Main thread.</summary>
    public void RunSynchronously(float nowSeconds, Dictionary<string, VisualElement> elements, int timeoutMs)
    {
        if (_dead)
            return;
        Snapshot(elements);
        using var done = new ManualResetEventSlim(false);
        _toEngine.Enqueue(() =>
        {
            RunFrame(nowSeconds);
            done.Set();
        });
        _wake.Set();
        done.Wait(timeoutMs);
        Pump();
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        // The engine is disposed by the worker on its way out; wait briefly, then release the event.
        _thread.Join(500);
        _wake.Dispose();
    }

    private void Snapshot(Dictionary<string, VisualElement> elements)
    {
        foreach (var kv in elements)
        {
            var r = kv.Value.contentRect;
            if (!float.IsNaN(r.width) && !float.IsNaN(r.height))
                _sizes[kv.Key] = (r.width, r.height);
        }
    }

    // ---------------- worker thread ----------------

    private void Worker()
    {
        try
        {
            _engine = new Engine(o =>
            {
                o.LimitRecursion(200);
                o.TimeoutInterval(TimeSpan.FromSeconds(2)); // a runaway script frame must not wedge the worker
                o.Strict(false);
            });
            _engine.SetValue("__log", new Action<string, string>(Log));
            _engine.SetValue("__has", new Func<string, bool>(id => _find(id) != null || _findShape(id) != null));
            _engine.SetValue("__query", new Func<string, string[]>(sel => _query(sel).ToArray()));
            _engine.SetValue("__setStyle", new Action<string, string, string>(SetStyle));
            _engine.SetValue("__setText", new Action<string, string>(SetText));
            _engine.SetValue("__setHtml", new Action<string, string>(SetHtml));
            _engine.SetValue("__setClass", new Action<string, string>(SetClass));
            _engine.SetValue("__getAttr", new Func<string, string, string?>(GetAttr));
            _engine.SetValue("__setAttr", new Action<string, string, string>(SetAttr));
            _engine.SetValue("__appendHtml", new Action<string, string>((parent, html) => _toMain.Enqueue(() => _appendHtml(parent, html))));
            _engine.SetValue("__remove", new Action<string>(id => _toMain.Enqueue(() => _remove(id))));
            _engine.SetValue("__size", new Func<string, double[]>(Size));
            _engine.SetValue("__canvasFrame", new Action<string, double[], string[], int>(CanvasFrame));
            _engine.SetValue("__now", new Func<double>(() => _frameNow * 1000.0));
            _engine.Execute(Prelude);
            if (HtmlConfig.Diagnostics) ScriptedScreensHtmlPlugin.Log?.LogInfo("js: engine ready (worker thread)");
        }
        catch (Exception ex)
        {
            _warn("js: engine failed to start: " + ex);
            _dead = true;
            return;
        }

        while (!_stop)
        {
            _wake.WaitOne(250);
            if (_stop)
                break;
            try
            {
                while (_toEngine.TryDequeue(out var work))
                    work();
                if (_frameRequested)
                {
                    _frameRequested = false;
                    RunFrame(_frameNow);
                }
            }
            catch (Exception ex)
            {
                Report("js: " + ex.Message);
            }
        }
        try { _engine?.Dispose(); } catch (Exception) { }
    }

    private void RunFrame(float nowSeconds)
    {
        _busy = true;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var pending = _engine!.Invoke("__tick", nowSeconds * 1000.0);
            _hasPendingWork = pending.IsBoolean() && pending.AsBoolean();
        }
        finally
        {
            LastFrameMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            _busy = false;
        }
    }

    private void AfterRun()
    {
        var has = _engine!.Invoke("__hasDataHandler");
        _hasDataHandler = has.IsBoolean() && has.AsBoolean();
        var pending = _engine.Invoke("__hasPending");
        _hasPendingWork = pending.IsBoolean() && pending.AsBoolean();
    }

    // ---- callbacks from the script (worker thread): reads answer from snapshots, writes queue to main ----

    private void Log(string level, string msg)
    {
        if (level == "error" || level == "warn")
            ScriptedScreensHtmlPlugin.Log?.LogWarning("js: " + msg);
        else
            ScriptedScreensHtmlPlugin.Log?.LogInfo("js: " + msg);
    }

    private void SetStyle(string id, string prop, string value)
    {
        var sb = new StringBuilder(prop.Length + 4);
        foreach (var ch in prop)
        {
            if (char.IsUpper(ch)) { sb.Append('-'); sb.Append(char.ToLowerInvariant(ch)); }
            else sb.Append(ch);
        }
        var css = sb.ToString();
        _toMain.Enqueue(() =>
        {
            var ve = _find(id);
            if (ve != null)
                StyleApplier.Apply(ve, new CssDeclaration(css, value), Report);
        });
    }

    private void SetText(string id, string text)
    {
        _toMain.Enqueue(() =>
        {
            var ve = _find(id);
            var label = ve != null ? HtmlSurface.TextTargetFor(ve, id) : null;
            if (label != null)
                label.text = text;
        });
    }

    private void SetHtml(string id, string html)
    {
        // Parse on the worker (pure managed), assign on main.
        var rich = HtmlRenderer.FragmentToRichText(html, _findNode(id), _rules);
        _toMain.Enqueue(() =>
        {
            var ve = _find(id);
            var label = ve != null ? HtmlSurface.TextTargetFor(ve, id) : null;
            if (label != null)
                label.text = rich;
        });
    }

    private void SetClass(string id, string cls)
    {
        _toMain.Enqueue(() =>
        {
            var ve = _find(id);
            if (ve != null)
                _setClass(ve, cls);
        });
    }

    private string? GetAttr(string id, string name)
    {
        if (_attrCache.TryGetValue(id + "\n" + name, out var cached))
            return cached;
        var shape = _findShape(id);
        if (shape != null)
            return shape.Attr(name);
        return _findNode(id)?.Attr(name);
    }

    private void SetAttr(string id, string name, string value)
    {
        _attrCache[id + "\n" + name] = value;
        _toMain.Enqueue(() =>
        {
            var shape = _findShape(id);
            if (shape != null)
            {
                shape.Set(name, value);
                return;
            }
            var ve = _find(id);
            if (ve is CanvasElement cv)
            {
                if (name == "width" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) { cv.CanvasWidth = w; cv.MarkDirtyRepaint(); }
                else if (name == "height" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) { cv.CanvasHeight = h; cv.MarkDirtyRepaint(); }
                return;
            }
            var node = _findNode(id);
            if (node != null)
                node.Attributes[name] = value;
        });
    }

    private double[] Size(string id)
    {
        return _sizes.TryGetValue(id, out var s) ? new[] { (double)s.w, (double)s.h } : new[] { 0.0, 0.0 };
    }

    private void CanvasFrame(string id, double[] cmds, string[] colours, int count)
    {
        var f = new float[count];
        for (var i = 0; i < count; i++)
            f[i] = (float)cmds[i];
        var cols = new List<string>(colours);
        _toMain.Enqueue(() =>
        {
            if (_find(id) is CanvasElement cv)
                cv.SetFrame(f, count, cols);
        });
    }

    private void Report(string message)
    {
        if (_reported.Add(message))
            _warn(message);
    }

    /// <summary>The browser-ish surface, in JavaScript, over the callbacks above.</summary>
    private const string Prelude = @"
var window = globalThis;
var console = {
  log: function(){ __log('log', Array.prototype.slice.call(arguments).join(' ')); },
  warn: function(){ __log('warn', Array.prototype.slice.call(arguments).join(' ')); },
  error: function(){ __log('error', Array.prototype.slice.call(arguments).join(' ')); },
  info: function(){ __log('log', Array.prototype.slice.call(arguments).join(' ')); }
};
var performance = { now: function(){ return __now(); } };

// ---- timers and animation frames ----
var __timers = [], __rafs = [], __nextId = 1, __now_ms = 0;
function setTimeout(fn, ms){ var id = __nextId++; __timers.push({id:id, fn:fn, at:__now_ms + (ms||0), every:0, args:Array.prototype.slice.call(arguments,2)}); return id; }
function setInterval(fn, ms){ var id = __nextId++; __timers.push({id:id, fn:fn, at:__now_ms + (ms||0), every:Math.max(1, ms||1), args:Array.prototype.slice.call(arguments,2)}); return id; }
function clearTimeout(id){ __timers = __timers.filter(function(t){ return t.id !== id; }); }
var clearInterval = clearTimeout;
function requestAnimationFrame(fn){ var id = __nextId++; __rafs.push({id:id, fn:fn}); return id; }
function cancelAnimationFrame(id){ __rafs = __rafs.filter(function(r){ return r.id !== id; }); }
function __hasPending(){ return __rafs.length > 0 || __timers.length > 0; }
function __tick(now){
  __now_ms = now;
  var rafs = __rafs; __rafs = [];
  for (var i = 0; i < rafs.length; i++) { try { rafs[i].fn(now); } catch (e) { console.error(String(e && e.stack || e)); } }
  var due = __timers.filter(function(t){ return t.at <= now; });
  for (var j = 0; j < due.length; j++) {
    var t = due[j];
    if (t.every) t.at = now + t.every; else __timers = __timers.filter(function(x){ return x !== t; });
    try { t.fn.apply(null, t.args); } catch (e) { console.error(String(e && e.stack || e)); }
  }
  __flushCanvases();
  return __hasPending();
}

// ---- events ----
var __listeners = {};
function addEventListener(type, fn){ (__listeners[type] = __listeners[type] || []).push(fn); }
function removeEventListener(type, fn){ if (__listeners[type]) __listeners[type] = __listeners[type].filter(function(f){ return f !== fn; }); }
function __hasDataHandler(){ return !!(window.ondata) || !!(__listeners['data'] && __listeners['data'].length); }
function __emit(type, json){
  var detail = json ? JSON.parse(json) : null;
  var ev = { type: type, detail: detail };
  if (type === 'data' && typeof window.ondata === 'function') { try { window.ondata(detail, ev); } catch (e) { console.error(String(e && e.stack || e)); } }
  var ls = __listeners[type] || [];
  for (var i = 0; i < ls.length; i++) { try { ls[i](ev); } catch (e) { console.error(String(e && e.stack || e)); } }
  __flushCanvases();
}

// ---- canvas 2D context recorder (op codes match CanvasElement) ----
var __canvases = {};
function __ctx(id){
  if (__canvases[id]) return __canvases[id];
  var c = { __id:id, __cmds:[], __cols:[], __colIdx:{}, fillStyle:'#000', strokeStyle:'#000', lineWidth:1, lineCap:'butt', lineJoin:'miter', globalAlpha:1 };
  function col(s){ s = String(s); var i = c.__colIdx[s]; if (i === undefined) { i = c.__cols.length; c.__cols.push(s); c.__colIdx[s] = i; } return i; }
  c.beginPath = function(){ c.__cmds.push(0); };
  c.moveTo = function(x,y){ c.__cmds.push(1,x,y); };
  c.lineTo = function(x,y){ c.__cmds.push(2,x,y); };
  c.quadraticCurveTo = function(cx,cy,x,y){ c.__cmds.push(3,cx,cy,x,y); };
  c.bezierCurveTo = function(a,b,d,e,x,y){ c.__cmds.push(4,a,b,d,e,x,y); };
  c.arc = function(x,y,r,a0,a1,ccw){ c.__cmds.push(5,x,y,r,a0,a1,ccw?1:0); };
  c.arcTo = function(x1,y1,x2,y2,r){ c.__cmds.push(10,x1,y1,x2,y2,r); };
  c.rect = function(x,y,w,h){ c.__cmds.push(11,x,y,w,h); };
  c.closePath = function(){ c.__cmds.push(6); };
  c.fill = function(){ c.__cmds.push(7, col(c.fillStyle), c.globalAlpha); };
  c.stroke = function(){ c.__cmds.push(8, col(c.strokeStyle), c.globalAlpha, c.lineWidth, c.lineCap==='round'?2:c.lineCap==='square'?1:0, c.lineJoin==='round'?2:c.lineJoin==='bevel'?1:0); };
  c.fillRect = function(x,y,w,h){ c.__cmds.push(9,x,y,w,h, col(c.fillStyle), c.globalAlpha); };
  c.clearRect = function(){ c.__cmds.length = 0; c.__cols.length = 0; c.__colIdx = {}; };
  c.save = function(){}; c.restore = function(){};
  c.__flush = function(){ __canvasFrame(id, c.__cmds, c.__cols, c.__cmds.length); c.__cmds = []; c.__cols = []; c.__colIdx = {}; };
  __canvases[id] = c;
  return c;
}
function __flushCanvases(){ for (var k in __canvases) { var c = __canvases[k]; if (c.__cmds.length) c.__flush(); } }

// ---- elements ----
function __el(id){
  var el = {
    id: id,
    get style(){ return new Proxy({}, { set: function(o, p, v){ __setStyle(id, String(p), String(v)); return true; }, get: function(){ return ''; } }); },
    set textContent(v){ __setText(id, String(v)); }, get textContent(){ return ''; },
    set innerText(v){ __setText(id, String(v)); },
    set innerHTML(v){ __setHtml(id, String(v)); }, get innerHTML(){ return ''; },
    set className(v){ __setClass(id, String(v)); },
    classList: { add: function(){ }, remove: function(){ } },
    get clientWidth(){ return __size(id)[0]; }, get clientHeight(){ return __size(id)[1]; },
    get offsetWidth(){ return __size(id)[0]; }, get offsetHeight(){ return __size(id)[1]; },
    get width(){ return Number(__getAttr(id,'width')) || 0; }, set width(v){ __setAttr(id,'width',String(v)); },
    get height(){ return Number(__getAttr(id,'height')) || 0; }, set height(v){ __setAttr(id,'height',String(v)); },
    get dataset(){ return new Proxy({}, { get: function(o, p){ return __getAttr(id, 'data-' + String(p).replace(/[A-Z]/g, function(m){ return '-' + m.toLowerCase(); })); } }); },
    getAttribute: function(n){ return __getAttr(id, n); },
    setAttribute: function(n, v){ __setAttr(id, n, String(v)); },
    getContext: function(){ return __ctx(id); },
    addEventListener: function(){ },
    get tagName(){ return String(__getAttr(id, '__tag') || 'DIV').toUpperCase(); },
    appendChild: function(c){ __appendHtml(id, __serialize(c)); if (c.__adopt) c.__adopt(); return c; },
    append: function(){ for (var i = 0; i < arguments.length; i++) { var c = arguments[i]; if (typeof c === 'string') __appendHtml(id, __escape(c)); else el.appendChild(c); } },
    removeChild: function(c){ __remove(c.id); return c; },
    remove: function(){ __remove(id); }
  };
  el.classList = {
    __list: function(){ return String(__getAttr(id, 'class') || '').split(/\s+/).filter(Boolean); },
    add: function(){ var l = el.classList.__list(); for (var i = 0; i < arguments.length; i++) if (l.indexOf(arguments[i]) < 0) l.push(arguments[i]); __setClass(id, l.join(' ')); },
    remove: function(){ var l = el.classList.__list(); for (var i = 0; i < arguments.length; i++) l = l.filter(function(x){ return x !== arguments[i]; }.bind(null)); var drop = Array.prototype.slice.call(arguments); __setClass(id, el.classList.__list().filter(function(x){ return drop.indexOf(x) < 0; }).join(' ')); },
    toggle: function(c, force){ var l = el.classList.__list(); var has = l.indexOf(c) >= 0; var want = force === undefined ? !has : !!force; if (want && !has) l.push(c); if (!want && has) l = l.filter(function(x){ return x !== c; }); __setClass(id, l.join(' ')); return want; },
    contains: function(c){ return el.classList.__list().indexOf(c) >= 0; }
  };
  return el;
}
// ---- DOM creation: an element is built detached, serialised to HTML when it is appended
// to a live one, and forwards its writes by id from then on ----
var __jsSeq = 0;
function __escape(s){ return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/""/g, '&quot;'); }
function __kebab(p){ return String(p).replace(/[A-Z]/g, function(m){ return '-' + m.toLowerCase(); }); }
function __serialize(c){
  if (!c.__tag) return __escape(String(c.textContent || ''));
  if (!c.id) c.id = '__js' + (++__jsSeq);
  var a = ' id=""' + c.id + '""';
  if (c.className) a += ' class=""' + __escape(c.className) + '""';
  for (var k in c.__attrs) a += ' ' + k + '=""' + __escape(c.__attrs[k]) + '""';
  var st = '';
  for (var p in c.__style) st += __kebab(p) + ':' + c.__style[p] + ';';
  if (st) a += ' style=""' + __escape(st) + '""';
  var inner = c.__text !== null ? __escape(c.__text) : (c.__html !== null ? c.__html : c.__children.map(__serialize).join(''));
  return '<' + c.__tag + a + '>' + inner + '</' + c.__tag + '>';
}
function __detached(tag){
  var o = { __tag: String(tag).toLowerCase(), __attrs: {}, __style: {}, __children: [], __html: null, __text: null, __live: false, id: '', className: '' };
  o.tagName = o.__tag.toUpperCase();
  o.setAttribute = function(n, v){ if (o.__live) __setAttr(o.id, n, String(v)); else o.__attrs[n] = String(v); };
  o.getAttribute = function(n){ return o.__live ? __getAttr(o.id, n) : (o.__attrs[n] === undefined ? null : o.__attrs[n]); };
  o.appendChild = function(c){ if (o.__live) { __appendHtml(o.id, __serialize(c)); if (c.__adopt) c.__adopt(); } else o.__children.push(c); return c; };
  o.append = function(){ for (var i = 0; i < arguments.length; i++) { var c = arguments[i]; o.appendChild(typeof c === 'string' ? { textContent: c } : c); } };
  o.removeChild = function(c){ if (o.__live) __remove(c.id); else o.__children = o.__children.filter(function(x){ return x !== c; }); return c; };
  o.remove = function(){ if (o.__live) __remove(o.id); };
  o.addEventListener = function(){ };
  o.style = new Proxy({}, { set: function(t, p, v){ if (o.__live) __setStyle(o.id, String(p), String(v)); else o.__style[p] = String(v); return true; }, get: function(t, p){ return o.__style[p] || ''; } });
  Object.defineProperty(o, 'innerHTML', { set: function(v){ if (o.__live) __setHtml(o.id, String(v)); else { o.__html = String(v); o.__text = null; } }, get: function(){ return o.__html || ''; } });
  Object.defineProperty(o, 'textContent', { set: function(v){ if (o.__live) __setText(o.id, String(v)); else { o.__text = String(v); o.__html = null; } }, get: function(){ return o.__text || ''; } });
  Object.defineProperty(o, 'innerText', { set: function(v){ o.textContent = v; }, get: function(){ return o.__text || ''; } });
  o.__adopt = function(){ o.__live = true; o.__children.forEach(function(c){ if (c.__adopt) c.__adopt(); }); };
  return o;
}
var document = {
  getElementById: function(id){ return __has(id) ? __el(id) : null; },
  querySelectorAll: function(sel){ return __query(sel).map(__el); },
  querySelector: function(sel){ var r = __query(sel); return r.length ? __el(r[0]) : null; },
  createElement: __detached,
  createTextNode: function(t){ return { textContent: String(t) }; },
  addEventListener: addEventListener,
  body: __el('body')
};
";
}

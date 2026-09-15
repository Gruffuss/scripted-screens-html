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
    /// <summary>Set while the main thread has drained every queued write; a tree read on the worker waits for it (DOM writes read back synchronously, as in a browser).</summary>
    private readonly ManualResetEventSlim _drained = new(true);

    private void Write(Action a)
    {
        _drained.Reset();
        _toMain.Enqueue(a);
    }

    /// <summary>Before a tree read on the worker: wait (up to a frame or two) for pending writes to land on the main thread.</summary>
    private void Sync()
    {
        if (!_toMain.IsEmpty) _drained.Wait(250);
    }
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
    private readonly Action<string, string> _setValue;
    private readonly Action<string> _wantClicks;
    private readonly Action<string, string, string> _insertHtml;
    private readonly Action<string, float> _setScroll;
    /// <summary>The page's built result and a re-emit hook, for attribute writes that change layout (dialog/details `open`).</summary>
    private HtmlRenderer.Result? _built;
    private Action? _onLayoutAttr;
    private UnityEngine.Vector2 _viewport = new(460f, 460f);
    private Func<VisualElement, CssKeyframes, AnimationSpec, int>? _animate;
    private Action<int>? _cancelAnimation;
    public void Attach(HtmlRenderer.Result built, Action onLayoutAttr, UnityEngine.Vector2 viewport, Func<VisualElement, CssKeyframes, AnimationSpec, int> animate, Action<int> cancelAnimation)
    {
        _built = built; _onLayoutAttr = onLayoutAttr; _viewport = viewport; _animate = animate; _cancelAnimation = cancelAnimation;
    }

    /// <summary>The cascaded value of a property on an element, for getComputedStyle and style read-back.</summary>
    private string CssOf(string id, string prop)
    {
        var ve = _find(id);
        return ve != null && _built != null && _built.CssOf(ve).TryGetValue(prop, out var v) ? v : string.Empty;
    }

    /// <summary>Element.animate: frames as "pct|prop:val;prop:val" strings, options as "durationMs,delayMs,iterations,direction,easing,fill". Returns a handle.</summary>
    private int Animate(string id, string[] frames, string options)
    {
        var kf = new CssKeyframes { Name = "js" };
        foreach (var f in frames)
        {
            var bar = f.IndexOf('|');
            if (bar < 0) continue;
            var frame = new CssKeyframe { Percent = float.TryParse(f.Substring(0, bar), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0f };
            frame.Declarations.AddRange(CssParser.ParseDeclarations(f.Substring(bar + 1)));
            kf.Frames.Add(frame);
        }
        kf.Frames.Sort((a, b) => a.Percent.CompareTo(b.Percent));
        var o = options.Split(',');
        var spec = new AnimationSpec { Name = "js" };
        if (o.Length > 0 && float.TryParse(o[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var dur)) spec.Duration = dur / 1000f;
        if (o.Length > 1 && float.TryParse(o[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var delay)) spec.Delay = delay / 1000f;
        if (o.Length > 2) { var t = 0; spec.ApplyToken(o[2] == "Infinity" ? "infinite" : o[2], ref t); }
        if (o.Length > 3 && o[3].Length > 0) { var t = 0; spec.ApplyToken(o[3], ref t); }
        if (o.Length > 4 && o[4].Length > 0) { var t = 0; spec.ApplyToken(o[4], ref t); }
        if (o.Length > 5 && o[5].Length > 0) { var t = 0; spec.ApplyToken(o[5], ref t); }
        var handle = 0;
        var ve = _find(id);
        if (ve == null || _animate == null) return 0;
        using var doneEvent = new System.Threading.ManualResetEventSlim(false);
        Write(() => { handle = _animate(ve, kf, spec); doneEvent.Set(); });
        return doneEvent.Wait(50) ? handle : -1; // ponytail: the handle is needed synchronously; the main thread answers within a frame
    }
    /// <summary>Layout rects per id, relative to the page, refreshed every frame for getBoundingClientRect.</summary>
    private readonly ConcurrentDictionary<string, (float x, float y, float w, float h)> _rects = new(StringComparer.Ordinal);

    public ScriptHost(Func<string, VisualElement?> find, Func<string, SvgShape?> findShape, Func<string, HtmlNode?> findNode,
        Func<string, List<string>> query, Action<VisualElement, string> setClass, List<CssRule> rules, Action<string> warn,
        Action<string, string> appendHtml, Action<string> remove, Action<string, string> setValue, Action<string> wantClicks,
        Action<string, float> setScroll, Action<string, string, string> insertHtml)
    {
        _insertHtml = insertHtml;
        _setScroll = setScroll;
        _appendHtml = appendHtml;
        _remove = remove;
        _setValue = setValue;
        _wantClicks = wantClicks;
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
            _engine.Invoke("__ready");
            AfterRun();
            if (HtmlConfig.Diagnostics) ScriptedScreensHtmlPlugin.Log?.LogInfo($"js: page script done, data handler {_hasDataHandler}, pending work {_hasPendingWork}");
        });
        _wake.Set();
    }

    /// <summary>
    /// Deliver a chip payload. If the page has a data handler it gets a `data` event;
    /// otherwise the fallback runs on the main thread. Ordered after Run by the queue.
    /// </summary>
    /// <summary>A click on a page element: listeners, `onclick`, and an inline onclick attribute run.</summary>
    public void EmitClick(string id, float x, float y)
    {
        _toEngine.Enqueue(() =>
        {
            _engine!.Invoke("__click", id, (double)x, (double)y);
            AfterRun();
        });
        _wake.Set();
    }

    /// <summary>mouseover/mouseout/mousemove/mousedown/mouseup on an element, with page coordinates. Only queued when the script listens for that type.</summary>
    public void EmitPointer(string id, string type, float x, float y)
    {
        if (!_pointerTypes.Contains(type)) return;
        _toEngine.Enqueue(() =>
        {
            _engine!.Invoke("__pointer", id, type, (double)x, (double)y);
            AfterRun();
        });
        _wake.Set();
    }

    private readonly HashSet<string> _pointerTypes = new(StringComparer.Ordinal);

    /// <summary>A plain event on an element: `submit` on a form, `toggle` on a details.</summary>
    public void EmitEvent(string id, string type)
    {
        _toEngine.Enqueue(() =>
        {
            _engine!.Invoke("__fire", id, type, null);
            AfterRun();
        });
        _wake.Set();
    }

    /// <summary>A control changed (user or ScriptedScreens): the element gets `input` and `change` events and its value.</summary>
    public void EmitInput(string id, string value)
    {
        _toEngine.Enqueue(() =>
        {
            _engine!.Invoke("__input", id, value);
            AfterRun();
        });
        _wake.Set();
    }

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
        if (_toMain.IsEmpty) _drained.Set();
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
        var until = Environment.TickCount + timeoutMs;
        while (!done.Wait(5) && Environment.TickCount < until) Pump();
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
        var origin = elements.TryGetValue("body", out var body) ? body.worldBound.position : UnityEngine.Vector2.zero;
        foreach (var kv in elements)
        {
            var r = kv.Value.contentRect;
            if (!float.IsNaN(r.width) && !float.IsNaN(r.height))
                _sizes[kv.Key] = (r.width, r.height);
            var wb = kv.Value.worldBound;
            if (!float.IsNaN(wb.width) && !float.IsNaN(wb.height))
                _rects[kv.Key] = (wb.x - origin.x, wb.y - origin.y, wb.width, wb.height);
        }
    }

    // ---- reads over the node tree, on the worker; the tree is written on the main thread and read here ----
    private string TextOf(string id) => _findNode(id) is { } n ? Text(n).Trim() : string.Empty;
    private static string Text(HtmlNode n)
    {
        if (n.IsText) return n.Text;
        var sb = new System.Text.StringBuilder();
        foreach (var c in n.Children) if (c.Attr("data-pseudo") == null && c.Attr("data-marker") == null) sb.Append(Text(c));
        return sb.ToString();
    }
    private string HtmlOf(string id, bool outer) => _findNode(id) is { } n ? HtmlRenderer.ToHtml(n, outer) : string.Empty;
    private string[] ChildrenOf(string id)
    {
        var list = new List<string>();
        if (_findNode(id) is { } n)
            foreach (var c in n.Children)
                if (!c.IsText && c.Attr("data-pseudo") == null && c.Attr("data-marker") == null && c.Attr("id") is { } cid) list.Add(cid);
        return list.ToArray();
    }
    private string? ParentOf(string id) => _findNode(id)?.Parent?.Attr("id");
    private string[] AttrsOf(string id)
    {
        var list = new List<string>();
        if (_findNode(id) is { } n)
            foreach (var kv in n.Attributes)
                if (kv.Key != "id" && !kv.Key.StartsWith("data-listed", StringComparison.Ordinal) && kv.Key != "data-control" && kv.Key != "data-pseudo") { list.Add(kv.Key); list.Add(kv.Value); }
        return list.ToArray();
    }
    private bool Contains(string ancestorId, string id)
    {
        for (var n = _findNode(id)?.Parent; n != null; n = n.Parent)
            if (n.Attr("id") == ancestorId) return true;
        return false;
    }
    /// <summary>The innermost element (smallest box) whose layout rect contains the page point.</summary>
    private string? ElementAt(double x, double y)
    {
        string? best = null;
        var bestArea = float.MaxValue;
        foreach (var kv in _rects)
        {
            var r = kv.Value;
            if (x < r.x || y < r.y || x > r.x + r.w || y > r.y + r.h) continue;
            var area = r.w * r.h;
            if (area < bestArea) { bestArea = area; best = kv.Key; }
        }
        return best;
    }

    private double[] RectOf(string id)
    {
        return _rects.TryGetValue(id, out var r) ? new[] { (double)r.x, (double)r.y, (double)r.w, (double)r.h } : new[] { 0.0, 0.0, 0.0, 0.0 };
    }

    // ---------------- worker thread ----------------

    private void Worker()
    {
        try
        {
            _engine = new Engine(o =>
            {
                o.LimitRecursion(200);
                o.TimeoutInterval(TimeSpan.FromSeconds(15)); // a runaway script frame must not wedge the worker; generous because a game load stalls every thread for seconds
                o.Strict(false);
            });
            _engine.SetValue("__log", new Action<string, string>(Log));
            _engine.SetValue("__has", new Func<string, bool>(id => { Sync(); return _find(id) != null || _findShape(id) != null; }));
            _engine.SetValue("__query", new Func<string, string[]>(sel => { Sync(); return _query(sel).ToArray(); }));
            _engine.SetValue("__setStyle", new Action<string, string, string>(SetStyle));
            _engine.SetValue("__setText", new Action<string, string>(SetText));
            _engine.SetValue("__setHtml", new Action<string, string>(SetHtml));
            _engine.SetValue("__setClass", new Action<string, string>(SetClass));
            _engine.SetValue("__getAttr", new Func<string, string, string?>(GetAttr));
            _engine.SetValue("__setAttr", new Action<string, string, string>(SetAttr));
            _engine.SetValue("__appendHtml", new Action<string, string>((parent, html) => Write(() => _appendHtml(parent, html))));
            _engine.SetValue("__remove", new Action<string>(id => Write(() => _remove(id))));
            _engine.SetValue("__setValue", new Action<string, string>((id, v) => Write(() => _setValue(id, v))));
            _engine.SetValue("__wantClicks", new Action<string>(id => Write(() => _wantClicks(id))));
            _engine.SetValue("__wantPointer", new Action<string>(type => _pointerTypes.Add(type)));
            _engine.SetValue("__cssOf", new Func<string, string, string>(CssOf));
            _engine.SetValue("__setScroll", new Action<string, double>((id, off) => Write(() => _setScroll(id, (float)off))));
            _engine.SetValue("__scrollBox", new Func<string, string?>(id =>
            {
                // the nearest ancestor (or the element itself) that scrolls: overflow auto/scroll
                for (var cur = id; cur != null; cur = ParentOf(cur))
                {
                    var o = CssOf(cur, "overflow"); var oy = CssOf(cur, "overflow-y");
                    if (o.Trim() is "auto" or "scroll" || oy.Trim() is "auto" or "scroll") return cur;
                }
                return null;
            }));
            _engine.SetValue("__viewport", new Func<double[]>(() => new[] { (double)_viewport.x, (double)_viewport.y }));
            _engine.SetValue("__media", new Func<string, bool>(CssParser.MediaMatches));
            _engine.SetValue("__animate", new Func<string, string[], string, int>(Animate));
            _engine.SetValue("__cancelAnimation", new Action<int>(h => Write(() => _cancelAnimation?.Invoke(h))));
            _engine.SetValue("__children_rects", new Func<string, double[]>(id =>
            {
                // the content extent below and right of an element's own top-left: scrollWidth / scrollHeight
                var right = 0.0; var bottom = 0.0;
                var own = RectOf(id);
                foreach (var c in ChildrenOf(id)) { var r = RectOf(c); right = Math.Max(right, r[0] + r[2] - own[0]); bottom = Math.Max(bottom, r[1] + r[3] - own[1]); }
                return new[] { Math.Max(right, own[2]), Math.Max(bottom, own[3]) };
            }));
            _engine.SetValue("__textOf", new Func<string, string>(id => { Sync(); return TextOf(id); }));
            _engine.SetValue("__htmlOf", new Func<string, bool, string>((id, outer) => { Sync(); return HtmlOf(id, outer); }));
            _engine.SetValue("__children", new Func<string, string[]>(id => { Sync(); return ChildrenOf(id); }));
            _engine.SetValue("__parent", new Func<string, string?>(id => { Sync(); return ParentOf(id); }));
            _engine.SetValue("__attrs", new Func<string, string[]>(id => { Sync(); return AttrsOf(id); }));
            _engine.SetValue("__contains", new Func<string, string, bool>((a, b) => { Sync(); return Contains(a, b); }));
            _engine.SetValue("__rect", new Func<string, double[]>(RectOf));
            _engine.SetValue("__elementAt", new Func<double, double, string?>(ElementAt));
            _engine.SetValue("__insertHtml", new Action<string, string, string>((parent, html, before) => Write(() => _insertHtml(parent, html, before))));
            _engine.SetValue("__removeAttr", new Action<string, string>((id, name) => { _attrCache.TryRemove(id + "\n" + name, out _); Write(() => { var n = _findNode(id); if (n != null && n.Attributes.Remove(name)) AfterAttribute(id, n, name); }); }));
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
        Write(() =>
        {
            var ve = _find(id);
            if (ve != null)
                StyleApplier.Apply(ve, new CssDeclaration(css, value), Report);
        });
    }

    private void SetText(string id, string text)
    {
        Write(() =>
        {
            var ve = _find(id);
            var label = ve != null ? HtmlSurface.TextTargetFor(ve, id) : null;
            if (label != null)
                label.text = text;
            if (_findNode(id) is { } node && !node.IsText)
            {
                node.Children.Clear();
                node.Children.Add(new HtmlNode { Text = text, Parent = node });
            }
        });
    }

    private void SetHtml(string id, string html)
    {
        // Parse on the worker (pure managed), assign on main. Inline-only markup becomes the
        // element's rich text; anything with real elements (blocks, ids, classes) replaces
        // the element's children as built elements, the way appendChild does.
        var parsed = HtmlParser.Parse(html, _ => { });
        var inlineOnly = HtmlRenderer.IsInlineOnly(parsed);
        var rich = inlineOnly ? HtmlRenderer.FragmentToRichText(html, _findNode(id), _rules) : string.Empty;
        Write(() =>
        {
            var ve = _find(id);
            var node = _findNode(id);
            // whatever elements were inside go, with their ids and layout
            if (node != null)
                foreach (var c in node.Children.ToArray())
                    if (!c.IsText && c.Attr("id") is { } cid && cid != id) _remove(cid);
            var label = ve != null ? HtmlSurface.TextTargetFor(ve, id) : null;
            if (label != null)
                label.text = rich;
            if (node != null && !node.IsText)
            {
                node.Children.Clear();
                if (inlineOnly && label != null)
                    foreach (var c in HtmlParser.Parse(html, _ => { }).Children) { c.Parent = node; node.Children.Add(c); }
            }
            // built elements, or inline text on a container that has no label of its own
            if (html.Trim().Length > 0 && (!inlineOnly || label == null))
                _appendHtml(id, html);
        });
    }

    private void SetClass(string id, string cls)
    {
        _attrCache[id + "\n" + "class"] = cls;
        Write(() =>
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
        Sync();
        var shape = _findShape(id);
        if (shape != null)
            return shape.Attr(name);
        return _findNode(id)?.Attr(name);
    }

    private void SetAttr(string id, string name, string value)
    {
        _attrCache[id + "\n" + name] = value;
        Write(() =>
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
            {
                node.Attributes[name] = value;
                AfterAttribute(id, node, name);
            }
        });
    }

    /// <summary>Attributes whose presence is layout: `open` shows a dialog or a details' content.</summary>
    private void AfterAttribute(string id, HtmlNode node, string name)
    {
        if (name != "open") return;
        var ve = _find(id);
        if (ve == null) return;
        if (node.Tag == "dialog")
            ve.style.display = node.Attr("open") != null ? DisplayStyle.Flex : DisplayStyle.None;
        else if (node.Tag == "details" && _built != null)
            HtmlRenderer.ShowDetails(ve, node, _built);
        _onLayoutAttr?.Invoke();
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
        Write(() =>
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

// ---- browser globals a page script may touch; none of them do anything here ----
function __storage(){
  var m = {};
  return {
    getItem: function(k){ return Object.prototype.hasOwnProperty.call(m, k) ? m[k] : null; },
    setItem: function(k, v){ m[k] = String(v); },
    removeItem: function(k){ delete m[k]; },
    clear: function(){ m = {}; },
    key: function(i){ return Object.keys(m)[i] || null; },
    get length(){ return Object.keys(m).length; }
  };
}
var localStorage = __storage(), sessionStorage = __storage();
function alert(m){ __log('log', 'alert: ' + m); }
function confirm(m){ __log('log', 'confirm: ' + m); return true; }
function prompt(m){ __log('log', 'prompt: ' + m); return null; }
var location = { href: 'about:page', protocol: 'about:', host: '', hostname: '', port: '', pathname: '/page', search: '', hash: '', origin: 'null',
                 reload: function(){}, assign: function(){}, replace: function(){}, toString: function(){ return this.href; } };
var navigator = { userAgent: 'ScriptedScreensHtml', language: 'en', languages: ['en'], onLine: false, platform: 'stationeers', clipboard: { writeText: function(){ return Promise.resolve(); } } };
var history = { length: 1, pushState: function(){}, replaceState: function(){}, back: function(){}, forward: function(){} };

// ---- timers and animation frames ----
function __errText(e){ if (!e) return String(e); var head = e.message !== undefined ? (e.name || 'Error') + ': ' + e.message : String(e); return e.stack ? head + String.fromCharCode(10) + e.stack : head; }
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
  for (var i = 0; i < rafs.length; i++) { try { rafs[i].fn(now); } catch (e) { console.error(__errText(e)); } }
  var due = __timers.filter(function(t){ return t.at <= now; });
  for (var j = 0; j < due.length; j++) {
    var t = due[j];
    if (t.every) t.at = now + t.every; else __timers = __timers.filter(function(x){ return x !== t; });
    try { t.fn.apply(null, t.args); } catch (e) { console.error(__errText(e)); }
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
  if (type === 'data' && typeof window.ondata === 'function') { try { window.ondata(detail, ev); } catch (e) { console.error(__errText(e)); } }
  var ls = __listeners[type] || [];
  for (var i = 0; i < ls.length; i++) { try { ls[i](ev); } catch (e) { console.error(__errText(e)); } }
  __flushCanvases();
}

// ---- canvas 2D context recorder (op codes match CanvasElement and VectorEmitter.EmitCanvas) ----
// Every call is a number list; strings (colours, gradients, text, fonts, image sources) go
// through a per-frame string table. The frame is flushed to the main thread after each
// script run; the emitter turns it into vector nodes.
var __canvases = {};
function __ctx(id){
  if (__canvases[id]) return __canvases[id];
  var c = { __id:id, __cmds:[], __cols:[], __colIdx:{}, fillStyle:'#000', strokeStyle:'#000', lineWidth:1, lineCap:'butt', lineJoin:'miter', miterLimit:10, globalAlpha:1,
            font:'10px sans-serif', textAlign:'start', textBaseline:'alphabetic', shadowColor:'rgba(0,0,0,0)', shadowBlur:0, shadowOffsetX:0, shadowOffsetY:0,
            globalCompositeOperation:'source-over', lineDashOffset:0, imageSmoothingEnabled:true, direction:'ltr', filter:'none' };
  c.canvas = { get width(){ return Number(__getAttr(id, 'width')) || 300; }, get height(){ return Number(__getAttr(id, 'height')) || 150; }, id: id,
               getContext: function(){ return c; }, toDataURL: function(){ return ''; }, get clientWidth(){ return __size(id)[0]; }, get clientHeight(){ return __size(id)[1]; } };
  function key(s){ s = String(s); var i = c.__colIdx[s]; if (i === undefined) { i = c.__cols.length; c.__cols.push(s); c.__colIdx[s] = i; } return i; }
  function paint(st){ return key(st && st.__key ? st.__key() : st); }
  function shadowKey(){ return (c.shadowBlur > 0 || c.shadowOffsetX || c.shadowOffsetY) ? key('SH|' + c.shadowOffsetX + '|' + c.shadowOffsetY + '|' + c.shadowBlur + '|' + c.shadowColor) : -1; }
  var __dash = [];
  c.beginPath = function(){ c.__cmds.push(0); };
  c.moveTo = function(x,y){ c.__cmds.push(1,x,y); };
  c.lineTo = function(x,y){ c.__cmds.push(2,x,y); };
  c.quadraticCurveTo = function(cx,cy,x,y){ c.__cmds.push(3,cx,cy,x,y); };
  c.bezierCurveTo = function(a,b,d,e,x,y){ c.__cmds.push(4,a,b,d,e,x,y); };
  c.arc = function(x,y,r,a0,a1,ccw){ c.__cmds.push(5,x,y,r,a0,a1,ccw?1:0); };
  c.closePath = function(){ c.__cmds.push(6); };
  c.fill = function(rule){ if (typeof rule === 'string') c.__cmds.push(30, key(rule)); c.__cmds.push(29, shadowKey()); c.__cmds.push(7, paint(c.fillStyle), c.globalAlpha); };
  c.stroke = function(){ c.__cmds.push(29, shadowKey()); c.__cmds.push(8, paint(c.strokeStyle), c.globalAlpha, c.lineWidth, c.lineCap==='round'?2:c.lineCap==='square'?1:0, c.lineJoin==='round'?2:c.lineJoin==='bevel'?1:0); };
  c.fillRect = function(x,y,w,h){ c.__cmds.push(29, shadowKey()); c.__cmds.push(9,x,y,w,h, paint(c.fillStyle), c.globalAlpha); };
  c.arcTo = function(x1,y1,x2,y2,r){ c.__cmds.push(10,x1,y1,x2,y2,r); };
  c.rect = function(x,y,w,h){ c.__cmds.push(11,x,y,w,h); };
  c.strokeRect = function(x,y,w,h){ c.__cmds.push(12,x,y,w,h, paint(c.strokeStyle), c.globalAlpha, c.lineWidth, c.lineCap==='round'?2:c.lineCap==='square'?1:0, c.lineJoin==='round'?2:c.lineJoin==='bevel'?1:0); };
  c.fillText = function(t,x,y,mw){ c.__cmds.push(13, key(t), x, y, mw === undefined ? -1 : mw, key(c.font), key(c.textAlign), key(c.textBaseline), paint(c.fillStyle), c.globalAlpha, shadowKey()); };
  c.strokeText = function(t,x,y,mw){ c.__cmds.push(14, key(t), x, y, mw === undefined ? -1 : mw, key(c.font), key(c.textAlign), key(c.textBaseline), paint(c.strokeStyle), c.globalAlpha, c.lineWidth); };
  c.translate = function(x,y){ c.__cmds.push(15,x,y); };
  c.rotate = function(a){ c.__cmds.push(16,a); };
  c.scale = function(x,y){ c.__cmds.push(17,x,y === undefined ? x : y); };
  c.setTransform = function(a,b,d,e,f,g){ if (a && typeof a === 'object') { c.__cmds.push(18,a.a,a.b,a.c,a.d,a.e,a.f); } else c.__cmds.push(18,a,b,d,e,f,g); };
  c.transform = function(a,b,d,e,f,g){ c.__cmds.push(19,a,b,d,e,f,g); };
  c.resetTransform = function(){ c.__cmds.push(20); };
  c.getTransform = function(){ return { a:1, b:0, c:0, d:1, e:0, f:0 }; };
  c.save = function(){ c.__cmds.push(21); };
  c.restore = function(){ c.__cmds.push(22); };
  c.clip = function(rule){ c.__cmds.push(23, key(typeof rule === 'string' ? rule : 'nonzero')); };
  c.ellipse = function(x,y,rx,ry,rot,a0,a1,ccw){ c.__cmds.push(24,x,y,rx,ry,rot||0,a0,a1,ccw?1:0); };
  c.drawImage = function(img){
    var src = img && (img.src || (img.getAttribute && img.getAttribute('src')) || (img.id && __getAttr(img.id, 'src'))) || '';
    var a = arguments;
    var dx, dy, dw, dh;
    if (a.length >= 9) { dx = a[5]; dy = a[6]; dw = a[7]; dh = a[8]; } // ponytail: the source crop is ignored
    else { dx = a[1]; dy = a[2]; dw = a.length > 3 ? a[3] : (img.naturalWidth || img.width || 0); dh = a.length > 4 ? a[4] : (img.naturalHeight || img.height || 0); }
    c.__cmds.push(25, key(src), dx, dy, dw, dh, c.globalAlpha);
  };
  c.setLineDash = function(seg){ __dash = seg ? Array.prototype.slice.call(seg) : []; c.__cmds.push(26, key(__dash.join(','))); };
  c.getLineDash = function(){ return __dash.slice(); };
  c.roundRect = function(x,y,w,h,r){ var rr = Array.isArray(r) ? r[0] : (r || 0); c.__cmds.push(27,x,y,w,h,rr); };
  c.clearRect = function(x,y,w,h){ var cw = c.canvas.width, ch = c.canvas.height; if (x <= 0 && y <= 0 && w >= cw && h >= ch) { c.__cmds.length = 0; c.__cols.length = 0; c.__colIdx = {}; } else c.__cmds.push(28,x,y,w,h); };
  c.measureText = function(t){ var m = /(\d+(?:\.\d+)?)px/.exec(String(c.font)); var size = m ? parseFloat(m[1]) : 10; var w = String(t).length * size * 0.55;
    return { width: w, actualBoundingBoxAscent: size * 0.8, actualBoundingBoxDescent: size * 0.2, actualBoundingBoxLeft: 0, actualBoundingBoxRight: w, fontBoundingBoxAscent: size * 0.9, fontBoundingBoxDescent: size * 0.25 }; };
  c.createLinearGradient = function(x0,y0,x1,y1){ var g = { __stops: [], addColorStop: function(o, col){ g.__stops.push(o + ':' + col); return g; }, __key: function(){ return 'GL|' + x0 + '|' + y0 + '|' + x1 + '|' + y1 + '|' + g.__stops.join(';'); } }; return g; };
  c.createRadialGradient = function(x0,y0,r0,x1,y1,r1){ var g = { __stops: [], addColorStop: function(o, col){ g.__stops.push(o + ':' + col); return g; }, __key: function(){ return 'GR|' + x0 + '|' + y0 + '|' + r0 + '|' + x1 + '|' + y1 + '|' + r1 + '|' + g.__stops.join(';'); } }; return g; };
  c.createConicGradient = function(a,x,y){ var g = { __stops: [], addColorStop: function(o, col){ g.__stops.push(o + ':' + col); return g; }, __key: function(){ return 'GC|' + x + '|' + y + '|' + a + '|' + g.__stops.join(';'); } }; return g; };
  c.createPattern = function(){ return '#808080'; };
  c.isPointInPath = function(){ return false; }; c.isPointInStroke = function(){ return false; };
  c.getImageData = function(){ console.warn('canvas: getImageData is not available in a geometry layer'); return { data: new Uint8ClampedArray(0), width: 0, height: 0 }; };
  c.putImageData = function(){ console.warn('canvas: putImageData is not available in a geometry layer'); };
  c.createImageData = function(w,h){ return { data: new Uint8ClampedArray(0), width: w, height: h }; };
  c.__flush = function(){ __canvasFrame(id, c.__cmds, c.__cols, c.__cmds.length); c.__cmds = []; c.__cols = []; c.__colIdx = {}; };
  __canvases[id] = c;
  return c;
}
function __flushCanvases(){ for (var k in __canvases) { var c = __canvases[k]; if (c.__cmds.length) c.__flush(); } }

var __textCache = {}, __htmlCache = {}, __styleCache = {}, __scrollCache = {};
// ---- readiness: after the page script, as a browser fires them after parsing ----
document_readyState = 'loading';
function __ready(){
  if (document_readyState === 'complete') return; // a later script (external, module) does not reload the page
  document_readyState = 'interactive';
  __emit('DOMContentLoaded', null);
  document_readyState = 'complete';
  __emit('load', null);
  if (typeof window.onload === 'function') { try { window.onload({ type: 'load' }); } catch (e) { console.error(__errText(e)); } }
}
// ---- Event constructors and dispatch ----
function Event(type, init){ this.type = type; init = init || {}; this.bubbles = !!init.bubbles; this.cancelable = !!init.cancelable; this.defaultPrevented = false; this.detail = init.detail === undefined ? null : init.detail; }
Event.prototype.preventDefault = function(){ this.defaultPrevented = true; };
Event.prototype.stopPropagation = function(){ this.__stop = true; };
Event.prototype.stopImmediatePropagation = function(){ this.__stop = true; };
function CustomEvent(type, init){ Event.call(this, type, init); }
CustomEvent.prototype = Object.create(Event.prototype);
var FocusEvent = Event; // MouseEvent, KeyboardEvent, PointerEvent and InputEvent are defined below with their fields
function __dispatchOn(id, ev){
  // listeners on the element, then its ancestors (bubbling), then document/window
  ev.target = ev.target || __el(id);
  var cur = id;
  while (cur) {
    ev.currentTarget = __el(cur);
    var fns = (__elListeners[cur] || {})[ev.type] || [];
    for (var i = 0; i < fns.length; i++) { try { fns[i].call(ev.currentTarget, ev); } catch (e) { console.error(__errText(e)); } if (ev.__stop) return ev; }
    var h = (__elHandlers[cur] || {})['on' + ev.type];
    if (typeof h === 'function') { try { h.call(ev.currentTarget, ev); } catch (e) { console.error(__errText(e)); } if (ev.__stop) return ev; }
    var code = __getAttr(cur, 'on' + ev.type);
    if (code) { try { (new Function('event', code)).call(ev.currentTarget, ev); } catch (e) { console.error('on' + ev.type + ' of #' + cur + ': ' + __errText(e)); } if (ev.__stop) return ev; }
    if (ev.bubbles === false && cur === id) break;
    cur = __parent(cur);
  }
  var gl = __listeners[ev.type] || [];
  for (var j = 0; j < gl.length; j++) { try { gl[j](ev); } catch (e) { console.error(__errText(e)); } if (ev.__stop) break; }
  return ev;
}
function URLSearchParams(init){
  var self = this; this.__p = [];
  function add(k, v){ self.__p.push([String(k), String(v)]); }
  if (typeof init === 'string') { init.replace(/^\?/, '').split('&').forEach(function(kv){ if (!kv) return; var i = kv.indexOf('='); add(decodeURIComponent(i < 0 ? kv : kv.slice(0, i)), decodeURIComponent(i < 0 ? '' : kv.slice(i + 1)).replace(/\+/g, ' ')); }); }
  else if (init && typeof init === 'object') { for (var k in init) add(k, init[k]); }
  this.get = function(k){ for (var i = 0; i < self.__p.length; i++) if (self.__p[i][0] === k) return self.__p[i][1]; return null; };
  this.getAll = function(k){ return self.__p.filter(function(p){ return p[0] === k; }).map(function(p){ return p[1]; }); };
  this.has = function(k){ return self.get(k) !== null; };
  this.set = function(k, v){ self.delete(k); add(k, v); };
  this.append = add;
  this.delete = function(k){ self.__p = self.__p.filter(function(p){ return p[0] !== k; }); };
  this.forEach = function(fn){ self.__p.forEach(function(p){ fn(p[1], p[0]); }); };
  this.toString = function(){ return self.__p.map(function(p){ return encodeURIComponent(p[0]) + '=' + encodeURIComponent(p[1]); }).join('&'); };
}
function URL(href, base){
  var m = /^([a-z][a-z0-9+.-]*:)?(?:\/\/([^\/?#]*))?([^?#]*)(\?[^#]*)?(#.*)?$/i.exec(String(href)) || [];
  this.href = String(href); this.protocol = m[1] || ''; this.host = m[2] || ''; this.hostname = this.host.split(':')[0]; this.port = this.host.split(':')[1] || '';
  this.pathname = m[3] || '/'; this.search = m[4] || ''; this.hash = m[5] || ''; this.origin = this.protocol + '//' + this.host;
  this.searchParams = new URLSearchParams(this.search);
  this.toString = function(){ return this.href; };
}
function TextEncoder(){ this.encode = function(s){ s = unescape(encodeURIComponent(String(s))); var a = new Uint8Array(s.length); for (var i = 0; i < s.length; i++) a[i] = s.charCodeAt(i); return a; }; }
function TextDecoder(){ this.decode = function(a){ var s = ''; for (var i = 0; i < a.length; i++) s += String.fromCharCode(a[i]); try { return decodeURIComponent(escape(s)); } catch (e) { return s; } }; }
function structuredClone(v){ return JSON.parse(JSON.stringify(v)); }
function queueMicrotask(fn){ Promise.resolve().then(fn); }
var crypto = { randomUUID: function(){ return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c){ var r = Math.random() * 16 | 0; return (c === 'x' ? r : (r & 3 | 8)).toString(16); }); },
               getRandomValues: function(a){ for (var i = 0; i < a.length; i++) a[i] = Math.random() * 256 | 0; return a; } };
console.table = function(d){ console.log(JSON.stringify(d)); }; console.group = function(l){ console.log(l || ''); }; console.groupEnd = function(){}; console.debug = console.log;
console.time = function(){}; console.timeEnd = function(){}; console.assert = function(c, m){ if (!c) console.error('assert: ' + (m || '')); }; console.trace = function(){}; console.dir = console.log;
function Image(){ var o = __detached('img'); var self = o; Object.defineProperty(o, 'src', { set: function(v){ o.setAttribute('src', String(v)); if (o.__live) __setAttr(o.id, 'src', String(v)); setTimeout(function(){ if (typeof self.onload === 'function') self.onload({ type: 'load', target: self }); }, 0); }, get: function(){ return o.getAttribute('src') || ''; } }); o.complete = true; o.naturalWidth = 0; o.naturalHeight = 0; return o; }
function Audio(src){ var o = __detached('audio'); if (src) o.setAttribute('src', String(src));
  o.play = function(){ o.setAttribute('autoplay', ''); if (!o.__live) document.body.appendChild(o); return Promise.resolve(); };
  o.pause = function(){ if (o.__live) __remove(o.id); o.__live = false; };
  o.load = function(){}; o.volume = 1; o.loop = false; o.currentTime = 0; o.paused = true; return o; }
function matchMedia(q){ var m = __media(String(q)); return { matches: m, media: String(q), addEventListener: function(){}, removeEventListener: function(){}, addListener: function(){}, removeListener: function(){}, onchange: null }; }
function getComputedStyle(el){
  var id = el && el.id; var r = id ? __rect(id) : [0, 0, 0, 0];
  var o = { width: r[2] + 'px', height: r[3] + 'px', getPropertyValue: function(p){ p = __kebab(p); if (p === 'width') return r[2] + 'px'; if (p === 'height') return r[3] + 'px'; var c = (__styleCache[id] || {})[p]; return c !== undefined ? c : (id ? __cssOf(id, p) : ''); } };
  return new Proxy(o, { get: function(t, p){ if (p in t) return t[p]; return t.getPropertyValue(String(p)); } });
}
var screen = { get width(){ return __viewport()[0]; }, get height(){ return __viewport()[1]; }, get availWidth(){ return __viewport()[0]; }, get availHeight(){ return __viewport()[1]; }, colorDepth: 24, pixelDepth: 24 };
var devicePixelRatio = 1;
Object.defineProperty(window, 'innerWidth', { get: function(){ return __viewport()[0]; } });
Object.defineProperty(window, 'innerHeight', { get: function(){ return __viewport()[1]; } });
Object.defineProperty(window, 'outerWidth', { get: function(){ return __viewport()[0]; } });
Object.defineProperty(window, 'outerHeight', { get: function(){ return __viewport()[1]; } });
window.scrollTo = function(){}; window.scrollBy = function(){}; window.scrollX = 0; window.scrollY = 0; window.pageXOffset = 0; window.pageYOffset = 0;
window.getSelection = function(){ return { toString: function(){ return ''; }, removeAllRanges: function(){}, rangeCount: 0 }; };
window.open = function(){ return null; }; window.close = function(){}; window.print = function(){}; window.focus = function(){}; window.blur = function(){};
// base64, the event constructors a page may build, and the interfaces it may test with instanceof
var __b64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
window.btoa = function(s){ var str = String(s), out = '', i = 0; while (i < str.length) { var c1 = str.charCodeAt(i++), c2 = str.charCodeAt(i++), c3 = str.charCodeAt(i++); var e1 = c1 >> 2, e2 = ((c1 & 3) << 4) | ((c2 || 0) >> 4), e3 = isNaN(c2) ? 64 : ((c2 & 15) << 2) | ((c3 || 0) >> 6), e4 = isNaN(c3) ? 64 : c3 & 63; out += __b64.charAt(e1) + __b64.charAt(e2) + (e3 === 64 ? '=' : __b64.charAt(e3)) + (e4 === 64 ? '=' : __b64.charAt(e4)); } return out; };
window.atob = function(s){ var str = String(s).replace(/[^A-Za-z0-9+\/]/g, ''), out = '', i = 0; while (i < str.length) { var ix = function(c){ return c ? __b64.indexOf(c) : -1; }; var e1 = ix(str.charAt(i++)), e2 = ix(str.charAt(i++)), e3 = ix(str.charAt(i++)), e4 = ix(str.charAt(i++)); out += String.fromCharCode((e1 << 2) | (e2 >> 4)); if (e3 >= 0) out += String.fromCharCode(((e2 & 15) << 4) | (e3 >> 2)); if (e4 >= 0) out += String.fromCharCode(((e3 & 3) << 6) | e4); } return out; };
function KeyboardEvent(type, init){ Event.call(this, type, init); init = init || {}; this.key = init.key || ''; this.code = init.code || ''; this.keyCode = init.keyCode || 0; this.ctrlKey = !!init.ctrlKey; this.shiftKey = !!init.shiftKey; this.altKey = !!init.altKey; this.metaKey = !!init.metaKey; this.repeat = !!init.repeat; }
KeyboardEvent.prototype = Object.create(Event.prototype);
function PointerEvent(type, init){ Event.call(this, type, init); init = init || {}; this.clientX = init.clientX || 0; this.clientY = init.clientY || 0; this.pointerType = init.pointerType || 'mouse'; this.button = init.button || 0; this.buttons = init.buttons || 0; }
PointerEvent.prototype = Object.create(Event.prototype);
var MouseEvent = PointerEvent;
function InputEvent(type, init){ Event.call(this, type, init); init = init || {}; this.data = init.data === undefined ? null : init.data; this.inputType = init.inputType || ''; this.isComposing = !!init.isComposing; }
InputEvent.prototype = Object.create(Event.prototype);
function __interface(name, test){ var f = function(){ throw new TypeError('Illegal constructor'); }; Object.defineProperty(f, Symbol.hasInstance, { value: test }); return f; }
var __isElement = function(o){ return !!o && typeof o === 'object' && (typeof o.tagName === 'string' || typeof o.__tag === 'string'); };
var __isNode = function(o){ return __isElement(o) || (!!o && typeof o === 'object' && (o === document || typeof o.textContent === 'string')); };
var EventTarget = __interface('EventTarget', function(o){ return __isNode(o) || o === window; });
var Node = __interface('Node', __isNode), Element = __interface('Element', __isElement), HTMLElement = __interface('HTMLElement', __isElement);
var HTMLInputElement = __interface('HTMLInputElement', function(o){ return __isElement(o) && String(o.tagName).toUpperCase() === 'INPUT'; }), SVGElement = __interface('SVGElement', function(o){ return __isElement(o) && String(o.tagName).toUpperCase() === 'SVG'; });
function Option(text, value, defaultSelected, selected){ var o = __detached('option'); o.textContent = text === undefined ? '' : String(text); if (value !== undefined) o.setAttribute('value', String(value)); if (selected || defaultSelected) o.setAttribute('selected', ''); return o; }
window.requestIdleCallback = function(fn){ return setTimeout(function(){ fn({ timeRemaining: function(){ return 10; }, didTimeout: false }); }, 1); }; window.cancelIdleCallback = clearTimeout;
window.self = window; window.top = window; window.parent = window; window.frames = [];
function __styleProxy(id){
  var cache = __styleCache[id] = __styleCache[id] || {};
  var target = {
    setProperty: function(p, v){ p = __kebab(p); cache[p] = String(v); __setStyle(id, p, String(v)); },
    getPropertyValue: function(p){ p = __kebab(p); return cache[p] !== undefined ? cache[p] : ''; },
    removeProperty: function(p){ p = __kebab(p); var old = cache[p]; delete cache[p]; __setStyle(id, p, ''); return old || ''; },
    get cssText(){ var s = ''; for (var k in cache) s += k + ': ' + cache[k] + '; '; return s.trim(); },
    set cssText(v){ String(v).split(';').forEach(function(d){ var i = d.indexOf(':'); if (i > 0) target.setProperty(d.slice(0, i).trim(), d.slice(i + 1).trim()); }); },
    get length(){ return Object.keys(cache).length; }
  };
  return new Proxy(target, {
    set: function(t, p, v){ if (p in t) { t[p] = v; return true; } var k = __kebab(String(p)); cache[k] = String(v); __setStyle(id, k, String(v)); return true; },
    get: function(t, p){ if (p in t) return t[p]; var k = __kebab(String(p)); return cache[k] !== undefined ? cache[k] : ''; }
  });
}
// ---- Element.animate: the keyframe runner the page's CSS animations use ----
function __toFrames(keyframes){
  var out = [];
  if (Array.isArray(keyframes)) {
    var n = keyframes.length;
    keyframes.forEach(function(k, i){ var pct = k.offset !== undefined ? k.offset * 100 : (n === 1 ? 100 : i * 100 / (n - 1)); var d = []; for (var p in k) { if (p === 'offset' || p === 'easing' || p === 'composite') continue; d.push(__kebab(p) + ':' + k[p]); } out.push(pct + '|' + d.join(';')); });
  } else if (keyframes && typeof keyframes === 'object') {
    var props = Object.keys(keyframes); var len = 0; props.forEach(function(p){ if (Array.isArray(keyframes[p])) len = Math.max(len, keyframes[p].length); });
    for (var i = 0; i < len; i++) { var d = []; props.forEach(function(p){ var v = keyframes[p]; var vi = Array.isArray(v) ? v[Math.min(i, v.length - 1)] : v; d.push(__kebab(p) + ':' + vi); }); out.push((len === 1 ? 100 : i * 100 / (len - 1)) + '|' + d.join(';')); }
  }
  return out;
}
function __animate_el(id, keyframes, options){
  var o = typeof options === 'number' ? { duration: options } : (options || {});
  var opts = [o.duration || 0, o.delay || 0, o.iterations === Infinity ? 'Infinity' : (o.iterations || 1), o.direction || '', o.easing || '', o.fill || ''].join(',');
  var h = __animate(id, __toFrames(keyframes), opts);
  var anim = { id: h, playState: 'running', currentTime: 0, effect: null, onfinish: null, oncancel: null,
    cancel: function(){ __cancelAnimation(h); anim.playState = 'idle'; if (typeof anim.oncancel === 'function') anim.oncancel(); },
    finish: function(){ __cancelAnimation(h); anim.playState = 'finished'; if (typeof anim.onfinish === 'function') anim.onfinish(); },
    pause: function(){ anim.playState = 'paused'; }, play: function(){ anim.playState = 'running'; }, reverse: function(){},
    addEventListener: function(t, fn){ if (t === 'finish') anim.onfinish = fn; if (t === 'cancel') anim.oncancel = fn; } };
  anim.finished = new Promise(function(res){ setTimeout(function(){ if (anim.playState === 'running') { anim.playState = 'finished'; if (typeof anim.onfinish === 'function') anim.onfinish(); } res(anim); }, (o.duration || 0) * (o.iterations === Infinity ? 1e9 : (o.iterations || 1)) + (o.delay || 0)); });
  return anim;
}
function __sibling(id, step){
  var p = __parent(id); if (!p) return null;
  var c = __children(p); var i = c.indexOf(id) + step;
  return i >= 0 && i < c.length ? __el(c[i]) : null;
}
// Observers a page may construct: nothing to observe here, so they never fire.
function MutationObserver(){ this.observe = function(){}; this.disconnect = function(){}; this.takeRecords = function(){ return []; }; }
function ResizeObserver(){ this.observe = function(){}; this.unobserve = function(){}; this.disconnect = function(){}; }
function IntersectionObserver(){ this.observe = function(){}; this.unobserve = function(){}; this.disconnect = function(){}; }

// ---- controls: values, per-element listeners, events ----
var __values = {}, __elListeners = {}, __elHandlers = {};
function __fire(id, type, detail, x, y){
  var r = (x !== undefined) ? __rect(id) : [0, 0, 0, 0];
  var ev = new Event(type, { bubbles: true, cancelable: true, detail: detail });
  ev.button = 0; ev.buttons = type === 'mousedown' ? 1 : 0;
  ev.clientX = x || 0; ev.clientY = y || 0; ev.pageX = x || 0; ev.pageY = y || 0; ev.x = x || 0; ev.y = y || 0;
  ev.offsetX = (x || 0) - r[0]; ev.offsetY = (y || 0) - r[1];
  ev.target = __el(id);
  return __dispatchOn(id, ev);
}
function __input(id, value){
  __values[id] = value;
  __fire(id, 'input', value);
  __fire(id, 'change', value);
  __flushCanvases();
}
function __click(id, x, y){
  __fire(id, 'click', null, x, y);
  __flushCanvases();
}
function __pointer(id, type, x, y){
  __fire(id, type, null, x, y);
  if (type === 'mouseover') __fire(id, 'mouseenter', null, x, y);
  if (type === 'mouseout') __fire(id, 'mouseleave', null, x, y);
  __flushCanvases();
}

// ---- elements ----
function __el(id){
  var el = {
    dispatchEvent: function(ev){ if (!ev || !ev.type) return true; ev.target = ev.target || el; __dispatchOn(id, ev); return !ev.defaultPrevented; },
    insertAdjacentHTML: function(where, html){ where = String(where).toLowerCase(); if (where === 'beforeend') __appendHtml(id, String(html)); else if (where === 'afterbegin') { var c = __children(id); if (c.length) __insertHtml(id, String(html), c[0]); else __appendHtml(id, String(html)); } else if (where === 'beforebegin') { var p = __parent(id); if (p) __insertHtml(p, String(html), id); } else if (where === 'afterend') { var p2 = __parent(id); var s = __sibling(id, 1); if (p2) { if (s) __insertHtml(p2, String(html), s.id); else __appendHtml(p2, String(html)); } } },
    insertAdjacentElement: function(where, n){ el.insertAdjacentHTML(where, __serialize(n)); if (n.__adopt) n.__adopt(); return n; },
    insertAdjacentText: function(where, t){ el.insertAdjacentHTML(where, __escape(t)); },
    before: function(){ var p = __parent(id); if (!p) return; for (var i = 0; i < arguments.length; i++) { var n = arguments[i]; __insertHtml(p, typeof n === 'string' ? __escape(n) : __serialize(n), id); if (n && n.__adopt) n.__adopt(); } },
    after: function(){ var p = __parent(id); if (!p) return; var s = __sibling(id, 1); for (var i = 0; i < arguments.length; i++) { var n = arguments[i]; var h = typeof n === 'string' ? __escape(n) : __serialize(n); if (s) __insertHtml(p, h, s.id); else __appendHtml(p, h); if (n && n.__adopt) n.__adopt(); } },
    prepend: function(){ for (var i = arguments.length - 1; i >= 0; i--) el.insertAdjacentHTML('afterbegin', typeof arguments[i] === 'string' ? __escape(arguments[i]) : __serialize(arguments[i])); },
    replaceWith: function(){ el.before.apply(el, arguments); __remove(id); },
    toggleAttribute: function(n, force){ var has = __getAttr(id, n) !== null; var want = force === undefined ? !has : !!force; if (want && !has) __setAttr(id, n, ''); if (!want && has) __removeAttr(id, n); return want; },
    get className(){ return __getAttr(id, 'class') || ''; },
    animate: function(k, o){ return __animate_el(id, k, o); },
    getAnimations: function(){ return []; },
    get scrollWidth(){ return __children_rects(id)[0]; }, get scrollHeight(){ return __children_rects(id)[1]; },
    get scrollTop(){ return __scrollCache[id] || 0; }, set scrollTop(v){ __scrollCache[id] = Math.max(0, Number(v) || 0); __setScroll(id, __scrollCache[id]); },
    get scrollLeft(){ return 0; }, set scrollLeft(v){},
    scrollTo: function(a, b){ var y = (a && typeof a === 'object') ? (a.top || 0) : (b || 0); el.scrollTop = y; },
    scrollBy: function(a, b){ var y = (a && typeof a === 'object') ? (a.top || 0) : (b || 0); el.scrollTop = (__scrollCache[id] || 0) + y; },
    scrollIntoView: function(arg){
      // the nearest scrolling ancestor jumps so this element's top (or bottom, for block: 'end') meets its edge
      var box = __scrollBox(__parent(id) || id); if (!box) return;
      var r = __rect(id), b = __rect(box), cur = __scrollCache[box] || 0;
      var toEnd = arg && typeof arg === 'object' && (arg.block === 'end' || arg.block === 'nearest' && r[1] > b[1] + b[3] / 2) || arg === false;
      var y = toEnd ? cur + (r[1] + r[3]) - (b[1] + b[3]) : cur + r[1] - b[1];
      __el(box).scrollTop = y;
    },
    get offsetParent(){ return el.parentElement; },
    get value(){ return __values[id] !== undefined ? __values[id] : (__getAttr(id, 'value') || ''); },
    set value(v){ __values[id] = String(v); __setValue(id, String(v)); },
    get checked(){ var v = __values[id]; return v !== undefined ? v === 'true' : __getAttr(id, 'checked') !== null; },
    set checked(v){ __values[id] = v ? 'true' : 'false'; __setValue(id, v ? 'true' : 'false'); },
    get onchange(){ return (__elHandlers[id] || {}).onchange; }, set onchange(f){ (__elHandlers[id] = __elHandlers[id] || {}).onchange = f; },
    get onclick(){ return (__elHandlers[id] || {}).onclick; }, set onclick(f){ (__elHandlers[id] = __elHandlers[id] || {}).onclick = f; __wantClicks(id); },
    get oninput(){ return (__elHandlers[id] || {}).oninput; }, set oninput(f){ (__elHandlers[id] = __elHandlers[id] || {}).oninput = f; },
    select: function(){},
    get style(){ return __styleProxy(id); },
    set textContent(v){ __textCache[id] = String(v); delete __htmlCache[id]; __setText(id, String(v)); }, get textContent(){ return __textCache[id] !== undefined ? __textCache[id] : __textOf(id); },
    set innerText(v){ el.textContent = v; }, get innerText(){ return el.textContent; },
    set innerHTML(v){ __htmlCache[id] = String(v); delete __textCache[id]; __setHtml(id, String(v)); }, get innerHTML(){ return __htmlCache[id] !== undefined ? __htmlCache[id] : __htmlOf(id, false); },
    get outerHTML(){ return __htmlOf(id, true); },
    get children(){ return __children(id).map(__el); }, get childNodes(){ return __children(id).map(__el); },
    get childElementCount(){ return __children(id).length; },
    get firstChild(){ var c = __children(id); return c.length ? __el(c[0]) : null; }, get firstElementChild(){ return el.firstChild; },
    get lastChild(){ var c = __children(id); return c.length ? __el(c[c.length - 1]) : null; }, get lastElementChild(){ return el.lastChild; },
    get parentElement(){ var p = __parent(id); return p ? __el(p) : null; }, get parentNode(){ return el.parentElement; },
    get nextElementSibling(){ return __sibling(id, 1); }, get nextSibling(){ return __sibling(id, 1); },
    get previousElementSibling(){ return __sibling(id, -1); }, get previousSibling(){ return __sibling(id, -1); },
    get isConnected(){ return __has(id); }, get nodeType(){ return 1; }, get nodeName(){ return el.tagName; },
    getBoundingClientRect: function(){ var r = __rect(id); return { x: r[0], y: r[1], left: r[0], top: r[1], width: r[2], height: r[3], right: r[0] + r[2], bottom: r[1] + r[3] }; },
    get offsetLeft(){ return __rect(id)[0]; }, get offsetTop(){ return __rect(id)[1]; },
    hasAttribute: function(n){ return __getAttr(id, n) !== null; },
    replaceChildren: function(){ __setHtml(id, ''); for (var i = 0; i < arguments.length; i++) el.appendChild(typeof arguments[i] === 'string' ? { textContent: arguments[i] } : arguments[i]); },
    getClientRects: function(){ return [el.getBoundingClientRect()]; },
    get clientTop(){ return parseFloat(__cssOf(id, 'border-top-width')) || 0; }, get clientLeft(){ return parseFloat(__cssOf(id, 'border-left-width')) || 0; },
    normalize: function(){},
    get placeholder(){ return __getAttr(id, 'placeholder') || ''; }, set placeholder(v){ __setAttr(id, 'placeholder', String(v)); },
    get alt(){ return __getAttr(id, 'alt') || ''; }, set alt(v){ __setAttr(id, 'alt', String(v)); },
    get tabIndex(){ var t = __getAttr(id, 'tabindex'); return t === null ? -1 : (parseInt(t, 10) || 0); }, set tabIndex(v){ __setAttr(id, 'tabindex', String(v)); },
    get selected(){ return __getAttr(id, 'selected') !== null; }, set selected(v){ if (v) __setAttr(id, 'selected', ''); else __removeAttr(id, 'selected'); },
    get options(){ return __query('#' + id + ' option').map(__el); },
    get selectedIndex(){ var o = el.options; for (var i = 0; i < o.length; i++) if (o[i].selected) return i; return o.length ? 0 : -1; },
    set selectedIndex(v){ var o = el.options; for (var i = 0; i < o.length; i++) o[i].selected = (i === Number(v)); if (o[Number(v)]) __setValue(id, o[Number(v)].value); },
    removeAttribute: function(n){ __removeAttr(id, n); },
    get attributes(){ var a = __attrs(id), out = []; for (var i = 0; i < a.length; i += 2) out.push({ name: a[i], value: a[i + 1] }); return out; },
    matches: function(sel){ return __query(sel).indexOf(id) >= 0; },
    closest: function(sel){ var hits = __query(sel); for (var p = id; p; p = __parent(p)) if (hits.indexOf(p) >= 0) return __el(p); return null; },
    contains: function(o){ return !!o && (o.id === id || __contains(id, o.id)); },
    querySelectorAll: function(sel){ return __query(sel).filter(function(c){ return __contains(id, c); }).map(__el); },
    querySelector: function(sel){ var r = __query(sel).filter(function(c){ return __contains(id, c); }); return r.length ? __el(r[0]) : null; },
    insertBefore: function(n, ref){ if (!ref) return el.appendChild(n); __insertHtml(id, __serialize(n), ref.id); if (n.__adopt) n.__adopt(); return n; },
    replaceChild: function(n, old){ el.insertBefore(n, old); __remove(old.id); return old; },
    cloneNode: function(deep){ var c = __detached(el.tagName); var a = __attrs(id); for (var i = 0; i < a.length; i += 2) { if (a[i] === 'class') c.className = a[i + 1]; else c.__attrs[a[i]] = a[i + 1]; } if (deep) c.__html = __htmlOf(id, false); return c; },
    focus: function(){}, blur: function(){},
    get open(){ return __getAttr(id, 'open') !== null; }, set open(v){ if (v) __setAttr(id, 'open', ''); else __removeAttr(id, 'open'); },
    show: function(){ __setAttr(id, 'open', ''); }, showModal: function(){ __setAttr(id, 'open', ''); __setAttr(id, 'data-modal', ''); }, close: function(){ __removeAttr(id, 'open'); __removeAttr(id, 'data-modal'); },
    submit: function(){ __fire(id, 'submit', null); }, reset: function(){},
    get disabled(){ return __getAttr(id, 'disabled') !== null; }, set disabled(v){ if (v) __setAttr(id, 'disabled', ''); else __removeAttr(id, 'disabled'); },
    get hidden(){ return __getAttr(id, 'hidden') !== null; }, set hidden(v){ if (v) { __setAttr(id, 'hidden', ''); __setStyle(id, 'display', 'none'); } else { __removeAttr(id, 'hidden'); __setStyle(id, 'display', ''); } },
    get title(){ return __getAttr(id, 'title') || ''; }, set title(v){ __setAttr(id, 'title', String(v)); },
    get name(){ return __getAttr(id, 'name') || ''; }, get type(){ return __getAttr(id, 'type') || ''; },
    get href(){ return __getAttr(id, 'href') || ''; }, set href(v){ __setAttr(id, 'href', String(v)); },
    get src(){ return __getAttr(id, 'src') || ''; }, set src(v){ __setAttr(id, 'src', String(v)); },
    get max(){ return __getAttr(id, 'max'); }, set max(v){ __setAttr(id, 'max', String(v)); },
    get min(){ return __getAttr(id, 'min'); }, set min(v){ __setAttr(id, 'min', String(v)); },
    set className(v){ __setClass(id, String(v)); },
    get id(){ return id; }, set id(v){ console.warn('setting id is not supported; ids are fixed at build'); },
    classList: { add: function(){ }, remove: function(){ } },
    get clientWidth(){ return __size(id)[0]; }, get clientHeight(){ return __size(id)[1]; },
    get offsetWidth(){ return __size(id)[0]; }, get offsetHeight(){ return __size(id)[1]; },
    get width(){ return Number(__getAttr(id,'width')) || 0; }, set width(v){ __setAttr(id,'width',String(v)); },
    get height(){ return Number(__getAttr(id,'height')) || 0; }, set height(v){ __setAttr(id,'height',String(v)); },
    get dataset(){ return new Proxy({}, { get: function(o, p){ return __getAttr(id, 'data-' + String(p).replace(/[A-Z]/g, function(m){ return '-' + m.toLowerCase(); })); } }); },
    getAttribute: function(n){ return __getAttr(id, n); },
    setAttribute: function(n, v){ __setAttr(id, n, String(v)); },
    getContext: function(){ return __ctx(id); },
    addEventListener: function(type, fn){ var l = __elListeners[id] = __elListeners[id] || {}; (l[type] = l[type] || []).push(fn); if (type === 'click') __wantClicks(id); if (type.indexOf('mouse') === 0) __wantPointer(type === 'mouseenter' ? 'mouseover' : type === 'mouseleave' ? 'mouseout' : type); },
    removeEventListener: function(type, fn){ var l = __elListeners[id]; if (l && l[type]) l[type] = l[type].filter(function(f){ return f !== fn; }); },
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
  if (c.__frag) return c.__html !== null ? c.__html : c.__children.map(__serialize).join('');
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
  o.__listeners = [];
  o.addEventListener = function(type, fn){ if (o.__live) __el(o.id).addEventListener(type, fn); else o.__listeners.push([type, fn]); };
  o.removeEventListener = function(type, fn){ if (o.__live) __el(o.id).removeEventListener(type, fn); else o.__listeners = o.__listeners.filter(function(l){ return l[0] !== type || l[1] !== fn; }); };
  o.style = new Proxy({}, { set: function(t, p, v){ if (o.__live) __setStyle(o.id, String(p), String(v)); else o.__style[p] = String(v); return true; }, get: function(t, p){ return o.__style[p] || ''; } });
  Object.defineProperty(o, 'innerHTML', { set: function(v){ if (o.__live) __setHtml(o.id, String(v)); else { o.__html = String(v); o.__text = null; } }, get: function(){ return o.__live ? __el(o.id).innerHTML : (o.__html || ''); } });
  Object.defineProperty(o, 'textContent', { set: function(v){ if (o.__live) __setText(o.id, String(v)); else { o.__text = String(v); o.__html = null; } }, get: function(){ return o.__live ? __el(o.id).textContent : (o.__text || ''); } });
  Object.defineProperty(o, 'innerText', { set: function(v){ o.textContent = v; }, get: function(){ return o.__text || ''; } });
  o.__adopt = function(){ o.__children.forEach(function(c){ if (c.__adopt) c.__adopt(); }); if (o.__frag) { o.__children = []; o.__html = null; return; } o.__live = true; o.__listeners.forEach(function(l){ __el(o.id).addEventListener(l[0], l[1]); }); o.__listeners = []; };
  Object.defineProperty(o, 'childNodes', { get: function(){ return o.__live ? __el(o.id).childNodes : o.__children.slice(); } });
  Object.defineProperty(o, 'children', { get: function(){ return o.__live ? __el(o.id).children : o.__children.filter(function(c){ return !!c.__tag; }); } });
  Object.defineProperty(o, 'firstChild', { get: function(){ return o.__live ? __el(o.id).firstChild : (o.__children[0] || null); } });
  Object.defineProperty(o, 'hasChildNodes', { value: function(){ return o.__children.length > 0; } });
  // once adopted, anything the shim does not define itself is the live element's (replaceWith, before, classList, rects, siblings...)
  return new Proxy(o, {
    get: function(t, p){ if (t.__live && (p === 'className' || !(p in t))) { var live = __el(t.id); var v = live[p]; return typeof v === 'function' ? v.bind(live) : v; } return t[p]; },
    set: function(t, p, v){ if (t.__live && (p === 'className' || !(p in t))) { __el(t.id)[p] = v; return true; } t[p] = v; return true; },
    has: function(t, p){ return (p in t) || (t.__live && (p in __el(t.id))); }
  });
}
var document = {
  getElementById: function(id){ return __has(id) ? __el(id) : null; },
  elementFromPoint: function(x, y){ var id = __elementAt(Number(x) || 0, Number(y) || 0); return id ? __el(id) : null; },
  elementsFromPoint: function(x, y){ var e = document.elementFromPoint(x, y); var out = []; while (e) { out.push(e); e = e.parentElement; } return out; },
  querySelectorAll: function(sel){ return __query(sel).map(__el); },
  querySelector: function(sel){ var r = __query(sel); return r.length ? __el(r[0]) : null; },
  createElement: __detached,
  createElementNS: function(ns, tag){ return __detached(tag); },
  createTextNode: function(t){ return { textContent: String(t) }; },
  getElementsByClassName: function(c){ return __query('.' + String(c).trim().split(/\s+/).join('.')).map(__el); },
  getElementsByTagName: function(t){ return __query(String(t) === '*' ? '*' : String(t)).map(__el); },
  getElementsByName: function(n){ return __query('[name=' + JSON.stringify(String(n)) + ']').map(__el); },
  get readyState(){ return document_readyState; },
  get activeElement(){ return null; },
  get cookie(){ return ''; }, set cookie(v){},
  get hidden(){ return false; }, get visibilityState(){ return 'visible'; },
  get location(){ return location; },
  get defaultView(){ return window; },
  createEvent: function(){ return new Event(''); },
  hasFocus: function(){ return true; },
  execCommand: function(){ return false; },
  fonts: { ready: Promise.resolve(), load: function(){ return Promise.resolve([]); }, check: function(){ return true; } },
  get head(){ return __has('head') ? __el('head') : __el('body'); },
  get forms(){ return __query('form').map(__el); }, get images(){ return __query('img').map(__el); }, get links(){ return __query('a').map(__el); }, get scripts(){ return []; },
  createDocumentFragment: function(){ var f = __detached('#fragment'); f.__frag = true; f.nodeType = 11; return f; },
  get documentElement(){ return __el('body'); },
  get title(){ return ''; }, set title(v){},
  contains: function(o){ return !!o && __has(o.id); },
  addEventListener: addEventListener,
  body: __el('body')
};
";
}

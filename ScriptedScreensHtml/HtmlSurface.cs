using System.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.UIElements;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;
using Motherboard = Assets.Scripts.Objects.Items.Motherboard;
using CartridgeIntegratedCircuitLua = ScriptedScreens.CartridgeIntegratedCircuitLua;
using ProgrammableVisorGlasses = ScriptedScreens.ProgrammableVisorGlasses;

namespace ScriptedScreensHtml;

/// <summary>
/// A page on a console: its element tree (laid out by <see cref="Panel"/>), its script, its data
/// and input, and the translation into the vector scene the vector mod draws.
/// </summary>
internal sealed class HtmlSurface : MonoBehaviour
{
    private const int DataAwakeFrames = 60;


    /// <summary>Scale per page key, so a host rebuilt by ScriptedScreens comes back at the same sharpness.</summary>
    internal string PageKey = string.Empty;

    /// <summary>
    /// The live surface per page. A rebuild (a screen capture does one) makes a new surface for the
    /// same page before Unity destroys the old one at the end of the frame; the old one must stop
    /// sending then, or its value patches (under its own slot names) land on the new scene.
    /// </summary>
    private static readonly Dictionary<string, HtmlSurface> Current = new(StringComparer.Ordinal);

    private bool IsCurrent => string.IsNullOrEmpty(PageKey) || !Current.TryGetValue(PageKey, out var live) || live == this;
    /// <summary>What the bridge needs to address the vector mod: the host identity and the page element id.</summary>
    internal object? Board;
    internal object? Cartridge;
    /// <summary>Set once this page runs compiled; while it is set the page does no per-frame work.</summary>
    private CompiledRun? _compiled;
    /// <summary>Where a data key's value lands in the scene, for a page with no script to compile.</summary>
    private DataSlots? _dataSlots;
    /// <summary>
    /// Every key sent before the data compile, merged, later wins: all of them are in the DOM by the
    /// next emit, which is the one the compile maps and proves them against.
    /// </summary>
    private Dictionary<string, SS.UiValue>? _dataPending;
    /// <summary>Data ticks that skipped layout, translate and emit entirely (diagnostics).</summary>
    private int _dataFastTicks;
    /// <summary>The FontLibrary.Generation this page last laid out under.</summary>
    private int _fontGeneration;
    /// <summary>Payloads dropped after the page's data compile failed outright, for the diagnostics line.</summary>
    private int _dataDropped;
    /// <summary>Why the data compile failed outright (it threw), or null. Set once; a later payload does no work.</summary>
    private string? _dataRefused;
    /// <summary>Elements whose opacity group the data drives (a bool, an opacity), so a rebuilt page names them again.</summary>
    private readonly HashSet<string> _dataDriven = new(StringComparer.Ordinal);
    internal object? Visor;
    internal string ElementId = string.Empty;
    /// <summary>The data element's id: the vector mod needs a host of its own for a data payload.</summary>
    internal string DataElementId = string.Empty;
    /// <summary>The vector scene id ("html:" and the element id), built once: every payload names it, and a concatenation per send was garbage per tick.</summary>
    private string SceneId => ReferenceEquals(_sceneIdOf, ElementId) ? _sceneId! : _sceneId = "html:" + (_sceneIdOf = ElementId);
    private string? _sceneId, _sceneIdOf;
    private bool _dirty;
    private int _dScript, _dAnim, _dTween, _dDom, _dOther;   // dirty causes since the last diagnostics line
    private int _gateSkips;   // frames the page was due for and wanted nothing from
    /// <summary>The structure the vector mod has (the scene with its values as $slots), and the values it was last sent.</summary>
    private string? _lastTemplate;
    private readonly Dictionary<string, SceneSlots.Value> _sentValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SceneSlots.Value> _slotScratch = new(StringComparer.Ordinal);
    private int _structureSends, _patchSends, _patchSlots, _morphs;
    private string _slotPrefix = "L";
    private float _lastWhyAt;
    /// <summary>Page design width (meta viewport); 0 = the element's own width.</summary>
    private float _designWidth;

    /// <summary>Identity of the element this surface draws, set by the patch. Null on a clone.</summary>
    internal object? State;
    internal string Surface = string.Empty;


    private Panel? _panel;
    private VisualElement? _content;
    private Dictionary<string, VisualElement> _byId = new(System.StringComparer.Ordinal);
    private Dictionary<string, SvgShape> _shapes = new(System.StringComparer.Ordinal);
    private readonly List<SvgElement> _svgs = new();
    private readonly List<KeyframeRunner> _animations = new();
    private readonly Tweens _tweens = new();
    private ScriptHost? _script;
    private HtmlRenderer.Result? _built;
    /// <summary>Frames the document stays enabled after a change. 0 = asleep, panel disposed.</summary>
    private int _awakeFrames;
    private string _source = string.Empty;

    internal void SetSource(string source)
    {
        if (!string.IsNullOrEmpty(PageKey))
            Current[PageKey] = this;
        Hold();
        // A handed-over page's chunk runs in its chip until another page's replaces it: this host
        // draws and compiles the new source as any page does (HtmlElementPatch switched it back on).
        if (_handedOver) { _handedOver = false; _compiled = null; _pageStop = false; }
        _source = source;
        // New source, new compile: the data mapping, its refusal and the names it drove belong to
        // the page that was, and the released working set must not stand in for the new one.
        _dataSlots = null; _dataPending = null;
        _dataRefused = null; _dataDriven.Clear(); _easeValues.Clear();
        _released = false;
        if (!Surfaces.Contains(this))
            Surfaces.Add(this);
        Build();
    }

    // No capture path of its own any more. The page is drawn by the vector mod, whose
    // clone carries its mesh through Instantiate and builds inline when it has none, and
    // the first structure is emitted synchronously at build (below), so a capture that
    // rebuilds the surface sees the scene inside the same call.

    // frames slower than 25 ms since the last diagnostics line, and the slowest (one count for all pages)
    private static int _frameSeen = -1, _slowFrames;
    private static float _worstFrame;

    private long _updateTicks;
    private int _awakeCount;

    private void Update()
    {
        var u0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { UpdateInner(); }
        finally
        {
            var spent = System.Diagnostics.Stopwatch.GetTimestamp() - u0;
            _updateTicks += spent;
            _allUpdateTicks += spent;
            if (_awakeFrames > 0) _awakeCount++;
        }
    }

    private void UpdateInner()
    {
        if (Time.frameCount != _frameSeen)
        {
            _frameSeen = Time.frameCount;
            OffThread.Now = Time.time;
            var dt = Time.unscaledDeltaTime * 1000f;
            if (dt > 25f) _slowFrames++;
            if (dt > _worstFrame) _worstFrame = dt;
        }
        // Handed to its chip (Install): its host is switched off and this never runs again for it.
        if (_handedOver) return;
        // A compiled page is not laid out, scripted, translated or split: its Lua is in the chip and
        // writes the scene's slots directly, so all this update does is give that Lua a frame.
        //
        // ABOVE the _panel guard, and that is the whole point. Releasing the working set nulls the
        // panel, so a compiled page trips that guard by design - and while this sat below it, every
        // compiled console silently stopped ticking the moment it was released. The page did not
        // error and did not go blank; it just stopped moving, and only "sent 1 patches" in the
        // diagnostics said so.
        // A data page is released the same way once its table is compiled, and only once the emit
        // that proved it has gone out: no job in flight, and none owed - a job whose tree changed
        // under it (FinishJob's stale branch) sent nothing and is retried, and releasing first
        // would leave the console on the scene before it.
        if (_compiled == null && _releasePending && IsCurrent && _pageState == PageIdle && !_dirty)
        {
            _releasePending = false;
            ReleaseWorkingSet();
        }
        if (_compiled != null && IsCurrent)
        {
            // One Update after compiling, so the structure the chip writes into has gone out.
            if (_releasePending) { _releasePending = false; ReleaseWorkingSet(); }

            // At the rate the renderer will actually draw, not at display rate. The compiled tick
            // had no limit at all, so it ran the page's Lua ~78 times a second per console while the
            // vector mod rebuilds at 30 - every second frame thrown away, on the GAME thread, times
            // fourteen consoles. The interpreted path has always used this same gate; the compiled
            // one simply never picked it up.
            var clock = Time.time;
            var lodNow = VectorBridge.Lod(clock);
            var seen = IsOnScreen(out var widthNow);
            var wantHidden = !seen && HtmlConfig.CullOffScreen switch
            {
                CullChoice.Always => true,
                CullChoice.Never => false,
                _ => lodNow.cull,
            };
            // No half-frame tolerance here, and that is the difference that matters. The
            // interpreted path grants it so a 60 Hz cap on a 73 fps display is not halved to 36 -
            // and it can afford to, because its work is on a worker. This work is on the GAME
            // thread (the chip's VM is the game's, and is shared with the author's program, so it
            // cannot move), and measured at 0.69 ms per console per frame for the runner. Letting
            // every display frame through produced values the renderer never drew, at 14 consoles.
            var interval = wantHidden ? HiddenInterval : FrameInterval(widthNow, lodNow);
            if (clock - _lastCompiledTick < interval) return;
            _lastCompiledTick = clock;

            if (_compiled.Tick(Cartridge ?? Board)) return;
            // It gave up - the chip recompiled under it, or its frames kept failing. Back to the
            // interpreter, which is always able to run the page, so the page has to exist again.
            _compiled = null;
            if (_released && !Rehydrate()) return;
            _dirty = true; _dOther++;
            Wake();
        }

        if (_panel == null)
        {
            // a surface with no page (a capture's clone before its build): its script still runs here
            if (_script != null)
            {
                if (_scriptPending) { _scriptPending = false; _script.Run(_built?.Script ?? string.Empty); }
                if (_script.Frame(Time.time, _byId)) { _dirty = true; _dScript++; Wake(); }
            }
            return;
        }
        ReportIfDue();
        if (!IsCurrent)
            return; // replaced by a rebuilt surface for the same page: it sends from now on
        if (_pageState == PageRunning)
            return; // the page thread owns the page until its frame ends
        if (_pageState == PageDone)
            FinishJob();
        // FinishJob may just have handed the page to its chip, and let it go.
        if (_handedOver) return;
        // A face arriving re-lays out EVERY page showing it, not only the one whose Update drained
        // the queue. Not a released page (compiled, or a data page after its table): it returned at
        // the _panel guard above, on purpose. Its scene already names the face, and the vector mod
        // looks faces up by name on every rebuild, so the glyphs change anyway; a relayout would
        // move boxes under a slot table compiled against the old ones. Its metrics stay the
        // fallback's, which the emitter's label slack absorbs.
        if (FontLibrary.ResolvePending() | _fontGeneration != FontLibrary.Generation) { _fontGeneration = FontLibrary.Generation; _dirty = true; _dOther++; }
        if (_scriptPending && _script != null)
        {
            // the page script starts once the page exists; its engine thread runs it
            _scriptPending = false;
            _script.Run(_built?.Script ?? string.Empty);
        }
        // Off screen the vector mod culls its rebuild, so laying the page out, running its script
        // frame and translating it produces nothing anyone sees - and it is the garbage that makes
        // the game collect. A hidden page keeps a slow heartbeat (a browser throttles a background
        // tab the same way) so its clock, timers and state carry on, and it emits at once when it
        // comes back into view. Queued data and input still arrive on that heartbeat.
        var lod = VectorBridge.Lod(Time.time);
        var visible = IsOnScreen(out var screenWidth);
        var cull = HtmlConfig.CullOffScreen switch
        {
            CullChoice.Always => true,
            CullChoice.Never => false,
            _ => lod.cull,   // a page only needs a frame the vector mod will draw
        };
        var onScreen = visible || !cull;
        _hiddenNow = !onScreen;
        if (onScreen != _wasOnScreen)
        {
            _wasOnScreen = onScreen;
            if (onScreen) { _dirty = true; _dOther++; }   // come back showing the current state, not the last one drawn
        }
        // A browser runs requestAnimationFrame at the display rate; this machine draws 73 frames a
        // second, so a page was laying out, running its script and translating 73 times too. The
        // vector mod rebuilds at most 60 Hz anyway, so the extra frames were never drawn.
        // Half a frame of tolerance: a cap just under the display rate would otherwise only ever be
        // met on every second frame (a 60 Hz cap on a 73 fps display ran the page at 36).
        var due = onScreen
            ? Time.time - _lastPageFrame >= FrameInterval(screenWidth, lod) - Time.unscaledDeltaTime * 0.5f
            : Time.time - _lastHiddenFrame >= HiddenInterval;
        // A page frame is worth running when something will come of it. Having a <script> is not that:
        // a page stepping twice a second was laying out, ticking and translating on every display
        // frame, and 96% of those frames produced a scene identical to the one before.
        var wanted = _dirty || !_inbox.IsEmpty || (_script?.WantsFrame(Time.time) ?? false)
                     || AnimationDue(Time.time) || _tweens.NextDue(Time.time) <= Time.time || AnySvgBlending();
        _lastDue = due;
        if (due && !wanted) _gateSkips++;
        if (due && wanted)
        {
            if (!onScreen) _lastHiddenFrame = Time.time;
            _lastPageFrame = Time.time;
            StartFrame(Time.time, LayoutSize());
        }
        if (_awakeFrames > 0)
            _awakeFrames--;
    }

    /// <summary>How often a page that nobody can see still runs a frame.</summary>
    private const float HiddenInterval = 0.5f;
    private float _lastHiddenFrame;
    private float _lastPageFrame;
    /// <summary>Whether the last update found the console out of view (for the diagnostics line).</summary>
    private bool _hiddenNow;
    private bool _wasOnScreen = true;
    private static Camera? _camera;

    /// <summary>
    /// Is any part of this console's screen in view? The same projection the vector mod culls its
    /// rebuilds with, so the two agree about what is worth drawing. Unknown counts as visible: a
    /// page that stops updating reads as a broken mod, while an extra frame only costs frames.
    /// </summary>
    /// <summary>Is any part of this console in view, and how wide is it on screen in display pixels (-1 unknown)?</summary>
    private bool IsOnScreen(out float screenWidth)
    {
        screenWidth = -1f;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return true;
        var camera = canvas.worldCamera != null ? canvas.worldCamera : (_camera != null ? _camera : _camera = Camera.main);
        if (camera == null)
            return true;

        var rt = (RectTransform)transform;
        var rect = rt.rect;
        var minX = float.MaxValue; var minY = float.MaxValue;
        var maxX = float.MinValue; var maxY = float.MinValue;
        var anyInFront = false;
        for (var i = 0; i < 4; i++)
        {
            var corner = new Vector3(i is 0 or 3 ? rect.xMin : rect.xMax, i is 0 or 1 ? rect.yMin : rect.yMax, 0f);
            var projected = camera.WorldToScreenPoint(rt.TransformPoint(corner));
            if (projected.z <= 0.01f) continue;
            anyInFront = true;
            minX = Mathf.Min(minX, projected.x); minY = Mathf.Min(minY, projected.y);
            maxX = Mathf.Max(maxX, projected.x); maxY = Mathf.Max(maxY, projected.y);
        }
        if (!anyInFront)
            return false;
        screenWidth = maxX - minX;
        const float margin = 64f;
        return maxX >= -margin && minX <= Screen.width + margin && maxY >= -margin && minY <= Screen.height + margin;
    }

    /// <summary>
    /// How often a page is worth running: whatever the vector mod will actually rebuild. Its cap
    /// applies always; its distance curve only when its own rate LOD is on, with its threshold and
    /// floor, so turning that off in its config gives full-rate pages again. Unknown size means
    /// full rate, since a page that stops updating reads as a broken mod.
    /// </summary>
    private static float FrameInterval(float screenWidth, (bool cull, bool rateLod, float maxHz, float minHz, float fullPixels) lod)
    {
        var maxHz = Mathf.Max(1f, lod.maxHz);
        if (!lod.rateLod || screenWidth < 0f || screenWidth >= lod.fullPixels)
            return 1f / maxHz;
        var t = Mathf.Sqrt(Mathf.Clamp01(screenWidth / Mathf.Max(1f, lod.fullPixels)));  // held up near the threshold, as the vector mod's curve is
        return 1f / Mathf.Max(1f, Mathf.Lerp(lod.minHz, maxHz, t));
    }

    /// <summary>What the gate sees right now, for the diagnostics line: which clause is holding frames back.</summary>
    private string GateWhy() =>
        $"dirty {_dirty}, inbox {!_inbox.IsEmpty}, script {(_script != null ? _script.GateWhy() : "none")}, runners {_animations.Count}, tweens {_tweens.Any}, "
        + $"due {_lastDue}, updated {_frameSeen == Time.frameCount}, panel {_panel != null}, current {IsCurrent}, state {_pageState}";

    /// <summary>
    /// The gate's own answer last update. Read it with `updated`: a surface whose Update is not
    /// running at all (ScriptedScreens deactivates a console nobody is looking at) reports exactly
    /// like one the gate is holding back - no emits, nothing dirty, no skips - and the two want
    /// opposite fixes.
    /// </summary>
    private bool _lastDue;

    /// <summary>
    /// Is any keyframe runner at a boundary? Having one is not a reason to run a frame: a runner
    /// writes at boundaries and does nothing between them, so `_animations.Count > 0` held this gate
    /// open permanently for any page with an animation the emitter cannot compile to an expression -
    /// which includes background-position, background-color and filter, i.e. most real pages.
    /// </summary>
    private bool AnimationDue(float now)
    {
        for (var i = 0; i < _animations.Count; i++)
            if (_animations[i].NextDue(now) <= now) return true;
        return false;
    }

    private bool AnySvgBlending()
    {
        foreach (var svg in _svgs)
            if (svg.Blending) return true;
        return false;
    }

    // ---- the page's own thread ----
    // Everything a page does between frames runs here: queued input and data, the script's
    // writes, animation steps, transitions, layout and translation. The game thread starts a frame
    // with the time and the layout size, and on a later frame collects the result and hands it to
    // the vector mod. Nothing else touches the page while a frame runs: a game-thread entry point
    // that must answer at once waits for it (Hold); the rest are queued (Post).

    private const int PageIdle = 0, PageRunning = 1, PageDone = 2;
    private volatile int _pageState;
    private System.Threading.Thread? _pageThread;
    private readonly System.Threading.AutoResetEvent _pageWake = new(false);
    private readonly System.Threading.ManualResetEventSlim _pageDone = new(true);
    private volatile bool _pageStop;
    private float _frameNow;
    private Vector2 _frameSize;
    private OffThread.Globals _frameGlobals;
    private bool _frameDiagnostics;
    private EmitResult? _frameResult;
    private Exception? _frameError;
    private double _workerMsTotal;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _inbox = new();

    /// <summary>
    /// Cascade, layout and the shared state they use (counters, pending calc, the em size) are
    /// one page at a time; translation, the long part, runs on all page threads at once.
    /// The compiler's own gate: a compile on a page thread takes it per cascade and per layout, so it
    /// lays out between other pages' frames, never across one of them.
    /// </summary>
    internal static readonly object CascadeGate = PageCompiler.Gate;

    /// <summary>Runs <paramref name="work"/> on the page thread at the start of its next frame. Game thread.</summary>
    private void Post(Action work) => _inbox.Enqueue(work);

    private void StartFrame(float now, Vector2 size)
    {
        if (_pageThread == null)
        {
            _pageThread = new System.Threading.Thread(PageLoop) { IsBackground = true, Name = "html page " + ElementId };
            _pageThread.Start();
        }
        _frameNow = now;
        _frameSize = size;
        _frameGlobals = OffThread.Globals.Take();
        _frameDiagnostics = HtmlConfig.Diagnostics;
        _frameResult = null;
        _frameError = null;
        _compileStop = false;
        _pageDone.Reset();
        _pageState = PageRunning;
        _pageWake.Set();
    }

    private void PageLoop()
    {
        // this thread never touches the engine: font questions are queued for the game thread
        OffThread.Active = true;
        while (true)
        {
            _pageWake.WaitOne();
            if (_pageStop) return;
            var w0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try { _frameResult = PageFrame(); }
            catch (Exception ex) { _frameError = ex; }
            finally
            {
                _workerMsTotal += (System.Diagnostics.Stopwatch.GetTimestamp() - w0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                _pageState = PageDone;
                _pageDone.Set();
            }
        }
    }

    /// <summary>One frame of the page, on its thread. The result is null when nothing changed.</summary>
    private EmitResult? PageFrame()
    {
        var now = _frameNow;
        OffThread.Job = _frameGlobals;
        var b0 = Allocated();
        lock (CascadeGate)
        {
            StepPage(now);
            if (!_dirty || _content == null || _panel == null || _built == null)
            {
                _allocStep += Allocated() - b0;
                return null;
            }
            _dirty = false;
            var b1 = Allocated();
            var t0 = Clock.Elapsed.TotalMilliseconds;
            _panel.Layout(_frameSize.x, _frameSize.y);
            var b2 = Allocated();
            var t1 = Clock.Elapsed.TotalMilliseconds;
            OffThread.Capture(_content, _built, _boxes, _boxScratch);
            _lastLayoutMs = t1 - t0;
            _lastCopyMs = Clock.Elapsed.TotalMilliseconds - t1;
            _allocStep += b1 - b0;
            _allocLayout += b2 - b1;
            _allocCopy += Allocated() - b2;
        }
        var b3 = Allocated();
        var result = Translate(_frameSize, now, _frameGlobals, _frameDiagnostics, worker: true);
        _allocTranslate += Allocated() - b3;
        return result;
    }

    /// <summary>Queued input and data, the script's writes, animation steps and transitions.</summary>
    private void StepPage(float now)
    {
        while (_inbox.TryDequeue(out var work))
        {
            try { work(); }
            catch (Exception ex) { ScriptedScreensHtmlPlugin.Log?.LogError($"html \"{ElementId}\": {ex}"); }
        }
        if (_script != null && _script.Frame(now, _byId, waitMs: 12)) { _dirty = true; _dScript++; Wake(); }

        // Keyframe animations step at keyframe boundaries; the scene interpolates between.
        // Time.time, so they pause with the game like the vector layer.
        // a runner whose element a script removed (an innerHTML page rebuilds its lamps every tick) would keep writing
        _animations.RemoveAll(a => a.Element.panel == null);
        foreach (var a in _animations)
        {
            a.Update(now);
            if (a.Wrote) { a.Wrote = false; _dirty = true; _dAnim++; }
            if (a.Finished && !a.Restored)
            {
                // animation-fill-mode: without forwards (the CSS default), the element
                // returns to its own style once the last iteration ends.
                a.Restored = true;
                if (!a.Spec.FillForwards && _built != null && _built.NodeOf.TryGetValue(a.Element, out var an))
                {
                    _built.Reclass(a.Element, an.Attr("class") ?? string.Empty);
                    _dirty = true;
                }
            }
        }
        // The last tween ending re-emits the scene once with plain numbers: static again.
        if (_tweens.Expire(now)) { _dirty = true; _dTween++; }
    }

    /// <summary>
    /// Layout size of the page in canvas units: the element's width, and a height in the
    /// console's TRUE proportions. ScriptedScreens reports a 460x460 canvas for every console
    /// size and stretches it onto the panel, so the rect's own height would lay the page out
    /// distorted. The world-space rect gives the physical aspect; the RawImage then stretches
    /// a correctly proportioned image onto the stretched rect and the two cancel.
    /// </summary>
    private Vector2 LayoutSize()
    {
        var rt = (RectTransform)transform;
        var rect = rt.rect;
        var width = Mathf.Max(64f, _designWidth > 0f ? _designWidth : rect.width);

        var bl = rt.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f));
        var br = rt.TransformPoint(new Vector3(rect.xMax, rect.yMin, 0f));
        var tl = rt.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f));
        var worldW = Vector3.Distance(bl, br);
        var worldH = Vector3.Distance(bl, tl);
        var aspect = worldW > 1e-5f && worldH > 1e-5f ? worldH / worldW : rect.height / width;

        return new Vector2(width, Mathf.Max(64f, width * aspect));
    }

    /// <summary>Marks the page busy for a few frames (the diagnostics line counts them).</summary>
    private void Wake(int frames = 3) => _awakeFrames = Mathf.Max(_awakeFrames, frames);

    private void Build()
    {
        Hold();
        try
        {
            lock (CascadeGate)
                BuildInner();
        }
        catch (System.Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogError($"html: build failed: {ex}");
        }
    }

    /// <summary>
    /// Marks the elements whose wrapping transform group has to carry its id. Lives on the renderer
    /// so the headless bench runs the same setup the game does - a scene measured without it differs
    /// from the real one in exactly the place a compiled page writes.
    /// </summary>
    private static void NameDrivenGroups(HtmlRenderer.Result built) => HtmlRenderer.NameDrivenGroups(built);

    // ---- a compiled page keeps nothing it no longer uses ------------------------------------------

    /// <summary>
    /// Lets go of a page's DOM, cascade, layout tree and script engine once its Lua is in the chip.
    /// </summary>
    /// <remarks>
    /// Measured at <b>5.6 MB per page</b>, 87.7 MB across sixteen consoles, 94% of it the script
    /// engine (<c>ScriptedScreensHtml.Bench --retained</c>). None of it is read again: <c>Update</c>
    /// returns as soon as a compiled run exists, clicks go to the chip, and values come back as slot
    /// writes. It still costs, though - the collector marks every live byte on every collection, and
    /// the mark is what decides how LONG a pause is. Allocation rate only decides how often.
    ///
    /// Anything that does need the page again rebuilds it from <c>_source</c>, which is the same call
    /// that made it in the first place. That is why this is safe to do at all: nothing here is the
    /// only copy of anything.
    /// </remarks>
    private void ReleaseWorkingSet()
    {
        if (_built == null && _script == null) return;

        var nodes = _built?.NodeOf.Count ?? 0;
        _script?.Dispose();
        _script = null;
        _built = null;
        _panel = null;
        _content = null;
        _byId = new Dictionary<string, VisualElement>(System.StringComparer.Ordinal);
        _shapes = new Dictionary<string, SvgShape>(System.StringComparer.Ordinal);
        _svgs.Clear();
        _animations.Clear();
        _boxes.Clear();
        _boxScratch.Clear();
        // The compiled slot table is what serves every later payload; it references nothing released.
        _dataPending = null;
        if (_muted.Count > 0)
        {
            lock (Tweens.Shared) foreach (var ve in _muted) Tweens.Override.Remove(ve);
            _muted.Clear();
        }
        _released = true;

        if (HtmlConfig.Diagnostics)
            ScriptedScreensHtmlPlugin.Log?.LogInfo(
                $"html \"{ElementId}\": released the page's working set ({nodes} node(s), dom + cascade + layout + engine); "
                + "it rebuilds from source if anything needs it again");
    }

    /// <summary>True while this page's working set has been let go because it compiled.</summary>
    private bool _released;
    /// <summary>The page is its chip's now (a plain compile, Install): nothing here runs for it.</summary>
    private bool _handedOver;
    /// <summary>Set when a page compiles; acted on next Update, once its structure has gone out.</summary>
    private bool _releasePending;
    /// <summary>When the compiled chunk last ran a frame, so it runs at the rate the renderer draws.</summary>
    private float _lastCompiledTick;
    /// <summary>Reused per send; only the slots whose value actually moved go in it.</summary>
    private readonly List<SS.UiProp> _propScratch = new(32);
    private readonly List<SS.UiProp> _easedScratch = new(8);
    private readonly List<SS.UiProp> _easeTiming = new(8);
    /// <summary>Elements whose emitter tween this surface muted, so a rebuilt page can unmute them: the override table is static and keyed by element.</summary>
    private readonly List<VisualElement> _muted = new();

    /// <summary>
    /// Puts the page back when something needs it: a capture, a rebuild, or the compiled run giving up.
    /// </summary>
    /// <returns>False when there is no source to rebuild from, which should not happen.</returns>
    private bool Rehydrate()
    {
        if (_built != null) return true;
        if (string.IsNullOrEmpty(_source)) return false;
        if (HtmlConfig.Diagnostics)
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html \"{ElementId}\": rebuilding the page - something asked for it after it was released");
        _released = false;
        Build();
        // The rebuilt page must emit the same slots the compiled table writes, or the values the
        // console keeps receiving would name nothing in the new structure: every id is Driven again
        // by the build, and the opacity groups the data named are named again here.
        if (_built != null) foreach (var id in _dataDriven) { _built.Driven.Add(id); _built.NamedGroups.Add(id); }
        // A data page needed only that one emit (a capture): its values come from the table
        // (DataSlots.Overlay), and nothing else will ask for the page again.
        if (_dataSlots != null) _releasePending = true;
        return _built != null;
    }

    private void BuildInner()
    {
        {
            var size = LayoutSize();
            HtmlRenderer.SurfaceAspect = size.x > 0f ? size.y / size.x : 1f;
            // Every console reports a 460x460 canvas whatever its physical shape, so the only thing
            // that knows a console is tall is the WORLD transform of its rect. If that is not valid
            // yet - which is exactly the case on a surface built inside a capture's own call - the
            // aspect falls back to the square canvas and the page lays out for the wrong screen.
            if (HtmlConfig.Diagnostics)
            {
                var rt = (RectTransform)transform;
                var r = rt.rect;
                ScriptedScreensHtmlPlugin.Log?.LogInfo(
                    $"html: \"{PageKey}\" laying out {size.x:0.#}x{size.y:0.#} (aspect {HtmlRenderer.SurfaceAspect:0.###}), " +
                    $"rect {r.width:0.#}x{r.height:0.#}, lossyScale {rt.lossyScale.x:0.###},{rt.lossyScale.y:0.###}");
            }
        }
        var face = FontLibrary.Default();
        ResolvedStyle.DefaultFace = face;
        var built = HtmlRenderer.Build(_source, face);
        foreach (var w in built.Warnings)
            ScriptedScreensHtmlPlugin.Log?.LogWarning(w);

        _designWidth = built.ViewportWidth;
        _content = built.Root;
        _panel = new Panel(_content);
        _byId = built.ById;
        _shapes = built.Shapes;
        _built = built;
        // So a page built again - a capture, the same push twice, fifteen consoles showing it -
        // finds its compile rather than making it again.
        PageCompiler.Remember(built, _source);
        AttachLayouts(built);

        _script?.Dispose();
        _script = null;
        if (HtmlConfig.Diagnostics) ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: page built, script {built.Script.Length} chars, {built.ById.Count} elements");
        // Which elements the script drives with a transform or an opacity, so the emitter names
        // their wrapping groups and those numbers become addressable slots. Everything else an
        // element's box already carries its id for. Cheap: one AST walk per build, and the set is
        // the handful a page really animates - four on the game page.
        NameDrivenGroups(built);
        // A page with no script is driven by data, and any element with an id may be what a key
        // names. Driven from the first emit, so that scene already carries every slot a key can
        // write - even a zero radius or width - and the first payload compiles against the very
        // next emit, with no emit spent only on naming. Groups are not named here: a named node
        // makes the renderer retain its props, so only the data that needs one names it (BindById).
        if (string.IsNullOrWhiteSpace(built.Script))
            foreach (var id in built.ById.Keys)
                if (!id.StartsWith("__", StringComparison.Ordinal)) built.Driven.Add(id);

        if (!string.IsNullOrWhiteSpace(built.Script))
        {
            _script = new ScriptHost(
                id => _byId.TryGetValue(id, out var e) ? e : null,
                id => _shapes.TryGetValue(id, out var sh) ? sh : null,
                id => _byId.TryGetValue(id, out var e2) && built.NodeOf.TryGetValue(e2, out var n) ? n : null,
                built.Query,
                built.Reclass,
                built.Rules,
                m => ScriptedScreensHtmlPlugin.Log?.LogWarning(m),
                (parentId, html) =>
                {
                    if (_byId.TryGetValue(parentId, out var p) && built.NodeOf.TryGetValue(p, out var pn))
                    {
                        HtmlRenderer.AppendFragment(p, pn, html, built);
                        AttachLayouts(built);
                        _dirty = true; _dDom++;
                        Wake();
                    }
                    else ScriptedScreensHtmlPlugin.Log?.LogWarning($"js: appendChild: no element \"{parentId}\"");
                },
                id =>
                {
                    if (_byId.TryGetValue(id, out var r))
                    {
                        HtmlRenderer.Remove(r, built);
                        _dirty = true; _dDom++;
                        Wake();
                    }
                },
                SetInputValue,
                WantClicks,
                SetScroll,
                (parentId, html, beforeId) =>
                {
                    if (_byId.TryGetValue(parentId, out var p) && built.NodeOf.TryGetValue(p, out var pn))
                    {
                        HtmlRenderer.InsertFragment(p, pn, html, beforeId, built);
                        AttachLayouts(built);
                        _dirty = true; _dDom++;
                        Wake();
                    }
                });
        }
        _script?.Attach(built, () => { _dirty = true; _dOther++; Wake(); }, LayoutSize(), StartAnimation, CancelAnimation);
        if (_script != null)
            _script.TryMorph = (id, html) =>
            {
                if (!_byId.TryGetValue(id, out var target) || !built.NodeOf.TryGetValue(target, out var targetNode))
                    return false;
                // a restyled element starts without a transition (a browser's innerHTML makes a new one),
                // except a running animation, which keeps its state (and the scene its structure)
                if (!HtmlRenderer.Morph(target, targetNode, html, built, ve =>
                    {
                        var rec = built.CssOf(ve);
                        if (!rec.ContainsKey("animation") && !rec.ContainsKey("animation-name")) _tweens.Forget(ve);
                    }))
                    return false;
                AttachLayouts(built);
                _dirty = true; _dDom++; _morphs++;
                Wake();
                return true;
            };
        // <link rel=stylesheet href> and <script src>: fetched the way ScriptedScreens fetches
        // an image, then the sheet is inlined and the page rebuilt, or the script run after
        // the inline ones. ponytail: http(s) only, no caching, 15 s timeout.
        foreach (var href in built.ExternalStyles)
            if (_fetched.Add("css:" + href)) StartCoroutine(Fetch(href, css => { _source = InlineStylesheet(_source, href, css); Build(); }));
        foreach (var href in built.ExternalImports)
            if (_fetched.Add("css:" + href)) StartCoroutine(Fetch(href, css => { _source = InlineImport(_source, href, css); Build(); }));
        foreach (var (urls, code) in built.Modules)
            StartCoroutine(RunModule(urls, code));
        foreach (var src in built.ExternalScripts)
            if (_fetched.Add("js:" + src)) StartCoroutine(Fetch(src, js => _script?.Run(src.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ? HtmlRenderer.StripModuleSyntax(js) : js)));
        _svgs.Clear();
        foreach (var shape in _shapes.Values)
        {
            if (shape.Owner != null && !_svgs.Contains(shape.Owner))
                _svgs.Add(shape.Owner);
        }

        _animations.Clear();
        built.AnimationAttached.Clear();
        AttachAnimations(built);

        _dirty = true;
        Wake();
        // Queue the page script NOW, not next frame: the worker runs queued work in order, so
        // data that arrives right after build (remembered data on a rebuilt host) reaches a
        // page whose handler is already registered. Sizes are snapshotted per frame, so a
        // script that reads clientWidth before layout sees 0 and tries again next frame.
        _scriptPending = false;
        _script?.Run(built.Script);

    }

    /// <summary>
    /// Emit the structure now rather than next Update. A screen capture rebuilds the surface
    /// and clones it inside one call, and the vector mod can only draw a scene it has been
    /// given by then. Called by the patch after the remembered data is applied, so the
    /// capture shows the page with its values, not the markup's placeholders.
    /// </summary>
    internal void EmitNow()
    {
        Hold();
        // A capture wants the page itself, so it comes back for this.
        if (_released) Rehydrate();
        var s0 = _structureSends;
        var p0 = _patchSends;
        lock (CascadeGate)
            EmitNowLocked();
        if (HtmlConfig.Diagnostics)
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html \"{ElementId}\": rebuilt on host {transform.parent?.gameObject.GetInstanceID()} at frame {Time.frameCount}, sent inside the call: "
                + (_structureSends > s0 ? "structure" : _patchSends > p0 ? "values only (same template)" : "nothing"));
    }

    private void EmitNowLocked()
    {
        Hold();
        // As in a browser: the page script loads and draws, then the remembered Lua data
        // arrives, then the script's frames. A data event before the script has run finds no
        // handler; bound values applied while it still runs are drawn over by its first render.
        if (_script != null && _scriptPending)
        {
            _scriptPending = false;
            _script.Run(_built?.Script ?? string.Empty);
            _script.RunSynchronously(Time.time, _byId, 2000); // waits for the run, applies its render
        }
        DrainInbox();
        // A capture builds and copies the page in one call. The script gets a few frames
        // first (its load work, a short timer, an animation frame), each waited for, so the
        // capture shows what the script drew rather than the bare markup.
        if (_script != null)
        {
            var w0 = Clock.Elapsed.TotalMilliseconds;
            var timedOut = 0;
            for (var k = 0; k < 6; k++)
                if (!_script.RunSynchronously(Time.time + k * 0.1f, _byId, 300)) timedOut++;
            if (HtmlConfig.Diagnostics)
                ScriptedScreensHtmlPlugin.Log?.LogInfo($"html \"{ElementId}\": capture warm-up {Clock.Elapsed.TotalMilliseconds - w0:0} ms, {timedOut} of 6 frames timed out");
            _script.Pump(); // what the last frame queued lands before the capture's emit
        }
        _emitLabel = "capture emit 1";
        EmitNowInline();
        if (_restore != null)
        {
            // the layout exists now: put the old surface's hover, press and focus back and emit once more
            ApplyRestoredPointer();
            DrainInbox();
            _emitLabel = "capture emit 2 (dirty after restore)";
            if (_dirty) EmitNowInline();
        }
        _emitLabel = null;
    }

    /// <summary>Set by the capture path so the emit it triggers can be told apart in the log.</summary>
    private string? _emitLabel;

    /// <summary>
    /// Says whether the scene this surface just produced carries a compiler sentinel.
    /// </summary>
    /// <remarks>
    /// Diagnostic for a bug that only the game reproduces: a capture's clone drew `#0F0000` where
    /// its heading belongs, while every offline path was clean and the live console was fine. The
    /// only way to find which emit carries it is to ask each one.
    /// </remarks>
    private void ReportSentinels(EmitResult? result, string where)
    {
        if (!HtmlConfig.Diagnostics) return;
        if (result?.Output is not { } o)
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html \"{ElementId}\": {where}: no output (template unchanged, so nothing was re-emitted)");
            return;
        }
        var text = new string(o.Chars, 0, o.Length);
        var n = 0;
        var at = 0;
        while ((at = text.IndexOf("#0F0", at, StringComparison.Ordinal)) >= 0) { n++; at += 4; }
        at = 0;
        while ((at = text.IndexOf("98765", at, StringComparison.Ordinal)) >= 0) { n++; at += 5; }
        ScriptedScreensHtmlPlugin.Log?.LogInfo(
            $"html \"{ElementId}\": {where}: {n} sentinel(s) in {o.Length} chars, {_boxes.Count} boxes, "
            + $"clone={_restore != null}, compiled={_compiled != null}");
    }

    private bool _scriptPending;

    /// <summary>
    /// Lay the page out now, translate it to scene text, and hand it to the vector mod.
    /// The scene is resent only when its text changed; the vector mod caches parsed scenes
    /// by source text, so an unchanged resend costs nothing.
    /// </summary>
    /// <summary>Ids of the ScriptedScreens elements created for img/video/audio, to update and remove.</summary>
    private readonly HashSet<string> _externals = new(StringComparer.Ordinal);
    /// <summary>The page node behind each external, by key, for routing a control's events.</summary>
    private readonly Dictionary<string, HtmlNode> _externalNodes = new(StringComparer.Ordinal);
    /// <summary>Current value of each control the user or the script changed: text, "true"/"false", a slider number, a select index.</summary>
    private readonly Dictionary<string, string> _inputValues = new(StringComparer.Ordinal);
    /// <summary>What each external element was last applied with, so an unchanged one is not re-sent (a re-send re-downloads an image).</summary>
    private readonly Dictionary<string, string> _externalState = new(StringComparer.Ordinal);

    /// <summary>
    /// img, video and audio: a ScriptedScreens image / media / sound element for each,
    /// applied through the real ApplyElementInternal. The id is hierarchical
    /// ("page/imgN"), which parents it under this page's host, so its rect is in this
    /// host's pixels: the design box scaled to the host. Removed when the box is gone.
    /// </summary>
    private void ApplyExternals(List<VectorEmitter.External> externals)
    {
        if (State is not SS.BoardState state)
            return;
        var rt = (RectTransform)transform;
        var layout = LayoutSize();
        var sx = layout.x > 0f ? rt.rect.width / layout.x : 1f;
        var sy = layout.y > 0f ? rt.rect.height / layout.y : 1f;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ext in externals)
        {
            var id = ElementId + "/" + ext.Key;
            seen.Add(id);
            var node = ext.Node;
            _externalNodes[ext.Key] = node;
            var url = HtmlRenderer.ResolveUrl(node.Attr("src") ?? FirstOfSrcset(node.Attr("srcset")) ?? string.Empty, _built);
            var props = new List<SS.UiProp>();
            if (node.Tag is "video" or "audio")
                props.Add(new SS.UiProp { Key = "url", Value = SS.UiValue.FromString(url) });
            var styleProps = new List<SS.UiProp>();
            string type;
            switch (node.Tag)
            {
                case "input":
                case "textarea":
                case "select":
                    type = ControlProps(node, ext, sx, props, styleProps);
                    break;
                case "video":
                    type = "media";
                    props.Add(new SS.UiProp { Key = "playing", Value = SS.UiValue.FromString(node.Attr("autoplay") != null ? "true" : "false") });
                    props.Add(new SS.UiProp { Key = "loop", Value = SS.UiValue.FromString(node.Attr("loop") != null ? "true" : "false") });
                    props.Add(new SS.UiProp { Key = "volume", Value = SS.UiValue.FromNumber(node.Attr("muted") != null ? 0f : 1f) });
                    break;
                case "audio":
                    type = "sound";
                    props.Add(new SS.UiProp { Key = "playing", Value = SS.UiValue.FromString(node.Attr("autoplay") != null ? "true" : "false") });
                    props.Add(new SS.UiProp { Key = "loop", Value = SS.UiValue.FromString(node.Attr("loop") != null ? "true" : "false") });
                    break;
                default:
                    type = "image";
                    break;
            }
            var rect = new SS.UiRect { Unit = SS.UiRectUnit.Pixels, X = ext.X * sx, Y = ext.Y * sy, W = ext.W * sx, H = ext.H * sy };
            var element = new SS.UiElement
            {
                Id = id,
                Type = type,
                Rect = rect,
                Props = props.ToArray(),
                Style = styleProps.ToArray(),
            };
            var stateKey = new System.Text.StringBuilder(type).Append('|').Append(element.Rect.X).Append(',').Append(element.Rect.Y).Append(',').Append(element.Rect.W).Append(',').Append(element.Rect.H);
            foreach (var pr in props) stateKey.Append('|').Append(pr.Key).Append('=').Append(PropText(pr.Value));
            foreach (var pr in styleProps) stateKey.Append('|').Append(pr.Key).Append('=').Append(PropText(pr.Value));
            var stateText = stateKey.ToString();
            if (_externalState.TryGetValue(id, out var last) && last == stateText)
                continue;
            _externalState[id] = stateText;
            try
            {
                // The element must be in the surface model, not just applied: after every
                // batch and rebuild ScriptedScreens re-sorts the hosts by z_index, and only
                // model elements take part, each moved to the last sibling. An applied-only
                // host sinks under the page and the image is never seen. In the model it is
                // ordered (one above the page), re-applied on a rebuild and cleared with the
                // surface. ponytail: local model only; a remote client is not sent it.
                var pageZ = 0;
                if (state.Surfaces.TryGetValue(Surface, out var model) && model != null)
                {
                    lock (model.PendingOpsLock)
                    {
                        if (model.Elements.TryGetValue(ElementId, out var pageElement) && pageElement != null)
                            foreach (var pr in pageElement.Props)
                                if (string.Equals(pr.Key, "z_index", StringComparison.OrdinalIgnoreCase) || string.Equals(pr.Key, "zIndex", StringComparison.OrdinalIgnoreCase))
                                    pageZ = (int)pr.Value.Number;
                    }
                }
                props.Add(new SS.UiProp { Key = "z_index", Value = SS.UiValue.FromNumber(pageZ + 1) });
                element.Props = props.ToArray();
                if (model != null)
                    lock (model.PendingOpsLock)
                        model.Elements[id] = element;
                SS.ApplyElementInternal(Board as Motherboard, Cartridge as CartridgeIntegratedCircuitLua, Visor as ProgrammableVisorGlasses, state, Surface, element);
                _externals.Add(id);
                if (state.SurfaceElementRoots.TryGetValue(Surface, out var roots) && roots.TryGetValue(id, out var host) && host != null)
                    host.transform.SetAsLastSibling();
            }
            catch (Exception ex)
            {
                ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: <{node.Tag}> as {type} failed: {ex.Message}");
            }
        }
        foreach (var id in new List<string>(_externals))
        {
            if (seen.Contains(id)) continue;
            _externalNodes.Remove(id.Substring(ElementId.Length + 1));
            if (state.Surfaces.TryGetValue(Surface, out var model) && model != null)
                lock (model.PendingOpsLock)
                    model.Elements.Remove(id);
            SS.RemoveElement(state, Surface, id);
            _externals.Remove(id);
            _externalState.Remove(id);
        }
    }

    /// <summary>
    /// The last emitted scene, as a file beside the DLL (`scenes/&lt;page&gt;.txt`): the one
    /// artefact that answers "what did the emitter actually produce" without a debugger.
    /// </summary>
    private void DumpScene(string scene)
    {
        try
        {
            var here = System.IO.Path.GetDirectoryName(typeof(HtmlSurface).Assembly.Location) ?? string.Empty;
            var dir = System.IO.Path.Combine(here, "scenes");
            System.IO.Directory.CreateDirectory(dir);
            var name = string.IsNullOrEmpty(ElementId) ? "page" : ElementId;
            foreach (var bad in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + ".txt"), scene);
            // the layout tree beside it: every element with its tag, id and laid-out box, including the
            // containers the scene never draws, so a misplaced box can be traced to the element that moved it
            if (_content != null && _built != null)
            {
                var sb = new StringBuilder();
                var origin = _content.worldBound.position;
                void Walk(VisualElement ve, int depth)
                {
                    var wb = ve.worldBound;
                    var tag = _built.NodeOf.TryGetValue(ve, out var n) ? (n.Tag ?? "#text") : "?";
                    sb.Append(' ', depth * 2).Append(tag).Append(' ').Append(ve.name).Append(ve is Label ? " [label]" : string.Empty)
                      .Append(" x=").Append((wb.x - origin.x).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(" y=").Append((wb.y - origin.y).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(" w=").Append(wb.width.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                      .Append(" h=").Append(wb.height.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
                    if (ve.resolvedStyle.display == DisplayStyle.None) sb.Append(" display=none");
                    if (ve.resolvedStyle.position == Position.Absolute) sb.Append(" abs");
                    sb.AppendLine();
                    foreach (var c in ve.Children()) Walk(c, depth + 1);
                }
                Walk(_content, 0);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + "-layout.txt"), sb.ToString());
            }
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning("html: scene dump failed: " + ex.Message);
        }
    }

    // ---- diagnostics: what each page costs, reported once a second when enabled ----
    private static readonly List<HtmlSurface> Surfaces = new();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _nextReport;
    private static int _framesAtReport;
    private static long _allUpdateTicks, _allEmitTicks;
    private const double ReportIntervalSeconds = 1.0;
    private int _emits;
    private int _emitsAtReport;
    private double _lastLayoutMs;
    private double _lastTranslateMs;
    private double _translateMsTotal;
    private int _lastNodes;
    private int _lastChars;

    /// <summary>Also driven by the plugin each frame, so the heap line keeps coming when no page exists: without a reading for "no consoles at all" there is no denominator for what a page costs.</summary>
    /// <summary>The allocation rate for the report line: the counter when this player serves one, else summed heap growth.</summary>
    private static string AllocLine()
    {
        if (FrameAlloc.Valid) return $"alloc {FrameAlloc.LastFrameBytes / 1024f:0} KB/frame; ";
        var (mb, collections) = FrameAlloc.TakeRate(ReportIntervalSeconds);
        return $"alloc {mb:0.0} MB/s, {collections} collected; ";
    }

    internal static void ReportIfDue()
    {
        FrameAlloc.Retry();   // the counter may only become available once the profiler's systems are up
        if (!HtmlConfig.Diagnostics)
            return;
        var now = Clock.Elapsed.TotalSeconds;
        if (now < _nextReport)
            return;
        _nextReport = now + ReportIntervalSeconds;
        var frames = Mathf.Max(1, Time.frameCount - _framesAtReport);
        _framesAtReport = Time.frameCount;
        double PerFrame(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / frames;
        ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: frames over 25 ms: {_slowFrames}, slowest {_worstFrame:0} ms, heap {System.GC.GetTotalMemory(false) / 1048576f:0} MB, gc {System.GC.CollectionCount(0)}, "
            + AllocLine()
            + $"game thread per frame: pages {PerFrame(_allUpdateTicks):0.00} ms (emit {PerFrame(_allEmitTicks):0.00}), "
            // THE SPEC: nothing of this mod runs after a page is compiled, so this must read 0.
            + $"work after compile: {CompiledRun.WorkAfterCompile} calls");
        CompiledRun.WorkAfterCompile = 0;
        _slowFrames = 0; _worstFrame = 0f;
        _allUpdateTicks = _allEmitTicks = 0;
        foreach (var page in Surfaces)
        {
            if (page == null) continue;
            // A page that compiled has let its DOM go, and skipping it here would have made the most
            // interesting page in the base the one that reports nothing - "no line" reading as
            // "not loaded" rather than as "costing nothing", which is the wrong way round.
            if (page._built == null)
            {
                if (page._handedOver)
                    ScriptedScreensHtmlPlugin.Log?.LogInfo($"html \"{page.ElementId}\": handed to its chip - its own vector elements and Lua on the chip's tick; nothing of this mod runs for it");
                else if (page._released)
                    ScriptedScreensHtmlPlugin.Log?.LogInfo(
                        $"html \"{page.ElementId}\": compiled and released - no layout, script, translate or emit; "
                        + $"sent {page._patchSends} patches ({page._patchSlots} values), heap {System.GC.GetTotalMemory(false) / 1048576f:0} MB, gc {System.GC.CollectionCount(0)}");
                continue;
            }
            var emits = page._emits - page._emitsAtReport;
            page._emitsAtReport = page._emits;
            ScriptedScreensHtmlPlugin.Log?.LogInfo(
                $"html \"{page.ElementId}\": {emits / ReportIntervalSeconds:0.0} emits/s, {(page._hiddenNow ? "hidden, " : string.Empty)}last {page._lastLayoutMs + page._lastTranslateMs:0.0} ms "
                + $"(layout {page._lastLayoutMs:0.00} + copy {page._lastCopyMs:0.00}, translate {page._lastTranslateMs:0.0}; page thread {page._workerMsTotal / ReportIntervalSeconds:0.0} ms/s; game thread waited {page._heldMs:0.00} ms), {page._lastNodes} nodes / {page._lastChars / 1024f:0.0} KB, "
                + $"{page._tweens.Count} tweens, main {page._updateTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / ReportIntervalSeconds / Mathf.Max(1f, Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 60f):0.00} ms/frame, awake {page._awakeCount} frames, sent: {page._structureSends} structures {page._patchSends} patches ({page._patchSlots} values), {page._morphs} in-place, script {(page._script != null ? page._script.LastFrameMs : 0f):0.0} ms/frame, {page._externals.Count} externals, {page._animations.Count} runners, kept: {(page._built != null ? page._built.NodeOf.Count : 0)} nodes {(page._built != null ? page._built.CssCount : 0)} records made {page._tweens.Shown} snaps {(page._script != null ? page._script.CacheSizes : 0)} cached, heap {System.GC.GetTotalMemory(false) / 1048576f:0} MB, gc {System.GC.CollectionCount(0)}, dirty: script {page._dScript} anim {page._dAnim} tween {page._dTween} dom {page._dDom} other {page._dOther}, gate: {page._gateSkips} skipped, data-direct: {page._dataFastTicks}, data-refused: {(page._dataRefused ?? "-")}, data-dropped: {page._dataDropped}, {page.GateWhy()}"
                + (_perThreadAlloc ? $", allocated per emit: step {page._allocStep / 1024f / Mathf.Max(1, emits):0} KB, layout {page._allocLayout / 1024f / Mathf.Max(1, emits):0} KB, copy {page._allocCopy / 1024f / Mathf.Max(1, emits):0} KB, translate {page._allocTranslate / 1024f / Mathf.Max(1, emits):0} KB, send {page._allocSend / 1024f / Mathf.Max(1, emits):0} KB (of translate: emit {page._allocEmit / 1024f / Mathf.Max(1, emits):0} KB, split {page._allocSplit / 1024f / Mathf.Max(1, emits):0} KB)"
                    // Mono has no per-thread counter, so there is nothing to divide between phases.
                    // Printing the heap delta per phase looked like attribution and was noise.
                    : ", per-phase allocation: unavailable on this runtime"));
            page._dScript = page._dAnim = page._dTween = page._dDom = page._dOther = page._gateSkips = 0;
            page._structureSends = page._patchSends = page._patchSlots = page._morphs = 0;
            page._allocStep = page._allocLayout = page._allocCopy = page._allocTranslate = page._allocSend = page._allocEmit = page._allocSplit = 0;
            page._updateTicks = 0; page._awakeCount = 0; page._translateMsTotal = 0; page._heldMs = 0; page._workerMsTotal = 0;
        }
    }

    // ---- translation off the game thread ----
    // The game thread lays the page out and copies what the translator reads (OffThread.Capture);
    // a worker translates the copy, splits it into a template and values and decides what to send;
    // the game thread hands that to the vector mod on a later frame. While a job runs nothing
    // changes the page: Update waits, and every entry point that would change it finishes the job
    // first (Hold), which normally costs nothing since a job ends long before the next frame.

    private sealed class EmitResult
    {
        public VectorEmitter.Output Output = null!;
        public double TranslateMs;
        public bool Stale;
        public SS.UiProp[]? Patch;
        public string? Structure;
        public SS.UiProp[]? Values;
        public string? Why;
    }

    /// <summary>The build this surface last tried to compile, so a failure is not retried every frame.</summary>
    private HtmlRenderer.Result? _compileTried;
    /// <summary>
    /// What the page thread compiled, for FinishJob to install on the game thread: the build and size
    /// compiled, and the scene template and values the compile bound (the structure the chunk writes
    /// into, when the compile does not bring its own).
    /// </summary>
    private (HtmlRenderer.Result Built, Vector2 Size, string Template, Dictionary<string, SceneSlots.Value> Values)? _compileReady;
    /// <summary>Set by the game thread when it needs the page (Hold): a compile running on the page thread stops at its next step.</summary>
    private volatile bool _compileStop;
    private readonly Dictionary<VisualElement, OffThread.Box> _boxes = new();
    private readonly List<VisualElement> _boxScratch = new();
    private double _lastCopyMs;
    // bytes allocated per phase since the last diagnostics line: where a frame's garbage comes from
    private long _allocStep, _allocLayout, _allocCopy, _allocTranslate, _allocSend, _allocEmit, _allocSplit;
    // Mono does not implement the per-thread counter (it answers 0), so fall back to the heap
    // total: noisier, since other threads allocate too, but a frame here allocates megabytes.
    private static bool _perThreadAlloc = true;
    private static long Allocated()
    {
        if (_perThreadAlloc)
        {
            var n = System.GC.GetAllocatedBytesForCurrentThread();
            if (n > 0) return n;
            _perThreadAlloc = false;
        }
        return System.GC.GetTotalMemory(false);
    }

    /// <summary>Waits for the page thread's frame and hands its result on, before the game thread touches the page. Game thread.</summary>
    private void Hold()
    {
        if (_pageState == PageIdle)
            return;
        if (_pageState == PageRunning)
        {
            // A compile in this frame takes seconds, and waiting it out here froze the game: it
            // stops at its next step instead, puts the page back, and a later frame compiles again.
            _compileStop = true;
            var w0 = Clock.Elapsed.TotalMilliseconds;
            _pageDone.Wait();
            _heldMs += Clock.Elapsed.TotalMilliseconds - w0;
        }
        FinishJob();
    }

    /// <summary>Runs what was posted for the page thread, here and now. Game thread, page thread idle.</summary>
    private void DrainInbox()
    {
        while (_inbox.TryDequeue(out var work))
            work();
    }

    private const char SceneNewline = (char)10;
    private float _lastVerify;
    private string? _verifyScene;

    /// <summary>
    /// Translates the page again without reusing anything kept from the frame before, and says so
    /// if the two differ. The reuse is what makes an animated page cheap, and its failure - text
    /// replayed for an element whose input the check cannot see - shows up as a console quietly
    /// displaying the wrong thing. Once a second, and only when asked for.
    /// </summary>
    private void VerifyCache(VectorEmitter.Output output, Vector2 layout, float now)
    {
        if (!HtmlConfig.VerifyEmitCache || OffThread.Seconds - _lastVerify < 1f)
            return;
        _lastVerify = OffThread.Seconds;
        var cached = new string(output.Chars, 0, output.Length);
        VectorEmitter.NoCache = true;
        try
        {
            var fresh = VectorEmitter.Emit(_built!, _content!, layout.x, layout.y, _tweens, now, ScrollSet);
            _verifyScene = new string(fresh.Chars, 0, fresh.Length);
        }
        finally { VectorEmitter.NoCache = false; }
        if (string.Equals(cached, _verifyScene, StringComparison.Ordinal))
            return;
        var a = cached.Split(SceneNewline);
        var b = _verifyScene!.Split(SceneNewline);
        var k = 0;
        while (k < a.Length && k < b.Length && a[k] == b[k]) k++;
        ScriptedScreensHtmlPlugin.Log?.LogWarning(
            $"html \"{ElementId}\": the reused translation differs from a fresh one at line {k}. Reused: {(k < a.Length ? a[k] : "(end)")} | fresh: {(k < b.Length ? b[k] : "(end)")}");
    }

    private double _heldMs;
    private float _lastPatchDump;

    /// <summary>Lays the page out and translates it here and now, on the game thread (a capture needs the scene inside the call).</summary>
    private void EmitNowInline()
    {
        Hold();
        DrainInbox();
        if (_content == null || _panel == null || _built == null)
            return;
        _dirty = false;
        var t0 = Clock.Elapsed.TotalMilliseconds;
        Wake();
        var size = LayoutSize();
        _panel.Layout(size.x, size.y);
        var t1 = Clock.Elapsed.TotalMilliseconds;
        OffThread.Capture(_content, _built, _boxes, _boxScratch);
        var t2 = Clock.Elapsed.TotalMilliseconds;
        _lastLayoutMs = t1 - t0;
        _lastCopyMs = t2 - t1;
        _frameResult = Translate(size, Time.time, OffThread.Globals.Take(), HtmlConfig.Diagnostics, worker: false);
        // Read here, not after FinishJob: that consumes _frameResult, and a diagnostic that read it
        // afterwards found null and said nothing - which is how this bug stayed invisible for one
        // more cycle.
        if (_emitLabel != null) ReportSentinels(_frameResult, _emitLabel);
        _frameError = null;
        _pageState = PageDone;
        FinishJob();
    }

    /// <summary>The job: translation, template split and the send decision. Reads only the copies; touches only this surface's own send state.</summary>
    private EmitResult Translate(Vector2 layout, float now, OffThread.Globals globals, bool diagnostics, bool worker)
    {
        var r = new EmitResult();
        var c0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var wasActive = OffThread.Active;
        OffThread.Active = worker;
        OffThread.Boxes = _boxes;
        OffThread.Job = globals;
        OffThread.Stale = false;
        try
        {
            _tweens.Diff(_content!, _built!, now);
            if (_lastTemplate == null) _tweens.Epoch = now;
            var ea = Allocated();
            var output = VectorEmitter.Emit(_built!, _content!, layout.x, layout.y, _tweens, now, ScrollSet);
            _allocEmit += Allocated() - ea;
            VerifyCache(output, layout, now);
            r.Output = output;
            var sa = Allocated();
            var template = SceneSlots.Split(output.Chars, output.Length, _slotScratch, _slotPrefix);
            _allocSplit += Allocated() - sa;
            // The compiler needs the slot table, which only exists once the scene has been split,
            // so this is the first moment a page can be compiled - on the page's own thread and only
            // there. A compile takes seconds for an innerHTML page, and it used to take them on the
            // game thread's emit at push time, once per console. That emit (a push, a capture) now
            // draws the page as the interpreter does, and FinishJob installs this on a later frame.
            if (worker) CompileHere(layout, template);
            // A data page compiles here, against the emit that drew its first payload: the emitter
            // has just said what every slot really is, which is the only honest check there is -
            // what the table WOULD write, against what was drawn. Built in the same emit rather
            // than the next, so a label empty until its first value already has its slot.
            if (_dataPending != null && _script == null && _dataSlots == null && _built != null)
                CompileData(template);
            // Any emit after the compile - the proof tick's, a capture's - carries the data's values
            // (bools included) rather than whatever the page's DOM last held.
            _dataSlots?.Overlay(_slotScratch);
            if (template == _lastTemplate)
            {
                List<SS.UiProp>? patch = null;
                foreach (var kv in _slotScratch)
                {
                    if (_sentValues.TryGetValue(kv.Key, out var was) && was.Equals(kv.Value)) continue;
                    (patch ??= new List<SS.UiProp>()).Add(Prop(kv.Key, kv.Value));
                    _sentValues[kv.Key] = kv.Value;
                }
                r.Patch = patch?.ToArray();
                return r;
            }
            if (diagnostics && _lastTemplate != null && OffThread.Seconds - _lastWhyAt > 2f)
            {
                // why this is a structure and not a patch: the first line that differs
                _lastWhyAt = OffThread.Seconds;
                var was = _lastTemplate.Split('\n');
                var cur = template.Split('\n');
                var k = 0;
                while (k < was.Length && k < cur.Length && was[k] == cur[k]) k++;
                var la = k < was.Length ? was[k] : string.Empty;
                var lb = k < cur.Length ? cur[k] : string.Empty;
                var c = 0;
                while (c < la.Length && c < lb.Length && la[c] == lb[c]) c++;
                var from = Math.Max(0, c - 40);
                string Cut(string[] lines) => k < lines.Length ? lines[k].Substring(Math.Min(from, lines[k].Length), Math.Min(120, Math.Max(0, lines[k].Length - from))).Trim() : "(end)";
                r.Why = $"html \"{ElementId}\": new structure ({was.Length} -> {cur.Length} lines), first difference at line {k}: \"{Cut(was)}\" -> \"{Cut(cur)}\"; last in-place miss: {HtmlRenderer.LastMorphMiss ?? "none"}";
            }
            // The compiled structure keeps its own slot names - its chunk writes exactly those - so it
            // is never re-emitted from the page, re-split or given the other prefix.
            if (_compiled?.Structure == null)
            {
                // A new structure restarts the vector clock: running tweens are written against that moment.
                if (_tweens.Any && _lastTemplate != null)
                {
                    _tweens.Epoch = now;
                    output = VectorEmitter.Emit(_built!, _content!, layout.x, layout.y, _tweens, now, ScrollSet);
                    r.Output = output;
                }
                // A new structure takes the other slot names: its values, sent before it, must not land on
                // the structure still on screen (where the same name means another value). Two sets alternate,
                // so the vector mod's table stays bounded.
                if (_lastTemplate != null)
                    _slotPrefix = _slotPrefix == "L" ? "M" : "L";
                template = SceneSlots.Split(output.Chars, output.Length, _slotScratch, _slotPrefix);
                // Split again, so the data's values go over again: without this a new structure - the
                // compile tick's, whose bool named an opacity group - went out with the DOM's values,
                // a hidden alarm drawn shown until a payload next moved it.
                _dataSlots?.Overlay(_slotScratch);
            }
            _tweens.Epoch = now;
            _lastTemplate = template;
            _sentValues.Clear();
            var values = new SS.UiProp[_slotScratch.Count];
            var n = 0;
            foreach (var kv in _slotScratch)
            {
                values[n++] = Prop(kv.Key, kv.Value);
                _sentValues[kv.Key] = kv.Value;
            }
            r.Values = values;
            r.Structure = template;
            return r;
        }
        finally
        {
            r.Stale = OffThread.Stale;
            OffThread.Active = wasActive;
            OffThread.Boxes = null;
            r.TranslateMs = (System.Diagnostics.Stopwatch.GetTimestamp() - c0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }
    }

    /// <summary>
    /// Compiles a data page against the scene just split: maps and proves the keys sent so far, and
    /// copies out what a key sent later needs. The page is let go on the next Update. Page thread
    /// (or the game thread inside a capture), with the page not changing under it.
    /// </summary>
    private void CompileData(string template)
    {
        var built = _built!;
        var sample = new List<KeyValuePair<string, SS.UiValue>>(_dataPending!);
        // A new structure is sent under the other prefix (Translate, below the compile), and a line
        // with no id that a measured declaration moves has to be written under that name.
        var prefix = _slotPrefix;
        var sendPrefix = _lastTemplate == null || template == _lastTemplate ? prefix : prefix == "L" ? "M" : "L";
        try
        {
            // The lock: a bool's two states are two more layouts of this page (PageCompiler.ToggleOf),
            // and layout is one page at a time.
            lock (CascadeGate)
                _dataSlots = DataSlots.Compile(sample, _slotScratch, template, built.ById.Keys,
                    (id, available) => PageCompiler.BoxFor(built, id, available),
                    id => built.ById.TryGetValue(id, out var ve) ? TextCss(built, ve) : null,
                    _shapes.Keys,
                    id => PageCompiler.ToggleOf(built, id),
                    m => ScriptedScreensHtmlPlugin.Log?.LogWarning($"html \"{ElementId}\": {m}"),
                    (id, property, value) => DataSlots.Sample(built, id, property, value, prefix, ReshowBools),
                    prefix, sendPrefix);
            if (HtmlConfig.Diagnostics)
                ScriptedScreensHtmlPlugin.Log?.LogInfo(
                    $"html \"{ElementId}\": data compiled - {_dataSlots.Count} key(s) write slots directly; no layout, translate or emit per tick");
        }
        catch (Exception ex)
        {
            // Not a refusal - a defect. Said with its stack, and the page keeps what it showed rather
            // than going back to a layout per tick.
            _dataRefused = "the data compile threw";
            ScriptedScreensHtmlPlugin.Log?.LogError($"html \"{ElementId}\": data compile failed, later payloads are dropped: {ex}");
        }
        // only now: a measurement's re-cascade re-shows what the pending payload keeps shown
        _dataPending = null;
        _releasePending = true;
    }

    /// <summary>
    /// An element's record with the text properties a browser inherits filled in from its ancestors,
    /// as the emitter reads a label's (VectorEmitter.WithInherited): `tabular-nums` on the body
    /// monospaces every label under it. A copy only when something is added.
    /// </summary>
    private static Dictionary<string, string> TextCss(HtmlRenderer.Result built, VisualElement ve)
    {
        var own = built.CssOf(ve);
        Dictionary<string, string>? merged = null;
        for (var p = ve.parent; p != null; p = p.parent)
            foreach (var pair in built.CssOf(p))
                if (VectorEmitter.Inherits(pair.Key) && !own.ContainsKey(pair.Key) && (merged == null || !merged.ContainsKey(pair.Key)))
                    (merged ??= new Dictionary<string, string>(own, StringComparer.Ordinal))[pair.Key] = pair.Value;
        return merged ?? own;
    }

    private static SS.UiProp Prop(string key, SceneSlots.Value v) =>
        new() { Key = key, Value = v.IsNumber ? SS.UiValue.FromNumber(v.Number) : SS.UiValue.FromString(v.Text ?? string.Empty) };

    /// <summary>
    /// Sends what a compiled page's frame produced, through the same path a translated patch takes.
    /// </summary>
    /// <remarks>
    /// The values come from Lua rather than from a translation, and that is the only difference -
    /// the renderer cannot tell, and neither can anything between here and it. `snap` for the same
    /// reason a patch uses it: a browser does not ease, so neither should this.
    /// </remarks>
    private void SendCompiled(Dictionary<string, SceneSlots.Value> values)
    {
        if (State is not SS.BoardState state || values.Count == 0) return;

        // Only what moved. The chunk writes every slot it owns each frame - a page's draw() does not
        // know which numbers changed - so a patch carried all twenty-six every time. The emit path
        // has always diffed against _sentValues; the compiled path never did.
        _propScratch.Clear();
        _easedScratch.Clear();
        _easeTiming.Clear();
        foreach (var pair in values)
        {
            var now = pair.Value;
            if (_sentValues.TryGetValue(pair.Key, out var was) && was.Equals(now)) continue;
            _sentValues[pair.Key] = now;
            var prop = Prop(pair.Key, now);
            // A slot under a CSS transition goes in its own payload, without snap and with the
            // renderer's `ease` timing (vector 0.11.33): a property of the change, so it is stated on
            // every payload that moves the slot. Everything else snaps, as a browser does.
            if (_dataSlots != null && _dataSlots.TryEase(pair.Key, out var timing))
            {
                _easedScratch.Add(prop);
                // the same timing every time, so built once per slot
                if (!_easeValues.TryGetValue(pair.Key, out var ease))
                    _easeValues[pair.Key] = ease = SS.UiValue.FromArray(timing.Delay > 0f
                        ? new[] { SS.UiValue.FromNumber(timing.Dur), SS.UiValue.FromString(timing.Curve), SS.UiValue.FromNumber(timing.Delay) }
                        : new[] { SS.UiValue.FromNumber(timing.Dur), SS.UiValue.FromString(timing.Curve) });
                _easeTiming.Add(new SS.UiProp { Key = pair.Key, Value = ease });
            }
            else _propScratch.Add(prop);
        }
        if (_easedScratch.Count > 0)
        {
            VectorBridge.Data(Board, Cartridge, Visor, state, Surface, ElementId, SceneId,
                new SS.UiValue { Type = SS.UiValueType.Map, Map = Exact(_easedScratch, _easedArrays) }, null, snap: false,
                ease: new SS.UiValue { Type = SS.UiValueType.Map, Map = Exact(_easeTiming, _easeArrays) });
            _patchSends++;
            _patchSlots += _easedScratch.Count;
        }
        if (_propScratch.Count == 0) return;

        VectorBridge.Data(Board, Cartridge, Visor, state, Surface, ElementId, SceneId,
            new SS.UiValue { Type = SS.UiValueType.Map, Map = Exact(_propScratch, _propArrays) }, null, snap: HtmlConfig.SnapData);
        _patchSends++;
        _patchSlots += _propScratch.Count;
    }

    // One array per length, per list, reused for every payload of that length: a map is an array
    // and the renderer reads it to the end, and it copies what it keeps (SceneParser.ReadData), so
    // the array is free again when the call returns. Separate per list because one call can carry
    // two of the same length (the eased values and their timings).
    private readonly Dictionary<int, SS.UiProp[]> _propArrays = new(), _easedArrays = new(), _easeArrays = new(), _dataArrays = new();
    private readonly Dictionary<string, SS.UiValue> _easeValues = new(StringComparer.Ordinal);

    private static SS.UiProp[] Exact(List<SS.UiProp> list, Dictionary<int, SS.UiProp[]> arrays)
    {
        if (!arrays.TryGetValue(list.Count, out var array)) arrays[list.Count] = array = new SS.UiProp[list.Count];
        list.CopyTo(array);
        return array;
    }

    /// <summary>Hands a finished translation to the vector mod. Game thread.</summary>
    /// <summary>
    /// Compiles the page on its own thread, for FinishJob to install, and runs the compile probe.
    /// Once per build; a build of a page compiled before at this size finds it in PageCompiler's
    /// cache, so a capture or a second push costs a lookup here.
    /// </summary>
    private void CompileHere(Vector2 layout, string template)
    {
        var built = _built;
        if (built == null || _panel == null || _compileTried == built || string.IsNullOrWhiteSpace(built.Script)) return;
        // Whichever holds the chip. A console's host is a Motherboard and its `cartridge` is
        // null; only a tablet has one. Gating on the cartridge alone meant no console ever
        // reached this, which is why nothing happened the first time it was switched on.
        var run = HtmlConfig.RunCompiled && _compiled == null && (Cartridge ?? Board) != null;
        if (!run && !HtmlConfig.CompileProbe) return;
        _compileTried = built;
        PageCompiler.Cancelled = () => _compileStop;
        try
        {
            // Reports only: whether the compiler can handle this page, on this machine, under Mono.
            CompileProbe.Run(PageKey, built.Script);
            CompileProbe.Full(PageKey, built, _panel, layout, _slotScratch);
            // For the cache CompiledRun.Start reads on the game thread, which makes this console's
            // chunk from it by filling in the element the chunk writes to - named the same way this
            // mod names it, so the chunk's set_props lands on the surface this page already draws.
            if (run) PageCompiler.Compile(built, _panel, layout, _slotScratch, (Surface, ElementId, SceneId));
        }
        finally { PageCompiler.Cancelled = null; }
        // Stopped part way (the game thread needed the page): nothing was kept, so a later frame
        // compiles again.
        if (_compileStop) { _compileTried = null; return; }
        if (run) _compileReady = (built, layout, template, new Dictionary<string, SceneSlots.Value>(_slotScratch, StringComparer.Ordinal));
    }

    /// <summary>
    /// Hands a page the page thread compiled to its chip and sends the structure its chunk writes
    /// into. From the next update this surface stops laying out, running the script and
    /// translating, which is the whole point; a page that cannot be handed over carries on exactly
    /// as it does today. Game thread. False when nothing was installed.
    /// </summary>
    private bool Install((HtmlRenderer.Result Built, Vector2 Size, string Template, Dictionary<string, SceneSlots.Value> Values) ready,
                         SS.BoardState state)
    {
        // Only a compile the cache holds. CompiledRun.Start compiles whatever the cache lacks, and
        // here that would be the game thread's frame again.
        var holder = Cartridge ?? Board;
        if (_compiled != null || holder == null || ready.Built != _built || _panel == null
            || !PageCompiler.IsCompiled(ready.Built, ready.Size))
            return false;
        var l0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _compiled = CompiledRun.Start(PageKey, holder, ready.Built, _panel, ready.Size, ready.Values,
                                      (Surface, ElementId, SceneId));
        // What is left on this frame: the chunk's load into the chip, and its first run.
        if (HtmlConfig.Diagnostics)
            ScriptedScreensHtmlPlugin.Log?.LogInfo(
                $"html \"{ElementId}\": {(_compiled != null ? "installed" : "not installed")} on the game thread in "
                + $"{(System.Diagnostics.Stopwatch.GetTimestamp() - l0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0} ms "
                + "(the compile ran on the page thread)");
        if (_compiled == null) return false;
        if (_compiled.Plain)
        {
            // Handed over: the chunk made the page's own vector elements and runs on the chip's tick.
            // This surface lets the page go and switches its host off - the interpreter's scene is on
            // that host and must not draw beside the chunk's - so it gets no Update again.
            CompiledRun.HandedOver(PageKey, _source, _compiled.State);
            _handedOver = true;
            ReleaseWorkingSet();
            // and its thread ends: this page runs no frame again
            _pageStop = true;
            _pageWake.Set();
            _pageThread = null;
            transform.parent.gameObject.SetActive(false);
            // The chunk's scene shares this page's scene id, so the vector mod draws one of the two -
            // whichever structure came last - and switches the other's host off. A page this host drew
            // again after an earlier hand-over (a new source) switched the chunk's host off that way,
            // and the vector mod never switches a host back on.
            if (state.SurfaceElementRoots.TryGetValue(Surface, out var roots) && roots != null
                && roots.TryGetValue(ElementId + PlainPage.SceneSuffix, out var sceneHost) && sceneHost != null)
                sceneHost.SetActive(true);
            return true;
        }
        var template = ready.Template;
        var values = ready.Values;
        // A page whose markup the compiler laid out itself (every alternative and row at its
        // maximum, holes named) draws THAT structure, and its chunk writes those slot names:
        // the interpreter's emit of whichever state the page happened to be in would have
        // slots the chunk never writes and lack the ones it does.
        if (_compiled.Structure is { } compiledStructure && _compiled.StructureValues is { } opening)
        {
            template = compiledStructure;
            values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            foreach (var kv in opening) values[kv.Key] = kv.Value;
        }
        // The page's motion goes into the structure as expressions of t, so nothing computes
        // or sends it again - it IS the structure - and the slots it covers leave the value
        // table with it.
        if (_compiled.Expressions.Count > 0)
            template = Motion.Bake(template, _compiled.Expressions, values);
        // Everything above needed the page; from here nothing does. Released on the next update,
        // after this structure has gone out - the scene is what the chip writes into.
        _releasePending = true;
        _lastTemplate = template;
        _sentValues.Clear();
        var props = new SS.UiProp[values.Count];
        var n = 0;
        foreach (var kv in values)
        {
            props[n++] = Prop(kv.Key, kv.Value);
            _sentValues[kv.Key] = kv.Value;
        }
        _structureSends++;
        // the values first, so the structure never shows an unbound slot
        VectorBridge.Data(Board, Cartridge, Visor, state, Surface, ElementId, SceneId,
            new SS.UiValue { Type = SS.UiValueType.Map, Map = props }, null, snap: HtmlConfig.SnapData);
        VectorBridge.Structure(Board, Cartridge, Visor, state, Surface, ElementId, SceneId, template);
        _compiled.Resync();
        return true;
    }

    /// <summary>
    /// A structure or values just went out with the scene's resting values, so the chunk running
    /// this page resends everything on its next frame. This surface's chunk, or - when this is a
    /// capture's rebuild that has none - the live surface's, whose chunk is the one on screen.
    /// </summary>
    private void ResyncCompiled()
    {
        var run = _compiled;
        if (run == null && !string.IsNullOrEmpty(PageKey) && Current.TryGetValue(PageKey, out var live) && live != this)
            run = live._compiled;
        run?.Resync();
    }

    private void FinishJob()
    {
        if (_pageState != PageDone)
            return;
        _pageState = PageIdle;
        var r = _frameResult;
        var error = _frameError;
        _frameResult = null;
        _frameError = null;
        if (error != null)
        {
            // a frame that failed leaves no trustworthy send state: the next one sends everything
            ScriptedScreensHtmlPlugin.Log?.LogError($"html \"{ElementId}\": page frame failed: {error}");
            _lastTemplate = null;
            _dirty = true;
            return;
        }
        _tweens.ApplyHides();
        if (r == null)
        {
            if (OffThread.ResolveFonts()) { _dirty = true; _dOther++; }
            return;
        }
        if (OffThread.ResolveFonts()) { _dirty = true; _dOther++; }
        if (r.Stale || r.Output == null)
        {
            // the tree changed under the job: try again with a full send
            _lastTemplate = null;
            _dirty = true;
            _dOther++;
            // A compile in this job was laid out on the same stale answers (a font still on its
            // way): it is dropped, from the cache too, and made again on a later frame.
            if (_compileReady is { } stale)
            {
                PageCompiler.Forget(stale.Built, stale.Size);
                _compileReady = null;
                _compileTried = null;
            }
            return;
        }
        var output = r.Output;
        _lastTranslateMs = r.TranslateMs;
        _translateMsTotal += r.TranslateMs;
        _lastNodes = output.Nodes;
        // Output.Length, never Output.Scene.Length: the latter materialises the whole scene
        // (new string(Chars, 0, Length)) to read a number this already holds, on the game thread,
        // every emit, for one diagnostics line that is off by default. 20 KB a frame on a 10 KB
        // scene - the exact garbage Output's buffer exists to avoid, put back one line away.
        _lastChars = output.Length;
        _emits++;
        foreach (var w in output.Warnings)
            ScriptedScreensHtmlPlugin.Log?.LogWarning(w);
        if (r.Why != null)
            ScriptedScreensHtmlPlugin.Log?.LogInfo(r.Why);
        if (!IsCurrent)
            return;
        var bs = Allocated();
        ApplyExternals(output.Externals);
        if (State is not SS.BoardState state)
            return;
        if (_compileReady is { } ready)
        {
            _compileReady = null;
            // Installed, its structure went out in place of this job's scene.
            if (Install(ready, state)) return;
        }
        if (r.Structure == null)
        {
            if (r.Patch == null)
                return;
            // the scene as drawn now (structure plus values), at most every two seconds
            if (HtmlConfig.DumpScenes && OffThread.Seconds - _lastPatchDump > 2f)
            {
                _lastPatchDump = OffThread.Seconds;
                DumpScene(output.Scene);
            }
            VectorBridge.Data(Board, Cartridge, Visor, state, Surface, ElementId, SceneId,
                new SS.UiValue { Type = SS.UiValueType.Map, Map = r.Patch }, null, snap: HtmlConfig.SnapData);
            _patchSends++;
            _patchSlots += r.Patch.Length;
            ResyncCompiled();
            _allocSend += Allocated() - bs;
            return;
        }
        _structureSends++;
        // the values first, so the structure never shows an unbound slot
        VectorBridge.Data(Board, Cartridge, Visor, state, Surface, ElementId, SceneId,
            new SS.UiValue { Type = SS.UiValueType.Map, Map = r.Values ?? Array.Empty<SS.UiProp>() }, null, snap: HtmlConfig.SnapData);
        VectorBridge.Structure(Board, Cartridge, Visor, state, Surface, ElementId, SceneId, r.Structure);
        ResyncCompiled();
        if (HtmlConfig.Diagnostics)
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: emitted {output.Nodes} vector nodes, {output.Scene.Length} chars");
        if (HtmlConfig.DumpScenes)
            DumpScene(output.Scene);
        if (!_sceneLive)
        {
            // The first structure: data that arrived before it was dropped by the vector
            // mod (no scene to attach to), so everything forwarded so far goes again.
            _sceneLive = true;
            if (_forwarded.Count > 0)
            {
                // Not a name the structure has a slot for: its value just went with the structure, as
                // the page drew it, and a raw string sent after would overwrite it - a label keyed
                // by its element's id losing its tabular monospacing until the value next changed.
                var all = new List<SS.UiProp>(_forwarded.Count);
                foreach (var kv in _forwarded)
                    if (!_slotScratch.ContainsKey(kv.Key))
                        all.Add(new SS.UiProp { Key = kv.Key, Value = kv.Value });
                if (all.Count > 0) SendData(all);
            }
        }
    }

    private static string PropText(SS.UiValue v)
    {
        if (v.Type == SS.UiValueType.Array && v.Array != null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var item in v.Array) sb.Append(PropText(item)).Append('|');
            return sb.ToString();
        }
        return v.String ?? v.Number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The ScriptedScreens element behind an input, select or textarea, with its props and
    /// style from the page: text inputs and textareas are `textinput`, checkbox and radio
    /// their own types, range a `slider`, select a `select`. The current value comes from
    /// what the user or the script set, else from the markup.
    /// </summary>
    private string ControlProps(HtmlNode node, VectorEmitter.External ext, float sx, List<SS.UiProp> props, List<SS.UiProp> style)
    {
        var rs = ext.Ve.resolvedStyle;
        var css = _built?.CssOf(ext.Ve);
        var kind = node.Tag == "input" ? (node.Attr("type") ?? "text").ToLowerInvariant() : node.Tag;
        _inputValues.TryGetValue(ext.Key, out var current);
        string type;
        switch (kind)
        {
            case "range":
            {
                type = "slider";
                var min = Num(node.Attr("min"), 0f);
                var max = Num(node.Attr("max"), 100f);
                var value = Num(current ?? node.Attr("value"), (min + max) * 0.5f);
                props.Add(new SS.UiProp { Key = "value", Value = SS.UiValue.FromNumber(value) });
                props.Add(new SS.UiProp { Key = "min", Value = SS.UiValue.FromNumber(min) });
                props.Add(new SS.UiProp { Key = "max", Value = SS.UiValue.FromNumber(max) });
                if (Accent(css, out var fill)) style.Add(new SS.UiProp { Key = "fill", Value = SS.UiValue.FromString(VectorEmitter.Hex(fill)) });
                break;
            }
            case "select":
            {
                type = "select";
                var options = new List<SS.UiValue>();
                var selected = 0;
                var i = 0;
                foreach (var opt in Options(node))
                {
                    options.Add(SS.UiValue.FromString(OptionText(opt)));
                    if (opt.Attr("selected") != null) selected = i;
                    i++;
                }
                if (current != null && int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)) selected = idx;
                props.Add(new SS.UiProp { Key = "options", Value = SS.UiValue.FromArray(options.ToArray()) });
                props.Add(new SS.UiProp { Key = "selected", Value = SS.UiValue.FromNumber(selected) });
                break;
            }
            default:
            {
                type = "textinput";
                var value = current ?? (node.Tag == "textarea" ? TextOf(node) : node.Attr("value") ?? string.Empty);
                props.Add(new SS.UiProp { Key = "value", Value = SS.UiValue.FromString(value) });
                props.Add(new SS.UiProp { Key = "placeholder", Value = SS.UiValue.FromString(node.Attr("placeholder") ?? string.Empty) });
                props.Add(new SS.UiProp { Key = "title", Value = SS.UiValue.FromString(node.Attr("title") ?? node.Attr("placeholder") ?? "Enter text") });
                if (node.Attr("data-placeholder-color") is { } pc && StyleApplier.TryColor(pc, out var pcol))
                    style.Add(new SS.UiProp { Key = "placeholder_color", Value = SS.UiValue.FromString(VectorEmitter.Hex(pcol)) });
                break;
            }
        }
        // The page's own look for the control: background, text colour, font size.
        if (rs.backgroundColor.a > 0.002f) style.Add(new SS.UiProp { Key = "bg", Value = SS.UiValue.FromString(VectorEmitter.Hex(rs.backgroundColor)) });
        if (type is "textinput" or "select")
        {
            style.Add(new SS.UiProp { Key = "text", Value = SS.UiValue.FromString(VectorEmitter.Hex(rs.color)) });
            // the control pads its text; a font taller than about two thirds of the field is clipped
            style.Add(new SS.UiProp { Key = "font_size", Value = SS.UiValue.FromNumber(Mathf.Max(8f, Mathf.Min(rs.fontSize, ext.H * 0.62f) * sx)) });
        }
        return type;
    }

    private static bool Accent(Dictionary<string, string>? css, out Color colour)
    {
        colour = default;
        return css != null && css.TryGetValue("accent-color", out var v) && StyleApplier.TryColor(v.Trim(), out colour);
    }

    private static float Num(string? text, float fallback)
    {
        return text != null && float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }

    private static string TextOf(HtmlNode node)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in node.Children)
            if (c.IsText) sb.Append(c.Text); else sb.Append(TextOf(c));
        return sb.ToString().Trim();
    }

    private static string OptionText(HtmlNode opt) => TextOf(opt);

    /// <summary>The selectable options, through optgroups, skipping disabled ones.</summary>
    private static IEnumerable<HtmlNode> Options(HtmlNode select)
    {
        foreach (var c in select.Children)
        {
            if (c.Tag == "option" && c.Attr("disabled") == null) yield return c;
            else if (c.Tag == "optgroup" && c.Attr("disabled") == null)
                foreach (var o in c.Children) if (o.Tag == "option" && o.Attr("disabled") == null) yield return o;
        }
    }

    /// <summary>What a select option reports: its value attribute, else its text.</summary>
    private static string OptionValue(HtmlNode select, int index)
    {
        var i = 0;
        foreach (var opt in Options(select))
        {
            if (i == index) return opt.Attr("value") ?? OptionText(opt);
            i++;
        }
        return string.Empty;
    }

    private static string? FirstOfSrcset(string? srcset)
    {
        if (string.IsNullOrEmpty(srcset)) return null;
        var first = srcset!.Split(',')[0].Trim().Split(' ')[0];
        return first.Length > 0 ? first : null;
    }

    /// <summary>
    /// A control's event from ScriptedScreens (a text field's edit, a click on a checkbox, a
    /// slider or select change). Updates the stored value, re-applies the control, and hands
    /// the page script an `input`/`change` event on the element. Returns the name and value
    /// for the page's Lua on_change ("name=value").
    /// </summary>
    internal bool OnExternalInput(string key, string evt, string value, out string name, out string delivered)
    {
        Hold();
        lock (CascadeGate)
            return OnExternalInputLocked(key, evt, value, out name, out delivered);
    }

    private bool OnExternalInputLocked(string key, string evt, string value, out string name, out string delivered)
    {
        Hold();
        name = key;
        delivered = string.Empty;
        if (!_externalNodes.TryGetValue(key, out var node))
            return false;
        name = node.Attr("name") ?? key;
        var kind = node.Tag == "input" ? (node.Attr("type") ?? "text").ToLowerInvariant() : node.Tag;
        string stored;
        switch (kind)
        {
            case "select":
            {
                if (!string.Equals(evt, "change", StringComparison.OrdinalIgnoreCase)) return false;
                stored = value.Trim();
                delivered = int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) ? OptionValue(node, idx) : stored;
                break;
            }
            default:
            {
                if (!string.Equals(evt, "change", StringComparison.OrdinalIgnoreCase)) return false;
                stored = value ?? string.Empty;
                if (kind == "number" && float.TryParse(stored.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                {
                    // min/max/step as a browser applies them on change
                    var min = Num(node.Attr("min"), float.NegativeInfinity);
                    var max = Num(node.Attr("max"), float.PositiveInfinity);
                    var step = Num(node.Attr("step"), 0f);
                    if (step > 0f) num = (float.IsInfinity(min) ? 0f : min) + Mathf.Round((num - (float.IsInfinity(min) ? 0f : min)) / step) * step;
                    num = Mathf.Clamp(num, min, max);
                    stored = num.ToString(CultureInfo.InvariantCulture);
                }
                delivered = stored;
                break;
            }
        }
        _inputValues[key] = stored;
        node.Attributes["data-touched"] = string.Empty; // :user-invalid / :user-valid: the user has interacted
        if (node.Tag is "input" or "textarea") node.Attributes["value"] = stored;
        Recascade(node);
        _externalState.Remove(ElementId + "/" + key);
        _dirty = true;
        Wake();
        _script?.EmitInput(key, delivered);
        return true;
    }

    /// <summary>
    /// A click on a page-drawn checkbox or radio (it arrives as the page's own click with the
    /// node id as value). Flips the node's `checked`, re-emits, tells the script, and returns
    /// the name and value for Lua. False when the id is not such a control.
    /// </summary>
    internal bool OnControlClick(string key, out string name, out string delivered)
    {
        Hold();
        lock (CascadeGate)
            return OnControlClickLocked(key, out name, out delivered);
    }

    private bool OnControlClickLocked(string key, out string name, out string delivered)
    {
        Hold();
        name = key;
        delivered = string.Empty;
        if (_built == null || !_byId.TryGetValue(key, out var ve) || !_built.NodeOf.TryGetValue(ve, out var node))
            return false;
        // <label for=x>: the click goes to the control it names.
        if (node.Tag == "label" && node.Attr("for") is { } target && target != key)
        {
            if (OnControlClick(target, out name, out delivered)) return true;
            SetFocus(target);
            return false;
        }
        // <img usemap>: the area under the pointer gets the click
        if (node.Tag == "img" && node.Attr("usemap") is { } usemap && _content != null)
        {
            var mapName = usemap.TrimStart('#');
            HtmlNode? map = null;
            foreach (var n in _built.NodeOf.Values) if (n.Tag == "map" && (n.Attr("name") == mapName || n.Attr("id") == mapName)) { map = n; break; }
            var box = ve.worldBound; box.position -= _content.worldBound.position;
            var p = _pointerPage - box.position;
            // ponytail: area coords are in the image's own pixels; the box's px stand in (exact when width/height match the picture)
            if (map != null && AreaAt(map, p) is { } area && area.Attr("id") is { } areaId)
            {
                _script?.EmitEvent(areaId, "click");
                name = areaId;
                return false;
            }
        }
        // a datalist option: the value goes into the input, the list closes
        if (node.Attr("data-datalist") is { } forInput)
        {
            var text = node.Attr("data-value") ?? string.Empty;
            SetInputValue(forInput, text);
            if (_byId.TryGetValue(forInput, out var ive) && _built.NodeOf.TryGetValue(ive, out var inode)) { inode.Attributes["value"] = text; Recascade(inode); }
            _script?.EmitInput(forInput, text);
            CloseDatalist();
            return false;
        }
        // popovertarget: the button shows, hides or toggles the popover it names
        if (node.Attr("popovertarget") is { } popTarget && _byId.TryGetValue(popTarget, out var pve) && _built.NodeOf.TryGetValue(pve, out var pnode))
        {
            var action = (node.Attr("popovertargetaction") ?? "toggle").ToLowerInvariant();
            var isOpen = pnode.Attr("data-popover-open") != null;
            var open = action == "show" || (action == "toggle" && !isOpen);
            if (open) pnode.Attributes["data-popover-open"] = string.Empty; else pnode.Attributes.Remove("data-popover-open");
            pve.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            Recascade(pnode);
            _dirty = true;
            Wake();
            _script?.EmitEvent(popTarget, "toggle");
            return false;
        }
        // <summary>: toggles its details.
        if (node.Tag == "summary" && node.Parent is { Tag: "details" } details && details.Attr("id") is { } detailsId && _byId.TryGetValue(detailsId, out var dve))
        {
            if (details.Attr("open") != null) details.Attributes.Remove("open"); else details.Attributes["open"] = string.Empty;
            HtmlRenderer.ShowDetails(dve, details, _built);
            Recascade(details);
            _dirty = true;
            Wake();
            _script?.EmitEvent(detailsId, "toggle");
            return false;
        }
        var control = node.Attr("data-control");
        if (control == null || control is "progress" or "meter")
            return false;
        name = node.Attr("name") ?? key;
        var on = control == "radio" || node.Attr("checked") == null;
        SetChecked(node, on);
        if (control == "radio" && on && node.Attr("name") is { } group)
        {
            foreach (var kv in _built.NodeOf)
                if (kv.Value != node && kv.Value.Attr("data-control") == "radio" && kv.Value.Attr("name") == group)
                    SetChecked(kv.Value, false);
        }
        delivered = on ? "true" : "false";
        _dirty = true;
        Wake();
        _script?.EmitInput(key, delivered);
        return true;
    }

    /// <summary>Sets the node's checked state and re-runs its cascade, so `:checked` rules take effect.</summary>
    private void SetChecked(HtmlNode node, bool on)
    {
        if (on) node.Attributes["checked"] = string.Empty;
        else node.Attributes.Remove("checked");
        if (_built != null)
            foreach (var kv in _built.NodeOf)
                if (kv.Value == node) { _built.Reclass(kv.Key, node.Attr("class") ?? string.Empty); break; }
    }

    private readonly HashSet<string> _fetched = new(StringComparer.Ordinal);

    private static System.Collections.IEnumerator Fetch(string url, Action<string> done)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: external \"{url}\": only http(s) urls are fetched");
            yield break;
        }
        using var req = UnityWebRequest.Get(new Uri(url));
        req.timeout = 15;
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: external \"{url}\" failed: {req.error}");
            yield break;
        }
        done(req.downloadHandler.text ?? string.Empty);
    }

    /// <summary>The &lt;link&gt; for this href becomes a &lt;style&gt; with the fetched text, so a rebuild sees it as page CSS.</summary>
    /// <summary>The @import statement naming <paramref name="href"/> replaced by the fetched sheet, in place, so its rules keep their position.</summary>
    private static string InlineImport(string source, string href, string css)
    {
        var rx = new System.Text.RegularExpressions.Regex(@"@import\s+(?:url\()?[""']?" + System.Text.RegularExpressions.Regex.Escape(href) + @"[""']?\)?[^;]*;", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return rx.Replace(source, css.Replace("</style>", string.Empty), 1);
    }

    /// <summary>A module script: its URL imports fetched and run in order (exports become globals), then its body.</summary>
    private System.Collections.IEnumerator RunModule(List<string> urls, string code)
    {
        foreach (var url in urls)
            yield return Fetch(url, js => _script?.Run(HtmlRenderer.StripModuleSyntax(js)));
        _script?.Run(code);
    }

    private static string InlineStylesheet(string source, string href, string css)
    {
        var rx = new System.Text.RegularExpressions.Regex("<link[^>]*href=[\"']?" + System.Text.RegularExpressions.Regex.Escape(href) + "[\"']?[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return rx.Replace(source, "<style>" + css.Replace("</style>", string.Empty) + "</style>", 1);
    }

    /// <summary>Element.animate(): a keyframe runner made by the script; the handle cancels it.</summary>
    private int StartAnimation(VisualElement ve, CssKeyframes frames, AnimationSpec spec)
    {
        // a looping opacity/transform animation runs in the scene (REDESIGN step 4), as for CSS ones
        if (_built != null && float.IsPositiveInfinity(spec.Iterations) && !spec.Paused && VectorEmitter.Compilable(frames, _built.CssOf(ve)))
        {
            _built.TimeAnimations[ve] = (spec, OffThread.Now);
            _scriptTimeAnimations[++_animationSeq] = ve;
            _dirty = true;
            Wake();
            return _animationSeq;
        }
        var runner = new KeyframeRunner(ve, frames, spec, OffThread.Now, m => ScriptedScreensHtmlPlugin.Log?.LogWarning(m), _built?.CssOf(ve), _built != null ? _built.Touch : null);
        _animations.Add(runner);
        _scriptAnimations[++_animationSeq] = runner;
        _awakeFrames = Mathf.Max(_awakeFrames, 2);
        return _animationSeq;
    }

    private void CancelAnimation(int handle)
    {
        if (_scriptTimeAnimations.TryGetValue(handle, out var timed))
        {
            _scriptTimeAnimations.Remove(handle);
            _built?.TimeAnimations.Remove(timed);
            _dirty = true;
            Wake();
            return;
        }
        if (!_scriptAnimations.TryGetValue(handle, out var runner)) return;
        _scriptAnimations.Remove(handle);
        _animations.Remove(runner);
        if (_built != null && _built.NodeOf.TryGetValue(runner.Element, out var n))
            _built.Reclass(runner.Element, n.Attr("class") ?? string.Empty);
        _dirty = true;
        Wake();
    }

    private readonly Dictionary<int, KeyframeRunner> _scriptAnimations = new();
    private readonly Dictionary<int, VisualElement> _scriptTimeAnimations = new();
    private int _animationSeq;

    /// <summary>A click on a page click region: the script gets a `click` event on the element, with the pointer's page coordinates. A submit button also fires `submit` on its form.</summary>
    internal void OnPageClick(string key)
    {
        Hold();
        lock (CascadeGate)
            OnPageClickLocked(key);
    }

    private void OnPageClickLocked(string key)
    {
        Hold();
        // A compiled page's handlers live in its chip, with the state the console is drawing. The
        // click arrives here exactly as before - the vector region names the element - it just has
        // to go somewhere that is still alive.
        if (_compiled != null)
        {
            if (!_compiled.Click(key, _pointerPage.x, _pointerPage.y))
                ScriptedScreensHtmlPlugin.Log?.LogWarning(
                    $"html: \"{PageKey}\" is compiled but could not take a click on \"{key}\"; the page is not interactive");
            return;
        }
        _script?.EmitClick(key, _pointerPage.x, _pointerPage.y);
        if (_built != null && _byId.TryGetValue(key, out var ve) && _built.NodeOf.TryGetValue(ve, out var node)
            && node.Tag == "button" && string.Equals(node.Attr("type") ?? "submit", "submit", StringComparison.OrdinalIgnoreCase))
        {
            for (var f = node.Parent; f != null; f = f.Parent)
                if (f.Tag == "form" && f.Attr("id") is { } formId) { _script?.EmitEvent(formId, "submit"); break; }
        }
    }

    // ---- pointer state: :hover / :active / :focus and mouse events, from the page's own boxes ----
    private Vector2 _pointerPage;
    private readonly HashSet<HtmlNode> _hovered = new();
    private readonly HashSet<HtmlNode> _active = new();
    private HtmlNode? _focused;
    private volatile string? _focusedId;
    private string? _hoverDeepest;

    private (bool inside, bool down, Vector2 fraction, string? focus)? _restore;

    /// <summary>A rebuilt surface takes the pointer state the old one had, applied once the page is laid out.</summary>
    internal void RestorePointer((bool inside, bool down, Vector2 fraction, string? focus) state) => _restore = state;

    private void ApplyRestoredPointer()
    {
        if (_restore is not { } r) return;
        _restore = null;
        if (r.focus != null) SetFocus(r.focus);
        if (r.inside) PointerMove(r.fraction);
        if (r.down) PointerDown(r.fraction);
    }

    private void SavePointer(bool inside, bool down, Vector2 fraction)
    {
        if (PageKey == null) return;
        HtmlElementPatch.PointerStates[PageKey] = (inside, down, fraction, _focusedId);
    }

    private Vector2 _lastFraction;

    /// <summary>The pointer at a fraction of the host rect: which page boxes are under it.</summary>
    internal void PointerMove(Vector2 fraction)
    {
        SavePointer(true, _pressed, fraction);
        // Where the pointer is, in page coordinates, BEFORE the early return. It used to be
        // computed only inside PointerMoveOnPage/PointerDownOnPage, which a compiled page never
        // reaches - so every `event.clientX` and `clientY` a compiled page read was 0. The
        // coordinates survive the whole chunk path correctly; they simply started at the origin.
        var here = LayoutSize();
        _pointerPage = new Vector2(fraction.x * here.x, fraction.y * here.y);
        // A compiled page has no boxes to hit-test and no interpreter to tell: its handlers are in
        // the chip and the click reaches them through the scene's own region, which names the
        // element outright. Running this would only feed the copy nobody is drawing.
        if (_compiled != null) return;
        Post(() => PointerMoveOnPage(fraction));
    }

    /// <summary>Whether a press is held, as the game thread last saw it (the page's own set lives on its thread).</summary>
    private volatile bool _pressed;

    private void PointerMoveOnPage(Vector2 fraction)
    {
        _lastFraction = fraction;
        var layout = LayoutSize();
        _pointerPage = new Vector2(fraction.x * layout.x, fraction.y * layout.y);
        var deepest = Deepest(_pointerPage);
        var deepestId = deepest != null && _built != null && _built.NodeOf.TryGetValue(deepest, out var dn) ? dn.Attr("id") : null;
        if (deepestId != _hoverDeepest)
        {
            if (_hoverDeepest != null) _script?.EmitPointer(_hoverDeepest, "mouseout", _pointerPage.x, _pointerPage.y);
            if (deepestId != null) _script?.EmitPointer(deepestId, "mouseover", _pointerPage.x, _pointerPage.y);
            _hoverDeepest = deepestId;
        }
        else if (deepestId != null)
            _script?.EmitPointer(deepestId, "mousemove", _pointerPage.x, _pointerPage.y);
        SetState(_hovered, "data-hover", Chain(deepest));
    }

    internal void PointerLeave()
    {
        _pressed = false;
        SavePointer(false, false, _lastFraction);
        // A compiled page has no boxes to hit-test and no interpreter to tell: its handlers are in
        // the chip and the click reaches them through the scene's own region, which names the
        // element outright. Running this would only feed the copy nobody is drawing.
        if (_compiled != null) return;
        Post(PointerLeaveOnPage);
    }

    private void PointerLeaveOnPage()
    {
        if (_hoverDeepest != null) _script?.EmitPointer(_hoverDeepest, "mouseout", _pointerPage.x, _pointerPage.y);
        _hoverDeepest = null;
        SetState(_hovered, "data-hover", null);
        SetState(_active, "data-active", null);
    }

    internal void PointerDown(Vector2 fraction)
    {
        _pressed = true;
        SavePointer(true, true, fraction);
        // Where the pointer is, in page coordinates, BEFORE the early return. It used to be
        // computed only inside PointerMoveOnPage/PointerDownOnPage, which a compiled page never
        // reaches - so every `event.clientX` and `clientY` a compiled page read was 0. The
        // coordinates survive the whole chunk path correctly; they simply started at the origin.
        var here = LayoutSize();
        _pointerPage = new Vector2(fraction.x * here.x, fraction.y * here.y);
        // A compiled page has no boxes to hit-test and no interpreter to tell: its handlers are in
        // the chip and the click reaches them through the scene's own region, which names the
        // element outright. Running this would only feed the copy nobody is drawing.
        if (_compiled != null) return;
        Post(() => PointerDownOnPage(fraction));
    }

    private void PointerDownOnPage(Vector2 fraction)
    {
        _lastFraction = fraction;
        var layout = LayoutSize();
        _pointerPage = new Vector2(fraction.x * layout.x, fraction.y * layout.y);
        var deepest = Deepest(_pointerPage);
        SetState(_active, "data-active", Chain(deepest));
        if (deepest != null && _built != null && _built.NodeOf.TryGetValue(deepest, out var dn) && dn.Attr("id") is { } id)
            _script?.EmitPointer(id, "mousedown", _pointerPage.x, _pointerPage.y);
    }

    internal void PointerUp()
    {
        _pressed = false;
        SavePointer(true, false, _lastFraction);
        // A compiled page has no boxes to hit-test and no interpreter to tell: its handlers are in
        // the chip and the click reaches them through the scene's own region, which names the
        // element outright. Running this would only feed the copy nobody is drawing.
        if (_compiled != null) return;
        Post(PointerUpOnPage);
    }

    private void PointerUpOnPage()
    {
        if (_hoverDeepest != null) _script?.EmitPointer(_hoverDeepest, "mouseup", _pointerPage.x, _pointerPage.y);
        SetState(_active, "data-active", null);
    }

    /// <summary>The last clicked element or control takes :focus; a click elsewhere moves it.</summary>
    /// <summary>The innermost area of the map containing the point, in the image's coordinates.</summary>
    private static HtmlNode? AreaAt(HtmlNode map, Vector2 p)
    {
        HtmlNode? hit = null;
        void Walk(HtmlNode n)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag == "area" && hit == null)
                {
                    var shape = (c.Attr("shape") ?? "rect").Trim().ToLowerInvariant();
                    var nums = new List<float>();
                    foreach (var t in (c.Attr("coords") ?? string.Empty).Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)) nums.Add(StyleApplier.Num(t));
                    var inside = shape switch
                    {
                        "circle" => nums.Count >= 3 && Vector2.Distance(p, new Vector2(nums[0], nums[1])) <= nums[2],
                        "poly" or "polygon" => nums.Count >= 6 && PointInPolygon(p, nums),
                        "default" => true,
                        _ => nums.Count >= 4 && p.x >= Mathf.Min(nums[0], nums[2]) && p.x <= Mathf.Max(nums[0], nums[2]) && p.y >= Mathf.Min(nums[1], nums[3]) && p.y <= Mathf.Max(nums[1], nums[3]),
                    };
                    if (inside) hit = c;
                }
                Walk(c);
            }
        }
        Walk(map);
        return hit;
    }

    private static bool PointInPolygon(Vector2 p, List<float> xy)
    {
        var inside = false;
        var n = xy.Count / 2;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var xi = xy[2 * i]; var yi = xy[2 * i + 1]; var xj = xy[2 * j]; var yj = xy[2 * j + 1];
            if ((yi > p.y) != (yj > p.y) && p.x < (xj - xi) * (p.y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    private string? _datalistFor;

    /// <summary>
    /// input list="id": the datalist's options drawn as a box under the focused field, each a
    /// click region that sets the value. Closed when the focus moves or an option is picked.
    /// </summary>
    private void ShowDatalist(HtmlNode input, VisualElement ive)
    {
        if (_built == null || _content == null || input.Attr("list") is not { } listId || !_byId.TryGetValue(listId, out var lve) || !_built.NodeOf.TryGetValue(lve, out var list)) return;
        CloseDatalist();
        var key = input.Attr("id") ?? string.Empty;
        var box = ive.worldBound; box.position -= _content.worldBound.position;
        var fs = ive.resolvedStyle.fontSize > 0f ? ive.resolvedStyle.fontSize : 13f;
        var sb = new StringBuilder();
        sb.Append("<div id=\"__datalist\" style=\"position:absolute;left:").Append(box.x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
          .Append("px;top:").Append((box.y + box.height).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
          .Append("px;width:").Append(box.width.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
          .Append("px;background:#1c2230;border:1px solid #567;font-size:").Append(fs.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append("px;color:#eee\">");
        var n = 0;
        foreach (var opt in list.Children)
        {
            if (opt.Tag != "option") continue;
            var value = opt.Attr("value") ?? string.Concat(opt.Children.ConvertAll(c => c.IsText ? c.Text : string.Empty)).Trim();
            var label = opt.Attr("label") ?? value;
            sb.Append("<div id=\"__dl").Append(++n).Append("\" data-click=\"1\" data-datalist=\"").Append(key).Append("\" data-value=\"").Append(value.Replace("\"", "&quot;")).Append("\" style=\"padding:2px 6px\">").Append(label.Replace("<", "&lt;")).Append("</div>");
        }
        sb.Append("</div>");
        if (n == 0) return;
        if (_built.NodeOf.TryGetValue(_content, out var body))
        {
            HtmlRenderer.AppendFragment(_content, body, sb.ToString(), _built);
            AttachLayouts(_built);
            _datalistFor = key;
            _dirty = true;
            Wake();
        }
    }

    private void CloseDatalist()
    {
        if (_datalistFor == null || _built == null) return;
        _datalistFor = null;
        if (_byId.TryGetValue("__datalist", out var dl)) { HtmlRenderer.Remove(dl, _built); _dirty = true; Wake(); }
    }

    private void SetFocus(string key)
    {
        if (_built == null || !_byId.TryGetValue(key, out var ve) || !_built.NodeOf.TryGetValue(ve, out var node)) return;
        if (_datalistFor != null && key != _datalistFor && node.Attr("data-datalist") == null) CloseDatalist();
        if (node.Tag == "input" && node.Attr("list") != null && _focused != node) ShowDatalist(node, ve);
        if (_focused == node) return;
        var changed = false;
        if (_focused != null) { _focused.Attributes.Remove("data-focus"); Recascade(_focused); changed = true; }
        _focused = node;
        _focusedId = node.Attr("id");
        node.Attributes["data-focus"] = string.Empty;
        Recascade(node);
        if (changed || CssParser.UsesPointerState) { _dirty = true; Wake(); }
    }

    /// <summary>The innermost element whose box contains the page point.</summary>
    private VisualElement? Deepest(Vector2 p)
    {
        if (_content == null) return null;
        var origin = _content.worldBound.position;
        VisualElement? best = null;
        var bestDepth = -1;
        foreach (var ve in _byId.Values)
        {
            if (ve.resolvedStyle.display == DisplayStyle.None) continue;
            var wb = ve.worldBound;
            if (float.IsNaN(wb.width)) continue;
            var r = new Rect(wb.x - origin.x, wb.y - origin.y, wb.width, wb.height);
            if (!r.Contains(p)) continue;
            var depth = 0;
            for (var e = ve; e != null && e != _content; e = e.parent) depth++;
            if (depth > bestDepth) { bestDepth = depth; best = ve; }
        }
        return best;
    }

    /// <summary>The element and its ancestors, as nodes: a pointer over a child is over every ancestor too, as in CSS.</summary>
    private HashSet<HtmlNode>? Chain(VisualElement? ve)
    {
        if (ve == null || _built == null) return null;
        var set = new HashSet<HtmlNode>();
        for (var e = ve; e != null; e = e.parent)
            if (_built.NodeOf.TryGetValue(e, out var n)) set.Add(n);
        return set;
    }

    /// <summary>Moves a state attribute from the old set to the new one, re-cascading what changed, and emits if any rule cares.</summary>
    private void SetState(HashSet<HtmlNode> current, string attr, HashSet<HtmlNode>? next)
    {
        if (!CssParser.UsesPointerState) { current.Clear(); return; }
        var changed = false;
        foreach (var n in new List<HtmlNode>(current))
        {
            if (next != null && next.Contains(n)) continue;
            n.Attributes.Remove(attr);
            current.Remove(n);
            Recascade(n);
            changed = true;
        }
        if (next != null)
            foreach (var n in next)
            {
                if (current.Contains(n)) continue;
                n.Attributes[attr] = string.Empty;
                current.Add(n);
                Recascade(n);
                changed = true;
            }
        if (changed) { _dirty = true; Wake(); }
    }

    private void Recascade(HtmlNode node)
    {
        if (_built == null || node.Attr("id") is not { } id || !_byId.TryGetValue(id, out var ve)) return;
        _built.Reclass(ve, node.Attr("class") ?? string.Empty);
    }

    /// <summary>
    /// The script added a click listener (or set onclick) on an element: it becomes a click
    /// region on the next emit, as a button is. Marked on the node so the emitter sees it.
    /// </summary>
    private void WantClicks(string key)
    {
        if (_built == null || !_byId.TryGetValue(key, out var ve) || !_built.NodeOf.TryGetValue(ve, out var node))
            return;
        if (node.Attr("data-click") != null || node.Tag == "button" || node.Attr("onclick") != null)
            return;
        node.Attributes["data-click"] = "1";
        _dirty = true;
        Wake();
    }

    /// <summary>Scroll offsets a script asked for, by scroll box id, with a version the vector mod applies once (so/sov, vector requirement 7).</summary>
    internal readonly Dictionary<string, (float offset, int version)> ScrollSet = new(StringComparer.Ordinal);
    private int _scrollVersion;

    /// <summary>
    /// A scroll container moved on the client (wheel, drag, or a jump the script asked for):
    /// the script sees the real scrollTop/scrollHeight and gets a `scroll` event, as in a browser.
    /// </summary>
    internal void OnScrollReport(string key, float offset, float max, float view)
    {
        Hold();
        lock (CascadeGate)
            OnScrollReportLocked(key, offset, max, view);
    }

    private void OnScrollReportLocked(string key, float offset, float max, float view)
    {
        Hold();
        if (_script == null) return;
        var changed = !_script.ScrollState.TryGetValue(key, out var prev) || Mathf.Abs(prev[0] - offset) > 0.01f || Mathf.Abs(prev[1] - (max + view)) > 0.01f;
        _script.ScrollState[key] = new[] { offset, max + view, view };
        if (changed && prev != null) _script.EmitEvent(key, "scroll");
    }

    /// <summary>Grid and post-layout passes for every element that has none yet: after the build, and after each fragment a script appends.</summary>
    private void AttachLayouts(HtmlRenderer.Result built)
    {
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
        AttachAnimations(built);
    }

    /// <summary>A keyframe runner for every animated element that has none yet (the build, then each fragment a script appends).</summary>
    private void AttachAnimations(HtmlRenderer.Result built)
    {
        foreach (var (element, spec) in built.Animations)
        {
            if (!built.AnimationAttached.Add(element)) continue;
            // animation-timeline: scroll()/view(): the emitter writes the frames as expressions over the scroll offset; no clock runs it
            if (built.CssOf(element).TryGetValue("animation-timeline", out var timeline) && timeline.Trim() != "auto") continue;
            if (!built.Keyframes.TryGetValue(spec.Name, out var frames)) continue;
            // a looping animation of opacity and transform only: the scene runs it (REDESIGN step 4), no
            // runner, no redraw at every keyframe
            if (float.IsPositiveInfinity(spec.Iterations) && !spec.Paused && VectorEmitter.Compilable(frames, built.CssOf(element)))
            {
                built.TimeAnimations[element] = (spec, OffThread.Now);
                continue;
            }
            _animations.Add(new KeyframeRunner(element, frames, spec, OffThread.Now, m => ScriptedScreensHtmlPlugin.Log?.LogWarning(m), built.CssOf(element), built.Touch));
        }
    }

    private void SetScroll(string key, float offset)
    {
        ScrollSet[key] = (Mathf.Max(0f, offset), ++_scrollVersion);
        _dirty = true;
        Wake();
    }

    /// <summary>The page script set a control's value or checked state.</summary>
    private void SetInputValue(string key, string value)
    {
        if (_built != null && _byId.TryGetValue(key, out var ve) && _built.NodeOf.TryGetValue(ve, out var node) && node.Attr("data-control") is { } control)
        {
            if (control is "progress" or "meter") node.Attributes["value"] = value;
            else SetChecked(node, value == "true");
            _dirty = true;
            Wake();
            return;
        }
        if (!_externalNodes.TryGetValue(key, out var field))
            return;
        _inputValues[key] = value;
        if (field.Tag is "input" or "textarea") { field.Attributes["value"] = value; Recascade(field); }
        _externalState.Remove(ElementId + "/" + key);
        _dirty = true;
        Wake();
    }

    /// <summary>The live page for a host and page element id, for routing control events.</summary>
    internal static HtmlSurface? Find(object? board, object? cartridge, object? visor, string surface, string pageId)
    {
        for (var i = Surfaces.Count - 1; i >= 0; i--)
        {
            // newest first: a rebuilt surface is registered after the one it replaces
            var s = Surfaces[i];
            if (s == null || s.State == null || !s.IsCurrent) continue;
            if (!ReferenceEquals(s.Board, board) || !ReferenceEquals(s.Cartridge, cartridge) || !ReferenceEquals(s.Visor, visor)) continue;
            if (s.Surface == surface && s.ElementId == pageId) return s;
        }
        return null;
    }

    /// <summary>
    /// The label to write text into. A Label is itself; an element with no children gets a
    /// Label child on the first write, because an empty div filled from script is ordinary
    /// HTML (the mockup does exactly that for every value); anything else is refused.
    /// </summary>
    internal static Label? TextTargetFor(VisualElement ve, string id) => TextTarget(ve, id);

    private static string ToJson(List<KeyValuePair<string, SS.UiValue>> entries)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        var first = true;
        foreach (var e in entries)
        {
            if (!first) sb.Append(',');
            first = false;
            JsonString(sb, e.Key);
            sb.Append(':');
            JsonValue(sb, e.Value);
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static void JsonValue(System.Text.StringBuilder sb, SS.UiValue v)
    {
        switch (v.Type)
        {
            case SS.UiValueType.Number: sb.Append(v.Number.ToString("R", CultureInfo.InvariantCulture)); break;
            case SS.UiValueType.Bool: sb.Append(v.Bool ? "true" : "false"); break;
            case SS.UiValueType.String: JsonString(sb, v.String ?? string.Empty); break;
            case SS.UiValueType.Array when v.Array != null:
            {
                sb.Append('[');
                for (var i = 0; i < v.Array.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    JsonValue(sb, v.Array[i]);
                }
                sb.Append(']');
                break;
            }
            case SS.UiValueType.Map when v.Map != null:
            {
                sb.Append('{');
                var first = true;
                foreach (var p in v.Map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    JsonString(sb, p.Key ?? string.Empty);
                    sb.Append(':');
                    JsonValue(sb, p.Value);
                }
                sb.Append('}');
                break;
            }
            default: sb.Append("null"); break;
        }
    }

    private static void JsonString(System.Text.StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }

    private static Label? TextTarget(VisualElement ve, string id)
    {
        if (ve is Label l)
            return l;
        if (ve.childCount == 1 && ve[0] is Label existing && existing.name == "#text")
            return existing;
        if (ve.childCount == 0)
        {
            var created = new Label { name = "#text" };
            created.style.flexGrow = 1;
            ve.Add(created);
            return created;
        }
        ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: \"{id}\" is not a text element");
        return null;
    }

    /// <summary>
    /// Patch elements by their HTML id. A string or number sets the text of a label; a
    /// map applies each entry as a CSS declaration, so { height = "62%" } moves a bar and,
    /// because the element persists, a CSS transition on it animates the move.
    /// </summary>
    internal void ApplyData(SS.UiValue data)
    {
        if (data.Type != SS.UiValueType.Map || data.Map == null)
            return;
        // Reused: every tick of a compiled data page passes through here, and the payload is read
        // and done with inside the call. Only the page thread, before the compile, keeps one, and
        // ApplyPairs copies it for that.
        _pairScratch.Clear();
        foreach (var e in data.Map)
            _pairScratch.Add(new KeyValuePair<string, SS.UiValue>(e.Key, e.Value));
        ApplyPairs(_pairScratch);
    }

    private readonly List<KeyValuePair<string, SS.UiValue>> _pairScratch = new();

    internal void ApplyData(Dictionary<string, SS.UiValue> merged)
    {
        ApplyPairs(new List<KeyValuePair<string, SS.UiValue>>(merged));
    }

    private bool _saidNoData;

    private void ApplyPairs(List<KeyValuePair<string, SS.UiValue>> entries)
    {
        // Lua data lands outside Update; its cost counts toward the page's game-thread time all the same
        var d0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // A page handed to its chip reads no data - its script had no handler, or it would not have
            // been handed over - so a payload has nowhere to go. Said once, and counted.
            if (_handedOver)
            {
                CompiledRun.WorkAfterCompile++;
                if (!_saidNoData) ScriptedScreensHtmlPlugin.Log?.LogWarning($"html \"{ElementId}\": this page is handed to its chip and reads no data; the payload is not shown");
                _saidNoData = true;
                return;
            }
            // A data page whose slot table is compiled: the values go to the scene from here, on the
            // game thread the renderer expects, and the page - released by then - is not touched.
            // Each key is placed or dropped on its own (DataSlots says which, once); nothing here
            // allocates, and only the names the scene reads as $name are forwarded as data.
            if (_script == null && _dataSlots is { } fast)
            {
                // The table is set inside the page thread's frame that compiled it, and that frame is
                // still writing _sentValues: wait it out. Free every tick after, the page being idle.
                Hold();
                if (fast.ReadsAny) ForwardData(entries, fast);
                SendCompiled(fast.Apply(entries));
                _dataFastTicks++;
                CompiledRun.WorkAfterCompile++;
                return;
            }
            // the vector scene's copy goes now (the vector mod is the game thread's); the page's on its thread
            ForwardData(entries, null);
            // The data compile threw: the page keeps what it showed. It is not laid out again for a
            // value - that was a layout, translate and emit per tick, for ever.
            if (_script == null && _dataRefused != null) { _dataDropped++; return; }
            // A compiled page's values come from its own Lua, and its DOM has been let go. Binding
            // into a rebuilt copy would cost a rebuild per tick and draw nothing, since the copy is
            // not what the console shows.
            // A compiled page's script is in its chip: the payload goes there, as its `data` event.
            if (_compiled != null) { _compiled.Data(entries); return; }
            if (_released) return;
            var kept = new List<KeyValuePair<string, SS.UiValue>>(entries);
            Post(() => BindData(kept));
        }
        finally { _allUpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - d0; }
    }

    private void BindData(List<KeyValuePair<string, SS.UiValue>> entries)
    {
        // Ids bind first, always: a key naming an element is the simplest contract a page
        // has. A script's data handler gets the same payload as an event afterwards; keys
        // it consumes that match no id are not warned about when a script is present.
        BindById(entries);
        if (_script != null)
        {
            _script.EmitData(ToJson(entries));
            Wake(DataAwakeFrames);
        }
    }

    /// <summary>
    /// The same payload, flattened, to the vector scene as $names: numbers, strings and
    /// number arrays as they are, nested tables joined with "_" (`co2 = { gasFill = .. }`
    /// is `$co2_gasFill`). The vector mod eases scalars between ticks, so an SVG
    /// expression over $data moves smoothly at any tick rate with nothing re-emitted.
    /// </summary>
    /// <summary>Everything forwarded so far, merged: the scene may not exist yet when data arrives.</summary>
    private readonly Dictionary<string, SS.UiValue> _forwarded = new(StringComparer.Ordinal);
    private bool _sceneLive;

    /// <param name="only">A compiled data page's table: then only the names its scene reads as $name go.</param>
    private void ForwardData(List<KeyValuePair<string, SS.UiValue>> entries, DataSlots? only)
    {
        _flatScratch.Clear();
        foreach (var e in entries)
            Flatten(e.Key, e.Value, only);
        if (_flatScratch.Count == 0)
            return;
        foreach (var p in _flatScratch)
            _forwarded[p.Key] = p.Value;
        if (_sceneLive)
            SendData(_flatScratch);
    }

    private readonly List<SS.UiProp> _flatScratch = new(32);
    /// <summary>`co2` + `gasFill` as `co2_gasFill`, made once: a tick builds no name.</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _flatNames = new(StringComparer.Ordinal);

    /// <summary>The shorthand or either longhand a page is as likely to write (Tailwind emits the longhands).</summary>
    private bool HasTransition(VisualElement ve)
    {
        var css = _built!.CssOf(ve);
        return css.ContainsKey("transition") || css.ContainsKey("transition-property") || css.ContainsKey("transition-duration");
    }

    private void SendData(List<SS.UiProp> props)
    {
        if (string.IsNullOrEmpty(DataElementId) || State is not SS.BoardState state || !IsCurrent)
            return;
        var map = new SS.UiValue { Type = SS.UiValueType.Map, Map = Exact(props, _dataArrays) };
        // snap, because a browser snaps. A value assignment moves a box at once unless a CSS
        // transition says otherwise, and a transition is compiled INTO the scene as an expression by
        // the emitter - so the renderer easing on top of that is a second animation nobody asked for.
        //
        // It also costs far more than it looks. An eased payload opens a blend window as long as the
        // gap between payloads, and the scene counts as ANIMATED for that whole window - so a console
        // fed twice a second never stops being animated and rebuilds its mesh at up to 60 Hz for
        // ever. Snapped values open no window: the scene goes static between payloads and a tick
        // costs exactly one rebuild.
        VectorBridge.Data(Board, Cartridge, Visor, state, Surface, DataElementId, SceneId, map, null, snap: HtmlConfig.SnapData);
    }

    private void Flatten(string key, SS.UiValue v, DataSlots? only)
    {
        switch (v.Type)
        {
            case SS.UiValueType.Number:
            case SS.UiValueType.String:
            case SS.UiValueType.Array:
                if (only == null || only.Reads(key)) _flatScratch.Add(new SS.UiProp { Key = key, Value = v });
                break;
            case SS.UiValueType.Bool:
                if (only == null || only.Reads(key)) _flatScratch.Add(new SS.UiProp { Key = key, Value = SS.UiValue.FromNumber(v.Bool ? 1f : 0f) });
                break;
            case SS.UiValueType.Map when v.Map != null:
                if (!_flatNames.TryGetValue(key, out var names)) _flatNames[key] = names = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var e in v.Map)
                {
                    var field = e.Key ?? string.Empty;
                    if (!names.TryGetValue(field, out var name)) names[field] = name = key + "_" + field;
                    Flatten(name, e.Value, only);
                }
                break;
        }
    }

    private void BindById(List<KeyValuePair<string, SS.UiValue>> entries)
    {
        // A page with no script compiles from its first payload: every key goes into the DOM below,
        // and the emit that draws them compiles the table against what it drew (CompileData) - no
        // emit spent on anything else, since every element with an id was named for the emitter
        // when the page was built. Payloads that arrive before that emit merge, later wins. From
        // then on ApplyPairs writes values straight to the scene and this is never reached again.
        if (_script == null && _dataSlots == null && _built != null)
        {
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Key)) continue;
                // Made on the first real key: the data element is created with `data = {}`, and a
                // compile over nothing would release the page before its bools were ever seen.
                (_dataPending ??= new Dictionary<string, SS.UiValue>(StringComparer.Ordinal))[entry.Key] = entry.Value;
                if (!_byId.TryGetValue(entry.Key, out var ve)) continue;
                // A bool hides its element through the element's opacity group, and an opacity
                // writes it: named now, so the scene the compile checks carries `<id>_o`.
                if (WritesGroup(entry.Value) && _built.NamedGroups.Add(entry.Key)) _dataDriven.Add(entry.Key);
                // Its transition, if any, is the renderer's to run (DataSlots._eased). The emitter's
                // tween would write an expression into the slot instead of the number the table
                // writes, and the proof would disagree - for the sake of a glide the renderer does anyway.
                // A custom property moves whatever reads it below, so their transitions go the same way.
                Mute(ve, WritesCustom(entry.Value));
            }
        }

        void Mute(VisualElement ve, bool subtree)
        {
            if (HasTransition(ve) && !_muted.Contains(ve)) { lock (Tweens.Shared) Tweens.Override[ve] = (0f, Tweens.Easing.Default); _muted.Add(ve); }
            if (subtree) for (var i = 0; i < ve.childCount; i++) Mute(ve[i], true);
        }

        _dirty = true;
        foreach (var entry in entries)
        {
            // An SVG shape: a string sets `points` (the live-graph case), a map sets attributes.
            if (!string.IsNullOrEmpty(entry.Key) && _shapes.TryGetValue(entry.Key, out var shape))
            {
                var sv = entry.Value;
                if (sv.Type == SS.UiValueType.String)
                    shape.Set("points", sv.String ?? string.Empty);
                else if (sv.Type == SS.UiValueType.Map && sv.Map != null)
                {
                    foreach (var a in sv.Map)
                        shape.Set(a.Key, a.Value.Type == SS.UiValueType.Number ? a.Value.Number.ToString("G", CultureInfo.InvariantCulture) : a.Value.String ?? string.Empty);
                }
                else if (sv.Type == SS.UiValueType.Array && sv.Array != null)
                {
                    // Array of numbers: y values spread evenly across the viewBox width.
                    // The vector emitter binds the shape to $key[i] instead of copying the
                    // numbers, so the scene text stays constant and the vector mod scrolls
                    // the array between ticks itself.
                    shape.Attributes["__data"] = entry.Key;
                    shape.Attributes["__n"] = sv.Array.Length.ToString(CultureInfo.InvariantCulture);
                    var sb = new System.Text.StringBuilder();
                    var n = sv.Array.Length;
                    var vb = shape.Owner != null ? shape.Owner.ViewBox : new Rect(0, 0, 100, 100);
                    for (var i = 0; i < n; i++)
                    {
                        var x = n > 1 ? vb.x + vb.width * i / (n - 1) : vb.x;
                        if (i > 0) sb.Append(' ');
                        sb.Append(x.ToString("G", CultureInfo.InvariantCulture)).Append(',').Append(sv.Array[i].Number.ToString("G", CultureInfo.InvariantCulture));
                    }
                    shape.Set("points", sb.ToString());
                }
                continue;
            }

            // A key naming no element: a page with a script may consume it in its handler, and a
            // page without one is told by its data compile, once, unless the scene reads it as $name.
            if (string.IsNullOrEmpty(entry.Key) || !_byId.TryGetValue(entry.Key, out var ve))
                continue;

            var v = entry.Value;
            switch (v.Type)
            {
                case SS.UiValueType.String:
                {
                    var l = TextTarget(ve, entry.Key);
                    if (l != null) l.text = v.String ?? string.Empty;
                    if (_script == null) KeepText(ve, l?.text);
                    break;
                }
                case SS.UiValueType.Number:
                {
                    var l = TextTarget(ve, entry.Key);
                    if (l != null) l.text = v.Number.ToString("G", CultureInfo.InvariantCulture);
                    if (_script == null) KeepText(ve, l?.text);
                    break;
                }
                case SS.UiValueType.Bool:
                    // A data page's bool is a toggle (both states captured by ToggleOf, written by
                    // DataSlots.Apply and Overlay) or a refusal of that key. The element is SHOWN
                    // whatever the value, so the scene the compile checks carries its lines: an
                    // alert hidden by its CSS until data shows it had no slots there, and its bool
                    // was refused although a browser draws it. The value is put back by the table.
                    if (_script == null) { ve.style.display = DisplayStyle.Flex; break; }
                    ve.style.display = v.Bool ? DisplayStyle.Flex : DisplayStyle.None;
                    break;
                case SS.UiValueType.Map when v.Map != null && _script == null:
                    // Kept on the node as an inline style, so a custom property's re-cascade - or a
                    // capture's, when the table compiles - does not put the stylesheet back over it.
                    if (DataSlots.Bind(_built!, ve, v.Map, m => ScriptedScreensHtmlPlugin.Log?.LogWarning(m)))
                        ReshowBools();
                    // `display` is the bool's two states: shown, as the bool's element is, below
                    if (KeepsShown(v)) ve.style.display = DisplayStyle.Flex;
                    break;
                case SS.UiValueType.Map when v.Map != null:
                    foreach (var decl in v.Map)
                    {
                        var text = decl.Value.Type == SS.UiValueType.Number
                            ? decl.Value.Number.ToString("G", CultureInfo.InvariantCulture)
                            : decl.Value.String ?? string.Empty;
                        StyleApplier.Apply(ve, new CssDeclaration(decl.Key, text), m => ScriptedScreensHtmlPlugin.Log?.LogWarning(m));
                    }
                    break;
            }
        }

        // ponytail: a fixed awake window. Transitions longer than ~1 s freeze until the
        // next data tick; derive it from the page's longest transition if that bites.
        Wake(DataAwakeFrames);
    }

    /// <summary>
    /// Whether a data value writes its element's groups: a bool or a display (hide/show), an opacity
    /// or visibility, or a transform - named up front, since a page translated once never gets the
    /// frame on which the emitter would have seen it move.
    /// </summary>
    private static bool WritesGroup(SS.UiValue v)
    {
        if (v.Type == SS.UiValueType.Bool) return true;
        if (v.Type != SS.UiValueType.Map || v.Map == null) return false;
        foreach (var d in v.Map) if (d.Key is "opacity" or "visibility" or "transform" or "display") return true;
        return false;
    }

    /// <summary>Whether a data value hides and shows its element, which is then kept shown until the table compiles.</summary>
    private static bool KeepsShown(SS.UiValue v)
    {
        if (v.Type == SS.UiValueType.Bool) return true;
        if (v.Type != SS.UiValueType.Map || v.Map == null) return false;
        foreach (var d in v.Map) if (d.Key == "display") return true;
        return false;
    }

    /// <summary>Whether a data value sets a custom property, which moves whatever reads it below the element.</summary>
    private static bool WritesCustom(SS.UiValue v)
    {
        if (v.Type != SS.UiValueType.Map || v.Map == null) return false;
        foreach (var d in v.Map) if (d.Key != null && d.Key.StartsWith("--", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// A no-script page's elements that data shows and hides, shown again: a re-cascade (a custom
    /// property's) puts their stylesheet `display` back, and the compile needs their lines drawn.
    /// </summary>
    private void ReshowBools()
    {
        if (_dataPending == null) return;
        foreach (var pair in _dataPending)
            if (KeepsShown(pair.Value) && _byId.TryGetValue(pair.Key, out var shown)) shown.style.display = DisplayStyle.Flex;
    }

    /// <summary>
    /// A data text kept on the element's node too, as a script's textContent is (ScriptHost): a
    /// re-cascade rebuilds a label's rich text from its node, and would put the markup's "--" back.
    /// </summary>
    private void KeepText(VisualElement ve, string? text)
    {
        if (text == null || _built == null || !_built.NodeOf.TryGetValue(ve, out var node) || node.IsText) return;
        node.Children.Clear();
        node.Children.Add(new HtmlNode { Text = text, Parent = node });
    }

    private void OnDestroy()
    {
        Surfaces.Remove(this);
        if (!string.IsNullOrEmpty(PageKey) && Current.TryGetValue(PageKey, out var live) && live == this)
            Current.Remove(PageKey);
        _script?.Dispose();
        _pageStop = true;
        _compileStop = true;
        _pageWake.Set();
    }
}

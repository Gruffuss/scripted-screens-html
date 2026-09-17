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
/// Probe: does the shipped UI Toolkit runtime render to a texture in this build?
/// Owns a PanelSettings, a RenderTexture, a UIDocument, and a RawImage showing the result.
/// </summary>
/// <remarks>
/// Resolution follows on-screen size. The element rect is in canvas units (436x400 on a
/// 460 console) but the console is drawn far larger on screen, so a texture at canvas size
/// is visibly blurred. The panel is rendered at <c>scale</c> times canvas size, where the
/// scale is the measured ratio of screen pixels to canvas units, quantised so camera drift
/// does not recreate the texture every frame.
/// </remarks>
internal sealed class HtmlSurface : MonoBehaviour
{
    private const float MaxScale = 16f;  // texture capped at MaxTextureSize; 4096 / 460 = 8.9x, 4096 / 768 = 5.3x
    private const int MaxTextureSize = 4096;
    private const int DataAwakeFrames = 60;


    /// <summary>Scale per page key, so a host rebuilt by ScriptedScreens comes back at the same sharpness.</summary>
    internal string PageKey = string.Empty;
    /// <summary>What the bridge needs to address the vector mod: the host identity and the page element id.</summary>
    internal object? Board;
    internal object? Cartridge;
    internal object? Visor;
    internal string ElementId = string.Empty;
    /// <summary>The data element's id: the vector mod needs a host of its own for a data payload.</summary>
    internal string DataElementId = string.Empty;
    private bool _dirty;
    private int _dScript, _dAnim, _dTween, _dDom, _dOther;   // dirty causes since the last diagnostics line
    private int _seenLayoutWrites;
    private int _settleFrames;
    private string _lastScene = string.Empty;
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


    /// <summary>UIElementsRuntimeUtility.UpdateRuntimePanels: sizes a panel from its settings. Per-frame normally.</summary>
    private static readonly MethodInfo? UpdateRuntimePanels = RuntimeUtilityMethod("UpdateRuntimePanels");

    private static MethodInfo? RuntimeUtilityMethod(string name)
    {
        return typeof(PanelSettings).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility")
            ?.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    }

    private PanelSettings? _panel;
    private RenderTexture? _texture;
    private RenderTexture? _pending;
    private UIDocument? _document;
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
    private float _scale = 1f;

    internal void SetSource(string source)
    {
        Hold();
        _source = source;
        if (!Surfaces.Contains(this))
            Surfaces.Add(this);
        EnsurePanel();
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
            if (_document != null && _document.gameObject.activeSelf) _awakeCount++;
        }
    }

    private void UpdateInner()
    {
        if (Time.frameCount != _frameSeen)
        {
            _frameSeen = Time.frameCount;
            var dt = Time.unscaledDeltaTime * 1000f;
            if (dt > 25f) _slowFrames++;
            if (dt > _worstFrame) _worstFrame = dt;
        }
        // The script runs whether or not the panel is awake: a timer on a static page must fire.
        // Its writes wake the panel so the change is laid out and emitted.
        if (_script != null && _panel == null && _job == null)
        {
            if (_scriptPending) { _scriptPending = false; _script.Run(_built?.Script ?? string.Empty); }
            if (_script.Frame(Time.time, _byId)) { _dirty = true; _dScript++; Wake(); }
        }
        if (_panel == null)
            return;
        ReportIfDue();
        if (_job != null)
        {
            if (!_job.IsCompleted)
                return; // the worker reads this page: nothing changes it until the job ends
            FinishJob();
        }

        // A texture created last frame has been painted into (offscreen panels repaint
        // after LateUpdate), so it can be shown now. Showing it the frame it was created
        // put an empty texture on screen for one frame: the flicker.
        if (_pending != null)
        {
            var old = _texture;
            _texture = _pending;
            _pending = null;
            if (old != null)
            {
                old.Release();
                Destroy(old);
            }
        }

        if (_script != null)
        {
            if (_scriptPending)
            {
                // Queue the page script one frame after build, so layout has happened and
                // clientWidth is real. The worker runs it before any data queued after it.
                _scriptPending = false;
                _script.Run(_built?.Script ?? string.Empty);
            }
            // Hand the worker this frame's time and sizes; apply whatever it finished.
            // A pending timer does not keep the panel awake: an awake panel is re-rendered off-screen
            // every frame (a texture nothing shows in vector mode). Writes wake it when they land.
            if (_script.Frame(Time.time, _byId)) { _dirty = true; _dScript++; Wake(); }
        }

        // Keyframe animations step at keyframe boundaries; UI Toolkit interpolates between.
        // While any is running the panel must stay awake to repaint. Time.time, so it
        // pauses with the game like the vector layer.
        var animating = false;
        // a runner whose element a script removed (an innerHTML page rebuilds its lamps every tick) would keep writing
        _animations.RemoveAll(a => a.Element.panel == null);
        foreach (var a in _animations)
        {
            a.Update(Time.time);
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
            animating |= !a.Finished;
        }
        // The last tween ending re-emits the scene once with plain numbers: static again.
        if (_tweens.Expire(Time.time)) { _dirty = true; _dTween++; }
        foreach (var svg in _svgs)
        {
            if (svg.Blending)
            {
                svg.MarkDirtyRepaint();
                animating = true;
            }
        }
        if (animating)
            _awakeFrames = Mathf.Max(_awakeFrames, 2);

        if (_dirty)
        {
            // The layout passes (grid, line boxes, baselines, mixed calc) write styles from geometry
            // events at the end of a frame and settle over a few frames. Emitting each of those frames
            // drew every intermediate layout (twenty emits for one innerHTML); a browser paints a
            // settled layout once. Hold while a frame's passes wrote, bounded so a pass that never
            // settles still shows.
            var writes = PostLayout.LayoutWrites;
            if (writes != _seenLayoutWrites && _settleFrames < 8)
            {
                _seenLayoutWrites = writes;
                _settleFrames++;
                _awakeFrames = Mathf.Max(_awakeFrames, 2);
            }
            else
            {
                _seenLayoutWrites = writes;
                _settleFrames = 0;
                _dirty = false;
                var e0 = System.Diagnostics.Stopwatch.GetTimestamp();
                EmitToVector();
                _allEmitTicks += System.Diagnostics.Stopwatch.GetTimestamp() - e0;
            }
        }

        if (_awakeFrames > 0 && --_awakeFrames == 0 && _document != null)
            _document.gameObject.SetActive(false);

    }

    /// <summary>The scale that fills the texture cap for this page's layout size.</summary>
    private float FixedScale()
    {
        var layout = LayoutSize();
        var largest = Mathf.Max(layout.x, layout.y);
        return Mathf.Clamp(MaxTextureSize / Mathf.Max(64f, largest), 1f, MaxScale);
    }

    private void EnsurePanel()
    {
        if (_panel != null)
            return;

        // FIXED MAXIMUM, by decision (2026-09-10): the largest texture the cap allows,
        // for every console, made once and never re-made. Measuring on-screen size and
        // re-rendering in steps was tried twice and both times the step was a visible
        // change in clarity while walking; the user chose stability over 1:1.
        _scale = 1f;
        var (w, h) = TextureSize(_scale);

        _texture = CreateTexture(w, h);

        _panel = ScriptableObject.CreateInstance<PanelSettings>();
        _panel.name = "HtmlSurface Panel";
        _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
        _panel.scale = _scale;
        _panel.clearColor = true;
        _panel.colorClearValue = new Color(0, 0, 0, 0);
        _panel.targetTexture = _texture;

        // Deliberately NOT parented under the console: the screen capture clones the
        // surface tree, and a cloned UIDocument would attach a second root to this panel
        // mid-frame and then be destroyed. Lifetime is tied to us via OnDestroy instead.
        var docGo = new GameObject("HtmlDocument:" + gameObject.GetInstanceID().ToString(CultureInfo.InvariantCulture));
        _document = docGo.AddComponent<UIDocument>();
        _document.panelSettings = _panel;

        // No RawImage: the panel exists for LAYOUT only. What is drawn is the vector scene
        // the emitter derives from that layout; the vector mod renders it as geometry.

        ReportShaders();
        if (HtmlConfig.Diagnostics) ScriptedScreensHtmlPlugin.Log?.LogInfo(
            $"html surface created: {w}x{h} at scale {_scale}, layout {LayoutSize()} for rect {((RectTransform)transform).rect.size}, theme={(_panel.themeStyleSheet == null ? "none" : _panel.themeStyleSheet.name)}");
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

    /// <summary>
    /// 24 depth bits: UI Toolkit clips rounded corners (overflow: hidden + border-radius)
    /// through the stencil buffer, and with 0 the clipped element painted solid white.
    /// Mipmaps + trilinear so a console seen from a distance minifies smoothly instead of
    /// shimmering; the panel repaints into level 0 and the chain regenerates automatically.
    /// </summary>
    private static RenderTexture CreateTexture(int w, int h)
    {
        // Mipmaps + anisotropic: a fixed 4096 texture is minified at any normal distance,
        // and without mips that aliases and shimmers. Negative bias keeps it on the sharp side.
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
        {
            name = "HtmlSurface RT",
            useMipMap = true,
            autoGenerateMips = true,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 16,
            mipMapBias = -0.5f,
        };
        rt.Create();
        return rt;
    }

    private void Resize(float scale)
    {
        if (_panel == null || _pending != null)
            return;
        _scale = scale;
        var (w, h) = TextureSize(scale);
        var fresh = CreateTexture(w, h);
        _panel.targetTexture = fresh;
        _panel.scale = scale;
        _pending = fresh;
        Wake();
        if (HtmlConfig.Diagnostics)
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: resized to {w}x{h} at scale {scale}");
    }

    private (int w, int h) TextureSize(float scale)
    {
        var size = LayoutSize();
        var w = Mathf.Clamp(Mathf.RoundToInt(size.x * scale), 64, MaxTextureSize);
        var h = Mathf.Clamp(Mathf.RoundToInt(size.y * scale), 64, MaxTextureSize);
        return (w, h);
    }

    private void ReportShaders()
    {
        foreach (var f in new[] { "m_AtlasBlitShader", "m_RuntimeShader", "m_RuntimeWorldShader" })
        {
            var field = typeof(PanelSettings).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance);
            var shader = field?.GetValue(_panel) as Shader;
            if (HtmlConfig.Diagnostics)
                ScriptedScreensHtmlPlugin.Log?.LogInfo($"  {f}: {(shader == null ? "NULL" : shader.name)}");
        }
    }

    private static Font? FindFont()
    {
        foreach (var n in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
        {
            try
            {
                var f = Resources.GetBuiltinResource<Font>(n);
                if (f != null)
                    return f;
            }
            catch (System.Exception)
            {
                // Not present in this Unity version; try the next name.
            }
        }

        var any = Resources.FindObjectsOfTypeAll<Font>();
        return any.Length > 0 ? any[0] : null;
    }

    /// <summary>
    /// Enable the document so the panel exists and repaints, attach our content to its
    /// (fresh) root, and schedule sleep. Disabling the document nulls its root, so the
    /// content tree is kept on our side and re-attached each wake.
    /// </summary>
    private void Wake(int frames = 3)
    {
        if (_document == null || _content == null)
            return;

        if (!_document.gameObject.activeSelf)
            _document.gameObject.SetActive(true);

        var root = _document.rootVisualElement;
        if (root == null)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning("html: rootVisualElement is null on wake");
            return;
        }

        // The document root is a plain child of the panel's tree and sizes to its content
        // unless told to fill. Without this the root measured 436x53 on a 436x400 panel
        // and the scene was a strip along the top.
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;

        if (_content.parent != root)
        {
            root.Clear();
            root.Add(_content);
        }

        // At least two repaints: one into the texture, one more so a pending swap lands on content.
        _awakeFrames = Mathf.Max(_awakeFrames, frames);
    }

    private void Build()
    {
        if (_document == null)
            return;
        try
        {
            BuildInner();
        }
        catch (System.Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogError($"html: build failed: {ex}");
        }
    }

    private void BuildInner()
    {
        {
            var size = LayoutSize();
            HtmlRenderer.SurfaceAspect = size.x > 0f ? size.y / size.x : 1f;
        }
        var built = HtmlRenderer.Build(_source, FindFont());
        foreach (var w in built.Warnings)
            ScriptedScreensHtmlPlugin.Log?.LogWarning(w);

        if (!Mathf.Approximately(built.ViewportWidth, _designWidth))
        {
            // A new design width changes the layout size; rebuild the texture for it.
            _designWidth = built.ViewportWidth;
            Resize(1f);
        }

        _content = built.Root;
        _byId = built.ById;
        _shapes = built.Shapes;
        _built = built;
        AttachLayouts(built);

        _script?.Dispose();
        _script = null;
        if (HtmlConfig.Diagnostics) ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: page built, script {built.Script.Length} chars, {built.ById.Count} elements");
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
        // A capture builds and copies the page in one call. The script gets a few frames
        // first (its load work, a short timer, an animation frame), each waited for, so the
        // capture shows what the script drew rather than the bare markup.
        if (_script != null)
        {
            for (var k = 0; k < 6; k++)
                _script.RunSynchronously(Time.time + k * 0.1f, _byId, 300);
            _script.Pump(); // what the last frame queued lands before the capture's emit
        }
        _dirty = false;
        EmitToVector(inline: true);
        if (_restore != null)
        {
            // the layout exists now: put the old surface's hover, press and focus back and emit once more
            ApplyRestoredPointer();
            if (_dirty) { _dirty = false; EmitToVector(inline: true); }
        }
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

    private static void ReportIfDue()
    {
        if (!HtmlConfig.Diagnostics)
            return;
        var now = Clock.Elapsed.TotalSeconds;
        if (now < _nextReport)
            return;
        _nextReport = now + ReportIntervalSeconds;
        var frames = Mathf.Max(1, Time.frameCount - _framesAtReport);
        _framesAtReport = Time.frameCount;
        double PerFrame(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / frames;
        ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: frames over 25 ms: {_slowFrames}, slowest {_worstFrame:0} ms, heap {System.GC.GetTotalMemory(false) / 1048576f:0} MB, gc {System.GC.CollectionCount(0)}; "
            + $"game thread per frame: ui toolkit update {PerFrame(UiToolkitPatch.UpdateTicks):0.00} ms + paint {PerFrame(UiToolkitPatch.RepaintTicks):0.00} ms, "
            + $"pages {PerFrame(_allUpdateTicks):0.00} ms (emit {PerFrame(_allEmitTicks):0.00})");
        _slowFrames = 0; _worstFrame = 0f;
        UiToolkitPatch.UpdateTicks = UiToolkitPatch.RepaintTicks = 0;
        _allUpdateTicks = _allEmitTicks = 0;
        foreach (var page in Surfaces)
        {
            if (page == null || page._built == null)
                continue;
            var emits = page._emits - page._emitsAtReport;
            page._emitsAtReport = page._emits;
            ScriptedScreensHtmlPlugin.Log?.LogInfo(
                $"html \"{page.ElementId}\": {emits / ReportIntervalSeconds:0.0} emits/s, last {page._lastLayoutMs + page._lastTranslateMs:0.0} ms "
                + $"(layout {page._lastLayoutMs:0.00} + copy {page._lastCopyMs:0.00} on the game thread, translate {page._lastTranslateMs:0.0} on a worker, {page._translateMsTotal / ReportIntervalSeconds:0.0} ms/s; waited {page._heldMs:0.00} ms), {page._lastNodes} nodes / {page._lastChars / 1024f:0.0} KB, "
                + $"{page._tweens.Count} tweens, main {page._updateTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / ReportIntervalSeconds / Mathf.Max(1f, Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 60f):0.00} ms/frame, awake {page._awakeCount} frames, sent: {page._structureSends} structures {page._patchSends} patches ({page._patchSlots} values), {page._morphs} in-place, script {(page._script != null ? page._script.LastFrameMs : 0f):0.0} ms/frame, {page._externals.Count} externals, {page._animations.Count} runners, kept: {(page._built != null ? page._built.NodeOf.Count : 0)} nodes {(page._built != null ? page._built.CssCount : 0)} records made {page._tweens.Shown} snaps {(page._script != null ? page._script.CacheSizes : 0)} cached, heap {System.GC.GetTotalMemory(false) / 1048576f:0} MB, gc {System.GC.CollectionCount(0)}, dirty: script {page._dScript} anim {page._dAnim} tween {page._dTween} dom {page._dDom} other {page._dOther}");
            page._dScript = page._dAnim = page._dTween = page._dDom = page._dOther = 0;
            page._structureSends = page._patchSends = page._patchSlots = page._morphs = 0;
            page._updateTicks = 0; page._awakeCount = 0; page._translateMsTotal = 0; page._heldMs = 0;
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

    private System.Threading.Tasks.Task<EmitResult>? _job;
    private readonly Dictionary<VisualElement, OffThread.Box> _boxes = new();
    private readonly List<VisualElement> _boxScratch = new();
    private double _lastCopyMs;

    /// <summary>Finishes a translation in flight before the page changes. Game thread.</summary>
    private void Hold()
    {
        if (_job == null)
            return;
        if (!_job.IsCompleted)
        {
            var w0 = Clock.Elapsed.TotalMilliseconds;
            try { _job.Wait(); } catch (AggregateException) { }
            _heldMs += Clock.Elapsed.TotalMilliseconds - w0;
        }
        FinishJob();
    }

    private double _heldMs;

    /// <summary>Lays the page out, copies it and starts its translation; <paramref name="inline"/> runs it on the game thread (a capture needs the scene inside the call).</summary>
    private void EmitToVector(bool inline = false)
    {
        Hold();
        if (_content == null || _document == null || _built == null)
            return;
        var t0 = Clock.Elapsed.TotalMilliseconds;
        Wake();
        UpdateRuntimePanels?.Invoke(null, null);
        if (_document.rootVisualElement == null)
            return;
        var t1 = Clock.Elapsed.TotalMilliseconds;
        OffThread.Capture(_content, _built, _boxes, _boxScratch);
        var layout = LayoutSize();
        var globals = OffThread.Globals.Take();
        var now = Time.time;
        var diagnostics = HtmlConfig.Diagnostics;
        var t2 = Clock.Elapsed.TotalMilliseconds;
        _lastLayoutMs = t1 - t0;
        _lastCopyMs = t2 - t1;
        if (inline)
        {
            var result = Translate(layout, now, globals, diagnostics, worker: false);
            _job = System.Threading.Tasks.Task.FromResult(result);
            FinishJob();
            return;
        }
        _job = System.Threading.Tasks.Task.Run(() => Translate(layout, now, globals, diagnostics, worker: true));
    }

    /// <summary>The job: translation, template split and the send decision. Reads only the copies; touches only this surface's own send state.</summary>
    private EmitResult Translate(Vector2 layout, float now, OffThread.Globals globals, bool diagnostics, bool worker)
    {
        var r = new EmitResult();
        var c0 = System.Diagnostics.Stopwatch.GetTimestamp();
        OffThread.Active = worker;
        OffThread.Boxes = _boxes;
        OffThread.Job = globals;
        OffThread.Stale = false;
        try
        {
            _tweens.Diff(_content!, _built!, now);
            if (_lastTemplate == null) _tweens.Epoch = now;
            var output = VectorEmitter.Emit(_built!, _content!, layout.x, layout.y, _tweens, now, ScrollSet);
            r.Output = output;
            var template = SceneSlots.Split(output.Scene, _slotScratch, _slotPrefix);
            if (template == _lastTemplate)
            {
                _lastScene = output.Scene;
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
            template = SceneSlots.Split(output.Scene, _slotScratch, _slotPrefix);
            _tweens.Epoch = now;
            _lastScene = output.Scene;
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
            OffThread.Active = false;
            OffThread.Boxes = null;
            r.TranslateMs = (System.Diagnostics.Stopwatch.GetTimestamp() - c0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }
    }

    private static SS.UiProp Prop(string key, SceneSlots.Value v) =>
        new() { Key = key, Value = v.IsNumber ? SS.UiValue.FromNumber(v.Number) : SS.UiValue.FromString(v.Text ?? string.Empty) };

    /// <summary>Hands a finished translation to the vector mod. Game thread.</summary>
    private void FinishJob()
    {
        var job = _job;
        if (job == null || !job.IsCompleted)
            return;
        _job = null;
        if (job.IsFaulted)
        {
            // a translation that failed leaves no trustworthy send state: the next one sends everything
            ScriptedScreensHtmlPlugin.Log?.LogError($"html \"{ElementId}\": translation failed: {job.Exception?.InnerException}");
            _lastTemplate = null;
            return;
        }
        var r = job.Result;
        _tweens.ApplyHides();
        if (OffThread.ResolveFonts()) { _dirty = true; _dOther++; }
        if (r.Stale || r.Output == null)
        {
            // the tree changed under the job: try again with a full send
            _lastTemplate = null;
            _dirty = true;
            _dOther++;
            return;
        }
        var output = r.Output;
        _lastTranslateMs = r.TranslateMs;
        _translateMsTotal += r.TranslateMs;
        _lastNodes = output.Nodes;
        _lastChars = output.Scene.Length;
        _emits++;
        foreach (var w in output.Warnings)
            ScriptedScreensHtmlPlugin.Log?.LogWarning(w);
        if (r.Why != null)
            ScriptedScreensHtmlPlugin.Log?.LogInfo(r.Why);
        ApplyExternals(output.Externals);
        if (State is not SS.BoardState state)
            return;
        if (r.Structure == null)
        {
            if (r.Patch == null)
                return;
            VectorBridge.Data(Board, Cartridge, Visor, state, Surface, ElementId, "html:" + ElementId,
                new SS.UiValue { Type = SS.UiValueType.Map, Map = r.Patch }, null, snap: true);
            _patchSends++;
            _patchSlots += r.Patch.Length;
            return;
        }
        _structureSends++;
        // the values first, so the structure never shows an unbound slot
        VectorBridge.Data(Board, Cartridge, Visor, state, Surface, ElementId, "html:" + ElementId,
            new SS.UiValue { Type = SS.UiValueType.Map, Map = r.Values ?? Array.Empty<SS.UiProp>() }, null, snap: true);
        VectorBridge.Structure(Board, Cartridge, Visor, state, Surface, ElementId, "html:" + ElementId, r.Structure);
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
                var all = new List<SS.UiProp>(_forwarded.Count);
                foreach (var kv in _forwarded)
                    all.Add(new SS.UiProp { Key = kv.Key, Value = kv.Value });
                SendData(all);
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
        if (_built != null && float.IsPositiveInfinity(spec.Iterations) && !spec.Paused && VectorEmitter.Compilable(frames))
        {
            _built.TimeAnimations[ve] = (spec, Time.time);
            _scriptTimeAnimations[++_animationSeq] = ve;
            _dirty = true;
            Wake();
            return _animationSeq;
        }
        var runner = new KeyframeRunner(ve, frames, spec, Time.time, m => ScriptedScreensHtmlPlugin.Log?.LogWarning(m), _built?.CssOf(ve));
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
        SetFocus(key);
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
        HtmlElementPatch.PointerStates[PageKey] = (inside, down, fraction, _focused?.Attr("id"));
    }

    private Vector2 _lastFraction;

    /// <summary>The pointer at a fraction of the host rect: which page boxes are under it.</summary>
    internal void PointerMove(Vector2 fraction)
    {
        Hold();
        _lastFraction = fraction;
        SavePointer(true, _active.Count > 0, fraction);
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
        Hold();
        SavePointer(false, false, _lastFraction);
        if (_hoverDeepest != null) _script?.EmitPointer(_hoverDeepest, "mouseout", _pointerPage.x, _pointerPage.y);
        _hoverDeepest = null;
        SetState(_hovered, "data-hover", null);
        SetState(_active, "data-active", null);
    }

    internal void PointerDown(Vector2 fraction)
    {
        Hold();
        _lastFraction = fraction;
        SavePointer(true, true, fraction);
        var layout = LayoutSize();
        _pointerPage = new Vector2(fraction.x * layout.x, fraction.y * layout.y);
        var deepest = Deepest(_pointerPage);
        SetState(_active, "data-active", Chain(deepest));
        if (deepest != null && _built != null && _built.NodeOf.TryGetValue(deepest, out var dn) && dn.Attr("id") is { } id)
            _script?.EmitPointer(id, "mousedown", _pointerPage.x, _pointerPage.y);
    }

    internal void PointerUp()
    {
        Hold();
        SavePointer(true, false, _lastFraction);
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
            if (float.IsPositiveInfinity(spec.Iterations) && !spec.Paused && VectorEmitter.Compilable(frames))
            {
                built.TimeAnimations[element] = (spec, Time.time);
                continue;
            }
            _animations.Add(new KeyframeRunner(element, frames, spec, Time.time, m => ScriptedScreensHtmlPlugin.Log?.LogWarning(m), built.CssOf(element)));
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
        foreach (var s in Surfaces)
        {
            if (s == null || s.State == null) continue;
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
        var pairs = new List<KeyValuePair<string, SS.UiValue>>(data.Map.Length);
        foreach (var e in data.Map)
            pairs.Add(new KeyValuePair<string, SS.UiValue>(e.Key, e.Value));
        ApplyPairs(pairs);
    }

    internal void ApplyData(Dictionary<string, SS.UiValue> merged)
    {
        ApplyPairs(new List<KeyValuePair<string, SS.UiValue>>(merged));
    }

    private void ApplyPairs(List<KeyValuePair<string, SS.UiValue>> entries)
    {
        // Lua data lands outside Update; its cost counts toward the page's game-thread time all the same
        var d0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { ApplyPairsInner(entries); }
        finally { _allUpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - d0; }
    }

    private void ApplyPairsInner(List<KeyValuePair<string, SS.UiValue>> entries)
    {
        Hold();
        ForwardData(entries);
        // Ids bind first, always: a key naming an element is the simplest contract a page
        // has. A script's data handler gets the same payload as an event afterwards; keys
        // it consumes that match no id are not warned about when a script is present.
        BindById(entries, quiet: _script != null);
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

    private void ForwardData(List<KeyValuePair<string, SS.UiValue>> entries)
    {
        var flat = new List<SS.UiProp>();
        foreach (var e in entries)
            Flatten(flat, e.Key, e.Value);
        if (flat.Count == 0)
            return;
        foreach (var p in flat)
            _forwarded[p.Key] = p.Value;
        if (_sceneLive)
            SendData(flat);
    }

    private void SendData(List<SS.UiProp> props)
    {
        if (string.IsNullOrEmpty(DataElementId) || State is not SS.BoardState state)
            return;
        var map = new SS.UiValue { Type = SS.UiValueType.Map, Map = props.ToArray() };
        VectorBridge.Data(Board, Cartridge, Visor, state, Surface, DataElementId, "html:" + ElementId, map, null);
    }

    private static void Flatten(List<SS.UiProp> into, string key, SS.UiValue v)
    {
        switch (v.Type)
        {
            case SS.UiValueType.Number:
            case SS.UiValueType.String:
            case SS.UiValueType.Array:
                into.Add(new SS.UiProp { Key = key, Value = v });
                break;
            case SS.UiValueType.Bool:
                into.Add(new SS.UiProp { Key = key, Value = SS.UiValue.FromNumber(v.Bool ? 1f : 0f) });
                break;
            case SS.UiValueType.Map when v.Map != null:
                foreach (var e in v.Map)
                    Flatten(into, key + "_" + e.Key, e.Value);
                break;
        }
    }

    private void BindById(List<KeyValuePair<string, SS.UiValue>> entries, bool quiet = false)
    {
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

            if (string.IsNullOrEmpty(entry.Key) || !_byId.TryGetValue(entry.Key, out var ve))
            {
                // a key the scene reads as $name (an svg expression) is a legitimate target too
                if (!string.IsNullOrEmpty(entry.Key) && !quiet && _lastScene.Length > 0 && _lastScene.IndexOf("$" + entry.Key, StringComparison.Ordinal) < 0)  // before the first scene nothing is known yet
                    ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: data key \"{entry.Key}\" matches no element id and no $ expression");
                continue;
            }

            var v = entry.Value;
            switch (v.Type)
            {
                case SS.UiValueType.String:
                {
                    var l = TextTarget(ve, entry.Key);
                    if (l != null) l.text = v.String ?? string.Empty;
                    break;
                }
                case SS.UiValueType.Number:
                {
                    var l = TextTarget(ve, entry.Key);
                    if (l != null) l.text = v.Number.ToString("G", CultureInfo.InvariantCulture);
                    break;
                }
                case SS.UiValueType.Bool:
                    ve.style.display = v.Bool ? DisplayStyle.Flex : DisplayStyle.None;
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

    private void OnDestroy()
    {
        Surfaces.Remove(this);
        _script?.Dispose();
        if (_document != null)
            Destroy(_document.gameObject);
        if (_panel != null)
            Destroy(_panel);
        if (_texture != null)
        {
            _texture.Release();
            Destroy(_texture);
        }
        if (_pending != null)
        {
            _pending.Release();
            Destroy(_pending);
        }
    }
}

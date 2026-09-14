using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
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
    private string _lastScene = string.Empty;
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
        _source = source;
        EnsurePanel();
        Build();
    }

    // No capture path of its own any more. The page is drawn by the vector mod, whose
    // clone carries its mesh through Instantiate and builds inline when it has none, and
    // the first structure is emitted synchronously at build (below), so a capture that
    // rebuilds the surface sees the scene inside the same call.

    private void Update()
    {
        if (_panel == null)
            return;

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
            if (_script.Frame(Time.time, _byId))
                _dirty = true;
            if (_script.HasPendingWork)
                _awakeFrames = Mathf.Max(_awakeFrames, 2);
        }

        // Keyframe animations step at keyframe boundaries; UI Toolkit interpolates between.
        // While any is running the panel must stay awake to repaint. Time.time, so it
        // pauses with the game like the vector layer.
        var animating = false;
        foreach (var a in _animations)
        {
            a.Update(Time.time);
            if (a.Wrote) { a.Wrote = false; _dirty = true; }
            animating |= !a.Finished;
        }
        // The last tween ending re-emits the scene once with plain numbers: static again.
        if (_tweens.Expire(Time.time))
            _dirty = true;
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
            _dirty = false;
            EmitToVector();
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
        ScriptedScreensHtmlPlugin.Log?.LogInfo(
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
        foreach (var grid in built.Grids)
            GridLayout.Attach(grid, built);

        _script?.Dispose();
        _script = null;
        ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: page built, script {built.Script.Length} chars, {built.ById.Count} elements");
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
                        _dirty = true;
                        Wake();
                    }
                    else ScriptedScreensHtmlPlugin.Log?.LogWarning($"js: appendChild: no element \"{parentId}\"");
                },
                id =>
                {
                    if (_byId.TryGetValue(id, out var r))
                    {
                        HtmlRenderer.Remove(r, built);
                        _dirty = true;
                        Wake();
                    }
                });
        }
        _svgs.Clear();
        foreach (var shape in _shapes.Values)
        {
            if (shape.Owner != null && !_svgs.Contains(shape.Owner))
                _svgs.Add(shape.Owner);
        }

        _animations.Clear();
        foreach (var (element, spec) in built.Animations)
            _animations.Add(new KeyframeRunner(element, built.Keyframes[spec.Name], spec, Time.time, m => ScriptedScreensHtmlPlugin.Log?.LogWarning(m)));

        _dirty = true;
        Wake();
        // Queue the page script NOW, not next frame: the worker runs queued work in order, so
        // data that arrives right after build (remembered data on a rebuilt host) reaches a
        // page whose handler is already registered. Sizes are snapshotted per frame, so a
        // script that reads clientWidth before layout sees 0 and tries again next frame.
        _scriptPending = false;
        _script?.Run(built.Script);

        // The first structure goes out now rather than next Update: a screen capture
        // rebuilds the surface and clones it inside one call, and the vector mod can only
        // draw a scene it has been given by then.
        _dirty = false;
        EmitToVector();
    }

    private bool _scriptPending;

    /// <summary>
    /// Lay the page out now, translate it to scene text, and hand it to the vector mod.
    /// The scene is resent only when its text changed; the vector mod caches parsed scenes
    /// by source text, so an unchanged resend costs nothing.
    /// </summary>
    /// <summary>Ids of the ScriptedScreens elements created for img/video/audio, to update and remove.</summary>
    private readonly HashSet<string> _externals = new(StringComparer.Ordinal);
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
            var url = node.Attr("src") ?? string.Empty;
            var props = new List<SS.UiProp> { new() { Key = "url", Value = SS.UiValue.FromString(url) } };
            string type;
            switch (node.Tag)
            {
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
            var element = new SS.UiElement
            {
                Id = id,
                Type = type,
                Rect = new SS.UiRect { Unit = SS.UiRectUnit.Pixels, X = ext.X * sx, Y = ext.Y * sy, W = ext.W * sx, H = ext.H * sy },
                Props = props.ToArray(),
            };
            var stateKey = new System.Text.StringBuilder(type).Append('|').Append(element.Rect.X).Append(',').Append(element.Rect.Y).Append(',').Append(element.Rect.W).Append(',').Append(element.Rect.H);
            foreach (var pr in props) stateKey.Append('|').Append(pr.Key).Append('=').Append(pr.Value.String ?? pr.Value.Number.ToString(CultureInfo.InvariantCulture));
            var stateText = stateKey.ToString();
            if (_externalState.TryGetValue(id, out var last) && last == stateText)
                continue;
            _externalState[id] = stateText;
            try
            {
                SS.ApplyElementInternal(Board as Motherboard, Cartridge as CartridgeIntegratedCircuitLua, Visor as ProgrammableVisorGlasses, state, Surface, element);
                _externals.Add(id);
            }
            catch (Exception ex)
            {
                ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: <{node.Tag}> as {type} failed: {ex.Message}");
            }
        }
        foreach (var id in new List<string>(_externals))
        {
            if (seen.Contains(id)) continue;
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
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning("html: scene dump failed: " + ex.Message);
        }
    }

    private void EmitToVector()
    {
        if (_content == null || _document == null || _built == null)
            return;
        Wake();
        UpdateRuntimePanels?.Invoke(null, null);
        var root = _document.rootVisualElement;
        if (root == null)
            return;

        var layout = LayoutSize();
        _tweens.Diff(_content, _built, Time.time);
        var output = VectorEmitter.Emit(_built, _content, layout.x, layout.y, _tweens, Time.time);
        foreach (var w in output.Warnings)
            ScriptedScreensHtmlPlugin.Log?.LogWarning(w);
        // An identical scene is normally not resent. While tweens are live it must be: the
        // vector clock restarts on every apply and the expressions are written against it.
        ApplyExternals(output.Externals);
        if (output.Scene == _lastScene && !_tweens.Any)
            return;
        _lastScene = output.Scene;

        if (State is SS.BoardState state)
        {
            VectorBridge.Structure(Board, Cartridge, Visor, state, Surface, ElementId, "html:" + ElementId, output.Scene);
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: emitted {output.Nodes} vector nodes, {output.Scene.Length} chars");
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
        ForwardData(entries);
        if (_script != null)
        {
            // The worker decides after the page script has run: a data handler gets the
            // event; otherwise the id-binding below runs on the main thread. Queue order
            // keeps data after the script even when it arrives first.
            _script.EmitData(ToJson(entries), () => BindById(entries));
            Wake(DataAwakeFrames);
            return;
        }

        BindById(entries);
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

    private void BindById(List<KeyValuePair<string, SS.UiValue>> entries)
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
                if (!string.IsNullOrEmpty(entry.Key))
                    ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: data key \"{entry.Key}\" matches no element id");
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// What lets a page's translation run on a worker, the way the vector mod tessellates off the
/// game thread. UI Toolkit keeps its layout in native code that only the game thread may read,
/// so the game thread copies what the translator reads into a <see cref="Box"/> per element; the
/// worker reads only the copies. Fonts are answered from caches filled on the game thread: a
/// question the cache cannot answer is recorded, answered after the job, and the page emits again.
/// </summary>
internal static class OffThread
{
    /// <summary>True on a translation worker while it runs.</summary>
    [ThreadStatic] internal static bool Active;
    /// <summary>The copies the running translation reads, by element.</summary>
    [ThreadStatic] internal static Dictionary<VisualElement, Box>? Boxes;
    /// <summary>A copy was missing: the tree changed under the job, so its result is thrown away.</summary>
    [ThreadStatic] internal static bool Stale;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static float Seconds => (float)Clock.Elapsed.TotalSeconds;

    /// <summary>The resolved values the translator reads, copied from one element on the game thread.</summary>
    internal sealed class Box
    {
        public bool Seen;
        public Rect layout;
        public DisplayStyle display;
        public UnityEngine.UIElements.Visibility visibility;
        public float opacity, fontSize, marginBottom;
        public float borderTopWidth, borderRightWidth, borderBottomWidth, borderLeftWidth;
        public Color borderTopColor, borderRightColor, borderBottomColor, borderLeftColor;
        public float borderTopLeftRadius, borderTopRightRadius, borderBottomRightRadius, borderBottomLeftRadius;
        public float paddingTop, paddingRight, paddingBottom, paddingLeft;
        public Color color, backgroundColor;
        public WhiteSpace whiteSpace;
        public Vector3 translate;
        public Scale scale;
        public Rotate rotate;
        public TextAnchor unityTextAlign;
        public FontStyle unityFontStyleAndWeight;
        /// <summary>The inline overflow (what the renderer sets for overflow: hidden and scrolling).</summary>
        public Overflow overflow;
        /// <summary>A label's measured single-line width, for decoration lines; NaN when not measured.</summary>
        public float textWidth = float.NaN;

        internal void Read(VisualElement ve, bool measure)
        {
            var rs = ve.resolvedStyle;
            layout = ve.layout;
            display = rs.display;
            visibility = rs.visibility;
            opacity = rs.opacity;
            fontSize = rs.fontSize;
            marginBottom = rs.marginBottom;
            borderTopWidth = rs.borderTopWidth; borderRightWidth = rs.borderRightWidth; borderBottomWidth = rs.borderBottomWidth; borderLeftWidth = rs.borderLeftWidth;
            borderTopColor = rs.borderTopColor; borderRightColor = rs.borderRightColor; borderBottomColor = rs.borderBottomColor; borderLeftColor = rs.borderLeftColor;
            borderTopLeftRadius = rs.borderTopLeftRadius; borderTopRightRadius = rs.borderTopRightRadius; borderBottomRightRadius = rs.borderBottomRightRadius; borderBottomLeftRadius = rs.borderBottomLeftRadius;
            paddingTop = rs.paddingTop; paddingRight = rs.paddingRight; paddingBottom = rs.paddingBottom; paddingLeft = rs.paddingLeft;
            color = rs.color;
            backgroundColor = rs.backgroundColor;
            whiteSpace = rs.whiteSpace;
            translate = rs.translate;
            scale = rs.scale;
            rotate = rs.rotate;
            unityTextAlign = rs.unityTextAlign;
            unityFontStyleAndWeight = rs.unityFontStyleAndWeight;
            overflow = ve.style.overflow.value;
            textWidth = measure && ve is Label label
                ? label.MeasureTextSize(label.text ?? string.Empty, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x
                : float.NaN;
        }
    }

    /// <summary>The copy for an element: the job's snapshot, or (on the game thread without one) a fresh read.</summary>
    internal static Box Of(VisualElement ve)
    {
        if (Boxes != null && Boxes.TryGetValue(ve, out var box))
            return box;
        if (Active)
        {
            Stale = true;
            return Hidden;
        }
        var live = new Box();
        live.Read(ve, measure: ve is Label);
        return live;
    }

    private static readonly Box Hidden = new() { display = DisplayStyle.None, layout = new Rect(0f, 0f, float.NaN, float.NaN) };

    /// <summary>
    /// Copies every element under <paramref name="root"/> into <paramref name="boxes"/> (reusing the
    /// entries), and drops the entries of elements no longer in the tree. Game thread only.
    /// </summary>
    internal static void Capture(VisualElement root, HtmlRenderer.Result built, Dictionary<VisualElement, Box> boxes, List<VisualElement> scratch)
    {
        foreach (var b in boxes.Values) b.Seen = false;
        Walk(root, built, boxes);
        if (scratch.Count > 0) scratch.Clear();
        foreach (var kv in boxes) if (!kv.Value.Seen) scratch.Add(kv.Key);
        foreach (var ve in scratch) boxes.Remove(ve);
        scratch.Clear();
    }

    private static void Walk(VisualElement ve, HtmlRenderer.Result built, Dictionary<VisualElement, Box> boxes)
    {
        if (!boxes.TryGetValue(ve, out var box))
            boxes[ve] = box = new Box();
        box.Seen = true;
        box.Read(ve, measure: ve is Label && Decorated(built.CssOf(ve)));
        foreach (var child in ve.Children())
            Walk(child, built, boxes);
    }

    private static bool Decorated(Dictionary<string, string> css)
    {
        foreach (var key in css.Keys)
            if (key.StartsWith("text-decoration", StringComparison.Ordinal))
                return true;
        return false;
    }

    // ---- page-wide values the cascade leaves in StyleApplier statics, copied per job ----

    internal struct Globals
    {
        public float EmSize, RootFontSize, ViewportW, ViewportH;
        public bool ColorSchemeDark, Diagnostics;

        internal static Globals Take() => new()
        {
            EmSize = StyleApplier.EmSize,
            RootFontSize = StyleApplier.RootFontSize,
            ViewportW = StyleApplier.ViewportW,
            ViewportH = StyleApplier.ViewportH,
            ColorSchemeDark = StyleApplier.ColorSchemeDark,
            Diagnostics = HtmlConfig.Diagnostics,
        };
    }

    [ThreadStatic] internal static Globals Job;

    // ---- fonts ----

    /// <summary>
    /// Font answers the translator needs, cached under a lock. On the game thread they are computed
    /// (and cached); on a worker a cached answer is used and an unknown one is queued and answered
    /// false/0 for now, which <see cref="ResolveFonts"/> fixes after the job.
    /// </summary>
    private static readonly object FontGate = new();
    private static readonly Dictionary<string, (bool known, float at)> Libraries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (bool known, float at)> Faces = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (float em, float at)> Digits = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string face, char c), bool> Chars = new();
    private static readonly HashSet<(int kind, string name, char c)> Asked = new();
    private const float RetrySeconds = 5f;

    /// <summary>FontLibrary.Get(name) != null.</summary>
    internal static bool Library(string name)
    {
        lock (FontGate)
            if (Libraries.TryGetValue(name, out var e) && (e.known || Seconds - e.at < RetrySeconds))
                return e.known;
        if (Active) { Ask(0, name, '\0'); return false; }
        var known = FontLibrary.Get(name) != null;
        lock (FontGate) Libraries[name] = (known, Seconds);
        return known;
    }

    /// <summary>A TextMeshPro face of that name is registered.</summary>
    internal static bool Face(string name)
    {
        lock (FontGate)
            if (Faces.TryGetValue(name, out var e) && (e.known || Seconds - e.at < RetrySeconds))
                return e.known;
        if (Active) { Ask(1, name, '\0'); return false; }
        var known = VectorEmitter.AssetOf(name) != null;
        lock (FontGate) Faces[name] = (known, Seconds);
        return known;
    }

    /// <summary>The face's widest digit advance in em; 0 unknown.</summary>
    internal static float Digit(string name)
    {
        lock (FontGate)
            if (Digits.TryGetValue(name, out var e) && (e.em > 0f || Seconds - e.at < RetrySeconds))
                return e.em;
        if (Active) { Ask(2, name, '\0'); return 0f; }
        var value = VectorEmitter.DigitEmOf(name);
        lock (FontGate) Digits[name] = (value, Seconds);
        return value;
    }

    /// <summary>The face (or, for the fallback face, its fallback chain, adding to a dynamic atlas) has the character.</summary>
    internal static bool Has(string face, char c, bool viaFallbackChain)
    {
        lock (FontGate)
            if (Chars.TryGetValue((face, c), out var has))
                return has;
        // unknown on a worker: assume present, so nothing is wrapped in a fallback face until it is known
        if (Active) { Ask(viaFallbackChain ? 4 : 3, face, c); return !viaFallbackChain; }
        var asset = VectorEmitter.AssetOf(face);
        var value = asset != null && (viaFallbackChain ? asset.HasCharacter(c, true, true) : asset.HasCharacter(c));
        lock (FontGate) Chars[(face, c)] = value;
        return value;
    }

    private static void Ask(int kind, string name, char c)
    {
        lock (FontGate) Asked.Add((kind, name, c));
    }

    private static readonly List<(int kind, string name, char c)> Asking = new();

    /// <summary>Answers what workers asked. Game thread. True when an answer differs from what the worker assumed (the page emits again).</summary>
    internal static bool ResolveFonts()
    {
        lock (FontGate)
        {
            if (Asked.Count == 0) return false;
            Asking.AddRange(Asked);
            Asked.Clear();
        }
        var changed = false;
        foreach (var (kind, name, c) in Asking)
        {
            changed |= kind switch
            {
                0 => Library(name),
                1 => Face(name),
                2 => Digit(name) > 0f,
                3 => !Has(name, c, false),
                _ => Has(name, c, true),
            };
        }
        Asking.Clear();
        return changed;
    }
}

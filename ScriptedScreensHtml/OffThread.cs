using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UIElements;
using UIVisibility = UnityEngine.UIElements.Visibility;

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

    /// <summary>The game thread, recorded at load; engine objects are only touched there.</summary>
    internal static int MainThreadId = -1;
    internal static bool OnMain => MainThreadId < 0 || Environment.CurrentManagedThreadId == MainThreadId;

    /// <summary>The game's clock (Time.time) this frame, for page code off the game thread.</summary>
    internal static volatile float Now;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    internal static float Seconds => (float)Clock.Elapsed.TotalSeconds;

    /// <summary>The resolved values the translator reads, copied from one element on the game thread.</summary>
    /// <summary>Bookkeeping, not part of what the element looks like: excluded from <see cref="Box.Same"/>.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class BookkeepingAttribute : Attribute { }

    internal sealed class Box
    {
        [Bookkeeping] public bool Seen;
        /// <summary>The twin this element's next read goes into, so a comparison needs no copying: on a change the two swap places.</summary>
        [Bookkeeping] public Box? Other;

        // ---- what the emitter wrote for this element and everything under it, to write again
        // unchanged. Kept here because this is the one per-element, per-page store the page thread
        // already has in hand. Cleared by the emitter, never by the capture.
        [Bookkeeping] public char[]? CacheBody;
        [Bookkeeping] public int CacheBodyLength;
        [Bookkeeping] public char[]? CacheDefs;
        [Bookkeeping] public int CacheDefsLength;
        [Bookkeeping] public int CacheNodes;
        [Bookkeeping] public Vector2 CacheParentPos;
        [Bookkeeping] public int CacheDepth;
        [Bookkeeping] public long CacheEpoch;
        /// <summary>False when this subtree must not be reused: it produced externals, deferred elements, or something the comparison cannot see.</summary>
        [Bookkeeping] public bool CacheUsable;
        /// <summary>Something about this element changed since the last frame the emitter used.</summary>
        [Bookkeeping] public bool Changed = true;
        /// <summary>This element or something under it changed; cleared by the emitter when it rebuilds.</summary>
        [Bookkeeping] public bool SubtreeChanged = true;
        public Rect layout;
        public DisplayStyle display;
        public UIVisibility visibility;
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
        /// <summary>A label's text. Here rather than tracked at each writer: a label can change its
        /// words without changing its box, and this way every writer is covered by the comparison.</summary>
        public string? text;

        /// <summary>
        /// Every field that says what the element looks like, compared. Hand-written because the
        /// language will not generate it for a class - and pinned by BoxSameCoversEveryField, which
        /// reflects over the fields and fails if one of them is not compared here. Add a field and
        /// the test tells you, rather than a page quietly showing last frame's paint.
        /// </summary>
        internal bool Same(Box o) =>
            layout == o.layout
            && display == o.display && visibility == o.visibility
            && opacity.Equals(o.opacity) && fontSize.Equals(o.fontSize) && marginBottom.Equals(o.marginBottom)
            && borderTopWidth.Equals(o.borderTopWidth) && borderRightWidth.Equals(o.borderRightWidth)
            && borderBottomWidth.Equals(o.borderBottomWidth) && borderLeftWidth.Equals(o.borderLeftWidth)
            && borderTopColor == o.borderTopColor && borderRightColor == o.borderRightColor
            && borderBottomColor == o.borderBottomColor && borderLeftColor == o.borderLeftColor
            && borderTopLeftRadius.Equals(o.borderTopLeftRadius) && borderTopRightRadius.Equals(o.borderTopRightRadius)
            && borderBottomRightRadius.Equals(o.borderBottomRightRadius) && borderBottomLeftRadius.Equals(o.borderBottomLeftRadius)
            && paddingTop.Equals(o.paddingTop) && paddingRight.Equals(o.paddingRight)
            && paddingBottom.Equals(o.paddingBottom) && paddingLeft.Equals(o.paddingLeft)
            && color == o.color && backgroundColor == o.backgroundColor
            && whiteSpace == o.whiteSpace
            && translate == o.translate
            && scale.value == o.scale.value
            && rotate.angle.value.Equals(o.rotate.angle.value) && rotate.angle.unit == o.rotate.angle.unit
            && unityTextAlign == o.unityTextAlign && unityFontStyleAndWeight == o.unityFontStyleAndWeight
            && overflow == o.overflow
            && (textWidth.Equals(o.textWidth) || (float.IsNaN(textWidth) && float.IsNaN(o.textWidth)))
            && string.Equals(text, o.text, StringComparison.Ordinal);

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
            text = (ve as Label)?.text;
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
    /// entries), and drops the entries of elements no longer in the tree. Runs on whichever thread
    /// owns the page at the time: the game thread at build, the page thread per frame.
    /// </summary>
    internal static void Capture(VisualElement root, HtmlRenderer.Result built, Dictionary<VisualElement, Box> boxes, List<VisualElement> scratch)
    {
        foreach (var b in boxes.Values) b.Seen = false;
        Walk(root, built, boxes, built.Touched.Count > 0, false);
        built.Touched.Clear();
        built.TouchedDeep.Clear();
        if (scratch.Count > 0) scratch.Clear();
        foreach (var kv in boxes) if (!kv.Value.Seen) scratch.Add(kv.Key);
        foreach (var ve in scratch) boxes.Remove(ve);
        scratch.Clear();
    }

    /// <summary>
    /// Reads one element and its descendants, and says whether anything under here changed. The
    /// read goes into the entry's twin and the two swap on a change, so nothing is copied and the
    /// previous frame's values are still there to compare against. The flags are sticky: a frame
    /// that captures without emitting must not lose the fact that something moved, so only the
    /// emitter clears them, when it rebuilds that element.
    /// </summary>
    /// <param name="anyTouched">Whether anything reported a record change at all this frame.</param>
    /// <param name="forcedFromAbove">An ancestor's record changed, so what this element inherits may have too.</param>
    private static bool Walk(VisualElement ve, HtmlRenderer.Result built, Dictionary<VisualElement, Box> boxes, bool anyTouched, bool forcedFromAbove)
    {
        // A record change on this element; a deep one also decides what its descendants inherit.
        var touched = anyTouched && built.Touched.Contains(ve);
        var deep = forcedFromAbove || (touched && built.TouchedDeep.Contains(ve));
        var forced = touched || forcedFromAbove;
        var measure = ve is Label && Decorated(built.CssOf(ve));
        bool changed;
        if (!boxes.TryGetValue(ve, out var box))
        {
            boxes[ve] = box = new Box();
            box.Other = new Box { Other = box };
            box.Read(ve, measure);
            changed = true;
        }
        else
        {
            var spare = box.Other ??= new Box { Other = box };
            spare.Read(ve, measure);
            if (spare.Same(box))
            {
                changed = forced;
            }
            else
            {
                // the fresh read becomes the current one and the old becomes the spare
                spare.Seen = box.Seen;
                spare.CacheBody = box.CacheBody; spare.CacheBodyLength = box.CacheBodyLength;
                spare.CacheDefs = box.CacheDefs; spare.CacheDefsLength = box.CacheDefsLength;
                spare.CacheNodes = box.CacheNodes; spare.CacheParentPos = box.CacheParentPos;
                spare.CacheDepth = box.CacheDepth; spare.CacheEpoch = box.CacheEpoch;
                spare.CacheUsable = box.CacheUsable;
                boxes[ve] = spare;
                box = spare;
                changed = true;
            }
        }
        if (changed) { box.Changed = true; box.SubtreeChanged = true; }
        box.Seen = true;

        var subtree = changed;
        foreach (var child in ve.Children())
            subtree |= Walk(child, built, boxes, anyTouched, deep);
        if (subtree) box.SubtreeChanged = true;
        return subtree;
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

    /// <summary>Bumped whenever a font answer changes. A label's text depends on these, and they
    /// arrive minutes after a page is built, so every cached emission is keyed on this number.</summary>
    internal static volatile int FontEpoch;

    /// <summary>FontLibrary.Get(name) != null.</summary>
    internal static bool Library(string name)
    {
        lock (FontGate)
            if (Libraries.TryGetValue(name, out var e) && (e.known || Seconds - e.at < RetrySeconds))
                return e.known;
        if (Active) { Ask(0, name, '\0'); return false; }
        var known = FontLibrary.Get(name) != null;
        lock (FontGate) { Libraries[name] = (known, Seconds); FontEpoch++; }
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
        lock (FontGate) { Faces[name] = (known, Seconds); FontEpoch++; }
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
        lock (FontGate) { Digits[name] = (value, Seconds); FontEpoch++; }
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
        lock (FontGate) { Chars[(face, c)] = value; FontEpoch++; }
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

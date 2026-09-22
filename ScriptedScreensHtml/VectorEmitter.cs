using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// Translates a laid-out page into the vector mod's scene text. HTML and CSS give the
/// structure and the cascade, UI Toolkit gives the layout (used for that alone), and the
/// vector renderer draws: geometry, off-thread, in the Fonts mod's faces, with expressions
/// for motion. Boxes become R, text becomes T, overflow becomes a clip, gradients become
/// defs, SVG shapes become their vector twins, and an SVG attribute that starts with "="
/// is passed through as an expression.
/// </summary>
internal static class VectorEmitter
{
    internal sealed class Output
    {
        /// <summary>
        /// The scene as characters, in a buffer the emitter keeps: the split reads it straight from
        /// here, and only a structure that is actually being sent (or a dump) pays for a string.
        /// A 10 KB scene was 20 KB of garbage a frame, made and thrown away without being read.
        /// </summary>
        public char[] Chars = System.Array.Empty<char>();
        public int Length;

        private string? _scene;

        /// <summary>The scene as a string, made once when something needs one.</summary>
        public string Scene => _scene ??= new string(Chars, 0, Length);

        /// <summary>Back to empty, keeping the character buffer: one page's frame hands its result
        /// to the surface and the next frame reuses this, exactly as the scene builder does.</summary>
        internal void Reset()
        {
            Nodes = 0;
            Warnings.Clear();
            Externals.Clear();
            Length = 0;
            _scene = null;
        }

        internal void Take(StringBuilder built)
        {
            Length = built.Length;
            if (Chars.Length < Length) Chars = new char[System.Math.Max(Length, Chars.Length * 2)];
            built.CopyTo(0, Chars, 0, Length);
            _scene = null;
        }

        public int Nodes;
        public readonly List<string> Warnings = new();
        /// <summary>Boxes the vector layer does not draw: the surface places ScriptedScreens elements over them.</summary>
        public readonly List<External> Externals = new();
    }

    internal sealed class External
    {
        public string Key = string.Empty;
        public HtmlNode Node = null!;
        public VisualElement Ve = null!;
        public float X, Y, W, H;
    }

    private sealed class Ctx
    {
        public StringBuilder Body = new();
        public StringBuilder Defs = new();
        public HtmlRenderer.Result Built = null!;
        /// <summary>
        /// Def ids are minted per element: "grad7_2" is the second gradient of element 7, not the
        /// ninth gradient of the page. A single counter made an element's text depend on how many
        /// defs every earlier element happened to need, so one element gaining a gradient rewrote
        /// every id after it - a new template, and a whole structure resent, for nothing.
        /// </summary>
        public int Ids;
        private int _element, _seq;

        /// <summary>Mint this element's ids from now on; the caller restores what was current.</summary>
        public (int element, int seq) Enter(int index)
        {
            var was = (_element, _seq);
            _element = index;
            _seq = 0;
            return was;
        }

        public void Leave((int element, int seq) was) { _element = was.element; _seq = was.seq; }

        public string NextId(string prefix)
        {
            var n = ++_seq;
            return prefix + _element.ToString(CultureInfo.InvariantCulture) + "_" + n.ToString(CultureInfo.InvariantCulture);
        }
        public readonly Output Out = new();
        public HashSet<string> Reported = new(StringComparer.Ordinal);
        public Tweens? Tw;
        public float Now;
        /// <summary>Top of the scroll container being emitted, or NaN outside one: what a sticky child pins to.</summary>
        public float ScrollTop = float.NaN;
        /// <summary>Viewport height and scrollable range (content minus viewport) of that container.</summary>
        public float ScrollH, ScrollRange;
        /// <summary>The page's design size, for full-page overlays (::backdrop).</summary>
        public float PageW, PageH;
        /// <summary>The scale of the svg group being emitted (1 when the fit is baked), for vector-effect: non-scaling-stroke.</summary>
        public float SvgScale = 1f;
        /// <summary>Scroll offsets a script set, by box id (HtmlSurface.ScrollSet).</summary>
        public Dictionary<string, (float offset, int version)>? ScrollSet;
        /// <summary>Positioned elements with a z-index, emitted after everything else at the root in z order: a stacking context across parents.</summary>
        public List<(VisualElement ve, Vector2 parentPos, int z)> Deferred = new();
        public bool EmittingDeferred;
        /// <summary>What the page's own state hashes to this frame; a cached emission from another one is not reused.</summary>
        public long Epoch;
        /// <summary>Reused emissions this frame, and elements rebuilt: the diagnostics line reads these.</summary>
        public int Reused, Rebuilt;
        /// <summary>The scene text is assembled here, and child lists are borrowed from these: a page
        /// emits every frame, so nothing here is allocated again once the first frame has run.</summary>
        public readonly StringBuilder Scene = new(8192);
        /// <summary>One label's T line is assembled here and appended to the body: reused, since a page emits every frame.</summary>
        public readonly StringBuilder Label = new(256);
        /// <summary>The pseudo-elements any rule of this page styles, and what they were collected from: a page with no ::first-line rule must not probe every rule per label per frame.</summary>
        public HashSet<string>? Pseudos;
        public object? PseudosOf;
        public int PseudosRules;
        private readonly Stack<List<VisualElement>> _children = new();
        private readonly Stack<List<(int z, int i, VisualElement c)>> _sorting = new();
        private readonly Stack<Dictionary<string, string>> _records = new();

        /// <summary>One transform per depth of the walk: an element's own transform is finished with
        /// before its children are emitted, and each level below takes the next one.</summary>
        private readonly List<Xform> _xforms = new();
        public Xform RentXform(int depth)
        {
            while (_xforms.Count <= depth) _xforms.Add(new Xform());
            return _xforms[depth];
        }

        public List<VisualElement> RentChildren() => _children.Count > 0 ? _children.Pop() : new List<VisualElement>();
        public void Return(List<VisualElement> list) { list.Clear(); _children.Push(list); }
        public List<(int z, int i, VisualElement c)> RentSorting() => _sorting.Count > 0 ? _sorting.Pop() : new List<(int, int, VisualElement)>();
        public void Return(List<(int z, int i, VisualElement c)> list) { list.Clear(); _sorting.Push(list); }
        public Dictionary<string, string> RentRecord() => _records.Count > 0 ? _records.Pop() : new Dictionary<string, string>(StringComparer.Ordinal);
        public void Return(Dictionary<string, string> record) { record.Clear(); _records.Push(record); }

        /// <summary>
        /// Every svg and canvas the emitter has drawn, with a hash of what it read from it last
        /// time. Kept across frames, so it is not cleared by <see cref="Reset"/>; an element that
        /// has left the tree is dropped when its snapshot goes. See <see cref="Invalidate"/>.
        /// </summary>
        public readonly List<(VisualElement ve, long sig)> Watched = new();

        public void Reset(HtmlRenderer.Result built, Tweens? tweens, float now, Dictionary<string, (float offset, int version)>? scrollSet, float pageW, float pageH)
        {
            Body.Clear(); Defs.Clear(); Scene.Clear(); Reported.Clear(); Deferred.Clear();
            // Per frame, not per thread: these were only ever added to, so the diagnostics line
            // reported a running total - "13972 subtrees reused" for a 150-element page - and every
            // reading taken from it was meaningless.
            Reused = 0; Rebuilt = 0;
            WhyChanged = WhyNoCache = WhyMoved = WhyDepth = WhyEpoch = WhyTween = 0;
            Built = built; Tw = tweens; Now = now; ScrollSet = scrollSet; PageW = pageW; PageH = pageH;
            Ids = 0; EmittingDeferred = false; ScrollTop = float.NaN; ScrollH = 0f; ScrollRange = 0f; SvgScale = 1f;
            Out.Reset();
        }
    }

    [ThreadStatic] private static Ctx? _ctx;

    /// <summary>
    /// Emit everything from scratch, ignoring what elements kept from last frame. The verify mode
    /// emits a frame both ways and compares: if they differ, the cache is reusing text whose inputs
    /// it cannot see, and a console would be showing something stale.
    /// </summary>
    [ThreadStatic] internal static bool NoCache;

    /// <summary>Subtrees written from the last frame's characters, and elements rebuilt (diagnostics).</summary>
    internal static int LastReused, LastRebuilt;
    /// <summary>Why elements were rebuilt (diagnostics): changed, no cache yet, moved, deeper, another epoch, tweening.</summary>
    internal static int WhyChanged, WhyNoCache, WhyMoved, WhyDepth, WhyEpoch, WhyTween;

    /// <summary>
    /// Everything an element's text depends on that is not the element: the page's em and root font
    /// size, the viewport, the colour scheme, how many rules there are, the page size, the scroll
    /// offsets a script set, and how many font answers have arrived. A cached emission made under a
    /// different one of these is not reused.
    /// </summary>
    private static long EpochOf(Ctx ctx)
    {
        var g = OffThread.Job;
        var h = 17L;
        h = h * 31 + g.EmSize.GetHashCode();
        h = h * 31 + g.RootFontSize.GetHashCode();
        h = h * 31 + g.ViewportW.GetHashCode();
        h = h * 31 + g.ViewportH.GetHashCode();
        h = h * 31 + (g.ColorSchemeDark ? 1 : 0);
        h = h * 31 + ctx.Built.Rules.Count;
        h = h * 31 + ctx.PageW.GetHashCode();
        h = h * 31 + ctx.PageH.GetHashCode();
        h = h * 31 + OffThread.FontEpoch;
        if (ctx.ScrollSet != null)
            foreach (var kv in ctx.ScrollSet) h = h * 31 + kv.Value.version;
        return h;
    }

    // ---- svg and canvas content, which no Box field covers -----------------------------------
    //
    // A script writes an svg shape's attribute (SvgShape.Set) or a canvas frame
    // (CanvasElement.SetFrame) and NOTHING about the element's resolved box moves, so
    // OffThread.Box.Same sees no change and nobody calls Result.Touch. That is why these two were
    // excluded from the cache - but excluding them was never enough: an ancestor's cached text
    // CONTAINS the svg's, and the ancestor was always cacheable, so a page whose script animates a
    // shape replayed last frame's gauge for ever. (Reproduced: a circle whose `r` a script writes
    // each frame emitted rx=26 while a fresh emission said rx=27, on every frame.)
    //
    // So each svg and canvas keeps a hash of everything the emitter reads from it, and a change
    // marks the element AND its ancestors changed. With that in the signature they are ordinary
    // cacheable elements.

    /// <summary>Everything <see cref="EmitSvg"/> / <see cref="EmitCanvas"/> read from the element itself; 0 for anything else.</summary>
    // ponytail: a canvas is hashed over its whole command list every frame. That is O(n) on a
    // value that costs O(n) to emit, so it cannot dominate; if a huge static canvas ever shows up,
    // compare cv.Commands by reference first (SetFrame installs a fresh array per frame today).
    private static long ContentSig(VisualElement ve)
    {
        var h = 17L;
        switch (ve)
        {
            case SvgElement svg:
                var vb = svg.ViewBox;
                h = h * 31 + vb.x.GetHashCode(); h = h * 31 + vb.y.GetHashCode();
                h = h * 31 + vb.width.GetHashCode(); h = h * 31 + vb.height.GetHashCode();
                h = h * 31 + (svg.Stretch ? 1 : 0);
                h = h * 31 + svg.Shapes.Count;
                for (var i = 0; i < svg.Shapes.Count; i++) h = ShapeSig(h, svg.Shapes[i]);
                return h;
            case CanvasElement cv:
                h = h * 31 + cv.CanvasWidth.GetHashCode();
                h = h * 31 + cv.CanvasHeight.GetHashCode();
                h = h * 31 + cv.Count;
                var cmds = cv.Commands;
                var n = System.Math.Min(cv.Count, cmds.Length);
                for (var i = 0; i < n; i++) h = h * 31 + cmds[i].GetHashCode();
                h = h * 31 + cv.Strings.Count;
                for (var i = 0; i < cv.Strings.Count; i++) h = h * 31 + StringComparer.Ordinal.GetHashCode(cv.Strings[i]);
                return h;
            default:
                return 0;
        }
    }

    /// <summary>A shape's tag and every attribute of it, and of a clipPath's children.</summary>
    private static long ShapeSig(long h, SvgShape shape)
    {
        h = h * 31 + StringComparer.Ordinal.GetHashCode(shape.Tag);
        h = h * 31 + shape.Attributes.Count;
        foreach (var kv in shape.Attributes)
        {
            h = h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(kv.Key);
            h = h * 31 + StringComparer.Ordinal.GetHashCode(kv.Value);
        }
        if (shape.Children == null) return h;
        h = h * 31 + shape.Children.Count;
        for (var i = 0; i < shape.Children.Count; i++) h = ShapeSig(h, shape.Children[i]);
        return h;
    }

    /// <summary>
    /// Before the walk: an svg or canvas whose content changed marks itself and every ancestor
    /// changed, since each ancestor's cached text contains this element's. Elements that have left
    /// the tree (no snapshot) are dropped here.
    /// </summary>
    private static void Invalidate(Ctx ctx)
    {
        var boxes = OffThread.Boxes;
        // No snapshot means Of() answers with a fresh Box every time, so nothing is cached anyway.
        if (boxes == null || ctx.Watched.Count == 0) return;
        for (var i = ctx.Watched.Count - 1; i >= 0; i--)
        {
            var (ve, was) = ctx.Watched[i];
            if (ve.parent == null) { ctx.Watched.RemoveAt(i); continue; }   // out of the tree for good
            // Not this page's element: if a thread ever serves two pages, leave the entry alone
            // rather than consuming its change against the wrong snapshot.
            if (!boxes.ContainsKey(ve)) continue;
            var now = ContentSig(ve);
            if (now == was) continue;
            ctx.Watched[i] = (ve, now);
            for (var a = ve; a != null; a = a.parent)
                if (boxes.TryGetValue(a, out var box)) box.SubtreeChanged = true;
        }
    }

    /// <summary>This svg or canvas is one to watch from now on, at the content it is being drawn at.</summary>
    private static void Watch(Ctx ctx, VisualElement ve)
    {
        for (var i = 0; i < ctx.Watched.Count; i++)
            if (ReferenceEquals(ctx.Watched[i].ve, ve)) { ctx.Watched[i] = (ve, ContentSig(ve)); return; }
        ctx.Watched.Add((ve, ContentSig(ve)));
    }

    public static Output Emit(HtmlRenderer.Result built, VisualElement root, float designW, float designH, Tweens? tweens = null, float now = 0f, Dictionary<string, (float offset, int version)>? scrollSet = null)
    {
        var ctx = _ctx ??= new Ctx();
        ctx.Reset(built, tweens, now, scrollSet, designW, designH);
        ctx.Epoch = EpochOf(ctx);
        Invalidate(ctx);
        EmitElement(ctx, root, Vector2.zero, 0);
        if (ctx.Deferred.Count > 0)
        {
            ctx.EmittingDeferred = true;
            ctx.Deferred.Sort((a, b) => a.z.CompareTo(b.z));
            foreach (var (dve, dpos, _) in ctx.Deferred)
                EmitElement(ctx, dve, dpos, 1);
        }
        LastReused = ctx.Reused;
        LastRebuilt = ctx.Rebuilt;
        var sb = ctx.Scene;
        sb.Append("SCENE w=").AppendNum(designW).Append(" h=").AppendNum(designH).Append(" fit=stretch\n");
        if (ctx.Defs.Length > 0)
            sb.Append("DEFS {\n").Append(ctx.Defs).Append("}\n");
        sb.Append(ctx.Body);
        ctx.Out.Take(sb);
        return ctx.Out;
    }

    // ---------------------------------------------------------------- elements

    private static void EmitElement(Ctx ctx, VisualElement ve, Vector2 parentPos, int depth)
    {
        var rs = OffThread.Of(ve);
        if (rs.display == DisplayStyle.None || rs.visibility == UnityEngine.UIElements.Visibility.Hidden)
            return;

        var layout = rs.layout;
        if (float.IsNaN(layout.width) || float.IsNaN(layout.height))
            return;
        var x = parentPos.x + layout.x;
        var y = parentPos.y + layout.y;
        var w = layout.width;
        var h = layout.height;
        var css = ctx.Built.CssOf(ve);
        var indent = Indent(depth);
        var topNode = ctx.Built.NodeOf.TryGetValue(ve, out var dialogNode) ? dialogNode : null;
        var modal = topNode != null && topNode.Tag == "dialog" && topNode.Attr("data-modal") != null;
        var popoverOpen = topNode != null && topNode.Attr("popover") != null && topNode.Attr("data-popover-open") != null;
        if ((modal || popoverOpen) && !ctx.EmittingDeferred)
        {
            // the top layer: a modal dialog or an open popover paints over everything, whatever its place in the document
            ctx.Deferred.Add((ve, parentPos, modal ? int.MaxValue : int.MaxValue - 1));
            return;
        }
        // a modal dialog dims the page behind it: the ::backdrop pseudo-element, a full-page box under the dialog;
        // a popover's backdrop is drawn only when a rule gives it a background
        if (modal || (popoverOpen && (PseudoCss(ctx, ve, "backdrop").ContainsKey("background") || PseudoCss(ctx, ve, "backdrop").ContainsKey("background-color"))))
        {
            var bd = PseudoCss(ctx, ve, "backdrop");
            var backdrop = new Color(0f, 0f, 0f, 0.1f);
            if ((bd.TryGetValue("background", out var bdc) || bd.TryGetValue("background-color", out bdc)) && StyleApplier.TryColor(bdc, out var bcol)) backdrop = bcol;
            if (bd.TryGetValue("opacity", out var bdo)) backdrop.a *= StyleApplier.Num(bdo);
            ctx.Body.Append(indent).Append("R x=0 y=0 w=").AppendNum(ctx.PageW).Append(" h=").AppendNum(ctx.PageH).Append(" f=").AppendHex(backdrop).Append('\n');
            ctx.Out.Nodes++;
        }

        // A ScriptedScreens control (input, select, video...) fills the element's content box:
        // the page still paints the element's background and border around it, as a browser does.
        ctx.Built.Externals.TryGetValue(ve, out var external);
        if (external != null)
            ctx.Out.Externals.Add(new External { Key = ve.name, Node = external, Ve = ve, X = x + rs.borderLeftWidth, Y = y + rs.borderTopWidth, W = Mathf.Max(1f, w - rs.borderLeftWidth - rs.borderRightWidth), H = Mathf.Max(1f, h - rs.borderTopWidth - rs.borderBottomWidth) });

        // A positioned element with a z-index paints above its parent's later siblings: it is
        // emitted at the root after everything, in z order, unless we are already doing that.
        if (depth > 0 && !ctx.EmittingDeferred && css.TryGetValue("position", out var posn) && posn.Trim() is "absolute" or "fixed"
            && css.TryGetValue("z-index", out var zs) && int.TryParse(zs.Trim(), out var zi))
        {
            ctx.Deferred.Add((ve, parentPos, zi));
            return;
        }

        // A transition in flight: numbers below become expressions over t (Tweens.cs).
        var scrollTop = float.NaN;
        var scrollCh = 0f;
        // backface-visibility: hidden with a rotateX/rotateY past 90 degrees: the back of the card, not drawn
        if (css.TryGetValue("backface-visibility", out var bfv) && bfv.Trim() == "hidden" && css.TryGetValue("transform", out var bft) && BackfaceTurned(bft))
            return;
        // Nothing under here changed, and the page's own state is the same: write what this subtree
        // wrote last time. The check is the element's resolved box (compared field by field, pinned
        // by a test), everything a writer reported through Result.Touch, where the element sits
        // (a parent that moved shifts our absolute coordinates) and how deep it is (the indent).
        var tw = ctx.Tw?.Of(ve, ctx.Now);
        if (!NoCache && rs.CacheUsable && !rs.SubtreeChanged && rs.CacheEpoch == ctx.Epoch && tw == null
            && rs.CacheParentPos == parentPos && rs.CacheDepth == depth && rs.CacheBody != null)
        {
            ctx.Body.Append(rs.CacheBody, 0, rs.CacheBodyLength);
            if (rs.CacheDefsLength > 0) ctx.Defs.Append(rs.CacheDefs, 0, rs.CacheDefsLength);
            ctx.Out.Nodes += rs.CacheNodes;
            ctx.Reused++;
            return;
        }
        ctx.Rebuilt++;
        if (rs.SubtreeChanged) WhyChanged++;
        else if (!rs.CacheUsable || rs.CacheBody == null) WhyNoCache++;
        else if (tw != null) WhyTween++;
        else if (rs.CacheEpoch != ctx.Epoch) WhyEpoch++;
        else if (rs.CacheParentPos != parentPos) WhyMoved++;
        else if (rs.CacheDepth != depth) WhyDepth++;
        var bodyFrom = ctx.Body.Length;
        var defsFrom = ctx.Defs.Length;
        var nodesFrom = ctx.Out.Nodes;
        var externalsFrom = ctx.Out.Externals.Count;
        var deferredFrom = ctx.Deferred.Count;

        // From here on this element mints its own def ids and nothing returns early, so the
        // restore below always runs.
        var outerIds = ctx.Enter(ctx.Built.EmitIndexOf(ve));
        var ws = tw?.Lerp(tw.From.Rect.width, w);
        var hs = tw?.Lerp(tw.From.Rect.height, h);

        // Wrappers: position, transform, opacity, clip. Each opens a G that is closed after children.
        var groups = 0;
        if (!float.IsNaN(ctx.ScrollTop) && css.TryGetValue("position", out var pos) && pos.Trim() == "sticky")
        {
            // position: sticky inside a scrolling box: the element scrolls with the content
            // until it would leave the viewport top, then pins `top` below it. sy is the
            // container's scroll offset, so the lift is max(0, (viewportTop + top + sy) - y).
            var top = css.TryGetValue("top", out var t) ? StyleApplier.Num(t) : 0f;
            ctx.Body.Append(indent).Append("G t=[0,\"=max(0,").AppendNum(ctx.ScrollTop + top - y).Append("+sy)\"] {\n");
            groups++;
        }
        if (tw != null && (Mathf.Abs(tw.From.Rect.x - tw.To.Rect.x) > 0.01f || Mathf.Abs(tw.From.Rect.y - tw.To.Rect.y) > 0.01f))
        {
            // The subtree is emitted at its final place; this group carries it there from
            // where it was, so children move with it without expressions of their own.
            ctx.Body.Append(indent).Append("G t=[\"").Append(tw.Remaining(tw.From.Rect.x, tw.To.Rect.x)).Append("\",\"")
                .Append(tw.Remaining(tw.From.Rect.y, tw.To.Rect.y)).Append("\"] {\n");
            groups++;
        }
        // offset-path + offset-distance + offset-rotate: the box moved to its point on the path and turned along it
        var offset = css.TryGetValue("offset-path", out var opath) ? Offset(css, opath, ve, x, y, w, h, tw) : null;
        var xform = Xform.From(ctx, depth, ve, tw, x, y, w, h, offset);
        if (xform != null)
        {
            ctx.Body.Append(indent);
            xform.AppendTo(ctx.Body);
            // The wrapper carries its element's id when a script drives it, so its translate,
            // rotation and scale become named slots ($name_t_0 and so on) rather than positional
            // ones nothing outside can address. Only those few: an identified node makes the
            // renderer keep its whole prop array, so naming every wrapper would retain hundreds per
            // page. The id alone, never `click=1` - a button's hit region belongs on its box, and
            // two of them would fire twice for one press.
            if (ve.name is { Length: > 0 } named && ctx.Built.NamedGroups.Contains(named))
                ctx.Body.Append(" id=").Append(named);
            ctx.Body.Append(" {\n");
            groups++;
        }
        if (css.TryGetValue("transform", out var tcss) && StyleApplier.NeedsMatrix(tcss) && Matrix(tcss, css, x, y, w, h) is { } m)
        {
            // skew(), matrix(), 3D: the whole list composed into one matrix about the origin (vector requirement 13)
            ctx.Body.Append(indent).Append("G m=[").AppendNum(m[0]).Append(',').AppendNum(m[1]).Append(',').AppendNum(m[2]).Append(',').AppendNum(m[3]).Append(',').AppendNum(m[4]).Append(',').AppendNum(m[5]).Append("] {\n");
            groups++;
        }
        if (css.TryGetValue("filter", out var fcss) && Filters(ctx, fcss, out var filterAttrs, out var filterShadow) && filterAttrs.Length > 0)
        {
            ctx.Body.Append(indent).Append('G').Append(filterAttrs).Append(" {\n");  // colour filters on the subtree (vector requirement 12)
            groups++;
        }
        else filterShadow = css.TryGetValue("filter", out var fcss2) && Filters(ctx, fcss2, out _, out var fs2) ? fs2 : string.Empty;
        // An always-on group at identity must not turn a rectangular clip into a polygon one.
        var clipXform = xform is { IsIdentity: false } ? xform : null;
        if (css.TryGetValue("clip-path", out var cpath) || css.TryGetValue("-webkit-clip-path", out cpath))
        {
            if (ClipPath(ctx, cpath, x, y, w, h, clipXform, css.TryGetValue("clip-rule", out var clipRule) && clipRule.Trim() == "evenodd") is { } clipId)
            {
                ctx.Body.Append(indent).Append("G clip=").Append(clipId).Append(" {\n");
                groups++;
            }
            else if (cpath.Trim() is not ("none" or ""))
                // Failing to read a clip means the element draws UNCLIPPED - everything, not
                // nothing - so silence here looks like a layout bug rather than a missing feature.
                Warn(ctx, $"html: clip-path: {cpath.Trim()} is not drawn; the element is not clipped. inset(), rect(), xywh(), circle(), ellipse() and polygon() are.");
        }
        if ((css.TryGetValue("mask-image", out var mcss) || css.TryGetValue("-webkit-mask-image", out mcss) || css.TryGetValue("mask", out mcss)) && MaskDef(ctx, mcss, x, y, w, h, css, rs) is { } maskId)
        {
            ctx.Body.Append(indent).Append("G mask=@").Append(maskId).Append(" {\n");  // gradient mask on the subtree (vector requirement 10)
            groups++;
        }
        if (rs.opacity < 0.999f) Unsettled(ve).Faded = true;
        // Opacity is the other wrapper a value can delete: fully opaque and the G disappears, which
        // is a different scene, not a different number. Emitted whenever it can still change.
        if (rs.opacity < 0.999f || (tw != null && tw.From.Opacity < 0.999f) || CanFade(ctx, ve))
        {
            ctx.Body.Append(indent).Append("G o=").Append(tw != null ? tw.Lerp(tw.From.Opacity, rs.opacity) : F(rs.opacity)).Append(" {\n");
            groups++;
        }
        if (ctx.Built.TimeAnimations.TryGetValue(ve, out var ta) && TimeTimeline(ctx, ve, ta.spec, ta.start, x, y, w, h) is { } timeG)
        {
            ctx.Body.Append(indent).Append(timeG).Append(" {\n");  // a looping animation the scene runs by itself
            groups++;
        }
        if (!float.IsNaN(ctx.ScrollTop) && css.TryGetValue("animation-timeline", out var tlc) && tlc.Trim() != "auto" && ScrollTimeline(ctx, ve, css, tlc, x, y, w, h) is { } tlg)
        {
            ctx.Body.Append(indent).Append(tlg).Append(" {\n");  // keyframes as expressions over the scroll offset
            groups++;
        }
        // Background and border of the box itself.
        var clipText = (css.TryGetValue("background-clip", out var bclip) || css.TryGetValue("-webkit-background-clip", out bclip)) && bclip.Trim() == "text";
        var cornerPath = CornerPath(css, rs, x, y, w, h);
        // a box that shrinks to nothing (a bar at 0 %) keeps its node, so the structure does not change with the value
        if (w >= 0f && h >= 0f && !clipText)
        {
            var bg = rs.backgroundColor;
            css.TryGetValue("background", out var bgCss);
            // The record keeps the shorthand and the longhand separately, so `.p{background:#22aa44}`
            // plus `#p{background-image:linear-gradient(...)}` left the gradient unread: the
            // shorthand was found first and it names a colour. A longhand that actually carries a
            // picture is the one the cascade means, whichever rule set it.
            if (css.TryGetValue("background-image", out var bgImage)
                && (Gradient(bgImage.TrimStart()) || UrlOf(bgImage) != null))
                bgCss = bgImage;
            if (bgCss == null) bgCss = bgImage;
            if (cornerPath != null && tw == null && bg.a > 0.002f && (bgCss == null || !bgCss.Contains("gradient(")) && UrlOf(bgCss ?? string.Empty) == null)
            {
                // corner-shape: the box outline as a path with bevelled, scooped or notched corners
                ctx.Body.Append(indent).Append(cornerPath).Append(" f=").AppendHex(bg).Append(shadowOf(css, filterShadow)).AppendNodeId(ctx, ve).Append('\n');
                ctx.Out.Nodes++;
                bg = Color.clear;
            }
            if (bgCss != null && bgCss.StartsWith("repeating-linear-gradient", StringComparison.OrdinalIgnoreCase) && Stripes(ctx, css, bgCss, ve, rs, x, y, w, h, indent))
            {
                bg = Color.clear; bgCss = null; // drawn as a repeat of stripes (marching when animated)
            }
            if (bgCss != null && bgCss.StartsWith("repeating-", StringComparison.OrdinalIgnoreCase)) bgCss = ExpandRepeating(bgCss, w, h);
            var shadow = (css.TryGetValue("box-shadow", out var shCss) ? Shadows(shCss) : string.Empty) + filterShadow;
            var imageUrl = bgCss != null ? UrlOf(bgCss) : null;
            var layered = bgCss != null && imageUrl == null && bgCss.IndexOf("gradient(", StringComparison.OrdinalIgnoreCase) >= 0 && TopLevelCommas(bgCss) > 0;
            if (layered)
            {
                // a layered background: the colour layer (last) is the base, each gradient layer sits at its own
                // position and size ("linear-gradient(...) left bottom/100% 3px no-repeat, #000" is a fill edge)
                var layers = SplitTopLevelCommas(bgCss!);
                var baseC = bg;
                var gradientLayers = new List<string>();
                foreach (var raw in layers)
                {
                    var layer = raw.Trim();
                    if (layer.IndexOf("gradient(", StringComparison.OrdinalIgnoreCase) >= 0) gradientLayers.Add(layer);
                    else if (StyleApplier.TryColor(layer, out var lc)) baseC = lc;
                }
                if (baseC.a > 0.002f)
                {
                    ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                        .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=").AppendHex(baseC).Append(shadow).AppendNodeId(ctx, ve).Append('\n');
                    ctx.Out.Nodes++;
                }
                // CSS paints the first layer on top: emitted last
                for (var li = gradientLayers.Count - 1; li >= 0; li--)
                {
                    var layer = gradientLayers[li];
                    var close = MatchingParen(layer, layer.IndexOf('('));
                    if (close < 0) continue;
                    var gradient = layer.Substring(0, close + 1).Trim();
                    LayerBox(layer.Substring(close + 1), w, h, out var lx, out var ly, out var lw, out var lh);
                    if (lw <= 0.01f || lh <= 0.01f) continue;
                    if (gradient.StartsWith("repeating-", StringComparison.OrdinalIgnoreCase)) gradient = ExpandRepeating(gradient, lw, lh);
                    if (gradient.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase))
                        GradientBox(ctx, gradient, x + lx, y + ly, lw, lh, F(lw), F(lh), rs, indent, ve, xform, string.Empty);
                    else if (gradient.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase) && RadialDef(ctx, gradient) is { } lrid)
                    {
                        ctx.Body.Append(indent).Append("R x=").AppendNum(x + lx).Append(" y=").AppendNum(y + ly).Append(" w=").AppendNum(lw).Append(" h=").AppendNum(lh)
                            .Append(" f=@").Append(lrid).Append('\n');
                        ctx.Out.Nodes++;
                    }
                }
            }
            else if (imageUrl != null)
            {
                // background-image: url(): the colour (if any) under an IMG node (vector requirement 9)
                if (bg.a > 0.002f)
                {
                    ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                        .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=").AppendHex(bg).Append(shadow).AppendNodeId(ctx, ve).Append('\n');
                    ctx.Out.Nodes++;
                }
                // background-origin: padding-box (the default, inside the border), content-box, or border-box
                var origin = css.TryGetValue("background-origin", out var bo) ? bo.Trim() : "padding-box";
                var il = origin == "border-box" ? 0f : rs.borderLeftWidth + (origin == "content-box" ? rs.paddingLeft : 0f);
                var it = origin == "border-box" ? 0f : rs.borderTopWidth + (origin == "content-box" ? rs.paddingTop : 0f);
                var ir = origin == "border-box" ? 0f : rs.borderRightWidth + (origin == "content-box" ? rs.paddingRight : 0f);
                var ib = origin == "border-box" ? 0f : rs.borderBottomWidth + (origin == "content-box" ? rs.paddingBottom : 0f);
                EmitImage(ctx, imageUrl, BackgroundFit(css), css, rs, x + il, y + it, Mathf.Max(1f, w - il - ir), Mathf.Max(1f, h - it - ib), indent, bg.a > 0.002f ? null : ve);
            }
            else if (ColourTimeline(ctx, ve) is { } ka)
            {
                // A looping background-color animation: the scene walks the ramp, so no page frame
                // is ever run for it - before this it kept a KeyframeRunner and a frame per boundary.
                ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                    .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=@").Append(ka.gid).Append(" fat==").Append(ka.at).Append(shadow).AppendNodeId(ctx, ve).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (tw != null && !Tweens.Snap.NearColour(tw.From.Bg, bg) && (tw.From.Bg.a > 0.002f || bg.a > 0.002f) && !(bgCss != null && bgCss.Contains("gradient(")))
            {
                // A colour transition: a two-stop ramp sampled over the tween's clock (vector requirement 1)
                var gid = ctx.NextId("tw");
                ctx.Defs.Append("  GL id=").Append(gid).Append(" stops=[[0,").AppendHex(tw.From.Bg).Append("],[1,").AppendHex(bg).Append("]]\n");
                ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                    .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=@").Append(gid).Append(" fat==").Append(tw.P).Append(shadow).AppendNodeId(ctx, ve).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (bgCss != null && Gradient(bgCss)
                     && BackgroundTiles(ctx, css, rs, w, h, out var blx, out var bly, out var blw, out var blh, out var bnx, out var bny))
            {
                // background-size / -position / -origin / -repeat on a single gradient layer: the
                // colour fills the box, the gradient sits at its own size and tiles from its anchor
                if (bg.a > 0.002f || shadow.Length > 0)
                {
                    ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                        .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=").AppendHex(bg.a > 0.002f ? bg : new Color(0f, 0f, 0f, 1f / 255f)).Append(shadow).AppendNodeId(ctx, ve).Append('\n');
                    ctx.Out.Nodes++;
                }
                var tileDef = bgCss.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase) ? RadialDef(ctx, bgCss)
                    : bgCss.StartsWith("conic-gradient", StringComparison.OrdinalIgnoreCase) ? ConicDef(ctx, bgCss)
                    : null;
                for (var ty = 0; ty < bny; ty++)
                    for (var tx = 0; tx < bnx; tx++)
                    {
                        var gx = x + blx + tx * blw;
                        var gy = y + bly + ty * blh;
                        if (tileDef != null)
                        {
                            ctx.Body.Append(indent).Append("R x=").AppendNum(gx).Append(" y=").AppendNum(gy).Append(" w=").AppendNum(blw).Append(" h=").AppendNum(blh)
                                .Append(" f=@").Append(tileDef).Append('\n');
                            ctx.Out.Nodes++;
                        }
                        else if (bgCss.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase))
                            GradientBox(ctx, bgCss, gx, gy, blw, blh, F(blw), F(blh), rs, indent, ve, xform, string.Empty);
                    }
            }
            else if (bgCss != null && bgCss.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase))
            {
                GradientBox(ctx, bgCss, x, y, w, h, ws ?? F(w), hs ?? F(h), rs, indent, ve, xform, shadow);
            }
            else if (bgCss != null && bgCss.StartsWith("conic-gradient", StringComparison.OrdinalIgnoreCase) && ConicDef(ctx, bgCss) is { } cid)
            {
                ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                    .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=@").Append(cid).Append(shadow).AppendNodeId(ctx, ve).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (bgCss != null && bgCss.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase) && RadialDef(ctx, bgCss) is { } rid)
            {
                ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                    .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=@").Append(rid).Append(shadow).AppendNodeId(ctx, ve).Append('\n');
                ctx.Out.Nodes++;
            }
            // a declared background keeps its box when it turns transparent (a lamp going dark), so the
            // structure does not change with the colour; the vector mod skips invisible shapes itself
            else if (bg.a > 0.002f || css.ContainsKey("background-color") || (css.TryGetValue("background", out var bgDecl) && StyleApplier.TryColor(bgDecl.Trim(), out _)))
            {
                ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                    .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=").AppendHex(bg).Append(shadow).AppendNodeId(ctx, ve).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (IsButton(ctx, ve))
            {
                ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                    .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=#00000001").AppendNodeId(ctx, ve).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (shadow.Length > 0)
            {
                // A shadow under a transparent box still casts: an invisible fill carries it.
                ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendVal(ws, w).Append(" h=").AppendVal(hs, h)
                    .AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" f=#00000001").Append(shadow).Append('\n');
                ctx.Out.Nodes++;
            }

            if (Outline(css, out var ow, out var oc, out var ooff, out var ostyle) && ow > 0.01f && oc.a > 0.002f)
            {
                // Outside the border box, offset by outline-offset, stroke centred on its path.
                var od = ooff + ow * 0.5f;
                ctx.Body.Append(indent).Append("R x=").AppendNum(x - od).Append(" y=").AppendNum(y - od)
                    .Append(" w=").AppendNum(w + 2f * od).Append(" h=").AppendNum(h + 2f * od)
                    .AppendRadius(rs, w + 2f * od, h + 2f * od, od).Append(" f=none s=").AppendHex(oc).Append(" sw=").AppendNum(ow)
                    .Append(DashFor(ostyle, ow)).Append('\n');
                ctx.Out.Nodes++;
            }

            var bw = rs.borderTopWidth;
            var sameWidth = Mathf.Approximately(bw, rs.borderRightWidth) && Mathf.Approximately(bw, rs.borderBottomWidth) && Mathf.Approximately(bw, rs.borderLeftWidth);
            var sameColour = rs.borderTopColor == rs.borderRightColor && rs.borderTopColor == rs.borderBottomColor && rs.borderTopColor == rs.borderLeftColor;
            var rounded = rs.borderTopLeftRadius > 0.01f || rs.borderTopRightRadius > 0.01f || rs.borderBottomRightRadius > 0.01f || rs.borderBottomLeftRadius > 0.01f;
            var styles = SideStyles(css);
            var bstyle = styles[0];
            var mixed = styles[0] != styles[1] || styles[0] != styles[2] || styles[0] != styles[3];
            if (BorderImage(ctx, css, rs, x, y, w, h, indent))
            {
                // border-image replaces the border: a gradient stroke, or nine image slices
            }
            else if (!mixed && bstyle is "none" or "hidden")
            {
                // border-style: none with a width: no border, as CSS computes it
            }
            else if (cornerPath != null && tw == null && bw > 0.01f && sameWidth && sameColour && bstyle == "solid")
            {
                ctx.Body.Append(indent).Append(CornerPath(css, rs, x + bw * 0.5f, y + bw * 0.5f, w - bw, h - bw)).Append(" f=none s=").AppendHex(rs.borderTopColor).Append(" sw=").AppendNum(bw).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (mixed && rounded && RoundedSides(styles, rs, out var ringWidth, out var ringColours))
            {
                // Some sides without a border (a dome: border-bottom: none), the rest one solid width:
                // the drawn sides follow the rounded corners, as a browser draws them.
                SideArcs(ctx, indent, x, y, w, h, ringWidth, rs, ringColours);
            }
            else if (mixed && (bw > 0.01f || rs.borderRightWidth > 0.01f || rs.borderBottomWidth > 0.01f || rs.borderLeftWidth > 0.01f))
            {
                // Different styles per side: each side its own stroke, dashed or dotted as it says, none drawing nothing.
                SideLine(ctx, indent, styles[0], x, y + rs.borderTopWidth * 0.5f, x + w, y + rs.borderTopWidth * 0.5f, rs.borderTopWidth, rs.borderTopColor);
                SideLine(ctx, indent, styles[1], x + w - rs.borderRightWidth * 0.5f, y, x + w - rs.borderRightWidth * 0.5f, y + h, rs.borderRightWidth, rs.borderRightColor);
                SideLine(ctx, indent, styles[2], x, y + h - rs.borderBottomWidth * 0.5f, x + w, y + h - rs.borderBottomWidth * 0.5f, rs.borderBottomWidth, rs.borderBottomColor);
                SideLine(ctx, indent, styles[3], x + rs.borderLeftWidth * 0.5f, y, x + rs.borderLeftWidth * 0.5f, y + h, rs.borderLeftWidth, rs.borderLeftColor);
            }
            else if (bw > 0.01f && sameWidth && bstyle == "double")
            {
                // double: two strokes a third of the width each, at the outer and inner edges
                var third = bw / 3f;
                foreach (var inset in new[] { third * 0.5f, bw - third * 0.5f })
                {
                    ctx.Body.Append(indent).Append("R x=").AppendNum(x + inset).Append(" y=").AppendNum(y + inset).Append(" w=").AppendNum(w - 2f * inset).Append(" h=").AppendNum(h - 2f * inset)
                        .AppendRadius(rs, w, h, -inset).Append(" f=none s=").AppendHex(rs.borderTopColor).Append(" sw=").AppendNum(third).Append('\n');
                    ctx.Out.Nodes++;
                }
            }
            else if (bw > 0.01f && bstyle is "inset" or "outset" or "groove" or "ridge")
            {
                // 3D styles: light and dark sides. ponytail: groove = inset, ridge = outset
                var raised = bstyle is "outset" or "ridge";
                var light = Color.Lerp(rs.borderTopColor, Color.white, 0.35f);
                var dark = Color.Lerp(rs.borderTopColor, Color.black, 0.35f);
                Side(ctx, indent, x, y, w, rs.borderTopWidth, raised ? light : dark);
                Side(ctx, indent, x + w - rs.borderRightWidth, y, rs.borderRightWidth, h, raised ? dark : light);
                Side(ctx, indent, x, y + h - rs.borderBottomWidth, w, rs.borderBottomWidth, raised ? dark : light);
                Side(ctx, indent, x, y, rs.borderLeftWidth, h, raised ? light : dark);
            }
            else if (bw > 0.01f && sameWidth && !sameColour && rounded)
            {
                // A ring with a differently coloured side (the classic spinner): one stroked
                // arc path per side, each running from the middle of one corner arc to the
                // middle of the next, so the sides meet cleanly with butt caps.
                SideArcs(ctx, indent, x, y, w, h, bw, rs, new[] { rs.borderTopColor, rs.borderRightColor, rs.borderBottomColor, rs.borderLeftColor });
            }
            else if (bw > 0.01f && rs.borderTopColor.a > 0.002f && sameWidth && sameColour)
            {
                // A stroke is centred on its path: inset by half the width so it stays inside the box.
                var half = bw * 0.5f;
                ctx.Body.Append(indent).Append("R x=").AppendNum(x + half).Append(" y=").AppendNum(y + half)
                    .Append(" w=").AppendVal(tw != null ? tw.Lerp(tw.From.Rect.width - bw, w - bw) : null, w - bw)
                    .Append(" h=").AppendVal(tw != null ? tw.Lerp(tw.From.Rect.height - bw, h - bw) : null, h - bw)
                    .AppendRadius(rs, w, h, -half).Append(" f=none s=").AppendHex(rs.borderTopColor).Append(" sw=").AppendNum(bw).Append(Dash(css, bw)).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (bw > 0.01f || rs.borderRightWidth > 0.01f || rs.borderBottomWidth > 0.01f || rs.borderLeftWidth > 0.01f)
            {
                // Different widths or colours per side: four thin rects.
                Side(ctx, indent, x, y, w, rs.borderTopWidth, rs.borderTopColor);
                Side(ctx, indent, x + w - rs.borderRightWidth, y, rs.borderRightWidth, h, rs.borderRightColor);
                Side(ctx, indent, x, y + h - rs.borderBottomWidth, w, rs.borderBottomWidth, rs.borderBottomColor);
                Side(ctx, indent, x, y, rs.borderLeftWidth, h, rs.borderLeftColor);
            }
        }

        // overflow clips the content, not the box: the element's own background, border and shadow
        // are drawn above, outside its clip (a shadow clipped by its own box was invisible and costly)
        if (rs.overflow == Overflow.Hidden && w > 0f && h > 0f)
        {
            if (Scrolls(css))
            {
                // overflow: auto / scroll: the vector mod's scroll container. It clips to the
                // box and slides its children by a client-side offset (wheel or drag), so a
                // scroll costs one rebuild and no tick. Children stay in page coordinates.
                var ch = 0f;
                // one snapshot lookup per child, not three (Children() returns the List itself, so
                // the foreach here never boxed an enumerator - the repeated Of() was the only cost)
                for (var ci = 0; ci < ve.childCount; ci++)
                {
                    var cb = OffThread.Of(ve[ci]);
                    if (cb.display == DisplayStyle.None) continue;
                    var cl = cb.layout;
                    if (float.IsNaN(cl.yMax)) continue;
                    ch = Mathf.Max(ch, cl.yMax + cb.marginBottom);
                }
                ch += rs.paddingBottom;
                ctx.Body.Append(indent).Append("SC id=").Append(string.IsNullOrEmpty(ve.name) ? ctx.NextId("scroll") : ve.name)
                    .Append(" x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendNum(w).Append(" h=").AppendNum(h)
                    .Append(" ch=").AppendNum(Mathf.Max(ch, h)).AppendRadius(rs, w, h, 0f, Keeps(ctx, ve));
                if (ctx.ScrollSet != null && !string.IsNullOrEmpty(ve.name) && ctx.ScrollSet.TryGetValue(ve.name, out var ss))
                    ctx.Body.Append(" so=").AppendNum(ss.offset).Append(" sov=").Append(ss.version); // applied once per version (vector requirement 7)
                ctx.Body.Append(" {\n");
                ctx.Out.Nodes++;
                groups++;
                scrollTop = y;
                scrollCh = Mathf.Max(ch, h);
            }
            else
            {
                var id = ctx.NextId("clip");
                var margin = css.TryGetValue("overflow-clip-margin", out var ocm) ? StyleApplier.Num(ocm) : 0f;  // the clip box grown by overflow-clip-margin
                ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").AppendNum(x - margin).Append(" y=").AppendNum(y - margin)
                    .Append(" w=").AppendNum(w + 2f * margin).Append(" h=").AppendNum(h + 2f * margin).AppendRadius(rs, w + 2f * margin, h + 2f * margin).Append(" }\n");
                ctx.Body.Append(indent).Append("G clip=").Append(id).Append(" {\n");
                groups++;
            }
        }

        switch (ve)
        {
            case VisualElement when external != null:
                break; // the control draws the content
            case not Label when ctx.Built.NodeOf.TryGetValue(ve, out var cnode) && cnode.Attr("data-control") is { } control:
                EmitCheck(ctx, control, cnode, css, rs, x, y, w, h, indent);
                break;
            case not Label when ctx.Built.NodeOf.TryGetValue(ve, out var inode) && inode.Tag == "img":
            {
                var src = inode.Attr("src") ?? FirstOfSrcset(inode.Attr("srcset"));
                if (src != null)
                    EmitImage(ctx, src, css.TryGetValue("object-fit", out var of) ? of.Trim() : "fill", css, rs, x, y, w, h, indent, ve);
                break;
            }
            case Label when ctx.Built.NodeOf.TryGetValue(ve, out var mnode) && mnode.Attr("data-marker") is { } markerShape:
                EmitMarker(ctx, markerShape, rs.color, x, y, w, h, indent);
                break;
            case Label label when css.TryGetValue("writing-mode", out var wm) && wm.Trim().StartsWith("vertical", StringComparison.OrdinalIgnoreCase) || (css.TryGetValue("writing-mode", out wm) && wm.Trim().StartsWith("sideways", StringComparison.OrdinalIgnoreCase)):
            {
                // vertical text: the label rotated about the box centre, its box swapped
                var cx = x + w * 0.5f; var cy = y + h * 0.5f;
                var angle = string.Equals(wm.Trim(), "sideways-lr", StringComparison.OrdinalIgnoreCase) ? -90f : 90f;
                ctx.Body.Append(indent).Append("G a=[").AppendNum(cx).Append(',').AppendNum(cy).Append("] r=").AppendNum(angle).Append(" {\n");
                EmitText(ctx, label, css, cx - h * 0.5f, cy - w * 0.5f, h, w, Indent(depth + 1));
                ctx.Body.Append(indent).Append("}\n");
                break;
            }
            case Label label:
                EmitText(ctx, label, css, x, y, w, h, indent);
                break;
            case SvgElement svg:
                Watch(ctx, svg);
                EmitSvg(ctx, svg, x, y, w, h, indent);
                break;
            case CanvasElement cv:
                Watch(ctx, cv);
                EmitCanvas(ctx, cv, css, x, y, w, h, indent);
                break;
            default:
            {
                var outer = (ctx.ScrollTop, ctx.ScrollH, ctx.ScrollRange);
                if (!float.IsNaN(scrollTop)) { ctx.ScrollTop = scrollTop; ctx.ScrollH = h; ctx.ScrollRange = Mathf.Max(0f, scrollCh - h); }
                var children = ByZIndex(ctx, ve);
                foreach (var child in children)
                    EmitElement(ctx, child, new Vector2(x, y), depth + groups);
                ctx.Return(children);
                if (!float.IsNaN(scrollTop)) EmitScrollbar(ctx, ve, css, x, y, w, h, scrollCh, Indent(depth + 1));
                (ctx.ScrollTop, ctx.ScrollH, ctx.ScrollRange) = outer;
                break;
            }
        }

        for (var i = 0; i < groups; i++)
            ctx.Body.Append(indent).Append("}\n");
        ctx.Leave(outerIds);

        // Keep what this subtree wrote, to write again unchanged next frame. A subtree that produced
        // an external element or put something in the top layer is not kept: those are side effects
        // beside the text, and replaying the characters alone would lose them. Nor is one that is
        // tweening, whose numbers are expressions over a start time. An svg's shapes and a canvas's
        // commands are covered by ContentSig / Invalidate above, not by the box comparison.
        rs.SubtreeChanged = false;
        rs.Changed = false;
        rs.CacheUsable = ctx.Out.Externals.Count == externalsFrom && ctx.Deferred.Count == deferredFrom
                         && tw == null;
        if (!rs.CacheUsable)
            return;
        rs.CacheBodyLength = Keep(ctx.Body, bodyFrom, ref rs.CacheBody);
        rs.CacheDefsLength = Keep(ctx.Defs, defsFrom, ref rs.CacheDefs);
        rs.CacheNodes = ctx.Out.Nodes - nodesFrom;
        rs.CacheParentPos = parentPos;
        rs.CacheDepth = depth;
        rs.CacheEpoch = ctx.Epoch;
    }

    /// <summary>Copies what was appended since <paramref name="from"/> into a buffer the element keeps.</summary>
    private static int Keep(StringBuilder sb, int from, ref char[]? into)
    {
        var length = sb.Length - from;
        if (length <= 0) return 0;
        // Double rather than size exactly, as Output.Take does: the root's buffer is the whole
        // scene, so a page whose text grows one character ("9" -> "10") reallocated ~20 KB and
        // one buffer per ancestor with it.
        if (into == null || into.Length < length) into = new char[System.Math.Max(length, (into?.Length ?? 32) * 2)];
        sb.CopyTo(from, into, 0, length);
        return length;
    }

    /// <summary>Every side with a border is solid and of one width; the others draw nothing (their colour is clear).</summary>
    private static bool RoundedSides(string[] styles, OffThread.Box rs, out float width, out Color[] colours)
    {
        var widths = new[] { rs.borderTopWidth, rs.borderRightWidth, rs.borderBottomWidth, rs.borderLeftWidth };
        colours = new[] { rs.borderTopColor, rs.borderRightColor, rs.borderBottomColor, rs.borderLeftColor };
        width = 0f;
        for (var i = 0; i < 4; i++)
        {
            if (styles[i] is "none" or "hidden" || widths[i] <= 0.01f) { colours[i] = Color.clear; continue; }
            if (styles[i] != "solid" || (width > 0f && !Mathf.Approximately(width, widths[i]))) return false;
            width = widths[i];
        }
        return width > 0f;
    }

    private static void SideArcs(Ctx ctx, string indent, float x, float y, float w, float h, float bw, OffThread.Box rs, Color[] colours)
    {
        // Stroke path inset by half the width; radii shrink by the same amount.
        var half = bw * 0.5f;
        x += half; y += half; w -= bw; h -= bw;
        var f = 1f;
        float tl = rs.borderTopLeftRadius, tr = rs.borderTopRightRadius, br = rs.borderBottomRightRadius, bl = rs.borderBottomLeftRadius;
        var ow = w + bw; var oh = h + bw;
        if (tl + tr > ow && tl + tr > 0f) f = Mathf.Min(f, ow / (tl + tr));
        if (bl + br > ow && bl + br > 0f) f = Mathf.Min(f, ow / (bl + br));
        if (tl + bl > oh && tl + bl > 0f) f = Mathf.Min(f, oh / (tl + bl));
        if (tr + br > oh && tr + br > 0f) f = Mathf.Min(f, oh / (tr + br));
        tl = Mathf.Max(0f, tl * f - half); tr = Mathf.Max(0f, tr * f - half); br = Mathf.Max(0f, br * f - half); bl = Mathf.Max(0f, bl * f - half);
        const float k = 0.70710678f; // cos 45

        // Corner centres and the 45-degree point of each arc, clockwise from top-left.
        Vector2 ctl = new(x + tl, y + tl), ctr = new(x + w - tr, y + tr), cbr = new(x + w - br, y + h - br), cbl = new(x + bl, y + h - bl);
        Vector2 mtl = ctl + new Vector2(-k * tl, -k * tl), mtr = ctr + new Vector2(k * tr, -k * tr), mbr = cbr + new Vector2(k * br, k * br), mbl = cbl + new Vector2(-k * bl, k * bl);

        // Each side: arc out of the previous corner, straight run, arc into the next corner.
        Arc(ctx, indent, bw, colours[0], mtl, tl, new Vector2(x + tl, y), new Vector2(x + w - tr, y), tr, mtr);
        Arc(ctx, indent, bw, colours[1], mtr, tr, new Vector2(x + w, y + tr), new Vector2(x + w, y + h - br), br, mbr);
        Arc(ctx, indent, bw, colours[2], mbr, br, new Vector2(x + w - br, y + h), new Vector2(x + bl, y + h), bl, mbl);
        Arc(ctx, indent, bw, colours[3], mbl, bl, new Vector2(x, y + h - bl), new Vector2(x, y + tl), tl, mtl);
    }

    private static void Arc(Ctx ctx, string indent, float bw, Color c, Vector2 from, float r0, Vector2 a, Vector2 b, float r1, Vector2 to)
    {
        if (c.a <= 0.002f) return;
        var d = new StringBuilder("M ").AppendNum(from.x).Append(' ').AppendNum(from.y);
        if (r0 > 0.01f) d.Append(" A ").AppendNum(r0).Append(' ').AppendNum(r0).Append(" 0 0 1 ").AppendNum(a.x).Append(' ').AppendNum(a.y);
        d.Append(" L ").AppendNum(b.x).Append(' ').AppendNum(b.y);
        if (r1 > 0.01f) d.Append(" A ").AppendNum(r1).Append(' ').AppendNum(r1).Append(" 0 0 1 ").AppendNum(to.x).Append(' ').AppendNum(to.y);
        ctx.Body.Append(indent).Append("P d=\"").Append(d).Append("\" f=none s=").AppendHex(c).Append(" sw=").AppendNum(bw).Append(" cap=butt\n");
        ctx.Out.Nodes++;
    }

    /// <summary>Children in paint order: z-index ascending, document order within a value.</summary>
    private static List<VisualElement> ByZIndex(Ctx ctx, VisualElement ve)
    {
        var list = ctx.RentSorting();
        var n = ve.childCount;
        for (var i = 0; i < n; i++)
        {
            var child = ve[i];
            var z = 0;
            if (ctx.Built.CssOf(child).TryGetValue("z-index", out var zs))
                int.TryParse(zs.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z);
            list.Add((z, i, child));
        }
        list.Sort((a, b) => a.z != b.z ? a.z.CompareTo(b.z) : a.i.CompareTo(b.i));
        var result = ctx.RentChildren();
        for (var i = 0; i < list.Count; i++) result.Add(list[i].c);
        ctx.Return(list);
        return result;
    }

    private static void Side(Ctx ctx, string indent, float x, float y, float w, float h, Color c)
    {
        if (w <= 0.01f || h <= 0.01f || c.a <= 0.002f)
            return;
        ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendNum(w).Append(" h=").AppendNum(h).Append(" f=").AppendHex(c).Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>
    /// The G that places an element on its offset-path: path("...") in the containing
    /// block's coordinates, sampled at offset-distance (px or % of the length), turned by
    /// offset-rotate (auto = along the tangent, an angle, or auto plus an angle).
    /// ponytail: ray()/shapes as offset-path and offset-anchor are not read; arcs flatten to a chord.
    /// </summary>
    /// <summary>Where an offset-path puts the element: a translation and turn, and when its
    /// offset-distance is tweening, the same as expressions over the tween's progress.</summary>
    internal sealed class OffsetPlace
    {
        public float Dx, Dy, Rot;
        public string? Ex, Ey, Er;
        /// <summary>The anchor point on the box that sits on the path (offset-anchor), in page coordinates.</summary>
        public float Ax, Ay;
    }

    /// <summary>
    /// offset-path: ray(angle [size] [contain]): a straight line from offset-position (the
    /// box's anchor when auto, the containing block's centre for normal) at the angle (0deg
    /// up, clockwise), as long as the size keyword says. Points in the containing block's space.
    /// </summary>
    private static (List<Vector2> pts, float total)? RayPath(Dictionary<string, string> css, string pathCss, VisualElement ve, float x, float y, float ax, float ay)
    {
        var open = pathCss.IndexOf("ray(", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var close = pathCss.LastIndexOf(')');
        var parts = pathCss.Substring(open + 4, Math.Max(0, close - open - 4)).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var angle = Degrees(parts[0]);
        var size = "closest-side";
        foreach (var p in parts) if (p is "closest-side" or "farthest-side" or "closest-corner" or "farthest-corner" or "sides") size = p;
        var parent = ve.parent;
        var pw = parent != null && !float.IsNaN(OffThread.Of(parent).layout.width) ? OffThread.Of(parent).layout.width : StyleApplier.ViewportW;
        var ph = parent != null && !float.IsNaN(OffThread.Of(parent).layout.height) ? OffThread.Of(parent).layout.height : StyleApplier.ViewportH;
        // the start, in the containing block's space: the anchor's own place (auto), the centre (normal), or a position
        var start = new Vector2(OffThread.Of(ve).layout.x + (ax - x), OffThread.Of(ve).layout.y + (ay - y));
        if (css.TryGetValue("offset-position", out var op))
        {
            var o = op.Trim().ToLowerInvariant();
            if (o == "normal") start = new Vector2(pw * 0.5f, ph * 0.5f);
            else if (o != "auto")
            {
                var pp = o.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                float Pos(string p, float size1) => p switch { "left" or "top" => 0f, "center" => size1 * 0.5f, "right" or "bottom" => size1, _ => p.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(p) / 100f * size1 : StyleApplier.Num(p) };
                start = new Vector2(pp.Length > 0 ? Pos(pp[0], pw) : pw * 0.5f, pp.Length > 1 ? Pos(pp[1], ph) : ph * 0.5f);
            }
        }
        var dir = new Vector2(Mathf.Sin(angle * Mathf.Deg2Rad), -Mathf.Cos(angle * Mathf.Deg2Rad));
        var dl = start.x; var dr = pw - start.x; var dt = start.y; var db = ph - start.y;
        float len;
        switch (size)
        {
            case "farthest-side": len = Mathf.Max(Mathf.Max(dl, dr), Mathf.Max(dt, db)); break;
            case "closest-corner": len = Mathf.Sqrt(Mathf.Min(dl, dr) * Mathf.Min(dl, dr) + Mathf.Min(dt, db) * Mathf.Min(dt, db)); break;
            case "farthest-corner": len = Mathf.Sqrt(Mathf.Max(dl, dr) * Mathf.Max(dl, dr) + Mathf.Max(dt, db) * Mathf.Max(dt, db)); break;
            case "sides":
            {
                // where the ray leaves the containing block
                len = float.MaxValue;
                if (dir.x > 0.0001f) len = Mathf.Min(len, dr / dir.x); else if (dir.x < -0.0001f) len = Mathf.Min(len, dl / -dir.x);
                if (dir.y > 0.0001f) len = Mathf.Min(len, db / dir.y); else if (dir.y < -0.0001f) len = Mathf.Min(len, dt / -dir.y);
                if (len == float.MaxValue) len = 0f;
                break;
            }
            default: len = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(dt, db)); break;
        }
        len = Mathf.Max(0.001f, len);
        return (new List<Vector2> { start, start + dir * len }, len);
    }

    private static readonly Dictionary<string, (List<Vector2> pts, float total)> PathCache = new(StringComparer.Ordinal);

    /// <summary>offset-path: path("...") flattened, with its length; null for anything else.</summary>
    internal static (List<Vector2> pts, float total)? Path(string pathCss)
    {
        lock (PathCache)
            if (PathCache.TryGetValue(pathCss, out var cached)) return cached;
        var open = pathCss.IndexOf("path(", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var close = pathCss.LastIndexOf(')');
        if (close <= open) return null;
        var pts = FlattenPath(pathCss.Substring(open + 5, close - open - 5).Trim().Trim('"', '\''));
        if (pts.Count < 2) return null;
        var total = 0f;
        for (var i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
        lock (PathCache)
        {
            if (PathCache.Count > 256) PathCache.Clear();
            PathCache[pathCss] = (pts, total);
        }
        return (pts, total);
    }

    internal static float PathLength(string pathCss) => Path(pathCss)?.total ?? 0f;

    /// <summary>The point at a distance along the polyline and the tangent's angle there, in degrees.</summary>
    private static (Vector2 p, float along) Sample(List<Vector2> pts, float dist)
    {
        var p = pts[0];
        var tangent = pts[1] - pts[0];
        var run = 0f;
        for (var i = 1; i < pts.Count; i++)
        {
            var seg = Vector2.Distance(pts[i - 1], pts[i]);
            if (seg <= 0f) continue;
            if (run + seg >= dist || i == pts.Count - 1)
            {
                p = Vector2.Lerp(pts[i - 1], pts[i], Mathf.Clamp01((dist - run) / seg));
                tangent = pts[i] - pts[i - 1];
                break;
            }
            run += seg;
        }
        return (p, Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);
    }

    private static OffsetPlace? Offset(Dictionary<string, string> css, string pathCss, VisualElement ve, float x, float y, float w, float h, Tweens.Tween? tw)
    {
        // offset-anchor: which point of the box rides the path (auto: the centre)
        var ax = x + w * 0.5f; var ay = y + h * 0.5f;
        if (css.TryGetValue("offset-anchor", out var oa) && !string.Equals(oa.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            var pp = oa.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            float Pos(string p, float size) => p switch { "left" or "top" => 0f, "center" => size * 0.5f, "right" or "bottom" => size, _ => p.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(p) / 100f * size : StyleApplier.Num(p) };
            ax = x + (pp.Length > 0 ? Pos(pp[0], w) : w * 0.5f);
            ay = y + (pp.Length > 1 ? Pos(pp[1], h) : h * 0.5f);
        }
        var pathData = Path(pathCss) ?? RayPath(css, pathCss, ve, x, y, ax, ay);
        if (pathData is not { } path) return null;
        var (pts, total) = path;
        var dist = css.TryGetValue("offset-distance", out var od) ? Tweens.Snap.OffsetPx(od, pathCss) : 0f;
        // offset-rotate: auto (along the tangent), reverse, an angle, or auto plus an angle
        var autoTurn = 1f; var extra = 0f; var anyRotate = false;
        if (css.TryGetValue("offset-rotate", out var orr))
        {
            autoTurn = 0f;
            foreach (var part in orr.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "auto") autoTurn = 1f;
                else if (part == "reverse") { autoTurn = 1f; extra += 180f; }
                else extra += Degrees(part);
                anyRotate = true;
            }
        }
        // the path is in the containing block's space: the parent's origin in page coordinates, less the anchor
        var ox = x - OffThread.Of(ve).layout.x - ax;
        var oy = y - OffThread.Of(ve).layout.y - ay;
        var end = Sample(pts, Mathf.Clamp(dist, 0f, total));
        var place = new OffsetPlace { Dx = ox + end.p.x, Dy = oy + end.p.y, Rot = autoTurn * end.along + extra, Ax = ax, Ay = ay };
        if (tw == null || Mathf.Abs(tw.From.Offset - tw.To.Offset) < 0.01f || tw.P.Length == 0) return place;

        // A tween on the distance: the path between the two distances sampled K times and the
        // position written as a piecewise-linear function of the eased progress P:
        //   x(P) = x0 + sum_k (x[k+1]-x[k]) * clamp(P*K - k, 0, 1)
        // The angle is unwrapped so the turn never jumps through 360.
        const int K = 16;
        var sx = new StringBuilder(); var sy = new StringBuilder(); var sr = new StringBuilder();
        var prev = Sample(pts, Mathf.Clamp(tw.From.Offset, 0f, total));
        var prevAngle = prev.along;
        sx.Append('=').AppendNum(ox + prev.p.x); sy.Append('=').AppendNum(oy + prev.p.y); sr.Append('=').AppendNum(autoTurn * prevAngle + extra);
        for (var k = 0; k < K; k++)
        {
            var dk = Mathf.Lerp(tw.From.Offset, tw.To.Offset, (k + 1f) / K);
            var cur = Sample(pts, Mathf.Clamp(dk, 0f, total));
            var angle = cur.along;
            while (angle - prevAngle > 180f) angle -= 360f;
            while (angle - prevAngle < -180f) angle += 360f;
            var step = "clamp(" + tw.P + "*" + K + "-" + k + ",0,1)";
            if (Mathf.Abs(cur.p.x - prev.p.x) > 0.01f) sx.Append("+(").AppendNum(cur.p.x - prev.p.x).Append(")*").Append(step);
            if (Mathf.Abs(cur.p.y - prev.p.y) > 0.01f) sy.Append("+(").AppendNum(cur.p.y - prev.p.y).Append(")*").Append(step);
            if (autoTurn != 0f && Mathf.Abs(angle - prevAngle) > 0.01f) sr.Append("+(").AppendNum(angle - prevAngle).Append(")*").Append(step);
            prev = cur; prevAngle = angle;
        }
        place.Ex = sx.ToString(); place.Ey = sy.ToString();
        place.Er = autoTurn != 0f || !anyRotate ? sr.ToString() : null;
        return place;
    }

    private static float Degrees(string v)
    {
        if (v.EndsWith("turn", StringComparison.Ordinal)) return StyleApplier.Num(v.Substring(0, v.Length - 4)) * 360f;
        if (v.EndsWith("rad", StringComparison.Ordinal)) return StyleApplier.Num(v.Substring(0, v.Length - 3)) * Mathf.Rad2Deg;
        if (v.EndsWith("grad", StringComparison.Ordinal)) return StyleApplier.Num(v.Substring(0, v.Length - 4)) * 0.9f;
        return StyleApplier.Num(v.EndsWith("deg", StringComparison.Ordinal) ? v.Substring(0, v.Length - 3) : v);
    }

    /// <summary>SVG path data as a polyline: M L H V C S Q T Z (absolute and relative); A as its chord.</summary>
    internal static List<Vector2> FlattenPath(string d)
    {
        var pts = new List<Vector2>();
        var nums = new List<float>();
        var cmd = ' ';
        var cur = Vector2.zero; var start = Vector2.zero; var lastCtrl = Vector2.zero;
        var i = 0;
        void Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
        {
            for (var k = 1; k <= 12; k++)
            {
                var u = k / 12f; var m = 1f - u;
                pts.Add(m * m * m * p0 + 3f * m * m * u * p1 + 3f * m * u * u * p2 + u * u * u * p3);
            }
        }
        void Run()
        {
            if (cmd == ' ') return;
            var rel = char.IsLower(cmd);
            var c = char.ToUpperInvariant(cmd);
            var n = 0;
            Vector2 R(int k) => rel ? cur + new Vector2(nums[k], nums[k + 1]) : new Vector2(nums[k], nums[k + 1]);
            if (c == 'Z') { if (pts.Count > 0) pts.Add(start); cur = start; return; }
            while (true)
            {
                switch (c)
                {
                    case 'M': if (n + 2 > nums.Count) return; cur = R(n); start = cur; pts.Add(cur); n += 2; c = 'L'; break;
                    case 'L': if (n + 2 > nums.Count) return; cur = R(n); pts.Add(cur); n += 2; break;
                    case 'H': if (n + 1 > nums.Count) return; cur = new Vector2(rel ? cur.x + nums[n] : nums[n], cur.y); pts.Add(cur); n += 1; break;
                    case 'V': if (n + 1 > nums.Count) return; cur = new Vector2(cur.x, rel ? cur.y + nums[n] : nums[n]); pts.Add(cur); n += 1; break;
                    case 'C': { if (n + 6 > nums.Count) return; var p1 = R(n); var p2 = R(n + 2); var p3 = R(n + 4); Cubic(cur, p1, p2, p3); lastCtrl = p2; cur = p3; n += 6; break; }
                    case 'S': { if (n + 4 > nums.Count) return; var p1 = cur + (cur - lastCtrl); var p2 = R(n); var p3 = R(n + 2); Cubic(cur, p1, p2, p3); lastCtrl = p2; cur = p3; n += 4; break; }
                    case 'Q': { if (n + 4 > nums.Count) return; var q = R(n); var p3 = R(n + 2); Cubic(cur, cur + 2f / 3f * (q - cur), p3 + 2f / 3f * (q - p3), p3); lastCtrl = q; cur = p3; n += 4; break; }
                    case 'T': { if (n + 2 > nums.Count) return; var q = cur + (cur - lastCtrl); var p3 = R(n); Cubic(cur, cur + 2f / 3f * (q - cur), p3 + 2f / 3f * (q - p3), p3); lastCtrl = q; cur = p3; n += 2; break; }
                    case 'A': if (n + 7 > nums.Count) return; cur = R(n + 5); pts.Add(cur); n += 7; break;
                    default: return;
                }
                if (c is not ('C' or 'S' or 'Q' or 'T')) lastCtrl = cur;
                if (n >= nums.Count) return;
            }
        }
        while (i < d.Length)
        {
            var ch = d[i];
            if (char.IsLetter(ch) && ch != 'e' && ch != 'E')
            {
                Run(); nums.Clear(); cmd = ch; i++;
                continue;
            }
            if (char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.')
            {
                var j = i + 1;
                while (j < d.Length && (char.IsDigit(d[j]) || d[j] == '.' || d[j] == 'e' || d[j] == 'E' || ((d[j] == '-' || d[j] == '+') && (d[j - 1] == 'e' || d[j - 1] == 'E')))) j++;
                if (float.TryParse(d.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) nums.Add(f);
                i = j;
                continue;
            }
            i++;
        }
        Run();
        return pts;
    }

    /// <summary>
    /// What this element has already been seen to do. The scene is compiled once, so a value that
    /// moves afterwards must be a slot in it - and the only elements whose CSS cannot answer
    /// "can this change?" are the ones a script writes to. Having done it once is the answer
    /// available here; a compiled script will say so up front instead.
    /// </summary>
    private sealed class Seen { public bool Moved, Faded; }

    // Weak keys, like HtmlRenderer's own records: a removed element must not be kept alive by this.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Seen> Watched = new();

    private static Seen Unsettled(VisualElement ve) => Watched.GetValue(ve, _ => new Seen());

    /// <summary>A transition or animation that could touch this property, or the property itself declared.</summary>
    private static bool Declared(Dictionary<string, string> css, string property, string[] keys)
    {
        foreach (var key in keys)
            if (css.ContainsKey(key)) return true;
        return (css.TryGetValue("transition", out var tr) || css.TryGetValue("transition-property", out tr))
               && (tr.IndexOf(property, StringComparison.Ordinal) >= 0 || tr.IndexOf("all", StringComparison.Ordinal) >= 0);
    }

    private static readonly string[] MoveKeys = { "transform", "translate", "rotate", "scale", "offset-path", "offset-distance", "animation", "animation-name" };
    private static readonly string[] FadeKeys = { "opacity", "animation", "animation-name" };

    /// <summary>Can this element's transform still change once the page is compiled?</summary>
    private static bool CanMove(Ctx ctx, VisualElement ve)
        => (Watched.TryGetValue(ve, out var seen) && seen.Moved)
           || Declared(ctx.Built.CssOf(ve), "transform", MoveKeys)
           || Driven(ctx, ve);

    /// <summary>Can this element's opacity still change? Same argument, same failure if it cannot.</summary>
    private static bool CanFade(Ctx ctx, VisualElement ve)
        => (Watched.TryGetValue(ve, out var seen) && seen.Faded)
           || Declared(ctx.Built.CssOf(ve), "opacity", FadeKeys)
           || Driven(ctx, ve);

    /// <summary>
    /// Whether a script moves this element, known before it has moved once.
    /// </summary>
    /// <remarks>
    /// The two tests above are both retrospective: either the element has ALREADY been seen to move,
    /// or its own CSS declares a transform. Neither fires for an element whose only mover is a
    /// script - `#player` declares no transform, and nothing has moved it yet at the moment a page
    /// is first translated - so the group was dropped at identity and there was nowhere for the
    /// script's writes to land. Today that heals itself on the next frame, because the element
    /// having moved makes the first test true. A page translated ONCE never gets that frame, so the
    /// answer has to be known up front, which is what NamedGroups carries.
    /// </remarks>
    /// <summary>Whether a key with a zero value must still be emitted, because a script will write it.</summary>
    private static bool Keeps(Ctx ctx, VisualElement ve)
        => ve.name is { Length: > 0 } name && ctx.Built.Driven.Contains(name);

    private static bool Driven(Ctx ctx, VisualElement ve)
        => ve.name is { Length: > 0 } name && ctx.Built.NamedGroups.Contains(name);

    /// <summary>A CSS transform as the vector G attributes, kept numeric so clip
    /// polygons declared in scene space can be put through the same transform.</summary>
    private sealed class Xform
    {
        public float Ax, Ay, Tx, Ty, R, Sx = 1f, Sy = 1f;
        /// <summary>offset-path: a translation and turn applied under the transform, both ends of a tween alike.</summary>
        private float _ox, _oy, _or;
        private OffsetPlace? _offset;
        private Tweens.Tween? _tw;
        /// <summary>
        /// This element's transform is not settled, so every part of the group is written even at
        /// identity: the scene is compiled once, and a value that moves afterwards needs a slot to
        /// land in. Without it a translate reaching 0 deletes the whole group from the text.
        /// </summary>
        public bool Always;
        /// <summary>Nothing to apply: a clip outline through this must stay a rectangle.</summary>
        public bool IsIdentity;

        /// <summary>The element's resolved transform (UI Toolkit has already applied the CSS), tweened if one is running.</summary>
        public static Xform? From(Ctx ctx, int depth, VisualElement ve, Tweens.Tween? tw, float x, float y, float w, float h, OffsetPlace? offset = null)
        {
            var rs = OffThread.Of(ve);
            // Decided before anything is built: most elements have no transform at all, and this
            // runs on every one of them on every frame.
            var tx = rs.translate.x + (offset?.Dx ?? 0f);
            var ty = rs.translate.y + (offset?.Dy ?? 0f);
            var r = rs.rotate.angle.ToDegrees() + (offset?.Rot ?? 0f);
            var sx = rs.scale.value.x;
            var sy = rs.scale.value.y;
            var running = tw != null && tw.From.TransformDiffers(tw.To) ? tw : null;
            var identity = Mathf.Abs(tx) < 0.01f && Mathf.Abs(ty) < 0.01f && Mathf.Abs(r) < 0.01f
                           && Mathf.Abs(sx - 1f) < 0.001f && Mathf.Abs(sy - 1f) < 0.001f;
            if (!identity) Unsettled(ve).Moved = true;
            var always = CanMove(ctx, ve);
            if (identity && running == null && offset?.Ex == null && !always)
                return null;
            var xf = ctx.RentXform(depth);
            xf.Ax = offset?.Ax ?? x + w * 0.5f; xf.Ay = offset?.Ay ?? y + h * 0.5f;
            // transform-origin: the point the G turns and scales about. Only looked up for an
            // element that actually has a transform, which is a small minority of any page.
            if (offset == null && ctx.Built.CssOf(ve).TryGetValue("transform-origin", out var torigin))
            {
                Origin(torigin, w, h, out var oax, out var oay);
                xf.Ax = x + oax; xf.Ay = y + oay;
            }
            xf.Tx = tx; xf.Ty = ty; xf.R = r; xf.Sx = sx; xf.Sy = sy;
            xf._ox = offset?.Dx ?? 0f; xf._oy = offset?.Dy ?? 0f; xf._or = offset?.Rot ?? 0f;
            xf._offset = offset;
            xf._tw = running;
            xf.Always = always;
            xf.IsIdentity = identity && running == null && offset?.Ex == null;
            return xf;
        }

        /// <summary>Writes the group straight into the scene: this runs on every element that has a
        /// transform, and building a string for it was most of what an element's wrappers cost.</summary>
        public void AppendTo(StringBuilder sb)
        {
            sb.Append("G a=[").AppendNum(Ax).Append(',').AppendNum(Ay).Append(']');
            if (_offset?.Ex != null)
            {
                // the distance tween: the CSS translate/rotate as numbers plus the path expressions.
                // ponytail: a transform tween running at the same time is emitted at its end state
                sb.Append(" t=[\"").Append(_offset.Ex).Append('+').AppendNum(Tx - _ox).Append("\",\"").Append(_offset.Ey).Append('+').AppendNum(Ty - _oy).Append("\"]");
                sb.Append(" r=").Append(_offset.Er != null ? "\"" + _offset.Er + "+" + F(R - _or) + "\"" : F(R));
                if (Sx != 1f || Sy != 1f) sb.Append(" s=[").AppendNum(Sx).Append(',').AppendNum(Sy).Append(']');
                return;
            }
            if (_tw != null)
            {
                var f = _tw.From;
                sb.Append(" t=[\"").Append(_tw.Lerp(f.Translate.x + _ox, Tx)).Append("\",\"").Append(_tw.Lerp(f.Translate.y + _oy, Ty)).Append("\"]");
                sb.Append(" r=").Append(_tw.Lerp(f.Rotate + _or, R));
                sb.Append(" s=[\"").Append(_tw.Lerp(f.Scale.x, Sx)).Append("\",\"").Append(_tw.Lerp(f.Scale.y, Sy)).Append("\"]");
                return;
            }
            // Always: every part written, so the line's shape does not depend on the values in it.
            if (Always || Tx != 0f || Ty != 0f) sb.Append(" t=[").AppendNum(Tx).Append(',').AppendNum(Ty).Append(']');
            if (Always || R != 0f) sb.Append(" r=").AppendNum(R);
            if (Always || Sx != 1f || Sy != 1f) sb.Append(" s=[").AppendNum(Sx).Append(',').AppendNum(Sy).Append(']');
        }

        /// <summary>Scale, rotate, translate about the anchor: the vector G's order.</summary>
        public Vector2 Apply(Vector2 p)
        {
            var dx = (p.x - Ax) * Sx;
            var dy = (p.y - Ay) * Sy;
            var rad = R * Mathf.Deg2Rad;
            var c = Mathf.Cos(rad);
            var sn = Mathf.Sin(rad);
            return new Vector2(Ax + Tx + dx * c - dy * sn, Ay + Ty + dx * sn + dy * c);
        }
    }

    /// <summary>
    /// A box with a linear-gradient background. A smooth ramp is one def and one rect. A
    /// HARD stop (two colours at the same position, the "two-tone badge" idiom) cannot be
    /// a gradient here: vertex colours interpolate, so the renderer would blur it across
    /// the shape. The box is split instead: one rect per segment, each clipped to its band
    /// of the gradient line, each a solid colour or its own ramp. Exact, and static.
    /// </summary>
    /// <summary>
    /// Shadow lists by declaration. A `sh=` list depends on nothing but the text, and every
    /// shadowed element re-parsed its own on every frame.
    /// </summary>
    private static readonly Dictionary<string, string> ShadowLists = new(StringComparer.Ordinal);

    /// <summary>CSS box-shadow list to the vector `sh` list: [[dx,dy,blur,spread,#colour[,inset]],...]; the vector mod draws inset ones inside the shape (ask 2).</summary>
    private static string Shadows(string css)
    {
        lock (ShadowLists)
            if (ShadowLists.TryGetValue(css, out var hit)) return hit;
        var made = ShadowsInner(css);
        lock (ShadowLists)
        {
            if (ShadowLists.Count > 4096) ShadowLists.Clear();   // a script writing fresh values cannot fill it
            ShadowLists[css] = made;
        }
        return made;
    }

    private static string ShadowsInner(string css)
    {
        var sb = new StringBuilder();
        foreach (var item in SplitTopLevelCommas(css))
        {
            var parts = item.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var nums = new List<float>();
            var colour = new Color(0f, 0f, 0f, 1f);
            var inset = false;
            var hasColour = false;
            foreach (var p in parts)
            {
                if (p == "inset") { inset = true; continue; }
                if (StyleApplier.IsNumber(p.TrimEnd('x').TrimEnd('p')) || char.IsDigit(p[0]) || p[0] == '-' || p[0] == '.') { nums.Add(StyleApplier.Num(p)); continue; }
                if (StyleApplier.TryColor(p, out var c)) { colour = c; hasColour = true; }
            }
            if (nums.Count < 2) continue;
            if (!hasColour) colour.a = 1f;
            while (nums.Count < 4) nums.Add(0f);
            if (sb.Length > 0) sb.Append(',');
            sb.Append('[').AppendNum(nums[0]).Append(',').AppendNum(nums[1]).Append(',').AppendNum(nums[2]).Append(',').AppendNum(nums[3]).Append(',').AppendHex(colour);
            if (inset) sb.Append(",inset"); // vector requirement 2 / 3
            sb.Append(']');
        }
        return sb.Length > 0 ? " sh=[" + sb + "]" : string.Empty;
    }

    /// <summary>The game's own face: it carries the subscripts, symbols and dingbats a console UI uses.</summary>
    private const string FallbackFace = "font_english";
    private static readonly Dictionary<string, (TMP_FontAsset? asset, float at)> FaceAssets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The TextMeshPro asset a scene `font=` name resolves to (the Fonts mod registers file fonts under their family and style), cached; a miss is retried after 5 s.</summary>
    /// <remarks>Game thread only; a translation asks <see cref="OffThread.Face"/>.</remarks>
    internal static TMP_FontAsset? AssetOf(string face)
    {
        if (FaceAssets.TryGetValue(face, out var e) && (e.asset != null || Time.realtimeSinceStartup - e.at < 5f)) return e.asset;
        TMP_FontAsset? found = null;
        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (string.Equals(f.name, face, StringComparison.OrdinalIgnoreCase)) { found = f; break; }
        if (!FaceAssets.ContainsKey(face) && HtmlConfig.Diagnostics)
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: face \"{face}\": {(found != null ? found.characterTable.Count + " characters" : "no TextMeshPro asset of that name")}");
        FaceAssets[face] = (found, Time.realtimeSinceStartup);
        return found;
    }

    private static readonly Dictionary<string, float> DigitEms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The face's widest digit advance in em (its tabular cell); 0 when the face is unknown.</summary>
    private static float DigitEm(string? face) => face == null ? 0f : OffThread.Digit(face);

    /// <remarks>Game thread only.</remarks>
    internal static float DigitEmOf(string face)
    {
        if (DigitEms.TryGetValue(face, out var em)) return em;
        var asset = AssetOf(face);
        em = 0f;
        if (asset != null && asset.faceInfo.pointSize > 0)
        {
            for (var c = '0'; c <= '9'; c++)
                if (asset.characterLookupTable.TryGetValue(c, out var ch) && ch.glyph != null)
                    em = Mathf.Max(em, ch.glyph.metrics.horizontalAdvance / asset.faceInfo.pointSize);
            if (em > 0f) DigitEms[face] = em;
        }
        return em;
    }

    /// <summary>
    /// A browser draws a glyph the face lacks from a fallback font; TextMeshPro drops it. Runs of
    /// such characters (outside tags, following the face of any inner font tag) take the game's own
    /// face. The text is scene-escaped rich text, so a quote inside a tag is a backslash and a quote.
    /// </summary>
    /// <summary>The open &lt;font&gt; faces while scanning a label, reused: this runs twice per label per frame.</summary>
    [ThreadStatic] private static List<string?>? _faceStack;

    internal static string GlyphFallback(string text, string? face)
    {
        var asset = face != null && !string.Equals(face, FallbackFace, StringComparison.OrdinalIgnoreCase) && OffThread.Face(face) ? face : null;
        if (asset == null && text.IndexOf("<font=", StringComparison.Ordinal) < 0) return text;
        StringBuilder? sb = null;
        var inRun = false;
        var stack = _faceStack ??= new List<string?>();
        stack.Clear();
        var cur = asset;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '<')
            {
                var close = text.IndexOf('>', i);
                if (close > i)
                {
                    var tag = text.Substring(i, close - i + 1);
                    if (inRun) { sb!.Append("</font>"); inRun = false; }
                    if (tag.StartsWith("<font=", StringComparison.Ordinal)) { stack.Add(cur); var named = tag.Substring(6, tag.Length - 7).Trim('"', '\\', ' '); if (OffThread.Face(named)) cur = named; }
                    else if (tag == "</font>" && stack.Count > 0) { cur = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); }
                    sb?.Append(tag);
                    i = close;
                    continue;
                }
            }
            if (c >= 0x80 && cur != null && TextMeasure.ScriptDigit(c, out var digit, out var sub) && !OffThread.Has(cur, c, false) && OffThread.Has(cur, digit, false))
            {
                // a subscript the face lacks: its own digit, lowered and smaller, not another face's glyph
                sb ??= new StringBuilder(text, 0, i, text.Length + 40);
                if (inRun) { sb.Append("</font>"); inRun = false; }
                sb.Append(sub ? "<sub>" : "<sup>").Append(digit).Append(sub ? "</sub>" : "</sup>");
                continue;
            }
            var missing = false;
            if (c >= 0x80 && !char.IsSurrogate(c) && cur != null && !OffThread.Has(cur, c, false))
                missing = OffThread.Has(FallbackFace, c, true); // the face's own fallback chain counts, and a dynamic atlas adds on request
            if (missing)
            {
                sb ??= new StringBuilder(text, 0, i, text.Length + 40);
                if (!inRun) { sb.Append("<font=\\\"").Append(FallbackFace).Append("\\\">"); inRun = true; }
                sb.Append(c);
            }
            else
            {
                if (inRun) { sb!.Append("</font>"); inRun = false; }
                sb?.Append(c);
            }
        }
        if (inRun) sb!.Append("</font>");
        return sb?.ToString() ?? text;
    }

    /// <summary>The index of the parenthesis closing the one at `open`, or -1.</summary>
    private static int MatchingParen(string s, int open)
    {
        if (open < 0) return -1;
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// A background layer's box from the tokens after its image: [position] [/ size] [repeat]. Position
    /// keywords or lengths (a percentage places the box the CSS way), size in px or % of the box, auto
    /// the box itself. ponytail: repeat is ignored, the layer draws once.
    /// </summary>
    private static void LayerBox(string tail, float w, float h, out float lx, out float ly, out float lw, out float lh)
    {
        lx = 0f; ly = 0f; lw = w; lh = h;
        var slash = tail.IndexOf('/');
        var posPart = (slash >= 0 ? tail.Substring(0, slash) : tail).Trim();
        var sizePart = slash >= 0 ? tail.Substring(slash + 1).Trim() : string.Empty;
        var sizes = new List<string>();
        foreach (var t in sizePart.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (t is "no-repeat" or "repeat" or "repeat-x" or "repeat-y" or "round" or "space") break;
            sizes.Add(t);
        }
        float Dim(string t, float full) => t == "auto" ? full : t.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(t) / 100f * full : StyleApplier.Num(t);
        if (sizes.Count > 0) { lw = Dim(sizes[0], w); lh = sizes.Count > 1 ? Dim(sizes[1], h) : lh; }
        var xs = new List<string>(); var ys = new List<string>(); var plain = new List<string>();
        foreach (var t in posPart.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (t is "no-repeat" or "repeat" or "repeat-x" or "repeat-y" or "round" or "space") continue;
            if (t is "left" or "right") xs.Add(t);
            else if (t is "top" or "bottom") ys.Add(t);
            else if (t == "center") { if (xs.Count == 0 && plain.Count == 0) xs.Add(t); else ys.Add(t); }
            else plain.Add(t);
        }
        float Pos(string t, float full, float size) => t switch
        {
            "left" or "top" => 0f,
            "right" or "bottom" => full - size,
            "center" => (full - size) * 0.5f,
            _ => t.EndsWith("%", StringComparison.Ordinal) ? (full - size) * StyleApplier.Num(t) / 100f : StyleApplier.Num(t),
        };
        var px = xs.Count > 0 ? xs[0] : plain.Count > 0 ? plain[0] : "left";
        var py = ys.Count > 0 ? ys[0] : plain.Count > (xs.Count > 0 ? 0 : 1) ? plain[xs.Count > 0 ? 0 : 1] : "top";
        lx = Pos(px, w, lw);
        ly = Pos(py, h, lh);
    }

    /// <summary><c>transform-origin</c> as a point inside the box; the default is its centre.</summary>
    private static void Origin(string v, float w, float h, out float ax, out float ay)
    {
        ax = w * 0.5f; ay = h * 0.5f;
        var seen = 0;
        foreach (var part in v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part)
            {
                case "left": ax = 0f; continue;
                case "right": ax = w; continue;
                case "top": ay = 0f; continue;
                case "bottom": ay = h; continue;
                case "center": seen++; continue;   // whichever axis is still unspoken keeps its centre
                default:
                    var full = seen == 0 ? w : h;
                    var at = part.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(part) / 100f * full : StyleApplier.Num(part);
                    if (seen == 0) ax = at; else ay = at;
                    seen++;
                    continue;
            }
        }
    }

    /// <summary>A background value that paints a gradient rather than a colour or a picture.</summary>
    private static bool Gradient(string bg) =>
        bg.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase)
        || bg.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase)
        || bg.StartsWith("conic-gradient", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>background-size</c> / <c>-position</c> / <c>-position-x</c> / <c>-position-y</c> /
    /// <c>-origin</c> / <c>-repeat</c> longhands as one tile's box and how many tiles cover the
    /// element. False - and nothing else touched - when the page names none of them, which is the
    /// ordinary case and must stay free.
    /// </summary>
    /// <remarks>
    /// A comma-separated layer spells the same thing in its own tail and <see cref="LayerBox"/>
    /// already reads it; a single layer spells it in longhands, and nothing read those at all, so a
    /// gradient simply filled its box however the page sized it. The tail is composed here and
    /// handed to the same parser rather than a second one being written.
    /// </remarks>
    private static bool BackgroundTiles(Ctx ctx, Dictionary<string, string> css, OffThread.Box rs, float w, float h,
                                        out float lx, out float ly, out float lw, out float lh,
                                        out int nx, out int ny)
    {
        lx = 0f; ly = 0f; lw = w; lh = h; nx = 1; ny = 1;
        var hasSize = css.TryGetValue("background-size", out var size);
        var hasPos = css.TryGetValue("background-position", out var pos);
        var hasX = css.TryGetValue("background-position-x", out var posX);
        var hasY = css.TryGetValue("background-position-y", out var posY);
        var hasOrigin = css.TryGetValue("background-origin", out var origin);
        var hasRepeat = css.TryGetValue("background-repeat", out var repeat);
        // background-repeat on its own cannot change anything: with no size the tile IS the
        // positioning area, so one copy fills it either way - and this runs per element per frame.
        if (!hasSize && !hasPos && !hasX && !hasY && !hasOrigin) return false;

        // background-origin: the box the position and a percentage size are measured in.
        var o = hasOrigin ? origin!.Trim() : "padding-box";
        var il = o == "border-box" ? 0f : rs.borderLeftWidth + (o == "content-box" ? rs.paddingLeft : 0f);
        var it = o == "border-box" ? 0f : rs.borderTopWidth + (o == "content-box" ? rs.paddingTop : 0f);
        var ir = o == "border-box" ? 0f : rs.borderRightWidth + (o == "content-box" ? rs.paddingRight : 0f);
        var ib = o == "border-box" ? 0f : rs.borderBottomWidth + (o == "content-box" ? rs.paddingBottom : 0f);
        var aw = Mathf.Max(1f, w - il - ir);
        var ah = Mathf.Max(1f, h - it - ib);

        var posText = hasPos ? pos!.Trim()
            : hasX || hasY ? (hasX ? posX!.Trim() : "left") + " " + (hasY ? posY!.Trim() : "top")
            : string.Empty;
        var sizeText = hasSize ? size!.Trim() : string.Empty;
        // cover and contain are proportions of a picture; a gradient has none, so both fill the area
        if (sizeText is "cover" or "contain" or "auto") sizeText = string.Empty;
        var tail = sizeText.Length > 0 ? posText + "/" + sizeText : posText;
        LayerBox(tail, aw, ah, out lx, out ly, out lw, out lh);
        if (lw <= 0.01f || lh <= 0.01f) return false;
        lx += il; ly += it;

        var r = hasRepeat ? repeat!.Trim() : "repeat";
        var tileX = r is "repeat" or "repeat-x" or "round" or "space";
        var tileY = r is "repeat" or "repeat-y" or "round" or "space";
        // A browser tiles outward from the anchor in both directions; walk back to the first tile
        // that still touches the box, then forward until it is covered.
        if (tileX && lw < aw) { var back = Mathf.Floor((lx - il) / lw); lx -= back * lw; nx = Mathf.CeilToInt((w - lx) / lw); }
        if (tileY && lh < ah) { var back = Mathf.Floor((ly - it) / lh); ly -= back * lh; ny = Mathf.CeilToInt((h - ly) / lh); }
        nx = Mathf.Max(1, nx);
        ny = Mathf.Max(1, ny);
        // Each tile is a shape (and a gradient tile is a def as well), so a fine repeat - a 2px hatch
        // over a whole panel - would be thousands of both. Draw one and say so rather than quietly
        // spend the frame on it. ponytail: 64 tiles; a real pattern needs an image layer, not this.
        if (nx * ny > 64)
        {
            Warn(ctx, "html: background-repeat would need " + (nx * ny) + " tiles at this background-size; drawn once");
            nx = 1; ny = 1;
        }
        return true;
    }

    /// <summary>border-style dashed/dotted (from the shorthand or the property) as a dash pattern in border widths.</summary>
    private static string Dash(Dictionary<string, string> css, float bw) => DashFor(SideStyle(css, 0), bw);

    /// <summary>
    /// radial-gradient([circle|ellipse] [size] [at x y,] stops): a GR def in bounding-box
    /// units. The size keywords are approximated by a radius: farthest-corner (the CSS
    /// default) reaches the box corner, closest-side stops at the nearer edge.
    /// </summary>
    private static string? RadialDef(Ctx ctx, string css)
    {
        var open = css.IndexOf('(');
        var close = css.LastIndexOf(')');
        if (open < 0 || close < open) return null;
        var args = SplitTopLevelCommas(css.Substring(open + 1, close - open - 1));
        var cx = 0.5f; var cy = 0.5f; var r = 0.7071f;
        var first = args.Count > 0 ? args[0].Trim() : string.Empty;
        if (!StyleApplier.TryColor(first.Split(' ')[0], out _))
        {
            var at = first.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
            var shape = at >= 0 ? first.Substring(0, at) : first;
            if (shape.Contains("closest-side")) r = 0.5f;
            else if (shape.Contains("closest-corner")) r = 0.7071f;
            else if (shape.Contains("farthest-side")) r = 0.5f;
            if (at >= 0)
            {
                var pos = first.Substring(at + 4).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (pos.Length > 0) cx = Fraction(pos[0] switch { "left" => "0", "center" => "50%", "right" => "100%", _ => pos[0] });
                if (pos.Length > 1) cy = Fraction(pos[1] switch { "top" => "0", "center" => "50%", "bottom" => "100%", _ => pos[1] });
            }
            args.RemoveAt(0);
        }
        var stops = new List<(float at, Color c)>();
        for (var i = 0; i < args.Count; i++)
        {
            var parts = args[i].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !StyleApplier.TryColor(parts[0], out var c)) continue;
            var pos = parts.Length > 1 && parts[1].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(parts[1]) / 100f : (args.Count == 1 ? 0f : (float)i / (args.Count - 1));
            stops.Add((pos, c));
        }
        if (stops.Count < 2) return null;
        var id = ctx.NextId("rad");
        ctx.Defs.Append("  GR id=").Append(id).Append(" units=bbox cx=").AppendNum(cx).Append(" cy=").AppendNum(cy).Append(" r=").AppendNum(r).Append(" stops=[");
        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0) ctx.Defs.Append(',');
            ctx.Defs.Append('[').AppendNum(stops[i].at).Append(',').AppendHex(stops[i].c).Append(']');
        }
        ctx.Defs.Append("]\n");
        return id;
    }

    private static void GradientBox(Ctx ctx, string css, float x, float y, float w, float h, string ws, string hs, OffThread.Box rs, string indent, VisualElement ve, Xform? xf, string shadow = "")
    {
        var parsed = ParseGradient(css);
        if (parsed == null) return;
        var (angle, stops) = parsed.Value;

        // Segment boundaries: indices where a stop repeats the previous position.
        var cuts = new List<int>();
        for (var i = 1; i < stops.Count; i++)
            if (Mathf.Abs(stops[i].at - stops[i - 1].at) < 0.001f && stops[i].c != stops[i - 1].c)
                cuts.Add(i);

        // CSS gradient line: through the centre, long enough that the 0% and 100% lines
        // pass through the box's corners (length w|sin| + h|cos|). Half of it, in
        // bounding-box units per axis, is what the def and the clip planes work in.
        var rad = angle * Mathf.Deg2Rad;
        var len = w * Mathf.Abs(Mathf.Sin(rad)) + h * Mathf.Abs(Mathf.Cos(rad));
        var dx = Mathf.Sin(rad) * len * 0.5f / w;
        var dy = -Mathf.Cos(rad) * len * 0.5f / h;
        var rect = " x=" + F(x) + " y=" + F(y) + " w=" + ws + " h=" + hs + Radius(rs, w, h) + shadow;

        if (cuts.Count == 0)
        {
            // one colour at every stop is a flat fill: a multi-stop def is refined into many
            // triangles (a rounded box went from 30 vertices to ~21,000), for nothing
            var flat = true;
            for (var i = 1; i < stops.Count; i++) flat &= stops[i].c == stops[0].c;
            if (flat)
            {
                ctx.Body.Append(indent).Append('R').Append(rect).Append(" f=").AppendHex(stops[0].c).AppendNodeId(ctx, ve).Append('\n');
                ctx.Out.Nodes++;
                return;
            }
            var gid = ctx.NextId("grad");
            GradientDefLine(ctx, gid, dx, dy, 0f, 1f, stops);
            ctx.Body.Append(indent).Append('R').Append(rect).Append(" f=@").Append(gid).AppendNodeId(ctx, ve).Append('\n');
            ctx.Out.Nodes++;
            return;
        }

        var from = 0;
        for (var seg = 0; seg <= cuts.Count; seg++)
        {
            var to = seg < cuts.Count ? cuts[seg] : stops.Count;
            var part = stops.GetRange(from, to - from);
            var p0 = seg == 0 ? 0f : stops[cuts[seg - 1]].at;
            var p1 = seg < cuts.Count ? stops[cuts[seg]].at : 1f;
            from = to;
            if (p1 - p0 < 0.0005f || part.Count == 0) continue;

            // The band of the box between gradient parameters p0 and p1, in scene space.
            var poly = new List<Vector2> { new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h) };
            var cx = x + w * 0.5f;
            var cy = y + h * 0.5f;
            // p(P): projection onto the line A..B in bbox units, A = (0.5-dx, 0.5-dy),
            // B = (0.5+dx, 0.5+dy): p = 0.5 + (u*dx + v*dy) / (2 (dx^2 + dy^2)).
            var q = 2f * (dx * dx + dy * dy);
            var nx = dx / (w * q);
            var ny = dy / (h * q);
            if (seg > 0) poly = ClipHalfPlane(poly, nx, ny, (p0 - 0.5f) + nx * cx + ny * cy, true);
            if (seg < cuts.Count) poly = ClipHalfPlane(poly, nx, ny, (p1 - 0.5f) + nx * cx + ny * cy, false);
            if (poly.Count < 3) continue;

            var cid = ctx.NextId("cut");
            ctx.Defs.Append("  CP id=").Append(cid).Append(" { Y p=[");
            for (var i = 0; i < poly.Count; i++)
            {
                var p = xf != null ? xf.Apply(poly[i]) : poly[i];
                if (i > 0) ctx.Defs.Append(',');
                ctx.Defs.AppendNum(p.x).Append(',').AppendNum(p.y);
            }
            ctx.Defs.Append("] }\n");

            string fill;
            var uniform = true;
            for (var i = 1; i < part.Count; i++) uniform &= part[i].c == part[0].c;
            if (uniform)
            {
                fill = Hex(part[0].c);
            }
            else
            {
                var gid = ctx.NextId("grad");
                var local = new List<(float at, Color c)>(part.Count);
                foreach (var st in part) local.Add(((st.at - p0) / (p1 - p0), st.c));
                GradientDefLine(ctx, gid, dx, dy, p0, p1, local);
                fill = "@" + gid;
            }
            ctx.Body.Append(indent).Append("G clip=").Append(cid).Append(" { R").Append(rect).Append(" f=").Append(fill).Append(" }\n");
            ctx.Out.Nodes++;
        }
    }

    /// <summary>Keep the part of a convex polygon where nx*x + ny*y >= d (or <= d).</summary>
    private static List<Vector2> ClipHalfPlane(List<Vector2> poly, float nx, float ny, float d, bool keepAbove)
    {
        var result = new List<Vector2>(poly.Count + 2);
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var da = nx * a.x + ny * a.y - d;
            var db = nx * b.x + ny * b.y - d;
            if (!keepAbove) { da = -da; db = -db; }
            var inA = da >= 0f;
            var inB = db >= 0f;
            if (inA) result.Add(a);
            if (inA != inB)
            {
                var t = da / (da - db);
                result.Add(a + (b - a) * t);
            }
        }
        return result;
    }

    /// <summary>A GL def along the CSS gradient line (dx, dy from the centre), spanning parameters p0..p1.</summary>
    private static void GradientDefLine(Ctx ctx, string id, float dx, float dy, float p0, float p1, List<(float at, Color c)> stops)
    {
        var ax = 0.5f - dx; var ay = 0.5f - dy;
        var bx = 0.5f + dx; var by = 0.5f + dy;

        // Fold the stop span into the line, so the positions that a running page changes become
        // GEOMETRY rather than entries in an array. `stops=[[0,C],[0.67,T]]` becomes
        // `x2=<0.67 of the line>` with `stops=[[0,C],[1,T]]`: an exact reparametrisation of a
        // linear map, and the numbers that move are now slottable, where an array is not.
        //
        // Requires vector >= 0.11.31. Before it, a two-stop ramp whose stops did not span the
        // shape kept ramping past its last stop instead of holding that colour, so shortening the
        // line would have changed what is drawn. Since that fix it clamps to the end stop, which is
        // what makes this equivalent.
        var first = stops[0].at;
        var last = stops[stops.Count - 1].at;
        var span = last - first;
        var foldable = span > 0.0005f && (first > 0.0005f || last < 0.9995f);
        var q0 = foldable ? p0 + (p1 - p0) * first : p0;
        var q1 = foldable ? p0 + (p1 - p0) * last : p1;

        ctx.Defs.Append("  GL id=").Append(id).Append(" units=bbox x1=").AppendNum(ax + (bx - ax) * q0).Append(" y1=").AppendNum(ay + (by - ay) * q0)
            .Append(" x2=").AppendNum(ax + (bx - ax) * q1).Append(" y2=").AppendNum(ay + (by - ay) * q1).Append(" stops=[");
        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0) ctx.Defs.Append(',');
            var at = foldable ? (stops[i].at - first) / span : stops[i].at;
            ctx.Defs.Append('[').AppendNum(at).Append(',').AppendHex(stops[i].c).Append(']');
        }
        ctx.Defs.Append("]\n");
    }

    /// <summary>CSS linear-gradient(...) to an angle and positioned stops. Null when unusable.</summary>
    /// <summary>
    /// Parsed gradients by declaration. A gradient's angle and stops depend on nothing but the
    /// text, and the same three declarations were parsed again on every frame. Callers read the
    /// stop list and must not change it.
    /// </summary>
    private static readonly Dictionary<string, (float angle, List<(float at, Color c)> stops)?> Gradients = new(StringComparer.Ordinal);

    private static (float angle, List<(float at, Color c)> stops)? ParseGradient(string css)
    {
        lock (Gradients)
            if (Gradients.TryGetValue(css, out var hit)) return hit;
        var made = ParseGradientInner(css);
        lock (Gradients) Gradients[css] = made;
        return made;
    }

    private static (float angle, List<(float at, Color c)> stops)? ParseGradientInner(string css)
    {
        var open = css.IndexOf('(');
        var close = css.LastIndexOf(')');
        if (open < 0 || close < open) return null;
        var args = SplitTopLevelCommas(css.Substring(open + 1, close - open - 1));
        var angle = 180f;
        var first = args.Count > 0 ? args[0].Trim() : string.Empty;
        if (first.StartsWith("to ", StringComparison.OrdinalIgnoreCase))
        {
            angle = first.Substring(3).Trim() switch
            {
                "top" => 0f, "right" => 90f, "bottom" => 180f, "left" => 270f,
                "top right" or "right top" => 45f, "bottom right" or "right bottom" => 135f,
                "bottom left" or "left bottom" => 225f, "top left" or "left top" => 315f, _ => 180f,
            };
            args.RemoveAt(0);
        }
        else if (first.EndsWith("deg", StringComparison.OrdinalIgnoreCase) || first.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
        {
            angle = AngleDeg(first);
            args.RemoveAt(0);
        }
        var stops = new List<(float at, Color c)>();
        for (var i = 0; i < args.Count; i++)
        {
            var parts = args[i].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !StyleApplier.TryColor(parts[0], out var c)) continue;
            var at = parts.Length > 1 && parts[1].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(parts[1]) / 100f : (args.Count == 1 ? 0f : (float)i / (args.Count - 1));
            // CSS: a stop position never goes below the previous one.
            if (stops.Count > 0 && at < stops[stops.Count - 1].at) at = stops[stops.Count - 1].at;
            stops.Add((at, c));
        }
        return stops.Count < 2 ? null : (angle, stops);
    }

    private static float AngleDeg(string v)
    {
        v = v.Trim();
        if (v.EndsWith("deg", StringComparison.OrdinalIgnoreCase)) return StyleApplier.Num(v.Substring(0, v.Length - 3));
        if (v.EndsWith("rad", StringComparison.OrdinalIgnoreCase)) return StyleApplier.Num(v.Substring(0, v.Length - 3)) * Mathf.Rad2Deg;
        if (v.EndsWith("turn", StringComparison.OrdinalIgnoreCase)) return StyleApplier.Num(v.Substring(0, v.Length - 4)) * 360f;
        return StyleApplier.Num(v);
    }

    // ---------------------------------------------------------------- text

    /// <summary>Properties a browser inherits; a label reads them from the nearest ancestor that sets them.</summary>
    /// <summary>The names in <see cref="InheritedText"/>, for callers that need to know whether a
    /// property written on one element decides what its descendants show.</summary>
    internal static bool Inherits(string property)
    {
        foreach (var name in InheritedText)
            if (string.Equals(name, property, StringComparison.Ordinal)) return true;
        return false;
    }

    private static readonly string[] InheritedText = { "font-family", "font-weight", "font-style", "font-variant-numeric", "letter-spacing", "word-spacing", "line-height", "text-transform", "text-align", "text-align-last", "white-space", "text-shadow", "text-emphasis-style", "text-emphasis-color", "text-emphasis-position", "font-variant-caps", "text-indent" };

    /// <summary>The label's record with the inherited text properties filled in from its ancestors (a copy only when something is added).</summary>
    private static Dictionary<string, string> WithInherited(Ctx ctx, VisualElement ve, Dictionary<string, string> css)
    {
        Dictionary<string, string>? merged = null;
        for (var p = ve.parent; p != null; p = p.parent)
        {
            var pc = ctx.Built.CssOf(p);
            foreach (var name in InheritedText)
            {
                if (css.ContainsKey(name) || (merged != null && merged.ContainsKey(name))) continue;
                if (!pc.TryGetValue(name, out var v)) continue;
                if (merged == null)
                {
                    merged = ctx.RentRecord();
                    foreach (var own in css) merged[own.Key] = own.Value;
                }
                merged[name] = v;
            }
        }
        return merged ?? css;
    }

    private static void EmitText(Ctx ctx, Label label, Dictionary<string, string> css, float x, float y, float w, float h, string indent)
    {
        var rs = OffThread.Of(label);
        var text = label.text ?? string.Empty;
        if (text.Length == 0)
            return;
        var ownCss = css;
        css = WithInherited(ctx, label, css);
        if (css.TryGetValue("text-transform", out var tt))
            text = Transform(text, Lower(tt));
        // text-indent: TextMeshPro's line-indent is the first line of each paragraph, which
        // is the block's first line for a label. word-break: break-all lets a line break
        // between any two characters: a zero-width space after each one outside tags.
        if (css.TryGetValue("text-indent", out var ti) && rs.fontSize > 0f)
        {
            var px = ti.Trim().EndsWith("em", StringComparison.OrdinalIgnoreCase) ? StyleApplier.Num(ti) * rs.fontSize : ti.Trim().EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(ti) / 100f * w : StyleApplier.Num(ti);
            if (px != 0f) text = "<line-indent=" + F(px) + "px>" + text;
        }
        if (css.TryGetValue("word-break", out var wb) && wb.Trim() == "break-all")
            text = BreakAll(text);
        var deco = Decoration(css);
        var ox = x; var ow = w;
        var drawDeco = deco.line != 0 && !(rs.whiteSpace == WhiteSpace.Normal && rs.fontSize > 0f && h > rs.fontSize * 1.6f && text.IndexOf(' ') >= 0) && (deco.colour != null || deco.style != "solid" || deco.thickness > 0f || !float.IsNaN(deco.offset) || (deco.line & 4) != 0);
        if (!drawDeco)
        {
            // TextMeshPro draws the plain forms itself, wrapped lines included
            if ((deco.line & 1) != 0) text = "<u>" + text + "</u>";
            if ((deco.line & 2) != 0) text = "<s>" + text + "</s>";
            // ...but only in the text's own colour, weight and place. A wrapping label cannot have
            // its decoration drawn as geometry (there is no per-line measure), so a colour, style or
            // thickness asked for on one is dropped - say so rather than draw a plain underline and
            // leave the author looking for the red dashes they asked for.
            if (deco.line != 0 && (deco.colour != null || deco.style != "solid" || deco.thickness > 0f))
                Warn(ctx, "html: text-decoration-color / -style / -thickness is not drawn on a label that wraps; the plain line is");
        }
        // font-style: italic. The renderer folds a real weight face into style.face and leaves the
        // italic bit here precisely so it can still be drawn; nothing read it, so `font-style` was
        // accepted by the cascade and then never reached the glyphs. TextMeshPro shears them.
        if (rs.unityFontStyleAndWeight is FontStyle.Italic or FontStyle.BoldAndItalic && text.Length > 0)
            text = "<i>" + text + "</i>";
        // Scene text escapes (vector mod 0.10.1.0): backslash first, then the quote; a line
        // break in the text becomes the two characters backslash-n.
        text = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        // The scene reader types a clean number as a number even when quoted, and a number
        // has no text: "3" vanished from the footer. TextMeshPro's <noparse> keeps it a string.
        // font-variant-numeric: tabular-nums: TextMeshPro has no OpenType features, but it can
        // monospace a span; digits get an em-fraction cell, the rest stays proportional.
        if (css.TryGetValue("font-variant-numeric", out var fvn) && fvn.Contains("tabular"))
            text = System.Text.RegularExpressions.Regex.Replace(text, "[0-9]+", m => "<mspace=0.6em>" + m.Value + "</mspace>");
        // a purely numeric label is read as a number by the scene reader: guard it (after the tags above, which must stay tags)
        else if (float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            text = "<noparse>" + text + "</noparse>";
        // Same failure, different trigger: a value beginning with `=` is an EXPRESSION to the scene
        // reader, so a label reading "= 5 kPa" or "=> vent" was handed over as a formula and drew
        // nothing. The tag moves the `=` off the front, which is all the reader looks at.
        if (text.Length > 0 && text[0] == '=' && !text.StartsWith("<noparse>", StringComparison.Ordinal))
            text = "<noparse>" + text + "</noparse>";
        var align = rs.unityTextAlign;
        var centre = align == TextAnchor.MiddleCenter || align == TextAnchor.UpperCenter || align == TextAnchor.LowerCenter;
        var right = align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight;
        // A label in a scrolling box is not clipped to a line: the container slides it.
        var clipped = (rs.overflow == Overflow.Hidden && !Scrolls(css))
                      || (label.parent != null && OffThread.Of(label.parent).overflow == Overflow.Hidden && !Scrolls(ctx.Built.CssOf(label.parent)));
        var wraps = rs.whiteSpace == WhiteSpace.Normal && rs.fontSize > 0f && h > rs.fontSize * 1.6f && (text.IndexOf(' ') >= 0 || text.IndexOf('​') >= 0);
        // text-overflow: ellipsis wants the exact box (the ellipsis sits at its edge); any other clipped
        // label is clipped by its container's CP, so its own rect can carry the slack too
        var ellipsis = clipped && css.TryGetValue("text-overflow", out var tov) && tov.Trim() == "ellipsis";
        if (!ellipsis && !wraps)
        {
            // The layout width is UI Toolkit's measure of the text; TextMeshPro measures the
            // same face a little wider and wraps a shrink-wrapped label ("GA" / "S"). The
            // rect only positions the text, so give it slack on the side alignment allows.
            var slack = w * 0.35f + 8f;
            if (centre) x -= slack * 0.5f;
            else if (right) x -= slack;
            w += slack;
        }
        // TextMeshPro drops a line whose own metrics exceed the box before it ellipsizes: give the
        // single line its metric height, centred on the CSS line box (the clip stays the CSS box).
        // Decided here rather than rewritten into the finished T line, which cost four formatted
        // numbers and four concatenations on every clipped single-line label.
        var lines = 1; foreach (var ch in text) if (ch == '\n') lines++;
        var ty = y; var th = h;
        if (clipped && !wraps && lines == 1 && h < rs.fontSize * 1.4f)
        {
            th = rs.fontSize * 1.4f;
            ty = y - (th - h) * 0.5f;
        }
        var sb = ctx.Label;
        sb.Clear();
        sb.Append(indent).Append("T x=").AppendNum(x).Append(" y=").AppendNum(ty).Append(" w=").AppendNum(w).Append(" h=").AppendNum(th);
        var textAt = sb.Length;
        sb.Append(" text=\"").Append(text).Append('"');
        string? labelFace = null;
        sb.Append(" size=").AppendNum(rs.fontSize);
        // glyphs the face lacks (Barlow has no subscript digits, no gear) come from the game's own face, as a browser falls back;
        // an inner font tag's face counts for its span
        {
            var withFallback = GlyphFallback(text, labelFace);
            if (!ReferenceEquals(withFallback, text))
            {
                sb.Remove(textAt, text.Length + 8);
                sb.Insert(textAt, " text=\"" + withFallback + "\"");
            }
        }
        var ttw = ctx.Tw?.Of(label, ctx.Now);
        var textClip = (css.TryGetValue("background-clip", out var tbc) || css.TryGetValue("-webkit-background-clip", out tbc)) && tbc.Trim() == "text";
        var textGrad = textClip && (css.TryGetValue("background", out var tbg) || css.TryGetValue("background-image", out tbg)) && tbg.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase) ? ParseGradient(tbg) : null;
        if (textGrad != null)
        {
            // background-clip: text with a gradient: the glyphs take the gradient (vector requirement 14)
            var (angle, stops) = textGrad.Value;
            var rad = angle * Mathf.Deg2Rad;
            var len = w * Mathf.Abs(Mathf.Sin(rad)) + h * Mathf.Abs(Mathf.Cos(rad));
            var gid = ctx.NextId("tg");
            GradientDefLine(ctx, gid, Mathf.Sin(rad) * len * 0.5f / Mathf.Max(1f, w), -Mathf.Cos(rad) * len * 0.5f / Mathf.Max(1f, h), 0f, 1f, stops);
            sb.Append(" f=@").Append(gid);
        }
        else if (ttw != null && !Tweens.Snap.NearColour(ttw.From.Fg, rs.color))
        {
            var gid = ctx.NextId("tw");
            ctx.Defs.Append("  GL id=").Append(gid).Append(" stops=[[0,").AppendHex(ttw.From.Fg).Append("],[1,").AppendHex(rs.color).Append("]]\n");
            sb.Append(" f=@").Append(gid).Append(" fat==").Append(ttw.P);
        }
        else
            sb.Append(" f=").AppendHex(rs.color);
        var first = string.Empty;
        var fs = rs.unityFontStyleAndWeight;
        var wantBold = fs == FontStyle.Bold || fs == FontStyle.BoldAndItalic;
        if (css.TryGetValue("font-family", out var family))
        {
            first = FirstFamily(family);
            if (first.Length > 0 && !NamedWeight(first))
            {
                // A weight or stretch the family has as a real face beats a synthetic one:
                // Barlow ships Thin..Black and Condensed, and TextMeshPro's synthetic bold
                // only widens glyphs.
                if (css.TryGetValue("font-stretch", out var stretch) && stretch.IndexOf("condensed", StringComparison.OrdinalIgnoreCase) >= 0 && OffThread.Library(Join(first, "Condensed")))
                    first = Join(first, "Condensed");
                var weight = WeightFace(css, wantBold);
                if (weight != null && OffThread.Library(Join(first, weight)))
                {
                    first = Join(first, weight);
                    wantBold = false;
                }
            }
            if (first.Length > 0)
            {
                labelFace = FontLibrary.ResolveFace(first);
                sb.Append(" font=\"").Append(labelFace).Append('"');
            }
        }
        // A face that is already a named weight ("Barlow SemiBold") must not be bolded again:
        // TextMeshPro's synthetic bold widens every glyph on top of it.
        if (wantBold && !NamedWeight(first))
            sb.Append(" weight=bold");
        if (css.TryGetValue("letter-spacing", out var ls) && rs.fontSize > 0f)
        {
            // TextMeshPro's characterSpacing is hundredths of an em.
            // an em value is read as a bare number times this label's font size (Num would resolve it against the cascade's em)
            var lsv = ls.Trim();
            var px = lsv.EndsWith("em", StringComparison.OrdinalIgnoreCase) && float.TryParse(lsv.Substring(0, lsv.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var emv) ? emv * rs.fontSize : StyleApplier.Num(lsv);
            if (px != 0f) sb.Append(" cspace=").AppendNum(px / rs.fontSize * 100f);
        }
        // text-align-last: the last line's alignment, which for a single-line label is the line
        var lastAlign = !wraps && css.TryGetValue("text-align-last", out var tal) ? Lower(tal) : null;
        if (lastAlign is "center") sb.Append(" align=center");
        else if (lastAlign is "right" or "end") sb.Append(" align=right");
        else if (lastAlign is "left" or "start") { }
        else if (css.TryGetValue("text-align", out var ta) && ta.Trim() == "justify") sb.Append(" align=justified");
        else if (centre) sb.Append(" align=center");
        else if (right) sb.Append(" align=right");
        // A browser starts a block's text at its top; the text is only centred when the box is a
        // flex container that centres its items (or the box is no taller than its lines).
        var lineHpx = rs.fontSize * 1.2f;
        if (css.TryGetValue("line-height", out var lh0) && lh0.Trim() != "normal")
        {
            var lv = lh0.Trim();
            lineHpx = lv.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? StyleApplier.Num(lv) : (lv.EndsWith("em", StringComparison.OrdinalIgnoreCase) || lv.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(lv) * (lv.EndsWith("%", StringComparison.Ordinal) ? 0.01f : 1f) : StyleApplier.Num(lv)) * rs.fontSize;
        }
        var flexCentred = css.TryGetValue("display", out var dsp0) && dsp0.Trim() is "flex" or "inline-flex"
                          && ((css.TryGetValue("align-items", out var ai0) && ai0.Trim() == "center") || (css.TryGetValue("flex-direction", out var fd0) && fd0.Trim().StartsWith("column", StringComparison.Ordinal) && css.TryGetValue("justify-content", out var jc0) && jc0.Trim() == "center"));
        var tall = h > lineHpx * (lines + 0.5f) && !wraps && !flexCentred && !(ctx.Built.NodeOf.TryGetValue(label, out var tn) && tn.Tag is "td" or "th" or "button" or "summary" or "option" or "legend" or "label");
        sb.Append(tall ? " valign=top" : " valign=middle");
        if (ellipsis)
            sb.Append(" fit=ellipsis");
        if (wraps)
            sb.Append(" wrap=1");
        if (css.TryGetValue("text-shadow", out var tsh))
            sb.Append(Shadows(tsh)); // every shadow: extra ones are extra labels on the vector side (requirement 4)
        else if (css.TryGetValue("filter", out var tfl) && Filters(ctx, tfl, out _, out var tShadow) && tShadow.Length > 0)
            sb.Append(tShadow);
        // ::first-line: on a wrapped label the vector mod restyles the first line (vector
        // requirement 15); on a single line the whole text is the first line.
        var fl = PseudoCss(ctx, label, "first-line");
        if (fl.Count > 0)
        {
            var attrs = new StringBuilder();
            if (fl.TryGetValue("color", out var flc) && StyleApplier.TryColor(flc, out var flcol)) attrs.Append(" f=").AppendHex(flcol);
            if (fl.TryGetValue("font-size", out var fls)) attrs.Append(" size=").AppendNum(fls.EndsWith("em", StringComparison.OrdinalIgnoreCase) ? StyleApplier.Num(fls) * rs.fontSize : fls.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(fls) / 100f * rs.fontSize : StyleApplier.Num(fls));
            if (fl.TryGetValue("font-weight", out var flw) && (flw == "bold" || flw == "bolder" || (StyleApplier.IsNumber(flw) && StyleApplier.Num(flw) >= 600))) attrs.Append(" weight=bold");
            if (fl.TryGetValue("font-family", out var flf) && flf.Split(',')[0].Trim().Trim('"', '\'') is { Length: > 0 } flFace)
            {
                var face = FontLibrary.ResolveFace(flFace);
                attrs.Append(" font=").Append(face.IndexOf(' ') >= 0 ? "'" + face + "'" : face); // fl quotes spaced values with '...'
            }
            if (attrs.Length > 0)
            {
                if (wraps) sb.Append(" fl=\"").Append(attrs.ToString().Trim()).Append('"');
                else
                {
                    // rewrite the text attribute: rich tags around the whole (single) line
                    var open = new StringBuilder(); var close = new StringBuilder();
                    if (fl.TryGetValue("color", out var c1) && StyleApplier.TryColor(c1, out var col1)) { open.Append("<color=").AppendHex(col1).Append('>'); close.Insert(0, "</color>"); }
                    if (fl.TryGetValue("font-size", out var s1)) { open.Append("<size=").Append(s1.Trim()).Append('>'); close.Insert(0, "</size>"); }
                    if (attrs.ToString().Contains("weight=bold")) { open.Append("<b>"); close.Insert(0, "</b>"); }
                    var marker = " text=\"";
                    var at = sb.ToString().IndexOf(marker, StringComparison.Ordinal);
                    if (at >= 0) sb.Insert(at + marker.Length, open.ToString()).Insert(sb.ToString().IndexOf('"', at + marker.Length + open.Length), close.ToString());
                }
            }
        }
        if (css.TryGetValue("line-height", out var lh) && rs.fontSize > 0f)
        {
            // CSS: a bare number is a multiple of the font size, a length is absolute.
            var v = lh.Trim();
            var mult = v.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? StyleApplier.Num(v) / rs.fontSize
                     : v.EndsWith("em", StringComparison.OrdinalIgnoreCase) || v.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(v) / (v.EndsWith("%", StringComparison.Ordinal) ? 100f : 1f)
                     : StyleApplier.Num(v);
            if (mult > 0f && v != "normal") sb.Append(" lh=").AppendNum(mult);
        }
        sb.AppendNodeId(ctx, label).Append('\n');
        // glyphs the face lacks (Barlow has no subscript digits, no gear) come from the game's own face, as a browser
        // falls back; an inner font tag's face counts for its span. Tabular digits take the face's widest digit as
        // their cell, as the font's own tabular figures would. The text attribute is found by search: earlier
        // passes may have rewritten the rect before it.
        {
            var final = GlyphFallback(text, labelFace);
            var em = DigitEm(labelFace);
            if (em > 0f && final.IndexOf("<mspace=0.6em>", StringComparison.Ordinal) >= 0) final = final.Replace("<mspace=0.6em>", "<mspace=" + F(em) + "em>");
            if (!ReferenceEquals(final, text) && final != text)
            {
                var key = " text=\"" + text + "\"";
                var at = sb.ToString().IndexOf(key, StringComparison.Ordinal);
                if (at >= 0) { sb.Remove(at, key.Length); sb.Insert(at, " text=\"" + final + "\""); }
            }
        }
        ctx.Body.Append(sb);
        ctx.Out.Nodes++;
        if (css.TryGetValue("text-emphasis-style", out var emphasis) && emphasis.Trim() != "none")
            EmitEmphasis(ctx, css, rs, text, emphasis, x, y, w, h, indent);
        if (drawDeco)
            EmitDecoration(ctx, label, deco, rs, ox, y, ow, h, centre, right, indent);
        if (!ReferenceEquals(css, ownCss)) ctx.Return(css);
    }

    private struct Deco
    {
        /// <summary>Bit 1 underline, 2 line-through, 4 overline.</summary>
        public int line;
        public string style;
        public Color? colour;
        public float thickness;
        /// <summary>text-underline-offset in px, NaN for auto.</summary>
        public float offset;
    }

    /// <summary>text-decoration and its longhands: the lines, style, colour, thickness and underline offset.</summary>
    private static Deco Decoration(Dictionary<string, string> css)
    {
        var d = new Deco { style = "solid", offset = float.NaN };
        void Lines(string v)
        {
            d.line = 0;
            foreach (var p in v.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (p == "underline") d.line |= 1;
                else if (p == "line-through") d.line |= 2;
                else if (p == "overline") d.line |= 4;
                else if (p == "none") d.line = 0;
            }
        }
        if (css.TryGetValue("text-decoration", out var td))
        {
            Lines(td);
            foreach (var p in td.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var lp = p.ToLowerInvariant();
                if (lp is "solid" or "double" or "dotted" or "dashed" or "wavy") d.style = lp;
                else if (lp is "underline" or "overline" or "line-through" or "none" or "blink") { }
                else if (char.IsDigit(lp[0]) || lp[0] == '.') d.thickness = StyleApplier.Num(lp);
                else if (StyleApplier.TryColor(p, out var c)) d.colour = c;
            }
        }
        if (css.TryGetValue("text-decoration-line", out var tl)) Lines(tl);
        if (css.TryGetValue("text-decoration-style", out var ts)) d.style = Lower(ts);
        if (css.TryGetValue("text-decoration-color", out var tc) && StyleApplier.TryColor(tc, out var col)) d.colour = col;
        if (css.TryGetValue("text-decoration-thickness", out var tt) && tt.Trim() is not ("auto" or "from-font")) d.thickness = StyleApplier.Num(tt);
        if (css.TryGetValue("text-underline-offset", out var to) && to.Trim() != "auto") d.offset = StyleApplier.Num(to);
        return d;
    }

    /// <summary>
    /// Decoration lines drawn as geometry under a single-line label: underline, overline
    /// and line-through in their colour, thickness, style and offset. The text width is
    /// the layout's measure; the vertical positions follow the label's middle alignment.
    /// </summary>
    private static void EmitDecoration(Ctx ctx, Label label, Deco d, OffThread.Box rs, float x, float y, float w, float h, bool centre, bool right, string indent)
    {
        var fs = rs.fontSize;
        if (fs <= 0f) return;
        var tw = rs.textWidth; // measured on the game thread with the snapshot
        if (float.IsNaN(tw) || tw <= 0f) tw = w;
        tw = Mathf.Min(tw, w);
        var left = centre ? x + (w - tw) * 0.5f : right ? x + w - tw : x;
        var colour = d.colour ?? rs.color;
        var t = d.thickness > 0f ? d.thickness : Mathf.Max(1f, fs / 14f);
        var mid = y + h * 0.5f;
        var baseline = mid + fs * 0.32f;
        var offset = float.IsNaN(d.offset) ? fs * 0.1f : d.offset;
        void Line(float ly)
        {
            if (d.style == "double")
            {
                Stroke(ctx, indent, left, ly - t, left + tw, ly - t, t, colour, string.Empty);
                Stroke(ctx, indent, left, ly + t, left + tw, ly + t, t, colour, string.Empty);
            }
            else if (d.style == "wavy")
            {
                // a quadratic wave, half a period per segment, amplitude one thickness
                var period = Mathf.Max(2f, t * 4f);
                var sb = new StringBuilder("P d=\"M").AppendNum(left).Append(' ').AppendNum(ly);
                var n = Mathf.Min(400, Mathf.CeilToInt(tw / (period * 0.5f)));
                for (var i = 0; i < n; i++)
                {
                    var x0 = left + i * period * 0.5f;
                    var x1 = Mathf.Min(left + tw, x0 + period * 0.5f);
                    var cy = ly + (i % 2 == 0 ? -1f : 1f) * t * 2f;
                    sb.Append(" Q").AppendNum((x0 + x1) * 0.5f).Append(' ').AppendNum(cy).Append(' ').AppendNum(x1).Append(' ').AppendNum(ly);
                }
                sb.Append("\" f=none s=").AppendHex(colour).Append(" sw=").AppendNum(t).Append('\n');
                ctx.Body.Append(indent).Append(sb);
                ctx.Out.Nodes++;
            }
            else
                Stroke(ctx, indent, left, ly, left + tw, ly, t, colour, DashFor(d.style, t));
        }
        if ((d.line & 1) != 0) Line(baseline + offset + t * 0.5f);
        if ((d.line & 4) != 0) Line(mid - fs * 0.58f + t * 0.5f);
        if ((d.line & 2) != 0) Line(mid - fs * 0.02f);
    }

    private static void Stroke(Ctx ctx, string indent, float x1, float y1, float x2, float y2, float width, Color c, string extra)
    {
        ctx.Body.Append(indent).Append("L p=[").AppendNum(x1).Append(',').AppendNum(y1).Append(',').AppendNum(x2).Append(',').AppendNum(y2).Append("] s=").AppendHex(c).Append(" sw=").AppendNum(width).Append(extra).Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>A zero-width space after every character outside rich-text tags, so TextMeshPro may break anywhere (word-break: break-all).</summary>
    internal static string BreakAll(string text)
    {
        var sb = new StringBuilder(text.Length * 2);
        var inTag = false;
        foreach (var ch in text)
        {
            if (ch == '<') inTag = true;
            sb.Append(ch);
            if (inTag) { if (ch == '>') inTag = false; continue; }
            if (!char.IsWhiteSpace(ch)) sb.Append('\u200B');
        }
        return sb.ToString();
    }

    /// <summary>text-transform over the text outside rich-text tags.</summary>
    private static string Transform(string text, string mode)
    {
        if (mode != "uppercase" && mode != "lowercase" && mode != "capitalize")
            return text;
        var sb = new StringBuilder(text.Length);
        var inTag = false;
        var wordStart = true;
        foreach (var ch in text)
        {
            if (ch == '<') inTag = true;
            if (inTag) { sb.Append(ch); if (ch == '>') inTag = false; continue; }
            sb.Append(mode switch
            {
                "uppercase" => char.ToUpperInvariant(ch),
                "lowercase" => char.ToLowerInvariant(ch),
                _ => wordStart ? char.ToUpperInvariant(ch) : ch,
            });
            wordStart = char.IsWhiteSpace(ch);
        }
        return sb.ToString();
    }

    /// <summary>The face-name suffix for the cascaded font-weight (Thin..Black), null for regular.</summary>
    private static string? WeightFace(Dictionary<string, string> css, bool bold)
    {
        if (!css.TryGetValue("font-weight", out var w)) return bold ? "Bold" : null;
        return WeightFace(w, bold);
    }

    internal static string? WeightFace(string w, bool bold)
    {
        var v = Lower(w);
        if (v == "bold" || v == "bolder") return "Bold";
        if (v == "lighter") return "Light";
        if (!StyleApplier.IsNumber(v)) return bold ? "Bold" : null;
        var n = StyleApplier.Num(v);
        if (n < 150) return "Thin";
        if (n < 250) return "ExtraLight";
        if (n < 350) return "Light";
        if (n < 450) return null;
        if (n < 550) return "Medium";
        if (n < 650) return "SemiBold";
        if (n < 750) return "Bold";
        if (n < 850) return "ExtraBold";
        return "Black";
    }

    internal static bool NamedWeight(string family)
    {
        var f = family.ToLowerInvariant();
        return f.Contains("bold") || f.Contains("black") || f.Contains("heavy") || f.Contains("semi") || f.Contains("medium")
               || f.Contains("light") || f.Contains("thin") || f.Contains("extra");
    }

    private static bool IsGeneric(string f)
    {
        switch (f.ToLowerInvariant())
        {
            case "sans-serif": case "serif": case "monospace": case "system-ui": case "ui-sans-serif": case "ui-serif": case "ui-monospace": case "cursive": case "fantasy":
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- svg

    private static void EmitSvg(Ctx ctx, SvgElement svg, float x, float y, float w, float h, string indent)
    {
        var vb = svg.ViewBox;
        if (vb.width <= 0f || vb.height <= 0f || w <= 0f || h <= 0f)
            return;
        float sx, sy, ox, oy;
        if (svg.Stretch)
        {
            sx = w / vb.width; sy = h / vb.height; ox = x; oy = y;
        }
        else
        {
            var s = Mathf.Min(w / vb.width, h / vb.height);
            sx = sy = s;
            ox = x + (w - vb.width * s) * 0.5f;
            oy = y + (h - vb.height * s) * 0.5f;
        }
        var id = ctx.NextId("svg");
        ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendNum(w).Append(" h=").AppendNum(h).AppendRadius(OffThread.Of(svg), w, h).Append(" }\n");

        // Uniform fit: one scaled group. Non-uniform (preserveAspectRatio="none" on a box of
        // another shape): the scale is baked into every coordinate instead, so a stroke keeps
        // one width in every direction rather than being stretched with the box.
        var group = " t=[" + F(ox - vb.x * sx) + "," + F(oy - vb.y * sy) + "] s=[" + F(sx) + "," + F(sy) + "]";
        var bake = Mathf.Abs(sx - sy) > 0.01f * Mathf.Max(sx, sy);
        var fit = bake ? new Fit(ox - vb.x * sx, oy - vb.y * sy, sx, sy, group) : new Fit(0f, 0f, 1f, 1f, null);
        ctx.SvgScale = bake ? 1f : sx;
        ctx.Body.Append(indent).Append("G clip=").Append(id).Append(bake ? string.Empty : group).Append(" {\n");
        var inner = indent + "  ";
        // SVG ids are per document, scene def ids are global: sixteen tanks each declare
        // `#fill`, so every reference is prefixed with this svg's id.
        // clip defs are scene-space whatever the group does: always the baked fit
        var absolute = new Fit(ox - vb.x * sx, oy - vb.y * sy, sx, sy, group);
        foreach (var shape in svg.Shapes)
            if (shape.Tag == "clipPath") SvgClipDef(ctx, shape, id + "_", absolute);
        foreach (var shape in svg.Shapes)
            EmitShape(ctx, shape, inner, id + "_", fit);
        ctx.Body.Append(indent).Append("}\n");
    }

    /// <summary>A clipPath's shapes as one CP def, in scene coordinates (the fit and any transform baked in).</summary>
    private static void SvgClipDef(Ctx ctx, SvgShape clip, string prefix, Fit fit)
    {
        var id = clip.Attr("id");
        if (id == null || clip.Children == null || clip.Children.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var shape in clip.Children)
        {
            var g = Geometry(shape, fit, out var isPath);
            if (g == null) continue;
            if (sb.Length > 0) sb.Append(' ');
            if (isPath && fit.Bake) sb.Append('G').Append(fit.PathGroup).Append(" { ").Append(g).Append(" }");
            else sb.Append(g);
        }
        if (sb.Length == 0) return;
        ctx.Defs.Append("  CP id=").Append(prefix).Append(id).Append(" { ").Append(sb).Append(" }\n");
    }

    /// <summary>The node text for a shape's geometry alone (no paint), or null for a tag with none.</summary>
    private static string? Geometry(SvgShape shape, Fit fit, out bool isPath)
    {
        isPath = shape.Tag == "path";
        switch (shape.Tag)
        {
            case "rect":
            {
                var r = "R x=" + fit.X(shape.Attr("x")) + " y=" + fit.Y(shape.Attr("y")) + " w=" + fit.W(shape.Attr("width")) + " h=" + fit.H(shape.Attr("height"));
                if (shape.Attr("rx") != null) r += " rx=" + fit.Len(shape.Attr("rx"));
                return r;
            }
            case "circle": return "C cx=" + fit.X(shape.Attr("cx")) + " cy=" + fit.Y(shape.Attr("cy")) + " rx=" + fit.W(shape.Attr("r")) + " ry=" + fit.H(shape.Attr("r"));
            case "ellipse": return "C cx=" + fit.X(shape.Attr("cx")) + " cy=" + fit.Y(shape.Attr("cy")) + " rx=" + fit.W(shape.Attr("rx")) + " ry=" + fit.H(shape.Attr("ry"));
            case "polygon":
            case "polyline":
            {
                var pts = (shape.Attr("points") ?? string.Empty).Split(new[] { ' ', ',', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new StringBuilder("Y p=[");
                for (var i = 0; i + 1 < pts.Length; i += 2) { if (i > 0) sb.Append(','); sb.Append(fit.X(pts[i])).Append(',').Append(fit.Y(pts[i + 1])); }
                return sb.Append(']').ToString();
            }
            case "path": return "P d=\"" + (shape.Attr("d") ?? string.Empty).Replace('"', ' ') + "\"";
        }
        return null;
    }

    /// <summary>A shape's `__m` (viewBox space) as a scene-space matrix: under a scaled group it applies as is; when the fit is baked it is conjugated by the fit.</summary>
    private static string? ShapeMatrix(SvgShape shape, Fit fit)
    {
        var mt = shape.Attr("__m");
        if (mt == null) return null;
        var parts = mt.Split(',');
        if (parts.Length != 6) return null;
        var m = new float[6];
        for (var i = 0; i < 6; i++) m[i] = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        if (fit.Bake)
        {
            var f = new[] { fit.Sx, 0f, 0f, fit.Sy, fit.Ox, fit.Oy };
            var inv = new[] { 1f / fit.Sx, 0f, 0f, 1f / fit.Sy, -fit.Ox / fit.Sx, -fit.Oy / fit.Sy };
            m = HtmlRenderer.MulMatrix(HtmlRenderer.MulMatrix(f, m), inv);
        }
        return "[" + F(m[0]) + "," + F(m[1]) + "," + F(m[2]) + "," + F(m[3]) + "," + F(m[4]) + "," + F(m[5]) + "]";
    }

    /// <summary>viewBox to scene mapping for one svg: identity under a scaled group, or baked.</summary>
    private readonly struct Fit
    {
        public readonly float Ox, Oy, Sx, Sy;
        /// <summary>When baking, the group a `path` still needs (its `d` cannot be rewritten).</summary>
        public readonly string? PathGroup;
        public bool Bake => PathGroup != null;

        public Fit(float ox, float oy, float sx, float sy, string? pathGroup)
        {
            Ox = ox; Oy = oy; Sx = sx; Sy = sy; PathGroup = pathGroup;
        }

        public string X(string? v) => Map(v, Ox, Sx);
        public string Y(string? v) => Map(v, Oy, Sy);
        public string W(string? v) => Map(v, 0f, Sx);
        public string H(string? v) => Map(v, 0f, Sy);
        /// <summary>A length with no direction (stroke width): the mean scale.</summary>
        public string Len(string? v) => Map(v, 0f, (Sx + Sy) * 0.5f);
        public float Px(float vx) => Ox + Sx * vx;
        public float Py(float vy) => Oy + Sy * vy;

        private string Map(string? v, float o, float scale)
        {
            v = (v ?? "0").Trim();
            if (!Bake)
                return Expr(v);
            if (v.StartsWith("=", StringComparison.Ordinal))
                return Expr("=" + F(o) + "+" + F(scale) + "*(" + v.Substring(1) + ")");
            return F(o + scale * StyleApplier.Num(v));
        }
    }

    /// <summary>
    /// Vector attributes with no SVG spelling, passed through verbatim so a page can say
    /// what the vector layer can do: `fo2` (opacity at a band's second edge), `fea`,
    /// `fea_edge`, `lod`, dashes.
    /// </summary>
    private static readonly string[] Passthrough = { "fo2", "fea", "fea_edge", "lod", "dash", "dofs", "ml", "sh", "sd", "sdo", "fr" };

    private static void EmitShape(Ctx ctx, SvgShape shape, string indent, string prefix, Fit fit)
    {
        if (shape.Tag == "linearGradient" || shape.Tag == "radialGradient")
        {
            SvgGradient(ctx, shape, prefix);
            return;
        }

        if (shape.Tag == "clipPath" || shape.Tag == "marker")
            return;
        var sb = new StringBuilder(indent);
        var opacity = shape.Attr("opacity");
        // wrappers: a clip-path reference, a transform (vector requirement 13)
        var wrappers = 0;
        if (shape.Attr("__clip") is { } clipRef) { sb.Append("G clip=").Append(prefix).Append(clipRef).Append(" { "); wrappers++; }
        if (ShapeMatrix(shape, fit) is { } mtx) { sb.Append("G m=").Append(mtx).Append(" { "); wrappers++; }
        if (shape.Tag == "text")
        {
            SvgTextNode(ctx, shape, sb, prefix, fit, opacity);
            for (var i = 0; i < wrappers; i++) sb.Append(" }");
            ctx.Body.Append(sb).Append('\n');
            ctx.Out.Nodes++;
            return;
        }
        if (shape.Tag == "image")
        {
            var href = HtmlRenderer.ResolveUrl(shape.Attr("href") ?? string.Empty, ctx.Built);
            if (href.Length == 0) return;
            var par = (shape.Attr("preserveAspectRatio") ?? "xMidYMid meet").ToLowerInvariant();
            var ifit = par.Contains("none") ? "fill" : par.Contains("slice") ? "cover" : "contain";
            sb.Append("IMG x=").Append(fit.X(shape.Attr("x"))).Append(" y=").Append(fit.Y(shape.Attr("y"))).Append(" w=").Append(fit.W(shape.Attr("width"))).Append(" h=").Append(fit.H(shape.Attr("height")))
              .Append(" src=\"").Append(href.Replace("\"", string.Empty)).Append("\" fit=").Append(ifit);
            if (opacity != null) sb.Append(" o=").Append(Expr(opacity));
            for (var i = 0; i < wrappers; i++) sb.Append(" }");
            ctx.Body.Append(sb).Append('\n');
            ctx.Out.Nodes++;
            return;
        }

        // `n` on a discrete shape is the repeat extension: n instances with `i` bound, the
        // vector layer's RP. On polygon/polyline it is the sampled form instead (below).
        var repeat = shape.Tag != "polygon" && shape.Tag != "polyline" ? shape.Attr("n") : null;
        if (repeat != null)
        {
            sb.Append("RP n=").Append(Expr(repeat));
            var lod = shape.Attr("lod");
            if (lod != null) sb.Append(" lod=").Append(Expr(lod));
            sb.Append(" { ");
        }
        var pathGroup = shape.Tag == "path" && fit.Bake;
        if (pathGroup)
            sb.Append('G').Append(fit.PathGroup).Append(" { ");

        switch (shape.Tag)
        {
            case "line":
                sb.Append("L p=[").Append(fit.X(shape.Attr("x1"))).Append(',').Append(fit.Y(shape.Attr("y1"))).Append(',').Append(fit.X(shape.Attr("x2"))).Append(',').Append(fit.Y(shape.Attr("y2"))).Append(']');
                break;
            case "polyline":
            case "polygon":
            {
                var n = shape.Attr("n");
                var bound = shape.Attr("__data");
                if (bound != null && shape.Owner != null)
                {
                    // Bound to a number array from the data payload: n samples across the
                    // viewBox, y from $key[i]. A polygon becomes a band down to the viewBox
                    // bottom (the filled-area idiom). Nothing here changes per tick.
                    var vb = shape.Owner.ViewBox;
                    var count = Mathf.Max(2, (int)StyleApplier.Num(shape.Attr("__n") ?? "2"));
                    sb.Append(shape.Tag == "polygon" ? "YS" : "LS").Append(" n=").Append(count.ToString(CultureInfo.InvariantCulture));
                    sb.Append(" x==").AppendNum(fit.Px(vb.x)).Append('+').AppendNum(fit.Sx * vb.width / (count - 1)).Append("*i");
                    sb.Append(" y=").Append(fit.Y("=$" + bound + "[i]"));
                    if (shape.Tag == "polygon")
                        sb.Append(" y2=").AppendNum(fit.Py(vb.y + vb.height));
                }
                else if (n != null)
                {
                    // Sampled form, the extension: n samples of x/y (and y2 for a band) as expressions over i.
                    sb.Append(shape.Tag == "polygon" ? "YS" : "LS").Append(" n=").Append(n);
                    sb.Append(" x=").Append(fit.X(shape.Attr("x") ?? "=i")).Append(" y=").Append(fit.Y(shape.Attr("y") ?? "=0"));
                    if (shape.Tag == "polygon")
                        sb.Append(" y2=").Append(fit.Y(shape.Attr("y2") ?? "=0"));
                }
                else
                {
                    var pts = (shape.Attr("points") ?? string.Empty).Split(new[] { ' ', ',', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    sb.Append(shape.Tag == "polygon" ? "Y" : "L").Append(" p=[");
                    for (var i = 0; i + 1 < pts.Length; i += 2)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(fit.X(pts[i])).Append(',').Append(fit.Y(pts[i + 1]));
                    }
                    sb.Append(']');
                }
                break;
            }
            case "rect":
                sb.Append("R x=").Append(fit.X(shape.Attr("x"))).Append(" y=").Append(fit.Y(shape.Attr("y"))).Append(" w=").Append(fit.W(shape.Attr("width"))).Append(" h=").Append(fit.H(shape.Attr("height")));
                if (shape.Attr("rx") != null) sb.Append(" rx=").Append(fit.Len(shape.Attr("rx")));
                break;
            case "circle":
                sb.Append("C cx=").Append(fit.X(shape.Attr("cx"))).Append(" cy=").Append(fit.Y(shape.Attr("cy"))).Append(" rx=").Append(fit.W(shape.Attr("r"))).Append(" ry=").Append(fit.H(shape.Attr("r")));
                break;
            case "ellipse":
                sb.Append("C cx=").Append(fit.X(shape.Attr("cx"))).Append(" cy=").Append(fit.Y(shape.Attr("cy"))).Append(" rx=").Append(fit.W(shape.Attr("rx"))).Append(" ry=").Append(fit.H(shape.Attr("ry")));
                break;
            case "path":
                sb.Append("P d=\"").Append((shape.Attr("d") ?? string.Empty).Replace('"', ' ')).Append('"');
                break;
            default:
                return;
        }

        // Paint. SVG defaults: fill black, no stroke; a line/polyline has no fill. Fill and
        // stroke are built apart so paint-order: stroke can draw the stroke under the fill.
        var fill = shape.Attr("fill") ?? (shape.Tag == "line" || shape.Tag == "polyline" ? "none" : "black");
        var fillSb = new StringBuilder();
        fillSb.Append(" f=").Append(Paint(fill, prefix));
        var fo = Mul(shape.Attr("fill-opacity"), opacity);
        if (fo != null) fillSb.Append(" fo=").Append(fo);
        var strokeSb = new StringBuilder();
        var stroke = shape.Attr("stroke");
        // pathLength: the author's length for the geometry; dashes and their offset scale by the real length over it
        var dashScale = 1f;
        if (shape.Attr("pathLength") is { } plen && StyleApplier.Num(plen) > 0f && GeometryLength(shape) is var real && real > 0f) dashScale = real / StyleApplier.Num(plen);
        string Dash(string p) => dashScale == 1f ? (pathGroup ? Expr(p) : fit.Len(p)) : F(StyleApplier.Num(p) * dashScale * (pathGroup ? 1f : (fit.Sx + fit.Sy) * 0.5f));
        if (stroke != null && stroke != "none")
        {
            strokeSb.Append(" s=").Append(Paint(stroke, prefix));
            // A path under its own scaled group keeps its width in viewBox units; a non-scaling
            // stroke is one width on screen whatever the svg's scale.
            var swRaw = shape.Attr("stroke-width") ?? "1";
            var nonScaling = shape.Attr("vector-effect") is { } veff && veff.Trim() == "non-scaling-stroke";
            strokeSb.Append(" sw=").Append(nonScaling ? F(StyleApplier.Num(swRaw) / Mathf.Max(0.001f, fit.Bake ? 1f : ctx.SvgScale)) : pathGroup ? Expr(swRaw) : fit.Len(swRaw));
            var so = Mul(shape.Attr("stroke-opacity"), opacity);
            if (so != null) strokeSb.Append(" so=").Append(so);
            var cap = shape.Attr("stroke-linecap");
            if (cap != null) strokeSb.Append(" cap=").Append(cap);
            var join = shape.Attr("stroke-linejoin");
            if (join != null) strokeSb.Append(" join=").Append(join);
            // dashes and miter limit in their SVG spellings
            if (shape.Attr("stroke-dasharray") is { } da && shape.Attr("dash") == null && da.Trim() != "none")
            {
                var parts = da.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var list = new StringBuilder();
                foreach (var p in parts) { if (list.Length > 0) list.Append(','); list.Append(Dash(p)); }
                if (list.Length > 0) strokeSb.Append(" dash=[").Append(list).Append(']');
            }
            if (shape.Attr("stroke-dashoffset") is { } dofs && shape.Attr("dofs") == null) strokeSb.Append(" dofs=").Append(Dash(dofs));
            if (shape.Attr("stroke-miterlimit") is { } ml && shape.Attr("ml") == null) strokeSb.Append(" ml=").Append(Expr(ml));
        }
        var common = new StringBuilder();
        foreach (var name in Passthrough)
        {
            if (name == "lod" && repeat != null) continue;
            var v = shape.Attr(name);
            if (v != null) common.Append(' ').Append(name).Append('=').Append(Expr(v));
        }
        if (shape.Attr("fill-rule") is { } frule && shape.Attr("fr") == null) common.Append(" fr=").Append(frule.Trim());
        if (shape.Attr("shape-rendering") is { } srend && srend.Trim() == "crispEdges" && shape.Attr("fea") == null) common.Append(" fea=0");
        var sid = shape.Attr("id");
        var tail = new StringBuilder();
        if (pathGroup) tail.Append(" }");
        if (repeat != null) tail.Append(" }");
        for (var i = 0; i < wrappers; i++) tail.Append(" }");
        var strokeFirst = strokeSb.Length > 0 && fill != "none" && shape.Attr("paint-order") is { } po && po.Trim().StartsWith("stroke", StringComparison.Ordinal);
        if (strokeFirst)
        {
            // the stroke first, then the fill over it (paint-order: stroke): two nodes of the same geometry
            ctx.Body.Append(sb).Append(" f=none").Append(strokeSb).Append(common).Append(tail).Append('\n');
            ctx.Out.Nodes++;
            ctx.Body.Append(sb).Append(fillSb).Append(common);
        }
        else ctx.Body.Append(sb).Append(fillSb).Append(strokeSb).Append(common);
        if (sid != null) ctx.Body.Append(" id=").Append(sid);
        ctx.Body.Append(tail).Append('\n');
        ctx.Out.Nodes++;
        EmitMarkers(ctx, shape, indent, prefix, fit);
    }

    /// <summary>The length of a shape's outline in viewBox units, for pathLength; 0 when unknown.</summary>
    private static float GeometryLength(SvgShape shape)
    {
        float N(string a) => shape.Attr(a) is { } v ? StyleApplier.Num(v) : 0f;
        switch (shape.Tag)
        {
            case "line": return Vector2.Distance(new Vector2(N("x1"), N("y1")), new Vector2(N("x2"), N("y2")));
            case "rect": return 2f * (N("width") + N("height"));
            case "circle": return 2f * Mathf.PI * N("r");
            case "ellipse": { var a = N("rx"); var b = N("ry"); var hh = (a - b) * (a - b) / Mathf.Max(0.0001f, (a + b) * (a + b)); return Mathf.PI * (a + b) * (1f + 3f * hh / (10f + Mathf.Sqrt(4f - 3f * hh))); }
            case "polyline": case "polygon": case "path":
            {
                var pts = shape.Tag == "path" ? FlattenPath(shape.Attr("d") ?? string.Empty) : Points(shape);
                var len = 0f;
                for (var i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
                if (shape.Tag == "polygon" && pts.Count > 1) len += Vector2.Distance(pts[pts.Count - 1], pts[0]);
                return len;
            }
            default: return 0f;
        }
    }

    private static List<Vector2> Points(SvgShape shape)
    {
        var raw = (shape.Attr("points") ?? string.Empty).Split(new[] { ' ', ',', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var pts = new List<Vector2>();
        for (var i = 0; i + 1 < raw.Length; i += 2) pts.Add(new Vector2(StyleApplier.Num(raw[i]), StyleApplier.Num(raw[i + 1])));
        return pts;
    }

    /// <summary>
    /// marker-start/mid/end: the marker's shapes at the shape's vertices, turned along the
    /// line (orient="auto"), scaled by the stroke width (markerUnits default) and the marker's
    /// viewBox, offset by refX/refY. ponytail: a path gets start and end markers only (its
    /// vertices are the flattened curve, not the author's points).
    /// </summary>
    private static void EmitMarkers(Ctx ctx, SvgShape shape, string indent, string prefix, Fit fit)
    {
        if (shape.Owner == null) return;
        var start = MarkerRef(shape.Attr("marker-start") ?? shape.Attr("marker"));
        var mid = MarkerRef(shape.Attr("marker-mid") ?? shape.Attr("marker"));
        var end = MarkerRef(shape.Attr("marker-end") ?? shape.Attr("marker"));
        if (start == null && mid == null && end == null) return;
        List<Vector2> pts;
        switch (shape.Tag)
        {
            case "line": pts = new List<Vector2> { new(StyleApplier.Num(shape.Attr("x1") ?? "0"), StyleApplier.Num(shape.Attr("y1") ?? "0")), new(StyleApplier.Num(shape.Attr("x2") ?? "0"), StyleApplier.Num(shape.Attr("y2") ?? "0")) }; break;
            case "polyline": case "polygon": pts = Points(shape); break;
            case "path": { var fp = FlattenPath(shape.Attr("d") ?? string.Empty); if (fp.Count < 2) return; pts = new List<Vector2> { fp[0], fp[1], fp[fp.Count - 2], fp[fp.Count - 1] }; mid = null; break; }
            default: return;
        }
        if (pts.Count < 2) return;
        var strokeW = StyleApplier.Num(shape.Attr("stroke-width") ?? "1");
        for (var i = 0; i < pts.Count; i++)
        {
            var which = i == 0 ? start : i == pts.Count - 1 ? end : mid;
            if (shape.Tag == "path" && (i == 1 || i == 2)) continue;
            if (which == null) continue;
            SvgShape? marker = null;
            foreach (var s in shape.Owner.Shapes) if (s.Tag == "marker" && s.Attr("id") == which) { marker = s; break; }
            if (marker?.Children == null || marker.Children.Count == 0) continue;
            var inDir = i > 0 ? pts[i] - pts[i - 1] : pts[1] - pts[0];
            var outDir = i + 1 < pts.Count ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
            var angle = Mathf.Atan2(inDir.y + outDir.y, inDir.x + outDir.x) * Mathf.Rad2Deg;
            var orient = (marker.Attr("orient") ?? "0").Trim().ToLowerInvariant();
            var rot = orient == "auto" ? angle : orient == "auto-start-reverse" ? (i == 0 ? angle + 180f : angle) : Degrees(orient);
            var units = (marker.Attr("markerUnits") ?? "strokeWidth").Trim();
            var k = units == "userSpaceOnUse" ? 1f : strokeW;
            var mw = StyleApplier.Num(marker.Attr("markerWidth") ?? "3");
            var mh = StyleApplier.Num(marker.Attr("markerHeight") ?? "3");
            var vbParts = (marker.Attr("viewBox") ?? string.Empty).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var refX = StyleApplier.Num(marker.Attr("refX") ?? "0");
            var refY = StyleApplier.Num(marker.Attr("refY") ?? "0");
            if (vbParts.Length == 4)
            {
                var vbw = Mathf.Max(0.001f, StyleApplier.Num(vbParts[2])); var vbh = Mathf.Max(0.001f, StyleApplier.Num(vbParts[3]));
                k *= Mathf.Min(mw / vbw, mh / vbh);
                refX -= StyleApplier.Num(vbParts[0]); refY -= StyleApplier.Num(vbParts[1]);
            }
            // vertex in the group's space (uniform fit) or in scene space (baked fit)
            var vx = fit.Bake ? fit.Px(pts[i].x) : pts[i].x;
            var vy = fit.Bake ? fit.Py(pts[i].y) : pts[i].y;
            var scale = k * (fit.Bake ? (fit.Sx + fit.Sy) * 0.5f : 1f);
            ctx.Body.Append(indent).Append("G a=[0,0] t=[").AppendNum(vx).Append(',').AppendNum(vy).Append("] r=").AppendNum(rot).Append(" s=[").AppendNum(scale).Append(',').AppendNum(scale).Append("] {\n");
            ctx.Body.Append(indent).Append("  G t=[").AppendNum(-refX).Append(',').AppendNum(-refY).Append("] {\n");
            foreach (var child in marker.Children)
                EmitShape(ctx, child, indent + "    ", prefix, new Fit(0f, 0f, 1f, 1f, null));
            ctx.Body.Append(indent).Append("  }\n").Append(indent).Append("}\n");
        }
    }

    private static string? MarkerRef(string? v)
    {
        if (v == null) return null;
        var u = v.IndexOf("#", StringComparison.Ordinal);
        if (u < 0) return null;
        var close = v.IndexOf(')', u);
        return v.Substring(u + 1, (close < 0 ? v.Length : close) - u - 1).Trim();
    }

    /// <summary>
    /// SVG text as a T: x/y are the baseline start, text-anchor sets the alignment and where
    /// the box sits, the font size is in viewBox units (scaled with the fit), a tspan with
    /// its own x/y is a line break.
    /// </summary>
    private static void SvgTextNode(Ctx ctx, SvgShape shape, StringBuilder sb, string prefix, Fit fit, string? opacity)
    {
        var text = shape.Attr("__text") ?? string.Empty;
        var size = StyleApplier.Num(shape.Attr("font-size") ?? "16");
        var ps = fit.Bake ? size * (fit.Sx + fit.Sy) * 0.5f : size;
        var lines = text.Split('\n');
        var longest = 0;
        foreach (var l in lines) longest = Math.Max(longest, l.Length);
        var width = longest * ps * 0.6f + ps;
        var height = ps * 1.25f * lines.Length;
        var anchor = (shape.Attr("text-anchor") ?? "start").Trim().ToLowerInvariant();
        var bx = fit.Bake ? fit.Px(StyleApplier.Num(shape.Attr("x") ?? "0")) : StyleApplier.Num(shape.Attr("x") ?? "0");
        var by = fit.Bake ? fit.Py(StyleApplier.Num(shape.Attr("y") ?? "0")) : StyleApplier.Num(shape.Attr("y") ?? "0");
        var left = anchor == "middle" ? bx - width * 0.5f : anchor == "end" ? bx - width : bx;
        var baseline = (shape.Attr("dominant-baseline") ?? "auto").Trim().ToLowerInvariant();
        var top = baseline is "middle" or "central" ? by - ps * 0.6f : baseline is "hanging" or "text-before-edge" ? by : by - ps * 0.95f;
        var esc = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", string.Empty).Replace("\n", "\\n");
        if (float.TryParse(esc.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) esc = "<noparse>" + esc + "</noparse>";
        sb.Append("T x=").AppendNum(left).Append(" y=").AppendNum(top).Append(" w=").AppendNum(width).Append(" h=").AppendNum(height).Append(" text=\"").Append(esc).Append("\" size=").AppendNum(ps);
        var fill = shape.Attr("fill") ?? "black";
        sb.Append(" f=").Append(Paint(fill, prefix));
        var fo = Mul(shape.Attr("fill-opacity"), opacity);
        if (fo != null) sb.Append(" fo=").Append(fo);
        if (shape.Attr("font-family") is { } fam)
        {
            var first = fam.Split(',')[0].Trim().Trim('"', '\'');
            if (first.Length > 0) sb.Append(" font=\"").Append(IsGeneric(first) ? StyleApplier.MapGeneric(first) ?? first : FontLibrary.ResolveFace(first)).Append('"');
        }
        var weight = (shape.Attr("font-weight") ?? "normal").Trim().ToLowerInvariant();
        if (weight == "bold" || weight == "bolder" || (StyleApplier.IsNumber(weight) && StyleApplier.Num(weight) >= 600)) sb.Append(" weight=bold");
        if (anchor == "middle") sb.Append(" align=center"); else if (anchor == "end") sb.Append(" align=right");
        sb.Append(" valign=top");
        if (shape.Attr("letter-spacing") is { } ls && ps > 0f) sb.Append(" cspace=").AppendNum(StyleApplier.Num(ls) / size * 100f);
        if (shape.Attr("id") is { } sid) sb.Append(" id=").Append(sid);
    }

    /// <summary>
    /// SVG gradient element to a scene def. Default units are objectBoundingBox, which is
    /// the vector layer's `units=bbox`; userSpaceOnUse is viewBox space, which is the
    /// local space of the svg group, so the coordinates pass straight through.
    /// </summary>
    private static void SvgGradient(Ctx ctx, SvgShape g, string prefix)
    {
        var id = g.Attr("id");
        if (id == null) return;
        var stops = new StringBuilder();
        var count = 0;
        foreach (var stop in (g.Attr("stops") ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = stop.Split('|');
            if (parts.Length < 2 || !StyleApplier.TryColor(parts[1], out var c)) continue;
            if (parts.Length > 2 && parts[2].Length > 0) c.a *= StyleApplier.Num(parts[2]);
            if (count > 0) stops.Append(',');
            stops.Append('[').AppendNum(Fraction(parts[0])).Append(',').AppendHex(c).Append(']');
            count++;
        }
        if (count == 0) return;
        var bbox = !string.Equals(g.Attr("gradientUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
        var units = bbox ? " units=bbox" : string.Empty;
        if (g.Tag == "radialGradient")
        {
            var cx = g.Attr("cx") ?? "50%"; var cy = g.Attr("cy") ?? "50%"; var r = g.Attr("r") ?? "50%";
            ctx.Defs.Append("  GR id=").Append(prefix).Append(id).Append(units)
                .Append(" cx=").AppendNum(Fraction(cx)).Append(" cy=").AppendNum(Fraction(cy)).Append(" r=").AppendNum(Fraction(r));
            if (g.Attr("fx") != null) ctx.Defs.Append(" fx=").AppendNum(Fraction(g.Attr("fx")!));
            if (g.Attr("fy") != null) ctx.Defs.Append(" fy=").AppendNum(Fraction(g.Attr("fy")!));
        }
        else
        {
            ctx.Defs.Append("  GL id=").Append(prefix).Append(id).Append(units)
                .Append(" x1=").AppendNum(Fraction(g.Attr("x1") ?? "0")).Append(" y1=").AppendNum(Fraction(g.Attr("y1") ?? "0"))
                .Append(" x2=").AppendNum(Fraction(g.Attr("x2") ?? "100%")).Append(" y2=").AppendNum(Fraction(g.Attr("y2") ?? "0"));
        }
        ctx.Defs.Append(" stops=[").Append(stops).Append("]\n");
    }

    /// <summary>"50%" is 0.5; a bare number is itself.</summary>
    private static float Fraction(string v)
    {
        v = v.Trim();
        return v.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(v) / 100f : StyleApplier.Num(v);
    }

    /// <summary>An SVG attribute value: a number, or an expression passed through untouched.</summary>
    private static string A(SvgShape s, string name)
    {
        var v = s.Attr(name) ?? "0";
        return Expr(v);
    }

    private static string Expr(string v)
    {
        v = v.Trim();
        if (v.StartsWith("=", StringComparison.Ordinal))
            return v.IndexOf(' ') >= 0 ? "\"" + v + "\"" : v;
        return F(StyleApplier.Num(v));
    }

    private static string? Mul(string? a, string? b)
    {
        if (a == null && b == null) return null;
        // An expression stays an expression; a plain `opacity` multiplies into it.
        if (a != null && a.TrimStart().StartsWith("=", StringComparison.Ordinal))
            return b == null ? Expr(a) : Expr("=(" + a.Trim().Substring(1) + ")*" + F(StyleApplier.Num(b)));
        if (b != null && b.TrimStart().StartsWith("=", StringComparison.Ordinal))
            return a == null ? Expr(b) : Expr("=(" + b.Trim().Substring(1) + ")*" + F(StyleApplier.Num(a)));
        var fa = a == null ? 1f : StyleApplier.Num(a);
        var fb = b == null ? 1f : StyleApplier.Num(b);
        return F(fa * fb);
    }

    private static string Paint(string v, string prefix = "")
    {
        v = v.Trim();
        if (v == "none" || v == "transparent") return "none";
        if (v.StartsWith("$", StringComparison.Ordinal)) return v;
        if (v.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return "@" + prefix + v.Substring(4).TrimEnd(')').Trim().TrimStart('#');
        return StyleApplier.TryColor(v, out var c) ? Hex(c) : "#FF00FF";
    }

    // ---------------------------------------------------------------- paint helpers

    private static StringBuilder AppendRadius(this StringBuilder sb, OffThread.Box rs, float w, float h, float inset = 0f, bool keep = false)
    {
        // `keep` for an element a script drives: the radii of a box with no height clamp to nothing,
        // so the key is dropped and there is no slot for the radius to return through once the box
        // grows. A bar animating up from 0% would be square for ever.
        if (!Radii(rs, w, h, inset, out var tl, out var tr, out var br, out var bl))
            return keep ? sb.Append(" rx=0") : sb;
        if (Mathf.Approximately(tl, tr) && Mathf.Approximately(tl, br) && Mathf.Approximately(tl, bl))
            return sb.Append(" rx=").AppendNum(tl);
        return sb.Append(" rx=[").AppendNum(tl).Append(',').AppendNum(tr).Append(',').AppendNum(br).Append(',').AppendNum(bl).Append(']');
    }

    private static string Radius(OffThread.Box rs, float w, float h, float inset = 0f)
    {
        if (!Radii(rs, w, h, inset, out var tl0, out var tr0, out var br0, out var bl0)) return string.Empty;
        if (Mathf.Approximately(tl0, tr0) && Mathf.Approximately(tl0, br0) && Mathf.Approximately(tl0, bl0))
            return " rx=" + F(tl0);
        return " rx=[" + F(tl0) + "," + F(tr0) + "," + F(br0) + "," + F(bl0) + "]";
    }

    /// <summary>The four corner radii, CSS-clamped; false when the box is square.</summary>
    private static bool Radii(OffThread.Box rs, float w, float h, float inset, out float tl, out float tr, out float br, out float bl)
    {
        tl = rs.borderTopLeftRadius;
        tr = rs.borderTopRightRadius;
        br = rs.borderBottomRightRadius;
        bl = rs.borderBottomLeftRadius;
        // CSS: when adjacent radii overflow a side, all radii scale down together. A
        // rounded rect whose radius exceeds half its height is self-intersecting, and the
        // tessellator says so in the log for every frame of a bar growing from 0%.
        var f = 1f;
        if (tl + tr > w && tl + tr > 0f) f = Mathf.Min(f, w / (tl + tr));
        if (bl + br > w && bl + br > 0f) f = Mathf.Min(f, w / (bl + br));
        if (tl + bl > h && tl + bl > 0f) f = Mathf.Min(f, h / (tl + bl));
        if (tr + br > h && tr + br > 0f) f = Mathf.Min(f, h / (tr + br));
        tl = Mathf.Max(0f, tl * f + inset);
        tr = Mathf.Max(0f, tr * f + inset);
        br = Mathf.Max(0f, br * f + inset);
        bl = Mathf.Max(0f, bl * f + inset);
        return !(tl <= 0.01f && tr <= 0.01f && br <= 0.01f && bl <= 0.01f);
    }

    /// <summary>
    /// A checkbox or radio as a browser draws one: unchecked, a ring or rounded box stroked in
    /// the text colour; checked, filled with the accent (`accent-color`, else the page's link
    /// blue) with a white tick or dot. The click region is the background rect emitted above.
    /// </summary>
    private static void EmitCheck(Ctx ctx, string control, HtmlNode node, Dictionary<string, string> css, OffThread.Box rs, float x, float y, float w, float h, string indent)
    {
        if (control is "progress" or "meter")
        {
            EmitBar(ctx, control, node, css, rs, x, y, w, h, indent);
            return;
        }
        // appearance: none, the browser way to restyle a control: the page's own
        // background, border and :checked rules draw it, and nothing is added here.
        if ((css.TryGetValue("appearance", out var ap) || css.TryGetValue("-webkit-appearance", out ap)) && ap.Trim() == "none")
            return;
        var on = node.Attr("checked") != null;
        var accent = css.TryGetValue("accent-color", out var ac) && StyleApplier.TryColor(ac.Trim(), out var a) ? a : new Color(0.31f, 0.63f, 1f);
        var ink = rs.color;
        var size = Mathf.Min(w, h);
        var cx = x + w * 0.5f;
        var cy = y + h * 0.5f;
        var left = cx - size * 0.5f;
        var top = cy - size * 0.5f;
        var sw = Mathf.Max(1f, size * 0.09f);
        if (control == "radio")
        {
            var r = size * 0.5f - sw * 0.5f;
            if (on)
            {
                ctx.Body.Append(indent).Append("C cx=").AppendNum(cx).Append(" cy=").AppendNum(cy).Append(" rx=").AppendNum(size * 0.5f).Append(" ry=").AppendNum(size * 0.5f).Append(" f=").AppendHex(accent).Append('\n');
                ctx.Body.Append(indent).Append("C cx=").AppendNum(cx).Append(" cy=").AppendNum(cy).Append(" rx=").AppendNum(size * 0.2f).Append(" ry=").AppendNum(size * 0.2f).Append(" f=#FFFFFF\n");
                ctx.Out.Nodes += 2;
            }
            else
            {
                ctx.Body.Append(indent).Append("C cx=").AppendNum(cx).Append(" cy=").AppendNum(cy).Append(" rx=").AppendNum(r).Append(" ry=").AppendNum(r).Append(" f=none s=").AppendHex(ink).Append(" sw=").AppendNum(sw).Append('\n');
                ctx.Out.Nodes++;
            }
            return;
        }
        var rx = Mathf.Max(0f, rs.borderTopLeftRadius > 0.01f ? rs.borderTopLeftRadius : size * 0.18f);
        if (on)
        {
            ctx.Body.Append(indent).Append("R x=").AppendNum(left).Append(" y=").AppendNum(top).Append(" w=").AppendNum(size).Append(" h=").AppendNum(size).Append(" rx=").AppendNum(rx).Append(" f=").AppendHex(accent).Append('\n');
            // The tick: two strokes from the left third, down to the bottom, up to the top right.
            var d = "M" + F(left + size * 0.24f) + " " + F(top + size * 0.52f) + " L" + F(left + size * 0.43f) + " " + F(top + size * 0.72f) + " L" + F(left + size * 0.78f) + " " + F(top + size * 0.3f);
            ctx.Body.Append(indent).Append("P d=\"").Append(d).Append("\" f=none s=#FFFFFF sw=").AppendNum(Mathf.Max(1.2f, size * 0.13f)).Append(" cap=round join=round\n");
            ctx.Out.Nodes += 2;
        }
        else
        {
            ctx.Body.Append(indent).Append("R x=").AppendNum(left + sw * 0.5f).Append(" y=").AppendNum(top + sw * 0.5f).Append(" w=").AppendNum(size - sw).Append(" h=").AppendNum(size - sw).Append(" rx=").AppendNum(Mathf.Max(0f, rx - sw * 0.5f)).Append(" f=none s=").AppendHex(ink).Append(" sw=").AppendNum(sw).Append('\n');
            ctx.Out.Nodes++;
        }
    }

    private static bool Scrolls(Dictionary<string, string> css)
    {
        return (css.TryGetValue("overflow", out var o) || css.TryGetValue("overflow-y", out o)) && o.Trim() is "auto" or "scroll";
    }

    /// <summary>progress and meter: a rounded track in the box's background (else a dim grey) and a fill in the accent, or the meter's low/high colour.</summary>
    private static void EmitBar(Ctx ctx, string control, HtmlNode node, Dictionary<string, string> css, OffThread.Box rs, float x, float y, float w, float h, string indent)
    {
        float Attr(string name, float fallback) => node.Attr(name) is { } v && float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;
        var min = control == "meter" ? Attr("min", 0f) : 0f;
        var max = Attr("max", control == "meter" ? 1f : 1f);
        var value = Attr("value", float.NaN);
        var frac = float.IsNaN(value) || max <= min ? 0f : Mathf.Clamp01((value - min) / (max - min));
        var accent = css.TryGetValue("accent-color", out var ac) && StyleApplier.TryColor(ac.Trim(), out var a) ? a : new Color(0.31f, 0.63f, 1f);
        if (control == "meter" && !float.IsNaN(value))
        {
            // green in the optimum region, amber between, red past low/high; the browser's three colours
            var low = Attr("low", min);
            var high = Attr("high", max);
            var optimum = Attr("optimum", (min + max) * 0.5f);
            var inOpt = optimum <= low ? value <= low : optimum >= high ? value >= high : value >= low && value <= high;
            var far = optimum <= low ? value > high : optimum >= high ? value < low : false;
            accent = inOpt ? new Color(0.18f, 0.55f, 0.43f) : far ? new Color(0.71f, 0.21f, 0.17f) : new Color(0.89f, 0.66f, 0.31f);
        }
        var track = rs.backgroundColor.a > 0.002f ? rs.backgroundColor : new Color(0.14f, 0.19f, 0.29f);
        var rx = rs.borderTopLeftRadius > 0.01f ? rs.borderTopLeftRadius : h * 0.5f;
        ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendNum(w).Append(" h=").AppendNum(h).AppendRadius(rs, w, h, 0f, false).Append(rs.borderTopLeftRadius > 0.01f ? string.Empty : " rx=" + F(rx)).Append(" f=").AppendHex(track).Append('\n');
        ctx.Out.Nodes++;
        if (float.IsNaN(value) && control == "progress")
        {
            // indeterminate: a third of the bar sliding back and forth
            ctx.Body.Append(indent).Append("R x==").AppendNum(x).Append('+').AppendNum(w * 0.67f).Append("*(0.5-0.5*cos(t*3)) y=").AppendNum(y).Append(" w=").AppendNum(w * 0.33f).Append(" h=").AppendNum(h).Append(" rx=").AppendNum(rx).Append(" f=").AppendHex(accent).Append('\n');
            ctx.Out.Nodes++;
            return;
        }
        if (frac > 0.001f)
        {
            ctx.Body.Append(indent).Append("R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendNum(Mathf.Max(h, w * frac)).Append(" h=").AppendNum(h).Append(" rx=").AppendNum(rx).Append(" f=").AppendHex(accent).Append('\n');
            ctx.Out.Nodes++;
        }
    }

    // ---------------------------------------------------------------- canvas: recorded 2D ops to nodes

    /// <summary>
    /// A recorded canvas frame as vector nodes: paths become P, rects R, text T, images IMG,
    /// gradients GL/GR/GC defs, clip() a CP group, and the transform stack is baked into the
    /// coordinates. Canvas units map onto the element's box (the width/height attributes
    /// against the layout size), as a browser scales its bitmap. Per-pixel calls have no
    /// equivalent. A frame that repaints every animation frame re-emits the scene at 30 Hz.
    /// </summary>
    private static void EmitCanvas(Ctx ctx, CanvasElement cv, Dictionary<string, string> css, float x, float y, float w, float h, string indent)
    {
        var cmds = cv.Commands;
        var n = cv.Count;
        if (cmds == null || n == 0 || cv.CanvasWidth <= 0f || cv.CanvasHeight <= 0f) return;
        var strings = cv.Strings;
        var sx = w / cv.CanvasWidth;
        var sy = h / cv.CanvasHeight;

        // transform stack: current matrix maps canvas space to canvas space; page = box + scaled
        var m = new[] { 1f, 0f, 0f, 1f, 0f, 0f };
        var stack = new Stack<(float[] m, int groups, string shadow, string dash, string rule)>();
        var groups = 0;         // open G clip groups at this level
        var shadow = string.Empty;
        var dash = string.Empty;
        var rule = "nonzero";
        var path = new StringBuilder();
        var hasCurrent = false;
        float curX = 0f, curY = 0f, startX = 0f, startY = 0f;

        Vector2 P(float px, float py)
        {
            var tx = m[0] * px + m[2] * py + m[4];
            var ty = m[1] * px + m[3] * py + m[5];
            return new Vector2(x + tx * sx, y + ty * sy);
        }
        float Scale() => Mathf.Sqrt(Mathf.Abs(m[0] * m[3] - m[1] * m[2])) * (sx + sy) * 0.5f;
        void Mul(float a, float b, float c, float d, float e, float f)
        {
            m = new[] { m[0] * a + m[2] * b, m[1] * a + m[3] * b, m[0] * c + m[2] * d, m[1] * c + m[3] * d, m[0] * e + m[2] * f + m[4], m[1] * e + m[3] * f + m[5] };
        }
        string Str(int idx) => idx >= 0 && idx < strings.Count ? strings[idx] : string.Empty;
        string Ind() => indent + new string(' ', groups * 2);
        void MoveTo(float px, float py) { var p = P(px, py); path.Append('M').AppendNum(p.x).Append(' ').AppendNum(p.y).Append(' '); curX = px; curY = py; startX = px; startY = py; hasCurrent = true; }
        void LineTo(float px, float py) { if (!hasCurrent) { MoveTo(px, py); return; } var p = P(px, py); path.Append('L').AppendNum(p.x).Append(' ').AppendNum(p.y).Append(' '); curX = px; curY = py; }
        void ArcSeg(float cx, float cy, float rx, float ry, float rot, float a0, float a1, bool ccw)
        {
            // canvas arc: a line from the current point to the arc start, then the arc; a full turn is two halves
            var sweep = ccw ? -1f : 1f;
            var delta = a1 - a0;
            if (!ccw && delta < 0f) delta += Mathf.PI * 2f * Mathf.Ceil(-delta / (Mathf.PI * 2f));
            if (ccw && delta > 0f) delta -= Mathf.PI * 2f * Mathf.Ceil(delta / (Mathf.PI * 2f));
            if (Mathf.Abs(delta) >= Mathf.PI * 2f - 1e-4f) delta = sweep * (Mathf.PI * 2f - 1e-3f);
            Vector2 On(float a)
            {
                var ex = rx * Mathf.Cos(a); var ey = ry * Mathf.Sin(a);
                var cr = Mathf.Cos(rot); var sr = Mathf.Sin(rot);
                return new Vector2(cx + ex * cr - ey * sr, cy + ex * sr + ey * cr);
            }
            var s0 = On(a0);
            if (hasCurrent) LineTo(s0.x, s0.y); else MoveTo(s0.x, s0.y);
            var steps = Mathf.Abs(delta) > Mathf.PI ? 2 : 1;
            for (var k = 1; k <= steps; k++)
            {
                var a = a0 + delta * k / steps;
                var e = On(a);
                var pe = P(e.x, e.y);
                // radii in page units: scale by the box; a rotation under a non-uniform transform is approximate
                var prx = rx * sx * Mathf.Sqrt(Mathf.Abs(m[0] * m[3] - m[1] * m[2]));
                var pry = ry * sy * Mathf.Sqrt(Mathf.Abs(m[0] * m[3] - m[1] * m[2]));
                var large = Mathf.Abs(delta / steps) > Mathf.PI ? 1 : 0;
                path.Append('A').AppendNum(prx).Append(' ').AppendNum(pry).Append(' ').AppendNum(rot * Mathf.Rad2Deg).Append(' ').Append(large).Append(' ').Append(ccw ? 0 : 1).Append(' ').AppendNum(pe.x).Append(' ').AppendNum(pe.y).Append(' ');
                curX = e.x; curY = e.y;
            }
        }
        string Paint(int idx, out bool ok)
        {
            // a colour, or a gradient recorded as GL|x0|y0|x1|y1|off:col;... (canvas units), GR|x0|y0|r0|x1|y1|r1|..., GC|x|y|a|...
            var s = Str(idx);
            ok = true;
            if (s.StartsWith("GL|", StringComparison.Ordinal) || s.StartsWith("GR|", StringComparison.Ordinal) || s.StartsWith("GC|", StringComparison.Ordinal))
            {
                var parts = s.Split('|');
                var stops = new StringBuilder();
                foreach (var st in parts[parts.Length - 1].Split(';'))
                {
                    var colon = st.IndexOf(':');
                    if (colon < 0 || !StyleApplier.TryColor(st.Substring(colon + 1), out var sc)) continue;
                    if (stops.Length > 0) stops.Append(',');
                    stops.Append('[').AppendNum(StyleApplier.Num(st.Substring(0, colon))).Append(',').AppendHex(sc).Append(']');
                }
                var id = ctx.NextId("cg");
                float N(int i) => i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
                if (s[1] == 'L')
                {
                    var a = P(N(1), N(2)); var b = P(N(3), N(4));
                    ctx.Defs.Append("  GL id=").Append(id).Append(" x1=").AppendNum(a.x).Append(" y1=").AppendNum(a.y).Append(" x2=").AppendNum(b.x).Append(" y2=").AppendNum(b.y).Append(" stops=[").Append(stops).Append("]\n");
                }
                else if (s[1] == 'R')
                {
                    var c1 = P(N(4), N(5)); var f0 = P(N(1), N(2));
                    ctx.Defs.Append("  GR id=").Append(id).Append(" cx=").AppendNum(c1.x).Append(" cy=").AppendNum(c1.y).Append(" r=").AppendNum(N(6) * Scale()).Append(" fx=").AppendNum(f0.x).Append(" fy=").AppendNum(f0.y).Append(" stops=[").Append(stops).Append("]\n");
                }
                else
                {
                    var c1 = P(N(1), N(2));
                    ctx.Defs.Append("  GC id=").Append(id).Append(" cx=").AppendNum(c1.x).Append(" cy=").AppendNum(c1.y).Append(" a=").AppendNum(N(3) * Mathf.Rad2Deg).Append(" stops=[").Append(stops).Append("]\n");
                }
                return "@" + id;
            }
            if (StyleApplier.TryColor(s, out var col)) return Hex(col);
            ok = false;
            return "#FF00FF";
        }
        string Shadow(int idx)
        {
            var s = Str(idx);
            if (!s.StartsWith("SH|", StringComparison.Ordinal)) return string.Empty;
            var p = s.Split('|');
            if (p.Length < 5 || !StyleApplier.TryColor(p[4], out var c) || c.a <= 0.002f) return string.Empty;
            var k = Scale();
            return " sh=[[" + F(StyleApplier.Num(p[1]) * k) + "," + F(StyleApplier.Num(p[2]) * k) + "," + F(StyleApplier.Num(p[3]) * k) + ",0," + Hex(c) + "]]";
        }
        string DashAttr(float lw)
        {
            if (dash.Length == 0) return string.Empty;
            var k = Scale();
            var parts = dash.Split(',');
            var sb = new StringBuilder(" dash=[");
            for (var i = 0; i < parts.Length; i++) { if (i > 0) sb.Append(','); sb.AppendNum(StyleApplier.Num(parts[i]) * k); }
            return sb.Append(']').ToString();
        }
        static string Cap(float c) => c switch { 2 => "round", 1 => "square", _ => "butt" };
        static string Join(float j) => j switch { 2 => "round", 1 => "bevel", _ => "miter" };
        void CanvasText(int textIdx, float tx, float ty, float maxW, int fontIdx, int alignIdx, int baseIdx, string fill, float alpha, string stroke, float lw)
        {
            var text = Str(textIdx);
            if (text.Length == 0) return;
            // font: "[italic] [bold|600] 14px Family"
            var font = Str(fontIdx);
            var size = 10f; var family = string.Empty; var bold = false; var italic = false;
            foreach (var part in font.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.EndsWith("px", StringComparison.OrdinalIgnoreCase) && StyleApplier.IsNumber(part.Substring(0, part.Length - 2))) { size = StyleApplier.Num(part); family = string.Empty; continue; }
                if (part == "bold" || part == "bolder" || (StyleApplier.IsNumber(part) && StyleApplier.Num(part) >= 600)) { bold = true; continue; }
                if (part == "italic" || part == "oblique") { italic = true; continue; }
                if (part == "normal" || StyleApplier.IsNumber(part)) continue;
                family = family.Length == 0 ? part : family + " " + part;
            }
            family = family.Trim().Trim('"', '\'').Split(',')[0].Trim();
            var k = Scale();
            var ps = size * k;
            var width = text.Length * ps * 0.6f + ps;
            if (maxW > 0f) width = Mathf.Min(width, maxW * k + ps * 0.5f);
            var align = Str(alignIdx);
            var baseline = Str(baseIdx);
            var p = P(tx, ty);
            var left = align is "center" ? p.x - width * 0.5f : align is "right" or "end" ? p.x - width : p.x;
            var top = baseline switch { "top" or "hanging" => p.y, "middle" => p.y - ps * 0.6f, "bottom" or "ideographic" => p.y - ps * 1.2f, _ => p.y - ps * 0.95f };
            var esc = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
            if (italic) esc = "<i>" + esc + "</i>";
            if (float.TryParse(esc.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) esc = "<noparse>" + esc + "</noparse>";
            var sb = new StringBuilder(Ind());
            sb.Append("T x=").AppendNum(left).Append(" y=").AppendNum(top).Append(" w=").AppendNum(width).Append(" h=").AppendNum(ps * 1.25f).Append(" text=\"").Append(esc).Append("\" size=").AppendNum(ps);
            if (stroke.Length > 0) sb.Append(" f=").Append(stroke); else sb.Append(" f=").Append(fill);
            if (alpha < 0.999f) sb.Append(" fo=").AppendNum(alpha);
            if (family.Length > 0 && !IsGeneric(family)) sb.Append(" font=\"").Append(FontLibrary.ResolveFace(family)).Append('"');
            else if (family.Length > 0 && StyleApplier.MapGeneric(family) is { } g) sb.Append(" font=\"").Append(g).Append('"');
            if (bold && !NamedWeight(family)) sb.Append(" weight=bold");
            if (align is "center") sb.Append(" align=center"); else if (align is "right" or "end") sb.Append(" align=right");
            sb.Append(" valign=top");
            if (shadow.Length > 0) sb.Append(shadow);
            // the transform's rotation turns the label about its anchor, the way a T rotates with its group (translate and scale are already in p and ps)
            var rot = Mathf.Atan2(m[1], m[0]) * Mathf.Rad2Deg;
            if (Mathf.Abs(rot) > 0.01f)
                ctx.Body.Append(Ind()).Append("G a=[").AppendNum(p.x).Append(',').AppendNum(p.y).Append("] r=").AppendNum(rot).Append(" { ").Append(sb.ToString().TrimStart()).Append(" }\n");
            else
                ctx.Body.Append(sb).Append('\n');
            ctx.Out.Nodes++;
        }

        var i = 0;
        while (i < n)
        {
            var op = (int)cmds[i++];
            switch (op)
            {
                case CanvasElement.OpBeginPath: path.Clear(); hasCurrent = false; break;
                case CanvasElement.OpMoveTo: MoveTo(cmds[i], cmds[i + 1]); i += 2; break;
                case CanvasElement.OpLineTo: LineTo(cmds[i], cmds[i + 1]); i += 2; break;
                case CanvasElement.OpQuadTo:
                {
                    if (!hasCurrent) MoveTo(cmds[i], cmds[i + 1]);
                    var c1 = P(cmds[i], cmds[i + 1]); var e = P(cmds[i + 2], cmds[i + 3]);
                    path.Append('Q').AppendNum(c1.x).Append(' ').AppendNum(c1.y).Append(' ').AppendNum(e.x).Append(' ').AppendNum(e.y).Append(' ');
                    curX = cmds[i + 2]; curY = cmds[i + 3]; i += 4; break;
                }
                case CanvasElement.OpCubicTo:
                {
                    if (!hasCurrent) MoveTo(cmds[i], cmds[i + 1]);
                    var c1 = P(cmds[i], cmds[i + 1]); var c2 = P(cmds[i + 2], cmds[i + 3]); var e = P(cmds[i + 4], cmds[i + 5]);
                    path.Append('C').AppendNum(c1.x).Append(' ').AppendNum(c1.y).Append(' ').AppendNum(c2.x).Append(' ').AppendNum(c2.y).Append(' ').AppendNum(e.x).Append(' ').AppendNum(e.y).Append(' ');
                    curX = cmds[i + 4]; curY = cmds[i + 5]; i += 6; break;
                }
                case CanvasElement.OpArc: ArcSeg(cmds[i], cmds[i + 1], cmds[i + 2], cmds[i + 2], 0f, cmds[i + 3], cmds[i + 4], cmds[i + 5] > 0.5f); i += 6; break;
                case CanvasElement.OpClosePath: if (hasCurrent) { path.Append("Z "); curX = startX; curY = startY; } break;
                case CanvasElement.OpFill:
                {
                    var fill = Paint((int)cmds[i], out _); var alpha = cmds[i + 1]; i += 2;
                    if (path.Length > 0)
                    {
                        ctx.Body.Append(Ind()).Append("P d=\"").Append(path.ToString().TrimEnd()).Append("\" f=").Append(fill);
                        if (alpha < 0.999f) ctx.Body.Append(" fo=").AppendNum(alpha);
                        if (rule == "evenodd") ctx.Body.Append(" fr=evenodd");
                        ctx.Body.Append(shadow).Append('\n');
                        ctx.Out.Nodes++;
                    }
                    break;
                }
                case CanvasElement.OpStroke:
                {
                    var col = Paint((int)cmds[i], out _); var alpha = cmds[i + 1]; var lw = cmds[i + 2] * Scale(); var cap = Cap(cmds[i + 3]); var join = Join(cmds[i + 4]); i += 5;
                    if (path.Length > 0)
                    {
                        ctx.Body.Append(Ind()).Append("P d=\"").Append(path.ToString().TrimEnd()).Append("\" f=none s=").Append(col).Append(" sw=").AppendNum(Mathf.Max(0.5f, lw)).Append(" cap=").Append(cap).Append(" join=").Append(join);
                        if (alpha < 0.999f) ctx.Body.Append(" so=").AppendNum(alpha);
                        ctx.Body.Append(DashAttr(lw)).Append(shadow).Append('\n');
                        ctx.Out.Nodes++;
                    }
                    break;
                }
                case CanvasElement.OpFillRect:
                case 12:
                {
                    var rx = cmds[i]; var ry = cmds[i + 1]; var rw = cmds[i + 2]; var rh = cmds[i + 3];
                    var col = Paint((int)cmds[i + 4], out _); var alpha = cmds[i + 5];
                    var isStroke = op == 12;
                    var lw = isStroke ? cmds[i + 6] * Scale() : 0f;
                    var cap = isStroke ? Cap(cmds[i + 7]) : "butt"; var join = isStroke ? Join(cmds[i + 8]) : "miter";
                    i += isStroke ? 9 : 6;
                    var axisAligned = Mathf.Abs(m[1]) < 1e-4f && Mathf.Abs(m[2]) < 1e-4f;
                    if (axisAligned)
                    {
                        var a = P(rx, ry); var b = P(rx + rw, ry + rh);
                        var left = Mathf.Min(a.x, b.x); var top = Mathf.Min(a.y, b.y);
                        ctx.Body.Append(Ind()).Append("R x=").AppendNum(left).Append(" y=").AppendNum(top).Append(" w=").AppendNum(Mathf.Abs(b.x - a.x)).Append(" h=").AppendNum(Mathf.Abs(b.y - a.y));
                    }
                    else
                    {
                        var p0 = P(rx, ry); var p1 = P(rx + rw, ry); var p2 = P(rx + rw, ry + rh); var p3 = P(rx, ry + rh);
                        ctx.Body.Append(Ind()).Append("Y p=[").AppendNum(p0.x).Append(',').AppendNum(p0.y).Append(',').AppendNum(p1.x).Append(',').AppendNum(p1.y).Append(',').AppendNum(p2.x).Append(',').AppendNum(p2.y).Append(',').AppendNum(p3.x).Append(',').AppendNum(p3.y).Append(']');
                    }
                    if (isStroke) ctx.Body.Append(" f=none s=").Append(col).Append(" sw=").AppendNum(Mathf.Max(0.5f, lw)).Append(" cap=").Append(cap).Append(" join=").Append(join).Append(DashAttr(lw));
                    else ctx.Body.Append(" f=").Append(col);
                    if (alpha < 0.999f) ctx.Body.Append(isStroke ? " so=" : " fo=").AppendNum(alpha);
                    ctx.Body.Append(shadow).Append('\n');
                    ctx.Out.Nodes++;
                    break;
                }
                case CanvasElement.OpArcTo:
                {
                    // ponytail: the tangent arc as a quadratic through the corner; right for the rounded corners it is used for
                    if (!hasCurrent) MoveTo(cmds[i], cmds[i + 1]);
                    var c1 = P(cmds[i], cmds[i + 1]); var e = P(cmds[i + 2], cmds[i + 3]);
                    path.Append('Q').AppendNum(c1.x).Append(' ').AppendNum(c1.y).Append(' ').AppendNum(e.x).Append(' ').AppendNum(e.y).Append(' ');
                    curX = cmds[i + 2]; curY = cmds[i + 3]; i += 5; break;
                }
                case CanvasElement.OpRect:
                {
                    var rx = cmds[i]; var ry = cmds[i + 1]; var rw = cmds[i + 2]; var rh = cmds[i + 3]; i += 4;
                    MoveTo(rx, ry); LineTo(rx + rw, ry); LineTo(rx + rw, ry + rh); LineTo(rx, ry + rh); path.Append("Z "); curX = rx; curY = ry;
                    break;
                }
                case 13:
                {
                    var keep = shadow; shadow = Shadow((int)cmds[i + 9]);
                    CanvasText((int)cmds[i], cmds[i + 1], cmds[i + 2], cmds[i + 3], (int)cmds[i + 4], (int)cmds[i + 5], (int)cmds[i + 6], Paint((int)cmds[i + 7], out _), cmds[i + 8], string.Empty, 0f);
                    shadow = keep; i += 10; break;
                }
                case 14: CanvasText((int)cmds[i], cmds[i + 1], cmds[i + 2], cmds[i + 3], (int)cmds[i + 4], (int)cmds[i + 5], (int)cmds[i + 6], "#000000", cmds[i + 8], Paint((int)cmds[i + 7], out _), cmds[i + 9]); i += 10; break;
                case 15: Mul(1, 0, 0, 1, cmds[i], cmds[i + 1]); i += 2; break;
                case 16: { var r = cmds[i]; Mul(Mathf.Cos(r), Mathf.Sin(r), -Mathf.Sin(r), Mathf.Cos(r), 0, 0); i += 1; break; }
                case 17: Mul(cmds[i], 0, 0, cmds[i + 1], 0, 0); i += 2; break;
                case 18: m = new[] { cmds[i], cmds[i + 1], cmds[i + 2], cmds[i + 3], cmds[i + 4], cmds[i + 5] }; i += 6; break;
                case 19: Mul(cmds[i], cmds[i + 1], cmds[i + 2], cmds[i + 3], cmds[i + 4], cmds[i + 5]); i += 6; break;
                case 20: m = new[] { 1f, 0f, 0f, 1f, 0f, 0f }; break;
                case 21: stack.Push(((float[])m.Clone(), groups, shadow, dash, rule)); break;
                case 22:
                {
                    if (stack.Count == 0) break;
                    var (pm, pg, psh, pd, pr) = stack.Pop();
                    while (groups > pg) { groups--; ctx.Body.Append(Ind()).Append("}\n"); }
                    m = pm; shadow = psh; dash = pd; rule = pr;
                    break;
                }
                case 23:
                {
                    // clip(): the current path as a CP; the rest of this save level goes inside a clip group
                    i += 1;
                    if (path.Length == 0) break;
                    var id = ctx.NextId("cclip");
                    ctx.Defs.Append("  CP id=").Append(id).Append(" { P d=\"").Append(path.ToString().TrimEnd()).Append("\" }\n");
                    ctx.Body.Append(Ind()).Append("G clip=").Append(id).Append(" {\n");
                    groups++;
                    break;
                }
                case 24: ArcSeg(cmds[i], cmds[i + 1], cmds[i + 2], cmds[i + 3], cmds[i + 4], cmds[i + 5], cmds[i + 6], cmds[i + 7] > 0.5f); i += 8; break;
                case 25:
                {
                    var src = Str((int)cmds[i]); var dx = cmds[i + 1]; var dy = cmds[i + 2]; var dw = cmds[i + 3]; var dh = cmds[i + 4]; var alpha = cmds[i + 5]; i += 6;
                    if (src.Length == 0 || dw <= 0f || dh <= 0f) break;
                    var a = P(dx, dy); var b = P(dx + dw, dy + dh);
                    ctx.Body.Append(Ind()).Append("IMG x=").AppendNum(Mathf.Min(a.x, b.x)).Append(" y=").AppendNum(Mathf.Min(a.y, b.y)).Append(" w=").AppendNum(Mathf.Abs(b.x - a.x)).Append(" h=").AppendNum(Mathf.Abs(b.y - a.y)).Append(" src=\"").Append(src.Replace("\"", string.Empty)).Append("\" fit=fill");
                    if (alpha < 0.999f) ctx.Body.Append(" o=").AppendNum(alpha);
                    ctx.Body.Append('\n');
                    ctx.Out.Nodes++;
                    break;
                }
                case 26: dash = Str((int)cmds[i]); i += 1; break;
                case 27:
                {
                    var rx = cmds[i]; var ry = cmds[i + 1]; var rw = cmds[i + 2]; var rh = cmds[i + 3]; var r = Mathf.Min(cmds[i + 4], Mathf.Min(rw, rh) * 0.5f); i += 5;
                    MoveTo(rx + r, ry); LineTo(rx + rw - r, ry);
                    ArcSeg(rx + rw - r, ry + r, r, r, 0f, -Mathf.PI * 0.5f, 0f, false); LineTo(rx + rw, ry + rh - r);
                    ArcSeg(rx + rw - r, ry + rh - r, r, r, 0f, 0f, Mathf.PI * 0.5f, false); LineTo(rx + r, ry + rh);
                    ArcSeg(rx + r, ry + rh - r, r, r, 0f, Mathf.PI * 0.5f, Mathf.PI, false); LineTo(rx, ry + r);
                    ArcSeg(rx + r, ry + r, r, r, 0f, Mathf.PI, Mathf.PI * 1.5f, false); path.Append("Z ");
                    break;
                }
                case 28:
                {
                    // a partial clearRect: painted over with the page background under the canvas, the nearest thing to erasing
                    var rx = cmds[i]; var ry = cmds[i + 1]; var rw = cmds[i + 2]; var rh = cmds[i + 3]; i += 4;
                    var a = P(rx, ry); var b = P(rx + rw, ry + rh);
                    var under = css.TryGetValue("background-color", out var bgc) && StyleApplier.TryColor(bgc, out var bc) ? bc : new Color(0.043f, 0.086f, 0.133f);
                    ctx.Body.Append(Ind()).Append("R x=").AppendNum(Mathf.Min(a.x, b.x)).Append(" y=").AppendNum(Mathf.Min(a.y, b.y)).Append(" w=").AppendNum(Mathf.Abs(b.x - a.x)).Append(" h=").AppendNum(Mathf.Abs(b.y - a.y)).Append(" f=").AppendHex(under).Append('\n');
                    ctx.Out.Nodes++;
                    break;
                }
                case 29: shadow = Shadow((int)cmds[i]); i += 1; break;
                case 30: rule = Str((int)cmds[i]); i += 1; break;
                default:
                    Warn(ctx, $"html: canvas op {op} unknown; frame truncated");
                    i = n;
                    break;
            }
        }
        while (groups > 0) { groups--; ctx.Body.Append(Ind()).Append("}\n"); }
    }

    // ---------------------------------------------------------------- Batch C helpers

    /// <summary>IMG node (vector requirement 9): a picture in scene order with fit and the box's radii.</summary>
    /// <param name="idOf">The element whose id the image carries, or null for none.</param>
    private static void EmitImage(Ctx ctx, string src, string fit, Dictionary<string, string> css, OffThread.Box rs, float x, float y, float w, float h, string indent, VisualElement? idOf)
    {
        var f = fit switch { "cover" => "cover", "contain" or "scale-down" => "contain", _ => "fill" };
        src = HtmlRenderer.ResolveUrl(src, ctx.Built);
        ctx.Body.Append(indent).Append("IMG x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendNum(w).Append(" h=").AppendNum(h)
            .Append(" src=\"").Append(src.Replace("\"", string.Empty)).Append("\" fit=").Append(f).AppendRadius(rs, w, h, 0f, false);
        if (rs.opacity < 0.999f) ctx.Body.Append(" o=").AppendNum(rs.opacity);
        if (idOf != null) ctx.Body.AppendNodeId(ctx, idOf);
        ctx.Body.Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>
    /// text-emphasis: a mark over (or under) every character, as a second label of marks
    /// in the same box. ponytail: the marks are spaced by their own advance, not the glyphs'
    /// under them, so they drift on a proportional face; exact only for monospace text.
    /// </summary>
    private static void EmitEmphasis(Ctx ctx, Dictionary<string, string> css, OffThread.Box rs, string text, string style, float x, float y, float w, float h, string indent)
    {
        var s = style.Trim().ToLowerInvariant();
        var open = s.Contains("open");
        string mark;
        var q = style.IndexOf('"');
        if (q >= 0 && style.LastIndexOf('"') > q) mark = style.Substring(q + 1, style.LastIndexOf('"') - q - 1);
        else if (s.Contains("double-circle")) mark = open ? "\u25CE" : "\u25C9";
        else if (s.Contains("circle")) mark = open ? "\u25CB" : "\u25CF";
        else if (s.Contains("triangle")) mark = open ? "\u25B3" : "\u25B2";
        else if (s.Contains("sesame")) mark = open ? "\uFE46" : "\uFE45";
        else mark = open ? "\u25E6" : "\u2022";
        var plain = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty).Replace("\n", " ");
        if (plain.Trim().Length == 0) return;
        var marks = new StringBuilder(plain.Length);
        foreach (var ch in plain) marks.Append(char.IsWhiteSpace(ch) ? ' ' : mark[0]);
        var under = css.TryGetValue("text-emphasis-position", out var pos) && pos.Contains("under");
        var colour = css.TryGetValue("text-emphasis-color", out var ec) && StyleApplier.TryColor(ec, out var col) ? col : rs.color;
        var size = rs.fontSize * 0.5f;
        var dy = under ? rs.fontSize * 0.6f : -rs.fontSize * 0.6f;
        var line = new StringBuilder(indent);
        line.Append("T x=").AppendNum(x).Append(" y=").AppendNum(y + dy).Append(" w=").AppendNum(w).Append(" h=").AppendNum(h)
            .Append(" text=\"").Append(marks).Append("\" size=").AppendNum(size).Append(" f=").AppendHex(colour).Append(" valign=middle");
        if (css.TryGetValue("text-align", out var ta)) { var t = ta.Trim(); if (t == "center") line.Append(" align=center"); else if (t is "right" or "end") line.Append(" align=right"); }
        ctx.Body.Append(line).Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>url(...) inside a background value, or null.</summary>
    private static string? UrlOf(string css)
    {
        var set = css.IndexOf("image-set(", StringComparison.OrdinalIgnoreCase);
        if (set >= 0)
        {
            // image-set("a.png" 1x, "b.png" 2x): one resolution here, the first candidate
            var inner = css.Substring(set + 10);
            var first = SplitTopLevelCommas(inner.TrimEnd(')'))[0].Trim();
            if (first.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) return UrlOf(first);
            var q = first.IndexOfAny(new[] { '"', '\'' });
            if (q >= 0) { var q2 = first.IndexOf(first[q], q + 1); if (q2 > q) return first.Substring(q + 1, q2 - q - 1); }
            return first.Split(' ')[0];
        }
        var u = css.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (u < 0) return null;
        var close = css.IndexOf(')', u);
        if (close < 0) return null;
        var url = css.Substring(u + 4, close - u - 4).Trim().Trim('"', '\'');
        return url.Length > 0 ? url : null;
    }

    /// <summary>background-size to an IMG fit: cover, contain, "100% 100%" fills, anything else keeps the picture's proportions.</summary>
    private static string BackgroundFit(Dictionary<string, string> css)
    {
        string? bs = null;
        if (css.TryGetValue("background-size", out var explicitSize)) bs = explicitSize;
        else if (css.TryGetValue("background", out var shorthand) && shorthand.IndexOf('/') is var slash && slash >= 0)
            bs = shorthand.Substring(slash + 1).Trim().Split(' ')[0]; // "center / contain no-repeat"
        if (bs == null) return "contain";
        var v = bs.Trim().ToLowerInvariant();
        if (v == "cover") return "cover";
        if (v == "contain" || v == "auto") return "contain";
        return "fill";
    }

    private static string? FirstOfSrcset(string? srcset)
    {
        if (string.IsNullOrEmpty(srcset)) return null;
        var first = srcset!.Split(',')[0].Trim().Split(' ')[0];
        return first.Length > 0 ? first : null;
    }

    /// <summary>conic-gradient([from Adeg] [at x y,] stops) to a GC def (vector requirement 11), bbox units.</summary>
    private static string? ConicDef(Ctx ctx, string css)
    {
        var open = css.IndexOf('(');
        var close = css.LastIndexOf(')');
        if (open < 0 || close < open) return null;
        var args = SplitTopLevelCommas(css.Substring(open + 1, close - open - 1));
        var from = 0f; var cx = 0.5f; var cy = 0.5f;
        if (args.Count > 0 && (args[0].TrimStart().StartsWith("from", StringComparison.OrdinalIgnoreCase) || args[0].TrimStart().StartsWith("at", StringComparison.OrdinalIgnoreCase)))
        {
            var head = args[0].Trim();
            var at = head.IndexOf("at ", StringComparison.OrdinalIgnoreCase);
            if (head.StartsWith("from", StringComparison.OrdinalIgnoreCase))
                from = StyleApplier.Num((at >= 0 ? head.Substring(4, at - 4) : head.Substring(4)).Trim());
            if (at >= 0)
            {
                var pos = head.Substring(at + 3).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                cx = pos.Length > 0 ? Position(pos[0]) : 0.5f;
                cy = pos.Length > 1 ? Position(pos[1]) : 0.5f;
            }
            args.RemoveAt(0);
        }
        var stops = new StringBuilder();
        var n = 0;
        var lastAt = 0f;
        foreach (var raw in args)
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !StyleApplier.TryColor(parts[0], out var c)) continue;
            var at = parts.Length > 1 ? (parts[1].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(parts[1]) / 100f : StyleApplier.Num(parts[1]) / 360f) : (args.Count <= 1 ? 0f : (float)n / (args.Count - 1)); // unpositioned stops spread evenly, as CSS does
            if (n > 0 && at < lastAt) at = lastAt;
            if (stops.Length > 0) stops.Append(',');
            stops.Append('[').AppendNum(at).Append(',').AppendHex(c).Append(']');
            lastAt = at; n++;
        }
        if (n < 2) return null;
        var id = ctx.NextId("conic");
        ctx.Defs.Append("  GC id=").Append(id).Append(" units=bbox cx=").AppendNum(cx).Append(" cy=").AppendNum(cy).Append(" a=").AppendNum(from).Append(" stops=[").Append(stops).Append("]\n");
        return id;
    }

    private static float Position(string v)
    {
        var t = v.Trim().ToLowerInvariant();
        return t switch { "left" or "top" => 0f, "center" => 0.5f, "right" or "bottom" => 1f, _ => t.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(t) / 100f : 0.5f };
    }

    /// <summary>The angle of a linear gradient's head ("90deg", "to right"), CSS default 180 (to bottom).</summary>
    private static float GradientAngle(string head)
    {
        var h = head.Trim().ToLowerInvariant();
        if (h.StartsWith("to ", StringComparison.Ordinal))
        {
            var a = 180f;
            if (h.Contains("top")) a = 0f; if (h.Contains("right")) a = 90f; if (h.Contains("left")) a = 270f;
            if (h.Contains("top") && h.Contains("right")) a = 45f; if (h.Contains("bottom") && h.Contains("right")) a = 135f;
            if (h.Contains("bottom") && h.Contains("left")) a = 225f; if (h.Contains("top") && h.Contains("left")) a = 315f;
            return a;
        }
        if (h.EndsWith("deg", StringComparison.Ordinal) || h.EndsWith("turn", StringComparison.Ordinal) || h.EndsWith("rad", StringComparison.Ordinal) || h.EndsWith("grad", StringComparison.Ordinal)) return Degrees(h);
        return 180f;
    }

    /// <summary>
    /// A repeating-linear-gradient along an axis whose stops are all hard edges (`a 0 6px, b 6px 12px`:
    /// the stripe idiom), drawn as one repeat of rects per colour inside the box's clip. When a
    /// keyframe animation moves `background-position`, the stripes march: an expression over t.
    /// </summary>
    private static bool Stripes(Ctx ctx, Dictionary<string, string> css, string bgCss, VisualElement ve, OffThread.Box rs, float x, float y, float w, float h, string indent)
    {
        var open = bgCss.IndexOf('(');
        var close = bgCss.LastIndexOf(')');
        if (open < 0 || close < open) return false;
        var args = SplitTopLevelCommas(bgCss.Substring(open + 1, close - open - 1));
        var angle = 180f;
        var first = 0;
        if (args.Count > 0 && !StyleApplier.TryColor(args[0].Trim().Split(' ')[0], out _)) { angle = GradientAngle(args[0]); first = 1; }
        angle = Mathf.Repeat(angle, 360f);
        var horizontal = Mathf.Abs(angle - 90f) < 0.5f || Mathf.Abs(angle - 270f) < 0.5f;
        var vertical = Mathf.Abs(angle) < 0.5f || Mathf.Abs(angle - 180f) < 0.5f;
        if (!horizontal && !vertical) return false;
        var span = horizontal ? w : h;
        // stops as (position px, colour); a stop with two positions is two stops
        var stops = new List<(float at, Color c)>();
        for (var i = first; i < args.Count; i++)
        {
            var parts = args[i].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !StyleApplier.TryColor(parts[0], out var col)) return false;
            if (parts.Length == 1) return false; // an auto-positioned stop: a soft ramp, not stripes
            for (var p = 1; p < parts.Length; p++)
                stops.Add((parts[p].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(parts[p]) / 100f * span : StyleApplier.Num(parts[p]), col));
        }
        if (stops.Count < 2) return false;
        var period = stops[stops.Count - 1].at - stops[0].at;
        if (period < 0.5f) return false;
        // every segment must be hard-edged: consecutive stops of different colours share a position
        var segments = new List<(float s0, float s1, Color c)>();
        for (var i = 1; i < stops.Count; i++)
        {
            if (stops[i].c == stops[i - 1].c) { segments.Add((stops[i - 1].at, stops[i].at, stops[i].c)); continue; }
            if (Mathf.Abs(stops[i].at - stops[i - 1].at) > 0.01f) return false; // a ramp between colours: not stripes
        }
        if (segments.Count == 0) return false;
        // marching: a keyframe animation on background-position, its 100% frame's offset over the duration
        var shift = string.Empty;
        foreach (var (element, spec) in ctx.Built.Animations)
        {
            if (element != ve || !ctx.Built.Keyframes.TryGetValue(spec.Name, out var kf) || spec.Duration <= 0f) continue;
            if (css.TryGetValue("animation-play-state", out var ps) && ps.Trim() == "paused") continue;
            var delta = 0f; var found = false;
            foreach (var frame in kf.Frames)
                foreach (var d in frame.Declarations)
                    if (d.Name is "background-position" or "background-position-x" or "background-position-y")
                    {
                        var nums = d.Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var idx = d.Name == "background-position-y" ? 0 : horizontal ? 0 : 1;
                        if (idx < nums.Length) { delta = StyleApplier.Num(nums[idx]) * (frame.Percent >= 99f ? 1f : 0f) + (frame.Percent < 99f ? delta : 0f); found = true; }
                    }
            if (found && Mathf.Abs(delta) > 0.01f)
                shift = "+mod(t*" + F(delta / spec.Duration) + "+" + F(period * 1000f) + "," + F(period) + ")";
        }
        var id = ctx.NextId("stripes");
        ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").AppendNum(x).Append(" y=").AppendNum(y).Append(" w=").AppendNum(w).Append(" h=").AppendNum(h).AppendRadius(rs, w, h, 0f, Keeps(ctx, ve)).Append(" }\n");
        ctx.Body.Append(indent).Append("G clip=").Append(id).Append(" {\n");
        var count = Mathf.CeilToInt(span / period) + 2;
        var origin = (horizontal ? x : y) - period; // one period before the box so a shift never shows a gap
        foreach (var (s0, s1, c) in segments)
        {
            if (s1 - s0 < 0.01f || c.a <= 0.002f) continue;
            ctx.Body.Append(indent).Append("  RP n=").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" { R ");
            if (horizontal)
                ctx.Body.Append("x==").AppendNum(origin + s0).Append("+i*").AppendNum(period).Append(shift).Append(" y=").AppendNum(y).Append(" w=").AppendNum(s1 - s0).Append(" h=").AppendNum(h);
            else
                ctx.Body.Append("x=").AppendNum(x).Append(" y==").AppendNum(origin + s0).Append("+i*").AppendNum(period).Append(shift).Append(" w=").AppendNum(w).Append(" h=").AppendNum(s1 - s0);
            ctx.Body.Append(" f=").AppendHex(c).Append(" }\n");
            ctx.Out.Nodes++;
        }
        ctx.Body.Append(indent).Append("}\n");
        return true;
    }

    /// <summary>repeating-linear/radial-gradient(...) rewritten as the plain gradient with its stop list repeated to 100%; px stops are read against the gradient line.</summary>
    private static string ExpandRepeating(string css, float w = 0f, float h = 0f)
    {
        var open = css.IndexOf('(');
        var close = css.LastIndexOf(')');
        if (open < 0 || close < open) return css;
        var name = css.Substring(0, open).Trim().Substring("repeating-".Length);
        var args = SplitTopLevelCommas(css.Substring(open + 1, close - open - 1));
        var head = new List<string>();
        var stops = new List<(float at, string colour)>();
        var rad = (args.Count > 0 && !StyleApplier.TryColor(args[0].Trim().Split(' ')[0], out _) ? GradientAngle(args[0]) : 180f) * Mathf.Deg2Rad;
        var lineLen = Mathf.Max(1f, w * Mathf.Abs(Mathf.Sin(rad)) + h * Mathf.Abs(Mathf.Cos(rad)));
        foreach (var raw in args)
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && StyleApplier.TryColor(parts[0], out _))
            {
                if (parts.Length == 1) stops.Add((float.NaN, parts[0]));
                for (var p = 1; p < parts.Length; p++)
                    stops.Add((parts[p].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(parts[p]) / 100f : StyleApplier.Num(parts[p]) / lineLen, parts[0]));
            }
            else head.Add(raw.Trim());
        }
        if (stops.Count < 2) return name + css.Substring(open);
        // CSS fills in the positions a page left out: the first is 0, the last is 100%, and a run
        // between two known ones is spread evenly. Bailing out on any of them instead drew ONE
        // gradient across the whole box - a repeating gradient that silently did not repeat.
        if (float.IsNaN(stops[0].at)) stops[0] = (0f, stops[0].colour);
        if (float.IsNaN(stops[stops.Count - 1].at)) stops[stops.Count - 1] = (1f, stops[stops.Count - 1].colour);
        for (var i = 1; i < stops.Count - 1; i++)
        {
            if (!float.IsNaN(stops[i].at)) continue;
            var next = i;
            while (next < stops.Count && float.IsNaN(stops[next].at)) next++;
            if (next >= stops.Count) break;
            var step = (stops[next].at - stops[i - 1].at) / (next - i + 1);
            for (var k = i; k < next; k++) stops[k] = (stops[i - 1].at + step * (k - i + 1), stops[k].colour);
            i = next - 1;
        }
        var period = stops[stops.Count - 1].at - stops[0].at;
        if (period <= 0.0001f) return name + css.Substring(open);
        var outStops = new List<string>();
        for (var k = 0; stops[0].at + k * period < 1f && k < 64; k++)
            foreach (var (at, colour) in stops)
                outStops.Add(colour + " " + F(Mathf.Min(1f, at + k * period) * 100f) + "%");
        head.AddRange(outStops);
        return name + "(" + string.Join(", ", head) + ")";
    }

    /// <summary>border-style from the property or the shorthand: solid (default), dashed, dotted, double, groove, ridge, inset, outset, none.</summary>
    private static string shadowOf(Dictionary<string, string> css, string filterShadow) => (css.TryGetValue("box-shadow", out var s) ? Shadows(s) : string.Empty) + filterShadow;

    /// <summary>corner-shape: the box outline as a P with bevel, scoop or notch corners at the border radii; null for round/square.</summary>
    /// <summary>Any corner-*-shape declaration at all. Asked first because every element asks
    /// CornerPath every frame, and composing the nine property names to find none is a dozen
    /// string allocations per element.</summary>
    private static bool AnyCornerShape(Dictionary<string, string> css)
    {
        foreach (var kv in css)
            if (kv.Key.StartsWith("corner-", StringComparison.Ordinal)) return true;
        return false;
    }

    private static string? CornerPath(Dictionary<string, string> css, OffThread.Box rs, float x, float y, float w, float h)
    {
        if (!AnyCornerShape(css)) return null;  // round corners keep their rx and are drawn by the vector mod's own arcs
        // corner-shape takes one to four values (top-left, top-right, bottom-right, bottom-left, as
        // border-radius); corner-<side>-shape and corner-<corner>-shape override per corner
        var all = css.TryGetValue("corner-shape", out var cs) ? cs.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
        string Nth(int i) => all.Length == 0 ? "round" : all.Length == 1 ? all[0] : all.Length == 2 ? all[i % 2] : all.Length == 3 ? (i == 3 ? all[1] : all[i]) : all[i];
        string ShapeFor(string corner, string sideA, string sideB, int i)
        {
            if (css.TryGetValue("corner-" + corner + "-shape", out var v) || css.TryGetValue("corner-" + sideA + "-shape", out v) || css.TryGetValue("corner-" + sideB + "-shape", out v)) return Lower(v);
            return Nth(i);
        }
        var sTL = ShapeFor("top-left", "top", "left", 0); var sTR = ShapeFor("top-right", "top", "right", 1);
        var sBR = ShapeFor("bottom-right", "bottom", "right", 2); var sBL = ShapeFor("bottom-left", "bottom", "left", 3);
        static bool Custom(string s) => s is "bevel" or "scoop" or "notch" or "square";
        if (!Custom(sTL) && !Custom(sTR) && !Custom(sBR) && !Custom(sBL)) return null; // round and squircle: the R keeps its rx
        var tl = Mathf.Min(rs.borderTopLeftRadius, Mathf.Min(w, h) * 0.5f);
        var tr = Mathf.Min(rs.borderTopRightRadius, Mathf.Min(w, h) * 0.5f);
        var br = Mathf.Min(rs.borderBottomRightRadius, Mathf.Min(w, h) * 0.5f);
        var bl = Mathf.Min(rs.borderBottomLeftRadius, Mathf.Min(w, h) * 0.5f);
        if (tl + tr + br + bl < 0.01f) return null;
        var sb = new StringBuilder("P d=\"");
        void Corner(float cx, float cy, float r, float dx, float dy, bool first, bool alongTop, string shape)
        {
            // corner at (cx,cy); dx,dy point from the corner into the box. Clockwise, the
            // outline reaches a left-hand corner along the vertical side and a right-hand
            // corner along the horizontal one: (ax,ay) is where it arrives, (bx,by) where it leaves.
            var ax = alongTop ? cx + dx * r : cx; var ay = alongTop ? cy : cy + dy * r;
            var bx = alongTop ? cx : cx + dx * r; var by = alongTop ? cy + dy * r : cy;
            if (r <= 0.01f || shape == "square") { sb.Append(first ? "M" : " L").AppendNum(cx).Append(' ').AppendNum(cy); return; }
            switch (shape)
            {
                case "bevel": sb.Append(first ? "M" : " L").AppendNum(ax).Append(' ').AppendNum(ay).Append(" L").AppendNum(bx).Append(' ').AppendNum(by); break;
                case "notch": sb.Append(first ? "M" : " L").AppendNum(ax).Append(' ').AppendNum(ay).Append(" L").AppendNum(cx + dx * r).Append(' ').AppendNum(cy + dy * r).Append(" L").AppendNum(bx).Append(' ').AppendNum(by); break;
                default: sb.Append(first ? "M" : " L").AppendNum(ax).Append(' ').AppendNum(ay).Append(" A").AppendNum(r).Append(' ').AppendNum(r).Append(" 0 0 0 ").AppendNum(bx).Append(' ').AppendNum(by); break; // scoop: concave arc
            }
        }
        // clockwise from the top-left corner: TL arrives from the left side going up, leaves along the top
        Corner(x, y, tl, 1f, 1f, true, false, sTL);
        Corner(x + w, y, tr, -1f, 1f, false, true, sTR);
        Corner(x + w, y + h, br, -1f, -1f, false, false, sBR);
        Corner(x, y + h, bl, 1f, -1f, false, true, sBL);
        sb.Append(" Z\"");
        return sb.ToString();
    }

    /// <summary>
    /// border-image: a gradient source becomes a gradient stroke over the border box; an
    /// image source becomes nine IMG slices with source crops (vector requirement 16).
    /// Slices in percent are exact; a number/px slice needs the image size, which is not
    /// known here, so it is read as thirds (the common nine-slice layout) and reported.
    /// </summary>
    private static bool BorderImage(Ctx ctx, Dictionary<string, string> css, OffThread.Box rs, float x, float y, float w, float h, string indent)
    {
        string? source = null;
        var sliceText = "100%";
        string? widthText = null;
        var fill = false;
        if (css.TryGetValue("border-image", out var shorthand))
        {
            var s = shorthand.Trim();
            if (s.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || s.Contains("gradient("))
            {
                var depth = 0; var end = -1;
                for (var i = 0; i < s.Length; i++) { if (s[i] == '(') depth++; else if (s[i] == ')' && --depth == 0) { end = i; break; } }
                if (end > 0) { source = s.Substring(0, end + 1); s = s.Substring(end + 1).Trim(); }
            }
            else if (s != "none") { var sp = s.IndexOf(' '); source = sp > 0 ? s.Substring(0, sp) : s; s = sp > 0 ? s.Substring(sp + 1) : string.Empty; }
            var slash = s.IndexOf('/');
            var slicePart = slash >= 0 ? s.Substring(0, slash) : s;
            if (slash >= 0) widthText = s.Substring(slash + 1).Split('/')[0].Trim();
            slicePart = slicePart.Replace("round", string.Empty).Replace("repeat", string.Empty).Replace("stretch", string.Empty).Replace("space", string.Empty);
            if (slicePart.Contains("fill")) { fill = true; slicePart = slicePart.Replace("fill", string.Empty); }
            if (slicePart.Trim().Length > 0) sliceText = slicePart.Trim();
        }
        if (css.TryGetValue("border-image-source", out var bis)) source = bis.Trim();
        if (css.TryGetValue("border-image-slice", out var bisl)) { sliceText = bisl.Replace("fill", string.Empty).Trim(); fill |= bisl.Contains("fill"); }
        if (css.TryGetValue("border-image-width", out var biw)) widthText = biw.Trim();
        if (source == null || source == "none") return false;

        var bw = new[] { rs.borderTopWidth, rs.borderRightWidth, rs.borderBottomWidth, rs.borderLeftWidth };
        if (widthText != null)
        {
            var wp = widthText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (wp.Length > 0)
                for (var i = 0; i < 4; i++)
                {
                    var t = StyleApplier.SideOf(wp, i);
                    if (t == "auto") continue;
                    bw[i] = t.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(t) / 100f * (i % 2 == 0 ? h : w) : StyleApplier.IsNumber(t) ? StyleApplier.Num(t) * bw[i] : StyleApplier.Num(t);
                }
        }
        if (bw[0] + bw[1] + bw[2] + bw[3] < 0.01f) return false;

        if (source.Contains("gradient("))
        {
            var parsed = ParseGradient(source);
            if (parsed == null) return false;
            var (angle, stops) = parsed.Value;
            var rad = angle * Mathf.Deg2Rad;
            var len = w * Mathf.Abs(Mathf.Sin(rad)) + h * Mathf.Abs(Mathf.Cos(rad));
            var gid = ctx.NextId("bimg");
            var cx = x + w * 0.5f; var cy = y + h * 0.5f;
            var hx = Mathf.Sin(rad) * len * 0.5f; var hy = -Mathf.Cos(rad) * len * 0.5f;
            ctx.Defs.Append("  GL id=").Append(gid).Append(" x1=").AppendNum(cx - hx).Append(" y1=").AppendNum(cy - hy).Append(" x2=").AppendNum(cx + hx).Append(" y2=").AppendNum(cy + hy).Append(" stops=[");
            for (var i = 0; i < stops.Count; i++) { if (i > 0) ctx.Defs.Append(','); ctx.Defs.Append('[').AppendNum(stops[i].at).Append(',').AppendHex(stops[i].c).Append(']'); }
            ctx.Defs.Append("]\n");
            // one stroke when the widths agree, four gradient-filled sides otherwise
            if (Mathf.Approximately(bw[0], bw[1]) && Mathf.Approximately(bw[0], bw[2]) && Mathf.Approximately(bw[0], bw[3]))
            {
                var half = bw[0] * 0.5f;
                ctx.Body.Append(indent).Append("R x=").AppendNum(x + half).Append(" y=").AppendNum(y + half).Append(" w=").AppendNum(w - bw[0]).Append(" h=").AppendNum(h - bw[0])
                    .Append(" f=none s=@").Append(gid).Append(" sw=").AppendNum(bw[0]).Append('\n');
                ctx.Out.Nodes++;
            }
            else
            {
                var sides = new[] { (x, y, w, bw[0]), (x + w - bw[1], y, bw[1], h), (x, y + h - bw[2], w, bw[2]), (x, y, bw[3], h) };
                foreach (var (sx, sy, sw, sh) in sides)
                {
                    if (sw <= 0.01f || sh <= 0.01f) continue;
                    ctx.Body.Append(indent).Append("R x=").AppendNum(sx).Append(" y=").AppendNum(sy).Append(" w=").AppendNum(sw).Append(" h=").AppendNum(sh).Append(" f=@").Append(gid).Append('\n');
                    ctx.Out.Nodes++;
                }
            }
            return true;
        }

        var url = UrlOf(source);
        if (url == null) return false;
        // slices as fractions of the image: top right bottom left
        var sl = new float[4];
        var sp2 = sliceText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var thirds = false;
        for (var i = 0; i < 4; i++)
        {
            var t = sp2.Length > 0 ? StyleApplier.SideOf(sp2, i) : "100%";
            if (t.EndsWith("%", StringComparison.Ordinal)) sl[i] = Mathf.Clamp01(StyleApplier.Num(t) / 100f);
            else { sl[i] = 1f / 3f; thirds = true; }
        }
        if (thirds && ctx.Reported.Add("border-image px slices")) ctx.Out.Warnings.Add("html: border-image slice in px needs the image size; read as thirds (use % for an exact cut)");
        var u0 = sl[3]; var u1 = 1f - sl[1]; var v0 = sl[0]; var v1 = 1f - sl[2];
        void Img(float ix, float iy, float iw, float ih, float ua, float va, float ub, float vb)
        {
            if (iw <= 0.01f || ih <= 0.01f || ub <= ua || vb <= va) return;
            ctx.Body.Append(indent).Append("IMG x=").AppendNum(ix).Append(" y=").AppendNum(iy).Append(" w=").AppendNum(iw).Append(" h=").AppendNum(ih)
                .Append(" src=\"").Append(url.Replace("\"", string.Empty)).Append("\" fit=fill uv=[").AppendNum(ua).Append(',').AppendNum(va).Append(',').AppendNum(ub).Append(',').AppendNum(vb).Append("]\n");
            ctx.Out.Nodes++;
        }
        var t0 = bw[0]; var r0 = bw[1]; var b0 = bw[2]; var l0 = bw[3];
        Img(x, y, l0, t0, 0f, 0f, u0, v0);                                   // top-left
        Img(x + l0, y, w - l0 - r0, t0, u0, 0f, u1, v0);                      // top
        Img(x + w - r0, y, r0, t0, u1, 0f, 1f, v0);                           // top-right
        Img(x, y + t0, l0, h - t0 - b0, 0f, v0, u0, v1);                      // left
        if (fill) Img(x + l0, y + t0, w - l0 - r0, h - t0 - b0, u0, v0, u1, v1); // centre
        Img(x + w - r0, y + t0, r0, h - t0 - b0, u1, v0, 1f, v1);             // right
        Img(x, y + h - b0, l0, b0, 0f, v1, u0, 1f);                           // bottom-left
        Img(x + l0, y + h - b0, w - l0 - r0, b0, u0, v1, u1, 1f);             // bottom
        Img(x + w - r0, y + h - b0, r0, b0, u1, v1, 1f, 1f);                  // bottom-right
        return true;
    }

    /// <summary>True when the transform's rotateX/rotateY turn the element past 90 degrees, showing its back.</summary>
    private static bool BackfaceTurned(string transform)
    {
        var sign = 1f;
        foreach (var (name, args) in StyleApplier.Functions(transform))
        {
            if (name is not ("rotatex" or "rotatey") || args.Length == 0) continue;
            var c = Mathf.Cos(StyleApplier.Num(args[0].Trim()) * Mathf.Deg2Rad);
            sign *= c < 0f ? -1f : 1f;
        }
        return sign < 0f;
    }

    /// <summary>The cascaded declarations of a pseudo-element of an element, for the ones with no generated node (scrollbars, first-line).</summary>
    /// <summary>Read-only answer for a pseudo-element no rule mentions.</summary>
    private static readonly Dictionary<string, string> NoPseudoCss = new(StringComparer.Ordinal);

    private static Dictionary<string, string> PseudoCss(Ctx ctx, VisualElement ve, string pseudo)
    {
        // Which pseudo-elements the page styles at all, collected once per stylesheet: without
        // this every label built a probe node and walked every rule, on every frame.
        if (!ReferenceEquals(ctx.PseudosOf, ctx.Built) || ctx.PseudosRules != ctx.Built.Rules.Count)
        {
            var names = ctx.Pseudos ??= new HashSet<string>(StringComparer.Ordinal);
            names.Clear();
            foreach (var rule in ctx.Built.Rules)
                foreach (var sel in rule.Selectors)
                    if (sel.Chain[sel.Chain.Count - 1].PseudoElement is { } pe) names.Add(pe);
            ctx.PseudosOf = ctx.Built;
            ctx.PseudosRules = ctx.Built.Rules.Count;
        }
        if (!ctx.Pseudos!.Contains(pseudo)) return NoPseudoCss;

        var css = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!ctx.Built.NodeOf.TryGetValue(ve, out var node)) return css;
        var probe = new HtmlNode { Tag = "span", Parent = node };
        probe.Attributes["data-pseudo"] = pseudo;
        var matched = new List<(int spec, int order, CssRule rule)>();
        foreach (var rule in ctx.Built.Rules)
        {
            var best = -1;
            foreach (var sel in rule.Selectors)
                if (sel.Chain[sel.Chain.Count - 1].PseudoElement == pseudo && sel.Matches(probe)) best = Math.Max(best, sel.Specificity);
            if (best >= 0) matched.Add((best, rule.Order, rule));
        }
        matched.Sort((a, b) => a.spec != b.spec ? a.spec.CompareTo(b.spec) : a.order.CompareTo(b.order));
        foreach (var m in matched)
            foreach (var d in m.rule.Declarations)
            {
                var v = d.Value.IndexOf("var(", StringComparison.Ordinal) >= 0 ? HtmlRenderer.TryResolveVars(d.Value, node) : d.Value.Trim();
                if (v != null) css[d.Name] = v;
            }
        return css;
    }

    /// <summary>
    /// A scrollbar drawn over the right edge of a scroll box, inside it so it reads `sy`:
    /// a track and a thumb whose position follows the offset. scrollbar-width and
    /// scrollbar-color, or the ::-webkit-scrollbar family, set its look; none hides it.
    /// </summary>
    private static void EmitScrollbar(Ctx ctx, VisualElement ve, Dictionary<string, string> css, float x, float y, float w, float h, float ch, string indent)
    {
        var range = ch - h;
        if (range <= 0.5f) return;
        var width = 8f;
        if (css.TryGetValue("scrollbar-width", out var swv))
        {
            var v = swv.Trim();
            if (v == "none") return;
            if (v == "thin") width = 5f;
        }
        var thumb = new Color(1f, 1f, 1f, 0.35f);
        var track = Color.clear;
        var radius = width * 0.5f;
        if (css.TryGetValue("scrollbar-color", out var scv))
        {
            var parts = CssParser.SplitTopLevel(scv.Trim(), ' ');
            if (parts.Count > 0 && StyleApplier.TryColor(parts[0], out var tc)) thumb = tc;
            if (parts.Count > 1 && StyleApplier.TryColor(parts[1], out var kc)) track = kc;
        }
        var bar = PseudoCss(ctx, ve, "-webkit-scrollbar");
        if (bar.TryGetValue("display", out var bd) && bd == "none") return;
        if (bar.TryGetValue("width", out var bwv)) width = StyleApplier.Num(bwv);
        if (bar.TryGetValue("background", out var bb) || bar.TryGetValue("background-color", out bb)) { if (StyleApplier.TryColor(bb, out var c)) track = c; }
        var tr = PseudoCss(ctx, ve, "-webkit-scrollbar-track");
        if (tr.TryGetValue("background", out var tb) || tr.TryGetValue("background-color", out tb)) { if (StyleApplier.TryColor(tb, out var c)) track = c; }
        var th = PseudoCss(ctx, ve, "-webkit-scrollbar-thumb");
        if (th.TryGetValue("background", out var hb) || th.TryGetValue("background-color", out hb)) { if (StyleApplier.TryColor(hb, out var c)) thumb = c; }
        if (th.TryGetValue("border-radius", out var hr)) radius = StyleApplier.Num(hr);
        if (width <= 0.01f) return;
        var thumbH = Mathf.Max(16f, h * h / ch);
        var k = 1f + (h - thumbH) / range;
        if (track.a > 0.002f)
        {
            ctx.Body.Append(indent).Append("R x=").AppendNum(x + w - width).Append(" y==").AppendNum(y).Append("+sy w=").AppendNum(width).Append(" h=").AppendNum(h).Append(" f=").AppendHex(track).Append('\n');
            ctx.Out.Nodes++;
        }
        ctx.Body.Append(indent).Append("R x=").AppendNum(x + w - width + 1f).Append(" y==").AppendNum(y).Append("+sy*").AppendNum(k).Append(" w=").AppendNum(width - 2f).Append(" h=").AppendNum(thumbH)
            .Append(" rx=").AppendNum(Mathf.Min(radius, (width - 2f) * 0.5f)).Append(" f=").AppendHex(thumb).Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>
    /// animation-timeline: scroll() / view(): the element's @keyframes written as one G whose
    /// opacity and transform are piecewise expressions over the scroll progress p (0..1),
    /// eased per segment; scroll() runs over the box's whole range, view() while the
    /// element crosses the viewport. No clock, no rebuilds: the vector mod evaluates sy.
    /// </summary>
    /// <summary>
    /// Keyframes the scene can run on its own clock: opacity and transform become expressions on a
    /// G, background-color becomes a ramp the fill walks. Anything else needs a KeyframeRunner, and
    /// a runner costs the page a whole frame at every keyframe boundary.
    ///
    /// The element's own style decides too: a colour animation is painted by the plain-background
    /// branch, so an element backed by an image or a gradient keeps its runner rather than quietly
    /// losing its animation - the emitter would never reach the branch that draws it.
    /// </summary>
    internal static bool Compilable(CssKeyframes frames, Dictionary<string, string>? css = null)
    {
        if (frames.Frames.Count == 0) return false;
        var colour = false;
        foreach (var f in frames.Frames)
            foreach (var d in f.Declarations)
            {
                if (d.Name == "opacity" || d.Name == "transform") continue;
                if (d.Name == "background-color") { colour = true; continue; }
                return false;
            }
        if (colour && (css == null || Layered(css))) return false;
        return true;
    }

    /// <summary>Does this element paint its background as anything other than one flat colour?</summary>
    private static bool Layered(Dictionary<string, string> css)
    {
        if (css.TryGetValue("background-image", out var bi) && bi.Trim() != "none") return true;
        return css.TryGetValue("background", out var bg)
               && (bg.IndexOf("gradient(", StringComparison.OrdinalIgnoreCase) >= 0
                   || bg.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// A looping keyframe animation as a G over the scene clock: progress is the phase of
    /// (t - start) in the duration (a triangle for alternate). Start is written relative to the
    /// moment the structure was applied (the vector clock's zero), so value patches leave it running.
    /// </summary>
    private static string? TimeTimeline(Ctx ctx, VisualElement ve, AnimationSpec spec, float started, float x, float y, float w, float h)
    {
        if (!ctx.Built.Keyframes.TryGetValue(spec.Name, out var frames) || frames.Frames.Count == 0) return null;
        var epoch = ctx.Tw != null && !float.IsNaN(ctx.Tw.Epoch) ? ctx.Tw.Epoch : ctx.Now;
        var s0 = started + spec.Delay - epoch;
        var d = Mathf.Max(0.001f, spec.Duration);
        var elapsed = "max(0,t-(" + F(s0) + "))";
        var p = spec.Alternate
            ? "(1-abs(mod(" + elapsed + "," + F(2f * d) + ")/" + F(d) + "-1))"
            : "(mod(" + elapsed + "," + F(d) + ")/" + F(d) + ")";
        return KeyframeGroup(spec, frames, p, x, y, w, h);
    }

    /// <summary>The phase of a looping animation in the scene clock, 0..1. Shared, so a colour ramp
    /// and the transform group around it walk the same animation at the same rate.</summary>
    private static string TimeProgress(Ctx ctx, AnimationSpec spec, float started)
    {
        var epoch = ctx.Tw != null && !float.IsNaN(ctx.Tw.Epoch) ? ctx.Tw.Epoch : ctx.Now;
        var s0 = started + spec.Delay - epoch;
        var d = Mathf.Max(0.001f, spec.Duration);
        var elapsed = "max(0,t-(" + F(s0) + "))";
        var p = spec.Alternate
            ? "(1-abs(mod(" + elapsed + "," + F(2f * d) + ")/" + F(d) + "-1))"
            : "(mod(" + elapsed + "," + F(d) + ")/" + F(d) + ")";
        return spec.Reverse ? "(1-" + p + ")" : p;
    }

    /// <summary>
    /// A looping background-color animation as a ramp plus a place to sample it. An expression
    /// cannot return a colour, but it can return a position along one, so the frames become a GL def
    /// and `fat` walks it - the same mechanism a colour transition already uses, with a stop per
    /// keyframe instead of two. The page then runs no frames for the animation at all.
    /// </summary>
    private static (string gid, string at)? ColourTimeline(Ctx ctx, VisualElement ve)
    {
        if (!ctx.Built.TimeAnimations.TryGetValue(ve, out var ta)) return null;
        if (!ctx.Built.Keyframes.TryGetValue(ta.spec.Name, out var frames) || frames.Frames.Count == 0) return null;

        // a stop per keyframe; a frame naming no colour holds the one before it, as CSS does
        var stops = _stops ??= new List<(float at, Color c)>();
        stops.Clear();
        foreach (var f in frames.Frames)
        {
            var has = false; var c = default(Color);
            foreach (var d in f.Declarations)
                if (d.Name == "background-color" && StyleApplier.TryColor(d.Value.Trim(), out var parsed)) { c = parsed; has = true; }
            if (!has && stops.Count > 0) { c = stops[stops.Count - 1].c; has = true; }
            if (has) stops.Add((f.Percent / 100f, c));
        }
        if (stops.Count < 2) return null;

        var gid = ctx.NextId("ka");
        ctx.Defs.Append("  GL id=").Append(gid).Append(" stops=[");
        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0) ctx.Defs.Append(',');
            ctx.Defs.Append('[').AppendNum(stops[i].at).Append(',').AppendHex(stops[i].c).Append(']');
        }
        ctx.Defs.Append("]\n");
        return (gid, EasedAt(ta.spec, stops, TimeProgress(ctx, ta.spec, ta.start)));
    }

    [ThreadStatic] private static List<(float at, Color c)>? _stops;

    /// <summary>Where along the ramp the progress sits. The stops are at the keyframe percents, so
    /// easing is only a matter of crossing the gap between two of them faster or slower.</summary>
    private static string EasedAt(AnimationSpec spec, List<(float at, Color c)> stops, string p)
    {
        var expr = F(stops[stops.Count - 1].at);
        for (var i = stops.Count - 2; i >= 0; i--)
        {
            var a = stops[i].at; var b = stops[i + 1].at;
            var span = Mathf.Max(0.0001f, b - a);
            var local = spec.Easing.Expr("clamp((" + p + "-" + F(a) + ")/" + F(span) + ",0,1)");
            expr = "if(lt(" + p + "," + F(b) + ")," + F(a) + "+(" + F(b - a) + ")*" + local + "," + expr + ")";
        }
        return expr;
    }

    private static string? ScrollTimeline(Ctx ctx, VisualElement ve, Dictionary<string, string> css, string timeline, float x, float y, float w, float h)
    {
        AnimationSpec? spec = null;
        foreach (var (element, s) in ctx.Built.Animations) if (element == ve) spec = s;
        if (spec == null || !ctx.Built.Keyframes.TryGetValue(spec.Name, out var frames) || frames.Frames.Count == 0) return null;
        string p;
        var tl = timeline.Trim().ToLowerInvariant();
        if (tl.StartsWith("view", StringComparison.Ordinal))
        {
            // enters at the viewport bottom (sy = y - top - H), leaves at its top (sy = y - top + h)
            var enter = y - ctx.ScrollTop - ctx.ScrollH;
            p = "clamp((sy-(" + F(enter) + "))/" + F(Mathf.Max(1f, h + ctx.ScrollH)) + ",0,1)";
        }
        else
            p = "clamp(sy/" + F(Mathf.Max(1f, ctx.ScrollRange)) + ",0,1)";
        return KeyframeGroup(spec, frames, p, x, y, w, h);
    }

    /// <summary>The frames as a G whose opacity and transform follow progress <paramref name="p"/> (0..1).</summary>
    private static string? KeyframeGroup(AnimationSpec spec, CssKeyframes frames, string p, float x, float y, float w, float h)
    {
        if (spec.Reverse) p = "(1-" + p + ")";

        var keys = new List<(float at, float o, float tx, float ty, float sx, float sy, float r)>();
        foreach (var f in frames.Frames)
        {
            var o = 1f; var tx = 0f; var ty = 0f; var sx = 1f; var sy = 1f; var r = 0f;
            var seen = false;
            foreach (var d in f.Declarations)
            {
                if (d.Name == "opacity") { o = StyleApplier.Num(d.Value); seen = true; }
                else if (d.Name == "transform")
                {
                    seen = true;
                    foreach (var (name, args) in StyleApplier.Functions(d.Value))
                    {
                        float A(int i) => args.Length > i ? StyleApplier.Num(args[i].Trim()) : 0f;
                        switch (name)
                        {
                            case "translate": tx = A(0); ty = args.Length > 1 ? A(1) : 0f; break;
                            case "translatex": tx = A(0); break;
                            case "translatey": ty = A(0); break;
                            case "scale": sx = A(0); sy = args.Length > 1 ? A(1) : A(0); break;
                            case "scalex": sx = A(0); break;
                            case "scaley": sy = A(0); break;
                            case "rotate": r = A(0); break;
                        }
                    }
                }
            }
            if (!seen && keys.Count > 0) { var prev = keys[keys.Count - 1]; o = prev.o; tx = prev.tx; ty = prev.ty; sx = prev.sx; sy = prev.sy; r = prev.r; }
            keys.Add((f.Percent / 100f, o, tx, ty, sx, sy, r));
        }
        if (keys.Count == 1) keys.Insert(0, (0f, 1f, 0f, 0f, 1f, 1f, 0f));

        // piecewise: nested if() over the segments, each eased on its own progress
        string Piece(Func<(float at, float o, float tx, float ty, float sx, float sy, float r), float> pick)
        {
            var all = true;
            for (var i = 1; i < keys.Count; i++) if (Mathf.Abs(pick(keys[i]) - pick(keys[0])) > 0.0001f) all = false;
            if (all) return F(pick(keys[0]));
            var expr = F(pick(keys[keys.Count - 1]));
            for (var i = keys.Count - 2; i >= 0; i--)
            {
                var a = keys[i]; var b = keys[i + 1];
                var span = Mathf.Max(0.0001f, b.at - a.at);
                var local = spec.Easing.Expr("clamp((" + p + "-" + F(a.at) + ")/" + F(span) + ",0,1)");
                var seg = F(pick(a)) + "+(" + F(pick(b) - pick(a)) + ")*" + local;
                expr = "if(lt(" + p + "," + F(b.at) + ")," + seg + "," + expr + ")";
            }
            return "=" + expr;
        }
        var sb = new StringBuilder("G a=[").AppendNum(x + w * 0.5f).Append(',').AppendNum(y + h * 0.5f).Append(']');
        var moved = false;
        var op = Piece(k => k.o);
        if (op != "1") { sb.Append(" o=").Append(Quote(op)); moved = true; }
        var txe = Piece(k => k.tx); var tye = Piece(k => k.ty);
        if (txe != "0" || tye != "0") { sb.Append(" t=[").Append(Quote(txe)).Append(',').Append(Quote(tye)).Append(']'); moved = true; }
        var re = Piece(k => k.r);
        if (re != "0") { sb.Append(" r=").Append(Quote(re)); moved = true; }
        var sxe = Piece(k => k.sx); var sye = Piece(k => k.sy);
        if (sxe != "1" || sye != "1") { sb.Append(" s=[").Append(Quote(sxe)).Append(',').Append(Quote(sye)).Append(']'); moved = true; }
        // A colour-only animation has nothing to say to a transform group, and an empty one would
        // cost a node and a nesting level per animated element for no effect.
        return moved ? sb.ToString() : null;
    }

    private static string Quote(string v) => v.StartsWith("=", StringComparison.Ordinal) ? "\"" + v + "\"" : v;

    private static string BorderStyle(Dictionary<string, string> css) => SideStyle(css, 0);

    private static bool IsBorderStyle(string p) => p is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge" or "inset" or "outset" or "none" or "hidden";

    /// <summary>The property names per side, composed once: a page emits every frame, and
    /// "border-" + side + "-style" is four string allocations per element per frame.</summary>
    private static readonly string[] SideStyleKeys = { "border-top-style", "border-right-style", "border-bottom-style", "border-left-style" };
    private static readonly string[] SideKeys = { "border-top", "border-right", "border-bottom", "border-left" };

    /// <summary>The four sides' styles, into a buffer reused per thread: this runs on every element of every frame.</summary>
    [ThreadStatic] private static string[]? _sideStyles;
    private static string[] SideStyles(Dictionary<string, string> css)
    {
        var s = _sideStyles ??= new string[4];
        for (var i = 0; i < 4; i++) s[i] = SideStyle(css, i);
        return s;
    }

    /// <summary>One side's border style: border-&lt;side&gt;-style, then the border-&lt;side&gt; shorthand, then border-style (1-4 values), then the border shorthand, else solid.</summary>
    private static string SideStyle(Dictionary<string, string> css, int index)
    {
        if (css.TryGetValue(SideStyleKeys[index], out var own)) return Lower(own);
        if (css.TryGetValue(SideKeys[index], out var sh) && StyleWord(sh) is { } shw) return shw;
        if (css.TryGetValue("border-style", out var bs))
        {
            var parts = Lower(bs).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) return StyleApplier.SideOf(parts, index);
        }
        if (css.TryGetValue("border", out var b) && StyleWord(b) is { } bw) return bw;
        return "solid";
    }

    private static readonly string[] BorderStyleWords = { "solid", "dashed", "dotted", "double", "groove", "ridge", "inset", "outset", "none", "hidden" };

    /// <summary>The style word in a border shorthand ("1px solid #333"), found without splitting
    /// it: four sides of every element asked this on every frame, and each ask built an array.</summary>
    private static string? StyleWord(string value)
    {
        var i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && value[i] == ' ') i++;
            var start = i;
            while (i < value.Length && value[i] != ' ') i++;
            var len = i - start;
            if (len == 0) continue;
            foreach (var word in BorderStyleWords)
            {
                if (word.Length != len) continue;
                var same = true;
                for (var k = 0; k < len && same; k++) same = char.ToLowerInvariant(value[start + k]) == word[k];
                if (same) return word;
            }
        }
        return null;
    }

    private static string DashFor(string style, float bw)
    {
        return style switch
        {
            "dashed" => " dash=[" + F(bw * 3f) + "," + F(bw * 2f) + "]",
            "dotted" => " dash=[" + F(bw) + "," + F(bw) + "] cap=round",
            _ => string.Empty,
        };
    }

    /// <summary>One border side as a stroked line in its own style; none/hidden draws nothing.</summary>
    private static void SideLine(Ctx ctx, string indent, string style, float x1, float y1, float x2, float y2, float width, Color c)
    {
        if (width <= 0.01f || c.a <= 0.002f || style is "none" or "hidden")
            return;
        ctx.Body.Append(indent).Append("L p=[").AppendNum(x1).Append(',').AppendNum(y1).Append(',').AppendNum(x2).Append(',').AppendNum(y2).Append("] s=").AppendHex(c).Append(" sw=").AppendNum(width).Append(DashFor(style, width)).Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>
    /// CSS filter list: colour functions become group attributes (vector requirement 12),
    /// drop-shadow() becomes a shadow entry for the box or label, blur() is reported once.
    /// </summary>
    private static bool Filters(Ctx ctx, string css, out string attrs, out string shadow)
    {
        var sb = new StringBuilder();
        var sh = new StringBuilder();
        foreach (var (name, args) in StyleApplier.Functions(css))
        {
            var a = args.Length > 0 ? args[0].Trim() : string.Empty;
            float Amount() => a.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(a) / 100f : StyleApplier.Num(a);
            switch (name)
            {
                case "brightness": sb.Append(" bri=").AppendNum(Amount()); break;
                case "contrast": sb.Append(" con=").AppendNum(Amount()); break;
                case "saturate": sb.Append(" sat=").AppendNum(Amount()); break;
                case "grayscale": sb.Append(" gray=").AppendNum(Amount()); break;
                case "sepia": sb.Append(" sep=").AppendNum(Amount()); break;
                case "invert": sb.Append(" inv=").AppendNum(Amount()); break;
                case "hue-rotate": sb.Append(" hue=").AppendNum(StyleApplier.Num(a)); break;
                case "opacity": sb.Append(" o=").AppendNum(Amount()); break;
                case "drop-shadow":
                {
                    var parts = string.Join(" ", args).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var nums = new List<float>(); var colour = new Color(0, 0, 0, 1);
                    foreach (var p in parts) { if (StyleApplier.TryColor(p, out var c)) colour = c; else nums.Add(StyleApplier.Num(p)); }
                    while (nums.Count < 3) nums.Add(0f);
                    sh.Append('[').AppendNum(nums[0]).Append(',').AppendNum(nums[1]).Append(',').AppendNum(nums[2]).Append(",0,").AppendHex(colour).Append(']');
                    break;
                }
                case "blur":
                    Warn(ctx, "html: filter: blur() has no equivalent in a geometry layer; ignored");
                    break;
            }
        }
        attrs = sb.ToString();
        shadow = sh.Length > 0 ? " sh=[" + sh + "]" : string.Empty;
        return attrs.Length > 0 || shadow.Length > 0;
    }

    /// <summary>clip-path basic shapes to a CP def in page coordinates: inset(), circle(), ellipse(), polygon(). A concave polygon is passed as is (vector requirement 8).</summary>
    private static string? ClipPath(Ctx ctx, string css, float x, float y, float w, float h, Xform? xform = null, bool evenOdd = false)
    {
        var rule = evenOdd ? " fr=evenodd" : string.Empty;
        var v = css.Trim();
        var open = v.IndexOf('(');
        if (open < 0 || !v.EndsWith(")", StringComparison.Ordinal)) return null;
        var name = v.Substring(0, open).Trim().ToLowerInvariant();
        var inner = v.Substring(open + 1, v.Length - open - 2).Trim();
        var id = ctx.NextId("cpath");
        float Along(string s, float size) => s.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(s) / 100f * size : StyleApplier.Num(s);
        // Under a transform the def (scene space, like every CP) is the shape put through it, as a
        // polygon. ponytail: a tweened transform clips at its end position; inset radii are dropped.
        string? Through(List<Vector2> pts)
        {
            if (xform == null) return null;
            var sb = new StringBuilder();
            foreach (var p in pts)
            {
                var q = xform.Apply(p);
                if (sb.Length > 0) sb.Append(',');
                sb.AppendNum(q.x).Append(',').AppendNum(q.y);
            }
            ctx.Defs.Append("  CP id=").Append(id).Append(" { Y p=[").Append(sb).Append(']').Append(rule).Append(" }\n");
            return id;
        }
        switch (name)
        {
            case "xywh":
            case "rect":
            {
                // xywh(x y w h [round r]) and rect(top right bottom left [round r]), edges from the box's top-left
                var round = inner.IndexOf(" round ", StringComparison.OrdinalIgnoreCase);
                var radius = round >= 0 ? StyleApplier.Num(inner.Substring(round + 7).Trim().Split(' ')[0]) : 0f;
                var nums = (round >= 0 ? inner.Substring(0, round) : inner).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (nums.Length < 4) return null;
                float rx0, ry0, rw, rh;
                if (name == "xywh") { rx0 = x + Along(nums[0], w); ry0 = y + Along(nums[1], h); rw = Along(nums[2], w); rh = Along(nums[3], h); }
                else
                {
                    var t = nums[0] == "auto" ? 0f : Along(nums[0], h); var r = nums[1] == "auto" ? w : Along(nums[1], w);
                    var b = nums[2] == "auto" ? h : Along(nums[2], h); var l = nums[3] == "auto" ? 0f : Along(nums[3], w);
                    rx0 = x + l; ry0 = y + t; rw = Mathf.Max(0f, r - l); rh = Mathf.Max(0f, b - t);
                }
                if (xform != null) return Through(new List<Vector2> { new(rx0, ry0), new(rx0 + rw, ry0), new(rx0 + rw, ry0 + rh), new(rx0, ry0 + rh) });
                ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").AppendNum(rx0).Append(" y=").AppendNum(ry0).Append(" w=").AppendNum(rw).Append(" h=").AppendNum(rh);
                if (radius > 0f) ctx.Defs.Append(" rx=").AppendNum(radius);
                ctx.Defs.Append(" }\n");
                return id;
            }
            case "inset":
            {
                var round = inner.IndexOf(" round ", StringComparison.OrdinalIgnoreCase);
                var radius = round >= 0 ? StyleApplier.Num(inner.Substring(round + 7).Trim().Split(' ')[0]) : 0f;
                var sides = (round >= 0 ? inner.Substring(0, round) : inner).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var t = sides.Length > 0 ? Along(sides[0], h) : 0f;
                var r = sides.Length > 1 ? Along(sides[1], w) : t;
                var b = sides.Length > 2 ? Along(sides[2], h) : t;
                var l = sides.Length > 3 ? Along(sides[3], w) : r;
                if (xform != null) return Through(new List<Vector2> { new(x + l, y + t), new(x + w - r, y + t), new(x + w - r, y + h - b), new(x + l, y + h - b) });
                ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").AppendNum(x + l).Append(" y=").AppendNum(y + t).Append(" w=").AppendNum(Mathf.Max(0f, w - l - r)).Append(" h=").AppendNum(Mathf.Max(0f, h - t - b));
                if (radius > 0f) ctx.Defs.Append(" rx=").AppendNum(radius);
                ctx.Defs.Append(" }\n");
                return id;
            }
            case "circle":
            case "ellipse":
            {
                var at = inner.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
                var size = (at >= 0 ? inner.Substring(0, at) : inner).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var pos = at >= 0 ? inner.Substring(at + 4).Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
                var cx = x + (pos.Length > 0 ? Along(pos[0], w) : w * 0.5f);
                var cy = y + (pos.Length > 1 ? Along(pos[1], h) : h * 0.5f);
                float rx, ry;
                if (name == "circle")
                {
                    var rr = size.Length > 0 && size[0] != "closest-side" && size[0] != "farthest-side" ? Along(size[0], Mathf.Sqrt(w * w + h * h) / 1.41421f) : Mathf.Min(w, h) * 0.5f;
                    rx = ry = rr;
                }
                else
                {
                    rx = size.Length > 0 ? Along(size[0], w) : w * 0.5f;
                    ry = size.Length > 1 ? Along(size[1], h) : h * 0.5f;
                }
                if (xform != null)
                {
                    var ring = new List<Vector2>();
                    for (var k = 0; k < 32; k++) { var a = k / 32f * 2f * Mathf.PI; ring.Add(new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a))); }
                    return Through(ring);
                }
                ctx.Defs.Append("  CP id=").Append(id).Append(" { C cx=").AppendNum(cx).Append(" cy=").AppendNum(cy).Append(" rx=").AppendNum(rx).Append(" ry=").AppendNum(ry).Append(" }\n");
                return id;
            }
            case "polygon":
            {
                var pts = new StringBuilder();
                var poly = new List<Vector2>();
                foreach (var pair in SplitTopLevelCommas(inner))
                {
                    var xy = pair.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (xy.Length < 2) continue;
                    if (pts.Length > 0) pts.Append(',');
                    poly.Add(new Vector2(x + Along(xy[0], w), y + Along(xy[1], h)));
                    pts.AppendNum(x + Along(xy[0], w)).Append(',').AppendNum(y + Along(xy[1], h));
                }
                if (pts.Length == 0) return null;
                if (xform != null) return Through(poly);
                ctx.Defs.Append("  CP id=").Append(id).Append(" { Y p=[").Append(pts).Append(']').Append(rule).Append(" }\n");
                return id;
            }
        }
        return null;
    }

    /// <summary>mask-image: a linear or radial gradient as a GL/GR def whose alpha masks the subtree (vector requirement 10).</summary>
    private static string? MaskDef(Ctx ctx, string css, float x, float y, float w, float h, Dictionary<string, string>? all = null, OffThread.Box? rs = null)
    {
        var v = css.Trim();
        if (v.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase)) return RadialDef(ctx, v);
        if (!v.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase)) return null;
        if (ParseGradient(v) is not { } g) return null;
        // mask-origin / mask-clip: the box the gradient spans (border box by default here, the
        // bbox); mask-size and mask-position: a sub-box of it. All in bbox fractions.
        // ponytail: mask-repeat is accepted; a gradient does not tile, it clamps to its ends
        var bx = 0f; var by = 0f; var bw = 1f; var bh = 1f;
        if (all != null && rs != null && w > 0f && h > 0f)
        {
            var origin = (all.TryGetValue("mask-origin", out var mo) ? mo : all.TryGetValue("mask-clip", out var mc) ? mc : "border-box").Trim();
            if (origin is "padding-box" or "content-box")
            {
                var l = rs.borderLeftWidth + (origin == "content-box" ? rs.paddingLeft : 0f); var t = rs.borderTopWidth + (origin == "content-box" ? rs.paddingTop : 0f);
                var r = rs.borderRightWidth + (origin == "content-box" ? rs.paddingRight : 0f); var b = rs.borderBottomWidth + (origin == "content-box" ? rs.paddingBottom : 0f);
                bx = l / w; by = t / h; bw = Mathf.Max(0.01f, (w - l - r) / w); bh = Mathf.Max(0.01f, (h - t - b) / h);
            }
            float Frac(string p, float size) => p.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(p) / 100f : StyleApplier.Num(p) / size;
            if (all.TryGetValue("mask-size", out var ms) && ms.Trim() is not ("auto" or "cover" or "contain"))
            {
                var parts = ms.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var sw = parts.Length > 0 && parts[0] != "auto" ? Frac(parts[0], w) : 1f;
                var shh = parts.Length > 1 && parts[1] != "auto" ? Frac(parts[1], h) : 1f;
                var px = 0f; var py = 0f;
                if (all.TryGetValue("mask-position", out var mp))
                {
                    var pp = mp.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    float Pos(string p, float free) => p switch { "left" or "top" => 0f, "center" => 0.5f * free, "right" or "bottom" => free, _ => p.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(p) / 100f * free : StyleApplier.Num(p) / (p == pp[0] ? w : h) };
                    px = pp.Length > 0 ? Pos(pp[0], bw - sw * bw) : 0f;
                    py = pp.Length > 1 ? Pos(pp[1], bh - shh * bh) : pp.Length == 1 && pp[0] == "center" ? 0.5f * (bh - shh * bh) : 0f;
                }
                bx += px; by += py; bw *= sw; bh *= shh;
            }
        }
        var rad = g.angle * Mathf.Deg2Rad;
        var len = bw * w * Mathf.Abs(Mathf.Sin(rad)) + bh * h * Mathf.Abs(Mathf.Cos(rad));
        var dx = Mathf.Sin(rad) * len * 0.5f / w;
        var dy = -Mathf.Cos(rad) * len * 0.5f / h;
        var cx = bx + bw * 0.5f; var cy = by + bh * 0.5f;
        var id = ctx.NextId("mask");
        ctx.Defs.Append("  GL id=").Append(id).Append(" units=bbox x1=").AppendNum(cx - dx).Append(" y1=").AppendNum(cy - dy).Append(" x2=").AppendNum(cx + dx).Append(" y2=").AppendNum(cy + dy).Append(" stops=[");
        for (var i = 0; i < g.stops.Count; i++)
        {
            if (i > 0) ctx.Defs.Append(',');
            ctx.Defs.Append('[').AppendNum(g.stops[i].at).Append(',').AppendHex(g.stops[i].c).Append(']');
        }
        ctx.Defs.Append("]\n");
        return id;
    }

    /// <summary>
    /// A CSS transform list as one 2x3 matrix about the transform origin, page coordinates
    /// (vector requirement 13). 3D functions contribute their 2D part.
    /// </summary>
    private static float[]? Matrix(string transform, Dictionary<string, string> css, float x, float y, float w, float h)
    {
        // origin
        var ox = x + w * 0.5f; var oy = y + h * 0.5f;
        if (css.TryGetValue("transform-origin", out var to))
        {
            var p = to.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 0) ox = x + (p[0] is "left" ? 0f : p[0] is "right" ? w : p[0] is "center" ? w * 0.5f : p[0].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(p[0]) / 100f * w : StyleApplier.Num(p[0]));
            if (p.Length > 1) oy = y + (p[1] is "top" ? 0f : p[1] is "bottom" ? h : p[1] is "center" ? h * 0.5f : p[1].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(p[1]) / 100f * h : StyleApplier.Num(p[1]));
        }
        // m = [a b c d e f] for x' = a x + c y + e, y' = b x + d y + f
        var m = new[] { 1f, 0f, 0f, 1f, 0f, 0f };
        void Mul(float a, float b, float c, float d, float e, float f)
        {
            var r = new[]
            {
                m[0] * a + m[2] * b, m[1] * a + m[3] * b,
                m[0] * c + m[2] * d, m[1] * c + m[3] * d,
                m[0] * e + m[2] * f + m[4], m[1] * e + m[3] * f + m[5],
            };
            m = r;
        }
        Mul(1, 0, 0, 1, ox, oy);
        var any = false;
        foreach (var (name, args) in StyleApplier.Functions(transform))
        {
            float A(int i) => args.Length > i ? StyleApplier.Num(args[i].Trim()) : 0f;
            switch (name)
            {
                case "translate": case "translate3d": Mul(1, 0, 0, 1, A(0), A(1)); break;
                case "translatex": Mul(1, 0, 0, 1, A(0), 0); break;
                case "translatey": Mul(1, 0, 0, 1, 0, A(0)); break;
                case "scale": case "scale3d": Mul(A(0), 0, 0, args.Length > 1 ? A(1) : A(0), 0, 0); break;
                case "scalex": Mul(A(0), 0, 0, 1, 0, 0); break;
                case "scaley": Mul(1, 0, 0, A(0), 0, 0); break;
                case "rotate": case "rotatez": { var r = A(0) * Mathf.Deg2Rad; Mul(Mathf.Cos(r), Mathf.Sin(r), -Mathf.Sin(r), Mathf.Cos(r), 0, 0); break; }
                case "rotate3d": { var r = A(3) * Mathf.Deg2Rad * (A(2) >= 0 ? 1f : -1f); Mul(Mathf.Cos(r), Mathf.Sin(r), -Mathf.Sin(r), Mathf.Cos(r), 0, 0); break; }
                case "skew": Mul(1, Mathf.Tan(A(1) * Mathf.Deg2Rad), Mathf.Tan(A(0) * Mathf.Deg2Rad), 1, 0, 0); break;
                case "skewx": Mul(1, 0, Mathf.Tan(A(0) * Mathf.Deg2Rad), 1, 0, 0); break;
                case "skewy": Mul(1, Mathf.Tan(A(0) * Mathf.Deg2Rad), 0, 1, 0, 0); break;
                case "matrix": if (args.Length >= 6) Mul(A(0), A(1), A(2), A(3), A(4), A(5)); break;
                case "matrix3d": if (args.Length >= 16) Mul(A(0), A(1), A(4), A(5), A(12), A(13)); break;
                // rotateX/rotateY: the flat projection of the turn, a foreshortening (a card flip reads as its width closing and reopening)
                case "rotatex": Mul(1, 0, 0, Mathf.Cos(A(0) * Mathf.Deg2Rad), 0, 0); break;
                case "rotatey": Mul(Mathf.Cos(A(0) * Mathf.Deg2Rad), 0, 0, 1, 0, 0); break;
                // perspective: no depth here
            }
            any = true;
        }
        Mul(1, 0, 0, 1, -ox, -oy);
        return any ? m : null;
    }

    /// <summary>A list marker in the text colour: a filled disc, a hollow circle or a filled square.</summary>
    private static void EmitMarker(Ctx ctx, string shape, Color colour, float x, float y, float w, float h, string indent)
    {
        var r = Mathf.Max(1f, Mathf.Min(w, h) * 0.5f);
        var cx = x + w * 0.5f;
        var cy = y + h * 0.5f;
        switch (shape)
        {
            case "tri-right":
                ctx.Body.Append(indent).Append("P d=\"M").AppendNum(cx - r * 0.6f).Append(' ').AppendNum(cy - r).Append(" L").AppendNum(cx + r * 0.8f).Append(' ').AppendNum(cy).Append(" L").AppendNum(cx - r * 0.6f).Append(' ').AppendNum(cy + r).Append(" Z\" f=").AppendHex(colour).Append('\n');
                break;
            case "tri-down":
                ctx.Body.Append(indent).Append("P d=\"M").AppendNum(cx - r).Append(' ').AppendNum(cy - r * 0.6f).Append(" L").AppendNum(cx + r).Append(' ').AppendNum(cy - r * 0.6f).Append(" L").AppendNum(cx).Append(' ').AppendNum(cy + r * 0.8f).Append(" Z\" f=").AppendHex(colour).Append('\n');
                break;
            case "square":
                ctx.Body.Append(indent).Append("R x=").AppendNum(cx - r).Append(" y=").AppendNum(cy - r).Append(" w=").AppendNum(2f * r).Append(" h=").AppendNum(2f * r).Append(" f=").AppendHex(colour).Append('\n');
                break;
            case "circle":
                ctx.Body.Append(indent).Append("C cx=").AppendNum(cx).Append(" cy=").AppendNum(cy).Append(" rx=").AppendNum(r - 0.5f).Append(" ry=").AppendNum(r - 0.5f).Append(" f=none s=").AppendHex(colour).Append(" sw=1\n");
                break;
            default:
                ctx.Body.Append(indent).Append("C cx=").AppendNum(cx).Append(" cy=").AppendNum(cy).Append(" rx=").AppendNum(r).Append(" ry=").AppendNum(r).Append(" f=").AppendHex(colour).Append('\n');
                break;
        }
        ctx.Out.Nodes++;
    }

    /// <summary>
    /// The node id, plus `click=1` on a button, straight into the buffer: the vector mod makes
    /// such a node a hit region and the click arrives at the page element's own on_click with the
    /// node id as value. A button without an id gets its synthetic one, so it can still be clicked.
    /// </summary>
    private static StringBuilder AppendNodeId(this StringBuilder sb, Ctx ctx, VisualElement ve)
    {
        var button = IsButton(ctx, ve);
        if (string.IsNullOrEmpty(ve.name) || (ve.name.StartsWith("__", StringComparison.Ordinal) && !button))
            return sb;
        sb.Append(" id=").Append(ve.name);
        return button ? sb.Append(" click=1") : sb;
    }

    private static bool IsButton(Ctx ctx, VisualElement ve)
    {
        return ctx.Built.NodeOf.TryGetValue(ve, out var node)
               && (node.Tag == "button" || node.Attr("onclick") != null || node.Attr("data-click") != null || node.Attr("data-control") != null)
               && !(ctx.Built.CssOf(ve).TryGetValue("pointer-events", out var pe) && pe.Trim() == "none");
    }

    /// <summary>outline / outline-width / outline-color / outline-offset; false for none.</summary>
    private static bool Outline(Dictionary<string, string> css, out float width, out Color colour, out float offset, out string style)
    {
        width = 0f; colour = Color.white; offset = 0f; style = "none";
        var any = false;
        if (css.TryGetValue("outline", out var shorthand))
        {
            var v = shorthand.Trim();
            if (v == "none" || v == "0") return false;
            foreach (var part in SplitParts(v))
            {
                if (part is "solid" or "dashed" or "dotted" or "double" or "auto") { style = part == "auto" ? "solid" : part; any = true; continue; }
                if (StyleApplier.IsNumber(part) || part.EndsWith("px", StringComparison.OrdinalIgnoreCase)) { width = StyleApplier.Num(part); any = true; }
                else if (StyleApplier.TryColor(part, out var c)) { colour = c; any = true; }
            }
            if (any && width <= 0f) width = 3f; // medium
        }
        if (css.TryGetValue("outline-width", out var wv)) { width = StyleApplier.Num(wv); any = true; }
        if (css.TryGetValue("outline-color", out var cv) && StyleApplier.TryColor(cv, out var cc)) { colour = cc; any = true; }
        if (css.TryGetValue("outline-style", out var sv)) { style = sv.Trim() == "auto" ? "solid" : sv.Trim(); any = true; }
        if (css.TryGetValue("outline-offset", out var ov)) offset = StyleApplier.Num(ov);
        // outline-style's initial value is none, so a width and a colour with no style draw nothing -
        // as in a browser. Before this the style was read only to cancel an outline, never to make one.
        return any && style is not ("none" or "hidden");
    }

    private static List<string> SplitParts(string v)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= v.Length; i++)
        {
            if (i < v.Length)
            {
                if (v[i] == '(') depth++;
                else if (v[i] == ')') depth--;
                if (v[i] != ' ' || depth > 0) continue;
            }
            if (i > start) parts.Add(v.Substring(start, i - start));
            start = i + 1;
        }
        return parts;
    }

    private static void Warn(Ctx ctx, string message)
    {
        if (ctx.Reported.Add(message))
            ctx.Out.Warnings.Add(message);
    }

    internal static string Hex(Color c)
    {
        var r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f);
        var g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f);
        var b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
        var a = Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f);
        var s = "#" + r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture);
        return a >= 255 ? s : s + a.ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Writes a number the way <c>F</c> formats it ("0.##"), straight into the buffer. A frame writes
    /// thousands of numbers, and a string each would be most of a page's garbage.
    /// </summary>
    /// <summary>A tweened value is an expression string; anything else is the plain number.</summary>
    private static StringBuilder AppendVal(this StringBuilder sb, string? expr, float v) => expr != null ? sb.Append(expr) : sb.AppendNum(v);

    private static StringBuilder AppendNum(this StringBuilder sb, float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return sb.Append(v.ToString("0.##", CultureInfo.InvariantCulture));
        if (v < 0f) { sb.Append('-'); v = -v; }
        var hundredths = (long)(v * 100.0 + 0.5);   // "0.##": two decimals, halves away from zero
        var whole = hundredths / 100;
        var rest = (int)(hundredths % 100);
        AppendLong(sb, whole);
        if (rest == 0) return sb;
        sb.Append('.').Append((char)('0' + rest / 10));
        if (rest % 10 != 0) sb.Append((char)('0' + rest % 10));
        return sb;
    }

    private static void AppendLong(StringBuilder sb, long v)
    {
        if (v >= 10) AppendLong(sb, v / 10);
        sb.Append((char)('0' + (int)(v % 10)));
    }

    /// <summary>The colour as #RRGGBB or #RRGGBBAA, straight into the buffer.</summary>
    private static StringBuilder AppendHex(this StringBuilder sb, Color c)
    {
        sb.Append('#');
        Byte2(sb, c.r); Byte2(sb, c.g); Byte2(sb, c.b);
        var a = Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f);
        if (a < 255) Byte2(sb, c.a);
        return sb;
    }

    private static void Byte2(StringBuilder sb, float channel)
    {
        var v = Mathf.RoundToInt(Mathf.Clamp01(channel) * 255f);
        const string hex = "0123456789ABCDEF";
        sb.Append(hex[(v >> 4) & 0xF]).Append(hex[v & 0xF]);
    }


    /// <summary>
    /// The first real family in a font-family list, else what its first generic stands for.
    /// Cached because the answer depends only on the declaration, and every label asked it
    /// again on every frame: a Split, a Trim per name, and a string for each.
    /// </summary>
    private static readonly Dictionary<string, string> FirstFamilies = new(StringComparer.Ordinal);
    private static string FirstFamily(string family)
    {
        lock (FirstFamilies)
            if (FirstFamilies.TryGetValue(family, out var hit)) return hit;
        var first = string.Empty;
        string? generic = null;
        foreach (var raw in family.Split(','))
        {
            var name = raw.Trim().Trim('"', '\'');
            if (name.Length == 0) continue;
            if (IsGeneric(name)) { generic ??= StyleApplier.MapGeneric(name); continue; }
            first = name;
            break;
        }
        if (first.Length == 0 && generic != null) first = generic;
        lock (FirstFamilies) FirstFamilies[family] = first;
        return first;
    }

    /// <summary>"Barlow" + "SemiBold" = "Barlow SemiBold", made once per pair rather than per label per frame.</summary>
    private static readonly Dictionary<(string, string), string> Joined = new();
    private static string Join(string family, string suffix)
    {
        lock (Joined)
        {
            if (Joined.TryGetValue((family, suffix), out var hit)) return hit;
            var made = family + " " + suffix;
            Joined[(family, suffix)] = made;
            return made;
        }
    }

    /// <summary>
    /// value.Trim().ToLowerInvariant(), cached by the declaration text: the same values are
    /// read on every element on every frame, and each read allocated its own copy. Cleared
    /// when it grows past a page's worth, so a script writing fresh values cannot fill it.
    /// </summary>
    private static readonly Dictionary<string, string> Lowered = new(StringComparer.Ordinal);
    private static string Lower(string v)
    {
        lock (Lowered)
        {
            if (Lowered.TryGetValue(v, out var hit)) return hit;
            if (Lowered.Count > 4096) Lowered.Clear();
            var made = v.Trim().ToLowerInvariant();
            Lowered[v] = made;
            return made;
        }
    }

    /// <summary>The indent for a depth, made once: every element wrote a fresh string of spaces per frame.</summary>
    private static readonly List<string> Indents = new() { string.Empty };
    private static string Indent(int depth)
    {
        if (depth < 0) depth = 0;
        lock (Indents)
        {
            while (Indents.Count <= depth) Indents.Add(new string(' ', Indents.Count * 2));
            return Indents[depth];
        }
    }

    private static string F(float v)
    {
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<(string name, string[] args)> Functions(string v)
    {
        var i = 0;
        while (i < v.Length)
        {
            var open = v.IndexOf('(', i);
            if (open < 0) yield break;
            var name = v.Substring(i, open - i).Trim().ToLowerInvariant();
            var close = v.IndexOf(')', open);
            if (close < 0) yield break;
            var args = v.Substring(open + 1, close - open - 1).Split(',');
            for (var k = 0; k < args.Length; k++) args[k] = args[k].Trim();
            yield return (name, args);
            i = close + 1;
        }
    }

    /// <summary>Commas outside brackets, counted without splitting: the layered-background test asks this of every background, every frame.</summary>
    private static int TopLevelCommas(string v)
    {
        var depth = 0;
        var n = 0;
        foreach (var c in v)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0) n++;
        }
        return n;
    }

    private static List<string> SplitTopLevelCommas(string v)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= v.Length; i++)
        {
            if (i < v.Length)
            {
                if (v[i] == '(') depth++;
                else if (v[i] == ')') depth--;
                if (!(v[i] == ',' && depth == 0)) continue;
            }
            list.Add(v.Substring(start, i - start));
            start = i + 1;
        }
        return list;
    }
}

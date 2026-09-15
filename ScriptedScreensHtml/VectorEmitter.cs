using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        public string Scene = string.Empty;
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
        public Vector2 RootOrigin;
        public int Ids;
        public Output Out = new();
        public HashSet<string> Reported = new(StringComparer.Ordinal);
        public Tweens? Tw;
        public float Now;
        /// <summary>Top of the scroll container being emitted, or NaN outside one: what a sticky child pins to.</summary>
        public float ScrollTop = float.NaN;
        /// <summary>Viewport height and scrollable range (content minus viewport) of that container.</summary>
        public float ScrollH, ScrollRange;
        /// <summary>The page's design size, for full-page overlays (::backdrop).</summary>
        public float PageW, PageH;
        /// <summary>Scroll offsets a script set, by box id (HtmlSurface.ScrollSet).</summary>
        public Dictionary<string, (float offset, int version)>? ScrollSet;
        /// <summary>Positioned elements with a z-index, emitted after everything else at the root in z order: a stacking context across parents.</summary>
        public List<(VisualElement ve, Vector2 parentPos, int z)> Deferred = new();
        public bool EmittingDeferred;
    }

    public static Output Emit(HtmlRenderer.Result built, VisualElement root, float designW, float designH, Tweens? tweens = null, float now = 0f, Dictionary<string, (float offset, int version)>? scrollSet = null)
    {
        var ctx = new Ctx { Built = built, RootOrigin = root.worldBound.position, Tw = tweens, Now = now, ScrollSet = scrollSet, PageW = designW, PageH = designH };
        var inv = CultureInfo.InvariantCulture;
        EmitElement(ctx, root, Vector2.zero, 0);
        if (ctx.Deferred.Count > 0)
        {
            ctx.EmittingDeferred = true;
            ctx.Deferred.Sort((a, b) => a.z.CompareTo(b.z));
            foreach (var (dve, dpos, _) in ctx.Deferred)
                EmitElement(ctx, dve, dpos, 1);
        }
        var sb = new StringBuilder();
        sb.Append("SCENE w=").Append(designW.ToString("0.##", inv)).Append(" h=").Append(designH.ToString("0.##", inv)).Append(" fit=stretch\n");
        if (ctx.Defs.Length > 0)
            sb.Append("DEFS {\n").Append(ctx.Defs).Append("}\n");
        sb.Append(ctx.Body);
        ctx.Out.Scene = sb.ToString();
        return ctx.Out;
    }

    // ---------------------------------------------------------------- elements

    private static void EmitElement(Ctx ctx, VisualElement ve, Vector2 parentPos, int depth)
    {
        var rs = ve.resolvedStyle;
        if (rs.display == DisplayStyle.None || rs.visibility == UnityEngine.UIElements.Visibility.Hidden)
            return;

        var layout = ve.layout;
        if (float.IsNaN(layout.width) || float.IsNaN(layout.height))
            return;
        var x = parentPos.x + layout.x;
        var y = parentPos.y + layout.y;
        var w = layout.width;
        var h = layout.height;
        var css = ctx.Built.CssOf(ve);
        var indent = new string(' ', depth * 2);
        var modal = ctx.Built.NodeOf.TryGetValue(ve, out var dialogNode) && dialogNode.Tag == "dialog" && dialogNode.Attr("data-modal") != null;
        if (modal && !ctx.EmittingDeferred)
        {
            // the top layer: a modal dialog paints over everything, whatever its place in the document
            ctx.Deferred.Add((ve, parentPos, int.MaxValue));
            return;
        }
        // a modal dialog dims the page behind it: the ::backdrop pseudo-element, a full-page box under the dialog
        if (modal)
        {
            var bd = PseudoCss(ctx, ve, "backdrop");
            var backdrop = new Color(0f, 0f, 0f, 0.1f);
            if ((bd.TryGetValue("background", out var bdc) || bd.TryGetValue("background-color", out bdc)) && StyleApplier.TryColor(bdc, out var bcol)) backdrop = bcol;
            if (bd.TryGetValue("opacity", out var bdo)) backdrop.a *= StyleApplier.Num(bdo);
            ctx.Body.Append(indent).Append("R x=0 y=0 w=").Append(F(ctx.PageW)).Append(" h=").Append(F(ctx.PageH)).Append(" f=").Append(Hex(backdrop)).Append('\n');
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
        var tw = ctx.Tw?.Of(ve, ctx.Now);
        var scrollTop = float.NaN;
        var scrollCh = 0f;
        // backface-visibility: hidden with a rotateX/rotateY past 90 degrees: the back of the card, not drawn
        if (css.TryGetValue("backface-visibility", out var bfv) && bfv.Trim() == "hidden" && css.TryGetValue("transform", out var bft) && BackfaceTurned(bft))
            return;
        var ws = tw != null ? tw.Lerp(tw.From.Rect.width, w) : F(w);
        var hs = tw != null ? tw.Lerp(tw.From.Rect.height, h) : F(h);

        // Wrappers: position, transform, opacity, clip. Each opens a G that is closed after children.
        var groups = 0;
        if (!float.IsNaN(ctx.ScrollTop) && css.TryGetValue("position", out var pos) && pos.Trim() == "sticky")
        {
            // position: sticky inside a scrolling box: the element scrolls with the content
            // until it would leave the viewport top, then pins `top` below it. sy is the
            // container's scroll offset, so the lift is max(0, (viewportTop + top + sy) - y).
            var top = css.TryGetValue("top", out var t) ? StyleApplier.Num(t) : 0f;
            ctx.Body.Append(indent).Append("G t=[0,\"=max(0,").Append(F(ctx.ScrollTop + top - y)).Append("+sy)\"] {\n");
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
        var offset = css.TryGetValue("offset-path", out var opath) ? Offset(css, opath, ve, x, y, w, h) : null;
        var xform = Xform.From(ve, tw, x, y, w, h, offset);
        if (xform != null) { ctx.Body.Append(indent).Append(xform.Group()).Append(" {\n"); groups++; }
        if (css.TryGetValue("transform", out var tcss) && StyleApplier.NeedsMatrix(tcss) && Matrix(tcss, css, x, y, w, h) is { } m)
        {
            // skew(), matrix(), 3D: the whole list composed into one matrix about the origin (vector requirement 13)
            ctx.Body.Append(indent).Append("G m=[").Append(F(m[0])).Append(',').Append(F(m[1])).Append(',').Append(F(m[2])).Append(',').Append(F(m[3])).Append(',').Append(F(m[4])).Append(',').Append(F(m[5])).Append("] {\n");
            groups++;
        }
        if (css.TryGetValue("filter", out var fcss) && Filters(ctx, fcss, out var filterAttrs, out var filterShadow) && filterAttrs.Length > 0)
        {
            ctx.Body.Append(indent).Append('G').Append(filterAttrs).Append(" {\n");  // colour filters on the subtree (vector requirement 12)
            groups++;
        }
        else filterShadow = css.TryGetValue("filter", out var fcss2) && Filters(ctx, fcss2, out _, out var fs2) ? fs2 : string.Empty;
        if ((css.TryGetValue("clip-path", out var cpath) || css.TryGetValue("-webkit-clip-path", out cpath)) && ClipPath(ctx, cpath, x, y, w, h, xform) is { } clipId)
        {
            ctx.Body.Append(indent).Append("G clip=").Append(clipId).Append(" {\n");
            groups++;
        }
        if ((css.TryGetValue("mask-image", out var mcss) || css.TryGetValue("-webkit-mask-image", out mcss) || css.TryGetValue("mask", out mcss)) && MaskDef(ctx, mcss, x, y, w, h) is { } maskId)
        {
            ctx.Body.Append(indent).Append("G mask=@").Append(maskId).Append(" {\n");  // gradient mask on the subtree (vector requirement 10)
            groups++;
        }
        if (rs.opacity < 0.999f || (tw != null && tw.From.Opacity < 0.999f))
        {
            ctx.Body.Append(indent).Append("G o=").Append(tw != null ? tw.Lerp(tw.From.Opacity, rs.opacity) : F(rs.opacity)).Append(" {\n");
            groups++;
        }
        if (!float.IsNaN(ctx.ScrollTop) && css.TryGetValue("animation-timeline", out var tlc) && tlc.Trim() != "auto" && ScrollTimeline(ctx, ve, css, tlc, x, y, w, h) is { } tlg)
        {
            ctx.Body.Append(indent).Append(tlg).Append(" {\n");  // keyframes as expressions over the scroll offset
            groups++;
        }
        if (ve.style.overflow.value == Overflow.Hidden && w > 0f && h > 0f)
        {
            if (Scrolls(css))
            {
                // overflow: auto / scroll: the vector mod's scroll container. It clips to the
                // box and slides its children by a client-side offset (wheel or drag), so a
                // scroll costs one rebuild and no tick. Children stay in page coordinates.
                var ch = 0f;
                foreach (var child in ve.Children())
                {
                    if (child.resolvedStyle.display == DisplayStyle.None) continue;
                    var cl = child.layout;
                    if (float.IsNaN(cl.yMax)) continue;
                    ch = Mathf.Max(ch, cl.yMax + child.resolvedStyle.marginBottom);
                }
                ch += rs.paddingBottom;
                ctx.Body.Append(indent).Append("SC id=").Append(string.IsNullOrEmpty(ve.name) ? "scroll" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture) : ve.name)
                    .Append(" x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(F(w)).Append(" h=").Append(F(h))
                    .Append(" ch=").Append(F(Mathf.Max(ch, h))).Append(Radius(rs, w, h));
                if (ctx.ScrollSet != null && !string.IsNullOrEmpty(ve.name) && ctx.ScrollSet.TryGetValue(ve.name, out var ss))
                    ctx.Body.Append(" so=").Append(F(ss.offset)).Append(" sov=").Append(ss.version); // applied once per version (vector requirement 7)
                ctx.Body.Append(" {\n");
                ctx.Out.Nodes++;
                groups++;
                scrollTop = y;
                scrollCh = Mathf.Max(ch, h);
            }
            else
            {
                var id = "clip" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
                ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").Append(F(x)).Append(" y=").Append(F(y))
                    .Append(" w=").Append(F(w)).Append(" h=").Append(F(h)).Append(Radius(rs, w, h)).Append(" }\n");
                ctx.Body.Append(indent).Append("G clip=").Append(id).Append(" {\n");
                groups++;
            }
        }

        // Background and border of the box itself.
        var clipText = (css.TryGetValue("background-clip", out var bclip) || css.TryGetValue("-webkit-background-clip", out bclip)) && bclip.Trim() == "text";
        var cornerPath = CornerPath(css, rs, x, y, w, h);
        if (w > 0f && h > 0f && !clipText)
        {
            var bg = rs.backgroundColor;
            css.TryGetValue("background", out var bgCss);
            if (bgCss == null) css.TryGetValue("background-image", out bgCss);
            if (cornerPath != null && tw == null && bg.a > 0.002f && (bgCss == null || !bgCss.Contains("gradient(")) && UrlOf(bgCss ?? string.Empty) == null)
            {
                // corner-shape: the box outline as a path with bevelled, scooped or notched corners
                ctx.Body.Append(indent).Append(cornerPath).Append(" f=").Append(Hex(bg)).Append(shadowOf(css, filterShadow)).Append(NodeId(ctx, ve)).Append('\n');
                ctx.Out.Nodes++;
                bg = Color.clear;
            }
            if (bgCss != null && bgCss.StartsWith("repeating-", StringComparison.OrdinalIgnoreCase)) bgCss = ExpandRepeating(bgCss);
            var shadow = (css.TryGetValue("box-shadow", out var shCss) ? Shadows(shCss) : string.Empty) + filterShadow;
            var imageUrl = bgCss != null ? UrlOf(bgCss) : null;
            if (imageUrl != null)
            {
                // background-image: url(): the colour (if any) under an IMG node (vector requirement 9)
                if (bg.a > 0.002f)
                {
                    ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(ws).Append(" h=").Append(hs)
                        .Append(Radius(rs, w, h)).Append(" f=").Append(Hex(bg)).Append(shadow).Append(NodeId(ctx, ve)).Append('\n');
                    ctx.Out.Nodes++;
                }
                EmitImage(ctx, imageUrl, BackgroundFit(css), css, rs, x, y, w, h, indent, bg.a > 0.002f ? string.Empty : NodeId(ctx, ve));
            }
            else if (tw != null && !Tweens.Snap.NearColour(tw.From.Bg, bg) && (tw.From.Bg.a > 0.002f || bg.a > 0.002f) && !(bgCss != null && bgCss.Contains("gradient(")))
            {
                // A colour transition: a two-stop ramp sampled over the tween's clock (vector requirement 1)
                var gid = "tw" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
                ctx.Defs.Append("  GL id=").Append(gid).Append(" stops=[[0,").Append(Hex(tw.From.Bg)).Append("],[1,").Append(Hex(bg)).Append("]]\n");
                ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(ws).Append(" h=").Append(hs)
                    .Append(Radius(rs, w, h)).Append(" f=@").Append(gid).Append(" fat==").Append(tw.P).Append(shadow).Append(NodeId(ctx, ve)).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (bgCss != null && bgCss.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase))
            {
                GradientBox(ctx, bgCss, x, y, w, h, ws, hs, rs, indent, ve, xform, shadow);
            }
            else if (bgCss != null && bgCss.StartsWith("conic-gradient", StringComparison.OrdinalIgnoreCase) && ConicDef(ctx, bgCss) is { } cid)
            {
                ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(ws).Append(" h=").Append(hs)
                    .Append(Radius(rs, w, h)).Append(" f=@").Append(cid).Append(shadow).Append(NodeId(ctx, ve)).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (bgCss != null && bgCss.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase) && RadialDef(ctx, bgCss) is { } rid)
            {
                ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(ws).Append(" h=").Append(hs)
                    .Append(Radius(rs, w, h)).Append(" f=@").Append(rid).Append(shadow).Append(NodeId(ctx, ve)).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (bg.a > 0.002f)
            {
                ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(ws).Append(" h=").Append(hs)
                    .Append(Radius(rs, w, h)).Append(" f=").Append(Hex(bg)).Append(shadow).Append(NodeId(ctx, ve)).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (IsButton(ctx, ve))
            {
                ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(ws).Append(" h=").Append(hs)
                    .Append(Radius(rs, w, h)).Append(" f=#00000001").Append(NodeId(ctx, ve)).Append('\n');
                ctx.Out.Nodes++;
            }
            else if (shadow.Length > 0)
            {
                // A shadow under a transparent box still casts: an invisible fill carries it.
                ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(ws).Append(" h=").Append(hs)
                    .Append(Radius(rs, w, h)).Append(" f=#00000001").Append(shadow).Append('\n');
                ctx.Out.Nodes++;
            }

            if (Outline(css, out var ow, out var oc, out var ooff) && ow > 0.01f && oc.a > 0.002f)
            {
                // Outside the border box, offset by outline-offset, stroke centred on its path.
                var od = ooff + ow * 0.5f;
                ctx.Body.Append(indent).Append("R x=").Append(F(x - od)).Append(" y=").Append(F(y - od))
                    .Append(" w=").Append(F(w + 2f * od)).Append(" h=").Append(F(h + 2f * od))
                    .Append(Radius(rs, w + 2f * od, h + 2f * od, od)).Append(" f=none s=").Append(Hex(oc)).Append(" sw=").Append(F(ow))
                    .Append('\n');
                ctx.Out.Nodes++;
            }

            var bw = rs.borderTopWidth;
            var sameWidth = Mathf.Approximately(bw, rs.borderRightWidth) && Mathf.Approximately(bw, rs.borderBottomWidth) && Mathf.Approximately(bw, rs.borderLeftWidth);
            var sameColour = rs.borderTopColor == rs.borderRightColor && rs.borderTopColor == rs.borderBottomColor && rs.borderTopColor == rs.borderLeftColor;
            var rounded = rs.borderTopLeftRadius > 0.01f || rs.borderTopRightRadius > 0.01f || rs.borderBottomRightRadius > 0.01f || rs.borderBottomLeftRadius > 0.01f;
            var styles = new[] { SideStyle(css, "top", 0), SideStyle(css, "right", 1), SideStyle(css, "bottom", 2), SideStyle(css, "left", 3) };
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
                ctx.Body.Append(indent).Append(CornerPath(css, rs, x + bw * 0.5f, y + bw * 0.5f, w - bw, h - bw)).Append(" f=none s=").Append(Hex(rs.borderTopColor)).Append(" sw=").Append(F(bw)).Append('\n');
                ctx.Out.Nodes++;
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
                    ctx.Body.Append(indent).Append("R x=").Append(F(x + inset)).Append(" y=").Append(F(y + inset)).Append(" w=").Append(F(w - 2f * inset)).Append(" h=").Append(F(h - 2f * inset))
                        .Append(Radius(rs, w, h, -inset)).Append(" f=none s=").Append(Hex(rs.borderTopColor)).Append(" sw=").Append(F(third)).Append('\n');
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
                SideArcs(ctx, indent, x, y, w, h, bw, rs);
            }
            else if (bw > 0.01f && rs.borderTopColor.a > 0.002f && sameWidth)
            {
                // A stroke is centred on its path: inset by half the width so it stays inside the box.
                var half = bw * 0.5f;
                ctx.Body.Append(indent).Append("R x=").Append(F(x + half)).Append(" y=").Append(F(y + half))
                    .Append(" w=").Append(tw != null ? tw.Lerp(tw.From.Rect.width - bw, w - bw) : F(w - bw))
                    .Append(" h=").Append(tw != null ? tw.Lerp(tw.From.Rect.height - bw, h - bw) : F(h - bw))
                    .Append(Radius(rs, w, h, -half)).Append(" f=none s=").Append(Hex(rs.borderTopColor)).Append(" sw=").Append(F(bw)).Append(Dash(css, bw)).Append('\n');
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
                    EmitImage(ctx, src, css.TryGetValue("object-fit", out var of) ? of.Trim() : "fill", css, rs, x, y, w, h, indent, NodeId(ctx, ve));
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
                ctx.Body.Append(indent).Append("G a=[").Append(F(cx)).Append(',').Append(F(cy)).Append("] r=").Append(F(angle)).Append(" {\n");
                EmitText(ctx, label, css, cx - h * 0.5f, cy - w * 0.5f, h, w, indent + "  ");
                ctx.Body.Append(indent).Append("}\n");
                break;
            }
            case Label label:
                EmitText(ctx, label, css, x, y, w, h, indent);
                break;
            case SvgElement svg:
                EmitSvg(ctx, svg, x, y, w, h, indent);
                break;
            case CanvasElement cv:
                EmitCanvas(ctx, cv, css, x, y, w, h, indent);
                break;
            default:
            {
                var outer = (ctx.ScrollTop, ctx.ScrollH, ctx.ScrollRange);
                if (!float.IsNaN(scrollTop)) { ctx.ScrollTop = scrollTop; ctx.ScrollH = h; ctx.ScrollRange = Mathf.Max(0f, scrollCh - h); }
                foreach (var child in ByZIndex(ctx, ve))
                    EmitElement(ctx, child, new Vector2(x, y), depth + groups);
                if (!float.IsNaN(scrollTop)) EmitScrollbar(ctx, ve, css, x, y, w, h, scrollCh, indent + "  ");
                (ctx.ScrollTop, ctx.ScrollH, ctx.ScrollRange) = outer;
                break;
            }
        }

        for (var i = 0; i < groups; i++)
            ctx.Body.Append(indent).Append("}\n");
    }

    private static void SideArcs(Ctx ctx, string indent, float x, float y, float w, float h, float bw, IResolvedStyle rs)
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
        Arc(ctx, indent, bw, rs.borderTopColor, mtl, tl, new Vector2(x + tl, y), new Vector2(x + w - tr, y), tr, mtr);
        Arc(ctx, indent, bw, rs.borderRightColor, mtr, tr, new Vector2(x + w, y + tr), new Vector2(x + w, y + h - br), br, mbr);
        Arc(ctx, indent, bw, rs.borderBottomColor, mbr, br, new Vector2(x + w - br, y + h), new Vector2(x + bl, y + h), bl, mbl);
        Arc(ctx, indent, bw, rs.borderLeftColor, mbl, bl, new Vector2(x, y + h - bl), new Vector2(x, y + tl), tl, mtl);
    }

    private static void Arc(Ctx ctx, string indent, float bw, Color c, Vector2 from, float r0, Vector2 a, Vector2 b, float r1, Vector2 to)
    {
        if (c.a <= 0.002f) return;
        var d = new StringBuilder("M ").Append(F(from.x)).Append(' ').Append(F(from.y));
        if (r0 > 0.01f) d.Append(" A ").Append(F(r0)).Append(' ').Append(F(r0)).Append(" 0 0 1 ").Append(F(a.x)).Append(' ').Append(F(a.y));
        d.Append(" L ").Append(F(b.x)).Append(' ').Append(F(b.y));
        if (r1 > 0.01f) d.Append(" A ").Append(F(r1)).Append(' ').Append(F(r1)).Append(" 0 0 1 ").Append(F(to.x)).Append(' ').Append(F(to.y));
        ctx.Body.Append(indent).Append("P d=\"").Append(d).Append("\" f=none s=").Append(Hex(c)).Append(" sw=").Append(F(bw)).Append(" cap=butt\n");
        ctx.Out.Nodes++;
    }

    /// <summary>Children in paint order: z-index ascending, document order within a value.</summary>
    private static List<VisualElement> ByZIndex(Ctx ctx, VisualElement ve)
    {
        var list = new List<(int z, int i, VisualElement c)>();
        var i = 0;
        foreach (var child in ve.Children())
        {
            var z = 0;
            if (ctx.Built.CssOf(child).TryGetValue("z-index", out var zs))
                int.TryParse(zs.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z);
            list.Add((z, i++, child));
        }
        list.Sort((a, b) => a.z != b.z ? a.z.CompareTo(b.z) : a.i.CompareTo(b.i));
        var result = new List<VisualElement>(list.Count);
        foreach (var e in list) result.Add(e.c);
        return result;
    }

    private static void Side(Ctx ctx, string indent, float x, float y, float w, float h, Color c)
    {
        if (w <= 0.01f || h <= 0.01f || c.a <= 0.002f)
            return;
        ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(F(w)).Append(" h=").Append(F(h)).Append(" f=").Append(Hex(c)).Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>
    /// The G that places an element on its offset-path: path("...") in the containing
    /// block's coordinates, sampled at offset-distance (px or % of the length), turned by
    /// offset-rotate (auto = along the tangent, an angle, or auto plus an angle).
    /// ponytail: ray()/shapes as offset-path and offset-anchor are not read; arcs flatten to a chord.
    /// </summary>
    private static (float dx, float dy, float rot)? Offset(Dictionary<string, string> css, string pathCss, VisualElement ve, float x, float y, float w, float h)
    {
        var open = pathCss.IndexOf("path(", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var close = pathCss.LastIndexOf(')');
        if (close <= open) return null;
        var d = pathCss.Substring(open + 5, close - open - 5).Trim().Trim('"', '\'');
        var pts = FlattenPath(d);
        if (pts.Count < 2) return null;
        var total = 0f;
        for (var i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
        var dist = 0f;
        if (css.TryGetValue("offset-distance", out var od))
            dist = od.Trim().EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(od) / 100f * total : StyleApplier.Num(od);
        dist = Mathf.Clamp(dist, 0f, total);
        var p = pts[0];
        var tangent = pts[1] - pts[0];
        var run = 0f;
        for (var i = 1; i < pts.Count; i++)
        {
            var seg = Vector2.Distance(pts[i - 1], pts[i]);
            if (seg <= 0f) continue;
            if (run + seg >= dist || i == pts.Count - 1)
            {
                var f = Mathf.Clamp01((dist - run) / seg);
                p = Vector2.Lerp(pts[i - 1], pts[i], f);
                tangent = pts[i] - pts[i - 1];
                break;
            }
            run += seg;
        }
        var along = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
        var rot = along;
        if (css.TryGetValue("offset-rotate", out var orr))
        {
            var parts = orr.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rot = 0f;
            foreach (var part in parts)
            {
                if (part == "auto") rot += along;
                else if (part == "reverse") rot += along + 180f;
                else rot += Degrees(part);
            }
        }
        // the path is in the containing block's space: the parent's origin in page coordinates
        var ox = x - ve.layout.x;
        var oy = y - ve.layout.y;
        var cx = x + w * 0.5f;
        var cy = y + h * 0.5f;
        return (ox + p.x - cx, oy + p.y - cy, rot);
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

    /// <summary>A CSS transform as the vector G attributes, kept numeric so clip
    /// polygons declared in scene space can be put through the same transform.</summary>
    private sealed class Xform
    {
        public float Ax, Ay, Tx, Ty, R, Sx = 1f, Sy = 1f;
        /// <summary>offset-path: a translation and turn applied under the transform, both ends of a tween alike.</summary>
        private float _ox, _oy, _or;
        private Tweens.Tween? _tw;

        /// <summary>The element's resolved transform (UI Toolkit has already applied the CSS), tweened if one is running.</summary>
        public static Xform? From(VisualElement ve, Tweens.Tween? tw, float x, float y, float w, float h, (float dx, float dy, float rot)? offset = null)
        {
            var rs = ve.resolvedStyle;
            var xf = new Xform
            {
                Ax = x + w * 0.5f, Ay = y + h * 0.5f,
                Tx = rs.translate.x + (offset?.dx ?? 0f), Ty = rs.translate.y + (offset?.dy ?? 0f),
                R = rs.rotate.angle.ToDegrees() + (offset?.rot ?? 0f),
                Sx = rs.scale.value.x, Sy = rs.scale.value.y,
                _ox = offset?.dx ?? 0f, _oy = offset?.dy ?? 0f, _or = offset?.rot ?? 0f,
                _tw = tw != null && tw.From.TransformDiffers(tw.To) ? tw : null,
            };
            var identity = Mathf.Abs(xf.Tx) < 0.01f && Mathf.Abs(xf.Ty) < 0.01f && Mathf.Abs(xf.R) < 0.01f
                           && Mathf.Abs(xf.Sx - 1f) < 0.001f && Mathf.Abs(xf.Sy - 1f) < 0.001f;
            return identity && xf._tw == null ? null : xf;
        }

        public string Group()
        {
            var sb = new StringBuilder("G a=[").Append(F(Ax)).Append(',').Append(F(Ay)).Append(']');
            if (_tw != null)
            {
                var f = _tw.From;
                sb.Append(" t=[\"").Append(_tw.Lerp(f.Translate.x + _ox, Tx)).Append("\",\"").Append(_tw.Lerp(f.Translate.y + _oy, Ty)).Append("\"]");
                sb.Append(" r=").Append(_tw.Lerp(f.Rotate + _or, R));
                sb.Append(" s=[\"").Append(_tw.Lerp(f.Scale.x, Sx)).Append("\",\"").Append(_tw.Lerp(f.Scale.y, Sy)).Append("\"]");
                return sb.ToString();
            }
            if (Tx != 0f || Ty != 0f) sb.Append(" t=[").Append(F(Tx)).Append(',').Append(F(Ty)).Append(']');
            if (R != 0f) sb.Append(" r=").Append(F(R));
            if (Sx != 1f || Sy != 1f) sb.Append(" s=[").Append(F(Sx)).Append(',').Append(F(Sy)).Append(']');
            return sb.ToString();
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
    /// <summary>CSS box-shadow list to the vector `sh` list: [[dx,dy,blur,spread,#colour],...]. Inset shadows are skipped.</summary>
    private static string Shadows(string css, int max = int.MaxValue)
    {
        var sb = new StringBuilder();
        var count = 0;
        foreach (var item in SplitTopLevelCommas(css))
        {
            if (count >= max) break;
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
            sb.Append('[').Append(F(nums[0])).Append(',').Append(F(nums[1])).Append(',').Append(F(nums[2])).Append(',').Append(F(nums[3])).Append(',').Append(Hex(colour));
            if (inset) sb.Append(",inset"); // vector requirement 2 / 3
            sb.Append(']');
            count++;
        }
        return sb.Length > 0 ? " sh=[" + sb + "]" : string.Empty;
    }

    /// <summary>border-style dashed/dotted (from the shorthand or the property) as a dash pattern in border widths.</summary>
    private static string Dash(Dictionary<string, string> css, float bw) => DashFor(SideStyle(css, "top", 0), bw);

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
        var id = "rad" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
        ctx.Defs.Append("  GR id=").Append(id).Append(" units=bbox cx=").Append(F(cx)).Append(" cy=").Append(F(cy)).Append(" r=").Append(F(r)).Append(" stops=[");
        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0) ctx.Defs.Append(',');
            ctx.Defs.Append('[').Append(F(stops[i].at)).Append(',').Append(Hex(stops[i].c)).Append(']');
        }
        ctx.Defs.Append("]\n");
        return id;
    }

    private static void GradientBox(Ctx ctx, string css, float x, float y, float w, float h, string ws, string hs, IResolvedStyle rs, string indent, VisualElement ve, Xform? xf, string shadow = "")
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
            var gid = "grad" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
            GradientDefLine(ctx, gid, dx, dy, 0f, 1f, stops);
            ctx.Body.Append(indent).Append('R').Append(rect).Append(" f=@").Append(gid).Append(NodeId(ctx, ve)).Append('\n');
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

            var cid = "cut" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
            ctx.Defs.Append("  CP id=").Append(cid).Append(" { Y p=[");
            for (var i = 0; i < poly.Count; i++)
            {
                var p = xf != null ? xf.Apply(poly[i]) : poly[i];
                if (i > 0) ctx.Defs.Append(',');
                ctx.Defs.Append(F(p.x)).Append(',').Append(F(p.y));
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
                var gid = "grad" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
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
        ctx.Defs.Append("  GL id=").Append(id).Append(" units=bbox x1=").Append(F(ax + (bx - ax) * p0)).Append(" y1=").Append(F(ay + (by - ay) * p0))
            .Append(" x2=").Append(F(ax + (bx - ax) * p1)).Append(" y2=").Append(F(ay + (by - ay) * p1)).Append(" stops=[");
        for (var i = 0; i < stops.Count; i++)
        {
            if (i > 0) ctx.Defs.Append(',');
            ctx.Defs.Append('[').Append(F(stops[i].at)).Append(',').Append(Hex(stops[i].c)).Append(']');
        }
        ctx.Defs.Append("]\n");
    }

    /// <summary>CSS linear-gradient(...) to an angle and positioned stops. Null when unusable.</summary>
    private static (float angle, List<(float at, Color c)> stops)? ParseGradient(string css)
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

    private static void EmitText(Ctx ctx, Label label, Dictionary<string, string> css, float x, float y, float w, float h, string indent)
    {
        var rs = label.resolvedStyle;
        var text = label.text ?? string.Empty;
        if (text.Length == 0)
            return;
        if (css.TryGetValue("text-transform", out var tt))
            text = Transform(text, tt.Trim().ToLowerInvariant());
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
        }
        // Scene text escapes (vector mod 0.10.1.0): backslash first, then the quote; a line
        // break in the text becomes the two characters backslash-n.
        text = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        // The scene reader types a clean number as a number even when quoted, and a number
        // has no text: "3" vanished from the footer. TextMeshPro's <noparse> keeps it a string.
        if (float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            text = "<noparse>" + text + "</noparse>";
        // font-variant-numeric: tabular-nums: TextMeshPro has no OpenType features, but it can
        // monospace a span; digits get an em-fraction cell, the rest stays proportional.
        if (css.TryGetValue("font-variant-numeric", out var fvn) && fvn.Contains("tabular"))
            text = System.Text.RegularExpressions.Regex.Replace(text, "[0-9]+", m => "<mspace=0.6em>" + m.Value + "</mspace>");
        var align = rs.unityTextAlign;
        var centre = align == TextAnchor.MiddleCenter || align == TextAnchor.UpperCenter || align == TextAnchor.LowerCenter;
        var right = align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight;
        // A label in a scrolling box is not clipped to a line: the container slides it.
        var clipped = (label.style.overflow.value == Overflow.Hidden && !Scrolls(css))
                      || (label.parent != null && label.parent.style.overflow.value == Overflow.Hidden && !Scrolls(ctx.Built.CssOf(label.parent)));
        var wraps = rs.whiteSpace == WhiteSpace.Normal && rs.fontSize > 0f && h > rs.fontSize * 1.6f && (text.IndexOf(' ') >= 0 || text.IndexOf('​') >= 0);
        if (!clipped && !wraps)
        {
            // The layout width is UI Toolkit's measure of the text; TextMeshPro measures the
            // same face a little wider and wraps a shrink-wrapped label ("GA" / "S"). The
            // rect only positions the text, so give it slack on the side alignment allows.
            var slack = w * 0.35f + 8f;
            if (centre) x -= slack * 0.5f;
            else if (right) x -= slack;
            w += slack;
        }
        var sb = new StringBuilder();
        sb.Append(indent).Append("T x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(F(w)).Append(" h=").Append(F(h));
        sb.Append(" text=\"").Append(text).Append('"');
        sb.Append(" size=").Append(F(rs.fontSize));
        var ttw = ctx.Tw?.Of(label, ctx.Now);
        var textClip = (css.TryGetValue("background-clip", out var tbc) || css.TryGetValue("-webkit-background-clip", out tbc)) && tbc.Trim() == "text";
        var textGrad = textClip && (css.TryGetValue("background", out var tbg) || css.TryGetValue("background-image", out tbg)) && tbg.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase) ? ParseGradient(tbg) : null;
        if (textGrad != null)
        {
            // background-clip: text with a gradient: the glyphs take the gradient (vector requirement 14)
            var (angle, stops) = textGrad.Value;
            var rad = angle * Mathf.Deg2Rad;
            var len = w * Mathf.Abs(Mathf.Sin(rad)) + h * Mathf.Abs(Mathf.Cos(rad));
            var gid = "tg" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
            GradientDefLine(ctx, gid, Mathf.Sin(rad) * len * 0.5f / Mathf.Max(1f, w), -Mathf.Cos(rad) * len * 0.5f / Mathf.Max(1f, h), 0f, 1f, stops);
            sb.Append(" f=@").Append(gid);
        }
        else if (ttw != null && !Tweens.Snap.NearColour(ttw.From.Fg, rs.color))
        {
            var gid = "tw" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
            ctx.Defs.Append("  GL id=").Append(gid).Append(" stops=[[0,").Append(Hex(ttw.From.Fg)).Append("],[1,").Append(Hex(rs.color)).Append("]]\n");
            sb.Append(" f=@").Append(gid).Append(" fat==").Append(ttw.P);
        }
        else
            sb.Append(" f=").Append(Hex(rs.color));
        var first = string.Empty;
        var fs = rs.unityFontStyleAndWeight;
        var wantBold = fs == FontStyle.Bold || fs == FontStyle.BoldAndItalic;
        if (css.TryGetValue("font-family", out var family))
        {
            // The first real family, else what the first generic stands for here.
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
            if (first.Length > 0 && !NamedWeight(first))
            {
                // A weight or stretch the family has as a real face beats a synthetic one:
                // Barlow ships Thin..Black and Condensed, and TextMeshPro's synthetic bold
                // only widens glyphs.
                if (css.TryGetValue("font-stretch", out var stretch) && stretch.IndexOf("condensed", StringComparison.OrdinalIgnoreCase) >= 0 && FontLibrary.Get(first + " Condensed") != null)
                    first += " Condensed";
                var weight = WeightFace(css, wantBold);
                if (weight != null && FontLibrary.Get(first + " " + weight) != null)
                {
                    first += " " + weight;
                    wantBold = false;
                }
            }
            if (first.Length > 0)
                sb.Append(" font=\"").Append(FontLibrary.ResolveFace(first)).Append('"');
        }
        // A face that is already a named weight ("Barlow SemiBold") must not be bolded again:
        // TextMeshPro's synthetic bold widens every glyph on top of it.
        if (wantBold && !NamedWeight(first))
            sb.Append(" weight=bold");
        if (css.TryGetValue("letter-spacing", out var ls) && rs.fontSize > 0f)
        {
            // TextMeshPro's characterSpacing is hundredths of an em.
            var px = ls.EndsWith("em", StringComparison.OrdinalIgnoreCase) ? StyleApplier.Num(ls) * rs.fontSize : StyleApplier.Num(ls);
            if (px != 0f) sb.Append(" cspace=").Append(F(px / rs.fontSize * 100f));
        }
        // text-align-last: the last line's alignment, which for a single-line label is the line
        var lastAlign = !wraps && css.TryGetValue("text-align-last", out var tal) ? tal.Trim().ToLowerInvariant() : null;
        if (lastAlign is "center") sb.Append(" align=center");
        else if (lastAlign is "right" or "end") sb.Append(" align=right");
        else if (lastAlign is "left" or "start") { }
        else if (css.TryGetValue("text-align", out var ta) && ta.Trim() == "justify") sb.Append(" align=justified");
        else if (centre) sb.Append(" align=center");
        else if (right) sb.Append(" align=right");
        sb.Append(" valign=middle");
        if (clipped)
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
            if (fl.TryGetValue("color", out var flc) && StyleApplier.TryColor(flc, out var flcol)) attrs.Append(" f=").Append(Hex(flcol));
            if (fl.TryGetValue("font-size", out var fls)) attrs.Append(" size=").Append(F(fls.EndsWith("em", StringComparison.OrdinalIgnoreCase) ? StyleApplier.Num(fls) * rs.fontSize : fls.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(fls) / 100f * rs.fontSize : StyleApplier.Num(fls)));
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
                    if (fl.TryGetValue("color", out var c1) && StyleApplier.TryColor(c1, out var col1)) { open.Append("<color=").Append(Hex(col1)).Append('>'); close.Insert(0, "</color>"); }
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
            if (mult > 0f && v != "normal") sb.Append(" lh=").Append(F(mult));
        }
        sb.Append(NodeId(ctx, label)).Append('\n');
        ctx.Body.Append(sb);
        ctx.Out.Nodes++;
        if (drawDeco)
            EmitDecoration(ctx, label, deco, rs, ox, y, ow, h, centre, right, indent);
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
        if (css.TryGetValue("text-decoration-style", out var ts)) d.style = ts.Trim().ToLowerInvariant();
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
    private static void EmitDecoration(Ctx ctx, Label label, Deco d, IResolvedStyle rs, float x, float y, float w, float h, bool centre, bool right, string indent)
    {
        var fs = rs.fontSize;
        if (fs <= 0f) return;
        var tw = label.MeasureTextSize(label.text ?? string.Empty, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
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
                var sb = new StringBuilder("P d=\"M").Append(F(left)).Append(' ').Append(F(ly));
                var n = Mathf.Min(400, Mathf.CeilToInt(tw / (period * 0.5f)));
                for (var i = 0; i < n; i++)
                {
                    var x0 = left + i * period * 0.5f;
                    var x1 = Mathf.Min(left + tw, x0 + period * 0.5f);
                    var cy = ly + (i % 2 == 0 ? -1f : 1f) * t * 2f;
                    sb.Append(" Q").Append(F((x0 + x1) * 0.5f)).Append(' ').Append(F(cy)).Append(' ').Append(F(x1)).Append(' ').Append(F(ly));
                }
                sb.Append("\" f=none s=").Append(Hex(colour)).Append(" sw=").Append(F(t)).Append('\n');
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
        ctx.Body.Append(indent).Append("L p=[").Append(F(x1)).Append(',').Append(F(y1)).Append(',').Append(F(x2)).Append(',').Append(F(y2)).Append("] s=").Append(Hex(c)).Append(" sw=").Append(F(width)).Append(extra).Append('\n');
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
        var v = w.Trim().ToLowerInvariant();
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

    private static bool NamedWeight(string family)
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
        var id = "svg" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
        ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(F(w)).Append(" h=").Append(F(h)).Append(Radius(svg.resolvedStyle, w, h)).Append(" }\n");

        // Uniform fit: one scaled group. Non-uniform (preserveAspectRatio="none" on a box of
        // another shape): the scale is baked into every coordinate instead, so a stroke keeps
        // one width in every direction rather than being stretched with the box.
        var group = " t=[" + F(ox - vb.x * sx) + "," + F(oy - vb.y * sy) + "] s=[" + F(sx) + "," + F(sy) + "]";
        var bake = Mathf.Abs(sx - sy) > 0.01f * Mathf.Max(sx, sy);
        var fit = bake ? new Fit(ox - vb.x * sx, oy - vb.y * sy, sx, sy, group) : new Fit(0f, 0f, 1f, 1f, null);
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

        if (shape.Tag == "clipPath")
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
            var href = shape.Attr("href") ?? string.Empty;
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
                    sb.Append(" x==").Append(F(fit.Px(vb.x))).Append('+').Append(F(fit.Sx * vb.width / (count - 1))).Append("*i");
                    sb.Append(" y=").Append(fit.Y("=$" + bound + "[i]"));
                    if (shape.Tag == "polygon")
                        sb.Append(" y2=").Append(F(fit.Py(vb.y + vb.height)));
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

        // Paint. SVG defaults: fill black, no stroke; a line/polyline has no fill.
        var fill = shape.Attr("fill") ?? (shape.Tag == "line" || shape.Tag == "polyline" ? "none" : "black");
        sb.Append(" f=").Append(Paint(fill, prefix));
        var fo = Mul(shape.Attr("fill-opacity"), opacity);
        if (fo != null) sb.Append(" fo=").Append(fo);
        var stroke = shape.Attr("stroke");
        if (stroke != null && stroke != "none")
        {
            sb.Append(" s=").Append(Paint(stroke, prefix));
            // A path under its own scaled group keeps its width in viewBox units.
            sb.Append(" sw=").Append(pathGroup ? Expr(shape.Attr("stroke-width") ?? "1") : fit.Len(shape.Attr("stroke-width") ?? "1"));
            var so = Mul(shape.Attr("stroke-opacity"), opacity);
            if (so != null) sb.Append(" so=").Append(so);
            var cap = shape.Attr("stroke-linecap");
            if (cap != null) sb.Append(" cap=").Append(cap);
            var join = shape.Attr("stroke-linejoin");
            if (join != null) sb.Append(" join=").Append(join);
        }
        foreach (var name in Passthrough)
        {
            if (name == "lod" && repeat != null) continue;
            var v = shape.Attr(name);
            if (v != null) sb.Append(' ').Append(name).Append('=').Append(Expr(v));
        }
        // dashes, miter limit and fill rule in their SVG spellings
        if (shape.Attr("stroke-dasharray") is { } da && shape.Attr("dash") == null && da.Trim() != "none")
        {
            var parts = da.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new StringBuilder();
            foreach (var p in parts) { if (list.Length > 0) list.Append(','); list.Append(pathGroup ? Expr(p) : fit.Len(p)); }
            if (list.Length > 0) sb.Append(" dash=[").Append(list).Append(']');
        }
        if (shape.Attr("stroke-dashoffset") is { } dofs && shape.Attr("dofs") == null) sb.Append(" dofs=").Append(pathGroup ? Expr(dofs) : fit.Len(dofs));
        if (shape.Attr("stroke-miterlimit") is { } ml && shape.Attr("ml") == null) sb.Append(" ml=").Append(Expr(ml));
        if (shape.Attr("fill-rule") is { } frule && shape.Attr("fr") == null) sb.Append(" fr=").Append(frule.Trim());
        var sid = shape.Attr("id");
        if (sid != null) sb.Append(" id=").Append(sid);
        if (pathGroup) sb.Append(" }");
        if (repeat != null) sb.Append(" }");
        for (var i = 0; i < wrappers; i++) sb.Append(" }");
        ctx.Body.Append(sb).Append('\n');
        ctx.Out.Nodes++;
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
        sb.Append("T x=").Append(F(left)).Append(" y=").Append(F(top)).Append(" w=").Append(F(width)).Append(" h=").Append(F(height)).Append(" text=\"").Append(esc).Append("\" size=").Append(F(ps));
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
        if (shape.Attr("letter-spacing") is { } ls && ps > 0f) sb.Append(" cspace=").Append(F(StyleApplier.Num(ls) / size * 100f));
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
            stops.Append('[').Append(F(Fraction(parts[0]))).Append(',').Append(Hex(c)).Append(']');
            count++;
        }
        if (count == 0) return;
        var bbox = !string.Equals(g.Attr("gradientUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
        var units = bbox ? " units=bbox" : string.Empty;
        if (g.Tag == "radialGradient")
        {
            var cx = g.Attr("cx") ?? "50%"; var cy = g.Attr("cy") ?? "50%"; var r = g.Attr("r") ?? "50%";
            ctx.Defs.Append("  GR id=").Append(prefix).Append(id).Append(units)
                .Append(" cx=").Append(F(Fraction(cx))).Append(" cy=").Append(F(Fraction(cy))).Append(" r=").Append(F(Fraction(r)));
            if (g.Attr("fx") != null) ctx.Defs.Append(" fx=").Append(F(Fraction(g.Attr("fx")!)));
            if (g.Attr("fy") != null) ctx.Defs.Append(" fy=").Append(F(Fraction(g.Attr("fy")!)));
        }
        else
        {
            ctx.Defs.Append("  GL id=").Append(prefix).Append(id).Append(units)
                .Append(" x1=").Append(F(Fraction(g.Attr("x1") ?? "0"))).Append(" y1=").Append(F(Fraction(g.Attr("y1") ?? "0")))
                .Append(" x2=").Append(F(Fraction(g.Attr("x2") ?? "100%"))).Append(" y2=").Append(F(Fraction(g.Attr("y2") ?? "0")));
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

    private static string Radius(IResolvedStyle rs, float w, float h, float inset = 0f)
    {
        var tl = rs.borderTopLeftRadius;
        var tr = rs.borderTopRightRadius;
        var br = rs.borderBottomRightRadius;
        var bl = rs.borderBottomLeftRadius;
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
        if (tl <= 0.01f && tr <= 0.01f && br <= 0.01f && bl <= 0.01f)
            return string.Empty;
        if (Mathf.Approximately(tl, tr) && Mathf.Approximately(tl, br) && Mathf.Approximately(tl, bl))
            return " rx=" + F(tl);
        return " rx=[" + F(tl) + "," + F(tr) + "," + F(br) + "," + F(bl) + "]";
    }

    /// <summary>
    /// A checkbox or radio as a browser draws one: unchecked, a ring or rounded box stroked in
    /// the text colour; checked, filled with the accent (`accent-color`, else the page's link
    /// blue) with a white tick or dot. The click region is the background rect emitted above.
    /// </summary>
    private static void EmitCheck(Ctx ctx, string control, HtmlNode node, Dictionary<string, string> css, IResolvedStyle rs, float x, float y, float w, float h, string indent)
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
                ctx.Body.Append(indent).Append("C cx=").Append(F(cx)).Append(" cy=").Append(F(cy)).Append(" rx=").Append(F(size * 0.5f)).Append(" ry=").Append(F(size * 0.5f)).Append(" f=").Append(Hex(accent)).Append('\n');
                ctx.Body.Append(indent).Append("C cx=").Append(F(cx)).Append(" cy=").Append(F(cy)).Append(" rx=").Append(F(size * 0.2f)).Append(" ry=").Append(F(size * 0.2f)).Append(" f=#FFFFFF\n");
                ctx.Out.Nodes += 2;
            }
            else
            {
                ctx.Body.Append(indent).Append("C cx=").Append(F(cx)).Append(" cy=").Append(F(cy)).Append(" rx=").Append(F(r)).Append(" ry=").Append(F(r)).Append(" f=none s=").Append(Hex(ink)).Append(" sw=").Append(F(sw)).Append('\n');
                ctx.Out.Nodes++;
            }
            return;
        }
        var rx = Mathf.Max(0f, rs.borderTopLeftRadius > 0.01f ? rs.borderTopLeftRadius : size * 0.18f);
        if (on)
        {
            ctx.Body.Append(indent).Append("R x=").Append(F(left)).Append(" y=").Append(F(top)).Append(" w=").Append(F(size)).Append(" h=").Append(F(size)).Append(" rx=").Append(F(rx)).Append(" f=").Append(Hex(accent)).Append('\n');
            // The tick: two strokes from the left third, down to the bottom, up to the top right.
            var d = "M" + F(left + size * 0.24f) + " " + F(top + size * 0.52f) + " L" + F(left + size * 0.43f) + " " + F(top + size * 0.72f) + " L" + F(left + size * 0.78f) + " " + F(top + size * 0.3f);
            ctx.Body.Append(indent).Append("P d=\"").Append(d).Append("\" f=none s=#FFFFFF sw=").Append(F(Mathf.Max(1.2f, size * 0.13f))).Append(" cap=round join=round\n");
            ctx.Out.Nodes += 2;
        }
        else
        {
            ctx.Body.Append(indent).Append("R x=").Append(F(left + sw * 0.5f)).Append(" y=").Append(F(top + sw * 0.5f)).Append(" w=").Append(F(size - sw)).Append(" h=").Append(F(size - sw)).Append(" rx=").Append(F(Mathf.Max(0f, rx - sw * 0.5f))).Append(" f=none s=").Append(Hex(ink)).Append(" sw=").Append(F(sw)).Append('\n');
            ctx.Out.Nodes++;
        }
    }

    private static bool Scrolls(Dictionary<string, string> css)
    {
        return (css.TryGetValue("overflow", out var o) || css.TryGetValue("overflow-y", out o)) && o.Trim() is "auto" or "scroll";
    }

    /// <summary>progress and meter: a rounded track in the box's background (else a dim grey) and a fill in the accent, or the meter's low/high colour.</summary>
    private static void EmitBar(Ctx ctx, string control, HtmlNode node, Dictionary<string, string> css, IResolvedStyle rs, float x, float y, float w, float h, string indent)
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
        ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(F(w)).Append(" h=").Append(F(h)).Append(Radius(rs, w, h)).Append(rs.borderTopLeftRadius > 0.01f ? string.Empty : " rx=" + F(rx)).Append(" f=").Append(Hex(track)).Append('\n');
        ctx.Out.Nodes++;
        if (float.IsNaN(value) && control == "progress")
        {
            // indeterminate: a third of the bar sliding back and forth
            ctx.Body.Append(indent).Append("R x==").Append(F(x)).Append('+').Append(F(w * 0.67f)).Append("*(0.5-0.5*cos(t*3)) y=").Append(F(y)).Append(" w=").Append(F(w * 0.33f)).Append(" h=").Append(F(h)).Append(" rx=").Append(F(rx)).Append(" f=").Append(Hex(accent)).Append('\n');
            ctx.Out.Nodes++;
            return;
        }
        if (frac > 0.001f)
        {
            ctx.Body.Append(indent).Append("R x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(F(Mathf.Max(h, w * frac))).Append(" h=").Append(F(h)).Append(" rx=").Append(F(rx)).Append(" f=").Append(Hex(accent)).Append('\n');
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
        void MoveTo(float px, float py) { var p = P(px, py); path.Append('M').Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' '); curX = px; curY = py; startX = px; startY = py; hasCurrent = true; }
        void LineTo(float px, float py) { if (!hasCurrent) { MoveTo(px, py); return; } var p = P(px, py); path.Append('L').Append(F(p.x)).Append(' ').Append(F(p.y)).Append(' '); curX = px; curY = py; }
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
                path.Append('A').Append(F(prx)).Append(' ').Append(F(pry)).Append(' ').Append(F(rot * Mathf.Rad2Deg)).Append(' ').Append(large).Append(' ').Append(ccw ? 0 : 1).Append(' ').Append(F(pe.x)).Append(' ').Append(F(pe.y)).Append(' ');
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
                    stops.Append('[').Append(F(StyleApplier.Num(st.Substring(0, colon)))).Append(',').Append(Hex(sc)).Append(']');
                }
                var id = "cg" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
                float N(int i) => i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
                if (s[1] == 'L')
                {
                    var a = P(N(1), N(2)); var b = P(N(3), N(4));
                    ctx.Defs.Append("  GL id=").Append(id).Append(" x1=").Append(F(a.x)).Append(" y1=").Append(F(a.y)).Append(" x2=").Append(F(b.x)).Append(" y2=").Append(F(b.y)).Append(" stops=[").Append(stops).Append("]\n");
                }
                else if (s[1] == 'R')
                {
                    var c1 = P(N(4), N(5)); var f0 = P(N(1), N(2));
                    ctx.Defs.Append("  GR id=").Append(id).Append(" cx=").Append(F(c1.x)).Append(" cy=").Append(F(c1.y)).Append(" r=").Append(F(N(6) * Scale())).Append(" fx=").Append(F(f0.x)).Append(" fy=").Append(F(f0.y)).Append(" stops=[").Append(stops).Append("]\n");
                }
                else
                {
                    var c1 = P(N(1), N(2));
                    ctx.Defs.Append("  GC id=").Append(id).Append(" cx=").Append(F(c1.x)).Append(" cy=").Append(F(c1.y)).Append(" a=").Append(F(N(3) * Mathf.Rad2Deg)).Append(" stops=[").Append(stops).Append("]\n");
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
            for (var i = 0; i < parts.Length; i++) { if (i > 0) sb.Append(','); sb.Append(F(StyleApplier.Num(parts[i]) * k)); }
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
            sb.Append("T x=").Append(F(left)).Append(" y=").Append(F(top)).Append(" w=").Append(F(width)).Append(" h=").Append(F(ps * 1.25f)).Append(" text=\"").Append(esc).Append("\" size=").Append(F(ps));
            if (stroke.Length > 0) sb.Append(" f=").Append(stroke); else sb.Append(" f=").Append(fill);
            if (alpha < 0.999f) sb.Append(" fo=").Append(F(alpha));
            if (family.Length > 0 && !IsGeneric(family)) sb.Append(" font=\"").Append(FontLibrary.ResolveFace(family)).Append('"');
            else if (family.Length > 0 && StyleApplier.MapGeneric(family) is { } g) sb.Append(" font=\"").Append(g).Append('"');
            if (bold && !NamedWeight(family)) sb.Append(" weight=bold");
            if (align is "center") sb.Append(" align=center"); else if (align is "right" or "end") sb.Append(" align=right");
            sb.Append(" valign=top");
            if (shadow.Length > 0) sb.Append(shadow);
            // the transform's rotation turns the label about its anchor, the way a T rotates with its group (translate and scale are already in p and ps)
            var rot = Mathf.Atan2(m[1], m[0]) * Mathf.Rad2Deg;
            if (Mathf.Abs(rot) > 0.01f)
                ctx.Body.Append(Ind()).Append("G a=[").Append(F(p.x)).Append(',').Append(F(p.y)).Append("] r=").Append(F(rot)).Append(" { ").Append(sb.ToString().TrimStart()).Append(" }\n");
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
                    path.Append('Q').Append(F(c1.x)).Append(' ').Append(F(c1.y)).Append(' ').Append(F(e.x)).Append(' ').Append(F(e.y)).Append(' ');
                    curX = cmds[i + 2]; curY = cmds[i + 3]; i += 4; break;
                }
                case CanvasElement.OpCubicTo:
                {
                    if (!hasCurrent) MoveTo(cmds[i], cmds[i + 1]);
                    var c1 = P(cmds[i], cmds[i + 1]); var c2 = P(cmds[i + 2], cmds[i + 3]); var e = P(cmds[i + 4], cmds[i + 5]);
                    path.Append('C').Append(F(c1.x)).Append(' ').Append(F(c1.y)).Append(' ').Append(F(c2.x)).Append(' ').Append(F(c2.y)).Append(' ').Append(F(e.x)).Append(' ').Append(F(e.y)).Append(' ');
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
                        if (alpha < 0.999f) ctx.Body.Append(" fo=").Append(F(alpha));
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
                        ctx.Body.Append(Ind()).Append("P d=\"").Append(path.ToString().TrimEnd()).Append("\" f=none s=").Append(col).Append(" sw=").Append(F(Mathf.Max(0.5f, lw))).Append(" cap=").Append(cap).Append(" join=").Append(join);
                        if (alpha < 0.999f) ctx.Body.Append(" so=").Append(F(alpha));
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
                        ctx.Body.Append(Ind()).Append("R x=").Append(F(left)).Append(" y=").Append(F(top)).Append(" w=").Append(F(Mathf.Abs(b.x - a.x))).Append(" h=").Append(F(Mathf.Abs(b.y - a.y)));
                    }
                    else
                    {
                        var p0 = P(rx, ry); var p1 = P(rx + rw, ry); var p2 = P(rx + rw, ry + rh); var p3 = P(rx, ry + rh);
                        ctx.Body.Append(Ind()).Append("Y p=[").Append(F(p0.x)).Append(',').Append(F(p0.y)).Append(',').Append(F(p1.x)).Append(',').Append(F(p1.y)).Append(',').Append(F(p2.x)).Append(',').Append(F(p2.y)).Append(',').Append(F(p3.x)).Append(',').Append(F(p3.y)).Append(']');
                    }
                    if (isStroke) ctx.Body.Append(" f=none s=").Append(col).Append(" sw=").Append(F(Mathf.Max(0.5f, lw))).Append(" cap=").Append(cap).Append(" join=").Append(join).Append(DashAttr(lw));
                    else ctx.Body.Append(" f=").Append(col);
                    if (alpha < 0.999f) ctx.Body.Append(isStroke ? " so=" : " fo=").Append(F(alpha));
                    ctx.Body.Append(shadow).Append('\n');
                    ctx.Out.Nodes++;
                    break;
                }
                case CanvasElement.OpArcTo:
                {
                    // ponytail: the tangent arc as a quadratic through the corner; right for the rounded corners it is used for
                    if (!hasCurrent) MoveTo(cmds[i], cmds[i + 1]);
                    var c1 = P(cmds[i], cmds[i + 1]); var e = P(cmds[i + 2], cmds[i + 3]);
                    path.Append('Q').Append(F(c1.x)).Append(' ').Append(F(c1.y)).Append(' ').Append(F(e.x)).Append(' ').Append(F(e.y)).Append(' ');
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
                    var id = "cclip" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
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
                    ctx.Body.Append(Ind()).Append("IMG x=").Append(F(Mathf.Min(a.x, b.x))).Append(" y=").Append(F(Mathf.Min(a.y, b.y))).Append(" w=").Append(F(Mathf.Abs(b.x - a.x))).Append(" h=").Append(F(Mathf.Abs(b.y - a.y))).Append(" src=\"").Append(src.Replace("\"", string.Empty)).Append("\" fit=fill");
                    if (alpha < 0.999f) ctx.Body.Append(" o=").Append(F(alpha));
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
                    ctx.Body.Append(Ind()).Append("R x=").Append(F(Mathf.Min(a.x, b.x))).Append(" y=").Append(F(Mathf.Min(a.y, b.y))).Append(" w=").Append(F(Mathf.Abs(b.x - a.x))).Append(" h=").Append(F(Mathf.Abs(b.y - a.y))).Append(" f=").Append(Hex(under)).Append('\n');
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
    private static void EmitImage(Ctx ctx, string src, string fit, Dictionary<string, string> css, IResolvedStyle rs, float x, float y, float w, float h, string indent, string nodeId)
    {
        var f = fit switch { "cover" => "cover", "contain" or "scale-down" => "contain", _ => "fill" };
        ctx.Body.Append(indent).Append("IMG x=").Append(F(x)).Append(" y=").Append(F(y)).Append(" w=").Append(F(w)).Append(" h=").Append(F(h))
            .Append(" src=\"").Append(src.Replace("\"", string.Empty)).Append("\" fit=").Append(f).Append(Radius(rs, w, h));
        if (rs.opacity < 0.999f) ctx.Body.Append(" o=").Append(F(rs.opacity));
        ctx.Body.Append(nodeId).Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>url(...) inside a background value, or null.</summary>
    private static string? UrlOf(string css)
    {
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
            stops.Append('[').Append(F(at)).Append(',').Append(Hex(c)).Append(']');
            lastAt = at; n++;
        }
        if (n < 2) return null;
        var id = "conic" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
        ctx.Defs.Append("  GC id=").Append(id).Append(" units=bbox cx=").Append(F(cx)).Append(" cy=").Append(F(cy)).Append(" a=").Append(F(from)).Append(" stops=[").Append(stops).Append("]\n");
        return id;
    }

    private static float Position(string v)
    {
        var t = v.Trim().ToLowerInvariant();
        return t switch { "left" or "top" => 0f, "center" => 0.5f, "right" or "bottom" => 1f, _ => t.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(t) / 100f : 0.5f };
    }

    /// <summary>repeating-linear/radial-gradient(...) rewritten as the plain gradient with its stop list repeated to 100%. Stop positions must be percentages.</summary>
    private static string ExpandRepeating(string css)
    {
        var open = css.IndexOf('(');
        var close = css.LastIndexOf(')');
        if (open < 0 || close < open) return css;
        var name = css.Substring(0, open).Trim().Substring("repeating-".Length);
        var args = SplitTopLevelCommas(css.Substring(open + 1, close - open - 1));
        var head = new List<string>();
        var stops = new List<(float at, string colour)>();
        foreach (var raw in args)
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && StyleApplier.TryColor(parts[0], out _))
                stops.Add((parts.Length > 1 && parts[1].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(parts[1]) / 100f : float.NaN, parts[0]));
            else head.Add(raw.Trim());
        }
        if (stops.Count < 2 || stops.Exists(s => float.IsNaN(s.at))) return name + css.Substring(open); // px stops: not expanded
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
    private static string? CornerPath(Dictionary<string, string> css, IResolvedStyle rs, float x, float y, float w, float h)
    {
        if (!css.TryGetValue("corner-shape", out var cs)) return null;
        var shape = cs.Trim().ToLowerInvariant();
        if (shape is not ("bevel" or "scoop" or "notch" or "squircle")) return null;
        if (shape == "squircle") return null; // close enough to round; the R keeps its rx
        var tl = Mathf.Min(rs.borderTopLeftRadius, Mathf.Min(w, h) * 0.5f);
        var tr = Mathf.Min(rs.borderTopRightRadius, Mathf.Min(w, h) * 0.5f);
        var br = Mathf.Min(rs.borderBottomRightRadius, Mathf.Min(w, h) * 0.5f);
        var bl = Mathf.Min(rs.borderBottomLeftRadius, Mathf.Min(w, h) * 0.5f);
        if (tl + tr + br + bl < 0.01f) return null;
        var sb = new StringBuilder("P d=\"");
        void Corner(float cx, float cy, float r, float dx, float dy, bool first, bool alongTop)
        {
            // corner at (cx,cy); dx,dy point from the corner into the box. Clockwise, the
            // outline reaches a left-hand corner along the vertical side and a right-hand
            // corner along the horizontal one: (ax,ay) is where it arrives, (bx,by) where it leaves.
            var ax = alongTop ? cx + dx * r : cx; var ay = alongTop ? cy : cy + dy * r;
            var bx = alongTop ? cx : cx + dx * r; var by = alongTop ? cy + dy * r : cy;
            if (r <= 0.01f) { sb.Append(first ? "M" : " L").Append(F(cx)).Append(' ').Append(F(cy)); return; }
            switch (shape)
            {
                case "bevel": sb.Append(first ? "M" : " L").Append(F(ax)).Append(' ').Append(F(ay)).Append(" L").Append(F(bx)).Append(' ').Append(F(by)); break;
                case "notch": sb.Append(first ? "M" : " L").Append(F(ax)).Append(' ').Append(F(ay)).Append(" L").Append(F(cx + dx * r)).Append(' ').Append(F(cy + dy * r)).Append(" L").Append(F(bx)).Append(' ').Append(F(by)); break;
                default: sb.Append(first ? "M" : " L").Append(F(ax)).Append(' ').Append(F(ay)).Append(" A").Append(F(r)).Append(' ').Append(F(r)).Append(" 0 0 0 ").Append(F(bx)).Append(' ').Append(F(by)); break; // scoop: concave arc
            }
        }
        // clockwise from the top-left corner: TL arrives from the left side going up, leaves along the top
        Corner(x, y, tl, 1f, 1f, true, false);
        Corner(x + w, y, tr, -1f, 1f, false, true);
        Corner(x + w, y + h, br, -1f, -1f, false, false);
        Corner(x, y + h, bl, 1f, -1f, false, true);
        sb.Append(" Z\"");
        return sb.ToString();
    }

    /// <summary>
    /// border-image: a gradient source becomes a gradient stroke over the border box; an
    /// image source becomes nine IMG slices with source crops (vector requirement 16).
    /// Slices in percent are exact; a number/px slice needs the image size, which is not
    /// known here, so it is read as thirds (the common nine-slice layout) and reported.
    /// </summary>
    private static bool BorderImage(Ctx ctx, Dictionary<string, string> css, IResolvedStyle rs, float x, float y, float w, float h, string indent)
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
            var gid = "bimg" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
            var cx = x + w * 0.5f; var cy = y + h * 0.5f;
            var hx = Mathf.Sin(rad) * len * 0.5f; var hy = -Mathf.Cos(rad) * len * 0.5f;
            ctx.Defs.Append("  GL id=").Append(gid).Append(" x1=").Append(F(cx - hx)).Append(" y1=").Append(F(cy - hy)).Append(" x2=").Append(F(cx + hx)).Append(" y2=").Append(F(cy + hy)).Append(" stops=[");
            for (var i = 0; i < stops.Count; i++) { if (i > 0) ctx.Defs.Append(','); ctx.Defs.Append('[').Append(F(stops[i].at)).Append(',').Append(Hex(stops[i].c)).Append(']'); }
            ctx.Defs.Append("]\n");
            // one stroke when the widths agree, four gradient-filled sides otherwise
            if (Mathf.Approximately(bw[0], bw[1]) && Mathf.Approximately(bw[0], bw[2]) && Mathf.Approximately(bw[0], bw[3]))
            {
                var half = bw[0] * 0.5f;
                ctx.Body.Append(indent).Append("R x=").Append(F(x + half)).Append(" y=").Append(F(y + half)).Append(" w=").Append(F(w - bw[0])).Append(" h=").Append(F(h - bw[0]))
                    .Append(" f=none s=@").Append(gid).Append(" sw=").Append(F(bw[0])).Append('\n');
                ctx.Out.Nodes++;
            }
            else
            {
                var sides = new[] { (x, y, w, bw[0]), (x + w - bw[1], y, bw[1], h), (x, y + h - bw[2], w, bw[2]), (x, y, bw[3], h) };
                foreach (var (sx, sy, sw, sh) in sides)
                {
                    if (sw <= 0.01f || sh <= 0.01f) continue;
                    ctx.Body.Append(indent).Append("R x=").Append(F(sx)).Append(" y=").Append(F(sy)).Append(" w=").Append(F(sw)).Append(" h=").Append(F(sh)).Append(" f=@").Append(gid).Append('\n');
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
            ctx.Body.Append(indent).Append("IMG x=").Append(F(ix)).Append(" y=").Append(F(iy)).Append(" w=").Append(F(iw)).Append(" h=").Append(F(ih))
                .Append(" src=\"").Append(url.Replace("\"", string.Empty)).Append("\" fit=fill uv=[").Append(F(ua)).Append(',').Append(F(va)).Append(',').Append(F(ub)).Append(',').Append(F(vb)).Append("]\n");
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
    private static Dictionary<string, string> PseudoCss(Ctx ctx, VisualElement ve, string pseudo)
    {
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
                css[d.Name] = d.Value.IndexOf("var(", StringComparison.Ordinal) >= 0 ? HtmlRenderer.ResolveVars(d.Value, node) : d.Value.Trim();
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
            ctx.Body.Append(indent).Append("R x=").Append(F(x + w - width)).Append(" y==").Append(F(y)).Append("+sy w=").Append(F(width)).Append(" h=").Append(F(h)).Append(" f=").Append(Hex(track)).Append('\n');
            ctx.Out.Nodes++;
        }
        ctx.Body.Append(indent).Append("R x=").Append(F(x + w - width + 1f)).Append(" y==").Append(F(y)).Append("+sy*").Append(F(k)).Append(" w=").Append(F(width - 2f)).Append(" h=").Append(F(thumbH))
            .Append(" rx=").Append(F(Mathf.Min(radius, (width - 2f) * 0.5f))).Append(" f=").Append(Hex(thumb)).Append('\n');
        ctx.Out.Nodes++;
    }

    /// <summary>
    /// animation-timeline: scroll() / view(): the element's @keyframes written as one G whose
    /// opacity and transform are piecewise expressions over the scroll progress p (0..1),
    /// eased per segment; scroll() runs over the box's whole range, view() while the
    /// element crosses the viewport. No clock, no rebuilds: the vector mod evaluates sy.
    /// </summary>
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
        var sb = new StringBuilder("G a=[").Append(F(x + w * 0.5f)).Append(',').Append(F(y + h * 0.5f)).Append(']');
        var op = Piece(k => k.o);
        if (op != "1") sb.Append(" o=").Append(Quote(op));
        var txe = Piece(k => k.tx); var tye = Piece(k => k.ty);
        if (txe != "0" || tye != "0") sb.Append(" t=[").Append(Quote(txe)).Append(',').Append(Quote(tye)).Append(']');
        var re = Piece(k => k.r);
        if (re != "0") sb.Append(" r=").Append(Quote(re));
        var sxe = Piece(k => k.sx); var sye = Piece(k => k.sy);
        if (sxe != "1" || sye != "1") sb.Append(" s=[").Append(Quote(sxe)).Append(',').Append(Quote(sye)).Append(']');
        return sb.ToString();
    }

    private static string Quote(string v) => v.StartsWith("=", StringComparison.Ordinal) ? "\"" + v + "\"" : v;

    private static string BorderStyle(Dictionary<string, string> css) => SideStyle(css, "top", 0);

    private static bool IsBorderStyle(string p) => p is "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge" or "inset" or "outset" or "none" or "hidden";

    /// <summary>One side's border style: border-&lt;side&gt;-style, then the border-&lt;side&gt; shorthand, then border-style (1-4 values), then the border shorthand, else solid.</summary>
    private static string SideStyle(Dictionary<string, string> css, string side, int index)
    {
        if (css.TryGetValue("border-" + side + "-style", out var own)) return own.Trim().ToLowerInvariant();
        if (css.TryGetValue("border-" + side, out var sh))
            foreach (var p in sh.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (IsBorderStyle(p)) return p;
        if (css.TryGetValue("border-style", out var bs))
        {
            var parts = bs.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) return StyleApplier.SideOf(parts, index);
        }
        if (css.TryGetValue("border", out var b))
            foreach (var p in b.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (IsBorderStyle(p)) return p;
        return "solid";
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
        ctx.Body.Append(indent).Append("L p=[").Append(F(x1)).Append(',').Append(F(y1)).Append(',').Append(F(x2)).Append(',').Append(F(y2)).Append("] s=").Append(Hex(c)).Append(" sw=").Append(F(width)).Append(DashFor(style, width)).Append('\n');
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
                case "brightness": sb.Append(" bri=").Append(F(Amount())); break;
                case "contrast": sb.Append(" con=").Append(F(Amount())); break;
                case "saturate": sb.Append(" sat=").Append(F(Amount())); break;
                case "grayscale": sb.Append(" gray=").Append(F(Amount())); break;
                case "sepia": sb.Append(" sep=").Append(F(Amount())); break;
                case "invert": sb.Append(" inv=").Append(F(Amount())); break;
                case "hue-rotate": sb.Append(" hue=").Append(F(StyleApplier.Num(a))); break;
                case "opacity": sb.Append(" o=").Append(F(Amount())); break;
                case "drop-shadow":
                {
                    var parts = string.Join(" ", args).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var nums = new List<float>(); var colour = new Color(0, 0, 0, 1);
                    foreach (var p in parts) { if (StyleApplier.TryColor(p, out var c)) colour = c; else nums.Add(StyleApplier.Num(p)); }
                    while (nums.Count < 3) nums.Add(0f);
                    sh.Append('[').Append(F(nums[0])).Append(',').Append(F(nums[1])).Append(',').Append(F(nums[2])).Append(",0,").Append(Hex(colour)).Append(']');
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
    private static string? ClipPath(Ctx ctx, string css, float x, float y, float w, float h, Xform? xform = null)
    {
        var v = css.Trim();
        var open = v.IndexOf('(');
        if (open < 0 || !v.EndsWith(")", StringComparison.Ordinal)) return null;
        var name = v.Substring(0, open).Trim().ToLowerInvariant();
        var inner = v.Substring(open + 1, v.Length - open - 2).Trim();
        var id = "cpath" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
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
                sb.Append(F(q.x)).Append(',').Append(F(q.y));
            }
            ctx.Defs.Append("  CP id=").Append(id).Append(" { Y p=[").Append(sb).Append("] }\n");
            return id;
        }
        switch (name)
        {
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
                ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").Append(F(x + l)).Append(" y=").Append(F(y + t)).Append(" w=").Append(F(Mathf.Max(0f, w - l - r))).Append(" h=").Append(F(Mathf.Max(0f, h - t - b)));
                if (radius > 0f) ctx.Defs.Append(" rx=").Append(F(radius));
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
                ctx.Defs.Append("  CP id=").Append(id).Append(" { C cx=").Append(F(cx)).Append(" cy=").Append(F(cy)).Append(" rx=").Append(F(rx)).Append(" ry=").Append(F(ry)).Append(" }\n");
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
                    pts.Append(F(x + Along(xy[0], w))).Append(',').Append(F(y + Along(xy[1], h)));
                }
                if (pts.Length == 0) return null;
                if (xform != null) return Through(poly);
                ctx.Defs.Append("  CP id=").Append(id).Append(" { Y p=[").Append(pts).Append("] }\n");
                return id;
            }
        }
        return null;
    }

    /// <summary>mask-image: a linear or radial gradient as a GL/GR def whose alpha masks the subtree (vector requirement 10).</summary>
    private static string? MaskDef(Ctx ctx, string css, float x, float y, float w, float h)
    {
        var v = css.Trim();
        if (v.StartsWith("radial-gradient", StringComparison.OrdinalIgnoreCase)) return RadialDef(ctx, v);
        if (!v.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase)) return null;
        if (ParseGradient(v) is not { } g) return null;
        var rad = g.angle * Mathf.Deg2Rad;
        var len = w * Mathf.Abs(Mathf.Sin(rad)) + h * Mathf.Abs(Mathf.Cos(rad));
        var dx = Mathf.Sin(rad) * len * 0.5f / w;
        var dy = -Mathf.Cos(rad) * len * 0.5f / h;
        var id = "mask" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
        ctx.Defs.Append("  GL id=").Append(id).Append(" units=bbox x1=").Append(F(0.5f - dx)).Append(" y1=").Append(F(0.5f - dy)).Append(" x2=").Append(F(0.5f + dx)).Append(" y2=").Append(F(0.5f + dy)).Append(" stops=[");
        for (var i = 0; i < g.stops.Count; i++)
        {
            if (i > 0) ctx.Defs.Append(',');
            ctx.Defs.Append('[').Append(F(g.stops[i].at)).Append(',').Append(Hex(g.stops[i].c)).Append(']');
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
                ctx.Body.Append(indent).Append("P d=\"M").Append(F(cx - r * 0.6f)).Append(' ').Append(F(cy - r)).Append(" L").Append(F(cx + r * 0.8f)).Append(' ').Append(F(cy)).Append(" L").Append(F(cx - r * 0.6f)).Append(' ').Append(F(cy + r)).Append(" Z\" f=").Append(Hex(colour)).Append('\n');
                break;
            case "tri-down":
                ctx.Body.Append(indent).Append("P d=\"M").Append(F(cx - r)).Append(' ').Append(F(cy - r * 0.6f)).Append(" L").Append(F(cx + r)).Append(' ').Append(F(cy - r * 0.6f)).Append(" L").Append(F(cx)).Append(' ').Append(F(cy + r * 0.8f)).Append(" Z\" f=").Append(Hex(colour)).Append('\n');
                break;
            case "square":
                ctx.Body.Append(indent).Append("R x=").Append(F(cx - r)).Append(" y=").Append(F(cy - r)).Append(" w=").Append(F(2f * r)).Append(" h=").Append(F(2f * r)).Append(" f=").Append(Hex(colour)).Append('\n');
                break;
            case "circle":
                ctx.Body.Append(indent).Append("C cx=").Append(F(cx)).Append(" cy=").Append(F(cy)).Append(" rx=").Append(F(r - 0.5f)).Append(" ry=").Append(F(r - 0.5f)).Append(" f=none s=").Append(Hex(colour)).Append(" sw=1\n");
                break;
            default:
                ctx.Body.Append(indent).Append("C cx=").Append(F(cx)).Append(" cy=").Append(F(cy)).Append(" rx=").Append(F(r)).Append(" ry=").Append(F(r)).Append(" f=").Append(Hex(colour)).Append('\n');
                break;
        }
        ctx.Out.Nodes++;
    }

    /// <summary>
    /// The node id, plus `click=1` on a button: the vector mod makes such a node a hit region
    /// and the click arrives at the page element's own on_click with the node id as value.
    /// A button without an id gets its synthetic one, so it can still be clicked.
    /// </summary>
    private static string NodeId(Ctx ctx, VisualElement ve)
    {
        var button = IsButton(ctx, ve);
        if (string.IsNullOrEmpty(ve.name) || (ve.name.StartsWith("__", StringComparison.Ordinal) && !button))
            return string.Empty;
        return " id=" + ve.name + (button ? " click=1" : string.Empty);
    }

    private static bool IsButton(Ctx ctx, VisualElement ve)
    {
        return ctx.Built.NodeOf.TryGetValue(ve, out var node)
               && (node.Tag == "button" || node.Attr("onclick") != null || node.Attr("data-click") != null || node.Attr("data-control") != null)
               && !(ctx.Built.CssOf(ve).TryGetValue("pointer-events", out var pe) && pe.Trim() == "none");
    }

    /// <summary>outline / outline-width / outline-color / outline-offset; false for none.</summary>
    private static bool Outline(Dictionary<string, string> css, out float width, out Color colour, out float offset)
    {
        width = 0f; colour = Color.white; offset = 0f;
        var any = false;
        if (css.TryGetValue("outline", out var shorthand))
        {
            var v = shorthand.Trim();
            if (v == "none" || v == "0") return false;
            foreach (var part in SplitParts(v))
            {
                if (part is "solid" or "dashed" or "dotted" or "double" or "auto") { any = true; continue; }
                if (StyleApplier.IsNumber(part) || part.EndsWith("px", StringComparison.OrdinalIgnoreCase)) { width = StyleApplier.Num(part); any = true; }
                else if (StyleApplier.TryColor(part, out var c)) { colour = c; any = true; }
            }
            if (any && width <= 0f) width = 3f; // medium
        }
        if (css.TryGetValue("outline-width", out var wv)) { width = StyleApplier.Num(wv); any = true; }
        if (css.TryGetValue("outline-color", out var cv) && StyleApplier.TryColor(cv, out var cc)) { colour = cc; any = true; }
        if (css.TryGetValue("outline-style", out var sv) && sv.Trim() == "none") return false;
        if (css.TryGetValue("outline-offset", out var ov)) offset = StyleApplier.Num(ov);
        return any;
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

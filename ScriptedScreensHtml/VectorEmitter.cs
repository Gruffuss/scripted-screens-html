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
    }

    public static Output Emit(HtmlRenderer.Result built, VisualElement root, float designW, float designH, Tweens? tweens = null, float now = 0f)
    {
        var ctx = new Ctx { Built = built, RootOrigin = root.worldBound.position, Tw = tweens, Now = now };
        var inv = CultureInfo.InvariantCulture;
        EmitElement(ctx, root, Vector2.zero, 0);
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

        if (ctx.Built.Externals.TryGetValue(ve, out var external))
        {
            ctx.Out.Externals.Add(new External { Key = ve.name, Node = external, X = x, Y = y, W = w, H = h });
            return;
        }

        // A transition in flight: numbers below become expressions over t (Tweens.cs).
        var tw = ctx.Tw?.Of(ve, ctx.Now);
        var ws = tw != null ? tw.Lerp(tw.From.Rect.width, w) : F(w);
        var hs = tw != null ? tw.Lerp(tw.From.Rect.height, h) : F(h);

        // Wrappers: position, transform, opacity, clip. Each opens a G that is closed after children.
        var groups = 0;
        if (tw != null && (Mathf.Abs(tw.From.Rect.x - tw.To.Rect.x) > 0.01f || Mathf.Abs(tw.From.Rect.y - tw.To.Rect.y) > 0.01f))
        {
            // The subtree is emitted at its final place; this group carries it there from
            // where it was, so children move with it without expressions of their own.
            ctx.Body.Append(indent).Append("G t=[\"").Append(tw.Remaining(tw.From.Rect.x, tw.To.Rect.x)).Append("\",\"")
                .Append(tw.Remaining(tw.From.Rect.y, tw.To.Rect.y)).Append("\"] {\n");
            groups++;
        }
        var xform = Xform.From(ve, tw, x, y, w, h);
        if (xform != null) { ctx.Body.Append(indent).Append(xform.Group()).Append(" {\n"); groups++; }
        if (rs.opacity < 0.999f || (tw != null && tw.From.Opacity < 0.999f))
        {
            ctx.Body.Append(indent).Append("G o=").Append(tw != null ? tw.Lerp(tw.From.Opacity, rs.opacity) : F(rs.opacity)).Append(" {\n");
            groups++;
        }
        if (ve.style.overflow.value == Overflow.Hidden && w > 0f && h > 0f)
        {
            var id = "clip" + (++ctx.Ids).ToString(CultureInfo.InvariantCulture);
            ctx.Defs.Append("  CP id=").Append(id).Append(" { R x=").Append(F(x)).Append(" y=").Append(F(y))
                .Append(" w=").Append(F(w)).Append(" h=").Append(F(h)).Append(Radius(rs, w, h)).Append(" }\n");
            ctx.Body.Append(indent).Append("G clip=").Append(id).Append(" {\n");
            groups++;
        }

        // Background and border of the box itself.
        if (w > 0f && h > 0f)
        {
            var bg = rs.backgroundColor;
            css.TryGetValue("background", out var bgCss);
            if (bgCss == null) css.TryGetValue("background-image", out bgCss);
            var shadow = css.TryGetValue("box-shadow", out var shCss) ? Shadows(shCss) : string.Empty;
            if (bgCss != null && bgCss.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase))
            {
                GradientBox(ctx, bgCss, x, y, w, h, ws, hs, rs, indent, ve, xform, shadow);
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
            if (bw > 0.01f && sameWidth && !sameColour && rounded)
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
            case Label when ctx.Built.NodeOf.TryGetValue(ve, out var mnode) && mnode.Attr("data-marker") is { } markerShape:
                EmitMarker(ctx, markerShape, rs.color, x, y, w, h, indent);
                break;
            case Label label:
                EmitText(ctx, label, css, x, y, w, h, indent);
                break;
            case SvgElement svg:
                EmitSvg(ctx, svg, x, y, w, h, indent);
                break;
            case CanvasElement:
                Warn(ctx, "html: <canvas> is not drawn in vector mode; use <svg> with expressions");
                break;
            default:
                foreach (var child in ByZIndex(ctx, ve))
                    EmitElement(ctx, child, new Vector2(x, y), depth + groups);
                break;
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

    /// <summary>A CSS transform as the vector G attributes, kept numeric so clip
    /// polygons declared in scene space can be put through the same transform.</summary>
    private sealed class Xform
    {
        public float Ax, Ay, Tx, Ty, R, Sx = 1f, Sy = 1f;
        private Tweens.Tween? _tw;

        /// <summary>The element's resolved transform (UI Toolkit has already applied the CSS), tweened if one is running.</summary>
        public static Xform? From(VisualElement ve, Tweens.Tween? tw, float x, float y, float w, float h)
        {
            var rs = ve.resolvedStyle;
            var xf = new Xform
            {
                Ax = x + w * 0.5f, Ay = y + h * 0.5f,
                Tx = rs.translate.x, Ty = rs.translate.y,
                R = rs.rotate.angle.ToDegrees(),
                Sx = rs.scale.value.x, Sy = rs.scale.value.y,
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
                sb.Append(" t=[\"").Append(_tw.Lerp(f.Translate.x, Tx)).Append("\",\"").Append(_tw.Lerp(f.Translate.y, Ty)).Append("\"]");
                sb.Append(" r=").Append(_tw.Lerp(f.Rotate, R));
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
            if (inset || nums.Count < 2) continue;
            if (!hasColour) colour.a = 1f;
            while (nums.Count < 4) nums.Add(0f);
            if (sb.Length > 0) sb.Append(',');
            sb.Append('[').Append(F(nums[0])).Append(',').Append(F(nums[1])).Append(',').Append(F(nums[2])).Append(',').Append(F(nums[3])).Append(',').Append(Hex(colour)).Append(']');
            count++;
        }
        return sb.Length > 0 ? " sh=[" + sb + "]" : string.Empty;
    }

    /// <summary>border-style dashed/dotted (from the shorthand or the property) as a dash pattern in border widths.</summary>
    private static string Dash(Dictionary<string, string> css, float bw)
    {
        string? style = null;
        if (css.TryGetValue("border-style", out var bs)) style = bs;
        else if (css.TryGetValue("border", out var b))
        {
            foreach (var p in b.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (p == "dashed" || p == "dotted" || p == "solid") style = p;
        }
        return style switch
        {
            "dashed" => " dash=[" + F(bw * 3f) + "," + F(bw * 2f) + "]",
            "dotted" => " dash=[" + F(bw) + "," + F(bw) + "] cap=round",
            _ => string.Empty,
        };
    }

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
        if (css.TryGetValue("text-decoration", out var td))
        {
            if (td.Contains("underline")) text = "<u>" + text + "</u>";
            if (td.Contains("line-through")) text = "<s>" + text + "</s>";
        }
        // Scene text escapes (vector mod 0.10.1.0): backslash first, then the quote; a line
        // break in the text becomes the two characters backslash-n.
        text = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        // The scene reader types a clean number as a number even when quoted, and a number
        // has no text: "3" vanished from the footer. TextMeshPro's <noparse> keeps it a string.
        if (float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            text = "<noparse>" + text + "</noparse>";
        var align = rs.unityTextAlign;
        var centre = align == TextAnchor.MiddleCenter || align == TextAnchor.UpperCenter || align == TextAnchor.LowerCenter;
        var right = align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight;
        var clipped = label.style.overflow.value == Overflow.Hidden || (label.parent != null && label.parent.style.overflow.value == Overflow.Hidden);
        var wraps = rs.whiteSpace == WhiteSpace.Normal && rs.fontSize > 0f && h > rs.fontSize * 1.6f && text.IndexOf(' ') >= 0;
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
        sb.Append(" f=").Append(Hex(rs.color));
        var first = string.Empty;
        if (css.TryGetValue("font-family", out var family))
        {
            first = family.Split(',')[0].Trim().Trim('"', '\'');
            if (first.Length > 0 && !IsGeneric(first))
                sb.Append(" font=\"").Append(first).Append('"');
        }
        var fs = rs.unityFontStyleAndWeight;
        // A face that is already a named weight ("Barlow SemiBold") must not be bolded again:
        // TextMeshPro's synthetic bold widens every glyph on top of it.
        if ((fs == FontStyle.Bold || fs == FontStyle.BoldAndItalic) && !NamedWeight(first))
            sb.Append(" weight=bold");
        if (css.TryGetValue("letter-spacing", out var ls) && rs.fontSize > 0f)
        {
            // TextMeshPro's characterSpacing is hundredths of an em.
            var px = ls.EndsWith("em", StringComparison.OrdinalIgnoreCase) ? StyleApplier.Num(ls) * rs.fontSize : StyleApplier.Num(ls);
            if (px != 0f) sb.Append(" cspace=").Append(F(px / rs.fontSize * 100f));
        }
        if (centre) sb.Append(" align=center");
        else if (right) sb.Append(" align=right");
        sb.Append(" valign=middle");
        if (clipped)
            sb.Append(" fit=ellipsis");
        if (wraps)
            sb.Append(" wrap=1");
        if (css.TryGetValue("text-shadow", out var tsh))
            sb.Append(Shadows(tsh, 1)); // one shadow per label: the underlay is a single layer
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
        foreach (var shape in svg.Shapes)
            EmitShape(ctx, shape, inner, id + "_", fit);
        ctx.Body.Append(indent).Append("}\n");
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

        var sb = new StringBuilder(indent);
        var opacity = shape.Attr("opacity");

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
        var sid = shape.Attr("id");
        if (sid != null) sb.Append(" id=").Append(sid);
        if (pathGroup) sb.Append(" }");
        if (repeat != null) sb.Append(" }");
        ctx.Body.Append(sb).Append('\n');
        ctx.Out.Nodes++;
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

    /// <summary>A list marker in the text colour: a filled disc, a hollow circle or a filled square.</summary>
    private static void EmitMarker(Ctx ctx, string shape, Color colour, float x, float y, float w, float h, string indent)
    {
        var r = Mathf.Max(1f, Mathf.Min(w, h) * 0.5f);
        var cx = x + w * 0.5f;
        var cy = y + h * 0.5f;
        switch (shape)
        {
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
               && (node.Tag == "button" || node.Attr("onclick") != null || node.Attr("data-click") != null)
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

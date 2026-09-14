using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// Maps CSS declarations onto a VisualElement's inline style. Covers what UI Toolkit can
/// do: box model, flexbox, colours, borders, radii, opacity, text basics, transforms and
/// transitions. Unknown properties are reported once per name and otherwise ignored.
/// </summary>
internal static class StyleApplier
{
    /// <summary>The vector back-end animates; UI Toolkit lays out only.</summary>
    internal static bool VectorMode = true;

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public static void Apply(VisualElement ve, CssDeclaration d, Action<string>? warn)
    {
        var v = d.Value.Trim();
        var s = ve.style;
        switch (d.Name)
        {
            // Box
            case "width": s.width = Len(v); break;
            case "height": s.height = Len(v); break;
            case "min-width": s.minWidth = Len(v); break;
            case "min-height": s.minHeight = Len(v); break;
            case "max-width": s.maxWidth = Len(v); break;
            case "max-height": s.maxHeight = Len(v); break;
            case "margin": Sides(v, out var mt, out var mr, out var mb, out var ml); s.marginTop = mt; s.marginRight = mr; s.marginBottom = mb; s.marginLeft = ml; break;
            case "margin-top": s.marginTop = Len(v); break;
            case "margin-right": s.marginRight = Len(v); break;
            case "margin-bottom": s.marginBottom = Len(v); break;
            case "margin-left": s.marginLeft = Len(v); break;
            case "padding": Sides(v, out var pt, out var pr, out var pb, out var pl); s.paddingTop = pt; s.paddingRight = pr; s.paddingBottom = pb; s.paddingLeft = pl; break;
            case "padding-top": s.paddingTop = Len(v); break;
            case "padding-right": s.paddingRight = Len(v); break;
            case "padding-bottom": s.paddingBottom = Len(v); break;
            case "padding-left": s.paddingLeft = Len(v); break;

            // Position
            case "position": s.position = v == "absolute" ? Position.Absolute : Position.Relative; break;
            case "left": s.left = Len(v); break;
            case "top": s.top = Len(v); break;
            case "right": s.right = Len(v); break;
            case "bottom": s.bottom = Len(v); break;
            case "inset": Sides(v, out var it, out var ir, out var ib, out var il); s.top = it; s.right = ir; s.bottom = ib; s.left = il; break;

            // Flex
            case "display":
                s.display = v == "none" ? DisplayStyle.None : DisplayStyle.Flex;
                // CSS: a flex container lays out in a row unless told otherwise; a block (or
                // grid, whose children are placed absolutely) stacks. Later declarations win.
                if (v == "flex" || v == "inline-flex") s.flexDirection = FlexDirection.Row;
                break;
            case "flex-direction":
                s.flexDirection = v switch { "row" => FlexDirection.Row, "row-reverse" => FlexDirection.RowReverse, "column-reverse" => FlexDirection.ColumnReverse, _ => FlexDirection.Column };
                break;
            case "flex-wrap": s.flexWrap = v == "wrap" ? Wrap.Wrap : v == "wrap-reverse" ? Wrap.WrapReverse : Wrap.NoWrap; break;
            case "flex-grow": s.flexGrow = Num(v); break;
            case "flex-shrink": s.flexShrink = Num(v); break;
            case "flex-basis": s.flexBasis = Len(v); break;
            case "flex":
            {
                // flex: <grow> [<shrink>] [<basis>] | none | auto | <basis>
                if (v == "none") { s.flexGrow = 0; s.flexShrink = 0; s.flexBasis = StyleKeyword.Auto; break; }
                if (v == "auto") { s.flexGrow = 1; s.flexShrink = 1; s.flexBasis = StyleKeyword.Auto; break; }
                var parts = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && IsNumber(parts[0]))
                {
                    s.flexGrow = Num(parts[0]);
                    s.flexShrink = parts.Length >= 2 && IsNumber(parts[1]) ? Num(parts[1]) : 1;
                    s.flexBasis = parts.Length >= 3 ? Len(parts[2]) : (parts.Length == 2 && !IsNumber(parts[1]) ? Len(parts[1]) : new StyleLength(new Length(0)));
                }
                else
                {
                    s.flexBasis = Len(parts[0]);
                }
                break;
            }
            case "justify-content":
                s.justifyContent = v switch { "center" => Justify.Center, "flex-end" or "end" => Justify.FlexEnd, "space-between" => Justify.SpaceBetween, "space-around" or "space-evenly" => Justify.SpaceAround, _ => Justify.FlexStart };
                break;
            case "align-items": s.alignItems = AlignOf(v); break;
            case "align-self": s.alignSelf = AlignOf(v); break;
            case "align-content": s.alignContent = AlignOf(v); break;
            case "overflow": s.overflow = v == "hidden" || v == "clip" || v == "scroll" || v == "auto" ? Overflow.Hidden : Overflow.Visible; break;
            case "visibility": s.visibility = v == "hidden" ? UnityEngine.UIElements.Visibility.Hidden : UnityEngine.UIElements.Visibility.Visible; break;
            case "opacity": s.opacity = Num(v); break;

            // Colour and background
            case "color": if (TryColor(v, out var fg)) s.color = fg; else Unknown(d, warn); break;
            case "background":
            case "background-color":
            case "background-image":
                if (v == "none" || v == "transparent") { s.backgroundColor = Color.clear; s.backgroundImage = StyleKeyword.None; }
                else if (v.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase)) Gradient(s, v, warn);
                else if (TryColor(v, out var bg)) s.backgroundColor = bg;
                else Unknown(d, warn);
                break;

            // Border
            case "border": Border(s, v, warn, true, true, true, true); break;
            case "border-top": Border(s, v, warn, true, false, false, false); break;
            case "border-right": Border(s, v, warn, false, true, false, false); break;
            case "border-bottom": Border(s, v, warn, false, false, true, false); break;
            case "border-left": Border(s, v, warn, false, false, false, true); break;
            case "border-width": Sides(v, out var bt, out var br, out var bb, out var bl); s.borderTopWidth = bt.value.value; s.borderRightWidth = br.value.value; s.borderBottomWidth = bb.value.value; s.borderLeftWidth = bl.value.value; break;
            case "border-top-width": s.borderTopWidth = Num(v); break;
            case "border-right-width": s.borderRightWidth = Num(v); break;
            case "border-bottom-width": s.borderBottomWidth = Num(v); break;
            case "border-left-width": s.borderLeftWidth = Num(v); break;
            case "border-top-color": if (TryColor(v, out var btc)) s.borderTopColor = btc; else Unknown(d, warn); break;
            case "border-right-color": if (TryColor(v, out var brc)) s.borderRightColor = brc; else Unknown(d, warn); break;
            case "border-bottom-color": if (TryColor(v, out var bbc)) s.borderBottomColor = bbc; else Unknown(d, warn); break;
            case "border-left-color": if (TryColor(v, out var blc)) s.borderLeftColor = blc; else Unknown(d, warn); break;
            case "border-color":
                if (TryColor(v, out var bc)) { s.borderTopColor = bc; s.borderRightColor = bc; s.borderBottomColor = bc; s.borderLeftColor = bc; }
                else Unknown(d, warn);
                break;
            case "border-radius": Sides(v, out var tl, out var tr, out var brr, out var bll); s.borderTopLeftRadius = tl; s.borderTopRightRadius = tr; s.borderBottomRightRadius = brr; s.borderBottomLeftRadius = bll; break;
            case "border-top-left-radius": s.borderTopLeftRadius = Len(v); break;
            case "border-top-right-radius": s.borderTopRightRadius = Len(v); break;
            case "border-bottom-right-radius": s.borderBottomRightRadius = Len(v); break;
            case "border-bottom-left-radius": s.borderBottomLeftRadius = Len(v); break;

            // Text
            case "font-family":
            {
                // Font files first (the Fonts mod's Barlow etc., as dynamic SDF assets built
                // by UI Toolkit's own text engine), registered TextMeshPro faces second. No
                // OS fonts. An unknown family keeps the inherited legacy face and warns once.
                FontAsset? sdf = null;
                foreach (var raw in v.Split(','))
                {
                    var name = raw.Trim().Trim('"', (char)39);
                    if (name.Length == 0) continue;
                    sdf = FontLibrary.Get(name);
                    if (sdf != null) break;
                }
                if (sdf != null) s.unityFontDefinition = FontDefinition.FromSDFFont(sdf);
                else Unknown(d, warn);
                break;
            }
            case "font":
            {
                // font: [style] [weight] size[/line-height] family-list
                var parts = SplitTopLevel(v);
                var familyStart = -1;
                for (var i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    if (p == "italic" || p == "oblique") { Apply(ve, new CssDeclaration("font-style", "italic"), warn); continue; }
                    if (p == "bold" || p == "bolder" || (IsNumber(p) && Num(p) >= 100)) { Apply(ve, new CssDeclaration("font-weight", p), warn); continue; }
                    if (p == "normal") continue;
                    if (char.IsDigit(p[0]) || p[0] == '.')
                    {
                        var slash = p.IndexOf('/');
                        Apply(ve, new CssDeclaration("font-size", slash > 0 ? p.Substring(0, slash) : p), warn);
                        familyStart = i + 1;
                        break;
                    }
                }
                if (familyStart >= 0 && familyStart < parts.Count)
                    Apply(ve, new CssDeclaration("font-family", string.Join(" ", parts.GetRange(familyStart, parts.Count - familyStart))), warn);
                break;
            }
            case "font-size": s.fontSize = Len(v); break;
            case "font-weight":
            case "font-style":
            {
                var bold = v == "bold" || v == "bolder" || (IsNumber(v) && Num(v) >= 600);
                var italic = v == "italic" || v == "oblique";
                var cur = s.unityFontStyleAndWeight.value;
                var wasBold = cur == FontStyle.Bold || cur == FontStyle.BoldAndItalic;
                var wasItalic = cur == FontStyle.Italic || cur == FontStyle.BoldAndItalic;
                if (d.Name == "font-weight") wasBold = bold; else wasItalic = italic;
                s.unityFontStyleAndWeight = wasBold && wasItalic ? FontStyle.BoldAndItalic : wasBold ? FontStyle.Bold : wasItalic ? FontStyle.Italic : FontStyle.Normal;
                break;
            }
            case "text-align":
                s.unityTextAlign = v switch { "center" => TextAnchor.MiddleCenter, "right" or "end" => TextAnchor.MiddleRight, _ => TextAnchor.MiddleLeft };
                break;
            case "white-space": s.whiteSpace = v == "nowrap" || v == "pre" ? WhiteSpace.NoWrap : WhiteSpace.Normal; break;
            case "letter-spacing": s.letterSpacing = Len(v); break;
            case "word-spacing": s.wordSpacing = Len(v); break;
            case "text-overflow": s.textOverflow = v == "ellipsis" ? TextOverflow.Ellipsis : TextOverflow.Clip; break;
            case "text-shadow":
            {
                // <x> <y> [blur] <color>
                var parts = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var sh = new TextShadow { offset = new Vector2(Num(parts[0]), Num(parts[1])) };
                    var next = 2;
                    if (parts.Length >= 4 && IsNumber(parts[2].TrimEnd('p', 'x'))) { sh.blurRadius = Num(parts[2]); next = 3; }
                    if (TryColor(string.Join(" ", parts, next, parts.Length - next), out var sc)) sh.color = sc;
                    s.textShadow = sh;
                }
                break;
            }

            // Transform
            case "transform":
            {
                if (v == "none") { s.translate = new Translate(0, 0); s.rotate = new Rotate(0); s.scale = new Scale(Vector2.one); break; }
                foreach (var fn in Functions(v))
                {
                    var args = fn.args;
                    switch (fn.name)
                    {
                        case "translate": s.translate = new Translate(Len(args[0]).value, args.Length > 1 ? Len(args[1]).value : new Length(0)); break;
                        case "translatex": s.translate = new Translate(Len(args[0]).value, new Length(0)); break;
                        case "translatey": s.translate = new Translate(new Length(0), Len(args[0]).value); break;
                        case "rotate": s.rotate = new Rotate(Angle(args[0])); break;
                        case "scale": s.scale = new Scale(new Vector2(Num(args[0]), args.Length > 1 ? Num(args[1]) : Num(args[0]))); break;
                        case "scalex": s.scale = new Scale(new Vector2(Num(args[0]), 1)); break;
                        case "scaley": s.scale = new Scale(new Vector2(1, Num(args[0]))); break;
                        default: warn?.Invoke($"css: transform {fn.name}() not supported"); break;
                    }
                }
                break;
            }
            case "transform-origin":
            {
                var parts = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var x = parts.Length > 0 ? OriginLen(parts[0]) : new Length(50, LengthUnit.Percent);
                var y = parts.Length > 1 ? OriginLen(parts[1]) : new Length(50, LengthUnit.Percent);
                s.transformOrigin = new TransformOrigin(x, y, 0);
                break;
            }

            // Transitions
            case "transition":
            {
                // Vector mode: transitions are compiled to expressions (Tweens.cs). UI Toolkit
                // interpolating as well would put a moving snapshot under the emitter.
                if (VectorMode) break;
                // transition: prop dur [easing] [delay] {, ...}
                var props = new List<StylePropertyName>();
                var durs = new List<TimeValue>();
                var eases = new List<EasingFunction>();
                var delays = new List<TimeValue>();
                foreach (var item in v.Split(','))
                {
                    var parts = item.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;
                    props.Add(new StylePropertyName(UssName(parts[0])));
                    durs.Add(parts.Length > 1 ? Time(parts[1]) : new TimeValue(0));
                    var ease = EasingMode.Ease;
                    var delay = new TimeValue(0);
                    for (var i = 2; i < parts.Length; i++)
                    {
                        if (TryEasing(parts[i], out var e)) ease = e; else delay = Time(parts[i]);
                    }
                    eases.Add(new EasingFunction(ease));
                    delays.Add(delay);
                }
                s.transitionProperty = props;
                s.transitionDuration = durs;
                s.transitionTimingFunction = eases;
                s.transitionDelay = delays;
                break;
            }
            case "transition-property": { if (VectorMode) break; var l = new List<StylePropertyName>(); foreach (var p in v.Split(',')) l.Add(new StylePropertyName(UssName(p.Trim()))); s.transitionProperty = l; break; }
            case "transition-duration": { if (VectorMode) break; var l = new List<TimeValue>(); foreach (var p in v.Split(',')) l.Add(Time(p.Trim())); s.transitionDuration = l; break; }
            case "transition-delay": { if (VectorMode) break; var l = new List<TimeValue>(); foreach (var p in v.Split(',')) l.Add(Time(p.Trim())); s.transitionDelay = l; break; }
            case "transition-timing-function": { if (VectorMode) break; var l = new List<EasingFunction>(); foreach (var p in v.Split(',')) l.Add(new EasingFunction(TryEasing(p.Trim(), out var e) ? e : EasingMode.Ease)); s.transitionTimingFunction = l; break; }

            default:
                Unknown(d, warn);
                break;
        }
    }

    /// <summary>
    /// linear-gradient(angle|to side, colour [stop%], ...) baked into a 64-pixel ramp texture
    /// and stretched as the background image. UI Toolkit has no gradient of its own; the ramp
    /// is invisible at any console size once bilinear-filtered. ponytail: snapped to the
    /// nearer axis; a diagonal renders as horizontal or vertical.
    /// </summary>
    private static void Gradient(IStyle s, string v, Action<string>? warn)
    {
        var open = v.IndexOf('(');
        var close = v.LastIndexOf(')');
        if (open < 0 || close < open) { warn?.Invoke($"css: bad gradient \"{v}\""); return; }
        var args = SplitTopLevelCommas(v.Substring(open + 1, close - open - 1));
        var angle = 180f; // CSS default: to bottom
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
            angle = Angle(first);
            args.RemoveAt(0);
        }

        var stops = new List<(float at, Color c)>();
        for (var i = 0; i < args.Count; i++)
        {
            var parts = SplitTopLevel(args[i].Trim());
            if (parts.Count == 0 || !TryColor(parts[0], out var c)) { warn?.Invoke($"css: gradient stop \"{args[i]}\" not understood"); continue; }
            var at = parts.Count > 1 && parts[1].EndsWith("%", StringComparison.Ordinal) ? Num(parts[1]) / 100f : (args.Count == 1 ? 0f : (float)i / (args.Count - 1));
            stops.Add((at, c));
        }
        if (stops.Count == 0) return;
        if (stops.Count == 1) { s.backgroundColor = stops[0].c; return; }

        const int n = 64;
        var sin = Mathf.Sin(angle * Mathf.Deg2Rad);
        var cos = Mathf.Cos(angle * Mathf.Deg2Rad);
        var axisAligned = Mathf.Abs(sin) < 0.01f || Mathf.Abs(cos) < 0.01f;

        Texture2D tex;
        Color[] pixels;
        if (axisAligned)
        {
            var horizontal = Mathf.Abs(sin) > Mathf.Abs(cos);
            // Texture x runs left to right; texture y runs bottom to top. CSS 90deg is "to
            // right" and 180deg is "to bottom", so a vertical ramp is written inverted.
            var flip = horizontal ? sin < 0f : cos < 0f;
            tex = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, mipChain: false);
            pixels = new Color[n];
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / (n - 1);
                if (flip) t = 1f - t;
                pixels[i] = Sample(stops, t);
            }
        }
        else
        {
            // Diagonal: project each texel onto the gradient line. CSS measures the angle
            // clockwise from "to top"; the line is scaled so the corners land on 0 and 1.
            // The texture is square and stretched to the element, so on a non-square box
            // the angle follows the box's diagonal rather than the exact degree value.
            tex = new Texture2D(n, n, TextureFormat.RGBA32, mipChain: false);
            pixels = new Color[n * n];
            var norm = Mathf.Abs(sin) + Mathf.Abs(cos);
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var u = (float)x / (n - 1) - 0.5f;
                    var w = (float)y / (n - 1) - 0.5f;
                    var t = 0.5f + (u * sin + w * cos) / norm;
                    pixels[y * n + x] = Sample(stops, Mathf.Clamp01(t));
                }
            }
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.name = "css gradient";
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        s.backgroundImage = new StyleBackground(tex);
        s.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        s.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        s.backgroundColor = Color.clear;
    }

    private static Color Sample(List<(float at, Color c)> stops, float t)
    {
        if (t <= stops[0].at) return stops[0].c;
        for (var i = 1; i < stops.Count; i++)
        {
            if (t <= stops[i].at)
            {
                var span = stops[i].at - stops[i - 1].at;
                var k = span <= 0f ? 1f : (t - stops[i - 1].at) / span;
                return Color.Lerp(stops[i - 1].c, stops[i].c, k);
            }
        }
        return stops[stops.Count - 1].c;
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

    /// <summary>Handled outside the style object: by the grid layout, the emitter, or by the box model itself.</summary>
    private static readonly HashSet<string> Elsewhere = new(StringComparer.Ordinal)
    {
        "gap", "row-gap", "column-gap", "grid-gap", "grid-row-gap", "grid-column-gap",
        "grid-template-columns", "grid-template-rows", "grid-auto-rows", "grid-auto-columns", "grid-auto-flow",
        "grid-column", "grid-row", "grid-column-start", "grid-column-end", "grid-row-start", "grid-row-end",
        "line-height", "text-transform", "z-index", "box-sizing",
        "box-shadow", "text-shadow", "border-style", "text-decoration",
    };

    private static void Unknown(CssDeclaration d, Action<string>? warn)
    {
        if (Elsewhere.Contains(d.Name))
            return;
        if (Reported.Add(d.Name + ":" + d.Value))
            warn?.Invoke($"css: \"{d.Name}: {d.Value}\" not supported");
    }

    // ---- values ----

    public static bool IsNumber(string v) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>Unit context for em, rem, vw and vh: set per element by the cascade, per page by the renderer.</summary>
    internal static float EmSize = 16f;
    internal static float RootFontSize = 16f;
    internal static float ViewportW = 460f;
    internal static float ViewportH = 460f;

    /// <summary>A number in px (or the bare number of a percentage). Units: px pt em rem vw vh; calc().</summary>
    public static float Num(string v)
    {
        v = v.Trim();
        if (v.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
        {
            Calc(v, out var px, out var pct);
            return Mathf.Abs(px) > 0.0001f || Mathf.Abs(pct) < 0.0001f ? px : pct;
        }
        return Unit(v);
    }

    private static float Unit(string v)
    {
        var scale = 1f;
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 2);
        else if (v.EndsWith("%", StringComparison.Ordinal)) v = v.Substring(0, v.Length - 1);
        else if (v.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) { v = v.Substring(0, v.Length - 3); scale = RootFontSize; }
        else if (v.EndsWith("em", StringComparison.OrdinalIgnoreCase)) { v = v.Substring(0, v.Length - 2); scale = EmSize; }
        else if (v.EndsWith("vw", StringComparison.OrdinalIgnoreCase)) { v = v.Substring(0, v.Length - 2); scale = ViewportW / 100f; }
        else if (v.EndsWith("vh", StringComparison.OrdinalIgnoreCase)) { v = v.Substring(0, v.Length - 2); scale = ViewportH / 100f; }
        else if (v.EndsWith("vmin", StringComparison.OrdinalIgnoreCase)) { v = v.Substring(0, v.Length - 4); scale = Mathf.Min(ViewportW, ViewportH) / 100f; }
        else if (v.EndsWith("pt", StringComparison.OrdinalIgnoreCase)) { v = v.Substring(0, v.Length - 2); scale = 4f / 3f; }
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f * scale : 0f;
    }

    public static StyleLength Len(string v)
    {
        v = v.Trim();
        if (v == "auto") return StyleKeyword.Auto;
        if (v == "initial" || v == "unset") return StyleKeyword.Initial;
        if (v.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
        {
            // px and % cannot be mixed in one length here; whichever part is non-zero wins.
            Calc(v, out var px, out var pct);
            return Mathf.Abs(px) > 0.0001f || Mathf.Abs(pct) < 0.0001f ? new Length(px, LengthUnit.Pixel) : new Length(pct, LengthUnit.Percent);
        }
        if (v.EndsWith("%", StringComparison.Ordinal)) return new Length(Num(v), LengthUnit.Percent);
        return new Length(Num(v), LengthUnit.Pixel);
    }

    /// <summary>calc(): + - * / and parentheses over lengths; px and % accumulate separately.</summary>
    private static void Calc(string v, out float px, out float pct)
    {
        var inner = v.Substring(5, v.Length - 6);
        var pos = 0;
        (px, pct) = CalcExpr(inner, ref pos);
    }

    private static (float px, float pct) CalcExpr(string s, ref int i)
    {
        var (px, pct) = CalcTerm(s, ref i);
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || (s[i] != '+' && s[i] != '-')) break;
            var op = s[i++];
            var (p2, c2) = CalcTerm(s, ref i);
            if (op == '+') { px += p2; pct += c2; } else { px -= p2; pct -= c2; }
        }
        return (px, pct);
    }

    private static (float px, float pct) CalcTerm(string s, ref int i)
    {
        var (px, pct) = CalcFactor(s, ref i);
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || (s[i] != '*' && s[i] != '/')) break;
            var op = s[i++];
            var (p2, c2) = CalcFactor(s, ref i);
            // one side must be a bare number: it is whichever carries no unit
            var k = Mathf.Abs(p2) > 0f || Mathf.Abs(c2) > 0f ? p2 + c2 : 0f;
            if (op == '*') { px *= k; pct *= k; }
            else if (Mathf.Abs(k) > 0.00001f) { px /= k; pct /= k; }
        }
        return (px, pct);
    }

    private static (float px, float pct) CalcFactor(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '(')
        {
            i++;
            var r = CalcExpr(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ')') i++;
            return r;
        }
        var start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.' || s[i] == '%')) i++;
        var tok = s.Substring(start, i - start);
        if (tok.EndsWith("%", StringComparison.Ordinal)) return (0f, Unit(tok));
        return (Unit(tok), 0f);
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
    }

    private static Length OriginLen(string v)
    {
        return v switch
        {
            "left" or "top" => new Length(0, LengthUnit.Percent),
            "center" => new Length(50, LengthUnit.Percent),
            "right" or "bottom" => new Length(100, LengthUnit.Percent),
            _ => Len(v).value,
        };
    }

    private static float Angle(string v)
    {
        v = v.Trim();
        float deg;
        if (v.EndsWith("deg", StringComparison.OrdinalIgnoreCase)) deg = Num(v.Substring(0, v.Length - 3));
        else if (v.EndsWith("rad", StringComparison.OrdinalIgnoreCase)) deg = Num(v.Substring(0, v.Length - 3)) * Mathf.Rad2Deg;
        else if (v.EndsWith("turn", StringComparison.OrdinalIgnoreCase)) deg = Num(v.Substring(0, v.Length - 4)) * 360f;
        else deg = Num(v);
        // A whole number of turns is the same rotation as none, and a transition to "the
        // same value" does nothing. Browsers animate rotate(360deg) as a full turn; shave
        // a hundredth of a degree so UI Toolkit sees a change to interpolate.
        if (deg != 0f && Mathf.Abs(deg % 360f) < 0.001f)
            deg -= Mathf.Sign(deg) * 0.01f;
        return deg;
    }

    private static TimeValue Time(string v)
    {
        v = v.Trim();
        if (v.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) return new TimeValue(Num(v.Substring(0, v.Length - 2)), TimeUnit.Millisecond);
        if (v.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return new TimeValue(Num(v.Substring(0, v.Length - 1)), TimeUnit.Second);
        return new TimeValue(Num(v), TimeUnit.Second);
    }

    internal static bool TryEasing(string v, out EasingMode mode)
    {
        switch (v)
        {
            case "ease": mode = EasingMode.Ease; return true;
            case "linear": mode = EasingMode.Linear; return true;
            case "ease-in": mode = EasingMode.EaseIn; return true;
            case "ease-out": mode = EasingMode.EaseOut; return true;
            case "ease-in-out": mode = EasingMode.EaseInOut; return true;
            default: mode = EasingMode.Ease; return false;
        }
    }

    /// <summary>CSS property name to the USS name UI Toolkit's transition system expects.</summary>
    private static string UssName(string css)
    {
        return css switch
        {
            "transform" => "translate",
            "color" => "color",
            "font-weight" or "font-style" => "-unity-font-style",
            "text-align" => "-unity-text-align",
            "background" => "background-color",
            _ => css,
        };
    }

    private static void Sides(string v, out StyleLength top, out StyleLength right, out StyleLength bottom, out StyleLength left)
    {
        var p = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) p = new[] { "0" }; // an unresolved var() or an empty value: zero, not a crash
        top = Len(p[0]);
        right = p.Length > 1 ? Len(p[1]) : top;
        bottom = p.Length > 2 ? Len(p[2]) : top;
        left = p.Length > 3 ? Len(p[3]) : right;
    }

    private static StyleEnum<Align> AlignOf(string v)
    {
        return v switch
        {
            "center" => Align.Center,
            "flex-end" or "end" => Align.FlexEnd,
            "stretch" => Align.Stretch,
            "flex-start" or "start" => Align.FlexStart,
            _ => Align.Auto,
        };
    }

    private static void Border(IStyle s, string v, Action<string>? warn, bool t, bool r, bool b, bool l)
    {
        // border: <width> [style] [color], any order; style is ignored (UI Toolkit draws solid).
        if (v == "none" || v == "0")
        {
            if (t) s.borderTopWidth = 0; if (r) s.borderRightWidth = 0; if (b) s.borderBottomWidth = 0; if (l) s.borderLeftWidth = 0;
            return;
        }
        float? width = null;
        Color? color = null;
        var parts = SplitTopLevel(v);
        foreach (var part in parts)
        {
            if (part == "solid" || part == "dashed" || part == "dotted" || part == "double") continue;
            if (IsNumber(part) || part.EndsWith("px", StringComparison.OrdinalIgnoreCase)) width = Num(part);
            else if (TryColor(part, out var c)) color = c;
            else warn?.Invoke($"css: border value \"{part}\" not understood");
        }
        if (width.HasValue)
        {
            if (t) s.borderTopWidth = width.Value; if (r) s.borderRightWidth = width.Value; if (b) s.borderBottomWidth = width.Value; if (l) s.borderLeftWidth = width.Value;
        }
        if (color.HasValue)
        {
            if (t) s.borderTopColor = color.Value; if (r) s.borderRightColor = color.Value; if (b) s.borderBottomColor = color.Value; if (l) s.borderLeftColor = color.Value;
        }
    }

    /// <summary>Split on spaces outside parentheses so rgba(1, 2, 3) stays whole.</summary>
    private static List<string> SplitTopLevel(string v)
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
                if (!(v[i] == ' ' && depth == 0)) continue;
            }
            if (i > start) list.Add(v.Substring(start, i - start));
            start = i + 1;
        }
        return list;
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

    // ---- colour ----

    private static readonly Dictionary<string, Color> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Color.black, ["white"] = Color.white, ["red"] = Color.red, ["green"] = new Color(0, 0.5f, 0),
        ["lime"] = Color.green, ["blue"] = Color.blue, ["yellow"] = Color.yellow, ["cyan"] = Color.cyan, ["aqua"] = Color.cyan,
        ["magenta"] = Color.magenta, ["fuchsia"] = Color.magenta, ["gray"] = Color.gray, ["grey"] = Color.gray,
        ["silver"] = new Color(0.75f, 0.75f, 0.75f), ["orange"] = new Color(1f, 0.647f, 0f), ["navy"] = new Color(0, 0, 0.5f),
        ["teal"] = new Color(0, 0.5f, 0.5f), ["maroon"] = new Color(0.5f, 0, 0), ["purple"] = new Color(0.5f, 0, 0.5f),
        ["olive"] = new Color(0.5f, 0.5f, 0), ["transparent"] = Color.clear,
    };

    /// <summary>#rgb #rgba #rrggbb #rrggbbaa rgb() rgba() hsl() and a few names. Managed code only:
    /// ColorUtility.TryParseHtmlString is a native call that fails headless.</summary>
    public static bool TryColor(string v, out Color color)
    {
        v = v.Trim();
        color = Color.white;
        if (v.Length == 0) return false;
        if (Named.TryGetValue(v, out color)) return true;

        if (v[0] == '#')
        {
            var h = v.Substring(1);
            if (h.Length == 3 || h.Length == 4)
            {
                var e = new char[h.Length * 2];
                for (var i = 0; i < h.Length; i++) { e[i * 2] = h[i]; e[i * 2 + 1] = h[i]; }
                h = new string(e);
            }
            if (h.Length != 6 && h.Length != 8) return false;
            if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return false;
            if (h.Length == 6) n = (n << 8) | 0xFF;
            color = new Color(((n >> 24) & 0xFF) / 255f, ((n >> 16) & 0xFF) / 255f, ((n >> 8) & 0xFF) / 255f, (n & 0xFF) / 255f);
            return true;
        }

        var lower = v.ToLowerInvariant();
        if (lower.StartsWith("rgb", StringComparison.Ordinal) || lower.StartsWith("hsl", StringComparison.Ordinal))
        {
            var open = v.IndexOf('(');
            var close = v.LastIndexOf(')');
            if (open < 0 || close < open) return false;
            var args = v.Substring(open + 1, close - open - 1).Replace('/', ',').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length < 3) return false;
            float Channel(string a) => a.EndsWith("%", StringComparison.Ordinal) ? Num(a) / 100f : Num(a) / 255f;
            float Alpha(string a) => a.EndsWith("%", StringComparison.Ordinal) ? Num(a) / 100f : Num(a);
            var alpha = args.Length > 3 ? Alpha(args[3]) : 1f;
            if (lower[0] == 'r')
            {
                color = new Color(Channel(args[0]), Channel(args[1]), Channel(args[2]), alpha);
            }
            else
            {
                var hue = Num(args[0]) / 360f;
                var sat = Num(args[1]) / 100f;
                var light = Num(args[2]) / 100f;
                // HSL to RGB via HSV: v = l + s*min(l,1-l); s_v = 2*(1 - l/v)
                var val = light + sat * Mathf.Min(light, 1f - light);
                var sv = val <= 0f ? 0f : 2f * (1f - light / val);
                color = Color.HSVToRGB(hue, sv, val);
                color.a = alpha;
            }
            return true;
        }
        return false;
    }
}

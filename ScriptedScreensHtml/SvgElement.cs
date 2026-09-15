using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>One SVG shape: tag plus attributes, re-read on every paint so a data update
/// that changes an attribute redraws without rebuilding anything.</summary>
internal sealed class SvgShape
{
    public string Tag = string.Empty;
    public readonly Dictionary<string, string> Attributes = new(StringComparer.OrdinalIgnoreCase);
    public SvgElement? Owner;
    /// <summary>A clipPath's shapes, which draw nothing themselves and become one CP def.</summary>
    public List<SvgShape>? Children;

    // Point interpolation. A data tick arrives ~2 Hz and a polyline's points are not a
    // style, so nothing eases them: the graph stepped. Successive arrays are the same
    // window shifted one slot, so lerping old[i] -> new[i] over the measured tick
    // interval is a true scroll, not a cross-fade (same finding as the vector layer).
    private List<float>? _from;
    private List<float>? _to;
    private float _blendStart;
    private float _blendSeconds = 0.5f;
    private float _lastSet = -1f;

    public string? Attr(string name) => Attributes.TryGetValue(name, out var v) ? v : null;

    public bool Blending => _from != null && _to != null;

    public void Set(string name, string value)
    {
        Attributes[name] = value;
        if (string.Equals(name, "points", StringComparison.OrdinalIgnoreCase))
            BeginBlend(SvgElement.Numbers(value));
        Owner?.MarkDirtyRepaint();
    }

    private void BeginBlend(List<float> next)
    {
        var now = Time.time;
        if (_lastSet >= 0f)
            _blendSeconds = Mathf.Clamp(now - _lastSet, 0.05f, 1.5f);
        _lastSet = now;

        // Blend from wherever the previous blend currently is, so a late tick does not snap.
        var current = _to != null && _from != null ? Interpolated(now) : _to;
        if (current == null || current.Count != next.Count)
        {
            _from = null;
            _to = next;
            return;
        }
        _from = current;
        _to = next;
        _blendStart = now;
    }

    private List<float> Interpolated(float now)
    {
        var t = Mathf.Clamp01((now - _blendStart) / _blendSeconds);
        var list = new List<float>(_to!.Count);
        for (var i = 0; i < _to.Count; i++)
            list.Add(Mathf.Lerp(_from![i], _to[i], t));
        return list;
    }

    /// <summary>Points to draw now; advances the blend and ends it when complete.</summary>
    public List<float> CurrentPoints(float now)
    {
        if (_from == null || _to == null)
            return _to ?? SvgElement.Numbers(Attr("points"));
        var t = (now - _blendStart) / _blendSeconds;
        if (t >= 1f)
        {
            _from = null;
            return _to;
        }
        return Interpolated(now);
    }
}

/// <summary>
/// Inline <c>&lt;svg&gt;</c>: a VisualElement that draws its shapes with Painter2D, scaled
/// from the viewBox to its own rect. Supports polyline, polygon, line, rect, circle,
/// ellipse and a path subset (M L H V C Q Z, absolute and relative). Fill, stroke,
/// stroke-width, opacity, fill-opacity, stroke-opacity, stroke-linecap, stroke-linejoin,
/// and preserveAspectRatio="none" (default is uniform, centred). ponytail: no arcs (A),
/// no dashes, no transforms, no text, no gradients; add when a page needs one.
/// </summary>
internal sealed class SvgElement : VisualElement
{
    public readonly List<SvgShape> Shapes = new();
    public Rect ViewBox = new(0, 0, 100, 100);
    public bool Stretch;

    public SvgElement()
    {
        generateVisualContent += Paint;
    }

    /// <summary>True while any shape is interpolating; the surface repaints it per frame.</summary>
    public bool Blending
    {
        get
        {
            foreach (var s in Shapes)
            {
                if (s.Blending)
                    return true;
            }
            return false;
        }
    }

    private void Paint(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        var rect = contentRect;
        if (rect.width <= 0f || rect.height <= 0f || ViewBox.width <= 0f || ViewBox.height <= 0f)
            return;

        float sx, sy, ox, oy;
        if (Stretch)
        {
            sx = rect.width / ViewBox.width;
            sy = rect.height / ViewBox.height;
            ox = rect.x;
            oy = rect.y;
        }
        else
        {
            var s = Mathf.Min(rect.width / ViewBox.width, rect.height / ViewBox.height);
            sx = sy = s;
            ox = rect.x + (rect.width - ViewBox.width * s) * 0.5f;
            oy = rect.y + (rect.height - ViewBox.height * s) * 0.5f;
        }

        Vector2 Map(float x, float y) => new(ox + (x - ViewBox.x) * sx, oy + (y - ViewBox.y) * sy);
        var strokeScale = (sx + sy) * 0.5f;

        foreach (var shape in Shapes)
        {
            var opacity = Num(shape.Attr("opacity"), 1f);
            var hasFill = TryPaint(shape.Attr("fill"), shape.Tag == "line" || shape.Tag == "polyline" ? "none" : "black", Num(shape.Attr("fill-opacity"), 1f) * opacity, out var fill);
            var hasStroke = TryPaint(shape.Attr("stroke"), "none", Num(shape.Attr("stroke-opacity"), 1f) * opacity, out var stroke);
            if (!hasFill && !hasStroke)
                continue;

            p.BeginPath();
            var closed = BuildPath(p, shape, Map);
            if (closed)
                p.ClosePath();

            if (hasFill)
            {
                p.fillColor = fill;
                p.Fill();
            }
            if (hasStroke)
            {
                p.strokeColor = stroke;
                p.lineWidth = Num(shape.Attr("stroke-width"), 1f) * strokeScale;
                p.lineCap = shape.Attr("stroke-linecap") switch { "round" => LineCap.Round, _ => LineCap.Butt };
                p.lineJoin = shape.Attr("stroke-linejoin") switch { "round" => LineJoin.Round, "bevel" => LineJoin.Bevel, _ => LineJoin.Miter };
                p.Stroke();
            }
        }
    }

    /// <summary>Emit the shape's geometry; returns true when the path should be closed.</summary>
    private static bool BuildPath(Painter2D p, SvgShape shape, Func<float, float, Vector2> map)
    {
        switch (shape.Tag)
        {
            case "line":
                p.MoveTo(map(Num(shape.Attr("x1"), 0), Num(shape.Attr("y1"), 0)));
                p.LineTo(map(Num(shape.Attr("x2"), 0), Num(shape.Attr("y2"), 0)));
                return false;

            case "polyline":
            case "polygon":
            {
                var pts = shape.CurrentPoints(Time.time);
                for (var i = 0; i + 1 < pts.Count; i += 2)
                {
                    var v = map(pts[i], pts[i + 1]);
                    if (i == 0) p.MoveTo(v); else p.LineTo(v);
                }
                return shape.Tag == "polygon";
            }

            case "rect":
            {
                var x = Num(shape.Attr("x"), 0); var y = Num(shape.Attr("y"), 0);
                var w = Num(shape.Attr("width"), 0); var h = Num(shape.Attr("height"), 0);
                var r = Num(shape.Attr("rx"), Num(shape.Attr("ry"), 0));
                if (r <= 0f)
                {
                    p.MoveTo(map(x, y)); p.LineTo(map(x + w, y)); p.LineTo(map(x + w, y + h)); p.LineTo(map(x, y + h));
                }
                else
                {
                    r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
                    p.MoveTo(map(x + r, y));
                    p.ArcTo(map(x + w, y), map(x + w, y + h), RadiusPx(r, map));
                    p.ArcTo(map(x + w, y + h), map(x, y + h), RadiusPx(r, map));
                    p.ArcTo(map(x, y + h), map(x, y), RadiusPx(r, map));
                    p.ArcTo(map(x, y), map(x + w, y), RadiusPx(r, map));
                }
                return true;
            }

            case "circle":
            case "ellipse":
            {
                var cx = Num(shape.Attr("cx"), 0); var cy = Num(shape.Attr("cy"), 0);
                var rx = shape.Tag == "circle" ? Num(shape.Attr("r"), 0) : Num(shape.Attr("rx"), 0);
                var ry = shape.Tag == "circle" ? rx : Num(shape.Attr("ry"), 0);
                // Painter2D.Arc is circular; an ellipse is approximated with 4 cubic beziers.
                const float k = 0.5522847f;
                p.MoveTo(map(cx + rx, cy));
                p.BezierCurveTo(map(cx + rx, cy + ry * k), map(cx + rx * k, cy + ry), map(cx, cy + ry));
                p.BezierCurveTo(map(cx - rx * k, cy + ry), map(cx - rx, cy + ry * k), map(cx - rx, cy));
                p.BezierCurveTo(map(cx - rx, cy - ry * k), map(cx - rx * k, cy - ry), map(cx, cy - ry));
                p.BezierCurveTo(map(cx + rx * k, cy - ry), map(cx + rx, cy - ry * k), map(cx + rx, cy));
                return true;
            }

            case "path":
                return PathData(p, shape.Attr("d") ?? string.Empty, map);
        }
        return false;
    }

    private static float RadiusPx(float r, Func<float, float, Vector2> map)
    {
        return Vector2.Distance(map(0, 0), map(r, 0));
    }

    /// <summary>M L H V C Q Z (and lowercase relative). Returns whether Z closed the last subpath.</summary>
    private static bool PathData(Painter2D p, string d, Func<float, float, Vector2> map)
    {
        var tokens = Tokenize(d);
        var i = 0;
        var cmd = 'M';
        float cx = 0, cy = 0, startX = 0, startY = 0;
        var closed = false;

        float Next() => i < tokens.Count ? tokens[i++].number : 0f;

        while (i < tokens.Count)
        {
            if (tokens[i].command != '\0')
                cmd = tokens[i++].command;
            var rel = char.IsLower(cmd);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                {
                    var x = Next(); var y = Next();
                    if (rel) { x += cx; y += cy; }
                    p.MoveTo(map(x, y)); cx = x; cy = y; startX = x; startY = y;
                    cmd = rel ? 'l' : 'L'; // implicit lineto after moveto
                    closed = false;
                    break;
                }
                case 'L':
                {
                    var x = Next(); var y = Next();
                    if (rel) { x += cx; y += cy; }
                    p.LineTo(map(x, y)); cx = x; cy = y;
                    break;
                }
                case 'H': { var x = Next(); if (rel) x += cx; p.LineTo(map(x, cy)); cx = x; break; }
                case 'V': { var y = Next(); if (rel) y += cy; p.LineTo(map(cx, y)); cy = y; break; }
                case 'C':
                {
                    var x1 = Next(); var y1 = Next(); var x2 = Next(); var y2 = Next(); var x = Next(); var y = Next();
                    if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                    p.BezierCurveTo(map(x1, y1), map(x2, y2), map(x, y)); cx = x; cy = y;
                    break;
                }
                case 'Q':
                {
                    var x1 = Next(); var y1 = Next(); var x = Next(); var y = Next();
                    if (rel) { x1 += cx; y1 += cy; x += cx; y += cy; }
                    p.QuadraticCurveTo(map(x1, y1), map(x, y)); cx = x; cy = y;
                    break;
                }
                case 'Z':
                    p.ClosePath(); cx = startX; cy = startY; closed = true;
                    break;
                default:
                    // Unsupported command (A, S, T): skip its numbers.
                    while (i < tokens.Count && tokens[i].command == '\0') i++;
                    break;
            }
        }
        return closed;
    }

    private static List<(char command, float number)> Tokenize(string d)
    {
        var list = new List<(char, float)>();
        var i = 0;
        while (i < d.Length)
        {
            var c = d[i];
            if (char.IsLetter(c)) { list.Add((c, 0f)); i++; continue; }
            if (char.IsDigit(c) || c == '-' || c == '.' || c == '+')
            {
                var start = i++;
                while (i < d.Length && (char.IsDigit(d[i]) || d[i] == '.' || d[i] == 'e' || d[i] == 'E' || ((d[i] == '-' || d[i] == '+') && (d[i - 1] == 'e' || d[i - 1] == 'E'))))
                    i++;
                if (float.TryParse(d.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    list.Add(('\0', n));
                continue;
            }
            i++;
        }
        return list;
    }

    internal static List<float> Numbers(string? s)
    {
        var list = new List<float>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var t in s!.Split(new[] { ' ', ',', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                list.Add(n);
        }
        return list;
    }

    private static float Num(string? s, float fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        return float.TryParse(s!.Trim().TrimEnd('p', 'x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }

    private static bool TryPaint(string? value, string fallback, float alpha, out Color color)
    {
        var v = string.IsNullOrEmpty(value) ? fallback : value!.Trim();
        color = Color.clear;
        if (v == "none" || v == "transparent")
            return false;
        if (!StyleApplier.TryColor(v, out color))
            return false;
        color.a *= alpha;
        return color.a > 0f;
    }
}

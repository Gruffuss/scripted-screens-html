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
        var now = OffThread.Now;
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
/// Inline <c>&lt;svg&gt;</c>: an element holding its shapes for the emitter, scaled
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

    private static float RadiusPx(float r, Func<float, float, Vector2> map)
    {
        return Vector2.Distance(map(0, 0), map(r, 0));
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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// Two things a browser settles during layout that the layout engine here cannot express,
/// done from the laid-out geometry instead (the way GridLayout places its items):
/// a mixed calc() such as `height: calc(75% - 12px)` against the containing block, and
/// `align-items: baseline` on a flex row from each child's font metrics.
/// </summary>
internal static class PostLayout
{
    public static void Attach(HtmlRenderer.Result built)
    {
        foreach (var kv in StyleApplier.MixedCalc)
        {
            var ve = kv.Key;
            var entries = new List<(string prop, float px, float pct)>(kv.Value);
            var parent = ve.parent;
            if (parent == null || !built.LayoutAttached.Add(ve)) continue;
            void Apply()
            {
                var prs = parent.resolvedStyle;
                var cw = parent.layout.width - prs.paddingLeft - prs.paddingRight - prs.borderLeftWidth - prs.borderRightWidth;
                var chh = parent.layout.height - prs.paddingTop - prs.paddingBottom - prs.borderTopWidth - prs.borderBottomWidth;
                if (float.IsNaN(cw) || float.IsNaN(chh)) return;
                foreach (var (prop, px, pct) in entries)
                {
                    var horizontal = prop is "width" or "min-width" or "max-width" or "left" or "right";
                    var value = pct / 100f * (horizontal ? cw : chh) + px;
                    SetPx(ve.style, prop, value);
                }
            }
            parent.RegisterCallback<GeometryChangedEvent>(_ => Apply());
            built.AfterRecascade.Add(Apply);
        }
        // line-height: a label's box is its line count times the line height, as a browser's line
        // boxes are (the layout engine sizes a label from the font's own line metrics). Skipped for a
        // label with its own height or flex, and for one a row stretches.
        foreach (var kv in built.NodeOf)
        {
            if (kv.Key is not Label label || kv.Value.IsText) continue;
            var lhText = LineHeightOf(built, label);
            if (lhText == null || built.LayoutAttached.Contains(label)) continue;
            var own = built.CssOf(label);
            if (own.ContainsKey("height") || own.ContainsKey("min-height") || own.ContainsKey("flex") || own.ContainsKey("flex-basis")) continue;
            var lbl = label;
            var text = lhText;
            void FitLines()
            {
                var parent = lbl.parent;
                if (parent != null)
                {
                    var prs = parent.resolvedStyle;
                    var row = prs.flexDirection == FlexDirection.Row || prs.flexDirection == FlexDirection.RowReverse;
                    if (row && (prs.alignItems == Align.Stretch || prs.alignItems == Align.Auto) && lbl.resolvedStyle.alignSelf is Align.Auto or Align.Stretch) return;
                }
                if (lbl.resolvedStyle.flexGrow > 0f) return; // a growing label's height is the flex layout's to set
                if (string.IsNullOrWhiteSpace(lbl.text) || lbl.resolvedStyle.position == Position.Absolute) return; // an empty box, or one placed by its edges
                var fs = lbl.resolvedStyle.fontSize;
                if (fs <= 0f) return;
                var lh = LineHeightPx(text, fs);
                if (lh <= 0f) return;
                var oneLine = lbl.MeasureTextSize("X", 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).y;
                if (oneLine <= 0f || float.IsNaN(oneLine)) return;
                var w = lbl.layout.width;
                if (float.IsNaN(w) || w <= 0f) return;
                var natural = lbl.MeasureTextSize(lbl.text ?? string.Empty, w, VisualElement.MeasureMode.AtMost, 0f, VisualElement.MeasureMode.Undefined).y;
                var lines = Mathf.Max(1, Mathf.RoundToInt(natural / oneLine));
                var target = lines * lh;
                if (Differs(lbl.style.height, target)) lbl.style.height = target;
            }
            built.LayoutAttached.Add(label);
            label.RegisterCallback<GeometryChangedEvent>(_ => FitLines());
            built.AfterRecascade.Add(FitLines);
        }
        foreach (var row in StyleApplier.BaselineRows)
        {
            var container = row;
            if (!built.LayoutAttached.Add(container)) continue;
            container.RegisterCallback<GeometryChangedEvent>(_ => AlignBaselines(container));
            built.AfterRecascade.Add(() => AlignBaselines(container));
        }
    }

    private static void SetPx(IStyle s, string prop, float v)
    {
        switch (prop)
        {
            case "width": if (Differs(s.width, v)) s.width = v; break;
            case "height": if (Differs(s.height, v)) s.height = v; break;
            case "min-width": if (Differs(s.minWidth, v)) s.minWidth = v; break;
            case "min-height": if (Differs(s.minHeight, v)) s.minHeight = v; break;
            case "max-width": if (Differs(s.maxWidth, v)) s.maxWidth = v; break;
            case "max-height": if (Differs(s.maxHeight, v)) s.maxHeight = v; break;
            case "left": if (Differs(s.left, v)) s.left = v; break;
            case "top": if (Differs(s.top, v)) s.top = v; break;
            case "right": if (Differs(s.right, v)) s.right = v; break;
            case "bottom": if (Differs(s.bottom, v)) s.bottom = v; break;
        }
    }

    /// <summary>The line-height that applies to a label: its own, else the nearest ancestor's; null for normal.</summary>
    private static string? LineHeightOf(HtmlRenderer.Result built, VisualElement ve)
    {
        for (var p = ve; p != null; p = p.parent)
        {
            if (built.CssOf(p).TryGetValue("line-height", out var v))
            {
                v = v.Trim();
                return v == "normal" || v == "inherit" || v == "initial" ? null : v;
            }
        }
        return null;
    }

    private static float LineHeightPx(string v, float fontSize)
    {
        if (v.EndsWith("px", System.StringComparison.OrdinalIgnoreCase)) return StyleApplier.Num(v);
        if (v.EndsWith("%", System.StringComparison.Ordinal)) return StyleApplier.Num(v) / 100f * fontSize;
        if (v.EndsWith("em", System.StringComparison.OrdinalIgnoreCase) && float.TryParse(v.Substring(0, v.Length - 2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var em)) return em * fontSize;
        return float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n * fontSize : StyleApplier.Num(v);
    }

    private static bool Differs(StyleLength l, float v) => l.keyword != StyleKeyword.Undefined || l.value.unit != LengthUnit.Pixel || Mathf.Abs(l.value.value - v) > 0.5f;

    /// <summary>
    /// Bottoms are aligned (flex-end); a child whose baseline sits higher above its bottom
    /// than the deepest one gets that difference as a bottom margin, so every baseline lands
    /// on the deepest child's. Baseline height above the box bottom is taken as half the
    /// leading plus the descender, 0.22 of the font size.
    /// </summary>
    private static void AlignBaselines(VisualElement row)
    {
        var lift = new List<(VisualElement ve, float b)>();
        var deepest = 0f;
        foreach (var child in row.Children())
        {
            if (child.resolvedStyle.display == DisplayStyle.None) continue;
            var fs = FontSizeOf(child);
            var h = child.layout.height;
            if (float.IsNaN(h) || fs <= 0f) continue;
            var b = Mathf.Max(0f, (h - fs) * 0.5f) + 0.22f * fs;
            lift.Add((child, b));
            deepest = Mathf.Max(deepest, b);
        }
        foreach (var (child, b) in lift)
        {
            var margin = deepest - b;
            var cur = child.style.marginBottom;
            if (cur.keyword == StyleKeyword.Undefined && cur.value.unit == LengthUnit.Pixel && Mathf.Abs(cur.value.value - margin) < 0.5f) continue;
            child.style.marginBottom = margin;
        }
    }

    /// <summary>The font size that sets a child's baseline: its own label, or the largest label inside it.</summary>
    private static float FontSizeOf(VisualElement ve)
    {
        if (ve is Label) return ve.resolvedStyle.fontSize;
        var best = 0f;
        foreach (var c in ve.Children()) best = Mathf.Max(best, FontSizeOf(c));
        return best > 0f ? best : ve.resolvedStyle.fontSize;
    }
}

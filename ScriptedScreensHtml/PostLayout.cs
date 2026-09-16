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
            if (parent == null) continue;
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
        foreach (var row in StyleApplier.BaselineRows)
        {
            var container = row;
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

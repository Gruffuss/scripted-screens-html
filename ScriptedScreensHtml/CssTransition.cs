using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScriptedScreensHtml;

/// <summary>
/// Which transition, if any, a CSS declaration set gives one property. Its own file so the headless
/// suite can compile it: the class that uses it, <see cref="DataSlots"/>, needs ScriptedScreens types.
/// </summary>
internal static class CssTransition
{
    /// <summary>
    /// The CSS transition timing for one property, or null when nothing transitions it: the
    /// shorthand's items and the longhands (Tailwind writes those), the property named or <c>all</c>
    /// or a shorthand of it (<c>background</c> covers <c>background-color</c>), first time the
    /// duration, second the delay. The curve token is kept verbatim for the renderer, which reads
    /// the same CSS spellings; <c>step-start</c> jumps at once, which is a snap.
    /// </summary>
    internal static (float Dur, string Curve, float Delay)? For(IReadOnlyDictionary<string, string> css, string property)
    {
        css.TryGetValue("transition", out var shorthand);
        css.TryGetValue("transition-property", out var lProp);
        css.TryGetValue("transition-duration", out var lDur);
        css.TryGetValue("transition-delay", out var lDelay);
        css.TryGetValue("transition-timing-function", out var lEase);
        if (shorthand == null && lProp == null && lDur == null) return null;

        var names = lProp?.Split(',');
        var items = shorthand != null ? CssParser.SplitTopLevel(shorthand, ',') : null;
        var count = Math.Max(items?.Count ?? 0, names?.Length ?? 0);
        if (count == 0) count = 1;
        for (var k = 0; k < count; k++)
        {
            var prop = "all";
            var dur = 0f;
            var delay = 0f;
            var curve = "ease";
            if (items != null && k < items.Count)
            {
                var times = 0;
                var named = false;
                foreach (var raw in CssParser.SplitTopLevel(items[k].Trim(), ' '))
                {
                    var part = raw.Trim();
                    if (part.Length == 0) continue;
                    if (Seconds(part) is { } s) { if (times++ == 0) dur = s; else delay = s; }
                    else if (Tweens.Easing.TryParse(part, out _)) curve = part.ToLowerInvariant();
                    else if (!named) { prop = part.ToLowerInvariant(); named = true; }
                }
            }
            if (names is { Length: > 0 }) prop = names[k % names.Length].Trim().ToLowerInvariant();
            if (lDur != null && Seconds(Nth(lDur, k)) is { } d) dur = d;
            if (lDelay != null && Seconds(Nth(lDelay, k)) is { } dl) delay = dl;
            if (lEase != null && Tweens.Easing.TryParse(Nth(lEase, k), out _)) curve = Nth(lEase, k).ToLowerInvariant();
            if (prop != "all" && prop != property && !property.StartsWith(prop + "-", StringComparison.Ordinal)) continue;
            if (prop == "none") return null;
            if (curve == "step-start") return (0f, "linear", delay);
            if (curve == "step-end") curve = "steps(1)";
            return (dur, curve, delay);
        }
        return null;
    }

    private static string Nth(string list, int k)
    {
        var parts = list.Split(',');
        return parts[k % parts.Length].Trim();
    }

    /// <summary>A CSS time as seconds, or null when the token is not one.</summary>
    private static float? Seconds(string t)
    {
        var ms = t.EndsWith("ms", StringComparison.OrdinalIgnoreCase);
        var s = !ms && t.EndsWith("s", StringComparison.OrdinalIgnoreCase);
        if (!ms && !s) return null;
        var num = t.Substring(0, t.Length - (ms ? 2 : 1));
        if (!float.TryParse(num, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return null;
        return ms ? v / 1000f : v;
    }
}

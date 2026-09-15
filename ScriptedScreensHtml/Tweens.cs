using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// CSS transitions and keyframe segments as vector expressions over <c>t</c>.
/// </summary>
/// <remarks>
/// UI Toolkit's own transitions are switched off in vector mode: the emitter translates a
/// snapshot, so an interpolating layout would only ever show its end state. Instead this
/// keeps what the vector scene currently shows per element, diffs it against the layout
/// after every change, and where the element's CSS names a transition (or a keyframe
/// runner has set one), starts a tween: from, to, start, duration, easing. The emitter
/// writes the tweened numbers as <c>=from+(to-from)*ease(clamp((t-start)/dur,0,1))</c>,
/// the vector mod animates them every frame with nothing else running, and when the last
/// tween ends the scene is re-emitted once with plain numbers so it goes static again.
/// Both sides read <c>Time.time</c>, so <c>t</c> is the same clock.
/// </remarks>
internal sealed class Tweens
{
    internal struct Snap
    {
        public Rect Rect;
        public float Opacity;
        public float Rotate;
        public Vector2 Translate;
        public Vector2 Scale;
        public Color Bg;
        public Color Fg;

        public static Snap Of(VisualElement ve)
        {
            var rs = ve.resolvedStyle;
            return new Snap
            {
                Rect = ve.layout,
                Opacity = rs.opacity,
                Rotate = rs.rotate.angle.ToDegrees(),
                Translate = new Vector2(rs.translate.x, rs.translate.y),
                Scale = new Vector2(rs.scale.value.x, rs.scale.value.y),
                Bg = rs.backgroundColor,
                Fg = rs.color,
            };
        }

        public bool ColourDiffers(in Snap o) => !NearColour(Bg, o.Bg) || !NearColour(Fg, o.Fg);
        internal static bool NearColour(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f && Mathf.Abs(a.a - b.a) < 0.004f;

        public bool LayoutDiffers(in Snap o) => !Near(Rect.x, o.Rect.x) || !Near(Rect.y, o.Rect.y) || !Near(Rect.width, o.Rect.width) || !Near(Rect.height, o.Rect.height);
        public bool OpacityDiffers(in Snap o) => !Near(Opacity, o.Opacity);
        public bool TransformDiffers(in Snap o) => !Near(Rotate, o.Rotate) || !Near(Translate.x, o.Translate.x) || !Near(Translate.y, o.Translate.y) || !Near(Scale.x, o.Scale.x) || !Near(Scale.y, o.Scale.y);
        public bool Differs(in Snap o) => LayoutDiffers(o) || OpacityDiffers(o) || TransformDiffers(o) || ColourDiffers(o);

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;
    }

    /// <summary>
    /// A CSS timing function: the keywords, steps(n[, start|end]) and cubic-bezier(). Emitted
    /// as an expression over the tween's progress and evaluated in C# for snapshots. A bezier
    /// is approximated by evaluating y at t = progress (exact when x(t) = t, close for the
    /// eases pages use).
    /// </summary>
    internal readonly struct Easing
    {
        public readonly EasingMode Mode;
        public readonly int Steps;
        public readonly bool JumpStart;
        public readonly float Y1, Y2;
        public readonly bool Bezier;

        private Easing(EasingMode mode, int steps, bool jumpStart, bool bezier, float y1, float y2)
        {
            Mode = mode; Steps = steps; JumpStart = jumpStart; Bezier = bezier; Y1 = y1; Y2 = y2;
        }

        public static readonly Easing Default = new(EasingMode.Ease, 0, false, false, 0f, 0f);

        public static bool TryParse(string v, out Easing e)
        {
            var t = v.Trim().ToLowerInvariant();
            e = Default;
            switch (t)
            {
                case "ease": return true;
                case "linear": e = new Easing(EasingMode.Linear, 0, false, false, 0f, 0f); return true;
                case "ease-in": e = new Easing(EasingMode.EaseIn, 0, false, false, 0f, 0f); return true;
                case "ease-out": e = new Easing(EasingMode.EaseOut, 0, false, false, 0f, 0f); return true;
                case "ease-in-out": e = new Easing(EasingMode.EaseInOut, 0, false, false, 0f, 0f); return true;
                case "step-start": e = new Easing(EasingMode.Linear, 1, true, false, 0f, 0f); return true;
                case "step-end": e = new Easing(EasingMode.Linear, 1, false, false, 0f, 0f); return true;
            }
            if (t.StartsWith("steps(", StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal))
            {
                var args = t.Substring(6, t.Length - 7).Split(',');
                var n = int.TryParse(args[0].Trim(), out var k) ? Math.Max(1, k) : 1;
                var start = args.Length > 1 && args[1].Trim() is "start" or "jump-start" or "jump-both";
                e = new Easing(EasingMode.Linear, n, start, false, 0f, 0f);
                return true;
            }
            if (t.StartsWith("cubic-bezier(", StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal))
            {
                var args = t.Substring(13, t.Length - 14).Split(',');
                if (args.Length == 4
                    && float.TryParse(args[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y1)
                    && float.TryParse(args[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y2))
                {
                    e = new Easing(EasingMode.Linear, 0, false, true, y1, y2);
                    return true;
                }
            }
            return false;
        }

        /// <summary>The eased value for a linear progress 0..1.</summary>
        public float Progress(float p)
        {
            p = Mathf.Clamp01(p);
            if (Steps > 0) return Mathf.Min(1f, Mathf.Floor(p * Steps + (JumpStart ? 1f : 0f)) / Steps);
            if (Bezier) { var q = 1f - p; return 3f * q * q * p * Y1 + 3f * q * p * p * Y2 + p * p * p; }
            return Mode switch
            {
                EasingMode.Linear => p,
                EasingMode.EaseIn => p * p,
                EasingMode.EaseOut => 1f - (1f - p) * (1f - p),
                _ => p * p * (3f - 2f * p),
            };
        }

        /// <summary>The same as an expression over a progress fragment <paramref name="p"/> (0..1).</summary>
        public string Expr(string p)
        {
            if (Steps > 0) return "min(1,floor(" + p + "*" + Steps + (JumpStart ? "+1" : string.Empty) + ")/" + Steps + ")";
            if (Bezier) return "(3*(1-" + p + ")^2*" + p + "*" + Tweens.F(Y1) + "+3*(1-" + p + ")*" + p + "^2*" + Tweens.F(Y2) + "+" + p + "^3)";
            return Mode switch
            {
                EasingMode.Linear => p,
                EasingMode.EaseIn => "(" + p + ")^2",
                EasingMode.EaseOut => "(1-(1-" + p + ")^2)",
                _ => "smoothstep(0,1," + p + ")",
            };
        }
    }

    internal sealed class Tween
    {
        public Snap From;
        public Snap To;
        public float Start;
        public float Duration;
        public Easing Ease;
        /// <summary>The eased progress as an expression fragment, 0..1.</summary>
        public string P = string.Empty;

        public bool Active(float now) => now < Start + Duration;

        private float Progress(float now)
        {
            return Ease.Progress((now - Start) / Mathf.Max(0.001f, Duration));
        }

        /// <summary>Where the element is right now, for a tween that interrupts this one.</summary>
        public Snap At(float now)
        {
            var k = Progress(now);
            return new Snap
            {
                Rect = new Rect(Mathf.Lerp(From.Rect.x, To.Rect.x, k), Mathf.Lerp(From.Rect.y, To.Rect.y, k), Mathf.Lerp(From.Rect.width, To.Rect.width, k), Mathf.Lerp(From.Rect.height, To.Rect.height, k)),
                Opacity = Mathf.Lerp(From.Opacity, To.Opacity, k),
                Rotate = Mathf.Lerp(From.Rotate, To.Rotate, k),
                Translate = Vector2.Lerp(From.Translate, To.Translate, k),
                Scale = Vector2.Lerp(From.Scale, To.Scale, k),
            };
        }

        /// <summary>A number that runs from a to b, plus a constant offset: a plain number when a == b.</summary>
        public string Lerp(float a, float b, float offset = 0f)
        {
            if (Mathf.Abs(a - b) < 0.01f)
                return F(a + offset);
            return "=" + F(a + offset) + "+(" + F(b - a) + ")*" + P;
        }

        /// <summary>The displacement still to travel from the end state: (a - b) * (1 - P).</summary>
        public string Remaining(float a, float b)
        {
            if (Mathf.Abs(a - b) < 0.01f)
                return "0";
            return "=(" + F(a - b) + ")*(1-" + P + ")";
        }

        public static string Eased(Easing ease, float start, float duration)
        {
            var p = "clamp((t-" + F(start) + ")/" + F(Mathf.Max(0.001f, duration)) + ",0,1)";
            return ease.Expr(p);
        }
    }

    /// <summary>
    /// Set by a keyframe runner before it writes a frame: the segment's duration and
    /// easing apply to that element's next change instead of its CSS transition.
    /// </summary>
    internal static readonly Dictionary<VisualElement, (float dur, Easing ease)> Override = new();

    private readonly Dictionary<VisualElement, Snap> _shown = new();
    private readonly Dictionary<VisualElement, Tween> _live = new();
    private readonly List<VisualElement> _scratch = new();

    public bool Any => _live.Count > 0;
    public int Count => _live.Count;

    /// <summary>After layout, before emitting: start a tween for every element that changed and has a transition.</summary>
    public void Diff(VisualElement root, HtmlRenderer.Result built, float now)
    {
        Walk(root, built, now);
    }

    private void Walk(VisualElement ve, HtmlRenderer.Result built, float now)
    {
        var cur = Snap.Of(ve);
        var isNew = !_shown.TryGetValue(ve, out var prev);
        if (isNew && built.StartingRules.Count > 0 && StartingSnap(ve, built, cur) is { } starting) { prev = starting; isNew = false; }
        if (!isNew && prev.Differs(cur))
        {
            var timing = Timing(ve, built, prev, cur);
            if (HtmlConfig.Diagnostics && !_shown.ContainsKey(ve))
                ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: starting-style on {ve.name}: opacity {prev.Opacity}->{cur.Opacity}, transition {timing.dur}s");
            if (timing.dur > 0.001f)
            {
                var from = _live.TryGetValue(ve, out var running) && running.Active(now) ? running.At(now) : prev;
                var start = now + timing.delay;
                _live[ve] = new Tween { From = from, To = cur, Start = start, Duration = timing.dur, Ease = timing.ease };
            }
            else
            {
                _live.Remove(ve);
            }
        }
        _shown[ve] = cur;
        foreach (var child in ve.Children())
            Walk(child, built, now);
    }

    public Tween? Of(VisualElement ve, float now)
    {
        if (!_live.TryGetValue(ve, out var t) || !t.Active(now))
            return null;
        // The vector mod's `t` restarts at zero every time a scene is applied, so the
        // expression is written relative to THIS emission: a tween already under way has a
        // negative start and picks up mid-flight.
        t.P = Tween.Eased(t.Ease, t.Start - now, t.Duration);
        return t;
    }

    /// <summary>Drop finished tweens. True when any ended, so the scene can be re-emitted with plain numbers.</summary>
    public bool Expire(float now)
    {
        if (_live.Count == 0)
            return false;
        _scratch.Clear();
        foreach (var kv in _live)
            if (!kv.Value.Active(now))
                _scratch.Add(kv.Key);
        foreach (var ve in _scratch)
            _live.Remove(ve);
        return _scratch.Count > 0;
    }

    /// <summary>
    /// @starting-style for an element shown for the first time: its snapshot with the
    /// starting declarations applied (opacity, colours, px width/height, transform),
    /// so the usual transition runs from there. Null when no starting rule matches.
    /// </summary>
    private static Snap? StartingSnap(VisualElement ve, HtmlRenderer.Result built, in Snap cur)
    {
        if (!built.NodeOf.TryGetValue(ve, out var node)) return null;
        var s = cur;
        var any = false;
        foreach (var d in HtmlRenderer.Cascaded(node, built.StartingRules))
        {
            var v = d.Value.Trim();
            switch (d.Name)
            {
                case "opacity": s.Opacity = StyleApplier.Num(v); any = true; break;
                case "background-color": case "background": if (StyleApplier.TryColor(v, out var bg)) { s.Bg = bg; any = true; } break;
                case "color": if (StyleApplier.TryColor(v, out var fg)) { s.Fg = fg; any = true; } break;
                case "width": if (!v.EndsWith("%", StringComparison.Ordinal)) { s.Rect.width = StyleApplier.Num(v); any = true; } break;
                case "height": if (!v.EndsWith("%", StringComparison.Ordinal)) { s.Rect.height = StyleApplier.Num(v); any = true; } break;
                case "translate": { var p = v.Split(' ', StringSplitOptions.RemoveEmptyEntries); s.Translate = new Vector2(StyleApplier.Num(p[0]), p.Length > 1 ? StyleApplier.Num(p[1]) : 0f); any = true; break; }
                case "scale": { var p = v.Split(' ', StringSplitOptions.RemoveEmptyEntries); s.Scale = new Vector2(StyleApplier.Num(p[0]), StyleApplier.Num(p.Length > 1 ? p[1] : p[0])); any = true; break; }
                case "rotate": s.Rotate = Degrees(v); any = true; break;
                case "transform":
                    if (v == "none") { s.Translate = Vector2.zero; s.Scale = Vector2.one; s.Rotate = 0f; any = true; break; }
                    foreach (var (name, a) in StyleApplier.Functions(v))
                    {
                        switch (name)
                        {
                            case "translate": s.Translate = new Vector2(StyleApplier.Num(a[0]), a.Length > 1 ? StyleApplier.Num(a[1]) : 0f); break;
                            case "translatex": s.Translate.x = StyleApplier.Num(a[0]); break;
                            case "translatey": s.Translate.y = StyleApplier.Num(a[0]); break;
                            case "scale": s.Scale = new Vector2(StyleApplier.Num(a[0]), StyleApplier.Num(a.Length > 1 ? a[1] : a[0])); break;
                            case "scalex": s.Scale.x = StyleApplier.Num(a[0]); break;
                            case "scaley": s.Scale.y = StyleApplier.Num(a[0]); break;
                            case "rotate": case "rotatez": s.Rotate = Degrees(a[0]); break;
                        }
                        any = true;
                    }
                    break;
            }
        }
        return any ? s : null;
    }

    private static float Degrees(string v)
    {
        v = v.Trim().ToLowerInvariant();
        if (v.EndsWith("turn", StringComparison.Ordinal)) return StyleApplier.Num(v.Substring(0, v.Length - 4)) * 360f;
        if (v.EndsWith("rad", StringComparison.Ordinal)) return StyleApplier.Num(v.Substring(0, v.Length - 3)) * Mathf.Rad2Deg;
        if (v.EndsWith("grad", StringComparison.Ordinal)) return StyleApplier.Num(v.Substring(0, v.Length - 4)) * 0.9f;
        return StyleApplier.Num(v.EndsWith("deg", StringComparison.Ordinal) ? v.Substring(0, v.Length - 3) : v);
    }

    private static readonly string[] LayoutProps = { "width", "height", "top", "left", "right", "bottom", "margin", "padding", "flex", "min-width", "min-height", "max-width", "max-height", "inset" };

    private static (float dur, Easing ease, float delay) Timing(VisualElement ve, HtmlRenderer.Result built, in Snap prev, in Snap cur)
    {
        if (Override.TryGetValue(ve, out var o))
            return (o.dur, o.ease, 0f);

        if (!built.CssOf(ve).TryGetValue("transition", out var css))
            return (0f, Easing.Default, 0f);

        var layout = prev.LayoutDiffers(cur);
        var opacity = prev.OpacityDiffers(cur);
        var transform = prev.TransformDiffers(cur);
        var colour = prev.ColourDiffers(cur);
        foreach (var item in css.Split(','))
        {
            var parts = CssParser.SplitTopLevel(item.Trim(), ' ').FindAll(p => p.Length > 0).ToArray();
            if (parts.Length < 2) continue;
            var prop = parts[0].ToLowerInvariant();
            var matches = prop == "all"
                          || (colour && (prop == "color" || prop == "background-color" || prop == "background" || prop == "border-color"))
                          || (opacity && prop == "opacity")
                          || (transform && (prop == "transform" || prop == "rotate" || prop == "translate" || prop == "scale"))
                          || (layout && Array.Exists(LayoutProps, p => prop.StartsWith(p, StringComparison.Ordinal)));
            if (!matches) continue;
            var dur = Seconds(parts[1]);
            var ease = Easing.Default;
            var delay = 0f;
            for (var i = 2; i < parts.Length; i++)
            {
                if (Easing.TryParse(parts[i], out var e)) ease = e;
                else delay = Seconds(parts[i]);
            }
            return (dur, ease, delay);
        }
        return (0f, Easing.Default, 0f);
    }

    private static float Seconds(string v)
    {
        v = v.Trim();
        if (v.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) return StyleApplier.Num(v.Substring(0, v.Length - 2)) / 1000f;
        if (v.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return StyleApplier.Num(v.Substring(0, v.Length - 1));
        return StyleApplier.Num(v);
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

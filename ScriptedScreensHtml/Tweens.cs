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
            };
        }

        public bool LayoutDiffers(in Snap o) => !Near(Rect.x, o.Rect.x) || !Near(Rect.y, o.Rect.y) || !Near(Rect.width, o.Rect.width) || !Near(Rect.height, o.Rect.height);
        public bool OpacityDiffers(in Snap o) => !Near(Opacity, o.Opacity);
        public bool TransformDiffers(in Snap o) => !Near(Rotate, o.Rotate) || !Near(Translate.x, o.Translate.x) || !Near(Translate.y, o.Translate.y) || !Near(Scale.x, o.Scale.x) || !Near(Scale.y, o.Scale.y);
        public bool Differs(in Snap o) => LayoutDiffers(o) || OpacityDiffers(o) || TransformDiffers(o);

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;
    }

    internal sealed class Tween
    {
        public Snap From;
        public Snap To;
        public float Start;
        public float Duration;
        public EasingMode Ease;
        /// <summary>The eased progress as an expression fragment, 0..1.</summary>
        public string P = string.Empty;

        public bool Active(float now) => now < Start + Duration;

        private float Progress(float now)
        {
            var p = Mathf.Clamp01((now - Start) / Mathf.Max(0.001f, Duration));
            return Ease switch
            {
                EasingMode.Linear => p,
                EasingMode.EaseIn => p * p,
                EasingMode.EaseOut => 1f - (1f - p) * (1f - p),
                _ => p * p * (3f - 2f * p),
            };
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

        public static string Eased(EasingMode ease, float start, float duration)
        {
            var p = "clamp((t-" + F(start) + ")/" + F(Mathf.Max(0.001f, duration)) + ",0,1)";
            return ease switch
            {
                EasingMode.Linear => p,
                EasingMode.EaseIn => "(" + p + ")^2",
                EasingMode.EaseOut => "(1-(1-" + p + ")^2)",
                _ => "smoothstep(0,1," + p + ")",
            };
        }
    }

    /// <summary>
    /// Set by a keyframe runner before it writes a frame: the segment's duration and
    /// easing apply to that element's next change instead of its CSS transition.
    /// </summary>
    internal static readonly Dictionary<VisualElement, (float dur, EasingMode ease)> Override = new();

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
        if (_shown.TryGetValue(ve, out var prev) && prev.Differs(cur))
        {
            var timing = Timing(ve, built, prev, cur);
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

    private static readonly string[] LayoutProps = { "width", "height", "top", "left", "right", "bottom", "margin", "padding", "flex", "min-width", "min-height", "max-width", "max-height", "inset" };

    private static (float dur, EasingMode ease, float delay) Timing(VisualElement ve, HtmlRenderer.Result built, in Snap prev, in Snap cur)
    {
        if (Override.TryGetValue(ve, out var o))
            return (o.dur, o.ease, 0f);

        if (!built.CssOf(ve).TryGetValue("transition", out var css))
            return (0f, EasingMode.Ease, 0f);

        var layout = prev.LayoutDiffers(cur);
        var opacity = prev.OpacityDiffers(cur);
        var transform = prev.TransformDiffers(cur);
        foreach (var item in css.Split(','))
        {
            var parts = item.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var prop = parts[0].ToLowerInvariant();
            var matches = prop == "all"
                          || (opacity && prop == "opacity")
                          || (transform && (prop == "transform" || prop == "rotate" || prop == "translate" || prop == "scale"))
                          || (layout && Array.Exists(LayoutProps, p => prop.StartsWith(p, StringComparison.Ordinal)));
            if (!matches) continue;
            var dur = Seconds(parts[1]);
            var ease = EasingMode.Ease;
            var delay = 0f;
            for (var i = 2; i < parts.Length; i++)
            {
                if (StyleApplier.TryEasing(parts[i], out var e)) ease = e;
                else delay = Seconds(parts[i]);
            }
            return (dur, ease, delay);
        }
        return (0f, EasingMode.Ease, 0f);
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

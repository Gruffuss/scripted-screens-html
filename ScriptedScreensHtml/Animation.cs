using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>The `animation` shorthand and longhands, per element.</summary>
internal sealed class AnimationSpec
{
    public string Name = string.Empty;
    public float Duration = 1f;
    public float Delay;
    public float Iterations = 1f;   // float.PositiveInfinity for infinite
    public EasingMode Easing = EasingMode.Ease;
    public bool Reverse;
    public bool Alternate;

    /// <summary>Parse one token of the shorthand into whichever slot it belongs to.</summary>
    public void ApplyToken(string t, ref int timesSeen)
    {
        var lower = t.ToLowerInvariant();
        if (IsTime(lower))
        {
            var secs = Seconds(lower);
            if (timesSeen++ == 0) Duration = secs; else Delay = secs;
            return;
        }
        switch (lower)
        {
            case "ease": Easing = EasingMode.Ease; return;
            case "linear": Easing = EasingMode.Linear; return;
            case "ease-in": Easing = EasingMode.EaseIn; return;
            case "ease-out": Easing = EasingMode.EaseOut; return;
            case "ease-in-out": Easing = EasingMode.EaseInOut; return;
            case "infinite": Iterations = float.PositiveInfinity; return;
            case "normal": Reverse = false; Alternate = false; return;
            case "reverse": Reverse = true; Alternate = false; return;
            case "alternate": Alternate = true; Reverse = false; return;
            case "alternate-reverse": Alternate = true; Reverse = true; return;
            case "none": case "forwards": case "backwards": case "both": return; // fill-mode: always forwards here
            case "running": case "paused": return;
        }
        if (float.TryParse(lower, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            Iterations = n;
            return;
        }
        Name = t;
    }

    private static bool IsTime(string t)
    {
        // "-2.3s" is a legal (negative) delay: it starts the animation part-way through.
        var i = t.Length > 0 && (t[0] == '-' || t[0] == '+') ? 1 : 0;
        return t.Length > i + 1 && (char.IsDigit(t[i]) || t[i] == '.') && t.EndsWith("s", StringComparison.Ordinal);
    }

    private static float Seconds(string t)
    {
        if (t.EndsWith("ms", StringComparison.Ordinal))
            return StyleApplier.Num(t.Substring(0, t.Length - 2)) / 1000f;
        return StyleApplier.Num(t.Substring(0, t.Length - 1));
    }
}

/// <summary>
/// Drives one @keyframes animation on one element by stepping at keyframe boundaries and
/// letting UI Toolkit's transition system interpolate the segment. The mod does a few
/// style writes per keyframe, not per frame. Fill mode is always "forwards": the last
/// frame stays applied when a finite animation ends.
/// </summary>
internal sealed class KeyframeRunner
{
    private readonly VisualElement _ve;
    private readonly CssKeyframes _frames;
    private readonly AnimationSpec _spec;
    private readonly Action<string>? _warn;
    private readonly float _start;
    private int _segment = -2;          // -2 = not started, -1 = initial frame applied instantly
    private int _iteration = -1;
    private bool _lastReversed;
    private bool _finished;
    private bool _snapped;              // start frame of the current iteration applied instantly, transition pending

    public bool Finished => _finished;
    /// <summary>Set when a frame was written; the surface clears it and re-emits.</summary>
    public bool Wrote;

    public KeyframeRunner(VisualElement ve, CssKeyframes frames, AnimationSpec spec, float now, Action<string>? warn)
    {
        _ve = ve;
        _frames = frames;
        _spec = spec;
        _warn = warn;
        _start = now + spec.Delay;
    }

    public void Update(float now)
    {
        if (_finished || _frames.Frames.Count == 0)
            return;

        var frames = _frames.Frames;

        // Before the delay elapses, hold the first frame.
        if (_segment == -2)
        {
            SetTransition(0f);
            ApplyFrame(_spec.Reverse ? frames[frames.Count - 1] : frames[0]);
            _segment = -1;
            return;
        }

        var elapsed = now - _start;
        if (elapsed < 0f)
            return;

        var duration = Mathf.Max(0.001f, _spec.Duration);
        var iteration = Mathf.FloorToInt(elapsed / duration);
        if (iteration >= _spec.Iterations)
        {
            // Land exactly on the final frame of the last iteration.
            var lastReversed = IsReversed(Mathf.Max(0, (int)_spec.Iterations - 1));
            SetTransition(0f);
            ApplyFrame(lastReversed ? frames[0] : frames[frames.Count - 1]);
            _finished = true;
            return;
        }

        var reversed = IsReversed(iteration);

        // A new iteration snaps to its start frame with no transition, and only on the
        // NEXT update runs the first segment. Both writes in one frame would collapse into
        // a single style change and the snap would be animated. Without this, a two-frame
        // infinite animation (from/to) has one segment and never re-fires after cycle one.
        if (iteration != _iteration)
        {
            _iteration = iteration;
            _segment = -1;
            SetTransition(0f);
            ApplyFrame(reversed ? frames[frames.Count - 1] : frames[0]);
            _snapped = true;
            return;
        }
        if (_snapped)
        {
            _snapped = false;
            _segment = -1;
        }

        var progress = (elapsed - iteration * duration) / duration * 100f;
        if (reversed)
            progress = 100f - progress;

        // Segment index i: frames[i].Percent <= progress < frames[i+1].Percent.
        var seg = 0;
        for (var i = 0; i < frames.Count - 1; i++)
        {
            if (progress >= frames[i].Percent)
                seg = i;
        }

        if (seg == _segment && reversed == _lastReversed)
            return;

        _segment = seg;
        _lastReversed = reversed;

        // Target is the end of the segment in the direction of travel; the transition
        // takes the segment's share of the duration.
        var from = frames[seg];
        var to = frames[Mathf.Min(seg + 1, frames.Count - 1)];
        var span = Mathf.Abs(to.Percent - from.Percent) / 100f * duration;
        var target = reversed ? from : to;

        SetTransition(span);
        ApplyFrame(target);
    }

    private bool IsReversed(int iteration)
    {
        var reversed = _spec.Reverse;
        if (_spec.Alternate && (iteration & 1) == 1)
            reversed = !reversed;
        return reversed;
    }

    private void SetTransition(float seconds)
    {
        // Vector mode: the segment becomes a tween (an expression over t), not a UI Toolkit transition.
        Tweens.Override[_ve] = (seconds, _spec.Easing);
    }

    private void ApplyFrame(CssKeyframe frame)
    {
        foreach (var d in frame.Declarations)
            StyleApplier.Apply(_ve, d, _warn);
        Wrote = true;
    }
}

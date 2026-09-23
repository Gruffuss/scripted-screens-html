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
    public Tweens.Easing Easing = Tweens.Easing.Default;
    public bool Reverse;
    public bool Alternate;
    /// <summary>animation-fill-mode: whether the first frame shows during the delay and the last frame stays after the end.</summary>
    public bool FillBackwards;
    public bool FillForwards;
    public bool Paused;
    /// <summary>animation-composition: 0 replace (a frame's value is the value), 1 add (the frame is composed onto the
    /// element's own transform and opacity), 2 accumulate (as add, but a scale adds its excess over 1 rather than multiplying).</summary>
    public int Composition;

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
        if (Tweens.Easing.TryParse(lower, out var e)) { Easing = e; return; }
        switch (lower)
        {
            case "infinite": Iterations = float.PositiveInfinity; return;
            case "normal": Reverse = false; Alternate = false; return;
            case "reverse": Reverse = true; Alternate = false; return;
            case "alternate": Alternate = true; Reverse = false; return;
            case "alternate-reverse": Alternate = true; Reverse = true; return;
            case "none": FillForwards = false; FillBackwards = false; return;
            case "forwards": FillForwards = true; return;
            case "backwards": FillBackwards = true; return;
            case "both": FillForwards = true; FillBackwards = true; return;
            case "running": Paused = false; return;
            case "paused": Paused = true; return;
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
    public AnimationSpec Spec => _spec;
    public VisualElement Element => _ve;
    /// <summary>Set when a frame was written; the surface clears it and re-emits.</summary>
    public bool Wrote;
    /// <summary>Set once the surface has put the element back to its cascade after an animation without forwards fill.</summary>
    public bool Restored;
    private float _pausedAt = float.NaN;
    private float _pauseShift;

    /// <summary>The element's cascade record: the emitter reads offset-* from it, so keyframes write them there.</summary>
    private readonly Dictionary<string, string>? _record;

    /// <summary>Told when this runner writes into the record, so the emitter knows the element changed.</summary>
    private readonly Action<VisualElement>? _touch;

    /// <summary>The element's own transform and opacity, for animation-composition: add. Copied here because the
    /// record is the dictionary the frames write into - after the first frame it holds the frame, not the base.</summary>
    private readonly string? _baseTransform, _baseOpacity;

    public KeyframeRunner(VisualElement ve, CssKeyframes frames, AnimationSpec spec, float now, Action<string>? warn, Dictionary<string, string>? record = null, Action<VisualElement>? touch = null)
    {
        _record = record;
        _touch = touch;
        if (spec.Composition != 0 && record != null)
        {
            record.TryGetValue("transform", out _baseTransform);
            record.TryGetValue("opacity", out _baseOpacity);
        }
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

        // animation-play-state: paused holds the clock where it is; resuming shifts the start.
        if (_spec.Paused)
        {
            if (float.IsNaN(_pausedAt)) _pausedAt = now;
            return;
        }
        if (!float.IsNaN(_pausedAt)) { _pauseShift += now - _pausedAt; _pausedAt = float.NaN; }

        var frames = _frames.Frames;

        // Before the delay elapses, hold the first frame (fill-mode backwards/both); with
        // no backwards fill the element keeps its own style until the delay ends.
        if (_segment == -2)
        {
            if (_spec.FillBackwards || _spec.Delay <= 0f)
            {
                SetTransition(0f);
                ApplyFrame(_spec.Reverse ? frames[frames.Count - 1] : frames[0]);
            }
            _segment = -1;
            return;
        }

        var elapsed = now - _start - _pauseShift;
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

    /// <summary>
    /// The clock at which this runner next has something to do - it writes at keyframe boundaries
    /// and does nothing in between, so the surface has no reason to run a whole page frame for it
    /// until then. Existing was previously enough: `_animations.Count > 0` held the idle-frame gate
    /// open for the life of any page carrying one animation this cannot compile, so such a page paid
    /// the full pipeline at display rate forever while producing an identical scene.
    ///
    /// Errs toward <paramref name="now"/> - "due immediately" - for every case that is not a plain
    /// forward run between two frames. A needless frame costs a frame; a missed one stops an
    /// animation, which is the failure that reads as a broken mod.
    /// </summary>
    public float NextDue(float now)
    {
        if (_finished) return float.MaxValue;
        if (_spec.Paused || _segment < 0 || _snapped || _frames.Frames.Count < 2) return now;

        var frames = _frames.Frames;
        var duration = Mathf.Max(0.001f, _spec.Duration);
        var elapsed = now - _start - _pauseShift;
        if (elapsed < 0f) return _start + _pauseShift;            // still in the delay

        var iteration = Mathf.FloorToInt(elapsed / duration);
        if (iteration >= _spec.Iterations) return now;            // the landing frame is owed
        // A new iteration snaps to its start frame, which is a write owed now. _segment still
        // describes the PREVIOUS iteration, so projecting from it lands a whole period late and the
        // gate sleeps through the start of every cycle - four writes in ten seconds instead of twenty.
        if (iteration != _iteration) return now;
        if (IsReversed(iteration)) return now;                    // reverse walks the list backwards; not worth the arithmetic
        if (_segment + 1 >= frames.Count) return now;

        // Forward: the next write happens when progress reaches the following frame's percent.
        var at = _start + _pauseShift + iteration * duration + frames[_segment + 1].Percent / 100f * duration;
        return at > now ? at : now;
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
        lock (Tweens.Shared) Tweens.Override[_ve] = (seconds, _spec.Easing);
    }

    private void ApplyFrame(CssKeyframe frame)
    {
        foreach (var raw in frame.Declarations)
        {
            var d = raw;
            // animation-composition: add / accumulate. Composed NUMERICALLY and applied as one declaration: the
            // applier's transform scan sets translate/rotate/scale per function, last one wins, so
            // "translateX(20px) translateX(0)" would apply as 0. The composed value has to be applied, not only
            // recorded - the emitter reads the transform off the resolved style, not off the record, which is
            // where the first attempt at this stopped.
            if (_spec.Composition != 0 && d.Name == "transform" && _baseTransform != null)
                d = new CssDeclaration("transform", Composed(_baseTransform, d.Value, _spec.Composition == 2), d.Important);
            else if (_spec.Composition != 0 && d.Name == "opacity" && _baseOpacity != null)
                d = new CssDeclaration("opacity", Mathf.Clamp01(StyleApplier.Num(_baseOpacity) + StyleApplier.Num(d.Value)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), d.Important);
            StyleApplier.Apply(_ve, d, _warn);
            // the frame's values also go to the record the emitter reads (motion paths, gradients, masks...)
            if (_record != null) { _record[d.Name] = d.Value.Trim(); _touch?.Invoke(_ve); }
        }
        Wrote = true;
    }

    /// <summary>A CSS transform list read as translate / rotate / scale: what the applier and the emitter keep of one.</summary>
    internal static (float tx, float ty, float r, float sx, float sy) Parts(string transform)
    {
        var tx = 0f; var ty = 0f; var r = 0f; var sx = 1f; var sy = 1f;
        if (transform.Trim() == "none") return (tx, ty, r, sx, sy);
        foreach (var (name, args) in StyleApplier.Functions(transform))
        {
            float A(int i) => args.Length > i ? StyleApplier.Num(args[i]) : 0f;
            switch (name)
            {
                case "translate": tx = A(0); ty = args.Length > 1 ? A(1) : 0f; break;
                case "translatex": tx = A(0); break;
                case "translatey": ty = A(0); break;
                case "scale": sx = A(0); sy = args.Length > 1 ? A(1) : A(0); break;
                case "scalex": sx = A(0); break;
                case "scaley": sy = A(0); break;
                case "rotate": case "rotatez": r = A(0); break;
            }
        }
        return (tx, ty, r, sx, sy);
    }

    /// <summary>The frame's transform composed onto the base one, as CSS `add` (translations and turns add,
    /// scales multiply) or `accumulate` (a scale adds its excess over 1).</summary>
    internal static string Composed(string baseTransform, string frame, bool accumulate)
    {
        var b = Parts(baseTransform);
        var f = Parts(frame);
        var sx = accumulate ? b.sx + f.sx - 1f : b.sx * f.sx;
        var sy = accumulate ? b.sy + f.sy - 1f : b.sy * f.sy;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return "translate(" + (b.tx + f.tx).ToString("0.###", inv) + "px," + (b.ty + f.ty).ToString("0.###", inv) + "px) rotate("
               + (b.r + f.r).ToString("0.###", inv) + "deg) scale(" + sx.ToString("0.###", inv) + "," + sy.ToString("0.###", inv) + ")";
    }
}

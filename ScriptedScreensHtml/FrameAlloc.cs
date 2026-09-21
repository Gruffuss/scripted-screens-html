using System;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace ScriptedScreensHtml;

/// <summary>
/// How many managed bytes the whole process allocated in a frame, honestly.
///
/// The obvious API does not work here: Mono answers 0 for
/// <c>GC.GetAllocatedBytesForCurrentThread</c>, and the fallback this replaces -
/// <c>GC.GetTotalMemory(false)</c> - is the heap currently *in use*, not a running total. It falls
/// at every collection, so a window containing one reads negative, and every other thread's
/// allocation lands in whichever phase happened to be timing. Every per-phase KB figure this mod
/// printed was noise, and several conclusions were drawn from those figures before anyone checked.
///
/// Unity's own profiler counters do not go through that API. "GC Allocated In Frame" is cumulative
/// for the frame, resets each frame, and is fed by the runtime rather than by managed code.
///
/// **Whether it reports in a non-development player is the open question** - some counters are
/// development-build only. So nothing here assumes it works: <see cref="Valid"/> is false when the
/// recorder did not start or has never returned a sample, and callers must say "unavailable"
/// rather than print a zero that reads like a measurement.
/// </summary>
internal static class FrameAlloc
{
    private static ProfilerRecorder _recorder;
    private static bool _tried;
    private static bool _sawSample;

    /// <summary>Whether the counter is actually reporting. False means: do not print a number.</summary>
    internal static bool Valid
    {
        get
        {
            Start();
            if (!_recorder.Valid) return false;
            if (!_sawSample && _recorder.LastValue > 0) _sawSample = true;
            return _sawSample;
        }
    }

    /// <summary>Managed bytes allocated process-wide during the last completed frame, or -1.</summary>
    internal static long LastFrameBytes => Valid ? _recorder.LastValue : -1L;

    private static void Start()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            _recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo("html: frame allocation counter unavailable: " + ex.Message);
        }
    }

    /// <summary>
    /// Retried from the plugin's Update: the first attempt runs at mod load, long before the
    /// profiler's own systems are up, and "did not start" then may only mean "not yet".
    /// </summary>
    internal static void Retry()
    {
        if (_recorder.Valid || _absent) return;
        _tried = false;
        Start();
    }

    /// <summary>
    /// Settled in game 2026-09-21 by enumerating what the player actually serves: 42 counters, and
    /// the memory ones are all *used* or *reserved* - heap size, not a cumulative total. There is no
    /// per-frame allocation counter outside a development build, so in-game attribution is not a
    /// tooling problem to solve; a behavioural A/B is the only instrument left.
    /// </summary>
    private static bool _absent;

    /// <summary>
    /// What this player actually serves. Guessing a counter name and reading "did not start" as
    /// "the runtime cannot do this" is how the per-thread API got written off; ask instead.
    /// </summary>
    internal static void ListAvailable()
    {
        try
        {
            var handles = new System.Collections.Generic.List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var names = new System.Collections.Generic.List<string>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.Name != null && (d.Name.IndexOf("Alloc", StringComparison.OrdinalIgnoreCase) >= 0
                                       || d.Name.IndexOf("GC", StringComparison.Ordinal) >= 0
                                       || d.Name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0))
                    names.Add(d.Name);
            }
            _absent = !names.Contains("GC Allocated In Frame");
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: {handles.Count} profiler counters in this player; memory/alloc ones: "
                + (names.Count > 0 ? string.Join(" | ", names) : "none"));
            if (_absent)
                ScriptedScreensHtmlPlugin.Log?.LogInfo(
                    "html: no per-frame allocation counter in this player (all memory counters are used/reserved, "
                    + "i.e. heap size). Allocation cannot be attributed in game; compare behaviour instead.");
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo("html: could not enumerate profiler counters: " + ex.Message);
        }
    }

    /// <summary>
    /// The allocation rate, from the one number Mono will answer: the managed heap in use, sampled
    /// every frame, with the positive deltas summed.
    ///
    /// The note above rejects GC.GetTotalMemory because it falls at a collection and a window
    /// containing one reads negative. That is true of subtracting the endpoints, and not of this:
    /// between collections the heap only grows, so each frame's rise is allocation, and a fall is a
    /// collection and is dropped. What it misses is whatever was allocated during the frames a
    /// collection landed in - a handful of frames a minute, so a few per cent low.
    ///
    /// Why it is worth having: counting collections is not comparable between two runs whose live
    /// heaps differ, because the heap size sets the threshold that triggers one. Two engines
    /// measured that way came out 800 MB apart in heap and the collection counts said the opposite
    /// of every other measurement. A rate does not care how big the heap is.
    /// </summary>
    private static long _lastHeap = -1L;
    private static long _grown;
    private static int _falls;

    /// <summary>Once per frame, from the plugin.</summary>
    internal static void SampleHeap()
    {
        var now = GC.GetTotalMemory(false);
        if (_lastHeap >= 0)
        {
            var d = now - _lastHeap;
            if (d > 0) _grown += d; else if (d < 0) _falls++;
        }
        _lastHeap = now;
    }

    /// <summary>MB per second since the last call, and how many collections were seen in that time.</summary>
    internal static (double mb, int collections) TakeRate(double seconds)
    {
        var mb = seconds > 0 ? _grown / 1048576.0 / seconds : 0.0;
        var falls = _falls;
        _grown = 0; _falls = 0;
        return (mb, falls);
    }

    /// <summary>Said once, so a reading that is missing says why instead of looking like zero.</summary>
    internal static void Report()
    {
        Start();
        ListAvailable();
        if (!_recorder.Valid)
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo(
                "html: \"GC Allocated In Frame\" did not start at mod load"
                + (_absent ? "; this player does not serve it." : "; retrying each frame."));
            return;
        }
        ScriptedScreensHtmlPlugin.Log?.LogInfo(
            $"html: frame allocation counter started; first sample {_recorder.LastValue} B"
            + (_recorder.LastValue > 0 ? "." : " (0 so far - if it stays 0 this player does not serve the counter)."));
    }
}

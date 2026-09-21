using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Bench;

/// <summary>
/// One page, laid out once, then emitted in a loop: bytes allocated and microseconds per emit,
/// broken into layout / capture / emit / split. In game Mono answers 0 for per-thread allocation,
/// so this is the only place the emitter's garbage can be attributed to a phase at all.
///
///   dotnet run -c Release -- [page.html|page.lua] [iterations]
///
/// A .lua argument has its src = [[ ... ]] page pulled out, so the game's own example files are
/// the input. Times do not transfer to Mono; allocation does.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : "../ScriptedScreensHtml/examples/07-game.lua";
        var iterations = args.Length > 1 ? int.Parse(args[1]) : 300;
        if (!File.Exists(path)) { Console.Error.WriteLine($"no such file: {path}"); return 1; }

        if (Check.Run() > 0) return 1;

        var text = File.ReadAllText(path);
        var html = path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? PageOf(text) : text;
        if (html == null) { Console.Error.WriteLine("no [[ <html... ]] page in that file"); return 1; }
        Console.WriteLine($"{Path.GetFileName(path)}: {html.Length} chars of html");

        // The surface's own build path: cascade, tree, panel, then the layout passes it attaches.
        var face = FontLibrary.Default();
        ResolvedStyle.DefaultFace = face;
        HtmlRenderer.SurfaceAspect = 1f;
        var built = HtmlRenderer.Build(html, face);
        foreach (var w in built.Warnings) Console.WriteLine("  warn: " + w);
        var root = built.Root;
        var panel = new Panel(root);
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);

        var size = new Vector2(built.ViewportWidth, built.ViewportWidth);
        var boxes = new Dictionary<VisualElement, OffThread.Box>();
        var scratch = new List<VisualElement>();
        var tweens = new Tweens();
        var slots = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);

        OffThread.MainThreadId = Environment.CurrentManagedThreadId;
        OffThread.Boxes = boxes;
        OffThread.Job = OffThread.Globals.Take();

        var js = Environment.GetEnvironmentVariable("BENCH_JS") == "1";
        if (js) Js.Start(built, size);

        // Warm up: first pass builds every cache the steady state then reuses.
        for (var i = 0; i < 5; i++) Once(panel, root, built, boxes, scratch, tweens, slots, size, i * 0.016f);

        var phases = new (string name, long bytes, long ticks)[5];
        var names = new[] { "layout", "capture", "emit", "split", "script" };
        long totalBytes = 0;
        var sw = Stopwatch.StartNew();
        var skipped = 0;
        for (var i = 0; i < iterations; i++)
        {
            // the surface's own gate: a frame nobody needs is not run at all
            if (SkipIdle && !Js.Wants(1f + i * 0.016f)) { skipped++; continue; }
            var before = GC.GetAllocatedBytesForCurrentThread();
            Keep = i == iterations - 1;
            var p = Once(panel, root, built, boxes, scratch, tweens, slots, size, 1f + i * 0.016f);
            totalBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            for (var k = 0; k < 5; k++) { phases[k].bytes += p[k].bytes; phases[k].ticks += p[k].ticks; }
        }
        sw.Stop();

        Console.WriteLine($"{iterations} emits, {built.ById.Count} ids, scene {LastSceneLength} chars");
        if (SkipIdle) Console.WriteLine($"  gate    {skipped} of {iterations} frames skipped: the page wanted none ({100.0 * skipped / iterations:N0} %)");
        {
            int dirty = 0, kept = 0, total = 0;
            foreach (var b in boxes.Values) { total++; if (b.SubtreeChanged) dirty++; if (b.CacheBody != null) kept++; }
            Console.WriteLine($"  boxes   {total} total, {dirty} still marked changed after the last emit, {kept} holding cached text");
        }
        Console.WriteLine($"  why     changed {VectorEmitter.WhyChanged}, no cache {VectorEmitter.WhyNoCache}, tween {VectorEmitter.WhyTween}, epoch {VectorEmitter.WhyEpoch}, moved {VectorEmitter.WhyMoved}, depth {VectorEmitter.WhyDepth}");
        Console.WriteLine($"  cache   {VectorEmitter.LastReused,5} subtrees reused, {VectorEmitter.LastRebuilt,4} elements rebuilt on the last frame");
        if (Verify) Console.WriteLine(Mismatches == 0 ? $"  PASS  cached and fresh emissions identical over {iterations} frames" : $"  FAIL  {Mismatches} of {iterations} frames differed");
        if (Frames > 0)
            Console.WriteLine($"  slots   {TotalSlots / Frames,10:N0} per frame, {ChangedSlots / (double)Math.Max(1, Frames),7:N1} changed ({100.0 * ChangedSlots / Math.Max(1, TotalSlots),4:N1} %)");
        if (Frames > 0)
            Console.WriteLine($"  idle    {IdleFrames} of {Frames} frames produced a scene identical to the one before ({100.0 * IdleFrames / Frames:N0} %)");
        Console.WriteLine($"  total   {totalBytes / (double)iterations,10:N0} B   {sw.Elapsed.TotalMilliseconds / iterations,7:F3} ms per display frame");
        for (var k = 0; k < 5; k++)
            Console.WriteLine($"  {names[k],-7} {phases[k].bytes / (double)iterations,10:N0} B   {phases[k].ticks / (double)Stopwatch.Frequency * 1000.0 / iterations,7:F3} ms");
        if (Js.Steps > 0)
            Console.WriteLine($"  of script: waiting for the worker {Js.WaitBytes / (double)Js.Steps,8:N0} B, applying its writes {Js.ApplyBytes / (double)Js.Steps,8:N0} B");
        File.WriteAllText("scene.txt", LastScene);
        if (js) Js.Run(built, panel, size, iterations);
        if (Environment.GetEnvironmentVariable("BENCH_STYLE") == "1")
        {
            // The main-thread half of a page frame is applying the writes the script made, and the
            // bench charges it all to "script" without saying what inside it costs. These are the
            // values a page animating from a clock really produces.
            var ve = new VisualElement();
            var cases = new[]
            {
                new CssDeclaration("width", "81.93%"),
                new CssDeclaration("height", "12px"),
                new CssDeclaration("opacity", "0.819"),
                new CssDeclaration("transform", "translateY(-14.2px)"),
                new CssDeclaration("background-color", "#8b1a1a"),
                new CssDeclaration("left", "205.5px"),
            };
            Action<string> warn = _ => { };
            for (var i = 0; i < 2000; i++) foreach (var c in cases) StyleApplier.Apply(ve, c, warn);
            foreach (var c in cases)
            {
                const int n = 20000;
                StyleApplier.Apply(ve, c, warn);
                var b = GC.GetAllocatedBytesForCurrentThread();
                var t = Stopwatch.GetTimestamp();
                for (var i = 0; i < n; i++) StyleApplier.Apply(ve, c, warn);
                var bytes = (GC.GetAllocatedBytesForCurrentThread() - b) / (double)n;
                var us = (Stopwatch.GetTimestamp() - t) / (double)Stopwatch.Frequency * 1e6 / n;
                Console.WriteLine($"  style   {c.Name,-18} {c.Value,-18} {bytes,7:N0} B  {us,6:F2} us");
            }
        }

        if (Environment.GetEnvironmentVariable("BENCH_PARSE") == "1")
        {
            // what it costs merely to read the markup back, with no cascade and no tree: the floor
            // under an innerHTML update, morphed or not
            HtmlParser.Parse(html);
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            var sw2 = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++) HtmlParser.Parse(html);
            sw2.Stop();
            Console.WriteLine($"  parse   {(GC.GetAllocatedBytesForCurrentThread() - b0) / 20.0,10:N0} B   {sw2.Elapsed.TotalMilliseconds / 20,7:F3} ms for {html.Length} chars");
        }
        return 0;
    }

    private static int LastSceneLength;
    private static readonly bool Verify = Environment.GetEnvironmentVariable("BENCH_VERIFY") == "1";
    private static readonly bool SkipIdle = Environment.GetEnvironmentVariable("BENCH_GATE") == "1";
    internal static int Mismatches;
    private const char Newline = (char)10;
    private static readonly Dictionary<string, SceneSlots.Value> Previous = new(StringComparer.Ordinal);
    private static long TotalSlots, ChangedSlots, Frames, IdleFrames;
    internal static string LastScene = string.Empty;
    /// <summary>Set for the last iteration only, so the dump still has a scene to write.</summary>
    internal static bool Keep;


    private static (long bytes, long ticks)[] Once(Panel panel, VisualElement root, HtmlRenderer.Result built,
        Dictionary<VisualElement, OffThread.Box> boxes, List<VisualElement> scratch, Tweens tweens,
        Dictionary<string, SceneSlots.Value> slots, Vector2 size, float now)
    {
        var p = new (long bytes, long ticks)[5];
        long b0, t0;
        // the page's own frame first, as in the game: without it the emitter is measured against a
        // page that never moves, which is not the case anyone cares about. Charged as its own phase
        // so the script's share carries the SAME denominator as the emitter's - mixing a per-script-
        // frame figure with a per-display-frame one is how a fictional total gets published.
        b0 = GC.GetAllocatedBytesForCurrentThread(); t0 = Stopwatch.GetTimestamp();
        Js.Step(built, now);
        p[4] = (GC.GetAllocatedBytesForCurrentThread() - b0, Stopwatch.GetTimestamp() - t0);

        b0 = GC.GetAllocatedBytesForCurrentThread(); t0 = Stopwatch.GetTimestamp();
        panel.Layout(size.x, size.y);
        p[0] = (GC.GetAllocatedBytesForCurrentThread() - b0, Stopwatch.GetTimestamp() - t0);

        b0 = GC.GetAllocatedBytesForCurrentThread(); t0 = Stopwatch.GetTimestamp();
        OffThread.Capture(root, built, boxes, scratch);
        p[1] = (GC.GetAllocatedBytesForCurrentThread() - b0, Stopwatch.GetTimestamp() - t0);

        // As on a worker (HtmlSurface.Translate): font questions are deferred rather than asked
        // of TextMeshPro, whose lookups are native ECalls and throw outside the player.
        OffThread.Active = true;
        b0 = GC.GetAllocatedBytesForCurrentThread(); t0 = Stopwatch.GetTimestamp();
        tweens.Diff(root, built, now);
        var output = VectorEmitter.Emit(built, root, size.x, size.y, tweens, now, null);
        p[2] = (GC.GetAllocatedBytesForCurrentThread() - b0, Stopwatch.GetTimestamp() - t0);
        OffThread.Active = false;

        b0 = GC.GetAllocatedBytesForCurrentThread(); t0 = Stopwatch.GetTimestamp();
        SceneSlots.Split(output.Chars, output.Length, slots, "L");
        // how much of a frame's scene is the same as the last one's: the ceiling on any cache
        var changedBefore = ChangedSlots;
        foreach (var kv in slots)
        {
            TotalSlots++;
            if (!Previous.TryGetValue(kv.Key, out var was) || !was.Equals(kv.Value)) ChangedSlots++;
            Previous[kv.Key] = kv.Value;
        }
        Frames++;
        if (ChangedSlots == changedBefore) IdleFrames++;   // this frame's scene is the last one's, value for value
        p[3] = (GC.GetAllocatedBytesForCurrentThread() - b0, Stopwatch.GetTimestamp() - t0);

        LastSceneLength = output.Length;
        if (Verify)
        {
            // the same frame emitted from scratch: anything the cache reused whose inputs it cannot
            // see shows up here, and nowhere else until a player sees a stale console
            var cached = new string(output.Chars, 0, output.Length);
            OffThread.Active = true;   // as on a worker: no font questions may reach the engine
            VectorEmitter.NoCache = true;
            var fresh = VectorEmitter.Emit(built, root, size.x, size.y, tweens, now, null);
            var plain = new string(fresh.Chars, 0, fresh.Length);
            VectorEmitter.NoCache = false;
            OffThread.Active = false;
            if (!string.Equals(cached, plain, StringComparison.Ordinal))
            {
                Mismatches++;
                if (Mismatches == 1)
                {
                    var a = cached.Split(Newline);
                    var b = plain.Split(Newline);
                    var k = 0;
                    while (k < a.Length && k < b.Length && a[k] == b[k]) k++;
                    Console.WriteLine($"  CACHE MISMATCH at line {k}");
                    Console.WriteLine($"    cached: {(k < a.Length ? a[k] : "(end)")}");
                    Console.WriteLine($"    fresh:  {(k < b.Length ? b[k] : "(end)")}");
                }
            }
        }
        // Only the frame whose scene is actually written out pays for a string - the same trap the
        // game had at HtmlSurface.cs:1076, and the reason this file's phase table never summed to
        // its own total: the residue was always 2 x the scene's characters.
        if (Keep) LastScene = output.Scene;
        return p;
    }

    /// <summary>The page out of a chip file: the first long-bracket string that starts with a tag.</summary>
    private static string? PageOf(string lua)
    {
        var m = Regex.Match(lua, @"\[(=*)\[\s*<(.*?)\]\1\]", RegexOptions.Singleline);
        return m.Success ? "<" + m.Groups[2].Value : null;
    }
}

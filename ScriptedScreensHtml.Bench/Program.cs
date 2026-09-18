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

        var phases = new (string name, long bytes, long ticks)[4];
        var names = new[] { "layout", "capture", "emit", "split" };
        long totalBytes = 0;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var p = Once(panel, root, built, boxes, scratch, tweens, slots, size, 1f + i * 0.016f);
            totalBytes += GC.GetAllocatedBytesForCurrentThread() - before;
            for (var k = 0; k < 4; k++) { phases[k].bytes += p[k].bytes; phases[k].ticks += p[k].ticks; }
        }
        sw.Stop();

        Console.WriteLine($"{iterations} emits, {built.ById.Count} ids, scene {LastSceneLength} chars");
        Console.WriteLine($"  total   {totalBytes / (double)iterations,10:N0} B   {sw.Elapsed.TotalMilliseconds / iterations,7:F3} ms per emit");
        for (var k = 0; k < 4; k++)
            Console.WriteLine($"  {names[k],-7} {phases[k].bytes / (double)iterations,10:N0} B   {phases[k].ticks / (double)Stopwatch.Frequency * 1000.0 / iterations,7:F3} ms");
        File.WriteAllText("scene.txt", LastScene);
        if (js) Js.Run(built, panel, size, iterations);
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
    internal static string LastScene = string.Empty;


    private static (long bytes, long ticks)[] Once(Panel panel, VisualElement root, HtmlRenderer.Result built,
        Dictionary<VisualElement, OffThread.Box> boxes, List<VisualElement> scratch, Tweens tweens,
        Dictionary<string, SceneSlots.Value> slots, Vector2 size, float now)
    {
        var p = new (long bytes, long ticks)[4];
        long b0, t0;

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
        p[3] = (GC.GetAllocatedBytesForCurrentThread() - b0, Stopwatch.GetTimestamp() - t0);

        LastSceneLength = output.Length;
        LastScene = output.Scene;
        return p;
    }

    /// <summary>The page out of a chip file: the first long-bracket string that starts with a tag.</summary>
    private static string? PageOf(string lua)
    {
        var m = Regex.Match(lua, @"\[(=*)\[\s*<(.*?)\]\1\]", RegexOptions.Singleline);
        return m.Success ? "<" + m.Groups[2].Value : null;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Bench;

/// <summary>
/// The page's own script, frame after frame, as the surface runs it: how much the interpreter
/// and the DOM shims allocate per frame. Measured process-wide (the engine has its own thread,
/// so a per-thread counter here would read zero) with nothing else running.
/// </summary>
internal static class Js
{
    private static void Attach(HtmlRenderer.Result built)
    {
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
    }

    internal static void Run(HtmlRenderer.Result built, Panel panel, Vector2 size, int frames)
    {
        if (string.IsNullOrWhiteSpace(built.Script)) { Console.WriteLine("  (page has no script)"); return; }

        var host = new ScriptHost(
            id => built.ById.TryGetValue(id, out var e) ? e : null,
            id => built.Shapes.TryGetValue(id, out var sh) ? sh : null,
            id => built.ById.TryGetValue(id, out var e) && built.NodeOf.TryGetValue(e, out var n) ? n : null,
            built.Query,
            built.Reclass,
            built.Rules,
            m => Console.WriteLine("  warn: " + m),
            (parentId, html) =>
            {
                if (built.ById.TryGetValue(parentId, out var p) && built.NodeOf.TryGetValue(p, out var pn))
                {
                    HtmlRenderer.AppendFragment(p, pn, html, built);
                    Attach(built);
                }
            },
            id => { if (built.ById.TryGetValue(id, out var r)) HtmlRenderer.Remove(r, built); },
            (id, value) => { },
            id => { },
            (id, offset) => { },
            (parentId, html, beforeId) =>
            {
                if (built.ById.TryGetValue(parentId, out var p) && built.NodeOf.TryGetValue(p, out var pn))
                {
                    HtmlRenderer.InsertFragment(p, pn, html, beforeId, built);
                    Attach(built);
                }
            });
        host.Attach(built, () => { }, size, (ve, frames, spec) => 0, h => { });

        ScriptedScreensHtmlPlugin.Log ??= new BenchLogger();
        host.Run(built.Script);
        for (var i = 0; i < 60 && !host.Frame(i * 0.016f, built.ById, 200); i++) { }
        for (var i = 0; i < 20; i++) { host.Frame(1f + i * 0.016f, built.ById, 200); host.Pump(); panel.Layout(size.x, size.y); }

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        var ran = 0;
        for (var i = 0; i < frames; i++)
        {
            // one frame per iteration, so the per-frame figures do not depend on the rate gate
            if (host.RunSynchronously(2f + i * 0.016f, built.ById, 2000)) ran++;
            host.Pump();
        }
        sw.Stop();
        var bytes = GC.GetTotalAllocatedBytes(precise: true) - before;
        Console.WriteLine($"  script  {bytes / (double)Math.Max(1, ran),10:N0} B   {sw.Elapsed.TotalMilliseconds / Math.Max(1, ran),7:F3} ms per frame ({ran} frames ran)");
        host.Dispose();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Bench;

/// <summary>
/// What one page's working set COSTS TO KEEP: the DOM, the cascade records, the layout tree and
/// the script engine, measured as retained bytes rather than as bytes allocated per frame.
/// </summary>
/// <remarks>
/// This is the number that decides whether releasing a compiled page's working set is worth
/// building. A compiled page never touches any of it again - its Lua is in the chip - but every
/// byte still in the heap is a byte Boehm marks on every collection, and the mark phase is what
/// sets how LONG each pause is. Allocation rate sets how OFTEN.
///
/// Measured by keeping K independent copies alive and differencing a forced
/// <c>GC.GetTotalMemory(true)</c>. One copy would be lost in the noise of the runtime's own
/// warm-up; K of them makes the per-page figure fall out of the slope.
/// </remarks>
internal static class Retained
{
    internal static void Run(string html, int copies)
    {
        var face = FontLibrary.Default();
        ResolvedStyle.DefaultFace = face;
        HtmlRenderer.SurfaceAspect = 1f;
        ScriptedScreensHtmlPlugin.Log ??= new BenchLogger();

        // One throwaway build first: the renderer's static caches, the font face and the regex
        // tables are built once and would otherwise be charged to page number one.
        Warm(html, face);

        var pages = new List<object>(copies);
        var basePages = Settle();
        for (var i = 0; i < copies; i++) pages.Add(BuildOne(html));
        var withPages = Settle();

        var hosts = new List<ScriptHost>(copies);
        foreach (var p in pages)
        {
            var built = (HtmlRenderer.Result)((object[])p)[0];
            if (string.IsNullOrWhiteSpace(built.Script)) continue;
            var host = Js.Make(built, new Vector2(built.ViewportWidth, built.ViewportWidth));
            host.Run(built.Script);
            for (var i = 0; i < 20; i++) { host.RunSynchronously(i * 0.016f, built.ById, 500); host.Pump(); }
            hosts.Add(host);
        }
        var withHosts = Settle();

        var perPage = (withPages - basePages) / (double)copies;
        var perHost = hosts.Count > 0 ? (withHosts - withPages) / (double)hosts.Count : 0;
        Console.WriteLine($"retained, {copies} copies:");
        Console.WriteLine($"  dom + cascade + layout   {perPage / 1024.0,9:0.0} KB per page");
        Console.WriteLine($"  script engine            {perHost / 1024.0,9:0.0} KB per page ({hosts.Count} of them)");
        Console.WriteLine($"  total                    {(perPage + perHost) / 1024.0,9:0.0} KB per page");
        Console.WriteLine($"  16 consoles              {(perPage + perHost) * 16 / 1048576.0,9:0.0} MB");

        // Keep both alive to here, or the collector is free to take them before the last reading.
        GC.KeepAlive(pages); GC.KeepAlive(hosts);
        foreach (var h in hosts) h.Dispose();
    }

    private static void Warm(string html, FaceData face)
    {
        var b = HtmlRenderer.Build(html, face);
        var p = new Panel(b.Root);
        p.Layout(b.ViewportWidth, b.ViewportWidth);
        GC.KeepAlive(p);
    }

    private static object BuildOne(string html)
    {
        var built = HtmlRenderer.Build(html, ResolvedStyle.DefaultFace);
        var panel = new Panel(built.Root);
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
        panel.Layout(built.ViewportWidth, built.ViewportWidth);
        return new object[] { built, panel };
    }

    /// <summary>A reading the collector has had every chance to make smaller.</summary>
    private static long Settle()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }
        return GC.GetTotalMemory(true);
    }
}

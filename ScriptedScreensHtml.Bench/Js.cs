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
    internal static long DomBytes, DomCalls, DomChars, PumpBytes, TickBytes, MorphBytes, Morphs;

    private static void Attach(HtmlRenderer.Result built)
    {
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
    }

    private static ScriptHost? _host;

    /// <summary>Build the host and let the page's script populate the tree, before anything is measured.</summary>
    internal static void Start(HtmlRenderer.Result built, Vector2 size)
    {
        if (string.IsNullOrWhiteSpace(built.Script)) return;
        _host = Make(built, size);
        _host.Run(built.Script);
        for (var i = 0; i < 80; i++) { _host.RunSynchronously(i * 0.016f, built.ById, 500); _host.Pump(); }
    }

    /// <summary>The surface's gate: has this page anything to do at all this frame?</summary>
    internal static bool Wants(float now) => _host == null || _host.WantsFrame(now);

    /// <summary>One script frame, as the game runs one before each emit.</summary>
    /// <summary>What the main thread spends on the script half, split: waiting for the worker, and
    /// applying what it queued. The two want completely different fixes and the bench charged both
    /// to one "script" line.</summary>
    internal static long WaitBytes, ApplyBytes; internal static int Steps;

    internal static void Step(HtmlRenderer.Result built, float now)
    {
        if (_host == null) return;
        var b = GC.GetAllocatedBytesForCurrentThread();
        // Frame, not RunSynchronously: the game's per-frame path is Frame(now, byId, waitMs: 12)
        // (HtmlSurface.PageFrame). RunSynchronously is the capture and rebuild path and allocates an
        // event and a closure per call that a page frame never pays - measuring it charged the
        // emitter's neighbour for work the game does not do.
        _host.Frame(now, built.ById, 12);
        var mid = GC.GetAllocatedBytesForCurrentThread();
        _host.Pump();
        WaitBytes += mid - b; ApplyBytes += GC.GetAllocatedBytesForCurrentThread() - mid; Steps++;
    }

    internal static void Run(HtmlRenderer.Result built, Panel panel, Vector2 size, int frames)
    {
        if (string.IsNullOrWhiteSpace(built.Script)) { Console.WriteLine("  (page has no script)"); return; }

        var host = _host ?? Make(built, size);
        RunWith(host, built, panel, size, frames);
    }

    private static ScriptHost Make(HtmlRenderer.Result built, Vector2 size)
    {
        ScriptedScreensHtmlPlugin.Log ??= new BenchLogger();
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
                var b = GC.GetAllocatedBytesForCurrentThread();
                if (built.ById.TryGetValue(parentId, out var p) && built.NodeOf.TryGetValue(p, out var pn))
                {
                    HtmlRenderer.AppendFragment(p, pn, html, built);
                    Attach(built);
                }
                DomBytes += GC.GetAllocatedBytesForCurrentThread() - b; DomCalls++; DomChars += html.Length;
            },
            id => { var b = GC.GetAllocatedBytesForCurrentThread(); if (built.ById.TryGetValue(id, out var r)) HtmlRenderer.Remove(r, built); DomBytes += GC.GetAllocatedBytesForCurrentThread() - b; },
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
        host.Attach(built, () => { }, size, (ve, f, spec) => 0, h => { });
        // the surface's in-place path: an innerHTML write whose structure matches updates the tree
        // rather than rebuilding it. Without this the bench measures only the slow path.
        host.TryMorph = (id, html) =>
        {
            var b = GC.GetAllocatedBytesForCurrentThread();
            var ok = built.ById.TryGetValue(id, out var target)
                     && built.NodeOf.TryGetValue(target, out var targetNode)
                     && HtmlRenderer.Morph(target, targetNode, html, built, _ => { });
            MorphBytes += GC.GetAllocatedBytesForCurrentThread() - b;
            if (ok) { Morphs++; Attach(built); }
            return ok;
        };

        ScriptedScreensHtmlPlugin.Log ??= new BenchLogger();
        return host;
    }

    private static void RunWith(ScriptHost host, HtmlRenderer.Result built, Panel panel, Vector2 size, int frames)
    {
        for (var i = 0; i < 20; i++) { host.RunSynchronously(1f + i * 0.016f, built.ById, 500); host.Pump(); panel.Layout(size.x, size.y); }

        ScriptHost.TickBytes = ScriptHost.TickCalls = 0;
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        var ran = 0;
        for (var i = 0; i < frames; i++)
        {
            // one frame per iteration, so the per-frame figures do not depend on the rate gate
            var tb = GC.GetTotalAllocatedBytes(precise: true);
            if (host.RunSynchronously(2f + i * 0.016f, built.ById, 2000)) ran++;
            var pb = GC.GetTotalAllocatedBytes(precise: true);
            TickBytes += pb - tb;
            host.Pump();
            PumpBytes += GC.GetTotalAllocatedBytes(precise: true) - pb;
        }
        sw.Stop();
        var bytes = GC.GetTotalAllocatedBytes(precise: true) - before;
        Console.WriteLine($"  script  {bytes / (double)Math.Max(1, ran),10:N0} B   {sw.Elapsed.TotalMilliseconds / Math.Max(1, ran),7:F3} ms per frame ({ran} frames ran)");
        Console.WriteLine($"          of that: script+timers {TickBytes / (double)Math.Max(1, ran),10:N0} B, applying writes {PumpBytes / (double)Math.Max(1, ran),10:N0} B");
        if (Morphs > 0 || MorphBytes > 0)
            Console.WriteLine($"          {Morphs} in-place morphs, {MorphBytes / Math.Max(1, Morphs),10:N0} B each");
        if (DomCalls > 0)
            Console.WriteLine($"          {DomCalls} innerHTML/append calls, {DomChars / DomCalls} chars each, {DomBytes / DomCalls,10:N0} B each (parse, cascade, tree)");
        if (ScriptHost.TickCalls > 0)
            Console.WriteLine($"          inside the engine: {ScriptHost.TickBytes / (double)ScriptHost.TickCalls,10:N0} B per tick ({ScriptHost.TickCalls} ticks)");
        host.Dispose();
    }
}

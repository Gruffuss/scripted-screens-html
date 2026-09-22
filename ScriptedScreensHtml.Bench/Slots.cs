using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml.Bench;

/// <summary>
/// The slot names a page's emitted scene actually exposes.
/// </summary>
/// <remarks>
/// Written because the style-to-scene probe was asserting its own answer. It carried a hand-written
/// list of eleven slot names - no <c>_s</c>, no <c>_sw</c>, no <c>_fo</c> - and then reported
/// <c>borderColor</c>, <c>borderWidth</c> and <c>visibility</c> as writes the compiler refuses, on
/// the strength of that list. Six measurement bugs in one session share that shape: the instrument
/// agreeing with itself. The cure is the same every time - emit the thing and read what came out.
///
/// It lives here rather than in the test project because splitting a scene into slots needs
/// <c>SceneSlots</c>, which needs the game's managed assemblies; the tests are deliberately
/// Unity-free and shell out, exactly as they already do to draw a page.
/// </remarks>
internal static class Slots
{
    private static string Scene = string.Empty;

    internal static int Run(string[] args)
    {
        var path = args.Length > 1 ? args[1] : null;
        if (path == null || !File.Exists(path)) { Console.Error.WriteLine("usage: --slots <page.html>"); return 1; }

        var html = File.ReadAllText(path);
        var face = FontLibrary.Default();
        ResolvedStyle.DefaultFace = face;
        HtmlRenderer.SurfaceAspect = 1f;
        var built = HtmlRenderer.Build(html, face);
        // Without this no transform or opacity group is named, and those are exactly the slots a
        // compiled page writes - so the scene would differ from the game's in the place that matters.
        HtmlRenderer.NameDrivenGroups(built);

        var panel = new Panel(built.Root);
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
        var size = new Vector2(built.ViewportWidth, built.ViewportWidth);
        OffThread.MainThreadId = Environment.CurrentManagedThreadId;
        var boxes = new Dictionary<VisualElement, OffThread.Box>();
        OffThread.Boxes = boxes;
        OffThread.Job = OffThread.Globals.Take();
        panel.Layout(size.x, size.y);
        // Without this the emitter reads an empty box table and every shape has zero geometry, so
        // the scene comes out as nothing but its header - which looks exactly like a page that
        // failed to build.
        OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());

        var slots = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        OffThread.Active = true;
        try
        {
            var output = VectorEmitter.Emit(built, built.Root, size.x, size.y);
            Scene = new string(output.Chars, 0, output.Length);
            SceneSlots.Split(output.Chars, output.Length, slots);
        }
        finally { OffThread.Active = false; }

        Console.WriteLine("# " + slots.Count + " slots, " + built.Warnings.Count + " warning(s), script "
                          + (built.Script == null ? "null" : built.Script.Length.ToString()) + " chars, "
                          + built.Driven.Count + " driven, " + built.NamedGroups.Count + " named groups");
        Console.WriteLine("# --- scene ---");
        Console.WriteLine(Scene);
        foreach (var w in built.Warnings) Console.WriteLine("# warn: " + w);
        foreach (var name in slots.Keys.OrderBy(k => k, StringComparer.Ordinal)) Console.WriteLine(name);
        return 0;
    }
}

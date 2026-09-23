using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml.Bench;

/// <summary>
/// Whether a page's <c>innerHTML</c> compiles: how many of its values reach a slot, and whether
/// the stand-ins used to find them are gone afterwards.
/// </summary>
/// <remarks>
/// Finding where a value lands means putting the page's markup into the live page with a stand-in
/// in every hole (`987653`, `#0F0085`), laying it out and emitting it. If that is not undone, the
/// console draws the stand-ins where its readings belong - which is what AtmoDark, AtmoLight and
/// AtmoApple all did on 2026-09-22. So the checks that matter are on what comes out AFTER the
/// compile: the structure it hands the renderer, and the scene the page emits from then on.
/// </remarks>
internal static class Holes
{
    internal static int Run(string[] args)
    {
        var path = args.Length > 1 ? args[1] : "../ScriptedScreensHtml/AtmoDark.lua";
        if (!File.Exists(path)) { Console.Error.WriteLine($"no such file: {path}"); return 1; }

        var text = File.ReadAllText(path);
        var html = path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? Program.PageOf(text) : text;
        if (html == null) { Console.Error.WriteLine("no page in that file"); return 1; }

        var face = FontLibrary.Default();
        ResolvedStyle.DefaultFace = face;
        HtmlRenderer.SurfaceAspect = 1f;
        var built = HtmlRenderer.Build(html, face);
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
        OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());

        Console.WriteLine($"{Path.GetFileName(path)}\n");
        var slots = Emit(built, size, null);

        var t0 = DateTime.UtcNow;
        var compiled = PageCompiler.Compile(built, panel, size, slots, ("main", "page", "html:page"), out var markup);
        var ms = (DateTime.UtcNow - t0).TotalMilliseconds;
        if (markup == null) { Console.WriteLine("  no innerHTML write to compile"); return 0; }

        foreach (var t in markup.Targets)
        {
            var reached = t.Holes.Count;
            var kinds = string.Join(", ", t.Holes.Values.GroupBy(h => h.Kind).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
            Console.WriteLine($"  {t.Id}: {reached} of {t.Markup.Holes.Count} values reach the scene ({kinds}); "
                              + $"{t.Labels.Count} label(s) built from pieces, {t.Roots.Count} gated alternative(s), "
                              + $"{t.Bindings.Count} state(s): {string.Join(", ", t.Bindings.Select(b => b.Key + " x" + b.States.Count))}");
            var missed = t.Markup.Holes.Where(h => !t.Holes.ContainsKey(h.Index)).Select(h => h.Index).ToList();
            if (missed.Count > 0)
                Console.WriteLine($"    not written ({missed.Count}): " + string.Join(", ", missed.Take(8).Select(h => $"{h} `{t.Markup.Text(t.Markup.Holes[h].Value)}`")));
        }
        foreach (var p in markup.Problems.Take(8)) Console.WriteLine("    ! " + Short120(p));
        if (markup.Problems.Count > 8) Console.WriteLine($"    ... and {markup.Problems.Count - 8} more");

        // The structure the compile hands the renderer, and what it opens with.
        var inStructure = 0;
        if (markup.Template != null)
            foreach (var line in markup.Template.Split('\n'))
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b98765\d\b|#0F[0-9A-F]{4}\b")) inStructure++;
        var inValues = markup.Values.Count(p => Leak(p.Value));
        Console.WriteLine();
        Console.WriteLine(inStructure == 0 && inValues == 0
            ? $"  structure: no stand-ins ({markup.Template?.Split('\n').Length ?? 0} lines, {markup.Values.Count} opening values)"
            : $"  STRUCTURE: {inStructure} line(s) and {inValues} opening value(s) still carry a stand-in");

        // The page as it emits from here on: the compile put the real markup back.
        panel.Layout(size.x, size.y);
        OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());
        var after = Emit(built, size, "holes-scene-after-compile.txt");
        var leaked = after.Where(p => Leak(p.Value)).Select(p => p.Key).ToList();
        Console.WriteLine(leaked.Count == 0
            ? "  after compile: the page emits no stand-ins"
            : $"  AFTER COMPILE: {leaked.Count} stand-in(s) - {string.Join(", ", leaked.Take(6))} (scene in holes-scene-after-compile.txt)");

        Console.WriteLine();
        Console.WriteLine($"  compiles: {(compiled.Ok ? "YES" : "no")} in {ms:0} ms"
                          + $"  ({compiled.Bindings.Count} binding(s), {compiled.Problems.Count} problem(s), "
                          + $"{compiled.Unmapped.Count} unmapped, {compiled.Warnings.Count} warning(s))");
        foreach (var p in compiled.Problems.Take(4)) Console.WriteLine("    ! " + Short120(p));
        foreach (var u in compiled.Unmapped.Take(4)) Console.WriteLine("    ? " + Short120(u));
        return 0;
    }

    private static bool Leak(SceneSlots.Value v)
        => v.IsNumber ? MarkupSlots.HoleOf(v.Number) >= 0 : v.Text is { Length: > 0 } t && MarkupSlots.ColourHoleOf(t) >= 0;

    private static Dictionary<string, SceneSlots.Value> Emit(HtmlRenderer.Result built, Vector2 size, string? keep)
    {
        var slots = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        OffThread.Active = true;
        try
        {
            var output = VectorEmitter.Emit(built, built.Root, size.x, size.y);
            SceneSlots.Split(output.Chars, output.Length, slots);
            if (keep != null) File.WriteAllText(keep, new string(output.Chars, 0, output.Length));
        }
        finally { OffThread.Active = false; }
        return slots;
    }

    private static string Short120(string s) => s.Length <= 120 ? s : s.Substring(0, 119) + "~";
}

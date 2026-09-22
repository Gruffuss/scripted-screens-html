using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml.Bench;

/// <summary>
/// Whether a page's <c>innerHTML</c> holes actually resolve to scene slots.
/// </summary>
/// <remarks>
/// <see cref="Markup"/> can be checked headlessly because it only reads an AST. This half cannot:
/// resolving a hole means laying the page out and emitting it, which needs the layout engine and the
/// emitter. So it lives here, where the game's managed assemblies are available, and it answers the
/// only question that matters about the whole approach - of the holes a page has, how many reach a
/// slot the chip can write.
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

        var script = string.Empty;
        foreach (Match m in Regex.Matches(html, "<script[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            script += m.Groups[1].Value + "\n";

        Script ast;
        try { ast = new Parser().ParseScript(script); }
        catch (Exception ex) { Console.Error.WriteLine("parse: " + ex.Message); return 1; }

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
        OffThread.Boxes = new Dictionary<VisualElement, OffThread.Box>();
        OffThread.Job = OffThread.Globals.Take();
        panel.Layout(size.x, size.y);

        Console.WriteLine($"{Path.GetFileName(path)}\n");

        var any = false;
        foreach (var write in Writes(ast))
        {
            if (write.Left is not MemberExpression { Object: { } owner }) continue;
            var id = Target(owner, ast);
            if (id == null) { Console.WriteLine("  a write to an element chosen at run time - skipped"); continue; }

            var markup = Markup.Of(write.Right, ast);
            if (markup.Problems.Count > 0)
            {
                Console.WriteLine($"  {id}: markup does not reduce ({markup.Problems.Count} problem(s))");
                foreach (var p in markup.Problems.Take(2)) Console.WriteLine("    ! " + p);
                continue;
            }

            any = true;
            var shapes = markup.Shapes();
            static int Count(VisualElement v) { var n = 1; for (var i = 0; i < v.childCount; i++) n += Count(v[i]); return n; }
            var beforeTree = Count(built.Root);
            var beforeEl = built.ById.TryGetValue(id, out var probed) && probed != null ? probed.childCount : -1;
            var landed = MarkupSlots.ResolveAll(id, markup, built, panel, size);
            var afterEl = built.ById.TryGetValue(id, out var probed2) && probed2 != null ? probed2.childCount : -1;
            Console.WriteLine($"    tree: {beforeTree} -> {Count(built.Root)} elements; "
                              + $"\"{id}\" children {beforeEl} -> {afterEl}; "
                              + $"same element: {ReferenceEquals(probed, probed2)}");
            if (landed.Count == 0) { Console.WriteLine($"  {id}: no shape could be put into the page"); continue; }

            var holes = markup.Holes.Count;
            var numbers = landed.Values.Count(l => l.IsNumber);
            var texts = landed.Count - numbers;
            Console.WriteLine($"  {id}: {landed.Count} of {holes} holes reach a slot "
                              + $"({numbers} numeric, {texts} text), {markup.Choices} choices over {shapes.Count} shapes");

            // Where the ones that did not land actually sit in the markup: guessing at this is how
            // the last three "obvious" diagnoses in this project turned out wrong.
            // Presence is checked against EVERY shape that was emitted, not against the default
            // one. Checking the default alone reported 155 holes as "not in this shape" when they
            // were in a shape that had been emitted and simply had not reached a slot - which is a
            // completely different problem, and the one worth working on.
            var skeletons = shapes.Select(sh => markup.Skeleton(sh)).ToArray();
            var whole = 0; var inStyle = 0; var inAttr = 0; var inText = 0; var absent = 0;
            for (var h = 0; h < holes; h++)
            {
                if (landed.ContainsKey(h)) continue;
                var sentinel = Markup.Sentinel(h);
                var skeleton = skeletons.FirstOrDefault(k => k.Contains(sentinel, StringComparison.Ordinal));
                if (skeleton == null) { absent++; continue; }
                var at = skeleton.IndexOf(sentinel, StringComparison.Ordinal);
                var before = skeleton.Substring(Math.Max(0, at - 40), Math.Min(40, at));
                var quote = before.LastIndexOf('"');
                if (quote < 0) { inText++; continue; }
                var attr = before.Substring(0, quote);
                var eq = attr.LastIndexOf('=');
                var name = eq > 0 ? attr.Substring(attr.LastIndexOfAny(new[] { ' ', '<' }, eq) + 1, eq - attr.LastIndexOfAny(new[] { ' ', '<' }, eq) - 1) : "";
                if (name == "style") { if (quote == before.Length - 1) whole++; else inStyle++; }
                else inAttr++;
            }
            Console.WriteLine($"    of those: {whole} are a WHOLE style attribute, {inStyle} inside one, "
                              + $"{inAttr} another attribute, {inText} text, {absent} not in this shape");

            // The first few text misses in context. Which shape of markup they sit in is the whole
            // question, and it is cheaper to look than to reason about it.
            var shown = 0;
            for (var h = 0; h < holes && shown < 4; h++)
            {
                if (landed.ContainsKey(h)) continue;
                var sentinel = Markup.Sentinel(h);
                var k = skeletons.FirstOrDefault(x => x.Contains(sentinel, StringComparison.Ordinal));
                if (k == null) continue;
                var at = k.IndexOf(sentinel, StringComparison.Ordinal);
                var attr = k.LastIndexOf('"', at) > k.LastIndexOf('>', at) ? "attr" : "text";
                var from = Math.Max(0, at - 55);
                var context = k.Substring(from, Math.Min(110, k.Length - from)).Replace('\n', ' ');
                Console.WriteLine($"    miss {h,3} [{attr}]: ...{context}...");
                shown++;
            }

            var missed = Enumerable.Range(0, holes).Where(h => !landed.ContainsKey(h)).ToArray();
            if (missed.Length > 0)
                Console.WriteLine($"    {missed.Length} did not: holes {string.Join(",", missed.Take(12))}"
                                  + (missed.Length > 12 ? " ..." : ""));
            foreach (var pair in landed.Take(6))
                Console.WriteLine($"    hole {pair.Key,3} -> {pair.Value.Slot}"
                                  + (pair.Value.IsNumber ? "" : $"  text \"{Short(pair.Value.Before)}…{Short(pair.Value.After)}\""));
        }
        if (!any) Console.WriteLine("  no innerHTML write reduced far enough to resolve");

        // The end-to-end question, which nothing short of this answers: does the page COMPILE now?
        // Resolving holes is only worth anything if the compiler then accepts the page.
        var slots = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        var sceneText = string.Empty;
        OffThread.Active = true;
        try
        {
            var output = VectorEmitter.Emit(built, built.Root, size.x, size.y);
            sceneText = new string(output.Chars, 0, output.Length);
            SceneSlots.Split(output.Chars, output.Length, slots);
        }
        finally { OffThread.Active = false; }

        // Did probing leave the page as it found it? Resolving a hole means putting a SKELETON full
        // of sentinels into the live page, laying it out and emitting it. If that is not undone, the
        // console draws `987653` and `#0F0085` where its readings belong - which is exactly what
        // AtmoDark, AtmoLight and AtmoApple all did on 2026-09-22, silently, on pages that do not
        // even compile. So the scene emitted AFTER all the probing is the thing to check.
        var leaked = 0;
        foreach (var pair in slots)
        {
            var isLeak = (pair.Value.IsNumber && Markup.HoleOf(pair.Value.Number) >= 0)
                         || (pair.Value.Text is { Length: > 0 } t && Markup.ColourHoleOf(t) >= 0);
            if (!isLeak) continue;
            if (leaked < 12)
                Console.WriteLine($"    leak: {pair.Key} = "
                                  + (pair.Value.IsNumber ? pair.Value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                                         : pair.Value.Text));
            leaked++;
        }
        if (leaked > 0)
        {
            File.WriteAllText("holes-scene.txt", sceneText);
            Console.WriteLine("    (scene written to holes-scene.txt)");
        }
        Console.WriteLine();
        Console.WriteLine(leaked == 0
            ? "  restored: the page emits no sentinels after probing"
            : $"  RESTORED: NO - {leaked} sentinel(s) survive in the emitted scene, so a console would draw them");

        var compiled = PageCompiler.Compile(built, panel, size, slots);

        // The game emits AFTER Compile, not after ResolveAll - and Compile does more than resolve
        // holes (it enumerates states by applying classes to the live page, among other things).
        // The check above never covered that. This one does: emit again exactly as a surface would,
        // with the incremental capture a surface uses, and look.
        var after = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        panel.Layout(size.x, size.y);
        if (OffThread.Boxes is { } inc) OffThread.Capture(built.Root, built, inc, new List<VisualElement>());
        OffThread.Active = true;
        try
        {
            var o2 = VectorEmitter.Emit(built, built.Root, size.x, size.y);
            SceneSlots.Split(o2.Chars, o2.Length, after);
            File.WriteAllText("holes-scene-after-compile.txt", new string(o2.Chars, 0, o2.Length));
        }
        finally { OffThread.Active = false; }
        var leakedAfter = 0;
        foreach (var pair in after)
            if ((pair.Value.IsNumber && Markup.HoleOf(pair.Value.Number) >= 0)
                || (pair.Value.Text is { Length: > 0 } t2 && Markup.ColourHoleOf(t2) >= 0)) leakedAfter++;
        Console.WriteLine(leakedAfter == 0
            ? "  after Compile: still no sentinels"
            : $"  AFTER COMPILE: {leakedAfter} sentinel(s) - the leak is in a Compile step later than ResolveAll");

        Console.WriteLine();
        Console.WriteLine($"  compiles: {(compiled.Ok ? "YES" : "no")}"
                          + $"  ({compiled.Bindings.Count} binding(s), {compiled.Problems.Count} problem(s), "
                          + $"{compiled.Unmapped.Count} unmapped)");
        foreach (var p in compiled.Problems.Take(4)) Console.WriteLine("    ! " + Short120(p));
        foreach (var u in compiled.Unmapped.Take(4)) Console.WriteLine("    ? " + Short120(u));
        return 0;
    }

    private static string Short120(string s) => s.Length <= 120 ? s : s.Substring(0, 119) + "~";

    private static string Short(string? s) => s == null ? "" : s.Length <= 14 ? s : s.Substring(0, 13) + "~";

    private static IEnumerable<AssignmentExpression> Writes(Node n)
    {
        if (n is AssignmentExpression { Left: MemberExpression { Property: Identifier { Name: "innerHTML" } } } a) yield return a;
        foreach (var child in n.ChildNodes)
        {
            if (child == null) continue;
            foreach (var w in Writes(child)) yield return w;
        }
    }

    /// <summary>The element id a write targets, through the page's own getElementById wrapper.</summary>
    private static string? Target(Node owner, Script ast)
    {
        if (owner is CallExpression { Arguments.Count: 1 } call && call.Arguments[0] is StringLiteral s) return s.Value;
        if (owner is not Identifier name) return null;
        foreach (var node in All(ast))
            if (node is VariableDeclarator { Id: Identifier v, Init: CallExpression { Arguments.Count: 1 } init }
                && v.Name == name.Name && init.Arguments[0] is StringLiteral lit)
                return lit.Value;
        return null;
    }

    private static IEnumerable<Node> All(Node n)
    {
        yield return n;
        foreach (var child in n.ChildNodes)
        {
            if (child == null) continue;
            foreach (var d in All(child)) yield return d;
        }
    }
}

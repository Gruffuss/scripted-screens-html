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
            var landed = MarkupSlots.ResolveAll(id, markup, built, panel, size);
            if (landed.Count == 0) { Console.WriteLine($"  {id}: no shape could be put into the page"); continue; }

            var holes = markup.Holes.Count;
            var numbers = landed.Values.Count(l => l.IsNumber);
            var texts = landed.Count - numbers;
            Console.WriteLine($"  {id}: {landed.Count} of {holes} holes reach a slot "
                              + $"({numbers} numeric, {texts} text), {markup.Choices} choices over {shapes.Count} shapes");

            // Where the ones that did not land actually sit in the markup: guessing at this is how
            // the last three "obvious" diagnoses in this project turned out wrong.
            var skeleton = markup.Skeleton();
            var whole = 0; var inStyle = 0; var inAttr = 0; var inText = 0; var absent = 0;
            for (var h = 0; h < holes; h++)
            {
                if (landed.ContainsKey(h)) continue;
                var at = skeleton.IndexOf(Markup.Sentinel(h), StringComparison.Ordinal);
                if (at < 0) { absent++; continue; }
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

            var missed = Enumerable.Range(0, holes).Where(h => !landed.ContainsKey(h)).ToArray();
            if (missed.Length > 0)
                Console.WriteLine($"    {missed.Length} did not: holes {string.Join(",", missed.Take(12))}"
                                  + (missed.Length > 12 ? " ..." : ""));
            foreach (var pair in landed.Take(6))
                Console.WriteLine($"    hole {pair.Key,3} -> {pair.Value.Slot}"
                                  + (pair.Value.IsNumber ? "" : $"  text \"{Short(pair.Value.Before)}…{Short(pair.Value.After)}\""));
        }
        if (!any) Console.WriteLine("  no innerHTML write reduced far enough to resolve");
        return 0;
    }

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

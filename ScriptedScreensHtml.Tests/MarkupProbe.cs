using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// What the markup analysis makes of every page that writes <c>innerHTML</c>.
/// </summary>
/// <remarks>
/// The same discipline as the other sweeps: the question is not whether one page reduces, it is how
/// much of the corpus does and what stops the rest. A shape analysis that works on a test case and
/// not on the pages people wrote is worth nothing.
/// </remarks>
internal static class MarkupProbe
{
    internal static void Run(string[] args)
    {
        var paths = args.Length > 1 ? args.Skip(1).ToArray() : Corpus();
        var total = 0;
        var reduced = 0;
        var problems = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            string text;
            try { text = File.ReadAllText(path); } catch { continue; }
            var html = path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? text : Bracketed(text);
            if (html == null) continue;

            var script = string.Empty;
            foreach (Match m in Regex.Matches(html, "<script[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                script += m.Groups[1].Value + "\n";
            if (!script.Contains(".innerHTML", StringComparison.Ordinal)) continue;

            Script ast;
            try { ast = new Parser().ParseScript(script); }
            catch { continue; }

            foreach (var write in Writes(ast))
            {
                total++;
                var markup = Markup.Of(write.Right, ast);
                var skeleton = markup.Skeleton();
                // Recursively: the top level of a real page is a choice chain, so counting only the
                // outermost parts reported "0 fixed" for pages whose whole structure had reduced.
                var fixedChars = FixedChars(markup.Parts);
                var ok = markup.Problems.Count == 0 && skeleton.Contains('<', StringComparison.Ordinal);
                if (ok) reduced++;

                Console.WriteLine($"{Path.GetFileName(path),-28} {(ok ? "reduced" : "BLOCKED"),-8} "
                                  + $"{markup.Holes.Count,3} holes, {markup.Choices,2} choices, "
                                  + $"{skeleton.Length,6} chars of skeleton ({fixedChars} fixed)");
                foreach (var p in markup.Problems)
                {
                    Console.WriteLine("     ! " + p);
                    var kind = Regex.Replace(p, @"^line \d+: ", "");
                    kind = Regex.Replace(kind, "`[^`]*`", "`..`");
                    problems.TryGetValue(kind, out var n);
                    problems[kind] = n + 1;
                }
            }
        }

        Console.WriteLine($"\n{reduced} of {total} innerHTML writes reduce to structure + holes");
        if (problems.Count == 0) return;
        Console.WriteLine("\nwhat stops the rest:");
        foreach (var kv in problems.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Value,3}  {kv.Key}");
    }

    /// <summary>Fixed markup anywhere in the shape, which is what becomes scene structure.</summary>
    private static int FixedChars(List<Markup.Part> parts)
    {
        var n = 0;
        foreach (var part in parts)
            switch (part)
            {
                case Markup.Fixed f: n += f.Text.Length; break;
                case Markup.Choice c: n += FixedChars(c.Then) + FixedChars(c.Else); break;
                case Markup.Repeat r: n += FixedChars(r.Body); break;
            }
        return n;
    }

    /// <summary>Every `x.innerHTML = ...` in a script, with the expression assigned.</summary>
    private static IEnumerable<AssignmentExpression> Writes(Node n)
    {
        if (n is AssignmentExpression { Left: MemberExpression { Property: Identifier { Name: "innerHTML" } } } a) yield return a;
        foreach (var child in n.ChildNodes)
        {
            if (child == null) continue;
            foreach (var w in Writes(child)) yield return w;
        }
    }

    private static string[] Corpus()
    {
        var list = new List<string>();
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 9 && d != null; i++)
        {
            foreach (var folder in new[] { "StationeersLua", "StationeersLuaAddonTemplate/ScriptedScreensHtml" })
            {
                var root = Path.Combine(d, folder);
                if (!Directory.Exists(root)) continue;
                foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                    if ((f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                        && !f.Contains("\\obj\\") && !f.Contains("\\bin\\") && !f.Contains(".bak") && !list.Contains(f))
                        list.Add(f);
            }
            d = Path.GetDirectoryName(d);
        }
        return list.ToArray();
    }

    private static string? Bracketed(string lua)
    {
        var open = lua.IndexOf("[==[", StringComparison.Ordinal);
        var close = lua.LastIndexOf("]==]", StringComparison.Ordinal);
        if (open >= 0 && close > open) return lua.Substring(open + 4, close - open - 4);
        open = lua.IndexOf("[[", StringComparison.Ordinal);
        close = lua.LastIndexOf("]]", StringComparison.Ordinal);
        return open >= 0 && close > open ? lua.Substring(open + 2, close - open - 2) : null;
    }
}

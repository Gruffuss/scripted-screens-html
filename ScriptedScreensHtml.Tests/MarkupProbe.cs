using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// What the markup analysis makes of a page's <c>innerHTML</c> writes: every hole, choice and list,
/// with the source each came from. `--markup [--tree] page.lua...`
/// </summary>
/// <remarks>
/// The question worth asking of an analysis like this is never "does it reduce" but "what did it
/// decide each piece was" - a value read where structure was meant draws the wrong page, and only
/// the itemised list shows it.
/// </remarks>
internal static class MarkupProbe
{
    internal static void Run(string[] args)
    {
        var tree = args.Contains("--tree");
        foreach (var path in args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(path);
            var html = path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? text : Bracketed(text);
            if (html == null) continue;
            var script = string.Empty;
            foreach (Match m in Regex.Matches(html, "<script[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                script += m.Groups[1].Value + "\n";
            var ast = new Parser().ParseScript(script);
            foreach (var write in Writes(ast))
            {
                var t0 = DateTime.UtcNow;
                var markup = Markup.Of(write, ast, script);
                var ms = (DateTime.UtcNow - t0).TotalMilliseconds;
                var rows = markup.Lists.Sum(l => l.Each.Count);
                Console.WriteLine($"{Path.GetFileName(path)} line {write.Location.Start.Line}: {markup.Holes.Count} holes, "
                                  + $"{markup.Choices.Count} choices, {markup.Lists.Count} lists ({rows} rows), {ms:0} ms");
                foreach (var p in markup.Problems) Console.WriteLine("   ! " + p);
                if (tree) Dump(markup.Parts, script, "   ");
                foreach (var c in markup.Choices)
                    Console.WriteLine($"   choice {c.Index}: {Source(c.Test, script)}");
                foreach (var l in markup.Lists)
                    Console.WriteLine($"   list {l.Index}: {Source(l.List, script)} x{l.Each.Count}");
                if (args.Contains("--js"))
                {
                    // What the chunk will evaluate for each value, choice, list and handler.
                    foreach (var h in markup.Holes) Console.WriteLine($"   js {h.Index}: {markup.Js(h.Value) ?? "(none)"}");
                    foreach (var c in markup.Choices) Console.WriteLine($"   js ?{c.Index}: {markup.Js(c.Test) ?? "(none)"}");
                    foreach (var l in markup.Lists) Console.WriteLine($"   js *{l.Index}: {markup.Js(l.List) ?? "(none)"}");
                    var clicks = markup.Write("x", (_, _) => "0").Clicks;
                    foreach (var (element, push) in clicks) Console.WriteLine($"   on {element}: {markup.Handler(push.Handler, n => "KEPT_" + n)?.Replace("\n", " ") ?? "(none)"}");
                    continue;
                }
                foreach (var h in markup.Holes)
                {
                    var alts = markup.Enumerate(h.Value);
                    Console.WriteLine($"   hole {h.Index}: {Source(h.Value, script)}"
                                      + (alts != null ? $"   [{alts.Count} alt]" : ""));
                }
            }
        }
    }

    private static void Dump(List<Markup.Part> parts, string script, string indent)
    {
        foreach (var part in parts)
            switch (part)
            {
                case Markup.Fixed f: Console.WriteLine(indent + "F " + Short(f.Text)); break;
                case Markup.Hole h: Console.WriteLine(indent + "H" + h.Index + " " + Source(h.Value, script)); break;
                case Markup.Push p: Console.WriteLine(indent + "P " + Source(p.Handler, script)); break;
                case Markup.Choice c:
                    Console.WriteLine(indent + "? " + Source(c.Test, script));
                    Dump(c.Then, script, indent + "  |");
                    Console.WriteLine(indent + "  else");
                    Dump(c.Else, script, indent + "  |");
                    break;
                case Markup.Rows r:
                    Console.WriteLine(indent + "* " + Source(r.List, script) + " x" + r.Each.Count);
                    if (r.Each.Count > 0) Dump(r.Each[0], script, indent + "  #");
                    break;
            }
    }

    internal static string Source(Markup.Term t, string script) => t switch
    {
        Markup.Src s => Short(script.Substring(s.Expr.Range.Start, s.Expr.Range.End - s.Expr.Range.Start)),
        Markup.Elem e => Source(e.List, script) + "[" + e.Index + "]",
        Markup.Row r => r.Lua,
        Markup.Const c => "=" + Markup.JsString(c.Value),
        Markup.Applied a => "(call)",
        Markup.Picked p => Source(p.Of, script) + "." + p.Key,
        _ => t.GetType().Name,
    };

    private static string Short(string s)
    {
        s = s.Replace('\n', ' ');
        return s.Length <= 110 ? s : s.Substring(0, 107) + "...";
    }

    private static IEnumerable<AssignmentExpression> Writes(Node n)
    {
        if (n is AssignmentExpression { Left: MemberExpression { Property: Identifier { Name: "innerHTML" } } } a) yield return a;
        foreach (var child in n.ChildNodes)
        {
            if (child == null) continue;
            foreach (var w in Writes(child)) yield return w;
        }
    }

    internal static string? Bracketed(string lua)
    {
        var open = lua.IndexOf("[==[", StringComparison.Ordinal);
        var close = lua.LastIndexOf("]==]", StringComparison.Ordinal);
        if (open >= 0 && close > open) return lua.Substring(open + 4, close - open - 4);
        open = lua.IndexOf("[[", StringComparison.Ordinal);
        close = lua.LastIndexOf("]]", StringComparison.Ordinal);
        return open >= 0 && close > open ? lua.Substring(open + 2, close - open - 2) : null;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// Every page there is, and what stops each one compiling - the whole corpus in one table.
/// </summary>
/// <remarks>
/// Written because working page by page is how this got slow. Fixing whatever blocks the file that
/// happens to be open optimises for that file; the blockers are shared, and which ones to build is a
/// question about their FREQUENCY across all 80-odd pages, not about any one of them. This answers
/// that in a second, offline, and it is the thing to re-run after every change rather than a single
/// example.
///
/// It reports the two halves a page can fail at and they are different failures. <b>Translate</b> is
/// the JavaScript the transpiler refuses - a missing language feature, and a page that hits one
/// cannot compile at all. <b>Writes</b> is what the page does to the DOM that has no equivalent as a
/// scene value; those are answered per write, so a page can have several and still be worth fixing
/// for the one that appears everywhere.
///
/// The slot half of the question (does the emitted scene actually expose the name a write needs)
/// cannot be answered here, because it needs the layout engine. What it does answer is which
/// constructs stand between the corpus and compiling at all, which is the part that decides what to
/// build next.
/// </remarks>
internal static class Corpus
{
    private sealed class Page
    {
        public string Name = string.Empty;
        public string Group = string.Empty;
        public int Chars;
        public int ScriptChars;
        public bool Translates;
        public readonly List<string> TranslateProblems = new();
        public readonly List<string> WriteProblems = new();
        public int RuntimeWrites;
        public int Motion;
        public string Driver = "none";
        public int InnerHtml;
        public bool Compiles => ScriptChars == 0 || (Translates && TranslateProblems.Count == 0 && WriteProblems.Count == 0);
    }

    internal static void Run(string[] args)
    {
        var roots = args.Length > 1
            ? args.Skip(1).ToArray()
            : new[]
            {
                Near("StationeersLua"),
                Near("StationeersLuaAddonTemplate/ScriptedScreensHtml/examples"),
                Near("StationeersLuaAddonTemplate/ScriptedScreensHtml"),
            };

        var pages = new List<Page>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (root == null || !Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ext is not (".html" or ".lua")) continue;
                if (file.Contains("\\obj\\") || file.Contains("\\bin\\") || file.Contains(".bak")) continue;
                if (!seen.Add(Path.GetFileName(file) + "|" + new FileInfo(file).Length)) continue;
                var page = Of(file, root);
                if (page != null) pages.Add(page);
            }
        }

        Table(pages);
        Blockers(pages);
    }

    private static string? Near(string folder)
    {
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 9 && d != null; i++)
        {
            var candidate = Path.Combine(d, folder);
            if (Directory.Exists(candidate)) return candidate;
            d = Path.GetDirectoryName(d);
        }
        return null;
    }

    private static Page? Of(string file, string root)
    {
        string text;
        try { text = File.ReadAllText(file); } catch { return null; }

        var html = file.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? text : Bracketed(text);
        if (html == null) return null;
        // A .lua that merely mentions html is not a page; a page has a document in it.
        if (!html.Contains("<body", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("<div", StringComparison.OrdinalIgnoreCase)) return null;

        var page = new Page
        {
            Name = Path.GetFileName(file),
            Group = Path.GetDirectoryName(file)?.Replace(root, "").Trim('\\', '/') is { Length: > 0 } g ? g : ".",
            Chars = html.Length,
        };

        var script = string.Empty;
        foreach (Match m in Regex.Matches(html, "<script[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            script += m.Groups[1].Value + "\n";
        page.ScriptChars = script.Trim().Length;
        if (page.ScriptChars == 0) { page.Translates = true; return page; }

        page.Driver = script.Contains("requestAnimationFrame", StringComparison.Ordinal) ? "raf"
                    : script.Contains("setInterval", StringComparison.Ordinal) ? "interval"
                    : "none";
        page.InnerHtml = Regex.Matches(script, @"\.innerHTML\s*=").Count;

        try
        {
            var lua = JsToLua.Compile(script, out var problems);
            page.Translates = lua != null;
            foreach (var p in problems) page.TranslateProblems.Add(p);
        }
        catch (Exception ex) { page.TranslateProblems.Add("threw: " + ex.GetType().Name); return page; }

        try
        {
            var (writes, notes) = DomWrites.Of(script);
            foreach (var n in notes) page.WriteProblems.Add(n);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var w in writes)
            {
                if (!w.Runtime) continue;
                keys.Add((w.Id ?? w.Prefix ?? "?") + "." + w.Property);
                // The properties that have no scene equivalent at all, named so the histogram can
                // count them: these are what a page has to stop doing, or the compiler has to learn.
                if (w.Property is "innerHTML" or "scrollTop" or "scrollLeft")
                    page.WriteProblems.Add($"{w.Property} on \"{w.Id ?? w.Prefix ?? "?"}\"");
            }
            page.RuntimeWrites = keys.Count;
        }
        catch (Exception ex) { page.WriteProblems.Add("writes threw: " + ex.GetType().Name); }

        try
        {
            var (found, _) = Motion.Of(script);
            page.Motion = found.Select(f => f.Id + "." + f.Property).Distinct(StringComparer.Ordinal).Count();
        }
        catch { /* the motion pass never blocks a page */ }

        return page;
    }

    /// <summary>The page out of a chip file's long bracket. Null when the file holds no page.</summary>
    private static string? Bracketed(string lua)
    {
        var open = lua.IndexOf("[==[", StringComparison.Ordinal);
        var close = lua.LastIndexOf("]==]", StringComparison.Ordinal);
        if (open >= 0 && close > open) return lua.Substring(open + 4, close - open - 4);
        open = lua.IndexOf("[[", StringComparison.Ordinal);
        close = lua.LastIndexOf("]]", StringComparison.Ordinal);
        return open >= 0 && close > open ? lua.Substring(open + 2, close - open - 2) : null;
    }

    private static void Table(List<Page> pages)
    {
        Console.WriteLine($"{pages.Count} pages\n");
        Console.WriteLine($"{"page",-44} {"kb",4} {"drv",-8} {"rt",3} {"mot",3} {"state",-9} why");
        foreach (var p in pages.OrderBy(p => p.Compiles).ThenBy(p => p.Group, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal))
        {
            var why = p.TranslateProblems.Concat(p.WriteProblems).Take(2).ToArray();
            var state = p.ScriptChars == 0 ? "no script" : p.Compiles ? "COMPILES" : "blocked";
            Console.WriteLine($"{Trim(p.Group + "/" + p.Name, 44),-44} {p.Chars / 1024,4} {p.Driver,-8} {p.RuntimeWrites,3} {p.Motion,3} {state,-9} {Trim(string.Join("; ", why), 70)}");
        }
    }

    /// <summary>
    /// What stands between the corpus and compiling, most common first. This is the build order.
    /// </summary>
    private static void Blockers(List<Page> pages)
    {
        var scripted = pages.Where(p => p.ScriptChars > 0).ToList();
        Console.WriteLine($"\n{pages.Count(p => p.ScriptChars == 0)} of {pages.Count} have no script (nothing to compile)");
        Console.WriteLine($"{scripted.Count(p => p.Compiles)} of {scripted.Count} scripted pages compile today");
        Console.WriteLine($"drivers: {scripted.Count(p => p.Driver == "raf")} rAF, {scripted.Count(p => p.Driver == "interval")} interval, {scripted.Count(p => p.Driver == "none")} neither");

        var counts = new Dictionary<string, (int Pages, int Hits)>(StringComparer.Ordinal);
        foreach (var p in scripted)
        {
            foreach (var kind in p.TranslateProblems.Select(Kind).Concat(p.WriteProblems.Select(Kind)).Distinct(StringComparer.Ordinal))
            {
                counts.TryGetValue(kind, out var had);
                counts[kind] = (had.Pages + 1, had.Hits);
            }
            foreach (var kind in p.TranslateProblems.Select(Kind).Concat(p.WriteProblems.Select(Kind)))
            {
                counts.TryGetValue(kind, out var had);
                counts[kind] = (had.Pages, had.Hits + 1);
            }
        }

        Console.WriteLine($"\n{"blocker",-46} {"pages",5} {"total",5}");
        foreach (var kv in counts.OrderByDescending(k => k.Value.Pages).ThenByDescending(k => k.Value.Hits))
            Console.WriteLine($"{Trim(kv.Key, 46),-46} {kv.Value.Pages,5} {kv.Value.Hits,5}");

        // What compiling would be worth: pages that would send nothing per frame once they do.
        var wouldBeSilent = scripted.Count(p => p.Driver != "raf");
        Console.WriteLine($"\n{wouldBeSilent} of {scripted.Count} scripted pages have no frame loop at all, so compiling them alone stops all per-frame work");
        Console.WriteLine($"{scripted.Count(p => p.Driver == "raf")} write every frame and need motion as expressions on top of compiling");
    }

    /// <summary>A problem message reduced to the construct it is about, so it can be counted.</summary>
    private static string Kind(string problem)
    {
        var at = problem.IndexOf(" at line", StringComparison.Ordinal);
        if (at > 0) problem = problem.Substring(0, at);
        at = problem.IndexOf(" (line", StringComparison.Ordinal);
        if (at > 0) problem = problem.Substring(0, at);
        // a quoted name is this page's, not the construct's
        problem = Regex.Replace(problem, "\"[^\"]*\"", "\"..\"");
        problem = Regex.Replace(problem, @"\bline \d+\b", "line N");
        return Trim(problem.Trim(), 90);
    }

    private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "~";
}

using System;
using System.IO;
using System.Text.RegularExpressions;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// Why one page declines to compile, answered from a chip file instead of from a game restart.
/// Takes the page out of its `[[ ... ]]` string the way the bench does, pulls the script out of
/// it, and prints what the translator and the write analysis each object to.
/// </summary>
internal static class Probe4
{
    internal static void Run(string path)
    {
        var lua = File.ReadAllText(path);
        // A chip file wraps its page in a long bracket; a design mockup IS the page. Both are
        // worth probing, and guessing wrong on a raw .html produced a negative substring length.
        // A .html file IS the page. Searching it for a long bracket finds one inside its own CSS or
        // script and silently truncates it - 60 KB became 2.6 KB, which reads as "this mockup is
        // tiny" rather than as a broken read.
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) { Report(Path.GetFileName(path), lua); return; }

        var open = lua.IndexOf("[==[", StringComparison.Ordinal);
        var close = lua.LastIndexOf("]==]", StringComparison.Ordinal);
        if (open >= 0 && close > open) open += 4;
        else
        {
            open = lua.IndexOf("[[", StringComparison.Ordinal);
            close = lua.LastIndexOf("]]", StringComparison.Ordinal);
            if (open >= 0 && close > open) open += 2; else { open = 0; close = lua.Length; }
        }
        Report(Path.GetFileName(path), lua.Substring(open, close - open));
    }

    private static void Report(string name, string page)
    {
        var script = string.Empty;
        foreach (Match m in Regex.Matches(page, "<script[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            script += m.Groups[1].Value + "\n";
        Console.WriteLine($"{name}: page {page.Length} chars, script {script.Length} chars");
        if (script.Trim().Length == 0) { Console.WriteLine("  no script"); return; }

        var translated = JsToLua.Compile(script, out var problems);
        Console.WriteLine($"  translate: {(translated == null ? "FAILED" : translated.Length + " chars")}, {problems.Count} problem(s)");
        foreach (var p in problems) Console.WriteLine("    - " + p);

        var (writes, notes) = DomWrites.Of(script);
        Console.WriteLine($"  writes: {writes.Count}, notes: {notes.Count}");
        foreach (var n in notes) Console.WriteLine("    ! " + n);
        var runtime = 0;
        foreach (var w in writes) if (w.Runtime) runtime++;
        Console.WriteLine($"  of those, {runtime} run every frame");
        foreach (var w in writes)
            if (w.Runtime) Console.WriteLine($"    {w.Id ?? (w.Prefix != null ? w.Prefix + "*" : w.Computed) ?? "?"}.{w.Property}  (line {w.Line})");
    }
}

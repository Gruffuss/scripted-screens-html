using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// What the motion pass makes of a real page: which of its per-frame writes become scene
/// expressions, and which stay values the chip has to send.
/// </summary>
/// <remarks>
/// This is the only honest way to scope the pass. Reasoning about which writes "ought to" be closed
/// forms has a poor record on this project; running it over the pages that exist answers it in a
/// second, with no game start.
/// </remarks>
internal static class MotionProbe
{
    internal static void Run(string[] args)
    {
        var paths = args.Length > 1 ? args.Skip(1).ToArray() : Defaults();
        foreach (var path in paths)
        {
            if (!File.Exists(path)) { Console.WriteLine($"-- {path}: not found"); continue; }
            Report(path);
        }
    }

    private static string[] Defaults()
    {
        var root = Root();
        var list = new List<string>();
        foreach (var dir in new[] { Path.Combine(root, "ScriptedScreensHtml", "examples"), Path.Combine(root, "ScriptedScreensHtml") })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.lua")) list.Add(f);
        }
        return list.ToArray();
    }

    private static string Root()
    {
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && d != null; i++)
        {
            if (Directory.Exists(Path.Combine(d, "ScriptedScreensHtml"))) return d;
            d = Path.GetDirectoryName(d);
        }
        return Directory.GetCurrentDirectory();
    }

    private static void Report(string path)
    {
        var text = File.ReadAllText(path);
        var script = Script(text);
        if (script == null) { return; }

        var (found, notes) = Motion.Of(script);
        var runtime = Runtime(script);

        Console.WriteLine($"== {Path.GetFileName(path)}: {runtime} runtime write(s), {found.Count} as expressions");
        foreach (var n in notes) Console.WriteLine("   ! " + n);
        foreach (var f in found)
        {
            var parts = string.Join(" | ", f.Parts.Select(p => p ?? "-"));
            Console.WriteLine($"   + {f.Id}.{f.Property} = {Short(parts)}");
        }
        Console.WriteLine();
    }

    private static string Short(string s) => s.Length <= 150 ? s : s.Substring(0, 147) + "...";

    /// <summary>How many writes the page makes that can happen after it is compiled - the denominator.</summary>
    private static int Runtime(string script)
    {
        var (writes, _) = DomWrites.Of(script);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in writes) if (w.Runtime) keys.Add((w.Id ?? w.Prefix ?? "?") + "." + w.Property);
        return keys.Count;
    }

    /// <summary>The page's script, out of the &lt;script&gt; block of a chip file.</summary>
    private static string? Script(string text)
    {
        var open = text.IndexOf("<script>", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var close = text.IndexOf("</script>", open, StringComparison.OrdinalIgnoreCase);
        if (close < 0) return null;
        return text.Substring(open + 8, close - open - 8);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ScriptedScreensHtml.Bench;

/// <summary>
/// Every CSS declaration and HTML tag the corpus uses that the cascade does not implement.
/// </summary>
/// <remarks>
/// Counted by RUNNING the real cascade over every page and collecting the warnings it raises, not by
/// grepping stylesheets - a grep cannot tell a CSS property from a JavaScript object key, and the
/// first attempt at this reported `children`, `props` and `ref` as missing CSS features.
///
/// The output is a build order. A property used on sixty pages and a property used on one are the
/// same amount of work to implement and a very different amount of value, and only the count says
/// which is which.
/// </remarks>
internal static class CorpusCss
{
    internal static void Run(string[] args)
    {
        var roots = args.Length > 1 ? args.Skip(1).ToArray() : Defaults();
        var byWarning = new Dictionary<string, (int Pages, int Hits)>(StringComparer.Ordinal);
        var pages = 0;
        var failed = 0;

        var face = FontLibrary.Default();
        ResolvedStyle.DefaultFace = face;
        HtmlRenderer.SurfaceAspect = 1f;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (ext is not (".html" or ".lua")) continue;
                if (file.Contains("\\obj\\") || file.Contains("\\bin\\") || file.Contains(".bak")) continue;

                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                var html = ext == ".html" ? text : Bracketed(text);
                if (html == null || html.IndexOf("<body", StringComparison.OrdinalIgnoreCase) < 0
                    && html.IndexOf("<div", StringComparison.OrdinalIgnoreCase) < 0) continue;

                pages++;
                try
                {
                    var built = HtmlRenderer.Build(html, face);
                    var here = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var w in built.Warnings)
                    {
                        var kind = Kind(w);
                        byWarning.TryGetValue(kind, out var had);
                        byWarning[kind] = (had.Pages + (here.Add(kind) ? 1 : 0), had.Hits + 1);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    var kind = "BUILD THREW: " + ex.GetType().Name + ": " + Trim(ex.Message, 60);
                    byWarning.TryGetValue(kind, out var had);
                    byWarning[kind] = (had.Pages + 1, had.Hits + 1);
                }
            }
        }

        Console.WriteLine($"{pages} pages laid out, {failed} threw\n");
        Console.WriteLine($"{"unsupported",-58} {"pages",5} {"total",6}");
        foreach (var kv in byWarning.OrderByDescending(k => k.Value.Pages).ThenByDescending(k => k.Value.Hits))
            Console.WriteLine($"{Trim(kv.Key, 58),-58} {kv.Value.Pages,5} {kv.Value.Hits,6}");
    }

    private static string[] Defaults()
    {
        var list = new List<string>();
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 9 && d != null; i++)
        {
            foreach (var folder in new[] { "StationeersLua", "StationeersLuaAddonTemplate/ScriptedScreensHtml" })
            {
                var c = Path.Combine(d, folder);
                if (Directory.Exists(c) && !list.Contains(c)) list.Add(c);
            }
            d = Path.GetDirectoryName(d);
        }
        return list.ToArray();
    }

    /// <summary>A warning reduced to the feature it is about, so two pages missing the same one agree.</summary>
    private static string Kind(string warning)
    {
        var at = warning.IndexOf(':');
        var head = at > 0 ? warning.Substring(0, at) : "";
        var rest = at > 0 ? warning.Substring(at + 1).Trim() : warning;
        // "css: `filter: blur(2px)` not supported" -> the property, not this page's value
        var colon = rest.IndexOf(':');
        if (head == "css" && colon > 0) rest = rest.Substring(0, colon).Trim(' ', '`');
        return Trim(head.Length > 0 ? head + ": " + rest : rest, 58);
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

    private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "~";
}

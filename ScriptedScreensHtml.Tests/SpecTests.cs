using System;
using System.IO;
using System.Linq;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// The user's spec for this mod (ScriptedScreensHtml/CLAUDE.md, "THE SPEC"): a page is translated
/// ONCE into what a person would hand-write for the vector mod - a vector scene plus plain Lua that
/// runs on the chip's own tick and events - and nothing of this mod runs afterwards. These checks
/// fail for as long as a compiled page still needs this mod's DOM emulation on the chip; they are
/// meant to be red until the compiler meets the spec, and must never be weakened to go green.
/// </summary>
internal static class SpecTests
{
    private static readonly string[] Pages =
    {
        "AtmoDark.lua", "AtmoApple.lua", "AtmoLight.lua",
        Path.Combine("examples", "07-game.lua"), Path.Combine("examples", "09-transition.lua"),
    };

    internal static void Run(Action<bool, string> check)
    {
        var root = ModFolder();
        foreach (var name in Pages)
        {
            var text = File.ReadAllText(Path.Combine(root, name));
            var page = MarkupProbe.Bracketed(text) ?? text;
            var (compiled, _) = Probe4.Headless(page);
            var lua = compiled.Lua ?? string.Empty;
            // The DOM emulation is the prelude's `DOM` table (getElementById, innerHTML, classList,
            // bind/flush). Lua a person writes for the vector mod sets data values; it has no DOM.
            var dom = lua.IndexOf("DOM.", StringComparison.Ordinal) >= 0;
            check(!dom, $"SPEC: {name} compiles to a vector scene and plain Lua; its Lua carries no DOM emulation " +
                        $"({(dom ? "it does: " + lua.Length + " chars of chunk built on the prelude's DOM" : "ok")})");
        }
    }

    /// <summary>
    /// A compiled page's Lua as a hand-written vector console has it: data values, the chip's tick and
    /// clicks, and no DOM - neither the prelude's emulation nor any browser name surviving into code.
    /// String literals and comments are not code, so a label reading "document" is no failure.
    /// </summary>
    internal static void PlainLua(string name, string lua, Action<bool, string> check)
    {
        var code = System.Text.RegularExpressions.Regex.Replace(lua, @"--[^\n]*|""(?:\\.|[^""\\])*""|\[(=*)\[[\s\S]*?\]\1\]", " ");
        var found = DomNames.Where(n => code.Contains(n, StringComparison.Ordinal)).ToList();
        check(found.Count == 0, $"SPEC: [{name}] compiles to a vector scene and plain Lua with no DOM "
                                + (found.Count == 0 ? $"({lua.Length} chars)" : "(it names " + string.Join(", ", found) + ")"));
    }

    private static readonly string[] DomNames =
    {
        "DOM.", "document", "getElementById", "querySelector", "textContent", "classList", "className",
        "addEventListener", "innerHTML", "style.", "PAGE_SYNC", "Pending", "requestAnimationFrame",
    };

    private static string ModFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "ScriptedScreensHtml");
            if (File.Exists(Path.Combine(candidate, "AtmoDark.lua"))) return candidate;
        }
        throw new DirectoryNotFoundException("ScriptedScreensHtml folder not found above " + AppContext.BaseDirectory);
    }
}

using System;
using System.Diagnostics;
using System.IO;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// Runs the prelude's own checks under a real Lua interpreter.
/// </summary>
/// <remarks>
/// Everything else in this suite tests the compiler's OUTPUT - that the right Lua is emitted. Nothing
/// tested that the runtime it is emitted against works, because the runtime is Lua and this is C#.
/// The gap is not academic: the manifest of prelude methods is checked against the file by a regular
/// expression, so a method that is named but never defined, or defined with the wrong arity, passes
/// every test here and then fails on a console with no line from the page. <c>Math.log2</c> sat in
/// the manifest undefined for exactly that reason.
///
/// Skipped, loudly, when there is no interpreter: a check that silently does not run is worse than
/// one that is absent, because the suite still says ALL PASS.
/// </remarks>
internal static class PreludeRuntime
{
    internal static void Run(Action<bool, string> check)
    {
        var script = Find("ScriptedScreensHtml/tests/prelude-check.lua");
        if (script == null)
        {
            check(false, "prelude: prelude-check.lua is missing");
            return;
        }

        var lua = Which("lua") ?? Which("lua5.4") ?? Which("lua5.3") ?? Which("luajit");
        if (lua == null)
        {
            Console.WriteLine("  SKIP  prelude: no lua interpreter on PATH, so the runtime checks did not run");
            return;
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo(lua, "\"" + script + "\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (p == null) { check(false, "prelude: could not start " + lua); return; }
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(60000);
            check(p.ExitCode == 0 && output.Contains("all prelude checks pass", StringComparison.Ordinal),
                  "prelude: the runtime checks pass under a real Lua\n    " + output.Trim().Replace("\n", "\n    "));
        }
        catch (Exception ex)
        {
            check(false, "prelude: running the checks threw - " + ex.Message);
        }
    }

    private static string? Find(string relative)
    {
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 9 && d != null; i++)
        {
            var candidate = Path.Combine(d, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            d = Path.GetDirectoryName(d);
        }
        return null;
    }

    private static string? Which(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path == null) return null;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            foreach (var name in new[] { exe, exe + ".exe" })
            {
                try
                {
                    var full = Path.Combine(dir, name);
                    if (File.Exists(full)) return full;
                }
                catch (ArgumentException) { /* a PATH entry with illegal characters */ }
            }
        }
        return null;
    }
}

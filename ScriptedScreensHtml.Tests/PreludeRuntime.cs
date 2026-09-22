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
        OnTheGamesLua(check);
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

    /// <summary>
    /// The same checks, on the interpreter the game actually embeds.
    /// </summary>
    /// <remarks>
    /// The standalone <c>lua</c> on PATH is NOT that interpreter, and the difference is not
    /// academic. Three defects hid in exactly that gap and passed every run:
    /// <c>string.format('%e', v)</c> with no precision is ignored outright and returns the number;
    /// <c>%g</c> and <c>%.Ne</c> produce a capital E with a three-digit exponent (<c>1.23E+004</c>),
    /// so every number a page printed in exponential form was right in the test and wrong in game;
    /// and <c>math.log(1000, 10)</c> answers 2.9999999999999996, which makes the standard
    /// digit-counting idiom one too few for every power of ten.
    ///
    /// All three were invisible to the standalone interpreter. Running on the real one is the only
    /// thing that would have caught them, and it costs about twenty lines.
    /// </remarks>
    private static void OnTheGamesLua(Action<bool, string> check)
    {
        var script = Find("ScriptedScreensHtml/tests/prelude-check.lua");
        var prelude = Find("ScriptedScreensHtml/JsPrelude.lua");
        if (script == null || prelude == null)
        {
            check(false, "prelude: cannot find the prelude or its checks");
            return;
        }
        try
        {
            var state = Lua.LuaState.Create();
            Lua.Standard.OpenLibsExtensions.OpenStandardLibraries(state);
            Run(state, File.ReadAllText(prelude), "prelude");
            // The check script loads the prelude itself, by a path relative to the standalone
            // interpreter's `arg[0]`. There is no arg[0] here and the prelude is already loaded, so
            // dofile is made a no-op for the one file rather than the script being changed to suit
            // the harness - it has to keep working under `lua` too.
            Run(state, "local real = dofile dofile = function(p) "
                       + "if tostring(p):find('JsPrelude') then return end return real(p) end", "shim");
            Run(state, File.ReadAllText(script), "prelude-check");
            check(true, "prelude: the runtime checks pass on the interpreter the game embeds");
        }
        catch (Exception ex)
        {
            check(false, "prelude: the checks FAIL on the game's own interpreter - " + First(ex.Message));
        }
    }

    private static void Run(Lua.LuaState state, string text, string name) =>
        state.RunAsync(state.Load(text.AsSpan(), name, state.Environment)).AsTask().GetAwaiter().GetResult();

    private static string First(string s)
    {
        var at = s.IndexOf('\n');
        return at > 0 ? s.Substring(0, at) : s;
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

using System;

namespace ScriptedScreensHtml;

// The three mod-side singletons the pipeline reaches for, stubbed so the emitter compiles
// outside the game: the plugin logger, the BepInEx-bound config, and the font library (which
// in game mirrors TextMeshPro faces the game registers). Nothing here is measured; they only
// have to answer.

internal sealed class BenchLogger
{
    public void LogInfo(object m) => Console.WriteLine("  info: " + m);
    public void LogWarning(object m) => Console.WriteLine("  warn: " + m);
    public void LogError(object m) => Console.WriteLine("  error: " + m);
}

internal static class ScriptedScreensHtmlPlugin
{
    internal static BenchLogger? Log;
}

internal static class HtmlConfig
{
    internal static bool Diagnostics => false;
    internal static bool DumpScenes => false;

    // The bench measures the interpreter, so it always runs the interpreter: BENCH_V8=1 to compare.
    internal static bool UseV8 => Environment.GetEnvironmentVariable("BENCH_V8") == "1";

    /// <summary>The in-game compile probe, which the bench drives itself rather than by config.</summary>
    internal static bool CompileProbe => false;
}

/// <summary>
/// The compiled page's runtime, read from disk rather than from an embedded resource.
/// </summary>
/// <remarks>
/// In the mod it is embedded, because a page cannot be compiled without it and a file in a mod
/// folder can be deleted or lost in a Workshop update. Here the file IS the source of truth and
/// reading it means the bench measures the prelude as it stands, not as it was when last built.
/// </remarks>
internal static class CompileProbe
{
    private static string? _prelude;

    internal static string? Prelude(out string? error)
    {
        error = null;
        if (_prelude != null) return _prelude;
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 9 && d != null; i++)
        {
            var path = System.IO.Path.Combine(d, "ScriptedScreensHtml", "JsPrelude.lua");
            if (System.IO.File.Exists(path)) return _prelude = System.IO.File.ReadAllText(path);
            d = System.IO.Path.GetDirectoryName(d);
        }
        error = "JsPrelude.lua not found on disk";
        return null;
    }
}

internal static class FontLibrary
{
    // One synthetic face with a flat half-em advance over printable ASCII: text lays out at a
    // believable width without a font file. Glyph-accurate metrics are not what this measures.
    private static readonly FaceData Face = Make();

    private static FaceData Make()
    {
        var f = new FaceData
        {
            Name = "Bench",
            PointSize = 90f,
            Scale = 1f,
            Ascent = 80f,
            Descent = -20f,
            LineHeight = 100f,
            CapLine = 64f,
            TabWidth = 45f,
        };
        for (uint c = 32; c < 127; c++)
            f.Chars[c] = new FaceData.Glyph { Index = c, Advance = 45f, Scale = 1f };
        return f;
    }

    public static void Alias(string family, string src, string weight, string style) { }
    public static string ResolveFace(string family) => family;
    public static FaceData? Get(string family) => Face;
    public static bool ResolvePending() => false;
    public static FaceData? Default() => Face;
}

/// <summary>The one member ScriptHost reaches for on the surface, copied so the script host builds here.</summary>
internal static class HtmlSurface
{
    internal static Label? TextTargetFor(VisualElement ve, string id)
    {
        if (ve is Label l) return l;
        if (ve.childCount == 1 && ve[0] is Label existing && existing.name == "#text") return existing;
        if (ve.childCount == 0)
        {
            var created = new Label { name = "#text" };
            created.style.flexGrow = 1;
            ve.Add(created);
            return created;
        }
        return null;
    }
}

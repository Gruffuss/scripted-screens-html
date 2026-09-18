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

using System;
using System.Collections.Generic;
using System.IO;

namespace ScriptedScreensHtml;

// The three mod-side singletons the pipeline reaches for, stubbed so the emitter compiles
// outside the game: the plugin logger, the BepInEx-bound config, and the font library (which
// in game mirrors TextMeshPro faces the game registers). The logger and config only have to answer;
// the font library measures the Fonts mod's own font files as the game does.

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
    // A family the Fonts mod ships a file for (Barlow, Barlow SemiBold, Barlow Condensed...) is measured
    // with that file's own metrics, as the game measures it: the Fonts mod builds a TextMeshPro face from
    // the file at 48 pt, and FaceCopy copies that face's FreeType metrics. Anything else gets one synthetic
    // face with a flat half-em advance over printable ASCII, which lays text out at a believable width.
    private static readonly FaceData Face = Make();
    private static readonly Dictionary<string, string> Files = Index();
    private static readonly Dictionary<string, FaceData> Real = new(StringComparer.OrdinalIgnoreCase);

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
    public static FaceData? Get(string family) => Font(family) ?? Face;
    public static bool ResolvePending() => false;
    public static FaceData? Default() => Face;

    /// <summary>The face of a font file the Fonts mod ships, by the name it registers it under, or null.</summary>
    private static FaceData? Font(string family)
    {
        var key = Normalise(family);
        lock (Real)
        {
            if (Real.TryGetValue(key, out var known)) return known;
            if (!Files.TryGetValue(key, out var path)) return null;
            var face = TrueType.Face(File.ReadAllBytes(path), FaceName(Path.GetFileName(path)));
            Real[key] = face;
            TextMeasure.Register(face);
            return face;
        }
    }

    /// <summary>The Fonts mod's bundled font files, by the name each is registered under ("Barlow SemiBold").</summary>
    private static Dictionary<string, string> Index()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var d = AppContext.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
        {
            var dir = Path.Combine(d, "StationeersLuaAddonTemplate-main", "Assets", "fonts");
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories))
                files[Normalise(FaceName(Path.GetFileName(path)))] = path;
            break;
        }
        return files;
    }

    /// <summary>"BarlowCondensed-SemiBold.ttf" -> "Barlow Condensed SemiBold", as the mod's FontLibrary.FaceNameFromFile names it.</summary>
    private static string FaceName(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        var dash = stem.IndexOf('-');
        var family = dash > 0 ? stem.Substring(0, dash) : stem;
        var style = dash > 0 ? stem.Substring(dash + 1) : string.Empty;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < family.Length; i++)
        {
            if (i > 0 && char.IsUpper(family[i]) && !char.IsUpper(family[i - 1])) sb.Append(' ');
            sb.Append(family[i]);
        }
        if (style.Length > 0 && !string.Equals(style, "Regular", StringComparison.OrdinalIgnoreCase)) sb.Append(' ').Append(style);
        return sb.ToString();
    }

    /// <summary>"Barlow Condensed", "BarlowCondensed" and "barlow-condensed" all match, as in the mod.</summary>
    private static string Normalise(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}

/// <summary>
/// A TrueType file's metrics as the game's TextMeshPro face of it holds them. The Fonts mod loads the file
/// with FontEngine.LoadFontFace(bytes, 48) and builds a static atlas of unhinted glyphs (SDFAA): every
/// metric is the font's own, in font units, times 48 over the em, unrounded. Checked against the game's
/// own layout dump of a Barlow page: a whole-pixel line height (FreeType's grid-fitted 58 rather than
/// 57.6) puts rows a pixel lower by the third. The Fonts mod adds no kerning table, so there are no pairs.
/// </summary>
internal static class TrueType
{
    private const int PointSize = 48;

    internal static FaceData Face(byte[] f, string name)
    {
        var tables = new Dictionary<string, int>(StringComparer.Ordinal);
        var count = U16(f, 4);
        for (var i = 0; i < count; i++)
            tables[System.Text.Encoding.ASCII.GetString(f, 12 + 16 * i, 4)] = (int)U32(f, 12 + 16 * i + 8);

        var head = tables["head"];
        var upem = U16(f, head + 18);
        var hhea = tables["hhea"];
        int ascender = S16(f, hhea + 4), descender = S16(f, hhea + 6), gap = S16(f, hhea + 8);
        var metrics = U16(f, hhea + 34);
        var os2 = tables.TryGetValue("OS/2", out var o) ? o : -1;
        if (ascender == 0 && descender == 0 && os2 >= 0)
        {
            ascender = S16(f, os2 + 68);
            descender = S16(f, os2 + 70);
            gap = S16(f, os2 + 72);
        }

        float Px(int units) => units * (float)PointSize / upem;
        var face = new FaceData
        {
            Name = name,
            PointSize = PointSize,
            Scale = 1f,
            Ascent = Px(ascender),
            Descent = Px(descender),
            LineHeight = Px(ascender - descender + gap),
            CapLine = os2 >= 0 && U16(f, os2) >= 2 ? Px(S16(f, os2 + 88)) : Px(ascender) * 0.7f,
            BoldSpacing = 7f,
            TabSize = 10,
        };

        var hmtx = tables["hmtx"];
        float Advance(int glyph) => Px(U16(f, hmtx + 4 * Math.Min(glyph, metrics - 1)));
        foreach (var (code, glyph) in Cmap(f, tables["cmap"]))
            if (glyph != 0) face.Chars[code] = new FaceData.Glyph { Index = (uint)glyph, Advance = Advance(glyph), Scale = 1f };
        if (face.Chars.TryGetValue(32, out var space)) face.TabWidth = space.Advance;
        return face;
    }

    /// <summary>Each character the file maps and its glyph: the Unicode subtable, format 12 or 4.</summary>
    private static IEnumerable<(uint, int)> Cmap(byte[] f, int cmap)
    {
        int best = -1, bestRank = 0;
        for (var i = 0; i < U16(f, cmap + 2); i++)
        {
            int platform = U16(f, cmap + 4 + 8 * i), encoding = U16(f, cmap + 6 + 8 * i);
            var at = cmap + (int)U32(f, cmap + 8 + 8 * i);
            var rank = platform == 3 && encoding == 10 ? 3 : platform == 0 ? 2 : platform == 3 && encoding == 1 ? 1 : 0;
            if (rank > bestRank && U16(f, at) is 4 or 12) { best = at; bestRank = rank; }
        }
        if (best < 0) yield break;
        if (U16(f, best) == 12)
        {
            var groups = U32(f, best + 12);
            for (var g = 0; g < groups; g++)
            {
                var p = best + 16 + 12 * g;
                uint start = U32(f, p), end = U32(f, p + 4), glyph = U32(f, p + 8);
                for (var c = start; c <= end && c <= 0x10FFFF; c++) yield return (c, (int)(glyph + c - start));
            }
            yield break;
        }
        var segs = U16(f, best + 6) / 2;
        int ends = best + 14, starts = ends + 2 * segs + 2, deltas = starts + 2 * segs, offsets = deltas + 2 * segs;
        for (var s = 0; s < segs; s++)
        {
            int end = U16(f, ends + 2 * s), start = U16(f, starts + 2 * s), delta = S16(f, deltas + 2 * s), range = U16(f, offsets + 2 * s);
            for (var c = start; c <= end && c != 0xFFFF; c++)
            {
                int glyph;
                if (range == 0) glyph = (c + delta) & 0xFFFF;
                else
                {
                    glyph = U16(f, offsets + 2 * s + range + 2 * (c - start));
                    if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                }
                yield return ((uint)c, glyph);
            }
        }
    }

    private static int U16(byte[] f, int at) => (f[at] << 8) | f[at + 1];
    private static int S16(byte[] f, int at) => (short)U16(f, at);
    private static uint U32(byte[] f, int at) => ((uint)f[at] << 24) | ((uint)f[at + 1] << 16) | ((uint)f[at + 2] << 8) | f[at + 3];
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

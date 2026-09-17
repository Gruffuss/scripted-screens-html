using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensHtml;

/// <summary>
/// The faces pages are laid out with: the ones the vector mod draws in, registered with TextMeshPro
/// by the game and the Fonts mod, found by name (<c>font-family: Barlow</c>, weight 600 = the
/// registered "Barlow SemiBold") and copied for measuring (<see cref="FaceCopy"/>). This mod reads
/// no font files and knows no font folders: where fonts come from is the Fonts mod's business.
/// </summary>
internal static class FontLibrary
{
    private static readonly Dictionary<string, FaceData?> Cache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>When a name last failed to resolve: the Fonts mod registers its faces over the first minutes, so a miss is retried.</summary>
    private static readonly Dictionary<string, float> MissedAt = new(StringComparer.OrdinalIgnoreCase);
    private const float RetryAfterSeconds = 5f;
    /// <summary>@font-face names: the family the page uses -> the TextMeshPro face name the file is registered under.</summary>
    private static readonly Dictionary<string, string> AliasFace = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// @font-face { font-family: X; src: url(Family-Style.ttf) }: X resolves to the face the Fonts
    /// mod registered that file under (a page cannot ship a file). Weight and style pick the styled
    /// face: a bold rule maps "X Bold" as well.
    /// </summary>
    public static void Alias(string family, string src, string weight, string style)
    {
        var face = FaceNameFromFile(Path.GetFileName(src.Replace('\\', '/')));
        if (face.Length == 0)
            return;
        var bold = weight.Trim().ToLowerInvariant() is "bold" or "bolder" || (float.TryParse(weight.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 600);
        var italic = style.Trim().ToLowerInvariant() is "italic" or "oblique";
        var key = bold && italic ? family + " Bold Italic" : bold ? family + " Bold" : italic ? family + " Italic" : family;
        lock (AliasFace)
        {
            AliasFace[key] = face;
            if (!AliasFace.ContainsKey(family)) AliasFace[family] = face;
        }
        lock (Cache)
        {
            Cache.Remove(key);
            Cache.Remove(family);
        }
        if (Find(face) == null)
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: @font-face \"{family}\": no registered face \"{face}\" yet (the Fonts mod registers font files by family and style)");
    }

    /// <summary>The face name the vector layer (TextMeshPro, registered by the Fonts mod) knows a page's family by: the @font-face alias resolved, else the name itself.</summary>
    public static string ResolveFace(string family)
    {
        lock (AliasFace)
            return AliasFace.TryGetValue(family, out var face) ? face : family;
    }

    /// <summary>"BarlowCondensed-SemiBold.ttf" -> "Barlow Condensed SemiBold", the way the Fonts mod names it from the font's own metadata.</summary>
    private static string FaceNameFromFile(string file)
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

    /// <summary>
    /// A registered face, copied for text measurement, or null (then the element keeps the
    /// inherited face). On the game thread an unknown name is looked up now; elsewhere it is
    /// queued for <see cref="ResolvePending"/> and the page lays out again once it resolves.
    /// </summary>
    public static FaceData? Get(string family)
    {
        var name = ResolveFace(family);
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null)
                return cached;
            if (MissedAt.TryGetValue(name, out var at) && OffThread.Seconds - at < RetryAfterSeconds)
                return null;
            if (!OffThread.OnMain)
            {
                Pending.Add(name);
                return null;
            }
        }
        var tmp = Find(name);
        var face = tmp != null ? FaceCopy.Of(tmp) : null;
        lock (Cache)
        {
            if (face != null) { Cache[name] = face; MissedAt.Remove(name); }
            else MissedAt[name] = OffThread.Seconds;
        }
        return face;
    }

    private static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> Resolving = new();

    /// <summary>Looks up the names workers asked for. Game thread. True when one resolved.</summary>
    public static bool ResolvePending()
    {
        lock (Cache)
        {
            if (Pending.Count == 0) return false;
            Resolving.AddRange(Pending);
            Pending.Clear();
        }
        var any = false;
        foreach (var name in Resolving)
            any |= Get(name) != null;
        Resolving.Clear();
        return any;
    }

    /// <summary>The face ScriptedScreens' own labels use (what a page with no font-family is drawn in). Game thread.</summary>
    public static FaceData? Default()
    {
        TMP_FontAsset? tmp;
        try { tmp = SS.GetFont(); } catch (Exception) { tmp = null; }
        return tmp != null ? FaceCopy.Of(tmp) : null;
    }

    /// <summary>The registered TextMeshPro face of that name; "Barlow SemiBold", "BarlowSemiBold" and "barlow-semibold" all match. Game thread.</summary>
    private static TMP_FontAsset? Find(string family)
    {
        var wanted = Normalise(family);
        if (wanted.Length == 0)
            return null;
        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (Normalise(f.name) == wanted)
                return f;
        return null;
    }

    /// <summary>"Barlow Condensed" and "BarlowCondensed" and "barlow-condensed" all match.</summary>
    private static string Normalise(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

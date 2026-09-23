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
        var bold = weight.Trim().ToLowerInvariant() is "bold" or "bolder" || (float.TryParse(weight.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 600);
        var italic = style.Trim().ToLowerInvariant() is "italic" or "oblique";
        var key = bold && italic ? family + " Bold Italic" : bold ? family + " Bold" : italic ? family + " Italic" : family;

        // A link, as a browser page writes it - including every @font-face in a Google Fonts
        // stylesheet, which <link>/@import fetch and inline. The Fonts mod downloads and registers
        // it (its own host allow-list decides whether it may); the face names it reports are what
        // the family maps to. Asked from the game thread, in ResolvePending.
        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            lock (Remote) Remote.Add((src, family, key, bold, italic));
            return;
        }

        var face = FaceNameFromFile(Path.GetFileName(src.Replace('\\', '/')));
        if (face.Length == 0)
            return;
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

    /// <summary>@font-face links waiting to be handed to the Fonts mod, and the ones already handed over.</summary>
    private static readonly List<(string Link, string Family, string Key, bool Bold, bool Italic)> Remote = new();
    private static readonly HashSet<string> Requested = new(StringComparer.Ordinal);
    private static System.Reflection.MethodInfo? _requestFont;
    private static bool _requestFontLooked;

    /// <summary>
    /// Bumped whenever a face a page asked for becomes available. Every surface compares it with
    /// the value it last laid out under: <see cref="ResolvePending"/> answers true only to the one
    /// surface that happened to drain the queue, and fifteen consoles showing one page all need
    /// the new face, not the first of them.
    /// </summary>
    public static int Generation;

    /// <summary>Looks up the names workers asked for, and starts any font downloads. Game thread. True when one resolved.</summary>
    public static bool ResolvePending()
    {
        StartRemote();
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
        if (any) Generation++;
        return any;
    }

    /// <summary>
    /// Hands queued @font-face links to the Fonts mod's loader (found by reflection: no compile-time
    /// link between the mods). Its callback, on the game thread, names the faces it registered; the
    /// family maps to the one matching the rule's weight and style, and the name is queued so the
    /// next <see cref="ResolvePending"/> lays the pages out with it.
    /// </summary>
    private static void StartRemote()
    {
        List<(string Link, string Family, string Key, bool Bold, bool Italic)>? batch = null;
        lock (Remote)
        {
            if (Remote.Count == 0) return;
            batch = new List<(string, string, string, bool, bool)>(Remote);
            Remote.Clear();
        }
        if (!_requestFontLooked)
        {
            _requestFontLooked = true;
            _requestFont = Type.GetType("ScriptedScreensFonts.FontApi, ScriptedScreensFonts")?.GetMethod("RequestFont");
        }
        foreach (var r in batch)
        {
            if (!Requested.Add(r.Key + "\n" + r.Link)) continue;
            var req = r;
            object? accepted = null;
            try
            {
                accepted = _requestFont?.Invoke(null, new object[] { req.Link, (Action<string[]>)(names => Downloaded(req.Family, req.Key, req.Bold, req.Italic, req.Link, names)) });
            }
            catch (Exception ex) { ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: @font-face \"{req.Family}\": the Fonts mod's loader failed - {ex}"); }
            if (_requestFont == null)
                ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: @font-face \"{req.Family}\" links to {req.Link}; loading a font from a link needs a Fonts mod that can (not installed, or older)");
            else if (accepted is false)
                ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: @font-face \"{req.Family}\": the Fonts mod refused {req.Link} (its log says why; hosts are limited by its PageFontHosts setting)");
        }
    }

    private static void Downloaded(string family, string key, bool bold, bool italic, string link, string[] names)
    {
        if (names.Length == 0)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: @font-face \"{family}\": nothing loaded from {link}");
            lock (Remote) Requested.Remove(key + "\n" + link);   // a later page may ask again
            return;
        }
        // The registered names follow the Fonts mod's "Family" / "Family Style" contract, so the
        // one this rule means is the one whose style words match its weight and slant.
        var face = names[0];
        foreach (var name in names)
        {
            var b = name.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Black", StringComparison.OrdinalIgnoreCase) >= 0;
            var i = name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0;
            if (b == bold && i == italic) { face = name; break; }
        }
        lock (AliasFace)
        {
            AliasFace[key] = face;
            if (!AliasFace.ContainsKey(family)) AliasFace[family] = face;
        }
        lock (Cache)
        {
            Cache.Remove(key);
            Cache.Remove(family);
            foreach (var name in names) { MissedAt.Remove(name); Pending.Add(name); }
        }
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

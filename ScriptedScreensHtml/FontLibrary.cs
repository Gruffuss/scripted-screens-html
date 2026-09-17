using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;
using AtlasPopulationMode = UnityEngine.TextCore.Text.AtlasPopulationMode;

namespace ScriptedScreensHtml;

/// <summary>
/// The faces pages are laid out with. The vector mod draws text through TextMeshPro, in the faces
/// the game and the Fonts mod registered; UI Toolkit, which lays the page out, cannot use a
/// TextMeshPro face, so each one is mirrored into a UI Toolkit font asset by name
/// (<c>font-family: Barlow</c>, weight 600 = the registered "Barlow SemiBold"). This mod reads no
/// font files and knows no font folders: where fonts come from is the Fonts mod's business.
/// </summary>
internal static class FontLibrary
{
    private static readonly Dictionary<string, FontAsset?> Cache = new(StringComparer.OrdinalIgnoreCase);
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
        Cache.Remove(key);
        Cache.Remove(family);
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

    /// <summary>A registered face as a UI Toolkit font asset, or null (then the page keeps the default face).</summary>
    public static FontAsset? Get(string family)
    {
        var name = ResolveFace(family);
        if (Cache.TryGetValue(name, out var cached) && cached != null)
            return cached;
        if (MissedAt.TryGetValue(name, out var at) && Time.realtimeSinceStartup - at < RetryAfterSeconds)
            return null;
        var asset = Mirror(name);
        if (asset != null) { Cache[name] = asset; MissedAt.Remove(name); }
        else MissedAt[name] = Time.realtimeSinceStartup;
        return asset;
    }

    /// <summary>
    /// A font the game or the Fonts mod registered with TextMeshPro, by asset name, as a
    /// UI Toolkit asset. TextMeshPro and TextCore share FaceInfo and Glyph, so the mirror is
    /// the same atlas texture and the same tables under a different class; a static copy,
    /// since the TMP asset's atlas is what it is. This is what makes every face the vector
    /// layer's labels can use available to a page.
    /// </summary>
    private static FontAsset? _default;

    /// <summary>The face ScriptedScreens' own labels use, mirrored. Also the glyph fallback for every other mirrored face.</summary>
    public static FontAsset? Default()
    {
        if (_default != null)
            return _default;
        TMP_FontAsset? tmp = null;
        try { tmp = SS.GetFont(); } catch (Exception) { tmp = null; }
        _default = tmp != null ? MirrorAsset(tmp, isDefault: true) : null;
        // ScriptedScreens gives its labels a DejaVu fallback for glyphs the face lacks
        // (subscripts, arrows, the degree sign); the mirror gets the same one.
        if (_default != null)
        {
            var dejavu = Mirror("ss_dejavu_fallback");
            if (dejavu != null)
                _default.fallbackFontAssetTable = new List<FontAsset> { dejavu };
        }
        return _default;
    }

    private static FontAsset? Mirror(string family)
    {
        var tmp = Find(family);
        return tmp == null ? null : MirrorAsset(tmp, isDefault: false);
    }

    /// <summary>The registered TextMeshPro face of that name; "Barlow SemiBold", "BarlowSemiBold" and "barlow-semibold" all match.</summary>
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

    private static FontAsset? MirrorAsset(TMP_FontAsset tmp, bool isDefault)
    {
        if (tmp.atlasTextures == null || tmp.atlasTextures.Length == 0)
            return null;

        try
        {
            var asset = ScriptableObject.CreateInstance<FontAsset>();
            asset.name = tmp.name + " (ui)";
            asset.faceInfo = tmp.faceInfo;
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            asset.atlasWidth = tmp.atlasWidth;
            asset.atlasHeight = tmp.atlasHeight;
            asset.atlasPadding = tmp.atlasPadding;
            asset.atlasRenderMode = tmp.atlasRenderMode;
            asset.atlasTextures = tmp.atlasTextures;
            asset.material = tmp.material;
            asset.glyphTable = new List<Glyph>(tmp.glyphTable);
            var chars = new List<Character>(tmp.characterTable.Count);
            foreach (var c in tmp.characterTable)
                chars.Add(new Character(c.unicode, asset, c.glyph));
            asset.characterTable = chars;
            asset.ReadFontAssetDefinition();
            // Glyphs a face lacks (the game's "code" has 98 characters, no arrows or degree
            // sign) come from the default label face, as they do for TextMeshPro labels.
            if (!isDefault)
            {
                var fallback = Default();
                if (fallback != null)
                    asset.fallbackFontAssetTable = new List<FontAsset> { fallback };
            }
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: mirrored TMP font \"{tmp.name}\" ({chars.Count} characters) for UI Toolkit");
            return asset;
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: could not mirror TMP font \"{tmp.name}\": {ex}");
            return null;
        }
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

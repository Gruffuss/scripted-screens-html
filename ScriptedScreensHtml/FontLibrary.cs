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
/// Font files on disk, as UI Toolkit (TextCore) font assets. Scans this mod's Assets/fonts
/// and the Fonts mod's Assets/fonts next to it, so <c>font-family: Barlow</c> in a page is
/// the same Barlow file the vector layer's labels use. Assets are built lazily from the file
/// with a dynamic SDF atlas, crisp at any size. Family and style come from the file name
/// (Barlow-Bold.ttf = family "barlow", style "bold"); the family alone selects the regular
/// face and UI Toolkit synthesises bold and italic. ponytail: read the name table for real
/// family names if a file ever does not follow the Family-Style convention.
/// </summary>
internal static class FontLibrary
{
    private static Dictionary<string, string>? _files;   // "family" and "family|style" -> path
    private static readonly Dictionary<string, FontAsset?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static FontAsset? Get(string family)
    {
        // Generic families (sans-serif, monospace ...) are not resolved: the legacy face
        // stays, and a page that wants a real font names one.
        var key = Normalise(family);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        Scan();
        FontAsset? asset = null;
        if (_files!.TryGetValue(key, out var path) || _files.TryGetValue(key + "|regular", out path))
        {
            try
            {
                asset = FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                asset.name = Path.GetFileNameWithoutExtension(path);
            }
            catch (Exception ex)
            {
                ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: font \"{family}\" failed to load from {path}: {ex.Message}");
                asset = null;
            }
        }
        asset ??= Mirror(family);
        Cache[key] = asset;
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
        TMP_FontAsset? tmp = null;
        foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (string.Equals(f.name, family, StringComparison.OrdinalIgnoreCase))
            {
                tmp = f;
                break;
            }
        }
        return tmp == null ? null : MirrorAsset(tmp, isDefault: false);
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

    private static void Scan()
    {
        if (_files != null)
            return;
        _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var here = Path.GetDirectoryName(typeof(FontLibrary).Assembly.Location) ?? string.Empty;
        var folders = new[]
        {
            Path.Combine(here, "Assets", "fonts"),
            Path.Combine(here, "..", "ScriptedScreensFonts", "Assets", "fonts"),
        };
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf")
                    continue;
                var stem = Path.GetFileNameWithoutExtension(file);
                var dash = stem.IndexOf('-');
                var family = Normalise(dash > 0 ? stem.Substring(0, dash) : stem);
                var style = dash > 0 ? Normalise(stem.Substring(dash + 1)) : "regular";
                _files[family + "|" + style] = file;
                // "Barlow SemiBold", "BarlowSemiBold" and "barlow-semibold" all normalise
                // to the same key, so a styled face is addressable by its full name.
                _files[family + style] = file;
                if (style == "regular" || style == "book" || !_files.ContainsKey(family))
                    _files[family] = file;
            }
        }
        ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: font library scanned, {_files.Count} entries");
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

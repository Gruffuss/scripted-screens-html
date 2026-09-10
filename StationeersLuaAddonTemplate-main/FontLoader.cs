using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace ScriptedScreensFonts;

/// <summary>
/// Builds TMP font assets from .ttf/.otf files dropped in the mod's <c>Assets/fonts</c>
/// folder, so a font the game does not ship can be used as <c>&lt;font="Family"&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// TMP's own <c>TMP_FontAsset.CreateFontAsset</c> cannot do this. Every glyph load in this
/// TMP version routes through <c>FontEngine.LoadFontFace(Font, ...)</c>, which needs a
/// <see cref="Font"/> carrying embedded font data. A runtime <c>Font</c> has none — an OS
/// font's data lives in the OS, and there is no runtime way to build a <c>Font</c> from a
/// file — so the call fails with "Make sure Include Font Data is enabled". There is no
/// <c>DynamicOS</c> atlas mode in this TMP version to fall back on either.
/// </para>
/// <para>
/// <c>FontEngine.LoadFontFace(byte[])</c> has no such restriction, so the face is read
/// straight from the file and the asset assembled by hand, mirroring what
/// <c>TMP_FontAsset.TryAddCharacters</c> does. The consequence is a <b>static</b> atlas:
/// glyphs are rendered once at load and nothing can be added afterwards, because "afterwards"
/// would need the <c>Font</c> we do not have. Hence a fixed character set.
/// </para>
/// </remarks>
internal static class FontLoader
{
    /// <summary>Folder scanned for font files, relative to the mod's own directory.</summary>
    internal const string FontsFolder = "Assets/fonts";

    // 48 rather than TMP's default 90: the whole character set has to fit one atlas, since a
    // static atlas cannot spill into a second one. At 90pt a 1024x1024 atlas runs out at
    // roughly 100 characters and the rest would silently not render.
    private const int SamplingPointSize = 48;
    private const int AtlasPadding = 5;
    private const int AtlasSize = 1024;

    /// <summary>
    /// Symbols a console readout reaches for that Latin-1 does not carry. Written as
    /// codepoints rather than literals so the set does not depend on how this source file
    /// is encoded.
    /// </summary>
    /// <remarks>
    /// A font that lacks one of these simply does not get it — <c>TryGetGlyphIndex</c>
    /// returns 0 and the character is skipped — so an absent glyph costs nothing. Barlow,
    /// for instance, has the minus sign and both dashes but no arrows at all. Baking a
    /// broad list therefore cannot make a font render something it does not contain; it
    /// only spares the author from listing symbols in <c>ExtraCharacters</c> for the fonts
    /// that do.
    /// </remarks>
    private static readonly uint[] DefaultExtras = BuildDefaultExtras();

    private static uint[] BuildDefaultExtras()
    {
        var extras = new List<uint>
        {
            // Punctuation typography expects
            0x2013, 0x2014,                 // en dash, em dash
            0x2018, 0x2019, 0x201C, 0x201D, // curly quotes
            0x2026, 0x2022,                 // ellipsis, bullet

            // Maths and units
            0x2212,                         // true minus, width-matched to + unlike hyphen
            0x2248, 0x2260, 0x2264, 0x2265, // approx, not equal, <=, >=
            0x221E, 0x221A, 0x2211, 0x220F, // infinity, root, sigma, product
            0x2206, 0x0394, 0x2207, 0x03B4, // increment, Delta, nabla, delta
            0x03C0, 0x03A9,                 // pi, Omega

            // Arrows, including the double forms used for state transitions
            0x2190, 0x2191, 0x2192, 0x2193,
            0x2194, 0x2195,
            0x21D0, 0x21D1, 0x21D2, 0x21D3,

            // Geometric shapes: status dots, gauge needles, sort indicators
            0x25B2, 0x25BC, 0x25C0, 0x25B6,
            0x25CF, 0x25CB, 0x25C6, 0x25C7,
            0x25A0, 0x25A1, 0x2605, 0x2606,

            // Status marks
            0x2713, 0x2717, 0x26A0,
        };

        // Block elements. 2581..2588 are the eighth-height bars a text sparkline is built
        // from; 2591..2593 are the shades used for fill and disabled states.
        for (uint c = 0x2581; c <= 0x2588; c++)
            extras.Add(c);

        for (uint c = 0x2591; c <= 0x2593; c++)
            extras.Add(c);

        // Box drawing, single and double. Cheap to include and the only way to draw a
        // frame in a label rather than in the vector layer.
        extras.AddRange(new uint[]
        {
            0x2500, 0x2502, 0x250C, 0x2510, 0x2514, 0x2518,
            0x251C, 0x2524, 0x252C, 0x2534, 0x253C,
            0x2550, 0x2551, 0x2554, 0x2557, 0x255A, 0x255D,
            0x2560, 0x2563, 0x2566, 0x2569, 0x256C,
        });

        return extras.ToArray();
    }

    private static bool _engineReady;
    private static string? _modDirectory;
    private static string _extraCharacters = string.Empty;
    private static bool _pending;

    /// <summary>Records where to look, to be acted on by <see cref="TryLoadPending"/>.</summary>
    internal static void Configure(string modDirectory, string extraCharacters)
    {
        _modDirectory = modDirectory;
        _extraCharacters = extraCharacters ?? string.Empty;
        _pending = true;
    }

    /// <summary>
    /// Loads the fonts folder once TMP's shaders are reachable, and reports whether that
    /// has happened yet.
    /// </summary>
    /// <remarks>
    /// Not done at mod load. <c>ShaderUtilities.ShaderRef_MobileSDF</c> resolves through
    /// <c>Shader.Find</c>, which returns null until TMP's own resources are loaded, and
    /// <c>new Material(null)</c> throws. Retrying on a null shader rather than deferring by
    /// a fixed delay means the load happens as soon as it can, and keeps trying if the game
    /// takes its time.
    /// </remarks>
    internal static void TryLoadPending()
    {
        if (!_pending || _modDirectory == null)
            return;

        if (ShaderUtilities.ShaderRef_MobileSDF == null)
            return;

        _pending = false;
        LoadFolder(_modDirectory, _extraCharacters);
    }

    /// <summary>
    /// Loads every font file in <paramref name="modDirectory"/>'s fonts folder.
    /// </summary>
    /// <param name="modDirectory">Directory holding the mod assembly.</param>
    /// <param name="extraCharacters">Characters to include beyond ASCII and Latin-1.</param>
    private static void LoadFolder(string modDirectory, string extraCharacters)
    {
        var folder = Path.Combine(modDirectory, FontsFolder);
        if (!Directory.Exists(folder))
        {
            ScriptedScreensFontsPlugin.Log?.LogInfo($"No font folder at \"{folder}\"; drop .ttf files there to add fonts.");
            return;
        }

        // Recursive: organising fonts into per-project subfolders is the obvious thing to
        // do, and a skipped subfolder looks identical to a font that failed to load.
        var files = new List<string>();
        files.AddRange(Directory.GetFiles(folder, "*.ttf", SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles(folder, "*.otf", SearchOption.AllDirectories));

        if (files.Count == 0)
        {
            ScriptedScreensFontsPlugin.Log?.LogInfo($"No .ttf or .otf files in \"{folder}\".");
            return;
        }

        var charset = BuildCharacterSet(extraCharacters);

        foreach (var file in files)
        {
            try
            {
                Load(file, charset);
            }
            catch (Exception ex)
            {
                ScriptedScreensFontsPlugin.Log?.LogWarning($"Could not load \"{Path.GetFileName(file)}\": {ex}");
            }
        }
    }

    /// <summary>
    /// Printable ASCII, the Latin-1 supplement (accented European text plus degree,
    /// plus-minus, micro and superscripts), and <see cref="DefaultExtras"/>.
    /// </summary>
    private static List<uint> BuildCharacterSet(string extraCharacters)
    {
        var set = new HashSet<uint>();
        var ordered = new List<uint>();

        void Add(uint c)
        {
            if (set.Add(c))
                ordered.Add(c);
        }

        for (uint c = 0x20; c <= 0x7E; c++)
            Add(c);

        for (uint c = 0xA0; c <= 0xFF; c++)
            Add(c);

        foreach (var c in DefaultExtras)
            Add(c);

        foreach (var c in extraCharacters ?? string.Empty)
        {
            if (!char.IsControl(c))
                Add(c);
        }

        return ordered;
    }

    private static void Load(string path, List<uint> charset)
    {
        if (!_engineReady)
        {
            if (FontEngine.InitializeFontEngine() != FontEngineError.Success)
            {
                ScriptedScreensFontsPlugin.Log?.LogWarning("Could not initialise the font engine.");
                return;
            }

            _engineReady = true;
        }

        var file = Path.GetFileName(path);

        if (FontEngine.LoadFontFace(File.ReadAllBytes(path), SamplingPointSize) != FontEngineError.Success)
        {
            ScriptedScreensFontsPlugin.Log?.LogWarning($"\"{file}\" is not a font file the engine can read.");
            return;
        }

        var faceInfo = FontEngine.GetFaceInfo();
        var fontName = ComposeName(faceInfo);

        if (!FontRegistry.Claim(fontName))
        {
            ScriptedScreensFontsPlugin.Log?.LogWarning($"\"{file}\" declares the name \"{fontName}\", which is already registered; skipping.");
            return;
        }

        var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();

        // Load bearing. ReadFontAssetDefinition treats a version-less asset as pre-1.1 and
        // runs UpgradeFontAsset, which dereferences legacy fields a fresh asset never had.
        asset.version = "1.1.0";

        asset.name = fontName;
        asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
        asset.faceInfo = faceInfo;
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        asset.atlasWidth = AtlasSize;
        asset.atlasHeight = AtlasSize;
        asset.atlasPadding = AtlasPadding;
        asset.atlasRenderMode = GlyphRenderMode.SDFAA;

        var texture = new Texture2D(AtlasSize, AtlasSize, TextureFormat.Alpha8, mipChain: false)
        {
            name = fontName + " Atlas",
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };

        FontEngine.ResetAtlasTexture(texture);
        asset.atlasTextures = new[] { texture };

        // Mobile SDF rather than the desktop shader on purpose: it is the variant that
        // declares _CullMode, which TextMeshProUGUI writes on every canvas update. The
        // desktop shader is why so many of the game's own fonts spam errors in a UI label.
        var material = new Material(ShaderUtilities.ShaderRef_MobileSDF)
        {
            name = fontName + " Material",
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };

        material.SetTexture(ShaderUtilities.ID_MainTex, texture);
        material.SetFloat(ShaderUtilities.ID_TextureWidth, AtlasSize);
        material.SetFloat(ShaderUtilities.ID_TextureHeight, AtlasSize);
        material.SetFloat(ShaderUtilities.ID_GradientScale, AtlasPadding + 1);
        material.SetFloat(ShaderUtilities.ID_WeightNormal, asset.normalStyle);
        material.SetFloat(ShaderUtilities.ID_WeightBold, asset.boldStyle);
        asset.material = material;

        var freeRects = new List<GlyphRect> { new(0, 0, AtlasSize - 1, AtlasSize - 1) };
        var usedRects = new List<GlyphRect>();
        asset.freeGlyphRects = freeRects;
        asset.usedGlyphRects = usedRects;

        // One glyph can back several characters, so indices are de-duplicated before
        // rendering and the characters mapped back onto them afterwards.
        var glyphIndexes = new List<uint>();
        var seenIndexes = new HashSet<uint>();
        var wanted = new List<KeyValuePair<uint, uint>>();

        foreach (var unicode in charset)
        {
            if (!FontEngine.TryGetGlyphIndex(unicode, out var glyphIndex) || glyphIndex == 0)
                continue;

            wanted.Add(new KeyValuePair<uint, uint>(unicode, glyphIndex));
            if (seenIndexes.Add(glyphIndex))
                glyphIndexes.Add(glyphIndex);
        }

        if (glyphIndexes.Count == 0)
        {
            FontRegistry.Release(fontName);
            ScriptedScreensFontsPlugin.Log?.LogWarning($"\"{file}\" has none of the requested characters.");
            return;
        }

        var complete = FontEngine.TryAddGlyphsToTexture(
            glyphIndexes, AtlasPadding, GlyphPackingMode.BestShortSideFit,
            freeRects, usedRects, GlyphRenderMode.SDFAA, texture, out var glyphs);

        var glyphTable = asset.glyphTable;
        var byIndex = new Dictionary<uint, Glyph>();

        foreach (var glyph in glyphs)
        {
            if (glyph == null)
                continue;

            glyph.atlasIndex = 0;
            glyphTable.Add(glyph);
            byIndex[glyph.index] = glyph;
        }

        var characterTable = asset.characterTable;
        foreach (var pair in wanted)
        {
            if (byIndex.TryGetValue(pair.Value, out var glyph))
                characterTable.Add(new TMP_Character(pair.Key, asset, glyph));
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        asset.ReadFontAssetDefinition();
        MaterialReferenceManager.AddFontAsset(asset);

        ScriptedScreensFontsPlugin.Log?.LogInfo(
            $"Font available: <font=\"{fontName}\"> ({characterTable.Count} characters from {file})");

        if (!complete)
        {
            // A static atlas cannot spill into a second texture, so the overflow is simply
            // absent. Say so rather than leaving blank glyphs to be found in game.
            ScriptedScreensFontsPlugin.Log?.LogWarning(
                $"\"{fontName}\" did not fit {AtlasSize}x{AtlasSize}; some characters are missing.");
        }
    }

    /// <summary>
    /// Names the asset the way an author would write it: family alone for the regular
    /// weight, family plus style otherwise, so Barlow-Bold.ttf becomes "Barlow Bold".
    /// </summary>
    private static string ComposeName(FaceInfo faceInfo)
    {
        var family = faceInfo.familyName;
        var style = faceInfo.styleName;

        if (string.IsNullOrEmpty(family))
            return string.IsNullOrEmpty(style) ? "Unnamed" : style;

        if (string.IsNullOrEmpty(style) || style.Equals("Regular", StringComparison.OrdinalIgnoreCase))
            return family;

        return family + " " + style;
    }
}

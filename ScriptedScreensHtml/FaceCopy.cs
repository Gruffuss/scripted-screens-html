using System;
using System.Collections.Generic;
using TMPro;

namespace ScriptedScreensHtml;

/// <summary>
/// Copies TextMeshPro faces into <see cref="FaceData"/> for <see cref="TextMeasure"/>. Game thread
/// only (TMP assets are Unity objects). A face is copied once and again when its character table
/// grows (a dynamic atlas adds characters on request).
/// </summary>
internal static class FaceCopy
{
    private static readonly Dictionary<TMP_FontAsset, (FaceData data, int count)> Copies = new();

    /// <summary>The copy of this asset, made or refreshed now. Registered by name for rich-text font tags.</summary>
    internal static FaceData Of(TMP_FontAsset asset)
    {
        var table = asset.characterLookupTable;
        var count = table?.Count ?? 0;
        if (Copies.TryGetValue(asset, out var known) && known.count == count)
            return known.data;
        // A refreshed face is a new object, so a worker still measuring with the old one keeps a
        // consistent copy. Placed before filling, so a fallback chain that loops back ends here.
        var data = new FaceData { Name = asset.name };
        Copies[asset] = (data, count);
        Fill(data, asset);
        TextMeasure.Register(data);
        return data;
    }

    /// <summary>The copy by face name (the Fonts mod's and the game's registered faces), or null.</summary>
    internal static FaceData? Of(string name)
    {
        var asset = VectorEmitter.AssetOf(name);
        return asset != null ? Of(asset) : null;
    }

    private static void Fill(FaceData data, TMP_FontAsset asset)
    {
        var info = asset.faceInfo;
        data.PointSize = info.pointSize > 0 ? info.pointSize : 90f;
        data.Scale = info.scale > 0f ? info.scale : 1f;
        data.Ascent = info.ascentLine;
        data.Descent = info.descentLine;
        data.LineHeight = info.lineHeight;
        data.CapLine = info.capLine;
        data.SubSize = info.subscriptSize;
        data.SupSize = info.superscriptSize;
        data.TabWidth = info.tabWidth;
        data.TabSize = asset.tabSize;
        data.NormalSpacingOffset = asset.normalSpacingOffset;
        data.BoldSpacing = asset.boldSpacing;

        if (asset.characterLookupTable != null)
            foreach (var kv in asset.characterLookupTable)
            {
                var glyph = kv.Value?.glyph;
                if (glyph == null) continue;
                data.Chars[kv.Key] = new FaceData.Glyph
                {
                    Index = glyph.index,
                    Advance = glyph.metrics.horizontalAdvance,
                    Scale = glyph.scale * kv.Value!.scale,
                };
            }

        // new labels take kerning from TMP's settings
        if (TMP_Settings.enableKerning && asset.fontFeatureTable?.glyphPairAdjustmentRecords != null)
            foreach (var pair in asset.fontFeatureTable.glyphPairAdjustmentRecords)
            {
                var first = pair.firstAdjustmentRecord;
                var second = pair.secondAdjustmentRecord;
                var ignore = (pair.featureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) != 0;
                data.Pairs[(second.glyphIndex << 16) | first.glyphIndex] = (first.glyphValueRecord.xAdvance, second.glyphValueRecord.xAdvance, ignore);
            }

        // the face's own fallback chain, then TMP's global fallbacks and default face, as TMP searches
        var chain = new List<FaceData>();
        void Add(TMP_FontAsset? fb)
        {
            if (fb == null || fb == asset) return;
            var copy = Of(fb);
            if (!chain.Contains(copy)) chain.Add(copy);
        }
        if (asset.fallbackFontAssetTable != null)
            foreach (var fb in asset.fallbackFontAssetTable) Add(fb);
        try
        {
            if (TMP_Settings.fallbackFontAssets != null)
                foreach (var fb in TMP_Settings.fallbackFontAssets) Add(fb);
            Add(TMP_Settings.defaultFontAsset);
        }
        catch (NullReferenceException)
        {
            // TMP settings not loaded yet: the face's own chain only
        }
        data.Fallbacks = chain.ToArray();
    }
}

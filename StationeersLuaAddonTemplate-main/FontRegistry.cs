using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ScriptedScreensFonts;

/// <summary>
/// Makes the game's TMP font assets resolvable by name in rich text tags.
/// </summary>
/// <remarks>
/// TMP resolves <c>&lt;font="X"&gt;</c> in this order:
/// <list type="number">
/// <item><description><c>MaterialReferenceManager</c> lookup by hash of the name</description></item>
/// <item><description><c>Resources.Load&lt;TMP_FontAsset&gt;("Fonts &amp; Materials/" + name)</c></description></item>
/// <item><description>log an error and revert to the current font</description></item>
/// </list>
/// Stationeers fonts come from asset bundles, so step 2 can never find them and the tag
/// always errors. Adding them to step 1 is the whole fix. This is global TMP state, so it
/// applies inside ScriptedScreens labels without patching ScriptedScreens.
/// </remarks>
internal static class FontRegistry
{
    private const string CullModeProperty = "_CullMode";

    private static readonly HashSet<string> Seen = new(StringComparer.Ordinal);

    /// <summary>Font names registered so far, in discovery order.</summary>
    internal static IReadOnlyCollection<string> Names => Seen;

    /// <summary>
    /// Registers every loaded font asset TMP does not already know about.
    /// Idempotent and safe to call repeatedly as asset bundles come in.
    /// </summary>
    /// <returns>How many fonts this call newly registered.</returns>
    internal static int ScanAndRegister()
    {
        var added = 0;

        TMP_FontAsset[] assets;
        try
        {
            assets = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        }
        catch (Exception ex)
        {
            ScriptedScreensFontsPlugin.Log?.LogWarning($"Font scan failed: {ex.Message}");
            return 0;
        }

        foreach (var font in assets)
        {
            if (font == null)
                continue;

            var name = font.name;
            if (string.IsNullOrEmpty(name) || !Seen.Add(name))
                continue;

            try
            {
                // No-ops internally if TMP already has this hash, so no pre-check needed.
                MaterialReferenceManager.AddFontAsset(font);
                added++;
                ScriptedScreensFontsPlugin.Log?.LogInfo($"Font available: <font=\"{name}\">");
                WarnIfUiIncompatible(font, name);
            }
            catch (Exception ex)
            {
                Seen.Remove(name);
                ScriptedScreensFontsPlugin.Log?.LogWarning($"Could not register font \"{name}\": {ex.Message}");
            }
        }

        return added;
    }

    /// <summary>
    /// Reserves a font name for a caller that registers the asset itself.
    /// </summary>
    /// <returns><see langword="false"/> if the name is already taken.</returns>
    internal static bool Claim(string name) => !string.IsNullOrEmpty(name) && Seen.Add(name);

    /// <summary>Gives back a name claimed by <see cref="Claim"/> that went unused.</summary>
    internal static void Release(string name) => Seen.Remove(name);

    /// <summary>
    /// Flags fonts that render correctly but make Unity log an error on every canvas
    /// update when used in a UI label.
    /// </summary>
    /// <remarks>
    /// <c>TextMeshProUGUI</c> pushes <c>_CullMode</c> onto the font's material as part of
    /// its UI culling path. A material whose shader does not declare that property logs
    /// "doesn't have a float or range property '_CullMode'" every time, which is harmless
    /// but floods the screen. World-space <c>TextMeshPro</c> never takes that path, so a
    /// font the game itself uses on signage can still be unusable in a ScriptedScreens
    /// label. Registration is inert either way — only rendering triggers this.
    /// </remarks>
    private static void WarnIfUiIncompatible(TMP_FontAsset font, string name)
    {
        try
        {
            var material = font.material;
            if (material == null || material.HasProperty(CullModeProperty))
                return;

            ScriptedScreensFontsPlugin.Log?.LogWarning(
                $"Font \"{name}\" material \"{material.name}\" (shader \"{material.shader?.name}\") " +
                $"has no {CullModeProperty}; using it in a UI label spams Unity errors.");
        }
        catch (Exception ex)
        {
            ScriptedScreensFontsPlugin.Log?.LogWarning($"Could not inspect material for \"{name}\": {ex.Message}");
        }
    }
}

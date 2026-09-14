using BepInEx.Logging;
using HarmonyLib;
using StationeersMods.Interface;
using UnityEngine;

namespace ScriptedScreensHtml;

/// <summary>LaunchPad entrypoint. Client-side only.</summary>
[StationeersMod(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION_CONST)]
public sealed class ScriptedScreensHtmlPlugin : ModBehaviour
{
    private static bool _initialized;
    private static Harmony? _harmony;

    internal static ManualLogSource? Log { get; private set; }

    public override void OnLoaded(ContentHandler contentHandler)
    {
        base.OnLoaded(contentHandler);
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            Log = new ManualLogSource(PluginInfo.PLUGIN_NAME);
            try
            {
                BepInEx.Logging.Logger.Sources.Add(Log);
            }
            catch
            {
                // Logging is non-essential.
            }

            if (Application.isBatchMode)
            {
                Log.LogInfo("Headless server detected, skipping html renderer.");
                return;
            }

            // base.OnLoaded created Config; LaunchPad surfaces that instance in its settings UI.
            if (Config != null)
                HtmlConfig.Load(Config);
            Log.LogInfo(Config != null ? "Diagnostics settings registered with LaunchPad." : "No ConfigFile from LaunchPad; diagnostics stay off.");

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(HtmlElementPatch).Assembly);
            Log.LogInfo($"Patched ScriptedScreens; element type \"{HtmlElementPatch.ElementType}\" is live.");
        }
        catch (System.Exception ex)
        {
            Log?.LogError($"Html layer failed to install: {ex}");
        }
    }
}

using BepInEx.Logging;
using StationeersMods.Interface;
using UnityEngine;

namespace ScriptedScreensFonts;

/// <summary>
/// LaunchPad entrypoint. Client-side only: rendering happens on the client, and a headless
/// server has no TMP font assets to register.
/// </summary>
[StationeersMod(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION_CONST)]
public sealed class ScriptedScreensFontsPlugin : ModBehaviour
{
    private static bool _initialized;

    internal static ManualLogSource? Log { get; private set; }

    /// <inheritdoc />
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
                // Logger already disposed or source name taken; logging is non-essential.
            }

            if (Application.isBatchMode)
            {
                Log.LogInfo("Headless server detected, skipping font registration.");
                return;
            }

            // Config must bind into ModBehaviour.Config: that is the instance LaunchPad hands
            // to its settings UI. A private ConfigFile writes a valid .cfg nobody ever sees.
            var extraCharacters = Config.Bind(
                "Fonts",
                "ExtraCharacters",
                "",
                "Characters to include beyond ASCII and Latin-1. The atlas is built once at load "
                + "and cannot grow, so anything absent here will not render in a loaded font.");

            var modDirectory = System.IO.Path.GetDirectoryName(typeof(ScriptedScreensFontsPlugin).Assembly.Location);
            if (!string.IsNullOrEmpty(modDirectory))
                FontLoader.Configure(modDirectory, extraCharacters.Value);

            FontRegistryLoader.Install();
            Log.LogInfo("Watching for game fonts to register with TextMeshPro.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError(ex);
        }
    }
}

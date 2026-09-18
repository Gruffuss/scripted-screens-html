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
            ApplyGCSlice();

            _harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            _harmony.PatchAll(typeof(HtmlElementPatch).Assembly);
            Log.LogInfo($"Patched ScriptedScreens; element type \"{HtmlElementPatch.ElementType}\" is live.");
            HtmlDocsTool.TryRegister();
        }
        catch (System.Exception ex)
        {
            Log?.LogError($"Html layer failed to install: {ex}");
        }
    }

    /// <summary>
    /// Unity's incremental collector marks for at most a few milliseconds per frame (the game ships
    /// gc-max-time-slice=3); when it cannot keep up it falls back to one long pause. A page that
    /// animates makes garbage every frame, so the setting is worth raising while one is running —
    /// but it is the whole game's setting, so it stays opt-in and says what it did.
    /// </summary>
    private static void ApplyGCSlice()
    {
        try
        {
            var want = HtmlConfig.GCTimeSliceMs;
            var incremental = UnityEngine.Scripting.GarbageCollector.isIncremental;
            var now = UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds;
            if (want <= 0f)
            {
                Log?.LogInfo($"GC: incremental {incremental}, slice {now / 1000000.0:0.##} ms (left as the game set it).");
                return;
            }
            if (!incremental)
            {
                Log?.LogWarning("GC: this build is not running the incremental collector; GCTimeSliceMs does nothing.");
                return;
            }
            UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds = (ulong)(want * 1000000f);
            Log?.LogInfo($"GC: incremental slice {now / 1000000.0:0.##} ms -> {UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds / 1000000.0:0.##} ms (Performance.GCTimeSliceMs).");
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"GC: could not read or set the incremental slice: {ex.Message}");
        }
    }
}

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
            ReportGC();
            FrameAlloc.Report();
            if (HtmlConfig.ProbeV8) V8Probe.Run();

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
    /// What collector the game is running, logged once. Stationeers ships gc-max-time-slice=3, so
    /// Unity's incremental collector is on; raising that slice was measured and changed nothing,
    /// and a mod has no business reaching further into a setting the whole game shares.
    /// </summary>
    /// <summary>
    /// The diagnostics heartbeat. A surface drives it too, but with every console blank there is no
    /// surface and the heap line stops - which is exactly the reading needed to tell the game's own
    /// allocation from ours. Costs a comparison per frame when diagnostics are off.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Unity calls Update only on an instance method; making it static silently stops it running.")]
    // The heap is sampled here rather than in ReportIfDue because that runs once a report, and an
    // allocation rate needs every frame: a sample taken seconds apart hides the collections between.
    private void Update()
    {
        // Only while something is reading it. GC.GetTotalMemory is not a counter read on Mono, and
        // this ran every frame whether or not a console was on, whether or not diagnostics were
        // enabled - measured in game as about 1 ms a frame against a 2.5 GB heap, which is 7% of a
        // frame, paid by everyone, to feed a number nobody was looking at.
        if (HtmlConfig.Diagnostics) FrameAlloc.SampleHeap();
        HtmlSurface.ReportIfDue();
    }

    private static void ReportGC()
    {
        try
        {
            Log?.LogInfo($"GC: incremental {UnityEngine.Scripting.GarbageCollector.isIncremental}, slice {UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds / 1000000.0:0.##} ms.");
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"GC: could not read the collector's settings: {ex.Message}");
        }
    }
}

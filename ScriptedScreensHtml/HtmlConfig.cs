using BepInEx.Configuration;

namespace ScriptedScreensHtml;

/// <summary>
/// Settings in <c>BepInEx/config/&lt;ModID&gt;.cfg</c>, shown by LaunchPad's settings UI.
/// </summary>
/// <remarks>
/// Same shape as the vector mod's <c>VectorConfig</c>, for the same reason: it binds into
/// <c>ModBehaviour.Config</c>, the one <c>ConfigFile</c> LaunchPad reports to its UI. A
/// privately constructed file writes a valid .cfg that LaunchPad never shows. Values are
/// read live, so editing the file takes effect without a restart.
/// </remarks>
/// <summary>Who decides whether a page out of view keeps running.</summary>
internal enum CullChoice
{
    /// <summary>Whatever the vector mod does with its own rebuilds.</summary>
    FollowVectorMod,
    Always,
    Never,
}

internal static class HtmlConfig
{
    private static ConfigFile? _file;
    private static ConfigEntry<bool>? _diagnostics;
    private static ConfigEntry<bool>? _dumpScenes;
    private static ConfigEntry<CullChoice>? _cullOffScreen;

    /// <summary>
    /// Per-page statistics every five seconds and the informational log lines (page built,
    /// scene emitted, script started). Off by default: warnings and errors always log, and
    /// the rest is noise in an ordinary session. The instrumentation stays compiled in.
    /// </summary>
    internal static bool Diagnostics => OffThread.Active ? OffThread.Job.Diagnostics : Fresh() && (_diagnostics?.Value ?? false);

    private static DateTime _seenWrite;
    private static float _nextPoll;
    /// <summary>The file is re-read when it changes on disk (checked every 5 s), so a flag flipped in a text editor takes effect without a restart.</summary>
    private static bool Fresh()
    {
        if (_file == null) return true;
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (now < _nextPoll) return true;
        _nextPoll = now + 5f;
        try
        {
            var w = System.IO.File.GetLastWriteTimeUtc(_file.ConfigFilePath);
            if (w != _seenWrite) { _seenWrite = w; _file.Reload(); }
        }
        catch (Exception) { }
        return true;
    }

    /// <summary>
    /// Write the last emitted scene text of every page to <c>scenes/&lt;page&gt;.txt</c> beside
    /// the mod DLL. Answers "what did the emitter produce" without a debugger.
    /// </summary>
    internal static bool DumpScenes => _dumpScenes?.Value ?? false;

    internal static void Load(ConfigFile file)
    {
        if (_file != null || file == null)
            return;
        _file = file;

        _diagnostics = _file.Bind(
            "Diagnostics", "Enabled", false,
            "Log a cost line per page every second, plus the informational lines (page built, " +
            "script started, why a scene was rebuilt). Development tool; leave off for normal play. " +
            "Lines look like: 'html \"gas\": 2.0 emits/s, last 1.4 ms (layout 0.6 + copy 0.1, " +
            "translate 0.7; page thread 3.0 ms/s; ...), 226 nodes / 43 KB, ...'.");

        _dumpScenes = _file.Bind(
            "Diagnostics", "DumpScenes", false,
            "Write each page's vector scene to scenes/<page>.txt next to the mod DLL (and its " +
            "layout to scenes/<page>-layout.txt): on every new structure, and at most every two " +
            "seconds while only values change. Development tool for reading exactly what the " +
            "translation produced; each file is overwritten, not appended. Leave it off for normal play.");

        _cullOffScreen = _file.Bind(
            "Performance", "CullOffScreen", CullChoice.FollowVectorMod,
            "Stop laying out, running and translating a page whose console nobody can see, and run " +
            "it a couple of times a second instead so its clock, timers and data keep up; it emits " +
            "at once when it comes back into view. A screen capture is unaffected either way: that " +
            "path translates the page there and then. FollowVectorMod takes the answer from the " +
            "vector mod's own CullOffScreen, since a page only needs a frame that mod will draw; " +
            "Always and Never decide it here instead.");

    }

    /// <summary>Whether a page whose console is out of view drops to a heartbeat.</summary>
    internal static CullChoice CullOffScreen => _cullOffScreen?.Value ?? CullChoice.FollowVectorMod;
}

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
    private static ConfigEntry<bool>? _ablateLua;
    private static ConfigEntry<bool>? _ablateSend;
    private static ConfigEntry<bool>? _snapData;
    private static ConfigEntry<CullChoice>? _cullOffScreen;
    private static ConfigEntry<bool>? _verifyEmitCache;
    private static ConfigEntry<bool>? _probeV8;
    private static ConfigEntry<bool>? _compileProbe;
    private static ConfigEntry<bool>? _runCompiled;
    private static ConfigEntry<bool>? _useV8;

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

    /// <summary>
    /// Ablation for a compiled page: skip the chip's Lua, or skip the send, and read the difference
    /// in allocation off the frame line.
    /// </summary>
    /// <remarks>
    /// Guessing which half of a per-frame cost is the expensive one has a poor record on this
    /// project. A three-way stopwatch answered where the TIME goes; it says nothing about where the
    /// ALLOCATION goes, and Mono reports 0 for the per-thread counter so nothing finer is available
    /// in game. Subtraction is. Turn one off, read `alloc MB/s`, put it back.
    /// </remarks>
    internal static bool AblateLua => _ablateLua?.Value ?? false;
    internal static bool AblateSend => _ablateSend?.Value ?? false;
    /// <summary>
    /// Whether forwarded values snap or blend. On (the default) a value moves at once and the scene
    /// goes static between payloads; off, the renderer eases each value over its blend window and
    /// keeps rebuilding the mesh while it does. Built with snap on 2026-09-22 and never measured
    /// either way (BUGS #1); a live toggle so the two can be A/B'd in one session without a restart.
    /// </summary>
    internal static bool SnapData => _snapData?.Value ?? true;

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

        _ablateLua = _file.Bind(
            "Diagnostics", "AblateLua", false,
            "Measurement only. Stop running a compiled page's Lua while still draining and sending " +
            "whatever it last produced. The console freezes; the point is to read `alloc MB/s` off " +
            "the frame line with that half removed and subtract. Leave it off.");

        _ablateSend = _file.Bind(
            "Diagnostics", "AblateSend", false,
            "Measurement only. Run a compiled page's Lua and drain it, but do not hand the values " +
            "to the renderer. The console freezes; the difference in `alloc MB/s` is what the send " +
            "path costs. Leave it off.");

        _snapData = _file.Bind(
            "Renderer", "SnapData", true,
            "Whether a forwarded value snaps to its new value (true) or eases over the renderer's " +
            "blend window (false). Snapped, the scene is static between payloads; eased, the mesh " +
            "rebuilds while the value moves. Live: change it and the next payload uses it.");

        _verifyEmitCache = _file.Bind(
            "Diagnostics", "VerifyEmitCache", false,
            "Every second, translate the page a second time without reusing anything an element kept " +
            "from the frame before, and log it if the two differ. The emitter reuses a subtree's text " +
            "while nothing it reads has changed, which is what makes an animated page cheap; if it " +
            "ever reuses text whose input it cannot see, a console shows something stale and nothing " +
            "else would say so. Development tool: it doubles the translation cost while it is on.");

        _cullOffScreen = _file.Bind(
            "Performance", "CullOffScreen", CullChoice.FollowVectorMod,
            "Stop laying out, running and translating a page whose console nobody can see, and run " +
            "it a couple of times a second instead so its clock, timers and data keep up; it emits " +
            "at once when it comes back into view. A screen capture is unaffected either way: that " +
            "path translates the page there and then. FollowVectorMod takes the answer from the " +
            "vector mod's own CullOffScreen, since a page only needs a frame that mod will draw; " +
            "Always and Never decide it here instead.");

        _useV8 = _file.Bind(
            "Performance", "UseV8", false,
            "Run page scripts on V8 instead of the built-in C# interpreter. V8 keeps its objects in " +
            "a native heap Unity never walks, so a page's JavaScript stops feeding the collector that " +
            "causes the stutter - measured outside the game at 252 bytes a frame against 23,998. It " +
            "needs ClearScript's DLLs beside the plugin; without them a page says so in the log and " +
            "runs on the interpreter as before.");

        _probeV8 = _file.Bind(
            "Diagnostics", "ProbeV8", false,
            "Once at startup, try to load ClearScript's V8 from the mod folder and report what a " +
            "page frame costs the heap Unity collects. Does nothing unless ClearScript's DLLs have " +
            "been copied in by hand, and nothing in the mod uses them either way. Development tool " +
            "for deciding whether a JS engine with its own native heap is worth the dependency.");

        _runCompiled = _file.Bind(
            "Performance", "RunCompiled", false,
            "Compile a page once and let its chip run it, instead of laying it out, running its " +
            "script and translating it on every frame. The page becomes a vector scene whose values " +
            "are named slots plus Lua that writes them, and this mod stops doing anything per frame " +
            "for it. A page using something the compiler will not translate keeps running the way it " +
            "does now and says so in the log. The author's own program is never modified - the " +
            "compiled page is a separate chunk in the same VM with its own environment.");

        _compileProbe = _file.Bind(
            "Diagnostics", "CompileProbe", false,
            "When a page is built, also compile its script to Lua and say in the log whether that " +
            "worked and, if not, which line stopped it. Changes nothing a console shows - the page " +
            "runs exactly as it does now. This is the compiler that will eventually replace the " +
            "interpreter, and this is how it gets checked against real pages in the game before " +
            "anything depends on it.");
    }

    /// <summary>Whether page scripts run on V8 rather than the C# interpreter.</summary>
    internal static bool UseV8 => _useV8?.Value ?? false;

    /// <summary>Whether to try loading ClearScript's V8 once at startup and report what it costs.</summary>
    internal static bool ProbeV8 => _probeV8?.Value ?? false;

    /// <summary>Whether to compile each page's script to Lua at build time and report the outcome.</summary>
    internal static bool CompileProbe => _compileProbe?.Value ?? false;

    /// <summary>Whether a page that compiles is handed to its chip and stops doing per-frame work here.</summary>
    internal static bool RunCompiled => _runCompiled?.Value ?? false;

    /// <summary>Whether to re-translate each page once a second and check the reuse against it.</summary>
    internal static bool VerifyEmitCache => _verifyEmitCache?.Value ?? false;

    /// <summary>Whether a page whose console is out of view drops to a heartbeat.</summary>
    internal static CullChoice CullOffScreen => _cullOffScreen?.Value ?? CullChoice.FollowVectorMod;
}

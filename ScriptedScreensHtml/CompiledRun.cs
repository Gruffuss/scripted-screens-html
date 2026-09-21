using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// One page, running compiled: its Lua is in the chip, and this drives it.
/// </summary>
/// <remarks>
/// While this exists, the HTML side of that page does nothing per frame. It does not lay out, run a
/// script, translate or split - the scene was emitted once and the values now come from Lua writing
/// slots directly. That is the whole point of compiling, and it is why the surface releases the
/// page's working set once one of these takes over.
///
/// <b>The mod itself never goes anywhere.</b> It stays loaded and stays patched, because a new chip
/// carrying an HTML page can appear at any moment and has to be claimed and compiled like any other.
/// What is released is one page's DOM, cascade, layout tree and script engine - not the compiler.
/// </remarks>
internal sealed class CompiledRun
{
    private readonly object _state;
    private readonly object _frame;
    private readonly string _page;
    private float _last;
    private int _failures;
    // Enough to tell "it never runs" from "it runs and writes nothing" from "it writes and nothing
    // shows". Three different bugs that look identical on a frozen console.
    private int _ran, _blocked, _sent, _empty;
    private float _nextReport;

    /// <summary>After this many failed frames in a row the page is handed back to the interpreter.</summary>
    private const int GiveUpAfter = 30;

    private void Report()
    {
        if (!HtmlConfig.Diagnostics || Time.time < _nextReport) return;
        _nextReport = Time.time + 2f;
        ScriptedScreensHtmlPlugin.Log?.LogInfo(
            $"compiled \"{_page}\": {_ran} frame(s) ran, {_blocked} blocked, {_sent} value(s) sent, {_empty} frame(s) wrote nothing");
        _ran = _blocked = _sent = _empty = 0;
    }

    private readonly object _env;

    private CompiledRun(object state, object env, object frame, string page)
    {
        _state = state; _env = env; _frame = frame; _page = page;
    }

    /// <summary>The chip's Lua state, so the caller can notice when it is replaced.</summary>
    internal object State => _state;

    /// <summary>
    /// Compiles a page and puts it in its chip, or returns null with the reason logged.
    /// </summary>
    /// <remarks>
    /// Every step here can fail for an ordinary reason and each says so rather than throwing: the
    /// chip may not have compiled yet, the page may use something the compiler refuses, or this
    /// install may simply not have StationeersLua. A page that cannot be compiled keeps running the
    /// way it does today, which is the whole reason this is safe to turn on.
    /// </remarks>
    internal static CompiledRun? Start(string page, object? cartridge, HtmlRenderer.Result built,
                                       Panel panel, Vector2 size,
                                       System.Collections.Generic.IReadOnlyDictionary<string, SceneSlots.Value> slots)
    {
        if (!ChipHost.Available) return null;           // reported once at startup
        if (string.IsNullOrWhiteSpace(built.Script)) return null;

        var chip = ChipHost.ChipOf(cartridge);
        if (chip == null)
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: \"{page}\" has no programmable chip to run on");
            return null;
        }
        var state = ChipHost.StateOf(chip);
        if (state == null)
        {
            // Ordinary the first time - a page is built before its chip has compiled - so this is
            // said once per page rather than every frame, and the caller retries on the next build.
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: \"{page}\" - its chip has not compiled yet; will try again");
            return null;
        }

        var compiled = PageCompiler.Compile(built, panel, size, slots);
        if (!compiled.Ok)
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo(
                $"html: \"{page}\" stays on the interpreter - " +
                (compiled.Lua == null
                    ? string.Join("; ", compiled.Problems.GetRange(0, Math.Min(3, compiled.Problems.Count)))
                    : string.Join("; ", compiled.Unmapped.GetRange(0, Math.Min(3, compiled.Unmapped.Count)))));
            return null;
        }

        var (env, frame) = ChipHost.LoadInto(state, compiled.Lua!, "@html:" + page);
        if (env == null || frame == null)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: \"{page}\" compiled but defines no frame function");
            return null;
        }

        ScriptedScreensHtmlPlugin.Log?.LogInfo(
            $"html: \"{page}\" is running compiled - {compiled.Bindings.Count} slot binding(s); " +
            "the page no longer lays out, runs a script or translates");
        return new CompiledRun(state, env, frame, page);
    }

    /// <summary>
    /// Runs one frame. Returns false when this page has to go back to the interpreter - the chip
    /// recompiled under it, or its frames keep failing.
    /// </summary>
    /// <param name="send">Given whatever the frame wrote, when it wrote anything.</param>
    internal bool Tick(object? cartridge, Action<System.Collections.Generic.Dictionary<string, object>> send)
    {
        // A chip that recompiles gets a brand new LuaState, and the chunk went with the old one.
        // Silently doing nothing would leave a console frozen with no clue why.
        var now = ChipHost.StateOf(ChipHost.ChipOf(cartridge));
        if (now == null || !ReferenceEquals(now, _state))
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: \"{_page}\" lost its chip's Lua state; recompiling");
            return false;
        }

        var dt = _last > 0f ? Time.time - _last : 0f;
        _last = Time.time;

        if (ChipHost.RunFrame(_state, _frame, dt))
        {
            _failures = 0;
            _ran++;
            var values = ChipHost.Drain(_env);
            if (values != null) { _sent += values.Count; send(values); } else _empty++;
            Report();
            return true;
        }
        _blocked++;
        Report();

        // One failure is ordinary - the state was busy, or the game is paused. A run of them is not.
        return ++_failures < GiveUpAfter;
    }
}

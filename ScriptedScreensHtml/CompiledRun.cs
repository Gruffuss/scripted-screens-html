using System;
using System.Collections.Generic;
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
    private int _ran, _blocked, _empty;
    // Split, because "the compiled page costs 6 ms a frame" is a number with three possible owners
    // and guessing which has a bad record on this project. Run is the chip's Lua; drain is reading
    // PAYLOAD back; send is handing the values to the vector mod.
    private long _tRun;
    private bool _noted;
    /// <summary>Elements already named as undrawable, so each is said once rather than every frame.</summary>
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private float _nextReport;

    /// <summary>After this many failed frames in a row the page is handed back to the interpreter.</summary>
    private const int GiveUpAfter = 30;

    private void Report()
    {
        if (!HtmlConfig.Diagnostics || Time.time < _nextReport) return;
        _nextReport = Time.time + 2f;
        ScriptedScreensHtmlPlugin.Log?.LogInfo(
            $"compiled \"{_page}\": {_ran} frame(s) ran, {_blocked} blocked, " +
            $"{_empty} frame(s) wrote nothing, {_events} event(s) delivered" +
            (_ran > 0
                ? $"; {Ms(_tRun) / _ran:0.000} ms of lua per frame, nothing sent from here"
                : string.Empty));
        _ran = _blocked = _empty = _events = 0;
        _tRun = 0;
    }

    private readonly object _env;
    private readonly object? _event;
    private int _events;

    private CompiledRun(object state, object env, object frame, string page)
    {
        _state = state; _env = env; _frame = frame; _page = page;
        _event = ChipHost.FunctionIn(env, "event");
    }

    /// <summary>
    /// Hands the player's click to the page, and sends whatever it changed.
    /// </summary>
    /// <remarks>
    /// A browser turns one press into mousedown, mouseup and click, and pages rely on which one
    /// they get: the runner jumps on <c>mousedown</c> specifically, "a jump starts on the press,
    /// not on the release a click waits for" - so delivering only <c>click</c> would leave it
    /// looking as dead as delivering nothing.
    /// </remarks>
    internal bool Click(string id, float x, float y)
    {
        if (_event == null) return false;
        var any = ChipHost.RunEvent(_state, _event, id, "mousedown", x, y);
        any |= ChipHost.RunEvent(_state, _event, id, "mouseup", x, y);
        any |= ChipHost.RunEvent(_state, _event, id, "click", x, y);
        if (!any) return false;
        _events++;
        return true;   // the chunk's own flush sends whatever the handler changed
    }

    /// <summary>One pointer event, for the types a page uses to track a held button.</summary>
    internal bool Pointer(string id, string kind, float x, float y)
    {
        if (_event == null || !ChipHost.RunEvent(_state, _event, id, kind, x, y)) return false;
        _events++;
        return true;
    }

    private static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    /// <summary>The chip's Lua state, so the caller can notice when it is replaced.</summary>
    internal object State => _state;

    /// <summary>
    /// Slots the scene should carry as expressions of <c>t</c> rather than as values.
    /// </summary>
    /// <remarks>
    /// The page's motion, worked out at compile time. Nothing writes these and nothing sends them:
    /// the caller substitutes each into the template before the structure goes out, and the renderer
    /// evaluates them on its own worker for the rest of the console's life.
    /// </remarks>
    internal IReadOnlyDictionary<string, string> Expressions { get; private set; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Why a page did not compile: every list that has something in it, capped and counted.</summary>
    private static string Reasons(CompiledPage.Result r)
    {
        var parts = new System.Collections.Generic.List<string>(2);
        Add("cannot translate", r.Problems);
        Add("no slot for", r.Unmapped);
        return parts.Count > 0 ? string.Join("; ", parts) : "no reason recorded, which is itself a bug";

        void Add(string what, System.Collections.Generic.List<string> list)
        {
            if (list.Count == 0) return;
            var head = string.Join("; ", list.GetRange(0, Math.Min(3, list.Count)));
            parts.Add(list.Count > 3 ? $"{what}: {head} (+{list.Count - 3} more)" : $"{what}: {head}");
        }
    }

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
                                       System.Collections.Generic.IReadOnlyDictionary<string, SceneSlots.Value> slots,
                                       (string Surface, string Element, string Scene) target)
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

        var compiled = PageCompiler.Compile(built, panel, size, slots, target);
        if (!compiled.Ok)
        {
            // Both lists, not whichever one a `Lua == null` test guesses at. A page that translated
            // but has a Problem printed the EMPTY unmapped list, so the log read
            // "stays on the interpreter - " with nothing after it - the one thing a reason line
            // must never do.
            ScriptedScreensHtmlPlugin.Log?.LogInfo(
                $"html: \"{page}\" stays on the interpreter - {Reasons(compiled)}");
            return null;
        }

        var (env, frame) = ChipHost.LoadInto(state, compiled.Lua!, "@html:" + page);
        if (env == null || frame == null)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: \"{page}\" compiled but defines no frame function");
            return null;
        }

        ScriptedScreensHtmlPlugin.Log?.LogInfo(
            $"html: \"{page}\" is running compiled - {compiled.Bindings.Count} slot binding(s), " +
            $"{compiled.Expressions.Count} slot(s) as scene expressions; " +
            "the page no longer lays out, runs a script or translates");
        return new CompiledRun(state, env, frame, page) { Expressions = compiled.Expressions };
    }

    /// <summary>
    /// Runs one frame. Returns false when this page has to go back to the interpreter - the chip
    /// recompiled under it, or its frames keep failing.
    /// </summary>
    /// <param name="send">Given whatever the frame wrote, when it wrote anything.</param>
    internal bool Tick(object? cartridge)
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

        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        // Ablation, measurement only: subtraction is the one honest instrument left, because Mono
        // reports 0 for the per-thread allocation counter and a stopwatch answers a different
        // question than "what allocates".
        var ok = HtmlConfig.AblateLua || ChipHost.RunFrame(_state, _frame, dt);
        var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (ok)
        {
            _failures = 0;
            _ran++;
            // Nothing is drained and nothing is sent. The chunk writes its own values straight to
            // the vector element through ScriptedScreens' API, exactly as a hand-written console
            // does, so the payload never crosses back into C# and this mod is not in the path.
            _tRun += t1 - t0;
            if (!_noted && ChipHost.NoteIn(_env) is { } note)
            {
                _noted = true;
                ScriptedScreensHtmlPlugin.Log?.LogInfo($"html: \"{_page}\" send path - {note}");
            }
            // An element the page writes to that the scene has no shape for. Named once each: a page
            // that creates nodes writes to every one of them on every frame, and the point is to say
            // WHICH element is not being drawn, not to say it again forty times a second.
            if (ChipHost.MissingIn(_env) is { } missing)
                foreach (var id in missing)
                    if (_reported.Add(id))
                        ScriptedScreensHtmlPlugin.Log?.LogWarning(
                            $"html: \"{_page}\" writes to \"{id}\", which the compiled scene does not draw - " +
                            "an element the script created after the page was laid out has no shape to write into");
            Report();
            return true;
        }
        _blocked++;
        Report();

        // One failure is ordinary - the state was busy, or the game is paused. A run of them is not.
        return ++_failures < GiveUpAfter;
    }
}

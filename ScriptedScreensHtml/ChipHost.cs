using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensHtml;

/// <summary>
/// Puts a compiled page into the Lua VM of the chip that owns it, without touching what the player
/// wrote.
/// </summary>
/// <remarks>
/// <b>The author's program is never modified.</b> Not the source on the chip, not the source in the
/// save, not the source in the editor. The compiled page is a second tenant of the same VM: it is
/// loaded as its own chunk with its own <c>_ENV</c>, so it cannot read or clobber the author's
/// globals, and it disappears when the page does. StationeersLua does have a call that installs
/// source into a chip - <c>UpdateSourceServerSide</c> - and it is explicitly NOT used here: it
/// builds a whole new runtime and replaces the old one, which would destroy the player's program.
///
/// <b>And the author's <c>on_frame</c> is never taken.</b> ScriptedScreens' own
/// <c>FrameCallbackManager.Register</c> writes <c>_callbacks[referenceId] = data</c> - one callback
/// per chip, so registering would silently evict whatever the author registered, and there is no
/// API to chain. So the compiled page is driven from this mod's own update instead, with the same
/// guards that manager applies. It costs more code and it is the only route that does not collide.
///
/// Three reflection hops reach the chip's state, because StationeersLua keeps its runtime table
/// private. Everything else is a direct call: <c>Lua.dll</c> is a referenced assembly and
/// ScriptedScreens is already publicised.
/// </remarks>
internal static class ChipHost
{
    private static readonly Type? ManagerType = FindManager();
    private static readonly FieldInfo? RuntimesField =
        ManagerType?.GetField("Runtimes", BindingFlags.Static | BindingFlags.NonPublic);
    private static FieldInfo? _stateField;
    private static bool _complained;

    /// <summary>
    /// The runtime manager, found by walking the loaded assemblies rather than by
    /// <c>Type.GetType</c>. Under BepInEx a plugin assembly is not necessarily resolvable by name
    /// through the default load context, so the qualified-name lookup returns null and every page
    /// silently declines to compile with nothing in the log to say why.
    /// </summary>
    private static Type? FindManager()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, "StationeersLua", StringComparison.Ordinal)) continue;
            try { return assembly.GetType("StationeersLua.LuaChipRuntimeManager", throwOnError: false); }
            catch (Exception) { return null; }
        }
        return null;
    }

    /// <summary>Whether the Lua side is reachable at all on this install.</summary>
    internal static bool Available => RuntimesField != null;

    /// <summary>Says once, at startup, whether a page can be handed to its chip and what is missing if not.</summary>
    internal static void Report()
    {
        if (Available)
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo("html: the chip's Lua is reachable; a page that compiles can run on it");
            return;
        }
        ScriptedScreensHtmlPlugin.Log?.LogWarning(
            "html: cannot reach StationeersLua's runtime table, so no page can be handed to its chip - " +
            (ManagerType == null
                ? "LuaChipRuntimeManager was not found among the loaded assemblies"
                : "the Runtimes field was not found on it"));
    }

    /// <summary>
    /// The root Lua state of a chip, or null when it has not compiled yet. Null is ordinary and
    /// means "try again later", not "broken": a page is built before its chip has run.
    /// </summary>
    internal static object? StateOf(object? chip)
    {
        if (chip == null || RuntimesField == null) return null;
        try
        {
            var id = chip.GetType().GetProperty("ReferenceId")?.GetValue(chip);
            if (id == null) return null;

            // ConcurrentDictionary implements the non-generic IDictionary, so the private nested
            // runtime type never has to be named.
            if (RuntimesField.GetValue(null) is not IDictionary runtimes) return null;
            var runtime = runtimes[id];
            if (runtime == null) return null;

            _stateField ??= runtime.GetType().GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
            return _stateField?.GetValue(runtime);
        }
        catch (Exception ex)
        {
            if (!_complained)
            {
                _complained = true;
                ScriptedScreensHtmlPlugin.Log?.LogWarning(
                    $"html: cannot reach a chip's Lua state, so no page can be compiled into one: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// Loads a compiled page's chunk into a chip's running VM and returns its frame function.
    /// </summary>
    /// <remarks>
    /// The chunk gets its OWN environment table, whose metatable falls through to the chip's
    /// globals. So it can read <c>ic</c>, <c>math</c> and everything the author has - and every
    /// name it writes stays in its own table, where the author's program cannot see it and it
    /// cannot clobber the author's. The author's source is not read, not parsed and not touched.
    ///
    /// It is run through a protected coroutine and the stack is rewound afterwards, which is the
    /// pattern StationeersLua itself uses to inject its require bootstrap into a live chip.
    /// </remarks>
    internal static (object? Env, object? Frame) LoadInto(object? state, string lua, string chunkName)
    {
        if (state is not Lua.LuaState chip) return (null, null);
        try
        {
            var env = new Lua.LuaTable();
            var meta = new Lua.LuaTable();
            meta["__index"] = chip.Environment;
            env.Metatable = meta;

            var closure = chip.Load(lua.AsSpan(), chunkName, env);

            // Deliberately NOT a protected coroutine. Protection turns a failure into a quiet
            // `false, message` on the stack, and reading that back was more code than letting the
            // exception reach the catch below - where it is logged in full. A chunk that failed and
            // a chunk that ran and defined nothing look identical otherwise, which is the least
            // useful thing a log can say.
            var stack = chip.Stack;
            var baseline = stack.Count;
            var running = chip.RunAsync(closure, default);
            if (!running.IsCompleted) running.AsTask().GetAwaiter().GetResult();

            // Stack hygiene is not optional: whatever the chunk left behind would otherwise sit
            // under the author's next call.
            if (stack.Count > baseline) stack.PopUntil(baseline);

            return env["frame"].TryRead<Lua.LuaFunction>(out var frame) ? (env, frame) : (env, null);
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogError($"html: could not load the compiled page into its chip - {ex}");
            return (null, null);
        }
    }

    /// <summary>
    /// Runs a compiled page's frame function once, with the guards the game's own frame runner
    /// applies. Returns false when it could not run, which is ordinary and means "not this frame".
    /// </summary>
    /// <remarks>
    /// Driven from this mod rather than registered with ScriptedScreens' FrameCallbackManager,
    /// which keeps one callback per chip and would evict whatever the author registered. The
    /// guards that manager applies have to be repeated here instead, and each of them matters:
    /// a call while the state is already running corrupts it; a call while the chip's tick is
    /// suspended mid-yield is the thing that halts a chip outright; and a frame function that does
    /// not finish synchronously has to be abandoned rather than awaited.
    /// </remarks>
    internal static bool RunFrame(object? state, object? frame, float dt, int budget = 200000)
    {
        if (state is not Lua.LuaState chip || frame is not Lua.LuaFunction fn) return false;
        if (chip.IsRunning) return false;

        try
        {
            var stack = chip.Stack;
            var baseline = stack.Count;
            stack.Push(dt);
            var running = chip.RunAsync(fn, 1, default);
            if (!running.IsCompleted)
            {
                // It yielded or blocked. Both are fatal to a shared state, so it is dropped here
                // rather than waited on.
                if (stack.Count > baseline) stack.PopUntil(baseline);
                return false;
            }
            running.GetAwaiter().GetResult();
            if (stack.Count > baseline) stack.PopUntil(baseline);
            return true;
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: a compiled page's frame failed - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Hands a payload the chip's own Lua sent as data to a compiled page's <c>data</c> event.
    /// </summary>
    /// <remarks>
    /// As a Lua table, not JSON: the chunk lives in the same VM, so the values go straight in. The
    /// tables are this page's own and are refilled in place each payload - a key the payload no
    /// longer carries is removed - so a tick allocates nothing here once the shape has been seen.
    /// The same guards as an event: a payload that lands while the chip is mid-call is dropped
    /// rather than pushed onto a running state, and the next one carries the same keys.
    /// ponytail: a handler that keeps the payload object between ticks sees it refilled. JSON gave
    /// each tick a new object; keeping one is rare, and a copy per tick is the allocation this avoids.
    /// </remarks>
    internal static bool RunData(object? state, object? handler, ref object? tables,
                                 List<KeyValuePair<string, SS.UiValue>> entries)
    {
        if (state is not Lua.LuaState chip || handler is not Lua.LuaFunction fn) return false;
        if (chip.IsRunning) return false;
        var data = tables as DataTables ?? (DataTables)(tables = new DataTables());
        try
        {
            data.Fill(entries);
            var stack = chip.Stack;
            var baseline = stack.Count;
            stack.Push(data.Detail);
            var running = chip.RunAsync(fn, 1, default);
            if (!running.IsCompleted)
            {
                if (stack.Count > baseline) stack.PopUntil(baseline);
                return false;
            }
            running.GetAwaiter().GetResult();
            if (stack.Count > baseline) stack.PopUntil(baseline);
            return true;
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: a compiled page's data handler failed - {ex.Message}");
            return false;
        }
    }

    /// <summary>One page's data payload as Lua tables, kept and refilled: see <see cref="RunData"/>.</summary>
    private sealed class DataTables
    {
        internal readonly Lua.LuaTable Detail = new();
        private readonly List<Lua.LuaValue> _stale = new();

        internal void Fill(List<KeyValuePair<string, SS.UiValue>> entries)
        {
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.Key)) Detail[e.Key] = Value(Detail[e.Key], e.Value);
            Stale(Detail);
            for (var i = _stale.Count - 1; i >= 0; i--)
                foreach (var e in entries)
                    if (_stale[i].TryRead<string>(out var name) && e.Key == name) { _stale.RemoveAt(i); break; }
            Prune(Detail);
        }

        /// <summary>A value as the page's script reads it: a list is 0-based with a `length`, as the prelude's arrays are.</summary>
        private Lua.LuaValue Value(Lua.LuaValue was, in SS.UiValue v)
        {
            switch (v.Type)
            {
                case SS.UiValueType.Number: return Number(v.Number);
                case SS.UiValueType.Bool: return v.Bool;
                case SS.UiValueType.String: return v.String ?? string.Empty;
                case SS.UiValueType.Array when v.Array != null:
                    {
                        // Reused only if it was a list last time: a map's keys would otherwise linger in it.
                        Lua.LuaTable? list = null;
                        var length = 0;
                        if (was.TryRead<Lua.LuaTable>(out var old) && old["length"].TryRead<double>(out var had)) { list = old; length = (int)had; }
                        list ??= new Lua.LuaTable();
                        for (var i = 0; i < v.Array.Length; i++) list[(double)i] = Value(list[(double)i], v.Array[i]);
                        for (var i = v.Array.Length; i < length; i++) list[(double)i] = Lua.LuaValue.Nil;
                        list["length"] = (double)v.Array.Length;
                        return list;
                    }
                case SS.UiValueType.Map when v.Map != null:
                    {
                        var map = was.TryRead<Lua.LuaTable>(out var old) && !old["length"].TryRead<double>(out _) ? old : new Lua.LuaTable();
                        foreach (var p in v.Map)
                            if (!string.IsNullOrEmpty(p.Key)) map[p.Key] = Value(map[p.Key], p.Value);
                        Stale(map);
                        for (var i = _stale.Count - 1; i >= 0; i--)
                            foreach (var p in v.Map)
                                if (_stale[i].TryRead<string>(out var name) && p.Key == name) { _stale.RemoveAt(i); break; }
                        Prune(map);
                        return map;
                    }
            }
            return Lua.LuaValue.Nil;
        }

        // The keys this payload no longer carries: every key, then those it does carry struck off,
        // then the rest cleared. Walked first and cleared after, as NotesIn does - `next` does not
        // survive a nil mid-walk. Linear, because a payload is a handful of keys and a set per table
        // would be the allocation this avoids.
        private void Stale(Lua.LuaTable table)
        {
            _stale.Clear();
            var key = Lua.LuaValue.Nil;
            while (table.TryGetNext(key, out var pair))
            {
                key = pair.Key;
                _stale.Add(key);
            }
        }

        private void Prune(Lua.LuaTable table)
        {
            foreach (var k in _stale) table[k] = Lua.LuaValue.Nil;
            _stale.Clear();
        }

        /// <summary>
        /// A float as the double a page would have parsed from JSON: <c>21.3f</c> is 21.3, not
        /// 21.299999237060547, or a page comparing against a threshold it wrote sees a different number.
        /// </summary>
        private static double Number(float f)
            => !float.IsNaN(f) && !float.IsInfinity(f) && Math.Abs(f) < 7.9e27f ? (double)(decimal)f : f;
    }

    /// <summary>What the chunk's own send path had to say, once, or null while it has said nothing.</summary>
    internal static string? NoteIn(object? environment)
        => environment is Lua.LuaTable env && env["SENDNOTE"].TryRead<string>(out var note) ? note : null;

    /// <summary>
    /// Elements the page wrote to that the scene does not carry, cleared as they are read.
    /// </summary>
    /// <remarks>
    /// The prelude has always recorded these and nothing has ever read them, so a write to an
    /// element the scene has no shape for went nowhere and said nothing. That matters more now that
    /// the prelude provides <c>appendChild</c> and its family: a node the script CREATES cannot have
    /// been laid out, so every write to it lands here. Before, such a page refused outright and the
    /// author knew; without this it would compile, run, and quietly draw nothing where the new rows
    /// were meant to be - which is the failure this compiler exists to avoid.
    /// </remarks>
    internal static List<string>? MissingIn(object? environment)
    {
        if (environment is not Lua.LuaTable env
            || !env["DOM"].TryRead<Lua.LuaTable>(out var dom)
            || !dom["missing"].TryRead<Lua.LuaTable>(out var missing)) return null;

        List<string>? names = null;
        var key = Lua.LuaValue.Nil;
        while (missing.TryGetNext(key, out var pair))
        {
            key = pair.Key;
            if (key.Type != Lua.LuaValueType.String) continue;
            (names ??= new List<string>()).Add(key.Read<string>());
        }
        // Cleared in place, and only after the walk: nilling a key mid-iteration is what `next` is
        // explicitly not allowed to survive.
        if (names != null) foreach (var id in names) missing[id] = Lua.LuaValue.Nil;
        return names;
    }

    /// <summary>
    /// What the page asked for that a compiled page cannot give, keyed by the reason and cleared
    /// as read: a scroll offset, the cascade behind getComputedStyle, its own markup's text, a
    /// keyboard event, a promise rejection nothing caught. The prelude notes each once, by reason,
    /// with the element or key it was asked about as the value; unread, the note was exactly as
    /// silent as the nil it explained.
    /// </summary>
    internal static List<string>? NotesIn(object? environment)
    {
        if (environment is not Lua.LuaTable env
            || !env["DOM"].TryRead<Lua.LuaTable>(out var dom)
            || !dom["notes"].TryRead<Lua.LuaTable>(out var notes)) return null;

        List<string>? lines = null;
        List<string>? reasons = null;
        var key = Lua.LuaValue.Nil;
        while (notes.TryGetNext(key, out var pair))
        {
            key = pair.Key;
            if (key.Type != Lua.LuaValueType.String) continue;
            var why = key.Read<string>();
            (reasons ??= new List<string>()).Add(why);
            (lines ??= new List<string>()).Add(pair.Value.TryRead<string>(out var what) ? $"{why} - \"{what}\"" : why);
        }
        // Cleared after the walk, as MissingIn does: `next` does not survive a nil mid-iteration.
        if (reasons != null) foreach (var why in reasons) notes[why] = Lua.LuaValue.Nil;
        return lines;
    }

    /// <summary>A named function in a loaded chunk's environment, or null when it declared none.</summary>
    internal static object? FunctionIn(object? environment, string name)
        => environment is Lua.LuaTable env && env[name].TryRead<Lua.LuaFunction>(out var fn) ? fn : null;

    /// <summary>
    /// The modifier keys as they are right now, into the table the prelude builds an event from.
    /// </summary>
    /// <remarks>
    /// Read from the game rather than defaulted, because a <c>shiftKey</c> nobody ever set would be
    /// a plausible <c>false</c> on every event - the failure this project keeps recording - and a
    /// page branching on it would take the same branch for ever with nothing to say why.
    ///
    /// Called from the input dispatch, on the main thread, which is the only place
    /// <c>UnityEngine.Input</c> may be read. The table is the chunk's own and is mutated in place,
    /// so delivering an event still allocates nothing.
    /// </remarks>
    internal static void Modifiers(object? environment)
    {
        if (environment is not Lua.LuaTable env || !env["MODS"].TryRead<Lua.LuaTable>(out var mods)) return;
        mods["shift"] = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
        mods["ctrl"] = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl);
        mods["alt"] = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftAlt) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightAlt);
        mods["meta"] = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftCommand) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightCommand);
    }

    /// <summary>
    /// Delivers one event - a click, a press - to a compiled page's own handlers.
    /// </summary>
    /// <remarks>
    /// The same guards as a frame, for the same reasons, plus one that is specific to this: an event
    /// arrives from the input patch, NOT from Update, so it can land while the chip is mid-call.
    /// Pushing onto a running state corrupts it, so a click that arrives at a bad moment is dropped
    /// rather than forced - a browser drops one too when the tab is busy, and the alternative here
    /// is a chip the player has to re-flash.
    /// </remarks>
    internal static bool RunEvent(object? state, object? handler, string id, string kind, float x, float y)
    {
        if (state is not Lua.LuaState chip || handler is not Lua.LuaFunction fn) return false;
        if (chip.IsRunning) return false;

        try
        {
            var stack = chip.Stack;
            var baseline = stack.Count;
            stack.Push(id);
            stack.Push(kind);
            stack.Push(x);
            stack.Push(y);
            var running = chip.RunAsync(fn, 4, default);
            if (!running.IsCompleted)
            {
                if (stack.Count > baseline) stack.PopUntil(baseline);
                return false;
            }
            running.GetAwaiter().GetResult();
            if (stack.Count > baseline) stack.PopUntil(baseline);
            return true;
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: a compiled page's \"{kind}\" handler failed - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// What the last frame wrote, or null when it wrote nothing. Clears the flag, so a frame that
    /// changes nothing costs one boolean read and sends nothing.
    /// </summary>
    internal static Dictionary<string, object>? Drain(object? environment, Dictionary<string, object>? into = null)
    {
        if (environment is not Lua.LuaTable env) return null;
        if (!env["DIRTY"].TryRead<bool>(out var dirty) || !dirty) return null;
        env["DIRTY"] = false;

        if (!env["PAYLOAD"].TryRead<Lua.LuaTable>(out var payload) || payload == null) return null;

        // The caller's buffer, reused. A fresh dictionary per frame - with every double boxed into it
        // - is allocation in the one path this whole exercise exists to keep empty, and at fourteen
        // consoles times the draw rate it is the largest thing left.
        var values = into ?? new Dictionary<string, object>(StringComparer.Ordinal);
        values.Clear();
        var key = Lua.LuaValue.Nil;
        while (payload.TryGetNext(key, out var pair))
        {
            key = pair.Key;
            if (key.Type != Lua.LuaValueType.String) continue;
            var name = key.Read<string>();
            if (pair.Value.TryRead<double>(out var number)) values[name] = number;
            else if (pair.Value.TryRead<string>(out var text)) values[name] = text;
        }

        // Emptied, not just flagged. While only DIRTY was cleared the table kept every slot the page
        // had ever written, so a frame that moved one number re-sent all of them - every frame, for
        // ever. Clearing here rather than in Lua keeps the page's own code unaware of the host, and
        // the keys are nilled in place rather than by handing over a fresh table, which would
        // allocate once per frame in exactly the path this whole exercise exists to keep empty.
        foreach (var name in values.Keys) payload[name] = Lua.LuaValue.Nil;

        return values.Count > 0 ? values : null;
    }

    /// <summary>
    /// The programmable chip a housing holds - ScriptedScreens' own lookup, not a copy of it.
    /// </summary>
    /// <remarks>
    /// There are several housings and they are not interchangeable in the host's callback: a
    /// console arrives as a Motherboard with a null cartridge, a tablet as a cartridge, the visor
    /// as itself. Walking the slots here meant getting that walk right for each of them, and the
    /// first attempt found a chip in none. `CircuitHolderHelper.GetChip` already does it for every
    /// housing the mod supports, and the reference is publicised, so it is called rather than
    /// reproduced.
    /// </remarks>
    internal static object? ChipOf(object? holder)
    {
        if (holder is not Assets.Scripts.Objects.Thing thing) return null;
        try
        {
            return ScriptedScreens.ScriptableUi.Utility.CircuitHolderHelper.GetChip(thing);
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: cannot find the chip in this housing: {ex.Message}");
            return null;
        }
    }
}

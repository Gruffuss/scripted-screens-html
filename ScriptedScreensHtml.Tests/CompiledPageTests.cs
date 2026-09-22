using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lua;
using Lua.Standard;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// A page compiled end to end, and run.
/// </summary>
/// <remarks>
/// This is the whole compiler in one check. The script becomes Lua, its DOM writes resolve to scene
/// slots at compile time, and running it produces a payload of slot values which is compared against
/// what the page means. If the binding table is wrong, a bias is missing, or a write lands on the
/// wrong slot, the numbers here are wrong - and nothing else in the suite would notice, because
/// every other test checks one half or the other.
///
/// The element boxes come from a scene the game actually emitted, checked in beside this file, so
/// the parent origins and descendant offsets are real rather than assumed.
/// </remarks>
internal static class CompiledPageTests
{
    /// <summary>The player's own absolute top in the captured scene; its children's tops are relative to it.</summary>
    private const double PlayerTop = 85;

    /// <summary>The captured scene's resting value for a slot, so a state can carry what it does not move.</summary>
    private static Dictionary<string, SceneSlots.Value> _scene = new(StringComparer.Ordinal);
    private static double? Base(string slot)
        => _scene.TryGetValue(slot, out var v) && v.IsNumber ? v.Number : (double?)null;

    /// <summary>
    /// A border's colour and width reach the border's own shape, not the element's.
    /// </summary>
    /// <remarks>
    /// The border is a second shape beside the element's box, and it used to carry no id at all -
    /// so its `s` and `sw` sat in the scene under a positional name that moves whenever the scene
    /// does, and `el.style.borderColor` was refused on a renderer that emits exactly the slot for
    /// it. An alarm state turning a panel's outline red is the commonest runtime style write a
    /// console page makes.
    ///
    /// The second half matters as much: the companion must NOT be the element's own name. `s` there
    /// is already the transform scale, so `border-color` would have collided with it silently.
    /// </remarks>
    private static void BorderSlots(Action<bool, string> check)
    {
        var box = new DomSlots.Box(outOfFlow: true, parentX: 0, parentY: 0, hasBackground: true);
        var withBorder = new HashSet<string>(StringComparer.Ordinal)
        {
            "p", "p_x", "p_y", "p_w", "p_h", "p_f", "p_s_0", "p_s_1",
            "p__b_s", "p__b_sw", "p__b_x", "p__b_y", "p__b_w", "p__b_h",
        };
        var colour = DomSlots.Map("p", "style.borderColor", box, withBorder);
        check(colour.Mapped && colour.Slots.Length == 1 && colour.Slots[0] == "p__b_s",
              "slots: border-color lands on the border's own shape, not the element's"
              + (colour.Mapped ? " (" + string.Join(",", colour.Slots) + ")" : " - " + colour.Problem));

        var width = DomSlots.Map("p", "style.borderWidth", box, withBorder);
        check(width.Mapped && width.Slots.Length == 1 && width.Slots[0] == "p__b_sw",
              "slots: border-width lands on the border's own shape"
              + (width.Mapped ? string.Empty : " - " + width.Problem));

        // An element with no border, or one of the several-sided kinds, has no single stroke to
        // write to. Refusing says so; mapping it anyway would write into a slot nothing reads.
        var bare = new HashSet<string>(StringComparer.Ordinal) { "p", "p_x", "p_y", "p_w", "p_h", "p_f" };
        var none = DomSlots.Map("p", "style.borderColor", box, bare);
        check(!none.Mapped, "slots: border-color on a borderless element is refused, not mapped to nothing");
    }

    /// <summary>
    /// A box and its label share an id; the label's slots are named after the element too.
    /// </summary>
    /// <remarks>
    /// The emitter writes an element with a background as two lines carrying one id - the `R` and
    /// then the `T` - and the second used to fall back to a POSITIONAL name (`L5_f`) nothing could
    /// address. So `color` on anything with a background was refused, on a renderer that emits
    /// exactly the slot it needs. The second line is now `<id>__2_<key>`, stable and id-derived.
    /// The box must keep the bare name, because `background` and every geometry write land there.
    /// </remarks>
    private static void LabelSlots(Action<bool, string> check)
    {
        // As the emitter wrote it for the probe page, plus an author whose own id is shaped like
        // the generated name - which must not be allowed to claim the label's slots.
        const string scene = "SCENE w=400 h=400 fit=stretch\n"
            + "R x=10 y=12 w=96 h=46 rx=6 f=#22AA44 id=e\n"
            + "T x=10 y=12 w=137.6 h=46 text=\"hello\" size=14 f=#EEEEEE valign=top id=e\n"
            + "R x=1 y=2 w=3 h=4 f=#000000 id=e__2\n";
        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        SceneSlots.Split(scene, values);
        string Text(string k) => values.TryGetValue(k, out var v) && !v.IsNumber ? v.Text! : "(none)";
        double N(string k) => values.TryGetValue(k, out var v) && v.IsNumber ? v.Number : double.NaN;

        check(Text("e_f") == "#22AA44" && Text("e__2_f") == "#EEEEEE",
              $"slots: the box keeps e_f ({Text("e_f")}) and its label is e__2_f ({Text("e__2_f")})");
        check(Text("e") == "hello" && N("e_size") == 14,
              $"slots: textContent (e = {Text("e")}) and fontSize (e_size = {N("e_size")}) do not collide and keep their names");
        check(N("e__2_x") == 10,
              $"slots: an author's id `e__2` cannot claim the label's slots (e__2_x is {N("e__2_x")}: the label's 10, not the author's 1)");

        var box = new DomSlots.Box(outOfFlow: true, parentX: 0, parentY: 0, hasBackground: true);
        var keys = values.Keys.ToHashSet(StringComparer.Ordinal);
        var colour = DomSlots.Map("e", "style.color", box, keys);
        check(colour.Mapped && colour.Slots.SequenceEqual(new[] { "e__2_f" }),
              "slots: color on an element with a background lands on the label's fill"
              + (colour.Mapped ? " (" + string.Join(",", colour.Slots) + ")" : " - " + colour.Problem));
        var bg = DomSlots.Map("e", "style.background", box, keys);
        check(bg.Mapped && bg.Slots.SequenceEqual(new[] { "e_f" }), "slots: background still reaches the box's fill");
        check(DomSlots.Map("e", "textContent", box, keys).Slots.SequenceEqual(new[] { "e" })
              && DomSlots.Map("e", "style.fontSize", box, keys).Slots.SequenceEqual(new[] { "e_size" }),
              "slots: textContent and fontSize map to the same slots as before");
    }

    /// <summary>
    /// <c>visibility</c> reaches the wrapping group's opacity, and its VALUE is translated.
    /// </summary>
    /// <remarks>
    /// The slot alone is not the feature. DOM.bind falls through to reading a value as a CSS
    /// length, and length('hidden') is nil - so a slot with no arm is a write that the compiler
    /// reports as mapped and that does nothing, which is the bug the missing `state` arm was.
    /// </remarks>
    private static void VisibilitySlots(string root, Action<bool, string> check)
    {
        var available = new HashSet<string>(StringComparer.Ordinal) { "e", "e_x", "e_y", "e_w", "e_h", "e_f", "e_o" };
        var box = new DomSlots.Box(outOfFlow: true, parentX: 0, parentY: 0, hasBackground: true);
        var mapped = DomSlots.Map("e", "style.visibility", box, available);
        check(mapped.Mapped && mapped.Slots.SequenceEqual(new[] { "e_o" }),
              "slots: visibility lands on the wrapping group's opacity" + (mapped.Mapped ? "" : " - " + mapped.Problem));

        const string script = "var e = document.getElementById('e');\n"
            + "function tick() { e.style.visibility = 'hidden'; }\nsetInterval(tick, 100);\n";
        var state = Loaded(root, script, available, box, check, "the visibility page");
        if (state == null) return;
        var hidden = Payload(state, Driver(1));
        check(Num(hidden, "e_o") == 0, hidden.ContainsKey("e_o")
            ? $"compiled: visibility = hidden writes e_o = {Num(hidden, "e_o")}"
            : "compiled: visibility = hidden wrote nothing - the word fell through to length('hidden') = nil");
        var shown = Payload(state, "PAYLOAD, DIRTY = {}, false\nDOM.bind('e', 'style.visibility', 'visible')");
        check(Num(shown, "e_o") == 1, $"compiled: visibility = visible writes e_o = {Num(shown, "e_o")}");
    }

    /// <summary>
    /// <c>right</c> and <c>bottom</c> resolve from the containing block's size: <c>x = parent + parentW - w - right</c>.
    /// </summary>
    /// <remarks>
    /// Refused before, because the value is subtracted rather than added and the bias mechanism
    /// only added. A far-edge slot now carries a scale of -1, which the chunk applies. Everything
    /// inside the element rides along with the same sign, as it does for `left`.
    /// </remarks>
    private static void FarEdgeSlots(string root, Action<bool, string> check)
    {
        var available = new HashSet<string>(StringComparer.Ordinal) { "e", "e_x", "e_y", "e_w", "e_h", "e_f", "k_x", "k_y" };
        // the containing block at (100, 0) is 300x200; `e` is 50x20 and holds `k` 5 units in from its corner
        var inside = new[] { ("k", 5.0, 5.0) };
        var box = new DomSlots.Box(outOfFlow: true, parentX: 100, parentY: 0, hasBackground: true, inside,
                                   parentW: 300, parentH: 200, w: 50, h: 20);
        var right = DomSlots.Map("e", "style.right", box, available);
        check(right.Mapped && right.Slots.SequenceEqual(new[] { "e_x", "k_x" })
              && right.Bias.SequenceEqual(new[] { 350.0, 355.0 }) && right.Scale.All(s => s == -1),
              "slots: right maps to e_x and k_x with bias parent + parentW - w and a scale of -1"
              + (right.Mapped ? $" ({string.Join(",", right.Slots)} +{string.Join(",", right.Bias)} x{string.Join(",", right.Scale)})" : " - " + right.Problem));
        var unmeasured = DomSlots.Map("e", "style.right", new DomSlots.Box(true, 100, 0, true), available);
        check(!unmeasured.Mapped, "slots: right is refused, not mapped to the near edge, when the sizes were not measured");

        const string script = "var e = document.getElementById('e');\n"
            + "function tick() { e.style.right = '10px'; e.style.bottom = '4px'; }\nsetInterval(tick, 100);\n";
        var state = Loaded(root, script, available, box, check, "the far-edge page");
        if (state == null) return;
        var sent = Payload(state, Driver(1));
        check(Num(sent, "e_x") == 340 && Num(sent, "k_x") == 345,
              $"compiled: right = 10px puts e_x at {Num(sent, "e_x")} (100 + 300 - 50 - 10 = 340) and k_x rides at {Num(sent, "k_x")}");
        check(Num(sent, "e_y") == 176, $"compiled: bottom = 4px puts e_y at {Num(sent, "e_y")} (0 + 200 - 20 - 4 = 176)");
    }

    /// <summary>A small page compiled against a hand-made slot set and loaded, or null with the reason checked.</summary>
    private static LuaState? Loaded(string root, string script, ICollection<string> available, DomSlots.Box box,
                                    Action<bool, string> check, string what)
    {
        var compiled = CompiledPage.Compile(script, available, _ => box,
                                            prelude: File.ReadAllText(Path.Combine(root, "JsPrelude.lua")));
        if (compiled.Lua == null || compiled.Unmapped.Count > 0)
        {
            check(false, $"compiled: {what} does not compile - "
                  + string.Join("; ", (compiled.Lua == null ? compiled.Problems : compiled.Unmapped).Take(2)));
            return null;
        }
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        Chunk(state, compiled.Lua, "page");
        return state;
    }

    internal static void Run(Action<bool, string> check)
    {
        var root = Root();
        if (root == null) { check(false, "compiled: cannot find the source folder"); return; }

        var scenePath = ScenePath(root);
        if (!File.Exists(scenePath)) { check(false, "compiled: the captured scene is missing"); return; }

        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        SceneSlots.Split(File.ReadAllText(scenePath), values);
        _scene = values;
        var available = values.Keys.ToHashSet(StringComparer.Ordinal);
        var boxes = Boxes(File.ReadAllText(scenePath));

        var script = Regex.Match(File.ReadAllText(Path.Combine(root, "examples", "07-game.lua")),
                                 "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;

        var compiled = CompiledPage.Compile(script, available, id => Box(id, boxes, available),
                                            tabular: id => id is "score" or "hi",
                                            stateOf: (id, cls) => State(id, cls, boxes, available),
                                            // The same prelude production embeds. Leaving it out here
                                            // is what let the chunk ship without one: the test loaded
                                            // the prelude separately and never noticed.
                                            prelude: File.ReadAllText(Path.Combine(root, "JsPrelude.lua")),
                                            // the scene's resting values, so a state can carry what it does not move
                                            baseOf: Base);

        check(compiled.Lua != null, compiled.Lua != null
            ? $"compiled: 07-game compiles, {compiled.Bindings.Count} slot binding(s)"
            : $"compiled: does not compile - {string.Join("; ", compiled.Problems.Take(3))}");
        if (compiled.Lua == null) return;

        check(compiled.Unmapped.Count == 0, compiled.Unmapped.Count == 0
            ? "compiled: every runtime write binds to a slot"
            : $"compiled: {compiled.Unmapped.Count} unmapped - {string.Join("; ", compiled.Unmapped.Take(3))}");

        BorderSlots(check);
        LabelSlots(check);
        VisibilitySlots(root, check);
        FarEdgeSlots(root, check);

        Dictionary<string, LuaValue> sent;
        LuaValue snap;
        try
        {
            (sent, snap) = RunIt(root, compiled.Lua!, frames: 120);
        }
        catch (Exception ex)
        {
            check(false, $"compiled: running it threw - {ex.Message.Split('\n')[0]}");
            return;
        }

        if (sent.Count == 0) { check(false, "compiled: nothing reached the vector element"); return; }
        check(true, $"compiled: {sent.Count} slot value(s) reached the vector element");

        // The host sends with snap set; what the chunk owes is the flag that says there is
        // something to send at all.
        check(snap.Type == LuaValueType.Boolean, snap.Type == LuaValueType.Boolean
            ? "compiled: the chunk flags when a frame produced values"
            : "compiled: the chunk never flags a frame's values, so the host would send nothing");

        // draw() sets legA's height to 16, 18 or 24 and its top to legTop + (24 - height), and
        // legTop is 56 unless ducking, so top + height is 80 in CSS - and 80 plus the player's own
        // top in the scene, because the scene is absolute. That relation is the whole mapping in one
        // number: get the bias wrong and it fails, get the slot wrong and it fails.
        var h = Num(sent, "legA_h");
        var y = Num(sent, "legA_y");
        check(h is 16 or 18 or 24, $"compiled: legA_h is {h}, one of the heights draw() sets");
        var want = PlayerTop + 80 - h;
        check(Math.Abs(y - want) < 0.001, Math.Abs(y - want) < 0.001
            ? $"compiled: legA_y is {y} = the player's top {PlayerTop} + (80 - {h}) - the leg is in the right place"
            : $"compiled: legA_y is {y}, expected {want} for a height of {h}");

        check(sent.ContainsKey("bootA_y"), sent.ContainsKey("bootA_y")
            ? $"compiled: bootA_y rides with it ({Num(sent, "bootA_y")})"
            : "compiled: bootA_y never written, so the boot would stay behind");

        foreach (var slot in new[] { "player_t_0", "player_t_1", "ridgeFar_t_0", "domes_t_0" })
            check(sent.ContainsKey(slot), sent.ContainsKey(slot)
                ? $"compiled: {slot} = {Num(sent, slot)}"
                : $"compiled: {slot} never written");

        check(sent.TryGetValue("score", out var score) && score.Type == LuaValueType.String,
            sent.TryGetValue("score", out var s2) && s2.Type == LuaValueType.String
                ? $"compiled: score is a string slot, \"{s2.Read<string>()}\""
                : "compiled: score is not a string slot");

        // The compiler runs after the page's setup, so these are already in the geometry.
        var leaked = new[] { "field_h", "groundLine_y", "groundLine_h", "overlay_y" }.Where(sent.ContainsKey).ToList();
        check(leaked.Count == 0, leaked.Count == 0
            ? "compiled: setup-only writes send nothing at run time"
            : $"compiled: setup-only write(s) being sent every frame - {string.Join(", ", leaked)}");

        Clicks(root, available, boxes, script, Base, check);
        States(compiled.Lua!, check);
    }

    // ---- a class name actually draws its state -------------------------------------------------

    /// <summary>
    /// A className write moves the slots its state names, and leaving that state puts them back.
    /// </summary>
    /// <remarks>
    /// The compiler enumerates every class a page can assign, lays the page out in each and emits
    /// what each draws. All of that worked and none of it was connected: the runtime dispatched on
    /// 'text', 'colour' and 'translate' and then fell through to reading the value as a CSS length,
    /// which for "duck" is nil - so every compiled page drew its base state for ever, in silence.
    ///
    /// The restore half is the subtler one. States were emitted as DELTAS, so the base state's entry
    /// was empty; going duck -> "" wrote nothing and the ducked geometry stayed on screen. Each state
    /// now carries a value for every slot ANY state touches, which is what makes leaving one work.
    /// </remarks>
    private static void States(string lua, Action<bool, string> check)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        Chunk(state, lua, "page");
        Chunk(state, Driver(3), "frames");

        var ducked = Payload(state, "PAYLOAD, DIRTY = {}, false\nDOM.bind(\"player\", \"className\", \"duck\")");
        check(ducked.Count > 0, ducked.Count > 0
            ? $"compiled: a className write draws its state ({ducked.Count} slot(s) moved)"
            : "compiled: a className write moved nothing - every compiled page draws its base state for ever");
        if (ducked.Count == 0) return;

        var back = Payload(state, "PAYLOAD, DIRTY = {}, false\nDOM.bind(\"player\", \"className\", \"\")");
        var restored = ducked.Keys.All(back.ContainsKey);
        check(restored, restored
            ? "compiled: leaving a state puts every slot it moved back"
            : $"compiled: leaving a state restored {back.Count} of {ducked.Count} slot(s) - the rest keep the old state's geometry");
    }

    /// <summary>Runs one snippet against a loaded chunk and returns what it put in PAYLOAD.</summary>
    private static Dictionary<string, LuaValue> Payload(LuaState state, string snippet)
    {
        Chunk(state, snippet, "bind");
        var sent = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        if (state.Environment["PAYLOAD"].Type != LuaValueType.Table) return sent;
        var table = state.Environment["PAYLOAD"].Read<LuaTable>();
        var key = LuaValue.Nil;
        while (table.TryGetNext(key, out var pair))
        {
            key = pair.Key;
            if (key.Type == LuaValueType.String) sent[key.Read<string>()] = pair.Value;
        }
        return sent;
    }

    // ---- the player can still press the buttons -------------------------------------------------

    /// <summary>
    /// A click on a compiled page reaches the page's own handlers.
    /// </summary>
    /// <remarks>
    /// This is here because the prelude's <c>addEventListener</c> was a no-op, so a compiled page
    /// threw every handler away and every button on it was dead - while the scene still carried its
    /// click region, the log said nothing, and the runner's autopilot made the console look alive.
    /// Nothing in the suite would have caught it: every other check drives frames, and a frame is
    /// exactly the path that still worked.
    ///
    /// Two cases, because they fail separately. A listener on the element that was hit needs only
    /// the registry; a listener on a CONTAINER needs the parent map as well, and that is the case
    /// the runner actually uses - <c>field.addEventListener('mousedown', jump)</c>, with the click
    /// landing on whatever child is under the cursor.
    /// </remarks>
    private static void Clicks(string root, HashSet<string> available,
                               Dictionary<string, (double X, double Y, double W, double H)> boxes,
                               string script, Func<string, double?> baseOf, Action<bool, string> check)
    {
        // `player` is inside `field`, which is what makes the second case a bubbling test.
        var parents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["player"] = "field", ["field"] = "app", ["app"] = "body", ["body"] = "document",
            ["jumpBtn"] = "pads", ["duckBtn"] = "pads", ["pads"] = "app",
        };
        var compiled = CompiledPage.Compile(script, available, id => Box(id, boxes, available),
                                            tabular: id => id is "score" or "hi",
                                            stateOf: (id, cls) => State(id, cls, boxes, available),
                                            prelude: File.ReadAllText(Path.Combine(root, "JsPrelude.lua")),
                                            // the scene's resting values, so a state can carry what it does not move
                                            baseOf: baseOf,
                                            parents: parents);
        if (compiled.Lua == null) { check(false, "compiled: the click build does not compile"); return; }

        foreach (var (target, what) in new[] { ("jumpBtn", "on the button itself"), ("player", "bubbling up to the container") })
        {
            var sent = Click(compiled.Lua, target);
            // Pressing JUMP in the attract mode starts a real game: `reset(false)` hides the overlay,
            // so the big message is cleared. That is a slot write, which is the only evidence
            // available out here - and it is evidence the click ran the page's own code.
            var cleared = sent.TryGetValue("msgBig", out var msg)
                          && msg.Type == LuaValueType.String && msg.Read<string>().Length == 0;
            check(cleared, cleared
                ? $"compiled: a click reaches the page's handlers, {what}"
                : $"compiled: a click on \"{target}\" changed nothing - the page is not interactive ({what})");
        }
    }

    /// <summary>Setup, a few frames, then one press - and what that press alone wrote.</summary>
    private static Dictionary<string, LuaValue> Click(string lua, string target)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        Chunk(state, lua, "page");
        Chunk(state, Driver(3), "frames");
        // Emptied first, so what comes back is what the CLICK wrote and not what the frames did.
        Chunk(state, "PAYLOAD, DIRTY = {}, false\nevent(" + Quote(target) + ", \"mousedown\", 0, 0)", "click");

        var sent = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        if (state.Environment["PAYLOAD"].Type != LuaValueType.Table) return sent;
        var table = state.Environment["PAYLOAD"].Read<LuaTable>();
        var key = LuaValue.Nil;
        while (table.TryGetNext(key, out var pair))
        {
            key = pair.Key;
            if (key.Type == LuaValueType.String) sent[key.Read<string>()] = pair.Value;
        }
        return sent;
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>
    /// What one class state draws.
    /// </summary>
    /// <remarks>
    /// In the mod this sets the class, re-runs layout and reads back the boxes that moved, because
    /// only the layout engine knows where an element lands. Here the page is not laid out, so the
    /// stylesheet answers instead - which is enough to check that the SHAPE is right: that every
    /// state the script can assign is enumerated, produces slot values, and reaches the payload.
    /// It is not a check on the numbers, which need the real layout.
    /// </remarks>
    private static CompiledPage.StateValues? State(string id, string cls,
        Dictionary<string, (double X, double Y, double W, double H)> boxes, ICollection<string> available)
    {
        var values = new CompiledPage.StateValues();
        // #player.duck moves the descendants; #player.hurt recolours one; neither touches the player
        if (id == "player")
        {
            // Only what MOVED, which is what the real PageCompiler.StateOf returns - it diffs the
            // laid-out positions and records the ones that changed. The stub used to return a value
            // for every state including the base one, so it was already complete and the test could
            // not see states being emitted as deltas. A stub that is kinder than production tests
            // nothing.
            if (cls.Contains("duck", StringComparison.Ordinal))
                foreach (var part in new[] { "legA", "legB", "bootA", "bootB" })
                    if (available.Contains(part + "_y") && boxes.TryGetValue(part, out var b))
                        values.Numbers.Add((part + "_y", b.Y + 6));
            return values;
        }
        if (id == "score")
        {
            // #score.flash changes colour only
            if (available.Contains("score_f")) values.Text.Add(("score_f", cls == "flash" ? "#E2A94E" : "#F3E9DC"));
            return values;
        }
        // Any other element the scene knows: the state is producible, it just draws nothing different
        // in this stub. The real one lays the page out and reads what moved.
        var known = boxes.ContainsKey(id) || available.Contains(id + "_t_0") || available.Contains(id + "_f");
        return known ? values : null;
    }

    // ---- running it ---------------------------------------------------------------------------

    private static (Dictionary<string, LuaValue> Sent, LuaValue Snap) RunIt(string root, string lua, int frames)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        // The chunk carries its own prelude now, exactly as the one loaded into a chip does.
        Chunk(state, lua, "page");
        Chunk(state, Driver(frames), "frames");

        // What a frame wrote, read exactly as the host reads it: the chunk leaves its values in
        // PAYLOAD and flags DIRTY, and the host takes them from there. Nothing is sent from Lua.
        var sent = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        var value = state.Environment["PAYLOAD"];
        if (value.Type == LuaValueType.Table)
        {
            var table = value.Read<LuaTable>();
            var key = LuaValue.Nil;
            while (table.TryGetNext(key, out var pair))
            {
                key = pair.Key;
                if (key.Type == LuaValueType.String) sent[key.Read<string>()] = pair.Value;
            }
        }
        return (sent, state.Environment["DIRTY"]);
    }

    /// <summary>Drives the page's own callback, then hands one payload over - as the chip would.</summary>
    private static string Driver(int frames) => @"
for i = 1, " + frames.ToString(CultureInfo.InvariantCulture) + @" do
  local t = i * 16.6667
  if #Pending.frame > 0 then
    local fn = Pending.frame[#Pending.frame]
    Pending.frame = {}
    fn(t)
  else
    for _, timer in ipairs(Pending.timers) do timer.fn(t) end
  end
end
DOM.flush()";

    private static void Chunk(LuaState state, string text, string name) =>
        state.RunAsync(state.Load(text.AsSpan(), name, state.Environment)).AsTask().GetAwaiter().GetResult();

    private static double Num(Dictionary<string, LuaValue> d, string k) =>
        d.TryGetValue(k, out var v) && v.Type == LuaValueType.Number ? v.Read<double>() : double.NaN;

    // ---- the page's boxes, from the scene the game emitted --------------------------------------

    /// <summary>Every `id=` box in a scene, with its position and size. First line wins, as SceneSlots does.</summary>
    private static Dictionary<string, (double X, double Y, double W, double H)> Boxes(string scene)
    {
        var boxes = new Dictionary<string, (double, double, double, double)>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(scene, @"^\s*R x=(-?[\d.]+) y=(-?[\d.]+) w=(-?[\d.]+) h=(-?[\d.]+)[^\n]*? id=(\w+)", RegexOptions.Multiline))
        {
            var id = m.Groups[5].Value;
            if (!boxes.ContainsKey(id))
                boxes[id] = (D(m.Groups[1]), D(m.Groups[2]), D(m.Groups[3]), D(m.Groups[4]));
        }
        return boxes;

        static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// An element's box, as the compiler gets it from the cascade and layout. Every element this
    /// page drives is `#player div { position: absolute }`, so out of flow; the containing block is
    /// the player; and what is nested inside an element is read from the scene rather than assumed.
    /// </summary>
    private static DomSlots.Box? Box(string id, Dictionary<string, (double X, double Y, double W, double H)> boxes,
                                     ICollection<string> available)
    {
        var known = boxes.ContainsKey(id) || available.Contains(id) || available.Contains(id + "_t_0");
        if (!known) return null;

        var inside = new List<(string Id, double Dx, double Dy)>();
        if (id is "legA" or "legB")
        {
            var boot = id == "legA" ? "bootA" : "bootB";
            if (boxes.TryGetValue(id, out var self) && boxes.TryGetValue(boot, out var b))
                inside.Add((boot, b.X - self.X, b.Y - self.Y));
        }
        return new DomSlots.Box(outOfFlow: true, parentX: 0, parentY: PlayerTop, hasBackground: false, inside);
    }

    private static string ScenePath(string root)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "07-game.scene.txt");
        if (File.Exists(beside)) return beside;
        return Path.Combine(root, "..", "ScriptedScreensHtml.Tests", "07-game.scene.txt");
    }

    private static string? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ScriptedScreensHtml");
            if (File.Exists(Path.Combine(candidate, "JsPrelude.lua"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

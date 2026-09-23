using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lua;
using Lua.Standard;
using ScriptedScreensHtml;
using UnityEngine;

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
    /// <summary>
    /// The constants inside an animation that nothing writes go back into the scene as numbers,
    /// and a slot the chip does write stays a slot.
    /// </summary>
    private static void InlinedConstants(Action<bool, string> check)
    {
        var structure = "G a=[$L1_a_0,$L1_a_1] o=\"=if(lt((mod(max($L1_o_0,t-($L1_o_1)),$L1_o_2)/$L1_o_2),0.5),1,0.3)\" {\n"
                      + "  R x=$bar_x y=4 w=\"=$bar_w*0.5\" h=4 f=#fff id=bar\n}\n";
        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal)
        {
            ["L1_a_0"] = new SceneSlots.Value(5f), ["L1_a_1"] = new SceneSlots.Value(6f),
            ["L1_o_0"] = new SceneSlots.Value(0f), ["L1_o_1"] = new SceneSlots.Value(-1.25f), ["L1_o_2"] = new SceneSlots.Value(1.6f),
            ["bar_x"] = new SceneSlots.Value(10f), ["bar_w"] = new SceneSlots.Value(80f),
        };
        var written = new HashSet<string>(StringComparer.Ordinal) { "bar_w", "bar_x" };
        var result = CompiledPage.Inline(structure, values, written);
        check(result.Contains("o=\"=if(lt((mod(max(0,t-((-1.25))),1.6)/1.6),0.5),1,0.3)\""),
              "inline: an animation's unwritten constants become numbers (" + result.Split('\n')[0] + ")");
        check(result.Contains("w=\"=$bar_w*0.5\""), "inline: a slot the chip writes stays a slot inside an expression");
        check(!values.ContainsKey("L1_o_1") && values.ContainsKey("L1_a_0") && values.ContainsKey("bar_w"),
              "inline: an inlined slot leaves the opening values; one still referenced (a=[...]) and a written one stay");
        var again = CompiledPage.Inline(result, values, written);
        check(again == result, "inline: a second pass changes nothing");
    }

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
        // The emitter sizes the label's rect FROM the box's - slack, a centred shift, a clipped
        // height - so no one number moves both lines to where a re-layout would. Refused, naming
        // the text, rather than the box moved without its text or the text put off by the slack.
        var width = DomSlots.Map("e", "style.width", box, keys);
        check(!width.Mapped && width.Problem!.Contains("draws text over its box"),
              "slots: a size write on a box with text is refused, not written to both lines - " + (width.Problem ?? "(mapped)"));
        check(!DomSlots.Map("e", "style.top", box, keys).Mapped, "slots: a position write on a box with text is refused too");
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

    /// <summary>
    /// The four keys of a data-driven page reach slots: a block bar's `%` width inside a block
    /// track, a text span, a flex child's width still refused, and a bool as two states carrying
    /// the sibling under it.
    /// </summary>
    /// <remarks>
    /// Mirrors examples/02-live-data, where each of these was a refusal and the refusal kept the
    /// whole page on a layout, translate and emit per tick. The page is built, laid out and emitted
    /// here exactly as a surface does it, so the boxes, the block widths and the slot names are the
    /// emitter's rather than a hand-written approximation of them.
    ///
    /// The width check is the proof in miniature: what <c>Length("62%")</c> gives against what the
    /// emitter drew for the same CSS. The bool check reads the states off the laid-out page; the
    /// unnamed sibling is the refusal that matters, since the alternative is an alarm that vanishes
    /// while the note under it stays put.
    /// </remarks>
    private static void DataSlotsFixture(Action<bool, string> check)
    {
        const string head = "<html><head><meta name=\"viewport\" content=\"width=480\"><style>"
            + "body{background:#0B1622;color:#E4F1F7;padding:16px;font-size:14px}"
            + ".row{display:flex;justify-content:space-between;margin:8px 0 4px 0}"
            + ".track{height:14px;background:#172033;border-radius:7px;overflow:hidden}"
            + ".pad{padding:3px 6px}"
            + ".fill{height:100%;width:62%;background:#2E8B6E;border-radius:7px}"
            + "#alarm{margin-top:16px;padding:10px;background:#B5352C;border-radius:8px;text-align:center}"
            + ".note{margin-top:16px;color:#7A93A6;font-size:12px}"
            + "</style></head><body>"
            + "<div class=\"row\"><span>Pressure</span><span id=\"pressure\">--</span></div>"
            + "<div class=\"track\"><div class=\"fill\" id=\"bar\"></div></div>"
            + "<div class=\"track pad\"><div class=\"fill\" id=\"bar2\"></div></div>"
            + "<div class=\"row\"><span>Temperature</span><span id=\"temp\">--</span></div>"
            + "<div id=\"alarm\">OVER PRESSURE</div>";
        const string tail = "</body></html>";

        var (built, values) = Emitted(head + "<div class=\"note\" id=\"note\">Values arrive from Lua every second.</div>" + tail, Driven);
        var available = values.Keys.ToHashSet(StringComparer.Ordinal);
        double N(string k) => values.TryGetValue(k, out var v) && v.IsNumber ? v.Number : double.NaN;

        // (1) the bar: a block in a block track, so its width is local and maps although in flow
        var bar = PageCompiler.BoxFor(built, "bar", available);
        var width = bar is { } bb ? DomSlots.Map("bar", "style.width", bb, available) : DomSlots.Result.No("no box");
        check(width.Mapped && width.Slots.SequenceEqual(new[] { "bar_w" }),
              "data: a block bar's width inside a block track maps to bar_w" + (width.Mapped ? "" : " - " + width.Problem));
        check(bar is { LocalWidth: true } && Math.Abs(width.PercentOf - 448) < 0.01,
              $"data: the width's percentage is of the track's width, 448 (got {width.PercentOf})");

        // (2) "62%" resolves to what the emitter drew for width:62% - the proof in miniature
        var pct = DomSlots.Length("62%", width.PercentOf);
        check(pct is { } p && Math.Abs(p - N("bar_w")) < 0.01,
              $"data: 62% of the track is {pct?.ToString(CultureInfo.InvariantCulture) ?? "null"}, and the emitter drew bar_w = {N("bar_w")}");
        check(DomSlots.Length("62%") == null && DomSlots.Length("1em", 448) == null && DomSlots.Length("12px", 448) == 12,
              "data: a % of nothing measured and an em are refused, px passes through");
        // A % is of the block's CONTENT box: the padded track draws 448 wide and offers 436
        var bar2 = PageCompiler.BoxFor(built, "bar2", available);
        var width2 = bar2 is { } b2 ? DomSlots.Map("bar2", "style.width", b2, available) : DomSlots.Result.No("no box");
        var pct2 = DomSlots.Length("62%", width2.PercentOf);
        check(width2.Mapped && Math.Abs(width2.PercentOf - 436) < 0.01 && pct2 is { } p2 && Math.Abs(p2 - N("bar2_w")) < 0.01,
              $"data: 62% in a padded track is of its content box, 436 (got {width2.PercentOf}): {pct2?.ToString(CultureInfo.InvariantCulture) ?? "null"} against the emitter's bar2_w = {N("bar2_w")}");

        var colour = DomSlots.Map("bar", "style.background-color", bar!.Value, available);
        var text = PageCompiler.BoxFor(built, "pressure", available) is { } pb ? DomSlots.Map("pressure", "textContent", pb, available) : DomSlots.Result.No("no box");
        check(colour.Mapped && colour.Slots.SequenceEqual(new[] { "bar_f" }) && text.Mapped && text.Slots.SequenceEqual(new[] { "pressure" }),
              "data: the bar's colour and the span's text map to bar_f and pressure");

        // a flex child's width, and any height in flow, stay refused with the reason intact
        var flex = DomSlots.Map("pressure", "style.width", PageCompiler.BoxFor(built, "pressure", available)!.Value, available);
        check(!flex.Mapped && flex.Problem!.Contains("is in normal flow, so changing its width moves its siblings"),
              "data: a flex child's width is still refused - " + flex.Problem);
        var height = DomSlots.Map("bar", "style.height", bar.Value, available);
        check(!height.Mapped && height.Problem!.Contains("moves its siblings"), "data: the bar's height is still refused in flow");

        // (3) the bool: two states, the element's opacity and the note that closes the gap
        var toggle = PageCompiler.ToggleOf(built, "alarm");
        check(toggle.Problem == null, "data: alarm's shown and hidden states are captured" + (toggle.Problem == null ? "" : " - " + toggle.Problem));
        if (toggle.Problem == null)
        {
            double In(List<(string Slot, double Value)> s, string k) => s.FirstOrDefault(e => e.Slot == k) is { Slot: not null } e ? e.Value : double.NaN;
            check(In(toggle.Shown, "alarm_o") == 1 && In(toggle.Hidden, "alarm_o") == 0,
                  $"data: the states hide alarm through alarm_o ({In(toggle.Shown, "alarm_o")} -> {In(toggle.Hidden, "alarm_o")})");
            var shownY = In(toggle.Shown, "note_y");
            var hiddenY = In(toggle.Hidden, "note_y");
            check(Math.Abs(shownY - N("note_y")) < 0.01 && hiddenY < shownY - 16,
                  $"data: the note rides up when alarm hides (note_y {shownY} -> {hiddenY}, the scene's {N("note_y")})");
            check(built.NamedGroups.Contains("alarm"), "data: capturing the states names alarm's opacity group for the emitter");
        }

        // (4) a class state puts a box's label where the emitter draws it for the moved box - not at
        // the box's own coordinate, which a centred label sits half the slack to the left of
        const string moveHead = "<html><head><meta name=\"viewport\" content=\"width=480\"><style>"
            + "body{padding:10px}#a{width:200px;padding:6px;background:#333;text-align:center}.up{margin-left:40px}"
            + "</style></head><body>";
        var (rested, rest) = Emitted(moveHead + "<div id=\"a\">Hi</div><div id=\"b\">below</div></body></html>", "a");
        var (_, moved) = Emitted(moveHead + "<div id=\"a\" class=\"up\">Hi</div><div id=\"b\">below</div></body></html>", "a");
        var state = PageCompiler.StateOf("a", "up", rested, rested.Root.panel!, new Vector2(rested.ViewportWidth, rested.ViewportWidth), rest);
        double S(string k) => state != null && state.Numbers.FirstOrDefault(n => n.Slot == k) is { Slot: not null } n ? n.Value : double.NaN;
        double M(string k) => moved.TryGetValue(k, out var v) && v.IsNumber ? v.Number : double.NaN;
        check(state != null && Math.Abs(S("a_x") - M("a_x")) < 0.01 && Math.Abs(S("a__2_x") - M("a__2_x")) < 0.01 && M("a__2_x") != M("a_x"),
              $"data: a class state puts a box's label where the emitter draws it (a_x {S("a_x")} vs {M("a_x")}, a__2_x {S("a__2_x")} vs {M("a__2_x")})");

        // A class that repaints a box is a state too: what it draws is read off the emitted scene,
        // so the box's colour travels with it, not only where it sits.
        const string paintHead = "<html><head><meta name=\"viewport\" content=\"width=480\"><style>"
            + "body{padding:10px}#a{width:200px;height:20px;background:#333}#a.hot{background:#ff0000;height:30px}"
            + "</style></head><body>";
        var (painted, paintRest) = Emitted(paintHead + "<div id=\"a\"></div><div id=\"b\">below</div></body></html>", "a");
        var hot = PageCompiler.StateOf("a", "hot", painted, painted.Root.panel!, new Vector2(painted.ViewportWidth, painted.ViewportWidth), paintRest);
        var fill = hot?.Text.FirstOrDefault(t => t.Slot == "a_f").Value;
        var tall = hot != null && hot.Numbers.FirstOrDefault(n => n.Slot == "a_h") is { Slot: not null } hn ? hn.Value : double.NaN;
        check(fill == "#FF0000" && Math.Abs(tall - 30) < 0.01, fill == "#FF0000" && Math.Abs(tall - 30) < 0.01
            ? "data: a class that repaints and resizes a box carries its colour and its height (a_f #FF0000, a_h 30)"
            : $"data: the class state carries a_f {fill ?? "(nothing)"} and a_h {tall}, not its red and its 30");

        // the note without an id has no slot to move: refused, naming it, rather than left behind
        var (plain, _) = Emitted(head + "<div class=\"note\">Values arrive from Lua every second.</div>" + tail, Driven);
        var unnamed = PageCompiler.ToggleOf(plain, "alarm");
        check(unnamed.Problem is { } why && why.Contains("has no id") && why.Contains("Values arrive"),
              "data: hiding alarm over an unnamed note is refused, naming the note - " + (unnamed.Problem ?? "(mapped)"));
    }

    /// <summary>The ids the fixture's data drives, named as the surface names them so the scene carries their slots.</summary>
    private static readonly string[] Driven = { "pressure", "temp", "bar", "bar2", "alarm" };

    /// <summary>A page built, laid out and emitted as a surface does it, with its slot values.</summary>
    /// <remarks>
    /// Build installs the applier's @supports oracle and the page's viewport into CssParser's
    /// statics, and they stay. CssTests runs after this and expects the parser's own defaults -
    /// with the oracle left in place its `@supports` checks answered from the applier and two
    /// failed. Everything set here is put back.
    /// </remarks>
    private static (HtmlRenderer.Result Built, Dictionary<string, SceneSlots.Value> Values) Emitted(string html, params string[] driven)
    {
        var oracle = CssParser.SupportsOracle;
        var (vw, vh) = (CssParser.ViewportWidth, CssParser.ViewportHeight);
        var aspect = HtmlRenderer.SurfaceAspect;
        var mainThread = OffThread.MainThreadId;
        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        try
        {
            ResolvedStyle.DefaultFace = FontLibrary.Default();
            HtmlRenderer.SurfaceAspect = 1f;
            OffThread.MainThreadId = Environment.CurrentManagedThreadId;
            OffThread.Job = OffThread.Globals.Take();
            var built = HtmlRenderer.Build(html, FontLibrary.Default());
            HtmlRenderer.NameDrivenGroups(built);
            foreach (var id in driven) built.Driven.Add(id);
            var panel = new Panel(built.Root);
            foreach (var grid in built.Grids)
                if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
            PostLayout.Attach(built);
            var boxes = new Dictionary<VisualElement, OffThread.Box>();
            OffThread.Boxes = boxes;
            var size = new Vector2(built.ViewportWidth, built.ViewportWidth);
            panel.Layout(size.x, size.y);
            OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());
            OffThread.Active = true;
            var output = VectorEmitter.Emit(built, built.Root, size.x, size.y);
            SceneSlots.Split(output.Chars, output.Length, values);
            return (built, values);
        }
        finally
        {
            OffThread.Active = false;
            OffThread.Boxes = null;
            OffThread.MainThreadId = mainThread;
            HtmlRenderer.SurfaceAspect = aspect;
            CssParser.SupportsOracle = oracle;
            CssParser.ViewportWidth = vw;
            CssParser.ViewportHeight = vh;
        }
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
        InlinedConstants(check);
        var root = Root();
        if (root == null) { check(false, "compiled: cannot find the source folder"); return; }

        Markup(root, check);
        CompiledOnce(check);
        MarkupGauges(check);
        MarkupRuntime(root, check);
        MarkupValues(root, check);
        StateText(root, check);
        StyleStates(check);
        ReplacedListeners(check);

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
        DataSlotsFixture(check);
        DataSlotsTests.Run(check);

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

    // ---- markup written with innerHTML, compiled -----------------------------------------------

    /// <summary>
    /// The two ways a dashboard draws a level, compiled: a gauge whose height is a mixed calc(), and a
    /// hard-stop gradient bar. Each is one straight line from the value to a slot.
    /// </summary>
    /// <remarks>
    /// The gauge was reported three contradictory ways - out of proportion, and changing nothing - because
    /// a mixed calc() is written after layout from its parent's new size, and a rebuilt bar's parent had no
    /// new size: some probes read the percent alone and some the percent less 12px. The bar was a ramp over
    /// the whole box, because `C 0 37%` was read as one stop at 0.
    /// </remarks>
    private static void MarkupGauges(Action<bool, string> check)
    {
        const string page = @"<meta name=""viewport"" content=""width=300"">
<style>body{margin:0;background:#000}</style>
<body><div id=""frame"" style=""width:300px;height:300px""></div>
<script>
const st = { n: 40 };
function render() {
  const pct = Math.min(100, Math.max(4, st.n)).toFixed(1);
  document.getElementById('frame').innerHTML =
    '<div style=""position:relative;height:200px;background:#222222""><div style=""position:absolute;left:6px;right:6px;bottom:6px;height:calc(' + pct + '% - 12px);background:#33cc66""></div></div>'
    + '<div style=""height:20px;background:linear-gradient(90deg,#33cc66 0 ' + pct + '%,transparent 0) left bottom/100% 3px no-repeat,#000000""></div>';
}
function step() { st.n = (st.n + 7) % 100; render(); }
render();
setInterval(step, 500);
</script></body>";
        MarkupSlots.Result? markup;
        try { (_, markup) = Probe4.Headless(page); }
        catch (Exception ex) { check(false, "gauges: compiling threw - " + ex.Message.Split('\n')[0]); return; }
        if (markup == null || markup.Targets.Count == 0) { check(false, "gauges: nothing compiled"); return; }

        var lines = markup.Targets[0].Holes.Values.Where(h => h.Kind == MarkupSlots.Kind.Number).SelectMany(h => h.To).ToList();
        var gauge = lines.FirstOrDefault(l => l.Slot.EndsWith("_h", StringComparison.Ordinal));
        check(gauge.Slot != null && Math.Abs(gauge.Scale - 2) < 0.05 && Math.Abs(gauge.Bias + 12) < 1.5,
            gauge.Slot != null ? $"gauges: calc(X% - 12px) of 200px is h = {gauge.Scale:0.###}x + {gauge.Bias:0.#}" : "gauges: the gauge's height reaches no slot");
        var bar = lines.FirstOrDefault(l => l.Slot.StartsWith("cut", StringComparison.Ordinal) && l.Slot.EndsWith("_w", StringComparison.Ordinal));
        check(bar.Slot != null && Math.Abs(bar.Scale - 3) < 0.05,
            bar.Slot != null ? $"gauges: a hard-stop bar's band is {bar.Scale:0.###}x wide on a 300px bar" : "gauges: the hard-stop bar reaches no slot");
        var said = markup.Problems.Where(p => p.Contains("proportion", StringComparison.Ordinal) || p.Contains("changes nothing", StringComparison.Ordinal)).ToList();
        check(said.Count == 0, said.Count == 0 ? "gauges: neither is reported" : "gauges: " + said[0]);

        // A colour named through a custom property that is itself a var(): the chunk writes CSS and the
        // scene reads hex, and a name missing from the table reached the console as the text `var(--live)`.
        const string tones = @"<meta name=""viewport"" content=""width=300"">
<style>:root{--steel:#8899aa;--live:var(--steel)} body{margin:0;background:#000}</style>
<body><div id=""frame"" style=""width:300px;height:100px""></div>
<script>
const st = { hot: false };
function render() { document.getElementById('frame').innerHTML = '<div style=""height:20px;color:' + (st.hot ? '#ff0000' : 'var(--live)') + '"">x</div>'; }
function step() { st.hot = !st.hot; render(); }
render();
setInterval(step, 500);
</script></body>";
        CompiledPage.Result toned;
        try { (toned, _) = Probe4.Headless(tones); }
        catch (Exception ex) { check(false, "gauges: compiling the tone page threw - " + ex.Message.Split('\n')[0]); return; }
        check(toned.Lua != null && toned.Lua.Contains("[\"var(--live)\"] = \"#8899AA\"", StringComparison.Ordinal),
            "gauges: a var() naming another var() is in the colour table as the hex it ends at");
    }

    /// <summary>
    /// A page compiles once however often it is built: fifteen consoles showing it, a capture's
    /// rebuild, the same push twice. A second build of the same source at the same size finds the
    /// first compile, pointed at its own console, without laying anything out. And a compile its page
    /// stopped wanting part way is kept by nothing.
    /// </summary>
    private static void CompiledOnce(Action<bool, string> check)
    {
        const string page = @"<meta name=""viewport"" content=""width=300"">
<style>body{margin:0;background:#000;font-family:sans-serif}</style>
<body><div id=""frame"" style=""width:300px;height:300px;display:flex;flex-direction:column""></div>
<script>
const st = { n: 3, hot: false };
function render() {
  document.getElementById('frame').innerHTML = '<div style=""height:40px;color:' + (st.hot ? '#ff0000' : '#ffffff') + '"">Count ' + st.n + ' units</div>';
}
function step() { st.n += 1; st.hot = !st.hot; render(); }
render();
setInterval(step, 500);
</script></body>";
        var source = "compiled once: " + page;
        var one = ("main", "one", "html:one");
        var two = ("main", "two", "html:two");
        try
        {
            PageCompiler.Cancelled = () => true;
            CompiledPage.Result stopped;
            try { stopped = Probe4.Headless(page, source, one).Compiled; }
            finally { PageCompiler.Cancelled = null; }
            var e0 = MarkupSlots.Emits;
            var first = Probe4.Headless(page, source, one).Compiled;
            var e1 = MarkupSlots.Emits;
            var second = Probe4.Headless(page, source, two).Compiled;
            var e2 = MarkupSlots.Emits;
            check(!stopped.Ok && e1 > e0, !stopped.Ok && e1 > e0
                ? $"compiled once: a compile stopped part way is not kept - the next one lays the page out ({e1 - e0} layout(s))"
                : $"compiled once: stopped {(stopped.Ok ? "compiled anyway" : "refused")}, the next compile laid out {e1 - e0} time(s)");
            static string Line((string, string, string) t) => $"SURFACE, ELEMENT, SCENE = \"{t.Item1}\", \"{t.Item2}\", \"{t.Item3}\"";
            var same = first.Ok && second.Ok && first.Lua!.Contains(Line(one), StringComparison.Ordinal)
                       && second.Lua == first.Lua.Replace(Line(one), Line(two), StringComparison.Ordinal)
                       && second.Structure == first.Structure && second.Bindings.Count == first.Bindings.Count;
            check(same && e2 == e1, same && e2 == e1
                ? "compiled once: the same page built again finds its compile, pointed at the new console, with no layout"
                : $"compiled once: the second build laid out {e2 - e1} time(s); its chunk {(same ? "matches" : "differs from")} the first's but for the target");
        }
        catch (Exception ex) { check(false, "compiled once: threw - " + ex.Message.Split('\n')[0]); }
    }

    /// <summary>
    /// A page that draws with <c>innerHTML</c>, compiled once: its markup becomes the scene, and the
    /// chunk writes only what fills it.
    /// </summary>
    /// <remarks>
    /// The whole path in one page - a tab the page switches on, a label built round a value, a list
    /// the page grows, a colour it picks by name, a click that changes tab - laid out headless as a
    /// surface lays it out, then run in the Lua VM a chip runs. What each check reads is what the
    /// renderer would be sent.
    /// </remarks>
    private static void Markup(string root, Action<bool, string> check)
    {
        const string page = @"<meta name=""viewport"" content=""width=300"">
<style>:root{--live:#33cc66;--dim:#556677} body{margin:0;background:#000;font-family:sans-serif}</style>
<body><div id=""frame"" style=""width:300px;height:300px;display:flex;flex-direction:column""></div>
<script>
const st = { tab: 'a', n: 3, log: [] };
const TABS = [{ id: 'a', label: 'Alpha' }, { id: 'b', label: 'Beta' }];
let acts = [];
const act = (fn) => { acts.push(fn); return ' data-act=""' + (acts.length - 1) + '""'; };
function tabBar(o) {
  return '<div style=""display:flex;height:30px"">' + o.tabs.map((t) => '<div' + act(() => o.onSelect(t.id))
    + ' style=""flex:1;color:' + (t.id === o.active ? 'var(--live)' : 'var(--dim)') + '"">' + t.label + '</div>').join('') + '</div>';
}
function values() { return { tab: st.tab, count: st.n.toFixed(0), log: st.log, setTab: (id) => { st.tab = id; render(); } }; }
function render() {
  acts = [];
  const v = values();
  const frame = document.getElementById('frame');
  frame.innerHTML = (st.tab === 'a' ? '<div style=""height:40px;color:#ffffff"">Count ' + v.count + ' units</div>' : '<div style=""height:40px;color:#ff0000"">Other</div>')
    + v.log.map((l) => '<div style=""height:20px;color:#ffffff"">' + l + '</div>').join('')
    + tabBar({ tabs: TABS, active: v.tab, onSelect: v.setTab });
}
function step() { st.n += 1; st.log = ['line ' + st.n].concat(st.log).slice(0, 3); render(); }
render();
setInterval(step, 500);
</script></body>";
        CompiledPage.Result compiled;
        MarkupSlots.Result? markup;
        try { (compiled, markup) = Probe4.Headless(page); }
        catch (Exception ex) { check(false, "markup: compiling a markup page threw - " + ex.Message.Split('\n')[0]); return; }

        check(compiled.Ok && markup?.Template != null, compiled.Ok
            ? $"markup: a page drawn with innerHTML compiles - {compiled.Bindings.Count} binding(s), {compiled.Warnings.Count} warning(s)"
            : $"markup: a page drawn with innerHTML does not compile - {string.Join("; ", compiled.Problems.Concat(compiled.Unmapped).Take(3))}");
        if (!compiled.Ok || markup?.Template == null) return;
        var leaks = Regex.Matches(markup.Template + string.Join(" ", markup.Values.Values), "98765\\d|#0F[0-9A-F]{4}\\b").Count;
        check(leaks == 0, leaks == 0 ? "markup: no stand-in value is left in the structure" : $"markup: {leaks} stand-in value(s) left in the structure");

        var state = LuaState.Create();
        state.OpenStandardLibraries();
        string? Do(string text, string name)
        {
            try { Chunk(state, text, name); return null; }
            catch (Exception ex) { return ex.Message.Split('\n')[0]; }
        }
        Dictionary<string, string> Sent()
        {
            var sent = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!state.Environment["PAYLOAD"].TryRead<LuaTable>(out var table)) return sent;
            var key = LuaValue.Nil;
            while (table.TryGetNext(key, out var pair)) { key = pair.Key; if (key.TryRead<string>(out var k)) sent[k] = Text(pair.Value); }
            return sent;
        }
        bool Holds(Dictionary<string, string> sent, string text) => sent.Values.Any(v => v.Contains(text, StringComparison.Ordinal));
        // The scene prints the label round a placeholder (CompiledPage.Place), so the chunk writes
        // the value that goes in it, never the whole string.
        var countSlot = Regex.Match(compiled.Structure ?? string.Empty, "text=\"Count \\{\\$([A-Za-z0-9_]+)[^}]*\\} units\"").Groups[1].Value;
        bool Counts(Dictionary<string, string> sent, string n) => countSlot.Length > 0 && sent.TryGetValue(countSlot, out var v) && v == n;

        var load = Do(compiled.Lua!, "page");
        var first = Sent();
        check(load == null && Counts(first, "3"), load != null
            ? "markup: the chunk does not load - " + load
            : countSlot.Length == 0
                ? "markup: the scene has no placeholder for the count in \"Count ... units\""
                : Counts(first, "3")
                    ? "markup: the first render writes the label's value into the placeholder the scene prints it round (\"Count {3} units\")"
                    : $"markup: the first render wrote {first.Count} value(s) and not 3 into the count's placeholder");

        // A second of the page's own timer: two steps, two new rows, the count moved.
        foreach (var k in first.Keys.ToList()) Do("PAYLOAD[\"" + k + "\"] = nil", "clear");
        var frames = Do("for i = 1, 61 do frame(1 / 60) end", "frames");
        var later = Sent();
        check(frames == null && Counts(later, "5") && Holds(later, "line 5"), frames != null
            ? "markup: running frames failed - " + frames
            : Counts(later, "5") && Holds(later, "line 5")
                ? "markup: the page's timer re-renders by writing values - the count and the newest row"
                : $"markup: after two steps the chunk wrote {later.Count} value(s), missing the count or the row");
        var rows = markup.Targets[0].Bindings.FirstOrDefault(b => b.Key.StartsWith("rows#", StringComparison.Ordinal));
        check(rows != null, rows != null ? $"markup: the list is a state by its length ({rows.States.Count} lengths)" : "markup: the list has no length state");

        // The second tab, pressed: the handler the markup registered runs, and the drive state flips.
        var beta = Regex.Matches(compiled.Lua!, "DOM\\.on\\(\"([^\"]+)\", \"click\"").Select(m => m.Groups[1].Value).ToList();
        check(beta.Count == 2, $"markup: {beta.Count} click region(s) registered, one per tab");
        if (beta.Count != 2) return;
        foreach (var k in Sent().Keys.ToList()) Do("PAYLOAD[\"" + k + "\"] = nil", "clear");
        var pressed = Do($"event(\"{beta[1]}\", \"click\", 0, 0)", "click");
        var after = Sent();
        var gates = after.Where(p => p.Key.EndsWith("_v", StringComparison.Ordinal)).ToList();
        check(pressed == null && gates.Any(p => p.Value == "0") && gates.Any(p => p.Value == "1"), pressed != null
            ? "markup: pressing a tab failed - " + pressed
            : $"markup: pressing the second tab switches what is shown ({gates.Count(p => p.Value == "1")} shown, {gates.Count(p => p.Value == "0")} hidden)");
        var live = MarkupSlots.Hex(new Color(0x33 / 255f, 0xcc / 255f, 0x66 / 255f));
        check(after.Values.Contains(live), after.Values.Contains(live)
            ? $"markup: the pressed tab takes the page's colour by name, as the scene's hex ({live})"
            : "markup: no slot took the pressed tab's colour as hex - " + string.Join(", ", after.Where(p => p.Key.EndsWith("_f", StringComparison.Ordinal)).Select(p => p.Key + "=" + p.Value).Take(4)));
    }

    private static string Text(LuaValue v)
        => v.TryRead<string>(out var s) ? s : v.TryRead<double>(out var d) ? d.ToString(CultureInfo.InvariantCulture) : v.ToString();

    /// <summary>
    /// The runtime's own arms for compiled markup: a state for "none of them", a colour by name, a
    /// label from pieces. Each is a few lines of Lua a page reaches only through markup.
    /// </summary>
    private static void MarkupRuntime(string root, Action<bool, string> check)
    {
        var bindings = new List<CompiledPage.Binding>
        {
            new("frame.drive", Array.Empty<string>(), Array.Empty<double>(), CompiledPage.Kind.State,
                new List<CompiledPage.StateValues>
                {
                    Values("a", ("g_v", 1.0)),
                    Values("\u0001other", ("g_v", 7.0)),
                    Titled(Values("tab", ("g_v", 1.0)), ("lab", "TAB")),
                }, other: "\u0001other"),
            new("frame.innerHTML#1", new[] { "c_f" }, new double[1], CompiledPage.Kind.Colour,
                colours: new Dictionary<string, string> { ["var(--x)"] = "#123456" }),
            new("frame.label#0", new[] { "lab" }, new double[1], CompiledPage.Kind.Label,
                pieces: new List<object> { "Count ", 1, " in ", (2, (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["var(--x)"] = "#123456" }) },
                transform: "upper"),
            new("t.textContent", new[] { "t" }, new double[1], CompiledPage.Kind.Text),
        };
        var compiled = CompiledPage.Compile("const x = 1;", new HashSet<string>(StringComparer.Ordinal), _ => null,
                                            prelude: File.ReadAllText(Path.Combine(root, "JsPrelude.lua")),
                                            markupBindings: bindings);
        if (compiled.Lua == null) { check(false, "markup runtime: the chunk does not compile - " + string.Join("; ", compiled.Problems)); return; }
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        Chunk(state, compiled.Lua, "page");
        Chunk(state, "DOM.bind('frame', 'drive', 'zzz') DOM.bind('frame', 'innerHTML#1', 'var(--x)') DOM.label('frame', 'label#0', 'abc', 'var(--x)') DOM.bind('t', 'textContent', 42)", "writes");
        var payload = state.Environment["PAYLOAD"].Read<LuaTable>();
        var other = payload["g_v"];
        check(other.TryRead<double>(out var g) && g == 7, other.TryRead<double>(out var g2) && g2 == 7
            ? "markup runtime: a value no state is named after draws the state for none of them"
            : $"markup runtime: an unnamed value drew {other} instead of the state for none of them");
        var colour = Text(payload["c_f"]);
        check(colour == "#123456", colour == "#123456" ? "markup runtime: a colour written by name reaches the scene as its hex" : $"markup runtime: the colour reached the scene as {colour}");
        var label = Text(payload["lab"]);
        check(label == "Count ABC in #123456", label == "Count ABC in #123456"
            ? "markup runtime: a label is its pieces, its values in the label's case and its colours as hex"
            : $"markup runtime: the label reads \"{label}\"");

        // A page re-renders whole, so the same writes arrive again and again. What the scene already
        // shows is not sent again, and a label built from the same values is not built again: the
        // second render leaves nothing to send, and a thousand of them allocate nothing.
        // A text slot's value is data, never scene source: 42 is the text "42", with no guard the
        // scene reader would need and a data value never meets.
        var number = Text(payload["t"]);
        check(number == "42", number == "42" ? "markup runtime: a number written as text arrives as its digits" : $"markup runtime: 42 written as text arrived as \"{number}\"");

        const string again = "DOM.bind('frame', 'drive', 'zzz') DOM.bind('frame', 'innerHTML#1', 'var(--x)') DOM.label('frame', 'label#0', 'abc', 'var(--x)') DOM.bind('t', 'textContent', 42)";
        Chunk(state, "for k in pairs(PAYLOAD) do PAYLOAD[k] = nil end DIRTY = false", "sent");
        Chunk(state, again, "again");
        var resent = new List<string>();
        for (var key = LuaValue.Nil; payload.TryGetNext(key, out var pair);) { key = pair.Key; resent.Add(pair.Key.ToString()); }
        var dirty = state.Environment["DIRTY"].TryRead<bool>(out var d) && d;
        check(resent.Count == 0 && !dirty, resent.Count == 0 && !dirty
            ? "markup runtime: a render that changes nothing sends nothing"
            : $"markup runtime: a render that changed nothing sent {string.Join(", ", resent)}{(dirty ? " (and marked the frame dirty)" : "")}");
        Chunk(state, "function __again(n) for i = 1, n do " + again + " end end __again(3)", "loop");
        var a0 = GC.GetTotalAllocatedBytes(true);
        Chunk(state, "__again(0)", "n");
        var a1 = GC.GetTotalAllocatedBytes(true);
        Chunk(state, "__again(1000)", "n");
        var a2 = GC.GetTotalAllocatedBytes(true);
        var perRender = (a2 - a1 - (a1 - a0)) / 1000.0;
        check(perRender < 1, $"markup runtime: re-rendering the same values allocates {perRender:0.#} B a render" + (perRender < 1 ? "" : ", where it should allocate nothing"));

        // Handed over again rather than skipped: another binding - a tab's state - wrote the label's
        // slot in between, so the renderer holds that text, and the label's own must go back.
        Chunk(state, "DOM.bind('frame', 'drive', 'tab') DOM.label('frame', 'label#0', 'abc', 'var(--x)')", "over");
        var back = Text(payload["lab"]);
        check(back == "Count ABC in #123456", back == "Count ABC in #123456"
            ? "markup runtime: an unchanged label whose slot another binding wrote is written again"
            : $"markup runtime: after a state wrote the label's slot, the unchanged label left \"{back}\"");

        // A value is data, not scene source: a quote in it is a quote, not an escaped one.
        Chunk(state, "DOM.label('frame', 'label#0', 'a\"b', 'var(--x)')", "quote");
        var quoted = Text(payload["lab"]);
        check(quoted == "Count A\"B in #123456", quoted == "Count A\"B in #123456"
            ? "markup runtime: a quote in a label's value draws as a quote"
            : $"markup runtime: a quote in a label's value arrived as \"{quoted}\"");

        static CompiledPage.StateValues Values(string name, params (string Slot, double Value)[] numbers)
        {
            var s = new CompiledPage.StateValues { Name = name };
            s.Numbers.AddRange(numbers);
            return s;
        }

        static CompiledPage.StateValues Titled(CompiledPage.StateValues s, (string Slot, string Value) text)
        {
            s.Text.Add(text);
            return s;
        }
    }

    /// <summary>
    /// A class that repaints a box can be left: the state for "no class" carries the resting colour
    /// back, as it carries a resting position back.
    /// </summary>
    private static void StateText(string root, Action<bool, string> check)
    {
        const string script = @"
const el = document.getElementById('a');
function set(on) { el.className = on ? 'hot' : ''; }
function tick() { set(Math.random() > 0.5); }
setInterval(tick, 100);";
        var available = new HashSet<string>(StringComparer.Ordinal) { "a", "a_x", "a_y", "a_w", "a_h", "a_f" };
        var compiled = CompiledPage.Compile(script, available,
            id => id == "a" ? new DomSlots.Box(outOfFlow: false, parentX: 0, parentY: 0, hasBackground: true, new List<(string, double, double)>()) : null,
            stateOf: (id, cls) =>
            {
                var s = new CompiledPage.StateValues();
                if (cls == "hot") s.Text.Add(("a_f", "#FF0000"));
                return s;
            },
            prelude: File.ReadAllText(Path.Combine(root, "JsPrelude.lua")),
            baseOf: slot => slot == "a_h" ? 20 : null,
            textOf: slot => slot == "a_f" ? "#333333" : null);
        if (compiled.Lua == null || compiled.Unmapped.Count > 0)
        {
            check(false, "state text: the page does not compile - " + string.Join("; ", compiled.Problems.Concat(compiled.Unmapped)));
            return;
        }
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        Chunk(state, compiled.Lua, "page");
        Chunk(state, "DOM.bind('a', 'className', 'hot') DOM.bind('a', 'className', '')", "writes");
        var colour = Text(state.Environment["PAYLOAD"].Read<LuaTable>()["a_f"]);
        check(colour == "#333333", colour == "#333333"
            ? "state text: leaving a class that repainted a box puts its resting colour back"
            : $"state text: after leaving the class the box is {colour}, not its resting #333333");

        // The same write reached from a timer's inline callback is as much a write at run time.
        const string inlineScript = @"
const el = document.getElementById('a');
function set(on) { el.className = on ? 'hot' : ''; }
setInterval(() => set(Math.random() > 0.5), 100);";
        var (inline, _) = DomWrites.Of(inlineScript);
        check(inline.Count == 1 && inline[0].Runtime, inline.Count == 1 && inline[0].Runtime
            ? "state text: a write in a function a timer's inline callback calls runs at run time"
            : "state text: a write in a function a timer's inline callback calls is taken for setup, so it is never bound");
    }

    /// <summary>
    /// `color` on a wrapper whose text is its child's: nothing on the wrapper takes it, so the few
    /// values the script assigns are each laid out and the child's colour is what moves - the cascade
    /// carrying it down as a browser does. And a write that reaches nothing drawn (an animation with
    /// no keyframes the scene can run) is dropped by name, never by refusing the whole page.
    /// </summary>
    private static void StyleStates(Action<bool, string> check)
    {
        const string page = @"<body style=""margin:0""><div id=""wrap"" style=""position:absolute;left:0;top:0;width:200px;height:30px;color:#ffffff""><span id=""a"">alarm</span></div>
<script>
const RED = '#ff0000', BLUE = '#0000ff';
let hot = false;
function tick() {
  hot = !hot;
  const tone = hot ? RED : BLUE;
  document.getElementById('wrap').style.color = tone;
  document.getElementById('wrap').style.animation = hot ? 'nothing 1s infinite' : 'none';
}
setInterval(tick, 500);
</script></body>";
        CompiledPage.Result compiled;
        try { (compiled, _) = Probe4.Headless(page); }
        catch (Exception ex) { check(false, "style states: compiling the page threw - " + ex.Message.Split('\n')[0]); return; }

        var bound = compiled.Bindings.Where(b => b.Key == "wrap.style.color" && b.Read == CompiledPage.Kind.State).ToList();
        var colours = bound.SelectMany(b => b.States!).Select(s => s.Text.FirstOrDefault(t => t.Slot == "a_f").Value ?? "").ToList();
        var red = colours.Any(c => c.StartsWith("#FF0000", StringComparison.OrdinalIgnoreCase));
        var blue = colours.Any(c => c.StartsWith("#0000FF", StringComparison.OrdinalIgnoreCase));
        check(red && blue, red && blue
            ? "style states: `color` on a wrapper is a state per value, moving its child's text colour"
            : "style states: `color` on a wrapper - " + (bound.Count == 0
                ? "not bound (" + string.Join("; ", compiled.Unmapped) + ")"
                : "the child's colour per state is " + string.Join(", ", colours)));

        var dropped = compiled.Unmapped.Count == 1 && compiled.Unmapped[0].Contains("animation", StringComparison.Ordinal);
        check(compiled.Ok && dropped, compiled.Ok && dropped
            ? "style states: a write that reaches nothing drawn is named and dropped, and the page still compiles"
            : $"style states: ok={compiled.Ok}, unmapped: {string.Join("; ", compiled.Unmapped)}; problems: {string.Join("; ", compiled.Problems)}");
    }

    /// <summary>
    /// A listener added to an element the markup names, on every render: in a browser the write
    /// replaced the element and its old listener went with it, so there is only ever one. A compiled
    /// page keeps one element per id, and used to keep every render's listener too.
    /// </summary>
    private static void ReplacedListeners(Action<bool, string> check)
    {
        const string page = @"<body style=""margin:0""><div id=""frame"" style=""width:200px;height:100px""></div>
<script>
let n = 0;
function render() {
  n++;
  document.getElementById('frame').innerHTML = '<div id=""list"" style=""height:40px"">row ' + n + '</div>';
  document.getElementById('list').addEventListener('scroll', () => { n = 0; });
}
render();
setInterval(render, 100);
</script></body>";
        CompiledPage.Result compiled;
        try { (compiled, _) = Probe4.Headless(page); }
        catch (Exception ex) { check(false, "replaced listeners: compiling the page threw - " + ex.Message.Split('\n')[0]); return; }
        if (!compiled.Ok) { check(false, "replaced listeners: the page does not compile - " + string.Join("; ", compiled.Problems)); return; }
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        try
        {
            Chunk(state, compiled.Lua!, "page");
            Chunk(state, "for i = 1, 20 do frame(1) end LISTENING = #DOM.listeners.list.scroll", "frames");
        }
        catch (Exception ex) { check(false, "replaced listeners: running the page threw - " + ex.Message.Split('\n')[0]); return; }
        var count = state.Environment["LISTENING"].TryRead<double>(out var c) ? c : -1;
        check(count == 1, count == 1
            ? "replaced listeners: a listener added on every render to an element the markup replaces is held once"
            : $"replaced listeners: after 21 renders the element holds {count} listeners");
    }

    /// <summary>
    /// What the chunk reads for a value of markup built through the page's helpers: a field of a
    /// literal read off it, a field of a name that exists read where it exists, and a table read
    /// with a key from a known few never read as undefined.
    /// </summary>
    private static void MarkupValues(string root, Action<bool, string> check)
    {
        const string script = @"
const FILL = { steel: '#111', live: '#222' };
const st = { v: 3, sel: 'a' };
function panel(o) { return '<div style=""background:' + FILL[o.tone || 'steel'] + '"">' + o.note + '</div>'; }
function readout(v) { return '<b>' + v.status + '</b>'; }
function values() { return { tone: st.v > 2 ? 'live' : undefined, status: 'x' + st.v }; }
function render() {
  const v = values();
  const frame = document.getElementById('frame');
  frame.innerHTML = panel({ tone: v.tone, note: v.status }) + readout(v);
}
function bump() { st.v += 1; st.sel = 'b'; render(); }
render();";
        var ast = new Acornima.Parser().ParseScript(script);
        var write = PageCompiler.InnerHtmlWrites(ast).First().Write;
        var m = ScriptedScreensHtml.Markup.Of(write, ast, script);
        var fill = m.Holes.Select(h => m.Enumerate(h.Value)).FirstOrDefault(e => e != null && e.Contains("#111"));
        check(fill != null && !fill.Contains("undefined"), fill == null
            ? "markup values: FILL[o.tone || 'steel'] is not drawn from its table"
            : !fill.Contains("undefined")
                ? $"markup values: a table read with one of a few keys is one of its entries ({string.Join(", ", fill)}), never undefined"
                : $"markup values: a table read with a known key can be undefined ({string.Join(", ", fill)})");
        var js = m.Holes.Select(h => m.Js(h.Value)).ToList();
        check(js.Contains("(v.status)"), js.Contains("(v.status)")
            ? "markup values: a helper argument's field is read off the literal it was given - `v.status`"
            : "markup values: o.note reads " + string.Join(" | ", js));
        var status = js.Where(j => j != null && j.Contains("status", StringComparison.Ordinal)).ToList();
        check(status.Count == 2 && status.All(j => !j!.Contains("st.v", StringComparison.Ordinal)), status.All(j => !j!.Contains("st.v", StringComparison.Ordinal))
            ? "markup values: a helper's parameter holding the render's own `v` is read where the page computed it, not computed again"
            : "markup values: `v.status` through readout(v) is computed again - " + string.Join(" | ", status));

        // A list the page maps from another, drawn row by row: each row reads its own element of the
        // list it came from, so a row's state is that row's.
        var dark = File.ReadAllText(Path.Combine(root, "AtmoDark.lua"));
        var html = MarkupProbe.Bracketed(dark) ?? dark;
        var code = string.Concat(Regex.Matches(html, "<script[^>]*>(.*?)</script>", RegexOptions.Singleline).Select(x => x.Groups[1].Value + "\n"));
        var dast = new Acornima.Parser().ParseScript(code);
        var dm = ScriptedScreensHtml.Markup.Of(PageCompiler.InnerHtmlWrites(dast).First().Write, dast, code);
        var missing = dm.Holes.Count(h => dm.Js(h.Value) == null);
        check(missing == 0, missing == 0
            ? $"markup values: every one of AtmoDark's {dm.Holes.Count} values can be read by the chunk, candidate rows included"
            : $"markup values: {missing} of AtmoDark's values cannot be read by the chunk");
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

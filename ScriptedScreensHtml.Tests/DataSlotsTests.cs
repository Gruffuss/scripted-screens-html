using System;
using System.Collections.Generic;
using System.Linq;
using ScriptedScreensHtml;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

// DataSlots reads ScriptedScreens' value types and nothing else of the game, so the three it
// touches are stood in for here, shaped as the decompile has them (UiValue, UiProp, UiValueType),
// and the real DataSlots.cs compiles against them.
namespace ScriptedScreens.ScriptableUi
{
    internal static class ScriptedScreensScriptableUiSystem
    {
        internal enum UiValueType { Nil, Number, Bool, String, Array, Map }

        internal struct UiValue
        {
            public UiValueType Type;
            public float Number;
            public bool Bool;
            public string String;
            public UiValue[] Array;
            public UiProp[] Map;
        }

        internal struct UiProp
        {
            public string Key;
            public UiValue Value;
        }
    }
}

namespace ScriptedScreensHtml.Tests
{

/// <summary>
/// The data fast path's table: how a page's data compiles against the scene that drew it, what a
/// later payload writes, and how a key it cannot place is refused - that key, never the page. The
/// mapping itself is CompiledPageTests' (DataSlotsFixture); this is the half that runs every tick.
/// </summary>
internal static class DataSlotsTests
{
    private static SS.UiValue S(string s) => new() { Type = SS.UiValueType.String, String = s };
    private static SS.UiValue N(float n) => new() { Type = SS.UiValueType.Number, Number = n };
    private static SS.UiValue B(bool b) => new() { Type = SS.UiValueType.Bool, Bool = b };
    private static SS.UiValue A(params float[] n) => new() { Type = SS.UiValueType.Array, Array = n.Select(N).ToArray() };
    private static SS.UiValue M(params (string Key, string Value)[] d) => new()
    {
        Type = SS.UiValueType.Map,
        Map = d.Select(x => new SS.UiProp { Key = x.Key, Value = S(x.Value) }).ToArray(),
    };
    private static List<KeyValuePair<string, SS.UiValue>> P(params (string Key, SS.UiValue Value)[] e)
        => e.Select(x => new KeyValuePair<string, SS.UiValue>(x.Key, x.Value)).ToList();

    private static double Num(Dictionary<string, SceneSlots.Value> v, string k) => v.TryGetValue(k, out var x) && x.IsNumber ? x.Number : double.NaN;
    private static string? Txt(Dictionary<string, SceneSlots.Value> v, string k) => v.TryGetValue(k, out var x) && !x.IsNumber ? x.Text : null;

    internal static void Run(Action<bool, string> check)
    {
        // The 02-live-data shapes: a bar (a block in a 448-wide block track), text spans, an
        // out-of-flow box `e` holding `k` (one `left` reaches two slots), a lamp whose opacity group
        // was named, and the alarm whose bool hides it and closes the gap under it.
        var bar = new DomSlots.Box(false, 16, 100, true, null, 448, 14, 0, 14, localWidth: true, contentW: 448, contentH: 14);
        var span = new DomSlots.Box(false, 0, 0, false);
        var e = new DomSlots.Box(true, 100, 0, true, new[] { ("k", 5.0, 5.0) }, 300, 200, 50, 20);
        DomSlots.Box? BoxOf(string id) => id switch
        {
            "bar" => bar, "e" => e, "pressure" or "temp" or "alarm" or "lamp" or "note" or "tab" or "caps" => span, _ => null,
        };
        var ids = new[] { "pressure", "temp", "bar", "e", "alarm", "lamp", "note", "tab", "caps", "__7" };
        var shapes = new[] { "graph" };
        IReadOnlyDictionary<string, string>? CssOf(string id) =>
            id == "bar" ? new Dictionary<string, string> { ["transition"] = "width 0.6s ease, background-color 0.3s linear", ["border-radius"] = "7px" }
            : id == "tab" ? new Dictionary<string, string> { ["font-variant-numeric"] = "tabular-nums" }
            : id == "caps" ? new Dictionary<string, string> { ["text-transform"] = " Uppercase" } : null;
        Dictionary<string, SceneSlots.Value> Scene() => new(StringComparer.Ordinal)
        {
            ["pressure"] = new("--"), ["temp"] = new("<noparse>290.1</noparse>"), ["tab"] = new("--"), ["caps"] = new("RUNNING"),
            ["bar_w"] = new(278f), ["bar_h"] = new(14f), ["bar_f"] = new("#2E8B6E"), ["bar_rx"] = new(7f),
            ["e_x"] = new(110f), ["e_y"] = new(0f), ["k_x"] = new(115f), ["k_y"] = new(5f),
            ["lamp_o"] = new(0.5f), ["alarm"] = new("OVER PRESSURE"), ["alarm_o"] = new(1f),
            ["note_y"] = new(172.3f), ["body_h"] = new(480f), ["foot_y"] = new(300f), ["L3_x"] = new(4f),
        };
        // What ToggleOf captures: two more layouts of the page. body_h is identical in both, foot_y
        // differs by layout noise, and both differ from the live scene by a fraction of a pixel -
        // the in-game failure was the proof reading body 479.96 against the 480 drawn.
        DomSlots.Toggle ToggleOf(string id)
        {
            var t = new DomSlots.Toggle();
            t.Shown.Add(("alarm_o", 1)); t.Shown.Add(("note_y", 172)); t.Shown.Add(("body_h", 479.96)); t.Shown.Add(("foot_y", 300));
            t.Hidden.Add(("alarm_o", 0)); t.Hidden.Add(("note_y", 121)); t.Hidden.Add(("body_h", 479.96)); t.Hidden.Add(("foot_y", 300.2));
            return t;
        }
        var warned = new List<string>();
        DataSlots Compile(List<KeyValuePair<string, SS.UiValue>> sample, Dictionary<string, SceneSlots.Value>? scene = null, string template = "")
            => DataSlots.Compile(sample, scene ?? Scene(), template, ids, (id, _) => BoxOf(id), CssOf, shapes, ToggleOf, warned.Add);
        string Said() => warned.Count == 0 ? "(nothing said)" : string.Join(" | ", warned);

        // ---- compile: every key of the first payload, proven against the scene that drew it ----
        var first = P(("pressure", S("--")), ("temp", N(290.1f)), ("bar", M(("width", "62%"), ("background-color", "#2E8B6E"))),
                      ("e", M(("left", "10px"))), ("alarm", B(false)), ("lamp", M(("opacity", "50%"))));
        var map = Compile(first);
        check(warned.Count == 0, "data compile: the first payload maps and proves with nothing refused - " + Said());
        // the emitter guards a purely numeric label with <noparse>; a data value needs no guard
        check(warned.All(w => !w.Contains("temp")), "data compile: a number drawn as <noparse>290.1</noparse> agrees with 290.1");

        // Declarations in the other order, one declaration on two slots, a number where text was.
        var v = map.Apply(P(("bar", M(("background-color", "#B5352C"), ("width", "50%"))), ("e", M(("left", "20px"))), ("pressure", N(58.9f))));
        check(Num(v, "bar_w") == 224 && Txt(v, "bar_f") == "#B5352C", "data apply: declarations are matched to their targets by name, not position");
        check(Num(v, "e_x") == 120 && Num(v, "k_x") == 125, "data apply: one declaration reaching two slots writes both (e_x, k_x)");
        check(Txt(v, "pressure") == "58.9", "data apply: a number where the first payload sent text still lands as text - " + Txt(v, "pressure"));
        check(map.TryEase("bar_w", out var ease) && Math.Abs(ease.Dur - 0.6f) < 1e-4 && ease.Curve == "ease",
              "data apply: a transitioned width glides with the CSS's own timing");
        // A 7px radius on a bar 4px wide overflows its sides: CSS - and the emitter - scale it to 2.
        v = map.Apply(P(("bar", M(("width", "1%")))));
        var (narrow, narrowRx) = (Num(v, "bar_w"), Num(v, "bar_rx"));   // Apply's result is reused by the next call
        check(narrow == 4 && narrowRx == 2 && Num(map.Apply(P(("bar", M(("width", "50%"))))), "bar_rx") == 7,
              $"data apply: the corner radius is clamped with the width, as the emitter clamps it (rx {narrowRx} at {narrow}px)");

        // ---- the bool, compiled from the LIVE scene (the in-game body_h failure) ----
        var hidden = map.Apply(P(("alarm", B(false))));
        check(!hidden.ContainsKey("body_h") && !hidden.ContainsKey("foot_y"),
              "data bool: a slot equal in both captures, or apart by layout noise, is not in the state (body_h, foot_y)");
        check(Num(hidden, "alarm_o") == 0 && Math.Abs(Num(hidden, "note_y") - 121.3) < 1e-3,
              $"data bool: hidden is the live value plus how far hiding moved it (note_y {Num(hidden, "note_y")}, want 121.3)");
        var shown = map.Apply(P(("alarm", B(true))));
        check(Num(shown, "alarm_o") == 1 && Math.Abs(Num(shown, "note_y") - 172.3) < 1e-3,
              $"data bool: shown is what the emitter drew, not the capture (note_y {Num(shown, "note_y")}, want 172.3)");

        // ---- per key, never the page ----
        warned.Clear();
        v = map.Apply(P(("bar", M(("width", "1em"), ("background-color", "#123456"))), ("pressure", S("60"))));
        check(!v.ContainsKey("bar_w") && !v.ContainsKey("bar_f") && Txt(v, "pressure") == "60",
              "data apply: a value it cannot place drops that key whole for that payload, and the others apply");
        check(warned.Count == 1 && warned[0].Contains("\"bar\".width = \"1em\""), "data apply: the unplaceable value is named - " + Said());
        map.Apply(P(("bar", M(("width", "2em")))));
        check(warned.Count == 1, "data apply: said once, not per tick - " + Said());

        // A declaration and a key first seen after the compile map on arrival, from what was copied.
        warned.Clear();
        v = map.Apply(P(("e", M(("top", "30px"), ("left", "5px")))));
        check(Num(v, "e_y") == 30 && Num(v, "k_y") == 35 && Num(v, "e_x") == 105, "data late: a declaration the first payload did not carry maps on arrival - " + Said());
        v = map.Apply(P(("bar", M(("background-color", "red")))));
        check(Txt(v, "bar_f") == "#FF0000" && map.TryEase("bar_f", out var colourEase) && colourEase.Curve == "linear",
              "data late: a colour keyword lands as the hex the emitter writes, with its own transition - " + Txt(v, "bar_f"));
        v = map.Apply(P(("lamp", M(("visibility", "hidden")))));
        check(Num(v, "lamp_o") == 0, "data late: visibility hidden is the group's opacity at zero");
        v = map.Apply(P(("bar", M(("height", "30px"), ("width", "10%")))));
        check(Num(v, "bar_w") == 45 && !v.ContainsKey("bar_h") && warned.Any(w => w.Contains("\"bar\".height") && w.Contains("moves its siblings")),
              "data late: a declaration with no slot is refused alone, saying why, and the key's others apply - " + Said());

        // A key naming no element drops that key; one the scene reads as $name is forwarded, silently.
        warned.Clear();
        var withGhost = Compile(P(("ghost", S("x")), ("pressure", S("--")), ("co2", M(("fill", "0.5"))), ("graph", A(1, 2, 3))),
                                template: "R x=$L3_x\nYS y==$co2_fill*2\nT text=\"$pressure\"");
        check(warned.Count == 1 && warned[0].Contains("\"ghost\" names no element"),
              "data compile: a key naming no element is refused alone, once; an svg shape and a $name input are not - " + Said());
        check(withGhost.ReadsAny && withGhost.Reads("co2_fill") && !withGhost.Reads("pressure") && !withGhost.Reads("L3_x"),
              "data compile: only the names the scene reads that are not its own slots are forwarded");
        v = withGhost.Apply(P(("ghost", S("y")), ("pressure", S("7")), ("graph", A(3, 2, 1))));
        check(Txt(v, "pressure") == "7" && v.Count == 1 && warned.Count == 1, "data apply: the dropped key and the shape add nothing, and nothing more is said");
        withGhost.Apply(P(("graph", S("0,0 1,1"))));
        check(warned.Count == 2 && warned[1].Contains("svg shape"), "data apply: a shape's points string is its structure, said once - " + Said());

        // A proof that disagrees refuses that key; the rest of the page still compiles.
        warned.Clear();
        var off = Scene();
        off["bar_w"] = new(200f);
        var disagree = Compile(P(("bar", M(("width", "62%"))), ("pressure", S("--"))), off);
        check(warned.Count == 1 && warned[0].Contains("bar_w") && warned[0].Contains("200"), "data compile: a disagreement names the slot - " + Said());
        v = disagree.Apply(P(("bar", M(("width", "10%"))), ("pressure", S("9"))));
        check(!v.ContainsKey("bar_w") && Txt(v, "pressure") == "9", "data compile: the disagreeing key is dropped, the others apply");

        // A bool first sent after the compile: its states are layouts, and the page is gone.
        warned.Clear();
        var late = Compile(P(("pressure", S("--"))));
        v = late.Apply(P(("alarm", B(false)), ("pressure", S("1"))));
        check(!v.ContainsKey("alarm_o") && Txt(v, "pressure") == "1" && warned.Count == 1 && warned[0].Contains("first sent after the page was compiled"),
              "data late: a bool first sent after the compile is refused, loudly, alone - " + Said());
        v = late.Apply(P(("temp", N(3f))));
        check(Txt(v, "temp") == "3", "data late: a key first sent after the compile maps on arrival, from the boxes copied then");
        warned.Clear();
        v = late.Apply(P(("tab", S("12"))));
        check(Txt(v, "tab") == "12" && warned.Count == 1 && warned[0].Contains("tabular-nums"),
              "data late: a tabular label that drew no digit at compile says its digits are drawn proportional - " + Said());

        // mspace read off the scene when the page compiles: a later value is shaped the same way.
        var tabular = Scene();
        tabular["pressure"] = new("<mspace=0.57em>58</mspace>.<mspace=0.57em>9</mspace>");
        v = Compile(P(("pressure", S("58.9"))), tabular).Apply(P(("pressure", S("60.25"))));
        check(Txt(v, "pressure") == "<mspace=0.57em>60</mspace>.<mspace=0.57em>25</mspace>", "data apply: tabular digits are monospaced as the emitter does - " + Txt(v, "pressure"));

        // text-transform: the emitter drew the proof's value cased, so the table cases it too - raw, the
        // proof disagreed and the key was refused.
        warned.Clear();
        v = Compile(P(("caps", S("running")))).Apply(P(("caps", S("idle 2"))));
        check(warned.Count == 0 && Txt(v, "caps") == "IDLE 2",
              $"data text: text-transform holds - the proof agrees with the cased label, and a later value is cased too ({Txt(v, "caps")}) - " + Said());

        // What a scene emitted after the compile shows: the latest values, bools included.
        map.Apply(P(("alarm", B(false)), ("pressure", S("42"))));
        var again = Scene();
        map.Overlay(again);
        check(Num(again, "alarm_o") == 0 && Math.Abs(Num(again, "note_y") - 121.3) < 1e-3 && Txt(again, "pressure") == "42" && Num(again, "body_h") == 480,
              "data overlay: a re-emitted scene carries the latest values and the bool's state, and nothing it never wrote");

        // Zero allocation per tick once warm: the payload's own list is the caller's; this adds nothing.
        var tick = P(("pressure", S("58.9")), ("temp", N(291f)), ("caps", S("running")), ("bar", M(("width", "50%"), ("background-color", "#2E8B6E"))), ("alarm", B(true)), ("e", M(("left", "3px"))));
        map.Apply(tick);
        map.Apply(tick);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) map.Apply(tick);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        check(bytes == 0, $"data apply: a warm tick allocates nothing ({bytes} bytes over 100 ticks)");
    }
}
}

using System;
using System.Collections.Generic;
using System.Linq;
using ScriptedScreensHtml;
using UnityEngine;
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
        // needle: a 10x40 box turned by its transform group; card: a block in flow whose height moves
        // a named sibling and an unnamed parent; gauge: a custom property read by two elements below it
        var needle = new DomSlots.Box(true, 0, 0, true, null, 300, 200, 10, 40);
        DomSlots.Box? BoxOf(string id) => id switch
        {
            "bar" => bar, "e" => e, "needle" => needle,
            "pressure" or "temp" or "alarm" or "lamp" or "note" or "tab" or "caps" or "card" or "gauge" or "gauge2" or "fill" or "needle2" or "tank-1" => span,
            _ => null,
        };
        var ids = new[] { "pressure", "temp", "bar", "e", "alarm", "lamp", "note", "tab", "caps", "__7", "needle", "card", "gauge", "gauge2", "fill", "needle2", "tank-1" };
        var shapes = new[] { "graph" };
        IReadOnlyDictionary<string, string>? CssOf(string id) =>
            id == "bar" ? new Dictionary<string, string> { ["transition"] = "width 0.6s ease, background-color 0.3s linear", ["border-radius"] = "7px" }
            : id == "tab" ? new Dictionary<string, string> { ["font-variant-numeric"] = "tabular-nums" }
            : id == "caps" ? new Dictionary<string, string> { ["text-transform"] = " Uppercase" }
            : id == "needle" ? new Dictionary<string, string> { ["transition"] = "transform 0.4s ease-out" }
            : id == "card" ? new Dictionary<string, string> { ["transition"] = "height 0.3s linear" }
            : id == "needle2" ? new Dictionary<string, string> { ["transition"] = "transform 0.5s ease" } : null;
        Dictionary<string, SceneSlots.Value> Scene() => new(StringComparer.Ordinal)
        {
            ["pressure"] = new("--"), ["temp"] = new("<noparse>290.1</noparse>"), ["tab"] = new("--"), ["caps"] = new("RUNNING"),
            ["bar_w"] = new(278f), ["bar_h"] = new(14f), ["bar_f"] = new("#2E8B6E"), ["bar_rx"] = new(7f),
            ["e_x"] = new(110f), ["e_y"] = new(0f), ["k_x"] = new(115f), ["k_y"] = new(5f), ["e_a_0"] = new(135f), ["e_a_1"] = new(10f),
            ["lamp_o"] = new(0.5f), ["alarm"] = new("OVER PRESSURE"), ["alarm_o"] = new(1f),
            ["note_y"] = new(172.3f), ["body_h"] = new(480f), ["foot_y"] = new(300f), ["L3_x"] = new(4f),
            ["needle_t_0"] = new(0f), ["needle_t_1"] = new(0f), ["needle_r"] = new(45f), ["needle_s_0"] = new(1f), ["needle_s_1"] = new(1f),
            ["card_h"] = new(40f), ["card_y"] = new(100f), ["note2_y"] = new(170f), ["L5_h"] = new(100f),
            ["fill_w"] = new(278f), ["needle2_r"] = new(167.4f), ["tank_1"] = new("--"),
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
        DataSlots Compile(List<KeyValuePair<string, SS.UiValue>> sample, Dictionary<string, SceneSlots.Value>? scene = null, string template = "",
                          Func<string, string, string, (string, Dictionary<string, SceneSlots.Value>)?>? sampleOf = null, string? sendPrefix = null)
            => DataSlots.Compile(sample, scene ?? Scene(), template, ids, (id, _) => BoxOf(id), CssOf, shapes, ToggleOf, warned.Add, sampleOf, "L", sendPrefix);
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
        map.Apply(P(("bar", M(("width", "1%")))));
        check(Num(map.Apply(P(("bar", M(("border-radius", "7px"))))), "bar_rx") == 2,
              "data apply: a radius sent alone is clamped against the width the bar has now, not the one it was compiled with");
        map.Apply(P(("bar", M(("width", "50%")))));

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
        check(Num(v, "e_a_0") == 130 && Num(v, "e_a_1") == 40,
              $"data apply: a turned element moved by left and top takes its turn's origin with it (e_a {Num(v, "e_a_0")},{Num(v, "e_a_1")})");
        v = map.Apply(P(("bar", M(("background-color", "red")))));
        check(Txt(v, "bar_f") == "#FF0000" && map.TryEase("bar_f", out var colourEase) && colourEase.Curve == "linear",
              "data late: a colour keyword lands as the hex the emitter writes, with its own transition - " + Txt(v, "bar_f"));
        var hsla = Txt(map.Apply(P(("bar", M(("background-color", "hsla(120, 100%, 25%, 0.5)"))))), "bar_f");
        var none = Txt(map.Apply(P(("bar", M(("background-color", "none"))))), "bar_f");
        check(hsla == "#00800080" && none == "#00000000", $"data late: hsla() and a background's none land as hex ({hsla}, {none})");
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

        // ---- a transform: the element's group, five slots, turned about its transform-origin ----
        warned.Clear();
        var turn = Compile(P(("needle", M(("transform", "rotate(45deg)")))));
        check(warned.Count == 0, "data transform: rotate(45deg) proves against the named group - " + Said());
        v = turn.Apply(P(("needle", M(("transform", "rotate(90deg)")))));
        check(Num(v, "needle_r") == 90 && Num(v, "needle_t_0") == 0 && Num(v, "needle_t_1") == 0 && Num(v, "needle_s_0") == 1 && Num(v, "needle_s_1") == 1,
              "data transform: a rotation writes the turn and puts translate and scale at rest");
        v = turn.Apply(P(("needle", M(("transform", "translate(-50%, 10px) rotate(30deg) scale(2)")))));
        check(Num(v, "needle_t_0") == -5 && Num(v, "needle_t_1") == 10 && Num(v, "needle_r") == 30 && Num(v, "needle_s_0") == 2 && Num(v, "needle_s_1") == 2,
              $"data transform: translate (a % of the element's own box), rotate and scale together ({Num(v, "needle_t_0")},{Num(v, "needle_t_1")} r{Num(v, "needle_r")} s{Num(v, "needle_s_0")})");
        v = turn.Apply(P(("needle", M(("transform", "rotate(90deg) translateX(10px)")))));
        check(Math.Abs(Num(v, "needle_t_0")) < 1e-4 && Math.Abs(Num(v, "needle_t_1") - 10) < 1e-4 && Math.Abs(Num(v, "needle_r") - 90) < 1e-4,
              $"data transform: a translate after the turn goes along the turned axis, as CSS composes it (t {Num(v, "needle_t_0")},{Num(v, "needle_t_1")})");
        v = turn.Apply(P(("needle", M(("transform", "rotate(370deg)")))));
        check(Num(v, "needle_r") == 370, "data transform: a turn past a full circle is kept whole, so a glide from 350deg goes 20 degrees, not back round");
        check(turn.TryEase("needle_r", out var turnEase) && Math.Abs(turnEase.Dur - 0.4f) < 1e-4 && turnEase.Curve == "ease-out",
              "data transform: a transitioned transform glides with the CSS's own timing");
        warned.Clear();
        v = turn.Apply(P(("needle", M(("transform", "skewX(10deg)"))), ("pressure", S("3"))));
        check(!v.ContainsKey("needle_r") && Txt(v, "pressure") == "3" && warned.Count == 1 && warned[0].Contains("skew"),
              "data transform: a skew is refused for that payload, loudly, and the other keys apply - " + Said());
        warned.Clear();
        late = Compile(P(("pressure", S("--"))));
        late.Apply(P(("lamp", M(("transform", "rotate(3deg)")))));
        check(warned.Count == 1 && warned[0].Contains("transform group was not named"),
              "data transform: a group the first payload never named is refused, saying so - " + Said());

        // ---- display: the bool's two states under the CSS spelling ----
        warned.Clear();
        var shownBy = Compile(P(("alarm", M(("display", "block")))));
        v = shownBy.Apply(P(("alarm", M(("display", "none")))));
        check(warned.Count == 0 && Num(v, "alarm_o") == 0 && Math.Abs(Num(v, "note_y") - 121.3) < 1e-3,
              $"data display: none is the hidden state, and what follows closes the gap (note_y {Num(v, "note_y")}) - " + Said());
        v = shownBy.Apply(P(("alarm", M(("display", "flex")))));
        check(Num(v, "alarm_o") == 1 && Math.Abs(Num(v, "note_y") - 172.3) < 1e-3, "data display: any other value is the shown state");

        // ---- measured from the page: a height in flow, a custom property ----
        // What PageCompiler would lay out, as numbers: card's height moves a named sibling and an
        // unnamed parent one for one, and rounds as a layout does; --p sizes a fill and turns a needle.
        var samples = 0;
        var sampledProperties = new HashSet<string>();
        (string, Dictionary<string, SceneSlots.Value>)? Sampler(string id, string property, string value)
        {
            samples++;
            sampledProperties.Add(property);
            var n = double.Parse(new string(value.TakeWhile(c => char.IsDigit(c) || c is '.' or '-').ToArray()), System.Globalization.CultureInfo.InvariantCulture);
            var at = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal) { ["pressure"] = new("--") };
            var template = "LIVE";
            switch (id, property)
            {
                case ("card", "height"):
                    at["card_h"] = new((float)n); at["note2_y"] = new((float)Math.Round(130 + n)); at["L5_h"] = new((float)(60 + n)); at["card_y"] = new(100f);
                    break;
                case ("card", "margin-top"): at["card_y"] = new((float)Math.Min(100 + n, 150)); break;        // clamped
                case ("card", "padding-top"): at["card_h"] = new((float)n); template = n > 50 ? "OTHER" : "LIVE"; break;
                case ("card", "padding-left"): at["L5_w"] = new((float)(200 + n)); template = "ELSEWHERE"; break;
                case ("gauge", "--p"): at["fill_w"] = new((float)Math.Round(n * 448)); at["needle2_r"] = new((float)(n * 270)); break;
                case ("gauge", "--c"): at["fill_f"] = new(n > 0.3 ? "#FF0000" : "#00FF00"); break;
            }
            return (template, at);
        }
        warned.Clear();
        var measured = Compile(P(("card", M(("height", "40px"))), ("gauge", M(("--p", "0.62")))), template: "LIVE", sampleOf: Sampler, sendPrefix: "M");
        check(warned.Count == 0 && samples == 6, $"data measured: a height in flow and a custom property each lay the page out three times, and fit ({samples} layouts) - " + Said());
        v = measured.Apply(P(("card", M(("height", "65px")))));
        check(Num(v, "card_h") == 65 && Num(v, "note2_y") == 195 && Num(v, "M5_h") == 125 && !v.ContainsKey("L5_h"),
              $"data measured: the height moves its sibling and its parent, the unnamed one under the name it is sent with (card_h {Num(v, "card_h")}, note2_y {Num(v, "note2_y")}, M5_h {Num(v, "M5_h")})");
        v = measured.Apply(P(("card", M(("height", "70")))));
        check(Num(v, "card_h") == 70, "data measured: a bare number is px, as it is for a length");
        check(measured.TryEase("note2_y", out var siblingEase) && Math.Abs(siblingEase.Dur - 0.3f) < 1e-4 && measured.TryEase("M5_h", out _),
              "data measured: a transitioned height glides everything it moves on its curve, as a browser reflows each frame");
        v = measured.Apply(P(("gauge", M(("--p", "0.5")))));
        check(Math.Abs(Num(v, "fill_w") - 224) < 1 && Math.Abs(Num(v, "needle2_r") - 135) < 1e-3,
              $"data measured: one custom property drives a width and a turn (fill_w {Num(v, "fill_w")}, needle2_r {Num(v, "needle2_r")})");
        v = measured.Apply(P(("gauge", new SS.UiValue { Type = SS.UiValueType.Map, Map = new[] { new SS.UiProp { Key = "--p", Value = N(0.25f) } } })));
        check(Math.Abs(Num(v, "needle2_r") - 67.5) < 1e-3, "data measured: a number sent as a number is the same value");
        check(!measured.TryEase("fill_w", out _) && measured.TryEase("needle2_r", out var needleEase) && Math.Abs(needleEase.Dur - 0.5f) < 1e-4,
              "data measured: a custom property glides only what transitions on its own element, with that element's timing");
        warned.Clear();
        v = measured.Apply(P(("card", M(("height", "2em"))), ("pressure", S("8"))));
        check(!v.ContainsKey("card_h") && Txt(v, "pressure") == "8" && warned.Count == 1 && warned[0].Contains("px"),
              "data measured: a value in another unit is another slope, refused for that payload - " + Said());
        warned.Clear();
        measured.Apply(P(("gauge2", M(("--p", "0.5")))));
        check(warned.Count == 1 && warned[0].Contains("first payload"), "data measured: first sent after the compile, it is refused, saying when to send it - " + Said());

        warned.Clear();
        Compile(P(("card", M(("margin-top", "40px"), ("padding-top", "40px"), ("padding-left", "10px"), ("outline-color", "#FF0000"))), ("gauge", M(("--c", "0.2")))), template: "LIVE", sampleOf: Sampler);
        string? Said1(string what) => warned.FirstOrDefault(w => w.Contains(what));
        check(Said1("margin-top")?.Contains("120 at 20px, 140 at 40px and 150 at 60px - not a straight line") == true,
              "data measured: a clamp is refused with the numbers that show it - " + Said1("margin-top"));
        check(Said1("padding-top")?.Contains("structure") == true, "data measured: a value that changes the structure is a state, refused - " + Said1("padding-top"));
        check(Said1("padding-left")?.Contains("give that element an id") == true, "data measured: an unnamed line the live scene does not line up with is refused, naming the fix - " + Said1("padding-left"));
        check(Said1("--c")?.Contains("not a number") == true, "data measured: a colour that changes with it is refused - " + Said1("--c"));
        check(Said1("outline-color")?.Contains("has no equivalent in the scene") == true && !sampledProperties.Contains("outline-color"),
              "data measured: a colour with no slot is not measured, and says why it has none - " + Said1("outline-color"));

        // ---- an id with a dash: its slots are named with an underscore ----
        warned.Clear();
        v = Compile(P(("tank-1", S("--")))).Apply(P(("tank-1", S("51 kPa"))));
        check(warned.Count == 0 && Txt(v, "tank_1") == "51 kPa", "data: an id with a dash writes the slot the scene names with an underscore - " + Said());

        // Still zero once warm, with transforms that change and measured values.
        var tickA = P(("needle", M(("transform", "rotate(10deg)"))), ("card", M(("height", "50px"))), ("gauge", M(("--p", "0.4"))));
        var tickB = P(("needle", M(("transform", "translate(2px, 3px) rotate(20deg)"))), ("card", M(("height", "52px"))), ("gauge", M(("--p", "0.6"))));
        var both = Compile(P(("needle", M(("transform", "rotate(45deg)"))), ("card", M(("height", "40px"))), ("gauge", M(("--p", "0.62")))), template: "LIVE", sampleOf: Sampler);
        both.Apply(tickA); both.Apply(tickB);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) both.Apply(i % 2 == 0 ? tickA : tickB);
        bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        check(bytes == 0, $"data apply: a warm tick that turns a needle and moves measured slots allocates nothing ({bytes} bytes over 100 ticks)");

        RealPage(check);
    }

    /// <summary>
    /// The same three things on a real page, through the real cascade, layout and emitter: bound as a
    /// surface binds a first payload, compiled with the real measurement, and then what the table
    /// writes for new values compared with the page laid out again at those values - the ground
    /// truth a browser-faithful table has to match, slot for slot, named or not.
    /// </summary>
    private static void RealPage(Action<bool, string> check)
    {
        const string html = "<html><head><meta name=\"viewport\" content=\"width=480\"><style>"
            + "body{padding:10px;font-size:14px;color:#fff}"
            + ".card{background:#123456;padding:4px}"
            + "#grow{height:20px;background:#00ff00}"
            + ".gauge{position:relative;width:200px;height:40px}"
            + ".fill{height:10px;width:calc(var(--p) * 100%);background:#ff0000}"
            + ".half{height:6px;width:calc(var(--p) * 50%);background:#0000ff}"
            + ".needle{width:4px;height:30px;background:#ffffff;transform:rotate(calc(var(--p) * 270deg))}"
            + "#dial{width:10px;height:40px;background:#ffffff;transform-origin:50% 100%}"
            + "#nudge{position:relative;height:8px;background:#ff00ff}"
            + "</style></head><body>"
            + "<div class=\"card\"><div id=\"grow\"></div><div id=\"below\">below</div></div>"
            + "<div class=\"gauge\" id=\"gauge\" style=\"--p:0\"><div class=\"fill\" id=\"fill\"></div><div class=\"half\"></div><div class=\"needle\" id=\"needle\"></div></div>"
            + "<div id=\"dial\"></div><div id=\"nudge\"></div><div id=\"big\">Big</div><div>tail</div>"
            + "</body></html>";
        Live.With(() =>
        {
            var live = new Live(html, "dial");                // HtmlSurface names it, for a transform in the first payload
            live.Bind(("grow", "height", "20px"), ("gauge", "--p", "0.5"), ("dial", "transform", "rotate(30deg)"), ("nudge", "top", "0px"), ("big", "font-size", "14px"));
            var warned = new List<string>();
            var (template, scene, map) = live.Compile(P(("grow", Map("height", "20px")), ("gauge", Map("--p", "0.5")), ("dial", Map("transform", "rotate(30deg)")),
                                                        ("nudge", Map("top", "0px")), ("big", Map("font-size", "14px"))), warned);
            check(warned.Count == 0, "data real page: a height in flow, a custom property, a transform, a relative top and a font-size compile - "
                                     + (warned.Count == 0 ? "(nothing said)" : string.Join(" | ", warned)));
            var (again, rest) = live.Emit();
            check(again == template && rest.All(p => scene.TryGetValue(p.Key, out var was) && was.Equals(p.Value)),
                  "data real page: measuring leaves the page exactly as it found it");

            var written = new Dictionary<string, SceneSlots.Value>(map.Apply(P(("grow", Map("height", "55px")), ("gauge", Map("--p", "0.8")), ("dial", Map("transform", "rotate(120deg)")),
                                                                                ("nudge", Map("top", "12px")), ("big", Map("font-size", "20px")))));
            live.Bind(("grow", "height", "55px"), ("gauge", "--p", "0.8"), ("dial", "transform", "rotate(120deg)"), ("nudge", "top", "12px"), ("big", "font-size", "20px"));
            var (truthTemplate, truth) = live.Emit();
            check(truthTemplate == template, "data real page: the new values change no structure");
            var (moved, wrong) = Compare(scene, truth, written);
            check(wrong.Count == 0 && moved >= 6,
                  $"data real page: every slot the new values move - {moved}, the sibling, the card, the fill, an unnamed half-bar, the needle, the dial, the nudged box and the bigger text among them - is written where the page draws it"
                  + (wrong.Count == 0 ? "" : ": " + string.Join("; ", wrong)));
            check(written.TryGetValue("dial_r", out var dial) && Math.Abs(dial.Number - 120) < 1e-3 && truth.TryGetValue("dial_r", out var dialDrawn) && Math.Abs(dialDrawn.Number - 120) < 1e-3,
                  "data real page: the dial turns to 120 degrees about its transform-origin, as drawn");
        });

        // A flex gap is margins the cascade puts on the children, and an id with a dash is spelled
        // with an underscore in the scene: a gap sent as data moves the second child, and a bool on
        // `over-limit` hides that element itself, not only closes the gap under it.
        const string small = "<html><head><meta name=\"viewport\" content=\"width=480\"><style>"
            + "body{padding:10px;font-size:14px;color:#fff}#row{display:flex;gap:4px}#t2{width:10px;height:10px;background:#fff}"
            + "#over-limit{background:#b5352c;height:20px}"
            + "</style></head><body>"
            + "<div id=\"row\"><div id=\"t2\"></div><div id=\"x2\">x</div></div><div id=\"over-limit\">ALARM</div><div id=\"after\">after</div>"
            + "</body></html>";
        Live.With(() =>
        {
            var live = new Live(small, "over-limit");
            live.Bind(("row", "gap", "4px"));
            var warned = new List<string>();
            var (_, scene, map) = live.Compile(P(("row", Map("gap", "4px")), ("over-limit", B(true))), warned);
            var written = new Dictionary<string, SceneSlots.Value>(map.Apply(P(("row", Map("gap", "20px")))));
            live.Bind(("row", "gap", "20px"));
            var (_, truth) = live.Emit();
            var (moved, wrong) = Compare(scene, truth, written);
            check(warned.Count == 0 && wrong.Count == 0 && written.TryGetValue("x2_x", out var x2) && Math.Abs(x2.Number - truth["x2_x"].Number) < 1 && truth["x2_x"].Number > scene["x2_x"].Number,
                  $"data real page: a flex gap sent as data moves the child after it, where the page draws it ({moved} moved)"
                  + (warned.Count + wrong.Count == 0 ? "" : " - " + string.Join(" | ", warned.Concat(wrong))));
            var hidden = map.Apply(P(("over-limit", B(false))));
            check(Num(hidden, "over_limit_o") == 0 && Num(hidden, "after_y") < scene["after_y"].Number,
                  $"data real page: a bool on an id with a dash hides the element itself and closes the gap (over_limit_o {Num(hidden, "over_limit_o")}, after_y {Num(hidden, "after_y")})");
        });

        // The shipped example: its first payload compiles with nothing refused, and a later one
        // swings the needle and fills the bar exactly where the page draws them.
        var root = Root();
        check(root != null, "data example 10: the mod's folder is found");
        if (root == null) return;
        var lua = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "examples", "10-device-readout.lua"));
        var page = System.Text.RegularExpressions.Regex.Match(lua, @"src = \[\[(.*?)\]\]", System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        Live.With(() =>
        {
            var live = new Live(page, "needle", "alarm");
            live.Bind(("bar", "width", "62%"), ("bar", "background-color", "#2E8B6E"), ("needle", "transform", "rotate(21deg)"));
            var warned = new List<string>();
            var (template, scene, map) = live.Compile(P(("bar", M(("width", "62%"), ("background-color", "#2E8B6E"))), ("needle", Map("transform", "rotate(21deg)")), ("alarm", B(false))), warned);
            check(warned.Count == 0, "data example 10: its first payload - a bar, a needle and an alarm - compiles with nothing refused - "
                                     + (warned.Count == 0 ? "(nothing said)" : string.Join(" | ", warned)));
            var written = new Dictionary<string, SceneSlots.Value>(map.Apply(P(("bar", M(("width", "90%"))), ("needle", Map("transform", "rotate(72deg)")))));
            live.Bind(("bar", "width", "90%"), ("needle", "transform", "rotate(72deg)"));
            var (truthTemplate, truth) = live.Emit();
            var (moved, wrong) = Compare(scene, truth, written);
            check(truthTemplate == template && wrong.Count == 0 && moved >= 2 && written.TryGetValue("needle_r", out var r) && Math.Abs(r.Number - 72) < 1e-3,
                  $"data example 10: the needle turns and the bar fills where the page draws them ({moved} slots moved)" + (wrong.Count == 0 ? "" : ": " + string.Join("; ", wrong)));
            check(map.TryEase("needle_r", out var swing) && Math.Abs(swing.Dur - 0.6f) < 1e-4, "data example 10: the needle swings on its CSS transition");
        });
    }

    private static SS.UiValue Map(string property, string value) => M((property, value));

    /// <summary>How many numeric slots moved from the compiled scene to the truth, and each the table did not write where drawn.</summary>
    private static (int Moved, List<string> Wrong) Compare(Dictionary<string, SceneSlots.Value> scene, Dictionary<string, SceneSlots.Value> truth, Dictionary<string, SceneSlots.Value> written)
    {
        var wrong = new List<string>();
        var moved = 0;
        foreach (var pair in truth)
        {
            if (!pair.Value.IsNumber || !scene.TryGetValue(pair.Key, out var was) || !was.IsNumber) continue;
            var changed = Math.Abs(pair.Value.Number - was.Number) > 0.01;
            if (changed) moved++;
            var got = written.TryGetValue(pair.Key, out var w) && w.IsNumber ? w.Number : (float?)null;
            // a layout rounds to the pixel; the table writes the exact line through it
            if (changed ? got == null || Math.Abs(got.Value - pair.Value.Number) > 1 : got != null && Math.Abs(got.Value - pair.Value.Number) > 1)
                wrong.Add($"{pair.Key} drawn {was.Number} -> {pair.Value.Number}, written {(got?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing")}");
        }
        return (moved, wrong);
    }

    private static string? Root()
    {
        for (var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "ScriptedScreensHtml");
            if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "DataSlots.cs"))) return candidate;
        }
        return null;
    }

    /// <summary>A page built, laid out and emitted as a surface does it, outside the game.</summary>
    private sealed class Live
    {
        private readonly HtmlRenderer.Result _built;
        private readonly Panel _panel;
        private readonly Vector2 _size;

        /// <param name="groups">The elements whose groups the surface names for the first payload (a bool, a transform).</param>
        public Live(string html, params string[] groups)
        {
            _built = HtmlRenderer.Build(html, FontLibrary.Default());
            HtmlRenderer.NameDrivenGroups(_built);
            foreach (var id in _built.ById.Keys) _built.Driven.Add(id);
            foreach (var id in groups) _built.NamedGroups.Add(id);
            _panel = new Panel(_built.Root);
            foreach (var grid in _built.Grids)
                if (_built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, _built);
            PostLayout.Attach(_built);
            _size = new Vector2(_built.ViewportWidth, _built.ViewportWidth);
            _panel.Layout(_size.x, _size.y);
        }

        /// <summary>Declarations onto the page as a surface binds a payload (DataSlots.Bind), then laid out.</summary>
        public void Bind(params (string Id, string Property, string Value)[] decls)
        {
            foreach (var (id, property, value) in decls)
                DataSlots.Bind(_built, _built.ById[id], new[] { new SS.UiProp { Key = property, Value = S(value) } }, null);
            _panel.Layout(_size.x, _size.y);
        }

        public (string Template, Dictionary<string, SceneSlots.Value> Values) Emit()
        {
            var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            var boxes = new Dictionary<VisualElement, OffThread.Box>();
            OffThread.Capture(_built.Root, _built, boxes, new List<VisualElement>());
            OffThread.Boxes = boxes;
            OffThread.Active = true;
            try
            {
                var output = VectorEmitter.Isolated(() => VectorEmitter.Emit(_built, _built.Root, _size.x, _size.y));
                return (SceneSlots.Split(output.Chars, output.Length, values), values);
            }
            finally { OffThread.Active = false; OffThread.Boxes = null; }
        }

        /// <summary>Emitted and compiled against that emit, with the real measurement, as HtmlSurface.CompileData does.</summary>
        public (string Template, Dictionary<string, SceneSlots.Value> Scene, DataSlots Map) Compile(List<KeyValuePair<string, SS.UiValue>> first, List<string> warned)
        {
            var (template, scene) = Emit();
            var built = _built;
            var map = DataSlots.Compile(first, scene, template, built.ById.Keys,
                (id, available) => PageCompiler.BoxFor(built, id, available),
                id => built.ById.TryGetValue(id, out var ve) ? built.CssOf(ve) : null,
                Array.Empty<string>(),
                id => PageCompiler.ToggleOf(built, id),
                warned.Add,
                (id, property, value) => DataSlots.Sample(built, id, property, value, "L"));
            return (template, scene, map);
        }

        /// <summary>The statics a build and an emit read, set as the game has them and put back after.</summary>
        public static void With(Action body)
        {
            var oracle = CssParser.SupportsOracle;
            var (vw, vh) = (CssParser.ViewportWidth, CssParser.ViewportHeight);
            var aspect = HtmlRenderer.SurfaceAspect;
            var mainThread = OffThread.MainThreadId;
            try
            {
                ResolvedStyle.DefaultFace = FontLibrary.Default();
                HtmlRenderer.SurfaceAspect = 1f;
                OffThread.MainThreadId = Environment.CurrentManagedThreadId;
                OffThread.Job = OffThread.Globals.Take();
                body();
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
    }
}
}

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
/// The data fast path's table, from the payload side: what a later payload writes, and how one it
/// cannot place is refused. The mapping itself is CompiledPageTests' (DataSlotsFixture); this is
/// the half that runs on every tick.
/// </summary>
internal static class DataSlotsTests
{
    private static SS.UiValue S(string s) => new() { Type = SS.UiValueType.String, String = s };
    private static SS.UiValue B(bool b) => new() { Type = SS.UiValueType.Bool, Bool = b };
    private static SS.UiValue M(params (string Key, string Value)[] d) => new()
    {
        Type = SS.UiValueType.Map,
        Map = d.Select(x => new SS.UiProp { Key = x.Key, Value = S(x.Value) }).ToArray(),
    };
    private static List<KeyValuePair<string, SS.UiValue>> P(params (string Key, SS.UiValue Value)[] e)
        => e.Select(x => new KeyValuePair<string, SS.UiValue>(x.Key, x.Value)).ToList();

    internal static void Run(Action<bool, string> check)
    {
        // the 02-live-data shapes: a bar (a block in a 448-wide block track), a text span, and an
        // out-of-flow box `e` holding `k`, so one `left` declaration reaches two slots (e_x, k_x)
        var available = new HashSet<string>(StringComparer.Ordinal) { "bar_w", "bar_f", "pressure", "e_x", "e_y", "k_x", "k_y" };
        var bar = new DomSlots.Box(false, 16, 100, true, null, 448, 14, 0, 14, localWidth: true, contentW: 448, contentH: 14);
        var span = new DomSlots.Box(false, 0, 0, false);
        var e = new DomSlots.Box(true, 100, 0, true, new[] { ("k", 5.0, 5.0) }, 300, 200, 50, 20);
        DomSlots.Box? BoxOf(string id) => id switch { "bar" => bar, "pressure" => span, "e" => e, "alarm" => span, _ => null };
        DataSlots Build(List<KeyValuePair<string, SS.UiValue>> sample, Func<string, DomSlots.Toggle>? toggleOf = null)
            => DataSlots.Build(sample, BoxOf, _ => null, _ => false, available, toggleOf);

        var map = Build(P(("pressure", S("--")), ("bar", M(("width", "62%"), ("background-color", "#2E8B6E"))), ("e", M(("left", "10px")))));
        check(map.Problem == null, "data apply: the sample maps - " + (map.Problem ?? "ok"));

        // The first payload's declarations, in the other order, and one declaration on two slots.
        // Build appends a target per SLOT, so pairing declaration i with target i - which is what
        // this did - broke on the first declaration with two, and on any payload whose table Lua
        // happened to iterate differently.
        var v = map.Apply(P(("bar", M(("background-color", "#B5352C"), ("width", "50%"))), ("e", M(("left", "20px"))), ("pressure", S("58.9"))));
        check(v != null && v.TryGetValue("bar_w", out var w) && (double)w == 224 && v.TryGetValue("bar_f", out var f) && (string)f == "#B5352C",
              "data apply: declarations are matched to their targets by name, not position");
        check(v != null && v.TryGetValue("e_x", out var ex) && (double)ex == 120 && v.TryGetValue("k_x", out var kx) && (double)kx == 125,
              "data apply: one declaration reaching two slots writes both (e_x, k_x)");

        // A later payload the first did not shape is dropped whole, and the reason names what:
        // the key, the declaration and the value - not "a key the page has no slot for".
        check(map.Apply(P(("bar", M(("width", "1em"))))) == null && map.Refused == "\"bar\".width = \"1em\" is not a px or % length",
              "data apply: an em is refused naming the value - " + (map.Refused ?? "(no reason)"));
        var map2 = Build(P(("bar", M(("width", "62%")))));
        check(map2.Apply(P(("bar", M(("width", "10%"), ("height", "3px"))))) == null
              && map2.Refused == "\"bar\".height is not a declaration the first payload carried",
              "data apply: a new declaration is refused naming it - " + (map2.Refused ?? "(no reason)"));
        var map3 = Build(P(("pressure", S("--"))));
        check(map3.Apply(P(("pressure", B(true)))) == null && map3.Refused!.Contains("different kind"),
              "data apply: a string key sent as a bool is refused - " + (map3.Refused ?? "(no reason)"));

        // The proof: what this would write against what the emitter drew, agreeing and not.
        var emitted = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal)
        {
            ["bar_w"] = new(278f), ["bar_f"] = new("#2E8B6E"), ["pressure"] = new("--"), ["e_x"] = new(110f), ["k_x"] = new(115f),
        };
        var warned = new List<string>();
        map.Prove(P(("pressure", S("--")), ("bar", M(("width", "62%"), ("background-color", "#2E8B6E"))), ("e", M(("left", "10px")))), emitted, warned.Add);
        check(map.Proven && warned.Count == 0, "data prove: the table agrees with the emitter and the fast path is on" + (map.Problem != null ? " - " + map.Problem : string.Empty));
        var wrong = Build(P(("bar", M(("width", "62%")))));
        emitted["bar_w"] = new(200f);
        wrong.Prove(P(("bar", M(("width", "62%")))), emitted, warned.Add);
        check(!wrong.Proven && wrong.Problem is { } why && why.Contains("bar_w") && warned.Count == 1,
              "data prove: a disagreement names the slot and stays off - " + (wrong.Problem ?? "(proven)"));

        // A bool is a two-value state: Apply writes the whole state the value picks, the proof
        // reads it as shown (that is what the proof's scene was emitted with), and Overlay puts the
        // real state back over a scene the full path emitted.
        DomSlots.Toggle ToggleOf(string id)
        {
            var t = new DomSlots.Toggle();
            t.Shown.Add(("alarm_o", 1)); t.Shown.Add(("note_y", 172));
            t.Hidden.Add(("alarm_o", 0)); t.Hidden.Add(("note_y", 121));
            return t;
        }
        var toggled = Build(P(("alarm", B(false))), ToggleOf);
        check(toggled.Problem == null && toggled.Toggles("alarm"), "data bool: a bool key is a toggle" + (toggled.Problem != null ? " - " + toggled.Problem : string.Empty));
        var hidden = toggled.Apply(P(("alarm", B(false))));
        check(hidden != null && (double)hidden["alarm_o"] == 0 && (double)hidden["note_y"] == 121, "data bool: false writes the hidden state, the note's y included");
        var scene = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal) { ["alarm_o"] = new(1f), ["note_y"] = new(172f) };
        toggled.Prove(P(("alarm", B(false))), scene, warned.Add);
        check(toggled.Proven, "data bool: the proof reads the bool as shown, which is what the scene was emitted with" + (toggled.Problem != null ? " - " + toggled.Problem : string.Empty));
        toggled.Overlay(scene);
        check(scene["alarm_o"].Number == 0 && scene["note_y"].Number == 121, "data bool: Overlay puts the hidden state back over the shown scene");
        check(Build(P(("alarm", B(true)))).Problem is { } noWay && noWay.Contains("no way to lay the page out"),
              "data bool: with no way to lay the page out hidden, a bool is refused saying so");
    }
}
}

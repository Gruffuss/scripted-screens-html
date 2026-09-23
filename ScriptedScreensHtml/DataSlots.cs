using System;
using System.Collections.Generic;
using System.Globalization;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensHtml;

/// <summary>
/// A page with no script, driven by Lua data: which scene slot each data key writes.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> A page without a <c>&lt;script&gt;</c> never compiles - there is nothing
/// to translate - so it stayed on the interpreted path for ever, and every data tick set
/// <c>_dirty</c> and paid a full layout, translate, emit and split. Twice a second, for ever. That
/// is most of the real consoles: "no script" reads like "nothing happening" and means the opposite.
///
/// It is also the <i>easiest</i> case in the compiler, because the hard half is already absent. No
/// JavaScript, no call graph, no state enumeration: a data key names an element, the value says
/// what to write, and <see cref="DomSlots"/> already knows which slot that lands on.
///
/// <b>Compiled once, against the emit that drew the first payload.</b> Everything the mapping needs
/// from the page - every element's box, the CSS of the ones that transition, the scene's slot
/// names - is copied out at that moment, so the page can be let go and a key or declaration first
/// seen later still maps on arrival, from the copy. The first payload's keys are also PROVEN: what
/// this would write is compared with what the emitter drew for the same values.
///
/// <b>The unit of refusal is the key, never the page.</b> A key naming no element, a declaration
/// with no slot, a value in a unit that cannot be written, a proof that disagrees - each drops that
/// key (or that declaration), says so once, and every other key keeps applying. Within one payload a
/// key is written whole or not at all, so no frame shows half of a value.
/// </remarks>
internal sealed class DataSlots
{
    /// <summary>One slot a declaration writes, and how the value becomes the slot's.</summary>
    private readonly struct Target
    {
        public readonly string Slot;
        public readonly double Bias;
        /// <summary>-1 for a far-edge position (`right` grows as `x` shrinks), 1 otherwise.</summary>
        public readonly double Scale;
        public Target(string slot, double bias, double scale) { Slot = slot; Bias = bias; Scale = scale; }
    }

    /// <summary>What a declaration's value is, which decides how a payload's string becomes a number or a colour.</summary>
    /// <remarks>
    /// <b>Transform</b> is a function list written onto the element's transform group, five numbers
    /// at once. <b>Display</b> is the bool's two states under another spelling. <b>Linear</b> is a
    /// declaration no one slot takes - a height in flow, a margin, a custom property - whose effect
    /// on every slot was measured from the page when it compiled (<see cref="Fit"/>).
    /// </remarks>
    private enum Kind { Length, Colour, Opacity, Visibility, Transform, Display, Linear }

    private sealed class Decl
    {
        public Target[] Targets = Array.Empty<Target>();
        public Kind Kind;
        /// <summary>What a `%` is of (the containing block's content box), shared by every target.</summary>
        public double PercentOf = double.NaN;
        /// <summary>Why this declaration has no slot, said once. The key's other declarations still apply.</summary>
        public string? No;
        /// <summary>A value it could not place has been reported; later ones are dropped quietly.</summary>
        public bool Warned;
        /// <summary>A colour as last converted, so an unchanged one is not parsed and formatted again every tick.</summary>
        public string? LastIn, LastOut;
        /// <summary>A transform as last parsed: whether it could be, and its five numbers (t_0 t_1 r s_0 s_1).</summary>
        public bool LastOk;
        public double[]? Parts;
        /// <summary>The element's own box, which a `translate()` percentage is of.</summary>
        public double W = double.NaN, H = double.NaN;
        /// <summary>A Linear declaration's unit as the first payload sent it; a later value must be in the same one.</summary>
        public string Unit = string.Empty;
        /// <summary>Whether a number with no unit is in <see cref="Unit"/>: px for a length, unitless for a custom property.</summary>
        public bool Bare;
        /// <summary>What each target currently moves its slot by, from the compiled scene (<see cref="Add"/>).</summary>
        public double[] Given = Array.Empty<double>();
    }

    /// <summary>A bool's two states: its element's opacity and every slot that hiding it moves.</summary>
    private sealed class Toggle
    {
        public string[] Slots = Array.Empty<string>();
        public float[] Shown = Array.Empty<float>();
        public float[] Hidden = Array.Empty<float>();
        /// <summary>The element's own opacity, which the state sets outright; every other slot it moves by a distance.</summary>
        public int Own = -1;
        public double[] Given = Array.Empty<double>();
    }

    private sealed class Entry
    {
        /// <summary>The element's box as compiled; null for a key naming no element.</summary>
        public DomSlots.Box? Box;
        public bool Shape;
        /// <summary>The whole key, refused: its proof disagreed. Said once.</summary>
        public string? No;
        public string? Text, TextNo;
        /// <summary>The text's `text-transform`, null for none.</summary>
        public string? Case;
        public Dictionary<string, Decl>? Decls;
        public Toggle? Toggle;
        public string? BoolNo;
        /// <summary>A value of a kind this key cannot take has been reported.</summary>
        public bool Warned;
        // The text as last formatted and shaped, so an unchanged value builds no string per tick.
        public bool HasNumber;
        public float LastNumber;
        public string? LastNumberText, LastIn, LastOut;
    }

    private readonly Dictionary<string, Entry> _keys = new(StringComparer.Ordinal);

    // ---- what the page was, copied out while it existed ----
    private readonly Dictionary<string, DomSlots.Box> _boxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _transitions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shapes = new(StringComparer.Ordinal);
    /// <summary>Elements whose digits the emitter monospaces, so one whose width was never drawn can say so.</summary>
    private readonly HashSet<string> _tabular = new(StringComparer.Ordinal);
    /// <summary>Per element, its `text-transform` (inherited ones included): the emitter cases the text it draws.</summary>
    private readonly Dictionary<string, string> _case = new(StringComparer.Ordinal);
    /// <summary>Per element with one px corner radius: the radius as declared, its `rx` slot, and the box's size slots.</summary>
    private readonly Dictionary<string, (double R, string Slot, string W, string H)> _radius = new(StringComparer.Ordinal);
    private HashSet<string> _available = new(StringComparer.Ordinal);
    /// <summary>
    /// Per `<id>_x` / `<id>_y`, the element's transform group anchor (`<id>_a_0` / `_a_1`) and how far
    /// it sits from the box. The anchor is the transform-origin in absolute scene coordinates, so a
    /// `left` that moves the element has to move it too, or a turned element orbits where it was.
    /// </summary>
    private readonly Dictionary<string, (string Slot, double Offset)> _anchors = new(StringComparer.Ordinal);
    /// <summary>
    /// Names the scene reads as <c>$name</c> that are not its own slots: an SVG expression's inputs.
    /// Only these are worth forwarding as data - and a key that is ALSO a slot name must not be,
    /// since the raw value would overwrite what the slot write put there (a tabular label's
    /// monospacing, on every tick whose value had not changed).
    /// </summary>
    private readonly HashSet<string> _reads = new(StringComparer.Ordinal);
    private readonly Action<string> _warn;
    // Only while Compile runs: the page, and the scene it is checked against. Nulled after, so a
    // released page is not kept alive through them.
    private Func<string, DomSlots.Toggle>? _toggleOf;
    private Func<string, string, string, (string Template, Dictionary<string, SceneSlots.Value> Values)?>? _sampleOf;
    private IReadOnlyDictionary<string, SceneSlots.Value>? _scene;
    private string _template = string.Empty;
    /// <summary>
    /// The prefix the compiled scene's positional slots carry (`L12_y`), and the one the structure
    /// goes out with: a new structure takes the other set (HtmlSurface), so a line with no id that a
    /// Linear declaration moves is written under the name it will have when it is drawn.
    /// </summary>
    private string _prefix = "L", _sendPrefix = "L";

    /// <summary>What the last Apply wrote. Reused: a tick allocates nothing here.</summary>
    private readonly Dictionary<string, SceneSlots.Value> _out = new(StringComparer.Ordinal);

    /// <summary>
    /// Every number the compiled scene drew, and per slot how far the declarations and states since
    /// have moved it in total. A slot two keys move - a sibling below a growing box that is also
    /// nudged by its own `top`, a child inside a moved parent - is the sum of both moves, as a
    /// layout adds them; written outright, whichever came last would undo the other.
    /// </summary>
    private readonly Dictionary<string, double> _baseline = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _moved = new(StringComparer.Ordinal);

    /// <summary>
    /// The value every slot this has written holds now, so a scene the full path emits after the
    /// compile - the proof tick's, a capture's - shows the data rather than whatever the page's DOM
    /// last held. Written on the game thread (Apply), read on the page's (Overlay).
    /// </summary>
    private readonly Dictionary<string, SceneSlots.Value> _last = new(StringComparer.Ordinal);

    /// <summary>
    /// Slots whose property has a CSS transition, with its duration and curve. They are sent without
    /// <c>snap</c> and with the renderer's <c>ease</c> timing (vector 0.11.33), so the renderer runs
    /// the glide the CSS declares; every other slot snaps, as a browser does.
    /// </summary>
    private readonly Dictionary<string, (float Dur, string Curve, float Delay)> _eased = new(StringComparer.Ordinal);

    internal bool TryEase(string slot, out (float Dur, string Curve, float Delay) timing) => _eased.TryGetValue(slot, out timing);

    /// <summary>
    /// Per text slot, the <c>&lt;mspace=Xem&gt;</c> its digit runs are wrapped in, or absent for plain
    /// text. `font-variant-numeric: tabular-nums` makes the emitter wrap every digit run, with X from
    /// the element's measured font - which is the layout this class exists not to do - so it is read
    /// off the emitted scene, for every text slot that drew a digit, when the page is compiled.
    /// </summary>
    private readonly Dictionary<string, string> _mspace = new(StringComparer.Ordinal);
    private readonly System.Text.StringBuilder _shaped = new(64);

    /// <summary>Whether the scene reads any data name that is not one of its own slots (see <see cref="_reads"/>).</summary>
    internal bool ReadsAny => _reads.Count > 0;

    internal bool Reads(string name) => _reads.Contains(name);

    /// <summary>Keys that place something, for the diagnostics line.</summary>
    internal int Count
    {
        get
        {
            var n = 0;
            foreach (var e in _keys.Values)
                if (e.No == null && (e.Text != null || e.Toggle != null || e.Decls != null)) n++;
            return n;
        }
    }

    private DataSlots(Action<string> warn) => _warn = warn;

    /// <summary>
    /// Compiles a page's data against the scene that drew its first payload: maps every key, proves
    /// each against what the emitter drew, and copies out everything a later key needs.
    /// </summary>
    /// <param name="sample">Every key the page has been sent so far, as the scene shows them.</param>
    /// <param name="scene">That scene's slot values (SceneSlots.Split).</param>
    /// <param name="template">That scene's template, for the names it reads as data.</param>
    /// <param name="ids">Every element id in the page, whose box is copied for a key that arrives later.</param>
    /// <param name="boxOf">An element's box (PageCompiler.BoxFor), given the scene's slot names.</param>
    /// <param name="toggleOf">
    /// What a bool key's element looks like shown and hidden (PageCompiler.ToggleOf). Only while
    /// compiling: its states are two layouts, and the page is let go afterwards.
    /// </param>
    /// <param name="sampleOf">
    /// The page laid out and emitted with one declaration of one element set to a value (<see cref="Sample"/>),
    /// split with <paramref name="prefix"/>: how a Linear declaration's slots are measured. Only while compiling.
    /// </param>
    /// <param name="prefix">The positional prefix <paramref name="scene"/> was split with.</param>
    /// <param name="sendPrefix">The one the structure is sent with, when a new structure takes the other set.</param>
    internal static DataSlots Compile(IReadOnlyList<KeyValuePair<string, SS.UiValue>> sample,
                                      IReadOnlyDictionary<string, SceneSlots.Value> scene,
                                      string template,
                                      IEnumerable<string> ids,
                                      Func<string, HashSet<string>, DomSlots.Box?> boxOf,
                                      Func<string, IReadOnlyDictionary<string, string>?> cssOf,
                                      IEnumerable<string> shapes,
                                      Func<string, DomSlots.Toggle>? toggleOf,
                                      Action<string> warn,
                                      Func<string, string, string, (string Template, Dictionary<string, SceneSlots.Value> Values)?>? sampleOf = null,
                                      string prefix = "L", string? sendPrefix = null)
    {
        var map = new DataSlots(warn);
        map._prefix = prefix;
        map._sendPrefix = sendPrefix ?? prefix;
        map._available = new HashSet<string>(scene.Keys, StringComparer.Ordinal);
        foreach (var id in shapes) map._shapes.Add(id);
        foreach (var id in ids)
        {
            // Synthetic ids (`__3`) are the emitter's, not the author's: nothing sends data to them.
            if (string.IsNullOrEmpty(id) || id.StartsWith("__", StringComparison.Ordinal) || map._shapes.Contains(id)) continue;
            if (boxOf(id, map._available) is { } box) map._boxes[id] = box;
            if (cssOf(id) is not { } css) continue;
            if (css.ContainsKey("transition") || css.ContainsKey("transition-property") || css.ContainsKey("transition-duration"))
                map._transitions[id] = css;
            // the emitter's own test for monospacing a label's digits (VectorEmitter, tabular-nums)
            if (css.TryGetValue("font-variant-numeric", out var numeric) && numeric.Contains("tabular")) map._tabular.Add(id);
            if (css.TryGetValue("text-transform", out var textCase)) map._case[id] = textCase.Trim().ToLowerInvariant();
            // One px radius on all four corners is the `rx` slot the emitter clamps; a % or four
            // radii are not a single number, and the emitter writes those as a literal array.
            var slot = DomSlots.Slot(id);
            if (css.TryGetValue("border-radius", out var radius) && DomSlots.Length(radius) is double px && px > 0 && map._available.Contains(slot + "_rx"))
                map._radius[id] = (px, slot + "_rx", slot + "_w", slot + "_h");
        }
        map.ReadNames(template, scene);
        foreach (var pair in scene)
            if (pair.Value.IsNumber) map._baseline[pair.Key] = pair.Value.Number;
        foreach (var pair in scene)
        {
            if (!pair.Value.IsNumber) continue;
            var axis = pair.Key.EndsWith("_a_0", StringComparison.Ordinal) ? "_x" : pair.Key.EndsWith("_a_1", StringComparison.Ordinal) ? "_y" : null;
            if (axis == null) continue;
            var owner = pair.Key.Substring(0, pair.Key.Length - 4) + axis;
            if (scene.TryGetValue(owner, out var at) && at.IsNumber) map._anchors[owner] = (pair.Key, pair.Value.Number - at.Number);
        }
        foreach (var pair in scene)
        {
            if (pair.Value.IsNumber || pair.Value.Text == null) continue;
            var at = pair.Value.Text.IndexOf("<mspace=", StringComparison.Ordinal);
            var close = at < 0 ? -1 : pair.Value.Text.IndexOf('>', at);
            if (close > at) map._mspace[pair.Key] = pair.Value.Text.Substring(at, close - at + 1);
        }

        // Each key is written as the proof reads it - a bool as shown, which is how the scene was
        // emitted (the element stays in the layout whatever the value) - and checked against what
        // was drawn. A disagreement refuses that key alone.
        map._toggleOf = toggleOf;
        map._sampleOf = sampleOf;
        map._scene = scene;
        map._template = template;
        try
        {
            for (var i = 0; i < sample.Count; i++)
            {
                var key = sample[i].Key;
                if (string.IsNullOrEmpty(key)) continue;
                map._out.Clear();
                map.Write(key, sample[i].Value, proving: true);
                if (map.Disagrees(scene) is { } why) map.Refuse(map.EntryOf(key), $"\"{key}\" - {why}");
            }
        }
        finally { map._toggleOf = null; map._sampleOf = null; map._scene = null; map._template = string.Empty; }

        // The proof wrote every key as drawn, and a refused one may have moved a slot another shares:
        // the sums start again from the scene, and the payload below puts back what each key moves.
        map._moved.Clear();
        foreach (var entry in map._keys.Values)
        {
            if (entry.Toggle != null) Array.Clear(entry.Toggle.Given, 0, entry.Toggle.Given.Length);
            if (entry.Decls != null) foreach (var decl in entry.Decls.Values) Array.Clear(decl.Given, 0, decl.Given.Length);
        }

        // The payload's actual values, bools included, are what a scene emitted from here on shows.
        map.Apply(sample);
        return map;
    }

    /// <summary>
    /// What this payload writes: every key it can place, each whole. Keys, declarations and kinds of
    /// value seen for the first time are mapped now, from what was copied at compile.
    /// </summary>
    /// <remarks>Runs on every data tick: the result is a reused buffer, valid until the next call.</remarks>
    internal Dictionary<string, SceneSlots.Value> Apply(IReadOnlyList<KeyValuePair<string, SS.UiValue>> entries)
    {
        _out.Clear();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!string.IsNullOrEmpty(entry.Key)) Write(entry.Key, entry.Value, proving: false);
        }
        lock (_last)
            foreach (var pair in _out) _last[pair.Key] = pair.Value;
        return _out;
    }

    /// <summary>Puts every value this has written over a scene the full path just emitted.</summary>
    internal void Overlay(Dictionary<string, SceneSlots.Value> scene)
    {
        lock (_last)
            foreach (var pair in _last)
                if (scene.ContainsKey(pair.Key)) scene[pair.Key] = pair.Value;
    }

    /// <summary>One key's values into <see cref="_out"/>, whole or not at all.</summary>
    private void Write(string key, in SS.UiValue value, bool proving)
    {
        var e = EntryOf(key);
        if (e.No != null) return;
        if (e.Box == null)
        {
            // An SVG shape's array is bound in the scene as $key[i] and is forwarded, which already
            // makes it free. Its points string or attribute map are the shape's geometry - literal
            // arrays in the structure, not values.
            if (e.Shape && value.Type != SS.UiValueType.Array && !e.Warned)
            {
                e.Warned = true;
                Say($"\"{key}\" is an svg shape, and its {(value.Type == SS.UiValueType.Map ? "attributes are" : "points are")} the scene's structure, not a value - send an array (bound as ${key}[i]) or write the attribute as an expression over ${key}; dropped");
            }
            return;
        }

        switch (value.Type)
        {
            case SS.UiValueType.String:
            case SS.UiValueType.Number:
            {
                var slot = TextSlot(key, e);
                if (slot != null) _out[slot] = new SceneSlots.Value(TextOf(e, slot, value));
                break;
            }

            // Shows or hides the element: a two-value STATE, the same thing a class toggle is.
            case SS.UiValueType.Bool:
            {
                var t = ToggleFor(key, e);
                if (t == null) break;
                State(t, hidden: !proving && !value.Bool);
                break;
            }

            case SS.UiValueType.Map when value.Map != null:
            {
                // Two passes: nothing is written until every declaration this key can place has a
                // value it can place. A declaration with no slot at all is skipped every tick alike,
                // so it never makes a frame differ from the one before.
                var props = value.Map;
                for (var i = 0; i < props.Length; i++)
                {
                    var d = DeclOf(key, e, props[i].Key ?? string.Empty, props[i].Value);
                    if (d.No != null || Resolve(d, props[i].Value, out _, out _)) continue;
                    if (!d.Warned)
                    {
                        d.Warned = true;
                        Say($"\"{key}\".{props[i].Key} = {Show(props[i].Value)} is not a value this can write ({Expected(d)}) - the key is dropped for that payload and applies again with the next that it can place");
                    }
                    return;
                }
                double? r = null;
                var sized = false;
                for (var i = 0; i < props.Length; i++)
                {
                    var d = DeclOf(key, e, props[i].Key ?? string.Empty, props[i].Value);
                    if (d.No != null) continue;
                    Resolve(d, props[i].Value, out var number, out var text);
                    switch (d.Kind)
                    {
                        case Kind.Transform:
                            for (var k = 0; k < d.Targets.Length; k++) _out[d.Targets[k].Slot] = new SceneSlots.Value((float)d.Parts![k]);
                            continue;
                        case Kind.Display:
                        {
                            // the bool's states: shown while proving, as the scene was emitted
                            State(e.Toggle!, hidden: !proving && number == 0);
                            continue;
                        }
                    }
                    for (var k = 0; k < d.Targets.Length; k++)
                    {
                        var t = d.Targets[k];
                        if (text != null) _out[t.Slot] = new SceneSlots.Value(text);
                        // an opacity is set, not moved: a bool's state writes the same slot outright
                        else if (d.Kind is Kind.Opacity or Kind.Visibility) _out[t.Slot] = new SceneSlots.Value((float)(number * t.Scale + t.Bias));
                        else Add(t.Slot, ref d.Given[k], number * t.Scale + t.Bias);
                    }
                    if (props[i].Key is "border-radius" or "borderRadius") r = number;
                    else if (props[i].Key is "width" or "height" || d.Kind == Kind.Linear) sized = true;
                }
                // CSS scales a radius down when two of them overflow a side, and the emitter does the
                // same (VectorEmitter.Radii) - without it a bar written below twice its radius is a
                // self-intersecting shape. The size moved, so the clamp has to move with it: read off
                // what was just written to the box's own slots, which a Linear declaration reaches too.
                if ((sized || r != null) && _radius.TryGetValue(key, out var round))
                {
                    var box = e.Box.Value;
                    var w = Current(round.W, box.W);
                    var h = Current(round.H, box.H);
                    var clamped = Math.Min(r ?? round.R, Math.Min(w / 2, h / 2));
                    if (!double.IsNaN(clamped)) _out[round.Slot] = new SceneSlots.Value((float)Math.Max(0, clamped));
                }
                break;
            }

            default:
                // An array on an element is forwarded (the scene may read it as $key[i]); it has no slot of its own.
                if (!e.Warned && !(value.Type == SS.UiValueType.Array && Reads(key)))
                {
                    e.Warned = true;
                    Say($"\"{key}\" carries a {value.Type}, which no slot of an element takes - dropped");
                }
                break;
        }
    }

    private Entry EntryOf(string key)
    {
        if (_keys.TryGetValue(key, out var e)) return e;
        e = new Entry();
        _keys[key] = e;
        if (_shapes.Contains(key)) e.Shape = true;
        else if (_boxes.TryGetValue(key, out var box)) e.Box = box;
        // A name the scene reads as $key (an SVG expression's input) is forwarded, which is all it
        // needs; anything else is a key naming nothing, and a browser would ignore it too.
        else if (!ReadsKey(key)) Say($"\"{key}\" names no element in the page, and the scene reads no ${key} - dropped; the rest applies");
        return e;
    }

    private string? TextSlot(string key, Entry e)
    {
        if (e.Text != null || e.TextNo != null) return e.Text;
        var mapped = DomSlots.Map(key, "textContent", e.Box!.Value, _available);
        if (mapped.Mapped) { e.Text = mapped.Slots[0]; _case.TryGetValue(key, out e.Case); }
        else { e.TextNo = mapped.Problem; Say($"\"{key}\" as text - {mapped.Problem}; dropped"); }
        // The cell width is the face's widest digit, measured by the emitter; it is only known here
        // from a label that drew a digit when the page compiled. Said rather than drawn otherwise.
        if (e.Text != null && _tabular.Contains(key) && !_mspace.ContainsKey(e.Text))
            Say($"\"{key}\" is tabular-nums, and it drew no digit when the page compiled, so the cell width is unknown - its digits are drawn proportional; send a number in the first payload");
        return e.Text;
    }

    /// <param name="first">The value this declaration arrived with: a Linear one is measured around it.</param>
    private Decl DeclOf(string key, Entry e, string name, in SS.UiValue first)
    {
        e.Decls ??= new Dictionary<string, Decl>(StringComparer.Ordinal);
        if (e.Decls.TryGetValue(name, out var d)) return d;
        d = new Decl { Kind = KindOf(name) };
        e.Decls[name] = d;
        if (d.Kind == Kind.Transform)
        {
            // The element's transform group, named when the first payload carried a transform
            // (HtmlSurface): `G a=[origin] t=[x,y] r=deg s=[x,y] id=<key>`. Turning about the anchor
            // is turning about the CSS transform-origin, which the emitter put there.
            var id = DomSlots.Slot(key);
            var slots = new[] { id + "_t_0", id + "_t_1", id + "_r", id + "_s_0", id + "_s_1" };
            if (Array.TrueForAll(slots, _available.Contains))
            {
                d.Targets = new Target[slots.Length];
                for (var i = 0; i < slots.Length; i++) d.Targets[i] = new Target(slots[i], 0, 1);
                d.Parts = new double[slots.Length];
                d.W = e.Box!.Value.W;
                d.H = e.Box!.Value.H;
                if (_transitions.TryGetValue(key, out var css) && CssTransition.For(css, name) is { } timing)
                    foreach (var slot in slots) _eased[slot] = timing;
            }
            else d.No = $"\"{key}\"'s transform group was not named when the page was compiled - send `transform` in the first payload";
        }
        else if (d.Kind == Kind.Display)
        {
            // Said by ToggleFor, once, in its own words.
            if (ToggleFor(key, e) == null) { d.No = e.BoolNo; return d; }
        }
        else if (d.Kind == Kind.Linear)
        {
            // A custom property has no slot of its own by definition: what it moves is whatever reads it.
            Fit(key, d, name, first, null);
        }
        else if (name is "font-size" or "fontSize" && First(first, name, out _, out _))
        {
            // Its `size` slot alone draws bigger text in the old box; a browser grows the box with
            // the text and moves what follows. Measured, as a height in flow is.
            d.Kind = Kind.Linear;
            Fit(key, d, name, first, $"\"{key}\"'s text box grows with its font-size");
        }
        else
        {
            var mapped = DomSlots.Map(key, "style." + name, e.Box!.Value, _available);
            if (!mapped.Mapped)
            {
                // No slot of its own - a height in flow, a margin, a box with text over it: measured
                // from the page instead, which is exactly the layout the refusal was deferring to.
                if (d.Kind == Kind.Length && First(first, name, out _, out _)) { d.Kind = Kind.Linear; Fit(key, d, name, first, mapped.Problem); }
                else d.No = mapped.Problem;
            }
            // The element's opacity group carries an id only when something named it before the
            // scene was emitted (HtmlSurface does, for a bool or an opacity in the first payload):
            // naming every group would make the renderer retain every element's props.
            else if (mapped.NeedsGroup) d.No = $"\"{key}\"'s opacity group was not named when the page was compiled - send `{name}` in the first payload";
            else
            {
                d.PercentOf = mapped.PercentOf;
                var targets = new List<Target>(mapped.Slots.Length);
                for (var i = 0; i < mapped.Slots.Length; i++)
                {
                    targets.Add(new Target(mapped.Slots[i], mapped.Bias[i], mapped.Scale[i]));
                    if (_anchors.TryGetValue(mapped.Slots[i], out var anchor))
                        targets.Add(new Target(anchor.Slot, mapped.Bias[i] + anchor.Offset, mapped.Scale[i]));
                }
                d.Targets = targets.ToArray();
                d.Given = new double[d.Targets.Length];
                if (_transitions.TryGetValue(key, out var css) && CssTransition.For(css, name) is { } timing)
                    foreach (var t in d.Targets) _eased[t.Slot] = timing;
            }
        }
        if (d.No != null) Say($"\"{key}\".{name} - {d.No}; that declaration is dropped, the key's others apply");
        return d;
    }

    /// <summary>
    /// A declaration whose effect only the layout knows - a height in flow moves every sibling after
    /// it and an auto-height parent, a custom property moves whatever reads it - measured once, while
    /// the page exists: laid out at three values around the first one, and every slot that moves is
    /// fitted with a straight line through the outer two and checked against the middle. The value
    /// then writes each at <c>value * scale + bias</c>, anchored on the scene as drawn.
    /// </summary>
    /// <remarks>
    /// Refused, with the numbers, when a slot does not move in a straight line (a clamp, a wrap), a
    /// text or colour changes, or the scene's structure does. Linear over the range measured, which
    /// is around the first value; a clamp far outside it is not seen.
    /// </remarks>
    /// <param name="why">Why no slot takes it directly, for the message when it cannot be measured either.</param>
    private void Fit(string key, Decl d, string name, in SS.UiValue first, string? why)
    {
        var custom = name.StartsWith("--", StringComparison.Ordinal);
        if (_sampleOf == null || _scene == null)
        {
            d.No = (why != null ? why + ", and " : string.Empty)
                   + "what it moves is measured from the page, which is let go once compiled - send it in the first payload";
            return;
        }
        if (!First(first, name, out var n0, out var unit))
        {
            d.No = $"its first value {Show(first)} is not a number, so there is nothing to measure it around";
            return;
        }
        d.Unit = unit;
        d.Bare = unit.Length == 0 || (unit == "px" && !custom && !Unitless(name));
        // Wide enough that a layout's pixel rounding is small against the slope, and never below
        // zero for a value sent as positive - a negative height is not a smaller one.
        var spread = Math.Max(Math.Abs(n0) / 2, unit switch { "px" => 16, "%" => 10, "deg" => 15, "" => 0.25, _ => 0.5 });
        var at = n0 >= 0 && n0 - spread < 0
            ? new[] { n0, n0 + spread, n0 + 2 * spread }
            : new[] { n0 - spread, n0, n0 + spread };
        var got = new (string Template, Dictionary<string, SceneSlots.Value> Values)[3];
        for (var i = 0; i < 3; i++)
        {
            (string, Dictionary<string, SceneSlots.Value>)? sampled;
            try { sampled = _sampleOf(key, name, Format(at[i], unit)); }
            catch (Exception ex) { d.No = $"laying the page out with it at {Format(at[i], unit)} threw - {ex.Message}"; return; }
            if (sampled is not { } laid)
            {
                d.No = $"the page could not be laid out with it at {Format(at[i], unit)}";
                return;
            }
            got[i] = laid;
        }
        if (got[0].Template != got[1].Template || got[1].Template != got[2].Template)
        {
            d.No = $"it changes the scene's structure between {Format(at[0], unit)} and {Format(at[2], unit)}, which is a state and not a value";
            return;
        }
        // A line with no id is named by its position, which holds only if the page laid out here is
        // the one drawn; it is, unless something else about the page moved in between.
        var aligned = string.Equals(got[0].Template, _template, StringComparison.Ordinal);
        var targets = new List<Target>();
        foreach (var pair in got[0].Values)
        {
            var slot = pair.Key;
            var a = pair.Value;
            if (!got[1].Values.TryGetValue(slot, out var b) || !got[2].Values.TryGetValue(slot, out var c)) continue;
            if (!a.IsNumber || !b.IsNumber || !c.IsNumber)
            {
                if (a.Equals(b) && b.Equals(c)) continue;
                d.No = $"it changes \"{slot}\" ({Show(a)} at {Format(at[0], unit)}, {Show(c)} at {Format(at[2], unit)}), which is not a number";
                return;
            }
            if (Math.Abs(a.Number - b.Number) <= 0.01 && Math.Abs(b.Number - c.Number) <= 0.01) continue;
            var positional = Positional(slot, _prefix);
            if (positional && !aligned)
            {
                d.No = $"it moves \"{slot}\", which has no id, and the page laid out to measure it is not the one drawn - give that element an id";
                return;
            }
            if (!_scene.TryGetValue(slot, out var live) || !live.IsNumber)
            {
                d.No = $"it moves \"{slot}\", which is not a number in the emitted scene";
                return;
            }
            var slope = (c.Number - a.Number) / (at[2] - at[0]);
            // A layout rounds every box to the pixel, twice per comparison; an opacity or a scale is
            // not rounded, and a pixel there would be most of its range.
            var tolerance = Fraction(slot) ? 0.005 : 1.0;
            if (Math.Abs(a.Number + slope * (at[1] - at[0]) - b.Number) > tolerance)
            {
                d.No = $"it moves \"{slot}\" to {Show(a)} at {Format(at[0], unit)}, {Show(b)} at {Format(at[1], unit)} and {Show(c)} at {Format(at[2], unit)} - not a straight line, so not a value to write";
                return;
            }
            var sent = positional && _sendPrefix != _prefix ? _sendPrefix + slot.Substring(_prefix.Length) : slot;
            _baseline[sent] = live.Number;
            targets.Add(new Target(sent, live.Number - slope * n0, slope));
        }
        if (targets.Count == 0)
        {
            d.No = "nothing drawn moves when it changes";
            return;
        }
        d.Targets = targets.ToArray();
        d.Given = new double[d.Targets.Length];
        // A declaration that transitions glides every slot it moves on the same curve, which for a
        // straight line is the value gliding - siblings included, as a browser reflows them each frame.
        // A custom property does not transition by itself; what reads it does, on its own element.
        var own = !custom && _transitions.TryGetValue(key, out var css) ? CssTransition.For(css, name) : null;
        foreach (var t in d.Targets)
            if ((own ?? SlotTiming(t.Slot)) is { } timing) _eased[t.Slot] = timing;
    }

    /// <summary>
    /// One target's write, as a move from the compiled scene added to every other move of that slot:
    /// <paramref name="value"/> is where this declaration alone puts it. A slot the scene never drew
    /// as a number is written as it stands.
    /// </summary>
    private void Add(string slot, ref double given, double value)
    {
        if (!_baseline.TryGetValue(slot, out var at)) { _out[slot] = new SceneSlots.Value((float)value); return; }
        _moved.TryGetValue(slot, out var sum);
        sum += value - at - given;
        given = value - at;
        _moved[slot] = sum;
        _out[slot] = new SceneSlots.Value((float)(at + sum));
    }

    /// <summary>Where a slot stands now: this payload's write, else the scene plus every move since.</summary>
    private double Current(string slot, double otherwise)
    {
        if (_out.TryGetValue(slot, out var written) && written.IsNumber) return written.Number;
        if (!_baseline.TryGetValue(slot, out var at)) return otherwise;
        return _moved.TryGetValue(slot, out var sum) ? at + sum : at;
    }

    /// <summary>A bool's state: its own opacity outright, and what hiding it moves, as moves.</summary>
    private void State(Toggle t, bool hidden)
    {
        for (var k = 0; k < t.Slots.Length; k++)
        {
            var value = hidden ? t.Hidden[k] : t.Shown[k];
            if (k == t.Own) _out[t.Slots[k]] = new SceneSlots.Value(value);
            else Add(t.Slots[k], ref t.Given[k], value);
        }
    }

    /// <summary>A Linear declaration's first value as a number and its unit, lowercase; a length's bare number is px.</summary>
    private static bool First(in SS.UiValue v, string name, out double n, out string unit)
    {
        unit = string.Empty;
        n = 0;
        if (v.Type == SS.UiValueType.Number) n = v.Number;
        else if (v.Type != SS.UiValueType.String || !Scan(v.String, out n, out var us, out var ue)) return false;
        else unit = v.String!.Substring(us, ue - us).ToLowerInvariant();
        if (unit.Length == 0 && !name.StartsWith("--", StringComparison.Ordinal) && !Unitless(name)) unit = "px";
        return true;
    }

    /// <summary>A property whose bare number is a number and not px: a factor, a weight, an order.</summary>
    private static bool Unitless(string name) => name is "scale" or "flex-grow" or "flex-shrink" or "flex" or "line-height"
        or "z-index" or "order" or "font-weight" or "zoom" or "aspect-ratio";

    private static string Format(double v, string unit) => v.ToString("0.####", CultureInfo.InvariantCulture) + unit;

    /// <summary>A positional slot name (`L12_y`): the prefix, then the line number.</summary>
    private static bool Positional(string slot, string prefix)
        => slot.Length > prefix.Length && slot.StartsWith(prefix, StringComparison.Ordinal) && char.IsDigit(slot[prefix.Length]);

    /// <summary>A slot whose value is a fraction (an opacity, a scale), which no layout rounds.</summary>
    private static bool Fraction(string slot)
        => slot.EndsWith("_o", StringComparison.Ordinal) || slot.EndsWith("_fo", StringComparison.Ordinal)
           || slot.EndsWith("_so", StringComparison.Ordinal) || slot.EndsWith("_s_0", StringComparison.Ordinal)
           || slot.EndsWith("_s_1", StringComparison.Ordinal);

    /// <summary>The CSS property behind a named slot's key, for the transition its own element declares.</summary>
    private static readonly (string Suffix, string Property)[] SlotProperty =
    {
        ("_t_0", "transform"), ("_t_1", "transform"), ("_r", "transform"), ("_s_0", "transform"), ("_s_1", "transform"),
        ("_w", "width"), ("_h", "height"), ("_x", "left"), ("_y", "top"), ("_o", "opacity"),
    };

    /// <summary>The transition a named slot's own element declares for the property behind it, or null.</summary>
    private (float Dur, string Curve, float Delay)? SlotTiming(string slot)
    {
        foreach (var (suffix, property) in SlotProperty)
        {
            if (!slot.EndsWith(suffix, StringComparison.Ordinal)) continue;
            var id = slot.Substring(0, slot.Length - suffix.Length);
            // a box's label moves with its box, on the box's timing
            if (id.EndsWith(SceneSlots.SecondSuffix, StringComparison.Ordinal)) id = id.Substring(0, id.Length - SceneSlots.SecondSuffix.Length);
            foreach (var pair in _transitions)
                if (DomSlots.Slot(pair.Key) == id) return CssTransition.For(pair.Value, property);
            return null;
        }
        return null;
    }

    /// <summary>
    /// A bool's two states, captured while the page exists. Only the slots hiding the element
    /// actually moves go in, each at the value the LIVE scene holds - the capture is a second
    /// layout of the page, and it can differ from the emitted one by a fraction of a pixel (a body
    /// 479.96 high against the 480 drawn), which the proof would read as a disagreement and which
    /// written back would nudge a box that never moved. Hidden is the live value plus how far
    /// hiding moved it.
    /// </summary>
    private Toggle? ToggleFor(string key, Entry e)
    {
        if (e.Toggle != null || e.BoolNo != null) return e.Toggle;
        if (_toggleOf == null || _scene == null)
        {
            e.BoolNo = "is a bool first sent after the page was compiled, and hiding an element moves what follows it, which only a layout can say - send it in the first payload";
            Say($"\"{key}\" {e.BoolNo}; dropped");
            return null;
        }
        var captured = _toggleOf(key);
        // The capture names the element's own opacity by its raw id, which the scene spells as an
        // identifier (`over-limit` is `over_limit_o`): without it the state moved what follows and
        // left the element itself drawn.
        var own = DomSlots.Slot(key) + "_o";
        var hasOwn = false;
        foreach (var (slot, _) in captured.Shown) hasOwn |= slot == own;   // a loop: a lambda over `own` allocates on every call
        if (!hasOwn && _scene.TryGetValue(own, out var drawnO) && drawnO.IsNumber)
        {
            captured.Shown.Add((own, drawnO.Number));
            captured.Hidden.Add((own, 0));
            if (captured.Problem != null && captured.Problem.StartsWith("it draws nothing and moves nothing", StringComparison.Ordinal)) captured.Problem = null;
        }
        if (captured.Problem != null)
        {
            e.BoolNo = captured.Problem;
            Say($"\"{key}\" as a bool - {captured.Problem}; dropped");
            return null;
        }
        var slots = new List<string>();
        var shown = new List<float>();
        var hidden = new List<float>();
        foreach (var (slot, before) in captured.Shown)
        {
            var after = before;
            foreach (var h in captured.Hidden) if (h.Slot == slot) { after = h.Value; break; }
            var isOwn = slot == own;
            // half a pixel: two layouts of one page round differently, and a real move - the gap an
            // element leaves - is at least a line
            if (!isOwn && Math.Abs(after - before) < 0.5) continue;
            if (!_scene.TryGetValue(slot, out var live) || !live.IsNumber)
            {
                e.BoolNo = $"hiding it moves slot \"{slot}\", which is not in the emitted scene";
                Say($"\"{key}\" as a bool - {e.BoolNo}; dropped");
                return null;
            }
            slots.Add(slot);
            shown.Add(live.Number);
            hidden.Add(isOwn ? 0f : live.Number + (float)(after - before));
        }
        return e.Toggle = new Toggle { Slots = slots.ToArray(), Shown = shown.ToArray(), Hidden = hidden.ToArray(), Own = slots.IndexOf(own), Given = new double[slots.Count] };
    }

    /// <summary>A declaration's value as the number or colour its slots take; false when it is neither.</summary>
    private static bool Resolve(Decl d, in SS.UiValue v, out double number, out string? text)
    {
        number = 0;
        text = null;
        switch (d.Kind)
        {
            case Kind.Colour:
                if (v.Type != SS.UiValueType.String || string.IsNullOrEmpty(v.String)) return false;
                if (!string.Equals(v.String, d.LastIn, StringComparison.Ordinal))
                {
                    // As the emitter writes a colour (VectorEmitter.Hex), so `red` and `rgb()` land as
                    // the same #RRGGBB the scene holds - raw, they would disagree with the proof, and
                    // the renderer does not read `rgb()` at all.
                    d.LastOut = StyleApplier.TryColor(v.String!, out var c) ? VectorEmitter.Hex(c) : null;
                    d.LastIn = v.String;
                }
                text = d.LastOut;
                return text != null;

            case Kind.Transform:
                if (v.Type != SS.UiValueType.String) return false;
                // parsed only when it changed: a needle held still costs a string compare
                if (!string.Equals(v.String, d.LastIn, StringComparison.Ordinal))
                {
                    d.LastOk = Transform(v.String, d.W, d.H, d.Parts!);
                    d.LastIn = v.String;
                }
                return d.LastOk;

            case Kind.Display:
                if (v.Type != SS.UiValueType.String || string.IsNullOrWhiteSpace(v.String)) return false;
                number = string.Equals(v.String!.Trim(), "none", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                return true;

            case Kind.Linear:
            {
                // in the unit it was measured in: a length of another unit is another slope
                if (v.Type == SS.UiValueType.Number) { number = v.Number; return d.Bare; }
                if (v.Type != SS.UiValueType.String || !Scan(v.String, out number, out var us, out var ue)) return false;
                var unitLength = ue - us;
                if (unitLength == 0) return d.Bare;
                return unitLength == d.Unit.Length && string.Compare(v.String, us, d.Unit, 0, unitLength, StringComparison.OrdinalIgnoreCase) == 0;
            }

            case Kind.Visibility:
                if (v.Type != SS.UiValueType.String) return false;
                var word = v.String?.Trim();
                if (word is "hidden" or "collapse") { number = 0; return true; }
                // ponytail: visible is opacity 1, not the element's own; compose the two when a page needs both
                if (word == "visible") { number = 1; return true; }
                return false;

            default:
                if (v.Type == SS.UiValueType.Number) { number = v.Number; return true; }
                if (v.Type != SS.UiValueType.String) return false;
                // opacity takes a percentage of 1, as CSS does
                if (d.Kind == Kind.Opacity && DomSlots.Length(v.String, 100) is { } pct && v.String!.TrimEnd().EndsWith("%", StringComparison.Ordinal))
                { number = pct / 100; return true; }
                if (DomSlots.Length(v.String, d.PercentOf) is not { } len) return false;
                number = len;
                return true;
        }
    }

    private static Kind KindOf(string name) => name switch
    {
        "background" or "background-color" or "backgroundColor" or "color" or "border-color" or "borderColor" => Kind.Colour,
        "opacity" => Kind.Opacity,
        "visibility" => Kind.Visibility,
        "transform" => Kind.Transform,
        "display" => Kind.Display,
        _ when name.StartsWith("--", StringComparison.Ordinal) => Kind.Linear,
        _ => Kind.Length,
    };

    private static string Expected(Decl d) => d.Kind switch
    {
        Kind.Colour => "a colour",
        Kind.Visibility => "visible, hidden or collapse",
        Kind.Opacity => "a number or a %",
        Kind.Transform => "translate, rotate and scale functions - skew, matrix and 3D are a shape, not a value",
        Kind.Display => "a display value, none to hide",
        Kind.Linear => d.Bare ? (d.Unit.Length == 0 ? "a number" : "a number or " + d.Unit) : "a number in " + d.Unit + ", as the first payload sent it",
        _ => double.IsNaN(d.PercentOf) ? "px, as the containing block's size was not measured for a %" : "px or %",
    };

    private static string Show(in SS.UiValue v) => v.Type switch
    {
        SS.UiValueType.String => "\"" + v.String + "\"",
        SS.UiValueType.Number => v.Number.ToString(CultureInfo.InvariantCulture),
        SS.UiValueType.Bool => v.Bool ? "true" : "false",
        _ => "a " + v.Type,
    };

    /// <summary>The text as the emitter would have drawn it, built only when the value changed.</summary>
    private string TextOf(Entry e, string slot, in SS.UiValue v)
    {
        string raw;
        if (v.Type == SS.UiValueType.Number)
        {
            if (!e.HasNumber || !e.LastNumber.Equals(v.Number))
            {
                e.HasNumber = true;
                e.LastNumber = v.Number;
                e.LastNumberText = v.Number.ToString("G", CultureInfo.InvariantCulture);
            }
            raw = e.LastNumberText!;
        }
        else raw = v.String ?? string.Empty;

        var tabular = _mspace.TryGetValue(slot, out var open);
        if ((!tabular && e.Case == null) || raw.Length == 0) return raw;
        if (!string.Equals(raw, e.LastIn, StringComparison.Ordinal))
        {
            // Cased first, as the emitter does, and only then are the digit runs given their cells.
            var cased = e.Case == null ? raw : VectorEmitter.Transform(raw, e.Case);
            if (tabular)
            {
                _shaped.Clear();
                var i = 0;
                while (i < cased.Length)
                {
                    if (!char.IsDigit(cased[i])) { _shaped.Append(cased[i++]); continue; }
                    var start = i;
                    while (i < cased.Length && char.IsDigit(cased[i])) i++;
                    _shaped.Append(open).Append(cased, start, i - start).Append("</mspace>");
                }
                cased = _shaped.ToString();
            }
            e.LastIn = raw;
            e.LastOut = cased;
        }
        return e.LastOut!;
    }

    /// <summary>Why what <see cref="_out"/> holds is not what the emitter drew, or null when it is.</summary>
    private string? Disagrees(IReadOnlyDictionary<string, SceneSlots.Value> scene)
    {
        foreach (var pair in _out)
        {
            // a line with no id is written under the name the structure is sent with (Fit)
            var drawn = _sendPrefix != _prefix && Positional(pair.Key, _sendPrefix) ? _prefix + pair.Key.Substring(_sendPrefix.Length) : pair.Key;
            if (!scene.TryGetValue(drawn, out var was)) return $"slot \"{pair.Key}\" is not in the emitted scene";
            var mine = pair.Value;
            if (mine.IsNumber
                    ? !was.IsNumber || Math.Abs(was.Number - mine.Number) > 0.01f
                    : was.IsNumber || !SameText(was.Text, mine.Text))
                return $"slot \"{pair.Key}\": the emitter drew {Show(was)}, this would write {Show(mine)}";
        }
        return null;
    }

    /// <summary>
    /// Equal, or equal once the emitter's &lt;noparse&gt; guard is taken off. That guard is for the
    /// scene reader, which types a quoted "3" as a number; a data value arrives as a string and needs
    /// none, and TextMeshPro draws the two alike.
    /// </summary>
    private static bool SameText(string? drawn, string? mine)
    {
        const string open = "<noparse>", close = "</noparse>";
        if (string.Equals(drawn, mine, StringComparison.Ordinal)) return true;
        return drawn != null && mine != null && drawn.Length == mine.Length + open.Length + close.Length
               && drawn.StartsWith(open, StringComparison.Ordinal) && drawn.EndsWith(close, StringComparison.Ordinal)
               && string.CompareOrdinal(drawn, open.Length, mine, 0, mine.Length) == 0;
    }

    private static string Show(SceneSlots.Value v) =>
        v.IsNumber ? v.Number.ToString("0.##", CultureInfo.InvariantCulture) : "\"" + v.Text + "\"";

    /// <summary>The names after each `$` in the template that are not the scene's own slots.</summary>
    private void ReadNames(string template, IReadOnlyDictionary<string, SceneSlots.Value> scene)
    {
        var i = 0;
        while (i < template.Length)
        {
            if (template[i++] != '$') continue;
            var start = i;
            while (i < template.Length && (char.IsLetterOrDigit(template[i]) || template[i] == '_')) i++;
            if (i == start) continue;
            var name = template.Substring(start, i - start);
            if (!scene.ContainsKey(name)) _reads.Add(name);
        }
    }

    /// <summary>Whether the scene reads the key itself or a name flattened from it (`co2` as `$co2_gasFill`).</summary>
    private bool ReadsKey(string key)
    {
        foreach (var name in _reads)
            if (name.StartsWith(key, StringComparison.Ordinal) && (name.Length == key.Length || name[key.Length] == '_')) return true;
        return false;
    }

    /// <summary>
    /// A leading number and where its unit runs (trailing white space excluded), read in place: this
    /// runs on every tick a Linear declaration is sent.
    /// </summary>
    private static bool Scan(string? s, out double n, out int unitStart, out int unitEnd)
    {
        n = 0;
        unitStart = unitEnd = 0;
        return !string.IsNullOrEmpty(s) && Scan(s!, 0, s!.Length, out n, out unitStart, out unitEnd);
    }

    private static bool Scan(string s, int from, int to, out double n, out int unitStart, out int unitEnd)
    {
        n = 0;
        unitStart = unitEnd = 0;
        var i = from;
        while (i < to && char.IsWhiteSpace(s[i])) i++;
        var start = i;
        if (i < to && (s[i] == '-' || s[i] == '+')) i++;
        var digits = 0;
        while (i < to && (char.IsDigit(s[i]) || s[i] == '.')) { if (s[i] != '.') digits++; i++; }
        if (digits == 0) return false;
        // an exponent, when an `e` is followed by a digit (a unit such as `em` is not one)
        if (i + 1 < to && (s[i] == 'e' || s[i] == 'E')
            && (char.IsDigit(s[i + 1]) || ((s[i + 1] == '-' || s[i + 1] == '+') && i + 2 < to && char.IsDigit(s[i + 2]))))
        {
            i += 2;
            while (i < to && char.IsDigit(s[i])) i++;
        }
        if (!double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) return false;
        var end = to;
        while (end > i && char.IsWhiteSpace(s[end - 1])) end--;
        unitStart = i;
        unitEnd = end;
        return true;
    }

    /// <summary>
    /// A CSS transform list as the vector group's five numbers - translate x and y, rotate in
    /// degrees, scale x and y - into <paramref name="into"/>; false for anything the group cannot
    /// carry (skew, matrix, 3D, an unknown function, a % of a size not measured).
    /// </summary>
    /// <remarks>
    /// The group applies scale, then rotation, then translation, about the transform-origin, which
    /// is a list in the order translate, rotate, scale. A list in that order is read function by
    /// function: turns add up, so a needle going from 350deg to 370deg turns 20 degrees as the
    /// CSS says rather than back round. Any other order is composed into one matrix and taken apart
    /// again, which is exact while it has no skew. Allocates nothing.
    /// </remarks>
    internal static bool Transform(string? css, double w, double h, double[] into)
    {
        if (css == null) return false;
        double tx = 0, ty = 0, r = 0, sx = 1, sy = 1;
        double ma = 1, mb = 0, mc = 0, md = 1, me = 0, mf = 0;   // the list as one matrix
        var phase = 0;
        var ordered = true;
        var i = 0;
        while (i < css.Length && char.IsWhiteSpace(css[i])) i++;
        var end = css.Length;
        while (end > i && char.IsWhiteSpace(css[end - 1])) end--;
        if (end - i == 4 && string.Compare(css, i, "none", 0, 4, StringComparison.OrdinalIgnoreCase) == 0) i = end;
        while (true)
        {
            while (i < end && char.IsWhiteSpace(css[i])) i++;
            if (i >= end) break;
            var nameStart = i;
            while (i < end && char.IsLetterOrDigit(css[i])) i++;
            var nameLength = i - nameStart;
            while (i < end && char.IsWhiteSpace(css[i])) i++;
            if (nameLength == 0 || i >= end || css[i] != '(') return false;
            i++;
            double v0 = 0, v1 = 0;
            int u0 = 0, u0e = 0, u1 = 0, u1e = 0, count = 0;
            while (true)
            {
                while (i < end && (char.IsWhiteSpace(css[i]) || css[i] == ',')) i++;
                if (i >= end) return false;
                if (css[i] == ')') { i++; break; }
                var argStart = i;
                while (i < end && css[i] != ')' && css[i] != ',' && !char.IsWhiteSpace(css[i])) i++;
                if (count == 2 || !Scan(css, argStart, i, out var v, out var us, out var ue)) return false;
                if (count == 0) { v0 = v; u0 = us; u0e = ue; } else { v1 = v; u1 = us; u1e = ue; }
                count++;
            }
            if (count == 0) return false;
            double dx = 0, dy = 0, turn = 0, kx = 1, ky = 1;
            int kind;
            if (Is(css, nameStart, nameLength, "translate") || Is(css, nameStart, nameLength, "translateX") || Is(css, nameStart, nameLength, "translateY"))
            {
                kind = 0;
                var onlyY = Is(css, nameStart, nameLength, "translateY");
                var onlyX = Is(css, nameStart, nameLength, "translateX");
                if ((onlyX || onlyY) && count != 1) return false;
                if (!onlyY && !Length(css, v0, u0, u0e, w, out dx)) return false;
                if (onlyY && !Length(css, v0, u0, u0e, h, out dy)) return false;
                if (count == 2 && !Length(css, v1, u1, u1e, h, out dy)) return false;
            }
            else if (Is(css, nameStart, nameLength, "rotate") || Is(css, nameStart, nameLength, "rotateZ"))
            {
                kind = 1;
                if (count != 1 || !Angle(css, v0, u0, u0e, out turn)) return false;
            }
            else if (Is(css, nameStart, nameLength, "scale") || Is(css, nameStart, nameLength, "scaleX") || Is(css, nameStart, nameLength, "scaleY"))
            {
                kind = 2;
                var onlyX = Is(css, nameStart, nameLength, "scaleX");
                var onlyY = Is(css, nameStart, nameLength, "scaleY");
                if ((onlyX || onlyY) && count != 1) return false;
                if (!Factor(css, v0, u0, u0e, out var f0)) return false;
                var f1 = f0;
                if (count == 2 && !Factor(css, v1, u1, u1e, out f1)) return false;
                kx = onlyY ? 1 : f0;
                ky = onlyX ? 1 : f1;
            }
            // depth with nothing to see it by is the identity, as in a browser
            else if (Is(css, nameStart, nameLength, "translateZ") || Is(css, nameStart, nameLength, "scaleZ")) continue;
            else return false;

            if (kind < phase) ordered = false; else phase = kind;
            tx += dx; ty += dy; r += turn; sx *= kx; sy *= ky;
            // this function's own matrix, on the right: the list applies right to left
            var rad = turn * Math.PI / 180;
            var (fa, fb, fc, fd) = kind == 1 ? (Math.Cos(rad), Math.Sin(rad), -Math.Sin(rad), Math.Cos(rad)) : (kx, 0.0, 0.0, ky);
            (ma, mb, mc, md, me, mf) = (ma * fa + mc * fb, mb * fa + md * fb, ma * fc + mc * fd, mb * fc + md * fd,
                                        ma * dx + mc * dy + me, mb * dx + md * dy + mf);
        }
        if (!ordered)
        {
            // R(r) * diag(sx, sy), taken apart: its columns are perpendicular, or it is a skew
            sx = Math.Sqrt(ma * ma + mb * mb);
            if (sx < 1e-9) return false;
            sy = (ma * md - mb * mc) / sx;
            if (Math.Abs(ma * mc + mb * md) > 1e-6 * Math.Max(1, Math.Abs(sx * sy))) return false;
            r = Math.Atan2(mb, ma) * 180 / Math.PI;
            tx = me;
            ty = mf;
        }
        into[0] = tx; into[1] = ty; into[2] = r; into[3] = sx; into[4] = sy;
        return true;

        static bool Is(string s, int at, int length, string name)
            => length == name.Length && string.Compare(s, at, name, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
        static bool Unit(string s, int us, int ue, string unit)
            => ue - us == unit.Length && string.Compare(s, us, unit, 0, unit.Length, StringComparison.OrdinalIgnoreCase) == 0;
        static bool Length(string s, double v, int us, int ue, double of, out double px)
        {
            px = v;
            if (us == ue || Unit(s, us, ue, "px")) return true;
            if (!Unit(s, us, ue, "%") || double.IsNaN(of)) return false;
            px = v * of / 100;
            return true;
        }
        static bool Angle(string s, double v, int us, int ue, out double deg)
        {
            deg = v;
            if (us == ue || Unit(s, us, ue, "deg")) return true;
            if (Unit(s, us, ue, "rad")) { deg = v * 180 / Math.PI; return true; }
            if (Unit(s, us, ue, "grad")) { deg = v * 0.9; return true; }
            if (Unit(s, us, ue, "turn")) { deg = v * 360; return true; }
            return false;
        }
        static bool Factor(string s, double v, int us, int ue, out double k)
        {
            k = v;
            if (us == ue) return true;
            if (!Unit(s, us, ue, "%")) return false;
            k = v / 100;
            return true;
        }
    }

    /// <summary>
    /// The five numbers as the one list the layout engine draws exactly: translate, rotate, scale.
    /// A page's own order, a turn written twice or a part the value leaves out would otherwise reach
    /// the engine as it stands, which keeps one of each and never resets the rest.
    /// </summary>
    internal static string Canonical(double[] parts)
    {
        string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        return $"translate({F(parts[0])}px, {F(parts[1])}px) rotate({F(parts[2])}deg) scale({F(parts[3])}, {F(parts[4])})";
    }

    // ---- the page, while it exists: before the compile and during it ----------------------------

    /// <summary>
    /// One key's declarations onto its element before the page compiles, kept as a browser keeps an
    /// inline style: on the node, so a re-cascade - a custom property's, a capture's - applies them
    /// again instead of putting the stylesheet's values back over them.
    /// </summary>
    /// <returns>
    /// Whether the element and its subtree were cascaded again (a custom property changed), which
    /// also puts back whatever the stylesheet says about their `display`.
    /// </returns>
    internal static bool Bind(HtmlRenderer.Result built, VisualElement ve, SS.UiProp[] decls, Action<string>? warn)
    {
        if (!built.NodeOf.TryGetValue(ve, out var node)) return false;
        var custom = false;
        double[]? parts = null;
        foreach (var decl in decls)
        {
            var name = decl.Key ?? string.Empty;
            // Shown whatever the value, as a bool's element is: the scene the compile checks carries
            // its lines, and the table writes the state.
            if (name.Length == 0 || name == "display") continue;
            var text = decl.Value.Type == SS.UiValueType.Number
                ? decl.Value.Number.ToString("G", CultureInfo.InvariantCulture)
                : decl.Value.String ?? string.Empty;
            if (name == "transform" && Transform(text, ve.layout.width, ve.layout.height, parts ??= new double[5]))
                text = Canonical(parts);
            (node.ScriptStyle ??= new Dictionary<string, string>(StringComparer.Ordinal))[name] = text;
            if (name.StartsWith("--", StringComparison.Ordinal))
            {
                custom = true;
                (node.Vars ??= new Dictionary<string, string>(StringComparer.Ordinal))[name] = text;
                continue;
            }
            StyleApplier.Apply(ve, new CssDeclaration(name, text), warn);
            built.CssOf(ve)[name] = text;
            Hooked(built, ve, name);
        }
        // Every rule that reads a custom property with var() is cascaded again, the element's and its subtree's.
        if (custom) built.Reclass(ve, node.Attr("class") ?? string.Empty);
        return custom;
    }

    /// <summary>
    /// The page laid out and emitted with one declaration of one element set to <paramref name="value"/>,
    /// split with the live scene's prefix, and put back as it was: what <see cref="Fit"/> measures.
    /// </summary>
    /// <param name="recascaded">Run after the element is cascaded again, to re-show what the data keeps shown.</param>
    internal static (string Template, Dictionary<string, SceneSlots.Value> Values)? Sample(
        HtmlRenderer.Result built, string id, string property, string value, string prefix, Action? recascaded = null)
    {
        if (!built.ById.TryGetValue(id, out var ve) || ve == null || !built.NodeOf.TryGetValue(ve, out var node)
            || built.Root.panel is not { } panel) return null;
        var style = node.ScriptStyle ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var had = style.TryGetValue(property, out var was);
        var custom = property.StartsWith("--", StringComparison.Ordinal);
        try
        {
            Set(value);
            panel.Layout(panel.Width, panel.Height);
            var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            return (Emitted(built, panel, values, prefix), values);
        }
        finally
        {
            // Compiling leaves the page as it found it: the next capture is laid out from here.
            if (had) Set(was!);
            else
            {
                style.Remove(property);
                if (custom) node.Vars?.Remove(property);
                built.Reclass(ve, node.Attr("class") ?? string.Empty);
                recascaded?.Invoke();
            }
            panel.Layout(panel.Width, panel.Height);
        }

        void Set(string v)
        {
            style[property] = v;
            if (custom)
            {
                (node.Vars ??= new Dictionary<string, string>(StringComparer.Ordinal))[property] = v;
                built.Reclass(ve, node.Attr("class") ?? string.Empty);
                recascaded?.Invoke();
                return;
            }
            StyleApplier.Apply(ve, new CssDeclaration(property, v), null);
            built.CssOf(ve)[property] = v;
            Hooked(built, ve, property);
        }
    }

    /// <summary>
    /// What the cascade does after a declaration beyond applying it: the emitter's cache touched, and
    /// a flex `gap` - which the layout engine lacks - turned into its children's margins again.
    /// Without the second, a gap sent as data moved nothing and measured as nothing.
    /// </summary>
    private static void Hooked(HtmlRenderer.Result built, VisualElement ve, string name)
    {
        if (name is "gap" or "row-gap" or "column-gap") HtmlRenderer.ApplyGap(ve, built.CssOf(ve), built);
        if (VectorEmitter.Inherits(name)) built.TouchSubtree(ve); else built.Touch(ve);
    }

    /// <summary>As PageCompiler.Emitted, split with a given prefix so a line with no id has the name it has live.</summary>
    private static string Emitted(HtmlRenderer.Result built, Panel panel, Dictionary<string, SceneSlots.Value> values, string prefix)
    {
        var wasBoxes = OffThread.Boxes;
        var wasActive = OffThread.Active;
        var wasJob = OffThread.Job;
        // Capture clears the touch sets the surface's incremental capture relies on; put them back.
        var touched = new HashSet<VisualElement>(built.Touched);
        var deep = new HashSet<VisualElement>(built.TouchedDeep);
        try
        {
            var boxes = new Dictionary<VisualElement, OffThread.Box>();
            OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());
            built.Touched.UnionWith(touched);
            built.TouchedDeep.UnionWith(deep);
            OffThread.Boxes = boxes;
            OffThread.Active = true;
            OffThread.Job = OffThread.Globals.Take();
            var output = VectorEmitter.Isolated(() => VectorEmitter.Emit(built, built.Root, panel.Width, panel.Height));
            return SceneSlots.Split(output.Chars, output.Length, values, prefix);
        }
        finally
        {
            OffThread.Boxes = wasBoxes;
            OffThread.Active = wasActive;
            OffThread.Job = wasJob;
        }
    }

    private void Refuse(Entry e, string why)
    {
        e.No = why;
        Say(why + "; the key is dropped, the rest applies");
    }

    private void Say(string what) => _warn("data " + what);
}

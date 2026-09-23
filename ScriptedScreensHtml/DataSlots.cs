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
    private enum Kind { Length, Colour, Opacity, Visibility }

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
    }

    /// <summary>A bool's two states: its element's opacity and every slot that hiding it moves.</summary>
    private sealed class Toggle
    {
        public string[] Slots = Array.Empty<string>();
        public float[] Shown = Array.Empty<float>();
        public float[] Hidden = Array.Empty<float>();
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
    /// <summary>Per element with one px corner radius: the radius as declared, and its `rx` slot.</summary>
    private readonly Dictionary<string, (double R, string Slot)> _radius = new(StringComparer.Ordinal);
    private HashSet<string> _available = new(StringComparer.Ordinal);
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
    private IReadOnlyDictionary<string, SceneSlots.Value>? _scene;

    /// <summary>What the last Apply wrote. Reused: a tick allocates nothing here.</summary>
    private readonly Dictionary<string, SceneSlots.Value> _out = new(StringComparer.Ordinal);

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
    internal static DataSlots Compile(IReadOnlyList<KeyValuePair<string, SS.UiValue>> sample,
                                      IReadOnlyDictionary<string, SceneSlots.Value> scene,
                                      string template,
                                      IEnumerable<string> ids,
                                      Func<string, HashSet<string>, DomSlots.Box?> boxOf,
                                      Func<string, IReadOnlyDictionary<string, string>?> cssOf,
                                      IEnumerable<string> shapes,
                                      Func<string, DomSlots.Toggle>? toggleOf,
                                      Action<string> warn)
    {
        var map = new DataSlots(warn);
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
            if (css.TryGetValue("border-radius", out var radius) && DomSlots.Length(radius) is double px && px > 0 && map._available.Contains(id + "_rx"))
                map._radius[id] = (px, id + "_rx");
        }
        map.ReadNames(template, scene);
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
        map._scene = scene;
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
        finally { map._toggleOf = null; map._scene = null; }

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
                var state = proving || value.Bool ? t.Shown : t.Hidden;
                for (var i = 0; i < t.Slots.Length; i++) _out[t.Slots[i]] = new SceneSlots.Value(state[i]);
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
                    var d = DeclOf(key, e, props[i].Key ?? string.Empty);
                    if (d.No != null || Resolve(d, props[i].Value, out _, out _)) continue;
                    if (!d.Warned)
                    {
                        d.Warned = true;
                        Say($"\"{key}\".{props[i].Key} = {Show(props[i].Value)} is not a value this can write ({Expected(d)}) - the key is dropped for that payload and applies again with the next that it can place");
                    }
                    return;
                }
                double? w = null, h = null, r = null;
                for (var i = 0; i < props.Length; i++)
                {
                    var d = DeclOf(key, e, props[i].Key ?? string.Empty);
                    if (d.No != null) continue;
                    Resolve(d, props[i].Value, out var number, out var text);
                    foreach (var t in d.Targets)
                        _out[t.Slot] = text != null ? new SceneSlots.Value(text) : new SceneSlots.Value((float)(number * t.Scale + t.Bias));
                    switch (props[i].Key)
                    {
                        case "width": w = number; break;
                        case "height": h = number; break;
                        case "border-radius" or "borderRadius": r = number; break;
                    }
                }
                // CSS scales a radius down when two of them overflow a side, and the emitter does the
                // same (VectorEmitter.Radii) - without it a bar written below twice its radius is a
                // self-intersecting shape. The width moved, so the clamp has to move with it.
                if ((w != null || h != null || r != null) && _radius.TryGetValue(key, out var round))
                {
                    var box = e.Box.Value;
                    var clamped = Math.Min(r ?? round.R, Math.Min((w ?? box.W) / 2, (h ?? box.H) / 2));
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

    private Decl DeclOf(string key, Entry e, string name)
    {
        e.Decls ??= new Dictionary<string, Decl>(StringComparer.Ordinal);
        if (e.Decls.TryGetValue(name, out var d)) return d;
        d = new Decl { Kind = KindOf(name) };
        e.Decls[name] = d;
        if (name is "transform")
        {
            d.No = "a transform's value is a function list, which this does not parse";
        }
        else
        {
            var mapped = DomSlots.Map(key, "style." + name, e.Box!.Value, _available);
            if (!mapped.Mapped) d.No = mapped.Problem;
            // The element's opacity group carries an id only when something named it before the
            // scene was emitted (HtmlSurface does, for a bool or an opacity in the first payload):
            // naming every group would make the renderer retain every element's props.
            else if (mapped.NeedsGroup) d.No = $"\"{key}\"'s opacity group was not named when the page was compiled - send `{name}` in the first payload";
            else
            {
                d.PercentOf = mapped.PercentOf;
                d.Targets = new Target[mapped.Slots.Length];
                for (var i = 0; i < d.Targets.Length; i++) d.Targets[i] = new Target(mapped.Slots[i], mapped.Bias[i], mapped.Scale[i]);
                if (_transitions.TryGetValue(key, out var css) && CssTransition.For(css, name) is { } timing)
                    foreach (var slot in mapped.Slots) _eased[slot] = timing;
            }
        }
        if (d.No != null) Say($"\"{key}\".{name} - {d.No}; that declaration is dropped, the key's others apply");
        return d;
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
        if (captured.Problem != null)
        {
            e.BoolNo = captured.Problem;
            Say($"\"{key}\" as a bool - {captured.Problem}; dropped");
            return null;
        }
        var own = key + "_o";
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
        return e.Toggle = new Toggle { Slots = slots.ToArray(), Shown = shown.ToArray(), Hidden = hidden.ToArray() };
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
        _ => Kind.Length,
    };

    private static string Expected(Decl d) => d.Kind switch
    {
        Kind.Colour => "a colour",
        Kind.Visibility => "visible, hidden or collapse",
        Kind.Opacity => "a number or a %",
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
            if (!scene.TryGetValue(pair.Key, out var was)) return $"slot \"{pair.Key}\" is not in the emitted scene";
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

    private void Refuse(Entry e, string why)
    {
        e.No = why;
        Say(why + "; the key is dropped, the rest applies");
    }

    private void Say(string what) => _warn("data " + what);
}

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
/// what to write, and <see cref="DomSlots"/> already knows which slot that lands on. This is the
/// same mapping the compiled script path uses, reached from the other end.
///
/// <b>It proves itself before it is trusted.</b> The first payload runs the ordinary path anyway -
/// the page has to be laid out at least once - so this computes what it <i>would</i> have sent and
/// compares against what the emitter actually produced. Only on a match does the fast path switch
/// on. A disagreement disables it and says so, rather than quietly drawing something else.
/// </remarks>
internal sealed class DataSlots
{
    /// <summary>One data key and where its value goes.</summary>
    private readonly struct Target
    {
        public readonly string Slot;
        public readonly double Bias;
        /// <summary>Text goes through as a string; everything else is a number the scene reads.</summary>
        public readonly bool IsText;

        /// <summary>
        /// What the value is multiplied by before the bias is added: 1 for everything except a
        /// far-edge position, where it is -1. Carried here because a mapping that produced it and a
        /// consumer that dropped it would put `right` on the wrong side of its parent, silently.
        /// </summary>
        public readonly double Scale;
        /// <summary>What a `%` value is a percentage of (the containing block's size), NaN when it cannot be.</summary>
        public readonly double PercentOf;
        /// <summary>
        /// The CSS declaration this is a target of (<c>width</c>), for a key whose value is a map;
        /// null for text. A declaration lands on one slot per line it reaches, so a payload's
        /// declarations are matched to their targets by name - not by position, which paired
        /// declaration i with target i and broke on the first declaration with two slots.
        /// </summary>
        public readonly string? Decl;
        public Target(string slot, double bias, bool isText, double scale = 1, double percentOf = double.NaN, string? decl = null)
        { Slot = slot; Bias = bias; IsText = isText; Scale = scale; PercentOf = percentOf; Decl = decl; }
    }

    private readonly Dictionary<string, List<Target>> _byKey = new(StringComparer.Ordinal);

    /// <summary>
    /// Per bool key, the two states it picks between, and which one it is in. A bool shows or hides
    /// an element; in normal flow that moves what follows, so the state is the element's opacity
    /// plus every sibling that closes the gap, captured once from the laid-out page.
    /// </summary>
    private readonly Dictionary<string, DomSlots.Toggle> _toggles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _shown = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether this key is a bool the fast path toggles. The surface then keeps the element in the
    /// layout whatever the value: the scene it proves against and later writes into has to carry
    /// the element's lines, and a `display: none` would take them out.
    /// </summary>
    internal bool Toggles(string key) => Problem == null && _toggles.ContainsKey(key);

    /// <summary>
    /// Slots whose property has a CSS transition, with its duration and curve. They are sent without
    /// <c>snap</c> and with the renderer's <c>ease</c> timing (vector 0.11.33), so the renderer runs
    /// the glide the CSS declares; every other slot snaps, as a browser does.
    /// </summary>
    /// <remarks>
    /// This used to be a refusal: a transition was compiled into the scene as an expression, only the
    /// emitter could write one, so any transitioned key kept the whole page on the full path - a
    /// layout, translate and emit per tick, for ever, for one gliding bar. The renderer glides a value
    /// itself, and since 0.11.33 for a stated duration and curve; timing is a property of the change
    /// there, as in CSS, so it goes on every payload that moves the slot, not once. A delay rides as
    /// the array's third element, only when there is one.
    /// </remarks>
    private readonly Dictionary<string, (float Dur, string Curve, float Delay)> _eased = new(StringComparer.Ordinal);

    internal bool TryEase(string slot, out (float Dur, string Curve, float Delay) timing) => _eased.TryGetValue(slot, out timing);

    /// <summary>
    /// Per text slot, the em width its digit runs are monospaced at, or absent for plain text.
    /// </summary>
    /// <remarks>
    /// `font-variant-numeric: tabular-nums` makes the emitter wrap every digit run in
    /// `&lt;mspace=Xem&gt;`, and X comes from the element's measured font - which is exactly the
    /// layout this class exists not to do. So it is LEARNED from the value the emitter produced,
    /// during the same comparison that proves the mapping. That is sound because the em is a
    /// property of the element's font size: it cannot change without a relayout, and a relayout
    /// rebuilds all of this anyway.
    ///
    /// Found by the check rather than by reading the emitter: the first run refused with
    /// `the emitter drew "&lt;mspace=0.57em&gt;58&lt;/mspace&gt;.&lt;mspace=0.57em&gt;9&lt;/mspace&gt;",
    /// this would write "58.9"`, which named the problem and its size in one line.
    /// </remarks>
    private readonly Dictionary<string, string> _mspace = new(StringComparer.Ordinal);
    private readonly System.Text.StringBuilder _shaped = new(64);

    /// <summary>Why the fast path is off, or null while it is on.</summary>
    internal string? Problem { get; private set; }

    /// <summary>
    /// Why a later payload could not be placed, naming the key and the value, or null. A page's
    /// keys, declarations and units are fixed by the payload it was built from; one that carries
    /// anything else is dropped whole (HtmlSurface.ApplyPairs), and this is the reason, said once.
    /// </summary>
    internal string? Refused { get; private set; }

    /// <summary>True once the first payload has been checked against the emitter and agreed.</summary>
    internal bool Proven { get; private set; }

    /// <summary>Data keys this knows how to place. A payload naming anything else is not handled.</summary>
    internal int Count => _byKey.Count;

    /// <summary>
    /// Works out where every key in a payload would go, or gives the reason it cannot.
    /// </summary>
    /// <remarks>
    /// One refusal anywhere disables the whole payload rather than the key. A payload half applied
    /// through slots and half through a re-layout would draw a frame that is neither, and the
    /// failure would be intermittent - visible only on the ticks where the awkward key changed.
    /// </remarks>
    /// <param name="toggleOf">
    /// What a bool key's element looks like shown and hidden (PageCompiler.ToggleOf). Null when the
    /// caller cannot lay the page out, and every bool is then refused, saying so.
    /// </param>
    internal static DataSlots Build(IReadOnlyList<KeyValuePair<string, SS.UiValue>> sample,
                                    Func<string, DomSlots.Box?> boxOf,
                                    Func<string, IReadOnlyDictionary<string, string>?> cssOf,
                                    Func<string, bool> isShape,
                                    ICollection<string> available,
                                    Func<string, DomSlots.Toggle>? toggleOf = null)
    {
        var map = new DataSlots();
        foreach (var entry in sample)
        {
            var key = entry.Key;
            if (string.IsNullOrEmpty(key)) { map.Problem = "a payload carried an empty key"; return map; }

            // An SVG shape's value is read by the vector mod straight from $name, so the scene text
            // does not change and there is nothing here to place. It is already as cheap as this
            // would make it.
            if (isShape(key)) { map.Problem = $"\"{key}\" is an svg shape, which the scene already reads as $ {key}"; return map; }

            // A `$name` the scene reads as an expression, with no element of that name, is the same
            // story - already free, and not ours to route.
            var box = boxOf(key);
            if (box == null) { map.Problem = $"\"{key}\" names no element in the page"; return map; }

            // A transitioned property's slots glide on the renderer instead of jumping (see _eased);
            // the surface has muted the emitter's own tween for the element, so the emitted scene
            // carries the plain number this would write and the proof below still holds.
            var css = cssOf(key);

            var targets = new List<Target>();
            switch (entry.Value.Type)
            {
                case SS.UiValueType.String:
                case SS.UiValueType.Number:
                {
                    var mapped = DomSlots.Map(key, "textContent", box.Value, available);
                    if (!mapped.Mapped) { map.Problem = $"\"{key}\" - {mapped.Problem}"; return map; }
                    for (var i = 0; i < mapped.Slots.Length; i++)
                        targets.Add(new Target(mapped.Slots[i], mapped.Bias[i], isText: true, mapped.Scale[i]));
                    break;
                }

                case SS.UiValueType.Map when entry.Value.Map != null:
                {
                    foreach (var decl in entry.Value.Map)
                    {
                        var mapped = DomSlots.Map(key, "style." + decl.Key, box.Value, available);
                        if (!mapped.Mapped) { map.Problem = $"\"{key}\".{decl.Key} - {mapped.Problem}"; return map; }
                        // A length is written as a number, and only px and % resolve to one here:
                        // "62%" is of the containing block, which the mapping measured or did not.
                        // Checked on the sample so the page is refused before it is compiled; a
                        // later payload in another unit is dropped by Apply, which says so.
                        if (decl.Value.Type == SS.UiValueType.String && DomSlots.LooksLikeLength(decl.Value.String)
                            && DomSlots.Length(decl.Value.String, mapped.PercentOf) == null)
                        {
                            map.Problem = $"\"{key}\".{decl.Key} = \"{decl.Value.String}\" - only px and % lengths can be written directly"
                                          + (double.IsNaN(mapped.PercentOf) ? ", and the containing block's size was not measured" : string.Empty);
                            return map;
                        }
                        var timing = css != null ? CssTransition.For(css, decl.Key) : null;
                        for (var i = 0; i < mapped.Slots.Length; i++)
                        {
                            targets.Add(new Target(mapped.Slots[i], mapped.Bias[i], isText: false, mapped.Scale[i], mapped.PercentOf, decl.Key));
                            if (timing is { } tm) map._eased[mapped.Slots[i]] = tm;
                        }
                    }
                    break;
                }

                // Shows or hides the element: a two-value STATE, the same thing a class toggle is.
                // Hiding a box in normal flow closes the gap it left, so each state carries the
                // element's opacity and every sibling that moves, and the value picks one.
                case SS.UiValueType.Bool:
                {
                    if (toggleOf == null) { map.Problem = $"\"{key}\" is a bool, and there is no way to lay the page out with the element hidden"; return map; }
                    var toggle = toggleOf(key);
                    if (toggle.Problem != null) { map.Problem = $"\"{key}\" - {toggle.Problem}"; return map; }
                    map._toggles[key] = toggle;
                    map._shown[key] = entry.Value.Bool;
                    break;
                }

                default:
                    map.Problem = $"\"{key}\" carries a {entry.Value.Type}, which has no slot";
                    return map;
            }

            if (targets.Count > 0 || map._toggles.ContainsKey(key)) map._byKey[key] = targets;
        }

        if (map._byKey.Count == 0) map.Problem = "the payload placed nothing";
        return map;
    }

    /// <summary>What this payload writes, or null when it carries anything the first payload did not (see <see cref="Refused"/>).</summary>
    internal Dictionary<string, object>? Apply(IReadOnlyList<KeyValuePair<string, SS.UiValue>> entries) => Apply(entries, proving: false);

    /// <param name="proving">
    /// Read every bool as shown and remember nothing. The scene the proof runs against was emitted
    /// with the element in the layout whatever the value (see <see cref="Toggles"/>), so the shown
    /// state is the one it can be checked against; the hidden state's numbers come from the same
    /// layout and only their slots' presence is provable.
    /// </param>
    private Dictionary<string, object>? Apply(IReadOnlyList<KeyValuePair<string, SS.UiValue>> entries, bool proving)
    {
        if (Problem != null) return null;
        Dictionary<string, object>? values = null;

        foreach (var entry in entries)
        {
            var key = entry.Key ?? string.Empty;
            if (!_byKey.TryGetValue(key, out var targets))
                return Refuse(key, null, null, "is not a key the first payload carried", proving);
            // a bool became a string, or a string a bool: a different shape, not a different value
            if (_toggles.ContainsKey(key) != (entry.Value.Type == SS.UiValueType.Bool))
                return Refuse(key, null, null, "carries a different kind of value from the first payload's", proving);

            switch (entry.Value.Type)
            {
                case SS.UiValueType.Bool:
                {
                    var shown = proving || entry.Value.Bool;
                    // written on the game thread (ApplyPairs) and read by Overlay on the page's
                    if (!proving) lock (_shown) _shown[key] = shown;
                    var toggle = _toggles[key];
                    foreach (var (slot, v) in shown ? toggle.Shown : toggle.Hidden) (values ??= New())[slot] = v;
                    break;
                }

                case SS.UiValueType.String:
                    foreach (var t in targets) (values ??= New())[t.Slot] = Shape(t.Slot, entry.Value.String ?? string.Empty);
                    break;

                case SS.UiValueType.Number:
                    foreach (var t in targets)
                        (values ??= New())[t.Slot] = t.IsText
                            ? Shape(t.Slot, entry.Value.Number.ToString("G", CultureInfo.InvariantCulture))
                            : entry.Value.Number * t.Scale + t.Bias;
                    break;

                case SS.UiValueType.Map when entry.Value.Map != null:
                {
                    // Each declaration goes to the targets Build paired with its NAME: one per slot
                    // it reaches, so counting them is no pairing, and a declaration the first
                    // payload never carried has none.
                    for (var i = 0; i < entry.Value.Map.Length; i++)
                    {
                        var d = entry.Value.Map[i];
                        var placed = false;
                        double? n = null;
                        foreach (var t in targets)
                        {
                            if (!string.Equals(t.Decl, d.Key, StringComparison.Ordinal)) continue;
                            if (!placed)
                            {
                                placed = true;
                                // resolved once: every target of one declaration shares its % base
                                if (d.Value.Type == SS.UiValueType.Number) n = d.Value.Number;
                                else if (DomSlots.Length(d.Value.String, t.PercentOf) is { } len) n = len;
                                else if (DomSlots.LooksLikeLength(d.Value.String))
                                    return Refuse(key, d.Key, d.Value.String, "is not a px or % length", proving);
                            }
                            (values ??= New())[t.Slot] = n is { } v ? v * t.Scale + t.Bias : (object)(d.Value.String ?? string.Empty);
                        }
                        if (!placed) return Refuse(key, d.Key, null, "is not a declaration the first payload carried", proving);
                    }
                    break;
                }

                default:
                    return Refuse(key, null, null, "carries a value of a kind that has no slot", proving);
            }
        }
        return values;
    }

    /// <summary>
    /// Drops the payload, naming what could not be placed - once. After the first the reason
    /// stands and nothing is built per tick; a proof's refusal is Prove's to report.
    /// </summary>
    private Dictionary<string, object>? Refuse(string key, string? decl, string? value, string what, bool proving)
    {
        if (Refused == null)
        {
            Refused = $"\"{key}\"{(decl != null ? "." + decl : string.Empty)}{(value != null ? " = \"" + value + "\"" : string.Empty)} {what}";
            if (!proving)
                ScriptedScreensHtmlPlugin.Log?.LogWarning(
                    $"html: a data payload is dropped, as is any later one like it - {Refused}. A data page's keys, declarations and units are fixed by its first payload.");
        }
        return null;
    }

    /// <summary>
    /// Checks this against what the emitter actually produced, and turns the fast path on only if
    /// they agree.
    /// </summary>
    /// <remarks>
    /// The comparison is the whole safety argument. Everything else here is a rule about what
    /// <i>should</i> land where; this is the one place that looks at what did.
    /// </remarks>
    internal void Prove(IReadOnlyList<KeyValuePair<string, SS.UiValue>> entries,
                        IReadOnlyDictionary<string, SceneSlots.Value> emitted,
                        Action<string> warn)
    {
        if (Problem != null || Proven) return;

        var mine = Apply(entries, proving: true);
        if (mine == null) { Problem = Refused ?? "the payload placed nothing to check"; Refused = null; return; }

        foreach (var pair in mine)
        {
            if (!emitted.TryGetValue(pair.Key, out var was))
            {
                Problem = $"slot \"{pair.Key}\" is not in the emitted scene";
                break;
            }
            if (pair.Value is string s)
            {
                // A text slot the emitter monospaces differs only by the wrapping, and the wrapping
                // is learnable from this very comparison.
                if (!was.IsNumber && was.Text != null && LearnMspace(pair.Key, s, was.Text)) continue;
                if (was.IsNumber || !string.Equals(was.Text, s, StringComparison.Ordinal))
                {
                    Problem = $"slot \"{pair.Key}\": the emitter drew \"{(was.IsNumber ? was.Number.ToString(CultureInfo.InvariantCulture) : was.Text)}\", this would write \"{s}\"";
                    break;
                }
            }
            else if (pair.Value is double d)
            {
                if (!was.IsNumber || Math.Abs(was.Number - d) > 0.01)
                {
                    Problem = $"slot \"{pair.Key}\": the emitter drew {(was.IsNumber ? was.Number.ToString(CultureInfo.InvariantCulture) : was.Text)}, this would write {d.ToString("0.##", CultureInfo.InvariantCulture)}";
                    break;
                }
            }
        }

        if (Problem != null)
        {
            warn($"html: data slots disagree with the emitter and stay off - {Problem}");
            return;
        }
        Proven = true;
    }

    /// <summary>
    /// Writes each bool's current state over a scene the full path just emitted.
    /// </summary>
    /// <remarks>
    /// Once a bool is a toggle its element stays in the layout, so a full-path emit draws it shown
    /// with its siblings where the shown layout puts them - the proof tick, and any re-emit after
    /// it. This puts the actual state back before the scene is sent. Whole states only: with a slot
    /// missing the structure is not the one the state was captured from, and half a state (hidden
    /// but the gap still open) is worse than the shown frame the proof will have flagged.
    /// </remarks>
    internal void Overlay(Dictionary<string, SceneSlots.Value> scene)
    {
        // on the page thread, against a state Apply writes on the game thread
        lock (_shown)
            foreach (var pair in _toggles)
            {
                var state = _shown.TryGetValue(pair.Key, out var shown) && shown ? pair.Value.Shown : pair.Value.Hidden;
                var whole = true;
                foreach (var (slot, _) in state) if (!scene.ContainsKey(slot)) { whole = false; break; }
                if (!whole) continue;
                foreach (var (slot, v) in state) scene[slot] = new SceneSlots.Value((float)v);
            }
    }

    /// <summary>The text as the emitter would have drawn it: digit runs monospaced, when this slot is.</summary>
    private string Shape(string slot, string text)
    {
        if (!_mspace.TryGetValue(slot, out var open) || text.Length == 0) return text;
        _shaped.Clear();
        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsDigit(text[i])) { _shaped.Append(text[i++]); continue; }
            var start = i;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            _shaped.Append(open).Append(text, start, i - start).Append("</mspace>");
        }
        return _shaped.ToString();
    }

    /// <summary>Learns a slot's monospacing from what the emitter drew, when that is the only difference.</summary>
    private bool LearnMspace(string slot, string mine, string emitted)
    {
        const string tag = "<mspace=";
        var at = emitted.IndexOf(tag, StringComparison.Ordinal);
        if (at < 0) return false;
        var close = emitted.IndexOf('>', at);
        if (close < 0) return false;
        var open = emitted.Substring(at, close - at + 1);
        _mspace[slot] = open;
        return string.Equals(Shape(slot, mine), emitted, StringComparison.Ordinal);
    }

    private static Dictionary<string, object> New() => new(StringComparer.Ordinal);
}

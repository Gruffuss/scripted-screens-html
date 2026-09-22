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
        public Target(string slot, double bias, bool isText, double scale = 1)
        { Slot = slot; Bias = bias; IsText = isText; Scale = scale; }
    }

    private readonly Dictionary<string, List<Target>> _byKey = new(StringComparer.Ordinal);

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
    internal static DataSlots Build(IReadOnlyList<KeyValuePair<string, SS.UiValue>> sample,
                                    Func<string, DomSlots.Box?> boxOf,
                                    Func<string, bool> transitioned,
                                    Func<string, bool> isShape,
                                    ICollection<string> available)
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

            // A transition is compiled INTO the scene as an expression, and only the emitter can
            // write it. Taking the fast path on a transitioned element would land the end value
            // immediately and the bars would jump instead of gliding - the one thing a page author
            // notices at once.
            if (transitioned(key)) { map.Problem = $"\"{key}\" has a css transition, which only a re-emit can start"; return map; }

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
                        for (var i = 0; i < mapped.Slots.Length; i++)
                            targets.Add(new Target(mapped.Slots[i], mapped.Bias[i], isText: false, mapped.Scale[i]));
                    }
                    break;
                }

                // `display` is structure, not a value: hiding a box in normal flow closes the gap it
                // left and moves everything after it, which only the layout engine can work out.
                case SS.UiValueType.Bool:
                    map.Problem = $"\"{key}\" is a bool, which shows or hides the element - a layout change, not a value";
                    return map;

                default:
                    map.Problem = $"\"{key}\" carries a {entry.Value.Type}, which has no slot";
                    return map;
            }

            if (targets.Count > 0) map._byKey[key] = targets;
        }

        if (map._byKey.Count == 0) map.Problem = "the payload placed nothing";
        return map;
    }

    /// <summary>What this payload writes, or null when any key is not one this knows.</summary>
    internal Dictionary<string, object>? Apply(IReadOnlyList<KeyValuePair<string, SS.UiValue>> entries)
    {
        if (Problem != null) return null;
        Dictionary<string, object>? values = null;

        foreach (var entry in entries)
        {
            if (!_byKey.TryGetValue(entry.Key ?? string.Empty, out var targets)) return null;

            switch (entry.Value.Type)
            {
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
                    // The declarations and the targets were paired in the same order at Build time,
                    // and a payload whose shape has changed since is not this payload's to guess at.
                    if (entry.Value.Map.Length != targets.Count) return null;
                    for (var i = 0; i < entry.Value.Map.Length; i++)
                    {
                        var d = entry.Value.Map[i];
                        var t = targets[i];
                        if (d.Value.Type == SS.UiValueType.Number)
                            (values ??= New())[t.Slot] = d.Value.Number * t.Scale + t.Bias;
                        else if (Length(d.Value.String) is { } n)
                            (values ??= New())[t.Slot] = n * t.Scale + t.Bias;
                        else
                            (values ??= New())[t.Slot] = d.Value.String ?? string.Empty;
                    }
                    break;
                }

                default:
                    return null;
            }
        }
        return values;
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

        var mine = Apply(entries);
        if (mine == null) { Problem = "the payload placed nothing to check"; return; }

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

    /// <summary>A CSS length as a bare number, or null when it is not one (a colour, a keyword).</summary>
    private static double? Length(string? css)
    {
        if (string.IsNullOrEmpty(css)) return null;
        var i = 0;
        while (i < css!.Length && char.IsWhiteSpace(css[i])) i++;
        var start = i;
        if (i < css.Length && (css[i] == '-' || css[i] == '+')) i++;
        var digits = 0;
        while (i < css.Length && (char.IsDigit(css[i]) || css[i] == '.')) { if (css[i] != '.') digits++; i++; }
        if (digits == 0) return null;
        return double.TryParse(css.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

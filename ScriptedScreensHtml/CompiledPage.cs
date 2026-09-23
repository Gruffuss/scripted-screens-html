using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ScriptedScreensHtml;

/// <summary>
/// A page, compiled: the Lua that runs it, with its DOM writes already resolved to scene slots.
/// </summary>
/// <remarks>
/// This is where the two halves meet. <see cref="JsToLua"/> turns the script into Lua that writes
/// the DOM by name; <see cref="DomSlots"/> knows which scene slot each of those names lands on. The
/// binding table generated here joins them, so the running page writes <c>$legA_y</c> rather than
/// recording <c>"legA.style.top"</c> for somebody else to interpret.
///
/// The table is emitted as data rather than resolved at run time on purpose. Every lookup it would
/// otherwise do - which element, which property, which slot, what arithmetic - is a compile-time
/// fact, and the whole point of compiling is that none of it is paid for again per frame.
///
/// What the chunk ends up being, in order: the prelude, this table, the page's own translated code,
/// and a flush that hands the accumulated values to the vector element. Nothing of this mod is in
/// that list.
/// </remarks>
internal static class CompiledPage
{
    /// <summary>One DOM write and the slots it resolves to.</summary>
    internal readonly struct Binding
    {
        public readonly string Key;          // "legA.style.top"
        public readonly string[] Slots;      // legA_y, bootA_y
        public readonly double[] Bias;       // 85, 105
        /// <summary>Per slot, what the value is multiplied by before the bias: -1 for a far-edge position, else 1.</summary>
        public readonly double[] Scale;
        /// <summary>
        /// How to read the value the page wrote. A script does not write numbers: it writes CSS, so
        /// a height arrives as <c>"18px"</c> and a transform as <c>"translate(90px,88.7px)"</c>.
        /// Coercing those with tonumber gives nil and the write vanishes - which is exactly what
        /// happened the first time this ran end to end.
        /// </summary>
        public readonly Kind Read;
        /// <summary>For a <see cref="Kind.State"/> binding: what each reachable class name draws.</summary>
        public readonly IReadOnlyList<StateValues>? States;
        /// <summary>A state binding's state for a value none of its states is named after.</summary>
        public readonly string? Other;
        /// <summary>A colour binding's values as the scene's hex: the page writes CSS, `var(--live)`.</summary>
        public readonly IReadOnlyDictionary<string, string>? Colours;
        /// <summary>
        /// A <see cref="Kind.Label"/>'s text, in order: literal text as a string, a value as the
        /// 1-based position of its argument, and a colour value as that position with its table.
        /// </summary>
        public readonly IReadOnlyList<object>? Pieces;
        /// <summary>`upper` or `lower`: a label's text-transform, which the values in it follow too.</summary>
        public readonly string? Transform;
        /// <summary>
        /// A label the scene can print itself (see <see cref="Place"/>): per argument, the slot its
        /// placeholder reads, and the printf format of an argument that arrives as a number (null for text).
        /// </summary>
        public readonly string[]? Placed;
        public readonly string?[]? Formats;

        public Binding(string key, string[] slots, double[] bias, Kind read, IReadOnlyList<StateValues>? states = null, double[]? scale = null,
                       string? other = null, IReadOnlyDictionary<string, string>? colours = null,
                       IReadOnlyList<object>? pieces = null, string? transform = null,
                       string[]? placed = null, string?[]? formats = null)
        {
            Key = key; Slots = slots; Bias = bias; Read = read; States = states;
            if (scale == null) { scale = new double[slots.Length]; for (var i = 0; i < scale.Length; i++) scale[i] = 1; }
            Scale = scale;
            Other = other; Colours = colours; Pieces = pieces; Transform = transform;
            Placed = placed; Formats = formats;
        }
    }

    /// <summary>The shape of the CSS value a binding reads.</summary>
    internal enum Kind
    {
        /// <summary>A length: `18px`, `0`, `1.5`. One number for one slot.</summary>
        Length,
        /// <summary>`translate(x, y)`: two numbers, for the group's translate pair.</summary>
        Translate,
        /// <summary>A string, written to a text slot as it stands.</summary>
        Text,
        /// <summary>A colour, which the renderer takes as text and never eases.</summary>
        Colour,
        /// <summary>`hidden`/`collapse` or `visible`: a word that becomes 0 or 1 on the group's opacity.</summary>
        Visibility,
        /// <summary>
        /// A class name: not a value but a <b>state</b>. Every class the script can assign is
        /// enumerated at compile time, the page is laid out in each, and the slot values for each
        /// are emitted; the running page then picks one by name. That is the same mechanism a theme
        /// switch needs, arrived at from the other end.
        /// </summary>
        State,
        /// <summary>
        /// Text built from literal pieces and values - `set 101.3 kPa` - written whole when the last
        /// of its values is known, since one text slot is the whole label.
        /// </summary>
        Label,
    }

    /// <summary>The slot values one class state produces, emitted for every state the script can reach.</summary>
    internal sealed class StateValues
    {
        public string Name = string.Empty;                       // "duck", "duck hurt", ""
        public readonly List<(string Slot, double Value)> Numbers = new();
        public readonly List<(string Slot, string Value)> Text = new();
    }

    /// <summary>
    /// Fills every state out to the same set of slots, so leaving a state restores what it moved.
    /// </summary>
    /// <remarks>
    /// A slot a state does not mention takes the value the scene was emitted with - the page's
    /// resting geometry - which is exactly right: a state that does not move a box is a state in
    /// which that box sits where the markup put it.
    ///
    /// A slot with no base value is dropped from EVERY state, not only from the ones that omit it.
    /// Keeping it in some would make those states one-way - enterable and not leavable - which is
    /// the bug this whole function exists to remove, reintroduced for a subset.
    /// </remarks>
    private static void Complete(List<StateValues> states, Func<string, double?>? baseOf, Func<string, string?>? textOf = null)
    {
        // Text and colour the same way: a state that turns a box red has to be leavable too.
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in states)
            foreach (var (slot, _) in s.Text) texts.Add(slot);
        foreach (var s in states)
        {
            s.Text.RemoveAll(t => textOf?.Invoke(t.Slot) == null);
            foreach (var slot in texts)
                if (!s.Text.Exists(t => t.Slot == slot) && textOf?.Invoke(slot) is { } rest)
                    s.Text.Add((slot, rest));
        }

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in states)
            foreach (var (slot, _) in s.Numbers) union.Add(slot);
        if (union.Count == 0) return;

        var unrestorable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in union)
            if (baseOf?.Invoke(slot) == null) unrestorable.Add(slot);

        foreach (var s in states)
        {
            var has = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (slot, _) in s.Numbers) has.Add(slot);
            s.Numbers.RemoveAll(n => unrestorable.Contains(n.Slot));
            foreach (var slot in union)
                if (!has.Contains(slot) && !unrestorable.Contains(slot) && baseOf!(slot) is { } at)
                    s.Numbers.Add((slot, at));
        }
    }

    /// <summary>
    /// Each value laid out and drawn, completed so every one can be left. Null when any cannot be
    /// drawn, with those named in <paramref name="missing"/>.
    /// </summary>
    private static List<StateValues>? Drawn(IEnumerable<string> values, Func<string, StateValues?> draw, List<string> missing,
                                            Func<string, double?>? baseOf, Func<string, string?>? textOf)
    {
        var states = new List<StateValues>();
        foreach (var value in values.Distinct(StringComparer.Ordinal))
        {
            var produced = draw(value);
            if (produced == null) { missing.Add(value.Length == 0 ? "(none)" : value); continue; }
            produced.Name = value;
            states.Add(produced);
        }
        if (missing.Count > 0) return null;
        // Every state carries a value for every slot ANY state moves, not just the ones it moves
        // itself. States used to be deltas, so the base state's entry came out empty and going
        // duck -> "" wrote nothing: the page kept the ducked geometry for ever. A state is a
        // description of what the page looks like, not of what changed to get there.
        Complete(states, baseOf, textOf);
        return states;
    }

    /// <summary>
    /// The page's laid-out structure, as the chunk needs to see it.
    /// </summary>
    /// <remarks>
    /// A compiled page has no DOM and no layout engine, so everything a script asks ABOUT the page -
    /// its tags, its classes, where its boxes are - has to have been measured at compile time or be
    /// answered wrongly. Answered wrongly is the failure this project keeps recording, and
    /// `classList.contains` returning false for a class written in the markup is exactly it.
    /// </remarks>
    internal sealed class Tree
    {
        /// <summary>Every named element, in document order, for the document-wide queries.</summary>
        public readonly List<string> Order = new();
        public readonly Dictionary<string, string> Tag = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Class = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Box> Boxes = new(StringComparer.Ordinal);

        /// <summary>One element's geometry: border box, content size, and offset from its parent.</summary>
        internal readonly struct Box
        {
            public readonly double X, Y, W, H, ContentW, ContentH, OffsetX, OffsetY;
            public Box(double x, double y, double w, double h,
                       double contentW, double contentH, double offsetX, double offsetY)
            {
                X = x; Y = y; W = w; H = h;
                ContentW = contentW; ContentH = contentH; OffsetX = offsetX; OffsetY = offsetY;
            }
        }
    }

    /// <summary>What compiling produced, or why it could not.</summary>
    internal sealed class Result
    {
        public string? Lua;
        public readonly List<Binding> Bindings = new();
        /// <summary>
        /// Writes that have no slot, each with the reason. The page still compiles and each is said
        /// once, by name, when it starts: that write alone is dropped. Refusing the page for it put
        /// the WHOLE page back on the interpreter - a relayout every tick - to keep one value moving.
        /// </summary>
        public readonly List<string> Unmapped = new();
        public readonly List<string> Problems = new();
        /// <summary>
        /// Slots the scene carries as expressions of <c>t</c> rather than as values: motion the page
        /// wrote per frame that <see cref="Motion"/> proved is a closed form of time. The chip never
        /// computes these and never sends them - the renderer evaluates them on its own worker, which
        /// is the difference between a page that costs something every frame and one that does not.
        /// </summary>
        public readonly Dictionary<string, string> Expressions = new(StringComparer.Ordinal);
        /// <summary>
        /// What compiled but draws less than a browser would - an animation the scene cannot run, a
        /// value that needs the layout engine. Said at compile time, once, by name; the page still
        /// compiles, because falling back to running it every tick is the cost compiling removes.
        /// </summary>
        public readonly List<string> Warnings = new();
        /// <summary>
        /// The scene the page opens with when its markup was compiled: every alternative it can draw,
        /// each gated, with what it opens in. Null when there was no markup to compile, and the
        /// scene the page last drew is the structure.
        /// </summary>
        public string? Structure;
        public Dictionary<string, SceneSlots.Value>? StructureValues;
        /// <summary>Labels the chunk writes as placeholders: the label's slot, and the text its `T` gets instead.</summary>
        public readonly List<(string Slot, string Text)> Placements = new();
        /// <summary>Whether <see cref="Place"/> has rewritten <see cref="Structure"/>, so a second call does nothing.</summary>
        public bool Placed;
        /// <summary>
        /// The Lua is a plain program (<see cref="PlainPage"/>): it makes the page's own vector elements
        /// and runs on the chip's <c>tick</c>, so once installed nothing of this mod drives it.
        /// </summary>
        public bool Plain;
        public bool Ok => Lua != null && Problems.Count == 0;
    }

    /// <summary>
    /// Compiles a page.
    /// </summary>
    /// <param name="script">The page's script.</param>
    /// <param name="available">Slot names the emitted scene exposes.</param>
    /// <param name="boxOf">Where an element sits, for the safety question. Null for an unknown element.</param>
    /// <param name="tabular">Whether an element's text is monospaced by digit run (font-variant-numeric).</param>
    /// <param name="stateOf">
    /// What the page draws with a given class on a given element: the caller sets the class, lays
    /// the page out and reads back the slots that moved. Null when the state cannot be produced.
    /// </param>
    /// <param name="prelude">
    /// The runtime the compiled page sits on. It goes at the FRONT of the chunk: everything the
    /// translated page and the binding runtime use - js_str, DOM, Pending - is defined there, so
    /// without it the chunk dies on its first line and defines nothing.
    /// </param>
    /// <param name="viewport">
    /// The page's design size. A page lays itself out from <c>window.innerHeight</c> at setup, and
    /// the prelude's stub is zero - so every console compiled for the default 800 and the artwork
    /// sat at the wrong height on any console that is not that tall.
    /// </param>
    /// <param name="element">The vector element's name in Lua, which the flush writes to.</param>
    /// <param name="styleOf">
    /// What the page draws with one inline style declaration (element, CSS property, value) added:
    /// <paramref name="stateOf"/> for a property with no slot of its own.
    /// </param>
    internal static Result Compile(string script, ICollection<string> available,
                                   Func<string, DomSlots.Box?> boxOf,
                                   Func<string, bool>? tabular = null,
                                   Func<string, string, StateValues?>? stateOf = null,
                                   string? prelude = null,
                                   (double Width, double Height)? viewport = null,
                                   IReadOnlyDictionary<string, string>? parents = null,
                                   Tree? tree = null,
                                   IReadOnlyDictionary<string, JsToLua.MarkupPlan>? markup = null,
                                   IReadOnlyList<Binding>? markupBindings = null,
                                   Func<string, double?>? baseOf = null,
                                   Func<string, string?>? textOf = null,
                                   (string Surface, string Element, string Scene)? target = null,
                                   string element = "VDATA",
                                   Func<string, string, string, StateValues?>? styleOf = null)
    {
        var result = new Result();

        // Before the translation, which writes a placed label's toFixed pieces as numbers.
        var placeable = Placeable(markup, markupBindings, available);
        var lua = JsToLua.Compile(script, out var problems, markup);
        foreach (var p in problems) result.Problems.Add(p);
        if (lua == null) return result;

        var (writes, notes) = DomWrites.Of(script);
        foreach (var n in notes) result.Problems.Add(n);

        // Motion first, because a write it can express is not a binding at all. Anything left here is
        // genuinely data - it depends on an event, on state, or on something outside the page - and
        // that is the only kind of number the chip should ever send.
        var (motion, _) = Motion.Of(script);
        var expressed = new Dictionary<string, Motion.Found>(StringComparer.Ordinal);
        foreach (var f in motion) expressed[f.Id + "." + f.Property] = f;

        // One binding per distinct (element, property) a running page writes. Setup writes are
        // already in the geometry by the time this runs, so they are not bound to anything.
        // Every class string a key can take, across every write to it. A page assigns className from
        // several places - draw(), over(), reset() - and each knows only part of the set, so taking
        // the first write's list alone lost three of the player's four states.
        var classes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var computed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in writes)
        {
            if (!w.Runtime || w.Property != "className" && !w.Property.StartsWith("style.", StringComparison.Ordinal)) continue;
            foreach (var id in Targets(w, available))
            {
                var name = id + "." + w.Property;
                // One unknowable write and the set is not a set. A flag rather than a marker value
                // in the list, because a class really could be called anything.
                if (w.Classes == null) { computed.Add(name); continue; }
                if (!classes.TryGetValue(name, out var list)) classes[name] = list = new List<string>();
                foreach (var c in w.Classes) if (!list.Contains(c)) list.Add(c);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in writes)
        {
            if (!w.Runtime) continue;
            foreach (var id in Targets(w, available))
            {
                var key = id + "." + w.Property;
                if (!seen.Add(key)) continue;
                // Markup the compiler turned into structure: the plan's own bindings write it.
                if (w.Property == "innerHTML" && markup != null && markup.Values.Any(p => p.Id == id)) continue;

                var box = boxOf(id);
                if (box == null) { result.Unmapped.Add($"line {w.Line}: \"{id}\" is not an element of this page"); continue; }

                // A class name is a state, not a value: every one the script can assign is laid out
                // here and what it draws is emitted, so the running page picks rather than computes.
                if (w.Property == "className")
                {
                    var reachable = computed.Contains(key) ? null
                                  : classes.TryGetValue(key, out var all) ? all : w.Classes;
                    if (reachable == null)
                    {
                        result.Unmapped.Add($"line {w.Line}: {key} - the class is computed, so its states cannot be enumerated");
                        continue;
                    }
                    if (stateOf == null)
                    {
                        result.Unmapped.Add($"line {w.Line}: {key} - no way to lay the page out in each state");
                        continue;
                    }
                    var missing = new List<string>();
                    if (Drawn(reachable, cls => stateOf(id, cls), missing, baseOf, textOf) is { } states)
                        result.Bindings.Add(new Binding(key, Array.Empty<string>(), Array.Empty<double>(), Kind.State, states));
                    else
                        result.Unmapped.Add($"line {w.Line}: {key} - cannot draw the state(s) {string.Join(", ", missing)}");
                    continue;
                }

                var mapped = DomSlots.Map(id, w.Property, box.Value, available);
                if (!mapped.Mapped)
                {
                    // No slot for the property itself, but the script only ever assigns a few values:
                    // then it is a state like a class, and the page is laid out in each. That is
                    // `color` on a wrapper whose text is its children's - the cascade carries it down
                    // exactly as a browser would, which no slot on the wrapper could. A property that
                    // draws the same in every value it takes (an animation the scene cannot express)
                    // moves nothing, and is reported rather than bound to nothing.
                    if (styleOf != null && w.Property.StartsWith("style.", StringComparison.Ordinal)
                        && !computed.Contains(key) && classes.TryGetValue(key, out var values)
                        && Drawn(values, v => styleOf(id, DomSlots.Dashed(w.Property.Substring(6)), v), new List<string>(), baseOf, textOf) is { } drawn
                        && drawn.Any(s => s.Numbers.Count + s.Text.Count > 0))
                        result.Bindings.Add(new Binding(key, Array.Empty<string>(), Array.Empty<double>(), Kind.State, drawn));
                    else
                        result.Unmapped.Add($"line {w.Line}: {key} - {mapped.Problem}");
                    continue;
                }

                // Motion goes into the scene instead of into a binding. Every slot it covers is one
                // the chip never writes, so a page whose motion is wholly expressible sends nothing.
                if (expressed.TryGetValue(key, out var found)
                    && Express(found, mapped, Reading(w.Property), result.Expressions))
                    continue;

                result.Bindings.Add(new Binding(key, mapped.Slots, mapped.Bias, Reading(w.Property), scale: mapped.Scale));
            }
        }

        // Compiled markup's values and states, keyed `innerHTML#3`, `label#2`, `drive`, `choice#4` and
        // `rows#1` under the element whose markup it is, so they sit beside its other writes.
        if (markupBindings != null)
            foreach (var b in markupBindings)
                result.Bindings.Add(placeable.TryGetValue(b.Key, out var p) ? Placeholders(b, p.Plan, p.Holes, result) : b);

        result.Lua = Assemble(lua, result.Bindings, tabular ?? (_ => false), prelude, viewport, parents, tree, target, element);
        return result;
    }

    /// <summary>
    /// The compiled labels the scene can print itself: one `T` whose text is the label's literal
    /// pieces around a `{$slot:%.1f}` per value, so the chip sends the values and never builds the
    /// string. Marked in the plan before translation, because it changes what the pieces are passed as.
    /// </summary>
    /// <remarks>
    /// Skipped for a label whose literal text the scene source cannot carry: a double quote ends the
    /// value, a `{$` would read as a placeholder, a leading `=` as an expression - and for one whose
    /// placeholder names are already taken. Those stay one text slot, built on the chip, as before.
    /// </remarks>
    private static Dictionary<string, (JsToLua.MarkupPlan Plan, List<int> Holes)> Placeable(
        IReadOnlyDictionary<string, JsToLua.MarkupPlan>? markup, IReadOnlyList<Binding>? bindings, ICollection<string> available)
    {
        var found = new Dictionary<string, (JsToLua.MarkupPlan, List<int>)>(StringComparer.Ordinal);
        if (markup == null || bindings == null) return found;
        // A text slot a state also writes stays one slot: the state's text would land on a `T` that
        // no longer reads it.
        var stated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in bindings)
            if (b.States != null)
                foreach (var s in b.States)
                    foreach (var (slot, _) in s.Text) stated.Add(slot);
        foreach (var b in bindings)
        {
            if (b.Read != Kind.Label || b.Pieces == null || b.Slots.Length != 1 || stated.Contains(b.Slots[0])) continue;
            var dot = b.Key.IndexOf('.');
            if (dot <= 0) continue;
            var id = b.Key.Substring(0, dot);
            var key = b.Key.Substring(dot + 1);
            var text = new StringBuilder();
            foreach (var piece in b.Pieces) if (piece is string lit) text.Append(lit);
            var literal = text.ToString();
            if (literal.IndexOf('"') >= 0 || literal.IndexOf('\n') >= 0 || literal.Contains("{$", StringComparison.Ordinal)
                || b.Pieces[0] is string first && first.StartsWith("=", StringComparison.Ordinal))
                continue;
            foreach (var plan in markup.Values)
            {
                if (plan.Id != id) continue;
                var at = plan.Labels.FindIndex(l => l.Key == key);
                if (at < 0) continue;
                var holes = plan.Labels[at].Holes;
                var free = true;
                for (var k = 1; k <= holes.Count; k++) free &= !available.Contains(PieceSlot(b.Slots[0], k));
                if (free)
                {
                    plan.Placed.Add(key);
                    found[b.Key] = (plan, holes);
                }
                break;
            }
        }
        return found;
    }

    /// <summary>The slot a placed label's k-th value is written to.</summary>
    private static string PieceSlot(string label, int k) => label + "_p" + k.ToString(CultureInfo.InvariantCulture);

    /// <summary>A placeable label as the chunk writes it, with the text its `T` gets recorded for <see cref="Place"/>.</summary>
    private static Binding Placeholders(Binding b, JsToLua.MarkupPlan plan, List<int> holes, Result result)
    {
        var slots = new string[holes.Count];
        var formats = new string?[holes.Count];
        for (var k = 0; k < holes.Count; k++)
        {
            slots[k] = PieceSlot(b.Slots[0], k + 1);
            if (plan.Fixed.TryGetValue(holes[k], out var digits)) formats[k] = "%." + digits.ToString(CultureInfo.InvariantCulture) + "f";
        }
        var text = new StringBuilder();
        foreach (var piece in b.Pieces!)
        {
            var arg = piece switch { int a => a, ValueTuple<int, IReadOnlyDictionary<string, string>> c => c.Item1, _ => 0 };
            if (arg == 0) { text.Append((string)piece); continue; }
            text.Append("{$").Append(slots[arg - 1]);
            if (formats[arg - 1] is { } format) text.Append(':').Append(format);
            text.Append('}');
        }
        result.Placements.Add((b.Slots[0], text.ToString()));
        return new Binding(b.Key, b.Slots, b.Bias, Kind.Label, pieces: b.Pieces, transform: b.Transform, placed: slots, formats: formats);
    }

    /// <summary>
    /// Gives every placeable label's `T` its placeholder text, in place of the one text slot it had,
    /// and switches the chunk to writing the values instead of the text.
    /// </summary>
    /// <remarks>
    /// Run by whoever set <see cref="Result.Structure"/>, before it is sent; a chunk whose scene was
    /// never placed keeps building its labels whole, so skipping this is slower but never wrong. It
    /// does nothing a second time. When any label's `T` cannot be found nothing is placed, and that
    /// is said: the chunk switches every label at once, and a placed label with no `T` would be
    /// written and drawn nowhere.
    /// </remarks>
    /// <summary>
    /// Writes back, as literal numbers, every slot inside an expression that nothing on the chip ever
    /// writes, and drops those slots from the opening values.
    /// </summary>
    /// <remarks>
    /// Splitting the scene into structure and values turns EVERY number into a slot, the constants
    /// inside a keyframe loop included (<c>o="=...mod(max($L101_o_0,t-($L101_o_1)),...)"</c>). The chip
    /// never writes those: they are positional leftovers, fixed for the life of the structure. But a
    /// scene cannot tell a slot nobody writes from live data, so every such animation read as
    /// data-driven, and the renderer's no-rebuild path for a group whose opacity is a function of
    /// time and constants alone refused all of them - each blinking status dot kept its console
    /// rebuilding every frame. Idempotent: a second call finds nothing left to inline.
    /// </remarks>
    internal static void InlineConstants(Result r)
    {
        if (r.Structure == null || r.StructureValues == null) return;
        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in r.Bindings)
        {
            foreach (var s in b.Slots) written.Add(s);
            if (b.States != null)
                foreach (var st in b.States)
                {
                    foreach (var (slot, _) in st.Numbers) written.Add(slot);
                    foreach (var (slot, _) in st.Text) written.Add(slot);
                }
        }
        foreach (var slot in r.Expressions.Keys) written.Add(slot);
        foreach (var (slot, _) in r.Placements) written.Add(slot);
        r.Structure = Inline(r.Structure, r.StructureValues, written);
    }

    /// <summary>
    /// The pass <see cref="InlineConstants"/> runs, on the scene text: inside every <c>"=..."</c>
    /// expression a <c>$name</c> that is not in <paramref name="written"/> and has a number as its
    /// opening value becomes that number. A name followed by <c>[</c> is an array and stays; a name
    /// no longer referenced anywhere is removed from <paramref name="values"/>, so it is not sent.
    /// A label's placeholder is written by the chip under its label's name plus <c>_p</c>, so any
    /// name starting with a written name and <c>_p</c> counts as written too.
    /// </remarks>
    internal static string Inline(string structure, Dictionary<string, SceneSlots.Value> values, ICollection<string> written)
    {
        var sb = new StringBuilder(structure.Length);
        var inlined = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        while (i < structure.Length)
        {
            var open = structure.IndexOf("\"=", i, StringComparison.Ordinal);
            if (open < 0) { sb.Append(structure, i, structure.Length - i); break; }
            var close = structure.IndexOf('"', open + 2);
            if (close < 0) { sb.Append(structure, i, structure.Length - i); break; }
            sb.Append(structure, i, open + 2 - i);
            var k = open + 2;
            while (k < close)
            {
                var c = structure[k];
                if (c != '$') { sb.Append(c); k++; continue; }
                var e = k + 1;
                while (e < close && (char.IsLetterOrDigit(structure[e]) || structure[e] == '_')) e++;
                var name = structure.Substring(k + 1, e - k - 1);
                var array = e < close && structure[e] == '[';
                if (name.Length > 0 && !array && !Written(name, written)
                    && values.TryGetValue(name, out var v) && v.IsNumber)
                {
                    var n = v.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append(v.Number < 0f ? "(" + n + ")" : n);
                    inlined.Add(name);
                }
                else sb.Append(structure, k, e - k);
                k = e;
            }
            i = close;
        }
        var result = sb.ToString();
        foreach (var name in inlined)
            if (!Referenced(result, name)) values.Remove(name);
        return result;

        static bool Written(string name, ICollection<string> written)
        {
            if (written.Contains(name)) return true;
            var p = name.LastIndexOf("_p", StringComparison.Ordinal);
            return p > 0 && written.Contains(name.Substring(0, p));
        }

        static bool Referenced(string text, string name)
        {
            for (var at = text.IndexOf("$" + name, StringComparison.Ordinal); at >= 0; at = text.IndexOf("$" + name, at + 1, StringComparison.Ordinal))
            {
                var end = at + 1 + name.Length;
                if (end >= text.Length || !(char.IsLetterOrDigit(text[end]) || text[end] == '_')) return true;
            }
            return false;
        }
    }

    internal static void Place(Result r)
    {
        if (r.Placed || r.Structure == null || r.Placements.Count == 0) return;
        r.Placed = true;
        foreach (var (slot, _) in r.Placements)
            if (!r.Structure.Contains(" text=\"$" + slot + "\"", StringComparison.Ordinal))
            {
                r.Warnings.Add($"the label \"{slot}\" has no text in the scene to print into, so every label is built on the chip");
                return;
            }
        var sb = new StringBuilder(r.Structure);
        foreach (var (slot, text) in r.Placements)
            sb.Replace(" text=\"$" + slot + "\"", " text=\"" + text + "\"");
        r.Structure = sb.ToString();
        // And the chunk prints no label itself from now on: the two change together or not at all.
        if (r.Lua != null) r.Lua = r.Lua.Replace("LABELS_PLACED = false -- placed: no", "LABELS_PLACED = true -- placed: yes");
    }

    /// <summary>
    /// Puts one write's motion into the scene, as an expression per slot. False when it cannot go
    /// there, and the write stays a value the chip sends.
    /// </summary>
    /// <remarks>
    /// Three things have to line up, and each failure is ordinary rather than a bug.
    ///
    /// <b>The slots must exist.</b> <see cref="DomSlots"/> answers where a write WOULD land, and for a
    /// transform that is the wrapping group's translate - which only has an addressable name when the
    /// emitter gave that group an id. A page whose group is anonymous keeps its binding.
    ///
    /// <b>Every part must be expressible.</b> A transform carries two numbers and the scene writes
    /// them as one pair, so half of it cannot be an expression while the other half is a slot. Either
    /// both axes are closed forms or neither moves into the scene.
    ///
    /// <b>The bias comes along.</b> The scene is in absolute coordinates and CSS is not, so a slot
    /// that a value would have reached as <c>v + bias</c> reaches the scene as <c>(expr) + bias</c> -
    /// and one write may drive several slots, each with a bias of its own.
    /// </remarks>
    private static bool Express(Motion.Found found, DomSlots.Result mapped, Kind read,
                                Dictionary<string, string> into)
    {
        // Only geometry and opacity. Text is drawn from a string and a colour is not a scalar, so
        // neither has anything the expression language could evaluate.
        if (read is not (Kind.Length or Kind.Translate)) return false;
        if (mapped.NeedsGroup || mapped.Slots.Length == 0) return false;

        if (read == Kind.Translate)
        {
            if (found.Parts.Length != 2 || mapped.Slots.Length != 2) return false;
            // Only the axes this write sets have to be expressible. A `translateX` says nothing
            // about y, so y keeps whatever the scene emitted and the write still leaves entirely.
            for (var i = 0; i < 2; i++)
                if (found.Touches[i] && found.Parts[i] == null) return false;
            for (var i = 0; i < 2; i++)
                if (found.Parts[i] != null) into[mapped.Slots[i]] = Shift(found.Parts[i]!, mapped.Bias[i], mapped.Scale[i]);
            return true;
        }

        if (found.Parts.Length != 1 || found.Parts[0] == null) return false;
        for (var i = 0; i < mapped.Slots.Length; i++) into[mapped.Slots[i]] = Shift(found.Parts[0]!, mapped.Bias[i], mapped.Scale[i]);
        return true;
    }

    /// <summary>An expression moved into absolute scene coordinates: <c>bias - expr</c> for a far edge.</summary>
    private static string Shift(string expr, double bias, double scale = 1)
        => scale < 0 ? Num(bias) + "-(" + expr + ")"
         : bias == 0 ? expr : "(" + expr + ")+" + Num(bias);

    /// <summary>What shape of CSS value a property carries.</summary>
    private static Kind Reading(string property) => property switch
    {
        "className" => Kind.State,
        "textContent" or "innerText" => Kind.Text,
        "style.transform" => Kind.Translate,
        "style.visibility" => Kind.Visibility,
        "style.color" or "style.background" or "style.backgroundColor" or "style.background-color" => Kind.Colour,
        _ => Kind.Length,
    };

    /// <summary>
    /// The element ids one write reaches. Usually one; a family written through a computed id -
    /// <c>$('pb' + i)</c> - reaches every member, and they are real elements resolved here rather
    /// than looked up while the page runs.
    /// </summary>
    private static IEnumerable<string> Targets(DomWrites.Write w, ICollection<string> available)
    {
        if (w.Id != null) { yield return w.Id; yield break; }
        if (w.Prefix is not { Length: >= 2 } prefix) yield break;

        // a slot name is `<id>_<key>` or the bare id, so the family's members are discoverable
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in available)
        {
            if (!slot.StartsWith(prefix, StringComparison.Ordinal)) continue;

            // The member is the prefix plus a number and nothing else. `'ob' + i` makes ob0..ob3,
            // and without the number test it also matched `obstacles` - the container they live in -
            // so a className write meant for the obstacles was bound to their parent instead.
            var end = prefix.Length;
            while (end < slot.Length && slot[end] >= '0' && slot[end] <= '9') end++;
            if (end == prefix.Length) continue;                       // no index: a different element
            if (end < slot.Length && slot[end] != '_') continue;      // `obstacles` rather than `ob3`
            found.Add(slot.Substring(0, end));
        }
        foreach (var id in found) yield return id;
    }

    // ---- the chunk --------------------------------------------------------------------------------

    private static string Assemble(string page, List<Binding> bindings, Func<string, bool> tabular,
                                   string? prelude, (double Width, double Height)? viewport,
                                   IReadOnlyDictionary<string, string>? parents,
                                   Tree? tree,
                                   (string Surface, string Element, string Scene)? target, string element)
    {
        var sb = new StringBuilder(page.Length + (prelude?.Length ?? 0) + bindings.Count * 64 + 2048);

        sb.Append("-- Compiled page. The mod that produced this is not running.\n")
          .Append("-- Every lookup below was a compile-time fact; none of it is worked out again here.\n\n");

        // First, because everything after it depends on it: js_str, DOM, Pending and the rest
        // are all defined there, and without it the chunk dies on its first line and defines nothing.
        if (prelude != null) sb.Append(prelude).Append("\n\n");

        // The console this page was compiled for. A page reads it at setup to size itself, and
        // the prelude's stub is zero, so without this every console laid out for the fallback 800.
        if (viewport is { } v)
            sb.Append("window.innerWidth, window.innerHeight = ")
              .Append(Num(v.Width)).Append(", ").Append(Num(v.Height)).Append("\n\n");

        // Nested by element, then by property - NOT keyed by "id.property". A flat table reads
        // better and costs a string concatenation on every write, twenty-six a frame per console,
        // to rebuild a key the compiler already had. Two table lookups allocate nothing, and
        // "allocates nothing per frame" is the entire point of compiling.
        // Grouped, not run-length encoded. An element's bindings are NOT consecutive in the list -
        // `player.className` and `player.style.transform` are found at different points in the
        // script - so opening a group per run emitted the same element twice, and a Lua table keeps
        // only the last of two identical keys. Every binding in the first group vanished.
        var byOwner = new Dictionary<string, List<Binding>>(StringComparer.Ordinal);
        var ownerOrder = new List<string>();
        foreach (var b in bindings)
        {
            var at = b.Key.IndexOf('.');
            var who = at > 0 ? b.Key.Substring(0, at) : b.Key;
            if (!byOwner.TryGetValue(who, out var forOwner)) { byOwner[who] = forOwner = new List<Binding>(); ownerOrder.Add(who); }
            forOwner.Add(b);
        }

        sb.Append("BOUND = {\n");
        foreach (var owner in ownerOrder)
        {
            sb.Append("  [").Append(Quote(owner)).Append("] = {\n");
            foreach (var b in byOwner[owner])
            {
            var dot = b.Key.IndexOf('.');
            var member = dot > 0 ? b.Key.Substring(dot + 1) : string.Empty;
            sb.Append("    [").Append(Quote(member)).Append("] = { read = ").Append(Quote(b.Read.ToString().ToLowerInvariant()));

            // A state binding carries what each class DRAWS, laid out at compile time, rather than a
            // slot and a number. Picking one at run time is a table lookup and a copy.
            if (b.Other != null) sb.Append(", other = ").Append(Quote(b.Other));
            if (b.Transform != null) sb.Append(", case = ").Append(Quote(b.Transform));
            if (b.Colours != null) sb.Append(", map = ").Append(Map(b.Colours));
            if (b.Pieces != null)
            {
                sb.Append(", pieces = { ");
                foreach (var piece in b.Pieces)
                {
                    switch (piece)
                    {
                        case string text: sb.Append(Quote(text)); break;
                        case int arg: sb.Append(arg.ToString(CultureInfo.InvariantCulture)); break;
                        case ValueTuple<int, IReadOnlyDictionary<string, string>> colour:
                            sb.Append("{ ").Append(colour.Item1.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(Map(colour.Item2)).Append(" }");
                            break;
                    }
                    sb.Append(", ");
                }
                sb.Append('}');
            }
            // A placeable label: the slot each value goes to, the format of each that goes as a
            // number, and the colour table of a value that is a colour.
            if (b.Placed != null && b.Formats != null && b.Pieces != null)
            {
                sb.Append(", ph = { ");
                foreach (var slot in b.Placed) sb.Append(Quote(slot)).Append(", ");
                sb.Append("}, fmt = { ");
                for (var k = 0; k < b.Formats.Length; k++)
                    if (b.Formats[k] is { } format) sb.Append('[').Append((k + 1).ToString(CultureInfo.InvariantCulture)).Append("] = ").Append(Quote(format)).Append(", ");
                sb.Append('}');
                var colours = b.Pieces.OfType<ValueTuple<int, IReadOnlyDictionary<string, string>>>().ToList();
                if (colours.Count > 0)
                {
                    sb.Append(", cmap = { ");
                    foreach (var (arg, map) in colours) sb.Append('[').Append(arg.ToString(CultureInfo.InvariantCulture)).Append("] = ").Append(Map(map)).Append(", ");
                    sb.Append('}');
                }
            }
            if (b.Read == Kind.State && b.States != null)
            {
                sb.Append(", states = {");
                foreach (var state in b.States)
                {
                    sb.Append("\n    [").Append(Quote(state.Name)).Append("] = { ");
                    var first = true;
                    foreach (var (slot, value) in state.Numbers)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append("{ ").Append(Quote(slot)).Append(", ").Append(Num(value)).Append(" }");
                    }
                    foreach (var (slot, text) in state.Text)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append("{ ").Append(Quote(slot)).Append(", ").Append(Quote(text)).Append(" }");
                    }
                    sb.Append(" },");
                }
                sb.Append("\n  } },\n");
                continue;
            }

            sb.Append(", to = { ");
            for (var i = 0; i < b.Slots.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("{ ").Append(Quote(b.Slots[i])).Append(", ").Append(Num(b.Bias[i]));
                // A third entry only where the value is negated - a far-edge position - so the
                // runtime's ordinary write reads `{ slot, bias }` exactly as before.
                if (b.Scale[i] != 1) sb.Append(", ").Append(Num(b.Scale[i]));
                sb.Append(" }");
            }
            sb.Append(" } },\n");
            }
            sb.Append("  },\n");
        }
        sb.Append("}\n\n");

        // Which text slots draw tabular figures. The emitter monospaces each digit run when it writes
        // such a label, and a slot write bypasses the emitter, so without this a counter's digits
        // shift sideways as they change. Nothing else is shaped: a slot value is data, not scene
        // source, so the scene reader's escapes and numeric guard never meet it.
        sb.Append("TEXT = {\n");
        foreach (var b in bindings)
        {
            if (b.Read != Kind.Text) continue;
            var dot = b.Key.IndexOf('.');
            var id = dot > 0 ? b.Key.Substring(0, dot) : b.Key;
            if (tabular(id)) sb.Append("  [").Append(Quote(id)).Append("] = true,\n");
        }
        sb.Append("}\n\n");

        // Who contains whom, so an event can bubble. A page listens on a container and the click
        // lands on whichever child is under the cursor, so without this the common case - the
        // runner's `field.addEventListener('mousedown', jump)` - never fires at all. Static, because
        // the structure a compiled page draws does not change; that is the whole premise.
        sb.Append("PARENT = {\n");
        if (parents != null)
            foreach (var kv in parents)
                sb.Append("  [").Append(Quote(kv.Key)).Append("] = ").Append(Quote(kv.Value)).Append(",\n");
        sb.Append("}\n\n");

        // The page's own markup, which the chunk otherwise cannot see at ALL. Without these, a
        // script asking `classList.contains('wide')` about an element written in the HTML gets
        // FALSE for a class that is plainly there - a wrong answer rather than a missing feature -
        // and `closest`, `matches`, `tagName` and every document-wide query are dead.
        //
        // Emitted as data because it is all a compile-time fact: the structure a compiled page draws
        // does not change, which is the premise of compiling it.
        if (tree is { } t)
        {
            sb.Append("NODES = {");
            for (var i = 0; i < t.Order.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Quote(t.Order[i]));
            }
            sb.Append("}\n\n");

            sb.Append("TAG = {\n");
            foreach (var kv in t.Tag)
                sb.Append("  [").Append(Quote(kv.Key)).Append("] = ").Append(Quote(kv.Value)).Append(",\n");
            sb.Append("}\n\n");

            sb.Append("CLASS = {\n");
            foreach (var kv in t.Class)
                sb.Append("  [").Append(Quote(kv.Key)).Append("] = ").Append(Quote(kv.Value)).Append(",\n");
            sb.Append("}\n\n");

            // Every laid-out box, so getBoundingClientRect and the offset family answer where the
            // element IS rather than an honest zero. A compiled page has no layout engine, but the
            // compiler had one - these are what it measured.
            sb.Append("BOXES = {\n");
            foreach (var kv in t.Boxes)
            {
                var b = kv.Value;
                sb.Append("  [").Append(Quote(kv.Key)).Append("] = {")
                  .Append(Num(b.X)).Append(", ").Append(Num(b.Y)).Append(", ")
                  .Append(Num(b.W)).Append(", ").Append(Num(b.H)).Append(", ")
                  .Append(Num(b.ContentW)).Append(", ").Append(Num(b.ContentH)).Append(", ")
                  .Append(Num(b.OffsetX)).Append(", ").Append(Num(b.OffsetY)).Append("},\n");
            }
            sb.Append("}\n\n");
        }

        // Where the page's own values go. Emitted as constants so the chunk reaches its element
        // itself, through ScriptedScreens' API, exactly as a hand-written console does - which is
        // what keeps this mod out of the per-frame path of a page it has already translated.
        if (target is { } to)
            sb.Append("SURFACE, ELEMENT, SCENE = ")
              .Append(Quote(to.Surface)).Append(", ").Append(Quote(to.Element)).Append(", ").Append(Quote(to.Scene))
              .Append("\n\n");

        sb.Append(Runtime(element)).Append('\n');
        sb.Append(page);
        // What the page's top-level code queued - a Promise.then, a queueMicrotask - runs once,
        // here, as a browser runs it when the script returns and before any timer.
        sb.Append("\njs_microtasks()\n");
        return sb.ToString();
    }

    /// <summary>
    /// The few lines that turn a DOM write into a slot write, and hand the result over.
    /// </summary>
    /// <remarks>
    /// <c>DOM.bind</c> replaces the prelude's recorder. A write whose (element, property) has no
    /// binding is dropped rather than accumulated - it was either a setup write, or one the compiler
    /// refused, and in both cases keeping it would only grow a table nobody reads.
    ///
    /// <c>snap = 1</c> matters and is easy to lose: without it the renderer eases every number from
    /// what is on screen toward the new value over the gap between payloads. A browser does not, so
    /// a page would gain a glide it never had - on every value at once, which reads as the motion
    /// having been mistranslated rather than as a setting.
    /// </remarks>
    private static string Runtime(string element) => @"
-- Globals of this chunk, not locals, so the host can read what a frame produced. The payload does
-- not go to the element from here: the mod already has the send path, knows which element this page
-- is, and owns the rate at which the renderer wants values. Reaching for an element handle in Lua
-- would mean reproducing all of that, and getting the name wrong once already cost a silent frozen
-- console - the chunk ran, computed every value, and dropped them all.
PAYLOAD, DIRTY = {}, false

-- Whether this chunk's labels are printed by the scene (DOM.label's `ph` arm). CompiledPage.Place
-- turns it on in the same step as it puts the placeholders into the scene, so a chunk whose scene
-- was never placed keeps building its labels whole and draws right either way.
LABELS_PLACED = false -- placed: no

-- What each slot last went out as. A page re-renders whole - every value of every visible panel,
-- 2.4 times a second on the Atmo pages - and nearly all of it is what the scene already shows.
-- Sending it anyway put ~140 values a render through set_props, commit and the renderer's parse
-- and rebuild for nothing. Keyed by slot, not by binding: several bindings write one slot (a tab's
-- state and a choice inside it), and only the slot's last value says what the renderer holds.
-- Comparing a string or a number allocates nothing; the keys are added once, at a slot's first write.
local SENT = {}
local function set(slot, v)
  if SENT[slot] == v then return end
  SENT[slot] = v
  PAYLOAD[slot] = v
  DIRTY = true
end

-- A script writes CSS, not numbers: a height arrives as '18px', an offset as '-604.8px', an opacity
-- as '0.62'. tonumber gives nil for the first two, so coercing instead of parsing made every one of
-- those writes vanish - which is what happened the first time this was run rather than reasoned about.
local function length(v)
  if type(v) == 'number' then return v end
  if type(v) ~= 'string' then return nil end
  return tonumber(v:match('^%s*(-?%d*%.?%d+)'))
end

-- The whole transform string, because that is what a page assigns. A rotate or a scale in the same
-- string is not a translate and is left alone rather than guessed at.
local function translate(v)
  if type(v) ~= 'string' then return nil, nil end
  local x, y = v:match('translate%(%s*(-?%d*%.?%d+)[^,]*,%s*(-?%d*%.?%d+)')
  if x then return tonumber(x), tonumber(y) end
  x = v:match('translateX%(%s*(-?%d*%.?%d+)')
  if x then return tonumber(x), nil end
  y = v:match('translateY%(%s*(-?%d*%.?%d+)')
  if y then return nil, tonumber(y) end
  return nil, nil
end

local function put(slot, n)
  if n == n then set(slot, n) end                     -- NaN: a page mid-calculation, not a value
end

-- A colour as the scene reads it. The vector mod reads #rgb, #rrggbb, #rrggbbaa and Unity's own
-- names, and keeps anything else as text: the fill draws MAGENTA. A page writes CSS - rgba() and
-- hsl() built at run time, a CSS name (CSS green is #008000, Unity's is not) - so every colour a slot
-- takes is made hex here, and one that cannot be is not written at all: the slot keeps what it shows.
-- Remembered by the string the page wrote, so a value repeated every render allocates nothing, and
-- bounded, since a colour animated through rgba() is a new string each frame. `inherit` and
-- `currentColor` need the layout, so the compiler resolves those into a binding's own table.
-- In a block, and reached as DOM.colour, so the chunk's main function gains no local: a page's own
-- top-level locals count against Lua's 200 too, and AtmoDark's and AtmoApple's chunks stopped
-- loading at the ones this added.
do
  local NAMED = '" + StyleApplier.NamedTable + @"'
  local NAMES, CMEMO, CN, CARGS = nil, {}, 0, {}

  local function byte(v)
    if v ~= v then return 0 end
    v = math.floor(v + 0.5)
    if v < 0 then return 0 elseif v > 255 then return 255 end
    return v
  end

  -- As the emitter writes one (VectorEmitter.Hex): #RRGGBB, with AA only when it is not opaque.
  local function hex(r, g, b, a)
    a = byte(a * 255)
    if a == 255 then return string.format('#%02X%02X%02X', byte(r), byte(g), byte(b)) end
    return string.format('#%02X%02X%02X%02X', byte(r), byte(g), byte(b), a)
  end

  -- One argument of rgb() or hsl(): a number, a percentage of `full`, or `none`.
  local function arg(s, full)
    if s == 'none' then return 0 end
    local n, pct = s:match('^([-+]?%d*%.?%d+)(%%?)$')
    n = tonumber(n)
    if n == nil then return nil end
    if pct == '%' then return n * full / 100 end
    return n
  end

  local function hue(s)
    if s == 'none' then return 0 end
    local n, u = s:match('^([-+]?%d*%.?%d+)(%a*)$')
    n = tonumber(n)
    if n == nil then return nil end
    if u == '' or u == 'deg' then return n elseif u == 'turn' then return n * 360
    elseif u == 'rad' then return n * 180 / math.pi elseif u == 'grad' then return n * 0.9 end
    return nil
  end

  local function hue2(p, q, t)
    if t < 0 then t = t + 1 elseif t > 1 then t = t - 1 end
    if t < 1 / 6 then return p + (q - p) * 6 * t end
    if t < 1 / 2 then return q end
    if t < 2 / 3 then return p + (q - p) * (2 / 3 - t) * 6 end
    return p
  end

  local function clamp1(v) if v < 0 then return 0 elseif v > 1 then return 1 end return v end

  local function css_colour(s)
    s = s:lower():match('^%s*(.-)%s*$')
    if s == 'transparent' or s == 'none' then return '#00000000' end
    local h = s:match('^#(%x+)$')
    if h then
      local n = #h
      if n == 3 or n == 4 then h = h:gsub('.', '%0%0') n = n * 2 end
      if n == 8 and h:sub(7) == 'ff' then h = h:sub(1, 6) n = 6 end
      if n ~= 6 and n ~= 8 then return nil end
      return '#' .. h:upper()
    end
    local fn, inner = s:match('^(%a+)%((.*)%)$')
    if fn then
      local a = CARGS
      for i = #a, 1, -1 do a[i] = nil end
      for t in inner:gmatch('[^,%s/]+') do a[#a + 1] = t end
      if #a < 3 or #a > 4 then return nil end
      local alpha = 1
      if a[4] then alpha = arg(a[4], 1) if alpha == nil then return nil end end
      alpha = clamp1(alpha)
      if fn == 'rgb' or fn == 'rgba' then
        local r, g, b = arg(a[1], 255), arg(a[2], 255), arg(a[3], 255)
        if r == nil or g == nil or b == nil then return nil end
        return hex(r, g, b, alpha)
      end
      if fn == 'hsl' or fn == 'hsla' then
        local hh, sat, l = hue(a[1]), arg(a[2], 100), arg(a[3], 100)
        if hh == nil or sat == nil or l == nil then return nil end
        hh, sat, l = (hh % 360) / 360, clamp1(sat / 100), clamp1(l / 100)
        local q = l < 0.5 and l * (1 + sat) or l + sat - l * sat
        local p = 2 * l - q
        return hex(hue2(p, q, hh + 1 / 3) * 255, hue2(p, q, hh) * 255, hue2(p, q, hh - 1 / 3) * 255, alpha)
      end
      return nil
    end
    if NAMES == nil then
      NAMES = {}
      for name, v in NAMED:gmatch('(%a+) (%x+)') do NAMES[name] = '#' .. v:upper() end
    end
    return NAMES[s]
  end

  function DOM.colour(s)
    if type(s) ~= 'string' then return nil end
    local c = CMEMO[s]
    if c == nil then
      if CN >= 256 then CMEMO, CN = {}, 0 end
      c = css_colour(s) or false
      CMEMO[s] = c
      CN = CN + 1
    end
    return c or nil
  end
end

-- tabular-nums, as the emitter draws it: each digit run monospaced so a changing reading does not
-- shuffle sideways. One function for every call, not a closure per gsub.
local function digits(run) return '<mspace=0.6em>' .. run .. '</mspace>' end

-- A text or label value that is the same as last time is the same string as last time, so it is
-- handed over again rather than built again: js_str of a number, a case change and a gsub each make
-- a new string, and a render re-sends every label whether or not it changed. Handed over through
-- set() rather than skipped outright, because another binding - a tab's state - may have written the
-- slot since, and then the renderer holds that, not this.
local function same(b, v)
  if b.shown ~= nil and b.last == v and type(v) ~= 'table' then set(b.to[1][1], b.shown) return true end
  b.last = v
  return false
end

function DOM.bind(id, key, value)
  local e = BOUND[id]
  if e == nil then return end
  local b = e[key]
  if b == nil then return end                         -- a setup write, or one the compiler refused

  if b.read == 'text' then
    -- The value goes out as data, never as scene source, so none of the scene reader's escapes or
    -- guards apply: a quote shipped escaped drew its backslash, and '00042' is a string all the way
    -- to the label. Only the tabular shaping is drawing rather than encoding, so only it stays.
    if same(b, value) then for i = 2, #b.to do set(b.to[i][1], b.shown) end return end
    local s = js_str(value)
    if TEXT[id] then s = (s:gsub('%d+', digits)) end
    b.shown = s
    for i = 1, #b.to do set(b.to[i][1], s) end
    return
  end

  if b.read == 'colour' then
    -- A page writes CSS - `var(--cb-live)`, `inherit` - and the scene reads hex: compiled markup
    -- carries the table from one to the other, made when the page was laid out, and DOM.colour reads
    -- the rest. Passing an unmapped value through drew AtmoDark's gear magenta as `inherit`.
    local s = js_str(value)
    local c = b.map and b.map[s] or DOM.colour(s)
    if c == nil then return end                       -- no colour at all: the slot keeps what it shows
    for i = 1, #b.to do set(b.to[i][1], c) end
    return
  end

  if b.read == 'translate' then
    local x, y = b.x, b.y                             -- parsed once per value, as a length is below
    if b.v ~= value or b.v == nil or type(value) == 'table' then x, y = translate(value) b.v, b.x, b.y = value, x, y end
    if x and b.to[1] then put(b.to[1][1], x + b.to[1][2]) end
    if y and b.to[2] then put(b.to[2][1], y + b.to[2][2]) end
    return
  end

  -- A class name is not a value, it is a state: the compiler laid the page out in each one and
  -- emitted what each DRAWS, so picking one is a lookup and a copy. Without this arm the write fell
  -- through to length('duck'), which is nil, and every compiled page drew its base state for ever
  -- while the states sat in the table unused.
  if b.read == 'state' then
    -- Looked up once per value, not per render: a choice arrives as 1 or 0 and a list as its length,
    -- and js_str of a number is a new string - eighty choices on a page made that eighty a render.
    local st = b.st
    if b.v ~= value or b.v == nil or type(value) == 'table' then
      st = b.states[js_str(value)]
      -- A value none of the states is named after has one of its own when the markup draws
      -- something for 'none of them' (a tab the page does not know): its layout is `other`.
      if st == nil and b.other then st = b.states[b.other] end
      b.v, b.st = value, st
    end
    if st == nil then return end                      -- a class the enumeration never saw
    for i = 1, #st do set(st[i][1], st[i][2]) end
    return
  end

  -- `visibility` is a word on the group's opacity: hidden is 0, anything else is 1. It needs its
  -- own arm for the same reason `state` did - read as a length, 'hidden' is nil and the write
  -- silently does nothing, on a slot the compiler reported as mapped.
  if b.read == 'visibility' then
    local s = js_str(value)
    local n = (s == 'hidden' or s == 'collapse') and 0 or 1
    for i = 1, #b.to do set(b.to[i][1], n) end
    return
  end

  -- Parsed once per value for the same reason: '62%' is a pattern match and a capture every time.
  local n = b.n
  if b.v ~= value or b.v == nil or type(value) == 'table' then n = length(value) b.v, b.n = value, n end
  if n == nil then return end
  -- to[3] is -1 for a far-edge position (`right`, `bottom`) and absent otherwise.
  for i = 1, #b.to do local to = b.to[i] put(to[1], n * (to[3] or 1) + to[2]) end
end

-- A label of compiled markup: literal text and values, written whole once its last value is known.
-- A label whose values are the ones it was last built from is not built again (see same()); one
-- whose values moved costs exactly one string, its new text. That string is the floor while a
-- label is one text slot, which once the scene carries its placeholders is only a label whose
-- literal text the scene source cannot carry; every other label is placed (the `ph` arm below).
-- `b.args` is filled once, at the label's first render.
local LABEL = {}
function DOM.label(id, key, ...)
  local e = BOUND[id]
  local b = e and e[key]
  if b == nil then return end
  -- A placed label: the scene prints it from its own text, `{$slot:%.1f}` per value, so each value
  -- goes to its own slot and no label string is ever built. A number the scene formats goes as the
  -- number; a value that is text is made a string only when it changes.
  local ph = LABELS_PLACED and b.ph
  if ph then
    local fmt, cmap, pv, ps = b.fmt, b.cmap, b.pv, b.ps
    if pv == nil then pv, ps = {}, {} b.pv, b.ps = pv, ps end
    for i = 1, #ph do
      local v = (select(i, ...))
      if fmt[i] and type(v) == 'number' then set(ph[i], v)
      else
        if ps[i] == nil or pv[i] ~= v or type(v) == 'table' then
          local s = js_str(v)
          local map = cmap and cmap[i]
          -- a colour none can read keeps the last one (see DOM.colour)
          if map then s = map[s] or DOM.colour(s) or ps[i]
          elseif b.case == 'upper' then s = s:upper() elseif b.case == 'lower' then s = s:lower() end
          pv[i], ps[i] = v, s
        end
        if ps[i] ~= nil then set(ph[i], ps[i]) end
      end
    end
    return
  end
  local args, n = b.args, select('#', ...)
  if args == nil then args = {} b.args = args end
  local moved = b.shown == nil
  for i = 1, n do
    local v = (select(i, ...))
    if args[i] ~= v or type(v) == 'table' then args[i] = v moved = true end
  end
  if not moved then set(b.to[1][1], b.shown) return end
  local pieces = b.pieces
  for i = 1, #pieces do
    local piece = pieces[i]
    local kind = type(piece)
    if kind == 'string' then LABEL[i] = piece
    elseif kind == 'number' then
      -- Data, not scene source (see the text arm): no escapes, or a quote draws its backslash.
      -- A value the page wrote with toFixed arrives as the number (for a placed label), so it is
      -- formatted back with the same decimals when the label is built here instead.
      local v, f = (select(piece, ...)), b.fmt and b.fmt[piece]
      local s = f and type(v) == 'number' and string.format(f, v) or js_str(v)
      if b.case == 'upper' then s = s:upper() elseif b.case == 'lower' then s = s:lower() end
      LABEL[i] = s
    else
      local s = js_str((select(piece[1], ...)))
      local c = piece[2][s] or DOM.colour(s)
      if c == nil then return end                     -- no colour at all: the label keeps what it shows
      LABEL[i] = c
    end
  end
  b.shown = table.concat(LABEL, '', 1, #pieces)
  set(b.to[1][1], b.shown)
end

-- An innerHTML write replaced these elements, and their listeners went with them (see the call in
-- the translated page). Emptied in place, so a render that registers again reuses the same list.
function DOM.replaced(...)
  for i = 1, select('#', ...) do
    local byKind = DOM.listeners[(select(i, ...))]
    if byKind ~= nil then
      for _, list in pairs(byKind) do
        for j = #list, 1, -1 do
          if type(list[j]) == 'table' and list[j].capture then DOM.captures = DOM.captures - 1 end
          list[j] = nil
        end
      end
    end
  end
end

-- The two the compiler emits instead of a CSS string. A page writes `x + 'px'` and
-- `'translate(' + x + 'px,' + y + 'px)'`; the compiler recognises those shapes and calls these with
-- the numbers, so nothing is built and nothing is parsed. That round trip - build a string, then
-- pattern match the numbers back out of it - was the whole of a compiled page's per-frame garbage,
-- and the compiler was creating both halves of it.
function DOM.num(el, key, n)
  -- tonumber, because a page almost always writes `x.toFixed(1) + 'px'` and toFixed returns a
  -- STRING. Rejecting non-numbers here silently wrote nothing at all, which is how the runner's
  -- player stopped moving the first time this was tried.
  n = tonumber(n)
  if n == nil or n ~= n then return end
  local e = BOUND[rawget(el, '__id')]
  local b = e and e[key]
  if b == nil or b.read ~= 'length' then return end
  for i = 1, #b.to do local to = b.to[i] set(to[1], n * (to[3] or 1) + to[2]) end
end

function DOM.xy(el, key, x, y)
  local e = BOUND[rawget(el, '__id')]
  local b = e and e[key]
  if b == nil or b.read ~= 'translate' then return end
  x, y = tonumber(x), tonumber(y)
  if x ~= nil and x == x and b.to[1] then set(b.to[1][1], x + b.to[1][2]) end
  if y ~= nil and y == y and b.to[2] then set(b.to[2][1], y + b.to[2][2]) end
end

-- Everything one frame wrote, in one payload: the renderer merges a payload and rebuilds once, so
-- splitting these would show the console a half-applied frame.
--
-- `snap` matters and is easy to lose. Without it the renderer eases every number from what is on
-- screen toward the new value over the gap between payloads. A browser does not, so a page would
-- gain a glide it never had - on every value at once, which reads as the motion having been
-- mistranslated rather than as a setting.
-- The page's values, sent the way a hand-written console sends them: from the chip, straight to
-- the vector element, through ScriptedScreens' own API.
--
-- This used to leave PAYLOAD for the host to collect, and the host then built a dictionary, boxed
-- every number into it, built a UiProp[] and called into the vector mod. All of that ran on the
-- game thread, once per console per frame, for ever - so the mod was still in the per-frame path
-- of a page it had already finished translating. Translated once and then costs nothing is not
-- true while that is happening, whatever the per-frame numbers say.
--
-- The handle is taken once and kept. `keep = 1` means take the data and leave the scene alone,
-- which is what makes this an update rather than a re-upload of the structure.
-- Which ids the compiled scene has a shape for. The prelude has always tested this before
-- recording a missing element, and NOTHING EVER ASSIGNED IT - so the test was never true, the
-- missing table was never written, and the reader added to surface it could not fire. The
-- project's own characteristic bug, this time with the halves swapped.
DOM.known = {}
for id in pairs(BOUND) do DOM.known[id] = true end

local SURF, VEC
-- Made once: a table literal and a closure in the send itself were two allocations every time a
-- frame had anything to say. PAYLOAD is emptied in place, never replaced, so PROPS keeps pointing at it.
local PROPS = { data = PAYLOAD, snap = 1 }
local function send() VEC:set_props(PROPS) SURF:commit() end
function DOM.flush()
  -- Raised by the host when it has sent the structure again - a capture, a rebuilt host - with the
  -- scene's resting values. SENT no longer describes the screen, and a value written once (a target,
  -- a label set at load) would read its resting 0 until it next changed, which for those is never.
  -- So everything this page has sent goes again, once. SENT's keys are every slot ever written, so
  -- this is also the complete list, and it allocates only while PAYLOAD grows to that size.
  if RESYNC then
    RESYNC = false
    for k, v in pairs(SENT) do PAYLOAD[k] = v end
    DIRTY = true
  end
  if not DIRTY then return end
  DIRTY = false
  if VEC == nil then
    -- Every one of these used to be a silent return, which is how a console froze while the log
    -- said 82 frames ran and wrote values. SENDNOTE is read once by the host and logged.
    if SURFACE == nil then SENDNOTE = 'no target was compiled in' return end
    local oks, surf = pcall(function() return ss.ui.surface(SURFACE) end)
    if not oks then SENDNOTE = 'ss.ui.surface threw: ' .. tostring(surf) return end
    SURF = surf
    if SURF == nil then SENDNOTE = 'ss.ui.surface(' .. tostring(SURFACE) .. ') is nil' return end
    -- Its OWN element, not the one carrying the structure. `surface:element` REPLACES an element's
    -- props rather than merging them, so asking for a handle to the structure element dropped its
    -- `src` and the console went blank. A hand-written console has always had two - one for the
    -- scene and one for the data - and this is the same split, arrived at the same way.
    local oke, el = pcall(function()
      return SURF:element({ id = ELEMENT .. '_cd', type = 'vector',
                            props = { scene = SCENE, keep = 1 } })
    end)
    if not oke then SENDNOTE = 'surface:element threw: ' .. tostring(el) return end
    VEC = el
    if VEC == nil then SURF = nil SENDNOTE = 'surface:element returned nil for ' .. tostring(ELEMENT) return end
    SENDNOTE = 'sending to ' .. tostring(ELEMENT) .. ' on ' .. tostring(SURFACE)
  end
  local okw, err = pcall(send)
  if not okw then SENDNOTE = 'set_props/commit threw: ' .. tostring(err) return end
  -- Emptied after the commit, not before: what was sent has gone by then, and a frame that moves
  -- one number should send one number rather than every slot the page has ever written.
  for k in pairs(PAYLOAD) do PAYLOAD[k] = nil end
end

-- The entry point the host calls once a frame. It is a GLOBAL of this chunk's own environment,
-- which is how the host finds it - everything the page itself declared is a local of the chunk and
-- invisible from outside, deliberately.
--
-- What it drives depends on what the page asked for, and pages differ: a game registers a
-- requestAnimationFrame callback and re-registers it every frame, a dashboard sets an interval and
-- keeps the same one. Both were captured by the prelude rather than run, so this is where they are
-- finally driven - and then one payload goes out for everything the frame wrote.
-- The clock `Date.now()` and `performance.now()` read. The prelude asks for `js_now` and nothing
-- ever defined it, so both answered a hard 0 for ever: a page using the ordinary elapsed-time
-- idiom - `Date.now() - start` - got zero every frame and froze at its first, with nothing in any
-- log. It is milliseconds since the scene was applied rather than a wall clock, which is what an
-- elapsed-time measurement actually needs and all a console can honestly offer.
function js_now() return (CLOCK or 0) * 1000 end

frame = function(dt)
  CLOCK = (CLOCK or 0) + (dt or 0)
  local t = CLOCK * 1000

  local pending = Pending.frame
  local n = #pending
  if n > 0 then
    local fn = pending[n]
    -- Emptied in place before the call, since a rAF page re-registers inside it. A fresh table here
    -- was an allocation on every frame of every animated page.
    for i = n, 1, -1 do pending[i] = nil end
    fn(t)
  end

  -- Timers run whether or not an animation frame was pending. They used to be the `else` of the
  -- branch above, which meant a page doing BOTH - a requestAnimationFrame render loop plus a
  -- setInterval poll, which is the ordinary browser combination - lost every interval callback
  -- for the life of the console while the animation looked perfectly healthy.
  for i = 1, #Pending.timers do
    local timer = Pending.timers[i]
    if timer.fn ~= nil then            -- nil is how clearInterval empties a slot
      timer.at = (timer.at or 0) + (dt or 0) * 1000
      if timer.at >= (timer.ms or 0) then
        timer.at = 0
        local fn = timer.fn
        -- A one-shot is cleared BEFORE it runs, so a handler that schedules another timeout gets
        -- a slot of its own instead of having its registration wiped by this line. `once` was
        -- recorded by setTimeout and read by nothing, so every timeout repeated for ever.
        if timer.once then timer.fn = nil end
        fn(t)
      end
    end
  end

  -- The reactions this frame's callbacks queued, before the payload goes: a browser runs them
  -- when the callback returns, and a value a .then wrote has to land in THIS frame's payload,
  -- not the next one's. An empty queue is one comparison.
  js_microtasks()
  DOM.flush()
end

-- The other entry point: something the player did. The host calls this with the id of the scene
-- region that was hit, and the page's own handlers run here, in the chunk, against the state that
-- is on screen. Before this existed the click went to the INTERPRETER's copy of the page - which a
-- compiled console has stopped drawing - so every button was dead while the scene still carried its
-- click region and the log said nothing was wrong.
event = function(id, kind, x, y)
  DOM.fire(id, kind, x, y)
  js_microtasks()
  DOM.flush()
end

-- The third: what the chip's own Lua sent as data, as a table - the handler the page wrote gets it
-- as `window.ondata(detail, event)` and as a `data` event, exactly as the interpreter gave it. Before
-- this a compiled page's data handler never ran at all. One event object for the life of the chunk,
-- refilled, since a payload arrives every tick and a new event per tick was allocation for nothing.
DATA_EV = { type = 'data', bubbles = false, cancelable = false, defaultPrevented = false,
            eventPhase = 2, isTrusted = true, timeStamp = 0,
            preventDefault = function() end, stopPropagation = function() end,
            stopImmediatePropagation = function() end }
data_in = function(detail)
  local ev = DATA_EV
  ev.detail, ev.target, ev.currentTarget, ev.timeStamp = detail, window, window, js_now()
  local on = rawget(window, 'ondata')
  if type(on) == 'function' then on(detail, ev) end
  DOM.emit('window', 'data', ev)
  js_microtasks()
  DOM.flush()
end
";

    private static string Map(IReadOnlyDictionary<string, string> map)
    {
        var sb = new StringBuilder("{ ");
        foreach (var kv in map) sb.Append('[').Append(Quote(kv.Key)).Append("] = ").Append(Quote(kv.Value)).Append(", ");
        return sb.Append('}').ToString();
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    private static string Num(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e9
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.####", CultureInfo.InvariantCulture);
}

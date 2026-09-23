using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// A page's <c>innerHTML</c> markup, compiled into the scene: every piece of markup the page can
/// show, laid out once in each layout it can take, and the values the chip writes.
/// </summary>
/// <remarks>
/// <see cref="Markup"/> says what the page builds: fixed markup, values, choices, lists. This is the
/// half that needs the layout engine. It writes the union of everything the page can show into the
/// element (<see cref="Markup.Union"/>), lays it out in every layout the page can take - each choice's
/// side and each list's length - and emits each. What differs between those emissions is what the
/// chip writes when the page changes layout: a STATE, picked by the value the page tests. What a
/// value writes is found by putting a distinctive value where it goes and seeing which slot comes
/// back holding it, or - for a number in a style - by moving it and seeing which slots move.
///
/// Each layout is emitted with the alternatives it does not show set to `display: none`, so the
/// emission is the union's own lines with those subtrees cut out. The cut is known exactly - an
/// alternative is wrapped in its own named group, and its gradients and clips carry its elements'
/// names - so every line of every layout is matched to the union's line it came from, and a slot is
/// named the same in all of them. That match is checked line by line and a layout that does not
/// match is REPORTED: a state built from misaligned lines would move the wrong shapes.
///
/// The page is put back exactly as it was afterwards. Compiling leaves nothing behind.
/// </remarks>
internal static class MarkupSlots
{
    internal enum Kind { None, Text, Number, Colour, Variant }

    /// <summary>What one hole writes.</summary>
    internal sealed class HoleSlots
    {
        public Kind Kind;
        /// <summary>Number, Colour and a whole label's Text: the slots, each `value * Scale + Bias` for a number.</summary>
        public readonly List<(string Slot, double Scale, double Bias)> To = new();
        /// <summary>Text that is one piece of a label built from several: which label.</summary>
        public int Label = -1;
        /// <summary>A colour hole's values, as the scene's hex: every one the page can write here.</summary>
        public Dictionary<string, string>? Colours;
        /// <summary>A variant: what each value draws, laid out.</summary>
        public List<CompiledPage.StateValues>? States;
    }

    /// <summary>A text slot built from literal pieces and holes: `set 101.3 kPa · trip 140`.</summary>
    internal sealed class Label
    {
        public string Slot = string.Empty;
        public readonly List<(string? Text, int Hole, bool Colour)> Pieces = new();
        /// <summary>`upper` or `lower`, when the label's text-transform changes what is drawn.</summary>
        public string? Transform;
    }

    /// <summary>A state the chunk picks by the value it is given: which layout, which variant, how many rows.</summary>
    internal sealed class Binding
    {
        public string Key = string.Empty;
        public readonly List<CompiledPage.StateValues> States = new();
        /// <summary>The state for a value no state names ("none of the tabs"), or null.</summary>
        public string? Other;
    }

    internal sealed class Target
    {
        public string Id = string.Empty;
        public Markup Markup = null!;
        public Markup.Union Union = null!;
        public Markup.Drive? Drive;
        public readonly Dictionary<int, HoleSlots> Holes = new();
        public readonly List<Label> Labels = new();
        public readonly List<Binding> Bindings = new();
        /// <summary>Every alternative's top element, each gated by its own `v`.</summary>
        public readonly List<string> Roots = new();
        /// <summary>
        /// Holes whose values draw differently SHAPED markup: the compile runs again with their
        /// elements written once per value (<see cref="Markup.Of"/>'s `expand`).
        /// </summary>
        public readonly HashSet<int> Expand = new();
    }

    internal sealed class Result
    {
        public readonly List<Target> Targets = new();
        /// <summary>The whole scene with every union in it, gated: the structure the compiled page opens with.</summary>
        public string? Template;
        /// <summary>What that structure opens with: the layout the page starts in.</summary>
        public readonly Dictionary<string, SceneSlots.Value> Values = new(StringComparer.Ordinal);
        public readonly List<string> Problems = new();
    }

    /// <summary>What compiling cost, by phase: for a probe to print, and for deciding what to make faster.</summary>
    internal static readonly System.Diagnostics.Stopwatch TimeBuild = new(), TimeLayout = new(), TimeEmit = new();
    internal static int Builds, Emits;

    // ---- sentinels -------------------------------------------------------------------------------

    /// <summary>A value findable by what it is: a number far outside any page's, one per hole.</summary>
    internal const int SentinelBase = 987650;
    internal static string Sentinel(int hole) => (SentinelBase + hole).ToString(CultureInfo.InvariantCulture);
    internal static int HoleOf(double value)
    {
        var n = (int)value - SentinelBase;
        return n >= 0 && n < 10000 && value == (int)value ? n : -1;
    }
    /// <summary>The colour-shaped flavour, for where a number is not valid CSS.</summary>
    internal static string ColourSentinel(int hole) => "#0F" + hole.ToString("X4", CultureInfo.InvariantCulture);
    internal static int ColourHoleOf(string? value)
    {
        if (value is not { Length: 7 } || !value.StartsWith("#0F", StringComparison.OrdinalIgnoreCase)) return -1;
        return int.TryParse(value.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n) && n < 10000 ? n : -1;
    }

    // ---- the compile -----------------------------------------------------------------------------

    /// <summary>Compiles every markup write of a page into one scene.</summary>
    /// <param name="probed">
    /// What the page held when it last ran, when known: a hole's value by target and index, a
    /// drive's value at index -1, a choice's side at -1000 - index, a list's length at -2000 - index.
    /// </param>
    internal static Result Compile(HtmlRenderer.Result built, Panel panel,
                                   IReadOnlyList<(string Id, Markup Markup)> writes,
                                   Func<string, int, string?>? probed = null)
    {
        var result = new Result();
        var pristine = new List<(VisualElement Ve, HtmlNode Node, string Html)>();
        // Every emission here is of a page changed in place a moment ago, and the emitter's own cache
        // hands back a parent's last text when only something inside it moved - a bar whose width
        // was changed read as a bar that had not changed at all.
        var wasNoCache = VectorEmitter.NoCache;
        VectorEmitter.NoCache = true;
        // A number and its unit side by side are one label while markup compiles: laid out once, the
        // unit would stay where the stand-in number ended. The gated alternatives are added as found.
        var wasJoin = VectorEmitter.JoinRows;
        VectorEmitter.JoinRows = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var (id, markup) in writes)
            {
                if (!built.ById.TryGetValue(id, out var ve) || ve == null || !built.NodeOf.TryGetValue(ve, out var node))
                {
                    result.Problems.Add($"\"{id}\" names no element in the page");
                    continue;
                }
                if (result.Targets.Exists(t => t.Id == id)) continue;          // one plan per element
                pristine.Add((ve, node, HtmlRenderer.ToHtml(node, outer: false, keepIds: true)));
                result.Targets.Add(new Target { Id = id, Markup = markup, Drive = markup.Driver() });
            }
            if (result.Targets.Count == 0) return result;
            new Compiler(built, panel, result, probed).Run();
            return result;
        }
        catch (Exception ex)
        {
            result.Problems.Add("compiling the markup threw - " + ex);
            return result;
        }
        finally
        {
            VectorEmitter.NoCache = wasNoCache;
            VectorEmitter.JoinRows = wasJoin;
            // Put back, always, and checked: compiling mutates the live page, and a console drawing
            // sentinels where its readings belong is the failure this must never leave behind.
            foreach (var (ve, node, html) in pristine)
            {
                ve.Clear();
                node.Children.Clear();
                HtmlRenderer.AppendFragment(ve, node, html, built);
            }
            Attach(built);
            panel.Layout(panel.Width, panel.Height);
            foreach (var (ve, node, html) in pristine)
                if (!string.Equals(HtmlRenderer.ToHtml(node, outer: false, keepIds: true), html, StringComparison.Ordinal))
                    ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: \"{ve.name}\" could not be put back after compiling its markup. This is a compiler bug, not a fault in the page.");
        }
    }

    /// <summary>Grids and post-layout passes for whatever was just appended, as the surface attaches them.</summary>
    private static void Attach(HtmlRenderer.Result built)
    {
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
    }

    private sealed class Compiler
    {
        private readonly HtmlRenderer.Result _built;
        private readonly Panel _panel;
        private readonly Result _result;
        private readonly Func<string, int, string?>? _probed;
        /// <summary>Emit index to a stable name, for the defs an element mints.</summary>
        private readonly Dictionary<int, string> _defNames = new();
        private readonly Dictionary<string, VisualElement> _elements = new(StringComparer.Ordinal);
        /// <summary>Each union element's place in the markup, in document order.</summary>
        private readonly Dictionary<string, int> _order = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Target> _rootOf = new(StringComparer.Ordinal);
        private Scene _union = null!;
        /// <summary>For each union line, the gated roots it is drawn under.</summary>
        private List<string>[] _owners = Array.Empty<List<string>>();
        private readonly Dictionary<string, Scene> _emitted = new(StringComparer.Ordinal);
        /// <summary>Every layout that matched the union, in the order they were made.</summary>
        private readonly List<Scene> _laid = new();
        /// <summary>While a value is moved to see what it writes: those layouts are not what the page opens with.</summary>
        private bool _probing;
        /// <summary>Each alternative's own `display`, which "shown" puts back.</summary>
        private readonly Dictionary<string, StyleEnum<DisplayStyle>> _display = new(StringComparer.Ordinal);

        public Compiler(HtmlRenderer.Result built, Panel panel, Result result, Func<string, int, string?>? probed)
        {
            _built = built; _panel = panel; _result = result; _probed = probed;
        }

        public void Run()
        {
            // 1. The union, with the values the page really draws (or a stand-in), into every target.
            foreach (var t in _result.Targets)
            {
                t.Union = t.Markup.Write(t.Id, (h, place) => Real(t, h, place));
                foreach (var p in t.Union.Problems) Problem(t, p);
                Build(t, t.Union.Html);
                foreach (var side in t.Union.Sides.Values) { Roots(t, side.Then); Roots(t, side.Else); }
                foreach (var rows in t.Union.Rows.Values) foreach (var row in rows) Roots(t, row);
            }
            Name();
            Rounds();
            _union = UnionScene();
            Own();

            // 2. Every layout the page can take, and what each draws.
            foreach (var t in _result.Targets) States(t);

            // 3. What each value writes.
            Numbers();
            Variants();
            Discover();

            // 4. The structure, gated, opening in the page's first layout.
            Structure();
        }

        // ---- building ------------------------------------------------------------------------

        private string Real(Target t, Markup.Hole h, Markup.Place place)
        {
            if (_probed?.Invoke(t.Id, h.Index) is { } seen) return seen;
            if (t.Markup.Enumerate(h.Value) is { Count: > 0 } alts) return alts[0];
            // A stand-in that parses where it lands: a length, a colour, a word. Not zero for a
            // number: `C 0 0%, transparent 0%` is a gradient with nothing in it, drawn in a
            // different shape from the bar every real value draws.
            if (place.Attribute == null) return "0";
            if (place.Attribute != "style") return string.Empty;
            return IsColourProperty(place.Property) ? "#000000" : "37";
        }

        private void Build(Target t, string html)
        {
            Builds++;
            TimeBuild.Start();
            try { BuildInner(t, html); } finally { TimeBuild.Stop(); }
        }

        private void BuildInner(Target t, string html)
        {
            var ve = _built.ById[t.Id];
            var node = _built.NodeOf[ve];
            ve.Clear();
            node.Children.Clear();
            HtmlRenderer.AppendFragment(ve, node, html, _built);
            Attach(_built);
            Animate(t);
            Rename(ve);
        }

        /// <summary>
        /// Runs a union element's keyframe animation in the scene, as the surface does for the page's
        /// own. One the scene cannot run by itself would need a runner ticking on the game thread for
        /// ever, which a compiled page does not have - so it is said, not dropped in silence.
        /// </summary>
        private void Animate(Target t)
        {
            foreach (var (element, spec) in _built.Animations)
            {
                if (!_built.AnimationAttached.Add(element)) continue;
                if (_built.CssOf(element).TryGetValue("animation-timeline", out var timeline) && timeline.Trim() != "auto") continue;
                if (!_built.Keyframes.TryGetValue(spec.Name, out var frames)) continue;
                if (float.IsPositiveInfinity(spec.Iterations) && !spec.Paused && VectorEmitter.Compilable(frames, _built.CssOf(element)))
                {
                    _built.TimeAnimations[element] = (spec, OffThread.Now);
                    continue;
                }
                Problem(t, $"the animation `{spec.Name}` is not one the scene can run by itself (it ends, or animates something other than opacity, transform, a colour filter, a flat background colour or the position of a striped background), so it stays at its first frame");
            }
        }

        /// <summary>Every union element takes its compile-time name, so the scene names its slots after it.</summary>
        private void Rename(VisualElement root)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var ve = root[i];
                if (_built.NodeOf.TryGetValue(ve, out var node) && node.Attr(Markup.NameAttribute) is { Length: > 0 } sn && ve.name != sn)
                {
                    if (ve.name != null) _built.ById.Remove(ve.name);
                    ve.name = sn;
                    _built.ById[sn] = ve;
                }
                Rename(ve);
            }
        }

        private void Roots(Target t, List<string> names)
        {
            foreach (var n in names)
            {
                if (_rootOf.ContainsKey(n)) continue;
                _rootOf[n] = t;
                t.Roots.Add(n);
                VectorEmitter.JoinRows?.Add(n);   // gated on its own, so never folded into a neighbour's label
                // Named, so the emitter wraps it in a group carrying its id: that group is what the
                // gate goes on, and what marks where its lines begin and end.
                _built.NamedGroups.Add(n);
            }
        }

        /// <summary>Stable names for every union element, and emit indices assigned in that order.</summary>
        private void Name()
        {
            _defNames.Clear();
            _order.Clear();
            var before = new Dictionary<string, VisualElement>(_elements, StringComparer.Ordinal);
            _elements.Clear();
            foreach (var t in _result.Targets) Walk(_built.ById[t.Id], t.Id);

            void Walk(VisualElement ve, string above)
            {
                for (var i = 0; i < ve.childCount; i++)
                {
                    var child = ve[i];
                    var name = child.name is { Length: > 0 } n && _built.NodeOf.TryGetValue(child, out var node) && node.Attr(Markup.NameAttribute) == n
                        ? n : above + "_c" + i.ToString(CultureInfo.InvariantCulture);
                    _elements[name] = child;
                    _order[name] = _order.Count;
                    _defNames[_built.EmitIndexOf(child)] = name;
                    // An alternative's own display, read only off an element just built: one that was
                    // here before has had it set to none by the last layout, and reading that back
                    // would hide it for good.
                    if (_rootOf.ContainsKey(name) && (!before.TryGetValue(name, out var was) || !ReferenceEquals(was, child)))
                        _display[name] = child.style.display;
                    Walk(child, name);
                }
            }
        }

        // ---- emitting and aligning --------------------------------------------------------------

        private sealed class Scene
        {
            public string[] Lines = Array.Empty<string>();
            /// <summary>The same lines before the split: literal values, which a merge of two layouts re-splits.</summary>
            public string[] Raw = Array.Empty<string>();
            public readonly Dictionary<string, SceneSlots.Value> Values = new(StringComparer.Ordinal);
            /// <summary>This layout's line to the union's line.</summary>
            public int[]? Map;
        }

        /// <summary>Shows every alternative except the given ones, each with its own display.</summary>
        private void Show(HashSet<string>? hidden)
        {
            foreach (var t in _result.Targets)
                foreach (var r in t.Roots)
                    if (_elements.TryGetValue(r, out var ve) && _display.TryGetValue(r, out var own))
                        ve.style.display = hidden != null && hidden.Contains(r) ? DisplayStyle.None : own;
        }

        /// <summary>The page laid out with the given roots not drawn (null: every one drawn), emitted and split.</summary>
        private Scene Emit(HashSet<string>? hidden)
        {
            Show(hidden);
            return Emitted();
        }

        /// <summary>
        /// Everything the page can draw, as one set of lines: the layouts of a few real states that
        /// between them show every alternative, merged. Not everything laid out at once - side by
        /// side, alternatives squeeze each other (a clipped panel shrunk to nothing emits no clip),
        /// and a grid of seven tiles written twice each places fourteen. A real state draws every
        /// element as the page draws it.
        /// </summary>
        private Scene UnionScene()
        {
            Scene? merged = null;
            foreach (var config in _unionRounds)
            {
                merged = merged == null ? Emit(Hidden(config)) : Merge(merged, Emit(Hidden(config)));
            }
            return merged ?? Emit(null);
        }

        /// <summary>The states the union is merged from.</summary>
        private readonly List<Config> _unionRounds = new();

        /// <summary>
        /// The sides of the choices between copies of an element written once per value, in as few
        /// layouts as show every copy: each state is laid out once per round, so every copy is drawn
        /// in a state that shows it.
        /// </summary>
        private readonly List<Dictionary<(Target, int), bool>> _rounds = new();

        private void Rounds()
        {
            _rounds.Clear();
            _rounds.AddRange(Cover((t, choice) => Copied(t, choice), out _));
            _unionRounds.Clear();
            foreach (var sides in Cover((_, _) => true, out var drives))
            {
                var c = Base().Copy();
                foreach (var pair in sides)
                    if (pair.Key.Item1.Drive is not { } d || !d.Tests.ContainsKey(pair.Key.Item2)) c.Sides[pair.Key] = pair.Value;
                foreach (var pair in drives[sides]) c.Drive[pair.Key] = pair.Value;
                _unionRounds.Add(c);
            }
        }

        /// <summary>
        /// Sets of choice sides, as few as show every alternative once: each alternative needs the
        /// sides along its path, and joins the first set those do not contradict. A drive's choices
        /// are only compatible while one value of the drive takes all their sides at once.
        /// </summary>
        private List<Dictionary<(Target, int), bool>> Cover(Func<Target, int, bool> counts,
                                                          out Dictionary<Dictionary<(Target, int), bool>, Dictionary<Target, string?>> drives)
        {
            var sets = new List<Dictionary<(Target, int), bool>>();
            drives = new Dictionary<Dictionary<(Target, int), bool>, Dictionary<Target, string?>>();
            var drive = drives;
            foreach (var t in _result.Targets)
                foreach (var root in t.Roots)
                {
                    var need = new List<(int Choice, bool Side)>();
                    foreach (var (list, index, which) in t.Union.Path[root])
                        if (!list && counts(t, index)) need.Add((index, which == 1));
                    if (need.Count == 0) continue;
                    Dictionary<(Target, int), bool>? home = null;
                    string? value = null;
                    foreach (var set in sets)
                    {
                        if (!need.TrueForAll(n => !set.TryGetValue((t, n.Choice), out var side) || side == n.Side)) continue;
                        if (!DriveFor(t, set, need, out value)) continue;
                        home = set;
                        break;
                    }
                    if (home == null)
                    {
                        home = new Dictionary<(Target, int), bool>();
                        if (!DriveFor(t, home, need, out value)) continue;   // no value of the drive shows it: said by Showing
                        sets.Add(home);
                        drive[home] = new Dictionary<Target, string?>();
                    }
                    foreach (var n in need) home[(t, n.Choice)] = n.Side;
                    if (t.Drive != null && need.Exists(n => t.Drive.Tests.ContainsKey(n.Choice))) drive[home][t] = value;
                }
            return sets;

            // A value of the target's drive taking every drive choice's side the set and the need ask for.
            bool DriveFor(Target t, Dictionary<(Target, int), bool> set, List<(int Choice, bool Side)> need, out string? value)
            {
                value = drive.TryGetValue(set, out var had) && had.TryGetValue(t, out var v) ? v : null;
                if (t.Drive == null) return true;
                var asked = new List<(int, bool)>();
                foreach (var pair in set) if (pair.Key.Item1 == t && t.Drive.Tests.ContainsKey(pair.Key.Item2)) asked.Add((pair.Key.Item2, pair.Value));
                foreach (var n in need) if (t.Drive.Tests.ContainsKey(n.Choice)) asked.Add((n.Choice, n.Side));
                if (asked.Count == 0) return true;
                var candidates = new List<string?>(t.Drive.Values) { null };
                foreach (var candidate in candidates)
                    if (asked.TrueForAll(a => t.Drive.Side(a.Item1, candidate) == a.Item2)) { value = candidate; return true; }
                return false;
            }
        }

        /// <summary>A choice between the copies of an element written once per value (the drive's own are the drive's).</summary>
        private static bool Copied(Target t, int choice)
            => t.Markup.Choices.Find(c => c.Index == choice) is { Expanded: true } && (t.Drive == null || !t.Drive.Tests.ContainsKey(choice));

        /// <summary>A round's side of a choice between copies: the first copy where it says nothing.</summary>
        private static bool Side(Dictionary<(Target, int), bool> round, Target t, int choice)
            => !round.TryGetValue((t, choice), out var side) || side;

        /// <summary>
        /// Two emits of the same markup with different copies shown, as one: the lines both draw
        /// once, and each one's own lines where they stand. Matched as a tree - a group is one item
        /// among its siblings and its insides are merged in turn - so a line never pairs with its
        /// like in another copy's group.
        /// </summary>
        private Scene Merge(Scene a, Scene b)
        {
            var raw = new List<string>(a.Raw.Length + b.Raw.Length);
            Siblings(0, a.Lines.Length, 0, b.Lines.Length);
            var scene = new Scene();
            var wasStops = SceneSlots.SlotStops;
            SceneSlots.SlotStops = true;
            try { scene.Lines = SceneSlots.Split(string.Join("\n", raw), scene.Values).Split('\n'); }
            finally { SceneSlots.SlotStops = wasStops; }
            scene.Raw = raw.ToArray();
            return scene;

            void Siblings(int aFrom, int aTo, int bFrom, int bTo)
            {
                var ia = Items(a.Lines, aFrom, aTo);
                var ib = Items(b.Lines, bFrom, bTo);
                // the longest common run of items, by their first line
                // A rectangular table is exactly what a longest-common-subsequence needs; it lives for one call.
#pragma warning disable CA1814
                var lcs = new int[ia.Count + 1, ib.Count + 1];
#pragma warning restore CA1814
                for (var i = ia.Count - 1; i >= 0; i--)
                    for (var j = ib.Count - 1; j >= 0; j--)
                        lcs[i, j] = Same(ia[i], ib[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                int x = 0, y = 0;
                while (x < ia.Count || y < ib.Count)
                {
                    if (x < ia.Count && y < ib.Count && Same(ia[x], ib[y]) && lcs[x, y] == lcs[x + 1, y + 1] + 1)
                    {
                        var (sa, ea) = ia[x++];
                        var (sb, eb) = ib[y++];
                        // the line with the radius, when only one of them has room for it
                        raw.Add(Matches(a.Lines[sa], b.Lines[sb]) ? a.Raw[sa] : b.Raw[sb]);
                        if (ea > sa) { Siblings(sa + 1, ea, sb + 1, eb); raw.Add(a.Raw[ea]); }
                    }
                    // Where either side's next item could come first, the one earlier in the markup
                    // does: the copies of one element shown in different rounds keep their order.
                    else if (y >= ib.Count || x < ia.Count && (lcs[x + 1, y] > lcs[x, y + 1]
                             || lcs[x + 1, y] == lcs[x, y + 1] && Order(a.Lines, ia[x]) <= Order(b.Lines, ib[y])))
                    {
                        var (s0, e0) = ia[x++];
                        for (var k = s0; k <= e0; k++) raw.Add(a.Raw[k]);
                    }
                    else
                    {
                        var (s0, e0) = ib[y++];
                        for (var k = s0; k <= e0; k++) raw.Add(b.Raw[k]);
                    }
                }
            }

            bool Same((int Start, int End) p, (int Start, int End) q)
                => p.End > p.Start == q.End > q.Start && (Matches(a.Lines[p.Start], b.Lines[q.Start]) || Matches(b.Lines[q.Start], a.Lines[p.Start]));
        }

        /// <summary>Where the first element an item draws stands in the markup.</summary>
        private int Order(string[] lines, (int Start, int End) item)
        {
            for (var i = item.Start; i <= item.End; i++)
                if (IdOf(lines[i]) is { } id && _order.TryGetValue(id, out var n)) return n;
            return int.MaxValue;
        }

        /// <summary>The items between two lines: a line, or a group from its opening line to its closing one.</summary>
        private static List<(int Start, int End)> Items(string[] lines, int from, int to)
        {
            var items = new List<(int, int)>();
            for (var i = from; i < to; i++)
            {
                var end = Braces(lines[i]) > 0 ? Math.Min(Close(lines, i), to - 1) : i;
                items.Add((i, end));
                i = end;
            }
            return items;
        }

        private Scene Emitted()
        {
            Emits++;
            TimeLayout.Start();
            // Settled, as the surface settles a frame before it paints: a mixed calc() - a gauge's
            // `height: calc(62% - 12px)` - is written after layout from the parent's new size, and
            // one pass left it a layout behind, so the same value read differently in each probe.
            for (var pass = 0; pass < 4; pass++)
            {
                var writes = PostLayout.LayoutWrites;
                _panel.Layout(_panel.Width, _panel.Height);
                if (PostLayout.LayoutWrites == writes) break;
            }
            TimeLayout.Stop();
            TimeEmit.Start();
            try { return EmittedInner(); } finally { TimeEmit.Stop(); }
        }

        private Scene EmittedInner()
        {
            var scene = new Scene();
            var wasBoxes = OffThread.Boxes;
            var wasActive = OffThread.Active;
            var wasJob = OffThread.Job;
            var touched = new HashSet<VisualElement>(_built.Touched);
            var deep = new HashSet<VisualElement>(_built.TouchedDeep);
            try
            {
                var boxes = new Dictionary<VisualElement, OffThread.Box>();
                OffThread.Capture(_built.Root, _built, boxes, new List<VisualElement>());
                _built.Touched.UnionWith(touched);
                _built.TouchedDeep.UnionWith(deep);
                OffThread.Boxes = boxes;
                OffThread.Active = true;
                OffThread.Job = OffThread.Globals.Take();
                var text = VectorEmitter.Isolated(() =>
                {
                    var o = VectorEmitter.Emit(_built, _built.Root, _panel.Width, _panel.Height);
                    return new string(o.Chars, 0, o.Length);
                });
                var wasStops = SceneSlots.SlotStops;
                SceneSlots.SlotStops = true;
                var normal = Normalise(text);
                string template;
                try { template = SceneSlots.Split(normal, scene.Values); }
                finally { SceneSlots.SlotStops = wasStops; }
                scene.Lines = template.Split('\n');
                scene.Raw = normal.Split('\n');
            }
            finally
            {
                OffThread.Boxes = wasBoxes;
                OffThread.Active = wasActive;
                OffThread.Job = wasJob;
            }
            return scene;
        }

        private static readonly Regex DefId = new(@"\b(tw|scroll|clip|rad|grad|cut|tg|svg|cg|cclip|conic|stripes|bimg|ka|cpath|mask)(\d+)_(\d+)\b", RegexOptions.Compiled);

        /// <summary>
        /// A gradient or a clip minted by a union element, renamed after that element. The emitter
        /// numbers them by the element's emit index, which a rebuilt union does not keep - and a
        /// template that changes its def names between two builds of the same markup cannot be
        /// compared with itself.
        /// </summary>
        private string Normalise(string text)
            => AxisLines(RectClips(DefId.Replace(text, m =>
                int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && _defNames.TryGetValue(index, out var name)
                    ? m.Groups[1].Value + name + "_" + m.Groups[3].Value
                    : m.Value)));

        private static readonly Regex PolygonClip = new(@"CP id=(\S+) \{ Y p=\[([^\]]*)\] \}", RegexOptions.Compiled);

        /// <summary>
        /// A clip written as a polygon that is really an axis-aligned rectangle, written as one.
        /// </summary>
        /// <remarks>
        /// A hard colour stop (`linear-gradient(90deg, C 0 62%, transparent 0)`, every fill bar on a
        /// dashboard) is drawn as a rect clipped to its band, and the band is emitted as polygon points
        /// - an array, which the scene reads once and which cannot carry a slot. So the bar's length,
        /// and where the whole bar sits in a layout, would be baked into the structure. As a rect the
        /// same band has an x, a y, a w and an h the chip can write. Same geometry, to the pixel: only
        /// a polygon whose area IS its bounding box is rewritten.
        /// </remarks>
        internal static string RectClips(string text)
            => PolygonClip.Replace(text, m =>
            {
                var parts = m.Groups[2].Value.Split(',');
                if (parts.Length < 8 || parts.Length % 2 != 0) return m.Value;
                var pts = new float[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                    if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out pts[i])) return m.Value;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, area = 0f;
                var n = pts.Length / 2;
                for (var i = 0; i < n; i++)
                {
                    float ax = pts[2 * i], ay = pts[2 * i + 1], bx = pts[2 * ((i + 1) % n)], by = pts[2 * ((i + 1) % n) + 1];
                    x0 = Math.Min(x0, ax); y0 = Math.Min(y0, ay); x1 = Math.Max(x1, ax); y1 = Math.Max(y1, ay);
                    area += ax * by - bx * ay;
                }
                var box = (x1 - x0) * (y1 - y0);
                if (box <= 0f || Math.Abs(Math.Abs(area) * 0.5f - box) > 0.5f) return m.Value;
                return "CP id=" + m.Groups[1].Value + " { R x=" + Num(x0) + " y=" + Num(y0) + " w=" + Num(x1 - x0) + " h=" + Num(y1 - y0) + " }";
            });

        private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static readonly Regex AxisLine = new(@"^(?<indent>\s*)L p=\[(?<x1>[-\d.]+),(?<y1>[-\d.]+),(?<x2>[-\d.]+),(?<y2>[-\d.]+)\] s=(?<s>\S+) sw=(?<sw>[\d.]+)(?=\r?$)", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>
        /// A solid, square-ended line along an axis, written as the rect it covers: a border side or
        /// an underline. Its points are an array the scene reads once, so a border under a panel that
        /// moves between layouts would stay where the first layout put it; as a rect it has an x and
        /// a y the chip can write. Anything with a dash or a cap keeps its line.
        /// </summary>
        internal static string AxisLines(string text)
            => AxisLine.Replace(text, m =>
            {
                float F(string g) => float.Parse(m.Groups[g].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                var (x1, y1, x2, y2, sw) = (F("x1"), F("y1"), F("x2"), F("y2"), F("sw"));
                string rect;
                if (Math.Abs(y1 - y2) < 0.001f) rect = "R x=" + Num(Math.Min(x1, x2)) + " y=" + Num(y1 - sw / 2) + " w=" + Num(Math.Abs(x2 - x1)) + " h=" + Num(sw);
                else if (Math.Abs(x1 - x2) < 0.001f) rect = "R x=" + Num(x1 - sw / 2) + " y=" + Num(Math.Min(y1, y2)) + " w=" + Num(sw) + " h=" + Num(Math.Abs(y2 - y1));
                else return m.Value;
                return m.Groups["indent"].Value + rect + " f=" + m.Groups["s"].Value;
            });

        /// <summary>The union element each union line is drawn for, when it can be said.</summary>
        private string?[] _lineOwner = Array.Empty<string?>();

        /// <summary>
        /// Who each line of the union draws for: its own id when it carries one, the element a
        /// wrapper group wraps (the first element inside it), and for a line with neither the
        /// element drawn just before it in the same group.
        /// </summary>
        private void LineOwners()
        {
            var lines = _union.Lines;
            _lineOwner = new string?[lines.Length];
            var stack = new List<(int Line, string? Owner)>();
            string? previous = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var own = IdOf(lines[i]);
                var opens = Braces(lines[i]);
                string? owner = own;
                if (owner == null && opens > 0)
                {
                    // a wrapper: the first element drawn inside it
                    var end = Close(lines, i);
                    for (var k = i + 1; k <= end && owner == null; k++) owner = IdOf(lines[k]);
                }
                owner ??= previous ?? (stack.Count > 0 ? stack[stack.Count - 1].Owner : null);
                _lineOwner[i] = owner;
                if (opens > 0) { stack.Add((i, owner)); previous = null; }
                else if (opens < 0) { if (stack.Count > 0) { previous = stack[stack.Count - 1].Owner; stack.RemoveAt(stack.Count - 1); } }
                else previous = owner;
            }
        }

        /// <summary>The union element a line carries the id of (a border companion counts as its element).</summary>
        private string? IdOf(string line)
        {
            var at = line.IndexOf(" id=", StringComparison.Ordinal);
            if (at < 0) return null;
            var end = at + 4;
            while (end < line.Length && line[end] != ' ') end++;
            var id = line.Substring(at + 4, end - at - 4);
            if (id.EndsWith(VectorEmitter.BorderIdSuffix, StringComparison.Ordinal)) id = id.Substring(0, id.Length - VectorEmitter.BorderIdSuffix.Length);
            if (_elements.ContainsKey(id)) return id;
            var m = DefName.Match(id);
            return m.Success && _elements.ContainsKey(m.Groups["name"].Value) ? m.Groups["name"].Value : null;
        }

        /// <summary>Which gated roots each union line is drawn under: its group's lines, and the defs its elements mint.</summary>
        private void Own()
        {
            LineOwners();
            var lines = _union.Lines;
            _owners = new List<string>[lines.Length];
            for (var i = 0; i < lines.Length; i++) _owners[i] = new List<string>();

            foreach (var t in _result.Targets)
                foreach (var root in t.Roots)
                {
                    var from = FirstGroup(lines, root);
                    if (from < 0)
                    {
                        // Markup no state reaches - the active tab of a screen that hides the tabs -
                        // is never drawn, in a browser either.
                        if (Showing(t, t.Union.Path[root]) != null)
                            Problem(t, $"the alternative \"{root}\" draws no group of its own, so it cannot be shown and hidden");
                        continue;
                    }
                    var to = Close(lines, from);
                    for (var i = from; i <= to; i++) _owners[i].Add(root);
                }

            // Defs: a line whose def id carries a union element's name belongs to every root above it.
            var depth = 0;
            string? owner = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (depth == 0)
                {
                    if (line.TrimStart().StartsWith("DEFS", StringComparison.Ordinal)) depth = Braces(line);
                    continue;
                }
                if (depth == 1) owner = DefOwner(line.TrimStart());
                if (owner != null)
                    for (var e = owner; e != null; e = Up(e))
                        if (_rootOf.ContainsKey(e) && !_owners[i].Contains(e)) _owners[i].Add(e);
                depth += Braces(line);
            }
        }

        private string? Up(string element)
        {
            foreach (var t in _result.Targets)
                if (t.Union.Parent.TryGetValue(element, out var parent)) return parent;
            // an element without a compile-time name is named after its named ancestor: `x_c3`
            var cut = element.LastIndexOf("_c", StringComparison.Ordinal);
            return cut > 0 ? element.Substring(0, cut) : null;
        }

        /// <summary>The union element a def line was minted by, from its normalised id.</summary>
        private string? DefOwner(string trimmed)
        {
            var at = trimmed.IndexOf(" id=", StringComparison.Ordinal);
            if (at < 0) return null;
            var end = trimmed.IndexOf(' ', at + 4);
            var id = end < 0 ? trimmed.Substring(at + 4) : trimmed.Substring(at + 4, end - at - 4);
            var m = DefName.Match(id);
            if (!m.Success) return null;
            var name = m.Groups["name"].Value;
            return _elements.ContainsKey(name) ? name : null;
        }

        private static readonly Regex DefName = new(@"^(tw|scroll|clip|rad|grad|cut|tg|svg|cg|cclip|conic|stripes|bimg|ka|cpath|mask)(?<name>.+)_\d+$", RegexOptions.Compiled);

        private static int Braces(string line)
        {
            var n = 0;
            var quoted = false;
            foreach (var c in line)
            {
                if (c == '"') quoted = !quoted;
                else if (!quoted && c == '{') n++;
                else if (!quoted && c == '}') n--;
            }
            return n;
        }

        /// <summary>The first group line carrying this id: the outermost wrapper of that element.</summary>
        private static int FirstGroup(string[] lines, string id)
        {
            var tail = " id=" + id + " {";
            for (var i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (l.EndsWith(tail, StringComparison.Ordinal) && l.TrimStart().StartsWith("G ", StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static int Close(string[] lines, int from)
        {
            var depth = 0;
            for (var i = from; i < lines.Length; i++)
            {
                depth += Braces(lines[i]);
                if (depth <= 0) return i;
            }
            return lines.Length - 1;
        }

        /// <summary>
        /// A layout, emitted and matched line for line against the union. Null when it does not
        /// match - said, with the first line that differs.
        /// </summary>
        private Scene? Layout(Config config, string why)
        {
            var key = config.Key();
            if (_emitted.TryGetValue(key, out var cached)) return cached.Map != null ? cached : null;
            var hidden = Hidden(config);
            var scene = Emit(hidden);
            _emitted[key] = scene;

            var kept = new List<int>(scene.Lines.Length);
            for (var i = 0; i < _union.Lines.Length; i++)
            {
                var drop = false;
                foreach (var o in _owners[i]) if (hidden.Contains(o)) { drop = true; break; }
                if (!drop) kept.Add(i);
            }
            var map = Align(scene.Lines, kept, out var at, out var their);
            if (map == null)
            {
                var mine = at < scene.Lines.Length ? scene.Lines[at].Trim() : "(end)";
                var theirs = their >= 0 && their < kept.Count ? _union.Lines[kept[their]].Trim() : "(end)";
                _result.Problems.Add($"{why}: its lines do not match the union's at line {at} - \"{Short(mine)}\" against \"{Short(theirs)}\"");
                return null;
            }
            scene.Map = map;
            if (!_probing) _laid.Add(scene);
            return scene;
        }

        /// <summary>
        /// A layout's line to the union line it is, or null with the first line that is not.
        /// In order, except the defs: those are matched by what they are, since a union merged from
        /// several layouts lists the defs of one layout's copies after another's, and a def is
        /// named, not placed.
        /// </summary>
        private int[]? Align(string[] lines, List<int> kept, out int at, out int k)
        {
            var map = new int[lines.Length];
            k = 0;
            for (at = 0; at < lines.Length; at++, k++)
            {
                // A group the union has with nothing shown inside it, which the layout leaves out:
                // an empty list's scroll box has no height, and a box with none is not drawn.
                while (k + 1 < kept.Count && !Matches(lines[at], _union.Lines[kept[k]])
                       && Braces(_union.Lines[kept[k]]) > 0 && _union.Lines[kept[k + 1]].Trim() == "}") k += 2;
                if (k >= kept.Count || !Matches(lines[at], _union.Lines[kept[k]])) return null;
                map[at] = kept[k];
                if (!lines[at].TrimStart().StartsWith("DEFS", StringComparison.Ordinal) || Braces(lines[at]) <= 0) continue;

                // the defs of both, each by its text, then matched item for item
                var close = Close(lines, at);
                var theirs = new List<int>();
                var depth = Braces(_union.Lines[kept[k]]);
                while (++k < kept.Count && (depth += Braces(_union.Lines[kept[k]])) > 0) theirs.Add(k);
                if (k >= kept.Count) return null;
                var waiting = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
                var theirLines = theirs.ConvertAll(i => _union.Lines[kept[i]]).ToArray();
                foreach (var (from, to) in Items(theirLines, 0, theirLines.Length))
                {
                    var text = Positionless(string.Join("\n", theirLines, from, to - from + 1));
                    if (!waiting.TryGetValue(text, out var q)) waiting[text] = q = new Queue<int>();
                    q.Enqueue(from);
                }
                foreach (var (from, to) in Items(lines, at + 1, close))
                {
                    var text = Positionless(string.Join("\n", lines, from, to - from + 1));
                    if (!waiting.TryGetValue(text, out var q) || q.Count == 0) { at = from; k = -1; return null; }
                    var first = q.Dequeue();
                    for (var i = from; i <= to; i++) map[i] = kept[theirs[first + i - from]];
                }
                foreach (var q in waiting.Values) if (q.Count > 0) { at = close; k = kept.Count; return null; }
                at = close;
                map[at] = kept[k];
            }
            while (k + 1 < kept.Count && Braces(_union.Lines[kept[k]]) > 0 && _union.Lines[kept[k + 1]].Trim() == "}") k += 2;
            return k == kept.Count ? map : null;
        }

        private static string Short(string s) => s.Length <= 100 ? s : s.Substring(0, 99) + "~";

        /// <summary>
        /// A layout's line is the union's line: the same but for where things are, or the same but
        /// for a corner radius the union has and it does not - CSS scales radii down to fit a box,
        /// to nothing for an empty one, and the emitter then leaves the radius out.
        /// </summary>
        private static bool Matches(string mine, string theirs)
        {
            var a = Positionless(mine);
            var b = Positionless(theirs);
            return string.Equals(a, b, StringComparison.Ordinal)
                   || !a.Contains(" rx=", StringComparison.Ordinal) && string.Equals(a, Radius.Replace(b, ""), StringComparison.Ordinal);
        }

        private static readonly Regex Radius = new(@" rx=\$\S+", RegexOptions.Compiled);
        private static readonly Regex Valign = new(@" (valign=\w+|wrap=1)", RegexOptions.Compiled);
        private static readonly Regex PositionalRef = new(@"\$L\d+", RegexOptions.Compiled);
        private static string Positionless(string line) => PositionalRef.Replace(line, "$$L");

        /// <summary>A slot of a layout by its union name: a positional name moves to the union's line.</summary>
        private static string UnionName(Scene scene, string slot)
        {
            if (slot.Length < 2 || slot[0] != 'L' || !char.IsDigit(slot[1]) || scene.Map == null) return slot;
            var i = 1;
            while (i < slot.Length && char.IsDigit(slot[i])) i++;
            var line = int.Parse(slot.Substring(1, i - 1), CultureInfo.InvariantCulture);
            return line < scene.Map.Length ? "L" + scene.Map[line].ToString(CultureInfo.InvariantCulture) + slot.Substring(i) : slot;
        }

        /// <summary>A layout's values under union names.</summary>
        private static Dictionary<string, SceneSlots.Value> Values(Scene scene)
        {
            var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            foreach (var pair in scene.Values) values[UnionName(scene, pair.Key)] = pair.Value;
            return values;
        }

        // ---- layouts -------------------------------------------------------------------------------

        /// <summary>One layout: the driving value, and each other choice's side and list's length.</summary>
        private sealed class Config
        {
            public readonly Dictionary<Target, string?> Drive = new();
            public readonly Dictionary<(Target, int), bool> Sides = new();
            public readonly Dictionary<(Target, int), int> Counts = new();

            public Config Copy()
            {
                var c = new Config();
                foreach (var p in Drive) c.Drive[p.Key] = p.Value;
                foreach (var p in Sides) c.Sides[p.Key] = p.Value;
                foreach (var p in Counts) c.Counts[p.Key] = p.Value;
                return c;
            }

            public string Key()
            {
                var sb = new StringBuilder();
                foreach (var p in Drive) sb.Append(p.Key.Id).Append('=').Append(p.Value ?? "\u0001").Append(';');
                foreach (var p in Sides) sb.Append(p.Key.Item1.Id).Append('?').Append(p.Key.Item2).Append('=').Append(p.Value ? '1' : '0').Append(';');
                foreach (var p in Counts) sb.Append(p.Key.Item1.Id).Append('*').Append(p.Key.Item2).Append('=').Append(p.Value).Append(';');
                return sb.ToString();
            }
        }

        private Config? _base;

        /// <summary>The layout the page opens in: what the page last ran with, else each choice's first side and each list full.</summary>
        private Config Base()
        {
            if (_base != null) return _base;
            var c = new Config();
            foreach (var t in _result.Targets)
            {
                if (t.Drive != null) c.Drive[t] = _probed?.Invoke(t.Id, -1) ?? (t.Drive.Values.Count > 0 ? t.Drive.Values[0] : null);
                foreach (var ch in t.Markup.Choices)
                    if (t.Drive == null || !t.Drive.Tests.ContainsKey(ch.Index))
                        c.Sides[(t, ch.Index)] = _probed?.Invoke(t.Id, -1000 - ch.Index) is not "0";
                foreach (var l in t.Markup.Lists)
                    c.Counts[(t, l.Index)] = int.TryParse(_probed?.Invoke(t.Id, -2000 - l.Index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                        ? Math.Min(n, l.Each.Count) : l.Each.Count;
            }
            return _base = c;
        }

        private static bool Taken(Config c, Target t, int choice)
        {
            if (t.Drive != null && t.Drive.Tests.ContainsKey(choice)) return t.Drive.Side(choice, c.Drive.TryGetValue(t, out var v) ? v : null);
            return !c.Sides.TryGetValue((t, choice), out var s) || s;
        }

        /// <summary>Every gated root a layout does not show.</summary>
        private HashSet<string> Hidden(Config c)
        {
            var hidden = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in _result.Targets)
                foreach (var root in t.Roots)
                    if (!Shown(c, t, t.Union.Path[root])) hidden.Add(root);
            return hidden;
        }

        private static bool Shown(Config c, Target t, (bool List, int Index, int Which)[] path)
        {
            foreach (var (list, index, which) in path)
            {
                if (list) { if (which >= (c.Counts.TryGetValue((t, index), out var n) ? n : 0)) return false; }
                else if (Taken(c, t, index) != (which == 1)) return false;
            }
            return true;
        }

        /// <summary>
        /// A layout in which a given path is drawn: the base, with the drive and the sides and
        /// lengths along the path set so that everything on it is shown. Null when no value of the
        /// drive shows it - markup no state of the page can reach.
        /// </summary>
        private Config? Showing(Target t, (bool List, int Index, int Which)[] path)
        {
            var c = Base().Copy();
            if (t.Drive != null)
            {
                var needed = new List<(int Choice, bool Side)>();
                foreach (var (list, index, which) in path)
                    if (!list && t.Drive.Tests.ContainsKey(index)) needed.Add((index, which == 1));
                if (needed.Count > 0)
                {
                    var start = c.Drive.TryGetValue(t, out var now) ? now : null;
                    var candidates = new List<string?> { start };
                    foreach (var v in t.Drive.Values) if (v != start) candidates.Add(v);
                    if (start != null) candidates.Add(null);
                    var found = false;
                    foreach (var v in candidates)
                        if (needed.TrueForAll(n => t.Drive.Side(n.Choice, v) == n.Side)) { c.Drive[t] = v; found = true; break; }
                    if (!found) return null;
                }
            }
            foreach (var (list, index, which) in path)
            {
                if (list) { if (c.Counts[(t, index)] <= which) c.Counts[(t, index)] = which + 1; }
                else if (t.Drive == null || !t.Drive.Tests.ContainsKey(index)) c.Sides[(t, index)] = which == 1;
            }
            return c;
        }

        // ---- states --------------------------------------------------------------------------------

        /// <summary>The layouts a target's choices and lists pick between, and what each one draws.</summary>
        private void States(Target t)
        {
            // The drive: one layout per value it is compared with, and one for none of them - each
            // laid out once per round, so every copy of an expanded element is drawn somewhere.
            if (t.Drive != null)
            {
                var configs = new List<(string Name, List<Config> Layouts)>();
                foreach (var value in t.Drive.Values) configs.Add((value, Rounded(t, value, set: true)));
                configs.Add((OtherName, Rounded(t, null, set: true)));
                var roots = new List<string>();
                foreach (var ch in t.Markup.Choices)
                    if (t.Drive.Tests.ContainsKey(ch.Index)) { roots.AddRange(t.Union.Sides[ch.Index].Then); roots.AddRange(t.Union.Sides[ch.Index].Else); }
                if (State(t, "drive", configs, roots) is { } b) b.Other = OtherName;
            }
            else
            {
                // No drive: the base, once per round, so the copies of expanded elements have values.
                foreach (var c in Rounded(t, null, set: false)) Layout(c, $"\"{t.Id}\" at rest");
            }

            foreach (var ch in t.Markup.Choices)
            {
                if (t.Drive != null && t.Drive.Tests.ContainsKey(ch.Index)) continue;
                var key = "choice#" + ch.Index.ToString(CultureInfo.InvariantCulture);
                var then = new List<string>(t.Union.Sides[ch.Index].Then);
                var otherwise = new List<string>(t.Union.Sides[ch.Index].Else);
                if (ch.Expanded)
                {
                    // One element per value: the copies lay out alike, so which one shows is the
                    // whole state. What each copy draws comes from the rounds above.
                    var on = new CompiledPage.StateValues { Name = "1" };
                    var off = new CompiledPage.StateValues { Name = "0" };
                    foreach (var r in then) { on.Numbers.Add((r + "_v", 1)); off.Numbers.Add((r + "_v", 0)); }
                    foreach (var r in otherwise) { on.Numbers.Add((r + "_v", 0)); off.Numbers.Add((r + "_v", 1)); }
                    var gate = new Binding { Key = key };
                    gate.States.Add(on);
                    gate.States.Add(off);
                    t.Bindings.Add(gate);
                    continue;
                }
                // Any other choice, laid out where its own place is shown.
                var at = Showing(t, PathOf(t, false, ch.Index));
                if (at == null) { Problem(t, $"choice {ch.Index} sits where no state of the page shows it"); continue; }
                var yes = at.Copy(); yes.Sides[(t, ch.Index)] = true;
                var no = at.Copy(); no.Sides[(t, ch.Index)] = false;
                var roots = new List<string>(then);
                roots.AddRange(otherwise);
                State(t, key, new List<(string, List<Config>)> { ("1", new List<Config> { yes }), ("0", new List<Config> { no }) }, roots);
            }

            // Every list, at each length it can have.
            foreach (var l in t.Markup.Lists)
            {
                var at = Showing(t, PathOf(t, true, l.Index));
                if (at == null) { Problem(t, $"list {l.Index} sits where no state of the page shows it"); continue; }
                var configs = new List<(string, List<Config>)>();
                for (var n = 0; n <= l.Each.Count; n++)
                {
                    var c = at.Copy();
                    c.Counts[(t, l.Index)] = n;
                    configs.Add((n.ToString(CultureInfo.InvariantCulture), new List<Config> { c }));
                }
                var roots = new List<string>();
                foreach (var row in t.Union.Rows[l.Index]) roots.AddRange(row);
                State(t, "rows#" + l.Index.ToString(CultureInfo.InvariantCulture), configs, roots);
            }
        }

        /// <summary>
        /// The base layout with a driving value (when <paramref name="set"/>), once per round of
        /// copies, so each copy of an element written once per value is laid out where it shows.
        /// </summary>
        private List<Config> Rounded(Target t, string? value, bool set)
        {
            var list = new List<Config>();
            foreach (var round in _rounds.Count > 0 ? _rounds : new List<Dictionary<(Target, int), bool>> { new() })
            {
                var c = Base().Copy();
                if (set) c.Drive[t] = value;
                foreach (var ch in t.Markup.Choices)
                    if (Copied(t, ch.Index)) c.Sides[(t, ch.Index)] = Side(round, t, ch.Index);
                list.Add(c);
            }
            return list;
        }

        /// <summary>
        /// The choices and rows around a choice or a list: the path of anything inside it, up to
        /// its own entry.
        /// </summary>
        private static (bool List, int Index, int Which)[] PathOf(Target t, bool isList, int index)
        {
            foreach (var p in t.Union.Path.Values)
                for (var i = 0; i < p.Length; i++)
                    if (p[i].List == isList && p[i].Index == index)
                        return p[..i];
            return Array.Empty<(bool, int, int)>();
        }

        /// <summary>
        /// One state binding: each state's layouts emitted and merged, and every slot that differs
        /// between the states carried by every state that draws it, with the gates of the
        /// alternatives it switches (read from its first layout).
        /// </summary>
        private Binding? State(Target t, string key, List<(string Name, List<Config> Layouts)> configs, List<string> roots)
        {
            var laid = new List<(string Name, Config Config, Dictionary<string, SceneSlots.Value> Values)>();
            foreach (var (name, layouts) in configs)
            {
                var merged = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
                foreach (var config in layouts)
                {
                    var scene = Layout(config, $"\"{t.Id}\" {key} = {Printable(name)}");
                    if (scene == null) return null;
                    foreach (var pair in Values(scene)) if (!merged.ContainsKey(pair.Key)) merged[pair.Key] = pair.Value;
                }
                laid.Add((name, layouts[0], merged));
            }

            var varies = Varies(laid.ConvertAll(l => l.Values));
            var binding = new Binding { Key = key };
            foreach (var (name, config, values) in laid)
            {
                var state = new CompiledPage.StateValues { Name = name };
                var hidden = Hidden(config);
                foreach (var root in roots) state.Numbers.Add((root + "_v", hidden.Contains(root) ? 0 : 1));
                Carry(state, varies, values);
                binding.States.Add(state);
            }
            t.Bindings.Add(binding);
            return binding;
        }

        /// <summary>Every slot whose value differs between the layouts that draw it.</summary>
        private static HashSet<string> Varies(List<Dictionary<string, SceneSlots.Value>> layouts)
        {
            var first = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            var varies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var values in layouts)
                foreach (var pair in values)
                {
                    if (!first.TryGetValue(pair.Key, out var was)) first[pair.Key] = pair.Value;
                    else if (!Same(was, pair.Value)) varies.Add(pair.Key);
                }
            return varies;
        }

        private static void Carry(CompiledPage.StateValues state, HashSet<string> slots, Dictionary<string, SceneSlots.Value> values)
        {
            foreach (var slot in slots)
            {
                if (!values.TryGetValue(slot, out var v)) continue;
                if (v.IsNumber) state.Numbers.Add((slot, v.Number));
                else state.Text.Add((slot, v.Text ?? string.Empty));
            }
        }

        private static bool Same(SceneSlots.Value a, SceneSlots.Value b)
            => a.IsNumber == b.IsNumber && (a.IsNumber ? Math.Abs(a.Number - b.Number) <= 0.01f : string.Equals(a.Text, b.Text, StringComparison.Ordinal));

        /// <summary>The state for a driving value that matches none of the literals the page compares it with.</summary>
        internal const string OtherName = "\u0001other";
        private static string Printable(string name) => name == OtherName ? "(none of them)" : "\"" + name + "\"";

        // ---- values ------------------------------------------------------------------------------

        /// <summary>Holes already said to reach nothing, so the search that follows does not say it twice.</summary>
        private readonly HashSet<(Target, int)> _said = new();

        /// <summary>Holes being laid out with other values to see what they write.</summary>
        private Dictionary<(Target, int), string>? _override;

        /// <summary>What a hole is written with in the union: an override while probing, else what the page draws.</summary>
        private string Fill(Target t, Markup.Hole h, Markup.Place place)
            => _override != null && _override.TryGetValue((t, h.Index), out var v) ? v : Real(t, h, place);

        /// <summary>
        /// The union built again with some holes given other values, and one layout of it. The names
        /// are the union's own, so the layout lines up with it like any other.
        /// </summary>
        /// <remarks>
        /// Rebuilt, not restyled in place: the renderer turns a percentage width into pixels when it
        /// cascades, and a re-cascade of the same element kept the old pixels - `width:55.5%` measured
        /// as the 37% it replaced, and every bar read as a value that changed nothing.
        /// </remarks>
        private Scene? Probe(Config at, Dictionary<(Target, int), string>? values, string why)
        {
            // Only the elements whose attributes hold a moved value are built again, in place: a
            // whole union is a quarter of a second, and a compile lays out hundreds of these.
            var touched = new List<(Target T, string Element)>();
            if (values != null)
                foreach (var (t, hole) in values.Keys)
                    if (t.Union.Holes[hole].Element is { } e && !touched.Contains((t, e))) touched.Add((t, e));
            _override = values;
            try
            {
                foreach (var (t, e) in touched) Rebuild(t, e);
                if (touched.Count > 0) Name();
                _probing = true;
                _emitted.Remove(at.Key());
                return Layout(at, why);
            }
            finally
            {
                _emitted.Remove(at.Key());
                _probing = false;
                _override = null;
                foreach (var (t, e) in touched) Rebuild(t, e);
                if (touched.Count > 0) Name();
            }
        }

        /// <summary>
        /// One union element built again where it stands, its attributes written from their pieces
        /// with the current fill. Rebuilt rather than re-cascaded: the renderer turns a percentage
        /// into pixels when it cascades, and a re-cascade kept the old pixels.
        /// </summary>
        private void Rebuild(Target t, string element)
        {
            if (!_elements.TryGetValue(element, out var ve) || ve.parent is not { } parent) return;
            if (!_built.NodeOf.TryGetValue(ve, out var node) || !_built.NodeOf.TryGetValue(parent, out var parentNode)) return;
            Builds++;
            TimeBuild.Start();
            try
            {
                foreach (var pair in t.Union.Attrs)
                    if (pair.Key.Element == element && t.Union.Attribute(element, pair.Key.Attribute, h => Fill(t, t.Markup.Holes[h], t.Union.Holes[h])) is { } text)
                        node.Attributes[pair.Key.Attribute] = text;
                // An id for as long as it takes to put the copy where the original stands: an
                // element without one is appended at the end of its parent.
                // Not a `__` name: those are the renderer's own, and are left out of the markup.
                const string here = "markup-rebuild-here";
                node.Attributes["id"] = here;
                var html = HtmlRenderer.ToHtml(node, outer: true);
                node.Attributes.Remove("id");
                HtmlRenderer.InsertFragment(parent, parentNode, html, ve.name, _built);
                HtmlRenderer.Remove(ve, _built);
                if (_built.ById.TryGetValue(here, out var made))
                {
                    _built.ById.Remove(here);
                    if (_built.NodeOf.TryGetValue(made, out var madeNode)) madeNode.Attributes.Remove("id");
                    made.name = string.Empty;
                    // A grid places its items from callbacks it registered on the items it had when
                    // it was attached: a rebuilt item is new to it and would sit in the flow at the
                    // grid's full width. Its re-cascade hook places afresh, so the item calls that.
                    if (_built.Grids.Contains(parent))
                    {
                        void Place()
                        {
                            foreach (var (owner, act) in _built.AfterRecascade.ToArray())
                                if (owner == parent) act();
                        }
                        made.RegisterCallback<GeometryChangedEvent>(_ => Place());
                        Place();
                    }
                }
                Attach(_built);
                // A mixed calc() is applied when its parent's size changes, and the parent of a rebuilt
                // bar has not changed: without this the copy kept the percent alone, without its `- 12px`.
                if (made != null)
                    foreach (var (owner, act) in _built.AfterRecascade.ToArray())
                        for (var e = owner; e != null; e = e.parent)
                            if (e == made) { act(); break; }
                Animate(t);
                Rename(parent);
            }
            finally { TimeBuild.Stop(); }
        }

        /// <summary>
        /// Every number the page writes into a style: laid out at two values, and the slots that moved
        /// give the line from the value to the slot.
        /// </summary>
        /// <remarks>
        /// `width:62.3%` does not reach the scene as 62.3 - it is a width in scene units, of whatever
        /// the parent measures in the layout that shows it. So it is found by moving it: two points
        /// make a slope and an offset, which is all the chip needs, and a third says whether it is a
        /// line at all. A slot that moves out of proportion - text that wraps, a box at its minimum -
        /// needs the layout engine a compiled page does not have: refused, by name.
        /// </remarks>
        private void Numbers()
        {
            var groups = new List<(Target T, List<Markup.Hole> Holes, string Element, double V0, double Step, Config At)>();
            foreach (var t in _result.Targets)
            {
                // The same expression in the same scope is one value written twice - a hard stop is
                // `C 0 62%, transparent 62%` - and moving one without the other draws another gradient.
                var byKey = new Dictionary<string, List<Markup.Hole>>(StringComparer.Ordinal);
                foreach (var h in t.Markup.Holes)
                {
                    var place = t.Union.Holes[h.Index];
                    if (place.Attribute != "style" || place.Element == null) continue;
                    if (t.Markup.Enumerate(h.Value) is { Count: > 1 }) continue;          // a variant, not a number
                    if (IsColourProperty(place.Property)) continue;
                    var key = place.Element + "|" + Scope(h.Value) + "|" + t.Markup.Text(h.Value);
                    if (!byKey.TryGetValue(key, out var g)) byKey[key] = g = new List<Markup.Hole>();
                    g.Add(h);
                }
                foreach (var holes in byKey.Values)
                {
                    var h = holes[0];
                    var place = t.Union.Holes[h.Index];
                    var value = Real(t, h, place);
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v0))
                    {
                        if (!IsColour(value))
                            Problem(t, $"hole {h.Index} ({t.Markup.Text(h.Value)}) writes `{place.Property}` with a value that is neither a number nor a colour, and no fixed set of them is visible to lay out");
                        continue;
                    }
                    if (Showing(t, t.Union.Path["#" + h.Index.ToString(CultureInfo.InvariantCulture)]) is not { } at) continue;
                    groups.Add((t, holes, place.Element!, v0, Math.Abs(v0) > 20 ? v0 * 0.5 : 20, at));
                }
            }

            // One value at a time, at two distances: whatever moves moved because of it, and a slot
            // that moves twice as far for twice the change is a line the chip can draw.
            foreach (var g in groups)
            {
                // Measured from the element built again at its own value, not from the layout
                // before: building an element again settles text a pixel differently.
                if (Moved(g, 0) is not { } zero) continue;
                var was = Values(zero);
                var near = Moved(g, g.Step);
                var far = near == null ? null : Moved(g, 2 * g.Step);
                if (near == null || far == null) continue;
                var farther = Values(far);
                var slots = new HoleSlots { Kind = Kind.Number };
                var bent = new List<string>();
                foreach (var pair in Values(near))
                {
                    if (!pair.Value.IsNumber || !was.TryGetValue(pair.Key, out var w) || !w.IsNumber
                        || !farther.TryGetValue(pair.Key, out var w2) || !w2.IsNumber) continue;
                    var d1 = pair.Value.Number - w.Number;
                    var d2 = w2.Number - w.Number;
                    if (Math.Abs(d1) <= 0.05 && Math.Abs(d2) <= 0.05) continue;
                    // The layout runs on whole pixels: a neighbour that settles one pixel over and
                    // stays there whatever the value is rounding, not something the value places.
                    if (Math.Abs(d1 - d2) < 0.05 && Math.Abs(d1) <= 1.01) continue;
                    // Three whole-pixel readings of a straight line are each half a pixel out at
                    // most, which bends it by up to two.
                    if (Math.Abs(d2 - 2 * d1) > 2.01)
                    {
                        bent.Add(bent.Count > 0 ? pair.Key : $"{pair.Key} {w.Number:0.#}, {pair.Value.Number:0.#}, {w2.Number:0.#} at {g.V0:0.#}, {g.V0 + g.Step:0.#}, {g.V0 + 2 * g.Step:0.#}");
                        continue;
                    }
                    // The slope from the two far points and the offset through all three, which
                    // halves what the rounding costs.
                    var scale = d2 / (2 * g.Step);
                    var bias = (w.Number + pair.Value.Number + w2.Number) / 3 - scale * (g.V0 + g.Step);
                    slots.To.Add((pair.Key, scale, bias));
                }
                var text = g.T.Markup.Text(g.Holes[0].Value);
                if (bent.Count > 0)
                    Problem(g.T, $"hole {g.Holes[0].Index} ({text}) moves {bent.Count} slot(s) - {string.Join(", ", bent.GetRange(0, Math.Min(4, bent.Count)))} - by amounts not in proportion to it, so where they land needs the layout engine");
                if (slots.To.Count == 0)
                {
                    Problem(g.T, $"hole {g.Holes[0].Index} ({text}) changes nothing that is drawn");
                    foreach (var h in g.Holes) _said.Add((g.T, h.Index));
                    continue;
                }
                foreach (var h in g.Holes) g.T.Holes[h.Index] = slots;
            }

            Scene? Moved((Target T, List<Markup.Hole> Holes, string Element, double V0, double Step, Config At) g, double by)
            {
                var moved = new Dictionary<(Target, int), string>();
                foreach (var h in g.Holes) moved[(g.T, h.Index)] = (g.V0 + by).ToString("0.###", CultureInfo.InvariantCulture);
                return Probe(g.At, moved, $"\"{g.T.Id}\" hole {g.Holes[0].Index} moved by {by.ToString("0.###", CultureInfo.InvariantCulture)}");
            }
        }

        /// <summary>The scope a hole's expression is read in, so the same text in two helpers is not one value.</summary>
        private static string Scope(Markup.Term t)
            => t is Markup.Src { Env: { } env } ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(env).ToString(CultureInfo.InvariantCulture) : "-";

        /// <summary>Whether a slot is drawn by an element or by something inside it.</summary>
        private bool Within(string slot, string element)
        {
            for (var e = OwnerOf(slot); e != null; e = Up(e))
                if (e == element) return true;
            return false;
        }

        /// <summary>The union element a slot belongs to.</summary>
        private string? OwnerOf(string slot)
        {
            // A positional slot is a line with no id of its own - a wrapper group, a pseudo-element,
            // a second shape - and belongs to whoever that line is drawn for.
            if (slot.Length > 1 && slot[0] == 'L' && char.IsDigit(slot[1]))
            {
                var i = 1;
                while (i < slot.Length && char.IsDigit(slot[i])) i++;
                var line = int.Parse(slot.Substring(1, i - 1), CultureInfo.InvariantCulture);
                return line < _lineOwner.Length ? _lineOwner[line] : null;
            }
            var m = DefSlot.Match(slot);
            if (m.Success && _elements.ContainsKey(m.Groups["name"].Value)) return m.Groups["name"].Value;
            string? best = null;
            foreach (var name in _elements.Keys)
                if (slot.StartsWith(name, StringComparison.Ordinal) && (slot.Length == name.Length || slot[name.Length] == '_')
                    && (best == null || name.Length > best.Length)) best = name;
            return best;
        }

        private static readonly Regex DefSlot = new(@"^(tw|scroll|clip|rad|grad|cut|tg|svg|cg|cclip|conic|stripes|bimg|ka|cpath|mask)(?<name>.+?)_\d+_", RegexOptions.Compiled);

        /// <summary>
        /// Every hole drawn from a fixed set of values that is not a colour: each value laid out, and
        /// what it changes carried as a state picked by the value itself. A value that changes the
        /// SHAPE of what is drawn - a shadow appearing, an animation starting - cannot be a state on
        /// one shape, so its element is marked to be written once per value.
        /// </summary>
        private void Variants()
        {
            foreach (var t in _result.Targets)
                foreach (var h in t.Markup.Holes)
                {
                    var place = t.Union.Holes[h.Index];
                    if (place.Attribute is not ("style" or "class") || place.Element == null) continue;
                    var alts = t.Markup.Enumerate(h.Value);
                    if (alts == null || alts.Count < 2) continue;
                    if (IsColourProperty(place.Property) || alts.TrueForAll(IsColour)) continue;
                    if (Showing(t, t.Union.Path["#" + h.Index.ToString(CultureInfo.InvariantCulture)]) is not { } at) continue;

                    var laid = new List<(string Name, Dictionary<string, SceneSlots.Value> Values)>();
                    var shaped = false;
                    foreach (var alt in alts)
                    {
                        var scene = Probe(at, new Dictionary<(Target, int), string> { [(t, h.Index)] = alt }, $"\"{t.Id}\" hole {h.Index} = \"{Short(alt)}\"");
                        if (scene == null) { shaped = true; break; }
                        laid.Add((alt, Values(scene)));
                    }
                    if (shaped) { t.Expand.Add(h.Index); continue; }
                    // Everything that differs is carried, the element's neighbours included: a padding
                    // that pushes the rows below it down is what the state is for.
                    var varies = Varies(laid.ConvertAll(l => l.Values));
                    if (varies.Count == 0) continue;                                    // draws the same whatever it is
                    var states = new List<CompiledPage.StateValues>();
                    foreach (var (name, values) in laid)
                    {
                        var state = new CompiledPage.StateValues { Name = name };
                        Carry(state, varies, values);
                        states.Add(state);
                    }
                    t.Holes[h.Index] = new HoleSlots { Kind = Kind.Variant, States = states };
                }
        }

        // ---- text and colour, found by what comes back -------------------------------------------

        /// <summary>
        /// Text and colour holes: the union built again with a distinctive value in each, emitted
        /// with everything shown, and each value found where it landed.
        /// </summary>
        private void Discover()
        {
            var sought = new Dictionary<Target, HashSet<int>>();
            foreach (var t in _result.Targets)
            {
                var set = new HashSet<int>();
                foreach (var h in t.Markup.Holes)
                {
                    if (t.Holes.ContainsKey(h.Index) || _said.Contains((t, h.Index))) continue;
                    var place = t.Union.Holes[h.Index];
                    if (place.Attribute == null || place.Attribute == "style") set.Add(h.Index);
                }
                sought[t] = set;
                var union = t.Markup.Write(t.Id, (h, place) =>
                    !set.Contains(h.Index) ? Real(t, h, place)
                    : place.Attribute == null ? Sentinel(h.Index)
                    : ColourHole(t, h, place) ? ColourSentinel(h.Index)
                    : Real(t, h, place));
                Build(t, union.Html);
            }
            Name();
            var scene = UnionScene();
            // A positional name means a line, and the lines are only the same lines when the two
            // unions emit the same template; otherwise only slots named after an element count.
            // How a label wraps and sits is left out: six digits where the page draws "HOLD ON" do not
            // wrap (the emitter only wraps text with a space) and sit differently, without moving a line.
            var sameLines = scene.Lines.Length == _union.Lines.Length;
            for (var i = 0; sameLines && i < scene.Lines.Length; i++)
                sameLines = string.Equals(Valign.Replace(Positionless(scene.Lines[i]), ""), Valign.Replace(Positionless(_union.Lines[i]), ""), StringComparison.Ordinal);

            foreach (var pair in scene.Values)
            {
                var slot = pair.Key;
                if (!sameLines && slot.Length > 1 && slot[0] == 'L' && char.IsDigit(slot[1])) continue;
                var v = pair.Value;
                if (v.IsNumber)
                {
                    if (HoleOf(v.Number) is var hole && hole >= 0) Land(hole, slot, Kind.Text);
                    continue;
                }
                if (v.Text is not { Length: > 0 } text) continue;
                if (ColourHoleOf(text) is var colour && colour >= 0) { Land(colour, slot, Kind.Colour); continue; }
                Pieces(slot, text);
            }

            foreach (var t in _result.Targets)
                foreach (var h in sought[t])
                {
                    if (t.Holes.ContainsKey(h)) continue;
                    // A colour drawn from a fixed set that reached no slot is inside something the
                    // scene reads once - a gradient's stops, a shadow - and so is a shape per value.
                    if (t.Union.Holes[h].Attribute == "style" && t.Markup.Enumerate(t.Markup.Holes[h].Value) is { Count: > 1 })
                    {
                        t.Expand.Add(h);
                        continue;
                    }
                    Problem(t, $"hole {h} ({t.Markup.Text(t.Markup.Holes[h].Value)}) reaches no slot, so it would never be drawn");
                }

            void Land(int hole, string slot, Kind kind)
            {
                foreach (var t in _result.Targets)
                {
                    if (!sought[t].Contains(hole)) continue;
                    if (!t.Holes.TryGetValue(hole, out var hs)) t.Holes[hole] = hs = new HoleSlots { Kind = kind };
                    if (kind == Kind.Colour) hs.Colours ??= Colours(t, hole);
                    hs.To.Add((slot, 1, 0));
                    return;
                }
            }

            void Pieces(string slot, string text)
            {
                // Every sentinel in this text, with the literal text around each.
                var pieces = new List<(string? Text, int Hole, bool Colour)>();
                var from = 0;
                var i = 0;
                while (i < text.Length)
                {
                    if (text[i] == '#' && i + 7 <= text.Length && ColourHoleOf(text.Substring(i, 7)) is var ch && ch >= 0)
                    {
                        if (i > from) pieces.Add((text.Substring(from, i - from), -1, false));
                        pieces.Add((null, ch, true));
                        i += 7;
                        from = i;
                        continue;
                    }
                    if (char.IsDigit(text[i]))
                    {
                        var end = i;
                        while (end < text.Length && char.IsDigit(text[end])) end++;
                        if (end - i == 6 && HoleOf(double.Parse(text.Substring(i, 6), CultureInfo.InvariantCulture)) is var hole && hole >= 0)
                        {
                            if (i > from) pieces.Add((text.Substring(from, i - from), -1, false));
                            pieces.Add((null, hole, false));
                            from = end;
                        }
                        i = end;
                        continue;
                    }
                    i++;
                }
                if (pieces.Count == 0) return;
                if (from < text.Length) pieces.Add((text.Substring(from), -1, false));
                var first = pieces.Find(p => p.Hole >= 0).Hole;
                foreach (var t in _result.Targets)
                {
                    if (!sought[t].Contains(first)) continue;
                    var label = new Label { Slot = slot, Transform = Transform(slot) };
                    label.Pieces.AddRange(pieces);
                    t.Labels.Add(label);
                    foreach (var p in pieces)
                        if (p.Hole >= 0 && !t.Holes.ContainsKey(p.Hole))
                            t.Holes[p.Hole] = new HoleSlots
                            {
                                Kind = p.Colour ? Kind.Colour : Kind.Text,
                                Label = t.Labels.Count - 1,
                                Colours = p.Colour ? Colours(t, p.Hole) : null,
                            };
                    return;
                }
            }
        }

        /// <summary>Whether a hole in a style carries a colour: by its property, or by what it is written with.</summary>
        private bool ColourHole(Target t, Markup.Hole h, Markup.Place place)
            => IsColourProperty(place.Property)
               || t.Markup.Enumerate(h.Value) is { Count: > 0 } alts && alts.TrueForAll(IsColour)
               || IsColour(Real(t, h, place));

        /// <summary>`uppercase` on the label a slot draws, which the chunk has to repeat on what it writes.</summary>
        private string? Transform(string slot)
        {
            if (OwnerOf(slot) is not { } owner || !_elements.TryGetValue(owner, out var ve)) return null;
            return _built.CssOf(ve).TryGetValue("text-transform", out var tt)
                ? tt.Trim() switch { "uppercase" => "upper", "lowercase" => "lower", _ => null }
                : null;
        }

        /// <summary>
        /// Every colour a colour hole can be written with, as the scene's hex, resolved where the hole
        /// is: its alternatives when it has a fixed set, else every colour-shaped literal in the page.
        /// </summary>
        private Dictionary<string, string> Colours(Target t, int hole)
        {
            var place = t.Union.Holes[hole];
            HtmlNode? scope = null;
            if (place.Element != null && _elements.TryGetValue(place.Element, out var ve)) _built.NodeOf.TryGetValue(ve, out scope);
            if (scope == null && _built.ById.TryGetValue(t.Id, out var tve)) _built.NodeOf.TryGetValue(tve, out scope);
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var v in t.Markup.Enumerate(t.Markup.Holes[hole].Value) ?? t.Markup.Literals())
            {
                var resolved = v;
                // Until no var() is left: `--live: var(--steel-300)` resolves to another var(), and a
                // colour left unread here reached the console as the text `var(--live)`.
                for (var depth = 0; scope != null && depth < 8 && resolved.IndexOf("var(", StringComparison.Ordinal) >= 0; depth++)
                    resolved = HtmlRenderer.ResolveVars(resolved, scope);
                if (StyleApplier.TryColor(resolved.Trim(), out var colour)) table[v] = Hex(colour);
            }
            return table;
        }

        // ---- the structure --------------------------------------------------------------------------

        /// <summary>
        /// The union's template with a gate on every alternative, and the values it opens with: the
        /// page's first layout, and for anything that layout does not draw, the union's own.
        /// </summary>
        private void Structure()
        {
            // The real union again: discovery left the sentinel one in place.
            foreach (var t in _result.Targets) Build(t, t.Union.Html);
            Name();
            _emitted.Clear();

            // What each slot opens with: the page's first layout, then - for what that layout does not
            // draw - the first layout that does. Never the union's own positions: the union is laid
            // out with every alternative on top of the others, and a slot no state writes (it is the
            // same in every layout that shows it) keeps its opening value for ever.
            var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            var layouts = new List<Scene>();
            if (Layout(Base(), "the page's first layout") is { } first) layouts.Add(first);
            foreach (var scene in _laid) if (scene.Map != null) layouts.Add(scene);
            foreach (var scene in layouts)
                foreach (var pair in Values(scene))
                    if (!values.ContainsKey(pair.Key)) values[pair.Key] = pair.Value;
            foreach (var pair in _union.Values)
                if (!values.ContainsKey(pair.Key)) values[pair.Key] = pair.Value;

            var lines = (string[])_union.Lines.Clone();
            var hidden = Hidden(Base());
            foreach (var t in _result.Targets)
                foreach (var root in t.Roots)
                {
                    var at = FirstGroup(lines, root);
                    if (at < 0) continue;
                    var tail = " id=" + root + " {";
                    lines[at] = lines[at].Substring(0, lines[at].Length - tail.Length) + " v=$" + root + "_v" + tail;
                    values[root + "_v"] = new SceneSlots.Value(hidden.Contains(root) ? 0f : 1f);
                }
            _result.Template = string.Join("\n", lines);
            foreach (var pair in values) _result.Values[pair.Key] = pair.Value;
        }

        private void Problem(Target t, string what)
        {
            // A probe rebuilds the union, and a rebuilt element says what it says again.
            var line = $"\"{t.Id}\": {what}";
            if (!_result.Problems.Contains(line)) _result.Problems.Add(line);
        }
    }

    /// <summary>A colour as the emitter writes it: `#RRGGBB`, with `AA` when it is not opaque.</summary>
    internal static string Hex(Color c)
    {
        var sb = new StringBuilder(9).Append('#');
        Byte(c.r); Byte(c.g); Byte(c.b);
        if (Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f) < 255) Byte(c.a);
        return sb.ToString();

        void Byte(float channel)
        {
            var v = Mathf.RoundToInt(Mathf.Clamp01(channel) * 255f);
            const string hex = "0123456789ABCDEF";
            sb.Append(hex[(v >> 4) & 0xF]).Append(hex[v & 0xF]);
        }
    }

    private static readonly HashSet<string> ColourProperties = new(StringComparer.Ordinal)
    {
        "color", "background-color", "border-color", "border-top-color", "border-right-color", "border-bottom-color",
        "border-left-color", "outline-color", "fill", "stroke", "caret-color", "accent-color", "text-decoration-color",
        "column-rule-color", "stop-color",
    };

    internal static bool IsColourProperty(string? property) => property != null && ColourProperties.Contains(property);

    /// <summary>Whether a value written into a style is a colour: `var(--x)`, a name, a hex, a colour function.</summary>
    internal static bool IsColour(string value)
    {
        var v = value.Trim();
        if (v.StartsWith("var(", StringComparison.Ordinal)) return true;
        return v.Length > 0 && !char.IsDigit(v[0]) && v != "none" && StyleApplier.TryColor(v, out _);
    }
}

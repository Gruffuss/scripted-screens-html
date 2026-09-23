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
        /// elements written once per value (<see cref="Markup.Expand"/>).
        /// </summary>
        public readonly HashSet<int> Expand = new();
        /// <summary>
        /// Those of <see cref="Expand"/> seen to change shape by laying their values out, which a copy
        /// of the element carries with it; the others were only not found in the scene.
        /// </summary>
        public readonly HashSet<int> Inherit = new();
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
    /// <param name="scout">
    /// A first compile: its values and discovery come before its states, and it stops there when an
    /// element has to be written once per value - the markup changes, and everything else with it.
    /// </param>
    internal static Result Compile(HtmlRenderer.Result built, Panel panel,
                                   IReadOnlyList<(string Id, Markup Markup)> writes,
                                   Func<string, int, string?>? probed = null, bool scout = false)
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
        // What it gates is named for this compile only: a name left behind by an earlier one - of the
        // same page before an element was written once per value - wrapped that element in a group
        // with slots of its own, so the structure depended on how many times the page was compiled.
        var wasNamed = new HashSet<string>(built.NamedGroups, StringComparer.Ordinal);
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
            new Compiler(built, panel, result, probed, scout).Run();
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Problems.Add("the page stopped wanting its compile");
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
            built.NamedGroups.Clear();
            built.NamedGroups.UnionWith(wasNamed);
            // Put back, always, and checked: compiling mutates the live page, and a console drawing
            // sentinels where its readings belong is the failure this must never leave behind.
            lock (PageCompiler.Gate)
            {
                foreach (var (ve, node, html) in pristine)
                {
                    ve.Clear();
                    node.Children.Clear();
                    HtmlRenderer.AppendFragment(ve, node, html, built);
                }
                Attach(built);
                panel.Layout(panel.Width, panel.Height);
            }
            foreach (var (ve, node, html) in pristine)
                if (!string.Equals(HtmlRenderer.ToHtml(node, outer: false, keepIds: true), html, StringComparison.Ordinal))
                    ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: \"{ve.name}\" could not be put back after compiling its markup. This is a compiler bug, not a fault in the page.");
        }
    }

    /// <summary>A compile no longer wanted ends at its next build or layout, and puts the page back.</summary>
    private static void Stop()
    {
        if (PageCompiler.Cancelled?.Invoke() == true) throw new OperationCanceledException();
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
        /// <summary>A layout expected not to line up, perhaps: one that does not is an answer, not a fault.</summary>
        private bool _quiet;
        /// <summary>The last layout that did not line up, for saying whose lines changed.</summary>
        private Scene? _misaligned;
        /// <summary>Each alternative's own `display`, which "shown" puts back.</summary>
        private readonly Dictionary<string, StyleEnum<DisplayStyle>> _display = new(StringComparer.Ordinal);

        /// <summary>The first compile of a page, which may yet find an element to write once per value.</summary>
        private readonly bool _scout;

        public Compiler(HtmlRenderer.Result built, Panel panel, Result result, Func<string, int, string?>? probed, bool scout)
        {
            _built = built; _panel = panel; _result = result; _probed = probed; _scout = scout;
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
            Seed();

            // 2. What each value writes - and, the first time, whether an element has to be written
            // once per value, which changes the markup and so everything else: then the compile is
            // only that. Otherwise its states are laid out from the union as it was built: measuring
            // the values built some of its elements again, and discovery all of them.
            if (_scout)
            {
                // A shape found: the only other thing worth knowing of this markup is which colours
                // from a set are drawn inside something the scene reads once.
                if (Shapes()) { Discover(colourSetsOnly: true); return; }
                Holes();
                Discover();
                foreach (var t in _result.Targets) if (t.Expand.Count > 0) return;
                foreach (var t in _result.Targets) Build(t, t.Union.Html);
                Name();
            }

            // 3. Every layout the page can take, and what each draws.
            foreach (var t in _result.Targets) States(t);
            if (!_scout)
            {
                Holes();
                Discover();
            }

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
            Stop();
            lock (PageCompiler.Gate) BuildLocked(t, html);
        }

        private void BuildLocked(Target t, string html)
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
            /// <summary>Among the layouts the opening values are read from.</summary>
            public bool Laid;
            /// <summary>The copies drawn where another copy was laid out (<see cref="Clone"/>): what they draw only mirrors it.</summary>
            public readonly HashSet<string> Cloned = new(StringComparer.Ordinal);
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
            _roundScenes.Clear();
            foreach (var config in _unionRounds)
            {
                var scene = Emit(Hidden(config));
                _roundScenes.Add((config, scene));
                merged = merged == null ? scene : Merge(merged, scene);
            }
            if (merged == null) return Emit(null);
            // Merged a state at a time, and split once: a line's slot names are its place in the whole.
            if (_roundScenes.Count > 1)
            {
                var wasStops = SceneSlots.SlotStops;
                SceneSlots.SlotStops = true;
                try { merged.Lines = SceneSlots.Split(string.Join("\n", merged.Raw), merged.Values).Split('\n'); }
                finally { SceneSlots.SlotStops = wasStops; }
            }
            return merged;
        }

        /// <summary>The states the union was merged from, as each was laid out.</summary>
        private readonly List<(Config Config, Scene Scene)> _roundScenes = new();

        /// <summary>
        /// The states the union was merged from are layouts of the page like any other, so each is
        /// matched with the union and kept: the state that needs it later does not lay it out again.
        /// </summary>
        private void Seed()
        {
            foreach (var (config, scene) in _roundScenes)
            {
                var key = config.Key();
                if (_emitted.ContainsKey(key)) continue;
                _emitted[key] = scene;
                Aligned(config, scene, "a state the union is drawn from");
            }
            _roundScenes.Clear();
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
            // No rounds of copies: every layout draws every copy of an element (Clone), so the union
            // needs a state per alternative that is not a copy, and nothing more.
            _rounds.Clear();
            _unionRounds.Clear();
            foreach (var sides in Cover((t, choice) => !Copied(t, choice), out var drives))
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
        /// <remarks>
        /// The merged scene's lines are the lines each side had, not split again: two lines are
        /// matched by what they are, never by where, so a line's slot names only have to be right
        /// once the last state is merged (<see cref="UnionScene"/>).
        /// </remarks>
        private Scene Merge(Scene a, Scene b)
        {
            var raw = new List<string>(a.Raw.Length + b.Raw.Length);
            var lines = new List<string>(a.Raw.Length + b.Raw.Length);
            Siblings(0, a.Lines.Length, 0, b.Lines.Length);
            return new Scene { Lines = lines.ToArray(), Raw = raw.ToArray() };

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
                        var mine = Matches(a.Lines[sa], b.Lines[sb]);
                        raw.Add(mine ? a.Raw[sa] : b.Raw[sb]);
                        lines.Add(mine ? a.Lines[sa] : b.Lines[sb]);
                        if (ea > sa) { Siblings(sa + 1, ea, sb + 1, eb); raw.Add(a.Raw[ea]); lines.Add(a.Lines[ea]); }
                    }
                    // Where either side's next item could come first, the one earlier in the markup
                    // does: the copies of one element shown in different rounds keep their order.
                    else if (y >= ib.Count || x < ia.Count && (lcs[x + 1, y] > lcs[x, y + 1]
                             || lcs[x + 1, y] == lcs[x, y + 1] && Order(a.Lines, ia[x]) <= Order(b.Lines, ib[y])))
                    {
                        var (s0, e0) = ia[x++];
                        for (var k = s0; k <= e0; k++) { raw.Add(a.Raw[k]); lines.Add(a.Lines[k]); }
                    }
                    else
                    {
                        var (s0, e0) = ib[y++];
                        for (var k = s0; k <= e0; k++) { raw.Add(b.Raw[k]); lines.Add(b.Lines[k]); }
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
            Stop();
            lock (PageCompiler.Gate)
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
                Clone(boxes, scene.Cloned);
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

        /// <summary>
        /// Every copy of an element that this layout does not show, drawn where the shown copy is. The
        /// copies of an element written once per value lay out alike and differ only in paint
        /// (<see cref="Markup.Choice.Expanded"/>), so one layout places them all: each copy keeps its own
        /// paint and takes the shown copy's boxes, and a layout per copy - the rounds this replaced -
        /// has nothing left to say.
        /// </summary>
        /// <remarks>
        /// Innermost choices first, so a copy of an element that holds copies of its own is taken from a
        /// shown copy whose own copies are already in place.
        /// </remarks>
        private void Clone(Dictionary<VisualElement, OffThread.Box> boxes, HashSet<string> cloned)
        {
            foreach (var t in _result.Targets)
                for (var i = t.Markup.Choices.Count - 1; i >= 0; i--)
                {
                    var ch = t.Markup.Choices[i];
                    if (!Copied(t, ch.Index) || !t.Union.Sides.TryGetValue(ch.Index, out var sides)) continue;
                    VisualElement? shown = null;
                    foreach (var r in sides.Then) if (Displayed(r)) { shown = _elements[r]; break; }
                    if (shown == null) foreach (var r in sides.Else) if (Displayed(r)) { shown = _elements[r]; break; }
                    if (shown == null) continue;
                    foreach (var r in sides.Then) Copy(t, r, shown);
                    foreach (var r in sides.Else) Copy(t, r, shown);
                }

            void Copy(Target t, string root, VisualElement shown)
            {
                if (!_elements.TryGetValue(root, out var ve) || ve == shown || !boxes.TryGetValue(ve, out var b) || b.display != DisplayStyle.None) return;
                if (Deep(shown, ve)) cloned.Add(root);
                else Problem(t, $"the copies of \"{root}\" are not built alike, so one cannot be drawn where another is laid out");
            }

            bool Displayed(string root)
                => _elements.TryGetValue(root, out var ve) && boxes.TryGetValue(ve, out var b) && b.display != DisplayStyle.None && !float.IsNaN(b.layout.width);

            bool Deep(VisualElement from, VisualElement to)
            {
                if (from.childCount != to.childCount || !boxes.TryGetValue(from, out var a) || !boxes.TryGetValue(to, out var b)) return false;
                // The box is the shown copy's; what the copy declares itself - a border one copy has and
                // another does not - is its own, since a hidden element's layout reads as nothing.
                var s = to.style;
                b.display = a.display;
                b.layout = a.layout;
                b.marginBottom = a.marginBottom;
                b.paddingTop = Pad(s.paddingTop, a.paddingTop); b.paddingRight = Pad(s.paddingRight, a.paddingRight);
                b.paddingBottom = Pad(s.paddingBottom, a.paddingBottom); b.paddingLeft = Pad(s.paddingLeft, a.paddingLeft);
                b.borderTopWidth = Width(s.borderTopWidth); b.borderRightWidth = Width(s.borderRightWidth);
                b.borderBottomWidth = Width(s.borderBottomWidth); b.borderLeftWidth = Width(s.borderLeftWidth);
                // What a percentage resolves against is the box, which the copy has only now.
                b.borderTopLeftRadius = Radius(s.borderTopLeftRadius, a, b.borderTopLeftRadius);
                b.borderTopRightRadius = Radius(s.borderTopRightRadius, a, b.borderTopRightRadius);
                b.borderBottomRightRadius = Radius(s.borderBottomRightRadius, a, b.borderBottomRightRadius);
                b.borderBottomLeftRadius = Radius(s.borderBottomLeftRadius, a, b.borderBottomLeftRadius);
                if (s.translate.keyword == StyleKeyword.Undefined)
                {
                    var v = s.translate.value;
                    if (v.x.unit == LengthUnit.Percent || v.y.unit == LengthUnit.Percent)
                        b.translate = new Vector3(v.x.unit == LengthUnit.Percent ? v.x.value / 100f * a.layout.width : v.x.value,
                                                  v.y.unit == LengthUnit.Percent ? v.y.value / 100f * a.layout.height : v.y.value, v.z);
                }
                for (var k = 0; k < from.childCount; k++)
                    if (!Deep(from[k], to[k])) return false;
                return true;
            }

            static float Pad(StyleLength p, float shown)
                => p.keyword == StyleKeyword.Undefined && p.value.unit != LengthUnit.Percent ? p.value.value : shown;

            static float Width(StyleFloat w) => w.keyword == StyleKeyword.Undefined ? w.value : 0f;

            static float Radius(StyleLength r, OffThread.Box a, float own)
                => r.keyword == StyleKeyword.Undefined && r.value.unit == LengthUnit.Percent
                    ? r.value.value / 100f * Math.Min(a.layout.width, a.layout.height) : own;
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
        private void LineOwners() => _lineOwner = Owners(_union.Lines);

        /// <summary>The element each line of a scene is drawn for, as <see cref="LineOwners"/> says it.</summary>
        private string?[] Owners(string[] lines)
        {
            var owners = new string?[lines.Length];
            var stack = new List<(int Line, string? Owner)>();
            string? previous = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var own = IdOf(lines[i]) ?? DefUser(lines[i]);
                var opens = Braces(lines[i]);
                string? owner = own;
                if (owner == null && opens > 0)
                {
                    // a wrapper: the first element drawn inside it
                    var end = Close(lines, i);
                    for (var k = i + 1; k <= end && owner == null; k++) owner = IdOf(lines[k]) ?? DefUser(lines[k]);
                }
                owner ??= previous ?? (stack.Count > 0 ? stack[stack.Count - 1].Owner : null);
                owners[i] = owner;
                if (opens > 0) { stack.Add((i, owner)); previous = null; }
                else if (opens < 0) { if (stack.Count > 0) { previous = stack[stack.Count - 1].Owner; stack.RemoveAt(stack.Count - 1); } }
                else previous = owner;
            }
            return owners;
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

        /// <summary>
        /// The union element whose gradient or clip a line draws with: a striped background is a group
        /// clipped to `stripes&lt;element&gt;_1` with no id of its own.
        /// </summary>
        private string? DefUser(string line)
        {
            foreach (Match m in DefUse.Matches(line))
                if (_elements.ContainsKey(m.Groups["name"].Value)) return m.Groups["name"].Value;
            return null;
        }

        private static readonly Regex DefUse = new(@"(?:clip|f|s|mask)=@?(?:tw|scroll|clip|rad|grad|cut|tg|svg|cg|cclip|conic|stripes|bimg|ka|cpath|mask)(?<name>\S+?)_\d+(?!\d)", RegexOptions.Compiled);

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
            if (!_emitted.TryGetValue(key, out var scene))
            {
                scene = Emit(Hidden(config));
                _emitted[key] = scene;
                Aligned(config, scene, why);
            }
            if (scene.Map == null) return null;
            // In the order they are first asked for, which is the order the opening values are read in.
            if (!_probing && !scene.Laid) { scene.Laid = true; _laid.Add(scene); }
            return scene;
        }

        /// <summary>A layout matched line for line against the union: its <see cref="Scene.Map"/>, or said why not.</summary>
        private void Aligned(Config config, Scene scene, string why)
        {
            var absent = Absent(config);
            var kept = new List<int>(scene.Lines.Length);
            for (var i = 0; i < _union.Lines.Length; i++)
            {
                var drop = false;
                foreach (var o in _owners[i]) if (absent.Contains(o)) { drop = true; break; }
                if (!drop) kept.Add(i);
            }
            var map = Align(scene.Lines, kept, out var at, out var their);
            if (map == null)
            {
                var mine = at < scene.Lines.Length ? scene.Lines[at].Trim() : "(end)";
                var theirs = their >= 0 && their < kept.Count ? _union.Lines[kept[their]].Trim() : "(end)";
                if (!_quiet) _result.Problems.Add($"{why}: its lines do not match the union's at line {at} - \"{Short(mine)}\" against \"{Short(theirs)}\"");
                _misaligned = scene;
                return;
            }
            scene.Map = map;
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

        /// <summary>
        /// Every gated root a layout does not DRAW. A copy of an element is drawn whenever the copy the
        /// layout shows is (<see cref="Clone"/>), so a choice between copies hides nothing here.
        /// </summary>
        private HashSet<string> Absent(Config c)
        {
            var absent = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in _result.Targets)
                foreach (var root in t.Roots)
                    if (!Drawn(c, t, t.Union.Path[root])) absent.Add(root);
            return absent;
        }

        private static bool Drawn(Config c, Target t, (bool List, int Index, int Which)[] path)
        {
            foreach (var (list, index, which) in path)
            {
                if (list) { if (which >= (c.Counts.TryGetValue((t, index), out var n) ? n : 0)) return false; }
                else if (!Copied(t, index) && Taken(c, t, index) != (which == 1)) return false;
            }
            return true;
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
                var key = "rows#" + l.Index.ToString(CultureInfo.InvariantCulture);
                if (Lengths(t, l.Index, configs) is { } laid) Bind(t, key, laid, roots);
                else State(t, key, configs, roots);
            }
        }

        /// <summary>
        /// What a list draws at each length, from as few of them as say it: empty and full, then the
        /// length halfway between two, which either lies on the line through them - and so does every
        /// length between - or splits them in two. A row draws where it stands in the full list for as
        /// long as it is drawn, and what the rows push along moves in step with how many there are, so
        /// a list of rows alike is three layouts; one whose rows differ, or whose container only draws
        /// with rows in it, is laid out at the lengths where that happens. Null when a list is too
        /// short for this to lay out fewer.
        /// </summary>
        private List<(string Name, Config Config, Dictionary<string, SceneSlots.Value> Values)>? Lengths(
            Target t, int list, List<(string Name, List<Config> Layouts)> configs)
        {
            var len = configs.Count - 1;
            if (len < 3) return null;
            var rowOf = new Dictionary<string, int>(StringComparer.Ordinal);
            var rows = t.Union.Rows[list];
            for (var k = 0; k < rows.Count; k++) foreach (var root in rows[k]) rowOf[root] = k;
            var at = new Dictionary<string, SceneSlots.Value>?[len + 1];
            if (Laid(0) is null || Laid(len) is null || !Between(0, len)) return null;

            var laid = new List<(string, Config, Dictionary<string, SceneSlots.Value>)>();
            for (var n = 0; n <= len; n++) laid.Add((configs[n].Name, configs[n].Layouts[0], at[n]!));
            return laid;

            Dictionary<string, SceneSlots.Value>? Laid(int n)
                => at[n] = Layout(configs[n].Layouts[0], $"\"{t.Id}\" rows#{list} = {n}") is { } scene ? Values(scene) : null;

            // Every length strictly between two laid out: read off them when the one halfway agrees.
            bool Between(int lo, int hi)
            {
                if (hi - lo < 2) return true;
                var mid = (lo + hi) / 2;
                if (Laid(mid) is not { } real) return false;
                if (Line(lo, hi, mid) is { } predicted && Agrees(predicted, real))
                {
                    for (var n = lo + 1; n < hi; n++) if (n != mid) at[n] = Line(lo, hi, n);
                    return true;
                }
                return Between(lo, mid) && Between(mid, hi);
            }

            // What length n draws, from lengths lo and hi: a row's slots while it is drawn, everything
            // else on the line through the two. Null when something is drawn at one and not the other.
            Dictionary<string, SceneSlots.Value>? Line(int lo, int hi, int n)
            {
                var a = at[lo]!;
                var b = at[hi]!;
                var line = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
                foreach (var pair in b)
                {
                    var row = -1;
                    for (var e = OwnerOf(pair.Key); e != null && row < 0; e = Up(e)) if (rowOf.TryGetValue(e, out var k)) row = k;
                    if (row >= 0) { if (row < n) line[pair.Key] = pair.Value; continue; }
                    if (!a.TryGetValue(pair.Key, out var was)) return null;
                    if (Same(was, pair.Value)) { line[pair.Key] = pair.Value; continue; }
                    if (!was.IsNumber || !pair.Value.IsNumber) return null;
                    line[pair.Key] = new SceneSlots.Value(was.Number + (pair.Value.Number - was.Number) * (n - lo) / (hi - lo));
                }
                foreach (var key in a.Keys) if (!b.ContainsKey(key)) return null;
                return line;
            }

            // A layout runs on whole pixels, so a line through two of them is half a pixel out at most.
            static bool Agrees(Dictionary<string, SceneSlots.Value> predicted, Dictionary<string, SceneSlots.Value> real)
            {
                if (predicted.Count != real.Count) return false;
                foreach (var pair in real)
                    if (!predicted.TryGetValue(pair.Key, out var p) || p.IsNumber != pair.Value.IsNumber
                        || (p.IsNumber ? Math.Abs(p.Number - pair.Value.Number) > 0.51f : !string.Equals(p.Text, pair.Value.Text, StringComparison.Ordinal)))
                        return false;
                return true;
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
            return Bind(t, key, laid, roots);
        }

        /// <summary>The states of one binding from what each draws: what differs, and the gates.</summary>
        private Binding Bind(Target t, string key, List<(string Name, Config Config, Dictionary<string, SceneSlots.Value> Values)> laid, List<string> roots)
        {
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
        private Scene? Probe(Config at, Dictionary<(Target, int), string>? values, string why, bool quiet = false)
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
                Rebuild(touched);
                if (touched.Count > 0) Name();
                _probing = true;
                _quiet = quiet;
                _emitted.Remove(at.Key());
                return Layout(at, why);
            }
            finally
            {
                _emitted.Remove(at.Key());
                _probing = false;
                _quiet = false;
                _override = null;
                Rebuild(touched);
                if (touched.Count > 0) Name();
            }
        }

        /// <summary>
        /// Union elements built again where they stand, their attributes written from their pieces
        /// with the current fill. Rebuilt rather than re-cascaded: the renderer turns a percentage
        /// into pixels when it cascades, and a re-cascade kept the old pixels.
        /// </summary>
        /// <remarks>
        /// Together, and then attached together: attaching walks the page's post-layout passes, and
        /// once per element it was most of what building one cost. One inside another that is built
        /// again is built with it, from the attributes just written.
        /// </remarks>
        private void Rebuild(List<(Target T, string Element)> touched)
        {
            Stop();
            var made = new List<(Target T, VisualElement Made, VisualElement Parent)>();
            lock (PageCompiler.Gate)
                foreach (var (t, e) in touched)
                {
                    if (touched.Exists(o => o.Element != e && Nested(o.Element, e) && Inside(e, o.Element))) continue;
                    if (Rebuild(t, e) is { } m) made.Add((t, m.Made, m.Parent));
                }
            if (made.Count == 0) return;
            TimeBuild.Start();
            try
            {
                lock (PageCompiler.Gate) Attached(made);
            }
            finally { TimeBuild.Stop(); }
        }

        /// <summary>What was built again, attached as the surface attaches it.</summary>
        private void Attached(List<(Target T, VisualElement Made, VisualElement Parent)> made)
        {
            {
                Attach(_built);
                // A mixed calc() is applied when its parent's size changes, and the parent of a rebuilt
                // bar has not changed: without this the copy kept the percent alone, without its `- 12px`.
                var fresh = new HashSet<VisualElement>();
                foreach (var m in made) fresh.Add(m.Made);
                foreach (var (owner, act) in _built.AfterRecascade.ToArray())
                    for (var e = owner; e != null; e = e.parent)
                        if (fresh.Contains(e)) { act(); break; }
                var targets = new List<Target>();
                var parents = new List<VisualElement>();
                foreach (var m in made)
                {
                    if (!targets.Contains(m.T)) targets.Add(m.T);
                    if (!parents.Contains(m.Parent)) parents.Add(m.Parent);
                }
                foreach (var t in targets) Animate(t);
                foreach (var parent in parents) Rename(parent);
            }
        }

        /// <summary>Whether one union element is inside another.</summary>
        private bool Inside(string element, string ancestor)
        {
            for (var e = Up(element); e != null; e = Up(e)) if (e == ancestor) return true;
            return false;
        }

        /// <summary>One union element built again where it stands; what was made, and where.</summary>
        private (VisualElement Made, VisualElement Parent)? Rebuild(Target t, string element)
        {
            if (!_elements.TryGetValue(element, out var ve) || ve.parent is not { } parent) return null;
            if (!_built.NodeOf.TryGetValue(ve, out var node) || !_built.NodeOf.TryGetValue(parent, out var parentNode)) return null;
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
                return made == null ? null : (made, parent);
            }
            finally { TimeBuild.Stop(); }
        }

        /// <summary>
        /// Every number the page writes into a style, and every other style value drawn from a fixed
        /// set: each moved, and what moved with it read off the layouts.
        /// </summary>
        /// <remarks>
        /// `width:62.3%` does not reach the scene as 62.3 - it is a width in scene units, of whatever
        /// the parent measures in the layout that shows it. So it is found by moving it: two points
        /// make a slope and an offset, which is all the chip needs, and a third says whether it is a
        /// line at all. A slot that moves out of proportion - text that wraps, a box at its minimum -
        /// needs the layout engine a compiled page does not have: refused, by name. A value from a
        /// fixed set is laid out at each of its values, and what it changes is carried as a state; one
        /// that changes the SHAPE of what is drawn - a shadow appearing, an animation starting -
        /// cannot be a state on one shape, so its element is marked to be written once per value.
        ///
        /// Every value one layout of the page shows is moved in the same layouts: what each moved is
        /// what lies inside its own element, so one layout answers for all of them. Only something
        /// OUTSIDE every moved element that moved as well - a neighbour pushed along - needs a value
        /// alone to say whose it is, and only those values are laid out alone.
        /// </remarks>
        private void Holes()
        {
            var jobs = new List<Job>();
            // Numbers: the same expression in the same scope is one value written twice - a hard stop
            // is `C 0 62%, transparent 62%` - and moving one without the other draws another gradient.
            foreach (var t in _result.Targets)
            {
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
                    jobs.Add(new Job { T = t, Holes = holes, Element = place.Element!, At = at, V0 = v0, Step = Math.Abs(v0) > 20 ? v0 * 0.5 : 20 });
                }
            }
            jobs.AddRange(VariantJobs());
            foreach (var (at, group) in ByLayout(jobs)) Together(at, group);

            // What each says, in the order they were found.
            foreach (var job in jobs)
            {
                if (job.Shaped && job.Alts == null) job.Alone = true;
                if (job.Alone) Alone(job);
                if (job.Alts == null) Number(job);
                else Variant(job);
            }
        }

        /// <summary>Every value from a fixed set that is not a colour, where a layout shows it.</summary>
        private List<Job> VariantJobs()
        {
            var jobs = new List<Job>();
            foreach (var t in _result.Targets)
                foreach (var h in t.Markup.Holes)
                {
                    var place = t.Union.Holes[h.Index];
                    if (place.Attribute is not ("style" or "class") || place.Element == null) continue;
                    var alts = t.Markup.Enumerate(h.Value);
                    if (alts == null || alts.Count < 2) continue;
                    if (IsColourProperty(place.Property) || alts.TrueForAll(IsColour)) continue;
                    if (Showing(t, t.Union.Path["#" + h.Index.ToString(CultureInfo.InvariantCulture)]) is not { } at) continue;
                    jobs.Add(new Job { T = t, Holes = new List<Markup.Hole> { h }, Element = place.Element, At = at, Alts = alts });
                }
            return jobs;
        }

        /// <summary>
        /// Values by the layout that shows them. A copy of an element is drawn in any layout that shows
        /// the copy laid out (Clone), and moved with it: its own value moves with the laid-out copy's.
        /// </summary>
        private static List<(Config At, List<Job> Jobs)> ByLayout(List<Job> jobs)
        {
            var byLayout = new Dictionary<string, (Config At, List<Job> Jobs)>(StringComparer.Ordinal);
            foreach (var job in jobs)
            {
                job.Showing = job.At;
                var at = job.At.Copy();
                foreach (var side in new List<(Target, int)>(at.Sides.Keys))
                    if (Copied(side.Item1, side.Item2)) at.Sides.Remove(side);
                job.At = at;
                var key = at.Key();
                if (!byLayout.TryGetValue(key, out var group)) byLayout[key] = group = (at, new List<Job>());
                group.Jobs.Add(job);
            }
            return new List<(Config, List<Job>)>(byLayout.Values);
        }

        /// <summary>
        /// Whether a value from a set draws another shape, asked before anything else is measured:
        /// if one does, its element is written once per value, the markup changes, and everything
        /// else measured of this one would be thrown away. So only shapes are looked at - each value
        /// in the layout that shows it, against that layout as it was drawn.
        /// </summary>
        private bool Shapes()
        {
            var any = false;
            foreach (var (at, jobs) in ByLayout(VariantJobs()))
            {
                if (Layout(at, "a state a value is shown in") is not { } before) continue;
                var rounds = new List<List<Job>>();
                foreach (var job in jobs)
                {
                    var home = rounds.Find(r => r.TrueForAll(o => !Nested(o.Element, job.Element)));
                    if (home == null) rounds.Add(home = new List<Job>());
                    home.Add(job);
                }
                foreach (var round in rounds)
                {
                    var levels = 0;
                    foreach (var job in round) levels = Math.Max(levels, job.Levels);
                    for (var level = 1; level < levels; level++)
                    {
                        var moving = round.FindAll(j => level < j.Levels && !j.Shaped);
                        if (moving.Count == 0) continue;
                        Set(moving, level);
                        var scene = Quiet(at);
                        var shaped = scene == null ? Shaped(before, _misaligned, moving) : new List<Job>();
                        Set(moving, 0);
                        // Something else changed shape too: each value alone says whose it was.
                        if (shaped == null)
                        {
                            shaped = new List<Job>();
                            foreach (var job in moving)
                            {
                                Set(new List<Job> { job }, level);
                                if (Quiet(at) == null) shaped.Add(job);
                                Set(new List<Job> { job }, 0);
                            }
                        }
                        foreach (var job in shaped)
                        {
                            job.Shaped = true;
                            job.T.Expand.Add(job.Holes[0].Index);
                            job.T.Inherit.Add(job.Holes[0].Index);
                            any = true;
                        }
                    }
                }
            }
            return any;
        }

        /// <summary>One value moved to see what it writes: a number (one or more holes written with it) or a value from a set.</summary>
        private sealed class Job
        {
            public Target T = null!;
            public List<Markup.Hole> Holes = null!;
            public string Element = string.Empty;
            public Config At = null!;
            /// <summary>Where the value is shown with its own copy laid out, for laying it out alone.</summary>
            public Config Showing = null!;
            /// <summary>A number: what the page draws, and how far each layout moves it.</summary>
            public double V0, Step;
            /// <summary>A value from a set: every value, the first what the page draws.</summary>
            public List<string>? Alts;
            /// <summary>What each layout draws, as far as it is this value's doing; null where it could not be laid out.</summary>
            public Dictionary<string, SceneSlots.Value>?[] Laid = Array.Empty<Dictionary<string, SceneSlots.Value>?>();
            /// <summary>Something outside every moved element moved too: laid out alone, to say whose it is.</summary>
            public bool Alone;
            /// <summary>Its element drew another shape at one of its values: written once per value instead.</summary>
            public bool Shaped;
            /// <summary>The layout of every value as the page draws it, with their elements built again, when it was made.</summary>
            public Dictionary<string, SceneSlots.Value>? Rest;
            /// <summary>Copies drawn where another was laid out, in the layouts this was read from.</summary>
            public readonly HashSet<string> Mirrors = new(StringComparer.Ordinal);
            public int Levels => Alts?.Count ?? 3;
            public string Value(int level) => Alts == null
                ? (V0 + level * Step).ToString("0.###", CultureInfo.InvariantCulture)
                : Alts[level < Alts.Count ? level : 0];
        }

        /// <summary>
        /// Every value one layout shows, moved in the same layouts. What changed inside a value's own
        /// element is that value's; what changed outside all of them is a neighbour one of them pushed,
        /// and the values that could have pushed it are laid out alone. A value that changes the shape
        /// of its own element is told apart the same way, by whose lines are not the lines they were.
        /// </summary>
        /// <remarks>
        /// A value on an element inside another's element - a bar in a card whose padding is a value
        /// too - moves in a layout of its own: what changed inside both would be neither's alone. The
        /// layout with every value as the page draws it is the same for all of them, and every element
        /// compared is built again first: building an element again settles its text a pixel apart
        /// from the first build, so every layout compared here is of rebuilt elements.
        /// </remarks>
        private void Together(Config at, List<Job> jobs)
        {
            var rounds = new List<List<Job>>();
            foreach (var job in jobs)
            {
                var home = rounds.Find(r => r.TrueForAll(o => !Nested(o.Element, job.Element)));
                if (home == null) rounds.Add(home = new List<Job>());
                home.Add(job);
            }

            Set(jobs, 0);
            if (Quiet(at) is not { } restScene) { foreach (var job in jobs) job.Alone = true; return; }
            var rest = Values(restScene);
            foreach (var job in jobs) job.Rest = rest;
            foreach (var round in rounds)
            {
                var active = new List<Job>(round);
                var levels = 0;
                foreach (var job in round) levels = Math.Max(levels, job.Levels);
                var laid = new Dictionary<string, SceneSlots.Value>[levels];
                laid[0] = rest;
                for (var level = 1; level < levels && active.Count > 0; level++)
                {
                    while (true)
                    {
                        var moving = active.FindAll(j => level < j.Levels);
                        if (moving.Count == 0) { laid[level] = rest; break; }
                        Set(moving, level);
                        var scene = Quiet(at);
                        if (scene != null) { laid[level] = Values(scene); Set(moving, 0); break; }
                        // Some of them draw another shape: theirs are the elements whose lines changed.
                        var shaped = Shaped(restScene, _misaligned, moving);
                        Set(moving, 0);
                        if (shaped == null || shaped.Count == 0)
                        {
                            foreach (var job in active) job.Alone = true;
                            active.Clear();
                            break;
                        }
                        foreach (var job in shaped) { job.Shaped = true; active.Remove(job); }
                    }
                }
                if (active.Count == 0) continue;

                // Every slot that is not the same in every layout, and whose it is.
                var changed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in rest)
                    for (var level = 1; level < levels; level++)
                        if (!laid[level].TryGetValue(pair.Key, out var v) || !Same(v, pair.Value)) { changed.Add(pair.Key); break; }
                var own = new Dictionary<Job, HashSet<string>>();
                foreach (var job in active) own[job] = new HashSet<string>(StringComparer.Ordinal);
                var pushed = new List<string>();
                foreach (var slot in changed)
                {
                    var owner = active.Find(job => Within(slot, job.Element));
                    if (owner != null) own[owner].Add(slot);
                    else pushed.Add(slot);
                }
                // A neighbour moved. What can push it is a value whose own box changed, beside it or
                // beside something it is inside: when one such value could have, it did; when several
                // could have, only those, laid out alone, say which.
                if (pushed.Count > 0)
                {
                    var movers = active.FindAll(job => { foreach (var slot in own[job]) if (Geometric(slot)) return true; return false; });
                    foreach (var slot in pushed)
                    {
                        var could = movers.FindAll(job => Up(job.Element) is { } parent && Within(slot, parent));
                        if (could.Count == 1) own[could[0]].Add(slot);
                        else foreach (var job in could.Count == 0 ? movers : could) job.Alone = true;
                    }
                }

                foreach (var job in active)
                {
                    if (job.Alone) continue;
                    job.Laid = new Dictionary<string, SceneSlots.Value>?[job.Levels];
                    for (var level = 0; level < job.Levels; level++)
                    {
                        // Only what it moved: everything else is the same at every level, which is all
                        // the value's own reading needs of it.
                        var mine = new Dictionary<string, SceneSlots.Value>(own[job].Count, StringComparer.Ordinal);
                        foreach (var slot in own[job])
                            if (laid[level].TryGetValue(slot, out var v)) mine[slot] = v;
                        job.Laid[level] = mine;
                    }
                }
            }
        }

        /// <summary>The elements of these values built again with each at the given level (0: as the page draws it).</summary>
        private void Set(List<Job> jobs, int level)
        {
            var values = new Dictionary<(Target, int), string>();
            var touched = new List<(Target T, string Element)>();
            foreach (var job in jobs)
            {
                foreach (var h in job.Holes) values[(job.T, h.Index)] = job.Value(level);
                if (!touched.Contains((job.T, job.Element))) touched.Add((job.T, job.Element));
            }
            _override = level == 0 ? null : values;
            try { Rebuild(touched); }
            finally { _override = null; }
            Name();
        }

        /// <summary>A layout of the page as it stands, not what it opens with, and not a fault when it does not line up.</summary>
        private Scene? Quiet(Config at)
        {
            _probing = true;
            _quiet = true;
            _misaligned = null;
            try
            {
                _emitted.Remove(at.Key());
                return Layout(at, "values moved together");
            }
            finally
            {
                _emitted.Remove(at.Key());
                _probing = false;
                _quiet = false;
            }
        }

        /// <summary>
        /// Which of these values drew another shape: the ones whose element's own lines are not the
        /// lines it drew before. Null when something else changed shape too, which none of them owns.
        /// </summary>
        private List<Job>? Shaped(Scene before, Scene? after, List<Job> jobs)
        {
            if (after == null) return null;
            var was = Lines(before.Lines);
            var now = Lines(after.Lines);
            var shaped = new List<Job>();
            var changed = new List<string>();
            foreach (var pair in was)
                if (!now.TryGetValue(pair.Key, out var l) || !Alike(pair.Value, l)) changed.Add(pair.Key);
            foreach (var pair in now)
                if (!was.ContainsKey(pair.Key)) changed.Add(pair.Key);
            foreach (var element in changed)
            {
                var job = jobs.Find(j => j.Element == element);
                if (job == null) return null;
                if (!shaped.Contains(job)) shaped.Add(job);
            }
            return shaped;

            static bool Alike(List<string> a, List<string> b)
            {
                if (a.Count != b.Count) return false;
                for (var i = 0; i < a.Count; i++)
                    if (!Matches(a[i], b[i]) && !Matches(b[i], a[i])) return false;
                return true;
            }
        }

        /// <summary>A scene's lines by the element each is drawn for.</summary>
        private Dictionary<string, List<string>> Lines(string[] lines)
        {
            var owners = Owners(lines);
            var by = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (var i = 0; i < lines.Length; i++)
            {
                var owner = owners[i] ?? string.Empty;
                if (!by.TryGetValue(owner, out var list)) by[owner] = list = new List<string>();
                list.Add(lines[i].Trim());
            }
            return by;
        }

        /// <summary>Whether either element is the other or inside it.</summary>
        private bool Nested(string a, string b)
        {
            for (var e = a; e != null; e = Up(e)) if (e == b) return true;
            for (var e = b; e != null; e = Up(e)) if (e == a) return true;
            return false;
        }

        /// <summary>A position or a size: what a value that pushes its neighbours changes of its own.</summary>
        private static bool Geometric(string slot)
            => slot.EndsWith("_x", StringComparison.Ordinal) || slot.EndsWith("_y", StringComparison.Ordinal)
               || slot.EndsWith("_w", StringComparison.Ordinal) || slot.EndsWith("_h", StringComparison.Ordinal);

        /// <summary>One value laid out at each of its values with every other as the page draws it.</summary>
        private void Alone(Job job)
        {
            job.Laid = new Dictionary<string, SceneSlots.Value>?[job.Levels];
            for (var level = 0; level < job.Levels; level++)
            {
                // As the page draws it, in the layout it was measured together in: that one already.
                if (level == 0 && job.Rest != null && job.Showing.Key() == job.At.Key()) { job.Laid[0] = job.Rest; continue; }
                var moved = new Dictionary<(Target, int), string>();
                foreach (var h in job.Holes) moved[(job.T, h.Index)] = job.Value(level);
                var why = job.Alts == null
                    ? $"\"{job.T.Id}\" hole {job.Holes[0].Index} moved by {(level * job.Step).ToString("0.###", CultureInfo.InvariantCulture)}"
                    : $"\"{job.T.Id}\" hole {job.Holes[0].Index} = \"{Short(job.Value(level))}\"";
                // A value that changes the shape of what is drawn is found here, and is not a fault.
                var scene = Probe(job.Showing, moved, why, quiet: job.Alts != null);
                if (scene == null) return;
                job.Laid[level] = Values(scene);
                job.Mirrors.UnionWith(scene.Cloned);
            }
        }

        /// <summary>A number's line from the value to each slot, from the value laid out at three points.</summary>
        private void Number(Job g)
        {
            // Measured from the element built again at its own value, not from the layout
            // before: building an element again settles text a pixel differently.
            if (g.Laid.Length < 3 || g.Laid[0] is not { } was || g.Laid[1] is not { } near || g.Laid[2] is not { } farther) return;
            var slots = new HoleSlots { Kind = Kind.Number };
            var bent = new List<string>();
            foreach (var pair in near)
            {
                if (!pair.Value.IsNumber || !was.TryGetValue(pair.Key, out var w) || !w.IsNumber
                    || !farther.TryGetValue(pair.Key, out var w2) || !w2.IsNumber) continue;
                var d1 = pair.Value.Number - w.Number;
                var d2 = w2.Number - w.Number;
                if (Math.Abs(d1) <= 0.05 && Math.Abs(d2) <= 0.05) continue;
                // The layout runs on whole pixels: a neighbour that settles one pixel over and
                // stays there whatever the value is rounding, not something the value places.
                if (Math.Abs(d1 - d2) < 0.05 && Math.Abs(d1) <= 1.01) continue;
                // Another copy of the element, drawn where this one is laid out: it moved because
                // it mirrors this copy, and its own hole writes it.
                if (Mirrored(pair.Key, g.Mirrors)) continue;
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
                return;
            }
            foreach (var h in g.Holes) g.T.Holes[h.Index] = slots;
        }

        /// <summary>A value from a set: what each value draws, as a state picked by the value; a shape per value when one cannot be laid out.</summary>
        private void Variant(Job job)
        {
            var t = job.T;
            var h = job.Holes[0];
            var laid = new List<(string Name, Dictionary<string, SceneSlots.Value> Values)>();
            for (var level = 0; level < job.Levels; level++)
            {
                if (job.Laid.Length <= level || job.Laid[level] is not { } values) { t.Expand.Add(h.Index); t.Inherit.Add(h.Index); return; }
                laid.Add((job.Alts![level], values));
            }
            // Everything that differs is carried, the element's neighbours included: a padding
            // that pushes the rows below it down is what the state is for.
            var varies = Varies(laid.ConvertAll(l => l.Values));
            varies.RemoveWhere(slot => Mirrored(slot, job.Mirrors));
            if (varies.Count == 0) return;                                    // draws the same whatever it is
            var states = new List<CompiledPage.StateValues>();
            foreach (var (name, values) in laid)
            {
                var state = new CompiledPage.StateValues { Name = name };
                Carry(state, varies, values);
                states.Add(state);
            }
            t.Holes[h.Index] = new HoleSlots { Kind = Kind.Variant, States = states };
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

        /// <summary>Whether a slot is drawn inside one of the given copies.</summary>
        private bool Mirrored(string slot, HashSet<string> copies)
        {
            if (copies.Count == 0) return false;
            for (var e = OwnerOf(slot); e != null; e = Up(e))
                if (copies.Contains(e)) return true;
            return false;
        }

        /// <summary>What <see cref="OwnerOf"/> said about each slot: a probe asks it of every slot that moved.</summary>
        private readonly Dictionary<string, string?> _slotOwners = new(StringComparer.Ordinal);

        /// <summary>The union element a slot belongs to.</summary>
        private string? OwnerOf(string slot)
        {
            if (_slotOwners.TryGetValue(slot, out var known)) return known;
            return _slotOwners[slot] = FindOwner(slot);
        }

        private string? FindOwner(string slot)
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

        // ---- text and colour, found by what comes back -------------------------------------------

        /// <summary>
        /// Text and colour holes: the union built again with a distinctive value in each, emitted
        /// with everything shown, and each value found where it landed.
        /// </summary>
        /// <param name="colourSetsOnly">
        /// Only which colours from a set reach no slot, for markup that is about to change: nothing
        /// else has been measured, so any other value not found says nothing yet.
        /// </param>
        private void Discover(bool colourSetsOnly = false)
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
                    if (t.Union.Holes[h].Attribute == "style" && t.Markup.Enumerate(t.Markup.Holes[h].Value) is { Count: > 1 } alts
                        && (!colourSetsOnly || IsColourProperty(t.Union.Holes[h].Property) || alts.TrueForAll(IsColour)))
                    {
                        t.Expand.Add(h);
                        continue;
                    }
                    if (!colourSetsOnly) Problem(t, $"hole {h} ({t.Markup.Text(t.Markup.Holes[h].Value)}) reaches no slot, so it would never be drawn");
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
                        // Rich text writes `<color=#RRGGBBAA>`, so the sentinel arrives with its own
                        // opaque alpha. Left in the literal text, a translucent value landed before it
                        // as `#RRGGBBAAFF`, which is no colour; the value carries its alpha itself.
                        if (i + 3 <= text.Length && text[i] is 'F' or 'f' && text[i + 1] is 'F' or 'f' && text[i + 2] == '>') i += 2;
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
            VisualElement? ve = null;
            if (place.Element != null && _elements.TryGetValue(place.Element, out ve)) _built.NodeOf.TryGetValue(ve, out scope);
            if (scope == null && _built.ById.TryGetValue(t.Id, out var tve)) _built.NodeOf.TryGetValue(tve, out scope);
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var v in t.Markup.Enumerate(t.Markup.Holes[hole].Value) ?? t.Markup.Literals())
            {
                var resolved = v;
                // Until no var() is left: `--live: var(--steel-300)` resolves to another var(), and a
                // colour left unread here reached the console as the text `var(--live)`.
                for (var depth = 0; scope != null && depth < 8 && resolved.IndexOf("var(", StringComparison.Ordinal) >= 0; depth++)
                    resolved = HtmlRenderer.ResolveVars(resolved, scope);
                if (StyleApplier.TryColor(resolved.Trim(), out var colour) || Keyword(resolved, place.Property, ve, out colour)) table[v] = Hex(colour);
            }
            return table;
        }

        /// <summary>
        /// `inherit`, `currentColor` and the other keywords as the colour the renderer draws for them.
        /// They mean nothing without the layout, so unresolved here they reached the scene as the word
        /// and drew magenta - AtmoDark's gear, `color:inherit` whenever its tab was not open.
        /// </summary>
        /// <remarks>
        /// For `color` every one of them is what the parent draws: the renderer drops initial, unset and
        /// revert, and the text then inherits. currentColor elsewhere is the element's own colour, and a
        /// background's initial is none. Read from the union's own layout, not OffThread's copies, which
        /// belong to whichever emit ran last. ponytail: inherit on a property other than color is left
        /// unmapped (the chip then keeps what the slot shows); resolve it from the parent when a page needs it.
        /// </remarks>
        private static bool Keyword(string value, string? property, VisualElement? ve, out Color colour)
        {
            colour = default;
            if (ve == null) return false;
            var word = value.Trim().ToLowerInvariant();
            var text = property is null or "color";
            switch (word)
            {
                case "currentcolor" when !text:
                    colour = ve.resolvedStyle.color;
                    return true;
                case "currentcolor" or "inherit" or "initial" or "unset" or "revert" or "revert-layer" when text:
                    if (ve.parent == null) return false;
                    colour = ve.parent.resolvedStyle.color;
                    return true;
                case "initial" or "unset" or "revert" or "revert-layer" when property is "background" or "background-color":
                    colour = Color.clear;
                    return true;
                default:
                    return false;
            }
        }

        // ---- the structure --------------------------------------------------------------------------

        /// <summary>
        /// The union's template with a gate on every alternative, and the values it opens with: the
        /// page's first layout, and for anything that layout does not draw, the union's own.
        /// </summary>
        private void Structure()
        {
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

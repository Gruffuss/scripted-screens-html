using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// Compiles a live page: the side of the compiler that needs the layout engine.
/// </summary>
/// <remarks>
/// <see cref="CompiledPage"/> is deliberately Unity-free so it can be run and diffed headlessly,
/// which is why it asks for the two things only a laid-out page can answer.
///
/// <b>Where an element sits.</b> The scene is in absolute coordinates and CSS is not, so a position
/// write carries its containing block's origin; and because every box carries its own absolute
/// position, moving an element has to move everything inside it too. Both come from the layout.
///
/// <b>What a class draws.</b> A class name is a state rather than a value, so the page is laid out
/// once per reachable class and the boxes that moved are recorded. That costs a layout pass per
/// state, once, at compile time - against a browser's cost of doing it on every change for ever.
/// </remarks>
internal static class PageCompiler
{
    /// <summary>Compiles the page, using the layout it currently has.</summary>
    internal static CompiledPage.Result Compile(HtmlRenderer.Result built, Panel panel, Vector2 size,
                                                IReadOnlyDictionary<string, SceneSlots.Value> slots,
                                                (string Surface, string Element, string Scene)? target = null)
    {
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in slots.Keys) available.Add(name);

        var absolute = Absolute(built);

        // Every innerHTML write, reduced to structure plus holes and each hole resolved to the slot
        // it lands on. Done before the script is translated because the translation depends on it:
        // a write whose holes are known becomes a handful of slot writes instead of a document.
        var markup = Markup(built, panel, size);

        return CompiledPage.Compile(
            built.Script,
            available,
            id => BoxOf(id, built, absolute, available),
            id => Tabular(id, built),
            (id, cls) => StateOf(id, cls, built, panel, size, absolute, slots),
            // The chunk has to carry its own runtime. Everything the translated page and the
            // binding table use - js_str, DOM, Pending - lives there, and a chunk without it dies
            // on its first line and defines nothing, which reads as "the page defined no frame".
            CompileProbe.Prelude(out _),
            // The console's own design size, so the page sizes itself for THIS screen.
            (size.x, size.y),
            Parents(built),
            Structure(built, absolute),
            markup.Lookup,
            markup.Bindings,
            // The scene's own resting value for a slot, so a state can carry what it does NOT move
            // and therefore be leavable. Numbers only: a state that changes text or colour restores
            // through its own entry, and inventing a base for those would guess.
            slot => slots.TryGetValue(slot, out var v) && v.IsNumber ? v.Number : (double?)null,
            // Where the chunk sends its own values, so this mod is not in that path at all.
            target);
    }

    /// <summary>
    /// Each named element's nearest named ancestor, so an event can bubble inside the chunk.
    /// </summary>
    /// <remarks>
    /// NEAREST NAMED, not the direct parent: the tree has plenty of elements the scene never names,
    /// and a chain that stops at the first of those would strand every click below it. Walking past
    /// them keeps the chain whole and costs nothing, since the map is built once.
    ///
    /// The outermost element is pointed at <c>document</c> so a page that listens there - a keyboard
    /// handler, a click-anywhere-to-dismiss - is reached too.
    /// </remarks>
    private static Dictionary<string, string> Parents(HtmlRenderer.Result built)
    {
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        Walk(built.Root, "document");
        return parents;

        void Walk(VisualElement ve, string above)
        {
            var mine = above;
            if (ve.name is { Length: > 0 } name)
            {
                parents[name] = above;
                mine = name;
            }
            for (var i = 0; i < ve.childCount; i++) Walk(ve[i], mine);
        }
    }

    /// <summary>
    /// Every <c>innerHTML</c> write in the page, with each of its holes resolved to a scene slot.
    /// </summary>
    /// <remarks>
    /// The expensive half of compiling markup: each distinct shape the page can take is put into the
    /// tree, laid out and emitted, and the slots are read back. That is several layout passes per
    /// write - once, at load, against a page that would otherwise rebuild its whole document at its
    /// tick rate for the life of the console.
    /// </remarks>
    private static (JsToLua.HoleLookup Lookup, List<(string Key, string Slot, bool IsNumber)> Bindings)
        Markup(HtmlRenderer.Result built, Panel panel, Vector2 size)
    {
        var byElement = new Dictionary<string, Dictionary<int, MarkupSlots.Landing>>(StringComparer.Ordinal);
        var bindings = new List<(string, string, bool)>();
        if (string.IsNullOrWhiteSpace(built.Script)) return (Nothing, bindings);

        Acornima.Ast.Script ast;
        try { ast = new Acornima.Parser().ParseScript(built.Script); }
        catch (Exception) { return (Nothing, bindings); }

        foreach (var (id, value) in InnerHtmlWrites(ast))
        {
            if (byElement.ContainsKey(id)) continue;          // one plan per element
            var shape = ScriptedScreensHtml.Markup.Of(value, ast);
            if (shape.Problems.Count > 0) continue;           // reported by the translator itself

            var landed = MarkupSlots.ResolveAll(id, shape, built, panel, size);
            if (landed.Count == 0) continue;
            byElement[id] = landed;
            foreach (var pair in landed)
                bindings.Add((id + ".innerHTML#" + pair.Key.ToString(CultureInfo.InvariantCulture),
                              pair.Value.Slot, pair.Value.IsNumber));
        }

        return (Look, bindings);

        JsToLua.HolePlan? Look(string id, int hole)
            => byElement.TryGetValue(id, out var map) && map.TryGetValue(hole, out var landing)
                ? new JsToLua.HolePlan(landing.Slot, landing.IsNumber, landing.Before, landing.After)
                : null;
    }

    private static JsToLua.HolePlan? Nothing(string id, int hole) => null;

    /// <summary>Every `x.innerHTML = …` in a script, with the element it targets.</summary>
    private static IEnumerable<(string Id, Acornima.Ast.Expression Value)> InnerHtmlWrites(Acornima.Ast.Node root)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in Every(root))
            if (node is Acornima.Ast.VariableDeclarator
                { Id: Acornima.Ast.Identifier v, Init: Acornima.Ast.CallExpression { Arguments.Count: 1 } call }
                && call.Arguments[0] is Acornima.Ast.StringLiteral lit)
                names[v.Name] = lit.Value;

        foreach (var node in Every(root))
        {
            if (node is not Acornima.Ast.AssignmentExpression
                { Left: Acornima.Ast.MemberExpression { Property: Acornima.Ast.Identifier { Name: "innerHTML" }, Object: { } owner } } a)
                continue;
            var id = owner switch
            {
                Acornima.Ast.CallExpression { Arguments.Count: 1 } c when c.Arguments[0] is Acornima.Ast.StringLiteral s => s.Value,
                Acornima.Ast.Identifier n when names.TryGetValue(n.Name, out var held) => held,
                _ => null,
            };
            if (id != null) yield return (id, a.Right);
        }
    }

    private static IEnumerable<Acornima.Ast.Node> Every(Acornima.Ast.Node n)
    {
        yield return n;
        foreach (var child in n.ChildNodes)
        {
            if (child == null) continue;
            foreach (var d in Every(child)) yield return d;
        }
    }

    /// <summary>
    /// The page's tags, classes and boxes, measured once so the chunk can answer questions about
    /// itself.
    /// </summary>
    /// <remarks>
    /// In document order, because that is the order every document-wide query has to return and it
    /// cannot be recovered from a dictionary afterwards.
    /// </remarks>
    private static CompiledPage.Tree Structure(HtmlRenderer.Result built,
                                               Dictionary<VisualElement, Vector2> absolute)
    {
        var tree = new CompiledPage.Tree();
        Walk(built.Root);
        return tree;

        void Walk(VisualElement ve)
        {
            if (ve.name is { Length: > 0 } name && built.NodeOf.TryGetValue(ve, out var node))
            {
                tree.Order.Add(name);
                if (node.Tag is { Length: > 0 } tag) tree.Tag[name] = tag.ToLowerInvariant();
                if (node.Attr("class") is { Length: > 0 } classes) tree.Class[name] = classes;

                var at = absolute.TryGetValue(ve, out var pos) ? pos : Vector2.zero;
                var layout = ve.layout;
                var style = ve.resolvedStyle;
                // The content box is the border box less its own padding and border, which is what
                // clientWidth reports - not the child content's extent.
                var padX = style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth;
                var padY = style.paddingTop + style.paddingBottom + style.borderTopWidth + style.borderBottomWidth;
                tree.Boxes[name] = new CompiledPage.Tree.Box(
                    at.x, at.y, layout.width, layout.height,
                    Math.Max(0, layout.width - padX), Math.Max(0, layout.height - padY),
                    layout.x, layout.y);
            }
            for (var i = 0; i < ve.childCount; i++) Walk(ve[i]);
        }
    }

    /// <summary>
    /// One element's box, for a caller that has no compile in flight - the data path, which needs the
    /// same "where does this sit" answer without translating a script.
    /// </summary>
    internal static DomSlots.Box? BoxFor(HtmlRenderer.Result built, string id, ICollection<string> available)
    {
        var set = available as HashSet<string> ?? new HashSet<string>(available, StringComparer.Ordinal);
        return BoxOf(id, built, Absolute(built), set);
    }

    // ---- where things are -----------------------------------------------------------------------

    /// <summary>
    /// Every element's absolute position, accumulated down the tree exactly as the emitter does it
    /// (<c>x = parentPos.x + layout.x</c>). Reading <c>worldBound</c> instead would be shorter and
    /// wrong: it is in panel space, which the emitter never uses.
    /// </summary>
    internal static Dictionary<VisualElement, Vector2> Absolute(HtmlRenderer.Result built)
    {
        var positions = new Dictionary<VisualElement, Vector2>();
        Walk(built.Root, Vector2.zero);
        return positions;

        void Walk(VisualElement ve, Vector2 parent)
        {
            var here = new Vector2(parent.x + ve.layout.x, parent.y + ve.layout.y);
            positions[ve] = here;
            for (var i = 0; i < ve.childCount; i++) Walk(ve[i], here);
        }
    }

    /// <summary>Where an element sits and how it is laid out, for the safety question.</summary>
    private static DomSlots.Box? BoxOf(string id, HtmlRenderer.Result built,
                                       Dictionary<VisualElement, Vector2> absolute, HashSet<string> available)
    {
        if (!built.ById.TryGetValue(id, out var ve) || ve == null) return null;

        var css = built.CssOf(ve);
        var outOfFlow = css.TryGetValue("position", out var pos)
                        && (pos.Trim() == "absolute" || pos.Trim() == "fixed");

        // The containing block, which is what CSS `top` is measured from: the nearest positioned
        // ancestor for an out-of-flow element, and the parent otherwise.
        var origin = Vector2.zero;
        VisualElement? block = null;
        for (var p = ve.parent; p != null; p = p.parent)
        {
            if (outOfFlow)
            {
                var pc = built.CssOf(p);
                var ppos = pc.TryGetValue("position", out var pp) ? pp.Trim() : "static";
                if (ppos != "absolute" && ppos != "relative" && ppos != "fixed" && p.parent != null) continue;
            }
            if (absolute.TryGetValue(p, out var at)) origin = at;
            block = p;
            break;
        }

        var hasBackground = css.ContainsKey("background") || css.ContainsKey("background-color");

        // Everything inside that has a box of its own: the scene is absolute, so moving this element
        // has to move each of them by the same amount or they stay exactly where they were.
        var inside = new List<(string Id, double Dx, double Dy)>();
        if (absolute.TryGetValue(ve, out var self)) Collect(ve, self, inside);

        // Whether a width write is local although the element is in flow. CSS says a block's width
        // in a block container is; stated here in the layout's own terms, since that is what decides
        // where the siblings land: a leaf in a column, start-aligned or stretched with no auto
        // margin (so its left edge does not depend on its width), under containers whose widths come
        // from above rather than from their content. A leaf, because a child would re-wrap or
        // re-centre inside a new width, and only the layout knows how.
        var localWidth = !outOfFlow && ve.childCount == 0 && ve.parent is { } up && StartAligned(ve, up) && WidthFromAbove(up);

        // The containing block's size and this element's own, so `right`/`bottom` can be measured
        // from the far edge; without them DomSlots refuses those two rather than guessing the near edge.
        // And the block's content box, which is what a `%` is of: Yoga resolves a child's percentage
        // against the parent's inner size, as CSS does, so a padded track is narrower than it draws.
        var bs = block?.resolvedStyle;
        return new DomSlots.Box(outOfFlow, origin.x, origin.y, hasBackground, inside,
                                block != null ? block.layout.width : double.NaN,
                                block != null ? block.layout.height : double.NaN,
                                ve.layout.width, ve.layout.height, localWidth,
                                bs != null ? block!.layout.width - bs.paddingLeft - bs.paddingRight - bs.borderLeftWidth - bs.borderRightWidth : double.NaN,
                                bs != null ? block!.layout.height - bs.paddingTop - bs.paddingBottom - bs.borderTopWidth - bs.borderBottomWidth : double.NaN);

        void Collect(VisualElement parent, Vector2 from, List<(string, double, double)> into)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent[i];
                if (child.name is { Length: > 0 } name && absolute.TryGetValue(child, out var at)
                    && (available.Contains(name + "_x") || available.Contains(name + "_y")))
                    into.Add((name, at.x - from.x, at.y - from.y));
                Collect(child, from, into);
            }
        }
    }

    /// <summary>In flow in a non-wrapping column, with a left edge that does not move when the width does.</summary>
    private static bool StartAligned(VisualElement ve, VisualElement parent)
    {
        var rs = ve.resolvedStyle;
        var ps = parent.resolvedStyle;
        if (rs.position != Position.Relative || ps.flexDirection != FlexDirection.Column || ps.flexWrap != Wrap.NoWrap) return false;
        if (ve.style.marginLeft.keyword == StyleKeyword.Auto || ve.style.marginRight.keyword == StyleKeyword.Auto) return false;
        var align = rs.alignSelf == Align.Auto ? ps.alignItems : rs.alignSelf;
        return align is Align.Stretch or Align.FlexStart;
    }

    /// <summary>
    /// The element's width is decided above it, never by what it contains: the root (the viewport),
    /// an explicit width, or a stretched column child of such an element. A row's item, an absolute
    /// box or a shrink-wrapped one takes its width from its content, and a child's width then is
    /// not local.
    /// </summary>
    private static bool WidthFromAbove(VisualElement ve)
    {
        if (ve.parent is not { } up) return true;
        var width = ve.style.width;
        if (width.keyword == StyleKeyword.Undefined && width.value.unit is LengthUnit.Pixel or LengthUnit.Percent) return true;
        if (ve.resolvedStyle.position != Position.Relative || up.resolvedStyle.flexDirection != FlexDirection.Column) return false;
        var align = ve.resolvedStyle.alignSelf == Align.Auto ? up.resolvedStyle.alignItems : ve.resolvedStyle.alignSelf;
        return align == Align.Stretch && WidthFromAbove(up);
    }

    // ---- what a bool draws -----------------------------------------------------------------------

    /// <summary>
    /// What a bool data key writes: the page emitted with the element shown and with it hidden, and
    /// every slot that differs between the two. The page is put back afterwards.
    /// </summary>
    /// <remarks>
    /// Emitted rather than diffed from the layout, because the scene is what the fast path writes
    /// into: a text line's y is not always its box's, and only the emitter knows which elements
    /// draw a line at all. Hiding a box removes its lines, so the hidden scene is the shown one with
    /// one run of lines cut out; everything after it moves up in the numbering, which is what the
    /// alignment below undoes before comparing values line for line.
    ///
    /// A moved line with no id has no slot the state can carry, and it is REFUSED, naming the line:
    /// the alternative is a console where the alarm vanishes and the note under it stays where it
    /// was, silently, which is the failure this project keeps recording.
    ///
    /// Names the element's opacity group as a side effect - the state hides it through
    /// <c>&lt;id&gt;_o</c>, and the emitter only names a wrapper for an element it has been told
    /// something drives.
    /// </remarks>
    internal static DomSlots.Toggle ToggleOf(HtmlRenderer.Result built, string id)
    {
        var toggle = new DomSlots.Toggle();
        if (!built.ById.TryGetValue(id, out var ve) || ve == null) { toggle.Problem = "names no element in the page"; return toggle; }
        if (built.Root.panel is not { } panel) { toggle.Problem = "the page is not laid out yet"; return toggle; }
        built.NamedGroups.Add(id);
        built.Driven.Add(id);

        var was = ve.style.display;
        var shown = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        var hidden = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        string[] ts, th;
        try
        {
            ve.style.display = DisplayStyle.Flex;
            panel.Layout(panel.Width, panel.Height);
            ts = Emitted(built, panel, shown).Split('\n');
            ve.style.display = DisplayStyle.None;
            panel.Layout(panel.Width, panel.Height);
            th = Emitted(built, panel, hidden).Split('\n');
        }
        catch (Exception ex)
        {
            toggle.Problem = $"laying the page out with it hidden threw - {ex.Message}";
            return toggle;
        }
        finally
        {
            // Compiling must leave the page exactly as it found it: whatever the page shows next
            // is laid out from here.
            ve.style.display = was;
            panel.Layout(panel.Width, panel.Height);
        }

        // Line up the two templates: a common head, a common tail, and in between the run the
        // hidden element's lines occupied. Positional slot names carry their line number, so they
        // are blanked before the lines are compared as text.
        var head = 0;
        while (head < ts.Length && head < th.Length && Positionless(ts[head]) == Positionless(th[head])) head++;
        var tail = 0;
        while (tail < ts.Length - head && tail < th.Length - head
               && Positionless(ts[ts.Length - 1 - tail]) == Positionless(th[th.Length - 1 - tail])) tail++;
        var cut = ts.Length - th.Length;
        if (cut < 0 || th.Length - head - tail != 0)
        {
            toggle.Problem = "hiding it changes the scene's structure beyond removing its own lines";
            return toggle;
        }

        foreach (var pair in shown)
        {
            var slot = pair.Key;
            var (line, suffix) = Positional(slot);
            string other;
            if (line < 0) other = slot;                                   // named: the same slot in both
            else if (line < head) other = slot;                           // before the cut: unchanged numbering
            else if (line >= head + cut) other = "L" + (line - cut).ToString(CultureInfo.InvariantCulture) + suffix;
            else continue;                                                // inside the cut: the element's own
            if (!hidden.TryGetValue(other, out var after))
            {
                // Only the element's own slots vanish with it, and its opacity is what hides it.
                if (slot == id + "_o") { toggle.Shown.Add((slot, pair.Value.Number)); toggle.Hidden.Add((slot, 0)); }
                continue;
            }
            var before = pair.Value;
            if (before.IsNumber != after.IsNumber || (!before.IsNumber && !string.Equals(before.Text, after.Text, StringComparison.Ordinal)))
            {
                toggle.Problem = $"hiding it changes {Describe(slot, line, ts, shown)}, which is not a number";
                return toggle;
            }
            if (!before.IsNumber || Math.Abs(before.Number - after.Number) <= 0.01f) continue;
            if (line >= 0)
            {
                toggle.Problem = $"hiding it moves {Describe(slot, line, ts, shown)}, which has no id and so no slot to carry it - give it one";
                return toggle;
            }
            toggle.Shown.Add((slot, before.Number));
            toggle.Hidden.Add((slot, after.Number));
        }
        if (toggle.Shown.Count == 0)
            toggle.Problem = "it draws nothing and moves nothing when hidden, so there is nothing for a bool to write";
        return toggle;
    }

    /// <summary>The page as it is laid out now, emitted and split, leaving the surface's own capture alone.</summary>
    private static string Emitted(HtmlRenderer.Result built, Panel panel, Dictionary<string, SceneSlots.Value> values)
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
            // As on a worker: from the captured boxes, with font questions deferred rather than
            // asked of TextMeshPro, whose lookups throw outside the player.
            OffThread.Boxes = boxes;
            OffThread.Active = true;
            OffThread.Job = OffThread.Globals.Take();
            var output = VectorEmitter.Isolated(() => VectorEmitter.Emit(built, built.Root, panel.Width, panel.Height));
            return SceneSlots.Split(output.Chars, output.Length, values);
        }
        finally
        {
            OffThread.Boxes = wasBoxes;
            OffThread.Active = wasActive;
            OffThread.Job = wasJob;
        }
    }

    /// <summary>A template line with its positional slot numbers blanked, so lines either side of a cut compare equal.</summary>
    private static string Positionless(string line)
        => System.Text.RegularExpressions.Regex.Replace(line, @"\$L\d+", "$$L");

    /// <summary>A positional slot name's line and what follows it (<c>L12_y</c> is 12 and <c>_y</c>); -1 for a named slot.</summary>
    private static (int Line, string Suffix) Positional(string slot)
    {
        if (slot.Length < 2 || slot[0] != 'L' || !char.IsDigit(slot[1])) return (-1, string.Empty);
        var i = 1;
        while (i < slot.Length && char.IsDigit(slot[i])) i++;
        return (int.Parse(slot.Substring(1, i - 1), CultureInfo.InvariantCulture), slot.Substring(i));
    }

    /// <summary>A slot for a message: its scene line's op and, for a text line, the text it draws.</summary>
    private static string Describe(string slot, int line, string[] template, Dictionary<string, SceneSlots.Value> values)
    {
        if (line < 0) return $"\"{slot}\"";
        var text = template[line].TrimStart();
        var op = text.IndexOf(' ') is var sp && sp > 0 ? text.Substring(0, sp) : text;
        var label = values.TryGetValue("L" + line.ToString(CultureInfo.InvariantCulture) + "_text", out var t) && !t.IsNumber ? t.Text : null;
        return label != null ? $"a {op} \"{(label.Length > 32 ? label.Substring(0, 31) + "~" : label)}\"" : $"a {op} on scene line {line}";
    }

    /// <summary>Whether this element's digits are monospaced, which the emitter does and a slot write must repeat.</summary>
    private static bool Tabular(string id, HtmlRenderer.Result built)
        => built.ById.TryGetValue(id, out var ve) && ve != null
           && built.CssOf(ve).TryGetValue("font-variant-numeric", out var fvn)
           && fvn.Contains("tabular");

    // ---- what a class draws ----------------------------------------------------------------------

    /// <summary>
    /// The slot values one class produces: apply it, lay the page out, and record every box that
    /// moved. The page is put back afterwards, so compiling leaves nothing behind.
    /// </summary>
    internal static CompiledPage.StateValues? StateOf(string id, string cls, HtmlRenderer.Result built,
                                                      Panel panel, Vector2 size,
                                                      Dictionary<VisualElement, Vector2> before,
                                                      IReadOnlyDictionary<string, SceneSlots.Value> slots)
    {
        if (!built.ById.TryGetValue(id, out var ve) || ve == null) return null;

        // what it wore before, from the node the cascade keeps, so the page can be put back exactly
        var was = built.NodeOf.TryGetValue(ve, out var node) ? node.Attr("class") ?? string.Empty : string.Empty;
        try
        {
            built.Reclass(ve, cls);
            panel.Layout(size.x, size.y);
            var after = Absolute(built);

            var values = new CompiledPage.StateValues();
            foreach (var pair in after)
            {
                var element = pair.Key;
                if (element.name is not { Length: > 0 } name) continue;
                if (!before.TryGetValue(element, out var old)) continue;

                // only what actually moved or resized, and only where a slot exists to carry it
                if (Moved(old.x, pair.Value.x)) Move(values, name, "_x", pair.Value.x, slots);
                if (Moved(old.y, pair.Value.y)) Move(values, name, "_y", pair.Value.y, slots);
            }
            return values;
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: could not lay \"{id}\" out as \"{cls}\": {ex.Message}");
            return null;
        }
        finally
        {
            // Compiling must leave the page exactly as it found it: this runs during a build, and
            // whatever the page shows next is laid out from here.
            built.Reclass(ve, was);
            panel.Layout(size.x, size.y);
        }
    }

    private static bool Moved(float a, float b) => Mathf.Abs(a - b) > 0.01f;

    /// <summary>
    /// One moved coordinate into a state, for the element's line and for its label's: a box with
    /// text is two lines under one id, and a state that moved only the first left the text behind.
    /// </summary>
    /// <remarks>
    /// The label's line is NOT at the box's coordinate: the emitter gives its rect slack for
    /// TextMeshPro's wider measure, shifts a centred label by half of it, and a clipped single
    /// line by its metric height - so the same number written to both lines put the text off by
    /// exactly that. The offset is a function of the box's size, which a move does not change, so
    /// it is read off the resting scene and carried: the label lands where the emitter would draw
    /// it for this position.
    /// ponytail: a class that resizes the box changes the slack, and a resize is not recorded here
    /// at all (only x and y are), so such a class draws the box at its old size.
    /// </remarks>
    private static void Move(CompiledPage.StateValues values, string name, string key, float value,
                             IReadOnlyDictionary<string, SceneSlots.Value> slots)
    {
        if (!slots.TryGetValue(name + key, out var box) || !box.IsNumber) return;
        values.Numbers.Add((name + key, value));
        if (slots.TryGetValue(name + SceneSlots.SecondSuffix + key, out var label) && label.IsNumber)
            values.Numbers.Add((name + SceneSlots.SecondSuffix + key, value + (label.Number - box.Number)));
    }
}

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
            (id, cls) => StateOf(id, cls, built, panel, size, absolute, available),
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
    private static Dictionary<VisualElement, Vector2> Absolute(HtmlRenderer.Result built)
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

        // The containing block's size and this element's own, so `right`/`bottom` can be measured
        // from the far edge; without them DomSlots refuses those two rather than guessing the near edge.
        return new DomSlots.Box(outOfFlow, origin.x, origin.y, hasBackground, inside,
                                block != null ? block.layout.width : double.NaN,
                                block != null ? block.layout.height : double.NaN,
                                ve.layout.width, ve.layout.height);

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
    private static CompiledPage.StateValues? StateOf(string id, string cls, HtmlRenderer.Result built,
                                                     Panel panel, Vector2 size,
                                                     Dictionary<VisualElement, Vector2> before,
                                                     HashSet<string> available)
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
                if (Moved(old.x, pair.Value.x) && available.Contains(name + "_x")) values.Numbers.Add((name + "_x", pair.Value.x));
                if (Moved(old.y, pair.Value.y) && available.Contains(name + "_y")) values.Numbers.Add((name + "_y", pair.Value.y));
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

}

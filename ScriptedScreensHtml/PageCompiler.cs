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
                                                IReadOnlyDictionary<string, SceneSlots.Value> slots)
    {
        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in slots.Keys) available.Add(name);

        var absolute = Absolute(built);

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
            // The scene's own resting value for a slot, so a state can carry what it does NOT move
            // and therefore be leavable. Numbers only: a state that changes text or colour restores
            // through its own entry, and inventing a base for those would guess.
            slot => slots.TryGetValue(slot, out var v) && v.IsNumber ? v.Number : (double?)null);
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
        for (var p = ve.parent; p != null; p = p.parent)
        {
            if (outOfFlow)
            {
                var pc = built.CssOf(p);
                var ppos = pc.TryGetValue("position", out var pp) ? pp.Trim() : "static";
                if (ppos != "absolute" && ppos != "relative" && ppos != "fixed" && p.parent != null) continue;
            }
            if (absolute.TryGetValue(p, out var at)) origin = at;
            break;
        }

        var hasBackground = css.ContainsKey("background") || css.ContainsKey("background-color");

        // Everything inside that has a box of its own: the scene is absolute, so moving this element
        // has to move each of them by the same amount or they stay exactly where they were.
        var inside = new List<(string Id, double Dx, double Dy)>();
        if (absolute.TryGetValue(ve, out var self)) Collect(ve, self, inside);

        return new DomSlots.Box(outOfFlow, origin.x, origin.y, hasBackground, inside);

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

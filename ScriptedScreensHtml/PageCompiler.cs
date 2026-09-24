using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
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
    // ---- compiled once ------------------------------------------------------------------------

    /// <summary>
    /// The lock a page's cascade and layout take: the style and layout state they share is one page
    /// at a time. The surface's gate is this object. A compile takes it around each cascade and each
    /// layout it makes rather than for its whole length, so a compile on a page's own thread holds
    /// up another page, or the game thread, for one of those at most.
    /// </summary>
    internal static readonly object Gate = new();

    /// <summary>What each page was built from, so a page built again finds its compile.</summary>
    private static readonly ConditionalWeakTable<HtmlRenderer.Result, string> Sources = new();

    /// <summary>
    /// Every page compiled, by what it was built from and the size it was laid out at, with its
    /// chunk's target left open (<see cref="Retarget"/>): fifteen consoles showing one page compile it
    /// once, and a console that builds its page again - a capture, a push of the same page - compiles
    /// nothing. Refusals are kept too: the same page is refused the same way.
    /// </summary>
    private static readonly Dictionary<(string Source, float W, float H), CompiledPage.Result> Done = new();
    private static readonly Queue<(string, float, float)> DoneOrder = new();
    private const int Kept = 16;
    /// <summary>Compiles running, so a second page thread asking for the same one waits for it rather than compiling it again.</summary>
    private static readonly Dictionary<(string, float, float), ManualResetEventSlim> Running = new();

    /// <summary>The chunk's target as it is compiled, for <see cref="Retarget"/> to fill in per console.</summary>
    private static readonly (string Surface, string Element, string Scene) Open = ("\u0002surface", "\u0002element", "\u0002scene");

    /// <summary>What a page was built from, for finding its compile when it is built again.</summary>
    internal static void Remember(HtmlRenderer.Result built, string source) => Sources.AddOrUpdate(built, source);

    /// <summary>Whether this page, at this size, is compiled already: installing it then costs nothing but the chunk's load.</summary>
    internal static bool IsCompiled(HtmlRenderer.Result built, Vector2 size)
    {
        if (!Sources.TryGetValue(built, out var source)) return false;
        lock (Done) return Done.ContainsKey((source, size.x, size.y));
    }

    /// <summary>Lets a compile go, so the page compiles again: an answer it was laid out with has changed since.</summary>
    internal static void Forget(HtmlRenderer.Result built, Vector2 size)
    {
        if (!Sources.TryGetValue(built, out var source)) return;
        lock (Done) Done.Remove((source, size.x, size.y));
    }

    /// <summary>Set while a compile runs on a page's thread; true when the page no longer wants it.</summary>
    [ThreadStatic] internal static Func<bool>? Cancelled;

    /// <summary>Compiles the page, using the layout it currently has.</summary>
    internal static CompiledPage.Result Compile(HtmlRenderer.Result built, Panel panel, Vector2 size,
                                                IReadOnlyDictionary<string, SceneSlots.Value> slots,
                                                (string Surface, string Element, string Scene)? target = null)
        => Compile(built, panel, size, slots, target, out _);

    /// <summary>
    /// A page compiled once for every console showing it: what it compiled to, for this console's chip.
    /// </summary>
    /// <param name="markup">What compiling the page's markup made of it, for a probe to show; null when it was compiled before.</param>
    internal static CompiledPage.Result Compile(HtmlRenderer.Result built, Panel panel, Vector2 size,
                                                IReadOnlyDictionary<string, SceneSlots.Value> slots,
                                                (string Surface, string Element, string Scene)? target,
                                                out MarkupSlots.Result? markup)
    {
        markup = null;
        if (!Sources.TryGetValue(built, out var source)) return CompileNow(built, panel, size, slots, target, out markup);
        var key = (source, size.x, size.y);
        ManualResetEventSlim running;
        while (true)
        {
            CompiledPage.Result? done;
            ManualResetEventSlim? other = null;
            lock (Done)
            {
                if (!Done.TryGetValue(key, out done) && !Running.TryGetValue(key, out other))
                {
                    Running[key] = running = new ManualResetEventSlim(false);
                    break;
                }
            }
            if (done != null) return Retarget(done, target) ?? CompileNow(built, panel, size, slots, target, out markup);
            // Another console is compiling this page: its answer is this one's. Waited on in steps,
            // so a page its surface wants back stops waiting as a compile stops compiling.
            while (!other!.Wait(50))
                if (Cancelled?.Invoke() == true) return Stopped();
        }
        try
        {
            var open = CompileNow(built, panel, size, slots, Open, out markup);
            if (Cancelled?.Invoke() == true) return open;
            // Only a chunk that can be handed to another console is kept for one.
            if (Retarget(open, target) is not { } mine) return CompileNow(built, panel, size, slots, target, out markup);
            lock (Done)
            {
                Done[key] = open;
                DoneOrder.Enqueue(key);
                while (DoneOrder.Count > Kept) Done.Remove(DoneOrder.Dequeue());
            }
            return mine;
        }
        finally
        {
            lock (Done) Running.Remove(key);
            running.Set();
        }
    }

    /// <summary>
    /// The compile with its chunk pointed at one console's element, or null when the chunk does not
    /// carry the open target once, as it was written (then it is compiled for that console alone).
    /// </summary>
    /// <remarks>
    /// The target is the one thing a chunk carries per console: a line of constants the flush reads.
    /// Everything else - the translation, the bindings, the structure - is the page's, so it is shared.
    /// ponytail: copies Result field by field; a field added to it has to be added here (or Result
    /// could carry this itself).
    /// </remarks>
    private static CompiledPage.Result? Retarget(CompiledPage.Result open, (string Surface, string Element, string Scene)? target)
    {
        if (open.Lua == null) return open;
        var line = TargetLine(Open);
        var at = open.Lua.IndexOf(line, StringComparison.Ordinal);
        if (at < 0 || open.Lua.IndexOf(line, at + line.Length, StringComparison.Ordinal) >= 0) return null;
        var r = new CompiledPage.Result
        {
            Lua = open.Lua.Substring(0, at) + (target is { } to ? TargetLine(to) : string.Empty) + open.Lua.Substring(at + line.Length),
            Structure = open.Structure,
            // Its own: the surface fills what the chunk opens with in place.
            StructureValues = open.StructureValues == null ? null : new Dictionary<string, SceneSlots.Value>(open.StructureValues, StringComparer.Ordinal),
            // Placed with the structure it came with: placing again would find no `$slot` text left.
            Placed = open.Placed,
            Plain = open.Plain,
        };
        r.Bindings.AddRange(open.Bindings);
        r.Unmapped.AddRange(open.Unmapped);
        r.Problems.AddRange(open.Problems);
        r.Warnings.AddRange(open.Warnings);
        r.Placements.AddRange(open.Placements);
        foreach (var pair in open.Expressions) r.Expressions[pair.Key] = pair.Value;
        return r;

        // As the chunk writes it (CompiledPage.Assemble): a quoted string escapes `"` and `\`.
        static string TargetLine((string Surface, string Element, string Scene) to)
            => "SURFACE, ELEMENT, SCENE = " + Quote(to.Surface) + ", " + Quote(to.Element) + ", " + Quote(to.Scene) + "\n\n";

        static string Quote(string v) => "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>A compile the page stopped wanting, which nothing keeps.</summary>
    private static CompiledPage.Result Stopped()
    {
        var stopped = new CompiledPage.Result();
        stopped.Problems.Add("the page stopped wanting its compile");
        return stopped;
    }

    private static CompiledPage.Result CompileNow(HtmlRenderer.Result built, Panel panel, Vector2 size,
                                                  IReadOnlyDictionary<string, SceneSlots.Value> slots,
                                                  (string Surface, string Element, string Scene)? target,
                                                  out MarkupSlots.Result? markup)
    {
        markup = null;
        // A script whose every DOM use is a translated feature becomes a scene and plain Lua on the
        // chip's own tick, with nothing of this mod behind it (THE SPEC). Anything else keeps the path
        // below, and says which feature kept it there - whatever becomes of the rest of the compile.
        var refused = new List<string>();
        if (PlainTranslator.Compile(built, panel, size, target, refused) is { } plain) return plain;
        var because = refused.Count > 0
            ? "not translated to plain Lua: " + string.Join("; ", refused.GetRange(0, Math.Min(3, refused.Count)))
              + (refused.Count > 3 ? $" (+{refused.Count - 3} more)" : string.Empty)
            : null;
        if (Cancelled?.Invoke() == true) return Stopped();
        CompiledPage.Result result;
        try { result = OldPath(built, panel, size, slots, target, out markup); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The old path is a fallback: whatever it cannot do leaves the page on the interpreter, said, never a crash.
            result = new CompiledPage.Result();
            result.Problems.Add("the compile's old path threw - " + ex.Message);
            ScriptedScreensHtmlPlugin.Log?.LogWarning("html: the compile's old path threw, so the page runs interpreted - " + ex);
        }
        if (because != null) result.Warnings.Insert(0, because);
        return result;
    }

    /// <summary>The path before the plain translator: markup compiled by probing, and the script on the prelude's DOM.</summary>
    private static CompiledPage.Result OldPath(HtmlRenderer.Result built, Panel panel, Vector2 size,
                                               IReadOnlyDictionary<string, SceneSlots.Value> slots,
                                               (string Surface, string Element, string Scene)? target,
                                               out MarkupSlots.Result? markup)
    {
        // Every innerHTML write first, because the translation depends on it: a write whose markup
        // is structure becomes a handful of slot writes instead of a document - and the structure it
        // compiles to, with every alternative in it, is the scene the rest of the page is bound to.
        markup = null;
        var plans = new Dictionary<string, JsToLua.MarkupPlan>(StringComparer.Ordinal);
        var bindings = new List<CompiledPage.Binding>();
        if (built.Script is { } script && script.Contains(".innerHTML", StringComparison.Ordinal))
        {
            markup = CompileMarkup(built, panel).Result;
            if (Cancelled?.Invoke() == true) return Stopped();
            if (markup.Template != null)
            {
                slots = markup.Values;
                foreach (var t in markup.Targets) Plan(t, plans, bindings);
            }
        }

        var available = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in slots.Keys) available.Add(name);
        var absolute = Absolute(built);
        var table = slots;

        var result = CompiledPage.Compile(
            built.Script,
            available,
            id => BoxOf(id, built, absolute, available),
            id => Tabular(id, built),
            (id, cls) => StateOf(id, cls, built, panel, size, table),
            // The chunk has to carry its own runtime. Everything the translated page and the
            // binding table use - js_str, DOM, Pending - lives there, and a chunk without it dies
            // on its first line and defines nothing, which reads as "the page defined no frame".
            CompileProbe.Prelude(out _),
            // The console's own design size, so the page sizes itself for THIS screen.
            (size.x, size.y),
            Parents(built),
            Structure(built, absolute),
            plans,
            bindings,
            // The scene's own resting value for a slot, so a state can carry what it does NOT move
            // and therefore be leavable. Numbers only: a state that changes text or colour restores
            // through its own entry, and inventing a base for those would guess.
            slot => table.TryGetValue(slot, out var v) && v.IsNumber ? v.Number : (double?)null,
            // And its resting text and colour, so a state that repaints something can be left.
            slot => table.TryGetValue(slot, out var v) && !v.IsNumber ? v.Text : null,
            // Where the chunk sends its own values, so this mod is not in that path at all.
            target,
            // A style property with no slot, drawn as the few values the script assigns it.
            styleOf: (id, css, value) => StateOf(id, string.Empty, built, panel, size, table, css + ":" + value));

        if (markup?.Template != null)
        {
            result.Structure = markup.Template;
            result.StructureValues = new Dictionary<string, SceneSlots.Value>(markup.Values, StringComparer.Ordinal);
            foreach (var p in markup.Problems) result.Warnings.Add("markup: " + p);
            // Labels the scene prints itself, placed here so every consumer of a compile sees the
            // scene the chip will write into - not only the run that installs it (which places too;
            // placing twice does nothing).
            CompiledPage.Place(result);
            CompiledPage.InlineConstants(result);
        }
        return result;
    }

    /// <summary>
    /// One compiled markup write as the chunk needs it: which of its values, labels and states it
    /// writes and under which keys, and the bindings those keys resolve to.
    /// </summary>
    private static void Plan(MarkupSlots.Target t, Dictionary<string, JsToLua.MarkupPlan> plans, List<CompiledPage.Binding> bindings)
    {
        var plan = new JsToLua.MarkupPlan { Id = t.Id, Markup = t.Markup, Drive = t.Drive != null ? t.Markup.Js(t.Drive.Path) : null };
        plan.Clicks.AddRange(t.Union.Clicks);
        foreach (var pair in t.Union.Attrs)
            if (pair.Key.Attribute == "id" && pair.Value.Count == 1 && pair.Value[0].Text is { Length: > 0 } name && !plan.Ids.Contains(name))
                plan.Ids.Add(name);
        foreach (var b in t.Bindings)
        {
            plan.States.Add(b.Key);
            bindings.Add(new CompiledPage.Binding(t.Id + "." + b.Key, Array.Empty<string>(), Array.Empty<double>(),
                                                  CompiledPage.Kind.State, b.States, other: b.Other));
        }
        foreach (var pair in t.Holes)
        {
            var hs = pair.Value;
            if (hs.Label >= 0) continue;
            var key = t.Id + ".innerHTML#" + pair.Key.ToString(CultureInfo.InvariantCulture);
            var slots = new string[hs.To.Count];
            var bias = new double[hs.To.Count];
            var scale = new double[hs.To.Count];
            for (var i = 0; i < hs.To.Count; i++) (slots[i], scale[i], bias[i]) = hs.To[i];
            switch (hs.Kind)
            {
                case MarkupSlots.Kind.Variant when hs.States != null:
                    bindings.Add(new CompiledPage.Binding(key, Array.Empty<string>(), Array.Empty<double>(), CompiledPage.Kind.State, hs.States));
                    plan.Holes[pair.Key] = false;
                    break;
                case MarkupSlots.Kind.Number when slots.Length > 0:
                    bindings.Add(new CompiledPage.Binding(key, slots, bias, CompiledPage.Kind.Length, scale: scale));
                    plan.Holes[pair.Key] = true;
                    break;
                case MarkupSlots.Kind.Colour when slots.Length > 0:
                    bindings.Add(new CompiledPage.Binding(key, slots, new double[slots.Length], CompiledPage.Kind.Colour, colours: hs.Colours));
                    plan.Holes[pair.Key] = false;
                    break;
                case MarkupSlots.Kind.Text when slots.Length > 0:
                    bindings.Add(new CompiledPage.Binding(key, slots, new double[slots.Length], CompiledPage.Kind.Text));
                    plan.Holes[pair.Key] = false;
                    break;
            }
        }
        for (var i = 0; i < t.Labels.Count; i++)
        {
            var label = t.Labels[i];
            // The label's values in the order the markup computes them, each piece pointing at its own.
            var holes = new List<int>();
            foreach (var (_, hole, _) in label.Pieces) if (hole >= 0 && !holes.Contains(hole)) holes.Add(hole);
            holes.Sort();
            var pieces = new List<object>();
            foreach (var (text, hole, colour) in label.Pieces)
            {
                if (hole < 0) { pieces.Add(text ?? string.Empty); continue; }
                var arg = holes.IndexOf(hole) + 1;
                pieces.Add(colour
                    ? (arg, (IReadOnlyDictionary<string, string>)(t.Holes.TryGetValue(hole, out var hs) && hs.Colours != null ? hs.Colours : new Dictionary<string, string>()))
                    : (object)arg);
            }
            var key = "label#" + i.ToString(CultureInfo.InvariantCulture);
            plan.Labels.Add((key, holes));
            bindings.Add(new CompiledPage.Binding(t.Id + "." + key, new[] { label.Slot }, new double[1], CompiledPage.Kind.Label,
                                                  pieces: pieces, transform: label.Transform));
        }
        plans[JsToLua.MarkupPlan.KeyOf(t.Id, t.Markup.Start)] = plan;
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


    /// <summary>Every markup write in the page's script, reduced, with the element it targets.</summary>
    internal static List<(string Id, ScriptedScreensHtml.Markup Markup)> MarkupOf(HtmlRenderer.Result built)
    {
        var list = new List<(string, ScriptedScreensHtml.Markup)>();
        if (string.IsNullOrWhiteSpace(built.Script)) return list;
        Acornima.Ast.Script ast;
        try { ast = new Acornima.Parser().ParseScript(built.Script); }
        catch (Exception) { return list; }
        foreach (var (id, write) in InnerHtmlWrites(ast))
            list.Add((id, ScriptedScreensHtml.Markup.Of(write, ast, built.Script)));
        return list;
    }

    /// <summary>
    /// Every markup write compiled, and compiled again with each element whose values draw different
    /// shapes written once per value - which only laying the values out can tell.
    /// </summary>
    /// <remarks>
    /// The script is reduced once; an element found to need writing once per value is expanded in
    /// that reduction, not by reducing the script again. And a compile that finds one stops as soon
    /// as it has (<see cref="MarkupSlots"/>): what else it would have measured is of markup about to change.
    /// </remarks>
    internal static (MarkupSlots.Result Result, Dictionary<string, List<ICollection<int>>> Expand) CompileMarkup(
        HtmlRenderer.Result built, Panel panel, Func<string, int, string?>? probed = null)
    {
        var expand = new Dictionary<string, List<ICollection<int>>>(StringComparer.Ordinal);
        var writes = MarkupOf(built);
        var result = MarkupSlots.Compile(built, panel, writes, probed, scout: true);
        // Until nothing more needs it: a copy of an element carries its holes with it, and one of
        // those can turn out shaped only where the copy is drawn. Bounded, as a page is.
        for (var round = 0; round < 4; round++)
        {
            var again = false;
            foreach (var t in result.Targets)
                if (t.Expand.Count > 0)
                {
                    if (!expand.TryGetValue(t.Id, out var rounds)) expand[t.Id] = rounds = new List<ICollection<int>>();
                    rounds.Add(new HashSet<int>(t.Expand));
                    t.Markup.Expand(t.Expand, t.Inherit);
                    again = true;
                }
            if (!again) break;
            result = MarkupSlots.Compile(built, panel, writes, probed);
        }
        foreach (var t in result.Targets)
            if (t.Expand.Count > 0)
                result.Problems.Add($"\"{t.Id}\": holes {string.Join(", ", t.Expand)} still draw a different shape per value after every element was written once per value");
        return (result, expand);
    }

    /// <summary>Every `x.innerHTML = …` in a script, with the element it targets.</summary>
    internal static IEnumerable<(string Id, Acornima.Ast.AssignmentExpression Write)> InnerHtmlWrites(Acornima.Ast.Node root)
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
            if (id != null) yield return (id, a);
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
    internal static DomSlots.Box? BoxOf(string id, HtmlRenderer.Result built,
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
                    && (available.Contains(DomSlots.Slot(name) + "_x") || available.Contains(DomSlots.Slot(name) + "_y")))
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
        lock (Gate)
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
                if (slot == DomSlots.Slot(id) + "_o") { toggle.Shown.Add((slot, pair.Value.Number)); toggle.Hidden.Add((slot, 0)); }
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
    /// <param name="patch">Changes what the emitter reads of the layout before it emits (PlainTranslator: an element
    /// hidden in one state keeps its shapes, from the layout that shows it).</param>
    internal static string Emitted(HtmlRenderer.Result built, Panel panel, Dictionary<string, SceneSlots.Value> values,
                                   Action<Dictionary<VisualElement, OffThread.Box>>? patch = null)
    {
        var wasBoxes = OffThread.Boxes;
        var wasActive = OffThread.Active;
        var wasJob = OffThread.Job;
        try
        {
            var boxes = Captured(built);
            patch?.Invoke(boxes);
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

    /// <summary>What the emitter reads of the page as it is laid out now, leaving the surface's own capture alone.</summary>
    internal static Dictionary<VisualElement, OffThread.Box> Captured(HtmlRenderer.Result built)
    {
        // Capture clears the touch sets the surface's incremental capture relies on; put them back.
        var touched = new HashSet<VisualElement>(built.Touched);
        var deep = new HashSet<VisualElement>(built.TouchedDeep);
        var boxes = new Dictionary<VisualElement, OffThread.Box>();
        OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());
        built.Touched.UnionWith(touched);
        built.TouchedDeep.UnionWith(deep);
        return boxes;
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
    /// The slot values one class produces: the page emitted at rest and emitted with the class
    /// applied, and every named slot whose value differs - a box that moved or resized, a colour, a
    /// text. The page is put back afterwards, so compiling leaves nothing behind.
    /// </summary>
    /// <remarks>
    /// Diffed from the emitted scene, not from the layout: a state is what the renderer is sent, and
    /// only the emitter knows that a moved box's label sits off its box by the slack it gave
    /// TextMeshPro, or that a class which recolours a box changes its `_f` and nothing else. Reading
    /// positions off the layout carried x and y and nothing more - a class that resized a box or
    /// turned it red drew its resting self.
    /// </remarks>
    /// <param name="declaration">
    /// Instead of a class, one inline declaration added after the element's own (<c>color:red</c>),
    /// so it wins as a script's <c>el.style.color = 'red'</c> does; <paramref name="cls"/> is then ignored.
    /// </param>
    internal static CompiledPage.StateValues? StateOf(string id, string cls, HtmlRenderer.Result built,
                                                      Panel panel, Vector2 size,
                                                      IReadOnlyDictionary<string, SceneSlots.Value> slots,
                                                      string? declaration = null)
    {
        if (!built.ById.TryGetValue(id, out var ve) || ve == null) return null;

        // what it wore before, from the node the cascade keeps, so the page can be put back exactly
        var node = built.NodeOf.TryGetValue(ve, out var n) ? n : null;
        var was = node?.Attr("class") ?? string.Empty;
        var style = node?.Attr("style");
        if (declaration != null && node == null) return null;
        lock (Gate)
        try
        {
            panel.Layout(size.x, size.y);
            var rest = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            Emitted(built, panel, rest);
            if (declaration != null) node!.Attributes["style"] = (style ?? string.Empty) + ";" + declaration;
            built.Reclass(ve, declaration != null ? was : cls);
            panel.Layout(size.x, size.y);
            var drawn = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            Emitted(built, panel, drawn);

            var values = new CompiledPage.StateValues();
            foreach (var pair in drawn)
            {
                // A line with no id has no name that survives a state moving the lines above it,
                // and a slot the structure being sent does not carry has nowhere to go.
                if (Positional(pair.Key).Line >= 0 || !slots.ContainsKey(pair.Key)) continue;
                if (!rest.TryGetValue(pair.Key, out var before) || before.Equals(pair.Value)) continue;
                if (pair.Value.IsNumber)
                {
                    if (before.IsNumber && Mathf.Abs(before.Number - pair.Value.Number) <= 0.01f) continue;
                    values.Numbers.Add((pair.Key, pair.Value.Number));
                }
                else values.Text.Add((pair.Key, pair.Value.Text ?? string.Empty));
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
            if (declaration != null)
            {
                if (style == null) node!.Attributes.Remove("style");
                else node!.Attributes["style"] = style;
            }
            built.Reclass(ve, was);
            panel.Layout(size.x, size.y);
        }
    }
}

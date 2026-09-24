using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// Compiles a page into what a person would hand-write for the vector mod: its scene, and a Lua
/// program that is the page's own script, moving the scene's values from the chip's tick and the
/// scene's clicks. Nothing of this mod runs afterwards (CLAUDE.md, THE SPEC).
/// </summary>
/// <remarks>
/// The script's language goes through <see cref="JsToLua"/> unchanged. What this adds is the DOM,
/// translated by feature, every one resolved HERE, at compile time:
///
/// <b>Lookup.</b> <c>getElementById</c>, <c>querySelector(All)</c> and <c>getElementsBy…</c>, resolved against the
/// page as written. An element the script holds as a value is a number in the Lua, a list a table of them, and
/// an operation on one chosen at run time picks that element's own code from a table built once.
/// <b>Writes.</b> <c>textContent</c> becomes the label's text with a placeholder per value, so the Lua
/// writes numbers and never builds a string; <c>style.&lt;property&gt;</c> becomes the slots
/// <see cref="DomSlots"/> maps it to, as a scale and offset for a number and as one laid-out state per
/// value for a value taken from a fixed set; <c>className</c> and <c>classList.add/remove/toggle</c>
/// become one laid-out state per combination of the classes the script can change.
/// <b>Timers.</b> <c>setTimeout</c>, <c>setInterval</c> and their clears become a scheduler run from the
/// chip's own <c>tick</c>. <b>Clicks.</b> <c>addEventListener('click')</c> and <c>onclick</c> become the
/// scene element's <c>on_click</c>, with the element's box a hit region.
///
/// A state is what the laid-out page draws, emitted and diffed against the page at rest, so a class
/// that resizes a box, recolours a label or moves a sibling is exactly the numbers that moved.
/// Anything else the script does with the DOM is refused by name, and the page keeps the path it had.
/// </remarks>
internal static partial class PlainTranslator
{
    /// <summary>The page's own vector elements: its scene, and the 1x1 element its values go through.</summary>
    internal const string SceneSuffix = "_s", DataSuffix = "_d";

    /// <summary>Globals of the browser outside the features translated here, by name.</summary>
    private static readonly Dictionary<string, string> OtherGlobals = new(StringComparer.Ordinal)
    {
        ["window"] = "window", ["requestAnimationFrame"] = "requestAnimationFrame",
        ["cancelAnimationFrame"] = "cancelAnimationFrame",
        ["performance"] = "performance.now()", ["Date"] = "Date",
        ["getComputedStyle"] = "getComputedStyle", ["globalThis"] = "globalThis",
        ["queueMicrotask"] = "queueMicrotask", ["Promise"] = "Promise", ["addEventListener"] = "a window event listener",
        ["removeEventListener"] = "a window event listener",
    };

    private static readonly HashSet<string> TimerNames = new(StringComparer.Ordinal) { "setTimeout", "setInterval", "clearTimeout", "clearInterval" };

    /// <summary>The browser's globals translated here, bare or as members of <c>window</c>.</summary>
    private static readonly HashSet<string> WindowNames = new(StringComparer.Ordinal)
    {
        "location", "localStorage", "sessionStorage", "innerWidth", "innerHeight", "devicePixelRatio", "console",
    };

    /// <summary>
    /// What a page loaded from no URL sees in <c>location</c> (about:blank): a console holds one page, compiled
    /// onto it, and was never navigated to.
    /// </summary>
    private static readonly Dictionary<string, string> Blank = new(StringComparer.Ordinal)
    {
        ["href"] = "about:blank", ["protocol"] = "about:", ["host"] = "", ["hostname"] = "", ["port"] = "",
        ["pathname"] = "blank", ["search"] = "", ["hash"] = "", ["origin"] = "null",
    };

    /// <summary>
    /// Attributes the renderer draws from itself rather than through CSS, or keeps for its own use: a script
    /// changing one would need the page built again, which a laid-out state is not.
    /// </summary>
    private static readonly HashSet<string> Drawn = new(StringComparer.OrdinalIgnoreCase)
    {
        "width", "height", "href", "type", "open", "x", "y", "viewbox", "value", "src", "srcset", "media", "d", "usemap",
        "stop-opacity", "stop-color", "start", "span", "size", "rows", "cols", "rel", "preserveaspectratio", "popover",
        "points", "offset", "name", "for", "font-weight", "font-style", "fill", "face", "content", "colspan", "rowspan",
        "color", "rx", "ry", "r", "cx", "cy", "fx", "fy", "stroke", "stroke-width", "stroke-opacity", "stroke-dasharray",
        "stroke-dashoffset", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "fill-opacity", "n", "marker",
        "text-anchor", "vector-effect", "stops", "spreadmethod", "shape-rendering", "refx", "refy", "x1", "x2", "y1",
        "y2", "transform", "dir", "max", "min", "low", "high", "optimum", "checked", "selected", "disabled",
        "data-click", "data-control", "data-pseudo", "data-marker", "data-marker-image", "data-content", "data-listed",
        "data-popover-open", "data-empty-cells", "data-focus", "data-modal",
    };

    /// <summary>
    /// The page compiled to its scene and plain Lua, or null with the reasons in <paramref name="refused"/>
    /// when it uses something outside the features translated here. A page with no script returns null
    /// and says nothing: it has a path of its own.
    /// </summary>
    internal static CompiledPage.Result? Compile(HtmlRenderer.Result built, Panel panel, Vector2 size,
                                                 (string Surface, string Element, string Scene)? target, List<string> refused)
    {
        if (Analysed(built, refused) is not { } analysed) return null;
        var (page, ast) = analysed;
        try { return Compiled(page, ast, built, panel, size, target, refused); }
        catch (OperationCanceledException)
        {
            refused.Add("the page stopped wanting its compile");
            return null;
        }
    }

    private static CompiledPage.Result? Compiled(Page page, Script ast, HtmlRenderer.Result built, Panel panel, Vector2 size,
                                                 (string Surface, string Element, string Scene)? target, List<string> refused)
    {
        string template;
        var rest = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        lock (PageCompiler.Gate)
        {
            try
            {
                template = page.Layout(panel, size, rest);
            }
            finally
            {
                page.Restore();
                page.Unlay();
                panel.Layout(size.x, size.y);
            }
        }
        if (refused.Count > 0) return null;

        var hooks = new JsToLua.Hooks { Expression = page.Expression, Statement = page.Statement, Where = page.Where };
        var lua = JsToLua.Compile(ast, out var problems, null, hooks);
        if (lua == null || problems.Count > 0)
        {
            foreach (var p in problems) refused.Add(p);
            if (refused.Count == 0) refused.Add("the script did not translate");
            return null;
        }

        var result = new CompiledPage.Result { Plain = true };
        result.Structure = Literal(page.Placed(template), rest, page.Written);
        result.StructureValues = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        foreach (var slot in page.Written) result.StructureValues[slot] = page.Opening[slot];
        result.Warnings.AddRange(page.Warnings);
        result.Lua = page.Assemble(result.Structure, lua, target);
        return result;
    }

    /// <summary>
    /// Whether the page's script uses only the translated features, read from its source alone (no
    /// layout): what decides that a page is compiled before its script runs at all. Game thread, at
    /// build, so it is an AST walk - and, for markup a script writes with innerHTML, that markup parsed
    /// and built into the page and taken out again, since what it makes can be looked up.
    /// </summary>
    internal static bool Eligible(HtmlRenderer.Result built)
    {
        try
        {
            if (Analysed(built, new List<string>()) is not { } analysed) return false;
            analysed.Page.Unlay();
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>The script parsed and its every DOM use checked against the translated features; null with the reasons otherwise.</summary>
    private static (Page Page, Script Ast)? Analysed(HtmlRenderer.Result built, List<string> refused)
    {
        // a page with no code; one whose only code is in onclick attributes is compiled like any other
        if (!built.HasCode) return null;
        Script ast;
        try { ast = new Parser().ParseScript(built.Script); }
        catch (Exception ex) { refused.Add("parse: " + ex.Message); return null; }
        // A keyframe loop the scene cannot run on its own clock needs the interpreter's runner.
        foreach (var (element, spec) in built.Animations)
            if (!built.Keyframes.TryGetValue(spec.Name, out var frames) || !VectorEmitter.Compilable(frames, built.CssOf(element)))
            {
                refused.Add($"the animation \"{spec.Name}\" is not one the scene runs on its own clock");
                return null;
            }
        // each element's box, by the name the page's ids know it by (its id, or one the build gave it; the body's is "body")
        var boxed = new Dictionary<HtmlNode, string>();
        foreach (var pair in built.NodeOf)
            if (!string.IsNullOrEmpty(pair.Key.name) && built.ById.TryGetValue(pair.Key.name, out var same) && same == pair.Key) boxed[pair.Value] = pair.Key.name;
        // An onclick attribute is the element's click handler, made before any script runs: a function of
        // `event` whose `this` is the element. It goes in front of the script as the property write it is
        // (`el.onclick = function (event) { … }`), so a script that sets onclick replaces it, as in a browser.
        var inline = new Dictionary<Node, string>();
        var handlers = new List<Statement>();
        foreach (var node in Elements(built.Document))
            foreach (var attr in node.Attributes)
            {
                var name = attr.Key;
                if (!name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || name.Length <= 2) continue;
                var where = $"the {name} attribute of <{node.Tag}>";
                if (!name.Equals("onclick", StringComparison.OrdinalIgnoreCase))
                {
                    refused.Add($"an inline event handler attribute ({name}=) on <{node.Tag}>: the vector mod delivers only clicks");
                    return null;
                }
                if (!boxed.TryGetValue(node, out var id))
                {
                    refused.Add(where + ", which is drawn as part of its parent's text: it has no box of its own to click");
                    return null;
                }
                var js = "document.getElementById('" + id.Replace("\\", "\\\\").Replace("'", "\\'") + "').onclick = function (event) {\n" + attr.Value + "\n};";
                Script one;
                try { one = new Parser().ParseScript(js); }
                catch (Exception ex) { refused.Add(where + ": parse: " + ex.Message); return null; }
                if (one.Body.Count != 1) { refused.Add(where + ", which is not one function body"); return null; }
                inline[one.Body[0]] = where;
                handlers.Add(one.Body[0]);
            }
        if (handlers.Count > 0) ast = new Script(NodeList.From(handlers.Concat(ast.Body)), ast.Strict);
        var page = new Page(built, ast, refused, inline);
        try { page.Analyse(); }
        catch { page.Unlay(); throw; }
        if (refused.Count == 0) return (page, ast);
        page.Unlay();
        return null;
    }

    private static IEnumerable<HtmlNode> Elements(HtmlNode? n)
    {
        if (n == null) yield break;
        foreach (var c in n.Children)
        {
            if (c.IsText) continue;
            yield return c;
            foreach (var d in Elements(c)) yield return d;
        }
    }

    /// <summary>
    /// The size a page is laid out and compiled for on its console, as a browser window of that size:
    /// the page's own viewport width (a `&lt;meta name=viewport&gt;`), else the console's, and a height
    /// in the console's proportions. The console's size is its rect, and while the game has the screen
    /// switched off - no one in the room - that rect may never have been laid out, so the size the
    /// element was pushed with stands in; the world aspect likewise falls back to the rect's own. A browser
    /// window is whole CSS pixels, so both are rounded: a world aspect a hair short reads 460, not 459.9.
    /// </summary>
    internal static Vector2 ConsoleLayout(float designWidth, Vector2 rect, Vector2 world, Vector2 pushed)
    {
        var w = rect.x >= 1f ? rect.x : pushed.x >= 1f ? pushed.x : 460f;
        var h = rect.y >= 1f ? rect.y : pushed.y >= 1f ? pushed.y : w;
        var width = Mathf.Round(Mathf.Max(64f, designWidth > 0f ? designWidth : w));
        var aspect = world.x > 1e-5f && world.y > 1e-5f ? world.y / world.x : h / w;
        return new Vector2(width, Mathf.Round(Mathf.Max(64f, width * aspect)));
    }

    // ---- the page: what its script touches, and what that draws ------------------------------------

    /// <summary>One element the script writes to, found at compile time.</summary>
    private sealed class Target
    {
        public VisualElement Ve;
        public HtmlNode Node;
        public string Name;
        /// <summary>What the element is in the Lua when the script holds it as a value: a number, from 1.</summary>
        public readonly int Number;
        public Target(VisualElement ve, HtmlNode node, int number) { Ve = ve; Node = node; Name = ve.name; Number = number; }
        public string Slot => DomSlots.Slot(Name);

        public readonly List<(Expression At, Expression Value)> Texts = new();
        public readonly Dictionary<string, List<(Expression At, Expression Value)>> Styles = new(StringComparer.Ordinal);
        public readonly List<(Expression At, string Verb, List<string> Names, Expression? Force)> ClassOps = new();
        public readonly List<(Expression At, List<object> Values)> ClassNames = new();
        public bool Listens;
        /// <summary>A label with no text in the page, laid out holding a line so there is a text to write into.</summary>
        public bool Empty;
        /// <summary>setAttribute, removeAttribute, toggleAttribute, hidden and dataset writes, each by its attribute's name.</summary>
        public readonly List<AttrOp> AttrOps = new();
        /// <summary>The script reads attributes by a name only known at run time, so every one is kept.</summary>
        public bool AttrsByName;
        /// <summary>What the script reads back of what it wrote: textContent, className (or classList.item), style.&lt;property&gt;.</summary>
        public bool TextRead, ClassRead;
        public readonly HashSet<string> StyleReads = new(StringComparer.Ordinal);

        public TextPlan? Text;
        public readonly Dictionary<string, StylePlan> StylePlans = new(StringComparer.Ordinal);
        public ClassPlan? Class;
        /// <summary>The Lua table holding this element's attributes, when the script changes or looks them up by a run-time name.</summary>
        public string? AttrTable;

        /// <summary>A top element of markup a script writes with innerHTML, shown and hidden as that markup's shape changes.</summary>
        public bool Top;
        /// <summary>What a click can land on inside an element whose click listener reads its event's target: a hit region of its own.</summary>
        public bool Hit;
        /// <summary>The page gave it no id: the compile named it, and `el.id` reads "".</summary>
        public bool Unnamed;
        /// <summary>Whether the script changes what this element draws, so the scene has to name it.</summary>
        public bool Written => Texts.Count > 0 || Styles.Count > 0 || ClassOps.Count > 0 || ClassNames.Count > 0 || Listens || Top || Hit || AttrOps.Any(o => o.Facet);
        public bool Facets => ClassOps.Count > 0 || ClassNames.Count > 0 || AttrOps.Any(o => o.Facet);
    }

    /// <summary>One attribute write: set (with its value), remove, toggle (with its force), or hidden (set by truthiness).</summary>
    private sealed class AttrOp
    {
        public Expression At = null!;
        public string Name = string.Empty;
        public string Verb = string.Empty;
        public Expression? Value;
        /// <summary>Whether CSS selects on the attribute (or it is hidden): a laid-out state rather than a script value.</summary>
        public bool Facet;
    }

    /// <summary>A piece of a written text: a literal, or a value with the printf format its placeholder takes.</summary>
    private sealed class Piece
    {
        public string? Literal;
        public Expression? Hole;
        public string Format = "%g";
        /// <summary>The n of `x.toFixed(n)`: the value is rounded as JavaScript rounds it, then printed with `%.nf`.</summary>
        public int? Digits;
        /// <summary>A literal that is already TextMeshPro rich text (markup written with innerHTML), put in the scene as it is.</summary>
        public bool Raw;
    }

    private sealed class TextPlan
    {
        /// <summary>The label's text as the scene prints it, placeholders in it, with the emitter's own tags around.</summary>
        public string Template = string.Empty;
        /// <summary>Per write, what it sets: a slot and a literal, or a slot and the value of a hole.</summary>
        public readonly Dictionary<Expression, List<(string Slot, string? Literal, Piece? Hole)>> Writes = new();
        /// <summary>The text as textContent reads it back: literals, and slots printed as JavaScript prints them.</summary>
        public readonly List<(string? Literal, string? Slot, Piece? Hole)> Read = new();
        /// <summary>Written as markup (innerHTML): its literals are rich text, which textContent does not read back as they are.</summary>
        public bool Rich;
    }

    private sealed class StylePlan
    {
        public string Var = string.Empty;
        /// <summary>A number: each slot as scale times the value plus offset.</summary>
        public (string Slot, double A, double B)[]? Linear;
        /// <summary>A value from a fixed set: what each draws, every slot any of them moves.</summary>
        public Dictionary<object, Dictionary<string, SceneSlots.Value>>? States;
        public readonly Dictionary<Expression, Expression> Holes = new();
        /// <summary>When the script reads the property back: the value last written, and how the browser prints it.</summary>
        public string? ReadVar;
        public string Unit = string.Empty;
        public string? ReadInitial;
        public Dictionary<object, string>? Printed;
        /// <summary>A value (written, or the page's own) the browser gives back in a form not translated.</summary>
        public bool Unprintable;
    }

    private sealed class ClassPlan
    {
        public string Var = string.Empty;
        public readonly List<string> Names = new();
        public readonly Dictionary<string, bool> Initial = new(StringComparer.Ordinal);
        public readonly Dictionary<int, Dictionary<string, SceneSlots.Value>> States = new();
        /// <summary>Each class string className can be given, as which of <see cref="Names"/> it turns on.</summary>
        public readonly Dictionary<string, HashSet<string>> Sets = new(StringComparer.Ordinal);
        /// <summary>Classes the element keeps whatever the script does, for classList.contains.</summary>
        public readonly List<string> Fixed = new();
        /// <summary>Attributes CSS selects on, each with the values it can hold (the first, null, is absent).</summary>
        public readonly List<(string Name, List<string?> Options, Dictionary<object, int> Index)> Attrs = new();
        /// <summary>The class attribute as the page wrote it, for a className read before any change; null with no read.</summary>
        public string? Raw;
        public List<string>? Order;
    }

    private sealed partial class Page
    {
        private readonly HtmlRenderer.Result _built;
        private readonly Script _ast;
        private readonly List<string> _refused;
        private readonly Dictionary<Node, Node> _parent = new();
        private readonly Dictionary<string, int> _bindings = new(StringComparer.Ordinal);
        private readonly HashSet<string> _assigned = new(StringComparer.Ordinal);
        /// <summary>Names bound once to an element and never reassigned.</summary>
        private readonly Dictionary<string, Target> _vars = new(StringComparer.Ordinal);
        private readonly Dictionary<VisualElement, Target> _targets = new();
        private readonly List<Target> _order = new();
        /// <summary>Declarations the Lua drops: every name in them holds an element, which the Lua has no use for.</summary>
        private readonly HashSet<Node> _elementDeclarations = new();
        /// <summary>Expressions evaluated for their effect that are DOM operations, and the elements they can act on.</summary>
        private readonly Dictionary<Expression, List<Target>> _ops = new();
        /// <summary>An operation whose element is chosen at run time: the expression that chooses it (its value is the element's number).</summary>
        private readonly Dictionary<Expression, Expression> _recv = new();
        /// <summary>Expressions in <see cref="_lua"/> that are writes: their value is undefined, so they stand as statements anywhere.</summary>
        private readonly HashSet<Node> _effects = new();
        /// <summary>Reads that yield a boolean, and reads that yield null or undefined as Lua's nil, with the text JavaScript prints for it.</summary>
        private readonly HashSet<Node> _bool = new();
        private readonly Dictionary<Node, string> _null = new();
        /// <summary>A console call's arguments that do something, evaluated where the call stood.</summary>
        private readonly Dictionary<Node, List<Expression>> _consoleKept = new();
        private bool _timers;
        /// <summary>Expressions the Lua writes itself: a browser global read, an element read, an attribute or storage call.</summary>
        private readonly Dictionary<Node, Func<JsToLua, string>> _lua = new();
        /// <summary>Nodes of what a dropped console call would have printed: never evaluated, so never checked.</summary>
        private readonly HashSet<Node> _ignored = new();
        private bool _void, _hashWrites, _reload, _localStorage, _sessionStorage, _attrs;
        /// <summary>The page as its source wrote it, by id: what a read of anything the script never wrote answers.</summary>
        private Dictionary<string, HtmlNode>? _source;

        public readonly List<string> Warnings = new();
        /// <summary>Every slot the program writes, in the order first met.</summary>
        public readonly List<string> Written = new();
        /// <summary>What each written slot opens with.</summary>
        public readonly Dictionary<string, SceneSlots.Value> Opening = new(StringComparer.Ordinal);
        /// <summary>Each numeric slot's glide: the renderer's `ease` entry, 0 for a snap.</summary>
        private readonly Dictionary<string, string> _ease = new(StringComparer.Ordinal);
        /// <summary>Which facet claimed each slot, so two that move one slot are refused rather than raced.</summary>
        private readonly Dictionary<string, string> _claimed = new(StringComparer.Ordinal);

        private readonly List<Action> _restore = new();

        /// <summary>The statements an onclick attribute became, each with the attribute it is.</summary>
        private readonly Dictionary<Node, string> _inline;

        public Page(HtmlRenderer.Result built, Script ast, List<string> refused, Dictionary<Node, string> inline)
        {
            _built = built;
            _ast = ast;
            _refused = refused;
            _inline = inline;
        }

        /// <summary>The onclick attribute a node's code is in, for what is said about it; null for a node of the script.</summary>
        public string? Where(Node at)
        {
            for (Node? n = at; n != null; n = _parent.TryGetValue(n, out var up) ? up : null)
                if (_inline.TryGetValue(n, out var attr)) return attr;
            return null;
        }

        private void Refuse(Node at, string what)
        {
            // a node the compile made (a write standing for markup) is reported where its source is
            while (_origin.TryGetValue(at, out var from)) at = from;
            var line = (Where(at) ?? "line " + at.Location.Start.Line.ToString(CultureInfo.InvariantCulture)) + ": " + what;
            if (!_refused.Contains(line)) _refused.Add(line);
        }

        // ---- analysis -------------------------------------------------------------------------------

        public void Analyse()
        {
            Link(_ast);
            foreach (var n in Markup.Everything(_ast)) Bind(n);
            Index();
            // innerHTML first: the elements markup makes are elements of the page for everything after
            Markups();
            _els.Clear();
            _byId.Clear();

            foreach (var n in Markup.Everything(_ast))
                if (n is VariableDeclarator { Id: Identifier id, Init: { } init } && Lookup(init) is { } t
                    && _bindings.TryGetValue(id.Name, out var count) && count == 1 && !_assigned.Contains(id.Name))
                    _vars[id.Name] = t;
            foreach (var n in Markup.Everything(_ast))
                if (n is VariableDeclaration vd && vd.Declarations.All(d => d.Id is Identifier i && _vars.ContainsKey(i.Name) && d.Init != null && Lookup(d.Init) != null))
                    _elementDeclarations.Add(vd);

            Handlers();
            foreach (var n in Markup.Everything(_ast)) Visit(n);
            Selectors();
            MarkupChecks();
        }

        // ---- names: each reference's declaration, scoped as JavaScript scopes it ------------------------

        private readonly Dictionary<Node, Dictionary<string, Identifier>> _scopes = new();
        /// <summary>Every write to a declared name (`x = v`, `x++`, `x += v`, a loop's `for (x of …)`), by its declaration.</summary>
        private readonly Dictionary<Identifier, List<Node>> _writes = new();
        /// <summary>Every reference to a declared name, by its declaration.</summary>
        private readonly Dictionary<Identifier, List<Identifier>> _refs = new();

        private void Index()
        {
            foreach (var n in Markup.Everything(_ast))
            {
                switch (n)
                {
                    case Identifier id when Reference(id) && Decl(id) is { } d && d != id:
                        if (!_refs.TryGetValue(d, out var refs)) _refs[d] = refs = new List<Identifier>();
                        refs.Add(id);
                        break;
                    case AssignmentExpression { Left: Identifier al } a when Decl(al) is { } ad: Write(ad, a); break;
                    case UpdateExpression { Argument: Identifier ul } u when Decl(ul) is { } ud: Write(ud, u); break;
                    case ForInStatement { Left: Identifier fl } fi when Decl(fl) is { } fd: Write(fd, fi); break;
                    case ForOfStatement { Left: Identifier ol } fo when Decl(ol) is { } od: Write(od, fo); break;
                }
            }

            void Write(Identifier d, Node w)
            {
                if (!_writes.TryGetValue(d, out var list)) _writes[d] = list = new List<Node>();
                list.Add(w);
            }
        }

        /// <summary>The identifier that declares the name a reference reads, found scope by scope outwards; null for a global the script never declares.</summary>
        private Identifier? Decl(Identifier id)
        {
            if (_declOf.TryGetValue(id, out var known)) return known;
            for (Node n = id; _parent.TryGetValue(n, out var p); n = p)
                if (Scope(p).TryGetValue(id.Name, out var binding)) return _declOf[id] = binding;
            return _declOf[id] = null;
        }

        private readonly Dictionary<Identifier, Identifier?> _declOf = new();

        /// <summary>The names a node declares for what is inside it: a function its parameters and vars, a block its let, const, class and functions.</summary>
        private Dictionary<string, Identifier> Scope(Node p)
        {
            if (_scopes.TryGetValue(p, out var s)) return s;
            s = new Dictionary<string, Identifier>(StringComparer.Ordinal);
            switch (p)
            {
                case IFunction f:
                    if (p is FunctionExpression { Id: { } self }) s[self.Name] = self;
                    foreach (var param in f.Params) Patterns(param, s);
                    Vars(f.Body, s);
                    break;
                case Script or BlockStatement or FunctionBody or StaticBlock or SwitchStatement:
                    if (p is Script) Vars(p, s);
                    foreach (var kid in p is SwitchStatement sw ? sw.Cases.SelectMany(c => c.Consequent).Cast<Node?>() : p.ChildNodes.Cast<Node?>())
                        switch (kid)
                        {
                            case VariableDeclaration { Kind: not VariableDeclarationKind.Var } vd:
                                foreach (var d in vd.Declarations) Patterns(d.Id, s);
                                break;
                            case FunctionDeclaration { Id: { } fid }: s[fid.Name] = fid; break;
                            case ClassDeclaration { Id: { } cid }: s[cid.Name] = cid; break;
                        }
                    break;
                case ForStatement { Init: VariableDeclaration { Kind: not VariableDeclarationKind.Var } fd }:
                    foreach (var d in fd.Declarations) Patterns(d.Id, s);
                    break;
                case ForInStatement { Left: VariableDeclaration { Kind: not VariableDeclarationKind.Var } fi }:
                    foreach (var d in fi.Declarations) Patterns(d.Id, s);
                    break;
                case ForOfStatement { Left: VariableDeclaration { Kind: not VariableDeclarationKind.Var } fo }:
                    foreach (var d in fo.Declarations) Patterns(d.Id, s);
                    break;
                case CatchClause { Param: { } cp }:
                    Patterns(cp, s);
                    break;
            }
            _scopes[p] = s;
            return s;

            static void Patterns(Node pattern, Dictionary<string, Identifier> into)
            {
                switch (pattern)
                {
                    case Identifier i: into[i.Name] = i; break;
                    case AssignmentPattern ap: Patterns(ap.Left, into); break;
                    case RestElement re: Patterns(re.Argument, into); break;
                    case ArrayPattern arr: foreach (var e in arr.Elements) if (e != null) Patterns(e, into); break;
                    case ObjectPattern obj:
                        foreach (var prop in obj.Properties)
                            if (prop is Property pp) Patterns(pp.Value, into); else if (prop is RestElement r) Patterns(r.Argument, into);
                        break;
                }
            }

            // `var` belongs to the function (or the script) it is written in, from any block inside it
            static void Vars(Node body, Dictionary<string, Identifier> into)
            {
                foreach (var kid in body.ChildNodes)
                {
                    if (kid == null || kid is IFunction) continue;
                    if (kid is VariableDeclaration { Kind: VariableDeclarationKind.Var } vd)
                        foreach (var d in vd.Declarations) Patterns(d.Id, into);
                    Vars(kid, into);
                }
            }
        }

        /// <summary>
        /// Every value a declared variable is given in the source: its initialiser (null for none, which is
        /// undefined) and each `name = value`. Null when it is not a plain variable, or is also changed in a
        /// way the source does not show as a value (`++`, `+=`, a loop's element).
        /// </summary>
        private List<Expression?>? Given(Identifier decl)
        {
            if (!_parent.TryGetValue(decl, out var b) || b is not VariableDeclarator vd || vd.Id != decl) return null;
            if (_parent.TryGetValue(vd, out var dd) && _parent.TryGetValue(dd, out var loop) && loop is ForInStatement or ForOfStatement) return null;
            var list = new List<Expression?> { vd.Init };
            if (_writes.TryGetValue(decl, out var ws))
                foreach (var w in ws)
                    if (w is AssignmentExpression { Operator: Operator.Assignment } a) list.Add(a.Right);
                    else return null;
            return list;
        }

        /// <summary>A function a name is bound to for good: a declaration, or a const/let given a function and never changed.</summary>
        private IFunction? Function(Identifier callee)
        {
            if (Decl(callee) is not { } decl || !_parent.TryGetValue(decl, out var b)) return null;
            if (b is FunctionDeclaration fd && fd.Id == decl) return _writes.ContainsKey(decl) ? null : fd;
            if (b is VariableDeclarator { Init: IFunction fn } vd && vd.Id == decl && !_writes.ContainsKey(decl)) return fn;
            return null;
        }

        /// <summary>The declaration of the name a function is bound to (see <see cref="Function"/>), or null for one with none.</summary>
        private Identifier? NameOf(IFunction fn)
        {
            if (fn is FunctionDeclaration { Id: { } id }) return _writes.ContainsKey(id) ? null : id;
            if (_parent.TryGetValue((Node)fn, out var p) && p is VariableDeclarator { Id: Identifier vid } vd && vd.Init == fn && !_writes.ContainsKey(vid)) return vid;
            return null;
        }

        /// <summary>What a function returns: its concise body, or every `return`'s argument (null for a bare `return`), nested functions aside.</summary>
        private static List<Expression?> Returns(IFunction fn)
        {
            var found = new List<Expression?>();
            if (fn.Body is Expression concise) { found.Add(concise); return found; }
            Walk(fn.Body);
            return found;

            void Walk(Node n)
            {
                foreach (var kid in n.ChildNodes)
                {
                    if (kid == null || kid is IFunction) continue;
                    if (kid is ReturnStatement r) found.Add(r.Argument);
                    Walk(kid);
                }
            }
        }

        /// <summary>A parameter's function and its place in the list, when it is a plain name there.</summary>
        private (IFunction Fn, int Index)? ParamOf(Identifier decl)
        {
            if (!_parent.TryGetValue(decl, out var p) || p is not IFunction fn) return null;
            for (var i = 0; i < fn.Params.Count; i++) if (fn.Params[i] == decl) return (fn, i);
            return null;
        }

        /// <summary>The list a function is the forEach callback of, when it is one: <c>list.forEach(fn)</c> or <c>list.forEach(name)</c> for a function bound to a name.</summary>
        private Els? ForEachOver(Node callback)
        {
            if (!_parent.TryGetValue(callback, out var p) || p is not CallExpression c || c.Arguments.Count == 0 || c.Arguments[0] != callback
                || c.Callee is not MemberExpression { Computed: false, Property: Identifier { Name: "forEach" } } fm)
                return null;
            return Elems(fm.Object) is { List: true } list ? list : null;
        }

        private void Link(Node n)
        {
            foreach (var kid in n.ChildNodes)
            {
                if (kid == null) continue;
                _parent[kid] = n;
                Link(kid);
            }
        }

        /// <summary>Counts every binding of every name, so a name bound twice is never taken for one element.</summary>
        private void Bind(Node n)
        {
            switch (n)
            {
                case VariableDeclarator d: Names(d.Id); break;
                case FunctionDeclaration { Id: { } fid } f: Count(fid.Name); foreach (var p in f.Params) Names(p); break;
                case FunctionExpression fe: foreach (var p in fe.Params) Names(p); break;
                case ArrowFunctionExpression ae: foreach (var p in ae.Params) Names(p); break;
                case ClassDeclaration { Id: { } cid }: Count(cid.Name); break;
                case CatchClause { Param: { } cp }: Names(cp); break;
                case AssignmentExpression { Left: Identifier ai }: _assigned.Add(ai.Name); break;
                case UpdateExpression { Argument: Identifier ui }: _assigned.Add(ui.Name); break;
                case ForInStatement { Left: Identifier fi }: _assigned.Add(fi.Name); break;
                case ForOfStatement { Left: Identifier fo }: _assigned.Add(fo.Name); break;
            }

            void Names(Node p)
            {
                switch (p)
                {
                    case Identifier i: Count(i.Name); break;
                    case AssignmentPattern ap: Names(ap.Left); break;
                    case RestElement re: Names(re.Argument); break;
                    case ArrayPattern arr: foreach (var e in arr.Elements) if (e != null) Names(e); break;
                    case ObjectPattern obj:
                        foreach (var prop in obj.Properties)
                            if (prop is Property pp) Names(pp.Value); else if (prop is RestElement r) Names(r.Argument);
                        break;
                }
            }

            void Count(string name) => _bindings[name] = _bindings.TryGetValue(name, out var c) ? c + 1 : 1;
        }

        private bool Declared(string name) => _bindings.ContainsKey(name);

        /// <summary><c>document.getElementById('id')</c> with a literal id, as the element it names; null for anything else.</summary>
        private Target? Lookup(Node e)
        {
            if (e is not CallExpression { Callee: MemberExpression { Computed: false, Object: Identifier { Name: "document" }, Property: Identifier { Name: "getElementById" } } } call
                || Declared("document") || call.Arguments.Count != 1 || call.Arguments[0] is not StringLiteral lit
                || !_built.ById.TryGetValue(lit.Value, out var ve) || ve == null || !_built.NodeOf.TryGetValue(ve, out var node))
                return null;
            return TargetOf(ve, node);
        }

        /// <summary>
        /// `document.documentElement`: the page's root box. The renderer draws &lt;html&gt; and &lt;body&gt; as one box (`:root`
        /// matches it), so what the script does to the root element it does to that box.
        /// </summary>
        // ponytail: an attribute set here lands on <body>'s node, so a selector naming the html tag with it (`html[data-x]`) does not see it; `:root[data-x]` and `[data-x]` do
        private bool IsRoot(Expression e)
            => e is MemberExpression { Computed: false, Object: Identifier { Name: "document" }, Property: Identifier { Name: "documentElement" } } && !Declared("document");

        private Target Root() => TargetOf(_built.Root, _built.NodeOf[_built.Root]);

        private Target TargetOf(VisualElement ve, HtmlNode node)
        {
            if (!_targets.TryGetValue(ve, out var t))
            {
                _targets[ve] = t = new Target(ve, node, _order.Count + 1);
                _order.Add(t);
            }
            return t;
        }

        // ---- elements as values: lookups, lists, and an element chosen at run time ------------------------

        /// <summary>
        /// The elements an expression can yield, as the compile knows them: an element (one of <see cref="Ts"/>,
        /// or null), or a list of them (one of <see cref="Lists"/>, when it can be one of several). In the Lua an
        /// element is its <see cref="Target.Number"/> and a list a table of those numbers, fixed at compile time.
        /// </summary>
        private sealed class Els
        {
            public readonly List<Target> Ts = new();
            public bool List;
            public readonly List<List<Target>> Lists = new();
            /// <summary>For a list: "NodeList", "HTMLCollection" or "Array" - what a browser gives, and so which members it has.</summary>
            public string Kind = string.Empty;
            /// <summary>An element that can be null: a lookup matching nothing, an index past the end, a variable set to null.</summary>
            public bool Null;
            /// <summary>Why the compile cannot follow it; the script is refused where it is used.</summary>
            public string? Bad;
            /// <summary>Contributes nothing: null, undefined, or a name met again while it is being worked out.</summary>
            public bool Neutral;
            /// <summary>A list that is one list of markup's rows (after <c>Prefix</c> elements always there): as long as the rows shown.</summary>
            public (MkRep Rep, int Prefix)? Rows;
            /// <summary>A list holding elements markup makes in only some of its writes: those shown when it is looked up, as a browser's list holds what is there.</summary>
            public bool Shown;
            public bool One => !List && Bad == null && !Null && !Neutral && Ts.Count == 1;

            public static Els Nothing(bool isNull) => new() { Neutral = true, Null = isNull };
            public static Els Refused(string why) => new() { Bad = why };
            public static Els Of(Target t) { var e = new Els(); e.Ts.Add(t); return e; }
            public static Els ListOf(List<Target> items, string kind)
            {
                var e = new Els { List = true, Kind = kind };
                e.Lists.Add(items);
                foreach (var t in items) if (!e.Ts.Contains(t)) e.Ts.Add(t);
                return e;
            }

            /// <summary>
            /// A value that is one of several: their union. Null when none of them is an element; refused when
            /// some are and some are other kinds of value, which the Lua could not tell apart from an element.
            /// </summary>
            public static Els? Choice(IEnumerable<Els?> values)
            {
                var all = values.ToList();
                if (all.FirstOrDefault(x => x?.Bad != null) is { } bad) return bad;
                if (!all.Any(x => x is { Neutral: false })) return all.Count > 0 && all.All(x => x != null) ? Nothing(all.Any(x => x!.Null)) : null;
                if (all.Any(x => x == null)) return Refused("a value that is an element at one time and another kind of value at another");
                var u = new Els { Neutral = true };
                foreach (var x in all)
                {
                    if (x!.Neutral) { u.Null |= x.Null; continue; }
                    if (!u.Neutral && u.List != x.List) return Refused("a value that is a list of elements at one time and a single element at another");
                    if (u.Neutral) { u.Neutral = false; u.List = x.List; u.Kind = x.Kind; }
                    else if (u.Kind != x.Kind) u.Kind = "Array";
                    u.Null |= x.Null;
                    u.Shown |= x.Shown;
                    foreach (var t in x.Ts) if (!u.Ts.Contains(t)) u.Ts.Add(t);
                    foreach (var l in x.Lists) if (!u.Lists.Any(y => y.SequenceEqual(l))) u.Lists.Add(l);
                }
                var rows = all.Where(x => x is { Neutral: false }).Select(x => x!.Rows).Distinct().ToList();
                if (rows.Count > 1) return Refused("a list of a list's rows at one time and other elements at another");
                u.Rows = rows.Count == 1 ? rows[0] : null;
                return u;
            }
        }

        private readonly Dictionary<Node, Els?> _els = new();
        private readonly HashSet<Node> _elsBusy = new();

        /// <summary>
        /// The elements an expression yields, or null when it yields none (a number, a string, an object).
        /// <paramref name="bind"/> gives the parameters of a function inlined at one call its arguments there.
        /// </summary>
        private Els? Elems(Expression e, Inlined? bind = null)
        {
            if (bind == null && _els.TryGetValue(e, out var cached)) return cached;
            if (!_elsBusy.Add(e)) return Els.Nothing(false);
            Els? found;
            try { found = ElemsOf(e, bind); }
            finally { _elsBusy.Remove(e); }
            if (bind == null) _els[e] = found;
            return found;
        }

        private Els? ElemsOf(Expression e, Inlined? bind)
        {
            switch (e)
            {
                case ParenthesizedExpression w when _bindOf.TryGetValue(w, out var within):
                    return Elems(w.Expression, within);

                case CallExpression c when DomCall(c) is { } q:
                    return Looked(c, q.Kind, q.Under, q.Arg, bind);

                case MemberExpression root when IsRoot(root):
                    return Els.Of(Root());

                case MemberExpression { Computed: true } ix when Elems(ix.Object, bind) is { List: true } list:
                    return Pick(list, ix.Property);

                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "item" } } im } ic
                    when Elems(im.Object, bind) is { List: true } items:
                    return Pick(items, ic.Arguments.Count > 0 ? ic.Arguments[0] as Expression : null);

                case MemberExpression { Computed: false, Object: Identifier ev, Property: Identifier { Name: "target" or "currentTarget" } which }
                    when Decl(ev) is { } evd && _eventOf.ContainsKey(evd):
                    return EventElems(evd, which.Name == "currentTarget");

                case ThisExpression th:
                    return ThisElems(th);

                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "closest" } } cm } cc
                    when Elems(cm.Object, bind) is { List: false, Neutral: false } from:
                    return from.Bad != null ? from : Closest(cc, from, bind);

                case NullLiteral:
                    return Els.Nothing(true);

                case Identifier { Name: "undefined" } u when Decl(u) == null:
                    return Els.Nothing(true);

                case Identifier id:
                    {
                        if (_vars.TryGetValue(id.Name, out var held)) return Els.Of(held);
                        if (Decl(id) is not { } decl) return null;
                        if (bind != null && bind.TryGetValue(decl, out var arg)) return Elems(arg.Expr, arg.Env);
                        return Named(decl);
                    }

                case ConditionalExpression c:
                    return Els.Choice(new[] { Elems(c.Consequent, bind), Elems(c.Alternate, bind) });

                // `a && b` is a when a is falsy - an element only when it is null - and b otherwise
                case LogicalExpression { Operator: Operator.LogicalAnd } la:
                    {
                        var left = Elems(la.Left, bind);
                        return Els.Choice(new[] { left == null ? null : left.Bad != null ? left : Els.Nothing(true), Elems(la.Right, bind) });
                    }

                case LogicalExpression l:
                    return Els.Choice(new[] { Elems(l.Left, bind), Elems(l.Right, bind) });

                case ArrayExpression a:
                    {
                        var items = new List<Target>();
                        var kinds = new List<Els?>();
                        foreach (var x in a.Elements)
                        {
                            var one = x is Expression xe and not SpreadElement ? Elems(xe, bind) : null;
                            kinds.Add(one);
                            if (one is { Bad: null, Neutral: false }) { if (!one.One) return Els.Refused("an array of elements chosen at run time"); items.Add(one.Ts[0]); }
                        }
                        if (!kinds.Any(k => k is { Neutral: false })) return kinds.FirstOrDefault(k => k?.Bad != null);
                        if (kinds.Any(k => k == null || k.Neutral || k.Bad != null)) return kinds.FirstOrDefault(k => k?.Bad != null) ?? Els.Refused("an array holding elements and other values");
                        return Els.ListOf(items, "Array");
                    }

                case CallExpression { Callee: Identifier f } call when Function(f) is { } fn:
                    {
                        var inner = Inlined.Of(fn, call, bind);
                        var returned = Returns(fn).Select(r => r == null ? Els.Nothing(true) : Elems(r, inner)).ToList();
                        return returned.Any(r => r is { Neutral: false } || r?.Bad != null) ? Els.Choice(returned) : null;
                    }

                // `o.el` read through a getter of every object o can be: what the getters return
                case MemberExpression { Computed: false, Property: Identifier gname } gm when Getters(gm.Object, gname.Name, bind) is { } getters:
                    {
                        var returned = getters.SelectMany(g => Returns(g.Fn).Select(r => r == null ? Els.Nothing(true) : Elems(r, g.Env))).ToList();
                        return returned.Any(r => r is { Neutral: false } || r?.Bad != null) ? Els.Choice(returned) : null;
                    }

                default:
                    return null;
            }
        }

        private bool? _anyGetter;

        /// <summary>The getter of this name on every object literal an expression can be; null when one of them has none (or the script has no getter).</summary>
        private List<(IFunction Fn, Inlined? Env)>? Getters(Expression holder, string name, Inlined? bind)
        {
            _anyGetter ??= Markup.Everything(_ast).Any(n => n is Property { Kind: PropertyKind.Get });
            if (_anyGetter == false || Objects(holder, bind, 0) is not { Count: > 0 } objects) return null;
            var found = new List<(IFunction, Inlined?)>();
            foreach (var (o, env) in objects)
            {
                if (o.Properties.FirstOrDefault(p => p is Property { Computed: false, Kind: PropertyKind.Get } g
                        && (g.Key is Identifier k ? k.Name : (g.Key as StringLiteral)?.Value) == name) is not Property { Value: FunctionExpression fn })
                    return null;
                found.Add((fn, env));
            }
            return found;
        }

        /// <summary>A declared name's elements: what it is given, the list it walks (for...of), or what its function is called with.</summary>
        private Els? Named(Identifier decl)
        {
            if (!_parent.TryGetValue(decl, out var b)) return null;
            if (b is VariableDeclarator vd && vd.Id == decl && _parent.TryGetValue(vd, out var dd) && _parent.TryGetValue(dd, out var loop))
            {
                if (loop is ForOfStatement fo && fo.Left == dd) return Elems(fo.Right) is { List: true } over ? Members(over) : null;
                if (loop is ForInStatement) return null;
            }
            if (ParamOf(decl) is { } param) return ParamElems(param.Fn, param.Index);
            if (Given(decl) is not { } given)
            {
                // changed in a way the source does not show as a value: an element in it could not be followed
                var initial = b is VariableDeclarator { Init: { } init } ? Elems(init) : null;
                return initial is { Neutral: false } ? Els.Refused($"\"{decl.Name}\", which holds an element and is changed in a way the compile cannot follow") : null;
            }
            var values = given.Select(v => v == null ? Els.Nothing(true) : Elems(v)).ToList();
            return values.Any(v => v is { Neutral: false } || v?.Bad != null) ? Els.Choice(values) : null;
        }

        /// <summary>A parameter's elements: a forEach callback's first is each member of the list, and a named function's the arguments of every call.</summary>
        private Els? ParamElems(IFunction fn, int index)
        {
            if (ForEachOver((Node)fn) is { } over) return index == 0 ? Members(over) : index == 2 ? over : null;
            if (NameOf(fn) is not { } name || !_refs.TryGetValue(name, out var refs)) return null;
            var values = new List<Els?>();
            foreach (var r in refs)
            {
                if (_parent[r] is CallExpression call && call.Callee == r)
                    values.Add(index < call.Arguments.Count && call.Arguments[index] is Expression a and not SpreadElement ? Elems(a) : Els.Nothing(true));
                else if (ForEachOver(r) is { } list) values.Add(index == 0 ? Members(list) : index == 2 ? list : null);
                // passed around: what it is called with is not in the source (Flow refuses an element passed to it)
                else return null;
            }
            return values.Any(v => v is { Neutral: false } || v?.Bad != null) ? Els.Choice(values) : null;
        }

        /// <summary>Any member of a list, as an element.</summary>
        private static Els Members(Els list)
        {
            var e = new Els();
            e.Ts.AddRange(list.Ts);
            return e;
        }

        /// <summary>A list's member at an index: the one there for a literal index, any member (or undefined) otherwise.</summary>
        private static Els Pick(Els list, Expression? index)
        {
            var e = new Els();
            if (index is NumericLiteral { Value: var k } && k >= 0 && k == Math.Floor(k) && !list.Shown)
            {
                foreach (var l in list.Lists)
                    if (k < l.Count) { if (!e.Ts.Contains(l[(int)k])) e.Ts.Add(l[(int)k]); }
                    else e.Null = true;
                // a row may not be shown
                if (list.Rows != null) e.Null = true;
                return e;
            }
            e.Ts.AddRange(list.Ts);
            e.Null = true;
            return e;
        }

        /// <summary>
        /// A lookup the DOM answers: document.getElementById, and querySelector, querySelectorAll,
        /// getElementsByClassName and getElementsByTagName on the document or on one element.
        /// </summary>
        private (string Kind, Expression? Under, Expression? Arg)? DomCall(CallExpression c)
        {
            if (c.Callee is not MemberExpression { Computed: false, Property: Identifier p } m
                || p.Name is not ("getElementById" or "querySelector" or "querySelectorAll" or "getElementsByClassName" or "getElementsByTagName"))
                return null;
            var arg = c.Arguments.Count > 0 && c.Arguments[0] is Expression a and not SpreadElement ? a : null;
            if (m.Object is Identifier { Name: "document" } && !Declared("document")) return (p.Name, null, arg);
            if (p.Name == "getElementById") return null;
            return Elems(m.Object) is { List: false, Neutral: false } ? (p.Name, m.Object, arg) : null;
        }

        /// <summary>What a DOM lookup yields, resolved against the page as written.</summary>
        private Els Looked(CallExpression c, string kind, Expression? under, Expression? arg, Inlined? bind)
        {
            if (kind == "getElementById") return ById(arg, bind).Els;
            Target? scope = null;
            if (under != null)
            {
                var u = Elems(under, bind)!;
                if (u.Bad != null) return u;
                if (!u.One) return Els.Refused($"{kind} on an element chosen at run time (it answers differently for each)");
                scope = u.Ts[0];
            }
            if (arg == null || Finite(arg, null, bind) is not { Count: 1 } one || one[0] is not string text)
                return Els.Refused($"{kind} with {(arg == null ? "no argument" : "an argument only known at run time")}");
            var selector = kind switch
            {
                "getElementsByClassName" => string.Concat(text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(n => "." + n)),
                "getElementsByTagName" => text.Trim(),
                _ => text,
            };
            if (selector.Length == 0) return kind is "querySelector" ? Els.Nothing(true) : Els.ListOf(new List<Target>(), kind == "querySelectorAll" ? "NodeList" : "HTMLCollection");
            if (Select(selector, scope, out var why) is not { } found) return Els.Refused(why!);
            if (!_selectors.Any(s => s.Selector == selector && s.At == c)) _selectors.Add((selector, c));
            if (kind == "querySelector") return found.Count > 0 ? Els.Of(found[0]) : Els.Nothing(true);
            var list = Els.ListOf(found, kind == "querySelectorAll" ? "NodeList" : "HTMLCollection");
            list.Rows = RowsOf(found);
            list.Shown = list.Rows == null && found.Any(t => _madeBy.TryGetValue(t, out var of) && Varies(of));
            return list;
        }

        /// <summary>Selectors the script looks elements up by, each where it is used; checked against what the script changes.</summary>
        private readonly List<(string Selector, Node At)> _selectors = new();

        /// <summary>
        /// The elements a selector matches in document order, among the descendants of one element or the whole
        /// page. Null, with the reason, for a selector the compile cannot match as a browser does, or one that
        /// matches an element with no box of its own (a `&lt;b&gt;` drawn as part of its paragraph's text).
        /// </summary>
        private List<Target>? Select(string selector, Target? under, out string? why)
        {
            if (Matcher(selector, out why) is not { } match) return null;
            var top = under?.Node ?? Document();
            var found = new List<Target>();
            foreach (var node in Below(top))
            {
                if (node.IsText || !match(node)) continue;
                if (node.Attr("id") is not { } id || !_built.ById.TryGetValue(id, out var ve) || ve == null
                    || !_built.NodeOf.TryGetValue(ve, out var own) || own != node)
                {
                    why = $"the selector \"{selector}\" matches a <{node.Tag}> with no box of its own (it is drawn as part of its parent, which nothing can change on its own)";
                    return null;
                }
                found.Add(TargetOf(ve, node));
            }
            return found;

            static IEnumerable<HtmlNode> Below(HtmlNode n)
            {
                foreach (var c in n.Children)
                {
                    yield return c;
                    foreach (var d in Below(c)) yield return d;
                }
            }
        }

        /// <summary>The page's root node: the document a lookup on `document` searches.</summary>
        private HtmlNode Document()
        {
            var n = _built.NodeOf[_built.Root];
            while (n.Parent != null) n = n.Parent;
            return n;
        }

        /// <summary>
        /// getElementById with its id: a literal names one element; an id from a fixed set, or a fixed text
        /// around one value (`'row' + i`), names the elements whose ids it can make; anything else is refused.
        /// <see cref="IdKey.Hole"/> is what the Lua looks the element up by: the value itself, never the id built
        /// as a string.
        /// </summary>
        private (Els Els, IdKey? Key) ById(Expression? arg, Inlined? bind)
        {
            // what the analysis found stands for the Lua, which is written after the markup is taken out of the page again
            if (bind != null || arg == null) return ByIdOf(arg, bind);
            if (!_byId.TryGetValue(arg, out var known)) _byId[arg] = known = ByIdOf(arg, bind);
            return known;
        }

        private readonly Dictionary<Expression, (Els, IdKey?)> _byId = new();

        private (Els Els, IdKey? Key) ByIdOf(Expression? arg, Inlined? bind)
        {
            if (arg is StringLiteral lit)
                return (_built.ById.TryGetValue(lit.Value, out var ve) && ve != null && _built.NodeOf.TryGetValue(ve, out var node)
                    ? Els.Of(TargetOf(ve, node)) : Els.Nothing(true), null);
            if (arg == null) return (Els.Refused("getElementById with no id"), null);
            var key = new IdKey();
            var e = new Els();
            // a fixed text around one value: the value is the key
            var parts = new List<Expression>();
            Flat(arg, parts);
            var holes = parts.Where(x => x is not StringLiteral).ToList();
            if (holes.Count == 1 && parts.Count > 1)
            {
                static string Text(Expression x) => ((StringLiteral)x).Value;
                var at = parts.IndexOf(holes[0]);
                var prefix = string.Concat(parts.Take(at).Select(Text));
                var suffix = string.Concat(parts.Skip(at + 1).Select(Text));
                key.Hole = holes[0];
                IEnumerable<string> middles;
                if (Finite(holes[0], null, bind) is { } values) middles = values.Select(v => v is double d ? JsToLuaNumber(d) : (string)v);
                else
                {
                    // any value: every id of the page with that text around it
                    // ponytail: a value the page's ids never take reads as null, as the browser's lookup does
                    middles = Source().Keys.Concat(_markupIds).Where(id => id.Length >= prefix.Length + suffix.Length && id.StartsWith(prefix, StringComparison.Ordinal)
                                                        && id.EndsWith(suffix, StringComparison.Ordinal))
                                              .Select(id => id.Substring(prefix.Length, id.Length - prefix.Length - suffix.Length)).ToList();
                    e.Null = true;
                }
                foreach (var mid in middles.Distinct())
                    if (Named(prefix + mid) is { } t) { key.Table[mid] = t; if (!e.Ts.Contains(t)) e.Ts.Add(t); }
                    else e.Null = true;
                return (e, key);
            }
            if (Finite(arg, null, bind) is { } ids)
            {
                key.Hole = arg;
                foreach (var v in ids)
                {
                    var id = v is double d ? JsToLuaNumber(d) : (string)v;
                    if (Named(id) is { } t) { key.Table[id] = t; if (!e.Ts.Contains(t)) e.Ts.Add(t); }
                    else e.Null = true;
                }
                return (e, key);
            }
            return (Els.Refused("an element chosen at run time from ids the compile cannot bound (getElementById with an id computed at run time)"), null);

            // an id the page's source or its markup gives, never one the build made up for an element without
            Target? Named(string id)
                => (Source().ContainsKey(id) || _markupIds.Contains(id)) && _built.ById.TryGetValue(id, out var v) && v != null && _built.NodeOf.TryGetValue(v, out var n) ? TargetOf(v, n) : null;

            static void Flat(Expression x, List<Expression> into)
            {
                switch (x)
                {
                    case NonLogicalBinaryExpression { Operator: Operator.Addition } b when IsText(b.Left) || IsText(b.Right) || b.Left is NonLogicalBinaryExpression { Operator: Operator.Addition }:
                        Flat(b.Left, into);
                        Flat(b.Right, into);
                        return;
                    case TemplateLiteral t:
                        for (var i = 0; i < t.Quasis.Count; i++)
                        {
                            if ((t.Quasis[i].Value.Cooked ?? string.Empty).Length > 0) into.Add(new StringLiteral(t.Quasis[i].Value.Cooked!, t.Quasis[i].Value.Raw ?? string.Empty));
                            if (i < t.Expressions.Count) into.Add(t.Expressions[i]);
                        }
                        return;
                    default: into.Add(x); return;
                }
            }

            static bool IsText(Expression x) => x is StringLiteral or TemplateLiteral;
        }

        /// <summary>How the Lua finds an element by a value it holds: that value, and the element for each text it prints as.</summary>
        private sealed class IdKey
        {
            public Expression Hole = null!;
            public readonly Dictionary<string, Target> Table = new(StringComparer.Ordinal);
        }

        /// <summary>Expressions that yield elements, already checked where they stand.</summary>
        private readonly HashSet<Node> _sourced = new();

        /// <summary>An expression that yields elements, checked where it stands.</summary>
        private void Source(Expression n)
        {
            if (!_sourced.Add(n)) return;
            if (Elems(n) is not { } els) return;
            if (els.Bad != null) { Refuse(n, els.Bad); return; }
            if (els.Neutral) return;
            if (Transient(n, els) is { } transient) { Refuse(n, transient); return; }
            // A lookup that finds nothing, used as an element: a browser throws there, so it is refused by name.
            // A member of a list the page as written leaves empty is not: a loop over it does nothing, and the Lua
            // throws as a browser does if it is reached.
            if (!els.List && els.Ts.Count == 0 && n is CallExpression lookup && DomCall(lookup) != null
                && _parent.TryGetValue(n, out var p) && p is MemberExpression pm && pm.Object == n)
            {
                Refuse(n, (lookup.Callee is MemberExpression { Property: Identifier { Name: "getElementById" } } && lookup.Arguments.Count > 0 && lookup.Arguments[0] is StringLiteral id
                    ? $"getElementById('{id.Value}') names no element of the page" : "a lookup that finds no element of the page")
                    + " (a browser throws on the property of null)");
                return;
            }
            Flow(n, els);
        }

        /// <summary>
        /// Where an element (or a list of them) goes. The compile follows it into a name, a function's
        /// parameter, a return, an array and a comparison, and resolves what the script does with it there;
        /// anything else would carry an element where the compile cannot see it, and is refused.
        /// </summary>
        private void Flow(Expression n, Els els)
        {
            if (!_parent.TryGetValue(n, out var p)) return;
            var what = els.List ? "a list of elements" : ElementWord(els);
            switch (p)
            {
                case MemberExpression m when m.Object == n:
                    if (els.List) ListMember(m, els);
                    else Use(n, els);
                    return;
                case VariableDeclarator vd when vd.Init == n:
                    if (vd.Id is Identifier) return;
                    break;
                case AssignmentExpression { Operator: Operator.Assignment } a when a.Right == n && a.Left is Identifier:
                    return;
                case CallExpression c when c.Callee != n && c.Arguments.Contains(n):
                    if (c.Callee is Identifier f && Function(f) is { } fn && NameOf(fn) is { } fname
                        && _refs.TryGetValue(fname, out var refs) && refs.All(r => _parent[r] is CallExpression rc && rc.Callee == r || ForEachOver(r) != null))
                        return;
                    Refuse(n, $"{what} passed to a function the compile cannot follow (one called only by its name is followed)");
                    return;
                case ArrayExpression arr:
                    if (Elems(arr) is { } whole && whole.Bad == null && !whole.Neutral) { Flow(arr, whole); return; }
                    Refuse(n, $"{what} in an array with other values");
                    return;
                case ReturnStatement:
                case ArrowFunctionExpression af when af.Body == n:
                    {
                        Node? fnNode = p;
                        while (fnNode != null && fnNode is not IFunction) fnNode = _parent.TryGetValue(fnNode, out var up) ? up : null;
                        if (fnNode is IFunction owner && NameOf(owner) is { } oname && _refs.TryGetValue(oname, out var orefs)
                            && orefs.All(r => _parent[r] is CallExpression rc && rc.Callee == r))
                            return;
                        // a getter's, every read of which is followed through the getter
                        if (fnNode is FunctionExpression getter && _parent.TryGetValue(getter, out var gp) && gp is Property { Computed: false, Kind: PropertyKind.Get } gprop
                            && _parent.TryGetValue(gprop, out var go) && go is ObjectExpression gobj
                            && (gprop.Key is Identifier gk ? gk.Name : (gprop.Key as StringLiteral)?.Value) is { } gname
                            && FieldUses(gname, gobj) is { Assigned.Count: 0 } gflow
                            && gflow.Uses.All(u => !u.Computed && !WrittenTo(u) && Getters(u.Object, gname, null) != null))
                            return;
                        Refuse(n, $"{what} returned from a function the compile cannot follow (one called only by its name is followed)");
                        return;
                    }
                case ConditionalExpression c when c.Test == n:
                case IfStatement { Test: var it } when it == n:
                case WhileStatement { Test: var wt } when wt == n:
                case DoWhileStatement { Test: var dt } when dt == n:
                case ForStatement { Test: var ft } when ft == n:
                case NonUpdateUnaryExpression { Operator: Operator.LogicalNot }:
                case ExpressionStatement:
                    return;
                case NonLogicalBinaryExpression { Operator: Operator.StrictEquality or Operator.StrictInequality or Operator.Equality or Operator.Inequality }:
                    if (!els.List) return;
                    break;
                // `el && el.x`: only tested
                case LogicalExpression { Operator: Operator.LogicalAnd } la when la.Left == n:
                    return;
                case ConditionalExpression or LogicalExpression:
                    if (p is LogicalExpression && Tested(p)) return;
                    if (Elems((Expression)p) is { } whole2 && whole2.Bad == null && !whole2.Neutral) { Flow((Expression)p, whole2); return; }
                    if (Elems((Expression)p)?.Bad is { } bad) { Refuse(p, bad); return; }
                    break;
                case ForOfStatement fo when fo.Right == n && els.List:
                    if (fo.Left is VariableDeclaration) return;
                    break;
            }
            Refuse(n, $"{what} used as a value this way (the compile follows elements into names, calls, returns, arrays, loops and comparisons)");

            bool Tested(Node x)
            {
                while (_parent.TryGetValue(x, out var up))
                {
                    switch (up)
                    {
                        case LogicalExpression: x = up; continue;
                        case NonUpdateUnaryExpression { Operator: Operator.LogicalNot }: return true;
                        case IfStatement i: return i.Test == x;
                        case WhileStatement w: return w.Test == x;
                        case DoWhileStatement d: return d.Test == x;
                        case ForStatement f: return f.Test == x;
                        case ConditionalExpression c: return c.Test == x;
                        default: return false;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// A scroll container, as CSS makes one: overflow auto, scroll or hidden on either axis (a script can scroll a
        /// hidden one; visible and clip on both axes make none).
        /// </summary>
        private static bool ScrollBox(Dictionary<string, string> css)
            => new[] { "overflow", "overflow-x", "overflow-y" }.Any(k => css.TryGetValue(k, out var v)
                   && v.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Any(w => w.ToLowerInvariant() is "auto" or "scroll" or "hidden" or "overlay"));

        private static string ElementWord(Els els)
            => els.Ts.Count == 1 ? $"the element \"{els.Ts[0].Name}\"" : els.Ts.Count == 0 ? "an element the page as written does not have" : $"an element chosen at run time ({els.Ts.Count} candidates)";

        /// <summary>What the script does with a list of elements: length, item(), forEach(), an index and for...of, as a browser's list has them.</summary>
        private void ListMember(MemberExpression m, Els list)
        {
            if (m.Computed)
            {
                if (WrittenTo(m)) Refuse(m, "a list of elements written into (a browser's lists cannot be, and an array of elements is not followed once changed)");
                return;
            }
            var name = m.Property is Identifier pid ? pid.Name : "…";
            switch (name)
            {
                case "length" when !WrittenTo(m):
                    return;
                case "item" when list.Kind != "Array" && _parent[m] is CallExpression ic && ic.Callee == m:
                    return;
                case "forEach" when _parent[m] is CallExpression fc && fc.Callee == m:
                    if (list.Kind == "HTMLCollection") { Refuse(m, "forEach on an HTMLCollection, which has none in a browser either (a for...of loop walks it)"); return; }
                    if (fc.Arguments.Count == 0 || fc.Arguments[0] is not (IFunction or Identifier)) { Refuse(fc, "forEach on a list of elements with a callback the compile cannot follow"); return; }
                    return;
            }
            Refuse(m, $".{name} of a list of elements (length, item(), forEach(), an index and for...of are translated)");
        }

        /// <summary>
        /// The classes and attributes the selectors of lookups test, against what the script changes: a
        /// list a browser works out while the page runs could then hold other elements than the page as
        /// written gives, which one fixed at compile time cannot follow.
        /// </summary>
        private void Selectors()
        {
            if (_selectors.Count == 0) return;
            var classes = new HashSet<string>(StringComparer.Ordinal);
            var attrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // attributes the script adds or takes away, not only gives another value
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            static string[] Words(string v) => v.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in _order)
            {
                foreach (var op in t.ClassOps) classes.UnionWith(op.Names);
                if (t.ClassNames.Count > 0)
                {
                    // className given only sets that all hold a class (and so does the page): that class never changes
                    var sets = t.ClassNames.SelectMany(n => n.Values.OfType<string>()).Append(t.Node.Attr("class") ?? string.Empty).Select(Words).ToList();
                    var always = new HashSet<string>(sets[0], StringComparer.Ordinal);
                    foreach (var set in sets) always.IntersectWith(set);
                    foreach (var set in sets) classes.UnionWith(set.Where(c => !always.Contains(c)));
                }
                foreach (var op in t.AttrOps)
                {
                    attrs.Add(op.Name);
                    // markup's own value for an attribute its element is made with: the attribute is there in every write
                    if (!(op.Verb == "set" && _synthetic.Contains(op.At))) present.Add(op.Name);
                }
            }
            foreach (var (selector, at) in _selectors)
            {
                foreach (Match c in Regex.Matches(selector, @"\.(-?[_a-zA-Z][\w-]*)"))
                    if (classes.Contains(c.Groups[1].Value))
                    {
                        Refuse(at, $"the selector \"{selector}\" tests the class \"{c.Groups[1].Value}\", which the script changes, so what it finds changes as the page runs (not translated yet)");
                        break;
                    }
                // `[name]` asks only whether the attribute is there; `[name=…]` reads its value too
                foreach (Match a in Regex.Matches(selector, @"\[\s*([\w-]+)\s*(\])?"))
                    if ((a.Groups[2].Success ? present : attrs).Contains(a.Groups[1].Value))
                    {
                        Refuse(at, $"the selector \"{selector}\" tests the attribute \"{a.Groups[1].Value}\", which the script changes, so what it finds changes as the page runs (not translated yet)");
                        break;
                    }
            }
        }

        /// <summary>One node of the script: an element reference, a timer, or a browser global, each checked where it stands.</summary>
        private void Visit(Node n)
        {
            if (_ignored.Contains(n)) return;
            switch (n)
            {
                // an expression that yields elements: a lookup, a list's member, a call that returns one, a name holding one
                case CallExpression c when DomCall(c) != null:
                    Source(c);
                    return;
                case MemberExpression root when IsRoot(root):
                    Source(root);
                    return;
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "item" } } im } ic when Elems(im.Object) is { List: true }:
                    Source(ic);
                    return;
                case MemberExpression { Computed: true } ix when Elems(ix.Object) is { List: true }:
                    Source(ix);
                    return;
                case CallExpression { Callee: Identifier } uc when Elems(uc) is { Neutral: false }:
                    Source(uc);
                    return;
                case MemberExpression { Computed: false, Property: Identifier { Name: "target" or "currentTarget" } } et when Elems(et) is { Neutral: false }:
                    Source(et);
                    return;
                case MemberExpression { Computed: false, Property: Identifier gn } gm when Getters(gm.Object, gn.Name, null) != null && Elems(gm) is { Neutral: false }:
                    Source(gm);
                    return;
                case ThisExpression th when Elems(th) is { Neutral: false }:
                    Source(th);
                    return;
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "closest" } } } cl when Elems(cl) is { Neutral: false }:
                    Source(cl);
                    return;
                case Identifier id when Reference(id) && (_vars.ContainsKey(id.Name)
                                            ? !(_parent.TryGetValue(id, out var decl) && decl is VariableDeclarator vd && vd.Id == id)
                                            : Decl(id) is { } d && d != id)
                                        && Elems(id) is { Neutral: false }:
                    Source(id);
                    return;

                case Identifier { Name: "document" } doc when !Declared("document") && Reference(doc):
                    if (!(_parent[doc] is MemberExpression { Computed: false, Property: Identifier { Name: "getElementById" or "querySelector" or "querySelectorAll" or "getElementsByClassName" or "getElementsByTagName" } } dm
                          && dm.Object == doc && _parent.TryGetValue(dm, out var dc) && dc is CallExpression cc && cc.Callee == dm)
                        && !(_parent[doc] is MemberExpression de && IsRoot(de)))
                        Refuse(doc, "document." + (_parent[doc] is MemberExpression { Computed: false, Property: Identifier dp } ? dp.Name : "…")
                                    + ", which is outside the translated DOM features");
                    return;

                case Identifier g when WindowNames.Contains(g.Name) && !Declared(g.Name) && Reference(g):
                    Global(g, g.Name);
                    return;

                case Identifier { Name: "window" } w when !Declared("window") && Reference(w)
                        && _parent[w] is MemberExpression { Computed: false, Property: Identifier wp } wm && wm.Object == w && WindowNames.Contains(wp.Name):
                    Global(wm, wp.Name);
                    return;

                case Identifier g when TimerNames.Contains(g.Name) && !Declared(g.Name) && Reference(g):
                    if (_parent[g] is not CallExpression tc || tc.Callee != g) { Refuse(g, g.Name + " used as a value"); return; }
                    _timers = true;
                    if (g.Name.StartsWith("set", StringComparison.Ordinal))
                    {
                        if (tc.Arguments.Count is < 1 or > 2) Refuse(tc, g.Name + " with arguments for its callback");
                        else if (tc.Arguments[0] is StringLiteral or TemplateLiteral) Refuse(tc, g.Name + " with code in a string");
                    }
                    return;

                case Identifier o when OtherGlobals.TryGetValue(o.Name, out var what) && !Declared(o.Name) && Reference(o):
                    Refuse(o, what + ", which is outside the translated features");
                    return;
            }
        }

        /// <summary>Whether an identifier is a reference to a name, rather than a property name or an object key.</summary>
        private bool Reference(Identifier id)
        {
            if (!_parent.TryGetValue(id, out var p)) return true;
            return p switch
            {
                MemberExpression m => m.Object == id || m.Computed,
                Property prop => prop.Value == id || prop.Computed,
                MethodDefinition or PropertyDefinition => false,
                _ => true,
            };
        }

        /// <summary>The element(s) a DOM operation acts on, and whether the Lua picks one of them at run time.</summary>
        private sealed class Recv
        {
            public readonly Expression At;
            public readonly List<Target> Ts;
            public readonly bool Dyn;
            public Recv(Expression at, List<Target> ts, bool dyn) { At = at; Ts = ts; Dyn = dyn; }
            public string Name => Ts.Count == 0 ? "an element the page as written does not have"
                : Ts.Count == 1 ? "\"" + Ts[0].Name + "\"" : $"one of {Ts.Count} elements chosen at run time (\"{Ts[0].Name}\", …)";
        }

        /// <summary>
        /// Whether an expression that yields an element does nothing else - a lookup, a name, a list's member,
        /// a function that only looks one up - so an operation on it needs no Lua to find it.
        /// </summary>
        private bool Lookupish(Expression e, int depth = 0) => depth < 4 && e switch
        {
            Identifier => true,
            MemberExpression root when IsRoot(root) => true,
            CallExpression c when DomCall(c) is { } q => (q.Under == null || Lookupish(q.Under, depth + 1)) && c.Arguments.All(a => a is Expression x && Pure(x)),
            CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "item" } } im } ic
                => Lookupish(im.Object, depth + 1) && ic.Arguments.All(a => a is Expression x && Pure(x)),
            MemberExpression { Computed: true } ix => Lookupish(ix.Object, depth + 1) && Pure(ix.Property),
            CallExpression { Callee: Identifier f } uc when Function(f) is { } fn
                => uc.Arguments.All(a => a is Expression x && Pure(x))
                   && (fn.Body is Expression || fn.Body is BlockStatement { Body.Count: 1 } b && b.Body[0] is ReturnStatement)
                   && Returns(fn).All(r => r != null && Lookupish(r, depth + 1)),
            _ => Pure(e),
        };

        private readonly HashSet<Expression> _used = new();

        /// <summary>A DOM operation evaluated for its effect, on every element it can act on.</summary>
        private void Op(Recv r, Expression at)
        {
            _ops[at] = r.Ts;
            if (r.Dyn) _recv[at] = r.At;
        }

        /// <summary>A DOM read: the Lua for the one element, or a pick among the elements it can read.</summary>
        private void Read(Recv r, Node at, Func<JsToLua, Target, Func<Node, string>, string> body)
            => _lua[at] = lua => r.Dyn ? Dispatch(r.At, r.Ts, lua, (x, tr) => "return " + body(lua, x, tr)) : body(lua, r.Ts[0], lua.Translate);

        /// <summary>What the script does with an element (or one of a set, chosen at run time), checked against the features translated here.</summary>
        private void Use(Expression at, Els els)
        {
            // `(on ? a : b).textContent = …` is reached from a and from b: one operation
            if (!_used.Add(at)) return;
            var r = new Recv(at, els.Ts, !(els.One && Lookupish(at)));
            var p = _parent[at];
            if (p is not MemberExpression m || m.Object != at || m.Computed || m.Property is not Identifier prop)
            {
                Refuse(at, $"{r.Name} used as a value (only its properties are translated)");
                return;
            }
            var gp = _parent[m];
            switch (prop.Name)
            {
                case "innerHTML":
                    if (_parent[m] is AssignmentExpression mw && _markupSeen.Contains(mw)) return;
                    Refuse(m, _parent[m] is AssignmentExpression { Operator: Operator.AdditionAssignment } ? $"markup added to {r.Name} with += outside a list the compile can bound (`el.innerHTML = …` then += in loops, in one block, is followed)"
                        : WrittenTo(m) ? $"this write to .innerHTML of {r.Name}" : $"reading .innerHTML of {r.Name} (not translated yet)");
                    return;

                // a lookup under this element: a source of elements of its own
                case "querySelector" or "querySelectorAll" or "getElementsByClassName" or "getElementsByTagName" or "closest" when gp is CallExpression qc && qc.Callee == m:
                    return;

                case "matches" when gp is CallExpression mc && mc.Callee == m:
                    {
                        if (mc.Arguments.Count != 1 || mc.Arguments[0] is not Expression ma || Finite(ma) is not [string sel]) { Refuse(mc, "matches with a selector only known at run time"); return; }
                        if (Matcher(sel, out var why) is not { } match) { Refuse(mc, why!); return; }
                        _bool.Add(mc);
                        Read(r, mc, (_, x, _) => match(x.Node) ? "true" : "false");
                        return;
                    }

                case "id" when !WrittenTo(m):
                    Read(r, m, (_, x, _) => Q(x.Unnamed ? string.Empty : x.Node.Attr("id") ?? string.Empty));
                    return;

                case "tagName" when !WrittenTo(m):
                    Read(r, m, (_, x, _) => Q((x.Node.Tag ?? string.Empty).ToUpperInvariant()));
                    return;

                case "textContent" or "innerText":
                    if (Assigned(m) is { } text) Text(r, m, text.At, text.Value);
                    else if (Compound(m) is { } more)
                    {
                        // `el.textContent += v` is `el.textContent = el.textContent + v`: the text read back, then written
                        foreach (var x in r.Ts) x.TextRead = true;
                        Read(r, m, (lua, x, _) => prop.Name == "textContent" ? TextRead(x, m, lua) : InnerTextRead(x, m, lua));
                        Text(r, m, more.At, more.Value);
                    }
                    else if (!WrittenTo(m))
                    {
                        // what the program last wrote, or the page's own text: never a DOM
                        foreach (var x in r.Ts) x.TextRead = true;
                        Read(r, m, (lua, x, _) => prop.Name == "textContent" ? TextRead(x, m, lua) : InnerTextRead(x, m, lua));
                    }
                    else Refuse(m, $"this write to .{prop.Name} of {r.Name}");
                    return;

                case "style":
                    if (gp is MemberExpression s && s.Object == m && (s.Computed ? s.Property as StringLiteral is { } : s.Property is Identifier)
                        && Assigned(s) is { } style)
                    {
                        var name = DomSlots.Dashed(s.Property is Identifier si ? si.Name : ((StringLiteral)s.Property).Value);
                        foreach (var x in r.Ts)
                        {
                            if (!x.Styles.TryGetValue(name, out var list)) x.Styles[name] = list = new();
                            list.Add((style.At, style.Value));
                        }
                        Op(r, style.At);
                    }
                    else if (gp is MemberExpression rs && rs.Object == m && (rs.Computed ? rs.Property is StringLiteral : rs.Property is Identifier)
                             && !WrittenTo(rs) && !(_parent[rs] is CallExpression rc && rc.Callee == rs)
                             && DomSlots.Dashed(rs.Property is Identifier ri ? ri.Name : ((StringLiteral)rs.Property).Value) is var read
                             && read is not ("css-text" or "length" or "parent-rule"))
                    {
                        foreach (var x in r.Ts) x.StyleReads.Add(read);
                        Read(r, rs, (lua, x, _) => StyleRead(x, read, rs, lua));
                    }
                    else Refuse(m, $"this use of .style on {r.Name} (assigning and reading style.<property> are translated)");
                    return;

                case "className":
                    if (Assigned(m) is { } cls) ClassName(r, cls.At, cls.Value);
                    else if (!WrittenTo(m))
                    {
                        foreach (var x in r.Ts) x.ClassRead = true;
                        Read(r, m, (_, x, _) => ClassNameRead(x));
                    }
                    else Refuse(m, $"this use of .className of {r.Name} (a class added with += is classList.add)");
                    return;

                case "getAttribute" or "hasAttribute" or "setAttribute" or "removeAttribute" or "toggleAttribute":
                    if (gp is CallExpression ac && ac.Callee == m && !ac.Arguments.Any(a => a is SpreadElement)) Attribute(r, prop.Name, ac);
                    else Refuse(m, $".{prop.Name} of {r.Name} used as a value");
                    return;

                case "hidden":
                    if (Assigned(m) is { } hid) AttrWrite(r, hid.At, "hidden", "hidden", hid.Value);
                    else if (!WrittenTo(m)) { _bool.Add(m); Read(r, m, (_, x, tr) => AttrRead(x, true, "hidden", null, tr)); }
                    else Refuse(m, $"this use of .hidden of {r.Name}");
                    return;

                case "dataset":
                    Dataset(r, m);
                    return;

                // An element that is no scroll container has no offset: it reads 0 and a write does nothing, as in a browser.
                case "scrollTop" or "scrollLeft":
                    if (r.Ts.FirstOrDefault(x => ScrollBox(_built.CssOf(x.Ve))) is { } box)
                    {
                        Refuse(m, WrittenTo(m)
                            ? $".{prop.Name} written on \"{box.Name}\", a scroll box (not translated yet: the vector mod's SC takes a jump as so=/sov=)"
                            : $".{prop.Name} of \"{box.Name}\", a scroll box: the chip never learns the offset the player scrolled it to "
                              + "(the vector mod keeps it on each client and tells only C#, VectorGraphic.ScrollChanged; reading it needs the offset sent to the chip, as on_click is)");
                        return;
                    }
                    if (Assigned(m) is { } scroll) Read(r, scroll.At, (_, _, tr) => tr(scroll.Value));
                    else if (Compound(m) is { } moved)
                    {
                        // `el.scrollTop += v`: the offset read (0), the sum worked out for its effects, the write dropped
                        Read(r, m, (_, _, _) => "0");
                        Read(r, moved.At, (_, _, tr) => tr(moved.Value));
                    }
                    else if (!WrittenTo(m)) Read(r, m, (_, _, _) => "0");
                    else Refuse(m, $"this use of .{prop.Name} of {r.Name}");
                    return;

                case "classList":
                    ClassList(r, m);
                    return;

                case "onclick":
                    if (Assigned(m) is { } handler)
                    {
                        if (ReadsEvent(handler.Value)) return;
                        foreach (var x in r.Ts) x.Listens = true;
                        Op(r, handler.At);
                    }
                    else Refuse(m, $"reading .onclick of {r.Name}");
                    return;

                case "addEventListener":
                    if (gp is CallExpression ec && ec.Callee == m && Effect(ec))
                    {
                        if (ec.Arguments.Count != 2 || ec.Arguments[0] is not StringLiteral kind) { Refuse(ec, $"addEventListener on {r.Name} with options or a computed event name"); return; }
                        if (kind.Value != "click") { Refuse(ec, $"the \"{kind.Value}\" event on {r.Name}: the vector mod delivers only clicks"); return; }
                        if (ReadsEvent(ec.Arguments[1])) return;
                        foreach (var x in r.Ts) x.Listens = true;
                        Op(r, ec);
                    }
                    else Refuse(m, $"addEventListener on {r.Name} used as a value");
                    return;

                default:
                    Refuse(m, $".{prop.Name} of {r.Name}, which is outside the translated DOM features");
                    return;
            }
        }

        /// <summary>A text written whole: each element it can go to must hold only text.</summary>
        private void Text(Recv r, MemberExpression m, Expression at, Expression value)
        {
            foreach (var x in r.Ts)
                if (x.Ve is not Label) { Refuse(m, $"textContent of \"{x.Name}\", which holds elements rather than text, replaces them"); return; }
            foreach (var x in r.Ts) x.Texts.Add((at, value));
            Op(r, at);
        }

        /// <summary>className (or classList.value) given a value from a fixed set: the class states it draws.</summary>
        private void ClassName(Recv r, Expression at, Expression value)
        {
            if (Finite(value) is not { } values || values.Any(v => v is not string)) { Refuse(value, $"className of {r.Name} set to a value only known at run time"); return; }
            foreach (var x in r.Ts) x.ClassNames.Add((at, values));
            Op(r, at);
        }

        /// <summary>`el.x op= v` evaluated for its effect, as `el.x = el.x op v`: the write it stands for.</summary>
        private (Expression At, Expression Value)? Compound(MemberExpression target)
        {
            if (_parent[target] is not AssignmentExpression a || a.Left != target || !Effect(a)) return null;
            Operator? op = a.Operator switch
            {
                Operator.AdditionAssignment => Operator.Addition,
                Operator.SubtractionAssignment => Operator.Subtraction,
                Operator.MultiplicationAssignment => Operator.Multiplication,
                Operator.DivisionAssignment => Operator.Division,
                Operator.RemainderAssignment => Operator.Remainder,
                Operator.ExponentiationAssignment => Operator.Exponentiation,
                _ => null,
            };
            return op == null ? null : (a, new NonLogicalBinaryExpression(op.Value, target, a.Right));
        }

        /// <summary>classList: add, remove, toggle, contains, item, length and value.</summary>
        private void ClassList(Recv r, MemberExpression m)
        {
            var gp = _parent[m];
            if (gp is not MemberExpression { Computed: false, Property: Identifier verb } lm || lm.Object != m)
            {
                Refuse(m, $"classList of {r.Name} used as a value");
                return;
            }
            var call = _parent[lm] is CallExpression c && c.Callee == lm ? c : null;
            switch (verb.Name)
            {
                case "contains" when call is { Arguments.Count: 1 } && call.Arguments[0] is Expression key:
                    _bool.Add(call);
                    Read(r, call, (_, x, tr) => Contains(x, key, tr));
                    return;

                case "item" when call is { Arguments.Count: 1 } && call.Arguments[0] is Expression index:
                    foreach (var x in r.Ts) x.ClassRead = true;
                    _null[call] = "null";
                    Read(r, call, (_, x, tr) => ClassItem(x, index, tr));
                    return;

                case "length" when call == null && !WrittenTo(lm):
                    Read(r, lm, (_, x, _) => ClassCount(x));
                    return;

                case "value" when call == null:
                    if (Assigned(lm) is { } set) ClassName(r, set.At, set.Value);
                    else if (!WrittenTo(lm))
                    {
                        foreach (var x in r.Ts) x.ClassRead = true;
                        Read(r, lm, (_, x, _) => ClassNameRead(x));
                    }
                    else Refuse(lm, $"this use of classList.value of {r.Name}");
                    return;

                case "add" or "remove" or "toggle" when call != null && Effect(call):
                    {
                        var names = new List<string>();
                        var args = verb.Name == "toggle" ? call.Arguments.Take(1) : call.Arguments;
                        foreach (var a in args)
                            if (a is StringLiteral { Value: { Length: > 0 } cn } && cn.IndexOfAny(new[] { ' ', '\t', '\n' }) < 0) names.Add(cn);
                            else { Refuse(a, $"classList.{verb.Name} on {r.Name} with a class only known at run time"); return; }
                        if (names.Count == 0 || verb.Name == "toggle" && call.Arguments.Count > 2) { Refuse(call, $"classList.{verb.Name} on {r.Name} with these arguments"); return; }
                        var force = verb.Name == "toggle" && call.Arguments.Count == 2 ? (Expression)call.Arguments[1] : null;
                        foreach (var x in r.Ts) x.ClassOps.Add((call, verb.Name, names, force));
                        Op(r, call);
                        return;
                    }
            }
            Refuse(m, $"classList.{verb.Name} on {r.Name} (add, remove and toggle as statements, contains, item, length and value are translated)");
        }
        /// <summary>`target = value` evaluated for its effect: the only place a DOM write is translated.</summary>
        private (Expression At, Expression Value)? Assigned(MemberExpression target)
            => _parent[target] is AssignmentExpression { Operator: Operator.Assignment } a && a.Left == target && Effect(a)
                ? (a, a.Right) : null;

        /// <summary>Whether an expression's value is unused: a statement of its own, or a concise arrow's body.</summary>
        private bool Effect(Expression e)
            => _parent.TryGetValue(e, out var p) && (p is ExpressionStatement || p is ArrowFunctionExpression a && a.Body == e);

        /// <summary>
        /// A click handler that reads its event object other than as `e.target` and `e.currentTarget` - the element a
        /// click landed on and the element listening, which the Lua hands it as element numbers: refused.
        /// </summary>
        private bool ReadsEvent(Node handler)
        {
            if (Callback(handler) is not { } fn) return false;
            var body = (Node)fn.Body;
            for (var k = 0; k < fn.Params.Count; k++)
            {
                var p = fn.Params[k];
                if (p is not Identifier pid) { Refuse(p, "a click handler that unpacks its event"); return true; }
                foreach (var x in Markup.Everything(body))
                {
                    if (x is not Identifier i || i.Name != pid.Name || !Reference(i) || Decl(i) != pid) continue;
                    if (k == 0 && _parent[i] is MemberExpression { Computed: false, Property: Identifier { Name: "target" or "currentTarget" } } m && m.Object == i && !WrittenTo(m))
                    {
                        _events = true;
                        continue;
                    }
                    Refuse(handler, $"a click handler that reads its event object ({pid.Name}) other than its target and currentTarget, which is not translated yet");
                    return true;
                }
            }
            return false;
        }

        /// <summary>Each click handler's event parameter, and the elements it listens on: what `e.target` can be is worked out from them.</summary>
        private readonly Dictionary<Identifier, List<Expression>> _eventOf = new();
        private bool _events;

        private void Handlers()
        {
            foreach (var n in Markup.Everything(_ast))
            {
                Node? handler = null;
                Expression? on = null;
                if (n is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "addEventListener" } } am } ac
                    && ac.Arguments.Count == 2 && ac.Arguments[0] is StringLiteral { Value: "click" })
                    (handler, on) = (ac.Arguments[1], am.Object);
                else if (n is AssignmentExpression { Operator: Operator.Assignment, Left: MemberExpression { Computed: false, Property: Identifier { Name: "onclick" } } om } oa)
                    (handler, on) = (oa.Right, om.Object);
                if (handler == null || Callback(handler) is not { } fn) continue;
                if (!_listenerOn.TryGetValue(fn, out var ons)) _listenerOn[fn] = ons = new List<Expression>();
                ons.Add(on!);
                if (handler is Identifier named) _listenerNames.Add(named);
                if (fn.Params.Count == 0 || fn.Params[0] is not Identifier ev) continue;
                if (!_eventOf.TryGetValue(ev, out var list)) _eventOf[ev] = list = new List<Expression>();
                list.Add(on!);
            }
        }

        /// <summary>Each click listener function, and the elements it is registered on; and the names that register one by name.</summary>
        private readonly Dictionary<IFunction, List<Expression>> _listenerOn = new();
        private readonly HashSet<Identifier> _listenerNames = new();

        /// <summary>
        /// `this` in a click listener (an arrow's `this` is the function's around it): the element the listener is on,
        /// as `e.currentTarget` is. Null when it is in a function that is not only ever a click listener.
        /// </summary>
        private Els? ThisElems(ThisExpression th)
        {
            Node n = th;
            var arrow = false;
            while (_parent.TryGetValue(n, out var p) && p is not (FunctionExpression or FunctionDeclaration))
            {
                arrow |= p is ArrowFunctionExpression;
                n = p;
            }
            if (!_parent.TryGetValue(n, out var f) || f is not IFunction fn || !_listenerOn.TryGetValue(fn, out var ons)) return null;
            // a listener also called by name, or handed on, has another `this` there
            if (NameOf(fn) is { } name && _refs.TryGetValue(name, out var refs) && refs.Any(r => !_listenerNames.Contains(r)))
                return Els.Refused($"`this` in {name.Name}, which is called other than as a click listener (its `this` there is not the element)");
            var listening = Els.Choice(ons.Select(o => Elems(o)));
            if (listening == null || listening.Bad != null) return listening;
            if (listening.List) return Els.Refused("`this` in a click listener on a list of elements");
            // the element a click is on is read when the listener starts; a later run of an arrow in it needs it kept
            if (arrow && !listening.One) return Els.Refused("`this` in an arrow function inside a click listener on more than one element (not translated yet)");
            if (!listening.One) _events = true;
            return listening;
        }

        /// <summary>
        /// `e.target` of a click listener: the listening element and every element in it with a box of its own, each
        /// made a hit region so the click names it; `e.currentTarget`: the listening element.
        /// </summary>
        private Els? EventElems(Identifier decl, bool current)
        {
            var listening = Els.Choice(_eventOf[decl].Select(o => Elems(o)));
            if (listening == null || listening.Bad != null) return listening;
            if (listening.List) return Els.Refused("a click listener's event on a list of elements");
            if (current) return listening;
            var e = new Els { Null = listening.Null };
            foreach (var l in listening.Ts)
                foreach (var ve in Subtree(l.Ve))
                    if (!string.IsNullOrEmpty(ve.name) && _built.NodeOf.TryGetValue(ve, out var node) && _built.ById.TryGetValue(ve.name, out var named) && named == ve)
                    {
                        var t = TargetOf(ve, node);
                        t.Hit = true;
                        if (!e.Ts.Contains(t)) e.Ts.Add(t);
                    }
            return e;
        }

        /// <summary>`el.closest(selector)` with a selector the compile knows: for each element it can be, the nearest of it and its ancestors matching.</summary>
        private Els Closest(CallExpression c, Els from, Inlined? bind)
        {
            if (c.Arguments.Count != 1 || c.Arguments[0] is not Expression a || Finite(a, null, bind) is not [string sel]) return Els.Refused("closest with a selector only known at run time");
            if (Matcher(sel, out var why) is not { } match) return Els.Refused(why!);
            var e = new Els();
            var table = new Dictionary<Target, Target?>();
            foreach (var t in from.Ts)
            {
                Target? found = null;
                for (var n = t.Node; n != null; n = n.Parent)
                {
                    if (n.IsText || !match(n)) continue;
                    if (n.Attr("id") is not { } id || !_built.ById.TryGetValue(id, out var ve) || ve == null || !_built.NodeOf.TryGetValue(ve, out var own) || own != n)
                        return Els.Refused($"closest(\"{sel}\") finds a <{n.Tag}> with no box of its own (it is drawn as part of its parent)");
                    found = TargetOf(ve, n);
                    break;
                }
                table[t] = found;
                if (found == null) e.Null = true;
                else if (!e.Ts.Contains(found)) e.Ts.Add(found);
            }
            _closestOf[c] = table;
            return e;
        }

        private readonly Dictionary<CallExpression, Dictionary<Target, Target?>> _closestOf = new();
        private readonly Dictionary<CallExpression, string> _closestTable = new();

        /// <summary>A selector as a test of one node, as the page's own CSS is matched; null, with the reason, for one the compile cannot match as a browser does.</summary>
        private static Func<HtmlNode, bool>? Matcher(string selector, out string? why)
        {
            why = null;
            if (selector.Contains("::", StringComparison.Ordinal) || Regex.IsMatch(selector, @":(hover|active|focus|focus-within|focus-visible|visited|link|any-link|target|checked|indeterminate|placeholder-shown|default|valid|invalid|in-range|out-of-range|autofill|user-invalid|user-valid|defined|scope|has)\b"))
            {
                why = $"the selector \"{selector}\", which asks about a state or a part the page does not keep (not translated yet)";
                return null;
            }
            var parsed = new List<CssSelector>();
            var warned = new List<string>();
            foreach (var part in CssParser.SplitTopLevel(selector, ','))
            {
                var s = part.Trim().Length == 0 ? null : CssParser.ParseSelector(part.Trim(), warned.Add);
                if (s == null || warned.Count > 0) { why = $"the selector \"{selector}\", which does not parse as a supported selector{(warned.Count > 0 ? " (" + warned[0] + ")" : string.Empty)}"; return null; }
                parsed.Add(s);
            }
            return n => parsed.Any(s => s.Matches(n));
        }

        /// <summary>The Lua of closest(): from the element's number to what it finds, a table made once.</summary>
        private string ClosestLua(CallExpression c, JsToLua lua)
        {
            if (!_closestTable.TryGetValue(c, out var name))
            {
                name = "V_CL" + (_tables.Count + 1).ToString(CultureInfo.InvariantCulture);
                var entries = _closestOf[c].Where(p => p.Value != null).Select(p => "[" + p.Key.Number.ToString(CultureInfo.InvariantCulture) + "] = " + p.Value!.Number.ToString(CultureInfo.InvariantCulture));
                _tables.Add("local " + name + " = { " + string.Join(", ", entries) + " } -- what closest() finds from each element\n");
                _closestTable[c] = name;
            }
            return name + "[" + lua.Translate(((MemberExpression)c.Callee).Object) + "]";
        }

        /// <summary>Whether an expression is written to rather than read: assigned, updated or deleted.</summary>
        private bool WrittenTo(Expression e)
            => _parent.TryGetValue(e, out var p)
               && (p is AssignmentExpression a && a.Left == e || p is UpdateExpression || p is NonUpdateUnaryExpression { Operator: Operator.Delete });

        /// <summary>Whether evaluating an expression does nothing but yield its value: no call, assignment, update, new or delete in it.</summary>
        private static bool Pure(Node e) => e switch
        {
            Acornima.Ast.Literal or Identifier or ThisExpression or FunctionExpression or ArrowFunctionExpression => true,
            TemplateLiteral t => t.Expressions.All(Pure),
            MemberExpression m => Pure(m.Object) && (!m.Computed || Pure(m.Property)),
            NonLogicalBinaryExpression b => Pure(b.Left) && Pure(b.Right),
            LogicalExpression l => Pure(l.Left) && Pure(l.Right),
            NonUpdateUnaryExpression u => u.Operator != Operator.Delete && Pure(u.Argument),
            ConditionalExpression c => Pure(c.Test) && Pure(c.Consequent) && Pure(c.Alternate),
            ArrayExpression a => a.Elements.All(x => x == null || x is not SpreadElement && Pure(x)),
            ObjectExpression o => o.Properties.All(p => p is Property { Computed: false, Value: var v } && Pure(v)),
            CallExpression { Callee: MemberExpression { Object: Identifier { Name: "document" }, Property: Identifier { Name: "getElementById" } } } c
                => c.Arguments.All(Pure),
            _ => false,
        };

        // ---- the browser's globals: window, location, storage, console -------------------------------

        private const string Elsewhere = "a console holds only the page compiled onto it, and has no network to fetch another";

        /// <summary>A global of the browser, bare or as a member of <c>window</c>, checked where it stands.</summary>
        private void Global(Expression g, string name)
        {
            var p = _parent[g];
            if (p is NonUpdateUnaryExpression { Operator: Operator.TypeOf } && name is not ("innerWidth" or "innerHeight" or "devicePixelRatio"))
            {
                _lua[p] = _ => "\"object\"";
                return;
            }
            switch (name)
            {
                case "innerWidth" or "innerHeight" or "devicePixelRatio":
                    if (WrittenTo(g)) { Refuse(g, name + " written by the script (not translated yet)"); return; }
                    // The console is the window, and its size is the one the page is compiled for; a CSS
                    // pixel is one unit of the console's canvas.
                    _lua[g] = _ => name switch
                    {
                        "innerWidth" => JsToLuaNumber(Math.Floor(_size.x)),
                        "innerHeight" => JsToLuaNumber(Math.Floor(_size.y)),
                        _ => "1",
                    };
                    return;
                case "console": Console(g); return;
                case "location": Location(g); return;
                default: Storage(g, name); return;
            }
        }

        /// <summary>
        /// A console call. A chip has no developer console, so the call goes - and what its arguments do (a
        /// call, an assignment) stays, evaluated as the browser evaluates them.
        /// </summary>
        private void Console(Expression con)
        {
            if (_parent[con] is MemberExpression { Computed: false, Property: Identifier } m && m.Object == con && !WrittenTo(m))
            {
                if (_parent[m] is CallExpression c && c.Callee == m)
                {
                    var kept = new List<Expression>();
                    foreach (var a in c.Arguments)
                    {
                        var arg = a is SpreadElement spread ? spread.Argument : (Expression)a;
                        if (!Pure(arg)) kept.Add(arg);
                        else foreach (var x in Markup.Everything(a)) _ignored.Add(x);
                    }
                    _effects.Add(c);
                    _consoleKept[c] = kept;
                    _lua[c] = lua =>
                    {
                        if (kept.Count == 0) return "nil";
                        _void = true;
                        return "v_void(" + string.Join(", ", kept.Select(lua.Translate)) + ")";
                    };
                }
                // passed along as a function (`list.forEach(console.log)`): one that does nothing
                else _lua[m] = _ => { _void = true; return "v_void"; };
                return;
            }
            Refuse(con, "console used as a value (its methods are removed at compile)");
        }

        private void Location(Expression loc)
        {
            var p = _parent[loc];
            if (p is AssignmentExpression { Operator: Operator.Assignment } set && set.Left == loc)
            {
                Navigate(set, set.Right, "location = …");
                return;
            }
            if (p is MemberExpression { Computed: false, Property: Identifier prop } m && m.Object == loc)
            {
                var gp = _parent[m];
                if (Blank.ContainsKey(prop.Name))
                {
                    if (gp is AssignmentExpression { Operator: Operator.Assignment } a && a.Left == m)
                    {
                        if (prop.Name == "hash") HashWrite(a, a.Right);
                        else if (prop.Name == "href") Navigate(a, a.Right, "location.href = …");
                        else Refuse(a, $"location.{prop.Name} = …, which loads another document: {Elsewhere}");
                        return;
                    }
                    if (!WrittenTo(m))
                    {
                        var what = prop.Name;
                        _lua[m] = _ => LocationRead(what);
                        return;
                    }
                }
                else if (gp is CallExpression c && c.Callee == m)
                    switch (prop.Name)
                    {
                        case "toString":
                            _lua[c] = _ => LocationRead("href");
                            return;
                        case "assign" or "replace" when c.Arguments.Count >= 1 && c.Arguments[0] is Expression url:
                            Navigate(c, url, $"location.{prop.Name}()");
                            return;
                        case "reload":
                            // the page starts again from its source, as a browser loads it again (the
                            // storage and the URL stay); after the script that asked has finished
                            _reload = true;
                            _effects.Add(c);
                            _lua[c] = _ => "v_askreload()";
                            return;
                    }
                else if (prop.Name == "ancestorOrigins" && gp is MemberExpression { Computed: false, Property: Identifier ap } am && am.Object == m)
                {
                    // an empty list: a console's page is embedded in nothing
                    if (ap.Name == "length" && !WrittenTo(am)) { _lua[am] = _ => "0"; return; }
                    if (ap.Name is "item" or "contains" && _parent[am] is CallExpression ac && ac.Callee == am && ac.Arguments.All(Pure))
                    {
                        if (ap.Name == "item") _null[ac] = "null"; else _bool.Add(ac);
                        _lua[ac] = _ => ap.Name == "item" ? "nil" : "false";
                        return;
                    }
                }
            }
            Refuse(loc, "location used this way (its properties, assign, replace, reload and toString are translated)");
        }

        /// <summary>
        /// A navigation. A fragment of this page (<c>#settings</c>) is the one place a console page can go: it
        /// stays, and only its hash changes. Anywhere else is another document.
        /// </summary>
        private void Navigate(Expression at, Expression url, string what)
        {
            if (Fragment(url)) { HashWrite(at, url); return; }
            Refuse(at, $"{what} to {(Finite(url) != null ? "another document" : "a URL only known at run time, which may be another document")}: {Elsewhere}");
        }

        /// <summary>Whether a URL is a fragment of this page in every run: it starts with `#`.</summary>
        private bool Fragment(Expression url) => url switch
        {
            StringLiteral s => s.Value.StartsWith("#", StringComparison.Ordinal),
            TemplateLiteral t => (t.Quasis[0].Value.Cooked ?? string.Empty).StartsWith("#", StringComparison.Ordinal),
            NonLogicalBinaryExpression { Operator: Operator.Addition } b => Fragment(b.Left),
            ConditionalExpression c => Fragment(c.Consequent) && Fragment(c.Alternate),
            _ => Finite(url) is { Count: > 0 } all && all.All(v => v is string s && s.StartsWith("#", StringComparison.Ordinal)),
        };

        private void HashWrite(Expression at, Expression value)
        {
            _hashWrites = true;
            _effects.Add(at);
            _lua[at] = lua => "v_sethash(" + lua.Translate(value) + ")";
        }

        /// <summary>A location property as the program holds it: the hash it last set, or the constant about:blank has.</summary>
        private string LocationRead(string what)
            => _hashWrites && what is "hash" or "href" ? (what == "hash" ? "V_HASH" : "V_HREF") : Q(Blank[what]);

        /// <summary>
        /// localStorage (the chip's own store, which outlives a game restart as a browser's outlives a reload)
        /// and sessionStorage (the program's memory, which a location.reload keeps).
        /// </summary>
        private void Storage(Expression s, string name)
        {
            var table = name == "localStorage" ? "V_LS" : "V_SS";
            if (name == "localStorage") _localStorage = true; else _sessionStorage = true;
            if (_parent[s] is MemberExpression { Computed: false, Property: Identifier prop } m && m.Object == s)
            {
                if (prop.Name == "length" && !WrittenTo(m)) { _lua[m] = _ => table + ".n"; return; }
                if (_parent[m] is CallExpression c && c.Callee == m && !c.Arguments.Any(a => a is SpreadElement))
                {
                    var args = c.Arguments.Cast<Expression>().ToList();
                    Func<JsToLua, string>? f = (prop.Name, args.Count) switch
                    {
                        ("getItem", >= 1) => lua => table + ".vals[" + Str(lua, args[0]) + "]",
                        ("setItem", >= 2) => lua => "v_sset(" + table + ", " + Str(lua, args[0]) + ", " + Str(lua, args[1]) + ")",
                        ("removeItem", >= 1) => lua => "v_srm(" + table + ", " + Str(lua, args[0]) + ")",
                        ("clear", _) => _ => "v_sclear(" + table + ")",
                        ("key", >= 1) => lua => "v_skeyat(" + table + ", " + lua.Translate(args[0]) + ")",
                        _ => null,
                    };
                    if (f != null)
                    {
                        if (prop.Name is "setItem" or "removeItem" or "clear") _effects.Add(c);
                        if (prop.Name is "getItem" or "key") _null[c] = "null";
                        _lua[c] = f;
                        return;
                    }
                }
            }
            Refuse(s, $"{name} used this way (getItem, setItem, removeItem, clear, key and length are translated; named properties are not yet)");
        }

        /// <summary>A value as JavaScript's String() gives it: a literal as it stands.</summary>
        private static string Str(JsToLua lua, Expression e) => e is StringLiteral s ? JsToLua.Quote(s.Value) : "js_str(" + lua.Translate(e) + ")";

        // ---- attributes -----------------------------------------------------------------------------------

        private void Attribute(Recv r, string verb, CallExpression call)
        {
            var args = call.Arguments.Cast<Expression>().ToList();
            if (args.Count < (verb == "setAttribute" ? 2 : 1)) { Refuse(call, $"{verb} on {r.Name} without its arguments"); return; }
            var read = verb is "getAttribute" or "hasAttribute";
            if (args[0] is not StringLiteral lit)
            {
                if (read)
                {
                    foreach (var x in r.Ts) x.AttrsByName = true;
                    if (verb == "hasAttribute") _bool.Add(call); else _null[call] = "null";
                    Read(r, call, (_, x, tr) => AttrRead(x, verb == "hasAttribute", null, args[0], tr, call));
                }
                else Refuse(call, $"{verb} on {r.Name} with a name only known at run time");
                return;
            }
            var name = lit.Value.ToLowerInvariant();
            if (read)
            {
                if (name is "class" or "style") Refuse(call, $"{verb}('{name}') on {r.Name} (className, classList and style are translated; the attribute itself is not yet)");
                else
                {
                    if (verb == "hasAttribute") _bool.Add(call); else _null[call] = "null";
                    Read(r, call, (_, x, tr) => AttrRead(x, verb == "hasAttribute", name, null, tr, call));
                }
                return;
            }
            AttrWrite(r, call, name, verb switch { "setAttribute" => "set", "removeAttribute" => "remove", _ => "toggle" },
                      verb == "removeAttribute" || args.Count < 2 ? null : args[1]);
        }

        /// <summary>
        /// An attribute write. One that CSS selects on changes which rules match, so it is laid out as a
        /// class is: a state per value it can hold. `hidden` is the browser's own `[hidden] {display: none}`.
        /// Any other is the program's own value, for the script to read back.
        /// </summary>
        private void AttrWrite(Recv r, Expression at, string name, string verb, Expression? value)
        {
            if (name is "class" or "style" or "id") { Refuse(at, $"the {name} attribute written by {verb} on {r.Name} (className, classList and style are translated; this is not yet)"); return; }
            if (name.StartsWith("on", StringComparison.Ordinal)) { Refuse(at, $"an event handler attribute ({name}) written on {r.Name}"); return; }
            if (Drawn.Contains(name)) { Refuse(at, $"the {name} attribute of {r.Name}, which the page draws from directly rather than through CSS (not translated yet)"); return; }
            var facet = name == "hidden" || _built.AttributeSelectors.Contains(name);
            if (facet && verb == "set" && (Finite(value!) is not { } values || values.Any(v => v is not (string or double))))
            {
                Refuse(value!, $"the {name} attribute of {r.Name}, which CSS selects on, set to a value only known at run time");
                return;
            }
            var ops = new Dictionary<Target, AttrOp>();
            foreach (var x in r.Ts)
            {
                ops[x] = new AttrOp { At = at, Name = name, Verb = verb, Value = value, Facet = facet };
                x.AttrOps.Add(ops[x]);
            }
            if (verb != "toggle") _effects.Add(at);
            _lua[at] = lua => r.Dyn ? Dispatch(r.At, r.Ts, lua, (x, tr) => "return " + AttrLua(x, ops[x], tr)) : AttrLua(r.Ts[0], ops[r.Ts[0]], lua.Translate);
        }

        /// <summary><c>el.dataset.fooBar</c>: the attribute <c>data-foo-bar</c>, read, written, deleted or tested with `in`.</summary>
        private void Dataset(Recv r, MemberExpression ds)
        {
            static string Data(string key) => "data-" + Regex.Replace(key, "[A-Z]", c => "-" + c.Value.ToLowerInvariant());
            var gp = _parent[ds];
            if (gp is MemberExpression d && d.Object == ds && (d.Computed ? d.Property is StringLiteral : d.Property is Identifier))
            {
                var name = Data(d.Property is Identifier k ? k.Name : ((StringLiteral)d.Property).Value);
                if (Assigned(d) is { } w) { AttrWrite(r, w.At, name, "set", w.Value); return; }
                if (_parent[d] is NonUpdateUnaryExpression { Operator: Operator.Delete } del) { AttrWrite(r, del, name, "remove", null); return; }
                if (!WrittenTo(d) && !(_parent[d] is CallExpression dc && dc.Callee == d)) { _null[d] = "undefined"; Read(r, d, (_, x, tr) => AttrRead(x, false, name, null, tr, d)); return; }
            }
            else if (gp is NonLogicalBinaryExpression { Operator: Operator.In, Left: StringLiteral key } test && test.Right == ds)
            {
                var name = Data(key.Value);
                _bool.Add(test);
                Read(r, test, (_, x, tr) => AttrRead(x, true, name, null, tr));
                return;
            }
            Refuse(ds, $"this use of .dataset on {r.Name} (reading, writing, deleting and testing one named key are translated)");
        }

        // ---- values the script can write ---------------------------------------------------------------

        /// <summary>
        /// Every value an expression can take, when that is a fixed set known from the source: literals,
        /// either side of a ternary or a logical operator, `+ - *` of such sets, a template of them, a name
        /// every value of which is such a set (its own scope's, as JavaScript resolves it), a parameter from
        /// every call's argument, a loop counter over a fixed range, a field of a constant table, and a field
        /// of an object literal a function returns. Null when any of it is only known at run time.
        /// </summary>
        private List<object>? Finite(Expression e, HashSet<Identifier>? seen = null, Inlined? bind = null)
        {
            const int Most = 256;
            switch (e)
            {
                // a value of markup, read in the scope of the function it was written in
                case ParenthesizedExpression w when _bindOf.TryGetValue(w, out var within):
                    return Finite(w.Expression, seen, within);
                // a list's row item: every value the list's items can be
                case Identifier row when _itemOf.TryGetValue(row, out var items):
                    {
                        if (items == null) return null;
                        List<object>? all = new();
                        foreach (var (item, env) in items)
                        {
                            all = Union(all, Finite(item, seen, env));
                            if (all == null) return null;
                        }
                        return all;
                    }
                case StringLiteral s: return new List<object> { s.Value };
                case NumericLiteral n: return new List<object> { n.Value };
                case TemplateLiteral tl:
                    {
                        List<object>? all = new() { tl.Quasis[0].Value.Cooked ?? string.Empty };
                        for (var i = 0; i < tl.Expressions.Count; i++)
                        {
                            if (Finite(tl.Expressions[i], seen, bind) is not { } part) return null;
                            all = Product(all, part.Select(v => (object)(v is double d ? JsToLuaNumber(d) : (string)v)).ToList(), Operator.Addition);
                            all = all == null ? null : Product(all, new List<object> { tl.Quasis[i + 1].Value.Cooked ?? string.Empty }, Operator.Addition);
                            if (all == null) return null;
                        }
                        return all;
                    }
                case ConditionalExpression c: return Union(Finite(c.Consequent, seen, bind), Finite(c.Alternate, seen, bind));
                // `x || 'default'`, `x ?? 'default'` with x null or undefined (an argument not passed): the default
                case LogicalExpression { Operator: Operator.LogicalOr or Operator.NullishCoalescing } dl when Nullish(dl.Left, bind):
                    return Finite(dl.Right, seen, bind);
                // an item picked from a list the compile knows (find, an index), or the fallback when there is none
                case LogicalExpression { Operator: Operator.LogicalOr or Operator.NullishCoalescing } pl when Picked(pl.Left) is { } list:
                    {
                        if (ItemsOf(list, bind) is not { } items) return null;
                        List<object>? all = new();
                        foreach (var (item, env) in items) { all = Union(all, Finite(item, seen, env)); if (all == null) return null; }
                        return Union(all, Finite(pl.Right, seen, bind));
                    }
                case LogicalExpression l: return Union(Finite(l.Left, seen, bind), Finite(l.Right, seen, bind));
                // a function written in place and called there: what it gives back
                case CallExpression iife when Unwrap(iife.Callee) is IFunction fn:
                    {
                        var inner = Inlined.Of(fn, iife, bind);
                        List<object>? all = new();
                        foreach (var r in Returns(fn))
                        {
                            if (r == null) return null;
                            all = Union(all, Finite(r, seen, inner));
                            if (all == null) return null;
                        }
                        return all;
                    }
                case NonLogicalBinaryExpression { Operator: Operator.Addition or Operator.Subtraction or Operator.Multiplication } b:
                    return Finite(b.Left, seen, bind) is { } left && Finite(b.Right, seen, bind) is { } right ? Product(left, right, b.Operator) : null;
                // a text method that gives one text for one text, and toFixed of a number
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "trim" or "trimStart" or "trimEnd" or "toUpperCase" or "toLowerCase" or "toFixed" } method } on } call
                    when (method.Name == "toFixed" ? call.Arguments.Count == 1 && call.Arguments[0] is NumericLiteral { Value: >= 0 and <= 20 } : call.Arguments.Count == 0)
                         && Finite(on.Object, seen, bind) is { } inputs:
                    {
                        var outputs = new List<object>();
                        foreach (var v in inputs)
                        {
                            object? r = (method.Name, v) switch
                            {
                                ("toFixed", double d) => JsNumber.ToFixed(d, (int)((NumericLiteral)call.Arguments[0]).Value),
                                ("trim", string s) => s.Trim(JsSpace),
                                ("trimStart", string s) => s.TrimStart(JsSpace),
                                ("trimEnd", string s) => s.TrimEnd(JsSpace),
                                ("toUpperCase", string s) => s.ToUpperInvariant(),
                                ("toLowerCase", string s) => s.ToLowerInvariant(),
                                _ => null,
                            };
                            if (r == null) return null;
                            if (!outputs.Contains(r)) outputs.Add(r);
                        }
                        return outputs;
                    }
                case Identifier id when !id.Name.Equals("undefined", StringComparison.Ordinal):
                    {
                        if (Decl(id) is not { } decl) return null;
                        if (bind != null && bind.TryGetValue(decl, out var arg)) return Finite(arg.Expr, seen, arg.Env);
                        seen ??= new HashSet<Identifier>();
                        // Met again while its own values are worked out: `x = cond ? 'b' : x` keeps the values it
                        // has (a mark its own frame drops), but `n = n + 1` counts for ever (the mark in a sum,
                        // Product, is no fixed set at all).
                        if (!seen.Add(decl)) return new List<object> { new Again(decl) };
                        try
                        {
                            if (LoopRange(decl) is { } range) return range;
                            if (ParamOf(decl) is { } param) return ParamValues(param.Fn, param.Index, seen)?.Where(x => x is not Again a || a.Decl != decl).ToList();
                            if (Given(decl) is not { } given) return null;
                            List<object>? all = new();
                            foreach (var v in given)
                            {
                                // a declaration without a value is undefined, which is not a value a set holds
                                if (v == null) return null;
                                all = Union(all, Finite(v, seen));
                                if (all == null) return null;
                            }
                            return all.Where(x => x is not Again a || a.Decl != decl).ToList();
                        }
                        finally { seen.Remove(decl); }
                    }
                case MemberExpression { Object: Identifier table } m when ConstTable(table) is { } values:
                    if (!m.Computed && m.Property is Identifier key)
                        return key.Name == "length" && IsArrayTable(table) ? new List<object> { (double)values.Count }
                            : values.TryGetValue(key.Name, out var one) ? Finite(one, seen, bind) : null;
                    {
                        List<object>? each = new();
                        foreach (var v in values.Values) { each = Union(each, Finite(v, seen, bind)); if (each == null) return null; }
                        return each;
                    }
                // an item of a list the compile knows, by its index (`d[1]` of a row `['Pump A', 'on']`)
                case MemberExpression { Computed: true, Property: not StringLiteral } ix when Indexed(ix, seen, bind) is { } picked:
                    return picked;
                case MemberExpression { Computed: false, Property: Identifier { Name: "length" } } len when Elems(len.Object, bind) is { List: true } list:
                    // a list of a list's rows is as long as the rows shown, which only the run knows
                    return list.Rows != null ? null : list.Lists.Select(l => (object)(double)l.Count).Distinct().ToList();
                case MemberExpression m when !m.Computed && m.Property is Identifier || m.Computed && m.Property is StringLiteral:
                    {
                        // a field of an object literal: every object the expression can be, and that field of each
                        var field = m.Property is Identifier f ? f.Name : ((StringLiteral)m.Property).Value;
                        if (Objects(m.Object, bind, 0) is not { Count: > 0 } objects) return null;
                        List<object>? all = new();
                        foreach (var (o, ob) in objects)
                        {
                            if (Field(o, field) is not { } v || FieldWritten(field, o)) return null;
                            all = Union(all, Finite(v, seen, ob));
                            if (all == null) return null;
                        }
                        return all;
                    }
                default:
                    return null;
            }

            static List<object>? Union(List<object>? a, List<object>? b)
            {
                if (a == null || b == null) return null;
                var u = new List<object>(a);
                foreach (var x in b) if (!u.Contains(x)) u.Add(x);
                return u;
            }

            // Each value of the left with each of the right, as JavaScript's `+ - *` give them
            static List<object>? Product(List<object> a, List<object> b, Operator op)
            {
                if (a.Count * b.Count > Most || a.Concat(b).Any(x => x is Again)) return null;
                var u = new List<object>();
                foreach (var x in a)
                    foreach (var y in b)
                    {
                        object v;
                        if (op == Operator.Addition && (x is string || y is string))
                            v = (x is double dx ? JsToLuaNumber(dx) : (string)x) + (y is double dy ? JsToLuaNumber(dy) : (string)y);
                        else if (x is double nx && y is double ny) v = op switch { Operator.Addition => nx + ny, Operator.Subtraction => nx - ny, _ => nx * ny };
                        else return null;
                        if (!u.Contains(v)) u.Add(v);
                    }
                return u;
            }
        }

        /// <summary>
        /// `list[i]`: the items at the indexes i can be, when the list is an array written in place (through the names and
        /// arguments bound to it); otherwise any item of a list the compile knows. Null when neither is known.
        /// </summary>
        private List<object>? Indexed(MemberExpression ix, HashSet<Identifier>? seen, Inlined? bind)
        {
            var list = ix.Object;
            var env = bind;
            for (var k = 0; k < 8; k++)
            {
                if (list is ParenthesizedExpression pw && _bindOf.TryGetValue(pw, out var within)) { list = pw.Expression; env = within; continue; }
                list = Unwrap(list);
                if (list is Identifier id && Decl(id) is { } d && env != null && env.TryGetValue(d, out var arg)) { list = arg.Expr; env = arg.Env; continue; }
                break;
            }
            var all = new List<object>();
            if (list is ArrayExpression arr && Finite(ix.Property, seen, bind) is { Count: > 0 } indexes && indexes.All(v => v is double))
            {
                foreach (double i in indexes)
                {
                    if (i != Math.Floor(i) || i < 0 || i >= arr.Elements.Count || arr.Elements[(int)i] is not Expression el || el is SpreadElement) return null;
                    if (Finite(el, seen, env) is not { } one) return null;
                    foreach (var v in one) if (!all.Contains(v)) all.Add(v);
                }
                return all;
            }
            if (ItemsOf(ix.Object, bind) is not { } items) return null;
            foreach (var (item, ie) in items)
            {
                if (Finite(item, seen, ie) is not { } one) return null;
                foreach (var v in one) if (!all.Contains(v)) all.Add(v);
            }
            return all;
        }

        /// <summary>Whether an expression is null or undefined in every run: written so, or a name bound to that.</summary>
        private bool Nullish(Expression e, Inlined? bind, int depth = 0)
        {
            if (depth > 16) return false;
            switch (e)
            {
                case NullLiteral:
                    return true;
                case Identifier { Name: "undefined" } u when Decl(u) == null:
                    return true;
                case ParenthesizedExpression w when _bindOf.TryGetValue(w, out var within):
                    return Nullish(w.Expression, within, depth + 1);
                case Identifier id when Decl(id) is { } d && bind != null && bind.TryGetValue(d, out var arg):
                    return Nullish(arg.Expr, arg.Env, depth + 1);
                default:
                    return false;
            }
        }

        /// <summary>White space as String.prototype.trim takes it off: the ASCII kinds, no-break and the Unicode spaces.</summary>
        private static readonly char[] JsSpace =
        {
            ' ', '\t', '\n', '\v', '\f', '\r', '\u00A0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006',
            '\u2007', '\u2008', '\u2009', '\u200A', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000', '\uFEFF',
        };

        /// <summary>A name met again while its own values are worked out (see <see cref="Finite"/>).</summary>
        private sealed class Again
        {
            public readonly Identifier Decl;
            public Again(Identifier decl) { Decl = decl; }
        }

        /// <summary>
        /// A loop counter's values: <c>for (let i = A; i &lt; B; i++)</c> with A and B fixed numbers (B may be a
        /// fixed list's length) and nothing else writing i - every whole number from A up to B. At most 256.
        /// </summary>
        private List<object>? LoopRange(Identifier decl)
        {
            if (!_parent.TryGetValue(decl, out var b) || b is not VariableDeclarator { Init: NumericLiteral start } vd || vd.Id != decl
                || _parent[vd] is not VariableDeclaration dd || _parent[dd] is not ForStatement f || f.Init != dd
                || !_writes.TryGetValue(decl, out var ws) || ws.Count != 1 || ws[0] != f.Update)
                return null;
            var step = f.Update switch
            {
                UpdateExpression { Operator: Operator.Increment } => 1,
                AssignmentExpression { Operator: Operator.AdditionAssignment, Right: NumericLiteral { Value: 1 } } => 1,
                _ => 0,
            };
            if (step == 0 || f.Test is not NonLogicalBinaryExpression { Operator: Operator.LessThan or Operator.LessThanOrEqual, Left: Identifier ti } test
                || Decl(ti) != decl || Finite(test.Right) is not { Count: 1 } bound || bound[0] is not double end)
                return null;
            var values = new List<object>();
            for (var v = start.Value; test.Operator == Operator.LessThan ? v < end : v <= end; v += step)
            {
                if (values.Count >= 256) return null;
                values.Add(v);
            }
            return values;
        }

        /// <summary>
        /// The values a parameter takes: the argument every call passes in its place, when its function is only
        /// ever called by its name; a forEach callback's index, 0 to the length less one. Null for anything else.
        /// </summary>
        private List<object>? ParamValues(IFunction fn, int index, HashSet<Identifier> seen)
        {
            if (ForEachOver((Node)fn) is { } over)
                return index == 1 && over.Lists.Count == 1 ? Enumerable.Range(0, over.Lists[0].Count).Select(i => (object)(double)i).ToList() : null;
            if (Walked((Node)fn) is { } items) return Item(items, index, seen);
            if (NameOf(fn) is not { } name || !_refs.TryGetValue(name, out var refs)) return null;
            List<object>? all = new();
            foreach (var use in refs)
            {
                List<object>? one;
                if (_parent[use] is CallExpression call && call.Callee == use)
                    one = index < call.Arguments.Count && call.Arguments[index] is Expression a and not SpreadElement ? Finite(a, seen) : null;
                else if (ForEachOver(use) is { Lists.Count: 1 } list && index == 1)
                    one = Enumerable.Range(0, list.Lists[0].Count).Select(i => (object)(double)i).ToList();
                else if (Walked(use) is { } walked)
                    one = Item(walked, index, seen);
                // the function passed around rather than called: its arguments are not in the source
                else return null;
                if (one == null) return null;
                foreach (var v in one) if (!all.Contains(v)) all.Add(v);
            }
            return all;
        }

        /// <summary>
        /// The items of a fixed array a function is the callback of - <c>['a', 'b'].forEach(fn)</c>, or map, filter,
        /// some, every, find and findIndex, on an array written in place or a constant one - in order.
        /// </summary>
        private List<Expression>? Walked(Node callback)
        {
            if (!_parent.TryGetValue(callback, out var p) || p is not CallExpression c || c.Arguments.Count == 0 || c.Arguments[0] != callback
                || c.Callee is not MemberExpression { Computed: false, Property: Identifier { Name: "forEach" or "map" or "filter" or "some" or "every" or "find" or "findIndex" } } m)
                return null;
            if (m.Object is ArrayExpression arr)
                return arr.Elements.All(x => x is Acornima.Ast.Expression and not SpreadElement) ? arr.Elements.Cast<Acornima.Ast.Expression>().ToList() : null;
            if (m.Object is Identifier name && IsArrayTable(name) && ConstTable(name) is { } table)
                return Enumerable.Range(0, table.Count).Select(i => table[i.ToString(CultureInfo.InvariantCulture)]).ToList();
            return null;
        }

        /// <summary>What a callback over fixed items is given in a place: every item first, then its index.</summary>
        private List<object>? Item(List<Expression> items, int index, HashSet<Identifier> seen)
        {
            if (index == 1) return Enumerable.Range(0, items.Count).Select(i => (object)(double)i).ToList();
            if (index != 0) return null;
            var all = new List<object>();
            foreach (var x in items)
            {
                if (Finite(x, seen) is not { } one) return null;
                foreach (var v in one) if (!all.Contains(v)) all.Add(v);
            }
            return all;
        }

        /// <summary>A `const` (or a name given one value and never changed) bound to an object or array literal that nothing writes into, by key.</summary>
        private Dictionary<string, Expression>? ConstTable(Identifier name)
        {
            if (Decl(name) is not { } decl || Given(decl) is not [{ } init] || _writes.ContainsKey(decl)) return null;
            if (_refs.TryGetValue(decl, out var refs))
                foreach (var r in refs)
                    if (_parent[r] is MemberExpression m && m.Object == r && (WrittenTo(m) || _parent[m] is CallExpression c && c.Callee == m && m.Property is Identifier { Name: "push" or "pop" or "shift" or "unshift" or "splice" or "sort" or "reverse" or "fill" or "copyWithin" }))
                        return null;
            var table = new Dictionary<string, Expression>(StringComparer.Ordinal);
            if (init is ArrayExpression arr)
            {
                for (var i = 0; i < arr.Elements.Count; i++)
                    if (arr.Elements[i] is Expression x and not SpreadElement) table[i.ToString(CultureInfo.InvariantCulture)] = x; else return null;
                return table;
            }
            if (init is ObjectExpression obj)
            {
                // a field the script can reach unseen (the object patched by Object.assign, handed to a function that writes it) is no constant
                if (obj.Properties.Any(p => p is Property { Computed: false } fp && (fp.Key is Identifier fk ? fk.Name : (fp.Key as StringLiteral)?.Value) is { } key && FieldWritten(key, obj))) return null;
                foreach (var p in obj.Properties)
                    if (p is Property { Computed: false, Value: Expression v } prop && prop.Key is Identifier or StringLiteral)
                        table[prop.Key is Identifier k ? k.Name : ((StringLiteral)prop.Key).Value] = v;
                    else return null;
                return table;
            }
            return null;
        }

        private bool IsArrayTable(Identifier name) => Decl(name) is { } decl && Given(decl) is [ArrayExpression];

        /// <summary>
        /// The object literals an expression can be, each with the arguments of the call it came back from:
        /// the literal itself, a name given one, a named function's return. Empty for a value that is none (an
        /// array, a number, a text, a function); null when it can be anything else.
        /// </summary>
        private List<(ObjectExpression, Inlined?)>? Objects(Expression e, Inlined? bind, int depth)
        {
            if (depth > 12) return null;
            switch (e)
            {
                case ObjectExpression o:
                    return new() { (o, bind) };
                case ParenthesizedExpression w when _bindOf.TryGetValue(w, out var within):
                    return Objects(w.Expression, within, depth + 1);
                case ParenthesizedExpression pe:
                    return Objects(pe.Expression, bind, depth + 1);
                case Identifier row when _itemOf.TryGetValue(row, out var items):
                    return Each(items);
                case ArrayExpression or Acornima.Ast.Literal or TemplateLiteral or NonLogicalBinaryExpression or NonUpdateUnaryExpression or IFunction:
                    return new();
                // `this` in a getter or setter of an object literal: that object
                case ThisExpression th when AccessorOf(th) is { } self:
                    return new() { (self, null) };
                case ConditionalExpression c:
                    return Objects(c.Consequent, bind, depth + 1) is { } a && Objects(c.Alternate, bind, depth + 1) is { } b ? a.Concat(b).ToList() : null;
                // `found || fallback`, `found ?? fallback`: either (the fallback alone when there is nothing on the left)
                case LogicalExpression { Operator: Operator.LogicalOr or Operator.NullishCoalescing } lo:
                    {
                        if (Nullish(lo.Left, bind)) return Objects(lo.Right, bind, depth + 1);
                        return Objects(lo.Left, bind, depth + 1) is { } left && Objects(lo.Right, bind, depth + 1) is { } right ? left.Concat(right).ToList() : null;
                    }
                case Identifier id:
                    {
                        if (Decl(id) is not { } decl) return null;
                        if (bind != null && bind.TryGetValue(decl, out var arg)) return Objects(arg.Expr, arg.Env, depth + 1);
                        var all = new List<(ObjectExpression, Inlined?)>();
                        // a parameter never given another value: every argument its function is called with (none: undefined),
                        // and for a callback walking a list, every item of the list
                        if (ParamOf(decl) is { } param)
                        {
                            if (_writes.ContainsKey(decl)) return null;
                            foreach (var use in UsesOf(param.Fn))
                            {
                                if (_parent[use] is CallExpression call && call.Callee == use)
                                {
                                    if (call.Arguments.Take(param.Index + 1).Any(a => a is SpreadElement)) return null;
                                    if (param.Index >= call.Arguments.Count) continue;
                                    if (Objects((Expression)call.Arguments[param.Index], null, depth + 1) is not { } some) return null;
                                    all.AddRange(some);
                                    continue;
                                }
                                if (WalkedAt(use) is not { } walk) return null;
                                // the index that comes after the item is a number; anything else the callback is given is not followed
                                if (param.Index == walk.Item + 1) continue;
                                if (param.Index != walk.Item || ItemsOf(walk.List, null) is not { } walked || Each(walked) is not { } each) return null;
                                all.AddRange(each);
                            }
                            return all;
                        }
                        // a for...of loop's name, never given another value: each item of the list it walks
                        if (_parent.TryGetValue(decl, out var dp) && dp is VariableDeclarator dv && dv.Id == decl && _parent.TryGetValue(dv, out var dd)
                            && _parent.TryGetValue(dd, out var loop) && loop is ForOfStatement fo && fo.Left == dd)
                            return _writes.ContainsKey(decl) ? null : Each(ItemsOf(fo.Right, null));
                        if (Given(decl) is not { } given) return null;
                        foreach (var v in given)
                            if (v == null || Objects(v, null, depth + 1) is not { } some) return null;
                            else all.AddRange(some);
                        return all;
                    }
                case CallExpression call when Target(call) is { } fn:
                    {
                        var inner = Inlined.Of(fn, call, bind);
                        var all = new List<(ObjectExpression, Inlined?)>();
                        foreach (var r in Returns(fn))
                            if (r == null || Objects(r, inner, depth + 1) is not { } some) return null;
                            else all.AddRange(some);
                        return all;
                    }
                // one item of a list the compile knows: any of them
                case CallExpression or MemberExpression when Picked(e) is { } list:
                    return ItemsOf(list, bind) is { } picked ? Each(picked) : null;
                // a field of an object literal nothing writes: what it is written as
                case MemberExpression { Computed: false, Property: Identifier field } fm:
                    {
                        if (Objects(fm.Object, bind, depth + 1) is not { } holders) return null;
                        var all = new List<(ObjectExpression, Inlined?)>();
                        foreach (var (o, ob) in holders)
                        {
                            if (Field(o, field.Name) is not { } v || FieldWritten(field.Name, o) || Objects(v, ob, depth + 1) is not { } some) return null;
                            all.AddRange(some);
                        }
                        return all;
                    }
                default:
                    return null;
            }

            List<(ObjectExpression, Inlined?)>? Each(List<(Expression, Inlined?)>? items)
            {
                if (items == null) return null;
                var all = new List<(ObjectExpression, Inlined?)>();
                foreach (var (item, env) in items)
                    if (Objects(item, env, depth + 1) is not { } some) return null;
                    else all.AddRange(some);
                return all;
            }
        }

        /// <summary>The object literal `this` is in a getter or setter of it (an arrow function keeps the `this` around it); null anywhere else.</summary>
        private ObjectExpression? AccessorOf(ThisExpression th)
        {
            Node n = th;
            while (_parent.TryGetValue(n, out var p) && (n is not IFunction || n is ArrowFunctionExpression)) n = p;
            return n is FunctionExpression fn && _parent.TryGetValue(fn, out var prop) && prop is Property { Kind: PropertyKind.Get or PropertyKind.Set } accessor
                   && accessor.Value == fn && _parent.TryGetValue(accessor, out var o) ? o as ObjectExpression : null;
        }

        /// <summary>A field of an object literal as written: `key: value`, or `key` for `key: key`; null when absent or not plain.</summary>
        private static Expression? Field(ObjectExpression o, string name)
        {
            Expression? found = null;
            foreach (var p in o.Properties)
            {
                // a getter, setter or method of another name leaves this field as written (what one writes through `this` is FieldWritten's)
                if (p is Property { Computed: false } other && (other.Kind != PropertyKind.Init || other.Method)
                    && (other.Key is Identifier ok ? ok.Name : (other.Key as StringLiteral)?.Value) is { } okey && okey != name)
                    continue;
                if (p is not Property { Computed: false, Kind: PropertyKind.Init, Method: false, Value: Expression v } prop) return null;
                if ((prop.Key is Identifier k ? k.Name : prop.Key is StringLiteral s ? s.Value : null) == name) found = v;
            }
            return found;
        }

        /// <summary>
        /// Whether the script can change this field of this object literal. The object is followed wherever it
        /// goes - into names, a function's parameters, returns and comparisons - and every use of it there has to
        /// be one that cannot write this field: reading a field, or writing another. Anywhere else (stored into
        /// another object, passed to a function the compile cannot follow) it is taken as written.
        /// </summary>
        private bool FieldWritten(string name, ObjectExpression o)
            => FieldUses(name, o) is not { } flow || flow.Uses.Any(WrittenTo) || flow.Assigned.Count > 0;

        /// <summary>Where the script reaches one field of one object literal (see <see cref="FieldUses"/>).</summary>
        private sealed class FieldFlow
        {
            /// <summary>`x.name`, or `x[key]` with a key only known at run time - and, where the object is a source of `Object.assign`, the target's.</summary>
            public readonly List<MemberExpression> Uses = new();
            /// <summary>Each object literal `Object.assign(x, …)` copies this field onto it from, with that field's value there.</summary>
            public readonly List<(ObjectExpression From, Expression Value, Inlined? Env)> Assigned = new();
        }

        /// <summary>
        /// Every place the script reaches this field of this object literal, following the object as
        /// <see cref="FieldWritten"/> does: its uses, and what `Object.assign` copies onto it. Null when the object
        /// goes where its fields could be reached unseen.
        /// </summary>
        private FieldFlow? FieldUses(string name, ObjectExpression o)
        {
            if (_fieldUses.TryGetValue((name, o), out var known)) return known;
            // met again while it is worked out (objects copied into each other): taken as unfollowable
            _fieldUses[(name, o)] = null;
            var written = false;
            var flow = new FieldFlow();
            var uses = flow.Uses;
            var holders = new HashSet<Identifier>();
            var arrays = new HashSet<Node>();
            var functions = new HashSet<(IFunction, bool)>();
            Value(o);
            // `this` in the object's own getters and setters is the object
            foreach (var th in Markup.Everything(o).OfType<ThisExpression>())
                if (AccessorOf(th) == o) Value(th);
            return _fieldUses[(name, o)] = written ? null : flow;

            // an expression whose value can be the object
            void Value(Expression e)
            {
                if (written) return;
                if (!_parent.TryGetValue(e, out var p)) { written = true; return; }
                switch (p)
                {
                    case ParenthesizedExpression pe: Value(pe); return;
                    case VariableDeclarator vd when vd.Init == e && vd.Id is Identifier id: Holder(id); return;
                    case AssignmentExpression { Operator: Operator.Assignment } a when a.Right == e && a.Left is Identifier l && Decl(l) is { } d: Holder(d); return;
                    // a name holding it given another value: nothing is done to the object
                    case AssignmentExpression { Operator: Operator.Assignment } a0 when a0.Left == e && e is Identifier: return;
                    // stored in a field of another object literal: wherever that field is read
                    case Property { Computed: false, Kind: PropertyKind.Init, Method: false } prop when prop.Value == e: Stored(prop, Value); return;
                    case ReturnStatement: Returned(p, false); return;
                    case ArrowFunctionExpression af when af.Body == e: Returned(p, false); return;
                    case ConditionalExpression c when c.Test != e: Value(c); return;
                    case LogicalExpression l2: Value(l2); return;
                    case MemberExpression m when m.Object == e: Member(m); return;
                    case CallExpression c2 when c2.Arguments.Contains(e): Passed(c2, e); return;
                    // an item of an array written in place: wherever the array hands out its items
                    case ArrayExpression arr when arr.Elements.Contains(e): ItemsAt(arr); return;
                    // its fields copied into another object, read only
                    case SpreadElement when _parent.TryGetValue(p, out var so) && so is ObjectExpression: return;
                    case ExpressionStatement or IfStatement or NonUpdateUnaryExpression { Operator: Operator.LogicalNot }
                        or NonLogicalBinaryExpression { Operator: Operator.StrictEquality or Operator.StrictInequality or Operator.Equality or Operator.Inequality }:
                        return;
                    default: written = true; return;
                }
            }

            // a name holding it: each of its references is where the object goes
            void Holder(Identifier decl)
            {
                if (!holders.Add(decl) || !_refs.TryGetValue(decl, out var refs)) return;
                foreach (var r in refs) Value(r);
            }

            // A name holding an array with it among its items: the object goes wherever each reference hands the
            // array's items on. Given other values only with `=`, which are other arrays.
            void Items(Identifier array)
            {
                if (_writes.TryGetValue(array, out var ws) && ws.Any(w => w is not AssignmentExpression { Operator: Operator.Assignment })) { written = true; return; }
                if (!holders.Add(array) || !_refs.TryGetValue(array, out var refs)) return;
                foreach (var r in refs) ItemsAt(r);
            }

            // a field of object literals an array with it among its items is stored in: wherever that field is read
            void FieldItems(Expression holder, string field)
            {
                if (Objects(holder, null, 0) is not { } targets) { written = true; return; }
                foreach (var (target, _) in targets)
                {
                    if (FieldUses(field, target) is not { } there) { written = true; return; }
                    foreach (var u in there.Uses) if (!WrittenTo(u)) ItemsAt(u);
                }
            }

            // `{ key: value }`: the value stored in that field of that object literal, followed where the field is read
            void Stored(Property prop, Action<Expression> go)
            {
                if (!_parent.TryGetValue(prop, out var holder) || holder is not ObjectExpression lit
                    || (prop.Key is Identifier k ? k.Name : (prop.Key as StringLiteral)?.Value) is not { } key
                    || FieldUses(key, lit) is not { } there)
                {
                    written = true;
                    return;
                }
                foreach (var u in there.Uses) if (!WrittenTo(u)) go(u);
            }

            // An expression whose value is an array holding the object among its items: an item read (`a[i]`, find,
            // pop), each callback walking it, each for...of's name, and wherever the array itself goes - into a name,
            // a field, a slice or concat of it, a function's return or parameter.
            void ItemsAt(Expression r)
            {
                if (written || !arrays.Add(r)) return;
                if (!_parent.TryGetValue(r, out var p)) { written = true; return; }
                switch (p)
                {
                    case ParenthesizedExpression pe:
                        ItemsAt(pe);
                        return;
                    case MemberExpression { Computed: true } ix when ix.Object == r:
                        if (WrittenTo(ix)) { written = true; return; }
                        Value(ix);
                        return;
                    case MemberExpression { Computed: false, Property: Identifier { Name: var method } } mm when mm.Object == r:
                        if (method == "length") return;
                        if (_parent[mm] is CallExpression call && call.Callee == mm)
                        {
                            if (method is "map" or "forEach" or "filter" or "some" or "every" or "find" or "findIndex" or "findLast" or "findLastIndex" or "flatMap"
                                && call.Arguments.Count > 0 && Callback(call.Arguments[0]) is { } cb)
                            {
                                if (cb.Params.Count > 0)
                                {
                                    if (cb.Params[0] is Identifier item) Holder(item);
                                    else { written = true; return; }
                                }
                                // what filter gives back is an array of them, what find gives back one of them
                                if (method == "filter") ItemsAt(call);
                                else if (method is "find" or "findLast") Value(call);
                                return;
                            }
                            if (method is "reduce" or "reduceRight" && call.Arguments.Count > 0 && Callback(call.Arguments[0]) is { } rcb)
                            {
                                // the item comes in second; with no first value the first item is the first total, and can be the result
                                if (rcb.Params.Count > 1) { if (rcb.Params[1] is Identifier ri) Holder(ri); else { written = true; return; } }
                                if (call.Arguments.Count == 1)
                                {
                                    if (rcb.Params.Count > 0) { if (rcb.Params[0] is Identifier ra) Holder(ra); else { written = true; return; } }
                                    Value(call);
                                }
                                return;
                            }
                            if (method is "indexOf" or "lastIndexOf" or "includes" or "join" or "toString" or "push" or "unshift" or "fill") return;
                            if (method is "slice" or "concat" or "sort" or "reverse" or "toSorted" or "toReversed" or "splice") { ItemsAt(call); return; }
                            if (method is "pop" or "shift" or "at") { Value(call); return; }
                        }
                        written = true;
                        return;
                    case ForOfStatement { Left: VariableDeclaration { Declarations: [{ Id: Identifier x }] } } fo when fo.Right == r:
                        Holder(x);
                        return;
                    case SpreadElement when _parent.TryGetValue(p, out var outer) && outer is ArrayExpression oa:
                        ItemsAt(oa);
                        return;
                    case CallExpression passed when passed.Arguments.Contains(r):
                        {
                            if (passed.Callee is MemberExpression { Computed: false, Property: Identifier { Name: "concat" } }) { ItemsAt(passed); return; }
                            if (passed.Callee is MemberExpression { Object: Identifier { Name: "JSON" or "console" } g, Computed: false } && !Declared(g.Name)) return;
                            var index = passed.Arguments.ToList().IndexOf(r);
                            if (Target(passed) is { } fn && CallsOf(fn) is { } calls && calls.Contains(passed))
                            {
                                if (index < fn.Params.Count && fn.Params[index] is Identifier param) Items(param);
                                return;
                            }
                            written = true;
                            return;
                        }
                    case VariableDeclarator vd when vd.Init == r && vd.Id is Identifier id:
                        Items(id);
                        return;
                    case AssignmentExpression { Operator: Operator.Assignment } a when a.Right == r:
                        if (a.Left is Identifier l && Decl(l) is { } d) { Items(d); return; }
                        if (a.Left is MemberExpression { Computed: false, Property: Identifier f } fm) { FieldItems(fm.Object, f.Name); return; }
                        written = true;
                        return;
                    // the name or field holding it given another array
                    case AssignmentExpression { Operator: Operator.Assignment } a2 when a2.Left == r:
                        return;
                    case Property { Computed: false, Kind: PropertyKind.Init, Method: false } prop when prop.Value == r:
                        Stored(prop, ItemsAt);
                        return;
                    case ReturnStatement:
                        Returned(p, true);
                        return;
                    case ArrowFunctionExpression af when af.Body == r:
                        Returned(p, true);
                        return;
                    case ConditionalExpression c when c.Test != r:
                        ItemsAt(c);
                        return;
                    case LogicalExpression:
                        ItemsAt((Expression)p);
                        return;
                    // read and nothing more: tested, compared, turned into text
                    case ExpressionStatement or IfStatement or NonUpdateUnaryExpression { Operator: Operator.LogicalNot } or TemplateLiteral
                        or NonLogicalBinaryExpression { Operator: Operator.StrictEquality or Operator.StrictInequality or Operator.Equality or Operator.Inequality or Operator.Addition }:
                        return;
                    default:
                        written = true;
                        return;
                }
            }

            // what a function gives back: at each call of it (the calls' value is it, or with asItems an array of it);
            // a map callback's value is an item of what map gives
            void Returned(Node from, bool asItems)
            {
                Node? n = from;
                while (n != null && n is not IFunction) n = _parent.TryGetValue(n, out var up) ? up : null;
                if (n is not IFunction fn) { written = true; return; }
                if (!functions.Add((fn, asItems))) return;
                if (!asItems && Mapped(fn) is { } map) { ItemsAt(map); return; }
                if (CallsOf(fn) is not { } calls) { written = true; return; }
                foreach (var call in calls)
                    if (asItems) ItemsAt(call);
                    else Value(call);
            }

            // `x.key` of it: this field, or a field only known at run time, which may be it
            void Member(MemberExpression m)
            {
                var key = m.Computed ? (m.Property as StringLiteral)?.Value : (m.Property as Identifier)?.Name;
                if (key == null || key == name) uses.Add(m);
            }

            void Passed(CallExpression call, Expression arg)
            {
                // pushed into an array a name holds: wherever that array hands out its items
                if (call.Callee is MemberExpression { Computed: false, Object: Identifier held, Property: Identifier { Name: "push" or "unshift" } } && Decl(held) is { } array)
                {
                    Items(array);
                    return;
                }
                if (call.Callee is MemberExpression { Computed: false, Object: Identifier { Name: "Object" } assign, Property: Identifier { Name: "assign" } } && !Declared(assign.Name)
                    && call.Arguments.Count > 0 && !call.Arguments.Any(a => a is SpreadElement))
                {
                    if (call.Arguments[0] == arg)
                    {
                        // the target: every object each source can be, each field of which is plain; this one's value copied on
                        foreach (var src in call.Arguments.Skip(1))
                        {
                            if (Objects((Expression)src, null, 0) is not { } objs) { written = true; return; }
                            foreach (var (from, env) in objs)
                                foreach (var p in from.Properties)
                                {
                                    if (p is not Property { Computed: false, Kind: PropertyKind.Init, Method: false, Value: Expression v } prop
                                        || (prop.Key is Identifier k ? k.Name : (prop.Key as StringLiteral)?.Value) is not { } key)
                                    {
                                        written = true;
                                        return;
                                    }
                                    if (key == name) flow.Assigned.Add((from, v, env));
                                }
                        }
                        // what it gives back is the target
                        Value(call);
                        return;
                    }
                    // a source: only read, but this field's value is the target's field now too - wherever that is reached
                    if (Objects((Expression)call.Arguments[0], null, 0) is not { } targets) { written = true; return; }
                    foreach (var (target, _) in targets)
                    {
                        if (FieldUses(name, target) is not { } there) { written = true; return; }
                        uses.AddRange(there.Uses);
                    }
                    return;
                }
                var index = call.Arguments.ToList().IndexOf(arg);
                if (Target(call) is { } fn && CallsOf(fn) is { } calls && calls.Contains(call))
                {
                    if (index < fn.Params.Count && fn.Params[index] is Identifier param) Holder(param);
                    return;
                }
                written = true;
            }
        }

        private readonly Dictionary<(string, ObjectExpression), FieldFlow?> _fieldUses = new();

        /// <summary>
        /// A written text as literal pieces and values. `a + b + ' kPa'` is one value then a literal,
        /// because JavaScript adds `a + b` before anything is a string; a template or a chain after its
        /// first string literal is a value per hole. `x.toFixed(n)` is x, printed with `%.nf`.
        /// </summary>
        private List<Piece> Pieces(Expression e) => _givenPieces.TryGetValue(e, out var given) ? given : PiecesOf(e);

        private static List<Piece> PiecesOf(Expression e)
        {
            var pieces = new List<Piece>();
            Add(e);
            // neighbouring literals as one
            for (var i = pieces.Count - 1; i > 0; i--)
                if (pieces[i].Literal != null && pieces[i - 1].Literal != null)
                {
                    pieces[i - 1].Literal += pieces[i].Literal;
                    pieces.RemoveAt(i);
                }
            return pieces;

            void Add(Expression x)
            {
                switch (x)
                {
                    case StringLiteral s: pieces.Add(new Piece { Literal = s.Value }); return;
                    case TemplateLiteral t:
                        for (var i = 0; i < t.Quasis.Count; i++)
                        {
                            var q = t.Quasis[i].Value.Cooked ?? t.Quasis[i].Value.Raw ?? string.Empty;
                            if (q.Length > 0) pieces.Add(new Piece { Literal = q });
                            if (i < t.Expressions.Count) pieces.Add(Hole(t.Expressions[i]));
                        }
                        return;
                    case NonLogicalBinaryExpression { Operator: Operator.Addition }:
                        {
                            var chain = new List<Expression>();
                            Expression at = x;
                            while (at is NonLogicalBinaryExpression { Operator: Operator.Addition } b) { chain.Insert(0, b.Right); at = b.Left; }
                            chain.Insert(0, at);
                            var first = chain.FindIndex(c => c is StringLiteral or TemplateLiteral);
                            if (first < 0) { pieces.Add(Hole(x)); return; }
                            if (first > 0)
                            {
                                // the operands before the first string are added as JavaScript adds them: one value
                                Expression head = x;
                                for (var k = chain.Count - 1; k >= first; k--) head = ((NonLogicalBinaryExpression)head).Left;
                                pieces.Add(Hole(head));
                            }
                            for (var k = first; k < chain.Count; k++)
                                if (chain[k] is StringLiteral or TemplateLiteral) Add(chain[k]);
                                else pieces.Add(Hole(chain[k]));
                            return;
                        }
                    default: pieces.Add(Hole(x)); return;
                }
            }

            static Piece Hole(Expression h)
                => h is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toFixed" } } f, Arguments.Count: 1 } c
                   && c.Arguments[0] is NumericLiteral { Value: >= 0 and <= 20 } d && d.Value == Math.Floor(d.Value)
                    ? new Piece { Hole = f.Object, Digits = (int)d.Value, Format = "%." + ((int)d.Value).ToString(CultureInfo.InvariantCulture) + "f" }
                    : new Piece { Hole = h };
        }

        /// <summary>Whether an expression is a boolean in any run, so its hole prints "true"/"false" as JavaScript does.</summary>
        private static bool Boolean(Expression e) => e switch
        {
            BooleanLiteral => true,
            NonUpdateUnaryExpression { Operator: Operator.LogicalNot } => true,
            NonLogicalBinaryExpression b => b.Operator is Operator.StrictEquality or Operator.StrictInequality or Operator.Equality
                or Operator.Inequality or Operator.LessThan or Operator.LessThanOrEqual or Operator.GreaterThan
                or Operator.GreaterThanOrEqual or Operator.In or Operator.InstanceOf,
            _ => false,
        };

        /// <summary>A CSS value written as a number and a unit: the number's expression, or its value when literal.</summary>
        private static (Expression? Hole, double Value, string Unit)? Numeric(Expression e)
        {
            switch (e)
            {
                case NumericLiteral n: return (null, n.Value, string.Empty);
                case StringLiteral s:
                    {
                        var m = Regex.Match(s.Value.Trim(), @"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)([a-zA-Z%]*)$");
                        return m.Success ? (null, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value.ToLowerInvariant()) : null;
                    }
                case NonLogicalBinaryExpression { Operator: Operator.Addition, Right: StringLiteral unit } b
                    when Regex.IsMatch(unit.Value, "^[a-zA-Z%]+$") && b.Left is not (StringLiteral or TemplateLiteral):
                    return (Number(b.Left), 0, unit.Value.ToLowerInvariant());
                case TemplateLiteral { Expressions.Count: 1 } t
                    when (t.Quasis[0].Value.Cooked ?? "").Length == 0 && Regex.IsMatch(t.Quasis[1].Value.Cooked ?? "", "^[a-zA-Z%]*$"):
                    return (Number(t.Expressions[0]), 0, (t.Quasis[1].Value.Cooked ?? string.Empty).ToLowerInvariant());
                case TemplateLiteral or ObjectExpression or ArrayExpression: return null;
                default: return (Number(e), 0, string.Empty);
            }

            // `x.toFixed(1) + '%'` is x, as a number: the string was only ever for CSS to parse back
            static Expression Number(Expression x)
                => x is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toFixed" } } f } ? f.Object : x;
        }

        // ---- layout: what each write draws -----------------------------------------------------------

        private Panel _panel = null!;
        private Vector2 _size;
        private string _template = string.Empty;
        private Dictionary<string, SceneSlots.Value> _rest = null!;
        private readonly Dictionary<string, string> _textSlots = new(StringComparer.Ordinal);

        /// <summary>
        /// Names what the script touches, emits the page at rest, and works out every write's slots by
        /// laying the page out in each state the write can draw. Returns the scene template.
        /// </summary>
        public string Layout(Panel panel, Vector2 size, Dictionary<string, SceneSlots.Value> rest)
        {
            _panel = panel;
            _size = size;
            _rest = rest;
            Pristine();

            foreach (var t in _order)
            {
                // Only what the script changes is named: a read is answered by the program, and a name
                // would only make the renderer keep the element's props.
                if (!t.Written) continue;
                // A synthetic id is not addressable: the element gets a name of its own, for good, as
                // HtmlRenderer.Nameable names what a class moves.
                if (t.Ve.name.StartsWith("__", StringComparison.Ordinal))
                {
                    t.Unnamed = true;
                    var name = t.Ve.name.Substring(2);
                    for (var k = 2; _built.ById.ContainsKey(name); k++) name = t.Ve.name.Substring(2) + "_" + k.ToString(CultureInfo.InvariantCulture);
                    _built.ById.Remove(t.Ve.name);
                    t.Ve.name = name;
                    _built.ById[name] = t.Ve;
                    t.Node.Attributes["id"] = name;
                }
                t.Name = t.Ve.name;
                _built.Driven.Add(t.Name);
                foreach (var (css, writes) in t.Styles)
                    if (css is "opacity" or "visibility" or "transform"
                        || css == WholeStyle && writes.Any(w => Finite(w.Value)?.Any(v => v is string s && Regex.IsMatch(s, @"(^|;)\s*(opacity|visibility|transform)\s*:", RegexOptions.IgnoreCase)) == true))
                        _built.NamedGroups.Add(t.Name);
                if ((t.Listens || t.Hit) && !Clickable(t.Node)) t.Node.Attributes["data-click"] = "1";
                // hidden takes the element out of the layout; the scene keeps its shapes and a `v` to show them
                if (t.Top || t.AttrOps.Any(o => o.Name == "hidden")) { _hide.Add(t); _built.NamedGroups.Add(t.Name); }
                // An empty label draws nothing, so a text written into it later has nowhere to go: it is
                // laid out holding a line of text from the start, and opens empty.
                if (t.Texts.Count > 0 && t.Ve is Label label && label.text.Length == 0)
                {
                    t.Empty = true;
                    var node = t.Node;
                    var children = new List<HtmlNode>(node.Children);
                    Text(label, node, "0");
                    _restore.Add(() => { label.text = string.Empty; node.Children.Clear(); node.Children.AddRange(children); });
                    Warnings.Add($"\"{t.Name}\" is empty until the script writes it, and is laid out as holding one line of text from the start");
                }
            }

            panel.Layout(size.x, size.y);
            foreach (var t in _order) if (t.Texts.Count > 0) _restHeight[t] = t.Ve.layout.height;
            _template = Emit(rest);
            // Laid out once more with nothing changed: the page as it settles, which is what the scene
            // carries and what every state is compared against.
            _baseline = Variant(() => { }, () => { }) ?? rest;
            if (!ReferenceEquals(_baseline, rest)) foreach (var pair in _baseline) rest[pair.Key] = pair.Value;
            var available = new HashSet<string>(rest.Keys, StringComparer.Ordinal);

            MarkupStates();
            if (_refused.Count > 0) return _template;
            // What the script writes is worked out on the page as it draws it: an element markup makes in
            // a shape not shown at rest, with that shape shown - its box, its text, what its writes move.
            foreach (var (group, show) in Groups())
            {
                var rest0 = _baseline;
                try
                {
                    if (show != null)
                    {
                        show(true);
                        _panel.Layout(_size.x, _size.y);
                        foreach (var t in group) if (t.Texts.Count > 0) _restHeight[t] = t.Ve.layout.height;
                        _baseline = Variant(() => { }, () => { }) ?? _baseline;
                    }
                    var absolute = PageCompiler.Absolute(_built);
                    Texts(group);
                    if (_refused.Count > 0) return _template;
                    foreach (var t in group)
                    {
                        foreach (var pair in t.Styles)
                        {
                            Style(t, pair.Key, pair.Value, available, absolute);
                            if (_refused.Count > 0) return _template;
                        }
                        if (t.Facets) Classes(t);
                        if (_refused.Count > 0) return _template;
                    }
                }
                finally
                {
                    if (show != null) { show(false); _panel.Layout(_size.x, _size.y); }
                    _baseline = rest0;
                }
            }
            Reads();
            // what the program needs of the page as laid out, before markup laid into it is taken out again
            _chains = Chains().ToList();
            foreach (var slot in Written)
                if (!_ease.ContainsKey(slot) && Opening.TryGetValue(slot, out var v) && v.IsNumber) _ease[slot] = Ease(slot);
            return _template;
        }

        private List<(string, string)> _chains = new();

        /// <summary>
        /// Puts every element the script writes back as the page's source wrote it, for the length of
        /// the compile: its class, its style attribute, no script styles, and its text. A script that ran
        /// before the compile - the interpreter, in game, until the compile installs - left its writes on
        /// the live page, and a state laid out on top of them draws the script's value rather than the
        /// state's: a sampled `width: 37%` came out as the 25% the script had written. Only the elements
        /// the script writes can differ, since every other DOM use keeps a page out of this translator.
        /// </summary>
        private void Pristine()
        {
            if (string.IsNullOrEmpty(_built.Source)) return;
            var written = _order.Where(t => t.Texts.Count > 0 || t.Styles.Count > 0 || t.Facets).ToList();
            if (written.Count == 0) return;
            var source = Source();
            foreach (var t in written)
            {
                if (!source.TryGetValue(t.Name, out var o)) continue;
                var node = t.Node;
                var (cls, style, script) = (node.Attr("class") ?? string.Empty, node.Attr("style"), node.ScriptStyle);
                var (was, wasStyle) = (o.Attr("class") ?? string.Empty, o.Attr("style"));
                // the attributes CSS selects on that the script sets, and hidden with what it does to display
                var attrs = t.AttrOps.Where(a => a.Facet).Select(a => a.Name).Distinct().Where(a => node.Attr(a) != o.Attr(a)).ToList();
                var hides = t.AttrOps.Any(a => a.Name == "hidden");
                if (cls != was || style != wasStyle || script != null || attrs.Count > 0 || hides)
                {
                    var live = attrs.Select(a => (a, node.Attr(a))).ToList();
                    var display = t.Ve.style.display;
                    node.ScriptStyle = null;
                    if (wasStyle == null) node.Attributes.Remove("style"); else node.Attributes["style"] = wasStyle;
                    foreach (var a in attrs) if (o.Attr(a) is { } v) node.Attributes[a] = v; else node.Attributes.Remove(a);
                    if (hides) t.Ve.style.display = StyleKeyword.Null;
                    _built.Reclass(t.Ve, was);
                    if (hides && HtmlRenderer.HiddenByAttribute(node, _built.CssOf(t.Ve))) t.Ve.style.display = DisplayStyle.None;
                    _restore.Add(() =>
                    {
                        node.ScriptStyle = script;
                        if (style == null) node.Attributes.Remove("style"); else node.Attributes["style"] = style;
                        foreach (var (a, v) in live) if (v != null) node.Attributes[a] = v; else node.Attributes.Remove(a);
                        _built.Reclass(t.Ve, cls);
                        t.Ve.style.display = display;
                    });
                }
                if (t.Texts.Count > 0 && t.Ve is Label label)
                {
                    var (text, children) = (label.text, new List<HtmlNode>(node.Children));
                    node.Children.Clear();
                    foreach (var child in o.Children) { child.Parent = node; node.Children.Add(child); }
                    label.text = HtmlRenderer.RichText(node, _built.Rules);
                    if (label.text == text) { node.Children.Clear(); node.Children.AddRange(children); continue; }
                    _restore.Add(() => { label.text = text; node.Children.Clear(); node.Children.AddRange(children); });
                }
            }
        }

        /// <summary>The page as its source wrote it, by id, parsed once.</summary>
        private Dictionary<string, HtmlNode> Source()
        {
            if (_source != null) return _source;
            _source = new Dictionary<string, HtmlNode>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(_built.Source)) Index(HtmlParser.Parse(_built.Source, _ => { }));
            return _source;

            void Index(HtmlNode n)
            {
                if (n.Attr("id") is { } id && !_source.ContainsKey(id)) _source[id] = n;
                foreach (var c in n.Children) Index(c);
            }
        }

        /// <summary>An element as its source wrote it (its live node when the source has no id for it).</summary>
        private HtmlNode SourceNode(Target t) => Source().TryGetValue(t.Name, out var o) ? o : t.Node;

        public void Restore()
        {
            for (var i = _restore.Count - 1; i >= 0; i--) _restore[i]();
            _restore.Clear();
        }

        private static bool Clickable(HtmlNode node)
            => node.Tag == "button" || node.Attr("onclick") != null || node.Attr("data-click") != null || node.Attr("data-control") != null;

        /// <summary>A label's text as the interpreter writes textContent (ScriptHost.ApplyText): the label and its node.</summary>
        private static void Text(Label label, HtmlNode node, string text)
        {
            label.text = text;
            node.Children.Clear();
            node.Children.Add(new HtmlNode { Text = text, Parent = node });
        }

        // ---- hidden: out of the layout, and still in the scene -------------------------------------------

        /// <summary>Elements the script hides and shows through `hidden`, in document order.</summary>
        private readonly List<Target> _hide = new();

        private static IEnumerable<VisualElement> Subtree(VisualElement ve)
        {
            yield return ve;
            foreach (var child in ve.Children())
                foreach (var d in Subtree(child)) yield return d;
        }

        /// <summary>
        /// The page as it is laid out now, emitted and split. A scene cannot gain or lose shapes, so an
        /// element hidden in this layout keeps its shapes, in a group whose `v` is 0 (display: none to the
        /// vector mod: not drawn, not clickable). They are where this same layout puts the element shown -
        /// laid out once more with it alone shown - so that another state showing it later finds it where
        /// the browser would, and so a state that moves it says so.
        /// </summary>
        private string Emit(Dictionary<string, SceneSlots.Value> values)
        {
            if (_hide.Count == 0) return PageCompiler.Emitted(_built, _panel, values);
            var shown = new bool[_hide.Count];
            var text = PageCompiler.Emitted(_built, _panel, values, boxes =>
            {
                for (var i = 0; i < _hide.Count; i++) shown[i] = _hide[i].Ve.resolvedStyle.display != DisplayStyle.None;
                var done = new bool[_hide.Count];
                // Another shape of a row that is shown: laid out in that row's place, the shape shown swapped for it, so
                // a row's shapes share their boxes and picking one only shows it. One layout per shape of a row.
                var rows = new Dictionary<(MkRep, int), List<int>>();
                for (var i = 0; i < _hide.Count; i++)
                    if (_rowOf.TryGetValue(_hide[i], out var at))
                    {
                        if (!rows.TryGetValue((at.Rep, at.K), out var list)) rows[(at.Rep, at.K)] = list = new List<int>();
                        list.Add(i);
                    }
                var swaps = new SortedDictionary<int, (List<int> Show, List<int> Hide)>();
                foreach (var list in rows.Values)
                {
                    var on = list.Where(i => shown[i]).ToList();
                    if (on.Count == 0) continue;
                    foreach (var i in list.Where(i => !shown[i]))
                    {
                        var c = _rowOf[_hide[i]].C;
                        if (!swaps.TryGetValue(c, out var swap)) swaps[c] = swap = (new List<int>(), new List<int>());
                        swap.Show.Add(i);
                        foreach (var j in on) if (!swap.Hide.Contains(j)) swap.Hide.Add(j);
                        done[i] = true;
                    }
                }
                foreach (var (show, hide) in swaps.Values) Lay(show, hide);
                for (var i = 0; i < _hide.Count; i++)
                {
                    if (shown[i] || done[i]) continue;
                    // a list's rows not shown are laid out together, where the list at its longest has them; anything else alone
                    var batch = new List<int> { i };
                    if (_rowOf.TryGetValue(_hide[i], out var row))
                        for (var j = i + 1; j < _hide.Count; j++)
                            if (!shown[j] && !done[j] && _rowOf.TryGetValue(_hide[j], out var other) && other.Rep == row.Rep) batch.Add(j);
                    foreach (var b in batch) done[b] = true;
                    Lay(batch, new List<int>());
                }
                if (shown.Any(x => !x)) _panel.Layout(_size.x, _size.y);

                void Lay(List<int> show, List<int> hide)
                {
                    // the elements hidden in this layout, each laid out in a pass of its own
                    var hidden = new HashSet<VisualElement>(Enumerable.Range(0, _hide.Count).Where(j => !shown[j]).Select(j => _hide[j].Ve));
                    var opened = new List<(VisualElement Ve, StyleEnum<DisplayStyle> Was)>();
                    foreach (var h in hide)
                    {
                        opened.Add((_hide[h].Ve, _hide[h].Ve.style.display));
                        _hide[h].Ve.style.display = DisplayStyle.None;
                    }
                    foreach (var b in show)
                    {
                        opened.Add((_hide[b].Ve, _hide[b].Ve.style.display));
                        _hide[b].Ve.style.display = StyleKeyword.Null;
                    }
                    _panel.Layout(_size.x, _size.y);
                    var open = PageCompiler.Captured(_built);
                    foreach (var b in show) Take(_hide[b].Ve, true);
                    for (var o = opened.Count - 1; o >= 0; o--) opened[o].Ve.style.display = opened[o].Was;

                    // its boxes, but for an element inside it that is shown and hidden on its own: that has a pass of its own
                    void Take(VisualElement ve, bool top)
                    {
                        if (!top && hidden.Contains(ve)) return;
                        if (open.TryGetValue(ve, out var box)) boxes[ve] = box;
                        foreach (var child in ve.Children()) Take(child, false);
                    }
                }
            });
            var lines = text.Split('\n');
            for (var i = 0; i < _hide.Count; i++)
            {
                var name = _hide[i].Name;
                var tail = " id=" + name + " {";
                var k = Array.FindIndex(lines, l => l.TrimStart().StartsWith("G ", StringComparison.Ordinal) && l.EndsWith(tail, StringComparison.Ordinal));
                if (k < 0)
                {
                    // with no group to carry its v, a hidden element would be drawn: never that
                    Node at = (Node?)_hide[i].AttrOps.FirstOrDefault(o => o.Name == "hidden")?.At
                              ?? (_madeBy.TryGetValue(_hide[i], out var made) ? made.Writes[0].At : _ast);
                    Refuse(at, $"\"{name}\", shown and hidden as the script runs: its group in the scene carries no name");
                    continue;
                }
                var slot = DomSlots.Slot(name) + "_v";
                lines[k] = lines[k].Substring(0, lines[k].Length - tail.Length) + " v=$" + slot + tail;
                values[slot] = new SceneSlots.Value(shown[i] ? 1f : 0f);
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// The page drawn in one more state, or null when that state changes the scene's structure -
        /// a shape added or taken away is not a value.
        /// </summary>
        private Dictionary<string, SceneSlots.Value>? Variant(Action apply, Action undo, Action? laidOut = null)
        {
            // a compile its page no longer wants ends at its next layout (PageCompiler.Cancelled)
            if (PageCompiler.Cancelled?.Invoke() == true) throw new OperationCanceledException();
            MarkupSlots.Emits++;
            try
            {
                apply();
                _panel.Layout(_size.x, _size.y);
                laidOut?.Invoke();
                var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
                var emitted = Emit(values);
                if (emitted == _template) return values;
                _reshaped = Reshaped(_template, emitted);
                return null;
            }
            finally
            {
                undo();
            }
        }

        /// <summary>How the last state that changed the scene's structure changed it, for the refusal.</summary>
        private string _reshaped = string.Empty;

        /// <summary>
        /// What a state did to the scene's structure, as the log can show it: the line counts, and the first
        /// line that differs, at rest and in that state (with the scene's own slot names in it).
        /// </summary>
        internal static string Reshaped(string was, string now)
        {
            var a = was.Split('\n');
            var b = now.Split('\n');
            var i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
            static string Line(string[] lines, int at) => at < lines.Length ? "`" + Cut(lines[at].Trim()) + "`" : "(no line)";
            static string Cut(string line) => line.Length > 200 ? line.Substring(0, 200) + "…" : line;
            return $"{a.Length} scene lines at rest, {b.Length} in this state; line {i + 1} was {Line(a, i)}, is {Line(b, i)}";
        }

        /// <summary>The slots a state moved from the page at rest.</summary>
        /// <remarks>
        /// Against the page laid out once more with nothing changed (<see cref="_baseline"/>), not the
        /// first layout: laying a page out again settles it by a fraction of a pixel here and there (in
        /// game a body's height came back 0.1 different), and that is the layout, not the write. A
        /// number is moved only by half a pixel or more - less than that draws the same.
        /// </remarks>
        private List<string> Moved(Dictionary<string, SceneSlots.Value> state)
        {
            var moved = new List<string>();
            foreach (var pair in state)
            {
                var was = _baseline.TryGetValue(pair.Key, out var b) ? b : _rest[pair.Key];
                if (was.Equals(pair.Value) || was.IsNumber && pair.Value.IsNumber && Math.Abs(was.Number - pair.Value.Number) < Noise) continue;
                moved.Add(pair.Key);
            }
            return moved;
        }

        /// <summary>Less than this, in scene units, is layout noise rather than a move.</summary>
        private const float Noise = 0.5f;

        /// <summary>The page laid out a second time with nothing changed: what every state is compared against.</summary>
        private Dictionary<string, SceneSlots.Value> _baseline = new(StringComparer.Ordinal);

        /// <summary>A slot's number in a state against the baseline, for a message: "459.9 -> 463.1".</summary>
        private string Change(string slot, Dictionary<string, SceneSlots.Value> state)
        {
            static string N(SceneSlots.Value v) => v.IsNumber ? v.Number.ToString("0.##", CultureInfo.InvariantCulture) : "\"" + v.Text + "\"";
            var was = _baseline.TryGetValue(slot, out var b) ? b : _rest[slot];
            return N(was) + " -> " + (state.TryGetValue(slot, out var now) ? N(now) : "nothing");
        }

        /// <summary>A slot the program writes, with what it opens with; false when another facet already writes it.</summary>
        private bool Claim(string slot, string facet, Node at)
        {
            if (_claimed.TryGetValue(slot, out var other) && other != facet)
            {
                Refuse(at, $"{facet} and {other} both move the scene's \"{slot}\", which one value cannot follow");
                return false;
            }
            if (_claimed.ContainsKey(slot)) return true;
            _claimed[slot] = facet;
            Written.Add(slot);
            if (_rest.TryGetValue(slot, out var v)) Opening[slot] = v;
            return true;
        }

        // ---- text -------------------------------------------------------------------------------------

        /// <summary>
        /// Every written label's text as placeholders. The emitter wraps a label's text in its own tags
        /// (italic, small caps), so a marker is emitted in each label and the tags read off around it.
        /// </summary>
        private void Texts(ICollection<Target> group)
        {
            var texts = _order.Where(t => t.Texts.Count > 0 && group.Contains(t)).ToList();
            if (texts.Count == 0) return;
            const string Marker = "\u00A7ph\u00A7";
            var was = new List<(Label, string, List<HtmlNode>)>();
            var marked = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            try
            {
                foreach (var t in texts)
                {
                    var label = (Label)t.Ve;
                    was.Add((label, label.text, new List<HtmlNode>(t.Node.Children)));
                    Text(label, t.Node, Marker);
                }
                Emit(marked);
            }
            finally
            {
                foreach (var (label, text, children) in was)
                {
                    label.text = text;
                    var node = _built.NodeOf[label];
                    node.Children.Clear();
                    node.Children.AddRange(children);
                }
            }

            // Every text a write can put in its label, laid out at the size being compiled for: one that
            // wraps onto another line, or moves another box across, is a browser laying the page out
            // again, which a value cannot follow.
            foreach (var t in texts)
                foreach (var (text, at) in Writable(t))
                    if (Grows(t, text) is { } why)
                    {
                        Refuse(at, $"\"{text}\" in \"{t.Name}\" {why}: a browser lays the page out again, and a value cannot follow it");
                        return;
                    }

            foreach (var t in texts)
            {
                var slot = t.Slot;
                var at = t.Texts[0].At;
                if (!_rest.TryGetValue(slot, out var initial) || initial.IsNumber || !marked.TryGetValue(slot, out var m) || m.IsNumber
                    || !_template.Contains(" text=\"$" + slot + "\"", StringComparison.Ordinal))
                {
                    Refuse(at, $"\"{t.Name}\" draws no text of its own for textContent to replace");
                    continue;
                }
                var cut = m.Text!.IndexOf(Marker, StringComparison.Ordinal);
                if (cut < 0 || m.Text.IndexOf(Marker, cut + 1, StringComparison.Ordinal) >= 0)
                {
                    Refuse(at, $"\"{t.Name}\" changes its text as it draws it (text-transform), which a value written at run time cannot follow");
                    continue;
                }
                var prefix = m.Text.Substring(0, cut);
                var suffix = m.Text.Substring(cut + Marker.Length);
                var shown = initial.Text!;
                shown = shown.StartsWith(prefix, StringComparison.Ordinal) && shown.EndsWith(suffix, StringComparison.Ordinal) && shown.Length >= prefix.Length + suffix.Length
                    ? shown.Substring(prefix.Length, shown.Length - prefix.Length - suffix.Length) : shown;
                if (t.Empty) shown = string.Empty;
                if (_built.CssOf(t.Ve).TryGetValue("font-variant-numeric", out var fvn) && fvn.Contains("tabular"))
                    Warnings.Add($"\"{t.Name}\" asks for tabular figures, which a value written at run time is printed without");

                var plan = new TextPlan();
                var writes = t.Texts.Select(w => (w.At, Parts: Pieces(w.Value))).ToList();
                plan.Rich = writes.Any(w => w.Parts.Any(p => p.Raw));
                string Signature(List<Piece> ps) => string.Join("|", ps.Select(p => p.Literal != null ? "L" + p.Literal : "H" + p.Format));
                var one = writes.Select(w => Signature(w.Parts)).Distinct().ToList();
                var body = new StringBuilder();
                var opening = new List<(string, SceneSlots.Value)>();

                // One shape, and the page's own text is that shape: the literals go in the scene as a
                // hand-written label has them, and only the numbers are slots.
                if (one.Count == 1 && writes[0].Parts.Any(p => p.Hole != null) && Match(shown, writes[0].Parts) is { } values)
                {
                    var k = 0;
                    foreach (var piece in writes[0].Parts)
                    {
                        if (piece.Literal != null) { body.Append(Tmp(piece)); plan.Read.Add((piece.Literal, null, null)); continue; }
                        var name = Unique(slot + "_p" + k.ToString(CultureInfo.InvariantCulture));
                        body.Append("{$").Append(name).Append(':').Append(piece.Format).Append('}');
                        plan.Read.Add((null, name, piece));
                        opening.Add((name, values[k]));
                        foreach (var (w, parts) in writes)
                            Add(plan, w, name, null, parts.Where(p => p.Hole != null).ElementAt(k));
                        k++;
                    }
                }
                else
                {
                    // Otherwise a slot for the text a write sets whole, and per shape of values its own run
                    // of slots: a write fills its own run and empties every other, so nothing is built.
                    var whole = Unique(slot + "_p0");
                    body.Append("{$").Append(whole).Append('}');
                    plan.Read.Add((null, whole, null));
                    opening.Add((whole, new SceneSlots.Value(shown)));
                    var runs = new List<(string Sig, List<string> Slots)>();
                    foreach (var (w, parts) in writes)
                    {
                        if (parts.All(p => p.Literal != null))
                        {
                            Add(plan, w, whole, string.Concat(parts.Select(Tmp)), null);
                            continue;
                        }
                        var sig = string.Join("|", parts.Select(p => p.Literal != null ? "L" : "H" + p.Format));
                        var run = runs.FirstOrDefault(r => r.Sig == sig);
                        if (run.Slots == null)
                        {
                            run = (sig, new List<string>());
                            foreach (var p in parts)
                            {
                                var name = Unique(slot + "_p" + (runs.Sum(r => r.Slots.Count) + run.Slots.Count + 1).ToString(CultureInfo.InvariantCulture));
                                run.Slots.Add(name);
                                body.Append("{$").Append(name);
                                if (p.Hole != null) body.Append(':').Append(p.Format);
                                body.Append('}');
                                plan.Read.Add((null, name, p.Hole != null ? p : null));
                                opening.Add((name, new SceneSlots.Value(string.Empty)));
                            }
                            runs.Add(run);
                        }
                        Add(plan, w, whole, string.Empty, null);
                        for (var i = 0; i < parts.Count; i++)
                            Add(plan, w, run.Slots[i], parts[i].Literal != null ? Tmp(parts[i]) : null, parts[i].Literal != null ? null : parts[i]);
                        foreach (var other in runs)
                            if (other.Sig != sig)
                                foreach (var s in other.Slots) Add(plan, w, s, string.Empty, null);
                    }
                    // a run made after an earlier write was planned is emptied by that write too
                    foreach (var (w, parts) in writes)
                    {
                        var sig = parts.All(p => p.Literal != null) ? null : string.Join("|", parts.Select(p => p.Literal != null ? "L" : "H" + p.Format));
                        foreach (var r in runs)
                            if (r.Sig != sig)
                                foreach (var s in r.Slots)
                                    if (!plan.Writes[w].Any(x => x.Slot == s)) plan.Writes[w].Add((s, string.Empty, null));
                    }
                }
                plan.Template = prefix + body + suffix;
                t.Text = plan;
                _textSlots[slot] = plan.Template;
                foreach (var (name, value) in opening)
                {
                    if (!Claim(name, $"the text of \"{t.Name}\"", at)) break;
                    Opening[name] = value;
                    // a text changes at once, as a browser's does: its numbers never glide
                    _ease[name] = "0";
                }
                // the label's own text slot is the template now: nothing writes it
                _claimed[slot] = $"the text of \"{t.Name}\"";
            }

            static void Add(TextPlan plan, Expression w, string slot, string? literal, Piece? hole)
            {
                if (!plan.Writes.TryGetValue(w, out var list)) plan.Writes[w] = list = new();
                list.Add((slot, literal, hole));
            }
        }

        /// <summary>
        /// The texts a label can be given, each with the write that gives it: a constant exactly, and a
        /// text with values in it with each value as the texts it can print - every one of a fixed set,
        /// and otherwise the shortest its format prints (0), never a guessed longer one.
        /// ponytail: a number that grows past what its line holds overflows its box on the console where a
        /// browser would push the rest of the line along; only the values the source fixes are laid out.
        /// </summary>
        private List<(string Text, Expression At)> Writable(Target t)
        {
            var found = new List<(string, Expression)>();
            foreach (var (at, value) in t.Texts)
            {
                var texts = new List<string> { string.Empty };
                foreach (var piece in Pieces(value))
                {
                    List<string> options;
                    if (piece.Literal != null) options = new List<string> { piece.Literal };
                    else if (Finite(piece.Hole!) is { Count: > 0 } set)
                        options = set.Select(v => v is double d ? PrintJs(d, piece) : (string)v).Distinct().ToList();
                    else options = new List<string> { PrintJs(0, piece) };
                    var next = new List<string>();
                    foreach (var head in texts) foreach (var o in options) if (next.Count < 32) next.Add(head + o);
                    texts = next;
                }
                foreach (var text in texts)
                    if (!found.Any(f => f.Item1 == text)) found.Add((text, at));
            }
            return found;
        }

        /// <summary>A number as a write prints it: `x.toFixed(n)` fixed, anything else as JavaScript turns a number into text.</summary>
        private static string PrintJs(double v, Piece piece)
            => piece.Digits is { } n ? JsNumber.ToFixed(v, n) : JsToLuaNumber(v);

        private readonly Dictionary<Target, float> _restHeight = new();

        /// <summary>
        /// Why one text in a label is not a value, laid out at the size being compiled for: it wraps (its
        /// label grows by half a line or more), it changes the scene's structure, or it moves another box
        /// across or resizes one sideways. Null when it is a value. A change up or down while the label
        /// stays on one line is the text engine measuring these glyphs rather than a line - a browser's
        /// line box is as tall whatever the letters in it - so nothing is decided on it.
        /// </summary>
        private string? Grows(Target t, string text)
        {
            var label = (Label)t.Ve;
            var node = t.Node;
            var was = label.text;
            var children = new List<HtmlNode>(node.Children);
            var height = float.NaN;
            var drawn = Variant(() => Text(label, node, text), () =>
            {
                label.text = was;
                node.Children.Clear();
                node.Children.AddRange(children);
            }, () => height = label.layout.height);
            if (drawn == null) return "changes the scene's structure (" + _reshaped + ")";
            var rest = _restHeight.TryGetValue(t, out var h) ? h : height;
            var wraps = height - rest >= 0.5f * Math.Max(1f, label.resolvedStyle.fontSize);
            if (wraps) return "wraps onto another line";
            var own = new HashSet<string>(StringComparer.Ordinal) { t.Slot, t.Slot + "_w", t.Slot + SceneSlots.SecondSuffix + "_w" };
            foreach (var slot in Moved(drawn))
            {
                if (own.Contains(slot) || Vertical(slot)) continue;
                return $"moves the scene's \"{slot}\" ({Change(slot, drawn)})";
            }
            return null;
        }

        /// <summary>A slot that places or sizes something up and down the page.</summary>
        private static bool Vertical(string slot)
        {
            if (slot.EndsWith("_t_1", StringComparison.Ordinal)) return true;
            var key = slot.Substring(slot.LastIndexOf('_') + 1);
            return key is "y" or "h" or "cy" or "ry" or "y1" or "y2" or "ch";
        }

        /// <summary>
        /// The page's own text read as one shape: each hole's value, as a number when that prints back
        /// exactly as it was, or null when the literals do not line up.
        /// </summary>
        private static List<SceneSlots.Value>? Match(string shown, List<Piece> parts)
        {
            var text = shown.Replace("<noparse>", string.Empty).Replace("</noparse>", string.Empty);
            var values = new List<SceneSlots.Value>();
            var at = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Literal != null)
                {
                    if (text.Length - at < p.Literal.Length || string.CompareOrdinal(text, at, p.Literal, 0, p.Literal.Length) != 0) return null;
                    at += p.Literal.Length;
                    continue;
                }
                var next = i + 1 < parts.Count ? parts[i + 1].Literal : null;
                var end = next == null ? text.Length : text.IndexOf(next, at, StringComparison.Ordinal);
                if (end < 0) return null;
                var hole = text.Substring(at, end - at);
                values.Add(float.TryParse(hole, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && Printed(f, p.Format) == hole
                    ? new SceneSlots.Value(f) : new SceneSlots.Value(hole));
                at = end;
            }
            return at == text.Length ? values : null;
        }

        /// <summary>A number as the vector mod's placeholder prints it: `%.nf` fixed, `%g` its shortest form.</summary>
        internal static string Printed(float v, string format)
            => format.StartsWith("%.", StringComparison.Ordinal) && format.EndsWith("f", StringComparison.Ordinal)
                ? v.ToString("F" + format.Substring(2, format.Length - 3), CultureInfo.InvariantCulture)
                : v.ToString("G", CultureInfo.InvariantCulture);

        /// <summary>Plain text as TextMeshPro prints it literally: a `&lt;` is not a tag, as the emitter guards it.</summary>
        private static string Tmp(string text) => text.Replace("<", "<noparse><</noparse>");

        /// <summary>A literal piece as the scene prints it: plain text guarded, rich text (markup) as it is.</summary>
        private static string Tmp(Piece p) => p.Raw ? p.Literal! : Tmp(p.Literal!);

        private string Unique(string name)
        {
            var candidate = name;
            for (var k = 2; _rest.ContainsKey(candidate) || _claimed.ContainsKey(candidate); k++) candidate = name + "_" + k.ToString(CultureInfo.InvariantCulture);
            return candidate;
        }

        /// <summary>The scene with each written label's text replaced by its placeholders.</summary>
        public string Placed(string template)
        {
            var sb = new StringBuilder(template);
            foreach (var pair in _textSlots)
                sb.Replace(" text=\"$" + pair.Key + "\"", " text=\"" + Scene(pair.Value) + "\"");
            return sb.ToString();

            static string Scene(string s)
            {
                s = s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", string.Empty).Replace("\n", "\\n");
                // a leading `=` is an expression to the scene reader
                return s.StartsWith("=", StringComparison.Ordinal) ? "<noparse>=</noparse>" + s.Substring(1) : s;
            }
        }

        // ---- style ------------------------------------------------------------------------------------

        private void Style(Target t, string css, List<(Expression At, Expression Value)> writes,
                           HashSet<string> available, Dictionary<VisualElement, Vector2> absolute)
        {
            var at = writes[0].At;
            // the whole style attribute, written in markup: each value laid out as the attribute it is, as a browser cascades it
            var whole = css == WholeStyle;
            var box = PageCompiler.BoxOf(t.Name, _built, absolute, available);
            if (box == null) { Refuse(at, $"\"{t.Name}\" is not laid out"); return; }
            var mapped = whole ? default : DomSlots.Map(t.Name, "style." + css, box.Value, available);
            if (!whole && !mapped.Mapped) { Refuse(at, $"style.{css} on \"{t.Name}\": {mapped.Problem}"); return; }
            if (!whole && mapped.NeedsGroup) { Refuse(at, $"style.{css} on \"{t.Name}\": its group carries no name"); return; }

            var plan = new StylePlan { Var = "V_S" + (_order.Sum(x => x.StylePlans.Count) + 1).ToString(CultureInfo.InvariantCulture) };
            var facet = whole ? $"the style attribute of \"{t.Name}\"" : $"style.{css} of \"{t.Name}\"";
            var node = t.Node;
            var style = node.Attr("style");
            var cls = node.Attr("class") ?? string.Empty;
            var classless = node.Attr("class") == null;
            // Reclass writes the class attribute; an element the page gave none is put back with none
            Dictionary<string, SceneSlots.Value>? Drawn(string value) => Variant(
                () => { node.Attributes["style"] = whole ? value : (style ?? string.Empty) + ";" + css + ":" + value; _built.Reclass(t.Ve, cls); },
                () =>
                {
                    if (style == null) node.Attributes.Remove("style"); else node.Attributes["style"] = style;
                    _built.Reclass(t.Ve, cls);
                    if (classless) node.Attributes.Remove("class");
                });

            // Every value from a fixed set: each is laid out, and the write picks the state it draws.
            var sets = writes.Select(w => Finite(w.Value)).ToList();
            if (sets.All(s => s != null))
            {
                plan.States = new Dictionary<object, Dictionary<string, SceneSlots.Value>>();
                var moved = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in sets.SelectMany(s => s!).Distinct())
                {
                    var text = value is double d ? JsToLuaNumber(d) : (string)value;
                    var drawn = Drawn(text);
                    if (drawn == null) { Refuse(at, $"{facet} = \"{text}\" changes the scene's structure ({_reshaped}), which is not a value"); return; }
                    plan.States[value] = drawn;
                    moved.UnionWith(Moved(drawn));
                }
                Complete(plan.States.Values, moved);
                foreach (var slot in moved) if (!Claim(slot, facet, at)) return;
                t.StylePlans[css] = plan;
                return;
            }

            if (whole) { Refuse(at, $"{facet} is written in markup a value only known at run time (one from a fixed set is translated: each laid out as the attribute it is)"); return; }

            // Otherwise a number: the slots DomSlots names, each a scale and an offset of it.
            var numbers = writes.Select(w => Numeric(w.Value)).ToList();
            if (numbers.Any(n => n == null)) { Refuse(at, $"{facet} is written a value that is neither one of a fixed set nor a number"); return; }
            var units = numbers.Select(n => n!.Value.Unit).Distinct().ToList();
            if (units.Count != 1) { Refuse(at, $"{facet} is written in more than one unit ({string.Join(", ", units)})"); return; }
            var unit = units[0];
            plan.Unit = unit;
            double factor;
            if (unit == "px" || unit.Length == 0 && css is "opacity") factor = 1;
            else if (unit == "%" && !double.IsNaN(mapped.PercentOf)) factor = mapped.PercentOf / 100;
            // a value with no unit the compile cannot fix: for a colour, a colour only known at run time
            else if (unit.Length == 0 && (css == "color" || css.EndsWith("-color", StringComparison.Ordinal) || css is "background" or "fill" or "stroke"))
            { Refuse(at, $"{facet} is written a colour only known at run time (one from a fixed set is translated: the values the script can give it, each laid out)"); return; }
            else if (unit.Length == 0) { Refuse(at, $"{facet} is written a value only known at run time, neither one of a fixed set nor a number with a unit"); return; }
            else { Refuse(at, $"{facet} in \"{unit}\", which is not translated to scene units"); return; }

            plan.Linear = new (string, double, double)[mapped.Slots.Length];
            for (var i = 0; i < mapped.Slots.Length; i++) plan.Linear[i] = (mapped.Slots[i], mapped.Scale[i] * factor, mapped.Bias[i]);

            // Proved on the laid-out page rather than trusted: two samples drawn, and every slot they move
            // has to be one of these, where the scale and offset put it. Two, so that a slot moving
            // with the value can be told from one the layout merely settles differently.
            double[] samples = css == "opacity" ? new[] { 0.37, 0.74 } : new[] { 37.0, 74.0 };
            var proofs = new List<Dictionary<string, SceneSlots.Value>>();
            foreach (var sample in samples)
            {
                var proof = Drawn(JsToLuaNumber(sample) + unit);
                if (proof == null) { Refuse(at, $"{facet} changes the scene's structure at {JsToLuaNumber(sample)}{unit} ({_reshaped})"); return; }
                proofs.Add(proof);
            }
            for (var k = 0; k < proofs.Count; k++)
                foreach (var slot in Moved(proofs[k]))
                {
                    if (Array.FindIndex(plan.Linear, l => l.Slot == slot) >= 0) continue;
                    var other = proofs[1 - k];
                    var follows = other.TryGetValue(slot, out var o) && o.IsNumber && proofs[k][slot].IsNumber && Math.Abs(o.Number - proofs[k][slot].Number) >= Noise;
                    Refuse(at, $"{facet} also moves \"{slot}\" ({Change(slot, proofs[0])} at {JsToLuaNumber(samples[0])}{unit}, {Change(slot, proofs[1])} at {JsToLuaNumber(samples[1])}{unit}; "
                               + (follows ? "it follows the value" : "the same whatever the value") + "), which a scale and an offset of the value cannot follow");
                    return;
                }
            foreach (var (slot, a, b) in plan.Linear)
            {
                for (var k = 0; k < samples.Length; k++)
                {
                    var want = a * samples[k] + b;
                    if (!proofs[k].TryGetValue(slot, out var got) || !got.IsNumber || Math.Abs(got.Number - want) > 0.51)
                    {
                        Refuse(at, $"{facet} draws \"{slot}\" at {(proofs[k].TryGetValue(slot, out var g2) && g2.IsNumber ? g2.Number.ToString("0.##", CultureInfo.InvariantCulture) : "?")} for {JsToLuaNumber(samples[k])}{unit}, where its scale and offset say {want:0.##}");
                        return;
                    }
                }
                if (!Claim(slot, facet, at)) return;
            }
            for (var i = 0; i < writes.Count; i++)
                plan.Holes[writes[i].At] = numbers[i]!.Value.Hole ?? new NumericLiteral(numbers[i]!.Value.Value, JsToLuaNumber(numbers[i]!.Value.Value));
            t.StylePlans[css] = plan;
        }

        /// <summary>Every state carries every slot any of them moves: a state is what the page looks like, not what changed.</summary>
        private static void Complete(IEnumerable<Dictionary<string, SceneSlots.Value>> states, HashSet<string> moved)
        {
            foreach (var state in states)
                foreach (var key in state.Keys.ToList())
                    if (!moved.Contains(key)) state.Remove(key);
        }

        private static string JsToLuaNumber(double v)
            => v == Math.Floor(v) && Math.Abs(v) < 1e15 ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString("R", CultureInfo.InvariantCulture);

        // ---- classes ----------------------------------------------------------------------------------

        /// <summary>
        /// Every combination of the classes and the CSS-selected attributes the script changes on one
        /// element, laid out: the class and attribute states it can draw. At most 64.
        /// </summary>
        private void Classes(Target t)
        {
            var at = t.ClassOps.Count > 0 ? t.ClassOps[0].At : t.ClassNames.Count > 0 ? t.ClassNames[0].At : t.AttrOps.First(o => o.Facet).At;
            var source = SourceNode(t);
            var raw = source.Attr("class");
            var initial = (raw ?? string.Empty).Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            var plan = new ClassPlan { Var = "V_C" + (_order.Count(x => x.Class != null) + 1).ToString(CultureInfo.InvariantCulture) };
            foreach (var op in t.ClassOps) foreach (var n in op.Names) if (!plan.Names.Contains(n)) plan.Names.Add(n);
            foreach (var (_, values) in t.ClassNames)
                foreach (string v in values)
                    foreach (var n in v.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        if (!plan.Names.Contains(n)) plan.Names.Add(n);
            // className replaces them all, so the classes the page starts with can change too
            if (t.ClassNames.Count > 0) foreach (var n in initial) if (!plan.Names.Contains(n)) plan.Names.Add(n);
            foreach (var n in plan.Names) plan.Initial[n] = initial.Contains(n);
            foreach (var (_, values) in t.ClassNames)
                foreach (string v in values)
                    plan.Sets[v] = new HashSet<string>(v.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            var fixedClasses = initial.Where(n => !plan.Names.Contains(n)).ToList();
            plan.Fixed.AddRange(fixedClasses);
            if (t.ClassRead) { plan.Raw = raw ?? string.Empty; plan.Order = initial; }

            // Each attribute CSS selects on, with every value the script can give it and the page's own;
            // absent is the first. A number is the text JavaScript makes of it, and the Lua matches both.
            foreach (var group in t.AttrOps.Where(o => o.Facet).GroupBy(o => o.Name))
            {
                var options = new List<string?> { null };
                var index = new Dictionary<object, int>();
                void Option(string v, object? alias = null)
                {
                    if (!options.Contains(v)) options.Add(v);
                    index[v] = options.IndexOf(v);
                    if (alias != null) index[alias] = options.IndexOf(v);
                }
                if (source.Attr(group.Key) is { } own) Option(own);
                foreach (var op in group)
                    if (op.Verb == "set") foreach (var v in Finite(op.Value!)!) Option(v is double d ? JsToLuaNumber(d) : (string)v, v is double ? v : null);
                    else if (op.Verb is "toggle" or "hidden") Option(string.Empty);
                plan.Attrs.Add((group.Key, options, index));
            }
            var total = (1 << plan.Names.Count) * plan.Attrs.Aggregate(1, (a, x) => a * x.Options.Count);
            // Only className changing them: the class sets it is given (and the page's own) are the only
            // ones the element can have, not every combination of their names.
            HashSet<int>? reachable = null;
            if (t.ClassOps.Count == 0 && plan.Names.Count <= 16)
            {
                int Mask(IEnumerable<string> on) => plan.Names.Select((n, i) => on.Contains(n) ? 1 << i : 0).Sum();
                reachable = new HashSet<int> { Mask(initial) };
                foreach (var set in plan.Sets.Values) reachable.Add(Mask(set));
            }
            var laid = reachable != null ? reachable.Count * (total >> plan.Names.Count) : total;
            if (reachable == null && plan.Names.Count > 6 || laid > 64)
            {
                Refuse(at, $"the script changes {plan.Names.Count} classes and {plan.Attrs.Count} attributes of \"{t.Name}\", {laid} combinations, more than the 64 that are laid out");
                return;
            }

            var node = t.Node;
            var was = node.Attr("class") ?? string.Empty;
            var hadClass = node.Attr("class") != null;
            var wasAttrs = plan.Attrs.Select(a => node.Attr(a.Name)).ToList();
            var wasDisplay = t.Ve.style.display;
            var hides = plan.Attrs.Any(a => a.Name == "hidden");
            void Apply(string cls, IReadOnlyList<string?> attrs)
            {
                for (var i = 0; i < plan.Attrs.Count; i++)
                    if (attrs[i] is { } v) node.Attributes[plan.Attrs[i].Name] = v; else node.Attributes.Remove(plan.Attrs[i].Name);
                if (hides) t.Ve.style.display = StyleKeyword.Null;
                _built.Reclass(t.Ve, cls);
                // the browser's own [hidden] { display: none }, as the page was built with it
                if (hides && HtmlRenderer.HiddenByAttribute(node, _built.CssOf(t.Ve))) t.Ve.style.display = DisplayStyle.None;
            }
            var moved = new HashSet<string>(StringComparer.Ordinal);
            var facet = $"the classes and attributes of \"{t.Name}\"";
            for (var key = 0; key < total; key++)
            {
                if (reachable != null && !reachable.Contains(key & ((1 << plan.Names.Count) - 1))) continue;
                var on = new List<string>(fixedClasses);
                for (var i = 0; i < plan.Names.Count; i++) if ((key & (1 << i)) != 0) on.Add(plan.Names[i]);
                var cls = string.Join(" ", on);
                var attrs = new List<string?>();
                var rest = key >> plan.Names.Count;
                foreach (var a in plan.Attrs) { attrs.Add(a.Options[rest % a.Options.Count]); rest /= a.Options.Count; }
                // put back as built: an element written with no class attribute is left with none
                var drawn = Variant(() => Apply(cls, attrs), () => { Apply(was, wasAttrs); if (!hadClass) node.Attributes.Remove("class"); t.Ve.style.display = wasDisplay; });
                if (drawn == null)
                {
                    var described = cls + string.Concat(plan.Attrs.Select((a, i) => attrs[i] == null ? string.Empty : $" [{a.Name}=\"{attrs[i]}\"]"));
                    Refuse(at, $"class \"{described.Trim()}\" on \"{t.Name}\" changes the scene's structure ({_reshaped}), which is not a value");
                    return;
                }
                plan.States[key] = drawn;
                moved.UnionWith(Moved(drawn));
            }
            Complete(plan.States.Values, moved);
            foreach (var slot in moved) if (!Claim(slot, facet, at)) return;
            t.Class = plan;
        }

        // ---- reads: what the script wrote, answered by the program --------------------------------------

        /// <summary>Constant tables the chunk declares once (a class list looked up by a run-time name).</summary>
        private readonly List<string> _consts = new();
        private bool _cssNum, _toFixedRead, _untmp;

        /// <summary>What the program keeps for the script to read back: each element's attributes, and each style value it writes.</summary>
        private void Reads()
        {
            var tables = 0;
            var values = 0;
            foreach (var t in _order)
            {
                if (t.AttrOps.Count > 0 || t.AttrsByName)
                {
                    t.AttrTable = t.Class != null ? t.Class.Var + ".at" : "V_AT" + (++tables).ToString(CultureInfo.InvariantCulture);
                    _attrs = true;
                }
                var declared = Declarations(SourceNode(t));
                foreach (var css in t.StyleReads)
                {
                    if (!t.StylePlans.TryGetValue(css, out var plan)) continue;
                    plan.ReadVar = "V_SV" + (++values).ToString(CultureInfo.InvariantCulture);
                    declared.TryGetValue(css, out var own);
                    if (plan.Linear != null)
                    {
                        // the number last written; the page's own, when its unit is the one written
                        var m = own == null ? null : Regex.Match(own, @"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)([a-zA-Z%]*)$");
                        if (m is { Success: true } && string.Equals(m.Groups[2].Value, plan.Unit, StringComparison.OrdinalIgnoreCase))
                            plan.ReadInitial = JsToLuaNumber(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                        else plan.Unprintable = own != null;
                        continue;
                    }
                    plan.Printed = new Dictionary<object, string>();
                    foreach (var value in plan.States!.Keys)
                        if (CssPrinted(value is double d ? JsToLuaNumber(d) : (string)value) is { } printed) plan.Printed[value] = printed;
                        else plan.Unprintable = true;
                    if (own != null)
                    {
                        if (CssPrinted(own) is { } ownPrinted) { plan.Printed[own] = ownPrinted; plan.ReadInitial = Q(own); }
                        else plan.Unprintable = true;
                    }
                }
            }
        }

        private string TextRead(Target t, Node at, JsToLua lua)
        {
            if (t.Text is { } plan)
            {
                if (plan.Rich)
                {
                    lua.Refuse(at, $"reading the text of \"{t.Name}\", which the script writes as markup (not translated yet)");
                    return "nil";
                }
                if (plan.Read.Count == 0) return "\"\"";
                var escaped = plan.Template.Contains("<noparse>", StringComparison.Ordinal) || _rest.Values.Any(v => v.Text?.Contains("<noparse>") == true);
                var parts = new List<string>();
                foreach (var (literal, slot, hole) in plan.Read)
                {
                    if (literal != null) { parts.Add(Q(literal)); continue; }
                    var v = "V_D[" + Q(slot!) + "]";
                    if (hole?.Digits is { } n) { _toFixedRead = true; parts.Add("v_tofixed(" + v + ", " + n.ToString(CultureInfo.InvariantCulture) + ")"); }
                    else if (hole == null && escaped) { _untmp = true; parts.Add("v_untmp(" + v + ")"); }
                    else parts.Add("js_str(" + v + ")");
                }
                return parts.Count == 1 ? parts[0] : "(" + string.Join(" .. ", parts) + ")";
            }
            if (_order.Any(o => o != t && o.Texts.Count > 0 && Inside(o.Ve, t.Ve)))
            {
                lua.Refuse(at, $"reading textContent of \"{t.Name}\", which holds text the script writes (not translated yet)");
                return "nil";
            }
            // nothing writes it: the text the page's source gives it, whitespace and all
            return Q(TextContent(SourceNode(t)));
        }

        /// <summary>
        /// innerText read: the text as laid out rather than as written - runs of white space one space, a
        /// block's ends trimmed, `&lt;br&gt;` a line break, and text-transform applied - where textContent
        /// gives the characters as the source or the script put them.
        /// </summary>
        private string InnerTextRead(Target t, Node at, JsToLua lua)
        {
            if (t.Ve is not Label)
            {
                lua.Refuse(at, $"reading innerText of \"{t.Name}\", which holds other elements (the line breaks a browser puts between blocks are not translated yet)");
                return "nil";
            }
            var css = _built.CssOf(t.Ve);
            var transform = css.TryGetValue("text-transform", out var tt) ? tt.Trim().ToLowerInvariant() : "none";
            if (transform is not ("none" or "uppercase" or "lowercase"))
            {
                lua.Refuse(at, $"reading innerText of \"{t.Name}\", whose text-transform ({transform}) the program does not apply yet");
                return "nil";
            }
            var ws = css.TryGetValue("white-space", out var w) ? w.Trim().ToLowerInvariant() : "normal";
            if (ws is not ("normal" or "nowrap"))
            {
                lua.Refuse(at, $"reading innerText of \"{t.Name}\", whose white-space ({ws}) keeps its spaces (not translated yet)");
                return "nil";
            }
            var block = !InlineTags.Contains(t.Node.Tag ?? string.Empty);
            if (t.Text == null && !_order.Any(o => o != t && o.Texts.Count > 0 && Inside(o.Ve, t.Ve)))
            {
                // never written: the page's own text, laid out once here
                var text = Collapse(Rendered(SourceNode(t)), block);
                return Q(transform == "uppercase" ? text.ToUpperInvariant() : transform == "lowercase" ? text.ToLowerInvariant() : text);
            }
            _collapse = true;
            var s = "v_collapse(" + TextRead(t, at, lua) + ", " + (block ? "true" : "false") + ")";
            // ponytail: string.upper and string.lower change ASCII letters only
            return transform == "uppercase" ? "string.upper(" + s + ")" : transform == "lowercase" ? "string.lower(" + s + ")" : s;

            static string Rendered(HtmlNode n)
            {
                if (n.IsText) return n.Raw != null ? HtmlParser.DecodeEntities(n.Raw) : n.Text;
                if (n.Tag == "br") return "\u0001";
                var sb = new StringBuilder();
                foreach (var c in n.Children) sb.Append(Rendered(c));
                return sb.ToString();
            }
        }

        /// <summary>Text as a browser lays it out with white-space normal: each run of white space one space, a block's ends trimmed, a `&lt;br&gt;` (\u0001) a line break.</summary>
        internal static string Collapse(string text, bool block)
        {
            text = Regex.Replace(text, "[ \t\n\r\f]+", " ");
            text = Regex.Replace(text, " ?\u0001 ?", "\n");
            return block ? text.Trim(' ') : text;
        }

        /// <summary>Tags laid out inline: an inline element's innerText keeps a space at its ends, as its line does.</summary>
        private static readonly HashSet<string> InlineTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "span", "b", "i", "u", "s", "small", "big", "sub", "sup", "mark", "code", "font", "em", "strong", "a", "abbr", "cite", "kbd", "q", "samp", "var", "time", "data", "label",
        };

        private static bool Inside(VisualElement ve, VisualElement of)
        {
            for (var p = ve.parent; p != null; p = p.parent) if (p == of) return true;
            return false;
        }

        /// <summary>A node's textContent: every text under it as the source wrote it.</summary>
        private static string TextContent(HtmlNode n)
        {
            if (n.IsText) return n.Raw != null ? HtmlParser.DecodeEntities(n.Raw) : n.Text;
            var sb = new StringBuilder();
            foreach (var c in n.Children) sb.Append(TextContent(c));
            return sb.ToString();
        }

        private string ClassNameRead(Target t)
            => t.Class is { Order: not null } c
                ? "(" + c.Var + ".raw or table.concat(" + c.Var + ".order, \" \"))"
                : Q(SourceNode(t).Attr("class") ?? string.Empty);

        private string Contains(Target t, Expression key, Func<Node, string> tr)
        {
            var k = key is StringLiteral s ? Q(s.Value) : "js_str(" + tr(key) + ")";
            if (t.Class != null) return "(" + t.Class.Var + ".on[" + k + "] == true)";
            var classes = SourceClasses(t);
            if (key is StringLiteral lit) return classes.Contains(lit.Value) ? "true" : "false";
            return "(" + Const(t, "set", () => Table(classes.Select(c => (c, "true")))) + "[" + k + "] == true)";
        }

        /// <summary>The classes the page's source gives an element, each once, in order.</summary>
        private List<string> SourceClasses(Target t)
            => (SourceNode(t).Attr("class") ?? string.Empty).Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

        private readonly Dictionary<(Target, string), string> _constOf = new();

        /// <summary>A constant table the chunk declares once per element and use.</summary>
        private string Const(Target t, string use, Func<string> value)
        {
            if (_constOf.TryGetValue((t, use), out var name)) return name;
            name = "V_K" + (_consts.Count + 1).ToString(CultureInfo.InvariantCulture);
            _consts.Add("local " + name + " = " + value());
            return _constOf[(t, use)] = name;
        }

        /// <summary>classList.item(i): the class at that place, in the order the class attribute holds them; null past the end.</summary>
        private string ClassItem(Target t, Expression index, Func<Node, string> tr)
        {
            if (t.Class == null && index is NumericLiteral { Value: var k } && k == Math.Floor(k))
            {
                var own = SourceClasses(t);
                return k >= 0 && k < own.Count ? Q(own[(int)k]) : "nil";
            }
            _clsItem = true;
            var i = tr(index);
            if (t.Class != null) return "v_clsitem(" + t.Class.Var + ".order, " + i + ")";
            var classes = SourceClasses(t);
            return "v_clsitem(" + Const(t, "list", () => "{ " + string.Join(", ", classes.Select(Q)) + " }") + ", " + i + ")";
        }

        /// <summary>classList.length: how many classes the element has now.</summary>
        private string ClassCount(Target t)
        {
            if (t.Class == null) return SourceClasses(t).Count.ToString(CultureInfo.InvariantCulture);
            _clsCount = true;
            return "v_clscount(" + t.Class.Var + ".on)";
        }

        /// <summary>
        /// A style property read back, as the browser's inline style gives it: the value the script last
        /// wrote (a colour as rgb(), a length to six figures, a keyword in lower case), else what the style
        /// attribute says, else "".
        /// </summary>
        private string StyleRead(Target t, string css, Node at, JsToLua lua)
        {
            bool Related(string p) => p != css && (p.StartsWith(css + "-", StringComparison.Ordinal) || css.StartsWith(p + "-", StringComparison.Ordinal));
            var declared = Declarations(SourceNode(t));
            if (t.Styles.Keys.Any(Related) || declared.Keys.Any(Related))
            {
                lua.Refuse(at, $"reading style.{css} of \"{t.Name}\", which a shorthand or longhand of it also sets (not translated yet)");
                return "nil";
            }
            if (t.StylePlans.TryGetValue(css, out var plan) && plan.ReadVar != null)
            {
                if (plan.Linear != null && plan.Unprintable)
                {
                    lua.Refuse(at, $"reading style.{css} of \"{t.Name}\", whose style attribute gives it in another unit than the script writes (not translated yet)");
                    return "nil";
                }
                if (plan.Linear != null)
                {
                    _cssNum = true;
                    return "v_cssnum(" + plan.ReadVar + ", " + Q(plan.Unit) + ")";
                }
                if (plan.Unprintable)
                {
                    lua.Refuse(at, $"reading style.{css} of \"{t.Name}\", written a value the browser gives back in a form not translated yet");
                    return "nil";
                }
                return "(" + plan.ReadVar + "P[" + plan.ReadVar + "] or \"\")";
            }
            if (!declared.TryGetValue(css, out var own)) return "\"\"";
            if (CssPrinted(own) is { } printed) return Q(printed);
            lua.Refuse(at, $"reading style.{css} of \"{t.Name}\", whose value the browser gives back in a form not translated yet");
            return "nil";
        }

        /// <summary>A style attribute as its declarations, by property.</summary>
        private static Dictionary<string, string> Declarations(HtmlNode node)
        {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in (node.Attr("style") ?? string.Empty).Split(';'))
            {
                var colon = part.IndexOf(':');
                if (colon <= 0) continue;
                found[part.Substring(0, colon).Trim().ToLowerInvariant()] = part.Substring(colon + 1).Trim();
            }
            return found;
        }

        /// <summary>A CSS value as a browser's inline style gives it back, or null for a form not translated.</summary>
        internal static string? CssPrinted(string value)
        {
            var v = value.Trim();
            if (v.Length == 0) return string.Empty;
            var hex = Regex.Match(v, "^#([0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");
            if (hex.Success)
            {
                var h = hex.Groups[1].Value;
                if (h.Length <= 4) h = string.Concat(h.Select(c => new string(c, 2)));
                int B(int i) => Convert.ToInt32(h.Substring(i * 2, 2), 16);
                if (h.Length == 6) return $"rgb({B(0)}, {B(1)}, {B(2)})";
                // the fewest decimals that come back to the same byte
                var a = Math.Round(B(3) / 255.0, 2);
                if ((int)Math.Round(a * 255) != B(3)) a = Math.Round(B(3) / 255.0, 3);
                return $"rgba({B(0)}, {B(1)}, {B(2)}, {a.ToString("0.###", CultureInfo.InvariantCulture)})";
            }
            var num = Regex.Match(v, @"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)([a-zA-Z%]*)$");
            if (num.Success)
                return double.Parse(num.Groups[1].Value, CultureInfo.InvariantCulture).ToString("G6", CultureInfo.InvariantCulture).Replace("E", "e")
                       + num.Groups[2].Value.ToLowerInvariant();
            return Regex.IsMatch(v, "^[a-zA-Z-]+$") ? v.ToLowerInvariant() : null;
        }

        /// <summary>
        /// An attribute read: from the program's table when the script changes this element's attributes
        /// (or reads them by a run-time name), else the constant the page's source gives it.
        /// </summary>
        private string AttrRead(Target t, bool has, string? name, Expression? byName, Func<Node, string> tr, Node? read = null)
        {
            if (t.AttrTable != null)
            {
                var v = t.AttrTable + "[" + (name != null ? Q(name) : "string.lower(js_str(" + tr(byName!) + "))") + "]";
                return has ? "(" + v + " ~= nil)" : (read != null && Numbered(read) ? "v_attrnum(" : "v_attr(") + v + ")";
            }
            var node = SourceNode(t);
            var own = Marker(t, node, name!) ? null : node.Attr(name!);
            return has ? (own != null ? "true" : "false") : own != null ? Q(own) : "nil";
        }

        /// <summary>
        /// One of the mod's own markers (a click region's data-click and the like) on the live node of an element
        /// markup makes, which is where its attributes are read from: the page never wrote it, so it never reads it.
        /// </summary>
        // ponytail: a marker the page wrote itself into markup is hidden too; tell them apart by the markup's text if one matters
        private static bool Marker(Target t, HtmlNode node, string key)
            => ReferenceEquals(node, t.Node) && HtmlRenderer.InternalAttributes.Contains(key);

        /// <summary>Whether a read is turned straight into a number: `Number(x)` or `+x`.</summary>
        private bool Numbered(Node read)
            => _parent.TryGetValue(read, out var p)
               && (p is CallExpression { Callee: Identifier { Name: "Number" } num, Arguments.Count: 1 } c && c.Arguments[0] == read && Decl(num) == null
                   || p is NonUpdateUnaryExpression { Operator: Operator.UnaryPlus });

        private static string AttrLua(Target t, AttrOp op, Func<Node, string> tr)
        {
            var at = t.AttrTable!;
            var c = op.Facet ? t.Class!.Var : "nil";
            var n = Q(op.Name);
            return op.Verb switch
            {
                "set" => "v_setattr(" + at + ", " + n + ", " + tr(op.Value!) + ", " + c + ")",
                "remove" => "v_rmattr(" + at + ", " + n + ", " + c + ")",
                "toggle" => "v_toggleattr(" + at + ", " + n + ", " + (op.Value != null ? tr(op.Value) : "nil") + ", " + c + ")",
                _ => "v_hidden(" + at + ", " + tr(op.Value!) + ", " + c + ")",
            };
        }

        /// <summary>An element's attributes as the program's table starts: the page's own, but for id, class and style.</summary>
        private string AttrInitial(Target t)
        {
            var node = SourceNode(t);
            var pairs = new List<(string, string)>();
            if (node.AttributeCount > 0)
                foreach (var pair in node.Attributes)
                {
                    var key = pair.Key.ToLowerInvariant();
                    if (key is "id" or "class" or "style") continue;
                    if (Marker(t, node, key)) continue;
                    pairs.Add((key, Q(pair.Value)));
                }
            return Table(pairs);
        }

        // ---- the program ------------------------------------------------------------------------------

        /// <summary>The DOM writes and timers of the script, as the Lua that does them.</summary>
        public bool Statement(JsToLua lua, Node s)
        {
            // markup built in steps: written after its last step, the steps themselves doing nothing
            if (_markupAt.TryGetValue(s, out var steps)) { EmitMarkup(lua, steps); return true; }
            if (_silenced.Contains(s)) return true;
            if (s is VariableDeclaration && _elementDeclarations.Contains(s)) return true;
            if (s is ExpressionStatement es) s = es.Expression;
            if (_markupAt.TryGetValue(s, out steps)) { EmitMarkup(lua, steps); return true; }
            if (_silenced.Contains(s)) return true;
            if (s is not Expression e) return false;
            if (e is AssignmentExpression ma && _markup.TryGetValue(ma, out var markup))
            {
                EmitMarkup(lua, markup);
                return true;
            }
            if (e is AssignmentExpression mp && _markupPick.TryGetValue(mp, out var picked))
            {
                // an element chosen at run time: its number picks the write laid out into it; none is the browser's TypeError on null
                _nullref = true;
                lua.Emit("do");
                lua.Emit("  local V_el = " + lua.Translate(picked.Recv));
                for (var i = 0; i < picked.Writes.Count; i++)
                {
                    lua.Emit((i == 0 ? "  if" : "  elseif") + " V_el == " + picked.Writes[i].Of.T.Number.ToString(CultureInfo.InvariantCulture) + " then");
                    EmitMarkup(lua, picked.Writes[i]);
                }
                lua.Emit("  else v_nullref() end");
                lua.Emit("end");
                return true;
            }
            if (_lua.TryGetValue(e, out var own))
            {
                // A concise arrow's body is its return value: only a write, which yields nothing, is a statement there.
                if (_parent.TryGetValue(e, out var holder) && holder is ArrowFunctionExpression && !Effectual(e)) return false;
                if (_consoleKept.TryGetValue(e, out var kept))
                {
                    foreach (var arg in kept) lua.Effect(arg);
                    return true;
                }
                var code = own(lua);
                if (Call.IsMatch(code)) lua.Emit(code);
                else if (_picks.TryGetValue(code, out var pick)) lua.Emit("do local __f = " + pick.Pick + " __f(" + pick.Args + ") end");
                else if (code != "nil") lua.Emit("local __discard = " + code);
                return true;
            }
            if (!_ops.TryGetValue(e, out var ts)) return false;
            if (_recv.TryGetValue(e, out var recv))
            {
                // an element chosen at run time: the pick, then the operation on the one picked
                var pick = _picks[Dispatch(recv, ts, lua, (t, tr) => string.Join("\n", EmitOp(e, t, tr, early: false)))];
                lua.Emit("do local __f = " + pick.Pick + " __f(" + pick.Args + ") end");
                return true;
            }
            foreach (var line in EmitOp(e, ts[0], lua.Translate, early: true)) lua.Emit(line);
            return true;
        }

        /// <summary>
        /// One DOM write on one element, as Lua lines; <paramref name="tr"/> gives each value the write needs.
        /// <paramref name="early"/>: the values of a text written into several slots are worked out before
        /// any is set, since a value can read the text being replaced (`el.textContent += '.'`).
        /// </summary>
        private List<string> EmitOp(Expression e, Target t, Func<Node, string> tr, bool early)
        {
            var lines = new List<string>();
            if (e is AssignmentExpression { Left: MemberExpression m } a)
            {
                if (m.Object is MemberExpression { Computed: false, Property: Identifier { Name: "style" } })
                {
                    var css = DomSlots.Dashed(m.Property is Identifier si ? si.Name : ((StringLiteral)m.Property).Value);
                    var plan = t.StylePlans[css];
                    if (plan.ReadVar != null)
                    {
                        // read back later: the value is kept as written, and printed only when read
                        lines.Add("do");
                        lines.Add("  local v = " + tr(plan.States != null ? a.Right : plan.Holes[e]));
                        lines.Add("  " + plan.ReadVar + " = v");
                        if (plan.States != null) lines.Add("  v_state(" + plan.Var + ", v)");
                        else foreach (var (slot, sa, sb) in plan.Linear!) lines.Add("  v_set(" + Q(slot) + ", " + Affine("v", sa, sb) + ")");
                        lines.Add("end");
                        return lines;
                    }
                    if (plan.States != null)
                    {
                        lines.Add("v_state(" + plan.Var + ", " + tr(a.Right) + ")");
                        return lines;
                    }
                    var hole = plan.Holes[e];
                    if (plan.Linear!.Length == 1)
                    {
                        lines.Add("v_set(" + Q(plan.Linear[0].Slot) + ", " + Affine(tr(hole), plan.Linear[0].A, plan.Linear[0].B) + ")");
                        return lines;
                    }
                    lines.Add("do");
                    lines.Add("  local v = " + tr(hole));
                    foreach (var (slot, sa, sb) in plan.Linear) lines.Add("  v_set(" + Q(slot) + ", " + Affine("v", sa, sb) + ")");
                    lines.Add("end");
                    return lines;
                }
                if (m.Property is Identifier { Name: "textContent" or "innerText" })
                {
                    var sets = t.Text!.Writes[e];
                    var named = new Dictionary<Piece, string>();
                    if (early && sets.Count > 1 && sets.Any(x => x.Hole != null))
                    {
                        lines.Add("do");
                        foreach (var (_, _, hole) in sets)
                            if (hole != null && !named.ContainsKey(hole))
                            {
                                named[hole] = "p" + (named.Count + 1).ToString(CultureInfo.InvariantCulture);
                                lines.Add("  local " + named[hole] + " = " + HoleLua(tr, hole));
                            }
                    }
                    var pad = named.Count > 0 ? "  " : string.Empty;
                    foreach (var (slot, literal, hole) in sets)
                        lines.Add(pad + "v_set(" + Q(slot) + ", " + (literal != null ? JsToLua.Quote(literal) : named.TryGetValue(hole!, out var n) ? n : HoleLua(tr, hole!)) + ")");
                    if (named.Count > 0) lines.Add("end");
                    return lines;
                }
                if (m.Property is Identifier { Name: "className" } || m is { Property: Identifier { Name: "value" }, Object: MemberExpression { Property: Identifier { Name: "classList" } } })
                {
                    lines.Add("v_classname(" + t.Class!.Var + ", " + tr(a.Right) + ")");
                    return lines;
                }
                _onclick = true;
                lines.Add("v_onclick(" + Q(t.Name) + ", " + tr(a.Right) + ")");
                return lines;
            }

            if (e is CallExpression { Callee: MemberExpression { Property: Identifier { Name: "addEventListener" } } } listen)
            {
                lines.Add("v_listen(" + Q(t.Name) + ", " + tr(listen.Arguments[1]) + ")");
                return lines;
            }

            // classList.add / remove / toggle
            var op = t.ClassOps.First(o => o.At == e);
            var c = t.Class!.Var;
            foreach (var name in op.Names)
            {
                var on = c + ".on[" + Q(name) + "]";
                var want = op.Verb switch
                {
                    "add" => "true",
                    "remove" => "false",
                    _ => op.Force != null ? "js_truthy(" + tr(op.Force) + ")" : "not " + on,
                };
                // className is read back: the classes' order is kept as the attribute holds it
                lines.Add(t.Class.Order != null ? "v_classop(" + c + ", " + Q(name) + ", " + want + ")" : on + " = " + want);
            }
            lines.Add("v_class(" + c + ")");
            return lines;
        }

        /// <summary>
        /// An operation on an element chosen at run time, as a hand-written console picks one: a table, built
        /// once, from each element's number to a function doing the operation on that element, and the call
        /// that picks from it by the number the script holds. The values the operation needs are worked out
        /// where the script works them out, in its order, and passed in.
        /// </summary>
        private string Dispatch(Expression recv, List<Target> ts, JsToLua lua, Func<Target, Func<Node, string>, string> body)
        {
            var values = new List<Node>();
            foreach (var t in ts)
                body(t, x =>
                {
                    if (x is not Acornima.Ast.Literal && !values.Contains(x)) values.Add(x);
                    return "nil";
                });
            values.Sort((x, y) => x.Range.Start.CompareTo(y.Range.Start));
            var names = values.Select((_, i) => "a" + (i + 1).ToString(CultureInfo.InvariantCulture)).ToList();
            var table = "V_X" + (++_dispatches).ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder("local ").Append(table).Append(" = {\n");
            foreach (var t in ts)
            {
                sb.Append("  [").Append(t.Number.ToString(CultureInfo.InvariantCulture)).Append("] = function(").Append(string.Join(", ", names)).Append(")\n");
                foreach (var line in body(t, x => x is Acornima.Ast.Literal ? lua.Translate(x) : names[values.IndexOf(x)]).Split('\n'))
                    sb.Append("    ").Append(line).Append('\n');
                sb.Append("  end,\n");
            }
            _tables.Add(sb.Append("}\n").ToString());
            _nullref = true;
            var pick = table + "[" + lua.Translate(recv) + "] or v_nullref";
            var args = string.Join(", ", values.Select(lua.Translate));
            var code = "(" + pick + ")(" + args + ")";
            _picks[code] = (pick, args);
            return code;
        }

        /// <summary>Each pick's call, and its two halves, for a statement that makes it: `do local __f = pick __f(args) end`.</summary>
        private readonly Dictionary<string, (string Pick, string Args)> _picks = new(StringComparer.Ordinal);

        private int _dispatches;
        /// <summary>Tables the chunk builds once, before the page's script runs: the picks, element lists and id lookups.</summary>
        private readonly List<string> _tables = new();
        private bool _nullref, _item, _clsItem, _clsCount, _collapse, _nadd, _onclick;

        /// <summary>A call, which Lua takes as a statement of its own.</summary>
        private static readonly Regex Call = new(@"^[A-Za-z_][A-Za-z0-9_.]*\(.*\)$", RegexOptions.Singleline);

        /// <summary>Whether an expression the Lua writes itself is a write, whose value (undefined) nothing needs.</summary>
        private bool Effectual(Expression e) => _effects.Contains(e) || e is AssignmentExpression;

        /// <summary>Timers, elements as the values the Lua holds for them, and a null read where it prints.</summary>
        public string? Expression(JsToLua lua, Node e)
        {
            if (_lua.TryGetValue(e, out var own)) return own(lua);
            if (MarkupExpression(lua, e) is { } inMarkup) return inMarkup;
            if (e is CallExpression cl && _closestOf.ContainsKey(cl)) return ClosestLua(cl, lua);
            if (e is CallExpression { Callee: Identifier callee } call && TimerNames.Contains(callee.Name) && !Declared(callee.Name))
            {
                string Arg(int i) => i < call.Arguments.Count ? lua.Translate(call.Arguments[i]) : "nil";
                return callee.Name switch
                {
                    "setTimeout" => "v_timer(" + Arg(0) + ", " + Arg(1) + ", false)",
                    "setInterval" => "v_timer(" + Arg(0) + ", " + Arg(1) + ", true)",
                    _ => "v_clear(" + Arg(0) + ")",
                };
            }
            switch (e)
            {
                // an element is its number, a list of them a table of numbers built once
                case CallExpression c when DomCall(c) != null:
                    return DomValue(c, lua);
                case MemberExpression root when IsRoot(root):
                    return Root().Number.ToString(CultureInfo.InvariantCulture);
                case Identifier id when _vars.TryGetValue(id.Name, out var held):
                    return held.Number.ToString(CultureInfo.InvariantCulture);
                // `this` in a click listener: its element, or the one the click is going through (V_EV.currentTarget)
                case ThisExpression th when Elems(th) is { Bad: null, Neutral: false } me:
                    return me.One ? me.Ts[0].Number.ToString(CultureInfo.InvariantCulture) : "V_EV.currentTarget";
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "item" } } im } ic when Elems(im.Object) is { List: true }:
                    _item = true;
                    return "v_item(" + lua.Translate(im.Object) + ", " + (ic.Arguments.Count > 0 ? lua.Translate(ic.Arguments[0]) : "nil") + ")";

                // A read that yields null prints "null" where JavaScript turns it into text; Lua's nil is undefined.
                case NonLogicalBinaryExpression { Operator: Operator.Addition } add when IsNull(add.Left) || IsNull(add.Right):
                    _nadd = true;
                    return "v_nadd(" + lua.Translate(add.Left) + ", " + lua.Translate(add.Right) + ", " + (IsNull(add.Left) ? "true" : "false") + ", " + (IsNull(add.Right) ? "true" : "false") + ")";
                case TemplateLiteral tl when tl.Expressions.Any(IsNull):
                    {
                        var parts = new List<string>();
                        for (var i = 0; i < tl.Quasis.Count; i++)
                        {
                            var q = tl.Quasis[i].Value.Cooked ?? tl.Quasis[i].Value.Raw ?? string.Empty;
                            if (q.Length > 0) parts.Add(Q(q));
                            if (i < tl.Expressions.Count)
                                parts.Add(IsNull(tl.Expressions[i]) ? "(" + lua.Translate(tl.Expressions[i]) + " or \"null\")" : "js_str(" + lua.Translate(tl.Expressions[i]) + ")");
                        }
                        return parts.Count == 1 ? parts[0] : "(" + string.Join(" .. ", parts) + ")";
                    }
                case CallExpression { Callee: Identifier { Name: "String" } str, Arguments.Count: 1 } sc when !Declared("String") && sc.Arguments[0] is Expression sa && IsNull(sa):
                    return "(" + lua.Translate(sa) + " or \"null\")";
            }
            return null;

            // null as written, or a read that gives a string or null (getAttribute, getItem, key, item)
            bool IsNull(Node x) => x is NullLiteral || _null.TryGetValue(x, out var none) && none == "null";
        }

        /// <summary>A DOM lookup as the Lua holds its result: an element's number, a list's table, or a table from the id's value to the element.</summary>
        private string DomValue(CallExpression c, JsToLua lua)
        {
            if (_domValue.TryGetValue(c, out var known)) return known;
            var q = DomCall(c)!.Value;
            var els = Elems(c);
            string value;
            if (els == null || els.Bad != null) value = "nil";
            else if (q.Kind == "getElementById" && !(els.One && q.Arg is { } pa && Pure(pa)) && ById(q.Arg, null).Key is { } key)
            {
                var name = "V_ID" + (_tables.Count + 1).ToString(CultureInfo.InvariantCulture);
                var entries = new List<string>();
                foreach (var pair in key.Table)
                {
                    entries.Add("[" + Q(pair.Key) + "] = " + pair.Value.Number.ToString(CultureInfo.InvariantCulture));
                    // the value may be the number that prints as this text: `'row' + 3`
                    if (double.TryParse(pair.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && JsToLuaNumber(d) == pair.Key)
                        entries.Add("[" + pair.Key + "] = " + pair.Value.Number.ToString(CultureInfo.InvariantCulture));
                }
                _tables.Add("local " + name + " = { " + string.Join(", ", entries) + " } -- the element each id names\n");
                value = name + "[" + lua.Translate(key.Hole) + "]";
            }
            else if (els.List)
            {
                var name = "V_L" + (_tables.Count + 1).ToString(CultureInfo.InvariantCulture);
                var items = els.Lists[0];
                _tables.Add("local " + name + " = { " + string.Concat(items.Select((t, i) => "[" + i.ToString(CultureInfo.InvariantCulture) + "] = " + t.Number.ToString(CultureInfo.InvariantCulture) + ", "))
                            + "length = " + items.Count.ToString(CultureInfo.InvariantCulture) + " } -- " + q.Kind + "\n");
                value = name;
                if (els.Rows is { } rows)
                {
                    // as long as the rows shown when it is looked up, as a browser's list is
                    _rowsList = true;
                    value = "v_rows(" + name + ", " + rows.Prefix.ToString(CultureInfo.InvariantCulture) + ", " + rows.Rep.Var + ")";
                }
                else if (els.Shown)
                {
                    // the elements markup shows when it is looked up: each one's own and its holders' visibility slots, read
                    _shownList = true;
                    var vis = items.Select((t, i) => (i, Slots: ShownBy(t))).Where(x => x.Slots.Count > 0)
                        .Select(x => "[" + x.i.ToString(CultureInfo.InvariantCulture) + "] = { " + string.Join(", ", x.Slots.Select(Q)) + " }");
                    _tables.Add("local " + name + "s = { length = 0 } -- the members shown\n");
                    _tables.Add("local " + name + "v = { " + string.Join(", ", vis) + " } -- what shows each\n");
                    value = "v_shown(" + name + "s, " + name + ", " + name + "v)";
                }
            }
            else value = els.One ? els.Ts[0].Number.ToString(CultureInfo.InvariantCulture) : "nil";
            return _domValue[c] = value;
        }

        private readonly Dictionary<CallExpression, string> _domValue = new();

        private string HoleLua(Func<Node, string> tr, Piece hole)
        {
            // null as written prints as the word; Lua's nil would be undefined
            if (Unwrap(hole.Hole!) is NullLiteral) return Q("null");
            var v = tr(hole.Hole!);
            if (hole.Digits is { } n)
            {
                _fixed = true;
                return "v_fixed(" + v + ", " + n.ToString(CultureInfo.InvariantCulture) + ")";
            }
            var inner = Unwrap(hole.Hole!);
            if (Boolean(inner) || _bool.Contains(inner)) return "js_str(" + v + ")";
            // a read that can be null prints "null", as JavaScript's string conversion does
            if (_null.TryGetValue(inner, out var none)) return "(" + v + " or " + Q(none) + ")";
            if (Printable(inner)) return v;
            // the scene reads a number or a string; anything else (a boolean, undefined, an array) is sent as JavaScript prints it
            _txt = true;
            return "v_txt(" + v + ")";
        }

        private bool _txt;

        /// <summary>Whether a value is a number or a string in every run, which the scene takes as it is.</summary>
        private static bool Printable(Expression e) => e switch
        {
            NumericLiteral or StringLiteral or TemplateLiteral or UpdateExpression => true,
            NonUpdateUnaryExpression { Operator: Operator.UnaryNegation or Operator.UnaryPlus or Operator.BitwiseNot or Operator.TypeOf } => true,
            NonLogicalBinaryExpression b => b.Operator is Operator.Subtraction or Operator.Multiplication or Operator.Division
                or Operator.Remainder or Operator.Exponentiation
                || b.Operator == Operator.Addition && Printable(b.Left) && Printable(b.Right),
            AssignmentExpression { Operator: not Operator.Assignment and not Operator.NullishCoalescingAssignment
                and not Operator.LogicalAndAssignment and not Operator.LogicalOrAssignment } => true,
            CallExpression { Callee: MemberExpression { Computed: false, Object: Identifier { Name: "Math" } } } => true,
            CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toFixed" or "toString" or "toUpperCase"
                or "toLowerCase" or "trim" or "padStart" or "padEnd" or "join" or "repeat" or "charAt" or "substring" } } } => true,
            ConditionalExpression c => Printable(c.Consequent) && Printable(c.Alternate),
            _ => false,
        };

        /// <summary>Whether the program rounds a toFixed value, so the chunk carries v_fixed.</summary>
        private bool _fixed;

        private static string Affine(string v, double a, double b)
        {
            var s = a == 1 ? v : "(" + v + ") * " + JsToLuaNumber(a);
            return b == 0 ? s : s + (b < 0 ? " - " + JsToLuaNumber(-b) : " + " + JsToLuaNumber(b));
        }

        private static string Q(string s) => JsToLua.Quote(s);

        /// <summary>
        /// The chunk: the page's values, the few functions that move them, the page's own script, its two
        /// vector elements, and its tick chained in front of the author's.
        /// </summary>
        public string Assemble(string scene, string page, (string Surface, string Element, string Scene)? target)
        {
            var (surface, element, sceneId) = target ?? ("main", "page", "html:page");
            var listens = _order.Any(t => t.Listens);
            var classes = _order.Where(t => t.Class != null).ToList();
            var states = _order.SelectMany(t => t.StylePlans.Values).Where(p => p.States != null)
                .Concat(_markupOf.Values.SelectMany(m => m.Parts).Select(m => m.Plan).OfType<StylePlan>().Distinct()).ToList();

            var sb = new StringBuilder(scene.Length + page.Length + 4096);
            sb.Append("-- Compiled once by ScriptedScreens Html. The scene is the page; this program is its script,\n");
            sb.Append("-- moving the scene's values from the chip's own tick and the scene's clicks.\n");
            // PageCompiler.Retarget finds this line by its exact text, to point a shared compile at one console.
            sb.Append("local SURFACE, ELEMENT, SCENE = ").Append(QuoteTarget(surface)).Append(", ").Append(QuoteTarget(element))
              .Append(", ").Append(QuoteTarget(sceneId)).Append("\n\n");
            sb.Append("local ui = ss.ui.surface(SURFACE)\n");
            // Fields of the chunk's own environment, which the host reaches: V_LIVE goes false when the
            // page is replaced (the program then does nothing), and V_AUTHOR is the tick this one runs
            // after - the author's, or another page's chained before it (ChipHost.ChainTick).
            sb.Append("V_LIVE = true\n");
            sb.Append("V_AUTHOR = tick\n");
            sb.Append("local V_D = ").Append(Table(Written.Select(s => (s, Value(Opening[s]))))).Append('\n');
            // what the data element holds, as last sent: a value set and set back before the send is not sent
            sb.Append("local V_F = ").Append(Table(Written.Select(s => (s, Value(Opening[s]))))).Append('\n');
            sb.Append("local V_EASE = ").Append(Table(Written.Where(_ease.ContainsKey).Select(s => (s, _ease[s])))).Append('\n');
            sb.Append("local V_P, V_E = {}, {}\n");
            sb.Append("local V_SEND = { data = V_P, ease = V_E }\n");
            sb.Append("local V_DATA\n");
            sb.Append("local function v_set(k, v)\n  if V_D[k] == v then return end\n  V_D[k] = v\n");
            sb.Append("  if V_F[k] ~= v then V_P[k] = v V_E[k] = V_EASE[k] else V_P[k] = nil V_E[k] = nil end\nend\n");
            sb.Append("local function v_flush()\n  if next(V_P) == nil then return end\n  V_DATA:set_props(V_SEND)\n  ui:commit()\n");
            sb.Append("  for k, v in pairs(V_P) do V_F[k] = v V_P[k] = nil end\n  for k in pairs(V_E) do V_E[k] = nil end\nend\n");
            if (_reload)
            {
                sb.Append("-- location.reload(): the page starts again from its source once the script that asked has run\n");
                sb.Append("local V_D0 = ").Append(Table(Written.Select(s => (s, Value(Opening[s]))))).Append('\n');
                sb.Append("local V_RELOAD = false\nlocal v_main, v_reload\n");
                sb.Append("local function v_askreload() V_RELOAD = true end\n");
                sb.Append("local function v_reset(t, t0)\n  for k in pairs(t) do t[k] = nil end\n  for k, v in pairs(t0) do t[k] = v end\nend\n");
            }

            if (states.Count > 0 || _reps.Any(r => r.RowPlans.Count > 0 || r.GatePlans.Count > 0))
            {
                sb.Append("local function v_state(s, value)\n  local st = s[value]\n  if st == nil then return end\n");
                sb.Append("  for k, v in pairs(st) do v_set(k, v) end\nend\n");
                foreach (var plan in states)
                {
                    sb.Append("local ").Append(plan.Var).Append(" = {");
                    foreach (var pair in plan.States!)
                        sb.Append("\n  [").Append(pair.Key is double d ? JsToLuaNumber(d) : Q((string)pair.Key)).Append("] = ")
                          .Append(Table(pair.Value.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, Value(x.Value))))).Append(',');
                    sb.Append("\n}\n");
                }
            }
            if (classes.Count > 0)
            {
                sb.Append("local function v_class(c)\n  local key, bit, names, on = 0, 1, c.names, c.on\n");
                sb.Append("  for i = 1, #names do if on[names[i]] then key = key + bit end bit = bit + bit end\n");
                if (classes.Any(t => t.Class!.Attrs.Count > 0))
                {
                    // each attribute CSS selects on: the index of the value it holds, absent 0
                    sb.Append("  local attrs, at = c.attrs, c.at\n  if attrs then\n    for i = 1, #attrs do\n");
                    sb.Append("      local a = attrs[i]\n      local v = at[a.name]\n      if v ~= nil then key = key + bit * (a.index[v] or 0) end\n");
                    sb.Append("      bit = bit * a.n\n    end\n  end\n");
                }
                sb.Append("  for k, v in pairs(c.states[key]) do v_set(k, v) end\nend\n");
                if (classes.Any(t => t.Class!.Order != null))
                {
                    // className is read back, so the class attribute's order is kept as a browser keeps it
                    sb.Append("local function v_classop(c, name, want)\n  local on, order = c.on, c.order\n  c.raw = nil\n");
                    sb.Append("  if want and not on[name] then order[#order + 1] = name\n");
                    sb.Append("  elseif not want and on[name] then\n    for i = 1, #order do if order[i] == name then table.remove(order, i) break end end\n  end\n");
                    sb.Append("  on[name] = want\nend\n");
                }
                if (classes.Any(t => t.Class!.Sets.Count > 0))
                {
                    sb.Append("local function v_classname(c, value)\n  local want = c.sets[value]\n  if want == nil then return end\n");
                    sb.Append("  for i = 1, #c.names do c.on[c.names[i]] = want[c.names[i]] == true end\n");
                    if (classes.Any(t => t.Class!.Order != null))
                    {
                        sb.Append("  if c.order then\n    local list, order = c.lists[value], c.order\n");
                        sb.Append("    for i = #order, 1, -1 do order[i] = nil end\n    for i = 1, #list do order[i] = list[i] end\n    c.raw = value\n  end\n");
                    }
                    sb.Append("  v_class(c)\nend\n");
                }
                foreach (var t in classes)
                {
                    var plan = t.Class!;
                    var on = Table(plan.Fixed.Select(n => (n, "true")).Concat(plan.Names.Select(n => (n, plan.Initial[n] ? "true" : "false"))));
                    sb.Append("-- the classes").Append(plan.Attrs.Count > 0 ? " and attributes" : string.Empty).Append(" of \"").Append(t.Name)
                      .Append("\" the script changes, and what each combination draws\n");
                    sb.Append("local ").Append(plan.Var).Append(" = {\n");
                    sb.Append("  on = ").Append(on).Append(",\n");
                    sb.Append("  names = { ").Append(string.Join(", ", plan.Names.Select(Q))).Append(" },\n");
                    if (plan.Attrs.Count > 0)
                    {
                        sb.Append("  attrs = {");
                        foreach (var (name, options, index) in plan.Attrs)
                            sb.Append("\n    { name = ").Append(Q(name)).Append(", n = ").Append(options.Count.ToString(CultureInfo.InvariantCulture))
                              .Append(", index = { ").Append(string.Join(", ", index.Where(x => x.Value > 0).Select(x => "[" + (x.Key is double d ? JsToLuaNumber(d) : Q((string)x.Key)) + "] = " + x.Value.ToString(CultureInfo.InvariantCulture))))
                              .Append(" } },");
                        sb.Append("\n  },\n");
                    }
                    if (t.AttrTable == plan.Var + ".at")
                    {
                        sb.Append("  at = ").Append(AttrInitial(t)).Append(",\n");
                        if (_reload) sb.Append("  at0 = ").Append(AttrInitial(t)).Append(",\n");
                    }
                    if (_reload) sb.Append("  on0 = ").Append(on).Append(",\n");
                    if (plan.Sets.Count > 0)
                    {
                        sb.Append("  sets = {");
                        foreach (var pair in plan.Sets)
                            sb.Append("\n    [").Append(Q(pair.Key)).Append("] = ").Append(Table(pair.Value.Select(n => (n, "true")))).Append(',');
                        sb.Append("\n  },\n");
                    }
                    if (plan.Order != null)
                    {
                        var order = "{ " + string.Join(", ", plan.Order.Select(Q)) + " }";
                        sb.Append("  order = ").Append(order).Append(", raw = ").Append(Q(plan.Raw!)).Append(",\n");
                        if (_reload) sb.Append("  order0 = ").Append(order).Append(", raw0 = ").Append(Q(plan.Raw!)).Append(",\n");
                        if (plan.Sets.Count > 0)
                        {
                            sb.Append("  lists = {");
                            foreach (var pair in plan.Sets)
                                sb.Append("\n    [").Append(Q(pair.Key)).Append("] = { ")
                                  .Append(string.Join(", ", pair.Key.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Distinct().Select(Q))).Append(" },");
                            sb.Append("\n  },\n");
                        }
                    }
                    sb.Append("  states = {\n");
                    foreach (var pair in plan.States)
                        sb.Append("    [").Append(pair.Key).Append("] = ")
                          .Append(Table(pair.Value.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, Value(x.Value))))).Append(",\n");
                    sb.Append("  },\n}\n");
                }
            }
            sb.Append(Lists());
            var browser = Browser();
            sb.Append(browser);
            if (_timers)
            {
                sb.Append("-- the page's timers, run from the chip's tick in the order they fall due (times in ms)\n");
                sb.Append("local V_CLOCK, V_NOW, V_LAST, V_SLOTS = 0, 0, 0, 0\n");
                sb.Append("local V_FN, V_DUE, V_EVERY, V_ID = {}, {}, {}, {}\n");
                sb.Append("local function v_timer(fn, ms, every)\n");
                sb.Append("  ms = tonumber(ms or 0) or 0\n  if ms ~= ms or ms < 0 then ms = 0 end\n  if every and ms < 4 then ms = 4 end\n");
                sb.Append("  local s = 1\n  while V_FN[s] ~= nil do s = s + 1 end\n  if s > V_SLOTS then V_SLOTS = s end\n");
                sb.Append("  V_LAST = V_LAST + 1\n  V_FN[s], V_DUE[s], V_EVERY[s], V_ID[s] = fn, V_NOW + ms, every and ms or false, V_LAST\n  return V_LAST\nend\n");
                sb.Append("local function v_clear(h)\n  for s = 1, V_SLOTS do if V_ID[s] == h then V_FN[s] = nil V_ID[s] = nil end end\nend\n");
                sb.Append("local function v_run()\n");
                sb.Append("  for _ = 1, 1000 do -- ponytail: an interval that cannot keep up is cut off at 1000 runs a tick\n");
                sb.Append("    local best\n    for s = 1, V_SLOTS do\n");
                sb.Append("      if V_FN[s] ~= nil and V_DUE[s] <= V_CLOCK and (best == nil or V_DUE[s] < V_DUE[best]\n");
                sb.Append("         or V_DUE[s] == V_DUE[best] and V_ID[s] < V_ID[best]) then best = s end\n    end\n");
                sb.Append("    if best == nil then break end\n    local fn = V_FN[best]\n    V_NOW = V_DUE[best]\n");
                sb.Append("    if V_EVERY[best] then V_DUE[best] = V_DUE[best] + V_EVERY[best] else V_FN[best] = nil V_ID[best] = nil end\n");
                sb.Append("    fn()\n").Append(_reload ? "    if V_RELOAD then break end\n" : string.Empty).Append("  end\n  V_NOW = V_CLOCK\nend\n");
            }
            if (listens)
            {
                sb.Append("-- click listeners per element, and which elements a click on each hit region reaches, innermost first\n");
                sb.Append("local V_ON = ").Append(Table(_order.Where(t => t.Listens).Select(t => (t.Name, "{}")))).Append('\n');
                sb.Append("local V_ONCLICK = {}\n");
                sb.Append("local V_CHAIN = ").Append(Table(_chains)).Append('\n');
                sb.Append("local function v_listen(key, fn)\n  local list = V_ON[key]\n  for i = 1, #list do if list[i] == fn then return end end\n");
                sb.Append("  list[#list + 1] = fn\nend\n");
                if (_onclick)
                {
                    // as a browser keeps an event handler: among the listeners from when it is first set, out when set to null
                    sb.Append("-- el.onclick = f: the handler takes its place among the element's listeners when it is first set\n");
                    sb.Append("local V_HANDLER = {}\n");
                    sb.Append("local function v_onclick(key, fn)\n  local list = V_ON[key]\n  if type(fn) ~= \"function\" then fn = nil end\n");
                    sb.Append("  if fn == nil and V_ONCLICK[key] ~= nil then\n    for i = 1, #list do if list[i] == V_HANDLER then table.remove(list, i) break end end\n");
                    sb.Append("  elseif fn ~= nil and V_ONCLICK[key] == nil then list[#list + 1] = V_HANDLER end\n  V_ONCLICK[key] = fn\nend\n");
                }
                if (_madeBy.Keys.Any(t => t.Listens))
                    sb.Append("-- markup written again makes new elements: what listened on the old ones is gone\n")
                      .Append("local function v_unlisten(key)\n  local list = V_ON[key]\n  for i = #list, 1, -1 do list[i] = nil end\n  V_ONCLICK[key] = nil\nend\n");
                if (_events)
                {
                    sb.Append("-- the click event a listener is handed: the element the click landed on and the one listening, by number (one table, reused)\n");
                    sb.Append("local V_EV = {}\n");
                    sb.Append("local V_NUM = ").Append(Table(_order.Where(t => t.Hit || t.Listens).Select(t => (t.Name, t.Number.ToString(CultureInfo.InvariantCulture))))).Append('\n');
                }
                var ev = _events ? "V_EV" : string.Empty;
                sb.Append("local function v_click(nodeId)\n  local chain = V_LIVE and V_CHAIN[nodeId]\n  if not chain then return end\n");
                if (_events) sb.Append("  V_EV.target = V_NUM[nodeId]\n");
                sb.Append("  for c = 1, #chain do\n");
                if (_events) sb.Append("    V_EV.currentTarget = V_NUM[chain[c]]\n");
                if (_onclick)
                    sb.Append("    local list = V_ON[chain[c]]\n    for i = 1, #list do\n      local f = list[i]\n")
                      .Append("      if f == V_HANDLER then f = V_ONCLICK[chain[c]] end\n      if f then f(").Append(ev).Append(") end\n    end\n  end\n");
                else sb.Append("    local list = V_ON[chain[c]]\n    for i = 1, #list do list[i](").Append(ev).Append(") end\n  end\n");
                if (_reload) sb.Append("  if V_RELOAD then v_reload() end\n");
                sb.Append("  v_flush()\nend\n");
            }

            var reads = Reading();
            var tables = string.Concat(_tables);
            var helpers = Helpers(page + browser + reads + tables + (_fixed ? " NumberMethods.toFixed" : string.Empty));
            if (helpers.Length > 0) sb.Append("-- the JavaScript behaviours the script relies on\n").Append(helpers);
            sb.Append(reads);
            if (_fixed)
            {
                // The scene prints `%.nf` with .NET's rounding, which takes an exact half to even
                // (20.25 is "20.2"); JavaScript's toFixed takes it up ("20.3"). So the number is rounded
                // here as toFixed rounds it, and only the rare exact half pays for the prelude's own.
                sb.Append("local function v_fixed(v, n)\n  if type(v) ~= \"number\" then return v end\n");
                sb.Append("  local p = 10 ^ n\n  local s = math.abs(v) * p\n  local r = math.floor(s + 0.5)\n");
                sb.Append("  if r - s == 0.5 then return tonumber(NumberMethods.toFixed(v, n)) end\n");
                sb.Append("  return (v < 0 and -r or r) / p\nend\n");
            }
            if (tables.Length > 0) sb.Append("-- elements the script holds as values, by number: its lists, its lookups by id, and each operation on one chosen at run time\n").Append(tables);

            if (_reload)
            {
                sb.Append(Reload());
                sb.Append("\n-- the page's script, which a reload runs again\nv_main = function()\n").Append(page).Append("end\nv_main()\n\n");
            }
            else sb.Append("\n-- the page's script\ndo\n").Append(page).Append("end\n\n");

            // A long bracket no line of the scene closes: `stops=[[0,#fff],[1,#000]]` ends in `]]`.
            var level = "==";
            while (scene.Contains("]" + level + "]", StringComparison.Ordinal)) level += "=";
            sb.Append("ui:element({ id = ELEMENT .. \"").Append(SceneSuffix).Append("\", type = \"vector\", rect = ui:get(ELEMENT).rect,\n");
            sb.Append("  props = { scene = SCENE, src = [").Append(level).Append("[\n").Append(scene.TrimEnd('\n'))
              .Append("\n]").Append(level).Append("] }").Append(listens ? ", on_click = v_click" : string.Empty).Append(" })\n");
            sb.Append("V_DATA = ui:element({ id = ELEMENT .. \"").Append(DataSuffix).Append("\", type = \"vector\",\n");
            sb.Append("  rect = { unit = \"px\", x = -4, y = -4, w = 1, h = 1 }, props = { scene = SCENE, keep = 1, data = V_D } })\n");
            sb.Append("ui:commit()\n");
            sb.Append("for k, v in pairs(V_D) do V_F[k] = v end\nfor k in pairs(V_P) do V_P[k] = nil end\nfor k in pairs(V_E) do V_E[k] = nil end\n");
            if (_timers || _reload)
            {
                sb.Append("\nfunction tick(dt)\n  if V_LIVE then\n");
                if (_timers) sb.Append("    V_CLOCK = V_CLOCK + dt * 1000\n    v_run()\n");
                if (_reload) sb.Append("    if V_RELOAD then v_reload() end\n");
                sb.Append("    v_flush()\n  end\n");
                sb.Append("  if V_AUTHOR then return V_AUTHOR(dt) end\nend\n");
            }
            return sb.ToString();
        }

        /// <summary>The browser's pieces the program uses: attributes, the style values it reads back, location, storage, console.</summary>
        private string Browser()
        {
            var sb = new StringBuilder();
            if (_txt)
            {
                sb.Append("-- a value written into a text: a number or a string as it is, anything else as JavaScript prints it\n");
                sb.Append("local function v_txt(v)\n  local t = type(v)\n  if t == \"number\" or t == \"string\" then return v end\n  return js_str(v)\nend\n");
            }
            if (_attrs)
            {
                sb.Append("-- attributes, as the browser stores them: a string, or nil for absent\n");
                sb.Append("local function v_attr(v)\n  if v == nil or type(v) == \"string\" then return v end\n  return js_str(v)\nend\n");
                sb.Append("-- one read straight into a number (Number(el.getAttribute(…))): a number written there is that number, no text made for it\n");
                sb.Append("local function v_attrnum(v)\n  if type(v) == \"number\" then return v end\n  return v_attr(v)\nend\n");
                sb.Append("local function v_setattr(at, name, v, c)\n  if v == nil then v = \"undefined\" elseif type(v) == \"table\" then v = js_str(v) end\n");
                sb.Append("  at[name] = v\n  if c then v_class(c) end\nend\n");
                sb.Append("local function v_rmattr(at, name, c)\n  at[name] = nil\n  if c then v_class(c) end\n  return true\nend\n");
                sb.Append("local function v_toggleattr(at, name, force, c)\n  local on\n  if force == nil then on = at[name] == nil else on = js_truthy(force) end\n");
                sb.Append("  if on then if at[name] == nil then at[name] = \"\" end else at[name] = nil end\n  if c then v_class(c) end\n  return on\nend\n");
                sb.Append("local function v_hidden(at, v, c)\n  if js_truthy(v) then at.hidden = \"\" else at.hidden = nil end\n  if c then v_class(c) end\n  return v\nend\n");
                foreach (var t in _order.Where(t => t.AttrTable != null && t.AttrTable.StartsWith("V_AT", StringComparison.Ordinal)))
                {
                    sb.Append("local ").Append(t.AttrTable).Append(" = ").Append(AttrInitial(t)).Append(" -- the attributes of \"").Append(t.Name).Append("\"\n");
                    if (_reload) sb.Append("local ").Append(t.AttrTable).Append("0 = ").Append(AttrInitial(t)).Append('\n');
                }
            }
            foreach (var plan in _order.SelectMany(t => t.StylePlans.Values).Where(p => p.ReadVar != null))
            {
                sb.Append("local ").Append(plan.ReadVar).Append(" = ").Append(plan.ReadInitial ?? "nil").Append(" -- a style value as written, for reading back\n");
                if (plan.Printed != null)
                    sb.Append("local ").Append(plan.ReadVar).Append("P = { ")
                      .Append(string.Join(", ", plan.Printed.Select(p => "[" + (p.Key is double d ? JsToLuaNumber(d) : Q((string)p.Key)) + "] = " + Q(p.Value))))
                      .Append(" }\n");
            }
            if (_hashWrites)
            {
                sb.Append("-- location: about:blank, and the fragment the page last navigated to\n");
                sb.Append("local V_HASH, V_HREF = \"\", \"about:blank\"\n");
                sb.Append("local V_PCT = { [\" \"] = \"%20\", [\"\\\"\"] = \"%22\", [\"<\"] = \"%3C\", [\">\"] = \"%3E\", [\"`\"] = \"%60\", [\"\\127\"] = \"%7F\" }\n");
                sb.Append("for i = 0, 31 do V_PCT[string.char(i)] = string.format(\"%%%02X\", i) end\n");
                sb.Append("local function v_sethash(v)\n  local f = js_str(v)\n  if string.sub(f, 1, 1) == \"#\" then f = string.sub(f, 2) end\n");
                sb.Append("  f = string.gsub(f, \"[%c \\\"<>`]\", V_PCT)\n");
                sb.Append("  V_HASH = f == \"\" and \"\" or \"#\" .. f\n  V_HREF = \"about:blank#\" .. f\n  return v\nend\n");
            }
            if (_localStorage || _sessionStorage)
            {
                sb.Append("-- Storage: values by key and the keys in order; localStorage kept in the chip's own store (ic.persist)\n");
                sb.Append("local function v_storage(persist, list)\n  local s = { vals = {}, keys = {}, pos = {}, pk = {}, n = 0, persist = persist, list = list }\n");
                sb.Append("  local ok, raw = false, nil\n  if persist then ok, raw = pcall(persist.get, list) end\n");
                sb.Append("  if ok and type(raw) == \"string\" then\n    local i = 1\n    while i <= #raw do\n");
                sb.Append("      local colon = string.find(raw, \":\", i, true)\n      if colon == nil then break end\n");
                sb.Append("      local len = tonumber(string.sub(raw, i, colon - 1)) or 0\n      local k = string.sub(raw, colon + 1, colon + len)\n      i = colon + len + 1\n");
                sb.Append("      local okv, v = pcall(persist.get, list .. \":\" .. k)\n");
                sb.Append("      if okv and type(v) == \"string\" and s.pos[k] == nil then\n        s.n = s.n + 1 s.keys[s.n] = k s.pos[k] = s.n s.vals[k] = v\n      end\n");
                sb.Append("    end\n  end\n  return s\nend\n");
                sb.Append("local function v_quota()\n  error({ name = \"QuotaExceededError\", message = \"The quota has been exceeded.\", stack = \"\", __error = true }, 0)\nend\n");
                sb.Append("local function v_skey(s, k)\n  local p = s.pk[k]\n  if p == nil then p = s.list .. \":\" .. k s.pk[k] = p end\n  return p\nend\n");
                sb.Append("local function v_slist(s)\n  local parts = {}\n  for i = 1, s.n do parts[i] = #s.keys[i] .. \":\" .. s.keys[i] end\n");
                sb.Append("  local ok, done = pcall(s.persist.set, s.list, table.concat(parts))\n  return ok and done ~= false\nend\n");
                sb.Append("local function v_sset(s, k, v)\n  local was = s.vals[k]\n  if was == v then return end\n");
                sb.Append("  if s.persist then\n    local ok, done = pcall(s.persist.set, v_skey(s, k), v)\n    if not ok or done == false then v_quota() end\n  end\n");
                sb.Append("  s.vals[k] = v\n  if was ~= nil then return end\n  s.n = s.n + 1 s.keys[s.n] = k s.pos[k] = s.n\n");
                sb.Append("  if s.persist and not v_slist(s) then\n    s.keys[s.n] = nil s.pos[k] = nil s.vals[k] = nil s.n = s.n - 1\n");
                sb.Append("    pcall(s.persist.delete, v_skey(s, k))\n    v_quota()\n  end\nend\n");
                sb.Append("local function v_srm(s, k)\n  local p = s.pos[k]\n  if p == nil then return end\n  table.remove(s.keys, p)\n");
                sb.Append("  s.n = s.n - 1\n  for i = p, s.n do s.pos[s.keys[i]] = i end\n  s.pos[k] = nil s.vals[k] = nil\n");
                sb.Append("  if s.persist then pcall(s.persist.delete, v_skey(s, k)) v_slist(s) end\nend\n");
                sb.Append("local function v_sclear(s)\n  if s.n == 0 then return end\n");
                sb.Append("  for i = s.n, 1, -1 do\n    local k = s.keys[i]\n    if s.persist then pcall(s.persist.delete, v_skey(s, k)) end\n");
                sb.Append("    s.keys[i] = nil s.pos[k] = nil s.vals[k] = nil\n  end\n  s.n = 0\n  if s.persist then pcall(s.persist.delete, s.list) end\nend\n");
                sb.Append("local function v_skeyat(s, i)\n  i = tonumber(i) or 0\n  if i ~= i then i = 0 end\n  return s.keys[math.floor(i) + 1]\nend\n");
                if (_localStorage) sb.Append("local V_LS = v_storage(ic and ic.persist, \"html.ls\")\n");
                if (_sessionStorage) sb.Append("local V_SS = v_storage(nil, \"html.ss\")\n");
            }
            if (_void) sb.Append("-- the console's methods: a chip has no console\nlocal function v_void() end\n");
            foreach (var line in _consts) sb.Append(line).Append('\n');
            return sb.ToString();
        }

        /// <summary>How the program prints what it reads back, after the prelude it prints with.</summary>
        private string Reading()
        {
            var sb = new StringBuilder();
            if (_toFixedRead) sb.Append("local function v_tofixed(v, n)\n  if type(v) ~= \"number\" then return v end\n  return NumberMethods.toFixed(v, n)\nend\n");
            if (_untmp) sb.Append("local function v_untmp(v)\n  if type(v) ~= \"string\" then return js_str(v) end\n  return (string.gsub(v, \"<noparse><</noparse>\", \"<\"))\nend\n");
            if (_cssNum) sb.Append("local function v_cssnum(v, unit)\n  if v == nil then return \"\" end\n  return string.format(\"%.6g\", v) .. unit\nend\n");
            if (_nullref)
                sb.Append("-- an element chosen at run time that is not there: the browser's TypeError on null\n")
                  .Append("local function v_nullref()\n  error({ name = \"TypeError\", message = \"Cannot read properties of null\", stack = \"\", __error = true }, 0)\nend\n");
            if (_item) sb.Append("-- a list's item(i): i as a whole number, null past the end\nlocal function v_item(l, i)\n  i = js_num(i)\n  if i ~= i then i = 0 end\n  return l[math.floor(i)]\nend\n");
            if (_clsItem) sb.Append("local function v_clsitem(order, i)\n  i = js_num(i)\n  if i ~= i then i = 0 end\n  return order[math.floor(i) + 1]\nend\n");
            if (_clsCount) sb.Append("local function v_clscount(on)\n  local n = 0\n  for _, v in pairs(on) do if v then n = n + 1 end end\n  return n\nend\n");
            if (_collapse)
                sb.Append("-- text as laid out: each run of white space one space, a block's ends trimmed\n")
                  .Append("local function v_collapse(s, block)\n  s = string.gsub(js_str(s), \"[ \\t\\n\\r\\f]+\", \" \")\n")
                  .Append("  if block then s = string.gsub(s, \"^ \", \"\") s = string.gsub(s, \" $\", \"\") end\n  return s\nend\n");
            if (_nadd)
                sb.Append("-- `+` with a read that gives null: \"null\" in a text, 0 in a sum\n")
                  .Append("local function v_nadd(a, b, an, bn)\n  if type(a) == \"string\" or type(b) == \"string\" then\n")
                  .Append("    if a == nil and an then a = \"null\" end\n    if b == nil and bn then b = \"null\" end\n    return js_str(a) .. js_str(b)\n  end\n")
                  .Append("  return js_add(a, b)\nend\n");
            return sb.ToString();
        }

        /// <summary>location.reload(): everything the page's script set up, back to how the page's source has it, and the script run again.</summary>
        private string Reload()
        {
            var sb = new StringBuilder("v_reload = function()\n  V_RELOAD = false\n");
            if (_timers) sb.Append("  for s = 1, V_SLOTS do V_FN[s] = nil V_ID[s] = nil end\n");
            if (_order.Any(t => t.Listens))
                sb.Append("  for _, list in pairs(V_ON) do for i = #list, 1, -1 do list[i] = nil end end\n  for k in pairs(V_ONCLICK) do V_ONCLICK[k] = nil end\n");
            foreach (var t in _order.Where(t => t.Class != null))
            {
                var c = t.Class!.Var;
                sb.Append("  v_reset(").Append(c).Append(".on, ").Append(c).Append(".on0)\n");
                if (t.AttrTable == c + ".at") sb.Append("  v_reset(").Append(c).Append(".at, ").Append(c).Append(".at0)\n");
                if (t.Class.Order != null) sb.Append("  v_reset(").Append(c).Append(".order, ").Append(c).Append(".order0) ").Append(c).Append(".raw = ").Append(c).Append(".raw0\n");
            }
            foreach (var t in _order.Where(t => t.AttrTable != null && t.AttrTable.StartsWith("V_AT", StringComparison.Ordinal)))
                sb.Append("  v_reset(").Append(t.AttrTable).Append(", ").Append(t.AttrTable).Append("0)\n");
            foreach (var plan in _order.SelectMany(t => t.StylePlans.Values).Where(p => p.ReadVar != null))
                sb.Append("  ").Append(plan.ReadVar).Append(" = ").Append(plan.ReadInitial ?? "nil").Append('\n');
            sb.Append("  for k, v in pairs(V_D0) do v_set(k, v) end\n  v_main()\nend\n");
            return sb.ToString();
        }

        /// <summary>
        /// Every hit region's node id, and the elements with click listeners a click on it reaches:
        /// itself and its ancestors, innermost first, as a click bubbles.
        /// </summary>
        private IEnumerable<(string, string)> Chains()
        {
            var listening = _order.Where(t => t.Listens).ToDictionary(t => t.Ve, t => t.Name);
            foreach (var pair in _built.NodeOf)
            {
                if (!Clickable(pair.Value) || string.IsNullOrEmpty(pair.Key.name)) continue;
                var chain = new List<string>();
                for (var ve = pair.Key; ve != null; ve = ve.parent)
                    if (listening.TryGetValue(ve, out var name)) chain.Add(name);
                if (chain.Count > 0) yield return (pair.Key.name, "{ " + string.Join(", ", chain.Select(Q)) + " }");
            }
        }

        /// <summary>
        /// A number's glide as the renderer's own `ease` entry: the transition its element declares for the
        /// property behind the slot, or 0 - a snap, as a browser changes a value with no transition at once.
        /// ponytail: the transition is read from the element at rest, not from the state it moves to.
        /// </summary>
        private string Ease(string slot)
        {
            foreach (var (suffix, property) in DataSlots.SlotProperty)
            {
                if (!slot.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var name = slot.Substring(0, slot.Length - suffix.Length);
                if (name.EndsWith(SceneSlots.SecondSuffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - SceneSlots.SecondSuffix.Length);
                foreach (var pair in _built.ById)
                    if (DomSlots.Slot(pair.Key) == name && pair.Value != null
                        && CssTransition.For(_built.CssOf(pair.Value), property) is { Dur: > 0f } t)
                        return "{ " + Num(t.Dur) + ", " + Q(t.Curve) + (t.Delay > 0f ? ", " + Num(t.Delay) : string.Empty) + " }";
                break;
            }
            return "0";
        }

        private static string Table(IEnumerable<(string Key, string Value)> pairs)
        {
            var list = pairs.ToList();
            return list.Count == 0 ? "{}" : "{ " + string.Join(", ", list.Select(p => Key(p.Key) + " = " + p.Value)) + " }";
        }

        private static string Key(string name) => Keywords.Contains(name) || !Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") ? "[" + Q(name) + "]" : name;

        private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
            "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
        };

        private static string Value(SceneSlots.Value v) => v.IsNumber ? JsToLuaNumber(v.Number) : Q(v.Text ?? string.Empty);

        /// <summary>As PageCompiler.Retarget writes the target line: only `"` and `\` escaped, so its search matches.</summary>
        private static string QuoteTarget(string v) => "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    // ---- the JavaScript behaviours a script relies on ----------------------------------------------

    /// <summary>
    /// The definitions of the runtime prelude (JsPrelude.lua) that the translated script reaches, and
    /// nothing else: its top-level definitions, each with every definition it names, from what the
    /// script names - and for a method it calls through the dispatcher (<c>js_m(x, "toFixed")</c>),
    /// every method table's entry of that name. The prelude's DOM is never named, so never included.
    /// </summary>
    internal static string Helpers(string code)
    {
        var prelude = CompileProbe.Prelude(out _);
        if (prelude == null) return string.Empty;
        var blocks = Blocks(prelude);
        var defined = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < blocks.Count; i++)
            foreach (var name in blocks[i].Defines) defined[name] = i;

        var take = new HashSet<int>();
        var queue = new Queue<string>();
        void Want(string text)
        {
            foreach (var method in Called(text))
                foreach (var pair in defined)
                    if (pair.Key.EndsWith("." + method, StringComparison.Ordinal)) queue.Enqueue(pair.Key);
            var code = Strip(text);
            // a name the code declares for itself is its own, not the prelude's of the same spelling
            var own = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match d in Locals.Matches(code))
                foreach (var g in new[] { d.Groups[1], d.Groups[2], d.Groups[3] })
                    if (g.Success) foreach (var n in g.Value.Split(',')) own.Add(n.Trim());
            foreach (Match m in Names.Matches(code))
            {
                if (own.Contains(m.Value)) continue;
                queue.Enqueue(m.Value);
                if (m.Value.IndexOf('.') is var dot and > 0) queue.Enqueue(m.Value.Substring(0, dot));
            }
        }
        Want(code);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!defined.TryGetValue(name, out var i) || !take.Add(i)) continue;
            // a method's table comes with it
            var dot = name.IndexOf('.');
            if (dot > 0) queue.Enqueue(name.Substring(0, dot));
            Want(blocks[i].Text);
        }
        // Statements that only set up something taken (a metatable, a table filled by a loop) come with it.
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Defines.Count > 0) continue;
            var uses = Names.Matches(blocks[i].Text).Select(m => m.Value).Where(defined.ContainsKey).Distinct().ToList();
            if (uses.Count > 0 && uses.All(u => take.Contains(defined[u]))) take.Add(i);
        }
        var sb = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++) if (take.Contains(i)) sb.Append(blocks[i].Text);
        return sb.ToString();
    }

    private static readonly Regex Names = new(@"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?", RegexOptions.Compiled);
    private static readonly Regex Locals = new(@"local\s+function\s+(\w+)|local\s+([\w\s,]+?)\s*(?:=|\n)|function\s*[\w.:]*\s*\(([\w\s,]*)\)", RegexOptions.Compiled);
    /// <summary>
    /// The method names code calls through the dispatcher, <c>js_m(receiver, "name", …)</c>, with the receiver
    /// skipped however it nests: `js_m(js_array({ [0] = 1, 2 }, 2), "forEach", f)` calls forEach.
    /// </summary>
    internal static IEnumerable<string> Called(string code)
    {
        for (var at = code.IndexOf("js_m(", StringComparison.Ordinal); at >= 0; at = code.IndexOf("js_m(", at + 5, StringComparison.Ordinal))
        {
            var depth = 0;
            var i = at + 5;
            for (; i < code.Length; i++)
            {
                var c = code[i];
                if (c is '"' or '\'')
                {
                    // a string: to its closing quote, past escaped ones
                    for (i++; i < code.Length && code[i] != c; i++) if (code[i] == '\\') i++;
                    continue;
                }
                if (c is '(' or '{' or '[') depth++;
                else if (c is ')' or '}' or ']') { if (depth == 0) break; depth--; }
                else if (c == ',' && depth == 0) break;
            }
            if (i < code.Length && code[i] == ',' && MethodName.Match(code, i + 1) is { Success: true } m) yield return m.Groups[1].Value;
        }
    }

    private static readonly Regex MethodName = new(@"\G\s*""([A-Za-z_][A-Za-z0-9_]*)""", RegexOptions.Compiled);
    private static readonly Regex Start = new(
        @"^(?:local\s+function\s+(?<f>[\w.]+)|function\s+(?<f>[\w.]+)|local\s+(?<l>[\w,\s]+?)\s*(?:=|$)|(?<a>[A-Za-z_][\w.]*)(?:\[""(?<k>\w+)""\])?\s*=[^=])",
        RegexOptions.Compiled);

    /// <summary>The prelude as top-level definitions: a block starts at a line in column 0 that defines something or does something.</summary>
    private static List<(List<string> Defines, string Text)> Blocks(string prelude)
    {
        lock (BlockCache)
        {
            if (BlockCache.TryGetValue(prelude, out var cached)) return cached;
            var blocks = new List<(List<string>, string)>();
            List<string>? defines = null;
            var text = new StringBuilder();
            foreach (var raw in prelude.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var top = line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("--", StringComparison.Ordinal)
                          && !line.StartsWith("end", StringComparison.Ordinal) && line[0] is not ('}' or ')' or ']');
                if (top)
                {
                    if (defines != null) blocks.Add((defines, text.ToString()));
                    text.Clear();
                    defines = new List<string>();
                    var m = Start.Match(line);
                    if (m.Groups["f"].Success) defines.Add(m.Groups["f"].Value);
                    else if (m.Groups["l"].Success) defines.AddRange(m.Groups["l"].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
                    else if (m.Groups["a"].Success) defines.Add(m.Groups["a"].Value + (m.Groups["k"].Success ? "." + m.Groups["k"].Value : string.Empty));
                }
                if (defines == null || line.TrimStart().StartsWith("--", StringComparison.Ordinal)) continue;
                text.Append(line).Append('\n');
            }
            if (defines != null) blocks.Add((defines, text.ToString()));
            BlockCache[prelude] = blocks;
            return blocks;
        }
    }

    private static readonly Dictionary<string, List<(List<string>, string)>> BlockCache = new(StringComparer.Ordinal);

    /// <summary>Lua source without its string literals and comments, so a name inside one is not taken for a use.</summary>
    private static string Strip(string lua) => Regex.Replace(lua, @"--[^\n]*|""(?:\\.|[^""\\])*""|\[(=*)\[[\s\S]*?\]\1\]", " ");

    // ---- the scene ---------------------------------------------------------------------------------

    /// <summary>
    /// The scene with every slot the program does not write put back as the literal it was, so what
    /// remains as <c>$name</c> is exactly what moves - as a hand-written scene reads.
    /// </summary>
    internal static string Literal(string template, Dictionary<string, SceneSlots.Value> values, List<string> keep)
    {
        var sb = new StringBuilder(template.Length);
        var quoted = false;
        var expression = false;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '\\' && quoted && i + 1 < template.Length) { sb.Append(c).Append(template[++i]); continue; }
            if (c == '"')
            {
                quoted = !quoted;
                expression = quoted && i + 1 < template.Length && template[i + 1] == '=';
                sb.Append(c);
                continue;
            }
            // a placeholder in a label's text is the program's, not a slot of the template
            if (c == '{' && quoted && i + 1 < template.Length && template[i + 1] == '$')
            {
                var close = template.IndexOf('}', i);
                if (close > 0) { sb.Append(template, i, close - i + 1); i = close; continue; }
            }
            if (c != '$') { sb.Append(c); continue; }
            var end = i + 1;
            while (end < template.Length && (char.IsLetterOrDigit(template[end]) || template[end] == '_')) end++;
            var name = template.Substring(i + 1, end - i - 1);
            if (name.Length == 0 || keep.Contains(name) || !values.TryGetValue(name, out var v))
                sb.Append(template, i, end - i);
            else if (v.IsNumber)
                sb.Append(expression && v.Number < 0f ? "(" + Num(v.Number) + ")" : Num(v.Number));
            else
                sb.Append(quoted ? v.Text!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") : v.Text);
            i = end - 1;
        }
        return sb.ToString();
    }

    /// <summary>A number as the vector mod's placeholder prints it (TextNode's Printf): `%.nf` fixed, `%g` its shortest form.</summary>
    internal static string Print(float v, string format) => Page.Printed(v, format);

    private static string Num(float v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e9f
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("R", CultureInfo.InvariantCulture);
}

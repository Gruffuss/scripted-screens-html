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
/// <b>Lookup.</b> <c>document.getElementById('id')</c>, and a name bound once to one.
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
internal static class PlainTranslator
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
                panel.Layout(size.x, size.y);
            }
        }
        if (refused.Count > 0) return null;

        var hooks = new JsToLua.Hooks { Expression = page.Expression, Statement = page.Statement };
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
    /// build, so it is an AST walk and nothing more.
    /// </summary>
    internal static bool Eligible(HtmlRenderer.Result built)
    {
        try { return Analysed(built, new List<string>()) != null; }
        catch (Exception) { return false; }
    }

    /// <summary>The script parsed and its every DOM use checked against the translated features; null with the reasons otherwise.</summary>
    private static (Page Page, Script Ast)? Analysed(HtmlRenderer.Result built, List<string> refused)
    {
        if (string.IsNullOrWhiteSpace(built.Script)) return null;
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
        foreach (var node in built.NodeOf.Values)
            foreach (var name in node.Attributes.Keys)
                if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && name.Length > 2)
                {
                    refused.Add($"an inline event handler attribute ({name}=) on <{node.Tag}>");
                    return null;
                }
        var page = new Page(built, ast, refused);
        page.Analyse();
        return refused.Count > 0 ? null : (page, ast);
    }

    /// <summary>
    /// The size a page is laid out and compiled for on its console, as a browser window of that size:
    /// the page's own viewport width (a `&lt;meta name=viewport&gt;`), else the console's, and a height
    /// in the console's proportions. The console's size is its rect, and while the game has the screen
    /// switched off - no one in the room - that rect may never have been laid out, so the size the
    /// element was pushed with stands in; the world aspect likewise falls back to the rect's own.
    /// </summary>
    internal static Vector2 ConsoleLayout(float designWidth, Vector2 rect, Vector2 world, Vector2 pushed)
    {
        var w = rect.x >= 1f ? rect.x : pushed.x >= 1f ? pushed.x : 460f;
        var h = rect.y >= 1f ? rect.y : pushed.y >= 1f ? pushed.y : w;
        var width = Mathf.Max(64f, designWidth > 0f ? designWidth : w);
        var aspect = world.x > 1e-5f && world.y > 1e-5f ? world.y / world.x : h / w;
        return new Vector2(width, Mathf.Max(64f, width * aspect));
    }

    // ---- the page: what its script touches, and what that draws ------------------------------------

    /// <summary>One element the script writes to, found at compile time.</summary>
    private sealed class Target
    {
        public readonly VisualElement Ve;
        public readonly HtmlNode Node;
        public string Name;
        public Target(VisualElement ve, HtmlNode node) { Ve = ve; Node = node; Name = ve.name; }
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
        /// <summary>What the script reads back of what it wrote: textContent, className, style.&lt;property&gt;.</summary>
        public bool TextRead, ClassRead;
        public readonly HashSet<string> StyleReads = new(StringComparer.Ordinal);

        public TextPlan? Text;
        public readonly Dictionary<string, StylePlan> StylePlans = new(StringComparer.Ordinal);
        public ClassPlan? Class;
        /// <summary>The Lua table holding this element's attributes, when the script changes or looks them up by a run-time name.</summary>
        public string? AttrTable;

        /// <summary>Whether the script changes what this element draws, so the scene has to name it.</summary>
        public bool Written => Texts.Count > 0 || Styles.Count > 0 || ClassOps.Count > 0 || ClassNames.Count > 0 || Listens || AttrOps.Any(o => o.Facet);
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
    }

    private sealed class TextPlan
    {
        /// <summary>The label's text as the scene prints it, placeholders in it, with the emitter's own tags around.</summary>
        public string Template = string.Empty;
        /// <summary>Per write, what it sets: a slot and a literal, or a slot and the value of a hole.</summary>
        public readonly Dictionary<Expression, List<(string Slot, string? Literal, Piece? Hole)>> Writes = new();
        /// <summary>The text as textContent reads it back: literals, and slots printed as JavaScript prints them.</summary>
        public readonly List<(string? Literal, string? Slot, Piece? Hole)> Read = new();
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

    private sealed class Page
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
        /// <summary>Expressions evaluated for their effect that are DOM operations, and the element they act on.</summary>
        private readonly Dictionary<Expression, Target> _ops = new();
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

        public Page(HtmlRenderer.Result built, Script ast, List<string> refused)
        {
            _built = built;
            _ast = ast;
            _refused = refused;
        }

        private void Refuse(Node at, string what)
        {
            var line = "line " + at.Location.Start.Line.ToString(CultureInfo.InvariantCulture) + ": " + what;
            if (!_refused.Contains(line)) _refused.Add(line);
        }

        // ---- analysis -------------------------------------------------------------------------------

        public void Analyse()
        {
            Link(_ast);
            foreach (var n in Markup.Everything(_ast)) Bind(n);

            foreach (var n in Markup.Everything(_ast))
                if (n is VariableDeclarator { Id: Identifier id, Init: { } init } && Lookup(init, report: false) is { } t
                    && _bindings.TryGetValue(id.Name, out var count) && count == 1 && !_assigned.Contains(id.Name))
                    _vars[id.Name] = t;
            foreach (var n in Markup.Everything(_ast))
                if (n is VariableDeclaration vd && vd.Declarations.All(d => d.Id is Identifier i && _vars.ContainsKey(i.Name) && d.Init != null && Lookup(d.Init, false) != null))
                    _elementDeclarations.Add(vd);

            foreach (var n in Markup.Everything(_ast)) Visit(n);
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

        /// <summary><c>document.getElementById('id')</c>, as the element it names; null for anything else.</summary>
        private Target? Lookup(Node e, bool report)
        {
            if (e is not CallExpression { Callee: MemberExpression { Computed: false, Object: Identifier { Name: "document" }, Property: Identifier { Name: "getElementById" } } } call
                || Declared("document"))
                return null;
            if (call.Arguments.Count != 1 || call.Arguments[0] is not StringLiteral lit)
            {
                if (report) Refuse(call, "an element chosen at run time (getElementById with an id computed at run time)");
                return null;
            }
            if (!_built.ById.TryGetValue(lit.Value, out var ve) || ve == null || !_built.NodeOf.TryGetValue(ve, out var node))
            {
                if (report) Refuse(call, $"getElementById('{lit.Value}') names no element of the page");
                return null;
            }
            if (!_targets.TryGetValue(ve, out var t))
            {
                _targets[ve] = t = new Target(ve, node);
                _order.Add(t);
            }
            return t;
        }

        /// <summary>One node of the script: an element reference, a timer, or a browser global, each checked where it stands.</summary>
        private void Visit(Node n)
        {
            if (_ignored.Contains(n)) return;
            switch (n)
            {
                case CallExpression c when c.Callee is MemberExpression { Object: Identifier { Name: "document" }, Property: Identifier { Name: "getElementById" } }:
                    if (Lookup(c, report: true) is { } t) Use(c, t);
                    return;

                case Identifier id when _vars.TryGetValue(id.Name, out var held) && Reference(id)
                                        && !(_parent.TryGetValue(id, out var decl) && decl is VariableDeclarator vd && vd.Id == id):
                    Use(id, held);
                    return;

                case Identifier { Name: "document" } doc when !Declared("document") && Reference(doc):
                    if (!(_parent[doc] is MemberExpression { Property: Identifier { Name: "getElementById" } } dm && dm.Object == doc
                          && _parent.TryGetValue(dm, out var dc) && dc is CallExpression cc && cc.Callee == dm))
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

        /// <summary>What the script does with an element, checked against the features translated here.</summary>
        private void Use(Expression at, Target t)
        {
            var p = _parent[at];
            if (p is VariableDeclarator vd && vd.Init == at && vd.Id is Identifier vid && _vars.TryGetValue(vid.Name, out var held) && held == t)
                return;
            if (p is not MemberExpression m || m.Object != at || m.Computed || m.Property is not Identifier prop)
            {
                Refuse(at, $"the element \"{t.Name}\" used as a value (only its properties are translated)");
                return;
            }
            var gp = _parent[m];
            switch (prop.Name)
            {
                case "textContent" or "innerText":
                    if (Assigned(m) is { } text)
                    {
                        if (t.Ve is not Label) Refuse(m, $"textContent of \"{t.Name}\", which holds elements rather than text, replaces them");
                        else { t.Texts.Add((text.At, text.Value)); _ops[text.At] = t; }
                    }
                    else if (prop.Name == "textContent" && !WrittenTo(m))
                    {
                        // what the program last wrote, or the page's own text: never a DOM
                        t.TextRead = true;
                        _lua[m] = lua => TextRead(t, m, lua);
                    }
                    else Refuse(m, $"reading or computing with .{prop.Name} of \"{t.Name}\" (element reads are not translated yet)");
                    return;

                case "style":
                    if (gp is MemberExpression s && s.Object == m && (s.Computed ? s.Property as StringLiteral is { } : s.Property is Identifier)
                        && Assigned(s) is { } style)
                    {
                        var name = DomSlots.Dashed(s.Property is Identifier si ? si.Name : ((StringLiteral)s.Property).Value);
                        if (!t.Styles.TryGetValue(name, out var list)) t.Styles[name] = list = new();
                        list.Add((style.At, style.Value));
                        _ops[style.At] = t;
                    }
                    else if (gp is MemberExpression r && r.Object == m && (r.Computed ? r.Property is StringLiteral : r.Property is Identifier)
                             && !WrittenTo(r) && !(_parent[r] is CallExpression rc && rc.Callee == r)
                             && DomSlots.Dashed(r.Property is Identifier ri ? ri.Name : ((StringLiteral)r.Property).Value) is var read
                             && read is not ("css-text" or "length" or "parent-rule"))
                    {
                        t.StyleReads.Add(read);
                        _lua[r] = lua => StyleRead(t, read, r, lua);
                    }
                    else Refuse(m, $"this use of .style on \"{t.Name}\" (assigning and reading style.<property> are translated)");
                    return;

                case "className":
                    if (Assigned(m) is { } cls)
                    {
                        if (Finite(cls.Value) is not { } values || values.Any(v => v is not string)) Refuse(cls.Value, $"className of \"{t.Name}\" set to a value only known at run time");
                        else { t.ClassNames.Add((cls.At, values)); _ops[cls.At] = t; }
                    }
                    else if (!WrittenTo(m))
                    {
                        t.ClassRead = true;
                        _lua[m] = _ => ClassNameRead(t);
                    }
                    else Refuse(m, $"this use of .className of \"{t.Name}\"");
                    return;

                case "getAttribute" or "hasAttribute" or "setAttribute" or "removeAttribute" or "toggleAttribute":
                    if (gp is CallExpression ac && ac.Callee == m && !ac.Arguments.Any(a => a is SpreadElement)) Attribute(t, prop.Name, ac);
                    else Refuse(m, $".{prop.Name} of \"{t.Name}\" used as a value");
                    return;

                case "hidden":
                    if (Assigned(m) is { } hid) AttrWrite(t, hid.At, "hidden", "hidden", hid.Value);
                    else if (!WrittenTo(m)) { _bool.Add(m); _lua[m] = lua => AttrRead(t, true, "hidden", null, lua); }
                    else Refuse(m, $"this use of .hidden of \"{t.Name}\"");
                    return;

                case "dataset":
                    Dataset(t, m);
                    return;

                case "classList" when gp is MemberExpression { Computed: false, Property: Identifier { Name: "contains" } } has && has.Object == m
                                       && _parent[has] is CallExpression hc && hc.Callee == has && hc.Arguments.Count == 1 && hc.Arguments[0] is Expression key:
                    _bool.Add(hc);
                    _lua[hc] = lua => Contains(t, key, lua);
                    return;

                case "classList":
                    if (gp is MemberExpression { Computed: false, Property: Identifier verb } lm && lm.Object == m
                        && verb.Name is "add" or "remove" or "toggle"
                        && _parent[lm] is CallExpression call && call.Callee == lm && Effect(call))
                    {
                        var names = new List<string>();
                        var args = verb.Name == "toggle" ? call.Arguments.Take(1) : call.Arguments;
                        foreach (var a in args)
                            if (a is StringLiteral { Value: { Length: > 0 } c } && c.IndexOfAny(new[] { ' ', '\t', '\n' }) < 0) names.Add(c);
                            else { Refuse(a, $"classList.{verb.Name} on \"{t.Name}\" with a class only known at run time"); return; }
                        if (names.Count == 0 || verb.Name == "toggle" && call.Arguments.Count > 2) { Refuse(call, $"classList.{verb.Name} on \"{t.Name}\" with these arguments"); return; }
                        t.ClassOps.Add((call, verb.Name, names, verb.Name == "toggle" && call.Arguments.Count == 2 ? (Expression)call.Arguments[1] : null));
                        _ops[call] = t;
                    }
                    else Refuse(m, $"classList.{(gp is MemberExpression { Property: Identifier vv } ? vv.Name : "…")} on \"{t.Name}\" (add, remove and toggle as statements are translated)");
                    return;

                case "onclick":
                    if (Assigned(m) is { } handler)
                    {
                        if (ReadsEvent(handler.Value)) return;
                        t.Listens = true;
                        _ops[handler.At] = t;
                    }
                    else Refuse(m, $"reading .onclick of \"{t.Name}\"");
                    return;

                case "addEventListener":
                    if (gp is CallExpression ec && ec.Callee == m && Effect(ec))
                    {
                        if (ec.Arguments.Count != 2 || ec.Arguments[0] is not StringLiteral kind) { Refuse(ec, $"addEventListener on \"{t.Name}\" with options or a computed event name"); return; }
                        if (kind.Value != "click") { Refuse(ec, $"the \"{kind.Value}\" event on \"{t.Name}\": the vector mod delivers only clicks"); return; }
                        if (ReadsEvent(ec.Arguments[1])) return;
                        t.Listens = true;
                        _ops[ec] = t;
                    }
                    else Refuse(m, $"addEventListener on \"{t.Name}\" used as a value");
                    return;

                default:
                    Refuse(m, $".{prop.Name} of \"{t.Name}\", which is outside the translated DOM features");
                    return;
            }
        }

        /// <summary>`target = value` evaluated for its effect: the only place a DOM write is translated.</summary>
        private (Expression At, Expression Value)? Assigned(MemberExpression target)
            => _parent[target] is AssignmentExpression { Operator: Operator.Assignment } a && a.Left == target && Effect(a)
                ? (a, a.Right) : null;

        /// <summary>Whether an expression's value is unused: a statement of its own, or a concise arrow's body.</summary>
        private bool Effect(Expression e)
            => _parent.TryGetValue(e, out var p) && (p is ExpressionStatement || p is ArrowFunctionExpression a && a.Body == e);

        /// <summary>A click handler that reads its event object, which the vector mod does not deliver: refused.</summary>
        private bool ReadsEvent(Node handler)
        {
            NodeList<Node> ps;
            Node body;
            switch (handler)
            {
                case FunctionExpression f: ps = f.Params; body = f.Body; break;
                case ArrowFunctionExpression a: ps = a.Params; body = a.Body; break;
                default: return false;
            }
            foreach (var p in ps)
            {
                if (p is not Identifier pid) { Refuse(p, "a click handler that unpacks its event"); return true; }
                if (Markup.Everything(body).Any(x => x is Identifier i && i.Name == pid.Name && Reference(i)))
                {
                    Refuse(handler, $"a click handler that reads its event object ({pid.Name}), which is not translated yet");
                    return true;
                }
            }
            return false;
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

        private void Attribute(Target t, string verb, CallExpression call)
        {
            var args = call.Arguments.Cast<Expression>().ToList();
            if (args.Count < (verb == "setAttribute" ? 2 : 1)) { Refuse(call, $"{verb} on \"{t.Name}\" without its arguments"); return; }
            var read = verb is "getAttribute" or "hasAttribute";
            if (args[0] is not StringLiteral lit)
            {
                if (read)
                {
                    t.AttrsByName = true;
                    if (verb == "hasAttribute") _bool.Add(call); else _null[call] = "null";
                    _lua[call] = lua => AttrRead(t, verb == "hasAttribute", null, args[0], lua);
                }
                else Refuse(call, $"{verb} on \"{t.Name}\" with a name only known at run time");
                return;
            }
            var name = lit.Value.ToLowerInvariant();
            if (read)
            {
                if (name is "class" or "style") Refuse(call, $"{verb}('{name}') on \"{t.Name}\" (className, classList and style are translated; the attribute itself is not yet)");
                else
                {
                    if (verb == "hasAttribute") _bool.Add(call); else _null[call] = "null";
                    _lua[call] = lua => AttrRead(t, verb == "hasAttribute", name, null, lua);
                }
                return;
            }
            AttrWrite(t, call, name, verb switch { "setAttribute" => "set", "removeAttribute" => "remove", _ => "toggle" },
                      verb == "removeAttribute" || args.Count < 2 ? null : args[1]);
        }

        /// <summary>
        /// An attribute write. One that CSS selects on changes which rules match, so it is laid out as a
        /// class is: a state per value it can hold. `hidden` is the browser's own `[hidden] {display: none}`.
        /// Any other is the program's own value, for the script to read back.
        /// </summary>
        private void AttrWrite(Target t, Expression at, string name, string verb, Expression? value)
        {
            if (name is "class" or "style" or "id") { Refuse(at, $"the {name} attribute written by {verb} on \"{t.Name}\" (className, classList and style are translated; this is not yet)"); return; }
            if (name.StartsWith("on", StringComparison.Ordinal)) { Refuse(at, $"an event handler attribute ({name}) written on \"{t.Name}\""); return; }
            if (Drawn.Contains(name)) { Refuse(at, $"the {name} attribute of \"{t.Name}\", which the page draws from directly rather than through CSS (not translated yet)"); return; }
            var op = new AttrOp { At = at, Name = name, Verb = verb, Value = value, Facet = name == "hidden" || _built.AttributeSelectors.Contains(name) };
            if (op.Facet && verb == "set" && (Finite(value!) is not { } values || values.Any(v => v is not (string or double))))
            {
                Refuse(value!, $"the {name} attribute of \"{t.Name}\", which CSS selects on, set to a value only known at run time");
                return;
            }
            t.AttrOps.Add(op);
            if (verb != "toggle") _effects.Add(at);
            _lua[at] = lua => AttrLua(t, op, lua);
        }

        /// <summary><c>el.dataset.fooBar</c>: the attribute <c>data-foo-bar</c>, read, written, deleted or tested with `in`.</summary>
        private void Dataset(Target t, MemberExpression ds)
        {
            static string Data(string key) => "data-" + Regex.Replace(key, "[A-Z]", c => "-" + c.Value.ToLowerInvariant());
            var gp = _parent[ds];
            if (gp is MemberExpression d && d.Object == ds && (d.Computed ? d.Property is StringLiteral : d.Property is Identifier))
            {
                var name = Data(d.Property is Identifier k ? k.Name : ((StringLiteral)d.Property).Value);
                if (Assigned(d) is { } w) { AttrWrite(t, w.At, name, "set", w.Value); return; }
                if (_parent[d] is NonUpdateUnaryExpression { Operator: Operator.Delete } del) { AttrWrite(t, del, name, "remove", null); return; }
                if (!WrittenTo(d) && !(_parent[d] is CallExpression dc && dc.Callee == d)) { _null[d] = "undefined"; _lua[d] = lua => AttrRead(t, false, name, null, lua); return; }
            }
            else if (gp is NonLogicalBinaryExpression { Operator: Operator.In, Left: StringLiteral key } test && test.Right == ds)
            {
                var name = Data(key.Value);
                _bool.Add(test);
                _lua[test] = lua => AttrRead(t, true, name, null, lua);
                return;
            }
            Refuse(ds, $"this use of .dataset on \"{t.Name}\" (reading, writing, deleting and testing one named key are translated)");
        }

        // ---- values the script can write ---------------------------------------------------------------

        /// <summary>
        /// Every value an expression can take, when that is a fixed set known from the source: literals,
        /// either side of a ternary or a logical operator, a name every binding of which is such a set,
        /// or a field of a constant table of them. Null when any of it is only known at run time.
        /// </summary>
        private List<object>? Finite(Expression e, HashSet<string>? seen = null)
        {
            switch (e)
            {
                case StringLiteral s: return new List<object> { s.Value };
                case NumericLiteral n: return new List<object> { n.Value };
                case TemplateLiteral { Expressions.Count: 0 } tl: return new List<object> { tl.Quasis[0].Value.Cooked ?? string.Empty };
                case ConditionalExpression c: return Union(Finite(c.Consequent, seen), Finite(c.Alternate, seen));
                case LogicalExpression l: return Union(Finite(l.Left, seen), Finite(l.Right, seen));
                case Identifier id when !id.Name.Equals("undefined", StringComparison.Ordinal):
                    {
                        seen ??= new HashSet<string>(StringComparer.Ordinal);
                        if (!seen.Add(id.Name)) return new List<object>();
                        if (Parameter(id.Name) is { } fromCalls) return fromCalls(seen);
                        List<object>? all = new();
                        var any = false;
                        foreach (var n in Markup.Everything(_ast))
                        {
                            Expression? v = n switch
                            {
                                VariableDeclarator { Id: Identifier d } vd when d.Name == id.Name => vd.Init,
                                AssignmentExpression { Operator: Operator.Assignment, Left: Identifier a } ae when a.Name == id.Name => ae.Right,
                                _ => null,
                            };
                            if (n is VariableDeclarator { Id: Identifier dd, Init: null } && dd.Name == id.Name) return null;
                            if (n is AssignmentExpression { Left: Identifier ca } cae && ca.Name == id.Name && cae.Operator != Operator.Assignment) return null;
                            if (n is UpdateExpression { Argument: Identifier ua } && ua.Name == id.Name) return null;
                            if (v == null) continue;
                            any = true;
                            all = Union(all, Finite(v, seen));
                            if (all == null) return null;
                        }
                        // a parameter, a loop variable or anything else bound without a value is not a set
                        return any && _bindings.TryGetValue(id.Name, out var b) && b == Markup.Everything(_ast).Count(x => x is VariableDeclarator { Id: Identifier i } && i.Name == id.Name) ? all : null;
                    }
                case MemberExpression { Object: Identifier table } m when ConstTable(table.Name) is { } values:
                    if (!m.Computed && m.Property is Identifier key)
                        return values.TryGetValue(key.Name, out var one) ? Finite(one, seen) : null;
                    List<object>? each = new();
                    foreach (var v in values.Values) { each = Union(each, Finite(v, seen)); if (each == null) return null; }
                    return each;
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
        }

        /// <summary>
        /// A name that is only ever a parameter of one named function, which is only ever called by name:
        /// its values are the arguments every call passes in that place. Null for anything else.
        /// </summary>
        private Func<HashSet<string>, List<object>?>? Parameter(string name)
        {
            if (!_bindings.TryGetValue(name, out var b) || b != 1 || _assigned.Contains(name)) return null;
            foreach (var n in Markup.Everything(_ast))
            {
                (string? Fn, NodeList<Node> Ps) f = n switch
                {
                    FunctionDeclaration { Id: { } fid } fd => (fid.Name, fd.Params),
                    VariableDeclarator { Id: Identifier vid, Init: FunctionExpression fe } => (vid.Name, fe.Params),
                    VariableDeclarator { Id: Identifier aid, Init: ArrowFunctionExpression ae } => (aid.Name, ae.Params),
                    _ => (null, default),
                };
                if (f.Fn == null) continue;
                var index = -1;
                for (var i = 0; i < f.Ps.Count; i++) if (f.Ps[i] is Identifier p && p.Name == name) index = i;
                if (index < 0) continue;
                var fn = f.Fn;
                if (!_bindings.TryGetValue(fn, out var fb) || fb != 1 || _assigned.Contains(fn)) return null;
                return seen =>
                {
                    List<object>? all = new();
                    foreach (var use in Markup.Everything(_ast))
                    {
                        if (use is not Identifier u || u.Name != fn || !Reference(u)) continue;
                        if (_parent[use] is VariableDeclarator { Id: var d } && d == use) continue;
                        if (_parent[use] is FunctionDeclaration { Id: var fd2 } && fd2 == use) continue;
                        // the function passed around rather than called: its arguments are not in the source
                        if (_parent[use] is not CallExpression call || call.Callee != use || call.Arguments.Count <= index) return null;
                        var one = Finite((Expression)call.Arguments[index], seen);
                        if (one == null) return null;
                        foreach (var v in one) if (!all.Contains(v)) all.Add(v);
                    }
                    return all;
                };
            }
            return null;
        }

        /// <summary>A `const` bound once to an object or array literal that nothing writes into, by key.</summary>
        private Dictionary<string, Expression>? ConstTable(string name)
        {
            if (!_bindings.TryGetValue(name, out var b) || b != 1 || _assigned.Contains(name)) return null;
            foreach (var n in Markup.Everything(_ast))
                if (n is AssignmentExpression { Left: MemberExpression { Object: Identifier o } } && o.Name == name) return null;
            foreach (var n in Markup.Everything(_ast))
            {
                if (n is not VariableDeclarator { Id: Identifier id, Init: { } init } || id.Name != name) continue;
                var table = new Dictionary<string, Expression>(StringComparer.Ordinal);
                if (init is ArrayExpression arr)
                {
                    for (var i = 0; i < arr.Elements.Count; i++)
                        if (arr.Elements[i] is Expression x) table[i.ToString(CultureInfo.InvariantCulture)] = x; else return null;
                    return table;
                }
                if (init is ObjectExpression obj)
                {
                    foreach (var p in obj.Properties)
                        if (p is Property { Computed: false, Value: Expression v } prop && prop.Key is Identifier or StringLiteral)
                            table[prop.Key is Identifier k ? k.Name : ((StringLiteral)prop.Key).Value] = v;
                        else return null;
                    return table;
                }
            }
            return null;
        }

        /// <summary>
        /// A written text as literal pieces and values. `a + b + ' kPa'` is one value then a literal,
        /// because JavaScript adds `a + b` before anything is a string; a template or a chain after its
        /// first string literal is a value per hole. `x.toFixed(n)` is x, printed with `%.nf`.
        /// </summary>
        private static List<Piece> Pieces(Expression e)
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
                    var name = t.Ve.name.Substring(2);
                    for (var k = 2; _built.ById.ContainsKey(name); k++) name = t.Ve.name.Substring(2) + "_" + k.ToString(CultureInfo.InvariantCulture);
                    _built.ById.Remove(t.Ve.name);
                    t.Ve.name = name;
                    _built.ById[name] = t.Ve;
                    t.Node.Attributes["id"] = name;
                }
                t.Name = t.Ve.name;
                _built.Driven.Add(t.Name);
                foreach (var css in t.Styles.Keys)
                    if (css is "opacity" or "visibility" or "transform") _built.NamedGroups.Add(t.Name);
                if (t.Listens && !Clickable(t.Node)) t.Node.Attributes["data-click"] = "1";
                // hidden takes the element out of the layout; the scene keeps its shapes and a `v` to show them
                if (t.AttrOps.Any(o => o.Name == "hidden")) { _hide.Add(t); _built.NamedGroups.Add(t.Name); }
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
            var absolute = PageCompiler.Absolute(_built);

            Texts();
            foreach (var t in _order)
            {
                foreach (var pair in t.Styles)
                {
                    Style(t, pair.Key, pair.Value, available, absolute);
                    if (_refused.Count > 0) return _template;
                }
                if (t.Facets) Classes(t);
                if (_refused.Count > 0) return _template;
            }
            Reads();
            return _template;
        }

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
                    if (hides && node.Attr("hidden") != null) t.Ve.style.display = DisplayStyle.None;
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
                for (var i = 0; i < _hide.Count; i++)
                {
                    if (shown[i]) continue;
                    var ve = _hide[i].Ve;
                    var was = ve.style.display;
                    ve.style.display = StyleKeyword.Null;
                    _panel.Layout(_size.x, _size.y);
                    var open = PageCompiler.Captured(_built);
                    foreach (var d in Subtree(ve))
                        if (open.TryGetValue(d, out var b)) boxes[d] = b;
                    ve.style.display = was;
                }
                if (shown.Any(x => !x)) _panel.Layout(_size.x, _size.y);
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
                    Refuse(_hide[i].AttrOps.First(o => o.Name == "hidden").At, $"hidden on \"{name}\": its group in the scene carries no name");
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
            try
            {
                apply();
                _panel.Layout(_size.x, _size.y);
                laidOut?.Invoke();
                var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
                return Emit(values) == _template ? values : null;
            }
            finally
            {
                undo();
            }
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
        private void Texts()
        {
            var texts = _order.Where(t => t.Texts.Count > 0).ToList();
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
                        if (piece.Literal != null) { body.Append(Tmp(piece.Literal)); plan.Read.Add((piece.Literal, null, null)); continue; }
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
                            Add(plan, w, whole, Tmp(string.Concat(parts.Select(p => p.Literal))), null);
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
                            Add(plan, w, run.Slots[i], parts[i].Literal != null ? Tmp(parts[i].Literal!) : null, parts[i].Literal != null ? null : parts[i]);
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
            if (drawn == null) return "changes the scene's structure";
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
            var box = PageCompiler.BoxOf(t.Name, _built, absolute, available);
            if (box == null) { Refuse(at, $"\"{t.Name}\" is not laid out"); return; }
            var mapped = DomSlots.Map(t.Name, "style." + css, box.Value, available);
            if (!mapped.Mapped) { Refuse(at, $"style.{css} on \"{t.Name}\": {mapped.Problem}"); return; }
            if (mapped.NeedsGroup) { Refuse(at, $"style.{css} on \"{t.Name}\": its group carries no name"); return; }

            var plan = new StylePlan { Var = "V_S" + (_order.Sum(x => x.StylePlans.Count) + 1).ToString(CultureInfo.InvariantCulture) };
            var facet = $"style.{css} of \"{t.Name}\"";
            var node = t.Node;
            var style = node.Attr("style");
            var cls = node.Attr("class") ?? string.Empty;
            Dictionary<string, SceneSlots.Value>? Drawn(string value) => Variant(
                () => { node.Attributes["style"] = (style ?? string.Empty) + ";" + css + ":" + value; _built.Reclass(t.Ve, cls); },
                () => { if (style == null) node.Attributes.Remove("style"); else node.Attributes["style"] = style; _built.Reclass(t.Ve, cls); });

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
                    if (drawn == null) { Refuse(at, $"{facet} = \"{text}\" changes the scene's structure, which is not a value"); return; }
                    plan.States[value] = drawn;
                    moved.UnionWith(Moved(drawn));
                }
                Complete(plan.States.Values, moved);
                foreach (var slot in moved) if (!Claim(slot, facet, at)) return;
                t.StylePlans[css] = plan;
                return;
            }

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
            else { Refuse(at, $"{facet} in \"{(unit.Length == 0 ? "no unit" : unit)}\", which is not translated to scene units"); return; }

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
                if (proof == null) { Refuse(at, $"{facet} changes the scene's structure at {JsToLuaNumber(sample)}{unit}"); return; }
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
            if (plan.Names.Count > 6 || total > 64)
            {
                Refuse(at, $"the script changes {plan.Names.Count} classes and {plan.Attrs.Count} attributes of \"{t.Name}\", {total} combinations, more than the 64 that are laid out");
                return;
            }

            var node = t.Node;
            var was = node.Attr("class") ?? string.Empty;
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
                if (hides && node.Attr("hidden") != null) t.Ve.style.display = DisplayStyle.None;
            }
            var moved = new HashSet<string>(StringComparer.Ordinal);
            var facet = $"the classes and attributes of \"{t.Name}\"";
            for (var key = 0; key < total; key++)
            {
                var on = new List<string>(fixedClasses);
                for (var i = 0; i < plan.Names.Count; i++) if ((key & (1 << i)) != 0) on.Add(plan.Names[i]);
                var cls = string.Join(" ", on);
                var attrs = new List<string?>();
                var rest = key >> plan.Names.Count;
                foreach (var a in plan.Attrs) { attrs.Add(a.Options[rest % a.Options.Count]); rest /= a.Options.Count; }
                var drawn = Variant(() => Apply(cls, attrs), () => { Apply(was, wasAttrs); t.Ve.style.display = wasDisplay; });
                if (drawn == null)
                {
                    var described = cls + string.Concat(plan.Attrs.Select((a, i) => attrs[i] == null ? string.Empty : $" [{a.Name}=\"{attrs[i]}\"]"));
                    Refuse(at, $"class \"{described.Trim()}\" on \"{t.Name}\" changes the scene's structure, which is not a value");
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

        private string Contains(Target t, Expression key, JsToLua lua)
        {
            var k = key is StringLiteral s ? Q(s.Value) : "js_str(" + lua.Translate(key) + ")";
            if (t.Class != null) return "(" + t.Class.Var + ".on[" + k + "] == true)";
            var classes = (SourceNode(t).Attr("class") ?? string.Empty).Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            if (key is StringLiteral lit) return classes.Contains(lit.Value) ? "true" : "false";
            var name = "V_K" + (_consts.Count + 1).ToString(CultureInfo.InvariantCulture);
            _consts.Add("local " + name + " = " + Table(classes.Select(c => (c, "true"))));
            return "(" + name + "[" + k + "] == true)";
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
        private string AttrRead(Target t, bool has, string? name, Expression? byName, JsToLua lua)
        {
            if (t.AttrTable != null)
            {
                var v = t.AttrTable + "[" + (name != null ? Q(name) : "string.lower(js_str(" + lua.Translate(byName!) + "))") + "]";
                return has ? "(" + v + " ~= nil)" : "v_attr(" + v + ")";
            }
            var own = SourceNode(t).Attr(name!);
            return has ? (own != null ? "true" : "false") : own != null ? Q(own) : "nil";
        }

        private static string AttrLua(Target t, AttrOp op, JsToLua lua)
        {
            var at = t.AttrTable!;
            var c = op.Facet ? t.Class!.Var : "nil";
            var n = Q(op.Name);
            return op.Verb switch
            {
                "set" => "v_setattr(" + at + ", " + n + ", " + lua.Translate(op.Value!) + ", " + c + ")",
                "remove" => "v_rmattr(" + at + ", " + n + ", " + c + ")",
                "toggle" => "v_toggleattr(" + at + ", " + n + ", " + (op.Value != null ? lua.Translate(op.Value) : "nil") + ", " + c + ")",
                _ => "v_hidden(" + at + ", " + lua.Translate(op.Value!) + ", " + c + ")",
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
                    pairs.Add((key, Q(pair.Value)));
                }
            return Table(pairs);
        }

        // ---- the program ------------------------------------------------------------------------------

        /// <summary>The DOM writes and timers of the script, as the Lua that does them.</summary>
        public bool Statement(JsToLua lua, Node s)
        {
            if (s is VariableDeclaration && _elementDeclarations.Contains(s)) return true;
            if (s is ExpressionStatement es) s = es.Expression;
            if (s is not Expression e) return false;
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
                else if (code != "nil") lua.Emit("local __discard = " + code);
                return true;
            }
            if (!_ops.TryGetValue(e, out var t)) return false;

            if (e is AssignmentExpression { Left: MemberExpression m } a)
            {
                if (m.Object is MemberExpression { Computed: false, Property: Identifier { Name: "style" } })
                {
                    var css = DomSlots.Dashed(m.Property is Identifier si ? si.Name : ((StringLiteral)m.Property).Value);
                    var plan = t.StylePlans[css];
                    if (plan.ReadVar != null)
                    {
                        // read back later: the value is kept as written, and printed only when read
                        lua.Emit("do");
                        lua.Emit("  local v = " + lua.Translate(plan.States != null ? a.Right : plan.Holes[e]));
                        lua.Emit("  " + plan.ReadVar + " = v");
                        if (plan.States != null) lua.Emit("  v_state(" + plan.Var + ", v)");
                        else foreach (var (slot, sa, sb) in plan.Linear!) lua.Emit("  v_set(" + Q(slot) + ", " + Affine("v", sa, sb) + ")");
                        lua.Emit("end");
                        return true;
                    }
                    if (plan.States != null)
                    {
                        lua.Emit("v_state(" + plan.Var + ", " + lua.Translate(a.Right) + ")");
                        return true;
                    }
                    var hole = plan.Holes[e];
                    if (plan.Linear!.Length == 1)
                    {
                        lua.Emit("v_set(" + Q(plan.Linear[0].Slot) + ", " + Affine(lua.Translate(hole), plan.Linear[0].A, plan.Linear[0].B) + ")");
                        return true;
                    }
                    lua.Emit("do");
                    lua.Emit("  local v = " + lua.Translate(hole));
                    foreach (var (slot, sa, sb) in plan.Linear) lua.Emit("  v_set(" + Q(slot) + ", " + Affine("v", sa, sb) + ")");
                    lua.Emit("end");
                    return true;
                }
                if (m.Property is Identifier { Name: "textContent" or "innerText" })
                {
                    foreach (var (slot, literal, hole) in t.Text!.Writes[e])
                        lua.Emit("v_set(" + Q(slot) + ", " + (literal != null ? JsToLua.Quote(literal) : HoleLua(lua, hole!)) + ")");
                    return true;
                }
                if (m.Property is Identifier { Name: "className" })
                {
                    lua.Emit("v_classname(" + t.Class!.Var + ", " + lua.Translate(a.Right) + ")");
                    return true;
                }
                lua.Emit("V_ONCLICK[" + Q(t.Name) + "] = " + lua.Translate(a.Right));
                return true;
            }

            if (e is CallExpression { Callee: MemberExpression { Property: Identifier { Name: "addEventListener" } } } listen)
            {
                lua.Emit("v_listen(" + Q(t.Name) + ", " + lua.Translate(listen.Arguments[1]) + ")");
                return true;
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
                    _ => op.Force != null ? "js_truthy(" + lua.Translate(op.Force) + ")" : "not " + on,
                };
                // className is read back: the classes' order is kept as the attribute holds it
                lua.Emit(t.Class.Order != null ? "v_classop(" + c + ", " + Q(name) + ", " + want + ")" : on + " = " + want);
            }
            lua.Emit("v_class(" + c + ")");
            return true;
        }

        /// <summary>A call, which Lua takes as a statement of its own.</summary>
        private static readonly Regex Call = new(@"^[A-Za-z_][A-Za-z0-9_.]*\(.*\)$", RegexOptions.Singleline);

        /// <summary>Whether an expression the Lua writes itself is a write, whose value (undefined) nothing needs.</summary>
        private bool Effectual(Expression e) => _effects.Contains(e) || e is AssignmentExpression;

        /// <summary>Timers, and anything the analysis let through that must not reach the DOM-less Lua.</summary>
        public string? Expression(JsToLua lua, Node e)
        {
            if (_lua.TryGetValue(e, out var own)) return own(lua);
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
            if (e is CallExpression && Lookup(e, false) != null) return "nil";
            return null;
        }

        private string HoleLua(JsToLua lua, Piece hole)
        {
            var v = lua.Translate(hole.Hole!);
            if (hole.Digits is { } n)
            {
                _fixed = true;
                return "v_fixed(" + v + ", " + n.ToString(CultureInfo.InvariantCulture) + ")";
            }
            if (Boolean(hole.Hole!) || _bool.Contains(hole.Hole!)) return "js_str(" + v + ")";
            // a read that can be null prints "null", as JavaScript's string conversion does
            return _null.TryGetValue(hole.Hole!, out var none) ? "(" + v + " or " + Q(none) + ")" : v;
        }

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
            var states = _order.SelectMany(t => t.StylePlans.Values).Where(p => p.States != null).ToList();
            foreach (var slot in Written)
                if (!_ease.ContainsKey(slot) && Opening.TryGetValue(slot, out var v) && v.IsNumber) _ease[slot] = Ease(slot);

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
            sb.Append("local V_EASE = ").Append(Table(Written.Where(_ease.ContainsKey).Select(s => (s, _ease[s])))).Append('\n');
            sb.Append("local V_P, V_E = {}, {}\n");
            sb.Append("local V_SEND = { data = V_P, ease = V_E }\n");
            sb.Append("local V_DATA\n");
            sb.Append("local function v_set(k, v)\n  if V_D[k] ~= v then V_D[k] = v V_P[k] = v V_E[k] = V_EASE[k] end\nend\n");
            sb.Append("local function v_flush()\n  if next(V_P) == nil then return end\n  V_DATA:set_props(V_SEND)\n  ui:commit()\n");
            sb.Append("  for k in pairs(V_P) do V_P[k] = nil end\n  for k in pairs(V_E) do V_E[k] = nil end\nend\n");
            if (_reload)
            {
                sb.Append("-- location.reload(): the page starts again from its source once the script that asked has run\n");
                sb.Append("local V_D0 = ").Append(Table(Written.Select(s => (s, Value(Opening[s]))))).Append('\n');
                sb.Append("local V_RELOAD = false\nlocal v_main, v_reload\n");
                sb.Append("local function v_askreload() V_RELOAD = true end\n");
                sb.Append("local function v_reset(t, t0)\n  for k in pairs(t) do t[k] = nil end\n  for k, v in pairs(t0) do t[k] = v end\nend\n");
            }

            if (states.Count > 0)
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
                sb.Append("local V_CHAIN = ").Append(Table(Chains())).Append('\n');
                sb.Append("local function v_listen(key, fn)\n  local list = V_ON[key]\n  for i = 1, #list do if list[i] == fn then return end end\n");
                sb.Append("  list[#list + 1] = fn\nend\n");
                sb.Append("local function v_click(nodeId)\n  local chain = V_LIVE and V_CHAIN[nodeId]\n  if not chain then return end\n");
                sb.Append("  for c = 1, #chain do\n    local list = V_ON[chain[c]]\n    for i = 1, #list do list[i]() end\n");
                sb.Append("    local f = V_ONCLICK[chain[c]]\n    if f then f() end\n  end\n");
                if (_reload) sb.Append("  if V_RELOAD then v_reload() end\n");
                sb.Append("  v_flush()\nend\n");
            }

            var reads = Reading();
            var helpers = Helpers(page + browser + reads + (_fixed ? " NumberMethods.toFixed" : string.Empty));
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
            sb.Append("for k in pairs(V_P) do V_P[k] = nil end\nfor k in pairs(V_E) do V_E[k] = nil end\n");
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
            if (_attrs)
            {
                sb.Append("-- attributes, as the browser stores them: a string, or nil for absent\n");
                sb.Append("local function v_attr(v)\n  if v == nil or type(v) == \"string\" then return v end\n  return js_str(v)\nend\n");
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
            foreach (Match m in Called.Matches(text))
                foreach (var pair in defined)
                    if (pair.Key.EndsWith("." + m.Groups[1].Value, StringComparison.Ordinal)) queue.Enqueue(pair.Key);
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
    private static readonly Regex Called =new(@"js_m\([^,]+,\s*""([A-Za-z_][A-Za-z0-9_]*)""", RegexOptions.Compiled);
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

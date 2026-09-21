using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Acornima;
using Acornima.Ast;

namespace ScriptedScreensHtml;

/// <summary>
/// Which elements a page's script writes to, which properties, and whether it does so once at load
/// or on every frame.
/// </summary>
/// <remarks>
/// This is the question the compiler has to answer before it emits anything, and the answer decides
/// two things.
///
/// <b>What needs a slot.</b> The compiler runs <i>after</i> the page's setup code, so a write that
/// only ever happens during setup is already baked into the geometry and needs nothing at runtime.
/// Measured on <c>07-game</c>: <c>field.style.height</c>, <c>groundLine.style.top</c> and
/// <c>overlay.style.bottom</c> are all setup, while the four leg and boot boxes, the parallax
/// layers and the score are driven every frame. Only the second set needs anything.
///
/// <b>What needs a name.</b> An element's box already carries its id in the scene, so its x, y,
/// width, height and fill are addressable. A transform does not: it goes on a wrapping group, and
/// giving every wrapper an id would make the renderer retain a prop array for each one. So the
/// emitter is told which few elements are actually driven, and names only those.
///
/// Setup-versus-runtime is call-graph reachability, not source position: <c>draw()</c> is written
/// at the top level but only ever runs from a frame callback. The graph is walked from the two
/// roots - top-level statements, and anything handed to requestAnimationFrame, setInterval,
/// setTimeout or addEventListener - and a function reached from the second is runtime.
/// </remarks>
internal sealed class DomWrites
{
    /// <summary>One write the script makes: which element, which property, and when.</summary>
    internal sealed class Write
    {
        /// <summary>The element's id when it is a literal, else null.</summary>
        public string? Id;
        /// <summary>The id expression's source when it is computed, e.g. <c>'pb' + i</c>.</summary>
        public string? Computed;
        /// <summary>
        /// The literal head of a computed id, so <c>$('pb' + i)</c> gives <c>pb</c>. A page builds a
        /// family of elements with one prefix and a running number, and every member of that family
        /// already exists in the page with its own id - so the family resolves to a list of real
        /// elements at compile time and needs no run-time lookup at all.
        /// </summary>
        public string? Prefix;
        /// <summary>`style.height`, `textContent`, `className`, `@data-mode` for an attribute.</summary>
        public string Property = string.Empty;
        /// <summary>True when this write can happen after the page has been compiled.</summary>
        public bool Runtime;
        /// <summary>Line in the script, for a report the author can act on.</summary>
        public int Line;
        /// <summary>
        /// For a className write: every class string the script can assign here, when they are all
        /// literals. A class name is a state, not a value, so the compiler lays the page out in each
        /// and emits what each draws. Null when the script computes one, which cannot be enumerated.
        /// </summary>
        public List<string>? Classes;

        public override string ToString() =>
            (Id ?? "<" + Computed + ">") + "." + Property + (Runtime ? " (runtime)" : " (setup)");
    }

    /// <summary>Properties that reach an element rather than its style object.</summary>
    private static readonly HashSet<string> ElementProperties = new(StringComparer.Ordinal)
    {
        "textContent", "innerText", "innerHTML", "className", "value", "checked", "disabled",
        "src", "title", "scrollTop", "scrollLeft",
    };

    /// <summary>The names a page hands a callback to, which is what makes that callback runtime.</summary>
    private static readonly HashSet<string> Schedulers = new(StringComparer.Ordinal)
    {
        "requestAnimationFrame", "setInterval", "setTimeout", "addEventListener", "setImmediate",
    };

    private readonly Dictionary<string, FunctionInfo> _functions = new(StringComparer.Ordinal);
    private readonly List<Write> _writes = new();
    private readonly List<string> _notes = new();
    /// <summary>Names bound to `document.getElementById(...)`, so `const p = $('player')` resolves.</summary>
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    /// <summary>Names bound to a one-argument getElementById wrapper, i.e. the page's own `$`.</summary>
    private readonly HashSet<string> _lookups = new(StringComparer.Ordinal);
    /// <summary>
    /// A getter that hands back an element, and the literal head of the ids it hands back. The
    /// object-pool idiom: <c>{ id: 'ob' + i, get el() { return $(this.id); } }</c> makes
    /// <c>o.el.style.transform</c> a write to the `ob` family, and nothing else in the script says
    /// so. Keyed by the getter's name, since that is all a write site gives.
    /// </summary>
    private readonly Dictionary<string, string> _handles = new(StringComparer.Ordinal);
    /// <summary>
    /// What a `const` was initialised to. A page almost always builds a class name in a local first -
    /// `const cls = (duck ? 'duck' : '') + (over ? ' hurt' : '')` and then `el.className = cls.trim()` -
    /// so stopping at the name would miss every state that write can reach. Only `const`: anything
    /// reassigned later is not the single value this pretends it is.
    /// </summary>
    private readonly Dictionary<string, List<Expression>> _constants = new(StringComparer.Ordinal);
    /// <summary>Guards against a name defined in terms of itself while enumerating what it can hold.</summary>
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);

    private sealed class FunctionInfo
    {
        public readonly List<Write> Writes = new();
        public readonly HashSet<string> Calls = new(StringComparer.Ordinal);
        public bool Runtime;
    }

    /// <summary>Every write the script makes, and any shape the analysis could not resolve.</summary>
    internal static (IReadOnlyList<Write> Writes, IReadOnlyList<string> Notes) Of(string source)
    {
        var a = new DomWrites();
        Script ast;
        try { ast = new Parser().ParseScript(source); }
        catch (Exception ex) { return (Array.Empty<Write>(), new[] { "parse: " + ex.Message }); }

        a.Aliases(ast);
        a.Collect(ast);
        a.Spread(ast);
        return (a._writes, a._notes);
    }

    // ---- who is who -----------------------------------------------------------------------------

    /// <summary>
    /// Names that stand for an element, and names that stand for getElementById. A page almost
    /// always writes `const $ = id => document.getElementById(id)` and then `$('legA')`, or binds an
    /// element once as `const player = $('player')`; neither resolves without this.
    /// </summary>
    private void Aliases(Node root)
    {
        foreach (var n in All(root))
        {
            if (n is not VariableDeclarator { Id: Identifier name, Init: { } init }) continue;

            // const $ = (id) => document.getElementById(id)
            if (init is ArrowFunctionExpression { Body: CallExpression call } && IsLookup(call)) { _lookups.Add(name.Name); continue; }
            if (init is ArrowFunctionExpression { Body: BlockStatement block })
            {
                foreach (var s in block.Body)
                    if (s is ReturnStatement { Argument: CallExpression rc } && IsLookup(rc)) { _lookups.Add(name.Name); break; }
                continue;
            }
            // const player = $('player')  /  document.getElementById('player')
            if (init is CallExpression direct && Target(direct) is { } id) _aliases[name.Name] = id;
        }
        Handles(root);

        // Every expression a name can ever hold: its initialiser, anything later assigned to it, and
        // - when it is a parameter - every argument passed at a call site. The union over-approximates,
        // which is the safe direction: an extra state costs a little compile time, a missing one
        // costs a class the page can reach and the compiler cannot draw.
        //
        // Function bodies included, because the class a page assigns is almost always built in a
        // local inside draw(), and All() deliberately stops at a function boundary.
        foreach (var n in Everything(root))
        {
            switch (n)
            {
                case VariableDeclaration vd:
                    foreach (var d in vd.Declarations)
                        if (d.Id is Identifier vn && d.Init != null) Holds(vn.Name, d.Init);
                    break;

                case AssignmentExpression { Operator: Operator.Assignment, Left: Identifier an } assign:
                    Holds(an.Name, assign.Right);
                    break;

                // a parameter holds whatever its callers pass
                case FunctionDeclaration { Id: { } fid } fn:
                    Parameters(fid.Name, fn.Params, root);
                    break;
            }
        }
    }

    private void Holds(string name, Expression value)
    {
        if (!_constants.TryGetValue(name, out var list)) _constants[name] = list = new List<Expression>();
        if (list.Count < 16) list.Add(value);   // a name with more than sixteen sources is not a state
    }

    /// <summary>Binds each parameter to every argument any caller passes it.</summary>
    private void Parameters(string function, in NodeList<Node> parameters, Node root)
    {
        var names = new List<string?>();
        foreach (var p in parameters) names.Add(p is Identifier id ? id.Name : null);
        if (names.Count == 0) return;

        foreach (var n in Everything(root))
        {
            if (n is not CallExpression { Callee: Identifier callee } call || callee.Name != function) continue;
            for (var i = 0; i < names.Count && i < call.Arguments.Count; i++)
                if (names[i] is { } param && call.Arguments[i] is Expression arg)
                    Holds(param, arg);
        }
    }

    /// <summary>Every node below this one, through function boundaries as well.</summary>
    private static IEnumerable<Node> Everything(Node n)
    {
        yield return n;
        foreach (var kid in n.ChildNodes)
        {
            if (kid == null) continue;
            foreach (var deep in Everything(kid)) yield return deep;
        }
    }

    /// <summary>
    /// Finds the object-pool handle: a getter whose body looks up <c>this.FIELD</c>, where FIELD is
    /// set in the same literal to a string with a literal head. That head names the whole family,
    /// and every member of it already exists in the page under its own id.
    /// </summary>
    private void Handles(Node root)
    {
        foreach (var n in All(root))
        {
            if (n is not ObjectExpression obj) continue;

            // what each plain property is initialised to, so `this.id` can be followed
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in obj.Properties)
                if (p is ObjectProperty { Kind: PropertyKind.Init, Key: Identifier k } prop && Head(prop.Value) is { } head)
                    fields[k.Name] = head;

            foreach (var p in obj.Properties)
            {
                if (p is not ObjectProperty { Kind: PropertyKind.Get, Key: Identifier getter, Value: FunctionExpression fn }) continue;
                foreach (var inner in All(fn.Body))
                {
                    if (inner is not ReturnStatement { Argument: CallExpression call }) continue;
                    if (!IsLookup(call) && !(call.Callee is Identifier f && _lookups.Contains(f.Name))) continue;
                    if (call.Arguments.Count != 1) continue;
                    if (call.Arguments[0] is MemberExpression { Object: ThisExpression, Computed: false, Property: Identifier field }
                        && fields.TryGetValue(field.Name, out var prefix))
                        _handles[getter.Name] = prefix;
                }
            }
        }
    }

    private static bool IsLookup(CallExpression call) =>
        call.Callee is MemberExpression { Computed: false, Property: Identifier { Name: "getElementById" } };

    /// <summary>The element id a call resolves to, when it is a literal.</summary>
    private string? Target(CallExpression call)
    {
        if (call.Arguments.Count != 1) return null;
        var byDocument = IsLookup(call);
        var byShorthand = call.Callee is Identifier fn && _lookups.Contains(fn.Name);
        if (!byDocument && !byShorthand) return null;
        return call.Arguments[0] is StringLiteral s ? s.Value : null;
    }

    // ---- the writes -----------------------------------------------------------------------------

    private void Collect(Script ast)
    {
        // top level first: those writes are setup unless the graph later says otherwise
        foreach (var s in ast.Body)
        {
            if (s is FunctionDeclaration { Id: { } id } f)
            {
                var info = Info(id.Name);
                Scan(f.Body, info);
                continue;
            }
            Scan(s, null);
        }
    }

    /// <summary>Walks one function body (or a top-level statement) for writes and for calls.</summary>
    private void Scan(Node body, FunctionInfo? owner)
    {
        foreach (var n in All(body))
        {
            if (n is CallExpression call)
            {
                if (call.Callee is Identifier callee) owner?.Calls.Add(callee.Name);
                // a callback handed to a scheduler is runtime, and so is everything it reaches
                if (Scheduled(call) is { } scheduled) Info(scheduled).Runtime = true;
                if (call.Callee is MemberExpression { Computed: false, Property: Identifier { Name: "addEventListener" } }
                    && call.Arguments.Count >= 2 && call.Arguments[1] is Identifier handler)
                    Info(handler.Name).Runtime = true;
                // an inline callback's body is runtime in place
                foreach (var arg in call.Arguments)
                    if (IsSchedulerCall(call) && arg is ArrowFunctionExpression or FunctionExpression)
                        foreach (var w in Found(arg)) { w.Runtime = true; _writes.Add(w); }
            }

            if (n is not AssignmentExpression { Left: MemberExpression target } assign) continue;
            var write = Resolve(target, assign);
            if (write == null) continue;
            if (owner == null) _writes.Add(write); else owner.Writes.Add(write);
        }
    }

    private static bool IsSchedulerCall(CallExpression call) =>
        (call.Callee is Identifier i && Schedulers.Contains(i.Name))
        || (call.Callee is MemberExpression { Computed: false, Property: Identifier m } && Schedulers.Contains(m.Name));

    /// <summary>The name of a named function handed to a scheduler.</summary>
    private static string? Scheduled(CallExpression call)
    {
        if (!IsSchedulerCall(call)) return null;
        foreach (var arg in call.Arguments)
            if (arg is Identifier fn) return fn.Name;
        return null;
    }

    private List<Write> Found(Node body)
    {
        var writes = new List<Write>();
        foreach (var n in All(body))
            if (n is AssignmentExpression { Left: MemberExpression t } a && Resolve(t, a) is { } w)
                writes.Add(w);
        return writes;
    }

    /// <summary>
    /// A member assignment as a DOM write, or null when it is not one. Three shapes reach an
    /// element: <c>el.style.PROP = v</c>, <c>el.PROP = v</c> for a known element property, and
    /// <c>el.setAttribute(n, v)</c> (handled by the caller as a call, not an assignment).
    /// </summary>
    private Write? Resolve(MemberExpression target, AssignmentExpression assign)
    {
        if (target.Computed || target.Property is not Identifier prop) return null;

        // el.style.height = ...
        if (target.Object is MemberExpression { Computed: false, Property: Identifier { Name: "style" } } style)
            return Make(style.Object, "style." + prop.Name, assign);

        // el.textContent = ...
        if (ElementProperties.Contains(prop.Name))
        {
            var write = Make(target.Object, prop.Name, assign);
            // A className write is a state. Collect the strings it can take, so the compiler can lay
            // the page out in each; one computed value and the set is not enumerable, which is a
            // different answer and has to stay distinguishable from "no classes".
            if (write != null && prop.Name == "className") write.Classes = Literals(assign.Right);
            return write;
        }

        return null;
    }

    private Write? Make(Expression element, string property, Node at)
    {
        var write = new Write { Property = property, Line = at.Location.Start.Line };
        switch (element)
        {
            case CallExpression call when Target(call) is { } id:
                write.Id = id;
                return write;
            case CallExpression call2 when IsLookup(call2) || (call2.Callee is Identifier f && _lookups.Contains(f.Name)):
                // `$('pb' + i)` - a family of elements, resolved by its literal head below
                write.Computed = call2.Arguments.Count > 0 ? Source(call2.Arguments[0]) : "?";
                write.Prefix = call2.Arguments.Count > 0 ? Head(call2.Arguments[0]) : null;
                return write;
            case Identifier name when _aliases.TryGetValue(name.Name, out var aliased):
                write.Id = aliased;
                return write;
            case Identifier name2:
                write.Computed = name2.Name;
                return write;
            case MemberExpression { Computed: false, Property: Identifier handle } m when _handles.TryGetValue(handle.Name, out var family):
                // `o.el` on an object pool: one write site, a family of real elements
                write.Computed = Source(m);
                write.Prefix = family;
                return write;
            case MemberExpression m:
                write.Computed = Source(m);
                return write;
            default:
                return null;
        }
    }

    /// <summary>
    /// The literal string an expression begins with, or null. <c>'pb' + i</c> gives <c>pb</c>;
    /// anything whose left edge is not a literal gives nothing, because a prefix guessed wrong would
    /// name the wrong elements.
    /// </summary>
    private static string? Head(Node n) => n switch
    {
        StringLiteral s => s.Value,
        NonLogicalBinaryExpression { Operator: Operator.Addition } b => Head(b.Left),
        _ => null,
    };

    /// <summary>
    /// Every string an expression can evaluate to, when that is a finite set of literals. A page
    /// writes `duckNow ? 'duck' : ''` or `(duck ? 'duck' : '') + (over ? ' hurt' : '')`, and both are
    /// enumerable; anything else returns null rather than a guess.
    /// </summary>
    private List<string>? Literals(Expression e)
    {
        switch (e)
        {
            case Identifier name when _constants.TryGetValue(name.Name, out var sources):
                {
                    if (!_resolving.Add(name.Name)) return null;   // defined in terms of itself
                    try
                    {
                        var all = new List<string>();
                        foreach (var source in sources)
                        {
                            var some = Literals(source);
                            if (some == null) return null;          // one unknowable source, and the set is not a set
                            all.AddRange(some);
                        }
                        return all.Count == 0 ? null : all;
                    }
                    finally { _resolving.Remove(name.Name); }
                }

            case StringLiteral s:
                return new List<string> { s.Value };

            case ConditionalExpression c:
                {
                    var yes = Literals(c.Consequent);
                    var no = Literals(c.Alternate);
                    if (yes == null || no == null) return null;
                    yes.AddRange(no);
                    return yes;
                }

            // `a + b` where both sides are enumerable: every combination, which is how a page builds
            // "duck hurt" out of two independent flags
            case NonLogicalBinaryExpression { Operator: Operator.Addition } b:
                {
                    var left = Literals(b.Left);
                    var right = Literals(b.Right);
                    if (left == null || right == null) return null;
                    if (left.Count * right.Count > 32) return null;   // a combinatorial blow-up is not a state set
                    var all = new List<string>();
                    foreach (var l in left)
                        foreach (var r in right)
                            all.Add(l + r);
                    return all;
                }

            // `cls.trim()` and the like: the shape survives, so look through it
            case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "trim" } } m }:
                {
                    var inner = Literals(m.Object);
                    if (inner == null) return null;
                    var trimmed = new List<string>();
                    foreach (var v in inner) trimmed.Add(v.Trim());
                    return trimmed;
                }

            case LogicalExpression { Operator: Operator.LogicalOr } l:
                {
                    var left = Literals(l.Left);
                    var right = Literals(l.Right);
                    if (left == null || right == null) return null;
                    left.AddRange(right);
                    return left;
                }

            default:
                return null;
        }
    }

    /// <summary>A short, recognisable rendering of an expression for a report.</summary>
    private static string Source(Node n) => n switch
    {
        Identifier i => i.Name,
        StringLiteral s => "'" + s.Value + "'",
        NumericLiteral v => v.Value.ToString(CultureInfo.InvariantCulture),
        MemberExpression { Computed: false, Property: Identifier p } m => Source(m.Object) + "." + p.Name,
        MemberExpression m2 => Source(m2.Object) + "[…]",
        NonLogicalBinaryExpression b => Source(b.Left) + " " + b.Operator + " " + Source(b.Right),
        CallExpression c => Source(c.Callee) + "(…)",
        _ => n.Type.ToString(),
    };

    // ---- setup versus runtime --------------------------------------------------------------------

    /// <summary>
    /// Spreads "runtime" along the call graph from every scheduled callback, then files each
    /// function's writes with the answer. A function is runtime when anything runtime calls it.
    /// </summary>
    private void Spread(Script ast)
    {
        // a scheduler call anywhere, including inside another function, seeds a root
        foreach (var n in All(ast))
        {
            if (n is not CallExpression call) continue;
            if (Scheduled(call) is { } named) Info(named).Runtime = true;
        }

        var changed = true;
        var guard = 0;
        while (changed && guard++ < 1000)
        {
            changed = false;
            foreach (var fn in _functions.Values.Where(f => f.Runtime).ToList())
                foreach (var called in fn.Calls)
                    if (_functions.TryGetValue(called, out var callee) && !callee.Runtime)
                    {
                        callee.Runtime = true;
                        changed = true;
                    }
        }

        foreach (var fn in _functions.Values)
            foreach (var w in fn.Writes)
            {
                w.Runtime = fn.Runtime;
                _writes.Add(w);
            }

        foreach (var w in _writes)
            if (w.Runtime && w.Id == null && w.Prefix == null)
                _notes.Add($"line {w.Line}: writes `{w.Property}` on an element chosen at run time (`{w.Computed}`)");
    }

    private FunctionInfo Info(string name)
    {
        if (!_functions.TryGetValue(name, out var info)) _functions[name] = info = new FunctionInfo();
        return info;
    }

    /// <summary>Every node below this one, including it. Does not descend into nested functions.</summary>
    private static IEnumerable<Node> All(Node n)
    {
        yield return n;
        foreach (var kid in n.ChildNodes)
        {
            if (kid == null) continue;
            if (kid is FunctionDeclaration or FunctionExpression or ArrowFunctionExpression) { yield return kid; continue; }
            foreach (var deep in All(kid)) yield return deep;
        }
    }
}

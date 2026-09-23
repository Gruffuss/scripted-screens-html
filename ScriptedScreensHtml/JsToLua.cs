using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Acornima;
using Acornima.Ast;

namespace ScriptedScreensHtml;

/// <summary>
/// A page's script, as Lua source the chip can run.
/// </summary>
/// <remarks>
/// This is the half of the compiler that handles what cannot be an expression. A vector expression
/// is pure in <c>t</c>, <c>i</c> and the payload, so anything with memory - a game's physics, a
/// rolling average, a spawn timer - has nowhere to live in the scene. It lives here instead, in the
/// Lua VM the chip already runs, writing the scene's slots exactly as a hand-written console does.
///
/// The point is that NOTHING of this mod is running when the page runs. The transpiler executes once
/// at load, hands over Lua source, and unloads with the rest of the HTML side. There is no
/// JavaScript engine at runtime, which is why the engine question stopped mattering.
///
/// It is not a JavaScript implementation and does not try to be. Measured across every page in this
/// repository, the language they use is 35 AST node types: no classes, generators, async,
/// destructuring, template literals or switch. Anything outside that set is <b>reported</b>, not
/// approximated - a page that cannot be compiled says which construct stopped it, because a
/// silently mistranslated page is far worse than one that refuses.
/// </remarks>
internal sealed class JsToLua
{
    private readonly StringBuilder _sb = new();
    private readonly List<string> _problems = new();
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    /// <summary>
    /// Names already declared at the top of the CURRENT scope, so their statement assigns rather
    /// than redeclaring. Saved and restored around every function body: an inner `let total = 0`
    /// that shares a name with an outer one must shadow it, not assign it.
    /// </summary>
    private HashSet<string> _hoisted = new(StringComparer.Ordinal);
    /// <summary>
    /// Top-level <c>const</c>s bound to an object or array literal and bound nowhere else, so every
    /// <c>NAME.key</c> in the page reads that table: see <see cref="Eager"/>.
    /// </summary>
    private readonly HashSet<string> _constTables = new(StringComparer.Ordinal);
    /// <summary>Top-level <c>const</c>s bound once to a literal Lua cannot read as false: see <see cref="NeverFalsy"/>.</summary>
    private readonly HashSet<string> _constTruthy = new(StringComparer.Ordinal);
    private int _depth;
    /// <summary>Counts loops so each label is unique.</summary>
    private int _loop;
    /// <summary>The label a `continue` here belongs to: the ENCLOSING loop, saved and restored.</summary>
    private int _enclosing;
    /// <summary>A source label to the loop it names, for `break outer` and `continue outer`.</summary>
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    /// <summary>Loops something jumped out of, so only those get a break label emitted after them.</summary>
    private readonly HashSet<int> _broke = new();
    /// <summary>Whether emission is in the script's own top-level statement list.</summary>
    private bool _top = true;
    /// <summary>The name `this` refers to, set only inside a getter body.</summary>
    private string? _receiver;

    /// <summary>Lua keywords, which a JS name or property of the same spelling has to be moved past.</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if",
        "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    };


    /// <summary>What the runtime prelude defines, so a reference to one is not reported as unknown.</summary>
    private static readonly HashSet<string> Provided = new(StringComparer.Ordinal)
    {
        "Math", "Number", "String", "Boolean", "JSON", "console", "document", "window",
        "localStorage", "performance", "Date", "isNaN", "parseFloat", "parseInt", "Infinity", "NaN",
        "undefined", "requestAnimationFrame", "setTimeout", "setInterval", "clearInterval", "Object",
        "location", "Map", "Set", "WeakMap", "WeakSet", "Error", "TypeError", "RangeError", "Array",
        "RegExp", "clearTimeout", "cancelAnimationFrame", "addEventListener", "removeEventListener",
        "getComputedStyle", "globalThis", "queueMicrotask", "Promise",
    };

    /// <summary>
    /// Names the chunk itself binds. A page declaring one of these would shadow it, and the failure
    /// would be the host finding no entry points at all rather than anything the page could see.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "PAGE", "PAGE_SYNC", "DOM", "Pending", "UNDEFINED",
        // The tables the compiler emits for the chunk to read: the element tree, the boxes, the
        // modifier state. A page declaring one of these would shadow it and the failure would look
        // like the DOM simply not working.
        "PARENT", "BOXES", "TAG", "CLASS", "NODES", "MODS", "EPOCH",
    };

    /// <summary>
    /// Every method the prelude implements. A page calling anything else would otherwise compile
    /// cleanly and then fail at run time with no line number in its own source - which is exactly
    /// the contract this compiler exists to keep, and the longest way it was being broken: the whole
    /// tail of `splice`, `shift`, `charCodeAt`, `Math.log2`, `Object.values` and the rest.
    /// A page's own method on its own object is not in here, so a called name is only reported when
    /// the page does not define a property of that name anywhere either.
    /// JsToLuaTests checks this list against JsPrelude.lua, so the two cannot drift apart quietly.
    /// </summary>
    internal static readonly HashSet<string> PreludeMethods = new(StringComparer.Ordinal)
    {
        "add", "addEventListener", "atan2", "charAt", "concat", "contains", "createElement",
        "endsWith", "entries", "every", "filter", "find", "forEach", "getElementById", "hypot",
        "includes", "indexOf", "isFinite", "isNaN", "join", "keys", "map", "padEnd", "padStart",
        "parseFloat", "pop", "push", "querySelector", "querySelectorAll", "reduce", "remove",
        "replace", "replaceAll", "round", "sign", "slice", "some", "sort", "split", "startsWith",
        "substring", "toFixed", "toLowerCase", "toString", "toUpperCase", "toggle", "trim", "trunc",
        // plain functions on the library tables, reached the same way
        "floor", "ceil", "abs", "sqrt", "sin", "cos", "tan", "atan", "asin", "acos", "exp", "log",
        "pow", "min", "max", "random", "assign", "stringify", "parse", "getItem", "setItem", "now",
        "log2", "getAttribute", "setAttribute", "warn", "error",
        // Map, Set and the other built-in constructors
        "get", "set", "has", "delete", "clear", "values", "getTime", "valueOf",
        // String and Array, the tail real pages reach for
        "at", "charCodeAt", "codePointAt", "fill", "findIndex", "findLast", "findLastIndex", "flat",
        "flatMap", "lastIndexOf", "localeCompare", "reverse", "search", "shift", "splice", "substr",
        "toExponential", "toLocaleLowerCase", "toLocaleUpperCase", "toPrecision", "trimEnd",
        "trimStart", "unshift",
        // The DOM: tree mutation, queries, attributes and events
        "appendChild", "insertBefore", "removeChild", "replaceChild", "append", "prepend", "after",
        "before", "replaceWith", "cloneNode", "getElementsByClassName", "getElementsByTagName",
        "closest", "matches", "hasAttribute", "removeAttribute", "toggleAttribute", "setProperty",
        "getPropertyValue", "removeProperty", "preventDefault", "stopPropagation",
        "stopImmediatePropagation", "removeEventListener", "dispatchEvent", "getBoundingClientRect",
        "createElementNS",
        // Object, Array and Math statics
        "fromEntries", "freeze", "hasOwn", "isArray", "of", "from", "isInteger", "cbrt", "log10",
        "clz32",
        // `repeat` is a Lua keyword, so the prelude spells it StringMethods["repeat"] and the
        // manifest check cannot see it. It goes in by hand or a page calling it is refused.
        "repeat",
        // Events, focus and the element queries
        "click", "focus", "blur", "insertAdjacentElement", "item",
        // Object, Number, Math and String statics
        "hasOwnProperty", "create", "defineProperty", "getOwnPropertyNames", "getPrototypeOf",
        "setPrototypeOf", "is", "seal", "isSafeInteger", "parseInt", "sinh", "cosh", "tanh",
        "asinh", "acosh", "atanh", "expm1", "log1p", "imul", "fromCharCode", "fromCodePoint",
        "test", "exec",
        // The regex engine and the library tail it unblocked
        "match", "matchAll", "normalize", "raw",
        "copyWithin", "reduceRight", "toReversed", "toSorted", "toSpliced", "with",
        "fround",
        // Date's calendar accessors, which answer against EPOCH and throw by name until it is set
        "getDate", "getDay", "getFullYear", "getHours", "getMilliseconds", "getMinutes",
        "getMonth", "getSeconds", "getTimezoneOffset", "toISOString",
        "toLocaleDateString", "toLocaleTimeString",
        "createTextNode", "createDocumentFragment", "insertAdjacentText",
        // Promise: the prelude's microtask queue, drained by the chunk's runtime tail, frame and event
        "then", "catch", "finally", "resolve", "reject", "all", "allSettled", "any", "race",
    };

    /// <summary>Property names the page itself defines, so its own methods are not reported as unknown.</summary>
    private readonly HashSet<string> _pageProperties = new(StringComparer.Ordinal);

    /// <summary>
    /// Compiles a page's script to Lua. Returns null when something in it cannot be translated, and
    /// <paramref name="problems"/> then names each one with its line.
    /// </summary>
    /// <summary>
    /// What compiling one <c>innerHTML</c> write produced, for the chunk to fill it: the reduction
    /// the scene was compiled from, and which of its values, states and click handlers reach it.
    /// </summary>
    /// <remarks>
    /// Supplied by the caller rather than worked out here, because the answer needs the page laid
    /// out and emitted and this file is deliberately Unity-free. The indices line up because the
    /// compiler and this translation read the same <see cref="Markup"/>.
    /// </remarks>
    internal sealed class MarkupPlan
    {
        public string Id = string.Empty;
        public Markup Markup = null!;
        /// <summary>Holes written on their own, as `innerHTML#index`: true for a number.</summary>
        public readonly Dictionary<int, bool> Holes = new();
        /// <summary>Text built from several holes, written whole: its key and its holes in order.</summary>
        public readonly List<(string Key, List<int> Holes)> Labels = new();
        /// <summary>Labels the scene prints itself from a placeholder per hole; set by the caller before translation.</summary>
        public readonly HashSet<string> Placed = new(StringComparer.Ordinal);
        /// <summary>Holes of placed labels that are `x.toFixed(n)`, written as the number: hole to n.</summary>
        public readonly Dictionary<int, int> Fixed = new();
        /// <summary>The states the chunk picks: `drive`, `choice#i`, `rows#i`.</summary>
        public readonly HashSet<string> States = new(StringComparer.Ordinal);
        /// <summary>What the drive reads, as JavaScript, when the markup has one.</summary>
        public string? Drive;
        /// <summary>Every element with a click handler, and the handler.</summary>
        public readonly List<(string Element, Markup.Push Push)> Clicks = new();
        /// <summary>The ids the markup itself gives its elements: each write replaces them.</summary>
        public readonly List<string> Ids = new();

        /// <summary>The key a plan is found by: the element and where its write starts in the script.</summary>
        public static string KeyOf(string id, int start) => id + "@" + start.ToString(CultureInfo.InvariantCulture);
    }

    private IReadOnlyDictionary<string, MarkupPlan>? _markup;
    /// <summary>Click handlers of compiled markup, registered once at the end of the chunk.</summary>
    private readonly List<string> _handlers = new();

    /// <summary>
    /// What a plain translation (<see cref="PlainTranslator"/>) takes over: the page's DOM, which it
    /// resolves at compile time, while the language stays this file's. Given hooks, the output is
    /// plain Lua for a hand-written console - no view models, no PAGE table, no DOM helpers.
    /// </summary>
    internal sealed class Hooks
    {
        /// <summary>An expression the caller writes itself, or null for the usual translation.</summary>
        public Func<JsToLua, Node, string?>? Expression;
        /// <summary>
        /// True when the caller emitted this itself: a statement, or an expression evaluated for its
        /// effect (an expression statement, a loop's update, a concise arrow's body).
        /// </summary>
        public Func<JsToLua, Node, bool>? Statement;
    }

    private Hooks? _hooks;

    /// <summary>For a hook: an expression translated as usual.</summary>
    internal string Translate(Node e) => Expr(e);
    /// <summary>For a hook: one line of output at the current depth.</summary>
    internal void Emit(string line) => Line(line);
    /// <summary>For a hook: something it cannot translate, reported with its line.</summary>
    internal void Refuse(Node n, string what) => Unsupported(n, what);

    internal static string? Compile(string source, out IReadOnlyList<string> problems,
                                    IReadOnlyDictionary<string, MarkupPlan>? markup = null, Hooks? hooks = null)
    {
        Script ast;
        try
        {
            ast = new Parser().ParseScript(source);
        }
        catch (Exception ex)
        {
            problems = new[] { "parse: " + ex.Message };
            return null;
        }
        return Compile(ast, out problems, markup, hooks);
    }

    internal static string? Compile(Script ast, out IReadOnlyList<string> problems,
                                    IReadOnlyDictionary<string, MarkupPlan>? markup = null, Hooks? hooks = null)
    {
        var c = new JsToLua();
        c._markup = markup;
        c._hooks = hooks;
        problems = c._problems;

        // Every name the page declares anywhere, collected before a line is emitted. Emission order
        // is not declaration order in JavaScript - a function written early may use a `const`
        // written later - so an in-order check would report a name the page does define. This
        // over-approximates scope deliberately: it exists to catch a typo or an unsupported global,
        // not to reproduce block scoping.
        c._script = ast;
        c.Declared(ast);
        // Which name holds which element, so `frame.innerHTML` knows it is writing to #frame. The
        // same shape DomWrites resolves, kept here because the markup rewrite needs it before any
        // line is emitted. Inside functions too - `render()` is where a page takes its frame - and a
        // name two functions bind to different elements names neither.
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Markup.Everything(ast))
            if (node is VariableDeclarator { Id: Identifier v, Init: CallExpression { Arguments.Count: 1 } call }
                && call.Arguments[0] is StringLiteral lit
                && call.Callee is Identifier or MemberExpression)
            {
                if (c._elements.TryGetValue(v.Name, out var had) && had != lit.Value) ambiguous.Add(v.Name);
                c._elements[v.Name] = lit.Value;
            }
        foreach (var name in ambiguous) c._elements.Remove(name);
        c.ConstTables(ast);
        // A view model is the old runtime's optimisation; plain Lua has none.
        if (hooks == null) c.Views(ast);

        // Everything the script's own scope binds is forward-declared and then assigned where it
        // stood. Two reasons, and the second is the one that bites: JavaScript hoists a function
        // declaration, so a page may call one before it is written; and a function written early may
        // close over a `const` written later, which is ordinary JavaScript and which a Lua `local`
        // declared at that point cannot see - the function would read a global that is never set.
        // Only this outermost scope is hoisted: lifting a `let` out of a loop would change which
        // binding a closure inside it captures.
        var names = Hoistable(ast.Body);
        foreach (var n in names)
            if (Reserved.Contains(n))
                c._problems.Add("the page declares `" + n + "`, which the compiled chunk needs for itself");
        // `||`, `&&` and `??` whose right side is evaluated as it stands (Eager). No page name can
        // collide: Safe() spells every `_` of a page's own name as `_5f`.
        string[] valueOps =
        {
            "local function or_v(a, b) if js_truthy(a) then return a end return b end",
            "local function and_v(a, b) if js_truthy(a) then return b end return a end",
            "local function nc_v(a, b) if a == nil then return b end return a end",
        };
        if (hooks == null) foreach (var op in valueOps) c.Line(op);
        c.OpenScope(names);

        foreach (var s in ast.Body)
        {
            if (s is Statement st) c.Statement(st);
        }

        if (hooks != null)
        {
            // Plain Lua carries only the helpers it calls, and no PAGE table: nothing inspects it.
            if (c._problems.Count > 0) return null;
            var body = c._sb.ToString();
            var head = new StringBuilder();
            foreach (var op in valueOps)
                if (body.Contains(op.Substring(15, op.IndexOf('(') - 14), StringComparison.Ordinal)) head.Append(op).Append('\n');
            return head.Append(body).ToString();
        }

        // Compiled markup's click handlers, each registered once on the element it belongs to. They
        // read what the render that drew them kept, so pressing allocates nothing the page did not.
        foreach (var h in c._handlers) c.Line(h);

        // The page's whole top-level scope, by name, for the host to reach: the chunk's own bindings
        // are locals, so without this its frame callback is unreachable from outside it. State goes
        // in as well as functions - a table is by reference and so stays live, which is what lets a
        // compiled page be inspected while it runs.
        // A SNAPSHOT of the locals, plus a way to take another. The values are copied out once here,
        // so anything a name holds AFTER the top level has run - which is every value an event
        // handler, a timer or a frame produces - was invisible to anything reading PAGE. A number is
        // copied by value, and a page whose counter is a number simply reported its starting value
        // for ever.
        //
        // Found because a coverage probe read PAGE after driving the page and measured 5 of 22 event
        // members: four of the five that "passed" did so because the value before the tail happened
        // to equal the expected one, and would have passed with the event system deleted. A test
        // that passes against a deleted feature is worse than no test.
        c.Line("PAGE = {}");
        c.Line("function PAGE_SYNC()");
        foreach (var n in names) c.Line("  PAGE." + n + " = " + n);
        c.Line("end");
        c.Line("PAGE_SYNC()");

        return c._problems.Count == 0 ? c._sb.ToString() : null;
    }

    private void Line(string text) => _sb.Append(' ', _depth * 2).Append(text).Append('\n');

    /// <summary>
    /// What a scope has to declare up front. Three kinds, and each has its own reason:
    /// a <b>function declaration</b> because JavaScript hoists it and a page may call one written
    /// later; a <b>var</b> because it is function-scoped where a Lua local is block-scoped, so one
    /// declared inside an `if` has to outlive it; and a <b>let/const at the scope's own statement
    /// list</b> because a function written above it may close over it. A `let` deeper in - inside a
    /// loop or an `if` - is left alone, since lifting it out would change which binding a closure
    /// captures.
    /// </summary>
    private static List<string> Hoistable(in NodeList<Statement> body)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string name)
        {
            var safe = Safe(name);
            if (seen.Add(safe)) names.Add(safe);
        }

        foreach (var s in body)
        {
            if (s is FunctionDeclaration { Id: { } id }) Add(id.Name);
            else if (s is ClassDeclaration { Id: { } cid }) Add(cid.Name);
            else if (s is VariableDeclaration vd)
                foreach (var d in vd.Declarations)
                    if (d.Id is Identifier vid) Add(vid.Name);
        }

        // `var` and nested function declarations anywhere below, but not through another function
        foreach (var s in body)
            foreach (var n in Walk(s))
            {
                if (n is FunctionDeclaration { Id: { } deep }) Add(deep.Name);
                else if (n is VariableDeclaration { Kind: VariableDeclarationKind.Var } dv)
                    foreach (var d in dv.Declarations)
                        if (d.Id is Identifier dvid) Add(dvid.Name);
            }
        return names;
    }

    /// <summary>Declares a scope's hoisted names and makes their statements assign. Returns the scope it replaced.</summary>
    private HashSet<string> OpenScope(List<string> names)
    {
        var previous = _hoisted;
        _hoisted = new HashSet<string>(names, StringComparer.Ordinal);
        // forty to a line: Lua caps locals per function and one long line is unreadable
        for (var i = 0; i < names.Count; i += 40)
            Line("local " + string.Join(", ", names.GetRange(i, Math.Min(40, names.Count - i))));
        return previous;
    }

    /// <summary>Walks the whole tree for every name the page binds, so the unknown-name check is order-free.</summary>
    private void Declared(Node n)
    {
        if (n is VariableDeclarator { Id: Identifier v }) _known.Add(v.Name);
        if (n is FunctionDeclaration fd)
        {
            if (fd.Id != null) _known.Add(fd.Id.Name);
            Parameters(fd.Params);
        }
        // A class binds its name, and its members are names the page may call on an instance - the
        // same reason an object literal's keys are collected below. Without this every `obj.tick()`
        // on a page's own class was reported as a method the prelude does not provide.
        if (n is ClassDeclaration { Id: { } cid }) _known.Add(cid.Name);
        if (n is MethodDefinition { Computed: false, Key: Identifier mk }) _pageProperties.Add(mk.Name);
        if (n is PropertyDefinition { Computed: false, Key: Identifier fk }) _pageProperties.Add(fk.Name);
        if (n is FunctionExpression fe) Parameters(fe.Params);
        if (n is ArrowFunctionExpression ae) Parameters(ae.Params);
        if (n is CatchClause { Param: Identifier c }) _known.Add(c.Name);
        // a property the page writes is a method it may call, so it is not an unknown one
        if (n is ObjectProperty { Key: Identifier pk }) _pageProperties.Add(pk.Name);
        if (n is ObjectProperty { Key: StringLiteral sk }) _pageProperties.Add(sk.Value);
        if (n is AssignmentExpression { Left: MemberExpression { Computed: false, Property: Identifier ak } }) _pageProperties.Add(ak.Name);
        foreach (var kid in n.ChildNodes) if (kid != null) Declared(kid);

        void Parameters(in NodeList<Node> ps)
        {
            foreach (var p in ps) if (p is Identifier pid) _known.Add(pid.Name);
        }
    }

    /// <summary>
    /// <c>el.style.transform = 'translate(' + x + 'px,' + y + 'px)'</c>, emitted as the two numbers.
    /// </summary>
    /// <remarks>
    /// This is the whole remaining per-frame allocation of a compiled page, and it is one the
    /// compiler creates itself. The page writes CSS, so the translated Lua built the string - a
    /// <c>toFixed</c>, then three or four <c>js_add</c>s - and the binding runtime then pattern
    /// matched the numbers back out of it, allocating the captures. Twenty-six times a frame, per
    /// console, to move numbers the compiler had in its hands the whole time.
    ///
    /// So the literal pieces are recognised at compile time and dropped. Nothing is built and
    /// nothing is parsed: the value goes to its slot as a number.
    ///
    /// Only the exact shapes below, and only when every hole is an expression rather than a literal
    /// piece. Anything else falls through to the string path, which still works - this is an
    /// optimisation with a correctness floor, not a new way of writing CSS.
    /// </remarks>
    private bool NumericStyle(AssignmentExpression a)
    {
        if (a.Left is not MemberExpression { Computed: false, Property: Identifier prop } m) return false;
        if (m.Object is not MemberExpression { Computed: false, Property: Identifier { Name: "style" } } styleOf) return false;

        var parts = new List<object>();
        if (!Flatten(a.Right, parts)) return false;

        // literal pieces in order, expressions in order
        var literals = new List<string>();
        var holes = new List<Expression>();
        foreach (var part in parts)
        {
            if (part is string lit) literals.Add(lit);
            else holes.Add((Expression)part);
        }

        var element = Expr(styleOf.Object);
        var key = Quote("style." + prop.Name);   // the whole key, so the runtime never builds one

        // A hole that is `x.toFixed(n)` is a number a page formatted for CSS. On a numeric slot the
        // string it returns is built only to be parsed back, so the value is taken without it.
        string Hole(Expression e) =>
            e is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toFixed" } } fixedOn } call
                ? "js_fixnum(" + Expr(fixedOn.Object) + ", " + (call.Arguments.Count > 0 ? Expr(call.Arguments[0]) : "0") + ")"
                : Expr(e);

        // How many decimals the page asked toFixed for, so the uncompiled runtime can format the
        // value back into exactly the string the original wrote. The compiled one ignores it.
        string Digits(Expression e) =>
            e is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toFixed" } } } c2
                ? (c2.Arguments.Count > 0 ? Expr(c2.Arguments[0]) : "0")
                : "nil";

        // `x + 'px'` - one number and a unit
        if (holes.Count == 1 && literals.Count == 1 && Unit(literals[0]))
        {
            Line("DOM.num(" + element + ", " + key + ", " + Hole(holes[0]) + ", " + Quote(literals[0]) + ", " + Digits(holes[0]) + ")");
            return true;
        }

        // `'translate(' + x + 'px,' + y + 'px)'`
        if (holes.Count == 2 && literals.Count == 3
            && Head(literals[0], "translate(") && Mid(literals[1]) && Tail(literals[2]))
        {
            Line("DOM.xy(" + element + ", " + key + ", " + Hole(holes[0]) + ", " + Hole(holes[1]) + ", "
                 + Quote(literals[0]) + ", " + Quote(literals[1]) + ", " + Quote(literals[2])
                 + ", " + Digits(holes[0]) + ", " + Digits(holes[1]) + ")");
            return true;
        }

        // `'translateX(' + x + 'px)'` / `'translateY(' + y + 'px)'`
        if (holes.Count == 1 && literals.Count == 2 && Tail(literals[1]))
        {
            if (Head(literals[0], "translatex(")) { Line("DOM.xy(" + element + ", " + key + ", " + Hole(holes[0]) + ", nil, " + Quote(literals[0]) + ", \"\", " + Quote(literals[1]) + ", " + Digits(holes[0]) + ", nil)"); return true; }
            if (Head(literals[0], "translatey(")) { Line("DOM.xy(" + element + ", " + key + ", nil, " + Hole(holes[0]) + ", " + Quote(literals[0]) + ", \"\", " + Quote(literals[1]) + ", nil, " + Digits(holes[0]) + ")"); return true; }
        }
        return false;

        static bool Unit(string t) { t = t.Trim(); return Is(t, "px") || Is(t, "%") || t.Length == 0; }
        static bool Head(string t, string want) => Is(t.Trim(), want);
        static bool Mid(string t) { t = t.Trim(); return Is(t, "px,") || Is(t, "px ,"); }
        static bool Tail(string t) => Is(t.Trim(), "px)");
        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// `'border:1px solid ' + (on ? '#94bce3' : '#232c37') + ';padding:9px'`: a concatenation of
    /// literal strings and ternaries between them, as the finished strings it can produce - a string
    /// per outcome, chosen by the same tests in the same order, and none built at run time. Every
    /// style a page assembles from a flag was a fresh string per render; each outcome here is a
    /// constant. Null past sixteen outcomes, or when anything in it is not a literal string.
    /// </summary>
    /// <remarks>
    /// An outcome is a string or a <c>(test, then, else)</c> triple of outcomes. The tests are the
    /// page's own, evaluated once each on the path taken, left to right, as JavaScript evaluates them.
    /// </remarks>
    private static object? Fold(Expression e, out int outcomes)
    {
        outcomes = 0;
        var folded = Of(e);
        if (folded == null) return null;
        outcomes = Count(folded);
        return outcomes <= 16 ? folded : null;

        static object? Of(Expression e) => e switch
        {
            StringLiteral s => s.Value,
            ParenthesizedExpression p => Of(p.Expression),
            ConditionalExpression c => Of(c.Consequent) is { } yes && Of(c.Alternate) is { } no ? (c.Test, yes, no) : null,
            NonLogicalBinaryExpression { Operator: Operator.Addition } add
                => Of(add.Left) is { } left && Of(add.Right) is { } right && Count(left) * Count(right) <= 16 ? Join(left, right) : null,
            _ => null,
        };

        // Each outcome of the left, followed by each of the right: the left's tests run first.
        static object Join(object left, object right) => left switch
        {
            string l => Prefix(l, right),
            (Expression test, object yes, object no) => (test, Join(yes, right), Join(no, right)),
            _ => throw new InvalidOperationException(),
        };

        static object Prefix(string l, object right) => right switch
        {
            string r => l + r,
            (Expression test, object yes, object no) => (test, Prefix(l, yes), Prefix(l, no)),
            _ => throw new InvalidOperationException(),
        };

        static int Count(object o) => o is (Expression, object yes, object no) ? Count(yes) + Count(no) : 1;
    }

    /// <summary>A folded concatenation as Lua: nested `and`/`or` over constant strings, which are never false.</summary>
    private string Choose(object folded) => folded switch
    {
        string s => Quote(s),
        (Expression test, object yes, object no) => "(" + Truthy(test) + " and " + Choose(yes) + " or " + Choose(no) + ")",
        _ => throw new InvalidOperationException(),
    };

    /// <summary>A `+` chain as its pieces, in order. False when any piece is not a literal or a value.</summary>
    private static bool Flatten(Expression e, List<object> into)
    {
        switch (e)
        {
            case NonLogicalBinaryExpression { Operator: Operator.Addition } add:
                return Flatten(add.Left, into) && Flatten(add.Right, into);
            case StringLiteral s:
                into.Add(s.Value);
                return true;
            case NumericLiteral:
                return false;            // a literal number in the chain is not a hole to bind
            default:
                into.Add(e);
                return true;
        }
    }

    private void Unsupported(Node n, string what) =>
        _problems.Add("line " + n.Location.Start.Line.ToString(CultureInfo.InvariantCulture) + ": " + what + " is not translatable");

    private string Fail(Node n, string what)
    {
        Unsupported(n, what);
        return "nil";
    }

    // ---- statements --------------------------------------------------------------------------

    private void Statement(Statement s)
    {
        if (_hooks?.Statement?.Invoke(this, s) == true) return;
        switch (s)
        {
            case VariableDeclaration v:
                foreach (var d in v.Declarations)
                {
                    if (d.Id is ObjectPattern or ArrayPattern) { Destructure(d.Id, d.Init, declare: true); continue; }
                    if (d.Id is not Identifier id) { Unsupported(d, "a binding that is not a plain name"); continue; }
                    var init = d.Init == null ? "nil" : Expr(d.Init);
                    _known.Add(id.Name);
                    var name = Safe(id.Name);
                    // `_hoisted` only ever holds the script's own top-level names, so it may only be
                    // consulted there. An inner `let total = 0` that happens to share a name with a
                    // top-level one would otherwise assign the OUTER binding - silently, and
                    // corrupting state in a part of the page that never mentions it.
                    Line((_hoisted.Contains(name) ? "" : "local ") + name + " = " + init);
                }
                break;

            case FunctionDeclaration f when f.Id != null && _views.TryGetValue(f, out var view):
                LazyView(f.Id.Name, view);
                break;

            case FunctionDeclaration f when f.Id != null:
                // At the top level this is forward-declared, so it assigns. Anywhere else it is a
                // `local function`, or it would overwrite - and destroy - an outer function of the
                // same name for the rest of the page.
                // always an assignment: every scope declares its own function names up front, so a
                // nested one shadows rather than overwriting an outer function of the same name
                Line(Safe(f.Id.Name) + " = function(" + Params(f.Params, out var fnBind) + ")");
                {
                    var hidden = Shadow(f.Params, f.Body);
                    Body(f.Body, fnBind);
                    Unshadow(hidden);
                }
                Line("end");
                break;

            case ExpressionStatement e:
                ExprStatement(e.Expression);
                break;

            case IfStatement i:
                Line("if " + Truthy(i.Test) + " then");
                _depth++; Statement(i.Consequent); _depth--;
                if (i.Alternate != null) { Line("else"); _depth++; Statement(i.Alternate); _depth--; }
                Line("end");
                break;

            case BlockStatement b:
                foreach (var inner in b.Body) if (inner is Statement st) Statement(st);
                break;

            case ReturnStatement r when _tryReturn is { } carry:
                // Inside a pcall closure: record the value and leave the closure. Try() does the
                // real return afterwards, or the page's value would be swallowed by the pcall.
                Line(carry.Flag + ", " + carry.Value + " = true, " + (r.Argument == null ? "nil" : Expr(r.Argument)));
                Line("return");
                break;

            case ReturnStatement r:
                // `do ... end` because Lua allows `return` only as a block's last statement, and an
                // early return in the middle of a JS function is ordinary.
                Line(r.Argument == null ? "do return end" : "do return " + Expr(r.Argument) + " end");
                break;

            case ForStatement fo:
                ForLoop(fo);
                break;

            case ForOfStatement fof:
                ForOf(fof);
                break;

            case ForInStatement fin:
                ForIn(fin);
                break;

            case WhileStatement w:
                {
                    var mine = ++_loop;
                    Line("while " + Truthy(w.Test) + " do");
                    _depth++;
                    var outer = _enclosing; _enclosing = mine;
                    var wasTop = _top; _top = false;
                    Statement(w.Body);
                    Line("::continue" + mine.ToString(CultureInfo.InvariantCulture) + "::");
                    _enclosing = outer;
                    _top = wasTop;
                    _depth--;
                    Line("end");
                    break;
                }

            case DoWhileStatement dw:
                {
                    // Lua's `repeat ... until c` is `do ... while (!c)`, and its body shares a scope
                    // with the condition - which is what makes `repeat local x = f() until x` legal.
                    var mine = ++_loop;
                    Line("repeat");
                    _depth++;
                    var outer = _enclosing; _enclosing = mine;
                    var wasTop = _top; _top = false;
                    Statement(dw.Body);
                    Line("::continue" + mine.ToString(CultureInfo.InvariantCulture) + "::");
                    _enclosing = outer;
                    _top = wasTop;
                    _depth--;
                    Line("until not (" + Truthy(dw.Test) + ")");
                    break;
                }

            case SwitchStatement sw:
                Switch(sw);
                break;

            case ThrowStatement th:
                // Lua's error() carries any value, so a thrown object arrives at the catch intact.
                // Level 0 keeps the page's own message from being prefixed with a chunk position
                // that means nothing to whoever wrote the page.
                Line("error(" + Expr(th.Argument) + ", 0)");
                break;

            case LabeledStatement labelled:
                {
                    // `outer: for (...) { ... break outer; }`. Lua has no labelled loop, but it has
                    // goto - so the label becomes a target placed AFTER the loop, and a labelled
                    // break jumps there. The loop this label names is the next one to be numbered,
                    // which is why the name is recorded before the body is emitted.
                    var named = _loop + 1;
                    _labels[labelled.Label.Name] = named;
                    Statement(labelled.Body);
                    // Only emitted when something actually jumped here: Lua rejects a label that is
                    // the last statement of a block, and an unused one is noise in the output.
                    if (_broke.Remove(named))
                        Line("::break" + named.ToString(CultureInfo.InvariantCulture) + "::");
                    _labels.Remove(labelled.Label.Name);
                    break;
                }

            case BreakStatement { Label: { } target } when _labels.TryGetValue(target.Name, out var toLoop):
                _broke.Add(toLoop);
                Line("goto break" + toLoop.ToString(CultureInfo.InvariantCulture));
                break;

            case ContinueStatement { Label: { } target } when _labels.TryGetValue(target.Name, out var toLoop):
                Line("goto continue" + toLoop.ToString(CultureInfo.InvariantCulture));
                break;

            case BreakStatement { Label: { } unknown }:
                Unsupported(s, "`break " + unknown.Name + "`, which names no enclosing loop");
                break;

            case ContinueStatement { Label: { } unknown }:
                Unsupported(s, "`continue " + unknown.Name + "`, which names no enclosing loop");
                break;

            case ContinueStatement when _enclosing == 0:
                Unsupported(s, "`continue` outside a loop");
                break;

            case ContinueStatement:
                // the ENCLOSING loop, not the highest-numbered one: a loop that has already closed
                // left its counter behind, and jumping to its label is either wrong or unreachable
                Line("goto continue" + _enclosing.ToString(CultureInfo.InvariantCulture));
                break;

            case BreakStatement:
                Line("break");
                break;

            case TryStatement t:
                Try(t);
                break;

            case EmptyStatement:
                break;

            case ClassDeclaration cd when cd.Id != null:
                _known.Add(cd.Id.Name);
                var declared = Safe(cd.Id.Name);
                Line((_hoisted.Contains(declared) ? "" : "local ") + declared + " = " + Class(cd, cd.Id.Name));
                break;

            default:
                Unsupported(s, s.Type.ToString());
                break;
        }
    }

    // ---- classes -----------------------------------------------------------------------------

    /// <summary>How deeply classes are nested, so each one's base has a name of its own.</summary>
    private int _classes;

    /// <summary>The Lua name holding the current class's base, or null outside a class body.</summary>
    private string? _superBase;

    /// <summary>
    /// A class, as one expression.
    /// </summary>
    /// <remarks>
    /// This is the largest single gap the corpus had: <c>class</c> blocks eighteen pages, more than
    /// twice anything else, and it is ordinary JavaScript rather than a corner of the language.
    ///
    /// The shape is a closure that names the base and returns a built class, so a declaration and an
    /// expression are the same code and <c>super</c> has something to refer to. Methods, accessors
    /// and statics go to <see cref="Prelude"/>'s <c>js_class</c> as separate tables because they are
    /// separate namespaces in JavaScript: a static <c>make</c> and an instance <c>make</c> are two
    /// different functions, and flattening them into one table would silently make them one.
    ///
    /// <b>Field initialisers run before the constructor body, not interleaved with it.</b> JavaScript
    /// runs a derived class's fields after <c>super()</c> returns and before the rest of its
    /// constructor; here every class's fields run first, base to derived. The two differ only if a
    /// base constructor calls a method the derived class overrides AND that method reads a derived
    /// field - which is a pattern worth not writing anyway. Said here rather than left to be found.
    /// </remarks>
    private string Class(IClass node, string name)
    {
        var baseName = "__base" + (++_classes).ToString(CultureInfo.InvariantCulture);
        var hadSuper = _superBase;
        _superBase = node.SuperClass != null ? baseName : null;

        var methods = new List<string>();
        var getters = new List<string>();
        var setters = new List<string>();
        var statics = new List<string>();
        var fields = new List<string>();
        var blocks = new List<string>();

        foreach (var element in node.Body.Body)
        {
            switch (element)
            {
                case MethodDefinition m:
                    {
                        var key = KeyOf(m.Key, m.Computed, m);
                        if (key == null) continue;
                        if (m.Value is not FunctionExpression fn) { Unsupported(m, "a class member that is not a function"); continue; }
                        // A static method has no instance, so it takes no receiver - and `this`
                        // inside one refers to the class, which is reported rather than guessed.
                        var lambda = Lambda(fn.Params, fn.Body, m.Static ? null : Self);
                        var entry = "[" + Quote(m.Kind == PropertyKind.Constructor ? "__ctor" : key) + "] = " + lambda;
                        (m.Kind switch
                        {
                            PropertyKind.Get => getters,
                            PropertyKind.Set => setters,
                            _ => m.Static ? statics : methods,
                        }).Add(entry);
                        break;
                    }

                case PropertyDefinition p:
                    {
                        var key = KeyOf(p.Key, p.Computed, p);
                        if (key == null) continue;
                        var value = p.Value == null ? "nil" : Expr(p.Value);
                        if (p.Static) statics.Add("[" + Quote(key) + "] = " + value);
                        else fields.Add(Self + "[" + Quote(key) + "] = " + value);
                        break;
                    }

                case StaticBlock block:
                    // `static { ... }` runs once when the class is defined, with `this` bound to the
                    // class itself - so it is emitted as a function handed the class after js_class
                    // has built it, rather than as part of any instance.
                    blocks.Add(Lambda(new NodeList<Node>(), block, Self));
                    break;

                default:
                    Unsupported(element, element.Type.ToString());
                    break;
            }
        }

        // Fields are a function on the prototype rather than lines inside the constructor, because a
        // class with no constructor of its own still has to initialise them.
        if (fields.Count > 0)
        {
            var pad = new string(' ', (_depth + 2) * 2);
            methods.Add("[\"__fields\"] = function(" + Self + ")\n" + pad
                        + string.Join("\n" + pad, fields) + "\n" + new string(' ', (_depth + 1) * 2) + "end");
        }

        _superBase = hadSuper;
        _classes--;

        var indent = new string(' ', (_depth + 1) * 2);
        var sb = new StringBuilder("(function()\n");
        sb.Append(indent).Append("local ").Append(baseName).Append(" = ")
          .Append(node.SuperClass == null ? "nil" : Expr(node.SuperClass)).Append('\n');
        var built = "js_class(" + Quote(name) + ", " + baseName
                  + ", " + Table(methods)
                  + ", " + Table(getters)
                  + ", " + Table(setters)
                  + ", " + Table(statics) + ")";
        if (blocks.Count == 0)
        {
            sb.Append(indent).Append("return ").Append(built).Append('\n');
        }
        else
        {
            var cls = "__cls" + (_classes + 1).ToString(CultureInfo.InvariantCulture);
            sb.Append(indent).Append("local ").Append(cls).Append(" = ").Append(built).Append('\n');
            // Leading semicolon: Lua reads `local x = f()\n(g)(x)` as calling the result of f, so a
            // statement starting with a parenthesis has to be separated explicitly.
            foreach (var block in blocks)
                sb.Append(indent).Append(";(").Append(block).Append(")(").Append(cls).Append(")\n");
            sb.Append(indent).Append("return ").Append(cls).Append('\n');
        }
        return sb.Append(new string(' ', _depth * 2)).Append("end)()").ToString();

        static string Table(List<string> entries) => entries.Count == 0 ? "nil" : "{ " + string.Join(", ", entries) + " }";
    }

    /// <summary>The receiver name inside a method. Not `self`, which a page may well declare.</summary>
    private const string Self = "__self";

    /// <summary>A member's name, or null when it is computed - which cannot be resolved here.</summary>
    private string? KeyOf(Node key, bool computed, Node at)
    {
        if (computed) { Unsupported(at, "a computed member name"); return null; }
        var name = key switch
        {
            Identifier i => i.Name,
            StringLiteral s => s.Value,
            NumericLiteral n => Number(n.Value),
            PrivateIdentifier p => "#" + p.Name,
            _ => null,
        };
        if (name == null) Unsupported(at, "a member name that is not a plain name");
        return name;
    }

    /// <summary>An expression used for its effect. Lua allows only a call as a statement, so everything else is shaped into one.</summary>
    private void ExprStatement(Expression e)
    {
        if (_hooks?.Statement?.Invoke(this, e) == true) return;
        switch (e)
        {
            case AssignmentExpression a:
                Assign(a);
                break;

            case UpdateExpression u when u.Argument is Identifier or MemberExpression:
                var target = Expr(u.Argument);
                Line(target + " = " + target + (u.Operator == Operator.Increment ? " + 1" : " - 1"));
                break;

            case CallExpression:
                Line(Expr(e));
                break;

            default:
                // neither: keep whatever it does, discard what it yields. The name is one no
                // page can declare, since Safe() escapes every character Lua would reject.
                Line("local __discard = " + Expr(e));
                break;
        }
    }

    /// <summary>
    /// <c>el.innerHTML = …</c>, as writes to the slots its holes land on rather than a document.
    /// </summary>
    /// <remarks>
    /// This is what compiling innerHTML MEANS. The obvious translation - build the same string in
    /// Lua and hand it to the same parser - moves the work rather than removing it: the chip would
    /// allocate a whole document every tick and the page would still be re-parsed. Here the literal
    /// markup became scene structure at compile time and only the interpolated values are left, so
    /// a tick writes a handful of numbers.
    ///
    /// A hole the compiler could not place is REPORTED rather than dropped. Dropping it would draw a
    /// page that is almost right, with one reading frozen at whatever it was when the page loaded,
    /// which is the hardest kind of wrong to notice.
    /// </remarks>
    private bool InnerHtml(AssignmentExpression a)
    {
        if (_markup == null) return false;
        if (a.Left is not MemberExpression { Computed: false, Property: Identifier { Name: "innerHTML" }, Object: { } owner })
            return false;
        if (ElementId(owner) is not { } id || !_markup.TryGetValue(MarkupPlan.KeyOf(id, a.Range.Start), out var plan)) return false;
        var m = plan.Markup;

        // The handlers first, for the names they keep: a handler runs after this render has
        // returned, so what it reads of the render's locals and rows is put where it can find it.
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        string Keep(string name)
        {
            if (!kept.TryGetValue(name, out var holder))
            {
                holder = "mkk_" + Clean(id) + "_" + Clean(name);
                kept[name] = holder;
                _known.Add(holder);
            }
            return holder;
        }
        // Registered once, after the render below says which of them it can mark as drawn.
        var handlers = new List<(string Element, Markup.Push Push, string Fn)>();
        foreach (var (element, push) in plan.Clicks)
        {
            if (m.Handler(push.Handler, Keep) is not { } body)
            {
                Unsupported(a, "the click handler of \"" + element + "\" (" + m.Text(push.Handler) + ")");
                continue;
            }
            if (Parsed("(function () {\n" + body + "\n})", a) is not FunctionExpression fn) continue;
            var depth = _depth;
            _depth = 0;
            handlers.Add((element, push, Expr(fn)));
            _depth = depth;
        }
        var clickOf = new Dictionary<Markup.Push, string>();
        foreach (var (element, push, _) in handlers) clickOf[push] = element;
        var drawn = new HashSet<string>(StringComparer.Ordinal);
        var render = "DOM.renders[" + Quote(id) + "]";

        Line("-- #" + id + ": its markup is structure in the scene; only what fills it is written here");
        Line("do");
        _depth++;
        // Which render this is. An element this render writes is marked with it (the Push case
        // below), so its handler can tell whether the element exists now.
        Line(render + " = (" + render + " or 0) + 1");
        // The write replaces every element the markup names, and a listener dies with its element. A
        // compiled page keeps one element per id for ever, so a render that adds a listener after
        // this - `list.addEventListener('scroll', ...)` - added one more every render instead: a
        // list that grew without bound and was scanned whole on every registration.
        if (plan.Ids.Count > 0)
            Line("DOM.replaced(" + string.Join(", ", plan.Ids.ConvertAll(Quote)) + ")");
        foreach (var (name, holder) in kept)
            if (m.IsLocal(name)) Line(Safe(holder) + " = " + Safe(name));
        if (plan.States.Contains("drive") && plan.Drive != null && Value(plan.Drive, a) is { } drive)
            Line("DOM.bind(" + Quote(id) + ", \"drive\", " + drive + ")");
        var labels = new Dictionary<int, List<(string Key, List<int> Holes)>>();
        foreach (var label in plan.Labels)
            foreach (var h in label.Holes)
            {
                if (!labels.TryGetValue(h, out var of)) labels[h] = of = new List<(string, List<int>)>();
                of.Add(label);
            }
        MarkupParts(m.Parts);
        _depth--;
        Line("end");
        // A handler runs only while the last render drew its element. A browser's element - and its
        // listener - is gone when a render leaves it out, but a compiled page registers every handler
        // once, so a row past the list's current length, or a side the render did not take, kept a
        // live handler reading an item that no longer exists: "attempt to index a nil value".
        foreach (var (element, _, fn) in handlers)
            _handlers.Add(drawn.Contains(element)
                ? "DOM.on(" + Quote(element) + ", \"click\", DOM.drawn(" + Quote(element) + ", " + Quote(id) + ", " + fn + "))"
                : "DOM.on(" + Quote(element) + ", \"click\", " + fn + ")");
        return true;

        void MarkupParts(List<Markup.Part> parts)
        {
            foreach (var part in parts)
            {
                switch (part)
                {
                    case Markup.Push p when clickOf.TryGetValue(p, out var drawnHere):
                        Line("DOM.live[" + Quote(drawnHere) + "] = " + render);
                        drawn.Add(drawnHere);
                        break;
                    case Markup.Hole h:
                        {
                            var hole = h.Index.ToString(CultureInfo.InvariantCulture);
                            if (plan.Holes.TryGetValue(h.Index, out var numeric) && HoleValue(m.Js(h.Value), numeric, h) is { } v)
                                Line("DOM.bind(" + Quote(id) + ", " + Quote("innerHTML#" + hole) + ", " + v + ")");
                            if (!labels.TryGetValue(h.Index, out var of)) break;
                            // A piece of a label goes into a local, and each label is written once,
                            // after the last of its pieces: one text slot is the whole label.
                            // A label the scene prints itself takes `x.toFixed(n)` as the number,
                            // rounded as toFixed rounds, and formats it with the same n: the string
                            // was built each render only to be copied into the label.
                            var js = m.Js(h.Value);
                            var digits = of.TrueForAll(l => plan.Placed.Contains(l.Key)) ? FixedDigits(js) : null;
                            if (HoleValue(js, digits != null, h) is not { } piece) break;
                            if (digits is { } d) plan.Fixed[h.Index] = d;
                            Line("local mkh" + hole + " = " + piece);
                            foreach (var (key, holes) in of)
                            {
                                if (holes[holes.Count - 1] != h.Index) continue;
                                var args = new StringBuilder();
                                foreach (var arg in holes) args.Append(", mkh").Append(arg.ToString(CultureInfo.InvariantCulture));
                                Line("DOM.label(" + Quote(id) + ", " + Quote(key) + args + ")");
                            }
                            break;
                        }
                    case Markup.Choice c:
                        {
                            var key = "choice#" + c.Index.ToString(CultureInfo.InvariantCulture);
                            if (m.Js(c.Test) is not { } test || Parsed("(" + test + ")", a) is not Expression t)
                            {
                                Unsupported(a, "the choice `" + m.Text(c.Test) + "`");
                                break;
                            }
                            // Written only when something depends on it: a side that writes nothing
                            // and a choice that is not a state of its own need no test at all.
                            var bind = plan.States.Contains(key);
                            var then = Lines(() => { if (bind) Line("DOM.bind(" + Quote(id) + ", " + Quote(key) + ", 1)"); MarkupParts(c.Then); });
                            var otherwise = Lines(() => { if (bind) Line("DOM.bind(" + Quote(id) + ", " + Quote(key) + ", 0)"); MarkupParts(c.Else); });
                            if (then.Length == 0 && otherwise.Length == 0) break;
                            Line("if " + Truthy(t) + " then");
                            _sb.Append(then);
                            if (otherwise.Length > 0) { Line("else"); _sb.Append(otherwise); }
                            Line("end");
                            break;
                        }
                    case Markup.Rows r:
                        {
                            var n = (++_loop).ToString(CultureInfo.InvariantCulture);
                            var list = "mkl" + n;
                            var count = "mkn" + n;
                            _known.Add(list);
                            _known.Add(count);
                            if (m.Js(r.List) is not { } js || Value(js, a) is not { } lv)
                            {
                                Unsupported(a, "the list `" + m.Text(r.List) + "`");
                                break;
                            }
                            Line("local " + list + " = " + lv);
                            // As many rows as the list has, and never more than were drawn.
                            Line("local " + count + " = math.min(" + Value(list + ".length", a) + ", " + r.Each.Count.ToString(CultureInfo.InvariantCulture) + ")");
                            var rows = "rows#" + r.Index.ToString(CultureInfo.InvariantCulture);
                            if (plan.States.Contains(rows)) Line("DOM.bind(" + Quote(id) + ", " + Quote(rows) + ", " + count + ")");
                            for (var k = 0; k < r.Each.Count; k++)
                            {
                                var ks = k.ToString(CultureInfo.InvariantCulture);
                                var body = Lines(() =>
                                {
                                    var item = r.Items[k];
                                    _known.Add(item);
                                    Line("local " + Safe(item) + " = " + Value(list + "[" + ks + "]", a));
                                    if (kept.TryGetValue(item, out var holder)) Line(Safe(holder) + " = " + Safe(item));
                                    MarkupParts(r.Each[k]);
                                });
                                if (body.Length == 0) continue;
                                Line("if " + count + " > " + ks + " then");
                                _sb.Append(body);
                                Line("end");
                            }
                            break;
                        }
                }
            }
        }

        string? HoleValue(string? js, bool numeric, Markup.Hole h)
        {
            if (js == null)
            {
                Unsupported(a, "the value `" + m.Text(h.Value) + "`");
                return null;
            }
            if (Parsed("(" + js + ")", a) is not Expression e) return null;
            // A number written as `x.toFixed(1)` lands on a numeric slot as the number, rounded as
            // toFixed rounds: the string would only be built to be parsed straight back.
            if (numeric && e is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toFixed" }, Object: var of }, Arguments.Count: 1 } fixedCall)
                return "js_fixnum(" + Expr(of) + ", " + Expr(fixedCall.Arguments[0]) + ")";
            return Expr(e);
        }
    }

    /// <summary>A value the markup compiler wrote as JavaScript, translated where the markup is written.</summary>
    private string? Value(string js, Node at) => Parsed("(" + js + ")", at) is Expression e ? Expr(e) : null;

    /// <summary>n for markup JavaScript that is `x.toFixed(n)` with n a literal the scene can format with, else null.</summary>
    private static int? FixedDigits(string? js)
    {
        if (js == null) return null;
        try
        {
            if (new Parser().ParseScript("(" + js + ");").Body is { Count: 1 } body
                && body[0] is ExpressionStatement { Expression: CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toFixed" } }, Arguments: { Count: 1 } args } }
                && args[0] is NumericLiteral { Value: var n } && n >= 0 && n <= 9 && n == Math.Floor(n))
                return (int)n;
        }
        catch (ParseErrorException) { }
        return null;
    }

    /// <summary>JavaScript the markup compiler wrote, parsed: an expression, or null with the failure reported.</summary>
    private Expression? Parsed(string js, Node at)
    {
        try
        {
            if (new Parser().ParseScript(js + ";").Body is { Count: 1 } body && body[0] is ExpressionStatement { Expression: var e }) return e;
        }
        catch (Exception ex)
        {
            Unsupported(at, "the compiled markup's `" + (js.Length > 60 ? js.Substring(0, 59) + "~" : js) + "` (" + ex.Message + ")");
            return null;
        }
        Unsupported(at, "the compiled markup's `" + (js.Length > 60 ? js.Substring(0, 59) + "~" : js) + "`");
        return null;
    }

    /// <summary>What emitting something writes, taken back out of the buffer.</summary>
    private string Lines(Action emit)
    {
        var saved = _sb.Length;
        _depth++;
        emit();
        _depth--;
        var text = _sb.ToString(saved, _sb.Length - saved);
        _sb.Length = saved;
        return text;
    }

    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s) sb.Append(ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? ch : '_');
        return sb.ToString();
    }

    /// <summary>The element id a write targets, through the page's own getElementById wrapper.</summary>
    private string? ElementId(Expression owner)
    {
        if (owner is CallExpression { Arguments.Count: 1 } call && call.Arguments[0] is StringLiteral s) return s.Value;
        return owner is Identifier name && _elements.TryGetValue(name.Name, out var id) ? id : null;
    }

    /// <summary>Names bound to one element, so `frame.innerHTML` resolves to the id `frame` holds.</summary>
    private readonly Dictionary<string, string> _elements = new(StringComparer.Ordinal);
    private Script? _script;

    private void Assign(AssignmentExpression a)
    {
        if (InnerHtml(a)) return;
        // `arr.length = 0` is how JavaScript clears an array, and `.length` reads as js_len(), which
        // is a call and cannot be assigned to. Reported rather than emitted: truncating an array
        // properly means dropping the elements past the new length too, and a page that grows one
        // this way expects holes, so this is not a one-liner to guess at.
        if (a.Left is MemberExpression { Computed: false, Property: Identifier { Name: "length" } })
        {
            Unsupported(a, "assigning to .length");
            return;
        }
        // A CSS write the page builds as a string, emitted as the numbers it is made of.
        if (a.Operator == Operator.Assignment && _hooks == null && NumericStyle(a)) return;

        var target = Expr(a.Left);
        var value = Expr(a.Right);
        switch (a.Operator)
        {
            case Operator.Assignment: Line(target + " = " + value); break;
            case Operator.AdditionAssignment: Line(target + " = js_add(" + target + ", " + value + ")"); break;
            case Operator.SubtractionAssignment: Line(target + " = " + target + " - (" + value + ")"); break;
            case Operator.MultiplicationAssignment: Line(target + " = " + target + " * (" + value + ")"); break;
            case Operator.DivisionAssignment: Line(target + " = " + target + " / (" + value + ")"); break;
            case Operator.RemainderAssignment: Line(target + " = math.fmod(" + target + ", " + value + ")"); break;
            case Operator.BitwiseAndAssignment: Line(target + " = js_band(" + target + ", " + value + ")"); break;
            case Operator.BitwiseOrAssignment: Line(target + " = js_bor(" + target + ", " + value + ")"); break;
            case Operator.BitwiseXorAssignment: Line(target + " = js_bxor(" + target + ", " + value + ")"); break;
            case Operator.LeftShiftAssignment: Line(target + " = js_shl(" + target + ", " + value + ")"); break;
            case Operator.RightShiftAssignment: Line(target + " = js_shr(" + target + ", " + value + ")"); break;
            case Operator.UnsignedRightShiftAssignment: Line(target + " = js_ushr(" + target + ", " + value + ")"); break;
            case Operator.ExponentiationAssignment: Line(target + " = (" + target + ") ^ (" + value + ")"); break;

            // `a ??= b` assigns only when a is null or undefined; `||=` and `&&=` test truthiness.
            // All three are SHORT CIRCUITING - the right side must not be evaluated otherwise, which
            // is the whole reason a page writes them.
            case Operator.NullishCoalescingAssignment:
                Line("if " + target + " == nil then " + target + " = " + value + " end"); break;
            case Operator.LogicalOrAssignment:
                Line("if not js_truthy(" + target + ") then " + target + " = " + value + " end"); break;
            case Operator.LogicalAndAssignment:
                Line("if js_truthy(" + target + ") then " + target + " = " + value + " end"); break;

            default: Unsupported(a, "the " + a.Operator + " operator"); break;
        }
    }

    /// <summary>
    /// <c>try</c> as a <c>pcall</c> of a closure, which is the only shape Lua offers - and which is
    /// wrong for anything that leaves the block by another route. A <c>return</c> inside it returns
    /// from the closure and the function carries on; a <c>break</c> is a Lua load error; a
    /// declaration inside it dies at the closure's end; and <c>finally</c> has no equivalent at all.
    /// Each of those is reported rather than emitted, because on these pages <c>try</c> only ever
    /// guards localStorage and JSON - a plain fallback, which this shape does serve correctly.
    /// </summary>
    /// <summary>
    /// <c>try</c>, which is Lua's <c>pcall</c> around a closure - and the closure is the whole
    /// difficulty.
    /// </summary>
    /// <remarks>
    /// Three things do not survive being wrapped in a function, and each was previously refused or,
    /// worse, silently wrong:
    ///
    /// <b>Declarations.</b> A <c>var</c> inside the block became a local of the closure and vanished
    /// at its end, so the whole of <c>try { var a = 1; } catch (e) {}</c> - the most ordinary shape
    /// there is - was refused. The names are declared BEFORE the pcall and the statements inside
    /// assign them, which is what JavaScript's own hoisting does anyway.
    ///
    /// <b>The error.</b> <c>pcall</c> returns success AND the error, and only success was being
    /// read - so <c>catch (e)</c> left <c>e</c> as an unset global. It compiled, it ran, and the
    /// handler saw nothing. Now the second return value is bound to the catch parameter.
    ///
    /// <b>Returning.</b> A <c>return</c> inside the closure returns from the CLOSURE, not from the
    /// function the page wrote it in, so the value was swallowed. A flag and a value carry it out
    /// and the real return happens after. Two locals rather than a table, because this is on the
    /// per-frame path of any page that guards work with a try.
    ///
    /// <c>break</c> and <c>continue</c> still cannot cross the boundary - Lua's goto may not leave a
    /// function - and are reported rather than quietly dropped.
    /// </remarks>
    private void Try(TryStatement t)
    {
        foreach (var inner in Escapes(t.Block))
            Unsupported(inner, "`" + inner.Type + "` inside a `try` block, which cannot leave a pcall");

        var mine = ++_loop;
        var id = mine.ToString(CultureInfo.InvariantCulture);
        var ok = "__ok" + id;
        var err = "__err" + id;
        var returned = "__ret" + id;
        var value = "__val" + id;

        // Declared out here so they outlive the closure, and marked hoisted so the statements inside
        // assign rather than redeclare.
        var names = Hoistable(t.Block.Body);
        var wasHoisted = _hoisted;
        if (names.Count > 0)
        {
            _hoisted = new HashSet<string>(wasHoisted, StringComparer.Ordinal);
            foreach (var n in names) _hoisted.Add(n);
            for (var i = 0; i < names.Count; i += 40)
                Line("local " + string.Join(", ", names.GetRange(i, Math.Min(40, names.Count - i))));
        }

        var returns = Returns(t.Block);
        if (returns) Line("local " + returned + ", " + value + " = false, nil");

        var wasTry = _tryReturn;
        _tryReturn = returns ? (returned, value) : null;
        Line("local " + ok + ", " + err + " = pcall(function()");
        _depth++; Statement(t.Block); _depth--;
        Line("end)");
        _tryReturn = wasTry;
        _hoisted = wasHoisted;

        if (t.Handler != null)
        {
            Line("if not " + ok + " then");
            _depth++;
            if (t.Handler.Param is Identifier caught)
            {
                _known.Add(caught.Name);
                Line("local " + Safe(caught.Name) + " = " + err);
            }
            Statement(t.Handler.Body);
            _depth--;
            Line("end");
        }

        // `finally` runs whether or not the block threw, and before the return leaves.
        if (t.Finalizer != null) Statement(t.Finalizer);

        // An uncaught error still has to propagate, or a page swallows every fault it did not
        // handle and looks merely frozen.
        if (t.Handler == null) Line("if not " + ok + " then error(" + err + ", 0) end");
        if (returns) Line("if " + returned + " then return " + value + " end");
    }

    /// <summary>Where a `return` inside a pcall closure puts its value, or null outside one.</summary>
    private (string Flag, string Value)? _tryReturn;

    private static bool Returns(Node block)
    {
        foreach (var n in Walk(block)) if (n is ReturnStatement) return true;
        return false;
    }

    /// <summary>Statements in a try block whose effect escapes it, which a pcall closure swallows.</summary>
    private static IEnumerable<Node> Escapes(Node block)
    {
        foreach (var n in Walk(block))
        {
            // `return` is carried out by Try(); a loop jump cannot be, because Lua's goto may not
            // cross a function boundary and there is nowhere for it to land.
            if (n is BreakStatement or ContinueStatement) yield return n;
            // a nested function's own return is its own business
            if (n is FunctionDeclaration or FunctionExpression or ArrowFunctionExpression) break;
        }
    }

    private static IEnumerable<Node> Walk(Node n)
    {
        yield return n;
        foreach (var kid in n.ChildNodes)
        {
            if (kid == null || kid is FunctionDeclaration or FunctionExpression or ArrowFunctionExpression) continue;
            foreach (var deep in Walk(kid)) yield return deep;
        }
    }

    private void ForLoop(ForStatement f)
    {
        // A JS `for` is a while loop with an initialiser and an update, which is what this emits
        // rather than recognising a numeric range: the range form would be wrong the moment the body
        // writes the counter, and nothing is gained by it.
        Line("do");
        _depth++;
        var wasTop = _top; _top = false;
        if (f.Init is VariableDeclaration vd) Statement(vd);
        else if (f.Init is Expression ie) ExprStatement(ie);
        var mine = ++_loop;
        var outer = _enclosing; _enclosing = mine;
        Line("while " + (f.Test == null ? "true" : Truthy(f.Test)) + " do");
        _depth++;
        // The body goes in a block of its own with the label last. Lua refuses a `goto` that jumps
        // into a local's scope, and the update statement has to run after a `continue`, so the label
        // cannot simply sit before it: `for (...) { if (x) continue; let v = 1; }` is an everyday
        // shape and would not load.
        Line("do");
        _depth++;
        Statement(f.Body);
        Line("::continue" + mine.ToString(CultureInfo.InvariantCulture) + "::");
        _depth--;
        Line("end");
        if (f.Update != null) ExprStatement(f.Update);
        _depth--;
        Line("end");
        _depth--;
        Line("end");
        _enclosing = outer;
        _top = wasTop;
    }

    private void ForOf(ForOfStatement f)
    {
        if (f.Left is not VariableDeclaration { Declarations.Count: 1 } d || d.Declarations[0].Id is not Identifier id)
        {
            Unsupported(f, "a for-of over something other than a simple binding");
            return;
        }
        _known.Add(id.Name);
        var mine = ++_loop;
        var n = "__seq" + mine.ToString(CultureInfo.InvariantCulture);
        Line("do");
        _depth++;
        Line("local " + n + " = " + Expr(f.Right));
        // JS arrays are 0-based with a length field here (see the prelude), so the walk is too
        Line("for __i = 0, " + n + ".length - 1 do");
        _depth++;
        var outer = _enclosing; _enclosing = mine;
        var wasTop = _top; _top = false;
        Line("local " + Safe(id.Name) + " = " + n + "[__i]");
        Statement(f.Body);
        Line("::continue" + mine.ToString(CultureInfo.InvariantCulture) + "::");
        _enclosing = outer;
        _top = wasTop;
        _depth--;
        Line("end");
        _depth--;
        Line("end");
    }

    // ---- destructuring -------------------------------------------------------------------------

    /// <summary>
    /// A destructuring pattern, as the reads it stands for.
    /// </summary>
    /// <remarks>
    /// <c>const { a, b: c = 2 } = o</c> is three ordinary statements once the source is in a
    /// temporary, and so is <c>const [x, ...rest] = xs</c>. The temporary is what makes it correct
    /// rather than convenient: the right-hand side may be a call, and reading it once per bound name
    /// would run that call once per name. A page destructuring the result of a function with a side
    /// effect - which is every `const { a, b } = next()` - would then do it twice.
    ///
    /// Nesting works because each element recurses with its own temporary, so
    /// <c>const { a: { b } } = o</c> binds <c>b</c> and nothing else, exactly as it reads.
    /// </remarks>
    private void Destructure(Node pattern, Expression? from, bool declare)
    {
        var temp = "__d" + (++_loop).ToString(CultureInfo.InvariantCulture);
        Line("local " + temp + " = " + (from == null ? "nil" : Expr(from)));
        Unpack(pattern, temp, declare);
    }

    private void Unpack(Node pattern, string source, bool declare)
    {
        switch (pattern)
        {
            case Identifier id:
                _known.Add(id.Name);
                var name = Safe(id.Name);
                Line((declare && !_hoisted.Contains(name) ? "local " : "") + name + " = " + source);
                return;

            case AssignmentPattern def:
                // `{ a = 1 }`: the default applies when the value is missing, which in JavaScript
                // means undefined specifically - and nil is this runtime's undefined.
                var held = "__v" + (++_loop).ToString(CultureInfo.InvariantCulture);
                Line("local " + held + " = " + source);
                Line("if " + held + " == nil then " + held + " = " + Expr(def.Right) + " end");
                Unpack(def.Left, held, declare);
                return;

            case ObjectPattern obj:
                {
                    var taken = new List<string>();
                    foreach (var property in obj.Properties)
                    {
                        if (property is RestElement rest)
                        {
                            // `{ a, ...others }`: everything the pattern did not name. The names it
                            // did are known here, which is the only reason this can be built.
                            var into = "__r" + (++_loop).ToString(CultureInfo.InvariantCulture);
                            Line("local " + into + " = {}");
                            Line("for __k, __v in pairs(" + source + ") do");
                            _depth++;
                            Line("if " + (taken.Count == 0 ? "true" : string.Join(" and ", taken.ConvertAll(t => "__k ~= " + t)))
                                 + " then " + into + "[__k] = __v end");
                            _depth--;
                            Line("end");
                            Unpack(rest.Argument, into, declare);
                            continue;
                        }
                        // `Property`, not `ObjectProperty`: a pattern's entries are AssignmentProperty,
                        // which shares the base but not the type an object LITERAL's entries have.
                        if (property is not Property p) { Unsupported(property, property.Type.ToString()); continue; }
                        var key = KeyOf(p.Key, p.Computed, p);
                        if (key == null) continue;
                        taken.Add(Quote(key));
                        Unpack(p.Value, source + "[" + Quote(key) + "]", declare);
                    }
                    return;
                }

            case ArrayPattern arr:
                for (var i = 0; i < arr.Elements.Count; i++)
                {
                    var element = arr.Elements[i];
                    if (element == null) continue;            // a hole: `[, b] = xs`
                    if (element is RestElement rest)
                    {
                        var into = "__r" + (++_loop).ToString(CultureInfo.InvariantCulture);
                        Line("local " + into + " = js_m(" + source + ", \"slice\", " + i.ToString(CultureInfo.InvariantCulture) + ")");
                        Unpack(rest.Argument, into, declare);
                        break;                                 // a rest element is always the last
                    }
                    Unpack(element, source + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", declare);
                }
                return;

            default:
                Unsupported(pattern, "the binding " + pattern.Type);
                return;
        }
    }

    /// <summary>
    /// <c>for (const k in obj)</c> — the keys of an object, which is a different walk from for-of.
    /// </summary>
    /// <remarks>
    /// Over an array this yields the INDICES, as a string in a browser and as a number here. Pages
    /// that write it over an array mean the indices either way; pages that write it over an object
    /// mean its keys, and both work. The order is Lua's <c>pairs</c> order, which is unspecified -
    /// as it is in JavaScript for non-integer keys.
    /// </remarks>
    private void ForIn(ForInStatement f)
    {
        if (f.Left is not VariableDeclaration { Declarations.Count: 1 } d || d.Declarations[0].Id is not Identifier id)
        {
            Unsupported(f, "a for-in over something other than a simple binding");
            return;
        }
        _known.Add(id.Name);
        var mine = ++_loop;
        var key = Safe(id.Name);
        Line("for " + key + " in pairs(" + Expr(f.Right) + ") do");
        _depth++;
        var outer = _enclosing; _enclosing = mine;
        var wasTop = _top; _top = false;
        // `length` is this prelude's bookkeeping on an array, not a key the page put there, so a
        // for-in over an array would otherwise hand the page a key no browser ever would.
        Line("if " + key + " ~= \"length\" then");
        _depth++;
        Statement(f.Body);
        _depth--;
        Line("end");
        Line("::continue" + mine.ToString(CultureInfo.InvariantCulture) + "::");
        _enclosing = outer;
        _top = wasTop;
        _depth--;
        Line("end");
    }

    /// <summary>
    /// A <c>switch</c>, as an if-chain inside a breakable block.
    /// </summary>
    /// <remarks>
    /// Fall-through is the whole difficulty. JavaScript runs from the matching case to the next
    /// <c>break</c>, not to the next <c>case</c>, so a chain of <c>elseif</c> would quietly change
    /// what a page does. Instead a variable records whether any case has matched yet, and every case
    /// body runs while it is set - which reproduces fall-through exactly, including the deliberate
    /// empty-case idiom (<c>case 1: case 2: doBoth()</c>).
    ///
    /// <c>break</c> inside the switch leaves it, and that is Lua's own <c>break</c> out of the
    /// wrapping loop; a <c>continue</c> inside belongs to the enclosing loop, so that is left alone.
    /// </remarks>
    private void Switch(SwitchStatement s)
    {
        var mine = ++_loop;
        var subject = "__sw" + mine.ToString(CultureInfo.InvariantCulture);
        var hit = "__hit" + mine.ToString(CultureInfo.InvariantCulture);
        Line("local " + subject + " = " + Expr(s.Discriminant));
        Line("local " + hit + " = false");
        // `repeat ... until true` is Lua's block you can break out of, which is what a switch is.
        Line("repeat");
        _depth++;

        var wasTop = _top; _top = false;
        // The default runs only when nothing else matched, wherever it is written - so its test is
        // built from every other case rather than from its position.
        foreach (var c in s.Cases)
        {
            if (c.Test != null)
            {
                Line("if not " + hit + " and " + subject + " == " + Expr(c.Test) + " then " + hit + " = true end");
                Line("if " + hit + " then");
            }
            else
            {
                var others = new StringBuilder();
                foreach (var other in s.Cases)
                {
                    if (other.Test == null) continue;
                    if (others.Length > 0) others.Append(" and ");
                    others.Append(subject).Append(" ~= ").Append(Expr(other.Test));
                }
                Line("if not " + hit + " and (" + (others.Length > 0 ? others.ToString() : "true") + ") then " + hit + " = true end");
                Line("if " + hit + " then");
            }
            _depth++;
            foreach (var st in c.Consequent) Statement(st);
            _depth--;
            Line("end");
        }

        _top = wasTop;
        _depth--;
        Line("until true");
    }

    private void Body(Node body, List<(string Name, Node Pattern, Expression? Default, bool Variadic)>? bind = null)
    {
        var wasTop = _top; _top = false;
        var outer = _enclosing; _enclosing = 0;   // `continue` cannot cross a function boundary
        _depth++;
        if (body is BlockStatement b)
        {
            var previous = OpenScope(Hoistable(b.Body));
            // Defaults and patterns bind AFTER the scope is opened and before anything else runs, so
            // a hoisted name the page also destructures into is already declared and assigns here.
            if (bind != null) BindParams(bind);
            foreach (var s in b.Body) if (s is Statement st) Statement(st);
            _hoisted = previous;
        }
        else if (body is Expression e)
        {
            // A concise arrow body is one expression, so anything a parameter needs has to be said
            // before it - which means the body stops being a single `return`.
            if (bind is { Count: > 0 }) BindParams(bind);
            // A DOM write the caller emits as statements has no value to return, and a callback's is unread.
            if (_hooks?.Statement?.Invoke(this, e) != true) Line("do return " + Expr(e) + " end");
        }
        _depth--;
        _enclosing = outer;
        _top = wasTop;
    }

    /// <summary>
    /// A parameter list, and whatever has to happen at the top of the body to bind it.
    /// </summary>
    /// <remarks>
    /// Lua has plain positional parameters and nothing else, so a default, a pattern and a rest
    /// parameter all become a generated name plus statements inside the body. The caller emits those
    /// statements first, which is exactly where JavaScript evaluates them - a default may call a
    /// function, and it must do so on entry rather than at the use site.
    /// </remarks>
    private string Params(in NodeList<Node> ps, out List<(string Name, Node Pattern, Expression? Default, bool Variadic)> bind)
    {
        bind = new List<(string, Node, Expression?, bool)>();
        var sb = new StringBuilder();
        for (var i = 0; i < ps.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var slot = "__p" + i.ToString(CultureInfo.InvariantCulture);
            switch (ps[i])
            {
                case Identifier id:
                    _known.Add(id.Name);
                    sb.Append(Safe(id.Name));
                    break;

                case AssignmentPattern { Left: Identifier named } def:
                    // A defaulted plain name keeps its own name: only the fill-in moves to the body.
                    _known.Add(named.Name);
                    sb.Append(Safe(named.Name));
                    bind.Add((Safe(named.Name), named, def.Right, false));
                    break;

                case AssignmentPattern def:
                    sb.Append(slot);
                    bind.Add((slot, def.Left, def.Right, false));
                    break;

                case RestElement rest:
                    // `...args` is Lua's own vararg, collected into the array shape the prelude uses.
                    sb.Append("...");
                    bind.Add((slot, rest.Argument, null, true));
                    break;

                case ObjectPattern or ArrayPattern:
                    sb.Append(slot);
                    bind.Add((slot, ps[i], null, false));
                    break;

                default:
                    Unsupported(ps[i], "a parameter that is not a plain name");
                    sb.Append(slot);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Emits the statements a parameter list needs at the top of its body.</summary>
    private void BindParams(List<(string Name, Node Pattern, Expression? Default, bool Variadic)> bind)
    {
        foreach (var (name, pattern, fallback, variadic) in bind)
        {
            if (variadic)
            {
                Line("local " + name + " = js_array_of({ ... })");
                Unpack(pattern, name, declare: true);
                continue;
            }
            if (fallback != null) Line("if " + name + " == nil then " + name + " = " + Expr(fallback) + " end");
            if (pattern is not Identifier) Unpack(pattern, name, declare: true);
        }
    }

    // ---- expressions -------------------------------------------------------------------------

    /// <summary>A test, as a Lua boolean. JS counts 0 and "" as false where Lua counts them true.</summary>
    private string Truthy(Expression e)
    {
        switch (e)
        {
            // a comparison already yields a boolean in both languages, so it needs no conversion
            case NonLogicalBinaryExpression b when IsComparison(b.Operator):
                return Expr(e);
            case LogicalExpression { Operator: Operator.LogicalAnd } l:
                return "(" + Truthy(l.Left) + " and " + Truthy(l.Right) + ")";
            case LogicalExpression { Operator: Operator.LogicalOr } l2:
                return "(" + Truthy(l2.Left) + " or " + Truthy(l2.Right) + ")";
            case NonUpdateUnaryExpression { Operator: Operator.LogicalNot } u:
                return "(not " + Truthy(u.Argument) + ")";
            case BooleanLiteral bl:
                return bl.Value ? "true" : "false";
            default:
                return "js_truthy(" + Expr(e) + ")";
        }
    }

    /// <summary>
    /// Whether an expression can never evaluate to Lua's <c>false</c> or <c>nil</c>, which is what
    /// makes <c>a and b or c</c> a safe spelling of a ternary. Deliberately conservative: a wrong
    /// "yes" here silently returns the other branch, so anything not obviously a number, a string,
    /// a table or a function answers no and pays for a closure instead.
    /// </summary>
    private bool NeverFalsy(Expression e) => e switch
    {
        NumericLiteral or StringLiteral => true,
        // `RED_INK` where the page wrote `const RED_INK = 'var(--red-ink)'` and bound it nowhere else:
        // every `tone === 'trip' ? RED_INK : ...` a page writes was a closure per evaluation.
        Identifier id => _constTruthy.Contains(id.Name) && (_rename == null || !_rename.ContainsKey(id.Name)),
        BooleanLiteral b => b.Value,
        ObjectExpression or ArrayExpression or FunctionExpression or ArrowFunctionExpression => true,
        // arithmetic yields a number, and `+` yields a number or a string; neither is false or nil
        NonLogicalBinaryExpression a => a.Operator is Operator.Addition or Operator.Subtraction
            or Operator.Multiplication or Operator.Division or Operator.Remainder or Operator.Exponentiation,
        NonUpdateUnaryExpression u => u.Operator is Operator.UnaryNegation or Operator.UnaryPlus or Operator.TypeOf,
        ConditionalExpression c => NeverFalsy(c.Consequent) && NeverFalsy(c.Alternate),
        _ => false,
    };

    /// <summary>
    /// Whether the right side of <c>||</c>, <c>&amp;&amp;</c> or <c>??</c> may be evaluated whether or not the
    /// operator would have reached it: it can neither fault nor do anything a page could see.
    /// </summary>
    /// <remarks>
    /// A literal, a name, and one step into a table that is always there - <c>ROLES[0]</c>,
    /// <c>ALARM_STATE.ok</c> - which is what almost every fallback in a view model reads. The last is
    /// safe only for a <see cref="_constTables"/> name: nothing else can be bound under it, so it is
    /// that literal's table. A table's own fields have no getters here (<see cref="ConstTables"/>
    /// refuses a literal with one), and an array's missing index reaches only the prelude's method
    /// lookup, which changes nothing.
    /// ponytail: a function called during setup, before the const's own line has run, would fault
    /// here where the short circuit might not have been reached. Loudly, on load, and no page does it.
    /// </remarks>
    private bool Eager(Expression e) => e switch
    {
        NumericLiteral or StringLiteral or BooleanLiteral or NullLiteral or Identifier => true,
        MemberExpression { Object: Identifier root } m when _constTables.Contains(root.Name)
            => m.Computed ? m.Property is NumericLiteral or StringLiteral : m.Property is Identifier,
        _ => false,
    };

    /// <summary>Fills <see cref="_constTables"/>: see <see cref="Eager"/>.</summary>
    private void ConstTables(Script ast)
    {
        foreach (var s in ast.Body)
            if (s is VariableDeclaration { Kind: VariableDeclarationKind.Const } vd)
                foreach (var d in vd.Declarations)
                {
                    if (d.Id is Identifier id && (d.Init is ArrayExpression || d.Init is ObjectExpression o && Plain(o)))
                        _constTables.Add(id.Name);
                    if (d.Id is Identifier lit && d.Init is StringLiteral or NumericLiteral or TemplateLiteral
                                                  or ObjectExpression or ArrayExpression or BooleanLiteral { Value: true })
                        _constTruthy.Add(lit.Name);
                }
        if (_constTables.Count == 0 && _constTruthy.Count == 0) return;

        // Bound anywhere else - a parameter, an inner declaration, a catch - and `NAME.key` might read
        // that instead. Every identifier under a binding position counts, a destructuring key
        // included: counting too many only keeps a thunk that was not needed.
        var bound = new Dictionary<string, int>(StringComparer.Ordinal);
        void Bind(Node? n)
        {
            if (n == null) return;
            foreach (var x in Markup.Everything(n))
                if (x is Identifier i) bound[i.Name] = bound.TryGetValue(i.Name, out var k) ? k + 1 : 1;
        }
        foreach (var n in Markup.Everything(ast))
            switch (n)
            {
                case VariableDeclarator d: Bind(d.Id); break;
                case FunctionDeclaration f: Bind(f.Id); foreach (var p in f.Params) Bind(p); break;
                case FunctionExpression f: Bind(f.Id); foreach (var p in f.Params) Bind(p); break;
                case ArrowFunctionExpression f: foreach (var p in f.Params) Bind(p); break;
                case ClassDeclaration k: Bind(k.Id); break;
                case ClassExpression k: Bind(k.Id); break;
                case CatchClause k: Bind(k.Param); break;
            }
        _constTables.RemoveWhere(name => bound.TryGetValue(name, out var k) && k > 1);
        _constTruthy.RemoveWhere(name => bound.TryGetValue(name, out var k) && k > 1);

        // No getter or setter: reading one of those runs page code.
        static bool Plain(ObjectExpression o)
        {
            foreach (var p in o.Properties)
                if (p is Property { Kind: not PropertyKind.Init }) return false;
            return true;
        }
    }

    // ---- view models, read one field at a time -------------------------------------------------

    /// <summary>A function that builds a view model: its locals in order, and the fields it returns.</summary>
    internal sealed class View
    {
        public readonly List<(string Name, Expression Init)> Locals = new();
        public readonly List<(string Key, Expression Value)> Fields = new();
        /// <summary>
        /// Locals (`@name`) and fields whose building registers something - `act(fn)` pushing onto the
        /// page's handler list. They are computed when the builder is called, in source order, as the
        /// page computed them: a handler's index is its place in that list, and building it on first
        /// read would renumber every handler after it.
        /// </summary>
        public readonly HashSet<string> Eager = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<FunctionDeclaration, View> _views = new();
    /// <summary>While a view is emitted: its locals, as the Lua that reads each one.</summary>
    private Dictionary<string, string>? _rename;

    /// <summary>
    /// Finds the page's view-model builders - <c>function values() { const a = ...; return { x: ..., y: ... }; }</c>
    /// - whose result is only ever read field by field, so it can be computed field by field.
    /// </summary>
    /// <remarks>
    /// A compiled page draws one tab, and its render reads only that tab's fields; the view model a
    /// page builds for it computes every tab's strings, arrays and handlers regardless. On the Atmo
    /// pages that was nine tenths of what the chip allocated per render. Reading a field computes it
    /// (and what it depends on) once per call, so a field no drawn hole reads is never built.
    ///
    /// The call is the same, so only what it can NOT tell apart is checked: the object is never
    /// enumerated, stored, written or handed to anything but a page function that itself only reads
    /// it; and building it has no effect a lazy build would skip - the one allowed is a push onto a
    /// top-level registry the page resets (`acts.push(fn)`), which compiled markup does not read.
    /// Anything else keeps the eager builder, unchanged.
    /// ponytail: a field read after a LATER call sees the later values. Every page renders after
    /// each state change, so its handlers only ever read the latest; one that kept an old view
    /// model and compared it with a new one would need a copy per call.
    /// </remarks>
    private void Views(Script ast)
    {
        foreach (var pair in LazyViews(ast)) _views[pair.Key] = pair.Value;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Script, Dictionary<FunctionDeclaration, View>> Found = new();

    /// <summary>
    /// The builders <see cref="LazyView"/> emits lazily, by declaration. The markup compiler asks
    /// the same question of the same script, since what it inlines through a view model must agree
    /// with how the view model is emitted: see <see cref="Markup"/>'s view holders.
    /// </summary>
    internal static IReadOnlyDictionary<FunctionDeclaration, View> LazyViews(Script ast)
    {
        lock (Found)
        {
            if (Found.TryGetValue(ast, out var known)) return known;
            var views = FindViews(ast);
            Found.Add(ast, views);
            return views;
        }
    }

    private static Dictionary<FunctionDeclaration, View> FindViews(Script ast)
    {
        var found = new Dictionary<FunctionDeclaration, View>();
        var parents = new Dictionary<Node, Node>();
        void Link(Node n) { foreach (var k in n.ChildNodes) if (k != null) { parents[k] = n; Link(k); } }
        Link(ast);

        // Top-level functions by name, declared once: a call to one of them is the only thing a view
        // model may be handed to.
        var fns = new Dictionary<string, IFunction>(StringComparer.Ordinal);
        var twice = new HashSet<string>(StringComparer.Ordinal);
        void Top(string name, IFunction fn) { if (!fns.TryAdd(name, fn)) twice.Add(name); }
        foreach (var s in ast.Body)
        {
            if (s is FunctionDeclaration { Id: { } fid } fd) Top(fid.Name, fd);
            else if (s is VariableDeclaration vd)
                foreach (var d in vd.Declarations)
                    if (d.Id is Identifier vid && d.Init is ArrowFunctionExpression or FunctionExpression) Top(vid.Name, (IFunction)d.Init);
        }
        foreach (var n in twice) fns.Remove(n);

        // What building does that a lazy build could skip or reorder: nothing, a push onto a handler
        // registry (the order matters, so it is built eagerly), or anything else (no lazy view at all).
        const int None = 0, Registers = 1, Other = 2;
        var pure = new Dictionary<IFunction, int>();
        var reads = new Dictionary<(IFunction, int), bool>();
        foreach (var s in ast.Body)
        {
            if (s is not FunctionDeclaration { Id: { } id, Params.Count: 0, Body: { } body } fd || !fns.ContainsKey(id.Name)) continue;
            if (Shape(body) is not { } view) continue;
            if (!Calls(id.Name, fd) || Effect(fd) == Other) continue;
            var inside = Inside(fd);
            foreach (var (local, init) in view.Locals) if (Effects(init, inside) == Registers) view.Eager.Add("@" + local);
            foreach (var (key, value) in view.Fields) if (Effects(value, inside) == Registers) view.Eager.Add(key);
            found[fd] = view;
        }
        return found;

        // The builder: declarations, then `return { ... }` of plain named fields. Nothing reads
        // `this` or `arguments`, and no closure in it reassigns one of its locals.
        View? Shape(BlockStatement body)
        {
            var view = new View();
            var locals = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < body.Body.Count; i++)
            {
                var st = body.Body[i];
                if (i < body.Body.Count - 1)
                {
                    if (st is not VariableDeclaration { Kind: VariableDeclarationKind.Const or VariableDeclarationKind.Let } vd) return null;
                    foreach (var d in vd.Declarations)
                    {
                        if (d.Id is not Identifier lid || d.Init == null || !locals.Add(lid.Name)) return null;
                        view.Locals.Add((lid.Name, d.Init));
                    }
                    continue;
                }
                if (st is not ReturnStatement { Argument: ObjectExpression obj }) return null;
                foreach (var p in obj.Properties)
                {
                    if (p is not ObjectProperty { Computed: false, Kind: PropertyKind.Init } op || op.Value is not Expression value) return null;
                    var key = op.Key switch { Identifier k => k.Name, StringLiteral k => k.Value, _ => null };
                    // `@` is where the view model's locals are read from (LazyView), so no field may be one.
                    if (key == null || key.StartsWith("@", StringComparison.Ordinal)) return null;
                    view.Fields.RemoveAll(f => f.Key == key);
                    view.Fields.Add((key, value));
                }
            }
            if (view.Fields.Count == 0) return null;
            foreach (var n in Markup.Everything(body))
            {
                if (n is ThisExpression || n is Identifier { Name: "arguments" }) return null;
                if (n is AssignmentExpression { Left: Identifier a } && locals.Contains(a.Name)) return null;
                if (n is UpdateExpression { Argument: Identifier u } && locals.Contains(u.Name)) return null;
            }
            return view;
        }

        // Every mention of the builder is `const v = values()`, and every `v` is only read.
        bool Calls(string name, FunctionDeclaration decl)
        {
            var any = false;
            foreach (var n in Markup.Everything(ast))
            {
                if (n is not Identifier ident || ident.Name != name || ReferenceEquals(ident, decl.Id)) continue;
                if (!parents.TryGetValue(ident, out var call) || call is not CallExpression { Arguments.Count: 0 } c || !ReferenceEquals(c.Callee, ident)
                    || !parents.TryGetValue(c, out var owner) || owner is not VariableDeclarator { Id: Identifier held } vd || !ReferenceEquals(vd.Init, c))
                    return false;
                if (!OnlyRead(held.Name, Scope(vd), vd.Id)) return false;
                any = true;
            }
            return any;
        }

        Node Scope(Node n)
        {
            for (var at = n; parents.TryGetValue(at, out var up); at = up)
                if (up is IFunction) return up;
            return ast;
        }

        // Inside `scope`, the name is bound once (at `binding`) and each other mention is a field
        // read or an argument to a page function that only reads it in turn.
        bool OnlyRead(string name, Node scope, Node binding)
        {
            foreach (var n in Markup.Everything(scope))
            {
                if (n is not Identifier ident || ident.Name != name || ReferenceEquals(ident, binding)) continue;
                if (!parents.TryGetValue(ident, out var p)) return false;
                switch (p)
                {
                    case MemberExpression m when ReferenceEquals(m.Property, ident) && !m.Computed:
                    case ObjectProperty op when ReferenceEquals(op.Key, ident) && !op.Computed && !op.Shorthand:
                        continue;
                    case MemberExpression m when ReferenceEquals(m.Object, ident):
                        if (parents.TryGetValue(m, out var use)
                            && (use is AssignmentExpression wa && ReferenceEquals(wa.Left, m)
                                || use is UpdateExpression
                                || use is NonUpdateUnaryExpression { Operator: Operator.Delete }))
                            return false;
                        continue;
                    case CallExpression c when !ReferenceEquals(c.Callee, ident) && c.Callee is Identifier callee
                                               && fns.TryGetValue(callee.Name, out var fn):
                        {
                            var at = -1;
                            for (var i = 0; i < c.Arguments.Count; i++) if (ReferenceEquals(c.Arguments[i], ident)) at = i;
                            if (at < 0 || at >= fn.Params.Count || fn.Params[at] is not Identifier param) return false;
                            if (!reads.TryGetValue((fn, at), out var ok))
                            {
                                reads[(fn, at)] = true;   // a recursive call reads it the same way
                                ok = OnlyRead(param.Name, (Node)fn, param);
                                reads[(fn, at)] = ok;
                            }
                            if (!ok) return false;
                            continue;
                        }
                    default:
                        // A binding of the same name - a parameter or declaration below - or any other use.
                        return false;
                }
            }
            return true;
        }

        // Nothing a lazy build would skip: no write to a name declared outside the builder, no DOM,
        // and no call to a page function that does either. Closures stored as field values are
        // handlers - they run later, on their own - and are not part of building.
        HashSet<string> Inside(IFunction fn)
        {
            var inside = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in Markup.Everything((Node)fn))
            {
                if (n is VariableDeclarator { Id: var target }) foreach (var x in Markup.Everything(target)) if (x is Identifier b) inside.Add(b.Name);
                if (n is IFunction f) foreach (var q in f.Params) foreach (var x in Markup.Everything(q)) if (x is Identifier b) inside.Add(b.Name);
            }
            return inside;
        }

        int Effect(IFunction fn)
        {
            if (pure.TryGetValue(fn, out var known)) return known;
            pure[fn] = None;   // recursion: assume, and let the rest of the walk decide
            var found = Effects((Node)fn, Inside(fn));
            pure[fn] = found;
            return found;
        }

        int Effects(Node n, HashSet<string> inside)
        {
            if (n is IFunction && parents.TryGetValue(n, out var up) && Stored(n, up)) return None;
            var here = None;
            switch (n)
            {
                case AssignmentExpression a when !Local(a.Left, inside):
                case UpdateExpression u when !Local(u.Argument, inside):
                case NonUpdateUnaryExpression { Operator: Operator.Delete } d when !Local(d.Argument, inside):
                    return Other;
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier method, Object: var owner } }
                    when method.Name is "pop" or "shift" or "unshift" or "splice" or "sort" or "reverse" or "fill" or "copyWithin"
                         && !Local(owner, inside):
                    return Other;
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "push" }, Object: var list } }
                    when !Local(list, inside):
                    if (!Registry(list)) return Other;
                    here = Registers;
                    break;
                case CallExpression { Callee: MemberExpression { Object: Identifier { Name: "Object" }, Property: Identifier { Name: "assign" } } } oa
                    when oa.Arguments.Count > 0 && !Local(oa.Arguments[0], inside):
                    return Other;
                case Identifier { Name: "document" or "window" or "console" or "localStorage" or "sessionStorage" or "history" or "location" } g
                    when !(parents.TryGetValue(g, out var gp) && (gp is MemberExpression { Computed: false } gm && ReferenceEquals(gm.Property, g)
                                                                  || gp is ObjectProperty { Computed: false, Shorthand: false } gk && ReferenceEquals(gk.Key, g))):
                    return Other;
                case CallExpression { Callee: Identifier callee } when fns.TryGetValue(callee.Name, out var called):
                    here = Effect(called);
                    if (here == Other) return Other;
                    break;
            }
            foreach (var k in n.ChildNodes)
            {
                if (k == null) continue;
                var below = Effects(k, inside);
                if (below == Other) return Other;
                if (below > here) here = below;
            }
            return here;
        }

        // A closure that is a field's value, possibly behind a condition - `pick: on ? () => ... : null`
        // - or one handed to a page function that keeps it without calling it: `act(() => ...)`.
        bool Stored(Node fn, Node up)
        {
            var at = fn;
            while (up is ConditionalExpression or LogicalExpression or ParenthesizedExpression)
            {
                at = up;
                if (!parents.TryGetValue(up, out up!)) return false;
            }
            if (up is ObjectProperty op && ReferenceEquals(op.Value, at)) return true;
            if (up is not CallExpression { Callee: Identifier callee } c || !fns.TryGetValue(callee.Name, out var keeper)) return false;
            for (var i = 0; i < c.Arguments.Count && i < keeper.Params.Count; i++)
                if (ReferenceEquals(c.Arguments[i], at) && keeper.Params[i] is Identifier kept)
                {
                    foreach (var n in Markup.Everything((Node)keeper))
                        if (n is CallExpression { Callee: Identifier run } && run.Name == kept.Name) return false;
                    return true;
                }
            return false;
        }

        static bool Local(Node? target, HashSet<string> inside)
        {
            for (var guard = 0; target != null && guard < 32; guard++)
                switch (target)
                {
                    case Identifier id: return inside.Contains(id.Name);
                    case MemberExpression m: target = m.Object; break;
                    case ParenthesizedExpression p: target = p.Expression; break;
                    // `Object.assign({}, n, {...})`: a literal made right here is nobody else's.
                    case ObjectExpression or ArrayExpression: return true;
                    default: return false;
                }
            return false;
        }

        // `acts.push(fn)` onto a top-level `let` the page empties with `acts = []`.
        bool Registry(Node list)
        {
            if (list is not Identifier { Name: var name }) return false;
            foreach (var s in ast.Body)
                if (s is VariableDeclaration { Kind: VariableDeclarationKind.Let } vd)
                    foreach (var d in vd.Declarations)
                        if (d.Id is Identifier { Name: var declared } && declared == name)
                            foreach (var n in Markup.Everything(ast))
                                if (n is AssignmentExpression { Left: Identifier target, Right: ArrayExpression { Elements.Count: 0 } } && target.Name == name)
                                    return true;
            return false;
        }
    }

    /// <summary>
    /// A view-model builder as a lazy object: each local and field is a function that computes it at
    /// most once per call of the builder, and the object it returns computes a field when it is read.
    /// </summary>
    /// <remarks>
    /// Every function here is made ONCE, when the chunk loads. A call of the builder is a counter
    /// bump; a read of a field computed earlier in the same call is a table lookup. The fields'
    /// values - their strings, their lists, their handlers - are built only when something reads
    /// them, which on a compiled page is the drawn tab's holes and nothing else.
    /// </remarks>
    private void LazyView(string name, View view)
    {
        // A function of its own rather than a `do` block: a block's locals count against the main
        // function's 200, which the prelude and a large page already come close to. Called inside a
        // `do`, because a statement that opens with `(` after one that ends in a call - `X = f(...)` -
        // is read by Lua as calling that result.
        Line("-- " + name + "(): a view model computed field by field, as its fields are read");
        Line("do (function()");
        _depth++;
        Line("local vm_gen, vm_done, vm_memo, vm_locals, vm_fields = 0, {}, {}, {}, {}");
        var outer = _rename;
        _rename = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (local, _) in view.Locals) _rename[local] = "vm_locals[" + Quote(local) + "]()";
        foreach (var (local, init) in view.Locals) Memo("vm_locals[" + Quote(local) + "]", "@" + local, init);
        foreach (var (key, value) in view.Fields) Memo("vm_fields[" + Quote(key) + "]", key, value);
        // Its locals too, as `v["@name"]`: the markup compiler reads a hole straight out of the
        // view model's own expressions, and one that needs a local reads the memo here rather than
        // computing it again in every hole. `@` cannot begin a field a page wrote as a name.
        foreach (var (local, _) in view.Locals) Line("vm_fields[" + Quote("@" + local) + "] = vm_locals[" + Quote(local) + "]");
        _rename = outer;
        Line("local vm_view = setmetatable({}, { __index = function(_, k) local f = vm_fields[k] if f ~= nil then return f() end end })");
        var eager = new StringBuilder();
        foreach (var (local, _) in view.Locals) if (view.Eager.Contains("@" + local)) eager.Append(" vm_locals[").Append(Quote(local)).Append("]()");
        foreach (var (key, _) in view.Fields) if (view.Eager.Contains(key)) eager.Append(" vm_fields[").Append(Quote(key)).Append("]()");
        Line(Safe(name) + " = function() vm_gen = vm_gen + 1" + eager + " return vm_view end");
        _depth--;
        Line("end)() end");

        void Memo(string slot, string key, Expression init)
        {
            var k = Quote(key);
            Line(slot + " = function()");
            _depth++;
            Line("if vm_done[" + k + "] == vm_gen then return vm_memo[" + k + "] end");
            Line("local vm_x = " + Expr(init));
            Line("vm_memo[" + k + "] = vm_x");
            Line("vm_done[" + k + "] = vm_gen");
            Line("return vm_x");
            _depth--;
            Line("end");
        }
    }

    /// <summary>A function's own names hide a view's locals of the same name inside it.</summary>
    private List<(string, string)>? Shadow(in NodeList<Node> ps, Node body)
    {
        if (_rename == null || _rename.Count == 0) return null;
        List<(string, string)>? hidden = null;
        void Hide(Node? n)
        {
            if (n == null) return;
            foreach (var x in Markup.Everything(n))
                if (x is Identifier id && _rename.TryGetValue(id.Name, out var was))
                {
                    (hidden ??= new List<(string, string)>()).Add((id.Name, was));
                    _rename.Remove(id.Name);
                }
        }
        foreach (var p in ps) Hide(p);
        // The function's own scope, not a nested one's: each nested function hides its own names
        // when it is translated in turn. A nested declaration's NAME is bound here, though.
        foreach (var n in Walk(body))
        {
            if (n is VariableDeclarator d) Hide(d.Id);
            if (n is CatchClause { Param: { } cp }) Hide(cp);
            foreach (var kid in n.ChildNodes) if (kid is FunctionDeclaration { Id: { } fid }) Hide(fid);
        }
        return hidden;
    }

    private void Unshadow(List<(string, string)>? hidden)
    {
        if (hidden == null || _rename == null) return;
        foreach (var (n, was) in hidden) _rename[n] = was;
    }

    /// <summary>
    /// A name, or up to three field reads off one - `o.stats`, `st.lines.o2` - which reading twice
    /// costs nothing and changes nothing. `this` and calls are not paths.
    /// </summary>
    private static bool Path(Expression e)
    {
        for (var depth = 0; depth <= 3; depth++)
            switch (e)
            {
                case Identifier: return true;
                case MemberExpression { Computed: false } m: e = m.Object; break;
                case MemberExpression { Computed: true, Property: StringLiteral or NumericLiteral } m: e = m.Object; break;
                default: return false;
            }
        return false;
    }

    /// <summary>`null`, or the `undefined` that is spelled as a bare name; Lua's nil is both.</summary>
    private static bool IsNull(Expression e) => e is NullLiteral || (e is Identifier { Name: "undefined" });

    private static bool IsComparison(Operator op) => op is Operator.Equality or Operator.Inequality
        or Operator.StrictEquality or Operator.StrictInequality or Operator.LessThan
        or Operator.LessThanOrEqual or Operator.GreaterThan or Operator.GreaterThanOrEqual;

    private string Expr(Node? e)
    {
        if (e != null && _hooks?.Expression?.Invoke(this, e) is { } hooked) return hooked;
        switch (e)
        {
            case null:
                return "nil";

            case Identifier id:
                if (_rename != null && _rename.TryGetValue(id.Name, out var lazy)) return lazy;
                if (!_known.Contains(id.Name) && !Provided.Contains(id.Name))
                    Unsupported(id, "`" + id.Name + "`, which neither the page nor the prelude defines,");
                return Safe(id.Name);

            case NumericLiteral n:
                return Number(n.Value);

            case StringLiteral s:
                return Quote(s.Value);

            case BooleanLiteral b:
                return b.Value ? "true" : "false";

            case NullLiteral:
                return "nil";

            case ThisExpression when _receiver == null:
                // Only a getter has a receiver here; an arrow function has no `this` at all, so a
                // bare `self` would be a nil global rather than a translation.
                return Fail(e, "`this` outside a getter");

            case ThisExpression:
                return _receiver;

            case MemberExpression m:
                return Member(m);

            case NonLogicalBinaryExpression bin:
                return Binary(bin);

            // `??` yields the right side only when the left is null or undefined, where `||` yields
            // it for anything falsy - so `0 ?? 1` is 0 and `0 || 1` is 1. Lua's nil covers both null
            // and undefined, so this is exact rather than approximate. It was silently compiling to
            // `||` until a test asked for every unsupported construct to be refused and this one
            // was not refused.
            case LogicalExpression { Operator: Operator.NullishCoalescing } nc when Eager(nc.Right):
                return "nc_v(" + Expr(nc.Left) + ", " + Expr(nc.Right) + ")";

            case LogicalExpression { Operator: Operator.NullishCoalescing } nc:
                return "(function() local __v = " + Expr(nc.Left) + " if __v ~= nil then return __v end return "
                       + Expr(nc.Right) + " end)()";

            // A right side that cannot fault and does nothing - a literal, a name - is evaluated as it
            // stands: short-circuiting it would change nothing a page could see, and the thunk below is
            // a closure on every evaluation. `x || 0` and `tone || 'live'` are most of a page's `||`,
            // and a view model re-rendered 2.4 times a second made hundreds of them a render.
            case LogicalExpression log when Eager(log.Right):
                return (log.Operator == Operator.LogicalAnd ? "and_v(" : "or_v(") + Expr(log.Left) + ", " + Expr(log.Right) + ")";

            // `o.stats || []`, `st.bound || {}`: a left side that is a name or a short path of field
            // reads is read twice instead, and the right side is Lua's own short circuit - no closure.
            // A value JavaScript calls truthy is never Lua's false or nil, so `and a` yields it whole.
            // `&&` only when its right side can never be false or nil, for the same reason.
            case LogicalExpression { Operator: Operator.LogicalOr } lor when Path(lor.Left):
                return "(js_truthy(" + Expr(lor.Left) + ") and " + Expr(lor.Left) + " or " + Expr(lor.Right) + ")";

            case LogicalExpression { Operator: Operator.LogicalAnd } land when Path(land.Left) && NeverFalsy(land.Right):
                return "(js_truthy(" + Expr(land.Left) + ") and " + Expr(land.Right) + " or " + Expr(land.Left) + ")";

            case LogicalExpression log:
                // Value position, so the operand itself is the result as in JS. The right side is a
                // thunk because `&&` and `||` short-circuit, and guarding with them is the whole
                // point of writing them: `typeof location !== 'undefined' && location.hash` faults
                // on the spot without it. A closure per evaluation, only in value position - a test
                // goes through Truthy, which needs none.
                return (log.Operator == Operator.LogicalAnd ? "js_and(" : "js_or(")
                       + Expr(log.Left) + ", function() return " + Expr(log.Right) + " end)";

            case NonUpdateUnaryExpression u:
                return Unary(u);

            case ConditionalExpression c:
                // A ternary evaluates only the branch it takes, and that is not a detail: a branch
                // may call Math.random, and evaluating the other one too shifted a game's whole
                // obstacle sequence by one draw. Found by diffing a real page against itself.
                //
                // Lua's own `a and b or c` short-circuits and costs nothing, but yields c whenever b
                // is false or nil - so it is used only where the consequent provably cannot be
                // either. Everything else pays for two closures, which is the price of being right.
                return NeverFalsy(c.Consequent)
                    ? "(" + Truthy(c.Test) + " and " + Expr(c.Consequent) + " or " + Expr(c.Alternate) + ")"
                    : "(function() if " + Truthy(c.Test) + " then return " + Expr(c.Consequent)
                      + " end return " + Expr(c.Alternate) + " end)()";

            case CallExpression call:
                return Call(call);

            case ArrayExpression arr:
                {
                    var spreads = false;
                    foreach (var el in arr.Elements) if (el is SpreadElement) { spreads = true; break; }
                    if (spreads)
                    {
                        // `[a, ...xs, b]` - the length is not known here, so the pieces are joined
                        // at run time rather than indexed into a literal.
                        var into = new StringBuilder("js_concat(");
                        for (var i = 0; i < arr.Elements.Count; i++)
                        {
                            if (i > 0) into.Append(", ");
                            into.Append(arr.Elements[i] is SpreadElement element
                                ? "js_spread_of(" + Expr(element.Argument) + ")" : Expr(arr.Elements[i]));
                        }
                        return into.Append(')').ToString();
                    }
                    var sb = new StringBuilder("js_array({");
                    for (var i = 0; i < arr.Elements.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append('[').Append(i.ToString(CultureInfo.InvariantCulture)).Append("] = ").Append(Expr(arr.Elements[i]));
                    }
                    return sb.Append("}, ").Append(arr.Elements.Count.ToString(CultureInfo.InvariantCulture)).Append(')').ToString();
                }

            case ObjectExpression obj:
                return Object(obj);

            case ArrowFunctionExpression a:
                return Lambda(a.Params, a.Body);

            case FunctionExpression fe:
                return Lambda(fe.Params, fe.Body);

            case AssignmentExpression { Operator: Operator.Assignment, Left: MemberExpression { Computed: true } }:
                // `a[next()] = v` as a value would evaluate the index once to write and again to
                // read back, so next() would run twice. Reported rather than quietly done twice.
                return Fail(e, "an assignment to a computed index used as a value");

            case AssignmentExpression { Operator: Operator.Assignment } av:
                // Lua has no assignment expression, so `a || (a = x)` becomes a closure that
                // assigns and returns. Only the plain form: a compound assignment used as a value
                // does not appear on any page here and would evaluate its target twice.
                return "(function() " + Expr(av.Left) + " = " + Expr(av.Right) + " return " + Expr(av.Left) + " end)()";

            case AssignmentExpression:
                return Fail(e, "a compound assignment used as a value");

            case UpdateExpression:
                return Fail(e, "++ or -- used as a value");

            case RegExpLiteral re:
                // The prelude has a real engine now, so the pattern goes through as raw text with
                // its backslashes intact. It used to refuse anything containing regex syntax,
                // because a Lua pattern is not a regex and quietly matching something else is the
                // worst outcome available - that reasoning was right, and the answer was to build
                // the engine rather than to keep refusing. What it cannot do (lookbehind, \p{},
                // an enormous quantifier) it refuses at construction, naming the pattern.
                return "js_regex(" + Quote(re.Raw.Substring(1, re.Raw.LastIndexOf('/') - 1))
                       + ", " + Quote(re.Raw.Substring(re.Raw.LastIndexOf('/') + 1)) + ")";

            case TemplateLiteral tpl:
                return Template(tpl);

            case TaggedTemplateExpression tagged:
                {
                    // `tag`a${x}b`` calls tag with the literal pieces as an array and the values as
                    // the remaining arguments. The array carries `raw` because that is what every
                    // real tag function reads; cooked and raw differ only in escape processing,
                    // which this parser has already done, so both hold the same strings.
                    var quasi = tagged.Quasi;
                    var pieces = new StringBuilder("js_array({");
                    for (var i = 0; i < quasi.Quasis.Count; i++)
                    {
                        if (i > 0) pieces.Append(", ");
                        pieces.Append('[').Append(i.ToString(CultureInfo.InvariantCulture)).Append("] = ")
                              .Append(Quote(quasi.Quasis[i].Value.Cooked ?? quasi.Quasis[i].Value.Raw));
                    }
                    pieces.Append("}, ").Append(quasi.Quasis.Count.ToString(CultureInfo.InvariantCulture)).Append(')');

                    // Built inline rather than through a prelude helper: `raw` has to be a property
                    // OF the pieces array, which is one statement, and a helper for one statement is
                    // a name to keep in step with the manifest for no gain.
                    var call = new StringBuilder("(function() local __q = ").Append(pieces)
                                   .Append(" __q.raw = __q return ").Append(Expr(tagged.Tag)).Append("(__q");
                    foreach (var hole in quasi.Expressions) call.Append(", ").Append(Expr(hole));
                    return call.Append(") end)()").ToString();
                }

            case ChainExpression chain:
                return Chain(chain);

            case SequenceExpression seq:
                {
                    // `(a, b)` evaluates both and yields the last. The earlier ones are there for
                    // their side effects, so they are evaluated rather than dropped.
                    var sb = new StringBuilder("(function() ");
                    for (var i = 0; i < seq.Expressions.Count - 1; i++)
                        sb.Append("local _ = ").Append(Expr(seq.Expressions[i])).Append(' ');
                    return sb.Append("return ").Append(Expr(seq.Expressions[seq.Expressions.Count - 1]))
                             .Append(" end)()").ToString();
                }

            case ClassExpression ce:
                return Class(ce, ce.Id?.Name ?? "(anonymous)");

            case NewExpression ne:
                return New(ne);

            case Super when _superBase != null:
                // Bare `super` only ever appears as the head of a call or a member access, and both
                // are handled where they are built. Reaching here means something else was done
                // with it, and the base class itself is the honest translation.
                return _superBase;

            case Super:
                return Fail(e, "`super` outside a class that extends something");

            default:
                return Fail(e, e.Type.ToString());
        }
    }

    /// <summary>
    /// A template literal: the quasis are the structure and the expressions are the holes.
    /// </summary>
    /// <remarks>
    /// Concatenation, not <c>js_add</c>. Every hole in a template is string-coerced by definition -
    /// <c>`${1}${2}`</c> is "12", not 3 - so routing it through the <c>+</c> operator's translation
    /// would silently turn the common numeric case into arithmetic. <c>js_str</c> is the same
    /// coercion the rest of the prelude uses, so a number formats identically here and in
    /// <c>'a' + x</c>.
    ///
    /// A literal piece is emitted quoted rather than through js_str: it is already a string, and
    /// keeping it literal is what lets the compiler see the structure it is going to need.
    /// </remarks>
    private string Template(TemplateLiteral tpl)
    {
        var parts = new List<string>(tpl.Quasis.Count + tpl.Expressions.Count);
        for (var i = 0; i < tpl.Quasis.Count; i++)
        {
            var cooked = tpl.Quasis[i].Value.Cooked ?? tpl.Quasis[i].Value.Raw ?? string.Empty;
            // An empty piece contributes nothing - `${a}${b}` has three of them - and dropping it
            // keeps the emitted line readable.
            if (cooked.Length > 0) parts.Add(Quote(cooked));
            if (i < tpl.Expressions.Count) parts.Add("js_str(" + Expr(tpl.Expressions[i]) + ")");
        }
        // An empty template is still a string, and `` produces no parts at all.
        return parts.Count == 0 ? "\"\"" : parts.Count == 1 ? parts[0] : string.Join(" .. ", parts);
    }

    /// <summary>
    /// An object literal. Plain properties become a table; a getter becomes a function on the
    /// table's <c>__index</c>, which is Lua's own version of the same idea; a spread becomes a merge.
    /// </summary>
    private string Object(ObjectExpression obj)
    {
        var fields = new StringBuilder("{");
        var getters = new StringBuilder();
        var setters = new StringBuilder();
        var merge = new List<string>();
        var fieldCount = 0;

        foreach (var p in obj.Properties)
        {
            if (p is SpreadElement spread)
            {
                // `{ ...a, b: 1 }`: everything so far, then the spread, then whatever follows, in
                // order, because a later key wins in JS.
                if (fieldCount > 0 || merge.Count == 0) { merge.Add(fields.Append('}').ToString()); fields.Clear().Append('{'); fieldCount = 0; }
                merge.Add(Expr(spread.Argument));
                continue;
            }
            if (p is not ObjectProperty prop) { Unsupported(p, p.Type.ToString()); continue; }

            var key = prop.Key switch
            {
                Identifier k => k.Name,
                StringLiteral k => k.Value,
                NumericLiteral k => Number(k.Value),
                _ => null,
            };
            if (key == null) { Unsupported(p, "a computed property name"); continue; }

            if (prop.Kind == PropertyKind.Get)
            {
                if (prop.Value is not FunctionExpression g) { Unsupported(p, "a getter that is not a function"); continue; }
                if (getters.Length > 0) getters.Append(", ");
                // A getter is the one place `this` is real on these pages, and js_getters hands the
                // receiver in as the first argument, so it is named here.
                getters.Append('[').Append(Quote(key)).Append("] = ").Append(Lambda(g.Params, g.Body, "self"));
                continue;
            }
            if (prop.Kind == PropertyKind.Set)
            {
                if (prop.Value is not FunctionExpression st) { Unsupported(p, "a setter that is not a function"); continue; }
                if (setters.Length > 0) setters.Append(", ");
                setters.Append('[').Append(Quote(key)).Append("] = ").Append(Lambda(st.Params, st.Body, "self"));
                continue;
            }
            if (prop.Kind != PropertyKind.Init) { Unsupported(p, "a " + prop.Kind + " property"); continue; }

            if (fieldCount > 0) fields.Append(", ");
            fieldCount++;
            fields.Append('[').Append(Quote(key)).Append("] = ").Append(Expr(prop.Value));
        }
        fields.Append('}');

        var body = merge.Count > 0
            ? "js_merge(" + string.Join(", ", merge) + (fieldCount > 0 ? ", " + fields : "") + ")"
            : fields.ToString();
        // A setter needs __newindex, which costs a Lua call on EVERY write to the object, so an
        // object with only getters keeps the cheaper form.
        if (setters.Length > 0)
            return "js_accessors(" + body + ", {" + getters + "}, {" + setters + "})";
        return getters.Length > 0 ? "js_getters(" + body + ", {" + getters + "})" : body;
    }

    private string Lambda(in NodeList<Node> ps, Node body, string? receiver = null)
    {
        var declared = Params(ps, out var bind);
        var header = "function(" + (receiver == null ? declared : declared.Length == 0 ? receiver : receiver + ", " + declared) + ")";
        var hadReceiver = _receiver;
        if (receiver != null) _receiver = receiver;
        // the body writes lines into the shared buffer, so it is taken back out and inlined here
        var saved = _sb.Length;
        var hidden = Shadow(ps, body);
        Body(body, bind);
        Unshadow(hidden);
        var inner = _sb.ToString(saved, _sb.Length - saved);
        _sb.Length = saved;
        _receiver = hadReceiver;
        return header + "\n" + inner + new string(' ', _depth * 2) + "end";
    }

    /// <summary>
    /// <c>a?.b.c</c> and <c>f?.()</c>: a chain that stops at the first link that is not there.
    /// </summary>
    /// <remarks>
    /// The short circuit is the whole point and it covers the WHOLE chain, not one link: in
    /// JavaScript <c>a?.b.c</c> yields undefined when <c>a</c> is nullish rather than faulting on
    /// <c>.c</c>. So the steps are walked in order and the first nil ends it - which is also why
    /// this cannot be a chain of Lua `and`s, since those would yield false rather than nil and would
    /// stop on any falsy value rather than only on a missing one.
    /// </remarks>
    private string Chain(ChainExpression chain)
    {
        var steps = new List<Node>();
        for (Node at = chain.Expression; ;)
        {
            steps.Add(at);
            if (at is MemberExpression m) at = m.Object;
            else if (at is CallExpression c) at = c.Callee;
            else break;
        }
        steps.Reverse();

        var held = "__c" + (++_loop).ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder("(function() local ").Append(held).Append(" = ").Append(Expr((Expression)steps[0]));

        for (var i = 1; i < steps.Count; i++)
        {
            sb.Append(" if ").Append(held).Append(" == nil then return nil end ");
            switch (steps[i])
            {
                case MemberExpression m:
                    sb.Append(held).Append(" = ").Append(held)
                      .Append(m.Computed ? "[" + Expr(m.Property) + "]" : "[" + Quote(NameOf(m.Property)) + "]");
                    break;
                case CallExpression call:
                    {
                        var args = new StringBuilder();
                        foreach (var a in call.Arguments)
                        {
                            if (args.Length > 0) args.Append(", ");
                            args.Append(Expr(a));
                        }
                        // The receiver for a method call through a chain is the object the previous
                        // step came from, and it has already been consumed - so a chained method call
                        // goes through the dispatcher on the value itself.
                        sb.Append(held).Append(" = ").Append(held).Append('(').Append(args).Append(')');
                        break;
                    }
                default:
                    return Fail(chain, "an optional chain through " + steps[i].Type);
            }
        }
        return sb.Append(" return ").Append(held).Append(" end)()").ToString();
    }

    private static string NameOf(Node property) => property switch
    {
        Identifier i => i.Name,
        PrivateIdentifier p => "#" + p.Name,
        StringLiteral s => s.Value,
        _ => "?",
    };

    private string Member(MemberExpression m)
    {
        // `this.#count`. A private name is not reachable from outside the class in JavaScript, and
        // nothing here enforces that - but the NAME is distinct from any public one, so a class with
        // both `#x` and `x` keeps them apart, which is the part that would corrupt data if it did
        // not hold.
        if (m is { Computed: false, Property: PrivateIdentifier priv })
            return Expr(m.Object) + "[" + Quote("#" + priv.Name) + "]";

        var obj = Expr(m.Object);
        // Lua indexes a name, a call or a parenthesised expression - not a bare literal. `({a:7}).a`
        // would emit `{["a"] = 7}.a`, which does not parse.
        if (m.Object is ObjectExpression or ArrayExpression or StringLiteral or NumericLiteral
            or FunctionExpression or ArrowFunctionExpression or ConditionalExpression or LogicalExpression
            or NonLogicalBinaryExpression or NonUpdateUnaryExpression)
            obj = "(" + obj + ")";
        if (m.Computed) return obj + "[" + Expr(m.Property) + "]";
        if (m.Property is not Identifier p) return Fail(m, "that property access");
        // `.length` is a field on an array here and the character count on a string, so it asks
        if (p.Name == "length") return "js_len(" + obj + ")";
        return Keywords.Contains(p.Name) ? obj + "[" + Quote(p.Name) + "]" : obj + "." + p.Name;
    }

    private string Binary(NonLogicalBinaryExpression b)
    {
        if (b.Operator == Operator.Addition && Fold(b, out var folded) is { } choice && folded > 1) return Choose(choice);
        var l = Expr(b.Left);
        var r = Expr(b.Right);
        return b.Operator switch
        {
            // `+` is the one arithmetic operator that may concatenate instead, so it is the one
            // routed through a helper; -, * and / coerce to number in JS unconditionally.
            Operator.Addition => "js_add(" + l + ", " + r + ")",
            Operator.Subtraction => "(" + l + " - " + r + ")",
            Operator.Multiplication => "(" + l + " * " + r + ")",
            Operator.Division => "(" + l + " / " + r + ")",
            // JS % truncates toward zero where Lua's floors, so they differ on negatives. fmod is JS's.
            Operator.Remainder => "math.fmod(" + l + ", " + r + ")",
            Operator.Exponentiation => "(" + l + " ^ " + r + ")",
            Operator.StrictEquality => "(" + l + " == " + r + ")",
            Operator.StrictInequality => "(" + l + " ~= " + r + ")",
            // Loose equality coerces across types - `0 == "0"` and `1 == true` are both true - and
            // there is no short Lua equivalent. But the ONE form pages actually write is `x == null`,
            // meaning "null or undefined", and Lua's nil is exactly both of those. So that compiles
            // exactly and every other loose comparison is reported, which is a one-character fix for
            // whoever wrote it. Measured: every loose comparison across all six pages is this form.
            Operator.Equality when IsNull(b.Left) || IsNull(b.Right) => "(" + l + " == " + r + ")",
            Operator.Inequality when IsNull(b.Left) || IsNull(b.Right) => "(" + l + " ~= " + r + ")",
            Operator.Equality or Operator.Inequality =>
                Fail(b, "loose " + (b.Operator == Operator.Equality ? "==" : "!=") + " (use === or !==)"),
            Operator.LessThan => "(" + l + " < " + r + ")",
            Operator.LessThanOrEqual => "(" + l + " <= " + r + ")",
            Operator.GreaterThan => "(" + l + " > " + r + ")",
            Operator.GreaterThanOrEqual => "(" + l + " >= " + r + ")",

            // Bitwise, through helpers rather than Lua's own operators. JavaScript defines these on
            // 32-bit signed integers and Lua's are 64-bit, so `~a` and `a << 1` agree for small
            // positive numbers and diverge everywhere else - silently, and only for the inputs a
            // page hits in anger. The helpers truncate the way the language says.
            Operator.BitwiseAnd => "js_band(" + l + ", " + r + ")",
            Operator.BitwiseOr => "js_bor(" + l + ", " + r + ")",
            Operator.BitwiseXor => "js_bxor(" + l + ", " + r + ")",
            Operator.LeftShift => "js_shl(" + l + ", " + r + ")",
            Operator.RightShift => "js_shr(" + l + ", " + r + ")",
            Operator.UnsignedRightShift => "js_ushr(" + l + ", " + r + ")",

            // `k in o` asks about a KEY, and on an array about an index rather than a value.
            Operator.In => "js_in(" + l + ", " + r + ")",
            Operator.InstanceOf => "js_instanceof(" + l + ", " + r + ")",

            _ => Fail(b, "the " + b.Operator + " operator"),
        };
    }

    private string Unary(NonUpdateUnaryExpression u)
    {
        return u.Operator switch
        {
            Operator.UnaryNegation => "(-(" + Expr(u.Argument) + "))",
            Operator.UnaryPlus => "js_num(" + Expr(u.Argument) + ")",
            Operator.LogicalNot => "(not " + Truthy(u.Argument) + ")",
            Operator.BitwiseNot => "js_bnot(" + Expr(u.Argument) + ")",
            // `typeof x` is legal on a name nothing declares - it is how a page asks whether a host
            // object exists at all - so this is the one place an unknown name is not a problem.
            Operator.TypeOf => "js_typeof(" + (u.Argument is Identifier n && !_known.Contains(n.Name) && !Provided.Contains(n.Name)
                ? "nil" : Expr(u.Argument)) + ")",
            // a call, so the same emission works in value position and as a statement
            Operator.Delete when u.Argument is MemberExpression d =>
                "js_delete(" + Expr(d.Object) + ", " + (d.Computed ? Expr(d.Property) : Quote(((Identifier)d.Property).Name)) + ")",
            Operator.Void => "nil",
            _ => Fail(u, "unary " + u.Operator),
        };
    }

    private string Call(CallExpression c)
    {
        var args = new StringBuilder();
        var first = true;
        var spread = false;
        foreach (var a in c.Arguments) if (a is SpreadElement) { spread = true; break; }
        if (spread)
        {
            // `f(a, ...xs, b)`. The pieces are gathered into one array and unpacked, which is
            // correct wherever the spread sits - Lua only expands a call's LAST expression, so
            // emitting table.unpack in place would silently drop everything after it.
            args.Append("js_spread(js_concat(");
            foreach (var a in c.Arguments)
            {
                if (!first) args.Append(", ");
                first = false;
                args.Append(a is SpreadElement spreadArg ? "js_spread_of(" + Expr(spreadArg.Argument) + ")" : Expr(a));
            }
            args.Append("))");
        }
        else
        {
            foreach (var a in c.Arguments)
            {
                if (!first) args.Append(", ");
                first = false;
                args.Append(Expr(a));
            }
        }
        // `super(...)` and `super.method(...)`: both are a call on the BASE's prototype with this
        // instance as the receiver, which is what makes an inherited constructor or an overridden
        // method reachable. Neither goes through the method dispatcher - the name is known here, and
        // dispatching would find the derived override and recurse for ever.
        if (c.Callee is Super)
        {
            if (_superBase == null) return Fail(c, "`super()` outside a class that extends something");
            return _superBase + ".__proto.__ctor(" + Self + (args.Length > 0 ? ", " + args : "") + ")";
        }
        if (c.Callee is MemberExpression { Computed: false, Object: Super, Property: Identifier sp })
        {
            if (_superBase == null) return Fail(c, "`super." + sp.Name + "()` outside a class that extends something");
            return _superBase + ".__proto[" + Quote(sp.Name) + "](" + Self + (args.Length > 0 ? ", " + args : "") + ")";
        }

        // A method call goes through the prelude's dispatcher rather than Lua's `obj:name()`.
        // `"x".slice(1)` would otherwise need JavaScript's string methods on Lua's string metatable,
        // which is global: the generated chunk runs in its own _ENV precisely so it cannot reach
        // into the author's Lua, and mutating a shared metatable would walk straight past that.
        if (c.Callee is MemberExpression { Computed: false, Property: Identifier p } m && p.Name != "length")
        {
            if (!PreludeMethods.Contains(p.Name) && !_pageProperties.Contains(p.Name))
                Unsupported(c, "`." + p.Name + "()`, which the prelude does not provide");
            return "js_m(" + Expr(m.Object) + ", " + Quote(p.Name) + (args.Length > 0 ? ", " + args : "") + ")";
        }
        if (c.Callee is MemberExpression { Computed: true } cm)
            return "js_m(" + Expr(cm.Object) + ", " + Expr(cm.Property) + (args.Length > 0 ? ", " + args : "") + ")";
        // Lua calls only a name, a field or another call, so an immediately-invoked function
        // expression - `(function(){...})()`, the idiom a page uses for its setup block - has to
        // keep its parentheses.
        var callee = Expr(c.Callee);
        if (c.Callee is not (Identifier or MemberExpression or CallExpression)) callee = "(" + callee + ")";
        return callee + "(" + args + ")";
    }

    /// <summary>
    /// Built-in constructors, as the prelude's own makers. A page writes <c>new Map()</c> far more
    /// often than it writes a class, and these are the ones the corpus actually constructs.
    /// </summary>
    private static readonly Dictionary<string, string> Constructors = new(StringComparer.Ordinal)
    {
        ["Promise"] = "js_promise",
        ["Map"] = "js_map", ["Set"] = "js_set", ["WeakMap"] = "js_map", ["WeakSet"] = "js_set",
        ["Array"] = "js_new_array", ["Error"] = "js_error", ["TypeError"] = "js_error",
        ["RangeError"] = "js_error", ["Date"] = "js_date", ["Object"] = "js_new_object",
    };

    private string New(NewExpression n)
    {
        var args = new StringBuilder();
        foreach (var a in n.Arguments)
        {
            if (a is SpreadElement) { Unsupported(a, "a spread argument"); continue; }
            if (args.Length > 0) args.Append(", ");
            args.Append(Expr(a));
        }

        // A built-in has its own maker; anything else is a class, and js_new is what knows how to
        // instantiate one. A name that is neither is reported rather than handed to js_new, which
        // would fail at run time with no line from the page.
        if (n.Callee is Identifier id)
        {
            if (Constructors.TryGetValue(id.Name, out var maker)) return maker + "(" + args + ")";
            if (id.Name == "RegExp")
                return args.Length > 0 ? "js_regex(" + args + ")" : Fail(n, "`new RegExp()` with no pattern");
            if (!_known.Contains(id.Name))
                return Fail(n, "`new " + id.Name + "`, which neither the page nor the prelude defines");
        }
        return "js_new(" + Expr(n.Callee) + (args.Length > 0 ? ", " + args : "") + ")";
    }

    // ---- names, numbers and strings ------------------------------------------------------------

    /// <summary>
    /// A JavaScript name as a Lua one. JavaScript allows <c>$</c> and non-ASCII letters in an
    /// identifier where Lua allows neither, and a name that is a Lua keyword has to move aside.
    /// The mapping is injective - <c>_</c> is escaped too - so two names that differ in the page
    /// cannot become one here.
    /// </summary>
    private static string Safe(string name)
    {
        if (Keywords.Contains(name)) return "__kw_" + name;
        var clean = true;
        foreach (var ch in name)
        {
            if (ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')) continue;
            clean = false;
            break;
        }
        if (clean) return name;

        var sb = new StringBuilder(name.Length + 4);
        foreach (var ch in name)
        {
            if (ch is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')) sb.Append(ch);
            else sb.Append('_').Append(((int)ch).ToString("x2", CultureInfo.InvariantCulture));
        }
        // `$` is the only one any page in this repository uses, and it is always the page's own
        // shorthand for getElementById, so this reads as `_24("legA")`.
        return char.IsDigit(sb[0]) ? "_" + sb : sb.ToString();
    }

    /// <summary>A JS number as a Lua literal. "R" round-trips, and an integer stays one so table keys match.</summary>
    private static string Number(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e15
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("R", CultureInfo.InvariantCulture);

    internal static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var ch in s)
        {
            if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
            else if (ch == '\n') sb.Append("\\n");
            else if (ch == '\r') sb.Append("\\r");
            else if (ch == '\t') sb.Append("\\t");
            else if (ch < ' ') sb.Append('\\').Append(((int)ch).ToString(CultureInfo.InvariantCulture));
            else sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}

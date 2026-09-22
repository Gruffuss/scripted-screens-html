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
    private int _depth;
    /// <summary>Counts loops so each label is unique.</summary>
    private int _loop;
    /// <summary>The label a `continue` here belongs to: the ENCLOSING loop, saved and restored.</summary>
    private int _enclosing;
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

    /// <summary>Regular-expression syntax. A pattern containing any of these is reported, not guessed at.</summary>
    private static readonly char[] RegexMeta = { '\\', '^', '$', '.', '|', '?', '*', '+', '(', ')', '[', ']', '{', '}' };

    /// <summary>What the runtime prelude defines, so a reference to one is not reported as unknown.</summary>
    private static readonly HashSet<string> Provided = new(StringComparer.Ordinal)
    {
        "Math", "Number", "String", "Boolean", "JSON", "console", "document", "window",
        "localStorage", "performance", "Date", "isNaN", "parseFloat", "parseInt", "Infinity", "NaN",
        "undefined", "requestAnimationFrame", "setTimeout", "setInterval", "clearInterval", "Object",
        "location", "Map", "Set", "WeakMap", "WeakSet", "Error", "TypeError", "RangeError", "Array",
        "RegExp",
    };

    /// <summary>
    /// Names the chunk itself binds. A page declaring one of these would shadow it, and the failure
    /// would be the host finding no entry points at all rather than anything the page could see.
    /// </summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal) { "PAGE", "DOM", "Pending", "UNDEFINED" };

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
    };

    /// <summary>Property names the page itself defines, so its own methods are not reported as unknown.</summary>
    private readonly HashSet<string> _pageProperties = new(StringComparer.Ordinal);

    /// <summary>
    /// Compiles a page's script to Lua. Returns null when something in it cannot be translated, and
    /// <paramref name="problems"/> then names each one with its line.
    /// </summary>
    internal static string? Compile(string source, out IReadOnlyList<string> problems)
    {
        var c = new JsToLua();
        problems = c._problems;
        Script ast;
        try
        {
            ast = new Parser().ParseScript(source);
        }
        catch (Exception ex)
        {
            c._problems.Add("parse: " + ex.Message);
            return null;
        }

        // Every name the page declares anywhere, collected before a line is emitted. Emission order
        // is not declaration order in JavaScript - a function written early may use a `const`
        // written later - so an in-order check would report a name the page does define. This
        // over-approximates scope deliberately: it exists to catch a typo or an unsupported global,
        // not to reproduce block scoping.
        c.Declared(ast);

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
        c.OpenScope(names);

        foreach (var s in ast.Body)
        {
            if (s is Statement st) c.Statement(st);
        }

        // The page's whole top-level scope, by name, for the host to reach: the chunk's own bindings
        // are locals, so without this its frame callback is unreachable from outside it. State goes
        // in as well as functions - a table is by reference and so stays live, which is what lets a
        // compiled page be inspected while it runs.
        c.Line("PAGE = {}");
        foreach (var n in names) c.Line("PAGE." + n + " = " + n);

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

            case FunctionDeclaration f when f.Id != null:
                // At the top level this is forward-declared, so it assigns. Anywhere else it is a
                // `local function`, or it would overwrite - and destroy - an outer function of the
                // same name for the rest of the page.
                // always an assignment: every scope declares its own function names up front, so a
                // nested one shadows rather than overwriting an outer function of the same name
                Line(Safe(f.Id.Name) + " = function(" + Params(f.Params, out var fnBind) + ")");
                Body(f.Body, fnBind);
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

                case StaticBlock:
                    Unsupported(element, "a static initialisation block");
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
        sb.Append(indent).Append("return js_class(").Append(Quote(name)).Append(", ").Append(baseName)
          .Append(", ").Append(Table(methods))
          .Append(", ").Append(Table(getters))
          .Append(", ").Append(Table(setters))
          .Append(", ").Append(Table(statics)).Append(")\n");
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

    private void Assign(AssignmentExpression a)
    {
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
        if (a.Operator == Operator.Assignment && NumericStyle(a)) return;

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
    private void Try(TryStatement t)
    {
        if (t.Finalizer != null) Unsupported(t, "a `finally` block");
        foreach (var inner in Escapes(t.Block)) Unsupported(inner, "`" + inner.Type + "` inside a `try` block");

        Line("local __ok = pcall(function()");
        _depth++; Statement(t.Block); _depth--;
        Line("end)");
        if (t.Handler != null)
        {
            Line("if not __ok then");
            _depth++; Statement(t.Handler.Body); _depth--;
            Line("end");
        }
    }

    /// <summary>Statements in a try block whose effect escapes it, which a pcall closure swallows.</summary>
    private static IEnumerable<Node> Escapes(Node block)
    {
        foreach (var n in Walk(block))
        {
            if (n is ReturnStatement or BreakStatement or ContinueStatement or VariableDeclaration) yield return n;
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
            Line("do return " + Expr(e) + " end");
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
    private static bool NeverFalsy(Expression e) => e switch
    {
        NumericLiteral or StringLiteral => true,
        BooleanLiteral b => b.Value,
        ObjectExpression or ArrayExpression or FunctionExpression or ArrowFunctionExpression => true,
        // arithmetic yields a number, and `+` yields a number or a string; neither is false or nil
        NonLogicalBinaryExpression a => a.Operator is Operator.Addition or Operator.Subtraction
            or Operator.Multiplication or Operator.Division or Operator.Remainder or Operator.Exponentiation,
        NonUpdateUnaryExpression u => u.Operator is Operator.UnaryNegation or Operator.UnaryPlus or Operator.TypeOf,
        ConditionalExpression c => NeverFalsy(c.Consequent) && NeverFalsy(c.Alternate),
        _ => false,
    };

    /// <summary>`null`, or the `undefined` that is spelled as a bare name; Lua's nil is both.</summary>
    private static bool IsNull(Expression e) => e is NullLiteral || (e is Identifier { Name: "undefined" });

    private static bool IsComparison(Operator op) => op is Operator.Equality or Operator.Inequality
        or Operator.StrictEquality or Operator.StrictInequality or Operator.LessThan
        or Operator.LessThanOrEqual or Operator.GreaterThan or Operator.GreaterThanOrEqual;

    private string Expr(Node? e)
    {
        switch (e)
        {
            case null:
                return "nil";

            case Identifier id:
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
            case LogicalExpression { Operator: Operator.NullishCoalescing } nc:
                return "(function() local __v = " + Expr(nc.Left) + " if __v ~= nil then return __v end return "
                       + Expr(nc.Right) + " end)()";

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
                // Only a pattern that is plain characters, which the prelude treats as a literal
                // match. A real regular expression is reported rather than approximated: a Lua
                // pattern is not a regex, and quietly matching something else is the worst outcome
                // available. Every one on these pages is a single character being escaped for HTML.
                return re.Raw.IndexOfAny(RegexMeta) >= 0
                    ? Fail(e, "the regular expression " + re.Raw)
                    : "js_regex(" + Quote(re.Raw.Substring(1, re.Raw.LastIndexOf('/') - 1))
                      + ", " + Quote(re.Raw.Substring(re.Raw.LastIndexOf('/') + 1)) + ")";

            case TemplateLiteral tpl:
                return Template(tpl);

            case TaggedTemplateExpression:
                // A tag is a function taking the pieces and the values separately, which is a
                // different thing from a template and worth naming rather than lumping in with
                // "TaggedTemplateExpression is not translatable".
                return Fail(e, "a tagged template");

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
            if (prop.Kind != PropertyKind.Init) { Unsupported(p, "a setter"); continue; }

            if (fieldCount > 0) fields.Append(", ");
            fieldCount++;
            fields.Append('[').Append(Quote(key)).Append("] = ").Append(Expr(prop.Value));
        }
        fields.Append('}');

        var body = merge.Count > 0
            ? "js_merge(" + string.Join(", ", merge) + (fieldCount > 0 ? ", " + fields : "") + ")"
            : fields.ToString();
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
        Body(body, bind);
        var inner = _sb.ToString(saved, _sb.Length - saved);
        _sb.Length = saved;
        _receiver = hadReceiver;
        return header + "\n" + inner + new string(' ', _depth * 2) + "end";
    }

    private string Member(MemberExpression m)
    {
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
        foreach (var a in c.Arguments)
        {
            if (a is SpreadElement) { Unsupported(a, "a spread argument"); continue; }
            if (!first) args.Append(", ");
            first = false;
            args.Append(Expr(a));
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

    private static string Quote(string s)
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

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
    /// <summary>Names already declared at the top of the chunk, so their statement assigns rather than redeclares.</summary>
    private readonly HashSet<string> _hoisted = new(StringComparer.Ordinal);
    private int _depth;
    private int _loop;

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
        "location", "Array",
    };

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
        var names = new List<string>();
        foreach (var s in ast.Body)
        {
            if (s is FunctionDeclaration { Id: { } id }) names.Add(Safe(id.Name));
            else if (s is VariableDeclaration vd)
                foreach (var d in vd.Declarations)
                    if (d.Id is Identifier vid) names.Add(Safe(vid.Name));
        }
        foreach (var n in names) c._hoisted.Add(n);
        for (var i = 0; i < names.Count; i += 40)
            c.Line("local " + string.Join(", ", names.GetRange(i, Math.Min(40, names.Count - i))));

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

    /// <summary>Walks the whole tree for every name the page binds, so the unknown-name check is order-free.</summary>
    private void Declared(Node n)
    {
        if (n is VariableDeclarator { Id: Identifier v }) _known.Add(v.Name);
        if (n is FunctionDeclaration fd)
        {
            if (fd.Id != null) _known.Add(fd.Id.Name);
            Parameters(fd.Params);
        }
        if (n is FunctionExpression fe) Parameters(fe.Params);
        if (n is ArrowFunctionExpression ae) Parameters(ae.Params);
        if (n is CatchClause { Param: Identifier c }) _known.Add(c.Name);
        foreach (var kid in n.ChildNodes) if (kid != null) Declared(kid);

        void Parameters(in NodeList<Node> ps)
        {
            foreach (var p in ps) if (p is Identifier pid) _known.Add(pid.Name);
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
                    if (d.Id is not Identifier id) { Unsupported(d, "a destructuring declaration"); continue; }
                    var init = d.Init == null ? "nil" : Expr(d.Init);
                    _known.Add(id.Name);
                    var name = Safe(id.Name);
                    Line((_hoisted.Contains(name) ? "" : "local ") + name + " = " + init);
                }
                break;

            case FunctionDeclaration f when f.Id != null:
                // forward-declared above, so this is an assignment rather than a `local function`
                Line(Safe(f.Id.Name) + " = function(" + Params(f.Params) + ")");
                Body(f.Body);
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

            case ContinueStatement:
                Line("goto continue" + _loop.ToString(CultureInfo.InvariantCulture));
                break;

            case BreakStatement:
                Line("break");
                break;

            case TryStatement t:
                // pcall: on these pages try only ever guards localStorage and JSON, so the error
                // value is never read and the handler is a plain fallback.
                Line("local __ok = pcall(function()");
                _depth++; Statement(t.Block); _depth--;
                Line("end)");
                if (t.Handler != null)
                {
                    Line("if not __ok then");
                    _depth++; Statement(t.Handler.Body); _depth--;
                    Line("end");
                }
                break;

            case EmptyStatement:
                break;

            default:
                Unsupported(s, s.Type.ToString());
                break;
        }
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

    private void ForLoop(ForStatement f)
    {
        // A JS `for` is a while loop with an initialiser and an update, which is what this emits
        // rather than recognising a numeric range: the range form would be wrong the moment the body
        // writes the counter, and nothing is gained by it.
        Line("do");
        _depth++;
        if (f.Init is VariableDeclaration vd) Statement(vd);
        else if (f.Init is Expression ie) ExprStatement(ie);
        var mine = ++_loop;
        Line("while " + (f.Test == null ? "true" : Truthy(f.Test)) + " do");
        _depth++;
        Statement(f.Body);
        Line("::continue" + mine.ToString(CultureInfo.InvariantCulture) + "::");
        if (f.Update != null) ExprStatement(f.Update);
        _depth--;
        Line("end");
        _depth--;
        Line("end");
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
        Line("local " + Safe(id.Name) + " = " + n + "[__i]");
        Statement(f.Body);
        Line("::continue" + mine.ToString(CultureInfo.InvariantCulture) + "::");
        _depth--;
        Line("end");
        _depth--;
        Line("end");
    }

    private void Body(Node body)
    {
        _depth++;
        if (body is BlockStatement b) { foreach (var s in b.Body) if (s is Statement st) Statement(st); }
        else if (body is Expression e) Line("do return " + Expr(e) + " end");
        _depth--;
    }

    private string Params(in NodeList<Node> ps)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < ps.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            if (ps[i] is Identifier id) { _known.Add(id.Name); sb.Append(Safe(id.Name)); }
            else { Unsupported(ps[i], "a parameter that is not a plain name"); sb.Append("__p").Append(i.ToString(CultureInfo.InvariantCulture)); }
        }
        return sb.ToString();
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

            case ThisExpression:
                return "self";

            case MemberExpression m:
                return Member(m);

            case NonLogicalBinaryExpression bin:
                return Binary(bin);

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

            default:
                return Fail(e, e.Type.ToString());
        }
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
        var declared = Params(ps);
        var header = "function(" + (receiver == null ? declared : declared.Length == 0 ? receiver : receiver + ", " + declared) + ")";
        // the body writes lines into the shared buffer, so it is taken back out and inlined here
        var saved = _sb.Length;
        Body(body);
        var inner = _sb.ToString(saved, _sb.Length - saved);
        _sb.Length = saved;
        return header + "\n" + inner + new string(' ', _depth * 2) + "end";
    }

    private string Member(MemberExpression m)
    {
        var obj = Expr(m.Object);
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
            Operator.StrictEquality or Operator.Equality => "(" + l + " == " + r + ")",
            Operator.StrictInequality or Operator.Inequality => "(" + l + " ~= " + r + ")",
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
        // A method call goes through the prelude's dispatcher rather than Lua's `obj:name()`.
        // `"x".slice(1)` would otherwise need JavaScript's string methods on Lua's string metatable,
        // which is global: the generated chunk runs in its own _ENV precisely so it cannot reach
        // into the author's Lua, and mutating a shared metatable would walk straight past that.
        if (c.Callee is MemberExpression { Computed: false, Property: Identifier p } m && p.Name != "length")
            return "js_m(" + Expr(m.Object) + ", " + Quote(p.Name) + (args.Length > 0 ? ", " + args : "") + ")";
        if (c.Callee is MemberExpression { Computed: true } cm)
            return "js_m(" + Expr(cm.Object) + ", " + Expr(cm.Property) + (args.Length > 0 ? ", " + args : "") + ")";
        // Lua calls only a name, a field or another call, so an immediately-invoked function
        // expression - `(function(){...})()`, the idiom a page uses for its setup block - has to
        // keep its parentheses.
        var callee = Expr(c.Callee);
        if (c.Callee is not (Identifier or MemberExpression or CallExpression)) callee = "(" + callee + ")";
        return callee + "(" + args + ")";
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

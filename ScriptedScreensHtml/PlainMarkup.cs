using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Acornima;
using Acornima.Ast;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

internal static partial class PlainTranslator
{
    /// <summary>A function inlined at one call: each parameter's argument, with the scope that argument is read in.</summary>
    private sealed class Inlined : Dictionary<Identifier, (Expression Expr, Inlined? Env)>
    {
        public static Inlined Of(IFunction fn, CallExpression call, Inlined? env)
        {
            var b = new Inlined();
            for (var i = 0; i < fn.Params.Count; i++)
                if (fn.Params[i] is Identifier p)
                    b[p] = (i < call.Arguments.Count && call.Arguments[i] is Expression a and not SpreadElement ? a : new Identifier("undefined"), env);
            return b;
        }
    }

    /// <summary>
    /// innerHTML, part 1: markup a script writes into an element it names, compiled once. The string
    /// literals are structure and the values between them are slots, so the Lua writes values and picks
    /// shapes and never builds a string (CLAUDE.md, THE SPEC).
    /// </summary>
    /// <remarks>
    /// The markup is read from the source as a template: literals, values, and choices (a ternary, or
    /// a helper function's `if ... return`). Each combination of choices is one SHAPE. What each shape
    /// does is then said with the features translated already, so the rest of the compile needs nothing
    /// new: markup that is only text and inline tags is the element's rich text, written as textContent
    /// is (one text shape per markup shape); markup with elements is laid out as the union of every
    /// shape's elements in the element, one shown at a time as a laid-out state, and each value is a
    /// write to the element it lands in - its text, a style property, its classes, an attribute. The
    /// elements a shape makes are elements of the page like any other, so lookups, listeners and reads
    /// reach them.
    /// </remarks>
    private sealed partial class Page
    {
        private abstract class Mk { }
        private sealed class MkLit : Mk { public readonly string Text; public MkLit(string text) { Text = text; } }
        /// <summary>A value: <see cref="Expr"/> is read in its own scope (a wrapper, <see cref="_bindOf"/>).</summary>
        private sealed class MkVal : Mk
        {
            public readonly Expression Expr;
            /// <summary>What the helper returning this value did first (`acts.push(fn)`), done right before the write it lands in.</summary>
            public readonly List<Expression>? Before;
            public MkVal(Expression expr, List<Expression>? before = null) { Expr = expr; Before = before; }
        }
        private sealed class MkAlt : Mk
        {
            public readonly Expression Test;
            public readonly List<Mk> Yes, No;
            public MkAlt(Expression test, List<Mk> yes, List<Mk> no) { Test = test; Yes = yes; No = no; }
        }

        /// <summary>One `el.innerHTML = …`: its template, and per shape the writes it stands for, in the order the Lua picks them.</summary>
        private sealed class MarkupWrite
        {
            public AssignmentExpression At = null!;
            public MarkupOf Of = null!;
            public List<Mk> Template = null!;
            /// <summary>Per shape, in template order: what it lays out and the writes (synthetic nodes) it makes.</summary>
            public readonly List<ShapeOut> Shapes = new();
        }

        /// <summary>
        /// One shape of a markup write, laid out: its elements and its writes, and for each list in it, each row's
        /// elements and writes per shape of the row. Its states are <see cref="Base"/> on, one per length of its lists.
        /// </summary>
        private sealed class ShapeOut
        {
            public int Base;
            public readonly List<MkRep> Reps = new();
            public readonly List<Expression> Ops = new();
            /// <summary>Its top elements outside any list's rows, and every element it makes outside them.</summary>
            public readonly List<Target> Tops = new(), Made = new();
            public readonly Dictionary<(MkRep, int, int), List<Expression>> RowOps = new();
            public readonly Dictionary<(MkRep, int, int), List<Target>> RowTops = new(), RowMade = new();

            public List<Target> TopsOf(MkRep rep, int k, int c) => RowTops.TryGetValue((rep, k, c), out var l) ? l : new List<Target>();
        }

        /// <summary>One element the script writes markup into, over every place it does.</summary>
        private sealed class MarkupOf
        {
            public Target T = null!;
            /// <summary>Only text and inline tags: the element's rich text. Otherwise elements laid out as a union.</summary>
            public bool Inline;
            /// <summary>What the element holds as the page wrote it, which shows until a write replaces it.</summary>
            public bool KeepOwn;
            public readonly List<MarkupWrite> Writes = new();
            /// <summary>The element's own children as the page wrote them, when they show until a write.</summary>
            public readonly List<Target> Own = new();
            /// <summary>Every shape of every write, in the order the states are numbered.</summary>
            public readonly List<ShapeOut> Shapes = new();
            /// <summary>Per state: the elements shown of <see cref="Hide"/>, every other one hidden.</summary>
            public readonly List<HashSet<Target>> States = new();
            /// <summary>Every element a state shows or hides: tops of shapes, and each row's tops per shape of the row.</summary>
            public readonly List<Target> Hide = new();
            public readonly List<Target> Made = new();
            public StylePlan? Plan;
            /// <summary>The facet its states claim their slots as; shared by elements laid out together.</summary>
            public string? Facet;
            /// <summary>Laid out together with other elements: the Lua variable holding its state, and the combination's key.</summary>
            public string? KeyVar, JointKey;
        }

        private readonly Dictionary<AssignmentExpression, MarkupWrite> _markup = new();
        /// <summary>Every markup write looked at, compiled or refused (with its reason) here.</summary>
        private readonly HashSet<AssignmentExpression> _markupSeen = new();
        private readonly Dictionary<Target, MarkupOf> _markupOf = new();
        private readonly Dictionary<Target, MarkupOf> _madeBy = new();
        private readonly HashSet<Node> _synthetic = new();
        private readonly Dictionary<Node, Node> _origin = new();
        private readonly Dictionary<ParenthesizedExpression, Inlined?> _bindOf = new();
        private readonly Dictionary<Expression, List<Piece>> _givenPieces = new();
        /// <summary>While a value of markup translates: the scope its names are read in.</summary>
        private Inlined? _active;

        private const int MostShapes = 32;
        /// <summary>The most states an element's markup is laid out in: its shapes, times the lengths of their lists.</summary>
        private const int MostStates = 160;
        private const char Open = '\uE000', Close = '\uE001';

        /// <summary>Text-level tags: markup of only these (and text) is its element's rich text.</summary>
        private static readonly HashSet<string> TextTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "b", "strong", "i", "em", "u", "s", "span", "br", "small", "big", "font", "code", "sub", "sup", "mark", "a",
            "abbr", "cite", "q", "kbd", "samp", "var", "time", "dfn", "del", "ins", "bdi", "wbr", "data", "strike", "tt",
        };

        private static Expression Unwrap(Expression e)
        {
            while (e is ParenthesizedExpression p) e = p.Expression;
            return e;
        }

        /// <summary>An expression read in a function's scope, as a node of its own that knows that scope.</summary>
        private Expression Within(Expression e, Inlined? env, Node origin)
        {
            if (env == null || env.Count == 0) return e;
            var w = new ParenthesizedExpression(e);
            _bindOf[w] = env;
            _origin[w] = origin;
            return w;
        }

        private T Made<T>(T node, Node origin) where T : Node
        {
            _synthetic.Add(node);
            _origin[node] = origin;
            return node;
        }

        // ---- finding the writes ---------------------------------------------------------------------

        /// <summary>Every `el.innerHTML = …` of the script, compiled into the page before anything else is looked up.</summary>
        private void Markups()
        {
            var writes = Markup.Everything(_ast).OfType<AssignmentExpression>()
                .Where(a => a.Left is MemberExpression { Computed: false, Property: Identifier { Name: "innerHTML" } } && a.Operator == Operator.Assignment && Effect(a))
                .ToList();
            if (writes.Count == 0) return;
            lock (PageCompiler.Gate)
            {
                foreach (var a in writes)
                {
                    _markupSeen.Add(a);
                    var m = (MemberExpression)a.Left;
                    var els = Elems(m.Object);
                    if (els == null || els.Bad != null || !els.One || !Lookupish(m.Object))
                    {
                        Refuse(a, els?.Bad ?? "innerHTML on an element chosen at run time (not translated yet: one element the script names is)");
                        continue;
                    }
                    var t = els.Ts[0];
                    if (Tpl(a.Right, null, a, 0) is not { } tpl) continue;
                    // `el.innerHTML = …;` then `el.innerHTML += …` (in loops, too): one write, made after its last step
                    Statement? last = null;
                    if (_parent[a] is ExpressionStatement start && _parent.TryGetValue(start, out var block) && Stmts(block) is { } list)
                    {
                        var seen = new List<AssignmentExpression>();
                        var consumed = new List<Statement>();
                        if (Sequence(list, list.IndexOf(start) + 1, AppendToElement(t, seen), null, a, 0, bindDecls: false, out _, consumed) is not { } more) continue;
                        if (consumed.Count > 0)
                        {
                            tpl = tpl.Concat(more).ToList();
                            _silenced.Add(start);
                            foreach (var s in consumed) _silenced.Add(s);
                            foreach (var plus in seen) _markupSeen.Add(plus);
                            last = consumed[^1];
                        }
                    }
                    if (!_markupOf.TryGetValue(t, out var of)) _markupOf[t] = of = new MarkupOf { T = t };
                    var w = new MarkupWrite { At = a, Of = of, Template = tpl };
                    of.Writes.Add(w);
                    _markup[a] = w;
                    if (last != null) _markupAt[last] = w;
                }
                foreach (var of in _markupOf.Values.ToList())
                {
                    var before = _refused.Count;
                    Lay(of);
                    if (_refused.Count > before) _markupOf.Remove(of.T);
                }
            }
        }

        // ---- the template ---------------------------------------------------------------------------

        /// <summary>
        /// Markup as literals, values and choices. Null (with a refusal) for markup whose shape the compile
        /// cannot bound, or a list (part 2).
        /// </summary>
        private List<Mk>? Tpl(Expression e, Inlined? env, Node write, int depth)
        {
            if (depth > 32) { Refuse(write, "markup nested more deeply than the compile follows"); return null; }
            switch (e)
            {
                case StringLiteral s:
                    return new List<Mk> { new MkLit(s.Value) };

                case TemplateLiteral t:
                    {
                        var list = new List<Mk>();
                        for (var i = 0; i < t.Quasis.Count; i++)
                        {
                            var q = t.Quasis[i].Value.Cooked ?? t.Quasis[i].Value.Raw ?? string.Empty;
                            if (q.Length > 0) list.Add(new MkLit(q));
                            if (i < t.Expressions.Count)
                            {
                                if (Tpl(t.Expressions[i], env, write, depth + 1) is not { } part) return null;
                                list.AddRange(part);
                            }
                        }
                        return list;
                    }

                case NonLogicalBinaryExpression { Operator: Operator.Addition }:
                    {
                        var chain = new List<Expression>();
                        Expression at = e;
                        while (at is NonLogicalBinaryExpression { Operator: Operator.Addition } b) { chain.Insert(0, b.Right); at = b.Left; }
                        chain.Insert(0, at);
                        var parts = new List<List<Mk>>();
                        foreach (var c in chain)
                        {
                            if (Tpl(c, env, write, depth + 1) is not { } part) return null;
                            parts.Add(part);
                        }
                        // the first operand that is text: a string, or markup - not a number the compile knows (`i + 1` in a row)
                        var first = Enumerable.Range(0, chain.Count).Where(k => chain[k] is StringLiteral or TemplateLiteral
                            || parts[k].Any(m => m is MkRep or MkAlt || m is MkLit && Finite(chain[k], null, env) is not [double])).DefaultIfEmpty(-1).First();
                        if (first < 0) return Value(e, env, write);
                        var list = new List<Mk>();
                        if (first > 0)
                        {
                            // the operands before the first text are added as JavaScript adds them: one value
                            Expression head = e;
                            for (var k = chain.Count - 1; k >= first; k--) head = ((NonLogicalBinaryExpression)head).Left;
                            list.AddRange(Value(head, env, write) ?? new List<Mk>());
                        }
                        for (var k = first; k < chain.Count; k++) list.AddRange(parts[k]);
                        return list;
                    }

                case ConditionalExpression c:
                    {
                        if (Tpl(c.Consequent, env, write, depth + 1) is not { } yes || Tpl(c.Alternate, env, write, depth + 1) is not { } no) return null;
                        // `on ? 'ON' : 'OFF'` is a value from a fixed set; a side with markup, or text around a value, is a shape
                        if (Plain(yes) && Plain(no)) return Value(e, env, write);
                        if (!Readable(c.Test, env, write)) return null;
                        return new List<Mk> { new MkAlt(Within(c.Test, env, c.Test), yes, no) };
                    }

                case LogicalExpression l:
                    {
                        if (Tpl(l.Left, env, write, depth + 1) is not { } left || Tpl(l.Right, env, write, depth + 1) is not { } right) return null;
                        if (Plain(left) && Plain(right)) return Value(e, env, write);
                        Refuse(l, "markup chosen with && or || (a ternary chooses markup; not translated yet)");
                        return null;
                    }

                // a name given markup in steps inside a function inlined here: the markup it holds by then
                case Identifier ready when _templateOf.TryGetValue(ready, out var held):
                    return held;

                case Identifier id when Decl(id) is { } decl:
                    {
                        if (env != null && env.TryGetValue(decl, out var arg)) return Tpl(arg.Expr, arg.Env, write, depth + 1);
                        if (Given(decl) is [{ } init] && !_writes.ContainsKey(decl) && Markupish(init))
                            return Tpl(init, null, write, depth + 1);
                        // `let h = ''; for (…) h += '<li>…'; el.innerHTML = h;`
                        if (_writes.ContainsKey(decl) && Markupish(id)) return Accumulated(decl, id, write, depth);
                        return Value(e, env, write);
                    }

                // a helper returning markup, or part of a tag (`' data-act="' + n + '"'`): its markup, the attribute's name fixed
                case CallExpression { Callee: Identifier f } call when Function(f) is { } fn && (Markupish((Node)(object)fn) || Attributish(fn)):
                    {
                        // a function written as an argument changes nothing by being passed
                        if (call.Arguments.Any(a => a is SpreadElement || a is not IFunction && Writes(a))) { Refuse(call, $"markup from \"{f.Name}\" called with arguments that change something (not translated yet)"); return null; }
                        var bind = Inlined.Of(fn, call, env);
                        var inlined = fn.Body is Expression body ? Tpl(body, bind, write, depth + 1) : Steps(((FunctionBody)fn.Body).Body, 0, bind, write, depth + 1, fn);
                        return inlined;
                    }

                // a list: `items.map(x => '<li>' + x + '</li>').join('')`, with slices and filters before the map
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "join" } } jm } join
                    when Unwrap(jm.Object) is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "map" } } } map:
                    return Mapped(join, map, env, write, depth);

                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "join" or "map" or "reduce" or "repeat" or "concat" or "flatMap" } } } list when Markupish(list):
                    Refuse(list, "a list of markup built in a way the compile does not follow (.map(…).join(…), and loops adding markup, are followed)");
                    return null;

                default:
                    return Value(e, env, write);
            }
        }

        /// <summary>A template that is one value, or one literal with no markup in it: a value, not a shape.</summary>
        private static bool Plain(List<Mk> t)
            => t.Count == 1 && (t[0] is MkVal { Before: null } || t[0] is MkLit l && l.Text.IndexOfAny(new[] { '<', '&' }) < 0);

        private List<Mk>? Value(Expression e, Inlined? env, Node write)
        {
            if (Markupish(e))
            {
                Refuse(e, "markup built in a way the compile cannot follow (a list is followed as .map(…).join(…) or a loop adding markup)");
                return null;
            }
            if (!Readable(e, env, write)) return null;
            var value = Within(e, env, e);
            // one value in every run (`unit || '%'` with no unit passed) is text of the markup, not a slot
            if (Finite(value) is [var only]) return new List<Mk> { new MkLit(Text(only)) };
            return new List<Mk> { new MkVal(value) };
        }

        /// <summary>A helper's body, statement by statement: its constants bound, `if (…) return A;` a choice, `return B;` the end.</summary>
        private List<Mk>? Steps(NodeList<Statement> body, int i, Inlined bind, Node write, int depth, IFunction fn)
        {
            for (; i < body.Count; i++)
            {
                switch (body[i])
                {
                    // `let h = ''; for (…) h += '<li>…'; … return h;`: h holds the markup the steps add
                    case VariableDeclaration { Declarations: [{ Id: Identifier hi, Init: { } hinit }] } hd
                        when _writes.TryGetValue(hi, out var hw) && hw.OfType<AssignmentExpression>().Any(a => Markupish(a.Right)):
                        {
                            if (Tpl(hinit, bind, write, depth + 1) is not { } head) return null;
                            var consumed = new List<Statement>();
                            var list = body.ToList();
                            if (Sequence(list, i + 1, AppendTo(hi), bind, write, depth, bindDecls: true, out var end, consumed) is not { } steps) return null;
                            var inside = new HashSet<Node>(consumed.SelectMany(Markup.Everything));
                            if (_writes[hi].Any(x => !inside.Contains(x)))
                            {
                                Refuse(hd, $"\"{hi.Name}\" given markup in steps and changed again later (not translated yet)");
                                return null;
                            }
                            var ready = new Identifier(hi.Name);
                            _templateOf[ready] = head.Concat(steps).ToList();
                            bind[hi] = (ready, bind);
                            i = end - 1;
                            continue;
                        }
                    case VariableDeclaration vd when vd.Declarations.All(d => d.Id is Identifier di && d.Init != null && !_writes.ContainsKey(di)):
                        foreach (var d in vd.Declarations) bind[(Identifier)d.Id] = (d.Init!, bind);
                        continue;
                    case ReturnStatement { Argument: { } r }:
                        return Tpl(r, bind, write, depth + 1);
                    // `color = color || 'var(--x)'`: a parameter or constant given a value again, read from here on
                    case ExpressionStatement { Expression: AssignmentExpression { Operator: Operator.Assignment, Left: Identifier li } a }
                        when Decl(li) is { } again && bind.ContainsKey(again) && !Writes(a.Right):
                        {
                            var before = new Inlined();
                            foreach (var pair in bind) before[pair.Key] = pair.Value;
                            bind[again] = (a.Right, before);
                            continue;
                        }
                    case IfStatement ifs:
                        {
                            if (Branch(ifs.Consequent, bind, write, depth, fn) is not { } yes) return null;
                            var no = ifs.Alternate != null ? Branch(ifs.Alternate, bind, write, depth, fn) : Steps(body, i + 1, bind, write, depth, fn);
                            if (no == null || !Readable(ifs.Test, bind, write)) return null;
                            return new List<Mk> { new MkAlt(Within(ifs.Test, bind, ifs.Test), yes, no) };
                        }
                    // `acts.push(fn);`: done for what it does, right before the value after it is read
                    case ExpressionStatement { Expression: var fx } when !Writes(fx):
                        {
                            if (!Readable(fx, bind, write)) return null;
                            var fxEnv = Copy(bind);
                            if (Steps(body, i + 1, bind, write, depth, fn) is not { } rest) return null;
                            return Ahead(rest, Within(fx, fxEnv, fx), body[i]);
                        }
                }
                Refuse(body[i], "a function building markup with statements the compile cannot follow (constants, if and return are followed)");
                return null;
            }
            Refuse((Node)(object)fn, "a function building markup that can end without returning it");
            return null;
        }

        private List<Mk>? Branch(Statement s, Inlined bind, Node write, int depth, IFunction fn)
        {
            switch (s)
            {
                case BlockStatement b: return Steps(b.Body, 0, bind, write, depth, fn);
                case ReturnStatement { Argument: { } r }: return Tpl(r, bind, write, depth + 1);
                default:
                    Refuse(s, "a function building markup with statements the compile cannot follow (constants, if and return are followed)");
                    return null;
            }
        }

        /// <summary>Whether markup text is written in an expression (or a function returns some): a string with a tag in it.</summary>
        private bool Markupish(Node n)
        {
            foreach (var x in Markup.Everything(n))
            {
                if (x is StringLiteral s && s.Value.IndexOf('<') >= 0) return true;
                if (x is TemplateElement q && (q.Value.Cooked ?? string.Empty).IndexOf('<') >= 0) return true;
                // a name holding markup, built in steps (`s += '<li>…'`)
                if (x is Identifier id && Reference(id) && Decl(id) is { } d && d != id && _writes.TryGetValue(d, out var ws)
                    && ws.OfType<AssignmentExpression>().Any(a => Markup.Everything(a.Right).Any(y => y is StringLiteral { Value: var v } && v.IndexOf('<') >= 0
                                                                                                      || y is TemplateElement t && (t.Value.Cooked ?? string.Empty).IndexOf('<') >= 0)))
                    return true;
            }
            return false;
        }

        private static bool Writes(Node n) => Markup.Everything(n).Any(x => x is AssignmentExpression or UpdateExpression);

        /// <summary>
        /// Whether a value of markup reads only what the write can see: a helper's own names are its arguments
        /// and constants (bound), the rest must be in scope where the markup is written.
        /// </summary>
        private bool Readable(Expression e, Inlined? env, Node write)
        {
            if (Writes(e)) { Refuse(e, "a value in markup that changes something as it is read (not translated yet)"); return false; }
            foreach (var x in Markup.Everything(e))
            {
                if (x is not Identifier id || !Reference(id) || Decl(id) is not { } d || d == id) continue;
                if (Bound(env, d) || Visible(d, write)) continue;
                Refuse(id, $"\"{id.Name}\", read by markup from inside a function the compile cannot inline there");
                return false;
            }
            return true;

            static bool Bound(Inlined? b, Identifier d) => b != null && b.ContainsKey(d);
        }

        /// <summary>
        /// The names a function made in markup reads that the compile stands in for with an expression the Lua works
        /// out where it is read (a list's row, a helper's argument), rather than a variable the function captures.
        /// </summary>
        private List<Identifier> LateNames(IFunction fn, Inlined env)
        {
            var late = new List<Identifier>();
            foreach (var x in Markup.Everything((Node)fn.Body))
                if (x is Identifier id && Reference(id) && Decl(id) is { } d && !late.Contains(d) && env.TryGetValue(d, out var arg) && Late(arg.Expr, arg.Env)) late.Add(d);
            return late;

            bool Late(Expression e, Inlined? at)
                => Unwrap(e) switch
                {
                    Acornima.Ast.Literal => false,
                    Identifier i when _lua.ContainsKey(i) => true,
                    Identifier i when Decl(i) is { } di && at != null && at.TryGetValue(di, out var next) => Late(next.Expr, next.Env),
                    Identifier => false,
                    _ => true,
                };
        }

        /// <summary>Whether a function returns part of a tag: a string starting with an attribute (`' data-act="'`), as a helper adding one to markup does.</summary>
        private static bool Attributish(IFunction fn)
            => Markup.Everything((Node)fn).Any(x => x is StringLiteral s && AttrStart.IsMatch(s.Value)
                                                  || x is TemplateElement q && AttrStart.IsMatch(q.Value.Cooked ?? string.Empty));

        private static readonly System.Text.RegularExpressions.Regex AttrStart = new(@"^\s+[A-Za-z_:][-A-Za-z0-9_:.]*=[""']");

        /// <summary>
        /// A helper's template with something it does first (`acts.push(fn);`) done right before its first value is
        /// read: kept on that value, or on the first value of each side of a choice before it. Null, refused, when no
        /// value follows.
        /// </summary>
        private List<Mk>? Ahead(List<Mk> tpl, Expression fx, Node at)
        {
            var list = new List<Mk>(tpl);
            for (var i = 0; i < list.Count; i++)
                switch (list[i])
                {
                    case MkLit: continue;
                    case MkVal v:
                        list[i] = new MkVal(v.Expr, new List<Expression> { fx }.Concat(v.Before ?? new List<Expression>()).ToList());
                        return list;
                    case MkAlt a:
                        if (Ahead(a.Yes, fx, at) is not { } yes || Ahead(a.No, fx, at) is not { } no) return null;
                        list[i] = new MkAlt(a.Test, yes, no);
                        return list;
                    default:
                        i = list.Count;
                        break;
                }
            Refuse(at, "a function building markup that does something with no value of its markup after it (not translated yet)");
            return null;
        }

        /// <summary>Whether a declaration is in scope at a node.</summary>
        private bool Visible(Identifier decl, Node at)
        {
            for (Node n = at; _parent.TryGetValue(n, out var p); n = p)
                if (Scope(p).TryGetValue(decl.Name, out var b) && b == decl) return true;
            return false;
        }

        /// <summary>Whether a node runs while the page's script runs at load: code at the top, or in a function called from there.</summary>
        private bool AtSetup(Node n, HashSet<Node>? seen = null)
        {
            for (Node x = n; _parent.TryGetValue(x, out var p); x = p)
            {
                if (p is not IFunction fn) continue;
                // an immediately invoked function runs where it stands
                if (_parent.TryGetValue(p, out var up) && up is CallExpression iife && iife.Callee == p) { x = p; continue; }
                seen ??= new HashSet<Node>();
                if (!seen.Add(p) || NameOf(fn) is not { } name || !_refs.TryGetValue(name, out var refs)) return false;
                return refs.Any(r => _parent[r] is CallExpression c && c.Callee == r && AtSetup(c, seen));
            }
            return true;
        }

        // ---- shapes ---------------------------------------------------------------------------------

        /// <summary>Every combination of the template's choices, in the order the Lua takes them (yes first), each a flat list.</summary>
        private static List<List<Mk>> Shapes(List<Mk> tpl)
        {
            var all = new List<List<Mk>>();
            Walk(new List<Mk>(), tpl, 0);
            return all;

            void Walk(List<Mk> done, List<Mk> rest, int i)
            {
                if (all.Count > MostShapes) return;
                for (; i < rest.Count; i++)
                {
                    if (rest[i] is not MkAlt alt) { done.Add(rest[i]); continue; }
                    var tail = rest.Skip(i + 1).ToList();
                    Walk(new List<Mk>(done), alt.Yes.Concat(tail).ToList(), 0);
                    Walk(new List<Mk>(done), alt.No.Concat(tail).ToList(), 0);
                    return;
                }
                all.Add(done);
            }
        }

        /// <summary>
        /// A shape as markup: its literals, and each value as a marker (with its index) or as a stand-in for layout.
        /// A list is every row in every shape of the row, each in a wrapper (<see cref="RowTag"/>) that says which, taken
        /// out again once parsed (<see cref="Unrow"/>).
        /// </summary>
        private static string Render(List<Mk> shape, Func<int, MkVal, string> value)
        {
            var sb = new StringBuilder();
            var k = 0;
            var r = 0;
            foreach (var m in shape)
                switch (m)
                {
                    case MkLit l: sb.Append(l.Text); break;
                    case MkVal v: sb.Append(value(k++, v)); break;
                    case MkRep rep:
                        for (var row = 0; row < rep.Copies.Count; row++)
                            for (var c = 0; c < rep.Copies[row].Count; c++)
                            {
                                sb.Append('<').Append(RowTag).Append(" r=\"").Append(r.ToString(CultureInfo.InvariantCulture))
                                  .Append("\" k=\"").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\" c=\"").Append(c.ToString(CultureInfo.InvariantCulture)).Append("\">");
                                foreach (var x in rep.Copies[row][c])
                                {
                                    if (x is not MkGate gate) { Put(x); continue; }
                                    // a gated choice's shape: its elements, marked with the row and the copy key they answer to
                                    sb.Append('<').Append(GateTag).Append(" r=\"").Append(r.ToString(CultureInfo.InvariantCulture))
                                      .Append("\" k=\"").Append(row.ToString(CultureInfo.InvariantCulture)).Append("\" c=\"").Append(gate.Key.ToString(CultureInfo.InvariantCulture)).Append("\">");
                                    foreach (var y in gate.Body) Put(y);
                                    sb.Append("</").Append(GateTag).Append('>');
                                }
                                sb.Append("</").Append(RowTag).Append('>');
                            }
                        r++;
                        break;
                }
            return sb.ToString();

            void Put(Mk x)
            {
                if (x is MkLit rl) sb.Append(rl.Text);
                else sb.Append(value(k++, (MkVal)x));
            }
        }

        /// <summary>A shape's values in the order <see cref="Render"/> marks them.</summary>
        private static List<MkVal> Vals(List<Mk> shape)
        {
            var vals = new List<MkVal>();
            foreach (var m in shape)
                if (m is MkVal v) vals.Add(v);
                else if (m is MkRep rep)
                    foreach (var copies in rep.Copies)
                        foreach (var copy in copies)
                            foreach (var x in copy)
                                if (x is MkVal cv) vals.Add(cv);
                                else if (x is MkGate gate) vals.AddRange(gate.Body.OfType<MkVal>());
            return vals;
        }

        /// <summary>
        /// The row wrappers taken out of a parse, their children put where they stood; with <paramref name="rows"/>, each
        /// row's top nodes recorded against its list, row and row shape. False, refused, for a list that is text rather
        /// than elements, or one inside an attribute.
        /// </summary>
        private bool Unrow(HtmlNode n, Dictionary<HtmlNode, (int R, int K, int C)>? rows, List<MkRep> reps, Node at)
        {
            for (var i = 0; i < n.Children.Count; i++)
            {
                var c = n.Children[i];
                if (c.IsText) continue;
                if (c.AttributeCount > 0 && c.Attributes.Values.Any(v => v.Contains("<" + RowTag, StringComparison.Ordinal)))
                {
                    Refuse(at, "a list inside an attribute's value (not translated yet)");
                    return false;
                }
                if (c.AttributeCount > 0 && c.Attributes.Values.Any(v => v.Contains("<" + GateTag, StringComparison.Ordinal)))
                {
                    Refuse(at, $"a list whose rows have more than {MostRowShapes} shapes each, with a choice inside an attribute's value (not translated yet: choices of elements are)");
                    return false;
                }
                if (c.Tag != RowTag && c.Tag != GateTag)
                {
                    if (!Unrow(c, rows, reps, at)) return false;
                    continue;
                }
                var key = (int.Parse(c.Attr("r")!, CultureInfo.InvariantCulture), int.Parse(c.Attr("k")!, CultureInfo.InvariantCulture), int.Parse(c.Attr("c")!, CultureInfo.InvariantCulture));
                var kids = c.Children.ToList();
                n.Children.RemoveAt(i);
                n.Children.InsertRange(i, kids);
                // a gate at the top of its row was taken for one of the row's elements: its own elements are, under its key
                rows?.Remove(c);
                foreach (var g in kids)
                {
                    g.Parent = c.Parent;
                    if (rows == null) continue;
                    if (!g.IsText) { rows[g] = key; continue; }
                    if (g.Text.Trim().Length == 0) continue;
                    if (c.Tag == GateTag)
                    {
                        Refuse(at, $"a list whose rows have more than {MostRowShapes} shapes each, with a choice of text rather than of elements (not translated yet: choices of elements are)");
                        return false;
                    }
                    var sep = reps[key.Item1].Sep;
                    Refuse(at, sep.Trim().Length > 0 && g.Text.Trim() == sep.Trim()
                        ? $"a list joined with \"{sep}\" between its elements: text between them, which a list of elements does not hold (not translated yet)"
                        : "a list of text and text-level markup, drawn as part of one text (not translated yet: a list of elements is)");
                    return false;
                }
                i--;
            }
            return true;
        }

        private static string Marker(int k) => Open + k.ToString(CultureInfo.InvariantCulture) + Close;

        /// <summary>What a value is laid out with: the first of its fixed set (a text one, not empty), else 0.</summary>
        private string StandIn(MkVal v)
        {
            if (Finite(v.Expr) is { Count: > 0 } set)
            {
                var text = set.Select(x => x is double d ? JsToLuaNumber(d) : (string)x).FirstOrDefault(x => x.Length > 0);
                if (text != null) return text;
            }
            return "0";
        }

        /// <summary>The pieces of a text holding markers: literals (rich text) and the values, in order.</summary>
        private List<Piece> Split(string rich, List<MkVal> vals)
        {
            var pieces = new List<Piece>();
            var at = 0;
            while (at < rich.Length)
            {
                var open = rich.IndexOf(Open, at);
                if (open < 0) { pieces.Add(new Piece { Literal = rich.Substring(at), Raw = true }); break; }
                if (open > at) pieces.Add(new Piece { Literal = rich.Substring(at, open - at), Raw = true });
                var close = rich.IndexOf(Close, open);
                var k = int.Parse(rich.Substring(open + 1, close - open - 1), CultureInfo.InvariantCulture);
                pieces.Add(HolePiece(vals[k].Expr));
                at = close + 1;
            }
            return pieces;
        }

        /// <summary>A value as a text piece: `x.toFixed(n)` printed with `%.nf`, anything else as JavaScript prints it.</summary>
        private Piece HolePiece(Expression v)
        {
            var inner = Unwrap(v);
            if (inner is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "toFixed" } } f, Arguments.Count: 1 } c
                && c.Arguments[0] is NumericLiteral { Value: >= 0 and <= 20 } d && d.Value == Math.Floor(d.Value))
                return new Piece
                {
                    Hole = Within(f.Object, v is ParenthesizedExpression w && _bindOf.TryGetValue(w, out var b) ? b : null, f.Object),
                    Digits = (int)d.Value,
                    Format = "%." + ((int)d.Value).ToString(CultureInfo.InvariantCulture) + "f",
                };
            return new Piece { Hole = v };
        }

        // ---- laying the markup into the page --------------------------------------------------------

        /// <summary>
        /// One element's markup, into the page for the length of the compile: its rich text when every
        /// shape is text, else every shape's elements, one shown at a time; and each value as the write it is.
        /// </summary>
        private void Lay(MarkupOf of)
        {
            var t = of.T;
            var rules = _built.Rules;
            // every shape of every write, parsed with its values as markers
            var parsed = new List<(MarkupWrite W, List<Mk> Shape, List<MkVal> Vals, HtmlNode Marked)>();
            foreach (var w in of.Writes)
            {
                // a value in an attribute of text markup is a choice between its fixed values
                var tpl = w.Template;
                for (var round = 0; round < 2; round++)
                {
                    var shapes = Shapes(tpl);
                    if (shapes.Count > MostShapes) { Refuse(w.At, $"markup with more than {MostShapes} shapes"); return; }
                    var expand = new HashSet<MkVal>();
                    parsed.RemoveAll(x => x.W == w);
                    foreach (var shape in shapes)
                    {
                        var vals = Vals(shape);
                        var html = Render(shape, (k, _) => Marker(k));
                        if (System.Text.RegularExpressions.Regex.IsMatch(html, "</?\\s*" + Open)) { Refuse(w.At, "markup whose tag is a value (not translated)"); return; }
                        var marked = HtmlParser.Parse(html, _ => { });
                        foreach (var top in marked.Children) top.Parent = t.Node;
                        if (Holes(marked, vals, w.At) is not { } holes) return;
                        // drawn as part of an element's rich text: its attribute values are the text's shapes
                        foreach (var (v, attr, on) in holes)
                            if (attr != null && Folds(on, rules)) expand.Add(v);
                        parsed.Add((w, shape, vals, marked));
                    }
                    if (expand.Count == 0 || round == 1) break;
                    var map = new Dictionary<MkVal, Mk>();
                    foreach (var v in expand)
                    {
                        if (v.Before != null || Finite(v.Expr) is not { Count: > 0 } set) { Refuse(v.Expr, "a value in an attribute of text markup, only known at run time (not translated yet)"); return; }
                        Mk choice = new MkLit(Text(set[^1]));
                        for (var i = set.Count - 2; i >= 0; i--)
                            choice = new MkAlt(Made(new NonLogicalBinaryExpression(Operator.StrictEquality, v.Expr, Literal(set[i])), w.At),
                                               new List<Mk> { new MkLit(Text(set[i])) }, new List<Mk> { choice });
                        map[v] = choice;
                    }
                    tpl = Replace(tpl, map);
                    w.Template = tpl;
                }
            }
            of.Inline = parsed.All(p => TextOnly(p.Marked, rules));
            if (of.Inline) Text(of, parsed);
            else Union(of, parsed);
        }

        private static string Text(object v) => v is double d ? JsToLuaNumber(d) : (string)v;
        private static Expression Literal(object v) => v is double d ? new NumericLiteral(d, JsToLuaNumber(d)) : new StringLiteral((string)v, (string)v);

        private List<Mk> Replace(List<Mk> tpl, Dictionary<MkVal, Mk> map)
            => tpl.Select(m => m switch
            {
                MkVal v when map.TryGetValue(v, out var c) => c,
                MkAlt a => new MkAlt(a.Test, Replace(a.Yes, map), Replace(a.No, map)),
                MkRep rep => Replaced(rep, map),
                _ => m,
            }).ToList();

        /// <summary>A list's rows with values replaced, and each row's shapes worked out again.</summary>
        private MkRep Replaced(MkRep rep, Dictionary<MkVal, Mk> map)
        {
            for (var k = 0; k < rep.Rows.Count; k++)
            {
                rep.Rows[k] = Replace(rep.Rows[k], map);
                ShapeRow(rep, k, rep.At);
            }
            return rep;
        }

        /// <summary>Where each value lands: in a text (null) or an attribute (its name), on which node. Null, refused, when one lands in a name.</summary>
        private List<(MkVal Val, string? Attr, HtmlNode On)>? Holes(HtmlNode root, List<MkVal> vals, Node at)
        {
            var found = new List<(MkVal, string?, HtmlNode)>();
            foreach (var n in Below(root))
            {
                if (n.IsText) { foreach (var k in Markers(n.Text)) found.Add((vals[k], null, n)); continue; }
                if ((n.Tag ?? string.Empty).IndexOf(Open) >= 0) { Refuse(at, "markup whose tag is a value (not translated)"); return null; }
                if (n.Tag is "script" or "style") { Refuse(at, $"a <{n.Tag}> written with innerHTML, which a browser does not run either (not translated)"); return null; }
                if (n.AttributeCount == 0) continue;
                foreach (var pair in n.Attributes)
                {
                    if (pair.Key.IndexOf(Open) >= 0) { Refuse(at, "markup whose attribute name is a value (not translated)"); return null; }
                    foreach (var k in Markers(pair.Value)) found.Add((vals[k], pair.Key.ToLowerInvariant(), n));
                }
            }
            return found;
        }

        private static IEnumerable<int> Markers(string s)
        {
            for (var at = s.IndexOf(Open); at >= 0; at = s.IndexOf(Open, at + 1))
                yield return int.Parse(s.Substring(at + 1, s.IndexOf(Close, at) - at - 1), CultureInfo.InvariantCulture);
        }

        private static IEnumerable<HtmlNode> Below(HtmlNode n)
        {
            foreach (var c in n.Children)
            {
                yield return c;
                foreach (var d in Below(c)) yield return d;
            }
        }

        /// <summary>Markup of only text and text-level tags that fold into it: what the page draws as its element's text.</summary>
        private static bool TextOnly(HtmlNode root, List<CssRule> rules) => Below(root).All(n => n.IsText || Folds(n, rules));

        /// <summary>
        /// A text-level tag the renderer folds into its element's rich text: no id (a data target) and no class
        /// (a styled box), which keep an element of their own (HtmlRenderer.KeepsOwnElement), and not made a box by
        /// the cascade - its own display, or a flex or grid container around it (HtmlRenderer.Blockified).
        /// </summary>
        private static bool Folds(HtmlNode n, List<CssRule> rules)
            => TextTags.Contains(n.Tag!) && n.Attr("id") == null && n.Attr("class") == null && !HtmlRenderer.Blockified(n, rules);

        /// <summary>Text markup: each shape one rich text of the element, written as textContent is.</summary>
        private void Text(MarkupOf of, List<(MarkupWrite W, List<Mk> Shape, List<MkVal> Vals, HtmlNode Marked)> parsed)
        {
            var t = of.T;
            if (t.Ve is not Label)
            {
                // an element holding other elements becomes the text: rebuilt from the first shape
                var first = parsed[0];
                foreach (var child in t.Ve.Children().ToList()) Drop(child);
                Rebuild(of, HtmlParser.Parse(Render(first.Shape, (_, v) => StandIn(v)), _ => { }).Children.ToList());
                if (_refused.Count > 0) return;
                if (t.Ve is not Label) { Refuse(first.W.At, $"markup written into \"{t.Name}\" that the page does not draw as its text"); return; }
            }
            foreach (var (w, shape, vals, marked) in parsed)
            {
                var children = new List<HtmlNode>(t.Node.Children);
                string rich;
                try
                {
                    t.Node.Children.Clear();
                    foreach (var c in marked.Children) { c.Parent = t.Node; t.Node.Children.Add(c); }
                    rich = HtmlRenderer.RichText(t.Node, _built.Rules);
                }
                finally
                {
                    t.Node.Children.Clear();
                    t.Node.Children.AddRange(children);
                }
                var value = Made(new StringLiteral(string.Empty, string.Empty), w.At);
                _givenPieces[value] = Split(rich, vals);
                var at = Made(new AssignmentExpression(Operator.Assignment, new MemberExpression(new Identifier("__markup"), new Identifier("textContent"), false, false), value), w.At);
                var done = new HashSet<MkVal>();
                if (!Before(at, rich, vals, done, w.At) || !AllDone(vals, done, w.At)) return;
                t.Texts.Add((at, value));
                _ops[at] = new List<Target> { t };
                var shapeOut = new ShapeOut();
                shapeOut.Ops.Add(at);
                w.Shapes.Add(shapeOut);
            }
        }

        /// <summary>
        /// Element markup: every shape's elements under the element (and what the page wrote there, while no
        /// write has replaced it yet), one shape shown at a time; each value the write it stands for. A list in a
        /// shape is every row it can have, each row in every shape of its own, and a state per length of the list:
        /// its first rows shown, the rest hidden and what follows moved up.
        /// </summary>
        private void Union(MarkupOf of, List<(MarkupWrite W, List<Mk> Shape, List<MkVal> Vals, HtmlNode Marked)> parsed)
        {
            var t = of.T;
            // What the page wrote shows until the first write - unless a write runs as the page loads,
            // before a browser has drawn anything.
            of.KeepOwn = !of.Writes.Any(w => AtSetup(w.At));
            if (of.KeepOwn && (t.Ve is Label || t.Node.Children.Any(c => c.IsText && c.Text.Trim().Length > 0)))
            {
                Refuse(of.Writes[0].At, $"the text \"{t.Name}\" holds as the page wrote it, shown until markup replaces it later, next to elements the markup makes (not translated yet)");
                return;
            }
            // each shape's markup with stand-ins, parsed; its nodes paired with the marked parse; each list's rows found
            var built = new List<(MarkupWrite W, List<MkVal> Vals, HtmlNode Marked, HtmlNode Stand, Dictionary<HtmlNode, HtmlNode> Pair, List<MkRep> Reps, Dictionary<HtmlNode, (int R, int K, int C)> Rows)>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (w, shape, vals, marked) in parsed)
            {
                var stand = HtmlParser.Parse(Render(shape, (_, v) => StandIn(v)), _ => { });
                var pair = new Dictionary<HtmlNode, HtmlNode>();
                if (!Pair(stand, marked, pair)) { Refuse(w.At, "markup whose values change what it parses into (not translated yet)"); return; }
                var reps = shape.OfType<MkRep>().ToList();
                var rows = new Dictionary<HtmlNode, (int R, int K, int C)>();
                if (!Unrow(stand, rows, reps, w.At) || !Unrow(marked, null, reps, w.At)) return;
                foreach (var top in marked.Children)
                    if (top.IsText && top.Text.Trim().Length > 0) { Refuse(w.At, $"text directly inside \"{t.Name}\" next to the elements the markup makes (not translated yet)"); return; }
                foreach (var n in Below(stand))
                    if (n.Attr("id") is { } id && (!ids.Add(id) || _built.ById.TryGetValue(id, out var holder) && (of.KeepOwn || holder == null || !Inside(holder, t.Ve))))
                    {
                        Refuse(w.At, $"the id \"{id}\" in markup, which the page or another shape (or row) of it has too (not translated yet)");
                        return;
                    }
                built.Add((w, vals, marked, stand, pair, reps, rows));
            }

            // Into the page: the element built again around its own children (while they show) and every
            // shape's, so what its parent does for its children - list markers, gaps, rows - is done for these.
            // (copies: the compile names what it drives for good, and the page's own nodes are put back as they were)
            var own = HtmlParser.Parse(HtmlRenderer.ToHtml(t.Node, outer: false, keepIds: true), _ => { }).Children.ToList();
            if (!of.KeepOwn) foreach (var child in t.Ve.Children().ToList()) Drop(child);
            Rebuild(of, (of.KeepOwn ? own : new List<HtmlNode>()).Concat(built.SelectMany(b => b.Stand.Children)).ToList());
            if (_refused.Count > 0) return;

            if (of.KeepOwn) of.Own.AddRange(own.Where(n => !n.IsText && Ve(n) != null).Select(n => TargetOf(Ve(n)!, n)));
            foreach (var b in built)
            {
                var o = new ShapeOut();
                o.Reps.AddRange(b.Reps);
                b.W.Shapes.Add(o);
                of.Shapes.Add(o);
                // which row, of which list, in which of the row's shapes, each node is in
                var copyOf = new Dictionary<HtmlNode, (MkRep, int, int)>();
                foreach (var pairRow in b.Rows)
                {
                    var key = (b.Reps[pairRow.Value.R], pairRow.Value.K, pairRow.Value.C);
                    copyOf[pairRow.Key] = key;
                    foreach (var d in Below(pairRow.Key)) copyOf[d] = key;
                    if (Ve(pairRow.Key) is not { } rowVe)
                    {
                        Refuse(b.W.At, key.Item1.Gates.ContainsKey(key.Item2) && key.Item3 > 0
                            ? $"a list whose rows have more than {MostRowShapes} shapes each, with a choice drawn as part of a text (not translated yet: choices of elements of their own are)"
                            : "a list of text and text-level markup, drawn as part of one text (not translated yet: a list of elements is)");
                        return;
                    }
                    if (!o.RowTops.TryGetValue(key, out var tops)) o.RowTops[key] = tops = new List<Target>();
                    tops.Add(TargetOf(rowVe, pairRow.Key));
                }
                foreach (var node in b.Stand.Children)
                    if (!node.IsText && !copyOf.ContainsKey(node) && Ve(node) is { } ve) o.Tops.Add(TargetOf(ve, node));
                foreach (var node in Below(b.Stand))
                    if (!node.IsText && Ve(node) is { } made)
                    {
                        var target = TargetOf(made, node);
                        if (!of.Made.Contains(target)) of.Made.Add(target);
                        _madeBy[target] = of;
                        if (copyOf.TryGetValue(node, out var key))
                        {
                            _rowOf[target] = key;
                            if (!o.RowMade.TryGetValue(key, out var list)) o.RowMade[key] = list = new List<Target>();
                            list.Add(target);
                        }
                        else o.Made.Add(target);
                    }
                List<Expression> OpsFor(HtmlNode node)
                {
                    if (!copyOf.TryGetValue(node, out var key)) return o.Ops;
                    if (!o.RowOps.TryGetValue(key, out var list)) o.RowOps[key] = list = new List<Expression>();
                    return list;
                }
                if (!Writes(of, b.Vals, b.Marked, b.Pair, OpsFor, b.W.At)) return;
            }

            // the states: the element's own children (when kept), then per shape one per length of its lists (longest first)
            if (of.KeepOwn) of.States.Add(new HashSet<Target>(of.Own));
            foreach (var o in of.Shapes)
            {
                o.Base = of.States.Count;
                var radix = o.Reps.Select(r => r.Max - r.Min + 1).ToList();
                var total = radix.Aggregate(1L, (a, x) => a * x);
                if (of.States.Count + total > MostStates)
                {
                    Refuse(of.Writes[0].At, $"markup into \"{t.Name}\" with {of.States.Count + total} shapes and lengths of its lists, more than the {MostStates} laid out");
                    return;
                }
                for (var key = 0; key < total; key++)
                {
                    var shown = new HashSet<Target>(o.Tops);
                    var rest = key;
                    for (var r = 0; r < o.Reps.Count; r++)
                    {
                        var length = o.Reps[r].Max - rest % radix[r];
                        rest /= radix[r];
                        for (var k = 0; k < length; k++)
                        {
                            shown.UnionWith(o.TopsOf(o.Reps[r], k, 0));
                            // a gated row's choices, each in its shape at rest
                            if (o.Reps[r].Gates.TryGetValue(k, out var gates)) foreach (var (_, keys) in gates) shown.UnionWith(o.TopsOf(o.Reps[r], k, keys[0]));
                        }
                    }
                    of.States.Add(shown);
                }
            }
            foreach (var x in of.Own.Concat(of.Shapes.SelectMany(o => o.Tops.Concat(o.RowTops.Values.SelectMany(v => v)))))
                if (!of.Hide.Contains(x)) of.Hide.Add(x);
            if (Varies(of))
            {
                foreach (var top in of.Hide) top.Top = true;
                // at rest: the element's own children, or the first shape with its lists at their longest
                Show(of, of.States[0]);
            }
        }

        /// <summary>Whether an element's markup shows and hides what it makes: more than one state, or a row whose shape changes with its item.</summary>
        private static bool Varies(MarkupOf of)
            => !of.Inline && (of.States.Count > 1 || of.Shapes.Any(o => o.Reps.Any(r => r.Copies.Any(c => c.Count > 1) || r.Gates.Count > 0)));

        /// <summary>One state's display: what it shows of the elements states show and hide, and none of the rest.</summary>
        private static void Show(MarkupOf of, HashSet<Target> shown)
        {
            foreach (var h in of.Hide) h.Ve.style.display = shown.Contains(h) ? StyleKeyword.Null : DisplayStyle.None;
        }

        /// <summary>An element and everything in it out of the page's ids for the length of the compile: a lookup no longer finds them.</summary>
        private void Drop(VisualElement root)
        {
            foreach (var ve in Subtree(root))
                if (!string.IsNullOrEmpty(ve.name) && _built.ById.TryGetValue(ve.name, out var at) && at == ve)
                {
                    var name = ve.name;
                    _built.ById.Remove(name);
                    _undo.Add(() => _built.ById[name] = ve);
                }
        }

        /// <summary>The live element a parsed node was built into, by the id the page gives every element.</summary>
        private VisualElement? Ve(HtmlNode node)
            => node.Attr("id") is { } id && _built.ById.TryGetValue(id, out var ve) && ve != null && _built.NodeOf.TryGetValue(ve, out var n) && n == node ? ve : null;

        /// <summary>Two parses of one markup, node by node: false when they differ in shape.</summary>
        private static bool Pair(HtmlNode a, HtmlNode b, Dictionary<HtmlNode, HtmlNode> pair)
        {
            if (a.IsText != b.IsText || !string.Equals(a.Tag, b.Tag, StringComparison.OrdinalIgnoreCase) || a.Children.Count != b.Children.Count) return false;
            pair[a] = b;
            for (var i = 0; i < a.Children.Count; i++)
                if (!Pair(a.Children[i], b.Children[i], pair)) return false;
            return true;
        }

        /// <summary>Each value of one shape, as the write to the element it lands in; false, refused, for one the features do not translate.</summary>
        private bool Writes(MarkupOf of, List<MkVal> vals, HtmlNode marked, Dictionary<HtmlNode, HtmlNode> pair, Func<HtmlNode, List<Expression>> opsFor, Node write)
        {
            var back = pair.ToDictionary(p => p.Value, p => p.Key);
            var labels = new HashSet<Target>();
            var done = new HashSet<MkVal>();
            foreach (var mnode in Below(marked))
            {
                if (!back.TryGetValue(mnode, out var node)) continue;
                if (mnode.IsText)
                {
                    if (!mnode.Text.Contains(Open)) continue;
                    // the text of the element drawing it: the nearest one with a box of its own, which has to be a text
                    var owner = mnode.Parent;
                    VisualElement? drawn = null;
                    while (owner != null && back.TryGetValue(owner, out var standOwner))
                    {
                        if (Ve(standOwner) is { } found) { drawn = found; break; }
                        owner = owner.Parent;
                    }
                    if (owner == null || drawn is not Label label)
                    {
                        Refuse(write, "a value in text next to elements, in markup (not translated yet)");
                        return false;
                    }
                    var target = TargetOf(label, back[owner]);
                    var ops = opsFor(back[owner]);
                    if (!labels.Add(target)) continue;
                    var value = Made(new StringLiteral(string.Empty, string.Empty), write);
                    var rich = HtmlRenderer.RichText(owner, _built.Rules);
                    _givenPieces[value] = Split(rich, vals);
                    var at = Made(new AssignmentExpression(Operator.Assignment, new MemberExpression(new Identifier("__markup"), new Identifier("textContent"), false, false), value), write);
                    if (!Before(at, rich, vals, done, write)) return false;
                    target.Texts.Add((at, value));
                    _ops[at] = new List<Target> { target };
                    ops.Add(at);
                    continue;
                }
                if (mnode.AttributeCount == 0 || Ve(node) is not { } ve) continue;
                var el = TargetOf(ve, node);
                var elOps = opsFor(node);
                var r = new Recv(Made(new Identifier("__markup"), write), new List<Target> { el }, false);
                foreach (var attr in mnode.Attributes)
                {
                    if (attr.Value.IndexOf(Open) < 0) continue;
                    var name = attr.Key.ToLowerInvariant();
                    if (name == "id" || name.StartsWith("on", StringComparison.Ordinal))
                    {
                        Refuse(write, $"a value in the {name} attribute of markup (not translated)");
                        return false;
                    }
                    if (name == "style")
                    {
                        foreach (var decl in attr.Value.Split(';'))
                        {
                            if (decl.IndexOf(Open) < 0) continue;
                            var colon = decl.IndexOf(':');
                            if (colon < 0 || decl.Substring(0, colon).IndexOf(Open) >= 0) { Refuse(write, "a style property named by a value, in markup (not translated)"); return false; }
                            var css = decl.Substring(0, colon).Trim().ToLowerInvariant();
                            var value = Joined(decl.Substring(colon + 1).Trim(), vals, write);
                            var target = Made(new MemberExpression(Made(new MemberExpression(new Identifier("__markup"), new Identifier("style"), false, false), write), new StringLiteral(css, css), true, false), write);
                            var at = Made(new AssignmentExpression(Operator.Assignment, target, value), write);
                            if (!Before(at, decl, vals, done, write)) return false;
                            if (!el.Styles.TryGetValue(css, out var list)) el.Styles[css] = list = new();
                            list.Add((at, value));
                            _ops[at] = new List<Target> { el };
                            elOps.Add(at);
                        }
                        continue;
                    }
                    var joined = Joined(attr.Value, vals, write);
                    if (name == "class")
                    {
                        var at = Made(new AssignmentExpression(Operator.Assignment, new MemberExpression(new Identifier("__markup"), new Identifier("className"), false, false), joined), write);
                        if (!Before(at, attr.Value, vals, done, write)) return false;
                        ClassName(r, at, joined);
                        elOps.Add(at);
                        continue;
                    }
                    var set = Made(new AssignmentExpression(Operator.Assignment, new MemberExpression(new Identifier("__markup"), new Identifier(name), false, false), joined), write);
                    if (!Before(set, attr.Value, vals, done, write)) return false;
                    AttrWrite(r, set, name, "set", joined);
                    elOps.Add(set);
                }
            }
            return AllDone(vals, done, write);
        }

        /// <summary>What the helpers of a write's values did first (`acts.push(fn)`), done right before the write.</summary>
        private readonly Dictionary<Expression, List<Expression>> _before = new();

        /// <summary>
        /// What the helper of a value in a write's text did first, kept to be done right before the write; false,
        /// refused, for two such values in one write, since the Lua would do both before reading either.
        /// </summary>
        private bool Before(Expression op, string text, List<MkVal> vals, HashSet<MkVal> done, Node write)
        {
            foreach (var k in Markers(text))
            {
                if (vals[k].Before is not { } fx) continue;
                if (_before.ContainsKey(op)) { Refuse(write, "two values in one text or attribute of markup whose helpers do something first (not translated yet)"); return false; }
                _before[op] = fx;
                done.Add(vals[k]);
            }
            return true;
        }

        /// <summary>Whether every value whose helper does something first landed in a write that does it; refused otherwise.</summary>
        private bool AllDone(List<MkVal> vals, HashSet<MkVal> done, Node write)
        {
            if (!vals.Any(v => v.Before != null && !done.Contains(v))) return true;
            Refuse(write, "a value of markup whose helper does something first, landing where nothing is written (not translated yet)");
            return false;
        }

        /// <summary>An attribute's text with values in it as the expression JavaScript would add up: literals and values, left to right.</summary>
        private Expression Joined(string text, List<MkVal> vals, Node write)
        {
            var parts = new List<Expression>();
            foreach (var p in Split(text, vals))
                parts.Add(p.Literal != null ? Made(new StringLiteral(p.Literal, p.Literal), write) : p.Hole!);
            if (parts.Count == 1) return parts[0];
            var e = parts[0];
            for (var i = 1; i < parts.Count; i++) e = Made(new NonLogicalBinaryExpression(Operator.Addition, e, parts[i]), write);
            _joined[e] = Split(text, vals);
            return e;
        }

        /// <summary>An attribute's text with values in it, as its pieces: translated as a lookup of the whole text, never built.</summary>
        private readonly Dictionary<Expression, List<Piece>> _joined = new();
        private readonly Dictionary<Expression, string> _joinedTable = new();

        /// <summary>The element built again from its tag and attributes around these nodes, by the renderer as it builds a page.</summary>
        private void Rebuild(MarkupOf of, List<HtmlNode> children)
        {
            var t = of.T;
            var parentVe = t.Ve.parent;
            var parentNode = t.Node.Parent;
            if (parentVe == null || parentNode == null) { Refuse(of.Writes[0].At, $"markup written into \"{t.Name}\", which has no parent to be built again in"); return; }
            // an event handler attribute in markup would be dropped: the page's own are made into listeners before the script runs
            foreach (var n in children.Where(c => !c.IsText).SelectMany(c => new[] { c }.Concat(Elements(c))))
                if (n.Attributes.Keys.FirstOrDefault(k => k.Length > 2 && k.StartsWith("on", StringComparison.OrdinalIgnoreCase)) is { } on)
                {
                    Refuse(of.Writes[0].At, $"an inline event handler attribute ({on}=) in markup written into \"{t.Name}\" (not translated yet; a listener the script adds to what markup makes is)");
                    return;
                }
            var fresh = new HtmlNode { Tag = t.Node.Tag };
            if (t.Node.AttributeCount > 0) foreach (var a in t.Node.Attributes) fresh.Attributes[a.Key] = a.Value;
            var parents = children.Select(c => (Node: c, Was: c.Parent)).ToList();
            foreach (var c in children) { c.Parent = fresh; fresh.Children.Add(c); }
            var (oldVe, oldNode) = (t.Ve, t.Node);
            var index = parentVe.IndexOf(oldVe);
            var nodeIndex = parentNode.Children.IndexOf(oldNode);
            var before = new HashSet<string>(_built.ById.Keys, StringComparer.Ordinal);
            var saved = Subtree(oldVe).Where(v => !string.IsNullOrEmpty(v.name) && _built.ById.TryGetValue(v.name, out var at) && at == v).Select(v => (v.name, v)).ToList();
            parentVe.Remove(oldVe);
            parentNode.Children.RemoveAt(nodeIndex);
            HtmlRenderer.AppendNodes(parentVe, parentNode, new[] { fresh }, _built);
            // the ids the markup made; an id given later (the compile naming an element it drives) is not one of them
            var added = new HashSet<string>(_built.ById.Keys.Where(k => !before.Contains(k)), StringComparer.Ordinal);
            Attach();
            var made = parentVe.Children()[^1];
            parentVe.Insert(index, made);
            parentNode.Children.Remove(fresh);
            parentNode.Children.Insert(nodeIndex, fresh);
            _targets.Remove(oldVe);
            t.Ve = made;
            t.Node = fresh;
            _targets[made] = t;
            _undo.Add(() =>
            {
                foreach (var id in _built.ById.Keys.ToList())
                    if (added.Contains(id) || _built.ById[id] is { } now && Inside(now, made))
                    {
                        if (_built.ById[id] is { } gone) _built.ForgetElement(gone);
                        _built.ById.Remove(id);
                    }
                _built.ForgetElement(made);
                foreach (var (name, ve) in saved) _built.ById[name] = ve;
                foreach (var (node, was) in parents) node.Parent = was;
                parentVe.Remove(made);
                parentVe.Insert(index, oldVe);
                parentNode.Children.Remove(fresh);
                parentNode.Children.Insert(nodeIndex, oldNode);
                _built.ById[oldVe.name] = oldVe;
                _built.NodeOf[oldVe] = oldNode;
                _targets.Remove(made);
                t.Ve = oldVe;
                t.Node = oldNode;
                _targets[oldVe] = t;
            });
        }

        /// <summary>Grids and post-layout passes for what was just built, as the surface attaches them.</summary>
        private void Attach()
        {
            foreach (var grid in _built.Grids)
                if (_built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, _built);
            PostLayout.Attach(_built);
        }

        /// <summary>Put back what laying the markup in changed, last first.</summary>
        private readonly List<Action> _undo = new();

        public void Unlay()
        {
            lock (PageCompiler.Gate)
            {
                for (var i = _undo.Count - 1; i >= 0; i--) _undo[i]();
                _undo.Clear();
            }
        }

        // ---- after the analysis -----------------------------------------------------------------------

        /// <summary>
        /// What the rest of the script does to what markup makes: listeners and reads are translated; another
        /// write would be undone by the next markup write, which the compile does not do yet, so it is refused.
        /// </summary>
        private void MarkupChecks()
        {
            foreach (var (made, of) in _madeBy)
            {
                var own = made.Texts.Select(x => x.At).Concat(made.Styles.Values.SelectMany(l => l.Select(x => x.At)))
                    .Concat(made.ClassOps.Select(x => x.At)).Concat(made.ClassNames.Select(x => x.At)).Concat(made.AttrOps.Select(x => x.At))
                    .FirstOrDefault(x => !_synthetic.Contains(x));
                if (own != null)
                    Refuse(own, $"a write to \"{made.Name}\", which markup written into \"{of.T.Name}\" makes (the next markup write would undo it; not translated yet)");
            }
        }

        /// <summary>
        /// Whether a list (or a first match) includes elements markup makes in only some of its states: a
        /// browser's list holds what is shown, which one fixed at compile time cannot follow - but for a list of
        /// one list's rows (<see cref="Els.Rows"/>), whose length the Lua keeps as the rows shown. An element by
        /// its id is fine - a script looks up the elements of the shape it has just written.
        /// </summary>
        private string? Transient(Expression at, Els els)
        {
            if (!els.List && !(at is CallExpression c && DomCall(c) is { Kind: "querySelector" }) || els.List && els.Shown) return null;
            foreach (var t in els.Ts)
            {
                if (!_madeBy.TryGetValue(t, out var of) || !Varies(of)) continue;
                if (els.List && els.Rows is { } rows && _rowOf.TryGetValue(t, out var row) && row.Rep == rows.Rep) continue;
                return $"\"{t.Name}\", which markup written into \"{of.T.Name}\" makes in only some of its shapes or lengths (not translated yet: a list of one list's rows, one element per row, is)";
            }
            return null;
        }

        /// <summary>
        /// The visibility slots that show an element markup makes, in a lookup's list of what is shown (<see cref="Els.Shown"/>):
        /// its own and those of what holds it, each a top a state shows or hides. None for an element always there.
        /// </summary>
        private List<string> ShownBy(Target t)
        {
            var slots = new List<string>();
            if (!_madeBy.TryGetValue(t, out var of) || !Varies(of)) return slots;
            for (var ve = t.Ve; ve != null && ve != of.T.Ve; ve = ve.parent)
                if (of.Hide.FirstOrDefault(h => h.Ve == ve) is { } top) slots.Add(DomSlots.Slot(top.Name) + "_v");
            return slots;
        }

        /// <summary>
        /// A lookup's list that is one list's rows, one element per row in order, after elements that are always
        /// there: that list and how many come before its rows. Null for anything else.
        /// </summary>
        private (MkRep Rep, int Prefix)? RowsOf(List<Target> found)
        {
            var p = 0;
            while (p < found.Count && !(_madeBy.TryGetValue(found[p], out var of) && Varies(of))) p++;
            if (p == found.Count || !_rowOf.TryGetValue(found[p], out var first)) return null;
            var rep = first.Rep;
            if (found.Count - p != rep.Max) return null;
            for (var k = 0; k < rep.Max; k++)
                if (!_rowOf.TryGetValue(found[p + k], out var key) || key.Rep != rep || key.K != k || key.C != 0 || rep.Copies[k].Count != 1) return null;
            rep.Queried = true;
            return (rep, p);
        }

        /// <summary>
        /// The states of each element's union of shapes, laid out: the element's own children, and per shape one per
        /// length of its lists. Elements whose states move the same things - what follows two lists sits where both
        /// put it - are laid out together, every combination of their states, and each write picks the combination
        /// from its own state and the others' last. Then, per row whose shape changes with its item, what each of its
        /// shapes draws.
        /// </summary>
        private void MarkupStates()
        {
            var laid = new List<(MarkupOf Of, List<Dictionary<string, SceneSlots.Value>> Drawn, HashSet<string> Moved, Action Back)>();
            var backs = new Dictionary<MarkupOf, Action>();
            foreach (var of in _markupOf.Values)
            {
                if (!Varies(of)) continue;
                var display = of.Hide.ToDictionary(x => x, x => x.Ve.style.display);
                void Back() { foreach (var pair in display) pair.Key.Ve.style.display = pair.Value; }
                backs[of] = Back;
                of.Facet = $"the markup written into \"{of.T.Name}\"";
                if (of.States.Count < 2) continue;
                var drawn = new List<Dictionary<string, SceneSlots.Value>>();
                var moved = new HashSet<string>(StringComparer.Ordinal);
                var at = of.Writes[0].At;
                var lists = of.Shapes.Any(o => o.Reps.Count > 0);
                for (var s = 0; s < of.States.Count; s++)
                {
                    var shown = of.States[s];
                    if (lists && Positional(of, shown) is { } why) { Refuse(at, why); return; }
                    var one = Variant(() => Show(of, shown), Back);
                    if (one == null) { Refuse(at, $"{Described(of, s)} of the markup written into \"{of.T.Name}\" changes the scene's structure ({_reshaped})"); return; }
                    drawn.Add(one);
                    moved.UnionWith(Moved(one));
                }
                // every state says what it shows, so a row a write showed in another of its shapes is hidden by the next state
                if (lists) foreach (var h in of.Hide) moved.Add(DomSlots.Slot(h.Name) + "_v");
                laid.Add((of, drawn, moved, Back));
            }

            // elements laid out together: those whose states move a slot in common, and those linked to them
            var root = Enumerable.Range(0, laid.Count).ToArray();
            int Find(int x) => root[x] == x ? x : root[x] = Find(root[x]);
            for (var i = 0; i < laid.Count; i++)
                for (var j = i + 1; j < laid.Count; j++)
                    if (laid[i].Moved.Overlaps(laid[j].Moved)) root[Find(j)] = Find(i);
            var n = 0;
            var keys = 0;
            foreach (var members in Enumerable.Range(0, laid.Count).GroupBy(Find).Select(g => g.Select(i => laid[i]).ToList()))
            {
                var plan = new StylePlan { Var = "V_SH" + (++n).ToString(CultureInfo.InvariantCulture), States = new Dictionary<object, Dictionary<string, SceneSlots.Value>>() };
                var at = members[0].Of.Writes[0].At;
                var facet = "the markup written into " + string.Join(" and ", members.Select(m => "\"" + m.Of.T.Name + "\""));
                var moved = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in members) moved.UnionWith(m.Moved);
                if (members.Count == 1)
                    for (var s = 0; s < members[0].Drawn.Count; s++) plan.States[(double)s] = members[0].Drawn[s];
                else
                {
                    // ponytail: every combination laid out, so stacked lists multiply (the ceiling is MostStates); where
                    // their moves add up (blocks in one column), a shift per element summed by the Lua would scale
                    var total = members.Aggregate(1L, (a, m) => a * m.Of.States.Count);
                    if (total > MostStates)
                    {
                        Refuse(at, $"{facet}, whose shapes and lists move the same things, in {total} combinations, more than the {MostStates} laid out (not translated yet)");
                        return;
                    }
                    for (var key = 0; key < total; key++)
                    {
                        var digits = new int[members.Count];
                        var rest = key;
                        for (var i = 0; i < members.Count; i++) { digits[i] = rest % members[i].Of.States.Count; rest /= members[i].Of.States.Count; }
                        var drawn = Variant(() => { for (var i = 0; i < members.Count; i++) Show(members[i].Of, members[i].Of.States[digits[i]]); },
                                            () => { foreach (var m in members) m.Back(); });
                        if (drawn == null) { Refuse(at, $"{facet} changes the scene's structure in one of its combinations ({_reshaped})"); return; }
                        plan.States[(double)key] = drawn;
                        moved.UnionWith(Moved(drawn));
                    }
                    // each write picks the combination from its element's state and the others' last
                    var radix = 1;
                    var joint = new List<string>();
                    foreach (var m in members)
                    {
                        m.Of.KeyVar = "V_M" + (++keys).ToString(CultureInfo.InvariantCulture);
                        joint.Add(radix == 1 ? m.Of.KeyVar : radix.ToString(CultureInfo.InvariantCulture) + " * " + m.Of.KeyVar);
                        radix *= m.Of.States.Count;
                    }
                    foreach (var m in members) m.Of.JointKey = string.Join(" + ", joint);
                }
                Complete(plan.States.Values, moved);
                foreach (var slot in moved) if (!Claim(slot, facet, at)) return;
                foreach (var m in members) { m.Of.Plan = plan; m.Of.Facet = facet; }
            }

            foreach (var (of, back) in backs)
                foreach (var o in of.Shapes)
                    foreach (var rep in o.Reps)
                        for (var k = 0; k < rep.Max; k++)
                            if (rep.Gates.ContainsKey(k) ? !GateFacets(of, o, rep, k, of.Facet!, back) : rep.Copies[k].Count > 1 && !RowFacet(of, o, rep, k, of.Facet!, back)) return;
        }

        /// <summary>A state, as a refusal names it.</summary>
        private static string Described(MarkupOf of, int s)
        {
            if (of.KeepOwn && s == 0) return "what the element holds as the page wrote it";
            for (var i = of.Shapes.Count - 1; i >= 0; i--)
            {
                var o = of.Shapes[i];
                if (s < o.Base) continue;
                var rest = s - o.Base;
                var lengths = new List<string>();
                foreach (var rep in o.Reps)
                {
                    lengths.Add((rep.Max - rest % (rep.Max - rep.Min + 1)).ToString(CultureInfo.InvariantCulture));
                    rest /= rep.Max - rep.Min + 1;
                }
                return $"shape {i + 1}" + (lengths.Count == 0 ? string.Empty : " with its list" + (lengths.Count > 1 ? "s" : string.Empty) + " at " + string.Join(" and ", lengths) + " rows");
            }
            return $"state {s + 1}";
        }

        /// <summary>
        /// A row whose shape changes with its item: each of its shapes drawn with the list at its longest and every
        /// other row at rest, and nothing but the row allowed to move. State 0 is the row not there (the list shorter).
        /// </summary>
        private bool RowFacet(MarkupOf of, ShapeOut o, MkRep rep, int k, string facet, Action back)
        {
            var at = rep.At;
            var full = of.States[o.Base];
            var mine = new List<Target>();
            for (var c = 0; c < rep.Copies[k].Count; c++) mine.AddRange(o.TopsOf(rep, k, c));
            var inside = new HashSet<VisualElement>(mine.SelectMany(m => Subtree(m.Ve)));
            var outside = Subtree(_built.Root).Where(v => !inside.Contains(v)).ToList();
            var rest = new Dictionary<VisualElement, UnityEngine.Rect>();
            var baseline = Variant(() => Show(of, full), back, () => { foreach (var v in outside) rest[v] = v.layout; });
            if (baseline == null) { Refuse(at, $"the list written into \"{of.T.Name}\" changes the scene's structure ({_reshaped})"); return false; }
            var plan = new StylePlan { Var = rep.Var + ".w[" + k.ToString(CultureInfo.InvariantCulture) + "]", States = new Dictionary<object, Dictionary<string, SceneSlots.Value>>() };
            var moved = new HashSet<string>(StringComparer.Ordinal);
            for (var c = 0; c < rep.Copies[k].Count; c++)
            {
                var shown = new HashSet<Target>(full);
                shown.ExceptWith(o.TopsOf(rep, k, 0));
                shown.UnionWith(o.TopsOf(rep, k, c));
                if (Positional(of, shown) is { } why) { Refuse(at, why); return false; }
                VisualElement? shifted = null;
                var drawn = Variant(() => Show(of, shown), back, () => shifted = outside.FirstOrDefault(v => Apart(v.layout, rest[v])));
                if (drawn == null) { Refuse(at, $"row {k + 1} of the list written into \"{of.T.Name}\", in its shape {c + 1}, changes the scene's structure ({_reshaped})"); return false; }
                if (shifted != null)
                {
                    Refuse(at, $"row {k + 1} of the list written into \"{of.T.Name}\" is another size in its shape {c + 1}, which moves what is around it (not translated yet: rows whose shapes are one size are)");
                    return false;
                }
                plan.States[(double)(c + 1)] = drawn;
                foreach (var pair in drawn)
                    if (!baseline.TryGetValue(pair.Key, out var b) || !Near(b, pair.Value)) moved.Add(pair.Key);
            }
            foreach (var top in mine) moved.Add(DomSlots.Slot(top.Name) + "_v");
            Complete(plan.States.Values, moved);
            var none = new Dictionary<string, SceneSlots.Value>(plan.States[1.0], StringComparer.Ordinal);
            foreach (var top in mine) none[DomSlots.Slot(top.Name) + "_v"] = new SceneSlots.Value(0f);
            plan.States[0.0] = none;
            foreach (var slot in moved) if (!Claim(slot, facet, at)) return false;
            rep.RowPlans[k] = plan;
            return true;

            static bool Apart(UnityEngine.Rect a, UnityEngine.Rect b)
                => Math.Abs(a.x - b.x) >= Noise || Math.Abs(a.y - b.y) >= Noise || Math.Abs(a.width - b.width) >= Noise || Math.Abs(a.height - b.height) >= Noise;
            static bool Near(SceneSlots.Value a, SceneSlots.Value b) => a.Equals(b) || a.IsNumber && b.IsNumber && Math.Abs(a.Number - b.Number) < Noise;
        }

        /// <summary>
        /// A gated row (<see cref="Gate"/>): each choice's shapes drawn with every other choice at rest, the list at its
        /// longest and every other row at rest, nothing outside the row allowed to move. A choice is a state of its own;
        /// choices whose shapes move a slot in common are laid out together, every combination of their shapes, as
        /// elements whose states move the same things are (<see cref="MarkupStates"/>). So is a choice drawing nothing at
        /// rest but something in another shape, with every choice that changes size or moves the rest of the row: nothing
        /// at rest shows where those put it.
        /// </summary>
        private bool GateFacets(MarkupOf of, ShapeOut o, MkRep rep, int k, string facet, Action back)
        {
            var at = rep.At;
            var full = of.States[o.Base];
            var choices = rep.Gates[k];
            var mine = o.TopsOf(rep, k, 0).Concat(choices.SelectMany(ch => ch.Keys.SelectMany(key => o.TopsOf(rep, k, key)))).ToList();
            var inside = new HashSet<VisualElement>(mine.SelectMany(m => Subtree(m.Ve)));
            var outside = Subtree(_built.Root).Where(v => !inside.Contains(v)).ToList();
            var rest = new Dictionary<VisualElement, UnityEngine.Rect>();
            // the size each choice's shape at rest takes; per choice, whether another of its shapes takes another size or moves the rest of the row
            var restSize = new UnityEngine.Vector2[choices.Count];
            var shifts = new bool[choices.Count];
            var baseline = Variant(() => Show(of, full), back, () =>
            {
                foreach (var v in outside.Concat(inside)) rest[v] = v.layout;
                for (var g = 0; g < choices.Count; g++) restSize[g] = Size(o.TopsOf(rep, k, choices[g].Keys[0]));
            });
            if (baseline == null) { Refuse(at, $"the list written into \"{of.T.Name}\" changes the scene's structure ({_reshaped})"); return false; }
            var ownOf = choices.Select(ch => new HashSet<VisualElement>(ch.Keys.SelectMany(key => o.TopsOf(rep, k, key)).SelectMany(t => Subtree(t.Ve)))).ToList();

            // some of the row's choices in the given shapes, the rest at rest
            Dictionary<string, SceneSlots.Value>? Draw(List<int> members, int[] shapes)
            {
                var shown = new HashSet<Target>(full);
                for (var i = 0; i < members.Count; i++)
                {
                    var keys = choices[members[i]].Keys;
                    shown.ExceptWith(o.TopsOf(rep, k, keys[0]));
                    shown.UnionWith(o.TopsOf(rep, k, keys[shapes[i]]));
                }
                var what = string.Join(" and ", members.Select((m, i) => $"its choice {m + 1} in shape {shapes[i] + 1}"));
                if (Positional(of, shown) is { } why) { Refuse(at, why); return null; }
                VisualElement? shifted = null;
                var drawn = Variant(() => Show(of, shown), back, () =>
                {
                    shifted = outside.FirstOrDefault(v => Apart(v.layout, rest[v]));
                    if (members.Count != 1) return;
                    var g = members[0];
                    var size = Size(o.TopsOf(rep, k, choices[g].Keys[shapes[0]]));
                    if (Math.Abs(size.x - restSize[g].x) >= Noise || Math.Abs(size.y - restSize[g].y) >= Noise
                        || inside.Any(v => !ownOf[g].Contains(v) && Apart(v.layout, rest[v])))
                        shifts[g] = true;
                });
                if (drawn == null) { Refuse(at, $"row {k + 1} of the list written into \"{of.T.Name}\", {what}, changes the scene's structure ({_reshaped})"); return null; }
                if (shifted != null)
                {
                    Refuse(at, $"row {k + 1} of the list written into \"{of.T.Name}\" is another size with {what}, which moves what is around it (not translated yet: rows whose shapes are one size are)");
                    return null;
                }
                return drawn;
            }
            HashSet<string> MovedFrom(Dictionary<string, SceneSlots.Value> drawn)
                => new(drawn.Where(p => !baseline.TryGetValue(p.Key, out var b) || !Near(b, p.Value)).Select(p => p.Key), StringComparer.Ordinal);

            // each choice alone
            var alone = new List<List<Dictionary<string, SceneSlots.Value>>>();
            var moves = new List<HashSet<string>>();
            for (var g = 0; g < choices.Count; g++)
            {
                var states = new List<Dictionary<string, SceneSlots.Value>> { baseline };
                var moved = new HashSet<string>(StringComparer.Ordinal);
                for (var c = 1; c < choices[g].Keys.Count; c++)
                {
                    if (Draw(new List<int> { g }, new[] { c }) is not { } drawn) return false;
                    states.Add(drawn);
                    moved.UnionWith(MovedFrom(drawn));
                }
                foreach (var key in choices[g].Keys)
                    foreach (var top in o.TopsOf(rep, k, key)) moved.Add(DomSlots.Slot(top.Name) + "_v");
                alone.Add(states);
                moves.Add(moved);
            }

            // laid out together: choices moving a slot in common, and one drawing nothing at rest with every other
            var root = Enumerable.Range(0, choices.Count).ToArray();
            int Find(int x) => root[x] == x ? x : root[x] = Find(root[x]);
            bool Unseen(int g) => o.TopsOf(rep, k, choices[g].Keys[0]).Count == 0 && choices[g].Keys.Any(key => o.TopsOf(rep, k, key).Count > 0);
            for (var i = 0; i < choices.Count; i++)
                for (var j = i + 1; j < choices.Count; j++)
                    if (moves[i].Overlaps(moves[j]) || Unseen(i) && shifts[j] || Unseen(j) && shifts[i]) root[Find(j)] = Find(i);

            foreach (var members in Enumerable.Range(0, choices.Count).GroupBy(Find).Select(x => x.ToList()))
            {
                var plan = new StylePlan { Var = rep.Var + ".g[" + (rep.GatePlans.Count + 1).ToString(CultureInfo.InvariantCulture) + "]", States = new Dictionary<object, Dictionary<string, SceneSlots.Value>>() };
                var moved = new HashSet<string>(members.SelectMany(m => moves[m]), StringComparer.Ordinal);
                var total = members.Aggregate(1L, (a, m) => a * choices[m].Keys.Count);
                if (total > MostRowShapes)
                {
                    Refuse(at, $"row {k + 1} of the list written into \"{of.T.Name}\" has choices that move the same things, in {total} combinations, more than the {MostRowShapes} laid out (not translated yet)");
                    return false;
                }
                // each combination by its place as the Lua counts them (Choose): the first member's shape the slowest
                for (var leaf = 0; leaf < total; leaf++)
                {
                    var shapes = new int[members.Count];
                    var r = leaf;
                    for (var i = members.Count - 1; i >= 0; i--) { shapes[i] = r % choices[members[i]].Keys.Count; r /= choices[members[i]].Keys.Count; }
                    var changed = Enumerable.Range(0, members.Count).Where(i => shapes[i] > 0).ToList();
                    Dictionary<string, SceneSlots.Value>? drawn = changed.Count switch
                    {
                        0 => baseline,
                        1 => alone[members[changed[0]]][shapes[changed[0]]],
                        _ => Draw(members, shapes),
                    };
                    if (drawn == null) return false;
                    plan.States[(double)(leaf + 1)] = new Dictionary<string, SceneSlots.Value>(drawn, StringComparer.Ordinal);
                    moved.UnionWith(MovedFrom(drawn));
                }
                Complete(plan.States.Values, moved);
                foreach (var slot in moved) if (!Claim(slot, facet, at)) return false;
                rep.GatePlans.Add((k, members, plan));
            }
            return true;

            static bool Apart(UnityEngine.Rect a, UnityEngine.Rect b)
                => Math.Abs(a.x - b.x) >= Noise || Math.Abs(a.y - b.y) >= Noise || Math.Abs(a.width - b.width) >= Noise || Math.Abs(a.height - b.height) >= Noise;
            static bool Near(SceneSlots.Value a, SceneSlots.Value b) => a.Equals(b) || a.IsNumber && b.IsNumber && Math.Abs(a.Number - b.Number) < Noise;
            // what some elements span together, laid out as they stand (none: nothing)
            static UnityEngine.Vector2 Size(List<Target> tops)
            {
                if (tops.Count == 0) return default;
                var laid = tops.Select(x => x.Ve.layout).ToList();
                return new UnityEngine.Vector2(laid.Max(r => r.xMax) - laid.Min(r => r.xMin), laid.Max(r => r.yMax) - laid.Min(r => r.yMin));
            }
        }

        /// <summary>
        /// Whether CSS picks elements by their place among their siblings in a way a state changes: laid out once
        /// with every row and shape in the page, `:last-child`, `:nth-child`, `:empty` and the like would match
        /// otherwise than with only what the state shows there, as a browser has it.
        /// </summary>
        private string? Positional(MarkupOf of, HashSet<Target> shown)
        {
            var gone = of.Hide.Where(h => !shown.Contains(h)).Select(h => h.Node).ToList();
            if (gone.Count == 0) return null;
            var goneSet = new HashSet<HtmlNode>(gone);
            bool Out(HtmlNode x)
            {
                for (var p = x; p != null; p = p.Parent) if (goneSet.Contains(p)) return true;
                return false;
            }
            var test = new List<HtmlNode>();
            foreach (var parent in gone.Select(g => g.Parent).OfType<HtmlNode>().Distinct())
            {
                if (!Out(parent)) test.Add(parent);
                foreach (var d in Below(parent)) if (!d.IsText && !Out(d)) test.Add(d);
            }
            test = test.Distinct().ToList();
            var rules = _built.Rules;
            List<int> Matching(HtmlNode x)
            {
                var l = new List<int>();
                for (var i = 0; i < rules.Count; i++) if (rules[i].Selectors.Any(sel => sel.Matches(x))) l.Add(i);
                return l;
            }
            var before = test.Select(Matching).ToList();
            var taken = gone.Where(g => g.Parent != null).Select(g => (Node: g, Parent: g.Parent!, At: g.Parent!.Children.IndexOf(g))).ToList();
            try
            {
                foreach (var (node, parent, _) in taken) parent.Children.Remove(node);
                for (var i = 0; i < test.Count; i++)
                    if (!Matching(test[i]).SequenceEqual(before[i]))
                        return $"CSS picking elements by their place among their siblings (:last-child, :nth-child, :empty and the like) matches a <{test[i].Tag}> otherwise when the markup written into \"{of.T.Name}\" shows fewer rows or another shape (not translated yet)";
                return null;
            }
            finally
            {
                foreach (var (node, parent, index) in taken.OrderBy(x => x.At)) parent.Children.Insert(Math.Min(index, parent.Children.Count), node);
            }
        }

        /// <summary>
        /// The elements whose writes are worked out together, and how to show them: first everything shown at
        /// rest, then, per shape of markup not shown at rest, the elements it makes with that shape shown (its lists
        /// at their longest), and per shape of a row after its first, the elements every row makes in that shape.
        /// </summary>
        private List<(HashSet<Target> Group, Action<bool>? Show)> Groups()
        {
            var groups = new List<(HashSet<Target>, Action<bool>?)>();
            var hidden = new HashSet<Target>();
            foreach (var of in _markupOf.Values)
            {
                if (!Varies(of)) continue;
                var display = of.Hide.ToDictionary(x => x, x => x.Ve.style.display);
                for (var s = 0; s < of.Shapes.Count; s++)
                {
                    var o = of.Shapes[s];
                    var atRest = !of.KeepOwn && s == 0;
                    var most = o.Reps.SelectMany(r => r.Copies.Select(x => x.Count).Concat(r.Gates.Values.SelectMany(g => g.Select(x => x.Keys.Count)))).DefaultIfEmpty(1).Max();
                    for (var c = 0; c < most; c++)
                    {
                        if (atRest && c == 0) continue;
                        var made = new HashSet<Target>(c == 0 ? o.Made : new List<Target>());
                        var shown = new HashSet<Target>(of.States[o.Base]);
                        foreach (var rep in o.Reps)
                            for (var k = 0; k < rep.Max; k++)
                            {
                                // a gated row: its elements outside its choices at rest, and each choice's shape c
                                if (rep.Gates.TryGetValue(k, out var gates))
                                {
                                    if (c == 0 && o.RowMade.TryGetValue((rep, k, 0), out var own)) made.UnionWith(own);
                                    foreach (var (_, keys) in gates)
                                    {
                                        if (c >= keys.Count) continue;
                                        if (o.RowMade.TryGetValue((rep, k, keys[c]), out var gm)) made.UnionWith(gm);
                                        if (c == 0) continue;
                                        shown.ExceptWith(o.TopsOf(rep, k, keys[0]));
                                        shown.UnionWith(o.TopsOf(rep, k, keys[c]));
                                    }
                                    continue;
                                }
                                if (c >= rep.Copies[k].Count) continue;
                                if (o.RowMade.TryGetValue((rep, k, c), out var m)) made.UnionWith(m);
                                if (c == 0) continue;
                                shown.ExceptWith(o.TopsOf(rep, k, 0));
                                shown.UnionWith(o.TopsOf(rep, k, c));
                            }
                        if (made.Count == 0) continue;
                        hidden.UnionWith(made);
                        var view = shown;
                        groups.Add((made, on =>
                        {
                            if (on) Show(of, view);
                            else foreach (var pair in display) pair.Key.Ve.style.display = pair.Value;
                        }));
                    }
                }
            }
            groups.Insert(0, (new HashSet<Target>(_order.Where(t => !hidden.Contains(t))), null));
            return groups;
        }

        /// <summary>
        /// The Lua of one markup write: its choices as the script makes them, and in each shape the rows its lists
        /// have, the state that shows them, and each shown row's values (and, for a row whose shape changes with its
        /// item, the shape it takes).
        /// </summary>
        private void EmitMarkup(JsToLua lua, MarkupWrite w)
        {
            var of = w.Of;
            var leaf = 0;
            Choose(lua, w.Template, string.Empty, pad => Leaf(w.Shapes[leaf++], pad));

            void Leaf(ShapeOut o, string pad)
            {
                foreach (var rep in o.Reps) Count(lua, rep, pad);
                // a list of another shape's rows that a lookup lists is empty while this shape shows
                foreach (var other in of.Shapes.SelectMany(x => x.Reps).Where(r => r.Queried && !o.Reps.Contains(r)).Distinct())
                    lua.Emit(pad + other.Var + ".n = 0");
                if (of.Plan != null && of.KeyVar != null)
                {
                    lua.Emit(pad + of.KeyVar + " = " + Key(o));
                    lua.Emit(pad + "v_state(" + of.Plan.Var + ", " + of.JointKey + ")");
                }
                else if (of.Plan != null) lua.Emit(pad + "v_state(" + of.Plan.Var + ", " + Key(o) + ")");
                // the elements a write makes are new ones: what listened on the old ones is gone
                foreach (var made in of.Made.Where(m => m.Listens)) lua.Emit(pad + "v_unlisten(" + Q(made.Name) + ")");
                Ops(o.Ops, pad);
                foreach (var rep in o.Reps)
                    for (var k = 0; k < rep.Max; k++)
                    {
                        if (rep.Gates.TryGetValue(k, out var gates))
                        {
                            Gated(o, rep, k, gates, pad);
                            continue;
                        }
                        var copies = rep.Copies[k].Count;
                        if (copies == 1 && !o.RowOps.ContainsKey((rep, k, 0))) continue;
                        var row = k.ToString(CultureInfo.InvariantCulture);
                        lua.Emit(pad + "if " + rep.Var + ".n > " + row + " then");
                        if (copies == 1) Ops(o.RowOps[(rep, k, 0)], pad + "  ");
                        else
                        {
                            var c = 0;
                            var kk = k;
                            Choose(lua, rep.Rows[k], pad + "  ", inner =>
                            {
                                lua.Emit(inner + "v_state(" + rep.Var + ".w[" + row + "], " + (c + 1).ToString(CultureInfo.InvariantCulture) + ")");
                                if (o.RowOps.TryGetValue((rep, kk, c), out var ops)) Ops(ops, inner);
                                c++;
                            });
                            lua.Emit(pad + "else");
                            lua.Emit(pad + "  v_state(" + rep.Var + ".w[" + row + "], 0)");
                        }
                        lua.Emit(pad + "end");
                    }
            }

            void Ops(List<Expression> ops, string pad)
            {
                foreach (var op in ops)
                {
                    // what the values' helpers did first (`acts.push(fn)`), right before the value is read
                    if (_before.TryGetValue(op, out var fx))
                        foreach (var x in fx)
                        {
                            _effectBlocks = 0;
                            try
                            {
                                var code = lua.Translate(x);
                                lua.Emit(pad + (Unwrap(x) is CallExpression && Call.IsMatch(code) ? code : "do local _ = " + code + " end"));
                                for (; _effectBlocks > 0; _effectBlocks--) lua.Emit(pad + "end");
                            }
                            finally { _effectBlocks = -1; }
                        }
                    if (_lua.TryGetValue(op, out var own)) { lua.Emit(pad + own(lua)); continue; }
                    foreach (var line in EmitOp(op, _ops[op][0], lua.Translate, early: true)) lua.Emit(pad + line);
                }
            }

            // A gated row: its own values, then per group of its choices the combination the script makes, its state
            // picked and each member's shape given its values.
            void Gated(ShapeOut o, MkRep rep, int k, List<(MkAlt Alt, List<int> Keys)> gates, string pad)
            {
                var row = k.ToString(CultureInfo.InvariantCulture);
                lua.Emit(pad + "if " + rep.Var + ".n > " + row + " then");
                if (o.RowOps.TryGetValue((rep, k, 0), out var own)) Ops(own, pad + "  ");
                foreach (var (_, members, plan) in rep.GatePlans.Where(p => p.K == k))
                {
                    var leaf = 0;
                    Choose(lua, members.Select(m => (Mk)gates[m].Alt).ToList(), pad + "  ", inner =>
                    {
                        lua.Emit(inner + "v_state(" + plan.Var + ", " + (leaf + 1).ToString(CultureInfo.InvariantCulture) + ")");
                        var shapes = new int[members.Count];
                        var r = leaf;
                        for (var i = members.Count - 1; i >= 0; i--) { shapes[i] = r % gates[members[i]].Keys.Count; r /= gates[members[i]].Keys.Count; }
                        for (var i = 0; i < members.Count; i++)
                            if (o.RowOps.TryGetValue((rep, k, gates[members[i]].Keys[shapes[i]]), out var ops)) Ops(ops, inner);
                        leaf++;
                    });
                }
                lua.Emit(pad + "end");
            }
        }

        /// <summary>A template's choices as the Lua makes them (yes first, as <see cref="Shapes"/> numbers them), each shape one call of <paramref name="leaf"/>.</summary>
        private static void Choose(JsToLua lua, List<Mk> rest, string pad, Action<string> leaf)
        {
            for (var i = 0; i < rest.Count; i++)
            {
                if (rest[i] is not MkAlt alt) continue;
                var tail = rest.Skip(i + 1).ToList();
                lua.Emit(pad + "if js_truthy(" + lua.Translate(alt.Test) + ") then");
                Choose(lua, alt.Yes.Concat(tail).ToList(), pad + "  ", leaf);
                lua.Emit(pad + "else");
                Choose(lua, alt.No.Concat(tail).ToList(), pad + "  ", leaf);
                lua.Emit(pad + "end");
                return;
            }
            leaf(pad);
        }

        /// <summary>A shape's state as the Lua works it out: its first state, on by how much shorter each of its lists is than its longest.</summary>
        private static string Key(ShapeOut o)
        {
            var sb = new StringBuilder(o.Base.ToString(CultureInfo.InvariantCulture));
            var radix = 1;
            foreach (var rep in o.Reps)
            {
                sb.Append(" + ");
                if (radix > 1) sb.Append(radix.ToString(CultureInfo.InvariantCulture)).Append(" * ");
                sb.Append('(').Append(rep.Max.ToString(CultureInfo.InvariantCulture)).Append(" - ").Append(rep.Var).Append(".n)");
                radix *= rep.Max - rep.Min + 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// A list's rows as the Lua counts them when the markup is written: the array read once, its slices taken as
        /// bounds, its filters tested item by item into the row table (`i`), and how many rows show (`n`).
        /// </summary>
        private void Count(JsToLua lua, MkRep rep, string pad)
        {
            string N(int v) => v.ToString(CultureInfo.InvariantCulture);
            var v = rep.Var;
            var most = N(rep.Max);
            if (rep.Source == null)
            {
                lua.Emit(pad + "do");
                lua.Emit(pad + "  local V_c = js_num(" + lua.Translate(rep.Count!) + ")");
                if (rep is { Start: 0, Step: 1, Op: Operator.LessThan or Operator.LessThanOrEqual })
                {
                    // counting from 0 by 1: as many rows as whole numbers below the bound (up to it, for `<=`)
                    lua.Emit(pad + "  if V_c ~= V_c then V_c = 0 end");
                    lua.Emit(pad + "  " + v + ".n = math.max(0, math.min(" + (rep.Op == Operator.LessThanOrEqual ? "math.floor(V_c) + 1" : "math.ceil(V_c)") + ", " + most + "))");
                }
                else
                {
                    // any other start and step: the loop's own steps, counted as JavaScript takes them
                    var test = rep.Op switch { Operator.LessThan => "<", Operator.LessThanOrEqual => "<=", Operator.GreaterThan => ">", _ => ">=" };
                    lua.Emit(pad + "  local V_n, V_i = 0, " + JsToLuaNumber(rep.Start));
                    lua.Emit(pad + "  while V_n < " + most + " and V_i " + test + " V_c do V_n, V_i = V_n + 1, V_i + " + JsToLuaNumber(rep.Step) + " end");
                    lua.Emit(pad + "  " + v + ".n = V_n");
                }
                lua.Emit(pad + "end");
                return;
            }
            lua.Emit(pad + v + ".a = " + lua.Translate(rep.Source));
            lua.Emit(pad + "do");
            lua.Emit(pad + "  local V_s, V_e = 0, js_len(" + v + ".a)");
            var first = rep.Stages.FindIndex(s => s.Test != null);
            var lead = first < 0 ? rep.Stages.Count : first;
            for (var i = 0; i < lead; i++)
            {
                _slice = true;
                var st = rep.Stages[i];
                lua.Emit(pad + "  V_s, V_e = v_slice(V_s, V_e, " + N(st.From) + ", " + (st.To is { } to ? N(to) : "nil") + ")");
            }
            if (first < 0)
            {
                if (rep.Sliced) lua.Emit(pad + "  " + v + ".o = V_s");
                lua.Emit(pad + "  " + v + ".n = math.min(V_e - V_s, " + most + ")");
                // the slice the callback walks, as an array of its own: the rows' items
                if (rep.Whole > 0)
                {
                    lua.Emit(pad + "  for V_k = 0, " + N(rep.Max - 1) + " do");
                    lua.Emit(pad + "    if V_k < " + v + ".n then " + v + ".v[V_k] = " + v + ".a[V_s + V_k] else " + v + ".v[V_k] = nil end");
                    lua.Emit(pad + "  end");
                    lua.Emit(pad + "  " + v + ".v.length = " + v + ".n");
                }
                lua.Emit(pad + "end");
                return;
            }
            lua.Emit(pad + "  local V_n = 0");
            if (rep.Whole > 0) lua.Emit(pad + "  local V_w = 0");
            for (var i = first; i < rep.Stages.Count; i++) lua.Emit(pad + "  local " + v + "c" + N(i + 1) + " = 0");
            lua.Emit(pad + "  for V_j = V_s, V_e - 1 do");
            lua.Emit(pad + "    local " + v + "x = " + v + ".a[V_j]");
            var depth = pad + "    ";
            // an item every stage before the callback's own array kept: one of that array's items (the view `v`)
            void View(string at)
            {
                lua.Emit(at + v + ".v[V_w] = " + v + "x");
                lua.Emit(at + "V_w = V_w + 1");
            }
            for (var i = first; i < rep.Stages.Count; i++)
            {
                if (rep.Whole == i) View(depth);
                var st = rep.Stages[i];
                var q = v + "q" + N(i + 1);
                var c = v + "c" + N(i + 1);
                lua.Emit(depth + "local " + q + " = " + c);
                lua.Emit(depth + c + " = " + c + " + 1");
                lua.Emit(depth + (st.Test != null
                    ? "if " + (st.Negate ? "not " : string.Empty) + "js_truthy(" + lua.Translate(st.Test) + ") then"
                    : "if " + q + " >= " + N(st.From) + (st.To is { } to ? " and " + q + " < " + N(to) : string.Empty) + " then"));
                depth += "  ";
            }
            if (rep.Whole == rep.Stages.Count) View(depth);
            lua.Emit(depth + v + ".i[V_n] = V_j");
            if (rep.Positions) lua.Emit(depth + v + ".m[V_n] = " + v + "q" + N(rep.Stages.Count));
            lua.Emit(depth + "V_n = V_n + 1");
            for (var i = first; i < rep.Stages.Count; i++)
            {
                depth = depth.Substring(0, depth.Length - 2);
                lua.Emit(depth + "end");
            }
            lua.Emit(pad + "    if V_n >= " + most + " then break end");
            lua.Emit(pad + "  end");
            lua.Emit(pad + "  " + v + ".n = V_n");
            if (rep.Whole > 0)
            {
                lua.Emit(pad + "  for V_k = V_w, " + N(rep.Max - 1) + " do " + v + ".v[V_k] = nil end");
                lua.Emit(pad + "  " + v + ".v.length = V_w");
            }
            lua.Emit(pad + "end");
        }

        /// <summary>Each list's table, built once: its row table and per row whose shape changes with its item, what each shape draws.</summary>
        private string Lists()
        {
            var sb = new StringBuilder();
            var joint = _markupOf.Values.Where(m => m.KeyVar != null).Select(m => m.KeyVar!).ToList();
            if (joint.Count > 0)
                sb.Append("-- the state each element laid out with others is in, for the combination a write picks\n")
                  .Append("local ").Append(string.Join(", ", joint)).Append(" = ").Append(string.Join(", ", joint.Select(_ => "0"))).Append('\n');
            foreach (var rep in _reps)
            {
                if (rep.Rows.Count == 0 && rep.Max > 0) continue;
                string Zeros() => "{ " + string.Join(", ", Enumerable.Range(0, Math.Max(1, rep.Max)).Select(k => "[" + k.ToString(CultureInfo.InvariantCulture) + "] = 0")) + " }";
                sb.Append("local ").Append(rep.Var).Append(" = { n = 0, o = 0");
                if (rep.Filtered) sb.Append(", i = ").Append(Zeros());
                if (rep.Positions) sb.Append(", m = ").Append(Zeros());
                // the array the callback walks, made once and refilled in place
                if (rep.Whole > 0) sb.Append(", v = ").Append(Zeros().Replace(" }", ", length = 0 }"));
                if (rep.RowPlans.Count > 0)
                {
                    sb.Append(", w = {");
                    foreach (var pair in rep.RowPlans.OrderBy(p => p.Key))
                    {
                        sb.Append("\n  [").Append(pair.Key.ToString(CultureInfo.InvariantCulture)).Append("] = {");
                        foreach (var state in pair.Value.States!.OrderBy(x => (double)x.Key))
                            sb.Append("\n    [").Append(JsToLuaNumber((double)state.Key)).Append("] = ")
                              .Append(Table(state.Value.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, Value(x.Value))))).Append(',');
                        sb.Append("\n  },");
                    }
                    sb.Append("\n}");
                }
                if (rep.GatePlans.Count > 0)
                {
                    // per choice of a gated row (or choices laid out together): what each shape draws
                    sb.Append(", g = {");
                    for (var i = 0; i < rep.GatePlans.Count; i++)
                    {
                        sb.Append("\n  [").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("] = {");
                        foreach (var state in rep.GatePlans[i].Plan.States!.OrderBy(x => (double)x.Key))
                            sb.Append("\n    [").Append(JsToLuaNumber((double)state.Key)).Append("] = ")
                              .Append(Table(state.Value.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, Value(x.Value))))).Append(',');
                        sb.Append("\n  },");
                    }
                    sb.Append("\n}");
                }
                sb.Append(" } -- a list of markup: its rows shown (n), the first item (o), the item each row reads (i)\n");
            }
            if (_slice)
                sb.Append("-- a slice of the rows between s and e, as Array.prototype.slice takes one\n")
                  .Append("local function v_slice(s, e, a, b)\n  local len = e - s\n")
                  .Append("  if a < 0 then a = math.max(len + a, 0) else a = math.min(a, len) end\n")
                  .Append("  if b == nil then b = len elseif b < 0 then b = math.max(len + b, 0) else b = math.min(b, len) end\n")
                  .Append("  if b < a then b = a end\n  return s + a, s + b\nend\n");
            // ponytail: one table per lookup, so a list kept from an earlier lookup at the same place grows and shrinks
            // with the next one where a browser's would keep its length; a copy per lookup would be a table per call
            if (_rowsList)
                sb.Append("-- a lookup's list of a list's rows: as long as the rows shown when it is looked up\n")
                  .Append("local function v_rows(l, p, r) l.length = p + r.n return l end\n");
            if (_shownList)
                sb.Append("-- a lookup's list of what markup makes: the members shown when it is looked up (vis: what shows each)\n")
                  .Append("local function v_shown(l, all, vis)\n  local n = 0\n  for i = 0, all.length - 1 do\n    local ok, s = true, vis[i]\n")
                  .Append("    if s then for j = 1, #s do if V_D[s[j]] == 0 then ok = false break end end end\n")
                  .Append("    if ok then l[n] = all[i] n = n + 1 end\n  end\n  for i = n, l.length - 1 do l[i] = nil end\n  l.length = n\n  return l\nend\n");
            return sb.ToString();
        }

        /// <summary>While a helper's effect is written: the blocks opened for what a function made there keeps, closed after it (-1: none being written).</summary>
        private int _effectBlocks = -1;
        private int _captures;
        private readonly HashSet<Node> _capturing = new();

        /// <summary>A value of markup, translated in its own scope; a name there as what it stands for.</summary>
        private string? MarkupExpression(JsToLua lua, Node e)
        {
            // A function made by a helper's effect (`acts.push(() => pick(g))`) keeps what it reads of the row as it is
            // made, as a JavaScript closure does: each such name in a local of its own, set just before it.
            if (_effectBlocks >= 0 && e is IFunction made && _active != null && !_capturing.Contains(e) && LateNames(made, _active) is { Count: > 0 } late)
            {
                var kept = Copy(_active);
                foreach (var name in late)
                {
                    var (expr, at) = _active[name];
                    var local = "V_K" + (++_captures).ToString(CultureInfo.InvariantCulture);
                    var was = _active;
                    _active = at;
                    string value;
                    try { value = lua.Translate(expr); }
                    finally { _active = was; }
                    lua.Emit("do local " + local + " = " + value);
                    _effectBlocks++;
                    kept[name] = (Held(local), null);
                }
                var outer = _active;
                _capturing.Add(e);
                _active = kept;
                try { return lua.Translate(e); }
                finally { _active = outer; _capturing.Remove(e); }
            }
            // an attribute's whole text by its one value, from a table made once: no string is built
            if (e is Expression je && _joined.TryGetValue(je, out var pieces))
            {
                var holes = pieces.Where(x => x.Hole != null).ToList();
                if (holes.Count == 1 && Finite(holes[0].Hole!) is { Count: > 0 } set)
                {
                    if (!_joinedTable.TryGetValue(je, out var table))
                    {
                        table = "V_J" + (_tables.Count + 1).ToString(CultureInfo.InvariantCulture);
                        var entries = set.Select(v => "[" + (v is double d ? JsToLuaNumber(d) : Q((string)v)) + "] = "
                                                      + Q(string.Concat(pieces.Select(x => x.Literal ?? Text(v))))).ToList();
                        _tables.Add("local " + table + " = { " + string.Join(", ", entries) + " } -- a value in markup, as its attribute's text\n");
                        _joinedTable[je] = table;
                    }
                    return table + "[" + lua.Translate(holes[0].Hole!) + "]";
                }
            }
            if (e is ParenthesizedExpression w && _bindOf.TryGetValue(w, out var env))
            {
                var was = _active;
                _active = env;
                try { return lua.Translate(w.Expression); }
                finally { _active = was; }
            }
            if (_active != null && e is Identifier id && Decl(id) is { } d && _active.TryGetValue(d, out var arg))
            {
                var was = _active;
                _active = arg.Env;
                try { return "(" + lua.Translate(arg.Expr) + ")"; }
                finally { _active = was; }
            }
            return null;
        }
    }
}

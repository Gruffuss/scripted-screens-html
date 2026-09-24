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
        private sealed class MkVal : Mk { public readonly Expression Expr; public MkVal(Expression expr) { Expr = expr; } }
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
            /// <summary>Per shape, in template order: its state and the writes (synthetic nodes) it makes.</summary>
            public readonly List<(int State, List<Expression> Ops)> Shapes = new();
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
            /// <summary>Per state: the top elements it shows (state 0 is the element's own children when they are kept).</summary>
            public readonly List<List<Target>> Tops = new();
            public readonly List<Target> Made = new();
            /// <summary>Per state, the elements its markup makes.</summary>
            public readonly Dictionary<int, List<Target>> MadeIn = new();
            public StylePlan? Plan;
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
                        Refuse(a, els?.Bad ?? "innerHTML on an element chosen at run time (part 1: one fixed element)");
                        continue;
                    }
                    var t = els.Ts[0];
                    if (Tpl(a.Right, null, a, 0) is not { } tpl) continue;
                    if (!_markupOf.TryGetValue(t, out var of)) _markupOf[t] = of = new MarkupOf { T = t };
                    var w = new MarkupWrite { At = a, Of = of, Template = tpl };
                    of.Writes.Add(w);
                    _markup[a] = w;
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
                        var first = chain.FindIndex(c => c is StringLiteral or TemplateLiteral || parts[chain.IndexOf(c)].Any(m => m is not MkVal));
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

                case Identifier id when Decl(id) is { } decl:
                    {
                        if (env != null && env.TryGetValue(decl, out var arg)) return Tpl(arg.Expr, arg.Env, write, depth + 1);
                        if (Given(decl) is [{ } init] && !_writes.ContainsKey(decl) && Markupish(init))
                            return Tpl(init, null, write, depth + 1);
                        return Value(e, env, write);
                    }

                case CallExpression { Callee: Identifier f } call when Function(f) is { } fn && Markupish((Node)(object)fn):
                    {
                        if (call.Arguments.Any(a => a is SpreadElement || Writes(a))) { Refuse(call, $"markup from \"{f.Name}\" called with arguments that change something (not translated yet)"); return null; }
                        var bind = Inlined.Of(fn, call, env);
                        var inlined = fn.Body is Expression body ? Tpl(body, bind, write, depth + 1) : Steps(((FunctionBody)fn.Body).Body, 0, bind, write, depth + 1, fn);
                        return inlined;
                    }

                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "join" or "map" or "reduce" or "repeat" or "concat" or "flatMap" } } } list when Markupish(list):
                    Refuse(list, "a list of markup, repeated over an array (innerHTML part 2)");
                    return null;

                default:
                    return Value(e, env, write);
            }
        }

        /// <summary>A template that is one value, or one literal with no markup in it: a value, not a shape.</summary>
        private static bool Plain(List<Mk> t)
            => t.Count == 1 && (t[0] is MkVal || t[0] is MkLit l && l.Text.IndexOfAny(new[] { '<', '&' }) < 0);

        private List<Mk>? Value(Expression e, Inlined? env, Node write)
        {
            if (Markupish(e))
            {
                Refuse(e, "markup built in a way the compile cannot follow (a list, or text built up in steps, is part 2)");
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
                    && ws.OfType<AssignmentExpression>().Any(a => Markup.Everything(a.Right).Any(y => y is StringLiteral { Value: var v } && v.IndexOf('<') >= 0)))
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

        /// <summary>A shape as markup: its literals, and each value as a marker (with its index) or as a stand-in for layout.</summary>
        private static string Render(List<Mk> shape, Func<int, MkVal, string> value)
        {
            var sb = new StringBuilder();
            var k = 0;
            foreach (var m in shape)
                if (m is MkLit l) sb.Append(l.Text);
                else sb.Append(value(k++, (MkVal)m));
            return sb.ToString();
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
                        var vals = shape.OfType<MkVal>().ToList();
                        var html = Render(shape, (k, _) => Marker(k));
                        if (System.Text.RegularExpressions.Regex.IsMatch(html, "</?\\s*" + Open)) { Refuse(w.At, "markup whose tag is a value (not translated)"); return; }
                        var marked = HtmlParser.Parse(html, _ => { });
                        foreach (var top in marked.Children) top.Parent = t.Node;
                        if (Holes(marked, vals, w.At) is not { } holes) return;
                        // drawn as part of an element's rich text: its attribute values are the text's shapes
                        foreach (var (v, attr, on) in holes)
                            if (attr != null && Folds(on)) expand.Add(v);
                        parsed.Add((w, shape, vals, marked));
                    }
                    if (expand.Count == 0 || round == 1) break;
                    var map = new Dictionary<MkVal, Mk>();
                    foreach (var v in expand)
                    {
                        if (Finite(v.Expr) is not { Count: > 0 } set) { Refuse(v.Expr, "a value in an attribute of text markup, only known at run time (not translated yet)"); return; }
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

        private static List<Mk> Replace(List<Mk> tpl, Dictionary<MkVal, Mk> map)
            => tpl.Select(m => m switch
            {
                MkVal v when map.TryGetValue(v, out var c) => c,
                MkAlt a => new MkAlt(a.Test, Replace(a.Yes, map), Replace(a.No, map)),
                _ => m,
            }).ToList();

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
        private static bool TextOnly(HtmlNode root, List<CssRule> rules) => Below(root).All(n => n.IsText || Folds(n));

        /// <summary>
        /// A text-level tag the renderer folds into its element's rich text: no id (a data target) and no class
        /// (a styled box), which keep an element of their own (HtmlRenderer.KeepsOwnElement).
        /// </summary>
        private static bool Folds(HtmlNode n)
            => TextTags.Contains(n.Tag!) && n.Attr("id") == null && n.Attr("class") == null && !HtmlRenderer.Blockified(n);

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
                t.Texts.Add((at, value));
                _ops[at] = new List<Target> { t };
                w.Shapes.Add((0, new List<Expression> { at }));
            }
        }

        /// <summary>
        /// Element markup: every shape's elements under the element (and what the page wrote there, while no
        /// write has replaced it yet), one shape shown at a time; each value the write it stands for.
        /// </summary>
        private void Union(MarkupOf of, List<(MarkupWrite W, List<Mk> Shape, List<MkVal> Vals, HtmlNode Marked)> parsed)
        {
            var t = of.T;
            var rules = _built.Rules;
            // What the page wrote shows until the first write - unless a write runs as the page loads,
            // before a browser has drawn anything.
            of.KeepOwn = !of.Writes.Any(w => AtSetup(w.At));
            if (of.KeepOwn && (t.Ve is Label || t.Node.Children.Any(c => c.IsText && c.Text.Trim().Length > 0)))
            {
                Refuse(of.Writes[0].At, $"the text \"{t.Name}\" holds as the page wrote it, shown until markup replaces it later, next to elements the markup makes (not translated yet)");
                return;
            }
            // each shape's markup with stand-ins, parsed; its nodes paired with the marked parse
            var built = new List<(MarkupWrite W, List<MkVal> Vals, HtmlNode Marked, HtmlNode Stand, Dictionary<HtmlNode, HtmlNode> Pair)>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (w, shape, vals, marked) in parsed)
            {
                var stand = HtmlParser.Parse(Render(shape, (_, v) => StandIn(v)), _ => { });
                var pair = new Dictionary<HtmlNode, HtmlNode>();
                if (!Pair(stand, marked, pair)) { Refuse(w.At, "markup whose values change what it parses into (not translated yet)"); return; }
                foreach (var top in marked.Children)
                    if (top.IsText && top.Text.Trim().Length > 0) { Refuse(w.At, $"text directly inside \"{t.Name}\" next to the elements the markup makes (not translated yet)"); return; }
                foreach (var n in Below(stand))
                    if (n.Attr("id") is { } id && (!ids.Add(id) || _built.ById.TryGetValue(id, out var holder) && (of.KeepOwn || holder == null || !Inside(holder, t.Ve))))
                    {
                        Refuse(w.At, $"the id \"{id}\" in markup, which the page or another shape of it has too (not translated yet)");
                        return;
                    }
                built.Add((w, vals, marked, stand, pair));
            }

            // Into the page: the element built again around its own children (while they show) and every
            // shape's, so what its parent does for its children - list markers, gaps, rows - is done for these.
            // (copies: the compile names what it drives for good, and the page's own nodes are put back as they were)
            var own = HtmlParser.Parse(HtmlRenderer.ToHtml(t.Node, outer: false, keepIds: true), _ => { }).Children.ToList();
            if (!of.KeepOwn) foreach (var child in t.Ve.Children().ToList()) Drop(child);
            Rebuild(of, (of.KeepOwn ? own : new List<HtmlNode>()).Concat(built.SelectMany(b => b.Stand.Children)).ToList());
            if (_refused.Count > 0) return;

            // the states: the element's own children (when kept), then one per shape, in the Lua's order
            if (of.KeepOwn) of.Tops.Add(own.Where(n => !n.IsText && Ve(n) != null).Select(n => TargetOf(Ve(n)!, n)).ToList());
            foreach (var b in built)
            {
                var state = of.Tops.Count;
                var tops = new List<Target>();
                foreach (var node in b.Stand.Children)
                    if (!node.IsText && Ve(node) is { } ve) tops.Add(TargetOf(ve, node));
                of.Tops.Add(tops);
                of.MadeIn[state] = new List<Target>();
                foreach (var node in Below(b.Stand))
                    if (!node.IsText && Ve(node) is { } made)
                    {
                        var target = TargetOf(made, node);
                        if (!of.Made.Contains(target)) of.Made.Add(target);
                        of.MadeIn[state].Add(target);
                        _madeBy[target] = of;
                    }
                var ops = new List<Expression>();
                b.W.Shapes.Add((state, ops));
                if (!Writes(of, b.Vals, b.Marked, b.Pair, ops, b.W.At)) return;
            }
            if (of.Tops.Count > 1)
            {
                foreach (var tops in of.Tops) foreach (var top in tops) top.Top = true;
                // at rest: the element's own children, or the first shape
                for (var s = 1; s < of.Tops.Count; s++)
                    foreach (var top in of.Tops[s]) top.Ve.style.display = DisplayStyle.None;
            }
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
        private bool Writes(MarkupOf of, List<MkVal> vals, HtmlNode marked, Dictionary<HtmlNode, HtmlNode> pair, List<Expression> ops, Node write)
        {
            var back = pair.ToDictionary(p => p.Value, p => p.Key);
            var labels = new HashSet<Target>();
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
                    if (!labels.Add(target)) continue;
                    var value = Made(new StringLiteral(string.Empty, string.Empty), write);
                    _givenPieces[value] = Split(HtmlRenderer.RichText(owner, _built.Rules), vals);
                    var at = Made(new AssignmentExpression(Operator.Assignment, new MemberExpression(new Identifier("__markup"), new Identifier("textContent"), false, false), value), write);
                    target.Texts.Add((at, value));
                    _ops[at] = new List<Target> { target };
                    ops.Add(at);
                    continue;
                }
                if (mnode.AttributeCount == 0 || Ve(node) is not { } ve) continue;
                var el = TargetOf(ve, node);
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
                            if (!el.Styles.TryGetValue(css, out var list)) el.Styles[css] = list = new();
                            list.Add((at, value));
                            _ops[at] = new List<Target> { el };
                            ops.Add(at);
                        }
                        continue;
                    }
                    var joined = Joined(attr.Value, vals, write);
                    if (name == "class")
                    {
                        var at = Made(new AssignmentExpression(Operator.Assignment, new MemberExpression(new Identifier("__markup"), new Identifier("className"), false, false), joined), write);
                        ClassName(r, at, joined);
                        ops.Add(at);
                        continue;
                    }
                    var set = Made(new AssignmentExpression(Operator.Assignment, new MemberExpression(new Identifier("__markup"), new Identifier(name), false, false), joined), write);
                    AttrWrite(r, set, name, "set", joined);
                    ops.Add(set);
                }
            }
            return true;
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
                    if (!before.Contains(id) || _built.ById[id] is { } now && Inside(now, made))
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
        /// Whether a list (or a first match) includes elements markup makes in only some of its shapes: a
        /// browser's list holds the shape shown, which one fixed at compile time cannot follow. An element by
        /// its id is fine - a script looks up the elements of the shape it has just written.
        /// </summary>
        private string? Transient(Expression at, Els els)
        {
            if (!els.List && !(at is CallExpression c && DomCall(c) is { Kind: "querySelector" })) return null;
            foreach (var t in els.Ts)
                if (_madeBy.TryGetValue(t, out var of) && of.Tops.Count > 1)
                    return $"\"{t.Name}\", which markup written into \"{of.T.Name}\" makes in only some of its shapes (not translated yet)";
            return null;
        }

        /// <summary>The states of each element's union of shapes, laid out: one per shape (and the element's own children).</summary>
        private void MarkupStates()
        {
            var n = 0;
            foreach (var of in _markupOf.Values)
            {
                if (of.Inline || of.Tops.Count < 2) continue;
                var plan = new StylePlan { Var = "V_SH" + (++n).ToString(CultureInfo.InvariantCulture), States = new Dictionary<object, Dictionary<string, SceneSlots.Value>>() };
                var display = of.Tops.SelectMany(x => x).Distinct().ToDictionary(x => x, x => x.Ve.style.display);
                var moved = new HashSet<string>(StringComparer.Ordinal);
                var at = of.Writes[0].At;
                for (var s = 0; s < of.Tops.Count; s++)
                {
                    var state = s;
                    var drawn = Variant(() =>
                    {
                        for (var j = 0; j < of.Tops.Count; j++)
                            foreach (var top in of.Tops[j]) top.Ve.style.display = j == state ? StyleKeyword.Null : DisplayStyle.None;
                    }, () => { foreach (var pair in display) pair.Key.Ve.style.display = pair.Value; });
                    if (drawn == null) { Refuse(at, $"shape {state + 1} of the markup written into \"{of.T.Name}\" changes the scene's structure ({_reshaped})"); return; }
                    plan.States[(double)state] = drawn;
                    moved.UnionWith(Moved(drawn));
                }
                Complete(plan.States.Values, moved);
                foreach (var slot in moved) if (!Claim(slot, $"the markup written into \"{of.T.Name}\"", at)) return;
                of.Plan = plan;
            }
        }

        /// <summary>
        /// The elements whose writes are worked out together, and how to show them: first everything shown at
        /// rest, then, per shape of markup not shown at rest, the elements it makes with that shape shown.
        /// </summary>
        private List<(HashSet<Target> Group, Action<bool>? Show)> Groups()
        {
            var groups = new List<(HashSet<Target>, Action<bool>?)>();
            var hidden = new HashSet<Target>();
            foreach (var of in _markupOf.Values)
            {
                if (of.Inline || of.Tops.Count < 2) continue;
                var display = of.Tops.SelectMany(x => x).Distinct().ToDictionary(x => x, x => x.Ve.style.display);
                for (var s = 1; s < of.Tops.Count; s++)
                {
                    if (!of.MadeIn.TryGetValue(s, out var made) || made.Count == 0) continue;
                    var state = s;
                    hidden.UnionWith(made);
                    groups.Add((new HashSet<Target>(made), on =>
                    {
                        for (var j = 0; j < of.Tops.Count; j++)
                            foreach (var top in of.Tops[j])
                                top.Ve.style.display = on ? (j == state ? StyleKeyword.Null : DisplayStyle.None) : display[top];
                    }));
                }
            }
            groups.Insert(0, (new HashSet<Target>(_order.Where(t => !hidden.Contains(t))), null));
            return groups;
        }

        /// <summary>The Lua of one markup write: its choices as the script makes them, and in each shape its state and values.</summary>
        private void EmitMarkup(JsToLua lua, MarkupWrite w)
        {
            var of = w.Of;
            var leaf = 0;
            Walk(new List<Mk>(), w.Template, 0);

            void Walk(List<Mk> done, List<Mk> rest, int i)
            {
                for (; i < rest.Count; i++)
                {
                    if (rest[i] is not MkAlt alt) { done.Add(rest[i]); continue; }
                    var tail = rest.Skip(i + 1).ToList();
                    lua.Emit("if js_truthy(" + lua.Translate(alt.Test) + ") then");
                    Walk(new List<Mk>(done), alt.Yes.Concat(tail).ToList(), 0);
                    lua.Emit("else");
                    Walk(new List<Mk>(done), alt.No.Concat(tail).ToList(), 0);
                    lua.Emit("end");
                    return;
                }
                var (state, ops) = w.Shapes[leaf++];
                if (of.Plan != null) lua.Emit("v_state(" + of.Plan.Var + ", " + state.ToString(CultureInfo.InvariantCulture) + ")");
                // the elements a write makes are new ones: what listened on the old ones is gone
                foreach (var made in of.Made.Where(m => m.Listens)) lua.Emit("v_unlisten(" + Q(made.Name) + ")");
                foreach (var op in ops)
                {
                    if (_lua.TryGetValue(op, out var own)) { lua.Emit(own(lua)); continue; }
                    foreach (var line in EmitOp(op, _ops[op][0], lua.Translate, early: true)) lua.Emit(line);
                }
            }
        }

        /// <summary>A value of markup, translated in its own scope; a name there as what it stands for.</summary>
        private string? MarkupExpression(JsToLua lua, Node e)
        {
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Acornima;
using Acornima.Ast;

namespace ScriptedScreensHtml;

internal static partial class PlainTranslator
{
    /// <summary>
    /// innerHTML, part 2: markup repeated over a list - `.map(…).join(…)`, a loop adding markup to a name or
    /// to the element - compiled once. The list is laid out at the most rows the script can give it, each
    /// row's values its own slots; when the markup is written the Lua counts the rows the array has, picks
    /// the state that shows that many (what follows moving up as a browser lays it out), and writes each
    /// shown row's values. No string is built and no table is made (CLAUDE.md, THE SPEC).
    /// </summary>
    private sealed partial class Page
    {
        /// <summary>
        /// A list of markup: its rows, laid out at <see cref="Max"/>, each row its template with its item (and
        /// index) bound. In the Lua it is one table, <see cref="Var"/>: the array (`a`), the rows shown (`n`),
        /// the first item (`o`), the item each row reads when a filter picks them (`i`), and per row whose
        /// shape changes with its item, what each shape draws (`w`).
        /// </summary>
        private sealed class MkRep : Mk
        {
            public int Id;
            public Node At = null!;
            /// <summary>The array the rows are read from, in the scope it is written in; null for a loop over a count.</summary>
            public Expression? Source;
            /// <summary>A loop over a count (`for (let i = 0; i &lt; n; i++)`): how many rows, and whether `&lt;=` counts one more.</summary>
            public Expression? Count;
            public bool Inclusive;
            /// <summary>What happens to the array before its rows are made: slices and filters, in order.</summary>
            public readonly List<Stage> Stages = new();
            public int Max;
            /// <summary>The fewest rows the list can have: a list over an array nothing changes always has them all.</summary>
            public int Min;
            /// <summary>What join puts between two rows: part of each row after the first.</summary>
            public string Sep = string.Empty;
            /// <summary>Per row: its markup, and that markup's shapes (one, unless it chooses with a ternary).</summary>
            public readonly List<List<Mk>> Rows = new();
            public readonly List<List<List<Mk>>> Copies = new();
            /// <summary>The item a filter is testing: in the Lua a local of the loop that counts the rows.</summary>
            public Identifier Item = null!;
            /// <summary>A row's index in the array the rows are made from, when a filter the rows themselves make (a ternary with nothing on one side) leaves gaps: kept per row.</summary>
            public bool Positions;
            /// <summary>Per row whose shape changes with its item: what each of its shapes draws (0 the row not there).</summary>
            public readonly Dictionary<int, StylePlan> RowPlans = new();
            /// <summary>A lookup lists these rows, so the count of rows shown is kept for that list's length.</summary>
            public bool Queried;
            /// <summary>
            /// Per row whose choices take their shapes each on its own (see <see cref="Gate"/>): each choice, and the copy
            /// key of the gate drawing each of its shapes (the first its shape at rest).
            /// </summary>
            public readonly Dictionary<int, List<(MkAlt Alt, List<int> Keys)>> Gates = new();
            /// <summary>Per gated row, its choices laid out together (one, or those moving a slot in common) and what each combination of their shapes draws.</summary>
            public readonly List<(int K, List<int> Members, StylePlan Plan)> GatePlans = new();
            public string Var => "V_R" + Id.ToString(CultureInfo.InvariantCulture);
            public bool Filtered => Stages.Any(s => s.Test != null);
            public bool Sliced => Stages.Count > 0;
        }

        /// <summary>One shape of a choice in a gated row, drawn in place as elements of its own and shown while the choice takes it.</summary>
        private sealed class MkGate : Mk
        {
            /// <summary>Its copy key in the row: 1 and up (0 is the row itself).</summary>
            public readonly int Key;
            public readonly List<Mk> Body;
            public MkGate(int key, List<Mk> body) { Key = key; Body = body; }
        }

        /// <summary>A slice (literal bounds) or a filter (its test, with its item and index bound) between the array and its rows.</summary>
        private sealed class Stage
        {
            public Expression? Test;
            public bool Negate;
            public Identifier? Index;
            public int From;
            public int? To;
        }

        private const int MostRows = 64;
        /// <summary>The most shapes a row is laid out in as one product of its choices, and the most any choices laid out together take.</summary>
        private const int MostRowShapes = 8;
        private const int MostCopies = 512;
        private const int MostChoices = 16;
        private const string RowTag = "ss-row", GateTag = "ss-gate";

        private readonly List<MkRep> _reps = new();
        /// <summary>The items a row's item node can be (null: any), for fixed-set questions about it.</summary>
        private readonly Dictionary<Identifier, List<(Expression Expr, Inlined? Env)>?> _itemOf = new();
        /// <summary>A name given markup in steps, inside a function inlined: the markup it holds by then.</summary>
        private readonly Dictionary<Identifier, List<Mk>> _templateOf = new();
        /// <summary>Statements that only build markup up in steps: the markup write does their work, so they emit nothing.</summary>
        private readonly HashSet<Node> _silenced = new();
        /// <summary>A markup write made in steps (`el.innerHTML = …` then `+=`), emitted after its last step.</summary>
        private readonly Dictionary<Node, MarkupWrite> _markupAt = new();
        /// <summary>Each element a list's row makes: its list, its row and which of the row's shapes.</summary>
        private readonly Dictionary<Target, (MkRep Rep, int K, int C)> _rowOf = new();
        private bool _slice, _rowsList;

        private static Inlined Copy(Inlined? env)
        {
            var e = new Inlined();
            if (env != null) foreach (var pair in env) e[pair.Key] = pair.Value;
            return e;
        }

        /// <summary>The function a call runs, when the compile sees which: one bound to a name for good, or one written in place and called there.</summary>
        private IFunction? Target(CallExpression call) => call.Callee is Identifier f ? Function(f) : Unwrap(call.Callee) as IFunction;

        /// <summary>A function written in place as it stands in its parent, past any parentheses around it.</summary>
        private Node Standing(IFunction fn)
        {
            Node n = (Node)fn;
            while (_parent.TryGetValue(n, out var up) && up is ParenthesizedExpression) n = up;
            return n;
        }

        /// <summary>Where a function is used: its name's references, or, written in place, where it stands.</summary>
        private List<Expression> UsesOf(IFunction fn)
            => NameOf(fn) is { } name ? (_refs.TryGetValue(name, out var refs) ? refs.Cast<Expression>().ToList() : new List<Expression>())
               : Standing(fn) is Expression e ? new List<Expression> { e } : new List<Expression>();

        /// <summary>Every call of a function, when every use of it is a call: null when it is handed on (a callback, a value).</summary>
        private List<CallExpression>? CallsOf(IFunction fn)
        {
            var calls = new List<CallExpression>();
            foreach (var use in UsesOf(fn))
                if (_parent.TryGetValue(use, out var p) && p is CallExpression c && c.Callee == use) calls.Add(c);
                else return null;
            return calls;
        }

        /// <summary>The `.map` call a function written in place is the callback of.</summary>
        private CallExpression? Mapped(IFunction fn)
        {
            var n = Standing(fn);
            return _parent.TryGetValue(n, out var p) && p is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "map" } } } c
                   && c.Arguments.Count > 0 && c.Arguments[0] == n ? c : null;
        }

        /// <summary>A function handed to a method walking a list item by item (`list.map(fn)`, find, filter, some, reduce…): the list, and which parameter an item comes in.</summary>
        private (Expression List, int Item)? WalkedAt(Expression use)
        {
            if (!_parent.TryGetValue(use, out var p) || p is not CallExpression c || c.Arguments.Count == 0 || c.Arguments[0] != use
                || c.Callee is not MemberExpression { Computed: false, Property: Identifier { Name: var m } } me) return null;
            return m switch
            {
                "map" or "forEach" or "filter" or "find" or "findIndex" or "findLast" or "findLastIndex" or "some" or "every" or "flatMap" => (me.Object, 0),
                "reduce" or "reduceRight" => (me.Object, 1),
                _ => null,
            };
        }

        /// <summary>The items a list can hold (each with the scope it is read in), for one picked from it; null when the compile cannot say.</summary>
        private List<(Expression, Inlined?)>? ItemsOf(Expression list, Inlined? bind)
            => Listed(list, bind) is { Max: not null, Items: { } items } ? items : null;

        /// <summary>An item picked from a list the compile knows: `list.find(…)`, `findLast`, or `list[i]` - the list, else null.</summary>
        private static Expression? Picked(Expression e)
            => Unwrap(e) switch
            {
                CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "find" or "findLast" } } m } => m.Object,
                MemberExpression { Computed: true, Property: not StringLiteral } ix => ix.Object,
                _ => null,
            };

        private static List<Statement>? Stmts(Node n) => n switch
        {
            FunctionBody fb => fb.Body.ToList(),
            BlockStatement b => b.Body.ToList(),
            Script s => s.Body.ToList(),
            Statement st => new List<Statement> { st },
            _ => null,
        };

        private IFunction? Callback(Node cb) => cb switch
        {
            ArrowFunctionExpression a => a,
            FunctionExpression f => f,
            Identifier id => Function(id),
            _ => null,
        };

        /// <summary>What a callback returns as one expression: its concise body, or a body that is one `return`.</summary>
        private static Expression? Returned(IFunction fn)
            => fn.Body is Expression e ? e
               : Stmts(fn.Body) is [ReturnStatement { Argument: { } r }] ? r : null;

        private MkRep NewRep(Node at)
        {
            var rep = new MkRep { Id = _reps.Count + 1, At = at };
            _reps.Add(rep);
            rep.Item = new Identifier(rep.Var + "x");
            var item = rep.Item;
            _lua[item] = _ => rep.Var + "x";
            return rep;
        }

        /// <summary>A node standing for a value the Lua holds under a name: a row's item, a filter's index.</summary>
        private Identifier Held(string lua, List<(Expression, Inlined?)>? items = null)
        {
            var id = new Identifier(lua.Replace(".", "_").Replace("[", "_").Replace("]", "_"));
            _lua[id] = _ => lua;
            _itemOf[id] = items;
            return id;
        }

        private static string ItemLua(MkRep rep, int k)
        {
            var at = k.ToString(CultureInfo.InvariantCulture);
            return rep.Filtered ? rep.Var + ".a[" + rep.Var + ".i[" + at + "]]"
                : rep.Sliced ? rep.Var + ".a[" + rep.Var + ".o + " + at + "]"
                : rep.Var + ".a[" + at + "]";
        }

        // ---- `.map(…).join(…)` -----------------------------------------------------------------------

        /// <summary>`array[.slice(…)|.filter(…)]*.map(callback).join(separator)`, as a list of markup.</summary>
        private List<Mk>? Mapped(CallExpression join, CallExpression map, Inlined? env, Node write, int depth)
        {
            string sep;
            if (join.Arguments.Count == 0) sep = ",";
            else if (join.Arguments[0] is Expression se and not SpreadElement && Finite(se, null, env) is [string s]) sep = s;
            else { Refuse(join, "a list joined with a separator only known at run time (not translated yet)"); return null; }
            if (map.Arguments.Count != 1 || Callback(map.Arguments[0]) is not { } fn)
            {
                Refuse(map, "a list whose .map callback the compile cannot follow (a function written there, or one named, is followed)");
                return null;
            }
            // the chain between the array and map: slices and filters
            var chain = new List<CallExpression>();
            var src = Unwrap(((MemberExpression)map.Callee).Object);
            while (src is CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "filter" or "slice" } } sm } sc)
            {
                chain.Insert(0, sc);
                src = Unwrap(sm.Object);
            }
            var rep = NewRep(map);
            rep.Source = Within(src, env, src);
            var listed = Listed(src, env);
            // an array the compile cannot bound is still a list of bounded length through a slice of fixed size
            var most = listed.Max ?? int.MaxValue;
            var least = listed.Min;
            foreach (var call in chain)
            {
                var name = ((Identifier)((MemberExpression)call.Callee).Property).Name;
                if (name == "slice")
                {
                    if (Bounds(call, env) is not { } b) return null;
                    if (rep.Filtered && (b.From < 0 || b.To is < 0))
                    {
                        Refuse(call, "a slice counted from the end of a filtered list (not translated yet)");
                        return null;
                    }
                    rep.Stages.Add(new Stage { From = b.From, To = b.To });
                    most = SliceLen(most, b.From, b.To);
                    least = SliceLen(least, b.From, b.To);
                    continue;
                }
                if (call.Arguments.Count != 1 || Filter(rep, call.Arguments[0], env, listed.Items, write) is not { } stage)
                {
                    Refuse(call, "a filter whose test the compile cannot follow (a function returning one expression, or Boolean, is followed)");
                    return null;
                }
                rep.Stages.Add(stage);
                least = 0;
            }
            if (most > MostRows)
            {
                Refuse(map, most == int.MaxValue || listed.Max == null ? "a list whose length the compile cannot bound: " + listed.Why : $"a list of up to {most} rows, more than the {MostRows} laid out");
                return null;
            }
            rep.Max = most;
            rep.Min = Math.Min(least, most);
            if (!Params(fn, map, out var item, out var index, out var fields)) return null;
            var exact = Exact(src, env, rep);
            var ok = FillRows(rep, (x, i) =>
            {
                var rowEnv = Copy(env);
                if (item != null) rowEnv[item] = (x, null);
                if (index != null) rowEnv[index] = (i, null);
                foreach (var (name, field) in fields) rowEnv[field] = (Made(new MemberExpression(x, new Identifier(name), false, false), map), null);
                return fn.Body is Expression body ? Tpl(body, rowEnv, write, depth + 1) : Steps(((FunctionBody)fn.Body).Body, 0, rowEnv, write, depth + 1, fn);
            }, exact ?? listed.Items, exact != null, index != null && Reads(fn, index), sep, write);
            return ok ? new List<Mk> { rep } : null;
        }

        /// <summary>A callback's item and index parameters; an object pattern of plain names is the item's fields.</summary>
        private bool Params(IFunction fn, Node at, out Identifier? item, out Identifier? index, out List<(string Name, Identifier Id)> fields)
        {
            item = index = null;
            fields = new List<(string, Identifier)>();
            for (var p = 0; p < fn.Params.Count; p++)
            {
                var param = fn.Params[p];
                if (p == 0 && param is ObjectPattern obj)
                {
                    foreach (var prop in obj.Properties)
                        if (prop is Property { Computed: false, Key: Identifier key, Value: Identifier value }) fields.Add((key.Name, value));
                        else { Refuse(param, "a list callback that unpacks its item with more than plain names (not translated yet)"); return false; }
                    continue;
                }
                if (param is not Identifier id)
                {
                    Refuse(param, "a list callback whose parameters are not plain names (not translated yet)");
                    return false;
                }
                if (p == 0) item = id;
                else if (p == 1) index = id;
                else if (Reads(fn, id)) { Refuse(param, "a list callback reading the whole array it walks (not translated yet)"); return false; }
            }
            return true;
        }

        /// <summary>Whether a function's body reads a name.</summary>
        private bool Reads(IFunction fn, Identifier decl)
            => Markup.Everything((Node)fn.Body).Any(x => x is Identifier i && i != decl && Reference(i) && Decl(i) == decl);

        private (int From, int? To)? Bounds(CallExpression slice, Inlined? env)
        {
            int? Int(int k)
                => k < slice.Arguments.Count && slice.Arguments[k] is Expression a and not SpreadElement && Finite(a, null, env) is [double d] && d == Math.Floor(d) && Math.Abs(d) < 1e6 ? (int)d : null;
            if (slice.Arguments.Count > 2 || slice.Arguments.Count >= 1 && Int(0) == null || slice.Arguments.Count == 2 && Int(1) == null)
            {
                Refuse(slice, "a slice whose bounds are not whole numbers the compile knows (not translated yet)");
                return null;
            }
            return (slice.Arguments.Count == 0 ? 0 : Int(0)!.Value, slice.Arguments.Count == 2 ? Int(1) : null);
        }

        /// <summary>How many items `slice(from, to)` leaves of an array of <paramref name="length"/> (int.MaxValue: any length), as Array.prototype.slice counts.</summary>
        private static int SliceLen(int length, int from, int? to)
        {
            long len = length;
            var a = from < 0 ? Math.Max(len + from, 0) : Math.Min(from, len);
            var b = to == null ? len : to < 0 ? Math.Max(len + to.Value, 0) : Math.Min(to.Value, len);
            return (int)Math.Min(int.MaxValue, Math.Max(0, b - a));
        }

        /// <summary>A filter stage: its callback's test, with its item bound to the item being counted and its index to the stage's own count.</summary>
        private Stage? Filter(MkRep rep, Node cb, Inlined? env, List<(Expression, Inlined?)>? items, Node write)
        {
            var stage = new Stage();
            if (cb is Identifier { Name: "Boolean" } b && Decl(b) == null)
            {
                stage.Test = rep.Item;
                return stage;
            }
            if (Callback(cb) is not { } fn || Returned(fn) is not { } test) return null;
            if (!Params(fn, cb, out var item, out var index, out var fields)) return null;
            _itemOf[rep.Item] = items;
            var filterEnv = Copy(env);
            if (item != null) filterEnv[item] = (rep.Item, null);
            foreach (var (name, field) in fields) filterEnv[field] = (Made(new MemberExpression(rep.Item, new Identifier(name), false, false), cb), null);
            if (index != null)
            {
                stage.Index = Held(rep.Var + "q" + (rep.Stages.Count + 1).ToString(CultureInfo.InvariantCulture));
                filterEnv[index] = (stage.Index, null);
            }
            if (!Readable(test, filterEnv, write)) return null;
            stage.Test = Within(test, filterEnv, test);
            return stage;
        }

        /// <summary>
        /// Each row's item exactly, when the array is one written in place that nothing changes and only slices of
        /// fixed bounds come between: row k is that array's item at the slice's start plus k.
        /// </summary>
        private List<(Expression, Inlined?)>? Exact(Expression src, Inlined? env, MkRep rep)
        {
            if (rep.Filtered || rep.Stages.Any(s => s.From < 0 || s.To is < 0)) return null;
            ArrayExpression? arr = src as ArrayExpression;
            if (arr == null && src is Identifier id && IsArrayTable(id) && ConstTable(id) != null && Decl(id) is { } d && Given(d) is [ArrayExpression a]) arr = a;
            if (arr == null || arr.Elements.Any(x => x is null or SpreadElement)) return null;
            var start = 0;
            foreach (var s in rep.Stages) start += s.From;
            return arr.Elements.Skip(start).Select(x => ((Expression)x!, (Inlined?)null)).ToList();
        }

        /// <summary>
        /// The rows: each row's markup built with its item and index bound. A row that is all one choice with nothing
        /// on one side (`x.on ? '&lt;li&gt;…' : ''`, `if (!x.on) continue;`) is a filter, as a browser draws it: the
        /// rows after it close up.
        /// </summary>
        /// <param name="exact">True when <paramref name="items"/> is each row's own item, in order; otherwise any row may be any of them.</param>
        private bool FillRows(MkRep rep, Func<Expression, Expression, List<Mk>?> build, List<(Expression, Inlined?)>? items, bool exact,
                              bool indexRead, string sep, Node write)
        {
            _itemOf[rep.Item] = items;
            rep.Sep = sep;
            var probeIndex = Held(rep.Var + "q" + (rep.Stages.Count + 1).ToString(CultureInfo.InvariantCulture));
            if (build(rep.Item, probeIndex) is not { } probe) return false;
            var filter = Choice(probe);
            if (filter != null)
            {
                exact = false;
                rep.Stages.Add(new Stage { Test = filter.Test, Negate = filter.Yes.Count == 0, Index = probeIndex });
                rep.Positions = indexRead;
                rep.Min = 0;
            }
            var total = 0;
            for (var k = 0; k < rep.Max; k++)
            {
                var row = k.ToString(CultureInfo.InvariantCulture);
                var itemNode = new Identifier(rep.Var + "_" + row);
                var r = rep;
                var kk = k;
                _lua[itemNode] = _ => ItemLua(r, kk);
                _itemOf[itemNode] = exact && items != null ? (k < items.Count ? new List<(Expression, Inlined?)> { items[k] } : null) : items;
                Expression indexNode = filter != null && indexRead
                    ? Held(rep.Var + ".m[" + row + "]")
                    : new NumericLiteral(k, row);
                if (build(itemNode, indexNode) is not { } tpl) return false;
                if (filter != null)
                {
                    if (Choice(tpl) is not { } again) { Refuse(write, "a list whose rows choose differently from each other what to show (not translated yet)"); return false; }
                    tpl = again.Yes.Count == 0 ? again.No : again.Yes;
                }
                // what the row's own item and index decide is no choice for that row
                else tpl = Fold(tpl);
                if (k > 0 && sep.Length > 0) tpl.Insert(0, new MkLit(sep));
                if (tpl.Any(m => m is MkRep) || Flatten(tpl).Any(m => m is MkRep))
                {
                    Refuse(write, "a list inside a row of another list (not translated yet)");
                    return false;
                }
                rep.Rows.Add(tpl);
                rep.Copies.Add(new List<List<Mk>>());
                if (!ShapeRow(rep, k, write)) return false;
                total += rep.Gates.TryGetValue(k, out var gated) ? gated.Sum(g => g.Keys.Count) : rep.Copies[k].Count;
                if (total > MostCopies)
                {
                    Refuse(write, $"a list with more than {MostCopies} rows and row shapes to lay out");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A row's shapes: every combination of its choices as one copy each, up to <see cref="MostRowShapes"/>; past
        /// that, each choice drawn in place with its shapes gated (<see cref="Gate"/>), so its shapes add up rather than multiply.
        /// </summary>
        private bool ShapeRow(MkRep rep, int k, Node write)
        {
            rep.Gates.Remove(k);
            var copies = Shapes(rep.Rows[k]);
            if (copies.Count <= MostRowShapes)
            {
                rep.Copies[k] = copies;
                return true;
            }
            if (Gate(rep, k, write) is not { } copy) return false;
            rep.Copies[k] = new List<List<Mk>> { copy };
            return true;
        }

        /// <summary>
        /// A row whose choices are too many to lay out every combination of: one copy, each of its choices (at the top
        /// of the row's markup) standing as every one of its shapes side by side, each in a gate - the elements of all
        /// but one hidden. Each choice is then a state of its own, and only choices that move the same things are laid
        /// out together (<see cref="GateFacets"/>).
        /// </summary>
        private List<Mk>? Gate(MkRep rep, int k, Node write)
        {
            var copy = new List<Mk>();
            var choices = new List<(MkAlt, List<int>)>();
            var key = 0;
            foreach (var m in rep.Rows[k])
            {
                if (m is not MkAlt alt) { copy.Add(m); continue; }
                var shapes = Shapes(new List<Mk> { alt });
                if (shapes.Count > MostRowShapes)
                {
                    Refuse(write, $"a list whose rows have a choice of more than {MostRowShapes} shapes");
                    return null;
                }
                var keys = new List<int>();
                foreach (var shape in shapes)
                {
                    keys.Add(++key);
                    copy.Add(new MkGate(key, shape));
                }
                choices.Add((alt, keys));
            }
            if (choices.Count > MostChoices)
            {
                Refuse(write, $"a list whose rows have more than {MostChoices} choices each");
                return null;
            }
            rep.Gates[k] = choices;
            return copy;
        }

        /// <summary>A row's markup with every choice the compile can decide for that row made: the side it takes, in its place.</summary>
        private List<Mk> Fold(List<Mk> tpl)
        {
            var result = new List<Mk>();
            foreach (var m in tpl)
            {
                if (m is not MkAlt alt) { result.Add(m); continue; }
                switch (Truth(alt.Test))
                {
                    case true: result.AddRange(Fold(alt.Yes)); break;
                    case false: result.AddRange(Fold(alt.No)); break;
                    default: result.Add(new MkAlt(alt.Test, Fold(alt.Yes), Fold(alt.No))); break;
                }
            }
            return result;
        }

        /// <summary>
        /// A test's truth when every run gives the same one: a value from a fixed set all truthy or all falsy, a
        /// comparison of fixed sets, `!`, `&amp;&amp;` and `||` of those. Null when it can go either way, or the compile cannot say.
        /// </summary>
        private bool? Truth(Expression e, Inlined? bind = null)
        {
            switch (e)
            {
                case ParenthesizedExpression w when _bindOf.TryGetValue(w, out var within): return Truth(w.Expression, within);
                case ParenthesizedExpression p: return Truth(p.Expression, bind);
                case NonUpdateUnaryExpression { Operator: Operator.LogicalNot } not: return Truth(not.Argument, bind) is { } t ? !t : null;
                case LogicalExpression { Operator: Operator.LogicalAnd } and:
                    return Truth(and.Left, bind) switch { false => false, true => Truth(and.Right, bind), _ => Truth(and.Right, bind) == false ? false : null };
                case LogicalExpression { Operator: Operator.LogicalOr } or:
                    return Truth(or.Left, bind) switch { true => true, false => Truth(or.Right, bind), _ => Truth(or.Right, bind) == true ? true : null };
                case NonLogicalBinaryExpression { Operator: Operator.StrictEquality or Operator.StrictInequality or Operator.LessThan or Operator.LessThanOrEqual
                                                  or Operator.GreaterThan or Operator.GreaterThanOrEqual } b:
                    {
                        if (Finite(b.Left, null, bind) is not { Count: > 0 } left || Finite(b.Right, null, bind) is not { Count: > 0 } right) return null;
                        bool? all = null;
                        foreach (var x in left)
                            foreach (var y in right)
                            {
                                bool one;
                                if (x is not (double or string) || y is not (double or string)) return null;
                                if (b.Operator is Operator.StrictEquality or Operator.StrictInequality)
                                    one = (x is double dx && double.IsNaN(dx) ? false : x.Equals(y)) == (b.Operator == Operator.StrictEquality);
                                else if (x is double nx && y is double ny)
                                    one = b.Operator switch { Operator.LessThan => nx < ny, Operator.LessThanOrEqual => nx <= ny, Operator.GreaterThan => nx > ny, _ => nx >= ny };
                                else return null;
                                if (all != null && all != one) return null;
                                all = one;
                            }
                        return all;
                    }
                default:
                    {
                        if (Finite(e, null, bind) is not { Count: > 0 } set || set.Any(v => v is not (double or string))) return null;
                        static bool Truthy(object v) => v is double d ? d != 0 && !double.IsNaN(d) : ((string)v).Length > 0;
                        return set.All(Truthy) ? true : set.All(v => !Truthy(v)) ? false : null;
                    }
            }
        }

        private static IEnumerable<Mk> Flatten(List<Mk> tpl)
        {
            foreach (var m in tpl)
            {
                yield return m;
                if (m is MkAlt a)
                {
                    foreach (var y in Flatten(a.Yes)) yield return y;
                    foreach (var n in Flatten(a.No)) yield return n;
                }
            }
        }

        /// <summary>A row's markup that is one choice with nothing (or white space) on one side.</summary>
        private static MkAlt? Choice(List<Mk> tpl)
        {
            var parts = tpl.Where(m => !(m is MkLit l && l.Text.Trim().Length == 0)).ToList();
            if (parts is not [MkAlt alt]) return null;
            static bool Empty(List<Mk> side) => side.All(m => m is MkLit l && l.Text.Trim().Length == 0);
            if (Empty(alt.Yes) == Empty(alt.No)) return null;
            return Empty(alt.Yes) ? new MkAlt(alt.Test, new List<Mk>(), alt.No) : new MkAlt(alt.Test, alt.Yes, new List<Mk>());
        }

        // ---- markup built up in steps ----------------------------------------------------------------

        /// <summary>What a statement adds to a name holding markup: `h += x`, `h = h + x + y`; null for anything else.</summary>
        private Func<Node, List<Expression>?> AppendTo(Identifier decl) => n =>
        {
            if (n is ExpressionStatement es) n = es.Expression;
            switch (n)
            {
                case AssignmentExpression { Operator: Operator.AdditionAssignment, Left: Identifier l } a when Decl(l) == decl:
                    return new List<Expression> { a.Right };
                case AssignmentExpression { Operator: Operator.Assignment, Left: Identifier l, Right: NonLogicalBinaryExpression { Operator: Operator.Addition } } a when Decl(l) == decl:
                    {
                        var chain = new List<Expression>();
                        Expression at = a.Right;
                        while (at is NonLogicalBinaryExpression { Operator: Operator.Addition } b) { chain.Insert(0, b.Right); at = b.Left; }
                        return at is Identifier h && Decl(h) == decl ? chain : null;
                    }
            }
            return null;
        };

        /// <summary>What a statement adds to one element's markup: `el.innerHTML += x`.</summary>
        private Func<Node, List<Expression>?> AppendToElement(Target t, List<AssignmentExpression> seen) => n =>
        {
            if (n is ExpressionStatement es) n = es.Expression;
            if (n is AssignmentExpression { Operator: Operator.AdditionAssignment, Left: MemberExpression { Computed: false, Property: Identifier { Name: "innerHTML" } } m } a
                && Elems(m.Object) is { One: true } els && els.Ts[0] == t && Lookupish(m.Object))
            {
                if (!seen.Contains(a)) seen.Add(a);
                return new List<Expression> { a.Right };
            }
            return null;
        };

        private static bool Appending(Node s, Func<Node, List<Expression>?> append) => Markup.Everything(s).Any(n => append(n) != null);

        /// <summary>
        /// Statements from <paramref name="from"/> on that add markup: appends, loops and ifs of them, and declarations
        /// between them (bound, when <paramref name="bind"/>; left to run as they stand otherwise). Stops at the first
        /// statement that is none of these; <paramref name="consumed"/> gets every statement that adds markup.
        /// </summary>
        private List<Mk>? Sequence(List<Statement> list, int from, Func<Node, List<Expression>?> append, Inlined? env, Node write, int depth,
                                  bool bindDecls, out int end, List<Statement> consumed)
        {
            var result = new List<Mk>();
            for (end = from; end < list.Count; end++)
            {
                var s = list[end];
                if (append(s) is { } parts)
                {
                    foreach (var part in parts)
                    {
                        if (Tpl(part, env, write, depth + 1) is not { } piece) return null;
                        result.AddRange(piece);
                    }
                    consumed.Add(s);
                    continue;
                }
                if (Appending(s, append))
                {
                    if (s is IfStatement ifs)
                    {
                        if (!Readable(ifs.Test, env, write)) return null;
                        var yes = Branch(ifs.Consequent, append, env, write, depth);
                        var no = ifs.Alternate == null ? new List<Mk>() : Branch(ifs.Alternate, append, env, write, depth);
                        if (yes == null || no == null) return null;
                        result.Add(new MkAlt(Within(ifs.Test, env, ifs.Test), yes, no));
                        consumed.Add(s);
                        continue;
                    }
                    if (Loop(s, append, env, write, depth) is not { } rep) return null;
                    result.Add(rep);
                    consumed.Add(s);
                    continue;
                }
                if (s is VariableDeclaration vd && vd.Declarations.All(d => d.Id is Identifier di && d.Init != null && !_writes.ContainsKey(di)))
                {
                    if (bindDecls && env != null) foreach (var d in vd.Declarations) env[(Identifier)d.Id] = (d.Init!, env);
                    continue;
                }
                break;
            }
            return result;

            List<Mk>? Branch(Statement b, Func<Node, List<Expression>?> add, Inlined? e, Node w, int d)
            {
                var body = Stmts(b)!;
                var inner = Sequence(body, 0, add, e == null ? null : Copy(e), w, d + 1, bindDecls, out var stop, new List<Statement>());
                if (inner != null && stop < body.Count) { Refuse(body[stop], "markup built up in steps with other work in between (not translated yet)"); return null; }
                return inner;
            }
        }

        /// <summary>
        /// A loop adding markup: `for (const x of list)`, `for (let i = 0; i &lt; list.length; i++)` or over a count,
        /// `list.forEach((x, i) => …)`. Its body, per row: appends, declarations (bound per row), ifs of those, and
        /// `continue`.
        /// </summary>
        private MkRep? Loop(Statement s, Func<Node, List<Expression>?> append, Inlined? env, Node write, int depth)
        {
            Expression? src = null, count = null;
            var inclusive = false;
            Identifier? item = null, index = null;
            List<(string Name, Identifier Id)> fields = new();
            Node body;
            switch (s)
            {
                case ForOfStatement { Left: VariableDeclaration { Declarations: [{ Id: Identifier x }] } } fo:
                    src = fo.Right; item = x; body = fo.Body;
                    break;
                case ForStatement { Init: VariableDeclaration { Declarations: [{ Id: Identifier i, Init: NumericLiteral { Value: 0 } }] },
                                    Test: NonLogicalBinaryExpression { Operator: Operator.LessThan or Operator.LessThanOrEqual, Left: Identifier ti } test } f
                    when Decl(ti) == i && _writes.TryGetValue(i, out var iw) && iw.Count == 1 && iw[0] == f.Update
                         && f.Update is UpdateExpression { Operator: Operator.Increment } or AssignmentExpression { Operator: Operator.AdditionAssignment, Right: NumericLiteral { Value: 1 } }:
                    index = i;
                    inclusive = test.Operator == Operator.LessThanOrEqual;
                    if (!inclusive && test.Right is MemberExpression { Computed: false, Property: Identifier { Name: "length" } } lm) src = lm.Object;
                    else count = test.Right;
                    body = f.Body;
                    break;
                case ExpressionStatement { Expression: CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "forEach" } } fm } fc }
                    when fc.Arguments.Count == 1 && Callback(fc.Arguments[0]) is { } fn:
                    if (!Params(fn, fc, out item, out index, out fields)) return null;
                    src = fm.Object;
                    body = (Node)fn.Body;
                    break;
                default:
                    Refuse(s, "a loop adding markup that the compile does not follow (for...of, a for counting up from 0 by 1, and forEach are followed)");
                    return null;
            }
            var rep = NewRep(s);
            List<(Expression, Inlined?)>? items = null;
            if (src != null)
            {
                rep.Source = Within(src, env, src);
                var listed = Listed(src, env);
                if (listed.Max is not { } most) { Refuse(s, "a list whose length the compile cannot bound: " + listed.Why); return null; }
                rep.Max = most;
                rep.Min = Math.Min(listed.Min, most);
                items = listed.Items;
            }
            else
            {
                rep.Count = Within(count!, env, count!);
                rep.Inclusive = inclusive;
                if (Finite(count!, null, env) is not { Count: > 0 } set || set.Any(v => v is not double))
                {
                    Refuse(count!, "a loop over a count only known at run time (the list is laid out at the largest count, so it has to be one of a fixed set)");
                    return null;
                }
                int Rows(double v) => Math.Max(0, inclusive ? (int)Math.Floor(v) + 1 : (int)Math.Ceiling(v));
                rep.Max = Rows(set.Cast<double>().Max());
                rep.Min = Rows(set.Cast<double>().Min());
            }
            if (rep.Max > MostRows) { Refuse(s, $"a list of up to {rep.Max} rows, more than the {MostRows} laid out"); return null; }
            var statements = body is Expression ? null : Stmts(body);
            var readIndex = index != null && Markup.Everything(body).Any(x => x is Identifier r && r != index && Reference(r) && Decl(r) == index);
            var exact = src != null ? Exact(Unwrap(src), env, rep) : null;
            var ok = FillRows(rep, (x, i) =>
            {
                var rowEnv = Copy(env);
                if (item != null) rowEnv[item] = (x, null);
                if (index != null) rowEnv[index] = (i, null);
                foreach (var (name, field) in fields) rowEnv[field] = (Made(new MemberExpression(x, new Identifier(name), false, false), s), null);
                if (statements == null)
                {
                    if (append(body) is not { } parts) { Refuse(body, "a loop whose body does something other than add markup (not translated yet)"); return null; }
                    var row = new List<Mk>();
                    foreach (var part in parts)
                    {
                        if (Tpl(part, rowEnv, write, depth + 1) is not { } piece) return null;
                        row.AddRange(piece);
                    }
                    return row;
                }
                return RowBody(statements, 0, append, rowEnv, write, depth + 1);
            }, exact ?? items, exact != null, readIndex, string.Empty, write);
            return ok ? rep : null;
        }

        /// <summary>One row of a loop's body: appends, declarations bound for the row, ifs of those, and `continue`.</summary>
        private List<Mk>? RowBody(List<Statement> body, int from, Func<Node, List<Expression>?> append, Inlined env, Node write, int depth)
        {
            var result = new List<Mk>();
            for (var i = from; i < body.Count; i++)
            {
                var s = body[i];
                if (append(s) is { } parts)
                {
                    foreach (var part in parts)
                    {
                        if (Tpl(part, env, write, depth + 1) is not { } piece) return null;
                        result.AddRange(piece);
                    }
                    continue;
                }
                switch (s)
                {
                    case EmptyStatement:
                        continue;
                    case ContinueStatement { Label: null }:
                        return result;
                    case VariableDeclaration vd when vd.Declarations.All(d => d.Id is Identifier di && d.Init != null && !_writes.ContainsKey(di)):
                        foreach (var d in vd.Declarations) env[(Identifier)d.Id] = (d.Init!, Copy(env));
                        continue;
                    case IfStatement ifs:
                        {
                            if (!Readable(ifs.Test, env, write)) return null;
                            // a branch that ends in `continue` skips the rest of the body; the other goes on with it
                            var yesStmts = Stmts(ifs.Consequent)!;
                            var noStmts = ifs.Alternate == null ? new List<Statement>() : Stmts(ifs.Alternate)!;
                            var yesStops = yesStmts.Count > 0 && yesStmts[^1] is ContinueStatement { Label: null };
                            var noStops = noStmts.Count > 0 && noStmts[^1] is ContinueStatement { Label: null };
                            var rest = body.Skip(i + 1).ToList();
                            var yes = RowBody(yesStops ? yesStmts : yesStmts.Concat(rest).ToList(), 0, append, Copy(env), write, depth + 1);
                            var no = RowBody(noStops ? noStmts : noStmts.Concat(rest).ToList(), 0, append, Copy(env), write, depth + 1);
                            if (yes == null || no == null) return null;
                            result.Add(new MkAlt(Within(ifs.Test, env, ifs.Test), yes, no));
                            return result;
                        }
                }
                Refuse(s, "a loop adding markup that also does other work (appends, declarations, if and continue are followed)");
                return null;
            }
            return result;
        }

        /// <summary>
        /// `let h = …;` then steps adding markup to h, then h read by the markup write, in one block: the markup h
        /// holds by then. The steps emit nothing; the write does their work.
        /// </summary>
        private List<Mk>? Accumulated(Identifier decl, Identifier read, Node write, int depth)
        {
            const string How = "markup built up in steps in a way the compile does not follow (a name given markup, then `+=` and loops adding to it, then written, all in one block, are followed)";
            if (!_parent.TryGetValue(decl, out var p) || p is not VariableDeclarator { Init: { } init } vd || vd.Id != decl
                || !_parent.TryGetValue(vd, out var dn) || dn is not VariableDeclaration { Declarations.Count: 1 } vdecl
                || !_parent.TryGetValue(vdecl, out var block) || Stmts(block) is not { } list)
            {
                Refuse(read, How);
                return null;
            }
            var d = list.IndexOf(vdecl);
            var w = -1;
            for (Node n = read; _parent.TryGetValue(n, out var up); n = up)
                if (up == block) { w = list.IndexOf((Statement)n); break; }
            if (d < 0 || w <= d) { Refuse(read, How); return null; }
            var consumed = new List<Statement>();
            if (Tpl(init, null, write, depth + 1) is not { } head) return null;
            if (Sequence(list, d + 1, AppendTo(decl), null, write, depth, bindDecls: false, out var end, consumed) is not { } steps) return null;
            if (end != w) { Refuse(list[end], How + "; this comes between"); return null; }
            // every change of the name, and every read of it, is one of those steps or the write
            var inside = new HashSet<Node>(consumed.SelectMany(Markup.Everything));
            inside.UnionWith(Markup.Everything(list[w]));
            if (_writes.TryGetValue(decl, out var ws) && ws.Any(x => !inside.Contains(x)) || _refs.TryGetValue(decl, out var rs) && rs.Any(x => !inside.Contains(x)))
            {
                Refuse(read, How + "; it is also changed or read elsewhere");
                return null;
            }
            _silenced.Add(vdecl);
            foreach (var s in consumed) _silenced.Add(s);
            return head.Concat(steps).ToList();
        }

        // ---- how long a list can be ---------------------------------------------------------------------

        /// <summary>The most items an array can hold, and the values those items can be (null: any), or why the compile cannot say.</summary>
        private sealed class Lst
        {
            public int? Max;
            /// <summary>The fewest items it can hold.</summary>
            public int Min;
            public List<(Expression, Inlined?)>? Items = new();
            public string Why = string.Empty;
            /// <summary>The array's own value met again while it is worked out: what it already holds, adding nothing.</summary>
            public bool Again;
            public static Lst Unknown(string why) => new() { Why = why, Items = null };
        }

        /// <summary>
        /// How long an array can be, as the source shows it: a literal, a name every value and every change of which
        /// is bounded - pushes capped by a length test before or a trim after, or made in loops over bounded lists
        /// into an array made fresh each time - a function's return, slices of fixed bounds, filters, concat.
        /// </summary>
        private Lst Listed(Expression e, Inlined? bind, int depth = 0, HashSet<Node>? seen = null)
        {
            if (depth > 16) return Lst.Unknown("it is followed through more steps than the compile follows");
            switch (e)
            {
                case ParenthesizedExpression w when _bindOf.TryGetValue(w, out var within):
                    return Listed(w.Expression, within, depth + 1, seen);
                case ParenthesizedExpression p:
                    return Listed(p.Expression, bind, depth + 1, seen);
                case ArrayExpression a:
                    {
                        var l = new Lst { Max = 0 };
                        var grows = false;
                        foreach (var x in a.Elements)
                        {
                            if (x is SpreadElement sp)
                            {
                                var inner = Listed(sp.Argument, bind, depth + 1, seen);
                                // its own items again: no bound of its own, but the items it can hold are known (a slice of fixed size bounds it)
                                if (inner.Again) { grows = true; l.Items = Union(l.Items, inner.Items); continue; }
                                if (inner.Max == null) return inner;
                                l.Max += inner.Max;
                                l.Min += inner.Min;
                                l.Items = Union(l.Items, inner.Items);
                                continue;
                            }
                            l.Max++;
                            l.Min++;
                            l.Items = x is Expression xe ? Union(l.Items, new() { (xe, bind) }) : null;
                        }
                        return grows ? new Lst { Why = "it is spread into itself, and so grows each time", Items = l.Items } : l;
                    }
                case Identifier row when _itemOf.ContainsKey(row):
                    return Lst.Unknown("a list inside a row of another list (not translated yet)");
                case Identifier id:
                    {
                        if (Decl(id) is not { } decl) return Lst.Unknown($"\"{id.Name}\" is not declared by the script");
                        if (bind != null && bind.TryGetValue(decl, out var arg)) return Listed(arg.Expr, arg.Env, depth + 1, seen);
                        seen ??= new HashSet<Node>();
                        if (!seen.Add(decl)) return new Lst { Again = true, Max = 0 };
                        try { return Variable(decl, depth, seen); }
                        finally { seen.Remove(decl); }
                    }
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier p } m } c:
                    {
                        var inner = Listed(m.Object, bind, depth + 1, seen);
                        switch (p.Name)
                        {
                            case "slice":
                                {
                                    int? Int(int k) => k < c.Arguments.Count && c.Arguments[k] is Expression x and not SpreadElement && Finite(x, null, bind) is [double d] && d == Math.Floor(d) ? (int)d : null;
                                    var from = c.Arguments.Count == 0 ? 0 : Int(0);
                                    var to = c.Arguments.Count < 2 ? null : Int(1);
                                    if (from == null || c.Arguments.Count == 2 && to == null) return Lst.Unknown("a slice whose bounds are only known at run time");
                                    // a slice of fixed size is bounded whatever it is taken from
                                    if (from >= 0 && to is >= 0 || to == null && from < 0)
                                        return new Lst { Max = SliceLen(inner.Max ?? int.MaxValue, from.Value, to), Min = SliceLen(inner.Min, from.Value, to), Items = inner.Items };
                                    if (inner.Again) return new Lst { Again = true, Max = 0, Items = inner.Items };
                                    return inner.Max == null ? inner : new Lst { Max = SliceLen(inner.Max.Value, from.Value, to), Min = SliceLen(inner.Min, from.Value, to), Items = inner.Items };
                                }
                            case "filter":
                                return new Lst { Max = inner.Max, Min = 0, Items = inner.Items, Why = inner.Why, Again = inner.Again };
                            case "sort" or "reverse" or "toSorted" or "toReversed":
                                return inner;
                            case "map":
                                {
                                    if (inner.Max == null || inner.Again) return inner;
                                    // each item what the callback gives back for an item of the list, that item bound to its parameter
                                    List<(Expression, Inlined?)>? mapped = null;
                                    if (inner.Items != null && c.Arguments.Count == 1 && Callback(c.Arguments[0]) is { Params.Count: > 0 } mfn && mfn.Params[0] is Identifier mp
                                        && Returns(mfn) is var rs && rs.All(r => r != null))
                                    {
                                        mapped = new();
                                        foreach (var (x, xe) in inner.Items)
                                            foreach (var r in rs)
                                            {
                                                var env = new Inlined { [mp] = (x, xe) };
                                                mapped.Add((r!, env));
                                            }
                                    }
                                    return new Lst { Max = inner.Max, Min = inner.Min, Items = mapped };
                                }
                            case "concat":
                                {
                                    // its own items again: no bound of its own, but the items it can hold are known (a slice of fixed size bounds it)
                                    var grows = inner.Again;
                                    if (inner.Max == null && !grows) return inner;
                                    var l = new Lst { Max = inner.Max, Min = inner.Min, Items = inner.Items };
                                    foreach (var arg in c.Arguments)
                                    {
                                        if (arg is not Expression ae || ae is SpreadElement) return Lst.Unknown("concat of a spread");
                                        if (ae is ArrayExpression or Identifier or CallExpression or MemberExpression { Computed: false })
                                        {
                                            var more = Listed(ae, bind, depth + 1, seen);
                                            if (more.Again) { grows = true; l.Items = Union(l.Items, more.Items); continue; }
                                            if (more.Max == null) return more;
                                            l.Max += more.Max;
                                            l.Min += more.Min;
                                            l.Items = Union(l.Items, more.Items);
                                        }
                                        else { l.Max++; l.Min++; l.Items = Union(l.Items, new() { (ae, bind) }); }
                                    }
                                    return grows ? new Lst { Why = "it is concatenated onto itself, and so grows each time", Items = l.Items } : l;
                                }
                        }
                        return Lst.Unknown($"the array .{p.Name}() gives");
                    }
                case CallExpression call when Target(call) is { } fn:
                    {
                        var inner = Inlined.Of(fn, call, bind);
                        var l = new Lst { Max = 0, Min = int.MaxValue };
                        foreach (var r in Returns(fn))
                        {
                            if (r == null) { l.Min = 0; continue; }
                            var one = Listed(r, inner, depth + 1, seen);
                            if (one.Max == null) return one;
                            l.Max = Math.Max(l.Max.Value, one.Max.Value);
                            l.Min = Math.Min(l.Min, one.Min);
                            l.Items = Union(l.Items, one.Items);
                        }
                        if (l.Min == int.MaxValue) l.Min = 0;
                        return l;
                    }
                case MemberExpression me when Markup.Everything(me).Any(x => x is Identifier i && _itemOf.ContainsKey(i)):
                    return Lst.Unknown("a list inside a row of another list (not translated yet)");
                // a field of an object literal: every array it is given and every change made to it, as for a name
                case MemberExpression { Computed: false, Property: Identifier field } fm:
                    {
                        if (Objects(fm.Object, bind, 0) is not { Count: > 0 } objects) return Lst.Unknown($"a list read from the field \"{field.Name}\" of an object the compile cannot follow (not translated yet)");
                        var l = new Lst { Max = 0, Min = int.MaxValue };
                        foreach (var (o, ob) in objects)
                        {
                            if (Field(o, field.Name) is not { } value) return Lst.Unknown($"a list read from the field \"{field.Name}\", which an object it can be does not have");
                            var one = FieldList(o, field.Name, value, ob, depth, seen ??= new HashSet<Node>());
                            // the field's own value met again while it is worked out: what it already holds, adding nothing
                            if (one.Again) { if (objects.Count == 1) return one; return Lst.Unknown($"a list read from the field \"{field.Name}\" of one of several objects, itself among them"); }
                            if (one.Max == null) return one;
                            l.Max = Math.Max(l.Max.Value, one.Max.Value);
                            l.Min = Math.Min(l.Min, one.Min);
                            l.Items = Union(l.Items, one.Items);
                        }
                        return l;
                    }
                // `given || ['a', 'b']`: the default when there is nothing, else either
                case LogicalExpression { Operator: Operator.LogicalOr or Operator.NullishCoalescing } lo:
                    {
                        if (Nullish(lo.Left, bind)) return Listed(lo.Right, bind, depth + 1, seen);
                        var a = Listed(lo.Left, bind, depth + 1, seen);
                        var b = Listed(lo.Right, bind, depth + 1, seen);
                        if (a.Max == null) return a;
                        if (b.Max == null) return b;
                        return new Lst { Max = Math.Max(a.Max.Value, b.Max.Value), Min = Math.Min(a.Min, b.Min), Items = Union(a.Items, b.Items) };
                    }
                case ConditionalExpression ce:
                    {
                        var a = Listed(ce.Consequent, bind, depth + 1, seen);
                        var b = Listed(ce.Alternate, bind, depth + 1, seen);
                        if (a.Max == null) return a;
                        if (b.Max == null) return b;
                        return new Lst { Max = Math.Max(a.Max.Value, b.Max.Value), Min = Math.Min(a.Min, b.Min), Items = Union(a.Items, b.Items) };
                    }
                case MemberExpression:
                    return Lst.Unknown("a list read from a field of an object (not translated yet)");
                default:
                    return Lst.Unknown("it is not an array the compile can follow");
            }

            static List<(Expression, Inlined?)>? Union(List<(Expression, Inlined?)>? a, List<(Expression, Inlined?)>? b)
                => a == null || b == null ? null : a.Concat(b).ToList();
        }

        /// <summary>Where an array is held: a declared name, or a field of object literals; each reference is a place it is read or changed.</summary>
        private sealed class Holder
        {
            public readonly string Name;
            public readonly Identifier? Decl;
            public readonly HashSet<Expression> Refs;
            public Holder(string name, Identifier? decl, IEnumerable<Expression> refs) { Name = name; Decl = decl; Refs = new HashSet<Expression>(refs); }
        }

        private Holder HolderOf(Identifier decl)
            => new(decl.Name, decl, _refs.TryGetValue(decl, out var refs) ? refs : Enumerable.Empty<Expression>());

        /// <summary>Whether an expression is the array a holder holds, where it is read.</summary>
        private bool Is(Holder h, Expression e) => h.Decl != null ? e is Identifier i && Decl(i) == h.Decl : h.Refs.Contains(e);

        /// <summary>A declared name's array: a parameter's arguments, or every value it is given with every change made to it.</summary>
        private Lst Variable(Identifier decl, int depth, HashSet<Node> seen)
        {
            var name = decl.Name;
            var h = HolderOf(decl);
            if (ParamOf(decl) is { } param)
            {
                if (NameOf(param.Fn) is not { } fname || !_refs.TryGetValue(fname, out var calls))
                    return Lst.Unknown($"\"{name}\" is a parameter of a function the compile cannot see called");
                var all = new Lst { Max = 0, Min = int.MaxValue };
                foreach (var c in calls)
                {
                    if (_parent[c] is not CallExpression call || call.Callee != c) return Lst.Unknown($"\"{name}\" is a parameter of a function passed around rather than called");
                    var one = param.Index < call.Arguments.Count && call.Arguments[param.Index] is Expression a and not SpreadElement
                        ? Listed(a, null, depth + 1, seen) : new Lst { Max = 0 };
                    if (one.Max == null) return one;
                    all.Max = Math.Max(all.Max!.Value, one.Max!.Value);
                    all.Min = Math.Min(all.Min, one.Min);
                    all.Items = all.Items == null || one.Items == null ? null : all.Items.Concat(one.Items).ToList();
                }
                if (Changes(h, depth, seen) is { } changed && (changed.Max != 0 || changed.Why.Length > 0))
                    return Lst.Unknown(changed.Why.Length > 0 ? changed.Why : $"\"{name}\" is a parameter the function adds to");
                if (all.Min == int.MaxValue || Shrunk(h)) all.Min = 0;
                return all;
            }
            if (Given(decl) is not { } given) return Lst.Unknown($"\"{name}\" is changed in a way the compile cannot follow");
            return Kept(h, given.Select(v => (v, (Inlined?)null, (ObjectExpression?)null)).ToList(), depth, seen);
        }

        /// <summary>
        /// A field of an object literal's array: the value it is written with, every `x.field = …` of it and every
        /// object `Object.assign` copies it from (that object's field, followed the same way), with every change made
        /// to it - wherever the object goes, which has to be where the compile can see every use of it.
        /// </summary>
        private Lst FieldList(ObjectExpression o, string field, Expression value, Inlined? bind, int depth, HashSet<Node> seen)
        {
            // the field as written: its value's node, one per object and field
            if (!seen.Add(value)) return new Lst { Again = true, Max = 0 };
            try
            {
                if (FieldUses(field, o) is not { } flow)
                    return Lst.Unknown($"a list read from the field \"{field}\" of an object handed where the compile cannot follow what is done to it");
                var given = new List<(Expression?, Inlined?, ObjectExpression?)> { (value, bind, null) };
                foreach (var u in flow.Uses)
                {
                    if (!WrittenTo(u)) continue;
                    if (_parent[u] is AssignmentExpression { Operator: Operator.Assignment } a && a.Left == u && !u.Computed) given.Add((a.Right, null, null));
                    else return Lst.Unknown($"a list read from the field \"{field}\", which is changed in a way the compile cannot follow");
                }
                foreach (var (from, v, env) in flow.Assigned) given.Add((v, env, from));
                return Kept(new Holder(field, null, flow.Uses), given, depth, seen);
            }
            finally { seen.Remove(value); }
        }

        /// <summary>
        /// An array from every value it is given (null: none, undefined; with <c>From</c>, that object literal's field of
        /// the same name, copied by `Object.assign`) and every change made to it where it is held.
        /// </summary>
        private Lst Kept(Holder h, List<(Expression? Expr, Inlined? Env, ObjectExpression? From)> given, int depth, HashSet<Node> seen)
        {
            var l = new Lst { Max = 0, Min = int.MaxValue };
            foreach (var (v, env, from) in given)
            {
                if (v == null) { l.Min = 0; continue; }
                var one = from != null ? FieldList(from, h.Name, v, env, depth + 1, seen) : Listed(v, env, depth + 1, seen);
                if (one.Again) { l.Items = one.Items == null ? null : l.Items; continue; }
                if (one.Max == null) return Lst.Unknown($"\"{h.Name}\" is given a value whose length the compile cannot bound ({one.Why})");
                l.Max = Math.Max(l.Max!.Value, one.Max.Value);
                l.Min = Math.Min(l.Min, one.Min);
                l.Items = l.Items == null || one.Items == null ? null : l.Items.Concat(one.Items).ToList();
            }
            // taken from, or changed at all: it can hold fewer
            if (l.Min == int.MaxValue || Shrunk(h)) l.Min = 0;
            var grown = Changes(h, depth, seen);
            if (grown == null) return l;
            if (grown.Why.Length > 0) return Lst.Unknown(grown.Why);
            // grown.Max is what the changes add; Again marks a cap: the array never holds more than it
            l.Max = grown.Again ? Math.Max(l.Max!.Value, grown.Max!.Value) : l.Max + grown.Max;
            l.Items = l.Items == null || grown.Items == null ? null : l.Items.Concat(grown.Items).ToList();
            return l;
        }

        /// <summary>
        /// Where an array read at <paramref name="r"/> goes on to, still the same array: a parameter of a function every
        /// use of which is a call, or a field of an object literal it is written into (null there: the object goes where
        /// the compile cannot follow it). Null for anywhere else.
        /// </summary>
        private (Holder? To, Node Key, string What)? HandedOn(Expression r)
        {
            switch (_parent[r])
            {
                case CallExpression call when call.Arguments.Contains(r) && call.Callee is Identifier f && Function(f) is { } fn && NameOf(fn) is { } fname
                                              && _refs.TryGetValue(fname, out var fr) && fr.All(x => _parent[x] is CallExpression cc && cc.Callee == x):
                    {
                        var index = call.Arguments.ToList().IndexOf(r);
                        return index < fn.Params.Count && fn.Params[index] is Identifier param ? (HolderOf(param), param, $"passed to \"{fname}\"") : null;
                    }
                case Property { Computed: false, Kind: PropertyKind.Init, Method: false } prop when prop.Value == r && _parent.TryGetValue(prop, out var po) && po is ObjectExpression alias
                                                                                                   && prop.Key is Identifier or StringLiteral:
                    {
                        var key = prop.Key is Identifier k ? k.Name : ((StringLiteral)prop.Key).Value;
                        return (FieldUses(key, alias) is { } flow ? new Holder(key, null, flow.Uses) : null, prop, $"stored in the field \"{key}\"");
                    }
            }
            return null;
        }

        /// <summary>
        /// What is done to a held array, besides giving it values: null when nothing adds to it. Otherwise a Why (a
        /// change the compile cannot bound), or Max with Again set for a cap every addition is held to, or Max for the
        /// most that additions in bounded loops add to an array made fresh each time.
        /// </summary>
        private Lst? Changes(Holder h, int depth, HashSet<Node> seen)
        {
            if (h.Refs.Count == 0) return null;
            var name = h.Name;
            int? cap = null;
            var added = 0;
            var items = new List<(Expression, Inlined?)>();
            foreach (var r in h.Refs)
            {
                var p = _parent[r];
                if (p is MemberExpression m && m.Object == r)
                {
                    if (m.Computed)
                    {
                        if (WrittenTo(m)) return Lst.Unknown($"\"{name}\" is written at an index, which can make it longer (not translated yet)");
                        continue;
                    }
                    var method = m.Property is Identifier pi ? pi.Name : string.Empty;
                    if (method == "length")
                    {
                        if (!WrittenTo(m)) continue;
                        if (_parent[m] is AssignmentExpression { Operator: Operator.Assignment } la && la.Left == m && Finite(la.Right) is { Count: > 0 } lens && lens.All(x => x is double))
                        {
                            cap = Math.Max(cap ?? 0, (int)lens.Cast<double>().Max());
                            continue;
                        }
                        if (Trim(StatementOf(_parent[m]), h) is { } k) { cap = Math.Max(cap ?? 0, k); continue; }
                        return Lst.Unknown($"\"{name}\".length is set to a value only known at run time");
                    }
                    if (_parent[m] is CallExpression c && c.Callee == m)
                    {
                        int adds;
                        switch (method)
                        {
                            case "push" or "unshift":
                                adds = c.Arguments.Count;
                                break;
                            case "splice":
                                if (c.Arguments.Count <= 2) continue;
                                adds = c.Arguments.Count - 2;
                                break;
                            case "pop" or "shift" or "sort" or "reverse" or "fill" or "copyWithin" or "map" or "filter" or "forEach" or "slice" or "join"
                                or "indexOf" or "lastIndexOf" or "includes" or "some" or "every" or "find" or "findIndex" or "findLast" or "findLastIndex"
                                or "reduce" or "reduceRight" or "concat" or "at" or "entries" or "keys" or "values" or "toString" or "flat" or "flatMap"
                                or "toSorted" or "toReversed" or "with":
                                continue;
                            default:
                                return Lst.Unknown($"\"{name}\".{method}(), which the compile does not follow");
                        }
                        if (c.Arguments.Any(a => a is SpreadElement)) return Lst.Unknown($"\"{name}\".{method}(…) with a spread");
                        foreach (var a in c.Arguments.Skip(method == "splice" ? 2 : 0)) items.Add(((Expression)a, null));
                        var at = StatementOf(c);
                        if (at == null) return Lst.Unknown($"\"{name}\".{method}() used as a value");
                        if (Guarded(at, h) is { } g) { cap = Math.Max(cap ?? 0, g - 1 + adds); continue; }
                        if (_parent.TryGetValue(at, out var holder) && Stmts(holder) is { } sibs && sibs.IndexOf((Statement)at) is var ix and >= 0
                            && ix + 1 < sibs.Count && Trim(sibs[ix + 1], h) is { } trim && (adds == 1 || !OneAtATime(sibs[ix + 1])))
                        {
                            cap = Math.Max(cap ?? 0, trim);
                            continue;
                        }
                        if (h.Decl != null && Fresh(h.Decl, at, depth, seen) is { } times) { added += adds * times; continue; }
                        return Lst.Unknown($"\"{name}\".{method}() with nothing holding it to a length (an `if ({name}.length < N)` before it, or a trim after it, is followed)");
                    }
                    continue;
                }
                switch (p)
                {
                    case ForOfStatement fo when fo.Right == r:
                    case SpreadElement when _parent[p] is ArrayExpression:
                    case AssignmentExpression a when a.Left == r:
                    case IfStatement:
                    case NonUpdateUnaryExpression { Operator: Operator.LogicalNot }:
                    case NonLogicalBinaryExpression { Operator: Operator.StrictEquality or Operator.StrictInequality or Operator.Equality or Operator.Inequality }:
                    case ConditionalExpression cond when cond.Test == r:
                    case LogicalExpression or TemplateLiteral or NonLogicalBinaryExpression:
                        continue;
                    case CallExpression call when call.Arguments.Contains(r)
                        // read and nothing more: JSON.stringify, Array.isArray, String, a console call (which the compile drops)
                        && (call.Callee is MemberExpression { Object: Identifier { Name: "JSON" or "Array" or "console" } g, Computed: false } && !Declared(g.Name)
                            || call.Callee is Identifier { Name: "String" } s && !Declared(s.Name)
                            // another array's concat copies its items, and includes/indexOf only look
                            || call.Callee is MemberExpression { Computed: false, Property: Identifier { Name: "concat" or "includes" or "indexOf" or "lastIndexOf" } }):
                        continue;
                    case CallExpression or Property:
                        {
                            // handed on, the same array: whatever is done to it there
                            if (HandedOn(r) is not { } on) break;
                            if (on.To == null) return Lst.Unknown($"\"{name}\" is {on.What} of an object the compile cannot follow");
                            if (!seen.Add(on.Key)) continue;
                            try
                            {
                                if (Changes(on.To, depth + 1, seen) is { } inner && (inner.Why.Length > 0 || inner.Max != 0))
                                    return Lst.Unknown($"\"{name}\" is {on.What}, which adds to it");
                            }
                            finally { seen.Remove(on.Key); }
                            continue;
                        }
                }
                if (p is CallExpression pc && pc.Arguments.Contains(r)) return Lst.Unknown($"\"{name}\" is passed where the compile cannot follow what is done to it");
                // the list a markup write reads its rows from, and any other read of it as a value
                if (p is MemberExpression pm && pm.Object == r) continue;
                return Lst.Unknown($"\"{name}\" is handed where the compile cannot follow what is done to it");
            }
            if (cap == null && added == 0 && items.Count == 0) return null;
            if (cap != null && added > 0) return Lst.Unknown($"\"{name}\" is both held to a length and added to in loops, which the compile does not combine");
            return cap != null ? new Lst { Max = cap, Again = true, Items = items } : new Lst { Max = added, Items = items };
        }

        /// <summary>Whether a held array has anything done to it besides being read, where it is held or handed on: then it can hold fewer items than it was given.</summary>
        private bool Shrunk(Holder h, HashSet<Node>? seen = null)
        {
            foreach (var r in h.Refs)
            {
                if (_parent[r] is MemberExpression m && m.Object == r
                    && (m.Computed ? WrittenTo(m)
                        : m.Property is Identifier { Name: var n } && (n == "length" && WrittenTo(m)
                            || n is "pop" or "shift" or "splice" or "push" or "unshift" or "fill" or "copyWithin" && _parent[m] is CallExpression c && c.Callee == m)))
                    return true;
                if (HandedOn(r) is { To: { } to, Key: var key } && (seen ??= new HashSet<Node>()).Add(key) && Shrunk(to, seen)) return true;
            }
            return false;
        }

        /// <summary>The statement an expression is evaluated for its effect in, or null when its value is used.</summary>
        private Node? StatementOf(Node e) => _parent.TryGetValue(e, out var p) && p is ExpressionStatement ? p : null;

        /// <summary>`if (a.length &lt; K) a.push(…)`: K (for `&lt;=`, K + 1), when that is the only addition to the array in the if.</summary>
        private int? Guarded(Node stmt, Holder h)
        {
            var up = _parent[stmt];
            if (up is BlockStatement b)
            {
                var adds = b.Body.Count(x => x is ExpressionStatement { Expression: CallExpression { Callee: MemberExpression { Computed: false, Object: var o, Property: Identifier { Name: "push" or "unshift" or "splice" } } } } && Is(h, o));
                if (adds != 1) return null;
                stmt = b;
                up = _parent[b];
            }
            if (up is not IfStatement ifs || ifs.Consequent != stmt) return null;
            if (Length(ifs.Test, h) is not var (op, k)) return null;
            return op switch { "<" => k, "<=" => k + 1, _ => null };
        }

        /// <summary>A statement that holds the array to K: `if (a.length &gt; K) a.shift()` (or pop, splice(0, 1), `a.length = K`), `while (a.length &gt; K) a.shift()`, `a.splice(K)`, `a.splice(0, a.length - K)`, `a.length = Math.min(a.length, K)`.</summary>
        private int? Trim(Node? s, Holder h)
        {
            switch (s)
            {
                case IfStatement { Alternate: null } ifs when Length(ifs.Test, h) is var (op, k) && op is ">" or ">=":
                    return Shrinks(ifs.Consequent, h, op == ">" ? k : k - 1);
                case WhileStatement ws when Length(ws.Test, h) is var (op, k) && op is ">" or ">=":
                    return Shrinks(ws.Body, h, op == ">" ? k : k - 1);
                case ExpressionStatement { Expression: var e }:
                    if (e is CallExpression { Callee: MemberExpression { Computed: false, Object: var o, Property: Identifier { Name: "splice" } } } sp && Is(h, o))
                    {
                        if (sp.Arguments.Count == 1 && Finite((Expression)sp.Arguments[0]) is [double k1] && k1 >= 0) return (int)k1;
                        if (sp.Arguments.Count == 2 && sp.Arguments[0] is NumericLiteral { Value: 0 }
                            && sp.Arguments[1] is NonLogicalBinaryExpression { Operator: Operator.Subtraction, Left: MemberExpression { Computed: false, Object: var o2, Property: Identifier { Name: "length" } } } sub
                            && Is(h, o2) && Finite(sub.Right) is [double k2] && k2 >= 0)
                            return (int)k2;
                    }
                    if (e is AssignmentExpression { Operator: Operator.Assignment, Left: MemberExpression { Computed: false, Object: var o3, Property: Identifier { Name: "length" } } } la && Is(h, o3)
                        && la.Right is CallExpression { Callee: MemberExpression { Object: Identifier { Name: "Math" }, Property: Identifier { Name: "min" } } } min
                        && min.Arguments.Select(x => x as Expression).Where(x => x != null).Select(x => Finite(x!)).FirstOrDefault(f => f is [double]) is [double k3])
                        return (int)k3;
                    return null;
            }
            return null;
        }

        /// <summary>Whether a trim takes one item away (so each addition before it has to add one).</summary>
        private static bool OneAtATime(Node s) => s is IfStatement;

        /// <summary>A body that takes items off the array: shift, pop, splice(0, 1), or `a.length = K`.</summary>
        private int? Shrinks(Statement body, Holder h, int k)
        {
            var s = body is BlockStatement { Body.Count: 1 } b ? b.Body[0] : body;
            if (s is not ExpressionStatement { Expression: var e }) return null;
            if (e is CallExpression { Callee: MemberExpression { Computed: false, Object: var o, Property: Identifier { Name: var n } } } c && Is(h, o)
                && (n is "shift" or "pop" && c.Arguments.Count == 0 || n == "splice" && c.Arguments.Count == 2 && c.Arguments[1] is NumericLiteral { Value: >= 1 }))
                return k;
            if (e is AssignmentExpression { Operator: Operator.Assignment, Left: MemberExpression { Computed: false, Object: var o2, Property: Identifier { Name: "length" } } } la
                && Is(h, o2) && Finite(la.Right) is [double v] && v <= k)
                return k;
            return null;
        }

        /// <summary>`a.length OP K` (or `K OP a.length`, turned round) with K a fixed number.</summary>
        private (string Op, int K)? Length(Expression test, Holder h)
        {
            if (test is not NonLogicalBinaryExpression b) return null;
            bool Len(Expression x) => x is MemberExpression { Computed: false, Object: var o, Property: Identifier { Name: "length" } } && Is(h, o);
            string? op = b.Operator switch
            {
                Operator.LessThan => "<", Operator.LessThanOrEqual => "<=", Operator.GreaterThan => ">", Operator.GreaterThanOrEqual => ">=",
                _ => null,
            };
            if (op == null) return null;
            if (Len(b.Left) && Finite(b.Right) is [double k] && k == Math.Floor(k)) return (op, (int)k);
            if (Len(b.Right) && Finite(b.Left) is [double k2] && k2 == Math.Floor(k2))
                return (op switch { "<" => ">", "<=" => ">=", ">" => "<", _ => "<=" }, (int)k2);
            return null;
        }

        /// <summary>
        /// An addition to an array made fresh each time (`const rows = []` in the function that adds to it) inside
        /// loops over bounded lists: how many times it can run. Null when it runs in some other function, or in a
        /// loop the compile cannot bound.
        /// </summary>
        private int? Fresh(Identifier decl, Node at, int depth, HashSet<Node> seen)
        {
            if (_parent[decl] is not VariableDeclarator { Init: ArrayExpression } vd || !_parent.TryGetValue(vd, out var dd) || !_parent.TryGetValue(dd, out var scope)) return null;
            var times = 1;
            Node n = at;
            while (_parent.TryGetValue(n, out var up))
            {
                if (up == scope) return times;
                switch (up)
                {
                    case ForOfStatement fo when fo.Body == n:
                        {
                            if (Listed(fo.Right, null, depth + 1, seen).Max is not { } m) return null;
                            times *= m;
                            break;
                        }
                    case ForStatement { Init: VariableDeclaration { Declarations: [{ Id: Identifier i, Init: NumericLiteral { Value: 0 } }] }, Test: NonLogicalBinaryExpression { Operator: Operator.LessThan or Operator.LessThanOrEqual, Left: Identifier ti } test } f
                        when f.Body == n && Decl(ti) == i:
                        {
                            int? m;
                            if (test.Right is MemberExpression { Computed: false, Property: Identifier { Name: "length" } } lm && test.Operator == Operator.LessThan)
                                m = Listed(lm.Object, null, depth + 1, seen).Max;
                            else m = Finite(test.Right) is { Count: > 0 } set && set.All(v => v is double) ? (int)Math.Ceiling(set.Cast<double>().Max()) + (test.Operator == Operator.LessThanOrEqual ? 1 : 0) : null;
                            if (m == null) return null;
                            times *= Math.Max(0, m.Value);
                            break;
                        }
                    case WhileStatement or DoWhileStatement or ForInStatement or ForStatement or ForOfStatement:
                        return null;
                    case IFunction:
                        {
                            // a forEach callback is a loop; any other function runs some other time
                            if (!_parent.TryGetValue(up, out var call) || call is not CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "forEach" } } fm } fc
                                || fc.Arguments.Count == 0 || fc.Arguments[0] != up)
                                return null;
                            if (Listed(fm.Object, null, depth + 1, seen).Max is not { } m) return null;
                            times *= m;
                            n = call;
                            continue;
                        }
                }
                n = up;
            }
            return null;
        }
    }
}

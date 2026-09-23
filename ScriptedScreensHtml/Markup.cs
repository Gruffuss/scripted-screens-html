using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Acornima;
using Acornima.Ast;

namespace ScriptedScreensHtml;

/// <summary>
/// The markup a page builds, as the fixed parts, the values in them, and the choices between them.
/// </summary>
/// <remarks>
/// <c>innerHTML</c> is how every dashboard-shaped page draws: a <c>render()</c> that builds its whole
/// panel as one string on every tick. Compiling that must NOT mean emitting Lua that builds the same
/// string - that moves the work instead of removing it, 21 KB a tick on the chip. It means the string
/// LITERALS become scene structure once, and only the values interpolated into them are left.
///
/// So this is a small static evaluator over the page's own script. It follows what a name stands for
/// through the page's helpers (<c>panel({...})</c>, <c>heroReadout(o)</c>), into the object a helper
/// returns (<c>values().heroNote</c>), across a list the page maps over (<c>SETPOINTS.map(...)</c>)
/// and through <c>Object.assign</c>, and it folds what is known before the page runs: a constant
/// object's property, a helper's omitted argument, a filter over a literal list. What is left is one
/// of four things:
///
/// <b>fixed markup</b> - structure; <b>a value</b> - a hole the chip writes; <b>a choice</b> whose
/// sides are different markup - a state; and <b>a list</b> whose length the page decides at run time
/// - drawn at its largest and shown row by row. A list's length is a VALUE, never structure: the
/// scene's repeat count is fixed when the scene is read.
///
/// Nothing is executed. What cannot be reduced is REPORTED with its line - a page that compiles and
/// draws the wrong markup is far worse than one that says which expression stopped it.
/// </remarks>
internal sealed class Markup
{
    // ---- scopes and terms --------------------------------------------------------------------

    /// <summary>What a name stands for, frame by frame. Null is the scope the page's own code runs in.</summary>
    internal sealed class Env
    {
        public readonly Env? Outer;
        public readonly Dictionary<string, Term> Names = new(StringComparer.Ordinal);
        /// <summary>
        /// Names that EXIST when the compiled code runs - the page's globals and the locals of the
        /// function that writes innerHTML. They are read by name; everything else in a frame (a
        /// helper's parameter, a helper's local) never runs and is substituted by what it stands for.
        /// </summary>
        public readonly HashSet<string> Live = new(StringComparer.Ordinal);
        public Env(Env? outer) { Outer = outer; }

        public Term? Find(string name, out bool live)
        {
            for (var e = this; e != null; e = e.Outer)
                if (e.Names.TryGetValue(name, out var t)) { live = e.Live.Contains(name); return t; }
            live = true;
            return null;
        }
    }

    /// <summary>A value the markup reads, with the scope it is read in.</summary>
    internal abstract class Term;

    /// <summary>An expression in a scope.</summary>
    internal sealed class Src : Term
    {
        public readonly Expression Expr;
        public readonly Env? Env;
        public Src(Expression expr, Env? env) { Expr = expr; Env = env; }
    }

    /// <summary>Element <see cref="Index"/> of a list whose length is known.</summary>
    internal sealed class Elem : Term
    {
        public readonly Term List;
        public readonly int Index;
        public Elem(Term list, int index) { List = list; Index = index; }
    }

    /// <summary>A row of a list whose length the page decides: a Lua local when the chunk runs.</summary>
    internal sealed class Row : Term
    {
        public readonly string Lua;
        /// <summary>What any element of the list looks like, for the questions asked before it runs.</summary>
        public readonly Term? Shape;
        public Row(string lua, Term? shape) { Lua = lua; Shape = shape; }
    }

    /// <summary>A value known before the page runs: a string, a double, a bool, null or <see cref="Undefined"/>.</summary>
    internal sealed class Const : Term
    {
        public readonly object? Value;
        public Const(object? value) { Value = value; }
    }

    /// <summary>A function applied to arguments - a list's callback on one item - resolved when asked.</summary>
    internal sealed class Applied : Term
    {
        public readonly SFn Fn;
        public readonly Term[] Args;
        public Applied(SFn fn, Term[] args) { Fn = fn; Args = args; }
    }

    internal static readonly object Undefined = new();

    // ---- static values -----------------------------------------------------------------------

    internal abstract class SVal;
    internal sealed class SLit : SVal { public readonly object? V; public SLit(object? v) { V = v; } }
    /// <summary>An object: property lists, later ones winning, as `Object.assign` and spreads merge them.</summary>
    internal sealed class SObj : SVal
    {
        public readonly List<(ObjectExpression Obj, Env? Env)> Layers = new();
        /// <summary>False when a layer came from something unseen, so a missing property is unknown rather than undefined.</summary>
        public bool Closed = true;
    }
    internal sealed class SArr : SVal { public readonly List<Term> Items; public SArr(List<Term> items) { Items = items; } }
    internal sealed class SFn : SVal { public readonly IFunction Fn; public readonly Env? Env; public SFn(IFunction fn, Env? env) { Fn = fn; Env = env; } }
    /// <summary>One of these, which one decided at run time.</summary>
    internal sealed class SAny : SVal { public readonly List<Term> Options; public SAny(List<Term> options) { Options = options; } }
    /// <summary>
    /// Decided at run time. For a list, <see cref="Max"/> is the longest it can be (-1 unknown),
    /// <see cref="Base"/> the list the page iterates at run time and <see cref="Item"/> what an
    /// element looks like.
    /// </summary>
    internal sealed class SDyn : SVal
    {
        public readonly int Max;
        public readonly Term? Base;
        public readonly SFn? Map;
        public readonly Term? Item;
        public SDyn(int max = -1, Term? @base = null, SFn? map = null, Term? item = null) { Max = max; Base = @base; Map = map; Item = item; }
    }

    private static readonly SDyn Dyn = new();

    /// <summary>A string decided at run time that is never empty: `'set ' + f1(x)`, a `toFixed()`.</summary>
    internal sealed class SStr : SVal;
    /// <summary>A number decided at run time: never null, so `x == null` on it is known.</summary>
    internal sealed class SNum : SVal;
    private static readonly SStr Str = new();
    private static readonly SNum Num = new();

    // ---- the parts ---------------------------------------------------------------------------

    internal abstract class Part;

    /// <summary>Markup the page always writes: scene structure.</summary>
    internal sealed class Fixed : Part
    {
        public readonly string Text;
        public Fixed(string text) { Text = text; }
        public override string ToString() => Text;
    }

    /// <summary>A value the page computes: a slot, or a state when it is drawn from a fixed set.</summary>
    internal sealed class Hole : Part
    {
        public readonly Term Value;
        /// <summary>Document order, numbered once the whole markup is known.</summary>
        public int Index;
        public Hole(Term value) { Value = value; }
    }

    /// <summary>Two different pieces of markup, the page picking one when it runs.</summary>
    internal sealed class Choice : Part
    {
        public readonly Term Test;
        public readonly List<Part> Then, Else;
        public int Index;
        /// <summary>
        /// Made by <see cref="Expand"/>: one element written once per value. Its sides lay out alike
        /// and differ only in how they are painted, so switching between them is a gate and nothing else.
        /// </summary>
        public bool Expanded;
        /// <summary>For an expanded choice, how far down its chain it sits: the value it picks is copy Depth.</summary>
        public int Depth;
        public Choice(Term test, List<Part> then, List<Part> otherwise) { Test = test; Then = then; Else = otherwise; }
    }

    /// <summary>
    /// A list the page decides the length of, drawn at its largest: <see cref="Each"/> holds one row's
    /// markup per possible row, and the chunk shows the first n.
    /// </summary>
    internal sealed class Rows : Part
    {
        /// <summary>The list the page iterates, evaluated once per render.</summary>
        public readonly Term List;
        public readonly List<List<Part>> Each = new();
        /// <summary>The Lua local each row's item is read into.</summary>
        public readonly List<string> Items = new();
        public int Index;
        public Rows(Term list) { List = list; }
    }

    /// <summary>A click handler the markup registers for the element whose start tag it sits in.</summary>
    internal sealed class Push : Part
    {
        public readonly Term Handler;
        public Push(Term handler) { Handler = handler; }
    }

    // ---- the analysis ------------------------------------------------------------------------

    private readonly Script _script;
    private string? _source;
    private readonly List<string> _problems = new();
    private readonly Env _globals = new(null);
    /// <summary>Globals a page writes to after declaring them: their contents are decided at run time.</summary>
    private readonly HashSet<string> _mutable = new(StringComparer.Ordinal);
    private int _depth;
    private int _rowNames;
    private const int MaxDepth = 500;
    /// <summary>A list the page decides the length of and that nothing bounds is drawn this long, and says so.</summary>
    internal const int DefaultRows = 16;

    internal IReadOnlyList<string> Problems => _problems;
    internal List<Part> Parts { get; private set; } = new();
    internal readonly List<Hole> Holes = new();
    internal readonly List<Choice> Choices = new();
    internal readonly List<Rows> Lists = new();
    /// <summary>The scope the innerHTML assignment runs in, for translating what the chunk evaluates.</summary>
    internal Env Scope { get; private set; }

    private Markup(Script script)
    {
        _script = script;
        Scope = _globals;
    }

    /// <summary>
    /// Reduces one <c>innerHTML</c> assignment.
    /// </summary>
    /// <param name="assignment">The whole assignment, so the function it sits in can be found.</param>
    /// <param name="expand">
    /// Holes whose values draw differently SHAPED markup - a shadow that comes and goes, an
    /// animation that starts - so their element is written once per value and the page picks one.
    /// The compiler finds these by laying the values out, in rounds: each set is by the index the
    /// reduction had after the sets before it, since a copy of an element brings holes of its own.
    /// </param>
    internal static Markup Of(AssignmentExpression assignment, Script script, string? source = null, IReadOnlyList<ICollection<int>>? expand = null)
    {
        var m = new Markup(script) { _source = source, Start = assignment.Range.Start };
        m.Globals();
        m.Scope = m.Enclosing(assignment);
        m.Parts = new List<Part>();
        m.Walk(new Src(assignment.Right, m.Scope), m.Parts);
        Merge(m.Parts);
        m.SplitStyles(m.Parts, new Cursor(), 0);
        m.Number(m.Parts);
        foreach (var round in expand ?? Array.Empty<ICollection<int>>())
        {
            var targets = new HashSet<Hole>();
            foreach (var i in round) if (i >= 0 && i < m.Holes.Count) targets.Add(m.Holes[i]);
            m.Expand(m.Parts, targets);
            m.Holes.Clear(); m.Choices.Clear(); m.Lists.Clear();
            m.Number(m.Parts);
        }
        if (!Structural(m.Parts))
            m._problems.Add(Line(assignment) + "the markup is computed rather than built from literals, so there is no structure to emit");
        return m;
    }

    /// <summary>The page's top-level names, and which of them the page writes to after declaring.</summary>
    private void Globals()
    {
        foreach (var s in _script.Body)
            Declare(s, _globals, live: true);

        // A name is written if it is assigned, updated, or the root of an assigned or mutated path -
        // `st.log = ...`, `st.tick++`, `acts.push(fn)`. Its contents are then run-time values, and
        // folding them to their initial literal would draw the page as it was at load for ever.
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in Everything(_script))
            if (n is VariableDeclarator { Id: Identifier a, Init: { } init } && Root(init) is { } r) aliases[a.Name] = r;
        void Mark(Node? target)
        {
            if (Root(target) is not { } root) return;
            _mutable.Add(root);
            for (var guard = 0; guard < 8 && aliases.TryGetValue(root, out var up); guard++) { _mutable.Add(up); root = up; }
        }
        foreach (var n in Everything(_script))
        {
            switch (n)
            {
                case AssignmentExpression a: Mark(a.Left); break;
                case UpdateExpression u: Mark(u.Argument); break;
                case NonUpdateUnaryExpression { Operator: Operator.Delete } d: Mark(d.Argument); break;
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier p, Object: { } o } }
                    when p.Name is "push" or "pop" or "shift" or "unshift" or "splice" or "sort" or "reverse" or "fill" or "copyWithin":
                    Mark(o); break;
                case CallExpression { Callee: MemberExpression { Object: Identifier { Name: "Object" }, Property: Identifier { Name: "assign" } } } oa
                    when oa.Arguments.Count > 0:
                    Mark(oa.Arguments[0]); break;
            }
        }
    }

    /// <summary>The identifier at the bottom of a member chain: `st` for `st.log[0].msg`.</summary>
    private static string? Root(Node? n)
    {
        for (var guard = 0; n != null && guard < 32; guard++)
            switch (n)
            {
                case Identifier id: return id.Name;
                case MemberExpression m: n = m.Object; break;
                default: return null;
            }
        return null;
    }

    private static void Declare(Node s, Env env, bool live)
    {
        switch (s)
        {
            case FunctionDeclaration { Id: { } id } fd:
                env.Names[id.Name] = new FnTerm(new SFn(fd, env));
                if (live) env.Live.Add(id.Name);
                break;
            case VariableDeclaration vd:
                foreach (var d in vd.Declarations)
                    if (d.Id is Identifier v)
                    {
                        env.Names[v.Name] = d.Init != null ? new Src(d.Init, env) : new Const(Undefined);
                        if (live) env.Live.Add(v.Name);
                    }
                break;
        }
    }

    /// <summary>A function declaration, so a name bound to one resolves like a name bound to an arrow.</summary>
    internal sealed class FnTerm : Term
    {
        public readonly SFn Fn;
        public FnTerm(SFn fn) { Fn = fn; }
    }

    /// <summary>
    /// The scope the assignment runs in: the page's globals, then the parameters and locals of every
    /// function around it, innermost last. All of them exist when the chunk runs, so they are read by
    /// name - and a `const` among them is also followed for the questions asked before it runs,
    /// which is how `v.heroNote` is found to be markup inside `values()`.
    /// </summary>
    private Env Enclosing(Node target)
    {
        var chain = new List<IFunction>();
        Find(_script, new List<IFunction>());
        var env = _globals;
        foreach (var fn in chain)
        {
            env = new Env(env);
            foreach (var p in fn.Params)
                if (p is Identifier id) { env.Names[id.Name] = new Const(null); env.Live.Add(id.Name); Dynamic(env, id.Name); }
            if (fn.Body is BlockStatement block)
            {
                // Only what is declared BEFORE the assignment, and only a `const`: a `let` may be
                // reassigned between its declaration and the write, and then its initial value is not
                // what the markup reads.
                foreach (var st in block.Body)
                {
                    if (Contains(st, target)) break;
                    if (st is VariableDeclaration { Kind: VariableDeclarationKind.Const } vd)
                        Declare(vd, env, live: true);
                    else if (st is VariableDeclaration or FunctionDeclaration)
                        foreach (var n in Names(st)) { env.Names[n] = new Const(null); env.Live.Add(n); Dynamic(env, n); }
                }
            }
        }
        return env;

        bool Find(Node n, List<IFunction> stack)
        {
            if (ReferenceEquals(n, target)) { chain.AddRange(stack); return true; }
            var pushed = false;
            if (n is IFunction f) { stack.Add(f); pushed = true; }
            foreach (var c in n.ChildNodes)
                if (c != null && Find(c, stack)) return true;
            if (pushed) stack.RemoveAt(stack.Count - 1);
            return false;
        }
    }

    /// <summary>Marks a live name as decided at run time, for a parameter or a local with no fixed value.</summary>
    private static void Dynamic(Env env, string name) => env.Names[name] = new Const(DynamicMarker);
    private static readonly object DynamicMarker = new();

    private static IEnumerable<string> Names(Node s)
    {
        if (s is FunctionDeclaration { Id: { } id }) yield return id.Name;
        if (s is VariableDeclaration vd)
            foreach (var d in vd.Declarations) if (d.Id is Identifier v) yield return v.Name;
    }

    private static bool Contains(Node n, Node target)
    {
        if (ReferenceEquals(n, target)) return true;
        foreach (var c in n.ChildNodes) if (c != null && Contains(c, target)) return true;
        return false;
    }

    // ---- evaluation --------------------------------------------------------------------------

    /// <summary>What a term is, as far as can be known before the page runs.</summary>
    internal SVal Eval(Term t)
    {
        if (++_depth > MaxDepth) { _depth--; return Dyn; }
        try
        {
            switch (t)
            {
                case Const c: return ReferenceEquals(c.Value, DynamicMarker) ? Dyn : new SLit(c.Value);
                case FnTerm f: return f.Fn;
                case Row r: return r.Shape != null ? Eval(r.Shape) : Dyn;
                case Elem e:
                    {
                        var list = Eval(e.List);
                        if (list is SArr a) return e.Index < a.Items.Count ? Eval(a.Items[e.Index]) : new SLit(Undefined);
                        if (list is SDyn { Item: { } item }) return Eval(item);
                        return Dyn;
                    }
                case Applied ap:
                    {
                        var (result, _) = Inline(ap.Fn, ap.Args);
                        return result == null ? Dyn : Eval(result);
                    }
                case Picked p: return Property(Eval(p.Of), p.Key, null);
                case Fixed_ f: return f.Value;
                case Is i: return Eval(i.Value) is SLit l ? new SLit(JsString(l.V) == i.Literal) : Dyn;
                case Src s: return EvalExpr(s.Expr, s.Env);
            }
            return Dyn;
        }
        finally { _depth--; }
    }

    private SVal EvalExpr(Expression e, Env? env)
    {
        switch (e)
        {
            case StringLiteral s: return new SLit(s.Value);
            case NumericLiteral n: return new SLit(n.Value);
            case BooleanLiteral b: return new SLit(b.Value);
            case NullLiteral: return new SLit(null);
            case ParenthesizedExpression p: return EvalExpr(p.Expression, env);
            case TemplateLiteral tpl:
                {
                    var sb = new StringBuilder();
                    for (var i = 0; i < tpl.Quasis.Count; i++)
                    {
                        sb.Append(tpl.Quasis[i].Value.Cooked ?? tpl.Quasis[i].Value.Raw);
                        if (i < tpl.Expressions.Count)
                        {
                            if (EvalExpr(tpl.Expressions[i], env) is not SLit l) return Quasis(tpl) ? Str : Dyn;
                            sb.Append(JsString(l.V));
                        }
                    }
                    return new SLit(sb.ToString());
                }
            case Identifier id:
                {
                    if (id.Name == "undefined") return new SLit(Undefined);
                    if (id.Name == "Infinity") return new SLit(double.PositiveInfinity);
                    if (id.Name == "NaN") return new SLit(double.NaN);
                    var bound = (env ?? _globals).Find(id.Name, out var live);
                    if (bound == null) return Dyn;
                    if (live && _mutable.Contains(id.Name) && IsGlobal(env, id.Name)) return Dyn;
                    return Eval(bound);
                }
            case ObjectExpression obj:
                {
                    var o = new SObj();
                    // A spread folds the object it spreads into this one, before or after the
                    // literal's own keys as the source says: later keys win.
                    var hasSpread = false;
                    foreach (var p in obj.Properties) if (p is SpreadElement) { hasSpread = true; break; }
                    if (!hasSpread) { o.Layers.Add((obj, env)); return o; }
                    foreach (var p in obj.Properties)
                        if (p is SpreadElement sp)
                        {
                            if (EvalExpr(sp.Argument, env) is SObj inner) { o.Layers.AddRange(inner.Layers); o.Closed &= inner.Closed; }
                            else o.Closed = false;
                        }
                    o.Layers.Add((obj, env));
                    return o;
                }
            case ArrayExpression arr:
                {
                    var items = new List<Term>();
                    foreach (var el in arr.Elements)
                    {
                        if (el is SpreadElement or null) return Dyn;
                        items.Add(new Src((Expression)el, env));
                    }
                    return new SArr(items);
                }
            case ArrowFunctionExpression a: return new SFn(a, env);
            case FunctionExpression f: return new SFn(f, env);
            case MemberExpression m: return Member(m, env);
            case CallExpression c: return Call(c, env);
            case ConditionalExpression c:
                {
                    var truth = Truth(new Src(c.Test, env));
                    if (truth == true) return EvalExpr(c.Consequent, env);
                    if (truth == false) return EvalExpr(c.Alternate, env);
                    return new SAny(new List<Term> { new Src(c.Consequent, env), new Src(c.Alternate, env) });
                }
            case LogicalExpression { Operator: Operator.LogicalOr or Operator.NullishCoalescing } or:
                {
                    var left = EvalExpr(or.Left, env);
                    var truth = or.Operator == Operator.LogicalOr ? TruthOf(left) : NullishOf(left);
                    if (truth == true) return left;
                    if (truth == false) return EvalExpr(or.Right, env);
                    // What the left can be when it is what comes out - only its truthy values - and
                    // then the right. `TONE[x] || TONE.text` is never undefined.
                    var options = Options(left, or.Operator == Operator.LogicalOr ? false : (bool?)null);
                    options.Add(new Src(or.Right, env));
                    return new SAny(options);
                }
            case LogicalExpression { Operator: Operator.LogicalAnd } and:
                {
                    var left = EvalExpr(and.Left, env);
                    var truth = TruthOf(left);
                    if (truth == false) return left;
                    if (truth == true) return EvalExpr(and.Right, env);
                    var options = Options(left, true);
                    options.Add(new Src(and.Right, env));
                    return new SAny(options);
                }
            case NonUpdateUnaryExpression { Operator: Operator.LogicalNot } not:
                {
                    var t = Truth(new Src(not.Argument, env));
                    return t == null ? Dyn : new SLit(!t.Value);
                }
            case NonUpdateUnaryExpression { Operator: Operator.UnaryNegation } neg:
                return EvalExpr(neg.Argument, env) is SLit { V: double d } ? new SLit(-d) : Num;
            case NonUpdateUnaryExpression { Operator: Operator.UnaryPlus }:
                return Num;
            case NonLogicalBinaryExpression b: return Binary(b, env);
        }
        return Dyn;
    }

    private static bool Quasis(TemplateLiteral tpl)
    {
        foreach (var q in tpl.Quasis) if ((q.Value.Cooked ?? q.Value.Raw).Length > 0) return true;
        return false;
    }

    /// <summary>
    /// The possibilities of a value, less those that <paramref name="drop"/> says cannot come out:
    /// false drops the falsy ones (the left of `||`), true the truthy ones (the left of `&amp;&amp;`).
    /// </summary>
    private List<Term> Options(SVal v, bool? drop)
    {
        var list = new List<Term>();
        if (v is SAny any)
        {
            foreach (var o in any.Options)
            {
                var ov = Eval(o);
                if (ov is SAny) { list.AddRange(Options(ov, drop)); continue; }
                var truth = TruthOf(ov);
                if (drop == false && truth == false) continue;
                if (drop == true && truth == true) continue;
                list.Add(o);
            }
            return list;
        }
        var t = TruthOf(v);
        if (!(drop == false && t == false) && !(drop == true && t == true)) list.Add(new Fixed_(v));
        return list;
    }

    /// <summary>A value already worked out, as a term, so a list of possibilities can hold it.</summary>
    internal sealed class Fixed_ : Term
    {
        public readonly SVal Value;
        public Fixed_(SVal value) { Value = value; }
    }

    /// <summary>Whether a live name is the page's global rather than a local of the writing function.</summary>
    private bool IsGlobal(Env? env, string name)
    {
        for (var e = env ?? _globals; e != null; e = e.Outer)
            if (e.Names.ContainsKey(name)) return ReferenceEquals(e, _globals);
        return false;
    }

    private SVal Binary(NonLogicalBinaryExpression b, Env? env)
    {
        var l = EvalExpr(b.Left, env);
        var r = EvalExpr(b.Right, env);
        if (l is not SLit ll || r is not SLit rl)
        {
            // `x == null` on something that is plainly not null - an object, a function, a number
            // or a string a page computed - is decided whatever its value: the one shape of a loose
            // comparison a page actually writes, and `o.fill == null ? plain : gradient` folds on it.
            if (b.Operator is Operator.Equality or Operator.Inequality)
            {
                var other = r is SLit { V: null } ? l : l is SLit { V: null } ? r : null;
                if (other != null && NullishOf(other) is { } present)
                    return new SLit(b.Operator == Operator.Inequality ? present : !present);
            }
            // What kind of thing comes out, even when its value does not: arithmetic is a number,
            // and a concatenation with a non-empty literal in it is a string that is never empty.
            switch (b.Operator)
            {
                case Operator.Addition when NonEmptyString(l) || NonEmptyString(r): return Str;
                case Operator.Addition when (l is SNum || l is SLit { V: double }) && (r is SNum || r is SLit { V: double }): return Num;
                case Operator.Subtraction or Operator.Multiplication or Operator.Division or Operator.Remainder or Operator.Exponentiation:
                    return Num;
            }
            return Dyn;
        }
        var a = ll.V; var c = rl.V;
        switch (b.Operator)
        {
            case Operator.StrictEquality: return new SLit(StrictEquals(a, c));
            case Operator.StrictInequality: return new SLit(!StrictEquals(a, c));
            case Operator.Equality when a == null || c == null || a == Undefined || c == Undefined:
                return new SLit((a == null || a == Undefined) == (c == null || c == Undefined) && (a == null || a == Undefined));
            case Operator.Inequality when a == null || c == null || a == Undefined || c == Undefined:
                return new SLit(!((a == null || a == Undefined) && (c == null || c == Undefined)));
            case Operator.Addition when a is string || c is string: return new SLit(JsString(a) + JsString(c));
            case Operator.Addition when a is double x && c is double y: return new SLit(x + y);
            case Operator.Subtraction when a is double x && c is double y: return new SLit(x - y);
            case Operator.Multiplication when a is double x && c is double y: return new SLit(x * y);
            case Operator.Division when a is double x && c is double y: return new SLit(x / y);
            case Operator.LessThan when a is double x && c is double y: return new SLit(x < y);
            case Operator.LessThanOrEqual when a is double x && c is double y: return new SLit(x <= y);
            case Operator.GreaterThan when a is double x && c is double y: return new SLit(x > y);
            case Operator.GreaterThanOrEqual when a is double x && c is double y: return new SLit(x >= y);
        }
        return Dyn;
    }

    private static bool NonEmptyString(SVal v) => v is SStr || v is SLit { V: string { Length: > 0 } };

    private static bool StrictEquals(object? a, object? b)
        => a is double x && b is double y ? x == y : Equals(a, b);

    private SVal Member(MemberExpression m, Env? env)
    {
        var obj = EvalExpr(m.Object, env);
        var key = Key(m, env);
        // A table read with a key that is one of a few known names is one of those entries, not any
        // entry or undefined: `FILL[o.fillTone || 'steel']` never reads as nothing.
        if (key == null && m.Computed && obj is SObj { Closed: true } table
            && Enumerate(new Src((Expression)m.Property, env)) is { Count: > 0 } names)
        {
            var options = new List<Term>();
            foreach (var n in names) options.Add(PropertyTerm(table, n) ?? new Const(Undefined));
            return options.Count == 1 ? Eval(options[0]) : new SAny(options);
        }
        return Property(obj, key, new Src(m, env));
    }

    /// <summary>The property a member expression names, when it is known: a name, a string or an index.</summary>
    private object? Key(MemberExpression m, Env? env)
    {
        if (!m.Computed) return m.Property is Identifier p ? p.Name : null;
        return EvalExpr((Expression)m.Property, env) is SLit { V: string or double } k ? k.V : null;
    }

    private SVal Property(SVal obj, object? key, Term? whole)
    {
        switch (obj)
        {
            case SObj o when key != null:
                {
                    var name = key is double d ? JsString(d) : (string)key;
                    if (PropertyTerm(o, name) is { } term) return Eval(term);
                    return o.Closed ? new SLit(Undefined) : Dyn;
                }
            case SObj o:
                {
                    // A lookup table read with a key decided at run time is any one of its values -
                    // `TONE[s.tone]` - or undefined when the key is not one of them.
                    var options = new List<Term>();
                    foreach (var (lit, lenv) in o.Layers)
                        foreach (var p in lit.Properties)
                            if (p is ObjectProperty { Value: Expression v }) options.Add(new Src(v, lenv));
                    options.Add(new Const(Undefined));
                    return o.Closed ? new SAny(options) : Dyn;
                }
            case SArr a when key is "length": return new SLit((double)a.Items.Count);
            case SArr a when key is double i: return i >= 0 && i < a.Items.Count && i == Math.Floor(i) ? Eval(a.Items[(int)i]) : new SLit(Undefined);
            case SArr a when key == null:
                {
                    var options = new List<Term>(a.Items) { new Const(Undefined) };
                    return new SAny(options);
                }
            case SLit { V: string s } when key is "length": return new SLit((double)s.Length);
            // A field of nothing: JavaScript would throw, and the only place a page lets it happen is
            // the branch of an `a || b` that is never taken - so it reads as nothing.
            case SLit { V: null }:
            case SLit l0 when l0.V == Undefined:
                return new SLit(Undefined);
            case SAny any:
                {
                    // Each possibility, read the same way. `ROLES.find(...).devs` is one of fifteen
                    // literal lists, and the longest of them is what bounds the rows drawn for it.
                    var options = new List<Term>();
                    foreach (var option in any.Options)
                        options.Add(new Picked(option, key));
                    return new SAny(options);
                }
            case SDyn { Item: { } item } when key is double or null: return Eval(item);
            case SDyn when whole is Src { Expr: MemberExpression path } s && Path(path) is { } text && Bound(text) is var max && max >= 0:
                return new SDyn(max, s);
        }
        return Dyn;
    }

    /// <summary>A property read off a term that is itself decided later: what `find(...).devs` stands for.</summary>
    internal sealed class Picked : Term
    {
        public readonly Term Of;
        public readonly object? Key;
        public Picked(Term of, object? key) { Of = of; Key = key; }
    }

    /// <summary>The term an object's property is defined by, last layer first.</summary>
    internal static Term? PropertyTerm(SObj o, string name)
    {
        for (var i = o.Layers.Count - 1; i >= 0; i--)
        {
            var (lit, env) = o.Layers[i];
            for (var k = lit.Properties.Count - 1; k >= 0; k--)
            {
                if (lit.Properties[k] is not ObjectProperty p || p.Kind != PropertyKind.Init) continue;
                var pname = p.Key switch
                {
                    Identifier id when !p.Computed => id.Name,
                    StringLiteral s => s.Value,
                    NumericLiteral n => JsString(n.Value),
                    _ => null,
                };
                if (pname != name) continue;
                return p.Value is Expression v ? new Src(v, env) : null;
            }
        }
        return null;
    }

    private SVal Call(CallExpression c, Env? env)
    {
        // Object.assign(target, ...sources): one object, the sources' keys winning in order.
        if (c.Callee is MemberExpression { Object: Identifier { Name: "Object" }, Property: Identifier { Name: "assign" } })
        {
            var merged = new SObj();
            foreach (var a in c.Arguments)
            {
                if (a is not Expression x || EvalExpr(x, env) is not SObj o) { merged.Closed = false; continue; }
                merged.Layers.AddRange(o.Layers);
                merged.Closed &= o.Closed;
            }
            return merged;
        }

        if (c.Callee is MemberExpression { Computed: false, Property: Identifier method } mm)
        {
            var target = EvalExpr(mm.Object, env);
            var fn = c.Arguments.Count > 0 && c.Arguments[0] is Expression a0 ? EvalExpr(a0, env) as SFn : null;
            switch (method.Name)
            {
                case "map" when fn != null && target is SArr arr:
                    {
                        var items = new List<Term>();
                        for (var i = 0; i < arr.Items.Count; i++)
                            items.Add(new Applied(fn, new Term[] { new Elem(new Src(mm.Object, env), i), new Const((double)i), new Src(mm.Object, env) }));
                        return new SArr(items);
                    }
                case "map" when fn != null && Longest(target) is var max && max >= 0:
                    return new SDyn(max, new Src(mm.Object, env), fn,
                                    new Applied(fn, new Term[] { ItemOf(target) ?? new Const(DynamicMarker), new Const(DynamicMarker) }));
                case "filter" when fn != null && target is SArr arr:
                    {
                        // A filter over a literal list with a predicate known before the page runs -
                        // `ROLES.filter(r => r.group === 'gas')` - is a literal list itself.
                        var kept = new List<Term>();
                        for (var i = 0; i < arr.Items.Count; i++)
                        {
                            var keep = Truth(new Applied(fn, new Term[] { new Elem(new Src(mm.Object, env), i), new Const((double)i) }));
                            if (keep == null) return new SDyn(arr.Items.Count, new Src(c, env), null, ItemOf(target));
                            if (keep == true) kept.Add(arr.Items[i]);
                        }
                        return new SArr(kept);
                    }
                case "filter" when Longest(target) is var max && max >= 0:
                    return new SDyn(max, new Src(c, env), null, ItemOf(target));
                case "find" when fn != null && target is SArr arr:
                    {
                        var options = new List<Term>();
                        for (var i = 0; i < arr.Items.Count; i++)
                        {
                            var hit = Truth(new Applied(fn, new Term[] { new Elem(new Src(mm.Object, env), i), new Const((double)i) }));
                            if (hit == true && options.Count == 0) return Eval(arr.Items[i]);
                            if (hit != false) options.Add(arr.Items[i]);
                        }
                        options.Add(new Const(Undefined));
                        return new SAny(options);
                    }
                case "slice" when target is SArr arr && c.Arguments.Count <= 2:
                    {
                        var from = c.Arguments.Count > 0 && EvalExpr((Expression)c.Arguments[0], env) is SLit { V: double fv } ? (int)fv : 0;
                        var to = c.Arguments.Count > 1 && EvalExpr((Expression)c.Arguments[1], env) is SLit { V: double tv } ? (int)tv : arr.Items.Count;
                        from = Math.Max(0, Math.Min(from, arr.Items.Count));
                        to = Math.Max(from, Math.Min(to, arr.Items.Count));
                        return new SArr(arr.Items.GetRange(from, to - from));
                    }
                case "concat" when target is SArr arr:
                    {
                        var all = new List<Term>(arr.Items);
                        foreach (var a in c.Arguments)
                        {
                            if (a is not Expression x || EvalExpr(x, env) is not SArr more) return Dyn;
                            all.AddRange(more.Items);
                        }
                        return new SArr(all);
                    }
                case "join" when target is SArr arr:
                    {
                        var sep = c.Arguments.Count > 0 && EvalExpr((Expression)c.Arguments[0], env) is SLit { V: string s } ? s : ",";
                        var sb = new StringBuilder();
                        for (var i = 0; i < arr.Items.Count; i++)
                        {
                            if (Eval(arr.Items[i]) is not SLit l) return Dyn;
                            if (i > 0) sb.Append(sep);
                            sb.Append(l.V == null || l.V == Undefined ? string.Empty : JsString(l.V));
                        }
                        return new SLit(sb.ToString());
                    }
            }
            // A method of a page object: `v.inkOf(s)`, `sp.fmt(x)`. Only when its receiver is one of
            // the page's own objects - a method of a string or a number is a run-time value.
            if (target is SObj o && PropertyTerm(o, method.Name) is { } fnTerm && Eval(fnTerm) is SFn own)
                return InlineValue(own, c.Arguments, env);
            // What a built-in hands back, when its kind is all that matters: `toFixed` is a number's
            // text and never empty; the Math functions are numbers.
            if (method.Name is "toFixed" or "toPrecision" or "toExponential") return Str;
            if (mm.Object is Identifier { Name: "Math" }) return Num;
            return Dyn;
        }

        var callee = EvalExpr(c.Callee, env);
        if (callee is SFn f) return InlineValue(f, c.Arguments, env);
        return Dyn;
    }

    private SVal InlineValue(SFn f, in NodeList<Expression> arguments, Env? env)
    {
        var args = new Term[arguments.Count];
        for (var i = 0; i < args.Length; i++) args[i] = new Src(arguments[i], env);
        var (result, _) = Inline(f, args);
        return result == null ? Dyn : Eval(result);
    }

    /// <summary>The longest a list can be, or -1.</summary>
    private int Longest(SVal v)
    {
        switch (v)
        {
            case SArr a: return a.Items.Count;
            case SDyn d: return d.Max;
            case SAny any:
                {
                    var max = 0;
                    foreach (var o in any.Options)
                    {
                        var ov = Eval(o);
                        if (ov is SLit { V: null } || ov is SLit l && l.V == Undefined) continue;
                        var n = Longest(ov);
                        if (n < 0) return -1;
                        max = Math.Max(max, n);
                    }
                    return max;
                }
        }
        return -1;
    }

    /// <summary>What an element of a list looks like, when that can be said.</summary>
    private static Term? ItemOf(SVal list) => list switch
    {
        SDyn d => d.Item,
        SArr { Items.Count: > 0 } a => a.Items[0],
        _ => null,
    };

    /// <summary>JavaScript truthiness of a term, or null when it is decided at run time.</summary>
    internal bool? Truth(Term t) => TruthOf(Eval(t));

    private bool? TruthOf(SVal v)
    {
        switch (v)
        {
            case SLit l:
                return l.V switch
                {
                    null => false,
                    string s => s.Length > 0,
                    double d => d != 0 && !double.IsNaN(d),
                    bool b => b,
                    _ => l.V == Undefined ? false : true,
                };
            case SObj or SArr or SFn or SStr: return true;
            case SAny any:
                {
                    bool? all = null;
                    foreach (var o in any.Options)
                    {
                        var t = Truth(o);
                        if (t == null) return null;
                        if (all == null) all = t;
                        else if (all != t) return null;
                    }
                    return all;
                }
        }
        return null;
    }

    private bool? NullishOf(SVal v)
    {
        switch (v)
        {
            case SLit l: return !(l.V == null || l.V == Undefined);
            case SObj or SArr or SFn or SStr or SNum: return true;
            case SAny any:
                {
                    bool? all = null;
                    foreach (var o in any.Options)
                    {
                        var t = NullishOf(Eval(o));
                        if (t == null) return null;
                        if (all == null) all = t;
                        else if (all != t) return null;
                    }
                    return all;
                }
        }
        return null;
    }

    // ---- helpers, inlined --------------------------------------------------------------------

    /// <summary>
    /// A call to one of the page's own functions: the term its return value is, with its parameters
    /// and locals bound, and the callbacks it registers on the way.
    /// </summary>
    /// <remarks>
    /// The body must be a builder - declarations, a default or two, one registration, one return -
    /// and not a procedure. <c>function row(x) { const cls = pick(x); return '&lt;div class="' + cls + '"&gt;'; }</c>
    /// is the shape every page's helpers take, and refusing it would refuse most real pages.
    /// </remarks>
    internal (Term? Result, List<Term> Pushes) Inline(SFn f, Term[] args)
    {
        var pushes = new List<Term>();
        if (_depth > MaxDepth) return (null, pushes);
        var frame = new Env(f.Env);
        for (var i = 0; i < f.Fn.Params.Count; i++)
        {
            switch (f.Fn.Params[i])
            {
                case Identifier p:
                    frame.Names[p.Name] = i < args.Length ? args[i] : new Const(Undefined);
                    break;
                case AssignmentPattern { Left: Identifier dp, Right: Expression def }:
                    {
                        // `(o = {})`: the default stands in when the caller passed nothing or undefined.
                        var given = i < args.Length ? args[i] : null;
                        var missing = given == null || Eval(given) is SLit { V: var u } && ReferenceEquals(u, Undefined);
                        frame.Names[dp.Name] = missing ? new Src(def, frame) : given!;
                        break;
                    }
                default:
                    return (null, pushes);
            }
        }
        switch (f.Fn.Body)
        {
            case Expression x: return (new Src(x, frame), pushes);
            case BlockStatement block: return (Returned(block, frame, pushes), pushes);
        }
        return (null, pushes);
    }

    /// <summary>The returned term of a builder body, its declarations bound in the frame.</summary>
    private Src? Returned(BlockStatement block, Env frame, List<Term> pushes)
    {
        foreach (var s in block.Body)
        {
            switch (s)
            {
                case VariableDeclaration vd:
                    foreach (var d in vd.Declarations)
                        if (d.Id is Identifier id) frame.Names[id.Name] = d.Init != null ? new Src(d.Init, frame) : new Const(Undefined);
                        else return null;
                    break;
                case FunctionDeclaration { Id: { } fid } fd:
                    frame.Names[fid.Name] = new FnTerm(new SFn(fd, frame));
                    break;
                // `list.push(fn)` while building markup registers a click handler; its index is
                // positional and so knowable without running the page. The one side effect allowed.
                case ExpressionStatement { Expression: CallExpression
                    { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "push" }, Object: var receiver }, Arguments.Count: 1 } push }
                    when push.Arguments[0] is Expression value:
                    if (receiver is Identifier list) _registers.Add(list.Name);
                    pushes.Add(new Src(value, frame));
                    break;
                // `color = color || 'var(--cb-mark)'`: a default written the old way. It rebinds a
                // name this helper owns, which is not a side effect on anything outside it.
                case ExpressionStatement { Expression: AssignmentExpression { Operator: Operator.Assignment, Left: Identifier target } assign }
                    when frame.Names.ContainsKey(target.Name):
                    {
                        // The right side reads the OLD binding, so it gets a scope of its own where
                        // the name still means that; otherwise `color || x` would resolve to itself.
                        var before = new Env(frame);
                        before.Names[target.Name] = frame.Names[target.Name];
                        frame.Names[target.Name] = new Src(assign.Right, before);
                        break;
                    }
                case ReturnStatement { Argument: { } r }:
                    return new Src(r, frame);
                case EmptyStatement:
                    break;
                default:
                    return null;
            }
        }
        return null;
    }

    // ---- the markup --------------------------------------------------------------------------

    /// <summary>Reduces a term that produces markup into parts.</summary>
    private void Walk(Term t, List<Part> into)
    {
        if (++_depth > MaxDepth)
        {
            _depth--;
            _problems.Add("markup helpers nested more than " + MaxDepth + " deep");
            return;
        }
        try
        {
            switch (t)
            {
                case Const c when !ReferenceEquals(c.Value, DynamicMarker): into.Add(new Fixed(JsString(c.Value))); return;
                case Elem or Applied or Picked:
                    {
                        if (t is Applied ap)
                        {
                            var (result, pushes) = Inline(ap.Fn, ap.Args);
                            if (result != null)
                            {
                                foreach (var p in pushes) into.Add(new Push(p));
                                Walk(result, into);
                                return;
                            }
                        }
                        else if (Definition(t) is { } def && def != t) { Try(t, def, into); return; }
                        Value(t, into);
                        return;
                    }
                case Src s:
                    WalkExpr(s, into);
                    return;
            }
            Value(t, into);
        }
        finally { _depth--; }
    }

    private void WalkExpr(Src s, List<Part> into)
    {
        var env = s.Env;
        switch (s.Expr)
        {
            case StringLiteral lit:
                into.Add(new Fixed(lit.Value));
                return;
            case ParenthesizedExpression p:
                Walk(new Src(p.Expression, env), into);
                return;
            case TemplateLiteral tpl:
                for (var i = 0; i < tpl.Quasis.Count; i++)
                {
                    into.Add(new Fixed(tpl.Quasis[i].Value.Cooked ?? tpl.Quasis[i].Value.Raw));
                    if (i < tpl.Expressions.Count) Walk(new Src(tpl.Expressions[i], env), into);
                }
                return;
            case NonLogicalBinaryExpression { Operator: Operator.Addition } add
                when Stringy(new Src(add.Left, env)) || Stringy(new Src(add.Right, env)):
                Walk(new Src(add.Left, env), into);
                Walk(new Src(add.Right, env), into);
                return;
            case ConditionalExpression c:
                {
                    var truth = Truth(new Src(c.Test, env));
                    if (truth == true) { Walk(new Src(c.Consequent, env), into); return; }
                    if (truth == false) { Walk(new Src(c.Alternate, env), into); return; }
                    var then = new List<Part>(); Walk(new Src(c.Consequent, env), then);
                    var otherwise = new List<Part>(); Walk(new Src(c.Alternate, env), otherwise);
                    Branch(s, new Src(c.Test, env), then, otherwise, into);
                    return;
                }
            case LogicalExpression { Operator: Operator.LogicalOr } or:
                {
                    var truth = Truth(new Src(or.Left, env));
                    if (truth == true) { Walk(new Src(or.Left, env), into); return; }
                    if (truth == false) { Walk(new Src(or.Right, env), into); return; }
                    var then = new List<Part>(); Walk(new Src(or.Left, env), then);
                    var otherwise = new List<Part>(); Walk(new Src(or.Right, env), otherwise);
                    Branch(s, new Src(or.Left, env), then, otherwise, into);
                    return;
                }
            case LogicalExpression { Operator: Operator.LogicalAnd } and:
                {
                    var truth = Truth(new Src(and.Left, env));
                    if (truth == false) { Walk(new Src(and.Left, env), into); return; }
                    if (truth == true) { Walk(new Src(and.Right, env), into); return; }
                    var then = new List<Part>(); Walk(new Src(and.Right, env), then);
                    var otherwise = new List<Part>(); Walk(new Src(and.Left, env), otherwise);
                    Branch(s, new Src(and.Left, env), then, otherwise, into);
                    return;
                }
            case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "join" }, Object: { } listExpr } } join:
                {
                    var sep = join.Arguments.Count > 0 && EvalExpr((Expression)join.Arguments[0], env) is SLit { V: string sp } ? sp : ",";
                    if (Joined(new Src(listExpr, env), sep, into)) return;
                    Value(s, into);
                    return;
                }
            case CallExpression call:
                {
                    var callee = call.Callee is MemberExpression { Computed: false, Property: Identifier mp } me && EvalExpr(me.Object, env) is SObj owner
                                 && PropertyTerm(owner, mp.Name) is { } ft && Eval(ft) is SFn method ? method
                               : EvalExpr(call.Callee, env) as SFn;
                    if (callee != null)
                    {
                        var args = new Term[call.Arguments.Count];
                        for (var i = 0; i < args.Length; i++) args[i] = new Src(call.Arguments[i], env);
                        var (result, pushes) = Inline(callee, args);
                        if (result != null)
                        {
                            var parts = new List<Part>();
                            foreach (var p in pushes) parts.Add(new Push(p));
                            Walk(result, parts);
                            if (pushes.Count > 0 || Structural(parts)) { into.AddRange(parts); return; }
                        }
                        else if (Returns(callee))
                        {
                            _problems.Add(Line(call) + "the page's function builds markup with statements this cannot follow (a loop, an `if`), so it would have to run");
                            return;
                        }
                    }
                    Value(s, into);
                    return;
                }
            case Identifier or MemberExpression:
                if (Definition(s) is { } def && def != s) { Try(s, def, into); return; }
                Value(s, into);
                return;
        }
        Value(s, into);
    }

    /// <summary>Arrays the markup registers callbacks in, so their length is a position rather than a value.</summary>
    private readonly HashSet<string> _registers = new(StringComparer.Ordinal);

    private bool Counter(Expression e) => e switch
    {
        MemberExpression { Computed: false, Object: Identifier list, Property: Identifier { Name: "length" } } => _registers.Contains(list.Name),
        NonLogicalBinaryExpression { Operator: Operator.Subtraction, Right: NumericLiteral } b => Counter(b.Left),
        ParenthesizedExpression p => Counter(p.Expression),
        _ => false,
    };

    /// <summary>A function that returns something at all, so refusing to follow it is worth saying.</summary>
    private static bool Returns(SFn f)
    {
        if (f.Fn.Body is not BlockStatement block) return false;
        foreach (var n in Everything(block))
            if (n is ReturnStatement { Argument: StringLiteral or TemplateLiteral or NonLogicalBinaryExpression }) return true;
        return false;
    }

    /// <summary>
    /// A name or a field that may stand for markup: followed, and kept as markup only when what it
    /// stands for has tags in it. Otherwise it is read as it is, which keeps the chunk reading the
    /// page's own computed values (`v.gases[1].value`) instead of computing them again.
    /// </summary>
    private void Try(Term original, Term def, List<Part> into)
    {
        var parts = new List<Part>();
        Walk(def, parts);
        if (Structural(parts)) { into.AddRange(parts); return; }
        Value(original, into);
    }

    /// <summary>A value: folded to text when it is known now, a hole when the page decides it.</summary>
    private void Value(Term t, List<Part> into)
    {
        // `acts.length - 1`: the index a click registration hands back for its `data-act`. The
        // compiled page wires each handler to its element directly, so the number is never read
        // and is written as a constant rather than left as a slot.
        if (t is Src { Expr: var ce } && Counter(ce)) { into.Add(new Fixed("0")); return; }
        var v = Eval(t);
        if (v is SLit l) { into.Add(new Fixed(JsString(l.V))); return; }
        into.Add(new Hole(t));
    }

    /// <summary>
    /// A conditional whose side is decided at run time: a choice when the sides are different
    /// markup, one value when they are not.
    /// </summary>
    /// <remarks>
    /// Nearly every ternary on a real page picks a colour, a width or a word - and treating each as a
    /// choice of shapes doubles the scenes to emit. Measured on one page before this rule: 119
    /// choices, which is 2^119 shapes, against the 23 it really has.
    /// </remarks>
    private void Branch(Src whole, Term test, List<Part> then, List<Part> otherwise, List<Part> into)
    {
        Merge(then);
        Merge(otherwise);
        if (!Structural(then) && !Structural(otherwise)) { Value(whole, into); return; }
        into.Add(new Choice(test, then, otherwise));
    }

    /// <summary>`list.join(sep)` over markup: every row, known or at most as many as the list can hold.</summary>
    private bool Joined(Term list, string sep, List<Part> into)
    {
        var v = Eval(list);
        if (v is SArr arr)
        {
            // A list of words - `detected.map(g => g.n).join(', ')` - is one value, read from the page
            // as it stands; only a list of markup is structure.
            var parts = new List<Part>();
            for (var i = 0; i < arr.Items.Count; i++)
            {
                if (i > 0 && sep.Length > 0) parts.Add(new Fixed(sep));
                Walk(arr.Items[i], parts);
            }
            if (!Structural(parts)) return false;
            into.AddRange(parts);
            return true;
        }
        if (v is SDyn { Map: { } map, Base: { } baseList } dyn)
        {
            var max = dyn.Max;
            if (max < 0)
            {
                max = DefaultRows;
                _problems.Add("a list whose length nothing in the page bounds is drawn at most " + DefaultRows + " rows long");
            }
            var rows = new Rows(baseList);
            var shape = ItemOf(Eval(baseList));
            var n = ++_rowNames;
            for (var k = 0; k < max; k++)
            {
                var name = "__r" + n.ToString(CultureInfo.InvariantCulture) + "_" + k.ToString(CultureInfo.InvariantCulture);
                var row = new List<Part>();
                if (k > 0 && sep.Length > 0) row.Add(new Fixed(sep));
                Walk(new Applied(map, new Term[] { new Row(name, ElementAt(baseList, k) ?? shape), new Const((double)k), baseList }), row);
                Merge(row);
                rows.Items.Add(name);
                rows.Each.Add(row);
            }
            var any = false;
            foreach (var row in rows.Each) any |= Structural(row);
            if (!any) return false;
            into.Add(rows);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Element <paramref name="k"/> of a list, as the row that draws it will find it: of a list mapped
    /// from another - `candidates = sel.devs.map(dv => ...)` - the mapping applied to that list's own
    /// element k, so what a row's markup reads through it (`on`, from `dv`) is that row's and not any
    /// row's. Null when nothing more than the list's shape can be said.
    /// </summary>
    private Term? ElementAt(Term list, int k) => Eval(list) switch
    {
        SArr a => k < a.Items.Count ? a.Items[k] : null,
        SDyn { Map: { } f, Base: { } b } => new Applied(f, new Term[] { new Elem(b, k), new Const((double)k), b }),
        _ => null,
    };

    /// <summary>Whether a term is a string for `+`: a literal, a template, or a concatenation with one.</summary>
    private bool Stringy(Term t)
    {
        if (++_depth > MaxDepth) { _depth--; return false; }
        try
        {
            switch (t)
            {
                case Const { Value: string }: return true;
                case Src s:
                    switch (s.Expr)
                    {
                        case StringLiteral or TemplateLiteral: return true;
                        case ParenthesizedExpression p: return Stringy(new Src(p.Expression, s.Env));
                        case NonLogicalBinaryExpression { Operator: Operator.Addition } b:
                            return Stringy(new Src(b.Left, s.Env)) || Stringy(new Src(b.Right, s.Env));
                        case ConditionalExpression c:
                            return Stringy(new Src(c.Consequent, s.Env)) || Stringy(new Src(c.Alternate, s.Env));
                        case LogicalExpression l:
                            return Stringy(new Src(l.Left, s.Env)) || Stringy(new Src(l.Right, s.Env));
                        case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier m } }
                            when m.Name is "toFixed" or "join" or "toString" or "padStart" or "padEnd" or "slice" or "trim"
                                or "toUpperCase" or "toLowerCase" or "replace" or "replaceAll" or "substring" or "repeat" or "concat":
                            return true;
                        case CallExpression { Callee: Identifier { Name: "String" } }: return true;
                    }
                    break;
            }
            if (Eval(t) is SLit { V: string }) return true;
            return Definition(t) is { } d && d != t && Stringy(d);
        }
        finally { _depth--; }
    }

    /// <summary>
    /// The term a term is defined by, one step further: a name's binding, a field of an object the
    /// page wrote as a literal, an element of a literal list, the return of an inlined call.
    /// </summary>
    internal Term? Definition(Term t)
    {
        switch (t)
        {
            case Elem e:
                return Eval(e.List) is SArr a && e.Index < a.Items.Count ? a.Items[e.Index] : null;
            case Picked p:
                return Eval(p.Of) is SObj o && p.Key is string k ? PropertyTerm(o, k) : null;
            case Applied ap:
                return Inline(ap.Fn, ap.Args).Result;
            case Row r:
                return null;
            case Src s:
                switch (s.Expr)
                {
                    case ParenthesizedExpression p: return new Src(p.Expression, s.Env);
                    case Identifier id:
                        {
                            var bound = (s.Env ?? _globals).Find(id.Name, out var live);
                            if (bound == null || bound is Const { Value: var cv } && ReferenceEquals(cv, DynamicMarker)) return null;
                            if (live && _mutable.Contains(id.Name) && IsGlobal(s.Env, id.Name)) return null;
                            return bound;
                        }
                    case MemberExpression m:
                        {
                            var obj = EvalExpr(m.Object, s.Env);
                            var key = Key(m, s.Env);
                            if (obj is SObj ob && key != null) return PropertyTerm(ob, key is double d ? JsString(d) : (string)key);
                            if (obj is SArr arr && key is double i && i >= 0 && i < arr.Items.Count && i == Math.Floor(i)) return arr.Items[(int)i];
                            return null;
                        }
                    case CallExpression c:
                        {
                            var callee = c.Callee is MemberExpression { Computed: false, Property: Identifier mp } me
                                         && EvalExpr(me.Object, s.Env) is SObj owner && PropertyTerm(owner, mp.Name) is { } ft && Eval(ft) is SFn method
                                ? method
                                : EvalExpr(c.Callee, s.Env) as SFn;
                            if (callee == null) return null;
                            var args = new Term[c.Arguments.Count];
                            for (var i = 0; i < args.Length; i++) args[i] = new Src(c.Arguments[i], s.Env);
                            return Inline(callee, args).Result;
                        }
                    case ConditionalExpression c:
                        {
                            var truth = Truth(new Src(c.Test, s.Env));
                            return truth == true ? new Src(c.Consequent, s.Env) : truth == false ? new Src(c.Alternate, s.Env) : null;
                        }
                    case LogicalExpression { Operator: Operator.LogicalOr } or:
                        {
                            var truth = Truth(new Src(or.Left, s.Env));
                            return truth == true ? new Src(or.Left, s.Env) : truth == false ? new Src(or.Right, s.Env) : null;
                        }
                }
                return null;
        }
        return null;
    }

    /// <summary>
    /// Every string a value can be when it is drawn from a fixed set - a colour chosen by a ternary, a
    /// whole style attribute a helper assembles from literals and choices - or null when it is not.
    /// </summary>
    /// <remarks>
    /// This is what turns an opaque value into a state. `c.style` on a binding row is one string at
    /// run time, but it can only ever be one of four, and each of those four is laid out once here.
    /// </remarks>
    internal List<string>? Enumerate(Term t, int limit = 64)
    {
        if (++_depth > MaxDepth) { _depth--; return null; }
        try
        {
            var v = Eval(t);
            if (v is SLit l) return new List<string> { JsString(l.V) };
            if (t is Src s)
            {
                switch (s.Expr)
                {
                    case ParenthesizedExpression p: return Enumerate(new Src(p.Expression, s.Env), limit);
                    case NonLogicalBinaryExpression { Operator: Operator.Addition } b
                        when Stringy(new Src(b.Left, s.Env)) || Stringy(new Src(b.Right, s.Env)):
                        {
                            var left = Enumerate(new Src(b.Left, s.Env), limit);
                            if (left == null) return null;
                            var right = Enumerate(new Src(b.Right, s.Env), limit);
                            if (right == null || left.Count * right.Count > limit) return null;
                            var all = new List<string>();
                            foreach (var a in left) foreach (var c in right) Add(all, a + c);
                            return all;
                        }
                    case TemplateLiteral tpl:
                        {
                            var all = new List<string> { string.Empty };
                            for (var i = 0; i < tpl.Quasis.Count; i++)
                            {
                                var q = tpl.Quasis[i].Value.Cooked ?? tpl.Quasis[i].Value.Raw;
                                for (var k = 0; k < all.Count; k++) all[k] += q;
                                if (i >= tpl.Expressions.Count) continue;
                                var part = Enumerate(new Src(tpl.Expressions[i], s.Env), limit);
                                if (part == null || all.Count * part.Count > limit) return null;
                                var next = new List<string>();
                                foreach (var a in all) foreach (var c in part) Add(next, a + c);
                                all = next;
                            }
                            return all;
                        }
                }
            }
            if (v is SAny any)
            {
                var all = new List<string>();
                foreach (var o in any.Options)
                {
                    // Undefined in a lookup's options is the key missing; it only matters if nothing
                    // else can be drawn, so it is kept as the string a page would concatenate.
                    var one = Enumerate(o, limit);
                    if (one == null) return null;
                    foreach (var x in one) Add(all, x);
                    if (all.Count > limit) return null;
                }
                return all;
            }
            if (Definition(t) is { } d && d != t) return Enumerate(d, limit);
            return null;
        }
        finally { _depth--; }

        static void Add(List<string> list, string s) { if (!list.Contains(s)) list.Add(s); }
    }

    // ---- style attributes, opened up ------------------------------------------------------------

    /// <summary>
    /// A hole that is a whole style attribute, or a run of declarations, opened into its literal CSS
    /// and the values inside it.
    /// </summary>
    /// <remarks>
    /// `style="' + s.style + '"'` is one opaque string at run time - and a runtime string of CSS is
    /// the one thing a compiled page cannot apply. But its definition is right there in the page:
    /// literal declarations, a colour chosen by a ternary, a percentage from `toFixed`. Opened up,
    /// the literal parts are structure and the width is a number the chip writes, which is the
    /// whole point. Only inside `style`: in text, reading the page's own finished string is simpler
    /// and costs the chip nothing to compute again.
    /// </remarks>
    private Cursor SplitStyles(List<Part> parts, Cursor at, int depth)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            switch (parts[i])
            {
                case Fixed f:
                    at.Feed(f.Text);
                    break;
                case Hole h when ++depth < 4000 && at.State == Cursor.At.Quoted && string.Equals(at.Attribute, "style", StringComparison.OrdinalIgnoreCase):
                    {
                        var def = h.Value;
                        for (var guard = 0; guard < 16 && Definition(def) is { } next && next != def; guard++)
                        {
                            def = next;
                            if (def is Src { Expr: NonLogicalBinaryExpression or TemplateLiteral }) break;
                        }
                        if (def is not Src { Expr: NonLogicalBinaryExpression { Operator: Operator.Addition } or TemplateLiteral } || !Stringy(def)) break;
                        var opened = new List<Part>();
                        Walk(def, opened);
                        Merge(opened);
                        if (opened.Count <= 1) break;
                        parts.RemoveAt(i);
                        parts.InsertRange(i, opened);
                        i--;                                  // the first opened part goes through the cursor next
                        break;
                    }
                case Choice c:
                    {
                        var then = SplitStyles(c.Then, at.Clone(), depth);
                        SplitStyles(c.Else, at.Clone(), depth);
                        at = then;
                        break;
                    }
                case Rows r:
                    foreach (var row in r.Each) at = SplitStyles(row, at, depth);
                    break;
            }
        }
        Merge(parts);
        return at;
    }

    /// <summary>
    /// Where a position in markup is: in text, inside a start tag, in an attribute's value. Enough of
    /// HTML's tokenizer to place a hole and to see a start tag open and close - which is all the
    /// compiler asks of it.
    /// </summary>
    internal sealed class Cursor
    {
        internal enum At { Text, Name, Tag, Attr, AfterAttr, BeforeValue, Quoted, Unquoted, Close, Comment }

        public At State = At.Text;
        private char _quote;
        private readonly StringBuilder _name = new();
        private readonly StringBuilder _attr = new();
        /// <summary>The attribute whose value is being read, lower case.</summary>
        public string? Attribute;
        /// <summary>The tag being opened or last opened, lower case.</summary>
        public string Tag = string.Empty;
        /// <summary>Whether the start tag being read ends in `/&gt;`.</summary>
        public bool SelfClosing;

        /// <summary>
        /// Ends a tag name that runs into something other than text - `'&lt;div' + act(fn) + ...` -
        /// so the element can be named before the part that follows it. True when one was open.
        /// </summary>
        public bool FinishName()
        {
            if (State != At.Name) return false;
            Tag = _name.ToString().ToLowerInvariant();
            State = At.Tag;
            return true;
        }

        public Cursor Clone()
        {
            var c = new Cursor { State = State, _quote = _quote, Attribute = Attribute, Tag = Tag, SelfClosing = SelfClosing };
            c._name.Append(_name);
            c._attr.Append(_attr);
            return c;
        }

        /// <summary>What happened at one character: a tag name finished, a start tag closed, an end tag closed.</summary>
        internal enum Event { None, NameEnd, StartEnd, EndEnd, ValueStart, ValueEnd }

        /// <summary>Reads text, calling back at each event with the index it happened at.</summary>
        public void Feed(string text, Action<Event, int>? on = null)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                switch (State)
                {
                    case At.Text:
                        if (c != '<' || i + 1 >= text.Length) break;
                        var n = text[i + 1];
                        if (char.IsLetter(n)) { State = At.Name; _name.Clear(); SelfClosing = false; }
                        else if (n == '/') { State = At.Close; i++; }
                        else if (n == '!' && i + 3 < text.Length && text[i + 2] == '-' && text[i + 3] == '-') { State = At.Comment; i += 3; }
                        break;
                    case At.Name:
                        if (char.IsLetterOrDigit(c) || c == '-' || c == ':') { _name.Append(c); break; }
                        Tag = _name.ToString().ToLowerInvariant();
                        on?.Invoke(Event.NameEnd, i);
                        State = At.Tag;
                        i--;                                  // this character belongs to the tag
                        break;
                    case At.Tag:
                        if (char.IsWhiteSpace(c)) break;
                        if (c == '>') { State = At.Text; on?.Invoke(Event.StartEnd, i); break; }
                        if (c == '/') { SelfClosing = true; break; }
                        State = At.Attr; _attr.Clear(); _attr.Append(c);
                        break;
                    case At.Attr:
                        if (c == '=') { Attribute = _attr.ToString().ToLowerInvariant(); State = At.BeforeValue; break; }
                        if (char.IsWhiteSpace(c)) { State = At.AfterAttr; break; }
                        if (c == '>') { State = At.Text; on?.Invoke(Event.StartEnd, i); break; }
                        _attr.Append(c);
                        break;
                    case At.AfterAttr:
                        if (char.IsWhiteSpace(c)) break;
                        if (c == '=') { Attribute = _attr.ToString().ToLowerInvariant(); State = At.BeforeValue; break; }
                        if (c == '>') { State = At.Text; on?.Invoke(Event.StartEnd, i); break; }
                        State = At.Attr; _attr.Clear(); _attr.Append(c);
                        break;
                    case At.BeforeValue:
                        if (char.IsWhiteSpace(c)) break;
                        if (c == '"' || c == '\'') { _quote = c; State = At.Quoted; on?.Invoke(Event.ValueStart, i); break; }
                        if (c == '>') { State = At.Text; on?.Invoke(Event.StartEnd, i); break; }
                        State = At.Unquoted;
                        break;
                    case At.Quoted:
                        if (c == _quote) { State = At.Tag; on?.Invoke(Event.ValueEnd, i); }
                        break;
                    case At.Unquoted:
                        if (char.IsWhiteSpace(c)) State = At.Tag;
                        else if (c == '>') { State = At.Text; on?.Invoke(Event.StartEnd, i); }
                        break;
                    case At.Close:
                        if (c == '>') { State = At.Text; on?.Invoke(Event.EndEnd, i); }
                        break;
                    case At.Comment:
                        if (c == '>' && i >= 2 && text[i - 1] == '-' && text[i - 2] == '-') State = At.Text;
                        break;
                }
            }
        }
    }

    // ---- the union: every side and every row, written out once --------------------------------

    /// <summary>Where a hole sits in the markup.</summary>
    internal sealed class Place
    {
        /// <summary>The element whose text or start tag holds it; null for text directly in the target.</summary>
        public string? Element;
        /// <summary>The attribute whose value it is part of, lower case; null in text.</summary>
        public string? Attribute;
        /// <summary>Inside `style`: the property whose value it is part of, lower case; null when it is not inside one.</summary>
        public string? Property;
    }

    /// <summary>
    /// The markup with every side of every choice and every row a list can have, each start tag
    /// named, and where each hole, side, row and click handler landed.
    /// </summary>
    /// <remarks>
    /// This is the scene's structure: one document carrying everything the page can show, each
    /// alternative gated by its own visibility. The names are the point - `data-sn` is counted over
    /// the start tags in writing order, which depends only on the markup, so the same element has
    /// the same name in every layout the compiler makes and in the Lua that writes to it.
    /// </remarks>
    internal sealed class Union
    {
        public string Html = string.Empty;
        public readonly Dictionary<int, Place> Holes = new();
        /// <summary>Per choice: the elements each side opens at its own level, then those nested choices inside it open.</summary>
        public readonly Dictionary<int, (List<string> Then, List<string> Else)> Sides = new();
        /// <summary>Per list: the elements each row opens at its own level.</summary>
        public readonly Dictionary<int, List<List<string>>> Rows = new();
        /// <summary>Every click handler, with the element it belongs to, in writing order.</summary>
        public readonly List<(string Element, Push Push)> Clicks = new();
        public readonly Dictionary<string, string?> Parent = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Tag = new(StringComparer.Ordinal);
        /// <summary>
        /// For every element and every hole (keyed "#index"), the choices and rows around it,
        /// outermost first: (false, choice, 1 then / 0 else) or (true, list, row). What has to hold
        /// for it to be drawn, which is what a layout that shows it has to set up.
        /// </summary>
        public readonly Dictionary<string, (bool List, int Index, int Which)[]> Path = new(StringComparer.Ordinal);
        /// <summary>Every quoted attribute value, as literal text and the holes in it, by element and attribute.</summary>
        public readonly Dictionary<(string Element, string Attribute), List<(string? Text, int Hole)>> Attrs = new();
        public readonly List<string> Problems = new();

        /// <summary>An attribute's value written again, with each hole filled by <paramref name="fill"/>.</summary>
        public string? Attribute(string element, string attribute, Func<int, string> fill)
        {
            if (!Attrs.TryGetValue((element, attribute), out var pieces)) return null;
            var sb = new StringBuilder();
            foreach (var (text, hole) in pieces) sb.Append(text ?? fill(hole));
            return sb.ToString();
        }
    }

    /// <summary>The attribute that carries each element's compile-time name through the parser.</summary>
    internal const string NameAttribute = "data-sn";

    /// <summary>Writes the union, each hole filled by <paramref name="fill"/>.</summary>
    internal Union Write(string prefix, Func<Hole, Place, string> fill)
    {
        var u = new Union();
        var sb = new StringBuilder(16384);
        var cursor = new Cursor();
        var open = new List<string>();
        string? opening = null;
        var counter = 0;
        // The sides and rows being written, innermost last, with the depth their elements open at.
        var collectors = new List<(int Depth, List<string> Into)>();
        var path = new List<(bool List, int Index, int Which)>();
        // The attribute value being written: where it starts in the output, and the holes in it.
        var valueAt = -1;
        var valueHoles = new List<(int Start, int End, int Hole)>();

        WriteParts(Parts);
        u.Html = sb.ToString();
        return u;

        void Named()
        {
            var name = prefix + "_h" + (counter++).ToString(CultureInfo.InvariantCulture);
            sb.Append(' ').Append(NameAttribute).Append("=\"").Append(name).Append('"');
            opening = name;
            u.Tag[name] = cursor.Tag;
            u.Parent[name] = open.Count > 0 ? open[open.Count - 1] : null;
            u.Path[name] = path.ToArray();
            foreach (var (depth, into) in collectors)
                if (depth == open.Count) into.Add(name);
        }

        void Value(string element, string attribute)
        {
            var pieces = new List<(string? Text, int Hole)>();
            var at = valueAt;
            foreach (var (start, end, hole) in valueHoles)
            {
                if (start > at) pieces.Add((sb.ToString(at, start - at), -1));
                pieces.Add((null, hole));
                at = end;
            }
            if (sb.Length > at) pieces.Add((sb.ToString(at, sb.Length - at), -1));
            u.Attrs[(element, attribute)] = pieces;
        }

        void Text(string text)
        {
            var from = 0;
            cursor.Feed(text, (ev, i) =>
            {
                switch (ev)
                {
                    case Cursor.Event.NameEnd:
                        sb.Append(text, from, i - from);
                        from = i;
                        Named();
                        break;
                    case Cursor.Event.StartEnd:
                        if (opening != null && !cursor.SelfClosing && !HtmlParser.Void.Contains(cursor.Tag)) open.Add(opening);
                        opening = null;
                        break;
                    case Cursor.Event.EndEnd:
                        if (open.Count > 0) open.RemoveAt(open.Count - 1);
                        break;
                    case Cursor.Event.ValueStart:
                        sb.Append(text, from, i + 1 - from);
                        from = i + 1;
                        valueAt = sb.Length;
                        valueHoles.Clear();
                        break;
                    case Cursor.Event.ValueEnd:
                        sb.Append(text, from, i - from);
                        from = i;
                        if (valueAt >= 0 && opening != null && cursor.Attribute != null) Value(opening, cursor.Attribute);
                        valueAt = -1;
                        break;
                }
            });
            sb.Append(text, from, text.Length - from);
        }

        void WriteParts(List<Part> parts)
        {
            foreach (var part in parts)
            {
                if (part is not Fixed && cursor.FinishName()) Named();
                switch (part)
                {
                    case Fixed f:
                        Text(f.Text);
                        break;
                    case Hole h:
                        {
                            var place = new Place();
                            if (cursor.State is Cursor.At.Quoted or Cursor.At.Unquoted)
                            {
                                place.Element = opening;
                                place.Attribute = cursor.Attribute;
                                if (cursor.Attribute == "style") place.Property = StyleProperty(sb);
                            }
                            else place.Element = open.Count > 0 ? open[open.Count - 1] : null;
                            u.Holes[h.Index] = place;
                            u.Path["#" + h.Index.ToString(CultureInfo.InvariantCulture)] = path.ToArray();
                            var start = sb.Length;
                            sb.Append(fill(h, place));
                            if (valueAt >= 0 && cursor.State == Cursor.At.Quoted) valueHoles.Add((start, sb.Length, h.Index));
                            break;
                        }
                    case Push p:
                        {
                            var owner = opening ?? (open.Count > 0 ? open[open.Count - 1] : null);
                            if (owner == null) { u.Problems.Add("a click handler registered outside any element"); break; }
                            // A registered handler makes its element a click region, which is what the
                            // vector mod needs to report a press on it at all.
                            if (opening != null && cursor.State is Cursor.At.Tag or Cursor.At.AfterAttr)
                                sb.Append(" data-click=\"1\"");
                            u.Clicks.Add((owner, p));
                            break;
                        }
                    case Choice c:
                        {
                            var then = new List<string>();
                            var otherwise = new List<string>();
                            path.Add((false, c.Index, 1));
                            Side(c.Then, then);
                            path[path.Count - 1] = (false, c.Index, 0);
                            Side(c.Else, otherwise);
                            path.RemoveAt(path.Count - 1);
                            u.Sides[c.Index] = (then, otherwise);
                            break;
                        }
                    case Rows r:
                        {
                            var rows = new List<List<string>>();
                            for (var k = 0; k < r.Each.Count; k++)
                            {
                                var roots = new List<string>();
                                path.Add((true, r.Index, k));
                                Side(r.Each[k], roots);
                                path.RemoveAt(path.Count - 1);
                                rows.Add(roots);
                            }
                            u.Rows[r.Index] = rows;
                            break;
                        }
                }
            }
        }

        void Side(List<Part> parts, List<string> roots)
        {
            var depth = open.Count;
            collectors.Add((depth, roots));
            WriteParts(parts);
            collectors.RemoveAt(collectors.Count - 1);
            if (open.Count != depth || cursor.State != Cursor.At.Text)
                u.Problems.Add("a choice between pieces of markup that do not each open and close their own elements");
        }
    }

    /// <summary>The CSS property the style attribute being written has reached: `width` in `...;width:`.</summary>
    private static string? StyleProperty(StringBuilder sb)
    {
        // Back to the quote that opened the attribute, then forward to the declaration in progress.
        var i = sb.Length - 1;
        while (i >= 0 && sb[i] != '"' && sb[i] != '\'') i--;
        var decl = i + 1;
        for (var k = sb.Length - 1; k > i; k--)
            if (sb[k] == ';') { decl = k + 1; break; }
        var colon = -1;
        for (var k = decl; k < sb.Length; k++)
            if (sb[k] == ':') { colon = k; break; }
        if (colon < 0) return null;
        return sb.ToString(decl, colon - decl).Trim().ToLowerInvariant();
    }

    // ---- what the choices hinge on --------------------------------------------------------------

    /// <summary>
    /// One value of the page's state that decides several choices at once: `st.tab`, compared with
    /// 'atmo', 'supply', 'config'... in every test of the tab chain and of the header around it.
    /// </summary>
    /// <remarks>
    /// Without this the compiler would have to treat the header's `notConfig`, the five screens and
    /// the tab bar's `notConfig` as seven independent switches - 2^7 layouts, most of them a page no
    /// browser could ever show, and each laid out with the header still present above the settings
    /// screen it never shares a frame with. Read through the one value they all test, they are six
    /// layouts, every one of them real.
    /// </remarks>
    internal sealed class Drive
    {
        /// <summary>What the chunk reads to know which layout is showing.</summary>
        public Term Path = null!;
        public string Text = string.Empty;
        /// <summary>Every literal a test compares it with; a value matching none is its own layout.</summary>
        public readonly List<string> Values = new();
        public readonly Dictionary<int, (string Literal, bool Equal)> Tests = new();

        /// <summary>Which side a choice takes for a value; null is "none of the literals".</summary>
        public bool Side(int choice, string? value)
        {
            var (literal, equal) = Tests[choice];
            return string.Equals(value, literal, StringComparison.Ordinal) == equal;
        }
    }

    /// <summary>The value that decides the most choices, or null when no choice tests one.</summary>
    internal Drive? Driver()
    {
        var groups = new Dictionary<string, Drive>(StringComparer.Ordinal);
        foreach (var c in Choices)
        {
            if (Reduce(c.Test, false, 0) is not { } r) continue;
            if (!groups.TryGetValue(r.Text, out var d)) groups[r.Text] = d = new Drive { Path = r.Path, Text = r.Text };
            d.Tests[c.Index] = (r.Literal, r.Equal);
            if (!d.Values.Contains(r.Literal)) d.Values.Add(r.Literal);
        }
        Drive? best = null;
        foreach (var d in groups.Values)
            if (best == null || d.Tests.Count > best.Tests.Count) best = d;
        return best;
    }

    /// <summary>A test as `path === 'literal'` (or `!==`), through the names that stand for it.</summary>
    private (Term Path, string Text, string Literal, bool Equal)? Reduce(Term t, bool negate, int depth)
    {
        if (depth > 32 || t is not Src s) return null;
        switch (s.Expr)
        {
            case ParenthesizedExpression p:
                return Reduce(new Src(p.Expression, s.Env), negate, depth + 1);
            case NonUpdateUnaryExpression { Operator: Operator.LogicalNot } not:
                return Reduce(new Src(not.Argument, s.Env), !negate, depth + 1);
            case NonLogicalBinaryExpression { Operator: Operator.StrictEquality or Operator.StrictInequality or Operator.Equality or Operator.Inequality } b:
                {
                    var equal = b.Operator is Operator.StrictEquality or Operator.Equality;
                    Expression? other = null;
                    string? literal = null;
                    if (EvalExpr(b.Right, s.Env) is SLit { V: string r }) { literal = r; other = b.Left; }
                    else if (EvalExpr(b.Left, s.Env) is SLit { V: string l }) { literal = l; other = b.Right; }
                    if (literal == null || other == null) return null;
                    // The chunk reads the value itself, so it has to be something the page keeps: a
                    // chain of fields off one of the page's own globals that the page writes to.
                    var text = other switch { MemberExpression m => Path(m), Identifier id => id.Name, _ => null };
                    if (text == null || Root(other) is not { } root || !_mutable.Contains(root)) return null;
                    if ((s.Env ?? _globals).Find(root, out var live) == null || !live || !IsGlobal(s.Env, root)) return null;
                    return (new Src(other, s.Env), text, literal, equal != negate);
                }
            case Identifier or MemberExpression:
                return Definition(t) is { } def && def != t ? Reduce(def, negate, depth + 1) : null;
        }
        return null;
    }

    // ---- bounds of the page's own lists --------------------------------------------------------

    private readonly Dictionary<string, int> _bounds = new(StringComparer.Ordinal);

    /// <summary>
    /// The longest a list the page keeps in its state can get: `st.log`, which starts with three
    /// entries and is trimmed to seven every time something is logged. -1 when nothing bounds it.
    /// </summary>
    private int Bound(string path)
    {
        if (_bounds.TryGetValue(path, out var known)) return known;
        _bounds[path] = -1;                           // a list defined in terms of itself stops here
        var max = 0;
        var seen = false;
        foreach (var n in Everything(_script))
        {
            switch (n)
            {
                // the value it was declared with, where the path is a field of a declared object
                case VariableDeclarator { Id: Identifier root, Init: ObjectExpression init } when path.StartsWith(root.Name + ".", StringComparison.Ordinal):
                    {
                        var field = path.Substring(root.Name.Length + 1);
                        if (field.IndexOf('.') >= 0) break;
                        var o = new SObj(); o.Layers.Add((init, _globals));
                        if (PropertyTerm(o, field) is Src { Expr: ArrayExpression a }) { max = Math.Max(max, a.Elements.Count); seen = true; }
                        break;
                    }
                case AssignmentExpression { Left: MemberExpression left } a when Path(left) == path:
                    {
                        var n2 = LengthOf(a.Right, path);
                        if (n2 < 0) return _bounds[path] = -1;
                        max = Math.Max(max, n2);
                        seen = true;
                        break;
                    }
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier p, Object: MemberExpression o } }
                    when p.Name is "push" or "unshift" or "splice" && Path(o) == path:
                    return _bounds[path] = -1;
            }
        }
        return _bounds[path] = seen ? max : -1;
    }

    /// <summary>The longest an assigned list can be: `[...].concat(st.log).slice(0, 7)` is 7.</summary>
    private static int LengthOf(Expression e, string self)
    {
        switch (e)
        {
            case ArrayExpression a:
                foreach (var el in a.Elements) if (el is SpreadElement) return -1;
                return a.Elements.Count;
            case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "slice" }, Object: { } o } } c:
                {
                    if (c.Arguments.Count == 2 && c.Arguments[0] is NumericLiteral from && c.Arguments[1] is NumericLiteral to && to.Value >= from.Value && from.Value >= 0)
                        return (int)(to.Value - from.Value);
                    return LengthOf(o, self);
                }
            case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "filter" or "map" }, Object: { } o } }:
                return LengthOf(o, self);
            case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier { Name: "concat" }, Object: { } o } } c:
                {
                    var n = LengthOf(o, self);
                    if (n < 0) return -1;
                    foreach (var a in c.Arguments)
                    {
                        var m = a is Expression x ? LengthOf(x, self) : -1;
                        if (m < 0) return -1;
                        n += m;
                    }
                    return n;
                }
            case MemberExpression m when Path(m) == self:
                return -1;                            // the list itself, unbounded unless sliced
        }
        return -1;
    }

    /// <summary>`st.log` as text, for a plain chain of names; null for anything else.</summary>
    internal static string? Path(MemberExpression m)
    {
        if (m.Computed || m.Property is not Identifier p) return null;
        return m.Object switch
        {
            Identifier id => id.Name + "." + p.Name,
            MemberExpression inner when Path(inner) is { } head => head + "." + p.Name,
            _ => null,
        };
    }

    // ---- structure -----------------------------------------------------------------------------

    /// <summary>Whether parts change the shape of the markup rather than a value inside it.</summary>
    internal static bool Structural(List<Part> parts)
    {
        foreach (var part in parts)
            switch (part)
            {
                case Fixed f when HasTag(f.Text): return true;
                case Push: return true;
                case Choice: return true;
                case Rows: return true;
            }
        return false;
    }

    private static bool HasTag(string s)
    {
        for (var i = 0; i + 1 < s.Length; i++)
            if (s[i] == '<' && (char.IsLetter(s[i + 1]) || s[i + 1] == '/')) return true;
        return false;
    }

    /// <summary>Joins neighbouring fixed text, so the skeleton is markup rather than fragments.</summary>
    private static void Merge(List<Part> parts)
    {
        for (var i = parts.Count - 2; i >= 0; i--)
        {
            if (parts[i] is not Fixed a || parts[i + 1] is not Fixed b) continue;
            parts[i] = new Fixed(a.Text + b.Text);
            parts.RemoveAt(i + 1);
        }
    }

    // ---- an element per value ----------------------------------------------------------------

    /// <summary>
    /// A test made by the compiler rather than written by the page: the value is this literal.
    /// </summary>
    internal sealed class Is : Term
    {
        public readonly Term Value;
        public readonly string Literal;
        public Is(Term value, string literal) { Value = value; Literal = literal; }
    }

    /// <summary>
    /// Writes each element whose start tag holds one of <paramref name="targets"/> once per value,
    /// under a choice the page makes.
    /// </summary>
    /// <remarks>
    /// A shadow that is `none` in one state and an inset line in the other is not a different value
    /// on one shape: the scene carries a shadow as part of the shape's own description, read once.
    /// So the element is written twice, each copy with its own fixed style, and the chunk shows the
    /// one the page's value picks - which is exactly what the choice machinery already does for a
    /// ternary between two pieces of markup. Holes that hang on the same test (`on ? a : b` three
    /// times in one tag) vary together, so they make one choice, not three.
    /// </remarks>
    private void Expand(List<Part> parts, HashSet<Hole> targets)
    {
        for (var guard = 0; guard < 256; guard++)
        {
            if (!ExpandOne(parts, targets)) break;
        }
        foreach (var part in parts)
            switch (part)
            {
                case Choice c: Expand(c.Then, targets); Expand(c.Else, targets); break;
                case Rows r: foreach (var row in r.Each) Expand(row, targets); break;
            }
    }

    /// <summary>Expands the first element in this list whose start tag holds a target. False when there is none.</summary>
    private bool ExpandOne(List<Part> parts, HashSet<Hole> targets)
    {
        // The cursor's events place each start tag; the first holding a target is taken with
        // everything up to its end tag.
        var cursor = new Cursor();
        (int Part, int At) tagStart = default;
        (int Part, int At)? open = null;
        (int Part, int At)? end = null;
        var depthAtOpen = -1;
        var depth = 0;
        List<Hole>? inTag = null;
        List<Hole>? found = null;
        for (var p = 0; p < parts.Count && end == null; p++)
        {
            switch (parts[p])
            {
                case Fixed f:
                    {
                        var text = f.Text;
                        var here = p;
                        cursor.Feed(text, (ev, i) =>
                        {
                            if (end != null) return;
                            switch (ev)
                            {
                                case Cursor.Event.NameEnd:
                                    tagStart = (here, Math.Max(0, text.LastIndexOf('<', Math.Max(0, i - 1))));
                                    inTag = new List<Hole>();
                                    break;
                                case Cursor.Event.StartEnd:
                                    {
                                        var isVoid = cursor.SelfClosing || HtmlParser.Void.Contains(cursor.Tag);
                                        if (found == null && inTag is { Count: > 0 })
                                        {
                                            found = inTag;
                                            open = tagStart;
                                            depthAtOpen = depth;
                                            if (isVoid) { end = (here, i + 1); return; }
                                        }
                                        if (!isVoid) depth++;
                                        inTag = null;
                                        break;
                                    }
                                case Cursor.Event.EndEnd:
                                    depth--;
                                    if (found != null && depth == depthAtOpen) end = (here, i + 1);
                                    break;
                            }
                        });
                        break;
                    }
                case Hole h when inTag != null && targets.Contains(h):
                    inTag.Add(h);
                    break;
            }
            if (parts[p] is not Fixed && cursor.FinishName())
            {
                // a tag name ending right at a hole or a registration: the tag began in an earlier part
                for (var q = p - 1; q >= 0; q--)
                    if (parts[q] is Fixed pf && pf.Text.LastIndexOf('<') is var lt && lt >= 0) { tagStart = (q, lt); break; }
                inTag = new List<Hole>();
                if (parts[p] is Hole h2 && targets.Contains(h2)) inTag.Add(h2);
            }
        }
        if (open == null || end == null || found == null) return false;
        Replace(parts, open.Value, end.Value, found);
        return true;
    }

    /// <summary>The element between two positions, written once per value of its target holes.</summary>
    private void Replace(List<Part> parts, (int Part, int At) from, (int Part, int At) to, List<Hole> holes)
    {
        // Cut the element out: split the fixed text at each end so it is whole parts.
        var element = new List<Part>();
        var before = new List<Part>();
        var after = new List<Part>();
        for (var p = 0; p < parts.Count; p++)
        {
            if (p < from.Part) { before.Add(parts[p]); continue; }
            if (p > to.Part) { after.Add(parts[p]); continue; }
            if (parts[p] is Fixed f)
            {
                var start = p == from.Part ? from.At : 0;
                var end = p == to.Part ? to.At : f.Text.Length;
                if (p == from.Part && start > 0) before.Add(new Fixed(f.Text.Substring(0, start)));
                element.Add(new Fixed(f.Text.Substring(start, end - start)));
                if (p == to.Part && end < f.Text.Length) after.Add(new Fixed(f.Text.Substring(end)));
            }
            else element.Add(parts[p]);
        }

        // One variable per test the holes hang on; a hole with no shared test is its own.
        var variables = new List<(Term? Test, List<(Hole Hole, string Then, string Else)> Paired, Hole? Alone, List<string>? Values)>();
        foreach (var h in holes)
        {
            if (h.Value is Src { Expr: ConditionalExpression c } s
                && Enumerate(new Src(c.Consequent, s.Env)) is { Count: 1 } yes
                && Enumerate(new Src(c.Alternate, s.Env)) is { Count: 1 } no)
            {
                var key = Text(new Src(c.Test, s.Env));
                var at = variables.FindIndex(v => v.Test is Src ts && Text(ts) == key && ReferenceEquals(ts.Env, s.Env));
                if (at < 0) variables.Add((new Src(c.Test, s.Env), new List<(Hole, string, string)> { (h, yes[0], no[0]) }, null, null));
                else variables[at].Paired.Add((h, yes[0], no[0]));
                continue;
            }
            variables.Add((null, new List<(Hole, string, string)>(), h, Enumerate(h.Value) ?? new List<string>()));
        }

        var built = Build(0, new Dictionary<Hole, string>());
        parts.Clear();
        parts.AddRange(before);
        parts.AddRange(built);
        parts.AddRange(after);
        Merge(parts);

        // The element, with every assigned hole fixed, as a choice over the next variable's values.
        List<Part> Build(int v, Dictionary<Hole, string> fixedValues)
        {
            if (v == variables.Count) return Copy(element, fixedValues);
            var (test, paired, alone, values) = variables[v];
            if (test != null)
            {
                var yes = new Dictionary<Hole, string>(fixedValues);
                var no = new Dictionary<Hole, string>(fixedValues);
                foreach (var (h, a, b) in paired) { yes[h] = a; no[h] = b; }
                return new List<Part> { new Choice(test, Build(v + 1, yes), Build(v + 1, no)) { Expanded = true } };
            }
            // A value from a fixed set: the last one is what is left when none of the others is.
            List<Part> chain = new();
            for (var k = values!.Count - 1; k >= 0; k--)
            {
                var one = new Dictionary<Hole, string>(fixedValues) { [alone!] = values[k] };
                var copy = Build(v + 1, one);
                chain = k == values.Count - 1 ? copy : new List<Part> { new Choice(new Is(alone!.Value, values[k]), copy, chain) { Expanded = true, Depth = k } };
            }
            return chain;
        }
    }

    /// <summary>A deep copy of parts, with some holes replaced by the text they are fixed to.</summary>
    private static List<Part> Copy(List<Part> parts, Dictionary<Hole, string> fixedValues)
    {
        var copy = new List<Part>(parts.Count);
        foreach (var part in parts)
            switch (part)
            {
                case Hole h when fixedValues.TryGetValue(h, out var text): copy.Add(new Fixed(text)); break;
                case Hole h: copy.Add(new Hole(h.Value)); break;
                case Push p: copy.Add(new Push(p.Handler)); break;
                case Choice c: copy.Add(new Choice(c.Test, Copy(c.Then, fixedValues), Copy(c.Else, fixedValues)) { Expanded = c.Expanded, Depth = c.Depth }); break;
                case Rows r:
                    {
                        var rows = new Rows(r.List);
                        rows.Items.AddRange(r.Items);
                        foreach (var row in r.Each) rows.Each.Add(Copy(row, fixedValues));
                        copy.Add(rows);
                        break;
                    }
                default: copy.Add(part); break;
            }
        Merge(copy);
        return copy;
    }

    /// <summary>Numbers every hole, choice and list in document order, which is what the chunk and the slots agree on.</summary>
    private void Number(List<Part> parts)
    {
        foreach (var part in parts)
            switch (part)
            {
                case Hole h: h.Index = Holes.Count; Holes.Add(h); break;
                case Choice c:
                    c.Index = Choices.Count; Choices.Add(c);
                    Merge(c.Then); Merge(c.Else);
                    Number(c.Then); Number(c.Else);
                    break;
                case Rows r:
                    r.Index = Lists.Count; Lists.Add(r);
                    foreach (var row in r.Each) Number(row);
                    break;
            }
    }

    // ---- small things ----------------------------------------------------------------------------

    /// <summary>A term's source, for a message the page's author can find.</summary>
    // ---- what the chunk evaluates ------------------------------------------------------------

    /// <summary>Where the assignment this was reduced from starts in the page's script: which write it is.</summary>
    internal int Start { get; private set; }

    /// <summary>
    /// A term as JavaScript the chunk can run where the markup is written, or null.
    /// </summary>
    /// <remarks>
    /// Every name that stood for something only while the markup was being worked out - a helper's
    /// parameter, a helper's local - is replaced by what it stands for, and a field of an object the
    /// page wrote as a literal (<c>o.status</c> with <c>o</c> a helper's argument) is read off it here,
    /// so nothing is built at run time to be read once. What is left names only things that exist
    /// where the markup is written: the page's globals and the locals of the function writing it.
    /// Null when the value would need an object or a function built at run time, which a value read
    /// every render must not do.
    /// </remarks>
    internal string? Js(Term t) => Js(t, null);

    /// <summary>While a click handler is written: what a local of the writing function, or a row, is kept in until the press.</summary>
    private Func<string, string>? _kept;

    private string? Js(Term t, HashSet<string>? shadow)
    {
        if (++_depth > MaxDepth) { _depth--; return null; }
        try
        {
            if (t is not Row && Eval(t) is SLit l) return Literal(l.V);
            switch (t)
            {
                case Const c: return ReferenceEquals(c.Value, DynamicMarker) ? null : Literal(c.Value);
                case Src s: return JsOf(s.Expr, s.Env, shadow);
                case Row r: return _kept != null ? _kept(r.Lua) : r.Lua;
                case Elem e:
                    // An element of a list the page wrote as a literal is read off it now; of one a
                    // name holds at run time, read there - the page already built it.
                    if (StaticTerm(e.List) && Eval(e.List) is SArr a) return e.Index < a.Items.Count ? Js(a.Items[e.Index], shadow) : "undefined";
                    return Js(e.List, shadow) is { } list ? "(" + list + ")[" + e.Index.ToString(CultureInfo.InvariantCulture) + "]" : null;
                case Applied ap:
                    {
                        var (result, _) = Inline(ap.Fn, ap.Args);
                        return result == null ? null : Js(result, shadow);
                    }
                case Picked p:
                    if (Js(p.Of, shadow) is not { } of) return null;
                    return p.Key switch
                    {
                        string k => "(" + of + ")." + k,
                        double d => "(" + of + ")[" + JsString(d) + "]",
                        _ => null,
                    };
                case Is i:
                    return Js(i.Value, shadow) is { } v ? "((" + v + ") === " + Literal(i.Literal) + ")" : null;
            }
            return null;
        }
        finally { _depth--; }
    }

    /// <summary>
    /// A click handler's body as JavaScript statements the chunk can run when the element is pressed,
    /// or null. The handler's own parameters and locals stay names; everything else is replaced as
    /// <see cref="Js(Term)"/> does.
    /// </summary>
    /// <param name="kept">
    /// The name a local of the writing function, or a row, is kept under until the press: the
    /// handler runs later, where those names no longer exist.
    /// </param>
    internal string? Handler(Term handler, Func<string, string> kept)
    {
        _kept = kept;
        try { return HandlerBody(handler); }
        finally { _kept = null; }
    }

    private string? HandlerBody(Term handler)
    {
        if (Eval(handler) is not SFn f || f.Fn.Body is null)
            return Js(handler) is { } value ? "(" + value + ")();" : null;
        // The handler's own names: its parameters, and whatever its body declares.
        var shadow = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in f.Fn.Params) foreach (var n in Everything(p)) if (n is Identifier id) shadow.Add(id.Name);
        foreach (var n in Everything(f.Fn.Body))
        {
            if (n is VariableDeclarator { Id: var target }) foreach (var d in Everything(target)) if (d is Identifier did) shadow.Add(did.Name);
            if (n is FunctionDeclaration { Id: { } fid }) shadow.Add(fid.Name);
            if (n is IFunction inner && inner != f.Fn) foreach (var p in inner.Params) foreach (var d in Everything(p)) if (d is Identifier pid) shadow.Add(pid.Name);
            if (n is CatchClause { Param: { } cp }) foreach (var d in Everything(cp)) if (d is Identifier cid) shadow.Add(cid.Name);
        }
        if (f.Fn.Body is Expression body) return JsOf(body, f.Env, shadow) is { } e ? e + ";" : null;
        if (f.Fn.Body is not BlockStatement block) return null;
        var sb = new StringBuilder();
        foreach (var s in block.Body)
        {
            if (JsOf(s, f.Env, shadow) is not { } text) return null;
            sb.Append(text).Append('\n');
        }
        return sb.ToString();
    }

    private string? JsOf(Node node, Env? env, HashSet<string>? shadow)
    {
        if (_source == null) return null;
        // Folded when it is known before the page runs - but not a statement's worth of work in a
        // handler, where a call or an assignment is done for what it does, not what it returns.
        if (node is Expression x && node is not (Identifier or AssignmentExpression or UpdateExpression)
            && (shadow == null || node is not CallExpression && !Mentions(node, shadow)) && EvalExpr(x, env) is SLit l)
            return Literal(l.V);
        switch (node)
        {
            case Identifier id:
                {
                    if (shadow != null && shadow.Contains(id.Name)) return id.Name;
                    Term? bound = null;
                    var live = true;
                    if (env != null) bound = env.Find(id.Name, out live);
                    if (bound != null && !live) return Js(bound, null) is { } v ? "(" + v + ")" : null;
                    // A name that exists where the markup is written; one of the writing function's
                    // own is gone by the time a handler runs, so a handler reads what it was kept in.
                    return _kept != null && IsLocal(id.Name) ? _kept(id.Name) : id.Name;
                }
            case ObjectProperty { Shorthand: true, Key: Identifier sk } sp:
                return JsOf(sp.Value, env, shadow) is { } sv ? sk.Name + ": " + sv : null;
            case MemberExpression m when Static(m.Object, env, shadow):
                {
                    // A field of an object or a list the page wrote as a literal: read off it now.
                    var obj = EvalExpr(m.Object, env);
                    var key = Key(m, env);
                    if (obj is SObj o && o.Closed && key != null)
                        return PropertyTerm(o, key is double kd ? JsString(kd) : (string)key) is { } pt ? Js(pt, null) is { } pv ? "(" + pv + ")" : null : "undefined";
                    if (obj is SArr a && key is double i)
                        return i >= 0 && i < a.Items.Count && i == Math.Floor(i) ? Js(a.Items[(int)i], null) is { } iv ? "(" + iv + ")" : null : "undefined";
                    if (obj is SArr al && key is "length") return al.Items.Count.ToString(CultureInfo.InvariantCulture);
                    break;
                }
            case CallExpression c:
                {
                    // One of the page's helpers held by a name that does not exist at run time, or a
                    // method of an object the page wrote as a literal: inlined.
                    if (Inlinable(c, env, shadow) is not { } callee) break;
                    var args = new Term[c.Arguments.Count];
                    for (var k = 0; k < args.Length; k++) args[k] = new Src(c.Arguments[k], env);
                    var (result, _) = Inline(callee, args);
                    return result == null ? null : Js(result, null) is { } rv ? "(" + rv + ")" : null;
                }
            // A function written inside a value - `ROLES.find((r) => r.key === st.role)` - keeps its
            // own names: a helper's `r` bound further out is not the `r` this function is handed.
            // What it computes, the page computed in the same render; only a value no name holds at
            // run time is computed again here, and only once per render.
            case IFunction fn:
                {
                    var inner = shadow == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(shadow, StringComparer.Ordinal);
                    foreach (var p in fn.Params) foreach (var d in Everything(p)) if (d is Identifier pid) inner.Add(pid.Name);
                    if (fn.Body != null)
                        foreach (var d in Everything(fn.Body))
                            if (d is VariableDeclarator { Id: var target }) foreach (var t in Everything(target)) if (t is Identifier tid) inner.Add(tid.Name);
                    return Splice(node, env, inner);
                }
            case ClassExpression:
                return null;
        }
        return Splice(node, env, shadow);
    }

    /// <summary>Whether an object expression stands for something the page wrote as a literal and no name holds at run time.</summary>
    private bool Static(Expression e, Env? env, HashSet<string>? shadow)
    {
        switch (e)
        {
            case Identifier id:
                {
                    if (shadow != null && shadow.Contains(id.Name)) return false;
                    if (env == null) return false;
                    var bound = env.Find(id.Name, out var live);
                    // A helper's parameter holding a name that does exist - `renderAtmo(v)` - is that
                    // name: its fields are read at run time, where the page already computed them.
                    return bound != null && !live && StaticTerm(bound);
                }
            case ParenthesizedExpression p: return Static(p.Expression, env, shadow);
            case MemberExpression m: return Static(m.Object, env, shadow);
            case ObjectExpression or ArrayExpression: return true;
            case CallExpression c: return Inlinable(c, env, shadow) != null;
        }
        return false;
    }

    private bool StaticTerm(Term t) => t switch
    {
        Src s => Static(s.Expr, s.Env, null),
        Elem e => StaticTerm(e.List),
        Const or Applied or FnTerm => true,
        _ => false,
    };

    /// <summary>
    /// The function a call runs, when no name holds it at run time: one of the page's helpers passed
    /// in or declared inside another, or a method of an object the page wrote as a literal.
    /// </summary>
    private SFn? Inlinable(CallExpression c, Env? env, HashSet<string>? shadow)
    {
        if (shadow != null && Mentions(c.Callee, shadow)) return null;
        if (c.Callee is MemberExpression { Computed: false, Property: Identifier mp } me)
            return Static(me.Object, env, shadow) && EvalExpr(me.Object, env) is SObj owner
                   && PropertyTerm(owner, mp.Name) is { } ft && Written(ft) && Eval(ft) is SFn method ? method : null;
        if (c.Callee is not Identifier ci || env == null) return null;
        var bound = env.Find(ci.Name, out var live);
        return bound != null && !live && Written(bound) ? Eval(bound) as SFn : null;

        // A function written right there, not one a name that exists at run time hands over: a
        // page's `onSelect: v.setTab` is called as it is, not inlined - it does things, and returns
        // nothing a value could be read from.
        static bool Written(Term t) => t is FnTerm || t is Src { Expr: FunctionExpression or ArrowFunctionExpression };
    }

    private static bool Mentions(Node n, HashSet<string> names)
    {
        foreach (var d in Everything(n)) if (d is Identifier id && names.Contains(id.Name)) return true;
        return false;
    }

    /// <summary>The node's own source, each part that is a value replaced as <see cref="JsOf"/> replaces it.</summary>
    private string? Splice(Node node, Env? env, HashSet<string>? shadow)
    {
        var sb = new StringBuilder();
        var at = node.Range.Start;
        foreach (var child in ValueParts(node))
        {
            if (child.Range.Start < at || child.Range.End > node.Range.End) return null;
            if (JsOf(child, env, shadow) is not { } text) return null;
            sb.Append(_source, at, child.Range.Start - at);
            // A value in a place that takes a value, parenthesised so it binds as one.
            sb.Append(child is Expression && text != _source!.Substring(child.Range.Start, child.Range.End - child.Range.Start) ? "(" + text + ")" : text);
            at = child.Range.End;
        }
        sb.Append(_source, at, node.Range.End - at);
        return sb.ToString();
    }

    /// <summary>The children of a node that are values or statements - not a declared name, a key or a label.</summary>
    private static IEnumerable<Node> ValueParts(Node node)
    {
        switch (node)
        {
            case MemberExpression m:
                yield return m.Object;
                if (m.Computed) yield return m.Property;
                yield break;
            case ObjectProperty p:
                if (p.Computed) yield return p.Key;
                yield return p.Value;
                yield break;
            case VariableDeclarator d:
                if (d.Init != null) yield return d.Init;
                yield break;
            case IFunction f:
                if (f.Body != null) yield return f.Body;
                yield break;
            case TemplateLiteral tpl:
                foreach (var e in tpl.Expressions) yield return e;
                yield break;
            case BreakStatement or ContinueStatement:
                yield break;
            case LabeledStatement l:
                yield return l.Body;
                yield break;
            case CatchClause cc:
                yield return cc.Body;
                yield break;
            case MethodDefinition or PropertyDefinition:
                yield break;
        }
        foreach (var c in node.ChildNodes) if (c != null) yield return c;
    }

    /// <summary>A value known before the page runs, as a JavaScript literal.</summary>
    private static string Literal(object? v) => v switch
    {
        null => "null",
        string s => JsQuote(s),
        bool b => b ? "true" : "false",
        double d when double.IsNaN(d) => "NaN",
        double d when double.IsInfinity(d) => d > 0 ? "Infinity" : "-Infinity",
        double d => JsString(d),
        _ => v == Undefined ? "undefined" : "undefined",
    };

    private static string JsQuote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < ' ' || ch == '\u2028' || ch == '\u2029') sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Whether a name the chunk reads where the markup is written is a local of the writing function rather than a global.</summary>
    internal bool IsLocal(string name) => Scope.Find(name, out var live) != null && live && !IsGlobal(Scope, name);

    internal string Text(Term t)
    {
        switch (t)
        {
            case Src s when _source != null && s.Expr.Range.End <= _source.Length:
                var text = _source.Substring(s.Expr.Range.Start, s.Expr.Range.End - s.Expr.Range.Start).Replace('\n', ' ');
                return text.Length <= 60 ? text : text.Substring(0, 59) + "~";
            case Src s:
                return "line " + s.Expr.Location.Start.Line.ToString(CultureInfo.InvariantCulture);
            case Elem e: return Text(e.List) + "[" + e.Index.ToString(CultureInfo.InvariantCulture) + "]";
            case Row r: return r.Lua;
            case Const c: return JsString(c.Value);
            case Is i: return Text(i.Value) + " is \"" + i.Literal + "\"";
        }
        return "a value";
    }

    private List<string>? _literals;

    /// <summary>Every string literal in the page, which is every colour a colour hole with no fixed set could be written with.</summary>
    internal List<string> Literals()
    {
        if (_literals != null) return _literals;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        _literals = new List<string>();
        foreach (var n in Everything(_script))
            if (n is StringLiteral { Value: { Length: > 0 and < 64 } v } && seen.Add(v)) _literals.Add(v);
        return _literals;
    }

    /// <summary>A value as JavaScript would concatenate it.</summary>
    internal static string JsString(object? v) => v switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        double d when double.IsNaN(d) => "NaN",
        double d when double.IsPositiveInfinity(d) => "Infinity",
        double d when double.IsNegativeInfinity(d) => "-Infinity",
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        _ => v == Undefined ? "undefined" : v.ToString() ?? string.Empty,
    };

    internal static string Line(Node n)
        => "line " + n.Location.Start.Line.ToString(CultureInfo.InvariantCulture) + ": ";

    internal static IEnumerable<Node> Everything(Node n)
    {
        yield return n;
        foreach (var child in n.ChildNodes)
        {
            if (child == null) continue;
            foreach (var d in Everything(child)) yield return d;
        }
    }
}

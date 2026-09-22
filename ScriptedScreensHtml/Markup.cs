using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Acornima.Ast;

namespace ScriptedScreensHtml;

/// <summary>
/// The markup a page builds, as the fixed parts and the holes in them.
/// </summary>
/// <remarks>
/// <c>innerHTML</c> is the last thing standing between the compiler and the pages that matter: seven
/// of the corpus's fifty, and every dashboard shape. A page that writes it cannot compile at all
/// today, so it runs the whole interpreter every frame for ever.
///
/// <b>What compiling it must NOT mean.</b> The obvious translation is to emit Lua that builds the
/// same string and hands it to the same parser. That moves the work rather than removing it - the
/// chip would allocate a 21 KB string every tick and the page would still be re-parsed - and it is
/// the exact mistake this project's notes warn about. Compiling means the concatenation disappears:
/// the string LITERALS in the builder are structure, emitted once as scene nodes, and the
/// interpolated expressions are slots the chip writes numbers and text into.
///
/// So this reads the expression assigned to <c>innerHTML</c> and separates those two. The literals
/// join into markup with a sentinel where each hole was; that markup parses and lays out exactly as
/// page source does, and wherever a sentinel lands - a text node, an attribute, a style property -
/// is the slot that hole binds to.
///
/// <b>It is static.</b> Nothing is executed. A page's own string-returning helpers are inlined by
/// substituting their arguments, because splitting a builder across four functions is how anyone
/// writes one and stopping at the call would find no structure at all. What it cannot reduce it
/// REPORTS, with the line - a page that compiles and then draws the wrong markup would be far worse
/// than one that says which expression stopped it.
/// </remarks>
internal sealed class Markup
{
    /// <summary>A piece of the markup: fixed text, a value, a choice between shapes, or a list.</summary>
    internal abstract class Part;

    /// <summary>Markup the page always writes. This is what becomes scene structure.</summary>
    internal sealed class Fixed : Part
    {
        public readonly string Text;
        public Fixed(string text) { Text = text; }
        public override string ToString() => Text;
    }

    /// <summary>A value the page computes. This is what becomes a slot.</summary>
    internal sealed class Hole : Part
    {
        /// <summary>The expression, kept so the compiler can emit the Lua that produces it.</summary>
        public readonly Expression Source;
        /// <summary>Its index in <see cref="Markup.Holes"/>, which is what the sentinel carries.</summary>
        public readonly int Index;
        public Hole(Expression source, int index) { Source = source; Index = index; }
        public override string ToString() => "${" + Index.ToString(CultureInfo.InvariantCulture) + "}";
    }

    /// <summary>
    /// Two shapes, one test. A tab bar, an alarm panel that appears, a row that is only drawn when
    /// something is wrong: every page has these, and they are different STRUCTURE rather than a
    /// different value - so each side is laid out and emitted, and the page picks between them.
    /// </summary>
    internal sealed class Choice : Part
    {
        public readonly Expression Test;
        public readonly List<Part> Then;
        public readonly List<Part> Else;
        public Choice(Expression test, List<Part> then, List<Part> otherwise) { Test = test; Then = then; Else = otherwise; }
    }

    /// <summary>
    /// A callback the page registers while building markup, and the index it gets.
    /// </summary>
    /// <remarks>
    /// Every page of any size has one helper like this:
    /// <code>const act = (fn) =&gt; { acts.push(fn); return ' data-act="' + (acts.length - 1) + '"'; };</code>
    /// It builds markup AND wires a click, which is why a purely functional reduction cannot touch
    /// it - and it was 91 of the 109 things blocking the corpus, all from that one line.
    ///
    /// The index is positional in emission order, so it is known at compile time - but only while the
    /// markup is being written out, not while it is being analysed: a row inside a repeat gets a
    /// different index on each pass. So the push is a zero-width part that advances a counter when
    /// the skeleton is written, and the index is a part that reads that counter. Both then fall out
    /// correctly for a repeated row, which folding a number during analysis would get wrong for every
    /// row but the first.
    /// </remarks>
    internal sealed class Push : Part
    {
        public readonly string List;
        /// <summary>What is being registered - the handler, kept for the compiler to bind.</summary>
        public readonly Expression Value;
        public Push(string list, Expression value) { List = list; Value = value; }
    }

    /// <summary>How many things have been registered so far, less an offset. See <see cref="Push"/>.</summary>
    internal sealed class Counter : Part
    {
        public readonly string List;
        public readonly int Less;
        public Counter(string list, int less) { List = list; Less = less; }
    }

    /// <summary>
    /// A list: <c>xs.map(x =&gt; '...').join('')</c>, which is how every page repeats a row.
    /// </summary>
    /// <remarks>
    /// The body is one shape, so the scene can carry it a bounded number of times and let the page
    /// drive how many are visible - which is a value, not a structure change. What the bound should
    /// be is the caller's decision; this only reports the shape.
    /// </remarks>
    internal sealed class Repeat : Part
    {
        public readonly Expression List;
        public readonly string Item;
        public readonly List<Part> Body;
        public Repeat(Expression list, string item, List<Part> body) { List = list; Item = item; Body = body; }
    }

    // ---- the analysis ----------------------------------------------------------------------------

    private readonly List<Expression> _holes = new();
    private readonly List<string> _problems = new();
    private readonly Dictionary<string, IFunction> _functions = new(StringComparer.Ordinal);
    /// <summary>Parameters bound while a helper is inlined, innermost last.</summary>
    private readonly List<Dictionary<string, Expression>> _bindings = new();
    /// <summary>Guards a helper that calls itself, directly or round a ring.</summary>
    private readonly HashSet<string> _inlining = new(StringComparer.Ordinal);
    /// <summary>Names currently being resolved, so one defined in terms of itself terminates.</summary>
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);
    /// <summary>Parameters of open helpers that the caller did not supply: undefined, not unknown.</summary>
    private readonly List<string> _unset = new();

    /// <summary>Whether an expression is known to be undefined here, so `a || b` is just b.</summary>
    private bool IsUndefined(Expression e)
        => e is Identifier id && _unset.Contains(id.Name) && Bound(id.Name) == null;

    /// <summary>Every hole, in the order their sentinels are numbered.</summary>
    internal IReadOnlyList<Expression> Holes => _holes;
    /// <summary>Why a page's markup could not be reduced, each with its line.</summary>
    internal IReadOnlyList<string> Problems => _problems;

    /// <summary>How deep a helper chain may go before a page is simply told it is too tangled.</summary>
    private const int MaxInline = 24;

    /// <summary>
    /// Reduces one <c>innerHTML</c> assignment to its parts.
    /// </summary>
    /// <param name="value">The expression assigned.</param>
    /// <param name="script">The whole script, for the helpers the expression calls.</param>
    internal static Markup Of(Expression value, Script script)
    {
        var m = new Markup();
        foreach (var node in Everything(script))
            if (node is FunctionDeclaration { Id.Name: { } name }) m._functions[name] = (FunctionDeclaration)node;
            else if (node is VariableDeclarator { Id: Identifier v, Init: ArrowFunctionExpression a }) m._functions[v.Name] = a;
            else if (node is VariableDeclarator { Id: Identifier v2, Init: FunctionExpression f }) m._functions[v2.Name] = f;
        m.Parts = m.Reduce(value);
        // A reduction that produced no markup at all is not a reduction: the page computed its
        // whole document somewhere this cannot see - a loop building a string, a call into
        // something opaque - and every literal tag went with it. Said here rather than left for the
        // caller to notice it got one hole and no structure.
        if (!Structural(m.Parts))
            m._problems.Add(Line(value) + "the markup is computed rather than built from literals, "
                            + "so there is no structure to emit");
        return m;
    }

    /// <summary>The markup, reduced.</summary>
    internal List<Part> Parts { get; private set; } = new();

    private List<Part> Reduce(Expression e)
    {
        var parts = new List<Part>();
        Walk(e, parts);
        Merge(parts);
        return parts;
    }

    private void Walk(Expression e, List<Part> into)
    {
        switch (e)
        {
            case StringLiteral s:
                into.Add(new Fixed(s.Value));
                return;

            case NonLogicalBinaryExpression { Operator: Acornima.Operator.Addition } add:
                Walk(add.Left, into);
                Walk(add.Right, into);
                return;

            case TemplateLiteral tpl:
                // `<div>${a}</div>` is the same shape written the other way: the quasis are fixed and
                // the expressions are holes, already separated by the parser.
                for (var i = 0; i < tpl.Quasis.Count; i++)
                {
                    into.Add(new Fixed(tpl.Quasis[i].Value.Cooked ?? tpl.Quasis[i].Value.Raw));
                    if (i < tpl.Expressions.Count) Walk(tpl.Expressions[i], into);
                }
                return;

            case ConditionalExpression c:
                {
                    var then = new List<Part>(); Walk(c.Consequent, then); Merge(then);
                    var otherwise = new List<Part>(); Walk(c.Alternate, otherwise); Merge(otherwise);
                    Branch(c, c.Test, then, otherwise, into);
                    return;
                }

            case LogicalExpression { Operator: Acornima.Operator.LogicalOr } either when IsUndefined(either.Left):
                Walk(either.Right, into);
                return;

            case LogicalExpression { Operator: Acornima.Operator.LogicalOr } fallback:
                {
                    var supplied = new List<Part>(); Walk(fallback.Left, supplied); Merge(supplied);
                    var otherwise2 = new List<Part>(); Walk(fallback.Right, otherwise2); Merge(otherwise2);
                    Branch(fallback, fallback.Left, supplied, otherwise2, into);
                    return;
                }

            case LogicalExpression { Operator: Acornima.Operator.LogicalAnd } and:
                // `cond && markup` is `cond ? markup : ''` written shorter.
                {
                    var then = new List<Part>(); Walk(and.Right, then); Merge(then);
                    Branch(and, and.Left, then, new List<Part>(), into);
                    return;
                }

            case CallExpression call when Mapped(call, into):
                return;

            case CallExpression call when Inlined(call, into):
                return;

            case Identifier id when !_resolving.Contains(id.Name) && Bound(id.Name) is { } held:
                // `color = color || x` binds a name to an expression that names itself, so the inner
                // reference has to stop rather than resolve round again for ever.
                _resolving.Add(id.Name);
                try { Walk(held, into); }
                finally { _resolving.Remove(id.Name); }
                return;

            default:
                if (AsCounter(e) is { } counted) { into.Add(counted); return; }
                // Anything else is a value: a number, a field, a call this cannot see inside. It goes
                // in a hole, which is the right answer for almost all of them - a page interpolating
                // `v.pressure` wants a slot, not structure.
                into.Add(new Hole(e, _holes.Count));
                _holes.Add(e);
                return;
        }
    }

    /// <summary>
    /// A conditional: structure when the branches differ in SHAPE, a value when they do not.
    /// </summary>
    /// <remarks>
    /// This distinction is what makes the whole approach usable. Nearly every ternary on a real page
    /// picks a colour, a width or a word - <c>(on ? 'var(--cb-live)' : 'transparent')</c> inside a
    /// style attribute - and treating each as a choice of shapes doubles the number of scenes to emit.
    /// Measured on one page: 119 choices, which is 2^119 shapes, against the 23 the page really has.
    /// A branch whose sides contain no markup is a value, and a value is one slot.
    /// </remarks>
    private void Branch(Expression whole, Expression test, List<Part> then, List<Part> otherwise, List<Part> into)
    {
        if (!Structural(then) && !Structural(otherwise))
        {
            into.Add(new Hole(whole, _holes.Count));
            _holes.Add(whole);
            return;
        }
        into.Add(new Choice(test, then, otherwise));
    }

    /// <summary>Whether a branch changes the shape of the markup rather than a value inside it.</summary>
    private static bool Structural(List<Part> parts)
    {
        foreach (var part in parts)
            switch (part)
            {
                // A tag opens or closes: elements appear or disappear, which layout has to see.
                case Fixed f when f.Text.IndexOf('<') >= 0: return true;
                // A registration is a click handler appearing, which is structure by another name.
                case Push: return true;
                case Choice c when Structural(c.Then) || Structural(c.Else): return true;
                case Repeat: return true;
            }
        return false;
    }

    /// <summary>
    /// <c>xs.map(x =&gt; '...').join('')</c>, the way every page writes a repeated row.
    /// </summary>
    private bool Mapped(CallExpression call, List<Part> into)
    {
        // .join(...) on the outside
        if (call.Callee is not MemberExpression { Computed: false, Property: Identifier { Name: "join" }, Object: CallExpression inner })
            return false;
        if (inner.Callee is not MemberExpression { Computed: false, Property: Identifier { Name: "map" }, Object: Expression list })
            return false;
        if (inner.Arguments.Count != 1) return false;

        var fn = inner.Arguments[0] switch
        {
            ArrowFunctionExpression a => (IFunction)a,
            FunctionExpression f => f,
            _ => null,
        };
        if (fn == null || fn.Params.Count == 0 || fn.Params[0] is not Identifier item) return false;

        // The body has to be one expression - a row builder that branches internally is a choice
        // inside the repeat, which is fine, but a statement body would need running.
        // A row builder declares a local or two and returns - the same shape as a helper, so it is
        // reduced the same way. Requiring one bare expression refused eighteen real rows.
        var bound = new Dictionary<string, Expression>(StringComparer.Ordinal);
        var parts = new List<Part>();
        var body = fn.Body switch
        {
            Expression x => x,
            BlockStatement block => Returned(block, bound, parts),
            _ => null,
        };
        if (body == null)
        {
            _problems.Add(Line(inner) + "a .map() whose row builder does more than return markup");
            return false;
        }

        _bindings.Add(bound);
        try { Walk(body, parts); }
        finally { _bindings.RemoveAt(_bindings.Count - 1); }
        Merge(parts);
        into.Add(new Repeat(list, item.Name, parts));
        return true;
    }

    /// <summary>
    /// A call to one of the page's own string builders, reduced in place with its arguments bound.
    /// </summary>
    /// <remarks>
    /// Load bearing rather than a nicety. Every page of any size splits its markup across helpers -
    /// <c>screenHeader({...}) + renderAtmo(v) + tabBar({...})</c> - and stopping at the call would
    /// find one hole and no structure at all, which is exactly the shape that cannot be compiled.
    /// </remarks>
    private bool Inlined(CallExpression call, List<Part> into)
    {
        if (call.Callee is not Identifier name || !_functions.TryGetValue(name.Name, out var fn)) return false;
        if (_bindings.Count >= MaxInline) { _problems.Add(Line(call) + "markup helpers nested more than " + MaxInline + " deep"); return false; }
        if (!_inlining.Add(name.Name)) { _problems.Add(Line(call) + "`" + name.Name + "` builds markup from itself"); return false; }

        try
        {
            // Parameters and the helper's own locals share one frame, so a local written in terms
            // of a parameter resolves when it is used rather than needing an order here.
            var bound = new Dictionary<string, Expression>(StringComparer.Ordinal);
            var unset = new List<string>();
            for (var i = 0; i < fn.Params.Count; i++)
            {
                if (fn.Params[i] is not Identifier p) continue;
                if (i < call.Arguments.Count && call.Arguments[i] is Expression arg) bound[p.Name] = arg;
                else unset.Add(p.Name);                 // the caller omitted it: undefined, not unknown
            }
            foreach (var missing in unset) _unset.Add(missing);

            var body = fn.Body switch
            {
                Expression x => x,
                BlockStatement block => Returned(block, bound, into),
                _ => null,
            };
            if (body == null)
            {
                _problems.Add(Line(call) + "`" + name.Name + "` does more than return markup");
                return false;
            }

            _bindings.Add(bound);
            try { Walk(body, into); }
            finally
            {
                _bindings.RemoveAt(_bindings.Count - 1);
                foreach (var missing in unset) _unset.Remove(missing);
            }
            return true;
        }
        finally { _inlining.Remove(name.Name); }
    }

    /// <summary>
    /// The single expression a helper returns, when its body is only declarations and that return.
    /// </summary>
    /// <remarks>
    /// <c>function row(x) { const cls = pick(x); return '&lt;div class="' + cls + '"&gt;'; }</c> is a
    /// builder, not a procedure, and refusing it would refuse most real pages. The declarations are
    /// substituted into the returned expression, so the result is still one expression and still
    /// static. A body that assigns to anything outside itself is not reducible and says so.
    /// </remarks>
    private Expression? Returned(BlockStatement block, Dictionary<string, Expression> bound, List<Part> parts)
    {
        foreach (var s in block.Body)
        {
            switch (s)
            {
                case VariableDeclaration { Kind: VariableDeclarationKind.Const or VariableDeclarationKind.Let } d:
                    foreach (var one in d.Declarations)
                        if (one.Id is Identifier id && one.Init != null) bound[id.Name] = one.Init;
                    break;

                // Registering a callback while building markup. The ONLY side effect allowed here,
                // because it is the only one whose result is positional and so knowable without
                // running the page. Anything else genuinely needs execution.
                case ExpressionStatement { Expression: CallExpression
                    { Callee: MemberExpression { Computed: false, Object: Identifier list, Property: Identifier { Name: "push" } },
                      Arguments.Count: 1 } push } when push.Arguments[0] is Expression value:
                    _registers.Add(list.Name);
                    parts.Add(new Push(list.Name, value));
                    break;

                // `color = color || 'var(--cb-mark)'` - a default written the way everyone wrote one
                // before default parameters existed. It rebinds a name this helper owns, which is
                // not a side effect on anything outside it, so it reduces like a declaration.
                case ExpressionStatement { Expression: AssignmentExpression
                    { Operator: Acornima.Operator.Assignment, Left: Identifier target } assign }
                    when bound.ContainsKey(target.Name) || _unset.Contains(target.Name):
                    bound[target.Name] = assign.Right;
                    break;

                case ReturnStatement { Argument: { } r }:
                    // The first return wins and there cannot be a second: any statement that is not
                    // one of the above ends the reduction, so nothing follows it.
                    return r;
                case EmptyStatement:
                    break;
                default:
                    // An `if`, a loop, an assignment to something outside: this is a procedure, not a
                    // builder, and reducing it would need running it.
                    return null;
            }
        }
        return null;                                   // fell off the end without returning markup
    }

    /// <summary>Arrays the markup registers callbacks in, so their length is a countable position.</summary>
    private readonly HashSet<string> _registers = new(StringComparer.Ordinal);

    /// <summary>The registration position an expression reads, rather than a value it computes.</summary>
    private Part? AsCounter(Expression e)
    {
        switch (e)
        {
            case MemberExpression { Computed: false, Object: Identifier list, Property: Identifier { Name: "length" } }
                when _registers.Contains(list.Name):
                return new Counter(list.Name, 0);
            case NonLogicalBinaryExpression { Operator: Acornima.Operator.Subtraction, Right: NumericLiteral n } b
                when AsCounter(b.Left) is Counter c:
                return new Counter(c.List, c.Less + (int)n.Value);
        }
        return null;
    }

    /// <summary>What a name stands for, through the helper frames currently open.</summary>
    private Expression? Bound(string name)
    {
        for (var i = _bindings.Count - 1; i >= 0; i--)
            if (_bindings[i].TryGetValue(name, out var e)) return e;
        return null;
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

    private static string Line(Node n)
        => "line " + n.Location.Start.Line.ToString(CultureInfo.InvariantCulture) + ": ";

    // ---- the skeleton ----------------------------------------------------------------------------

    /// <summary>
    /// A sentinel standing in for a hole. Chosen to survive the HTML parser unchanged wherever a
    /// value can appear - text, an attribute, a style property - and to be findable afterwards
    /// without a chance of matching the page's own content.
    /// </summary>
    internal static string Sentinel(int index) => "H" + index.ToString(CultureInfo.InvariantCulture) + "";

    /// <summary>
    /// The markup as text, with each hole replaced by its sentinel.
    /// </summary>
    /// <param name="taken">
    /// Which side of each choice to take, in the order the choices appear. A page with three choices
    /// has eight shapes, and the caller lays out and emits the ones it decides are reachable.
    /// </param>
    /// <param name="rows">How many times to emit a repeated row.</param>
    internal string Skeleton(IReadOnlyList<bool>? taken = null, int rows = 1)
    {
        var sb = new StringBuilder();
        var choice = 0;
        // Counted here rather than during the analysis: a registration inside a repeat happens once
        // per row, so its index only exists while the markup is being written out.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        Registered.Clear();
        Write(Parts);
        return sb.ToString();

        void Write(List<Part> parts)
        {
            foreach (var part in parts)
            {
                switch (part)
                {
                    case Fixed f: sb.Append(f.Text); break;
                    case Hole h: sb.Append(Sentinel(h.Index)); break;
                    case Push p:
                        counts.TryGetValue(p.List, out var had);
                        counts[p.List] = had + 1;
                        Registered.Add((p.List, had, p.Value));
                        break;
                    case Counter c:
                        counts.TryGetValue(c.List, out var now);
                        sb.Append((now - c.Less).ToString(CultureInfo.InvariantCulture));
                        break;
                    case Choice c2:
                        {
                            var take = taken == null || choice >= taken.Count || taken[choice];
                            choice++;
                            Write(take ? c2.Then : c2.Else);
                            break;
                        }
                    case Repeat r:
                        for (var i = 0; i < rows; i++) Write(r.Body);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// What the last <see cref="Skeleton"/> registered, in order: the list, the index it was given,
    /// and the expression registered. This is how a click handler written into the markup reaches
    /// the compiled page, which otherwise has no wiring at all once the builder is compiled away.
    /// </summary>
    internal List<(string List, int Index, Expression Value)> Registered { get; } = new();

    /// <summary>How many choices the markup contains, so a caller knows how many shapes there are.</summary>
    internal int Choices
    {
        get
        {
            var n = 0;
            Count(Parts);
            return n;

            void Count(List<Part> parts)
            {
                foreach (var part in parts)
                {
                    if (part is Choice c) { n++; Count(c.Then); Count(c.Else); }
                    else if (part is Repeat r) Count(r.Body);
                }
            }
        }
    }

    private static IEnumerable<Node> Everything(Node n)
    {
        yield return n;
        foreach (var child in n.ChildNodes)
        {
            if (child == null) continue;
            foreach (var d in Everything(child)) yield return d;
        }
    }
}

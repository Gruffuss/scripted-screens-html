using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Acornima;
using Acornima.Ast;

namespace ScriptedScreensHtml;

/// <summary>
/// Motion that is a closed form of time, lifted out of the page's script and into the scene.
/// </summary>
/// <remarks>
/// A compiled page has two kinds of number and they belong in different places. A reading from a
/// device is DATA: nothing can predict it, so the chip sends it when it changes, at whatever rate it
/// changes - which is what a hand-written console does and what it costs. A drifting parallax layer,
/// a blinking warning, a spinning icon is MOTION: it is <c>f(t)</c> and nothing needs to be sent for
/// it at all, because the vector mod evaluates expressions on its own worker thread.
///
/// Everything this mod measured as expensive came from treating the second kind as the first. A page
/// whose script writes a position every frame makes the chip send every frame, and the renderer then
/// rebuilds every frame, per console. The fix is not a cheaper send: it is not sending.
///
/// So this pass reads the page's script and asks, of each write, whether its value is a closed form
/// of time. When it is, the emitted scene carries the expression and the write is dropped - the chip
/// never computes it and never sends it. When it is not, nothing changes and the write stays a slot.
///
/// <b>It refuses far more than it accepts, deliberately.</b> A value behind an <c>if</c>, a value
/// touched by an event handler, a value drawn from <c>Math.random</c> - none of these is a function
/// of time, and guessing would mean a console that draws confidently wrong motion for ever. Every
/// refusal carries the line and the reason, so the report says what a page would have to change.
/// </remarks>
internal sealed class Motion
{
    // ---- the symbolic value ---------------------------------------------------------------------

    /// <summary>
    /// An expression over <c>t</c>, in the small language both this pass and the vector mod can read.
    /// </summary>
    /// <remarks>
    /// A tree rather than a string because the whole pass turns on two questions a string cannot
    /// answer: is this linear in <c>t</c> (so it can be integrated), and is it constant (so it can be
    /// folded). Rendering to text happens once, at the end.
    /// </remarks>
    internal abstract class Sym
    {
        public abstract string Text { get; }
        /// <summary>The constant value when this is one, else null. Drives every fold below.</summary>
        public virtual double? Value => null;
    }

    private sealed class Num : Sym
    {
        public readonly double N;
        public Num(double n) { N = n; }
        public override double? Value => N;
        public override string Text => Fmt(N);
    }

    /// <summary>Seconds since the scene was applied - the vector mod's own clock.</summary>
    private sealed class Clock : Sym
    {
        public static readonly Clock It = new();
        public override string Text => "t";
    }

    private sealed class Bin : Sym
    {
        public readonly char Op; public readonly Sym A, B;
        public Bin(char op, Sym a, Sym b) { Op = op; A = a; B = b; }
        public override string Text => "(" + A.Text + Op + B.Text + ")";
    }

    private sealed class Fn : Sym
    {
        public readonly string Name; public readonly Sym[] Args;
        public Fn(string name, params Sym[] args) { Name = name; Args = args; }
        public override string Text
        {
            get
            {
                var sb = new StringBuilder(Name).Append('(');
                for (var i = 0; i < Args.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Args[i].Text); }
                return sb.Append(')').ToString();
            }
        }
    }

    private static string Fmt(double v) =>
        double.IsNaN(v) || double.IsInfinity(v)
            ? "0"
            : v.ToString("0.#####", CultureInfo.InvariantCulture);

    // ---- building, with folding -----------------------------------------------------------------
    //
    // Every constructor folds when both sides are known. Not for speed: the integrator has to
    // recognise `min(950, 380+9*t)` as "a constant against something linear", and it only can if the
    // 380 and the 9 have already collapsed out of whatever arithmetic the page wrote them as.

    private static Num N(double v) => new(v);

    private static Sym Add(Sym a, Sym b)
    {
        if (a.Value is { } x && b.Value is { } y) return N(x + y);
        if (a.Value == 0) return b;
        if (b.Value == 0) return a;
        return new Bin('+', a, b);
    }

    private static Sym Sub(Sym a, Sym b)
    {
        if (a.Value is { } x && b.Value is { } y) return N(x - y);
        if (b.Value == 0) return a;
        return new Bin('-', a, b);
    }

    private static Sym Mul(Sym a, Sym b)
    {
        if (a.Value is { } x && b.Value is { } y) return N(x * y);
        if (a.Value == 0 || b.Value == 0) return N(0);
        if (a.Value == 1) return b;
        if (b.Value == 1) return a;
        // Pull a constant through one that is already scaled, so `(1000*t)/1000` collapses to `t`
        // rather than travelling the whole way as arithmetic nobody will read. The integrator only
        // recognises a clamp against something LINEAR, and it can only see that once the scalars
        // have met each other.
        if (a.Value is { } k)
        {
            if (b is Bin { Op: '*' } m && m.A.Value is { } ka) return Mul(N(k * ka), m.B);
            if (b is Bin { Op: '*' } m2 && m2.B.Value is { } kb) return Mul(N(k * kb), m2.A);
        }
        if (b.Value != null) return Mul(b, a);
        return new Bin('*', a, b);
    }

    private static Sym? Div(Sym a, Sym b)
    {
        if (b.Value is { } y)
        {
            if (y == 0) return null;                       // the page divides by a constant zero
            if (a.Value is { } x) return N(x / y);
            return Mul(N(1 / y), a);
        }
        if (a.Value == 0) return N(0);
        return new Bin('/', a, b);
    }

    // ---- linear form and integration ------------------------------------------------------------

    /// <summary>
    /// <c>a + b*t</c>, when the expression has that shape. The integrator's whole domain: a rate that
    /// is linear in time integrates to a quadratic, and a rate clamped by <c>min</c>/<c>max</c> to a
    /// quadratic joined to a line. Everything a page actually does to a speed is one of those.
    /// </summary>
    private static (double A, double B)? Linear(Sym s)
    {
        switch (s)
        {
            case Num n: return (n.N, 0);
            case Clock: return (0, 1);
            case Bin b:
                var l = Linear(b.A); var r = Linear(b.B);
                if (l == null || r == null) return null;
                var (la, lb) = l.Value; var (ra, rb) = r.Value;
                switch (b.Op)
                {
                    case '+': return (la + ra, lb + rb);
                    case '-': return (la - ra, lb - rb);
                    case '*':
                        if (lb == 0) return (la * ra, la * rb);
                        if (rb == 0) return (la * ra, lb * ra);
                        return null;                        // t*t is not linear
                    case '/':
                        if (rb != 0 || ra == 0) return null;
                        return (la / ra, lb / ra);
                }
                return null;
        }
        return null;
    }

    /// <summary>
    /// The integral of a rate from 0 to <c>t</c>, or null when it has no closed form here.
    /// </summary>
    /// <remarks>
    /// The clamped case is the one worth writing down, because it is what every accelerating thing in
    /// a page looks like: <c>speed = min(MAX, START + k*t)</c>. Naively that needs a conditional, and
    /// the expression language has none. It does not need one - the crossing time
    /// <c>Tc = (MAX-START)/k</c> is a constant known here, so <c>u = min(t, Tc)</c> is the time spent
    /// accelerating and <c>max(0, t-Tc)</c> the time spent at the top. The integral is then
    /// <c>START*u + k*u^2/2 + MAX*max(0, t-Tc)</c>: one expression, no branch, exact.
    /// </remarks>
    private static Sym? Integrate(Sym rate)
    {
        // a + b*t  ->  a*t + b*t^2/2
        if (Linear(rate) is { } lin)
        {
            var (a, b) = lin;
            return Add(Mul(N(a), Clock.It), Mul(N(b / 2), Mul(Clock.It, Clock.It)));
        }

        switch (rate)
        {
            // sums integrate term by term; a constant factor comes out
            case Bin { Op: '+' } s when Integrate(s.A) is { } ia && Integrate(s.B) is { } ib:
                return Add(ia, ib);
            case Bin { Op: '-' } s when Integrate(s.A) is { } ia && Integrate(s.B) is { } ib:
                return Sub(ia, ib);
            case Bin { Op: '*' } s when s.A.Value is { } k && Integrate(s.B) is { } ib:
                return Mul(N(k), ib);
            case Bin { Op: '*' } s when s.B.Value is { } k && Integrate(s.A) is { } ia:
                return Mul(N(k), ia);
            case Bin { Op: '/' } s when s.B.Value is { } k && k != 0 && Integrate(s.A) is { } ia:
                return Mul(N(1 / k), ia);

            // min(M, a+b*t) with b > 0, and its mirror: accelerate, then hold
            case Fn { Name: "min", Args.Length: 2 } f: return Clamped(f.Args[0], f.Args[1], true);
            case Fn { Name: "max", Args.Length: 2 } f: return Clamped(f.Args[0], f.Args[1], false);
        }
        return null;

        Sym? Clamped(Sym p, Sym q, bool isMin)
        {
            // whichever side is the constant limit; the other must be linear and moving toward it
            var limit = p.Value ?? q.Value;
            if (limit == null) return null;
            var ramp = p.Value == null ? p : q;
            if (Linear(ramp) is not { } l) return null;
            var (a, b) = l;
            if (b == 0) return Integrate(N(limit.Value));            // never moves: it IS the limit
            // min wants a rising ramp, max a falling one; anything else means the clamp never bites
            if (isMin != (b > 0)) return null;
            var cross = (limit.Value - a) / b;
            if (cross <= 0) return Integrate(N(limit.Value));        // already past it at t=0
            var u = new Fn("min", Clock.It, N(cross));
            return Add(Add(Mul(N(a), u), Mul(N(b / 2), Mul(u, u))),
                       Mul(N(limit.Value), new Fn("max", N(0), Sub(Clock.It, N(cross)))));
        }
    }

    // ---- what the analysis produces -------------------------------------------------------------

    /// <summary>One write whose value is a closed form of time, and the expression that replaces it.</summary>
    internal readonly struct Found
    {
        /// <summary>The element the write targets, already resolved through any loop index.</summary>
        public readonly string Id;
        /// <summary>`style.transform`, `style.top`, ... exactly as <see cref="DomWrites"/> names it.</summary>
        public readonly string Property;
        /// <summary>
        /// One expression per value the property carries: two for a transform (x then y), one
        /// otherwise. A null entry means that value is not a closed form and keeps its slot.
        /// </summary>
        public readonly string?[] Parts;
        /// <summary>
        /// Which of those the write actually sets.
        /// </summary>
        /// <remarks>
        /// <c>translateX</c> moves one axis and says nothing about the other, so the other is not a
        /// refusal - it is simply not this write's business and keeps whatever the scene emitted.
        /// Without the distinction every single-axis move was refused for the axis it never touched,
        /// which is most of the motion on a real page.
        /// </remarks>
        public readonly bool[] Touches;
        public Found(string id, string property, string?[] parts, bool[]? touches = null)
        {
            Id = id; Property = property; Parts = parts;
            if (touches != null) { Touches = touches; return; }
            Touches = new bool[parts.Length];
            for (var i = 0; i < parts.Length; i++) Touches[i] = true;
        }
    }

    private readonly List<Found> _found = new();
    private readonly List<string> _notes = new();

    /// <summary>Every top-level name whose value is a constant, so the page's own configuration folds.</summary>
    private readonly Dictionary<string, Sym> _consts = new(StringComparer.Ordinal);
    /// <summary>
    /// A state variable's closed form. A missing entry means "not known", which is the safe answer
    /// and the common one: anything assigned under an <c>if</c>, inside an event handler, or from
    /// <c>Math.random</c> is removed from here and everything computed from it then refuses too.
    /// </summary>
    private readonly Dictionary<string, Sym> _state = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FunctionDeclaration> _functions = new(StringComparer.Ordinal);
    /// <summary>Names bound to <c>document.getElementById</c>, and the page's own one-argument wrapper.</summary>
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lookups = new(StringComparer.Ordinal);
    /// <summary>The loop variable currently being unrolled, and its value this time round.</summary>
    private readonly Dictionary<string, double> _loopVars = new(StringComparer.Ordinal);
    /// <summary>Guards against a page whose functions call each other.</summary>
    private readonly HashSet<string> _inlining = new(StringComparer.Ordinal);

    /// <summary>The parameter the frame driver is handed, in seconds, or null when there is none.</summary>
    private string? _timeParam;
    /// <summary>What the driver's own <c>dt</c> local is called, once it is recognised.</summary>
    private readonly HashSet<string> _dts = new(StringComparer.Ordinal);

    /// <summary>How many loop iterations are unrolled before a page is simply told it is too big.</summary>
    private const int MaxUnroll = 64;

    internal static (IReadOnlyList<Found> Found, IReadOnlyList<string> Notes) Of(string source)
    {
        var m = new Motion();
        try { m.Run(source); }
        catch (Exception ex) { m._notes.Add("motion: " + ex.Message); }
        return (m._found, m._notes);
    }

    private void Run(string source)
    {
        var ast = new Parser().ParseScript(source);

        foreach (var node in ast.Body)
        {
            if (node is FunctionDeclaration { Id.Name: { } fname }) _functions[fname] = (FunctionDeclaration)node;
        }
        Constants(ast);
        Lookups(ast);

        // The driver: the function a page hands to requestAnimationFrame. An interval page ticks at
        // its own rate and its values are data, not motion - it is already sending at the rate it
        // changes, which is the shape this pass exists to produce.
        var driver = Driver(ast);
        if (driver == null) { _notes.Add("motion: the page has no requestAnimationFrame loop, so nothing here runs per frame"); return; }

        _timeParam = driver.Params.Count > 0 && driver.Params[0] is Identifier p ? p.Name : null;
        SeedDelta(driver.Body as BlockStatement);
        if (_dts.Count == 0)
            _notes.Add("motion: the frame loop's elapsed time was not recognised, so nothing can be integrated");
        Body(driver.Body as BlockStatement);
    }

    /// <summary>
    /// Finds the driver's own elapsed-time local: the <c>dt</c> everything else is integrated against.
    /// </summary>
    /// <remarks>
    /// Every page writes this the same way and none of them writes it the same way twice:
    /// <c>const dt = last ? Math.min(0.05, now - last) : 0; last = now;</c>. What identifies it is not
    /// its spelling but its shape - it is computed from the frame's timestamp AND from a name that the
    /// same body then stamps with that timestamp. Nothing else in a frame loop has both.
    ///
    /// Getting this wrong in the safe direction means no page integrates and the pass finds nothing;
    /// getting it wrong the other way would mean integrating against something that is not time, so
    /// the test is a conjunction rather than a guess at the name.
    /// </remarks>
    private void SeedDelta(BlockStatement? body)
    {
        if (body == null || _timeParam == null) return;

        // A name holds the frame's timestamp when it is a pure restatement of one: exactly one free
        // name, and that name is already a stamp. `const now = t / 1000` and `last = now` qualify;
        // `const dt = last ? now - last : 0` does not, because it names two of them - which is what
        // makes it the elapsed time rather than another stamp.
        var stamps = new HashSet<string>(StringComparer.Ordinal) { _timeParam };
        for (var pass = 0; pass < 3; pass++)
            foreach (var node in Everything(body))
            {
                var (name, init) = node switch
                {
                    VariableDeclarator { Id: Identifier id, Init: { } e } => (id.Name, e),
                    AssignmentExpression { Left: Identifier id, Right: { } e } => (id.Name, (Expression)e),
                    _ => (null, null),
                };
                if (name == null || init == null || stamps.Contains(name)) continue;
                var free = FreeNames(init);
                if (free.Count == 1 && stamps.Contains(free.First())) stamps.Add(name);
            }

        foreach (var node in Everything(body))
        {
            if (node is not VariableDeclarator { Id: Identifier local, Init: { } init }) continue;
            if (stamps.Contains(local.Name)) continue;
            var free = FreeNames(init);
            free.IntersectWith(stamps);
            if (free.Count >= 2) _dts.Add(local.Name);
        }
    }

    /// <summary>
    /// The names an expression reads. The property side of <c>a.b</c> is not one, and neither is a
    /// namespace like <c>Math</c> - counting either would make every arithmetic expression look like
    /// it reads several variables, which is exactly the test above.
    /// </summary>
    private static HashSet<string> FreeNames(Node e)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Walk(e);
        names.Remove("Math");
        return names;

        void Walk(Node n)
        {
            if (n is Identifier id) { names.Add(id.Name); return; }
            if (n is MemberExpression { Computed: false } m) { Walk(m.Object); return; }
            foreach (var child in n.ChildNodes) if (child != null) Walk(child);
        }
    }

    // ---- reading the page ------------------------------------------------------------------------

    /// <summary>Top-level <c>const</c>/<c>let</c> numbers, so a page's own configuration folds away.</summary>
    private void Constants(Script ast)
    {
        foreach (var node in Everything(ast))
        {
            if (node is not VariableDeclaration decl) continue;
            foreach (var d in decl.Declarations)
            {
                if (d.Id is not Identifier id || d.Init == null) continue;
                // An object literal holds a page's mutable state: record each numeric field as its
                // own name, which is how `g.speed` finds its starting value.
                if (d.Init is ObjectExpression obj)
                {
                    foreach (var prop in obj.Properties)
                    {
                        if (prop is not Property { Key: Identifier key, Value: Expression v }) continue;
                        if (Fold(v) is { } folded) _state[id.Name + "." + key.Name] = folded;
                    }
                    continue;
                }
                if (Fold(d.Init) is { } value)
                {
                    _consts[id.Name] = value;
                    _state[id.Name] = value;
                }
            }
        }
    }

    /// <summary>A constant, folded through the constants already known. Null when it is not one.</summary>
    private Sym? Fold(Expression e)
    {
        var s = Eval(e);
        return s?.Value == null ? null : s;
    }

    /// <summary>
    /// The names that stand for <c>document.getElementById</c>, and the names bound to one element.
    /// </summary>
    private void Lookups(Script ast)
    {
        _lookups.Add("document.getElementById");
        foreach (var node in Everything(ast))
        {
            if (node is not VariableDeclaration decl) continue;
            foreach (var d in decl.Declarations)
            {
                if (d.Id is not Identifier id || d.Init == null) continue;
                // const $ = (id) => document.getElementById(id)
                if (d.Init is ArrowFunctionExpression { Params.Count: 1 } arrow
                    && arrow.Body is CallExpression { Callee: MemberExpression { Property: Identifier { Name: "getElementById" } } })
                    _lookups.Add(id.Name);
                // const player = $('player')
                if (d.Init is CallExpression call && IdOf(call) is { } elementId) _aliases[id.Name] = elementId;
            }
        }
    }

    /// <summary>The literal element id a lookup call names, or null when it is computed or not one.</summary>
    private string? IdOf(CallExpression call)
    {
        var name = call.Callee switch
        {
            Identifier i => i.Name,
            MemberExpression { Object: Identifier o, Property: Identifier p } => o.Name + "." + p.Name,
            _ => null,
        };
        if (name == null || !_lookups.Contains(name) || call.Arguments.Count != 1) return null;
        return Text(call.Arguments[0] as Expression);
    }

    /// <summary>A string expression's literal value, with any unrolled loop index substituted.</summary>
    private string? Text(Expression? e)
    {
        switch (e)
        {
            case null: return null;
            case StringLiteral s: return s.Value;
            case Identifier id when _loopVars.TryGetValue(id.Name, out var v): return Fmt(v);
            case NumericLiteral n: return Fmt(n.Value);
            case BinaryExpression { Operator: Operator.Addition } b:
                var l = Text(b.Left); var r = Text(b.Right);
                return l == null || r == null ? null : l + r;
        }
        // 'pb' + i where i is a loop index the unroller has bound
        return Eval(e)?.Value is { } value ? Fmt(value) : null;
    }

    private FunctionDeclaration? Driver(Script ast)
    {
        string? name = null;
        foreach (var node in Everything(ast))
        {
            if (node is CallExpression { Callee: Identifier { Name: "requestAnimationFrame" }, Arguments.Count: 1 } call
                && call.Arguments[0] is Identifier fn)
            { name = fn.Name; break; }
        }
        return name != null && _functions.TryGetValue(name, out var decl) ? decl : null;
    }

    // ---- symbolic execution ----------------------------------------------------------------------

    /// <summary>
    /// Runs a block, in order, tracking what each state variable is as a function of time.
    /// </summary>
    /// <remarks>
    /// Straight-line only, and that is the whole safety argument. A branch means the value depends on
    /// something other than time, so every name a branch could assign is FORGOTTEN rather than
    /// guessed - and a forgotten name makes everything computed from it refuse in turn. The same goes
    /// for a call this pass cannot see inside. A page therefore gets expressions for exactly the part
    /// of its motion that genuinely runs every frame regardless of state, which is the only part that
    /// is a function of time.
    /// </remarks>
    private void Body(BlockStatement? block)
    {
        if (block == null) return;
        for (var i = 0; i < block.Body.Count; i++)
        {
            var st = block.Body[i];
            // A conditional return does not merely make ITS OWN assignments doubtful - it makes every
            // statement after it conditional, because whether they run at all depends on the guard.
            // `if (g.state !== 'running') return;` is how a page pauses, and reading past it produced
            // a parallax that kept scrolling after the run had stopped: arithmetic that is pure in t
            // and still the wrong picture. Everything from here is forgotten and the block ends.
            if (st is not (VariableDeclaration or BlockStatement or ForStatement)
                && Returns(st))
            {
                for (var j = i; j < block.Body.Count; j++) Forget(block.Body[j]);
                return;
            }
            Statement(st);
        }
    }

    /// <summary>Whether a statement can leave the function early, so what follows it is conditional.</summary>
    private static bool Returns(Node st)
    {
        foreach (var node in Everything(st))
            if (node is ReturnStatement or BreakStatement or ContinueStatement or ThrowStatement) return true;
        return false;
    }

    private void Statement(Statement st)
    {
        switch (st)
        {
            case VariableDeclaration decl:
                foreach (var d in decl.Declarations)
                    if (d.Id is Identifier id)
                        Assign(id.Name, d.Init == null ? null : Value(id.Name, d.Init));
                return;

            case ExpressionStatement { Expression: AssignmentExpression a }:
                Assignment(a);
                return;

            case ExpressionStatement { Expression: CallExpression call }:
                Inline(call);
                return;

            case ForStatement f:
                Unroll(f);
                return;

            case BlockStatement b:
                Body(b);
                return;

            // Everything else - if, for-of, while, return, try, switch - makes what follows
            // conditional. Forget every name it could touch rather than pretend it did not happen.
            default:
                Forget(st);
                return;
        }
    }

    /// <summary>Drops every name a statement could assign, so nothing downstream trusts a guess.</summary>
    private void Forget(Node st)
    {
        foreach (var node in Everything(st))
        {
            switch (node)
            {
                case AssignmentExpression { Left: Expression left }:
                    if (Path(left) is { } name) _state.Remove(name);
                    break;
                case UpdateExpression { Argument: Expression arg }:
                    if (Path(arg) is { } counted) _state.Remove(counted);
                    break;
                // a call inside a branch runs whatever that function assigns, conditionally
                case CallExpression { Callee: Identifier fn } when _functions.TryGetValue(fn.Name, out var decl):
                    if (_inlining.Add(fn.Name)) { Forget(decl); _inlining.Remove(fn.Name); }
                    break;
            }
        }
    }

    private void Assignment(AssignmentExpression a)
    {
        // A write to an element's style or text is the thing this pass exists to catch.
        if (a.Left is MemberExpression target && Write(target, a)) return;

        if (Path(a.Left) is not { } name) return;

        var value = a.Operator switch
        {
            Operator.Assignment => Value(name, a.Right),
            Operator.AdditionAssignment => Accumulate(name, a.Right, false),
            Operator.SubtractionAssignment => Accumulate(name, a.Right, true),
            _ => null,
        };
        Assign(name, value);
    }

    /// <summary>
    /// What a name becomes when the page assigns it: a closed form, a self-update, or a per-frame
    /// delta kept for whatever integrates it later.
    /// </summary>
    /// <remarks>
    /// The three are tried in that order and they are genuinely different things.
    /// <c>x = sin(clock)</c> is a value. <c>x = (x + dx) % 800</c> is an integration written as an
    /// assignment, which is how every page writes a wrapping scroll. <c>const dx = speed * dt</c> is
    /// neither - on its own it has no closed form at all, because it is a quantity per frame rather
    /// than per second - but the rate inside it is exactly what the next line needs.
    /// </remarks>
    private Sym? Value(string name, Expression init)
    {
        // A right-hand side that names the variable it assigns is a STEP, never a value. Evaluating
        // it first would read the name's value so far and fold the whole thing to a constant -
        // `g.phase = g.phase + 0.02` would come out as 0.02 and stay there, which is a page frozen
        // one frame after it loaded rather than a page that refuses.
        if (FreeNames(init).Contains(Head(name)))
            return SelfUpdate(name, init);
        if (Eval(init) is { } direct) return direct;
        if (Rate(init) is { } perSecond) return new DtScaled(perSecond);
        return null;
    }

    /// <summary>The object a dotted name hangs off, which is what appears as an identifier.</summary>
    private static string Head(string path)
    {
        var dot = path.IndexOf('.');
        return dot < 0 ? path : path.Substring(0, dot);
    }

    /// <summary>
    /// <c>x = (x + dx) % 800</c> and its relatives, where an assignment IS an accumulation.
    /// </summary>
    /// <remarks>
    /// A page wraps a scroll offset with <c>%</c> and caps a speed with <c>Math.min</c>, and writes
    /// both as a plain assignment whose right-hand side mentions the name again. Peeling those
    /// wrappers off finds the accumulation underneath and puts them back around its closed form -
    /// which is exact, because <c>mod</c> and <c>min</c> mean the same thing applied once at time
    /// <c>t</c> as applied every frame along the way, for a quantity that only moves one direction.
    /// </remarks>
    private Sym? SelfUpdate(string name, Expression e)
    {
        switch (e)
        {
            // (core) % m
            case BinaryExpression { Operator: Operator.Remainder } b when SelfUpdate(name, b.Left) is { } inner:
                return Eval(b.Right) is { Value: not null and not 0 } m ? new Fn("mod", inner, m) : null;

            // Math.min(limit, core) / Math.max(limit, core), either way round
            case CallExpression { Callee: MemberExpression { Object: Identifier { Name: "Math" }, Property: Identifier fn } } call
                when (fn.Name is "min" or "max") && call.Arguments.Count == 2:
                {
                    for (var i = 0; i < 2; i++)
                    {
                        if (call.Arguments[i] is not Expression core || call.Arguments[1 - i] is not Expression other) continue;
                        if (SelfUpdate(name, core) is not { } inner) continue;
                        return Eval(other) is { } limit ? new Fn(fn.Name, limit, inner) : null;
                    }
                    return null;
                }

            // the accumulation itself: x + delta, in either order
            case BinaryExpression { Operator: Operator.Addition } b:
                if (Path(b.Left) == name) return Accumulate(name, b.Right, false);
                if (Path(b.Right) == name) return Accumulate(name, b.Left, false);
                return null;
            case BinaryExpression { Operator: Operator.Subtraction } b2 when Path(b2.Left) == name:
                return Accumulate(name, b2.Right, true);
        }
        return null;
    }

    private void Assign(string name, Sym? value)
    {
        if (value == null) _state.Remove(name); else _state[name] = value;
    }

    /// <summary>
    /// <c>x += rate*dt</c>, the shape every moving thing in a page is written as.
    /// </summary>
    /// <remarks>
    /// The <c>dt</c> is what makes this safe to integrate. A page that instead writes
    /// <c>phase += 0.02</c> advances once per FRAME, so its closed form depends on the frame rate the
    /// page happens to get - and baking one in would make the console run at a speed no browser would
    /// have shown. That refuses here rather than guessing 60.
    /// </remarks>
    private Sym? Accumulate(string name, Expression delta, bool negate)
    {
        if (!_state.TryGetValue(name, out var start)) return null;
        var rate = Rate(delta);
        if (rate == null) return null;
        var integral = Integrate(negate ? Mul(N(-1), rate) : rate);
        return integral == null ? null : Add(start, integral);
    }

    /// <summary>
    /// The per-second rate an increment carries: the expression with one factor of <c>dt</c> removed.
    /// Null when the increment does not carry exactly one, which is the refusal above.
    /// </summary>
    private Sym? Rate(Expression delta)
    {
        switch (delta)
        {
            case Identifier id when _dts.Contains(id.Name):
                return N(1);
            case BinaryExpression { Operator: Operator.Multiplication } b:
                if (Rate(b.Left) is { } lr) return Eval(b.Right) is { } rc ? Mul(lr, rc) : null;
                if (Rate(b.Right) is { } rr) return Eval(b.Left) is { } lc ? Mul(lc, rr) : null;
                return null;
            case BinaryExpression { Operator: Operator.Division } b when Rate(b.Left) is { } dl:
                return Eval(b.Right) is { } d ? Div(dl, d) : null;
            case BinaryExpression { Operator: Operator.Addition or Operator.Subtraction } b:
                var la = Rate(b.Left); var ra = Rate(b.Right);
                if (la == null || ra == null) return null;
                return b.Operator == Operator.Addition ? Add(la, ra) : Sub(la, ra);
            case UnaryExpression { Operator: Operator.UnaryNegation, Argument: { } arg } when Rate(arg) is { } nr:
                return Mul(N(-1), nr);
        }
        // a local holding "distance this frame", from `const dx = speed * dt`
        return Path(delta) is { } p && _state.TryGetValue(p, out var held2) && held2 is DtScaled scaled
            ? scaled.PerSecond : null;
    }

    /// <summary>
    /// A local that holds "a distance this frame", i.e. something times <c>dt</c>.
    /// </summary>
    /// <remarks>
    /// <c>const dx = g.speed * dt</c> then <c>g.dist += dx</c> is how every page writes this, and
    /// without carrying the <c>dt</c> through the local the second line looks like an increment by an
    /// unknown quantity. Keeping the per-second rate beside it makes the accumulation integrable.
    /// </remarks>
    private sealed class DtScaled : Sym
    {
        public readonly Sym PerSecond;
        public DtScaled(Sym perSecond) { PerSecond = perSecond; }
        // On its own it is a per-frame quantity, which has no closed form: only Rate() may read it.
        public override string Text => throw new InvalidOperationException("a per-frame delta has no closed form");
    }

    /// <summary>Unrolls a literal-bounds loop, so a family of elements written in one gets its own expression each.</summary>
    private void Unroll(ForStatement f)
    {
        if (f.Init is not VariableDeclaration { Declarations.Count: 1 } init
            || init.Declarations[0] is not { Id: Identifier v, Init: { } from }
            || Fold(from)?.Value is not { } start
            || f.Test is not BinaryExpression { Left: Identifier lv, Right: { } limitExpr } test
            || lv.Name != v.Name
            || Fold(limitExpr)?.Value is not { } limit
            || f.Update is not UpdateExpression { Operator: Operator.Increment, Argument: Identifier uv }
            || uv.Name != v.Name)
        { Forget(f); return; }

        var count = test.Operator switch
        {
            Operator.LessThan => limit - start,
            Operator.LessThanOrEqual => limit - start + 1,
            _ => -1,
        };
        if (count < 0 || count > MaxUnroll)
        {
            _notes.Add($"motion: a loop of {(count < 0 ? "an unknown number of" : count.ToString(CultureInfo.InvariantCulture))} steps is not unrolled");
            Forget(f);
            return;
        }

        // The body must not carry state ACROSS iterations, or unrolling it would be a lie: each pass
        // is evaluated with the index bound, and anything it assigns is forgotten afterwards.
        for (var i = start; i < start + count; i++)
        {
            _loopVars[v.Name] = i;
            _state[v.Name] = N(i);
            if (f.Body is BlockStatement b) { foreach (var st in b.Body) Statement(st); }
            else Statement(f.Body);
        }
        _loopVars.Remove(v.Name);
        _state.Remove(v.Name);
        Forget(f.Body);
    }

    /// <summary>Runs a call to one of the page's own functions in place, so its writes are seen.</summary>
    private void Inline(CallExpression call)
    {
        if (call.Callee is not Identifier fn || !_functions.TryGetValue(fn.Name, out var decl))
        { Forget(call); return; }
        if (!_inlining.Add(fn.Name)) { Forget(call); return; }
        try
        {
            // Bind the parameters. The driver's own `update(dt)` is what introduces the name this
            // pass integrates against, and it is usually not called `dt` in both places.
            for (var i = 0; i < decl.Params.Count; i++)
            {
                if (decl.Params[i] is not Identifier p) continue;
                var arg = i < call.Arguments.Count ? call.Arguments[i] as Expression : null;
                if (arg != null && IsFrameDelta(arg)) { _dts.Add(p.Name); _state.Remove(p.Name); }
                else Assign(p.Name, arg == null ? null : Eval(arg));
            }
            Body(decl.Body);
        }
        finally { _inlining.Remove(fn.Name); }
    }

    /// <summary>Whether an argument is the frame's own elapsed time, which is what may be integrated.</summary>
    private bool IsFrameDelta(Expression e) => e is Identifier id && _dts.Contains(id.Name);

    // ---- evaluating an expression ----------------------------------------------------------------

    /// <summary>The closed form of an expression, or null when it has none.</summary>
    private Sym? Eval(Expression? e)
    {
        switch (e)
        {
            case null: return null;
            case NumericLiteral n: return N(n.Value);
            case UnaryExpression { Operator: Operator.UnaryNegation, Argument: { } arg }:
                return Eval(arg) is { } neg ? Mul(N(-1), neg) : null;
            case UnaryExpression { Operator: Operator.UnaryPlus, Argument: { } arg }:
                return Eval(arg);

            case Identifier id:
                if (_loopVars.TryGetValue(id.Name, out var iv)) return N(iv);
                // The driver's time parameter, in milliseconds: the prelude hands the page
                // CLOCK*1000 exactly as a browser hands it performance.now().
                if (id.Name == _timeParam) return Mul(N(1000), Clock.It);
                if (_dts.Contains(id.Name)) return null;      // a per-frame delta is not a value
                return _state.TryGetValue(id.Name, out var held) && held is not DtScaled ? held : null;

            case MemberExpression m:
                if (Path(m) is { } p && _state.TryGetValue(p, out var field) && field is not DtScaled) return field;
                return null;

            case BinaryExpression b:
                {
                    var l = Eval(b.Left); var r = Eval(b.Right);
                    if (l == null || r == null) return null;
                    return b.Operator switch
                    {
                        Operator.Addition => Add(l, r),
                        Operator.Subtraction => Sub(l, r),
                        Operator.Multiplication => Mul(l, r),
                        Operator.Division => Div(l, r),
                        Operator.Remainder => r.Value == 0 ? null : new Fn("mod", l, r),
                        Operator.Exponentiation => r.Value is { } k ? Power(l, k) : null,
                        _ => null,
                    };
                }

            case CallExpression call: return Called(call);
        }
        return null;
    }

    private static Sym? Power(Sym b, double k)
        => k == 2 ? Mul(b, b) : k == 1 ? b : k == 0 ? N(1) : null;

    /// <summary>
    /// The maths a page may use inside motion, and only that.
    /// </summary>
    /// <remarks>
    /// Every one of these exists in the vector mod's expression language with the same meaning, which
    /// is what makes the translation a rename rather than a reimplementation. <c>Math.random</c> is
    /// deliberately absent: it is not a function of time, and a page using it has genuine state.
    /// <c>toFixed</c> is a formatting call around a number and passes straight through - the scene
    /// carries full precision and the renderer draws what it is given.
    /// </remarks>
    private static readonly Dictionary<string, string> MathFns = new(StringComparer.Ordinal)
    {
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan", ["abs"] = "abs", ["sign"] = "sign",
        ["sqrt"] = "sqrt", ["floor"] = "floor", ["ceil"] = "ceil", ["round"] = "round",
        ["min"] = "min", ["max"] = "max", ["atan2"] = "atan2",
    };

    private Sym? Called(CallExpression call)
    {
        // x.toFixed(n) - a page rounds for display; the scene keeps the number
        if (call.Callee is MemberExpression { Object: Expression inner, Property: Identifier { Name: "toFixed" } })
            return Eval(inner);

        if (call.Callee is not MemberExpression { Object: Identifier { Name: "Math" }, Property: Identifier fn }
            || !MathFns.TryGetValue(fn.Name, out var mapped))
            return null;

        var args = new Sym[call.Arguments.Count];
        for (var i = 0; i < args.Length; i++)
        {
            var a = Eval(call.Arguments[i] as Expression);
            if (a == null) return null;
            args[i] = a;
        }
        // The vector mod's min/max take exactly two; a page writing Math.min(a,b,c) is rare and is
        // refused rather than folded into nested calls that might not mean the same thing.
        if ((mapped is "min" or "max") && args.Length != 2) return null;
        if (mapped is not ("min" or "max" or "atan2") && args.Length != 1) return null;
        return new Fn(mapped, args);
    }

    /// <summary>A variable's dotted name, as this pass keys state by. Null for anything computed.</summary>
    private static string? Path(Node? e) => e switch
    {
        Identifier id => id.Name,
        MemberExpression { Computed: false, Object: Identifier o, Property: Identifier p } => o.Name + "." + p.Name,
        MemberExpression { Computed: false, Object: MemberExpression inner, Property: Identifier p }
            when Path(inner) is { } head => head + "." + p.Name,
        _ => null,
    };

    // ---- recognising a write ---------------------------------------------------------------------

    /// <summary>
    /// A write to an element, recorded when its value is a closed form. Returns true when the
    /// assignment WAS such a write, whether or not it could be expressed - so the caller does not
    /// then treat it as an ordinary variable.
    /// </summary>
    private bool Write(MemberExpression target, AssignmentExpression a)
    {
        // el.style.<prop> = ...
        string? property = null;
        Expression element;
        if (target is { Computed: false, Property: Identifier prop, Object: MemberExpression { Property: Identifier { Name: "style" }, Object: { } owner } })
        { property = "style." + prop.Name; element = owner; }
        else if (target is { Computed: false, Property: Identifier direct, Object: { } obj }
                 && direct.Name is "textContent" or "innerText")
        { property = direct.Name; element = obj; }
        else return false;

        if (property == null) return false;

        var id = Element(element);
        if (id == null) return true;                      // a write, but to an element not named here

        if (property == "style.transform")
        {
            var (parts, touches) = Translate(a.Right);
            foreach (var p in parts) if (p != null) { _found.Add(new Found(id, property, parts, touches)); break; }
            return true;
        }
        var one = new[] { Scalar(a.Right, property) };
        if (one[0] != null) _found.Add(new Found(id, property, one));
        return true;
    }

    /// <summary>The element id a write targets, resolved through aliases and unrolled loop indices.</summary>
    private string? Element(Expression e) => e switch
    {
        Identifier id => _aliases.TryGetValue(id.Name, out var to) ? to : null,
        CallExpression call => IdOf(call),
        _ => null,
    };

    /// <summary>A length or opacity write: the number, with any <c>px</c> suffix dropped.</summary>
    private string? Scalar(Expression value, string property)
    {
        // A text write is a string; the expression language has no strings, so only geometry and
        // opacity can move into the scene. Said once per page rather than per write.
        if (property is "textContent" or "innerText") return null;
        var e = Strip(value);
        return e == null ? null : Eval(e)?.Text;
    }

    /// <summary>
    /// The two numbers inside a transform string, as the page builds it.
    /// </summary>
    /// <remarks>
    /// A page writes <c>'translate(' + x + 'px,' + y + 'px)'</c> or the single-axis forms. The shapes
    /// are recognised rather than the string parsed, because the page never builds the string here -
    /// only its parts are needed, and each is an expression in its own right.
    /// </remarks>
    private (string?[] Parts, bool[] Touches) Translate(Expression value)
    {
        var flat = new List<object>();   // strings and expressions, in order
        Flatten(value, flat);
        var head = flat.Count > 0 ? flat[0] as string : null;
        var neither = (new string?[2], new[] { true, true });
        if (head == null) return neither;

        if (head.StartsWith("translateX(", StringComparison.Ordinal) && flat.Count >= 2 && flat[1] is Expression x)
            return (new[] { Eval(x)?.Text, null }, new[] { true, false });
        if (head.StartsWith("translateY(", StringComparison.Ordinal) && flat.Count >= 2 && flat[1] is Expression y)
            return (new[] { (string?)null, Eval(y)?.Text }, new[] { false, true });
        if (head.StartsWith("translate(", StringComparison.Ordinal) && flat.Count >= 4
            && flat[1] is Expression tx && flat[3] is Expression ty)
            return (new[] { Eval(tx)?.Text, Eval(ty)?.Text }, new[] { true, true });
        return neither;
    }

    /// <summary>A concatenation as its alternating literal and expression parts.</summary>
    private static void Flatten(Expression e, List<object> into)
    {
        if (e is BinaryExpression { Operator: Operator.Addition } b) { Flatten(b.Left, into); Flatten(b.Right, into); return; }
        if (e is StringLiteral s)
        {
            if (into.Count > 0 && into[into.Count - 1] is string prev) into[into.Count - 1] = prev + s.Value;
            else into.Add(s.Value);
            return;
        }
        into.Add(e);
    }

    /// <summary>The number inside <c>x + 'px'</c>, or the expression itself when there is no suffix.</summary>
    private static Expression? Strip(Expression e)
    {
        if (e is BinaryExpression { Operator: Operator.Addition, Right: StringLiteral suffix } b
            && (suffix.Value is "px" or "%" or "em" or "rem")) return b.Left;
        if (e is StringLiteral) return null;
        return e;
    }

    // ---- putting the motion into the scene -------------------------------------------------------

    /// <summary>
    /// Replaces each named slot in a scene template with the expression that computes it.
    /// </summary>
    /// <remarks>
    /// A template reads <c>x=$player_x</c> for a value the chip sends and <c>t=[$a_t_0,$a_t_1]</c> for
    /// a pair; the vector mod reads an expression in either place as a quoted string beginning with
    /// <c>=</c>. So one substitution serves both, and the bracketed pair needs no special handling.
    ///
    /// The slot leaves <paramref name="values"/> at the same time. Otherwise its last value would go
    /// out once, as a name the scene no longer mentions - harmless, but it would make the payload
    /// disagree with the structure, and a payload that carries slots nothing reads is exactly the
    /// thing that made the transform question hard to answer the first time.
    ///
    /// A name that is not in the template is skipped rather than reported: the compiler works from
    /// the slot table this same emit produced, so a miss means the scene genuinely stopped drawing
    /// that element, which is not an error.
    /// </remarks>
    internal static string Bake(string template, IReadOnlyDictionary<string, string> expressions,
                                Dictionary<string, SceneSlots.Value>? values = null)
    {
        var sb = new StringBuilder(template.Length + expressions.Count * 32);
        var i = 0;
        while (i < template.Length)
        {
            var at = template.IndexOf('$', i);
            if (at < 0) { sb.Append(template, i, template.Length - i); break; }
            sb.Append(template, i, at - i);

            var end = at + 1;
            while (end < template.Length && (char.IsLetterOrDigit(template[end]) || template[end] == '_')) end++;
            var name = template.Substring(at + 1, end - at - 1);
            if (name.Length > 0 && expressions.TryGetValue(name, out var expr))
            {
                sb.Append('"').Append('=').Append(expr).Append('"');
                values?.Remove(name);
            }
            else sb.Append(template, at, end - at);
            i = end;
        }
        return sb.ToString();
    }

    // ---- walking ---------------------------------------------------------------------------------

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

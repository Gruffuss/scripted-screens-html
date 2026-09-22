using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lua;
using Lua.Standard;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// The transpiler, checked against the thing it claims to replace.
/// </summary>
/// <remarks>
/// Every page's script is run <b>twice</b> - as the original JavaScript under Jint, and as the
/// transpiled Lua under the game's own Lua interpreter - fed the same frames, and every write each
/// makes to the DOM is compared. A translation that merely compiles is worth nothing; this is what
/// separates "it parses" from "it does the same thing".
///
/// Both runs share one deterministic <c>Math.random</c>. The generator's multiplier is small on
/// purpose: Lua 5.4 has an integer subtype and JavaScript does not, so any product past 2^53 gives
/// two different answers, and the test would diff on its own arithmetic rather than on the code
/// under test.
/// </remarks>
internal static class JsToLuaTests
{
    /// <summary>Declared before Pages: static initialisers run in order, and Pages reads it.</summary>
    private static readonly string[] None = Array.Empty<string>();

    /// <summary>Every page in the repository that carries a script, with how many frames to drive it.</summary>
    private static readonly (string Path, int Frames, string[] Known)[] Pages =
    {
        (@"examples\07-game.lua", 3000, None),   // physics, spawning, collision, game over and restart
        (@"examples\09-transition.lua", 20, None),
        (@"AtmoDark.lua", 60, None),
        (@"AtmoLight.lua", 60, None),
        // The one divergence in the repository, and it is a language difference rather than a
        // translation bug. The page saves a scroll position with `kept[id] = el.scrollTop` and
        // restores it by walking `Object.keys(kept)`. In JavaScript, assigning `undefined` still
        // CREATES the property, so the key is there and the restore runs; in Lua, assigning nil
        // creates nothing, so there is no key and the restore does not. Preserving it would mean an
        // `undefined` sentinel threaded through every comparison, coercion and truthiness test in
        // the prelude - a large, risky change to keep a scroll position that a compiled console does
        // not have. Listed here rather than papered over, so a NEW divergence on this page still
        // fails the test.
        (@"AtmoApple.lua", 60, new[] { "bindList.scrollTop", "log.scrollTop" }),
    };

    internal static void Run(Action<bool, string> check)
    {
        var root = Root();
        if (root == null) { check(false, "js->lua: cannot find the ScriptedScreensHtml folder"); return; }

        foreach (var (path, frames, known) in Pages)
        {
            var file = Path.Combine(root, path);
            if (!File.Exists(file)) { check(false, $"js->lua: {path} is missing"); continue; }

            var script = Regex.Match(File.ReadAllText(file), "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
            if (script.Length == 0) continue;

            var lua = JsToLua.Compile(script, out var problems);
            if (lua == null)
            {
                check(false, $"js->lua: {path} does not compile - {string.Join("; ", problems.Take(3))}");
                continue;
            }
            check(true, $"js->lua: {path} compiles");

            string? failure;
            try
            {
                failure = Diff(root, script, lua, frames, known);
            }
            catch (Exception ex)
            {
                // The message names the construct, so it is worth showing rather than swallowing.
                check(false, $"js->lua: {path} threw - {First(ex.Message)}");
                continue;
            }
            check(failure == null, failure ?? $"js->lua: {path} matches the original over {frames} frames"
                                  + (known.Length == 0 ? "" : $" ({known.Length} known divergence(s) allowed)"));
        }

        Writes(check);
        Scoping(check);
        Mapping(check);
        PairCollision(check);
        Manifest(check);
        Refused(check);
        Semantics(check);
        Refusals(check);
    }

    /// <summary>
    /// Constructs outside the supported subset, which must be REPORTED and not approximated. This
    /// is the load-bearing claim of the whole design: a page that refuses to compile names its line
    /// and can be fixed, while a page that compiles into something subtly different cannot even be
    /// noticed. Each entry is valid JavaScript that the transpiler is expected to turn down.
    /// </summary>
    private static readonly (string Name, string Source)[] MustRefuse =
    {
        // A TAGGED template is still refused: a tag is a function taking the pieces and the
        // values separately, which is a different thing from a template and not translated.
        ("a tagged template", "function tag(p, v){ return p[0]; } var s = tag`a${1}b`;"),
        ("async/await", "async function f() { await 1; }"),
        ("a generator", "function* g() { yield 1; }"),
        ("a real regular expression", "var s = 'a1'.replace(/[0-9]+/g, '');"),
        ("a spread argument", "function f() {} var xs = [1]; f(...xs);"),
        ("an undeclared name", "missingThing.doSomething();"),
        ("a labelled break", "outer: for (var i = 0; i < 2; i++) { break outer; }"),
        ("optional chaining", "var o = {}; var v = o?.a?.b;"),
        ("getters on a class", "var o = { set x(v) { this._x = v; } };"),
    };

    /// <summary>
    /// Behaviour that has to survive, one snippet each, run both ways like a page. Most of these
    /// come from an audit that demonstrated each as a real mistranslation; a few pin something that
    /// was already right so a later change cannot quietly break it. Each writes its answer with
    /// out(), so the existing diff machinery does the comparing.
    /// </summary>
    private static readonly (string Name, string Source)[] MustMatch =
    {
        // Template literals. The second one is the whole reason they cannot go through the `+`
        // translation: every hole in a template is string-coerced, so `${1}${2}` is "12". Routing it
        // through js_add would quietly make it 3 - arithmetic where the page wrote markup.
        ("a template literal interpolates",
         @"const n = 3, u = 'kPa'; out(`level ${n} ${u}`);"),
        ("a template coerces, it does not add",
         @"out(`${1}${2}`);"),
        ("a template with no holes is its text",
         @"out(`plain text`);"),
        ("an empty template is the empty string",
         @"out(`` + 'x');"),
        ("a template hole may be any expression",
         @"const a = [1,2,3]; out(`n=${a.length} half=${6/2} f=${(1.5).toFixed(1)}`);"),
        ("templates nest",
         @"const w = 40; out(`style=""width:${`${w}%`}""`);"),
        ("a template holds a number the way + does",
         @"const x = 0.1 + 0.2; out(`${x}` + '|' + ('' + x));"),
        ("an inner let shadows, not assigns",
         @"let total = 0; function tally(xs){ let total = 0; for (let j=0;j<xs.length;j++) total += xs[j]; return total; } var t = tally([1,2,3]); out(t + '/' + total);"),
        ("a nested function shadows, not replaces",
         @"function draw(){ return 'outer'; } function setup(){ function draw(){ return 'inner'; } return draw(); } out(setup() + '/' + draw());"),
        ("a function may call one declared below it",
         @"function a(){ return b(); } function b(){ return 'below'; } out(a());"),
        ("var is function-scoped, not block-scoped",
         @"function f(c){ if (c) { var y = 1; } return y; } out(String(f(true)) + '/' + String(f(false)));"),
        ("var survives a for block",
         @"function f(){ for (var i = 0; i < 3; i++) {} return i; } out(f());"),
        ("continue before a later local",
         @"var s = 0; for (let i=0;i<4;i++){ if (i===2) continue; let v = i*2; s += v; } out(s);"),
        ("continue after a sibling loop",
         @"var s = ''; for (let i=0;i<3;i++){ for (let j=0;j<2;j++){ s += '.'; } if (i===1) continue; s += i; } out(s);"),
        ("continue in a for-of",
         @"var s = 0; for (const v of [1,2,3,4]) { if (v === 2) continue; s += v; } out(s);"),
        ("a ternary evaluates one branch only",
         @"var n = 0; function bump(){ n++; return 1; } var x = false ? bump() : 2; out(x + '/' + n);"),
        ("&& and || short-circuit",
         @"var n = 0; function bump(){ n++; return 1; } var a = false && bump(); var b = true || bump(); out(String(a) + '/' + String(b) + '/' + n);"),
        ("x == null catches undefined too",
         @"var o = {}; out(String(o.missing == null) + '/' + String(null == null) + '/' + String(0 == null));"),
        ("?? keeps a falsy but defined left side",
         @"out(String(0 ?? 9) + '/' + String(null ?? 9) + '/' + String('' ?? 9));"),
        ("a literal as the object of a member access",
         @"out(({ a: 7 }).a + '/' + [3,4].length + '/' + (5).toFixed(1));"),
        ("% keeps the sign of the dividend",
         @"out((-1 % 3) + '/' + (1 % 3) + '/' + (-7 % 2));"),
        ("an early return leaves the function",
         @"function f(x){ if (x) { return 'yes'; } return 'no'; } out(f(true) + '/' + f(false));"),
        ("a getter sees its receiver",
         @"var o = { id: 'k', get label(){ return 'id=' + this.id; } }; out(o.label);"),
        ("a number prints as JavaScript prints it",
         @"out(String(1/3) + '/' + String(0.1+0.2) + '/' + String(3.0) + '/' + String(100));"),
    };

    private static void Semantics(Action<bool, string> check)
    {
        var root = Root();
        if (root == null) return;
        var wrong = new List<string>();
        foreach (var (name, source) in MustMatch)
        {
            // out() is the one thing the snippet needs, and it is the same write on both sides
            var js = "function out(v){ document.getElementById('r').textContent = String(v); }" + source;
            var lua = JsToLua.Compile(js, out var problems);
            if (lua == null) { wrong.Add($"{name}: does not compile ({problems.FirstOrDefault()})"); continue; }
            try
            {
                var a = RunJs(js, 0).TryGetValue("r.textContent", out var x) ? x : "(none)";
                var b = RunLua(root, lua, 0).TryGetValue("r.textContent", out var y) ? y : "(none)";
                if (a != b) wrong.Add($"{name}: js={a} lua={b}");
            }
            catch (Exception ex) { wrong.Add($"{name}: threw - {First(ex.Message)}"); }
        }
        check(wrong.Count == 0, wrong.Count == 0
            ? $"js->lua: all {MustMatch.Length} semantic cases match the original"
            : $"js->lua: {wrong.Count} semantic case(s) wrong - {string.Join("; ", wrong.Take(4))}");
    }

    /// <summary>Each one must be turned down, and the report must say where.</summary>
    private static void Refusals(Action<bool, string> check)
    {
        var accepted = new List<string>();
        var silent = new List<string>();
        foreach (var (name, source) in MustRefuse)
        {
            var lua = JsToLua.Compile(source, out var problems);
            if (lua != null) accepted.Add(name);
            else if (problems.Count == 0) silent.Add(name);
        }
        // `??` is supported rather than refused, so it is checked for the thing that distinguishes
        // it from `||`: a falsy-but-defined left side is kept.
        var nullish = JsToLua.Compile("var a = 0; var b = a ?? 9; var c = null; var d = c ?? 9;", out _);
        check(nullish != null && !nullish.Contains("js_or"), "js->lua: ?? does not compile to ||");

        check(accepted.Count == 0, accepted.Count == 0
            ? $"js->lua: all {MustRefuse.Length} unsupported constructs are reported, not approximated"
            : $"js->lua: silently accepted - {string.Join(", ", accepted)}");
        if (silent.Count > 0) check(false, $"js->lua: refused with no reason given - {string.Join(", ", silent)}");
    }

    /// <summary>
    /// Pages that cannot be compiled, and the reason each must give. A page that draws on a canvas
    /// or builds DOM has no translation today - `innerHTML` is a structural change, not a value -
    /// and the point is that it is turned down <b>at compile time with the method named</b>, rather
    /// than compiling and then failing on a console with nothing in the page to point at.
    /// </summary>
    private static readonly (string Path, string[] Reasons)[] CannotCompile =
    {
        (@"examples\05-script.lua", new[] { "appendChild", "getContext", "clearRect" }),
    };

    /// <summary>
    /// The transpiler's list of what the prelude provides, against the prelude itself. The list is
    /// what turns a missing method from a runtime surprise into a compile-time report, so the two
    /// drifting apart quietly would give back exactly the failure it was added to remove - a page
    /// that compiles and then dies on a console.
    /// </summary>
    private static void Manifest(Action<bool, string> check)
    {
        var root = Root();
        if (root == null) return;
        var prelude = File.ReadAllText(Path.Combine(root, "JsPrelude.lua"));
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(prelude, @"function\s+(?:String|Array|Number)Methods\.([A-Za-z_]\w*)")) defined.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(prelude, @"^(?:String|Array|Number)Methods\.([A-Za-z_]\w*)\s*=", RegexOptions.Multiline)) defined.Add(m.Groups[1].Value);

        var absent = defined.Where(n => !JsToLua.PreludeMethods.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        check(absent.Count == 0, absent.Count == 0
            ? $"js->lua: the method manifest covers all {defined.Count} the prelude defines"
            : $"js->lua: the prelude defines {string.Join(", ", absent)}, which the manifest would refuse");
    }

    /// <summary>
    /// What the script of the repository's most demanding page writes, and when. The split matters
    /// more than the list: the compiler runs AFTER the page's setup code, so a setup-only write is
    /// already in the geometry and needs no slot, while a runtime write needs one or the page has to
    /// be refused. Every `innerHTML` on this page is setup, which is why it is the page to make work
    /// first - the Atmo pages rebuild themselves at run time and need state enumeration instead.
    /// </summary>
    /// <summary>
    /// An element bound INSIDE the function that writes it still resolves to its literal id, and a
    /// name two functions bind differently resolves to nothing rather than to the wrong element.
    /// </summary>
    /// <remarks>
    /// Both halves of one change, and they pull against each other. Descending into function bodies
    /// is what makes `function render() { const frame = $('frame'); frame.innerHTML = ... }` -
    /// every Atmo page - resolvable at all; before it, those writes were reported as being on "an
    /// element chosen at run time" with the literal id three lines above. But the alias table has no
    /// scoping, so descending also lets two functions collide on a name, and the old single-slot
    /// table would have let the last one seen silently rename the other's writes. That failure draws
    /// the wrong element with no warning, which is worse than not compiling.
    /// </remarks>
    private static void Scoping(Action<bool, string> check)
    {
        const string inner = @"
            function render() {
              const frame = document.getElementById('frame');
              frame.textContent = 'hello';
            }
            setInterval(render, 100);";
        var (w1, n1) = DomWrites.Of(inner);
        var found = w1.FirstOrDefault(w => w.Property == "textContent");
        check(found?.Id == "frame" && n1.Count == 0,
            found?.Id == "frame" && n1.Count == 0
                ? "domwrites: an element bound inside the function that writes it resolves to its id"
                : $"domwrites: a function-local binding did not resolve - id {found?.Id ?? "(none)"}, {n1.Count} note(s)");

        const string clash = @"
            function a() { const el = document.getElementById('one'); el.textContent = 'x'; }
            function b() { const el = document.getElementById('two'); el.textContent = 'y'; }
            function step() { a(); b(); }
            setInterval(step, 100);";
        var (w2, n2) = DomWrites.Of(clash);
        var named = w2.Where(w => w.Property == "textContent" && w.Id != null).ToList();
        check(named.Count == 0 && n2.Count > 0,
            named.Count == 0 && n2.Count > 0
                ? "domwrites: a name two functions bind differently is reported, not guessed at"
                : $"domwrites: an ambiguous alias resolved anyway to {string.Join(", ", named.Select(x => x.Id))} - a page would draw into the wrong element");
    }

    private static void Writes(Action<bool, string> check)
    {
        var root = Root();
        if (root == null) return;
        var script = Regex.Match(File.ReadAllText(Path.Combine(root, @"examples\07-game.lua")), "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        var (writes, _) = DomWrites.Of(script);

        var runtime = writes.Where(w => w.Runtime && w.Id != null).Select(w => w.Id + "." + w.Property).Distinct().ToHashSet(StringComparer.Ordinal);
        var setup = writes.Where(w => !w.Runtime && w.Id != null).Select(w => w.Id + "." + w.Property).Distinct().ToHashSet(StringComparer.Ordinal);

        // driven every frame by draw(), so each needs a slot
        var mustBeRuntime = new[]
        {
            "legA.style.height", "legB.style.height", "legA.style.top", "legB.style.top",
            "bootA.style.top", "bootB.style.top", "player.style.transform",
            "ridgeFar.style.transform", "ridgeNear.style.transform", "domes.style.transform",
            "score.textContent",
        };
        // written once by shape()/fit()/scenery() before the compiler ever sees the page
        var mustBeSetup = new[]
        {
            "stars.innerHTML", "pebbles.innerHTML", "obstacles.innerHTML",
            "groundLine.style.top", "groundLine.style.height", "overlay.style.bottom",
        };

        var wrong = mustBeRuntime.Where(w => !runtime.Contains(w)).Select(w => w + " should be runtime")
            .Concat(mustBeSetup.Where(w => !setup.Contains(w) || runtime.Contains(w)).Select(w => w + " should be setup only"))
            .ToList();

        check(wrong.Count == 0, wrong.Count == 0
            ? $"js->lua: 07-game's writes split correctly - {runtime.Count} runtime, {setup.Count} setup only"
            : $"js->lua: writes misclassified - {string.Join("; ", wrong)}");
    }

    /// <summary>
    /// The acceptance test for the mapping: every runtime write 07-game makes, against the scene the
    /// emitter really produced for it (captured from the game, checked in beside this file). This is
    /// the one check that spans both halves of the compiler - what the script writes, and what the
    /// renderer exposes - so it is where a mismatch between them shows up.
    ///
    /// The elements this page moves are all `#player div { position: absolute }`, so they are out of
    /// flow and their boxes are their own business. An in-flow element would be refused, correctly.
    /// </summary>
    /// <summary>The player's own absolute top in the captured scene, which its children's tops are relative to.</summary>
    private const double PlayerTop = 85;

    private static void Mapping(Action<bool, string> check)
    {
        var root = Root();
        if (root == null) return;
        var scenePath = Path.Combine(AppContext.BaseDirectory, "07-game.scene.txt");
        if (!File.Exists(scenePath)) scenePath = Path.Combine(root, "..", "ScriptedScreensHtml.Tests", "07-game.scene.txt");
        if (!File.Exists(scenePath)) { check(false, "js->lua: the captured 07-game scene is missing"); return; }

        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        SceneSlots.Split(File.ReadAllText(scenePath), values);
        var available = values.Keys.ToHashSet(StringComparer.Ordinal);

        var script = Regex.Match(File.ReadAllText(Path.Combine(root, @"examples\07-game.lua")), "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        var (writes, _) = DomWrites.Of(script);

        // every element this page drives is a child of #player or a positioned layer
        var mapped = new List<string>();
        var needsGroup = new List<string>();
        var refused = new List<string>();
        foreach (var w in writes.Where(w => w.Runtime && w.Id != null).DistinctBy(w => w.Id + "." + w.Property))
        {
            // Every element this page drives is #player div { position: absolute } or a positioned
            // layer. The player's own top is 85 in this capture, which is the bias its children's
            // `top` writes carry - the scene is absolute and CSS is not.
            // legA holds bootA, and the scene is absolute, so moving legA must move bootA with it:
            // in the capture legA is at y=141 and bootA at y=161, twenty below.
            var inside = w.Id == "legA" ? new[] { ("bootA", 0d, 20d) }
                       : w.Id == "legB" ? new[] { ("bootB", 0d, 12d) }
                       : System.Array.Empty<(string, double, double)>();
            var box = new DomSlots.Box(outOfFlow: true, parentX: 0, parentY: PlayerTop,
                                       hasBackground: false, inside: inside);
            var r = DomSlots.Map(w.Id!, w.Property, box, available);
            if (!r.Mapped) refused.Add($"{w.Id}.{w.Property}: {r.Problem}");
            else if (r.NeedsGroup) needsGroup.Add($"{w.Id}.{w.Property}");
            else mapped.Add($"{w.Id}.{w.Property} -> {string.Join(",", r.Slots)}"
                            + (r.Bias.Any(b => b != 0) ? $" (+{string.Join(",", r.Bias)})" : ""));
        }

        // Moving an element must move what is inside it. The scene is absolute, so a child's box
        // carries its own position and would otherwise stay exactly where it was - the leg would
        // slide and leave its boot behind.
        var legTop = mapped.FirstOrDefault(m => m.StartsWith("legA.style.top ", StringComparison.Ordinal));
        check(legTop != null && legTop.Contains("bootA_y", StringComparison.Ordinal),
            legTop == null ? "js->lua: legA.style.top does not map"
            : legTop.Contains("bootA_y", StringComparison.Ordinal)
                ? $"js->lua: moving legA moves bootA with it - {legTop}"
                : $"js->lua: legA.style.top leaves bootA behind - {legTop}");

        // A position write carries the containing block's origin, and getting that wrong is silent:
        // legA sits at scene y=141 with a CSS top of 56, inside a player whose own top is 85.
        var topWrite = mapped.FirstOrDefault(m => m.StartsWith("legA.style.top ", StringComparison.Ordinal));
        check(topWrite != null && topWrite.Contains($"(+{PlayerTop}", StringComparison.Ordinal),
            topWrite == null ? "js->lua: legA.style.top does not map"
            : topWrite.Contains($"(+{PlayerTop}", StringComparison.Ordinal)
                ? $"js->lua: a position write carries its parent's origin - {topWrite}"
                : $"js->lua: legA.style.top has no bias, so it would draw {PlayerTop} units out - {topWrite}");

        // A size does not, and must not.
        var sizeWrite = mapped.FirstOrDefault(m => m.StartsWith("legA.style.height ", StringComparison.Ordinal));
        check(sizeWrite != null && !sizeWrite.Contains("(+", StringComparison.Ordinal),
            sizeWrite != null && !sizeWrite.Contains("(+", StringComparison.Ordinal)
                ? "js->lua: a size write carries no bias"
                : $"js->lua: legA.style.height should have no bias - {sizeWrite}");

        // What must map today, straight onto the box the emitter already names.
        foreach (var want in new[] { "legA.style.height", "legB.style.height", "legA.style.top", "bootA.style.top", "score.textContent" })
            check(mapped.Any(m => m.StartsWith(want + " ", StringComparison.Ordinal)),
                mapped.FirstOrDefault(m => m.StartsWith(want + " ", StringComparison.Ordinal)) ?? $"js->lua: {want} does not map");

        // The transforms need the wrapper to carry its element's id - a request to the emitter, not
        // a failure. Pinned so the count cannot drift without someone noticing.
        // Nothing should need a group any more: the emitter names the wrappers of the elements a
        // script drives, so a transform is an ordinary slot write like any other. This scene was
        // captured from the game AFTER that change, so a non-zero count here means the emitter
        // stopped naming them.
        check(needsGroup.Count == 0, needsGroup.Count == 0
            ? "js->lua: every transform has a named group in the emitted scene"
            : $"js->lua: {needsGroup.Count} transform(s) have no named group: {string.Join(", ", needsGroup)}");

        foreach (var want in new[] { "player.style.transform", "ridgeFar.style.transform", "domes.style.transform" })
            check(mapped.Any(m => m.StartsWith(want + " ", StringComparison.Ordinal)),
                mapped.FirstOrDefault(m => m.StartsWith(want + " ", StringComparison.Ordinal)) ?? $"js->lua: {want} does not map");

        // className is refused BY DESIGN here - it resolves through the stylesheet, not this table.
        var unexpected = refused.Where(r => !r.Contains("className", StringComparison.Ordinal)).ToList();
        check(unexpected.Count == 0, unexpected.Count == 0
            ? $"js->lua: every value write on 07-game maps ({mapped.Count} direct, {needsGroup.Count} via a group)"
            : $"js->lua: {unexpected.Count} write(s) unmapped - {string.Join("; ", unexpected.Take(4))}");
    }

    /// <summary>Prints what each page writes, split into setup and runtime. Run with --domwrites.</summary>
    internal static void Report()
    {
        var root = Root();
        if (root == null) { Console.WriteLine("no source folder"); return; }
        foreach (var (path, _, _) in Pages.Concat(CannotCompile.Select(c => (c.Path, 0, None))))
        {
            var file = Path.Combine(root, path);
            if (!File.Exists(file)) continue;
            var script = Regex.Match(File.ReadAllText(file), "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
            if (script.Length == 0) continue;
            var (writes, notes) = DomWrites.Of(script);
            var runtime = writes.Where(w => w.Runtime).ToList();
            Console.WriteLine($"\n== {path} == {writes.Count} writes, {runtime.Count} at runtime");
            foreach (var g in runtime.GroupBy(w => w.Id ?? (w.Prefix != null ? w.Prefix + "* (from " + w.Computed + ")" : "<" + w.Computed + ">")).OrderBy(g => g.Key, StringComparer.Ordinal))
                Console.WriteLine($"   {g.Key,-16} {string.Join(" ", g.Select(w => w.Property).Distinct().OrderBy(p => p, StringComparer.Ordinal))}"
                                  + (g.Any(w => w.Classes != null) ? "   states: " + string.Join(" | ", g.Where(w => w.Classes != null).SelectMany(w => w.Classes!).Distinct().Select(c => c.Length == 0 ? "(none)" : c)) : ""));
            var setup = writes.Where(w => !w.Runtime).Select(w => (w.Id ?? "<" + w.Computed + ">") + "." + w.Property).Distinct().ToList();
            if (setup.Count > 0) Console.WriteLine($"   setup only: {string.Join(", ", setup)}");
            foreach (var n in notes) Console.WriteLine($"   note: {n}");
        }
    }

    /// <summary>
    /// Two lines carrying one id, both writing a pair. The scalar case has a first-come rule; a pair
    /// never inserts its bare name, only its components, so the rule could not see the clash and the
    /// two lines shared their slots with the last value silently winning. Latent today because only
    /// the transform group is named - and this is what bounds how many wrappers ever may be.
    /// </summary>
    private static void PairCollision(Action<bool, string> check)
    {
        const string scene = "SCENE w=100 h=100\nG a=[1,2] t=[3,4] id=dup {\n  G a=[5,6] t=[7,8] id=dup {\n    R x=1 y=2 w=3 h=4 f=#FFFFFF\n  }\n}\n";
        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        SceneSlots.Split(scene, values);

        // the first line keeps the friendly names; the second must not reuse them
        var first = values.TryGetValue("dup_t_0", out var a) ? a.Number : float.NaN;
        var shared = values.Count(v => v.Value.IsNumber && Math.Abs(v.Value.Number - 7f) < 0.001f);
        check(Math.Abs(first - 3f) < 0.001f && shared == 1,
            Math.Abs(first - 3f) < 0.001f && shared == 1
                ? "slots: two lines sharing an id do not share their pair slots"
                : $"slots: pair collision - dup_t_0 is {first} (expected 3) and the second line's 7 appears {shared} time(s)");
    }

    private static void Refused(Action<bool, string> check)
    {
        var root = Root();
        if (root == null) return;
        foreach (var (path, reasons) in CannotCompile)
        {
            var script = Regex.Match(File.ReadAllText(Path.Combine(root, path)), "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
            var lua = JsToLua.Compile(script, out var problems);
            var all = string.Join(" ", problems);
            var missing = reasons.Where(r => !all.Contains(r, StringComparison.Ordinal)).ToList();
            check(lua == null && missing.Count == 0,
                lua != null ? $"js->lua: {path} compiled, but it draws on a canvas and cannot"
                : missing.Count > 0 ? $"js->lua: {path} is refused but does not name {string.Join(", ", missing)}"
                : $"js->lua: {path} is refused at compile time, naming {string.Join(", ", reasons)}");
        }
    }

    /// <summary>Null when the two runs agree, else the first few writes that differ.</summary>
    private static string? Diff(string root, string script, string lua, int frames, string[] known)
    {
        var js = RunJs(script, frames);
        var lu = RunLua(root, lua, frames);
        var bad = js.Keys.Union(lu.Keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => Get(js, k) != Get(lu, k))
            .Where(k => Array.IndexOf(known, k) < 0)
            .ToList();
        if (bad.Count == 0) return null;
        var detail = string.Join("; ", bad.Take(3).Select(k => $"{k}: js={Get(js, k)} lua={Get(lu, k)}"));
        return $"js->lua: {bad.Count} of {js.Keys.Union(lu.Keys).Count()} writes differ - {detail}";
    }

    private static string Get(IReadOnlyDictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : "(no write)";

    private static Dictionary<string, string> RunJs(string script, int frames)
    {
        var writes = new Dictionary<string, string>(StringComparer.Ordinal);
        var engine = new Jint.Engine();
        engine.SetValue("__record", new Action<string, string, string>((id, key, value) => writes[id + "." + key] = value));
        engine.Execute(Harness);
        engine.Execute(script);
        for (var i = 0; i < frames; i++) engine.Invoke("__step", (i + 1) * 16.6667);
        return writes;
    }

    private static Dictionary<string, string> RunLua(string root, string lua, int frames)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        Chunk(state, File.ReadAllText(Path.Combine(root, "JsPrelude.lua")), "prelude");
        Chunk(state, lua, "page");
        Chunk(state, Driver(frames), "frames");

        var writes = new Dictionary<string, string>(StringComparer.Ordinal);
        var recorded = state.Environment["DOM"].Read<LuaTable>()["writes"].Read<LuaTable>();
        var key = LuaValue.Nil;
        while (recorded.TryGetNext(key, out var pair))
        {
            key = pair.Key;
            if (key.Type == LuaValueType.String) writes[key.Read<string>()] = Text(pair.Value);
        }
        return writes;
    }

    /// <summary>The same stepping the JavaScript harness does, so any difference is the transpiler's.</summary>
    private static string Driver(int frames) => @"
for i = 1, " + frames.ToString(System.Globalization.CultureInfo.InvariantCulture) + @" do
  local t = i * 16.6667
  if #Pending.frame > 0 then
    local fn = Pending.frame[#Pending.frame]
    Pending.frame = {}
    fn(t)
  else
    for _, timer in ipairs(Pending.timers) do timer.fn(t) end
  end
end";

    private static void Chunk(LuaState state, string text, string name) =>
        state.RunAsync(state.Load(text.AsSpan(), name, state.Environment)).AsTask().GetAwaiter().GetResult();

    /// <summary>A Lua value as the string the JavaScript side would have produced for it.</summary>
    private static string Text(LuaValue v) => v.Type switch
    {
        LuaValueType.String => v.Read<string>(),
        LuaValueType.Number => Num(v.Read<double>()),
        LuaValueType.Boolean => v.Read<bool>() ? "true" : "false",
        LuaValueType.Nil => "undefined",
        // the prelude's stand-in for a written `undefined`, which nil cannot represent in a table
        LuaValueType.Table => "undefined",
        _ => v.Type.ToString(),
    };

    private static string Num(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e15
            ? ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string First(string message)
    {
        var line = message.Split('\n')[0];
        return line.Length > 140 ? line.Substring(0, 140) : line;
    }

    /// <summary>The tests run from bin\, so the source folder is found by walking up.</summary>
    private static string? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ScriptedScreensHtml");
            if (File.Exists(Path.Combine(candidate, "JsPrelude.lua"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // The JavaScript side's recorder, deliberately the same shape as the prelude's so that a
    // difference between the two runs is the transpiler and not the harness.
    private const string Harness = @"
var __el = {};
function __element(id) {
  if (__el[id]) return __el[id];
  var style = new Proxy({}, { set: function (t, k, v) { t[k] = v; __record(id, 'style.' + k, String(v)); return true; } });
  var target = { style: style,
                 setAttribute: function (n, v) { __record(id, '@' + n, String(v)); },
                 getAttribute: function () { return null; },
                 addEventListener: function () {},
                 appendChild: function () {}, removeChild: function () {}, remove: function () {},
                 children: [], childNodes: [], parentNode: null,
                 getBoundingClientRect: function () { return { x: 0, y: 0, width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0 }; } };
  var e = new Proxy(target, { set: function (t, k, v) { t[k] = v; __record(id, k, String(v)); return true; } });
  // classList is a view over className, exactly as the prelude models it: adding a class is a
  // className write and reaches the scene the same way assigning className does. It is attached to
  // the target rather than through the proxy, or setting it would itself be recorded as a write.
  function words() { return String(target.className == null ? '' : target.className).split(/\s+/).filter(function (w) { return w.length; }); }
  target.classList = {
    add: function (c) { var w = words(); if (w.indexOf(c) < 0) { w.push(c); e.className = w.join(' '); } },
    remove: function (c) { var w = words(), i = w.indexOf(c); if (i >= 0) { w.splice(i, 1); e.className = w.join(' '); } },
    toggle: function (c) { if (words().indexOf(c) < 0) { target.classList.add(c); } else { target.classList.remove(c); } },
    contains: function (c) { return words().indexOf(c) >= 0; }
  };
  __el[id] = e;
  return e;
}
var document = { getElementById: __element, documentElement: __element('html'), body: __element('body'),
                 createElement: function (t) { return __element('__new_' + t); },
                 querySelector: function () { return null; }, querySelectorAll: function () { return []; },
                 addEventListener: function () {} };
var window = { innerWidth: 0, innerHeight: 0, addEventListener: function () {} };
var localStorage = { getItem: function () { return null; }, setItem: function () {} };
var console = { log: function () {}, warn: function () {}, error: function () {} };
var performance = { now: function () { return 0; } };
var __frames = [], __timers = [];
function requestAnimationFrame(fn) { __frames.push(fn); return __frames.length; }
function setInterval(fn, ms) { __timers.push(fn); return __timers.length; }
function setTimeout(fn, ms) { __timers.push(fn); return __timers.length; }
function clearInterval() {}
// A rAF page re-registers each frame, so only the newest runs; a timer page keeps its callback.
function __step(t) {
  if (__frames.length) { var fn = __frames.pop(); __frames.length = 0; fn(t); return; }
  for (var i = 0; i < __timers.length; i++) __timers[i](t);
}
// MINSTD, and the multiplier is the point: 2^31 * 16807 stays under 2^53, so doubles and Lua's
// 64-bit integers agree and the two runs spawn the same obstacles.
var __rng = 48271;
Math.random = function () { __rng = (__rng * 16807) % 2147483647; return __rng / 2147483647; };
";
}

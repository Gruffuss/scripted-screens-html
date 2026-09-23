using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ScriptedScreensHtml;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// Why a page does or does not compile, answered from a chip file instead of from a game restart:
/// the script's translation, its runtime writes, and - for a page that builds markup - the whole
/// markup compile, laid out and emitted as a surface does it. `--why page.lua [--verbose]`
/// </summary>
internal static class Probe4
{
    internal static void Run(string path, bool verbose = false)
    {
        verbose |= Environment.GetEnvironmentVariable("WHY_VERBOSE") is { Length: > 0 };
        RunOne(path, verbose);
    }

    private static void RunOne(string path, bool verbose)
    {
        if (path.EndsWith(".snippet", StringComparison.OrdinalIgnoreCase)) { Scene(File.ReadAllText(path)); return; }
        if (path.EndsWith(".reclass", StringComparison.OrdinalIgnoreCase)) { Reclass(File.ReadAllText(path)); return; }
        var text = File.ReadAllText(path);
        // A .html file IS the page; searching it for a long bracket finds one inside its own CSS or
        // script and silently truncates it.
        var page = path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? text : MarkupProbe.Bracketed(text) ?? text;
        // Exploration only: resolve custom properties defined through other custom properties in the
        // page text, standing in for a renderer fix (HtmlRenderer.ResolveVarsCore does not resolve a
        // var() inside the value it finds). Never used by the suite.
        if (Environment.GetEnvironmentVariable("WHY_FLATTEN_VARS") is { Length: > 0 }) page = FlattenVars(page);
        Report(Path.GetFileName(path), page, verbose);
    }

    private static void Report(string name, string page, bool verbose)
    {
        var script = string.Empty;
        foreach (Match m in Regex.Matches(page, "<script[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            script += m.Groups[1].Value + "\n";
        Console.WriteLine($"{name}: page {page.Length} chars, script {script.Length} chars");
        if (script.Trim().Length == 0) { Console.WriteLine("  no script"); return; }

        var translated = JsToLua.Compile(script, out var problems);
        Console.WriteLine($"  translate: {(translated == null ? "FAILED" : translated.Length + " chars")}, {problems.Count} problem(s)");
        foreach (var p in problems) Console.WriteLine("    - " + p);

        var (writes, notes) = DomWrites.Of(script);
        Console.WriteLine($"  writes: {writes.Count}, notes: {notes.Count}");
        foreach (var n in notes) Console.WriteLine("    ! " + n);
        var runtime = writes.Count(w => w.Runtime);
        Console.WriteLine($"  of those, {runtime} run every frame");
        foreach (var w in writes)
            if (w.Runtime) Console.WriteLine($"    {w.Id ?? (w.Prefix != null ? w.Prefix + "*" : w.Computed) ?? "?"}.{w.Property}  (line {w.Line})");

        Compile(page, verbose);
    }

    private static string FlattenVars(string page)
    {
        var defs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(page, @"(--[\w-]+)\s*:\s*([^;}]+)")) defs[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        string Sub(string v, int depth) => depth > 8 ? v : Regex.Replace(v, @"var\((--[\w-]+)\)", m => defs.TryGetValue(m.Groups[1].Value, out var d) ? Sub(d, depth + 1) : m.Value);
        return Regex.Replace(page, @"(--[\w-]+)(\s*:\s*)([^;}]+)", m => m.Groups[1].Value + m.Groups[2].Value + Sub(m.Groups[3].Value, 0));
    }

    /// <summary>Exploration: a page, one element's style attribute changed in place and re-cascaded, its box before and after.</summary>
    private static void Reclass(string text)
    {
        var at = text.IndexOf("\n@@", StringComparison.Ordinal);
        var html = text.Substring(0, at);
        var cmd = text.Substring(at + 3).Trim().Split(' ', 2);
        ResolvedStyle.DefaultFace = FontLibrary.Default();
        HtmlRenderer.SurfaceAspect = 1f;
        OffThread.MainThreadId = Environment.CurrentManagedThreadId;
        OffThread.Job = OffThread.Globals.Take();
        var built = HtmlRenderer.Build(html, FontLibrary.Default());
        var panel = new Panel(built.Root);
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
        panel.Layout(built.ViewportWidth, built.ViewportWidth);
        var ve = built.ById[cmd[0]];
        var node = built.NodeOf[ve];
        Console.WriteLine($"before: {ve.layout.x},{ve.layout.y} {ve.layout.width}x{ve.layout.height} style.width={ve.style.width.value.value}{ve.style.width.value.unit}");
        node.Attributes["style"] = cmd[1];
        built.Reclass(ve, node.Attr("class") ?? string.Empty);
        panel.Layout(built.ViewportWidth, built.ViewportWidth);
        Console.WriteLine($"after:  {ve.layout.x},{ve.layout.y} {ve.layout.width}x{ve.layout.height} style.width={ve.style.width.value.value}{ve.style.width.value.unit}");
    }

    /// <summary>A snippet of markup emitted as a surface emits it, for looking at what the emitter makes of one thing.</summary>
    private static void Scene(string html)
    {
        ResolvedStyle.DefaultFace = FontLibrary.Default();
        HtmlRenderer.SurfaceAspect = 1f;
        OffThread.MainThreadId = Environment.CurrentManagedThreadId;
        OffThread.Job = OffThread.Globals.Take();
        var built = HtmlRenderer.Build(html, FontLibrary.Default());
        var panel = new Panel(built.Root);
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
        var boxes = new Dictionary<VisualElement, OffThread.Box>();
        OffThread.Boxes = boxes;
        panel.Layout(built.ViewportWidth, built.ViewportWidth);
        OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());
        OffThread.Active = true;
        var o = VectorEmitter.Emit(built, built.Root, built.ViewportWidth, built.ViewportWidth);
        OffThread.Active = false;
        Console.WriteLine(new string(o.Chars, 0, o.Length));
    }

    /// <summary>
    /// The compiled chunk run as the chip runs it: loaded, its first render, a few seconds of frames,
    /// and every click region pressed once - with what each wrote, and whether any of it failed.
    /// </summary>
    private static void Run(CompiledPage.Result compiled)
    {
        var state = Lua.LuaState.Create();
        Lua.Standard.OpenLibsExtensions.OpenStandardLibraries(state);
        string? Do(string text, string name)
        {
            try { state.RunAsync(state.Load(text.AsSpan(), name, state.Environment)).AsTask().GetAwaiter().GetResult(); return null; }
            catch (Exception ex) { return ex.Message.Split('\n')[0]; }
        }
        Dictionary<string, Lua.LuaValue> Payload()
        {
            var sent = new Dictionary<string, Lua.LuaValue>(StringComparer.Ordinal);
            // the real table while the colour watch (below) stands in for it
            if (state.Environment["__REAL"].TryRead<Lua.LuaTable>(out var table) || state.Environment["PAYLOAD"].TryRead(out table))
            {
                var key = Lua.LuaValue.Nil;
                while (table!.TryGetNext(key, out var pair)) { key = pair.Key; if (key.TryRead<string>(out var k)) sent[k] = pair.Value; }
            }
            return sent;
        }
        // Exploration: `__mark(name)` in a hand-edited chunk (WHY_LUA_IN) charges what was allocated since
        // the previous mark to `name`, for finding which lines of a page's Lua allocate; WHY_PROFILE prints them.
        state.Environment["__alloc"] = new Lua.LuaFunction("__alloc", (ctx, _) =>
            new System.Threading.Tasks.ValueTask<int>(ctx.Return((double)GC.GetAllocatedBytesForCurrentThread())));
        Do("__PROF, __LAST = {}, 0 function __mark(n) local now = __alloc() __PROF[n] = (__PROF[n] or 0) + (now - __LAST) __LAST = __alloc() end", "marks");
        // Exploration: a fixed seed, so two builds of a self-simulating page can be compared value for value.
        if (Environment.GetEnvironmentVariable("WHY_SEED") is { Length: > 0 } seed) Do("math.randomseed(" + int.Parse(seed) + ")", "seed");
        // Nothing is sent from here: the flush has no surface, so what a frame wrote stays in PAYLOAD.
        // Timed in two, as installing does it on the game thread (ChipHost.LoadInto): parsing and
        // compiling the chunk, which needs no chip, and its first run, which needs the chip's VM.
        string? load = null;
        var l0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var l1 = l0;
        try
        {
            var chunk = state.Load(compiled.Lua!.AsSpan(), "page", state.Environment);
            l1 = System.Diagnostics.Stopwatch.GetTimestamp();
            state.RunAsync(chunk).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) { load = ex.Message.Split('\n')[0]; }
        var l2 = System.Diagnostics.Stopwatch.GetTimestamp();
        var first = Payload();
        Console.WriteLine($"  run: load {(load == null ? "ok" : "FAILED - " + load)}, first render wrote {first.Count} value(s)");
        Console.WriteLine($"  install: {(l1 - l0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms parsing and compiling the chunk, "
                          + $"{(l2 - l1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms its first run");
        var leaks = first.Where(p => p.Value.ToString().Contains("98765") || p.Value.ToString().Contains("#0F0")).Select(p => p.Key).ToList();
        if (leaks.Count > 0) Console.WriteLine($"    SENTINEL in {leaks.Count} value(s): {string.Join(", ", leaks.Take(6))}");
        // Every value a slot is written from here on, kept aside of PAYLOAD, so a colour slot sent what
        // the renderer cannot read as a colour shows here rather than as magenta on a console.
        Do("__REAL, __W = PAYLOAD, {} PAYLOAD = setmetatable({}, { __newindex = function(_, k, v) "
           + "local s = __W[k] if s == nil then s = {} __W[k] = s end s[v] = true rawset(__REAL, k, v) end })", "watch");
        var frames = Do("for i = 1, 180 do frame(1 / 60) end", "frames");
        Console.WriteLine($"  run: 3 s of frames {(frames == null ? "ok" : "FAILED - " + frames)}, {Payload().Count} value(s) waiting");
        var clicks = System.Text.RegularExpressions.Regex.Matches(compiled.Lua!, @"DOM\.on\(""([^""]+)"", ""click""").Select(m => m.Groups[1].Value).ToList();
        var failed = new List<string>();
        foreach (var id in clicks)
            if (Do($"event(\"{id}\", \"click\", 0, 0)", "click " + id) is { } why) failed.Add(id + ": " + why);
        // Every region, hidden or not: a row past its list's length sits under `v = 0` on the console
        // and cannot be pressed there, so a failure here on such a row is the probe's, not the page's.
        Console.WriteLine($"  run: {clicks.Count} click region(s) pressed, {failed.Count} failed");
        foreach (var f in failed.Take(8)) Console.WriteLine("    " + f);
        Do("PAYLOAD = __REAL", "unwatch");
        Colours(compiled.Structure, first, state.Environment["__W"]);
        // Exploration: every value the frames and presses wrote, for comparing two builds of a page.
        if (Environment.GetEnvironmentVariable("WHY_PAYLOAD_AFTER") is { Length: > 0 } after)
            File.WriteAllLines(after, Payload().OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " = " + p.Value));

        // What one frame of the running page allocates in the chip, and whose it is. Lua-CSharp is
        // .NET, so its strings, tables and closures are .NET allocations and this is a measurement,
        // not an argument. `ss` is a stub whose calls do nothing, so the send is priced too, and each
        // figure is 40 frames minus an empty run of the same chunk. Frames are a second long, so a
        // page's interval fires once in each: for these pages a frame here is one render.
        long PerFrame(string ablate)
        {
            Do(ablate + "\nfunction __tick(n) for i = 1, n do frame(1) end end\n__tick(3)", "ablate");
            var a = GC.GetTotalAllocatedBytes(true);
            Do("__tick(0)", "tick");
            var b = GC.GetTotalAllocatedBytes(true);
            Do("__tick(40)", "tick");
            var c = GC.GetTotalAllocatedBytes(true);
            return (c - b - (b - a)) / 40;
        }
        var whole = PerFrame("local s = {} function s.set_props() end function s.commit() end function s.element() return s end\n"
                             + "ss = { ui = { surface = function() return s end } }");
        var noLabel = PerFrame("DOM.label = function() end");
        var noWrites = PerFrame("DOM.bind = function() end");
        Console.WriteLine($"  run: a frame allocates {whole} B in the chip: {whole - noLabel} B building labels, "
                          + $"{noLabel - noWrites} B in the other slot writes and the send, {noWrites} B the page's own code");
        if (Environment.GetEnvironmentVariable("WHY_PROFILE") is { Length: > 0 } setup)
        {
            // WHY_PROFILE is Lua run first - `PAGE.st.tab = 'atmo'` - or just `1`.
            Do((setup == "1" ? "" : setup) + " frame(1) __PROF = {} __LAST = __alloc() for i = 1, 40 do frame(1) end", "profile");
            if (state.Environment["__PROF"].TryRead<Lua.LuaTable>(out var prof))
            {
                var rows = new List<(string, double)>();
                var key = Lua.LuaValue.Nil;
                while (prof.TryGetNext(key, out var pair)) { key = pair.Key; rows.Add((key.ToString(), pair.Value.Read<double>() / 40)); }
                foreach (var (n, b) in rows.OrderByDescending(r => r.Item2)) Console.WriteLine($"    profile {n}: {b:0} B a frame");
            }
        }
        // Exploration: WHY_ALLOC="lua" prices one more ablation on top of the last, e.g. a stubbed function.
        if (Environment.GetEnvironmentVariable("WHY_ALLOC") is { Length: > 0 } extra)
            Console.WriteLine($"  run: with `{extra}`, {PerFrame(extra)} B a frame");
        if (Environment.GetEnvironmentVariable("WHY_PAYLOAD") is { Length: > 0 } path)
            File.WriteAllLines(path, first.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " = " + p.Value));
    }

    /// <summary>
    /// A plain chunk (PlainPage) run as its chip runs it: loaded once in an environment of its own that
    /// falls through to the chip's globals (ChipHost.LoadInto), with a stand-in `ss` that records every
    /// element it makes and every payload it sends; then its `tick` called as the chip's runtime calls
    /// the stored one (ChipHost.ChainTick), `ticks` times. The author's program has a `tick` of its own
    /// that counts its calls. Returns what happened, in order; a failure is a line starting `FAILED`.
    /// </summary>
    internal static List<string> DrivePlain(string lua, int ticks, double dt)
    {
        var state = Lua.LuaState.Create();
        Lua.Standard.OpenLibsExtensions.OpenStandardLibraries(state);
        var log = new List<string>();
        string? Do(string text, string name, Lua.LuaTable? env = null)
        {
            try { state.RunAsync(state.Load(text.AsSpan(), name, env ?? state.Environment)).AsTask().GetAwaiter().GetResult(); return null; }
            catch (Exception ex) { return ex.Message.Split('\n')[0]; }
        }
        var stub = Do(@"
LOG = {}
local function put(s) LOG[#LOG + 1] = s end
local function show(v)
  if type(v) == 'string' then return '""' .. v .. '""' end
  if type(v) ~= 'table' then return tostring(v) end
  local keys = {}
  for k in pairs(v) do keys[#keys + 1] = k end
  table.sort(keys, function(a, b) return tostring(a) < tostring(b) end)
  local parts = {}
  for _, k in ipairs(keys) do
    parts[#parts + 1] = tostring(k) .. '=' .. (k == 'src' and ('<' .. #v[k] .. ' chars>') or show(v[k]))
  end
  return '{' .. table.concat(parts, ',') .. '}'
end
local surface = {}
function surface:element(def)
  put('element ' .. def.id .. ' ' .. def.type .. ' rect=' .. show(def.rect) .. ' props=' .. show(def.props))
  local handle = {}
  function handle:set_props(p) put('set_props ' .. def.id .. ' ' .. show(p)) end
  return handle
end
function surface:get(id) return { id = id, type = 'html', rect = { unit = 'px', x = 0, y = 0, w = 460, h = 460 } } end
function surface:commit() put('commit') end
ss = { ui = { surface = function(name) put('surface ' .. name) return surface end } }
AUTHOR_TICKS = 0
function tick(dt) AUTHOR_TICKS = AUTHOR_TICKS + 1 end
AUTHOR = tick
", "stub");
        if (stub != null) return new List<string> { "FAILED stub: " + stub };
        var env = new Lua.LuaTable();
        env.Metatable = new Lua.LuaTable();
        env.Metatable["__index"] = state.Environment;
        if (Do(lua, "page", env) is { } load) return new List<string> { "FAILED load: " + load };
        state.Environment["PAGE_TICK"] = env["tick"];
        var run = Do($"for i = 1, {ticks} do PAGE_TICK({dt.ToString(System.Globalization.CultureInfo.InvariantCulture)}) LOG[#LOG + 1] = 'tick ' .. i end"
                     + " LOG[#LOG + 1] = 'author ticks ' .. AUTHOR_TICKS .. (tick == AUTHOR and ', its tick untouched' or ', its tick REPLACED')", "ticks");
        if (state.Environment["LOG"].TryRead<Lua.LuaTable>(out var lines))
            for (var i = 1; lines[(double)i].TryRead<string>(out var line); i++) log.Add(line);
        if (run != null) log.Add("FAILED ticks: " + run);
        return log;
    }

    /// <summary>
    /// Every value the run wrote to a colour slot that is not hex - a fill, a stroke, a gradient stop, a
    /// label's `&lt;color={$x}&gt;` with whatever the scene writes after it - and every `&lt;color=&gt;`
    /// inside a label built on the chip. The vector mod keeps such a value as text and draws the fill
    /// magenta. `WHY_SLOT=name` prints everything one slot was written.
    /// </summary>
    internal static List<string> Colours(string? structure, Dictionary<string, Lua.LuaValue> first, Lua.LuaValue watched)
    {
        var written = new List<(string Slot, Lua.LuaValue Value)>();
        foreach (var p in first) written.Add((p.Key, p.Value));
        if (watched.TryRead<Lua.LuaTable>(out var w))
        {
            var key = Lua.LuaValue.Nil;
            while (w.TryGetNext(key, out var pair))
            {
                key = pair.Key;
                if (!key.TryRead<string>(out var slot) || !pair.Value.TryRead<Lua.LuaTable>(out var values)) continue;
                var v = Lua.LuaValue.Nil;
                while (values.TryGetNext(v, out var seen)) { v = seen.Key; written.Add((slot, v)); }
            }
        }
        var bad = NotColours(structure, written);
        Console.WriteLine($"  colours: {bad.Count} value(s) written to a colour slot that are not hex");
        foreach (var b in bad.Take(30)) Console.WriteLine("    not a colour: " + b);
        if (Environment.GetEnvironmentVariable("WHY_SLOT") is { Length: > 0 } one)
            foreach (var x in written.Where(x => x.Slot == one).Select(x => x.Value.ToString()).Distinct()) Console.WriteLine($"    {one} <- {x}");
        return bad;
    }

    /// <summary>What <see cref="Colours"/> reports, for any list of (slot, value) writes.</summary>
    internal static List<string> NotColours(string? structure, IEnumerable<(string Slot, Lua.LuaValue Value)> written)
    {
        // A slot and what the scene writes straight after it: nothing for a fill, `FF` where a label's
        // rich text once carried the stand-in's alpha behind the placeholder.
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(structure ?? string.Empty, @"(?:(?<=\s[fs]=)|(?<=\[[-0-9.]+,))\$([A-Za-z_]\w*)")) slots[m.Groups[1].Value] = string.Empty;
        foreach (Match m in Regex.Matches(structure ?? string.Empty, @"<color=\{\$([A-Za-z_]\w*)\}([^>]*)>")) slots[m.Groups[1].Value] = m.Groups[2].Value;
        var bad = new List<string>();
        foreach (var (slot, value) in written)
        {
            var text = value.TryRead<string>(out var s) ? s : null;
            if (slots.TryGetValue(slot, out var after) && (text == null || !Hex.IsMatch(text + after))) bad.Add(slot + " = " + value + after);
            else if (text != null)
                foreach (Match m in RichColour.Matches(text))
                    if (!Hex.IsMatch(m.Groups[1].Value)) bad.Add(slot + " holds <color=" + m.Groups[1].Value + ">");
        }
        return bad.Distinct().ToList();
    }

    internal static readonly Regex Hex = new("^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);
    private static readonly Regex RichColour = new("<color=([^>]*)>", RegexOptions.Compiled);

    /// <summary>
    /// A page built, laid out and compiled as a surface does it, outside the game: the page as it
    /// first draws, its slots, and the compiler run over both. The static state it touches is put
    /// back afterwards.
    /// </summary>
    /// <param name="source">What the page was built from, as a surface remembers it (PageCompiler's cache); null compiles it afresh.</param>
    internal static (CompiledPage.Result Compiled, MarkupSlots.Result? Markup) Headless(string page, string? source = null,
        (string Surface, string Element, string Scene)? target = null)
    {
        var oracle = CssParser.SupportsOracle;
        var (vw, vh) = (CssParser.ViewportWidth, CssParser.ViewportHeight);
        try
        {
            ResolvedStyle.DefaultFace = FontLibrary.Default();
            HtmlRenderer.SurfaceAspect = 1f;
            OffThread.MainThreadId = Environment.CurrentManagedThreadId;
            OffThread.Job = OffThread.Globals.Take();
            var built = HtmlRenderer.Build(page, FontLibrary.Default());
            if (source != null) PageCompiler.Remember(built, source);
            HtmlRenderer.NameDrivenGroups(built);
            var panel = new Panel(built.Root);
            foreach (var grid in built.Grids)
                if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
            PostLayout.Attach(built);
            var boxes = new Dictionary<VisualElement, OffThread.Box>();
            OffThread.Boxes = boxes;
            var size = new Vector2(built.ViewportWidth, built.ViewportWidth);
            panel.Layout(size.x, size.y);
            OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());

            // The page as the surface first draws it: the slots a page without markup is bound to.
            OffThread.Active = true;
            var first = VectorEmitter.Emit(built, built.Root, size.x, size.y);
            OffThread.Active = false;
            var slots = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            SceneSlots.Split(first.Chars, first.Length, slots);

            var compiled = PageCompiler.Compile(built, panel, size, slots, target ?? ("main", "page", "html:page"), out var markup);
            return (compiled, markup);
        }
        finally
        {
            OffThread.Active = false;
            OffThread.Boxes = null;
            CssParser.SupportsOracle = oracle;
            CssParser.ViewportWidth = vw;
            CssParser.ViewportHeight = vh;
        }
    }

    /// <summary>The page built, laid out and compiled exactly as a surface would, with what came out.</summary>
    private static void Compile(string page, bool verbose)
    {
        {
            var t0 = DateTime.UtcNow;
            var bytes0 = GC.GetTotalAllocatedBytes(precise: true);
            var (layouts0, builds0) = (MarkupSlots.Emits, MarkupSlots.Builds);
            var (compiled, markupResult) = Headless(page);
            var ms = (DateTime.UtcNow - t0).TotalMilliseconds;
            // What the compile costs, against its budget: < 20 layouts, < 1 s, < 50 MB a page.
            Console.WriteLine($"  cost: {ms:0} ms, {(GC.GetTotalAllocatedBytes(precise: true) - bytes0) / 1048576.0:0.0} MB, "
                              + $"{MarkupSlots.Emits - layouts0} layouts, {MarkupSlots.Builds - builds0} builds");
            Console.WriteLine($"  compile: {(compiled.Ok ? "COMPILED" : "REFUSED")} in {ms:0} ms - lua {compiled.Lua?.Length ?? 0} chars, "
                              + $"{compiled.Bindings.Count} binding(s), {compiled.Problems.Count} problem(s), {compiled.Unmapped.Count} unmapped, {compiled.Warnings.Count} warning(s)");
            foreach (var p in compiled.Problems.Take(verbose ? 200 : 25)) Console.WriteLine("    problem: " + p);
            foreach (var u in compiled.Unmapped.Take(verbose ? 200 : 25)) Console.WriteLine("    unmapped: " + u);
            if (Environment.GetEnvironmentVariable("WHY_LUA") is { Length: > 0 } luaPath && compiled.Lua != null) File.WriteAllText(luaPath, compiled.Lua);
            if (Environment.GetEnvironmentVariable("WHY_STRUCTURE") is { Length: > 0 } structurePath && compiled.Structure != null) File.WriteAllText(structurePath, compiled.Structure);
            // A name the structure reads with no opening value draws magenta until the chip writes it.
            if (compiled.Structure != null && compiled.StructureValues != null)
                foreach (var name in System.Text.RegularExpressions.Regex.Matches(compiled.Structure, @"\$([A-Za-z_][A-Za-z0-9_]*)").Select(m => m.Groups[1].Value).Distinct())
                    if (!compiled.StructureValues.ContainsKey(name)) Console.WriteLine("    no opening value: $" + name);
            // Exploration: run a hand-edited chunk in place of the compiled one, to price a change
            // to the generated Lua before the compiler is taught to make it.
            if (Environment.GetEnvironmentVariable("WHY_LUA_IN") is { Length: > 0 } luaIn) compiled.Lua = File.ReadAllText(luaIn);
            if (compiled.Plain && compiled.Lua != null)
            {
                // A plain chunk has no frame function and reads no PAYLOAD: it is driven as its chip drives it.
                Console.WriteLine("  plain: 8 game ticks of 0.5 s, as the chip runs them:");
                foreach (var line in DrivePlain(compiled.Lua, 8, 0.5)) Console.WriteLine("    " + line);
                foreach (var w in compiled.Warnings) Console.WriteLine("    warning: " + w);
            }
            else if (compiled.Lua != null) Run(compiled);
            if (markupResult == null) return;
            var result = markupResult;
            var markup = result.Targets;

            Console.WriteLine($"  markup: {markup.Count} target(s), {result.Problems.Count} problem(s)");
            foreach (var t in result.Targets)
            {
                var kinds = t.Holes.Values.GroupBy(h => h.Kind).Select(g => $"{g.Count()} {g.Key}");
                Console.WriteLine($"    \"{t.Id}\": {t.Markup.Holes.Count} holes ({string.Join(", ", kinds)}), "
                                  + $"{t.Roots.Count} gated alternatives, {t.Labels.Count} built labels, drive {(t.Drive == null ? "none" : t.Drive.Text + " [" + string.Join(", ", t.Drive.Values) + "]")}");
                foreach (var b in t.Bindings)
                    Console.WriteLine($"      {b.Key}: {b.States.Count} states, {b.States.Sum(s => s.Numbers.Count + s.Text.Count)} slot values");
                if (verbose)
                    foreach (var pair in t.Holes.OrderBy(p => p.Key))
                        Console.WriteLine($"      hole {pair.Key} {pair.Value.Kind}: {t.Markup.Text(t.Markup.Holes[pair.Key].Value)} -> "
                                          + (pair.Value.Label >= 0 ? "label " + t.Labels[pair.Value.Label].Slot
                                             : string.Join(", ", pair.Value.To.Select(x => $"{x.Slot}*{x.Scale:0.###}+{x.Bias:0.###}")))
                                          + (pair.Value.States != null ? $" [{pair.Value.States.Count} states]" : "")
                                          + (pair.Value.Colours != null ? $" [{pair.Value.Colours.Count} colours]" : ""));
            }
            foreach (var p in result.Problems.Take(verbose ? 200 : 25)) Console.WriteLine("    ! " + p);
            if (result.Problems.Count > 25 && !verbose) Console.WriteLine($"    ... and {result.Problems.Count - 25} more");
            Console.WriteLine($"  structure: {(result.Template == null ? "none" : result.Template.Split('\n').Length + " lines")}, {result.Values.Count} slot value(s)");
            Console.WriteLine($"  time: {MarkupSlots.Builds} builds {MarkupSlots.TimeBuild.ElapsedMilliseconds} ms, {MarkupSlots.Emits} layouts {MarkupSlots.TimeLayout.ElapsedMilliseconds} ms + emits {MarkupSlots.TimeEmit.ElapsedMilliseconds} ms");
            if (Environment.GetEnvironmentVariable("WHY_DUMP") is { Length: > 0 } dump && result.Template != null)
            {
                File.WriteAllText(dump, result.Template);
                foreach (var t in result.Targets)
                {
                    File.WriteAllText(dump + "." + t.Id + ".html", t.Union.Html);
                    using var w = new StreamWriter(dump + "." + t.Id + ".txt");
                    foreach (var pair in t.Union.Sides) w.WriteLine($"choice {pair.Key}: then [{string.Join(",", pair.Value.Then)}] else [{string.Join(",", pair.Value.Else)}]");
                    foreach (var pair in t.Union.Rows) w.WriteLine($"list {pair.Key}: " + string.Join(" | ", pair.Value.Select(r => string.Join(",", r))));
                    foreach (var pair in t.Union.Parent) w.WriteLine($"{pair.Key} <- {pair.Value} [{string.Join(" ", t.Union.Path[pair.Key].Select(x => (x.List ? "*" : "?") + x.Index + "=" + x.Which))}] {t.Union.Tag[pair.Key]}");
                }
            }
        }
    }
}

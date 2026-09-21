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
        (@"examples\05-script.lua", 20, None),
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

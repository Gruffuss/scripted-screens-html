using System;
using System.Collections.Generic;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// The JavaScript statements and bindings a person writes without thinking about it.
/// </summary>
/// <remarks>
/// Every one of these was reported as untranslatable until now, which meant a page using a
/// <c>switch</c> or a <c>while</c> could not be compiled at all - it ran the interpreter every frame
/// for ever. They are grouped here rather than with the classes because they share one property
/// worth testing directly: each has a Lua equivalent that is ALMOST right, and the gap is where a
/// page would silently do something else.
/// </remarks>
internal static class SyntaxTests
{
    internal static void Run(Action<bool, string> check)
    {
        var lua = Compile("var x = 2, out = 0; switch (x) { case 1: out = 1; break; case 2: case 3: out = 23; break; default: out = 9; }", check,
                          "syntax: a switch translates");
        // Fall-through is what makes a switch not an if-chain: JavaScript runs from the matching
        // case to the next `break`, not to the next `case`. A chain of elseif would quietly change
        // what `case 2: case 3:` does, so the emitted form has to carry a "matched yet" flag.
        check(lua.Contains("__hit", StringComparison.Ordinal),
              "syntax: a switch keeps fall-through rather than becoming an if-chain");
        check(lua.Contains("repeat", StringComparison.Ordinal) && lua.Contains("until true", StringComparison.Ordinal),
              "syntax: a switch is a block `break` can leave");

        lua = Compile("var i = 0; while (i < 3) { i++; }", check, "syntax: a while loop translates");
        check(lua.Contains("while js_truthy", StringComparison.Ordinal) || lua.Contains("while (", StringComparison.Ordinal),
              "syntax: while tests the condition the way JavaScript counts truth");

        Compile("var i = 0; do { i++; } while (i < 3);", check, "syntax: a do-while translates");
        lua = Compile("var o = { a: 1, b: 2 }; var n = 0; for (const k in o) { n++; }", check,
                      "syntax: for-in translates");
        // `length` is this runtime's bookkeeping on an array, not a key the page put there. Handing
        // it to a for-in would give the page a key no browser ever would.
        check(lua.Contains("\"length\"", StringComparison.Ordinal),
              "syntax: for-in does not hand back the array length as a key");

        Compile("function f() { throw new Error('x'); } try { f(); } catch (e) { var m = e; }", check,
                "syntax: throw translates");

        // ---- destructuring ----
        Compile("var o = { a: 1, b: 2 }; var { a, b } = o;", check, "syntax: object destructuring");
        Compile("var xs = [1, 2, 3]; var [a, b] = xs;", check, "syntax: array destructuring");
        Compile("var o = { a: 1 }; var { a, b = 5 } = o;", check, "syntax: a destructuring default");
        Compile("var o = { a: { b: 1 } }; var { a: { b } } = o;", check, "syntax: nested destructuring");
        Compile("var xs = [1, 2, 3]; var [first, ...rest] = xs;", check, "syntax: an array rest binding");
        Compile("var o = { a: 1, b: 2 }; var { a, ...others } = o;", check, "syntax: an object rest binding");
        Compile("var o = { a: 1, b: 2 }; var { a: renamed } = o;", check, "syntax: a renamed binding");

        // The source is read once into a temporary. Not a tidiness point: the right-hand side is
        // often a call, and reading it per bound name would run that call per name.
        lua = Compile("function next() { return { a: 1, b: 2 }; } var { a, b } = next();", check,
                      "syntax: destructuring a call result");
        var calls = 0;
        for (var at = lua.IndexOf("next(", StringComparison.Ordinal); at >= 0; at = lua.IndexOf("next(", at + 1, StringComparison.Ordinal)) calls++;
        check(calls == 1, $"syntax: the destructured expression is evaluated once (found {calls})");

        // ---- parameters ----
        Compile("function f(a, b = 2) { return a + b; } var v = f(1);", check, "syntax: a default parameter");
        Compile("function f({ a, b }) { return a + b; } var v = f({ a: 1, b: 2 });", check,
                "syntax: a destructured parameter");
        Compile("function f([a, b]) { return a + b; } var v = f([1, 2]);", check,
                "syntax: an array-pattern parameter");
        Compile("function f(...args) { return args.length; } var v = f(1, 2);", check,
                "syntax: a rest parameter");
        Compile("const g = ({ a }) => a * 2; var v = g({ a: 2 });", check,
                "syntax: a destructured parameter on a concise arrow");

        // A default may call a function, and JavaScript calls it on entry rather than at the use
        // site. Emitting it into the body is what keeps that true.
        lua = Compile("function d() { return 1; } function f(a = d()) { return a; }", check,
                      "syntax: a default that calls a function");
        check(lua.Contains("if __", StringComparison.Ordinal) || lua.Contains("== nil then", StringComparison.Ordinal),
              "syntax: a default is filled in on entry, not at the use site");
    }

    private static string Compile(string js, Action<bool, string> check, string what)
    {
        var lua = JsToLua.Compile(js, out var problems);
        check(lua != null && problems.Count == 0,
              what + (problems.Count > 0 ? "\n    " + string.Join("\n    ", problems) : ""));
        return lua ?? string.Empty;
    }
}

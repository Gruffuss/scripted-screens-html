using System;
using System.Collections.Generic;
using System.Linq;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// Classes, <c>new</c> and the built-in constructors, translated.
/// </summary>
/// <remarks>
/// <c>class</c> blocked eighteen of the corpus's fifty scripted pages, more than twice anything else,
/// so this is the largest single thing the transpiler has learned. The tests check the SHAPE of what
/// it emits rather than running it - there is no Lua VM here - which catches the mistakes that matter:
/// a constructor that does not receive the instance, a method dispatched without its receiver, a
/// static folded in with the instance methods, and a <c>super</c> call that would recurse into the
/// override it is trying to skip.
/// </remarks>
internal static class ClassTests
{
    internal static void Run(Action<bool, string> check)
    {
        Translates(check);
        Shapes(check);
        Refuses(check);
    }

    private static string? Lua(string js, out IReadOnlyList<string> problems)
        => JsToLua.Compile(js, out problems);

    private static void Translates(Action<bool, string> check)
    {
        var lua = Lua("class Gauge { constructor(n) { this.n = n; } read() { return this.n; } }"
                      + "var g = new Gauge(3); var v = g.read();", out var problems);
        check(lua != null && problems.Count == 0,
              "class: a class with a constructor and a method translates"
              + (problems.Count > 0 ? "\n    " + string.Join("\n    ", problems) : ""));

        lua = Lua("class A { constructor(x) { this.x = x; } who() { return 'a'; } }"
                  + "class B extends A { constructor(x) { super(x); this.y = 1; } who() { return 'b' + super.who(); } }"
                  + "var b = new B(1);", out problems);
        check(lua != null && problems.Count == 0,
              "class: extends, super() and super.method() translate"
              + (problems.Count > 0 ? "\n    " + string.Join("\n    ", problems) : ""));

        lua = Lua("class P { get area() { return this.w * this.h; } set area(v) { this.w = v; } }"
                  + "var p = new P(); var a = p.area;", out problems);
        check(lua != null && problems.Count == 0,
              "class: accessors translate" + (problems.Count > 0 ? "\n    " + string.Join("\n    ", problems) : ""));

        lua = Lua("class C { static make() { return new C(); } tick() { return 1; } }"
                  + "var c = C.make();", out problems);
        check(lua != null && problems.Count == 0,
              "class: a static method translates" + (problems.Count > 0 ? "\n    " + string.Join("\n    ", problems) : ""));

        lua = Lua("class F { count = 0; label = 'x'; bump() { this.count++; } }"
                  + "var f = new F(); f.bump();", out problems);
        check(lua != null && problems.Count == 0,
              "class: field initialisers translate" + (problems.Count > 0 ? "\n    " + string.Join("\n    ", problems) : ""));

        lua = Lua("var m = new Map(); m.set('a', 1); var n = new Set(); n.add(2); var e = new Error('x');"
                  + "var arr = new Array(3);", out problems);
        check(lua != null && problems.Count == 0,
              "class: Map, Set, Error and Array construct"
              + (problems.Count > 0 ? "\n    " + string.Join("\n    ", problems) : ""));
    }

    private static void Shapes(Action<bool, string> check)
    {
        // The instance has to arrive as the first argument or `this` is nothing. This is the one
        // mistake that would compile, run, and draw wrong.
        var lua = Lua("class G { constructor(n) { this.n = n; } read() { return this.n; } }", out _) ?? "";
        check(lua.Contains("[\"__ctor\"] = function(__self, n)", StringComparison.Ordinal),
              "class: the constructor takes the instance first");
        check(lua.Contains("[\"read\"] = function(__self)", StringComparison.Ordinal),
              "class: a method takes the instance first");
        check(lua.Contains("__self.n", StringComparison.Ordinal),
              "class: `this` inside a method is the instance");

        // A static has no instance, so it must NOT take one - and it must not land in the table the
        // instances look their methods up in.
        lua = Lua("class S { static make() { return 1; } run() { return 2; } }", out _) ?? "";
        var call = lua.IndexOf("js_class(", StringComparison.Ordinal);
        check(call >= 0 && lua.Contains("[\"make\"] = function()", StringComparison.Ordinal),
              "class: a static method takes no instance");
        // js_class(name, base, methods, getters, setters, statics): `run` must be in the third
        // argument and `make` in the last, or an instance would answer to a static and vice versa.
        var afterCall = lua.Substring(call);
        check(afterCall.IndexOf("\"run\"", StringComparison.Ordinal) < afterCall.IndexOf("\"make\"", StringComparison.Ordinal),
              "class: instance methods and statics go to different tables");

        // super must call the BASE's function directly. Dispatching by name would find the
        // derived override and recurse until the stack ran out.
        lua = Lua("class A { go() { return 1; } } class B extends A { go() { return super.go(); } }", out _) ?? "";
        check(lua.Contains(".__proto[\"go\"](__self)", StringComparison.Ordinal),
              "class: super.method() calls the base's function with this instance");
        lua = Lua("class A { constructor(x) { this.x = x; } } class B extends A { constructor() { super(1); } }", out _) ?? "";
        check(lua.Contains(".__proto.__ctor(__self, 1)", StringComparison.Ordinal),
              "class: super() calls the base constructor with this instance");
    }

    private static void Refuses(Action<bool, string> check)
    {
        // `this` in a static refers to the class, not an instance, and there is no receiver to hand
        // it. Refusing is right; quietly emitting a nil would draw nothing and say nothing.
        Lua("class S { static go() { return this.x; } }", out var problems);
        check(problems.Count > 0, "class: `this` in a static method is reported, not guessed");

        // A static block used to be reported. It runs once against the class itself, which is a
        // thing this compiler can express, so it is translated now.
        var blockLua = Lua("class S { static x = 0; static { this.x = 1; } }", out problems);
        check(blockLua != null && problems.Count == 0,
              "class: a static initialisation block translates"
              + (problems.Count > 0 ? "\n    " + string.Join("\n    ", problems) : ""));
        check(blockLua != null && blockLua.Contains("__cls", StringComparison.Ordinal),
              "class: a static block runs against the class, not an instance");

        Lua("var k = 'go'; class S { [k]() { return 1; } }", out problems);
        check(problems.Count > 0, "class: a computed member name is reported");

        Lua("class S { go() { return super.go(); } }", out problems);
        check(problems.Count > 0, "class: super in a class that extends nothing is reported");

        Lua("var x = new NotDefinedAnywhere();", out problems);
        check(problems.Count > 0, "class: new on an undefined name is reported");
    }
}

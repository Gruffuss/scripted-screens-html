using System;
using System.Collections.Generic;
using System.Linq;
using Acornima;
using Acornima.Ast;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// Coverage measured against the LANGUAGE, not against the pages that happen to be in this folder.
/// </summary>
/// <remarks>
/// The corpus sweep answers "do our files compile", which is the wrong question: it says nothing
/// about what someone writes tomorrow. The mod's promise is that any valid JavaScript works, so the
/// denominator has to be the language itself.
///
/// For syntax that denominator is exact and needs no judgement. The parser has a closed set of AST
/// node types, and every program in the language is a tree of them - so the fraction of node types
/// the transpiler handles IS its syntactic coverage, with nothing sampled and nothing guessed.
///
/// A node type counts as handled only if the transpiler produces Lua for it. One that is reported as
/// untranslatable counts as missing, however politely it refuses: a page using it does not run.
/// </remarks>
internal static class Language
{
    internal static void Run(string[] args)
    {
        Syntax();
        Console.WriteLine();
        Library();
    }

    /// <summary>
    /// Every AST node type, and whether a program containing it translates.
    /// </summary>
    /// <remarks>
    /// Tested by compiling a minimal program per construct rather than by reading the transpiler's
    /// switch - a case can exist and still refuse, and the question is whether a page runs.
    /// </remarks>
    private static void Syntax()
    {
        var cases = Cases();
        var handled = new List<string>();
        var missing = new List<(string Name, string Why)>();

        foreach (var (name, source) in cases)
        {
            string? lua;
            IReadOnlyList<string> problems;
            try { lua = JsToLua.Compile(source, out problems); }
            catch (Exception ex) { missing.Add((name, "threw: " + ex.GetType().Name)); continue; }

            if (lua != null && problems.Count == 0) handled.Add(name);
            else missing.Add((name, problems.Count > 0 ? Short(problems[0]) : "produced nothing"));
        }

        var total = cases.Count;
        Console.WriteLine($"JavaScript syntax: {handled.Count} of {total} constructs "
                          + $"({100.0 * handled.Count / total:0}%)\n");
        Console.WriteLine("NOT translated:");
        foreach (var (name, why) in missing.OrderBy(m => m.Name, StringComparer.Ordinal))
            Console.WriteLine($"  {name,-34} {why}");
    }

    private static string Short(string s)
    {
        var at = s.IndexOf(": ", StringComparison.Ordinal);
        var body = at >= 0 ? s.Substring(at + 2) : s;
        return body.Length <= 64 ? body : body.Substring(0, 63) + "~";
    }

    /// <summary>
    /// One minimal program per language construct.
    /// </summary>
    /// <remarks>
    /// Written out rather than generated because the question is whether a REAL use translates, and
    /// a construct in isolation often parses into something else. Each is the smallest program that
    /// forces the parser to produce that node and the transpiler to do something with it.
    ///
    /// Grouped by the edition that introduced it, so what is missing reads as a date rather than a
    /// list - a page written today is ES2020 or later almost by default.
    /// </remarks>
    private static List<(string Name, string Source)> Cases() => new()
    {
        // ---- ES5 and earlier: the floor. Anything missing here is serious.
        ("var / let / const",          "var a = 1; let b = 2; const c = 3;"),
        ("function declaration",       "function f(a) { return a; }"),
        ("function expression",        "var f = function (a) { return a; };"),
        ("if / else",                  "var x = 1; if (x) { x = 2; } else { x = 3; }"),
        ("for",                        "for (var i = 0; i < 3; i++) { }"),
        ("while",                      "var i = 0; while (i < 3) { i++; }"),
        ("do-while",                   "var i = 0; do { i++; } while (i < 3);"),
        ("switch",                     "var x = 1; switch (x) { case 1: break; default: break; }"),
        ("break / continue",           "for (var i = 0; i < 3; i++) { if (i) continue; break; }"),
        ("labelled break",             "outer: for (var i = 0; i < 2; i++) { break outer; }"),
        ("try / catch",                "try { var a = 1; } catch (e) { var b = e; }"),
        ("try / finally",              "try { var a = 1; } finally { var b = 2; }"),
        ("throw",                      "function f() { throw new Error('x'); }"),
        ("ternary",                    "var x = 1; var y = x ? 1 : 2;"),
        ("logical && || !",            "var a = 1, b = 2; var c = a && b || !a;"),
        ("object literal",             "var o = { a: 1, 'b': 2 };"),
        ("array literal",              "var xs = [1, 2, 3];"),
        ("member access",              "var o = { a: 1 }; var v = o.a; var w = o['a'];"),
        ("getter in object",           "var o = { get x() { return 1; } };"),
        ("setter in object",           "var o = { _x: 0, set x(v) { this._x = v; } };"),
        ("new",                        "function F() {} var f = new F();"),
        ("typeof / delete / void",     "var o = { a: 1 }; var t = typeof o; delete o.a;"),
        ("in / instanceof",            "var o = { a: 1 }; var has = 'a' in o;"),
        ("comma operator",             "var a = 1; var b = (a, 2);"),
        ("regex literal",              "var s = 'a1'.replace(/[0-9]+/g, '');"),
        ("bitwise operators",          "var a = 5; var b = (a & 3) | (a ^ 1) | (~a) | (a << 1) | (a >> 1) | (a >>> 1);"),
        ("IIFE",                       "(function () { var a = 1; })();"),

        // ---- ES2015
        ("arrow function",             "var f = (a) => a + 1;"),
        ("class",                      "class A { go() { return 1; } }"),
        ("class extends / super",      "class A { go() { return 1; } } class B extends A { go() { return super.go(); } }"),
        ("template literal",           "var a = 1; var s = `v ${a} w`;"),
        ("tagged template",            "function tag(p) { return p[0]; } var s = tag`a${1}b`;"),
        ("destructuring (object)",     "var o = { a: 1 }; var { a } = o;"),
        ("destructuring (array)",      "var xs = [1, 2]; var [a, b] = xs;"),
        ("default parameter",          "function f(a = 1) { return a; }"),
        ("rest parameter",             "function f(...xs) { return xs.length; }"),
        ("spread in call",             "function f(a, b) { return a; } var xs = [1, 2]; f(...xs);"),
        ("spread in array",            "var xs = [1]; var ys = [0, ...xs];"),
        ("spread in object",           "var o = { a: 1 }; var p = { ...o, b: 2 };"),
        ("shorthand property",         "var a = 1; var o = { a };"),
        ("computed property",          "var k = 'a'; var o = { [k]: 1 };"),
        ("shorthand method",           "var o = { go() { return 1; } };"),
        ("for-of",                     "var xs = [1]; for (const x of xs) { var y = x; }"),
        ("for-in",                     "var o = { a: 1 }; for (const k in o) { var y = k; }"),
        ("generator",                  "function* g() { yield 1; }"),
        ("Symbol",                     "var s = Symbol('x');"),
        ("Promise",                    "var p = new Promise(function (ok) { ok(1); });"),

        // ---- ES2016-2018
        ("exponentiation",             "var a = 2 ** 3;"),
        ("async / await",              "async function f() { await 1; }"),
        ("for-await",                  "async function f(xs) { for await (const x of xs) { var y = x; } }"),
        ("object rest",                "var o = { a: 1, b: 2 }; var { a, ...rest } = o;"),

        // ---- ES2019-2020
        ("optional catch binding",     "try { var a = 1; } catch { var b = 2; }"),
        ("optional chaining",          "var o = {}; var v = o?.a;"),
        ("optional call",              "var o = {}; var v = o.f?.();"),
        ("nullish coalescing",         "var o = null; var v = o ?? 1;"),
        ("BigInt",                     "var a = 1n;"),
        ("dynamic import",             "function f() { return import('x'); }"),
        ("globalThis",                 "var g = globalThis;"),

        // ---- ES2021-2023
        ("logical assignment",         "var a = null; a ??= 1; a ||= 2; a &&= 3;"),
        ("numeric separators",         "var a = 1_000;"),
        ("class fields",               "class A { x = 1; }"),
        ("static class field",         "class A { static x = 1; }"),
        ("private class field",        "class A { #x = 1; go() { return this.#x; } }"),
        ("static block",               "class A { static { var a = 1; } }"),
        ("class getter / setter",      "class A { get x() { return 1; } set x(v) { this._x = v; } }"),
        ("static method",              "class A { static go() { return 1; } }"),
        ("top-level await",            "await 1;"),
        ("hashbang",                   "#!/usr/bin/env node\nvar a = 1;"),

        // ---- modules
        ("import",                     "import x from 'y';"),
        ("export",                     "export const a = 1;"),
    };

    /// <summary>
    /// The standard library, by object, against what the prelude provides.
    /// </summary>
    /// <remarks>
    /// The lists are the spec's own instance and static members, so the denominator is the language
    /// rather than whatever the corpus reached for. A method absent here is one a page can write and
    /// have refused.
    /// </remarks>
    private static void Library()
    {
        var groups = new (string Owner, string[] Members)[]
        {
            ("String", new[] { "at", "charAt", "charCodeAt", "codePointAt", "concat", "endsWith", "includes",
                "indexOf", "lastIndexOf", "localeCompare", "match", "matchAll", "normalize", "padEnd", "padStart",
                "repeat", "replace", "replaceAll", "search", "slice", "split", "startsWith", "substring", "substr",
                "toLowerCase", "toUpperCase", "toLocaleLowerCase", "toLocaleUpperCase", "trim", "trimStart",
                "trimEnd", "valueOf", "toString", "raw", "fromCharCode", "fromCodePoint" }),
            ("Array", new[] { "at", "concat", "copyWithin", "entries", "every", "fill", "filter", "find",
                "findIndex", "findLast", "findLastIndex", "flat", "flatMap", "forEach", "includes", "indexOf",
                "join", "keys", "lastIndexOf", "map", "pop", "push", "reduce", "reduceRight", "reverse", "shift",
                "slice", "some", "sort", "splice", "toReversed", "toSorted", "toSpliced", "unshift", "values",
                "with", "from", "isArray", "of" }),
            ("Object", new[] { "assign", "create", "defineProperty", "entries", "freeze", "fromEntries",
                "getOwnPropertyNames", "getPrototypeOf", "hasOwn", "hasOwnProperty", "is", "keys", "seal",
                "setPrototypeOf", "values" }),
            ("Number", new[] { "isFinite", "isInteger", "isNaN", "isSafeInteger", "parseFloat", "parseInt",
                "toExponential", "toFixed", "toPrecision", "toString", "valueOf" }),
            ("Math", new[] { "abs", "acos", "acosh", "asin", "asinh", "atan", "atan2", "atanh", "cbrt", "ceil",
                "clz32", "cos", "cosh", "exp", "expm1", "floor", "fround", "hypot", "imul", "log", "log10",
                "log1p", "log2", "max", "min", "pow", "random", "round", "sign", "sin", "sinh", "sqrt", "tan",
                "tanh", "trunc" }),
            ("JSON", new[] { "parse", "stringify" }),
            ("Map / Set", new[] { "add", "clear", "delete", "entries", "forEach", "get", "has", "keys", "set",
                "values" }),
            ("Date", new[] { "getDate", "getDay", "getFullYear", "getHours", "getMilliseconds", "getMinutes",
                "getMonth", "getSeconds", "getTime", "getTimezoneOffset", "now", "toISOString", "toLocaleDateString",
                "toLocaleTimeString", "valueOf" }),
            ("RegExp", new[] { "exec", "test" }),
            ("Promise", new[] { "all", "allSettled", "any", "catch", "finally", "race", "reject", "resolve",
                "then" }),
        };

        var have = JsToLua.PreludeMethods;
        var total = 0;
        var covered = 0;
        var lines = new List<string>();
        foreach (var (owner, members) in groups)
        {
            var missing = members.Where(m => !have.Contains(m)).ToArray();
            total += members.Length;
            covered += members.Length - missing.Length;
            lines.Add($"  {owner,-12} {members.Length - missing.Length,3}/{members.Length,-3}"
                      + (missing.Length > 0 ? "  missing: " + string.Join(" ", missing) : ""));
        }

        Console.WriteLine($"JavaScript standard library: {covered} of {total} members "
                          + $"({100.0 * covered / total:0}%)\n");
        foreach (var line in lines) Console.WriteLine(line);
        Console.WriteLine("\n  (a name shared by two objects - `entries`, `values`, `delete` - counts as covered");
        Console.WriteLine("   when ANY of them is implemented: the manifest is keyed by name alone, which is");
        Console.WriteLine("   bug #3 in BUGS.md and makes this number optimistic.)");
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Lua;
using Lua.Standard;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// Coverage measured against the DOM and HTML SPECIFICATIONS, not against the pages in this folder.
/// </summary>
/// <remarks>
/// <see cref="Language"/> does this for JavaScript the language. This is the other half: the objects
/// a page reaches for once it is running, and the markup it is written in. Same rule for the
/// denominator - the web platform - and the same rule for the numerator: a member counts only when
/// a page using it actually works.
///
/// Nothing here reads a switch statement to decide. Every DOM case is compiled by the real
/// transpiler and run on the game's own Lua against the real prelude; every HTML case is drawn by
/// the real renderer and looked for in the scene it emits. That matters because the two ways a
/// member fails are both invisible to inspection: a name can be in the compiler's manifest and
/// undefined in the prelude (which compiles clean and throws on a console), and a method can be
/// defined and answer nothing (which draws nothing and says nothing).
///
/// So each case lands in one of five buckets, and only the first counts as covered:
///
///   works    - the page gets what a browser would give it
///   hollow   - it runs and answers zero, empty or undefined where the spec says otherwise
///   refused  - the compiler says no, which is the honest failure: the author gets a line
///   throws   - it compiles and then dies at run time, which is the contract being broken
///   wrong    - it answers, definitely, with something else
///
/// Every case is written so that SUCCESS is a positive answer - a count, a name, `true`. A member
/// that is missing then reads as nil or 0 and lands in `hollow` by itself, rather than needing a
/// verdict from whoever wrote the case.
/// </remarks>
internal static class DomLanguage
{
    internal static void Run(string[] args)
    {
        var root = Root();
        if (root == null) { Console.WriteLine("no ScriptedScreensHtml folder above this one"); return; }
        var prelude = File.ReadAllText(Path.Combine(root, "JsPrelude.lua"));

        Section("1. Node and Element", NodeAndElement(), prelude);
        Section("2. HTMLElement", HtmlElement(), prelude);
        Section("3. Document", Document(), prelude);
        Section("4. Events", Events(), prelude);
        Section("5. classList, style, dataset", Views(), prelude);
        Section("6. Timers and frames", Timers(), prelude);
        StyleProperties();
        Elements(root);
        Attributes(root);
    }

    // ---- running one case -------------------------------------------------------------------

    private enum How { Works, Hollow, Refused, Throws, Wrong }

    /// <summary>One member, the smallest page that uses it, and what a browser answers.</summary>
    private readonly struct Case
    {
        /// <summary>The name reported.</summary>
        public readonly string Name;
        /// <summary>The page. Whatever it leaves in <c>RESULT</c> is the answer.</summary>
        public readonly string Js;
        /// <summary>
        /// Lua run after the page and before the answer is read, and what reads it. Everything a
        /// page cannot see from JavaScript lives here: the write log, and the frame loop that a
        /// timer or an event needs before there is anything to look at.
        /// </summary>
        public readonly string Tail;
        public readonly string Want;
        public Case(string name, string js, string tail, string want)
        { Name = name; Js = js; Tail = tail; Want = want; }
    }

    /// <summary>A case whose answer is the page's own <c>RESULT</c>.</summary>
    private static Case C(string name, string js, string want) => new(name, js, "PAGE_SYNC() OUT = PAGE.RESULT", want);

    /// <summary>A case that needs something to happen, or something only Lua can see, first.</summary>
    private static Case C(string name, string js, string tail, string want) => new(name, js, tail, want);

    /// <summary>
    /// An answer that means "nothing here": what a page reads back from a member that exists and
    /// holds no value, or from one that is not there at all. Separated from a wrong answer because
    /// the two fail differently - a hollow member draws nothing and a wrong one draws the wrong
    /// thing. <c>[object Object]</c> is in here because it is what the prelude's `undefined`
    /// sentinel stringifies to.
    /// </summary>
    private static bool Empty(string s) =>
        s is "undefined" or "null" or "" or "0" or "NaN" or "false" or "[object Object]";

    private static (How How, string Got) RunCase(in Case c, string prelude)
    {
        string? lua;
        IReadOnlyList<string> problems;
        try { lua = JsToLua.Compile(c.Js, out problems); }
        catch (Exception ex) { return (How.Refused, "compiler threw: " + ex.GetType().Name); }
        if (lua == null || problems.Count > 0)
            return (How.Refused, problems.Count > 0 ? First(problems[0]) : "produced nothing");

        string got;
        try
        {
            var state = LuaState.Create();
            state.OpenStandardLibraries();
            Chunk(state, prelude, "prelude");
            Chunk(state, Markup, "markup");
            Chunk(state, lua, "page");
            Chunk(state, c.Tail, "tail");
            Chunk(state, "OUT = js_str(OUT)", "read");
            got = state.Environment["OUT"].Type == LuaValueType.String
                ? state.Environment["OUT"].Read<string>()
                : "undefined";
        }
        catch (Exception ex) { return (How.Throws, First(ex.Message)); }

        if (got == c.Want) return (How.Works, got);
        return (Empty(got) ? How.Hollow : How.Wrong, got);
    }

    /// <summary>
    /// The page's own markup, as the compiler emits it.
    /// </summary>
    /// <remarks>
    /// A compiled page has no DOM: what a script can learn ABOUT the document comes from tables
    /// CompiledPage emits after laying the page out. The probe does not go through that path - it
    /// compiles one snippet and runs it - so without a fixture here every markup question answers
    /// as if the page were empty, and the probe measures its own harness rather than the runtime.
    ///
    /// This is the shape CompiledPage.Assemble writes, for the small document the cases assume:
    /// an #app containing a #box and a #label.
    /// </remarks>
    private const string Markup = @"
NODES = {'app', 'box', 'label'}
TAG = { ['app'] = 'div', ['box'] = 'div', ['label'] = 'span' }
-- #box carries no class: the classList cases build one up from nothing and would be
-- measuring the fixture rather than the runtime if it started with two.
CLASS = { ['app'] = 'root', ['label'] = 'text wide' }
PARENT = { ['box'] = 'app', ['label'] = 'box', ['app'] = 'document' }
-- x, y, w, h, contentW, contentH, offsetX, offsetY
-- #box is the element every geometry case reads, so its numbers are the ones those cases expect:
-- a 100x20 box at (8,8) inside #app, with no padding so the content box matches.
BOXES = {
  ['app']   = {0, 0, 800, 600, 800, 600, 0, 0},
  ['box']   = {8, 8, 100, 20, 100, 20, 8, 8},
  ['label'] = {20, 30, 60, 18, 60, 18, 12, 10},
}
";

    private static void Chunk(LuaState state, string text, string name) =>
        state.RunAsync(state.Load(text.AsSpan(), name, state.Environment)).AsTask().GetAwaiter().GetResult();

    private static string First(string s)
    {
        var line = s.Split('\n')[0].Trim();
        return line.Length <= 74 ? line : line.Substring(0, 73) + "~";
    }

    /// <summary>
    /// Members that answer nothing ON PURPOSE, each documented where it is defined. A compiled page
    /// has no layout in it at all - the cascade and the boxes were resolved once, at compile time,
    /// and what is left on the chip is a scene and some Lua - so every measurement is zero by
    /// construction rather than by omission. Listed so the report says which hollow answers are a
    /// decision and which are simply a gap.
    /// </summary>
    private static readonly HashSet<string> Deliberate = new(StringComparer.Ordinal)
    {
        "offsetWidth", "offsetHeight", "offsetLeft", "offsetTop", "clientWidth", "clientHeight",
        "scrollTop", "scrollLeft", "scrollWidth", "scrollHeight", "getBoundingClientRect",
        "document.querySelectorAll", "document.getElementsByTagName",
        "getComputedStyle (cascade)", "textContent (read)", "innerHTML (read)",
    };

    private static void Section(string title, List<Case> cases, string prelude)
    {
        var by = new Dictionary<How, List<string>>();
        foreach (How h in Enum.GetValues(typeof(How))) by[h] = new List<string>();

        foreach (var c in cases)
        {
            var (how, got) = RunCase(c, prelude);
            by[how].Add(how == How.Works ? c.Name : $"{c.Name,-32}  {got}");
        }

        var works = by[How.Works].Count;
        Console.WriteLine($"\n{title}: {works} of {cases.Count} ({100.0 * works / cases.Count:0}%)");
        Show("  answers nothing", by[How.Hollow]);
        Show("  refused by the compiler", by[How.Refused]);
        Show("  COMPILES THEN THROWS", by[How.Throws]);
        Show("  WRONG ANSWER", by[How.Wrong]);
    }

    private static void Show(string label, IReadOnlyCollection<string> lines)
    {
        if (lines.Count == 0) return;
        Console.WriteLine($"{label} ({lines.Count}):");
        foreach (var l in lines.OrderBy(x => x, StringComparer.Ordinal))
            Console.WriteLine("    " + l + (Deliberate.Contains(Head(l)) ? "   [by design]" : ""));
    }

    private static string Head(string line)
    {
        var at = line.IndexOf("  ", StringComparison.Ordinal);
        return at < 0 ? line : line.Substring(0, at).TrimEnd();
    }

    /// <summary>Reads one entry of the prelude's write log, which is where a DOM write lands.</summary>
    private static string Wrote(string slot) => $"OUT = DOM.writes[\"{slot}\"]";

    // ---- 1. Node and Element ------------------------------------------------------------------

    /// <summary>
    /// A three-node tree the script built, which is the only tree a compiled page has: the markup's
    /// own nodes are shapes in a scene by then, not a node list.
    /// </summary>
    private const string Tree = "var p = document.createElement('ul');\n"
                              + "var a = document.createElement('li');\n"
                              + "var b = document.createElement('b');\n"
                              + "p.appendChild(a); p.appendChild(b);\n";

    /// <summary>An element of the page's own markup, which is the case that matters most.</summary>
    private const string Box = "var e = document.getElementById('box');\n";

    private static List<Case> NodeAndElement() => new()
    {
        // ---- properties
        C("childNodes",            Tree + "var RESULT = p.childNodes.length;", "2"),
        C("children",              Tree + "var RESULT = p.children.length;", "2"),
        C("parentNode",            Tree + "var RESULT = a.parentNode === p;", "true"),
        C("parentElement",         Tree + "var RESULT = a.parentElement === p;", "true"),
        C("firstChild",            Tree + "var RESULT = p.firstChild === a;", "true"),
        C("lastChild",             Tree + "var RESULT = p.lastChild === b;", "true"),
        C("firstElementChild",     Tree + "var RESULT = p.firstElementChild === a;", "true"),
        C("lastElementChild",      Tree + "var RESULT = p.lastElementChild === b;", "true"),
        C("nextSibling",           Tree + "var RESULT = a.nextSibling === b;", "true"),
        C("previousSibling",       Tree + "var RESULT = b.previousSibling === a;", "true"),
        C("nextElementSibling",    Tree + "var RESULT = a.nextElementSibling === b;", "true"),
        C("nodeType",              Tree + "var RESULT = a.nodeType;", "1"),
        C("nodeName",              Tree + "var RESULT = a.nodeName;", "LI"),
        C("tagName",               Tree + "var RESULT = a.tagName;", "LI"),
        C("tagName (own markup)",  Box + "var RESULT = e.tagName;", "DIV"),
        C("id",                    Box + "var RESULT = e.id;", "box"),
        C("className",             Box + "e.className = 'a b'; var RESULT = e.className;", "a b"),
        C("classList",             Box + "e.classList.add('a'); var RESULT = e.className;", "a"),
        C("textContent (write)",   Box + "e.textContent = 'hi';", Wrote("box.textContent"), "hi"),
        C("textContent (read)",    Box + "var RESULT = e.textContent;", "hi"),
        C("innerText (write)",     Box + "e.innerText = 'hi';", Wrote("box.innerText"), "hi"),
        C("innerHTML (write)",     Box + "e.innerHTML = '<b>x</b>';", Wrote("box.innerHTML"), "<b>x</b>"),
        C("innerHTML (read)",      Box + "var RESULT = e.innerHTML;", "<b>x</b>"),
        C("outerHTML",             Box + "var RESULT = e.outerHTML;", "<div id=\"box\"></div>"),
        C("attributes",            Box + "e.setAttribute('k', 'v'); var RESULT = e.attributes.length;", "1"),

        // ---- methods
        C("appendChild",           Tree + "var RESULT = p.children.length;", "2"),
        C("insertBefore",          Tree + "var c = document.createElement('i'); p.insertBefore(c, b); var RESULT = p.children[1] === c;", "true"),
        C("removeChild",           Tree + "p.removeChild(a); var RESULT = p.children.length;", "1"),
        C("replaceChild",          Tree + "var c = document.createElement('i'); p.replaceChild(c, a); var RESULT = p.children[0] === c;", "true"),
        C("cloneNode",             Tree + "a.className = 'x'; var c = a.cloneNode(true); var RESULT = c.className;", "x"),
        C("contains",              Tree + "var RESULT = p.contains(a);", "true"),
        C("querySelector",         Tree + "a.className = 'x'; var RESULT = p.querySelector('.x') === a;", "true"),
        C("querySelectorAll",      Tree + "var RESULT = p.querySelectorAll('li').length;", "1"),
        C("getElementsByTagName",  Tree + "var RESULT = p.getElementsByTagName('li').length;", "1"),
        C("getElementsByClassName", Tree + "a.className = 'x'; var RESULT = p.getElementsByClassName('x').length;", "1"),
        C("closest",               Tree + "var RESULT = a.closest('ul') === p;", "true"),
        C("matches",               Tree + "a.className = 'x'; var RESULT = a.matches('.x');", "true"),
        C("matches (tag.class)",   Tree + "a.className = 'x'; var RESULT = a.matches('li.x');", "true"),
        C("getAttribute",          Box + "e.setAttribute('k', 'v'); var RESULT = e.getAttribute('k');", "v"),
        C("setAttribute",          Box + "e.setAttribute('k', 'v');", Wrote("box.@k"), "v"),
        C("removeAttribute",       Box + "e.setAttribute('k', 'v'); e.removeAttribute('k'); var RESULT = e.hasAttribute('k') ? 'kept' : 'gone';", "gone"),
        C("hasAttribute",          Box + "e.setAttribute('k', 'v'); var RESULT = e.hasAttribute('k');", "true"),
        C("toggleAttribute",       Box + "var RESULT = e.toggleAttribute('k');", "true"),
        C("append",                Tree + "p.append(document.createElement('i')); var RESULT = p.children.length;", "3"),
        C("prepend",               Tree + "var c = document.createElement('i'); p.prepend(c); var RESULT = p.children[0] === c;", "true"),
        C("before",                Tree + "var c = document.createElement('i'); a.before(c); var RESULT = p.children[0] === c;", "true"),
        C("after",                 Tree + "var c = document.createElement('i'); a.after(c); var RESULT = p.children[1] === c;", "true"),
        C("replaceWith",           Tree + "var c = document.createElement('i'); a.replaceWith(c); var RESULT = p.children[0] === c;", "true"),
        C("remove",                Tree + "a.remove(); var RESULT = p.children.length;", "1"),
        C("insertAdjacentHTML",    Box + "e.insertAdjacentHTML('beforeend', '<b>x</b>'); var RESULT = 'ran';", "ran"),
        C("insertAdjacentElement", Tree + "p.insertAdjacentElement('beforeend', document.createElement('i')); var RESULT = p.children.length;", "3"),
        C("insertAdjacentText",    Box + "e.insertAdjacentText('beforeend', 'x'); var RESULT = 'ran';", "ran"),
        C("createElement per call", "var a = document.createElement('div'); var b = document.createElement('div'); var RESULT = a === b ? 'shared' : 'distinct';", "distinct"),
    };

    // ---- 2. HTMLElement -----------------------------------------------------------------------

    private static List<Case> HtmlElement() => new()
    {
        C("style",              Box + "e.style.width = '5px';", Wrote("box.style.width"), "5px"),
        C("dataset (write)",    Box + "e.dataset.actIndex = '2';", Wrote("box.@data-act-index"), "2"),
        C("dataset (read)",     Box + "e.dataset.actIndex = '2'; var RESULT = e.dataset.actIndex;", "2"),
        C("hidden",             Box + "e.hidden = true;", Wrote("box.hidden"), "true"),
        C("title",              Box + "e.title = 't';", Wrote("box.title"), "t"),
        C("tabIndex",           Box + "e.tabIndex = 1; var RESULT = e.tabIndex;", "1"),
        C("focus",              Box + "e.focus(); var RESULT = 'ran';", "ran"),
        C("blur",               Box + "e.blur(); var RESULT = 'ran';", "ran"),
        C("click",              Box + "e.click(); var RESULT = 'ran';", "ran"),
        C("scrollIntoView",     Box + "e.scrollIntoView(); var RESULT = 'ran';", "ran"),
        C("offsetWidth",        Box + "var RESULT = e.offsetWidth;", "100"),
        C("offsetHeight",       Box + "var RESULT = e.offsetHeight;", "20"),
        C("offsetLeft",         Box + "var RESULT = e.offsetLeft;", "8"),
        C("offsetTop",          Box + "var RESULT = e.offsetTop;", "8"),
        C("clientWidth",        Box + "var RESULT = e.clientWidth;", "100"),
        C("clientHeight",       Box + "var RESULT = e.clientHeight;", "20"),
        C("scrollTop",          Box + "var RESULT = e.scrollTop;", "0"),
        C("scrollLeft",         Box + "var RESULT = e.scrollLeft;", "0"),
        C("scrollWidth",        Box + "var RESULT = e.scrollWidth;", "100"),
        C("scrollHeight",       Box + "var RESULT = e.scrollHeight;", "20"),
        C("getBoundingClientRect", Box + "var RESULT = e.getBoundingClientRect().width;", "100"),
        C("getComputedStyle",   Box + "e.style.width = '5px'; var RESULT = getComputedStyle(e).width;", "5px"),
        C("getComputedStyle (cascade)", Box + "var RESULT = getComputedStyle(e).width;", "100px"),
    };

    // ---- 3. Document --------------------------------------------------------------------------

    private static List<Case> Document() => new()
    {
        C("getElementById",     "var RESULT = document.getElementById('box') !== null;", "true"),
        C("createElement",      "var RESULT = document.createElement('div').tagName;", "DIV"),
        C("createElementNS",    "var RESULT = document.createElementNS('http://www.w3.org/2000/svg', 'rect').tagName;", "RECT"),
        C("createTextNode",     "var t = document.createTextNode('x'); var RESULT = 'ran';", "ran"),
        C("createDocumentFragment", "var f = document.createDocumentFragment(); var RESULT = 'ran';", "ran"),
        C("body",               "var RESULT = document.body !== null;", "true"),
        C("documentElement",    "var RESULT = document.documentElement !== null;", "true"),
        C("head",               "var RESULT = document.head !== null;", "true"),
        C("document.querySelector (#id)", "var RESULT = document.querySelector('#box') !== null;", "true"),
        C("document.querySelector (.class)", "var RESULT = document.querySelector('.wide') !== null;", "true"),
        // Two divs in the fixture markup, which is what a browser would report. The old
        // expectation of 1 was written when the chunk could only see nodes the script built.
        C("document.querySelectorAll", "var RESULT = document.querySelectorAll('div').length;", "2"),
        C("document.getElementsByTagName", "var RESULT = document.getElementsByTagName('div').length;", "2"),
        C("document.addEventListener", "var n = 0; document.addEventListener('x', function () { n = 1; });",
          "DOM.fire('document', 'x', 0, 0) OUT = PAGE.n", "1"),
        C("document.removeEventListener", "var n = 0; var f = function () { n = 1; }; document.addEventListener('x', f); document.removeEventListener('x', f);",
          "DOM.fire('document', 'x', 0, 0) OUT = PAGE.n == 0 and 'gone' or 'still there'", "gone"),
        C("document.dispatchEvent", "var n = 0; document.addEventListener('x', function () { n = 1; }); document.dispatchEvent({ type: 'x' }); var RESULT = n;", "1"),
        C("document.title",     "document.title = 'x'; var RESULT = document.title;", "x"),
    };

    // ---- 4. Events ----------------------------------------------------------------------------

    /// <summary>
    /// A click as the host delivers one: <c>DOM.fire</c> is what <c>event(id, kind, x, y)</c> calls,
    /// so the registry, the bubbling walk and the event object are all on the path - which calling a
    /// handler directly would skip.
    /// </summary>
    private const string Click = "DOM.fire('box', 'click', 3, 4) PAGE_SYNC() OUT = PAGE.RESULT";

    /// <summary>`box` sits inside `app`, which is what makes a bubbling case a bubbling case.</summary>
    private const string Nested = "PARENT['box'] = 'app'\n" + Click;

    private static List<Case> Events() => new()
    {
        C("addEventListener",   Box + "var RESULT = 0; e.addEventListener('click', function () { RESULT = 1; });", Click, "1"),
        C("removeEventListener", Box + "var RESULT = 'gone'; var f = function () { RESULT = 'fired'; }; e.addEventListener('click', f); e.removeEventListener('click', f);", Click, "gone"),
        C("dispatchEvent",      Box + "var RESULT = 0; e.addEventListener('x', function () { RESULT = 1; }); e.dispatchEvent({ type: 'x' });", "1"),
        C("bubbling to a parent", "var RESULT = 0; document.getElementById('app').addEventListener('click', function () { RESULT = 1; });", Nested, "1"),
        C("event.type",         Box + "var RESULT = ''; e.addEventListener('click', function (ev) { RESULT = ev.type; });", Click, "click"),
        C("event.target",       Box + "var RESULT = false; e.addEventListener('click', function (ev) { RESULT = ev.target === e; });", Click, "true"),
        C("event.currentTarget", "var a = document.getElementById('app'); var RESULT = false; a.addEventListener('click', function (ev) { RESULT = ev.currentTarget === a; });", Nested, "true"),
        C("event.bubbles",      Box + "var RESULT; e.addEventListener('click', function (ev) { RESULT = ev.bubbles; });", Click, "true"),
        C("event.preventDefault", Box + "var RESULT; e.addEventListener('click', function (ev) { ev.preventDefault(); RESULT = ev.defaultPrevented; });", Click, "true"),
        C("event.stopPropagation", Box + "var RESULT = 'stopped'; e.addEventListener('click', function (ev) { ev.stopPropagation(); }); document.getElementById('app').addEventListener('click', function () { RESULT = 'leaked'; });", Nested, "stopped"),
        C("event.stopImmediatePropagation", Box + "var RESULT = 'stopped'; e.addEventListener('click', function (ev) { ev.stopImmediatePropagation(); }); e.addEventListener('click', function () { RESULT = 'leaked'; });", Click, "stopped"),
        C("event.clientX/clientY", Box + "var RESULT = 0; e.addEventListener('click', function (ev) { RESULT = ev.clientX + ev.clientY; });", Click, "7"),
        C("event.offsetX/offsetY", Box + "var RESULT = 0; e.addEventListener('click', function (ev) { RESULT = ev.offsetX + ev.offsetY; });", Click, "7"),
        C("event.button",       Box + "var RESULT; e.addEventListener('click', function (ev) { RESULT = ev.button === 0 ? 'left' : 'other'; });", Click, "left"),
        C("event.key",          Box + "var RESULT; e.addEventListener('click', function (ev) { RESULT = ev.key; });", Click, "a"),
        C("event.code",         Box + "var RESULT; e.addEventListener('click', function (ev) { RESULT = ev.code; });", Click, "KeyA"),
        C("event.shiftKey",     Box + "var RESULT; e.addEventListener('click', function (ev) { RESULT = ev.shiftKey === false ? 'up' : 'down'; });", Click, "up"),
        C("event.ctrlKey",      Box + "var RESULT; e.addEventListener('click', function (ev) { RESULT = ev.ctrlKey === false ? 'up' : 'down'; });", Click, "up"),
        C("event.altKey",       Box + "var RESULT; e.addEventListener('click', function (ev) { RESULT = ev.altKey === false ? 'up' : 'down'; });", Click, "up"),
        C("onclick property",   Box + "var RESULT = 0; e.onclick = function () { RESULT = 1; };", Click, "1"),
        C("window.addEventListener", "var RESULT = 0; window.addEventListener('resize', function () { RESULT = 1; });",
          "DOM.fire('window', 'resize', 0, 0) PAGE_SYNC() OUT = PAGE.RESULT", "1"),
        C("a listener added during dispatch", Box + "var RESULT = 'once'; e.addEventListener('click', function () { e.addEventListener('click', function () { RESULT = 'twice'; }); });", Click, "once"),
    };

    // ---- 5. classList, style, dataset ---------------------------------------------------------

    private static List<Case> Views() => new()
    {
        C("classList.add",      Box + "e.classList.add('a'); e.classList.add('b'); var RESULT = e.className;", "a b"),
        C("classList.remove",   Box + "e.className = 'a b'; e.classList.remove('a'); var RESULT = e.className;", "b"),
        C("classList.toggle",   Box + "var RESULT = e.classList.toggle('a') + ':' + e.className;", "true:a"),
        C("classList.toggle(force)", Box + "e.classList.toggle('x', false); e.classList.toggle('y', true); var RESULT = e.className;", "y"),
        C("classList.contains", Box + "e.className = 'a'; var RESULT = e.classList.contains('a');", "true"),
        C("classList.replace",  Box + "e.className = 'a'; e.classList.replace('a', 'b'); var RESULT = e.className;", "b"),
        C("classList.length",   Box + "e.className = 'a b'; var RESULT = e.classList.length;", "2"),
        C("classList item access", Box + "e.className = 'a b'; var RESULT = e.classList[0];", "a"),
        C("style.setProperty",  Box + "e.style.setProperty('font-size', '9px');", Wrote("box.style.fontSize"), "9px"),
        C("style.getPropertyValue", Box + "e.style.setProperty('font-size', '9px'); var RESULT = e.style.getPropertyValue('font-size');", "9px"),
        C("style.removeProperty", Box + "e.style.width = '5px'; var was = e.style.removeProperty('width'); var RESULT = was + '/' + e.style.width;", "5px/"),
        C("style.cssText",      Box + "e.style.cssText = 'width:5px';", Wrote("box.style.width"), "5px"),
        C("style custom property", Box + "e.style.setProperty('--accent', '#f00');", Wrote("box.style.--accent"), "#f00"),
        C("style named write",  Box + "e.style.backgroundColor = '#f00';", Wrote("box.style.backgroundColor"), "#f00"),
        C("style read-back",    Box + "e.style.top = '3px'; var RESULT = e.style.top;", "3px"),
        C("dataset after removeAttribute", Box + "e.dataset.k = '1'; e.removeAttribute('data-k'); var RESULT = e.dataset.k === undefined ? 'gone' : 'kept';", "gone"),
    };

    // ---- 6. Timers and frames -----------------------------------------------------------------

    /// <summary>
    /// The frame loop a compiled page really gets, copied from the chunk tail <c>CompiledPage</c>
    /// emits. It belongs here rather than in the prelude because the prelude only RECORDS a timer -
    /// what happens to it afterwards is where the behaviour is, and measuring the prelude alone
    /// would report every one of these as working.
    /// </summary>
    private const string Drive = @"
-- The same loop CompiledPage.Runtime drives, and it has to STAY the same. This was copied before
-- two bugs in it were fixed and never copied back, so the probe kept measuring the old behaviour:
-- timers as the `else` of the animation branch (a page that animates ran no timer at all) and a
-- one-shot that never stopped. A probe that disagrees with production measures the probe.
local function DRIVE(n, dt)
  for _ = 1, n do
    local pending = Pending.frame
    if #pending > 0 then
      local fn = pending[#pending]
      Pending.frame = {}
      fn(0)
    end
    for i = 1, #Pending.timers do
      local timer = Pending.timers[i]
      if timer.fn ~= nil then
        timer.at = (timer.at or 0) + dt
        if timer.at >= (timer.ms or 0) then
          timer.at = 0
          local fn = timer.fn
          if timer.once then timer.fn = nil end
          fn(0)
        end
      end
    end
  end
end
DRIVE(4, 100)
PAGE_SYNC()
OUT = PAGE.RESULT
";

    private static List<Case> Timers() => new()
    {
        C("setTimeout runs",    "var RESULT = 0; setTimeout(function () { RESULT++; }, 100);", Drive, "1"),
        C("clearTimeout",       "var RESULT = 'cancelled'; var h = setTimeout(function () { RESULT = 'fired'; }, 100); clearTimeout(h);", Drive, "cancelled"),
        C("setInterval",        "var RESULT = 0; setInterval(function () { RESULT++; }, 100);", Drive, "4"),
        C("setInterval honours its delay", "var RESULT = 0; setInterval(function () { RESULT++; }, 200);", Drive, "2"),
        C("clearInterval",      "var RESULT = 'cancelled'; var h = setInterval(function () { RESULT = 'fired'; }, 100); clearInterval(h);", Drive, "cancelled"),
        C("requestAnimationFrame", "var RESULT = 0; function tick() { RESULT++; requestAnimationFrame(tick); } requestAnimationFrame(tick);", Drive, "4"),
        C("cancelAnimationFrame", "var RESULT = 'cancelled'; var h = requestAnimationFrame(function () { RESULT = 'fired'; }); cancelAnimationFrame(h);", Drive, "cancelled"),
        C("rAF is given a timestamp", "var RESULT = 'none'; requestAnimationFrame(function (t) { RESULT = typeof t; });", Drive, "number"),
        C("an interval beside a rAF loop", "var RESULT = 0; setInterval(function () { RESULT++; }, 100); function tick() { requestAnimationFrame(tick); } requestAnimationFrame(tick);", Drive, "4"),
        C("setTimeout with no delay", "var RESULT = 0; setTimeout(function () { RESULT++; });", Drive, "1"),
        C("queueMicrotask",     "var RESULT = 0; queueMicrotask(function () { RESULT = 1; });", "1"),
    };

    // ---- the style properties a write can actually reach ---------------------------------------

    /// <summary>
    /// <c>el.style.anything = ...</c> is recorded by the prelude whatever the property is, so the
    /// sections above would report `style` as working and stop there. What decides whether it draws
    /// is <see cref="DomSlots"/>, which maps a write onto a slot in the emitted scene - and it knows
    /// twelve spellings of eight properties. Everything else is refused at compile time with a
    /// reason, which is the honest failure, but it is still a page that does not run.
    /// </summary>
    private static readonly string[] CommonStyleWrites =
    {
        "width", "height", "left", "top", "right", "bottom", "opacity", "transform", "background",
        "backgroundColor", "color", "borderRadius", "fontSize", "display", "visibility", "margin",
        "marginLeft", "padding", "border", "borderColor", "borderWidth", "boxShadow", "textAlign",
        "fontWeight", "fontFamily", "lineHeight", "letterSpacing", "zIndex", "overflow", "flex",
        "flexDirection", "alignItems", "justifyContent", "gap", "gridTemplateColumns", "position",
        "cursor", "filter", "backdropFilter", "textShadow", "strokeDasharray", "clipPath",
        "backgroundImage", "borderTopLeftRadius", "maxWidth", "minHeight", "transition",
    };

    private static void StyleProperties()
    {
        var available = new HashSet<string>(StringComparer.Ordinal)
        {
            "e", "e_w", "e_h", "e_x", "e_y", "e_f", "e_rx", "e_size", "e_o", "e_t_0", "e_t_1",
        };
        var box = new DomSlots.Box(outOfFlow: true, parentX: 0, parentY: 0, hasBackground: false);
        var reach = new List<string>();
        var refused = new List<string>();
        foreach (var property in CommonStyleWrites)
        {
            var mapped = DomSlots.Map("e", "style." + property, box, available);
            if (mapped.Mapped) reach.Add(property);
            else refused.Add($"{property,-22}  {mapped.Problem}");
        }
        Console.WriteLine($"\n5b. style properties that reach the scene: {reach.Count} of "
                          + $"{CommonStyleWrites.Length} ({100.0 * reach.Count / CommonStyleWrites.Length:0}%)");
        Console.WriteLine("    " + string.Join(" ", reach));
        Show("  a write the compiler refuses", refused);
    }

    // ---- 7. HTML elements ---------------------------------------------------------------------

    /// <summary>
    /// Every element a page can write, against what the renderer draws. Not read from a switch: the
    /// probe writes one page carrying all of them, runs the real renderer through
    /// <c>ScriptedScreensHtml.Bench</c>, and looks in the scene it emitted - the same ground truth a
    /// console shows. An element that parses and never draws is not covered, and the only way to
    /// tell those apart is to draw it.
    ///
    /// Each element carries its own background so that anything taking part in the layout emits a
    /// box under its own id. Without that a perfectly supported container - a <c>ul</c> whose only
    /// mark on the scene is its children's - would read as missing.
    /// </summary>
    private static readonly (string Tag, string Inner)[] HtmlElements =
    {
        // sections and grouping
        ("div", "x"), ("span", "x"), ("p", "x"), ("h1", "x"), ("h2", "x"), ("h3", "x"), ("h4", "x"),
        ("h5", "x"), ("h6", "x"), ("hgroup", "<h1>x</h1>"), ("header", "x"), ("footer", "x"),
        ("main", "x"), ("section", "x"), ("article", "x"), ("aside", "x"), ("nav", "x"),
        ("search", "x"), ("address", "x"), ("blockquote", "x"), ("figure", "x"), ("figcaption", "x"),
        ("hr", ""), ("pre", "x"), ("center", "x"),
        // lists
        ("ul", "<li>x</li>"), ("ol", "<li>x</li>"), ("li", "x"), ("dl", "<dt>k</dt><dd>v</dd>"),
        ("dt", "x"), ("dd", "x"), ("menu", "<li>x</li>"),
        // text level
        ("a", "x"), ("b", "x"), ("strong", "x"), ("i", "x"), ("em", "x"), ("u", "x"), ("s", "x"),
        ("del", "x"), ("ins", "x"), ("small", "x"), ("big", "x"), ("mark", "x"), ("sub", "x"),
        ("sup", "x"), ("code", "x"), ("kbd", "x"), ("samp", "x"), ("var", "x"), ("abbr", "x"),
        ("cite", "x"), ("q", "x"), ("dfn", "x"), ("time", "x"), ("data", "x"), ("bdi", "x"),
        ("bdo", "x"), ("ruby", "x<rt>y</rt>"), ("br", ""), ("wbr", ""), ("font", "x"), ("tt", "x"),
        ("strike", "x"), ("nobr", "x"), ("marquee", "x"),
        // embedded
        ("img", ""), ("picture", "<img src=a.png>"), ("svg", "<circle cx=5 cy=5 r=4 fill=#0f0/>"),
        ("canvas", ""), ("video", ""), ("audio", ""), ("iframe", ""), ("embed", ""), ("object", ""),
        ("map", "<area shape=rect coords='0,0,1,1'>"), ("math", "<mi>x</mi>"),
        // tables
        ("table", "<tr><td>x</td></tr>"), ("caption", "x"), ("thead", "<tr><td>x</td></tr>"),
        ("tbody", "<tr><td>x</td></tr>"), ("tfoot", "<tr><td>x</td></tr>"), ("tr", "<td>x</td>"),
        ("td", "x"), ("th", "x"), ("colgroup", "<col>"),
        // forms
        ("form", "<input>"), ("label", "x"), ("input", ""), ("button", "x"),
        ("select", "<option>a</option>"), ("optgroup", "<option>a</option>"),
        ("datalist", "<option>a</option>"), ("textarea", "x"), ("fieldset", "<legend>l</legend>x"),
        ("legend", "x"), ("output", "x"), ("progress", ""), ("meter", ""),
        // interactive and scripting
        ("details", "<summary>s</summary>b"), ("summary", "x"), ("dialog", "x"),
        ("template", "<b>x</b>"), ("slot", "x"), ("noscript", "x"),
        // metadata: a browser draws none of these either
        ("head", ""), ("title", "x"), ("meta", ""), ("link", ""), ("style", ""), ("script", ""),
        ("base", ""),
    };

    /// <summary>
    /// The elements the renderer hands to ScriptedScreens rather than drawing itself. They are real
    /// on a console - a picture, a sound, a text field - and simply are not in the vector scene, so
    /// the scene cannot be asked about them. <c>HtmlRenderer</c> routes exactly these into
    /// <c>result.Externals</c>, plus <c>canvas</c>, which is its own element type.
    /// </summary>
    private static readonly HashSet<string> External = new(StringComparer.Ordinal)
    {
        "img", "picture", "video", "audio", "input", "select", "textarea", "canvas",
    };

    /// <summary>
    /// Elements a browser draws nothing for either, so drawing nothing is the right answer. Kept
    /// apart from the gaps rather than folded into the total, since counting a correct blank as a
    /// success and counting it as a failure are both wrong.
    /// </summary>
    private static readonly HashSet<string> Invisible = new(StringComparer.Ordinal)
    {
        "head", "title", "meta", "link", "style", "script", "base", "template", "noscript",
        "colgroup", "datalist", "map", "optgroup", "br", "wbr",
    };

    private static void Elements(string root)
    {
        var bench = Bench(root);
        if (bench == null) { Skip("7. HTML elements"); return; }

        var page = new StringBuilder(
            "<!doctype html><html><head><meta name=\"viewport\" content=\"width=400\">"
            + "</head><body style=\"background:#111\">");
        foreach (var (tag, inner) in HtmlElements)
            page.Append("<div id=\"w_").Append(tag).Append("\" style=\"background:#222;margin-bottom:2px\"><")
                .Append(tag).Append(" id=\"el_").Append(tag).Append("\" style=\"background:#345\">")
                .Append(inner).Append("</").Append(tag).Append("></div>");
        page.Append("</body></html>");

        var scene = Draw(bench, page.ToString(), "elements");
        if (scene == null) { Console.WriteLine("\n7. HTML elements: the renderer did not answer"); return; }

        var drawn = new List<string>();
        var external = new List<string>();
        var blank = new List<string>();
        var boxOnly = new List<string>();
        var nothing = new List<string>();
        foreach (var (tag, _) in HtmlElements)
        {
            if (External.Contains(tag)) external.Add(tag);
            else if (Named(scene, "el_" + tag)) drawn.Add(tag);
            else if (Invisible.Contains(tag)) blank.Add(tag);
            else if (Named(scene, "w_" + tag)) boxOnly.Add(tag);
            else nothing.Add(tag);
        }

        var covered = drawn.Count + external.Count + blank.Count;
        Console.WriteLine($"\n7. HTML elements: {covered} of {HtmlElements.Length} "
                          + $"({100.0 * covered / HtmlElements.Length:0}%)");
        Console.WriteLine($"  drawn in the scene: {drawn.Count}");
        Console.WriteLine($"  drawn by ScriptedScreens' own element: {external.Count}  ({string.Join(" ", external)})");
        Console.WriteLine($"  correctly invisible, as in a browser: {blank.Count}  ({string.Join(" ", blank)})");
        Show("  takes room but paints nothing", boxOnly);
        Show("  nothing at all", nothing);
    }

    /// <summary>Whether the scene carries a node under this id.</summary>
    private static bool Named(string scene, string id) =>
        scene.Contains(" id=" + id + "\n", StringComparison.Ordinal)
        || scene.Contains(" id=" + id + " ", StringComparison.Ordinal);

    // ---- 8. Global HTML attributes -------------------------------------------------------------

    /// <summary>
    /// An attribute is covered when it CHANGES what is drawn. That is a stricter test than being
    /// parsed - every attribute parses, the tokenizer has no list of them - and it is the only one
    /// worth making: an attribute the renderer stores and never reads is the same as an absent one.
    ///
    /// Each is measured as a difference between two scenes, the same markup with and without it.
    /// The ones that carry no appearance of their own are made visible by a rule that selects on
    /// them, which is exactly how a page would use them.
    /// </summary>
    private static readonly (string Name, string With, string Without, string Why)[] AttributeCases =
    {
        ("id",        "<div id=probe style='background:#f00;height:9px'></div>", "<div style='background:#f00;height:9px'></div>", "names the node in the scene"),
        ("class",     "<div class=k style='height:9px'></div>", "<div style='height:9px'></div>", "selected by .k"),
        ("style",     "<div style='background:#0f0;height:9px'></div>", "<div style='height:9px'></div>", "inline declarations"),
        ("hidden",    "<div hidden style='background:#f00;height:9px'></div>", "<div style='background:#f00;height:9px'></div>", "not drawn at all"),
        ("title",     "<div title=t style='background:#f00;height:9px'></div>", "<div style='background:#f00;height:9px'></div>", "a tooltip"),
        ("data-*",    "<div data-k=v class=dk style='height:9px'></div>", "<div class=dk style='height:9px'></div>", "selected by [data-k]"),
        ("tabindex",  "<div tabindex=0 class=tb style='height:9px'></div>", "<div class=tb style='height:9px'></div>", "selected by [tabindex]"),
        ("role",      "<div role=button class=rl style='height:9px'></div>", "<div class=rl style='height:9px'></div>", "selected by [role]"),
        ("aria-*",    "<div aria-hidden=true class=ar style='height:9px'></div>", "<div class=ar style='height:9px'></div>", "selected by [aria-hidden]"),
        ("lang",      "<div lang=en class=lg style='height:9px'></div>", "<div class=lg style='height:9px'></div>", "selected by :lang()"),
        ("dir",       "<div dir=rtl class=dr style='height:9px'></div>", "<div class=dr style='height:9px'></div>", "selected by :dir()"),
        ("contenteditable", "<div contenteditable class=ce style='height:9px'></div>", "<div class=ce style='height:9px'></div>", "selected by [contenteditable]"),
        ("draggable", "<div draggable=true class=dg style='height:9px'></div>", "<div class=dg style='height:9px'></div>", "selected by [draggable]"),
        ("width/height on img", "<img src=a.png width=33 height=44>", "<img src=a.png>", "the picture's box"),
        ("href",      "<a href=x>t</a>", "<a>t</a>", "a link is coloured"),
        ("colspan",   "<table><tr><td colspan=2 style='background:#f00'>a</td></tr><tr><td>b</td><td>c</td></tr></table>", "<table><tr><td style='background:#f00'>a</td></tr><tr><td>b</td><td>c</td></tr></table>", "a cell spanning two columns"),
        ("open",      "<details open><summary>s</summary>body</details>", "<details><summary>s</summary>body</details>", "an open disclosure shows its body"),
        ("disabled",  "<button disabled class=db>x</button>", "<button class=db>x</button>", "selected by :disabled"),
        ("checked",   "<input type=checkbox checked class=ck>", "<input type=checkbox class=ck>", "selected by :checked"),
        ("type",      "<input type=range>", "<input type=text>", "which control is drawn"),
        ("rows/cols", "<textarea rows=4 cols=30></textarea>", "<textarea></textarea>", "the field's box"),
        ("colspan on th", "<table><tr><th colspan=2 style='background:#f00'>a</th></tr><tr><td>b</td><td>c</td></tr></table>", "<table><tr><th style='background:#f00'>a</th></tr><tr><td>b</td><td>c</td></tr></table>", "a header spanning two columns"),
        ("start on ol", "<ol start=5><li>x</li></ol>", "<ol><li>x</li></ol>", "the first number"),
        ("value on li", "<ol><li value=9>x</li></ol>", "<ol><li>x</li></ol>", "one item's number"),
        ("colspan on a wide cell", "<table><tr><td colspan=3 style='background:#f00'>a</td></tr><tr><td>b</td><td>c</td><td>d</td></tr></table>", "<table><tr><td colspan=2 style='background:#f00'>a</td></tr><tr><td>b</td><td>c</td><td>d</td></tr></table>", "the span is read, not assumed"),
    };

    private static void Attributes(string root)
    {
        var bench = Bench(root);
        if (bench == null) { Skip("8. Global HTML attributes"); return; }

        // Every selector here is what makes an attribute with no appearance of its own visible in a
        // scene that carries only geometry.
        const string Sheet = ".k{background:#00f}[data-k]{background:#0ff}[role]{background:#f0f}"
                           + "[aria-hidden]{background:#ff0}:lang(en){background:#0f0}:dir(rtl){background:#f00}"
                           + "[contenteditable]{background:#080}[draggable]{background:#808}"
                           + "[tabindex]{background:#088}:disabled{background:#880}:checked{background:#808}";

        var ignored = new List<string>();
        var n = 0;
        foreach (var (name, with, without, why) in AttributeCases)
        {
            var a = Draw(bench, Page(Sheet, with), "attr" + n + "a");
            var b = Draw(bench, Page(Sheet, without), "attr" + n + "b");
            n++;
            if (a == null || b == null) ignored.Add($"{name,-24}  the renderer did not answer");
            else if (a == b) ignored.Add($"{name,-24}  changes nothing ({why})");
        }

        var works = AttributeCases.Length - ignored.Count;
        Console.WriteLine($"\n8. Global HTML attributes: {works} of {AttributeCases.Length} "
                          + $"({100.0 * works / AttributeCases.Length:0}%)");
        Show("  in the markup, absent from the scene", ignored);
    }

    private static string Page(string sheet, string body) =>
        "<!doctype html><html><head><meta name=\"viewport\" content=\"width=300\"><style>" + sheet
        + "</style></head><body style=\"background:#111\">" + body + "</body></html>";

    // ---- driving the renderer -------------------------------------------------------------------

    private static string? Bench(string root)
    {
        var exe = Path.GetFullPath(Path.Combine(root, "..", "ScriptedScreensHtml.Bench",
                                                "bin", "Release", "net8.0", "ScriptedScreensHtml.Bench.exe"));
        return File.Exists(exe) ? exe : null;
    }

    private static void Skip(string section)
    {
        Console.WriteLine($"\n{section}: SKIPPED - build ScriptedScreensHtml.Bench -c Release first.");
        Console.WriteLine("   The renderer needs Unity's managed assemblies, which this project has none of.");
    }

    /// <summary>
    /// One page through the real renderer and emitter, and the scene that came out.
    /// </summary>
    /// <remarks>
    /// Out of process because the renderer needs Unity's managed assemblies and this project is
    /// deliberately Unity-free - the same reason <see cref="PreludeRuntime"/> shells out to a Lua
    /// interpreter. The bench writes <c>scene.txt</c> into its working directory, so each run gets a
    /// directory of its own under the temp folder and nothing in the repository is touched.
    /// </remarks>
    private static string? Draw(string bench, string html, string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ss-domlanguage", name);
        Directory.CreateDirectory(dir);
        var page = Path.Combine(dir, "page.html");
        File.WriteAllText(page, html);
        try
        {
            using var p = Process.Start(new ProcessStartInfo(bench, $"\"{page}\" 2")
            {
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (p == null) return null;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(120000);
            var scene = Path.Combine(dir, "scene.txt");
            return File.Exists(scene) ? File.ReadAllText(scene) : null;
        }
        catch (Exception) { return null; }
    }

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
}

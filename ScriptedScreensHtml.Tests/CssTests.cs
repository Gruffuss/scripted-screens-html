using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// The Unity-free half of the CSS front end: selector matching, media queries and @supports.
/// Every check here was watched to fail with its fix reverted - a regression test that has
/// never been seen red is an assumption, not a test.
/// </summary>
internal static class CssTests
{
    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("CssParser: selectors, @media, @supports");
        var w = CssParser.ViewportWidth;
        var h = CssParser.ViewportHeight;
        // Scene() builds pages, and a build installs the applier's @supports oracle for good
        var oracle = CssParser.SupportsOracle;
        try
        {
            AttributeFlags(check);
            NthOf(check);
            Media(check);
            Supports(check);
            SupportsAnsweredByTheApplier(check);
            ColumnCombinator(check);
            StatePseudos(check);
            PseudoElements(check);
            ImpliedEndTags(check);
            Containers(check);
            ContainersLaidOut(check);
            TextProperties(check);
            LayoutProperties(check);
            Leftovers(check);
            PicturePosition(check);
            VectorKeys(check);
            EmptyDrivenText(check);
            BarsAndLoops(check);
        }
        finally
        {
            CssParser.ViewportWidth = w;
            CssParser.ViewportHeight = h;
            CssParser.SupportsOracle = oracle;
            CssParser.ForgetReported();
        }
    }

    private static HtmlNode Doc(string html) => HtmlParser.Parse(html).Children[0];

    private static bool Hits(string selector, HtmlNode node) => CssParser.ParseSelector(selector, null) is { } s && s.Matches(node);

    // ---- [attr=v i] and [attr=v s] -------------------------------------------------------

    private static void AttributeFlags(Action<bool, string> check)
    {
        var div = Doc("<div><span id=a title=\"Hello World\" data-k=\"X y\" data-t=\"xs\">a</span></div>");
        var span = div.Children[0];

        check(!Hits("[title=\"hello world\"]", span), "[attr=v] is case-sensitive by default");
        check(Hits("[title=\"hello world\" i]", span), "[attr=v i] matches whatever the case");
        check(Hits("[title=\"Hello World\" s]", span), "[attr=v s] matches, and the s is not part of the value");
        check(!Hits("[title=\"hello world\" s]", span), "[attr=v s] is still case-sensitive");
        check(Hits("[data-k~=\"x\" i]", span), "the i flag reaches ~=");
        check(Hits("[data-k^=\"x\" i]", span) && Hits("[data-k$=\"Y\" i]", span) && Hits("[data-k*=\" Y\" i]", span),
            "the i flag reaches ^=, $= and *=");
        // the space before the flag is what identifies it: without that test this value lost its s
        check(Hits("[data-t=xs]", span), "a value ending in s is not mistaken for the s flag");
    }

    // ---- :nth-child(An+B of S) -----------------------------------------------------------

    private static void NthOf(Action<bool, string> check)
    {
        var ul = Doc("<ul><li id=one>1</li><li id=two class=a>2</li><li id=three>3</li><li id=four class=a>4</li></ul>");
        HtmlNode Li(int i) => ul.Children[i];

        check(Hits("li:nth-child(1 of .a)", Li(1)), ":nth-child(1 of S) counts only the siblings matching S");
        check(!Hits("li:nth-child(1 of .a)", Li(0)), "an element that does not match S never matches at all");
        check(Hits("li:nth-child(2 of .a)", Li(3)), "the second of the matching siblings, not the second child");
        check(!Hits("li:nth-child(2 of .a)", Li(1)), "and not the first one");
        check(Hits("li:nth-last-child(1 of .a)", Li(3)), ":nth-last-child(1 of S) counts S from the end");
        check(Hits("li:nth-child(2n of .a)", Li(3)) && !Hits("li:nth-child(2n of .a)", Li(1)), "An+B applies to the filtered list");
        // the plain forms still work after the four cases were merged into one
        check(Hits("li:nth-child(2)", Li(1)) && Hits("li:nth-child(odd)", Li(2)) && Hits("li:nth-child(-n+2)", Li(0)), ":nth-child without `of` is unchanged");
        check(Hits("li:nth-last-child(1)", Li(3)) && Hits("li:nth-of-type(2)", Li(1)), ":nth-last-child and :nth-of-type are unchanged");
    }

    // ---- @media ---------------------------------------------------------------------------

    private static int Applied(string sheet, List<string> warnings) => CssParser.ParseStylesheet(sheet, warnings.Add).Count;

    private static void Media(Action<bool, string> check)
    {
        CssParser.ViewportWidth = 400f;
        CssParser.ViewportHeight = 400f;
        var warn = new List<string>();
        int N(string query) => Applied("@media " + query + " { .z { color: red } }", warn);

        check(N("(prefers-color-scheme: dark)") == 1 && N("(prefers-color-scheme: light)") == 0,
            "a console is a dark panel, so prefers-color-scheme is dark");
        check(N("(prefers-reduced-motion: no-preference)") == 1 && N("(prefers-reduced-motion: reduce)") == 0,
            "no motion preference is expressed, so no-preference is the answer");
        check(N("(prefers-reduced-motion)") == 0, "the boolean form of a preference asks whether one was expressed");
        check(N("(hover: hover)") == 1 && N("(hover: none)") == 0 && N("(pointer: fine)") == 1,
            "the crosshair is a real pointer that can rest on a box");
        check(N("(min-resolution: 1dppx)") == 1 && N("(min-resolution: 2x)") == 0 && N("(min-resolution: 96dpi)") == 1,
            "resolution is 1 device pixel per CSS pixel, in dppx, x and dpi");
        check(N("(-webkit-min-device-pixel-ratio: 1)") == 1, "the vendor-prefixed device pixel ratio is the same question");

        check(N("(width >= 400px)") == 1 && N("(width > 400px)") == 0, "range syntax: feature on the left");
        check(N("(400px <= width)") == 1 && N("(500px <= width)") == 0, "range syntax: feature on the right, operator flipped");
        check(N("(200px < width < 600px)") == 1 && N("(200px < width < 300px)") == 0, "range syntax: both bounds");
        check(N("(width = 400px)") == 1, "range syntax: equality");
        check(N("(min-width: 20em)") == 1 && N("(min-width: 30em)") == 0, "em in a media query is the initial font size (20em = 320px)");
        // every length a query may legally be written in; without these the operand was NaN and
        // the block vanished with nothing said, which reads exactly like a rule that does nothing
        check(N("(min-width: 300pt)") == 1 && N("(min-width: 301pt)") == 0, "pt in a media query (400px = 300pt)");
        check(N("(min-width: 25pc)") == 1 && N("(min-width: 26pc)") == 0, "pc");
        check(N("(min-width: 4.16in)") == 1 && N("(min-width: 4.2in)") == 0, "in");
        check(N("(min-width: 10.5cm)") == 1 && N("(min-width: 10.6cm)") == 0, "cm");
        check(N("(min-width: 105mm)") == 1 && N("(min-width: 106mm)") == 0, "mm");
        check(N("(min-width: 423Q)") == 1 && N("(min-width: 424Q)") == 0, "Q");
        check(N("(min-width: 100vw)") == 1 && N("(min-width: 101vw)") == 0, "vw");
        check(N("(min-height: 100vh)") == 1 && N("(min-height: 101vh)") == 0, "vh");
        check(N("(min-width: 100vmin)") == 1 && N("(min-width: 100vmax)") == 1, "vmin and vmax are not read as `in` and `max`");
        check(N("(width >= 300pt)") == 1 && N("(width >= 301pt)") == 0, "the range syntax reads the same units");

        CssParser.ForgetReported();
        warn.Clear();
        check(N("(min-width: 40 kilometres)") == 0, "a quantity the parser cannot read still skips the block");
        check(warn.Count == 1 && warn[0].Contains("kilometres", StringComparison.Ordinal) && warn[0].Contains("width", StringComparison.Ordinal),
            $"and now says so, quoting what it could not read (got {warn.Count}: {string.Join("; ", warn)})");
        check(N("(min-width: 40 kilometres)") == 0 && warn.Count == 1, "once per page, not once per rule");
        check(N("(min-aspect-ratio: 1/2)") == 1 && N("(min-aspect-ratio: 2/1)") == 0, "aspect-ratio is still a ratio");
        check(N("screen and (min-width: 10px)") == 1 && N("not print") == 1 && N("print, screen") == 1, "types, not and commas still work");

        CssParser.ForgetReported();
        warn.Clear();
        check(N("(environment-blending: opaque)") == 0, "a feature a console cannot answer matches nothing");
        check(warn.Count == 1 && warn[0].Contains("environment-blending", StringComparison.Ordinal),
            "and it says which feature made the block vanish, naming itself");
        check(N("(environment-blending: opaque)") == 0 && warn.Count == 1, "reported once, not once per rule");
        CssParser.ForgetReported();
        warn.Clear();
        check(N("(environment-blending: opaque)") == 0 && warn.Count == 1, "ForgetReported puts the report back for the next page");
    }

    // ---- @supports -------------------------------------------------------------------------

    private static void Supports(Action<bool, string> check)
    {
        var warn = new List<string>();
        int N(string cond) => Applied("@supports " + cond + " { .z { color: red } }", warn);

        check(N("(display: flex)") == 1, "@supports of a declaration is true: the parser does accept it");
        // the half that mattered: a fallback was applied on top of the rules it was the fallback FOR
        check(N("not (display: flex)") == 0, "@supports not inverts instead of being taken anyway");
        check(N("(display: flex) and (gap: 1px)") == 1 && N("(display: flex) and (not (gap: 1px))") == 0, "and");
        check(N("(display: nonsense) or (display: flex)") == 1, "or");
        check(N("((display: flex))") == 1, "a redundant parenthesis is not read as a declaration");
        check(N("selector(:has(a))") == 1, "selector() is answered by parsing the selector");
        check(N("selector(:no-such-pseudo)") == 0, "and is false for one this parser cannot read");
        check(N("font-tech(color-COLRv1)") == 0, "no font here is chosen by technology");
    }

    /// <summary>
    /// The same grammar with the applier answering, which is how every page in game runs: a build
    /// installs it. Without it every declaration is true, and that is precisely what let a parser that
    /// read `((display: flex))` as the declaration "(display" pass the checks above.
    /// </summary>
    private static void SupportsAnsweredByTheApplier(Action<bool, string> check)
    {
        var oracle = CssParser.SupportsOracle;
        try
        {
            StyleApplier.InstallSupportsOracle();
            CssParser.ForgetReported();
            var warn = new List<string>();
            int N(string cond) => Applied("@supports " + cond + " { .z { color: red } }", warn);

            check(N("(display: flex)") == 1 && N("(display: nonsense)") == 0, "with the applier answering, a real declaration is true and a bad one false");
            // these are read by the grid, the emitter and the layout rather than the applier's switch,
            // and the probe answered no for every one of them - so each fallback beside them was applied
            check(N("(gap: 1px)") == 1 && N("(box-shadow: 0 0 4px #000)") == 1 && N("not (clip-path: circle(50%))") == 0,
                "a property another stage draws is supported: gap, box-shadow, clip-path");
            check(N("((display: flex))") == 1 && N("(((display: flex)))") == 1, "a redundant parenthesis is a condition, at any depth, not the declaration \"(display\"");
            check(N("(display: flex) and (not (gap: 1px))") == 0 && N("(display: flex) and (not (display: nonsense))") == 1,
                "`not` inside a parenthesis under `and`");
            check(N("((display: nonsense) or (display: flex)) and (gap: 1px)") == 1 && N("not ((display: nonsense) or (color: nonsense))") == 1,
                "an `or` group nested in an `and`, and a negated group");
            check(N("(selector(a:hover))") == 1, "the colon inside selector() is not a declaration's");
            check(N("(content: \"a) and (display: nonsense\")") == 1, "a parenthesis inside a string closes nothing");
            check(N("(foo bar)") == 0 && N("not (foo bar)") == 1, "anything else in parentheses is CSS's general-enclosed, which is false");
            check(N("not(display: nonsense)") == 0, "`not(` is a function, not the keyword, and is false as in a browser");

            warn.Clear();
            check(N("(display: flex) and (gap: 1px) or (color: red)") == 0, "`and` and `or` mixed without parentheses is not a condition");
            check(warn.Count == 1 && warn[0].Contains("not a valid condition", StringComparison.Ordinal),
                $"and the skipped block says so ({string.Join(" | ", warn)})");

            // the stale half of StyleApplier.Dropped: each of these is drawn, so @supports must say yes
            check(N("(accent-color: red)") == 1 && N("(object-position: right 10% top)") == 1 && N("(backface-visibility: hidden)") == 1
                  && N("(text-emphasis-style: dot)") == 1 && N("(mask-position: 5px 5px)") == 1,
                "a property the emitter draws is supported, not answered from a stale refusal list");
            check(N("(transform: translateZ(0))") == 1, "translateZ is the identity in a flat scene, as in a browser without perspective");
            check(N("(backdrop-filter: blur(4px))") == 0, "and one nothing draws is still unsupported, so its fallback is kept");
        }
        finally
        {
            CssParser.SupportsOracle = oracle;
            CssParser.ForgetReported();
        }
    }

    // ---- the column combinator ----------------------------------------------------------

    private static void ColumnCombinator(Action<bool, string> check)
    {
        var warn = new List<string>();
        var rules = CssParser.ParseStylesheet("#t || td, .keep { color: red }", warn.Add);
        check(rules.Count == 1 && rules[0].Selectors.Count == 1 && rules[0].Selectors[0].Chain[0].Classes[0] == "keep",
            "|| drops its own selector and keeps its rule-mates");
        check(warn.Exists(m => m.Contains("column combinator", StringComparison.Ordinal)),
            "and says so, instead of inventing a tag selector named ||");
        var td = Doc("<table><tr><td>c</td></tr></table>").Children[0].Children[0];
        check(!Hits("|| td", td), "nothing matches through a combinator that was refused");
    }

    // ---- state pseudo-classes ------------------------------------------------------------

    /// <summary>
    /// Every pseudo-class here answered "no" to the coverage probe, which reads as missing. Each
    /// one is really a correct NO for the probe's own fixture - an undisturbed page has no pointer
    /// on it, a &lt;dialog open&gt; is not modal, an out-of-range number is not :valid - so the
    /// only way to tell "implemented and correctly silent" from "dead" is to supply the state and
    /// watch it match. Without that these are indistinguishable from BUGS.md's recurring shape:
    /// wired up, compiles, does nothing.
    /// </summary>
    private static void StatePseudos(Action<bool, string> check)
    {
        HtmlNode One(string html) => Doc("<div>" + html + "</div>").Children[0];

        check(Hits("#a:hover", One("<span id=a data-hover></span>")) && !Hits("#a:hover", One("<span id=a></span>")),
            ":hover matches the node the pointer is over, and only that one");
        check(Hits("#a:active", One("<span id=a data-active></span>")) && !Hits("#a:active", One("<span id=a></span>")),
            ":active matches the node being pressed");
        check(Hits("#a:focus", One("<input id=a data-focus>")) && Hits("#a:focus-visible", One("<input id=a data-focus>"))
              && !Hits("#a:focus", One("<input id=a>")),
            ":focus and :focus-visible match the focused field");
        var form = Doc("<form id=f><input id=a data-focus></form>");
        check(Hits("#f:focus-within", form) && !Hits("#f:focus-within", Doc("<form id=f><input id=a></form>")),
            ":focus-within looks down the subtree, not only at itself");

        check(Hits("#a:valid", One("<input id=a required value=v>")) && !Hits("#a:valid", One("<input id=a required>")),
            ":valid is the other half of :invalid, not a synonym for it");
        check(!Hits("#a:valid", One("<input id=a type=number min=1 max=5 value=9>")),
            "and an out-of-range number is not valid - which is why the probe's own fixture answers no");

        check(Hits("#a:indeterminate", One("<progress id=a></progress>")) && !Hits("#a:indeterminate", One("<progress id=a value=1></progress>")),
            ":indeterminate matches a progress bar with no value");
        check(Hits("#a:modal", One("<dialog id=a open data-modal>d</dialog>")) && !Hits("#a:modal", One("<dialog id=a open>d</dialog>")),
            ":modal needs showModal(), not the open attribute - a plain <dialog open> is not modal in a browser either");
        check(Hits("#a:popover-open", One("<div id=a popover data-popover-open></div>")) && !Hits("#a:popover-open", One("<div id=a popover></div>")),
            ":popover-open needs the popover to have been shown");

        // :target has no URL fragment to name it and :visited no history to have been in; both are
        // permanent noes. What matters is that they stay SELECTORS, so a rule-mate is not lost.
        var warn = new List<string>();
        var rules = CssParser.ParseStylesheet("#a:target, #a:visited, .keep { color: red }", warn.Add);
        check(rules.Count == 1 && rules[0].Selectors.Count == 3,
            ":target and :visited parse and never match, rather than taking their rule-mates with them");
        check(!Hits("#a:target", One("<span id=a></span>")) && !Hits("#a:visited", One("<a id=a href=x></a>")),
            "and neither of them matches anything");
    }

    // ---- pseudo-elements -----------------------------------------------------------------

    /// <summary>
    /// A pseudo-element is only real if something GENERATES its node. Seven names do -
    /// ::before/::after and ::first-letter in the cascade, ::marker, ::placeholder,
    /// ::details-content, ::backdrop under a modal, and the three scrollbar parts, which
    /// VectorEmitter reads. ::first-line does not and now says so.
    /// </summary>
    private static void PseudoElements(Action<bool, string> check)
    {
        CssParser.ForgetReported();
        HtmlNode Generated(string host, string which)
        {
            var n = new HtmlNode { Tag = "span", Parent = Doc("<div>" + host + "</div>").Children[0] };
            n.Attributes["data-pseudo"] = which;
            return n;
        }

        check(Hits("dialog::backdrop", Generated("<dialog open data-modal></dialog>", "backdrop")),
            "::backdrop matches the box a modal dialog dims the page with");
        check(Hits("div::-webkit-scrollbar-thumb", Generated("<div></div>", "-webkit-scrollbar-thumb"))
              && Hits("div::-webkit-scrollbar-track", Generated("<div></div>", "-webkit-scrollbar-track")),
            "the scrollbar thumb and track are pseudo-elements the emitter draws");
        check(Hits("details::details-content", Generated("<details open></details>", "details-content")),
            "::details-content matches the generated body of an open <details>");
        check(!Hits("div::-webkit-scrollbar-thumb", Generated("<div></div>", "backdrop")),
            "and a generated node only answers to its own pseudo-element");

        // ::first-line is refused by name. It used to parse, set PseudoElement, and then match
        // nothing at all - the silent variant of the same outcome, which an author cannot act on.
        var warn = new List<string>();
        var rules = CssParser.ParseStylesheet("p::first-line, .keep { color: red } p::first-line { color: blue }", warn.Add);
        check(rules.Count == 2 && rules[0].Selectors.Count == 2,
            "::first-line keeps its rule and its rule-mates instead of dropping them");
        check(warn.Count == 1 && warn[0].Contains("first-line", StringComparison.Ordinal) && warn[0].Contains("after layout", StringComparison.Ordinal),
            $"and warns once, naming itself and why (got {warn.Count}: {string.Join("; ", warn)})");
        check(!Hits("p::first-line", Generated("<p>t</p>", "first-line")) && CssParser.ParseSelector("p::first-line", null)!.Chain[0].PseudoElement == null,
            "::first-line matches nothing and is no longer recorded as a pseudo-element");

        // ParseSelector(x, null) - @supports selector(), querySelector - must not consume the
        // one report the page's own parse owes. Same family as the process-global sets in #10/#37.
        CssParser.ForgetReported();
        CssParser.ParseSelector("p::first-line", null);
        warn.Clear();
        CssParser.ParseStylesheet("p::first-line { color: red }", warn.Add);
        check(warn.Count == 1, $"a silent parse of the same pseudo does not eat the page's warning (got {warn.Count})");
        CssParser.ForgetReported();
    }

    // ---- HTML's optional end tags --------------------------------------------------------

    /// <summary>
    /// The tokenizer's half of the same contract, checked here because this is the front end's
    /// Unity-free test file. `&lt;li&gt;a&lt;li&gt;b` is two items in every browser; it used to be
    /// a nest here, which draws - wrongly, and quietly, which is the expensive kind.
    /// </summary>
    private static void ImpliedEndTags(Action<bool, string> check)
    {
        HtmlNode Body(string html) => Doc("<body>" + html + "</body>");
        int Kids(HtmlNode n) { var k = 0; foreach (var c in n.Children) if (!c.IsText) k++; return k; }

        var ul = Body("<ul><li>a<li>b<li>c</ul>").Children[0];
        check(Kids(ul) == 3 && Kids(ul.Children[0]) == 0, "<li> closes an open <li> instead of nesting inside it");

        var withList = Body("<ul><li>a<ul><li>x</li></ul></li><li>b</ul>").Children[0];
        check(Kids(withList) == 2 && Kids(withList.Children[0]) == 1, "and a genuinely nested list is still nested");

        var body = Body("<p>one<p>two<table><tr><td>c</table>");
        check(Kids(body) == 3 && body.Children[2].Tag == "table",
            "a block start tag closes an open <p>, so the table is a sibling and not inside the paragraph");
        check(Body("<p>one<span>two</span>").Children[0].Tag == "p" && Kids(Body("<p>one<span>two</span>")) == 1,
            "and phrasing content still belongs to the paragraph");

        var table = Body("<table><tr><td>a<td>b<tr><td>c</table>").Children[0];
        check(Kids(table) == 2 && Kids(table.Children[0]) == 2 && Kids(table.Children[1]) == 1,
            "<td> closes a cell and <tr> closes a cell AND its row");

        var select = Body("<select><option>x<option>y</select>").Children[0];
        check(Kids(select) == 2, "<option> closes an open <option>");

        var dl = Body("<dl><dt>k<dd>v<dt>k2<dd>v2</dl>").Children[0];
        check(Kids(dl) == 4, "<dt> and <dd> close each other");
    }

    /// <summary>
    /// An inline element a value is written into later but with no text yet still gets its T line,
    /// empty: the scene is compiled once, and a text sent afterwards otherwise had no slot ("draws no
    /// text"). Not an empty BOX: a second line under a bar's id makes its width unwritable (DomSlots).
    /// </summary>
    private static void EmptyDrivenText(Action<bool, string> check)
    {
        static string? Line(string scene, string id)
        {
            foreach (var line in scene.Split('\n'))
                if (line.TrimStart().StartsWith("T ", StringComparison.Ordinal) && line.EndsWith(" id=" + id, StringComparison.Ordinal)) return line;
            return null;
        }
        const string Readout = "<p id=p>Pressure: <span id=v style=\"color:#ff0000;font-size:20px;text-decoration:underline\"></span> kPa</p>";
        var span = Scene(Readout, string.Empty, data: true);
        check(Line(span, "v") is { } s && s.Contains(" text=\"\" size=20 f=#FF0000", StringComparison.Ordinal) && s.Contains(" missing=\"\"", StringComparison.Ordinal),
            "an empty span a data key names draws an empty T line in its own font and colour, placeholder off - " + Line(span, "v"));
        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        SceneSlots.Split(span, values);
        check(values.TryGetValue("v", out var slot) && !slot.IsNumber && slot.Text == string.Empty,
            "the empty T line is a text slot named after the element, holding \"\"");
        check(Line(Scene(Readout, string.Empty), "v") == null, "an empty span nothing writes to still draws no text");
    }

    // ---- text properties that only exist in the emitted scene ----------------------------

    /// <summary>
    /// The seven below are drawn, not laid out, so the cascade cannot answer for them: a property
    /// the record carries and the emitter ignores draws exactly what its absence draws. The scene
    /// string is the only witness, so these compile the Unity half like CssLanguage does.
    /// </summary>
    private static string Scene(string body, string css, bool animate = false, List<string>? warnings = null, bool data = false)
    {
        ResolvedStyle.DefaultFace = FontLibrary.Default();
        HtmlRenderer.SurfaceAspect = 1f;
        OffThread.MainThreadId = Environment.CurrentManagedThreadId;
        OffThread.Job = OffThread.Globals.Take();

        var html = "<html><head><meta name=\"viewport\" content=\"width=400\"><style>"
                   + "#p{color:#eeeeee;font-size:14px}" + css + "</style></head><body>" + body + "</body></html>";
        var built = HtmlRenderer.Build(html, FontLibrary.Default());
        HtmlRenderer.NameDrivenGroups(built);
        // a page with no script is driven by data: every id may be a key (as HtmlSurface.BuildInner)
        if (data)
            foreach (var id in built.ById.Keys)
                if (!id.StartsWith("__", StringComparison.Ordinal)) built.Driven.Add(id);
        var panel = new Panel(built.Root);
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);
        var boxes = new Dictionary<VisualElement, OffThread.Box>();
        OffThread.Boxes = boxes;
        var size = new Vector2(built.ViewportWidth, built.ViewportWidth);
        panel.Layout(size.x, size.y);
        if (animate)
        {
            // as CssLanguage.Scene: a looping opacity/transform animation is compiled into the scene, any other
            // is stepped by a runner, twice, so the frame after the first is what the scene shows
            foreach (var (element, spec) in built.Animations)
            {
                if (!built.AnimationAttached.Add(element) || !built.Keyframes.TryGetValue(spec.Name, out var frames)) continue;
                if (float.IsPositiveInfinity(spec.Iterations) && !spec.Paused && VectorEmitter.Compilable(frames, built.CssOf(element)))
                {
                    built.TimeAnimations[element] = (spec, 0f);
                    continue;
                }
                var runner = new KeyframeRunner(element, frames, spec, 0f, _ => { }, built.CssOf(element), built.Touch);
                runner.Update(0f);
                runner.Update(0.3f);
            }
            panel.Layout(size.x, size.y);
        }
        OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());
        OffThread.Active = true;
        try
        {
            var tweens = new Tweens();
            tweens.Diff(built.Root, built, 0f);
            var output = VectorEmitter.Emit(built, built.Root, size.x, size.y, tweens, 0f, null);
            warnings?.AddRange(built.Warnings);
            warnings?.AddRange(output.Warnings);
            return new string(output.Chars, 0, output.Length);
        }
        finally { OffThread.Active = false; }
    }

    // ---- a fill bar and the loops a status panel runs --------------------------------------------

    private static void BarsAndLoops(Action<bool, string> check)
    {
        // `C 0 37%` is two stops of C: the bar is a hard edge at 37% of the box, and moves with the value.
        // Read as one stop, the bar was a ramp over the whole box that no value moved.
        string Bar(string at) => Scene("<div id=p style=\"width:200px;height:20px\"></div>",
            "#p{background:linear-gradient(90deg,#33cc66 0 " + at + "%,transparent 0) left bottom/100% 3px no-repeat,#000}");
        var bar = Bar("37");
        check(bar.Contains("CP id=cut", StringComparison.Ordinal) && bar.Contains(",74,", StringComparison.Ordinal),
            "a hard stop written with two positions is cut at 37% of a 200px bar (x 74)");
        check(bar != Bar("55.5"), "moving the stop moves the bar");
        check(bar.Contains("f=#33CC66 id=p }", StringComparison.Ordinal),
            "the bar's band is named after its element, so its colour is a slot the chip can reach in every layout");

        // A trip flash: filter over the frames is a group attribute the scene evaluates, not a runner.
        var flash = Scene("<div id=p style=\"width:50px;height:20px;background:#333333;animation:trip 1s linear infinite\">x</div>",
            "@keyframes trip{0%,100%{filter:brightness(1)}50%{filter:brightness(1.3)}}", animate: true);
        check(flash.Contains(" bri=\"=", StringComparison.Ordinal), "a looping brightness() keyframe is a bri expression of t");

        // A marching dashed line: background-position on stripes is what the stripes path already writes
        // as an expression, so it is runnable - and a gradient that cannot be drawn as stripes is not.
        bool Marches(string background)
        {
            var built = HtmlRenderer.Build("<html><head><style>@keyframes m{to{background-position:12px 0}}"
                + "#p{height:4px;animation:m 1s linear infinite;background:" + background + "}</style></head><body><div id=p></div></body></html>",
                FontLibrary.Default());
            foreach (var (element, spec) in built.Animations)
                if (built.Keyframes.TryGetValue(spec.Name, out var frames)) return VectorEmitter.Compilable(frames, built.CssOf(element));
            return false;
        }
        check(Marches("repeating-linear-gradient(90deg,#33cc66 0 6px,#000000 6px 12px)"), "background-position on hard-edged stripes is runnable in the scene");
        check(!Marches("repeating-linear-gradient(45deg,#33cc66 0 6px,#000000 6px 12px)"), "on diagonal stripes it is not, and so is still said");

        // A number and its unit on one baseline: while markup compiles, one label, so the unit follows
        // whatever the number turns out to be instead of sitting where the stand-in ended.
        const string Row = "<div style=\"display:flex;align-items:baseline;gap:4px{0}\"><span style=\"font-size:26px;font-weight:600\">97.7</span>"
                           + "<span style=\"font-size:13px;color:#888888\">kPa</span></div>";
        static int Labels(string scene) => System.Text.RegularExpressions.Regex.Matches(scene, @"^\s*T x=", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
        check(Labels(Scene(string.Format(Row, string.Empty), string.Empty)) == 2, "a number and its unit outside a compile are still two labels");
        var joinWas = VectorEmitter.JoinRows;
        VectorEmitter.JoinRows = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var joined = Scene(string.Format(Row, string.Empty), string.Empty);
            check(Labels(joined) == 1 && joined.Contains("97.7", StringComparison.Ordinal) && joined.Contains("<size=13><color=#888888><space=", StringComparison.Ordinal)
                  && joined.Contains(">kPa<", StringComparison.Ordinal),
                "compiled, a number and its unit are one label: the unit carries its own size, colour and the gap");
            check(Scene(string.Format(Row, ";justify-content:flex-end"), string.Empty).Contains(" align=right", StringComparison.Ordinal),
                "a row packed to the end is one right-aligned label, growing to the left as the browser's does");
            check(Labels(Scene(string.Format(Row, string.Empty).Replace("color:#888888", "color:#888888;background:#222222"), string.Empty)) == 2,
                "a piece that paints a box of its own stays its own element");
        }
        finally { VectorEmitter.JoinRows = joinWas; }
    }

    // ---- the CSS leftovers: each of these rows read as a gap until the thing under it was built --

    private static void Leftovers(Action<bool, string> check)
    {
        const string Box = "<div id=p style=\"width:60px;height:20px;background:#22aa44\">x</div>";
        const string Frames = "@keyframes probe{from{transform:translateX(0)}to{transform:translateX(40px)}}";

        // animation-composition: add composes the frame onto the element's own transform. Finite, so a runner
        // steps it: the composed value has to reach the resolved style, which is what the emitter reads.
        check(Scene(Box, Frames + "#p{transform:translateX(20px);animation:probe 2s linear 1}", animate: true).Contains("t=[0,0]", StringComparison.Ordinal),
            "a keyframe's transform replaces the element's own by default");
        check(Scene(Box, Frames + "#p{transform:translateX(20px);animation:probe 2s linear 1;animation-composition:add}", animate: true).Contains("t=[20,0]", StringComparison.Ordinal),
            "animation-composition: add puts the frame's translate on top of the element's own");
        // and a compiled loop, which sits inside the element's own transform group: replace takes the base out
        // again, add leaves the frame as written for the outer group to add
        var loopReplace = Scene(Box, Frames + "#p{transform:translateX(20px);animation:probe 2s linear infinite}", animate: true);
        var loopAdd = Scene(Box, Frames + "#p{transform:translateX(20px);animation:probe 2s linear infinite;animation-composition:add}", animate: true);
        check(loopReplace.Contains("-20+(40)*", StringComparison.Ordinal) && loopAdd.Contains("0+(40)*", StringComparison.Ordinal) && !loopAdd.Contains("-20+", StringComparison.Ordinal),
            "a compiled loop under replace runs 0..40 despite the base translate, and under add 20..60");

        // attr() as a length: the element's attribute with the unit asked for, a fallback when it is absent
        const string Attr = "<div id=p data-w=\"50\" style=\"height:20px;background:#22aa44\">x</div>";
        check(Rects(Scene(Attr, "#p{width:attr(data-w px)}"), "#22AA44") is [(_, _, 50f)], "width: attr(data-w px) reads the attribute as 50px");
        check(Rects(Scene(Attr, "#p{width:attr(data-nope px, 30px)}"), "#22AA44") is [(_, _, 30f)], "attr() with no such attribute takes its fallback");
        check(Scene(Attr, "#p{width:attr(data-nope px)}") == Scene(Attr, string.Empty), "attr() with no attribute and no fallback drops the declaration, as a browser does");
        check(Scene("<div id=p data-t=\"Hi\" style=\"height:20px\"></div>", "#p::before{content:attr(data-t)}").Contains("text=\"Hi\"", StringComparison.Ordinal),
            "content: attr() is still the string it was");

        // lh: the element's line box, which is its declared line-height when it has one; rlh the root's
        const string Unsized = "<div id=p style=\"height:20px;background:#22aa44\">x</div>";   // Box's inline width would beat the rule
        check(Rects(Scene(Unsized, "#p{line-height:20px;width:2lh}"), "#22AA44") is [(_, _, 40f)], "2lh with line-height: 20px is 40px");
        check(Rects(Scene(Unsized, "#p{font-size:10px;line-height:1.5;width:2lh}"), "#22AA44") is [(_, _, 30f)], "a bare line-height factor scales the font size");
        check(Rects(Scene(Unsized, ":root{line-height:30px}#p{width:2rlh}"), "#22AA44") is [(_, _, 60f)], "2rlh reads the root's line-height");

        // clip-path: path() is a CP polygon in page coordinates
        var clipped = Scene(Box, "#p{clip-path:path(\"M 0 0 L 40 0 L 40 20 Z\")}");
        check(clipped.Contains("G clip=", StringComparison.Ordinal) && clipped.Contains("Y p=[0,0,40,0,40,20", StringComparison.Ordinal),
            "clip-path: path() clips to the outline, offset to the box");

        // clip-path reference boxes: alone the box is the clip, beside a shape it is what the shape is measured in.
        // 60x20 content, 3px padding, 5px border: padding box 66x26, border box 76x36.
        const string Bordered = "<div id=p style=\"width:60px;height:20px;background:#22aa44;border:5px solid #ff8800;padding:3px\">x</div>";
        string ClipDef(string css, List<string>? warned = null)
        {
            foreach (var line in Scene(Bordered, "#p{clip-path:" + css + "}", warnings: warned).Split('\n'))
                if (line.Contains("CP id=", StringComparison.Ordinal)) return line.Trim();
            return string.Empty;
        }
        check(Num(ClipDef("padding-box"), " w=") == 66f && Num(ClipDef("padding-box"), " h=") == 26f
              && Num(ClipDef("content-box"), " w=") == 60f && Num(ClipDef("border-box"), " w=") == 76f,
            $"a box alone clips to that edge ({ClipDef("padding-box")})");
        var boxWarned = new List<string>();
        check(ClipDef("circle(50%) content-box", boxWarned).Contains("rx=22.36", StringComparison.Ordinal) && !boxWarned.Exists(m => m.Contains("clip-path", StringComparison.Ordinal)),
            $"and beside a shape it is the box the shape is measured in, with nothing refused ({ClipDef("circle(50%) content-box")})");

        // line-height in em is a factor of the font size, not the em size squared
        check(Scene("<div id=p style=\"width:200px;height:40px\">Agy</div>", "#p{line-height:1.5em}").Contains(" lh=1.5", StringComparison.Ordinal),
            "line-height: 1.5em reaches the label as the factor 1.5");

        // the attributes DomLanguage's section 8 reads as changing nothing: they size the control's box, which
        // is not in the scene, so what moves is whatever comes after it
        float Below(string field) => Rects(Scene(field + "<div id=q style=\"height:9px;background:#ff0000\"></div>", string.Empty), "#FF0000")[0].y;
        check(Below("<textarea></textarea>") == 60f && Below("<textarea rows=4 cols=30></textarea>") == 78f,
            $"rows/cols size a textarea's box ({Below("<textarea></textarea>")} -> {Below("<textarea rows=4 cols=30></textarea>")})");
        check(Below("<input type=text>") == 26f && Below("<input type=range>") == 20f,
            $"type picks the control's box ({Below("<input type=text>")} -> {Below("<input type=range>")})");
    }

    // ---- where a picture sits in its box: the IMG node's `at` -----------------------------

    /// <summary>
    /// object-position, a picture background's background-position and an SVG image's
    /// preserveAspectRatio all say where the picture sits in the room its fit leaves, and the IMG
    /// node's `at` is exactly that, as fractions of the room - which a CSS percentage already is.
    /// </summary>
    private static void PicturePosition(Action<bool, string> check)
    {
        string Img(string css, List<string>? warned = null) => Scene("<img id=p src=\"a.png\" width=80 height=50>", "#p{" + css + "}", warnings: warned);
        static string? At(string scene) => Key(scene, "at");

        check(At(Img("object-fit:cover;object-position:left top")) == "[0,0]", "keywords: left top is [0,0]");
        check(At(Img("object-fit:contain;object-position:25% 75%")) == "[0.25,0.75]", "a percentage is the fraction of the free room");
        check(At(Img("object-fit:contain;object-position:bottom")) == "[0.5,1]" && At(Img("object-fit:contain;object-position:center left")) == "[0,0.5]",
            "one value, and `center` giving way to the keyword for its own axis");
        check(At(Img("object-fit:cover;object-position:right 10% bottom 20%")) == "[0.9,0.8]", "the edge-offset form counts in from the far edges");
        check(At(Img("object-fit:cover;object-position:calc(100% - 25%) 0%")) == "[0.75,0]", "calc() of percentages");
        check(At(Img("object-position:left top")) == null, "under fill nothing is free, so no position is written");

        var warned = new List<string>();
        var lengths = Img("object-fit:cover;object-position:10px 5px", warned);
        check(At(lengths) == "[0,0]" && Key(lengths, "off") == "[10,5]" && !warned.Exists(w => w.Contains("object-position", StringComparison.Ordinal)),
            "a length is the node's off, after at: exact, and nothing said - " + Key(lengths, "off"));
        check(At(Img("object-fit:none;object-position:right 10px bottom 4px")) == "[1,1]" && Key(Img("object-fit:none;object-position:right 10px bottom 4px"), "off") == "[-10,-4]",
            "the edge-offset form with lengths: at the far edges, off back in from them");
        check(At(Img("object-fit:contain;object-position:calc(100% - 10px) 50%")) == "[1,0.5]" && Key(Img("object-fit:contain;object-position:calc(100% - 10px) 50%"), "off") == "[-10,0]",
            "calc() mixing a percentage and a length splits into at and off");
        check(At(Img("object-position:10px 0")) == null && Key(Img("object-position:10px 0"), "off") == "[10,0]",
            "under fill a length still moves the picture, as it does in a browser");
        warned.Clear();
        var zero = Img("object-fit:cover;object-position:left 0 top 0", warned);
        check(!warned.Exists(w => w.Contains("object-position", StringComparison.Ordinal)) && Key(zero, "off") == null, "a zero length is exact and writes nothing");

        // a background picture: CSS starts it at the top left, not in the middle
        string Bg(string css) => Scene("<div id=p style=\"width:80px;height:50px\"></div>", "#p{" + css + "}");
        check(At(Bg("background-image:url(a.png);background-size:cover")) == "[0,0]", "background-position's initial value is the top-left corner");
        check(At(Bg("background:url(a.png) right bottom / cover no-repeat")) == "[1,1]", "the shorthand's position words");
        check(At(Bg("background:url(a.png) center / contain no-repeat")) == null, "centred is the node's default and needs no key");
        check(Bg("background:url(https://example.test/img/a.png) 0 0 / cover no-repeat").Contains(" fit=cover", StringComparison.Ordinal),
            "the slashes inside a url() are not the shorthand's size separator");

        // an SVG image: preserveAspectRatio's alignment half
        check(At(Scene("<svg id=p width=80 height=50><image href=\"a.png\" width=80 height=50 preserveAspectRatio=\"xMinYMax meet\"/></svg>", string.Empty)) == "[0,1]",
            "preserveAspectRatio xMinYMax is [0,1]");

        // a video is ScriptedScreens' own element: no fit or position reaches it, and that is said
        warned.Clear();
        Scene("<video id=p src=\"a.mp4\" width=80 height=50></video>", "#p{object-fit:cover;object-position:left}", warnings: warned);
        check(warned.Exists(w => w.Contains("<video>", StringComparison.Ordinal) && w.Contains("object-position", StringComparison.Ordinal)),
            "object-position on a video says it is not drawn");
    }

    /// <summary>The value of a key (`at`, `off`, `tile`, `slice`...) on the first node carrying it, or null; the SCENE header is skipped.</summary>
    private static string? Key(string scene, string key)
    {
        var i = scene.IndexOf(" " + key + "=", Math.Max(0, scene.IndexOf('\n')), StringComparison.Ordinal);
        if (i < 0) return null;
        i += key.Length + 2;
        var end = scene[i] == '[' ? scene.IndexOf(']', i) + 1 : scene.IndexOfAny(new[] { ' ', '\n' }, i);
        return scene.Substring(i, (end < 0 ? scene.Length : end) - i);
    }

    /// <summary>The scene's first line for a node or def (`IMG`, `GL`...), trimmed, for a check's message.</summary>
    private static string Op(string scene, string op)
    {
        foreach (var line in scene.Split('\n'))
            if (line.TrimStart().StartsWith(op + " ", StringComparison.Ordinal)) return line.Trim();
        return "(no " + op + ")";
    }

    /// <summary>The first IMG node's box, or null.</summary>
    private static (float x, float y, float w, float h)? ImgBox(string scene)
    {
        var m = System.Text.RegularExpressions.Regex.Match(scene, @"IMG x=([-\d.]+) y=([-\d.]+) w=([-\d.]+) h=([-\d.]+)");
        if (!m.Success) return null;
        float N(int g) => float.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (N(1), N(2), N(3), N(4));
    }

    // ---- the vector layer's picture, text-outline and gradient-spread keys ----------------------

    /// <summary>
    /// background-size / -repeat, image-rendering, border-image, -webkit-text-stroke, repeating
    /// gradients, SVG spreadMethod and mask-repeat onto IMG `fit`/`tile`/`off`/`smp`/`slice`, T
    /// `ow`/`oc` and GL/GR `spread`. Each check reads the emitted scene text.
    /// </summary>
    private static void VectorKeys(Action<bool, string> check)
    {
        string Bg(string css, List<string>? warned = null) => Scene("<div id=p style=\"width:80px;height:50px\"></div>", "#p{" + css + "}", warnings: warned);
        string Img(string css) => Scene("<img id=p src=\"a.png\" width=80 height=50>", "#p{" + css + "}");
        bool Said(List<string> warned, string what) => warned.Exists(w => w.Contains(what, StringComparison.Ordinal));

        // object-fit none and scale-down are the node's own fits
        check(Img("object-fit:none").Contains(" fit=none", StringComparison.Ordinal), "object-fit: none is fit=none");
        check(Img("object-fit:scale-down").Contains(" fit=scale-down", StringComparison.Ordinal), "object-fit: scale-down is fit=scale-down");

        // background-size auto (the initial value) is the picture's own size, repeated both ways by default
        var natural = Bg("background-image:url(a.png)");
        check(Key(natural, "tile") == "[0,0]" && Key(natural, "fit") == null && Key(natural, "at") == "[0,0]",
            "an unsized picture background tiles at its own size from the top-left corner - " + Op(natural, "IMG"));
        check(Key(Bg("background:url(a.png) no-repeat"), "fit") == "none" && Key(Bg("background:url(a.png) no-repeat"), "tile") == null,
            "no-repeat at its own size is fit=none, once");
        var warned = new List<string>();
        var repeatX = Bg("background-image:url(a.png);background-repeat:repeat-x", warned);
        check(Key(repeatX, "fit") == "none" && Said(warned, "repeat-x"), "repeat-x at the picture's own size needs that size: once, and said");

        // a size in lengths is exact under every repeat but space
        var sized = Bg("background-image:url(a.png);background-size:16px 8px");
        check(Key(sized, "tile") == "[16,8]" && ImgBox(sized) is { w: 80f, h: 50f }, "background-size in px is the tile size over the whole area - " + Op(sized, "IMG"));
        var row = Bg("background-image:url(a.png);background-size:16px 8px;background-repeat:repeat-x;background-position:4px 10px");
        check(Key(row, "tile") == "[16,8]" && ImgBox(row) is { } rb && rb.w == 80f && rb.h == 8f && Key(row, "off") == "[4,0]",
            "repeat-x with a known size is a box one tile deep at the position - " + Op(row, "IMG"));
        var col = Bg("background-image:url(a.png);background-size:16px 8px;background-repeat:repeat-y;background-position:right 6px top 0");
        check(ImgBox(col) is { } cb && cb.w == 16f && cb.h == 50f && Mathf.Approximately(cb.x - ImgBox(sized)!.Value.x, 58f),
            "repeat-y with a known size is a column one tile wide, placed from the right edge - " + Op(col, "IMG"));
        var round = Bg("background-image:url(a.png);background-size:30px 30px;background-repeat:round");
        check(Key(round, "tile") == "[26.67,25]",
            "round rescales the tile so a whole number of copies fills the area (80/3, 50/2) - " + Key(round, "tile"));
        var once = Bg("background-image:url(a.png);background-size:20px 10px;background-repeat:no-repeat;background-position:right 5px bottom 5px");
        check(ImgBox(once) is { } ob && ob.w == 20f && ob.h == 10f && Mathf.Approximately(ob.x - ImgBox(sized)!.Value.x, 55f) && Mathf.Approximately(ob.y - ImgBox(sized)!.Value.y, 35f),
            "no-repeat with a known size is one copy's box at its position - " + Op(once, "IMG"));
        check(Key(Bg("background-image:url(a.png);background-size:100% 100%"), "fit") == "fill", "100% 100% is the area itself: fit=fill");
        check(Key(Bg("background-image:url(a.png);background-size:calc(50% - 8px) 10px"), "tile") == "[32,10]", "a calc() size keeps its spaces: half of 80 less 8");
        warned.Clear();
        Bg("background-image:url(a.png);background-size:30px 30px;background-repeat:space", warned);
        check(Said(warned, "space"), "space with room for several copies needs gaps the node has no key for: said");
        warned.Clear();
        check(Key(Bg("background-image:url(a.png);background-size:50px auto", warned), "fit") == "contain" && Said(warned, "proportions"),
            "a size with one auto keeps the picture's proportions, which needs its size: drawn as contain, and said");
        warned.Clear();
        Bg("background-image:url(a.png);background-size:contain", warned);
        check(Said(warned, "contain"), "contain with the initial repeat tiles copies a browser would draw: said");
        var cut = Bg("background-image:url(a.png);background-size:20px 10px;background-repeat:no-repeat;border-radius:8px");
        check(cut.Contains("CP id=bgclip", StringComparison.Ordinal) && cut.Contains("G clip=bgclip", StringComparison.Ordinal),
            "a box smaller than a rounded area is cut by the area's corners, not rounded itself - " + Op(cut, "CP"));

        // image-rendering: pixelated / crisp-edges are hard-edged texels, and inherit
        check(Img("image-rendering:pixelated").Contains(" smp=point", StringComparison.Ordinal), "image-rendering: pixelated is smp=point");
        check(Img("image-rendering:crisp-edges").Contains(" smp=point", StringComparison.Ordinal), "image-rendering: crisp-edges is smp=point");
        check(!Img("image-rendering:auto").Contains(" smp=", StringComparison.Ordinal), "image-rendering: auto samples smoothly");
        check(Scene("<div id=w><img id=p src=\"a.png\" width=80 height=50></div>", "#w{image-rendering:pixelated}").Contains(" smp=point", StringComparison.Ordinal),
            "image-rendering is inherited");
        check(Bg("background-image:url(a.png);image-rendering:pixelated").Contains(" smp=point", StringComparison.Ordinal), "a pixelated background picture");
        check(Scene("<svg id=p width=80 height=50><image href=\"a.png\" width=80 height=50 image-rendering=\"pixelated\"/></svg>", string.Empty).Contains(" smp=point", StringComparison.Ordinal),
            "an SVG image's image-rendering");

        // border-image: number slices are texels, which the node's nine-slice cuts by
        var frame = Bg("border:10px solid #000;border-image:url(b.png) 30 fill");
        check(Key(frame, "slice") == "[30,30,30,30]" && Key(frame, "bw") == "[10,10,10,10]" && !frame.Contains(" mid=0", StringComparison.Ordinal),
            "border-image with number slices is one nine-slice IMG, its middle drawn with fill - " + Op(frame, "IMG"));
        check(Bg("border:10px solid #000;border-image:url(b.png) 30").Contains(" mid=0", StringComparison.Ordinal), "without fill the middle is not drawn, as CSS");
        check(Key(Bg("border:10px solid #000;border-image:url(b.png) 24 12;border-image-width:auto"), "bw") == "[24,12,24,12]",
            "border-image-width: auto is the slices' own size");
        check(Bg("border:10px solid #000;border-image:url(b.png) 25% fill").Contains(" uv=[0.25,0.25,0.75,0.75]", StringComparison.Ordinal),
            "percentage slices stay exact as nine uv crops");
        warned.Clear();
        var rounded = Bg("border:10px solid #000;border-image:url(b.png) 30 round", warned);
        check(Key(rounded, "slice") == "[30,30,30,30]" && Said(warned, "border-image-repeat: round"), "a tiled border image needs the picture's size: stretched, and said");

        // -webkit-text-stroke is the label's outline
        const string Label = "<div id=p style=\"width:120px;height:20px\">Ag</div>";
        check(Scene(Label, "#p{-webkit-text-stroke:2px #ff0000}").Contains(" ow=2 oc=#FF0000", StringComparison.Ordinal), "-webkit-text-stroke: 2px red is ow=2 oc=#FF0000");
        check(Scene(Label, "#p{-webkit-text-stroke-width:1.5px;-webkit-text-stroke-color:#00ff00}").Contains(" ow=1.5 oc=#00FF00", StringComparison.Ordinal), "the two longhands");
        check(Scene(Label, "#p{-webkit-text-stroke:thin}").Contains(" ow=1 oc=#EEEEEE", StringComparison.Ordinal), "the colour defaults to the text's own");
        check(Scene(Label, "#p{color:#123456;-webkit-text-fill-color:transparent;-webkit-text-stroke:1px}").Contains(" oc=#123456", StringComparison.Ordinal),
            "hollow lettering: a transparent fill still outlines in `color`");
        check(Scene("<div id=w><div id=p style=\"width:120px;height:20px\">Ag</div></div>", "#w{-webkit-text-stroke:1px #ff0000}").Contains(" ow=1 oc=#FF0000", StringComparison.Ordinal),
            "-webkit-text-stroke is inherited");
        check(!Scene(Label, string.Empty).Contains(" ow=", StringComparison.Ordinal), "no stroke, no outline");
        check(Scene("<svg id=p width=80 height=50><text x=\"4\" y=\"20\" font-size=\"12\" fill=\"#ffffff\" stroke=\"#ff0000\" stroke-width=\"2\">Ag</text></svg>", string.Empty)
                .Contains(" ow=2 oc=#FF0000", StringComparison.Ordinal),
            "a stroke on SVG text is the same outline");

        // repeating gradients are one period the scene repeats
        var diagonal = Bg("background:repeating-linear-gradient(45deg,#ff0000 0 6px,#0000ff 6px 12px)");
        check(diagonal.Contains(" spread=repeat", StringComparison.Ordinal) && !diagonal.Contains("CP id=cut", StringComparison.Ordinal)
              && diagonal.Contains("stops=[[0,#FF0000],[0.5,#FF0000],[0.5,#0000FF],[1,#0000FF]]", StringComparison.Ordinal),
            "diagonal hard-edged stripes are one period with spread=repeat, no clip per stripe - " + Op(diagonal, "GL"));
        var ramp = Bg("background:repeating-linear-gradient(to right,#ff0000 0,#0000ff 16px)");
        check(ramp.Contains(" spread=repeat", StringComparison.Ordinal) && ramp.Contains(" x1=0 y1=0.5 x2=0.2 y2=0.5", StringComparison.Ordinal),
            "a repeating ramp's line is one 16px period of an 80px box - " + Op(ramp, "GL"));
        var rings = Bg("background:repeating-radial-gradient(circle,#ff0000 0 6px,#0000ff 6px 12px)");
        check(rings.Contains(" spread=repeat", StringComparison.Ordinal) && Key(rings, "r") == "0.15",
            "repeating-radial rings: the ray cut to one 12px period (12 / 80) - " + Op(rings, "GR"));
        var reflect = Scene("<svg id=p width=80 height=50><defs><linearGradient id=g spreadMethod=\"reflect\" x2=\"0.25\"><stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs>"
                            + "<rect width=\"80\" height=\"50\" fill=\"url(#g)\"/></svg>", string.Empty);
        check(reflect.Contains(" spread=reflect", StringComparison.Ordinal), "SVG spreadMethod=reflect is spread=reflect");

        // mask-repeat: an axis-aligned mask tile repeats as its own ramp
        var tiled = Bg("background:#22aa44;mask-image:linear-gradient(#000,transparent);mask-size:100% 10px");
        check(tiled.Contains(" spread=repeat", StringComparison.Ordinal), "a 10px mask tile along its gradient repeats (mask-repeat's initial value) - " + Op(tiled, "GL"));
        check(!Bg("background:#22aa44;mask-image:linear-gradient(#000,transparent);mask-size:100% 10px;mask-repeat:no-repeat").Contains(" spread=", StringComparison.Ordinal),
            "mask-repeat: no-repeat does not");
        warned.Clear();
        Bg("background:#22aa44;mask-image:linear-gradient(45deg,#000,transparent);mask-size:20px 20px", warned);
        check(Said(warned, "mask-repeat"), "a diagonal mask tiled on a lattice is not one repeated ramp: said");
    }

    /// <summary>The y of the first decoration stroke in the scene, or NaN when none was drawn.</summary>
    private static float StrokeY(string scene)
    {
        var i = scene.IndexOf("L p=[", StringComparison.Ordinal);
        if (i < 0) return float.NaN;
        var parts = scene.Substring(i + 5, scene.IndexOf(']', i) - i - 5).Split(',');
        return float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void TextProperties(Action<bool, string> check)
    {
        const string Box = "<div id=p style=\"width:200px;height:18px\">Agy one</div>";

        // font-synthesis: none refuses both fakes; the default allows both
        var synth = Scene(Box, "#p{font-style:italic;font-weight:bold;font-synthesis:none}");
        var faked = Scene(Box, "#p{font-style:italic;font-weight:bold}");
        check(faked.Contains("<i>", StringComparison.Ordinal) && faked.Contains("weight=bold", StringComparison.Ordinal)
              && !synth.Contains("<i>", StringComparison.Ordinal) && !synth.Contains("weight=bold", StringComparison.Ordinal),
            "font-synthesis: none draws neither the sheared italic nor the widened bold");
        check(Scene(Box, "#p{font-style:italic;font-weight:bold;font-synthesis:style}") is var styleOnly
              && styleOnly.Contains("<i>", StringComparison.Ordinal) && !styleOnly.Contains("weight=bold", StringComparison.Ordinal),
            "font-synthesis: style keeps the italic and drops the bold");

        // text-underline-position: under puts the line below the descenders. Measured against the
        // same underline with the offset pinned, so only the property moves it.
        var above = StrokeY(Scene(Box, "#p{text-decoration:underline;text-underline-offset:0}"));
        var under = StrokeY(Scene(Box, "#p{text-decoration:underline;text-underline-offset:0;text-underline-position:under}"));
        check(!float.IsNaN(above) && !float.IsNaN(under) && under > above + 2f,
            $"text-underline-position: under drops the line clear of the descenders ({above} -> {under})");

        // font-variant / font-variant-caps: small-caps
        check(Scene(Box, "#p{font-variant:small-caps}").Contains("<smallcaps>", StringComparison.Ordinal),
            "font-variant: small-caps reaches the glyphs");
        check(Scene(Box, "#p{font-variant-caps:small-caps}").Contains("<smallcaps>", StringComparison.Ordinal),
            "font-variant-caps: small-caps reaches the glyphs");

        // text-orientation: upright stacks the characters instead of turning the line
        var upright = Scene("<div id=p style=\"width:30px;height:120px\">Hi</div>", "#p{writing-mode:vertical-rl;text-orientation:upright}");
        var turned = Scene("<div id=p style=\"width:30px;height:120px\">Hi</div>", "#p{writing-mode:vertical-rl}");
        check(upright.Contains("text=\"H\\ni\"", StringComparison.Ordinal) && !upright.Contains("r=90", StringComparison.Ordinal)
              && turned.Contains("r=90", StringComparison.Ordinal),
            "text-orientation: upright stacks the glyphs and leaves the line unturned");

        // word-spacing: the box was measured with the gap, so the text must carry it too
        check(Scene(Box, "#p{word-spacing:6px}").Contains("<space=6px>", StringComparison.Ordinal),
            "word-spacing widens the gaps in the drawn text, not only the box");

        // overflow-wrap / word-wrap: break-word breaks the long word ONLY - break-all breaks them all
        const string Long = "<div id=p style=\"width:150px;height:60px\">short supercalifragilisticexpialidocious</div>";
        foreach (var name in new[] { "overflow-wrap", "word-wrap" })
        {
            var s = Scene(Long, "#p{" + name + ":break-word}");
            check(s.Contains("short ", StringComparison.Ordinal) && s.Contains("s\u200Bu\u200Bp\u200Be\u200Br", StringComparison.Ordinal),
                name + ": break-word breaks a word too long for the line and leaves a short one whole");
        }
    }

    // ---- layout properties, also only visible in the scene -------------------------------

    /// <summary>The x/y/w of every solid R rect painted in <paramref name="hex"/>, in scene order.</summary>
    private static List<(float x, float y, float w)> Rects(string scene, string hex)
    {
        var list = new List<(float, float, float)>();
        foreach (var raw in scene.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("R ", StringComparison.Ordinal) || !line.Contains("f=" + hex, StringComparison.Ordinal)) continue;
            list.Add((Num(line, " x="), Num(line, " y="), Num(line, " w=")));
        }
        return list;
    }

    private static float Num(string line, string key)
    {
        var i = line.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return float.NaN;
        i += key.Length;
        var j = i;
        while (j < line.Length && line[j] != ' ') j++;
        return float.TryParse(line.Substring(i, j - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
    }

    private static int CountOf(string s, string needle)
    {
        var n = 0;
        for (var i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    private static void LayoutProperties(Action<bool, string> check)
    {
        // border-collapse: an interior edge is drawn by one of the two cells that share it, not both.
        // One side per cell, so the count of painted edges IS the count of drawn borders.
        const string Table = "<table id=p><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>";
        int Edges(string side, bool collapse) => Rects(Scene(Table, "td{border-" + side + ":2px solid #ff8800}"
            + (collapse ? "#p{border-collapse:collapse}" : string.Empty)), "#FF8800").Count;
        check(Edges("left", false) == 4 && Edges("left", true) == 2 && Edges("top", false) == 4 && Edges("top", true) == 2,
            $"border-collapse: collapse draws each shared edge once ({Edges("left", false)}->{Edges("left", true)} vertical, {Edges("top", false)}->{Edges("top", true)} horizontal)");

        // border-spacing: cells abut under `separate` unless it says otherwise
        var tight = Rects(Scene(Table, "td{background:#884444}"), "#884444");
        var spaced = Rects(Scene(Table, "td{background:#884444}#p{border-spacing:9px 7px}"), "#884444");
        check(tight.Count == 4 && spaced.Count == 4 && spaced[1].x > tight[1].x + 3f && spaced[2].y > tight[2].y + 6f,
            $"border-spacing separates the cells ({tight[1].x}->{spaced[1].x} across, {tight[2].y}->{spaced[2].y} down)");

        // content: open-quote was falling through to the empty string
        const string Quoted = "<p id=p>hi</p>";
        var curly = Scene(Quoted, "#p::before{content:open-quote}#p::after{content:close-quote}");
        var guillemets = Scene(Quoted, "#p::before{content:open-quote}#p::after{content:close-quote}#p{quotes:\"«\" \"»\"}");
        check(curly.Contains("text=\"“\"", StringComparison.Ordinal) && curly.Contains("text=\"”\"", StringComparison.Ordinal)
              && guillemets.Contains("text=\"«\"", StringComparison.Ordinal) && guillemets.Contains("text=\"»\"", StringComparison.Ordinal),
            "content: open-quote/close-quote draw a quote pair, and `quotes` chooses which");

        // all: unset undoes what the cascade set before it on this element
        const string Painted = "<div id=p class=x>hi</div>";
        check(Scene(Painted, ".x{background:#22aa44;width:120px;height:30px}").Contains("f=#22AA44", StringComparison.Ordinal)
              && !Scene(Painted, ".x{background:#22aa44;width:120px;height:30px}#p{all:unset}").Contains("f=#22AA44", StringComparison.Ordinal),
            "all: unset drops the declarations that came before it");

        // columns: the shorthand has to be split before anything reads column-count
        const string Prose = "<p id=p style=\"width:220px\">one two three four five six seven eight nine ten</p>";
        check(CountOf(Scene(Prose, string.Empty), "T x=") == 1 && CountOf(Scene(Prose, "#p{columns:2 60px}"), "T x=") == 2,
            "columns: 2 60px splits the text into two columns");

        // clear: the floated row wraps, so a line break before the child is a real one
        const string Floats = "<div id=p style=\"width:200px\">"
            + "<div style=\"float:left;width:60px;height:20px;background:#884444\"></div>"
            + "<div id=b style=\"width:60px;height:20px;background:#884444\"></div></div>";
        var inline_ = Rects(Scene(Floats, string.Empty), "#884444");
        var cleared = Rects(Scene(Floats, "#b{clear:both}"), "#884444");
        check(inline_.Count == 2 && cleared.Count == 2 && inline_[1].y <= inline_[0].y + 0.5f && cleared[1].y > cleared[0].y + 10f,
            $"clear: both puts the child on the next line ({inline_[1].y} -> {cleared[1].y})");

        // scrollbar-gutter: stable insets the content so the box does not jump when it overflows
        const string Scroller = "<div id=p style=\"width:100px;height:40px;overflow:auto\">"
            + "<div style=\"height:200px;background:#884444\"></div></div>";
        var loose = Rects(Scene(Scroller, string.Empty), "#884444");
        var gutter = Rects(Scene(Scroller, "#p{scrollbar-gutter:stable}"), "#884444");
        check(loose.Count == 1 && gutter.Count == 1 && gutter[0].w < loose[0].w - 7f,
            $"scrollbar-gutter: stable reserves the bar's width ({loose[0].w} -> {gutter[0].w})");
    }

    // ---- @container ----------------------------------------------------------------------

    /// <summary>
    /// A container query is answered by the nearest ANCESTOR that establishes containment, by name
    /// when the query gives one. It used to be answered by MediaMatches against the page's design
    /// width with the name thrown away, so `@container sidebar` and `@container main` were the same
    /// query and a 200px panel inside a 900px page took the 900px branch.
    /// </summary>
    private static void Containers(Action<bool, string> check)
    {
        var info = CssParser.ContainerInfo;
        var sink = CssParser.ContainerWarn;
        var warnings = new List<string>();
        try
        {
            CssParser.ViewportWidth = 900f;     // the page: every query below would match if it answered from here
            CssParser.ViewportHeight = 900f;
            CssParser.ContainerWarn = warnings.Add;

            // side is 200 wide and named "sidebar"; main is 900 wide and named "main"
            var body = Doc("<body><div id=side><p id=a>x</p></div><div id=main><p id=b>x</p></div><p id=loose>x</p></body>");
            HtmlNode Id(string id) { foreach (var c in body.Children) { if (c.Attr("id") == id) return c; foreach (var g in c.Children) if (g.Attr("id") == id) return g; } throw new InvalidOperationException(id); }

            var boxes = new Dictionary<string, (Dictionary<string, string> css, float width, float height)>(StringComparer.Ordinal);
            Dictionary<string, string> Css(params string[] kv)
            {
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
                return d;
            }
            boxes["side"] = (Css("container-type", "inline-size", "container-name", "sidebar"), 200f, 500f);
            boxes["main"] = (Css("container-type", "inline-size", "container-name", "main"), 900f, 500f);
            CssParser.ContainerInfo = n => n.Attr("id") is { } nid && boxes.TryGetValue(nid, out var box)
                ? box
                : ((Dictionary<string, string> css, float width, float height)?)null;

            bool Hit(string sheet, HtmlNode node)
            {
                foreach (var r in CssParser.ParseStylesheet(sheet, warnings.Add))
                    foreach (var sel in r.Selectors)
                        if (sel.Matches(node)) return true;
                return false;
            }

            // the two the brief asks for
            check(!Hit("@container sidebar (min-width: 400px) { p { color: red } }", Id("b")),
                "a named query does not match a container with a different name");
            check(Hit("@container main (min-width: 400px) { p { color: red } }", Id("b")),
                "and does match the container it names");
            check(!Hit("@container (min-width: 400px) { p { color: red } }", Id("a"))
                && Hit("@container (min-width: 400px) { p { color: red } }", Id("b")),
                "an unnamed query is answered by the nearest container, not by the 900px page");
            check(!Hit("@container (max-width: 400px) { p { color: red } }", Id("b"))
                && Hit("@container (max-width: 400px) { p { color: red } }", Id("a")),
                "and the narrow container answers max-width where the page would not");

            // the name is a list, and the `container` shorthand carries both halves
            boxes["side"] = (Css("container-type", "inline-size", "container-name", "tools sidebar"), 200f, 500f);
            check(Hit("@container sidebar (max-width: 400px) { p { color: red } }", Id("a")), "container-name is a list of names");
            boxes["side"] = (Css("container", "sidebar / inline-size"), 200f, 500f);
            check(Hit("@container sidebar (max-width: 400px) { p { color: red } }", Id("a")), "the `container` shorthand gives the name and the type");
            boxes["side"] = (Css("container-type", "normal", "container-name", "sidebar"), 200f, 500f);
            check(!Hit("@container sidebar (max-width: 400px) { p { color: red } }", Id("a")),
                "container-type: normal establishes no containment, so the query passes it by");
            boxes["side"] = (Css("container-type", "inline-size", "container-name", "sidebar"), 200f, 500f);

            // nested containers: the inner one wins for an unnamed query, and a name reaches past it
            check(Hit("@container (max-width: 400px) { p { color: red } }", Id("a")), "the nearest container is the inner one");

            // an unresolvable query fails loudly rather than guessing
            CssParser.ForgetReported();
            warnings.Clear();
            check(!Hit("@container (min-width: 10px) { p { color: red } }", Id("loose")),
                "a query with no container above it matches nothing, as in a browser");
            check(warnings.Count == 1 && warnings[0].Contains("no ancestor", StringComparison.Ordinal),
                $"and says so rather than falling back to the page ({warnings.Count}: {string.Join(" | ", warnings)})");
            CssParser.ForgetReported();
            warnings.Clear();
            check(!Hit("@container sidebar (min-width: 10px) { p { color: red } }", Id("b"))
                && warnings.Count == 1 && warnings[0].Contains("sidebar", StringComparison.Ordinal),
                $"a query naming a container that is not above the element names it ({string.Join(" | ", warnings)})");

            // inline-size gives the inline axis alone; a block-axis question is refused, not answered from the page
            CssParser.ForgetReported();
            warnings.Clear();
            check(!Hit("@container (min-height: 10px) { p { color: red } }", Id("b"))
                && warnings.Count == 1 && warnings[0].Contains("block axis", StringComparison.Ordinal),
                $"inline-size refuses a height query instead of answering it from the page ({string.Join(" | ", warnings)})");
            boxes["main"] = (Css("container-type", "size", "container-name", "main"), 900f, 500f);
            check(Hit("@container (min-height: 400px) { p { color: red } }", Id("b")), "container-type: size does answer the block axis");
            check(Hit("@container (min-inline-size: 400px) { p { color: red } }", Id("b"))
                && !Hit("@container (min-block-size: 900px) { p { color: red } }", Id("b")),
                "the container syntax's own inline-size / block-size names");

            // a style query is not answerable here; refused at parse time, with the block skipped
            CssParser.ForgetReported();
            warnings.Clear();
            check(!Hit("@container style(--theme: dark) { p { color: red } }", Id("b"))
                && warnings.Count == 1 && warnings[0].Contains("style", StringComparison.Ordinal),
                $"a style() query is refused rather than mis-parsed as a container name ({string.Join(" | ", warnings)})");

            // nothing installed at all: every query says so and matches nothing
            CssParser.ForgetReported();
            warnings.Clear();
            CssParser.ContainerInfo = null;
            check(!Hit("@container (min-width: 10px) { p { color: red } }", Id("b"))
                && warnings.Count == 1 && warnings[0].Contains("laid-out size", StringComparison.Ordinal),
                $"with no way to read a container's size, a query matches nothing and says why ({string.Join(" | ", warnings)})");
        }
        finally
        {
            CssParser.ContainerInfo = info;
            CssParser.ContainerWarn = sink;
            CssParser.ForgetReported();
        }
    }

    /// <summary>
    /// The same question through the real pipeline: a page built, laid out, and re-cascaded the way
    /// the renderer must once a container's box is known. This is also the proof for the two hooks,
    /// which HtmlRenderer.Build now installs (ContainerInfo) and the GeometryChangedEvent on a
    /// container fires (ContainerResized); the lambda below is the same reading, run here against
    /// the real cascade rather than a stub.
    /// </summary>
    private static void ContainersLaidOut(Action<bool, string> check)
    {
        var info = CssParser.ContainerInfo;
        var sink = CssParser.ContainerWarn;
        try
        {
            ResolvedStyle.DefaultFace = FontLibrary.Default();
            HtmlRenderer.SurfaceAspect = 1f;
            OffThread.MainThreadId = Environment.CurrentManagedThreadId;
            OffThread.Boxes = new Dictionary<VisualElement, OffThread.Box>();
            OffThread.Job = OffThread.Globals.Take();

            // A 900px page holding a 200px panel and a 800px one. Both cards are containers; the
            // plain rule comes first so the container rule wins by source order when it matches.
            const string Html = "<html><head><meta name=\"viewport\" content=\"width=900\"><style>"
                + ".card{container-type:inline-size}"
                + "#narrow{width:200px}#wide{width:800px}"
                + ".t{color:#ff0000}"
                + "@container (min-width:400px){.t{color:#00ff00}}"
                + "</style></head><body>"
                + "<div id=narrow class=card><p id=a class=t>x</p></div>"
                + "<div id=wide class=card><p id=b class=t>x</p></div>"
                + "</body></html>";

            var built = HtmlRenderer.Build(Html, FontLibrary.Default());
            CssParser.ContainerWarn = built.Warnings.Add;
            CssParser.ContainerInfo = n =>
            {
                if (n.Attr("id") is not { } nid || !built.ById.TryGetValue(nid, out var cve))
                    return null;
                var rs = cve.resolvedStyle;
                return (built.CssOf(cve),
                    cve.layout.width - rs.paddingLeft - rs.paddingRight - rs.borderLeftWidth - rs.borderRightWidth,
                    cve.layout.height - rs.paddingTop - rs.paddingBottom - rs.borderTopWidth - rs.borderBottomWidth);
            };

            string Colour(string id) => built.ById.TryGetValue(id, out var ve) && built.CssOf(ve).TryGetValue("color", out var c) ? c.Trim().ToLowerInvariant() : "(none)";
            var panel = new Panel(built.Root);
            panel.Layout(900f, 900f);
            // The build's cascade ran before any layout, so both took the plain rule then. The first
            // layout gives the containers their size, and the GeometryChangedEvent hook re-cascades
            // under each one by itself - so this is already the right answer with no manual
            // re-cascade. (This check used to expect #ff0000 for both and called the manual
            // re-cascade below "not optional": that described the hook not existing. The checks
            // after it now prove the manual path is idempotent rather than necessary.)
            check(Colour("a") == "#ff0000" && Colour("b") == "#00ff00",
                $"the first layout re-cascades under each container on its own: narrow {Colour("a")} (no match), wide {Colour("b")} (match)");

            foreach (var id in new[] { "narrow", "wide" })
                if (built.ById.TryGetValue(id, out var card)) built.Reclass(card, "card");
            check(Colour("a") == "#ff0000", $"the 200px panel inside a 900px page takes the small branch (got {Colour("a")})");
            check(Colour("b") == "#00ff00", $"and the 800px one beside it takes the large branch (got {Colour("b")})");
        }
        finally
        {
            CssParser.ContainerInfo = info;
            CssParser.ContainerWarn = sink;
            CssParser.ForgetReported();
        }
    }
}

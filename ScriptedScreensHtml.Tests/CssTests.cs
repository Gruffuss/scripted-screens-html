using System;
using System.Collections.Generic;
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
        try
        {
            AttributeFlags(check);
            NthOf(check);
            Media(check);
            Supports(check);
            ColumnCombinator(check);
            StatePseudos(check);
            PseudoElements(check);
            ImpliedEndTags(check);
        }
        finally
        {
            CssParser.ViewportWidth = w;
            CssParser.ViewportHeight = h;
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
}

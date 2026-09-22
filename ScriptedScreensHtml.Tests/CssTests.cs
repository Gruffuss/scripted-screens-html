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
            Containers(check);
            ContainersLaidOut(check);
            TextProperties(check);
            LayoutProperties(check);
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

    // ---- text properties that only exist in the emitted scene ----------------------------

    /// <summary>
    /// The seven below are drawn, not laid out, so the cascade cannot answer for them: a property
    /// the record carries and the emitter ignores draws exactly what its absence draws. The scene
    /// string is the only witness, so these compile the Unity half like CssLanguage does.
    /// </summary>
    private static string Scene(string body, string css)
    {
        ResolvedStyle.DefaultFace = FontLibrary.Default();
        HtmlRenderer.SurfaceAspect = 1f;
        OffThread.MainThreadId = Environment.CurrentManagedThreadId;
        OffThread.Job = OffThread.Globals.Take();

        var html = "<html><head><meta name=\"viewport\" content=\"width=400\"><style>"
                   + "#p{color:#eeeeee;font-size:14px}" + css + "</style></head><body>" + body + "</body></html>";
        var built = HtmlRenderer.Build(html, FontLibrary.Default());
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
        OffThread.Active = true;
        try
        {
            var tweens = new Tweens();
            tweens.Diff(built.Root, built, 0f);
            var output = VectorEmitter.Emit(built, built.Root, size.x, size.y, tweens, 0f, null);
            return new string(output.Chars, 0, output.Length);
        }
        finally { OffThread.Active = false; }
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
    /// the renderer must once a container's box is known. This is also the proof for the two hooks -
    /// nothing in HtmlRenderer installs them yet, so the lambda below IS the patch that has to land
    /// there, run here against the real cascade rather than a stub.
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

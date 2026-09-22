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
        }
        finally
        {
            CssParser.ViewportWidth = w;
            CssParser.ViewportHeight = h;
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
}

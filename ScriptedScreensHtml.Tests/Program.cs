using System;
using System.Collections.Generic;
using ScriptedScreensHtml;

var failures = new List<string>();
void Check(bool ok, string what)
{
    Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
    if (!ok) failures.Add(what);
}

if (args.Length > 0 && args[0] == "--probe") { Probe.Run(); return 0; }
if (args.Length > 0 && args[0] == "--jsbench") { JsBench.Run(); return 0; }
Console.WriteLine("HtmlParser");
{
    var warnings = new List<string>();
    var doc = HtmlParser.Parse(
        "<!DOCTYPE html><html><head><style>.a { color: red }</style></head>" +
        "<body class='x y'><div id=main style=\"width:10px\">Hello <b>big</b> world<br/>next</div>" +
        "<p>unclosed<p>second &amp; &lt;3 &#x41;</p><!-- c --><img src=a.png><hr></body></html>",
        warnings.Add);

    var html = doc.Children[0];
    Check(html.Tag == "html", "root element is html");
    var body = html.Children[1];
    Check(body.Tag == "body" && body.Attr("class") == "x y", "body with unquoted-ish class attr");
    var div = body.Children[0];
    Check(div.Attr("id") == "main" && div.Attr("style") == "width:10px", "bare and quoted attributes");
    Check(div.Children.Count == 5, $"div has text, b, text, br, text = 5 children (got {div.Children.Count})");
    Check(div.Children[0].Text == "Hello " && div.Children[1].Tag == "b" && div.Children[1].Children[0].Text == "big", "inline b inside text");
    Check(div.Children[3].Tag == "br" && div.Children[3].Children.Count == 0, "self-closing br is void");
    // Tag soup: an unclosed <p> nests the next <p> rather than closing at it. Documented
    // behaviour, pinned here so a change to it is deliberate.
    var p1 = body.Children[1];
    Check(p1.Tag == "p" && p1.Children.Count == 4 && p1.Children[1].Tag == "p" && p1.Children[3].Tag == "hr", "unclosed <p> nests the following <p> and later siblings");
    var second = p1.Children[1];
    Check(second.Children[0].Text == "second & <3 A", $"entities decoded (got \"{second.Children[0].Text}\")");
    Check(html.Children[0].Children[0].Tag == "style" && html.Children[0].Children[0].Children[0].Text.Contains(".a"), "style raw text preserved");
    Check(warnings.Count == 0, $"no warnings (got {string.Join("; ", warnings)})");

    var soup = HtmlParser.Parse("<div><span>a</div></span>", warnings.Add);
    Check(soup.Children[0].Tag == "div" && soup.Children[0].Children[0].Tag == "span" && warnings.Count == 1, "stray close tag warns once and does not break the tree");
}

Console.WriteLine("CssParser");
{
    var warnings = new List<string>();
    var rules = CssParser.ParseStylesheet(
        "/* comment */ body { color: #fff; background: rgba(1, 2, 3, .5); }\n" +
        ".tank, #main .row.big { flex: 1 1 0; margin: 1px 2px }\n" +
        "@media (x) { .z { color: red } }\n" +
        "a:hover { color: red }\n" +
        "div.k { border: 1px solid red !important }",
        warnings.Add);
    Check(rules.Count == 4, $"four rules, a:hover kept as never-matching (got {rules.Count})");
    Check(rules[0].Declarations.Count == 2 && rules[0].Declarations[1].Value == "rgba(1, 2, 3, .5)", "semicolon inside rgba() not split");
    Check(rules[1].Selectors.Count == 2, "selector list split on comma");
    var sel = rules[1].Selectors[1];
    Check(sel.Chain.Count == 2 && sel.Chain[0].Id == "main" && sel.Chain[1].Classes.Count == 2, "descendant chain #main .row.big");
    Check(sel.Specificity == 10000 + 200, $"specificity ids/classes (got {sel.Specificity})");
    Check(rules[3].Declarations[0].Value == "1px solid red", "!important stripped");
    Check(warnings.Count == 0, $"@media with an unknown feature is dropped silently, :hover does not warn (got {warnings.Count})");

    CssParser.ViewportWidth = 640f; CssParser.ViewportHeight = 640f;
    var media = CssParser.ParseStylesheet("@media (min-width: 600px) { .w { color: red } } @media screen and (max-width: 300px) { .n { color: red } } @media not print { .p { color: red } }", warnings.Add);
    Check(media.Count == 2 && media[0].Selectors[0].Chain[0].Classes[0] == "w" && media[1].Selectors[0].Chain[0].Classes[0] == "p", $"@media decided against the design width (got {media.Count} rules)");
    var attrDoc = HtmlParser.Parse("<div><span data-k=\"x y\">a</span><b>b</b><i>c</i><i lang=\"en-GB\">d</i></div>");
    var d0 = attrDoc.Children[0];
    Check(CssParser.ParseSelector("[data-k]", null)!.Matches(d0.Children[0]) && !CssParser.ParseSelector("[data-k]", null)!.Matches(d0.Children[1]), "[attr] presence");
    Check(CssParser.ParseSelector("span[data-k~=y]", null)!.Matches(d0.Children[0]) && CssParser.ParseSelector("[data-k^=\"x \"]", null)!.Matches(d0.Children[0]) && !CssParser.ParseSelector("[data-k=x]", null)!.Matches(d0.Children[0]), "[attr~=], [attr^=], [attr=] value tests");
    Check(CssParser.ParseSelector("i[lang|=en]", null)!.Matches(d0.Children[3]), "[attr|=] dash match");
    Check(CssParser.ParseSelector("span ~ i", null)!.Matches(d0.Children[2]) && CssParser.ParseSelector("span ~ i", null)!.Matches(d0.Children[3]) && !CssParser.ParseSelector("span + i", null)!.Matches(d0.Children[2]), "~ reaches any later sibling, + only the next");
    var before = CssParser.ParseSelector(".tag::before", null)!;
    var gen = new HtmlNode { Tag = "span", Parent = d0 }; gen.Attributes["data-pseudo"] = "before"; d0.Attributes["class"] = "tag";
    Check(before.Chain[0].PseudoElement == "before" && before.Matches(gen) && !before.Matches(d0) && !CssParser.ParseSelector("span", null)!.Matches(gen), "::before matches the generated child only, and plain rules skip it");
    Check(rules[2].Selectors[0].Chain[0].Pseudos.Count == 1, ":hover parsed as a pseudo-class");

    var doc = HtmlParser.Parse("<div id=main><div class='row big'><span class=row>t</span></div></div>");
    var main = doc.Children[0];
    var row = main.Children[0];
    var span = row.Children[0];
    Check(sel.Matches(row), "#main .row.big matches the row");
    Check(!sel.Matches(span), "#main .row.big does not match .row only");
    var tagSel = CssParser.ParseSelector("div.k", null)!;
    Check(tagSel.Chain[0].Tag == "div" && tagSel.Chain[0].Classes[0] == "k", "tag.class compound");

    var inline = CssParser.ParseDeclarations("color:red; transform: translate(1px, 2px) rotate(3deg);");
    Check(inline.Count == 2 && inline[1].Value == "translate(1px, 2px) rotate(3deg)", "inline declarations");
}

Console.WriteLine("Keyframes, child combinator, !important");
{
    var warnings = new List<string>();
    var kfs = new Dictionary<string, CssKeyframes>();
    var rules = CssParser.ParseStylesheet(
        "@keyframes spin { from { transform: rotate(0deg) } 50% { opacity: .5 } to { transform: rotate(360deg) } }" +
        "@keyframes blink { 0%, 100% { opacity: 1 } 50% { opacity: 0 } }" +
        ".a > .b { color: red }" +
        ".c { color: blue !important; width: 1px }",
        warnings.Add, kfs);
    Check(kfs.Count == 2 && kfs["spin"].Frames.Count == 3 && kfs["spin"].Frames[1].Percent == 50f, "two @keyframes parsed, frames sorted");
    Check(kfs["blink"].Frames.Count == 3 && kfs["blink"].Frames[0].Percent == 0f && kfs["blink"].Frames[2].Percent == 100f, "0%, 100% selector list expands to two frames");
    Check(warnings.Count == 0, $"no warnings for keyframes (got {string.Join("; ", warnings)})");

    var doc = HtmlParser.Parse("<div class=a><div class=b id=direct><div class=b id=deep></div></div></div>");
    var direct = doc.Children[0].Children[0];
    var deep = direct.Children[0];
    var child = rules[0].Selectors[0];
    Check(child.Matches(direct) && !child.Matches(deep), ".a > .b matches the direct child only");
    var desc = CssParser.ParseSelector(".a .b", null)!;
    Check(desc.Matches(direct) && desc.Matches(deep), ".a .b matches both");

    Check(rules[1].Declarations[0].Important && !rules[1].Declarations[1].Important, "!important flag set per declaration");
}

Console.WriteLine(failures.Count == 0 ? "ALL PASS" : $"{failures.Count} FAILED");
return failures.Count == 0 ? 0 : 1;

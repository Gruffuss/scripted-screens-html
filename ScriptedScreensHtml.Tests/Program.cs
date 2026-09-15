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

    var pre = HtmlParser.Parse("<pre>\n  two  spaces\nnext line</pre><p>  a   b  </p>");
    Check(pre.Children[0].Children[0].Text == "  two  spaces\nnext line", $"pre keeps spaces and newlines, drops the first newline (got \"{pre.Children[0].Children[0].Text.Replace("\n", "|")}\")");
    Check(pre.Children[1].Children[0].Text == " a b ", "outside pre whitespace collapses");
    var ent = HtmlParser.Parse("<p>&copy; &eacute; &alpha; &ne; &hearts; &#x2713; &nosuch;</p>");
    Check(ent.Children[0].Children[0].Text == "\u00A9 \u00E9 \u03B1 \u2260 \u2665 \u2713 &nosuch;", $"named entities from the table, unknown left alone (got \"{ent.Children[0].Children[0].Text}\")");
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

    var nested = CssParser.ParseStylesheet(".card { color: red; .title { font-size: 20px } &:hover { color: blue } > b { color: green } @media (min-width: 100px) { padding: 4px } }", warnings.Add);
    Check(nested.Count == 5, $"nesting expands to five rules (got {nested.Count})");
    Check(nested[0].Selectors[0].Chain.Count == 1 && nested[0].Declarations[0].Name == "color", "parent rule keeps its own declarations");
    Check(nested[1].Selectors[0].Chain.Count == 2 && nested[1].Selectors[0].Chain[1].Classes[0] == "title", "nested .title becomes .card .title");
    Check(nested[2].Selectors[0].Chain.Count == 1 && nested[2].Selectors[0].Chain[0].Pseudos.Count == 1, "&:hover becomes .card:hover");
    Check(nested[3].Selectors[0].Chain[1].ChildOfPrevious, "> b keeps the child combinator");
    Check(nested[4].Declarations[0].Name == "padding", "nested @media applies to the parent selector");
    var typeDoc = HtmlParser.Parse("<div><p>a</p><span>x</span><p>b</p><p>c</p><i></i></div>");
    var td = typeDoc.Children[0];
    Check(CssParser.ParseSelector("p:nth-of-type(2)", null)!.Matches(td.Children[2]) && !CssParser.ParseSelector("p:nth-of-type(2)", null)!.Matches(td.Children[0]), ":nth-of-type counts only the tag");
    Check(CssParser.ParseSelector("span:only-of-type", null)!.Matches(td.Children[1]) && CssParser.ParseSelector("p:last-of-type", null)!.Matches(td.Children[3]), ":only-of-type and :last-of-type");
    Check(CssParser.ParseSelector(":is(p, span)", null)!.Matches(td.Children[1]) && !CssParser.ParseSelector(":not(p, span)", null)!.Matches(td.Children[1]), ":is() and :not() with lists");
    Check(CssParser.ParseSelector("div:has(> span)", null)!.Matches(td) && !CssParser.ParseSelector("div:has(> b)", null)!.Matches(td), ":has(> x) tests the children");
    Check(CssParser.ParseSelector("i:empty", null)!.Matches(td.Children[4]) && !CssParser.ParseSelector("p:empty", null)!.Matches(td.Children[0]), ":empty");
    CssParser.FontFaces.Clear();
    CssParser.ParseStylesheet("@font-face { font-family: 'Sig'; src: url(fonts/Barlow-Black.ttf) format('truetype'); font-weight: 900 }", warnings.Add);
    Check(CssParser.FontFaces.Count == 1 && CssParser.FontFaces[0].family == "Sig" && CssParser.FontFaces[0].src.EndsWith("Barlow-Black.ttf") && CssParser.FontFaces[0].weight == "900", "@font-face collected");
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

// Batch F2: at-rules, form pseudo-classes, pseudo-elements
{
    var warnings = new List<string>();
    CssParser.Imports.Clear();
    CssParser.PropertyInitials.Clear();
    var rules = CssParser.ParseStylesheet(
        "@import url(https://x/a.css) screen; @import \"https://x/b.css\";" +
        "@layer base { p { color: red } } @layer { q { color: blue } }" +
        "@scope (.card) { img { width: 1px } :scope { padding: 1px } }" +
        "@container side (min-width: 100px) { .in { color: green } } @container (min-width: 99999px) { .out { color: green } }" +
        "@property --gap { syntax: '<length>'; inherits: false; initial-value: 7px; }" +
        "input:required { a: 1 } input:invalid { a: 2 } input:in-range { a: 3 } input::placeholder { color: gray } li::marker { color: red } a:any-link { a: 4 } p:lang(en) { a: 5 } div:dir(rtl) { a: 6 }",
        m => warnings.Add(m), new Dictionary<string, CssKeyframes>());
    Check(CssParser.Imports.Count == 2 && CssParser.Imports[0] == "https://x/a.css" && CssParser.Imports[1] == "https://x/b.css", "@import urls collected (url() and quoted forms)");
    Check(rules.Exists(r => r.Selectors[0].Matches(HtmlParser.Parse("<p></p>").Children[0])) && rules.Exists(r => r.Selectors[0].Matches(HtmlParser.Parse("<q></q>").Children[0])), "@layer blocks parsed, named and anonymous");
    var card = HtmlParser.Parse("<div class=card><img></div>").Children[0];
    Check(rules.Exists(r => r.Selectors[0].Matches(card.Children[0])) && rules.Exists(r => r.Selectors[0].Matches(card) && r.Declarations[0].Name == "padding"), "@scope prefixes its rules with the root; :scope is the root");
    Check(rules.Exists(r => r.Declarations[0].Value.Trim() == "green" && r.Selectors[0].Matches(HtmlParser.Parse("<div class=in></div>").Children[0])) && !rules.Exists(r => r.Selectors[0].Matches(HtmlParser.Parse("<div class=out></div>").Children[0])), "@container decided like @media");
    Check(CssParser.PropertyInitials.TryGetValue("--gap", out var init) && init == "7px", "@property initial-value recorded");
    var form = HtmlParser.Parse("<input id=r required><input id=v type=number min=1 max=5 value=3><input id=o type=number min=1 max=5 value=9><a id=l href=x></a><p id=p lang=en-GB></p><div dir=rtl><div id=d></div></div>");
    HtmlNode ById(HtmlNode n, string id) { if (n.Attr("id") == id) return n; foreach (var c in n.Children) { var f = ById(c, id); if (f != null) return f; } return null!; }
    CssSelector Sel(string s) => CssParser.ParseSelector(s, null)!;
    Check(Sel("input:required").Matches(ById(form, "r")) && !Sel("input:required").Matches(ById(form, "v")), ":required from the attribute");
    Check(Sel("input:invalid").Matches(ById(form, "r")) && !Sel("input:invalid").Matches(ById(form, "v")) && Sel("input:invalid").Matches(ById(form, "o")), ":invalid for an empty required field and an out-of-range number");
    Check(Sel("input:in-range").Matches(ById(form, "v")) && Sel("input:out-of-range").Matches(ById(form, "o")) && !Sel("input:in-range").Matches(ById(form, "r")), ":in-range/:out-of-range from min/max");
    Check(Sel("a:any-link").Matches(ById(form, "l")) && Sel("p:lang(en)").Matches(ById(form, "p")) && Sel("div:dir(rtl)").Matches(ById(form, "d")) && !Sel("div:dir(ltr)").Matches(ById(form, "d")), ":any-link, :lang() prefix, :dir() inherited");
    var marker = HtmlParser.Parse("<li><span data-marker=disc></span>text</li>").Children[0].Children[0];
    Check(Sel("li::marker").Matches(marker) && !Sel("li span").Matches(marker), "::marker matches the marker span and ordinary selectors do not");
    var field = HtmlParser.Parse("<input>").Children[0];
    var ph = new HtmlNode { Tag = "span", Parent = field };
    ph.Attributes["data-pseudo"] = "placeholder";
    Check(Sel("input::placeholder").Matches(ph) && Sel("input::-webkit-input-placeholder").Matches(ph), "::placeholder (and the vendor spelling) matches the field's placeholder pseudo-element");
    Check(warnings.Count == 0, $"no warnings for the F2 sheet (got {string.Join("; ", warnings)})");
}

// Batch F4: @counter-style systems, the rescued pseudo-elements
{
    CssParser.CounterStyles.Clear();
    var warnings = new List<string>();
    CssParser.ParseStylesheet(
        "@counter-style stars { system: cyclic; symbols: \"*\" \"+\"; suffix: \" \"; }" +
        "@counter-style abc { system: alphabetic; symbols: a b c; }" +
        "@counter-style bin { system: numeric; symbols: \"0\" \"1\"; }" +
        "@counter-style dots { system: symbolic; symbols: \"•\"; }" +
        "p::first-letter { a: 1 } p::first-line { a: 2 } div::-webkit-scrollbar { width: 6px } div::-webkit-scrollbar-thumb { a: 3 }",
        m => warnings.Add(m), new Dictionary<string, CssKeyframes>());
    var stars = CssParser.CounterStyles["stars"];
    Check(stars.Text(1) == "*" && stars.Text(2) == "+" && stars.Text(3) == "*" && stars.Suffix == " ", "@counter-style cyclic wraps its symbols");
    Check(CssParser.CounterStyles["abc"].Text(4) == "aa" && CssParser.CounterStyles["abc"].Text(3) == "c", "@counter-style alphabetic counts like spreadsheet columns");
    Check(CssParser.CounterStyles["bin"].Text(5) == "101" && CssParser.CounterStyles["bin"].Text(0) == "0", "@counter-style numeric is a positional system");
    Check(CssParser.CounterStyles["dots"].Text(3) == "•••", "@counter-style symbolic repeats");
    var p = HtmlParser.Parse("<p></p>").Children[0];
    var fl = new HtmlNode { Tag = "span", Parent = p }; fl.Attributes["data-pseudo"] = "first-letter";
    var sb = new HtmlNode { Tag = "span", Parent = HtmlParser.Parse("<div></div>").Children[0] }; sb.Attributes["data-pseudo"] = "-webkit-scrollbar";
    Check(CssParser.ParseSelector("p::first-letter", null)!.Matches(fl) && !CssParser.ParseSelector("p::first-line", null)!.Matches(fl), "::first-letter and ::first-line are distinct pseudo-elements");
    Check(CssParser.ParseSelector("div::-webkit-scrollbar", null)!.Matches(sb), "::-webkit-scrollbar parses and matches");
    Check(warnings.Count == 0, $"no warnings for the F4 sheet (got {string.Join("; ", warnings)})");
}

if (args.Length > 0 && args[0] == "--probe2") { Probe2.Run(); return 0; }
Console.WriteLine(failures.Count == 0 ? "ALL PASS" : $"{failures.Count} FAILED");
return failures.Count == 0 ? 0 : 1;

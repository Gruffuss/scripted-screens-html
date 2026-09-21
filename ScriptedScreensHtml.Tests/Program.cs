using System.Linq;
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
    TestBatchG();
}

void TestBatchG()
{
    var warnings = new List<string>();
    CssParser.StartingRules.Clear();
    var rules = CssParser.ParseStylesheet(
        "@starting-style { .card { opacity: 0 } } .pill { opacity: 1; @starting-style { transform: scale(.5) } } " +
        "input:user-invalid { a: 1 } dialog::backdrop { background: #0008 } .x { width: calc(sin(30deg) * 100px) }",
        m => warnings.Add(m), new Dictionary<string, CssKeyframes>());
    Check(rules.Count == 4, $"starting-style blocks are not ordinary rules ({rules.Count})");
    Check(CssParser.StartingRules.Count == 2 && CssParser.StartingRules[1].Declarations[0].Name == "transform", "@starting-style: top-level and nested blocks collected");
    var inp = HtmlParser.Parse("<input required>").Children[0];
    var ui = CssParser.ParseSelector("input:user-invalid", null)!;
    Check(!ui.Matches(inp), ":user-invalid: untouched field never matches");
    inp.Attributes["data-touched"] = "";
    Check(ui.Matches(inp), ":user-invalid: touched empty required field matches");
    var bd = new HtmlNode { Tag = "span", Parent = HtmlParser.Parse("<dialog></dialog>").Children[0] }; bd.Attributes["data-pseudo"] = "backdrop";
    Check(CssParser.ParseSelector("dialog::backdrop", null)!.Matches(bd), "::backdrop is a pseudo-element");
    Check(warnings.Count == 0, $"no warnings for the G sheet (got {string.Join("; ", warnings)})");
    TestBuildRows();
}

void TestBuildRows()
{
    var warnings = new List<string>();
    var rules = CssParser.ParseStylesheet(":scope { a: 1 } [popover]:popover-open { a: 2 } details::details-content { a: 3 }", m => warnings.Add(m), new Dictionary<string, CssKeyframes>());
    Check(rules.Count == 3 && warnings.Count == 0, $"scope, popover-open and details-content parse ({rules.Count}, {string.Join("; ", warnings)})");
    var body = HtmlParser.Parse("<body><div popover id=p></div></body>").Children[0];
    Check(CssParser.ParseSelector(":scope", null)!.Matches(body), ":scope alone is the root");
    var pop = body.Children[0];
    Check(!CssParser.ParseSelector(":popover-open", null)!.Matches(pop), ":popover-open: closed popover does not match");
    pop.Attributes["data-popover-open"] = "";
    Check(CssParser.ParseSelector(":popover-open", null)!.Matches(pop), ":popover-open: shown popover matches");
    var dc = new HtmlNode { Tag = "div", Parent = HtmlParser.Parse("<details></details>").Children[0] }; dc.Attributes["data-pseudo"] = "details-content";
    Check(CssParser.ParseSelector("details::details-content", null)!.Matches(dc), "::details-content is a pseudo-element");
    var area = HtmlParser.Parse("<map name=m><area shape=rect coords=\"0,0,10,10\" id=a1><area shape=circle coords=\"5,5,3\" id=a2></map>").Children[0];
    Check(area.Children.Count == 2 && area.Children[1].Tag == "area", "area is a void tag: two siblings under the map");
    TestSceneSlots();
    TestTextMeasure();
}

void TestTextMeasure()
{
    // a face at point size 100: letters advance 50, space 25, hyphen 30; A then V kerns by -10
    var face = new FaceData { Name = "Test", PointSize = 100f, Scale = 1f, LineHeight = 120f, BoldSpacing = 10f };
    uint g = 1;
    foreach (var ch in "abcdefghijklmnopqrstuvwxyzAV<>") face.Chars[ch] = new FaceData.Glyph { Index = g++, Advance = 50f, Scale = 1f };
    face.Chars[' '] = new FaceData.Glyph { Index = g++, Advance = 25f, Scale = 1f };
    face.Chars['-'] = new FaceData.Glyph { Index = g++, Advance = 30f, Scale = 1f };
    face.Chars[0x200B] = new FaceData.Glyph { Index = g++, Advance = 0f, Scale = 1f };
    face.Pairs[(face.Chars['V'].Index << 16) | face.Chars['A'].Index] = (-10f, 0f, false);
    var big = new FaceData { Name = "Big", PointSize = 50f, Scale = 1f, LineHeight = 60f };
    big.Chars['a'] = new FaceData.Glyph { Index = 1, Advance = 50f, Scale = 1f };
    TextMeasure.Register(big);
    var style = new TextMeasure.Style { Size = 20f, Rich = true };   // element scale 0.2: a letter is 10 px, a space 5
    var wrap = style; wrap.Wrap = true;
    TextMeasure.Result M(string text, in TextMeasure.Style s, float width = 10000f) => TextMeasure.Measure(text, face, s, width);
    bool Near(float a, float b) => Math.Abs(a - b) < 0.02f;

    var r = M("abc", style);
    Check(Near(r.Width, 30f) && r.Lines == 1 && Near(r.LineHeight, 24f), $"text: three letters are 30 px on one line ({r.Width}, {r.Lines}, {r.LineHeight})");
    r = M("aaa aaa", wrap, 50f);
    Check(r.Lines == 2 && Near(r.Width, 30f), $"text: wraps at the space, the space adds no width ({r.Width}, {r.Lines})");
    r = M("aaa aaa", style, 50f);
    Check(r.Lines == 1 && Near(r.Width, 65f), $"text: without wrapping it stays one line ({r.Width}, {r.Lines})");
    r = M("aaaaaaa", wrap, 30f);
    Check(r.Lines == 3 && Near(r.Width, 30f), $"text: a word with no break point breaks before the letter that overflows ({r.Width}, {r.Lines})");
    r = M("aa-aaa", wrap, 40f);
    Check(r.Lines == 2 && Near(r.Width, 30f), $"text: breaks after a hyphen, which stays on the first line ({r.Width}, {r.Lines})");
    r = M("aa​aaa", wrap, 40f);
    Check(r.Lines == 2 && Near(r.Width, 30f), $"text: breaks at a zero-width space ({r.Width}, {r.Lines})");
    r = M("aa aaa", wrap, 40f);
    Check(r.Lines == 2 && Near(r.Width, 35f), $"text: a no-break space is not a break point: the word breaks at the overflowing letter ({r.Width}, {r.Lines})");
    r = M("<b>ab</b>", style);
    Check(Near(r.Width, 22f), $"text: bold adds the face's bold spacing per letter ({r.Width})");
    r = M("ab", new TextMeasure.Style { Size = 20f, Bold = true, Rich = true });
    Check(Near(r.Width, 22f), $"text: a bold style counts like a b tag ({r.Width})");
    r = M("<size=40>a</size>a", style);
    Check(Near(r.Width, 30f), $"text: a size tag scales its letters ({r.Width})");
    r = M("<size=50%>a</size>a", style);
    Check(Near(r.Width, 15f), $"text: a percentage size is of the base size ({r.Width})");
    r = M("aa", new TextMeasure.Style { Size = 20f, LetterSpacing = 2f, Rich = true });
    Check(Near(r.Width, 22f), $"text: letter spacing is added after each letter ({r.Width})");
    r = M("a a", new TextMeasure.Style { Size = 20f, WordSpacing = 4f, Rich = true });
    Check(Near(r.Width, 29f), $"text: word spacing is added after each space ({r.Width})");
    r = M("AV", style);
    Check(Near(r.Width, 18f), $"text: a kerning pair moves the second letter ({r.Width})");
    r = M("aa\naaa", style);
    Check(r.Lines == 2 && Near(r.Width, 30f), $"text: a newline starts a line; width is the widest ({r.Width}, {r.Lines})");
    r = M("a<br>aaa", style);
    Check(r.Lines == 2 && Near(r.Width, 30f), $"text: br is a newline ({r.Width}, {r.Lines})");
    r = M("<mspace=0.5em>ab</mspace>", style);
    Check(Near(r.Width, 20f), $"text: mspace makes each letter a cell of the given width ({r.Width})");
    r = M("<font=\"Big\">a</font>a", style);
    Check(Near(r.Width, 30f), $"text: a font tag measures with that face's point size ({r.Width})");
    r = M("<noparse><b></noparse>", style);
    Check(Near(r.Width, 30f), $"text: noparse draws tags as text ({r.Width})");
    r = M("<color=#fff><u>ab</u></color>", style);
    Check(Near(r.Width, 20f), $"text: colour and underline tags take no width ({r.Width})");
    r = M("<zzz>", style);
    Check(Near(r.Width, 50f), $"text: an unknown tag is text ({r.Width})");
    face.Chars['2'] = new FaceData.Glyph { Index = g++, Advance = 50f, Scale = 1f };
    r = M("a₂", style);
    Check(Near(r.Width, 15f), $"text: a subscript the face lacks is its own digit at subscript size ({r.Width})");
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var k = 0; k < 1000; k++) M("<b>Room</b> pressure <size=80%>kPa</size> and more words", wrap, 60f);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check(allocated < 1000, $"text: measuring allocates nothing once warm ({allocated} bytes for 1000 calls)");
}

void TestSceneSlots()
{
    // Two emits of the same page with different values: same template, only values differ;
    // and filling the slots back in gives the scene that was emitted.
    const string a = "SCENE w=640 h=640\nDEFS {\n  CP id=clip1 { R x=0 y=0 w=10 h=10 }\n}\nR x=20 y=79.5 w=152 h=46 rx=23 f=#FFFFFF sh=[[0,3,8,0,#0000001F]] id=tab1 click=1\nG clip=clip1 o==1+(-0.75)*smoothstep(0,1,clamp((t--0.023)/0.55,0,1)) {\n  T x=-10.6 y=79 w=213.2 h=46 text=\"say \\\"hi\\\"\\nO<sub>2</sub> 21.0\" size=18 f=#000000 font=\"Manrope SemiBold\" align=center lh=1.33\n}\nG a=[0,90] t=[-140.9,0] {\n  R x=0 y=0 w=4 h=4 f=#FFFFFF\n}\n";
    const string b = "SCENE w=640 h=640\nDEFS {\n  CP id=clip1 { R x=0 y=0 w=10 h=10 }\n}\nR x=20 y=79.5 w=160 h=46 rx=23 f=#EEF4FF sh=[[0,3,8,0,#0000001F]] id=tab1 click=1\nG clip=clip1 o==1+(-0.25)*smoothstep(0,1,clamp((t-1.5)/0.55,0,1)) {\n  T x=-10.6 y=79 w=213.2 h=46 text=\"22.4 %\" size=18 f=#000000 font=\"Manrope SemiBold\" align=center lh=1.33\n}\nG a=[0,90] t=[-144.7,0] {\n  R x=0 y=0 w=4 h=4 f=#FFFFFF\n}\n";
    var va = new Dictionary<string, SceneSlots.Value>();
    var vb = new Dictionary<string, SceneSlots.Value>();
    var ta = SceneSlots.Split(a, va);
    var tb = SceneSlots.Split(b, vb);
    Check(ta == tb, $"slots: same structure, same template\n{ta}\n{tb}");
    Check(ta.Contains("CP id=clip1 { R x=0 y=0 w=10 h=10 }") && ta.StartsWith("SCENE w=640 h=640"), "slots: SCENE and DEFS stay literal");
    Check(ta.Contains("sh=[[0,3,8,0,#0000001F]]") && ta.Contains("id=tab1") && ta.Contains("font=\"Manrope SemiBold\"") && ta.Contains("lh=1.33"), "slots: arrays, ids, fonts and number-only keys stay literal");
    // A line carrying the author's id names its slots after it, so Lua can write to the compiled
    // scene by the name in the markup; a line with no id keeps the positional name.
    Check(va["tab1_w"].Number == 152f && vb["tab1_w"].Number == 160f && va["tab1_f"].Text == "#FFFFFF", "slots: numbers and colours are values, named by the element's own id");
    Check(!va.ContainsKey("L4_w") && ta.Contains("w=$tab1_w"), $"slots: an author id replaces the positional name\n{ta}");
    Check(va["L8_t_0"].Number == -140.9f && vb["L8_t_0"].Number == -144.7f && ta.Contains("t=[$L8_t_0,$L8_t_1]"), $"slots: a moved group is values, not structure\n{ta}");
    Check(va["L6_text"].Text == "say \"hi\"\nO<sub>2</sub> 21.0", $"slots: text is unescaped ({va["L6_text"].Text})");
    Check(ta.Contains("(t-$L5_o_") && ta.Contains("+($L5_o_") && ta.Contains("*smoothstep($L5_o_") && ta.Contains(",clamp(("), $"slots: numbers in expressions become slots, signs included, names do not\n{ta}");
    // fill the template back in and compare with the original
    string Fill(string t, Dictionary<string, SceneSlots.Value> v)
    {
        foreach (var kv in v.OrderByDescending(k => k.Key.Length))
        {
            var repl = kv.Value.IsNumber ? kv.Value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) : kv.Value.Text!;
            t = t.Replace("\"$" + kv.Key + "\"", "\"" + repl.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"").Replace("$" + kv.Key, repl);
        }
        return t;
    }
    Check(Fill(ta, va) == a, $"slots: template plus values is the emitted scene\n{Fill(ta, va)}");
}

Console.WriteLine("JsNumber");
{
    // What a browser prints for each of these; the page script must not see anything else. The
    // awkward ones are the exact midpoints (JS rounds them away from zero, .NET's "F" to even)
    // and the values that only look like midpoints once multiplied (1.45 is 1.44999999999999995559).
    var cases = new (double v, int d, string want)[]
    {
        (1.005, 2, "1.00"), (2.5, 0, "3"), (-1.5, 0, "-2"), (0.5, 0, "1"), (1.45, 1, "1.4"),
        (-0.0001, 2, "-0.00"), (123.456, 2, "123.46"), (0.1, 5, "0.10000"), (9.995, 2, "9.99"),
        (0.125, 2, "0.13"), (-0.125, 2, "-0.13"), (99.5, 0, "100"), (9.95, 1, "9.9"),
        (0, 2, "0.00"), (1e21, 2, "1e+21"), (double.NaN, 2, "NaN"),
        (double.PositiveInfinity, 1, "Infinity"), (double.NegativeInfinity, 1, "-Infinity"),
    };
    foreach (var (v, d, want) in cases)
    {
        var got = JsNumber.ToFixed(v, d);
        Check(got == want, $"toFixed: ({v}).toFixed({d}) is \"{want}\"" + (got == want ? string.Empty : $" but was \"{got}\""));
    }
}

if (args.Length > 0 && args[0] == "--probe2") { Probe2.Run(); return 0; }
if (args.Length > 0 && args[0] == "--probe3") { Probe3.Run(); return 0; }
Console.WriteLine(failures.Count == 0 ? "ALL PASS" : $"{failures.Count} FAILED");
return failures.Count == 0 ? 0 : 1;

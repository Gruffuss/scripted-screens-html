using System;
using ScriptedScreensHtml;
internal static class Probe3
{
    public static void Run()
    {
        var rules = CssParser.ParseStylesheet("svg .big { r: 12; } svg .rot { transform: rotate(20deg); transform-box: fill-box; }", Console.WriteLine, new System.Collections.Generic.Dictionary<string, CssKeyframes>());
        foreach (var r in rules) foreach (var d in r.Declarations) Console.WriteLine(d.Name + " = " + d.Value);
        var doc = HtmlParser.Parse("<svg><circle class=\"big\" cx=\"140\" cy=\"48\" r=\"3\"/></svg>");
        var circle = doc.Children[0].Children[0];
        foreach (var r in rules) foreach (var sel in r.Selectors) Console.WriteLine(sel.Matches(circle) ? "matches" : "no match");
    }
}

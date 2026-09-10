using System;
using System.Collections.Generic;
using ScriptedScreensHtml;
public static class Probe
{
    public static void Run()
    {
        var css = System.IO.File.ReadAllText("gascss.txt");
        var kfs = new Dictionary<string, CssKeyframes>();
        var warnings = new List<string>();
        var t0 = DateTime.Now;
        var rules = CssParser.ParseStylesheet(css, warnings.Add, kfs);
        Console.WriteLine($"css: {rules.Count} rules, {kfs.Count} keyframes, {warnings.Count} warnings in {(DateTime.Now-t0).TotalMilliseconds} ms");
        foreach (var w in warnings) Console.WriteLine("  " + w);
        t0 = DateTime.Now;
        var doc = HtmlParser.Parse(System.IO.File.ReadAllText("gaspage.html"), warnings.Add);
        Console.WriteLine($"html parsed in {(DateTime.Now-t0).TotalMilliseconds} ms, top {doc.Children.Count}");
        var decl = CssParser.ParseDeclarations("left:12%; top:40%; background:rgba(1,2,3,0.55); animation: m3 4.1s ease-in-out infinite; animation-delay: -2.3s");
        Console.WriteLine($"inline decls {decl.Count}");
    }
}

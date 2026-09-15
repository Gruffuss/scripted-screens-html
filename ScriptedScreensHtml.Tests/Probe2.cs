using System;
using ScriptedScreensHtml;
internal static class Probe2
{
    public static void Run()
    {
        var doc = HtmlParser.Parse("<footer><span><span class=\"led\"></span>network ok</span><span>second</span></footer>", _ => { });
        void Dump(HtmlNode n, int d)
        {
            Console.WriteLine(new string(' ', d * 2) + (n.IsText ? "#text '" + n.Text + "'" : "<" + n.Tag + "> class=" + (n.Attr("class") ?? "-") + " children=" + n.Children.Count));
            foreach (var c in n.Children) Dump(c, d + 1);
        }
        Dump(doc, 0);
    }
}

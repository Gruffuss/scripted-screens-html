using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// HTML source to a VisualElement tree. Block elements become VisualElements, and an
/// element whose children are only text and inline tags becomes a Label with rich text,
/// because UI Toolkit has no inline flow. Styles apply in cascade order: tag defaults,
/// stylesheet rules by specificity then source order, inline style attribute.
/// </summary>
internal static class HtmlRenderer
{
    private static readonly HashSet<string> Inline = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "strong", "i", "em", "u", "s", "span", "br", "small", "big", "font", "code", "sub", "sup", "mark", "a",
        "abbr", "cite", "q", "kbd", "samp", "var", "time", "dfn", "del", "ins", "bdi", "wbr", "data", "strike", "tt",
    };

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "title", "meta", "link", "script", "style", "template",
    };

    public sealed class Result
    {
        public VisualElement Root = new();
        public readonly List<string> Warnings = new();
        public readonly Dictionary<string, VisualElement> ById = new(StringComparer.Ordinal);
        public readonly Dictionary<string, CssKeyframes> Keyframes = new(StringComparer.Ordinal);
        /// <summary>SVG shapes by id, for data binding of attributes such as points.</summary>
        public readonly Dictionary<string, SvgShape> Shapes = new(StringComparer.Ordinal);
        public readonly List<(VisualElement element, AnimationSpec spec)> Animations = new();
        public string Script = string.Empty;
        /// <summary>&lt;script src&gt; urls in document order and &lt;link rel=stylesheet href&gt; urls; the surface fetches them.</summary>
        public readonly List<string> ExternalScripts = new();
        public readonly List<string> ExternalStyles = new();
        /// <summary>Design width from meta viewport, or 0 to use the element's own width.</summary>
        public float ViewportWidth;
        /// <summary>Stylesheet rules and the node each element came from, for className changes at runtime.</summary>
        public List<CssRule> Rules = new();
        public readonly Dictionary<VisualElement, HtmlNode> NodeOf = new();
        public HtmlNode Document = new();
        /// <summary>display: grid containers, laid out by GridLayout once attached.</summary>
        public readonly List<VisualElement> Grids = new();
        /// <summary>img / video / audio boxes: drawn by ScriptedScreens' own image, media and sound elements.</summary>
        public readonly Dictionary<VisualElement, HtmlNode> Externals = new();
        /// <summary>Computed font size per node, for em units on its children.</summary>
        public readonly Dictionary<HtmlNode, float> FontSizes = new();

        /// <summary>The declarations that won the cascade per element, for the vector emitter (gradients, transforms, fonts).</summary>
        private readonly Dictionary<VisualElement, Dictionary<string, string>> _css = new();

        public Dictionary<string, string> CssOf(VisualElement ve)
        {
            if (!_css.TryGetValue(ve, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                _css[ve] = map;
            }
            return map;
        }

        /// <summary>Ids of elements matching a simple selector (tag, .class, #id, descendant, child).</summary>
        public List<string> Query(string selector)
        {
            var ids = new List<string>();
            var sel = CssParser.ParseSelector(selector.Trim(), null);
            if (sel == null)
                return ids;
            foreach (var kv in NodeOf)
            {
                if (sel.Matches(kv.Value))
                {
                    var id = kv.Key.name;
                    if (!string.IsNullOrEmpty(id) && ById.ContainsKey(id))
                        ids.Add(id);
                }
            }
            return ids;
        }

        /// <summary>Re-run the cascade for an element after its class attribute changed.</summary>
        public void Reclass(VisualElement ve, string classes)
        {
            if (!NodeOf.TryGetValue(ve, out var node))
                return;
            node.Attributes["class"] = classes;
            ve.ClearClassList();
            foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                ve.AddToClassList(c);
            ApplyStyles(ve, node, Rules, this);
        }
    }

    private static int _autoId;

    public static Result Build(string source, Font? font)
    {
        var result = new Result();
        void Warn(string m) => result.Warnings.Add(m);

        var doc = HtmlParser.Parse(source, Warn);
        result.Document = doc;

        var rules = new List<CssRule>();
        var script = new StringBuilder();
        result.ViewportWidth = ReadViewport(doc);
        CssParser.ViewportWidth = result.ViewportWidth > 0f ? result.ViewportWidth : 460f;
        CssParser.ViewportHeight = CssParser.ViewportWidth * SurfaceAspect;
        CssParser.FontFaces.Clear();
        Collect(doc, rules, script, result.Keyframes, Warn, result);
        foreach (var (family, src, weight, style) in CssParser.FontFaces)
            FontLibrary.Alias(family, src, weight, style);
        result.Script = script.ToString();
        rules.Sort((a, b) => a.Order.CompareTo(b.Order));
        result.Rules = rules;

        var body = Find(doc, "body") ?? Find(doc, "html") ?? doc;
        StyleApplier.ViewportW = result.ViewportWidth > 0f ? result.ViewportWidth : 460f;
        StyleApplier.ViewportH = StyleApplier.ViewportW * SurfaceAspect;

        var root = new VisualElement { name = "body" };
        root.style.flexGrow = 1;
        root.style.color = Color.white;
        root.style.whiteSpace = WhiteSpace.Normal;
        if (font != null)
            root.style.unityFont = font;
        result.Root = root;

        result.NodeOf[root] = body;
        result.ById["body"] = root;
        ApplyStyles(root, body, rules, result);
        foreach (var child in body.Children)
            Append(root, child, rules, result);
        ApplyGap(root, result.CssOf(root));

        return result;
    }

    /// <summary>Height over width of the surface being built, for vh; set by the surface before Build.</summary>
    internal static float SurfaceAspect = 1f;

    private static void Collect(HtmlNode node, List<CssRule> rules, StringBuilder script, Dictionary<string, CssKeyframes> keyframes, Action<string> warn, Result? result = null)
    {
        if (node.Tag == "style")
        {
            foreach (var c in node.Children)
                rules.AddRange(CssParser.ParseStylesheet(c.Text, warn, keyframes));
            return;
        }
        if (node.Tag == "link" && string.Equals(node.Attr("rel"), "stylesheet", StringComparison.OrdinalIgnoreCase) && node.Attr("href") is { } href)
        {
            result?.ExternalStyles.Add(href);
            return;
        }
        if (node.Tag == "script")
        {
            var type = (node.Attr("type") ?? string.Empty).Trim().ToLowerInvariant();
            if (type.Length > 0 && type != "module" && !type.Contains("javascript") && type != "text/ecmascript")
                return; // JSON, templates, importmaps: not code
            if (node.Attr("src") is { } src)
            {
                result?.ExternalScripts.Add(src);
                return;
            }
            foreach (var c in node.Children)
                script.Append(type == "module" ? StripModuleSyntax(c.Text) : c.Text).Append('\n');
            return;
        }
        foreach (var c in node.Children)
            Collect(c, rules, script, keyframes, warn, result);
    }

    /// <summary>
    /// A module script runs as a classic one: import lines go (there is no module graph to
    /// resolve, so they would only throw) and `export` is dropped from declarations.
    /// ponytail: real modules need a loader; pages that import from a URL will not work.
    /// </summary>
    internal static string StripModuleSyntax(string js)
    {
        var lines = js.Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            var t = line.TrimStart();
            if (t.StartsWith("import ", StringComparison.Ordinal) && t.Contains(" from ")) continue;
            if (t.StartsWith("import '", StringComparison.Ordinal) || t.StartsWith("import \"", StringComparison.Ordinal)) continue;
            if (t.StartsWith("export default ", StringComparison.Ordinal)) { sb.Append(line.Replace("export default ", "var __default = ")).Append('\n'); continue; }
            if (t.StartsWith("export ", StringComparison.Ordinal)) { sb.Append(line.Replace("export ", string.Empty)).Append('\n'); continue; }
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// The page's design width: <c>&lt;meta name="viewport" content="width=768"&gt;</c>.
    /// The surface lays the page out at that width and maps it onto the element, so a
    /// page designed at 768 draws on a 460 console without a CSS transform (which
    /// rasterises text at layout size and then scales it, and reads soft).
    /// </summary>
    private static float ReadViewport(HtmlNode node)
    {
        foreach (var c in node.Children)
        {
            if (c.Tag == "meta" && string.Equals(c.Attr("name"), "viewport", StringComparison.OrdinalIgnoreCase))
            {
                var content = c.Attr("content") ?? string.Empty;
                foreach (var part in content.Split(','))
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2 && kv[0].Trim().Equals("width", StringComparison.OrdinalIgnoreCase)
                        && float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) && w > 0f)
                        return w;
                }
            }
            var deeper = ReadViewport(c);
            if (deeper > 0f)
                return deeper;
        }
        return 0f;
    }

    private static HtmlNode? Find(HtmlNode node, string tag)
    {
        foreach (var c in node.Children)
        {
            if (string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase))
                return c;
            var deeper = Find(c, tag);
            if (deeper != null)
                return deeper;
        }
        return null;
    }

    /// <summary>Script-created elements: parse the HTML and append it under a live element, cascaded like the page.</summary>
    internal static void AppendFragment(VisualElement parent, HtmlNode parentNode, string html, Result result)
    {
        var frag = HtmlParser.Parse(html, m => result.Warnings.Add(m));
        foreach (var child in frag.Children)
        {
            child.Parent = parentNode;
            parentNode.Children.Add(child);
            Append(parent, child, result.Rules, result);
        }
    }

    /// <summary>
    /// Script insertBefore: the fragment's nodes go before `beforeId` in both trees. With no
    /// such child it appends. Text nodes are labels without ids, so only elements move.
    /// </summary>
    internal static void InsertFragment(VisualElement parent, HtmlNode parentNode, string html, string beforeId, Result result)
    {
        result.ById.TryGetValue(beforeId, out var beforeVe);
        HtmlNode? beforeNode = null;
        if (beforeVe != null) result.NodeOf.TryGetValue(beforeVe, out beforeNode);
        if (beforeVe == null || beforeNode == null || beforeVe.parent != parent)
        {
            AppendFragment(parent, parentNode, html, result);
            return;
        }
        var frag = HtmlParser.Parse(html, m => result.Warnings.Add(m));
        foreach (var child in frag.Children)
        {
            child.Parent = parentNode;
            var at = parentNode.Children.IndexOf(beforeNode);
            parentNode.Children.Insert(at < 0 ? parentNode.Children.Count : at, child);
            Append(parent, child, result.Rules, result);
            var id = child.Attr("id");
            if (id != null && result.ById.TryGetValue(id, out var made) && made.parent == parent)
                made.PlaceBehind(beforeVe);
        }
    }

    /// <summary>The node's markup back as HTML: its children (inner) or itself with them (outer). Generated content, markers and synthetic ids are left out.</summary>
    internal static string ToHtml(HtmlNode node, bool outer)
    {
        var sb = new StringBuilder();
        if (outer) WriteNode(sb, node); else foreach (var c in node.Children) WriteNode(sb, c);
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, HtmlNode node)
    {
        if (node.IsText) { sb.Append(EscapeHtml(node.Text)); return; }
        if (node.Attr("data-pseudo") != null || node.Attr("data-marker") != null) return;
        sb.Append('<').Append(node.Tag);
        foreach (var kv in node.Attributes)
        {
            if (kv.Key == "data-listed" || kv.Key == "data-control") continue;
            if (kv.Key == "id" && kv.Value.StartsWith("__", StringComparison.Ordinal)) continue;
            sb.Append(' ').Append(kv.Key);
            if (kv.Value.Length > 0) sb.Append("=\"").Append(EscapeHtml(kv.Value)).Append('"');
        }
        sb.Append('>');
        if (HtmlParser.Void.Contains(node.Tag!)) return;
        foreach (var c in node.Children) WriteNode(sb, c);
        sb.Append("</").Append(node.Tag).Append('>');
    }

    private static string EscapeHtml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>Script removal: the element, its node, and every id under it.</summary>
    internal static void Remove(VisualElement ve, Result result)
    {
        if (result.NodeOf.TryGetValue(ve, out var node))
        {
            node.Parent?.Children.Remove(node);
            result.NodeOf.Remove(ve);
        }
        Forget(ve, result);
        ve.RemoveFromHierarchy();
    }

    private static void Forget(VisualElement ve, Result result)
    {
        if (!string.IsNullOrEmpty(ve.name))
            result.ById.Remove(ve.name);
        result.Externals.Remove(ve);
        foreach (var child in ve.Children())
            Forget(child, result);
    }

    private static void Append(VisualElement parent, HtmlNode node, List<CssRule> rules, Result result)
    {
        if (node.IsText)
        {
            if (node.Text.Trim().Length == 0)
                return;
            var label = new Label(node.Text.Trim());
            parent.Add(label);
            return;
        }

        if (Skipped.Contains(node.Tag!))
            return;

        AddGenerated(node, rules);

        if (node.Tag == "table")
        {
            AppendTable(parent, node, rules, result);
            return;
        }

        if (node.Tag is "progress" or "meter")
        {
            // Drawn by the emitter from the attributes: a track and a fill.
            node.Attributes["data-control"] = node.Tag;
            var bar = new VisualElement();
            bar.style.width = 160;
            bar.style.height = node.Tag == "progress" ? 10 : 12;
            Register(bar, node, result);
            ApplyStyles(bar, node, rules, result);
            parent.Add(bar);
            return;
        }

        if (node.Tag == "summary")
            node.Attributes["data-click"] = "1";
        if (node.Tag == "label" && node.Attr("for") != null)
            node.Attributes["data-click"] = "1";

        if (node.Tag == "svg")
        {
            var svg = BuildSvg(node, result);
            Register(svg, node, result);
            ApplyStyles(svg, node, rules, result);
            parent.Add(svg);
            return;
        }

        if (node.Tag == "input")
        {
            var kind = (node.Attr("type") ?? "text").ToLowerInvariant();
            if (kind is "button" or "submit" or "reset")
            {
                // A push button is the same click region as <button>.
                node.Tag = "button";
                node.Children.Add(new HtmlNode { Text = node.Attr("value") ?? "Submit", Parent = node });
            }
            else if (kind == "hidden")
                return;
            else if (kind is "checkbox" or "radio")
            {
                // Drawn by the page: a box (or ring) the size CSS says, a tick (or dot) when
                // checked, and a click region. State lives on the node as the `checked` attribute.
                node.Attributes["data-control"] = kind;
                var check = new VisualElement();
                check.style.width = 16;
                check.style.height = 16;
                check.style.flexShrink = 0;
                Register(check, node, result);
                ApplyStyles(check, node, rules, result);
                parent.Add(check);
                return;
            }
        }

        if (node.Tag == "img")
        {
            // Drawn by the scene as an IMG node (vector requirement 9): a box here, the
            // picture in the mesh order with radii, fit and clips.
            var pic = new VisualElement();
            var pw = node.Attr("width");
            var ph = node.Attr("height");
            pic.style.width = pw != null ? StyleApplier.Len(pw) : 120;
            pic.style.height = ph != null ? StyleApplier.Len(ph) : 80;
            pic.style.flexShrink = 0;
            Register(pic, node, result);
            ApplyStyles(pic, node, rules, result);
            parent.Add(pic);
            return;
        }

        if (node.Tag == "video" || node.Tag == "audio" || node.Tag == "input" || node.Tag == "select" || node.Tag == "textarea")
        {
            // A box in the layout; the picture, sound or control itself is a ScriptedScreens
            // element positioned over it by the surface (see HtmlSurface.ApplyExternals).
            var box = new VisualElement();
            var bw = node.Attr("width");
            var bh = node.Attr("height");
            if (bw != null) box.style.width = StyleApplier.Len(bw);
            if (bh != null) box.style.height = StyleApplier.Len(bh);
            if (node.Tag == "audio") { box.style.width = 0; box.style.height = 0; }
            switch (node.Tag)
            {
                case "input":
                {
                    var kind = (node.Attr("type") ?? "text").ToLowerInvariant();
                    if (kind is "checkbox" or "radio") { box.style.width = 18; box.style.height = 18; }
                    else if (kind == "range") { box.style.width = 140; box.style.height = 20; }
                    else { box.style.width = 160; box.style.height = 26; }
                    break;
                }
                case "select": box.style.width = 160; box.style.height = 26; break;
                case "textarea":
                    box.style.width = node.Attr("cols") is { } cols && int.TryParse(cols, out var nc) ? nc * 7 + 16 : 160;
                    box.style.height = node.Attr("rows") is { } rows && int.TryParse(rows, out var nr) ? nr * 17 + 10 : 60;
                    break;
            }
            box.style.flexShrink = 0;
            Register(box, node, result);
            ApplyStyles(box, node, rules, result);
            result.Externals[box] = node;
            parent.Add(box);
            return;
        }

        if (node.Tag == "canvas")
        {
            var cv = new CanvasElement();
            var cw = node.Attr("width");
            var ch = node.Attr("height");
            if (cw != null && float.TryParse(cw, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)) cv.CanvasWidth = w;
            if (ch != null && float.TryParse(ch, NumberStyles.Float, CultureInfo.InvariantCulture, out var h)) cv.CanvasHeight = h;
            Register(cv, node, result);
            ApplyStyles(cv, node, rules, result);
            parent.Add(cv);
            return;
        }

        VisualElement ve;
        var mixed = false;
        if (IsTextLike(node))
        {
            ve = new Label(RichText(node, rules));
        }
        else if (Inline.Contains(node.Tag!) && IsInlineOnly(node) && IsTextLikeIgnoringSelf(node))
        {
            // An inline element reached here on its own (it carries an id or class) whose
            // content is plain text: a Label of its children's rich text; its own styles
            // apply to the element itself through the cascade.
            ve = new Label(RichText(node, rules));
        }
        else
        {
            ve = new VisualElement();
            mixed = IsMixedInline(node);
            if (mixed)
            {
                ve.style.flexDirection = FlexDirection.Row;
                ve.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
                ve.style.alignItems = Align.FlexEnd;
            }
        }

        Register(ve, node, result);
        TagDefaults(ve, node.Tag!);
        if (node.Tag is "pre" or "code" or "kbd" or "samp")
        {
            // A monospace default that a page rule can still override: it is written into the
            // cascade record before the rules, and the layout face follows.
            result.CssOf(ve)["font-family"] = "monospace";
            var mono = FontLibrary.Get("code");
            if (mono != null) ve.style.unityFontDefinition = FontDefinition.FromSDFFont(mono);
        }
        ApplyStyles(ve, node, rules, result);
        parent.Add(ve);
        if (node.Tag == "dialog" && node.Attr("open") == null)
            ve.style.display = DisplayStyle.None;
        if (node.Attr("hidden") != null)
            ve.style.display = DisplayStyle.None;

        if (ve is Label)
            return;

        if (!mixed)
        {
            var list = node.Tag == "ul" || node.Tag == "ol";
            var ordinal = 0;
            if (node.Tag == "details")
                AddDisclosure(node);
            foreach (var child in node.Children)
            {
                if (list && child.Tag == "li")
                    AddMarker(child, node, result.CssOf(ve), ++ordinal, rules);
                Append(ve, child, rules, result);
            }
            if (node.Tag == "details")
                ShowDetails(ve, node, result);
            ApplyGap(ve, result.CssOf(ve));
            Flow(parent, ve, result);
            return;
        }

        // Merge runs between id-bearing inline elements into single labels so "a <b>b</b>"
        // stays one label, and spaces at run edges survive as spaces.
        var run = new StringBuilder();
        void FlushRun()
        {
            var t = run.ToString();
            run.Clear();
            if (t.Trim().Length == 0)
                return;
            var l = new Label(t.Trim());
            if (t.StartsWith(" ", StringComparison.Ordinal)) l.style.marginLeft = 4;
            if (t.EndsWith(" ", StringComparison.Ordinal)) l.style.marginRight = 4;
            ve.Add(l);
        }
        foreach (var child in node.Children)
        {
            if (!child.IsText && KeepsOwnElement(child))
            {
                FlushRun();
                Append(ve, child, rules, result);
                continue;
            }
            AppendRich(run, child, rules);
        }
        FlushRun();
    }

    /// <summary>
    /// Inline SVG. viewBox, width/height attributes, preserveAspectRatio="none"; shapes are
    /// collected flat (a g's children are hoisted, its own attributes are not inherited).
    /// </summary>
    private static SvgElement BuildSvg(HtmlNode node, Result result)
    {
        var svg = new SvgElement();
        var vb = node.Attr("viewBox") ?? node.Attr("viewbox");
        if (vb != null)
        {
            var n = vb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (n.Length == 4
                && float.TryParse(n[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && float.TryParse(n[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                && float.TryParse(n[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                && float.TryParse(n[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                svg.ViewBox = new Rect(x, y, w, h);
        }
        var wAttr = node.Attr("width");
        var hAttr = node.Attr("height");
        if (wAttr != null) svg.style.width = StyleApplier.Len(wAttr);
        if (hAttr != null) svg.style.height = StyleApplier.Len(hAttr);
        if (wAttr == null && hAttr == null && vb == null)
            svg.style.flexGrow = 1;
        if (vb == null)
            result.Warnings.Add("html: <svg> without viewBox uses 0 0 100 100");
        svg.Stretch = string.Equals(node.Attr("preserveAspectRatio"), "none", StringComparison.OrdinalIgnoreCase);

        CollectShapes(node, svg, result);
        return svg;
    }

    private static void CollectShapes(HtmlNode node, SvgElement svg, Result result)
    {
        foreach (var c in node.Children)
        {
            if (c.IsText)
                continue;
            switch (c.Tag)
            {
                case "g":
                case "defs":
                    CollectShapes(c, svg, result);
                    break;
                case "lineargradient":
                case "radialgradient":
                {
                    // Kept as a shape the painter ignores; the vector emitter turns it into a
                    // def. Stops travel as one attribute: offset|color|opacity; ...
                    var shape = new SvgShape { Tag = c.Tag == "lineargradient" ? "linearGradient" : "radialGradient", Owner = svg };
                    foreach (var kv in c.Attributes)
                        shape.Attributes[kv.Key] = kv.Value;
                    var stops = new System.Text.StringBuilder();
                    foreach (var st in c.Children)
                    {
                        if (st.IsText || st.Tag != "stop") continue;
                        var style = st.Attr("style");
                        var colour = st.Attr("stop-color");
                        var opacity = st.Attr("stop-opacity");
                        if (style != null)
                        {
                            foreach (var d in CssParser.ParseDeclarations(style))
                            {
                                if (d.Name == "stop-color") colour = d.Value;
                                else if (d.Name == "stop-opacity") opacity = d.Value;
                            }
                        }
                        stops.Append(st.Attr("offset") ?? "0").Append('|').Append(colour ?? "black").Append('|').Append(opacity ?? string.Empty).Append(';');
                    }
                    shape.Attributes["stops"] = stops.ToString();
                    svg.Shapes.Add(shape);
                    break;
                }
                case "line": case "polyline": case "polygon": case "rect": case "circle": case "ellipse": case "path":
                {
                    var shape = new SvgShape { Tag = c.Tag!, Owner = svg };
                    foreach (var kv in c.Attributes)
                        shape.Attributes[kv.Key] = kv.Value;
                    // style="stroke: red" on a shape: presentation attributes win in SVG,
                    // but authors write style= expecting it to apply, so it does.
                    var style = c.Attr("style");
                    if (style != null)
                    {
                        foreach (var d in CssParser.ParseDeclarations(style))
                            shape.Attributes[d.Name] = d.Value;
                    }
                    svg.Shapes.Add(shape);
                    var id = c.Attr("id");
                    if (id != null)
                        result.Shapes[id] = shape;
                    break;
                }
                default:
                    result.Warnings.Add($"html: svg <{c.Tag}> not supported");
                    break;
            }
        }
    }

    /// <summary>Name, classes, id (a synthetic one when the page gave none) and the node map.</summary>
    private static void Register(VisualElement ve, HtmlNode node, Result result)
    {
        var id = node.Attr("id");
        if (id == null)
        {
            id = "__" + node.Tag + (++_autoId).ToString(CultureInfo.InvariantCulture);
            node.Attributes["id"] = id;
        }
        ve.name = id;
        // A browser never shrinks a block below its content (min-height: auto); the layout
        // engine's default is to shrink flex children to fit a fixed parent. A page's own
        // flex-shrink / flex declaration still wins, since the cascade runs after this.
        ve.style.flexShrink = 0;
        var cls = node.Attr("class");
        if (cls != null)
        {
            foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                ve.AddToClassList(c);
        }
        result.ById[id] = ve;
        result.NodeOf[ve] = node;
    }

    /// <summary>
    /// innerHTML for a label: an HTML fragment as Unity rich text. The fragment's nodes are
    /// parented under the target's node so the page's stylesheet applies to them (the
    /// mockup colours its units with ".val small"), and matched colour, font-size,
    /// font-weight, font-style and text-decoration become rich text tags.
    /// </summary>
    public static string FragmentToRichText(string html, HtmlNode? under, List<CssRule>? rules)
    {
        var frag = HtmlParser.Parse(html);
        if (under != null)
        {
            foreach (var c in frag.Children)
                c.Parent = under;
        }
        var sb = new StringBuilder();
        foreach (var c in frag.Children)
            AppendRich(sb, c, rules);
        return sb.ToString().Trim();
    }

    private static bool IsTextLike(HtmlNode node)
    {
        if (node.Children.Count == 0)
            return false;
        var anyText = false;
        foreach (var c in node.Children)
        {
            if (c.IsText)
            {
                if (c.Text.Trim().Length > 0)
                    anyText = true;
                continue;
            }
            if (!Inline.Contains(c.Tag!))
                return false;
            if (!IsInlineOnly(c))
                return false;
            // An inline element with an id (data-binding target) or a class (styled box,
            // like the mockup's <span class="dia">) keeps its own element; the parent then
            // becomes a wrapping row (see Append). Bare <b>/<i>/<span> still fold into text.
            if (KeepsOwnElement(c))
                return false;
            anyText = true;
        }
        return anyText;
    }

    /// <summary>An inline child that stays its own element: it has an id (data target), a class (styled box) or is generated by ::before/::after (usually a box).</summary>
    private static bool KeepsOwnElement(HtmlNode c) => c.Attr("id") != null || c.Attr("class") != null || c.Attr("data-pseudo") != null || c.Attr("data-marker") != null;

    /// <summary>
    /// ::before / ::after with `content`: a generated span child carrying data-pseudo, so the
    /// cascade can address it (CssSelector.Matches) and the layout treats it like a classed
    /// inline element. `content` takes quoted strings, attr(name) and none; counters do not exist.
    /// </summary>
    private static void AddGenerated(HtmlNode node, List<CssRule> rules)
    {
        if (node.IsText || node.Tag == "svg" || node.Attr("data-pseudo") != null)
            return;
        foreach (var which in new[] { "before", "after" })
        {
            var probe = new HtmlNode { Tag = "span", Parent = node };
            probe.Attributes["data-pseudo"] = which;
            string? content = null;
            var bestSpec = -1;
            var bestOrder = -1;
            foreach (var rule in rules)
            {
                var spec = -1;
                foreach (var sel in rule.Selectors)
                    if (sel.Chain[sel.Chain.Count - 1].PseudoElement == which && sel.Matches(probe))
                        spec = Math.Max(spec, sel.Specificity);
                if (spec < 0) continue;
                foreach (var d in rule.Declarations)
                {
                    if (d.Name != "content") continue;
                    if (spec > bestSpec || (spec == bestSpec && rule.Order > bestOrder)) { content = d.Value; bestSpec = spec; bestOrder = rule.Order; }
                }
            }
            if (content == null) continue;
            var text = GeneratedText(content, node);
            if (text == null) continue;
            probe.Children.Add(new HtmlNode { Text = text, Parent = probe });
            if (which == "before") node.Children.Insert(0, probe); else node.Children.Add(probe);
        }
    }

    /// <summary>The string a `content` value produces, or null for none/normal.</summary>
    private static string? GeneratedText(string value, HtmlNode node)
    {
        var v = value.Trim();
        if (v == "none" || v == "normal") return null;
        var sb = new StringBuilder();
        var i = 0;
        while (i < v.Length)
        {
            var ch = v[i];
            if (ch == '"' || ch == '\'')
            {
                i++;
                while (i < v.Length && v[i] != ch)
                {
                    if (v[i] == '\\' && i + 1 < v.Length)
                    {
                        // CSS escapes: \25B2 is a code point, \" a literal.
                        var j = i + 1;
                        var hex = new StringBuilder();
                        while (j < v.Length && hex.Length < 6 && Uri.IsHexDigit(v[j])) hex.Append(v[j++]);
                        if (hex.Length > 0)
                        {
                            sb.Append(char.ConvertFromUtf32(Convert.ToInt32(hex.ToString(), 16)));
                            if (j < v.Length && v[j] == ' ') j++;
                            i = j;
                            continue;
                        }
                        sb.Append(v[i + 1]);
                        i += 2;
                        continue;
                    }
                    sb.Append(v[i++]);
                }
                i++;
            }
            else if (string.CompareOrdinal(v, i, "attr(", 0, 5) == 0)
            {
                var close = v.IndexOf(')', i);
                if (close < 0) break;
                sb.Append(node.Attr(v.Substring(i + 5, close - i - 5).Trim()) ?? string.Empty);
                i = close + 1;
            }
            else i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// A table is a grid: one column per cell of the widest row, td/th as items, colspan as a
    /// column span. tr/thead/tbody/tfoot are transparent, so a rule on tr has nothing to style.
    /// Columns share the width equally (auto tracks are 1fr here); size them with CSS on the cells.
    /// </summary>
    private static void AppendTable(VisualElement parent, HtmlNode node, List<CssRule> rules, Result result)
    {
        var rows = new List<HtmlNode>();
        CollectRows(node, rows);
        var columns = 1;
        foreach (var row in rows)
        {
            var n = 0;
            foreach (var cell in row.Children)
                if (cell.Tag == "td" || cell.Tag == "th") n += Span(cell);
            columns = Math.Max(columns, n);
        }
        // Rows are real elements, so tr takes background, :nth-child and :hover; sections
        // (thead/tbody/tfoot) too. Cells share the row equally unless a cell names a width,
        // and a colspan takes that many shares. ponytail: no rowspan, no content-sized columns;
        // measure cells in a second pass if a real page needs them.
        var ve = new VisualElement();
        Register(ve, node, result);
        TagDefaults(ve, node.Tag!);
        ApplyStyles(ve, node, rules, result);
        parent.Add(ve);
        foreach (var child in node.Children)
        {
            if (child.IsText) continue;
            switch (child.Tag)
            {
                case "caption":
                    Append(ve, child, rules, result);
                    break;
                case "thead": case "tbody": case "tfoot":
                {
                    var section = new VisualElement();
                    Register(section, child, result);
                    ApplyStyles(section, child, rules, result);
                    ve.Add(section);
                    foreach (var tr in child.Children)
                        if (tr.Tag == "tr") AppendRow(section, tr, columns, rules, result);
                    break;
                }
                case "tr":
                    AppendRow(ve, child, columns, rules, result);
                    break;
            }
        }
        ApplyGap(ve, result.CssOf(ve));
    }

    private static void AppendRow(VisualElement table, HtmlNode tr, int columns, List<CssRule> rules, Result result)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Stretch;
        Register(row, tr, result);
        ApplyStyles(row, tr, rules, result);
        table.Add(row);
        foreach (var cell in tr.Children)
        {
            if (cell.Tag != "td" && cell.Tag != "th") continue;
            var w = cell.Attr("width");
            if (w != null && cell.Attr("style")?.Contains("width") != true)
                cell.Attributes["style"] = "width:" + (w.EndsWith("%", StringComparison.Ordinal) ? w : w + "px") + ";" + (cell.Attr("style") ?? string.Empty);
            Append(row, cell, rules, result);
            var id = cell.Attr("id");
            if (id == null || !result.ById.TryGetValue(id, out var cve)) continue;
            if (!result.CssOf(cve).ContainsKey("width"))
            {
                cve.style.flexGrow = Span(cell);
                cve.style.flexBasis = 0;
            }
            cve.style.flexShrink = 1;
        }
    }

    /// <summary>
    /// After a container's children are built: floats, `display: contents`, columns and
    /// aspect-ratio, the flow features the layout engine has no direct notion of.
    /// - A float makes its parent a wrapping row; `left` goes first, `right` last with an auto
    ///   left margin, the rest of the content takes the remaining width. Not text flowing
    ///   round a box (no inline formatting context), but the layouts pages write with floats.
    /// - `display: contents` unwraps: the children move to the grandparent, the box goes.
    /// - `column-count: n` splits the children into n equal columns in order.
    /// - `aspect-ratio` sets the missing dimension from the other on every layout pass.
    /// </summary>
    private static void Flow(VisualElement parent, VisualElement ve, Result result)
    {
        var css = result.CssOf(ve);
        var anyFloat = false;
        foreach (var child in ve.Children())
        {
            var ccss = result.CssOf(child);
            if (!ccss.TryGetValue("float", out var fl)) continue;
            var f = fl.Trim();
            if (f == "left") { anyFloat = true; child.style.alignSelf = Align.FlexStart; }
            else if (f == "right") { anyFloat = true; child.style.alignSelf = Align.FlexStart; child.style.marginLeft = StyleKeyword.Auto; }
        }
        if (anyFloat && !css.ContainsKey("display"))
        {
            ve.style.flexDirection = FlexDirection.Row;
            ve.style.flexWrap = UnityEngine.UIElements.Wrap.Wrap;
            ve.style.alignItems = Align.FlexStart;
            var order = new List<VisualElement>(ve.Children());
            order.Sort((a, b) => Rank(result.CssOf(a)).CompareTo(Rank(result.CssOf(b))));
            foreach (var c in order) c.BringToFront();
            static int Rank(Dictionary<string, string> c) => c.TryGetValue("float", out var f) ? (f.Trim() == "left" ? 0 : 2) : 1;
        }
        if (css.TryGetValue("column-count", out var cc) && int.TryParse(cc.Trim(), out var columns) && columns > 1)
        {
            var kids = new List<VisualElement>(ve.Children());
            var gap = css.TryGetValue("column-gap", out var cg) ? StyleApplier.Num(cg) : 16f;
            ve.style.flexDirection = FlexDirection.Row;
            ve.style.alignItems = Align.FlexStart;
            var per = (kids.Count + columns - 1) / columns;
            for (var c = 0; c < columns; c++)
            {
                var col = new VisualElement { name = ve.name + "__col" + c };
                col.style.flexGrow = 1; col.style.flexBasis = 0; col.style.flexShrink = 1;
                if (c > 0) col.style.marginLeft = gap;
                for (var i = c * per; i < Math.Min(kids.Count, (c + 1) * per); i++) col.Add(kids[i]);
                ve.Add(col);
            }
        }
        if (css.TryGetValue("aspect-ratio", out var ar))
        {
            var parts = ar.Split('/');
            var ratio = parts.Length == 2 && StyleApplier.IsNumber(parts[0].Trim()) && StyleApplier.IsNumber(parts[1].Trim()) ? StyleApplier.Num(parts[0]) / Mathf.Max(0.001f, StyleApplier.Num(parts[1]))
                      : StyleApplier.IsNumber(ar.Trim()) ? StyleApplier.Num(ar) : 0f;
            if (ratio > 0f)
            {
                var hasH = css.ContainsKey("height");
                ve.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    var w = ve.layout.width; var h = ve.layout.height;
                    if (!hasH && w > 0f && Mathf.Abs(h - w / ratio) > 0.5f) ve.style.height = w / ratio;
                    else if (hasH && h > 0f && Mathf.Abs(w - h * ratio) > 0.5f) ve.style.width = h * ratio;
                });
            }
        }
        if (css.TryGetValue("display", out var display) && display.Trim() == "contents")
        {
            var kids = new List<VisualElement>(ve.Children());
            foreach (var k in kids) parent.Add(k);
            ve.RemoveFromHierarchy();
        }
    }

    /// <summary>The disclosure triangle at the front of a summary, as a drawn marker; a details without a summary gets one.</summary>
    private static void AddDisclosure(HtmlNode details)
    {
        HtmlNode? summary = null;
        foreach (var c in details.Children) if (c.Tag == "summary") { summary = c; break; }
        if (summary == null)
        {
            summary = new HtmlNode { Tag = "summary", Parent = details };
            summary.Children.Add(new HtmlNode { Text = "Details", Parent = summary });
            summary.Attributes["data-click"] = "1";
            details.Children.Insert(0, summary);
        }
        foreach (var c in summary.Children) if (c.Attr("data-marker") != null) return;
        var tri = new HtmlNode { Tag = "span", Parent = summary };
        tri.Attributes["data-marker"] = details.Attr("open") != null ? "tri-down" : "tri-right";
        tri.Attributes["style"] = "width: 0.6em; height: 0.6em; margin-right: 0.4em; align-self: center; flex-shrink: 0";
        summary.Children.Insert(0, tri);
    }

    /// <summary>Everything in a details but its summary shows only while `open` is set.</summary>
    internal static void ShowDetails(VisualElement ve, HtmlNode node, Result result)
    {
        var open = node.Attr("open") != null;
        foreach (var child in ve.Children())
        {
            if (result.NodeOf.TryGetValue(child, out var cn) && cn.Tag == "summary") continue;
            child.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }
        foreach (var c in node.Children)
        {
            if (c.Tag != "summary") continue;
            foreach (var m in c.Children)
                if (m.Attr("data-marker") is "tri-down" or "tri-right") m.Attributes["data-marker"] = open ? "tri-down" : "tri-right";
        }
    }

    private static void CollectRows(HtmlNode node, List<HtmlNode> rows)
    {
        foreach (var c in node.Children)
        {
            if (c.Tag == "tr") rows.Add(c);
            else if (c.Tag == "thead" || c.Tag == "tbody" || c.Tag == "tfoot") CollectRows(c, rows);
        }
    }

    private static int Span(HtmlNode cell)
    {
        var s = cell.Attr("colspan");
        return s != null && int.TryParse(s, out var n) && n > 1 ? n : 1;
    }

    /// <summary>
    /// List markers at the front of the li. Numbering is text; disc, circle and square are
    /// drawn as shapes by the emitter (a span carrying data-marker), because no text face
    /// here is guaranteed the geometric glyphs and a bullet is a shape anyway.
    /// </summary>
    private static void AddMarker(HtmlNode li, HtmlNode list, Dictionary<string, string> css, int ordinal, List<CssRule> rules)
    {
        if (li.Attr("data-listed") != null) return;
        li.Attributes["data-listed"] = "1";
        var type = list.Tag == "ol" ? "decimal" : "disc";
        void TakeType(string name, string value)
        {
            if (name == "list-style-type") { type = value.Trim(); return; }
            if (name != "list-style") return;
            foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (part is "none" or "disc" or "circle" or "square" or "decimal" or "lower-alpha" or "upper-alpha" or "lower-roman" or "upper-roman") type = part;
        }
        // The list's own value, then rules on the item, then the item's inline style: the
        // item has not been through the cascade yet, so its declarations are read here.
        if (css.TryGetValue("list-style", out var ls)) TakeType("list-style", ls);
        if (css.TryGetValue("list-style-type", out var lst)) TakeType("list-style-type", lst);
        var matched = new List<(int spec, int order, CssRule rule)>();
        foreach (var rule in rules)
        {
            var best = -1;
            foreach (var sel in rule.Selectors)
                if (sel.Matches(li)) best = Math.Max(best, sel.Specificity);
            if (best >= 0) matched.Add((best, rule.Order, rule));
        }
        matched.Sort((x, y) => x.spec != y.spec ? x.spec.CompareTo(y.spec) : x.order.CompareTo(y.order));
        foreach (var m in matched)
            foreach (var d in m.rule.Declarations)
                TakeType(d.Name, d.Value);
        if (li.Attr("style") is { } inline)
            foreach (var d in CssParser.ParseDeclarations(inline))
                TakeType(d.Name, d.Value);
        if (type == "none") return;
        string? text = type switch
        {
            "decimal" => ordinal + ". ",
            "lower-alpha" => (char)('a' + (ordinal - 1) % 26) + ". ",
            "upper-alpha" => (char)('A' + (ordinal - 1) % 26) + ". ",
            "lower-roman" => Roman(ordinal).ToLowerInvariant() + ". ",
            "upper-roman" => Roman(ordinal) + ". ",
            _ => null,
        };
        if (text != null)
        {
            li.Children.Insert(0, new HtmlNode { Text = text, Parent = li });
            return;
        }
        var shape = type is "circle" or "square" ? type : "disc";
        var span = new HtmlNode { Tag = "span", Parent = li };
        span.Attributes["data-marker"] = shape;
        span.Attributes["style"] = "width: 0.4em; height: 0.4em; margin-right: 0.5em; align-self: center; flex-shrink: 0";
        li.Children.Insert(0, span);
    }

    private static string Roman(int n)
    {
        var sb = new StringBuilder();
        foreach (var (v, sym) in new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
            while (n >= v) { sb.Append(sym); n -= v; }
        return sb.ToString();
    }

    /// <summary>Text-like test for an inline element's own children (its own id/class do not matter).</summary>
    private static bool IsTextLikeIgnoringSelf(HtmlNode node)
    {
        if (node.Children.Count == 0)
            return true;
        foreach (var c in node.Children)
        {
            if (c.IsText)
                continue;
            if (!Inline.Contains(c.Tag!) || !IsInlineOnly(c))
                return false;
            if (KeepsOwnElement(c))
                return false;
        }
        return true;
    }

    /// <summary>A block whose children are text and inline tags, at least one carrying an id or class.</summary>
    private static bool IsMixedInline(HtmlNode node)
    {
        var anyId = false;
        foreach (var c in node.Children)
        {
            if (c.IsText)
                continue;
            if (!Inline.Contains(c.Tag!) || !IsInlineOnly(c))
                return false;
            if (KeepsOwnElement(c))
                anyId = true;
        }
        return anyId;
    }

    private static bool IsInlineOnly(HtmlNode node)
    {
        foreach (var c in node.Children)
        {
            if (c.IsText)
                continue;
            if (!Inline.Contains(c.Tag!) || !IsInlineOnly(c))
                return false;
        }
        return true;
    }

    /// <summary>Flatten inline children to Unity rich text.</summary>
    private static string RichText(HtmlNode node, List<CssRule>? rules = null)
    {
        var sb = new StringBuilder();
        foreach (var c in node.Children)
            AppendRich(sb, c, rules);
        return sb.ToString().Trim();
    }

    private static void AppendRich(StringBuilder sb, HtmlNode node, List<CssRule>? rules = null)
    {
        if (node.IsText)
        {
            sb.Append(node.Text.Replace("<", "<noparse><</noparse>"));
            return;
        }

        if (node.Tag == "br")
        {
            sb.Append('\n');
            return;
        }

        // Tag defaults, then matching stylesheet rules, then inline style, then attributes.
        var open = new StringBuilder();
        var close = new StringBuilder();
        string? colour = null;
        string? size = null;
        string? face = null;
        var bold = node.Tag == "b" || node.Tag == "strong";
        var italic = node.Tag is "i" or "em" or "cite" or "var" or "dfn" or "address";
        var underline = node.Tag is "u" or "a" or "ins";
        var strike = node.Tag is "s" or "del" or "strike";
        switch (node.Tag)
        {
            case "a": colour = "#4EA1FF"; break;
            case "code": case "kbd": case "samp": case "tt": face = "code"; break;
            case "q": open.Append('\u201C'); close.Insert(0, "\u201D"); break;
            case "wbr": return;
            case "sub": open.Append("<sub>"); close.Insert(0, "</sub>"); break;
            case "sup": open.Append("<sup>"); close.Insert(0, "</sup>"); break;
            case "small": size = "80%"; break;
            case "big": size = "120%"; break;
            case "mark": colour = "#FFD54F"; break;
        }

        void Take(CssDeclaration raw)
        {
            if (raw.Name.StartsWith("--", StringComparison.Ordinal))
            {
                (node.Vars ??= new Dictionary<string, string>(StringComparer.Ordinal))[raw.Name] = raw.Value.Trim();
                return;
            }
            var d = raw.Value.IndexOf("var(", StringComparison.Ordinal) >= 0
                ? new CssDeclaration(raw.Name, ResolveVars(raw.Value, node), raw.Important)
                : raw;
            switch (d.Name)
            {
                case "color": colour = d.Value; break;
                case "font-size": size = d.Value; break;
                case "font-family": face = d.Value.Split(',')[0].Trim().Trim('"', '\''); break;
                case "vertical-align":
                {
                    var va = d.Value.Trim().ToLowerInvariant();
                    if (va == "sub") { open.Append("<sub>"); close.Insert(0, "</sub>"); }
                    else if (va == "super") { open.Append("<sup>"); close.Insert(0, "</sup>"); }
                    else if (va is "middle" or "text-top" or "top") { open.Append("<voffset=0.25em>"); close.Insert(0, "</voffset>"); }
                    else if (va is "text-bottom" or "bottom") { open.Append("<voffset=-0.15em>"); close.Insert(0, "</voffset>"); }
                    else if (va != "baseline") { open.Append("<voffset=").Append(va).Append('>'); close.Insert(0, "</voffset>"); }
                    break;
                }
                case "font-weight": bold = d.Value == "bold" || d.Value == "bolder" || (StyleApplier.IsNumber(d.Value) && StyleApplier.Num(d.Value) >= 600); break;
                case "font-style": italic = d.Value == "italic" || d.Value == "oblique"; break;
                case "text-decoration": underline = d.Value.Contains("underline"); strike = d.Value.Contains("line-through"); break;
                case "font":
                    foreach (var part in d.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (part == "bold" || (StyleApplier.IsNumber(part) && StyleApplier.Num(part) >= 600)) bold = true;
                        else if (part == "italic") italic = true;
                        else if (char.IsDigit(part[0]) || part[0] == '.') { size = part.Split('/')[0]; break; }
                    }
                    break;
            }
        }

        if (rules != null)
        {
            var matches = new List<(int spec, int order, CssRule rule)>();
            foreach (var rule in rules)
            {
                var best = -1;
                foreach (var sel in rule.Selectors)
                {
                    if (sel.Matches(node))
                        best = Math.Max(best, sel.Specificity);
                }
                if (best >= 0)
                    matches.Add((best, rule.Order, rule));
            }
            matches.Sort((x, y) => x.spec != y.spec ? x.spec.CompareTo(y.spec) : x.order.CompareTo(y.order));
            foreach (var m in matches)
            {
                foreach (var d in m.rule.Declarations)
                    Take(d);
            }
        }
        var style = node.Attr("style");
        if (style != null)
        {
            foreach (var d in CssParser.ParseDeclarations(style))
                Take(d);
        }
        colour = node.Attr("color") ?? colour;
        size = node.Attr("size") ?? size;
        face = node.Attr("face") ?? face;
        if (face != null) { open.Append("<font=\"").Append(face).Append("\">"); close.Insert(0, "</font>"); }

        if (bold) { open.Append("<b>"); close.Insert(0, "</b>"); }
        if (italic) { open.Append("<i>"); close.Insert(0, "</i>"); }
        if (underline) { open.Append("<u>"); close.Insert(0, "</u>"); }
        if (strike) { open.Append("<s>"); close.Insert(0, "</s>"); }
        if (colour != null && StyleApplier.TryColor(colour, out var c))
        {
            open.Append("<color=#").Append(ColorUtilityHex(c)).Append('>');
            close.Insert(0, "</color>");
        }
        if (size != null)
        {
            var sz = size.Trim();
            open.Append("<size=").Append(sz.EndsWith("%", StringComparison.Ordinal) ? sz : StyleApplier.Num(sz).ToString(CultureInfo.InvariantCulture)).Append('>');
            close.Insert(0, "</size>");
        }
        sb.Append(open);
        foreach (var child in node.Children)
            AppendRich(sb, child, rules);
        sb.Append(close);
    }

    private static string ColorUtilityHex(Color c)
    {
        // Managed formatting; ColorUtility.ToHtmlStringRGBA is native.
        var r = Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f);
        var g = Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f);
        var b = Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
        var a = Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f);
        return r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture)
             + b.ToString("X2", CultureInfo.InvariantCulture) + a.ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>The handful of user-agent defaults that make plain HTML look like HTML.</summary>
    private static void TagDefaults(VisualElement ve, string tag)
    {
        var s = ve.style;
        switch (tag)
        {
            case "h1": s.fontSize = 28; s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 8; s.marginBottom = 8; break;
            case "h2": s.fontSize = 22; s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 6; s.marginBottom = 6; break;
            case "h3": s.fontSize = 18; s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 4; s.marginBottom = 4; break;
            case "h4": case "h5": case "h6": s.fontSize = 15; s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 4; s.marginBottom = 4; break;
            case "p": s.marginTop = 4; s.marginBottom = 4; break;
            case "b": case "strong": s.unityFontStyleAndWeight = FontStyle.Bold; break;
            case "i": case "em": s.unityFontStyleAndWeight = FontStyle.Italic; break;
            case "hr": s.height = 1; s.backgroundColor = new Color(1, 1, 1, 0.3f); s.marginTop = 6; s.marginBottom = 6; break;
            case "a": s.color = new Color(0.31f, 0.63f, 1f); break;
            case "ul": case "ol": s.paddingLeft = 16; s.marginTop = 4; s.marginBottom = 4; break;
            case "li": s.marginBottom = 2; break;
            case "th": s.unityFontStyleAndWeight = FontStyle.Bold; s.paddingTop = 2; s.paddingBottom = 2; s.paddingLeft = 4; s.paddingRight = 4; break;
            case "td": s.paddingTop = 2; s.paddingBottom = 2; s.paddingLeft = 4; s.paddingRight = 4; break;
            case "caption": s.unityTextAlign = TextAnchor.MiddleCenter; s.marginBottom = 2; break;
            case "pre": case "code": case "kbd": case "samp": s.whiteSpace = WhiteSpace.NoWrap; break;
            case "blockquote": s.marginLeft = 20; s.marginTop = 6; s.marginBottom = 6; s.paddingLeft = 8; s.borderLeftWidth = 2; s.borderLeftColor = new Color(1, 1, 1, 0.3f); break;
            case "address": case "cite": case "var": case "dfn": s.unityFontStyleAndWeight = FontStyle.Italic; break;
            case "figure": s.marginTop = 8; s.marginBottom = 8; break;
            case "figcaption": s.fontSize = 12; s.color = new Color(1, 1, 1, 0.6f); s.marginTop = 2; break;
            case "dt": s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 4; break;
            case "dd": s.marginLeft = 20; break;
            case "fieldset": s.borderTopWidth = 1; s.borderRightWidth = 1; s.borderBottomWidth = 1; s.borderLeftWidth = 1; s.borderTopColor = s.borderRightColor = s.borderBottomColor = s.borderLeftColor = new Color(1, 1, 1, 0.3f); s.paddingTop = 8; s.paddingRight = 10; s.paddingBottom = 8; s.paddingLeft = 10; s.marginTop = 6; s.marginBottom = 6; s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 4; break;
            case "legend": s.unityFontStyleAndWeight = FontStyle.Bold; s.fontSize = 12; s.color = new Color(1, 1, 1, 0.6f); s.marginBottom = 4; s.alignSelf = Align.FlexStart; break; // ponytail: sits inside the box, not cut into its border
            case "summary": s.unityFontStyleAndWeight = FontStyle.Bold; s.flexDirection = FlexDirection.Row; s.alignItems = Align.Center; break;
            case "details": s.marginTop = 4; s.marginBottom = 4; break;
            case "dialog":
                s.position = Position.Absolute; s.left = new Length(50, LengthUnit.Percent); s.top = new Length(50, LengthUnit.Percent);
                s.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
                s.backgroundColor = new Color(0.12f, 0.16f, 0.23f); s.paddingTop = s.paddingBottom = 12; s.paddingLeft = s.paddingRight = 16;
                s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 8;
                break;
            case "label": s.flexDirection = FlexDirection.Row; s.alignItems = Align.Center; break;
            case "center": s.alignItems = Align.Center; s.unityTextAlign = TextAnchor.MiddleCenter; break;
            case "button":
                s.paddingTop = 4; s.paddingBottom = 4; s.paddingLeft = 10; s.paddingRight = 10;
                s.backgroundColor = new Color(0.25f, 0.28f, 0.35f);
                s.borderTopLeftRadius = 4; s.borderTopRightRadius = 4; s.borderBottomLeftRadius = 4; s.borderBottomRightRadius = 4;
                s.unityTextAlign = TextAnchor.MiddleCenter; s.alignSelf = Align.FlexStart;
                break;
        }
    }

    private static float Px(StyleLength l) => l.keyword == StyleKeyword.Undefined && l.value.unit == LengthUnit.Pixel ? l.value.value : 0f;

    /// <summary>The nearest ancestor's cascaded value for a property, or null.</summary>
    private static string? InheritedValue(HtmlNode node, string property, Result result)
    {
        for (var n = node.Parent; n != null; n = n.Parent)
        {
            var id = n.Attr("id");
            if (id != null && result.ById.TryGetValue(id, out var pve) && result.CssOf(pve).TryGetValue(property, out var v))
                return v;
        }
        return null;
    }

    private static float InheritedFontSize(HtmlNode? node, Result result)
    {
        for (var n = node; n != null; n = n.Parent)
            if (result.FontSizes.TryGetValue(n, out var px)) return px;
        return StyleApplier.RootFontSize;
    }

    /// <summary>Substitute var(--name[, fallback]) from this node's chain of custom properties.</summary>
    internal static string ResolveVars(string value, HtmlNode node)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < value.Length)
        {
            var at = value.IndexOf("var(", i, StringComparison.Ordinal);
            if (at < 0) { sb.Append(value, i, value.Length - i); break; }
            sb.Append(value, i, at - i);
            var depth = 0;
            var j = at + 3;
            while (j < value.Length)
            {
                if (value[j] == '(') depth++;
                else if (value[j] == ')' && --depth == 0) break;
                j++;
            }
            var inner = value.Substring(at + 4, Math.Max(0, j - at - 4));
            var comma = inner.IndexOf(',');
            var name = (comma >= 0 ? inner.Substring(0, comma) : inner).Trim();
            var fallback = comma >= 0 ? inner.Substring(comma + 1).Trim() : null;
            string? found = null;
            for (var n = node; n != null && found == null; n = n.Parent)
                if (n.Vars != null && n.Vars.TryGetValue(name, out var v)) found = v;
            found ??= fallback != null ? ResolveVars(fallback, node) : string.Empty;
            sb.Append(found);
            i = j + 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Flex `gap` on a layout engine without it: margins on the children along the main
    /// axis, and on the cross axis when the container wraps.
    /// </summary>
    private static void ApplyGap(VisualElement ve, Dictionary<string, string> css)
    {
        if (css.TryGetValue("display", out var display) && display.Trim() == "grid") return;
        var rowGap = 0f; var colGap = 0f;
        if (css.TryGetValue("gap", out var gap))
        {
            var parts = gap.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rowGap = StyleApplier.Num(parts[0]);
            colGap = parts.Length > 1 ? StyleApplier.Num(parts[1]) : rowGap;
        }
        if (css.TryGetValue("row-gap", out var rg)) rowGap = StyleApplier.Num(rg);
        if (css.TryGetValue("column-gap", out var cg)) colGap = StyleApplier.Num(cg);
        if (rowGap <= 0f && colGap <= 0f) return;
        var dir = ve.style.flexDirection.value;
        var row = dir == FlexDirection.Row || dir == FlexDirection.RowReverse;
        var wrap = ve.style.flexWrap.value == UnityEngine.UIElements.Wrap.Wrap;
        var count = ve.childCount;
        for (var i = 0; i < count; i++)
        {
            var child = ve[i];
            var last = i == count - 1;
            if (row)
            {
                if (!last && colGap > 0f) child.style.marginRight = child.resolvedStyle.marginRight + colGap;
                if (wrap && rowGap > 0f) child.style.marginBottom = child.resolvedStyle.marginBottom + rowGap;
            }
            else
            {
                if (!last && rowGap > 0f) child.style.marginBottom = child.resolvedStyle.marginBottom + rowGap;
                if (wrap && colGap > 0f) child.style.marginRight = child.resolvedStyle.marginRight + colGap;
            }
        }
    }

    private static void ApplyStyles(VisualElement ve, HtmlNode node, List<CssRule> rules, Result result)
    {
        void Warn(string m) => result.Warnings.Add(m);

        // Matching rules by specificity then source order; inline last; !important
        // declarations after everything else, in the same order among themselves.
        var matches = new List<(int spec, int order, CssRule rule)>();
        foreach (var rule in rules)
        {
            var best = -1;
            foreach (var sel in rule.Selectors)
            {
                if (sel.Matches(node))
                    best = Math.Max(best, sel.Specificity);
            }
            if (best >= 0)
                matches.Add((best, rule.Order, rule));
        }
        matches.Sort((a, b) => a.spec != b.spec ? a.spec.CompareTo(b.spec) : a.order.CompareTo(b.order));

        var ordered = new List<CssDeclaration>();
        var important = new List<CssDeclaration>();
        foreach (var m in matches)
        {
            foreach (var d in m.rule.Declarations)
                (d.Important ? important : ordered).Add(d);
        }
        var inline = node.Attr("style");
        if (inline != null)
        {
            foreach (var d in CssParser.ParseDeclarations(inline))
                (d.Important ? important : ordered).Add(d);
        }
        ordered.AddRange(important);

        AnimationSpec? anim = null;
        var record = result.CssOf(ve);
        StyleApplier.EmSize = InheritedFontSize(node.Parent, result);
        // Custom properties first, whatever rule they came from: `:root { --pad }` sorts after
        // `body { padding: var(--pad) }` by specificity, and the variable must exist by then.
        foreach (var raw in ordered)
        {
            if (raw.Name.StartsWith("--", StringComparison.Ordinal))
                (node.Vars ??= new Dictionary<string, string>(StringComparer.Ordinal))[raw.Name] = raw.Value.Trim();
        }
        foreach (var raw in ordered)
        {
            // var() in a value is resolved here, so everything downstream (the emitter
            // included) sees the substituted value.
            if (raw.Name.StartsWith("--", StringComparison.Ordinal))
                continue;
            var d = raw.Value.IndexOf("var(", StringComparison.Ordinal) >= 0
                ? new CssDeclaration(raw.Name, ResolveVars(raw.Value, node), raw.Important)
                : raw;
            // Keywords: inherit takes the parent's cascaded value (the layout inherits text
            // properties by itself, but not backgrounds or borders); initial/unset/revert
            // drop the declaration. currentColor is the element's own colour, else inherited.
            var kw = d.Value.Trim().ToLowerInvariant();
            if (kw == "inherit")
            {
                var inherited = InheritedValue(node, d.Name, result);
                if (inherited == null) { record.Remove(d.Name); continue; }
                d = new CssDeclaration(d.Name, inherited, d.Important);
            }
            else if (kw is "initial" or "unset" or "revert" or "revert-layer")
            {
                record.Remove(d.Name);
                continue;
            }
            if (d.Value.IndexOf("currentcolor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var cc = d.Name == "color" ? InheritedValue(node, "color", result) : (record.TryGetValue("color", out var own) ? own : InheritedValue(node, "color", result));
                d = new CssDeclaration(d.Name, System.Text.RegularExpressions.Regex.Replace(d.Value, "currentcolor", cc ?? "#FFFFFF", System.Text.RegularExpressions.RegexOptions.IgnoreCase), d.Important);
            }
            record[d.Name] = d.Value;
            if (d.Name == "font-size")
            {
                var fs = d.Value.Trim();
                var px = fs.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.EmSize * StyleApplier.Num(fs) / 100f : StyleApplier.Num(fs);
                if (px > 0f) { result.FontSizes[node] = px; StyleApplier.EmSize = px; }
            }
            if (d.Name.StartsWith("animation", StringComparison.Ordinal))
            {
                anim ??= new AnimationSpec();
                ApplyAnimationDeclaration(anim, d);
                continue;
            }
            StyleApplier.Apply(ve, d, Warn);
        }
        // CSS sizes a box content-box unless told otherwise; the layout engine is border-box.
        // A width the page set therefore grows by its padding and border, unless the page
        // opted into border-box (which most stylesheets do with `* { box-sizing: border-box }`).
        if (!(record.TryGetValue("box-sizing", out var sizing) && sizing.Trim() == "border-box"))
        {
            var st = ve.style;
            if (record.ContainsKey("width") && st.width.keyword == StyleKeyword.Undefined && st.width.value.unit == LengthUnit.Pixel)
                st.width = st.width.value.value + Px(st.paddingLeft) + Px(st.paddingRight) + st.borderLeftWidth.value + st.borderRightWidth.value;
            if (record.ContainsKey("height") && st.height.keyword == StyleKeyword.Undefined && st.height.value.unit == LengthUnit.Pixel)
                st.height = st.height.value.value + Px(st.paddingTop) + Px(st.paddingBottom) + st.borderTopWidth.value + st.borderBottomWidth.value;
        }
        if (record.TryGetValue("display", out var display) && display.Trim() == "grid")
            result.Grids.Add(ve);
        if (record.TryGetValue("position", out var position) && position.Trim() == "sticky")
        {
            // Sticky stays in flow; its top/left are the pin, not an offset. The emitter pins it.
            ve.style.top = StyleKeyword.Auto;
            ve.style.left = StyleKeyword.Auto;
        }

        if (anim != null && anim.Name.Length > 0 && anim.Name != "none")
        {
            if (result.Keyframes.ContainsKey(anim.Name))
                result.Animations.Add((ve, anim));
            else
                Warn($"css: animation \"{anim.Name}\" has no @keyframes");
        }
    }

    private static void ApplyAnimationDeclaration(AnimationSpec anim, CssDeclaration d)
    {
        var v = d.Value.Trim();
        switch (d.Name)
        {
            case "animation":
            {
                var times = 0;
                foreach (var token in CssParser.SplitTopLevel(v, ' '))
                    if (token.Length > 0) anim.ApplyToken(token, ref times);
                break;
            }
            case "animation-name": anim.Name = v; break;
            case "animation-duration": { var t = 0; anim.ApplyToken(v, ref t); break; }
            case "animation-delay": { var t = 1; anim.ApplyToken(v, ref t); break; }
            case "animation-iteration-count":
            case "animation-timing-function":
            case "animation-direction":
            case "animation-fill-mode":
            case "animation-play-state": { var t = 0; anim.ApplyToken(v, ref t); break; }
        }
    }
}

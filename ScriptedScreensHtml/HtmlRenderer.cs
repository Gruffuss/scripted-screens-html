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
        "b", "strong", "i", "em", "u", "s", "span", "br", "small", "big", "font", "code", "sub", "sup", "mark",
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
        Collect(doc, rules, script, result.Keyframes, Warn);
        result.Script = script.ToString();
        rules.Sort((a, b) => a.Order.CompareTo(b.Order));
        result.Rules = rules;

        var body = Find(doc, "body") ?? Find(doc, "html") ?? doc;
        result.ViewportWidth = ReadViewport(doc);
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

    private static void Collect(HtmlNode node, List<CssRule> rules, StringBuilder script, Dictionary<string, CssKeyframes> keyframes, Action<string> warn)
    {
        if (node.Tag == "style")
        {
            foreach (var c in node.Children)
                rules.AddRange(CssParser.ParseStylesheet(c.Text, warn, keyframes));
            return;
        }
        if (node.Tag == "script")
        {
            foreach (var c in node.Children)
                script.Append(c.Text).Append('\n');
            return;
        }
        foreach (var c in node.Children)
            Collect(c, rules, script, keyframes, warn);
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

        if (node.Tag == "svg")
        {
            var svg = BuildSvg(node, result);
            Register(svg, node, result);
            ApplyStyles(svg, node, rules, result);
            parent.Add(svg);
            return;
        }

        if (node.Tag == "img" || node.Tag == "video" || node.Tag == "audio")
        {
            // A box in the layout; the picture or sound itself is a ScriptedScreens element
            // positioned over it by the surface (see HtmlSurface.ApplyExternals).
            var box = new VisualElement();
            var bw = node.Attr("width");
            var bh = node.Attr("height");
            if (bw != null) box.style.width = StyleApplier.Len(bw);
            if (bh != null) box.style.height = StyleApplier.Len(bh);
            if (node.Tag == "audio") { box.style.width = 0; box.style.height = 0; }
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
        ApplyStyles(ve, node, rules, result);
        parent.Add(ve);

        if (ve is Label)
            return;

        if (!mixed)
        {
            foreach (var child in node.Children)
                Append(ve, child, rules, result);
            ApplyGap(ve, result.CssOf(ve));
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
            if (!child.IsText && (child.Attr("id") != null || child.Attr("class") != null))
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
            if (c.Attr("id") != null || c.Attr("class") != null)
                return false;
            anyText = true;
        }
        return anyText;
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
            if (c.Attr("id") != null || c.Attr("class") != null)
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
            if (c.Attr("id") != null || c.Attr("class") != null)
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
        var bold = node.Tag == "b" || node.Tag == "strong";
        var italic = node.Tag == "i" || node.Tag == "em";
        var underline = node.Tag == "u";
        var strike = node.Tag == "s";
        switch (node.Tag)
        {
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
            case "pre": case "code": s.whiteSpace = WhiteSpace.NoWrap; break;
            case "center": s.alignItems = Align.Center; s.unityTextAlign = TextAnchor.MiddleCenter; break;
            case "button":
                s.paddingTop = 4; s.paddingBottom = 4; s.paddingLeft = 10; s.paddingRight = 10;
                s.backgroundColor = new Color(0.25f, 0.28f, 0.35f);
                s.borderTopLeftRadius = 4; s.borderTopRightRadius = 4; s.borderBottomLeftRadius = 4; s.borderBottomRightRadius = 4;
                s.unityTextAlign = TextAnchor.MiddleCenter; s.alignSelf = Align.FlexStart;
                break;
        }
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
        foreach (var raw in ordered)
        {
            // Custom properties are stored, not applied; var() in a value is resolved here,
            // so everything downstream (the emitter included) sees the substituted value.
            if (raw.Name.StartsWith("--", StringComparison.Ordinal))
            {
                (node.Vars ??= new Dictionary<string, string>(StringComparer.Ordinal))[raw.Name] = raw.Value.Trim();
                continue;
            }
            var d = raw.Value.IndexOf("var(", StringComparison.Ordinal) >= 0
                ? new CssDeclaration(raw.Name, ResolveVars(raw.Value, node), raw.Important)
                : raw;
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
        if (record.TryGetValue("display", out var display) && display.Trim() == "grid")
            result.Grids.Add(ve);

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
                foreach (var token in v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    anim.ApplyToken(token, ref times);
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

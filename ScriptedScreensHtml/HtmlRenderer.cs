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
        "ruby", "rt", "rp", "rb",
        // inline replaced elements and controls: inline in the flow, always their own element
        "img", "input", "button", "select", "textarea", "label",
    };

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "title", "meta", "link", "script", "style", "template", "noscript",
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
        /// <summary>The page's own source, as it was built: what the compiler reads the page as it was written from.</summary>
        public string Source = string.Empty;
        /// <summary>&lt;script src&gt; urls in document order and &lt;link rel=stylesheet href&gt; urls; the surface fetches them.</summary>
        public readonly List<string> ExternalScripts = new();
        public readonly List<string> ExternalStyles = new();
        /// <summary>@import urls; the surface fetches each and inlines it in place of the statement.</summary>
        public readonly List<string> ExternalImports = new();
        /// <summary>Module scripts that import from URLs: the surface fetches and runs the imports in order, then the body.</summary>
        public readonly List<(List<string> urls, string code)> Modules = new();
        /// <summary>Design width from meta viewport, or 0 to use the element's own width.</summary>
        public float ViewportWidth;
        /// <summary>Stylesheet rules and the node each element came from, for className changes at runtime.</summary>
        public List<CssRule> Rules = new();
        /// <summary>
        /// Element ids whose wrapping transform group must carry its id, so the numbers on it are
        /// addressable slots rather than positional ones nothing outside can name. Only the few a
        /// page's script actually drives: the renderer retains an identified node's whole prop
        /// array, so naming every wrapper would retain hundreds per page. Filled from DomWrites.
        /// </summary>
        public readonly HashSet<string> NamedGroups = new(StringComparer.Ordinal);
        /// <summary>
        /// Every element a page's script writes after load. Wider than <see cref="NamedGroups"/> and
        /// cheaper: it only decides whether a key is emitted at all, where naming a node makes the
        /// renderer retain its whole prop array. Used to keep a key that would otherwise be dropped
        /// for having a zero value - a slot that is not emitted can never be written to.
        /// </summary>
        public readonly HashSet<string> Driven = new(StringComparer.Ordinal);

        /// <summary>
        /// Numbers the synthetic ids of elements the page gave none. Per page, not per process: it
        /// used to be a static that was never reset, so an id counted every element this process had
        /// ever built rather than the document it belongs to. That id is what the renderer hands Lua
        /// as a click's value, and a compiled page's handler matches on it - so it has to be the
        /// same next session or the button silently stops working.
        /// </summary>
        internal int AutoId;
        /// <summary>&lt;base href&gt;: relative urls in the page resolve against it.</summary>
        public string? BaseUrl;
        /// <summary>@starting-style rules: the state a newly shown element transitions from.</summary>
        public readonly List<CssRule> StartingRules = new();
        /// <summary>Attribute names the stylesheet selects on ([data-mode=dark]): a script write to one re-cascades the element and its subtree.</summary>
        public readonly HashSet<string> AttributeSelectors = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Run after a re-cascade: the post-layout placements (grid items, mixed calc, baselines) put back what the cascade reset.</summary>
        internal readonly List<(VisualElement owner, Action act)> AfterRecascade = new();
        internal void OnRecascade(VisualElement owner, Action act) => AfterRecascade.Add((owner, act));

        /// <summary>
        /// How a container query reads this page. CssParser's hook is one static shared by every
        /// page, so it is re-installed before every re-cascade rather than trusted from Build.
        /// </summary>
        internal Func<HtmlNode, (Dictionary<string, string> css, float width, float height)?>? ContainerInfo;
        internal void InstallContainers() => CssParser.InstallContainers(ContainerInfo, Warnings.Add);

        /// <summary>A query container's content box changed: the queries under it may answer differently now.</summary>
        internal void ContainerResized(VisualElement ve)
        {
            InstallContainers();
            Recascade(ve);
            PruneForgotten();
            foreach (var after in AfterRecascade.ToArray()) after.act();
        }

        /// <summary>
        /// Everything the page keeps per element, dropped when the element is removed. A page that
        /// rebuilds its content with innerHTML every tick otherwise keeps every element it ever had:
        /// the node map grew without bound and is walked on every rebuild, and the retained
        /// subtrees lengthened every garbage collection (the game crawled within a minute).
        /// </summary>
        internal void ForgetElement(VisualElement ve)
        {
            if (NodeOf.TryGetValue(ve, out var node)) { FontSizes.Remove(node); NodeOf.Remove(ve); }
            _css.Remove(ve);
            LayoutAttached.Remove(ve);
            AnimationAttached.Remove(ve);
            GapContainers.Remove(ve);
            TabularAttached.Remove(ve);
            TimeAnimations.Remove(ve);
            Externals.Remove(ve);
            Grids.Remove(ve);
            StyleApplier.MixedCalc.Remove(ve);
            StyleApplier.BaselineRows.Remove(ve);
            lock (Tweens.Shared)
            {
                Tweens.Override.Remove(ve);
                Tweens.AllowDiscrete.Remove(ve);
                Tweens.PendingHide.Remove(ve);
            }
            _forgotten.Add(ve);
        }

        private readonly HashSet<VisualElement> _forgotten = new();

        /// <summary>Lists pruned in one pass once enough elements went (removal from a list per element would be quadratic).</summary>
        internal void PruneForgotten()
        {
            if (_forgotten.Count == 0) return;
            Animations.RemoveAll(a => _forgotten.Contains(a.element));
            AfterRecascade.RemoveAll(a => _forgotten.Contains(a.owner));
            _forgotten.Clear();
        }

        /// <summary>Style records created so far (a page whose count climbs without bound builds without end).</summary>
        internal int CssCount => _cssMade;
        private int _cssMade;
        /// <summary>Elements whose grid or post-layout pass is attached, so a fragment appended later attaches only its own.</summary>
        internal readonly HashSet<VisualElement> LayoutAttached = new();
        /// <summary>Elements whose keyframe animation has its runner.</summary>
        internal readonly HashSet<VisualElement> AnimationAttached = new();
        /// <summary>Infinite opacity/transform animations the scene runs by itself, with the Time.time they started.</summary>
        internal readonly Dictionary<VisualElement, (AnimationSpec spec, float start)> TimeAnimations = new();
        /// <summary>Flex containers with a gap, re-applied when their children or cascade change.</summary>
        internal readonly HashSet<VisualElement> GapContainers = new();
        /// <summary>Labels whose tabular-figure width pass is attached.</summary>
        internal readonly HashSet<VisualElement> TabularAttached = new();
        public readonly Dictionary<VisualElement, HtmlNode> NodeOf = new();

        /// <summary>
        /// A number per element, stable for as long as the element lives: what the emitter names its
        /// defs after, so an element's ids do not move when a sibling gains or loses one.
        /// </summary>
        private readonly Dictionary<VisualElement, int> _emitIndex = new();
        private int _emitIndexNext;

        /// <summary>
        /// Elements whose style record, attributes or script styles changed since the last capture.
        /// The emitter's cache is keyed on the element's resolved box, which catches everything the
        /// engine resolves - but a record holds what it does not (shadows, gradients, clips, masks,
        /// filters, letter spacing), so every write to one says so here. Read and cleared by
        /// OffThread.Capture under the same lock that does the writing.
        /// </summary>
        internal readonly HashSet<VisualElement> Touched = new();
        /// <summary>Elements whose change reaches their descendants too: an inherited property written straight into the record, where no re-cascade walks the subtree to report it.</summary>
        internal readonly HashSet<VisualElement> TouchedDeep = new();

        internal void Touch(VisualElement ve) => Touched.Add(ve);

        internal void TouchSubtree(VisualElement ve) { Touched.Add(ve); TouchedDeep.Add(ve); }

        public int EmitIndexOf(VisualElement ve)
        {
            if (_emitIndex.TryGetValue(ve, out var index)) return index;
            index = ++_emitIndexNext;
            _emitIndex[ve] = index;
            return index;
        }
        public HtmlNode Document = new();
        /// <summary>display: grid containers, laid out by GridLayout once attached.</summary>
        public readonly List<VisualElement> Grids = new();
        /// <summary>img / video / audio boxes: drawn by ScriptedScreens' own image, media and sound elements.</summary>
        public readonly Dictionary<VisualElement, HtmlNode> Externals = new();
        /// <summary>Computed font size per node, for em units on its children.</summary>
        public readonly Dictionary<HtmlNode, float> FontSizes = new();
        /// <summary>Each node's declared line-height as computed (px, or a bare factor), for the `lh` unit below it.</summary>
        public readonly Dictionary<HtmlNode, string> LineHeights = new();

        /// <summary>The declarations that won the cascade per element, for the vector emitter (gradients, transforms, fonts).</summary>
        // Weak keys: a record never keeps an element alive. A removed element's own callbacks can still
        // ask for its record after the page forgot it (the growth that made the game crawl).
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, Dictionary<string, string>> _css = new();

        public Dictionary<string, string> CssOf(VisualElement ve)
        {
            if (!_css.TryGetValue(ve, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                _css.Add(ve, map);
                _cssMade++;
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
            InstallContainers();
            node.Attributes["class"] = classes;
            ve.ClearClassList();
            foreach (var c in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                ve.AddToClassList(c);
            ApplyStyles(ve, node, Rules, this);
            // the descendants too: their rules may key off this element (".dark .card", "[data-mode=dark]")
            // and their var() values off custom properties it declares; labels rebuild their rich text
            Recascade(ve);
            // hooks of elements a script has since removed go (an innerHTML page rebuilds its content every tick)
            PruneForgotten();
            GapContainers.RemoveWhere(g => g.panel == null);
            foreach (var g in GapContainers) ApplyGap(g, CssOf(g), this);
            foreach (var after in AfterRecascade.ToArray()) after.act();
        }

        private void Recascade(VisualElement ve)
        {
            foreach (var child in ve.Children())
            {
                if (NodeOf.TryGetValue(child, out var cn) && !cn.IsText)
                {
                    ApplyStyles(child, cn, Rules, this);
                    if (child is Label label && cn.Children.Count > 0 && cn.Attr("data-control") == null && cn.Tag != "svg")
                        label.text = RichText(cn, Rules);
                }
                Recascade(child);
            }
        }
    }



    public static Result Build(string source, FaceData? font)
    {
        var result = new Result();
        void Warn(string m) => result.Warnings.Add(m);

        // Lets @supports ask the applier what it supports. Installed here rather than in a static
        // constructor so it is in place for every entry point - the mod, the bench and the tests
        // all come through Build - and CssParser keeps knowing nothing about Unity.
        StyleApplier.InstallSupportsOracle();
        // @container: answered against the container's own laid-out content box, by name when the
        // query gives one. Without this every @container block warns and is skipped - the parser half
        // has been in place since 5ed23c7 and nothing ever installed its hook.
        result.ContainerInfo = n =>
        {
            if (n.Attr("id") is not { } nid || !result.ById.TryGetValue(nid, out var cve) || cve == null) return null;
            // Before layout a box has no size, and a query answered against NaN or zero would match
            // `(min-width: 0)` on every container. No size means no answer, not a small one.
            if (float.IsNaN(cve.layout.width) || float.IsNaN(cve.layout.height)) return null;
            var rs = cve.resolvedStyle;
            return (result.CssOf(cve),
                cve.layout.width - rs.paddingLeft - rs.paddingRight - rs.borderLeftWidth - rs.borderRightWidth,
                cve.layout.height - rs.paddingTop - rs.paddingBottom - rs.borderTopWidth - rs.borderBottomWidth);
        };
        result.InstallContainers();

        // Per page, not per process. The applier names an unsupported declaration once so a page
        // using it on forty elements says so once; kept across pages it made every page after the
        // first look clean.
        StyleApplier.ForgetReported();

        var doc = HtmlParser.Parse(source, Warn);
        result.Document = doc;
        result.Source = source;

        var rules = new List<CssRule>();
        var script = new StringBuilder();
        result.ViewportWidth = ReadViewport(doc);
        CssParser.ViewportWidth = result.ViewportWidth > 0f ? result.ViewportWidth : 460f;
        CssParser.ViewportHeight = CssParser.ViewportWidth * SurfaceAspect;
        Counters.Clear();
        AfterDecls.Clear();
        lock (Tweens.Shared)
        {
            Tweens.AllowDiscrete.Clear();
            Tweens.PendingHide.Clear();
        }
        StyleApplier.MixedCalc.Clear();
        StyleApplier.BaselineRows.Clear();
        CssParser.CounterStyles.Clear();
        CssParser.FontFaces.Clear();
        CssParser.Imports.Clear();
        CssParser.PropertyInitials.Clear();
        CssParser.StartingRules.Clear();
        CssParser.UsedAttributes.Clear();
        CssParser.ForgetReported();
        _placeholdersSeen = false;
        _building = true;
        try { return BuildInner(source, font, result, doc, rules, script, Warn); }
        finally { _building = false; }
    }

    private static Result BuildInner(string source, FaceData? font, Result result, HtmlNode doc, List<CssRule> rules, StringBuilder script, Action<string> Warn)
    {
        Collect(doc, rules, script, result.Keyframes, Warn, result);
        result.StartingRules.AddRange(CssParser.StartingRules);
        result.AttributeSelectors.UnionWith(CssParser.UsedAttributes);
        for (var i = 0; i < result.ExternalStyles.Count; i++) result.ExternalStyles[i] = ResolveUrl(result.ExternalStyles[i], result);
        for (var i = 0; i < result.ExternalScripts.Count; i++) result.ExternalScripts[i] = ResolveUrl(result.ExternalScripts[i], result);
        result.ExternalImports.AddRange(CssParser.Imports);
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
        root.style.fontSize = 16; // a browser's default, so a page that names no font-size still has text
        root.style.whiteSpace = WhiteSpace.Normal;
        if (font != null)
            root.style.face = font;
        result.Root = root;

        result.NodeOf[root] = body;
        result.ById["body"] = root;
        ApplyStyles(root, body, rules, result);
        foreach (var child in body.Children)
            Append(root, child, rules, result);
        ApplyGap(root, result.CssOf(root), result);

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
        if (node.Tag == "base" && node.Attr("href") is { } baseHref && result != null)
        {
            result.BaseUrl ??= baseHref.Trim();
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
            if (type == "module" && result != null)
            {
                var text = new StringBuilder();
                foreach (var c in node.Children) text.Append(c.Text).Append('\n');
                var urls = ImportUrls(text.ToString(), out var defaults);
                if (urls.Count > 0)
                {
                    // `import x from url`: the module's `export default` lands in __default; bind it to the name
                    result.Modules.Add((urls, defaults + StripModuleSyntax(text.ToString())));
                    return;
                }
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
    /// <summary>The http(s) urls a module imports, in order; relative specifiers have no base here and are skipped.</summary>
    private static List<string> ImportUrls(string js, out string defaults)
    {
        var urls = new List<string>();
        var sb = new StringBuilder();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(js, @"^\s*import\s+(?:([A-Za-z_$][\w$]*)\s*(?:,\s*\{[^}]*\})?\s+from\s+|\{[^}]*\}\s+from\s+|\*\s+as\s+\w+\s+from\s+)?[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.Multiline))
        {
            var url = m.Groups[2].Value;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            urls.Add(url);
            if (m.Groups[1].Success) sb.Append("var ").Append(m.Groups[1].Value).Append(" = typeof __default !== 'undefined' ? __default : undefined;\n");
        }
        defaults = sb.ToString();
        return urls;
    }

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
        ApplyGap(parent, result.CssOf(parent), result);
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
        ApplyGap(parent, result.CssOf(parent), result);
    }

    /// <summary>The node's markup back as HTML: its children (inner) or itself with them (outer). Generated content, markers and synthetic ids are left out.</summary>
    internal static string ToHtml(HtmlNode node, bool outer, bool keepIds = false)
    {
        var sb = new StringBuilder();
        if (outer) WriteNode(sb, node, keepIds); else foreach (var c in node.Children) WriteNode(sb, c, keepIds);
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, HtmlNode node, bool keepIds = false)
    {
        if (node.IsText) { sb.Append(EscapeHtml(node.Text)); return; }
        if (node.Attr("data-pseudo") != null || node.Attr("data-marker") != null) return;
        sb.Append('<').Append(node.Tag);
        foreach (var kv in node.Attributes)
        {
            if (kv.Key == "data-listed" || kv.Key == "data-control") continue;
            // synthetic ids are hidden from a script reading innerHTML, but kept when the markup
            // goes to the main thread: they are how an in-place update recognises its elements
            if (kv.Key == "id" && !keepIds && kv.Value.StartsWith("__", StringComparison.Ordinal)) continue;
            sb.Append(' ').Append(kv.Key);
            if (kv.Value.Length > 0) sb.Append("=\"").Append(EscapeHtml(kv.Value)).Append('"');
        }
        sb.Append('>');
        if (HtmlParser.Void.Contains(node.Tag!)) return;
        foreach (var c in node.Children) WriteNode(sb, c, keepIds);
        sb.Append("</").Append(node.Tag).Append('>');
    }

    private static string EscapeHtml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>Script removal: the element, its node, and every id under it.</summary>
    internal static void Remove(VisualElement ve, Result result)
    {
        var parent = ve.parent;
        if (result.NodeOf.TryGetValue(ve, out var node))
        {
            node.Parent?.Children.Remove(node);
            result.NodeOf.Remove(ve);
        }
        Forget(ve, result);
        result.PruneForgotten();
        ve.RemoveFromHierarchy();
        if (parent != null && result.GapContainers.Contains(parent)) ApplyGap(parent, result.CssOf(parent), result);
    }

    private static void Forget(VisualElement ve, Result result)
    {
        if (!string.IsNullOrEmpty(ve.name) && result.ById.TryGetValue(ve.name, out var same) && same == ve)
            result.ById.Remove(ve.name);
        result.ForgetElement(ve);
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

        if (node.Attr("data-pseudo") == "after" && node.Attr("data-content") is { } afterContent && node.Children.Count == 0)
        {
            // an ::after is built last among its siblings: its counters and text are resolved now
            var owner = node.Parent ?? node;
            if (AfterDecls.TryGetValue(node, out var afterDecls)) ApplyCounters(node, owner, afterDecls);
            var afterText = GeneratedText(afterContent, owner, rules);
            node.Attributes.Remove("data-content");
            if (afterText == null) return;
            node.Children.Add(new HtmlNode { Text = afterText, Parent = node });
        }

        AddGenerated(node, rules);

        if (node.Tag == "tr")
        {
            // a row on its own (built by a script into a tbody): laid out as a row of cells
            var n = 0;
            foreach (var cell in node.Children) if (cell.Tag == "td" || cell.Tag == "th") n += Span(cell);
            AppendRow(parent, node, Math.Max(1, n), rules, result);
            return;
        }
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
            var svg = BuildSvg(node, rules, result);
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

        if (node.Tag == "picture")
        {
            // the first <source> whose media query matches the design width supplies the img's src
            HtmlNode? img = null; string? chosen = null;
            foreach (var c in node.Children)
            {
                if (c.Tag == "img") img = c;
                else if (c.Tag == "source" && chosen == null && (c.Attr("media") == null || CssParser.MediaMatches(c.Attr("media")!)) && (c.Attr("srcset") ?? c.Attr("src")) is { } set)
                    chosen = set.Split(',')[0].Trim().Split(' ')[0];
            }
            if (img != null)
            {
                if (chosen != null) img.Attributes["src"] = chosen;
                Append(parent, img, rules, result);
            }
            return;
        }
        if (node.Tag == "img" && node.Attr("usemap") != null)
            node.Attributes["data-click"] = "1"; // an image map: the click lands on the image, the surface finds the area
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
            // a canvas is its bitmap size (300x150 by default) unless CSS sizes the box
            var ccss = result.CssOf(cv);
            if (!ccss.ContainsKey("width")) cv.style.width = cv.CanvasWidth;
            if (!ccss.ContainsKey("height")) cv.style.height = cv.CanvasHeight;
            parent.Add(cv);
            return;
        }

        VisualElement ve;
        var mixed = false;
        // display: flex / grid blockifies the children: spans in it are items with their own boxes, not one line of text
        var itemsDisplay = CascadedValue(node, rules, "display") is { } dv0 && dv0.Trim().ToLowerInvariant() is "flex" or "inline-flex" or "grid" or "inline-grid"
                           && node.Children.Exists(c => !c.IsText);
        if (IsTextLike(node) && node.Children.TrueForAll(c => c.IsText) && TextColumns(node, rules) is var textColumns && textColumns > 1)
        {
            // column-count on plain text: the words shared out over the columns as block children.
            // ponytail: an equal word count per column, not balanced by height; inline tags inside keep one column
            var words = string.Join(" ", node.Children.ConvertAll(c => c.Text)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            node.Children.Clear();
            var perColumn = (words.Length + textColumns - 1) / textColumns;
            for (var c = 0; c < textColumns; c++)
            {
                var col = new HtmlNode { Tag = "div", Parent = node };
                col.Children.Add(new HtmlNode { Text = string.Join(" ", words, Math.Min(words.Length, c * perColumn), Math.Max(0, Math.Min(perColumn, words.Length - c * perColumn))), Parent = col });
                node.Children.Add(col);
            }
        }
        if (IsTextLike(node) && !itemsDisplay)
        {
            ve = new Label(RichText(node, rules));
            // a flex box holding only text: its justify-content / align-items place the text (a button's label)
            var fdisp = CascadedValue(node, rules, "display");
            if (fdisp is "flex" or "inline-flex")
            {
                var column = CascadedValue(node, rules, "flex-direction") is { } fdir && fdir.StartsWith("column", StringComparison.Ordinal);
                var main = CascadedValue(node, rules, "justify-content") ?? "flex-start";
                var cross = CascadedValue(node, rules, "align-items") ?? "stretch";
                var horiz = column ? cross : main;
                var vert = column ? main : cross;
                var right = horiz is "center" ? 1 : horiz is "flex-end" or "end" ? 2 : 0;
                var middle = vert is "center" ? 1 : vert is "flex-end" or "end" ? 2 : 0;
                ve.style.unityTextAlign = (right, middle) switch
                {
                    (1, 1) => TextAnchor.MiddleCenter, (1, 2) => TextAnchor.LowerCenter, (1, _) => TextAnchor.UpperCenter,
                    (2, 1) => TextAnchor.MiddleRight, (2, 2) => TextAnchor.LowerRight, (2, _) => TextAnchor.UpperRight,
                    (_, 1) => TextAnchor.MiddleLeft, (_, 2) => TextAnchor.LowerLeft, _ => TextAnchor.UpperLeft,
                };
                if (right == 1) result.CssOf(ve)["text-align"] = "center"; else if (right == 2) result.CssOf(ve)["text-align"] = "right";
            }
        }
        else if (Inline.Contains(node.Tag!) && IsInlineOnly(node, rules) && IsTextLikeIgnoringSelf(node) && !itemsDisplay)
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
        TagDefaults(ve, node);
        if (node.Tag is "pre" or "code" or "kbd" or "samp")
        {
            // A monospace default that a page rule can still override: it is written into the
            // cascade record before the rules, and the layout face follows.
            result.CssOf(ve)["font-family"] = "monospace";
            var mono = FontLibrary.Get("code");
            if (mono != null) ve.style.face = mono;
        }
        ApplyStyles(ve, node, rules, result);
        {
            var tcss = result.CssOf(ve);
            if ((tcss.TryGetValue("transition-behavior", out var tb) && tb.Contains("allow-discrete")) || (tcss.TryGetValue("transition", out var tr0) && tr0.Contains("allow-discrete")))
                lock (Tweens.Shared) Tweens.AllowDiscrete.Add(ve);
        }
        if (node.Tag == "dialog")
        {
            // the tag default centres it (left/top 50% + translate -50%); a page that places it keeps its own numbers
            var dcss = result.CssOf(ve);
            if ((dcss.ContainsKey("left") || dcss.ContainsKey("top") || dcss.ContainsKey("right") || dcss.ContainsKey("bottom") || dcss.ContainsKey("inset") || dcss.ContainsKey("margin"))
                && !dcss.ContainsKey("translate") && !dcss.ContainsKey("transform"))
                ve.style.translate = new Translate(0, 0);
        }
        if (mixed && result.CssOf(ve).TryGetValue("display", out var dsp) && dsp.Trim().ToLowerInvariant() is "flex" or "inline-flex" or "grid" or "inline-grid")
        {
            // a flex or grid container whose children happen to be inline: they are items, not a line box
            mixed = false;
            if (!result.CssOf(ve).ContainsKey("flex-wrap")) ve.style.flexWrap = UnityEngine.UIElements.Wrap.NoWrap;
            if (!result.CssOf(ve).ContainsKey("align-items")) ve.style.alignItems = Align.Stretch;
        }
        parent.Add(ve);
        if (node.Tag == "dialog" && node.Attr("open") == null)
            ve.style.display = DisplayStyle.None;
        if (node.Attr("hidden") != null)
            ve.style.display = DisplayStyle.None;
        if (node.Attr("popover") != null && node.Attr("data-popover-open") == null)
            ve.style.display = DisplayStyle.None;

        if (ve is Label)
            return;

        if (!mixed)
        {
            var list = node.Tag == "ul" || node.Tag == "ol";
            // <ol start=5> begins there; <li value=9> moves the count and the rest follow it
            var ordinal = node.Tag == "ol" && int.TryParse(node.Attr("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var startAt) ? startAt - 1 : 0;
            if (node.Tag == "details")
                AddDisclosure(node, rules);
            // Inline content between blocks (text, <b>, a ::before, a span) flows as one line
            // box, the way a browser wraps it in an anonymous block: consecutive inline
            // children are gathered into a synthetic span and built as one row. The children
            // keep their real parent so selectors still match.
            var itemsContainer = result.CssOf(ve).TryGetValue("display", out var disp0) && disp0.Trim().ToLowerInvariant() is "flex" or "inline-flex" or "grid" or "inline-grid";
            HtmlNode? lineRun = null;
            void CloseRun()
            {
                if (lineRun == null) return;
                Append(ve, lineRun, rules, result);
                lineRun = null;
            }
            foreach (var child in node.Children)
            {
                if (list && child.Tag == "li")
                {
                    if (int.TryParse(child.Attr("value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)) ordinal = at - 1;
                    AddMarker(child, node, result.CssOf(ve), ++ordinal, rules);
                }
                var inline = child.IsText ? child.Text.Trim().Length > 0 : (Inline.Contains(child.Tag!) && !Blockified(child, rules) && IsInlineOnly(child, rules)) || (child.Attr("data-pseudo") is { } dp && dp != "details-content") || child.Attr("data-marker") != null;
                // the children of a flex or grid container are items, never gathered into a line box (CSS blockifies them)
                if (inline && !itemsContainer && node.Tag is not ("table" or "tr" or "ul" or "ol" or "select" or "svg"))
                {
                    lineRun ??= new HtmlNode { Tag = "span", Parent = node };
                    lineRun.Attributes["data-run"] = "1";
                    lineRun.Children.Add(child);
                    continue;
                }
                if (child.IsText)
                {
                    // a text node directly in a flex or grid container is an anonymous item of its own
                    if (itemsContainer && child.Text.Trim().Length > 0)
                    {
                        var item = new HtmlNode { Tag = "span", Parent = node };
                        item.Attributes["data-run"] = "1";
                        item.Children.Add(child);
                        Append(ve, item, rules, result);
                    }
                    continue;
                }
                CloseRun();
                Append(ve, child, rules, result);
            }
            CloseRun();
            if (node.Tag == "details")
                ShowDetails(ve, node, result);
            ApplyGap(ve, result.CssOf(ve), result);
            OrderChildren(ve, result);
            Flow(parent, ve, result);
            return;
        }

        // Merge runs between id-bearing inline elements into single labels so "a <b>b</b>"
        // stays one label, and spaces at run edges survive as spaces.
        var run = new StringBuilder();
        var afterLetter = false;
        void FlushRun()
        {
            var t = run.ToString();
            run.Clear();
            if (t.Trim().Length == 0)
                return;
            var l = new Label(t.Trim());
            if (t.StartsWith(" ", StringComparison.Ordinal)) l.style.marginLeft = 4;
            if (t.EndsWith(" ", StringComparison.Ordinal)) l.style.marginRight = 4;
            if (afterLetter)
            {
                // the text after a drop letter or a list marker shares the line with it: content
                // basis (so a shrink-wrapped list is as wide as its text), shrinkable and wrapping
                // inside its own box when the line is narrower; the row itself does not wrap.
                l.style.flexGrow = 1; l.style.flexShrink = 1; l.style.flexBasis = StyleKeyword.Auto; l.style.minWidth = 0; l.style.whiteSpace = WhiteSpace.Normal;
                ve.style.flexWrap = UnityEngine.UIElements.Wrap.NoWrap;
                afterLetter = false;
            }
            ve.Add(l);
        }
        foreach (var child in node.Children)
        {
            if (!child.IsText && KeepsOwnElement(child))
            {
                FlushRun();
                var before = ve.childCount;
                Append(ve, child, rules, result);
                if (ve.childCount > before)
                {
                    var built = ve[ve.childCount - 1];
                    var own = OwnText(child);
                    if (own.EndsWith(" ", StringComparison.Ordinal)) built.style.marginRight = 4;
                    if (own.StartsWith(" ", StringComparison.Ordinal)) built.style.marginLeft = 4;
                }
                // the text after a drop letter or a list marker fills the line and wraps beside it
                afterLetter = child.Attr("data-pseudo") == "first-letter" || child.Attr("data-marker") != null || child.Attr("data-marker-image") != null;
                continue;
            }
            AppendRich(run, child, rules);
        }
        FlushRun();
        // a floated inline box in a sentence (float: right on a span) still floats
        OrderChildren(ve, result);
        Flow(parent, ve, result);
    }

    /// <summary>
    /// Inline SVG. viewBox, width/height attributes, preserveAspectRatio="none"; shapes are
    /// collected flat (a g's children are hoisted, its own attributes are not inherited).
    /// </summary>
    private static SvgElement BuildSvg(HtmlNode node, List<CssRule> rules, Result result)
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

        CollectShapes(node, svg, result, rules);
        return svg;
    }

    /// <summary>SVG presentation properties that a group hands down to its content.</summary>
    private static readonly string[] SvgInherited =
    {
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin", "stroke-dasharray", "stroke-dashoffset", "stroke-miterlimit",
        "font-family", "font-size", "font-weight", "font-style", "text-anchor", "dominant-baseline", "letter-spacing", "color",
    };

    /// <summary>
    /// The bare path data inside a CSS <c>d: path("…")</c>, or null for a value with no attribute form.
    /// </summary>
    /// <remarks>
    /// The CSS property and the SVG attribute of the same name are spelled differently - the
    /// attribute is the data itself, the property wraps it in <c>path()</c> - and the cascade copies
    /// property over attribute by name. So the wrapper travelled all the way to the scene and drew
    /// nothing. Returning null for `none` or a `shape()` leaves the attribute standing, which is
    /// what a browser does with a value it cannot use.
    /// </remarks>
    private static string? PathValue(string v)
    {
        v = v.Trim();
        if (!v.StartsWith("path(", StringComparison.OrdinalIgnoreCase)) return null;
        var close = v.LastIndexOf(')');
        if (close <= 5) return null;
        var inner = v.Substring(5, close - 5).Trim();
        // path() may carry a fill-rule first: path(evenodd, "M …"). The rule is `fill-rule`'s job.
        var comma = inner.IndexOf(',');
        if (comma > 0 && inner.IndexOf('"') > comma) inner = inner.Substring(comma + 1).Trim();
        return inner.Length >= 2 && (inner[0] == '"' || inner[0] == '\'') && inner[inner.Length - 1] == inner[0]
            ? inner.Substring(1, inner.Length - 2)
            : inner;
    }

    /// <summary>A shape's own bounding box in viewBox units, for transform-box: fill-box.</summary>
    private static Rect ShapeBox(HtmlNode c)
    {
        float N(string a, float d = 0f) => c.Attr(a) is { } v ? StyleApplier.Num(v) : d;
        switch (c.Tag)
        {
            case "rect": case "image": case "use": return new Rect(N("x"), N("y"), N("width"), N("height"));
            case "circle": return new Rect(N("cx") - N("r"), N("cy") - N("r"), 2f * N("r"), 2f * N("r"));
            case "ellipse": return new Rect(N("cx") - N("rx"), N("cy") - N("ry"), 2f * N("rx"), 2f * N("ry"));
            case "line": return Rect.MinMaxRect(Mathf.Min(N("x1"), N("x2")), Mathf.Min(N("y1"), N("y2")), Mathf.Max(N("x1"), N("x2")), Mathf.Max(N("y1"), N("y2")));
            case "polyline": case "polygon":
            {
                var pts = (c.Attr("points") ?? string.Empty).Split(new[] { ' ', ',', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (var i = 0; i + 1 < pts.Length; i += 2) { var px = StyleApplier.Num(pts[i]); var py = StyleApplier.Num(pts[i + 1]); x0 = Mathf.Min(x0, px); y0 = Mathf.Min(y0, py); x1 = Mathf.Max(x1, px); y1 = Mathf.Max(y1, py); }
                return pts.Length >= 2 ? Rect.MinMaxRect(x0, y0, x1, y1) : default;
            }
            case "path":
            {
                var pts = VectorEmitter.FlattenPath(c.Attr("d") ?? string.Empty);
                if (pts.Count == 0) return default;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
                return Rect.MinMaxRect(x0, y0, x1, y1);
            }
            default: return default;
        }
    }

    /// <summary>
    /// Walks the svg subtree the way a browser resolves it: presentation attributes, then CSS
    /// rules matching the node, then inline style; inherited properties flow down through
    /// groups, `opacity` multiplies, `transform` composes into one matrix per shape (written
    /// as `__m`). `use` inlines what it references (a `symbol` scales to the use's box),
    /// `clipPath` becomes a shape holding its children, `text` and `image` become shapes,
    /// content inside `defs` draws only when referenced.
    /// </summary>
    private static void CollectShapes(HtmlNode node, SvgElement svg, Result result, List<CssRule> rules, Dictionary<string, string>? inherited = null, float[]? matrix = null, Dictionary<string, HtmlNode>? byId = null, int depth = 0, bool inDefs = false, SvgShape? clipTarget = null)
    {
        if (byId == null)
        {
            byId = new Dictionary<string, HtmlNode>(StringComparer.Ordinal);
            IndexIds(node, byId);
        }
        inherited ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (depth > 12) return;
        foreach (var c in node.Children)
        {
            if (c.IsText)
                continue;
            var tag = c.Tag!;
            if (tag is "title" or "desc" or "metadata" or "script" or "style")
                continue;
            if (tag is "lineargradient" or "radialgradient")
            {
                var shape = new SvgShape { Tag = tag == "lineargradient" ? "linearGradient" : "radialGradient", Owner = svg };
                foreach (var kv in c.Attributes)
                    shape.Attributes[kv.Key] = kv.Value;
                var stops = new StringBuilder();
                foreach (var st in c.Children)
                {
                    if (st.IsText || st.Tag != "stop") continue;
                    var style = st.Attr("style");
                    var colour = st.Attr("stop-color");
                    var opacity = st.Attr("stop-opacity");
                    if (style != null)
                        foreach (var d in CssParser.ParseDeclarations(style))
                        {
                            if (d.Name == "stop-color") colour = d.Value;
                            else if (d.Name == "stop-opacity") opacity = d.Value;
                        }
                    stops.Append(st.Attr("offset") ?? "0").Append('|').Append(colour ?? "black").Append('|').Append(opacity ?? string.Empty).Append(';');
                }
                shape.Attributes["stops"] = stops.ToString();
                svg.Shapes.Add(shape);
                continue;
            }

            // the node's own presentation, in cascade order
            var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in c.Attributes)
                own[kv.Key] = kv.Value;
            var matched = new List<(int spec, int order, CssRule rule)>();
            foreach (var rule in rules)
            {
                var best = -1;
                foreach (var sel in rule.Selectors)
                    if (sel.Matches(c)) best = Math.Max(best, sel.Specificity);
                if (best >= 0) matched.Add((best, rule.Order, rule));
            }
            matched.Sort((a, b) => a.spec != b.spec ? a.spec.CompareTo(b.spec) : a.order.CompareTo(b.order));
            foreach (var m in matched)
                foreach (var d in m.rule.Declarations)
                {
                    var v = HasFn(d.Value) ? TryResolveVars(d.Value, c) : d.Value;
                    if (v != null) own[d.Name] = v;
                }
            if (c.Attr("style") is { } inlineStyle)
                foreach (var d in CssParser.ParseDeclarations(inlineStyle))
                    own[d.Name] = d.Value;
            if (own.TryGetValue("display", out var disp) && disp.Trim() == "none") continue;
            if (own.TryGetValue("visibility", out var vis) && vis.Trim() == "hidden") continue;

            // effective: inherited, then own; opacity multiplies; transform composes
            var eff = new Dictionary<string, string>(inherited, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in own)
                if (Array.IndexOf(SvgInherited, kv.Key.ToLowerInvariant()) >= 0) eff[kv.Key] = kv.Value;
            // a clip on a group clips everything under it; the nearest wins
            if (own.TryGetValue("clip-path", out var gcp) && UrlId(gcp) is { } gcid) eff["__clip"] = gcid;
            if (own.TryGetValue("opacity", out var op))
            {
                var o = StyleApplier.Num(op);
                eff["opacity"] = (inherited.TryGetValue("opacity", out var io) ? StyleApplier.Num(io) * o : o).ToString("0.###", CultureInfo.InvariantCulture);
            }
            var m2 = matrix;
            if (own.TryGetValue("transform", out var tr) && SvgTransform(tr) is { } tm)
            {
                // transform-origin (CSS): about a point of the view box, or of the shape's own box with transform-box: fill-box
                if (own.TryGetValue("transform-origin", out var torigin))
                {
                    var fillBox = own.TryGetValue("transform-box", out var tbox) && tbox.Trim() == "fill-box";
                    var box = fillBox ? ShapeBox(c) : svg.ViewBox;
                    var parts = torigin.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    float Along(string p, float start, float size) => p switch
                    {
                        "left" or "top" => start, "center" => start + size * 0.5f, "right" or "bottom" => start + size,
                        _ => p.EndsWith("%", StringComparison.Ordinal) ? start + StyleApplier.Num(p) / 100f * size : start + StyleApplier.Num(p),
                    };
                    var oxp = parts.Length > 0 ? Along(parts[0], box.x, box.width) : box.x + box.width * 0.5f;
                    var oyp = parts.Length > 1 ? Along(parts[1], box.y, box.height) : box.y + box.height * 0.5f;
                    tm = MulMatrix(MulMatrix(new[] { 1f, 0f, 0f, 1f, oxp, oyp }, tm), new[] { 1f, 0f, 0f, 1f, -oxp, -oyp });
                }
                m2 = matrix == null ? tm : MulMatrix(matrix, tm);
            }

            switch (tag)
            {
                case "g": case "a": case "switch":
                    CollectShapes(c, svg, result, rules, eff, m2, byId, depth + 1, inDefs, clipTarget);
                    break;
                case "defs":
                    CollectShapes(c, svg, result, rules, eff, m2, byId, depth + 1, true, clipTarget);
                    break;
                case "symbol":
                    if (!inDefs && depth > 0) CollectShapes(c, svg, result, rules, eff, m2, byId, depth + 1, inDefs, clipTarget);
                    break; // a symbol draws only through use
                case "marker":
                {
                    // a marker draws only at the vertices of the shapes that reference it (marker-start/mid/end)
                    var mk = new SvgShape { Tag = "marker", Owner = svg, Children = new List<SvgShape>() };
                    foreach (var kv in c.Attributes) mk.Attributes[kv.Key] = kv.Value;
                    var beforeMk = svg.Shapes.Count;
                    CollectShapes(c, svg, result, rules, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null, byId, depth + 1, false, mk);
                    svg.Shapes.Insert(beforeMk, mk);
                    break;
                }
                case "clippath":
                {
                    var cp = new SvgShape { Tag = "clipPath", Owner = svg, Children = new List<SvgShape>() };
                    if (c.Attr("id") is { } cid) cp.Attributes["id"] = cid;
                    var before = svg.Shapes.Count;
                    CollectShapes(c, svg, result, rules, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), m2, byId, depth + 1, false, cp);
                    svg.Shapes.Insert(before, cp);
                    break;
                }
                case "use":
                {
                    var href = (c.Attr("href") ?? c.Attr("xlink:href") ?? string.Empty).Trim();
                    if (!href.StartsWith("#", StringComparison.Ordinal) || !byId.TryGetValue(href.Substring(1), out var target) || depth > 8)
                        break;
                    var ux = StyleApplier.Num(c.Attr("x") ?? "0"); var uy = StyleApplier.Num(c.Attr("y") ?? "0");
                    var um = MulMatrix(m2 ?? Identity(), new[] { 1f, 0f, 0f, 1f, ux, uy });
                    if (target.Tag == "symbol" && target.Attr("viewBox") is { } svb)
                    {
                        var n = svb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                        if (n.Length == 4 && c.Attr("width") is { } uw && c.Attr("height") is { } uh)
                        {
                            var vbw = StyleApplier.Num(n[2]); var vbh = StyleApplier.Num(n[3]);
                            var sc = Mathf.Min(StyleApplier.Num(uw) / Mathf.Max(1f, vbw), StyleApplier.Num(uh) / Mathf.Max(1f, vbh));
                            um = MulMatrix(um, new[] { sc, 0f, 0f, sc, -StyleApplier.Num(n[0]) * sc, -StyleApplier.Num(n[1]) * sc });
                        }
                    }
                    // the use's own presentation is what the referenced content inherits
                    var wrapper = new HtmlNode { Tag = "g", Parent = c };
                    wrapper.Children.Add(target);
                    CollectShapes(wrapper, svg, result, rules, eff, um, byId, depth + 1, false, clipTarget);
                    break;
                }
                case "text":
                {
                    if (inDefs && clipTarget == null) break;
                    var text = SvgText(c);
                    if (text.Length == 0) break;
                    var shape = new SvgShape { Tag = "text", Owner = svg };
                    foreach (var kv in eff) shape.Attributes[kv.Key] = kv.Value;
                    foreach (var kv in c.Attributes) if (!shape.Attributes.ContainsKey(kv.Key)) shape.Attributes[kv.Key] = kv.Value;
                    // the non-inherited presentation from CSS rules and inline style (geometry, paint-order,
                    // markers, vector-effect...), over the attribute of the same name, as the cascade says
                    foreach (var kv in own)
                        if (Array.IndexOf(SvgInherited, kv.Key.ToLowerInvariant()) < 0 && kv.Key.ToLowerInvariant() is not ("opacity" or "transform" or "transform-origin" or "transform-box" or "style" or "display" or "visibility" or "clip-path" or "class" or "id"))
                            shape.Attributes[kv.Key] = kv.Value; // x, y, dx, dy
                    shape.Attributes["__text"] = text;
                    if (m2 != null) shape.Attributes["__m"] = MatrixText(m2);
                                        svg.Shapes.Add(shape);
                    if (c.Attr("id") is { } tid) result.Shapes[tid] = shape;
                    break;
                }
                case "image":
                {
                    if (inDefs && clipTarget == null) break;
                    var shape = new SvgShape { Tag = "image", Owner = svg };
                    foreach (var kv in eff) shape.Attributes[kv.Key] = kv.Value;
                    foreach (var kv in c.Attributes) if (!shape.Attributes.ContainsKey(kv.Key)) shape.Attributes[kv.Key] = kv.Value; // x, y, width, height
                    shape.Attributes["href"] = c.Attr("href") ?? c.Attr("xlink:href") ?? string.Empty;
                    if (m2 != null) shape.Attributes["__m"] = MatrixText(m2);
                    svg.Shapes.Add(shape);
                    break;
                }
                case "line": case "polyline": case "polygon": case "rect": case "circle": case "ellipse": case "path":
                {
                    if (inDefs && clipTarget == null) break;
                    var shape = new SvgShape { Tag = tag, Owner = svg };
                    foreach (var kv in eff) shape.Attributes[kv.Key] = kv.Value;
                    foreach (var kv in c.Attributes) if (!shape.Attributes.ContainsKey(kv.Key)) shape.Attributes[kv.Key] = kv.Value;
                    // CSS rules and inline style over the attribute of the same name (geometry, paint-order, markers...), as the cascade says
                    foreach (var kv in own)
                        if (Array.IndexOf(SvgInherited, kv.Key.ToLowerInvariant()) < 0 && kv.Key.ToLowerInvariant() is not ("opacity" or "transform" or "transform-origin" or "transform-box" or "style" or "display" or "visibility" or "clip-path" or "class" or "id"))
                            // `d` is the one geometry property whose CSS spelling differs from its
                            // attribute: the attribute is bare path data, the property wraps it in
                            // path(). Copied verbatim it reached the scene as d="path( M 0 0 ... )"
                            // and drew nothing. Anything else - `none`, a shape() - has no attribute
                            // form, so dropping it leaves the attribute standing, which is what CSS
                            // does with a value it cannot use.
                            shape.Attributes[kv.Key] = kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase)
                                ? PathValue(kv.Value) ?? shape.Attr("d") ?? string.Empty
                                : kv.Value;
                    if (m2 != null) shape.Attributes["__m"] = MatrixText(m2);
                                        if (clipTarget != null) clipTarget.Children!.Add(shape);
                    else svg.Shapes.Add(shape);
                    var id = c.Attr("id");
                    if (id != null && clipTarget == null)
                        result.Shapes[id] = shape;
                    break;
                }
                default:
                    result.Warnings.Add($"html: svg <{tag}> not supported");
                    break;
            }
        }
    }

    private static void IndexIds(HtmlNode node, Dictionary<string, HtmlNode> byId)
    {
        foreach (var c in node.Children)
        {
            if (c.IsText) continue;
            if (c.Attr("id") is { } id && !byId.ContainsKey(id)) byId[id] = c;
            IndexIds(c, byId);
        }
    }

    /// <summary>The text of a &lt;text&gt; with its tspans, whitespace collapsed; a tspan with its own x/y starts a new line.</summary>
    private static string SvgText(HtmlNode text)
    {
        var sb = new StringBuilder();
        void Walk(HtmlNode n)
        {
            foreach (var c in n.Children)
            {
                if (c.IsText) { sb.Append(c.Text); continue; }
                if (c.Tag == "tspan" && (c.Attr("x") != null || c.Attr("y") != null) && sb.Length > 0) sb.Append('\n');
                // a tspan's own fill / weight / style travel as rich-text tags
                var open = new StringBuilder(); var close = new StringBuilder();
                var style = c.Attr("style");
                string? fill = c.Attr("fill"), weight = c.Attr("font-weight"), fstyle = c.Attr("font-style");
                if (style != null)
                    foreach (var d in CssParser.ParseDeclarations(style))
                    {
                        if (d.Name == "fill") fill = d.Value; else if (d.Name == "font-weight") weight = d.Value; else if (d.Name == "font-style") fstyle = d.Value;
                    }
                if (fill != null && StyleApplier.TryColor(fill, out var fc)) { open.Append("<color=").Append(VectorEmitter.Hex(fc)).Append('>'); close.Insert(0, "</color>"); }
                if (weight != null && (weight.Trim() is "bold" or "bolder" || (StyleApplier.IsNumber(weight.Trim()) && StyleApplier.Num(weight) >= 600))) { open.Append("<b>"); close.Insert(0, "</b>"); }
                if (fstyle != null && fstyle.Trim() is "italic" or "oblique") { open.Append("<i>"); close.Insert(0, "</i>"); }
                sb.Append(open);
                Walk(c);
                sb.Append(close);
            }
        }
        Walk(text);
        return sb.ToString().Trim();
    }

    private static string? UrlId(string v)
    {
        var u = v.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (u < 0) return null;
        var close = v.IndexOf(')', u);
        if (close < 0) return null;
        var id = v.Substring(u + 4, close - u - 4).Trim().Trim('"', '\'');
        return id.StartsWith("#", StringComparison.Ordinal) ? id.Substring(1) : id;
    }

    private static float[] Identity() => new[] { 1f, 0f, 0f, 1f, 0f, 0f };

    internal static float[] MulMatrix(float[] m, float[] n)
    {
        // m then n: points go through n first (SVG composes left to right as nested groups)
        return new[]
        {
            m[0] * n[0] + m[2] * n[1], m[1] * n[0] + m[3] * n[1],
            m[0] * n[2] + m[2] * n[3], m[1] * n[2] + m[3] * n[3],
            m[0] * n[4] + m[2] * n[5] + m[4], m[1] * n[4] + m[3] * n[5] + m[5],
        };
    }

    internal static string MatrixText(float[] m) => string.Join(",", Array.ConvertAll(m, v => v.ToString("0.####", CultureInfo.InvariantCulture)));

    /// <summary>The SVG transform attribute: translate, rotate (about a point), scale, skewX/Y, matrix, in order.</summary>
    internal static float[]? SvgTransform(string v)
    {
        var m = Identity();
        var any = false;
        foreach (System.Text.RegularExpressions.Match fn in System.Text.RegularExpressions.Regex.Matches(v, @"([a-zA-Z]+)\s*\(([^)]*)\)"))
        {
            var a = fn.Groups[2].Value.Split(new[] { ' ', ',', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            float A(int i) => a.Length > i ? StyleApplier.Num(a[i]) : 0f;
            switch (fn.Groups[1].Value.ToLowerInvariant())
            {
                case "translate": m = MulMatrix(m, new[] { 1f, 0f, 0f, 1f, A(0), A(1) }); break;
                case "scale": m = MulMatrix(m, new[] { A(0), 0f, 0f, a.Length > 1 ? A(1) : A(0), 0f, 0f }); break;
                case "rotate":
                {
                    var r = A(0) * Mathf.Deg2Rad; var cx = A(1); var cy = A(2);
                    if (a.Length > 2) m = MulMatrix(m, new[] { 1f, 0f, 0f, 1f, cx, cy });
                    m = MulMatrix(m, new[] { Mathf.Cos(r), Mathf.Sin(r), -Mathf.Sin(r), Mathf.Cos(r), 0f, 0f });
                    if (a.Length > 2) m = MulMatrix(m, new[] { 1f, 0f, 0f, 1f, -cx, -cy });
                    break;
                }
                case "skewx": m = MulMatrix(m, new[] { 1f, 0f, Mathf.Tan(A(0) * Mathf.Deg2Rad), 1f, 0f, 0f }); break;
                case "skewy": m = MulMatrix(m, new[] { 1f, Mathf.Tan(A(0) * Mathf.Deg2Rad), 0f, 1f, 0f, 0f }); break;
                case "matrix": if (a.Length >= 6) m = MulMatrix(m, new[] { A(0), A(1), A(2), A(3), A(4), A(5) }); break;
                default: continue;
            }
            any = true;
        }
        return any ? m : null;
    }

    /// <summary>Name, classes, id (a synthetic one when the page gave none) and the node map.</summary>
    private static void Register(VisualElement ve, HtmlNode node, Result result)
    {
        var id = node.Attr("id");
        if (id == null)
        {
            id = "__" + node.Tag + (++result.AutoId).ToString(CultureInfo.InvariantCulture);
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
            if (!Inline.Contains(c.Tag!) || Blockified(c))
                return false;
            if (!IsInlineOnly(c))
                return false;
            // An inline element with an id (data-binding target) or a class (styled box,
            // like the mockup's <span class="dia">) keeps its own element, and so does one
            // holding such an element; the parent then becomes a wrapping row (see Append).
            // Bare <b>/<i>/<span> still fold into text.
            if (KeepsOwnElement(c) || HasKeptDescendant(c))
                return false;
            anyText = true;
        }
        return anyText;
    }

    /// <summary>An inline child that stays its own element: it has an id (data target), a class (styled box) or is generated by ::before/::after (usually a box).</summary>
    private static bool KeepsOwnElement(HtmlNode c) => c.Attr("id") != null || c.Attr("class") != null || Blockified(c) || c.Attr("data-pseudo") != null || c.Attr("data-marker") != null
        || c.Tag is "img" or "input" or "button" or "select" or "textarea" or "label";

    /// <summary>
    /// ::before / ::after with `content`: a generated span child carrying data-pseudo, so the
    /// cascade can address it (CssSelector.Matches) and the layout treats it like a classed
    /// inline element. `content` takes quoted strings, attr(name) and none; counters do not exist.
    /// </summary>
    /// <summary>
    /// CSS counters, walked in document order: an entry per counter-reset, scoped to the
    /// element that reset it (valid for its subtree and following siblings), innermost last.
    /// </summary>
    private static readonly List<(string name, HtmlNode scope, int value)> Counters = new();
    /// <summary>True while Build runs: counters advance only then, not when a script rewrites a label later.</summary>
    private static bool _building;
    /// <summary>This page was already told it is an unrendered template, so it is said once, not per declaration.</summary>
    private static bool _placeholdersSeen;
    /// <summary>Counter declarations of ::after pseudo-elements, applied when the pseudo is built (after its siblings).</summary>
    private static readonly Dictionary<HtmlNode, List<CssDeclaration>> AfterDecls = new();

    private static bool AncestorOrSelf(HtmlNode? a, HtmlNode n)
    {
        if (a == null) return true;
        for (var p = n; p != null; p = p.Parent) if (p == a) return true;
        return false;
    }

    /// <summary>The counter declarations of an element or pseudo-element, applied: reset creates, increment/set change the innermost live one (creating it on the element when none is live).</summary>
    private static void ApplyCounters(HtmlNode scope, HtmlNode at, IEnumerable<CssDeclaration> decls)
    {
        Counters.RemoveAll(e => !AncestorOrSelf(e.scope.Parent, at));
        foreach (var d in decls)
        {
            if (d.Name is not ("counter-reset" or "counter-increment" or "counter-set")) continue;
            var parts = d.Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] == "none") continue;
            for (var i = 0; i < parts.Length; i++)
            {
                var name = parts[i];
                if (int.TryParse(name, out _)) continue;
                var n = i + 1 < parts.Length && int.TryParse(parts[i + 1], out var v) ? v : (d.Name == "counter-increment" ? 1 : 0);
                if (d.Name == "counter-reset") { Counters.Add((name, scope, n)); continue; }
                var idx = Counters.FindLastIndex(e => e.name == name);
                if (idx < 0) { Counters.Add((name, scope, n)); continue; }
                var e = Counters[idx];
                Counters[idx] = (e.name, e.scope, d.Name == "counter-set" ? n : e.value + n);
            }
        }
    }

    /// <summary>The element's own cascaded declarations (rules by specificity, then inline), for reads before the element is built.</summary>
    internal static List<CssDeclaration> Cascaded(HtmlNode node, List<CssRule> rules)
    {
        var matched = new List<(int spec, int order, CssRule rule)>();
        foreach (var rule in rules)
        {
            var best = -1;
            foreach (var sel in rule.Selectors)
                if (sel.Matches(node)) best = Math.Max(best, sel.Specificity);
            if (best >= 0) matched.Add((best, rule.Order, rule));
        }
        matched.Sort((x, y) => x.spec != y.spec ? x.spec.CompareTo(y.spec) : x.order.CompareTo(y.order));
        var list = new List<CssDeclaration>();
        foreach (var m in matched) list.AddRange(m.rule.Declarations);
        if (node.Attr("style") is { } inline) list.AddRange(CssParser.ParseDeclarations(inline));
        // Expanded, as ApplyStyles does it: raw declarations left every reader of this blind to
        // shorthands and logical properties, so `columns: 2 60px` reached a column count of none.
        return StyleApplier.Expand(list);
    }

    private static string? CascadedValue(HtmlNode node, List<CssRule> rules, string name)
    {
        string? v = null;
        foreach (var d in Cascaded(node, rules)) if (d.Name == name) v = d.Value.Trim();
        return v;
    }

    private static string CounterText(string name, string style, string separator, bool all)
    {
        var values = new List<int>();
        foreach (var e in Counters) if (e.name == name) values.Add(e.value);
        if (values.Count == 0) values.Add(0);
        if (!all) values = new List<int> { values[values.Count - 1] };
        var parts = new List<string>();
        foreach (var v in values)
            parts.Add(CssParser.CounterStyles.TryGetValue(style, out var custom) && custom.Text(v) is { } customText ? customText : style switch
            {
                "lower-alpha" or "lower-latin" => v >= 1 ? ((char)('a' + (v - 1) % 26)).ToString() : v.ToString(CultureInfo.InvariantCulture),
                "upper-alpha" or "upper-latin" => v >= 1 ? ((char)('A' + (v - 1) % 26)).ToString() : v.ToString(CultureInfo.InvariantCulture),
                "lower-roman" => v >= 1 ? Roman(v).ToLowerInvariant() : "0",
                "upper-roman" => v >= 1 ? Roman(v) : "0",
                "decimal-leading-zero" => v.ToString("00", CultureInfo.InvariantCulture),
                _ => v.ToString(CultureInfo.InvariantCulture),
            });
        return string.Join(separator, parts);
    }

    private static void AddGenerated(HtmlNode node, List<CssRule> rules)
    {
        if (node.IsText || node.Tag == "svg" || node.Attr("data-pseudo") != null)
            return;
        // the element's own counters first, then each pseudo-element's, in order.
        // ponytail: ::after is generated here, before the children, so a counter it
        // shows does not include increments by descendants; move it after the subtree if a page needs that
        ApplyCounters(node, node, Cascaded(node, rules));
        FirstLetter(node, rules);
        foreach (var which in new[] { "before", "after" })
        {
            var probe = new HtmlNode { Tag = "span", Parent = node };
            probe.Attributes["data-pseudo"] = which;
            string? content = null;
            var bestSpec = -1;
            var bestOrder = -1;
            var pseudoDecls = new List<CssDeclaration>();
            foreach (var rule in rules)
            {
                var spec = -1;
                foreach (var sel in rule.Selectors)
                    if (sel.Chain[sel.Chain.Count - 1].PseudoElement == which && sel.Matches(probe))
                        spec = Math.Max(spec, sel.Specificity);
                if (spec < 0) continue;
                foreach (var d in rule.Declarations)
                {
                    if (d.Name is "counter-reset" or "counter-increment" or "counter-set") pseudoDecls.Add(d);
                    if (d.Name != "content") continue;
                    if (spec > bestSpec || (spec == bestSpec && rule.Order > bestOrder)) { content = d.Value; bestSpec = spec; bestOrder = rule.Order; }
                }
            }
            if (content == null) continue;
            if (which == "after")
            {
                // filled when it is built, after the children, so counters they incremented are seen
                probe.Attributes["data-content"] = content;
                AfterDecls[probe] = pseudoDecls;
                node.Children.Add(probe);
                continue;
            }
            ApplyCounters(probe, node, pseudoDecls);
            var text = GeneratedText(content, node, rules);
            if (text == null) continue;
            probe.Children.Add(new HtmlNode { Text = text, Parent = probe });
            node.Children.Insert(0, probe);
        }
    }

    /// <summary>
    /// ::first-letter: when a rule names it, the first letter (with any punctuation before
    /// it) of the element's first text becomes a generated span the cascade styles; the
    /// parent then lays its text out as a wrapping row of labels.
    /// ponytail: the rest of the text wraps below the letter as one label, not around it
    /// </summary>
    private static void FirstLetter(HtmlNode node, List<CssRule> rules)
    {
        var probe = new HtmlNode { Tag = "span", Parent = node };
        probe.Attributes["data-pseudo"] = "first-letter";
        var any = false;
        foreach (var rule in rules)
        {
            foreach (var sel in rule.Selectors)
                if (sel.Chain[sel.Chain.Count - 1].PseudoElement == "first-letter" && sel.Matches(probe)) { any = true; break; }
            if (any) break;
        }
        if (!any) return;
        for (var i = 0; i < node.Children.Count; i++)
        {
            var c = node.Children[i];
            if (!c.IsText) { if (c.Attr("data-pseudo") == "before") continue; return; }
            var t = c.Text;
            var start = 0;
            while (start < t.Length && char.IsWhiteSpace(t[start])) start++;
            if (start >= t.Length) continue;
            var end = start;
            while (end < t.Length && char.IsPunctuation(t[end])) end++;
            if (end < t.Length) end++;
            probe.Children.Add(new HtmlNode { Text = t.Substring(start, end - start), Parent = probe });
            c.Text = t.Substring(end);
            node.Children.Insert(i, probe);
            return;
        }
    }

    /// <summary>The quote a `quotes` pair gives (inherited, level one only), else the usual curly mark.</summary>
    private static string Quote(HtmlNode node, List<CssRule>? rules, bool open)
    {
        for (var n = node; rules != null && n != null; n = n.Parent)
            if (CascadedValue(n, rules, "quotes") is { } q && q != "auto" && q != "none")
            {
                var parts = CssParser.SplitTopLevel(q, ' ');
                if (parts.Count > (open ? 0 : 1)) return parts[open ? 0 : 1].Trim().Trim('"', '\'');
            }
        return open ? "“" : "”";
    }

    /// <summary>The string a `content` value produces, or null for none/normal.</summary>
    private static string? GeneratedText(string value, HtmlNode node, List<CssRule>? rules = null)
    {
        var v = value.Trim();
        if (v == "none" || v == "normal") return null;
        var sb = new StringBuilder();
        var i = 0;
        while (i < v.Length)
        {
            var ch = v[i];
            if ((v.Length - i > 8 && string.CompareOrdinal(v, i, "counter(", 0, 8) == 0) || (v.Length - i > 9 && string.CompareOrdinal(v, i, "counters(", 0, 9) == 0))
            {
                var all = v[i + 7] == 's';
                var open = v.IndexOf('(', i);
                var closeParen = v.IndexOf(')', open);
                if (closeParen < 0) break;
                var args = CssParser.SplitTopLevel(v.Substring(open + 1, closeParen - open - 1), ',');
                var name = args.Count > 0 ? args[0].Trim() : string.Empty;
                var sep = all && args.Count > 1 ? args[1].Trim().Trim('"', '\'') : ".";
                var style = args.Count > (all ? 2 : 1) ? args[all ? 2 : 1].Trim() : "decimal";
                sb.Append(CounterText(name, style, sep, all));
                i = closeParen + 1;
                continue;
            }
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
            // open-quote / close-quote: the fall-through below walked past them and produced an
            // empty string. `no-` prefixed: the fall-through is right, they draw nothing.
            else if ((i == 0 || v[i - 1] != '-') && (ch == 'o' || ch == 'c')
                     && (string.CompareOrdinal(v, i, "open-quote", 0, 10) == 0 || string.CompareOrdinal(v, i, "close-quote", 0, 11) == 0))
            {
                sb.Append(Quote(node, rules, ch == 'o'));
                i += ch == 'o' ? 10 : 11;
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
        TagDefaults(ve, node);
        ApplyStyles(ve, node, rules, result);
        parent.Add(ve);
        HtmlNode? bottomCaption = null;
        // <colgroup>/<col>: widths by column (the width attribute or a style width), span repeats
        var colWidths = new List<string?>();
        void TakeCol(HtmlNode col)
        {
            string? w = col.Attr("width");
            if (w != null && !w.EndsWith("%", StringComparison.Ordinal) && !w.EndsWith("px", StringComparison.OrdinalIgnoreCase)) w += "px";
            if (col.Attr("style") is { } cs)
                foreach (var d in CssParser.ParseDeclarations(cs)) if (d.Name == "width") w = d.Value.Trim();
            var span = int.TryParse(col.Attr("span"), out var sp) && sp > 0 ? sp : 1;
            for (var i = 0; i < span; i++) colWidths.Add(w);
        }
        foreach (var child in node.Children)
        {
            if (child.Tag == "col") TakeCol(child);
            else if (child.Tag == "colgroup") { var any = false; foreach (var c in child.Children) if (c.Tag == "col") { TakeCol(c); any = true; } if (!any) TakeCol(child); }
        }
        if (colWidths.TrueForAll(w => w == null)) colWidths.Clear();
        var hideEmpty = CascadedValue(node, rules, "empty-cells") == "hide";
        if (hideEmpty) node.Attributes["data-empty-cells"] = "hide";
        // border-collapse: collapse - every cell draws its own border, so an interior edge was
        // drawn twice; the cell on the right/below it drops its edge and the pair becomes one line.
        // border-spacing only means anything under `separate`, which is the CSS default.
        var collapse = CascadedValue(node, rules, "border-collapse")?.Trim() == "collapse";
        var gaps = !collapse && CascadedValue(node, rules, "border-spacing") is { } bsv
            ? bsv.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) : null;
        var spaceX = gaps is { Length: > 0 } ? StyleApplier.Num(gaps[0]) : 0f;
        var spaceY = gaps is { Length: > 1 } ? StyleApplier.Num(gaps[1]) : spaceX;
        var rowIndex = 0;
        foreach (var child in node.Children)
        {
            if (child.IsText) continue;
            switch (child.Tag)
            {
                case "caption":
                    if ((CascadedValue(child, rules, "caption-side") ?? CascadedValue(node, rules, "caption-side")) == "bottom") bottomCaption = child; // inherited from the table
                    else Append(ve, child, rules, result);
                    break;
                case "thead": case "tbody": case "tfoot":
                {
                    var section = new VisualElement();
                    Register(section, child, result);
                    ApplyStyles(section, child, rules, result);
                    ve.Add(section);
                    foreach (var tr in child.Children)
                        if (tr.Tag == "tr") AppendRow(section, tr, columns, rules, result, colWidths, collapse, spaceX, spaceY, rowIndex++);
                    break;
                }
                case "tr":
                    AppendRow(ve, child, columns, rules, result, colWidths, collapse, spaceX, spaceY, rowIndex++);
                    break;
            }
        }
        if (bottomCaption != null) Append(ve, bottomCaption, rules, result);
        ApplyGap(ve, result.CssOf(ve), result);
    }

    private static string OwnText(HtmlNode n)
    {
        var sb = new StringBuilder();
        foreach (var c in n.Children) sb.Append(c.IsText ? c.Text : OwnText(c));
        return sb.ToString();
    }

    private static bool IsEmptyCell(HtmlNode cell)
    {
        foreach (var c in cell.Children)
            if (!c.IsText || c.Text.Trim().Length > 0) return false;
        return true;
    }

    private static HtmlNode? TableOf(HtmlNode n)
    {
        for (var p = n.Parent; p != null; p = p.Parent) if (p.Tag == "table") return p;
        return null;
    }

    private static void AppendRow(VisualElement table, HtmlNode tr, int columns, List<CssRule> rules, Result result, List<string?>? colWidths = null,
                                  bool collapse = false, float spaceX = 0f, float spaceY = 0f, int rowIndex = 0)
    {
        var colIndex = 0;
        // A row narrower than the table cannot say so with flex-grow: shares of a row are
        // shares of whatever that row happens to hold, so a lone cell filled it whatever its
        // colspan said - which is why colspan drew nothing. Such a row is sized by its share
        // of the TABLE instead. A row that fills the table keeps the grow model exactly.
        var spanned = 0;
        foreach (var cell in tr.Children)
            if (cell.Tag == "td" || cell.Tag == "th") spanned += Span(cell);
        var short_ = spanned < columns && columns > 0;
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Stretch;
        Register(row, tr, result);
        ApplyStyles(row, tr, rules, result);
        table.Add(row);
        if (rowIndex > 0 && spaceY > 0f) row.style.marginTop = spaceY;
        foreach (var cell in tr.Children)
        {
            if (cell.Tag != "td" && cell.Tag != "th") continue;
            // empty-cells: hide on the table: an empty cell keeps its place and paints nothing
            if (IsEmptyCell(cell) && TableOf(tr)?.Attr("data-empty-cells") == "hide")
                cell.Attributes["style"] = "visibility: hidden;" + (cell.Attr("style") ?? string.Empty);
            var w = cell.Attr("width");
            if (w == null && colWidths != null && colIndex < colWidths.Count) w = colWidths[colIndex];
            colIndex += Span(cell);
            if (w != null && cell.Attr("style")?.Contains("width") != true)
                cell.Attributes["style"] = "width:" + (w.EndsWith("%", StringComparison.Ordinal) || w.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? w : w + "px") + ";" + (cell.Attr("style") ?? string.Empty);
            // The cell's element is the one Append just added. Looking it up by id instead
            // meant colspan worked only on cells that happened to carry an id - which is
            // almost none of them, so a spanning cell silently took one column's width.
            var before = row.childCount;
            Append(row, cell, rules, result);
            if (row.childCount <= before) continue;
            var cve = row[row.childCount - 1];
            if (collapse)
            {
                if (row.childCount > 1) cve.style.borderLeftWidth = 0f;
                if (rowIndex > 0) cve.style.borderTopWidth = 0f;
            }
            else if (spaceX > 0f && row.childCount > 1) cve.style.marginLeft = spaceX;
            if (!result.CssOf(cve).ContainsKey("width"))
            {
                if (short_)
                {
                    cve.style.flexGrow = 0;
                    cve.style.flexBasis = new Length(100f * Span(cell) / columns, LengthUnit.Percent);
                }
                else
                {
                    cve.style.flexGrow = Span(cell);
                    cve.style.flexBasis = result.CssOf(table).ContainsKey("width") ? 0 : StyleKeyword.Auto;
                }
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
    /// <summary>The flex `order` property: children re-sequenced by it, stable, only when one names it.</summary>
    private static void OrderChildren(VisualElement ve, Result result)
    {
        var any = false;
        foreach (var child in ve.Children())
            if (result.CssOf(child).ContainsKey("order")) { any = true; break; }
        if (!any) return;
        var kids = new List<VisualElement>(ve.Children());
        var keyed = new List<(int order, int index, VisualElement ve)>();
        for (var i = 0; i < kids.Count; i++)
            keyed.Add((result.CssOf(kids[i]).TryGetValue("order", out var o) && int.TryParse(o.Trim(), out var n) ? n : 0, i, kids[i]));
        keyed.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.index.CompareTo(b.index));
        foreach (var k in keyed) k.ve.BringToFront();
    }

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
            // clear: the row wraps, so a real line break exists - a full-width zero-height spacer
            // before the child pushes it onto the next line. ponytail: left/right/both alike
            foreach (var c in new List<VisualElement>(ve.Children()))
            {
                if (!result.CssOf(c).TryGetValue("clear", out var cl) || cl.Trim() is "none" or "") continue;
                var brk = new VisualElement { name = "__clear" };
                brk.style.flexBasis = new Length(100, LengthUnit.Percent);
                brk.style.height = 0;
                ve.Insert(ve.IndexOf(c), brk);
            }
        }
        var columns = 0;
        if (css.TryGetValue("column-count", out var cc) && int.TryParse(cc.Trim(), out var ccn)) columns = ccn;
        else if (css.TryGetValue("column-width", out var cwv) && cwv.Trim() != "auto" && StyleApplier.Num(cwv) > 0f)
        {
            // column-width: as many columns of that width as fit the box (its own width, or the page's)
            var gapW = css.TryGetValue("column-gap", out var cg0) ? StyleApplier.Num(cg0) : 16f;
            var boxW = css.TryGetValue("width", out var cwidth) && !cwidth.Trim().EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(cwidth) : StyleApplier.ViewportW;
            columns = Mathf.Max(1, Mathf.FloorToInt((boxW + gapW) / (StyleApplier.Num(cwv) + gapW)));
        }
        if (columns > 1)
        {
            var kids = new List<VisualElement>(ve.Children());
            var gap = css.TryGetValue("column-gap", out var cg) ? StyleApplier.Num(cg) : 16f;
            ve.style.flexDirection = FlexDirection.Row;
            ve.style.alignItems = Align.FlexStart;
            var per = (kids.Count + columns - 1) / columns;
            // column-rule: a line in the middle of each gap, drawn as the left border of every column but the first
            var ruleW = 0f; var ruleStyle = "solid"; var ruleColor = Color.clear;
            if (css.TryGetValue("column-rule", out var cr))
                foreach (var part in CssParser.SplitTopLevel(cr.Trim(), ' '))
                {
                    if (part is "solid" or "dashed" or "dotted" or "double" or "none" or "hidden") ruleStyle = part;
                    else if (StyleApplier.TryColor(part, out var rc)) ruleColor = rc;
                    else if (part.Length > 0 && (char.IsDigit(part[0]) || part[0] == '.')) ruleW = StyleApplier.Num(part);
                    else if (part is "thin") ruleW = 1f; else if (part is "medium") ruleW = 3f; else if (part is "thick") ruleW = 5f;
                }
            if (css.TryGetValue("column-rule-width", out var crw)) ruleW = crw.Trim() is "thin" ? 1f : crw.Trim() is "medium" ? 3f : crw.Trim() is "thick" ? 5f : StyleApplier.Num(crw);
            if (css.TryGetValue("column-rule-style", out var crs)) ruleStyle = crs.Trim();
            if (css.TryGetValue("column-rule-color", out var crc) && StyleApplier.TryColor(crc, out var crcol)) ruleColor = crcol;
            if (ruleW > 0f && ruleColor.a <= 0.002f) ruleColor = ve.resolvedStyle.color;
            var hasRule = ruleW > 0f && ruleStyle is not ("none" or "hidden");
            void Distribute(VisualElement host, List<VisualElement> items)
            {
                var perCol = (items.Count + columns - 1) / columns;
                for (var c = 0; c < columns; c++)
                {
                    var col = new VisualElement { name = ve.name + "__col" + c };
                    col.style.flexGrow = 1; col.style.flexBasis = 0; col.style.flexShrink = 1;
                    if (c > 0 && hasRule)
                    {
                        col.style.marginLeft = gap * 0.5f - ruleW * 0.5f;
                        col.style.paddingLeft = gap * 0.5f - ruleW * 0.5f;
                        col.style.borderLeftWidth = ruleW;
                        col.style.borderLeftColor = ruleColor;
                        result.CssOf(col)["border-left-style"] = ruleStyle;
                    }
                    else if (c > 0) col.style.marginLeft = gap;
                    for (var i = c * perCol; i < Math.Min(items.Count, (c + 1) * perCol); i++) col.Add(items[i]);
                    host.Add(col);
                }
            }
            // column-span: all: the element takes the full width and the columns restart under it.
            // ponytail: column-fill: auto is accepted and fills like balance (the heights are not known before layout)
            var spanning = kids.FindAll(k => result.CssOf(k).TryGetValue("column-span", out var sp) && sp.Trim() == "all");
            if (spanning.Count == 0) Distribute(ve, kids);
            else
            {
                ve.style.flexDirection = FlexDirection.Column;
                ve.style.alignItems = Align.Stretch;
                var segment = new List<VisualElement>();
                void Flush()
                {
                    if (segment.Count == 0) return;
                    var row = new VisualElement { name = ve.name + "__cols" };
                    row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.FlexStart;
                    Distribute(row, new List<VisualElement>(segment));
                    ve.Add(row);
                    segment.Clear();
                }
                foreach (var k in kids)
                {
                    if (spanning.Contains(k)) { Flush(); ve.Add(k); }
                    else segment.Add(k);
                }
                Flush();
            }
        }
        // A query container: when its content box changes, the @container blocks under it may
        // answer differently, so its subtree is re-cascaded. Per container element only.
        // ponytail: an element that becomes a container through a later class write gets no
        // callback, since Flow runs at build.
        if (CssParser.IsQueryContainer(css))
        {
            float lastW = float.NaN, lastH = float.NaN;
            ve.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var crs = ve.resolvedStyle;
                var w = ve.layout.width - crs.paddingLeft - crs.paddingRight - crs.borderLeftWidth - crs.borderRightWidth;
                var h = ve.layout.height - crs.paddingTop - crs.paddingBottom - crs.borderTopWidth - crs.borderBottomWidth;
                if (Mathf.Abs(w - lastW) < 0.5f && Mathf.Abs(h - lastH) < 0.5f) return;   // NaN the first time, so it always runs once
                lastW = w; lastH = h;
                result.ContainerResized(ve);
            });
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
                    if (!hasH && w > 0f && Mathf.Abs(h - w / ratio) > 0.5f) { ve.style.height = w / ratio; PostLayout.LayoutWrites++; }
                    else if (hasH && h > 0f && Mathf.Abs(w - h * ratio) > 0.5f) { ve.style.width = h * ratio; PostLayout.LayoutWrites++; }
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
    private static void AddDisclosure(HtmlNode details, List<CssRule> rules)
    {
        HtmlNode? summary = null;
        foreach (var c in details.Children) if (c.Tag == "summary") { summary = c; break; }
        // ::details-content: the body of the details as one box, only when a rule names it (the wrapper changes what "details > p" means)
        var contentProbe = new HtmlNode { Tag = "div", Parent = details };
        contentProbe.Attributes["data-pseudo"] = "details-content";
        var wantContent = false;
        foreach (var rule in rules)
        {
            foreach (var sel in rule.Selectors)
                if (sel.Chain[sel.Chain.Count - 1].PseudoElement == "details-content" && sel.Matches(contentProbe)) { wantContent = true; break; }
            if (wantContent) break;
        }
        if (wantContent && !details.Children.Exists(c => c.Attr("data-pseudo") == "details-content"))
        {
            var body = details.Children.FindAll(c => c.Tag != "summary");
            details.Children.RemoveAll(c => c.Tag != "summary");
            foreach (var c in body) { c.Parent = contentProbe; contentProbe.Children.Add(c); }
            details.Children.Add(contentProbe);
        }
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
        string? image = null;
        void TakeType(string name, string value)
        {
            if (name == "list-style-type") { type = value.Trim(); return; }
            if (name == "list-style-image") { image = value.Trim() == "none" ? null : value.Trim(); return; }
            if (name != "list-style") return;
            foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part is "none" or "disc" or "circle" or "square" or "decimal" or "lower-alpha" or "upper-alpha" or "lower-roman" or "upper-roman") type = part;
                else if (part.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) image = part;
            }
        }
        // The list's own value, then rules on the item, then the item's inline style: the
        // item has not been through the cascade yet, so its declarations are read here.
        if (css.TryGetValue("list-style", out var ls)) TakeType("list-style", ls);
        if (css.TryGetValue("list-style-type", out var lst)) TakeType("list-style-type", lst);
        if (css.TryGetValue("list-style-image", out var lsi)) TakeType("list-style-image", lsi);
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
        if (image != null)
        {
            // list-style-image: the marker is a small image box the emitter draws as IMG
            var u = image.IndexOf('(');
            var src = u >= 0 ? image.Substring(u + 1, Math.Max(0, image.LastIndexOf(')') - u - 1)).Trim().Trim('"', '\'') : image;
            var img = new HtmlNode { Tag = "img", Parent = li };
            img.Attributes["src"] = src;
            img.Attributes["data-marker-image"] = "1";
            img.Attributes["style"] = "width: 0.9em; height: 0.9em; margin-right: 0.4em; align-self: center; flex-shrink: 0";
            li.Children.Insert(0, img);
            return;
        }
        if (type == "none") return;
        if (CssParser.CounterStyles.TryGetValue(type, out var counterStyle))
        {
            var t = counterStyle.Text(ordinal) ?? ordinal.ToString(CultureInfo.InvariantCulture);
            li.Children.Insert(0, new HtmlNode { Text = counterStyle.Prefix + t + counterStyle.Suffix, Parent = li });
            return;
        }
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
        span.Attributes["style"] = "width: 0.4em; height: 0.4em; margin-right: 0.5em; margin-top: 0.45em; align-self: flex-start; flex-shrink: 0"; // on the first line, as a browser places it
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
            if (KeepsOwnElement(c) || HasKeptDescendant(c))
                return false;
        }
        return true;
    }

    /// <summary>True when some descendant must stay its own element (an id, a class, a control, an image, generated content).</summary>
    private static bool HasKeptDescendant(HtmlNode node)
    {
        foreach (var c in node.Children)
        {
            if (c.IsText) continue;
            if (KeepsOwnElement(c) || HasKeptDescendant(c)) return true;
        }
        return false;
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
            if (KeepsOwnElement(c) || HasKeptDescendant(c))
                anyId = true;
        }
        return anyId;
    }

    internal static bool IsInlineOnly(HtmlNode node) => IsInlineOnly(node, null);

    /// <summary>Only text and inline tags below, none of which the cascade (when given) or inline style makes a box.</summary>
    internal static bool IsInlineOnly(HtmlNode node, List<CssRule>? rules)
    {
        foreach (var c in node.Children)
        {
            if (c.IsText)
                continue;
            if (!Inline.Contains(c.Tag!) || Blockified(c, rules) || !IsInlineOnly(c, rules))
                return false;
        }
        return true;
    }

    /// <summary>An inline tag whose inline style makes it a box: position absolute/fixed, float, or a block-level display.</summary>
    internal static bool Blockified(HtmlNode c) => Blockified(c, null);

    /// <summary>An inline tag whose cascaded display, position or float makes it a box (stylesheet rules count, as in a browser).</summary>
    internal static bool Blockified(HtmlNode c, List<CssRule>? rules)
    {
        if (rules != null)
        {
            var disp = CascadedValue(c, rules, "display")?.Trim().ToLowerInvariant();
            if (disp != null && (disp.StartsWith("block") || disp.StartsWith("flex") || disp.StartsWith("grid") || disp.StartsWith("table") || disp.StartsWith("inline-block") || disp.StartsWith("inline-flex") || disp.StartsWith("inline-grid"))) return true;
            var pos = CascadedValue(c, rules, "position")?.Trim().ToLowerInvariant();
            if (pos is "absolute" or "fixed") return true;
            var fl = CascadedValue(c, rules, "float")?.Trim().ToLowerInvariant();
            if (fl != null && fl != "none") return true;
        }
        if (c.Attr("style") is not { } st) return false;
        var s = st.ToLowerInvariant();
        if (s.IndexOf("position:", StringComparison.Ordinal) >= 0 && (s.Contains("absolute") || s.Contains("fixed"))) return true;
        if (s.IndexOf("float:", StringComparison.Ordinal) >= 0 && !s.Contains("float:none")) return true;
        var d = s.IndexOf("display:", StringComparison.Ordinal);
        if (d >= 0)
        {
            var v = s.Substring(d + 8).TrimStart();
            if (v.StartsWith("block") || v.StartsWith("flex") || v.StartsWith("grid") || v.StartsWith("table") || v.StartsWith("inline-block") || v.StartsWith("inline-flex") || v.StartsWith("inline-grid")) return true;
        }
        return false;
    }

    /// <summary>column-count, or the count column-width gives for the block's width; 0 when neither.</summary>
    private static int TextColumns(HtmlNode node, List<CssRule> rules)
    {
        if (CascadedValue(node, rules, "column-count") is { } ccv && int.TryParse(ccv, out var n)) return n;
        if (CascadedValue(node, rules, "column-width") is { } cw && cw != "auto" && StyleApplier.Num(cw) > 0f)
        {
            var gap = CascadedValue(node, rules, "column-gap") is { } g ? StyleApplier.Num(g) : 16f;
            var w = CascadedValue(node, rules, "width") is { } wv && !wv.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(wv) : StyleApplier.ViewportW;
            return Mathf.Max(1, Mathf.FloorToInt((w + gap) / (StyleApplier.Num(cw) + gap)));
        }
        return 0;
    }

    /// <summary>Flatten inline children to Unity rich text.</summary>
    /// <summary>
    /// innerHTML without a rebuild: when the new markup has the same shape as the element's
    /// current children (same tags, same ids, text where there was text), the new attribute
    /// values and text are copied onto the existing nodes; only elements whose attributes changed
    /// are restyled and only labels whose content changed are re-rendered. No element is created
    /// or thrown away, so a page that re-renders its screen every tick costs a few value writes.
    /// False (nothing touched) when the shape differs: the caller rebuilds.
    /// </summary>
    internal static bool Morph(VisualElement parent, HtmlNode parentNode, string html, Result result, Action<VisualElement>? restyled)
    {
        var frag = HtmlParser.Parse(html, _ => { }, MorphPool);
        var why = SameShape(parentNode.Children, frag.Children, parentNode.Attr("id") ?? "?");
        if (why != null)
        {
            LastMorphMiss = why;
            MorphPool.Return(frag);
            return false;
        }
        var changed = new HashSet<HtmlNode>();
        CopyInto(parentNode.Children, frag.Children, changed, result, restyled);
        if (changed.Count > 0)
            Relabel(parent, result, changed);
        MorphPool.Return(frag);   // nothing below keeps a node of the fragment: the values were copied across
        return true;
    }

    /// <summary>The fragment of an in-place update is parsed, compared, copied across and dropped: its nodes come back here.</summary>
    [ThreadStatic] private static HtmlParser.Pool? _morphPool;
    private static HtmlParser.Pool MorphPool => _morphPool ??= new HtmlParser.Pool();

    /// <summary>Why the last in-place update was refused (Diagnostics).</summary>
    internal static string? LastMorphMiss;

    private static bool Generated(HtmlNode n) => !n.IsText && (n.Attr("data-pseudo") != null || n.Attr("data-marker") != null);

    /// <summary>
    /// Null when the old children (without the ones the renderer generated: ::before/::after,
    /// list markers) have the new markup's shape; otherwise where they differ. A node with
    /// generated children must keep its attributes, since those children follow its cascade.
    /// </summary>
    private static string? SameShape(List<HtmlNode> a, List<HtmlNode> b, string at)
    {
        var ia = 0;
        for (var ib = 0; ib < b.Count; ib++, ia++)
        {
            while (ia < a.Count && Generated(a[ia])) ia++;
            if (ia >= a.Count) return at + ": fewer children than the new markup";
            var x = a[ia]; var y = b[ib];
            if (x.IsText != y.IsText) return at + "[" + ib + "]: text where an element is, or the reverse";
            if (x.IsText) continue;
            if (x.Tag != y.Tag) return at + "[" + ib + "]: <" + x.Tag + "> became <" + y.Tag + ">";
            if (!string.Equals(x.Attr("id"), y.Attr("id"), StringComparison.Ordinal)) return at + "[" + ib + "]: id " + x.Attr("id") + " became " + y.Attr("id");
            if (x.Children.Exists(Generated) && !SameAttributes(x, y)) return x.Attr("id") + ": attributes changed on an element with generated content";
            var inner = SameShape(x.Children, y.Children, x.Attr("id") ?? at);
            if (inner != null) return inner;
        }
        while (ia < a.Count && Generated(a[ia])) ia++;
        return ia < a.Count ? at + ": more children than the new markup" : null;
    }

    /// <summary>Attributes the mod keeps on a node for itself (click region, list and control markers); markup never carries them.</summary>
    private static readonly HashSet<string> InternalAttributes = new(StringComparer.OrdinalIgnoreCase) { "data-click", "data-listed", "data-control", "data-touched" };

    private static bool SameAttributes(HtmlNode o, HtmlNode n)
    {
        var count = 0;
        foreach (var kv in o.Attributes)
        {
            if (InternalAttributes.Contains(kv.Key)) continue;
            count++;
            if (!n.Attributes.TryGetValue(kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.Ordinal)) return false;
        }
        foreach (var kv in n.Attributes) if (!InternalAttributes.Contains(kv.Key)) count--;
        return count == 0;
    }

    private static void CopyInto(List<HtmlNode> old, List<HtmlNode> fresh, HashSet<HtmlNode> changed, Result result, Action<VisualElement>? restyled)
    {
        var io = 0;
        for (var i = 0; i < fresh.Count; i++, io++)
        {
            while (io < old.Count && Generated(old[io])) io++;
            var o = old[io]; var n = fresh[i];
            if (o.IsText)
            {
                if (!string.Equals(o.Text, n.Text, StringComparison.Ordinal)) { o.Text = n.Text; changed.Add(o); }
                continue;
            }
            if (!SameAttributes(o, n))
            {
                List<KeyValuePair<string, string>>? kept = null;
                foreach (var kv in o.Attributes) if (InternalAttributes.Contains(kv.Key)) (kept ??= new()).Add(kv);
                o.Attributes.Clear();
                foreach (var kv in n.Attributes) o.Attributes[kv.Key] = kv.Value;
                if (kept != null) foreach (var kv in kept) o.Attributes[kv.Key] = kv.Value;
                changed.Add(o);
                if (o.Attr("id") is { } id && result.ById.TryGetValue(id, out var ve) && result.NodeOf.ContainsKey(ve))
                {
                    ve.ClearClassList();
                    foreach (var c in (o.Attr("class") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)) ve.AddToClassList(c);
                    ApplyStyles(ve, o, result.Rules, result);
                    restyled?.Invoke(ve);
                }
            }
            CopyInto(o.Children, n.Children, changed, result, restyled);
        }
    }

    /// <summary>Labels under <paramref name="root"/> whose content holds a changed node get their rich text again.</summary>
    private static void Relabel(VisualElement root, Result result, HashSet<HtmlNode> changed)
    {
        foreach (var child in root.Children())
        {
            if (child is Label label && result.NodeOf.TryGetValue(child, out var node) && !node.IsText && node.Children.Count > 0
                && node.Attr("data-control") == null && node.Tag != "svg" && Holds(node, changed))
            {
                var text = RichText(node, result.Rules);
                if (!string.Equals(label.text, text, StringComparison.Ordinal)) label.text = text;
            }
            Relabel(child, result, changed);
        }
    }

    private static bool Holds(HtmlNode node, HashSet<HtmlNode> changed)
    {
        if (changed.Contains(node)) return true;
        foreach (var c in node.Children)
            if (Holds(c, changed)) return true;
        return false;
    }

    internal static string RichText(HtmlNode node, List<CssRule>? rules = null)
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
        if (_building && rules != null)
        {
            // an inline element flattened into its parent's label still counts (counter-increment)
            // and an ::after probe left for last is filled here, after its siblings
            if (node.Attr("data-pseudo") == null && node.Attr("data-marker") == null)
                ApplyCounters(node, node, Cascaded(node, rules));
            else if (node.Attr("data-pseudo") == "after" && node.Attr("data-content") is { } afterContent && node.Children.Count == 0)
            {
                var owner = node.Parent ?? node;
                if (AfterDecls.TryGetValue(node, out var afterDecls)) ApplyCounters(node, owner, afterDecls);
                node.Attributes.Remove("data-content");
                if (GeneratedText(afterContent, owner, rules) is { } afterText) node.Children.Add(new HtmlNode { Text = afterText, Parent = node });
            }
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
            // ruby: the annotation small and raised right after its base. ponytail: a browser
            // stacks it above the base; inline keeps the sentence flowing in one label
            case "rt": open.Append("<size=55%><voffset=0.55em>"); close.Insert(0, "</voffset></size>"); break;
            case "rp": return;
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
            var resolved = HasFn(raw.Value) ? TryResolveVars(raw.Value, node, raw.Name) : raw.Value;
            if (resolved == null) return; // an undefined var() with no fallback: the declaration is dropped
            var d = HasFn(raw.Value) ? new CssDeclaration(raw.Name, resolved, raw.Important) : raw;
            switch (d.Name)
            {
                case "color": colour = d.Value; break;
                case "font-size": size = d.Value; break;
                case "font-family":
                {
                    // the first real family, else what the first generic stands for; a generic never reaches TextMeshPro as a name
                    face = null;
                    string? generic = null;
                    foreach (var fam in d.Value.Split(','))
                    {
                        var name = fam.Trim().Trim('"', '\'');
                        if (name.Length == 0) continue;
                        if (StyleApplier.MapGeneric(name) is { } g) { generic ??= g; continue; }
                        face = name;
                        break;
                    }
                    face ??= generic;
                    break;
                }
                case "vertical-align":
                case "alignment-baseline":
                case "baseline-shift":
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
                case "text-decoration": case "text-decoration-line": underline = d.Value.Contains("underline"); strike = d.Value.Contains("line-through"); break;
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
    private static void TagDefaults(VisualElement ve, HtmlNode node)
    {
        var s = ve.style;
        switch (node.Tag)
        {
            case "body": s.fontSize = 16; s.color = Color.white; break; // a browser's defaults, so a page with no font-size still has text
            case "h1": s.fontSize = 28; s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 8; s.marginBottom = 8; break;
            case "h2": s.fontSize = 22; s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 6; s.marginBottom = 6; break;
            case "h3": s.fontSize = 18; s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 4; s.marginBottom = 4; break;
            case "h4": case "h5": case "h6": s.fontSize = 15; s.unityFontStyleAndWeight = FontStyle.Bold; s.marginTop = 4; s.marginBottom = 4; break;
            case "p": s.marginTop = 4; s.marginBottom = 4; break;
            case "b": case "strong": s.unityFontStyleAndWeight = FontStyle.Bold; break;
            case "i": case "em": s.unityFontStyleAndWeight = FontStyle.Italic; break;
            case "hr": s.height = 1; s.backgroundColor = new Color(1, 1, 1, 0.3f); s.marginTop = 6; s.marginBottom = 6; break;
            // a browser colours a:any-link, not every <a>: an anchor with no href is ordinary text
            case "a": if (node.Attr("href") != null) s.color = new Color(0.31f, 0.63f, 1f); break;
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
            case "datalist": case "map": case "area": s.display = DisplayStyle.None; break; // built (ids, events) but never drawn
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

    private static string? InheritedLineHeight(HtmlNode? node, Result result)
    {
        for (var n = node; n != null; n = n.Parent)
            if (result.LineHeights.TryGetValue(n, out var lh)) return lh;
        return null;
    }

    private static float InheritedFontSize(HtmlNode? node, Result result)
    {
        for (var n = node; n != null; n = n.Parent)
            if (result.FontSizes.TryGetValue(n, out var px)) return px;
        return StyleApplier.RootFontSize;
    }

    /// <summary>Substitute var(--name[, fallback]) from this node's chain of custom properties.</summary>
    /// <summary>A url against the page's &lt;base href&gt;; absolute and data urls, and pages without a base, pass through.</summary>
    internal static string ResolveUrl(string url, Result? result)
    {
        var u = url.Trim();
        if (result?.BaseUrl == null || u.Length == 0 || u.Contains("://") || u.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || u.StartsWith("#", StringComparison.Ordinal)) return u;
        try { return new Uri(new Uri(result.BaseUrl), u).ToString(); }
        catch (UriFormatException) { return u; }
    }

    /// <summary>True when a value needs resolving before use: var(), env() or light-dark() in it.</summary>
    internal static bool HasFn(string v) => v.IndexOf("var(", StringComparison.Ordinal) >= 0 || v.IndexOf("env(", StringComparison.Ordinal) >= 0 || v.IndexOf("light-dark(", StringComparison.Ordinal) >= 0 || v.IndexOf("attr(", StringComparison.Ordinal) >= 0;

    /// <summary>
    /// attr(name [unit | type(...)] [, fallback]) anywhere but `content`: the element's attribute, with the unit
    /// appended when one is asked for. `content` keeps its own attr(), which is a string and is read where
    /// the text is built. An attribute the element lacks takes the fallback, else the declaration is dropped
    /// as an unresolvable var() is.
    /// </summary>
    private static string ResolveAttr(string value, HtmlNode node, ref bool unresolved)
    {
        while (true)
        {
            var at = value.IndexOf("attr(", StringComparison.Ordinal);
            if (at < 0) return value;
            var depth = 0; var j = at + 4;
            while (j < value.Length) { if (value[j] == '(') depth++; else if (value[j] == ')' && --depth == 0) break; j++; }
            var parts = CssParser.SplitTopLevel(value.Substring(at + 5, Math.Max(0, j - at - 5)), ',');
            var head = parts.Count > 0 ? parts[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
            var raw = head.Length > 0 ? node.Attr(head[0]) : null;
            var type = head.Length > 1 ? head[1].ToLowerInvariant() : null;
            string? found = null;
            if (raw != null)
            {
                if (type == null || type.StartsWith("type(", StringComparison.Ordinal) || type is "raw-string" or "string") found = raw;
                else if (StyleApplier.IsNumber(raw.Trim())) found = raw.Trim() + type;   // a unit: px, em, %, deg, s...
            }
            if (found == null && parts.Count > 1) found = parts[1].Trim();
            if (found == null) { unresolved = true; found = string.Empty; }
            value = value.Substring(0, at) + found + (j + 1 < value.Length ? value.Substring(j + 1) : string.Empty);
        }
    }

    /// <summary>env(name[, fallback]): the safe-area and titlebar insets are 0 on a console; other names take the fallback.
    /// light-dark(a, b): a, or b when the cascade said color-scheme: dark.</summary>
    private static string ResolveEnvAndScheme(string value)
    {
        foreach (var fn in new[] { "env(", "light-dark(" })
        {
            while (true)
            {
                var at = value.IndexOf(fn, StringComparison.Ordinal);
                if (at < 0) break;
                var depth = 0; var j = at + fn.Length - 1;
                while (j < value.Length) { if (value[j] == '(') depth++; else if (value[j] == ')' && --depth == 0) break; j++; }
                var inner = value.Substring(at + fn.Length, Math.Max(0, j - at - fn.Length));
                var parts = CssParser.SplitTopLevel(inner, ',');
                string replacement;
                if (fn == "env(")
                {
                    var name = parts.Count > 0 ? parts[0].Trim() : string.Empty;
                    replacement = name.StartsWith("safe-area-inset", StringComparison.Ordinal) || name.StartsWith("titlebar-area", StringComparison.Ordinal) ? "0px" : parts.Count > 1 ? parts[1].Trim() : "0";
                }
                else replacement = parts.Count >= 2 ? (StyleApplier.ColorSchemeDark ? parts[1] : parts[0]).Trim() : inner;
                value = value.Substring(0, at) + replacement + (j + 1 < value.Length ? value.Substring(j + 1) : string.Empty);
            }
        }
        return value;
    }

    internal static string ResolveVars(string value, HtmlNode node) => ResolveVarsCore(value, node, out _);

    /// <summary>null when a var() names no custom property in scope and has no fallback: the declaration
    /// is invalid at computed-value time and is dropped, as a browser drops it.</summary>
    internal static string? TryResolveVars(string value, HtmlNode node, string? property = null)
    {
        var r = ResolveVarsCore(value, node, out var unresolved, property != "content");
        return unresolved ? null : r;
    }

    [ThreadStatic] private static int _varDepth;

    private static string ResolveVarsCore(string value, HtmlNode node, out bool unresolved, bool attr = true)
    {
        unresolved = false;
        value = ResolveEnvAndScheme(value);
        if (attr) value = ResolveAttr(value, node, ref unresolved);
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
                if (n.Vars != null && n.Vars.TryGetValue(name, out var v))
                {
                    found = v;
                    // A custom property holding var() is substituted where it is declared, as CSS
                    // computes it: appended raw, `--live: var(--steel-300)` left
                    // `background: var(--steel-300)` for the colour parser, and the box was not drawn
                    // at all. Capped, since `--a: var(--a)` is a cycle.
                    if (v.IndexOf("var(", StringComparison.Ordinal) >= 0 && _varDepth < 8)
                    {
                        _varDepth++;
                        try { found = ResolveVarsCore(v, n, out var nested, attr); if (nested) unresolved = true; }
                        finally { _varDepth--; }
                    }
                }
            if (found == null && fallback != null) { found = ResolveVarsCore(fallback, node, out var fbBad); if (fbBad) unresolved = true; }
            // @property --name { initial-value } IS a value, so a var() naming it resolves.
            // Marking it unresolved dropped the whole declaration, which is what an undefined
            // custom property deserves and a declared one does not.
            if (found == null && CssParser.PropertyInitials.TryGetValue(name, out var initial)) found = initial;
            if (found == null) { unresolved = true; found = string.Empty; }
            sb.Append(found);
            i = j + 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Flex `gap` on a layout engine without it: margins on the children along the main
    /// axis, and on the cross axis when the container wraps.
    /// </summary>
    internal static void ApplyGap(VisualElement ve, Dictionary<string, string> css, Result result)
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
        // remembered: children a script appends later, and a re-cascade that rewrites the margins, get the gap again
        result.GapContainers.Add(ve);
        var dir = ve.style.flexDirection.value;
        var row = dir == FlexDirection.Row || dir == FlexDirection.RowReverse;
        var wrap = ve.style.flexWrap.value == UnityEngine.UIElements.Wrap.Wrap;
        var count = ve.childCount;
        for (var i = 0; i < count; i++)
        {
            var child = ve[i];
            var last = i == count - 1;
            // the child's own margin from its record, so applying the gap twice adds it once
            var own = result.CssOf(child);
            var right = row ? (!last ? colGap : 0f) : (wrap ? colGap : 0f);
            var bottom = row ? (wrap ? rowGap : 0f) : (!last ? rowGap : 0f);
            if ((row || wrap) && DeclaredSide(own, "right") is { } mr) child.style.marginRight = mr + right;
            if ((!row || wrap) && DeclaredSide(own, "bottom") is { } mb) child.style.marginBottom = mb + bottom;
        }
    }

    /// <summary>A margin side as the element's CSS declares it, in px; null for auto (left alone).</summary>
    private static float? DeclaredSide(Dictionary<string, string> css, string side)
    {
        string? v = null;
        if (css.TryGetValue("margin-" + side, out var s)) v = s;
        else if (css.TryGetValue("margin", out var m))
        {
            var p = m.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 0) v = side == "right" ? (p.Length > 1 ? p[1] : p[0]) : (p.Length > 2 ? p[2] : p[0]);
        }
        if (v == null) return 0f;
        v = v.Trim();
        if (v == "auto") return null;
        return StyleApplier.Num(v);
    }

    private static void ApplyStyles(VisualElement ve, HtmlNode node, List<CssRule> rules, Result result)
    {
        result.Touch(ve);   // the record is about to be rewritten
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
        if (node.ScriptStyle != null)
            foreach (var kv in node.ScriptStyle) ordered.Add(new CssDeclaration(kv.Key, kv.Value));
        ordered.AddRange(important);
        ordered = StyleApplier.Expand(ordered);

        AnimationSpec? anim = null;
        var record = result.CssOf(ve);

        // A re-cascade (a class or attribute change) starts from scratch as a browser's does: what the
        // last cascade set and this one does not goes back to its default, then the tag's defaults.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in ordered) if (!d.Name.StartsWith("--", StringComparison.Ordinal)) names.Add(d.Name);
        if (node.Cascaded != null)
        {
            var reset = false;
            foreach (var old in node.Cascaded)
            {
                if (names.Contains(old)) continue;
                record.Remove(old);
                StyleApplier.Reset(ve, old);
                reset = true;
            }
            if (reset && node.Tag != null) TagDefaults(ve, node);
        }
        node.Cascaded = names;
        StyleApplier.EmSize = InheritedFontSize(node.Parent, result);
        StyleApplier.LineHeight = InheritedLineHeight(node.Parent, result);
        if (ve == result.Root) StyleApplier.RootLineHeight = null;   // per page: the field outlives a build
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
            // A design tool's export still holding {{ ... }} is a template whose markup was never
            // rendered, not CSS. Left to the cascade it fails three different ways and names none of
            // them: `border: 1px solid {{ x }}` reports "{{" and "x" as border values, and
            // `animation: {{ a }} 2s` reports an animation called "}}" with no @keyframes.
            if (raw.Value.IndexOf("{{", StringComparison.Ordinal) >= 0)
            {
                if (!_placeholdersSeen) { _placeholdersSeen = true; Warn("css: this page still has {{ }} template placeholders - its markup was never rendered, so those declarations are skipped"); }
                continue;
            }
            var resolved = HasFn(raw.Value) ? TryResolveVars(raw.Value, node, raw.Name) : raw.Value;
            if (resolved == null) continue; // an undefined var() with no fallback: the declaration is dropped
            var d = HasFn(raw.Value) ? new CssDeclaration(raw.Name, resolved, raw.Important) : raw;
            // `all`'s only legal values ARE the CSS-wide keywords, so the branch below dropped it
            // and it never reached anything. It undoes what the cascade has set so far on this
            // element; declarations after it still apply, as in a browser.
            // ponytail: the tag's own defaults come back, so all three keywords behave as `revert`
            // - `button { all: unset }` keeps the drawn button chrome. Reset the tag defaults too
            // if a page needs the full `unset`.
            if (d.Name == "all")
            {
                foreach (var gone in new List<string>(record.Keys)) { StyleApplier.Reset(ve, gone); record.Remove(gone); names.Remove(gone); }
                if (node.Tag != null) TagDefaults(ve, node);
                continue;
            }
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
            if (d.Name == "line-height")
            {
                // As computed: a percentage or em is a length of this element's, a bare number inherits as the factor.
                var lv = d.Value.Trim();
                var computed = lv.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(lv) / 100f * StyleApplier.EmSize + "px"
                             : lv.EndsWith("em", StringComparison.OrdinalIgnoreCase) ? StyleApplier.Num(lv) + "px" : lv;
                result.LineHeights[node] = computed;
                StyleApplier.LineHeight = computed;
                if (ve == result.Root) StyleApplier.RootLineHeight = computed;   // :root is the body here
            }
            if (d.Name.StartsWith("animation", StringComparison.Ordinal))
            {
                anim ??= new AnimationSpec();
                ApplyAnimationDeclaration(anim, d);
                continue;
            }
            StyleApplier.Apply(ve, d, Warn);
        }
        // ::placeholder: the field is a ScriptedScreens control, so its placeholder colour is
        // read off the rules that match a placeholder pseudo-element of this node and handed
        // to the control as placeholder_color. ponytail: colour only, last matching rule wins
        if (node.Tag is "input" or "textarea")
        {
            var ph = new HtmlNode { Tag = "span", Parent = node };
            ph.Attributes["data-pseudo"] = "placeholder";
            string? pc = null;
            foreach (var rule in rules)
            {
                var hit = false;
                foreach (var sel in rule.Selectors) if (sel.Matches(ph)) { hit = true; break; }
                if (!hit) continue;
                foreach (var d in rule.Declarations) if (d.Name == "color") pc = d.Value;
            }
            if (pc != null) node.Attributes["data-placeholder-color"] = HasFn(pc) ? ResolveVars(pc, node) : pc;
        }
        if (ve is Label breakLabel && record.TryGetValue("word-break", out var wbreak) && wbreak.Trim() == "break-all" && !string.IsNullOrEmpty(breakLabel.text))
        {
            breakLabel.text = VectorEmitter.BreakAll(breakLabel.text);
            // ponytail: the layout engine breaks only at spaces; the height a per-character wrap needs is estimated from an average glyph width
            if (record.TryGetValue("width", out var bwid) && bwid.Trim().EndsWith("px", StringComparison.Ordinal))
            {
                var fs = StyleApplier.EmSize > 0f ? StyleApplier.EmSize : 14f;
                var inner = Mathf.Max(8f, StyleApplier.Num(bwid) - Px(breakLabel.style.paddingLeft) - Px(breakLabel.style.paddingRight));
                var chars = breakLabel.text.Replace("​", string.Empty).Length;
                var lines = Mathf.Max(1, Mathf.CeilToInt(chars * fs * 0.55f / inner));
                breakLabel.style.minHeight = lines * fs * 1.25f + Px(breakLabel.style.paddingTop) + Px(breakLabel.style.paddingBottom);
                breakLabel.style.whiteSpace = WhiteSpace.Normal;
            }
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
        // scrollbar-gutter: stable - the bar is drawn as an overlay over the content, so a box's
        // layout jumped the moment it overflowed. Reserving the bar's width as padding is what
        // `stable` is for. ponytail: the 8/5 px here is EmitScrollbar's own default width.
        if (record.TryGetValue("scrollbar-gutter", out var gutter) && gutter.Trim().StartsWith("stable", StringComparison.Ordinal)
            && (record.TryGetValue("overflow", out var scrolls) || record.TryGetValue("overflow-y", out scrolls)) && scrolls.Trim() is "auto" or "scroll")
        {
            var barW = record.TryGetValue("scrollbar-width", out var sbw) ? sbw.Trim() switch { "none" => 0f, "thin" => 5f, _ => 8f } : 8f;
            if (barW > 0f) ve.style.paddingRight = Px(ve.style.paddingRight) + barW;
        }
        if (record.TryGetValue("display", out var display) && display.Trim() == "grid" && !result.Grids.Contains(ve))
            result.Grids.Add(ve);
        if (record.TryGetValue("position", out var position) && position.Trim() == "sticky")
        {
            // Sticky stays in flow; its top/left are the pin, not an offset. The emitter pins it.
            ve.style.top = StyleKeyword.Auto;
            ve.style.left = StyleKeyword.Auto;
        }

        if (anim != null && anim.Name.Length > 0 && anim.Name != "none")
        {
            if (result.Keyframes.ContainsKey(anim.Name) && !result.AnimationAttached.Contains(ve) && !result.Animations.Exists(a => a.element == ve))
                result.Animations.Add((ve, anim));
            else
                Warn($"css: animation \"{anim.Name}\" has no @keyframes");
        }
        ResolveWeightFace(ve, record);
    }

    /// <summary>
    /// The layout measures with the face the emitter draws: the family's real weight (and Condensed)
    /// face where the Fonts mod has one, instead of the regular face under a synthetic bold that is
    /// no wider. Otherwise a bold title measured narrow and the next flex item overlapped it.
    /// </summary>
    private static void ResolveWeightFace(VisualElement ve, Dictionary<string, string> record)
    {
        if (!record.TryGetValue("font-family", out var fam)) return;
        string? first = null;
        foreach (var raw in fam.Split(','))
        {
            var n = raw.Trim().Trim('"', '\'');
            if (n.Length == 0) continue;
            if (StyleApplier.MapGeneric(n) is { } g) { first ??= g; continue; }
            first = n;
            break;
        }
        if (first == null || VectorEmitter.NamedWeight(first)) return;
        var face = first;
        if (record.TryGetValue("font-stretch", out var st) && st.IndexOf("condensed", StringComparison.OrdinalIgnoreCase) >= 0 && FontLibrary.Get(face + " Condensed") != null)
            face += " Condensed";
        var fs = ve.style.unityFontStyleAndWeight.value;
        var bold = fs == FontStyle.Bold || fs == FontStyle.BoldAndItalic;
        var weight = record.TryGetValue("font-weight", out var w) ? VectorEmitter.WeightFace(w, bold) : (bold ? "Bold" : null);
        if (weight == null) return;
        var asset = FontLibrary.Get(face + " " + weight);
        if (asset == null) return;
        ve.style.face = asset;
        var italic = fs == FontStyle.Italic || fs == FontStyle.BoldAndItalic;
        ve.style.unityFontStyleAndWeight = italic ? FontStyle.Italic : FontStyle.Normal;
    }

    internal static void ApplyAnimationDeclaration(AnimationSpec anim, CssDeclaration d)
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
            case "animation-composition": anim.Composition = v.ToLowerInvariant() switch { "add" => 1, "accumulate" => 2, _ => 0 }; break;
        }
    }

    /// <summary>
    /// Gives every unnamed element under <paramref name="root"/> a name a slot can be made from.
    /// </summary>
    /// <remarks>
    /// A synthetic name starts with <c>__</c>, which the slot namer rejects deliberately - those are
    /// this mod's own inventions and nothing outside should address them. But an element a class
    /// moves has to be addressable or the state has nowhere to write, so the ones under a
    /// class-written element are renamed after their position in the document: stable across
    /// sessions, unlike the synthetic counter, and unlikely to collide with anything an author wrote.
    /// </remarks>
    internal static void Nameable(VisualElement root, HtmlRenderer.Result built, string prefix)
    {
        Walk(root, prefix);

        void Walk(VisualElement ve, string path)
        {
            for (var i = 0; i < ve.childCount; i++)
            {
                var child = ve[i];
                var here = path + "_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (child.name != null && child.name.StartsWith("__", StringComparison.Ordinal)
                    && !built.ById.ContainsKey(here))
                {
                    built.ById.Remove(child.name);
                    child.name = here;
                    built.ById[here] = child;
                    if (built.NodeOf.TryGetValue(child, out var node)) node.Attributes["id"] = here;
                }
                Walk(child, child.name ?? here);
            }
        }
    }

    /// <summary>
    /// Marks the elements whose wrapping transform group has to carry its id, so a compiled page
    /// can address their translate, rotation and scale by name. Only the ones a script really
    /// drives, and only those it drives <b>after</b> the page has loaded - a transform written once
    /// during setup is already in the geometry by the time anything is emitted.
    /// </summary>
    /// <remarks>
    /// Deliberately not "name every wrapper". The renderer registers an identified node in
    /// <c>scene.Identified</c> and keeps its whole prop array, so that would retain hundreds per
    /// page - which is the cost this whole redesign exists to remove.
    /// </remarks>
    internal static void NameDrivenGroups(HtmlRenderer.Result built)
    {
        built.NamedGroups.Clear();
        if (string.IsNullOrWhiteSpace(built.Script)) return;
        try
        {
            var (writes, _) = DomWrites.Of(built.Script);

            // Everything a script drives, so a key with a zero value is still emitted and still has
            // a slot. A bar that animates up from 0% has every corner radius clamped to nothing at
            // the moment it is translated, and without this it would have no rx to come back into.
            foreach (var w in writes)
            {
                if (!w.Runtime) continue;
                if (w.Id != null) built.Driven.Add(w.Id);
                else if (w.Prefix is { Length: >= 2 } family)
                    foreach (var id in built.ById.Keys)
                        if (id.Length > family.Length && id.StartsWith(family, StringComparison.Ordinal))
                            built.Driven.Add(id);
            }

            // An element a CLASS moves needs a name of its own. `#player.duck .helmet` shifts a
            // descendant that the markup never named, so it carries a synthetic `__div42` - which
            // the slot namer rejects, leaving the state with nothing to write. Every element under
            // one whose class is written gets a stable, addressable name instead, derived from its
            // position in the document so it is the same next session.
            foreach (var w in writes)
            {
                if (!w.Runtime || w.Property != "className" || w.Id == null) continue;
                if (built.ById.TryGetValue(w.Id, out var root) && root != null) Nameable(root, built, w.Id);
            }

            foreach (var w in writes)
            {
                if (!w.Runtime || w.Property is not ("style.transform" or "style.opacity" or "style.visibility" or "className")) continue;
                if (w.Id != null) { built.NamedGroups.Add(w.Id); continue; }
                // A family written through one expression - `$('pb' + i)` over fourteen pebbles.
                // Every member already exists in the page under its own id, so the family resolves
                // to real elements here and needs no lookup at run time. A prefix short enough to
                // catch unrelated elements is ignored rather than guessed at.
                if (w.Prefix is { Length: >= 2 } prefix)
                    foreach (var id in built.ById.Keys)
                        if (id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.Ordinal))
                            built.NamedGroups.Add(id);
            }
        }
        catch (System.Exception ex)
        {
            // A page whose script cannot be analysed still runs; it just gets no named groups.
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: could not read the script's writes: {ex.Message}");
        }
    }

}

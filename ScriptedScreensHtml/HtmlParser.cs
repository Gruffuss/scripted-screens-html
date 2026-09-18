using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ScriptedScreensHtml;

/// <summary>A parsed HTML node. Tag is null for a text node.</summary>
internal sealed class HtmlNode
{
    public string? Tag;
    public string Text = string.Empty;

    // Made on demand. Half the nodes of a page are text, which never has either, and a page that
    // rebuilds through innerHTML parses its whole markup again two or three times a second: an
    // empty dictionary and an empty list per node was most of what that cost.
    private Dictionary<string, string>? _attributes;
    private List<HtmlNode>? _children;

    public Dictionary<string, string> Attributes => _attributes ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<HtmlNode> Children => _children ??= new List<HtmlNode>();

    /// <summary>Reads that must not bring the collection into being.</summary>
    public int AttributeCount => _attributes?.Count ?? 0;
    public int ChildCount => _children?.Count ?? 0;
    public bool HasChildren => _children != null && _children.Count > 0;

    public HtmlNode? Parent;
    /// <summary>Custom properties (--name) declared on this node; lookups walk up the tree.</summary>
    public Dictionary<string, string>? Vars;
    /// <summary>A script's element.style writes, as a browser's style attribute holds them: they outlive a re-cascade and beat the rules.</summary>
    public Dictionary<string, string>? ScriptStyle;
    /// <summary>The properties the last cascade set on this node, so a rule that stops matching can be undone.</summary>
    public HashSet<string>? Cascaded;

    public bool IsText => Tag == null;

    /// <summary>Back to a blank node, keeping the capacity of any collections it already made.</summary>
    internal void Reset()
    {
        Tag = null;
        Text = string.Empty;
        Parent = null;
        Vars = null;
        ScriptStyle = null;
        Cascaded = null;
        _attributes?.Clear();
        _children?.Clear();
    }

    public string? Attr(string name) => _attributes != null && _attributes.TryGetValue(name, out var v) ? v : null;
}

/// <summary>
/// Forgiving HTML tokenizer: tags, attributes (quoted or bare), void and self-closing tags,
/// comments, entities, raw text inside style/script. Unclosed tags close at their parent;
/// a stray close tag closes the nearest matching ancestor. No DOCTYPE handling beyond
/// skipping it. ponytail: this is a tag soup parser, not HTML5; add cases when a page breaks.
/// </summary>
internal static class HtmlParser
{
    internal static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "hr", "img", "input", "meta", "link", "col", "wbr", "source", "area", "base", "track", "embed", "param",
    };

    private static readonly HashSet<string> RawText = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "script",
    };

    /// <summary>
    /// The same strings come out of a parse over and over: every tag name, every class, and the
    /// text of every element a page redraws without changing. This is a fixed-size cache keyed by
    /// the characters themselves - a hit costs a hash and a compare, a miss costs the string it
    /// would have cost anyway and overwrites whatever shared its slot. Bounded by construction, so
    /// a page inventing new text every tick cannot grow it.
    /// </summary>
    private const int InternSlots = 8192;
    [ThreadStatic] private static string[]? _intern;

    private static string Intern(string source, int start, int length)
    {
        if (length == 0) return string.Empty;
        var table = _intern ??= new string[InternSlots];
        var hash = 17;
        for (var i = 0; i < length; i++) hash = hash * 31 + source[start + i];
        var slot = (hash & int.MaxValue) & (InternSlots - 1);
        var hit = table[slot];
        if (hit != null && hit.Length == length && string.CompareOrdinal(hit, 0, source, start, length) == 0)
            return hit;
        var made = source.Substring(start, length);
        table[slot] = made;
        return made;
    }

    /// <summary>The same, for text that had to be decoded or collapsed on the way out of a builder.</summary>
    /// <summary>Lowercase, but only when it is not already: a tag name usually is, and ToLowerInvariant always makes a string.</summary>
    private static string Lower(string v)
    {
        for (var i = 0; i < v.Length; i++)
            if (char.IsUpper(v[i])) return v.ToLowerInvariant();
        return v;
    }

    private static string Intern(StringBuilder built)
    {
        var length = built.Length;
        if (length == 0) return string.Empty;
        var table = _intern ??= new string[InternSlots];
        var hash = 17;
        for (var i = 0; i < length; i++) hash = hash * 31 + built[i];
        var slot = (hash & int.MaxValue) & (InternSlots - 1);
        var hit = table[slot];
        if (hit != null && hit.Length == length)
        {
            var same = true;
            for (var i = 0; i < length && same; i++) same = hit[i] == built[i];
            if (same) return hit;
        }
        var made = built.ToString();
        table[slot] = made;
        return made;
    }

    /// <summary>
    /// Nodes to reuse instead of allocating. A caller that throws its tree away every tick (the
    /// in-place update of an innerHTML write) hands the same pool back each time, so a parse costs
    /// the text it found and nothing else. Not thread-safe: one pool per caller.
    /// </summary>
    internal sealed class Pool
    {
        private readonly Stack<HtmlNode> _free = new();

        internal HtmlNode Take() => _free.Count > 0 ? _free.Pop() : new HtmlNode();

        /// <summary>Give a tree back, children and all. The nodes must not be referenced any more.</summary>
        internal void Return(HtmlNode node)
        {
            for (var i = 0; i < node.ChildCount; i++) Return(node.Children[i]);
            node.Reset();
            _free.Push(node);
        }
    }

    /// <summary>Parse to a synthetic root whose children are the document's top level.</summary>
    public static HtmlNode Parse(string html, Action<string>? warn = null) => Parse(html, warn, null);

    public static HtmlNode Parse(string html, Action<string>? warn, Pool? pool)
    {
        HtmlNode New() => pool != null ? pool.Take() : new HtmlNode();
        var root = New();
        root.Tag = "#root";
        var current = root;
        var i = 0;
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length == 0)
                return;
            // Inside <pre> and <textarea> whitespace is content; HTML drops one newline
            // straight after the opening tag.
            var raw = text.ToString();
            text.Clear();
            var preformatted = false;
            for (var n = current; n != null; n = n.Parent)
                if (n.Tag is "pre" or "textarea") { preformatted = true; break; }
            string kept;
            if (preformatted)
            {
                kept = DecodeEntities(raw.Replace("\r\n", "\n"));
                if (current.Children.Count == 0 && kept.StartsWith("\n", StringComparison.Ordinal)) kept = kept.Substring(1);
            }
            else
                kept = CollapseWhitespace(raw);
            if (kept.Length == 0)
                return;
            { var t = New(); t.Text = kept; t.Parent = current; current.Children.Add(t); }
        }

        while (i < html.Length)
        {
            var c = html[i];
            if (c != '<')
            {
                text.Append(c);
                i++;
                continue;
            }

            // Comment, doctype, processing instruction.
            if (StartsWith(html, i, "<!--"))
            {
                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }
            if (i + 1 < html.Length && (html[i + 1] == '!' || html[i + 1] == '?'))
            {
                var end = html.IndexOf('>', i);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }

            // Close tag.
            if (i + 1 < html.Length && html[i + 1] == '/')
            {
                var end = html.IndexOf('>', i);
                if (end < 0)
                    break;
                var name = html.Substring(i + 2, end - i - 2).Trim();
                i = end + 1;
                FlushText();

                var target = current;
                while (target != root && !string.Equals(target.Tag, name, StringComparison.OrdinalIgnoreCase))
                    target = target.Parent!;
                if (target == root)
                    warn?.Invoke($"html: stray </{name}> ignored");
                else
                    current = target.Parent!;
                continue;
            }

            // Open tag. A '<' not followed by a letter is literal text.
            if (i + 1 >= html.Length || !char.IsLetter(html[i + 1]))
            {
                text.Append(c);
                i++;
                continue;
            }

            FlushText();
            var node = New(); node.Parent = current;
            i++;
            var start = i;
            while (i < html.Length && (char.IsLetterOrDigit(html[i]) || html[i] == '-' || html[i] == ':'))
                i++;
            node.Tag = Lower(Intern(html, start, i - start));

            var selfClosing = false;
            while (i < html.Length)
            {
                SkipWhitespace(html, ref i);
                if (i >= html.Length)
                    break;
                if (html[i] == '>')
                {
                    i++;
                    break;
                }
                if (html[i] == '/')
                {
                    selfClosing = true;
                    i++;
                    continue;
                }

                var nameStart = i;
                while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '=' && html[i] != '>' && html[i] != '/')
                    i++;
                var attrName = Intern(html, nameStart, i - nameStart);
                if (attrName.Length == 0)
                {
                    i++;
                    continue;
                }

                var value = string.Empty;
                SkipWhitespace(html, ref i);
                if (i < html.Length && html[i] == '=')
                {
                    i++;
                    SkipWhitespace(html, ref i);
                    if (i < html.Length && (html[i] == '"' || html[i] == '\''))
                    {
                        var quote = html[i];
                        var end = html.IndexOf(quote, i + 1);
                        if (end < 0)
                            end = html.Length;
                        value = Intern(html, i + 1, end - i - 1);
                        i = Math.Min(html.Length, end + 1);
                    }
                    else
                    {
                        var vs = i;
                        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>')
                            i++;
                        value = Intern(html, vs, i - vs);
                    }
                }
                node.Attributes[attrName] = DecodeEntities(value);
            }

            current.Children.Add(node);

            if (RawText.Contains(node.Tag))
            {
                var closeTag = "</" + node.Tag;
                var end = html.IndexOf(closeTag, i, StringComparison.OrdinalIgnoreCase);
                var raw = end < 0 ? html.Substring(i) : html.Substring(i, end - i);
                { var t = New(); t.Text = raw; t.Parent = node; node.Children.Add(t); }
                if (end < 0)
                {
                    i = html.Length;
                }
                else
                {
                    var gt = html.IndexOf('>', end);
                    i = gt < 0 ? html.Length : gt + 1;
                }
                continue;
            }

            if (!selfClosing && !Void.Contains(node.Tag))
                current = node;
        }

        FlushText();
        return root;
    }

    /// <summary>Collapse runs of whitespace to one space, as HTML does outside pre.</summary>
    public static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var ch in DecodeEntities(s))
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                continue;
            }
            if (pendingSpace)
                sb.Append(' ');
            pendingSpace = false;
            sb.Append(ch);
        }
        // Leading and trailing whitespace become single spaces rather than vanishing:
        // "<b>3</b> tanks" needs the one before "tanks". Callers trim whole labels.
        if (pendingSpace)
            sb.Append(' ');
        return sb.ToString();
    }

    public static string DecodeEntities(string s)
    {
        if (s.IndexOf('&') < 0)
            return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '&')
            {
                sb.Append(s[i]);
                continue;
            }
            var semi = s.IndexOf(';', i);
            if (semi < 0 || semi - i > 10)
            {
                sb.Append('&');
                continue;
            }
            var name = s.Substring(i + 1, semi - i - 1);
            string? rep = name switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" => "'",
                "nbsp" => " ",
                "deg" => "°",
                "middot" => "·",
                "bull" => "•",
                "rarr" => "→",
                "larr" => "←",
                "uarr" => "↑",
                "darr" => "↓",
                "hellip" => "…",
                "ndash" => "–",
                "mdash" => "—",
                _ => NamedEntities.TryGetValue(name, out var known) ? known : null,
            };
            if (rep == null && name.Length > 1 && name[0] == '#')
            {
                var hex = name.Length > 2 && (name[1] == 'x' || name[1] == 'X');
                var digits = hex ? name.Substring(2) : name.Substring(1);
                if (int.TryParse(digits, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var cp)
                    && cp > 0 && cp < 0x110000)
                    rep = char.ConvertFromUtf32(cp);
            }
            if (rep == null)
            {
                sb.Append('&');
                continue;
            }
            sb.Append(rep);
            i = semi;
        }
        return sb.ToString();
    }

    /// <summary>The named entities pages use: Latin-1, Greek, punctuation, arrows, maths, shapes. Codepoints, so this file's encoding does not matter.</summary>
    private static readonly Dictionary<string, string> NamedEntities = BuildEntities();

    private static Dictionary<string, string> BuildEntities()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        void Run(string names, int first)
        {
            var i = first;
            foreach (var n in names.Split(' ', StringSplitOptions.RemoveEmptyEntries)) { if (n != "-") d[n] = char.ConvertFromUtf32(i); i++; }
        }
        Run("nbsp iexcl cent pound curren yen brvbar sect uml copy ordf laquo not shy reg macr deg plusmn sup2 sup3 acute micro para middot cedil sup1 ordm raquo frac14 frac12 frac34 iquest", 160);
        Run("Agrave Aacute Acirc Atilde Auml Aring AElig Ccedil Egrave Eacute Ecirc Euml Igrave Iacute Icirc Iuml ETH Ntilde Ograve Oacute Ocirc Otilde Ouml times Oslash Ugrave Uacute Ucirc Uuml Yacute THORN szlig", 192);
        Run("agrave aacute acirc atilde auml aring aelig ccedil egrave eacute ecirc euml igrave iacute icirc iuml eth ntilde ograve oacute ocirc otilde ouml divide oslash ugrave uacute ucirc uuml yacute thorn yuml", 224);
        Run("Alpha Beta Gamma Delta Epsilon Zeta Eta Theta Iota Kappa Lambda Mu Nu Xi Omicron Pi Rho - Sigma Tau Upsilon Phi Chi Psi Omega", 913);
        Run("alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigmaf sigma tau upsilon phi chi psi omega", 945);
        void One(string name, int cp) => d[name] = char.ConvertFromUtf32(cp);
        One("OElig", 338); One("oelig", 339); One("Scaron", 352); One("scaron", 353); One("Yuml", 376); One("fnof", 402); One("circ", 710); One("tilde", 732);
        One("thetasym", 977); One("upsih", 978); One("piv", 982);
        One("ensp", 8194); One("emsp", 8195); One("thinsp", 8201); One("zwnj", 8204); One("zwj", 8205); One("lrm", 8206); One("rlm", 8207);
        One("lsquo", 8216); One("rsquo", 8217); One("sbquo", 8218); One("ldquo", 8220); One("rdquo", 8221); One("bdquo", 8222);
        One("dagger", 8224); One("Dagger", 8225); One("permil", 8240); One("lsaquo", 8249); One("rsaquo", 8250); One("euro", 8364);
        One("prime", 8242); One("Prime", 8243); One("oline", 8254); One("frasl", 8260); One("weierp", 8472); One("image", 8465); One("real", 8476); One("trade", 8482); One("alefsym", 8501);
        One("harr", 8596); One("crarr", 8629); One("lArr", 8656); One("uArr", 8657); One("rArr", 8658); One("dArr", 8659); One("hArr", 8660);
        One("forall", 8704); One("part", 8706); One("exist", 8707); One("empty", 8709); One("nabla", 8711); One("isin", 8712); One("notin", 8713); One("ni", 8715);
        One("prod", 8719); One("sum", 8721); One("minus", 8722); One("lowast", 8727); One("radic", 8730); One("prop", 8733); One("infin", 8734); One("ang", 8736);
        One("and", 8743); One("or", 8744); One("cap", 8745); One("cup", 8746); One("int", 8747); One("there4", 8756); One("sim", 8764); One("cong", 8773); One("asymp", 8776);
        One("ne", 8800); One("equiv", 8801); One("le", 8804); One("ge", 8805); One("sub", 8834); One("sup", 8835); One("nsub", 8836); One("sube", 8838); One("supe", 8839);
        One("oplus", 8853); One("otimes", 8855); One("perp", 8869); One("sdot", 8901); One("lceil", 8968); One("rceil", 8969); One("lfloor", 8970); One("rfloor", 8971);
        One("lang", 9001); One("rang", 9002); One("loz", 9674); One("spades", 9824); One("clubs", 9827); One("hearts", 9829); One("diams", 9830);
        One("check", 10003); One("cross", 10007); One("star", 9734); One("starf", 9733); One("laquo", 171); One("raquo", 187); One("hyphen", 8208); One("times", 215);
        return d;
    }

    private static bool StartsWith(string s, int at, string prefix)
    {
        return string.CompareOrdinal(s, at, prefix, 0, prefix.Length) == 0;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }
}

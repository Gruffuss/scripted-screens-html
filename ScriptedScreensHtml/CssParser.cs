using System;
using System.Collections.Generic;
using System.Text;

namespace ScriptedScreensHtml;

/// <summary>One property: value pair as written, value untrimmed of its inner spacing.</summary>
internal readonly struct CssDeclaration
{
    public readonly string Name;
    public readonly string Value;
    public readonly bool Important;

    public CssDeclaration(string name, string value, bool important = false)
    {
        Name = name;
        Value = value;
        Important = important;
    }
}

/// <summary>
/// One compound selector: optional tag plus any number of #id and .class parts. A selector
/// with a descendant combinator is a chain of these, matched right to left up the ancestors.
/// Pseudo-classes and attribute selectors are not supported; a rule using them is skipped.
/// </summary>
internal sealed class CssCompound
{
    public string? Tag;
    public string? Id;
    public readonly List<string> Classes = new();
    /// <summary>True when this compound was preceded by '>' : the previous compound must be the direct parent.</summary>
    public bool ChildOfPrevious;
    /// <summary>True when preceded by '+': the previous compound must be the immediately preceding element sibling.</summary>
    public bool SiblingOfPrevious;
    /// <summary>True when preceded by '~': the previous compound must match some earlier element sibling.</summary>
    public bool GeneralSiblingOfPrevious;
    /// <summary>"before" or "after": the subject is the generated child (data-pseudo) of the element this compound describes.</summary>
    public string? PseudoElement;
    /// <summary>Pseudo-class tests: :root, :first-child, :last-child, :nth-child(), :not().</summary>
    public readonly List<Func<HtmlNode, bool>> Pseudos = new();

    public bool Matches(HtmlNode node)
    {
        if (Tag != null && !string.Equals(Tag, node.Tag, StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var p in Pseudos)
            if (!p(node)) return false;
        if (Id != null && !string.Equals(Id, node.Attr("id"), StringComparison.Ordinal))
            return false;
        if (Classes.Count > 0)
        {
            var have = node.Attr("class");
            if (have == null)
                return false;
            var parts = have.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var want in Classes)
            {
                if (Array.IndexOf(parts, want) < 0)
                    return false;
            }
        }
        return true;
    }
}

internal sealed class CssSelector
{
    /// <summary>Left to right: ancestors first, subject last.</summary>
    public readonly List<CssCompound> Chain = new();

    /// <summary>CSS specificity packed as ids*10000 + classes*100 + tags.</summary>
    public int Specificity
    {
        get
        {
            var s = 0;
            foreach (var c in Chain)
                s += (c.Id != null ? 10000 : 0) + (c.Classes.Count + c.Pseudos.Count) * 100 + (c.Tag != null ? 1 : 0) + (c.PseudoElement != null ? 1 : 0);
            return s;
        }
    }

    public bool Matches(HtmlNode node)
    {
        var idx = Chain.Count - 1;
        // ::before/::after: the subject is a generated child carrying data-pseudo; the
        // selector proper is matched against its parent. Generated children match nothing else.
        var pseudoElement = Chain[idx].PseudoElement;
        var generated = node.Attr("data-pseudo");
        if (pseudoElement != null)
        {
            if (node.Parent == null || !string.Equals(generated, pseudoElement, StringComparison.Ordinal))
                return false;
            node = node.Parent;
        }
        else if (generated != null)
            return false;
        if (!Chain[idx].Matches(node))
            return false;
        var ancestor = node.Parent;
        var subject = node;
        while (idx > 0)
        {
            var direct = Chain[idx].ChildOfPrevious;
            var sibling = Chain[idx].SiblingOfPrevious;
            var general = Chain[idx].GeneralSiblingOfPrevious;
            idx--;
            if (sibling || general)
            {
                var prev = HtmlNodeExtensions.PreviousElementSibling(subject);
                if (general)
                    while (prev != null && !Chain[idx].Matches(prev))
                        prev = HtmlNodeExtensions.PreviousElementSibling(prev);
                if (prev == null || !Chain[idx].Matches(prev))
                    return false;
                subject = prev;
                ancestor = prev.Parent;
                continue;
            }
            if (direct)
            {
                if (ancestor == null || !Chain[idx].Matches(ancestor))
                    return false;
            }
            else
            {
                while (ancestor != null && !Chain[idx].Matches(ancestor))
                    ancestor = ancestor.Parent;
                if (ancestor == null)
                    return false;
            }
            ancestor = ancestor.Parent;
        }
        return true;
    }
}

internal sealed class CssKeyframe
{
    public float Percent;
    public readonly List<CssDeclaration> Declarations = new();
}

/// <summary>One @keyframes block, frames sorted by percent.</summary>
internal sealed class CssKeyframes
{
    public string Name = string.Empty;
    public readonly List<CssKeyframe> Frames = new();
}

internal static class HtmlNodeExtensions
{
    public static HtmlNode? PreviousElementSibling(HtmlNode node)
    {
        var parent = node.Parent;
        if (parent == null)
            return null;
        HtmlNode? prev = null;
        foreach (var c in parent.Children)
        {
            if (c == node)
                return prev;
            if (!c.IsText)
                prev = c;
        }
        return null;
    }
}

internal sealed class CssRule
{
    public readonly List<CssSelector> Selectors = new();
    public readonly List<CssDeclaration> Declarations = new();
    /// <summary>Source order, for tie-breaking equal specificity.</summary>
    public int Order;
}

/// <summary>
/// CSS subset parser: rules with selector lists and declaration blocks, comments,
/// inline style strings. @-rules are skipped whole. ponytail: no nesting, no !important.
/// </summary>
internal static class CssParser
{
    public static List<CssRule> ParseStylesheet(string css, Action<string>? warn = null, Dictionary<string, CssKeyframes>? keyframes = null)
    {
        var rules = new List<CssRule>();
        css = StripComments(css);
        var i = 0;
        var order = 0;
        while (i < css.Length)
        {
            SkipWhitespace(css, ref i);
            if (i >= css.Length)
                break;

            if (css[i] == '@')
            {
                var semi = css.IndexOf(';', i);
                var brace = css.IndexOf('{', i);
                if (brace < 0 || (semi >= 0 && semi < brace))
                {
                    i = semi < 0 ? css.Length : semi + 1;
                    continue;
                }
                var depth = 0;
                var j = brace;
                for (; j < css.Length; j++)
                {
                    if (css[j] == '{') depth++;
                    else if (css[j] == '}' && --depth == 0) { j++; break; }
                }
                var header = css.Substring(i + 1, brace - i - 1).Trim();
                if (header.StartsWith("keyframes ", StringComparison.OrdinalIgnoreCase) || header.StartsWith("-webkit-keyframes ", StringComparison.OrdinalIgnoreCase))
                {
                    var name = header.Substring(header.IndexOf(' ') + 1).Trim();
                    var inner = css.Substring(brace + 1, Math.Max(0, j - brace - 2));
                    var kf = ParseKeyframes(name, inner, warn);
                    if (keyframes != null)
                        keyframes[name] = kf;
                }
                else if (header.StartsWith("media", StringComparison.OrdinalIgnoreCase) || header.StartsWith("supports", StringComparison.OrdinalIgnoreCase))
                {
                    // One screen, one design width: a media query is decided once, here.
                    var inner = css.Substring(brace + 1, Math.Max(0, j - brace - 2));
                    if (header.StartsWith("supports", StringComparison.OrdinalIgnoreCase) || MediaMatches(header.Substring(5)))
                    {
                        foreach (var r in ParseStylesheet(inner, warn, keyframes))
                        {
                            r.Order = order++;
                            rules.Add(r);
                        }
                    }
                }
                else
                {
                    warn?.Invoke($"css: @{header.Split(' ')[0]} skipped");
                }
                i = j;
                continue;
            }

            var open = css.IndexOf('{', i);
            if (open < 0)
                break;
            var close = css.IndexOf('}', open);
            if (close < 0)
                close = css.Length;

            var selectorText = css.Substring(i, open - i).Trim();
            var body = css.Substring(open + 1, close - open - 1);
            i = Math.Min(css.Length, close + 1);

            var rule = new CssRule { Order = order++ };
            foreach (var sel in selectorText.Split(','))
            {
                var parsed = ParseSelector(sel.Trim(), warn);
                if (parsed != null)
                    rule.Selectors.Add(parsed);
            }
            if (rule.Selectors.Count == 0)
                continue;
            rule.Declarations.AddRange(ParseDeclarations(body));
            rules.Add(rule);
        }
        return rules;
    }

    /// <summary>The page's design size, for @media; set by the renderer before parsing.</summary>
    public static float ViewportWidth = 460f, ViewportHeight = 460f;

    /// <summary>
    /// A media query list against the design size: min/max-width/height, orientation,
    /// screen/all (true), print (false), "not", "and", commas. Unknown features are false,
    /// as in a browser.
    /// </summary>
    public static bool MediaMatches(string query)
    {
        foreach (var alternative in query.Split(','))
        {
            var q = alternative.Trim().ToLowerInvariant();
            if (q.Length == 0) continue;
            var negate = false;
            if (q.StartsWith("not ", StringComparison.Ordinal)) { negate = true; q = q.Substring(4); }
            if (q.StartsWith("only ", StringComparison.Ordinal)) q = q.Substring(5);
            var ok = true;
            foreach (var clause in q.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries))
            {
                var c = clause.Trim().Trim('(', ')').Trim();
                if (c == "screen" || c == "all" || c.Length == 0) continue;
                if (c == "print") { ok = false; break; }
                var colon = c.IndexOf(':');
                if (colon < 0) { ok = false; break; }
                var feature = c.Substring(0, colon).Trim();
                var value = c.Substring(colon + 1).Trim();
                var n = Px(value);
                var pass = feature switch
                {
                    "min-width" => ViewportWidth >= n,
                    "max-width" => ViewportWidth <= n,
                    "width" => Math.Abs(ViewportWidth - n) < 0.5f,
                    "min-height" => ViewportHeight >= n,
                    "max-height" => ViewportHeight <= n,
                    "height" => Math.Abs(ViewportHeight - n) < 0.5f,
                    "orientation" => value == "landscape" ? ViewportWidth >= ViewportHeight : ViewportWidth < ViewportHeight,
                    "min-aspect-ratio" or "max-aspect-ratio" or "aspect-ratio" => AspectClause(feature, value),
                    _ => false,
                };
                if (!pass) { ok = false; break; }
            }
            if (ok != negate) return true;
        }
        return false;
    }

    /// <summary>A px length or plain number as a float, NaN otherwise. Unity-free on purpose: this file is tested headless.</summary>
    private static float Px(string value)
    {
        var v = value.Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 2);
        return float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : float.NaN;
    }

    private static bool AspectClause(string feature, string value)
    {
        var parts = value.Split('/');
        if (parts.Length != 2 || float.IsNaN(Px(parts[0])) || float.IsNaN(Px(parts[1])) || Px(parts[1]) == 0f) return false;
        var want = Px(parts[0]) / Px(parts[1]);
        var have = ViewportWidth / ViewportHeight;
        return feature == "min-aspect-ratio" ? have >= want : feature == "max-aspect-ratio" ? have <= want : Math.Abs(have - want) < 0.01f;
    }

    /// <summary>Body of a @keyframes block: "from { } 50% { } to { }". Frames sorted by percent.</summary>
    public static CssKeyframes ParseKeyframes(string name, string body, Action<string>? warn)
    {
        var kf = new CssKeyframes { Name = name };
        var i = 0;
        while (i < body.Length)
        {
            var open = body.IndexOf('{', i);
            if (open < 0)
                break;
            var close = body.IndexOf('}', open);
            if (close < 0)
                close = body.Length;
            var selectors = body.Substring(i, open - i);
            var decls = ParseDeclarations(body.Substring(open + 1, close - open - 1));
            foreach (var sel in selectors.Split(','))
            {
                var t = sel.Trim().ToLowerInvariant();
                float pct;
                if (t == "from") pct = 0f;
                else if (t == "to") pct = 100f;
                else if (t.EndsWith("%", StringComparison.Ordinal) && float.TryParse(t.Substring(0, t.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p)) pct = p;
                else if (t.Length == 0) continue;
                else { warn?.Invoke($"css: keyframe selector \"{t}\" ignored"); continue; }
                var frame = new CssKeyframe { Percent = pct };
                frame.Declarations.AddRange(decls);
                kf.Frames.Add(frame);
            }
            i = Math.Min(body.Length, close + 1);
        }
        kf.Frames.Sort((a, b) => a.Percent.CompareTo(b.Percent));
        return kf;
    }

    public static List<CssDeclaration> ParseDeclarations(string body)
    {
        var list = new List<CssDeclaration>();
        // Split on ';' but not inside parentheses (rgba(...), url(...)).
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length)
            {
                if (body[i] == '(') depth++;
                else if (body[i] == ')') depth--;
                if (body[i] != ';' || depth > 0)
                    continue;
            }
            var decl = body.Substring(start, i - start);
            start = i + 1;
            var colon = decl.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = decl.Substring(0, colon).Trim().ToLowerInvariant();
            var value = decl.Substring(colon + 1).Trim();
            var bang = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
            var important = bang >= 0;
            if (important)
                value = value.Substring(0, bang).Trim();
            if (name.Length > 0 && value.Length > 0)
                list.Add(new CssDeclaration(name, value, important));
        }
        return list;
    }

    public static CssSelector? ParseSelector(string text, Action<string>? warn)
    {
        if (text.Length == 0)
            return null;
        var selector = new CssSelector();
        var childNext = false;
        var siblingNext = false;
        var generalNext = false;
        foreach (var rawPart in SplitSelector(text))
        {
            var part = rawPart;
            if (part == ">")
            {
                childNext = true;
                continue;
            }
            if (part == "+")
            {
                siblingNext = true;
                continue;
            }
            if (part == "~")
            {
                generalNext = true;
                continue;
            }
            var compound = new CssCompound { ChildOfPrevious = childNext, SiblingOfPrevious = siblingNext, GeneralSiblingOfPrevious = generalNext };
            childNext = false;
            siblingNext = false;
            generalNext = false;
            // [attr], [attr=v], [attr~=v], [attr|=v], [attr^=v], [attr$=v], [attr*=v]
            int open;
            while ((open = part.IndexOf('[')) >= 0)
            {
                var closeAt = part.IndexOf(']', open);
                if (closeAt < 0) { warn?.Invoke($"css: selector \"{text}\" skipped: unclosed ["); return null; }
                if (!AttributeTest(part.Substring(open + 1, closeAt - open - 1), compound)) { warn?.Invoke($"css: selector \"{text}\" skipped: bad attribute test"); return null; }
                part = part.Substring(0, open) + part.Substring(closeAt + 1);
            }
            if (part.Length == 0) part = "*";
            var pseudoAt = PseudoStart(part);
            if (pseudoAt >= 0)
            {
                if (!ParsePseudos(part.Substring(pseudoAt), compound, warn))
                {
                    warn?.Invoke($"css: selector \"{text}\" skipped: pseudo not supported");
                    return null;
                }
                part = part.Substring(0, pseudoAt);
                if (part.Length == 0) part = "*";
            }
            var i = 0;
            while (i < part.Length)
            {
                var kind = part[i];
                if (kind == '.' || kind == '#')
                    i++;
                var start = i;
                while (i < part.Length && part[i] != '.' && part[i] != '#')
                    i++;
                var name = part.Substring(start, i - start);
                if (name.Length == 0)
                    continue;
                if (kind == '.')
                    compound.Classes.Add(name);
                else if (kind == '#')
                    compound.Id = name;
                else if (name != "*")
                    compound.Tag = name.ToLowerInvariant();
            }
            selector.Chain.Add(compound);
        }
        return selector.Chain.Count > 0 ? selector : null;
    }

    /// <summary>Compounds and combinators, split on whitespace and on > + ~ outside brackets and parentheses.</summary>
    private static List<string> SplitSelector(string text)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        var depth = 0;
        void Flush() { if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); } }
        foreach (var ch in text)
        {
            if (ch == '[' || ch == '(') depth++;
            else if (ch == ']' || ch == ')') depth--;
            if (depth == 0 && (ch == '>' || ch == '+' || ch == '~')) { Flush(); parts.Add(ch.ToString()); continue; }
            if (depth == 0 && char.IsWhiteSpace(ch)) { Flush(); continue; }
            sb.Append(ch);
        }
        Flush();
        return parts;
    }

    private static bool AttributeTest(string body, CssCompound compound)
    {
        body = body.Trim();
        var op = -1;
        for (var i = 0; i < body.Length; i++)
            if (body[i] == '=') { op = i; break; }
        string name, value = string.Empty, kind = "exists";
        if (op < 0) name = body;
        else
        {
            var before = body.Substring(0, op);
            var last = before.Length > 0 ? before[before.Length - 1] : ' ';
            kind = last switch { '~' => "word", '|' => "dash", '^' => "prefix", '$' => "suffix", '*' => "contains", _ => "equals" };
            name = kind == "equals" ? before : before.Substring(0, before.Length - 1);
            value = body.Substring(op + 1).Trim();
            if (value.EndsWith(" i", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 2).Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0]) value = value.Substring(1, value.Length - 2);
        }
        name = name.Trim();
        if (name.Length == 0) return false;
        compound.Pseudos.Add(n =>
        {
            var have = n.Attr(name);
            if (have == null) return false;
            return kind switch
            {
                "exists" => true,
                "equals" => have == value,
                "word" => Array.IndexOf(have.Split(' ', StringSplitOptions.RemoveEmptyEntries), value) >= 0,
                "dash" => have == value || have.StartsWith(value + "-", StringComparison.Ordinal),
                "prefix" => value.Length > 0 && have.StartsWith(value, StringComparison.Ordinal),
                "suffix" => value.Length > 0 && have.EndsWith(value, StringComparison.Ordinal),
                "contains" => value.Length > 0 && have.Contains(value, StringComparison.Ordinal),
                _ => false,
            };
        });
        return true;
    }

    /// <summary>Index of the first ':' outside parentheses, or -1.</summary>
    private static int PseudoStart(string part)
    {
        var depth = 0;
        for (var i = 0; i < part.Length; i++)
        {
            if (part[i] == '(') depth++;
            else if (part[i] == ')') depth--;
            else if (part[i] == ':' && depth == 0) return i;
        }
        return -1;
    }

    /// <summary>
    /// ":root", ":first-child", ":last-child", ":nth-child(an+b|odd|even)", ":not(compound)".
    /// State pseudo-classes (:hover, :active, :focus, ...) never match: there is no pointer.
    /// Pseudo-elements and anything else are unsupported and skip the rule.
    /// </summary>
    private static bool ParsePseudos(string text, CssCompound compound, Action<string>? warn)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != ':') return false;
            i++;
            var element = false;
            if (i < text.Length && text[i] == ':') { element = true; i++; }
            var start = i;
            while (i < text.Length && text[i] != ':' && text[i] != '(') i++;
            var name = text.Substring(start, i - start).ToLowerInvariant();
            var arg = string.Empty;
            if (i < text.Length && text[i] == '(')
            {
                var depth = 0;
                var open = i;
                while (i < text.Length)
                {
                    if (text[i] == '(') depth++;
                    else if (text[i] == ')' && --depth == 0) break;
                    i++;
                }
                arg = text.Substring(open + 1, i - open - 1).Trim();
                i++;
            }
            if (element || name == "before" || name == "after")
            {
                // ::before / ::after (and the legacy one-colon spelling); other pseudo-elements skip the rule.
                if (name != "before" && name != "after") return false;
                compound.PseudoElement = name;
                continue;
            }
            switch (name)
            {
                case "root":
                    compound.Pseudos.Add(n => n.Tag == "html" || n.Tag == "body");
                    break;
                case "first-child":
                    compound.Pseudos.Add(n => ElementIndex(n) == 0);
                    break;
                case "last-child":
                    compound.Pseudos.Add(n => ElementIndex(n) == ElementCount(n) - 1);
                    break;
                case "nth-child":
                {
                    var (a, b) = ParseNth(arg);
                    compound.Pseudos.Add(n =>
                    {
                        var k = ElementIndex(n) + 1;
                        if (a == 0) return k == b;
                        var m = k - b;
                        return m % a == 0 && m / a >= 0;
                    });
                    break;
                }
                case "not":
                {
                    var inner = ParseSelector(arg, warn);
                    if (inner == null || inner.Chain.Count != 1) return false;
                    var c = inner.Chain[0];
                    compound.Pseudos.Add(n => !c.Matches(n));
                    break;
                }
                case "hover": case "active": case "focus": case "focus-visible": case "focus-within":
                case "visited": case "link": case "checked": case "disabled": case "enabled":
                    compound.Pseudos.Add(_ => false);
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static (int a, int b) ParseNth(string arg)
    {
        arg = arg.Replace(" ", string.Empty).ToLowerInvariant();
        if (arg == "odd") return (2, 1);
        if (arg == "even") return (2, 0);
        var n = arg.IndexOf('n');
        if (n < 0) return (0, int.TryParse(arg, out var only) ? only : 0);
        var aText = arg.Substring(0, n);
        var a = aText.Length == 0 || aText == "+" ? 1 : aText == "-" ? -1 : int.TryParse(aText, out var av) ? av : 1;
        var bText = arg.Substring(n + 1);
        var b = bText.Length == 0 ? 0 : int.TryParse(bText, out var bv) ? bv : 0;
        return (a, b);
    }

    private static int ElementIndex(HtmlNode node)
    {
        if (node.Parent == null) return 0;
        var idx = 0;
        foreach (var sib in node.Parent.Children)
        {
            if (sib == node) return idx;
            if (!sib.IsText) idx++;
        }
        return idx;
    }

    private static int ElementCount(HtmlNode node)
    {
        if (node.Parent == null) return 1;
        var count = 0;
        foreach (var sib in node.Parent.Children)
            if (!sib.IsText) count++;
        return count;
    }

    private static string StripComments(string css)
    {
        if (css.IndexOf("/*", StringComparison.Ordinal) < 0)
            return css;
        var sb = new StringBuilder(css.Length);
        var i = 0;
        while (i < css.Length)
        {
            if (i + 1 < css.Length && css[i] == '/' && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? css.Length : end + 2;
                continue;
            }
            sb.Append(css[i]);
            i++;
        }
        return sb.ToString();
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }
}

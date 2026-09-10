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

    public bool Matches(HtmlNode node)
    {
        if (Tag != null && !string.Equals(Tag, node.Tag, StringComparison.OrdinalIgnoreCase))
            return false;
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
                s += (c.Id != null ? 10000 : 0) + c.Classes.Count * 100 + (c.Tag != null ? 1 : 0);
            return s;
        }
    }

    public bool Matches(HtmlNode node)
    {
        var idx = Chain.Count - 1;
        if (!Chain[idx].Matches(node))
            return false;
        var ancestor = node.Parent;
        var subject = node;
        while (idx > 0)
        {
            var direct = Chain[idx].ChildOfPrevious;
            var sibling = Chain[idx].SiblingOfPrevious;
            idx--;
            if (sibling)
            {
                var prev = HtmlNodeExtensions.PreviousElementSibling(subject);
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
        if (text.IndexOf(':') >= 0 || text.IndexOf('[') >= 0 || text.IndexOf('~') >= 0)
        {
            warn?.Invoke($"css: selector \"{text}\" not supported (only tag, .class, #id, descendant, > child, + sibling)");
            return null;
        }

        var selector = new CssSelector();
        var childNext = false;
        var siblingNext = false;
        foreach (var part in text.Replace(">", " > ").Replace("+", " + ").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
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
            var compound = new CssCompound { ChildOfPrevious = childNext, SiblingOfPrevious = siblingNext };
            childNext = false;
            siblingNext = false;
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

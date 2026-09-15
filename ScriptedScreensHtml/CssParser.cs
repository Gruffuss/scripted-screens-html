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
        var generated = node.Attr("data-pseudo") ?? (node.Attr("data-marker") != null ? "marker" : null);
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

/// <summary>An @counter-style: how a counter value becomes text.</summary>
internal sealed class CounterStyle
{
    public string System = "symbolic";
    public List<string> Symbols = new();
    public string Suffix = ". ";
    public string Prefix = string.Empty;

    /// <summary>The text for value n, without prefix/suffix; null when the system cannot represent it (falls back to decimal).</summary>
    public string? Text(int n)
    {
        var k = Symbols.Count;
        if (k == 0) return null;
        switch (System)
        {
            case "cyclic": return Symbols[((n - 1) % k + k) % k];
            case "fixed": return n >= 1 && n <= k ? Symbols[n - 1] : null;
            case "symbolic": { if (n < 1) return null; var reps = (n - 1) / k + 1; var sym = Symbols[(n - 1) % k]; var sb = new StringBuilder(); for (var i = 0; i < reps; i++) sb.Append(sym); return sb.ToString(); }
            case "alphabetic":
            {
                if (n < 1 || k < 2) return null;
                var sb = new StringBuilder();
                while (n > 0) { n--; sb.Insert(0, Symbols[n % k]); n /= k; }
                return sb.ToString();
            }
            case "numeric":
            {
                if (n < 0 || k < 2) return null;
                if (n == 0) return Symbols[0];
                var sb = new StringBuilder();
                while (n > 0) { sb.Insert(0, Symbols[n % k]); n /= k; }
                return sb.ToString();
            }
            default: return null;
        }
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
                    // statement at-rules: @import is fetched by the surface; @layer lists and @charset are nothing to do
                    var stmt = css.Substring(i + 1, (semi < 0 ? css.Length : semi) - i - 1).Trim();
                    if (stmt.StartsWith("import", StringComparison.OrdinalIgnoreCase) && ImportUrl(stmt.Substring(6)) is { } importUrl)
                        Imports.Add(importUrl);
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
                else if (header.StartsWith("font-face", StringComparison.OrdinalIgnoreCase))
                {
                    var inner = css.Substring(brace + 1, Math.Max(0, j - brace - 2));
                    string? fam = null, src = null, weight = "normal", style = "normal";
                    foreach (var d in ParseDeclarations(inner))
                    {
                        switch (d.Name)
                        {
                            case "font-family": fam = d.Value.Trim().Trim('"', '\''); break;
                            case "src":
                            {
                                // src: url(file.ttf) format("truetype"), url(...) ...: the first url with a font extension
                                foreach (var cand in SplitTopLevel(d.Value, ','))
                                {
                                    var u = cand.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
                                    if (u < 0) continue;
                                    var closeParen = cand.IndexOf(')', u);
                                    if (closeParen < 0) continue;
                                    var path = cand.Substring(u + 4, closeParen - u - 4).Trim().Trim('"', '\'');
                                    var lower = path.ToLowerInvariant();
                                    if (lower.EndsWith(".ttf") || lower.EndsWith(".otf")) { src = path; break; }
                                    src ??= path;
                                }
                                break;
                            }
                            case "font-weight": weight = d.Value.Trim(); break;
                            case "font-style": style = d.Value.Trim(); break;
                        }
                    }
                    if (fam != null && src != null) FontFaces.Add((fam, src, weight, style));
                    else warn?.Invoke("css: @font-face needs font-family and src");
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
                else if (header.StartsWith("layer", StringComparison.OrdinalIgnoreCase) || header.StartsWith("container", StringComparison.OrdinalIgnoreCase) || header.StartsWith("scope", StringComparison.OrdinalIgnoreCase))
                {
                    // @layer: its rules in source order (layer precedence is source order here);
                    // @container: decided against the design size like @media (ponytail: a real
                    // container query needs the container's laid-out size and a re-cascade);
                    // @scope (root): the block becomes a nested rule under the root selector.
                    var inner = css.Substring(brace + 1, Math.Max(0, j - brace - 2));
                    var take = true;
                    if (header.StartsWith("container", StringComparison.OrdinalIgnoreCase))
                    {
                        var paren = header.IndexOf('(');
                        take = paren >= 0 && MediaMatches(header.Substring(paren));
                    }
                    else if (header.StartsWith("scope", StringComparison.OrdinalIgnoreCase))
                    {
                        var paren = header.IndexOf('(');
                        var closeParen = paren >= 0 ? header.IndexOf(')', paren) : -1;
                        var root = closeParen > paren ? header.Substring(paren + 1, closeParen - paren - 1).Trim() : ":root";
                        inner = root + " { " + inner.Replace(":scope", "&") + " }";
                    }
                    if (take)
                    {
                        foreach (var r in ParseStylesheet(inner, warn, keyframes))
                        {
                            r.Order = order++;
                            rules.Add(r);
                        }
                    }
                }
                else if (header.StartsWith("starting-style", StringComparison.OrdinalIgnoreCase))
                {
                    // @starting-style { rules }: what an element looks like the moment it appears; Tweens starts from it
                    var inner = css.Substring(brace + 1, Math.Max(0, j - brace - 2));
                    StartingRules.AddRange(ParseStylesheet(inner, warn, keyframes));
                }
                else if (header.StartsWith("counter-style", StringComparison.OrdinalIgnoreCase))
                {
                    // @counter-style name { system; symbols; suffix; prefix }: used by list-style-type and counter()
                    var inner = css.Substring(brace + 1, Math.Max(0, j - brace - 2));
                    var cname = header.Substring(13).Trim();
                    var style = new CounterStyle();
                    foreach (var d in ParseDeclarations(inner))
                    {
                        switch (d.Name)
                        {
                            case "system": style.System = d.Value.Trim().Split(' ')[0].ToLowerInvariant(); break;
                            case "symbols": style.Symbols = Symbols(d.Value); break;
                            case "suffix": style.Suffix = Unquote(d.Value); break;
                            case "prefix": style.Prefix = Unquote(d.Value); break;
                        }
                    }
                    if (cname.Length > 0 && style.Symbols.Count > 0) CounterStyles[cname] = style;
                }
                else if (header.StartsWith("property", StringComparison.OrdinalIgnoreCase))
                {
                    // @property --name { initial-value }: the value a var() falls back to
                    var inner = css.Substring(brace + 1, Math.Max(0, j - brace - 2));
                    var pname = header.Substring(8).Trim();
                    foreach (var d in ParseDeclarations(inner))
                        if (d.Name == "initial-value") PropertyInitials[pname] = d.Value.Trim();
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
            var close = MatchBrace(css, open);

            var selectorText = css.Substring(i, open - i).Trim();
            var body = css.Substring(open + 1, Math.Max(0, close - open - 1));
            i = Math.Min(css.Length, close + 1);
            ParseRule(selectorText, body, null, rules, ref order, warn, keyframes);
        }
        return rules;
    }

    /// <summary>Index of the '}' matching the '{' at <paramref name="open"/>, or the end of the text.</summary>
    private static int MatchBrace(string s, int open)
    {
        var depth = 0;
        for (var j = open; j < s.Length; j++)
        {
            if (s[j] == '{') depth++;
            else if (s[j] == '}' && --depth == 0) return j;
        }
        return s.Length;
    }

    /// <summary>Split on a separator outside parentheses and brackets.</summary>
    internal static List<string> SplitTopLevel(string text, char sep)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                var c = text[i];
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                if (c != sep || depth > 0) continue;
            }
            parts.Add(text.Substring(start, i - start));
            start = i + 1;
        }
        return parts;
    }

    /// <summary>
    /// One rule body, which under CSS nesting may hold declarations and nested rules in any
    /// order. A nested selector is expanded against every parent selector: `&amp;` is replaced
    /// by the parent, otherwise the parent is prefixed as an ancestor (a leading combinator
    /// keeps it). A nested `@media`/`@supports` applies its block to the parent selectors.
    /// </summary>
    private static void ParseRule(string selectorText, string body, List<string>? parents, List<CssRule> rules, ref int order, Action<string>? warn, Dictionary<string, CssKeyframes>? keyframes)
    {
        var selectors = new List<string>();
        foreach (var raw in SplitTopLevel(selectorText, ','))
        {
            var t = raw.Trim();
            if (t.Length == 0) continue;
            if (parents == null) { selectors.Add(t); continue; }
            foreach (var p in parents)
                selectors.Add(t.IndexOf('&') >= 0 ? t.Replace("&", p) : p + " " + t);
        }

        var decls = new StringBuilder();
        var nested = new List<(string sel, string body)>();
        var segStart = 0;
        for (var k = 0; k < body.Length; k++)
        {
            if (body[k] != '{') continue;
            var selStart = body.LastIndexOf(';', k);
            selStart = selStart < segStart ? segStart : selStart + 1;
            decls.Append(body, segStart, selStart - segStart);
            var end = MatchBrace(body, k);
            nested.Add((body.Substring(selStart, k - selStart).Trim(), body.Substring(k + 1, Math.Max(0, end - k - 1))));
            k = end;
            segStart = k + 1;
        }
        if (segStart < body.Length) decls.Append(body, segStart, body.Length - segStart);

        var rule = new CssRule { Order = order++ };
        foreach (var sel in selectors)
        {
            var parsed = ParseSelector(sel, warn);
            if (parsed != null) rule.Selectors.Add(parsed);
        }
        rule.Declarations.AddRange(ParseDeclarations(decls.ToString()));
        if (rule.Selectors.Count > 0 && rule.Declarations.Count > 0)
            rules.Add(rule);

        foreach (var (sel, nbody) in nested)
        {
            if (sel.StartsWith("@", StringComparison.Ordinal))
            {
                var header = sel.Substring(1).Trim();
                var kind = header.Split(' ', '(')[0].ToLowerInvariant();
                if (kind == "media" && !MediaMatches(header.Substring(5))) continue;
                if (kind == "container")
                {
                    var paren = header.IndexOf('(');
                    if (paren < 0 || !MediaMatches(header.Substring(paren))) continue;
                }
                if (kind == "starting-style")
                    ParseRule("&", nbody, selectors, StartingRules, ref order, warn, keyframes);
                else if (kind is "media" or "supports" or "layer" or "container")
                    ParseRule("&", nbody, selectors, rules, ref order, warn, keyframes);
                else
                    warn?.Invoke($"css: nested @{header.Split(' ')[0]} skipped");
                continue;
            }
            ParseRule(sel, nbody, selectors, rules, ref order, warn, keyframes);
        }
    }

    /// <summary>The page's design size, for @media; set by the renderer before parsing.</summary>
    public static float ViewportWidth = 460f, ViewportHeight = 460f;

    /// <summary>True once any stylesheet used :hover/:active/:focus, so the surface tracks the pointer.</summary>
    public static bool UsesPointerState;

    private static readonly CssSelector FocusSel = ParseSelector("[data-focus]", null)!;

    /// <summary>@font-face declarations collected while parsing; the renderer clears and registers them.</summary>
    public static readonly List<(string family, string src, string weight, string style)> FontFaces = new();

    /// <summary>@import urls collected while parsing; the surface fetches and inlines them.</summary>
    public static readonly List<string> Imports = new();

    /// <summary>@starting-style rules collected while parsing: the "from" state of an element that has just appeared.</summary>
    public static readonly List<CssRule> StartingRules = new();

    /// <summary>@property initial values, what an undefined var() of that name resolves to.</summary>
    public static readonly Dictionary<string, string> PropertyInitials = new(StringComparer.Ordinal);

    /// <summary>@counter-style rules by name.</summary>
    public static readonly Dictionary<string, CounterStyle> CounterStyles = new(StringComparer.Ordinal);

    private static string Unquote(string v)
    {
        v = v.Trim();
        return v.Length >= 2 && (v[0] == '"' || v[0] == '\'') && v[v.Length - 1] == v[0] ? v.Substring(1, v.Length - 2) : v;
    }

    /// <summary>The symbols list of @counter-style: quoted strings or bare tokens.</summary>
    private static List<string> Symbols(string v)
    {
        var list = new List<string>();
        var i = 0;
        while (i < v.Length)
        {
            var ch = v[i];
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (ch == '"' || ch == '\'')
            {
                var end = v.IndexOf(ch, i + 1);
                if (end < 0) end = v.Length;
                list.Add(v.Substring(i + 1, end - i - 1));
                i = end + 1;
                continue;
            }
            var j = i;
            while (j < v.Length && !char.IsWhiteSpace(v[j])) j++;
            list.Add(v.Substring(i, j - i));
            i = j;
        }
        return list;
    }

    /// <summary>The url of an @import prelude: url(...) or a quoted string; the media/layer conditions after it are ignored.</summary>
    private static string? ImportUrl(string prelude)
    {
        var p = prelude.Trim();
        if (p.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var closeParen = p.IndexOf(')');
            if (closeParen < 0) return null;
            p = p.Substring(4, closeParen - 4);
        }
        else
        {
            var end = p.Length > 1 ? p.IndexOf(p[0], 1) : -1;
            if (end > 0 && (p[0] == '"' || p[0] == '\'')) p = p.Substring(0, end + 1);
        }
        p = p.Trim().Trim('"', '\'');
        return p.Length > 0 ? p : null;
    }

    private static bool IsField(HtmlNode n) => n.Tag is "input" or "textarea" or "select";

    private static string? FieldValue(HtmlNode n)
    {
        if (n.Attr("value") is { } v) return v;
        if (n.Tag != "textarea") return null;
        var sb = new StringBuilder();
        foreach (var c in n.Children) if (c.IsText) sb.Append(c.Text);
        return sb.ToString();
    }

    /// <summary>Constraint validation as a browser runs it: required, pattern, minlength/maxlength, type email/url/number, min/max.</summary>
    private static bool Valid(HtmlNode n)
    {
        var v = FieldValue(n) ?? string.Empty;
        var type = (n.Attr("type") ?? "text").ToLowerInvariant();
        if (n.Attr("required") != null)
        {
            if (type is "checkbox" or "radio") { if (n.Attr("checked") == null) return false; }
            else if (v.Trim().Length == 0) return false;
        }
        if (v.Length == 0) return true;
        if (n.Attr("pattern") is { } pat)
        {
            try { if (!System.Text.RegularExpressions.Regex.IsMatch(v, "^(?:" + pat + ")$")) return false; }
            catch (ArgumentException) { }
        }
        if (n.Attr("minlength") is { } mn && int.TryParse(mn, out var minLen) && v.Length < minLen) return false;
        if (n.Attr("maxlength") is { } mx && int.TryParse(mx, out var maxLen) && v.Length > maxLen) return false;
        if (type == "email" && !System.Text.RegularExpressions.Regex.IsMatch(v.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return false;
        if (type == "url" && !Uri.TryCreate(v.Trim(), UriKind.Absolute, out _)) return false;
        if (type is "number" or "range")
        {
            if (!float.TryParse(v.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) return false;
            if (InRange(n) == false) return false;
        }
        return true;
    }

    /// <summary>For a number/range input with a value: inside min/max or not; null when the question does not apply.</summary>
    private static bool? InRange(HtmlNode n)
    {
        if (n.Tag != "input" || (n.Attr("type") ?? "text").ToLowerInvariant() is not ("number" or "range")) return null;
        if (!float.TryParse((FieldValue(n) ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)) return null;
        if (n.Attr("min") is { } mn && float.TryParse(mn, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min) && x < min) return false;
        if (n.Attr("max") is { } mx && float.TryParse(mx, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var max) && x > max) return false;
        return true;
    }

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
                // ::before / ::after (and the legacy one-colon spelling), ::marker (the list
                // marker span), ::placeholder (the field's placeholder, colour only); other
                // pseudo-elements skip the rule.
                if (name is "-webkit-input-placeholder" or "-moz-placeholder" or "-ms-input-placeholder") name = "placeholder";
                if (name is not ("before" or "after" or "marker" or "placeholder" or "first-letter" or "first-line" or "backdrop" or "-webkit-scrollbar" or "-webkit-scrollbar-thumb" or "-webkit-scrollbar-track")) return false;
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
                case "only-child":
                    compound.Pseudos.Add(n => ElementCount(n) == 1);
                    break;
                case "first-of-type":
                    compound.Pseudos.Add(n => TypeIndex(n) == 0);
                    break;
                case "last-of-type":
                    compound.Pseudos.Add(n => TypeIndex(n) == TypeCount(n) - 1);
                    break;
                case "only-of-type":
                    compound.Pseudos.Add(n => TypeCount(n) == 1);
                    break;
                case "nth-of-type":
                case "nth-last-of-type":
                case "nth-last-child":
                {
                    var (a, b) = ParseNth(arg);
                    var ofType = name.EndsWith("of-type", StringComparison.Ordinal);
                    var fromEnd = name.StartsWith("nth-last", StringComparison.Ordinal);
                    compound.Pseudos.Add(n =>
                    {
                        var idx = ofType ? TypeIndex(n) : ElementIndex(n);
                        var cnt = ofType ? TypeCount(n) : ElementCount(n);
                        var k = (fromEnd ? cnt - 1 - idx : idx) + 1;
                        if (a == 0) return k == b;
                        var m = k - b;
                        return m % a == 0 && m / a >= 0;
                    });
                    break;
                }
                case "empty":
                    compound.Pseudos.Add(n =>
                    {
                        foreach (var c in n.Children)
                            if (!c.IsText || c.Text.Trim().Length > 0) return false;
                        return true;
                    });
                    break;
                case "is":
                case "where":
                case "matches":
                {
                    var any = new List<CssSelector>();
                    foreach (var part in SplitTopLevel(arg, ','))
                    {
                        var inner = ParseSelector(part.Trim(), warn);
                        if (inner != null) any.Add(inner);
                    }
                    if (any.Count == 0) return false;
                    compound.Pseudos.Add(n => { foreach (var s in any) if (s.Matches(n)) return true; return false; });
                    break;
                }
                case "has":
                {
                    // :has(> x) tests the children, :has(x) every descendant.
                    var wants = new List<(bool childOnly, CssSelector sel)>();
                    foreach (var part in SplitTopLevel(arg, ','))
                    {
                        var t = part.Trim();
                        var childOnly = t.StartsWith(">", StringComparison.Ordinal);
                        if (childOnly) t = t.Substring(1).Trim();
                        var inner = ParseSelector(t, warn);
                        if (inner != null) wants.Add((childOnly, inner));
                    }
                    if (wants.Count == 0) return false;
                    compound.Pseudos.Add(n =>
                    {
                        foreach (var (childOnly, sel) in wants)
                            if (AnyDescendant(n, sel, childOnly)) return true;
                        return false;
                    });
                    break;
                }
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
                    var none = new List<CssSelector>();
                    foreach (var part in SplitTopLevel(arg, ','))
                    {
                        var inner = ParseSelector(part.Trim(), warn);
                        if (inner != null) none.Add(inner);
                    }
                    if (none.Count == 0) return false;
                    compound.Pseudos.Add(n => { foreach (var s in none) if (s.Matches(n)) return false; return true; });
                    break;
                }
                case "checked":
                    compound.Pseudos.Add(n => n.Attr("checked") != null || n.Attr("selected") != null);
                    break;
                case "disabled":
                    compound.Pseudos.Add(n => n.Attr("disabled") != null);
                    break;
                case "enabled":
                    compound.Pseudos.Add(n => n.Attr("disabled") == null);
                    break;
                case "hover":
                    UsesPointerState = true;
                    compound.Pseudos.Add(n => n.Attr("data-hover") != null);
                    break;
                case "active":
                    UsesPointerState = true;
                    compound.Pseudos.Add(n => n.Attr("data-active") != null);
                    break;
                case "focus": case "focus-visible":
                    UsesPointerState = true;
                    compound.Pseudos.Add(n => n.Attr("data-focus") != null);
                    break;
                case "focus-within":
                    UsesPointerState = true;
                    compound.Pseudos.Add(n => n.Attr("data-focus") != null || AnyDescendant(n, FocusSel, false));
                    break;
                case "required": compound.Pseudos.Add(n => n.Attr("required") != null); break;
                case "optional": compound.Pseudos.Add(n => IsField(n) && n.Attr("required") == null); break;
                case "read-only": compound.Pseudos.Add(n => !IsField(n) || n.Attr("readonly") != null || n.Attr("disabled") != null); break;
                case "read-write": compound.Pseudos.Add(n => IsField(n) && n.Attr("readonly") == null && n.Attr("disabled") == null); break;
                case "placeholder-shown": compound.Pseudos.Add(n => n.Attr("placeholder") != null && string.IsNullOrEmpty(FieldValue(n))); break;
                case "default": compound.Pseudos.Add(n => n.Attr("checked") != null || n.Attr("selected") != null || n.Attr("default") != null); break;
                case "indeterminate": compound.Pseudos.Add(n => n.Attr("indeterminate") != null || (n.Tag == "progress" && n.Attr("value") == null)); break;
                case "valid": compound.Pseudos.Add(n => IsField(n) && Valid(n)); break;
                case "invalid": compound.Pseudos.Add(n => IsField(n) && !Valid(n)); break;
                case "user-invalid": compound.Pseudos.Add(n => IsField(n) && n.Attr("data-touched") != null && !Valid(n)); break;
                case "user-valid": compound.Pseudos.Add(n => IsField(n) && n.Attr("data-touched") != null && Valid(n)); break;
                case "in-range": compound.Pseudos.Add(n => InRange(n) == true); break;
                case "out-of-range": compound.Pseudos.Add(n => InRange(n) == false); break;
                case "open": compound.Pseudos.Add(n => n.Attr("open") != null); break;
                case "modal": compound.Pseudos.Add(n => n.Tag == "dialog" && n.Attr("data-modal") != null); break;
                case "link": case "any-link": compound.Pseudos.Add(n => n.Tag is "a" or "area" && n.Attr("href") != null); break;
                case "lang":
                {
                    var want = arg.Trim().Trim('"', '\'').ToLowerInvariant();
                    compound.Pseudos.Add(n =>
                    {
                        for (var p = n; p != null; p = p.Parent)
                            if (p.Attr("lang") is { } l) { l = l.ToLowerInvariant(); return l == want || l.StartsWith(want + "-", StringComparison.Ordinal); }
                        return false;
                    });
                    break;
                }
                case "dir":
                {
                    var want = arg.Trim().ToLowerInvariant();
                    compound.Pseudos.Add(n =>
                    {
                        for (var p = n; p != null; p = p.Parent)
                            if (p.Attr("dir") is { } d) return string.Equals(d, want, StringComparison.OrdinalIgnoreCase);
                        return want == "ltr";
                    });
                    break;
                }
                case "visited": case "target":
                    compound.Pseudos.Add(_ => false);
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static int TypeIndex(HtmlNode n)
    {
        if (n.Parent == null) return 0;
        var i = 0;
        foreach (var c in n.Parent.Children)
        {
            if (c == n) return i;
            if (!c.IsText && string.Equals(c.Tag, n.Tag, StringComparison.OrdinalIgnoreCase)) i++;
        }
        return i;
    }

    private static int TypeCount(HtmlNode n)
    {
        if (n.Parent == null) return 1;
        var i = 0;
        foreach (var c in n.Parent.Children)
            if (!c.IsText && string.Equals(c.Tag, n.Tag, StringComparison.OrdinalIgnoreCase)) i++;
        return i;
    }

    private static bool AnyDescendant(HtmlNode n, CssSelector sel, bool childOnly)
    {
        foreach (var c in n.Children)
        {
            if (c.IsText) continue;
            if (sel.Matches(c)) return true;
            if (!childOnly && AnyDescendant(c, sel, false)) return true;
        }
        return false;
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

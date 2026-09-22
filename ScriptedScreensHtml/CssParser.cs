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
/// Pseudo-classes and attribute tests are extra predicates on the compound; a name may carry
/// escaped punctuation (`.dark\:bg-x`), which is unescaped only once it is taken as a name.
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

    /// <summary>
    /// The @container queries this selector sits inside, innermost last; all must hold. Carried
    /// on the selector rather than on the rule because every part of the renderer asks the same
    /// question through <see cref="Matches"/> - the cascade, the ::placeholder probe, the
    /// ::first-letter probe - so one check here covers all of them.
    /// </summary>
    public List<CssContainerQuery>? Containers;

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
        // Last, because it walks the ancestors again and reads their laid-out boxes: a selector
        // that was never going to match should not pay for it.
        if (Containers != null)
            foreach (var q in Containers)
                if (!q.Holds(node)) return false;
        return true;
    }
}

/// <summary>
/// One `@container [name] &lt;condition&gt;`: which container it asks about, and what it asks.
/// </summary>
/// <remarks>
/// The name used to be thrown away and the condition answered by <see cref="CssParser.MediaMatches"/>
/// against the page's design width, so `@container sidebar (min-width: 40em)` and
/// `@container main (min-width: 40em)` were the same query and a 200px panel inside a 900px page
/// took the 900px branch. Both halves of that are fixed here: the name is kept and matched, and
/// the condition is answered against the container's own laid-out content box.
/// </remarks>
internal sealed class CssContainerQuery
{
    /// <summary>The container-name asked for, null when the query names none (nearest container wins).</summary>
    public string? Name;
    /// <summary>The condition as written, `(min-width: 40em)`.</summary>
    public string Condition = string.Empty;

    public bool Holds(HtmlNode subject) => CssParser.ContainerHolds(this, subject);

    public override string ToString() => "@container " + (Name == null ? string.Empty : Name + " ") + Condition;
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
                    if (header.StartsWith("supports", StringComparison.OrdinalIgnoreCase)
                        ? SupportsMatches(header.Substring(8), warn)
                        : MediaMatches(header.Substring(5), warn))
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
                    CssContainerQuery? query = null;
                    if (header.StartsWith("container", StringComparison.OrdinalIgnoreCase))
                    {
                        query = ContainerQuery(header.Substring(9), warn);
                        take = query != null;
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
                            if (query != null)
                                foreach (var sel in r.Selectors) (sel.Containers ??= new List<CssContainerQuery>()).Add(query);
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
                if (c == '\\') { i++; continue; }
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                if (c != sep || depth > 0) continue;
            }
            parts.Add(text.Substring(start, i - start));
            start = i + 1;
        }
        return parts;
    }

    // ---- CSS escapes ----
    // Tailwind spells a class named `dark:bg-x` as `.dark\:bg-x`, and one named `[&>tr]:border`
    // as `.\[\&\>tr\]\:border`. So a selector's punctuation is only punctuation when unescaped,
    // and every scan below has to step over `\x` as one unit. Unescaping early is not an option:
    // it would turn those names straight back into a pseudo-class and a child combinator.

    /// <summary>Index of the first unescaped <paramref name="want"/>, or -1.</summary>
    private static int IndexOfBare(string s, char want)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == want) return i;
        }
        return -1;
    }

    /// <summary>`\:` and `\[` back to `:` and `[`; `\41 ` and `\1F600` to their character.</summary>
    private static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            var start = ++i;
            while (i < s.Length && i - start < 6 && Uri.IsHexDigit(s[i])) i++;
            if (i > start && int.TryParse(s.Substring(start, i - start), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var cp) && cp > 0 && cp <= 0x10FFFF)
            {
                sb.Append(char.ConvertFromUtf32(cp));
                if (i < s.Length && s[i] == ' ') i++; // the space that ends a hex escape is not part of the name
                i--;
                continue;
            }
            i = start;
            sb.Append(s[i]);
        }
        return sb.ToString();
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
                if (kind == "media" && !MediaMatches(header.Substring(5), warn)) continue;
                if (kind == "supports" && !SupportsMatches(header.Substring(8), warn)) continue;
                CssContainerQuery? query = null;
                if (kind == "container")
                {
                    query = ContainerQuery(header.Substring(9), warn);
                    if (query == null) continue;
                }
                if (kind == "starting-style")
                    ParseRule("&", nbody, selectors, StartingRules, ref order, warn, keyframes);
                else if (kind is "media" or "supports" or "layer" or "container")
                {
                    var from = rules.Count;
                    ParseRule("&", nbody, selectors, rules, ref order, warn, keyframes);
                    for (var r = from; query != null && r < rules.Count; r++)
                        foreach (var inside in rules[r].Selectors) (inside.Containers ??= new List<CssContainerQuery>()).Add(query);
                }
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

    /// <summary>Attribute names that appear in selectors, collected while parsing.</summary>
    public static readonly HashSet<string> UsedAttributes = new(StringComparer.OrdinalIgnoreCase);
    // declared above FocusSel: that selector is parsed while the type initialises and records its attribute name
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
    /// A media query list against the console: media types, `not`/`only`/`and`/commas, the
    /// `min-`/`max-` prefixes, the range syntax (`width >= 400px`, `200px &lt; width &lt; 600px`)
    /// and the discrete features in <see cref="Discrete"/>.
    /// </summary>
    /// <remarks>
    /// An unknown feature is false, as in a browser - but in a browser that is because the
    /// browser genuinely lacks it, and here it is usually because nobody has answered it yet.
    /// Either way the author's whole block vanishes, so it says which feature did it, once.
    /// The one-argument overload exists because ScriptHost binds this as a Func&lt;string,bool&gt;.
    /// </remarks>
    public static bool MediaMatches(string query) => MediaMatches(query, null);

    public static bool MediaMatches(string query, Action<string>? warn)
    {
        foreach (var alternative in SplitTopLevel(query, ','))
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
                if (c == "print" || c == "speech" || c == "tty" || c == "tv" || c == "projection" || c == "handheld") { ok = false; break; }
                var colon = c.IndexOf(':');
                var pass = colon < 0 && (c.IndexOf('<') >= 0 || c.IndexOf('>') >= 0 || c.IndexOf('=') >= 0)
                    ? RangeClause(c, warn)
                    : Feature(colon < 0 ? c : c.Substring(0, colon).Trim(), colon < 0 ? null : c.Substring(colon + 1).Trim(), warn);
                if (!pass) { ok = false; break; }
            }
            if (ok != negate) return true;
        }
        return false;
    }

    // ---- @container ----------------------------------------------------------------------

    /// <summary>
    /// A node seen as a query container: the declarations that won its cascade, and its laid-out
    /// content box. The renderer installs this for the page it is cascading, the way
    /// <see cref="SupportsOracle"/> is installed - the parser stays Unity-free and knows nothing
    /// about VisualElements or layout.
    /// </summary>
    /// <remarks>
    /// Installing this is a promise to re-cascade a container's subtree when its box changes. A
    /// page's first cascade runs BEFORE any layout, so every query answers false there and only
    /// the re-cascade sees the real size. With nothing installed every container query says so
    /// and matches nothing, which is a browser's answer for a query it cannot resolve.
    /// </remarks>
    internal static Func<HtmlNode, (Dictionary<string, string> css, float width, float height)?>? ContainerInfo;

    /// <summary>Where a container query's complaints go: the page's warning list, installed with <see cref="ContainerInfo"/>.</summary>
    internal static Action<string>? ContainerWarn;

    /// <summary>
    /// Point the parser at the page about to be cascaded. One call rather than two field writes so
    /// that a build with no caller still compiles: the analyzers reject both a never-assigned field
    /// and one explicitly set to its own default.
    /// </summary>
    internal static void InstallContainers(Func<HtmlNode, (Dictionary<string, string> css, float width, float height)?>? info, Action<string>? warn)
    {
        ContainerInfo = info;
        ContainerWarn = warn;
    }

    /// <summary>Whether these cascaded declarations make the element a size query container.</summary>
    internal static bool IsQueryContainer(Dictionary<string, string> css) => ContainerTypeOf(css) != null;

    /// <summary>"inline-size", "size", or null when the element is no size container (the `container` shorthand included).</summary>
    private static string? ContainerTypeOf(Dictionary<string, string> css)
    {
        var type = css.TryGetValue("container-type", out var t) ? t : null;
        if (type == null && css.TryGetValue("container", out var shorthand))
        {
            var slash = shorthand.IndexOf('/');
            type = slash >= 0 ? shorthand.Substring(slash + 1) : null;
        }
        type = type?.Trim().ToLowerInvariant();
        return type is "inline-size" or "size" ? type : null;
    }

    /// <summary>Whether this element answers to that container-name (a name list, or the `container` shorthand's first half).</summary>
    private static bool HasContainerName(Dictionary<string, string> css, string want)
    {
        var names = css.TryGetValue("container-name", out var n) ? n : null;
        if (names == null && css.TryGetValue("container", out var shorthand))
        {
            var slash = shorthand.IndexOf('/');
            names = slash >= 0 ? shorthand.Substring(0, slash) : shorthand;
        }
        if (names == null) return false;
        foreach (var one in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (string.Equals(one.Trim(), want, StringComparison.Ordinal))   // a CSS ident is case-sensitive
                return true;
        return false;
    }

    /// <summary>
    /// `@container [name] &lt;condition&gt;`: the optional name, then the condition. Null when the
    /// header is one this renderer cannot answer, which skips the block as an unreadable @media does.
    /// </summary>
    private static CssContainerQuery? ContainerQuery(string header, Action<string>? warn)
    {
        var text = header.Trim();
        string? name = null;
        if (text.Length > 0 && text[0] != '(')
        {
            var space = text.IndexOf(' ');
            var first = space < 0 ? text : text.Substring(0, space);
            // `not (...)`, `style(...)` and `scroll-state(...)` start the condition; anything else is the name
            if (first.IndexOf('(') < 0 && !string.Equals(first, "not", StringComparison.OrdinalIgnoreCase))
            {
                name = first;
                text = space < 0 ? string.Empty : text.Substring(space + 1).Trim();
            }
        }
        if (text.Length == 0)
        {
            ReportContainer($"css: @container {header.Trim()} has no condition, so that block is skipped", warn);
            return null;
        }
        if (text.IndexOf("style(", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("scroll-state(", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            ReportContainer($"css: @container {text} asks about a style or a scroll state, which this renderer cannot answer, so that block is skipped", warn);
            return null;
        }
        return new CssContainerQuery { Name = name, Condition = text };
    }

    /// <summary>
    /// The query answered against the nearest ancestor that establishes a containment context -
    /// by name when the query gives one, as a browser does. Not the page's design width, which is
    /// what this used to answer and what made every container query the same query.
    /// </summary>
    internal static bool ContainerHolds(CssContainerQuery query, HtmlNode subject)
    {
        if (ContainerInfo == null)
        {
            ReportContainer($"css: {query} cannot be answered - nothing here can read a container's laid-out size, so that block is skipped", ContainerWarn);
            return false;
        }
        for (var a = subject.Parent; a != null; a = a.Parent)
        {
            if (ContainerInfo(a) is not { } info) continue;
            var type = ContainerTypeOf(info.css);
            if (type == null) continue;                                      // not a container at all
            if (query.Name != null && !HasContainerName(info.css, query.Name)) continue;
            return ContainerAnswers(query, type, info.width, info.height);
        }
        ReportContainer(query.Name == null
            ? $"css: {query} has no ancestor with a container-type above it, so that block is skipped"
            : $"css: {query} has no ancestor named \"{query.Name}\" with a container-type above it, so that block is skipped", ContainerWarn);
        return false;
    }

    private static bool ContainerAnswers(CssContainerQuery query, string type, float width, float height)
    {
        // the container syntax's own feature names; everything else a size query may ask is @media's
        var condition = query.Condition.Replace("inline-size", "width").Replace("block-size", "height");
        if (type == "inline-size" && (condition.IndexOf("height", StringComparison.Ordinal) >= 0
            || condition.IndexOf("orientation", StringComparison.Ordinal) >= 0
            || condition.IndexOf("aspect-ratio", StringComparison.Ordinal) >= 0))
        {
            ReportContainer($"css: {query} asks about the block axis, which only `container-type: size` gives - `inline-size` answers the inline axis alone, so that block is skipped", ContainerWarn);
            return false;
        }
        // ponytail: a container with no width yet - the cascade before the page's first layout -
        // answers false, which is also what a genuinely zero-wide container gives a min-width query.
        if (!(width > 0f)) return false;
        var w = ViewportWidth;
        var h = ViewportHeight;
        ViewportWidth = width;
        // inline-size contains the inline axis only, so the block axis is not answerable: NaN
        // rather than the page's height, which would answer a height query from the wrong box.
        ViewportHeight = type == "size" ? height : float.NaN;
        try { return MediaMatches(condition, ContainerWarn); }
        finally { ViewportWidth = w; ViewportHeight = h; }
    }

    /// <summary>Once per distinct message, cleared with the rest by <see cref="ForgetReported"/>.</summary>
    private static void ReportContainer(string message, Action<string>? warn)
    {
        if (ReportedMedia.Add(message)) warn?.Invoke(message);
    }

    /// <summary>
    /// The range syntax: `width >= 400px`, `400px &lt;= width`, `200px &lt; width &lt; 600px`,
    /// `width = 400px`. The feature is whichever operand is not a quantity.
    /// </summary>
    private static bool RangeClause(string c, Action<string>? warn)
    {
        var operands = new List<string>();
        var ops = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < c.Length; i++)
        {
            if (c[i] != '<' && c[i] != '>' && c[i] != '=') { sb.Append(c[i]); continue; }
            var op = c[i].ToString();
            if (i + 1 < c.Length && c[i + 1] == '=') { op += "="; i++; }
            operands.Add(sb.ToString().Trim());
            sb.Clear();
            ops.Add(op);
        }
        operands.Add(sb.ToString().Trim());
        if (ops.Count == 0 || ops.Count > 2 || operands.Count != ops.Count + 1) return false;
        var at = operands.FindIndex(o => o.Length > 0 && float.IsNaN(Px(o)) && !char.IsDigit(o[0]) && o[0] != '.' && o[0] != '-');
        if (at < 0) return false;
        var name = operands[at];
        var mine = FeatureNumber(name);
        if (float.IsNaN(mine)) { Unknown(name, warn); return false; }
        for (var i = 0; i < ops.Count; i++)
        {
            // `a < feature` is `feature > a`, so an operator on the feature's left is flipped
            var other = i < at ? operands[i] : operands[i + 1];
            var op = i < at ? Flip(ops[i]) : ops[i];
            var want = Operand(name, other);
            if (float.IsNaN(want)) return BadOperand(name, other, warn);
            var eps = name.EndsWith("aspect-ratio", StringComparison.Ordinal) ? 0.01f : 0.5f;
            var pass = op switch
            {
                "<" => mine < want,
                "<=" => mine <= want + eps,
                ">" => mine > want,
                ">=" => mine >= want - eps,
                _ => Math.Abs(mine - want) < eps,
            };
            if (!pass) return false;
        }
        return true;
    }

    private static string Flip(string op) => op switch { "<" => ">", "<=" => ">=", ">" => "<", ">=" => "<=", _ => op };

    /// <summary>One `(feature)` or `(feature: value)` clause; <paramref name="value"/> null is the boolean form.</summary>
    private static bool Feature(string name, string? value, Action<string>? warn)
    {
        if (name.StartsWith("-webkit-", StringComparison.Ordinal)) name = name.Substring(8);
        else if (name.StartsWith("-moz-", StringComparison.Ordinal)) name = name.Substring(5);
        var cmp = 0;
        if (name.StartsWith("min-", StringComparison.Ordinal)) { cmp = 1; name = name.Substring(4); }
        else if (name.StartsWith("max-", StringComparison.Ordinal)) { cmp = -1; name = name.Substring(4); }
        if (name == "device-pixel-ratio") name = "resolution";

        var mine = FeatureNumber(name);
        if (!float.IsNaN(mine))
        {
            if (value == null) return mine != 0f;
            var want = Operand(name, value);
            if (float.IsNaN(want)) return BadOperand(name, value, warn);
            var eps = name.EndsWith("aspect-ratio", StringComparison.Ordinal) ? 0.01f : 0.5f;
            return cmp > 0 ? mine >= want - eps : cmp < 0 ? mine <= want + eps : Math.Abs(mine - want) < eps;
        }

        if (Discrete(name) is { } answer)
            // the boolean form asks whether the feature is "on", which for a preference means a preference was expressed
            return value == null ? answer is not ("none" or "no-preference") : value == answer;

        Unknown(name, warn);
        return false;
    }

    /// <summary>The console's own value for a range-capable feature, NaN when it has none.</summary>
    private static float FeatureNumber(string name) => name switch
    {
        "width" or "device-width" => ViewportWidth,
        "height" or "device-height" => ViewportHeight,
        "aspect-ratio" or "device-aspect-ratio" => ViewportHeight > 0f ? ViewportWidth / ViewportHeight : float.NaN,
        "resolution" => 1f,                                  // the page is drawn as vectors: one device pixel per CSS pixel
        "color" => 8f,                                       // bits per colour channel
        "color-index" or "monochrome" or "grid" => 0f,       // not a palette, not monochrome, not a character grid
        _ => float.NaN,
    };

    /// <summary>
    /// The console as a media-query respondent. It is a lit panel in a dark ship, so the
    /// scheme is dark; the game's crosshair is a real pointer that can rest on a box, so
    /// hover and a fine pointer are true; nothing here carries a user preference, so every
    /// preference answers with its neutral value. Null means the feature is not answerable.
    /// </summary>
    private static string? Discrete(string name) => name switch
    {
        "orientation" => ViewportWidth >= ViewportHeight ? "landscape" : "portrait",
        "prefers-color-scheme" => "dark",
        "prefers-reduced-motion" or "prefers-reduced-transparency" or "prefers-reduced-data" or "prefers-contrast" => "no-preference",
        "forced-colors" or "inverted-colors" => "none",
        "hover" or "any-hover" => "hover",
        "pointer" or "any-pointer" => "fine",
        "scripting" => "enabled",
        "update" => "fast",
        "display-mode" => "fullscreen",
        "dynamic-range" or "video-dynamic-range" => "standard",
        "overflow-block" or "overflow-inline" => "scroll",
        "color-gamut" => "srgb",
        "scan" => "progressive",
        _ => null,
    };

    private static readonly HashSet<string> ReportedMedia = new(StringComparer.Ordinal);

    private static void Unknown(string feature, Action<string>? warn)
    {
        if (ReportedMedia.Add(feature))
            warn?.Invoke($"css: @media ({feature}) is not something a console can answer, so that block is skipped");
    }

    /// <summary>
    /// A feature this console can answer, written against a quantity it cannot read. The block
    /// vanishes either way; saying so is the difference between an author fixing the unit and an
    /// author wondering why a rule that looks right does nothing.
    /// </summary>
    private static bool BadOperand(string feature, string text, Action<string>? warn)
    {
        if (ReportedMedia.Add(feature + ":" + text))
            warn?.Invoke($"css: @media ({feature}) was given \"{text.Trim()}\", which is not a length this parser reads, so that block is skipped");
        return false;
    }

    /// <summary>A written value in the feature's own unit: a ratio, dppx, or px.</summary>
    private static float Operand(string name, string text)
    {
        var v = text.Trim();
        if (name.EndsWith("aspect-ratio", StringComparison.Ordinal))
        {
            var parts = v.Split('/');
            var b = parts.Length > 1 ? Px(parts[1]) : 1f;
            return b == 0f ? float.NaN : Px(parts[0]) / b;
        }
        if (name == "resolution")
        {
            if (v.EndsWith("dppx", StringComparison.OrdinalIgnoreCase)) return Px(v.Substring(0, v.Length - 4));
            if (v.EndsWith("dpcm", StringComparison.OrdinalIgnoreCase)) return Px(v.Substring(0, v.Length - 4)) / 37.795275f;
            if (v.EndsWith("dpi", StringComparison.OrdinalIgnoreCase)) return Px(v.Substring(0, v.Length - 3)) / 96f;
            if (v.EndsWith("x", StringComparison.OrdinalIgnoreCase)) return Px(v.Substring(0, v.Length - 1));
        }
        // a media query's em is the initial font size, never the element's: there is no element yet
        if (v.EndsWith("rem", StringComparison.OrdinalIgnoreCase)) return Px(v.Substring(0, v.Length - 3)) * 16f;
        if (v.EndsWith("em", StringComparison.OrdinalIgnoreCase)) return Px(v.Substring(0, v.Length - 2)) * 16f;
        // The rest of the lengths a query may legally be written in. Fixed ratios, and the
        // viewport ones the query is asking about anyway; none of them needs an element or a
        // font, which is why they belong here and not in the cascade's own unit table. Without
        // them the operand was NaN and the whole block vanished with nothing said.
        foreach (var (suffix, scale) in MediaUnits)
            if (v.Length > suffix.Length && v.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return Px(v.Substring(0, v.Length - suffix.Length)) * scale();
        return Px(v);
    }

    /// <summary>Longest suffix first, so `vmin` is not read as `n` and `pc` not as `c`.</summary>
    private static readonly (string Suffix, Func<float> Scale)[] MediaUnits =
    {
        ("vmin", () => Math.Min(ViewportWidth, ViewportHeight) / 100f),
        ("vmax", () => Math.Max(ViewportWidth, ViewportHeight) / 100f),
        ("vw", () => ViewportWidth / 100f),
        ("vh", () => ViewportHeight / 100f),
        ("mm", () => 96f / 25.4f),
        ("cm", () => 96f / 2.54f),
        ("in", () => 96f),
        ("pt", () => 96f / 72f),
        ("pc", () => 16f),
        ("q", () => 96f / 101.6f),
    };

    /// <summary>A px length or plain number as a float, NaN otherwise. Unity-free on purpose: this file is tested headless.</summary>
    private static float Px(string value)
    {
        var v = value.Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 2);
        return float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : float.NaN;
    }

    /// <summary>
    /// An @supports condition: `not`, `and`, `or`, parentheses, and `selector(...)` answered
    /// by actually parsing the selector. `font-tech()`/`font-format()` are false - no font
    /// here is loaded by technology. A plain `(property: value)` leaf is TRUE, because this
    /// parser does accept every declaration; whether the value then DRAWS is a different
    /// question, and COVERAGE.md's rather than this one's.
    /// </summary>
    /// <remarks>
    /// `not` is the half that mattered. Taken unconditionally - which is what happened before
    /// this existed - a page's `@supports not (...)` fallback was applied on top of the rules
    /// it was the fallback FOR, and being later in the sheet it won.
    /// </remarks>
    internal static bool SupportsMatches(string condition, Action<string>? warn)
    {
        var cond = condition.Trim();
        if (cond.Length == 0) return true;

        var any = SplitKeyword(cond, " or ");
        if (any.Count > 1)
        {
            foreach (var part in any)
                if (SupportsMatches(part, warn)) return true;
            return false;
        }
        var all = SplitKeyword(cond, " and ");
        if (all.Count > 1)
        {
            foreach (var part in all)
                if (!SupportsMatches(part, warn)) return false;
            return true;
        }
        if (cond.StartsWith("not ", StringComparison.OrdinalIgnoreCase)) return !SupportsMatches(cond.Substring(4), warn);
        if (cond.StartsWith("not(", StringComparison.OrdinalIgnoreCase)) return !SupportsMatches(cond.Substring(3), warn);
        if (cond.StartsWith("selector(", StringComparison.OrdinalIgnoreCase))
            return ParseSelector(Inside(cond, 8).Trim(), null) != null;
        if (cond.StartsWith("font-tech(", StringComparison.OrdinalIgnoreCase) || cond.StartsWith("font-format(", StringComparison.OrdinalIgnoreCase))
            return false;
        if (cond[0] != '(') return true;

        var inner = Inside(cond, 0);
        // `(a: b)` is a declaration; anything else in parentheses is a condition of its own
        if (inner.IndexOf(':') > 0 && SplitKeyword(inner, " and ").Count == 1 && SplitKeyword(inner, " or ").Count == 1
            && !inner.TrimStart().StartsWith("not", StringComparison.OrdinalIgnoreCase))
        {
            var colon = inner.IndexOf(':');
            return Declares(inner.Substring(0, colon).Trim(), inner.Substring(colon + 1).Trim());
        }
        return SupportsMatches(inner, warn);
    }

    /// <summary>
    /// Whether a declaration does anything here - the question <c>@supports (a: b)</c> asks.
    /// </summary>
    /// <remarks>
    /// This returned TRUE for every declaration, including nonsense, and that is not a harmless
    /// over-claim: it makes the FALLBACK form always wrong. An author writing the standard
    ///
    ///   @supports not (backdrop-filter: blur(4px)) { .panel { background: #222 } }
    ///
    /// had their fallback silently dropped, on a renderer that does not do backdrop-filter - so the
    /// one arm that would have drawn something was the one thrown away. The positive form was
    /// harmless by comparison: it applies an enhancement that then does nothing.
    ///
    /// The answer has to come from the code that applies declarations, which is Unity-side, and
    /// CssParser is deliberately Unity-free (it is what lets the whole front end be tested
    /// headlessly). So the applier installs itself here instead of being called directly. With no
    /// oracle installed the old answer stands, which keeps the headless tests working unchanged.
    /// </remarks>
    internal static Func<string, string, bool>? SupportsOracle;

    private static bool Declares(string name, string value)
    {
        if (name.StartsWith("--", StringComparison.Ordinal)) return true;   // any custom property
        return SupportsOracle?.Invoke(name, value) ?? true;
    }

    /// <summary>The text between the parenthesis at or after <paramref name="from"/> and its match.</summary>
    private static string Inside(string s, int from)
    {
        var open = s.IndexOf('(', from);
        if (open < 0) return string.Empty;
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return s.Substring(open + 1, i - open - 1);
        }
        return s.Substring(open + 1);
    }

    /// <summary>Split on a keyword outside parentheses.</summary>
    private static List<string> SplitKeyword(string s, string keyword)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (depth == 0 && i + keyword.Length <= s.Length
                     && string.Compare(s, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                parts.Add(s.Substring(start, i - start));
                start = i + keyword.Length;
                i += keyword.Length - 1;
            }
        }
        parts.Add(s.Substring(start));
        return parts;
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
            {
                list.Add(new CssDeclaration(name, value, important));
                if (name == "font") list.AddRange(FontLonghands(value, important));
            }
        }
        return list;
    }

    /// <summary>font: [style] [weight] size[/line-height] family-list, as its longhands (a later longhand in the same list still wins).</summary>
    private static IEnumerable<CssDeclaration> FontLonghands(string v, bool important)
    {
        var parts = SplitTopLevel(v.Trim(), ' ');
        var familyStart = -1;
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i].Trim();
            if (p.Length == 0) continue;
            if (p == "italic" || p == "oblique") { yield return new CssDeclaration("font-style", p, important); continue; }
            if (p == "bold" || p == "bolder" || p == "lighter" || (float.TryParse(p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var wn) && wn >= 100f)) { yield return new CssDeclaration("font-weight", p, important); continue; }
            if (p == "normal" || p == "small-caps") continue;
            if (char.IsDigit(p[0]) || p[0] == '.')
            {
                var slash = p.IndexOf('/');
                yield return new CssDeclaration("font-size", slash > 0 ? p.Substring(0, slash) : p, important);
                if (slash > 0 && slash + 1 < p.Length) yield return new CssDeclaration("line-height", p.Substring(slash + 1), important);
                familyStart = i + 1;
                break;
            }
            // a keyword font (menu, caption...) or an unknown token: no longhands
            yield break;
        }
        if (familyStart >= 0 && familyStart < parts.Count)
            yield return new CssDeclaration("font-family", string.Join(" ", parts.GetRange(familyStart, parts.Count - familyStart)).Trim(), important);
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
            if (part == "||")
            {
                // The column combinator addresses a <col>'s cells. Nothing here models a
                // column, and swallowing it silently made a tag selector named "||".
                warn?.Invoke($"css: selector \"{text}\" skipped: the column combinator || is not supported");
                return null;
            }
            var compound = new CssCompound { ChildOfPrevious = childNext, SiblingOfPrevious = siblingNext, GeneralSiblingOfPrevious = generalNext };
            childNext = false;
            siblingNext = false;
            generalNext = false;
            // Pseudos come out first, arguments and all: `:not([hidden]).x` has a bracket that
            // belongs to the :not(), and a tag/class can follow a pseudo (`:where(a,b).x`).
            SplitCompound(part, out part, out var pseudoText);
            if (pseudoText.Length > 0 && !ParsePseudos(pseudoText, compound, warn))
            {
                warn?.Invoke($"css: selector \"{text}\" skipped: pseudo not supported");
                return null;
            }
            // [attr], [attr=v], [attr~=v], [attr|=v], [attr^=v], [attr$=v], [attr*=v]
            int open;
            while ((open = IndexOfBare(part, '[')) >= 0)
            {
                var closeAt = IndexOfBare(part.Substring(open + 1), ']');
                if (closeAt < 0) { warn?.Invoke($"css: selector \"{text}\" skipped: unclosed ["); return null; }
                closeAt += open + 1;
                if (!AttributeTest(part.Substring(open + 1, closeAt - open - 1), compound)) { warn?.Invoke($"css: selector \"{text}\" skipped: bad attribute test"); return null; }
                part = part.Substring(0, open) + part.Substring(closeAt + 1);
            }
            if (part.Length == 0) part = "*";
            var i = 0;
            while (i < part.Length)
            {
                var kind = part[i];
                if (kind == '.' || kind == '#')
                    i++;
                var start = i;
                while (i < part.Length && part[i] != '.' && part[i] != '#')
                    i += part[i] == '\\' ? 2 : 1;
                var name = Unescape(part.Substring(start, Math.Min(i, part.Length) - start));
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
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            // `.\>x` is a class called ">x", not a child combinator. A hex escape takes its
            // terminating space with it: Tailwind spells a class named `2xl:flex` `.\32 xl\:flex`,
            // and splitting there would silently make it a descendant selector matching nothing.
            if (ch == '\\')
            {
                sb.Append(ch);
                if (i + 1 >= text.Length) continue;
                if (!Uri.IsHexDigit(text[i + 1])) { sb.Append(text[++i]); continue; }
                var hex = 0;
                while (i + 1 < text.Length && hex < 6 && Uri.IsHexDigit(text[i + 1])) { sb.Append(text[++i]); hex++; }
                if (i + 1 < text.Length && text[i + 1] == ' ') sb.Append(text[++i]);
                continue;
            }
            if (ch == '[' || ch == '(') depth++;
            else if (ch == ']' || ch == ')') depth--;
            // `||` is a combinator; a single `|` is a namespace separator and stays glued on
            if (depth == 0 && ch == '|' && i + 1 < text.Length && text[i + 1] == '|') { Flush(); parts.Add("||"); i++; continue; }
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
        var cmp = StringComparison.Ordinal;
        if (op < 0) name = body;
        else
        {
            var before = body.Substring(0, op);
            var last = before.Length > 0 ? before[before.Length - 1] : ' ';
            kind = last switch { '~' => "word", '|' => "dash", '^' => "prefix", '$' => "suffix", '*' => "contains", _ => "equals" };
            name = kind == "equals" ? before : before.Substring(0, before.Length - 1);
            value = body.Substring(op + 1).Trim();
            // `[a=v i]` / `[a=v s]`: the flag sits after the value, outside its quotes. An
            // unquoted value cannot hold a space, so the space before the flag identifies it -
            // without that test `[data-k=xs]` loses its s.
            if (value.Length > 2 && (value[value.Length - 1] is 'i' or 'I' or 's' or 'S') && char.IsWhiteSpace(value[value.Length - 2]))
            {
                if (value[value.Length - 1] is 'i' or 'I') cmp = StringComparison.OrdinalIgnoreCase;
                value = value.Substring(0, value.Length - 2).Trim();
            }
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0]) value = value.Substring(1, value.Length - 2);
        }
        UsedAttributes.Add(name.Trim());
        name = name.Trim();
        if (name.Length == 0) return false;
        compound.Pseudos.Add(n =>
        {
            var have = n.Attr(name);
            if (have == null) return false;
            return kind switch
            {
                "exists" => true,
                "equals" => string.Equals(have, value, cmp),
                "word" => Word(have, value, cmp),
                "dash" => string.Equals(have, value, cmp) || have.StartsWith(value + "-", cmp),
                "prefix" => value.Length > 0 && have.StartsWith(value, cmp),
                "suffix" => value.Length > 0 && have.EndsWith(value, cmp),
                "contains" => value.Length > 0 && have.Contains(value, cmp),
                _ => false,
            };
        });
        return true;
    }

    private static bool Word(string have, string want, StringComparison cmp)
    {
        foreach (var w in have.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (string.Equals(w, want, cmp)) return true;
        return false;
    }

    /// <summary>
    /// A compound split into its tag/class/id text and its pseudos, each pseudo taken with its
    /// whole parenthesised argument. Pseudos are not necessarily last (`:where(a,b).x`), and an
    /// argument may hold brackets and colons of its own that are none of the caller's business.
    /// </summary>
    private static void SplitCompound(string part, out string bare, out string pseudos)
    {
        var b = new StringBuilder(part.Length);
        var p = new StringBuilder();
        for (var i = 0; i < part.Length; i++)
        {
            var c = part[i];
            if (c == '\\') { b.Append(c); if (i + 1 < part.Length) b.Append(part[++i]); continue; }
            if (c != ':') { b.Append(c); continue; }
            var start = i++;
            if (i < part.Length && part[i] == ':') i++;
            while (i < part.Length && (char.IsLetterOrDigit(part[i]) || part[i] == '-' || part[i] == '_')) i++;
            if (i < part.Length && part[i] == '(')
            {
                var depth = 0;
                for (; i < part.Length; i++)
                {
                    if (part[i] == '\\') { i++; continue; }
                    if (part[i] == '(') depth++;
                    else if (part[i] == ')' && --depth == 0) { i++; break; }
                }
            }
            p.Append(part, start, Math.Min(i, part.Length) - start);
            i--; // the character that ended the pseudo is the next compound's, or the next pseudo's ':'
        }
        bare = b.ToString();
        pseudos = p.ToString();
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
                // marker span), ::placeholder (the field's placeholder, colour only).
                if (name is "-webkit-input-placeholder" or "-moz-placeholder" or "-ms-input-placeholder") name = "placeholder";
                // Every name kept here has something that generates its node: ::before/::after and
                // ::first-letter in HtmlRenderer.AddGenerated, ::marker with the list marker span,
                // ::placeholder on a field, ::details-content under an open <details>, ::backdrop
                // under a modal dialog or open popover, and the three scrollbar parts under a
                // scrolling box (VectorEmitter reads all three). ::first-line is NOT in the list:
                // where the first line ends is only known after layout, and the text would have to
                // be split before layout runs, so it goes to NeverMatches and warns. A rule that
                // parses and then never matches is worse than one that is refused - an author can
                // act on a warning and cannot act on a paragraph that simply is not styled.
                if (name is not ("before" or "after" or "marker" or "placeholder" or "first-letter" or "backdrop" or "details-content" or "-webkit-scrollbar" or "-webkit-scrollbar-thumb" or "-webkit-scrollbar-track"))
                {
                    if (!NeverMatches(name, warn)) return false;
                    compound.Pseudos.Add(_ => false);
                    continue;
                }
                compound.PseudoElement = name;
                continue;
            }
            switch (name)
            {
                case "root":
                case "scope": // bare :scope (outside querySelector and @scope, which have their own root) is the document root
                    compound.Pseudos.Add(n => n.Tag == "html" || n.Tag == "body");
                    break;
                case "popover-open": compound.Pseudos.Add(n => n.Attr("popover") != null && n.Attr("data-popover-open") != null); break;
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
                case "nth-child":
                case "nth-of-type":
                case "nth-last-of-type":
                case "nth-last-child":
                {
                    // `:nth-child(An+B of S)`: the siblings counted are only those matching S,
                    // so the element must match it too before its position means anything.
                    var expr = arg;
                    List<CssSelector>? of = null;
                    var ofAt = OfKeyword(arg);
                    if (ofAt >= 0)
                    {
                        of = new List<CssSelector>();
                        foreach (var part in SplitTopLevel(arg.Substring(ofAt + 4), ','))
                            if (ParseSelector(part.Trim(), warn) is { } inner) of.Add(inner);
                        if (of.Count == 0) return false;
                        expr = arg.Substring(0, ofAt);
                    }
                    var (a, b) = ParseNth(expr);
                    var ofType = name.EndsWith("of-type", StringComparison.Ordinal);
                    var fromEnd = name.StartsWith("nth-last", StringComparison.Ordinal);
                    compound.Pseudos.Add(n =>
                    {
                        int idx, cnt;
                        if (of != null)
                        {
                            IndexAmong(n, of, out idx, out cnt);
                            if (idx < 0) return false;
                        }
                        else
                        {
                            idx = ofType ? TypeIndex(n) : ElementIndex(n);
                            cnt = ofType ? TypeCount(n) : ElementCount(n);
                        }
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
                    if (!NeverMatches(name, warn)) return false;
                    compound.Pseudos.Add(_ => false);
                    break;
            }
        }
        return true;
    }

    private static readonly HashSet<string> ReportedPseudos = new(StringComparer.Ordinal);

    /// <summary>
    /// A pseudo that names something this page has none of: a text selection, a shadow tree, a
    /// browser's own widget internals, or a line box that does not exist until layout has run.
    /// Keeping the rule and matching nothing is what a browser without that vendor's parts does,
    /// and it beats dropping the rule - dropping it loses the other selectors in the same list.
    /// Reported once so the gap is on the record either way.
    /// </summary>
    private static bool NeverMatches(string name, Action<string>? warn)
    {
        var part = name is "scroll-marker" or "scroll-marker-group" or "scroll-button" or "column";
        var line = name == "first-line";
        if (!part && !line && !name.StartsWith("-", StringComparison.Ordinal)
            && name is not ("selection" or "host" or "host-context" or "slotted" or "part" or "cue" or "cue-region"
                or "file-selector-button" or "spelling-error" or "grammar-error" or "highlight" or "target-text"
                or "view-transition" or "view-transition-group" or "view-transition-image-pair"
                or "view-transition-old" or "view-transition-new" or "picker" or "picker-icon" or "checkmark"))
            return false;
        // warn != null first: ParseSelector(x, null) - @supports selector(), querySelector - would
        // otherwise mark the name reported and eat the warning the page's own parse owes it.
        if (warn != null && ReportedPseudos.Add(name))
            warn.Invoke(line
                ? $"css: \"{name}\" matches nothing here: where a line breaks is only known after layout, and the text would have to be split before it runs"
                : part
                ? $"css: \"{name}\" matches nothing here: the page has no such box to paint"
                : $"css: \"{name}\" matches nothing here: no text selection, shadow tree or browser widget internals");
        return true;
    }

    /// <summary>
    /// Forget which pseudos and media features have already been reported. Without this they
    /// are process-global: the first page reports the gap and every page after it looks clean,
    /// which is exactly the bug StyleApplier.ForgetReported exists to avoid.
    /// </summary>
    public static void ForgetReported()
    {
        ReportedPseudos.Clear();
        ReportedMedia.Clear();
    }

    /// <summary>Index of the ` of ` keyword at the top level of an nth-child argument, or -1.</summary>
    private static int OfKeyword(string arg)
    {
        var depth = 0;
        for (var i = 0; i + 4 <= arg.Length; i++)
        {
            if (arg[i] == '(' || arg[i] == '[') depth++;
            else if (arg[i] == ')' || arg[i] == ']') depth--;
            else if (depth == 0 && string.Compare(arg, i, " of ", 0, 4, StringComparison.OrdinalIgnoreCase) == 0) return i;
        }
        return -1;
    }

    /// <summary>Position of <paramref name="n"/> among the siblings matching <paramref name="of"/>, and how many there are. Index -1 when it is not one of them.</summary>
    private static void IndexAmong(HtmlNode n, List<CssSelector> of, out int index, out int count)
    {
        index = -1;
        count = 0;
        if (n.Parent == null) { index = 0; count = 1; return; }
        foreach (var c in n.Parent.Children)
        {
            if (c.IsText) continue;
            var hit = false;
            foreach (var s in of)
                if (s.Matches(c)) { hit = true; break; }
            if (!hit) continue;
            if (c == n) index = count;
            count++;
        }
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

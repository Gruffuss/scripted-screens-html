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
    public readonly Dictionary<string, string> Attributes = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<HtmlNode> Children = new();
    public HtmlNode? Parent;

    public bool IsText => Tag == null;

    public string? Attr(string name) => Attributes.TryGetValue(name, out var v) ? v : null;
}

/// <summary>
/// Forgiving HTML tokenizer: tags, attributes (quoted or bare), void and self-closing tags,
/// comments, entities, raw text inside style/script. Unclosed tags close at their parent;
/// a stray close tag closes the nearest matching ancestor. No DOCTYPE handling beyond
/// skipping it. ponytail: this is a tag soup parser, not HTML5; add cases when a page breaks.
/// </summary>
internal static class HtmlParser
{
    private static readonly HashSet<string> Void = new(StringComparer.OrdinalIgnoreCase)
    {
        "br", "hr", "img", "input", "meta", "link", "col", "wbr", "source",
    };

    private static readonly HashSet<string> RawText = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "script",
    };

    /// <summary>Parse to a synthetic root whose children are the document's top level.</summary>
    public static HtmlNode Parse(string html, Action<string>? warn = null)
    {
        var root = new HtmlNode { Tag = "#root" };
        var current = root;
        var i = 0;
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length == 0)
                return;
            var collapsed = CollapseWhitespace(text.ToString());
            text.Clear();
            if (collapsed.Length == 0)
                return;
            current.Children.Add(new HtmlNode { Text = collapsed, Parent = current });
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
            var node = new HtmlNode { Parent = current };
            i++;
            var start = i;
            while (i < html.Length && (char.IsLetterOrDigit(html[i]) || html[i] == '-' || html[i] == ':'))
                i++;
            node.Tag = html.Substring(start, i - start).ToLowerInvariant();

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
                var attrName = html.Substring(nameStart, i - nameStart);
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
                        value = html.Substring(i + 1, end - i - 1);
                        i = Math.Min(html.Length, end + 1);
                    }
                    else
                    {
                        var vs = i;
                        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>')
                            i++;
                        value = html.Substring(vs, i - vs);
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
                node.Children.Add(new HtmlNode { Text = raw, Parent = node });
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
                _ => null,
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

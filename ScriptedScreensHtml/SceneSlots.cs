using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ScriptedScreensHtml;

/// <summary>
/// Splits an emitted scene into a template and its values, so the vector mod gets the structure
/// once and only changed values afterwards (REDESIGN.md, step 1). Every position, size, opacity,
/// stroke width, font size, text and colour in a node line becomes a data slot
/// <c>$L&lt;line&gt;_&lt;key&gt;</c>, and so does every number inside an expression. Two emits of
/// the same page with different values produce the same template; only the values differ.
/// </summary>
/// <remarks>
/// Pure text work, no Unity: tested headless. DEFS (gradients, clips) and the SCENE line stay
/// literal, since the vector mod reads them once. Arrays (radii, shadows, points) stay literal:
/// a change there is a structure change.
/// </remarks>
internal static class SceneSlots
{
    /// <summary>One slot's value: a number, or a string (text, or a colour the vector mod parses).</summary>
    internal readonly struct Value : IEquatable<Value>
    {
        public readonly bool IsNumber;
        public readonly float Number;
        public readonly string? Text;
        public Value(float n) { IsNumber = true; Number = n; Text = null; }
        public Value(string s) { IsNumber = false; Number = 0f; Text = s; }
        public bool Equals(Value o) => IsNumber == o.IsNumber && (IsNumber ? Number.Equals(o.Number) : string.Equals(Text, o.Text, StringComparison.Ordinal));
        public override bool Equals(object? obj) => obj is Value v && Equals(v);
        public override int GetHashCode() => IsNumber ? Number.GetHashCode() : (Text?.GetHashCode() ?? 0);
    }

    private static readonly HashSet<string> NumberKeys = new(StringComparer.Ordinal)
    {
        "x", "y", "w", "h", "cx", "cy", "r", "rx", "ry", "x1", "y1", "x2", "y2", "o", "fo", "fo2", "so", "sw", "size",
    };
    private static readonly HashSet<string> ColourKeys = new(StringComparer.Ordinal) { "f", "s" };

    /// <summary>The template and the values its slots name, in order.</summary>
    public static string Split(string scene, Dictionary<string, Value> values, string prefix = "L")
    {
        values.Clear();
        var sb = new StringBuilder(scene.Length + scene.Length / 4);
        var line = 0;
        var defsDepth = 0;
        var start = 0;
        while (start <= scene.Length)
        {
            var end = scene.IndexOf('\n', start);
            if (end < 0) end = scene.Length;
            var text = scene.Substring(start, end - start);
            var trimmed = text.TrimStart();
            if (defsDepth > 0)
            {
                // inside DEFS: literal; track braces to find its end
                foreach (var c in text) { if (c == '{') defsDepth++; else if (c == '}') defsDepth--; }
                sb.Append(text);
            }
            else if (trimmed.StartsWith("DEFS", StringComparison.Ordinal))
            {
                foreach (var c in text) if (c == '{') defsDepth++; else if (c == '}') defsDepth--;
                sb.Append(text);
            }
            else if (trimmed.Length == 0 || trimmed[0] == '}' || trimmed[0] == '#' || trimmed.StartsWith("SCENE", StringComparison.Ordinal))
            {
                sb.Append(text);
            }
            else
            {
                SlotLine(text, line, sb, values, prefix);
            }
            if (end < scene.Length) sb.Append('\n');
            line++;
            start = end + 1;
        }
        return sb.ToString();
    }

    private static void SlotLine(string text, int line, StringBuilder sb, Dictionary<string, Value> values, string prefix)
    {
        var i = 0;
        // indent and op
        while (i < text.Length && text[i] == ' ') sb.Append(text[i++]);
        while (i < text.Length && text[i] != ' ') sb.Append(text[i++]);
        while (i < text.Length)
        {
            if (text[i] == ' ') { sb.Append(' '); i++; continue; }
            // one token: key=value, a bare flag, or the opening brace
            var eq = -1;
            var j = i;
            while (j < text.Length && text[j] != ' ' && text[j] != '=') j++;
            if (j < text.Length && text[j] == '=') eq = j;
            if (eq < 0)
            {
                sb.Append(text, i, j - i);
                i = j;
                continue;
            }
            var key = text.Substring(i, eq - i);
            var vStart = eq + 1;
            var vEnd = ValueEnd(text, vStart);
            var raw = text.Substring(vStart, vEnd - vStart);
            sb.Append(key).Append('=');
            sb.Append(SlotValue(key, raw, line, values, prefix));
            i = vEnd;
        }
    }

    /// <summary>End of a value: a quoted string (with \" escapes), a bracketed array, or up to the next space.</summary>
    private static int ValueEnd(string text, int i)
    {
        if (i < text.Length && text[i] == '"')
        {
            i++;
            while (i < text.Length && text[i] != '"') { if (text[i] == '\\') i++; i++; }
            return Math.Min(text.Length, i + 1);
        }
        var depth = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '[') depth++;
            else if (c == ']') depth--;
            else if (c == ' ' && depth <= 0) break;
            i++;
        }
        return i;
    }

    private static string SlotValue(string key, string raw, int line, Dictionary<string, Value> values, string prefix)
    {
        if (raw.Length == 0) return raw;
        var quoted = raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"';
        var body = quoted ? raw.Substring(1, raw.Length - 2) : raw;
        var name = prefix + line.ToString(CultureInfo.InvariantCulture) + "_" + Safe(key);

        if (body.Length > 0 && body[0] == '=')
        {
            var expr = SlotNumbers(body, name, values);
            return quoted ? "\"" + expr + "\"" : expr;
        }
        if (key == "text" && quoted)
        {
            values[name] = new Value(Unescape(body));
            return "\"$" + name + "\"";
        }
        if (NumberKeys.Contains(key) && float.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            values[name] = new Value(n);
            return "$" + name;
        }
        if (ColourKeys.Contains(key) && body.Length > 1 && body[0] == '#')
        {
            values[name] = new Value(body);
            return "$" + name;
        }
        return raw;
    }

    /// <summary>Every number literal in an expression becomes a slot; identifiers (i1, hash2, $names) are left alone.</summary>
    private static string SlotNumbers(string expr, string name, Dictionary<string, Value> values)
    {
        var sb = new StringBuilder(expr.Length + 16);
        var k = 0;
        var i = 0;
        while (i < expr.Length)
        {
            var c = expr[i];
            var prev = i > 0 ? expr[i - 1] : ' ';
            var startsNumber = (char.IsDigit(c) || (c == '.' && i + 1 < expr.Length && char.IsDigit(expr[i + 1])))
                               && !(char.IsLetterOrDigit(prev) || prev == '_' || prev == '$' || prev == '.');
            // a unary minus belongs to the number: `t--0.02` and `t-1.5` are the same template, `t-$a`
            var signed = c == '-' && i + 1 < expr.Length && (char.IsDigit(expr[i + 1]) || expr[i + 1] == '.')
                         && (i == 0 || "+-*/(,=<>!&|?:".IndexOf(prev) >= 0);
            if (!startsNumber && !signed) { sb.Append(c); i++; continue; }
            var j = signed ? i + 1 : i;
            while (j < expr.Length && (char.IsDigit(expr[j]) || expr[j] == '.')) j++;
            if (j < expr.Length && (expr[j] == 'e' || expr[j] == 'E') && j + 1 < expr.Length && (char.IsDigit(expr[j + 1]) || ((expr[j + 1] == '-' || expr[j + 1] == '+') && j + 2 < expr.Length && char.IsDigit(expr[j + 2]))))
            {
                j += 2;
                while (j < expr.Length && char.IsDigit(expr[j])) j++;
            }
            if (float.TryParse(expr.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                var slot = name + "_" + (k++).ToString(CultureInfo.InvariantCulture);
                values[slot] = new Value(n);
                sb.Append('$').Append(slot);
            }
            else
            {
                sb.Append(expr, i, j - i);
            }
            i = j;
        }
        return sb.ToString();
    }

    private static string Safe(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var c in key) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }

    /// <summary>Scene text escapes back to the string: \\ \" and \n.</summary>
    internal static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                var n = s[++i];
                sb.Append(n == 'n' ? '\n' : n);
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}

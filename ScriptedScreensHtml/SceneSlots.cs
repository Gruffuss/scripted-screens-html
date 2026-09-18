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
///
/// A page emits every frame, so this runs at frame rate and allocates nothing once warm: it reads
/// the scene in place (no substrings), builds into one reused buffer, and keeps the slot names, the
/// template and the text and colour values it made last time, handing the same instances back while
/// they still match. Garbage here showed up as a stutter every few seconds.
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
    /// <summary>Two-number pairs the vector mod reads item by item, each item a number or an expression.</summary>
    private static readonly HashSet<string> PairKeys = new(StringComparer.Ordinal) { "t", "s", "a" };

    /// <summary>What one token produced last time: its key, its slot name, and the names of its parts.</summary>
    private sealed class Token
    {
        public string Key = string.Empty;
        public string Name = string.Empty;
        public List<string>? Parts;
    }

    /// <summary>Per page thread: the buffer, the template and the names and values of the last split.</summary>
    private sealed class Memory
    {
        public readonly StringBuilder Builder = new(4096);
        public string Prefix = string.Empty;
        public string? Template;
        public readonly List<List<Token>> Lines = new();
        public readonly Dictionary<string, Value> Last = new(StringComparer.Ordinal);

        public void Reset(string prefix)
        {
            Prefix = prefix;
            Template = null;
            Lines.Clear();
            Last.Clear();
        }

        public Token TokenAt(int line, int index)
        {
            while (Lines.Count <= line) Lines.Add(new List<Token>());
            var tokens = Lines[line];
            while (tokens.Count <= index) tokens.Add(new Token());
            return tokens[index];
        }
    }

    [ThreadStatic] private static Memory? _memory;

    /// <summary>The template and the values its slots name, in order. Convenience for tests and tools.</summary>
    public static string Split(string scene, Dictionary<string, Value> values, string prefix = "L")
        => Split(scene.ToCharArray(), scene.Length, values, prefix);

    /// <summary>The template and the values its slots name, in order.</summary>
    public static string Split(char[] scene, int sceneLength, Dictionary<string, Value> values, string prefix = "L")
    {
        values.Clear();
        var memory = _memory ??= new Memory();
        if (!string.Equals(memory.Prefix, prefix, StringComparison.Ordinal))
            memory.Reset(prefix);

        var sb = memory.Builder;
        sb.Clear();
        var line = 0;
        var defsDepth = 0;
        var start = 0;
        while (start <= sceneLength)
        {
            var end = Array.IndexOf(scene, '\n', start, sceneLength - start);
            if (end < 0) end = sceneLength;
            var text = start;
            while (text < end && scene[text] == ' ') text++;
            if (defsDepth > 0)
            {
                // inside DEFS: literal; track braces to find its end
                for (var i = start; i < end; i++) { if (scene[i] == '{') defsDepth++; else if (scene[i] == '}') defsDepth--; }
                sb.Append(scene, start, end - start);
            }
            else if (Starts(scene, sceneLength, text, end, "DEFS"))
            {
                for (var i = start; i < end; i++) { if (scene[i] == '{') defsDepth++; else if (scene[i] == '}') defsDepth--; }
                sb.Append(scene, start, end - start);
            }
            else if (text >= end || scene[text] == '}' || scene[text] == '#' || Starts(scene, sceneLength, text, end, "SCENE"))
            {
                sb.Append(scene, start, end - start);
            }
            else
            {
                SlotLine(scene, sceneLength, start, end, line, sb, values, memory);
            }
            if (end < sceneLength) sb.Append('\n');
            line++;
            start = end + 1;
        }

        // the same template as last time is handed back as the same string: the caller compares it
        // by reference and equality, and an unchanged page then allocates nothing here
        if (memory.Template != null && Same(sb, memory.Template))
            return memory.Template;
        memory.Template = sb.ToString();
        return memory.Template;
    }

    private static void SlotLine(char[] scene, int sceneLength, int from, int to, int line, StringBuilder sb, Dictionary<string, Value> values, Memory memory)
    {
        var i = from;
        // indent and op
        while (i < to && scene[i] == ' ') sb.Append(scene[i++]);
        while (i < to && scene[i] != ' ') sb.Append(scene[i++]);
        var index = 0;
        while (i < to)
        {
            if (scene[i] == ' ') { sb.Append(' '); i++; continue; }
            // one token: key=value, a bare flag, or the opening brace
            var j = i;
            while (j < to && scene[j] != ' ' && scene[j] != '=') j++;
            if (j >= to || scene[j] != '=')
            {
                sb.Append(scene, i, j - i);
                i = j;
                continue;
            }
            var keyStart = i;
            var keyEnd = j;
            var vStart = j + 1;
            var vEnd = ValueEnd(scene, sceneLength, vStart, to);
            sb.Append(scene, keyStart, keyEnd - keyStart).Append('=');
            SlotValue(scene, sceneLength, keyStart, keyEnd, vStart, vEnd, line, index++, sb, values, memory);
            i = vEnd;
        }
    }

    /// <summary>End of a value: a quoted string (with \" escapes), a bracketed array, or up to the next space.</summary>
    private static int ValueEnd(char[] text, int sceneLength, int i, int to)
    {
        if (i < to && text[i] == '"')
        {
            i++;
            while (i < to && text[i] != '"') { if (text[i] == '\\') i++; i++; }
            return Math.Min(to, i + 1);
        }
        var depth = 0;
        while (i < to)
        {
            var c = text[i];
            if (c == '[') depth++;
            else if (c == ']') depth--;
            else if (c == ' ' && depth <= 0) break;
            i++;
        }
        return i;
    }

    private static void SlotValue(char[] scene, int sceneLength, int keyStart, int keyEnd, int rawStart, int rawEnd,
                                  int line, int index, StringBuilder sb, Dictionary<string, Value> values, Memory memory)
    {
        if (rawEnd <= rawStart) return;
        var quoted = rawEnd - rawStart >= 2 && scene[rawStart] == '"' && scene[rawEnd - 1] == '"';
        var bodyStart = quoted ? rawStart + 1 : rawStart;
        var bodyEnd = quoted ? rawEnd - 1 : rawEnd;
        var token = memory.TokenAt(line, index);
        if (!Is(scene, sceneLength, keyStart, keyEnd, token.Key))
        {
            token.Key = new string(scene, keyStart, keyEnd - keyStart);
            token.Name = memory.Prefix + line.ToString(CultureInfo.InvariantCulture) + "_" + Safe(token.Key);
            token.Parts?.Clear();
        }
        var name = token.Name;

        if (bodyEnd > bodyStart && scene[bodyStart] == '=')
        {
            if (quoted) sb.Append('"');
            SlotNumbers(scene, sceneLength, bodyStart, bodyEnd, token, sb, values);
            if (quoted) sb.Append('"');
            return;
        }
        if (quoted && Is(scene, sceneLength, keyStart, keyEnd, "text"))
        {
            values[name] = Keep(name, Unescaped(scene, sceneLength, bodyStart, bodyEnd, memory.Last.TryGetValue(name, out var had) ? had.Text : null), memory);
            sb.Append('"').Append('$').Append(name).Append('"');
            return;
        }
        if (In(NumberKeys, scene, sceneLength, keyStart, keyEnd)
            && float.TryParse(new ReadOnlySpan<char>(scene, bodyStart, bodyEnd - bodyStart), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            values[name] = new Value(n);
            sb.Append('$').Append(name);
            return;
        }
        if (In(ColourKeys, scene, sceneLength, keyStart, keyEnd) && bodyEnd - bodyStart > 1 && scene[bodyStart] == '#')
        {
            var previous = memory.Last.TryGetValue(name, out var was) ? was.Text : null;
            var colour = previous != null && Is(scene, sceneLength, bodyStart, bodyEnd, previous) ? previous : new string(scene, bodyStart, bodyEnd - bodyStart);
            values[name] = Keep(name, colour, memory);
            sb.Append('$').Append(name);
            return;
        }
        if (In(PairKeys, scene, sceneLength, keyStart, keyEnd) && !quoted && rawEnd - rawStart > 2
            && scene[rawStart] == '[' && scene[rawEnd - 1] == ']' && Array.IndexOf(scene, '[', rawStart + 1, rawEnd - rawStart - 1) < 0)
        {
            // a group's translate, scale and anchor: an element moved by a script changes values, not the structure
            sb.Append('[');
            var part = 0;
            var i = rawStart + 1;
            var last = rawEnd - 1;
            while (i <= last)
            {
                var comma = i;
                while (comma < last && scene[comma] != ',') comma++;
                if (part > 0) sb.Append(',');
                if (float.TryParse(new ReadOnlySpan<char>(scene, i, comma - i), NumberStyles.Float, CultureInfo.InvariantCulture, out var item))
                {
                    var slot = Part(token, part, name);
                    values[slot] = new Value(item);
                    sb.Append('$').Append(slot);
                }
                else sb.Append(scene, i, comma - i);
                part++;
                i = comma + 1;
            }
            sb.Append(']');
            return;
        }
        sb.Append(scene, rawStart, rawEnd - rawStart);
    }

    /// <summary>Every number literal in an expression becomes a slot; identifiers (i1, hash2, $names) are left alone.</summary>
    private static void SlotNumbers(char[] expr, int sceneLength, int from, int to, Token token, StringBuilder sb, Dictionary<string, Value> values)
    {
        var k = 0;
        var i = from;
        while (i < to)
        {
            var c = expr[i];
            var prev = i > from ? expr[i - 1] : ' ';
            var startsNumber = (char.IsDigit(c) || (c == '.' && i + 1 < to && char.IsDigit(expr[i + 1])))
                               && !(char.IsLetterOrDigit(prev) || prev == '_' || prev == '$' || prev == '.');
            // a unary minus belongs to the number: `t--0.02` and `t-1.5` are the same template, `t-$a`
            var signed = c == '-' && i + 1 < to && (char.IsDigit(expr[i + 1]) || expr[i + 1] == '.')
                         && (i == from || "+-*/(,=<>!&|?:".IndexOf(prev) >= 0);
            if (!startsNumber && !signed) { sb.Append(c); i++; continue; }
            var j = signed ? i + 1 : i;
            while (j < to && (char.IsDigit(expr[j]) || expr[j] == '.')) j++;
            if (j < to && (expr[j] == 'e' || expr[j] == 'E') && j + 1 < to && (char.IsDigit(expr[j + 1]) || ((expr[j + 1] == '-' || expr[j + 1] == '+') && j + 2 < to && char.IsDigit(expr[j + 2]))))
            {
                j += 2;
                while (j < to && char.IsDigit(expr[j])) j++;
            }
            if (float.TryParse(expr.AsSpan(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                var slot = Part(token, k++, token.Name);
                values[slot] = new Value(n);
                sb.Append('$').Append(slot);
            }
            else
            {
                sb.Append(expr, i, j - i);
            }
            i = j;
        }
    }

    /// <summary>The name of a token's nth part, made once and kept.</summary>
    private static string Part(Token token, int index, string name)
    {
        token.Parts ??= new List<string>(4);
        while (token.Parts.Count <= index)
            token.Parts.Add(name + "_" + token.Parts.Count.ToString(CultureInfo.InvariantCulture));
        return token.Parts[index];
    }

    /// <summary>Remembers a text or colour value, so the next split can hand back the same string.</summary>
    private static Value Keep(string name, string text, Memory memory)
    {
        var value = new Value(text);
        memory.Last[name] = value;
        return value;
    }

    /// <summary>The unescaped text, or <paramref name="previous"/> itself when it already says the same.</summary>
    private static string Unescaped(char[] scene, int sceneLength, int from, int to, string? previous)
    {
        if (previous != null && UnescapedIs(scene, sceneLength, from, to, previous)) return previous;
        var sb = new StringBuilder(to - from);
        for (var i = from; i < to; i++)
        {
            if (scene[i] == '\\' && i + 1 < to)
            {
                var n = scene[++i];
                sb.Append(n == 'n' ? '\n' : n);
            }
            else sb.Append(scene[i]);
        }
        return sb.ToString();
    }

    private static bool UnescapedIs(char[] scene, int sceneLength, int from, int to, string other)
    {
        var k = 0;
        for (var i = from; i < to; i++)
        {
            char c;
            if (scene[i] == '\\' && i + 1 < to)
            {
                var n = scene[++i];
                c = n == 'n' ? '\n' : n;
            }
            else c = scene[i];
            if (k >= other.Length || other[k++] != c) return false;
        }
        return k == other.Length;
    }

    /// <summary>The range is exactly this word.</summary>
    private static bool Is(char[] scene, int sceneLength, int from, int to, string word)
    {
        if (to - from != word.Length) return false;
        for (var i = 0; i < word.Length; i++)
            if (scene[from + i] != word[i]) return false;
        return true;
    }

    /// <summary>The range begins with this word.</summary>
    private static bool Starts(char[] scene, int sceneLength, int from, int to, string word)
    {
        if (to - from < word.Length) return false;
        for (var i = 0; i < word.Length; i++)
            if (scene[from + i] != word[i]) return false;
        return true;
    }

    private static bool In(HashSet<string> set, char[] scene, int sceneLength, int from, int to)
    {
        foreach (var key in set)
            if (key.Length == to - from && Is(scene, sceneLength, from, to, key)) return true;
        return false;
    }

    private static bool Same(StringBuilder sb, string other)
    {
        if (sb.Length != other.Length) return false;
        for (var i = 0; i < other.Length; i++)
            if (sb[i] != other[i]) return false;
        return true;
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

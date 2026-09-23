using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ScriptedScreensHtml;

/// <summary>
/// Splits an emitted scene into a template and its values, so the vector mod gets the structure
/// once and only changed values afterwards (the compile-once design, ScriptedScreensHtml/CLAUDE.md). Every position, size, opacity,
/// stroke width, font size, text and colour in a node line becomes a data slot, and so does every
/// number inside an expression. Two emits of the same page with different values produce the same
/// template; only the values differ.
///
/// A slot is named after the element's own id where the line carries one - <c>&lt;span id="temp"&gt;</c>
/// is <c>$temp</c> and its box is <c>$temp_x</c>, <c>$temp_w</c> - so a Lua chip can write to the
/// compiled scene by the names in the markup, with this mod no longer running. A line with no id
/// keeps the positional <c>$L&lt;line&gt;_&lt;key&gt;</c>, which nothing outside can address.
/// </summary>
/// <remarks>
/// Pure text work, no Unity: tested headless. The SCENE line stays
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
    /// <summary>
    /// What the SECOND line carrying an id is named after: <c>e__2_f</c>. A box and its label share
    /// the element's id, and the second used to fall back to a positional name nothing could
    /// address - so `color` on anything with a background had no slot. <see cref="DomSlots"/>
    /// composes the same suffix from the other side.
    /// </summary>
    internal const string SecondSuffix = "__2";

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
        // a scroll box's content height, which a list that grows changes without changing a shape
        "ch",
    };
    private static readonly HashSet<string> ColourKeys = new(StringComparer.Ordinal) { "f", "s" };
    /// <summary>Two-number pairs the vector mod reads item by item, each item a number or an expression.</summary>
    private static readonly HashSet<string> PairKeys = new(StringComparer.Ordinal) { "t", "s", "a" };

    /// <summary>What one token produced last time: its key, its slot name, and the names of its parts.</summary>
    private sealed class Token
    {
        public string Key = string.Empty;
        /// <summary>The id the line carried when Name was worked out; "" when it carried none.</summary>
        public string Id = string.Empty;
        /// <summary>Preferred: the author's id, or the positional name when there is no usable id.</summary>
        public string Name = string.Empty;
        /// <summary>The positional name, used when Name and Second are both claimed.</summary>
        public string Fallback = string.Empty;
        /// <summary>The id-derived name for a second line carrying this id (<c>e__2_f</c>); null when there is no usable id.</summary>
        public string? Second;
        /// <summary>Which of the two the last split actually used: Parts are built from it.</summary>
        public string? Chosen;
        public List<string>? Parts;
    }

    /// <summary>Per page thread: the buffer, the template and the names and values of the last split.</summary>
    private sealed class Memory
    {
        public readonly StringBuilder Builder = new(4096);
        /// <summary>Unescaping a changed text slot; Builder is busy holding the template. Grows, never shrinks.</summary>
        public char[] Text = new char[256];
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

    /// <summary>
    /// Whether a gradient's stop COLOURS become slots (`stops=[[0,$g_stops_0],[1,$g_stops_1]]`; the
    /// vector mod reads a `$name` stop colour from the payload since 0.11.31). Off for the per-frame
    /// split, which keeps arrays literal. On while the markup compiler emits: a colour the page picks
    /// per state inside a gradient is then a value, where literal it would be a different shape per
    /// value and its whole element written once per colour.
    /// </summary>
    [ThreadStatic] internal static bool SlotStops;

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
                // Inside DEFS. These used to be copied literally, because the vector mod read a
                // gradient or a clip ONCE at parse time and a slot there would have been silently
                // dropped. Since vector 0.11.31 it re-reads them before each tree walk, so they
                // slot like any other line — which is what stops a data-driven gradient or a
                // clipped bar from forcing a whole new structure.
                //
                // Arrays still stay literal (`stops=[[0,#fff],[1,#000]]`, `p=[...]`) and so do
                // quoted strings (`d="M ..."`), both handled by SlotLine already. A brace counted
                // here still ends the block, and a line that is only a brace has nothing to slot.
                for (var i = start; i < end; i++) { if (scene[i] == '{') defsDepth++; else if (scene[i] == '}') defsDepth--; }
                if (text >= end || scene[text] == '}')
                    sb.Append(scene, start, end - start);
                else
                    SlotLine(scene, sceneLength, start, end, line, sb, values, memory);
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
        var (idStart, idEnd) = LineId(scene, from, to);
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
            SlotValue(scene, sceneLength, keyStart, keyEnd, vStart, vEnd, idStart, idEnd, line, index++, sb, values, memory);
            i = vEnd;
        }
    }

    /// <summary>
    /// The <c>id=</c> this line carries, or an empty range. Quoted values are stepped over, so
    /// <c>text="run id=3"</c> is not mistaken for one.
    /// </summary>
    private static (int start, int end) LineId(char[] scene, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            if (scene[i] == '"')
            {
                i++;
                while (i < to && scene[i] != '"') { if (scene[i] == '\\') i++; i++; }
                continue;
            }
            if (scene[i] != ' ' || i + 3 >= to || scene[i + 1] != 'i' || scene[i + 2] != 'd' || scene[i + 3] != '=') continue;
            var s = i + 4;
            var e = s;
            while (e < to && scene[e] != ' ') e++;
            return (s, e);
        }
        return (0, 0);
    }

    /// <summary>
    /// An element id as a slot name, or null when it cannot be one: the mod's own synthetic names
    /// (<c>__x3</c>), and anything shaped like a positional name (<c>L12</c>), which would then be
    /// claimed by two unrelated things.
    /// </summary>
    private static string? Friendly(string id)
    {
        if (id.Length == 0 || id.Length > 48) return null;
        if (id.Length >= 2 && id[0] == '_' && id[1] == '_') return null;
        if ((id[0] == 'L' || id[0] == 'M') && id.Length > 1 && id[1] >= '0' && id[1] <= '9') return null;
        // `e__2` is what a second line carrying `e` is named, so an author's id of that shape would
        // claim the label's slots. `e__b` (the border companion) stays usable: no digit follows.
        for (var at = id.IndexOf("__", StringComparison.Ordinal); at >= 0; at = id.IndexOf("__", at + 1, StringComparison.Ordinal))
            if (at + 2 < id.Length && id[at + 2] >= '0' && id[at + 2] <= '9') return null;
        // ASCII only, so the name is an identifier the vector mod's expression parser accepts:
        // anything else (a dash, a space, an accent) becomes an underscore.
        var sb = new StringBuilder(id.Length + 1);
        if (id[0] >= '0' && id[0] <= '9') sb.Append('_');
        foreach (var c in id)
            sb.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' ? c : '_');
        return sb.ToString();
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
                                  int idStart, int idEnd, int line, int index, StringBuilder sb, Dictionary<string, Value> values, Memory memory)
    {
        if (rawEnd <= rawStart) return;
        var quoted = rawEnd - rawStart >= 2 && scene[rawStart] == '"' && scene[rawEnd - 1] == '"';
        var bodyStart = quoted ? rawStart + 1 : rawStart;
        var bodyEnd = quoted ? rawEnd - 1 : rawEnd;
        var token = memory.TokenAt(line, index);
        if (!Is(scene, sceneLength, keyStart, keyEnd, token.Key) || !Is(scene, sceneLength, idStart, idEnd, token.Id))
        {
            token.Key = new string(scene, keyStart, keyEnd - keyStart);
            token.Id = idEnd > idStart ? new string(scene, idStart, idEnd - idStart) : string.Empty;
            token.Fallback = memory.Prefix + line.ToString(CultureInfo.InvariantCulture) + "_" + Safe(token.Key);
            // The author's own id names the slot, so a Lua chip writes to the scene by the name it
            // wrote in the markup: <span id="temp"> is $temp, and its box is $temp_x, $temp_w and so
            // on. A line with no id keeps the positional name, which nothing outside can address.
            var friendly = Friendly(token.Id);
            var isText = string.Equals(token.Key, "text", StringComparison.Ordinal);
            token.Name = friendly == null ? token.Fallback
                : isText ? friendly
                : friendly + "_" + Safe(token.Key);
            token.Second = friendly == null ? null
                : isText ? friendly + SecondSuffix
                : friendly + SecondSuffix + "_" + Safe(token.Key);
            token.Parts?.Clear();
            token.Chosen = null;
        }
        // Two lines carrying the same id would name the same slot twice and one value would be lost.
        // First come wins, and emission order is the document's, so the same line wins every time;
        // the second takes `<id>__2_<key>` - a box's label, whose colour a script can then reach -
        // and only a third falls back to the positional name.
        var name = token.Name;
        if (!ReferenceEquals(name, token.Fallback) && values.ContainsKey(name))
            name = token.Second != null && !values.ContainsKey(token.Second) ? token.Second : token.Fallback;
        if (!ReferenceEquals(name, token.Chosen)) { token.Chosen = name; token.Parts?.Clear(); }

        if (bodyEnd > bodyStart && scene[bodyStart] == '=')
        {
            if (quoted) sb.Append('"');
            SlotNumbers(scene, sceneLength, bodyStart, bodyEnd, token, name, sb, values);
            if (quoted) sb.Append('"');
            return;
        }
        if (quoted && Is(scene, sceneLength, keyStart, keyEnd, "text"))
        {
            values[name] = Keep(name, Unescaped(scene, sceneLength, bodyStart, bodyEnd, memory.Last.TryGetValue(name, out var had) ? had.Text : null, memory), memory);
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
            // Same first-come rule as a scalar, and it has to be spelled out here because a pair
            // never inserts its bare name - only `X_t_0` and `X_t_1` through Part() - so the check
            // above cannot see the clash. Two lines carrying one id and both writing a pair would
            // otherwise SHARE those slots silently, and the last value written would win.
            // Part() rather than `name + "_0"`, which built a fresh string on every split for every
            // named pair - a per-frame allocation in a file whose contract is none once warm.
            if (!ReferenceEquals(name, token.Fallback) && values.ContainsKey(Part(token, 0, name)))
            {
                name = token.Second ?? token.Fallback;
                token.Chosen = name;
                token.Parts?.Clear();
                if (!ReferenceEquals(name, token.Fallback) && values.ContainsKey(Part(token, 0, name)))
                {
                    name = token.Fallback;
                    token.Chosen = name;
                    token.Parts?.Clear();
                }
            }

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
        if (SlotStops && Is(scene, sceneLength, keyStart, keyEnd, "stops") && rawEnd - rawStart > 2 && scene[rawStart] == '[')
        {
            // Each `#colour` inside the stop list, and nothing else: positions stay as they are.
            var k = 0;
            for (var i = rawStart; i < rawEnd; i++)
            {
                if (scene[i] != '#') { sb.Append(scene[i]); continue; }
                var e = i + 1;
                while (e < rawEnd && Uri.IsHexDigit(scene[e])) e++;
                var slot = Part(token, k++, name);
                values[slot] = Keep(slot, new string(scene, i, e - i), memory);
                sb.Append('$').Append(slot);
                i = e - 1;
            }
            return;
        }
        sb.Append(scene, rawStart, rawEnd - rawStart);
    }

    /// <summary>Every number literal in an expression becomes a slot; identifiers (i1, hash2, $names) are left alone.</summary>
    private static void SlotNumbers(char[] expr, int sceneLength, int from, int to, Token token, string name, StringBuilder sb, Dictionary<string, Value> values)
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
                var slot = Part(token, k++, name);
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
    private static string Unescaped(char[] scene, int sceneLength, int from, int to, string? previous, Memory memory)
    {
        if (previous != null && UnescapedIs(scene, sceneLength, from, to, previous)) return previous;
        // A buffer the page thread keeps, so only the result string is new. A StringBuilder was
        // tried first and is worse here: past its first chunk Clear() copies into a fresh array,
        // which showed up as one frame in eight costing more than the allocating version did.
        if (memory.Text.Length < to - from)
            memory.Text = new char[Math.Max(to - from, memory.Text.Length * 2)];
        var buffer = memory.Text;
        var length = 0;
        for (var i = from; i < to; i++)
        {
            if (scene[i] == '\\' && i + 1 < to)
            {
                var n = scene[++i];
                buffer[length++] = n == 'n' ? '\n' : n;
            }
            else buffer[length++] = scene[i];
        }
        return new string(buffer, 0, length);
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

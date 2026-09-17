using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScriptedScreensHtml;

/// <summary>
/// One face's metrics, copied from a TextMeshPro asset on the game thread and read-only afterwards,
/// so text can be measured on any thread. Values are in the asset's own units (TMP's faceInfo).
/// </summary>
internal sealed class FaceData
{
    internal struct Glyph
    {
        public uint Index;
        public float Advance;
        /// <summary>glyph.scale times character.scale: TMP's per-element scale.</summary>
        public float Scale;
    }

    public string Name = string.Empty;
    public float PointSize = 90f, Scale = 1f;
    public float Ascent, Descent, LineHeight, CapLine;
    public float NormalSpacingOffset, BoldSpacing, TabWidth;
    public int TabSize = 10;
    public float SubSize = 0.5f, SupSize = 0.5f;
    public readonly Dictionary<uint, Glyph> Chars = new();
    /// <summary>Pair adjustments by (second glyph &lt;&lt; 16 | first glyph): the first glyph's and the second glyph's x advance, and whether spacing is ignored.</summary>
    public readonly Dictionary<uint, (float first, float second, bool ignoreSpacing)> Pairs = new();
    /// <summary>The face's own fallback chain, then the global fallbacks.</summary>
    public FaceData[] Fallbacks = Array.Empty<FaceData>();
}

/// <summary>
/// Text measurement the way the vector mod's TextMeshPro labels lay text out, for our layout
/// engine: a port of the width and line-break arithmetic in TMP_Text.CalculatePreferredValues
/// (per-character scale, kerning pairs, character, bold and word spacing, the rich-text tags
/// the emitter writes, break opportunities at white space, zero-width spaces and hyphens).
/// Pure data work: safe on any thread once the faces are copied, and it allocates nothing.
/// </summary>
/// <remarks>
/// Heights are line counts: the layout sets the line height (CSS line-height, or the face's own
/// line height for normal). Sprites, CJK line-breaking rules, soft-hyphen substitution and
/// auto-sizing are not measured; the emitter writes none of them.
/// </remarks>
internal static class TextMeasure
{
    internal struct Result
    {
        /// <summary>Widest line, from its first character to the end of its last visible glyph.</summary>
        public float Width;
        public int Lines;
        /// <summary>The face's natural line height at the base size (TMP's line advance with no spacing).</summary>
        public float LineHeight;
    }

    internal struct Style
    {
        public float Size { get; set; }
        public bool Bold { get; set; }
        /// <summary>Extra advance per character, in px (CSS letter-spacing).</summary>
        public float LetterSpacing { get; set; }
        /// <summary>Extra advance per white space, in px (CSS word-spacing).</summary>
        public float WordSpacing { get; set; }
        public bool Wrap { get; set; }
        /// <summary>Rich-text tags are read (the labels the emitter writes are rich text).</summary>
        public bool Rich { get; set; }
        /// <summary>CSS text-transform outside tags, as the emitter applies it: 1 uppercase, 2 lowercase, 3 capitalize.</summary>
        public int Transform { get; set; }
    }

    /// <summary>Faces by name, for rich-text font tags. Filled on the game thread (<see cref="Register"/>).</summary>
    private static readonly Dictionary<string, FaceData> Faces = new(StringComparer.OrdinalIgnoreCase);

    internal static void Register(FaceData face)
    {
        lock (Faces) Faces[face.Name] = face;
    }

    internal static FaceData? Find(string name)
    {
        lock (Faces) return Faces.TryGetValue(name, out var face) ? face : null;
    }

    // ---- measuring ----

    private struct Unit
    {
        public uint Code;
        public FaceData Face;
        public FaceData.Glyph Glyph;
        public float FontSize, FontScaleMultiplier, CSpace, MSpace;
        public bool Bold, NoBreak;
    }

    [ThreadStatic] private static List<Unit>? _units;

    public static Result Measure(string text, FaceData face, in Style style, float maxWidth)
    {
        var units = _units ??= new List<Unit>(128);
        units.Clear();
        Parse(text, face, style, units);
        var result = new Result { LineHeight = face.LineHeight * style.Size / face.PointSize * face.Scale };
        if (units.Count == 0) return result;

        var limit = maxWidth + 0.0001f;
        var x = 0f;              // TMP's m_xAdvance
        var lineInk = 0f;        // right edge of the last visible glyph on this line
        var firstOfLine = 0;
        var breakIndex = -1;     // the unit after which this line may break
        var breakInk = 0f;       // the line's ink at that point
        var lines = 1;
        var widest = 0f;
        for (var i = 0; i < units.Count; i++)
        {
            var u = units[i];
            var code = u.Code;
            var elementScale = u.FontSize / u.Face.PointSize * u.Face.Scale * u.FontScaleMultiplier * u.Glyph.Scale;
            var emScale = u.FontSize * 0.01f;
            var white = code <= 0xFFFF && char.IsWhiteSpace((char)code);

            if (code == 10 || code == 11 || code == 8232 || code == 8233)
            {
                widest = Math.Max(widest, lineInk);
                lines++;
                x = 0f; lineInk = 0f; firstOfLine = i + 1; breakIndex = -1;
                continue;
            }

            // kerning: this glyph with the next, and the previous with this
            var spacing = style.LetterSpacing / Math.Max(0.0001f, emScale); // TMP's character spacing is in em/100
            var kern = 0f;
            if (i + 1 < units.Count && units[i + 1].Face == u.Face && u.Face.Pairs.TryGetValue((units[i + 1].Glyph.Index << 16) | u.Glyph.Index, out var p1))
            {
                kern += p1.first;
                if (p1.ignoreSpacing) spacing = 0f;
            }
            if (i > 0 && units[i - 1].Face == u.Face && u.Face.Pairs.TryGetValue((u.Glyph.Index << 16) | units[i - 1].Glyph.Index, out var p2))
            {
                kern += p2.second;
                if (p2.ignoreSpacing) spacing = 0f;
            }

            // monospace: TMP centres the glyph in its cell (on width and bearing; half the advance is close for digits)
            var mono = u.MSpace != 0f ? u.MSpace / 2f - u.Glyph.Advance * elementScale / 2f : 0f;

            // the break test: a visible glyph that would end past the box wraps the word it is in
            if (code == 9 || (!white && code != 8203 && code != 173))
            {
                var ink = Math.Abs(x + mono) + u.Glyph.Advance * elementScale;
                if (ink > limit && style.Wrap && i != firstOfLine)
                {
                    widest = Math.Max(widest, breakIndex >= firstOfLine ? breakInk : lineInk);
                    lines++;
                    x = 0f; lineInk = 0f;
                    if (breakIndex >= firstOfLine)
                    {
                        i = breakIndex; // the next pass starts at the first unit after the break
                        firstOfLine = i + 1;
                        breakIndex = -1;
                        continue;
                    }
                    // no break opportunity on this line: TMP breaks before this character
                    firstOfLine = i;
                    breakIndex = -1;
                    ink = Math.Abs(mono) + u.Glyph.Advance * elementScale;
                }
                lineInk = ink;
            }

            if (code == 9)
            {
                var tab = u.Face.TabWidth * u.Face.TabSize * elementScale;
                if (tab > 0f)
                {
                    var next = (float)Math.Ceiling(x / tab) * tab;
                    x = next > x ? next : x + tab;
                }
            }
            else if (u.MSpace != 0f)
            {
                x += u.MSpace + (u.Face.NormalSpacingOffset + spacing) * emScale + u.CSpace;
            }
            else
            {
                var bold = u.Bold ? u.Face.BoldSpacing : 0f;
                x += (u.Glyph.Advance + kern) * elementScale + (u.Face.NormalSpacingOffset + spacing + bold) * emScale + u.CSpace;
            }
            if (white || code == 8203)
                x += style.WordSpacing;

            // break opportunities: after white space (not a no-break space), a zero-width space or a hyphen
            if (style.Wrap && !u.NoBreak && (white || code == 8203 || code == 45 || code == 173)
                && code != 160 && code != 8199 && code != 8209 && code != 8239 && code != 8288)
            {
                breakIndex = i;
                breakInk = lineInk; // white space adds no ink; a hyphen's is already counted
            }
        }
        widest = Math.Max(widest, lineInk);
        result.Width = (float)Math.Floor(widest * 100f + 1f) / 100f; // TMP rounds its preferred width up this way
        result.Lines = lines;
        return result;
    }

    /// <summary>
    /// A subscript or superscript digit (₂, ⁴, ²) as its plain digit. A face without the script
    /// glyph draws the plain digit with sub/sup instead of another typeface's heavier glyph.
    /// </summary>
    internal static bool ScriptDigit(uint c, out char digit, out bool sub)
    {
        sub = c >= 0x2080 && c <= 0x2089;
        digit = '\0';
        if (sub) digit = (char)('0' + (c - 0x2080));
        else if (c == 0x2070) digit = '0';
        else if (c == 0x00B9) digit = '1';
        else if (c == 0x00B2) digit = '2';
        else if (c == 0x00B3) digit = '3';
        else if (c >= 0x2074 && c <= 0x2079) digit = (char)('0' + (c - 0x2070));
        return digit != '\0';
    }

    private static bool Resolve(FaceData face, uint unicode, out FaceData owner, out FaceData.Glyph glyph, int depth = 0)
    {
        if (face.Chars.TryGetValue(unicode, out glyph)) { owner = face; return true; }
        if (depth < 4)
            foreach (var fb in face.Fallbacks)
                if (Resolve(fb, unicode, out owner, out glyph, depth + 1)) return true;
        owner = face;
        return false;
    }

    // ---- rich text: the tags TMP knows that change measurement; the others are skipped ----

    private struct Tags
    {
        public FaceData Face;
        public float Size, ScaleMul, CSpace, MSpace;
        public int Bold, NoBreak;
        public bool NoParse;
    }

    [ThreadStatic] private static List<FaceData>? _faceStack;
    [ThreadStatic] private static List<float>? _sizeStack, _scaleStack;

    private static void Parse(string text, FaceData baseFace, in Style style, List<Unit> units)
    {
        var faces = _faceStack ??= new List<FaceData>(4);
        var sizes = _sizeStack ??= new List<float>(4);
        var scales = _scaleStack ??= new List<float>(4);
        faces.Clear(); sizes.Clear(); scales.Clear();
        var t = new Tags { Face = baseFace, Size = style.Size, ScaleMul = 1f, Bold = style.Bold ? 1 : 0 };
        var wordStart = true;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (style.Rich && c == '<')
            {
                var close = text.IndexOf('>', i + 1);
                if (close > i)
                {
                    var tag = text.AsSpan(i + 1, close - i - 1);
                    bool known;
                    if (t.NoParse)
                    {
                        known = Is(tag, "/noparse");
                        if (known) t.NoParse = false;
                    }
                    else
                    {
                        known = Tag(tag, baseFace, style.Size, ref t, faces, sizes, scales, units);
                    }
                    if (known)
                    {
                        i = close + 1;
                        continue;
                    }
                }
            }
            if (style.Transform != 0)
            {
                var original = c;
                c = style.Transform switch
                {
                    1 => char.ToUpperInvariant(c),
                    2 => char.ToLowerInvariant(c),
                    _ => wordStart ? char.ToUpperInvariant(c) : c,
                };
                wordStart = char.IsWhiteSpace(original);
            }
            uint code = c;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                code = (uint)char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }
            i++;
            if (code == 13) continue;
            FaceData owner;
            FaceData.Glyph glyph;
            var scale = t.ScaleMul;
            if (!t.Face.Chars.ContainsKey(code) && ScriptDigit(code, out var digit, out var sub) && t.Face.Chars.TryGetValue(digit, out glyph))
            {
                // the emitter draws it as the face's own digit under sub/sup
                owner = t.Face;
                var s = sub ? t.Face.SubSize : t.Face.SupSize;
                scale *= s > 0f ? s : 1f;
                code = digit;
            }
            else if (code is 10 or 11 or 8232 or 8233 or 8203 or 8288)
            {
                // line breaks and zero-width characters: no glyph, no advance
                owner = t.Face;
                glyph = default;
            }
            else if (!Resolve(t.Face, code, out owner, out glyph)
                && !(code == 160 && Resolve(t.Face, 32, out owner, out glyph))       // TMP draws a missing no-break space as a space
                && !(code == 8209 && Resolve(t.Face, 45, out owner, out glyph))      // and a missing non-breaking hyphen as a hyphen
                && !Resolve(t.Face, 9633, out owner, out glyph))                     // anything else missing: the missing-glyph square
            {
                continue;
            }
            units.Add(new Unit
            {
                Code = code, Face = owner, Glyph = glyph, FontSize = t.Size, FontScaleMultiplier = scale,
                CSpace = t.CSpace, MSpace = t.MSpace, Bold = t.Bold > 0, NoBreak = t.NoBreak > 0,
            });
        }
    }

    private static bool Is(ReadOnlySpan<char> name, string wanted) => name.Equals(wanted.AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static bool Tag(ReadOnlySpan<char> tag, FaceData baseFace, float baseSize, ref Tags t,
        List<FaceData> faces, List<float> sizes, List<float> scales, List<Unit> units)
    {
        var eq = tag.IndexOf('=');
        var name = (eq < 0 ? tag : tag.Slice(0, eq)).Trim();
        var value = eq < 0 ? ReadOnlySpan<char>.Empty : tag.Slice(eq + 1).Trim();
        value = value.Trim('"').Trim('\\').Trim('"').Trim('\'');

        if (Is(name, "b")) { t.Bold++; return true; }
        if (Is(name, "/b")) { if (t.Bold > 0) t.Bold--; return true; }
        if (Is(name, "noparse")) { t.NoParse = true; return true; }
        if (Is(name, "nobr")) { t.NoBreak++; return true; }
        if (Is(name, "/nobr")) { if (t.NoBreak > 0) t.NoBreak--; return true; }
        if (Is(name, "br"))
        {
            units.Add(new Unit { Code = 10, Face = t.Face, FontSize = t.Size, FontScaleMultiplier = t.ScaleMul });
            return true;
        }
        if (Is(name, "font"))
        {
            var next = Find(value.ToString()); // the one allocation here, and only for a font tag
            if (next != null)
            {
                faces.Add(t.Face);
                t.Face = next;
            }
            return true; // TMP keeps the current face for an unknown name
        }
        if (Is(name, "/font"))
        {
            if (faces.Count > 0) { t.Face = faces[faces.Count - 1]; faces.RemoveAt(faces.Count - 1); }
            else t.Face = baseFace;
            return true;
        }
        if (Is(name, "size"))
        {
            sizes.Add(t.Size);
            if (value.EndsWith("%".AsSpan())) t.Size = baseSize * Num(value.Slice(0, value.Length - 1)) / 100f;
            else if (value.EndsWith("em".AsSpan(), StringComparison.OrdinalIgnoreCase)) t.Size = baseSize * Num(value.Slice(0, value.Length - 2));
            else if (value.Length > 0 && (value[0] == '+' || value[0] == '-')) t.Size = baseSize + Px(value);
            else t.Size = Px(value);
            return true;
        }
        if (Is(name, "/size"))
        {
            if (sizes.Count > 0) { t.Size = sizes[sizes.Count - 1]; sizes.RemoveAt(sizes.Count - 1); }
            else t.Size = baseSize;
            return true;
        }
        if (Is(name, "sub") || Is(name, "sup"))
        {
            scales.Add(t.ScaleMul);
            var s = Is(name, "sub") ? t.Face.SubSize : t.Face.SupSize;
            t.ScaleMul *= s > 0f ? s : 1f;
            return true;
        }
        if (Is(name, "/sub") || Is(name, "/sup"))
        {
            if (scales.Count > 0) { t.ScaleMul = scales[scales.Count - 1]; scales.RemoveAt(scales.Count - 1); }
            return true;
        }
        if (Is(name, "cspace")) { t.CSpace = EmOrPx(value, t.Size); return true; }
        if (Is(name, "/cspace"))
        {
            if (units.Count > 0) { var last = units[units.Count - 1]; last.CSpace = 0f; units[units.Count - 1] = last; }
            t.CSpace = 0f;
            return true;
        }
        if (Is(name, "mspace")) { t.MSpace = EmOrPx(value, t.Size); return true; }
        if (Is(name, "/mspace")) { t.MSpace = 0f; return true; }
        // tags that change only colour, decoration, case or position: no effect on width
        foreach (var skip in Ignored)
            if (Is(name, skip)) return true;
        return false; // not a tag TMP knows: drawn as text
    }

    private static readonly string[] Ignored =
    {
        "i", "/i", "u", "/u", "s", "/s", "mark", "/mark", "color", "/color", "alpha", "/alpha", "voffset", "/voffset",
        "align", "/align", "link", "/link", "lowercase", "/lowercase", "uppercase", "/uppercase", "allcaps", "/allcaps",
        "smallcaps", "/smallcaps", "style", "/style", "gradient", "/gradient", "material", "/material", "rotate", "/rotate",
    };

    private static float EmOrPx(ReadOnlySpan<char> v, float size) =>
        v.EndsWith("em".AsSpan(), StringComparison.OrdinalIgnoreCase) ? Num(v.Slice(0, v.Length - 2)) * size : Px(v);

    private static float Px(ReadOnlySpan<char> v) =>
        Num(v.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase) ? v.Slice(0, v.Length - 2) : v);

    private static float Num(ReadOnlySpan<char> v) =>
        float.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
}

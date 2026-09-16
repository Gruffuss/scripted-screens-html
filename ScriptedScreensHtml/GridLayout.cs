using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// <c>display: grid</c> on top of a layout engine that only has flexbox.
/// </summary>
/// <remarks>
/// The container stays a flex box; its children are positioned absolutely from the track
/// sizes, recomputed whenever the container or a child changes size. Covered: column and
/// row templates in px, %, fr, auto and repeat(); minmax() by its max; gap; auto placement
/// row-first; explicit grid-column / grid-row with "a / b", "span n" and "a / span n".
/// Auto rows take the tallest child in them, read back from the layout, so placement
/// settles over two passes. Not covered: named lines and areas, dense packing,
/// justify/align-items inside a cell (a child fills its cell).
/// </remarks>
internal sealed class GridLayout
{
    private struct Track
    {
        public float Px;   // fixed size
        public float Pct;  // percent of the container
        public float Fr;   // share of the remainder
        public bool Auto;  // sized by content (rows) or as 1fr (columns)
    }

    private struct Item
    {
        public VisualElement Ve;
        public int Col, ColSpan, Row, RowSpan;
        public int ColEnd, RowEnd; // explicit end lines (1-based, negative from the end), 0 when unset
    }

    private readonly VisualElement _ve;
    private readonly HtmlRenderer.Result _built;
    private readonly Dictionary<VisualElement, Rect> _placed = new();
    private float _minHeight = -1f;
    private float _minWidth = -1f;
    private bool _placing;

    private GridLayout(VisualElement ve, HtmlRenderer.Result built)
    {
        _ve = ve;
        _built = built;
    }

    public static void Attach(VisualElement ve, HtmlRenderer.Result built)
    {
        var grid = new GridLayout(ve, built);
        ve.RegisterCallback<GeometryChangedEvent>(_ => grid.Place());
        foreach (var child in ve.Children())
            child.RegisterCallback<GeometryChangedEvent>(_ => grid.Place());
        // a re-cascade writes the items' own position/size again: place them afresh
        built.AfterRecascade.Add(() => { grid._placed.Clear(); grid.Place(); });
    }

    private void Place()
    {
        if (_placing) return;
        _placing = true;
        try { PlaceInner(); }
        catch (Exception ex) { ScriptedScreensHtmlPlugin.Log?.LogWarning("html: grid: " + ex.Message); }
        finally { _placing = false; }
    }

    private void PlaceInner()
    {
        var css = _built.CssOf(_ve);
        var rs = _ve.resolvedStyle;
        var padL = rs.paddingLeft; var padT = rs.paddingTop;
        var w = _ve.layout.width - padL - rs.paddingRight;
        var h = _ve.layout.height - padT - rs.paddingBottom;
        // A grid in a flex row (a table beside a list) has no width of its own: its children
        // are absolute and contribute nothing. Then auto columns take their content width and
        // the container gets that as its minimum, the way auto rows already work.
        var definiteWidth = HasDefiniteWidth(css) || !ParentIsRow();
        if (!definiteWidth)
            w = float.IsNaN(w) ? 0f : Mathf.Max(0f, w);
        else if (float.IsNaN(w) || w <= 0f)
            return;
        var definiteHeight = HasDefiniteHeight(css) && !float.IsNaN(h) && h > 0f;

        Gaps(css, out var rowGap, out var colGap);
        // grid-template: [areas + row sizes] / columns; grid-template-areas names cells by row strings
        var colsText = Get(css, "grid-template-columns");
        var rowsText = Get(css, "grid-template-rows");
        var areasText = Get(css, "grid-template-areas");
        // grid: the grid-template forms, or "auto-flow [dense] [size] / cols" (row flow) and "rows / auto-flow [dense] [size]"
        string? autoRowsFromGrid = null;
        var template = Get(css, "grid-template");
        if (template == null && Get(css, "grid") is { } gridShort && gridShort != "none")
        {
            var slash = gridShort.IndexOf('/');
            var left = slash >= 0 ? gridShort.Substring(0, slash).Trim() : gridShort;
            var right = slash >= 0 ? gridShort.Substring(slash + 1).Trim() : string.Empty;
            if (left.StartsWith("auto-flow", StringComparison.Ordinal))
            {
                var size = left.Substring(9).Replace("dense", string.Empty).Trim();
                if (size.Length > 0) autoRowsFromGrid = size;
                colsText ??= right;
            }
            else if (right.StartsWith("auto-flow", StringComparison.Ordinal))
            {
                // ponytail: column flow is laid out as row flow; the rows keep their sizes
                rowsText ??= left;
                var size = right.Substring(9).Replace("dense", string.Empty).Trim();
                if (size.Length > 0) colsText ??= size;
            }
            else template = gridShort;
        }
        if (template != null && template.Trim() != "none")
        {
            var slash = template.LastIndexOf('/');
            var rowsPart = slash >= 0 ? template.Substring(0, slash) : template;
            if (slash >= 0) colsText ??= template.Substring(slash + 1);
            var strings = System.Text.RegularExpressions.Regex.Matches(rowsPart, "\"[^\"]*\"|'[^']*'");
            if (strings.Count > 0)
            {
                areasText ??= rowsPart;
                rowsText ??= System.Text.RegularExpressions.Regex.Replace(rowsPart, "\"[^\"]*\"|'[^']*'|\\[[^\\]]*\\]", " ");
            }
            else rowsText ??= rowsPart;
        }
        var areas = ParseAreas(areasText);
        var cols = ParseTracks(colsText ?? "auto");
        if (cols.Count == 0) cols.Add(new Track { Auto = true });
        var rowsSpec = ParseTracks(rowsText ?? string.Empty);
        var autoRow = ParseTracks(Get(css, "grid-auto-rows") ?? autoRowsFromGrid ?? "auto");
        var autoRowTrack = autoRow.Count > 0 ? autoRow[0] : new Track { Auto = true };

        // Items and placement.
        var items = new List<Item>();
        foreach (var child in _ve.Children())
        {
            if (child.resolvedStyle.display == DisplayStyle.None) continue;
            var ccss = _built.CssOf(child);
            var (c0, cs, ce) = Line(Get(ccss, "grid-column"), Get(ccss, "grid-column-start"), Get(ccss, "grid-column-end"));
            var (r0, rsp, re) = Line(Get(ccss, "grid-row"), Get(ccss, "grid-row-start"), Get(ccss, "grid-row-end"));
            if (Get(ccss, "grid-area") is { } ga)
            {
                var name = ga.Trim();
                if (areas.TryGetValue(name, out var area))
                {
                    r0 = area.row; rsp = area.rows; c0 = area.col; cs = area.cols; re = 0; ce = 0;
                }
                else
                {
                    // grid-area: row-start / column-start / row-end / column-end
                    var p = name.Split('/');
                    (r0, rsp, re) = Line(null, p.Length > 0 ? p[0] : null, p.Length > 2 ? p[2] : null);
                    (c0, cs, ce) = Line(null, p.Length > 1 ? p[1] : null, p.Length > 3 ? p[3] : null);
                }
            }
            items.Add(new Item { Ve = child, Col = c0, ColSpan = Mathf.Max(1, cs), Row = r0, RowSpan = Mathf.Max(1, rsp), ColEnd = ce, RowEnd = re });
        }
        var ncols = cols.Count;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.ColEnd != 0 && it.Col >= 0)
            {
                var end = it.ColEnd < 0 ? ncols + 2 + it.ColEnd : it.ColEnd; // -1 is the line after the last column
                it.ColSpan = Mathf.Max(1, end - 1 - it.Col);
            }
            if (it.RowEnd != 0 && it.Row >= 0)
            {
                var end = it.RowEnd < 0 ? rowsSpec.Count + 2 + it.RowEnd : it.RowEnd;
                it.RowSpan = Mathf.Max(1, end - 1 - it.Row);
            }
            items[i] = it;
        }
        var occupied = new HashSet<(int r, int c)>();
        var cursorR = 0; var cursorC = 0;
        var rowCount = rowsSpec.Count;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.ColSpan > ncols) it.ColSpan = ncols;
            if (it.Col >= 0 && it.Row >= 0)
            {
                // fully explicit
            }
            else if (it.Col >= 0)
            {
                var r = 0;
                while (Collides(occupied, r, it.Col, it.RowSpan, it.ColSpan)) r++;
                it.Row = r;
            }
            else
            {
                var r = it.Row >= 0 ? it.Row : cursorR;
                var c = it.Row >= 0 ? 0 : cursorC;
                while (true)
                {
                    if (c + it.ColSpan > ncols) { c = 0; r++; }
                    if (!Collides(occupied, r, c, it.RowSpan, it.ColSpan)) break;
                    c++;
                }
                it.Row = r; it.Col = c;
                if (items[i].Row < 0) { cursorR = r; cursorC = c + it.ColSpan; if (cursorC >= ncols) { cursorC = 0; cursorR++; } }
            }
            for (var rr = it.Row; rr < it.Row + it.RowSpan; rr++)
                for (var cc = it.Col; cc < it.Col + it.ColSpan; cc++)
                    occupied.Add((rr, cc));
            rowCount = Mathf.Max(rowCount, it.Row + it.RowSpan);
            items[i] = it;
        }

        // Column widths. Content-sized auto columns measure the widest single-column child.
        // auto columns take their content's width (measured from the layout, which sizes them
        // to content); fr columns share what is left
        var colW = Resolve(cols, w, colGap, false, out _);
        foreach (var it in items)
        {
            if (it.ColSpan != 1 || it.Col >= cols.Count || !cols[it.Col].Auto) continue;
            var iw = it.Ve.layout.width;
            if (float.IsNaN(iw)) continue;
            colW[it.Col] = Mathf.Max(colW[it.Col], iw);
        }
        if (definiteWidth && cols.Exists(c => c.Auto))
        {
            var fixedSum = 0f; var frSum = 0f;
            foreach (var c in cols) if (c.Fr > 0f) frSum += c.Fr;
            for (var c = 0; c < cols.Count; c++) if (cols[c].Fr <= 0f) fixedSum += colW[c];
            var free = Mathf.Max(0f, w - fixedSum - colGap * Mathf.Max(0, cols.Count - 1));
            if (frSum > 0f) for (var c = 0; c < cols.Count; c++) if (cols[c].Fr > 0f) colW[c] = free * cols[c].Fr / frSum;
        }

        // Row heights: explicit tracks, implicit rows from grid-auto-rows; auto rows measure
        // the tallest single-row child in them.
        var rows = new List<Track>(rowsSpec);
        while (rows.Count < rowCount) rows.Add(autoRowTrack);
        var content = new float[rows.Count];
        foreach (var it in items)
        {
            if (it.RowSpan != 1) continue;
            var ch = it.Ve.layout.height;
            if (float.IsNaN(ch)) continue;
            content[it.Row] = Mathf.Max(content[it.Row], ch);
        }
        var rowH = Resolve(rows, definiteHeight ? h : 0f, rowGap, false, out var anyFr);
        // fr rows in a grid without a height of its own: all as tall as the tallest of them (grid-auto-rows: 1fr)
        var frMax = 0f;
        if (!definiteHeight)
            for (var r = 0; r < rows.Count; r++) if (rows[r].Fr > 0f) frMax = Mathf.Max(frMax, content[r]);
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Auto) rowH[r] = content[r];
            else if (!definiteHeight && rows[r].Fr > 0f) rowH[r] = frMax;
        }
        if (definiteHeight)
        {
            // a grid with a height of its own hands the space its tracks leave to the auto rows (align-content: stretch)
            var used = 0f; var autoRows = 0;
            for (var r = 0; r < rows.Count; r++) { used += rowH[r]; if (rows[r].Auto) autoRows++; }
            used += rowGap * Mathf.Max(0, rows.Count - 1);
            var free = h - used;
            if (autoRows > 0 && free > 0.5f)
                for (var r = 0; r < rows.Count; r++) if (rows[r].Auto) rowH[r] += free / autoRows;
        }
        var alignItems = (Get(css, "align-items") ?? "stretch").Trim();
        var justifyItems = (Get(css, "justify-items") ?? "stretch").Trim();
        static float Factor(string v) => v is "center" ? 0.5f : v is "end" or "flex-end" or "self-end" or "last baseline" ? 1f : 0f;
        static bool Stretches(string v) => v is "stretch" or "normal" or "auto";

        // Offsets.
        var colX = new float[cols.Count];
        var x = 0f;
        for (var c = 0; c < cols.Count; c++) { colX[c] = x; x += colW[c] + colGap; }
        var rowY = new float[rows.Count];
        var y = 0f;
        for (var r = 0; r < rows.Count; r++) { rowY[r] = y; y += rowH[r] + rowGap; }
        var total = rows.Count > 0 ? y - rowGap : 0f;

        foreach (var it in items)
        {
            var cw = 0f;
            for (var c = it.Col; c < it.Col + it.ColSpan && c < cols.Count; c++) cw += colW[c] + (c > it.Col ? colGap : 0f);
            var chh = 0f;
            var fixedRow = true;
            for (var r = it.Row; r < it.Row + it.RowSpan && r < rows.Count; r++)
            {
                chh += rowH[r] + (r > it.Row ? rowGap : 0f);
                if (rows[r].Auto && !definiteHeight) fixedRow = false;
            }
            var contentCol = it.ColSpan == 1 && it.Col < cols.Count && cols[it.Col].Auto;
            // align-items / justify-items (and the self forms): a child that does not stretch keeps its
            // own size and sits at the start, centre or end of its cell, measured from the layout
            var ccss = _built.CssOf(it.Ve);
            var alignSelf = (Get(ccss, "align-self") ?? alignItems).Trim();
            var justifySelf = (Get(ccss, "justify-self") ?? justifyItems).Trim();
            var ownH = Stretches(alignSelf) || !fixedRow;
            var ownW = Stretches(justifySelf) || contentCol;
            var childH = it.Ve.layout.height; if (float.IsNaN(childH)) childH = 0f;
            var childW = it.Ve.layout.width; if (float.IsNaN(childW)) childW = 0f;
            var offY = ownH ? 0f : Mathf.Max(0f, chh - childH) * Factor(alignSelf);
            var offX = ownW ? 0f : Mathf.Max(0f, cw - childW) * Factor(justifySelf);
            // a stretched item takes the cell (-1 = its own size, left to the cascade or the content)
            var rect = new Rect(padL + colX[it.Col] + offX, padT + rowY[it.Row] + offY, ownW && !contentCol ? cw : -1f, ownH && fixedRow ? chh : -1f);
            if (_placed.TryGetValue(it.Ve, out var prev) && Same(prev, rect))
                continue;
            _placed[it.Ve] = rect;
            var s = it.Ve.style;
            s.position = Position.Absolute;
            s.left = rect.x;
            s.top = rect.y;
            if (contentCol) s.width = StyleKeyword.Auto;
            else if (ownW) s.width = rect.width;
            if (ownH) s.height = fixedRow ? rect.height : StyleKeyword.Auto;
        }

        if (!definiteHeight)
        {
            var min = padT + total + rs.paddingBottom;
            if (Mathf.Abs(min - _minHeight) > 0.5f)
            {
                _minHeight = min;
                _ve.style.minHeight = min;
            }
        }
        if (!definiteWidth)
        {
            var min = padL + (cols.Count > 0 ? x - colGap : 0f) + rs.paddingRight;
            if (Mathf.Abs(min - _minWidth) > 0.5f)
            {
                _minWidth = min;
                _ve.style.minWidth = min;
            }
        }
        _ = anyFr;
    }

    private static bool HasDefiniteWidth(Dictionary<string, string> css)
    {
        return (Get(css, "width") is { } w && w.Trim() != "auto") || Get(css, "flex") != null || Get(css, "flex-grow") != null || Get(css, "flex-basis") != null;
    }

    /// <summary>In a column parent a grid stretches to the parent's width; in a row it has only its content.</summary>
    private bool ParentIsRow()
    {
        var p = _ve.parent;
        if (p == null) return false;
        var dir = p.resolvedStyle.flexDirection;
        return dir == FlexDirection.Row || dir == FlexDirection.RowReverse;
    }

    private static bool Same(Rect a, Rect b)
    {
        return Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f && Mathf.Abs(a.width - b.width) < 0.5f && Mathf.Abs(a.height - b.height) < 0.5f;
    }

    private static bool Collides(HashSet<(int r, int c)> occupied, int r0, int c0, int rs, int cs)
    {
        for (var r = r0; r < r0 + rs; r++)
            for (var c = c0; c < c0 + cs; c++)
                if (occupied.Contains((r, c))) return true;
        return false;
    }

    /// <summary>Track sizes for one axis. `space` is the container size along it (0 when indefinite).</summary>
    private static float[] Resolve(List<Track> tracks, float space, float gap, bool autoIsFr, out bool anyFr)
    {
        var sizes = new float[tracks.Count];
        var fixedSum = 0f;
        var frSum = 0f;
        anyFr = false;
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            if (t.Auto && autoIsFr) { frSum += 1f; anyFr = true; continue; }
            if (t.Auto) continue;
            if (t.Fr > 0f) { frSum += t.Fr; anyFr = true; continue; }
            sizes[i] = t.Px + t.Pct * space / 100f;
            fixedSum += sizes[i];
        }
        var remaining = Mathf.Max(0f, space - fixedSum - gap * Mathf.Max(0, tracks.Count - 1));
        var perFr = frSum > 0f ? remaining / frSum : 0f;
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            if (t.Auto && autoIsFr) sizes[i] = perFr;
            else if (t.Fr > 0f) sizes[i] = perFr * t.Fr;
        }
        return sizes;
    }

    private static List<Track> ParseTracks(string spec)
    {
        var list = new List<Track>();
        foreach (var tok in Split(spec))
        {
            var t = tok.Trim();
            if (t.Length == 0) continue;
            if (t.StartsWith("repeat(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = t.Substring(7, t.Length - 8);
                var comma = inner.IndexOf(',');
                if (comma < 0) continue;
                var count = (int)StyleApplier.Num(inner.Substring(0, comma));
                var body = inner.Substring(comma + 1);
                var sub = ParseTracks(body);
                for (var i = 0; i < count; i++) list.AddRange(sub);
                continue;
            }
            if (t.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = t.Substring(7, t.Length - 8);
                var parts = inner.Split(',');
                var max = ParseTracks(parts[parts.Length - 1]);
                if (max.Count > 0 && !max[0].Auto) { list.Add(max[0]); continue; }
                var min = ParseTracks(parts[0]);
                list.Add(min.Count > 0 ? min[0] : new Track { Auto = true });
                continue;
            }
            if (t.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new Track { Fr = StyleApplier.Num(t.Substring(0, t.Length - 2)) });
                continue;
            }
            if (t == "auto" || t.StartsWith("min-content", StringComparison.Ordinal) || t.StartsWith("max-content", StringComparison.Ordinal) || t.StartsWith("fit-content", StringComparison.Ordinal))
            {
                list.Add(new Track { Auto = true });
                continue;
            }
            if (t.EndsWith("%", StringComparison.Ordinal))
            {
                list.Add(new Track { Pct = StyleApplier.Num(t) });
                continue;
            }
            list.Add(new Track { Px = StyleApplier.Num(t) });
        }
        return list;
    }

    /// <summary>Split on spaces outside parentheses.</summary>
    private static IEnumerable<string> Split(string spec)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= spec.Length; i++)
        {
            if (i < spec.Length)
            {
                if (spec[i] == '(') depth++;
                else if (spec[i] == ')') depth--;
                if (!(spec[i] == ' ' && depth == 0)) continue;
            }
            if (i > start) yield return spec.Substring(start, i - start);
            start = i + 1;
        }
    }

    /// <summary>grid-column / grid-row to (start index or -1 for auto, span, explicit end line or 0).</summary>
    /// <summary>grid-template-areas: each quoted string is a row of cell names; a name's rectangle is its area (0-based row/col, spans).</summary>
    private static Dictionary<string, (int row, int col, int rows, int cols)> ParseAreas(string? text)
    {
        var areas = new Dictionary<string, (int row, int col, int rows, int cols)>(StringComparer.Ordinal);
        if (text == null) return areas;
        var r = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"([^\"]*)\"|'([^']*)'"))
        {
            var cells = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var c = 0; c < cells.Length; c++)
            {
                var name = cells[c];
                if (name == "." || name.Length == 0) continue;
                if (areas.TryGetValue(name, out var a))
                    areas[name] = (a.row, Mathf.Min(a.col, c), Mathf.Max(a.rows, r - a.row + 1), Mathf.Max(a.cols, c - a.col + 1));
                else
                    areas[name] = (r, c, 1, 1);
            }
            r++;
        }
        return areas;
    }

    private static (int start, int span, int end) Line(string? shorthand, string? startProp, string? endProp)
    {
        var start = -1;
        var span = 1;
        var end = 0;
        string? a = startProp, b = endProp;
        if (shorthand != null)
        {
            var slash = shorthand.IndexOf('/');
            a = slash >= 0 ? shorthand.Substring(0, slash) : shorthand;
            b = slash >= 0 ? shorthand.Substring(slash + 1) : null;
        }
        a = a?.Trim(); b = b?.Trim();
        if (a != null && a.StartsWith("span", StringComparison.OrdinalIgnoreCase))
            span = (int)StyleApplier.Num(a.Substring(4));
        else if (a != null && a != "auto" && int.TryParse(a, out var s0))
            start = s0 - 1;
        if (b != null)
        {
            if (b.StartsWith("span", StringComparison.OrdinalIgnoreCase))
                span = (int)StyleApplier.Num(b.Substring(4));
            else if (b != "auto" && int.TryParse(b, out var e0))
                end = e0;
        }
        return (start, span, end);
    }

    private static void Gaps(Dictionary<string, string> css, out float rowGap, out float colGap)
    {
        rowGap = 0f; colGap = 0f;
        var gap = Get(css, "gap") ?? Get(css, "grid-gap");
        if (gap != null)
        {
            var parts = gap.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rowGap = StyleApplier.Num(parts[0]);
            colGap = parts.Length > 1 ? StyleApplier.Num(parts[1]) : rowGap;
        }
        var rg = Get(css, "row-gap") ?? Get(css, "grid-row-gap");
        var cg = Get(css, "column-gap") ?? Get(css, "grid-column-gap");
        if (rg != null) rowGap = StyleApplier.Num(rg);
        if (cg != null) colGap = StyleApplier.Num(cg);
    }

    private static bool HasDefiniteHeight(Dictionary<string, string> css)
    {
        if (Get(css, "height") is { } hh && hh != "auto") return true;
        if (Get(css, "flex") is { } f && StyleApplier.Num(f.Split(' ')[0]) > 0f) return true;
        if (Get(css, "flex-grow") is { } fg && StyleApplier.Num(fg) > 0f) return true;
        if (Get(css, "position") == "absolute" && Get(css, "top") != null && Get(css, "bottom") != null) return true;
        return false;
    }

    private static string? Get(Dictionary<string, string> css, string key)
    {
        return css.TryGetValue(key, out var v) ? v.Trim() : null;
    }
}

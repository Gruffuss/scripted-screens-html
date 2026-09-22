using System.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

using IStyle = ScriptedScreensHtml.ElementStyle;

namespace ScriptedScreensHtml;

/// <summary>
/// Maps CSS declarations onto a VisualElement's inline style. Covers what UI Toolkit can
/// do: box model, flexbox, colours, borders, radii, opacity, text basics, transforms and
/// transitions. Unknown properties are reported once per name and otherwise ignored.
/// </summary>
internal static class StyleApplier
{
    /// <summary>The vector back-end animates; UI Toolkit lays out only.</summary>
    internal static bool VectorMode = true;

    /// <summary>
    /// Declarations already named, so one unsupported property does not warn per element that uses
    /// it. Cleared when a page is built: it is per PAGE, not per process. As a process-global it
    /// made every page after the first look clean, which in game is harmless (one page per surface)
    /// and in the corpus sweep silently undercounted how many pages hit a gap - a scoreboard that
    /// reads better than the truth is worse than no scoreboard.
    /// </summary>
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    /// <summary>Starts a fresh page, so its warnings are its own.</summary>
    /// <summary>Per page, from HtmlRenderer.Build: everything the LAST page told this class.
    /// color-scheme belongs here - it is set by a declaration and was never cleared, so one page's
    /// `color-scheme: dark` picked the dark half of every later page's light-dark().</summary>
    // Dark is the default because CssParser.Discrete answers `prefers-color-scheme: dark`, and a
    // page that declares no color-scheme must not get its dark media rules and its light
    // light-dark() halves at the same time. A console is a lit panel in a dark room.
    internal static void ForgetReported() { Reported.Clear(); ColorSchemeDark = true; }

    /// <summary>
    /// A property the cascade no longer sets (its rule stopped matching) goes back to its initial
    /// value. Properties only the emitter reads (gradients, shadows, clips) are undone by their
    /// removal from the record; ponytail: rarer layout properties keep their last value.
    /// </summary>
    public static void Reset(VisualElement ve, string name)
    {
        var s = ve.style;
        var none = StyleKeyword.Null;
        switch (name)
        {
            case "width": s.width = none; break;
            case "height": s.height = none; break;
            case "min-width": s.minWidth = none; break;
            case "min-height": s.minHeight = none; break;
            case "max-width": s.maxWidth = none; break;
            case "max-height": s.maxHeight = none; break;
            case "margin": s.marginTop = s.marginRight = s.marginBottom = s.marginLeft = none; break;
            case "margin-top": s.marginTop = none; break;
            case "margin-right": s.marginRight = none; break;
            case "margin-bottom": s.marginBottom = none; break;
            case "margin-left": s.marginLeft = none; break;
            case "padding": s.paddingTop = s.paddingRight = s.paddingBottom = s.paddingLeft = none; break;
            case "padding-top": s.paddingTop = none; break;
            case "padding-right": s.paddingRight = none; break;
            case "padding-bottom": s.paddingBottom = none; break;
            case "padding-left": s.paddingLeft = none; break;
            case "position": s.position = none; break;
            case "left": s.left = none; break;
            case "top": s.top = none; break;
            case "right": s.right = none; break;
            case "bottom": s.bottom = none; break;
            case "inset": s.top = s.right = s.bottom = s.left = none; break;
            case "display": s.display = none; s.flexDirection = none; s.FlexDirectionNamed = false; break;
            case "flex-direction": s.flexDirection = none; s.FlexDirectionNamed = false; break;
            case "flex-wrap": s.flexWrap = none; break;
            case "flex-grow": s.flexGrow = none; break;
            case "flex-shrink": s.flexShrink = none; break;
            case "flex-basis": s.flexBasis = none; break;
            case "flex": s.flexGrow = none; s.flexShrink = none; s.flexBasis = none; break;
            case "justify-content": s.justifyContent = none; break;
            case "align-items": s.alignItems = none; break;
            case "align-self": s.alignSelf = none; break;
            case "align-content": s.alignContent = none; break;
            case "overflow": case "overflow-y": s.overflow = none; break;
            case "visibility": s.visibility = none; break;
            case "opacity": s.opacity = none; break;
            case "color": s.color = none; break;
            case "background": case "background-color": s.backgroundColor = none; break;
            case "border-color": s.borderTopColor = s.borderRightColor = s.borderBottomColor = s.borderLeftColor = none; break;
            case "border-top-color": s.borderTopColor = none; break;
            case "border-right-color": s.borderRightColor = none; break;
            case "border-bottom-color": s.borderBottomColor = none; break;
            case "border-left-color": s.borderLeftColor = none; break;
            case "border": case "border-width":
                s.borderTopWidth = s.borderRightWidth = s.borderBottomWidth = s.borderLeftWidth = none;
                s.borderTopColor = s.borderRightColor = s.borderBottomColor = s.borderLeftColor = none;
                break;
            case "border-top": case "border-top-width": s.borderTopWidth = none; break;
            case "border-right": case "border-right-width": s.borderRightWidth = none; break;
            case "border-bottom": case "border-bottom-width": s.borderBottomWidth = none; break;
            case "border-left": case "border-left-width": s.borderLeftWidth = none; break;
            case "border-radius": s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomRightRadius = s.borderBottomLeftRadius = none; break;
            case "font-family": s.face = null; break;
            case "font-size": s.fontSize = none; break;
            case "font-weight": case "font-style": s.unityFontStyleAndWeight = none; break;
            case "text-align": s.unityTextAlign = none; break;
            case "white-space": s.whiteSpace = none; break;
            case "letter-spacing": s.letterSpacing = none; break;
            case "word-spacing": s.wordSpacing = none; break;
            case "text-transform": s.textTransform = null; break;
            case "text-overflow": s.textOverflow = none; break;
            case "transform": s.translate = none; s.rotate = none; s.scale = none; break;
            case "translate": s.translate = none; break;
            case "rotate": s.rotate = none; break;
            case "scale": s.scale = none; break;
        }
    }

    /// <summary>
    /// Where <see cref="Unit"/> reports a value it could not read, since it cannot be given one.
    /// </summary>
    /// <remarks>
    /// Forty-odd length properties reach Unit through Len/LenFor/Num, none of which carry a warn
    /// callback, and a failed parse returned 0f - so `width: attr(data-w px)`, `width: 10qq` and
    /// `gap: var(--never-declared)` all became ZERO with nothing in the log. That is the silent
    /// acceptance this project keeps finding: the page draws, it draws wrongly, and the author has
    /// no line to act on. Threading a parameter through every one of those call sites would be a
    /// large diff for one report; a thread-static sink set for the duration of one Apply is small
    /// and costs nothing when nothing fails.
    ///
    /// ThreadStatic because layout runs on a worker: a plain static would cross two pages' warnings.
    /// </remarks>
    [ThreadStatic] private static Action<string>? _lengthWarn;
    [ThreadStatic] private static string? _lengthProp;

    [ThreadStatic] private static bool _probing;
    [ThreadStatic] private static VisualElement? _probe;

    /// <summary>
    /// Answers <c>@supports (name: value)</c> by trying the declaration and seeing whether anything
    /// read it.
    /// </summary>
    /// <remarks>
    /// The switch in <see cref="Apply"/> IS the list of what is supported, and a second list kept
    /// beside it would drift the first time a property was added to one and not the other. So the
    /// question is answered by asking the code: apply it to a scratch element nobody looks at and
    /// watch for the report. Once per @supports condition at parse time, which is nothing.
    ///
    /// When the scratch element cannot be made, the answer is the OLD one - yes. Failing toward
    /// "supported" applies an enhancement that may do nothing; failing toward "unsupported" would
    /// start dropping every guarded block on a page, which is far worse and much harder to see.
    /// </remarks>
    internal static bool Supports(string name, string value)
    {
        try { _probe ??= new VisualElement(); }
        catch (Exception) { return true; }

        var ok = true;
        var was = _probing;
        _probing = true;
        try { Apply(_probe, new CssDeclaration(name, value), _ => ok = false); }
        catch (Exception) { ok = false; }
        finally { _probing = was; }
        return ok;
    }

    /// <summary>Lets CssParser ask this file what it supports without referencing Unity.</summary>
    internal static void InstallSupportsOracle() => CssParser.SupportsOracle = Supports;

    /// <summary>
    /// Words that reach a length parser legitimately, where returning 0 is the intended answer and
    /// a warning would be noise.
    /// </summary>
    /// <summary>Every value `display` has, so one it does not have is reported rather than drawn.</summary>
    private static readonly HashSet<string> Displays = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "block", "inline", "inline-block", "flex", "inline-flex", "grid", "inline-grid",
        "contents", "flow-root", "list-item", "table", "inline-table", "table-row", "table-cell",
        "table-row-group", "table-header-group", "table-footer-group", "table-column",
        "table-column-group", "table-caption", "ruby", "ruby-base", "ruby-text",
        "inherit", "initial", "unset", "revert", "revert-layer",
    };

    private static readonly HashSet<string> LengthWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "auto", "none", "normal", "inherit", "initial", "unset", "revert", "revert-layer",
        "min-content", "max-content", "fit-content", "stretch", "available", "thin", "medium",
        "thick", "baseline", "center", "left", "right", "top", "bottom", "start", "end", "",
    };

    public static void Apply(VisualElement ve, CssDeclaration d, Action<string>? warn)
    {
        var v = d.Value.Trim();
        var s = ve.style;
        _lengthWarn = warn;
        _lengthProp = d.Name;
        try { ApplyInner(ve, d, warn, v, s); }
        finally { _lengthWarn = null; _lengthProp = null; }
    }

    private static void ApplyInner(VisualElement ve, CssDeclaration d, Action<string>? warn, string v, IStyle s)
    {
        switch (d.Name)
        {
            // Box
            // a box thinner than a pixel is painted a pixel wide, as a browser paints a .5px hairline
            case "width": s.width = Hairline(LenFor(ve, "width", v)); break;
            case "height": s.height = Hairline(LenFor(ve, "height", v)); break;
            case "min-width": s.minWidth = LenFor(ve, "min-width", v); break;
            case "min-height": s.minHeight = LenFor(ve, "min-height", v); break;
            case "max-width": s.maxWidth = LenFor(ve, "max-width", v); break;
            case "max-height": s.maxHeight = LenFor(ve, "max-height", v); break;
            case "margin": Sides(v, out var mt, out var mr, out var mb, out var ml); s.marginTop = mt; s.marginRight = mr; s.marginBottom = mb; s.marginLeft = ml; break;
            case "margin-top": s.marginTop = Len(v); break;
            case "margin-right": s.marginRight = Len(v); break;
            case "margin-bottom": s.marginBottom = Len(v); break;
            case "margin-left": s.marginLeft = Len(v); break;
            case "padding": Sides(v, out var pt, out var pr, out var pb, out var pl); s.paddingTop = pt; s.paddingRight = pr; s.paddingBottom = pb; s.paddingLeft = pl; break;
            case "padding-top": s.paddingTop = Len(v); break;
            case "padding-right": s.paddingRight = Len(v); break;
            case "padding-bottom": s.paddingBottom = Len(v); break;
            case "padding-left": s.paddingLeft = Len(v); break;

            // Position
            case "position": s.position = v == "absolute" || v == "fixed" ? Position.Absolute : Position.Relative; break;
            case "left": s.left = LenFor(ve, "left", v); break;
            case "top": s.top = LenFor(ve, "top", v); break;
            case "right": s.right = LenFor(ve, "right", v); break;
            case "bottom": s.bottom = LenFor(ve, "bottom", v); break;
            case "inset": Sides(v, out var it, out var ir, out var ib, out var il); s.top = it; s.right = ir; s.bottom = ib; s.left = il; break;

            // Flex
            case "display":
                // inline / inline-block: the layout has no inline flow, and an element that
                // reached here is already its own box, so the value is accepted as-is.
                // transition-behavior: allow-discrete holds display: none until the transition ends (Tweens)
                // A value that is not a display at all falls to the default arm below and would be
                // drawn as `flex`, silently - and it would also make @supports claim to support it,
                // which is what turns a mistyped guard into a dropped fallback.
                if (!Displays.Contains(v)) { Unknown(d, warn); break; }
                lock (Tweens.Shared)
                {
                    if (v == "none" && Tweens.AllowDiscrete.Contains(ve)) { Tweens.PendingHide.Add(ve); break; }
                    Tweens.PendingHide.Remove(ve);
                }
                s.display = v == "none" ? DisplayStyle.None : DisplayStyle.Flex;
                // CSS: a flex container lays out in a row unless told otherwise; a block (or
                // grid, whose children are placed absolutely) stacks. "Told otherwise" is any
                // flex-direction the cascade named, whatever order it arrived in - an inline
                // style="display:flex" is applied last and used to undo a rule's column.
                if ((v == "flex" || v == "inline-flex") && !s.FlexDirectionNamed) s.flexDirection = FlexDirection.Row;
                break;
            case "flex-direction":
                s.flexDirection = v switch { "row" => FlexDirection.Row, "row-reverse" => FlexDirection.RowReverse, "column-reverse" => FlexDirection.ColumnReverse, _ => FlexDirection.Column };
                s.FlexDirectionNamed = true;
                break;
            case "flex-wrap": s.flexWrap = v == "wrap" ? Wrap.Wrap : v == "wrap-reverse" ? Wrap.WrapReverse : Wrap.NoWrap; break;
            case "flex-flow":
                foreach (var part in v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    Apply(ve, new CssDeclaration(part.StartsWith("wrap", StringComparison.Ordinal) || part == "nowrap" ? "flex-wrap" : "flex-direction", part), warn);
                break;
            case "place-items": case "place-content": case "place-self":
            {
                // align-* first, justify-* second (or the same value for both)
                var parts = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) break;
                var suffix = d.Name.Substring(6);
                Apply(ve, new CssDeclaration("align-" + suffix, parts[0]), warn);
                if (suffix == "content") Apply(ve, new CssDeclaration("justify-content", parts.Length > 1 ? parts[1] : parts[0]), warn);
                break;
            }
            case "-webkit-line-clamp": case "line-clamp":
            {
                // the box ends after n lines and the emitter ellipsises the last: max-height in line heights
                if (v == "none") break;
                var n = Num(v);
                if (n > 0f) { s.maxHeight = n * EmSize * 1.25f; s.overflow = Overflow.Hidden; }
                break;
            }
            case "flex-grow": s.flexGrow = Num(v); break;
            case "flex-shrink": s.flexShrink = Num(v); break;
            case "flex-basis": s.flexBasis = Len(v); break;
            case "flex":
            {
                // flex: <grow> [<shrink>] [<basis>] | none | auto | <basis>
                if (v == "none") { s.flexGrow = 0; s.flexShrink = 0; s.flexBasis = StyleKeyword.Auto; break; }
                if (v == "auto") { s.flexGrow = 1; s.flexShrink = 1; s.flexBasis = StyleKeyword.Auto; break; }
                var parts = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && IsNumber(parts[0]))
                {
                    s.flexGrow = Num(parts[0]);
                    s.flexShrink = parts.Length >= 2 && IsNumber(parts[1]) ? Num(parts[1]) : 1;
                    s.flexBasis = parts.Length >= 3 ? Len(parts[2]) : (parts.Length == 2 && !IsNumber(parts[1]) ? Len(parts[1]) : new StyleLength(new Length(0)));
                }
                else
                {
                    s.flexBasis = Len(parts[0]);
                }
                break;
            }
            case "justify-content":
                s.justifyContent = v switch { "center" => Justify.Center, "flex-end" or "end" => Justify.FlexEnd, "space-between" => Justify.SpaceBetween, "space-around" or "space-evenly" => Justify.SpaceAround, _ => Justify.FlexStart };
                break;
            case "align-items":
                // baseline: no such mode in the layout engine; bottoms align and PostLayout lifts each
                // child by its own descent so the baselines meet (see PostLayout.Baseline)
                if (v == "baseline" || v == "first baseline" || v == "last baseline") { s.alignItems = Align.FlexEnd; BaselineRows.Add(ve); break; }
                s.alignItems = AlignOf(v);
                break;
            case "align-self": s.alignSelf = AlignOf(v); break;
            case "align-content": s.alignContent = AlignOf(v); break;
            // The layout has one overflow, not one per axis, so a clip on either axis clips the box.
            // Erring toward clipping matches what the author asked for on the axis they named; the
            // other axis of a box that already fits is not observable.
            case "overflow":
            case "overflow-y":
            case "overflow-x": s.overflow = v == "hidden" || v == "clip" || v == "scroll" || v == "auto" ? Overflow.Hidden : Overflow.Visible; break;
            case "visibility": s.visibility = v == "hidden" ? UnityEngine.UIElements.Visibility.Hidden : UnityEngine.UIElements.Visibility.Visible; break;
            case "opacity": s.opacity = Num(v); break;

            // Colour and background
            case "color": if (TryColor(v, out var fg)) s.color = fg; else Unknown(d, warn); break;
            // -webkit-text-fill-color wins over `color` for the glyphs themselves, and every
            // gradient-text recipe on the web sets it. There is no separate fill here, so it IS
            // the colour; `transparent` would hide the text, which is what the recipe intends.
            case "-webkit-text-fill-color": if (TryColor(v, out var tfc)) s.color = tfc; break;
            // content-visibility: auto is a rendering hint a console does not need, but `hidden`
            // is a visible instruction - the subtree is not painted - and there is nothing here
            // that skips a subtree, so saying nothing would draw it in full.
            case "content-visibility":
                // ponytail: `hidden` hides the whole element, where CSS keeps its own background and
                // border and hides only its contents. Closer than painting it in full, which is what
                // warning and doing nothing amounted to.
                if (v == "hidden") s.visibility = Visibility.Hidden;
                break;
            case "background":
            case "background-color":
            case "background-image":
                if (v == "none" || v == "transparent") { s.backgroundColor = Color.clear; s.backgroundImage = StyleKeyword.None; }
                // A layered background ("linear-gradient(...) 6px 0/1px 13px no-repeat, #222") is the
                // emitter's job, layer by layer; here only the colour layer matters. Handing the whole
                // string to Gradient() made it read the second layer's text as the first one's stops.
                else if (v.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase) && SplitTopLevelCommas(v).Count == 1) Gradient(s, v, warn);
                else if (TryColor(v, out var bg)) s.backgroundColor = bg;
                else if (v.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0 || v.IndexOf("gradient(", StringComparison.OrdinalIgnoreCase) >= 0 || v.IndexOf("image-set(", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // the shorthand: a colour token among the image, position, size and repeat parts; the emitter draws the image
                    foreach (var part in SplitTopLevel(v))
                        if (!part.Contains('(') && TryColor(part, out var shc)) { s.backgroundColor = shc; break; }
                }
                else Unknown(d, warn);
                break;

            // Border
            case "border": Border(s, v, warn, true, true, true, true); break;
            case "border-top": Border(s, v, warn, true, false, false, false); break;
            case "border-right": Border(s, v, warn, false, true, false, false); break;
            case "border-bottom": Border(s, v, warn, false, false, true, false); break;
            case "border-left": Border(s, v, warn, false, false, false, true); break;
            case "border-width": Sides(v, out var bt, out var br, out var bb, out var bl); s.borderTopWidth = bt.value.value; s.borderRightWidth = br.value.value; s.borderBottomWidth = bb.value.value; s.borderLeftWidth = bl.value.value; break;
            case "border-top-width": s.borderTopWidth = Num(v); break;
            case "border-right-width": s.borderRightWidth = Num(v); break;
            case "border-bottom-width": s.borderBottomWidth = Num(v); break;
            case "border-left-width": s.borderLeftWidth = Num(v); break;
            case "border-top-style": if (v is "none" or "hidden") s.borderTopWidth = 0f; break;
            case "border-right-style": if (v is "none" or "hidden") s.borderRightWidth = 0f; break;
            case "border-bottom-style": if (v is "none" or "hidden") s.borderBottomWidth = 0f; break;
            case "border-left-style": if (v is "none" or "hidden") s.borderLeftWidth = 0f; break;
            case "border-style":
            {
                var parts = v.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) break;
                if (SideOf(parts, 0) is "none" or "hidden") s.borderTopWidth = 0f;
                if (SideOf(parts, 1) is "none" or "hidden") s.borderRightWidth = 0f;
                if (SideOf(parts, 2) is "none" or "hidden") s.borderBottomWidth = 0f;
                if (SideOf(parts, 3) is "none" or "hidden") s.borderLeftWidth = 0f;
                break;
            }
            case "border-top-color": if (TryColor(v, out var btc)) s.borderTopColor = btc; else Unknown(d, warn); break;
            case "border-right-color": if (TryColor(v, out var brc)) s.borderRightColor = brc; else Unknown(d, warn); break;
            case "border-bottom-color": if (TryColor(v, out var bbc)) s.borderBottomColor = bbc; else Unknown(d, warn); break;
            case "border-left-color": if (TryColor(v, out var blc)) s.borderLeftColor = blc; else Unknown(d, warn); break;
            case "border-color":
                if (TryColor(v, out var bc)) { s.borderTopColor = bc; s.borderRightColor = bc; s.borderBottomColor = bc; s.borderLeftColor = bc; }
                else Unknown(d, warn);
                break;
            case "border-radius": Sides(v, out var tl, out var tr, out var brr, out var bll); s.borderTopLeftRadius = tl; s.borderTopRightRadius = tr; s.borderBottomRightRadius = brr; s.borderBottomLeftRadius = bll; break;
            case "border-top-left-radius": s.borderTopLeftRadius = Len(v); break;
            case "border-top-right-radius": s.borderTopRightRadius = Len(v); break;
            case "border-bottom-right-radius": s.borderBottomRightRadius = Len(v); break;
            case "border-bottom-left-radius": s.borderBottomLeftRadius = Len(v); break;

            // Text
            case "font-family":
            {
                // A face registered with TextMeshPro (the Fonts mod's, the game's), copied for
                // measuring. No font files, no OS fonts. An unknown family keeps the inherited
                // face and warns once.
                FaceData? face = null;
                foreach (var raw in v.Split(','))
                {
                    var name = raw.Trim().Trim('"', (char)39);
                    if (name.Length == 0) continue;
                    face = FontLibrary.Get(MapGeneric(name) ?? name);
                    if (face != null) break;
                }
                if (face != null) s.face = face;
                else Unknown(d, warn);
                break;
            }
            case "font":
            {
                // font: [style] [weight] size[/line-height] family-list
                var parts = SplitTopLevel(v);
                var familyStart = -1;
                for (var i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    if (p == "italic" || p == "oblique") { Apply(ve, new CssDeclaration("font-style", "italic"), warn); continue; }
                    if (p == "bold" || p == "bolder" || (IsNumber(p) && Num(p) >= 100)) { Apply(ve, new CssDeclaration("font-weight", p), warn); continue; }
                    if (p == "normal") continue;
                    if (char.IsDigit(p[0]) || p[0] == '.')
                    {
                        var slash = p.IndexOf('/');
                        Apply(ve, new CssDeclaration("font-size", slash > 0 ? p.Substring(0, slash) : p), warn);
                        familyStart = i + 1;
                        break;
                    }
                }
                if (familyStart >= 0 && familyStart < parts.Count)
                    Apply(ve, new CssDeclaration("font-family", string.Join(" ", parts.GetRange(familyStart, parts.Count - familyStart))), warn);
                break;
            }
            case "font-size": s.fontSize = Len(v); break;
            case "font-weight":
            case "font-style":
            {
                var bold = v == "bold" || v == "bolder" || (IsNumber(v) && Num(v) >= 600);
                var italic = v == "italic" || v == "oblique";
                var cur = s.unityFontStyleAndWeight.value;
                var wasBold = cur == FontStyle.Bold || cur == FontStyle.BoldAndItalic;
                var wasItalic = cur == FontStyle.Italic || cur == FontStyle.BoldAndItalic;
                if (d.Name == "font-weight") wasBold = bold; else wasItalic = italic;
                s.unityFontStyleAndWeight = wasBold && wasItalic ? FontStyle.BoldAndItalic : wasBold ? FontStyle.Bold : wasItalic ? FontStyle.Italic : FontStyle.Normal;
                break;
            }
            case "text-align":
                s.unityTextAlign = v switch { "center" => TextAnchor.MiddleCenter, "right" or "end" => TextAnchor.MiddleRight, _ => TextAnchor.MiddleLeft };
                break;
            case "white-space": s.whiteSpace = v == "nowrap" || v == "pre" ? WhiteSpace.NoWrap : WhiteSpace.Normal; break;
            // text-wrap is the same wrapping switch under its modern name (balance/pretty still wrap)
            case "text-wrap": case "text-wrap-mode": s.whiteSpace = v == "nowrap" ? WhiteSpace.NoWrap : WhiteSpace.Normal; break;
            case "white-space-collapse": if (v.StartsWith("preserve", StringComparison.Ordinal)) s.whiteSpace = WhiteSpace.NoWrap; break;
            // `light dark` declares support for BOTH and is what every dark-capable page writes;
            // the old test read it as light-only. It has to agree with CssParser.Discrete, which
            // answers `prefers-color-scheme: dark` - otherwise a page gets its dark media rules and
            // the light half of every light-dark(), which looks like the page's own mistake.
            case "color-scheme": ColorSchemeDark = v.Contains("dark"); break;
            case "initial-letter":
            {
                // initial-letter: <lines> [<sink>]: the drop cap spans that many lines of the paragraph (line height taken as 1.2em)
                var first = v.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (first != "normal" && IsNumber(first) && Num(first) > 0f) s.fontSize = Num(first) * EmSize * 1.2f;
                break;
            }
            case "letter-spacing": s.letterSpacing = Len(v); break;
            // drawn by the emitter; the layout measures the transformed text
            case "text-transform": s.textTransform = v.Trim().ToLowerInvariant(); break;
            // The LAYOUT takes it (the box is measured with the spacing) but the scene's label has
            // no word spacing, only cspace - so the box is wide and the text is not. Applied and
            // reported, because half of it does happen.
            case "word-spacing": s.wordSpacing = Len(v); if (v != "normal" && Num(v) != 0f) Unknown(d, warn); break;
            case "text-overflow": s.textOverflow = v == "ellipsis" ? TextOverflow.Ellipsis : TextOverflow.Clip; break;
            case "text-shadow":
            {
                // <x> <y> [blur] <color>
                var parts = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    var sh = new TextShadow { offset = new Vector2(Num(parts[0]), Num(parts[1])) };
                    var next = 2;
                    if (parts.Length >= 4 && IsNumber(parts[2].TrimEnd('p', 'x'))) { sh.blurRadius = Num(parts[2]); next = 3; }
                    if (TryColor(string.Join(" ", parts, next, parts.Length - next), out var sc)) sh.color = sc;
                    s.textShadow = sh;
                }
                break;
            }

            // Transform
            case "transform":
            {
                if (v == "none") { s.translate = new Translate(0, 0); s.rotate = new Rotate(0); s.scale = new Scale(Vector2.one); break; }
                if (NeedsMatrix(v))
                {
                    // skew(), matrix(), 3D functions: the emitter composes the whole transform
                    // into a matrix on the group; the layout engine must not apply any of it.
                    s.translate = new Translate(0, 0); s.rotate = new Rotate(0); s.scale = new Scale(Vector2.one);
                    break;
                }
                // Scanned in place. This is the hot write of every animating page, and the iterator
                // version made a name string, a lowercased copy, an argument array and a trimmed
                // string per argument for a value that is only ever read as numbers.
                var fn = new TransformScan(v.AsSpan());
                while (fn.Next())
                {
                    var name = fn.Name;
                    if (Same(name, "translate")) s.translate = new Translate(Len(fn.Arg(0)).value, fn.Count > 1 ? Len(fn.Arg(1)).value : new Length(0));
                    else if (Same(name, "translatex")) s.translate = new Translate(Len(fn.Arg(0)).value, new Length(0));
                    else if (Same(name, "translatey")) s.translate = new Translate(new Length(0), Len(fn.Arg(0)).value);
                    else if (Same(name, "rotate")) s.rotate = new Rotate(Angle(fn.Arg(0)));
                    else if (Same(name, "scale")) s.scale = new Scale(new Vector2(Num(fn.Arg(0)), Num(fn.Arg(fn.Count > 1 ? 1 : 0))));
                    else if (Same(name, "scalex")) s.scale = new Scale(new Vector2(Num(fn.Arg(0)), 1));
                    else if (Same(name, "scaley")) s.scale = new Scale(new Vector2(1, Num(fn.Arg(0))));
                    else warn?.Invoke($"css: transform {name.ToString()}() not supported");
                }
                break;
            }
            case "transform-origin":
            {
                var parts = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var x = parts.Length > 0 ? OriginLen(parts[0]) : new Length(50, LengthUnit.Percent);
                var y = parts.Length > 1 ? OriginLen(parts[1]) : new Length(50, LengthUnit.Percent);
                s.transformOrigin = new TransformOrigin(x, y, 0);
                break;
            }

            // Transitions
            case "transition":
            {
                // Vector mode: transitions are compiled to expressions (Tweens.cs). UI Toolkit
                // interpolating as well would put a moving snapshot under the emitter.
                if (VectorMode) break;
                // transition: prop dur [easing] [delay] {, ...}
                var props = new List<StylePropertyName>();
                var durs = new List<TimeValue>();
                var eases = new List<EasingFunction>();
                var delays = new List<TimeValue>();
                foreach (var item in v.Split(','))
                {
                    var parts = item.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;
                    props.Add(new StylePropertyName(UssName(parts[0])));
                    durs.Add(parts.Length > 1 ? Time(parts[1]) : new TimeValue(0));
                    var ease = EasingMode.Ease;
                    var delay = new TimeValue(0);
                    for (var i = 2; i < parts.Length; i++)
                    {
                        if (TryEasing(parts[i], out var e)) ease = e; else delay = Time(parts[i]);
                    }
                    eases.Add(new EasingFunction(ease));
                    delays.Add(delay);
                }
                s.transitionProperty = props;
                s.transitionDuration = durs;
                s.transitionTimingFunction = eases;
                s.transitionDelay = delays;
                break;
            }
            case "transition-property": { if (VectorMode) break; var l = new List<StylePropertyName>(); foreach (var p in v.Split(',')) l.Add(new StylePropertyName(UssName(p.Trim()))); s.transitionProperty = l; break; }
            case "transition-duration": { if (VectorMode) break; var l = new List<TimeValue>(); foreach (var p in v.Split(',')) l.Add(Time(p.Trim())); s.transitionDuration = l; break; }
            case "transition-delay": { if (VectorMode) break; var l = new List<TimeValue>(); foreach (var p in v.Split(',')) l.Add(Time(p.Trim())); s.transitionDelay = l; break; }
            case "transition-timing-function": { if (VectorMode) break; var l = new List<EasingFunction>(); foreach (var p in v.Split(',')) l.Add(new EasingFunction(TryEasing(p.Trim(), out var e) ? e : EasingMode.Ease)); s.transitionTimingFunction = l; break; }

            default:
                Unknown(d, warn);
                break;
        }
    }

    /// <summary>linear-gradient(...) as a background: its stops are checked here; the emitter draws it from the CSS record.</summary>
    private static void Gradient(IStyle s, string v, Action<string>? warn)
    {
        var open = v.IndexOf('(');
        var close = v.LastIndexOf(')');
        if (open < 0 || close < open) { warn?.Invoke($"css: bad gradient \"{v}\""); return; }
        var args = SplitTopLevelCommas(v.Substring(open + 1, close - open - 1));
        var first = args.Count > 0 ? args[0].Trim() : string.Empty;
        if (first.StartsWith("to ", StringComparison.OrdinalIgnoreCase) || first.EndsWith("deg", StringComparison.OrdinalIgnoreCase) || first.EndsWith("turn", StringComparison.OrdinalIgnoreCase))
            args.RemoveAt(0);

        var stops = new List<(float at, Color c)>();
        for (var i = 0; i < args.Count; i++)
        {
            var parts = SplitTopLevel(args[i].Trim());
            if (parts.Count == 0 || !TryColor(parts[0], out var c)) { warn?.Invoke($"css: gradient stop \"{args[i]}\" not understood"); continue; }
            var at = parts.Count > 1 && parts[1].EndsWith("%", StringComparison.Ordinal) ? Num(parts[1]) / 100f : (args.Count == 1 ? 0f : (float)i / (args.Count - 1));
            stops.Add((at, c));
        }
        // the emitter draws the gradient from the CSS record; the box keeps no colour of its own
        s.backgroundColor = stops.Count == 1 ? stops[0].c : Color.clear;
    }

    private static List<string> SplitTopLevelCommas(string v)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= v.Length; i++)
        {
            if (i < v.Length)
            {
                if (v[i] == '(') depth++;
                else if (v[i] == ')') depth--;
                if (!(v[i] == ',' && depth == 0)) continue;
            }
            list.Add(v.Substring(start, i - start));
            start = i + 1;
        }
        return list;
    }

    /// <summary>Handled outside the style object: by the grid layout, the emitter, or by the box model itself.</summary>
    private static bool MixPart(string part, out Color c, out float pct)
    {
        pct = float.NaN;
        var tokens = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var colourText = part.Trim();
        if (tokens.Length >= 2 && tokens[tokens.Length - 1].EndsWith("%", StringComparison.Ordinal))
        {
            pct = Num(tokens[tokens.Length - 1]);
            colourText = colourText.Substring(0, colourText.Length - tokens[tokens.Length - 1].Length).Trim();
        }
        else if (tokens.Length >= 2 && tokens[0].EndsWith("%", StringComparison.Ordinal))
        {
            pct = Num(tokens[0]);
            colourText = colourText.Substring(tokens[0].Length).Trim();
        }
        return TryColor(colourText, out c);
    }

    private static readonly HashSet<string> Elsewhere = new(StringComparer.Ordinal)
    {
        // Batch F5: accepted silently. Each has no visible effect here (no printing, no pointer
        // physics, no font hinting, no snap physics) or is a hint the layout does not need.
        "scroll-snap-type", "scroll-snap-align", "scroll-snap-stop", "scroll-margin", "scroll-margin-top", "scroll-margin-right", "scroll-margin-bottom", "scroll-margin-left",
        "scroll-padding", "scroll-padding-top", "scroll-padding-right", "scroll-padding-bottom", "scroll-padding-left", "scroll-behavior", "overscroll-behavior", "overscroll-behavior-x", "overscroll-behavior-y",
        "will-change", "contain", "content-visibility", "isolation", "touch-action", "-webkit-font-smoothing", "-moz-osx-font-smoothing", "font-smooth", "text-rendering", "image-rendering",
        "color-scheme", "zoom", "all", "text-wrap", "text-size-adjust", "-webkit-text-size-adjust", "-webkit-tap-highlight-color", "-webkit-overflow-scrolling", "print-color-adjust", "-webkit-print-color-adjust", "forced-color-adjust",
        "resize", "caret-color", "tab-size", "orphans", "widows", "page-break-before", "page-break-after", "page-break-inside", "break-before", "break-after", "break-inside",
        "unicode-bidi", "direction", "font-kerning", "font-feature-settings", "font-optical-sizing", "font-synthesis", "font-stretch", "font-variant", "font-variant-ligatures", "font-variant-caps", "quotes", "hanging-punctuation",
        "background-attachment", "animation-timeline", "animation-range", "animation-range-start", "animation-range-end",
        "scroll-timeline", "scroll-timeline-name", "scroll-timeline-axis", "view-timeline", "view-timeline-name", "view-timeline-axis", "timeline-scope",
        "corner-shape", "border-image", "border-image-source", "border-image-slice", "border-image-width", "border-image-repeat", "border-image-outset",
        "backface-visibility", "perspective", "perspective-origin", "transform-style", "scrollbar-width", "scrollbar-color", "scrollbar-gutter",
        "background-clip", "-webkit-background-clip", "-webkit-text-fill-color", "ruby-position", "ruby-align",
        "justify-items", "justify-self", "table-layout", "caption-side", "empty-cells", "grid-area", "grid-template", "grid-template-areas",
        "counter-reset", "counter-increment", "counter-set", "list-style-image", "text-indent", "word-break", "overflow-wrap", "word-wrap", "hyphens", "text-align-last", "order", "-webkit-box-orient",
        "container", "container-type", "container-name",
        "text-decoration-line", "text-decoration-color", "text-decoration-style", "text-decoration-thickness", "text-underline-offset", "text-underline-position", "text-decoration-skip-ink",
        "gap", "row-gap", "column-gap", "grid-gap", "grid-row-gap", "grid-column-gap",
        "grid-template-columns", "grid-template-rows", "grid-auto-rows", "grid-auto-columns", "grid-auto-flow",
        "grid-column", "grid-row", "grid-column-start", "grid-column-end", "grid-row-start", "grid-row-end",
        "line-height", "text-transform", "z-index", "box-sizing",
        "box-shadow", "text-shadow", "border-style", "text-decoration",
        "outline", "outline-width", "outline-color", "outline-style", "outline-offset",
        "pointer-events", "cursor", "user-select", "content", "appearance", "-webkit-appearance", "-moz-appearance", "accent-color",
        "filter", "clip-path", "mask-image", "-webkit-mask-image", "mask", "writing-mode", "text-orientation", "vertical-align", "object-fit", "object-position",
        "font-variant-numeric", "fill", "stroke", "stroke-width", "stroke-opacity", "fill-opacity", "fill-rule", "stroke-dasharray", "stroke-dashoffset", "stroke-linecap", "stroke-linejoin", "text-anchor", "dominant-baseline", "stroke-miterlimit",
        "background-size", "background-position", "background-repeat", "float", "clear", "column-count", "columns", "column-gap", "aspect-ratio", "mix-blend-mode", "backdrop-filter",
        "list-style", "list-style-type", "list-style-position", "border-collapse", "border-spacing",
        "column-rule", "column-rule-width", "column-rule-style", "column-rule-color", "offset-path", "offset-distance", "offset-rotate",
        "offset-anchor", "offset-position", "background-position-x", "background-position-y", "background-origin", "overflow-clip-margin", "clip-rule",
        "corner-top-left-shape", "corner-top-right-shape", "corner-bottom-right-shape", "corner-bottom-left-shape", "corner-top-shape", "corner-bottom-shape", "corner-left-shape", "corner-right-shape",
        "column-width", "column-span", "column-fill", "transform-box", "transition-behavior", "grid",
        "text-emphasis-style", "text-emphasis-color", "text-emphasis-position", "baseline-shift", "alignment-baseline",
        "mask-position", "mask-size", "mask-origin", "mask-clip", "mask-repeat", "paint-order", "vector-effect", "shape-rendering", "marker-start", "marker-mid", "marker-end", "marker",
        "cx", "cy", "r", "rx", "ry", "x", "y", "d", "path-length", // svg geometry as CSS: every declaration on a shape becomes its attribute in the collector
    };

    /// <summary>
    /// Properties that change what a BROWSER paints and that this renderer drops. Each says why, once
    /// per page, naming itself.
    /// </summary>
    /// <remarks>
    /// The split from <see cref="Elsewhere"/> is the whole point. That set holds two very different
    /// things that were being treated alike: properties another stage really does read (gap, grid-*,
    /// box-shadow, clip-path), and properties nothing reads at all. For the first, silence is
    /// correct. For the second it is the worst outcome there is - the page sets `background-blend-mode`
    /// or `border-collapse`, nothing complains, and the console quietly looks wrong.
    ///
    /// Not listed, deliberately: a property whose effect a console cannot have in the first place.
    /// `cursor`, `user-select`, `resize`, `caret-color`, `scroll-behavior`, `touch-action`,
    /// `will-change`, `text-rendering`, the print and page-break family. Ignoring those IS correct,
    /// and a warning for them would be noise an author cannot act on - which is how a warning list
    /// stops being read.
    /// </remarks>
    private static readonly Dictionary<string, string> Dropped = new(StringComparer.Ordinal)
    {
        ["all"] = "resets every property, which the cascade here cannot undo",
        ["appearance"] = "the controls are drawn, not native, so there is no native look to remove",
        ["accent-color"] = "the drawn controls carry their own colour",
        ["backdrop-filter"] = "nothing is composited behind a shape to filter",
        ["background-attachment"] = "the page does not scroll under its background",
        ["background-blend-mode"] = "layers are drawn one over another, never blended",
        ["mix-blend-mode"] = "shapes are drawn one over another, never blended",
        ["backface-visibility"] = "the scene is 2D",
        ["perspective"] = "the scene is 2D",
        ["perspective-origin"] = "the scene is 2D",
        ["transform-style"] = "the scene is 2D",
        ["border-collapse"] = "table borders are drawn per cell",
        ["border-spacing"] = "table cells are laid out without separation",
        ["caption-side"] = "a caption stays where it is written",
        ["empty-cells"] = "an empty cell is drawn like any other",
        ["table-layout"] = "columns are always sized from their content",
        ["columns"] = "the shorthand is not split; set column-count",
        ["column-span"] = "a column-spanning element is laid out in its column",
        ["clear"] = "there is no float line to clear",
        ["direction"] = "the page is laid out left to right",
        ["overflow-wrap"] = "a long word is not broken; word-break: break-all is",
        ["word-wrap"] = "a long word is not broken; word-break: break-all is",
        ["tab-size"] = "a tab is drawn at the face's own width",
        ["quotes"] = "content: open-quote is not understood, so a quote pair set here would change nothing",
        ["list-style-position"] = "a marker always sits outside the item",
        ["text-orientation"] = "a vertical label is the whole line turned, so its glyphs turn with it and cannot be set upright one by one",
        ["text-emphasis-style"] = "there are no emphasis marks",
        ["text-emphasis-color"] = "there are no emphasis marks",
        ["text-emphasis-position"] = "there are no emphasis marks",
        ["offset-rotate"] = "an element on an offset-path keeps its own rotation",
        ["marker"] = "line markers are not drawn",
        ["zoom"] = "set the design size with <meta name=\"viewport\" content=\"width=N\">",
        // Each of these was parsed, put in the record, and read by nothing - the worst outcome.
        ["font-variant"] = "small caps are not drawn; use text-transform: uppercase with a smaller size",
        ["font-variant-caps"] = "small caps are not drawn; use text-transform: uppercase with a smaller size",
        ["column-fill"] = "columns are filled in order, never balanced",
        ["border-image-slice"] = "a border image is stretched over the border box; it is not sliced",
        ["border-image-repeat"] = "a border image is stretched over the border box; it is not tiled",
        ["border-image-outset"] = "a border image stays inside the border box",
        ["mask-position"] = "a mask is drawn over the whole box",
        ["mask-repeat"] = "a mask is drawn over the whole box, never tiled",
        ["content-visibility"] = "nothing here skips a subtree, so a hidden one is still drawn",
        ["word-spacing"] = "the label has no word spacing, only letter-spacing; the box is measured with it and the text drawn without",
    };

    private static void Unknown(CssDeclaration d, Action<string>? warn)
    {
        // @supports is asking a question, not styling anything: answer it and leave `Reported`
        // alone. Going through the de-duplicating path would make the SECOND query about the same
        // declaration answer "supported", since the report is swallowed and the probe sees no
        // warning - the process-global failure shape this file has now produced three times.
        if (_probing) { warn?.Invoke(string.Empty); return; }
        if (Dropped.TryGetValue(d.Name, out var why))
        {
            if (Reported.Add(d.Name)) warn?.Invoke($"css: \"{d.Name}: {d.Value}\" is not drawn - {why}");
            return;
        }
        if (Elsewhere.Contains(d.Name))
            return;
        if (Reported.Add(d.Name + ":" + d.Value))
            warn?.Invoke($"css: \"{d.Name}: {d.Value}\" not supported");
    }

    // ---- values ----

    public static bool IsNumber(string v) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>Unit context for em, rem, vw and vh: set per element by the cascade, per page by the renderer.</summary>
    /// A translation worker reads the values the game thread had when its job started (OffThread.Job).
    internal static float EmSize { get => OffThread.Active ? OffThread.Job.EmSize : _emSize; set { if (OffThread.Active) OffThread.Job.EmSize = value; else _emSize = value; } }
    internal static float RootFontSize { get => OffThread.Active ? OffThread.Job.RootFontSize : _rootFontSize; set { if (OffThread.Active) OffThread.Job.RootFontSize = value; else _rootFontSize = value; } }
    /// <summary>color-scheme: dark seen in the cascade; light-dark() picks its second value then.</summary>
    internal static bool ColorSchemeDark { get => OffThread.Active ? OffThread.Job.ColorSchemeDark : _colorSchemeDark; set { if (OffThread.Active) OffThread.Job.ColorSchemeDark = value; else _colorSchemeDark = value; } }
    internal static float ViewportW { get => OffThread.Active ? OffThread.Job.ViewportW : _viewportW; set { if (OffThread.Active) OffThread.Job.ViewportW = value; else _viewportW = value; } }
    internal static float ViewportH { get => OffThread.Active ? OffThread.Job.ViewportH : _viewportH; set { if (OffThread.Active) OffThread.Job.ViewportH = value; else _viewportH = value; } }
    private static float _emSize = 16f, _rootFontSize = 16f, _viewportW = 460f, _viewportH = 460f;
    private static bool _colorSchemeDark = true;

    /// <summary>A number in px (or the bare number of a percentage). Units: px pt em rem vw vh; calc().</summary>
    public static float Num(string v)
    {
        v = v.Trim();
        if (v.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
        {
            Calc(v, out var px, out var pct);
            return Mathf.Abs(px) > 0.0001f || Mathf.Abs(pct) < 0.0001f ? px : pct;
        }
        if (Func(v) is { } f) return f;
        return Unit(v);
    }

    /// <summary>The unit suffix stripped by slicing, not by Substring: every Num() in the codebase came
    /// through here and paid a string for the digits it was about to parse.</summary>
    private static float Unit(ReadOnlySpan<char> v)
    {
        var scale = 1f;
        if (Ends(v, "px")) v = v.Slice(0, v.Length - 2);
        else if (v.EndsWith("%".AsSpan(), StringComparison.Ordinal)) v = v.Slice(0, v.Length - 1);
        else if (Ends(v, "rem")) { v = v.Slice(0, v.Length - 3); scale = RootFontSize; }
        else if (Ends(v, "em")) { v = v.Slice(0, v.Length - 2); scale = EmSize; }
        // The small/large/dynamic viewport units, before vw/vh: "10svh" ends with "vh", so the
        // plain branch took it and left "10s", which parses as 0. A console's viewport never
        // grows or shrinks, so all three are the same as vw/vh.
        else if (Ends(v, "svw") || Ends(v, "lvw") || Ends(v, "dvw")) { v = v.Slice(0, v.Length - 3); scale = ViewportW / 100f; }
        else if (Ends(v, "svh") || Ends(v, "lvh") || Ends(v, "dvh")) { v = v.Slice(0, v.Length - 3); scale = ViewportH / 100f; }
        else if (Ends(v, "vw")) { v = v.Slice(0, v.Length - 2); scale = ViewportW / 100f; }
        else if (Ends(v, "vh")) { v = v.Slice(0, v.Length - 2); scale = ViewportH / 100f; }
        // lh / rlh: the line box of this element / of the root. The emitter's own "normal" is 1.2.
        else if (Ends(v, "rlh")) { v = v.Slice(0, v.Length - 3); scale = RootFontSize * 1.2f; }
        else if (Ends(v, "lh")) { v = v.Slice(0, v.Length - 2); scale = EmSize * 1.2f; }
        else if (Ends(v, "vmin")) { v = v.Slice(0, v.Length - 4); scale = Mathf.Min(ViewportW, ViewportH) / 100f; }
        else if (Ends(v, "vmax")) { v = v.Slice(0, v.Length - 4); scale = Mathf.Max(ViewportW, ViewportH) / 100f; }
        else if (Ends(v, "pt")) { v = v.Slice(0, v.Length - 2); scale = 4f / 3f; }
        else if (Ends(v, "pc")) { v = v.Slice(0, v.Length - 2); scale = 16f; }
        else if (Ends(v, "cm")) { v = v.Slice(0, v.Length - 2); scale = 96f / 2.54f; }
        else if (Ends(v, "mm")) { v = v.Slice(0, v.Length - 2); scale = 96f / 25.4f; }
        else if (Ends(v, "in")) { v = v.Slice(0, v.Length - 2); scale = 96f; }
        else if (Ends(v, "cap")) { v = v.Slice(0, v.Length - 3); scale = EmSize * 0.7f; }  // ponytail: a typical cap height; the font's own is not exposed
        else if (Ends(v, "ic")) { v = v.Slice(0, v.Length - 2); scale = EmSize; }          // ponytail: the ideographic advance is one em in CJK faces
        else if (Ends(v, "q") && !Ends(v, "sq")) { v = v.Slice(0, v.Length - 1); scale = 96f / 25.4f / 4f; }
        else if (Ends(v, "ch")) { v = v.Slice(0, v.Length - 2); scale = EmSize * 0.5f; }   // the "0" of a text face is about half an em
        else if (Ends(v, "ex")) { v = v.Slice(0, v.Length - 2); scale = EmSize * 0.5f; }
        else if (Ends(v, "q")) { v = v.Slice(0, v.Length - 1); scale = 96f / 25.4f / 4f; }
        else if (Ends(v, "grad")) { v = v.Slice(0, v.Length - 4); scale = 0.9f; }
        else if (Ends(v, "deg")) { v = v.Slice(0, v.Length - 3); }
        else if (Ends(v, "rad")) { v = v.Slice(0, v.Length - 3); scale = 180f / Mathf.PI; }
        else if (Ends(v, "turn")) { v = v.Slice(0, v.Length - 4); scale = 360f; }
        if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f * scale;
        // 0 is what a browser uses for an invalid length too, so the DRAWING is not the defect -
        // the silence was. Reported once per property and value so a page cannot flood the log.
        if (_lengthWarn is { } warn && !LengthWords.Contains(v.ToString())
            && (_probing || Reported.Add("len:" + _lengthProp + ":" + v.ToString())))
            warn.Invoke($"css: \"{_lengthProp}: {v.ToString()}\" is not a length this understands, so it is 0");
        return 0f;
    }

    private static bool Ends(ReadOnlySpan<char> v, string suffix) => v.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase);

    private static bool Same(ReadOnlySpan<char> v, string other) => v.Equals(other.AsSpan(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Num/Len/Angle over a slice of a larger string. A value carrying '(' (calc, min, clamp,
    /// a trigonometric function) needs the string paths and materialises there; a plain number does not.</summary>
    private static float Num(ReadOnlySpan<char> v)
    {
        v = v.Trim();
        return v.IndexOf('(') >= 0 ? Num(v.ToString()) : Unit(v);
    }

    private static StyleLength Len(ReadOnlySpan<char> v)
    {
        v = v.Trim();
        if (v.IndexOf('(') >= 0) return Len(v.ToString());
        if (Same(v, "auto")) return StyleKeyword.Auto;
        if (Same(v, "initial") || Same(v, "unset")) return StyleKeyword.Initial;
        if (v.EndsWith("%".AsSpan(), StringComparison.Ordinal)) return new Length(Unit(v), LengthUnit.Percent);
        return new Length(Unit(v), LengthUnit.Pixel);
    }

    /// <summary>
    /// The CSS math functions over lengths and numbers: min/max/clamp, the trigonometric ones
    /// (angles in deg/rad/grad/turn, a bare number is radians), pow/sqrt/hypot/log/exp,
    /// abs/sign/mod/rem/round. Each argument may be a calc-style expression. Null when not one.
    /// </summary>
    private static float? Func(string v)
    {
        var lower = v.ToLowerInvariant();
        var paren = lower.IndexOf('(');
        if (paren <= 0 || !lower.EndsWith(")", StringComparison.Ordinal)) return null;
        var fn = lower.Substring(0, paren).Trim();
        if (fn is not ("min" or "max" or "clamp" or "sin" or "cos" or "tan" or "asin" or "acos" or "atan" or "atan2" or "pow" or "sqrt" or "hypot" or "log" or "exp" or "abs" or "sign" or "mod" or "rem" or "round"))
            return null;
        var inner = v.Substring(paren + 1, Math.Max(0, v.Length - paren - 2));
        var raw = CssParser.SplitTopLevel(inner, ',');
        static float Angle(string a)
        {
            a = a.Trim().ToLowerInvariant();
            static float N(string t) => float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
            if (a.EndsWith("deg", StringComparison.Ordinal)) return N(a.Substring(0, a.Length - 3)) * Mathf.Deg2Rad;
            if (a.EndsWith("grad", StringComparison.Ordinal)) return N(a.Substring(0, a.Length - 4)) * Mathf.PI / 200f;
            if (a.EndsWith("turn", StringComparison.Ordinal)) return N(a.Substring(0, a.Length - 4)) * 2f * Mathf.PI;
            if (a.EndsWith("rad", StringComparison.Ordinal)) return N(a.Substring(0, a.Length - 3));
            return Num("calc(" + a + ")");
        }
        var args = new List<float>();
        var isTrig = fn is "sin" or "cos" or "tan";
        for (var i = 0; i < raw.Count; i++)
        {
            var a = raw[i].Trim();
            if (fn == "round" && i == 0 && a is "nearest" or "up" or "down" or "to-zero") { continue; }
            args.Add(isTrig ? Angle(a) : Num("calc(" + a + ")"));
        }
        if (args.Count == 0) return 0f;
        switch (fn)
        {
            case "min": { var m = args[0]; foreach (var a in args) m = Mathf.Min(m, a); return m; }
            case "max": { var m = args[0]; foreach (var a in args) m = Mathf.Max(m, a); return m; }
            case "clamp": return args.Count >= 3 ? Mathf.Clamp(args[1], args[0], args[2]) : args[0];
            case "sin": return Mathf.Sin(args[0]);
            case "cos": return Mathf.Cos(args[0]);
            case "tan": return Mathf.Tan(args[0]);
            case "asin": return Mathf.Asin(Mathf.Clamp(args[0], -1f, 1f)) * Mathf.Rad2Deg; // angles come back in degrees, as CSS returns <angle>
            case "acos": return Mathf.Acos(Mathf.Clamp(args[0], -1f, 1f)) * Mathf.Rad2Deg;
            case "atan": return Mathf.Atan(args[0]) * Mathf.Rad2Deg;
            case "atan2": return args.Count >= 2 ? Mathf.Atan2(args[0], args[1]) * Mathf.Rad2Deg : 0f;
            case "pow": return args.Count >= 2 ? Mathf.Pow(args[0], args[1]) : args[0];
            case "sqrt": return Mathf.Sqrt(Mathf.Max(0f, args[0]));
            case "hypot": { var s2 = 0f; foreach (var a in args) s2 += a * a; return Mathf.Sqrt(s2); }
            case "log": return args.Count >= 2 ? Mathf.Log(args[0]) / Mathf.Log(args[1]) : Mathf.Log(args[0]);
            case "exp": return Mathf.Exp(args[0]);
            case "abs": return Mathf.Abs(args[0]);
            case "sign": return Mathf.Sign(args[0]) * (args[0] == 0f ? 0f : 1f);
            case "mod": return args.Count >= 2 && args[1] != 0f ? args[0] - args[1] * Mathf.Floor(args[0] / args[1]) : 0f;
            case "rem": return args.Count >= 2 && args[1] != 0f ? args[0] % args[1] : 0f;
            default: // round(<strategy>, value, interval)
            {
                var value = args[0]; var step = args.Count >= 2 && args[1] != 0f ? args[1] : 1f;
                var strategy = raw.Count > args.Count ? raw[0].Trim().ToLowerInvariant() : "nearest";
                var q = value / step;
                var r = strategy switch { "up" => Mathf.Ceil(q), "down" => Mathf.Floor(q), "to-zero" => (float)Math.Truncate(q), _ => Mathf.Round(q) };
                return r * step;
            }
        }
    }

    /// <summary>calc() with both px and % parts, per element and property: resolved against the containing block after layout (PostLayout).</summary>
    internal static readonly Dictionary<VisualElement, List<(string prop, float px, float pct)>> MixedCalc = new();
    /// <summary>Flex containers with align-items: baseline, aligned after layout (PostLayout).</summary>
    internal static readonly HashSet<VisualElement> BaselineRows = new();

    /// <summary>A length that may be a mixed calc(): the percent part goes to the layout now, the px part is added after layout.</summary>
    private static StyleLength LenFor(VisualElement ve, string prop, string v)
    {
        v = v.Trim();
        if (v.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
        {
            Calc(v, out var px, out var pct);
            if (Mathf.Abs(px) > 0.0001f && Mathf.Abs(pct) > 0.0001f)
            {
                if (!MixedCalc.TryGetValue(ve, out var list)) MixedCalc[ve] = list = new List<(string, float, float)>();
                list.RemoveAll(e => e.prop == prop);
                list.Add((prop, px, pct));
                return new Length(pct, LengthUnit.Percent);
            }
        }
        if (MixedCalc.TryGetValue(ve, out var old)) old.RemoveAll(e => e.prop == prop);
        return Len(v);
    }

    private static StyleLength Hairline(StyleLength l)
        => l.keyword == StyleKeyword.Undefined && l.value.unit == LengthUnit.Pixel && l.value.value > 0f && l.value.value < 1f ? new Length(1f, LengthUnit.Pixel) : l;

    public static StyleLength Len(string v)
    {
        v = v.Trim();
        if (v == "auto") return StyleKeyword.Auto;
        if (v == "initial" || v == "unset") return StyleKeyword.Initial;
        if (Func(v) is { } fpx) return new Length(fpx, LengthUnit.Pixel);
        if (v.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
        {
            // px and % cannot be mixed in one length here; whichever part is non-zero wins.
            Calc(v, out var px, out var pct);
            return Mathf.Abs(px) > 0.0001f || Mathf.Abs(pct) < 0.0001f ? new Length(px, LengthUnit.Pixel) : new Length(pct, LengthUnit.Percent);
        }
        if (v.EndsWith("%", StringComparison.Ordinal)) return new Length(Num(v), LengthUnit.Percent);
        return new Length(Num(v), LengthUnit.Pixel);
    }

    /// <summary>calc(): + - * / and parentheses over lengths; px and % accumulate separately.</summary>
    private static void Calc(string v, out float px, out float pct)
    {
        var inner = v.Substring(5, v.Length - 6);
        var pos = 0;
        (px, pct) = CalcExpr(inner, ref pos);
    }

    private static (float px, float pct) CalcExpr(string s, ref int i)
    {
        var (px, pct) = CalcTerm(s, ref i);
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || (s[i] != '+' && s[i] != '-')) break;
            var op = s[i++];
            var (p2, c2) = CalcTerm(s, ref i);
            if (op == '+') { px += p2; pct += c2; } else { px -= p2; pct -= c2; }
        }
        return (px, pct);
    }

    private static (float px, float pct) CalcTerm(string s, ref int i)
    {
        var (px, pct) = CalcFactor(s, ref i);
        while (true)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || (s[i] != '*' && s[i] != '/')) break;
            var op = s[i++];
            var (p2, c2) = CalcFactor(s, ref i);
            // one side must be a bare number: it is whichever carries no unit
            var k = Mathf.Abs(p2) > 0f || Mathf.Abs(c2) > 0f ? p2 + c2 : 0f;
            if (op == '*') { px *= k; pct *= k; }
            else if (Mathf.Abs(k) > 0.00001f) { px /= k; pct /= k; }
        }
        return (px, pct);
    }

    private static (float px, float pct) CalcFactor(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i < s.Length && s[i] == '(')
        {
            i++;
            var r = CalcExpr(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ')') i++;
            return r;
        }
        var start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.' || s[i] == '%' || s[i] == '-')) i++;
        var tok = s.Substring(start, i - start);
        if (i < s.Length && s[i] == '(')
        {
            // a nested function: min(), max(), clamp(), var() already resolved, calc()
            var depth = 0;
            var j = i;
            for (; j < s.Length; j++) { if (s[j] == '(') depth++; else if (s[j] == ')' && --depth == 0) { j++; break; } }
            var call = s.Substring(start, j - start);
            i = j;
            return (Num(call), 0f);
        }
        if (tok.EndsWith("%", StringComparison.Ordinal)) return (0f, Unit(tok));
        return (Unit(tok), 0f);
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && s[i] == ' ') i++;
    }

    private static Length OriginLen(string v)
    {
        return v switch
        {
            "left" or "top" => new Length(0, LengthUnit.Percent),
            "center" => new Length(50, LengthUnit.Percent),
            "right" or "bottom" => new Length(100, LengthUnit.Percent),
            _ => Len(v).value,
        };
    }

    private static float Angle(ReadOnlySpan<char> v)
    {
        v = v.Trim();
        float deg;
        if (Ends(v, "deg")) deg = Num(v.Slice(0, v.Length - 3));
        else if (Ends(v, "grad")) deg = Num(v.Slice(0, v.Length - 4)) * 0.9f;   // before rad: "50grad" ends with "rad" too
        else if (Ends(v, "rad")) deg = Num(v.Slice(0, v.Length - 3)) * Mathf.Rad2Deg;
        else if (Ends(v, "turn")) deg = Num(v.Slice(0, v.Length - 4)) * 360f;
        else deg = Num(v);
        // A whole number of turns is the same rotation as none, and a transition to "the
        // same value" does nothing. Browsers animate rotate(360deg) as a full turn; shave
        // a hundredth of a degree so UI Toolkit sees a change to interpolate.
        if (deg != 0f && Mathf.Abs(deg % 360f) < 0.001f)
            deg -= Mathf.Sign(deg) * 0.01f;
        return deg;
    }

    private static TimeValue Time(string v)
    {
        v = v.Trim();
        if (v.EndsWith("ms", StringComparison.OrdinalIgnoreCase)) return new TimeValue(Num(v.Substring(0, v.Length - 2)), TimeUnit.Millisecond);
        if (v.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return new TimeValue(Num(v.Substring(0, v.Length - 1)), TimeUnit.Second);
        return new TimeValue(Num(v), TimeUnit.Second);
    }

    internal static bool TryEasing(string v, out EasingMode mode)
    {
        switch (v)
        {
            case "ease": mode = EasingMode.Ease; return true;
            case "linear": mode = EasingMode.Linear; return true;
            case "ease-in": mode = EasingMode.EaseIn; return true;
            case "ease-out": mode = EasingMode.EaseOut; return true;
            case "ease-in-out": mode = EasingMode.EaseInOut; return true;
            default: mode = EasingMode.Ease; return false;
        }
    }

    /// <summary>CSS property name to the USS name UI Toolkit's transition system expects.</summary>
    private static string UssName(string css)
    {
        return css switch
        {
            "transform" => "translate",
            "color" => "color",
            "font-weight" or "font-style" => "-unity-font-style",
            "text-align" => "-unity-text-align",
            "background" => "background-color",
            _ => css,
        };
    }

    private static void Sides(string v, out StyleLength top, out StyleLength right, out StyleLength bottom, out StyleLength left)
    {
        var p = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) p = new[] { "0" }; // an unresolved var() or an empty value: zero, not a crash
        top = Len(p[0]);
        right = p.Length > 1 ? Len(p[1]) : top;
        bottom = p.Length > 2 ? Len(p[2]) : top;
        left = p.Length > 3 ? Len(p[3]) : right;
    }

    private static StyleEnum<Align> AlignOf(string v)
    {
        return v switch
        {
            "center" => Align.Center,
            "flex-end" or "end" => Align.FlexEnd,
            "stretch" => Align.Stretch,
            "flex-start" or "start" => Align.FlexStart,
            _ => Align.Auto,
        };
    }

    private static void Border(IStyle s, string v, Action<string>? warn, bool t, bool r, bool b, bool l)
    {
        // border: <width> [style] [color], any order; style is ignored (UI Toolkit draws solid).
        if (v == "none" || v == "0")
        {
            if (t) s.borderTopWidth = 0; if (r) s.borderRightWidth = 0; if (b) s.borderBottomWidth = 0; if (l) s.borderLeftWidth = 0;
            return;
        }
        float? width = null;
        Color? color = null;
        var parts = SplitTopLevel(v);
        foreach (var part in parts)
        {
            if (part is "solid" or "dashed" or "dotted" or "double" or "inset" or "outset" or "groove" or "ridge" or "none" or "hidden") continue; // the style keyword is read by the emitter
            if (IsNumber(part) || part.EndsWith("px", StringComparison.OrdinalIgnoreCase)) width = Num(part);
            else if (TryColor(part, out var c)) color = c;
            else warn?.Invoke($"css: border value \"{part}\" not understood");
        }
        if (width.HasValue)
        {
            if (t) s.borderTopWidth = width.Value; if (r) s.borderRightWidth = width.Value; if (b) s.borderBottomWidth = width.Value; if (l) s.borderLeftWidth = width.Value;
        }
        if (color.HasValue)
        {
            if (t) s.borderTopColor = color.Value; if (r) s.borderRightColor = color.Value; if (b) s.borderBottomColor = color.Value; if (l) s.borderLeftColor = color.Value;
        }
    }

    /// <summary>Split on spaces outside parentheses so rgba(1, 2, 3) stays whole.</summary>
    private static List<string> SplitTopLevel(string v)
    {
        var list = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= v.Length; i++)
        {
            if (i < v.Length)
            {
                if (v[i] == '(') depth++;
                else if (v[i] == ')') depth--;
                if (!(v[i] == ' ' && depth == 0)) continue;
            }
            if (i > start) list.Add(v.Substring(start, i - start));
            start = i + 1;
        }
        return list;
    }

    /// <summary>A transform list the layout engine cannot represent (skew, matrix, 3D): the emitter takes it whole.
    /// Ordinal-ignore-case scans, not a lowercased copy: this runs per element per emit as well as per write.</summary>
    internal static bool NeedsMatrix(string transform)
    {
        return Mentions(transform, "skew") || Mentions(transform, "matrix") || Mentions(transform, "3d")
            || Mentions(transform, "rotatex") || Mentions(transform, "rotatey") || Mentions(transform, "rotatez")
            || Mentions(transform, "perspective");

        static bool Mentions(string t, string what) => t.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// The same walk as <see cref="Functions"/>, in place: name and arguments as slices of the
    /// declaration. Used where it runs per frame; the enumerable stays for the callers that want
    /// strings back.
    /// </summary>
    private ref struct TransformScan
    {
        private readonly ReadOnlySpan<char> _v;
        private int _i;
        private ReadOnlySpan<char> _args;

        public ReadOnlySpan<char> Name { get; private set; }
        /// <summary>Comma-separated arguments, as CSS counts them: an empty list still has one (empty) argument.</summary>
        public int Count { get; private set; }

        public TransformScan(ReadOnlySpan<char> v)
        {
            _v = v;
            _i = 0;
            _args = default;
            Name = default;
            Count = 0;
        }

        public bool Next()
        {
            if (_i >= _v.Length) return false;
            var rest = _v.Slice(_i);
            var open = rest.IndexOf('(');
            if (open < 0) return false;
            // The MATCHING ')': the first one closes calc( in rotate(calc(1deg * 90)), which took
            // the argument one character short and turned 90 into 9 - a wrong number, silently.
            var close = Match(rest, open);
            if (close < 0) return false;
            Name = rest.Slice(0, open).Trim();
            _args = rest.Slice(open + 1, close - open - 1);
            _i += close + 1;
            Count = 1;
            var depth = 0;
            for (var k = 0; k < _args.Length; k++)
            {
                if (_args[k] == '(') depth++;
                else if (_args[k] == ')') depth--;
                else if (_args[k] == ',' && depth == 0) Count++;
            }
            return true;
        }

        /// <summary>The index of the ')' matching the '(' at <paramref name="open"/>, or -1.</summary>
        internal static int Match(ReadOnlySpan<char> s, int open)
        {
            var depth = 0;
            for (var k = open; k < s.Length; k++)
            {
                if (s[k] == '(') depth++;
                else if (s[k] == ')' && --depth == 0) return k;
            }
            return -1;
        }

        public ReadOnlySpan<char> Arg(int n)
        {
            var start = 0;
            var depth = 0;
            for (var k = 0; k <= _args.Length; k++)
            {
                if (k < _args.Length)
                {
                    if (_args[k] == '(') depth++;
                    else if (_args[k] == ')') depth--;
                    if (_args[k] != ',' || depth != 0) continue;
                }
                if (n == 0) return _args.Slice(start, k - start).Trim();
                n--;
                start = k + 1;
            }
            return ReadOnlySpan<char>.Empty;
        }
    }

    internal static IEnumerable<(string name, string[] args)> Functions(string v)
    {
        var i = 0;
        while (i < v.Length)
        {
            var open = v.IndexOf('(', i);
            if (open < 0) yield break;
            var name = v.Substring(i, open - i).Trim().ToLowerInvariant();
            // the MATCHING ')' and top-level commas only: drop-shadow(0 2px 4px rgb(255,0,0))
            var close = -1;
            var depth = 0;
            for (var k = open; k < v.Length; k++)
            {
                if (v[k] == '(') depth++;
                else if (v[k] == ')' && --depth == 0) { close = k; break; }
            }
            if (close < 0) yield break;
            var list = CssParser.SplitTopLevel(v.Substring(open + 1, close - open - 1), ',');
            var args = new string[Math.Max(1, list.Count)];
            args[0] = string.Empty;
            for (var k = 0; k < list.Count; k++) args[k] = list[k].Trim();
            yield return (name, args);
            i = close + 1;
        }
    }

    // ---- colour ----

    /// <summary>The 148 CSS named colours (CSS Color Level 4), plus transparent.</summary>
    private static readonly Dictionary<string, Color> Named = BuildNamed(
        "aliceblue f0f8ff antiquewhite faebd7 aqua 00ffff aquamarine 7fffd4 azure f0ffff beige f5f5dc bisque ffe4c4 black 000000 " +
        "blanchedalmond ffebcd blue 0000ff blueviolet 8a2be2 brown a52a2a burlywood deb887 cadetblue 5f9ea0 chartreuse 7fff00 " +
        "chocolate d2691e coral ff7f50 cornflowerblue 6495ed cornsilk fff8dc crimson dc143c cyan 00ffff darkblue 00008b " +
        "darkcyan 008b8b darkgoldenrod b8860b darkgray a9a9a9 darkgreen 006400 darkgrey a9a9a9 darkkhaki bdb76b darkmagenta 8b008b " +
        "darkolivegreen 556b2f darkorange ff8c00 darkorchid 9932cc darkred 8b0000 darksalmon e9967a darkseagreen 8fbc8f " +
        "darkslateblue 483d8b darkslategray 2f4f4f darkslategrey 2f4f4f darkturquoise 00ced1 darkviolet 9400d3 deeppink ff1493 " +
        "deepskyblue 00bfff dimgray 696969 dimgrey 696969 dodgerblue 1e90ff firebrick b22222 floralwhite fffaf0 forestgreen 228b22 " +
        "fuchsia ff00ff gainsboro dcdcdc ghostwhite f8f8ff gold ffd700 goldenrod daa520 gray 808080 green 008000 greenyellow adff2f " +
        "grey 808080 honeydew f0fff0 hotpink ff69b4 indianred cd5c5c indigo 4b0082 ivory fffff0 khaki f0e68c lavender e6e6fa " +
        "lavenderblush fff0f5 lawngreen 7cfc00 lemonchiffon fffacd lightblue add8e6 lightcoral f08080 lightcyan e0ffff " +
        "lightgoldenrodyellow fafad2 lightgray d3d3d3 lightgreen 90ee90 lightgrey d3d3d3 lightpink ffb6c1 lightsalmon ffa07a " +
        "lightseagreen 20b2aa lightskyblue 87cefa lightslategray 778899 lightslategrey 778899 lightsteelblue b0c4de lightyellow ffffe0 " +
        "lime 00ff00 limegreen 32cd32 linen faf0e6 magenta ff00ff maroon 800000 mediumaquamarine 66cdaa mediumblue 0000cd " +
        "mediumorchid ba55d3 mediumpurple 9370db mediumseagreen 3cb371 mediumslateblue 7b68ee mediumspringgreen 00fa9a " +
        "mediumturquoise 48d1cc mediumvioletred c71585 midnightblue 191970 mintcream f5fffa mistyrose ffe4e1 moccasin ffe4b5 " +
        "navajowhite ffdead navy 000080 oldlace fdf5e6 olive 808000 olivedrab 6b8e23 orange ffa500 orangered ff4500 orchid da70d6 " +
        "palegoldenrod eee8aa palegreen 98fb98 paleturquoise afeeee palevioletred db7093 papayawhip ffefd5 peachpuff ffdab9 " +
        "peru cd853f pink ffc0cb plum dda0dd powderblue b0e0e6 purple 800080 rebeccapurple 663399 red ff0000 rosybrown bc8f8f " +
        "royalblue 4169e1 saddlebrown 8b4513 salmon fa8072 sandybrown f4a460 seagreen 2e8b57 seashell fff5ee sienna a0522d " +
        "silver c0c0c0 skyblue 87ceeb slateblue 6a5acd slategray 708090 slategrey 708090 snow fffafa springgreen 00ff7f " +
        "steelblue 4682b4 tan d2b48c teal 008080 thistle d8bfd8 tomato ff6347 turquoise 40e0d0 violet ee82ee wheat f5deb3 " +
        "white ffffff whitesmoke f5f5f5 yellow ffff00 yellowgreen 9acd32");

    private static Dictionary<string, Color> BuildNamed(string table)
    {
        var d = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase) { ["transparent"] = Color.clear };
        var parts = table.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var n = uint.Parse(parts[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            d[parts[i]] = new Color(((n >> 16) & 0xFF) / 255f, ((n >> 8) & 0xFF) / 255f, (n & 0xFF) / 255f, 1f);
        }
        return d;
    }

    /// <summary>The face a generic family stands for here: the game's mono face for monospace, Barlow for sans, RBBook for serif.</summary>
    public static string? MapGeneric(string family)
    {
        switch (family.Trim().Trim('"', '\'').ToLowerInvariant())
        {
            case "monospace": case "ui-monospace": return "code";
            case "serif": case "ui-serif": return "RBBook SDF";
            case "sans-serif": case "system-ui": case "ui-sans-serif": case "ui-rounded": case "cursive": case "fantasy": return "Barlow";
        }
        return null;
    }

    /// <summary>#rgb #rgba #rrggbb #rrggbbaa rgb() rgba() hsl() and a few names. Managed code only:
    /// ColorUtility.TryParseHtmlString is a native call that fails headless.</summary>
    /// <summary>The value for side index 0..3 (top right bottom left) of a 1-4 value list, as CSS repeats them.</summary>
    internal static string SideOf(string[] parts, int side)
    {
        return parts.Length switch
        {
            1 => parts[0],
            2 => parts[side % 2],
            3 => side == 3 ? parts[1] : parts[side],
            _ => parts[Math.Min(side, parts.Length - 1)],
        };
    }

    /// <summary>
    /// Logical properties as their physical ones for a horizontal, left-to-right page:
    /// inline is x, block is y, start is left/top. A two-value pair (margin-inline: a b)
    /// splits; a shorthand (border-inline: 1px solid) applies whole to both sides.
    /// </summary>
    /// <summary>offset, columns and text-emphasis as their longhands.</summary>
    private static IEnumerable<CssDeclaration> SplitShorthand(CssDeclaration d)
    {
        var v = d.Value.Trim();
        switch (d.Name)
        {
            case "font":
            {
                // [style] [weight] size[/line-height] family-list
                var parts = SplitTopLevel(v);
                var familyStart = -1;
                for (var i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    if (p == "italic" || p == "oblique") { yield return new CssDeclaration("font-style", p, d.Important); continue; }
                    if (p == "bold" || p == "bolder" || p == "lighter" || (IsNumber(p) && Num(p) >= 100)) { yield return new CssDeclaration("font-weight", p, d.Important); continue; }
                    if (p == "normal" || p == "small-caps") continue;
                    if (char.IsDigit(p[0]) || p[0] == '.')
                    {
                        var slash = p.IndexOf('/');
                        yield return new CssDeclaration("font-size", slash > 0 ? p.Substring(0, slash) : p, d.Important);
                        if (slash > 0 && slash + 1 < p.Length) yield return new CssDeclaration("line-height", p.Substring(slash + 1), d.Important);
                        familyStart = i + 1;
                        break;
                    }
                }
                if (familyStart >= 0 && familyStart < parts.Count)
                    yield return new CssDeclaration("font-family", string.Join(" ", parts.GetRange(familyStart, parts.Count - familyStart)), d.Important);
                yield break;
            }
            case "offset":
            {
                // [offset-position]? [offset-path [offset-distance || offset-rotate]?]? [/ offset-anchor]?
                var slash = v.IndexOf('/');
                if (slash >= 0) { yield return new CssDeclaration("offset-anchor", v.Substring(slash + 1).Trim(), d.Important); v = v.Substring(0, slash).Trim(); }
                var tokens = CssParser.SplitTopLevel(v, ' ');
                var position = new List<string>();
                var seenPath = false;
                foreach (var raw in tokens)
                {
                    var t = raw.Trim();
                    if (t.Length == 0) continue;
                    var lower = t.ToLowerInvariant();
                    if (lower.StartsWith("path(", StringComparison.Ordinal) || lower.StartsWith("ray(", StringComparison.Ordinal) || lower.StartsWith("url(", StringComparison.Ordinal) || lower == "none")
                    { yield return new CssDeclaration("offset-path", t, d.Important); seenPath = true; continue; }
                    if (!seenPath) { position.Add(t); continue; }
                    if (lower == "auto" || lower == "reverse" || lower.EndsWith("deg", StringComparison.Ordinal) || lower.EndsWith("turn", StringComparison.Ordinal) || lower.EndsWith("rad", StringComparison.Ordinal))
                        yield return new CssDeclaration("offset-rotate", t, d.Important);
                    else yield return new CssDeclaration("offset-distance", t, d.Important);
                }
                if (position.Count > 0) yield return new CssDeclaration("offset-position", string.Join(" ", position), d.Important);
                break;
            }
            case "columns":
                foreach (var raw in v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (raw == "auto") continue;
                    if (IsNumber(raw) && !raw.Contains('.')) yield return new CssDeclaration("column-count", raw, d.Important);
                    else yield return new CssDeclaration("column-width", raw, d.Important);
                }
                break;
            default: // text-emphasis: <style> || <color>
            {
                var style = new List<string>();
                foreach (var raw in CssParser.SplitTopLevel(v, ' '))
                {
                    var t = raw.Trim();
                    if (t.Length == 0) continue;
                    if (TryColor(t, out _) && !(t is "none")) yield return new CssDeclaration("text-emphasis-color", t, d.Important);
                    else style.Add(t);
                }
                if (style.Count > 0) yield return new CssDeclaration("text-emphasis-style", string.Join(" ", style), d.Important);
                break;
            }
        }
    }

    internal static List<CssDeclaration> Expand(List<CssDeclaration> list)
    {
        List<CssDeclaration>? outp = null;
        for (var i = 0; i < list.Count; i++)
        {
            var d = list[i];
            var n = d.Name;
            string? a = null, b = null;
            var pair = false;
            switch (n)
            {
                case "offset": case "columns": case "text-emphasis":
                    outp ??= new List<CssDeclaration>(list.GetRange(0, i));
                    outp.AddRange(SplitShorthand(d));
                    continue;
                case "corner-start-start-shape": a = "corner-top-left-shape"; break;
                case "corner-start-end-shape": a = "corner-top-right-shape"; break;
                case "corner-end-start-shape": a = "corner-bottom-left-shape"; break;
                case "corner-end-end-shape": a = "corner-bottom-right-shape"; break;
                case "inline-size": a = "width"; break;
                case "block-size": a = "height"; break;
                case "min-inline-size": a = "min-width"; break;
                case "max-inline-size": a = "max-width"; break;
                case "min-block-size": a = "min-height"; break;
                case "max-block-size": a = "max-height"; break;
                case "border-start-start-radius": a = "border-top-left-radius"; break;
                case "border-start-end-radius": a = "border-top-right-radius"; break;
                case "border-end-start-radius": a = "border-bottom-left-radius"; break;
                case "border-end-end-radius": a = "border-bottom-right-radius"; break;
                case "overflow-inline": a = "overflow-x"; break;
                case "overflow-block": a = "overflow-y"; break;
                case "inset-inline-start": a = "left"; break;
                case "inset-inline-end": a = "right"; break;
                case "inset-block-start": a = "top"; break;
                case "inset-block-end": a = "bottom"; break;
                case "inset-inline": a = "left"; b = "right"; pair = true; break;
                case "inset-block": a = "top"; b = "bottom"; pair = true; break;
                case "text-align": case "float": case "clear":
                {
                    var kw = d.Value.Trim().ToLowerInvariant();
                    if (kw is "start" or "inline-start") a = n;
                    else if (kw is "end" or "inline-end") a = n;
                    else break;
                    outp ??= new List<CssDeclaration>(list.GetRange(0, i));
                    outp.Add(new CssDeclaration(n, kw is "start" or "inline-start" ? "left" : "right", d.Important));
                    a = null;
                    continue;
                }
                default:
                    if (n.IndexOf("-inline-start", StringComparison.Ordinal) >= 0) a = n.Replace("-inline-start", "-left");
                    else if (n.IndexOf("-inline-end", StringComparison.Ordinal) >= 0) a = n.Replace("-inline-end", "-right");
                    else if (n.IndexOf("-block-start", StringComparison.Ordinal) >= 0) a = n.Replace("-block-start", "-top");
                    else if (n.IndexOf("-block-end", StringComparison.Ordinal) >= 0) a = n.Replace("-block-end", "-bottom");
                    else if (n.IndexOf("-inline", StringComparison.Ordinal) >= 0) { a = n.Replace("-inline", "-left"); b = n.Replace("-inline", "-right"); pair = n.StartsWith("margin", StringComparison.Ordinal) || n.StartsWith("padding", StringComparison.Ordinal) || n.StartsWith("scroll", StringComparison.Ordinal); }
                    else if (n.IndexOf("-block", StringComparison.Ordinal) >= 0) { a = n.Replace("-block", "-top"); b = n.Replace("-block", "-bottom"); pair = n.StartsWith("margin", StringComparison.Ordinal) || n.StartsWith("padding", StringComparison.Ordinal) || n.StartsWith("scroll", StringComparison.Ordinal); }
                    break;
            }
            if (a == null) { outp?.Add(d); continue; }
            outp ??= new List<CssDeclaration>(list.GetRange(0, i));
            if (b == null) { outp.Add(new CssDeclaration(a, d.Value, d.Important)); continue; }
            var va = d.Value.Trim();
            var vb = va;
            if (pair)
            {
                var parts = va.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && !parts[0].Contains('(')) { va = parts[0]; vb = parts[1]; }
            }
            outp.Add(new CssDeclaration(a, va, d.Important));
            outp.Add(new CssDeclaration(b, vb, d.Important));
        }
        return outp ?? list;
    }

    public static bool TryColor(string v, out Color color)
    {
        v = v.Trim();
        color = Color.white;
        if (v.Length == 0) return false;
        if (Named.TryGetValue(v, out color)) return true;
        if (v.StartsWith("color-mix(", StringComparison.OrdinalIgnoreCase))
        {
            // color-mix(in <space>, A [p%], B [q%]): the percentages normalised as the spec
            // says, mixed in sRGB. ponytail: oklab/oklch/hsl interpolation differs a little in hue paths
            var closeParen = v.LastIndexOf(')');
            if (closeParen < 10) return false;
            var parts = CssParser.SplitTopLevel(v.Substring(10, closeParen - 10), ',');
            if (parts.Count < 3) return false;
            if (!MixPart(parts[1], out var ca, out var pa) || !MixPart(parts[2], out var cb, out var pb)) return false;
            if (float.IsNaN(pa) && float.IsNaN(pb)) { pa = 50f; pb = 50f; }
            else if (float.IsNaN(pa)) pa = 100f - pb;
            else if (float.IsNaN(pb)) pb = 100f - pa;
            var sum = pa + pb;
            if (sum <= 0f) return false;
            var t = pb / sum;
            color = Color.Lerp(ca, cb, t);
            return true;
        }

        if (v[0] == '#')
        {
            var h = v.Substring(1);
            if (h.Length == 3 || h.Length == 4)
            {
                var e = new char[h.Length * 2];
                for (var i = 0; i < h.Length; i++) { e[i * 2] = h[i]; e[i * 2 + 1] = h[i]; }
                h = new string(e);
            }
            if (h.Length != 6 && h.Length != 8) return false;
            if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return false;
            if (h.Length == 6) n = (n << 8) | 0xFF;
            color = new Color(((n >> 24) & 0xFF) / 255f, ((n >> 16) & 0xFF) / 255f, ((n >> 8) & 0xFF) / 255f, (n & 0xFF) / 255f);
            return true;
        }

        var lower = v.ToLowerInvariant();
        if (lower.StartsWith("light-dark(", StringComparison.Ordinal))
        {
            var ld = CssParser.SplitTopLevel(v.Substring(11, Math.Max(0, v.Length - 12)), ',');
            return ld.Count >= 2 && TryColor(ColorSchemeDark ? ld[1] : ld[0], out color);
        }
        if (ColorSpaces.TryParse(lower, v, out color)) return true;
        if (lower.StartsWith("rgb", StringComparison.Ordinal) || lower.StartsWith("hsl", StringComparison.Ordinal))
        {
            var open = v.IndexOf('(');
            var close = v.LastIndexOf(')');
            if (open < 0 || close < open) return false;
            var args = v.Substring(open + 1, close - open - 1).Replace('/', ',').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (args.Length < 3) return false;
            float Channel(string a) => a.EndsWith("%", StringComparison.Ordinal) ? Num(a) / 100f : Num(a) / 255f;
            float Alpha(string a) => a.EndsWith("%", StringComparison.Ordinal) ? Num(a) / 100f : Num(a);
            var alpha = args.Length > 3 ? Alpha(args[3]) : 1f;
            if (lower[0] == 'r')
            {
                color = new Color(Channel(args[0]), Channel(args[1]), Channel(args[2]), alpha);
            }
            else
            {
                var hue = Mathf.Repeat(Num(args[0]) / 360f, 1f);
                var sat = Num(args[1]) / 100f;
                var light = Num(args[2]) / 100f;
                // HSL to RGB via HSV: v = l + s*min(l,1-l); s_v = 2*(1 - l/v)
                var val = light + sat * Mathf.Min(light, 1f - light);
                var sv = val <= 0f ? 0f : 2f * (1f - light / val);
                color = Color.HSVToRGB(hue, sv, val);
                color.a = alpha;
            }
            return true;
        }
        return false;
    }
}


/// <summary>
/// The CSS colour spaces beyond sRGB and HSL, converted to sRGB: hwb(), lab(), lch(),
/// oklab(), oklch(); and the relative colour syntax (`rgb(from red r g b / 50%)`,
/// any of the functions) with the base colour's channels substituted into calc().
/// </summary>
internal static class ColorSpaces
{
    public static bool TryParse(string lower, string v, out Color color)
    {
        color = Color.white;
        var open = lower.IndexOf('(');
        if (open <= 0 || !lower.EndsWith(")", StringComparison.Ordinal)) return false;
        var fn = lower.Substring(0, open).Trim();
        if (fn == "color") return TryColorFunction(v.Substring(open + 1, v.Length - open - 2).Trim(), out color);
        if (fn is not ("rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch")) return false;
        var inner = v.Substring(open + 1, v.Length - open - 2).Trim();
        var relative = inner.StartsWith("from ", StringComparison.OrdinalIgnoreCase);
        if (!relative && fn is "rgb" or "rgba" or "hsl" or "hsla") return false; // the plain forms are parsed by TryColor itself
        var tokens = Tokens(inner);
        Color baseColor = Color.black;
        if (relative)
        {
            if (tokens.Count < 2 || !StyleApplier.TryColor(tokens[1], out baseColor)) return false;
            tokens.RemoveRange(0, 2);
        }
        // channels: up to three, then an optional "/ alpha"
        var ch = new List<string>();
        string? alphaText = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] == "/") { if (i + 1 < tokens.Count) alphaText = tokens[i + 1]; break; }
            ch.Add(tokens[i]);
        }
        if (ch.Count < 3) return false;
        var space = fn.TrimEnd('a') == "rgb" ? "rgb" : fn.TrimEnd('a') == "hsl" ? "hsl" : fn;
        // the base colour's channels in this space, for the relative keywords
        var baseCh = relative ? ChannelsOf(baseColor, space) : new float[3];
        float Chan(string t, int idx, float percentScale)
        {
            if (relative)
            {
                var names = Names(space);
                t = SubstituteKeywords(t, names, baseCh, baseColor.a);
            }
            if (t == "none") return 0f;
            if (t.EndsWith("%", StringComparison.Ordinal)) return StyleApplier.Num(t) / 100f * percentScale;
            return StyleApplier.Num(t);
        }
        float c0, c1, c2;
        switch (space)
        {
            case "rgb": c0 = Chan(ch[0], 0, 255f); c1 = Chan(ch[1], 1, 255f); c2 = Chan(ch[2], 2, 255f); color = new Color(c0 / 255f, c1 / 255f, c2 / 255f); break;
            case "hsl": c0 = Chan(ch[0], 0, 360f); c1 = Chan(ch[1], 1, 100f); c2 = Chan(ch[2], 2, 100f); color = HslToRgb(c0, c1 / 100f, c2 / 100f); break;
            case "hwb": c0 = Chan(ch[0], 0, 360f); c1 = Chan(ch[1], 1, 100f); c2 = Chan(ch[2], 2, 100f); color = HwbToRgb(c0, c1 / 100f, c2 / 100f); break;
            case "lab": c0 = Chan(ch[0], 0, 100f); c1 = Chan(ch[1], 1, 125f); c2 = Chan(ch[2], 2, 125f); color = LabToRgb(c0, c1, c2); break;
            case "lch": c0 = Chan(ch[0], 0, 100f); c1 = Chan(ch[1], 1, 150f); c2 = Chan(ch[2], 2, 360f); color = LabToRgb(c0, c1 * Mathf.Cos(c2 * Mathf.Deg2Rad), c1 * Mathf.Sin(c2 * Mathf.Deg2Rad)); break;
            case "oklab": c0 = Chan(ch[0], 0, 1f); c1 = Chan(ch[1], 1, 0.4f); c2 = Chan(ch[2], 2, 0.4f); color = OklabToRgb(c0, c1, c2); break;
            default: c0 = Chan(ch[0], 0, 1f); c1 = Chan(ch[1], 1, 0.4f); c2 = Chan(ch[2], 2, 360f); color = OklabToRgb(c0, c1 * Mathf.Cos(c2 * Mathf.Deg2Rad), c1 * Mathf.Sin(c2 * Mathf.Deg2Rad)); break;
        }
        var alpha = relative ? baseColor.a : 1f;
        if (alphaText != null)
        {
            var at = relative ? SubstituteKeywords(alphaText, Names(space), baseCh, baseColor.a) : alphaText;
            alpha = at.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(at) / 100f : StyleApplier.Num(at);
        }
        color.a = Mathf.Clamp01(alpha);
        color.r = Mathf.Clamp01(color.r); color.g = Mathf.Clamp01(color.g); color.b = Mathf.Clamp01(color.b);
        return true;
    }

    /// <summary>
    /// color(space c0 c1 c2 [/ alpha]). The wide-gamut RGB spaces are taken as sRGB: a console's
    /// panel is sRGB, so a browser would clamp them to very nearly these numbers anyway. xyz goes
    /// through the same matrix lab() uses.
    /// </summary>
    private static bool TryColorFunction(string inner, out Color color)
    {
        color = Color.white;
        var tokens = Tokens(inner);
        if (tokens.Count < 4) return false;
        var space = tokens[0].ToLowerInvariant();
        float N(int i)
        {
            var t = tokens[i];
            return t == "none" ? 0f : t.EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(t) / 100f : StyleApplier.Num(t);
        }
        float c0 = N(1), c1 = N(2), c2 = N(3);
        switch (space)
        {
            case "srgb" or "display-p3" or "a98-rgb" or "prophoto-rgb" or "rec2020":
                color = new Color(c0, c1, c2); break;
            case "srgb-linear":
                color = new Color(ToSrgb(c0), ToSrgb(c1), ToSrgb(c2)); break;
            case "xyz" or "xyz-d65" or "xyz-d50":
                color = new Color(
                    ToSrgb(3.2404542f * c0 - 1.5371385f * c1 - 0.4985314f * c2),
                    ToSrgb(-0.9692660f * c0 + 1.8760108f * c1 + 0.0415560f * c2),
                    ToSrgb(0.0556434f * c0 - 0.2040259f * c1 + 1.0572252f * c2));
                break;
            default: return false;
        }
        var slash = tokens.IndexOf("/");
        color.a = slash >= 0 && slash + 1 < tokens.Count
            ? Mathf.Clamp01(tokens[slash + 1].EndsWith("%", StringComparison.Ordinal) ? StyleApplier.Num(tokens[slash + 1]) / 100f : StyleApplier.Num(tokens[slash + 1]))
            : 1f;
        color.r = Mathf.Clamp01(color.r); color.g = Mathf.Clamp01(color.g); color.b = Mathf.Clamp01(color.b);
        return true;
    }

    /// <summary>Space-separated tokens at parenthesis depth 0; "/" is its own token.</summary>
    private static List<string> Tokens(string s)
    {
        var list = new List<string>();
        var depth = 0; var start = 0;
        for (var i = 0; i <= s.Length; i++)
        {
            var end = i == s.Length;
            var c = end ? ' ' : s[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            if ((c == ' ' || c == '/' || end) && depth == 0)
            {
                if (i > start) list.Add(s.Substring(start, i - start));
                if (c == '/') list.Add("/");
                start = i + 1;
            }
        }
        return list;
    }

    private static string[] Names(string space) => space switch
    {
        "rgb" => new[] { "r", "g", "b" },
        "hsl" => new[] { "h", "s", "l" },
        "hwb" => new[] { "h", "w", "b" },
        "lab" or "oklab" => new[] { "l", "a", "b" },
        _ => new[] { "l", "c", "h" },
    };

    private static string SubstituteKeywords(string t, string[] names, float[] baseCh, float alpha)
    {
        // whole-word replacement of the channel keywords and "alpha" with the base colour's numbers
        var sb = new StringBuilder();
        var i = 0;
        while (i < t.Length)
        {
            if (char.IsLetter(t[i]))
            {
                var j = i;
                while (j < t.Length && (char.IsLetterOrDigit(t[j]) || t[j] == '-')) j++;
                var word = t.Substring(i, j - i);
                var idx = Array.IndexOf(names, word);
                if (idx >= 0) sb.Append(baseCh[idx].ToString("0.####", CultureInfo.InvariantCulture));
                else if (word == "alpha") sb.Append(alpha.ToString("0.####", CultureInfo.InvariantCulture));
                else sb.Append(word);
                i = j;
                continue;
            }
            sb.Append(t[i]); i++;
        }
        return sb.ToString();
    }

    /// <summary>An sRGB colour's channels in the named space (rgb 0..255, hsl/hwb degrees and 0..100, lab/lch, oklab/oklch).</summary>
    private static float[] ChannelsOf(Color c, string space)
    {
        switch (space)
        {
            case "rgb": return new[] { c.r * 255f, c.g * 255f, c.b * 255f };
            case "hsl": { Color.RGBToHSV(c, out var h, out var sv, out var val); var l = val * (1f - sv * 0.5f); var s = l <= 0f || l >= 1f ? 0f : (val - l) / Mathf.Min(l, 1f - l); return new[] { h * 360f, s * 100f, l * 100f }; }
            case "hwb": { Color.RGBToHSV(c, out var h, out var sv, out var val); return new[] { h * 360f, (1f - sv) * val * 100f, (1f - val) * 100f }; }
            case "lab": return RgbToLab(c);
            case "lch": { var lab = RgbToLab(c); return new[] { lab[0], Mathf.Sqrt(lab[1] * lab[1] + lab[2] * lab[2]), Mathf.Repeat(Mathf.Atan2(lab[2], lab[1]) * Mathf.Rad2Deg, 360f) }; }
            case "oklab": return RgbToOklab(c);
            default: { var ok = RgbToOklab(c); return new[] { ok[0], Mathf.Sqrt(ok[1] * ok[1] + ok[2] * ok[2]), Mathf.Repeat(Mathf.Atan2(ok[2], ok[1]) * Mathf.Rad2Deg, 360f) }; }
        }
    }

    private static Color HslToRgb(float hDeg, float s, float l)
    {
        var val = l + s * Mathf.Min(l, 1f - l);
        var sv = val <= 0f ? 0f : 2f * (1f - l / val);
        return Color.HSVToRGB(Mathf.Repeat(hDeg / 360f, 1f), sv, val);
    }

    private static Color HwbToRgb(float hDeg, float w, float b)
    {
        if (w + b >= 1f) { var g = w / (w + b); return new Color(g, g, g); }
        var rgb = Color.HSVToRGB(Mathf.Repeat(hDeg / 360f, 1f), 1f, 1f);
        return new Color(rgb.r * (1f - w - b) + w, rgb.g * (1f - w - b) + w, rgb.b * (1f - w - b) + w);
    }

    // sRGB <-> linear
    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    private static float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(Mathf.Max(0f, c), 1f / 2.4f) - 0.055f;

    // CIELAB with the D50 white the CSS spec uses, through XYZ with the Bradford-adapted sRGB matrices
    private static Color LabToRgb(float l, float a, float b)
    {
        var fy = (l + 16f) / 116f; var fx = fy + a / 500f; var fz = fy - b / 200f;
        const float k = 24389f / 27f, e = 216f / 24389f;
        float Inv(float f) { var f3 = f * f * f; return f3 > e ? f3 : (116f * f - 16f) / k; }
        var x = Inv(fx) * 0.96422f; var y = l > k * e ? fy * fy * fy : l / k; var z = Inv(fz) * 0.82521f;
        var r = 3.1338561f * x - 1.6168667f * y - 0.4906146f * z;
        var g = -0.9787684f * x + 1.9161415f * y + 0.0334540f * z;
        var bb = 0.0719453f * x - 0.2289914f * y + 1.4052427f * z;
        return new Color(ToSrgb(r), ToSrgb(g), ToSrgb(bb));
    }

    private static float[] RgbToLab(Color c)
    {
        var r = ToLinear(c.r); var g = ToLinear(c.g); var b = ToLinear(c.b);
        var x = (0.4360747f * r + 0.3850649f * g + 0.1430804f * b) / 0.96422f;
        var y = 0.2225045f * r + 0.7168786f * g + 0.0606169f * b;
        var z = (0.0139322f * r + 0.0971045f * g + 0.7141733f * b) / 0.82521f;
        const float k = 24389f / 27f, e = 216f / 24389f;
        float F(float t) => t > e ? Mathf.Pow(t, 1f / 3f) : (k * t + 16f) / 116f;
        var fx = F(x); var fy = F(y); var fz = F(z);
        return new[] { 116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz) };
    }

    private static Color OklabToRgb(float l, float a, float b)
    {
        var l_ = l + 0.3963377774f * a + 0.2158037573f * b;
        var m_ = l - 0.1055613458f * a - 0.0638541728f * b;
        var s_ = l - 0.0894841775f * a - 1.2914855480f * b;
        var l3 = l_ * l_ * l_; var m3 = m_ * m_ * m_; var s3 = s_ * s_ * s_;
        var r = 4.0767416621f * l3 - 3.3077115913f * m3 + 0.2309699292f * s3;
        var g = -1.2684380046f * l3 + 2.6097574011f * m3 - 0.3413193965f * s3;
        var bb = -0.0041960863f * l3 - 0.7034186147f * m3 + 1.7076147010f * s3;
        return new Color(ToSrgb(r), ToSrgb(g), ToSrgb(bb));
    }

    private static float[] RgbToOklab(Color c)
    {
        var r = ToLinear(c.r); var g = ToLinear(c.g); var b = ToLinear(c.b);
        var l = Mathf.Pow(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b, 1f / 3f);
        var m = Mathf.Pow(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b, 1f / 3f);
        var s = Mathf.Pow(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b, 1f / 3f);
        return new[] { 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s, 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s, 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s };
    }
}

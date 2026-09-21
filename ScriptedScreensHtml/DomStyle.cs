using UnityEngine;
using UnityEngine.UIElements;
using UIVisibility = UnityEngine.UIElements.Visibility;
using Y = ScriptedScreensHtml.Yoga;
using static ScriptedScreensHtml.Yoga.YGNodeStyleAPI;
using static ScriptedScreensHtml.Yoga.YGNodeLayoutAPI;

namespace ScriptedScreensHtml;

/// <summary>
/// An element's own style, with UI Toolkit's property names and value types. Layout properties go
/// straight into the element's Yoga node; the rest are kept for <see cref="ResolvedStyle"/> and the
/// emitter. An unset property reads as <c>StyleKeyword.Null</c>, as UI Toolkit's inline style does.
/// </summary>
internal sealed class ElementStyle
{
    private readonly VisualElement _ve;
    private Y.Node N => _ve.Node;

    internal ElementStyle(VisualElement ve) => _ve = ve;

    private static bool Unset(StyleKeyword k) => k is StyleKeyword.Null or StyleKeyword.Initial;

    // ---- lengths that drive the layout ----

    private enum L
    {
        Width, Height, MinWidth, MinHeight, MaxWidth, MaxHeight,
        MarginLeft, MarginTop, MarginRight, MarginBottom,
        PaddingLeft, PaddingTop, PaddingRight, PaddingBottom,
        Left, Top, Right, Bottom, FlexBasis, Count,
    }

    private readonly StyleLength?[] _len = new StyleLength?[(int)L.Count];

    private StyleLength Get(L i) => _len[(int)i] ?? new StyleLength(StyleKeyword.Null);

    private void Set(L i, StyleLength v)
    {
        StyleLength? value = Unset(v.keyword) ? null : v;
        // Writing the value it already has is not a write. The settle loop in Panel.Layout stops
        // when a pass writes nothing, so counting a no-op keeps it running to its pass cap: a
        // caller that re-asserts Left = 0 every pass (GridLayout does) held it open on its own.
        var had = _len[(int)i];
        if (had.HasValue == value.HasValue
            && (!value.HasValue || (had!.Value.keyword == value.Value.keyword
                                    && had.Value.value.unit == value.Value.value.unit
                                    && had.Value.value.value.Equals(value.Value.value.value))))
            return;
        _len[(int)i] = value;
        VisualElement.StyleWrites++;
        var auto = value?.keyword == StyleKeyword.Auto;
        var hasLength = value != null && value.Value.keyword == StyleKeyword.Undefined;
        var length = hasLength ? value!.Value.value : default;
        var pct = hasLength && length.unit == LengthUnit.Percent;
        var px = hasLength ? length.value : float.NaN;
        var n = N;
        switch (i)
        {
            case L.Width: if (pct) YGNodeStyleSetWidthPercent(n, px); else if (hasLength) YGNodeStyleSetWidth(n, px); else YGNodeStyleSetWidthAuto(n); break;
            case L.Height: if (pct) YGNodeStyleSetHeightPercent(n, px); else if (hasLength) YGNodeStyleSetHeight(n, px); else YGNodeStyleSetHeightAuto(n); break;
            case L.MinWidth: if (pct) YGNodeStyleSetMinWidthPercent(n, px); else YGNodeStyleSetMinWidth(n, px); break;
            case L.MinHeight: if (pct) YGNodeStyleSetMinHeightPercent(n, px); else YGNodeStyleSetMinHeight(n, px); break;
            case L.MaxWidth: if (pct) YGNodeStyleSetMaxWidthPercent(n, px); else YGNodeStyleSetMaxWidth(n, px); break;
            case L.MaxHeight: if (pct) YGNodeStyleSetMaxHeightPercent(n, px); else YGNodeStyleSetMaxHeight(n, px); break;
            case L.MarginLeft: Margin(n, Y.YGEdge.Left, pct, auto, hasLength ? px : 0f); break;
            case L.MarginTop: Margin(n, Y.YGEdge.Top, pct, auto, hasLength ? px : 0f); break;
            case L.MarginRight: Margin(n, Y.YGEdge.Right, pct, auto, hasLength ? px : 0f); break;
            case L.MarginBottom: Margin(n, Y.YGEdge.Bottom, pct, auto, hasLength ? px : 0f); break;
            case L.PaddingLeft: Padding(n, Y.YGEdge.Left, pct, hasLength ? px : 0f); break;
            case L.PaddingTop: Padding(n, Y.YGEdge.Top, pct, hasLength ? px : 0f); break;
            case L.PaddingRight: Padding(n, Y.YGEdge.Right, pct, hasLength ? px : 0f); break;
            case L.PaddingBottom: Padding(n, Y.YGEdge.Bottom, pct, hasLength ? px : 0f); break;
            case L.Left: Inset(n, Y.YGEdge.Left, pct, hasLength, px); break;
            case L.Top: Inset(n, Y.YGEdge.Top, pct, hasLength, px); break;
            case L.Right: Inset(n, Y.YGEdge.Right, pct, hasLength, px); break;
            case L.Bottom: Inset(n, Y.YGEdge.Bottom, pct, hasLength, px); break;
            case L.FlexBasis: if (pct) YGNodeStyleSetFlexBasisPercent(n, px); else if (hasLength) YGNodeStyleSetFlexBasis(n, px); else YGNodeStyleSetFlexBasisAuto(n); break;
        }
    }

    private static void Margin(Y.Node n, Y.YGEdge edge, bool pct, bool auto, float px)
    {
        if (auto) YGNodeStyleSetMarginAuto(n, edge);
        else if (pct) YGNodeStyleSetMarginPercent(n, edge, px);
        else YGNodeStyleSetMargin(n, edge, px);
    }

    private static void Padding(Y.Node n, Y.YGEdge edge, bool pct, float px)
    {
        if (pct) YGNodeStyleSetPaddingPercent(n, edge, px);
        else YGNodeStyleSetPadding(n, edge, px);
    }

    private static void Inset(Y.Node n, Y.YGEdge edge, bool pct, bool hasLength, float px)
    {
        if (pct) YGNodeStyleSetPositionPercent(n, edge, px);
        else if (hasLength) YGNodeStyleSetPosition(n, edge, px);
        else YGNodeStyleSetPositionAuto(n, edge);
    }

    public StyleLength width { get => Get(L.Width); set => Set(L.Width, value); }
    public StyleLength height { get => Get(L.Height); set => Set(L.Height, value); }
    public StyleLength minWidth { get => Get(L.MinWidth); set => Set(L.MinWidth, value); }
    public StyleLength minHeight { get => Get(L.MinHeight); set => Set(L.MinHeight, value); }
    public StyleLength maxWidth { get => Get(L.MaxWidth); set => Set(L.MaxWidth, value); }
    public StyleLength maxHeight { get => Get(L.MaxHeight); set => Set(L.MaxHeight, value); }
    public StyleLength marginLeft { get => Get(L.MarginLeft); set => Set(L.MarginLeft, value); }
    public StyleLength marginTop { get => Get(L.MarginTop); set => Set(L.MarginTop, value); }
    public StyleLength marginRight { get => Get(L.MarginRight); set => Set(L.MarginRight, value); }
    public StyleLength marginBottom { get => Get(L.MarginBottom); set => Set(L.MarginBottom, value); }
    public StyleLength paddingLeft { get => Get(L.PaddingLeft); set => Set(L.PaddingLeft, value); }
    public StyleLength paddingTop { get => Get(L.PaddingTop); set => Set(L.PaddingTop, value); }
    public StyleLength paddingRight { get => Get(L.PaddingRight); set => Set(L.PaddingRight, value); }
    public StyleLength paddingBottom { get => Get(L.PaddingBottom); set => Set(L.PaddingBottom, value); }
    public StyleLength left { get => Get(L.Left); set => Set(L.Left, value); }
    public StyleLength top { get => Get(L.Top); set => Set(L.Top, value); }
    public StyleLength right { get => Get(L.Right); set => Set(L.Right, value); }
    public StyleLength bottom { get => Get(L.Bottom); set => Set(L.Bottom, value); }
    public StyleLength flexBasis { get => Get(L.FlexBasis); set => Set(L.FlexBasis, value); }

    // ---- numbers ----

    private StyleFloat? _flexGrow, _flexShrink, _opacity, _borderTopWidth, _borderRightWidth, _borderBottomWidth, _borderLeftWidth;

    private static StyleFloat Or(StyleFloat? v) => v ?? new StyleFloat(StyleKeyword.Null);
    private static StyleFloat? Keep(StyleFloat v) => Unset(v.keyword) ? null : v;
    private static float Num(StyleFloat? v, float initial) => v != null && v.Value.keyword == StyleKeyword.Undefined ? v.Value.value : initial;

    public StyleFloat flexGrow
    {
        get => Or(_flexGrow);
        set { _flexGrow = Keep(value); VisualElement.StyleWrites++; YGNodeStyleSetFlexGrow(N, Num(_flexGrow, 0f)); }
    }

    public StyleFloat flexShrink
    {
        get => Or(_flexShrink);
        set { _flexShrink = Keep(value); VisualElement.StyleWrites++; YGNodeStyleSetFlexShrink(N, Num(_flexShrink, 1f)); }
    }

    public StyleFloat opacity { get => Or(_opacity); set { _opacity = Keep(value); VisualElement.StyleWrites++; } }

    public StyleFloat borderTopWidth { get => Or(_borderTopWidth); set { _borderTopWidth = Keep(value); Border(Y.YGEdge.Top, _borderTopWidth); } }
    public StyleFloat borderRightWidth { get => Or(_borderRightWidth); set { _borderRightWidth = Keep(value); Border(Y.YGEdge.Right, _borderRightWidth); } }
    public StyleFloat borderBottomWidth { get => Or(_borderBottomWidth); set { _borderBottomWidth = Keep(value); Border(Y.YGEdge.Bottom, _borderBottomWidth); } }
    public StyleFloat borderLeftWidth { get => Or(_borderLeftWidth); set { _borderLeftWidth = Keep(value); Border(Y.YGEdge.Left, _borderLeftWidth); } }

    private void Border(Y.YGEdge edge, StyleFloat? v)
    {
        VisualElement.StyleWrites++;
        YGNodeStyleSetBorder(N, edge, Num(v, 0f));
    }

    // ---- keywords that drive the layout ----

    private StyleEnum<DisplayStyle>? _display;
    private StyleEnum<Position>? _position;
    private StyleEnum<FlexDirection>? _flexDirection;
    private StyleEnum<Wrap>? _flexWrap;
    private StyleEnum<Justify>? _justifyContent;
    private StyleEnum<Align>? _alignItems, _alignSelf, _alignContent;
    private StyleEnum<Overflow>? _overflow;

    private static StyleEnum<T> Or<T>(StyleEnum<T>? v) where T : struct, System.IConvertible => v ?? new StyleEnum<T>(StyleKeyword.Null);
    private static StyleEnum<T>? Keep<T>(StyleEnum<T> v) where T : struct, System.IConvertible => Unset(v.keyword) ? null : v;
    private static T Val<T>(StyleEnum<T>? v, T initial) where T : struct, System.IConvertible => v != null && v.Value.keyword == StyleKeyword.Undefined ? v.Value.value : initial;

    public StyleEnum<DisplayStyle> display
    {
        get => Or(_display);
        set
        {
            _display = Keep(value);
            VisualElement.StyleWrites++;
            YGNodeStyleSetDisplay(N, Val(_display, DisplayStyle.Flex) == DisplayStyle.None ? Y.YGDisplay.None : Y.YGDisplay.Flex);
        }
    }

    public StyleEnum<Position> position
    {
        get => Or(_position);
        set
        {
            _position = Keep(value);
            VisualElement.StyleWrites++;
            YGNodeStyleSetPositionType(N, Val(_position, Position.Relative) == Position.Absolute ? Y.YGPositionType.Absolute : Y.YGPositionType.Relative);
        }
    }

    public StyleEnum<FlexDirection> flexDirection
    {
        get => Or(_flexDirection);
        set
        {
            _flexDirection = Keep(value);
            VisualElement.StyleWrites++;
            YGNodeStyleSetFlexDirection(N, Val(_flexDirection, FlexDirection.Column) switch
            {
                FlexDirection.Row => Y.YGFlexDirection.Row,
                FlexDirection.RowReverse => Y.YGFlexDirection.RowReverse,
                FlexDirection.ColumnReverse => Y.YGFlexDirection.ColumnReverse,
                _ => Y.YGFlexDirection.Column,
            });
        }
    }

    public StyleEnum<Wrap> flexWrap
    {
        get => Or(_flexWrap);
        set
        {
            _flexWrap = Keep(value);
            VisualElement.StyleWrites++;
            YGNodeStyleSetFlexWrap(N, Val(_flexWrap, Wrap.NoWrap) switch
            {
                Wrap.Wrap => Y.YGWrap.Wrap,
                Wrap.WrapReverse => Y.YGWrap.WrapReverse,
                _ => Y.YGWrap.NoWrap,
            });
        }
    }

    public StyleEnum<Justify> justifyContent
    {
        get => Or(_justifyContent);
        set
        {
            _justifyContent = Keep(value);
            VisualElement.StyleWrites++;
            YGNodeStyleSetJustifyContent(N, Val(_justifyContent, Justify.FlexStart) switch
            {
                Justify.Center => Y.YGJustify.Center,
                Justify.FlexEnd => Y.YGJustify.FlexEnd,
                Justify.SpaceBetween => Y.YGJustify.SpaceBetween,
                Justify.SpaceAround => Y.YGJustify.SpaceAround,
                _ => Y.YGJustify.FlexStart,
            });
        }
    }

    private static Y.YGAlign ToYoga(Align a) => a switch
    {
        Align.FlexStart => Y.YGAlign.FlexStart,
        Align.Center => Y.YGAlign.Center,
        Align.FlexEnd => Y.YGAlign.FlexEnd,
        Align.Stretch => Y.YGAlign.Stretch,
        _ => Y.YGAlign.Auto,
    };

    public StyleEnum<Align> alignItems
    {
        get => Or(_alignItems);
        set { _alignItems = Keep(value); VisualElement.StyleWrites++; YGNodeStyleSetAlignItems(N, ToYoga(Val(_alignItems, Align.Stretch))); }
    }

    public StyleEnum<Align> alignSelf
    {
        get => Or(_alignSelf);
        set { _alignSelf = Keep(value); VisualElement.StyleWrites++; YGNodeStyleSetAlignSelf(N, ToYoga(Val(_alignSelf, Align.Auto))); }
    }

    public StyleEnum<Align> alignContent
    {
        get => Or(_alignContent);
        set { _alignContent = Keep(value); VisualElement.StyleWrites++; YGNodeStyleSetAlignContent(N, ToYoga(Val(_alignContent, Align.FlexStart))); }
    }

    public StyleEnum<Overflow> overflow
    {
        get => Or(_overflow);
        set
        {
            _overflow = Keep(value);
            VisualElement.StyleWrites++;
            YGNodeStyleSetOverflow(N, Val(_overflow, Overflow.Visible) == Overflow.Hidden ? Y.YGOverflow.Hidden : Y.YGOverflow.Visible);
        }
    }

    // ---- text: inherited, and a change re-measures the labels below ----

    private StyleColor? _color;
    private StyleLength? _fontSize, _letterSpacing, _wordSpacing;
    private StyleEnum<FontStyle>? _fontStyle;
    private StyleEnum<TextAnchor>? _textAlign;
    private StyleEnum<WhiteSpace>? _whiteSpace;
    private StyleEnum<UIVisibility>? _visibility;
    private FaceData? _face;
    private string? _textTransform;

    private void TextChanged()
    {
        VisualElement.StyleWrites++;
        _ve.InheritedChanged();
    }

    public StyleColor color { get => _color ?? new StyleColor(StyleKeyword.Null); set { _color = Unset(value.keyword) ? null : value; VisualElement.StyleWrites++; } }
    public StyleLength fontSize { get => _fontSize ?? new StyleLength(StyleKeyword.Null); set { _fontSize = Unset(value.keyword) ? null : value; TextChanged(); } }
    public StyleLength letterSpacing { get => _letterSpacing ?? new StyleLength(StyleKeyword.Null); set { _letterSpacing = Unset(value.keyword) ? null : value; TextChanged(); } }
    public StyleLength wordSpacing { get => _wordSpacing ?? new StyleLength(StyleKeyword.Null); set { _wordSpacing = Unset(value.keyword) ? null : value; TextChanged(); } }
    public StyleEnum<FontStyle> unityFontStyleAndWeight { get => Or(_fontStyle); set { _fontStyle = Keep(value); TextChanged(); } }
    public StyleEnum<TextAnchor> unityTextAlign { get => Or(_textAlign); set { _textAlign = Keep(value); VisualElement.StyleWrites++; } }
    public StyleEnum<WhiteSpace> whiteSpace { get => Or(_whiteSpace); set { _whiteSpace = Keep(value); TextChanged(); } }
    public StyleEnum<UIVisibility> visibility { get => Or(_visibility); set { _visibility = Keep(value); VisualElement.StyleWrites++; } }

    /// <summary>CSS text-transform (uppercase, lowercase, capitalize, none); null inherits.</summary>
    public string? textTransform { get => _textTransform; set { _textTransform = value; TextChanged(); } }

    /// <summary>The face text is measured with (a TextMeshPro face, copied); null inherits.</summary>
    public FaceData? face
    {
        get => _face;
        set { _face = value; TextChanged(); }
    }

    internal bool TryColor(out Color c) { var ok = _color is { keyword: StyleKeyword.Undefined }; c = ok ? _color!.Value.value : default; return ok; }
    internal StyleLength? FontSizeValue => _fontSize;
    internal StyleLength? LetterSpacingValue => _letterSpacing;
    internal StyleLength? WordSpacingValue => _wordSpacing;
    internal StyleEnum<FontStyle>? FontStyleValue => _fontStyle;
    internal StyleEnum<TextAnchor>? TextAlignValue => _textAlign;
    internal StyleEnum<WhiteSpace>? WhiteSpaceValue => _whiteSpace;
    internal StyleEnum<UIVisibility>? VisibilityValue => _visibility;
    internal FaceData? FaceValue => _face;
    internal string? TextTransformValue => _textTransform;

    // ---- paint and transform: kept for the resolved style and the emitter ----

    public StyleColor backgroundColor { get; set; } = new(StyleKeyword.Null);
    public StyleColor borderTopColor { get; set; } = new(StyleKeyword.Null);
    public StyleColor borderRightColor { get; set; } = new(StyleKeyword.Null);
    public StyleColor borderBottomColor { get; set; } = new(StyleKeyword.Null);
    public StyleColor borderLeftColor { get; set; } = new(StyleKeyword.Null);
    public StyleLength borderTopLeftRadius { get; set; } = new(StyleKeyword.Null);
    public StyleLength borderTopRightRadius { get; set; } = new(StyleKeyword.Null);
    public StyleLength borderBottomRightRadius { get; set; } = new(StyleKeyword.Null);
    public StyleLength borderBottomLeftRadius { get; set; } = new(StyleKeyword.Null);
    public StyleTranslate translate { get; set; } = new(StyleKeyword.Null);
    public StyleRotate rotate { get; set; } = new(StyleKeyword.Null);
    public StyleScale scale { get; set; } = new(StyleKeyword.Null);
    public StyleTransformOrigin transformOrigin { get; set; } = new(StyleKeyword.Null);
    public StyleEnum<TextOverflow> textOverflow { get; set; } = new(StyleKeyword.Null);
    public StyleTextShadow textShadow { get; set; } = new(StyleKeyword.Null);
    public StyleBackground backgroundImage { get; set; } = new(StyleKeyword.Null);
    public StyleBackgroundSize backgroundSize { get; set; } = new(StyleKeyword.Null);
    public StyleBackgroundRepeat backgroundRepeat { get; set; } = new(StyleKeyword.Null);
    public StyleList<TimeValue> transitionDelay { get; set; } = new(StyleKeyword.Null);
    public StyleList<TimeValue> transitionDuration { get; set; } = new(StyleKeyword.Null);
    public StyleList<StylePropertyName> transitionProperty { get; set; } = new(StyleKeyword.Null);
    public StyleList<EasingFunction> transitionTimingFunction { get; set; } = new(StyleKeyword.Null);
}

/// <summary>
/// The values an element ends up with: its own style, inherited text properties, UI Toolkit's
/// initial values otherwise, and box sizes from the layout.
/// </summary>
internal sealed class ResolvedStyle
{
    private readonly VisualElement _ve;

    internal ResolvedStyle(VisualElement ve) => _ve = ve;

    private ElementStyle S => _ve.style;
    private Y.Node N => _ve.Node;

    private static bool Has<T>(StyleEnum<T> v) where T : struct, System.IConvertible => v.keyword == StyleKeyword.Undefined;
    private static bool Has(StyleColor v) => v.keyword == StyleKeyword.Undefined;
    private static bool Has(StyleFloat v) => v.keyword == StyleKeyword.Undefined;
    private static float Px(StyleLength v) => v.keyword == StyleKeyword.Undefined && v.value.unit == LengthUnit.Pixel ? v.value.value : 0f;

    // ---- box ----
    public float width => _ve.layout.width;
    public float height => _ve.layout.height;
    public float left => _ve.layout.x;
    public float top => _ve.layout.y;
    public float marginTop => YGNodeLayoutGetMargin(N, Y.YGEdge.Top);
    public float marginRight => YGNodeLayoutGetMargin(N, Y.YGEdge.Right);
    public float marginBottom => YGNodeLayoutGetMargin(N, Y.YGEdge.Bottom);
    public float marginLeft => YGNodeLayoutGetMargin(N, Y.YGEdge.Left);
    public float paddingTop => YGNodeLayoutGetPadding(N, Y.YGEdge.Top);
    public float paddingRight => YGNodeLayoutGetPadding(N, Y.YGEdge.Right);
    public float paddingBottom => YGNodeLayoutGetPadding(N, Y.YGEdge.Bottom);
    public float paddingLeft => YGNodeLayoutGetPadding(N, Y.YGEdge.Left);
    public float borderTopWidth => YGNodeLayoutGetBorder(N, Y.YGEdge.Top);
    public float borderRightWidth => YGNodeLayoutGetBorder(N, Y.YGEdge.Right);
    public float borderBottomWidth => YGNodeLayoutGetBorder(N, Y.YGEdge.Bottom);
    public float borderLeftWidth => YGNodeLayoutGetBorder(N, Y.YGEdge.Left);

    // ---- flex ----
    public DisplayStyle display => Has(S.display) ? S.display.value : DisplayStyle.Flex;
    public Position position => Has(S.position) ? S.position.value : Position.Relative;
    public FlexDirection flexDirection => Has(S.flexDirection) ? S.flexDirection.value : FlexDirection.Column;
    public Wrap flexWrap => Has(S.flexWrap) ? S.flexWrap.value : Wrap.NoWrap;
    public Justify justifyContent => Has(S.justifyContent) ? S.justifyContent.value : Justify.FlexStart;
    public Align alignItems => Has(S.alignItems) ? S.alignItems.value : Align.Stretch;
    public Align alignSelf => Has(S.alignSelf) ? S.alignSelf.value : Align.Auto;
    public Align alignContent => Has(S.alignContent) ? S.alignContent.value : Align.FlexStart;
    public float flexGrow => Has(S.flexGrow) ? S.flexGrow.value : 0f;
    public float flexShrink => Has(S.flexShrink) ? S.flexShrink.value : 1f;
    public Overflow overflow => Has(S.overflow) ? S.overflow.value : Overflow.Visible;

    // ---- paint ----
    public float opacity => Has(S.opacity) ? S.opacity.value : 1f;
    public Color backgroundColor => Has(S.backgroundColor) ? S.backgroundColor.value : Color.clear;
    public Color borderTopColor => Has(S.borderTopColor) ? S.borderTopColor.value : Color.clear;
    public Color borderRightColor => Has(S.borderRightColor) ? S.borderRightColor.value : Color.clear;
    public Color borderBottomColor => Has(S.borderBottomColor) ? S.borderBottomColor.value : Color.clear;
    public Color borderLeftColor => Has(S.borderLeftColor) ? S.borderLeftColor.value : Color.clear;
    public float borderTopLeftRadius => Radius(S.borderTopLeftRadius);
    public float borderTopRightRadius => Radius(S.borderTopRightRadius);
    public float borderBottomRightRadius => Radius(S.borderBottomRightRadius);
    public float borderBottomLeftRadius => Radius(S.borderBottomLeftRadius);

    private float Radius(StyleLength v)
    {
        if (v.keyword != StyleKeyword.Undefined) return 0f;
        if (v.value.unit != LengthUnit.Percent) return v.value.value;
        var box = _ve.layout;
        return v.value.value / 100f * System.Math.Min(box.width, box.height);
    }

    public Vector3 translate
    {
        get
        {
            var t = S.translate;
            if (t.keyword != StyleKeyword.Undefined) return Vector3.zero;
            var v = t.value;
            var box = _ve.layout;
            var x = v.x.unit == LengthUnit.Percent ? v.x.value / 100f * box.width : v.x.value;
            var y = v.y.unit == LengthUnit.Percent ? v.y.value / 100f * box.height : v.y.value;
            return new Vector3(float.IsNaN(x) ? 0f : x, float.IsNaN(y) ? 0f : y, v.z);
        }
    }

    public Rotate rotate => S.rotate.keyword == StyleKeyword.Undefined ? S.rotate.value : new Rotate(new Angle(0f));
    public Scale scale => S.scale.keyword == StyleKeyword.Undefined ? S.scale.value : new Scale(Vector3.one);

    // ---- text: inherited ----
    public Color color
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.TryColor(out var c)) return c;
            return Color.black;
        }
    }

    public float fontSize => FontSizeOf(_ve);

    private static float FontSizeOf(VisualElement? ve)
    {
        for (var e = ve; e != null; e = e.parent)
        {
            if (e.style.FontSizeValue is not { } v || v.keyword != StyleKeyword.Undefined) continue;
            if (v.value.unit != LengthUnit.Percent) return v.value.value;
            return v.value.value / 100f * FontSizeOf(e.parent);
        }
        return 0f;
    }

    public float letterSpacing
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.LetterSpacingValue is { } v) return Px(v);
            return 0f;
        }
    }

    public float wordSpacing
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.WordSpacingValue is { } v) return Px(v);
            return 0f;
        }
    }

    public FontStyle unityFontStyleAndWeight
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.FontStyleValue is { keyword: StyleKeyword.Undefined } v) return v.value;
            return FontStyle.Normal;
        }
    }

    public TextAnchor unityTextAlign
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.TextAlignValue is { keyword: StyleKeyword.Undefined } v) return v.value;
            return TextAnchor.UpperLeft;
        }
    }

    public WhiteSpace whiteSpace
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.WhiteSpaceValue is { keyword: StyleKeyword.Undefined } v) return v.value;
            return WhiteSpace.Normal;
        }
    }

    public UIVisibility visibility
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.VisibilityValue is { keyword: StyleKeyword.Undefined } v) return v.value;
            return UIVisibility.Visible;
        }
    }

    /// <summary>The inherited text-transform: 1 uppercase, 2 lowercase, 3 capitalize, 0 none.</summary>
    public int textTransform
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.TextTransformValue is { } t)
                    return t switch { "uppercase" => 1, "lowercase" => 2, "capitalize" => 3, _ => 0 };
            return 0;
        }
    }

    /// <summary>The face text is measured with: set here or above, else the page default.</summary>
    public FaceData? face
    {
        get
        {
            for (var e = _ve; e != null; e = e.parent)
                if (e.style.FaceValue is { } f) return f;
            return DefaultFace;
        }
    }

    /// <summary>The face a page without font-family is drawn in (ScriptedScreens' own). Set on the game thread.</summary>
    internal static FaceData? DefaultFace { get; set; }

    public TextOverflow textOverflow => S.textOverflow.keyword == StyleKeyword.Undefined ? S.textOverflow.value : TextOverflow.Clip;
}

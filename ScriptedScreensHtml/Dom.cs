using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Y = ScriptedScreensHtml.Yoga;
using static ScriptedScreensHtml.Yoga.YGNodeAPI;
using static ScriptedScreensHtml.Yoga.YGNodeStyleAPI;
using static ScriptedScreensHtml.Yoga.YGNodeLayoutAPI;

namespace ScriptedScreensHtml;

// Our own layout tree: the part of UI Toolkit's API this mod uses, over Yoga and TextMeasure, in
// plain C#. Nothing here touches the engine, so a page can be laid out on any thread. The style
// values are UI Toolkit's own structs (StyleLength, Length, the enums), which are plain data.

/// <summary>Sent to an element after a layout pass changed its box.</summary>
internal sealed class GeometryChangedEvent
{
    internal static readonly GeometryChangedEvent Instance = new();
}

/// <summary>What a page's root is attached to: the layout size and the pass that lays the tree out.</summary>
internal sealed class Panel
{
    private static readonly Y.Config Config = MakeConfig();

    private static Y.Config MakeConfig()
    {
        var config = Y.YGConfigAPI.YGConfigNew();
        // UI Toolkit's layout: whole pixels at scale 1, and the classic (pre-3.0) Yoga behaviour
        Y.YGConfigAPI.YGConfigSetPointScaleFactor(config, 1f);
        Y.YGConfigAPI.YGConfigSetErrata(config, Y.YGErrata.Classic);
        return config;
    }

    internal static Y.Node NewNode() => YGNodeNewWithConfig(Config);

    public VisualElement Root { get; }
    public float Width { get; private set; }
    public float Height { get; private set; }
    /// <summary>Style writes made by geometry callbacks during the last pass (the old settle counter).</summary>
    public int CallbackWrites { get; private set; }

    public Panel(VisualElement root)
    {
        Root = root;
        root.Attach(this);
    }

    private readonly List<VisualElement> _changed = new();

    /// <summary>
    /// Lays the tree out at this size, then runs the geometry callbacks of every element whose box
    /// changed; a callback that writes styles gets another pass, as UI Toolkit would give it on the
    /// next frame. Up to eight passes, then the layout is taken as it is.
    /// </summary>
    public void Layout(float width, float height)
    {
        Width = width;
        Height = height;
        YGNodeStyleSetWidth(Root.Node, width);
        YGNodeStyleSetHeight(Root.Node, height);
        CallbackWrites = 0;
        for (var pass = 0; pass < 8; pass++)
        {
            YGNodeCalculateLayout(Root.Node, width, height, Y.YGDirection.LTR);
            _changed.Clear();
            Root.CollectChanged(_changed);
            if (_changed.Count == 0) return;
            var before = VisualElement.StyleWrites;
            foreach (var ve in _changed)
                ve.FireGeometryChanged();
            var wrote = VisualElement.StyleWrites - before;
            CallbackWrites += wrote;
            if (wrote == 0 && !Root.AnyDirty()) return;
        }
    }
}

internal class VisualElement
{
    internal readonly Y.Node Node;
    private readonly List<VisualElement> _children = new();
    private List<Action<GeometryChangedEvent>>? _geometry;
    private List<string>? _classes;
    private Rect _shownLayout = new(float.NaN, float.NaN, float.NaN, float.NaN);
    private Panel? _panel;

    /// <summary>Style writes on any element, for the layout pass to see callbacks that changed styles.</summary>
    [ThreadStatic] internal static int StyleWrites;

    public string name { get; set; } = string.Empty;
    public VisualElement? parent { get; private set; }
    public ElementStyle style { get; }
    public ResolvedStyle resolvedStyle { get; }

    public VisualElement()
    {
        Node = Panel.NewNode();
        // UI Toolkit's initial values where Yoga's differ
        YGNodeStyleSetFlexShrink(Node, 1f);
        YGNodeStyleSetFlexDirection(Node, Y.YGFlexDirection.Column);
        YGNodeStyleSetAlignContent(Node, Y.YGAlign.FlexStart);
        style = new ElementStyle(this);
        resolvedStyle = new ResolvedStyle(this);
    }

    // ---- tree ----

    /// <summary>The panel this element is attached to, or null when it is not in a page.</summary>
    public Panel? panel
    {
        get
        {
            var e = this;
            while (e.parent != null) e = e.parent;
            return e._panel;
        }
    }

    internal void Attach(Panel panel) => _panel = panel;

    public int childCount => _children.Count;
    public VisualElement this[int index] => _children[index];
    public VisualElement ElementAt(int index) => _children[index];

    /// <summary>The children; a list, so walking it allocates nothing.</summary>
    public List<VisualElement> Children() => _children;

    public int IndexOf(VisualElement child) => _children.IndexOf(child);

    public void Add(VisualElement child) => Insert(_children.Count, child);

    public void Insert(int index, VisualElement child)
    {
        child.RemoveFromHierarchy();
        index = Math.Max(0, Math.Min(index, _children.Count));
        _children.Insert(index, child);
        child.parent = this;
        YGNodeInsertChild(Node, child.Node, (nuint)index);
        child.InheritedChanged();
    }

    public void Remove(VisualElement child)
    {
        var i = _children.IndexOf(child);
        if (i >= 0) RemoveAt(i);
    }

    public void RemoveAt(int index)
    {
        var child = _children[index];
        _children.RemoveAt(index);
        YGNodeRemoveChild(Node, child.Node);
        child.parent = null;
    }

    public void Clear()
    {
        foreach (var child in _children)
            child.parent = null;
        _children.Clear();
        YGNodeRemoveAllChildren(Node);
    }

    public void RemoveFromHierarchy() => parent?.Remove(this);

    public void BringToFront()
    {
        if (parent == null) return;
        var p = parent;
        p.Remove(this);
        p.Add(this);
    }

    public void SendToBack()
    {
        if (parent == null) return;
        var p = parent;
        p.Remove(this);
        p.Insert(0, this);
    }

    /// <summary>Moves this element to just before <paramref name="sibling"/>.</summary>
    public void PlaceBehind(VisualElement sibling)
    {
        var p = sibling.parent;
        if (p == null || sibling == this) return;
        RemoveFromHierarchy();
        p.Insert(p.IndexOf(sibling), this);
    }

    public void PlaceInFront(VisualElement sibling)
    {
        var p = sibling.parent;
        if (p == null || sibling == this) return;
        RemoveFromHierarchy();
        p.Insert(p.IndexOf(sibling) + 1, this);
    }

    // ---- classes (kept for bookkeeping; the cascade is ours) ----

    public void AddToClassList(string className)
    {
        _classes ??= new List<string>();
        if (!_classes.Contains(className)) _classes.Add(className);
    }

    public void RemoveFromClassList(string className) => _classes?.Remove(className);
    public void ClearClassList() => _classes?.Clear();
    public bool ClassListContains(string className) => _classes != null && _classes.Contains(className);

    // ---- geometry ----

    /// <summary>The box relative to the parent's border box, as Yoga laid it out.</summary>
    public Rect layout => new(YGNodeLayoutGetLeft(Node), YGNodeLayoutGetTop(Node), YGNodeLayoutGetWidth(Node), YGNodeLayoutGetHeight(Node));

    /// <summary>The box in page coordinates, with the translations of this element and its ancestors.</summary>
    public Rect worldBound
    {
        get
        {
            var box = layout;
            var x = box.x;
            var y = box.y;
            var t = resolvedStyle.translate;
            x += t.x;
            y += t.y;
            for (var p = parent; p != null; p = p.parent)
            {
                var pl = p.layout;
                var pt = p.resolvedStyle.translate;
                x += pl.x + pt.x;
                y += pl.y + pt.y;
            }
            return new Rect(x, y, box.width, box.height);
        }
    }

    public Rect localBound => layout;

    /// <summary>The content box in this element's own coordinates.</summary>
    public Rect contentRect
    {
        get
        {
            var l = YGNodeLayoutGetPadding(Node, Y.YGEdge.Left) + YGNodeLayoutGetBorder(Node, Y.YGEdge.Left);
            var t = YGNodeLayoutGetPadding(Node, Y.YGEdge.Top) + YGNodeLayoutGetBorder(Node, Y.YGEdge.Top);
            var r = YGNodeLayoutGetPadding(Node, Y.YGEdge.Right) + YGNodeLayoutGetBorder(Node, Y.YGEdge.Right);
            var b = YGNodeLayoutGetPadding(Node, Y.YGEdge.Bottom) + YGNodeLayoutGetBorder(Node, Y.YGEdge.Bottom);
            var box = layout;
            return new Rect(l, t, Math.Max(0f, box.width - l - r), Math.Max(0f, box.height - t - b));
        }
    }

    public Rect paddingRect
    {
        get
        {
            var l = YGNodeLayoutGetBorder(Node, Y.YGEdge.Left);
            var t = YGNodeLayoutGetBorder(Node, Y.YGEdge.Top);
            var r = YGNodeLayoutGetBorder(Node, Y.YGEdge.Right);
            var b = YGNodeLayoutGetBorder(Node, Y.YGEdge.Bottom);
            var box = layout;
            return new Rect(l, t, Math.Max(0f, box.width - l - r), Math.Max(0f, box.height - t - b));
        }
    }

    /// <summary>Geometry-changed callbacks: the only event this tree sends.</summary>
    public void RegisterCallback<T>(Action<T> callback)
    {
        (_geometry ??= new List<Action<GeometryChangedEvent>>()).Add(e => callback((T)(object)e));
    }

    /// <summary>Nothing is painted here (the vector mod draws); counted so a repaint request still reads as a change.</summary>
    public void MarkDirtyRepaint() => RepaintRequests++;

    internal int RepaintRequests { get; private set; }

    internal void CollectChanged(List<VisualElement> changed)
    {
        if (!YGNodeGetHasNewLayout(Node))
            return;
        YGNodeSetHasNewLayout(Node, false);
        var now = layout;
        if (now != _shownLayout)
        {
            _shownLayout = now;
            if (_geometry != null) changed.Add(this);
        }
        foreach (var child in _children)
            child.CollectChanged(changed);
    }

    internal void FireGeometryChanged()
    {
        if (_geometry == null) return;
        for (var i = 0; i < _geometry.Count; i++)
            _geometry[i](GeometryChangedEvent.Instance);
    }

    internal bool AnyDirty() => YGNodeIsDirty(Node);

    // ---- text ----

    public enum MeasureMode
    {
        Undefined,
        Exactly,
        AtMost,
    }

    /// <summary>The size of <paramref name="text"/> in this element's font, wrapped to the width when its white-space wraps.</summary>
    public Vector2 MeasureTextSize(string text, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var rs = resolvedStyle;
        var face = rs.face;
        // an empty label takes no space, as an empty element does in a browser (UI Toolkit gave it a
        // line: the stray blank row under a table a script had emptied)
        if (face == null || string.IsNullOrEmpty(text))
            return Vector2.zero;
        var measureStyle = new TextMeasure.Style
        {
            Size = rs.fontSize,
            Bold = rs.unityFontStyleAndWeight is FontStyle.Bold or FontStyle.BoldAndItalic,
            LetterSpacing = rs.letterSpacing,
            WordSpacing = rs.wordSpacing,
            Wrap = rs.whiteSpace == WhiteSpace.Normal && widthMode != MeasureMode.Undefined,
            Rich = true,
            Transform = rs.textTransform,
        };
        var r = TextMeasure.Measure(text, face, measureStyle, widthMode == MeasureMode.Undefined ? float.PositiveInfinity : width);
        var w = widthMode == MeasureMode.Exactly ? width : widthMode == MeasureMode.AtMost ? Math.Min(r.Width, width) : r.Width;
        var h = r.Lines * r.LineHeight;
        if (heightMode == MeasureMode.Exactly) h = height;
        else if (heightMode == MeasureMode.AtMost) h = Math.Min(h, height);
        return new Vector2(w, h);
    }

    /// <summary>An inherited text property changed here or above: every label below measures again.</summary>
    internal void InheritedChanged()
    {
        if (this is Label) YGNodeMarkDirty(Node);
        foreach (var child in _children)
            child.InheritedChanged();
    }
}

internal sealed class Label : VisualElement
{
    private string _text = string.Empty;

    public Label() : this(string.Empty) { }

    public Label(string text)
    {
        _text = text ?? string.Empty;
        YGNodeSetMeasureFunc(Node, Measure);
    }

    public string text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (value == _text) return;
            _text = value;
            YGNodeMarkDirty(Node);
        }
    }

    public bool enableRichText { get; set; } = true;

    private Y.YGSize Measure(Y.Node node, float width, Y.MeasureMode widthMode, float height, Y.MeasureMode heightMode)
    {
        var size = MeasureTextSize(_text, width, Mode(widthMode), height, Mode(heightMode));
        return new Y.YGSize { Width = size.x, Height = size.y };
    }

    private static MeasureMode Mode(Y.MeasureMode m) => m switch
    {
        Y.MeasureMode.Exactly => MeasureMode.Exactly,
        Y.MeasureMode.AtMost => MeasureMode.AtMost,
        _ => MeasureMode.Undefined,
    };
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// <c>&lt;canvas&gt;</c>: a VisualElement that replays a recorded 2D-context command list
/// with Painter2D. The page's script records into a flat number array through the JS
/// context shim (one interop call per frame per canvas, not per command) and the element
/// draws it on repaint. Canvas coordinates are the canvas's own width/height, mapped onto
/// the element's rect like an SVG viewBox with preserveAspectRatio="none".
/// </summary>
internal sealed class CanvasElement : VisualElement
{
    // Op codes shared with the JS shim in ScriptHost.Prelude. Keep the two in step.
    public const int OpBeginPath = 0;
    public const int OpMoveTo = 1;        // x y
    public const int OpLineTo = 2;        // x y
    public const int OpQuadTo = 3;        // cx cy x y
    public const int OpCubicTo = 4;       // c1x c1y c2x c2y x y
    public const int OpArc = 5;           // x y r a0 a1 ccw
    public const int OpClosePath = 6;
    public const int OpFill = 7;          // colourIndex alpha
    public const int OpStroke = 8;        // colourIndex alpha lineWidth cap join
    public const int OpFillRect = 9;      // x y w h colourIndex alpha
    public const int OpArcTo = 10;        // x1 y1 x2 y2 r
    public const int OpRect = 11;         // x y w h  (adds a closed subpath)

    /// <summary>Backing store the JS side sets; width/height are the canvas's own pixel grid.</summary>
    public float CanvasWidth = 300f;
    public float CanvasHeight = 150f;

    private float[] _commands = System.Array.Empty<float>();
    private int _count;
    private readonly List<Color> _colours = new();

    public CanvasElement()
    {
        generateVisualContent += Paint;
    }

    /// <summary>Replace the frame's command list. Colours are parsed once per distinct string.</summary>
    public void SetFrame(float[] commands, int count, List<string> colours)
    {
        _commands = commands;
        _count = count;
        _colours.Clear();
        foreach (var c in colours)
            _colours.Add(StyleApplier.TryColor(c, out var col) ? col : Color.magenta);
        MarkDirtyRepaint();
    }

    private void Paint(MeshGenerationContext ctx)
    {
        var p = ctx.painter2D;
        var rect = contentRect;
        if (rect.width <= 0f || rect.height <= 0f || CanvasWidth <= 0f || CanvasHeight <= 0f || _count == 0)
            return;

        var sx = rect.width / CanvasWidth;
        var sy = rect.height / CanvasHeight;
        var ox = rect.x;
        var oy = rect.y;
        Vector2 M(float x, float y) => new(ox + x * sx, oy + y * sy);
        var lineScale = (sx + sy) * 0.5f;

        var c = _commands;
        var i = 0;
        while (i < _count)
        {
            var op = (int)c[i++];
            switch (op)
            {
                case OpBeginPath: p.BeginPath(); break;
                case OpMoveTo: p.MoveTo(M(c[i], c[i + 1])); i += 2; break;
                case OpLineTo: p.LineTo(M(c[i], c[i + 1])); i += 2; break;
                case OpQuadTo: p.QuadraticCurveTo(M(c[i], c[i + 1]), M(c[i + 2], c[i + 3])); i += 4; break;
                case OpCubicTo: p.BezierCurveTo(M(c[i], c[i + 1]), M(c[i + 2], c[i + 3]), M(c[i + 4], c[i + 5])); i += 6; break;
                case OpArc:
                {
                    // Painter2D arcs are circular in element space; use the mean scale.
                    var centre = M(c[i], c[i + 1]);
                    var r = c[i + 2] * lineScale;
                    var a0 = c[i + 3] * Mathf.Rad2Deg;
                    var a1 = c[i + 4] * Mathf.Rad2Deg;
                    var ccw = c[i + 5] > 0.5f;
                    p.Arc(centre, r, a0, a1, ccw ? ArcDirection.CounterClockwise : ArcDirection.Clockwise);
                    i += 6;
                    break;
                }
                case OpArcTo: p.ArcTo(M(c[i], c[i + 1]), M(c[i + 2], c[i + 3]), c[i + 4] * lineScale); i += 5; break;
                case OpClosePath: p.ClosePath(); break;
                case OpRect:
                {
                    var x = c[i]; var y = c[i + 1]; var w = c[i + 2]; var h = c[i + 3];
                    p.MoveTo(M(x, y)); p.LineTo(M(x + w, y)); p.LineTo(M(x + w, y + h)); p.LineTo(M(x, y + h)); p.ClosePath();
                    i += 4;
                    break;
                }
                case OpFill:
                {
                    var col = Colour((int)c[i], c[i + 1]);
                    p.fillColor = col;
                    p.Fill();
                    i += 2;
                    break;
                }
                case OpStroke:
                {
                    p.strokeColor = Colour((int)c[i], c[i + 1]);
                    p.lineWidth = Mathf.Max(0.1f, c[i + 2] * lineScale);
                    p.lineCap = c[i + 3] > 1.5f ? LineCap.Round : LineCap.Butt;
                    p.lineJoin = c[i + 4] > 1.5f ? LineJoin.Round : c[i + 4] > 0.5f ? LineJoin.Bevel : LineJoin.Miter;
                    p.Stroke();
                    i += 5;
                    break;
                }
                case OpFillRect:
                {
                    var x = c[i]; var y = c[i + 1]; var w = c[i + 2]; var h = c[i + 3];
                    p.BeginPath();
                    p.MoveTo(M(x, y)); p.LineTo(M(x + w, y)); p.LineTo(M(x + w, y + h)); p.LineTo(M(x, y + h)); p.ClosePath();
                    p.fillColor = Colour((int)c[i + 4], c[i + 5]);
                    p.Fill();
                    i += 6;
                    break;
                }
                default:
                    return; // unknown op: stop rather than misread the stream
            }
        }
    }

    private Color Colour(int index, float alpha)
    {
        var col = index >= 0 && index < _colours.Count ? _colours[index] : Color.magenta;
        col.a *= Mathf.Clamp01(alpha);
        return col;
    }
}

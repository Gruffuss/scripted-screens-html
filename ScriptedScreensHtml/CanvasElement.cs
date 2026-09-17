using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// <c>&lt;canvas&gt;</c>: an element holding a recorded 2D-context command list for the
/// emitter. The page's script records into a flat number array through the JS
/// context shim (one interop call per frame per canvas, not per command) and the emitter
/// translates it. Canvas coordinates are the canvas's own width/height, mapped onto
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


    /// <summary>Replace the frame's command list. Colours are parsed once per distinct string.</summary>
    /// <summary>The recorded frame as the emitter reads it: opcodes and numbers, plus the string table (colours, gradients, text, fonts, image sources).</summary>
    public float[] Commands => _commands;
    public int Count => _count;
    public readonly List<string> Strings = new();

    public void SetFrame(float[] commands, int count, List<string> colours)
    {
        _commands = commands;
        _count = count;
        _colours.Clear();
        Strings.Clear();
        foreach (var c in colours)
        {
            Strings.Add(c);
            _colours.Add(StyleApplier.TryColor(c, out var col) ? col : Color.magenta);
        }
        MarkDirtyRepaint();
    }

    private Color Colour(int index, float alpha)
    {
        var col = index >= 0 && index < _colours.Count ? _colours[index] : Color.magenta;
        col.a *= Mathf.Clamp01(alpha);
        return col;
    }
}

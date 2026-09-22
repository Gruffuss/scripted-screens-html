using System;
using System.Collections.Generic;
using System.Globalization;

namespace ScriptedScreensHtml;

/// <summary>
/// Where a DOM write lands in the emitted scene: the join between what a page's script writes and
/// what the renderer will draw.
/// </summary>
/// <remarks>
/// A compiled page is a scene whose values are named slots plus Lua that writes them. This decides,
/// for one write, which slot - or refuses, which is the important half.
///
/// The decision has two parts and they fail differently:
///
/// <b>Which key.</b> A CSS property maps to a key on the element's emitted node: <c>height</c> to
/// <c>h</c>, <c>top</c> to <c>y</c>, <c>background</c> to <c>f</c>, <c>textContent</c> to the label's
/// own name. That table is small because the set of properties real pages write is small - measured
/// across this repository, eight style properties and four element properties.
///
/// <b>Whether it is safe.</b> A write is a slot write only if changing it cannot move anything else.
/// For an element that is out of flow - <c>position: absolute</c> or <c>fixed</c> - its box is its
/// own business and the answer is yes. For an element in normal flow, changing its height moves
/// every sibling after it, and only the layout engine knows where they land; that is a recompile,
/// not a value. The caller supplies the answer because only it has the cascade.
///
/// Both halves REPORT rather than guess. A slot that does not exist, or a write that would reflow,
/// comes back as a <see cref="Result"/> carrying the reason - a page that refuses names a line its
/// author can act on, where a page that compiles and quietly draws the wrong thing does not.
/// </remarks>
internal static class DomSlots
{
    /// <summary>Where an element sits and how it is laid out: what the safety question turns on.</summary>
    internal readonly struct Box
    {
        /// <summary>Whether the element is out of normal flow, so its box moves nothing else.</summary>
        public readonly bool OutOfFlow;
        /// <summary>
        /// The absolute scene position of the containing block's top-left corner. The emitter writes
        /// ABSOLUTE coordinates - <c>x = parentPos.x + layout.x</c> - while CSS `top` and `left` are
        /// measured from the containing block, so the two differ by exactly this. Measured on the
        /// game page: legA sits at scene y=141 with a CSS top of 56, inside a player whose own top
        /// is 85. Writing the CSS value straight into the slot would put it 85 units too high.
        /// </summary>
        public readonly double ParentX, ParentY;
        /// <summary>Whether this element also paints a background, so its own `f` slot is the box's and not the text's.</summary>
        public readonly bool HasBackground;
        /// <summary>
        /// Every descendant that emits a named box, and how far its own box sits from this one.
        /// Load bearing, and the least obvious thing in this file: because the scene is in absolute
        /// coordinates, moving an element does NOT move its children - each child's box carries its
        /// own absolute position and would stay exactly where it was. So one DOM write is not one
        /// slot. `legA.style.top` has to move `legA_y` and `bootA_y` together, and `bootA_y` is also
        /// the target of `bootA.style.top`, so two writes compose into one slot.
        /// </summary>
        public readonly IReadOnlyList<(string Id, double Dx, double Dy)> Inside;

        public Box(bool outOfFlow, double parentX, double parentY, bool hasBackground,
                   IReadOnlyList<(string Id, double Dx, double Dy)>? inside = null)
        {
            OutOfFlow = outOfFlow; ParentX = parentX; ParentY = parentY; HasBackground = hasBackground;
            Inside = inside ?? Array.Empty<(string, double, double)>();
        }
    }

    /// <summary>What a write maps to, or why it does not.</summary>
    internal readonly struct Result
    {
        /// <summary>The slot names to write, in order. Empty when <see cref="Problem"/> is set.</summary>
        public readonly string[] Slots;
        /// <summary>
        /// Added to the written value before it reaches each slot, in the same order. Zero for a
        /// size; the containing block's origin for a position, since the scene is in absolute
        /// coordinates and CSS is not.
        /// </summary>
        public readonly double[] Bias;
        /// <summary>Why this write cannot be a slot, or null when it can.</summary>
        public readonly string? Problem;
        /// <summary>True when the write needs the element's transform group to carry its id.</summary>
        public readonly bool NeedsGroup;

        private Result(string[] slots, double[] bias, string? problem, bool needsGroup)
        {
            Slots = slots; Bias = bias; Problem = problem; NeedsGroup = needsGroup;
        }

        public static Result Ok(params string[] slots) => new(slots, new double[slots.Length], null, false);
        public static Result Shifted(string slot, double bias) => new(new[] { slot }, new[] { bias }, null, false);
        public static Result Shifted(string[] slots, double[] bias) => new(slots, bias, null, false);
        public static Result Group(params string[] slots) => new(slots, new double[slots.Length], null, true);
        public static Result No(string why) => new(Array.Empty<string>(), Array.Empty<double>(), why, false);
        public bool Mapped => Problem == null;
    }

    /// <summary>
    /// A CSS property and the key it lands on. Only the ones that reach a node's own line: a
    /// property that changes layout for anything else is not in here by construction.
    /// </summary>
    private static readonly Dictionary<string, string> StyleKeys = new(StringComparer.Ordinal)
    {
        ["width"] = "w",
        ["height"] = "h",
        ["left"] = "x",
        ["top"] = "y",
        ["background"] = "f",
        ["background-color"] = "f",
        ["backgroundColor"] = "f",
        ["color"] = "f",
        ["border-radius"] = "rx",
        ["borderRadius"] = "rx",
        ["font-size"] = "size",
        ["fontSize"] = "size",
        // The border and visibility family. Each maps onto a key the scene already exposes, and the
        // guard below refuses when this element does not emit one - `s` and `sw` only exist on a box
        // that actually draws a stroke, so a page setting a border colour on something that has no
        // border is told rather than writing into nothing.
        ["border-color"] = "s",
        ["borderColor"] = "s",
        ["border-width"] = "sw",
        ["borderWidth"] = "sw",
        ["border-top-left-radius"] = "rx",
        ["borderTopLeftRadius"] = "rx",
        ["border-top-right-radius"] = "rx",
        ["borderTopRightRadius"] = "rx",
        // `visibility: hidden` keeps the box and stops the paint, which is what fill opacity is.
        ["visibility"] = "fo",
        ["opacity"] = "fo",
    };


    /// <summary>Properties carried by the element's wrapping group rather than its own node.</summary>
    private static readonly Dictionary<string, string[]> GroupKeys = new(StringComparer.Ordinal)
    {
        ["opacity"] = new[] { "o" },
        ["transform"] = new[] { "t_0", "t_1" },
    };

    /// <summary>
    /// <c>right</c> and <c>bottom</c> are positions measured from the far edge, so the scene's
    /// <c>x</c>/<c>y</c> depends on the parent's size as well as this value. Expressible, but only
    /// once the compiler emits that arithmetic; refused until then rather than mapped to the wrong
    /// edge, which would read as the element jumping to the other side of its parent.
    /// </summary>
    private static readonly HashSet<string> FarEdge = new(StringComparer.Ordinal)
    {
        "right", "bottom",
    };

    /// <summary>
    /// The slot(s) a write lands on.
    /// </summary>
    /// <param name="id">The element's id. A write to an element without one cannot be mapped.</param>
    /// <param name="property">As <see cref="DomWrites"/> reports it: <c>style.height</c>, <c>textContent</c>.</param>
    /// <param name="box">Where the element sits and how it is laid out, from the cascade and layout.</param>
    /// <param name="available">Slot names the emitted scene actually exposes, from <see cref="SceneSlots"/>.</param>
    internal static Result Map(string id, string property, in Box box, ICollection<string> available)
    {
        if (string.IsNullOrEmpty(id))
            return Result.No("the element has no id, so nothing in the scene is named after it");

        // Text is a slot on the label's own line and carries the element's bare name. It is never an
        // expression: the renderer's T.text takes a value, not a formula, so every textContent write
        // stays a write however pure the value that produced it.
        if (property is "textContent" or "innerText")
            return available.Contains(id)
                ? Result.Ok(id)
                : Result.No($"\"{id}\" draws no text, so there is no slot to write");

        // `innerHTML#3` - one hole of a markup write, already resolved to its slot by MarkupSlots.
        // The slot name IS the answer, so this only has to hand it back; the work happened when the
        // page was laid out with that hole's sentinel in it.
        if (property.StartsWith("innerHTML#", StringComparison.Ordinal))
            return Result.No("a markup hole is bound by the compiler, not mapped here");

        if (property == "innerHTML")
            return Result.No("innerHTML replaces structure, which is a state to enumerate rather than a value to write");

        if (property == "className")
            return Result.No("className is resolved against the stylesheet into the declarations it changes; see Classes()");

        if (!property.StartsWith("style.", StringComparison.Ordinal))
            return Result.No($"`{property}` does not reach the scene");

        var css = Dashed(property.Substring("style.".Length));

        if (FarEdge.Contains(css))
            return Result.No($"`{css}` is measured from the far edge, so its position depends on the parent's size");

        if (GroupKeys.TryGetValue(css, out var groupKeys))
        {
            var slots = new string[groupKeys.Length];
            for (var i = 0; i < groupKeys.Length; i++) slots[i] = id + "_" + groupKeys[i];
            foreach (var slot in slots)
                if (!available.Contains(slot))
                    // The wrapper exists but carries no id, so its numbers have positional names
                    // nothing outside can address. The emitter names only the elements a script
                    // actually drives, because an identified node makes the renderer retain its
                    // whole prop array - so this is a request, not a failure.
                    return Result.Group(slots);
            return Result.Ok(slots);
        }

        if (!StyleKeys.TryGetValue(css, out var key))
            return Result.No($"`{css}` has no equivalent in the scene");

        // The condition that makes the rest sound. In normal flow this element's size and position
        // decide where its siblings go, and only the layout engine knows that.
        if (!box.OutOfFlow && (key is "w" or "h" or "x" or "y"))
            return Result.No($"\"{id}\" is in normal flow, so changing its {css} moves its siblings");

        // An element that paints a background emits its box and its text as two lines carrying the
        // same id, and the first one claims `<id>_f`. That is the box's fill, so a `color` write
        // routed to it would repaint the background instead of the text - silently, and only on the
        // elements that have both. Refused until the emitter distinguishes them.
        if (css == "color" && box.HasBackground)
            return Result.No($"\"{id}\" paints a background, so its `f` slot is the box's fill rather than the text's");

        var name = id + "_" + key;
        if (!available.Contains(name))
            return Result.No($"\"{id}\" emits no {key}, so `{css}` has no slot");

        // The scene is in absolute coordinates and CSS is not, so a position carries its containing
        // block's origin - and everything inside this element has to move with it, because each
        // descendant's box carries its own absolute position and would otherwise stay put.
        if (key is "x" or "y")
        {
            var vertical = key == "y";
            var origin = vertical ? box.ParentY : box.ParentX;
            var slots = new List<string> { name };
            var bias = new List<double> { origin };
            foreach (var child in box.Inside)
            {
                var childSlot = child.Id + "_" + key;
                if (!available.Contains(childSlot)) continue;
                slots.Add(childSlot);
                bias.Add(origin + (vertical ? child.Dy : child.Dx));
            }
            return Result.Shifted(slots.ToArray(), bias.ToArray());
        }
        return Result.Ok(name);
    }

    /// <summary>
    /// A <c>className</c> write, as the declarations switching that class actually changes. It is a
    /// slot write when every one of those declarations is itself slottable, and a structural change
    /// when any is not - which is a question about the stylesheet, not about the script.
    /// </summary>
    /// <param name="changed">
    /// Per element id, the CSS properties whose computed value differs between the two class states.
    /// The caller computes this from the cascade; this decides whether the difference is expressible.
    /// </param>
    internal static Result Classes(IReadOnlyDictionary<string, IReadOnlyCollection<string>> changed,
                                   Func<string, Box> boxOf, ICollection<string> available)
    {
        var slots = new List<string>();
        var group = false;
        foreach (var pair in changed)
            foreach (var property in pair.Value)
            {
                var mapped = Map(pair.Key, "style." + property, boxOf(pair.Key), available);
                if (!mapped.Mapped)
                    return Result.No($"the class changes `{property}` on \"{pair.Key}\", which is not a value: {mapped.Problem}");
                group |= mapped.NeedsGroup;
                slots.AddRange(mapped.Slots);
            }
        return group ? Result.Group(slots.ToArray()) : Result.Ok(slots.ToArray());
    }

    /// <summary>`backgroundColor` as `background-color`: a script writes one spelling, CSS the other.</summary>
    private static string Dashed(string property)
    {
        var needs = false;
        foreach (var c in property) if (c >= 'A' && c <= 'Z') { needs = true; break; }
        if (!needs) return property;

        var sb = new System.Text.StringBuilder(property.Length + 2);
        foreach (var c in property)
        {
            if (c >= 'A' && c <= 'Z') sb.Append('-').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>A number as the scene writes it, for a Lua chip building a payload.</summary>
    internal static string Number(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e9
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.###", CultureInfo.InvariantCulture);
}

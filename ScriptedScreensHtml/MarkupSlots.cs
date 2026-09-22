using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// Where each hole in a page's markup lands in the emitted scene.
/// </summary>
/// <remarks>
/// <see cref="Markup"/> reduces an <c>innerHTML</c> assignment to fixed structure plus holes. This is
/// the other half: which scene slot each hole writes, so the compiled page can set a value instead of
/// rebuilding a document.
///
/// <b>The method is to put the question to the machinery that already answers it.</b> Rather than
/// walk the DOM and infer what kind of property each hole sits in - a length, a colour, a label, an
/// attribute, half of a shorthand - the skeleton is put into the page with a distinctive NUMBER in
/// each hole, the page is laid out and emitted exactly as it normally is, and the scene is split into
/// slots exactly as it normally is. Whichever slot comes back holding 987653 is the slot hole 3
/// writes. No inference, and no second implementation of "what is a slot" to drift from the first.
///
/// That is also why the sentinel is a number rather than a marker: <c>width:987653px</c> parses and
/// lays out, where <c>width:H3px</c> does not - and a hole that breaks the layout never
/// reaches the scene to be found.
///
/// The page is put back exactly as it was afterwards. Compiling must leave nothing behind: whatever
/// the console draws next is emitted from this same tree.
/// </remarks>
internal static class MarkupSlots
{
    /// <summary>What one hole writes.</summary>
    internal readonly struct Landing
    {
        /// <summary>The slot name in the emitted scene.</summary>
        public readonly string Slot;
        /// <summary>Whether it arrived as a number or as text, which decides how the chip writes it.</summary>
        public readonly bool IsNumber;
        /// <summary>
        /// For a text slot: what surrounds the hole in that slot's value.
        /// </summary>
        /// <remarks>
        /// A log line is <c>"pressure " + v + " kPa"</c>, so the label's text is one slot holding
        /// three pieces. Writing the hole's value alone would drop the words either side of it, so
        /// the constant parts travel with the landing and the chip rebuilds the string.
        /// </remarks>
        public readonly string? Before;
        public readonly string? After;

        public Landing(string slot, bool isNumber, string? before = null, string? after = null)
        {
            Slot = slot; IsNumber = isNumber; Before = before; After = after;
        }
    }

    /// <summary>
    /// Resolves every hole to a slot, for one choice of shape.
    /// </summary>
    /// <param name="id">The element the page assigns <c>innerHTML</c> on.</param>
    /// <param name="taken">Which side of each choice this shape takes; null for every first side.</param>
    /// <returns>Hole index to where it lands. A hole missing from the map did not reach the scene.</returns>
    internal static Dictionary<int, Landing>? Resolve(string id, Markup markup, HtmlRenderer.Result built,
                                                      Panel panel, Vector2 size,
                                                      IReadOnlyList<bool>? taken = null, int rows = 1)
    {
        if (!built.ById.TryGetValue(id, out var element) || element == null) return null;
        if (!built.NodeOf.TryGetValue(element, out var node)) return null;

        // Kept so the page can be put back byte for byte. `keepIds` because the ids are what the
        // scene names its slots after, and restoring without them would rename every one.
        var original = HtmlRenderer.ToHtml(node, outer: false, keepIds: true);

        try
        {
            var skeleton = markup.Skeleton(taken, rows);
            if (!Replace(element, node, skeleton, built))
            {
                ScriptedScreensHtmlPlugin.Log?.LogInfo(
                    $"html: could not put the skeleton into \"{id}\" - {HtmlRenderer.LastMorphMiss ?? "shape differs"}");
                return null;
            }
            // Everything the skeleton introduced needs a name the scene can use; the markup's own
            // elements rarely carry ids, and an unnamed line gets a positional slot name that
            // nothing outside can address.
            HtmlRenderer.Nameable(element, built, id);
            panel.Layout(size.x, size.y);

            // As on a worker: font questions are deferred rather than asked of TextMeshPro, whose
            // lookups are native ECalls and throw outside the player. The boxes the emitter reads
            // have to be captured from the freshly laid-out tree first, or it emits the previous
            // shape's geometry for the skeleton's elements.
            OffThread.Capture(built.Root, built, OffThread.Boxes, new List<VisualElement>());
            var wasActive = OffThread.Active;
            OffThread.Active = true;
            VectorEmitter.Output output;
            try { output = VectorEmitter.Emit(built, built.Root, size.x, size.y); }
            finally { OffThread.Active = wasActive; }
            var table = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
            SceneSlots.Split(output.Chars, output.Length, table);

            var found = new Dictionary<int, Landing>();
            foreach (var pair in table)
            {
                if (pair.Value.IsNumber)
                {
                    var hole = Markup.HoleOf(pair.Value.Number);
                    if (hole >= 0) found[hole] = new Landing(pair.Key, isNumber: true);
                    continue;
                }
                if (pair.Value.Text is not { Length: > 0 } text) continue;
                foreach (var (hole, before, after) in InText(text))
                    found[hole] = new Landing(pair.Key, isNumber: false, before, after);
            }
            return found;
        }
        finally
        {
            // Put back, always. A compile that throws half way must not leave the console drawing
            // six-digit numbers where its readings should be.
            Replace(element, node, original, built);
            HtmlRenderer.Nameable(element, built, id);
            panel.Layout(size.x, size.y);
        }
    }

    /// <summary>
    /// Resolves every hole, across every shape the markup can take.
    /// </summary>
    /// <remarks>
    /// One shape covers only the holes on its own path through the choices - measured on AtmoDark,
    /// 20 of 328 - so this walks the shapes the markup says are worth emitting and unions what each
    /// one lands. A hole appears in exactly one branch, so the union is complete rather than a
    /// sample, and the shape count is tens rather than two to the power of the choice count.
    /// </remarks>
    internal static Dictionary<int, Landing> ResolveAll(string id, Markup markup, HtmlRenderer.Result built,
                                                        Panel panel, Vector2 size, int rows = 1)
    {
        var all = new Dictionary<int, Landing>();
        foreach (var shape in markup.Shapes())
        {
            var landed = Resolve(id, markup, built, panel, size, shape, rows);
            if (landed == null) continue;
            // First shape to land a hole wins. A hole reached by two shapes is the same hole in the
            // same place; taking the later one would only churn.
            foreach (var pair in landed) if (!all.ContainsKey(pair.Key)) all[pair.Key] = pair.Value;
        }
        return all;
    }

    /// <summary>
    /// Swaps an element's contents, in place when the shape allows and by rebuilding when it does not.
    /// </summary>
    private static bool Replace(VisualElement element, HtmlNode node, string html, HtmlRenderer.Result built)
    {
        if (HtmlRenderer.Morph(element, node, html, built, null)) return true;
        try
        {
            // A different shape - a choice taking its other side - so the subtree is rebuilt. The
            // children go first or the fragment is appended to what is already there.
            element.Clear();
            node.Children.Clear();
            HtmlRenderer.AppendFragment(element, node, html, built);
            return true;
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: rebuilding a markup shape threw - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Every sentinel in a piece of text, with what sits either side of it.
    /// </summary>
    /// <remarks>
    /// Only ONE hole per text slot is reported with its surroundings, and that is deliberate: two
    /// holes in one label would need the chip to rebuild a string from several values, which is
    /// string building on the per-frame path - the exact cost compiling exists to remove. A second
    /// hole in the same slot is dropped and the caller reports it, so the page is refused rather
    /// than drawn with one of its two numbers missing.
    /// </remarks>
    private static IEnumerable<(int Hole, string Before, string After)> InText(string text)
    {
        var at = 0;
        while (at < text.Length)
        {
            var start = -1;
            for (var i = at; i + 5 < text.Length + 1; i++)
            {
                if (!char.IsDigit(text[i])) continue;
                var end = i;
                while (end < text.Length && char.IsDigit(text[end])) end++;
                if (end - i >= 6 && int.TryParse(text.AsSpan(i, end - i), out var value)
                    && Markup.HoleOf(value) is var hole && hole >= 0)
                {
                    start = i;
                    yield return (hole, text.Substring(0, i), text.Substring(end));
                    at = end;
                    break;
                }
                i = end - 1;
            }
            if (start < 0) yield break;
        }
    }
}

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
                                                      IReadOnlyList<bool>? taken = null, int rows = 1,
                                                      bool colours = false)
    {
        if (!built.ById.TryGetValue(id, out var element) || element == null) return null;
        if (!built.NodeOf.TryGetValue(element, out var node)) return null;

        // Kept so the page can be put back byte for byte. `keepIds` because the ids are what the
        // scene names its slots after, and restoring without them would rename every one.
        var original = HtmlRenderer.ToHtml(node, outer: false, keepIds: true);

        try
        {
            var skeleton = markup.Skeleton(taken, rows, colours);
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
            // Boxes is null until a surface has run once. Compiling before that would emit every
            // element at the origin, so the holes would map to slots on shapes nobody can see.
            if (OffThread.Boxes is not { } boxes) return null;
            OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());
            var wasActive = OffThread.Active;
            OffThread.Active = true;
            VectorEmitter.Output output;
            // Isolated: this emit must not overwrite the buffer the surface's own emit returned and
            // is about to send. See VectorEmitter.Isolated for the measurement.
            try { output = VectorEmitter.Isolated(() => VectorEmitter.Emit(built, built.Root, size.x, size.y)); }
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
                // A colour arrives as its own slot value, whole - there is nothing around it to
                // keep, and a colour is never half of a label.
                var colour = Markup.ColourHoleOf(text);
                if (colour >= 0) { found[colour] = new Landing(pair.Key, isNumber: false); continue; }
                foreach (var (hole, before, after) in InText(text))
                    found[hole] = new Landing(pair.Key, isNumber: false, before, after);
            }
            return found;
        }
        finally
        {
            // Put back, always. A compile that throws half way must not leave the console drawing
            // six-digit numbers where its readings should be. Rebuilt rather than morphed: see the
            // `inPlace` note on Replace - the fast path restores structure and not values.
            Replace(element, node, original, built, inPlace: false);
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

        // The page as it was BEFORE any probing, kept here rather than only per pass.
        //
        // This is the difference between a bug that heals and one that compounds. Each Resolve below
        // saves its own "original" and puts it back, but it saves it from the LIVE tree - so if one
        // pass fails to restore, the next pass saves the SKELETON as its original and faithfully
        // restores that. Ten passes run here (five shapes, two sentinel flavours), so one slip
        // poisons every pass after it and the page keeps the skeleton for good.
        //
        // Seen in game on 2026-09-22: AtmoDark, AtmoLight and AtmoApple all drew `#0F0085` and
        // `987653` where their readings belong, with their chrome gone, and NOTHING in the log said
        // so. Worse, the pages that show it do not even compile - so a compile ATTEMPT that was
        // going to be abandoned anyway is what destroyed the page it failed to compile.
        if (!built.ById.TryGetValue(id, out var element) || element == null) return all;
        if (!built.NodeOf.TryGetValue(element, out var node)) return all;
        var pristine = HtmlRenderer.ToHtml(node, outer: false, keepIds: true);

        try
        {
            // Each shape twice: a number serves a length and a label, a colour serves the positions a
            // number is not valid in. Neither flavour alone reaches half the holes on a real page.
            foreach (var shape in markup.Shapes())
            foreach (var colours in new[] { false, true })
            {
                var landed = Resolve(id, markup, built, panel, size, shape, rows, colours);
                if (landed == null) continue;
                // First shape to land a hole wins. A hole reached by two shapes is the same hole in the
                // same place; taking the later one would only churn.
                foreach (var pair in landed) if (!all.ContainsKey(pair.Key)) all[pair.Key] = pair.Value;
            }
            return all;
        }
        finally
        {
            Restore(id, element, node, pristine, built, panel, size);
            Forget(built, panel, size);
        }
    }

    /// <summary>
    /// Clears the box snapshot and measures the restored page fresh.
    /// </summary>
    /// <remarks>
    /// One of TWO things probing leaves behind, and the one that only showed offline. The other -
    /// the probe's emit overwriting the shared output buffer a caller was still holding - is what
    /// the game actually drew, and is closed by running the probe's emit inside
    /// <see cref="VectorEmitter.Isolated"/>. This one is measured, not derived: the bench emitted
    /// 17-22 sentinels after probing until this clear-and-remeasure, and reverting it brings them
    /// back. Capture prunes entries it did not see, so "the cache only grows" is not the reason;
    /// why the incremental walk kept the skeleton's values for elements it did see is not pinned.
    /// Kept because it is cheap - one re-measure per compile attempt, once per page - and proven.
    /// </remarks>
    private static void Forget(HtmlRenderer.Result built, Panel panel, Vector2 size)
    {
        if (OffThread.Boxes is not { } boxes) return;
        boxes.Clear();
        panel.Layout(size.x, size.y);
        OffThread.Capture(built.Root, built, boxes, new List<VisualElement>());
    }

    /// <summary>
    /// Puts the page back exactly as it was, and says so out loud when it cannot.
    /// </summary>
    /// <remarks>
    /// The check is the point. Probing mutates the live page, so "it was restored" is a claim the
    /// code has to verify rather than assume - and the failure it guards against is invisible: a
    /// console quietly drawing sentinels where its numbers should be, with no warning anywhere.
    /// </remarks>
    private static void Restore(string id, VisualElement element, HtmlNode node, string pristine,
                                HtmlRenderer.Result built, Panel panel, Vector2 size)
    {
        var now = HtmlRenderer.ToHtml(node, outer: false, keepIds: true);
        if (Environment.GetEnvironmentVariable("PROBE_RESTORE") != null)
            Console.Error.WriteLine($"[restore] {id}: same={string.Equals(now, pristine, StringComparison.Ordinal)} "
                                    + $"now={now.Length} pristine={pristine.Length} "
                                    + $"nowHasSentinel={now.Contains("#0F0", StringComparison.Ordinal) || now.Contains("98765", StringComparison.Ordinal)}");
        if (string.Equals(now, pristine, StringComparison.Ordinal))
            return;

        Replace(element, node, pristine, built, inPlace: false);
        HtmlRenderer.Nameable(element, built, id);
        panel.Layout(size.x, size.y);

        if (!string.Equals(HtmlRenderer.ToHtml(node, outer: false, keepIds: true), pristine, StringComparison.Ordinal))
            ScriptedScreensHtmlPlugin.Log?.LogWarning(
                $"html: \"{id}\" could not be put back after probing it for slots, so the page may be "
                + "drawing placeholder values. This is a compiler bug, not a fault in the page.");
    }

    /// <summary>
    /// Swaps an element's contents, in place when the shape allows and by rebuilding when it does not.
    /// </summary>
    /// <param name="inPlace">
    /// Whether the fast path may be used. False when PUTTING THE PAGE BACK, and that is not a
    /// preference: <c>Morph</c> reports success having updated the structure while leaving a label's
    /// text and a shape's resolved colour as the skeleton left them. The static text of a restored
    /// page came back and every scripted value stayed a sentinel - `#0F0000` where the heading
    /// belongs - which is how three consoles ended up drawing placeholders on 2026-09-22. Rebuilding
    /// is slower and this runs once per probe, not per frame.
    /// </param>
    private static bool Replace(VisualElement element, HtmlNode node, string html, HtmlRenderer.Result built,
                                bool inPlace = true)
    {
        if (inPlace && HtmlRenderer.Morph(element, node, html, built, null)) return true;
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

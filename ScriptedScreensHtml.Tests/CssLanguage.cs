using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// CSS coverage measured against the LANGUAGE, not against the pages in this folder.
/// </summary>
/// <remarks>
/// Language.cs does this for JavaScript, where the denominator is exact: the parser has a closed set
/// of AST node types, so nothing is sampled and nothing is guessed. CSS has no such set, so the
/// denominator here is written out - several hundred properties, the selector forms, the at-rules and
/// the value syntaxes - and the point of writing it out is that it is the SPECIFICATION's list rather
/// than the corpus's. A page written tomorrow is what this has to answer for.
///
/// The verdict is never read off the source. A property counts as covered when adding it to a page
/// CHANGES THE EMITTED SCENE, which is the only definition matching the promise: a property the
/// cascade parses, accepts and then ignores draws exactly what its absence draws, and an inventory
/// that counted it would be describing a warning list, not a renderer. A value counts as covered when
/// it emits the SAME scene as a reference spelling of the same thing, which is stricter - "the scene
/// changed" would pass a length that failed to parse and became zero.
///
/// That is why this probe compiles the Unity half of the pipeline (see the csproj): the real cascade
/// and the real emitter are the only things that can answer either question.
///
/// A row whose fixture cannot reveal its property reads as missing, so the number errs low. That is
/// the direction to err in for a figure whose whole purpose is not to flatter.
/// </remarks>
internal static class CssLanguage
{
    internal static void Run(string[] args)
    {
        Setup();
        var only = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.Ordinal))?.Substring(7);
        if (only == null || only == "properties") { Properties(); Console.WriteLine(); }
        if (only == null || only == "selectors") { Selectors(); Console.WriteLine(); }
        if (only == null || only == "atrules") { AtRules(); Console.WriteLine(); }
        if (only == null || only == "values") Values();
    }

    // ---- the instrument ------------------------------------------------------------------

    private static void Setup()
    {
        ResolvedStyle.DefaultFace = FontLibrary.Default();
        HtmlRenderer.SurfaceAspect = 1f;
        OffThread.MainThreadId = Environment.CurrentManagedThreadId;
        OffThread.Boxes = new Dictionary<VisualElement, OffThread.Box>();
        OffThread.Job = OffThread.Globals.Take();
    }

    /// <summary>
    /// A page laid out and emitted exactly as the surface does it, down to the string the vector mod
    /// would receive. Warnings come back too: a property can be missing in two different ways -
    /// refused loudly, or accepted and dropped - and only the scene tells those apart.
    /// </summary>
    private static string Scene(string html, out List<string> warnings)
    {
        var built = HtmlRenderer.Build(html, FontLibrary.Default());
        // The cascade's warnings AND the emitter's. They are kept in different places and only the
        // cascade's were being read, so every property the EMITTER refuses - `filter: blur()`, a
        // clip-path it cannot draw, a text-decoration on a wrapping label - was scored as "accepted,
        // draws the same". That is the worst bucket in this report, and three of its entries were
        // really loud refusals filed under it.
        warnings = built.Warnings;
        HtmlRenderer.NameDrivenGroups(built);
        var root = built.Root;
        var panel = new Panel(root);
        foreach (var grid in built.Grids)
            if (built.LayoutAttached.Add(grid)) GridLayout.Attach(grid, built);
        PostLayout.Attach(built);

        var boxes = new Dictionary<VisualElement, OffThread.Box>();
        OffThread.Boxes = boxes;
        var size = new Vector2(built.ViewportWidth, built.ViewportWidth);
        panel.Layout(size.x, size.y);
        // Run the animations. Without this no KeyframeRunner is ever constructed, so every
        // `animation-*` longhand reads as missing - eight rows - while the code implementing them is
        // present and correct. The two that "passed" did so because naming an animation wraps the
        // element in an identity group, which carries no motion at all.
        foreach (var (element, spec) in built.Animations)
        {
            if (!built.AnimationAttached.Add(element)) continue;
            if (!built.Keyframes.TryGetValue(spec.Name, out var frames)) continue;
            // An infinite opacity/transform loop is compiled into the scene as expressions rather
            // than driven, which is the surface's own rule and has to be the probe's too.
            if (float.IsPositiveInfinity(spec.Iterations) && !spec.Paused
                && VectorEmitter.Compilable(frames, built.CssOf(element)))
            {
                built.TimeAnimations[element] = (spec, 0f);
                continue;
            }
            // Half a second in: past any delay the fixtures use, and inside a one-second duration,
            // so a longhand that changes timing changes what is drawn.
            new KeyframeRunner(element, frames, spec, 0f, _ => { }, built.CssOf(element), built.Touch)
                .Update(0.5f);
        }
        panel.Layout(size.x, size.y);

        OffThread.Capture(root, built, boxes, new List<VisualElement>());
        OffThread.Active = true;
        try
        {
            var tweens = new Tweens();
            tweens.Diff(root, built, 0f);
            var output = VectorEmitter.Emit(built, root, size.x, size.y, tweens, 0f, null);
            warnings.AddRange(output.Warnings);
            return new string(output.Chars, 0, output.Length);
        }
        finally { OffThread.Active = false; }
    }

    // ---- fixtures ------------------------------------------------------------------------

    /// <summary>
    /// A property only draws in a context that can show it: `flex-grow` needs a flex parent with
    /// slack, `grid-column` needs a grid, `border-collapse` needs a table, `overflow` needs something
    /// to overflow. One fixture cannot be all of those, so each row names the smallest one that could
    /// reveal it.
    /// </summary>
    private enum Fix { Box, Flex, FlexKid, Grid, GridKid, List, Table, Svg, Text, Abs, Inline, Clip }

    /// <summary>
    /// The fixture's own styling goes through CLASS selectors and the probe's through #p, so the probe
    /// outranks it. Written inline it did the opposite - an inline style beats any selector - and the
    /// first run of this probe reported every property as "drawn the same" for that reason alone.
    /// </summary>
    private static string Page(Fix fix, string rule, string extra)
    {
        const string head = "<html><head><meta name=\"viewport\" content=\"width=400\"><style>"
            + ".w{width:300px;height:200px;background:#101010}"
            + ".p{width:120px;height:60px;background:#22aa44;border:2px solid #ff8800;color:#eeeeee;font-size:14px}"
            + ".q{width:40px;height:20px;background:#444444}"
            + ".k{width:30px;height:20px;background:#884444}";
        var body = fix switch
        {
            Fix.Box or Fix.FlexKid =>
                "<div id=w class=w style=\"display:flex;flex-direction:row\">"
                + "<div id=p class=p>Ag mn <b>b</b></div><div id=q class=q>q</div></div>",
            Fix.Flex =>
                "<div id=w class=w><div id=p class=p style=\"display:flex\">"
                + "<div id=a class=k>a</div><div id=b class=k>b</div><div id=c class=k>c</div></div></div>",
            Fix.Grid =>
                "<div id=w class=w><div id=p class=p style=\"display:grid\">"
                + "<div id=a class=k>a</div><div id=b class=k>b</div>"
                + "<div id=c class=k>c</div><div id=d class=k>d</div></div></div>",
            Fix.GridKid =>
                "<div id=w class=w style=\"display:grid;grid-template-columns:60px 60px 60px;grid-template-rows:40px 40px\">"
                + "<div id=p class=k>p</div><div id=b class=k>b</div>"
                + "<div id=c class=k>c</div><div id=d class=k>d</div></div>",
            Fix.List =>
                "<div id=w class=w><ul id=l style=\"color:#eeeeee;font-size:14px\"><li id=p>one</li><li id=r>two</li></ul></div>",
            Fix.Table =>
                "<div id=w class=w><table id=p style=\"color:#eeeeee;font-size:14px\">"
                + "<tr><td id=c1 class=k>a</td><td id=c2 class=k>b</td></tr>"
                + "<tr><td class=k>c</td><td class=k>d</td></tr></table></div>",
            Fix.Svg =>
                "<div id=w class=w><svg width=\"200\" height=\"120\" viewBox=\"0 0 200 120\">"
                + "<rect id=p x=\"10\" y=\"10\" width=\"80\" height=\"50\" fill=\"#22aa44\"/>"
                + "<path id=q d=\"M 100 20 L 180 20 L 140 90 Z\" fill=\"#884444\"/></svg></div>",
            Fix.Text =>
                "<div id=w class=w><p id=p style=\"width:150px;color:#eeeeee;font-size:14px\">"
                + "Room pressure supercalifragilisticexpialidocious kPa and rather more words than fit</p></div>",
            Fix.Abs =>
                "<div id=w class=w style=\"position:relative\"><div id=p class=q style=\"position:absolute\">p</div></div>",
            Fix.Clip =>
                "<div id=w class=w><div id=p style=\"width:60px;height:24px;background:#22aa44;color:#eeeeee;font-size:14px\">"
                + "<div class=w style=\"width:200px;height:90px;background:#884444\">x</div></div></div>",
            _ =>
                "<div id=w class=w style=\"color:#eeeeee;font-size:14px\">before <span id=p>middle</span> after</div>",
        };
        return head + extra + rule + "</style></head><body>" + body + "</body></html>";
    }

    /// <summary>A declaration list becomes a rule on #p; anything carrying a brace is taken whole, so a
    /// row can put its declarations on a pseudo-element or on a sibling.</summary>
    private static string Rule(string css) => css.IndexOf('{') >= 0 ? css : "#p{" + css + "}";

    /// <summary>The fixture with the row's extra CSS but nothing on the probe: the scene a missing
    /// property draws. Keyed by the extra too, since a row's context is part of its baseline.</summary>
    private static readonly Dictionary<(Fix, string), string> Baseline = new();

    private static string BaseOf(Fix fix, string extra)
    {
        if (!Baseline.TryGetValue((fix, extra), out var s))
            Baseline[(fix, extra)] = s = Scene(Page(fix, string.Empty, extra), out _);
        return s;
    }

    // ---- 1. properties -------------------------------------------------------------------

    private static void Properties()
    {
        var rows = PropertyList();
        var have = 0;
        var missing = new List<(string, string)>();

        foreach (var (name, css, fix, extra) in rows)
        {
            try
            {
                var with = Scene(Page(fix, Rule(css), extra), out var warn);
                if (with != BaseOf(fix, extra)) { have++; continue; }
                var said = warn.FirstOrDefault(w => w.Contains(name + ":", StringComparison.Ordinal));
                missing.Add((name, said != null ? "refused" : "accepted, draws the same"));
            }
            catch (Exception ex) { missing.Add((name, "threw: " + ex.GetType().Name)); }
        }

        Report("CSS properties", have, rows.Count, missing);
    }

    // ---- 2. selectors --------------------------------------------------------------------

    /// <summary>
    /// A selector is covered when a rule using it PAINTS. Parsing is not the promise, and a compound
    /// that parses and then matches nothing - which is what several pseudo-elements do here - is
    /// indistinguishable to an author from one that was never understood.
    /// </summary>
    private static void Selectors()
    {
        var cases = SelectorList();
        var basis = Scene(Dom(string.Empty), out _);
        var have = 0;
        var missing = new List<(string, string)>();

        foreach (var (name, selector, decls) in cases)
        {
            try
            {
                var sheet = selector + "{" + (decls.Length > 0 ? decls : "background-color:#ff0000;color:#ff0000") + "}";
                var with = Scene(Dom(sheet), out var warn);
                if (with != basis) { have++; continue; }
                missing.Add((name, warn.Count > 0 ? Short(warn[warn.Count - 1]) : "parsed, painted nothing"));
            }
            catch (Exception ex) { missing.Add((name, "threw: " + ex.GetType().Name)); }
        }

        Report("CSS selectors", have, cases.Count, missing);
    }

    /// <summary>One document holding something for every selector to reach for.</summary>
    private static string Dom(string sheet) =>
        "<html><head><meta name=\"viewport\" content=\"width=400\"><style>"
        + "li,p,a,em,input,td,summary,div{color:#cccccc;font-size:14px}" + sheet
        + "</style></head><body><div id=root lang=\"en-GB\" dir=\"rtl\" style=\"width:300px\">"
        + "<ul id=list><li id=one class=\"a b\" data-k=\"x y\" title=\"Hello World\">one</li>"
        + "<li id=two class=a>two</li><li id=three>three</li><li id=four>four</li></ul>"
        + "<p id=para>para <span id=kid>kid</span> tail</p>"
        + "<a id=link href=\"#x\">link</a><em id=hollow></em><b id=bold>bold</b>"
        + "<form id=f><input id=req required><input id=num type=\"number\" min=\"1\" max=\"5\" value=\"9\">"
        + "<input id=ph placeholder=\"type here\"><input id=off disabled><input id=on checked>"
        + "<input id=ro readonly value=\"v\"></form>"
        + "<details id=det open><summary id=sum>s</summary>body</details>"
        + "<dialog id=dlg open>d</dialog>"
        + "<table id=t><tr><td id=cell>c</td><td id=cell2>c2</td></tr></table></div></body></html>";

    // ---- 3. at-rules ---------------------------------------------------------------------

    private static void AtRules()
    {
        var cases = AtRuleList();
        var have = 0;
        var missing = new List<(string, string)>();

        foreach (var (name, sheet, css) in cases)
        {
            try
            {
                var with = Scene(Page(Fix.Box, Rule(css), sheet), out var warn);
                if (with != BaseOf(Fix.Box, string.Empty)) { have++; continue; }
                missing.Add((name, warn.Count > 0 ? Short(warn[warn.Count - 1]) : "the block changed nothing"));
            }
            catch (Exception ex) { missing.Add((name, "threw: " + ex.GetType().Name)); }
        }

        Report("CSS at-rules", have, cases.Count, missing);
    }

    // ---- 4. values and functions ---------------------------------------------------------

    /// <summary>
    /// A value is covered when it draws the same as a reference spelling of the same thing. "It
    /// changed the scene" is not enough: a length that fails to parse becomes 0, which changes the
    /// scene too - so the reference is also required to have moved the scene off its baseline, or the
    /// row would pass by both spellings being ignored equally.
    ///
    /// A reference starting with ~ is a string the emitted scene must contain instead, for the values
    /// whose correctness is which vector op comes out (the gradients) rather than a number.
    /// </summary>
    private static void Values()
    {
        var cases = ValueList();
        var have = 0;
        var missing = new List<(string, string)>();

        foreach (var (name, property, wrote, meant, fix) in cases)
        {
            try
            {
                var got = Scene(Page(fix, Rule(property + ":" + wrote), string.Empty), out var warn);
                if (meant.StartsWith("~", StringComparison.Ordinal))
                {
                    if (got.Contains(meant.Substring(1), StringComparison.Ordinal)) have++;
                    else missing.Add((name, got == BaseOf(fix, string.Empty) ? "ignored" : "drew something else"));
                    continue;
                }
                var want = Scene(Page(fix, Rule(property + ":" + meant), string.Empty), out _);
                if (want == BaseOf(fix, string.Empty)) { missing.Add((name, "BAD ROW: the reference draws nothing either")); continue; }
                if (got == want) { have++; continue; }
                missing.Add((name, got == BaseOf(fix, string.Empty) ? "ignored"
                    : warn.Count > 0 ? Short(warn[warn.Count - 1]) : "parsed to something else"));
            }
            catch (Exception ex) { missing.Add((name, "threw: " + ex.GetType().Name)); }
        }

        Report("CSS values and functions", have, cases.Count, missing);
    }

    // ---- reporting -----------------------------------------------------------------------

    private static void Report(string title, int have, int total, List<(string Name, string Why)> missing)
    {
        Console.WriteLine($"{title}: {have} of {total} ({100.0 * have / total:0}%)\n");
        if (missing.Count == 0) { Console.WriteLine("  nothing missing"); return; }
        Console.WriteLine("NOT drawn:");
        foreach (var (name, why) in missing.OrderBy(m => m.Name, StringComparer.Ordinal))
            Console.WriteLine($"  {name,-40} {why}");
    }

    private static string Short(string s)
    {
        var at = s.IndexOf(": ", StringComparison.Ordinal);
        var body = at >= 0 ? s.Substring(at + 2) : s;
        return body.Length <= 60 ? body : body.Substring(0, 59) + "~";
    }

    // ======================================================================================
    // The denominators. Written from the specification's own property index, selector spec and
    // at-rule list, not from what the corpus uses.
    // ======================================================================================

    private static List<(string Name, string Css, Fix Fix, string Extra)> PropertyList()
    {
        var p = new List<(string, string, Fix, string)>();
        void P(string name, string css, Fix fix = Fix.Box, string extra = "") => p.Add((name, css, fix, extra));

        // ---- display and positioning
        P("display", "display:none");
        P("position", "position:absolute");
        P("top", "top:30px", Fix.Abs);
        P("right", "right:30px", Fix.Abs);
        P("bottom", "bottom:30px", Fix.Abs);
        P("left", "left:30px", Fix.Abs);
        P("inset", "inset:20px", Fix.Abs);
        P("inset-block", "inset-block:20px", Fix.Abs);
        P("inset-inline", "inset-inline:20px", Fix.Abs);
        P("inset-block-start", "inset-block-start:25px", Fix.Abs);
        P("inset-block-end", "inset-block-end:25px", Fix.Abs);
        P("inset-inline-start", "inset-inline-start:25px", Fix.Abs);
        P("inset-inline-end", "inset-inline-end:25px", Fix.Abs);
        P("float", "float:right");
        P("clear", "clear:both", Fix.Box, "#q{float:left}");
        P("z-index", "z-index:5");
        P("visibility", "visibility:hidden");
        P("opacity", "opacity:0.4");
        P("order", "order:5", Fix.FlexKid);
        P("isolation", "isolation:isolate");
        P("contain", "contain:strict");
        P("content-visibility", "content-visibility:hidden");
        P("box-sizing", "box-sizing:border-box");
        P("aspect-ratio", "aspect-ratio:3/1", Fix.Box, "#p{height:auto}");
        P("zoom", "zoom:2");
        P("all", "all:unset");

        // ---- overflow and clipping
        P("overflow", "overflow:hidden", Fix.Clip);
        P("overflow-x", "overflow-x:hidden", Fix.Clip);
        P("overflow-y", "overflow-y:hidden", Fix.Clip);
        P("overflow-block", "overflow-block:hidden", Fix.Clip);
        P("overflow-inline", "overflow-inline:hidden", Fix.Clip);
        P("overflow-clip-margin", "overflow-clip-margin:12px", Fix.Clip, "#p{overflow:hidden}");
        P("clip-path", "clip-path:inset(10px)");
        P("clip-rule", "clip-rule:evenodd", Fix.Svg);
        P("text-overflow", "text-overflow:ellipsis", Fix.Clip, "#p{overflow:hidden;white-space:nowrap}");

        // ---- flex container
        P("flex-direction", "flex-direction:column", Fix.Flex);
        P("flex-wrap", "flex-wrap:wrap", Fix.Flex, "#p{width:70px}");
        P("flex-flow", "flex-flow:column wrap", Fix.Flex);
        P("justify-content", "justify-content:flex-end", Fix.Flex);
        P("align-items", "align-items:flex-end", Fix.Flex);
        P("align-content", "align-content:flex-end", Fix.Flex, "#p{flex-wrap:wrap;width:70px}");
        P("place-content", "place-content:flex-end", Fix.Flex, "#p{flex-wrap:wrap;width:70px}");
        P("place-items", "place-items:flex-end", Fix.Flex);
        P("justify-items", "justify-items:end", Fix.Grid);
        P("gap", "gap:8px", Fix.Flex);
        P("row-gap", "row-gap:8px", Fix.Flex, "#p{flex-direction:column}");
        P("column-gap", "column-gap:8px", Fix.Flex);

        // ---- flex item
        P("flex", "flex:1 1 0", Fix.FlexKid);
        P("flex-grow", "flex-grow:1", Fix.FlexKid);
        P("flex-shrink", "flex-shrink:1", Fix.FlexKid, "#w{width:100px}");
        P("flex-basis", "flex-basis:200px", Fix.FlexKid);
        P("align-self", "align-self:flex-end", Fix.FlexKid);
        P("justify-self", "justify-self:end", Fix.GridKid);
        P("place-self", "place-self:end", Fix.GridKid);

        // ---- grid
        P("grid-template-columns", "grid-template-columns:30px 70px", Fix.Grid);
        P("grid-template-rows", "grid-template-rows:12px 40px", Fix.Grid);
        P("grid-template-areas", "grid-template-areas:\"a b\" \"c d\"", Fix.Grid);
        P("grid-template", "grid-template:20px 30px / 40px 50px", Fix.Grid);
        P("grid", "grid:20px 30px / 40px 50px", Fix.Grid);
        P("grid-auto-columns", "grid-auto-columns:70px", Fix.Grid);
        P("grid-auto-rows", "grid-auto-rows:45px", Fix.Grid);
        P("grid-auto-flow", "grid-auto-flow:column", Fix.Grid);
        P("grid-column", "grid-column:2 / 4", Fix.GridKid);
        P("grid-row", "grid-row:2 / 3", Fix.GridKid);
        P("grid-column-start", "grid-column-start:3", Fix.GridKid);
        P("grid-column-end", "grid-column-end:4", Fix.GridKid, "#p{grid-column-start:2}");
        P("grid-row-start", "grid-row-start:2", Fix.GridKid);
        P("grid-row-end", "grid-row-end:3", Fix.GridKid, "#p{grid-row-start:2}");
        P("grid-area", "grid-area:2 / 2 / 3 / 4", Fix.GridKid);

        // ---- box model
        P("width", "width:90px");
        P("height", "height:35px");
        P("min-width", "min-width:200px");
        P("min-height", "min-height:120px");
        P("max-width", "max-width:50px");
        P("max-height", "max-height:20px");
        P("inline-size", "inline-size:90px");
        P("block-size", "block-size:35px");
        P("min-inline-size", "min-inline-size:200px");
        P("max-inline-size", "max-inline-size:50px");
        P("min-block-size", "min-block-size:120px");
        P("max-block-size", "max-block-size:20px");
        P("margin", "margin:9px");
        P("margin-top", "margin-top:9px");
        P("margin-right", "margin-right:9px");
        P("margin-bottom", "margin-bottom:9px");
        P("margin-left", "margin-left:9px");
        P("margin-block", "margin-block:9px");
        P("margin-inline", "margin-inline:9px");
        P("margin-block-start", "margin-block-start:9px");
        P("margin-block-end", "margin-block-end:9px");
        P("margin-inline-start", "margin-inline-start:9px");
        P("margin-inline-end", "margin-inline-end:9px");
        P("padding", "padding:9px");
        P("padding-top", "padding-top:9px");
        P("padding-right", "padding-right:9px");
        P("padding-bottom", "padding-bottom:9px");
        P("padding-left", "padding-left:9px");
        P("padding-block", "padding-block:9px");
        P("padding-inline", "padding-inline:9px");
        P("padding-block-start", "padding-block-start:9px");
        P("padding-block-end", "padding-block-end:9px");
        P("padding-inline-start", "padding-inline-start:9px");
        P("padding-inline-end", "padding-inline-end:9px");

        // ---- borders
        P("border", "border:5px solid #ff0000");
        P("border-width", "border-width:6px");
        P("border-style", "border-style:dashed");
        P("border-color", "border-color:#ff0000");
        P("border-top", "border-top:7px solid #ff0000");
        P("border-right", "border-right:7px solid #ff0000");
        P("border-bottom", "border-bottom:7px solid #ff0000");
        P("border-left", "border-left:7px solid #ff0000");
        P("border-top-width", "border-top-width:7px");
        P("border-right-width", "border-right-width:7px");
        P("border-bottom-width", "border-bottom-width:7px");
        P("border-left-width", "border-left-width:7px");
        P("border-top-style", "border-top-style:none");
        P("border-right-style", "border-right-style:dotted");
        P("border-bottom-style", "border-bottom-style:none");
        P("border-left-style", "border-left-style:none");
        P("border-top-color", "border-top-color:#ff0000");
        P("border-right-color", "border-right-color:#ff0000");
        P("border-bottom-color", "border-bottom-color:#ff0000");
        P("border-left-color", "border-left-color:#ff0000");
        P("border-block", "border-block:7px solid #ff0000");
        P("border-inline", "border-inline:7px solid #ff0000");
        P("border-block-start", "border-block-start:7px solid #ff0000");
        P("border-block-end", "border-block-end:7px solid #ff0000");
        P("border-inline-start", "border-inline-start:7px solid #ff0000");
        P("border-inline-end", "border-inline-end:7px solid #ff0000");
        P("border-radius", "border-radius:14px");
        P("border-top-left-radius", "border-top-left-radius:14px");
        P("border-top-right-radius", "border-top-right-radius:14px");
        P("border-bottom-right-radius", "border-bottom-right-radius:14px");
        P("border-bottom-left-radius", "border-bottom-left-radius:14px");
        P("border-start-start-radius", "border-start-start-radius:14px");
        P("border-start-end-radius", "border-start-end-radius:14px");
        P("border-end-start-radius", "border-end-start-radius:14px");
        P("border-end-end-radius", "border-end-end-radius:14px");
        P("border-collapse", "border-collapse:collapse", Fix.Table);
        P("border-spacing", "border-spacing:9px", Fix.Table);
        P("border-image", "border-image:linear-gradient(#f00,#00f) 30");
        P("border-image-source", "border-image-source:linear-gradient(#f00,#00f)");
        P("border-image-slice", "border-image-slice:30", Fix.Box, "#p{border-image-source:linear-gradient(#f00,#00f)}");
        P("border-image-width", "border-image-width:4px", Fix.Box, "#p{border-image-source:linear-gradient(#f00,#00f)}");
        P("border-image-repeat", "border-image-repeat:round", Fix.Box, "#p{border-image-source:linear-gradient(#f00,#00f)}");
        P("border-image-outset", "border-image-outset:4px", Fix.Box, "#p{border-image-source:linear-gradient(#f00,#00f)}");
        P("corner-shape", "corner-shape:bevel", Fix.Box, "#p{border-radius:14px}");

        // ---- outline
        P("outline", "outline:4px solid #ff0000");
        P("outline-width", "outline-width:4px", Fix.Box, "#p{outline-style:solid;outline-color:#ff0000}");
        P("outline-style", "outline-style:solid", Fix.Box, "#p{outline-width:4px;outline-color:#ff0000}");
        P("outline-color", "outline-color:#ff0000", Fix.Box, "#p{outline:4px solid #00ff00}");
        P("outline-offset", "outline-offset:6px", Fix.Box, "#p{outline:4px solid #ff0000}");

        // ---- background and paint
        P("background", "background:#ff0000");
        P("background-color", "background-color:#ff0000");
        P("background-image", "background-image:linear-gradient(to right,#ff0000,#0000ff)");
        P("background-position", "background-position:12px 8px", Fix.Box, "#p{background-image:linear-gradient(to right,#ff0000,#0000ff);background-size:20px 20px;background-repeat:no-repeat}");
        P("background-position-x", "background-position-x:12px", Fix.Box, "#p{background-image:linear-gradient(to right,#ff0000,#0000ff);background-size:20px 20px;background-repeat:no-repeat}");
        P("background-position-y", "background-position-y:12px", Fix.Box, "#p{background-image:linear-gradient(to right,#ff0000,#0000ff);background-size:20px 20px;background-repeat:no-repeat}");
        P("background-size", "background-size:20px 10px", Fix.Box, "#p{background-image:linear-gradient(to right,#ff0000,#0000ff);background-repeat:no-repeat}");
        P("background-repeat", "background-repeat:no-repeat", Fix.Box, "#p{background-image:linear-gradient(to right,#ff0000,#0000ff);background-size:20px 10px}");
        P("background-origin", "background-origin:content-box", Fix.Box, "#p{background-image:linear-gradient(to right,#ff0000,#0000ff);background-size:20px 10px;background-repeat:no-repeat;padding:8px}");
        P("background-clip", "background-clip:text", Fix.Box, "#p{background-image:linear-gradient(to right,#ff0000,#0000ff)}");
        P("background-attachment", "background-attachment:fixed");
        P("background-blend-mode", "background-blend-mode:multiply", Fix.Box, "#p{background-image:linear-gradient(to right,#ff0000,#0000ff)}");
        P("box-shadow", "box-shadow:0 4px 8px #ff0000");
        P("filter", "filter:blur(3px)");
        P("backdrop-filter", "backdrop-filter:blur(3px)");
        P("mix-blend-mode", "mix-blend-mode:multiply");
        P("mask-image", "mask-image:linear-gradient(#000,transparent)");
        P("mask", "mask:linear-gradient(#000,transparent)");
        P("mask-size", "mask-size:20px", Fix.Box, "#p{mask-image:linear-gradient(#000,transparent)}");
        P("mask-position", "mask-position:5px 5px", Fix.Box, "#p{mask-image:linear-gradient(#000,transparent)}");
        P("mask-repeat", "mask-repeat:no-repeat", Fix.Box, "#p{mask-image:linear-gradient(#000,transparent)}");
        P("mask-origin", "mask-origin:content-box", Fix.Box, "#p{mask-image:linear-gradient(#000,transparent)}");
        P("mask-clip", "mask-clip:content-box", Fix.Box, "#p{mask-image:linear-gradient(#000,transparent)}");
        P("mask-mode", "mask-mode:luminance", Fix.Box, "#p{mask-image:linear-gradient(#000,transparent)}");
        P("mask-composite", "mask-composite:subtract", Fix.Box, "#p{mask-image:linear-gradient(#000,transparent)}");

        // ---- typography
        P("color", "color:#ff0000");
        P("font", "font:italic 700 22px/1.4 monospace");
        P("font-family", "font-family:monospace");
        P("font-size", "font-size:24px");
        P("font-weight", "font-weight:700");
        P("font-style", "font-style:italic");
        P("font-variant", "font-variant:small-caps");
        P("font-variant-caps", "font-variant-caps:small-caps");
        P("font-variant-numeric", "font-variant-numeric:tabular-nums");
        P("font-variant-ligatures", "font-variant-ligatures:none");
        P("font-stretch", "font-stretch:condensed");
        P("font-kerning", "font-kerning:none");
        P("font-feature-settings", "font-feature-settings:\"tnum\"");
        P("font-optical-sizing", "font-optical-sizing:none");
        P("font-synthesis", "font-synthesis:none");
        P("line-height", "line-height:2.4", Fix.Text);
        P("letter-spacing", "letter-spacing:3px");
        P("word-spacing", "word-spacing:6px");
        P("text-align", "text-align:right", Fix.Text);
        P("text-align-last", "text-align-last:right", Fix.Text);
        P("text-indent", "text-indent:20px", Fix.Text);
        P("text-transform", "text-transform:uppercase");
        P("text-decoration", "text-decoration:underline");
        P("text-decoration-line", "text-decoration-line:line-through");
        P("text-decoration-color", "text-decoration-color:#ff0000", Fix.Box, "#p{text-decoration:underline}");
        P("text-decoration-style", "text-decoration-style:dashed", Fix.Box, "#p{text-decoration:underline}");
        P("text-decoration-thickness", "text-decoration-thickness:4px", Fix.Box, "#p{text-decoration:underline}");
        P("text-underline-offset", "text-underline-offset:4px", Fix.Box, "#p{text-decoration:underline}");
        P("text-underline-position", "text-underline-position:under", Fix.Box, "#p{text-decoration:underline}");
        P("text-shadow", "text-shadow:2px 2px 3px #ff0000");
        P("text-overflow (longhand)", "text-overflow:clip", Fix.Clip, "#p{overflow:hidden;white-space:nowrap;text-overflow:ellipsis}");
        P("white-space", "white-space:nowrap", Fix.Text);
        P("white-space-collapse", "white-space-collapse:preserve", Fix.Text);
        P("text-wrap", "text-wrap:nowrap", Fix.Text);
        P("word-break", "word-break:break-all", Fix.Text);
        P("overflow-wrap", "overflow-wrap:break-word", Fix.Text);
        P("word-wrap", "word-wrap:break-word", Fix.Text);
        P("hyphens", "hyphens:auto", Fix.Text);
        P("tab-size", "tab-size:12", Fix.Text);
        P("vertical-align", "vertical-align:super", Fix.Inline);
        P("writing-mode", "writing-mode:vertical-rl", Fix.Text);
        P("text-orientation", "text-orientation:upright", Fix.Text, "#p{writing-mode:vertical-rl}");
        P("direction", "direction:rtl", Fix.Text);
        P("unicode-bidi", "unicode-bidi:bidi-override", Fix.Text);
        P("text-emphasis", "text-emphasis:dot #ff0000");
        P("text-emphasis-style", "text-emphasis-style:dot");
        P("text-emphasis-color", "text-emphasis-color:#ff0000", Fix.Box, "#p{text-emphasis-style:dot}");
        P("text-emphasis-position", "text-emphasis-position:under", Fix.Box, "#p{text-emphasis-style:dot}");
        P("quotes", "quotes:\"<<\" \">>\"", Fix.Box, "#p::before{content:open-quote}");
        P("hanging-punctuation", "hanging-punctuation:first", Fix.Text);
        P("initial-letter", "initial-letter:3", Fix.Text);
        P("-webkit-line-clamp", "-webkit-line-clamp:2", Fix.Text, "#p{display:-webkit-box;-webkit-box-orient:vertical;overflow:hidden}");
        P("line-clamp", "line-clamp:2", Fix.Text, "#p{overflow:hidden}");
        P("text-rendering", "text-rendering:optimizeLegibility");
        P("-webkit-font-smoothing", "-webkit-font-smoothing:antialiased");
        P("-webkit-text-fill-color", "-webkit-text-fill-color:#ff0000");
        P("-webkit-text-stroke", "-webkit-text-stroke:1px #ff0000");
        P("text-size-adjust", "text-size-adjust:200%");
        P("color-scheme", "color-scheme:dark");
        P("forced-color-adjust", "forced-color-adjust:none");
        P("print-color-adjust", "print-color-adjust:exact");

        // ---- transforms and motion
        P("transform", "transform:rotate(12deg)");
        P("transform-origin", "transform-origin:0 0", Fix.Box, "#p{transform:rotate(12deg)}");
        P("transform-box", "transform-box:fill-box", Fix.Svg, "#p{transform:rotate(12deg)}");
        P("transform-style", "transform-style:preserve-3d");
        P("translate", "translate:10px 6px");
        P("rotate", "rotate:12deg");
        P("scale", "scale:1.4");
        P("perspective", "perspective:200px");
        P("perspective-origin", "perspective-origin:0 0", Fix.Box, "#p{perspective:200px}");
        P("backface-visibility", "backface-visibility:hidden");
        P("will-change", "will-change:transform");
        P("offset-path", "offset-path:path(\"M 0 0 L 40 40\")");
        P("offset-distance", "offset-distance:50%", Fix.Box, "#p{offset-path:path(\"M 0 0 L 40 40\")}");
        P("offset-rotate", "offset-rotate:45deg", Fix.Box, "#p{offset-path:path(\"M 0 0 L 40 40\")}");
        P("offset-anchor", "offset-anchor:0 0", Fix.Box, "#p{offset-path:path(\"M 0 0 L 40 40\")}");
        P("offset", "offset:path(\"M 0 0 L 40 40\") 50%");

        // ---- transitions and animations. A transition only draws while it is running, so the row
        // starts one with @starting-style: the element appears from a state the sheet names, and at
        // t = 0 the scene carries the interpolation. Without that a transition is correctly invisible.
        const string appears = "@starting-style{#p{opacity:0.1}}";
        P("transition", "transition:opacity 2s", Fix.Box, appears);
        P("transition-property", "transition-property:opacity", Fix.Box, appears + "#p{transition-duration:2s}");
        P("transition-duration", "transition-duration:2s", Fix.Box, appears + "#p{transition-property:opacity}");
        P("transition-delay", "transition-delay:1s", Fix.Box, appears + "#p{transition:opacity 2s}");
        P("transition-timing-function", "transition-timing-function:ease-in", Fix.Box, appears + "#p{transition:opacity 2s linear}");
        P("transition-behavior", "transition-behavior:allow-discrete", Fix.Box, appears + "#p{transition:display 2s}");
        const string frames = "@keyframes probe{from{opacity:0.2;transform:translateX(0)}to{opacity:1;transform:translateX(40px)}}";
        P("animation", "animation:probe 2s linear infinite", Fix.Box, frames);
        P("animation-name", "animation-name:probe", Fix.Box, frames + "#p{animation-duration:2s}");
        P("animation-duration", "animation-duration:2s", Fix.Box, frames + "#p{animation-name:probe}");
        P("animation-delay", "animation-delay:-1s", Fix.Box, frames + "#p{animation:probe 2s linear infinite}");
        P("animation-iteration-count", "animation-iteration-count:3", Fix.Box, frames + "#p{animation:probe 2s linear 1}");
        P("animation-direction", "animation-direction:reverse", Fix.Box, frames + "#p{animation:probe 2s linear infinite}");
        P("animation-fill-mode", "animation-fill-mode:backwards", Fix.Box, frames + "#p{animation:probe 2s linear 1 1s}");
        P("animation-play-state", "animation-play-state:paused", Fix.Box, frames + "#p{animation:probe 2s linear infinite}");
        P("animation-timing-function", "animation-timing-function:ease-in", Fix.Box, frames + "#p{animation:probe 2s linear infinite}");
        P("animation-composition", "animation-composition:add", Fix.Box, frames + "#p{animation:probe 2s linear infinite}");

        // ---- lists, counters and generated content
        P("content", "#p::before{content:\"XX\"}", Fix.Box);
        P("list-style", "list-style:square inside", Fix.List);
        P("list-style-type", "list-style-type:square", Fix.List);
        P("list-style-position", "list-style-position:inside", Fix.List);
        P("list-style-image", "list-style-image:linear-gradient(#f00,#00f)", Fix.List);
        P("counter-reset", "counter-reset:z 7", Fix.Box, "#p::before{content:counter(z)}");
        P("counter-increment", "counter-increment:z 4", Fix.Box, "#p::before{content:counter(z)}");
        P("counter-set", "counter-set:z 5", Fix.Box, "#p::before{content:counter(z)}");

        // ---- tables
        P("table-layout", "table-layout:fixed", Fix.Table);
        P("caption-side", "caption-side:bottom", Fix.Table);
        P("empty-cells", "empty-cells:hide", Fix.Table);

        // ---- multi-column
        P("columns", "columns:2 60px", Fix.Text);
        P("column-count", "column-count:2", Fix.Text);
        P("column-width", "column-width:60px", Fix.Text);
        P("column-gap (columns)", "column-gap:20px", Fix.Text, "#p{column-count:2}");
        P("column-rule", "column-rule:2px solid #ff0000", Fix.Text, "#p{column-count:2}");
        P("column-rule-width", "column-rule-width:4px", Fix.Text, "#p{column-count:2;column-rule:1px solid #ff0000}");
        P("column-rule-style", "column-rule-style:dashed", Fix.Text, "#p{column-count:2;column-rule:1px solid #ff0000}");
        P("column-rule-color", "column-rule-color:#ff0000", Fix.Text, "#p{column-count:2;column-rule:1px solid #00ff00}");
        P("column-span", "column-span:all", Fix.Text, "#p{column-count:2}");
        P("column-fill", "column-fill:balance", Fix.Text, "#p{column-count:2}");

        // ---- replaced content and images
        P("object-fit", "object-fit:contain");
        P("object-position", "object-position:0 0");
        P("image-rendering", "image-rendering:pixelated");

        // ---- interaction, scrolling, fragmentation
        P("cursor", "cursor:pointer");
        P("pointer-events", "pointer-events:none");
        P("user-select", "user-select:none");
        P("resize", "resize:both");
        P("caret-color", "caret-color:#ff0000");
        P("accent-color", "accent-color:#ff0000");
        P("appearance", "appearance:none");
        P("touch-action", "touch-action:none");
        P("scrollbar-width", "scrollbar-width:thin", Fix.Clip, "#p{overflow:scroll}");
        P("scrollbar-color", "scrollbar-color:#ff0000 #000000", Fix.Clip, "#p{overflow:scroll}");
        P("scrollbar-gutter", "scrollbar-gutter:stable", Fix.Clip, "#p{overflow:scroll}");
        P("scroll-behavior", "scroll-behavior:smooth", Fix.Clip, "#p{overflow:scroll}");
        P("scroll-snap-type", "scroll-snap-type:x mandatory", Fix.Clip, "#p{overflow:scroll}");
        P("scroll-snap-align", "scroll-snap-align:center", Fix.Clip, "#p{overflow:scroll}");
        P("scroll-margin", "scroll-margin:8px", Fix.Clip, "#p{overflow:scroll}");
        P("scroll-padding", "scroll-padding:8px", Fix.Clip, "#p{overflow:scroll}");
        P("overscroll-behavior", "overscroll-behavior:contain", Fix.Clip, "#p{overflow:scroll}");
        P("break-before", "break-before:page", Fix.Text);
        P("break-after", "break-after:page", Fix.Text);
        P("break-inside", "break-inside:avoid", Fix.Text);
        P("page-break-inside", "page-break-inside:avoid", Fix.Text);
        P("orphans", "orphans:3", Fix.Text);
        P("widows", "widows:3", Fix.Text);
        P("container-type", "container-type:inline-size");
        P("container-name", "container-name:probe", Fix.Box, "#p{container-type:inline-size}");

        // ---- SVG presentation attributes written as CSS
        P("fill", "fill:#ff0000", Fix.Svg);
        P("fill-opacity", "fill-opacity:0.3", Fix.Svg);
        P("fill-rule", "fill-rule:evenodd", Fix.Svg);
        P("stroke", "stroke:#ff0000", Fix.Svg);
        P("stroke-width", "stroke-width:6", Fix.Svg, "#p{stroke:#ff0000}");
        P("stroke-opacity", "stroke-opacity:0.3", Fix.Svg, "#p{stroke:#ff0000;stroke-width:4}");
        P("stroke-dasharray", "stroke-dasharray:6 3", Fix.Svg, "#p{stroke:#ff0000;stroke-width:4}");
        P("stroke-dashoffset", "stroke-dashoffset:3", Fix.Svg, "#p{stroke:#ff0000;stroke-width:4;stroke-dasharray:6 3}");
        P("stroke-linecap", "stroke-linecap:round", Fix.Svg, "#q{stroke:#ff0000;stroke-width:6}");
        P("stroke-linejoin", "stroke-linejoin:round", Fix.Svg, "#q{stroke:#ff0000;stroke-width:6}");
        P("stroke-miterlimit", "stroke-miterlimit:2", Fix.Svg, "#q{stroke:#ff0000;stroke-width:6}");
        P("paint-order", "paint-order:stroke", Fix.Svg, "#p{stroke:#ff0000;stroke-width:4}");
        P("vector-effect", "vector-effect:non-scaling-stroke", Fix.Svg, "#p{stroke:#ff0000;stroke-width:4}");
        P("shape-rendering", "shape-rendering:crispEdges", Fix.Svg);
        P("text-anchor", "text-anchor:middle", Fix.Svg);
        P("dominant-baseline", "dominant-baseline:middle", Fix.Svg);
        P("marker-end", "marker-end:url(#m)", Fix.Svg);
        P("cx", "cx:40px", Fix.Svg);
        P("x", "x:40px", Fix.Svg);
        P("y", "y:40px", Fix.Svg);
        P("rx", "rx:8px", Fix.Svg);
        P("ry", "ry:8px", Fix.Svg);
        P("d", "d:path(\"M 0 0 L 30 30 Z\")", Fix.Svg);

        return p;
    }

    private static List<(string Name, string Selector, string Decls)> SelectorList()
    {
        var s = new List<(string, string, string)>();
        void S(string name, string sel, string decls = "") => s.Add((name, sel, decls));

        // ---- simple selectors
        S("type", "li");
        S("universal", "*");
        S("class", ".a");
        S("id", "#two");
        S("compound tag.class", "li.a");
        S("selector list", "em, #three");

        // ---- combinators
        S("descendant", "#list li");
        S("child >", "#list > li");
        S("next sibling +", "#one + li");
        S("later sibling ~", "#one ~ li");
        S("column ||", "#t || td");

        // ---- attribute selectors
        S("[attr]", "[data-k]");
        S("[attr=v]", "[data-k=\"x y\"]");
        S("[attr~=v]", "[data-k~=y]");
        S("[attr|=v]", "[lang|=en]");
        S("[attr^=v]", "[data-k^=\"x\"]");
        S("[attr$=v]", "[data-k$=\"y\"]");
        S("[attr*=v]", "[data-k*=\" \"]");
        S("[attr=v i] case-insensitive", "[title=\"hello world\" i]");
        S("[attr=v s] case-sensitive", "[title=\"Hello World\" s]");

        // ---- structural pseudo-classes
        S(":root", ":root");
        S(":first-child", "#list :first-child");
        S(":last-child", "#list :last-child");
        S(":only-child", "#det :only-child");
        S(":nth-child(n)", "li:nth-child(2)");
        S(":nth-child(An+B)", "li:nth-child(2n+1)");
        S(":nth-child(odd)", "li:nth-child(odd)");
        S(":nth-child(even)", "li:nth-child(even)");
        S(":nth-child(-n+B)", "li:nth-child(-n+2)");
        S(":nth-child(B of S)", "li:nth-child(1 of .a)");
        S(":nth-last-child()", "li:nth-last-child(1)");
        S(":first-of-type", "#list li:first-of-type");
        S(":last-of-type", "#list li:last-of-type");
        S(":only-of-type", "#para span:only-of-type");
        S(":nth-of-type()", "li:nth-of-type(2)");
        S(":nth-last-of-type()", "li:nth-last-of-type(2)");
        S(":empty", "#hollow:empty", "background-color:#ff0000;width:9px;height:9px");

        // ---- logical pseudo-classes
        S(":is()", ":is(#one, #two)");
        S(":where()", ":where(#one, #two)");
        S(":not()", "#list li:not(.a)");
        S(":has()", "#list:has(> .a) li");
        S(":matches() legacy", ":matches(#one, #two)");

        // ---- input and link state
        S(":link", "a:link");
        S(":any-link", "a:any-link");
        S(":visited", "a:visited");
        S(":target", "#para:target");
        S(":hover", "#one:hover");
        S(":active", "#one:active");
        S(":focus", "#req:focus");
        S(":focus-visible", "#req:focus-visible");
        S(":focus-within", "#f:focus-within");
        S(":checked", "#on:checked");
        S(":disabled", "#off:disabled");
        S(":enabled", "#req:enabled");
        S(":required", "#req:required");
        S(":optional", "#ph:optional");
        S(":read-only", "#ro:read-only");
        S(":read-write", "#req:read-write");
        S(":placeholder-shown", "#ph:placeholder-shown");
        S(":default", "#on:default");
        S(":indeterminate", "#req:indeterminate");
        S(":valid", "#num:valid");
        S(":invalid", "#num:invalid");
        S(":in-range", "#num:in-range");
        S(":out-of-range", "#num:out-of-range");
        S(":user-valid", "#num:user-valid");
        S(":user-invalid", "#num:user-invalid");
        S(":open", "#det:open");
        S(":modal", "#dlg:modal");
        S(":popover-open", "#dlg:popover-open");
        S(":lang()", "#one:lang(en)");
        S(":dir()", "#one:dir(rtl)");
        S(":scope", ":scope");

        // ---- pseudo-elements
        S("::before", "#one::before", "content:\"XX\";color:#ff0000");
        S("::after", "#one::after", "content:\"XX\";color:#ff0000");
        S(":before legacy", "#two:before", "content:\"XX\";color:#ff0000");
        S("::marker", "#list li::marker", "color:#ff0000");
        S("::placeholder", "#ph::placeholder", "color:#ff0000");
        S("::first-letter", "#para::first-letter", "color:#ff0000");
        S("::first-line", "#para::first-line", "color:#ff0000");
        S("::selection", "#para::selection", "color:#ff0000");
        S("::backdrop", "#dlg::backdrop", "background-color:#ff0000");
        S("::details-content", "#det::details-content", "color:#ff0000");
        S("::file-selector-button", "#req::file-selector-button", "color:#ff0000");
        S("::-webkit-scrollbar", "#root::-webkit-scrollbar", "background-color:#ff0000");

        // ---- escapes and specificity
        S("escaped class", ".a\\/b, #one");
        S("!important", "#one", "color:#ff0000 !important");
        S("nesting &", "#list{ & li{color:#ff0000} }", "color:#cccccc");

        return s;
    }

    private static List<(string Name, string Sheet, string Css)> AtRuleList() => new()
    {
        ("@media (min-width)", "@media (min-width:10px){#p{background-color:#ff0000}}", ""),
        ("@media (max-width)", "@media (max-width:9999px){#p{background-color:#ff0000}}", ""),
        ("@media (width)", "@media (width:400px){#p{background-color:#ff0000}}", ""),
        ("@media (min-height)", "@media (min-height:10px){#p{background-color:#ff0000}}", ""),
        ("@media (orientation)", "@media (orientation:landscape){#p{background-color:#ff0000}}", ""),
        ("@media (aspect-ratio)", "@media (min-aspect-ratio:1/2){#p{background-color:#ff0000}}", ""),
        ("@media screen", "@media screen{#p{background-color:#ff0000}}", ""),
        ("@media not print", "@media not print{#p{background-color:#ff0000}}", ""),
        ("@media and", "@media screen and (min-width:10px){#p{background-color:#ff0000}}", ""),
        ("@media comma", "@media print, screen{#p{background-color:#ff0000}}", ""),
        ("@media (prefers-color-scheme)", "@media (prefers-color-scheme:dark){#p{background-color:#ff0000}}", ""),
        ("@media (prefers-reduced-motion)", "@media (prefers-reduced-motion:no-preference){#p{background-color:#ff0000}}", ""),
        ("@media (hover)", "@media (hover:none){#p{background-color:#ff0000}}", ""),
        ("@media (pointer)", "@media (pointer:coarse){#p{background-color:#ff0000}}", ""),
        ("@media (resolution)", "@media (min-resolution:1dppx){#p{background-color:#ff0000}}", ""),
        ("@media range syntax", "@media (400px <= width){#p{background-color:#ff0000}}", ""),
        ("@supports", "@supports (display:flex){#p{background-color:#ff0000}}", ""),
        ("@supports not", "@supports not (display:nonsense){#p{background-color:#ff0000}}", ""),
        ("@supports selector()", "@supports selector(:has(a)){#p{background-color:#ff0000}}", ""),
        ("@font-face", "@font-face{font-family:\"Probe\";src:url(Barlow-Bold.ttf)}", "font-family:Probe"),
        ("@keyframes", "@keyframes probe{from{opacity:0.2}to{opacity:1}}", "animation:probe 2s linear infinite"),
        ("@layer named", "@layer base{#p{background-color:#ff0000}}", ""),
        ("@layer anonymous", "@layer{#p{background-color:#ff0000}}", ""),
        ("@scope", "@scope(.w){#p{background-color:#ff0000}}", ""),
        ("@container", "@container (min-width:10px){#p{background-color:#ff0000}}", "container-type:inline-size"),
        ("@property", "@property --probe{syntax:\"<color>\";inherits:false;initial-value:#ff0000}", "background-color:var(--probe)"),
        ("@starting-style", "@starting-style{#p{opacity:0.1}}", "opacity:1;transition:opacity 2s"),
        ("@counter-style", "@counter-style probe{system:cyclic;symbols:\"**\";suffix:\" \"}", "#p{list-style-type:probe}"),
        ("@page", "@page{margin:2cm}", ""),
        ("@import", "@import url(probe.css);", ""),
        ("@charset", "@charset \"utf-8\";#p{background-color:#ff0000}", ""),
        ("@namespace", "@namespace svg url(http://www.w3.org/2000/svg);#p{background-color:#ff0000}", ""),
        ("@nest / nesting", "#p{&.p{background-color:#ff0000}}", ""),
        ("@media nested in a rule", "#p{@media (min-width:10px){background-color:#ff0000}}", ""),
    };

    private static List<(string Name, string Property, string Wrote, string Meant, Fix Fix)> ValueList()
    {
        var v = new List<(string, string, string, string, Fix)>();
        void V(string name, string prop, string wrote, string meant, Fix fix = Fix.Box) => v.Add((name, prop, wrote, meant, fix));

        // ---- math functions
        V("calc() +", "width", "calc(30px + 20px)", "50px");
        V("calc() -", "width", "calc(70px - 20px)", "50px");
        V("calc() *", "width", "calc(25px * 2)", "50px");
        V("calc() /", "width", "calc(100px / 2)", "50px");
        V("calc() nested", "width", "calc(10px + calc(20px * 2))", "50px");
        V("calc() precedence", "width", "calc(10px + 20px * 2)", "50px");
        V("calc() percent", "width", "calc(50% - 100px)", "50px");
        V("calc() em", "width", "calc(2em + 22px)", "50px");
        V("min()", "width", "min(50px, 90px)", "50px");
        V("max()", "width", "max(50px, 10px)", "50px");
        V("clamp()", "width", "clamp(10px, 50px, 90px)", "50px");
        V("abs()", "width", "abs(-50px)", "50px");
        V("sign()", "width", "calc(50px * sign(3))", "50px");
        V("round()", "width", "round(50.4px, 1px)", "50px");
        V("mod()", "width", "mod(170px, 60px)", "50px");
        V("rem()", "width", "rem(170px, 60px)", "50px");
        V("pow()", "width", "calc(1px * pow(50, 1))", "50px");
        V("sqrt()", "width", "calc(1px * sqrt(2500))", "50px");
        V("hypot()", "width", "calc(1px * hypot(30, 40))", "50px");
        V("log()", "width", "calc(1px * log(50))", "calc(1px * 3.912023)");
        V("exp()", "width", "calc(1px * exp(1))", "calc(1px * 2.718282)");
        V("sin()", "width", "calc(100px * sin(30deg))", "50px");
        V("cos()", "width", "calc(100px * cos(60deg))", "50px");
        V("tan()", "width", "calc(50px * tan(45deg))", "50px");
        V("asin()", "transform", "rotate(calc(1deg * asin(1)))", "rotate(90deg)");
        V("atan2()", "transform", "rotate(calc(1deg * atan2(1, 1)))", "rotate(45deg)");

        // ---- custom properties
        V("var()", "width", "var(--probe)", "50px", Fix.Box);
        V("var() fallback", "width", "var(--nope, 50px)", "50px");
        V("var() nested fallback", "width", "var(--nope, var(--also-nope, 50px))", "50px");
        V("var() in calc()", "width", "calc(var(--nope, 30px) + 20px)", "50px");
        V("attr()", "width", "attr(data-w px)", "50px");
        V("env() fallback", "width", "env(safe-area-inset-left, 50px)", "50px");

        // ---- colour syntaxes
        V("#rgb", "background-color", "#f00", "#ff0000");
        V("#rgba", "background-color", "#f00f", "#ff0000");
        V("#rrggbb", "background-color", "#ff0000", "rgb(255,0,0)");
        V("#rrggbbaa", "background-color", "#ff000080", "rgba(255,0,0,0.502)");
        V("rgb() legacy", "background-color", "rgb(255, 0, 0)", "#ff0000");
        V("rgb() space-separated", "background-color", "rgb(255 0 0)", "#ff0000");
        V("rgb() with / alpha", "background-color", "rgb(255 0 0 / 50%)", "rgba(255,0,0,0.5)");
        V("rgb() percentages", "background-color", "rgb(100%, 0%, 0%)", "#ff0000");
        V("rgba()", "background-color", "rgba(255, 0, 0, 1)", "#ff0000");
        V("hsl() legacy", "background-color", "hsl(0, 100%, 50%)", "#ff0000");
        V("hsl() space-separated", "background-color", "hsl(0 100% 50%)", "#ff0000");
        V("hsla()", "background-color", "hsla(0, 100%, 50%, 1)", "#ff0000");
        V("hwb()", "background-color", "hwb(0 0% 0%)", "#ff0000");
        V("lab()", "background-color", "lab(54.29% 80.8 69.89)", "#ff0000");
        V("lch()", "background-color", "lch(54.29% 106.84 40.85)", "#ff0000");
        V("oklab()", "background-color", "oklab(0.6279 0.2249 0.1258)", "#ff0000");
        V("oklch()", "background-color", "oklch(0.6279 0.2577 29.23)", "#ff0000");
        V("color()", "background-color", "color(srgb 1 0 0)", "#ff0000");
        V("color-mix()", "background-color", "color-mix(in srgb, #ff0000 100%, #0000ff)", "#ff0000");
        V("named colour", "background-color", "red", "#ff0000");
        V("transparent", "background-color", "transparent", "rgba(0,0,0,0)");
        V("currentColor", "background-color", "currentColor", "#eeeeee");
        V("light-dark()", "background-color", "light-dark(#ff0000, #00ff00)", "#ff0000");

        // ---- lengths and other units
        V("px", "width", "50px", "50px");
        V("bare 0", "width", "0", "0px");
        V("percent", "width", "50%", "150px");
        V("em", "width", "4em", "56px");
        V("rem", "width", "4rem", "64px");
        V("ex", "width", "8ex", "56px");
        V("ch", "width", "8ch", "56px");
        V("cap", "width", "8cap", "78.4px");
        V("ic", "width", "4ic", "56px");
        V("vw", "width", "10vw", "40px");
        V("vh", "width", "10vh", "40px");
        V("vmin", "width", "10vmin", "40px");
        V("vmax", "width", "10vmax", "40px");
        V("svh", "width", "10svh", "40px");
        V("lvh", "width", "10lvh", "40px");
        V("dvh", "width", "10dvh", "40px");
        V("svw", "width", "10svw", "40px");
        V("dvw", "width", "10dvw", "40px");
        V("lh", "width", "2lh", "42px");
        V("rlh", "width", "2rlh", "48px");
        V("pt", "width", "37.5pt", "50px");
        V("pc", "width", "3pc", "48px");
        V("cm", "width", "1cm", "37.795px");
        V("mm", "width", "10mm", "37.795px");
        V("Q", "width", "40Q", "37.795px");
        V("in", "width", "0.5in", "48px");
        V("deg", "transform", "rotate(45deg)", "rotate(45deg)");
        V("rad", "transform", "rotate(0.7853982rad)", "rotate(45deg)");
        V("grad", "transform", "rotate(50grad)", "rotate(45deg)");
        V("turn", "transform", "rotate(0.125turn)", "rotate(45deg)");
        V("s / ms", "transition-duration", "2000ms", "2s");

        // ---- images and gradients
        V("linear-gradient()", "background", "linear-gradient(to right,#ff0000,#0000ff)", "~GL ");
        V("linear-gradient(deg)", "background", "linear-gradient(45deg,#ff0000,#0000ff)", "~GL ");
        V("linear-gradient(3 stops)", "background", "linear-gradient(to right,#ff0000,#00ff00,#0000ff)", "~GL ");
        V("radial-gradient()", "background", "radial-gradient(circle,#ff0000,#0000ff)", "~GR ");
        V("radial-gradient(at)", "background", "radial-gradient(circle at 20% 30%,#ff0000,#0000ff)", "~GR ");
        V("conic-gradient()", "background", "conic-gradient(#ff0000,#0000ff)", "~GC ");
        V("repeating-linear-gradient()", "background", "repeating-linear-gradient(to right,#ff0000 0 6px,#0000ff 6px 12px)", "~GL ");
        V("repeating-radial-gradient()", "background", "repeating-radial-gradient(circle,#ff0000 0 6px,#0000ff 6px 12px)", "~GR ");
        V("repeating-conic-gradient()", "background", "repeating-conic-gradient(#ff0000 0 20deg,#0000ff 20deg 40deg)", "~GC ");
        V("gradient colour stop hint", "background", "linear-gradient(to right,#ff0000 20%,#0000ff 80%)", "~GL ");
        V("url()", "background-image", "url(probe.png)", "~image");
        V("image-set()", "background-image", "image-set(url(probe.png) 1x)", "~image");
        V("none", "background-image", "none", "~SCENE");

        // ---- shapes and filters
        V("inset()", "clip-path", "inset(10px)", "~CP");
        V("circle()", "clip-path", "circle(20px)", "~CP");
        V("ellipse()", "clip-path", "ellipse(20px 10px)", "~CP");
        V("polygon()", "clip-path", "polygon(0 0, 100% 0, 50% 100%)", "~CP");
        V("path()", "clip-path", "path(\"M 0 0 L 40 0 L 40 40 Z\")", "~CP");
        V("blur()", "filter", "blur(3px)", "~");
        V("drop-shadow()", "filter", "drop-shadow(0 2px 4px #ff0000)", "~sh=");

        return v;
    }
}

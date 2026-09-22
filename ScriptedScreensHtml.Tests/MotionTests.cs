using System;
using System.Collections.Generic;
using System.Linq;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// What the motion pass will and - more importantly - will not turn into a scene expression.
/// </summary>
/// <remarks>
/// The refusals are the half worth testing. A missed expression costs a payload; a wrong one draws
/// confidently incorrect motion on a console for ever, with nothing in any log to say so. So every
/// accept here is checked against the arithmetic by hand, and every refuse is a case where the value
/// depends on something other than time.
/// </remarks>
internal static class MotionTests
{
    internal static void Run(Action<bool, string> check)
    {
        Accepts(check);
        Refuses(check);
        Bakes(check);
    }

    private static Dictionary<string, string> Of(string script)
    {
        var (found, _) = Motion.Of(script);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in found)
            for (var i = 0; i < f.Parts.Length; i++)
                if (f.Parts[i] != null)
                    map[f.Id + "." + f.Property + (f.Parts.Length > 1 ? "#" + i : "")] = f.Parts[i]!;
        return map;
    }

    /// <summary>A frame loop in the shape every page writes one, around whatever body is given.</summary>
    private static string Page(string setup, string draw) => @"
const $ = (id) => document.getElementById(id);
" + setup + @"
function update(dt) { g.clock += dt; " + Update + @" }
function draw() { " + draw + @" }
let last = 0;
function frame(t) {
  const now = t / 1000;
  const dt = last ? Math.min(0.05, now - last) : 0;
  last = now;
  update(dt);
  draw();
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
";

    private static string Update = string.Empty;

    private static void Accepts(Action<bool, string> check)
    {
        // The plain case: a value that is a function of the page's own accumulated clock. `g.clock`
        // is `t` exactly, because it accumulates dt and nothing else touches it.
        Update = string.Empty;
        var m = Of(Page("const lamp = $('lamp'); const g = { clock: 0 };",
                        "lamp.style.opacity = 0.35 + 0.35 * Math.sin(g.clock * 3);"));
        check(m.TryGetValue("lamp.style.opacity", out var lamp) && lamp == "(0.35+(0.35*sin((3*t))))",
              "motion: a clock-driven opacity is an expression of t" + Got(m, "lamp.style.opacity"));

        // A ramped speed integrated into a wrapping offset. Worth doing the arithmetic by hand:
        // speed = min(400, 100 + 30t) reaches its cap at t = (400-100)/30 = 10, so the distance is
        // 100*u + 15*u^2 + 400*max(0, t-10) with u = min(t,10) - and 15 is 30/2, the quadratic term.
        Update = "g.speed = Math.min(400, g.speed + 30 * dt); const dx = g.speed * dt; g.scroll = (g.scroll + dx * 0.25) % 800;";
        m = Of(Page("const band = $('band'); const g = { clock: 0, scroll: 0, speed: 100 };",
                    "band.style.transform = 'translateX(' + (-g.scroll) + 'px)';"));
        var wanted = "(-1*mod((0.25*(((100*min(t,10))+(15*(min(t,10)*min(t,10))))+(400*max(0,(t-10))))),800))";
        check(m.TryGetValue("band.style.transform#0", out var band) && band == wanted,
              "motion: a clamped acceleration integrates to a closed form" + Got(m, "band.style.transform#0"));

        // A family written through one loop: each element gets its own expression with the index
        // folded in, because the scene names them separately and `i` only exists inside a repeat.
        Update = "g.speed = Math.min(400, g.speed + 30 * dt); const dx = g.speed * dt; g.scroll = (g.scroll + dx * 0.25) % 800;";
        m = Of(Page("const g = { clock: 0, scroll: 0, speed: 100 };",
                    "for (let i = 0; i < 3; i++) { $('tick' + i).style.transform = 'translateX(' + ((i * 60 - g.scroll + 800) % 800) + 'px)'; }"));
        check(m.ContainsKey("tick0.style.transform#0") && m.ContainsKey("tick1.style.transform#0")
              && m.ContainsKey("tick2.style.transform#0"),
              $"motion: a loop over a family gives each member its own expression ({m.Keys.Count(k => k.StartsWith("tick", StringComparison.Ordinal))} of 3)");
        check(m.TryGetValue("tick1.style.transform#0", out var t1) && t1.StartsWith("mod(((60-", StringComparison.Ordinal),
              "motion: the loop index is folded into each member's expression" + Got(m, "tick1.style.transform#0"));

        // A single-axis move sets one axis and says nothing about the other: the untouched one must
        // not be read as "could not express it", or every translateX on every page is refused.
        Update = string.Empty;
        var (found, _) = Motion.Of(Page("const band = $('band'); const g = { clock: 0 };",
                                        "band.style.transform = 'translateX(' + (Math.sin(g.clock) * 40) + 'px)';"));
        var move = found.FirstOrDefault(f => f.Id == "band");
        check(move.Parts is { Length: 2 } && move.Parts[0] != null && move.Parts[1] == null
              && move.Touches[0] && !move.Touches[1],
              "motion: translateX sets x and leaves y alone");
    }

    private static void Refuses(Action<bool, string> check)
    {
        // The safety property, and the reason the runner yields almost nothing: an early return makes
        // everything below it conditional on state, so it is not a function of time however pure the
        // arithmetic looks. Expressing it anyway would draw a page that keeps moving after it stopped.
        Update = "if (g.over) return; g.scroll = (g.scroll + 200 * dt) % 800;";
        var m = Of(Page("const band = $('band'); const g = { clock: 0, scroll: 0, over: 0 };",
                        "band.style.transform = 'translateX(' + (-g.scroll) + 'px)';"));
        check(!m.ContainsKey("band.style.transform#0"),
              "motion: a value behind an early return is refused" + Got(m, "band.style.transform#0"));

        // Randomness is not time. Nothing about hash() would make this equivalent - the page's own
        // sequence is not reproducible in the scene.
        Update = "g.jitter = Math.random() * 10;";
        m = Of(Page("const dot = $('dot'); const g = { clock: 0, jitter: 0 };",
                    "dot.style.top = g.jitter + 'px';"));
        check(!m.ContainsKey("dot.style.top"), "motion: a random value is refused" + Got(m, "dot.style.top"));

        // A per-frame increment with no dt has a closed form only if the frame rate is assumed, and
        // baking one in would run the console at a speed no browser ever showed it at.
        Update = "g.phase = g.phase + 0.02;";
        m = Of(Page("const dial = $('dial'); const g = { clock: 0, phase: 0 };",
                    "dial.style.opacity = 0.5 + 0.5 * Math.sin(g.phase);"));
        check(!m.ContainsKey("dial.style.opacity"),
              "motion: an increment that does not scale by dt is refused" + Got(m, "dial.style.opacity"));

        // Text is drawn from a string and the expression language has no strings.
        Update = string.Empty;
        m = Of(Page("const score = $('score'); const g = { clock: 0 };",
                    "score.textContent = String(Math.floor(g.clock));"));
        check(!m.ContainsKey("score.textContent"), "motion: a text write is refused" + Got(m, "score.textContent"));
    }

    private static void Bakes(Action<bool, string> check)
    {
        var expressions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["band_t_0"] = "mod(t,800)",
            ["lamp_o"] = "0.5+0.5*sin(t)",
        };
        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal)
        {
            ["band_t_0"] = new(0f), ["lamp_o"] = new(1f), ["band_t_1"] = new(0f),
        };
        var baked = Motion.Bake("G t=[$band_t_0,$band_t_1] {\nR o=$lamp_o f=$lamp_f\n", expressions, values);

        check(baked.Contains("t=[\"=mod(t,800)\",$band_t_1]", StringComparison.Ordinal),
              "bake: one half of a pair becomes an expression and the other keeps its slot\n    got: " + baked.Replace("\n", " / "));
        check(baked.Contains("o=\"=0.5+0.5*sin(t)\"", StringComparison.Ordinal),
              "bake: a scalar slot becomes a quoted expression");
        // A slot the scene no longer mentions must not still be sent: the payload would carry a name
        // the structure does not have, which is how a value silently goes nowhere.
        check(!values.ContainsKey("band_t_0") && !values.ContainsKey("lamp_o"),
              "bake: an expressed slot leaves the value table");
        check(values.ContainsKey("band_t_1"), "bake: a slot that is still a value stays");
        // A name that merely starts with another's must not be caught by it.
        check(baked.Contains("f=$lamp_f", StringComparison.Ordinal),
              "bake: a longer slot name sharing a prefix is left alone");
    }

    private static string Got(Dictionary<string, string> m, string key)
        => m.TryGetValue(key, out var v) ? "\n    got: " + v : "\n    got: (nothing)";
}

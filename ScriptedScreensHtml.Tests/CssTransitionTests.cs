using System;
using System.Collections.Generic;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// The transition a data slot is sent with, read from the element's CSS: the shorthand list, the
/// longhands, `all`, a shorthand property covering its longhand, and the two step keywords.
/// </summary>
internal static class CssTransitionTests
{
    public static void Run(Action<bool, string> check)
    {
        static Dictionary<string, string> Css(params string[] kv)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }
        static string Show((float Dur, string Curve, float Delay)? t) => t is { } v
            ? v.Dur.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " + v.Curve + (v.Delay > 0f ? " +" + v.Delay.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "")
            : "none";

        var bar = Css("transition", "width 0.6s ease, background-color 600ms ease-out");
        check(Show(CssTransition.For(bar, "width")) == "0.6 ease", "transition: the named property takes its own item (" + Show(CssTransition.For(bar, "width")) + ")");
        check(Show(CssTransition.For(bar, "background-color")) == "0.6 ease-out", "transition: ms and a keyword curve (" + Show(CssTransition.For(bar, "background-color")) + ")");
        check(CssTransition.For(bar, "height") == null, "transition: a property not listed has no glide");

        var all = Css("transition", "all 300ms cubic-bezier(0.4, 0, 0.2, 1)");
        check(Show(CssTransition.For(all, "opacity")) == "0.3 cubic-bezier(0.4, 0, 0.2, 1)", "transition: all covers any property and keeps the bezier verbatim (" + Show(CssTransition.For(all, "opacity")) + ")");

        var longhand = Css("transition-property", "opacity, transform", "transition-duration", "200ms, 1s", "transition-timing-function", "ease-in");
        check(Show(CssTransition.For(longhand, "transform")) == "1 ease-in", "longhands: the kth duration and a cycled curve (" + Show(CssTransition.For(longhand, "transform")) + ")");
        check(CssTransition.For(longhand, "width") == null, "longhands: an unlisted property has no glide");

        var shorthand = Css("transition", "background 1s");
        check(Show(CssTransition.For(shorthand, "background-color")) == "1 ease", "a shorthand property covers its longhand (" + Show(CssTransition.For(shorthand, "background-color")) + ")");

        check(Show(CssTransition.For(Css("transition", "width 1s step-end"), "width")) == "1 steps(1)", "step-end is steps(1)");
        check(Show(CssTransition.For(Css("transition", "width 1s step-start"), "width")) == "0 linear", "step-start jumps at once, which is a snap");
        check(Show(CssTransition.For(Css("transition", "opacity 0.3s ease-in 150ms"), "opacity")) == "0.3 ease-in +0.15", "the second time is the delay (" + Show(CssTransition.For(Css("transition", "opacity 0.3s ease-in 150ms"), "opacity")) + ")");
        check(Show(CssTransition.For(Css("transition-property", "opacity", "transition-duration", "1s", "transition-delay", "2s"), "opacity")) == "1 ease +2", "transition-delay longhand");
        check(CssTransition.For(Css("transition", "none"), "width") == null, "transition: none is no glide");
        check(CssTransition.For(Css("color", "red"), "width") == null, "no transition declared, no glide");
    }
}

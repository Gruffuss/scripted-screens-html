namespace ScriptedScreensHtml;

/// <summary>
/// Number formatting the page script asks for, done by the host. Unity-free on purpose so the
/// headless tests can pin it against a browser's answers.
/// </summary>
internal static class JsNumber
{
    private static readonly string[] Formats = { "F0", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20" };

    private static readonly double[] Pow10 = { 1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20 };

    /// <summary>
    /// Number.prototype.toFixed. The prelude routes the call here because the interpreter's own
    /// costs about 2.3 KB of garbage per call, and an animating page calls it for every coordinate
    /// of every frame: 25 calls a frame measured 61 KB, most of that page's garbage. Pinned against
    /// a browser's answers by ToFixedMatchesBrowser.
    /// </summary>
    internal static string ToFixed(double v, int digits)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (double.IsNaN(v)) return "NaN";
        if (double.IsInfinity(v)) return v > 0 ? "Infinity" : "-Infinity";
        // at 1e21 and above JS gives up on fixed notation and returns String(x)
        if (System.Math.Abs(v) >= 1e21) return v.ToString("R", invariant).ToLowerInvariant();

        // JS rounds a value sitting exactly on a midpoint away from zero; .NET's "F" rounds it to
        // even. Only an exact midpoint differs, and multiplying to look for one invents them
        // (1.45 * 10 is 14.5 in doubles, while 1.45 itself is 1.44999999999999995559, which JS
        // rounds down). So the cheap product only screens, and the decimal expansion decides:
        // .NET prints the exact value at "F20".
        var scaled = v * Pow10[digits];
        if (System.Math.Abs(scaled) < 9.007199254740992e15 && System.Math.Abs(scaled % 1.0) == 0.5)
        {
            var exact = System.Math.Abs(v).ToString("F20", invariant);
            var dot = exact.IndexOf('.');
            var frac = exact.Substring(dot + 1);
            if (digits < frac.Length)
            {
                var tail = frac.Substring(digits);
                if (tail[0] == '5' && tail.TrimEnd('0').Length == 1)
                    return (v < 0 ? "-" : string.Empty) + RoundUp(exact.Substring(0, dot) + frac.Substring(0, digits), digits);
            }
        }
        return v.ToString(Formats[digits], invariant);
    }

    /// <summary>The digits of a number with the point removed, plus one, with the point put back.</summary>
    private static string RoundUp(string digitsOnly, int decimals)
    {
        var chars = digitsOnly.ToCharArray();
        var i = chars.Length - 1;
        while (i >= 0)
        {
            if (chars[i] != '9') { chars[i]++; break; }
            chars[i] = '0';
            i--;
        }
        var body = i < 0 ? "1" + new string(chars) : new string(chars);
        if (decimals == 0) return body;
        if (body.Length <= decimals) body = body.PadLeft(decimals + 1, '0');
        return body.Substring(0, body.Length - decimals) + "." + body.Substring(body.Length - decimals);
    }
}

// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;

namespace ScriptedScreensHtml.Yoga
{
    public enum Direction : byte
    {
        Inherit = 0,
        LTR = 1,
        RTL = 2,
    }

    public static partial class YogaEnums
    {
        public static int OrdinalCount(Direction _) => 3;

        public static string ToString(Direction e)
        {
            return e switch
            {
                Direction.Inherit => "inherit",
                Direction.LTR => "ltr",
                Direction.RTL => "rtl",
                _ => throw new ArgumentOutOfRangeException(nameof(e), e, "Invalid Direction value"),
            };
        }
    }
}


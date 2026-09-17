// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;

namespace ScriptedScreensHtml.Yoga
{
    public enum Justify : byte
    {
        Auto = 0,
        FlexStart = 1,
        Center = 2,
        FlexEnd = 3,
        SpaceBetween = 4,
        SpaceAround = 5,
        SpaceEvenly = 6,
        Stretch = 7,
        Start = 8,
        End = 9,
    }

    public static partial class YogaEnums
    {
        public static int OrdinalCount(Justify _) => 10;

        public static string ToString(Justify e)
        {
            return e switch
            {
                Justify.Auto => "auto",
                Justify.FlexStart => "flex-start",
                Justify.Center => "center",
                Justify.FlexEnd => "flex-end",
                Justify.SpaceBetween => "space-between",
                Justify.SpaceAround => "space-around",
                Justify.SpaceEvenly => "space-evenly",
                Justify.Stretch => "stretch",
                Justify.Start => "start",
                Justify.End => "end",
                _ => throw new ArgumentOutOfRangeException(nameof(e), e, "Invalid Justify enum value"),
            };
        }
    }
}


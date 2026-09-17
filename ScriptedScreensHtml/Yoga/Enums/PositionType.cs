// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;
namespace ScriptedScreensHtml.Yoga
{
    public enum PositionType : byte
    {
        Static = 0,
        Relative = 1,
        Absolute = 2,
    }

    public static partial class YogaEnums
    {
        public static int OrdinalCount(PositionType _) => 3;

        public static string ToString(PositionType e) => e switch
        {
            PositionType.Static => "static",
            PositionType.Relative => "relative",
            PositionType.Absolute => "absolute",
            _ => "unknown"
        };
    }
}


// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;

namespace ScriptedScreensHtml.Yoga
{
    public enum MeasureMode : byte
    {
        Undefined = 0,
        Exactly = 1,
        AtMost = 2,
    }

    public static partial class YogaEnums
    {
        public static int OrdinalCount(MeasureMode mode) => 3;
    }

    public static class MeasureModeExtensions
    {
        public static string ToStringFast(this MeasureMode mode)
        {
            return mode switch
            {
                MeasureMode.Undefined => "undefined",
                MeasureMode.Exactly => "exactly",
                MeasureMode.AtMost => "at-most",
                _ => mode.ToString()
            };
        }
    }
}


// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;

namespace ScriptedScreensHtml.Yoga
{
    public enum SizingMode
    {
        StretchFit,
        MaxContent,
        FitContent,
    }

    public static class SizingModeExtensions
    {
        public static MeasureMode ToMeasureMode(this SizingMode mode)
        {
            return mode switch
            {
                SizingMode.StretchFit => MeasureMode.Exactly,
                SizingMode.MaxContent => MeasureMode.Undefined,
                SizingMode.FitContent => MeasureMode.AtMost,
                _ => throw new InvalidOperationException("Invalid SizingMode"),
            };
        }

        public static SizingMode ToSizingMode(this MeasureMode mode)
        {
            return mode switch
            {
                MeasureMode.Exactly => SizingMode.StretchFit,
                MeasureMode.Undefined => SizingMode.MaxContent,
                MeasureMode.AtMost => SizingMode.FitContent,
                _ => throw new InvalidOperationException("Invalid MeasureMode"),
            };
        }
    }
}


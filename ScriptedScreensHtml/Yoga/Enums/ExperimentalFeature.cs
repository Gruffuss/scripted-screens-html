// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;

namespace ScriptedScreensHtml.Yoga
{
    public enum ExperimentalFeature : byte
    {
        WebFlexBasis = 0,
        FixFlexBasisFitContent = 1,
    }

    public static partial class YogaEnums
    {
        public static string ToString(ExperimentalFeature e)
        {
            return e switch
            {
                ExperimentalFeature.WebFlexBasis => nameof(ExperimentalFeature.WebFlexBasis),
                ExperimentalFeature.FixFlexBasisFitContent => nameof(ExperimentalFeature.FixFlexBasisFitContent),
                _ => e.ToString()
            };
        }
    }
}


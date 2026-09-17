// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;

namespace ScriptedScreensHtml.Yoga
{
    public enum YGBoxSizing
    {
        BorderBox,
        ContentBox
    }

    public enum BoxSizing : byte
    {
        BorderBox = YGBoxSizing.BorderBox,
        ContentBox = YGBoxSizing.ContentBox,
    }

    public static partial class YogaEnums
    {
        public static int OrdinalCount(BoxSizing type)
        {
            return 2;
        }

        public static BoxSizing ScopedEnum(YGBoxSizing unscoped)
        {
            return (BoxSizing)unscoped;
        }

        public static YGBoxSizing UnscopedEnum(BoxSizing scoped)
        {
            return (YGBoxSizing)scoped;
        }

        public static string ToString(BoxSizing e)
        {
            return UnscopedEnum(e).ToString();
        }
    }
}


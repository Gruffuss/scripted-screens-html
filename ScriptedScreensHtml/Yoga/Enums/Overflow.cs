// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;
namespace ScriptedScreensHtml.Yoga
{
    public enum Overflow : byte
    {
        Visible = 0,
        Hidden = 1,
        Scroll = 2,
    }

    public static class OverflowExtensions
    {
        public static int OrdinalCount() => 3;

        public static string ToString(Overflow e)
        {
            return e switch
            {
                Overflow.Visible => "visible",
                Overflow.Hidden => "hidden",
                Overflow.Scroll => "scroll",
                _ => e.ToString()
            };
        }
    }
}


// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;
namespace ScriptedScreensHtml.Yoga
{
    public enum Display
    {
        Flex = 0,
        None = 1,
        Contents = 2,
        Grid = 3,
    }

    public static partial class YogaEnums
    {
    }

    public static class DisplayExtensions
    {
        public static string ToString(this Display display)
        {
            return display switch
            {
                Display.Flex => "flex",
                Display.None => "none",
                Display.Contents => "contents",
                Display.Grid => "grid",
                _ => "unknown"
            };
        }
    }
}


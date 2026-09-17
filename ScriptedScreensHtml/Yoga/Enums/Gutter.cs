// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;
namespace ScriptedScreensHtml.Yoga
{
    public enum Gutter : byte
    {
        Column = 0,
        Row = 1,
        All = 2
    }

    public static class GutterExtensions
    {
        public const int OrdinalCount = 3;

        public static Gutter ScopedEnum(int unscoped)
        {
            return (Gutter)unscoped;
        }

        public static int UnscopedEnum(Gutter scoped)
        {
            return (int)scoped;
        }

        public static string ToString(Gutter e)
        {
            return e switch
            {
                Gutter.Column => "GutterColumn",
                Gutter.Row => "GutterRow",
                Gutter.All => "GutterAll",
                _ => throw new ArgumentOutOfRangeException(nameof(e))
            };
        }
    }
}


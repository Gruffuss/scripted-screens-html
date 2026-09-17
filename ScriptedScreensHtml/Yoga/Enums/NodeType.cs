// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;

namespace ScriptedScreensHtml.Yoga
{
    public enum NodeType : byte
    {
        Default = 0,
        Text = 1,
    }

    public static partial class YogaEnums
    {
        public static int OrdinalCount(NodeType _)
        {
            return 2;
        }

        public static string ToString(NodeType nodeType)
        {
            return nodeType switch
            {
                NodeType.Default => nameof(NodeType.Default),
                NodeType.Text => nameof(NodeType.Text),
                _ => throw new ArgumentOutOfRangeException(nameof(nodeType), nodeType, "Unsupported NodeType")
            };
        }
    }
}


// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;

namespace ScriptedScreensHtml.Yoga.Debug
{
    public static class AssertFatal
    {
        private static void FatalWithMessage(string message)
        {
            throw new InvalidOperationException(message);
        }

        public static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                YogaLog.Log(LogLevel.Fatal, message);
                FatalWithMessage(message);
            }
        }

        public static void AssertWithNode(Node node, bool condition, string message)
        {
            if (!condition)
            {
                YogaLog.Log(node, LogLevel.Fatal, message);
                FatalWithMessage(message);
            }
        }

        public static void AssertWithConfig(Config config, bool condition, string message)
        {
            if (!condition)
            {
                YogaLog.Log(config, LogLevel.Fatal, message);
                FatalWithMessage(message);
            }
        }
    }
}


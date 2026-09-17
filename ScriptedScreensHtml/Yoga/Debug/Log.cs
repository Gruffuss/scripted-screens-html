// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;

namespace ScriptedScreensHtml.Yoga
{
    public delegate int YogaLoggerDelegate(Config? config, Node? node, LogLevel level, string message);

    public static partial class YogaLog
    {
        public static void Log(LogLevel level, string format, params object[] args)
        {
            var message = string.Format(format, args);
            var logger = GetDefaultLogger();
            logger(null, null, level, message);
        }

        public static void Log(Node node, LogLevel level, string format, params object[] args)
        {
            var message = string.Format(format, args);
            if (node == null)
            {
                var logger = GetDefaultLogger();
                logger(null, null, level, message);
            }
            else
            {
                var config = node.Config;
                if (config == null)
                {
                    var logger = GetDefaultLogger();
                    logger(null, node, level, message);
                }
                else
                {
                    config.Log(node, level, message);
                }
            }
        }

        public static void Log(Config config, LogLevel level, string format, params object[] args)
        {
            var message = string.Format(format, args);
            if (config == null)
            {
                var logger = GetDefaultLogger();
                logger(null, null, level, message);
            }
            else
            {
                config.Log(null!, level, message);
            }
        }

        public static YogaLoggerDelegate GetDefaultLogger()
        {
            return (config, node, level, message) =>
            {
                switch (level)
                {
                    case LogLevel.Fatal:
                    case LogLevel.Error:
                        Console.Error.Write(message);
                        break;
                    case LogLevel.Warn:
                    case LogLevel.Info:
                    case LogLevel.Debug:
                    case LogLevel.Verbose:
                    default:
                        Console.Write(message);
                        break;
                }
                return 0;
            };
        }
    }
}


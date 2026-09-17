// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System.Collections;
using System.Collections.Generic;
// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

using System;

namespace ScriptedScreensHtml.Yoga
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
    public sealed class YogaDeprecatedAttribute : Attribute
    {
        public string Message { get; }

        public YogaDeprecatedAttribute(string message)
        {
            Message = message;
        }
    }
}


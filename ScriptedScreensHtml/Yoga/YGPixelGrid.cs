// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.
#nullable enable
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;
// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
//
// Original: yoga/YGPixelGrid.h, yoga/YGPixelGrid.cpp

namespace ScriptedScreensHtml.Yoga
{
    /// <summary>
    /// Public C-style API wrapper for pixel grid rounding.
    /// Delegates to the internal PixelGrid class.
    /// </summary>
    public static class YGPixelGridAPI
    {
        public static float YGRoundValueToPixelGrid(
            double value,
            double pointScaleFactor,
            bool forceCeil,
            bool forceFloor)
        {
            return PixelGrid.RoundValueToPixelGrid(
                value, pointScaleFactor, forceCeil, forceFloor);
        }
    }
}

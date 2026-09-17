// Added to the vendored Yoga.Net: pools for the lists and lines a layout pass used to allocate.
#nullable enable
#pragma warning disable
using System.Collections.Generic;

namespace ScriptedScreensHtml.Yoga
{
    /// <summary>Per-thread pools, so a layout pass allocates nothing once warm and page workers never share one.</summary>
    internal static class YogaPools
    {
        [System.ThreadStatic] private static Stack<List<Node>>? _lists;
        [System.ThreadStatic] private static Stack<FlexLine>? _lines;
        [System.ThreadStatic] private static ListSegmentEnumerator? _segment;

        internal static List<Node> RentList()
        {
            var lists = _lists ??= new Stack<List<Node>>();
            return lists.Count > 0 ? lists.Pop() : new List<Node>(8);
        }

        internal static void ReturnList(List<Node> list)
        {
            list.Clear();
            (_lists ??= new Stack<List<Node>>()).Push(list);
        }

        internal static FlexLine Line(List<Node> items, float sizeConsumed, int autoMargins, FlexLineRunningLayout layout)
        {
            var lines = _lines ??= new Stack<FlexLine>();
            if (lines.Count == 0)
                return new FlexLine(items, sizeConsumed, autoMargins, layout);
            var line = lines.Pop();
            line.ItemsInFlow = items;
            line.SizeConsumed = sizeConsumed;
            line.NumberOfAutoMargins = autoMargins;
            line.Layout = layout;
            return line;
        }

        /// <summary>Returns the line and its item list.</summary>
        internal static void Return(FlexLine line)
        {
            ReturnList(line.ItemsInFlow);
            (_lines ??= new Stack<FlexLine>()).Push(line);
        }

        /// <summary>The one line iterator of this thread: a line is collected completely before the next starts.</summary>
        internal static ListSegmentEnumerator Segment(List<Node> list, int startIndex)
        {
            var segment = _segment ??= new ListSegmentEnumerator(list, startIndex);
            segment.Reset(list, startIndex);
            return segment;
        }
    }
}

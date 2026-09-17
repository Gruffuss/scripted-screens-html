"""Copies Yoga.Net's source into this folder for netstandard2.1, under our own namespace, with the
per-layout allocations removed. Check after running: Yoga.Net's own tests against this copy
(all layout tests pass; the EventsTest cases fail because events are compiled out)."""
import os, re, shutil
import sys
# usage: python vendor.py <path to a Yoga.Net checkout>   (https://github.com/chenrensong/Yoga.Net)
SRC = sys.argv[1]
DST = os.path.dirname(os.path.abspath(__file__))
os.makedirs(DST, exist_ok=True)  # overwritten file by file: OneDrive locks folders against removal
USINGS = "using System;\nusing System.Collections;\nusing System.Collections.Generic;\n"
n = 0
root = os.path.join(SRC, "src", "Yoga.Net")
for dirpath, dirs, files in os.walk(root):
    dirs[:] = [d for d in dirs if d not in ("obj", "bin")]
    for f in files:
        if not f.endswith(".cs"):
            continue
        rel = os.path.relpath(os.path.join(dirpath, f), root)
        s = open(os.path.join(dirpath, f), encoding="utf-8-sig").read()
        s = s.replace("namespace Facebook.Yoga", "namespace ScriptedScreensHtml.Yoga")
        s = s.replace("using Facebook.Yoga", "using ScriptedScreensHtml.Yoga")
        s = s.replace("Facebook.Yoga.", "ScriptedScreensHtml.Yoga.")
        # netstandard2.1 has neither Unsafe nor BitOperations
        s = s.replace("return Unsafe.As<TEnum, byte>(ref e);", "return Convert.ToByte(e); // boxes: call sites cast directly instead")
        s = s.replace("yield return Unsafe.As<byte, TEnum>(ref Unsafe.As<int, byte>(ref i));", "yield return (TEnum)Enum.ToObject(typeof(TEnum), (byte)i);")
        s = s.replace("return 32 - BitOperations.LeadingZeroCount((uint)count);", "var bits = 0;\n            while (count > 0) { bits++; count >>= 1; }\n            return bits;")
        # the hot path (every margin/padding/border read): a cast, no boxing
        s = re.sub(r"YogaEnums\.ToUnderlying\(([\w.]+)\)", lambda m: "(int)" + m.group(1), s)
        extra = "".join(u + "\n" for u in USINGS.splitlines() if u not in s)
        s = "// Vendored from Yoga.Net (MIT, see LICENSE in this folder), a C# port of Meta's Yoga 3.2.1.\n#nullable enable\n" + extra + s
        out = os.path.join(DST, rel)
        os.makedirs(os.path.dirname(out), exist_ok=True)
        open(out, "w", encoding="utf-8", newline="\n").write(s)
        n += 1
shutil.copy(os.path.join(SRC, "LICENSE"), os.path.join(DST, "LICENSE"))
open(os.path.join(DST, ".editorconfig"), "w", encoding="utf-8").write(
    "# Vendored code: not held to this project's analyzers.\n[*.cs]\ngenerated_code = true\ndotnet_analyzer_diagnostic.severity = none\n")
print("vendored", n, "files")

# ---- no garbage per layout: the port allocated ~47 KB per incremental layout of a 200-node tree ----
def patch(rel, pairs):
    path = os.path.join(DST, rel)
    s = open(path, encoding="utf-8").read()
    for old, new in pairs:
        assert old in s, (rel, old)
        s = s.replace(old, new)
    open(path, "w", encoding="utf-8", newline="\n").write(s)

# debug events: a data object and a global lock per call, with nobody listening; compiled out
path = os.path.join(DST, "Event", "Event.cs")
s = open(path, encoding="utf-8").read()
s = s.replace("    public class Event\n    {\n", "    public class Event\n    {\n        /// <summary>Events are compiled out in this copy: no allocation and no lock per layout call.</summary>\n        public const bool Enabled = false;\n\n", 1)
open(path, "w", encoding="utf-8", newline="\n").write(s)
for rel in ("Algorithm/CalculateLayout.cs", "Algorithm/Baseline.cs", "YGNode.cs"):
    path = os.path.join(DST, rel)
    s = open(path, encoding="utf-8").read()
    s = re.sub(r"(?<![\w.])Event\.Publish\(", "if (Event.Enabled) Event.Publish(", s)
    open(path, "w", encoding="utf-8", newline="\n").write(s)

patch("Node/LayoutableChildren.cs", [("public class LayoutableChildren<T>", "public readonly struct LayoutableChildren<T>")])
patch("Node/Node.cs", [("public IReadOnlyList<Node> GetChildren() => _children;", "public List<Node> GetChildren() => _children;")])
patch("Algorithm/FlexLine.cs", [
    ("        public readonly IReadOnlyList<Node> ItemsInFlow;\n        public readonly float SizeConsumed;\n        public readonly int NumberOfAutoMargins;",
     "        public List<Node> ItemsInFlow;\n        public float SizeConsumed;\n        public int NumberOfAutoMargins;"),
    ("public FlexLine(IReadOnlyList<Node> itemsInFlow,", "public FlexLine(List<Node> itemsInFlow,"),
    ("            IEnumerator<Node> iterator,", "            ListSegmentEnumerator iterator,"),
    ("            var itemsInFlow = new List<Node>((int)node.GetChildCount());", "            var itemsInFlow = YogaPools.RentList();"),
    ("            return new FlexLine(\n                itemsInFlow,", "            return YogaPools.Line(\n                itemsInFlow,"),
])
patch("Algorithm/CalculateLayout.cs", [
    ("    internal class ListSegmentEnumerator : IEnumerator<Node>", "    public sealed class ListSegmentEnumerator : IEnumerator<Node>"),
    ("            var layoutChildren = new List<Node>(node.GetLayoutChildren());",
     "            var layoutChildren = YogaPools.RentList();\n            foreach (var layoutChild in node.GetLayoutChildren()) layoutChildren.Add(layoutChild);"),
    ("                var lineIterator = new ListSegmentEnumerator(layoutChildren, startOfLineIndex);",
     "                var lineIterator = YogaPools.Segment(layoutChildren, startOfLineIndex);"),
    ("                maxLineMainDim = Comparison.MaxOrDefined(maxLineMainDim, flexLine.Layout.MainDim);\n                lineCount++;\n            }",
     "                maxLineMainDim = Comparison.MaxOrDefined(maxLineMainDim, flexLine.Layout.MainDim);\n                lineCount++;\n                YogaPools.Return(flexLine);\n            }"),
    ("        }\n\n        public static bool CalculateLayoutInternal(",
     "            YogaPools.ReturnList(layoutChildren);\n        }\n\n        public static bool CalculateLayoutInternal("),
    ("        private readonly List<Node> _list;\n        private int _index;",
     "        private List<Node> _list;\n        private int _index;\n\n        internal void Reset(List<Node> list, int startIndex)\n        {\n            _list = list;\n            _index = startIndex - 1;\n        }"),
])
open(os.path.join(DST, "YogaPools.cs"), "w", encoding="utf-8", newline="\n").write('''// Added to the vendored Yoga.Net: pools for the lists and lines a layout pass used to allocate.
#nullable enable
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
''')
print("patched for no per-layout garbage")

# StyleLength was a class: every style read allocated one (about 97% of the garbage)
patch("Style/StyleLength.cs", [
    ("    public class StyleLength : IEquatable<StyleLength>", "    public readonly struct StyleLength : IEquatable<StyleLength>"),
    ("        public bool Equals(StyleLength? other)\n        {\n            if (other is null) return false;\n",
     "        public bool Equals(StyleLength other)\n        {\n"),
    ("        public static bool operator ==(StyleLength? left, StyleLength? right)\n        {\n            if (left is null) return right is null;\n",
     "        public static bool operator ==(StyleLength left, StyleLength right)\n        {\n"),
    ("        public static bool operator !=(StyleLength? left, StyleLength? right)", "        public static bool operator !=(StyleLength left, StyleLength right)"),
    ("        private readonly Unit _unit = Unit.Undefined;", "        private readonly Unit _unit; // default(StyleLength) is Undefined: Unit.Undefined is 0"),
])

# compiler warnings in vendored code (unreachable event calls and the like) are not ours to fix
for dirpath, dirs, files in os.walk(DST):
    for f in files:
        if f.endswith(".cs"):
            path = os.path.join(dirpath, f)
            s = open(path, encoding="utf-8").read()
            if "#pragma warning disable" not in s:
                s = s.replace("#nullable enable\n", "#nullable enable\n#pragma warning disable\n", 1)
                open(path, "w", encoding="utf-8", newline="\n").write(s)
print("struct StyleLength, warnings off")

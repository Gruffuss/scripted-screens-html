using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// UI Toolkit's own per-frame work for our panels, which runs in Unity's player loop and not in
/// <see cref="HtmlSurface"/>'s Update, so the page's diagnostics line never saw it.
/// </summary>
/// <remarks>
/// The panels exist for layout only: the vector mod draws the page. Measured 2026-09-17: the
/// update step costs about 0.03 ms a frame and the repaint step is never reached for them.
/// The game itself uses no UI Toolkit, so both timings are ours.
/// </remarks>
internal static class UiToolkitPatch
{
    internal static long UpdateTicks, RepaintTicks;

    private static readonly System.Type? Utility =
        typeof(PanelSettings).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    [HarmonyPatch]
    private static class TimeUpdate
    {
        private static bool Prepare() => Utility != null;
        private static MethodInfo TargetMethod() => AccessTools.Method(Utility, "UpdateRuntimePanels");
        private static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();
        private static void Postfix(long __state) => UpdateTicks += Stopwatch.GetTimestamp() - __state;
    }

    [HarmonyPatch]
    private static class TimeRepaint
    {
        private static bool Prepare() => Utility != null;
        private static MethodInfo TargetMethod() => AccessTools.Method(Utility, "RepaintOffscreenPanels");
        private static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();
        private static void Postfix(long __state) => RepaintTicks += Stopwatch.GetTimestamp() - __state;
    }
}

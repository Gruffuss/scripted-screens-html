using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;
using Motherboard = Assets.Scripts.Objects.Items.Motherboard;
using CartridgeIntegratedCircuitLua = ScriptedScreens.CartridgeIntegratedCircuitLua;
using ProgrammableVisorGlasses = ScriptedScreens.ProgrammableVisorGlasses;

namespace ScriptedScreensHtml;

/// <summary>
/// Same integration as the vector mod: one postfix claims elements of type <c>html</c>
/// and attaches an <see cref="HtmlSurface"/> to a stretched child of the host.
/// </summary>
[HarmonyPatch(typeof(SS), "ApplyElementInternal")]
internal static class HtmlElementPatch
{
    internal const string ElementType = "html";
    private const string SurfaceChildName = "HtmlSurface";

    /// <summary>Live pages by "board/surface/pageId", so data elements can find them.</summary>
    private static readonly Dictionary<string, HtmlSurface> Pages = new(StringComparer.Ordinal);

    /// <summary>
    /// Every data key ever sent to a page, merged. ScriptedScreens rebuilds the host on some
    /// events (the screen capture is one), which rebuilds the page from src with its
    /// defaults, and a tick only carries what changed; without this, tanks that had not
    /// changed since the rebuild showed "--" for good. Also covers data before structure.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, SS.UiValue>> Remembered = new(StringComparer.Ordinal);
    /// <summary>Pointer state per page key, so a surface rebuilt by ScriptedScreens (a click does that) keeps :hover, :active and :focus.</summary>
    internal static readonly Dictionary<string, (bool inside, bool down, UnityEngine.Vector2 fraction, string? focus)> PointerStates = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> DataIds = new(StringComparer.Ordinal);

    private static void Postfix(Motherboard? board, CartridgeIntegratedCircuitLua? cartridge,
        ProgrammableVisorGlasses? visor, SS.BoardState state, string surface, SS.UiElement element)
    {
        try
        {
            if (state == null || string.IsNullOrEmpty(surface) || element == null)
                return;
            if (!string.Equals(element.Type, ElementType, StringComparison.OrdinalIgnoreCase))
                return;
            if (!state.SurfaceElementRoots.TryGetValue(surface, out var roots) || roots == null)
                return;
            if (!roots.TryGetValue(element.Id, out var host) || host == null)
                return;

            // The fallback Image occupies the host's Graphic slot; clear rather than
            // destroy, ScriptedScreens re-adds it on every upsert.
            var fallback = host.GetComponent<Image>();
            if (fallback != null)
                fallback.color = Color.clear;

            // Structure and data are separate elements, paired by `page` (default: the
            // element id). Same reason as the vector mod: set_props merges and then upserts
            // the whole element, so one element would resend the page on every tick.
            var pageId = ReadString(element.Props, "page") ?? element.Id;
            var key = RuntimeHelpers.GetHashCode(state).ToString(CultureInfo.InvariantCulture) + "/" + surface + "/" + pageId;

            var data = Find(element.Props, "data");
            if (data != null)
            {
                Remember(key, data.Value);
                DataIds[key] = element.Id;
                if (Pages.TryGetValue(key, out var page) && page != null)
                {
                    page.DataElementId = element.Id;
                    page.ApplyData(data.Value);
                }
                return;
            }

            var src = ReadString(element.Props, "src");
            if (src == null)
                return;

            var surfaceComponent = EnsureSurface(host);
            surfaceComponent.State = state;
            surfaceComponent.Surface = surface;
            surfaceComponent.PageKey = key;
            surfaceComponent.Board = board;
            surfaceComponent.Cartridge = cartridge;
            surfaceComponent.Visor = visor;
            surfaceComponent.ElementId = element.Id;
            if (DataIds.TryGetValue(key, out var dataId))
                surfaceComponent.DataElementId = dataId;
            surfaceComponent.SetSource(src);
            if (PointerStates.TryGetValue(key, out var pointer)) surfaceComponent.RestorePointer(pointer);
            Pages[key] = surfaceComponent;
            if (Remembered.TryGetValue(key, out var all) && all.Count > 0)
                surfaceComponent.ApplyData(all);
            surfaceComponent.EmitNow();
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogError($"html element failed: {ex}");
        }
    }

    private static void Remember(string key, SS.UiValue data)
    {
        if (data.Type != SS.UiValueType.Map || data.Map == null)
            return;
        if (!Remembered.TryGetValue(key, out var all))
        {
            all = new Dictionary<string, SS.UiValue>(StringComparer.Ordinal);
            Remembered[key] = all;
        }
        foreach (var entry in data.Map)
        {
            if (string.IsNullOrEmpty(entry.Key))
                continue;
            // Maps merge one level deep: a later { o2 = { drift = 1 } } must not erase the
            // o2 fields an earlier payload carried, or a rebuilt page shows "--" for them.
            if (entry.Value.Type == SS.UiValueType.Map && entry.Value.Map != null
                && all.TryGetValue(entry.Key, out var existing) && existing.Type == SS.UiValueType.Map && existing.Map != null)
            {
                var merged = new Dictionary<string, SS.UiProp>(StringComparer.Ordinal);
                foreach (var p in existing.Map) merged[p.Key ?? string.Empty] = p;
                foreach (var p in entry.Value.Map) merged[p.Key ?? string.Empty] = p;
                var copy = existing;
                copy.Map = new List<SS.UiProp>(merged.Values).ToArray();
                all[entry.Key] = copy;
            }
            else
            {
                all[entry.Key] = entry.Value;
            }
        }
    }

    private static HtmlSurface EnsureSurface(GameObject host)
    {
        var existing = host.transform.Find(SurfaceChildName);
        GameObject child;
        if (existing == null)
        {
            child = new GameObject(SurfaceChildName, typeof(RectTransform), typeof(CanvasRenderer)) { layer = host.layer };
            var rect = child.GetComponent<RectTransform>();
            rect.SetParent(host.transform, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        else
        {
            child = existing.gameObject;
        }

        var surface = child.GetComponent<HtmlSurface>();
        if (surface == null) surface = child.AddComponent<HtmlSurface>();
        var pointer = host.GetComponent<HtmlPointer>();
        if (pointer == null) pointer = host.AddComponent<HtmlPointer>();
        pointer.Surface = surface;
        return surface;
    }

    private static SS.UiValue? Find(SS.UiProp[] props, string key)
    {
        if (props == null)
            return null;
        foreach (var p in props)
        {
            if (string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        }
        return null;
    }

    private static string? ReadString(SS.UiProp[] props, string key)
    {
        var v = Find(props, key);
        return v?.Type == SS.UiValueType.String ? v.Value.String : null;
    }
}

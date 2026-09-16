using System;
using System.Collections.Generic;
using System.Reflection;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;

namespace ScriptedScreensHtml;

/// <summary>
/// Hands a synthetic <c>vector</c> element to the vector mod's own <c>ApplyElementInternal</c>
/// postfix, found by reflection at runtime. No compile-time link: the vector mod stays a
/// separate, unmodified mod, and this one is just another author of its scene text.
/// </summary>
internal static class VectorBridge
{
    private static MethodInfo? _postfix;
    private static bool _searched;

    public static bool Available
    {
        get
        {
            Resolve();
            return _postfix != null;
        }
    }

    private static void Resolve()
    {
        if (_searched)
            return;
        _searched = true;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != "ScriptedScreensVector")
                continue;
            var type = asm.GetType("ScriptedScreensVector.VectorElementPatch");
            _postfix = type?.GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static);
            // vector 0.11.24: `VectorGraphic.ScrollChanged(host, scId, offset, max, view)` after a rebuild that
            // moved a scroll container (ask 18). Older vector mods have no event: scroll stays one-way.
            try
            {
                var graphic = asm.GetType("ScriptedScreensVector.VectorGraphic");
                var evt = graphic?.GetEvent("ScrollChanged", BindingFlags.Public | BindingFlags.Static);
                var handler = typeof(HtmlElementPatch).GetMethod("OnVectorScroll", BindingFlags.NonPublic | BindingFlags.Static);
                if (evt != null && handler != null && evt.EventHandlerType != null)
                    evt.AddEventHandler(null, Delegate.CreateDelegate(evt.EventHandlerType, handler));
                else if (evt == null)
                    ScriptedScreensHtmlPlugin.Log?.LogInfo("html: this vector mod does not report scroll offsets (needs 0.11.24); scroll events stay off");
            }
            catch (Exception ex) { ScriptedScreensHtmlPlugin.Log?.LogWarning("html: scroll report hook: " + ex.Message); }
            break;
        }
        if (_postfix == null)
            ScriptedScreensHtmlPlugin.Log?.LogWarning("html: the ScriptedScreens Vector mod is not loaded; pages cannot be drawn");
    }

    /// <summary>Send a structure (scene text) for the page's element.</summary>
    public static void Structure(object? board, object? cartridge, object? visor, SS.BoardState state, string surface,
        string elementId, string sceneId, string sceneText)
    {
        var element = new SS.UiElement
        {
            Id = elementId,
            Type = "vector",
            Props = new[]
            {
                new SS.UiProp { Key = "scene", Value = SS.UiValue.FromString(sceneId) },
                new SS.UiProp { Key = "src", Value = SS.UiValue.FromString(sceneText) },
            },
        };
        Send(board, cartridge, visor, state, surface, element);
    }

    /// <summary>Send a data payload (values and node patches) through the data element's host.</summary>
    public static void Data(object? board, object? cartridge, object? visor, SS.BoardState state, string surface,
        string elementId, string sceneId, SS.UiValue? data, SS.UiValue? nodes)
    {
        var props = new List<SS.UiProp>
        {
            new() { Key = "scene", Value = SS.UiValue.FromString(sceneId) },
            new() { Key = "keep", Value = SS.UiValue.FromNumber(1f) },
        };
        if (data != null) props.Add(new SS.UiProp { Key = "data", Value = data.Value });
        if (nodes != null) props.Add(new SS.UiProp { Key = "nodes", Value = nodes.Value });
        Send(board, cartridge, visor, state, surface, new SS.UiElement { Id = elementId, Type = "vector", Props = props.ToArray() });
    }

    private static void Send(object? board, object? cartridge, object? visor, SS.BoardState state, string surface, SS.UiElement element)
    {
        Resolve();
        if (_postfix == null)
            return;
        try
        {
            _postfix.Invoke(null, new[] { board, cartridge, visor, state, surface, element });
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogError($"html: vector bridge failed: {ex}");
        }
    }
}

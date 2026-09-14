using System;
using HarmonyLib;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;
using Motherboard = Assets.Scripts.Objects.Items.Motherboard;
using CartridgeIntegratedCircuitLua = ScriptedScreens.CartridgeIntegratedCircuitLua;
using ProgrammableVisorGlasses = ScriptedScreens.ProgrammableVisorGlasses;

namespace ScriptedScreensHtml;

/// <summary>
/// A page's controls are ScriptedScreens elements with ids <c>page/key</c>, and their events
/// go on the Lua event bus under that id, where no handler is registered. This prefix sees
/// every dispatched input, recognises a page's control by the slash, lets the page update
/// the control and its script, and re-dispatches the event to the <b>page</b> element as
/// <c>change</c> with the value <c>name=value</c>, which is what the page's Lua on_change
/// receives. The original dispatch still runs; nothing listens to it.
/// </summary>
[HarmonyPatch(typeof(SS), "DispatchUiInput")]
internal static class HtmlInputPatch
{
    private static void Prefix(Motherboard? board, CartridgeIntegratedCircuitLua? cartridge, ProgrammableVisorGlasses? visor, string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text) || !SS.TryDeserializeUiInput(text, out var input) || input == null)
                return;
            var id = input.Id ?? string.Empty;
            var slash = id.IndexOf('/');
            if (slash <= 0 || slash == id.Length - 1)
                return;
            var pageId = id.Substring(0, slash);
            var key = id.Substring(slash + 1);
            var page = HtmlSurface.Find(board, cartridge, visor, input.Surface ?? string.Empty, pageId);
            if (page == null)
                return;
            if (!page.OnExternalInput(key, input.Event ?? string.Empty, input.Value ?? string.Empty, out var name, out var value))
                return;
            var forward = new SS.UiInput { Surface = input.Surface ?? string.Empty, Id = pageId, Event = "change", Value = name + "=" + value };
            SS.DispatchUiInput(board, cartridge, visor, SS.SerializeUiInput(forward));
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: control event failed: {ex}");
        }
    }
}

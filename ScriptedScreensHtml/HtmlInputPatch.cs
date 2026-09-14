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
    private static bool Prefix(Motherboard? board, CartridgeIntegratedCircuitLua? cartridge, ProgrammableVisorGlasses? visor, string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text) || !SS.TryDeserializeUiInput(text, out var input) || input == null)
                return true;
            var id = input.Id ?? string.Empty;
            var surface = input.Surface ?? string.Empty;
            var slash = id.IndexOf('/');
            if (slash < 0)
            {
                // A click on the page itself with a node id: a page-drawn checkbox or radio
                // becomes a change on the page and the click is not delivered as a click.
                if (!string.Equals(input.Event, "click", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(input.Value))
                    return true;
                var owner = HtmlSurface.Find(board, cartridge, visor, surface, id);
                if (owner == null)
                    return true;
                if (!owner.OnControlClick(input.Value, out var cname, out var cvalue))
                {
                    // An ordinary click region: the page script gets a click event on the
                    // element, and Lua's on_click still gets the node id.
                    owner.OnPageClick(input.Value);
                    return true;
                }
                SS.DispatchUiInput(board, cartridge, visor, SS.SerializeUiInput(new SS.UiInput { Surface = surface, Id = id, Event = "change", Value = cname + "=" + cvalue }));
                return false;
            }
            if (slash == 0 || slash == id.Length - 1)
                return true;
            var pageId = id.Substring(0, slash);
            var key = id.Substring(slash + 1);
            var page = HtmlSurface.Find(board, cartridge, visor, surface, pageId);
            if (page == null)
                return true;
            if (!page.OnExternalInput(key, input.Event ?? string.Empty, input.Value ?? string.Empty, out var name, out var value))
                return true;
            var forward = new SS.UiInput { Surface = surface, Id = pageId, Event = "change", Value = name + "=" + value };
            SS.DispatchUiInput(board, cartridge, visor, SS.SerializeUiInput(forward));
            return true;
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: control event failed: {ex}");
            return true;
        }
    }
}

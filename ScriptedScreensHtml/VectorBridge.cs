using System;
using System.Collections.Generic;
using System.Reflection;
using SS = ScriptedScreens.ScriptableUi.ScriptedScreensScriptableUiSystem;
using Motherboard = Assets.Scripts.Objects.Items.Motherboard;
using CartridgeIntegratedCircuitLua = ScriptedScreens.CartridgeIntegratedCircuitLua;
using ProgrammableVisorGlasses = ScriptedScreens.ProgrammableVisorGlasses;

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

    /// <summary>
    /// The vector mod's postfix as a delegate, bound once, plus the buffers a send reuses.
    /// </summary>
    /// <remarks>
    /// MEASURED, by ablation across three game sessions: fourteen compiled consoles allocated
    /// ~35 MB/s, of which the send path was ~27 - against a whole game with 59 other mods at
    /// 2.2-2.6 MB/s. `MethodInfo.Invoke` builds an `object[]` per call and boxes every struct
    /// argument in it, and this is called once per console per frame. A bound delegate does
    /// neither, and the two collections below are rebuilt per send for no reason at all.
    ///
    /// Game thread only, and not reentrant: every caller is `HtmlSurface.Update` or the input
    /// patch, both of which are the game's own thread.
    /// </remarks>
    /// The parameter types are the postfix's OWN, not object. Delegate.CreateDelegate will not
    /// widen them - an `object` parameter against a `Motherboard` one is "method arguments are
    /// incompatible", which is exactly what the first attempt logged while quietly falling back to
    /// reflection and changing nothing.
    private delegate void PostfixCall(Motherboard? board, CartridgeIntegratedCircuitLua? cartridge,
                                      ProgrammableVisorGlasses? visor,
                                      SS.BoardState state, string surface, SS.UiElement element);
    private static PostfixCall? _call;
    private static readonly List<SS.UiProp> Scratch = new(8);

    public static bool Available
    {
        get
        {
            Resolve();
            return _postfix != null;
        }
    }

    // ---- what the vector mod will actually draw ----
    // A page only needs a frame the vector mod will rebuild: it culls off-screen surfaces and caps
    // its rebuild rate, both from its own config, and its settings are the single source of truth.
    // Read by reflection, like everything else here, so the two mods stay unlinked; if the fields
    // are missing (an older vector mod) the defaults below are its documented ones.
    private static PropertyInfo? _cull, _rateLod, _maxHz, _minHz, _fullPixels;
    private static float _readAt = -999f;
    private static bool _cullValue = true, _rateLodValue;
    private static float _maxHzValue = 60f, _minHzValue = 15f, _fullPixelsValue = 220f;

    /// <summary>The vector mod's culling and rate settings, re-read every few seconds (its config is live).</summary>
    public static (bool cull, bool rateLod, float maxHz, float minHz, float fullPixels) Lod(float now)
    {
        Resolve();
        if (now - _readAt >= 5f)
        {
            _readAt = now;
            try
            {
                if (_cull != null) _cullValue = (bool)_cull.GetValue(null);
                if (_rateLod != null) _rateLodValue = (bool)_rateLod.GetValue(null);
                if (_maxHz != null) _maxHzValue = (float)_maxHz.GetValue(null);
                if (_minHz != null) _minHzValue = (float)_minHz.GetValue(null);
                if (_fullPixels != null) _fullPixelsValue = (float)_fullPixels.GetValue(null);
            }
            catch (Exception ex) { ScriptedScreensHtmlPlugin.Log?.LogWarning("html: reading the vector mod's LOD settings: " + ex.Message); }
        }
        return (_cullValue, _rateLodValue, _maxHzValue, _minHzValue, _fullPixelsValue);
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
            if (_postfix != null)
            {
                try { _call = (PostfixCall)Delegate.CreateDelegate(typeof(PostfixCall), _postfix); }
                catch (Exception ex)
                {
                    // Signature drift in the vector mod would land here. Reflection still works, so
                    // the page draws; it just allocates, and the log says which.
                    ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: could not bind the vector postfix as a delegate, falling back to reflection: {ex.Message}");
                    _call = (b, c, v, st, su, el) => _postfix!.Invoke(null, new object?[] { b, c, v, st, su, el });
                }
            }
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
            try
            {
                var config = asm.GetType("ScriptedScreensVector.VectorConfig");
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                _cull = config?.GetProperty("CullOffScreen", any);
                _rateLod = config?.GetProperty("RateLodEnabled", any);
                _maxHz = config?.GetProperty("MaximumHz", any);
                _minHz = config?.GetProperty("MinimumHz", any);
                _fullPixels = config?.GetProperty("FullRatePixels", any);
                if (_cull == null)
                    ScriptedScreensHtmlPlugin.Log?.LogInfo("html: cannot read the vector mod's LOD settings; pages use its documented defaults");
            }
            catch (Exception ex) { ScriptedScreensHtmlPlugin.Log?.LogWarning("html: reading the vector mod's config: " + ex.Message); }
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
        string elementId, string sceneId, SS.UiValue? data, SS.UiValue? nodes, bool snap = false, SS.UiValue? ease = null)
    {
        Scratch.Clear();
        Scratch.Add(new SS.UiProp { Key = "scene", Value = SS.UiValue.FromString(sceneId) });
        Scratch.Add(new SS.UiProp { Key = "keep", Value = SS.UiValue.FromNumber(1f) });
        // vector 0.11.26 (ask 19): the payload's numbers apply at once, as a browser shows them
        if (snap) Scratch.Add(new SS.UiProp { Key = "snap", Value = SS.UiValue.FromNumber(1f) });
        // vector 0.11.33: per-name glide timing, { name = { seconds, curve } }, for the names this
        // payload moves. A property of the change, like snap: stated on every payload that moves them.
        if (ease != null) Scratch.Add(new SS.UiProp { Key = "ease", Value = ease.Value });
        if (data != null) Scratch.Add(new SS.UiProp { Key = "data", Value = data.Value });
        if (nodes != null) Scratch.Add(new SS.UiProp { Key = "nodes", Value = nodes.Value });
        Send(board, cartridge, visor, state, surface, new SS.UiElement { Id = elementId, Type = "vector", Props = Scratch.ToArray() });
    }

    private static void Send(object? board, object? cartridge, object? visor, SS.BoardState state, string surface, SS.UiElement element)
    {
        Resolve();
        if (_call == null)
            return;
        try
        {
            // A bound delegate, not MethodInfo.Invoke: the latter allocates an object[] per call and
            // boxes every struct in it, once per console per frame.
            _call(board as Motherboard, cartridge as CartridgeIntegratedCircuitLua,
                  visor as ProgrammableVisorGlasses, state, surface, element);
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogError($"html: vector bridge failed: {ex}");
        }
    }
}

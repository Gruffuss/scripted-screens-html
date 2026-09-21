using System;
using System.Collections;
using System.Reflection;

namespace ScriptedScreensHtml;

/// <summary>
/// Puts a compiled page into the Lua VM of the chip that owns it, without touching what the player
/// wrote.
/// </summary>
/// <remarks>
/// <b>The author's program is never modified.</b> Not the source on the chip, not the source in the
/// save, not the source in the editor. The compiled page is a second tenant of the same VM: it is
/// loaded as its own chunk with its own <c>_ENV</c>, so it cannot read or clobber the author's
/// globals, and it disappears when the page does. StationeersLua does have a call that installs
/// source into a chip - <c>UpdateSourceServerSide</c> - and it is explicitly NOT used here: it
/// builds a whole new runtime and replaces the old one, which would destroy the player's program.
///
/// <b>And the author's <c>on_frame</c> is never taken.</b> ScriptedScreens' own
/// <c>FrameCallbackManager.Register</c> writes <c>_callbacks[referenceId] = data</c> - one callback
/// per chip, so registering would silently evict whatever the author registered, and there is no
/// API to chain. So the compiled page is driven from this mod's own update instead, with the same
/// guards that manager applies. It costs more code and it is the only route that does not collide.
///
/// Three reflection hops reach the chip's state, because StationeersLua keeps its runtime table
/// private. Everything else is a direct call: <c>Lua.dll</c> is a referenced assembly and
/// ScriptedScreens is already publicised.
/// </remarks>
internal static class ChipHost
{
    private static readonly Type? ManagerType = Type.GetType("StationeersLua.LuaChipRuntimeManager, StationeersLua");
    private static readonly FieldInfo? RuntimesField =
        ManagerType?.GetField("Runtimes", BindingFlags.Static | BindingFlags.NonPublic);
    private static FieldInfo? _stateField;
    private static bool _complained;

    /// <summary>Whether the Lua side is reachable at all on this install.</summary>
    internal static bool Available => RuntimesField != null;

    /// <summary>
    /// The root Lua state of a chip, or null when it has not compiled yet. Null is ordinary and
    /// means "try again later", not "broken": a page is built before its chip has run.
    /// </summary>
    internal static object? StateOf(object? chip)
    {
        if (chip == null || RuntimesField == null) return null;
        try
        {
            var id = chip.GetType().GetProperty("ReferenceId")?.GetValue(chip);
            if (id == null) return null;

            // ConcurrentDictionary implements the non-generic IDictionary, so the private nested
            // runtime type never has to be named.
            if (RuntimesField.GetValue(null) is not IDictionary runtimes) return null;
            var runtime = runtimes[id];
            if (runtime == null) return null;

            _stateField ??= runtime.GetType().GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic);
            return _stateField?.GetValue(runtime);
        }
        catch (Exception ex)
        {
            if (!_complained)
            {
                _complained = true;
                ScriptedScreensHtmlPlugin.Log?.LogWarning(
                    $"html: cannot reach a chip's Lua state, so no page can be compiled into one: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// The programmable chip a cartridge holds, by the same walk ScriptedScreens does. Pure game
    /// API; the cartridge is not itself a chip, it has one in a slot.
    /// </summary>
    internal static object? ChipOf(object? cartridge)
    {
        if (cartridge == null) return null;
        try
        {
            var slots = cartridge.GetType().GetProperty("Slots")?.GetValue(cartridge) as IEnumerable;
            if (slots == null) return null;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                var type = slot.GetType().GetProperty("Type")?.GetValue(slot);
                if (type == null || type.ToString() != "ProgrammableChip") continue;
                var get = slot.GetType().GetMethod("Get", Type.EmptyTypes);
                var chip = get?.MakeGenericMethod(Type.GetType("Assets.Scripts.Objects.Electrical.ProgrammableChip, Assembly-CSharp")!)
                              .Invoke(slot, null);
                if (chip != null) return chip;
            }
        }
        catch (Exception ex)
        {
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html: cannot find the chip in this cartridge: {ex.Message}");
        }
        return null;
    }
}

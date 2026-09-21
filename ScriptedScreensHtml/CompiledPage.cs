using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ScriptedScreensHtml;

/// <summary>
/// A page, compiled: the Lua that runs it, with its DOM writes already resolved to scene slots.
/// </summary>
/// <remarks>
/// This is where the two halves meet. <see cref="JsToLua"/> turns the script into Lua that writes
/// the DOM by name; <see cref="DomSlots"/> knows which scene slot each of those names lands on. The
/// binding table generated here joins them, so the running page writes <c>$legA_y</c> rather than
/// recording <c>"legA.style.top"</c> for somebody else to interpret.
///
/// The table is emitted as data rather than resolved at run time on purpose. Every lookup it would
/// otherwise do - which element, which property, which slot, what arithmetic - is a compile-time
/// fact, and the whole point of compiling is that none of it is paid for again per frame.
///
/// What the chunk ends up being, in order: the prelude, this table, the page's own translated code,
/// and a flush that hands the accumulated values to the vector element. Nothing of this mod is in
/// that list.
/// </remarks>
internal static class CompiledPage
{
    /// <summary>One DOM write and the slots it resolves to.</summary>
    internal readonly struct Binding
    {
        public readonly string Key;          // "legA.style.top"
        public readonly string[] Slots;      // legA_y, bootA_y
        public readonly double[] Bias;       // 85, 105
        /// <summary>
        /// How to read the value the page wrote. A script does not write numbers: it writes CSS, so
        /// a height arrives as <c>"18px"</c> and a transform as <c>"translate(90px,88.7px)"</c>.
        /// Coercing those with tonumber gives nil and the write vanishes - which is exactly what
        /// happened the first time this ran end to end.
        /// </summary>
        public readonly Kind Read;
        /// <summary>For a <see cref="Kind.State"/> binding: what each reachable class name draws.</summary>
        public readonly IReadOnlyList<StateValues>? States;

        public Binding(string key, string[] slots, double[] bias, Kind read, IReadOnlyList<StateValues>? states = null)
        {
            Key = key; Slots = slots; Bias = bias; Read = read; States = states;
        }
    }

    /// <summary>The shape of the CSS value a binding reads.</summary>
    internal enum Kind
    {
        /// <summary>A length: `18px`, `0`, `1.5`. One number for one slot.</summary>
        Length,
        /// <summary>`translate(x, y)`: two numbers, for the group's translate pair.</summary>
        Translate,
        /// <summary>A string, written to a text slot as it stands.</summary>
        Text,
        /// <summary>A colour, which the renderer takes as text and never eases.</summary>
        Colour,
        /// <summary>
        /// A class name: not a value but a <b>state</b>. Every class the script can assign is
        /// enumerated at compile time, the page is laid out in each, and the slot values for each
        /// are emitted; the running page then picks one by name. That is the same mechanism a theme
        /// switch needs, arrived at from the other end.
        /// </summary>
        State,
    }

    /// <summary>The slot values one class state produces, emitted for every state the script can reach.</summary>
    internal sealed class StateValues
    {
        public string Name = string.Empty;                       // "duck", "duck hurt", ""
        public readonly List<(string Slot, double Value)> Numbers = new();
        public readonly List<(string Slot, string Value)> Text = new();
    }

    /// <summary>What compiling produced, or why it could not.</summary>
    internal sealed class Result
    {
        public string? Lua;
        public readonly List<Binding> Bindings = new();
        /// <summary>Writes that have no slot, each with the reason. A page with any of these is not compiled.</summary>
        public readonly List<string> Unmapped = new();
        public readonly List<string> Problems = new();
        public bool Ok => Lua != null && Problems.Count == 0 && Unmapped.Count == 0;
    }

    /// <summary>
    /// Compiles a page.
    /// </summary>
    /// <param name="script">The page's script.</param>
    /// <param name="available">Slot names the emitted scene exposes.</param>
    /// <param name="boxOf">Where an element sits, for the safety question. Null for an unknown element.</param>
    /// <param name="tabular">Whether an element's text is monospaced by digit run (font-variant-numeric).</param>
    /// <param name="stateOf">
    /// What the page draws with a given class on a given element: the caller sets the class, lays
    /// the page out and reads back the slots that moved. Null when the state cannot be produced.
    /// </param>
    /// <param name="element">The vector element's name in Lua, which the flush writes to.</param>
    internal static Result Compile(string script, ICollection<string> available,
                                   Func<string, DomSlots.Box?> boxOf,
                                   Func<string, bool>? tabular = null,
                                   Func<string, string, StateValues?>? stateOf = null,
                                   string element = "VDATA")
    {
        var result = new Result();

        var lua = JsToLua.Compile(script, out var problems);
        foreach (var p in problems) result.Problems.Add(p);
        if (lua == null) return result;

        var (writes, notes) = DomWrites.Of(script);
        foreach (var n in notes) result.Problems.Add(n);

        // One binding per distinct (element, property) a running page writes. Setup writes are
        // already in the geometry by the time this runs, so they are not bound to anything.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in writes)
        {
            if (!w.Runtime) continue;
            foreach (var id in Targets(w, available))
            {
                var key = id + "." + w.Property;
                if (!seen.Add(key)) continue;

                var box = boxOf(id);
                if (box == null) { result.Unmapped.Add($"line {w.Line}: \"{id}\" is not an element of this page"); continue; }

                // A class name is a state, not a value: every one the script can assign is laid out
                // here and what it draws is emitted, so the running page picks rather than computes.
                if (w.Property == "className")
                {
                    if (w.Classes == null)
                    {
                        result.Unmapped.Add($"line {w.Line}: {key} - the class is computed, so its states cannot be enumerated");
                        continue;
                    }
                    if (stateOf == null)
                    {
                        result.Unmapped.Add($"line {w.Line}: {key} - no way to lay the page out in each state");
                        continue;
                    }
                    var states = new List<StateValues>();
                    var missing = new List<string>();
                    foreach (var cls in w.Classes.Distinct(StringComparer.Ordinal))
                    {
                        var produced = stateOf(id, cls);
                        if (produced == null) { missing.Add(cls.Length == 0 ? "(none)" : cls); continue; }
                        produced.Name = cls;
                        states.Add(produced);
                    }
                    if (missing.Count > 0)
                    {
                        result.Unmapped.Add($"line {w.Line}: {key} - cannot draw the state(s) {string.Join(", ", missing)}");
                        continue;
                    }
                    result.Bindings.Add(new Binding(key, Array.Empty<string>(), Array.Empty<double>(), Kind.State, states));
                    continue;
                }

                var mapped = DomSlots.Map(id, w.Property, box.Value, available);
                if (!mapped.Mapped) { result.Unmapped.Add($"line {w.Line}: {key} - {mapped.Problem}"); continue; }

                result.Bindings.Add(new Binding(key, mapped.Slots, mapped.Bias, Reading(w.Property)));
            }
        }

        result.Lua = Assemble(lua, result.Bindings, tabular ?? (_ => false), element);
        return result;
    }

    /// <summary>What shape of CSS value a property carries.</summary>
    private static Kind Reading(string property) => property switch
    {
        "className" => Kind.State,
        "textContent" or "innerText" => Kind.Text,
        "style.transform" => Kind.Translate,
        "style.color" or "style.background" or "style.backgroundColor" or "style.background-color" => Kind.Colour,
        _ => Kind.Length,
    };

    /// <summary>
    /// The element ids one write reaches. Usually one; a family written through a computed id -
    /// <c>$('pb' + i)</c> - reaches every member, and they are real elements resolved here rather
    /// than looked up while the page runs.
    /// </summary>
    private static IEnumerable<string> Targets(DomWrites.Write w, ICollection<string> available)
    {
        if (w.Id != null) { yield return w.Id; yield break; }
        if (w.Prefix is not { Length: >= 2 } prefix) yield break;

        // a slot name is `<id>_<key>` or the bare id, so the family's members are discoverable
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in available)
        {
            if (!slot.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var cut = slot.LastIndexOf('_');
            var id = cut > 0 ? slot.Substring(0, cut) : slot;
            // `pb1_t_0` cuts to `pb1_t`, so keep cutting while what is left still starts the prefix
            while (id.Length > prefix.Length && !found.Contains(id))
            {
                var next = id.LastIndexOf('_');
                if (next <= 0 || !id.StartsWith(prefix, StringComparison.Ordinal)) break;
                var shorter = id.Substring(0, next);
                if (!shorter.StartsWith(prefix, StringComparison.Ordinal) || shorter.Length < prefix.Length) break;
                id = shorter;
            }
            if (id.Length > prefix.Length && id.StartsWith(prefix, StringComparison.Ordinal)) found.Add(id);
        }
        foreach (var id in found) yield return id;
    }

    // ---- the chunk --------------------------------------------------------------------------------

    private static string Assemble(string page, List<Binding> bindings, Func<string, bool> tabular, string element)
    {
        var sb = new StringBuilder(page.Length + bindings.Count * 64 + 2048);

        sb.Append("-- Compiled page. The mod that produced this is not running.\n")
          .Append("-- Every lookup below was a compile-time fact; none of it is worked out again here.\n\n");

        sb.Append("BOUND = {\n");
        foreach (var b in bindings)
        {
            sb.Append("  [").Append(Quote(b.Key)).Append("] = { read = ").Append(Quote(b.Read.ToString().ToLowerInvariant()));

            // A state binding carries what each class DRAWS, laid out at compile time, rather than a
            // slot and a number. Picking one at run time is a table lookup and a copy.
            if (b.Read == Kind.State && b.States != null)
            {
                sb.Append(", states = {");
                foreach (var state in b.States)
                {
                    sb.Append("\n    [").Append(Quote(state.Name)).Append("] = { ");
                    var first = true;
                    foreach (var (slot, value) in state.Numbers)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append("{ ").Append(Quote(slot)).Append(", ").Append(Num(value)).Append(" }");
                    }
                    foreach (var (slot, text) in state.Text)
                    {
                        if (!first) sb.Append(", ");
                        first = false;
                        sb.Append("{ ").Append(Quote(slot)).Append(", ").Append(Quote(text)).Append(" }");
                    }
                    sb.Append(" },");
                }
                sb.Append("\n  } },\n");
                continue;
            }

            sb.Append(", to = { ");
            for (var i = 0; i < b.Slots.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("{ ").Append(Quote(b.Slots[i])).Append(", ").Append(Num(b.Bias[i])).Append(" }");
            }
            sb.Append(" } },\n");
        }
        sb.Append("}\n\n");

        // How each text slot's value has to be shaped before it reaches the scene. The emitter does
        // this when it writes a label, and a slot write bypasses the emitter entirely - so without
        // it a score of "00042" is read back by the scene reader as the NUMBER 42 and the label
        // draws nothing, and a tabular-figures label loses the monospacing that stops its digits
        // dancing as they change.
        sb.Append("TEXT = {\n");
        foreach (var b in bindings)
        {
            if (b.Read != Kind.Text) continue;
            var dot = b.Key.IndexOf('.');
            var id = dot > 0 ? b.Key.Substring(0, dot) : b.Key;
            sb.Append("  [").Append(Quote(id)).Append("] = ")
              .Append(tabular(id) ? "js_tabular" : "js_plain").Append(",\n");
        }
        sb.Append("}\n\n");

        sb.Append(Runtime(element)).Append('\n');
        sb.Append(page);
        return sb.ToString();
    }

    /// <summary>
    /// The few lines that turn a DOM write into a slot write, and hand the result over.
    /// </summary>
    /// <remarks>
    /// <c>DOM.bind</c> replaces the prelude's recorder. A write whose (element, property) has no
    /// binding is dropped rather than accumulated - it was either a setup write, or one the compiler
    /// refused, and in both cases keeping it would only grow a table nobody reads.
    ///
    /// <c>snap = 1</c> matters and is easy to lose: without it the renderer eases every number from
    /// what is on screen toward the new value over the gap between payloads. A browser does not, so
    /// a page would gain a glide it never had - on every value at once, which reads as the motion
    /// having been mistranslated rather than as a setting.
    /// </remarks>
    private static string Runtime(string element) => @"
local PAYLOAD, DIRTY = {}, false

-- A script writes CSS, not numbers: a height arrives as '18px', an offset as '-604.8px', an opacity
-- as '0.62'. tonumber gives nil for the first two, so coercing instead of parsing made every one of
-- those writes vanish - which is what happened the first time this was run rather than reasoned about.
local function length(v)
  if type(v) == 'number' then return v end
  if type(v) ~= 'string' then return nil end
  return tonumber(v:match('^%s*(-?%d*%.?%d+)'))
end

-- The whole transform string, because that is what a page assigns. A rotate or a scale in the same
-- string is not a translate and is left alone rather than guessed at.
local function translate(v)
  if type(v) ~= 'string' then return nil, nil end
  local x, y = v:match('translate%(%s*(-?%d*%.?%d+)[^,]*,%s*(-?%d*%.?%d+)')
  if x then return tonumber(x), tonumber(y) end
  x = v:match('translateX%(%s*(-?%d*%.?%d+)')
  if x then return tonumber(x), nil end
  y = v:match('translateY%(%s*(-?%d*%.?%d+)')
  if y then return nil, tonumber(y) end
  return nil, nil
end

local function put(slot, n)
  if n == n then PAYLOAD[slot] = n DIRTY = true end   -- NaN: a page mid-calculation, not a value
end

function DOM.bind(id, key, value)
  local b = BOUND[id .. '.' .. key]
  if b == nil then return end                         -- a setup write, or one the compiler refused

  if b.read == 'text' then
    -- The emitter shapes a label's text - digit runs monospaced, a numeric string guarded - and the
    -- scene reader takes a bare number as a number, so a text slot given '00042' would draw nothing.
    -- TEXT() applies the same shaping the emitter did for this element.
    local shaped = TEXT[id]
    local s = js_str(value)
    if shaped then s = shaped(s) end
    for i = 1, #b.to do PAYLOAD[b.to[i][1]] = s end
    DIRTY = true
    return
  end

  if b.read == 'colour' then
    for i = 1, #b.to do PAYLOAD[b.to[i][1]] = js_str(value) end
    DIRTY = true
    return
  end

  if b.read == 'translate' then
    local x, y = translate(value)
    if x and b.to[1] then put(b.to[1][1], x + b.to[1][2]) end
    if y and b.to[2] then put(b.to[2][1], y + b.to[2][2]) end
    return
  end

  local n = length(value)
  if n == nil then return end
  for i = 1, #b.to do put(b.to[i][1], n + b.to[i][2]) end
end

-- Everything one frame wrote, in one payload: the renderer merges a payload and rebuilds once, so
-- splitting these would show the console a half-applied frame.
--
-- `snap` matters and is easy to lose. Without it the renderer eases every number from what is on
-- screen toward the new value over the gap between payloads. A browser does not, so a page would
-- gain a glide it never had - on every value at once, which reads as the motion having been
-- mistranslated rather than as a setting.
function DOM.flush()
  if not DIRTY then return end
  DIRTY = false
  if " + element + @" then " + element + @":set_props({ data = PAYLOAD, snap = 1 }) end
end
";

    private static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    private static string Num(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e9
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.####", CultureInfo.InvariantCulture);
}

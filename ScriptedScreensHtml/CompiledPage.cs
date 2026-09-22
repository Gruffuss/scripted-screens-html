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

    /// <summary>
    /// Fills every state out to the same set of slots, so leaving a state restores what it moved.
    /// </summary>
    /// <remarks>
    /// A slot a state does not mention takes the value the scene was emitted with - the page's
    /// resting geometry - which is exactly right: a state that does not move a box is a state in
    /// which that box sits where the markup put it.
    ///
    /// A slot with no base value is dropped from EVERY state, not only from the ones that omit it.
    /// Keeping it in some would make those states one-way - enterable and not leavable - which is
    /// the bug this whole function exists to remove, reintroduced for a subset.
    /// </remarks>
    private static void Complete(List<StateValues> states, Func<string, double?>? baseOf)
    {
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in states)
            foreach (var (slot, _) in s.Numbers) union.Add(slot);
        if (union.Count == 0) return;

        var unrestorable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in union)
            if (baseOf?.Invoke(slot) == null) unrestorable.Add(slot);

        foreach (var s in states)
        {
            var has = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (slot, _) in s.Numbers) has.Add(slot);
            s.Numbers.RemoveAll(n => unrestorable.Contains(n.Slot));
            foreach (var slot in union)
                if (!has.Contains(slot) && !unrestorable.Contains(slot) && baseOf!(slot) is { } at)
                    s.Numbers.Add((slot, at));
        }
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
    /// <param name="prelude">
    /// The runtime the compiled page sits on. It goes at the FRONT of the chunk: everything the
    /// translated page and the binding runtime use - js_str, DOM, Pending - is defined there, so
    /// without it the chunk dies on its first line and defines nothing.
    /// </param>
    /// <param name="viewport">
    /// The page's design size. A page lays itself out from <c>window.innerHeight</c> at setup, and
    /// the prelude's stub is zero - so every console compiled for the default 800 and the artwork
    /// sat at the wrong height on any console that is not that tall.
    /// </param>
    /// <param name="element">The vector element's name in Lua, which the flush writes to.</param>
    internal static Result Compile(string script, ICollection<string> available,
                                   Func<string, DomSlots.Box?> boxOf,
                                   Func<string, bool>? tabular = null,
                                   Func<string, string, StateValues?>? stateOf = null,
                                   string? prelude = null,
                                   (double Width, double Height)? viewport = null,
                                   IReadOnlyDictionary<string, string>? parents = null,
                                   Func<string, double?>? baseOf = null,
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
        // Every class string a key can take, across every write to it. A page assigns className from
        // several places - draw(), over(), reset() - and each knows only part of the set, so taking
        // the first write's list alone lost three of the player's four states.
        var classes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var computed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in writes)
        {
            if (!w.Runtime || w.Property != "className") continue;
            foreach (var id in Targets(w, available))
            {
                var name = id + ".className";
                // One unknowable write and the set is not a set. A flag rather than a marker value
                // in the list, because a class really could be called anything.
                if (w.Classes == null) { computed.Add(name); continue; }
                if (!classes.TryGetValue(name, out var list)) classes[name] = list = new List<string>();
                foreach (var c in w.Classes) if (!list.Contains(c)) list.Add(c);
            }
        }

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
                    var reachable = computed.Contains(key) ? null
                                  : classes.TryGetValue(key, out var all) ? all : w.Classes;
                    if (reachable == null)
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
                    foreach (var cls in reachable.Distinct(StringComparer.Ordinal))
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
                    // Every state carries a value for every slot ANY state moves, not just the ones
                    // it moves itself. States used to be deltas, so the base state's entry came out
                    // empty and going duck -> "" wrote nothing: the page kept the ducked geometry
                    // for ever. A state is a description of what the page looks like, not of what
                    // changed to get there, and only the first of those can be left.
                    Complete(states, baseOf);
                    result.Bindings.Add(new Binding(key, Array.Empty<string>(), Array.Empty<double>(), Kind.State, states));
                    continue;
                }

                var mapped = DomSlots.Map(id, w.Property, box.Value, available);
                if (!mapped.Mapped) { result.Unmapped.Add($"line {w.Line}: {key} - {mapped.Problem}"); continue; }

                result.Bindings.Add(new Binding(key, mapped.Slots, mapped.Bias, Reading(w.Property)));
            }
        }

        result.Lua = Assemble(lua, result.Bindings, tabular ?? (_ => false), prelude, viewport, parents, element);
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

            // The member is the prefix plus a number and nothing else. `'ob' + i` makes ob0..ob3,
            // and without the number test it also matched `obstacles` - the container they live in -
            // so a className write meant for the obstacles was bound to their parent instead.
            var end = prefix.Length;
            while (end < slot.Length && slot[end] >= '0' && slot[end] <= '9') end++;
            if (end == prefix.Length) continue;                       // no index: a different element
            if (end < slot.Length && slot[end] != '_') continue;      // `obstacles` rather than `ob3`
            found.Add(slot.Substring(0, end));
        }
        foreach (var id in found) yield return id;
    }

    // ---- the chunk --------------------------------------------------------------------------------

    private static string Assemble(string page, List<Binding> bindings, Func<string, bool> tabular,
                                   string? prelude, (double Width, double Height)? viewport,
                                   IReadOnlyDictionary<string, string>? parents, string element)
    {
        var sb = new StringBuilder(page.Length + (prelude?.Length ?? 0) + bindings.Count * 64 + 2048);

        sb.Append("-- Compiled page. The mod that produced this is not running.\n")
          .Append("-- Every lookup below was a compile-time fact; none of it is worked out again here.\n\n");

        // First, because everything after it depends on it: js_str, DOM, Pending and the rest
        // are all defined there, and without it the chunk dies on its first line and defines nothing.
        if (prelude != null) sb.Append(prelude).Append("\n\n");

        // The console this page was compiled for. A page reads it at setup to size itself, and
        // the prelude's stub is zero, so without this every console laid out for the fallback 800.
        if (viewport is { } v)
            sb.Append("window.innerWidth, window.innerHeight = ")
              .Append(Num(v.Width)).Append(", ").Append(Num(v.Height)).Append("\n\n");

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

        // Who contains whom, so an event can bubble. A page listens on a container and the click
        // lands on whichever child is under the cursor, so without this the common case - the
        // runner's `field.addEventListener('mousedown', jump)` - never fires at all. Static, because
        // the structure a compiled page draws does not change; that is the whole premise.
        sb.Append("PARENT = {\n");
        if (parents != null)
            foreach (var kv in parents)
                sb.Append("  [").Append(Quote(kv.Key)).Append("] = ").Append(Quote(kv.Value)).Append(",\n");
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
-- Globals of this chunk, not locals, so the host can read what a frame produced. The payload does
-- not go to the element from here: the mod already has the send path, knows which element this page
-- is, and owns the rate at which the renderer wants values. Reaching for an element handle in Lua
-- would mean reproducing all of that, and getting the name wrong once already cost a silent frozen
-- console - the chunk ran, computed every value, and dropped them all.
PAYLOAD, DIRTY = {}, false

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

  -- A class name is not a value, it is a state: the compiler laid the page out in each one and
  -- emitted what each DRAWS, so picking one is a lookup and a copy. Without this arm the write fell
  -- through to length('duck'), which is nil, and every compiled page drew its base state for ever
  -- while the states sat in the table unused.
  if b.read == 'state' then
    local st = b.states[js_str(value)]
    if st == nil then return end                      -- a class the enumeration never saw
    for i = 1, #st do PAYLOAD[st[i][1]] = st[i][2] end
    if #st > 0 then DIRTY = true end
    return
  end

  local n = length(value)
  if n == nil then return end
  for i = 1, #b.to do put(b.to[i][1], n + b.to[i][2]) end
end

-- The two the compiler emits instead of a CSS string. A page writes `x + 'px'` and
-- `'translate(' + x + 'px,' + y + 'px)'`; the compiler recognises those shapes and calls these with
-- the numbers, so nothing is built and nothing is parsed. That round trip - build a string, then
-- pattern match the numbers back out of it - was the whole of a compiled page's per-frame garbage,
-- and the compiler was creating both halves of it.
function DOM.num(el, key, n)
  -- tonumber, because a page almost always writes `x.toFixed(1) + 'px'` and toFixed returns a
  -- STRING. Rejecting non-numbers here silently wrote nothing at all, which is how the runner's
  -- player stopped moving the first time this was tried.
  n = tonumber(n)
  if n == nil or n ~= n then return end
  local b = BOUND[rawget(el, '__id') .. '.style.' .. key]
  if b == nil or b.read ~= 'length' then return end
  for i = 1, #b.to do PAYLOAD[b.to[i][1]] = n + b.to[i][2] end
  DIRTY = true
end

function DOM.xy(el, key, x, y)
  local b = BOUND[rawget(el, '__id') .. '.style.' .. key]
  if b == nil or b.read ~= 'translate' then return end
  x, y = tonumber(x), tonumber(y)
  if x ~= nil and x == x and b.to[1] then PAYLOAD[b.to[1][1]] = x + b.to[1][2] DIRTY = true end
  if y ~= nil and y == y and b.to[2] then PAYLOAD[b.to[2][1]] = y + b.to[2][2] DIRTY = true end
end

-- Everything one frame wrote, in one payload: the renderer merges a payload and rebuilds once, so
-- splitting these would show the console a half-applied frame.
--
-- `snap` matters and is easy to lose. Without it the renderer eases every number from what is on
-- screen toward the new value over the gap between payloads. A browser does not, so a page would
-- gain a glide it never had - on every value at once, which reads as the motion having been
-- mistranslated rather than as a setting.
function DOM.flush()
  -- Nothing to do: the host takes PAYLOAD when DIRTY says there is something in it, and empties
  -- BOTH it and the flag. Emptying matters - while it only cleared the flag, every frame re-sent
  -- every slot the page had ever written, so a frame that moved one number sent all of them.
  -- Kept as a function so the page's frame reads the same whether or not a host is listening.
end

-- The entry point the host calls once a frame. It is a GLOBAL of this chunk's own environment,
-- which is how the host finds it - everything the page itself declared is a local of the chunk and
-- invisible from outside, deliberately.
--
-- What it drives depends on what the page asked for, and pages differ: a game registers a
-- requestAnimationFrame callback and re-registers it every frame, a dashboard sets an interval and
-- keeps the same one. Both were captured by the prelude rather than run, so this is where they are
-- finally driven - and then one payload goes out for everything the frame wrote.
frame = function(dt)
  CLOCK = (CLOCK or 0) + (dt or 0)
  local t = CLOCK * 1000

  local pending = Pending.frame
  if #pending > 0 then
    local fn = pending[#pending]
    Pending.frame = {}                 -- a rAF page re-registers inside the call
    fn(t)
  else
    for i = 1, #Pending.timers do
      local timer = Pending.timers[i]
      timer.at = (timer.at or 0) + (dt or 0) * 1000
      if timer.at >= (timer.ms or 0) then
        timer.at = 0
        timer.fn(t)
      end
    end
  end

  DOM.flush()
end

-- The other entry point: something the player did. The host calls this with the id of the scene
-- region that was hit, and the page's own handlers run here, in the chunk, against the state that
-- is on screen. Before this existed the click went to the INTERPRETER's copy of the page - which a
-- compiled console has stopped drawing - so every button was dead while the scene still carried its
-- click region and the log said nothing was wrong.
event = function(id, kind, x, y)
  DOM.fire(id, kind, x, y)
  DOM.flush()
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

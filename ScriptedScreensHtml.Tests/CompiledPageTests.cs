using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lua;
using Lua.Standard;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Tests;

/// <summary>
/// A page compiled end to end, and run.
/// </summary>
/// <remarks>
/// This is the whole compiler in one check. The script becomes Lua, its DOM writes resolve to scene
/// slots at compile time, and running it produces a payload of slot values which is compared against
/// what the page means. If the binding table is wrong, a bias is missing, or a write lands on the
/// wrong slot, the numbers here are wrong - and nothing else in the suite would notice, because
/// every other test checks one half or the other.
///
/// The element boxes come from a scene the game actually emitted, checked in beside this file, so
/// the parent origins and descendant offsets are real rather than assumed.
/// </remarks>
internal static class CompiledPageTests
{
    /// <summary>The player's own absolute top in the captured scene; its children's tops are relative to it.</summary>
    private const double PlayerTop = 85;

    internal static void Run(Action<bool, string> check)
    {
        var root = Root();
        if (root == null) { check(false, "compiled: cannot find the source folder"); return; }

        var scenePath = ScenePath(root);
        if (!File.Exists(scenePath)) { check(false, "compiled: the captured scene is missing"); return; }

        var values = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        SceneSlots.Split(File.ReadAllText(scenePath), values);
        var available = values.Keys.ToHashSet(StringComparer.Ordinal);
        var boxes = Boxes(File.ReadAllText(scenePath));

        var script = Regex.Match(File.ReadAllText(Path.Combine(root, "examples", "07-game.lua")),
                                 "<script>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;

        var compiled = CompiledPage.Compile(script, available, id => Box(id, boxes, available),
                                            tabular: id => id is "score" or "hi",
                                            stateOf: (id, cls) => State(id, cls, boxes, available));

        check(compiled.Lua != null, compiled.Lua != null
            ? $"compiled: 07-game compiles, {compiled.Bindings.Count} slot binding(s)"
            : $"compiled: does not compile - {string.Join("; ", compiled.Problems.Take(3))}");
        if (compiled.Lua == null) return;

        check(compiled.Unmapped.Count == 0, compiled.Unmapped.Count == 0
            ? "compiled: every runtime write binds to a slot"
            : $"compiled: {compiled.Unmapped.Count} unmapped - {string.Join("; ", compiled.Unmapped.Take(3))}");

        Dictionary<string, LuaValue> sent;
        LuaValue snap;
        try
        {
            (sent, snap) = RunIt(root, compiled.Lua!, frames: 120);
        }
        catch (Exception ex)
        {
            check(false, $"compiled: running it threw - {ex.Message.Split('\n')[0]}");
            return;
        }

        if (sent.Count == 0) { check(false, "compiled: nothing reached the vector element"); return; }
        check(true, $"compiled: {sent.Count} slot value(s) reached the vector element");

        check(snap.Type != LuaValueType.Nil, snap.Type != LuaValueType.Nil
            ? "compiled: the payload carries snap, so values do not glide"
            : "compiled: the payload has no snap - every value would ease over the tick gap");

        // draw() sets legA's height to 16, 18 or 24 and its top to legTop + (24 - height), and
        // legTop is 56 unless ducking, so top + height is 80 in CSS - and 80 plus the player's own
        // top in the scene, because the scene is absolute. That relation is the whole mapping in one
        // number: get the bias wrong and it fails, get the slot wrong and it fails.
        var h = Num(sent, "legA_h");
        var y = Num(sent, "legA_y");
        check(h is 16 or 18 or 24, $"compiled: legA_h is {h}, one of the heights draw() sets");
        var want = PlayerTop + 80 - h;
        check(Math.Abs(y - want) < 0.001, Math.Abs(y - want) < 0.001
            ? $"compiled: legA_y is {y} = the player's top {PlayerTop} + (80 - {h}) - the leg is in the right place"
            : $"compiled: legA_y is {y}, expected {want} for a height of {h}");

        check(sent.ContainsKey("bootA_y"), sent.ContainsKey("bootA_y")
            ? $"compiled: bootA_y rides with it ({Num(sent, "bootA_y")})"
            : "compiled: bootA_y never written, so the boot would stay behind");

        foreach (var slot in new[] { "player_t_0", "player_t_1", "ridgeFar_t_0", "domes_t_0" })
            check(sent.ContainsKey(slot), sent.ContainsKey(slot)
                ? $"compiled: {slot} = {Num(sent, slot)}"
                : $"compiled: {slot} never written");

        check(sent.TryGetValue("score", out var score) && score.Type == LuaValueType.String,
            sent.TryGetValue("score", out var s2) && s2.Type == LuaValueType.String
                ? $"compiled: score is a string slot, \"{s2.Read<string>()}\""
                : "compiled: score is not a string slot");

        // The compiler runs after the page's setup, so these are already in the geometry.
        var leaked = new[] { "field_h", "groundLine_y", "groundLine_h", "overlay_y" }.Where(sent.ContainsKey).ToList();
        check(leaked.Count == 0, leaked.Count == 0
            ? "compiled: setup-only writes send nothing at run time"
            : $"compiled: setup-only write(s) being sent every frame - {string.Join(", ", leaked)}");
    }

    /// <summary>
    /// What one class state draws.
    /// </summary>
    /// <remarks>
    /// In the mod this sets the class, re-runs layout and reads back the boxes that moved, because
    /// only the layout engine knows where an element lands. Here the page is not laid out, so the
    /// stylesheet answers instead - which is enough to check that the SHAPE is right: that every
    /// state the script can assign is enumerated, produces slot values, and reaches the payload.
    /// It is not a check on the numbers, which need the real layout.
    /// </remarks>
    private static CompiledPage.StateValues? State(string id, string cls,
        Dictionary<string, (double X, double Y, double W, double H)> boxes, ICollection<string> available)
    {
        var values = new CompiledPage.StateValues();
        // #player.duck moves the descendants; #player.hurt recolours one; neither touches the player
        if (id == "player")
        {
            var ducking = cls.Contains("duck", StringComparison.Ordinal);
            foreach (var part in new[] { "legA", "legB", "bootA", "bootB" })
                if (available.Contains(part + "_y") && boxes.TryGetValue(part, out var b))
                    values.Numbers.Add((part + "_y", ducking ? b.Y + 6 : b.Y));
            return values;
        }
        if (id == "score")
        {
            // #score.flash changes colour only
            if (available.Contains("score_f")) values.Text.Add(("score_f", cls == "flash" ? "#E2A94E" : "#F3E9DC"));
            return values;
        }
        // Any other element the scene knows: the state is producible, it just draws nothing different
        // in this stub. The real one lays the page out and reads what moved.
        var known = boxes.ContainsKey(id) || available.Contains(id + "_t_0") || available.Contains(id + "_f");
        return known ? values : null;
    }

    // ---- running it ---------------------------------------------------------------------------

    private static (Dictionary<string, LuaValue> Sent, LuaValue Snap) RunIt(string root, string lua, int frames)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        Chunk(state, File.ReadAllText(Path.Combine(root, "JsPrelude.lua")), "prelude");
        // a stand-in for the vector element, so the flush has somewhere to go
        Chunk(state, "VDATA = { set_props = function(self, t) SENT = t.data LAST_SNAP = t.snap end }", "element");
        Chunk(state, lua, "page");
        Chunk(state, Driver(frames), "frames");

        var sent = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
        var value = state.Environment["SENT"];
        if (value.Type == LuaValueType.Table)
        {
            var table = value.Read<LuaTable>();
            var key = LuaValue.Nil;
            while (table.TryGetNext(key, out var pair))
            {
                key = pair.Key;
                if (key.Type == LuaValueType.String) sent[key.Read<string>()] = pair.Value;
            }
        }
        return (sent, state.Environment["LAST_SNAP"]);
    }

    /// <summary>Drives the page's own callback, then hands one payload over - as the chip would.</summary>
    private static string Driver(int frames) => @"
for i = 1, " + frames.ToString(CultureInfo.InvariantCulture) + @" do
  local t = i * 16.6667
  if #Pending.frame > 0 then
    local fn = Pending.frame[#Pending.frame]
    Pending.frame = {}
    fn(t)
  else
    for _, timer in ipairs(Pending.timers) do timer.fn(t) end
  end
end
DOM.flush()";

    private static void Chunk(LuaState state, string text, string name) =>
        state.RunAsync(state.Load(text.AsSpan(), name, state.Environment)).AsTask().GetAwaiter().GetResult();

    private static double Num(Dictionary<string, LuaValue> d, string k) =>
        d.TryGetValue(k, out var v) && v.Type == LuaValueType.Number ? v.Read<double>() : double.NaN;

    // ---- the page's boxes, from the scene the game emitted --------------------------------------

    /// <summary>Every `id=` box in a scene, with its position and size. First line wins, as SceneSlots does.</summary>
    private static Dictionary<string, (double X, double Y, double W, double H)> Boxes(string scene)
    {
        var boxes = new Dictionary<string, (double, double, double, double)>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(scene, @"^\s*R x=(-?[\d.]+) y=(-?[\d.]+) w=(-?[\d.]+) h=(-?[\d.]+)[^\n]*? id=(\w+)", RegexOptions.Multiline))
        {
            var id = m.Groups[5].Value;
            if (!boxes.ContainsKey(id))
                boxes[id] = (D(m.Groups[1]), D(m.Groups[2]), D(m.Groups[3]), D(m.Groups[4]));
        }
        return boxes;

        static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// An element's box, as the compiler gets it from the cascade and layout. Every element this
    /// page drives is `#player div { position: absolute }`, so out of flow; the containing block is
    /// the player; and what is nested inside an element is read from the scene rather than assumed.
    /// </summary>
    private static DomSlots.Box? Box(string id, Dictionary<string, (double X, double Y, double W, double H)> boxes,
                                     ICollection<string> available)
    {
        var known = boxes.ContainsKey(id) || available.Contains(id) || available.Contains(id + "_t_0");
        if (!known) return null;

        var inside = new List<(string Id, double Dx, double Dy)>();
        if (id is "legA" or "legB")
        {
            var boot = id == "legA" ? "bootA" : "bootB";
            if (boxes.TryGetValue(id, out var self) && boxes.TryGetValue(boot, out var b))
                inside.Add((boot, b.X - self.X, b.Y - self.Y));
        }
        return new DomSlots.Box(outOfFlow: true, parentX: 0, parentY: PlayerTop, hasBackground: false, inside);
    }

    private static string ScenePath(string root)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "07-game.scene.txt");
        if (File.Exists(beside)) return beside;
        return Path.Combine(root, "..", "ScriptedScreensHtml.Tests", "07-game.scene.txt");
    }

    private static string? Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ScriptedScreensHtml");
            if (File.Exists(Path.Combine(candidate, "JsPrelude.lua"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}

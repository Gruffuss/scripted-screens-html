using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Acornima;
using Acornima.Ast;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScriptedScreensHtml;

/// <summary>
/// Compiles a page into what a person would hand-write for the vector mod: its scene, and a short Lua
/// program on the chip's own <c>tick</c> that writes the scene's values. Nothing of this mod runs
/// afterwards (CLAUDE.md, THE SPEC).
/// </summary>
/// <remarks>
/// Every DOM write the script makes is resolved HERE, at compile time, to the slot values it draws:
/// the page is changed as the script would change it, laid out, emitted, and diffed against what it
/// drew before. So a class that resizes a box, or a text that wraps and pushes the next line down,
/// becomes exactly the numbers that moved, and the Lua carries no DOM - only those numbers.
///
/// Takes the pages whose script it can resolve completely and returns null for the rest, which keep
/// the path they had. Today that is a script made only of <c>setTimeout</c> calls whose callbacks set
/// literal text or classes on elements named by a literal id.
/// ponytail: one shape of script so far; each next page widens <see cref="Timers"/>.
/// </remarks>
internal static class PlainPage
{
    /// <summary>The page's own vector elements: its scene, and the 1x1 element its values go through.</summary>
    internal const string SceneSuffix = "_s", DataSuffix = "_d";

    /// <summary>One DOM write, with its value already known: a text, or the element's whole class string after it.</summary>
    private sealed class Op
    {
        public readonly string Id;
        public readonly string? Text;
        public string? Class;
        public Op(string id, string? text, string? cls) { Id = id; Text = text; Class = cls; }
    }

    /// <summary>The page compiled plainly, or null when its script is outside what this resolves.</summary>
    internal static CompiledPage.Result? Compile(HtmlRenderer.Result built, Panel panel, Vector2 size,
                                                 (string Surface, string Element, string Scene)? target)
    {
        if (string.IsNullOrWhiteSpace(built.Script) || Timers(built) is not { } timers) return null;
        // A keyframe loop the scene cannot run on its own clock needs the interpreter's runner.
        foreach (var (element, spec) in built.Animations)
            if (!built.Keyframes.TryGetValue(spec.Name, out var frames) || !VectorEmitter.Compilable(frames, built.CssOf(element))) return null;

        var result = new CompiledPage.Result { Plain = true };
        var rest = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        var writes = new List<List<(string Slot, SceneSlots.Value Value)>>();
        string template;
        var classes = new Dictionary<VisualElement, string>();
        var texts = new Dictionary<Label, (string Text, List<HtmlNode> Children)>();
        lock (PageCompiler.Gate)
        {
            try
            {
                panel.Layout(size.x, size.y);
                template = PageCompiler.Emitted(built, panel, rest);
                var before = rest;
                foreach (var (_, ops) in timers)
                {
                    foreach (var op in ops)
                    {
                        var ve = built.ById[op.Id];
                        var node = built.NodeOf[ve];
                        if (op.Class != null)
                        {
                            if (!classes.ContainsKey(ve)) classes[ve] = node.Attr("class") ?? string.Empty;
                            built.Reclass(ve, op.Class);
                        }
                        else
                        {
                            // as the interpreter writes textContent (ScriptHost.ApplyText): the label and its node
                            var label = (Label)ve;
                            if (!texts.ContainsKey(label)) texts[label] = (label.text, new List<HtmlNode>(node.Children));
                            label.text = op.Text!;
                            node.Children.Clear();
                            node.Children.Add(new HtmlNode { Text = op.Text!, Parent = node });
                        }
                    }
                    panel.Layout(size.x, size.y);
                    var now = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
                    // A change that adds or removes a shape is a new structure, not a value.
                    if (PageCompiler.Emitted(built, panel, now) != template) return null;
                    var changed = new List<(string, SceneSlots.Value)>();
                    foreach (var pair in now)
                    {
                        var was = before[pair.Key];
                        if (was.Equals(pair.Value) || was.IsNumber && pair.Value.IsNumber && Math.Abs(was.Number - pair.Value.Number) <= 0.01f) continue;
                        changed.Add((pair.Key, pair.Value));
                    }
                    writes.Add(changed);
                    before = now;
                }
            }
            finally
            {
                // Compiling leaves the page exactly as it found it: it is still drawn from until installed.
                foreach (var pair in texts)
                {
                    pair.Key.text = pair.Value.Text;
                    var node = built.NodeOf[pair.Key];
                    node.Children.Clear();
                    node.Children.AddRange(pair.Value.Children);
                }
                foreach (var pair in classes) built.Reclass(pair.Key, pair.Value);
                panel.Layout(size.x, size.y);
            }
        }

        var written = new List<string>();
        foreach (var list in writes)
            foreach (var (slot, _) in list)
                if (!written.Contains(slot)) written.Add(slot);

        result.Structure = Literal(template, rest, written);
        result.StructureValues = new Dictionary<string, SceneSlots.Value>(StringComparer.Ordinal);
        foreach (var slot in written) result.StructureValues[slot] = rest[slot];
        result.Lua = Lua(result.Structure, result.StructureValues, Ease(built, written, rest, result.Warnings),
                         timers.Select(t => t.Seconds).ToList(), writes, target);
        return result;
    }

    // ---- the script --------------------------------------------------------------------------------

    /// <summary>
    /// The script as timers, each with the writes its callback makes, in the order they fire; null when
    /// any statement is something else. Class strings are followed through the program, so each
    /// write carries the whole class the element has after it.
    /// </summary>
    private static List<(double Seconds, List<Op> Ops)>? Timers(HtmlRenderer.Result built)
    {
        Script ast;
        try { ast = new Parser().ParseScript(built.Script); }
        catch (Exception) { return null; }

        var timers = new List<(double Seconds, List<Op> Ops)>();
        foreach (var statement in ast.Body)
        {
            if (statement is not ExpressionStatement { Expression: CallExpression { Callee: Identifier { Name: "setTimeout" } } call }
                || call.Arguments.Count is < 1 or > 2) return null;
            var delay = 0.0;
            if (call.Arguments.Count == 2)
            {
                if (call.Arguments[1] is not NumericLiteral ms) return null;
                delay = Math.Max(0, ms.Value) / 1000.0;
            }
            var body = call.Arguments[0] switch
            {
                FunctionExpression { Params.Count: 0 } f => f.Body.Body.ToList<Node>(),
                ArrowFunctionExpression { Params.Count: 0, Body: BlockStatement b } => b.Body.ToList<Node>(),
                ArrowFunctionExpression { Params.Count: 0, Body: Expression e } => new List<Node> { e },
                _ => null,
            };
            if (body == null) return null;
            var ops = new List<Op>();
            foreach (var n in body)
                if (Write(n is ExpressionStatement es ? es.Expression : n, built, ops) == false) return null;
            timers.Add((delay, ops));
        }
        if (timers.Count == 0) return null;
        // JavaScript fires equal delays in the order they were set: a stable sort keeps that.
        timers = timers.OrderBy(t => t.Seconds).ToList();

        // The class each element has after each write, followed in firing order.
        var classes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, ops) in timers)
            foreach (var op in ops)
            {
                if (op.Class == null) continue;
                if (!classes.TryGetValue(op.Id, out var list))
                    classes[op.Id] = list = (built.NodeOf[built.ById[op.Id]].Attr("class") ?? string.Empty)
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                var verb = op.Class.Substring(0, op.Class.IndexOf(' '));
                var name = op.Class.Substring(verb.Length + 1);
                switch (verb)
                {
                    case "=": list.Clear(); list.AddRange(name.Split(' ', StringSplitOptions.RemoveEmptyEntries)); break;
                    case "add": if (!list.Contains(name)) list.Add(name); break;
                    case "remove": list.Remove(name); break;
                    default: if (!list.Remove(name)) list.Add(name); break;   // toggle
                }
                op.Class = string.Join(" ", list);
            }
        return timers;
    }

    /// <summary>
    /// One statement of a callback as writes: <c>$(id).textContent = 'text'</c>, <c>.className = 'a b'</c>,
    /// <c>.classList.add/remove/toggle('a', ...)</c>. A class write is recorded as its verb and name
    /// here and turned into the whole class string by <see cref="Timers"/>. False for anything else.
    /// </summary>
    private static bool Write(Node n, HtmlRenderer.Result built, List<Op> ops)
    {
        switch (n)
        {
            case AssignmentExpression { Operator: Operator.Assignment, Left: MemberExpression { Computed: false, Property: Identifier p } target, Right: StringLiteral value }
                when Element(target.Object, built) is { } id:
                if (p.Name is "textContent" or "innerText")
                {
                    // the label itself, as a <p> or <span> of text is; anything else is a new shape
                    if (built.ById[id] is not Label) return false;
                    ops.Add(new Op(id, value.Value, null));
                    return true;
                }
                if (p.Name != "className") return false;
                ops.Add(new Op(id, null, "= " + value.Value));
                return true;

            case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier verb, Object: MemberExpression { Computed: false, Property: Identifier { Name: "classList" } } list } } call
                when verb.Name is "add" or "remove" or "toggle" && Element(list.Object, built) is { } owner
                     && call.Arguments.Count > 0 && (verb.Name != "toggle" || call.Arguments.Count == 1):
                foreach (var arg in call.Arguments)
                {
                    if (arg is not StringLiteral s || s.Value.Length == 0 || s.Value.Contains(' ')) return false;
                    ops.Add(new Op(owner, null, verb.Name + " " + s.Value));
                }
                return true;

            default:
                return false;
        }
    }

    /// <summary>The id <c>document.getElementById('id')</c> names, when the page has that element.</summary>
    private static string? Element(Expression e, HtmlRenderer.Result built)
        => e is CallExpression { Callee: MemberExpression { Computed: false, Object: Identifier { Name: "document" }, Property: Identifier { Name: "getElementById" } } } call
           && call.Arguments.Count == 1 && call.Arguments[0] is StringLiteral id
           && built.ById.TryGetValue(id.Value, out var ve) && ve != null && built.NodeOf.ContainsKey(ve)
            ? id.Value : null;

    // ---- the scene ---------------------------------------------------------------------------------

    /// <summary>
    /// The scene with every slot the program does not write put back as the literal it was, so what
    /// remains as <c>$name</c> is exactly what moves - as a hand-written scene reads.
    /// </summary>
    internal static string Literal(string template, Dictionary<string, SceneSlots.Value> values, List<string> keep)
    {
        var sb = new StringBuilder(template.Length);
        var quoted = false;
        var expression = false;
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '\\' && quoted && i + 1 < template.Length) { sb.Append(c).Append(template[++i]); continue; }
            if (c == '"')
            {
                quoted = !quoted;
                expression = quoted && i + 1 < template.Length && template[i + 1] == '=';
                sb.Append(c);
                continue;
            }
            if (c != '$') { sb.Append(c); continue; }
            var end = i + 1;
            while (end < template.Length && (char.IsLetterOrDigit(template[end]) || template[end] == '_')) end++;
            var name = template.Substring(i + 1, end - i - 1);
            if (name.Length == 0 || keep.Contains(name) || !values.TryGetValue(name, out var v))
                sb.Append(template, i, end - i);
            else if (v.IsNumber)
                sb.Append(expression && v.Number < 0f ? "(" + Num(v.Number) + ")" : Num(v.Number));
            else
                sb.Append(quoted ? v.Text!.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") : v.Text);
            i = end - 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Each written number's glide as the renderer's own <c>ease</c> entry: the transition its element
    /// declares for the property behind the slot, or 0 - a snap, as a browser changes a value with no
    /// transition at once. Colours and text never glide on the renderer, so a colour transition is said.
    /// ponytail: the transition is read from the element at rest, not from the state it moves to.
    /// </summary>
    private static List<(string Slot, string Entry)> Ease(HtmlRenderer.Result built, List<string> written,
                                                         Dictionary<string, SceneSlots.Value> rest, List<string> warnings)
    {
        var ease = new List<(string, string)>();
        foreach (var slot in written)
        {
            if (!rest[slot].IsNumber)
            {
                if (slot.EndsWith("_f", StringComparison.Ordinal) && ElementOf(built, slot.Substring(0, slot.Length - 2)) is { } painted
                    && (CssTransition.For(built.CssOf(painted), "background-color") ?? CssTransition.For(built.CssOf(painted), "color")) != null)
                    warnings.Add($"\"{painted.name}\" transitions a colour, which changes at once: the renderer glides numbers only");
                continue;
            }
            var entry = "0";
            foreach (var (suffix, property) in DataSlots.SlotProperty)
            {
                if (!slot.EndsWith(suffix, StringComparison.Ordinal)) continue;
                if (ElementOf(built, slot.Substring(0, slot.Length - suffix.Length)) is { } ve
                    && CssTransition.For(built.CssOf(ve), property) is { Dur: > 0f } t)
                    entry = "{ " + Num(t.Dur) + ", " + JsToLua.Quote(t.Curve) + (t.Delay > 0f ? ", " + Num(t.Delay) : string.Empty) + " }";
                break;
            }
            ease.Add((slot, entry));
        }
        return ease;
    }

    /// <summary>The element a slot's name comes from (<c>bar</c> for <c>bar_w</c>, and for a label's <c>bar__2_w</c>).</summary>
    private static VisualElement? ElementOf(HtmlRenderer.Result built, string name)
    {
        if (name.EndsWith(SceneSlots.SecondSuffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - SceneSlots.SecondSuffix.Length);
        foreach (var pair in built.ById)
            if (DomSlots.Slot(pair.Key) == name) return pair.Value;
        return null;
    }

    // ---- the program -------------------------------------------------------------------------------

    private static string Lua(string scene, Dictionary<string, SceneSlots.Value> opening,
                              List<(string Slot, string Entry)> ease, List<double> seconds,
                              List<List<(string Slot, SceneSlots.Value Value)>> writes,
                              (string Surface, string Element, string Scene)? target)
    {
        var (surface, element, sceneId) = target ?? ("main", "page", "html:page");
        // A long bracket no line of the scene closes: `stops=[[0,#fff],[1,#000]]` ends in `]]`.
        var level = "==";
        while (scene.Contains("]" + level + "]", StringComparison.Ordinal)) level += "=";

        var sb = new StringBuilder(scene.Length + 1024);
        sb.Append("-- Compiled once by ScriptedScreens Html. The scene is the page; this program moves its values\n");
        sb.Append("-- on the chip's own tick, as a hand-written vector console does.\n");
        // PageCompiler.Retarget finds this line by its exact text, to point a shared compile at one console.
        sb.Append("local SURFACE, ELEMENT, SCENE = ").Append(Quote(surface)).Append(", ").Append(Quote(element))
          .Append(", ").Append(Quote(sceneId)).Append("\n\n");
        sb.Append("local ui = ss.ui.surface(SURFACE)\n");
        sb.Append("ui:element({ id = ELEMENT .. \"").Append(SceneSuffix).Append("\", type = \"vector\", rect = ui:get(ELEMENT).rect,\n");
        sb.Append("  props = { scene = SCENE, src = [").Append(level).Append("[\n").Append(scene.TrimEnd('\n'))
          .Append("\n]").Append(level).Append("] } })\n");
        sb.Append("local D = {");
        var first = true;
        foreach (var pair in opening)
        {
            sb.Append(first ? " " : ", ").Append(Key(pair.Key)).Append(" = ").Append(Value(pair.Value));
            first = false;
        }
        sb.Append(first ? "}\n" : " }\n");
        sb.Append("local VDATA = ui:element({ id = ELEMENT .. \"").Append(DataSuffix).Append("\", type = \"vector\",\n");
        sb.Append("  rect = { unit = \"px\", x = -4, y = -4, w = 1, h = 1 }, props = { scene = SCENE, data = D } })\n");
        sb.Append("ui:commit()\n\n");

        sb.Append("local SEND = { data = D");
        if (ease.Count > 0)
        {
            sb.Append(", ease = {");
            for (var i = 0; i < ease.Count; i++) sb.Append(i == 0 ? " " : ", ").Append(Key(ease[i].Slot)).Append(" = ").Append(ease[i].Entry);
            sb.Append(" }");
        }
        sb.Append(" }\n");
        sb.Append("local TIMERS = {\n");
        for (var k = 0; k < seconds.Count; k++)
        {
            sb.Append("  { ").Append(Num(seconds[k])).Append(", function()");
            foreach (var (slot, value) in writes[k]) sb.Append(" D").Append(Key(slot, field: true)).Append(" = ").Append(Value(value));
            sb.Append(" end },\n");
        }
        sb.Append("}\n");
        sb.Append("local clock, due = 0, 1\n");
        sb.Append("local author_tick = tick\n");
        sb.Append("function tick(dt)\n");
        sb.Append("  clock = clock + dt\n");
        sb.Append("  local timer = TIMERS[due]\n");
        sb.Append("  if timer ~= nil and clock >= timer[1] then\n");
        sb.Append("    repeat\n");
        sb.Append("      timer[2]()\n");
        sb.Append("      due = due + 1\n");
        sb.Append("      timer = TIMERS[due]\n");
        sb.Append("    until timer == nil or clock < timer[1]\n");
        sb.Append("    VDATA:set_props(SEND)\n");
        sb.Append("    ui:commit()\n");
        sb.Append("  end\n");
        sb.Append("  if author_tick then return author_tick(dt) end\n");
        sb.Append("end\n");
        return sb.ToString();
    }

    /// <summary>A slot name as a table key: bare, unless it is a Lua keyword (an element with id "end").</summary>
    private static string Key(string name, bool field = false)
        => Keywords.Contains(name) ? "[\"" + name + "\"]" : field ? "." + name : name;

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    };

    private static string Value(SceneSlots.Value v) => v.IsNumber ? Num(v.Number) : JsToLua.Quote(v.Text ?? string.Empty);

    /// <summary>As PageCompiler.Retarget writes the target line: only `"` and `\` escaped, so its search matches.</summary>
    private static string Quote(string v) => "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Num(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e9
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("R", CultureInfo.InvariantCulture);

    private static string Num(float v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e9f
            ? ((long)v).ToString(CultureInfo.InvariantCulture)
            : v.ToString("R", CultureInfo.InvariantCulture);
}

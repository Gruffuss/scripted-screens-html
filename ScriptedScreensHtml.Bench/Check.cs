using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using ScriptedScreensHtml;

namespace ScriptedScreensHtml.Bench;

/// <summary>
/// Correctness checks that need Unity's types, so they cannot live in the headless test project.
/// Run at the start of every bench run: they are microseconds and they guard the assumption the
/// emit cache is built on.
/// </summary>
internal static class Check
{
    internal static int Run()
    {
        var failures = 0;

        // OffThread.Box.Same must compare every field that says what an element looks like. If it
        // misses one, an element whose only change is that field keeps last frame's cached text and
        // the console silently shows something stale. Reflection means a field added later is
        // covered without anyone remembering to add it here.
        var fields = typeof(OffThread.Box)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.GetCustomAttribute<OffThread.BookkeepingAttribute>() == null)
            .ToArray();

        foreach (var field in fields)
        {
            var a = new OffThread.Box();
            var b = new OffThread.Box();
            if (!Different(field, out var changed))
            {
                Console.WriteLine($"  FAIL  Box.Same: this check does not know how to change {field.Name} ({field.FieldType.Name})");
                failures++;
                continue;
            }
            field.SetValue(b, changed);
            if (a.Same(b))
            {
                Console.WriteLine($"  FAIL  Box.Same ignores {field.Name}: an element whose only change is that field would keep stale text");
                failures++;
            }
        }

        Console.WriteLine(failures == 0
            ? $"  PASS  Box.Same compares all {fields.Length} fields"
            : $"  {failures} FAILED of {fields.Length} fields");
        // A keyframe runner has to make progress when it is stepped, and then say when it next
        // wants stepping. The idle-frame gate asks NextDue and runs no page frame until then, so a
        // runner that always answers "now" costs a frame every frame, and one that answers too late
        // stops animating - and neither is visible in the code, only in a clock.
        {
            var ve = new VisualElement();
            var frames = new CssKeyframes { Name = "pulse" };
            frames.Frames.Add(new CssKeyframe { Percent = 0f, Declarations = { new CssDeclaration("background-color", "#8b1a1a") } });
            frames.Frames.Add(new CssKeyframe { Percent = 50f, Declarations = { new CssDeclaration("background-color", "#ff5c5c") } });
            frames.Frames.Add(new CssKeyframe { Percent = 100f, Declarations = { new CssDeclaration("background-color", "#8b1a1a") } });
            var spec = new AnimationSpec { Name = "pulse", Duration = 2f, Iterations = float.PositiveInfinity };
            var runner = new KeyframeRunner(ve, frames, spec, 0f, null);

            // The gate loop, exactly as HtmlSurface runs it: step only when the runner says it is due.
            var writes = 0; var steps = 0; var t = 0f;
            for (var frame = 0; frame < 600; frame++)   // ten seconds at 60 fps
            {
                t = frame / 60f;
                if (runner.NextDue(t) > t) continue;
                steps++;
                runner.Update(t);
                if (runner.Wrote) { runner.Wrote = false; writes++; }
            }
            // 2 s period, a write at each of 0%/50%/100%: about 10 writes in ten seconds, plus the
            // per-iteration snap. Far fewer means it stalled; hundreds means NextDue never closed.
            if (writes < 8 || writes > 40)
                { Console.WriteLine($"FAIL: keyframe runner wrote {writes} times in 10 s, expected about 10-20"); failures++; }
            if (steps > 120)
                { Console.WriteLine($"FAIL: keyframe runner asked for {steps} steps in 600 frames; NextDue is not gating"); failures++; }
        }

        // Compilable decides whether an animation runs on the scene clock or keeps a KeyframeRunner,
        // and getting it wrong in the generous direction is silent: the emitter simply never reaches
        // the branch that would paint the colour, and the animation disappears with no warning.
        {
            var pulse = new CssKeyframes { Name = "pulse" };
            pulse.Frames.Add(new CssKeyframe { Percent = 0f, Declarations = { new CssDeclaration("background-color", "#8b1a1a") } });
            pulse.Frames.Add(new CssKeyframe { Percent = 100f, Declarations = { new CssDeclaration("background-color", "#ff5c5c") } });

            var flat = new System.Collections.Generic.Dictionary<string, string> { ["background"] = "#8b1a1a" };
            if (!VectorEmitter.Compilable(pulse, flat))
                { Console.WriteLine("FAIL: a background-color animation on a flat background should run in the scene"); failures++; }

            foreach (var layered in new[]
                     {
                         new System.Collections.Generic.Dictionary<string, string> { ["background"] = "linear-gradient(#111,#222)" },
                         new System.Collections.Generic.Dictionary<string, string> { ["background-image"] = "url(a.png)" },
                     })
                if (VectorEmitter.Compilable(pulse, layered))
                    { Console.WriteLine("FAIL: a background-color animation over an image or gradient must keep its runner"); failures++; }

            var filter = new CssKeyframes { Name = "blur" };
            filter.Frames.Add(new CssKeyframe { Percent = 0f, Declarations = { new CssDeclaration("filter", "blur(0)") } });
            if (VectorEmitter.Compilable(filter, flat))
                { Console.WriteLine("FAIL: filter is not something the scene can run"); failures++; }
        }

        return failures;
    }

    /// <summary>A value that differs from the field's default, or false when the type is unknown here.</summary>
    private static bool Different(FieldInfo field, out object value)
    {
        var t = field.FieldType;
        value = null!;
        if (t == typeof(float)) { value = 1.5f; return true; }
        if (t == typeof(bool)) { value = true; return true; }
        if (t == typeof(int)) { value = 7; return true; }
        if (t == typeof(Rect)) { value = new Rect(1f, 2f, 3f, 4f); return true; }
        if (t == typeof(Color)) { value = new Color(0.25f, 0.5f, 0.75f, 1f); return true; }
        if (t == typeof(Vector2)) { value = new Vector2(1f, 2f); return true; }
        if (t == typeof(Vector3)) { value = new Vector3(1f, 2f, 3f); return true; }
        if (t == typeof(Scale)) { value = new Scale(new Vector2(2f, 3f)); return true; }
        if (t == typeof(Rotate)) { value = new Rotate(new Angle(45f, AngleUnit.Degree)); return true; }
        if (t == typeof(string)) { value = "different"; return true; }
        if (t.IsEnum)
        {
            var current = Enum.ToObject(t, 0);
            foreach (var candidate in Enum.GetValues(t))
                if (!candidate.Equals(current)) { value = candidate; return true; }
            return false;
        }
        return false;
    }
}

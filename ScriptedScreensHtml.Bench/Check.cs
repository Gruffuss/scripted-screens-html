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

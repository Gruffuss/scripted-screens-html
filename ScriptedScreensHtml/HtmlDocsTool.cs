using System;
using System.Linq;
using System.Text;

namespace ScriptedScreensHtml;

/// <summary>
/// Publishes the shipped docs and examples as StationeersLua MCP resources, the way ScriptedScreens
/// and ScriptedScreens Vector do: an `html` search scope, the guide and SUPPORT.md one resource
/// per section, the changelog, a quick start served as the index, and everything under examples/.
/// </summary>
/// <remarks>
/// Bound by reflection so the mod loads without StationeersLua. Split by section because the
/// search returns at most two hits per resource: a whole 25 KB file would answer every question
/// with its first two matches. Files are read from the mod folder when a resource is read, so the
/// docs never go stale against the DLL. Each call is optional: an older StationeersLua without one
/// of them loses just that part.
/// </remarks>
internal static class HtmlDocsTool
{
    private const string DocRoot = "stationeers://html/";
    /// <summary>The console mockups: whole pages of dense CSS, in a scope of their own so they do not
    /// outrank the support sections for every CSS property question in scope `html`.</summary>
    private const string MockupRoot = "stationeers://html-mockups/";

    internal static void TryRegister()
    {
        try
        {
            var registry = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("StationeersLua.LuaMcpRegistry", throwOnError: false))
                .FirstOrDefault(t => t != null);
            if (registry == null)
            {
                ScriptedScreensHtmlPlugin.Log?.LogInfo("StationeersLua MCP registry not present; html docs not registered.");
                return;
            }
            RegisterDocs(registry);
        }
        catch (Exception ex)
        {
            // Never fatal: the mod's job is drawing pages, and the docs are a convenience.
            ScriptedScreensHtmlPlugin.Log?.LogWarning($"html docs registration failed: {ex.Message}");
        }
    }

    private static void RegisterDocs(Type registry)
    {
        var folder = System.IO.Path.GetDirectoryName(typeof(HtmlDocsTool).Assembly.Location);
        if (string.IsNullOrEmpty(folder))
            return;

        var resource = registry.GetMethod("RegisterDocumentationResource",
            new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(Func<string>) });
        if (resource == null)
        {
            ScriptedScreensHtmlPlugin.Log?.LogInfo("StationeersLua has no documentation resources; html docs not registered.");
            return;
        }

        registry.GetMethod("RegisterDocumentationSearchScope", new[] { typeof(string), typeof(string) })
            ?.Invoke(null, new object[] { "html", DocRoot });

        var index = new StringBuilder("\nSearch everything with scope `html`, or read these URIs:\n\n");
        var count = 0;

        void Add(string key, string name, string description, string mime, Func<string> content)
        {
            resource.Invoke(null, new object[] { DocRoot + key, name, description, mime, content });
            index.Append("- `").Append(DocRoot).Append(key).Append("` -- ").Append(name).Append('\n');
            count++;
        }

        foreach (var (file, key, title, blurb, deep) in new[]
        {
            ("README.md", "guide", "Guide", "the authoring guide: the html element, showing real device data (reading devices in Lua, the data element), controls, CSS, fonts, SVG, JavaScript, diagnostics", false),
            ("SUPPORT.md", "support", "Support", "what works from HTML, CSS and JavaScript, what is accepted without effect, and what is still being finished", true),
        })
        {
            var path = System.IO.Path.Combine(folder, file);
            foreach (var (line, label, slug) in Sections(path, deep))
            {
                var wanted = line;
                var subsection = deep;
                Add($"{key}/{slug}", $"Html {title}: {label}", $"ScriptedScreens Html {blurb} -- section \"{label}\".",
                    "text/markdown", () => Section(path, wanted, subsection));
            }
        }

        Add("changelog", "Html Changelog", "ScriptedScreens Html release history, newest first.",
            "text/markdown", () => Read(System.IO.Path.Combine(folder, "CHANGELOG.md")));

        registry.GetMethod("RegisterDocumentationSearchScope", new[] { typeof(string), typeof(string) })
            ?.Invoke(null, new object[] { "html-mockups", MockupRoot });
        var mockups = System.IO.Path.Combine(folder, "mockups");
        foreach (var (file, what) in new[]
        {
            ("AtmoApple.lua", "an Apple-style regulator console: five screens, a page script with a simulation, theme and accent switches, scrolling lists"),
            ("AtmoDark.lua", "the same console in the Coldbench dark design system: token stylesheets, layered backgrounds, corner marks, blinking lamps"),
            ("AtmoLight.lua", "the same console in the Hardsuit light theme: heavy outlines, hazard tabs, a lit/dim theme attribute"),
        })
        {
            // a resource read is capped at 28000 characters: a page is served in numbered parts that each
            // fit, split on line boundaries, so the whole file can be read through the MCP
            var path = System.IO.Path.Combine(mockups, file);
            var parts = Parts(Read(path)).Count;
            for (var part = 1; part <= parts; part++)
            {
                var n = part;
                resource.Invoke(null, new object[] { MockupRoot + $"{file}/part{n}", $"Html mockup {file}, part {n} of {parts}",
                    $"A complete console written as a browser page (search scope html-mockups), part {n} of {parts}: " + what + ". TAB at the top picks the opening screen.",
                    "text/plain", new Func<string>(() => { var all = Parts(Read(path)); return n <= all.Count ? all[n - 1] : string.Empty; }) });
                count++;
            }
            index.Append("- `").Append(MockupRoot).Append(file).Append("/part1` to `part").Append(parts).Append("` -- ").Append(what).Append('\n');
        }

        index.Append("\nRunnable examples, one idea each: `").Append(DocRoot).Append("examples/index`. Whole consoles as pages: the three `").Append(MockupRoot).Append("` resources above (search scope `html-mockups`); each page is served in parts that fit a resource read; join them in order for the whole file, which is also in the mod's `mockups/` folder.\n");
        index.Append("A picture of a console: the `capture_scripted_screen` tool. Chip errors: `get_chip_errors`. Per-page cost and the exact scene text: the Diagnostics settings in the mod's config.\n");
        // The index is the quick start with the resource list appended: the one page an editor
        // needs before writing a first page, and the map to everything else.
        var list = index.ToString();
        var quickstart = System.IO.Path.Combine(folder, "QUICKSTART.md");
        resource.Invoke(null, new object[] { DocRoot + "index", "Html quick start and documentation index",
            "Start here: what the ScriptedScreens html element is, a page and data element that run as written, showing real device data, the rules that fail silently, how to check a page, and every documentation URI.",
            "text/markdown", new Func<string>(() => Read(quickstart) + list) });

        registry.GetMethod("RegisterBundledExampleDocumentation", new[] { typeof(string), typeof(string), typeof(string) })
            ?.Invoke(null, new object[] { folder!, DocRoot + "examples/", "ScriptedScreens Html" });

        ScriptedScreensHtmlPlugin.Log?.LogInfo($"registered {count + 1} html documentation resources and the examples");
    }

    private static string Read(string path)
    {
        try
        {
            return System.IO.File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return $"Could not read {path}: {ex.Message}";
        }
    }

    /// <summary>A text in pieces of at most 24000 characters, each cut after a newline.</summary>
    private static System.Collections.Generic.List<string> Parts(string text)
    {
        const int Max = 24000;
        var parts = new System.Collections.Generic.List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(text.Length, start + Max);
            if (end < text.Length)
            {
                var nl = text.LastIndexOf('\n', end - 1, end - start);
                if (nl > start) end = nl + 1;
            }
            parts.Add(text.Substring(start, end - start));
            start = end;
        }
        if (parts.Count == 0) parts.Add(string.Empty);
        return parts;
    }

    private static bool IsHeading(string line, bool deep)
    {
        return line.StartsWith("## ", StringComparison.Ordinal)
               || (deep && line.StartsWith("### ", StringComparison.Ordinal));
    }

    /// <summary>
    /// The sections of a markdown file: text before the first heading ("introduction"), then one
    /// per `##` heading -- and per `###` when <paramref name="deep"/> -- outside code fences.
    /// Each carries its heading line as the key, a readable label and a URI-safe slug.
    /// </summary>
    internal static System.Collections.Generic.List<(string Line, string Label, string Slug)> Sections(string path, bool deep)
    {
        var found = new System.Collections.Generic.List<(string, string, string)>();
        var used = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { "introduction" };
        var fence = false;
        var parent = "";

        foreach (var line in Read(path).Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                fence = !fence;

            if (fence || !IsHeading(line, deep))
            {
                if (found.Count == 0 && line.Trim().Length > 0)
                    found.Add(("", "Introduction", "introduction"));
                continue;
            }

            var text = line.TrimStart('#').Trim();
            var label = text;
            if (line.StartsWith("## ", StringComparison.Ordinal))
                parent = text;
            else if (parent.Length > 0)
                label = parent + " / " + text;

            var slugText = new StringBuilder();
            foreach (var c in label)
            {
                if (char.IsLetterOrDigit(c))
                    slugText.Append(char.ToLowerInvariant(c));
                else if (slugText.Length > 0 && slugText[slugText.Length - 1] != '-')
                    slugText.Append('-');
            }

            var slug = slugText.ToString().TrimEnd('-');
            var unique = slug.Length == 0 ? "section" : slug;
            for (var n = 2; !used.Add(unique); n++)
                unique = $"{slug}-{n}";

            found.Add((line, label, unique));
        }

        return found;
    }

    /// <summary>The section starting at heading line <paramref name="line"/> ("" for the introduction), up to the next heading.</summary>
    internal static string Section(string path, string line, bool deep)
    {
        var text = new StringBuilder();
        var fence = false;
        var inside = line.Length == 0;

        foreach (var current in Read(path).Split('\n'))
        {
            if (current.StartsWith("```", StringComparison.Ordinal))
                fence = !fence;

            if (!fence && IsHeading(current, deep))
            {
                if (inside)
                    break;
                inside = current == line;
            }

            if (inside)
                text.Append(current).Append('\n');
        }

        return text.Length > 0 ? text.ToString() : $"That section is no longer in {System.IO.Path.GetFileName(path)}.";
    }
}

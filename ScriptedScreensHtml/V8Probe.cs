using System;
using System.IO;
using System.Reflection;

namespace ScriptedScreensHtml;

/// <summary>
/// Does ClearScript's V8 load and run under the game's Mono, and what does a frame cost the managed
/// heap there? Measured outside the game it is 252 B/frame against Jint's 23,998 - the JS garbage
/// goes to V8's own native heap, where Unity's collector never sees it - but .NET 8 is not Mono and
/// a spike that tests the wrong host proves nothing. This answers it in the only place that counts.
///
/// Loaded entirely by reflection, so the mod keeps no compile-time dependency and an ordinary build
/// ships nothing extra: drop ClearScript's DLLs into the mod folder and turn the setting on.
/// Everything is wrapped - a failed probe reports and leaves the mod exactly as it was.
/// </summary>
internal static class V8Probe
{
    internal static void Run()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(V8Probe).Assembly.Location);
            if (dir == null) { Log("cannot locate the mod folder"); return; }

            var core = Path.Combine(dir, "ClearScript.Core.dll");
            var v8lib = Path.Combine(dir, "ClearScript.V8.dll");
            foreach (var f in new[] { core, v8lib })
                if (!File.Exists(f)) { Log($"not present: {f} - copy ClearScript's managed DLLs into the mod folder to probe"); return; }

            // BepInEx walks a mod folder recursively and tries to load every *.dll in it as a managed
            // assembly; a native one throws BadImageFormatException and takes the whole mod down with
            // it (measured twice - a subfolder does not help). So the native library ships under an
            // extension BepInEx ignores and is staged outside the scan path here, where ClearScript is
            // then pointed at it. This is the shipping arrangement, not a probe-only trick.
            var shipped = Path.Combine(dir, "ClearScriptV8.win-x64.dll.bin");
            if (!File.Exists(shipped)) { Log($"not present: {shipped} - the native library ships as .dll.bin so BepInEx leaves it alone"); return; }
            var nativeDir = Path.Combine(Path.GetTempPath(), "ScriptedScreensHtml.native");
            var native = Path.Combine(nativeDir, "ClearScriptV8.win-x64.dll");
            Directory.CreateDirectory(nativeDir);
            if (!File.Exists(native) || new FileInfo(native).Length != new FileInfo(shipped).Length)
                File.Copy(shipped, native, overwrite: true);

            var coreAsm = Assembly.LoadFrom(core);
            var v8Asm = Assembly.LoadFrom(v8lib);

            // ClearScript probes for its native DLL beside the assembly and under runtimes/win-x64/native;
            // under BepInEx neither is the process directory, so it is pointed at the mod folder outright.
            var settings = coreAsm.GetType("Microsoft.ClearScript.HostSettings");
            settings?.GetProperty("AuxiliarySearchPath", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, nativeDir);

            var engineType = v8Asm.GetType("Microsoft.ClearScript.V8.V8ScriptEngine");
            if (engineType == null) { Log("ClearScript.V8.dll carries no V8ScriptEngine"); return; }

            using var engine = (IDisposable)Activator.CreateInstance(engineType);
            var execute = engineType.GetMethod("Execute", new[] { typeof(string) });
            var evaluate = engineType.GetMethod("Evaluate", new[] { typeof(string) });
            if (execute == null || evaluate == null) { Log("no Execute/Evaluate on V8ScriptEngine"); return; }

            execute.Invoke(engine, new object[]
            {
                "var __sink = '';" +
                "function __frame(now){ var s = '';" +
                "  for (var i = 0; i < 25; i++) { var v = 50 + 40 * Math.sin(now * 0.7 + i * 0.31);" +
                "    s = 'width:' + v.toFixed(1) + '%;opacity:' + (v / 100).toFixed(3); }" +
                "  __sink = s; return s; }",
            });
            var first = evaluate.Invoke(engine, new object[] { "__frame(100.25)" });
            Log($"loaded and ran under Mono; a frame returns {first}");

            // The number the decision rests on: what one page frame costs the heap Unity collects.
            // Mono answers 0 for the per-thread counter, so this is the whole-process total, which is
            // why nothing else may run during it - it is a rough figure, read as an order of magnitude.
            for (var i = 0; i < 200; i++) evaluate.Invoke(engine, new object[] { "__frame(" + (1.0 + i * 0.016) + ")" });
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            const int n = 500;
            var before = GC.GetTotalMemory(false);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < n; i++) evaluate.Invoke(engine, new object[] { "__frame(" + (100.0 + i * 0.016) + ")" });
            sw.Stop();
            var after = GC.GetTotalMemory(false);
            Log($"{(after - before) / (double)n:N0} managed B per frame, {sw.Elapsed.TotalMilliseconds / n * 1000:F1} us per frame "
                + "(25 elements; outside the game V8 costs 252 B and Jint 23,998)");
        }
        catch (Exception ex)
        {
            // The whole point is to learn how it fails, so the inner exception is what gets logged:
            // a reflection call wraps the real one, and its message is the useless half.
            var real = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            Log($"FAILED: {real.GetType().Name}: {real.Message}");
        }
    }

    private static void Log(string message) => ScriptedScreensHtmlPlugin.Log?.LogInfo("html v8 probe: " + message);
}

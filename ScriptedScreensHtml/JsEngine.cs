using System;
using System.IO;
using System.Reflection;
using Jint;   // IsNumber/AsNumber and the Options builders are extension methods in this namespace

namespace ScriptedScreensHtml;

/// <summary>
/// The JavaScript engine behind a page, behind an interface small enough that swapping it is a
/// configuration line rather than a rewrite. Two implementations: Jint, a C# interpreter whose
/// garbage lands in the heap Unity collects, and V8 through ClearScript, whose garbage lands in a
/// native heap Unity never walks.
/// </summary>
/// <remarks>
/// Why this exists at all: measured across fifteen consoles, a page with its script stripped costs
/// the same as no page at all, while the same page with its script costs twenty times more. The
/// whole of what this mod contributes to collection pauses is the interpreter.
///
/// The interface is deliberately blunt - bind a delegate, run some text, call a function - because
/// everything richer differs between the two engines and would have to be written twice. Arrays in
/// particular do not survive: a host array is a JS array under Jint and is not one under
/// ClearScript, so no binding here passes one in either direction (see ScriptHost's Texts/Nums).
/// </remarks>
internal interface IJsEngine : IDisposable
{
    /// <summary>What to call this engine in a log line.</summary>
    string Name { get; }

    /// <summary>Make a host delegate callable from script under <paramref name="name"/>.</summary>
    void Bind(string name, Delegate fn);

    /// <summary>Run script text for its effects.</summary>
    void Execute(string script);

    /// <summary>Call a global function. Returns null when it returned nothing usable.</summary>
    object? Invoke(string name, params object?[] args);

    /// <summary>A returned value as a number, or 0 when it was not one. The two engines hand back different types for the same JS number.</summary>
    double AsNumber(object? value);

    /// <summary>A returned value as a boolean, false when it was not one.</summary>
    bool AsBool(object? value);
}

internal sealed class JintEngine : IJsEngine
{
    private readonly Jint.Engine _engine;

    public JintEngine()
    {
        _engine = new Jint.Engine(o =>
        {
            o.LimitRecursion(200);
            o.TimeoutInterval(TimeSpan.FromSeconds(15)); // a runaway script frame must not wedge the worker; generous because a game load stalls every thread for seconds
            o.Strict(false);
        });
        BindToFixed();
    }

    public string Name => "Jint";
    public void Bind(string name, Delegate fn) => _engine.SetValue(name, fn);
    public void Execute(string script) => _engine.Execute(script);
    public object? Invoke(string name, params object?[] args) => _engine.Invoke(name, args);

    public double AsNumber(object? value) => value is Jint.Native.JsValue v && v.IsNumber() ? v.AsNumber() : 0d;
    public bool AsBool(object? value) => value is Jint.Native.JsValue v && v.IsBoolean() && v.AsBoolean();

    public void Dispose() => _engine.Dispose();

    /// <summary>
    /// Number.prototype.toFixed, formatted by the host. Jint's own allocates in proportion to how
    /// many digits the double really has, and a page animating from a clock never has round values:
    /// measured at 59,602 bytes a frame for 25 calls against 4,216 here, and faster with it. Bound
    /// as a function on the prototype rather than a JS wrapper, which cost half the saving again.
    /// Digits outside 0..20 go back to the engine, so a page still sees the error a browser gives.
    ///
    /// V8 needs none of this: its own toFixed is correct and allocates nothing Unity can see.
    /// </summary>
    private void BindToFixed()
    {
        var prototype = _engine.Evaluate("Number.prototype").AsObject();
        var native = prototype.Get("toFixed");
        prototype.FastSetProperty("toFixed", new Jint.Runtime.Descriptors.PropertyDescriptor(
            new Jint.Runtime.Interop.ClrFunction(_engine, "toFixed", (self, args) =>
            {
                var digits = args.Length > 0 && !args[0].IsUndefined() ? (int)Jint.Runtime.TypeConverter.ToNumber(args[0]) : 0;
                if (digits < 0 || digits > 20)
                    return native is Jint.Native.Function.Function fn ? fn.Call(self, args) : Jint.Native.JsValue.Undefined;
                var value = self.IsNumber() ? self.AsNumber() : Jint.Runtime.TypeConverter.ToNumber(self);
                return JsNumber.ToFixed(value, digits);
            }), true, false, true));
    }
}

/// <summary>
/// V8 through ClearScript, loaded by reflection so an ordinary build carries no compile-time
/// dependency and a mod folder without ClearScript's assemblies simply falls back to Jint.
/// </summary>
internal sealed class V8Engine : IJsEngine
{
    private readonly object _engine;
    private readonly MethodInfo _execute, _invoke, _addHost;

    public string Name => "V8";

    private V8Engine(object engine, MethodInfo execute, MethodInfo invoke, MethodInfo addHost)
    {
        _engine = engine; _execute = execute; _invoke = invoke; _addHost = addHost;
    }

    /// <summary>Builds one, or returns null with the reason: the caller falls back to Jint rather than leaving a page dead.</summary>
    internal static IJsEngine? TryCreate(out string why)
    {
        why = string.Empty;
        try
        {
            var dir = Path.GetDirectoryName(typeof(V8Engine).Assembly.Location);
            if (dir == null) { why = "cannot locate the mod folder"; return null; }

            var core = Path.Combine(dir, "ClearScript.Core.dll");
            var v8lib = Path.Combine(dir, "ClearScript.V8.dll");
            foreach (var f in new[] { core, v8lib })
                if (!File.Exists(f)) { why = "not present: " + Path.GetFileName(f); return null; }

            // BepInEx walks a mod folder recursively and loads every *.dll in it as a managed
            // assembly; a native one throws BadImageFormatException and takes the whole mod down
            // with it (measured twice - a subfolder does not help). So the native library ships
            // under an extension BepInEx ignores and is staged outside the scan path here.
            // Outside BepInEx (the bench) the native library sits where ClearScript already looks,
            // so the staging is skipped and its own probing is left alone.
            var shipped = Path.Combine(dir, "ClearScriptV8.win-x64.dll.bin");
            string? nativeDir = null;
            if (File.Exists(shipped))
            {
                nativeDir = Path.Combine(Path.GetTempPath(), "ScriptedScreensHtml.native");
                var native = Path.Combine(nativeDir, "ClearScriptV8.win-x64.dll");
                Directory.CreateDirectory(nativeDir);
                if (!File.Exists(native) || new FileInfo(native).Length != new FileInfo(shipped).Length)
                    File.Copy(shipped, native, overwrite: true);
            }

            var coreAsm = Assembly.LoadFrom(core);
            var v8Asm = Assembly.LoadFrom(v8lib);

            // ClearScript probes beside its own assembly and under runtimes/win-x64/native; under
            // BepInEx neither is the process directory, so it is pointed at the staging folder.
            if (nativeDir != null)
                coreAsm.GetType("Microsoft.ClearScript.HostSettings")
                    ?.GetProperty("AuxiliarySearchPath", BindingFlags.Public | BindingFlags.Static)
                    ?.SetValue(null, nativeDir);

            var type = v8Asm.GetType("Microsoft.ClearScript.V8.V8ScriptEngine");
            if (type == null) { why = "ClearScript.V8.dll carries no V8ScriptEngine"; return null; }

            var engine = Activator.CreateInstance(type);
            if (engine == null) { why = "V8ScriptEngine would not construct"; return null; }

            var execute = type.GetMethod("Execute", new[] { typeof(string) });
            var invoke = type.GetMethod("Invoke", new[] { typeof(string), typeof(object[]) });
            var addHost = type.GetMethod("AddHostObject", new[] { typeof(string), typeof(object) });
            if (execute == null || invoke == null || addHost == null)
            {
                (engine as IDisposable)?.Dispose();
                why = "V8ScriptEngine is not the shape this expects (Execute/Invoke/AddHostObject)";
                return null;
            }
            return new V8Engine(engine, execute, invoke, addHost);
        }
        catch (Exception ex)
        {
            // A reflection call wraps the real exception and its own message is the useless half.
            var real = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            why = real.GetType().Name + ": " + real.Message;
            return null;
        }
    }

    public void Bind(string name, Delegate fn) => _addHost.Invoke(_engine, new object[] { name, fn });
    public void Execute(string script) => _execute.Invoke(_engine, new object[] { script });
    public object? Invoke(string name, params object?[] args) => _invoke.Invoke(_engine, new object[] { name, args });

    public double AsNumber(object? value) => value is IConvertible c && value is not string && value is not bool
        ? c.ToDouble(System.Globalization.CultureInfo.InvariantCulture) : 0d;
    public bool AsBool(object? value) => value is bool b && b;

    public void Dispose() => (_engine as IDisposable)?.Dispose();
}

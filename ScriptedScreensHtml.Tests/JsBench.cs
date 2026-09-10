using System;
using System.Diagnostics;
using Jint;
public static class JsBench
{
    public static void Run()
    {
        var engine = new Engine();
        var frames = 0; long cmds = 0;
        engine.SetValue("__log", new Action<string, string>((l, m) => Console.WriteLine("js " + l + ": " + m)));
        engine.SetValue("__has", new Func<string, bool>(id => true));
        engine.SetValue("__query", new Func<string, string[]>(sel => { var a = new string[16]; for (var i = 0; i < 16; i++) a[i] = "c" + i; return a; }));
        engine.SetValue("__setStyle", new Action<string, string, string>((a, b, c) => { }));
        engine.SetValue("__setText", new Action<string, string>((a, b) => { }));
        var htmlWrites = 0; engine.SetValue("__setHtml", new Action<string, string>((a, b) => { htmlWrites++; if (htmlWrites <= 2) Console.WriteLine("setHtml " + a + " = " + b); }));
        var classWrites = 0; engine.SetValue("__setClass", new Action<string, string>((a, b) => { classWrites++; }));
        engine.SetValue("__getAttr", new Func<string, string, string>((id, n) => n == "data-key" ? "o2" : n == "data-phase" ? (id.EndsWith("1") ? "liq" : "gas") : n == "width" ? "50" : n == "height" ? "120" : null));
        engine.SetValue("__setAttr", new Action<string, string, string>((a, b, c) => { }));
        engine.SetValue("__size", new Func<string, double[]>(id => new[] { 50.0, 120.0 }));
        engine.SetValue("__canvasFrame", new Action<string, double[], string[], int>((id, c, cols, n) => { cmds += n; }));
        engine.SetValue("__now", new Func<double>(() => 0));
        engine.Execute(System.IO.File.ReadAllText("prelude.js"));
        engine.Execute(System.IO.File.ReadAllText("gaspage.js"));
        engine.Invoke("__emit", "data", "{\"o2\":{\"gasMol\":412.6,\"gasKpa\":2841,\"gasT\":21.4,\"liqL\":12.4,\"liqKpa\":5210,\"liqT\":-118.6,\"molIn\":2.41,\"molOut\":0.83,\"gasFill\":0.62,\"liqFill\":0.31,\"drift\":1}}");
        Console.WriteLine($"after data event: {htmlWrites} innerHTML writes, {classWrites} className writes");
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 30; i++) { engine.Invoke("__tick", i * 16.7); frames++; }
        sw.Stop();
        Console.WriteLine($"{frames} frames: {sw.Elapsed.TotalMilliseconds / frames:F2} ms per frame on .NET 8 (Mono is 2-4x slower), {cmds / frames} command numbers per frame");
    }
}

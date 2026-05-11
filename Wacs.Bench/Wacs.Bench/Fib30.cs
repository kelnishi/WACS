// fib(30) dispatch-mode comparison. Recursive fib — ~2.7M call/return pairs,
// dominated by call dispatch and i32 arithmetic. Five trials per mode, fresh
// runtime per trial. Phases reported (mean ± stddev, milliseconds):
//
//   parse     BinaryModuleParser.ParseWasm
//   inst      new WasmRuntime + InstantiateModule
//   xpile     (transpiler only) ModuleTranspiler.Transpile
//   activate  (transpiler) Activator.CreateInstance(ModuleClass)
//             (interpreter) runtime.CreateStackInvoker
//   first     first invoke (cold JIT for dispatch path / transpiled CIL)
//   warm      second invoke (hot path)
//   TOTAL     parse + inst [+ xpile] + activate + first

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;

internal static class Fib30
{
    private const int N = 30;
    private const int Trials = 20;
    private const int WarmupPasses = 5;

    private struct Phase
    {
        public double Parse;
        public double Inst;
        public double Xpile;     // 0 on interpreter rows
        public double Activate;
        public double First;
        public double Warm;
    }

    public static int Run(string[] args)
    {
        var here = AppContext.BaseDirectory;
        var wasmPath = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "fib.wasm"));
        if (!File.Exists(wasmPath))
            wasmPath = Path.Combine(here, "fib.wasm");
        var bytes = File.ReadAllBytes(wasmPath);

        Console.WriteLine($"fib-rec({N}) full-phase comparison");
        Console.WriteLine($"  trials per mode: {Trials} (fresh runtime per trial)");
        Console.WriteLine($"  warmup passes:   {WarmupPasses} per mode before timing");
        Console.WriteLine($"  units:           milliseconds (ms), shown as mean ± stddev");
        Console.WriteLine();

        // JIT-warm every dispatch path's own .NET code before the timed
        // trials so stddev reflects steady-state run-to-run noise rather
        // than trial-0's CLR tier-up cost for the parser/runtime itself.
        for (int w = 0; w < WarmupPasses; w++)
        {
            RunInterpreter(bytes, useSwitch: false, super: false);
            RunInterpreter(bytes, useSwitch: false, super: true);
            RunInterpreter(bytes, useSwitch: true,  super: false);
            RunInterpreter(bytes, useSwitch: true,  super: true);
            RunTranspiler(bytes);
        }

        Bench("poly",         bytes, () => RunInterpreter(bytes, useSwitch: false, super: false));
        Bench("poly+super",   bytes, () => RunInterpreter(bytes, useSwitch: false, super: true));
        Bench("switch",       bytes, () => RunInterpreter(bytes, useSwitch: true,  super: false));
        Bench("switch+super", bytes, () => RunInterpreter(bytes, useSwitch: true,  super: true));
        Bench("transpiler",   bytes, () => RunTranspiler(bytes));
        return 0;
    }

    private static void Bench(string label, byte[] bytes, Func<Phase> oneTrial)
    {
        var samples = new Phase[Trials];
        for (int i = 0; i < Trials; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            samples[i] = oneTrial();
        }
        var parse    = Stat(samples, s => s.Parse);
        var inst     = Stat(samples, s => s.Inst);
        var xpile    = Stat(samples, s => s.Xpile);
        var activate = Stat(samples, s => s.Activate);
        var first    = Stat(samples, s => s.First);
        var warm     = Stat(samples, s => s.Warm);
        var total    = Stat(samples, s => s.Parse + s.Inst + s.Xpile + s.Activate + s.First);

        Console.WriteLine($"  === {label} ===");
        PrintRow("parse",    parse);
        PrintRow("inst",     inst);
        if (xpile.Mean > 0) PrintRow("xpile", xpile);
        PrintRow("activate", activate);
        PrintRow("first",    first);
        PrintRow("warm",     warm);
        PrintRow("TOTAL",    total);
        Console.WriteLine();
    }

    private static (double Mean, double Sd, double Min, double Max) Stat(
        Phase[] xs, Func<Phase, double> sel)
    {
        var ys = xs.Select(sel).ToArray();
        double mean = ys.Average();
        double var = ys.Sum(y => (y - mean) * (y - mean)) / ys.Length;
        return (mean, Math.Sqrt(var), ys.Min(), ys.Max());
    }

    private static void PrintRow(string phase,
        (double Mean, double Sd, double Min, double Max) s)
    {
        Console.WriteLine(
            $"    {phase,-10}  mean {s.Mean,10:F3}   sd {s.Sd,8:F3}   "
            + $"min {s.Min,10:F3}   max {s.Max,10:F3}");
    }

    private static Phase RunInterpreter(byte[] bytes, bool useSwitch, bool super)
    {
        var p = new Phase();
        var sw = new Stopwatch();

        sw.Restart();
        Wacs.Core.Module module;
        using (var ms = new MemoryStream(bytes))
            module = BinaryModuleParser.ParseWasm(ms);
        sw.Stop();
        p.Parse = ToMs(sw);

        sw.Restart();
        var runtime = new WasmRuntime();
        runtime.UseSwitchRuntime = useSwitch;
        if (useSwitch)
            runtime.ExecContext.Attributes.UseSwitchSuperInstructions = super;
        else
            runtime.SuperInstruction = super;
        var modInst = runtime.InstantiateModule(module,
            new RuntimeOptions { SkipModuleValidation = true });
        runtime.RegisterModule("bench", modInst);
        sw.Stop();
        p.Inst = ToMs(sw);

        if (!runtime.TryGetExportedFunction(("bench", "fib-rec"), out var addr))
            throw new Exception("fib-rec not exported");

        sw.Restart();
        var invoker = runtime.CreateStackInvoker(addr,
            new InvokerOptions { SynchronousExecution = true });
        sw.Stop();
        p.Activate = ToMs(sw);

        var argv = new[] { new Value(N) };

        sw.Restart();
        invoker(argv);
        sw.Stop();
        p.First = ToMs(sw);

        sw.Restart();
        invoker(argv);
        sw.Stop();
        p.Warm = ToMs(sw);

        return p;
    }

    private static Phase RunTranspiler(byte[] bytes)
    {
        var p = new Phase();
        var sw = new Stopwatch();

        sw.Restart();
        Wacs.Core.Module module;
        using (var ms = new MemoryStream(bytes))
            module = BinaryModuleParser.ParseWasm(ms);
        sw.Stop();
        p.Parse = ToMs(sw);

        sw.Restart();
        var runtime = new WasmRuntime();
        var modInst = runtime.InstantiateModule(module,
            new RuntimeOptions { SkipModuleValidation = true });
        sw.Stop();
        p.Inst = ToMs(sw);

        sw.Restart();
        var transpiler = new ModuleTranspiler();
        var result = transpiler.Transpile(modInst, runtime, "Bench");
        sw.Stop();
        p.Xpile = ToMs(sw);

        if (result.ModuleClass == null)
            throw new Exception("transpiler produced no Module class");

        sw.Restart();
        var instance = Activator.CreateInstance(result.ModuleClass);
        sw.Stop();
        p.Activate = ToMs(sw);

        var exportMeta = result.ExportMethods.FirstOrDefault(m => m.WasmName == "fib-rec")
            ?? throw new Exception("fib-rec not exported in transpiled module");
        var method = result.ModuleClass.GetMethod(exportMeta.Name,
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new Exception($"method {exportMeta.Name} not found");
        var argv = new object[] { N };

        sw.Restart();
        try { method.Invoke(instance, argv); }
        catch (TargetInvocationException tie) when (tie.InnerException != null) { throw tie.InnerException; }
        sw.Stop();
        p.First = ToMs(sw);

        sw.Restart();
        try { method.Invoke(instance, argv); }
        catch (TargetInvocationException tie) when (tie.InnerException != null) { throw tie.InnerException; }
        sw.Stop();
        p.Warm = ToMs(sw);

        return p;
    }

    private static double ToMs(Stopwatch sw)
        => sw.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
}

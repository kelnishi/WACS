// End-to-end transpiler benchmark. Drives the transpile pipeline in-
// process (ModuleTranspiler → ModuleClass → Activator.CreateInstance),
// then invokes the transpiled export in a tight loop. Bypasses the
// Wacs.Console CLI which doesn't propagate function args through the
// --engine transpiler path today, so wall time would otherwise be
// dominated by JIT + Reflection.Emit setup with the loop never
// running.
//
// Workload: fib-iter from fib.wasm — pure i32 arithmetic + br_if loop
// condition. Hits the compare/branch fusion peephole on every iteration
// (the i32.ge_s + br_if at loop head). Doesn't exercise the calli
// call_indirect path; that one's measured by CallIndirectBench against
// a synthetic target.
//
// Run with:
//   dotnet run -c Release -- transpiler

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;

namespace Wacs.Bench
{

internal static class TranspilerBench
{
    public static int Run(string[] args)
    {
        var here = AppContext.BaseDirectory;
        var fibWasm = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "fib.wasm"));
        var bytes = File.ReadAllBytes(fibWasm);

        Console.WriteLine("Transpiler end-to-end bench (fib.wasm hot loops)");
        Console.WriteLine();

        // Iteration counts tuned to land each row in the 0.5–3 second
        // band on an M-series Mac so JIT + startup noise doesn't
        // dominate. Bump if your machine eats them.
        TimeWorkload("fib-iter   N=  1B", bytes, "fib", 1_000_000_000);
        TimeWorkload("fac-iter   N= 10M", bytes, "fac",        10_000_000);
        TimeWorkload("sum        N=  1B", bytes, "sum", 1_000_000_000);

        // call_indirect-heavy workload: tight loop dispatching through
        // a 4-slot funcref table every iteration. Wall time should be
        // dominated by indirect-call cost — the calli path's target.
        var dispatchPath = Path.GetFullPath(
            Path.Combine(here, "dispatch.wasm"));
        if (File.Exists(dispatchPath))
        {
            var dispatchBytes = File.ReadAllBytes(dispatchPath);
            TimeWorkload("dispatch   N=100M", dispatchBytes, "run", 100_000_000);
        }
        var dispatchBigPath = Path.GetFullPath(
            Path.Combine(here, "dispatch-bigbody.wasm"));
        if (File.Exists(dispatchBigPath))
        {
            var bigBytes = File.ReadAllBytes(dispatchBigPath);
            TimeWorkload("dispatch-bb N= 50M", bigBytes, "run", 50_000_000);
        }

        Console.WriteLine();
        Console.WriteLine("Note: JIT warmup is included in cold; warm reuses the same module.");
        return 0;
    }

    private static void TimeWorkload(string label, byte[] wasm, string export, int arg)
    {
        // Parse + transpile fresh each row so the cold number includes
        // the full pipeline cost. Warm reuses the activated instance.
        var module = BinaryModuleParser.ParseWasm(new MemoryStream(wasm));
        var runtime = new WasmRuntime();
        var modInst = runtime.InstantiateModule(module,
            new RuntimeOptions { SkipModuleValidation = true });

        var transpiler = new ModuleTranspiler("Wacs.Bench.Transpiler",
            new TranspilerOptions());
        var result = transpiler.Transpile(modInst, runtime, "BenchMod");
        var instance = Activator.CreateInstance(result.ModuleClass!)!;
        var em = result.ExportMethods.First(m => m.WasmName == export);
        var method = result.ModuleClass!.GetMethod(em.Name,
            BindingFlags.Public | BindingFlags.Instance)!;
        var argArr = new object[] { arg };

        // Warm: run once to JIT the typed dispatch + the transpiled
        // function body itself. Subsequent invocations are steady-
        // state.
        method.Invoke(instance, argArr);

        const int trials = 5;
        var times = new double[trials];
        for (int t = 0; t < trials; t++)
        {
            var sw = Stopwatch.StartNew();
            method.Invoke(instance, argArr);
            sw.Stop();
            times[t] = sw.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        double median = times[trials / 2];
        double min = times[0];
        double max = times[trials - 1];
        Console.WriteLine($"  {label}  median={median,7:F1} ms   min={min,7:F1}   max={max,7:F1}");
    }
}

}

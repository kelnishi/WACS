// Minimal WASM dispatch benchmark. Runs a handful of workloads through both
// the polymorphic and switch runtimes, records wall time, prints a table.
//
// Not rigorous (no BenchmarkDotNet, no warmup stabilization past a single call,
// no GC forcing). Rough first-order numbers to guide generator work.

using System.Diagnostics;
using Wacs.Core;
using Wacs.Core.Runtime;

static long RunOne(string wasmPath, bool useSwitch, Bench b)
{
    var bytes = File.ReadAllBytes(wasmPath);
    using var ms = new MemoryStream(bytes);
    var module = BinaryModuleParser.ParseWasm(ms);

    var runtime = new WasmRuntime();
    runtime.UseSwitchRuntime = useSwitch;

    var modInst = runtime.InstantiateModule(module,
        new RuntimeOptions { SkipModuleValidation = true });
    runtime.RegisterModule("bench", modInst);

    if (!runtime.TryGetExportedFunction(("bench", b.Function), out var addr))
        throw new Exception($"function {b.Function} not exported");

    var invoker = runtime.CreateStackInvoker(addr,
        new InvokerOptions { SynchronousExecution = true });

    var argArr = new Value[] { new Value((int)b.Arg) };

    // One warmup call so compile-on-demand + JIT tiering settle.
    invoker(argArr);

    var sw = Stopwatch.StartNew();
    for (int i = 0; i < b.Repeats; i++)
        invoker(argArr);
    sw.Stop();
    return sw.ElapsedMilliseconds;
}

static void RunBench(string wasmPath, Bench b)
{
    long polyMs = RunOne(wasmPath, useSwitch: false, b);
    long switchMs = RunOne(wasmPath, useSwitch: true, b);
    double ratio = (double)switchMs / polyMs;
    Console.WriteLine($"  {b.Name,-20} poly={polyMs,6}ms   switch={switchMs,6}ms   ratio={ratio,5:F2}x");
}

var here = AppContext.BaseDirectory;
var fibWasm = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "fib.wasm"));

Console.WriteLine($"WASM dispatch micro-benchmarks");
Console.WriteLine($"  {"workload",-20} {"polymorphic",-12}  {"switch",-12}  {"ratio",-8}");
Console.WriteLine($"  ------------------------------------------------------------");

// Tight loop — tests dispatch throughput on i32 arith + branches.
RunBench(fibWasm, new Bench("fib-iter(5M)", "fib",     5_000_000, Repeats: 3));
// Exponential recursion — tests call / frame setup dominance.
RunBench(fibWasm, new Bench("fib-rec(25)",  "fib-rec",         25, Repeats: 3));
// i64 accumulate — different dispatch case, fewer branches.
RunBench(fibWasm, new Bench("sum(5M)",      "sum",     5_000_000, Repeats: 3));

return 0;

record Bench(string Name, string Function, long Arg, int Repeats);

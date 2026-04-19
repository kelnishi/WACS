// Minimal WASM dispatch benchmark. Runs a handful of workloads through both
// the polymorphic and switch runtimes, records wall time, prints a table.
//
// Not rigorous (no BenchmarkDotNet, no warmup stabilization past a single call,
// no GC forcing). Rough first-order numbers to guide generator work.

using System.Diagnostics;
using Wacs.Core;
using Wacs.Core.Runtime;

static long RunOne(string wasmPath, bool useSwitch, bool superInst, bool useMinimal, bool switchSuper, Bench b)
{
    var bytes = File.ReadAllBytes(wasmPath);
    using var ms = new MemoryStream(bytes);
    var module = BinaryModuleParser.ParseWasm(ms);

    var runtime = new WasmRuntime();
    runtime.UseSwitchRuntime = useSwitch;
    runtime.SuperInstruction = superInst;
    Wacs.Core.Compilation.SwitchRuntime.UseMinimal = useMinimal;
    // Switch-runtime-side superinstructions (phase G StreamFusePass). Orthogonal
    // to polymorphic's SuperInstruction — different fusion rules, different cost
    // model. Configured via the same WasmRuntime's ExecContext.Attributes.
    runtime.ExecContext.Attributes.UseSwitchSuperInstructions = switchSuper;

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
    long polyMs   = RunOne(wasmPath, useSwitch: false, superInst: false, useMinimal: false, switchSuper: false, b);
    long superMs  = RunOne(wasmPath, useSwitch: false, superInst: true,  useMinimal: false, switchSuper: false, b);
    long switchMs = RunOne(wasmPath, useSwitch: true,  superInst: false, useMinimal: false, switchSuper: false, b);
    long swFuseMs = RunOne(wasmPath, useSwitch: true,  superInst: false, useMinimal: false, switchSuper: true,  b);
    long minMs    = RunOne(wasmPath, useSwitch: true,  superInst: false, useMinimal: true,  switchSuper: false, b);
    Console.WriteLine($"  {b.Name,-18} poly={polyMs,5}   super={superMs,5}   switch={switchMs,5}   swFuse={swFuseMs,5}   min={minMs,5}   " +
                      $"fuse/switch={(double)swFuseMs/switchMs,5:F2}x   min/super={(double)minMs/superMs,5:F2}x");
}

var here = AppContext.BaseDirectory;
var fibWasm = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "fib.wasm"));

Console.WriteLine($"WASM dispatch micro-benchmarks");
Console.WriteLine($"  {"workload",-20} {"polymorphic",-12}  {"switch",-12}  {"ratio",-8}");
Console.WriteLine($"  ------------------------------------------------------------");

// Tight loop — tests dispatch throughput on i32 arith + branches.
RunBench(fibWasm, new Bench("fib-iter(5M)", "fib",     5_000_000, Repeats: 3));
// mul-heavy loop — fac iterates 20 times per call, same dispatch mix as fib.
RunBench(fibWasm, new Bench("fac(20)x250k", "fac",            20, Repeats: 250_000));
// i64 accumulate — different dispatch case, fewer branches.
RunBench(fibWasm, new Bench("sum(5M)",      "sum",     5_000_000, Repeats: 3));

return 0;

record Bench(string Name, string Function, long Arg, int Repeats);

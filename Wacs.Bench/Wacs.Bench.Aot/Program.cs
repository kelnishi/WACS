// AOT-only cold-start bench. References only Wacs.Core (no transpiler,
// no component-model). Goal: prove an embedder can ship an
// interpreter-only WACS as a NativeAOT binary, and measure what that
// buys for cold start.
//
// Same workload as Wacs.Bench's Coldstart.cs interpreter rows so the
// numbers line up. Run with:
//
//   dotnet publish Wacs.Bench.Aot -c Release -r osx-arm64 \
//       -p:PublishAot=true -o /tmp/wacs-bench-aot
//   /tmp/wacs-bench-aot/Wacs.Bench.Aot

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.Runtime;

return args.Length >= 2 && args[0] == "run"
    ? RunChild(args[1])
    : RunFreshProcesses();

static int RunFreshProcesses()
{
    Console.WriteLine("Coldstart benchmark — fib.wasm — fresh process per runtime (AOT build)");
    Console.WriteLine("  trials per cell: 7 (trial 0 = 'first', subsequent 6 reported as median)");
    Console.WriteLine("  units: microseconds (us)");
    Console.WriteLine();

    var dotnet = Environment.ProcessPath ?? "dotnet";
    foreach (var runtime in new[] { "interp-poly", "interp-switch" })
    {
        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add(runtime);
        using var p = Process.Start(psi)!;
        Console.Write(p.StandardOutput.ReadToEnd());
        p.WaitForExit();
    }
    return 0;
}

static int RunChild(string runtime)
{
    var bytes = File.ReadAllBytes(LocateFib());
    foreach (var arg in new[] { 100, 5_000_000 })
    {
        Console.WriteLine($"=== {runtime} fib({arg:N0}) ({bytes.Length}-byte module) ===");
        Header();
        switch (runtime)
        {
            case "interp-poly":   Measure(runtime, bytes, arg, useSwitch: false); break;
            case "interp-switch": Measure(runtime, bytes, arg, useSwitch: true);  break;
            default: Console.Error.WriteLine($"unknown runtime: {runtime}"); return 2;
        }
        Console.WriteLine();
    }
    return 0;
}

static string LocateFib()
{
    var here = AppContext.BaseDirectory;
    var sideBySide = Path.Combine(here, "fib.wasm");
    if (File.Exists(sideBySide)) return sideBySide;
    var devTree = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "..", "Wacs.Bench", "fib.wasm"));
    if (File.Exists(devTree)) return devTree;
    throw new FileNotFoundException(
        $"fib.wasm not found next to the binary or in the dev tree (looked at {sideBySide} and {devTree})");
}

static void Header()
{
    Console.WriteLine($"  {"runtime",-15}  {"phase",-10}  {"first(us)",10}  {"subseq-med(us)",16}");
    Console.WriteLine($"  ----------------------------------------------------------------");
}

const int Trials = 7;

static void Measure(string name, byte[] bytes, int arg, bool useSwitch)
{
    var trials = new (double parse, double inst, double first, double warm)[Trials];
    for (int i = 0; i < Trials; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        trials[i] = RunOne(bytes, arg, useSwitch);
    }
    var subseq = trials.Skip(1).ToArray();
    Print(name, "parse", trials[0].parse, Median(subseq.Select(t => t.parse).ToArray()));
    Print(name, "inst",  trials[0].inst,  Median(subseq.Select(t => t.inst).ToArray()));
    Print(name, "first", trials[0].first, Median(subseq.Select(t => t.first).ToArray()));
    Print(name, "warm",  trials[0].warm,  Median(subseq.Select(t => t.warm).ToArray()));
    double total0 = trials[0].parse + trials[0].inst + trials[0].first;
    double totalMed = Median(subseq.Select(t => t.parse + t.inst + t.first).ToArray());
    Print(name, "TOTAL", total0, totalMed);
}

static void Print(string name, string phase, double first, double subseq)
    => Console.WriteLine($"  {name,-15}  {phase,-10}  {first,10:F1}  {subseq,16:F1}");

static double Median(double[] xs)
{
    Array.Sort(xs);
    int n = xs.Length;
    return n % 2 == 1 ? xs[n / 2] : (xs[n / 2 - 1] + xs[n / 2]) / 2.0;
}

static (double parse, double inst, double first, double warm) RunOne(byte[] bytes, int arg, bool useSwitch)
{
    var sw = new Stopwatch();
    sw.Restart();
    Wacs.Core.Module module;
    using (var ms = new MemoryStream(bytes))
        module = BinaryModuleParser.ParseWasm(ms);
    sw.Stop();
    var parse = ToUs(sw);

    sw.Restart();
    var runtime = new WasmRuntime();
    runtime.UseSwitchRuntime = useSwitch;
    var modInst = runtime.InstantiateModule(module,
        new RuntimeOptions { SkipModuleValidation = true });
    runtime.RegisterModule("bench", modInst);
    sw.Stop();
    var inst = ToUs(sw);

    if (!runtime.TryGetExportedFunction(("bench", "fib"), out var addr))
        throw new Exception("fib not exported");
    var invoker = runtime.CreateStackInvoker(addr,
        new InvokerOptions { SynchronousExecution = true });
    var argArr = new[] { new Value(arg) };

    sw.Restart();
    invoker(argArr);
    sw.Stop();
    var first = ToUs(sw);

    sw.Restart();
    invoker(argArr);
    sw.Stop();
    var warm = ToUs(sw);

    return (parse, inst, first, warm);
}

static double ToUs(Stopwatch sw)
    => sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency;

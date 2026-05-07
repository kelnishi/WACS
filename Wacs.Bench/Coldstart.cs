// Coldstart micro-benchmark. Measures how long each runtime takes to go
// from a wasm byte buffer to a returned value on the first invocation.
//
// Phases measured per trial:
//   parse    BinaryModuleParser.ParseWasm
//   inst     WasmRuntime ctor + InstantiateModule
//   xpile    (transpiler only) ModuleTranspiler.Transpile
//   activate (transpiler only) Activator.CreateInstance(ModuleClass)
//   first    first invoke (cold JIT for generated/dispatched code)
//   warm     second invoke (hot JIT)
//
// Trial 0 is reported separately ("first") as the closest in-process proxy
// for true cold start; trials 1..N median is reported as "subsequent" since
// in-process JIT for runtime code itself warms up after the first trial.
//
// Workload is fib.wasm exporting fib(n). We run two variants:
//   fib(100)         — trivial body, exposes startup overhead
//   fib(5_000_000)   — long body, exposes per-op dispatch cost on first call

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Transpiler.AOT;

internal static class Coldstart
{
    // Top-level entry. Dispatches based on optional second arg:
    //   coldstart                  -> spawns one child per runtime (true cold per runtime)
    //   coldstart in-process       -> measures all three in this process (shared JIT warmup)
    //   coldstart run <runtime>    -> child-process mode; emits one runtime's numbers
    public static int Run(string[] args)
    {
        if (args.Length >= 3 && args[1] == "run")
            return RunChild(args[2]);

        bool inProcess = args.Length >= 2 && args[1] == "in-process";
        return inProcess ? RunInProcess() : RunFreshProcesses();
    }

    private static int RunFreshProcesses()
    {
        Console.WriteLine($"Coldstart benchmark — fib.wasm — fresh process per runtime");
        Console.WriteLine($"  trials per cell: 7 (trial 0 = 'first', subsequent 6 reported as median)");
        Console.WriteLine($"  units: microseconds (us)");
        Console.WriteLine();

        var dll = typeof(Coldstart).Assembly.Location;
        var dotnet = Environment.ProcessPath ?? "dotnet";
        bool selfHosted = !string.Equals(Path.GetFileName(dotnet), "dotnet", StringComparison.OrdinalIgnoreCase);

        foreach (var runtime in new[] { "interp-poly", "interp-switch", "transpiler", "transpiler-saved", "transpiler-saved-aotlinked" })
        {
            var psi = new ProcessStartInfo
            {
                FileName = selfHosted ? dotnet : "dotnet",
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            if (!selfHosted) psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add("coldstart");
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(runtime);

            using var p = Process.Start(psi)!;
            Console.Write(p.StandardOutput.ReadToEnd());
            p.WaitForExit();
        }
        return 0;
    }

    private static int RunChild(string runtime)
    {
        var bytes = File.ReadAllBytes(LocateFib());

        // For the saved-DLL runtimes, transpile + persist to a temp .dll once
        // before the trial loop. This is the build-time cost — paid once per
        // module ever, not at startup. Print it as a separate one-time line
        // so it's not folded into the per-trial coldstart numbers.
        string? savedDllPath = null;
        if (runtime == "transpiler-saved" || runtime == "transpiler-saved-aotlinked")
        {
            var emission = runtime == "transpiler-saved-aotlinked"
                ? EmissionTarget.AotLinked
                : EmissionTarget.Standard;
            var sw = Stopwatch.StartNew();
            savedDllPath = TranspileAndSave(bytes, emission, ns: "BenchSaved");
            sw.Stop();
            Console.WriteLine($"  one-time build cost (transpile + PAB save, {emission}): {ToUs(sw):F1} us");
            Console.WriteLine($"  saved .dll: {savedDllPath} ({new FileInfo(savedDllPath).Length} bytes)");
            Console.WriteLine();
        }

        foreach (var arg in new[] { 100, 5_000_000 })
        {
            Console.WriteLine($"=== {runtime} fib({arg:N0}) ({bytes.Length}-byte module) ===");
            Header();
            switch (runtime)
            {
                case "interp-poly":      Measure(runtime, bytes, arg, RunInterpreter,  useSwitch: false); break;
                case "interp-switch":    Measure(runtime, bytes, arg, RunInterpreter,  useSwitch: true);  break;
                case "transpiler":       Measure(runtime, bytes, arg, RunTranspiler,   useSwitch: false); break;
                case "transpiler-saved": MeasureSaved(runtime, savedDllPath!, arg); break;
                case "transpiler-saved-aotlinked": MeasureSaved(runtime, savedDllPath!, arg); break;
                default: Console.Error.WriteLine($"unknown runtime: {runtime}"); return 2;
            }
            Console.WriteLine();
        }

        if (savedDllPath != null && File.Exists(savedDllPath))
        {
            try { File.Delete(savedDllPath); } catch { /* best-effort cleanup */ }
        }
        return 0;
    }

    private static int RunInProcess()
    {
        var bytes = File.ReadAllBytes(LocateFib());

        Console.WriteLine($"Coldstart benchmark — fib.wasm ({bytes.Length} bytes) — IN-PROCESS");
        Console.WriteLine($"  warning: only the first runtime listed gets true cold .NET JIT;");
        Console.WriteLine($"  later runtimes share warmed parser/runtime code. Use the default");
        Console.WriteLine($"  fresh-process mode for fair cross-runtime comparison.");
        Console.WriteLine($"  trials per cell: 7 (1 first + 6 subsequent; subsequent reported as median)");
        Console.WriteLine();

        var savedSw = Stopwatch.StartNew();
        var savedDll = TranspileAndSave(bytes, EmissionTarget.Standard, ns: "BenchStd");
        savedSw.Stop();
        Console.WriteLine($"  one-time build cost (Standard emission, transpile + PAB save): {ToUs(savedSw):F1} us");
        var aotSw = Stopwatch.StartNew();
        var aotDll = TranspileAndSave(bytes, EmissionTarget.AotLinked, ns: "BenchAot");
        aotSw.Stop();
        Console.WriteLine($"  one-time build cost (AotLinked emission, transpile + PAB save): {ToUs(aotSw):F1} us");
        Console.WriteLine();

        foreach (var arg in new[] { 100, 5_000_000 })
        {
            Console.WriteLine($"=== fib({arg:N0}) ===");
            Header();
            Measure("interp-poly",      bytes, arg, RunInterpreter, useSwitch: false);
            Measure("interp-switch",    bytes, arg, RunInterpreter, useSwitch: true);
            Measure("transpiler",       bytes, arg, RunTranspiler,  useSwitch: false);
            MeasureSaved("transpiler-saved", savedDll, arg);
            MeasureSaved("transpiler-saved-aotlinked", aotDll, arg);
            Console.WriteLine();
        }

        try { File.Delete(savedDll); } catch { }
        try { File.Delete(aotDll); } catch { }
        return 0;
    }

    private const int Trials = 7;

    private static void Header()
    {
        Console.WriteLine($"  {"runtime",-15}  {"phase",-10}  {"first(us)",10}  {"subseq-med(us)",16}");
        Console.WriteLine($"  ----------------------------------------------------------------");
    }

    // run-fn signature: takes (bytes, arg, useSwitch, out PhaseTimings) and produces no return value.
    // PhaseTimings has parse/inst/xpile/activate/first/warm in microseconds.
    private delegate PhaseTimings RunFn(byte[] bytes, int arg, bool useSwitch);

    private struct PhaseTimings
    {
        public double Parse;     // us
        public double Inst;      // us
        public double Xpile;     // us, 0 for interpreter
        public double Activate;  // us, 0 for interpreter
        public double First;     // us, first invoke
        public double Warm;      // us, second invoke
    }

    private static void Measure(string name, byte[] bytes, int arg, RunFn fn, bool useSwitch)
    {
        var trials = new PhaseTimings[Trials];
        for (int i = 0; i < Trials; i++)
        {
            // Force GC before each trial so allocations during the measured region
            // aren't followed by a collection that gets billed to the next phase.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            trials[i] = fn(bytes, arg, useSwitch);
        }

        // Trial 0 = "first"; remaining 6 = "subsequent".
        var subseq = trials.Skip(1).ToArray();

        Print(name, "parse",    trials[0].Parse,    Median(subseq, t => t.Parse));
        Print(name, "inst",     trials[0].Inst,     Median(subseq, t => t.Inst));
        if (trials[0].Xpile > 0 || trials.Any(t => t.Xpile > 0))
            Print(name, "xpile",    trials[0].Xpile,    Median(subseq, t => t.Xpile));
        if (trials[0].Activate > 0 || trials.Any(t => t.Activate > 0))
            Print(name, "activate", trials[0].Activate, Median(subseq, t => t.Activate));
        Print(name, "first",    trials[0].First,    Median(subseq, t => t.First));
        Print(name, "warm",     trials[0].Warm,     Median(subseq, t => t.Warm));

        // Total cold = parse + inst + xpile + activate + first.
        double Total(PhaseTimings t) => t.Parse + t.Inst + t.Xpile + t.Activate + t.First;
        Print(name, "TOTAL",    Total(trials[0]),   Median(subseq, Total));
    }

    private static void Print(string name, string phase, double first, double subseq)
        => Console.WriteLine($"  {name,-15}  {phase,-10}  {first,10:F1}  {subseq,16:F1}");

    private static double Median(PhaseTimings[] xs, Func<PhaseTimings, double> sel)
    {
        var arr = xs.Select(sel).OrderBy(v => v).ToArray();
        int n = arr.Length;
        return n % 2 == 1 ? arr[n / 2] : (arr[n / 2 - 1] + arr[n / 2]) / 2.0;
    }

    // ---------- runtime drivers ----------

    private static PhaseTimings RunInterpreter(byte[] bytes, int arg, bool useSwitch)
    {
        var t = new PhaseTimings();
        var sw = new Stopwatch();

        sw.Restart();
        Wacs.Core.Module module;
        using (var ms = new MemoryStream(bytes))
            module = BinaryModuleParser.ParseWasm(ms);
        sw.Stop();
        t.Parse = ToUs(sw);

        sw.Restart();
        var runtime = new WasmRuntime();
        runtime.UseSwitchRuntime = useSwitch;
        var modInst = runtime.InstantiateModule(module,
            new RuntimeOptions { SkipModuleValidation = true });
        runtime.RegisterModule("bench", modInst);
        sw.Stop();
        t.Inst = ToUs(sw);

        if (!runtime.TryGetExportedFunction(("bench", "fib"), out var addr))
            throw new Exception("fib not exported");
        var invoker = runtime.CreateStackInvoker(addr,
            new InvokerOptions { SynchronousExecution = true });
        var argArr = new[] { new Value(arg) };

        sw.Restart();
        invoker(argArr);
        sw.Stop();
        t.First = ToUs(sw);

        sw.Restart();
        invoker(argArr);
        sw.Stop();
        t.Warm = ToUs(sw);

        return t;
    }

    private static PhaseTimings RunTranspiler(byte[] bytes, int arg, bool useSwitch)
    {
        var t = new PhaseTimings();
        var sw = new Stopwatch();

        sw.Restart();
        Wacs.Core.Module module;
        using (var ms = new MemoryStream(bytes))
            module = BinaryModuleParser.ParseWasm(ms);
        sw.Stop();
        t.Parse = ToUs(sw);

        sw.Restart();
        var runtime = new WasmRuntime();
        var modInst = runtime.InstantiateModule(module,
            new RuntimeOptions { SkipModuleValidation = true });
        sw.Stop();
        t.Inst = ToUs(sw);

        sw.Restart();
        var transpiler = new ModuleTranspiler();
        var result = transpiler.Transpile(modInst, runtime, "Bench");
        sw.Stop();
        t.Xpile = ToUs(sw);

        if (result.ModuleClass == null)
            throw new Exception("transpiler produced no Module class");

        sw.Restart();
        var instance = Activator.CreateInstance(result.ModuleClass);
        sw.Stop();
        t.Activate = ToUs(sw);

        // fib export — find the generated method by WASM name.
        var exportMeta = result.ExportMethods.FirstOrDefault(m => m.WasmName == "fib")
            ?? throw new Exception("fib not exported in transpiled module");
        var method = result.ModuleClass.GetMethod(exportMeta.Name,
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new Exception($"method {exportMeta.Name} not found");

        var clrArgs = new object[] { arg };

        sw.Restart();
        try { method.Invoke(instance, clrArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException != null) { throw tie.InnerException; }
        sw.Stop();
        t.First = ToUs(sw);

        sw.Restart();
        try { method.Invoke(instance, clrArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException != null) { throw tie.InnerException; }
        sw.Stop();
        t.Warm = ToUs(sw);

        return t;
    }

    private static double ToUs(Stopwatch sw)
        => sw.ElapsedTicks * 1_000_000.0 / Stopwatch.Frequency;

    // Resolve fib.wasm in either the development tree (run from
    // bin/Release/net8.0/) or a published self-contained directory (where
    // CopyToOutputDirectory drops it alongside the binary).
    private static string LocateFib()
    {
        var here = AppContext.BaseDirectory;
        var sideBySide = Path.Combine(here, "fib.wasm");
        if (File.Exists(sideBySide)) return sideBySide;
        var devTree = Path.GetFullPath(Path.Combine(here, "..", "..", "..", "fib.wasm"));
        if (File.Exists(devTree)) return devTree;
        throw new FileNotFoundException(
            $"fib.wasm not found next to the binary or in the dev tree (looked at {sideBySide} and {devTree})");
    }

    // ---------- transpiler-saved (.dll) driver ----------

    private static string TranspileAndSave(byte[] bytes,
        EmissionTarget emission = EmissionTarget.Standard,
        string ns = "Bench")
    {
        Wacs.Core.Module module;
        using (var ms = new MemoryStream(bytes))
            module = BinaryModuleParser.ParseWasm(ms);
        var runtime = new WasmRuntime();
        var modInst = runtime.InstantiateModule(module,
            new RuntimeOptions { SkipModuleValidation = true });
        var transpiler = new ModuleTranspiler(ns,
            new TranspilerOptions { Emission = emission });
        var result = transpiler.Transpile(modInst, runtime, "BenchMod");
        var path = Path.Combine(Path.GetTempPath(),
            $"wacs-bench-{emission.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.dll");
        result.SaveAssembly(path);
        return path;
    }

    private static void MeasureSaved(string name, string dllPath, int arg)
    {
        // Pre-read DLL bytes once, outside the trial loop. The trials measure
        // the cost of taking those bytes through Assembly load → instantiate →
        // first JITted call, not the disk I/O (which a real deployment would
        // have already streamed via OS page cache anyway).
        var dllBytes = File.ReadAllBytes(dllPath);

        var trials = new PhaseTimings[Trials];
        for (int i = 0; i < Trials; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            trials[i] = RunSavedOnce(dllBytes, arg);
        }

        var subseq = trials.Skip(1).ToArray();

        // Repurpose phase columns: parse=load (Assembly.LoadFromStream),
        // inst=resolve (find Module type), xpile=0, activate=ctor, first/warm=invoke.
        Print(name, "load",     trials[0].Parse,    Median(subseq, t => t.Parse));
        Print(name, "resolve",  trials[0].Inst,     Median(subseq, t => t.Inst));
        Print(name, "activate", trials[0].Activate, Median(subseq, t => t.Activate));
        Print(name, "first",    trials[0].First,    Median(subseq, t => t.First));
        Print(name, "warm",     trials[0].Warm,     Median(subseq, t => t.Warm));

        double Total(PhaseTimings t) => t.Parse + t.Inst + t.Activate + t.First;
        Print(name, "TOTAL",    Total(trials[0]),   Median(subseq, Total));
    }

    private static PhaseTimings RunSavedOnce(byte[] dllBytes, int arg)
    {
        var t = new PhaseTimings();
        var sw = new Stopwatch();

        // Fresh AssemblyLoadContext per trial — Assembly.LoadFile would cache
        // and trials 1..N would hit the cache, hiding the real load cost.
        var alc = new AssemblyLoadContext(
            "wacs-bench-" + Guid.NewGuid().ToString("N").Substring(0, 6),
            isCollectible: true);

        sw.Restart();
        Assembly asm;
        using (var ms = new MemoryStream(dllBytes))
            asm = alc.LoadFromStream(ms);
        sw.Stop();
        t.Parse = ToUs(sw);

        sw.Restart();
        Type? moduleType = null;
        foreach (var ty in asm.GetExportedTypes())
        {
            if (ty.Name != "Module") continue;
            var ctxField = ty.GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance);
            if (ctxField != null) { moduleType = ty; break; }
        }
        if (moduleType == null) throw new Exception("Module type not found in saved .dll");
        // Find fib export by CLR name (sanitized "fib" stays "fib").
        var method = moduleType.GetMethod("fib",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new Exception("fib export not found in saved .dll");
        sw.Stop();
        t.Inst = ToUs(sw);

        sw.Restart();
        var instance = Activator.CreateInstance(moduleType);
        sw.Stop();
        t.Activate = ToUs(sw);

        var clrArgs = new object[] { arg };

        sw.Restart();
        try { method.Invoke(instance, clrArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException != null) { throw tie.InnerException; }
        sw.Stop();
        t.First = ToUs(sw);

        sw.Restart();
        try { method.Invoke(instance, clrArgs); }
        catch (TargetInvocationException tie) when (tie.InnerException != null) { throw tie.InnerException; }
        sw.Stop();
        t.Warm = ToUs(sw);

        alc.Unload();
        return t;
    }
}

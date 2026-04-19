// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Opcode-frequency profiler. Runs a fixed set of WASM modules through the
// polymorphic runtime with per-instruction stats enabled, then emits a report
// showing which opcodes dominate execution for each module and across the set.
//
// Usage (from repo root):
//   dotnet run --project Wacs.OpProfile -c Release -- [modules...]
//
// With no args, runs the default set (coremark + perl + pystone). Pass one or
// more `name=path` specs to override (or append with `+name=path`).
//
// Output:
//   /tmp/opprofile.tsv    — machine-readable, one row per (module, opcode)
//   stdout                — human-readable top-N per module + aggregate summary
//
// Rationale: the switch runtime's per-op dispatch cost is uniform across
// opcodes, but the JIT has a limited inlining budget inside the huge Run
// method. Knowing which opcodes dominate in real workloads tells us whether
// a hot/cold split is justified and what the hot set looks like — or whether
// the set is too varied for a static split to be worth it. This tool runs
// against real binaries (not benchmarks) so the findings reflect production
// distribution, not a tight loop's idiosyncrasies.

using System.Diagnostics;
using System.IO;
using System.Linq;
using Wacs.Core;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.WASIp1;
using Wacs.WASIp1;
using Wacs.WASIp1.Types;

var specs = ParseSpecs(args);
if (specs.Count == 0)
{
    System.Console.Error.WriteLine("No modules to profile.");
    return 1;
}

var reports = new List<ProfileReport>();
foreach (var spec in specs)
{
    System.Console.Error.WriteLine($"[{spec.Name}] loading {spec.Path}");
    try
    {
        var report = Profile(spec);
        reports.Add(report);
        System.Console.Error.WriteLine(
            $"[{spec.Name}] {report.TotalInstructions:N0} instructions across {report.Counts.Count} distinct opcodes " +
            $"({report.WallMs:N0} ms)");
    }
    catch (Exception ex)
    {
        System.Console.Error.WriteLine($"[{spec.Name}] FAILED: {ex.GetType().Name}: {ex.Message}");
    }
}

WriteTsv("/tmp/opprofile.tsv", reports);
PrintSummary(reports);
return 0;

// ----------------------------------------------------------------------------

static List<ModuleSpec> ParseSpecs(string[] args)
{
    // Default set of three modules covering distinct workload shapes:
    //  - coremark: classic tight-loop C microbenchmark, minimal memory churn.
    //  - wasm2wat: a real wabt tool — heavier string/memory ops than coremark.
    //  - perl:     script-interpreter harness — highly varied, allocation-heavy.
    //
    // Substituting wasm2wat for the originally requested auth2.wasm (not in
    // this repo) and for pystone (pystone uses a custom "seq_*" runtime we'd
    // have to stub end-to-end). Paths are relative to the repo root; the
    // profiler expects cwd = repo root.
    var defaults = new List<ModuleSpec>
    {
        new("coremark",    "Wacs.Console/Data/coremark.wasm",    null, true, 120_000, null, false),
        new("wasm2wat",    "Wacs.Console/Data/wasm2wat.wasm",    null, true, 120_000,
            new[] { "wasm2wat", "Wacs.Console/Data/coremark.wasm" }, false),
        new("perl",        "Wacs.Console/Data/andrewsample/perl.wasm", null, true, 120_000,
            new[] { "perl", "-e", "for (1..100) { print \"ok\\n\"; }" }, false),
        // f64-numeric: a synthetic matmul + dot-product kernel hand-written to
        // dominate execution with f64.load / f64.mul / f64.add plus linear-memory
        // i32 address arithmetic. Designed to be the counterpoint to coremark —
        // if a hot/cold split demotes f64 ops to a sub-method, this is the
        // workload that pays for it. Starts via a `(start $run)` wasm start
        // function, so no WASI wiring is needed.
        new("f64-numeric", "Wacs.OpProfile/Data/f64-numeric.wasm",  null, false, 120_000, null, false),
        // spectra: an emscripten-compiled C++ eigenvalue-solver library (Spectra on
        // top of Eigen). Today this entry fails parsing — the binary was built
        // with emscripten's *old* exception-handling proposal (opcode 0x06 `try`
        // etc.), which WACS doesn't support (only the newer `try_table`
        // variant). Kept in the default list as a forward-compatibility slot:
        // re-building spectra with `-fwasm-exceptions=new` (or dropping -fexceptions
        // entirely) produces a module WACS can parse, and the Emscripten=true
        // stubs below are ready to satisfy its env.* / embind imports for a
        // __wasm_call_ctors-only profile. Until then we accept the failure.
        new("spectra",     "Wacs.OpProfile/Data/spectra.wasm",
            "__wasm_call_ctors", false, 120_000, null, true),
    };

    if (args.Length == 0) return defaults;

    var overrides = new List<ModuleSpec>();
    bool append = false;
    foreach (var arg in args)
    {
        var a = arg;
        if (a.StartsWith("+"))
        {
            append = true;
            a = a.Substring(1);
        }
        var eq = a.IndexOf('=');
        if (eq < 0)
        {
            System.Console.Error.WriteLine($"Unparseable spec (expected name=path): {arg}");
            continue;
        }
        overrides.Add(new ModuleSpec(a.Substring(0, eq), a.Substring(eq + 1), null, true, 120_000, null, false));
    }
    return append ? defaults.Concat(overrides).ToList() : overrides;
}

static ProfileReport Profile(ModuleSpec spec)
{
    if (!File.Exists(spec.Path))
        throw new FileNotFoundException($"wasm not found: {spec.Path}");

    var bytes = File.ReadAllBytes(spec.Path);
    using var ms = new MemoryStream(bytes);
    var module = BinaryModuleParser.ParseWasm(ms);

    var runtime = new WasmRuntime();

    // Modules shipped in the repo typically target WASI plus a small set of
    // env-imports the sample drivers satisfy. Replicate the minimum surface so
    // perl / coremark / pystone don't trap on import resolution.
    runtime.BindHostFunction<Action<char>>(("env", "sayc"), c => { /* swallow */ });
    // emscripten sometimes asks the host to re-read memory pages after grow;
    // nothing to do here, we just need to not trap.
    runtime.BindHostFunction<Action<int>>(("env", "emscripten_notify_memory_growth"), _ => { });

    if (spec.Emscripten)
    {
        BindEmscriptenStubs(runtime);
    }

    Wacs.WASIp1.Wasi wasi = null;
    if (spec.Wasi)
    {
        // In-memory stdout / stderr sinks so the perl test doesn't fill the
        // terminal. Stdin is empty. Preopen the current working directory
        // (the repo root when the profiler is invoked from the normal dotnet
        // run harness) so WASI-native tools like wasm2wat can read file args
        // passed on their command line.
        var wasiConfig = new WasiConfiguration
        {
            StandardInput  = new MemoryStream(),
            StandardOutput = new MemoryStream(),
            // Route module stderr to our stderr so errors like "cannot open
            // foo.wasm" surface immediately. Useful for diagnosing why a WASI
            // tool exits early (file-not-found, missing env, etc).
            StandardError  = System.Console.OpenStandardError(),
            Arguments = (spec.WasiArgs ?? new[] { spec.Name }).ToList(),
            // LC_ALL=C bypasses perl's composite-locale parser, which crashes on the
            // WASI libc's default locale string. Keeps the env otherwise empty so
            // modules see a stable reproducible environment across runs.
            EnvironmentVariables = new Dictionary<string, string> { ["LC_ALL"] = "C" },
            HostRootDirectory = Directory.GetCurrentDirectory(),
        };
        wasiConfig.PreopenedDirectories = new List<PreopenedDirectory>
        {
            new(wasiConfig, "."),
        };
        wasi = new Wacs.WASIp1.Wasi(wasiConfig);
        wasi.BindToRuntime(runtime);
    }

    var modInst = runtime.InstantiateModule(module,
        new RuntimeOptions { SkipModuleValidation = true });
    runtime.RegisterModule(spec.Name, modInst);

    // Choose the entry: explicit --invoke arg wins, then module start fn, then _start.
    Action runInvoker;
    var callOptions = new InvokerOptions
    {
        CollectStats = StatsDetail.Instruction,
        SynchronousExecution = true,
    };

    if (spec.Invoke != null &&
        runtime.TryGetExportedFunction((spec.Name, spec.Invoke), out var invokeAddr))
    {
        var stackInvoker = runtime.CreateStackInvoker(invokeAddr, callOptions);
        runInvoker = () => stackInvoker(Array.Empty<Value>());
    }
    else if (modInst.StartFunc != null)
    {
        runInvoker = runtime.CreateInvokerAction(modInst.StartFunc, callOptions);
    }
    else if (runtime.TryGetExportedFunction((spec.Name, "_start"), out var startAddr))
    {
        runInvoker = runtime.CreateInvokerAction(startAddr, callOptions);
    }
    else
    {
        throw new InvalidOperationException(
            $"{spec.Name}: no --invoke, no StartFunc, and no _start export");
    }

    // The polymorphic runtime's PrintStats dumps a full per-opcode table to
    // Console.Error at the end of every run. We read Context.Stats directly,
    // so PrintStats is just noise — redirect Console.Error to a buffer for
    // the duration of the invoke. Module-emitted WASI stderr writes don't go
    // through Console.Error; they go through the WASI StandardError stream
    // set in wasiConfig, so they stay visible.
    var origErr = System.Console.Error;
    System.Console.SetError(TextWriter.Null);
    var sw = Stopwatch.StartNew();
    try
    {
        runInvoker();
    }
    catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is SignalException)
    {
        // WASI exit: proc_exit, unsupported-syscall bailout, or similar. The
        // stats accumulated up to the exit are still valid. SystemExitException
        // derives from SignalException so both land here.
    }
    catch (SignalException) { }
    catch (System.Reflection.TargetInvocationException tie)
    {
        // Any other exception from WASM execution wrapped by the invoker —
        // stats up to the trap are still collected. Emit a note so the user
        // knows why the module stopped early.
        System.Console.Error.WriteLine(
            $"  [{spec.Name}] execution stopped: {tie.InnerException?.GetType().Name}: {tie.InnerException?.Message}");
    }
    finally
    {
        wasi?.Dispose();
        System.Console.SetError(origErr);
    }
    sw.Stop();

    // Snapshot Context.Stats into a plain Dictionary. The Stats key is a
    // ushort: low byte = primary, high byte = secondary (or 0 for the
    // non-prefixed opcodes). ExecStat.count accumulates invocation counts.
    var counts = new Dictionary<ushort, long>();
    long total = 0;
    foreach (var kv in runtime.ExecContext.Stats)
    {
        if (kv.Value.count == 0) continue;
        counts[kv.Key] = kv.Value.count;
        total += kv.Value.count;
    }

    return new ProfileReport(spec.Name, counts, total, sw.ElapsedMilliseconds);
}

static void WriteTsv(string path, List<ProfileReport> reports)
{
    using var w = new StreamWriter(path);
    w.WriteLine("module\topcode_key\topcode_name\tcount\tpercent");
    foreach (var r in reports)
    {
        foreach (var (key, count) in r.Counts.OrderByDescending(kv => kv.Value))
        {
            var bc = ByteCodeFromKey(key);
            double pct = r.TotalInstructions == 0 ? 0.0 : 100.0 * count / r.TotalInstructions;
            w.WriteLine($"{r.Name}\t0x{key:X4}\t{bc.GetMnemonic()}\t{count}\t{pct:F4}");
        }
    }
    System.Console.Error.WriteLine($"wrote TSV to {path}");
}

static void PrintSummary(List<ProfileReport> reports)
{
    const int topN = 20;
    foreach (var r in reports)
    {
        System.Console.WriteLine();
        System.Console.WriteLine($"=== {r.Name} — {r.TotalInstructions:N0} insts in {r.WallMs:N0}ms ===");
        System.Console.WriteLine($"{"rank",4} {"opcode",-24} {"count",15} {"pct",7} {"cum",7}");
        double cum = 0;
        int rank = 0;
        foreach (var (key, count) in r.Counts.OrderByDescending(kv => kv.Value).Take(topN))
        {
            rank++;
            var bc = ByteCodeFromKey(key);
            double pct = 100.0 * count / r.TotalInstructions;
            cum += pct;
            System.Console.WriteLine($"{rank,4} {bc.GetMnemonic(),-24} {count,15:N0} {pct,6:F2}% {cum,6:F2}%");
        }
    }

    // Aggregate across modules: distinct-opcode count, "hot set" size needed
    // to cover 90% / 95% / 99% of total execution in each module — shows how
    // concentrated each workload is.
    System.Console.WriteLine();
    System.Console.WriteLine("=== coverage summary ===");
    System.Console.WriteLine($"{"module",-12} {"ops",5} {"90%:N",5} {"95%:N",5} {"99%:N",5}");
    foreach (var r in reports)
    {
        int n90 = 0, n95 = 0, n99 = 0;
        double cum = 0;
        foreach (var (_, count) in r.Counts.OrderByDescending(kv => kv.Value))
        {
            cum += 100.0 * count / r.TotalInstructions;
            if (cum < 90) n90++;
            if (cum < 95) n95++;
            if (cum < 99) n99++;
        }
        System.Console.WriteLine($"{r.Name,-12} {r.Counts.Count,5} {n90+1,5} {n95+1,5} {n99+1,5}");
    }

    // Union / intersection: which opcodes are hot (>=1%) across ALL modules
    // vs any. A small intersection set is the safest hot-dispatch candidate.
    var hotPerModule = reports.Select(r =>
    {
        var set = new HashSet<ushort>();
        foreach (var (key, count) in r.Counts)
            if (100.0 * count / r.TotalInstructions >= 1.0)
                set.Add(key);
        return set;
    }).ToList();

    var union = new HashSet<ushort>();
    foreach (var s in hotPerModule) union.UnionWith(s);
    HashSet<ushort> intersection = null;
    foreach (var s in hotPerModule)
    {
        if (intersection == null) intersection = new HashSet<ushort>(s);
        else intersection.IntersectWith(s);
    }
    intersection ??= new HashSet<ushort>();

    System.Console.WriteLine();
    System.Console.WriteLine($"opcodes ≥1% in ALL modules ({intersection.Count}):");
    foreach (var key in intersection.OrderBy(k => k))
        System.Console.WriteLine($"  0x{key:X4}  {ByteCodeFromKey(key).GetMnemonic()}");
    System.Console.WriteLine($"opcodes ≥1% in ANY module ({union.Count}):");
    foreach (var key in union.OrderBy(k => k))
        System.Console.WriteLine($"  0x{key:X4}  {ByteCodeFromKey(key).GetMnemonic()}");
}

/// <summary>
/// Bind no-op stubs for the env + wasi_snapshot_preview1 imports that
/// emscripten+embind wasm modules expect. Signatures match spectra.wasm's
/// import table; a different emscripten module may pull in additional
/// imports — add them here when they fail with a NotSupportedException.
///
/// <para>The stubs intentionally don't emulate emscripten semantics. Handle
/// values returned from <c>_emval_*</c> are dummy (0) — fine because spectra's
/// ctor path uses them only to register embind metadata, never to round-trip
/// through JS. <c>emscripten_resize_heap</c> returns 0 (failure) — if the
/// module allocates past its initial memory the grow will fail, but
/// <c>__wasm_call_ctors</c> doesn't trigger that path for this module.</para>
/// </summary>
static void BindEmscriptenStubs(WasmRuntime runtime)
{
    runtime.BindHostFunction<Action<int>>(("env", "_emval_decref"),           _ => { });
    runtime.BindHostFunction<Action<int,int,int,int,int,int,int,int>>(
        ("env", "_embind_register_function"),                                 (a,b,c,d,e,f,g,h) => { });
    runtime.BindHostFunction<Func<int>>(("env", "_emval_new_object"),         () => 0);
    runtime.BindHostFunction<Action<int,int,int,int>>(
        ("env", "__assert_fail"),                                             (a,b,c,d) => throw new InvalidOperationException("__assert_fail"));
    runtime.BindHostFunction<Action<int>>(("env", "_emval_incref"),           _ => { });
    runtime.BindHostFunction<Func<int,int>>(("env", "_emval_new_cstring"),    _ => 0);
    runtime.BindHostFunction<Action<int,int,int>>(("env", "_emval_set_property"),  (a,b,c) => { });
    runtime.BindHostFunction<Func<int,int,int,int>>(("env", "_emval_create_invoker"), (a,b,c) => 0);
    runtime.BindHostFunction<Func<int,int,int,int,int,double>>(
        ("env", "_emval_invoke"),                                             (a,b,c,d,e) => 0.0);
    runtime.BindHostFunction<Action<int>>(("env", "_emval_run_destructors"),  _ => { });
    runtime.BindHostFunction<Func<int,int,int>>(("env", "_emval_get_property"),    (a,b) => 0);
    runtime.BindHostFunction<Action<int,int>>(("env", "_emval_array_to_memory_view"), (a,b) => { });
    runtime.BindHostFunction<Func<int>>(("env", "_emval_new_array"),          () => 0);
    runtime.BindHostFunction<Action<int,int>>(("env", "_embind_register_void"),       (a,b) => { });
    runtime.BindHostFunction<Action<int,int,int,int>>(("env", "_embind_register_bool"),   (a,b,c,d) => { });
    runtime.BindHostFunction<Action<int,int,int,int,int>>(("env", "_embind_register_integer"), (a,b,c,d,e) => { });
    runtime.BindHostFunction<Action<int,int,int,long,long>>(("env", "_embind_register_bigint"), (a,b,c,d,e) => { });
    runtime.BindHostFunction<Action<int,int,int>>(("env", "_embind_register_float"),  (a,b,c) => { });
    runtime.BindHostFunction<Action<int,int>>(("env", "_embind_register_std_string"), (a,b) => { });
    runtime.BindHostFunction<Action<int,int,int>>(("env", "_embind_register_std_wstring"), (a,b,c) => { });
    runtime.BindHostFunction<Action<int>>(("env", "_embind_register_emval"),  _ => { });
    runtime.BindHostFunction<Action<int,int,int>>(("env", "_embind_register_memory_view"), (a,b,c) => { });
    runtime.BindHostFunction<Func<int,int>>(("env", "emscripten_resize_heap"), _ => 0);
    runtime.BindHostFunction<Func<int,int,int>>(("wasi_snapshot_preview1", "environ_sizes_get"), (a,b) => 0);
    runtime.BindHostFunction<Func<int,int,int>>(("wasi_snapshot_preview1", "environ_get"), (a,b) => 0);
    runtime.BindHostFunction<Action<int,int,int,int>>(("env", "_tzset_js"),   (a,b,c,d) => { });
    runtime.BindHostFunction<Action>(("env", "_abort_js"),                    () => throw new InvalidOperationException("emscripten _abort_js"));
}

static ByteCode ByteCodeFromKey(ushort key)
{
    // Reconstruct a ByteCode from the ushort Stats key. The explicit
    // `(ushort)ByteCode` cast in OpCodes/ByteCode.cs places the primary in the
    // HIGH byte and the secondary in the LOW byte: `(x00 << 8) | xFB/xFC/…`.
    // So: primary = key >> 8, secondary = key & 0xFF. Non-prefix primaries
    // (everything except 0xFB..0xFF) have secondary = 0, producing keys of
    // the form 0xXX00. Leaving this decoding backwards was the first bug
    // when this tool went live — keep the mapping aligned with ByteCode.cs.
    byte primary = (byte)(key >> 8);
    byte secondary = (byte)(key & 0xFF);
    return primary switch
    {
        (byte)OpCode.FB => new ByteCode((GcCode)secondary),
        (byte)OpCode.FC => new ByteCode((ExtCode)secondary),
        (byte)OpCode.FD => new ByteCode((SimdCode)secondary),
        (byte)OpCode.FE => new ByteCode((AtomCode)secondary),
        (byte)OpCode.FF => new ByteCode((WacsCode)secondary),
        _ => new ByteCode((OpCode)primary),
    };
}

sealed record ModuleSpec(
    string Name,
    string Path,
    string Invoke,
    bool Wasi,
    int TimeoutMs,
    string[] WasiArgs,
    bool Emscripten);

sealed record ProfileReport(
    string Name,
    Dictionary<ushort, long> Counts,
    long TotalInstructions,
    long WallMs);

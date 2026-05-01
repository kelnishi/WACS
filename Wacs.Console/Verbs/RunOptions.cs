// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using CommandLine;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// `wacs run [files]...` — execute one or more `.wasm` modules
    /// via the interpreter (default) or transpiler engine. Trailing
    /// positional args after `--` are forwarded to the wasm guest's
    /// argv.
    /// </summary>
    [Verb("run", HelpText =
        "Execute a .wasm module via the interpreter (default) or "
        + "transpiler engine. Default verb when the first arg is a "
        + "file path.")]
    public sealed class RunOptions : SharedOptions
    {
        [Value(0, MetaName = "files", HelpText =
            "Path(s) to .wasm / .wat / .component.wasm input file(s). "
            + "Multiple inputs trigger ModuleLinker composition; "
            + "cross-module imports resolve via filename basenames. "
            + "Trailing args after `--` are forwarded to the guest's "
            + "argv / --call function.")]
        public IEnumerable<string> Files { get; set; } = new List<string>();

        [Option("call", HelpText =
            "Function export to invoke (default: _start, then any "
            + "configured WASM start section). Pass arguments after "
            + "`--` for the function or for WASI argv.")]
        public string Call { get; set; } = "";

        [Option("engine", Default = "interpreter", HelpText =
            "Execution engine: interpreter (default) or transpiler. "
            + "transpiler JITs to .NET IL via Reflection.Emit and "
            + "runs through CLR-native dispatch with imports proxied "
            + "back to the interpreter for mixed-mode execution.")]
        public string Engine { get; set; } = "interpreter";

        [Option('m', "module", Default = "_", HelpText =
            "Name to register the instantiated module under for "
            + "cross-module import resolution. Defaults to `_` for "
            + "single-module runs; multi-input runs use the file "
            + "basename per input.")]
        public string ModuleName { get; set; } = "_";

        // ---- WASI Preview 1 ----

        [Option('e', "env", Separator = ',', HelpText =
            "Environment variables for WASI Preview 1 (KEY=VALUE), "
            + "comma-separated.")]
        public IEnumerable<string> EnvironmentVars { get; set; } = new List<string>();

        [Option('d', "dir", Separator = ',', HelpText =
            "Pre-opened directories for WASI Preview 1, "
            + "comma-separated.")]
        public IEnumerable<string> Directories { get; set; } = new List<string>();

        [Option("wasi", HelpText =
            "Bind WASI Preview 1 host imports before execution. "
            + "Equivalent to `--bind <Wacs.WASIp1>`.")]
        public bool Wasi { get; set; }

        // ---- Component-mode host packages ----

        [Option("host-package", Separator = ',', HelpText =
            "Host package assembly name(s) or path(s) whose "
            + "[WitSource]-tagged interfaces resolve a component's "
            + "imports at transpile time. Component-mode only.")]
        public IEnumerable<string> HostPackage { get; set; } = new List<string>();

        [Option("wasip2", HelpText =
            "Shorthand for `--host-package Wacs.WASI.Preview2`. "
            + "Component-mode only.")]
        public bool Wasip2 { get; set; }

        [Option("bind", Separator = ',', HelpText =
            "Path(s) to assemblies containing IBindable host "
            + "libraries to wire into the runtime before execution.")]
        public IEnumerable<string> Bind { get; set; } = new List<string>();

        // ---- Instrumentation (interpreter engine only) ----

        [Option("profile", HelpText =
            "Bracket execution with a JetBrains dotTrace measure-"
            + "profiling session. Snapshot lands in the OS-default "
            + "profiler temp dir.")]
        public bool Profile { get; set; }

        [Option("log-gas", HelpText =
            "Print total instructions executed after the run.")]
        public bool LogGas { get; set; }

        [Option("gas-limit", Default = 0, HelpText =
            "Limit dispatched instructions; raises a TrapException "
            + "when exceeded. 0 = unlimited.")]
        public int GasLimit { get; set; }

        [Option("log-progress", Default = -1, HelpText =
            "Print '.' every N instructions. -1 = disabled.")]
        public int LogProgressEvery { get; set; }

        [Option("log-execution", Default = Wacs.Core.Runtime.InstructionLogging.None,
            HelpText = "Log per-instruction execution: None | Computes | Calls | Branches | Memory | All.")]
        public Wacs.Core.Runtime.InstructionLogging LogExecution { get; set; }
            = Wacs.Core.Runtime.InstructionLogging.None;

        [Option("calculate-lines", HelpText =
            "Calculate line numbers for instruction-execution logs. "
            + "Adds a per-call lookup over the parsed module.")]
        public bool CalculateLines { get; set; }

        [Option("stats", Default = Wacs.Core.Runtime.StatsDetail.None,
            HelpText = "Collect per-instruction statistics: None | Total | Instruction | Function.")]
        public Wacs.Core.Runtime.StatsDetail Stats { get; set; }
            = Wacs.Core.Runtime.StatsDetail.None;

        // ---- Interpreter runtime selection ----

        [Option("super", HelpText =
            "Enable super-instruction fusion. With --switch, fuses "
            + "the bytecode stream; otherwise rewrites the polymorphic "
            + "instruction tree.")]
        public bool SuperInstructions { get; set; }

        [Option("switch", HelpText =
            "Use the source-generated monolithic switch runtime "
            + "(faster, AOT-safe).")]
        public bool UseSwitch { get; set; }

        // ---- Transpiler engine knobs (only when --engine transpiler) ----

        [Option("simd", Default = "scalar", HelpText =
            "SIMD strategy for --engine transpiler: interpreter | "
            + "scalar | intrinsics.")]
        public string Simd { get; set; } = "scalar";

        [Option("no-tail-calls", HelpText =
            "Disable CIL `tail.` prefix for return_call* ops "
            + "(--engine transpiler only).")]
        public bool NoTailCalls { get; set; }

        [Option("max-fn-size", Default = 0, HelpText =
            "Skip transpilation of functions larger than N "
            + "instructions (--engine transpiler only). 0 = unlimited.")]
        public int MaxFnSize { get; set; }

        [Option("data-storage", Default = "compressed", HelpText =
            "Data segment storage for --engine transpiler: "
            + "compressed | raw | static.")]
        public string DataStorage { get; set; } = "compressed";

        // Trailing argv comes through the dispatcher (Program.cs's
        // direct-run shortcut + dash-dash split). This list is
        // populated programmatically rather than via [Value], to
        // sidestep CommandLineParser's positional-collection
        // limitations when there's already a Value(0) collection.
        public IEnumerable<string> Args { get; set; } = new List<string>();
    }
}

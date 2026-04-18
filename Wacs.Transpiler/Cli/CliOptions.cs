// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using CommandLine;

namespace Wacs.Transpiler.Cli
{
    /// <summary>
    /// Command-line options for the `wasm-transpile` tool.
    /// Flags fall into three groups: I/O plumbing, TranspilerOptions mapping,
    /// and --emit-main boilerplate.
    /// </summary>
    public class CliOptions
    {
        // ---- I/O ----

        [Option('i', "input", Required = true, HelpText = "Input .wasm file.")]
        public string Input { get; set; } = "";

        [Option('o', "output", Required = true, HelpText = "Output .dll path.")]
        public string Output { get; set; } = "";

        [Option('n', "namespace", Default = "CompiledWasm",
            HelpText = "Root namespace for generated types.")]
        public string Namespace { get; set; } = "CompiledWasm";

        [Option('m', "module", Default = "WasmModule",
            HelpText = "Name of the generated Module class.")]
        public string ModuleName { get; set; } = "WasmModule";

        [Option('v', "verbose", HelpText = "Print transpilation diagnostics and a summary.")]
        public bool Verbose { get; set; }

        // ---- TranspilerOptions mapping ----

        [Option("simd", Default = "scalar",
            HelpText = "SIMD strategy: interpreter | scalar | intrinsics.")]
        public string Simd { get; set; } = "scalar";

        [Option("no-tail-calls", HelpText = "Disable emission of the CIL `tail.` prefix for return_call* ops.")]
        public bool NoTailCalls { get; set; }

        [Option("max-fn-size", Default = 0,
            HelpText = "Maximum function body size (in instructions) to attempt transpilation. 0 = unlimited.")]
        public int MaxFunctionSize { get; set; }

        [Option("data-storage", Default = "compressed",
            HelpText = "Data segment storage: compressed | raw | static.")]
        public string DataStorage { get; set; } = "compressed";

        [Option("gc-checking", Default = "",
            HelpText = "Comma-separated TranspilerCapabilities flags enabling additional GC type-check layers.")]
        public string GcChecking { get; set; } = "";

        // ---- --emit-main boilerplate ----

        [Option("emit-main",
            HelpText = "Emit a Program.Main(string[]) inside the output assembly so it runs as an executable host for the transpiled module. v0.1 only supports modules with no imports.")]
        public bool EmitMain { get; set; }

        [Option("entry-point", Default = "_start",
            HelpText = "When --emit-main is set, the WASM export Main invokes. Scalar args only (i32/i64/f32/f64).")]
        public string EntryPoint { get; set; } = "_start";

        [Option("main-class", Default = "Program",
            HelpText = "When --emit-main is set, the name of the emitted Program class.")]
        public string MainClass { get; set; } = "Program";

        [Option("run",
            HelpText = "After transpile (requires --emit-main), invoke the emitted Program.Main in-process with any trailing positional args.")]
        public bool Run { get; set; }

        [Value(0, MetaName = "args",
            HelpText = "Positional arguments forwarded to Program.Main when --run is set.")]
        public System.Collections.Generic.IEnumerable<string> Args { get; set; } = System.Array.Empty<string>();
    }
}

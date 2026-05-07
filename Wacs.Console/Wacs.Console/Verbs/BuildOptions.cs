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
    /// `wacs build [files]... -o out.dll` — transpile one or more
    /// `.wasm` modules to .NET assemblies. Always uses the
    /// transpiler engine (no run unless `--emit-main` + reflective
    /// invocation downstream).
    /// </summary>
    [Verb("build", HelpText =
        "Transpile a .wasm module to a .NET assembly (.dll). "
        + "Multi-input runs land siblings as <basename>.dll alongside "
        + "the chosen --output path.")]
    public sealed class BuildOptions : SharedOptions
    {
        [Value(0, MetaName = "files", HelpText =
            "Path(s) to .wasm / .component.wasm input file(s). "
            + "Multi-file → ModuleLinker composition; component-mode "
            + "auto-detects via the layer header byte.")]
        public IEnumerable<string> Files { get; set; } = new List<string>();

        [Option('o', "output", HelpText =
            "Output .dll path. Omit (or pass a directory) to default to "
            + "<inputBasename>.dll alongside the input. With multiple "
            + "inputs, this is the path for the LAST input; siblings land "
            + "at <basename>.dll alongside.")]
        public string Output { get; set; } = "";

        [Option("namespace", Default = "CompiledWasm", HelpText =
            "Root namespace for the generated types.")]
        public string Namespace { get; set; } = "CompiledWasm";

        [Option('m', "module", Default = "WasmModule", HelpText =
            "Name of the generated Module class.")]
        public string ModuleName { get; set; } = "WasmModule";

        [Option("assembly-name", HelpText =
            "Override the generated assembly's logical name. When set, "
            + "skips the default `<namespace>.<module>_<uniqueId>` scheme. "
            + "Required for the wacs-aot whole-program build path so "
            + "ILC's resolver can find the .dll via static <Reference>; "
            + "the on-disk filename should also be <assemblyname>.dll.")]
        public string AssemblyName { get; set; } = "";

        [Option("emission", Default = "standard", HelpText =
            "Module ctor emission shape: standard (codec-encoded init "
            + "data, works for in-process and cross-process load) or "
            + "aot-linked (direct ThinContext construction, no codec "
            + "wrapper, dead-strippable from a NativeAOT consumer's "
            + "binary). aot-linked currently supports compute-only "
            + "modules; coverage will grow.")]
        public string Emission { get; set; } = "standard";

        // ---- Host packages / bindings ----

        [Option("wasi", HelpText =
            "Bake WASI Preview 1 bindings into the output assembly.")]
        public bool Wasi { get; set; }

        [Option("bind", Separator = ',', HelpText =
            "Path(s) to assemblies containing IBindable host "
            + "libraries to wire into the build runtime.")]
        public IEnumerable<string> Bind { get; set; } = new List<string>();

        [Option("host-package", Separator = ',', HelpText =
            "Host package assembly name(s) or path(s) whose "
            + "[WitSource]-tagged interfaces resolve component "
            + "imports at transpile time. Component-mode only.")]
        public IEnumerable<string> HostPackage { get; set; } = new List<string>();

        [Option("wasip2", HelpText =
            "Shorthand for `--host-package Wacs.WASI.Preview2`. "
            + "Component-mode only.")]
        public bool Wasip2 { get; set; }

        // ---- emit-main boilerplate ----

        [Option("emit-main", HelpText =
            "Bake a Program.Main(string[]) into the output assembly "
            + "so it runs as an executable host for the transpiled "
            + "module. Core-mode requires zero-import modules; "
            + "component-mode handles imports through the configured "
            + "host package.")]
        public bool EmitMain { get; set; }

        [Option("entry-point", Default = "_start", HelpText =
            "When --emit-main is set, the WASM export Main invokes.")]
        public string EntryPoint { get; set; } = "_start";

        [Option("main-class", Default = "Program", HelpText =
            "When --emit-main is set, the name of the emitted "
            + "Program class.")]
        public string MainClass { get; set; } = "Program";

        // ---- TranspilerOptions mapping ----

        [Option("simd", Default = "scalar", HelpText =
            "SIMD strategy: interpreter | scalar | intrinsics.")]
        public string Simd { get; set; } = "scalar";

        [Option("no-tail-calls", HelpText =
            "Disable CIL `tail.` prefix for return_call* ops.")]
        public bool NoTailCalls { get; set; }

        [Option("max-fn-size", Default = 0, HelpText =
            "Skip transpilation of functions larger than N "
            + "instructions. 0 = unlimited.")]
        public int MaxFnSize { get; set; }

        [Option("data-storage", Default = "compressed", HelpText =
            "Data segment storage: compressed | raw | static.")]
        public string DataStorage { get; set; } = "compressed";

        [Option("gc-checking", Default = "", HelpText =
            "Comma-separated TranspilerCapabilities flags enabling "
            + "additional GC type-check layers.")]
        public string GcChecking { get; set; } = "";
    }
}

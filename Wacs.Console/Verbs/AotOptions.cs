// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using CommandLine;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// `wacs aot input.wasm -o appname` — produce a self-contained
    /// NativeAOT native binary from a wasm input. Internally:
    /// transpiles to a stable-named .dll with AotLinked emission,
    /// scaffolds a throwaway consumer csproj that statically references
    /// the .dll plus the WACS runtime support assemblies, and runs
    /// `dotnet publish -p:PublishAot=true -r &lt;rid&gt;`. The native
    /// binary is copied to the requested output path; the temp build
    /// directory is removed unless --keep-temp is set.
    ///
    /// MVP scope: single-input core-wasm modules with a known entry-
    /// point export (default <c>_start</c>); no imports, no memories,
    /// no globals (the AotLinked emission target's current limits —
    /// see <c>EmissionTarget.AotLinked</c>). WASI / host bindings /
    /// component-mode are tracked as follow-ups.
    /// </summary>
    [Verb("aot", HelpText =
        "Produce a self-contained NativeAOT native binary from a "
        + "wasm input. End-to-end transpile + scaffold + publish. "
        + "MVP supports compute-only modules; coverage will grow.")]
    public sealed class AotOptions
    {
        [Value(0, MetaName = "input", Required = true, HelpText =
            "Path to the input .wasm file.")]
        public string Input { get; set; } = "";

        [Option('o', "output", HelpText =
            "Path for the produced native binary. Defaults to "
            + "<inputBasename> in the current directory (no .exe "
            + "extension by default — set explicitly on Windows).")]
        public string Output { get; set; } = "";

        [Option("rid", HelpText =
            "Target .NET runtime identifier (e.g. osx-arm64, "
            + "linux-x64, win-x64). Defaults to the host's RID.")]
        public string RuntimeIdentifier { get; set; } = "";

        [Option("entry-point", Default = "_start", HelpText =
            "WASM export the emitted Program.Main invokes. Scalar "
            + "args only (i32/i64/f32/f64).")]
        public string EntryPoint { get; set; } = "_start";

        [Option("namespace", Default = "WacsAot", HelpText =
            "Root namespace for generated types in the transpiled .dll.")]
        public string Namespace { get; set; } = "WacsAot";

        [Option("simd", Default = "scalar", HelpText =
            "SIMD strategy: interpreter | scalar | intrinsics.")]
        public string Simd { get; set; } = "scalar";

        [Option("keep-temp", HelpText =
            "Don't delete the temporary build directory. Useful for "
            + "inspecting the scaffolded csproj / Program.cs.")]
        public bool KeepTemp { get; set; }

        [Option('v', "verbose", HelpText =
            "Print each step (transpile, scaffold, publish, copy).")]
        public bool Verbose { get; set; }
    }
}

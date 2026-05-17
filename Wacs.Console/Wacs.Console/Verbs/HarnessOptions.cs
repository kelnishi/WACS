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
    /// `wacs harness` — emit a typed WIT-harness `.dll` from a
    /// WIT directory. The output assembly carries one sealed
    /// harness class per world (e.g. `HelloHarness`) with a static
    /// `LoadFrom(byte[], Action&lt;WasmRuntime&gt;?)` factory plus
    /// one typed method per WIT export. Consumers reference the
    /// `.dll` and call it like any normal .NET binding — no
    /// reflection, AOT- and IL2CPP-clean.
    ///
    /// <para>The two ergonomic surfaces of
    /// <c>WACS.ComponentModel.Harness.Lib</c> (persisted vs
    /// in-memory) share one emit core; this verb wraps the
    /// persisted side. The in-memory side is consumed by future
    /// `wacs run --wit-dir` / `wacs transpile --wit-dir` flows
    /// (deferred — separate from this verb's scope).</para>
    /// </summary>
    [Verb("harness", HelpText =
        "Emit a typed WIT-harness .dll from a directory of .wit files. "
        + "Output is an AOT-clean .NET assembly with one harness class "
        + "per world exposing LoadFrom + typed per-export methods.")]
    public sealed class HarnessOptions : SharedOptions
    {
        [Value(0, MetaName = "wit-dir", HelpText =
            "Directory containing the .wit files. Recurses into deps/ "
            + "for cross-package references.")]
        public IEnumerable<string> WitDirectories { get; set; } = new List<string>();

        [Option('o', "output", Required = true, HelpText =
            "Output path for the emitted harness assembly (.dll).")]
        public string Output { get; set; } = "";

        [Option("namespace", HelpText =
            "Root C# namespace for emitted types. Default: "
            + "Wacs.ComponentModel.Harness.Generated.")]
        public string? Namespace { get; set; }

        [Option("assembly-name", HelpText =
            "Override the emitted assembly's simple-name. Default: "
            + "derived from the WIT world name (PascalCase + 'Harness').")]
        public string? AssemblyName { get; set; }
    }
}

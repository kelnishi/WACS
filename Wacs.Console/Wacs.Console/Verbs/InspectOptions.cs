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
    /// `wacs inspect file` — diagnostics + format conversion: WAT
    /// dump, wasm dump, stats, exports/imports listing. Parse-only
    /// at the runtime level — no instantiation, no execution.
    /// Accepts either .wat or .wasm input and emits the opposite
    /// format with --dump-wasm / --dump-wat.
    /// </summary>
    [Verb("inspect", HelpText =
        "Diagnostics + format conversion: parse a .wat / .wasm and "
        + "dump the opposite format, stats, or exports/imports. "
        + "WAT ↔ wasm round-trips include $names and an optional "
        + "wacs.trivia custom section for comments / annotations.")]
    public sealed class InspectOptions : SharedOptions
    {
        [Value(0, MetaName = "file", HelpText =
            "Path to a .wasm / .wat / .component.wasm input file.")]
        public IEnumerable<string> Files { get; set; } = new List<string>();

        [Option("dump-wat", HelpText =
            "Render parser-friendly WAT to stdout (or to "
            + "<basename>.wat with --output-dir). Accepts .wasm or "
            + ".wat input; round-trips back through the text parser. "
            + "Recovers function $names from the `name` custom "
            + "section and `;;` / `(@...)` trivia from `wacs.trivia` "
            + "when those sections are present.")]
        public bool DumpWat { get; set; }

        [Option("dump-wasm", HelpText =
            "Render canonical wasm binary. With --output-dir writes "
            + "<basename>.wasm; without it streams raw bytes to "
            + "stdout (pipe target). Accepts .wat or .wasm input. "
            + "WAT inputs propagate $names into the `name` custom "
            + "section and comments / annotations into `wacs.trivia` "
            + "so a subsequent --dump-wat recovers them.")]
        public bool DumpWasm { get; set; }

        [Option("output-dir", HelpText =
            "When --dump-wat / --dump-wasm is set, write the output "
            + "file into this directory instead of stdout.")]
        public string OutputDir { get; set; } = "";

        [Option("stats", HelpText =
            "Print a summary table: function count, import / export "
            + "counts, memory pages, table sizes, data segment "
            + "bytes, parsed-section sizes.")]
        public bool Stats { get; set; }

        [Option("exports", HelpText =
            "List the module's exports (name, kind, type).")]
        public bool ListExports { get; set; }

        [Option("imports", HelpText =
            "List the module's imports (module, name, kind, type).")]
        public bool ListImports { get; set; }
    }
}

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
    /// `wacs inspect file.wasm` — diagnostics: WAT dump, stats,
    /// exports/imports listing. Parse-only — no instantiation,
    /// no execution.
    /// </summary>
    [Verb("inspect", HelpText =
        "Diagnostics: parse a .wasm and dump WAT / stats / exports.")]
    public sealed class InspectOptions : SharedOptions
    {
        [Value(0, MetaName = "file", HelpText =
            "Path to a .wasm / .wat / .component.wasm input file.")]
        public IEnumerable<string> Files { get; set; } = new List<string>();

        [Option("dump-wat", HelpText =
            "Render parser-friendly WAT to stdout (or to "
            + "<basename>.wat with --output-dir). Round-trips back "
            + "through the text parser.")]
        public bool DumpWat { get; set; }

        [Option("output-dir", HelpText =
            "When --dump-wat is set, write the .wat file into this "
            + "directory instead of stdout.")]
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

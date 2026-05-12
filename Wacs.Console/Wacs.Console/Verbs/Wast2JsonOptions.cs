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
    /// `wacs wast2json file.wast -o out/` — convert a `.wast` spec
    /// test script into the wast2json bundle: a `.json` file listing
    /// commands plus a side-car `.wasm` file for each referenced
    /// module. Mirrors what `wabt`'s `wast2json` emits; the resulting
    /// directory is consumable by any spec-test runner that reads
    /// the canonical wast2json format.
    /// </summary>
    [Verb("wast2json", HelpText =
        "Convert a .wast script into a wast2json bundle (one .json + "
        + "one .wasm per module). Mirrors wabt's wast2json output.")]
    public sealed class Wast2JsonOptions : SharedOptions
    {
        [Value(0, MetaName = "file", HelpText =
            "Path to a .wast script.")]
        public IEnumerable<string> Files { get; set; } = new List<string>();

        [Option('o', "output-dir", Required = true, HelpText =
            "Directory to write the .json + side-car .wasm files into. "
            + "Created if it doesn't exist.")]
        public string OutputDir { get; set; } = "";

        [Option("base-name", HelpText =
            "Stem for the generated files (default: the input file's "
            + "name without extension). Produces <base>.json plus "
            + "<base>.0.wasm, <base>.1.wasm, … for each module.")]
        public string BaseName { get; set; } = "";
    }
}

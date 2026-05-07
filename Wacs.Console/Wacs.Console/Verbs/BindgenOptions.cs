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
    /// `wacs bindgen` — WIT ↔ C# binding generator. Two
    /// directions on the same verb:
    /// <list type="bullet">
    /// <item><b>Forward:</b> a `.wit` file or a directory tree of
    /// WIT files (with `deps/` recursion) → `[WitSource]`-tagged
    /// C# interfaces in `--output`.</item>
    /// <item><b>Reverse:</b> a transpiled `.dll` (with embedded
    /// component-type metadata) → regenerated WIT + C# bindings.
    /// Useful when the original `.wit` is gone but the shipped
    /// `.dll` is still on hand.</item>
    /// </list>
    /// Direction is inferred from the input shape: a `.dll`
    /// triggers reverse; anything else is forward.
    /// </summary>
    [Verb("bindgen", HelpText =
        "Generate C# bindings from WIT (forward) or regenerate WIT "
        + "+ bindings from a transpiled .dll (reverse). Inputs are "
        + "positional: .wit / a WIT directory / a .dll.")]
    public sealed class BindgenOptions : SharedOptions
    {
        [Value(0, MetaName = "input", HelpText =
            "Input path: a single .wit file, a directory tree of "
            + ".wit files (recurses into deps/), or a transpiled "
            + ".dll for the reverse direction.")]
        public IEnumerable<string> Files { get; set; } = new List<string>();

        [Option('o', "output", Required = true, HelpText =
            "Output directory. One C# file per emitted interface / "
            + "world / package.")]
        public string Output { get; set; } = "";

        [Option("namespace", HelpText =
            "Root C# namespace for generated bindings. Defaults to "
            + "the WIT package's qualified name (Phase 1d uses the "
            + "pinned wit-bindgen-csharp convention; explicit "
            + "override is a follow-up).")]
        public string? Namespace { get; set; }

        [Option("write-wit", HelpText =
            "Reverse mode only. Also write the raw extracted "
            + "component-type bytes alongside the .cs files (useful "
            + "for `wasm-tools component wit` round-trip inspection).")]
        public bool WriteWit { get; set; }
    }
}

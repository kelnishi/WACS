// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using CommandLine;

namespace Wacs.ComponentModel.Bindgen.Cli
{
    /// <summary>
    /// CLI flag surface for <c>wit-bindgen-wacs</c>. Exactly one
    /// of <see cref="Wit"/> / <see cref="WitDir"/> /
    /// <see cref="Dll"/> must be provided — they pick the input
    /// shape (single .wit file, .wit directory tree, or
    /// transpiled .dll for the reverse path).
    /// </summary>
    public sealed class CliOptions
    {
        [Option('w', "wit",
            HelpText = "Path to a single .wit file. Mutually exclusive with --wit-dir / --dll.")]
        public string? Wit { get; set; }

        [Option('W', "wit-dir",
            HelpText = "Path to a directory containing .wit files. Recurses into deps/ subdirectories. Mutually exclusive with --wit / --dll.")]
        public string? WitDir { get; set; }

        [Option('d', "dll",
            HelpText = "Path to a transpiled .dll. Reverse direction: extract embedded WIT metadata + regenerate C# bindings. Mutually exclusive with --wit / --wit-dir.")]
        public string? Dll { get; set; }

        [Option('o', "out", Required = true,
            HelpText = "Output directory. One C# file per generated source.")]
        public string Out { get; set; } = "";

        [Option('n', "namespace",
            HelpText = "Root C# namespace for generated bindings. Defaults to the WIT package's qualified name.")]
        public string? Namespace { get; set; }

        [Option('v', "verbose",
            HelpText = "Print per-file emission progress.")]
        public bool Verbose { get; set; }

        [Option("write-wit",
            HelpText = "Reverse mode only. Also write the extracted WIT metadata bytes alongside the .cs files.")]
        public bool WriteWit { get; set; }
    }
}

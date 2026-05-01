// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.Bindgen;
using Wacs.ComponentModel.CSharpEmit;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// `wacs bindgen` verb handler. Direction-routes on the input
    /// extension: `.dll` → reverse (regenerate from embedded WIT
    /// metadata); `.wit` → forward single-file; otherwise treat as
    /// a directory tree of WIT files. Wraps
    /// <see cref="WitForward"/> and <see cref="WitReverse"/>
    /// (shipped in <c>WACS.ComponentModel.Bindgen.Lib</c>) so the
    /// behavior matches the legacy <c>wit-bindgen-wacs</c> CLI 1:1.
    /// </summary>
    public static class BindgenHandler
    {
        public static int Execute(BindgenOptions opts)
        {
            var files = (opts.Files ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .ToList();
            if (files.Count == 0)
            {
                System.Console.Error.WriteLine(
                    "error: wacs bindgen requires an input path "
                    + "(.wit file, WIT directory, or .dll for "
                    + "reverse direction).");
                return 1;
            }
            if (files.Count > 1)
            {
                System.Console.Error.WriteLine(
                    "error: wacs bindgen accepts a single input "
                    + "path (got " + files.Count + ").");
                return 1;
            }
            if (string.IsNullOrEmpty(opts.Output))
            {
                System.Console.Error.WriteLine(
                    "error: wacs bindgen requires --output (-o) <dir>.");
                return 1;
            }

            var input = files[0];
            var output = Path.GetFullPath(opts.Output);

            // EmitOptions stays default — Phase 1d's namespace
            // shape is pinned to the wit-bindgen-csharp convention
            // and the override is a follow-up. Mirror the legacy
            // CLI's warning so users who pass --namespace get the
            // same heads-up.
            var emitOptions = new EmitOptions();
            if (!string.IsNullOrEmpty(opts.Namespace))
                System.Console.Error.WriteLine(
                    "warning: --namespace ignored (Phase 1d uses "
                    + "pinned wit-bindgen-csharp namespace conventions). "
                    + "Override is a follow-up.");

            // Direction routing on file shape:
            //   .dll        → reverse
            //   .wit        → forward, single file
            //   directory   → forward, tree (recurses into deps/)
            //   anything else → usage error
            bool isDll, isWit, isDir;
            try
            {
                isDir = Directory.Exists(input);
                isDll = !isDir && string.Equals(
                    Path.GetExtension(input), ".dll",
                    StringComparison.OrdinalIgnoreCase);
                isWit = !isDir && string.Equals(
                    Path.GetExtension(input), ".wit",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: could not classify input '" + input + "': "
                    + ex.Message);
                return 1;
            }

            if (!isDll && !isWit && !isDir)
            {
                System.Console.Error.WriteLine(
                    "error: input '" + input + "' is not a .wit file, "
                    + "WIT directory, or .dll. Cannot infer bindgen "
                    + "direction.");
                return 1;
            }
            if (!isDir && !File.Exists(input))
            {
                System.Console.Error.WriteLine(
                    "error: input file not found: " + input);
                return 1;
            }

            try
            {
                IReadOnlyList<EmittedSource> sources = isDll
                    ? RunReverse(input, output, emitOptions, opts)
                    : RunForward(input, isWit, emitOptions);

                if (sources.Count == 0)
                {
                    System.Console.Error.WriteLine(isDll
                        ? "error: .dll has no embedded WIT metadata; "
                          + "nothing to regenerate"
                        : "error: no worlds found in WIT input");
                    return isDll ? 3 : 2;
                }

                WitForward.WriteToDirectory(sources, output);
                if (opts.Verbose)
                {
                    foreach (var s in sources)
                        System.Console.WriteLine(
                            "wrote " + Path.Combine(output, s.FileName));
                }
                System.Console.WriteLine("wacs bindgen: emitted "
                    + sources.Count + " files to " + output);
                return 0;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine("error: " + ex.Message);
                if (opts.Verbose)
                    System.Console.Error.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        private static IReadOnlyList<EmittedSource> RunForward(
            string input, bool isWit, EmitOptions emitOptions)
        {
            if (isWit)
            {
                var text = File.ReadAllText(input);
                return WitForward.EmitFromText(text, emitOptions);
            }
            // directory
            return WitForward.EmitFromDirectory(input, emitOptions);
        }

        private static IReadOnlyList<EmittedSource> RunReverse(
            string dllPath, string outDir, EmitOptions emitOptions,
            BindgenOptions opts)
        {
            // --write-wit: persist the raw extracted bytes
            // alongside the .cs files. Useful for inspection /
            // wasm-tools round-trip.
            if (opts.WriteWit)
            {
                var bytes = WitReverse.ExtractWitBytes(dllPath);
                if (bytes != null)
                {
                    Directory.CreateDirectory(outDir);
                    var witPath = Path.Combine(outDir,
                        Path.GetFileNameWithoutExtension(dllPath)
                            + ".componenttype.bin");
                    File.WriteAllBytes(witPath, bytes);
                    if (opts.Verbose)
                        System.Console.WriteLine("wrote " + witPath);
                }
            }

            return WitReverse.RegenerateBindings(dllPath, emitOptions);
        }
    }
}

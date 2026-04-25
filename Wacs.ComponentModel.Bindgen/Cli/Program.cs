// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using Wacs.ComponentModel.Bindgen;
using Wacs.ComponentModel.CSharpEmit;

namespace Wacs.ComponentModel.Bindgen.Cli
{
    /// <summary>
    /// <c>wit-bindgen-wacs</c> CLI entry point. Routes to one
    /// of three input modes:
    /// <list type="bullet">
    /// <item><c>--wit foo.wit</c> — forward, single file.</item>
    /// <item><c>--wit-dir wit/</c> — forward, directory tree
    /// (with <c>deps/</c> recursion and headerless-file
    /// attribution per <see cref="WIT.WitLoader"/>).</item>
    /// <item><c>--dll foo.dll</c> — reverse, regenerate from a
    /// transpiled assembly's embedded WIT metadata.</item>
    /// </list>
    /// </summary>
    public static class Program
    {
        private const int ExitOk = 0;
        private const int ExitUsage = 1;
        private const int ExitGenerationFailure = 2;
        private const int ExitMissingMetadata = 3;

        public static int Main(string[] args)
        {
            int exit = ExitOk;
            Parser.Default.ParseArguments<CliOptions>(args)
                .WithParsed(opts => exit = Run(opts))
                .WithNotParsed(_ => exit = ExitUsage);
            return exit;
        }

        private static int Run(CliOptions opts)
        {
            int provided = (opts.Wit != null ? 1 : 0)
                         + (opts.WitDir != null ? 1 : 0)
                         + (opts.Dll != null ? 1 : 0);
            if (provided != 1)
            {
                Console.Error.WriteLine(
                    "error: exactly one of --wit / --wit-dir / --dll is required");
                return ExitUsage;
            }

            // EmitOptions's contract is pinned to wit-bindgen-csharp's
            // namespace shape; --namespace override is a Phase 2
            // follow-up (would require threading through
            // CSharpEmitter's per-file namespace derivation).
            var emitOptions = new EmitOptions();
            if (!string.IsNullOrEmpty(opts.Namespace))
                Console.Error.WriteLine(
                    "warning: --namespace ignored (Phase 1d uses pinned "
                    + "wit-bindgen-csharp namespace conventions). "
                    + "Override is a follow-up.");

            try
            {
                var sources = opts.Dll != null
                    ? RunReverse(opts.Dll, opts.Out, emitOptions, opts)
                    : RunForward(opts, emitOptions);

                if (sources.Count == 0)
                {
                    Console.Error.WriteLine(
                        opts.Dll != null
                            ? "error: .dll has no embedded WIT metadata; nothing to regenerate"
                            : "error: no worlds found in WIT input");
                    return opts.Dll != null
                        ? ExitMissingMetadata : ExitGenerationFailure;
                }

                WitForward.WriteToDirectory(sources, opts.Out);
                if (opts.Verbose)
                {
                    foreach (var s in sources)
                        Console.WriteLine(
                            "wrote " + Path.Combine(opts.Out, s.FileName));
                }
                Console.WriteLine(
                    $"wit-bindgen-wacs: emitted {sources.Count} files to {opts.Out}");
                return ExitOk;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                if (opts.Verbose)
                    Console.Error.WriteLine(ex.StackTrace);
                return ExitGenerationFailure;
            }
        }

        private static IReadOnlyList<EmittedSource> RunForward(
            CliOptions opts, EmitOptions emitOptions)
        {
            if (opts.Wit != null)
            {
                if (!File.Exists(opts.Wit))
                    throw new FileNotFoundException(
                        "WIT file not found: " + opts.Wit);
                var text = File.ReadAllText(opts.Wit);
                return WitForward.EmitFromText(text, emitOptions);
            }
            // opts.WitDir != null
            if (!Directory.Exists(opts.WitDir))
                throw new DirectoryNotFoundException(
                    "WIT directory not found: " + opts.WitDir);
            return WitForward.EmitFromDirectory(opts.WitDir!, emitOptions);
        }

        private static IReadOnlyList<EmittedSource> RunReverse(
            string dllPath, string outDir, EmitOptions emitOptions,
            CliOptions opts)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException(
                    "Assembly not found: " + dllPath);

            // The --write-wit flag writes the raw extracted bytes
            // alongside the .cs files. Useful when consumers
            // want to inspect the embedded metadata or feed it
            // through `wasm-tools component wit` for a textual
            // form.
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
                        Console.WriteLine("wrote " + witPath);
                }
            }

            return WitReverse.RegenerateBindings(dllPath, emitOptions);
        }
    }
}

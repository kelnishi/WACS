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
using Wacs.ComponentModel.Harness.Lib;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// `wacs harness` verb handler — thin wrapper over
    /// <see cref="HarnessEmitter.EmitToFile"/>. Input validation
    /// + error reporting; the emit + IL pass lives in
    /// `WACS.ComponentModel.Harness.Lib`.
    /// </summary>
    public static class HarnessHandler
    {
        public static int Execute(HarnessOptions opts)
        {
            var dirs = (opts.WitDirectories ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .ToList();
            if (dirs.Count == 0)
            {
                System.Console.Error.WriteLine(
                    "error: wacs harness requires a positional WIT directory.");
                return 1;
            }
            if (dirs.Count > 1)
            {
                System.Console.Error.WriteLine(
                    $"error: wacs harness accepts a single WIT directory (got {dirs.Count}).");
                return 1;
            }
            if (string.IsNullOrEmpty(opts.Output))
            {
                System.Console.Error.WriteLine(
                    "error: wacs harness requires --output (-o) <path.dll>.");
                return 1;
            }

            var witDir = dirs[0];
            if (!Directory.Exists(witDir))
            {
                System.Console.Error.WriteLine(
                    $"error: WIT directory not found: {witDir}");
                return 1;
            }

            var outPath = Path.GetFullPath(opts.Output);
            var outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);

            var harnessOpts = new Wacs.ComponentModel.Harness.Lib.HarnessOptions
            {
                Namespace = opts.Namespace ?? "Wacs.ComponentModel.Harness.Generated",
            };
            if (!string.IsNullOrEmpty(opts.AssemblyName))
                harnessOpts.AssemblyName = opts.AssemblyName;

            try
            {
                HarnessEmitter.EmitToFile(witDir, outPath, harnessOpts);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    System.Console.Error.WriteLine($"  inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                return 1;
            }

            System.Console.Out.WriteLine($"emitted: {outPath}");
            return 0;
        }
    }
}

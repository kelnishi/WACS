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
using Wacs.Core.Text;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// Implements <see cref="Wast2JsonOptions"/>: parses a `.wast`
    /// script via <see cref="TextScriptParser"/>, then writes the
    /// JSON + side-car wasm files through <see cref="WastJsonEmitter"/>.
    /// </summary>
    public static class Wast2JsonHandler
    {
        public static int Execute(Wast2JsonOptions opts)
        {
            var files = (opts.Files ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .ToList();
            if (files.Count == 0)
            {
                System.Console.Error.WriteLine(
                    "error: wacs wast2json requires a .wast input file.");
                return 1;
            }
            if (files.Count > 1)
            {
                System.Console.Error.WriteLine(
                    "error: wacs wast2json accepts a single input file.");
                return 1;
            }
            var input = files[0];
            if (!File.Exists(input))
            {
                System.Console.Error.WriteLine(
                    "error: input file not found: " + input);
                return 1;
            }

            // The script parser tolerates malformed inner modules
            // wrapped in assert_invalid / assert_malformed (the
            // assertion is satisfied trivially when parsing fails),
            // but a top-level parse error is fatal — surface it.
            List<ScriptCommand> commands;
            try
            {
                using var fs = new FileStream(input, FileMode.Open,
                    FileAccess.Read);
                commands = TextScriptParser.ParseWast(fs);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: failed to parse " + input + ": " + ex.Message);
                return 1;
            }

            string baseName = string.IsNullOrWhiteSpace(opts.BaseName)
                ? Path.GetFileNameWithoutExtension(input)
                : opts.BaseName;
            string outputDir = Path.GetFullPath(opts.OutputDir);

            WastJsonEmitter.Output result;
            try
            {
                result = WastJsonEmitter.Emit(
                    commands, outputDir, baseName,
                    sourceFilename: Path.GetFileName(input));
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "error: failed to emit wast2json bundle: " + ex.Message);
                return 1;
            }

            System.Console.Error.WriteLine(
                $"wrote {result.JsonPath} ({commands.Count} commands, "
                + $"{result.WasmPaths.Count} side-car modules)");
            return 0;
        }
    }
}

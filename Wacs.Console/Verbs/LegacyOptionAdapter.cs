// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// Phase 1 transitional shim: translates the new verb-style
    /// <see cref="RunOptions"/> into the legacy
    /// <see cref="CommandLineOptions"/> shape so the existing
    /// <see cref="Program.RunWithOptions"/> body keeps working
    /// during the multi-commit migration. Phase 2 deletes this
    /// shim along with the legacy options class.
    /// </summary>
    internal static class LegacyOptionAdapter
    {
        public static CommandLineOptions? AdaptForRun(RunOptions opts)
        {
            var files = opts.Files?.ToList() ?? new List<string>();
            if (files.Count == 0)
            {
                System.Console.Error.WriteLine(
                    "error: wacs run requires at least one input file.");
                return null;
            }
            if (files.Count > 1)
            {
                System.Console.Error.WriteLine(
                    "error: wacs run multi-input mode is wired in "
                    + "Phase 2; for now pass a single .wasm.");
                return null;
            }

            return new CommandLineOptions
            {
                WasmModule = files[0],
                ModuleName = opts.ModuleName,
                SkipValidation = opts.NoValidate,
                LogProg = opts.Verbose,
                Render = false,
                LogGas = opts.LogGas,
                LimitGas = opts.GasLimit,
                LogProgressEvery = opts.LogProgressEvery,
                LogInstructionExecution = opts.LogExecution,
                CalculateLineNumbers = opts.CalculateLines,
                CollectStats = opts.Stats,
                Profile = opts.Profile,
                InvokeFunction = opts.Call,
                Transpile = string.Equals(opts.Engine, "transpiler",
                    System.StringComparison.OrdinalIgnoreCase),
                Aot = string.Equals(opts.Engine, "transpiler",
                    System.StringComparison.OrdinalIgnoreCase),
                SuperInstructions = opts.SuperInstructions,
                UseSwitch = opts.UseSwitch,
                AotSave = "",
                AotSimd = opts.Simd,
                AotNoTailCalls = opts.NoTailCalls,
                AotMaxFnSize = opts.MaxFnSize,
                AotDataStorage = opts.DataStorage,
                EnvironmentVars = opts.EnvironmentVars
                    ?? System.Linq.Enumerable.Empty<string>(),
                Directories = opts.Directories
                    ?? System.Linq.Enumerable.Empty<string>(),
                ExecutableArgs = opts.Args
                    ?? System.Linq.Enumerable.Empty<string>(),
            };
        }
    }
}

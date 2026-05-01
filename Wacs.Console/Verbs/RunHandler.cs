// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.Console.Verbs
{
    /// <summary>
    /// Phase 1 stub. Phase 2 ports the existing
    /// <see cref="Wacs.Console.Program"/> interpreter wiring (gas,
    /// profile, instr logging, line numbers, stats, super, switch,
    /// invoke, --aot/--engine transpiler) plus wasm-transpile's
    /// RunMultiOrInterpreter and RunComponent into this class.
    /// </summary>
    public static class RunHandler
    {
        public static int Execute(RunOptions opts)
        {
            // Phase 1 placeholder: the verb skeleton compiles + parses
            // flags; full behavior comes in the next commit. Until
            // then, route through the legacy single-file Program.cs
            // path so existing scripts keep working during the
            // transition.
            var legacy = LegacyOptionAdapter.AdaptForRun(opts);
            if (legacy == null) return 1;
            return Program.RunWithOptions(legacy);
        }
    }
}

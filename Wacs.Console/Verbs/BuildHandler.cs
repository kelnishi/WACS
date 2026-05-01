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
    /// Phase 1 stub. Phase 3 ports wasm-transpile's transpile-only
    /// flow (single + multi + component, with --emit-main wiring)
    /// into this class.
    /// </summary>
    public static class BuildHandler
    {
        public static int Execute(BuildOptions opts)
        {
            System.Console.Error.WriteLine(
                "wacs build: not yet implemented in this build "
                + "(skeleton phase). Use `wasm-transpile` for now.");
            return 2;
        }
    }
}

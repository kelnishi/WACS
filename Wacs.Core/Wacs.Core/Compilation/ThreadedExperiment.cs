// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

// Minimal InlineIL.Fody proof-of-concept. Dispatch(x) uses a raw IL switch
// directly to three labels. Fody runs at build time and replaces each
// IL.Emit.Switch / IL.Emit.Br call with the corresponding raw IL instruction;
// the final assembly has no runtime dependency on InlineIL or Fody.
//
// Validates that:
//   1. The build toolchain invokes Fody on Wacs.Core and it processes this
//      file successfully.
//   2. IL.Emit.Switch + string label names resolves against C# labels in the
//      same method.
//   3. The emitted IL runs correctly.

using InlineIL;
using static InlineIL.IL.Emit;

namespace Wacs.Core.Compilation
{
    internal static class ThreadedExperiment
    {
        /// <summary>Returns 100/200/300 for x = 0/1/2; -1 otherwise. Implemented via
        /// raw <c>switch</c> IL instruction for Fody verification.</summary>
        public static int Dispatch(int x)
        {
            // Pattern from the InlineIL.Fody README: each branch ends with IL-level
            // Ret() (not a C# `return`), and the method body ends with
            // `return IL.Unreachable()` so the C# compiler sees a normal control-flow
            // exit (IL.Unreachable throws NotImplementedException at runtime but is
            // rewritten at build time into nothing). MarkLabel positions between
            // sequential Ret()s are allowed because C# doesn't try to eliminate them
            // when they're flanked by IL-level opcodes.

            Ldarg(nameof(x));
            Switch("L_0", "L_1", "L_2");
            Br("L_default");

            IL.MarkLabel("L_0");
            Ldc_I4(100);
            Ret();

            IL.MarkLabel("L_1");
            Ldc_I4(200);
            Ret();

            IL.MarkLabel("L_2");
            Ldc_I4(300);
            Ret();

            IL.MarkLabel("L_default");
            Ldc_I4(-1);
            Ret();

            throw IL.Unreachable();
        }
    }
}

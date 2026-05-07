// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Partial class extended by <c>DispatchGenerator</c> at compile time. The generated file
    /// (GeneratedDispatcher.g.cs) supplies <c>TryDispatch(ExecContext, ushort)</c> — a switch
    /// that pops operands off the OpStack, runs the inlined body of each [OpSource]-marked
    /// method, and pushes the result.
    ///
    /// This is the backbone of the optional byte-stream runtime. See SwitchRuntime for the
    /// driving loop. The original polymorphic Execute path in WasmRuntimeExecution remains
    /// the canonical runtime and is unaffected.
    /// </summary>
    public static partial class GeneratedDispatcher
    {
    }
}

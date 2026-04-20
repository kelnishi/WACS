// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.OpCodes;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Marks a method as the handler for a specific WASM opcode in the monolithic-switch
    /// interpreter. The source generator (<c>DispatchGenerator</c>) inspects the parameter list
    /// and emits the call site automatically:
    /// <list type="bullet">
    ///   <item><c>ExecContext ctx</c>: forwarded.</item>
    ///   <item><c>ref int pc</c>: forwarded; handler may mutate for control flow.</item>
    ///   <item><c>[Imm] T param</c>: generator emits <c>StreamReader.Read*</c> based on T and advances <c>pc</c>.</item>
    ///   <item>Trailing primitive params (no attribute): popped from the OpStack in reverse order (matching the <c>[OpSource]</c> rule).</item>
    ///   <item>Primitive return value: pushed onto the OpStack.</item>
    /// </list>
    /// Use <c>[OpSource]</c> for the common numeric case (stack-only, no ExecContext, no immediates);
    /// use <c>[OpHandler]</c> for everything else.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class OpHandlerAttribute : Attribute
    {
        public readonly ByteCode Op;

        public OpHandlerAttribute(OpCode code)   => Op = code;
        public OpHandlerAttribute(GcCode code)   => Op = code;
        public OpHandlerAttribute(ExtCode code)  => Op = code;
        public OpHandlerAttribute(SimdCode code) => Op = code;
        public OpHandlerAttribute(AtomCode code) => Op = code;
        public OpHandlerAttribute(WacsCode code) => Op = code;
    }

    /// <summary>
    /// Marks a parameter as a pre-decoded immediate read from the annotated bytecode stream.
    /// The generator emits the appropriate <c>StreamReader</c> call based on the parameter's
    /// CLR type: <c>int → ReadS32</c>, <c>uint → ReadU32</c>, <c>long → ReadS64</c>,
    /// <c>ulong → ReadU64</c>, <c>float → ReadF32</c>, <c>double → ReadF64</c>,
    /// <c>byte → ReadU8</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
    public sealed class ImmAttribute : Attribute
    {
    }
}

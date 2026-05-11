// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Instructions;

namespace Wacs.Core.OpCodes
{
    /// <summary>
    /// Per-opcode stack arity — how many operands an instruction
    /// pops and pushes. Used by the folded / S-expression renderer
    /// in <c>Wacs.Core.Text.TextModuleWriter</c> to decide whether
    /// a given instruction's operands can be folded into the
    /// preceding chain.
    ///
    /// <para>This table covers the "pure" (no side-effect-on-control
    /// or polymorphic) opcodes. Control instructions
    /// (block/loop/if/br/br_if/br_table/return/call/call_indirect/throw)
    /// and the rare polymorphic ones (select, drop in some paths)
    /// are detected separately by the folder via their type, so
    /// they don't appear here.</para>
    /// </summary>
    public static class OpcodeArity
    {
        /// <summary>
        /// Try to resolve <paramref name="inst"/>'s arity. Returns
        /// <c>false</c> for instructions that need module context to
        /// determine their arity (call, call_indirect) or for control
        /// instructions the folder must handle specially.
        /// </summary>
        public static bool TryGet(InstructionBase inst, out int consume, out int produce)
        {
            consume = 0;
            produce = 0;
            // Instructions whose stackDiff is reliable: most numeric and
            // memory ops bake (consume, produce) into the constructor.
            // We can't derive (consume, produce) from a single signed
            // StackDiff (e.g. +1 might be 0 in / 1 out or 1 in / 2 out),
            // so this table holds the spec-known shapes.
            switch (inst.Op.x00)
            {
                // ---- Numeric constants — leaf producers ---------------
                case OpCode.I32Const:
                case OpCode.I64Const:
                case OpCode.F32Const:
                case OpCode.F64Const:
                    produce = 1; return true;

                // ---- Numeric unary (1 → 1) ----------------------------
                case OpCode.I32Eqz:
                case OpCode.I64Eqz:
                case OpCode.I32Clz: case OpCode.I32Ctz: case OpCode.I32Popcnt:
                case OpCode.I64Clz: case OpCode.I64Ctz: case OpCode.I64Popcnt:
                case OpCode.F32Abs: case OpCode.F32Neg: case OpCode.F32Sqrt:
                case OpCode.F32Ceil: case OpCode.F32Floor: case OpCode.F32Trunc:
                case OpCode.F32Nearest:
                case OpCode.F64Abs: case OpCode.F64Neg: case OpCode.F64Sqrt:
                case OpCode.F64Ceil: case OpCode.F64Floor: case OpCode.F64Trunc:
                case OpCode.F64Nearest:
                case OpCode.I32WrapI64:
                case OpCode.I32TruncF32S: case OpCode.I32TruncF32U:
                case OpCode.I32TruncF64S: case OpCode.I32TruncF64U:
                case OpCode.I64ExtendI32S: case OpCode.I64ExtendI32U:
                case OpCode.I64TruncF32S: case OpCode.I64TruncF32U:
                case OpCode.I64TruncF64S: case OpCode.I64TruncF64U:
                case OpCode.F32ConvertI32S: case OpCode.F32ConvertI32U:
                case OpCode.F32ConvertI64S: case OpCode.F32ConvertI64U:
                case OpCode.F32DemoteF64:
                case OpCode.F64ConvertI32S: case OpCode.F64ConvertI32U:
                case OpCode.F64ConvertI64S: case OpCode.F64ConvertI64U:
                case OpCode.F64PromoteF32:
                case OpCode.I32ReinterpretF32:
                case OpCode.I64ReinterpretF64:
                case OpCode.F32ReinterpretI32:
                case OpCode.F64ReinterpretI64:
                case OpCode.I32Extend8S: case OpCode.I32Extend16S:
                case OpCode.I64Extend8S: case OpCode.I64Extend16S:
                case OpCode.I64Extend32S:
                    consume = 1; produce = 1; return true;

                // ---- Numeric binary / compare (2 → 1) -----------------
                case OpCode.I32Eq: case OpCode.I32Ne:
                case OpCode.I32LtS: case OpCode.I32LtU:
                case OpCode.I32GtS: case OpCode.I32GtU:
                case OpCode.I32LeS: case OpCode.I32LeU:
                case OpCode.I32GeS: case OpCode.I32GeU:
                case OpCode.I64Eq: case OpCode.I64Ne:
                case OpCode.I64LtS: case OpCode.I64LtU:
                case OpCode.I64GtS: case OpCode.I64GtU:
                case OpCode.I64LeS: case OpCode.I64LeU:
                case OpCode.I64GeS: case OpCode.I64GeU:
                case OpCode.F32Eq: case OpCode.F32Ne:
                case OpCode.F32Lt: case OpCode.F32Gt: case OpCode.F32Le: case OpCode.F32Ge:
                case OpCode.F64Eq: case OpCode.F64Ne:
                case OpCode.F64Lt: case OpCode.F64Gt: case OpCode.F64Le: case OpCode.F64Ge:
                case OpCode.I32Add: case OpCode.I32Sub: case OpCode.I32Mul:
                case OpCode.I32DivS: case OpCode.I32DivU:
                case OpCode.I32RemS: case OpCode.I32RemU:
                case OpCode.I32And: case OpCode.I32Or: case OpCode.I32Xor:
                case OpCode.I32Shl: case OpCode.I32ShrS: case OpCode.I32ShrU:
                case OpCode.I32Rotl: case OpCode.I32Rotr:
                case OpCode.I64Add: case OpCode.I64Sub: case OpCode.I64Mul:
                case OpCode.I64DivS: case OpCode.I64DivU:
                case OpCode.I64RemS: case OpCode.I64RemU:
                case OpCode.I64And: case OpCode.I64Or: case OpCode.I64Xor:
                case OpCode.I64Shl: case OpCode.I64ShrS: case OpCode.I64ShrU:
                case OpCode.I64Rotl: case OpCode.I64Rotr:
                case OpCode.F32Add: case OpCode.F32Sub: case OpCode.F32Mul: case OpCode.F32Div:
                case OpCode.F32Min: case OpCode.F32Max: case OpCode.F32Copysign:
                case OpCode.F64Add: case OpCode.F64Sub: case OpCode.F64Mul: case OpCode.F64Div:
                case OpCode.F64Min: case OpCode.F64Max: case OpCode.F64Copysign:
                    consume = 2; produce = 1; return true;

                // ---- Variable instructions ---------------------------
                case OpCode.LocalGet:    produce = 1; return true;
                case OpCode.LocalSet:    consume = 1; return true;
                case OpCode.LocalTee:    consume = 1; produce = 1; return true;
                case OpCode.GlobalGet:   produce = 1; return true;
                case OpCode.GlobalSet:   consume = 1; return true;

                // ---- Reference (leaf producers / unary) --------------
                case OpCode.RefNull:     produce = 1; return true;
                case OpCode.RefFunc:     produce = 1; return true;
                case OpCode.RefIsNull:   consume = 1; produce = 1; return true;
                case OpCode.RefAsNonNull:consume = 1; produce = 1; return true;

                // ---- Memory loads (1 → 1: addr → value) --------------
                case OpCode.I32Load: case OpCode.I64Load:
                case OpCode.F32Load: case OpCode.F64Load:
                case OpCode.I32Load8S: case OpCode.I32Load8U:
                case OpCode.I32Load16S: case OpCode.I32Load16U:
                case OpCode.I64Load8S: case OpCode.I64Load8U:
                case OpCode.I64Load16S: case OpCode.I64Load16U:
                case OpCode.I64Load32S: case OpCode.I64Load32U:
                    consume = 1; produce = 1; return true;

                // ---- Memory stores (2 → 0: addr + value) -------------
                case OpCode.I32Store: case OpCode.I64Store:
                case OpCode.F32Store: case OpCode.F64Store:
                case OpCode.I32Store8: case OpCode.I32Store16:
                case OpCode.I64Store8: case OpCode.I64Store16: case OpCode.I64Store32:
                    consume = 2; return true;

                // ---- Drop (polymorphic 1 → 0) -------------------------
                case OpCode.Drop:        consume = 1; return true;

                // ---- Memory size / grow ------------------------------
                case OpCode.MemorySize:  produce = 1; return true;
                case OpCode.MemoryGrow:  consume = 1; produce = 1; return true;

                default:
                    return false;
            }
        }
    }
}

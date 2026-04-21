// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using Wacs.Core.OpCodes;
using Wacs.Core.Text;
using Xunit;

namespace Wacs.Core.Test
{
    public class MnemonicsTests
    {
        [Theory]
        [InlineData("unreachable",       OpCode.Unreachable)]
        [InlineData("nop",               OpCode.Nop)]
        [InlineData("block",             OpCode.Block)]
        [InlineData("if",                OpCode.If)]
        [InlineData("end",               OpCode.End)]
        [InlineData("br",                OpCode.Br)]
        [InlineData("call",              OpCode.Call)]
        [InlineData("return",            OpCode.Return)]
        [InlineData("drop",              OpCode.Drop)]
        [InlineData("local.get",         OpCode.LocalGet)]
        [InlineData("global.set",        OpCode.GlobalSet)]
        [InlineData("i32.const",         OpCode.I32Const)]
        [InlineData("i32.add",           OpCode.I32Add)]
        [InlineData("i64.mul",           OpCode.I64Mul)]
        [InlineData("f32.sqrt",          OpCode.F32Sqrt)]
        [InlineData("f64.convert_i32_s", OpCode.F64ConvertI32S)]
        [InlineData("i32.extend8_s",     OpCode.I32Extend8S)]
        [InlineData("ref.null",          OpCode.RefNull)]
        [InlineData("ref.func",          OpCode.RefFunc)]
        [InlineData("try_table",         OpCode.TryTable)]
        [InlineData("throw",             OpCode.Throw)]
        [InlineData("call_ref",          OpCode.CallRef)]
        public void OpCode_mnemonics_resolve(string mnemonic, OpCode expected)
        {
            Assert.True(Mnemonics.TryLookup(mnemonic, out var code));
            Assert.Equal(expected, code.x00);
        }

        [Theory]
        [InlineData("i32.trunc_sat_f32_s", ExtCode.I32TruncSatF32S)]
        [InlineData("memory.copy",         ExtCode.MemoryCopy)]
        [InlineData("table.size",          ExtCode.TableSize)]
        [InlineData("data.drop",           ExtCode.DataDrop)]
        public void ExtCode_mnemonics_resolve(string mnemonic, ExtCode expected)
        {
            Assert.True(Mnemonics.TryLookup(mnemonic, out var code));
            Assert.Equal(OpCode.FC, code.x00);
            Assert.Equal(expected, code.xFC);
        }

        [Theory]
        [InlineData("struct.new",  GcCode.StructNew)]
        [InlineData("array.get",   GcCode.ArrayGet)]
        [InlineData("ref.test",    GcCode.RefTest)]
        [InlineData("br_on_cast",  GcCode.BrOnCast)]
        [InlineData("i31.get_s",   GcCode.I31GetS)]
        public void GcCode_mnemonics_resolve(string mnemonic, GcCode expected)
        {
            Assert.True(Mnemonics.TryLookup(mnemonic, out var code));
            Assert.Equal(OpCode.FB, code.x00);
            Assert.Equal(expected, code.xFB);
        }

        [Theory]
        [InlineData("v128.load",  SimdCode.V128Load)]
        [InlineData("v128.const", SimdCode.V128Const)]
        [InlineData("i32x4.add",  SimdCode.I32x4Add)]
        [InlineData("f64x2.mul",  SimdCode.F64x2Mul)]
        public void SimdCode_mnemonics_resolve(string mnemonic, SimdCode expected)
        {
            Assert.True(Mnemonics.TryLookup(mnemonic, out var code));
            Assert.Equal(OpCode.FD, code.x00);
            Assert.Equal(expected, code.xFD);
        }

        [Theory]
        [InlineData("memory.atomic.notify", AtomCode.MemoryAtomicNotify)]
        [InlineData("i32.atomic.load",      AtomCode.I32AtomicLoad)]
        [InlineData("i64.atomic.rmw.add",   AtomCode.I64AtomicRmwAdd)]
        [InlineData("atomic.fence",         AtomCode.AtomicFence)]
        public void AtomCode_mnemonics_resolve(string mnemonic, AtomCode expected)
        {
            Assert.True(Mnemonics.TryLookup(mnemonic, out var code));
            Assert.Equal(OpCode.FE, code.x00);
            Assert.Equal(expected, code.xFE);
        }

        [Fact]
        public void Relaxed_simd_canonical_wins_over_prototype()
        {
            // "i8x16.relaxed_swizzle" is declared twice in SimdCode: canonical
            // at 0x100 and a prototype alias at 0xA2. The registry must land on
            // the canonical entry.
            Assert.True(Mnemonics.TryLookup("i8x16.relaxed_swizzle", out var code));
            Assert.Equal(OpCode.FD, code.x00);
            Assert.Equal(SimdCode.I8x16RelaxedSwizzle, code.xFD);
            Assert.NotEqual(SimdCode.Prototype_I8x16RelaxedSwizzle, code.xFD);
        }

        [Fact]
        public void WacsCode_super_ops_are_not_registered()
        {
            // These are internal rewriter outputs; user WAT must not be able
            // to reference them.
            Assert.False(Mnemonics.TryLookup("stack.val",     out _));
            Assert.False(Mnemonics.TryLookup("i32.lladd",     out _));
            Assert.False(Mnemonics.TryLookup("reg.prog",      out _));
            Assert.False(Mnemonics.TryLookup("i32.fused.add", out _));
        }

        [Fact]
        public void Display_only_mnemonics_are_filtered()
        {
            // GC's "ref.test (ref null)" and "ref.cast (ref null)" are rendering
            // labels, not WAT tokens. Parser handles the null-qualified form
            // via operand shape.
            Assert.False(Mnemonics.TryLookup("ref.test (ref null)", out _));
            Assert.False(Mnemonics.TryLookup("ref.cast (ref null)", out _));
        }

        [Fact]
        public void Unknown_mnemonic_misses()
        {
            Assert.False(Mnemonics.TryLookup("not.a.real.op", out _));
            Assert.False(Mnemonics.TryLookup("",              out _));
            Assert.False(Mnemonics.TryLookup("MODULE",        out _));  // case sensitive
        }

        [Fact]
        public void Select_resolves_to_plain_Select_not_SelectT()
        {
            // Both OpCode.Select (0x1B) and OpCode.SelectT (0x1C) declare
            // [OpCode("select")]. The registry stores the first one (plain
            // Select); the parser promotes to SelectT at parse time when an
            // inline (result ...) annotation is present.
            Assert.True(Mnemonics.TryLookup("select", out var code));
            Assert.Equal(OpCode.Select, code.x00);
        }

        [Fact]
        public void Registry_covers_full_core_opcode_surface()
        {
            // Sanity-check the table is large enough to cover the spec. If
            // this number ever plummets, something in the reflection pass
            // broke. Upper-bound it too so additions here get visibility.
            Assert.InRange(Mnemonics.Count, 500, 1000);
        }
    }
}

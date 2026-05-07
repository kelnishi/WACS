// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using Wacs.Core;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Numeric;
using Wacs.Core.Instructions.Reference;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Transpiler.AOT;
using Xunit;
using WasmExpression = Wacs.Core.Types.Expression;

namespace Wacs.Transpiler.Test
{
    /// <summary>
    /// Phase 1 of the init-data codec test suite. Covers header validation
    /// plus scalar / array-of-primitive sections. Expression, DefType, and
    /// ref-typed Value payloads are pending phases 2-3; their round-trip
    /// tests live in follow-up commits.
    /// </summary>
    public class InitDataCodecTests
    {
        // =================================================================
        // LEB128 primitive round-trips
        // =================================================================

        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        [InlineData(127u)]
        [InlineData(128u)]
        [InlineData(16383u)]
        [InlineData(16384u)]
        [InlineData(1u << 28)]
        [InlineData(uint.MaxValue)]
        public void VarUInt32_RoundTrip(uint value)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                InitDataCodec.WriteVarUInt32(w, value);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            Assert.Equal(value, InitDataCodec.ReadVarUInt32(r));
            Assert.Equal(ms.Length, ms.Position);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(63)]
        [InlineData(-64)]
        [InlineData(64)]
        [InlineData(-65)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void VarInt32_RoundTrip(int value)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                InitDataCodec.WriteVarInt32(w, value);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            Assert.Equal(value, InitDataCodec.ReadVarInt32(r));
            Assert.Equal(ms.Length, ms.Position);
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(1L)]
        [InlineData(-1L)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        [InlineData(1L << 48)]
        [InlineData(-(1L << 48))]
        public void VarInt64_RoundTrip(long value)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                InitDataCodec.WriteVarInt64(w, value);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            Assert.Equal(value, InitDataCodec.ReadVarInt64(r));
            Assert.Equal(ms.Length, ms.Position);
        }

        [Theory]
        [InlineData(0UL)]
        [InlineData(1UL)]
        [InlineData(ulong.MaxValue)]
        [InlineData(1UL << 50)]
        public void VarUInt64_RoundTrip(ulong value)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                InitDataCodec.WriteVarUInt64(w, value);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            Assert.Equal(value, InitDataCodec.ReadVarUInt64(r));
            Assert.Equal(ms.Length, ms.Position);
        }

        // =================================================================
        // Header validation
        // =================================================================

        [Fact]
        public void Decode_RejectsBadMagic()
        {
            var bytes = new byte[]
            {
                (byte)'N', (byte)'O', (byte)'P', (byte)'E',
                (byte)'I', (byte)'N', (byte)'I', (byte)'T',
                1, 0, 0, 0,
            };
            Assert.Throws<InvalidDataException>(() => InitDataCodec.Decode(bytes));
        }

        [Fact]
        public void Decode_RejectsNewerMajor()
        {
            var bytes = new byte[]
            {
                (byte)'W', (byte)'A', (byte)'C', (byte)'S',
                (byte)'I', (byte)'N', (byte)'I', (byte)'T',
                (byte)(InitDataCodec.VersionMajor + 1), 0, 0, 0,
            };
            Assert.Throws<InvalidDataException>(() => InitDataCodec.Decode(bytes));
        }

        [Fact]
        public void Decode_AcceptsEmptyV1()
        {
            var empty = new ModuleInitData();
            var bytes = InitDataCodec.Encode(empty);
            var back = InitDataCodec.Decode(bytes);
            AssertEquivalentScalarFields(empty, back);
        }

        [Fact]
        public void Decode_SkipsUnknownSections()
        {
            // Encode a normal payload, then splice in an unknown section
            // between the header and the real sections.
            var data = new ModuleInitData
            {
                StartFuncIndex = 42,
                ImportFuncCount = 3,
                TotalFuncCount = 7,
            };
            var baseline = InitDataCodec.Encode(data);

            // Header is 12 bytes; splice an unknown tag right after.
            using var ms = new MemoryStream();
            ms.Write(baseline, 0, 12);
            ms.WriteByte(0xF7);                             // unknown tag
            ms.WriteByte(3);                                // length = 3 (LEB single byte)
            ms.Write(new byte[] { 0xDE, 0xAD, 0xBE }, 0, 3); // garbage payload
            ms.Write(baseline, 12, baseline.Length - 12);

            var spliced = ms.ToArray();
            var back = InitDataCodec.Decode(spliced);
            Assert.Equal(42, back.StartFuncIndex);
            Assert.Equal(3, back.ImportFuncCount);
            Assert.Equal(7, back.TotalFuncCount);
        }

        // =================================================================
        // Section-level round-trips (full Encode/Decode path)
        // =================================================================

        [Fact]
        public void Memories_RoundTrip()
        {
            var data = new ModuleInitData
            {
                Memories = new (long, long?)[]
                {
                    (0, null),
                    (1, 10),
                    (1024, null),
                    (65536, 131072),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(data.Memories.Length, back.Memories.Length);
            for (int i = 0; i < data.Memories.Length; i++)
            {
                Assert.Equal(data.Memories[i].min, back.Memories[i].min);
                Assert.Equal(data.Memories[i].max, back.Memories[i].max);
            }
        }

        [Fact]
        public void Globals_ScalarsRoundTrip()
        {
            Value i32 = default; i32.Type = ValType.I32; i32.Data.Int32 = 42;
            Value i64 = default; i64.Type = ValType.I64; i64.Data.Int64 = 0x1234_5678_9ABC_DEF0L;
            Value f32 = default; f32.Type = ValType.F32; f32.Data.Float32 = 3.14f;
            Value f64 = default; f64.Type = ValType.F64; f64.Data.Float64 = 2.718281828;

            var data = new ModuleInitData
            {
                Globals = new (ValType, Mutability, Value)[]
                {
                    (ValType.I32, Mutability.Immutable, i32),
                    (ValType.I64, Mutability.Mutable, i64),
                    (ValType.F32, Mutability.Immutable, f32),
                    (ValType.F64, Mutability.Mutable, f64),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(4, back.Globals.Length);
            Assert.Equal(ValType.I32, back.Globals[0].type);
            Assert.Equal(42, back.Globals[0].init.Data.Int32);
            Assert.Equal(Mutability.Mutable, back.Globals[1].mut);
            Assert.Equal(0x1234_5678_9ABC_DEF0L, back.Globals[1].init.Data.Int64);
            Assert.Equal(3.14f, back.Globals[2].init.Data.Float32);
            Assert.Equal(2.718281828, back.Globals[3].init.Data.Float64);
        }

        [Fact]
        public void FuncTypeHashes_NullVsEmptyVsPopulated()
        {
            // null → null
            var a = new ModuleInitData { FuncTypeHashes = null };
            Assert.Null(InitDataCodec.Decode(InitDataCodec.Encode(a)).FuncTypeHashes);

            // empty → empty
            var b = new ModuleInitData { FuncTypeHashes = System.Array.Empty<int>() };
            var bBack = InitDataCodec.Decode(InitDataCodec.Encode(b)).FuncTypeHashes;
            Assert.NotNull(bBack);
            Assert.Empty(bBack!);

            // populated → equal
            var c = new ModuleInitData { FuncTypeHashes = new[] { 0, -1, int.MaxValue, int.MinValue, 42 } };
            Assert.Equal(c.FuncTypeHashes, InitDataCodec.Decode(InitDataCodec.Encode(c)).FuncTypeHashes);
        }

        [Fact]
        public void FuncTypeSuperHashes_JaggedRoundTrip()
        {
            var data = new ModuleInitData
            {
                FuncTypeSuperHashes = new[]
                {
                    new[] { 1 },
                    new[] { 2, 3, 4 },
                    System.Array.Empty<int>(),
                    new[] { int.MaxValue, int.MinValue },
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data)).FuncTypeSuperHashes;
            Assert.NotNull(back);
            Assert.Equal(data.FuncTypeSuperHashes.Length, back!.Length);
            for (int i = 0; i < back.Length; i++)
                Assert.Equal(data.FuncTypeSuperHashes[i], back[i]);
        }

        [Fact]
        public void TypeIsFunc_RoundTrip()
        {
            var data = new ModuleInitData
            {
                TypeIsFunc = new[] { true, false, true, true, false },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data)).TypeIsFunc;
            Assert.Equal(data.TypeIsFunc, back);
        }

        [Fact]
        public void ActiveDataSegments_RoundTrip()
        {
            var data = new ModuleInitData
            {
                ActiveDataSegments = new (int, int, int)[]
                {
                    (0, 0, 0),
                    (0, 1024, 1),
                    (1, 65536, 2),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(data.ActiveDataSegments, back.ActiveDataSegments);
        }

        [Fact]
        public void ActiveElementSegments_RoundTrip()
        {
            var data = new ModuleInitData
            {
                ActiveElementSegments = new (int, int, int[])[]
                {
                    (0, 0, new[] { 0, 1, 2 }),
                    (1, 100, new[] { 5 }),
                    (0, 50, System.Array.Empty<int>()),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(data.ActiveElementSegments.Length, back.ActiveElementSegments.Length);
            for (int i = 0; i < back.ActiveElementSegments.Length; i++)
            {
                Assert.Equal(data.ActiveElementSegments[i].tableIdx, back.ActiveElementSegments[i].tableIdx);
                Assert.Equal(data.ActiveElementSegments[i].offset, back.ActiveElementSegments[i].offset);
                Assert.Equal(data.ActiveElementSegments[i].funcIndices, back.ActiveElementSegments[i].funcIndices);
            }
        }

        [Fact]
        public void DeferredElemGlobals_RoundTrip()
        {
            var data = new ModuleInitData
            {
                DeferredElemGlobals = new List<(int, int, int)>
                {
                    (0, 0, 5),
                    (1, 3, 2),
                    (0, 7, 0),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(data.DeferredElemGlobals, back.DeferredElemGlobals);
        }

        [Fact]
        public void StartFuncIndex_RoundTrip()
        {
            foreach (int idx in new[] { -1, 0, 1, 42, int.MaxValue })
            {
                var data = new ModuleInitData { StartFuncIndex = idx };
                Assert.Equal(idx, InitDataCodec.Decode(InitDataCodec.Encode(data)).StartFuncIndex);
            }
        }

        [Fact]
        public void SegmentBaseIds_RoundTrip()
        {
            var data = new ModuleInitData { DataSegmentBaseId = 17, ElemSegmentBaseId = 42 };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(17, back.DataSegmentBaseId);
            Assert.Equal(42, back.ElemSegmentBaseId);
        }

        [Fact]
        public void ActiveIndicesArrays_RoundTrip()
        {
            var data = new ModuleInitData
            {
                ActiveElemIndices = new[] { 0, 2, 5 },
                ActiveDataIndices = new[] { 1, 3 },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(data.ActiveElemIndices, back.ActiveElemIndices);
            Assert.Equal(data.ActiveDataIndices, back.ActiveDataIndices);
        }

        [Fact]
        public void SavedDataSegments_RoundTrip()
        {
            var data = new ModuleInitData
            {
                SavedDataSegments = new Dictionary<int, byte[]>
                {
                    [0] = new byte[] { 1, 2, 3, 4, 5 },
                    [5] = System.Array.Empty<byte>(),
                    [99] = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(data.SavedDataSegments.Count, back.SavedDataSegments.Count);
            foreach (var kv in data.SavedDataSegments)
            {
                Assert.True(back.SavedDataSegments.ContainsKey(kv.Key));
                Assert.Equal(kv.Value, back.SavedDataSegments[kv.Key]);
            }
        }

        [Fact]
        public void Counts_RoundTrip()
        {
            var data = new ModuleInitData
            {
                ImportFuncCount = 3,
                TotalFuncCount = 17,
                ImportedTagCount = 2,
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(3, back.ImportFuncCount);
            Assert.Equal(17, back.TotalFuncCount);
            Assert.Equal(2, back.ImportedTagCount);
        }

        [Fact]
        public void GcGlobalInits_RoundTrip()
        {
            var data = new ModuleInitData
            {
                GcGlobalInits = new List<GcGlobalInit>
                {
                    new GcGlobalInit
                    {
                        GlobalIndex = 0,
                        TypeIndex = 3,
                        InitKind = 0,
                        Params = new long[] { 5, 10 },
                        ElementValType = 0x7F,
                    },
                    new GcGlobalInit
                    {
                        GlobalIndex = 1,
                        TypeIndex = 7,
                        InitKind = 2,
                        Params = System.Array.Empty<long>(),
                        ElementValType = 0,
                    },
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data)).GcGlobalInits;
            Assert.Equal(data.GcGlobalInits.Count, back.Count);
            for (int i = 0; i < back.Count; i++)
            {
                Assert.Equal(data.GcGlobalInits[i].GlobalIndex, back[i].GlobalIndex);
                Assert.Equal(data.GcGlobalInits[i].TypeIndex, back[i].TypeIndex);
                Assert.Equal(data.GcGlobalInits[i].InitKind, back[i].InitKind);
                Assert.Equal(data.GcGlobalInits[i].Params, back[i].Params);
                Assert.Equal(data.GcGlobalInits[i].ElementValType, back[i].ElementValType);
            }
        }

        // =================================================================
        // Composite / omnibus round-trip
        // =================================================================

        [Fact]
        public void FullData_ScalarSubset_RoundTrip()
        {
            Value i32 = default; i32.Type = ValType.I32; i32.Data.Int32 = 99;
            var data = new ModuleInitData
            {
                Memories = new (long, long?)[] { (1, 2), (4, null) },
                Globals = new (ValType, Mutability, Value)[] { (ValType.I32, Mutability.Mutable, i32) },
                FuncTypeHashes = new[] { 111, 222, 333 },
                TypeHashes = new[] { 1, 2 },
                TypeIsFunc = new[] { true, false },
                ActiveDataSegments = new (int, int, int)[] { (0, 128, 0) },
                ActiveElementSegments = new (int, int, int[])[] { (0, 0, new[] { 0, 1 }) },
                ActiveElemIndices = new[] { 0 },
                ActiveDataIndices = new[] { 0 },
                SavedDataSegments = new Dictionary<int, byte[]> { [0] = new byte[] { 42 } },
                StartFuncIndex = 5,
                DataSegmentBaseId = 0,
                ElemSegmentBaseId = 0,
                ImportFuncCount = 1,
                TotalFuncCount = 4,
                ImportedTagCount = 0,
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            AssertEquivalentScalarFields(data, back);
        }

        // =================================================================
        // Expression round-trips (phase 2)
        // =================================================================

        [Fact]
        public void Expression_I32Const_RoundTrip()
        {
            var expr = MakeExpression(
                InstI32Const.Inst.Immediate(42),
                new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(2, back.Instructions.Count);
            Assert.Equal(42, ((InstI32Const)back.Instructions[0]!).Value);
            Assert.IsType<InstEnd>(back.Instructions[1]!);
        }

        [Fact]
        public void Expression_I64Const_RoundTrip()
        {
            var i64 = new InstI64Const();
            // InstI64Const.Value is internal — round-trip via its Parse API
            // by synthesizing a tiny LEB buffer. Safer than reflecting.
            using (var ms = new MemoryStream())
            {
                WriteLeb64(ms, 0x1234_5678_9ABC_DEF0L);
                ms.Position = 0;
                using var br = new BinaryReader(ms);
                i64.Parse(br);
            }

            var expr = MakeExpression(i64, new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(0x1234_5678_9ABC_DEF0L, ((InstI64Const)back.Instructions[0]!).FetchImmediate(null!));
        }

        [Fact]
        public void Expression_F32Const_RoundTrip()
        {
            var f32 = new InstF32Const();
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(3.14159f);
                ms.Position = 0;
                using var br = new BinaryReader(ms);
                f32.Parse(br);
            }

            var expr = MakeExpression(f32, new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(3.14159f, ((InstF32Const)back.Instructions[0]!).FetchImmediate(null!));
        }

        [Fact]
        public void Expression_F64Const_RoundTrip()
        {
            var f64 = new InstF64Const();
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(2.718281828459045);
                ms.Position = 0;
                using var br = new BinaryReader(ms);
                f64.Parse(br);
            }

            var expr = MakeExpression(f64, new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(2.718281828459045, ((InstF64Const)back.Instructions[0]!).FetchImmediate(null!));
        }

        [Fact]
        public void Expression_GlobalGet_RoundTrip()
        {
            // InstGlobalGet.Index setter is internal; round-trip via Parse.
            var gg = new InstGlobalGet();
            using (var ms = new MemoryStream())
            {
                WriteLeb32Unsigned(ms, 7);
                ms.Position = 0;
                using var br = new BinaryReader(ms);
                gg.Parse(br);
            }

            var expr = MakeExpression(gg, new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(7, ((InstGlobalGet)back.Instructions[0]!).GetIndex());
        }

        [Fact]
        public void Expression_RefNull_RoundTrip()
        {
            var rn = (InstRefNull)new InstRefNull().Immediate(ValType.FuncRef | ValType.NullableRef);
            var expr = MakeExpression(rn, new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(ValType.FuncRef | ValType.NullableRef, ((InstRefNull)back.Instructions[0]!).RefType);
        }

        [Fact]
        public void Expression_RefFunc_RoundTrip()
        {
            var rf = new InstRefFunc();
            using (var ms = new MemoryStream())
            {
                WriteLeb32Unsigned(ms, 42);
                ms.Position = 0;
                using var br = new BinaryReader(ms);
                rf.Parse(br);
            }

            var expr = MakeExpression(rf, new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(42u, ((InstRefFunc)back.Instructions[0]!).FunctionIndex.Value);
        }

        [Fact]
        public void Expression_ConstArithmetic_RoundTrip()
        {
            // i32.const 10; i32.const 32; i32.add; end
            // i32.add in constant exprs is WASM 3.0.
            var expr = MakeExpression(
                InstI32Const.Inst.Immediate(10),
                InstI32Const.Inst.Immediate(32),
                InstI32BinOp.I32Add,
                new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(4, back.Instructions.Count);
            Assert.Same(InstI32BinOp.I32Add, back.Instructions[2]);
        }

        [Fact]
        public void Expression_AllBinOps_RoundTrip()
        {
            var ops = new InstructionBase[]
            {
                InstI32BinOp.I32Add, InstI32BinOp.I32Sub, InstI32BinOp.I32Mul,
                InstI64BinOp.I64Add, InstI64BinOp.I64Sub, InstI64BinOp.I64Mul,
                new InstEnd(),
            };
            var expr = MakeExpression(ops);
            var back = RoundTripExpression(expr);
            // Static instances: reference equality should hold after round-trip.
            for (int i = 0; i < 6; i++)
                Assert.Same(ops[i], back.Instructions[i]);
            Assert.IsType<InstEnd>(back.Instructions[6]!);
        }

        [Fact]
        public void Expression_EndOnly_RoundTrip()
        {
            var expr = MakeExpression(new InstEnd());
            var back = RoundTripExpression(expr);
            Assert.Equal(1, back.Instructions.Count);
            Assert.IsType<InstEnd>(back.Instructions[0]!);
        }

        [Fact]
        public void DeferredGlobalInits_ExpressionSection_RoundTrip()
        {
            var data = new ModuleInitData
            {
                DeferredGlobalInits = new List<(int, WasmExpression)>
                {
                    (3, MakeExpression(InstI32Const.Inst.Immediate(100), new InstEnd())),
                    (7, MakeExpression(
                        InstI32Const.Inst.Immediate(5),
                        InstI32Const.Inst.Immediate(2),
                        InstI32BinOp.I32Mul,
                        new InstEnd())),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(2, back.DeferredGlobalInits.Count);
            Assert.Equal(3, back.DeferredGlobalInits[0].globalIdx);
            Assert.Equal(100, ((InstI32Const)back.DeferredGlobalInits[0].initializer.Instructions[0]!).Value);
            Assert.Equal(7, back.DeferredGlobalInits[1].globalIdx);
            Assert.Same(InstI32BinOp.I32Mul, back.DeferredGlobalInits[1].initializer.Instructions[2]);
        }

        [Fact]
        public void DeferredDataOffsets_ExpressionSection_RoundTrip()
        {
            var data = new ModuleInitData
            {
                DeferredDataOffsets = new List<(int, WasmExpression)>
                {
                    (0, MakeExpression(
                        MakeGlobalGet(5),
                        InstI32Const.Inst.Immediate(16),
                        InstI32BinOp.I32Add,
                        new InstEnd())),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Single(back.DeferredDataOffsets);
            Assert.Equal(0, back.DeferredDataOffsets[0].dataSegIdx);
            Assert.Equal(4, back.DeferredDataOffsets[0].offsetExpr.Instructions.Count);
            Assert.Equal(5, ((InstGlobalGet)back.DeferredDataOffsets[0].offsetExpr.Instructions[0]!).GetIndex());
        }

        [Fact]
        public void Tables_WithInitExpr_RoundTrip()
        {
            var initExpr = MakeExpression(MakeRefNull(ValType.FuncRef | ValType.NullableRef), new InstEnd());
            var data = new ModuleInitData
            {
                Tables = new (long, long, ValType, WasmExpression?)[]
                {
                    (10, 100, ValType.FuncRef | ValType.NullableRef, initExpr),
                    (5, uint.MaxValue, ValType.ExternRef | ValType.NullableRef, null),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(2, back.Tables.Length);
            Assert.Equal(10L, back.Tables[0].min);
            Assert.NotNull(back.Tables[0].initExpr);
            Assert.Equal(2, back.Tables[0].initExpr!.Instructions.Count);
            Assert.Null(back.Tables[1].initExpr);
        }

        // =================================================================
        // Value round-trips (phase 3): ref types, V128, i31
        // =================================================================

        [Fact]
        public void Value_Null_Funcref_RoundTrip()
        {
            var data = new ModuleInitData
            {
                Globals = new (ValType, Mutability, Value)[]
                {
                    (ValType.FuncRef | ValType.NullableRef, Mutability.Immutable,
                     Value.Null(ValType.FuncRef | ValType.NullableRef)),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Single(back.Globals);
            Assert.True(back.Globals[0].init.IsNullRef);
        }

        [Fact]
        public void Value_Null_Externref_RoundTrip()
        {
            var data = new ModuleInitData
            {
                Globals = new (ValType, Mutability, Value)[]
                {
                    (ValType.ExternRef | ValType.NullableRef, Mutability.Immutable,
                     Value.Null(ValType.ExternRef | ValType.NullableRef)),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.True(back.Globals[0].init.IsNullRef);
        }

        [Fact]
        public void Value_Funcref_WithIdx_RoundTrip()
        {
            Value funcref = new Value(ValType.FuncRef, 42L, null);
            var data = new ModuleInitData
            {
                Globals = new (ValType, Mutability, Value)[]
                {
                    (ValType.FuncRef, Mutability.Immutable, funcref),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(ValType.FuncRef, back.Globals[0].init.Type);
            Assert.Equal(42L, back.Globals[0].init.Data.Ptr);
            Assert.False(back.Globals[0].init.IsNullRef);
        }

        [Fact]
        public void Value_I31_RoundTrip()
        {
            Value i31 = new Value(ValType.I31, 12345, new Wacs.Core.Runtime.GC.I31Ref(12345));
            var data = new ModuleInitData
            {
                Globals = new (ValType, Mutability, Value)[]
                {
                    (ValType.I31, Mutability.Immutable, i31),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(ValType.I31, back.Globals[0].init.Type);
            Assert.Equal(12345L, back.Globals[0].init.Data.Ptr);
            Assert.IsType<Wacs.Core.Runtime.GC.I31Ref>(back.Globals[0].init.GcRef);
            Assert.Equal(12345, ((Wacs.Core.Runtime.GC.I31Ref)back.Globals[0].init.GcRef!).Value);
        }

        [Fact]
        public void Value_V128_RoundTrip()
        {
            var v128 = new V128(0x1234_5678_9ABC_DEF0UL, 0xCAFE_BABE_FEED_FACEUL);
            Value val = default;
            val.Type = ValType.V128;
            val.GcRef = new VecRef(v128);

            var data = new ModuleInitData
            {
                Globals = new (ValType, Mutability, Value)[]
                {
                    (ValType.V128, Mutability.Immutable, val),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(ValType.V128, back.Globals[0].init.Type);
            Assert.IsType<VecRef>(back.Globals[0].init.GcRef);
            var roundTripped = ((VecRef)back.Globals[0].init.GcRef!).V128;
            Assert.Equal(0x1234_5678_9ABC_DEF0UL, roundTripped.U64x2_0);
            Assert.Equal(0xCAFE_BABE_FEED_FACEUL, roundTripped.U64x2_1);
        }

        [Fact]
        public void GcElementValues_WithI31_RoundTrip()
        {
            var data = new ModuleInitData
            {
                GcElementValues = new Dictionary<(int, int), Value>
                {
                    [(0, 0)] = new Value(ValType.I31, 7, new Wacs.Core.Runtime.GC.I31Ref(7)),
                    [(0, 1)] = new Value(ValType.I31, 42, new Wacs.Core.Runtime.GC.I31Ref(42)),
                    [(1, 3)] = Value.Null(ValType.FuncRef | ValType.NullableRef),
                },
            };
            var back = InitDataCodec.Decode(InitDataCodec.Encode(data));
            Assert.Equal(3, back.GcElementValues.Count);
            Assert.Equal(7, ((Wacs.Core.Runtime.GC.I31Ref)back.GcElementValues[(0, 0)].GcRef!).Value);
            Assert.Equal(42, ((Wacs.Core.Runtime.GC.I31Ref)back.GcElementValues[(0, 1)].GcRef!).Value);
            Assert.True(back.GcElementValues[(1, 3)].IsNullRef);
        }

        // =================================================================
        // Helpers
        // =================================================================

        private static WasmExpression MakeExpression(params InstructionBase[] insts)
        {
            return new WasmExpression(arity: 1, new InstructionSequence(insts), isStatic: true);
        }

        private static WasmExpression RoundTripExpression(WasmExpression expr)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                InitDataCodec.WriteExpression(w, expr);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            return InitDataCodec.ReadExpression(r);
        }

        private static InstGlobalGet MakeGlobalGet(uint idx)
        {
            var inst = new InstGlobalGet();
            using var ms = new MemoryStream();
            WriteLeb32Unsigned(ms, idx);
            ms.Position = 0;
            using var br = new BinaryReader(ms);
            inst.Parse(br);
            return inst;
        }

        private static InstRefNull MakeRefNull(ValType t)
            => (InstRefNull)new InstRefNull().Immediate(t);

        private static void WriteLeb32Unsigned(Stream s, uint v)
        {
            while (v >= 0x80) { s.WriteByte((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            s.WriteByte((byte)v);
        }

        private static void WriteLeb64(Stream s, long v)
        {
            bool more = true;
            while (more)
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                bool sign = (b & 0x40) != 0;
                more = !((v == 0 && !sign) || (v == -1 && sign));
                if (more) b |= 0x80;
                s.WriteByte(b);
            }
        }

        private static void AssertEquivalentScalarFields(ModuleInitData expected, ModuleInitData actual)
        {
            Assert.Equal(expected.Memories.Length, actual.Memories.Length);
            Assert.Equal(expected.Globals.Length, actual.Globals.Length);
            Assert.Equal(expected.FuncTypeHashes, actual.FuncTypeHashes);
            Assert.Equal(expected.TypeHashes, actual.TypeHashes);
            Assert.Equal(expected.TypeIsFunc, actual.TypeIsFunc);
            Assert.Equal(expected.ActiveDataSegments, actual.ActiveDataSegments);
            Assert.Equal(expected.ActiveElemIndices, actual.ActiveElemIndices);
            Assert.Equal(expected.ActiveDataIndices, actual.ActiveDataIndices);
            Assert.Equal(expected.StartFuncIndex, actual.StartFuncIndex);
            Assert.Equal(expected.DataSegmentBaseId, actual.DataSegmentBaseId);
            Assert.Equal(expected.ElemSegmentBaseId, actual.ElemSegmentBaseId);
            Assert.Equal(expected.ImportFuncCount, actual.ImportFuncCount);
            Assert.Equal(expected.TotalFuncCount, actual.TotalFuncCount);
            Assert.Equal(expected.ImportedTagCount, actual.ImportedTagCount);
        }
    }
}

// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.ComponentModel.CanonicalABI;
using Wacs.ComponentModel.Runtime.Parser;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 3 Slice K1 coverage: <see cref="FlatLowering"/>
    /// canon-ABI flat-count analyzer. Verifies the per-shape
    /// counts match the rules in
    /// <c>component-model/design/mvp/CanonicalABI.md</c>.
    /// </summary>
    public class FlatLoweringTests
    {
        private static ComponentValType Prim(ComponentPrim p) =>
            ComponentValType.OfPrim(p);

        private static ComponentValType Ref(uint idx) =>
            ComponentValType.OfRef(idx);

        // ---- Primitives ------------------------------------------------

        [Fact]
        public void Primitive_string_is_two_slots()
        {
            Assert.Equal(2,
                FlatLowering.FlatCount(Prim(ComponentPrim.String), null));
        }

        [Theory]
        [InlineData(ComponentPrim.Bool)]
        [InlineData(ComponentPrim.S8)]
        [InlineData(ComponentPrim.U8)]
        [InlineData(ComponentPrim.S16)]
        [InlineData(ComponentPrim.U16)]
        [InlineData(ComponentPrim.S32)]
        [InlineData(ComponentPrim.U32)]
        [InlineData(ComponentPrim.S64)]
        [InlineData(ComponentPrim.U64)]
        [InlineData(ComponentPrim.F32)]
        [InlineData(ComponentPrim.F64)]
        [InlineData(ComponentPrim.Char)]
        public void Primitive_non_string_is_one_slot(ComponentPrim p)
        {
            Assert.Equal(1, FlatLowering.FlatCount(Prim(p), null));
        }

        // ---- Aggregates ------------------------------------------------

        [Fact]
        public void List_is_two_slots_independent_of_element()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentListType(Prim(ComponentPrim.U64)),
            };
            Assert.Equal(2, FlatLowering.FlatCount(Ref(0), types));
        }

        [Fact]
        public void Handle_types_are_one_slot()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentOwnType(99),
                new ComponentBorrowType(99),
                new ComponentResourceType(null),
                new ComponentStreamType(Prim(ComponentPrim.U8)),
                new ComponentFutureType(Prim(ComponentPrim.U32)),
                ComponentErrorContextType.Instance,
            };
            for (uint i = 0; i < types.Count; i++)
                Assert.Equal(1, FlatLowering.FlatCount(Ref(i), types));
        }

        [Fact]
        public void Enum_is_one_slot_disc_only()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentEnumType(new[] { "a", "b", "c" }),
            };
            Assert.Equal(1, FlatLowering.FlatCount(Ref(0), types));
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(8, 1)]
        [InlineData(32, 1)]
        [InlineData(33, 2)]
        [InlineData(64, 2)]
        [InlineData(65, 3)]
        public void Flags_count_is_ceil_n_over_32(int n, int expected)
        {
            var names = new List<string>();
            for (int i = 0; i < n; i++) names.Add($"f{i}");
            var types = new List<DefTypeEntry>
            {
                new ComponentFlagsType(names),
            };
            Assert.Equal(expected, FlatLowering.FlatCount(Ref(0), types));
        }

        [Fact]
        public void Record_is_sum_of_fields()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentRecordType(new[]
                {
                    new ComponentRecordType.Field("a", Prim(ComponentPrim.U32)),
                    new ComponentRecordType.Field("s", Prim(ComponentPrim.String)),
                    new ComponentRecordType.Field("c", Prim(ComponentPrim.U64)),
                }),
            };
            // u32 (1) + string (2) + u64 (1) = 4
            Assert.Equal(4, FlatLowering.FlatCount(Ref(0), types));
        }

        [Fact]
        public void Tuple_is_sum_of_elements()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentTupleType(new[]
                {
                    Prim(ComponentPrim.U32),
                    Prim(ComponentPrim.U32),
                    Prim(ComponentPrim.F64),
                }),
            };
            Assert.Equal(3, FlatLowering.FlatCount(Ref(0), types));
        }

        [Fact]
        public void Variant_is_one_plus_max_case_payload()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentVariantType(new[]
                {
                    new ComponentVariantType.Case("none", null, null),
                    new ComponentVariantType.Case(
                        "small", Prim(ComponentPrim.U32), null),
                    new ComponentVariantType.Case(
                        "big", Prim(ComponentPrim.String), null),
                }),
            };
            // 1 disc + max(0, 1, 2) = 3
            Assert.Equal(3, FlatLowering.FlatCount(Ref(0), types));
        }

        [Fact]
        public void Variant_all_unit_cases_is_one_slot()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentVariantType(new[]
                {
                    new ComponentVariantType.Case("a", null, null),
                    new ComponentVariantType.Case("b", null, null),
                }),
            };
            Assert.Equal(1, FlatLowering.FlatCount(Ref(0), types));
        }

        [Fact]
        public void Option_is_one_plus_inner()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentOptionType(Prim(ComponentPrim.String)),
                new ComponentOptionType(Prim(ComponentPrim.U32)),
            };
            Assert.Equal(3, FlatLowering.FlatCount(Ref(0), types)); // 1 + 2
            Assert.Equal(2, FlatLowering.FlatCount(Ref(1), types)); // 1 + 1
        }

        [Fact]
        public void Result_is_one_plus_max_ok_err()
        {
            var types = new List<DefTypeEntry>
            {
                // result<string, u32>: 1 + max(2, 1) = 3
                new ComponentResultType(
                    Prim(ComponentPrim.String),
                    Prim(ComponentPrim.U32)),
                // result<_, u32>: 1 + max(0, 1) = 2
                new ComponentResultType(null, Prim(ComponentPrim.U32)),
                // result: 1 + max(0, 0) = 1
                new ComponentResultType(null, null),
            };
            Assert.Equal(3, FlatLowering.FlatCount(Ref(0), types));
            Assert.Equal(2, FlatLowering.FlatCount(Ref(1), types));
            Assert.Equal(1, FlatLowering.FlatCount(Ref(2), types));
        }

        // ---- Nested aggregates -----------------------------------------

        [Fact]
        public void Nested_record_resolves_through_typeidx()
        {
            // type 0: record { a: u32 }  -- count 1
            // type 1: record { inner: typeidx(0), s: string }  -- 1 + 2 = 3
            var types = new List<DefTypeEntry>
            {
                new ComponentRecordType(new[]
                {
                    new ComponentRecordType.Field("a", Prim(ComponentPrim.U32)),
                }),
                new ComponentRecordType(new[]
                {
                    new ComponentRecordType.Field("inner", Ref(0)),
                    new ComponentRecordType.Field("s", Prim(ComponentPrim.String)),
                }),
            };
            Assert.Equal(1, FlatLowering.FlatCount(Ref(0), types));
            Assert.Equal(3, FlatLowering.FlatCount(Ref(1), types));
        }

        // ---- Sentinels -------------------------------------------------

        [Fact]
        public void Returns_negative_one_when_types_missing()
        {
            // Ref(0) with null types: can't resolve.
            Assert.Equal(-1, FlatLowering.FlatCount(Ref(0), null));
        }

        [Fact]
        public void Returns_negative_one_when_typeidx_out_of_range()
        {
            var types = new List<DefTypeEntry>();
            Assert.Equal(-1, FlatLowering.FlatCount(Ref(5), types));
        }

        [Fact]
        public void Returns_negative_one_for_raw_def_type()
        {
            var types = new List<DefTypeEntry>
            {
                new RawDefType(0x55, System.Array.Empty<byte>()),
            };
            Assert.Equal(-1, FlatLowering.FlatCount(Ref(0), types));
        }

        [Fact]
        public void Returns_negative_one_for_funcs()
        {
            var types = new List<DefTypeEntry>
            {
                new ComponentFuncType(
                    System.Array.Empty<ComponentFuncType.Param>(),
                    System.Array.Empty<ComponentValType>()),
            };
            Assert.Equal(-1, FlatLowering.FlatCount(Ref(0), types));
        }
    }
}

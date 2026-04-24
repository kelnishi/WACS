// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.CanonicalABI;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Contract tests for the discriminant-byte helpers used by
    /// option / result adapters. These helpers are deliberately
    /// small — most of the marshaling complexity lives in the
    /// caller's payload dispatch, which uses the appropriate
    /// helper (string / list / prim) for the inner type.
    /// </summary>
    public class DiscriminantMarshalTests
    {
        [Fact]
        public void OptionMarshal_roundtrips_None()
        {
            var buf = new byte[4];
            OptionMarshal.WriteNoneTag(buf, 0);
            Assert.Equal(OptionMarshal.DiscriminantNone, buf[0]);
            Assert.False(OptionMarshal.IsSome(buf, 0));
        }

        [Fact]
        public void OptionMarshal_roundtrips_Some()
        {
            var buf = new byte[4];
            OptionMarshal.WriteSomeTag(buf, 0);
            Assert.Equal(OptionMarshal.DiscriminantSome, buf[0]);
            Assert.True(OptionMarshal.IsSome(buf, 0));
        }

        [Fact]
        public void OptionMarshal_rejects_invalid_discriminant()
        {
            var buf = new byte[] { 0x02 };
            Assert.Throws<FormatException>(() => OptionMarshal.IsSome(buf, 0));
        }

        [Fact]
        public void ResultMarshal_roundtrips_Ok()
        {
            var buf = new byte[4];
            ResultMarshal.WriteOkTag(buf, 0);
            Assert.Equal(ResultMarshal.DiscriminantOk, buf[0]);
            Assert.True(ResultMarshal.IsOk(buf, 0));
        }

        [Fact]
        public void ResultMarshal_roundtrips_Err()
        {
            var buf = new byte[4];
            ResultMarshal.WriteErrTag(buf, 0);
            Assert.Equal(ResultMarshal.DiscriminantErr, buf[0]);
            Assert.False(ResultMarshal.IsOk(buf, 0));
        }

        [Fact]
        public void ResultMarshal_rejects_invalid_discriminant()
        {
            var buf = new byte[] { 0xFF };
            Assert.Throws<FormatException>(() => ResultMarshal.IsOk(buf, 0));
        }

        [Fact]
        public void Discriminants_write_at_arbitrary_offset()
        {
            // Real return-area layouts put the discriminant at
            // offset 0 and payload at 4 or 8 depending on
            // alignment. Verify the helpers respect the offset
            // argument.
            var buf = new byte[16];
            ResultMarshal.WriteErrTag(buf, 3);
            Assert.Equal(0x00, buf[0]);
            Assert.Equal(0x00, buf[2]);
            Assert.Equal(ResultMarshal.DiscriminantErr, buf[3]);
        }
    }
}

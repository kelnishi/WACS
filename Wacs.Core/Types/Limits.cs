// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.7. Limits
    /// Represents the limits of a resizable storage (memory or table) in WebAssembly.
    /// </summary>
    public class Limits : ICloneable
    {
        /// <summary>
        /// The optional maximum number of units. If MaxValue, there is no specified maximum.
        /// </summary>
        public long? Maximum;

        /// <summary>
        /// The minimum number of units (e.g., pages for memory).
        /// </summary>
        public long Minimum;

        /// <summary>
        /// The address type of the memory.
        /// </summary>
        public AddrType AddressType;

        /// <summary>
        /// For threads, indicates whether the memory is shared.
        /// </summary>
        public bool Shared;

        /// <summary>
        /// Initializes a new instance of the <see cref="Limits"/> class with the specified minimum and optional maximum.
        /// </summary>
        /// <param name="type">i32|i64 pointer type</param>
        /// <param name="minimum">The minimum number of units.</param>
        /// <param name="maximum">The optional maximum number of units.</param>
        public Limits(AddrType type, long minimum, long? maximum = null, bool shared = false) {
            AddressType = type;
            Minimum = minimum;
            Maximum = maximum;
            Shared = shared;
        }

        public Limits(Limits copy) {
            AddressType = copy.AddressType;
            Minimum = copy.Minimum;
            Maximum = copy.Maximum;
            Shared = copy.Shared;
        }

        public object Clone() => new Limits(this);

        /// <summary>
        /// @Spec 5.3.7. Limits
        /// </summary>
        public static Limits Parse(BinaryReader reader) => 
            (LimitsFlag)reader.ReadByte() switch {
                LimitsFlag.Mem32Min => new Limits(AddrType.I32,reader.ReadLeb128_u32()),
                LimitsFlag.Mem32MinMax => new Limits(AddrType.I32,reader.ReadLeb128_u32(), reader.ReadLeb128_u32()),
                LimitsFlag.Mem32MinMaxShared => new Limits(AddrType.I32,reader.ReadLeb128_u32(), reader.ReadLeb128_u32(), true),
                //Clamp to 64-bit signed integer, we're not going to ever allow more than 2^63-1 pages.
                LimitsFlag.Mem64Min => new Limits(AddrType.I64, (long)reader.ReadLeb128_u64()),
                LimitsFlag.Mem64MinMax => new Limits(AddrType.I64,(long)reader.ReadLeb128_u64(), (long)reader.ReadLeb128_u64()),
                LimitsFlag.Mem64MinMaxShared => new Limits(AddrType.I64,(long)reader.ReadLeb128_u64(), (long)reader.ReadLeb128_u64(), true),
                var flag => throw new FormatException($"Invalid Limits flag {flag} at offset {reader.BaseStream.Position}.")
            };

        public string ToWat() => Maximum != null ? $"{AddressType} {Minimum} {Maximum}" : $"{AddressType} {Minimum}";
        public override string ToString() => Maximum != null ? $"Limits: [{AddressType} {Minimum}, {Maximum}]" : $"Limits: [{AddressType} {Minimum},?]";

        /// <summary>
        /// @Spec 3.2.1. Limits
        /// </summary>
        public class Validator : AbstractValidator<Limits>
        {
            public Validator(uint rangeK) {
                RuleFor(limits => limits.Minimum)
                    .LessThan(rangeK);
                When(l => l.Maximum.HasValue, () => {
                    RuleFor(l => l.Maximum)
                        .LessThanOrEqualTo(rangeK);
                    RuleFor(l => l.Maximum)
                        .GreaterThanOrEqualTo(limits => limits.Minimum);
                });
                
            }
        }
    }
}
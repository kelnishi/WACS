// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

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
        /// Initializes a new instance of the <see cref="Limits"/> class with the specified minimum and optional maximum.
        /// </summary>
        /// <param name="minimum">The minimum number of units.</param>
        /// <param name="maximum">The optional maximum number of units.</param>
        public Limits(long minimum, long? maximum = null) {
            Minimum = minimum;
            Maximum = maximum;
        }

        public Limits(Limits copy) {
            Minimum = copy.Minimum;
            Maximum = copy.Maximum;
        }

        public object Clone() => new Limits(this);

        /// <summary>
        /// @Spec 5.3.7. Limits
        /// </summary>
        public static Limits Parse(BinaryReader reader) => 
            (LimitsFlag)reader.ReadByte() switch {
                LimitsFlag.Mem32Min => new Limits(reader.ReadLeb128_u32()),
                LimitsFlag.Mem32MinMax => new Limits(reader.ReadLeb128_u32(), reader.ReadLeb128_u32()),
                //Clamp to 64-bit signed integer, we're not going to ever allow more than 2^63-1 pages.
                LimitsFlag.Mem64Min => new Limits((long)reader.ReadLeb128_u64()),
                LimitsFlag.Mem64MinMax => new Limits((long)reader.ReadLeb128_u64(), (long)reader.ReadLeb128_u64()),
                var flag => throw new FormatException($"Invalid Limits flag {flag} at offset {reader.BaseStream.Position}.")
            };

        public string ToWat() => Maximum != null ? $"{Minimum} {Maximum}" : $"{Minimum}";
        public override string ToString() => Maximum != null ? $"Limits: [{Minimum}, {Maximum}]" : $"Limits: [{Minimum},?]";

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
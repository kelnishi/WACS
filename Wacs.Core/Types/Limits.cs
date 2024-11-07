using System;
using System.IO;
using FluentValidation;
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
        /// Initializes a new instance of the <see cref="Limits"/> class with the specified minimum and optional maximum.
        /// </summary>
        /// <param name="minimum">The minimum number of units.</param>
        /// <param name="maximum">The optional maximum number of units.</param>
        public Limits(uint minimum, uint? maximum = null) {
            Minimum = minimum;
            Maximum = maximum;
        }

        public Limits(Limits copy) {
            Minimum = copy.Minimum;
            Maximum = copy.Maximum;
        }

        /// <summary>
        /// The minimum number of units (e.g., pages for memory).
        /// </summary>
        public uint Minimum { get; set; }

        /// <summary>
        /// The optional maximum number of units. If MaxValue, there is no specified maximum.
        /// </summary>
        public uint? Maximum { get; set; }

        public object Clone() => new Limits(this);

        /// <summary>
        /// @Spec 5.3.7. Limits
        /// </summary>
        public static Limits Parse(BinaryReader reader) => 
            reader.ReadByte() switch {
                0x00 => new Limits(reader.ReadLeb128_u32()),
                0x01 => new Limits(reader.ReadLeb128_u32(), reader.ReadLeb128_u32()),
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
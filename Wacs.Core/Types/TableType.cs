using System;
using System.IO;
using FluentValidation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 5.3.9 Table Types
    /// Represents the table type in WebAssembly, defining the element type and its limits.
    /// </summary>
    public class TableType
    {
        /// <summary>
        /// The limits specifying the minimum and optional maximum number of elements.
        /// </summary>
        public Limits Limits { get; set; }
        
        /// <summary>
        /// The element type of the table (e.g., funcref or externref).
        /// </summary>
        public ReferenceType ElementType { get; set; }

        private TableType(BinaryReader reader) =>
            (ElementType, Limits) = (ReferenceTypeParser.Parse(reader), Limits.Parse(reader));
        
        public static TableType Parse(BinaryReader reader) => new TableType(reader);

        /// <summary>
        /// @Spec 3.2.4. Table Types
        /// </summary>
        public class Validator : AbstractValidator<TableType>
        {
            private const uint MaxTableSize = 0xFF_FF_FF_FF; //2^32 - 1
            public Validator() {
                // @Spec 3.2.4.1. limits reftype
                RuleFor(tt => tt.Limits).SetValidator(new Limits.Validator(MaxTableSize));
                RuleFor(tt => tt.ElementType).IsInEnum();
            }
        }
    }
}
using System;
using System.IO;
using FluentValidation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.9 Table Types
    /// Represents the table type in WebAssembly, defining the element type and its limits.
    /// </summary>
    public class TableType : ICloneable
    {
        public const uint MaxTableSize = 0xFFFF_FFFF; //2^32 - 1

        /// <summary>
        /// The limits specifying the minimum and optional maximum number of elements.
        /// </summary>
        public Limits Limits { get; set; } = null!;
        
        /// <summary>
        /// The element type of the table (e.g., funcref or externref).
        /// </summary>
        public ReferenceType ElementType { get; set; }



        private TableType() { }

        private TableType(BinaryReader reader) =>
            (ElementType, Limits) = (ReferenceTypeParser.Parse(reader), Limits.Parse(reader));
        
        /// <summary>
        /// @Spec 5.3.9. Table Types
        /// </summary>
        public static TableType Parse(BinaryReader reader) => new(reader);

        /// <summary>
        /// @Spec 3.2.4. Table Types
        /// </summary>
        public class Validator : AbstractValidator<TableType>
        {
            public static Limits.Validator Limits = new(MaxTableSize);
            
            public Validator() {
                // @Spec 3.2.4.1. limits reftype
                RuleFor(tt => tt.Limits).SetValidator(Limits);
                RuleFor(tt => tt.ElementType).IsInEnum();
            }
        }
        
        public object Clone() {
            return new TableType {
                Limits = (Limits)Limits.Clone(), // Assuming Limits implements ICloneable
                ElementType = ElementType // Assuming ElementType is a value type or has a suitable copy method
            };
        }
    }
}
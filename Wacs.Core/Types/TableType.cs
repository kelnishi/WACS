using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Attributes;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.9 Table Types
    /// Represents the table type in WebAssembly, defining the element type and its limits.
    /// </summary>
    public class TableType : ICloneable, IRenderable
    {
        private TableType()
        {
        }

        private TableType(BinaryReader reader) =>
            (ElementType, Limits) = (ReferenceTypeParser.Parse(reader), Limits.Parse(reader));

        /// <summary>
        /// The limits specifying the minimum and optional maximum number of elements.
        /// </summary>
        public Limits Limits { get; set; } = null!;

        /// <summary>
        /// The element type of the table (e.g., funcref or externref).
        /// </summary>
        public ReferenceType ElementType { get; private set; }

        public string Id { get; set; } = "";

        public object Clone()
        {
            return new TableType
            {
                Limits = (Limits)Limits.Clone(), // Assuming Limits implements ICloneable
                ElementType = ElementType // Assuming ElementType is a value type or has a suitable copy method
            };
        }

        public void RenderText(StreamWriter writer, Module module, string indent)
        {
            var id = string.IsNullOrWhiteSpace(Id) ? "" : $" (;{Id};)";
            var tableType = $"{Limits.ToWat()} {ElementType.ToWat()}";
            var tableText = $"{indent}(table{id} {tableType})";
            
            writer.WriteLine(tableText);
        }

        /// <summary>
        /// @Spec 5.3.9. Table Types
        /// </summary>
        public static TableType Parse(BinaryReader reader) => new(reader);

        /// <summary>
        /// @Spec 3.2.4. Table Types
        /// </summary>
        public class Validator : AbstractValidator<TableType>
        {
            public static readonly Limits.Validator Limits = new(Constants.MaxTableSize);

            public Validator()
            {
                // @Spec 3.2.4.1. limits reftype
                RuleFor(tt => tt.Limits).SetValidator(Limits);
                RuleFor(tt => tt.ElementType).IsInEnum();
            }
        }
    }
}
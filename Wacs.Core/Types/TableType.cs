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
        /// <summary>
        /// The limits specifying the minimum and optional maximum number of elements.
        /// </summary>
        public Limits Limits = null!;

        private TableType()
        {
        }

        public TableType(ReferenceType elementType, Limits limits) =>
            (ElementType, Limits) = (elementType, limits);

        private TableType(BinaryReader reader) =>
            (ElementType, Limits) = (ReferenceTypeParser.Parse(reader), Limits.Parse(reader));

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

        public override string ToString() => $"TableType({ElementType}[{Limits}])";

        /// <summary>
        /// @Spec 5.3.9. Table Types
        /// </summary>
        public static TableType Parse(BinaryReader reader) => new(reader);

        /// <summary>
        /// Tables imported from host or other modules must fit within the import definition.
        /// </summary>
        /// <param name="imported">The table exported from host or other module.</param>
        /// <returns>True when the import fits inside the definition</returns>
        public bool IsCompatibleWith(TableType imported)
        {
            if (ElementType != imported.ElementType)
                return false;
            if (imported.Limits.Minimum < Limits.Minimum)
                return false;
            if (!Limits.Maximum.HasValue)
                return true;
            if (!imported.Limits.Maximum.HasValue)
                return false;
            if (imported.Limits.Maximum > Limits.Maximum)
                return false;
            return true;
        }

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
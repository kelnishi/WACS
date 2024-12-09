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
using Wacs.Core.Instructions.Reference;
using Wacs.Core.OpCodes;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.9 Table Types
    /// Represents the table type in WebAssembly, defining the element type and its limits.
    /// </summary>
    public class TableType : ICloneable, IRenderable
    {
        /// <summary>
        /// The element type of the table (e.g., funcref or externref).
        /// </summary>
        public readonly ValType ElementType;

        /// <summary>
        /// The limits specifying the minimum and optional maximum number of elements.
        /// </summary>
        public Limits Limits = null!;

        public readonly Expression Init;

        public TableType(ValType elementType, Limits limits, Expression? init = null)
        {
            ElementType = elementType;
            Limits = limits;
            
            if (init == null)
            { 
                var inst = BinaryModuleParser.InstructionFactory
                    .CreateInstruction<InstRefNull>(OpCode.RefNull).Immediate(elementType);
                init = new Expression(1, inst);
            }
            
            Init = init;
        }

        public string Id { get; set; } = "";

        public object Clone()
        {
            return new TableType(ElementType, (Limits)Limits.Clone(), Init);
        }

        public void RenderText(StreamWriter writer, Module module, string indent)
        {
            var id = string.IsNullOrWhiteSpace(Id) ? "" : $" (;{Id};)";
            var tableType = $"{Limits.ToWat()} {ElementType.ToWat()}";
            var tableText = $"{indent}(table{id} {tableType})";
            
            writer.WriteLine(tableText);
        }

        public override string ToString() => $"TableType({ElementType}[{Limits}])";

        private const byte TableTypeExpr = 0x40;
        private const byte FutureExtByte = 0x00;

        private static TableType ParseTableTypeWithExpr(BinaryReader reader)
        {
            switch (reader.ReadByte())
            {
                case FutureExtByte: break;
                case var b: throw new FormatException($"Invalid format parsing TableType {b}");
            }

            var type = ValTypeParser.ParseRefType(reader);
            var limits = Limits.Parse(reader);
            var init = Expression.ParseInitializer(reader);
            return new(type, limits, init);
        }
        
        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#table-section①
        /// </summary>
        public static TableType Parse(BinaryReader reader)
        {
            long pos = reader.BaseStream.Position;
            var type = ValTypeParser.ParseDefType(reader);
            if (type == ValType.Empty)
                return ParseTableTypeWithExpr(reader);
            if (type.IsDefType())
                type |= ValType.Ref;
            if (!type.IsRefType())
                throw new FormatException($"Invalid non-ref TableType {type} at {pos}");
            if (!type.IsNullable())
                throw new FormatException($"Invalid non-nullable TableType {type} at {pos}");
            
            var limits = Limits.Parse(reader);
            return new(type, limits);
        }

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
                RuleFor(tt => tt.ElementType)
                    .Must((_, type, ctx) => ctx.GetValidationContext().ValidateType(type))
                    .WithMessage(tt => $"TableType had invalid ElementType {tt.ElementType}");
                RuleFor(tt => tt.Init)
                    .Custom((expr, ctx) =>
                    {
                        var validationContext = ctx.GetValidationContext();
                        var subContext = validationContext.PushSubContext(expr);

                        var funcType = FunctionType.Empty;
                        validationContext.FunctionIndex = FuncIdx.Default;
                        validationContext.SetExecFrame(funcType, Array.Empty<ValType>());
                            
                        var tt = ctx.InstanceToValidate;
                        var exprValidator = new Expression.Validator(new ResultType(tt.ElementType), isConstant: true);
                            
                        var result = exprValidator.Validate(subContext);
                        foreach (var error in result.Errors)
                        {
                            ctx.AddFailure($"TableType.Init.{error.PropertyName}", error.ErrorMessage);
                        }
                        validationContext.PopValidationContext();
                    });
            }
        }
    }
}
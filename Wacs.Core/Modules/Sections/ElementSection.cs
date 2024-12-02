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
using System.Linq;
using FluentValidation;
using Wacs.Core.Attributes;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.7. Element Segments
        /// </summary>
        public ElementSegment[] Elements { get; internal set; } = Array.Empty<ElementSegment>();

        /// <summary>
        /// @Spec 2.5.7. Element Segments
        /// </summary>
        public class ElementSegment : IRenderable
        {
            private ElementSegment(ReferenceType type, InstructionBase[] funcIndices, ElementMode mode)
            {
                Type = type;
                Initializers = funcIndices.Select(inst => new Expression(inst, 1)).ToArray();
                Mode = mode;
                Mode.SegmentType = Type;
            }

            private ElementSegment(ReferenceType type, Expression[] expressions, ElementMode mode)
            {
                Type = type;
                Initializers = expressions;
                Mode = mode;
                Mode.SegmentType = Type;
            }

            private ElementSegment(TableIdx tableIndex, Expression e, ReferenceType type, InstructionBase[] funcIndices)
            {
                Type = type;
                Initializers = funcIndices.Select(inst => new Expression(inst, 1)).ToArray();
                Mode = new ElementMode.ActiveMode(tableIndex, e);
                Mode.SegmentType = Type;
            }

            private ElementSegment(TableIdx tableIndex, Expression e, ReferenceType type, Expression[] expressions)
            {
                Type = type;
                Initializers = expressions;
                Mode = new ElementMode.ActiveMode(tableIndex, e);
                Mode.SegmentType = Type;
            }

            public ReferenceType Type { get; }

            //A vector of (ref.func x) instructions
            public Expression[] Initializers { get; }

            public ElementMode Mode { get; }

            public string Id { get; set; } = "";

            public void RenderText(StreamWriter writer, Module module, string indent)
            {
                var id = string.IsNullOrWhiteSpace(Id) ? "" : $" (;{Id};)";
                var modeText = Mode switch
                {
                    ElementMode.PassiveMode => "",
                    ElementMode.ActiveMode am when am.TableIndex is { Value: 0 } && am.Offset.Instructions.IsConstant(null) =>
                        $"{ am.Offset.ToWat() }",
                    ElementMode.ActiveMode am => $" (table {am.TableIndex.Value}) (offset{am.Offset.ToWat()})",
                    ElementMode.DeclarativeMode => $" declare",
                    _ => throw new InvalidDataException($"Unknown Element Mode: {Mode}")
                };
                string elemListText = "";
                
                if (Type == ReferenceType.Funcref && IsAllRefFunc())
                {
                    var listElems = Initializers
                        .Select(expr => expr.Instructions[0])
                        .OfType<InstRefFunc>()
                        .Select(rf => rf.FunctionIndex.Value);
                    var listText = string.Join(" ", listElems);
                    elemListText = $" func {listText}";
                }
                else
                {
                    var listElems = Initializers
                        .Select(expr => expr.ToWat())
                        .Select(item => $" (item {item})");
                    var listText = string.Join("", listElems);
                    elemListText = $" {Type.ToWat()}{listText}";
                }
                
                var elemText = $"{indent}(elem{id}{modeText}{elemListText})";
            
                writer.WriteLine(elemText);
            }

            /// <summary>
            /// Generate a InstRefFunc for a funcidx
            /// </summary>
            private static InstructionBase ParseFuncIdxInstructions(BinaryReader reader) =>
                BinaryModuleParser.InstructionFactory.CreateInstruction(OpCode.RefFunc).Parse(reader);

            private static ReferenceType ParseElementKind(BinaryReader reader) =>
                reader.ReadByte() switch {
                    0x00 => ReferenceType.Funcref,
                    var b =>
                        throw new FormatException($"Invalid ElementKind {b} at {reader.BaseStream.Position - 1:x}")
                };

            private static TableIdx ParseTableIndex(BinaryReader reader) =>
                (TableIdx)reader.ReadLeb128_u32();

            public static ElementSegment Parse(BinaryReader reader) =>
                (ElementType)reader.ReadLeb128_u32() switch {
                    ElementType.ActiveNoIndexWithElemKind => 
                        new ElementSegment(
                            (TableIdx)0,
                            Expression.ParseInitializer(reader),
                            ReferenceType.Funcref,
                            reader.ParseVector(ParseFuncIdxInstructions)),
                    ElementType.PassiveWithElemKind =>
                        new ElementSegment(
                            ParseElementKind(reader),
                            reader.ParseVector(ParseFuncIdxInstructions),
                            new ElementMode.PassiveMode()),
                    ElementType.ActiveWithIndexAndElemKind =>
                        new ElementSegment(
                            ParseTableIndex(reader),
                            Expression.ParseInitializer(reader),
                            ParseElementKind(reader),
                            reader.ParseVector(ParseFuncIdxInstructions)),
                    ElementType.DeclarativeWithElemKind =>
                        new ElementSegment(
                            ParseElementKind(reader),
                            reader.ParseVector(ParseFuncIdxInstructions),
                            new ElementMode.DeclarativeMode()),
                    ElementType.ActiveNoIndexWithElemType =>
                        new ElementSegment(
                            (TableIdx)0,
                            Expression.ParseInitializer(reader),
                            ReferenceType.Funcref,
                            reader.ParseVector(Expression.ParseInitializer)),
                    ElementType.PassiveWithElemType =>
                        new ElementSegment(
                            ReferenceTypeParser.Parse(reader),
                            reader.ParseVector(Expression.ParseInitializer),
                            new ElementMode.PassiveMode()),
                    ElementType.ActiveWithIndexAndElemType =>
                        new ElementSegment(
                            ParseTableIndex(reader),
                            Expression.ParseInitializer(reader),
                            ReferenceTypeParser.Parse(reader),
                            reader.ParseVector(Expression.ParseInitializer)),
                    ElementType.DeclarativeWithElemType =>
                        new ElementSegment(
                            ReferenceTypeParser.Parse(reader),
                            reader.ParseVector(Expression.ParseInitializer),
                            new ElementMode.DeclarativeMode()),
                    _ => throw new FormatException($"Invalid Element at {reader.BaseStream.Position}")
                };

            private bool IsAllRefFunc()
            {
                return Initializers
                    .Select(expr => expr.Instructions[0] as InstRefFunc)
                    .All(inst => inst != null);
            }

            /// <summary>
            /// 3.4.5. Element Segments
            /// </summary>
            public class Validator : AbstractValidator<ElementSegment>
            {
                public Validator()
                {
                    RuleForEach(es => es.Initializers)
                        .Custom((expr, ctx) =>
                        {
                            var es = ctx.InstanceToValidate;
                            var resultType = es.Type switch {
                                ReferenceType.Funcref => ValType.Funcref.SingleResult(),
                                ReferenceType.Externref => ValType.Externref.SingleResult(),
                                _ => throw new InvalidDataException($"Element Segment has invalid reference type:{es.Type}")
                            };
                            
                            // @Spec 3.4.5.1. base
                            var validationContext = ctx.GetValidationContext();
                            var exprValidator = new Expression.Validator(resultType, isConstant: true);
                            var subContext = validationContext.PushSubContext(expr);
                            var result = exprValidator.Validate(subContext);
                            foreach (var error in result.Errors)
                            {
                                ctx.AddFailure($"Expression.{error.PropertyName}", error.ErrorMessage);
                            }
                            validationContext.PopValidationContext();
                        });
                    RuleFor(es => es.Mode).SetInheritanceValidator(v =>
                    {
                        v.Add(new ElementMode.PassiveMode.Validator());
                        v.Add(new ElementMode.ActiveMode.Validator());
                        v.Add(new ElementMode.DeclarativeMode.Validator());
                    });
                }
            }
        }

        public abstract class ElementMode
        {
            public ReferenceType SegmentType;

            public class PassiveMode : ElementMode
            {
                // @Spec 3.4.5.2. passive
                public class Validator : AbstractValidator<PassiveMode>
                {
                    public Validator()
                    {
                        //Valid for all reference types
                        RuleFor(mode => mode.SegmentType)
                            .IsInEnum();
                    }
                }
            }

            public class ActiveMode : ElementMode
            {
                public ActiveMode(TableIdx idx, Expression offset) => (TableIndex, Offset) = (idx, offset);
                public TableIdx TableIndex { get; }
                public Expression Offset { get; }

                // @Spec 3.4.5.3 active
                public class Validator : AbstractValidator<ActiveMode>
                {
                    public Validator()
                    {
                        RuleFor(mode => mode.TableIndex)
                            .Must((_, idx, ctx) =>
                                ctx.GetValidationContext().Tables.Contains(idx));
                        RuleFor(mode => mode)
                            .Custom((mode, ctx) =>
                            {
                                var validationContext = ctx.GetValidationContext();
                                if (!validationContext.Tables.Contains(mode.TableIndex))
                                    throw new ValidationException(
                                        $"Table index {mode.TableIndex.Value} exceeds table size {validationContext.Tables.Count}");
                                
                                var tableType = validationContext.Tables[mode.TableIndex];
                                var exprValidator = new Expression.Validator(ValType.I32.SingleResult(), isConstant: true);
                                var subContext = validationContext.PushSubContext(mode.Offset);
                                var result = exprValidator.Validate(subContext);
                                foreach (var error in result.Errors)
                                {
                                    ctx.AddFailure($"Expression.{error.PropertyName}", error.ErrorMessage);
                                }
                                validationContext.PopValidationContext();
                                
                                if (mode.SegmentType != tableType.ElementType)
                                {
                                    ctx.AddFailure($"Active ElementMode {mode.SegmentType} is not valid for table type {tableType.ElementType}");                                    
                                }
                            });
                    }
                }
            }

            public class DeclarativeMode : ElementMode
            {
                // @Spec 3.4.5.4. declarative
                public class Validator : AbstractValidator<DeclarativeMode>
                {
                    public Validator()
                    {
                        //Valid for all reference types
                        RuleFor(mode => mode.SegmentType)
                            .IsInEnum();
                    }
                }
            }
        }
    }
    
    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.12 Element Section
        /// </summary>
        private static Module.ElementSegment[] ParseElementSection(BinaryReader reader) =>
            reader.ParseVector(Module.ElementSegment.Parse);
    }
}
using System;
using System.IO;
using System.Linq;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.7. Element Segments
        /// </summary>
        public ElementSegment[] Elements { get; internal set; } = null!;
    
        /// <summary>
        /// @Spec 2.5.7. Element Segments
        /// </summary>
        public class ElementSegment
        {
            public ReferenceType Type { get; internal set; }
            public Expression[] Initializers { get; internal set; }
            
            public ElementMode Mode { get; internal set; }

            private ElementSegment(ReferenceType type, IInstruction[] funcIndices, ElementMode mode)
            {
                Type = type;
                Initializers = funcIndices.Select(inst => new Expression(inst)).ToArray();
                Mode = mode;
            }

            private ElementSegment(ReferenceType type, Expression[] expressions, ElementMode mode)
            {
                Type = type;
                Initializers = expressions;
                Mode = mode;
            }

            private ElementSegment(TableIdx tableIndex, Expression e, ReferenceType type, IInstruction[] funcIndices)
            {
                Type = type;
                Initializers = funcIndices.Select(inst => new Expression(inst)).ToArray();
                Mode = new ElementMode.ActiveMode(tableIndex, e);
            }
            
            private ElementSegment(TableIdx tableIndex, Expression e, ReferenceType type, Expression[] expressions)
            {
                Type = type;
                Initializers = expressions;
                Mode = new ElementMode.ActiveMode(tableIndex, e);
            }
            

            /// <summary>
            /// Generate a InstRefFunc for a funcidx
            /// </summary>
            private static IInstruction ParseFuncIdxInstructions(BinaryReader reader) => 
                InstructionFactory.CreateInstruction(OpCode.RefFunc).Parse(reader);

            private static ReferenceType ParseElementKind(BinaryReader reader) =>
                reader.ReadByte() switch {
                    0x00 => ReferenceType.Funcref,
                    var b =>
                        throw new InvalidDataException($"Invalid ElementKind {b} at {reader.BaseStream.Position:X4}")
                };

            private static TableIdx ParseTableIndex(BinaryReader reader) =>
                (TableIdx)reader.ReadLeb128_u32();
                
            public static ElementSegment Parse(BinaryReader reader) =>
                (ElementType)reader.ReadLeb128_u32() switch {
                    ElementType.ActiveNoIndexWithElemKind => 
                        new ElementSegment(
                            (TableIdx)0,
                            Expression.Parse(reader),
                            ReferenceType.Funcref,
                            reader.ParseVector(ParseFuncIdxInstructions)),
                    ElementType.PassiveWithElemKind =>
                        new ElementSegment(
                            ParseElementKind(reader),
                            reader.ParseVector(ParseFuncIdxInstructions),
                            ElementMode.Passive),
                    ElementType.ActiveWithIndexAndElemKind =>
                        new ElementSegment(
                            ParseTableIndex(reader),
                            Expression.Parse(reader),
                            ParseElementKind(reader),
                            reader.ParseVector(ParseFuncIdxInstructions)),
                    ElementType.DeclarativeWithElemKind =>
                        new ElementSegment(
                            ParseElementKind(reader),
                            reader.ParseVector(ParseFuncIdxInstructions),
                            ElementMode.Declarative),
                    ElementType.ActiveNoIndexWithElemType =>
                        new ElementSegment(
                            (TableIdx)0,
                            Expression.Parse(reader),
                            ReferenceType.Funcref,
                            reader.ParseVector(Expression.Parse)),
                    ElementType.PassiveWithElemType =>
                        new ElementSegment(
                            ReferenceTypeParser.Parse(reader),
                            reader.ParseVector(Expression.Parse),
                            ElementMode.Passive),
                    ElementType.ActiveWithIndexAndElemType =>
                        new ElementSegment(
                            ParseTableIndex(reader),
                            Expression.Parse(reader),
                            ReferenceTypeParser.Parse(reader),
                            reader.ParseVector(Expression.Parse)),
                    ElementType.DeclarativeWithElemType =>
                        new ElementSegment(
                            ReferenceTypeParser.Parse(reader),
                            reader.ParseVector(Expression.Parse),
                            ElementMode.Declarative),
                    _ => throw new InvalidDataException($"Invalid Element at {reader.BaseStream.Position}")
                };
            
        }
        
        public abstract class ElementMode
        {
            public class PassiveMode : ElementMode {}
            public static PassiveMode Passive = new PassiveMode();

            public class ActiveMode : ElementMode
            {
                public TableIdx TableIndex { get; internal set; }
                public Expression Offset { get; internal set; }
                public ActiveMode(TableIdx idx, Expression offset) => (TableIndex, Offset) = (idx, offset);
            }
            
            public class DeclarativeMode : ElementMode {}
            public static DeclarativeMode Declarative = new DeclarativeMode();
        }
        
    }
    
    public static partial class ModuleParser
    {
        /// <summary>
        /// @Spec 5.5.12 Element Section
        /// </summary>
        private static Module.ElementSegment[] ParseElementSection(BinaryReader reader) =>
            reader.ParseVector(Module.ElementSegment.Parse);
    }
}
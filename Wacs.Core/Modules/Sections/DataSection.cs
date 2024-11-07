using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public partial class Module
    {
        public enum DataFlags
        {
            ActiveDefault = 0,
            Passive = 1,
            ActiveExplicit = 2
        }

        /// <summary>
        /// @Spec 2.5.8. Data Segments
        /// </summary>
        public Data[] Datas { get; internal set; } = Array.Empty<Data>();

        public uint DataCount { get; internal set; } = uint.MaxValue;

        /// <summary>
        /// @Spec 2.5.8. Data Segments
        /// </summary>
        public class Data : IRenderable
        {
            private Data(DataMode mode, (uint size, byte[] bytes) data)
            {
                Mode = mode;
                Size = data.size;
                Init = data.bytes;
                if (Size != Init.Length)
                    throw new FormatException($"Data segment size {Size} differs from bytes provided {Init.Length}");
            }

            public DataMode Mode { get; }
            public uint Size { get; }
            public byte[] Init { get; }

            public string Id { get; set; } = "";

            public void RenderText(StreamWriter writer, Module module, string indent)
            {
                var id = string.IsNullOrWhiteSpace(Id) ? "" : $" (;{Id};)";
                var datastring = BytesEncoder.EncodeToWatString(Init);
                var data = Mode switch
                {
                    DataMode.PassiveMode => datastring,
                    DataMode.ActiveMode am when am.MemoryIndex is { Value: 0 } && am.Offset.Instructions.IsConstant(null) => $"{am.Offset.ToWat()} {datastring}",
                    DataMode.ActiveMode am => $" (memory {am.MemoryIndex.Value}) (offset{am.Offset.ToWat()}) {datastring}",
                    _ => throw new InvalidDataException($"Unkown datamode: {Mode}")
                };
                var dataText = $"{indent}(data{id}{data})";
            
                writer.Write(dataText);
            }

            private static (uint size, byte[] bytes) ParseByteVector(BinaryReader reader)
            {
                var size = reader.ReadLeb128_u32();
                var bytes = reader.ReadBytes((int)size);
                return (size, bytes);
            }

            public static Data Parse(BinaryReader reader) =>
                (DataFlags)reader.ReadLeb128_u32() switch
                {
                    DataFlags.ActiveDefault => 
                        new Data(new DataMode.ActiveMode((MemIdx)0, Expression.Parse(reader)), ParseByteVector(reader)),
                    DataFlags.ActiveExplicit => 
                        new Data(new DataMode.ActiveMode((MemIdx)reader.ReadLeb128_u32(), Expression.Parse(reader)), ParseByteVector(reader)),
                    DataFlags.Passive => 
                        new Data(DataMode.Passive, ParseByteVector(reader)),
                    _ => throw new FormatException($"Malformed Data section at {reader.BaseStream.Position}")
                };

            /// <summary>
            /// @Spec 3.4.6. Data Segments
            /// </summary>
            public class Validator : AbstractValidator<Data>
            {
                public Validator()
                {
                    RuleFor(d => d.Mode).SetInheritanceValidator(v =>
                    {
                        v.Add(new DataMode.PassiveMode.Validator());
                        v.Add(new DataMode.ActiveMode.Validator());
                    });
                }
            }
        }

        public abstract class DataMode
        {
            public static readonly PassiveMode Passive = new();

            public class PassiveMode : DataMode
            {
                /// <summary>
                /// @Spec 3.4.6.2. passive
                /// </summary>
                public class Validator : AbstractValidator<PassiveMode> {}
            }

            public class ActiveMode : DataMode
            {
                public ActiveMode(MemIdx index, Expression offset) => (MemoryIndex, Offset) = (index, offset);
                public MemIdx MemoryIndex { get; }
                public Expression Offset { get; }

                /// <summary>
                /// @Spec 3.4.6.3. active
                /// </summary>
                public class Validator : AbstractValidator<ActiveMode>
                {
                    public Validator()
                    {
                        RuleFor(mode => mode.MemoryIndex)
                            .Must((_, idx, ctx) => ctx.GetValidationContext().Mems.Contains(idx));
                        RuleFor(mode => mode.Offset)
                            .Custom((expr, ctx) =>
                            {
                                var exprValidator = new Expression.Validator(ValType.I32.SingleResult(), isConstant: true);
                                var validationContext = ctx.GetValidationContext();
                                var subContext = validationContext.PushSubContext(expr);
                                var result = exprValidator.Validate(subContext);
                                foreach (var error in result.Errors)
                                {
                                    ctx.AddFailure($"Expression.{error.PropertyName}", error.ErrorMessage);
                                }
                                validationContext.PopValidationContext();
                            });
                    }
                }
            }
        }
    }

    public static partial class BinaryModuleParser
    {
        /// <summary>
        /// @Spec 5.5.14 Data Section
        /// </summary>
        private static Module.Data[] ParseDataSection(BinaryReader reader) =>
            reader.ParseVector(Module.Data.Parse);

        /// <summary>
        /// @Spec 2.5.15. Data Count Section
        /// </summary>
        private static uint ParseDataCountSection(BinaryReader reader) =>
            reader.ReadLeb128_u32();
    }
}
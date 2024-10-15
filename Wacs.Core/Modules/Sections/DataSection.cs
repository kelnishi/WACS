using System.IO;
using FluentValidation;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    public partial class Module
    {
        /// <summary>
        /// @Spec 2.5.8. Data Segments
        /// </summary>
        public Data[] Datas { get; internal set; } = null!;

        public uint DataCount { get; internal set; } = 0;
        
        /// <summary>
        /// @Spec 2.5.8. Data Segments
        /// </summary>
        public class Data
        {
            public DataMode Mode { get; set; }
            public uint Size { get; set; }
            public byte[] Init { get; set; }

            private Data(DataMode mode, (uint size, byte[] bytes) data) =>
                (Mode, Size, Init) = (mode, data.size, data.bytes);

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
                    _ => throw new InvalidDataException($"Malformed Data section at {reader.BaseStream.Position}")
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
            public static PassiveMode Passive = new PassiveMode();

            public class PassiveMode : DataMode
            {
                /// <summary>
                /// @Spec 3.4.6.2. passive
                /// </summary>
                public class Validator : AbstractValidator<PassiveMode> { }
            }

            public class ActiveMode : DataMode
            {
                public MemIdx MemoryIndex { get; set; }
                public Expression Offset { get; set; }

                public ActiveMode(MemIdx index, Expression offset) => (MemoryIndex, Offset) = (index, offset);
                
                /// <summary>
                /// @Spec 3.4.6.3. active
                /// </summary>
                public class Validator : AbstractValidator<ActiveMode>
                {
                    public Validator()
                    {
                        RuleFor(mode => mode.MemoryIndex)
                            .Must((mode, idx, ctx) => ctx.GetExecContext().Mems.Contains(idx));
                        RuleFor(mode => mode.Offset)
                            .Custom((expr, ctx) =>
                            {
                                var exprValidator = new Expression.Validator(ValType.I32.SingleResult(), isConstant: true);
                                var subContext = ctx.GetSubContext(expr);
                                var result = exprValidator.Validate(subContext);
                                foreach (var error in result.Errors)
                                {
                                    ctx.AddFailure($"Expression.{error.PropertyName}", error.ErrorMessage);
                                }
                            });
                    }
                }
            }
        }

        public enum DataFlags
        {
            ActiveDefault = 0,
            Passive = 1,
            ActiveExplicit = 2
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
using System.IO;
using FluentValidation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.10. Global Types
    /// Represents the type of a global variable in WebAssembly.
    /// </summary>
    public class GlobalType
    {
        /// <summary>
        /// The mutability of the global variable (immutable or mutable).
        /// </summary>
        public Mutability Mutability { get; set; }
        
        /// <summary>
        /// The value type of the global variable.
        /// </summary>
        public ValType ContentType { get; set; }

        private GlobalType(ValType valtype, Mutability mut) =>
            (ContentType, Mutability) = (valtype, mut);

        /// <summary>
        /// @Spec 5.3.10. Global Types
        /// </summary>
        public static GlobalType Parse(BinaryReader reader) => 
            new GlobalType(
                valtype: ValueTypeParser.Parse(reader),
                mut: MutabilityParser.Parse(reader)
            );

        /// <summary>
        /// @Spec 3.2.6. Global Types
        /// </summary>
        public class Validator : AbstractValidator<GlobalType>
        {
            public Validator() {
                // @Spec 3.2.6.1. mut valtype
                RuleFor(gt => gt.Mutability).IsInEnum();
                RuleFor(gt => gt.ContentType).IsInEnum();
            }
        }
            
    }

    /// <summary>
    /// Specifies the mutability of a global variable.
    /// </summary>
    public enum Mutability : byte
    {
        Immutable = 0x00,
        Mutable = 0x01
    }
    
    public static class MutabilityParser
    {
        public static Mutability Parse(BinaryReader reader) =>
            (Mutability)reader.ReadByte() switch
            {
                Mutability.Immutable => Mutability.Immutable, //const
                Mutability.Mutable => Mutability.Mutable,     //var
                var flag => throw new InvalidDataException($"Invalid Mutability flag {flag} at offset {reader.BaseStream.Position}.")
            };
    }

}
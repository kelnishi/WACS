using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.6 Function Types
    /// Represents the type signature of a WebAssembly function, including parameter and return types.
    /// </summary>
    public class FunctionType
    {
        /// <summary>
        /// The vec of parameter types for the function.
        /// </summary>
        public ResultType ParameterTypes { get; internal set; }

        /// <summary>
        /// The vec of return types for the function.
        /// </summary>
        public ResultType ResultType { get; internal set; }

        public string ToNotation() =>
            $"{ParameterTypes.ToNotation()} --> {ResultType.ToNotation()}";

        public FunctionType(ResultType parameterTypes, ResultType resultType) =>
            (ParameterTypes, ResultType) = (parameterTypes, resultType);

        /// <summary>
        /// @Spec 5.3.6. Function Types
        /// </summary>
        public static FunctionType Parse(BinaryReader reader) =>
            reader.ReadByte() switch {
                0x60 => new FunctionType(ResultType.Parse(reader), ResultType.Parse(reader)),
                var form => throw new InvalidDataException(
                    $"Invalid function type form {form} at offset {reader.BaseStream.Position}.")
            };
        
        /// <summary>
        /// 3.2.3. Function Types
        /// Always valid
        /// </summary>
        public class Validator : AbstractValidator<FunctionType> { }
    }
}
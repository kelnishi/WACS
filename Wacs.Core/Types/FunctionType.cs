using System.IO;
using FluentValidation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.6 Function Types
    /// Represents the type signature of a WebAssembly function, including parameter and return types.
    /// </summary>
    public class FunctionType
    {
        public static readonly FunctionType Empty = new(ResultType.Empty, ResultType.Empty);

        public FunctionType(ResultType parameterTypes, ResultType resultType) =>
            (ParameterTypes, ResultType) = (parameterTypes, resultType);

        /// <summary>
        /// The vec of parameter types for the function.
        /// </summary>
        public ResultType ParameterTypes { get; }

        /// <summary>
        /// The vec of return types for the function.
        /// </summary>
        public ResultType ResultType { get; }

        public bool Matches(FunctionType other) =>
            ParameterTypes.Matches(other.ParameterTypes) &&
            ResultType.Matches(other.ResultType);

        public string ToNotation() =>
            $"{ParameterTypes.ToNotation()} -> {ResultType.ToNotation()}";

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
        public class Validator : AbstractValidator<FunctionType> {}
    }
}
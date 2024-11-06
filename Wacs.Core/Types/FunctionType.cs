using System;
using System.IO;
using FluentValidation;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.6 Function Types
    /// Represents the type signature of a WebAssembly function, including parameter and return types.
    /// </summary>
    public class FunctionType : IRenderable
    {
        public static readonly FunctionType Empty = new(ResultType.Empty, ResultType.Empty);
        public static readonly FunctionType SingleI32 = new(ResultType.Empty, new ResultType(ValType.I32));
        public static readonly FunctionType SingleI64 = new(ResultType.Empty, new ResultType(ValType.I64));
        public static readonly FunctionType SingleF32 = new(ResultType.Empty, new ResultType(ValType.F32));
        public static readonly FunctionType SingleF64 = new(ResultType.Empty, new ResultType(ValType.F64));
        public static readonly FunctionType SingleV128 = new(ResultType.Empty, new ResultType(ValType.V128));
        public static readonly FunctionType SingleFuncref = new(ResultType.Empty, new ResultType(ValType.Funcref));
        public static readonly FunctionType SingleExternref = new(ResultType.Empty, new ResultType(ValType.Externref));

        public FunctionType(ResultType parameterTypes, ResultType resultType) =>
            (ParameterTypes, ResultType) = (parameterTypes, resultType);

        /// <summary>
        /// For rendering/debugging
        /// </summary>
        public string Id { get; set; } = "";

        /// <summary>
        /// The vec of parameter types for the function.
        /// </summary>
        public ResultType ParameterTypes { get; }

        /// <summary>
        /// The vec of return types for the function.
        /// </summary>
        public ResultType ResultType { get; }

        public void RenderText(StreamWriter writer, Module module, string indent)
        {
            var symbol = string.IsNullOrWhiteSpace(Id) ? "" : $" (;{Id};)";
            var parameters = ParameterTypes.ToParameters();
            var results = ResultType.ToResults();
            var func = $" (func{parameters}{results})";
            
            writer.WriteLine($"{indent}(type{symbol}{func})");
        }

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
                var form => throw new FormatException(
                    $"Invalid function type form {form} at offset {reader.BaseStream.Position}.")
            };

        /// <summary>
        /// 3.2.3. Function Types
        /// Always valid
        /// </summary>
        public class Validator : AbstractValidator<FunctionType> {}
    }
}
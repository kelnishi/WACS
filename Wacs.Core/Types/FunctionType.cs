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
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public static readonly FunctionType SingleFuncref = new(ResultType.Empty, new ResultType(ValType.Func));
        public static readonly FunctionType SingleExternref = new(ResultType.Empty, new ResultType(ValType.Extern));

        /// <summary>
        /// The vec of parameter types for the function.
        /// </summary>
        public readonly ResultType ParameterTypes;

        /// <summary>
        /// The vec of return types for the function.
        /// </summary>
        public readonly ResultType ResultType;

        public FunctionType(ResultType parameterTypes, ResultType resultType) =>
            (ParameterTypes, ResultType) = (parameterTypes, resultType);

        /// <summary>
        /// For rendering/debugging
        /// </summary>
        public string Id { get; set; } = "";

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

        /// <summary>
        /// Determine if the stack deltas are equivalent.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equivalent(FunctionType? other)
        {
            if (other == null)
                return false;
            
            var myDelta = GetDelta();
            var otDelta = other.GetDelta();
            return myDelta.parameters.SequenceEqual(otDelta.parameters)
                   && myDelta.results.SequenceEqual(otDelta.results);
        }

        public (ValType[] parameters, ValType[] results) GetDelta()
        {
            var stackIn = new Stack<ValType>();
            var stackOut = new Stack<ValType>();
            foreach (var v in ParameterTypes.Types) stackIn.Push(v);
            foreach (var v in ResultType.Types) stackOut.Push(v);
            while (stackIn.Count > 0 && stackOut.Count > 0 && stackIn.Peek() == stackOut.Peek())
            {
                stackIn.Pop();
                stackOut.Pop();
            }

            return (stackIn.ToArray(), stackOut.ToArray());
        }

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

        public override string ToString() =>
            $"FunctionType({ToNotation()})";

        /// <summary>
        /// 3.2.3. Function Types
        /// Always valid
        /// </summary>
        public class Validator : AbstractValidator<FunctionType> {}
    }
}
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
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.10. Global Types
    /// Represents the type of a global variable in WebAssembly.
    /// </summary>
    public class GlobalType
    {
        /// <summary>
        /// The value type of the global variable.
        /// </summary>
        public readonly ValType ContentType;

        /// <summary>
        /// The mutability of the global variable (immutable or mutable).
        /// </summary>
        public readonly Mutability Mutability;

        public GlobalType(ValType valtype, Mutability mut) =>
            (ContentType, Mutability) = (valtype, mut);

        public ResultType ResultType => new(ContentType);

        public override string ToString() =>
            $"GlobalType({(Mutability == Mutability.Immutable ? "const" : "var")} {ContentType})";

        public override bool Equals(object obj) =>
            obj is GlobalType other &&
            ContentType == other.ContentType &&
            Mutability == other.Mutability;

        public override int GetHashCode() =>
            HashCode.Combine(ContentType, Mutability);

        public static bool operator ==(GlobalType left, GlobalType right) =>
            Equals(left, right);

        public static bool operator !=(GlobalType left, GlobalType right) =>
            !Equals(left, right);


        /// <summary>
        /// @Spec 5.3.10. Global Types
        /// </summary>
        public static GlobalType Parse(BinaryReader reader) => 
            new(
                valtype: ValTypeParser.Parse(reader),
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
                var flag => throw new FormatException($"Invalid Mutability flag {flag} at offset {reader.BaseStream.Position}.")
            };
    }

}
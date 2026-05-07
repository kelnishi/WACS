// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using FluentValidation;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

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

        /// <summary>
        /// Shared-everything-threads (Layer 5a): when true, this global is
        /// shared across host threads and must be accessed via the
        /// <c>global.atomic.*</c> opcodes (Layer 5d). Parsed from bit 1 of
        /// the global-flags byte. Validation rejects when the runtime has
        /// not opted into the shared-everything-threads proposal subset.
        /// </summary>
        public readonly bool Shared;

        /// <summary>
        /// Shared-everything-threads (Layer 5c): when true, each host
        /// thread sees its own copy of this global, initialized from the
        /// module's declared initializer on first access. Parsed from
        /// bit 2 of the global-flags byte. Mutually exclusive with
        /// <see cref="Shared"/>.
        /// </summary>
        public readonly bool ThreadLocal;

        public GlobalType(ValType valtype, Mutability mut, bool shared = false, bool threadLocal = false) =>
            (ContentType, Mutability, Shared, ThreadLocal) = (valtype, mut, shared, threadLocal);

        public ResultType ResultType => new(ContentType);

        public override string ToString()
        {
            var mut = Mutability == Mutability.Immutable ? "const" : "var";
            var qual = Shared ? "shared " : ThreadLocal ? "thread_local " : "";
            return $"GlobalType({qual}{mut} {ContentType})";
        }

        public override bool Equals(object obj) =>
            obj is GlobalType other &&
            ContentType == other.ContentType &&
            Mutability == other.Mutability &&
            Shared == other.Shared &&
            ThreadLocal == other.ThreadLocal;

        public override int GetHashCode() =>
            HashCode.Combine(ContentType, Mutability, Shared, ThreadLocal);

        public static bool operator ==(GlobalType left, GlobalType right) =>
            Equals(left, right);

        public static bool operator !=(GlobalType left, GlobalType right) =>
            !Equals(left, right);


        /// <summary>
        /// @Spec 5.3.10. Global Types
        /// Baseline: single mutability byte (0=const, 1=mut).
        /// Shared-everything-threads extension (Layer 5):
        /// bit 1 = shared, bit 2 = thread_local. Feature-flag check
        /// happens at module-allocation time in WasmRuntimeInstantiation
        /// — parsing accepts the raw bits so the binary can round-trip
        /// cleanly.
        /// </summary>
        public static GlobalType Parse(BinaryReader reader) =>
            new(
                valtype: ValTypeParser.Parse(reader),
                mut: MutabilityParser.Parse(reader, out var shared, out var threadLocal),
                shared: shared,
                threadLocal: threadLocal
            );

        public bool Matches(GlobalType other, TypesSpace? types)
        {
            if (Mutability != other.Mutability)
                return false;
            // Shared / thread-local must match exactly — a host providing a
            // plain global can't satisfy a shared import (the synchronization
            // discipline differs), and vice versa.
            if (Shared != other.Shared)
                return false;
            if (ThreadLocal != other.ThreadLocal)
                return false;
            if (!ContentType.Matches(other.ContentType, types))
                return false;
            if (Mutability == Mutability.Mutable && !other.ContentType.Matches(ContentType, types))
                return false;
            return true;
        }

        /// <summary>
        /// @Spec 3.2.6. Global Types
        /// </summary>
        public class Validator : AbstractValidator<GlobalType>
        {
            public Validator() {
                // @Spec 3.2.6.1. mut valtype
                RuleFor(gt => gt.Mutability).IsInEnum();
                RuleFor(gt => gt.ContentType)
                    .Must((gtype, vtype, ctx) => vtype.Validate(ctx.GetValidationContext().Types))
                    .WithMessage(gt => $"GlobalType had invalid ContentType {gt.ContentType}");
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
        /// <summary>
        /// Baseline-compatible parse: accepts only the bare 0/1 byte. Kept
        /// for callers that don't participate in the shared-everything
        /// extension.
        /// </summary>
        public static Mutability Parse(BinaryReader reader) =>
            (Mutability)reader.ReadByte() switch
            {
                Mutability.Immutable => Mutability.Immutable, //const
                Mutability.Mutable => Mutability.Mutable,     //var
                var flag => throw new FormatException($"Invalid Mutability flag {flag} at offset {reader.BaseStream.Position}.")
            };

        /// <summary>
        /// Shared-everything-aware parse. Decodes bit 0 as mutability,
        /// bit 1 as <c>shared</c>, bit 2 as <c>thread_local</c>. Rejects
        /// any other bits as malformed. Feature-flag enforcement (reject
        /// shared/thread_local when the runtime has not opted in) happens
        /// downstream at module-allocation time; parsing is permissive so
        /// the binary round-trips cleanly.
        /// </summary>
        public static Mutability Parse(BinaryReader reader, out bool shared, out bool threadLocal)
        {
            byte flags = reader.ReadByte();
            if ((flags & ~0x07) != 0)
                throw new FormatException($"Invalid global flags 0x{flags:X2} at offset {reader.BaseStream.Position}.");

            shared = (flags & 0x02) != 0;
            threadLocal = (flags & 0x04) != 0;

            if (shared && threadLocal)
                throw new FormatException($"Global cannot be both shared and thread_local (flags 0x{flags:X2}) at offset {reader.BaseStream.Position}.");

            return (flags & 0x01) != 0 ? Mutability.Mutable : Mutability.Immutable;
        }
    }

}
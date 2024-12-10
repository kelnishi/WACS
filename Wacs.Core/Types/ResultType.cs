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
using Wacs.Core.Attributes;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types
{
    /// <summary>
    /// @Spec 2.3.5 Result Types
    /// </summary>
    public class ResultType
    {
        public static readonly ResultType Empty = new();
        public readonly int Arity;

        public readonly ValType[] Types;

        private ResultType()
        {
            Types = Array.Empty<ValType>();
            Arity = 0;
        }

        public ResultType(ValType single)
        {
            Types = new[] { single };
            Arity = Types.Length;
        }

        public ResultType(ValType[] types)
        {
            Types = types;
            Arity = Types.Length;
        }

        public ResultType(Type[] sysTypes)
        {
            Types = new ValType[sysTypes.Length];
            Arity = sysTypes.Length;

            for (int i = 0; i < sysTypes.Length; ++i)
            {
                Types[i] = sysTypes[i].ToValType();
            }
        }

        private ResultType(BinaryReader reader)
        {
            Types = reader.ParseVector(ValTypeParser.Parse);
            Arity = Types.Length;
        }

        public string ToNotation() => $"[{string.Join(" ",Types.Select(t=>t.ToNotation()))}]";
        public string ToTypes() => string.Join("", Types.Select(t => $" {t.ToNotation()}"));
        public string ToParameters() => Types.Length == 0 ? "" : $" (param{ToTypes()})";
        public string ToResults() => Types.Length == 0 ? "" : $" (result{ToTypes()})";


        public bool Matches(ResultType other, TypesSpace? types)
        {
            if (Types.Length != other.Types.Length)
                return false;
            for (int i = 0, l = Types.Length; i < l; ++i)
            {
                if (!Types[i].Matches(other.Types[i], types))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// @Spec 5.3.5 Result Types
        /// </summary>
        public static ResultType Parse(BinaryReader reader) => new(reader);

        public object ComputeHash(int defIndexValue, List<DefType> defs)
        {
            var hash = new StableHash();
            hash.Add(nameof(ResultType));
            foreach (var type in Types)
            {
                hash.Add(type.ComputeHash(defIndexValue, defs));
            }
            return hash.ToHashCode();
        }
    }
}
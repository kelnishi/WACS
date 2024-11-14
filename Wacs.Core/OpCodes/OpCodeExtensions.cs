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
using System.Collections.Concurrent;
using Wacs.Core.Attributes;

// ReSharper disable InconsistentNaming
namespace Wacs.Core.OpCodes
{
    public static class OpCodeExtensions
    {
        private static readonly ConcurrentDictionary<OpCode, string> MnemonicCache00 = new();
        private static readonly ConcurrentDictionary<GcCode, string> MnemonicCacheFB = new();
        private static readonly ConcurrentDictionary<ExtCode, string> MnemonicCacheFC = new();
        private static readonly ConcurrentDictionary<SimdCode, string> MnemonicCacheFD = new();
        private static readonly ConcurrentDictionary<AtomCode, string> MnemonicCacheFE = new();

        /// <summary>
        /// Retrieves the WAT mnemonic associated with the given opcode.
        /// </summary>
        /// <param name="opcode">The opcode for which to retrieve the mnemonic.</param>
        /// <returns>The WAT mnemonic if available; otherwise, null.</returns>
        public static string GetMnemonic(this ByteCode opcode) =>
            opcode.x00 switch {
                OpCode.FB => opcode.xFB.GetMnemonic(),
                OpCode.FC => opcode.xFC.GetMnemonic(),
                OpCode.FD => opcode.xFD.GetMnemonic(),
                OpCode.FE => opcode.xFE.GetMnemonic(),
                _ => opcode.x00.GetMnemonic()
            };

        private static string LookUp<T>(T op)
        where T : Enum
        {
            var type = typeof(T);
            var memberInfo = type.GetMember(op.ToString());
            if (memberInfo.Length <= 0)
                return $"undefined {op}";

            var attributes = memberInfo[0].GetCustomAttributes(typeof(OpCodeAttribute), false);
            if (attributes.Length > 0)
            {
                return ((OpCodeAttribute)attributes[0]).Mnemonic;
            }

            return $"undefined {op}";
        }

        private static string GetOpCode<T>(T opcode, ConcurrentDictionary<T, string> cache)
        where T : Enum
        {
            if (cache.TryGetValue(opcode, out var result))
                return result;
            var newResult = LookUp(opcode);
            cache.TryAdd(opcode, newResult);
            return newResult;
        }

        public static string GetMnemonic(this OpCode opcode) => GetOpCode(opcode, MnemonicCache00);
        public static string GetMnemonic(this GcCode opcode) => GetOpCode(opcode, MnemonicCacheFB);
        public static string GetMnemonic(this ExtCode opcode) => GetOpCode(opcode, MnemonicCacheFC);
        public static string GetMnemonic(this SimdCode opcode) => GetOpCode(opcode, MnemonicCacheFD);
        public static string GetMnemonic(this AtomCode opcode) => GetOpCode(opcode, MnemonicCacheFE);
    }
}
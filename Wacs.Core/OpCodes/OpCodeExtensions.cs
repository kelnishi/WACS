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